using System.Collections.Generic;
using UnityEngine;

// 自然洞窟(2026-09-21) - 洞窟ステージ固有の「天井・天井の針・たいまつ」。
// TerrainManager(地面/穴/上下ルート)には手を入れず、そのステージ判定
// (ApplyStageTheme)でこのコンポーネントを有効化するだけの分離構成。有効化
// されていない間(荒野街道/天空回廊)は一切生成せず、問い合わせにもnullを返す。
//
// 設計:
//  - 天井は「地面ライン(穴を無視した地面の高さ)+クリアランス」を一定間隔
//    (nodeSpacing)のノードで表した折れ線。プレイヤーの移動は物理ではなく数式
//    駆動なので、当たりも数式で行う(PlayerController.Moveから問い合わせ)。
//  - 通常天井はダメージ無し(頭がぶつかったら上昇を止めるだけ)。針(スパイク)は
//    三角形の先端判定で、既存のTakeDamage(無敵時間+安全地点復帰)を使う
//    ため連続ヒットや挟まりは起きない。
//  - 低い天井は「穴の近く・上下ルート分岐の近く」には置かない(ノード単位で
//    降格)。針は穴/分岐の近くと、低天井の区間には置かない。
//  - 天井の見た目はノード列から直接メッシュを作るので、勾配が変わる継ぎ目に
//    隙間や重なりが出ない。
public partial class CaveStage : MonoBehaviour
{
    [Header("Ceiling (調整用)")]
    public float nodeSpacing = 4f;
    // 通常区間: 空中コンボ/2段ジャンプに十分な高さ(地面ラインからの高さ)。
    public float normalClearance = 7.5f;
    public float normalClearanceJitter = 0.7f;
    // 針の区間: 通常より少し低い。1段ジャンプ(頭頂約3.0)は針先に届かず、
    // 2段ジャンプ/上攻撃の空中コンボで針に触れる高さにしてある。
    public float spikeSectionClearance = 6.2f;
    // 低天井: 立って走れる+1段ジャンプはできるが2段ジャンプは天井に当たる高さ。
    public float lowClearance = 4.4f;
    [Range(0f, 1f)] public float lowSectionChance = 0.22f;
    [Range(0f, 1f)] public float spikeSectionChance = 0.32f;
    // 共通Encounter System(2026-09-27) - 一本道化に合わせて「天井の高い広い空洞」区間を追加(mode 3)。
    // 空中の敵(コウモリ)を置ける/視界が開ける区間。低天井・針区間とは別に抽選する。
    public float highClearance = 10.2f;
    [Range(0f, 1f)] public float highSectionChance = 0.18f;
    public float sectionLengthMin = 32f;
    public float sectionLengthMax = 72f;
    // 低天井を置かない範囲(穴/上下ルート分岐からの距離)。
    public float lowCeilingPitMargin = 10f;
    public float lowCeilingBranchMargin = 9f;

    [Header("Passage (通路の最低空間 / 調整用)")]
    // 床(上ルートの床も含む)から天井までの最低間隔 = プレイヤーの頭の高さ + この余裕。
    // 針のある所は「針の先端まで」で測る。通常/針/低天井のどの区間・地形の継ぎ目でも下回らない。
    public float passageMargin = 1.6f;
    // 上ルートの床から天井までを、下ルートより何ユニット詰めてよいか(洞窟が極端に高くなりすぎないように)。
    public float upperRouteClearanceReduction = 1.0f;
    // 天井の各ノードで、床の高さを調べる範囲(ノード間隔の何倍を両側に見るか)。坂・段差・接続部の取りこぼし防止。
    public float floorSampleStep = 1f;
    public float MinPassageHeight => playerHeadHeight + passageMargin;

    [Header("Spikes (調整用)")]
    [Range(0f, 1f)] public float spikeClusterChancePerNode = 0.7f;
    public int spikeClusterMax = 3;
    public float spikeSpacing = 1.5f;
    public float spikeLengthMin = 1.5f;
    public float spikeLengthMax = 2.2f;
    public float spikeWidthMin = 1.1f;
    public float spikeWidthMax = 2.0f;
    public float spikePitMargin = 8f;
    public float spikeBranchMargin = 6f;
    // 針の当たり判定を見た目より少し小さくする横方向の余裕(プレイヤー半幅に対する減算)。
    public float spikeHitLeniency = 0.2f;
    public float playerHalfWidth = 0.5f;

    [Header("Player vs Ceiling")]
    // プレイヤーの頭頂(Root位置=足元からの高さ。BoxCollider2Dの上端=1.0に合わせる)。
    public float playerHeadHeight = 1.0f;

    [Header("Torches (調整用)")]
    public float torchSpacingMin = 26f;
    public float torchSpacingMax = 52f;
    public float torchLightHeight = 2.2f;
    public float torchLightRadius = 11f;
    public float torchHeight = 2.4f;

    [Header("Generation")]
    public float generateAhead = 44f;
    // 高速時の自動操作補助(2026-09-28): 先読みに必要な距離。generateAhead(EncounterDirectorが書き換える)とは
    // 別に持ち、大きい方まで天井/針を作る。
    [System.NonSerialized] public float assistGenerateAhead;
    public float ceilingVisualDrop = 0.2f;
    public float bandHeight = 1.8f;
    public float fillHeight = 60f;
    public float fillTileWorld = 4.3f;
    public int meshNodeGroup = 2;
    // 洞窟中は地形を先読みする距離を広げる(穴/分岐の先読み判定 + 画面右端より先まで天井を用意するため)。
    public float terrainGenerateAhead = 64f;
    float savedTerrainAhead = -1f;
    // 洞窟中は地面の断面(GroundFill)を深くして、縦画面でも画面下端まで岩で埋める。
    public float terrainFillDepth = 48f;
    float savedFillDepth = -1f;
    int testMode; // Editor only: 1 = spikes only, 2 = low ceiling only

    [Header("Art (SceneBuilderが設定)")]
    public Texture2D ceilingBandTexture;
    public float ceilingBandAspect = 1f; // 幅/高さ
    public Texture2D ceilingFillTexture;
    public Sprite[] spikeSprites;
    public Sprite torchSprite;

    // ===== ステージ別の見た目/区間の差し替え(LAST CORRIDOR、2026-09-29) =====
    // 自然洞窟はStyle未指定(use=false)のまま=従来と全く同じ。
    // LAST CORRIDORは天井を「古代回廊の天井」の絵に、針を「古代の杭」に、たいまつを「光る灯柱」に差し替え、
    // 暗闇(CaveLighting)を使わない。区間の選び方(sectionPicker)も距離の段階ごとに変える(天井が抜けた区間=mode 4)。
    [System.Serializable]
    public class Style
    {
        public bool use;
        public Texture2D ceilingBandTexture;
        public float ceilingBandAspect = 1f;
        public float bandHeight = 1.8f;
        public float ceilingVisualDrop = 0.2f;
        public Texture2D ceilingFillTexture;
        public float fillTileWorld = 4.3f;
        public Sprite[] spikeSprites;
        public Sprite torchSprite;
        public float torchHeight = 2.4f;
        public bool useLighting = true;
        public Sprite brokenEdgeSprite; // 天井が抜ける所の切れ端(無ければ針の絵で代用)
    }
    Style defaultStyle;
    bool styleLighting = true;
    Sprite brokenEdgeSprite;

    public void ApplyStyle(Style st)
    {
        if (defaultStyle == null)
            defaultStyle = new Style { use = true, ceilingBandTexture = ceilingBandTexture, ceilingBandAspect = ceilingBandAspect, bandHeight = bandHeight, ceilingVisualDrop = ceilingVisualDrop,
                ceilingFillTexture = ceilingFillTexture, fillTileWorld = fillTileWorld, spikeSprites = spikeSprites, torchSprite = torchSprite,
                torchHeight = torchHeight, useLighting = true, brokenEdgeSprite = null };
        Style s = st != null && st.use ? st : defaultStyle;
        ceilingBandTexture = s.ceilingBandTexture; ceilingBandAspect = s.ceilingBandAspect; bandHeight = s.bandHeight; ceilingVisualDrop = s.ceilingVisualDrop;
        ceilingFillTexture = s.ceilingFillTexture; fillTileWorld = s.fillTileWorld;
        spikeSprites = s.spikeSprites; torchSprite = s.torchSprite; torchHeight = s.torchHeight;
        styleLighting = s.useLighting; brokenEdgeSprite = s.brokenEdgeSprite;
    }

    // 天井が抜けた区間(mode 4)の当たりの高さ(地面ラインから)。見た目の天井は描かない。
    public float openClearance = 40f;
    // 区間の選び方の差し替え(論理X, 0〜1の乱数 → mode)。nullなら従来の抽選。乱数の消費は従来と同じ1回。
    [System.NonSerialized] public System.Func<float, float, int> sectionPicker;
    // 暗闇の演出を使っているか(死神の描画順などが参照)。
    public bool DarknessActive => Active && styleLighting && lighting != null;

    public bool Active { get; private set; }
    // 共通Encounter System(2026-09-27) - 天井を生成済みの右端(この先はまだ天井の高さが決まっていない)。
    public float GeneratedEndX => nodes.Count > 0 ? nodeBaseX + (nodes.Count - 1) * nodeSpacing : float.NegativeInfinity;
    public static int SpikeHitCount; // debug counter (Editor auto test)

    class Node { public float x, y; public int mode; } // mode: 0 normal, 1 spike, 2 low, 3 high(広い空洞), 4 open(天井が抜けている)
    struct Spike { public float x, topY, len, hw; }
    public struct Torch { public Vector2 lightPos; public float phase; }

    readonly List<Node> nodes = new List<Node>();
    readonly List<Spike> spikes = new List<Spike>();
    readonly List<Torch> torches = new List<Torch>();
    readonly List<GameObject> spawned = new List<GameObject>();
    int builtNodeIndex;
    int sectionMode;
    float sectionEndX;
    float nextTorchX;
    float spikeClusterBlockedUntilX;
    // Floating Origin: ノード列の先頭(nodes[0])のX。以前は「index*nodeSpacing」で絶対Xと結び付けていたが、
    // 座標を戻す/古いノードを捨てるので、先頭Xを別に持つ。
    float nodeBaseX;
    public float keepBehindDistance = 120f; // プレイヤーよりこれ以上後ろの天井/針/たいまつ/メッシュは破棄

    // 自然洞窟ボス拡張(2026-09-22) - ボス遭遇区間だけ、最低限の戦闘可能
    // スペース(通常天井相当の高さ・針なし)を保証するための一時的な範囲。
    // マップ全体の生成システム自体は変更せず、AddNode/AddSpikeの判定に
    // このチェックを1つ足すだけ。範囲外(通常の生成)には一切影響しない。
    // 既にノードが確定してしまっている「戦闘開始位置の直上」までは遡って
    // 直せない(生成はプレイヤーの少し先を走り続けているため) - この点は
    // 既知の制約として報告する。
    bool bossClearZoneActive;
    float bossClearZoneCenterX, bossClearZoneRadius;

    public void SetBossClearZone(float centerX, float radius)
    {
        bossClearZoneActive = true;
        bossClearZoneCenterX = centerX;
        bossClearZoneRadius = radius;
    }

    public void ClearBossClearZone() { bossClearZoneActive = false; }

    bool InBossClearZone(float x) => bossClearZoneActive && Mathf.Abs(x - bossClearZoneCenterX) <= bossClearZoneRadius;

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }

    void OnOriginShifted(float s)
    {
        nodeBaseX -= s;
        for (int i = 0; i < nodes.Count; i++) nodes[i].x -= s;
        for (int i = 0; i < spikes.Count; i++) { Spike sp = spikes[i]; sp.x -= s; spikes[i] = sp; }
        for (int i = 0; i < torches.Count; i++) { Torch t = torches[i]; t.lightPos.x -= s; torches[i] = t; }
        sectionEndX -= s;
        nextTorchX -= s;
        spikeClusterBlockedUntilX -= s;
        if (bossClearZoneActive) bossClearZoneCenterX -= s;
        if (!Active || player == null) return;
        PruneBehind(player.position.x - keepBehindDistance);
    }

    // 古い区間(プレイヤーのずっと後ろ)を捨てる。長距離走行で天井ノード/メッシュが際限なく増えるのを防ぐ。
    void PruneBehind(float cutoffX)
    {
        int k = Mathf.FloorToInt((cutoffX - nodeBaseX) / nodeSpacing);
        k = Mathf.Min(k, builtNodeIndex - 1, nodes.Count - 3);
        if (k > 0)
        {
            nodes.RemoveRange(0, k);
            nodeBaseX += k * nodeSpacing;
            builtNodeIndex -= k;
        }
        int rs = 0; while (rs < spikes.Count && spikes[rs].x < cutoffX) rs++;
        if (rs > 0) spikes.RemoveRange(0, rs);
        int rt = 0; while (rt < torches.Count && torches[rt].lightPos.x < cutoffX) rt++;
        if (rt > 0) torches.RemoveRange(0, rt);
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            GameObject g = spawned[i];
            if (g == null) { spawned.RemoveAt(i); continue; }
            var r = g.GetComponent<Renderer>();
            if (r == null || r.bounds.max.x >= cutoffX) continue;
            var mf = g.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(g);
            spawned.RemoveAt(i);
        }
    }
    Material bandMat, fillMat;
    Transform player;

    // 洞窟ステージ中に隠す/止める他レイヤー。
    public GameObject[] hideWhileActive;
    public CaveLighting lighting;

    public IReadOnlyList<Torch> Torches => torches;

    static Material SpriteMaterialFor(Texture2D tex)
    {
        var tmp = new GameObject("tmpMat");
        var sr = tmp.AddComponent<SpriteRenderer>();
        var m = new Material(sr.sharedMaterial);
        Destroy(tmp);
        m.mainTexture = tex;
        return m;
    }

    public void SetActive(bool on)
    {
        if (on == Active && (on == false || nodes.Count > 0)) { if (!on) return; }
        Clear();
        Active = on;
        var tmSet = TerrainManager.Instance;
        if (tmSet != null)
        {
            if (on) { if (savedFillDepth < 0f) savedFillDepth = tmSet.groundFillDepth; tmSet.groundFillDepth = Mathf.Max(savedFillDepth, terrainFillDepth); }
            else if (savedFillDepth >= 0f) { tmSet.groundFillDepth = savedFillDepth; savedFillDepth = -1f; }
            if (on) { if (savedTerrainAhead < 0f) savedTerrainAhead = tmSet.generateAheadDistance; tmSet.generateAheadDistance = Mathf.Max(savedTerrainAhead, terrainGenerateAhead); }
            else if (savedTerrainAhead >= 0f) { tmSet.generateAheadDistance = savedTerrainAhead; savedTerrainAhead = -1f; }
        }
        if (hideWhileActive != null)
            foreach (GameObject g in hideWhileActive) if (g != null) g.SetActive(!on);
        if (lighting != null) lighting.SetActive(on && styleLighting);
        if (WorldTimeCycle.Instance != null) WorldTimeCycle.Instance.forceDayOnly = on;
        if (on)
        {
            if (PlayerController.Instance != null) player = PlayerController.Instance.transform;
            bandMat = SpriteMaterialFor(ceilingBandTexture);
            fillMat = SpriteMaterialFor(ceilingFillTexture);
#if UNITY_EDITOR
            testMode = UnityEditor.EditorPrefs.GetInt("CaveTestMode", 0);
            if (UnityEditor.EditorPrefs.GetInt("CaveAutoTest", 0) == 1) { UnityEditor.EditorPrefs.SetInt("CaveAutoTest", 0); StartCoroutine(AutoTest()); }
#endif
            sectionMode = 0;
            sectionEndX = 24f; // 出だしは通常天井
            nextTorchX = 14f;
        }
    }

    void Clear()
    {
        foreach (GameObject g in spawned) if (g != null) Destroy(g);
        spawned.Clear();
        nodes.Clear();
        spikes.Clear();
        torches.Clear();
        builtNodeIndex = 0;
        nodeBaseX = 0f;
        spikeClusterBlockedUntilX = 0f;
        if (bandMat != null) Destroy(bandMat);
        if (fillMat != null) Destroy(fillMat);
    }

    void DebugWarp(float x)
    {
        var tm = TerrainManager.Instance;
        float g = tm.GetHeightAt(x) ?? tm.GetGroundLineAt(x);
        PlayerController.Instance.transform.position = new Vector3(x, g, 0f);
    }

    void Update()
    {
#if UNITY_EDITOR
        // Editor debug: Insert = Lives 999 (dmg still registers), Home = log ceiling state.
        if (Input.GetKeyDown(KeyCode.I) && GameManager.Instance != null) GameManager.Instance.DebugSetLives(999);
        // J = warp to 14m before the next spike, K = warp into the next low-ceiling section, L = log state.
        if (Active && PlayerController.Instance != null)
        {
            Transform pt = PlayerController.Instance.transform;
            if (Input.GetKeyDown(KeyCode.J)) { foreach (Spike s in spikes) if (s.x > pt.position.x + 20f) { DebugWarp(s.x - 14f); break; } }
            if (Input.GetKeyDown(KeyCode.K)) { foreach (Node n in nodes) if (n.mode == 2 && n.x > pt.position.x + 20f) { DebugWarp(n.x - 6f); break; } }
            if (Input.GetKeyDown(KeyCode.L)) { float? c = GetCeilingHeightAtInternal(pt.position.x); Debug.Log($"[Cave] x={pt.position.x:F1} y={pt.position.y:F1} ceil={c} nodes={nodes.Count} spikes={spikes.Count} torches={torches.Count}"); }
        }
#endif
        if (!Active) return;
        TerrainManager tm = TerrainManager.Instance;
        if (tm == null) return;
        if (player == null && PlayerController.Instance != null) player = PlayerController.Instance.transform;
        float px = player != null ? player.position.x : 0f;
        float margin = Mathf.Max(lowCeilingPitMargin, lowCeilingBranchMargin, spikePitMargin) + nodeSpacing;
        int guard = 0;
        while (guard++ < 600)
        {
            float x = nodeBaseX + nodes.Count * nodeSpacing;
            if (x > px + Mathf.Max(generateAhead, assistGenerateAhead)) break;
            // 穴/分岐の判定に必要な範囲まで地形が生成済みになるまで待つ(未生成
            // 領域の地面高さは実際の地形と無関係なため)。
            if (x + margin > tm.GeneratedEndX) break;
            AddNode(tm, x);
        }
    }

    void AddNode(TerrainManager tm, float x)
    {
        // マルチプレイ(2026-09-25) - ノードごとに(論理位置で決まる)独立した乱数列を使う。
        // シングルプレイでは何もしない(従来どおりUnityEngine.Random)。
        int nodeKey = Mathf.RoundToInt(FloatingOrigin.ToLogical(x) / Mathf.Max(0.01f, nodeSpacing));
        WorldRng.Cave.ReseedAt(nodeKey);
        WorldRng.CaveDetail.ReseedAt(nodeKey);

        if (x >= sectionEndX)
        {
            float r = testMode == 1 ? 0.5f : (testMode == 2 ? 0.05f : WorldRng.Cave.Value);
            if (sectionPicker != null && testMode == 0) sectionMode = sectionPicker(FloatingOrigin.ToLogical(x), r);
            else sectionMode = r < lowSectionChance ? 2 : (r < lowSectionChance + spikeSectionChance ? 1 : (r < lowSectionChance + spikeSectionChance + highSectionChance ? 3 : 0));
            sectionEndX = x + WorldRng.Cave.Range(sectionLengthMin, sectionLengthMax);
            // 通常区間が続きすぎないよう、直前が通常なら通常を選び直す確率は下げない(単純)。
        }

        int mode = sectionMode;
        if (mode == 2)
        {
            // 穴の近く/上下ルート分岐の近くには低天井を置かない(ジャンプ経路・
            // 上ルートの頭上を確保)。ノード単位で通常へ降格する。
            if (tm.IsNearPit(x, lowCeilingPitMargin) || tm.IsBranchNear(x, lowCeilingBranchMargin)) mode = 0;
        }
        // ボス遭遇区間: 低天井/針区間へ降格させず、常に通常天井にする。
        if (InBossClearZone(x) && mode != 4) mode = 0; // 天井が抜けた区間はそのまま(戦う空間は十分ある)
        float clearance = mode == 2 ? lowClearance : (mode == 1 ? spikeSectionClearance : (mode == 3 ? highClearance : mode == 4 ? openClearance : normalClearance));
        if (WorldRng.IsDeterministic)
        {
            // マルチプレイ(2026-09-25) - ボス付近の降格など端末ごとに差が出うるmodeに関係なく
            // 毎ノード1回だけ消費し、以降の乱数列が端末間でずれないようにする。
            float jitter = WorldRng.CaveDetail.Range(-normalClearanceJitter, normalClearanceJitter);
            if (mode != 2) clearance += jitter;
        }
        else if (mode != 2) clearance += Random.Range(-normalClearanceJitter, normalClearanceJitter);
        // 針の区間は、最も長い針の先端でも最低間隔が残る高さを下限にする。
        float minClear = MinPassageHeight + (mode == 1 ? spikeLengthMax : 0f);
        clearance = Mathf.Max(clearance, minClear);

        // 床の高さ: このノードの前後(隣のノードとの間の線分を含む範囲)で、下ルートの地面ラインと
        // 上ルートの床の高い方を調べる。上ルートを走るプレイヤーの頭上にも十分な空間を確保する。
        float groundTop = float.NegativeInfinity, upperTop = float.NegativeInfinity;
        for (float sx = x - nodeSpacing; sx <= x + nodeSpacing + 0.001f; sx += Mathf.Max(0.25f, floorSampleStep))
        {
            groundTop = Mathf.Max(groundTop, tm.GetGroundLineAt(sx));
            float? sky = tm.GetSkyHeightAt(sx);
            if (sky.HasValue) upperTop = Mathf.Max(upperTop, sky.Value);
        }
        float ceilY = groundTop + clearance;
        if (upperTop > float.NegativeInfinity)
            ceilY = Mathf.Max(ceilY, upperTop + Mathf.Max(minClear, clearance - upperRouteClearanceReduction));

        var node = new Node { x = x, y = ceilY, mode = mode };
        nodes.Add(node);

        // 針: 直前ノードとこのノードの間(両端が針区間のとき)に置く。両ノードが確定
        // 済みなので天井の高さを正確に取れる。穴/分岐の近くは避ける。
        if (nodes.Count >= 2)
        {
            Node prev = nodes[nodes.Count - 2];
            if (prev.mode == 1 && mode == 1 && prev.x >= spikeClusterBlockedUntilX && WorldRng.CaveDetail.Value < spikeClusterChancePerNode)
            {
                int count = WorldRng.CaveDetail.Range(1, spikeClusterMax + 1);
                float span = nodeSpacing - 0.8f - (count - 1) * spikeSpacing;
                if (span < 0f) { count = 1; span = nodeSpacing - 0.8f; }
                float sx = prev.x + 0.4f + WorldRng.CaveDetail.Value * span;
                bool ok = true;
                for (int k = 0; k < count && ok; k++)
                {
                    float cx = sx + k * spikeSpacing;
                    if (tm.IsNearPit(cx, spikePitMargin) || tm.IsBranchNear(cx, spikeBranchMargin) || InBossClearZone(cx)) ok = false;
                }
                if (ok)
                {
                    for (int k = 0; k < count; k++) AddSpike(sx + k * spikeSpacing);
                    spikeClusterBlockedUntilX = sx + count * spikeSpacing + 3f;
                }
            }
        }

        // たいまつ(足元は地面ライン上の平らな場所)。
        if (x >= nextTorchX)
        {
            for (int t = 0; t < 4; t++)
            {
                float tx = x + t * 2f;
                if (tm.IsNearPit(tx, 3f) || Mathf.Abs(tm.GetSlopeAngleAt(tx)) > 0.5f) continue;
                float? gy = tm.GetHeightAt(tx);
                if (!gy.HasValue) continue;
                PlaceTorch(tx, gy.Value);
                break;
            }
            nextTorchX = x + WorldRng.CaveDetail.Range(torchSpacingMin, torchSpacingMax);
        }

        // メッシュはmeshNodeGroup区間ぶんまとまったら作る。
        while (nodes.Count - builtNodeIndex > meshNodeGroup)
        {
            BuildMesh(builtNodeIndex, builtNodeIndex + meshNodeGroup);
            builtNodeIndex += meshNodeGroup;
        }
    }

    void AddSpike(float x)
    {
        float? cy = GetCeilingHeightAtInternal(x);
        // 該当ノードがまだ無い(次ノード側にはみ出す)場合は次ノードの高さを外挿せず、
        // 現在の最後のノードの高さで代用(針は天井から生えるだけなので数cmの差)。
        float top = cy ?? nodes[nodes.Count - 1].y;
        float len = WorldRng.CaveDetail.Range(spikeLengthMin, spikeLengthMax);
        bool hasArt = spikeSprites != null && spikeSprites.Length > 0;
        Sprite sp = hasArt ? spikeSprites[WorldRng.CaveDetail.Range(0, spikeSprites.Length)] : null;
        // 絵の縦横比を保ったまま幅を決める(当たり判定の三角形も同じ寸法)。
        float w = hasArt ? len * (sp.bounds.size.x / sp.bounds.size.y) * WorldRng.CaveDetail.Range(0.9f, 1.1f) : WorldRng.CaveDetail.Range(spikeWidthMin, spikeWidthMax);
        // 針の先端から床(上ルート含む)までが最低間隔に満たない場所には針を置かない。
        float floorTop = float.NegativeInfinity;
        for (float fx = x - w * 0.5f - 0.5f; fx <= x + w * 0.5f + 0.5f; fx += 0.5f)
            floorTop = Mathf.Max(floorTop, TerrainManager.Instance.GetFloorTopAt(fx));
        if (top - len - floorTop < MinPassageHeight) return;
        spikes.Add(new Spike { x = x, topY = top, len = len, hw = w * 0.5f });

        if (!hasArt) return;
        var go = new GameObject("CeilingSpike");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = -1;
        Vector2 size = sp.bounds.size;
        // 幅・長さを当たり判定(三角形)の寸法へ合わせる。根元は少し天井に食い込ませる。
        go.transform.localScale = new Vector3(w / size.x, len / size.y, 1f);
        float embed = 0.15f;
        // pivotは中央想定 -> 中心を(天井 - len/2 + embed)へ。
        go.transform.position = new Vector3(x, top - len * 0.5f + embed, 0f);
        spawned.Add(go);
    }

    void PlaceTorch(float x, float groundY)
    {
        torches.Add(new Torch { lightPos = new Vector2(x, groundY + torchLightHeight), phase = Random.value * 10f });
        if (torchSprite == null) return;
        var go = new GameObject("CaveTorch");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = torchSprite;
        sr.sortingOrder = -1;
        Vector2 size = torchSprite.bounds.size;
        float s = torchHeight / Mathf.Max(0.01f, size.y);
        go.transform.localScale = new Vector3(s, s, 1f);
        // pivotは中央想定 -> 足元が地面ラインに来るよう持ち上げる。
        go.transform.position = new Vector3(x, groundY + torchHeight * 0.5f - 0.05f, 0f);
        spawned.Add(go);
    }

    void BuildMesh(int i0, int i1)
    {
        int n = i1 - i0 + 1;
        var bandV = new Vector3[n * 2];
        var bandUV = new Vector2[n * 2];
        var fillV = new Vector3[n * 2];
        var fillUV = new Vector2[n * 2];
        float bandTileW = bandHeight * Mathf.Max(0.1f, ceilingBandAspect);
        // UVは論理X(Transform X + Offset)基準にして、座標を戻しても模様がつながるようにする。
        // 値が大きくなるとfloat精度が落ちるので、メッシュごとに整数タイルぶんを引いておく(Repeatなので見た目は同じ)。
        double logical0 = nodes[i0].x + FloatingOrigin.Offset;
        double bandBase = System.Math.Floor(logical0 / bandTileW);
        double fillBase = System.Math.Floor(logical0 / fillTileWorld);
        for (int k = 0; k < n; k++)
        {
            Node nd = nodes[i0 + k];
            float bottom = nd.y - ceilingVisualDrop;
            float top = bottom + bandHeight;
            bandV[k * 2] = new Vector3(nd.x, bottom, 0f);
            bandV[k * 2 + 1] = new Vector3(nd.x, top, 0f);
            float bu = (float)((nd.x + FloatingOrigin.Offset) / bandTileW - bandBase);
            bandUV[k * 2] = new Vector2(bu, 0f);
            bandUV[k * 2 + 1] = new Vector2(bu, 1f);
            // 帯の上端に少し食い込ませて塗りつぶし(継ぎ目の隙間防止)。
            float fillBottom = top - 0.3f;
            float fillTop = nd.y + fillHeight;
            fillV[k * 2] = new Vector3(nd.x, fillBottom, 0f);
            fillV[k * 2 + 1] = new Vector3(nd.x, fillTop, 0f);
            float fu = (float)((nd.x + FloatingOrigin.Offset) / fillTileWorld - fillBase);
            fillUV[k * 2] = new Vector2(fu, fillBottom / fillTileWorld);
            fillUV[k * 2 + 1] = new Vector2(fu, fillTop / fillTileWorld);
        }
        // 天井が抜けた区間(mode 4)に掛かる所は描かない。抜ける境目には切れ端を置く。
        var triList = new List<int>((n - 1) * 6);
        for (int k = 0; k < n - 1; k++)
        {
            bool openA = nodes[i0 + k].mode == 4, openB = nodes[i0 + k + 1].mode == 4;
            if (openA || openB)
            {
                if (openA != openB) PlaceBrokenEdge(openA ? nodes[i0 + k + 1] : nodes[i0 + k], openA);
                continue;
            }
            int a = k * 2;
            triList.Add(a); triList.Add(a + 1); triList.Add(a + 2);
            triList.Add(a + 2); triList.Add(a + 1); triList.Add(a + 3);
        }
        if (triList.Count == 0) return;
        int[] tris = triList.ToArray();
        MakeMeshObject("CeilingFill", fillV, fillUV, tris, fillMat, -3);
        MakeMeshObject("CeilingBand", bandV, bandUV, tris, bandMat, -2);
    }

    // 天井が途切れる所の切れ端(崩れた天井の端)。solidは天井が残っている側のノード、openOnLeft=抜けているのが左側。
    void PlaceBrokenEdge(Node solid, bool openOnLeft)
    {
        Sprite sp = brokenEdgeSprite;
        if (sp == null && spikeSprites != null && spikeSprites.Length > 0) sp = spikeSprites[0];
        if (sp == null) return;
        var go = new GameObject("CeilingBrokenEdge");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = -2;
        Vector2 size = sp.bounds.size;
        float h = bandHeight * 1.6f;
        float sc = h / Mathf.Max(0.01f, size.y);
        go.transform.localScale = new Vector3(openOnLeft ? -sc : sc, sc, 1f);
        float bottom = solid.y - ceilingVisualDrop;
        go.transform.position = new Vector3(solid.x + (openOnLeft ? -0.25f : 0.25f), bottom + h * 0.5f - 0.35f, 0f);
        spawned.Add(go);
    }

    void MakeMeshObject(string name, Vector3[] v, Vector2[] uv, int[] tris, Material mat, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var mesh = new Mesh { name = name };
        mesh.vertices = v;
        mesh.uv = uv;
        var cols = new Color[v.Length];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        spawned.Add(go);
    }

    float? GetCeilingHeightAtInternal(float x)
    {
        if (nodes.Count < 2) return null;
        float f = (x - nodeBaseX) / nodeSpacing;
        int i = Mathf.FloorToInt(f);
        if (i < 0) i = 0;
        if (i >= nodes.Count - 1) return null;
        float t = f - i;
        return Mathf.Lerp(nodes[i].y, nodes[i + 1].y, t);
    }

    // 針の先端も含めた通行可能な天井の高さ。
    public float? GetEffectiveCeilingHeightAt(float x)
    {
        float? c = GetCeiling(x);
        if (!c.HasValue) return null;
        float best = c.Value;
        for (int i = 0; i < spikes.Count; i++)
        {
            Spike s = spikes[i];
            if (s.x < x - spikeWidthMax - 1f) continue;
            if (s.x > x + spikeWidthMax + 1f) break;
            if (Mathf.Abs(x - s.x) <= s.hw + playerHalfWidth) best = Mathf.Min(best, s.topY - s.len);
        }
        return best;
    }
    float? GetCeiling(float x) => Active ? GetCeilingHeightAtInternal(x) : null;

    // 天井の高さ(コリジョン面)。ステージが洞窟でない/まだ生成されていなければnull。
    public float? GetCeilingHeightAt(float x)
    {
        if (!Active) return null;
        return GetCeilingHeightAtInternal(x);
    }

    // プレイヤーの頭頂(headY)が、位置xにある針の三角形に食い込んでいるか。
    // 見た目と同じ三角形(根元幅2*hw、先端が下)で判定する。
    public bool IsSpikeHit(float x, float headY)
    {
        if (!Active || spikes.Count == 0) return false;
        float reach = playerHalfWidth + spikeWidthMax;
        // spikesはx昇順。二分探索で開始位置を見つける。
        int lo = 0, hi = spikes.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (spikes[mid].x < x - reach) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < spikes.Count; i++)
        {
            Spike s = spikes[i];
            if (s.x > x + reach) break;
            float tip = s.topY - s.len;
            if (headY <= tip) continue;
            float hwAt = s.hw * Mathf.Clamp01((headY - tip) / s.len);
            if (Mathf.Abs(x - s.x) < hwAt + playerHalfWidth - spikeHitLeniency) return true;
        }
        return false;
    }
}

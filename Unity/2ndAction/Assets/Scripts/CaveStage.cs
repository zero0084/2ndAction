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
    public float sectionLengthMin = 32f;
    public float sectionLengthMax = 72f;
    // 低天井を置かない範囲(穴/上下ルート分岐からの距離)。
    public float lowCeilingPitMargin = 10f;
    public float lowCeilingBranchMargin = 9f;

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

    public bool Active { get; private set; }
    public static int SpikeHitCount; // debug counter (Editor auto test)

    class Node { public float x, y; public int mode; } // mode: 0 normal, 1 spike, 2 low
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
        if (lighting != null) lighting.SetActive(on);
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
            float x = nodes.Count * nodeSpacing;
            if (x > px + generateAhead) break;
            // 穴/分岐の判定に必要な範囲まで地形が生成済みになるまで待つ(未生成
            // 領域の地面高さは実際の地形と無関係なため)。
            if (x + margin > tm.GeneratedEndX) break;
            AddNode(tm, x);
        }
    }

    void AddNode(TerrainManager tm, float x)
    {
        if (x >= sectionEndX)
        {
            float r = testMode == 1 ? 0.5f : (testMode == 2 ? 0.05f : Random.value);
            sectionMode = r < lowSectionChance ? 2 : (r < lowSectionChance + spikeSectionChance ? 1 : 0);
            sectionEndX = x + Random.Range(sectionLengthMin, sectionLengthMax);
            // 通常区間が続きすぎないよう、直前が通常なら通常を選び直す確率は下げない(単純)。
        }

        int mode = sectionMode;
        if (mode == 2)
        {
            // 穴の近く/上下ルート分岐の近くには低天井を置かない(ジャンプ経路・
            // 上ルートの頭上を確保)。ノード単位で通常へ降格する。
            if (tm.IsNearPit(x, lowCeilingPitMargin) || tm.IsBranchNear(x, lowCeilingBranchMargin)) mode = 0;
        }
        float clearance = mode == 2 ? lowClearance : (mode == 1 ? spikeSectionClearance : normalClearance);
        if (mode != 2) clearance += Random.Range(-normalClearanceJitter, normalClearanceJitter);

        var node = new Node { x = x, y = tm.GetGroundLineAt(x) + clearance, mode = mode };
        nodes.Add(node);

        // 針: 直前ノードとこのノードの間(両端が針区間のとき)に置く。両ノードが確定
        // 済みなので天井の高さを正確に取れる。穴/分岐の近くは避ける。
        if (nodes.Count >= 2)
        {
            Node prev = nodes[nodes.Count - 2];
            if (prev.mode == 1 && mode == 1 && prev.x >= spikeClusterBlockedUntilX && Random.value < spikeClusterChancePerNode)
            {
                int count = Random.Range(1, spikeClusterMax + 1);
                float span = nodeSpacing - 0.8f - (count - 1) * spikeSpacing;
                if (span < 0f) { count = 1; span = nodeSpacing - 0.8f; }
                float sx = prev.x + 0.4f + Random.value * span;
                bool ok = true;
                for (int k = 0; k < count && ok; k++)
                {
                    float cx = sx + k * spikeSpacing;
                    if (tm.IsNearPit(cx, spikePitMargin) || tm.IsBranchNear(cx, spikeBranchMargin)) ok = false;
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
            nextTorchX = x + Random.Range(torchSpacingMin, torchSpacingMax);
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
        float len = Random.Range(spikeLengthMin, spikeLengthMax);
        bool hasArt = spikeSprites != null && spikeSprites.Length > 0;
        Sprite sp = hasArt ? spikeSprites[Random.Range(0, spikeSprites.Length)] : null;
        // 絵の縦横比を保ったまま幅を決める(当たり判定の三角形も同じ寸法)。
        float w = hasArt ? len * (sp.bounds.size.x / sp.bounds.size.y) * Random.Range(0.9f, 1.1f) : Random.Range(spikeWidthMin, spikeWidthMax);
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
        for (int k = 0; k < n; k++)
        {
            Node nd = nodes[i0 + k];
            float bottom = nd.y - ceilingVisualDrop;
            float top = bottom + bandHeight;
            bandV[k * 2] = new Vector3(nd.x, bottom, 0f);
            bandV[k * 2 + 1] = new Vector3(nd.x, top, 0f);
            bandUV[k * 2] = new Vector2(nd.x / bandTileW, 0f);
            bandUV[k * 2 + 1] = new Vector2(nd.x / bandTileW, 1f);
            // 帯の上端に少し食い込ませて塗りつぶし(継ぎ目の隙間防止)。
            float fillBottom = top - 0.3f;
            float fillTop = nd.y + fillHeight;
            fillV[k * 2] = new Vector3(nd.x, fillBottom, 0f);
            fillV[k * 2 + 1] = new Vector3(nd.x, fillTop, 0f);
            fillUV[k * 2] = new Vector2(nd.x / fillTileWorld, fillBottom / fillTileWorld);
            fillUV[k * 2 + 1] = new Vector2(nd.x / fillTileWorld, fillTop / fillTileWorld);
        }
        var tris = new int[(n - 1) * 6];
        for (int k = 0; k < n - 1; k++)
        {
            int a = k * 2;
            tris[k * 6 + 0] = a; tris[k * 6 + 1] = a + 1; tris[k * 6 + 2] = a + 2;
            tris[k * 6 + 3] = a + 2; tris[k * 6 + 4] = a + 1; tris[k * 6 + 5] = a + 3;
        }
        MakeMeshObject("CeilingFill", fillV, fillUV, tris, fillMat, -3);
        MakeMeshObject("CeilingBand", bandV, bandUV, tris, bandMat, -2);
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
        float f = x / nodeSpacing;
        int i = Mathf.FloorToInt(f);
        if (i < 0) i = 0;
        if (i >= nodes.Count - 1) return null;
        float t = f - i;
        return Mathf.Lerp(nodes[i].y, nodes[i + 1].y, t);
    }

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

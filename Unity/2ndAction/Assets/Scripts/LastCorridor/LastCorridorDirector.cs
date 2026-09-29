using System.Collections.Generic;
using UnityEngine;

// LAST CORRIDOR(ラストダンジョン候補、仮名、2026-09-29)の「見た目と構成」担当。
// ルール(オートラン/フリック攻撃/ジャンプ/2段ジャンプ/カード/速度/敵/ボス/帰還/死亡でRun終了)は他のマップと全く同じで、
// ここでは既存の仕組みへの差し込み口(TerrainManager.ThemeApplied / pitWidthAt / skyAllowedAt、CaveStage.sectionPicker、
// ObstacleSpawner.Created)を使って、距離による段階的な景観の変化と、ギミックの再解釈だけを行う。
//
// 段階(距離は調整用、Inspectorで変更可):
//   P0  0〜25,000m   壊れていない巨大回廊: 天井あり(古代の杭の区間/低い天井)、白い巨大柱・アーチ・騎士像・灯柱。穴は狭い
//   P1  25,000〜55,000m 崩壊が始まる: 天井が抜ける区間が増えていく、折れた柱、落ちてくる構造物/閉じてくる扉が増える、穴が広がる
//   P2  55,000〜85,000m 奈落: 天井はほぼ無く、床は下の見えない橋(断面を描かない)、浮遊する回廊の欠片(空中足場)、宙に浮く瓦礫
//   P3  85,000〜100,000m 最後の道: 装飾をほとんど消し、道の両脇の灯柱だけが先へ続く(「進む先」を強調)
// 100,000mは死神(既存の仕組みのまま)。正式なラスボス/エンディング処理は持たない。
public class LastCorridorDirector : MonoBehaviour
{
    public const string StageId = "last_corridor";
    public static LastCorridorDirector Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance.active;

    [Header("段階の境界(m)")]
    public float phase1From = 25000f;
    public float phase2From = 55000f;
    public float phase3From = 85000f;
    [Tooltip("背景を次の段階へ切り替え始める手前の距離(m)")]
    public float backgroundBlendMeters = 1500f;

    [Header("段階ごとの値 (P0, P1, P2, P3)")]
    [Tooltip("穴(崩落/橋の切れ目)の幅。1段ジャンプ(約4.5m)で越えられる範囲")]
    public float[] pitWidths = { 3.0f, 3.4f, 3.8f, 3.4f };
    [Tooltip("床の断面の深さ。小さい=下の見えない橋(奈落)")]
    public float[] groundFillDepths = { 48f, 48f, 0.6f, 0.6f };
    [Tooltip("障害物が「落ちてくる構造物/閉じてくる扉」になる確率")]
    public float[] fallChances = { 0.1f, 0.35f, 0.3f, 0.2f };
    [Tooltip("空中足場(浮遊する回廊の欠片)を置き始める距離")]
    public float skyIslandsFrom = 55000f;

    [Header("天井の区間(抽選の割合)")]
    public Vector4 p0Sections = new Vector4(0.05f, 0.16f, 0.30f, 0.12f); // open, low, spike(杭), high
    public Vector2 p1OpenRange = new Vector2(0.18f, 0.6f);
    public Vector4 p1Sections = new Vector4(0f, 0.12f, 0.26f, 0.08f);
    public Vector4 p2Sections = new Vector4(0.9f, 0f, 0.1f, 0f);

    [Header("Art (SceneBuilderが設定)")]
    public Sprite[] phaseBackgrounds = new Sprite[4];
    public Sprite pillar, arch, statue, lamp, brokenPillar;
    public Color propTint = new Color(0.62f, 0.66f, 0.78f, 1f);
    public int propSortingOrder = -6;

    bool active;
    TerrainManager tm;
    SpriteRenderer baseBg, overlayBg;
    Sprite savedBgSprite;
    float nextPropLX;
    readonly List<Prop> props = new List<Prop>();
    struct Prop { public Transform t; public float baseY, bob, phase; }

    // 診断/テスト用
    public int PhaseNow { get; private set; } = -1;
    public int PropCount => props.Count;
    public static int FallingAttached, GateAttached;

    void Awake()
    {
        Instance = this;
        TerrainManager.ThemeApplied += OnThemeApplied;
        ObstacleSpawner.Created += OnObstacleCreated;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // テスト用: -lcPhaseScale 0.02 で段階の境界を縮める(マルチの2プロセス試験で、短い距離のうちに後半の地形を両方で作らせる)
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-lcPhaseScale" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k) && k > 0f)
            {
                phase1From *= k; phase2From *= k; phase3From *= k; skyIslandsFrom *= k; backgroundBlendMeters *= k;
                Debug.Log($"[LastCorridor] phase scale {k}: {phase1From}/{phase2From}/{phase3From}");
            }
#endif
    }

    void OnDestroy()
    {
        TerrainManager.ThemeApplied -= OnThemeApplied;
        ObstacleSpawner.Created -= OnObstacleCreated;
        if (Instance == this) Instance = null;
    }

    public int PhaseAt(float logicalX) => logicalX >= phase3From ? 3 : logicalX >= phase2From ? 2 : logicalX >= phase1From ? 1 : 0;
    float PhaseProgress(float lx)
    {
        switch (PhaseAt(lx))
        {
            case 0: return Mathf.Clamp01(lx / phase1From);
            case 1: return Mathf.InverseLerp(phase1From, phase2From, lx);
            case 2: return Mathf.InverseLerp(phase2From, phase3From, lx);
            default: return Mathf.InverseLerp(phase3From, 100000f, lx);
        }
    }
    static float Pick(float[] arr, int p) => arr != null && arr.Length > 0 ? arr[Mathf.Clamp(p, 0, arr.Length - 1)] : 0f;

    // ===================================================================== //
    // ステージの切り替え
    // ===================================================================== //
    void OnThemeApplied(TerrainManager t, string stageId)
    {
        bool on = stageId == StageId;
        if (!on) { if (active) Deactivate(); return; }
        tm = t;
        active = true;
        tm.pitWidthAt = lx => Pick(pitWidths, PhaseAt(lx));
        tm.skyAllowedAt = lx => lx >= skyIslandsFrom;
        if (tm.cave != null) tm.cave.sectionPicker = PickSection;
        baseBg = tm.backgroundRenderer;
        if (baseBg != null) savedBgSprite = baseBg.sprite;
        EnsureOverlay();
        ClearProps();
        nextPropLX = 12f;
        PhaseNow = -1;
    }

    void Deactivate()
    {
        active = false;
        ClearProps();
        if (overlayBg != null) overlayBg.enabled = false;
        PhaseNow = -1;
    }

    void EnsureOverlay()
    {
        if (overlayBg != null) { overlayBg.enabled = true; return; }
        if (baseBg == null) return;
        var go = new GameObject("LastCorridorBgOverlay");
        go.AddComponent<FloatingOriginExempt>();
        overlayBg = go.AddComponent<SpriteRenderer>();
        overlayBg.sortingOrder = baseBg.sortingOrder + 1;
        overlayBg.color = new Color(1f, 1f, 1f, 0f);
        var f = go.AddComponent<BackgroundFollower>();
        var bf = baseBg.GetComponent<BackgroundFollower>();
        f.cam = bf != null ? bf.cam : Camera.main;
    }

    // ===================================================================== //
    // 天井の区間(CaveStageの抽選の差し替え。乱数は従来どおり1回だけ)
    //  0 通常 / 1 杭(針)の区間 / 2 低い天井 / 3 高い天井 / 4 天井が抜けている
    // ===================================================================== //
    int PickSection(float lx, float r)
    {
        int p = PhaseAt(lx);
        Vector4 s;
        if (p == 0) s = p0Sections;
        else if (p == 1) { s = p1Sections; s.x = Mathf.Lerp(p1OpenRange.x, p1OpenRange.y, PhaseProgress(lx)); }
        else if (p == 2) s = p2Sections;
        else return 4;
        if (r < s.x) return 4; r -= s.x;
        if (r < s.y) return 2; r -= s.y;
        if (r < s.z) return 1; r -= s.z;
        if (r < s.w) return 3;
        return 0;
    }

    // ===================================================================== //
    // 障害物 → 落ちてくる構造物 / 閉じてくる扉
    // ===================================================================== //
    static uint Hash(int v)
    {
        uint x = (uint)v;
        x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
        return x;
    }

    void OnObstacleCreated(ObstacleController oc, string stageId)
    {
        if (!active || stageId != StageId || oc == null) return;
        float lx = FloatingOrigin.ToLogical(oc.transform.position.x);
        int p = PhaseAt(lx);
        float roll = (Hash(Mathf.RoundToInt(lx * 4f)) % 1000u) / 1000f;
        if (roll >= Pick(fallChances, p)) return;
        bool gate = oc.kind == "Wall" || oc.kind == "BreakableTree"; // 封印の扉は上から降りてきて道を閉じる
        float ground = oc.transform.position.y;
        float? ceil = tm != null ? tm.GetCeilingHeightAt(oc.transform.position.x) : null;
        float h = ceil.HasValue && ceil.Value - ground < 20f ? Mathf.Clamp(ceil.Value - ground - 1.2f, 4f, 9f) : 10f;
        FallingDebris.Attach(oc, gate ? FallingDebris.Kind.Gate : FallingDebris.Kind.Fall, h);
        if (gate) GateAttached++; else FallingAttached++;
    }

    // ===================================================================== //
    // 毎フレーム: 背景の段階切り替え / 床の断面の深さ / 背景側の構造物
    // ===================================================================== //
    void Update()
    {
        if (!active || tm == null) return;
        var gm = GameManager.Instance;
        float d = gm != null ? gm.MaxDistance : 0f;
        int p = PhaseAt(d);
        if (p != PhaseNow)
        {
            PhaseNow = p;
            if (gm != null && gm.DebugMode) Debug.Log($"[LastCorridor] phase {p} at {d:F0}m");
        }
        UpdateBackground(d, p);

        // これから作られる地面の断面の深さ(生成の先端の段階で決める)
        float genLX = FloatingOrigin.ToLogical(tm.GeneratedEndX);
        float wantDepth = Pick(groundFillDepths, PhaseAt(genLX));
        if (tm.cave != null && tm.cave.Active && PhaseAt(genLX) <= 1) wantDepth = Mathf.Max(wantDepth, tm.cave.terrainFillDepth);
        tm.groundFillDepth = wantDepth;

        PlaceProps();
        AnimateAndPruneProps();
    }

    void UpdateBackground(float d, int p)
    {
        if (baseBg == null) return;
        Sprite cur = BgFor(p);
        Sprite next = p < 3 ? BgFor(p + 1) : null;
        float boundary = p == 0 ? phase1From : p == 1 ? phase2From : phase3From;
        float a = p < 3 ? Mathf.Clamp01((d - (boundary - backgroundBlendMeters)) / Mathf.Max(1f, backgroundBlendMeters)) : 0f;
        if (cur != null && baseBg.sprite != cur) baseBg.sprite = cur;
        if (overlayBg != null)
        {
            bool show = next != null && next != cur && a > 0f;
            overlayBg.enabled = show;
            if (show)
            {
                if (overlayBg.sprite != next) overlayBg.sprite = next;
                Color bc = baseBg.color;
                overlayBg.color = new Color(bc.r, bc.g, bc.b, a);
            }
        }
    }

    Sprite BgFor(int p)
    {
        for (int i = Mathf.Min(p, phaseBackgrounds.Length - 1); i >= 0; i--)
            if (phaseBackgrounds[i] != null) return phaseBackgrounds[i];
        return savedBgSprite;
    }

    // ---- 背景側の構造物(当たり判定なし、地面の後ろ) ----
    void PlaceProps()
    {
        float endLX = FloatingOrigin.ToLogical(tm.GeneratedEndX) - 3f;
        int guard = 0;
        while (nextPropLX < endLX && guard++ < 40)
        {
            float lx = nextPropLX;
            uint h = Hash(Mathf.RoundToInt(lx * 8f) ^ 0x5a17);
            float r0 = (h & 0xffff) / 65535f, r1 = (h >> 16) / 65535f;
            int p = PhaseAt(lx);
            nextPropLX += p == 0 ? Mathf.Lerp(7f, 13f, r1) : p == 1 ? Mathf.Lerp(9f, 17f, r1) : p == 2 ? Mathf.Lerp(11f, 21f, r1) : 15f;
            float wx = (float)(lx - FloatingOrigin.Offset);
            if (tm.IsNearPit(wx, 2.2f)) continue;
            float gy = tm.GetGroundLineAt(wx);
            switch (p)
            {
                case 0:
                    if (r0 < 0.4f) AddProp(pillar, wx, gy - 0.4f, 16f, 0f);
                    else if (r0 < 0.6f) AddProp(arch, wx, gy - 0.3f, 7.5f, 0f);
                    else if (r0 < 0.8f) AddProp(statue, wx, gy - 0.25f, 4.6f, 0f);
                    else AddProp(lamp, wx, gy - 0.1f, 3.4f, 0f);
                    break;
                case 1:
                    if (r0 < 0.25f) AddProp(pillar, wx, gy - 0.4f, 16f, 0f);
                    else if (r0 < 0.6f) AddProp(brokenPillar, wx, gy - 0.3f, 4.2f, 0f);
                    else if (r0 < 0.72f) AddProp(arch, wx, gy - 0.3f, 7.5f, 0f);
                    else if (r0 < 0.84f) AddProp(statue, wx, gy - 0.25f, 4.6f, 0f);
                    else AddProp(brokenPillar, wx, gy + 3.5f + r1 * 3f, 2.6f, 0.25f); // 宙に浮き始めた欠片
                    break;
                case 2:
                    if (r0 < 0.55f) AddProp(brokenPillar, wx, gy + 2.8f + r1 * 5f, 2.2f + r1 * 1.6f, 0.35f);
                    else if (r0 < 0.75f) AddProp(lamp, wx, gy - 0.1f, 3.4f, 0f);
                    break;
                default:
                    AddProp(lamp, wx, gy - 0.1f, 3.6f, 0f); // 最後の道: 灯柱だけが先へ続く
                    break;
            }
        }
    }

    void AddProp(Sprite sp, float x, float y, float height, float bob)
    {
        if (sp == null) return;
        var go = new GameObject("LCProp_" + sp.name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = propSortingOrder;
        sr.color = propTint;
        float s = height / Mathf.Max(0.01f, sp.bounds.size.y);
        bool flip = ((int)(x * 3f) & 1) == 1 && sp != arch && sp != lamp;
        go.transform.localScale = new Vector3(flip ? -s : s, s, 1f);
        go.transform.position = new Vector3(x, y, 0f);
        props.Add(new Prop { t = go.transform, baseY = y, bob = bob, phase = x * 0.37f });
    }

    void AnimateAndPruneProps()
    {
        var pc = PlayerController.Instance;
        float cut = pc != null ? pc.transform.position.x - 40f : float.NegativeInfinity;
        float time = Time.time;
        for (int i = props.Count - 1; i >= 0; i--)
        {
            Prop pr = props[i];
            if (pr.t == null) { props.RemoveAt(i); continue; }
            if (pr.t.position.x < cut) { Destroy(pr.t.gameObject); props.RemoveAt(i); continue; }
            if (pr.bob > 0f)
            {
                Vector3 pos = pr.t.position;
                pos.y = pr.baseY + Mathf.Sin(time * 0.8f + pr.phase) * pr.bob;
                pr.t.position = pos;
            }
        }
    }

    void ClearProps()
    {
        foreach (var pr in props) if (pr.t != null) Destroy(pr.t.gameObject);
        props.Clear();
    }

    // デバッグワープで大きく進んだ時: 置く位置も現在地まで進める(過去の位置に大量に置かない)
    void OnEnable() { FloatingOrigin.Warped += OnWarped; }
    void OnDisable() { FloatingOrigin.Warped -= OnWarped; }
    void OnWarped(float dd)
    {
        if (!active) return;
        ClearProps();
        var pc = PlayerController.Instance;
        if (pc != null) nextPropLX = FloatingOrigin.ToLogical(pc.transform.position.x) - 20f;
    }
}

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

    // ===================================================================== //
    // ラストダンジョン化(2026-09-30): 「既存ルールのまま、最も難しいステージ」
    //   0〜90,000m  最高難度の通常区間。距離で決まる「難易度の波」(激しい→少し落ち着く→再び激しい)で、
    //               敵の密度/Hardの比重、障害物(壊せる扉が増える)、穴(確率/非常に大きな穴/狭い足場の連続)、
    //               天井(杭/低い天井=上下からの圧迫)、落ちてくる構造物をまとめて上下させる。
    //               複合: 穴の上に飛ぶ敵→着地点に地上の敵→直後に壊せる扉(ld_pit_ambush)など(Encounter側のFormation)。
    //   90,000〜99,000m ボスラッシュ(通常の敵なし。LastDungeonBossTuning.rushGates / BossManager.LastDungeon.cs) / 99,000〜100,000m 静寂(何も出さない)
    //   100,000m 三姉妹戦 → エンドロール → ONE MORE MILE?(LastDungeonFlow)
    // すべて論理X(走行距離)だけで決まるので、マルチでも全端末で同じ地形になる。
    // hardMode=false(または起動引数 -lcLegacyDifficulty)で従来のLAST CORRIDORに戻る(比較用)。
    // ===================================================================== //
    [Header("ラストダンジョン化: 難易度の波")]
    public bool hardMode = true;
    [Tooltip("波の1周期(m): 落ち着く→盛り上がる→激しい→収まる")] public float waveCycle = 3000f;
    [Tooltip("波が始まる距離(m)。それまでは準備運動")] public float waveStart = 600f;
    [Tooltip("ここから通常の敵を止める(ボスラッシュ)")] public float rushFrom = 90000f;
    [Tooltip("ここから何も出さない(静寂区間)")] public float silenceFrom = 99000f;
    [Tooltip("穴の確率(落ち着く区間, 激しい区間)。従来は5km以降ずっと0.55")] public Vector2 pitChanceRange = new Vector2(0.28f, 0.68f);
    [Tooltip("激しい区間で、穴が『非常に大きな穴』になる割合")] public float hugePitChance = 0.25f;
    [Tooltip("非常に大きな穴の幅の上限(m)")] public float hugePitMaxWidth = 9f;
    [Tooltip("非常に大きな穴の幅 = 一番遅いキャラの1段ジャンプで届く距離 × この割合(高速でも理不尽にしない)")] public float hugePitReachFraction = 0.45f;
    [Tooltip("1段ジャンプの滞空時間(秒)。2×jumpForce/gravity(9/20)。キャラによらない共通値(マルチで地形を一致させるため)")] public float jumpAirTime = 0.9f;
    [Tooltip("激しい区間で、穴の直後の平地を狭い足場にする割合と長さ(m)")] public float narrowFootingChance = 0.3f;
    public float narrowFootingLength = 4.8f;
    [Tooltip("障害物の配置間隔の倍率(落ち着く区間, 激しい区間)")] public Vector2 obstacleIntervalRange = new Vector2(1.3f, 0.5f);
    [Tooltip("敵のEncounterの間隔/休憩の倍率(落ち着く区間, 激しい区間)")] public Vector2 encounterPaceRange = new Vector2(1.3f, 0.42f);
    [Tooltip("落ちてくる構造物の確率の倍率(落ち着く区間, 激しい区間)")] public Vector2 fallChanceScale = new Vector2(1.0f, 2.3f);
    [Tooltip("激しい区間の複合: 穴の上の敵/着地点の敵の後ろに置く壊せる扉までの距離(m)")] public float ambushGateDistance = 3.4f;
    public static int AmbushGatesPlaced;
    [Tooltip("落ちてくる構造物: 着地からプレイヤーが届くまでに残す秒数(ラストダンジョン)。従来は1.1")] public float fallLeadTimeHard = 0.8f;

    // 0..1: その距離の激しさ。落ち着く区間≒0.1、激しい区間≒1。深いほど全体が少し強い。
    public float SurgeAt(float lx)
    {
        if (!hardMode || lx >= rushFrom) return 0f;
        if (lx < waveStart) return 0.12f;
        float u = (lx - waveStart) / Mathf.Max(500f, waveCycle);
        int n = Mathf.FloorToInt(u);
        float p = u - n;
        float jit = (Hash(n * 7919 + 13) % 1000u) / 1000f; // 周期ごとに少し形を変える(毎回同じリズムにしない)
        float calmEnd = 0.14f + 0.08f * jit, buildEnd = 0.40f + 0.06f * jit, surgeEnd = 0.80f - 0.06f * jit;
        float s;
        if (p < calmEnd) s = 0.08f;
        else if (p < buildEnd) s = Mathf.SmoothStep(0.15f, 0.75f, (p - calmEnd) / (buildEnd - calmEnd));
        else if (p < surgeEnd) s = 1f;
        else s = Mathf.Lerp(1f, 0.1f, (p - surgeEnd) / (1f - surgeEnd));
        float depth = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(lx / 85000f));
        return Mathf.Clamp01(s * depth);
    }
    public string WaveLabel(float lx) { float s = SurgeAt(lx); return s >= 0.75f ? "surge" : s >= 0.35f ? "build" : "calm"; }
    static float Hash01(float v, int salt) => (Hash(Mathf.RoundToInt(v) ^ salt) % 10000u) / 10000f;

    float PitWidthAtHard(float lx)
    {
        float w = Pick(pitWidths, PhaseAt(lx));
        if (!hardMode || lx >= rushFrom) return w;
        float s = SurgeAt(lx);
        if (s > 0.6f && Hash01(lx * 4f, 0x3a91) < hugePitChance)
        {
            var pc = PlayerController.Instance;
            float mult = pc != null ? pc.NaturalMultiplierAt(lx) : 1f;
            float slowest = 5f * mult * 0.93f; // 一番遅いキャラ(移動性能0.93)の巡航速度
            w = Mathf.Clamp(slowest * jumpAirTime * hugePitReachFraction, w, hugePitMaxWidth);
        }
        return w;
    }

    float PitChanceHard(float lx, float def)
    {
        if (!hardMode) return def;
        if (lx >= silenceFrom - 200f) return 0f;      // 静寂区間(とその手前)は穴なし
        if (lx >= rushFrom) return 0.12f;             // ボスラッシュ: 走る区間は控えめ
        if (lx < 300f) return Mathf.Min(def, 0.1f);
        return Mathf.Lerp(pitChanceRange.x, pitChanceRange.y, SurgeAt(lx));
    }

    float FlatLengthHard(float lx)
    {
        if (!hardMode || lx >= rushFrom) return 0f;
        return SurgeAt(lx) > 0.7f && Hash01(lx * 4f, 0x51c3) < narrowFootingChance ? narrowFootingLength : 0f;
    }

    // ---- 障害物/敵(走行距離基準) ----
    float ObstacleIntervalHard(float d)
    {
        if (!hardMode) return 1f;
        if (d >= rushFrom) return 1.7f;
        return Mathf.Lerp(obstacleIntervalRange.x, obstacleIntervalRange.y, SurgeAt(d));
    }
    float ObstacleWeightHard(float d, string kind)
    {
        if (!hardMode) return 1f;
        float s = SurgeAt(d);
        if (d >= rushFrom) return kind == "Wall" || kind == "GiantRock" ? 0f : 1f; // ボスラッシュの合間は大型なし
        switch (kind)
        {
            case "BreakableTree": return 1f + 1.3f * s; // 封印の小扉(壊せる)
            case "Wall": return 1f + 0.7f * s;          // 大扉
            case "GiantRock": return 0.8f + 0.4f * s;
            default: return 1f;
        }
    }

    void OnEncounterSpawned(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned)
    {
        if (!active || !hardMode || f == null || rec == null) return;
        if (f.formationId != "ld_pit_ambush" && f.formationId != "ld_gate_rush") return;
        // 着地点/最後の地上の敵のすぐ後ろに壊せる扉(「穴→空中の敵→着地点の敵→直後に破壊壁」)
        float lastGround = float.NegativeInfinity;
        foreach (var s in spawned)
            if (s.go != null && EncounterSlots.IsGround(s.slot)) lastGround = Mathf.Max(lastGround, s.go.transform.position.x);
        if (float.IsNegativeInfinity(lastGround)) return;
        var spawner = ObstacleSpawner.FindForStage(StageId);
        if (spawner == null) return;
        for (int k = 0; k < 12; k++)
        {
            if (spawner.SpawnSpecificAt(lastGround + ambushGateDistance + k * 0.7f, "BreakableTree") != null) { AmbushGatesPlaced++; break; }
        }
    }

    // ---- エンディング(LastDungeonFlow)用: 背景/構造物/床の断面の上書き ----
    [System.NonSerialized] public bool bgOverride;
    [System.NonSerialized] public Sprite bgOverrideFrom, bgOverrideTo;
    [System.NonSerialized] public float bgOverrideBlend;
    [System.NonSerialized] public Color bgOverrideTint = Color.white;
    [System.NonSerialized] public float propsEndLX = float.PositiveInfinity;
    [System.NonSerialized] public float fillDepthOverride = float.NaN;

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
        if (GetComponent<LastDungeonFlow>() == null) gameObject.AddComponent<LastDungeonFlow>(); // ラストダンジョンの一連の流れ(2026-09-30)
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
        if (System.Array.IndexOf(args, "-lcLegacyDifficulty") >= 0) { hardMode = false; Debug.Log("[LastCorridor] legacy difficulty (hardMode=false)"); }
#endif
    }

    void ClearHooks()
    {
        ObstacleSpawner.IntervalScaleAt = null; ObstacleSpawner.WeightScaleAt = null; ObstacleSpawner.SuppressAt = null; ObstacleSpawner.ExtraAheadAt = null;
        EncounterDirector.PaceAt = null; EncounterDirector.SurgeAt = null; EncounterDirector.SuppressAt = null;
        BonusZone.BlockedAt = null;
        EncounterDirector.Spawned -= OnEncounterSpawned;
    }

    void OnDestroy()
    {
        ClearHooks();
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
        tm.pitWidthAt = PitWidthAtHard;
        tm.skyAllowedAt = lx => lx >= skyIslandsFrom && (!hardMode || lx < silenceFrom - 300f);
        tm.pitChanceAt = PitChanceHard;
        tm.flatLengthAt = FlatLengthHard;
        tm.forceFlatAt = lx => hardMode && lx >= silenceFrom - 150f; // 静寂区間から先は坂も穴もない一本道
        if (tm.cave != null) tm.cave.sectionPicker = PickSection;
        if (hardMode)
        {
            ObstacleSpawner.IntervalScaleAt = ObstacleIntervalHard;
            ObstacleSpawner.WeightScaleAt = ObstacleWeightHard;
            ObstacleSpawner.SuppressAt = d => d >= silenceFrom - 60f;
            ObstacleSpawner.ExtraAheadAt = d =>
            {
                var pc = PlayerController.Instance;
                float v = pc != null ? pc.CurrentAutoRunSpeed : 0f;
                return d < silenceFrom ? Mathf.Clamp(v * 1.4f - 12f, 0f, 60f) : 0f; // 100km/hで約+27m
            };
            EncounterDirector.PaceAt = d => Mathf.Lerp(encounterPaceRange.x, encounterPaceRange.y, SurgeAt(d));
            EncounterDirector.SurgeAt = SurgeAt;
            EncounterDirector.SuppressAt = d => (BossManager.RushEnabled && d >= rushFrom - 60f) || d >= silenceFrom - 60f;
            BonusZone.BlockedAt = d => d >= rushFrom - 2500f;
            EncounterDirector.Spawned -= OnEncounterSpawned;
            EncounterDirector.Spawned += OnEncounterSpawned;
        }
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
        ClearHooks();
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
        if (hardMode && lx >= rushFrom - 300f) return 4; // ボスラッシュ〜静寂〜エンディング: 天井なし(大型ボス/空の見える道)
        if (p == 0) s = p0Sections;
        else if (p == 1) { s = p1Sections; s.x = Mathf.Lerp(p1OpenRange.x, p1OpenRange.y, PhaseProgress(lx)); }
        else if (p == 2) s = p2Sections;
        else if (!hardMode) return 4;
        else s = new Vector4(0.75f, 0f, 0.1f, 0f);
        if (hardMode)
        {
            float g = SurgeAt(lx);
            float press = Mathf.Lerp(0.5f, 1.8f, g);  // 杭/低い天井(圧迫)
            s.x *= Mathf.Lerp(1.3f, 0.55f, g);        // 抜けている区間は激しい所ほど減る
            s.y *= press; s.z *= press;
            if (p >= 2) { s.y += 0.12f * g; s.z += 0.08f * g; } // 奈落/最後の道でも、激しい区間は上下から圧迫する
            float sum = s.x + s.y + s.z + s.w;
            if (sum > 0.97f) s *= 0.97f / sum;
        }
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
        float fc = Pick(fallChances, p);
        if (hardMode) fc = lx >= rushFrom ? fc * 0.4f : Mathf.Min(0.85f, fc * Mathf.Lerp(fallChanceScale.x, fallChanceScale.y, SurgeAt(lx)));
        if (roll >= fc) return;
        bool gate = oc.kind == "Wall" || oc.kind == "BreakableTree"; // 封印の扉は上から降りてきて道を閉じる
        float ground = oc.transform.position.y;
        float? ceil = tm != null ? tm.GetCeilingHeightAt(oc.transform.position.x) : null;
        float h = ceil.HasValue && ceil.Value - ground < 20f ? Mathf.Clamp(ceil.Value - ground - 1.2f, 4f, 9f) : 10f;
        var fd = FallingDebris.Attach(oc, gate ? FallingDebris.Kind.Gate : FallingDebris.Kind.Fall, h);
        // ラストダンジョン化: 100km/h前後では画面に見えている先が約1秒ぶんしかないため、着地〜到達の余裕を0.8秒に
        // (通常の障害物が見えてから届くまでと同程度。高速補助は最初から当たり判定を見ている)
        if (hardMode) fd.leadTime = fallLeadTimeHard;
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
        if (!float.IsNaN(fillDepthOverride)) wantDepth = fillDepthOverride;
        tm.groundFillDepth = wantDepth;

        PlaceProps();
        AnimateAndPruneProps();
    }

    void UpdateBackground(float d, int p)
    {
        if (baseBg == null) return;
        if (bgOverride)
        {
            if (bgOverrideFrom != null && baseBg.sprite != bgOverrideFrom) baseBg.sprite = bgOverrideFrom;
            baseBg.color = bgOverrideTint;
            if (overlayBg != null)
            {
                bool show = bgOverrideTo != null && bgOverrideBlend > 0.001f;
                overlayBg.enabled = show;
                if (show) { if (overlayBg.sprite != bgOverrideTo) overlayBg.sprite = bgOverrideTo; overlayBg.color = new Color(bgOverrideTint.r, bgOverrideTint.g, bgOverrideTint.b, bgOverrideBlend); }
            }
            return;
        }
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
            if (lx >= propsEndLX) { nextPropLX = endLX + 1f; break; } // エンドロールでは構造物を置かない(文字が主役)
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

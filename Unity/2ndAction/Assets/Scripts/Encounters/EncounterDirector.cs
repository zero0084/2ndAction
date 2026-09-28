using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 共通Encounter Director(2026-09-27)。
//
// Stage → Distance Band → Encounter Pool → Intensity → Formation → Spawn Slot → Enemy の順に決めて、
// プレイヤーの前方(画面の外)に「1つのまとまった戦闘」を置く。敵を1体ずつ完全ランダムには出さない。
//
//  - どのステージを担当するかはStageEncounterProfile(Resources/Encounters)の有無だけで決まる。
//    ステージ固有の処理はProfile/Formationのデータ側にあり、このクラスは自然洞窟専用ではない。
//  - Intensity(Rest/Easy/Medium/Hard)は直前の履歴でWeightを変えて「戦闘の波」を作る
//    (Hardの連続/Restの連続を避け、Hardの後は休憩が出やすい)。Restは意図的な休憩区間。
//  - 直近のFormationは大きくWeightを下げる(絶対禁止ではない = 候補不足でも止まらない)。
//  - Formationの地形条件(穴が無い/平地/天井までの高さ/空中の空間/潜れる地面)を満たす場所にだけ出し、
//    狭い通路/広い空洞/長い直線でWeightを変える(相性)。
//  - 高速時は、出現位置(画面右端より先)・Encounter間隔・休憩・敵どうしの間隔を速度倍率で広げる
//    (プレイヤーの速度自体は変えない)。
//  - ボス戦中/ボス直前/CONTINUE直後の安全地帯では出さない。ボス戦後は休憩から再開する。
//  - マルチプレイでは敵を出すのはHOSTだけ(JOINは従来どおりHOSTから届く敵を表示する)。
[DefaultExecutionOrder(40)] // TerrainManager(既定0)が地面を先に生成した後に判断する
public class EncounterDirector : MonoBehaviour
{
    public static EncounterDirector Instance { get; private set; }

    public struct Member
    {
        public string enemyId;
        public EnemyAiTier tier;
        public EncounterSlotKind slot;
        public EncounterRoute route;
        public float x, y;
    }

    public class Record
    {
        public int index;
        public float distance;
        public string band;
        public EncounterIntensity intensity;
        public string formation;
        public string terrain;
        public readonly List<Member> members = new List<Member>();
        public string reason = "";
        public double anchorLogical, endLogical;
        // 分岐区間(RoutePair)の中身: 上ルート/下ルートそれぞれのFormationとIntensity
        public string upper = "", lower = "";
        public bool mirrored;
    }

    StageEncounterProfile profile;
    string profileStage;
    double nextAnchor = -1;          // 次のEncounterの基準点(論理X)
    bool paused, pausedForBoss;
    readonly List<EncounterIntensity> intensityHistory = new List<EncounterIntensity>();
    readonly List<string> formationHistory = new List<string>();
    // Encounterが占める範囲(論理X)とルート。障害物をここに置かない(上ルートの範囲は上ルートの障害物だけを避ける)。
    readonly List<(Vector2 range, EncounterRoute route)> spans = new List<(Vector2, EncounterRoute)>();
    double lastBranchForkLogical = double.NegativeInfinity; // 中身を決めた最後の分岐
    System.Random rng;
    int encounterIndex;

    // デバッグ: 指定Formationを強制
    public static string ForcedFormation;
    public static int ForcedRemaining;    // <0 = ずっと
    int debugSelect;
    // テスト用: 距離Bandの判定に足す距離(何千mも走らずに後半のBandを確認する)。通常は0。
    public static float DebugDistanceOffset;
    public static bool DebugGapLog; // テスト用: Gap Guardが置けない理由をログに出す
    // テスト用: Encounterを出した直後に呼ぶ(Formation/生成した敵/Slot)。
    public static System.Action<Record, EncounterFormation, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>> OnEncounterSpawned;

    // 統計(自動テスト/デバッグ)
    public readonly List<Record> Recent = new List<Record>();
    public readonly Dictionary<string, int> FormationCounts = new Dictionary<string, int>();
    public readonly int[] IntensityCounts = new int[4];
    public int SpawnedEnemies;
    public Record Last { get; private set; }
    public StageEncounterProfile Profile => profile;
    public EncounterDistanceBand CurrentBand { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[EncounterDirector]");
        DontDestroyOnLoad(go);
        go.AddComponent<EncounterDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ResetState(null);
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-encounterForce") { ForcedFormation = args[i + 1]; ForcedRemaining = -1; }
            if (args[i] == "-encounterSeed" && int.TryParse(args[i + 1], out int seed)) fixedSeed = seed;
        }
    }

    int fixedSeed = int.MinValue;

    void ResetState(StageEncounterProfile p)
    {
        profile = p;
        nextAnchor = -1;
        paused = pausedForBoss = false;
        baseTerrainAhead = baseCaveAhead = -1f;
        intensityHistory.Clear();
        formationHistory.Clear();
        spans.Clear();
        lastBranchForkLogical = double.NegativeInfinity;
        Recent.Clear();
        FormationCounts.Clear();
        for (int i = 0; i < IntensityCounts.Length; i++) IntensityCounts[i] = 0;
        SpawnedEnemies = 0;
        encounterIndex = 0;
        Last = null;
        CurrentBand = null;
        int seed = fixedSeed != int.MinValue ? fixedSeed : (WorldRng.IsDeterministic ? NetRunLauncher.ActiveRunSeed ^ 0x5eed : System.Environment.TickCount);
        rng = new System.Random(seed);
    }

    // ===================================================================== //
    // 外部から
    // ===================================================================== //

    // このステージの敵出現をDirectorが担当するか(TerrainManager/EnemyWallManagerが従来Spawnを止める判断に使う)。
    public static bool HandlesStage(string stageId)
    {
        var p = StageEncounterProfile.Find(stageId);
        return p != null && p.replacesChunkSpawns;
    }

    public static bool HandlesMilestoneWalls(string stageId)
    {
        var p = StageEncounterProfile.Find(stageId);
        return p != null && p.replacesMilestoneWalls;
    }

    // 障害物の配置側が、Encounterの範囲に障害物を重ねないために使う(シーン座標)。
    // 地面(一本道/下ルート)の障害物用。上ルートの障害物はIsInUpperEncounterSpan。
    public static bool IsInEncounterSpan(float sceneX, float margin) => InSpan(sceneX, margin, false);
    public static bool IsInUpperEncounterSpan(float sceneX, float margin) => InSpan(sceneX, margin, true);

    static bool InSpan(float sceneX, float margin, bool upper)
    {
        if (Instance == null || Instance.profile == null) return false;
        float lx = FloatingOrigin.ToLogical(sceneX);
        foreach (var s in Instance.spans)
        {
            if ((s.route == EncounterRoute.Upper) != upper) continue;
            if (lx >= s.range.x - margin && lx <= s.range.y + margin) return true;
        }
        return false;
    }

    // 上ルートの敵もDirectorが置くステージか(従来の上ルート用スポナーを止める判断)。
    public static bool HandlesUpperRoute(string stageId)
    {
        var p = StageEncounterProfile.Find(stageId);
        return p != null && p.replacesChunkSpawns && p.routeEncounters;
    }

    public static void ForceFormation(string id, int count = 1)
    {
        ForcedFormation = id;
        ForcedRemaining = count;
        if (Instance != null) Instance.nextAnchor = -1; // すぐ次の出現位置で出す
    }

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //

    void Update()
    {
        GameManager gm = GameManager.Instance;
        PlayerController pc = PlayerController.Instance;
        TerrainManager tm = TerrainManager.Instance;
        if (gm == null || pc == null || tm == null) return;
        if (!gm.HasStarted || gm.IsGameOver) return;

        string stage = gm.ActiveRunStageId;
        if (stage != profileStage)
        {
            profileStage = stage;
            ResetState(StageEncounterProfile.Find(stage));
            if (profile != null) Debug.Log($"[ENCOUNTER] Director active for stage={stage} bands={profile.bands.Count} stageFormations={profile.stageFormations.Count}");
        }
        if (profile == null || !profile.replacesChunkSpawns) return;
        if (NetCombat.SuppressLocalEnemySpawn) return; // JOIN: 敵はHOSTが出す
        if (tm.enemySpawnChance <= 0f) return;          // 敵の出現そのものを止めている(自動テスト等の既存の切り替え)

        double playerLogical = FloatingOrigin.ToLogical(pc.transform.position.x);
        float speed = SpeedScale(pc);
        float visibleAhead = VisibleAhead(pc);
        float ahead = Mathf.Max(profile.spawnAheadDistance * Mathf.Lerp(1f, speed, 0.5f), visibleAhead + profile.offscreenMargin);
        PruneSpans(playerLogical);
        ExtendGeneration(tm, ahead);

        bool bossPhase = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        bool pauseNow = bossPhase || gm.IsInSafeZone || gm.CountdownActive || pc.IsFinishing;
        if (pauseNow)
        {
            // 止めている間に出現位置がプレイヤーに追い越されないよう、前方へ送り続ける。
            if (!paused) { paused = true; pausedForBoss = bossPhase; Debug.Log($"[ENCOUNTER] paused ({(bossPhase ? "boss phase" : gm.IsInSafeZone ? "safe zone" : "countdown/finish")})"); }
            if (bossPhase) pausedForBoss = true;
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            return;
        }
        if (paused)
        {
            paused = false;
            double resume = playerLogical + ahead + (pausedForBoss ? profile.bossPostRest * GapScale(speed) : 0f);
            if (nextAnchor < resume) nextAnchor = resume;
            if (pausedForBoss) { intensityHistory.Add(EncounterIntensity.Rest); Debug.Log($"[ENCOUNTER] resumed after boss: rest {profile.bossPostRest:F0}m before the next encounter"); }
            pausedForBoss = false;
        }

        // 上下ルート分岐の中身は、分岐が出現範囲に入った時点で先に決めて出しておく(通常Encounterの進み具合を
        // 待つと決めるのが遅れ、中身がルートの奥へ押し出されて分岐の手前から見えなくなる)。
        if (profile.routeEncounters) PlanUpcomingBranch(tm, pc, gm, playerLogical, ahead, speed);

        if (nextAnchor < 0) nextAnchor = playerLogical + ahead;
        // 画面内(目の前)には絶対に出さない。
        if (nextAnchor < playerLogical + visibleAhead + 2.0) nextAnchor = playerLogical + ahead;

        int guard = 0;
        while (nextAnchor <= playerLogical + ahead && guard++ < 4)
        {
            float sceneAnchor = (float)(nextAnchor - FloatingOrigin.Offset);
            if (!tm.IsGenerated(sceneAnchor + 30f) || (tm.cave != null && tm.cave.Active && tm.cave.GeneratedEndX < sceneAnchor + 30f)) break;
            float runDistance = gm.MaxDistance + (float)(nextAnchor - playerLogical) + DebugDistanceOffset;
            if (runDistance < profile.noEncounterBeforeDistance)
            {
                nextAnchor += profile.noEncounterBeforeDistance - runDistance + 1f;
                continue;
            }
            if (DebugDistanceOffset == 0f && BossNear(runDistance))
            {
                nextAnchor += 20f;
                continue;
            }
            // 上下ルート分岐のあるステージ(荒野街道など): 分岐区間は上ルート/下ルートの組み合わせで1回として決め、
            // 分岐の手前(routeLead)は通常Encounterを置かずに空けておく(両ルートの中身を見て選ぶ区間)。
            float mainLimit = float.PositiveInfinity;
            if (profile.routeEncounters && tm.TryGetBranchAfter(sceneAnchor, out float fork, out float merge, out bool branchGenerated))
            {
                if (sceneAnchor >= fork - profile.routeLead)
                {
                    if (!branchGenerated || !tm.IsGenerated(merge + 2f)) break; // 分岐の地形ができるまで待つ
                    double forkLogical = FloatingOrigin.ToLogical(fork);
                    if (forkLogical > lastBranchForkLogical + 1.0)
                    {
                        lastBranchForkLogical = forkLogical;
                        float forkDistance = gm.MaxDistance + (float)(forkLogical - playerLogical) + DebugDistanceOffset;
                        if (DebugDistanceOffset != 0f || !BossNear(forkDistance)) PlanBranch(tm, pc, fork, merge, forkDistance, speed);
                    }
                    nextAnchor = FloatingOrigin.ToLogical(merge) + Range(profile.afterMergeGap) * GapScale(speed);
                    continue;
                }
                mainLimit = fork - profile.routeLead;
            }
            PlanAt(tm, pc, runDistance, speed, mainLimit);
        }
    }

    void PlanUpcomingBranch(TerrainManager tm, PlayerController pc, GameManager gm, double playerLogical, float ahead, float speed)
    {
        float px = pc.transform.position.x;
        if (!tm.TryGetBranchAfter(px, out float fork, out float merge, out bool generated) || !generated) return;
        double forkLogical = FloatingOrigin.ToLogical(fork);
        if (forkLogical <= lastBranchForkLogical + 1.0) return;              // もう決めた
        if (fork - px > ahead + profile.routeLead + 10f) return;             // まだ遠い
        if (!tm.IsGenerated(merge + 2f)) return;
        float forkDistance = gm.MaxDistance + (float)(forkLogical - playerLogical) + DebugDistanceOffset;
        lastBranchForkLogical = forkLogical;
        if (forkDistance < profile.noEncounterBeforeDistance) return;
        if (DebugDistanceOffset == 0f && BossNear(forkDistance)) return;
        PlanBranch(tm, pc, fork, merge, forkDistance, speed);
    }

    // 出現位置 + 最も長いFormationの幅ぶん先まで、地面(と洞窟の天井)を先に生成させる。
    // 地形条件(穴/坂/天井の高さ)を、実際に置く範囲で確かめてから出すため。基準値より小さくはしない。
    float baseTerrainAhead = -1f, baseCaveAhead = -1f;
    void ExtendGeneration(TerrainManager tm, float ahead)
    {
        if (baseTerrainAhead < 0f) baseTerrainAhead = tm.generateAheadDistance;
        float desired = ahead + TerrainProbe.Range + 4f; // 最も長いFormation(高速時の間隔込み)を調べられる範囲まで
        tm.generateAheadDistance = Mathf.Max(baseTerrainAhead, desired);
        if (tm.cave != null && tm.cave.Active)
        {
            if (baseCaveAhead < 0f) baseCaveAhead = tm.cave.generateAhead;
            tm.cave.generateAhead = Mathf.Max(baseCaveAhead, desired);
        }
    }

    bool BossNear(float runDistance)
    {
        BossManager bm = BossManager.Instance;
        if (bm == null) return false;
        float boss = bm.NextBossDistance;
        if (boss <= 0f) return false;
        return runDistance + 40f >= boss - profile.bossPreBuffer && runDistance <= boss + 5f;
    }

    static float SpeedScale(PlayerController pc) => Mathf.Max(1f, pc.SpeedRatio);

    float GapScale(float speed) => 1f + profile.gapSpeedScale * (speed - 1f);

    static float VisibleAhead(PlayerController pc)
    {
        Camera cam = Camera.main;
        if (cam == null) return 20f;
        float right = cam.transform.position.x + cam.orthographicSize * cam.aspect;
        return Mathf.Max(8f, right - pc.transform.position.x);
    }

    void PruneSpans(double playerLogical)
    {
        for (int i = spans.Count - 1; i >= 0; i--) if (spans[i].range.y < playerLogical - 40.0) spans.RemoveAt(i);
    }

    float R01() => (float)rng.NextDouble();
    float Range(float a, float b) => a + (b - a) * R01();
    float Range(Vector2 v) => Range(v.x, v.y);

    // ===================================================================== //
    // 1回のEncounterを決めて出す
    // ===================================================================== //

    void PlanAt(TerrainManager tm, PlayerController pc, float runDistance, float speed, float mainLimit = float.PositiveInfinity)
    {
        EncounterDistanceBand band = profile.BandFor(runDistance);
        CurrentBand = band;
        if (band == null) { DoRest(runDistance, null, speed, "no band"); return; }

        EncounterFormation forced = null;
        if (!string.IsNullOrEmpty(ForcedFormation) && ForcedRemaining != 0)
        {
            forced = profile.FindFormation(ForcedFormation);
            // 上下ルートの組み合わせは次の分岐で出す(それまでの一本道には何も置かない)
            if (forced != null && forced.routeMode == EncounterRouteMode.RoutePair) { DoRest(runDistance, band, speed, $"forced {forced.formationId}: waiting for the next branch", short_: true); return; }
            if (forced != null && forced.slots.Count == 0) { ConsumeForced(); DoRest(runDistance, band, speed, "forced rest"); return; }
        }

        EncounterIntensity intensity = forced != null
            ? (EncounterIntensity)Mathf.Clamp((int)EncounterIntensity.Medium, (int)forced.minIntensity, (int)forced.maxIntensity)
            : PickIntensity(band);
        if (intensity == EncounterIntensity.Rest) { DoRest(runDistance, band, speed, "wave"); return; }

        // 地形が合わない/直前と同じFormationしか選べない時は、少し先の地形で探し直す(最後の1回だけ連続を許す)。
        int shifts = forced != null ? 8 : 3;
        for (int shift = 0; shift < shifts; shift++)
        {
            bool allowRepeatLast = shift == shifts - 1;
            float sceneAnchor = (float)(nextAnchor - FloatingOrigin.Offset);
            var probe = new TerrainProbe(tm, profile, sceneAnchor) { LimitX = mainLimit };
            for (EncounterIntensity tryI = intensity; tryI >= EncounterIntensity.Easy; tryI--)
            {
                EncounterFormation f = forced ?? PickFormation(band, tryI, probe, speed, allowRepeatLast);
                if (f == null || (forced != null && !Fits(forced, probe, speed))) { if (forced != null) break; continue; }
                if (Spawn(tm, pc, band, f, tryI, probe, runDistance, speed)) { if (forced != null) ConsumeForced(); return; }
                if (forced != null) break;
            }
            nextAnchor += 6f; // この場所は条件に合わない(穴/狭い/坂) → 少し先で探し直す
        }
        DoRest(runDistance, band, speed, forced != null ? $"forced {forced.formationId} did not fit" : "no formation fits terrain", short_: true);
    }

    EncounterIntensity PickIntensity(EncounterDistanceBand band, float restScale = 1f)
    {
        float[] w = { Mathf.Max(0f, band.restWeight) * restScale, Mathf.Max(0f, band.easyWeight), Mathf.Max(0f, band.mediumWeight), Mathf.Max(0f, band.hardWeight) };
        int n = intensityHistory.Count;
        EncounterIntensity last = n > 0 ? intensityHistory[n - 1] : EncounterIntensity.Rest;
        EncounterIntensity prev = n > 1 ? intensityHistory[n - 2] : EncounterIntensity.Easy;
        if (n == 0) { w[0] = 0f; w[3] = 0f; } // 最初はEasy/Mediumから
        // 休憩を挟まない戦闘が続きすぎたら必ず休憩(呼吸)を入れる。
        int sinceRest = 0;
        for (int i = n - 1; i >= 0 && intensityHistory[i] != EncounterIntensity.Rest; i--) sinceRest++;
        if (n > 0 && sinceRest >= Mathf.Max(1, profile.maxEncountersWithoutRest)) return EncounterIntensity.Rest;
        switch (last)
        {
            case EncounterIntensity.Rest: w[0] *= profile.afterRestRestMultiplier; break;
            case EncounterIntensity.Easy: w[2] *= profile.afterEasyMediumMultiplier; break;
            case EncounterIntensity.Medium: w[3] *= profile.afterMediumHardMultiplier; break;
            case EncounterIntensity.Hard: w[3] *= profile.afterHardHardMultiplier; w[0] *= profile.afterHardRestMultiplier; break;
        }
        if (last == EncounterIntensity.Hard && prev == EncounterIntensity.Hard) w[3] = 0f;
        if (last == EncounterIntensity.Rest && prev == EncounterIntensity.Rest) w[0] = 0f;
        float sum = w[0] + w[1] + w[2] + w[3];
        if (sum <= 0f) return EncounterIntensity.Easy;
        float r = R01() * sum;
        for (int i = 0; i < 4; i++) { if (r < w[i]) return (EncounterIntensity)i; r -= w[i]; }
        return EncounterIntensity.Easy;
    }

    static void ConsumeForced()
    {
        if (ForcedRemaining > 0 && --ForcedRemaining == 0) ForcedFormation = null;
    }

    EncounterFormation PickFormation(EncounterDistanceBand band, EncounterIntensity intensity, TerrainProbe probe, float speed, bool allowRepeatLast)
    {
        string lastFormation = formationHistory.Count > 0 ? formationHistory[formationHistory.Count - 1] : null;
        var cands = new List<(EncounterFormation f, float w)>();
        float total = 0f;
        foreach (var fw in band.formations)
        {
            if (fw == null || fw.weight <= 0f) continue;
            EncounterFormation f = profile.FindFormation(fw.formationId);
            if (f == null || f.slots.Count == 0) continue;
            if (f.routeMode != EncounterRouteMode.Main) continue; // 上下ルートの組み合わせは分岐区間でだけ使う
            if (intensity < f.minIntensity || intensity > f.maxIntensity) continue;
            if (!Fits(f, probe, speed)) continue;
            if (!allowRepeatLast && f.formationId == lastFormation) continue;
            float w = fw.weight;
            // 地形との相性
            if (probe.Clearance < profile.narrowClearance) w *= f.narrowAffinity;
            else if (probe.Clearance >= profile.wideClearance) w *= f.wideAffinity;
            if (probe.FlatRun >= profile.straightLength) w *= f.straightAffinity;
            // 同じFormationの連続防止(絶対禁止ではない)
            int idx = formationHistory.LastIndexOf(f.formationId);
            if (idx >= 0)
            {
                int ago = formationHistory.Count - idx;
                w *= ago == 1 ? profile.repeatPenaltyLast : profile.repeatPenaltyOlder;
            }
            if (w <= 0f) continue;
            cands.Add((f, w));
            total += w;
        }
        if (cands.Count == 0) return null;
        float r = R01() * total;
        foreach (var c in cands) { if (r < c.w) return c.f; r -= c.w; }
        return cands[cands.Count - 1].f;
    }

    float Spacing(EncounterFormation f, float speed) => 1f + f.spacingSpeedScale * (speed - 1f);

    bool Fits(EncounterFormation f, TerrainProbe p, float speed)
    {
        float width = f.Width() * Spacing(f, speed);
        if (!p.CoversWidth(width)) return false;
        if (f.requiredHeight > 0f && p.MinClearance(width) < f.requiredHeight) return false;
        if (f.requiresGround && f.continuousGround && p.HasPit(width)) return false;
        if (f.requiresFlat && !p.AllFlat(width)) return false;
        if (f.requiresAir && p.AirBlocked) return false;   // 下ルート(上ルートの足場の下)には空中の敵を置く空間が無い
        if (f.requiresPit && !FindGap(f, p, speed, out _, out _)) return false;
        return true;
    }

    // Gap Guard: 基準点の先にある穴(幅maxPitWidth以下)を探す。穴の手前(敵+助走ぶん)と向こう岸(縁ぎりぎりで
    // 跳んだ時の着地点+敵ぶん)に別の穴が無いことも確かめる。上ルートには穴が無いので使わない。
    bool FindGap(EncounterFormation f, TerrainProbe p, float speed, out float pitStart, out float pitEnd)
    {
        pitStart = pitEnd = 0f;
        if (p.Upper) return false;
        float sp = Spacing(f, speed);
        float before = MaxPitOffset(f, EncounterPitAnchor.BeforePit) * sp + 1f;
        float from = Mathf.Max(f.pitSearchStart * sp, before), to = f.pitSearchEnd * sp;
        // 調査範囲の穴を手前から順に見て、前後の条件を満たす最初の穴を使う
        while (p.FindPit(from, to, out pitStart, out pitEnd))
        {
            float afterFar = Mathf.Max(pitEnd + MaxPitOffset(f, EncounterPitAnchor.AfterPit) * sp, pitStart + JumpReach() + f.pitLandingMargin) + 1.5f;
            bool ok = pitEnd - pitStart <= f.maxPitWidth
                && !p.HasPitBetween(pitStart - before, pitStart - 0.6f)   // 手前の敵と助走の区間に別の穴が無い
                && p.CoversX(afterFar) && afterFar <= p.LimitX
                && !p.HasPitBetween(pitEnd + 0.6f, afterFar);
            if (ok) return true;
            if (DebugGapLog) Debug.Log($"[ENCOUNTER][gap] pit {pitStart - p.AnchorX:F1}..{pitEnd - p.AnchorX:F1} w={pitEnd - pitStart:F1} beforeClear={!p.HasPitBetween(pitStart - before, pitStart - 0.6f)} covers={p.CoversX(afterFar)} limit={(afterFar <= p.LimitX)} afterClear={!p.HasPitBetween(pitEnd + 0.6f, afterFar)} afterFar={afterFar - pitEnd:F1}");
            from = pitEnd - p.AnchorX + 0.5f;
        }
        if (DebugGapLog) Debug.Log($"[ENCOUNTER][gap] no usable pit from {Mathf.Max(f.pitSearchStart * sp, before):F1} to {to:F1} (limit {p.LimitX - p.AnchorX:F1})");
        return false;
    }

    // 穴の縁ぎりぎりで跳んだ時に、踏み切りから着地までに進む距離(現在の走行速度×滞空時間)。
    static float JumpReach()
    {
        var pc = PlayerController.Instance;
        if (pc == null || pc.gravity <= 0f) return 6f;
        return pc.CurrentAutoRunSpeed * 2f * pc.jumpForce / pc.gravity;
    }

    static float MaxPitOffset(EncounterFormation f, EncounterPitAnchor kind)
    {
        float m = kind == EncounterPitAnchor.BeforePit ? f.pitMinBefore : 0f;
        foreach (var s in f.slots) if (s.pitAnchor == kind) m = Mathf.Max(m, s.xOffset + Mathf.Abs(s.jitter));
        return m;
    }

    // Gap GuardのSlotを実際の並び順(穴の手前の遠い方→近い方→穴の向こう)に並べる。
    static float PitOrder(EncounterSlot s) =>
        s.pitAnchor == EncounterPitAnchor.BeforePit ? -s.xOffset : s.pitAnchor == EncounterPitAnchor.AfterPit ? 1000f + s.xOffset : s.xOffset;

    // ---- 敵の選択 ----

    EnemyDefinition PickEnemy(EncounterDistanceBand band, EncounterSlot slot, out EncounterEnemyEntry entry)
    {
        entry = null;
        var all = new List<(EnemyDefinition d, EncounterEnemyEntry e)>();
        var pref = new List<(EnemyDefinition d, EncounterEnemyEntry e)>();
        foreach (var e in band.enemies)
        {
            if (e == null || e.weight <= 0f) continue;
            EnemyDefinition d = EnemyDatabase.FindById(e.enemyId);
            if (d == null || !EncounterSlots.Accepts(slot.kind, d)) continue;
            if (e.allowedSlots != null && e.allowedSlots.Count > 0 && !e.allowedSlots.Contains(slot.kind)) continue;
            all.Add((d, e));
            if (slot.preferEnemyIds != null && System.Array.IndexOf(slot.preferEnemyIds, e.enemyId) >= 0) pref.Add((d, e));
        }
        var pool = pref.Count > 0 ? pref : (slot.preferOnly && slot.preferEnemyIds != null && slot.preferEnemyIds.Length > 0 ? null : all);
        if (pool == null || pool.Count == 0) return null;
        float total = 0f;
        foreach (var c in pool) total += c.e.weight;
        float r = R01() * total;
        foreach (var c in pool) { if (r < c.e.weight) { entry = c.e; return c.d; } r -= c.e.weight; }
        entry = pool[pool.Count - 1].e;
        return pool[pool.Count - 1].d;
    }

    EnemyAiTier PickTier(EncounterTierWeights w, EncounterEnemyEntry e)
    {
        float t0 = Mathf.Max(0f, w.t0), t1 = Mathf.Max(0f, w.t1), t2 = Mathf.Max(0f, w.t2);
        float sum = t0 + t1 + t2;
        EnemyAiTier t = EnemyAiTier.T0;
        if (sum > 0f)
        {
            float r = R01() * sum;
            t = r < t0 ? EnemyAiTier.T0 : (r < t0 + t1 ? EnemyAiTier.T1 : EnemyAiTier.T2);
        }
        if (t < e.minTier) t = e.minTier;
        if (t > e.maxTier) t = e.maxTier;
        return t;
    }

    // ---- 配置 ----

    struct Planned
    {
        public EnemyDefinition def;
        public Vector2 pos;
        public EnemyAiTier tier;
        public EnemyBehaviorKind beh;
        public EncounterSlotKind slot;
        public EncounterRoute route;
    }

    // Formationの各Slotに置く敵と位置を決める(まだ出さない)。null = 穴が見つからない等で置けない。
    List<Planned> PlanSlots(TerrainManager tm, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, TerrainProbe probe, float speed, EncounterRoute route)
    {
        float spacing = Spacing(f, speed);
        float anchor = probe.AnchorX;
        var tiers = band.TiersFor(intensity);
        float pitStart = 0f, pitEnd = 0f;
        if (f.requiresPit && !FindGap(f, probe, speed, out pitStart, out pitEnd)) return null;
        float afterMin = pitStart + JumpReach() + f.pitLandingMargin;
        var slots = new List<EncounterSlot>(f.slots);
        if (f.requiresPit) slots.Sort((a, b) => PitOrder(a).CompareTo(PitOrder(b)));
        else slots.Sort((a, b) => a.xOffset.CompareTo(b.xOffset));
        float lastGroundX = float.NegativeInfinity;
        var list = new List<Planned>();
        foreach (var slot in slots)
        {
            if (slot.minIntensity > intensity) continue;
            EnemyDefinition def = PickEnemy(band, slot, out EncounterEnemyEntry entry);
            if (def == null) continue;
            float x;
            switch (slot.pitAnchor)
            {
                // 穴の手前: 縁からpitMinBefore以上手前(敵を倒してから助走して跳べる。被弾のノックバックは後ろ向き)
                case EncounterPitAnchor.BeforePit: x = pitStart - Mathf.Max(f.pitMinBefore, slot.xOffset) * spacing - Range(0f, Mathf.Abs(slot.jitter)); break;
                // 穴の向こう: 縁ぎりぎりで跳んでも着地点より先(跳んだら必ずぶつかる、にならない)
                case EncounterPitAnchor.AfterPit: x = Mathf.Max(pitEnd + slot.xOffset * spacing, afterMin) + Range(0f, Mathf.Abs(slot.jitter)); break;
                default: x = anchor + slot.xOffset * spacing + Range(-slot.jitter, slot.jitter); break;
            }
            if (x > probe.LimitX) continue;
            Vector2 pos;
            if (EncounterSlots.IsAir(slot.kind))
            {
                if (!PlaceAir(tm, slot.kind, x, route, out pos)) continue;
            }
            else if (slot.kind == EncounterSlotKind.Burrow)
            {
                if (route == EncounterRoute.Upper || !PlaceBurrow(tm, x, out pos)) continue;
            }
            else
            {
                if (x < lastGroundX + f.minGroundGap) x = lastGroundX + f.minGroundGap;
                if (!PlaceGround(tm, x, route, probe.LimitX, out pos)) continue;
                if (pos.x < lastGroundX + f.minGroundGap - 0.01f) continue; // 手前へ探し直した結果、前の敵に近づきすぎた
                if (slot.pitAnchor == EncounterPitAnchor.BeforePit && pos.x > pitStart - f.pitMinBefore * spacing * 0.9f) continue; // 穴へ寄りすぎた
                lastGroundX = pos.x;
            }
            EnemyAiTier tier = PickTier(tiers, entry);
            EnemyBehaviorKind beh = def.behaviorKind;
            if (entry.tierDrivesMelee) beh = tier == EnemyAiTier.T0 ? EnemyBehaviorKind.None : EnemyBehaviorKind.StationaryMelee;
            list.Add(new Planned { def = def, pos = pos, tier = tier, beh = beh, slot = slot.kind, route = route });
        }
        return list;
    }

    void SpawnPlanned(TerrainManager tm, List<Planned> list, Record rec, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned, ref float minX, ref float maxX)
    {
        foreach (var s in list)
        {
            GameObject go = tm.SpawnEncounterEnemy(s.def, s.pos, s.tier, s.beh);
            if (go == null) continue;
            // 上ルートの敵はノックバック/打ち上げの着地も上ルートの面で行う(下ルートへ落ちない)
            if (s.route == EncounterRoute.Upper) { var ec = go.GetComponent<EnemyController>(); if (ec != null) ec.onUpperRoute = true; }
            spawned.Add((go, s.def, s.slot));
            SpawnedEnemies++;
            minX = Mathf.Min(minX, s.pos.x);
            maxX = Mathf.Max(maxX, s.pos.x);
            rec.members.Add(new Member { enemyId = s.def.enemyId, tier = s.tier, slot = s.slot, route = s.route, x = FloatingOrigin.ToLogical(s.pos.x), y = s.pos.y });
        }
    }

    bool Spawn(TerrainManager tm, PlayerController pc, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, TerrainProbe probe, float runDistance, float speed)
    {
        var plan = PlanSlots(tm, band, f, intensity, probe, speed, EncounterRoute.Main);
        if (plan == null || plan.Count == 0) return false;
        float anchor = probe.AnchorX;
        var rec = new Record { index = ++encounterIndex, distance = runDistance, band = band.bandName, intensity = intensity, formation = f.formationId, terrain = probe.Describe(profile) };
        var spawnedGos = new List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>();
        float minX = anchor, endX = anchor;
        SpawnPlanned(tm, plan, rec, spawnedGos, ref minX, ref endX);
        rec.anchorLogical = FloatingOrigin.ToLogical(minX);
        rec.endLogical = FloatingOrigin.ToLogical(endX);
        spans.Add((new Vector2((float)rec.anchorLogical - 2f, (float)rec.endLogical + 2f), EncounterRoute.Main));
        Commit(rec);
        OnEncounterSpawned?.Invoke(rec, f, spawnedGos);
        nextAnchor = rec.endLogical + Range(GapRange(intensity)) * GapScale(speed);
        return true;
    }

    Vector2 GapRange(EncounterIntensity i) =>
        i == EncounterIntensity.Hard ? profile.gapAfterHard : i == EncounterIntensity.Medium ? profile.gapAfterMedium : profile.gapAfterEasy;

    bool PlaceGround(TerrainManager tm, float x, EncounterRoute route, float limitX, out Vector2 pos)
    {
        pos = default;
        // 分岐区間(穴の多い下ルート)では、穴にかかったSlotを少し手前にも探す(一本道は従来どおり前だけ)
        int kMin = route == EncounterRoute.Main ? 0 : -4;
        for (int kk = 0; kk < 5 - kMin; kk++)
        {
            int k = kk < 5 ? kk : -(kk - 4);
            float sx = x + k * 0.5f;
            if (sx > limitX) return false;
            if (route == EncounterRoute.Upper)
            {
                // 上ルートの面の上(前後にも面が続いている所)。上下ルートの間の空中には置かない。
                float? s = tm.GetSkyHeightAt(sx);
                if (!s.HasValue || !tm.GetSkyHeightAt(sx - 0.8f).HasValue || !tm.GetSkyHeightAt(sx + 0.8f).HasValue) continue;
                pos = new Vector2(sx, s.Value);
                return true;
            }
            float? gy = tm.GetHeightAt(sx);
            if (!gy.HasValue || tm.IsNearPit(sx, 1.2f)) continue;
            pos = new Vector2(sx, gy.Value);
            return true;
        }
        return false;
    }

    bool PlaceAir(TerrainManager tm, EncounterSlotKind kind, float x, EncounterRoute route, out Vector2 pos)
    {
        pos = default;
        if (route == EncounterRoute.Lower) return false; // 下ルートの上は上ルートの足場(空中の敵を置く空間が無い)
        if (route == EncounterRoute.Upper)
        {
            float? s = tm.GetSkyHeightAt(x);
            if (!s.HasValue) return false;
            pos = new Vector2(x, s.Value + Range(kind == EncounterSlotKind.AirHigh ? profile.airHighHeight : profile.airLowHeight));
            return true;
        }
        float ground = tm.GetGroundLineAt(x);
        Vector2 h = kind == EncounterSlotKind.AirHigh ? profile.airHighHeight : profile.airLowHeight;
        float y = ground + Range(h);
        float? ceil = tm.GetEffectiveCeilingHeightAt(x);
        if (ceil.HasValue)
        {
            float maxY = ceil.Value - profile.airCeilingMargin;
            if (maxY < ground + profile.airLowHeight.x) return false; // 空中の空間が無い
            y = Mathf.Min(y, maxY);
        }
        pos = new Vector2(x, y);
        return true;
    }

    bool PlaceBurrow(TerrainManager tm, float x, out Vector2 pos)
    {
        pos = default;
        for (int k = 0; k < 6; k++)
        {
            float sx = x + k * 0.75f;
            float? gy = tm.GetHeightAt(sx);
            if (!gy.HasValue || tm.IsNearPit(sx, 3f)) continue;
            bool flat = true;
            for (float d = -1.5f; d <= 1.5f && flat; d += 0.5f)
            {
                float? g2 = tm.GetHeightAt(sx + d);
                if (!g2.HasValue || Mathf.Abs(g2.Value - gy.Value) > 0.05f) flat = false;
            }
            if (!flat) continue;
            pos = new Vector2(sx, gy.Value);
            return true;
        }
        return false;
    }

    // ===================================================================== //
    // 上下ルート分岐(荒野街道など): 上ルート/下ルートに別々の内容を置き、どちらを走るかを選ばせる
    // ===================================================================== //

    void PlanBranch(TerrainManager tm, PlayerController pc, float fork, float merge, float runDistance, float speed)
    {
        EncounterDistanceBand band = profile.BandFor(runDistance);
        CurrentBand = band;
        float ramp = tm.BranchRampLength;
        // 上下とも同じX範囲(坂を上り切った所から)に置き、ルートの頭に寄せる → 分岐の手前から両方の中身を見比べられる。
        float regionStart = fork + ramp + profile.routeEdgeMargin;
        float regionEnd = merge - ramp - profile.routeEdgeMargin;
        regionStart = Mathf.Max(regionStart, pc.transform.position.x + VisibleAhead(pc) + 1f); // 目の前には出さない
        var rec = new Record { index = ++encounterIndex, distance = runDistance, band = band != null ? band.bandName : "-",
            terrain = $"branch fork={FloatingOrigin.ToLogical(fork):F0} merge={FloatingOrigin.ToLogical(merge):F0}" };
        rec.anchorLogical = FloatingOrigin.ToLogical(regionStart);
        rec.endLogical = FloatingOrigin.ToLogical(regionEnd);
        if (band == null || regionEnd - regionStart < 10f)
        {
            rec.intensity = EncounterIntensity.Rest; rec.formation = "rest"; rec.reason = band == null ? "no band" : "branch already too close";
            Commit(rec);
            return;
        }

        EncounterFormation pair = null;
        EncounterIntensity intensity = EncounterIntensity.Rest;
        EncounterFormation forced = !string.IsNullOrEmpty(ForcedFormation) && ForcedRemaining != 0 ? profile.FindFormation(ForcedFormation) : null;
        if (forced != null && forced.routeMode == EncounterRouteMode.RoutePair)
        {
            pair = forced;
            intensity = forced.maxIntensity;
            ConsumeForced();
        }
        else
        {
            EncounterIntensity want = PickIntensity(band, profile.branchRestScale);
            for (EncounterIntensity i = want; pair == null && i >= EncounterIntensity.Rest; i--)
            {
                pair = PickRoutePair(band, i);
                if (pair != null) intensity = i;
            }
        }
        rec.intensity = intensity;
        if (pair == null)
        {
            rec.formation = "rest"; rec.reason = "branch: both routes rest";
            Commit(rec);
            return;
        }
        rec.formation = pair.formationId;

        // 上下を入れ替えてもよい組み合わせは半々で入れ替える(「上=楽」に固定しない)。入れ替えると置けない時は元の向き。
        bool mirror = pair.allowMirror && R01() < 0.5f;
        List<Planned> up = null, low = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var upSide = mirror ? pair.lowerSide : pair.upperSide;
            var lowSide = mirror ? pair.upperSide : pair.lowerSide;
            up = PlanSide(tm, band, upSide, EncounterRoute.Upper, regionStart, regionEnd, speed);
            rec.upper = DescribeSide(upSide, up, usedFormation);
            low = PlanSide(tm, band, lowSide, EncounterRoute.Lower, regionStart, regionEnd, speed);
            rec.lower = DescribeSide(lowSide, low, usedFormation);
            if ((up != null && low != null) || !mirror) break;
            mirror = false;
        }
        rec.mirrored = mirror;
        if (up == null) { rec.reason += " upper-nofit"; up = new List<Planned>(); }
        if (low == null) { rec.reason += " lower-nofit"; low = new List<Planned>(); }

        var spawnedGos = new List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>();
        float uMin = float.PositiveInfinity, uMax = float.NegativeInfinity, lMin = float.PositiveInfinity, lMax = float.NegativeInfinity;
        SpawnPlanned(tm, up, rec, spawnedGos, ref uMin, ref uMax);
        SpawnPlanned(tm, low, rec, spawnedGos, ref lMin, ref lMax);
        if (uMax >= uMin) spans.Add((new Vector2(FloatingOrigin.ToLogical(uMin) - 2f, FloatingOrigin.ToLogical(uMax) + 2f), EncounterRoute.Upper));
        if (lMax >= lMin) spans.Add((new Vector2(FloatingOrigin.ToLogical(lMin) - 2f, FloatingOrigin.ToLogical(lMax) + 2f), EncounterRoute.Lower));
        Commit(rec);
        OnEncounterSpawned?.Invoke(rec, pair, spawnedGos);
    }

    // 片方のルートの中身。ルートの頭(routeFrontLoad以内)で地形条件に合う置き場所を探す。
    // Rest/"rest" = 何も置かない(空リスト)。置けない = null。
    string usedFormation = "";
    List<Planned> PlanSide(TerrainManager tm, EncounterDistanceBand band, EncounterRouteSide side, EncounterRoute route, float regionStart, float regionEnd, float speed)
    {
        usedFormation = side != null ? side.formationId : "rest";
        if (side == null || side.intensity == EncounterIntensity.Rest || string.IsNullOrEmpty(side.formationId) || side.formationId == "rest") return new List<Planned>();
        EncounterFormation f = profile.FindFormation(side.formationId);
        if (f == null || f.slots.Count == 0 || f.routeMode != EncounterRouteMode.Main) return new List<Planned>();
        usedFormation = f.formationId;
        var plan = PlanSideWith(tm, band, f, side.intensity, route, regionStart, regionEnd, speed);
        if (plan != null) return plan;
        // 置けない(下ルートの穴など) → 間隔の広いFormationで同じ強さを試す(空中の敵だけのFormationは除く)
        EncounterFormation fb = string.IsNullOrEmpty(profile.routeFallbackFormation) ? null : profile.FindFormation(profile.routeFallbackFormation);
        if (fb != null && fb != f && fb.slots.Count > 0)
        {
            plan = PlanSideWith(tm, band, fb, side.intensity, route, regionStart, regionEnd, speed);
            if (plan != null) usedFormation = fb.formationId + "(fallback)";
        }
        return plan;
    }

    List<Planned> PlanSideWith(TerrainManager tm, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, EncounterRoute route, float regionStart, float regionEnd, float speed)
    {
        float lastTry = Mathf.Min(regionEnd - 4f, regionStart + profile.routeFrontLoad);
        for (float a = regionStart; a <= lastTry; a += 2f)
        {
            var probe = new TerrainProbe(tm, profile, a, route == EncounterRoute.Upper) { LimitX = regionEnd, AirBlocked = route == EncounterRoute.Lower };
            if (!Fits(f, probe, speed)) continue;
            var plan = PlanSlots(tm, band, f, intensity, probe, speed, route);
            if (plan == null || plan.Count == 0) continue;
            // 先頭の敵がルートの頭に居ること(分岐の手前から見える)。穴の手前に置くGap Guardなどで奥へずれた時は探し直す。
            float first = float.PositiveInfinity;
            foreach (var m in plan) first = Mathf.Min(first, m.pos.x);
            if (first > regionStart + profile.routeFrontLoad + 2f) continue;
            return plan;
        }
        return null;
    }

    static string DescribeSide(EncounterRouteSide side, List<Planned> plan, string used)
    {
        if (side == null || side.formationId == "rest" || side.intensity == EncounterIntensity.Rest) return "rest";
        return plan == null ? $"{side.formationId}/{side.intensity} (did not fit)" : $"{used}/{side.intensity} x{plan.Count}";
    }

    EncounterFormation PickRoutePair(EncounterDistanceBand band, EncounterIntensity intensity)
    {
        var cands = new List<(EncounterFormation f, float w)>();
        float total = 0f;
        foreach (var fw in band.formations)
        {
            if (fw == null || fw.weight <= 0f) continue;
            EncounterFormation f = profile.FindFormation(fw.formationId);
            if (f == null || f.routeMode != EncounterRouteMode.RoutePair) continue;
            if (intensity < f.minIntensity || intensity > f.maxIntensity) continue;
            float w = fw.weight;
            int idx = formationHistory.LastIndexOf(f.formationId);
            if (idx >= 0) w *= formationHistory.Count - idx == 1 ? profile.repeatPenaltyLast : profile.repeatPenaltyOlder;
            if (w <= 0f) continue;
            cands.Add((f, w));
            total += w;
        }
        if (cands.Count == 0) return null;
        float r = R01() * total;
        foreach (var c in cands) { if (r < c.w) return c.f; r -= c.w; }
        return cands[cands.Count - 1].f;
    }

    void DoRest(float runDistance, EncounterDistanceBand band, float speed, string reason, bool short_ = false)
    {
        var rec = new Record { index = ++encounterIndex, distance = runDistance, band = band != null ? band.bandName : "-", intensity = EncounterIntensity.Rest, formation = "rest", reason = reason };
        float len = short_ ? 10f : Range(profile.restLength) * GapScale(speed);
        rec.anchorLogical = nextAnchor;
        rec.endLogical = nextAnchor + len;
        if (!short_) Commit(rec);
        else Debug.Log($"[ENCOUNTER] skip {len:F0}m at d={runDistance:F0} ({reason})");
        nextAnchor += len;
    }

    void Commit(Record rec)
    {
        intensityHistory.Add(rec.intensity);
        if (intensityHistory.Count > 8) intensityHistory.RemoveAt(0);
        if (rec.intensity != EncounterIntensity.Rest)
        {
            formationHistory.Add(rec.formation);
            while (formationHistory.Count > Mathf.Max(1, profile.historySize)) formationHistory.RemoveAt(0);
        }
        IntensityCounts[(int)rec.intensity]++;
        FormationCounts[rec.formation] = FormationCounts.TryGetValue(rec.formation, out int c) ? c + 1 : 1;
        Last = rec;
        Recent.Add(rec);
        if (Recent.Count > 400) Recent.RemoveAt(0);
        var sb = new StringBuilder();
        foreach (var m in rec.members) sb.Append($"{m.enemyId}({m.tier},{m.slot}{(m.route != EncounterRoute.Main ? "," + m.route : "")}) ");
        if (rec.upper != "" || rec.lower != "") sb.Append($" UPPER=[{rec.upper}] LOWER=[{rec.lower}]{(rec.mirrored ? " mirrored" : "")}");
        Debug.Log($"[ENCOUNTER] #{rec.index} d={rec.distance:F0} band={rec.band} intensity={rec.intensity} formation={rec.formation} terrain=[{rec.terrain}] span={rec.endLogical - rec.anchorLogical:F1}m members={rec.members.Count} [{sb.ToString().Trim()}]{(rec.reason != "" ? " reason=" + rec.reason : "")}");
    }

    // ===================================================================== //
    // 地形の調査(Formationの地形条件/相性)
    // ===================================================================== //

    class TerrainProbe
    {
        const float Step = 0.5f;
        public const float Range = 64f;
        public readonly float AnchorX;
        readonly float[] ground;      // NaN = 穴
        readonly float[] clearance;   // 天井までの高さ(天井なし=+∞)
        readonly bool[] flat;
        readonly float generatedEnd;
        public float Clearance { get; }
        public float FlatRun { get; }
        // 上ルートの面を調べる(荒野街道の分岐区間)。穴=上ルートの面が無い所。
        public readonly bool Upper;
        // Formationがこれより先へはみ出してはいけないX(分岐の手前/ルートの終わり)。
        public float LimitX = float.PositiveInfinity;
        // 空中の敵を置く空間が無い(下ルートの上は上ルートの足場)。
        public bool AirBlocked;

        public TerrainProbe(TerrainManager tm, StageEncounterProfile prof, float anchorX, bool upper = false)
        {
            AnchorX = anchorX;
            Upper = upper;
            int n = Mathf.CeilToInt(Range / Step) + 1;
            ground = new float[n]; clearance = new float[n]; flat = new bool[n];
            generatedEnd = tm.GeneratedEndX;
            if (tm.cave != null && tm.cave.Active) generatedEnd = Mathf.Min(generatedEnd, tm.cave.GeneratedEndX);
            for (int i = 0; i < n; i++)
            {
                float x = anchorX - 1f + i * Step;
                if (upper)
                {
                    float? sy = tm.GetSkyHeightAt(x);
                    ground[i] = sy.HasValue ? sy.Value : float.NaN;
                    clearance[i] = float.PositiveInfinity;
                    float? s2 = tm.GetSkyHeightAt(x + 0.25f);
                    flat[i] = sy.HasValue && s2.HasValue && Mathf.Abs(s2.Value - sy.Value) < 0.02f;
                    continue;
                }
                float? g = tm.GetHeightAt(x);
                ground[i] = g.HasValue ? g.Value : float.NaN;
                float? c = tm.GetEffectiveCeilingHeightAt(x);
                float gl = g.HasValue ? g.Value : tm.GetGroundLineAt(x);
                clearance[i] = c.HasValue ? c.Value - gl : float.PositiveInfinity;
                flat[i] = g.HasValue && Mathf.Abs(tm.GetSlopeAngleAt(x)) < 1f;
            }
            Clearance = MinClearance(12f);
            float run = 0f;
            for (int i = 2; i < n && flat[i]; i++) run += Step;
            FlatRun = run;
        }

        int Count(float width) => Mathf.Min(ground.Length, Mathf.CeilToInt((width + 2f) / Step) + 1);
        public bool CoversWidth(float width) => AnchorX + width + 1f <= generatedEnd && width + 2f <= Range && AnchorX + width <= LimitX;
        int Idx(float x) => Mathf.RoundToInt((x - AnchorX + 1f) / Step);
        float X(int i) => AnchorX - 1f + i * Step;
        public bool CoversX(float x) => x <= generatedEnd - 0.5f && Idx(x) < ground.Length;
        public bool HasPitBetween(float x0, float x1)
        {
            int i0 = Mathf.Max(0, Idx(x0)), i1 = Mathf.Min(ground.Length - 1, Idx(x1));
            for (int i = i0; i <= i1; i++) if (float.IsNaN(ground[i])) return true;
            return false;
        }
        // 基準点からfrom〜to(m)の範囲で始まる最初の穴(縁から縁)。穴が調査範囲の外まで続く時はfalse。
        public bool FindPit(float fromOffset, float toOffset, out float start, out float end)
        {
            start = end = 0f;
            int i0 = Mathf.Max(1, Idx(AnchorX + fromOffset)), i1 = Mathf.Min(ground.Length - 2, Idx(AnchorX + toOffset));
            for (int i = i0; i <= i1; i++)
            {
                if (!float.IsNaN(ground[i]) || float.IsNaN(ground[i - 1])) continue;
                int j = i;
                while (j < ground.Length && float.IsNaN(ground[j])) j++;
                if (j >= ground.Length) return false;
                start = X(i) - Step * 0.5f;
                end = X(j) - Step * 0.5f;
                return true;
            }
            return false;
        }
        public bool HasPit(float width) { int n = Count(width); for (int i = 0; i < n; i++) if (float.IsNaN(ground[i])) return true; return false; }
        public bool AllFlat(float width) { int n = Count(width); for (int i = 0; i < n; i++) if (!flat[i]) return false; return true; }
        public float MinClearance(float width) { int n = Count(width); float m = float.PositiveInfinity; for (int i = 0; i < n; i++) m = Mathf.Min(m, clearance[i]); return m; }

        public string Describe(StageEncounterProfile p)
        {
            string kind = float.IsPositiveInfinity(Clearance) ? "open" : Clearance < p.narrowClearance ? "narrow" : Clearance >= p.wideClearance ? "wide" : "normal";
            return $"{kind} clear={(float.IsPositiveInfinity(Clearance) ? "inf" : Clearance.ToString("F1"))} flatRun={FlatRun:F0}{(FlatRun >= p.straightLength ? " straight" : "")}";
        }
    }

    // ===================================================================== //
    // デバッグ表示(Debug Mode) + 指定Formationの強制
    // ===================================================================== //

    GUIStyle style, btn, btnSmall;
    static bool debugExpanded;

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.DebugMode || !gm.HasStarted || profile == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            style.normal.textColor = Color.white;
            btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            btnSmall = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        }
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        // 2026-09-27 - プレイ画面(敵が来る右側)を遮らないよう、既定は右下(一時停止ボタンの上)に1行だけ。
        // タップで詳細/FORCEを開く(上に向かって広がる)。
        Rect safe = Screen.safeArea;
        float right = w - (Screen.width - safe.xMax) / s - 8f, bottom = h - safe.yMin / s - 70f;
        string line = Last != null ? $"ENC #{Last.index} {Last.intensity.ToString()[0]} {Last.formation} {(paused ? "PAUSED" : "")}" : $"ENC {(paused ? "PAUSED" : "-")}";
        Rect lineRect = new Rect(right - 300f, bottom - 26f, 300f, 26f);
        if (GUI.Button(lineRect, (debugExpanded ? "▼ " : "▲ ") + line, btnSmall)) debugExpanded = !debugExpanded;
        if (!debugExpanded) { GUI.matrix = prev; return; }

        var r = new Rect(right - 460f, lineRect.y - 6f - 290f - 46f, 460f, 290f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var sb = new StringBuilder();
        sb.Append($"<b>ENCOUNTER</b> stage={profile.stageId} dist={gm.MaxDistance:F0}m band={(CurrentBand != null ? CurrentBand.bandName : "-")}{(paused ? " <color=#ffb070>PAUSED</color>" : "")}\n");
        if (Last != null)
        {
            sb.Append($"#{Last.index} {Last.intensity} <b>{Last.formation}</b> d={Last.distance:F0} [{Last.terrain}]\n");
            if (Last.upper != "" || Last.lower != "") sb.Append($"  UPPER: {Last.upper}\n  LOWER: {Last.lower}{(Last.mirrored ? " (mirrored)" : "")}\n");
            foreach (var m in Last.members) sb.Append($"  {m.enemyId} {m.tier} {m.slot}{(m.route != EncounterRoute.Main ? " " + m.route : "")}\n");
        }
        sb.Append("history: ");
        for (int i = Mathf.Max(0, Recent.Count - 6); i < Recent.Count; i++) sb.Append(Recent[i].formation).Append('/').Append(Recent[i].intensity.ToString()[0]).Append(' ');
        sb.Append($"\nR/E/M/H = {IntensityCounts[0]}/{IntensityCounts[1]}/{IntensityCounts[2]}/{IntensityCounts[3]}  spawned={SpawnedEnemies}");
        GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, r.height - 8f), sb.ToString(), style);

        // 指定Formationを強制(次の出現位置で1回)
        var ids = AllFormationIds();
        if (ids.Count > 0)
        {
            debugSelect = Mathf.Clamp(debugSelect, 0, ids.Count - 1);
            float by = r.yMax + 6f;
            if (GUI.Button(new Rect(r.x, by, 44f, 40f), "<", btn)) debugSelect = (debugSelect + ids.Count - 1) % ids.Count;
            GUI.Label(new Rect(r.x + 50f, by + 8f, 210f, 30f), ids[debugSelect], style);
            if (GUI.Button(new Rect(r.x + 262f, by, 44f, 40f), ">", btn)) debugSelect = (debugSelect + 1) % ids.Count;
            if (GUI.Button(new Rect(r.x + 312f, by, 118f, 40f), "FORCE", btn)) ForceFormation(ids[debugSelect], 1);
        }
        GUI.matrix = prev;
    }

    List<string> AllFormationIds()
    {
        var ids = new List<string>();
        if (profile == null) return ids;
        foreach (var b in profile.bands) foreach (var fw in b.formations) if (fw != null && !ids.Contains(fw.formationId)) ids.Add(fw.formationId);
        foreach (var f in profile.stageFormations) if (f != null && !ids.Contains(f.formationId)) ids.Add(f.formationId);
        if (!ids.Contains("rest")) ids.Add("rest");
        return ids;
    }
}

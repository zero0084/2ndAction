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
    }

    StageEncounterProfile profile;
    string profileStage;
    double nextAnchor = -1;          // 次のEncounterの基準点(論理X)
    bool paused, pausedForBoss;
    readonly List<EncounterIntensity> intensityHistory = new List<EncounterIntensity>();
    readonly List<string> formationHistory = new List<string>();
    readonly List<Vector2> spans = new List<Vector2>(); // Encounterが占める範囲(論理X)。障害物をここに置かない
    System.Random rng;
    int encounterIndex;

    // デバッグ: 指定Formationを強制
    public static string ForcedFormation;
    public static int ForcedRemaining;    // <0 = ずっと
    int debugSelect;
    // テスト用: 距離Bandの判定に足す距離(何千mも走らずに後半のBandを確認する)。通常は0。
    public static float DebugDistanceOffset;
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

    // 障害物の配置側が、Encounterの範囲に大きな障害物を重ねないために使う(シーン座標)。
    public static bool IsInEncounterSpan(float sceneX, float margin)
    {
        if (Instance == null || Instance.profile == null) return false;
        float lx = FloatingOrigin.ToLogical(sceneX);
        foreach (var s in Instance.spans) if (lx >= s.x - margin && lx <= s.y + margin) return true;
        return false;
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
            PlanAt(tm, pc, runDistance, speed);
        }
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
        for (int i = spans.Count - 1; i >= 0; i--) if (spans[i].y < playerLogical - 40.0) spans.RemoveAt(i);
    }

    float R01() => (float)rng.NextDouble();
    float Range(float a, float b) => a + (b - a) * R01();
    float Range(Vector2 v) => Range(v.x, v.y);

    // ===================================================================== //
    // 1回のEncounterを決めて出す
    // ===================================================================== //

    void PlanAt(TerrainManager tm, PlayerController pc, float runDistance, float speed)
    {
        EncounterDistanceBand band = profile.BandFor(runDistance);
        CurrentBand = band;
        if (band == null) { DoRest(runDistance, null, speed, "no band"); return; }

        EncounterFormation forced = null;
        if (!string.IsNullOrEmpty(ForcedFormation) && ForcedRemaining != 0)
        {
            forced = profile.FindFormation(ForcedFormation);
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
            var probe = new TerrainProbe(tm, profile, sceneAnchor);
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

    EncounterIntensity PickIntensity(EncounterDistanceBand band)
    {
        float[] w = { Mathf.Max(0f, band.restWeight), Mathf.Max(0f, band.easyWeight), Mathf.Max(0f, band.mediumWeight), Mathf.Max(0f, band.hardWeight) };
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
        return true;
    }

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

    bool Spawn(TerrainManager tm, PlayerController pc, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, TerrainProbe probe, float runDistance, float speed)
    {
        float spacing = Spacing(f, speed);
        float anchor = probe.AnchorX;
        var tiers = band.TiersFor(intensity);
        var rec = new Record { index = ++encounterIndex, distance = runDistance, band = band.bandName, intensity = intensity, formation = f.formationId, terrain = probe.Describe(profile) };
        var slots = new List<EncounterSlot>(f.slots);
        slots.Sort((a, b) => a.xOffset.CompareTo(b.xOffset));
        float lastGroundX = float.NegativeInfinity;
        var spawnList = new List<(EnemyDefinition d, Vector2 pos, EnemyAiTier tier, EnemyBehaviorKind beh, EncounterSlotKind slot)>();
        foreach (var slot in slots)
        {
            if (slot.minIntensity > intensity) continue;
            EnemyDefinition def = PickEnemy(band, slot, out EncounterEnemyEntry entry);
            if (def == null) continue;
            float x = anchor + slot.xOffset * spacing + Range(-slot.jitter, slot.jitter);
            Vector2 pos;
            if (EncounterSlots.IsAir(slot.kind))
            {
                if (!PlaceAir(tm, slot.kind, x, out pos)) continue;
            }
            else if (slot.kind == EncounterSlotKind.Burrow)
            {
                if (!PlaceBurrow(tm, x, out pos)) continue;
            }
            else
            {
                if (x < lastGroundX + f.minGroundGap) x = lastGroundX + f.minGroundGap;
                if (!PlaceGround(tm, x, out pos)) continue;
                lastGroundX = pos.x;
            }
            EnemyAiTier tier = PickTier(tiers, entry);
            EnemyBehaviorKind beh = def.behaviorKind;
            if (entry.tierDrivesMelee) beh = tier == EnemyAiTier.T0 ? EnemyBehaviorKind.None : EnemyBehaviorKind.StationaryMelee;
            spawnList.Add((def, pos, tier, beh, slot.kind));
        }
        if (spawnList.Count == 0) return false;

        float endX = anchor;
        var spawnedGos = new List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>();
        foreach (var s in spawnList)
        {
            GameObject go = tm.SpawnEncounterEnemy(s.d, s.pos, s.tier, s.beh);
            if (go == null) continue;
            spawnedGos.Add((go, s.d, s.slot));
            SpawnedEnemies++;
            endX = Mathf.Max(endX, s.pos.x);
            rec.members.Add(new Member { enemyId = s.d.enemyId, tier = s.tier, slot = s.slot, x = FloatingOrigin.ToLogical(s.pos.x), y = s.pos.y });
        }
        rec.anchorLogical = FloatingOrigin.ToLogical(anchor);
        rec.endLogical = FloatingOrigin.ToLogical(endX);
        spans.Add(new Vector2((float)rec.anchorLogical - 2f, (float)rec.endLogical + 2f));
        Commit(rec);
        OnEncounterSpawned?.Invoke(rec, f, spawnedGos);
        nextAnchor = rec.endLogical + Range(GapRange(intensity)) * GapScale(speed);
        return true;
    }

    Vector2 GapRange(EncounterIntensity i) =>
        i == EncounterIntensity.Hard ? profile.gapAfterHard : i == EncounterIntensity.Medium ? profile.gapAfterMedium : profile.gapAfterEasy;

    bool PlaceGround(TerrainManager tm, float x, out Vector2 pos)
    {
        pos = default;
        for (int k = 0; k < 5; k++)
        {
            float sx = x + k * 0.5f;
            float? gy = tm.GetHeightAt(sx);
            if (!gy.HasValue || tm.IsNearPit(sx, 1.2f)) continue;
            pos = new Vector2(sx, gy.Value);
            return true;
        }
        return false;
    }

    bool PlaceAir(TerrainManager tm, EncounterSlotKind kind, float x, out Vector2 pos)
    {
        pos = default;
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
        foreach (var m in rec.members) sb.Append($"{m.enemyId}({m.tier},{m.slot}) ");
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

        public TerrainProbe(TerrainManager tm, StageEncounterProfile prof, float anchorX)
        {
            AnchorX = anchorX;
            int n = Mathf.CeilToInt(Range / Step) + 1;
            ground = new float[n]; clearance = new float[n]; flat = new bool[n];
            generatedEnd = tm.GeneratedEndX;
            if (tm.cave != null && tm.cave.Active) generatedEnd = Mathf.Min(generatedEnd, tm.cave.GeneratedEndX);
            for (int i = 0; i < n; i++)
            {
                float x = anchorX - 1f + i * Step;
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
        public bool CoversWidth(float width) => AnchorX + width + 1f <= generatedEnd && width + 2f <= Range;
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

    GUIStyle style, btn;

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.DebugMode || !gm.HasStarted || profile == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            style.normal.textColor = Color.white;
            btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        }
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s;
        var r = new Rect(w - 440f, 150f, 430f, 250f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var sb = new StringBuilder();
        sb.Append($"<b>ENCOUNTER</b> stage={profile.stageId} dist={gm.MaxDistance:F0}m band={(CurrentBand != null ? CurrentBand.bandName : "-")}{(paused ? " <color=#ffb070>PAUSED</color>" : "")}\n");
        if (Last != null)
        {
            sb.Append($"#{Last.index} {Last.intensity} <b>{Last.formation}</b> d={Last.distance:F0} [{Last.terrain}]\n");
            foreach (var m in Last.members) sb.Append($"  {m.enemyId} {m.tier} {m.slot}\n");
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

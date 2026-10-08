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
    // ステージ固有の演出が使う通知(複数登録できる。テスト用のOnEncounterSpawnedとは別)。
    public static event System.Action<Record, EncounterFormation, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>> Spawned;

    // ===== ラストダンジョン(2026-09-30)の差し込み口。すべて走行距離(m)で決まる(マルチでもHOSTの判断が全員に共有される) =====
    //  SuppressAt(距離) … trueの間は何も置かない(ボスラッシュ/静寂/エンドロール)
    //  PaceAt(距離)     … Encounterどうしの間隔と休憩の長さの倍率(1=既定、<1=詰める=激しい区間、>1=落ち着く区間)
    //  SurgeAt(距離)    … 0..1。Intensity(Rest/Easy/Medium/Hard)の比重を激しい方へ寄せる(難易度の波)
    public static System.Func<float, bool> SuppressAt;
    public static System.Func<float, float> PaceAt;
    public static System.Func<float, float> SurgeAt;
    float planDistance; // 今決めているEncounterの走行距離(SurgeAt用)
    float Pace(float d) => (PaceAt != null ? Mathf.Max(0.2f, PaceAt(d)) : 1f) * SpawnGapFactor;

    // ===== 敵出現率のカード(2026-10-03) =====
    // GameManager.EnemySpawnRateMultiplier(1 + MORE ENEMIES/HORDE/GREED/... の合計)を、このDirectorでは
    // 「Encounterの頻度」に変える: Encounterどうしの間隔と休憩(Rest)の長さ ÷ 頻度、Restを選ぶ割合 ÷ 頻度。
    // 1つのEncounterの中身(Formationの敵の数/並び)は変えないので、敵が1か所に重ならない。ボスの関門/ボス戦中の停止/
    // BONUS ZONE/安全区間は今までどおり(このDirectorの外の仕組み)。極端な値でも頻度は下の範囲で止める(端末負荷)。
    // マルチ: 出現を決めるのはHOSTだけなので、HOSTのカードの値を使う(JOINのカードは効かない。仕様の確認待ち)。
    [Header("敵出現率カード(2026-10-03)")]
    [Tooltip("出現率カードで上がる頻度の上限(2=間隔が半分)")]
    public float spawnRateMaxFrequency = 2f;
    [Tooltip("出現率が下がる場合の頻度の下限")]
    public float spawnRateMinFrequency = 0.5f;
    public float SpawnFrequency
    {
        get
        {
            var gm = GameManager.Instance;
            float m = gm != null ? gm.EnemySpawnRateMultiplier : 1f;
            return Mathf.Clamp(m, Mathf.Max(0.05f, spawnRateMinFrequency), Mathf.Max(spawnRateMinFrequency, spawnRateMaxFrequency));
        }
    }
    float SpawnGapFactor => 1f / Mathf.Max(0.05f, SpawnFrequency);
    public static int SuppressedFrames; // テスト用

    // 統計(自動テスト/デバッグ)
    public readonly List<Record> Recent = new List<Record>();
    public readonly Dictionary<string, int> FormationCounts = new Dictionary<string, int>();
    public readonly int[] IntensityCounts = new int[4];
    public int SpawnedEnemies;
    public Record Last { get; private set; }
    public StageEncounterProfile Profile => profile;
    public EncounterDistanceBand CurrentBand { get; private set; }
    public int EncounterIndex => encounterIndex; // 決めたEncounterの数(休憩=restも含む)

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
        // シーンの読み直し(GAME OVER→Retry/HOME)では前のランの状態を捨てる。profileStage も空にする
        // (2026-10-01修正: 以前は profile だけを空にして profileStage を残していたため、同じステージで次のランを始めると
        //  「ステージは変わっていない」と判断されて Profile が読み直されず、そのラン中ずっと敵が出なかった)。
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { profileStage = null; ResetState(null); boundGm = null; boundStarted = false; };
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
        bonusWasActive = false;
        bonusZoneSeen = -1;
        logBoss = logBonus = logGameOver = false;
        baseTerrainAhead = baseCaveAhead = -1f;
        intensityHistory.Clear();
        formationHistory.Clear();
        encounterHistory.Clear();
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
    // 状態の確認用(ログ/自動テスト)。
    public string StateLine()
    {
        var gm = GameManager.Instance;
        var bm = BossManager.Instance;
        var bz = BonusZone.Instance;
        double pl = PlayerController.Instance != null ? FloatingOrigin.ToLogical(PlayerController.Instance.transform.position.x) : 0;
        return $"Stage={(gm != null ? gm.ActiveRunStageId : "-")} ProfileStage={profileStage ?? "-"} Profile={(profile != null ? "OK" : "NULL")} Enabled={enabled}"
            + $" Paused={paused}(boss={pausedForBoss}) BonusWasActive={bonusWasActive} BossActive={(bm != null && bm.IsBossPhase)} BonusActive={BonusZone.SuppressesNormalSpawns}({(bz != null ? bz.State.ToString() : "-")})"
            + $" Suppressed={(SuppressAt != null && gm != null && SuppressAt(gm.MaxDistance))} HookSuppress={(SuppressAt != null)} NextSpawn={(nextAnchor < 0 ? "unset" : (nextAnchor - pl).ToString("F0") + "m ahead")}"
            + $" Band={(CurrentBand != null ? CurrentBand.bandName : "-")} Encounters={encounterIndex} Spawned={SpawnedEnemies}";
    }

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
        // 新しいランの開始(Run開始/CONTINUE/マルチの開始のどれでも HasStarted が false→true になる)を検出して、
        // 前のランがどう終わったか(通常/ボス戦中/BONUS中の死亡、FINISH、HOME)に関係なく必ず初期状態から始める。
        if (gm != boundGm) { boundGm = gm; boundStarted = false; }
        if (!gm.HasStarted) { boundStarted = false; return; }
        if (!boundStarted) { boundStarted = true; ResetForNewRun(gm); }
        if (gm.IsGameOver)
        {
            if (!logGameOver) { logGameOver = true; Debug.Log($"[Encounter] GameOver BossActive={logBoss} BonusActive={logBonus} Suppressed={paused || logBonus} | {StateLine()}"); }
            return;
        }

        string stage = gm.ActiveRunStageId;
        // 安全策: ランの途中でステージが変わった/Profileが外れている(あるはずなのに無い)場合は読み直す
        // (敵を無理に出すのではなく、状態の食い違いを直すだけ)。
        if (stage != profileStage || (profile == null && StageEncounterProfile.Find(stage) != null))
        {
            if (stage == profileStage) Debug.LogWarning($"[Encounter] Profile was missing for {stage} during a run - reloaded");
            profileStage = stage;
            ResetState(StageEncounterProfile.Find(stage));
            if (profile != null) Debug.Log($"[ENCOUNTER] Director active for stage={stage} bands={profile.bands.Count} stageFormations={profile.stageFormations.Count}");
        }
        LogTransitions(gm);
        if (profile == null || !profile.replacesChunkSpawns) return;
        if (NetCombat.SuppressLocalEnemySpawn) return; // JOIN: 敵はHOSTが出す
        if (tm.enemySpawnChance <= 0f) return;          // 敵の出現そのものを止めている(自動テスト等の既存の切り替え)

        // マルチ Phase 3.1(最大8人の予定): 出現の基準は HOST ではなく WorldFront(ALIVEで走っている全員の最前)。
        // HOSTがカード選択で止まっている/DOWN/後方でも、最前の人の前に敵が出続ける。出すのは今まで通りHOSTだけ。
        // シングル/JOINは自分(JOINはここまで来ない)。距離の帯は「HOSTの距離の物差し」(RunDistanceAt)で数える。
        UpdateReference(pc, gm);
        double playerLogical = refLogical;
        float speed = refSpeed;
        float visibleAhead = refVisibleAhead;
        float ahead = Mathf.Max(profile.spawnAheadDistance * Mathf.Lerp(1f, speed, 0.5f), visibleAhead + profile.offscreenMargin);
        PruneSpans(FloatingOrigin.ToLogical(WorldRange.WorldBackSceneX)); // 最後尾の人がまだ通っていない区間の記録は残す
        ExtendGeneration(tm, ahead);

        if (SuppressAt != null && SuppressAt(RefDistance))
        {
            SuppressedFrames++;
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            return;
        }
        bool bossPhase = (BossManager.Instance != null && BossManager.Instance.SpawnsHeld) || CaveBossSafety.HoldZako; // ボス戦の強化: ラン再開後は雑魚を戻す / 自然洞窟ボスの必殺技中は出さない
        bool pauseNow = bossPhase || gm.IsDistanceInSafeZone(RefDistance) || gm.CountdownActive || gm.ResumeGateActive || (refIsLocal && pc.IsFinishing);
        if (pauseNow)
        {
            // 止めている間に出現位置がプレイヤーに追い越されないよう、前方へ送り続ける。
            if (!paused) { paused = true; pausedForBoss = bossPhase; Debug.Log($"[ENCOUNTER] paused ({(bossPhase ? "boss phase" : gm.IsDistanceInSafeZone(RefDistance) ? "safe zone" : "countdown/finish")})"); }
            if (bossPhase) pausedForBoss = true;
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            return;
        }
        if (paused)
        {
            paused = false;
            // ボス戦の強化(2026-10-01): ボスが残ったままのラン再開では「ボスの後の休み」を入れない(雑魚の出現の戻りはBossBattleTuning.zakoResumeDelay)
            bool bossStillAlive = BossManager.Instance != null && BossManager.Instance.RunResumed;
            double resume = playerLogical + ahead + (pausedForBoss && !bossStillAlive ? profile.bossPostRest * GapScale(speed) : 0f);
            if (nextAnchor < resume) nextAnchor = resume;
            if (pausedForBoss) { intensityHistory.Add(EncounterIntensity.Rest); Debug.Log($"[ENCOUNTER] resumed after boss: rest {profile.bossPostRest:F0}m before the next encounter"); }
            pausedForBoss = false;
        }

        // BONUS ZONE(2026-09-29): 区画の間は通常のEncounterを止め、Bonus用のFormationだけを流す。
        // 終わった後(終了の表示〜安全距離)は何も置かず、戻る時に出現位置を画面外の前方から始め直す。
        var bonus = BonusZone.Instance;
        if (BonusZone.SuppressesNormalSpawns && bonus != null)
        {
            UpdateBonus(tm, pc, gm, bonus, playerLogical, ahead, visibleAhead, speed);
            return;
        }
        if (bonusWasActive)
        {
            bonusWasActive = false;
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            intensityHistory.Add(EncounterIntensity.Rest);
            Debug.Log("[ENCOUNTER] resumed after BONUS ZONE");
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
            float runDistance = RunDistanceAt(nextAnchor);
            // 2026-10-02: 止める区間(ボスラッシュ/静寂)は「出す位置の距離」でも判定する(以前はプレイヤーの距離だけで、
            // 89,9xxmで決めた出現位置が90,000mの先=ボスラッシュの中へ雑魚を置いていた)
            if (SuppressAt != null && SuppressAt(runDistance - DebugDistanceOffset)) { SuppressedFrames++; nextAnchor += 20f; continue; }
            // 二重生成の防止(3.1): 同じ出現位置(1m単位)は1回しか決めない。最前の人が入れ替わっても基準点は前へしか進まないが、念のためHOSTで一意に管理する。
            long key = (long)System.Math.Round(nextAnchor);
            if (plannedAnchors.Contains(key)) { DuplicateAnchorsBlocked++; nextAnchor = key + 1.0; continue; }
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
                        float forkDistance = RunDistanceAt(forkLogical);
                        if (DebugDistanceOffset != 0f || !BossNear(forkDistance)) PlanBranch(tm, pc, fork, merge, forkDistance, speed);
                    }
                    nextAnchor = FloatingOrigin.ToLogical(merge) + Range(profile.afterMergeGap) * GapScale(speed) * SpawnGapFactor;
                    continue;
                }
                mainLimit = fork - profile.routeLead;
            }
            plannedAnchors.Add(key);
            NoteSpawnLead(nextAnchor - refRealLogical);
            PlanAt(tm, pc, runDistance, speed, mainLimit);
        }
    }

    // ===================================================================== //
    // 出現の基準(マルチ Phase 3.1)
    // ===================================================================== //
    double refLogical, refRealLogical, hostLogical;
    public static bool DebugLegacyHostAnchor;   // 自動テストの比較用: 3.1以前と同じ「HOST自身の位置」を基準にする(-netAutoLegacyAnchor)
    float refX, refSpeed = 1f, refVisibleAhead = 20f;
    bool refIsLocal = true;
    public int RefPlayer { get; private set; }
    float RefDistance => RunDistanceAt(refRealLogical) - DebugDistanceOffset;
    readonly HashSet<long> plannedAnchors = new HashSet<long>();
    public int DuplicateAnchorsBlocked { get; private set; }
    public float MinSpawnLead { get; private set; } = float.MaxValue;   // 出現を決めた時の「最前の人からの距離」の最小(m)
    public float LastSpawnLead { get; private set; }
    public readonly int[] SpawnsByFront = new int[9];                    // 出現を決めた時の最前の人(P0=シングル〜P8)

    // HOSTの距離の物差しで、論理X位置の距離
    float RunDistanceAt(double logical) => GameManager.Instance.MaxDistance + (float)(logical - hostLogical) + DebugDistanceOffset;

    void UpdateReference(PlayerController pc, GameManager gm)
    {
        float hostX = pc.transform.position.x;
        hostLogical = FloatingOrigin.ToLogical(hostX);
        WorldRange.PlayerPoint f = NetCombat.Authority ? WorldRange.Front : default;
        refIsLocal = !NetCombat.Authority || f.IsLocal || f.T == null || DebugLegacyHostAnchor;
        if (refIsLocal)
        {
            refX = hostX;
            refSpeed = SpeedScale(pc);
            refVisibleAhead = VisibleAhead(pc);
            refRealLogical = refLogical = hostLogical;
            RefPlayer = NetCombat.Authority ? NetCombat.LocalPlayerNumber : 0;
            return;
        }
        // 相手が最前: 分身の位置は通信と補間のぶん少し遅れて見えるので、その分(約0.3秒)だけ先を基準にする。
        // 速さは相手の走行速度から(高速の相手の前ほど遠くに出す)。画面の見える範囲は、相手のカメラが
        // 速度で前へずれていても足りるよう、画面の幅いっぱいを上限として見込む。
        refX = f.SceneX;
        refRealLogical = FloatingOrigin.ToLogical(f.SceneX);
        refLogical = refRealLogical + f.RunSpeed * 0.3f;
        float unit = Mathf.Max(0.5f, pc.runSpeed);
        refSpeed = Mathf.Max(1f, Mathf.Max(f.RunSpeed / unit, Mathf.Min(SpeedScale(pc), 1f)));
        Camera cam = Camera.main;
        float fullWidth = cam != null ? GameView.HalfWidth(cam) * 2f : 32f;
        refVisibleAhead = Mathf.Max(VisibleAhead(pc), fullWidth - 2f);
        RefPlayer = f.Pn;
    }

    void NoteSpawnLead(double lead)
    {
        LastSpawnLead = (float)lead;
        if (LastSpawnLead < MinSpawnLead) MinSpawnLead = LastSpawnLead;
        SpawnsByFront[Mathf.Clamp(RefPlayer, 0, SpawnsByFront.Length - 1)]++;
    }

    // ===================================================================== //
    // ランの開始/状態遷移のログ
    // ===================================================================== //
    GameManager boundGm;
    bool boundStarted;
    bool logBoss, logBonus, logGameOver;

    // 新しいランの初期化。前のランの状態(Profile/出現位置/一時停止/ボス・BONUSの記録/履歴/乱数)をすべて作り直す。
    // ラン単位の値はすべて ResetState にまとめてある(ここではステージのProfileを取り直してから呼ぶだけ)。
    void ResetForNewRun(GameManager gm)
    {
        plannedAnchors.Clear(); DuplicateAnchorsBlocked = 0; MinSpawnLead = float.MaxValue; LastSpawnLead = 0f;
        System.Array.Clear(SpawnsByFront, 0, SpawnsByFront.Length);
        var bm = BossManager.Instance;
        Debug.Log($"[Encounter] NewRunReset (previous: BossActive={logBoss} BonusActive={logBonus} Paused={paused} Profile={(profile != null ? "OK" : "NULL")} ProfileStage={profileStage ?? "-"})");
        profileStage = gm.ActiveRunStageId;
        ResetState(StageEncounterProfile.Find(profileStage));
        Debug.Log($"[Encounter] Initialize Stage={profileStage} Enabled={enabled && (profile != null && profile.replacesChunkSpawns)} Suppressed=False BossActive={(bm != null && bm.IsBossPhase)} BonusActive={BonusZone.SuppressesNormalSpawns} Profile={(profile != null ? "OK" : "none(this stage uses the legacy spawns)")} NextSpawn=unset(set on the first frame of running)");
    }

    void LogTransitions(GameManager gm)
    {
        var bm = BossManager.Instance;
        bool boss = bm != null && bm.IsBossPhase;
        if (boss != logBoss) { logBoss = boss; Debug.Log($"[Encounter] {(boss ? "BossStart" : "BossEnd")} d={gm.MaxDistance:F0} Suppressed={boss}"); }
        bool bonus = BonusZone.SuppressesNormalSpawns;
        if (bonus != logBonus) { logBonus = bonus; Debug.Log($"[Encounter] {(bonus ? "BonusStart" : "BonusEnd")} d={gm.MaxDistance:F0} Suppressed={bonus}"); }
    }

    // ===================================================================== //
    // BONUS ZONE
    // ===================================================================== //
    bool bonusWasActive;
    int bonusZoneSeen = -1;

    void UpdateBonus(TerrainManager tm, PlayerController pc, GameManager gm, BonusZone bonus, double playerLogical, float ahead, float visibleAhead, float speed)
    {
        bonusWasActive = true;
        // 区画の間に出現範囲へ入った上下ルートの分岐は「中身なし」で決めたことにする
        // (戻った直後に近くの分岐の中身を慌てて置くと、目の前に通常の敵が現れるため)
        if (profile.routeEncounters && tm.TryGetBranchAfter(refX - 5f, out float bf, out float bm, out bool bg) && bg)
        {
            double forkLogical = FloatingOrigin.ToLogical(bf);
            if (forkLogical > lastBranchForkLogical + 1.0 && bf - refX <= ahead + profile.routeLead + 10f) lastBranchForkLogical = forkLogical;
        }
        if (!bonus.SpawningAllowed)
        {
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            return;
        }
        // 始まった直後: 最初のwaveは画面のすぐ外から(走りながらすぐ獲物が見える)
        if (bonusZoneSeen != bonus.ZonesStarted) { bonusZoneSeen = bonus.ZonesStarted; nextAnchor = playerLogical + visibleAhead + 3.0; }
        if (nextAnchor < playerLogical + visibleAhead + 2.0) nextAnchor = playerLogical + visibleAhead + 3.0;
        // 報酬Enemyが多く残っている間は次のwaveを待つ(倒されずに溜まって画面を埋めない)
        if (bonus.ActiveEnemyCount() >= Mathf.Max(1, bonus.Profile.maxActiveEnemies))
        {
            if (nextAnchor < playerLogical + ahead) nextAnchor = playerLogical + ahead;
            return;
        }
        int guard = 0;
        while (nextAnchor <= playerLogical + ahead && guard++ < 3)
        {
            float sceneAnchor = (float)(nextAnchor - FloatingOrigin.Offset);
            if (!tm.IsGenerated(sceneAnchor + 30f) || (tm.cave != null && tm.cave.Active && tm.cave.GeneratedEndX < sceneAnchor + 30f)) break;
            EncounterFormation f = bonus.NextFormation(out Vector2 gap);
            if (f == null) { nextAnchor += 10f; continue; }
            float runDistance = RunDistanceAt(nextAnchor) - DebugDistanceOffset; // 基準が最前のプレイヤーでも距離の帯はHOSTの物差しで
            bool ok = false;
            for (int shift = 0; shift < 8 && !ok; shift++)
            {
                sceneAnchor = (float)(nextAnchor - FloatingOrigin.Offset);
                var probe = new TerrainProbe(tm, profile, sceneAnchor);
                if (Fits(f, probe, speed) && SpawnBonus(tm, bonus.Profile.band, f, probe, runDistance, speed, gap)) ok = true;
                else nextAnchor += 5f; // 穴/坂で置けない → 少し先で
            }
            if (!ok) nextAnchor += 6f;
        }
    }

    // 通常のEncounterの履歴(強さの波/連続の制御)には入れない。確認用にRecent/OnEncounterSpawnedへは流す。
    bool SpawnBonus(TerrainManager tm, EncounterDistanceBand band, EncounterFormation f, TerrainProbe probe, float runDistance, float speed, Vector2 gap)
    {
        var plan = PlanSlots(tm, band, f, EncounterIntensity.Hard, probe, speed, EncounterRoute.Main);
        if (plan == null || plan.Count == 0) return false;
        float anchor = probe.AnchorX;
        var rec = new Record { index = ++encounterIndex, distance = runDistance, band = band.bandName, intensity = EncounterIntensity.Medium, formation = f.formationId, terrain = probe.Describe(profile) };
        var spawnedGos = new List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>();
        float minX = anchor, endX = anchor;
        SpawnPlanned(tm, plan, rec, spawnedGos, ref minX, ref endX);
        rec.anchorLogical = FloatingOrigin.ToLogical(minX);
        rec.endLogical = FloatingOrigin.ToLogical(endX);
        Recent.Add(rec);
        if (Recent.Count > 400) Recent.RemoveAt(0);
        Last = rec;
        Debug.Log($"[ENCOUNTER] BONUS #{rec.index} d={rec.distance:F0} formation={rec.formation} members={rec.members.Count}");
        OnEncounterSpawned?.Invoke(rec, f, spawnedGos);
        nextAnchor = rec.endLogical + Range(gap) * GapScale(speed);
        return true;
    }

    void PlanUpcomingBranch(TerrainManager tm, PlayerController pc, GameManager gm, double playerLogical, float ahead, float speed)
    {
        float px = refX;
        if (!tm.TryGetBranchAfter(px, out float fork, out float merge, out bool generated) || !generated) return;
        double forkLogical = FloatingOrigin.ToLogical(fork);
        if (forkLogical <= lastBranchForkLogical + 1.0) return;              // もう決めた
        if (fork - px > ahead + profile.routeLead + 10f) return;             // まだ遠い
        if (!tm.IsGenerated(merge + 2f)) return;
        float forkDistance = RunDistanceAt(forkLogical);
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
        float right = GameView.Right(cam);
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
        planDistance = runDistance;
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
            : PickIntensity(band, SpawnGapFactor); // 出現率が高いほど休憩(Rest)を選びにくい
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
        // ラストダンジョン: 難易度の波(激しい区間ほどHard寄り・休憩が減る、落ち着く区間は休憩/Easy寄り)
        float surge = SurgeAt != null ? Mathf.Clamp01(SurgeAt(planDistance)) : -1f;
        if (surge >= 0f)
        {
            w[0] *= Mathf.Lerp(1.8f, 0.3f, surge);
            w[1] *= Mathf.Lerp(1.3f, 0.55f, surge);
            w[2] *= Mathf.Lerp(0.8f, 1.35f, surge);
            w[3] *= Mathf.Lerp(0.3f, 2.0f, surge);
        }
        // 休憩を挟まない戦闘が続きすぎたら必ず休憩(呼吸)を入れる。
        int sinceRest = 0;
        for (int i = n - 1; i >= 0 && intensityHistory[i] != EncounterIntensity.Rest; i--) sinceRest++;
        int maxNoRest = Mathf.Max(1, profile.maxEncountersWithoutRest) + (surge >= 0.75f ? 2 : 0);
        if (n > 0 && sinceRest >= maxNoRest) return EncounterIntensity.Rest;
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
            if (InGroupCooldown(f)) continue;
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

    float Spacing(EncounterFormation f, float speed) => 1f + f.spacingSpeedScale * (Mathf.Min(speed, Mathf.Max(1f, profile.maxSpacingSpeed)) - 1f);

    // 危険度の高いFormation(同じdangerGroup)が直近groupCooldown回のEncounterの中にあれば選ばない(強い戦闘を連続させない)。
    readonly List<string> encounterHistory = new List<string>(); // Restを含む直近のEncounter
    bool InGroupCooldown(EncounterFormation f)
    {
        if (string.IsNullOrEmpty(f.dangerGroup) || f.groupCooldown <= 0) return false;
        for (int i = encounterHistory.Count - 1, k = 0; i >= 0 && k < f.groupCooldown; i--, k++)
        {
            EncounterFormation o = profile.FindFormation(encounterHistory[i]);
            if (o != null && o.dangerGroup == f.dangerGroup) return true;
        }
        return false;
    }

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
            float afterFar = Mathf.Max(pitEnd + MaxPitOffset(f, EncounterPitAnchor.AfterPit) * sp, f.nearEdgeLanding ? pitEnd + f.pitLandingMargin : pitStart + JumpReach() + f.pitLandingMargin) + 1.5f;
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
        s.pitAnchor == EncounterPitAnchor.BeforePit ? -s.xOffset : s.pitAnchor == EncounterPitAnchor.AfterPit ? 1000f + s.xOffset : s.pitAnchor == EncounterPitAnchor.OverPit ? 500f + s.xOffset : s.xOffset;

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
        float[] tw = { Mathf.Max(0f, w.t0), Mathf.Max(0f, w.t1), Mathf.Max(0f, w.t2), Mathf.Max(0f, w.t3), Mathf.Max(0f, w.t4), Mathf.Max(0f, w.t5) };
        float sum = 0f;
        foreach (float x in tw) sum += x;
        EnemyAiTier t = EnemyAiTier.T0;
        if (sum > 0f)
        {
            float r = R01() * sum;
            for (int i = 0; i < tw.Length; i++) { if (r < tw[i]) { t = (EnemyAiTier)i; break; } r -= tw[i]; t = (EnemyAiTier)i; }
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
        public bool island;
    }

    // Formationの各Slotに置く敵と位置を決める(まだ出さない)。null = 穴が見つからない等で置けない。
    List<Planned> PlanSlots(TerrainManager tm, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, TerrainProbe probe, float speed, EncounterRoute route)
    {
        float spacing = Spacing(f, speed);
        float anchor = probe.AnchorX;
        var tiers = band.TiersFor(intensity);
        float pitStart = 0f, pitEnd = 0f;
        if (f.requiresPit && !FindGap(f, probe, speed, out pitStart, out pitEnd)) return null;
        float afterMin = f.nearEdgeLanding ? pitEnd + f.pitLandingMargin : pitStart + JumpReach() + f.pitLandingMargin;
        var slots = new List<EncounterSlot>(f.slots);
        if (f.requiresPit) slots.Sort((a, b) => PitOrder(a).CompareTo(PitOrder(b)));
        else slots.Sort((a, b) => a.xOffset.CompareTo(b.xOffset));
        float lastGroundX = float.NegativeInfinity;
        var list = new List<Planned>();
        int reqWanted = 0, reqPlaced = 0;
        foreach (var slot in slots)
        {
            if (slot.minIntensity > intensity) continue;
            if (slot.required) reqWanted++;
            EnemyDefinition def = PickEnemy(band, slot, out EncounterEnemyEntry entry);
            if (def == null) continue;
            float x;
            switch (slot.pitAnchor)
            {
                // 穴の手前: 縁からpitMinBefore以上手前(敵を倒してから助走して跳べる。被弾のノックバックは後ろ向き)
                case EncounterPitAnchor.BeforePit: x = pitStart - Mathf.Max(f.pitMinBefore, slot.xOffset) * spacing - Range(0f, Mathf.Abs(slot.jitter)); break;
                // 穴の向こう: 縁ぎりぎりで跳んでも着地点より先(跳んだら必ずぶつかる、にならない)
                case EncounterPitAnchor.AfterPit: x = Mathf.Max(pitEnd + slot.xOffset * spacing, afterMin) + Range(0f, Mathf.Abs(slot.jitter)); break;
                // 穴の真ん中の上空(ラストダンジョンの複合: 跳んだ先の空中に飛ぶ敵)
                case EncounterPitAnchor.OverPit: x = (pitStart + pitEnd) * 0.5f + slot.xOffset + Range(-Mathf.Abs(slot.jitter), Mathf.Abs(slot.jitter)); break;
                default: x = anchor + slot.xOffset * spacing + Range(-slot.jitter, slot.jitter); break;
            }
            if (x > probe.LimitX) continue;
            Vector2 pos;
            bool onIsland = false;
            if (EncounterSlots.IsAir(slot.kind))
            {
                if (!PlaceAir(tm, slot.kind, x, route, out pos)) continue;
            }
            else if (slot.kind == EncounterSlotKind.Island && profile.islandAware && route == EncounterRoute.Main && PlaceIsland(tm, x, probe.LimitX, out pos))
            {
                onIsland = true; // 浮島の上(地上の敵どうしの間隔は地面の列とは別に数える)
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
            list.Add(new Planned { def = def, pos = pos, tier = tier, beh = beh, slot = slot.kind, route = route, island = onIsland });
            if (slot.required) reqPlaced++;
        }
        if (reqPlaced < reqWanted) return null; // 主役を置けない(例: 守護兵の居ないGuardian Wall)なら、この場所では置かない
        if (profile.islandAware) KeepAirOrder(list);
        return list;
    }

    // 浮島のあるステージ: 空中の高さは真下の浮島から測るため、浮島の上の「低」が地面の上の「中」より高くなることがある。
    // 同じまとまり(前後10m以内)では 低 < 中 < 高 の見た目の順番(階段/段違い)を崩さないよう、上の段を少し持ち上げる
    // (下げると浮島にめり込む)。Aerial Waveのように離れた小集団どうしは比べない。
    static void KeepAirOrder(List<Planned> list)
    {
        const float step = 0.5f, window = 10f;
        int Rank(EncounterSlotKind k) => k == EncounterSlotKind.AirLow ? 0 : k == EncounterSlotKind.AirMiddle ? 1 : k == EncounterSlotKind.AirHigh ? 2 : -1;
        for (int r = 1; r <= 2; r++)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Rank(list[i].slot) != r) continue;
                float below = float.NegativeInfinity;
                for (int j = 0; j < list.Count; j++)
                {
                    int rj = Rank(list[j].slot);
                    if (rj < 0 || rj >= r || Mathf.Abs(list[j].pos.x - list[i].pos.x) > window) continue;
                    below = Mathf.Max(below, list[j].pos.y);
                }
                var pl = list[i];
                if (pl.pos.y < below + step) { pl.pos = new Vector2(pl.pos.x, below + step); list[i] = pl; }
            }
        }
    }

    void SpawnPlanned(TerrainManager tm, List<Planned> list, Record rec, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned, ref float minX, ref float maxX)
    {
        foreach (var s in list)
        {
            GameObject go = tm.SpawnEncounterEnemy(s.def, s.pos, s.tier, s.beh);
            if (go == null) continue;
            // 上ルートの敵はノックバック/打ち上げの着地も上ルートの面で行う(下ルートへ落ちない)
            if (s.route == EncounterRoute.Upper) { var ec = go.GetComponent<EnemyController>(); if (ec != null) ec.onUpperRoute = true; }
            if (s.island) { var ec = go.GetComponent<EnemyController>(); if (ec != null) ec.onIsland = true; }
            spawned.Add((go, s.def, s.slot));
            SpawnedEnemies++;
            minX = Mathf.Min(minX, s.pos.x);
            maxX = Mathf.Max(maxX, s.pos.x);
            rec.members.Add(new Member { enemyId = s.def.enemyId, tier = s.tier, slot = s.slot, route = s.route, x = FloatingOrigin.ToLogical(s.pos.x), y = s.pos.y });
        }
    }

    bool Spawn(TerrainManager tm, PlayerController pc, EncounterDistanceBand band, EncounterFormation f, EncounterIntensity intensity, TerrainProbe probe, float runDistance, float speed)
    {
        if (ChallengeSystem.SpawnHeldByCap()) return false; // カード v3: 生きている雑魚が多すぎる時は後回し(Android の負荷)
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
        Spawned?.Invoke(rec, f, spawnedGos);
        nextAnchor = rec.endLogical + Range(GapRange(intensity)) * GapScale(speed) * Pace(runDistance);
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
            pos = new Vector2(x, s.Value + Range(AirHeight(kind)));
            return true;
        }
        float ground = tm.GetGroundLineAt(x);
        Vector2 h = AirHeight(kind);
        // 浮島のあるステージ: 真下(前後1m)に浮島があれば、浮島の上面から測る(浮島にめり込ませない)
        if (profile.islandAware)
        {
            float? isl = tm.GetSkyHeightAt(x);
            if (!isl.HasValue) isl = tm.GetSkyHeightAt(x - 1f);
            if (!isl.HasValue) isl = tm.GetSkyHeightAt(x + 1f);
            if (isl.HasValue && isl.Value > ground) ground = isl.Value;
        }
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

    Vector2 AirHeight(EncounterSlotKind kind) =>
        kind == EncounterSlotKind.AirHigh ? profile.airHighHeight : kind == EncounterSlotKind.AirMiddle ? profile.airMiddleHeight : profile.airLowHeight;

    // 浮島の上(前後にも面が続く所)。見つからなければfalse(呼び出し側で地面へ置く)。
    bool PlaceIsland(TerrainManager tm, float x, float limitX, out Vector2 pos)
    {
        pos = default;
        for (int k = 0; k < 16; k++)
        {
            float sx = x + k * 0.75f;
            if (sx > limitX) return false;
            float? s = tm.GetSkyHeightAt(sx);
            if (!s.HasValue || !tm.GetSkyHeightAt(sx - 1f).HasValue || !tm.GetSkyHeightAt(sx + 1f).HasValue) continue;
            pos = new Vector2(sx, s.Value);
            return true;
        }
        return false;
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
        regionStart = Mathf.Max(regionStart, refX + refVisibleAhead + 1f); // 目の前には出さない(最前の人の画面にも)
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
        float len = short_ ? 10f : Range(profile.restLength) * GapScale(speed) * Pace(runDistance);
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
        encounterHistory.Add(rec.formation);
        if (encounterHistory.Count > 8) encounterHistory.RemoveAt(0);
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

        var r = new Rect(right - 460f, lineRect.y - 6f - 290f - 46f - 86f, 460f, 290f); // 下にFORCE行+敵1体のSPAWN行
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
        LocGUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, r.height - 8f), sb.ToString(), style);

        // 指定Formationを強制(次の出現位置で1回)
        var ids = AllFormationIds();
        if (ids.Count > 0)
        {
            debugSelect = Mathf.Clamp(debugSelect, 0, ids.Count - 1);
            float by = r.yMax + 6f;
            if (GUI.Button(new Rect(r.x, by, 44f, 40f), "<", btn)) debugSelect = (debugSelect + ids.Count - 1) % ids.Count;
            LocGUI.Label(new Rect(r.x + 50f, by + 8f, 210f, 30f), ids[debugSelect], style);
            if (GUI.Button(new Rect(r.x + 262f, by, 44f, 40f), ">", btn)) debugSelect = (debugSelect + 1) % ids.Count;
            if (GUI.Button(new Rect(r.x + 312f, by, 118f, 40f), "FORCE", btn)) ForceFormation(ids[debugSelect], 1);
        }

        // 天空回廊(2026-09-28): 敵を1体だけ指定Tierで出す(各AI状態の確認用) + 敵の頭上にAI状態を表示
        var enemyIds = AllEnemyIds();
        if (enemyIds.Count > 0)
        {
            debugEnemy = Mathf.Clamp(debugEnemy, 0, enemyIds.Count - 1);
            float ey = r.yMax + 52f;
            if (GUI.Button(new Rect(r.x, ey, 44f, 40f), "<", btn)) debugEnemy = (debugEnemy + enemyIds.Count - 1) % enemyIds.Count;
            LocGUI.Label(new Rect(r.x + 50f, ey + 8f, 160f, 30f), enemyIds[debugEnemy], style);
            if (GUI.Button(new Rect(r.x + 212f, ey, 44f, 40f), ">", btn)) debugEnemy = (debugEnemy + 1) % enemyIds.Count;
            if (GUI.Button(new Rect(r.x + 262f, ey, 48f, 40f), "T" + debugTier, btn)) debugTier = (debugTier + 1) % 6;
            if (GUI.Button(new Rect(r.x + 312f, ey, 118f, 40f), "SPAWN", btn)) DebugSpawnEnemy(enemyIds[debugEnemy], (EnemyAiTier)debugTier);
            if (GUI.Button(new Rect(r.x, ey + 46f, 150f, 30f), debugAiLabels ? "AI表示 ON" : "AI表示 OFF", btnSmall)) debugAiLabels = !debugAiLabels;
        }
        GUI.matrix = prev;
        if (debugAiLabels) DrawAiLabels();
    }

    int debugEnemy, debugTier = 1;
    static bool debugAiLabels;

    public List<string> StageEnemyIds() => AllEnemyIds(); // カード v3: WANTED の賞金首(このステージの Encounter に出る敵)
    List<string> AllEnemyIds()
    {
        var ids = new List<string>();
        if (profile == null) return ids;
        foreach (var b in profile.bands) foreach (var e in b.enemies) if (e != null && !ids.Contains(e.enemyId)) ids.Add(e.enemyId);
        return ids;
    }

    // 画面の右端より少し手前(見える所)に1体出す。地上の敵は地面、飛ぶ敵は低空。
    public GameObject DebugSpawnEnemy(string enemyId, EnemyAiTier tier)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        var def = EnemyDatabase.FindById(enemyId);
        if (tm == null || pc == null || def == null) return null;
        float x = pc.transform.position.x + Mathf.Max(8f, VisibleAhead(pc) - 2f);
        Vector2 pos;
        bool flying = def.movementType == EnemyMovementType.Flying;
        if (flying) { if (!PlaceAir(tm, EncounterSlotKind.AirLow, x, EncounterRoute.Main, out pos)) return null; }
        else if (!PlaceGround(tm, x, EncounterRoute.Main, float.PositiveInfinity, out pos)) return null;
        var go = tm.SpawnEncounterEnemy(def, pos, tier, def.behaviorKind);
        Debug.Log($"[ENCOUNTER] debug spawn {enemyId} {tier} at x={FloatingOrigin.ToLogical(pos.x):F1} y={pos.y:F1}");
        return go;
    }

    GUIStyle aiLabelStyle;
    void DrawAiLabels()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (aiLabelStyle == null) { aiLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter }; aiLabelStyle.normal.textColor = new Color(1f, 1f, 0.6f); }
        foreach (var sb in FindObjectsByType<EnemySpecialBehavior>(FindObjectsSortMode.None))
        {
            if (!sb.isActiveAndEnabled) continue;
            Vector3 sp = cam.WorldToScreenPoint(sb.transform.position + Vector3.up * 2.2f);
            if (sp.z < 0f || sp.x < 0f || sp.x > Screen.width) continue;
            LocGUI.Label(new Rect(sp.x - 90f, Screen.height - sp.y - 10f, 180f, 20f), sb.DebugState, aiLabelStyle);
        }
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

using System.Collections.Generic;
using UnityEngine;

// 高速時の自動操作補助(2026-09-28 試作) - 旧「高速時の自動スロー」(AutoSlowMotion)の置き換え。
//
// 時間は遅くしない(TimeControlの停止理由/HitStop/ボス演出はそのまま)。走行速度が engageKmh 以上の間だけ、
// そのプレイヤー自身が「進路上の敵への前攻撃」「穴/障害物/段差/天井の針を避けるジャンプ(必要なら二段)」を
// 既存の入力(上/前フリック)として自動で出す。攻撃/ジャンプの物理・威力・射程・クールダウンは既存のまま。
//
// 判断の流れ(毎フレーム、PlayerController.Update()の入力処理の中から Decide() が呼ばれる):
//  1. 判定速度 = PlayerController.CurrentAutoRunSpeed(継続的な走行速度。突進/ノックバック/復帰/座標補正は含まない)
//     engage以上で発動、release未満で解除(ヒステリシス)。
//  2. 「何もしない場合」の軌道を先読み時間ぶん予測し(地面/上ルート/穴/段差の壁/洞窟の天井と針/障害物/
//     落下死ライン)、最初の危険を見つける。地形が未生成・天井が未生成の場所は安全とみなさない。
//  3. 危険があれば、踏み切りの候補時刻ごとにジャンプ(必要なら二段)の軌道を予測し、安全に着地できて
//     着地直後にも余裕がある候補のうち、頂点が危険の真上に来る時刻に最も近いものを選ぶ。
//     それが「今」ならジャンプを入力、先なら待つ。空中では現在の軌道の着地が危険な時だけ二段ジャンプを使う。
//  4. ジャンプしないフレームで、進路上の敵に「攻撃の発生時間+入力遅延」後に間合いへ入るなら前攻撃を入力。
//     別の高さ(上ルート/足場の下)や、途中の穴/壁の向こうの敵は狙わない。
//  5. 安全な行動が無ければ何もしない(無敵化/ワープ/減速はしない)。理由はデバッグ表示と診断ログに残す。
//
// 手動入力が優先: 手動のフリックがあったフレームは補助は何もしない。手動の攻撃の後は manualAttackSuppress 秒
// 自動攻撃を、手動のジャンプの後は manualJumpSuppress 秒 自動ジャンプ(二段含む)を出さない(穴への補助が
// 間に合わなくならないよう短め)。予約は持たないので、停止/カード選択/HitStop/カウントダウンの後に古い操作が
// 遅れて発火することはない(再開直後のフレームは状態をリセットしてから判断する)。
//
// マルチ: PlayerControllerは各端末の「自分のプレイヤー」にしか無いので、補助の判断は各自の端末で自分のキャラだけ
// に対して1回だけ行われる(同じキャラに複数端末から自動入力されることはない)。入力は通常のフリックと同じ経路を
// 通るので、攻撃/被弾/撃破報酬の同期は既存の仕組みのまま。他のプレイヤーの速度・時間・地形には影響しない。
[DefaultExecutionOrder(-800)]
public class HighSpeedAssist : MonoBehaviour
{
    public static HighSpeedAssist Instance { get; private set; }

    [Header("発動条件(km/h)")]
    public bool assistEnabled = true;
    // 2026-09-29: 200/180 → 100/90(マスター指定)。ここ(またはInspector)で調整できる。
    public float engageKmh = 100f;
    public float releaseKmh = 90f;
    // 2026-09-30: 自然加速の上限が100km/hになり、通常のRunは大半を100km/hで走る。100km/hちょうどで全面的に補助すると
    // 自分で操作する余地が無くなるので、engage〜fullAssistKmh の間は段階的に強める:
    //  弱い = 「間に合う最後の踏み切り」だけ(自分で跳ばなかった時の救済)、強い = 従来どおり頂点が危険の真上に来る踏み切り。
    [Tooltip("補助が最大になる速度(km/h)。engageKmh〜この間は段階的に強くなる")]
    public float fullAssistKmh = 130f;
    public float Strength => Engaged ? Mathf.Clamp01(Mathf.InverseLerp(engageKmh, Mathf.Max(engageKmh + 1f, fullAssistKmh), JudgedKmh)) : 0f;

    [Header("障害物の破壊(2026-09-29)")]
    [Tooltip("接触までに壊し切る見込みがあれば、跳ばずに前攻撃で壊して通る")]
    public bool breakObstacles = true;
    [Tooltip("最後の一撃が当たる時刻と接触の時刻の間に最低限ほしい余裕(秒)")]
    public float breakMarginSeconds = 0.01f;
    [Tooltip("攻撃の間隔(秒)の初期値。実際に出した自動攻撃の間隔から学習して更新する")]
    public float defaultAttackCycle = 0.42f;
    [Tooltip("弾速が不明な時の仮の値(m/s)")]
    public float defaultProjectileSpeed = 16f;
    [Tooltip("飛び道具で壊す時の余裕(秒)")]
    public float projectileMarginSeconds = 0.12f;
    [Tooltip("飛び道具で障害物を狙い始める距離 = 走行速度 × この秒数(通常の狙い始め距離より遠い時だけ)")]
    public float obstacleAimSeconds = 0.7f;
    [Tooltip("着地の直後に越えられない障害物がある時、着地を待たずに二段ジャンプで越える(2026-09-29)")]
    public bool earlyDoubleJump = true;
    [Tooltip("障害物を越えるジャンプは、その障害物の先まで進めて初めて安全とみなす(2026-09-29)")]
    public bool requirePassObstacle = true;
    [Tooltip("跳ぶ計画で障害物の上・手前に見る余裕(m)")]
    public float jumpClearance = 0.25f;

    [Header("先読み")]
    [Tooltip("先読みする時間(秒)。距離 = 走行速度 × この秒数(下限/上限あり)")]
    public float lookAheadSeconds = 1.4f;
    public float minLookAheadMeters = 12f;
    public float maxLookAheadMeters = 200f;
    [Tooltip("軌道予測の刻み(メートル)。障害物をすり抜けないよう、体の幅より十分小さく")]
    public float simStepMeters = 0.3f;
    [Tooltip("超高速では刻みを「1/240秒で進む距離」まで広げる(上限simStepMaxMeters)。計算量を速度に比例させないため")]
    public float simStepMaxMeters = 0.5f;
    [Tooltip("入力から動作までの遅れ(秒)。入力は次のMove()で反映されるので約1フレーム")]
    public float inputLatency = 1f / 60f;
    [Tooltip("着地後、次の危険まで最低限必要な余裕(秒)")]
    public float landingRunwaySeconds = 0.05f;
    [Tooltip("踏み切り候補の最大数(負荷の上限)")]
    public int maxTakeoffCandidates = 24;

    [Header("攻撃")]
    [Tooltip("間合いの判定の余裕(メートル)。発生の瞬間に敵がこの分だけ手前でも出す")]
    public float attackReachMargin = 0.4f;
    public float attackBandBelow = 0.3f;
    public float attackBandAbove = 0.8f;

    [Header("手動操作との共存(秒)")]
    public float manualAttackSuppress = 0.35f;
    public float manualJumpSuppress = 0.12f;

    public enum Status { Off, WaitingSpeed, Active, ManualPriority, Blocked }
    public enum Hazard { None, Pit, Obstacle, Wall, Spike, FallOut, Unknown }

    // ---- 状態(デバッグ表示/自動テスト用) ----
    public Status CurrentStatus { get; private set; } = Status.WaitingSpeed;
    public string BlockedReason { get; private set; } = "";
    public float JudgedKmh { get; private set; }
    public bool Engaged { get; private set; }
    public string LastAction { get; private set; } = "";
    public float LastActionTime { get; private set; } = -99f;
    public string LastFailure { get; private set; } = "";
    public float LastFailureTime { get; private set; } = -99f;
    public string PlanText { get; private set; } = "";
    public int AutoJumps, AutoDoubleJumps, AutoAttacks, NoSafeActionCount, ManualInputs, ResumeResets;
    // 障害物ごとの判断(同じ障害物は1回だけ数える): 壊して通る/跳ぶ/どちらも無理
    public int ObstacleBreakPlans, ObstacleJumpPlans, ObstacleNoPlan, BreakAbandoned;
    public string LastBreakWhy { get; private set; } = "";
    public float LearnedAttackCycle { get; private set; }
    float lastAutoAttackTime = -99f; bool waitingReady;
    ObstacleController committedBreak, lastAttackOc; int hitsAtLastAttack;
    public float LastDecideMs { get; private set; }
    public float MaxDecideMs;               // 起動直後(JITの初回コンパイル)を除いた最大
    public int DecideCount, SlowDecides;    // SlowDecides = 2ms以上かかった回数
    public double TotalDecideMs;
    public int BudgetCutoffs;               // 計算量の上限で候補の検討を打ち切った回数
    [Tooltip("1フレームで軌道予測に使う最大ステップ数(処理落ち防止の上限)")]
    public int maxSimStepsPerFrame = 20000;
    int stepsUsed;
    bool OverBudget => stepsUsed > maxSimStepsPerFrame;

    float manualAttackUntil = -99f, manualJumpUntil = -99f;
    // 地上の踏み切り計画のキャッシュ(毎フレーム全候補を探し直さない)
    float cachedTakeoffTime = -1f, cachedHazardX = float.NaN; bool cachedDouble; int cacheAge;
    int lastDecideFrame = -10;
    float lastFailureHazardX = float.NaN;

    const string PrefKey = "HighSpeedAssistEnabled";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[HighSpeedAssist]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<HighSpeedAssist>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        assistEnabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ResetRunState();
    }

    public void SetEnabled(bool on)
    {
        assistEnabled = on;
        PlayerPrefs.SetInt(PrefKey, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ResetRunState()
    {
        Engaged = false;
        LastAction = ""; LastFailure = ""; PlanText = "";
        LastActionTime = LastFailureTime = -99f;
        manualAttackUntil = manualJumpUntil = -99f;
        lastFailureHazardX = float.NaN;
        AutoJumps = AutoDoubleJumps = AutoAttacks = NoSafeActionCount = ManualInputs = ResumeResets = 0;
        ObstacleBreakPlans = ObstacleJumpPlans = ObstacleNoPlan = BreakAbandoned = 0;
        LastBreakWhy = ""; LearnedAttackCycle = 0f; lastAutoAttackTime = -99f; waitingReady = false;
        committedBreak = null; lastAttackOc = null;
        decisions.Clear();
        MaxDecideMs = 0f; DecideCount = SlowDecides = BudgetCutoffs = 0; TotalDecideMs = 0.0;
        CurrentStatus = Status.WaitingSpeed;
    }

    // 補助が判断しないフレーム(停止/選択/カウントダウン等)でも速度の判定と生成範囲だけは更新する。
    void Update()
    {
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        if (pc == null || gm == null || !gm.HasStarted || gm.IsGameOver)
        {
            SetGenerateAhead(0f);
            if (gm == null || !gm.HasStarted) { Engaged = false; JudgedKmh = 0f; }
            return;
        }
        float v = pc.CurrentAutoRunSpeed;
        JudgedKmh = GameManager.SpeedKmh(v);
        if (!Engaged && JudgedKmh >= engageKmh) Engaged = true;
        else if (Engaged && JudgedKmh < releaseKmh) Engaged = false;
        // 先読みに必要な範囲まで地形/天井を先に作っておく(補助が働く時だけ。通常速度では従来の範囲のまま)。
        SetGenerateAhead(assistEnabled && Engaged ? LookAheadMeters(v) + 30f : 0f);
        if (lastDecideFrame < Time.frameCount - 1)
        {
            // このフレームは補助の判断まで来ていない(停止/選択/リアクション等)。表示用の状態だけ更新する。
            if (!assistEnabled) CurrentStatus = Status.Off;
            else if (!Engaged) CurrentStatus = Status.WaitingSpeed;
            else if (Time.timeScale <= 0f) { CurrentStatus = Status.Blocked; BlockedReason = "停止中"; }
            else if (gm.CountdownActive) { CurrentStatus = Status.Blocked; BlockedReason = "カウントダウン"; }
            else if (pc.NetIsChoosing) { CurrentStatus = Status.Blocked; BlockedReason = "カード選択中"; }
            else if (pc.NetIsDowned) { CurrentStatus = Status.Blocked; BlockedReason = "ダウン中"; }
        }
    }

    static void SetGenerateAhead(float meters)
    {
        TerrainManager tm = TerrainManager.Instance;
        if (tm == null) return;
        tm.assistGenerateAhead = meters;
        if (tm.cave != null) tm.cave.assistGenerateAhead = meters;
    }

    float LookAheadMeters(float v) => Mathf.Clamp(v * lookAheadSeconds, minLookAheadMeters, maxLookAheadMeters);

    // 補助が働いている間は、障害物/敵(距離マイルストーン方式の出現)をプレイヤーの先読み範囲の中に置く。
    // 固定の28m先だと、高速では「すでに跳んでいる放物線の途中/着地点」に突然現れて避けようがないため。
    // 通常速度(補助が待機中)やOFFの時は元の距離のまま。
    public static float SpawnAhead(float baseAhead)
    {
        HighSpeedAssist a = Instance;
        PlayerController pc = PlayerController.Instance;
        if (a == null || pc == null || !a.assistEnabled || !a.Engaged) return baseAhead;
        return Mathf.Max(baseAhead, a.LookAheadMeters(pc.CurrentAutoRunSpeed) - 2f);
    }

    public void NotifyManualInput(PlayerController.FlickDirection dir)
    {
        ManualInputs++;
        float now = Time.time;
        if (dir == PlayerController.FlickDirection.Up) manualJumpUntil = now + manualJumpSuppress;
        else manualAttackUntil = now + manualAttackSuppress;
        lastDecideFrame = Time.frameCount;
        if (assistEnabled && Engaged) CurrentStatus = Status.ManualPriority;
    }

    // ===================================================================== //
    // 判断(PlayerController.Update()の入力処理から、手動入力が無いフレームだけ呼ばれる)
    // ===================================================================== //
    public PlayerController.FlickDirection? Decide(PlayerController pc)
    {
        bool resumed = lastDecideFrame < Time.frameCount - 1;
        lastDecideFrame = Time.frameCount;
        if (resumed)
        {
            // 停止/選択/リアクション等から戻った最初のフレーム: 前の判断の続きは持ち越さない。
            ResumeResets++;
            lastFailureHazardX = float.NaN;
            cachedTakeoffTime = -1f; cachedHazardX = float.NaN;
            committedBreak = null;
        }
        if (!assistEnabled) { CurrentStatus = Status.Off; return null; }
        if (!Engaged) { CurrentStatus = Status.WaitingSpeed; return null; }
        if (pc.IsReacting) { CurrentStatus = Status.Blocked; BlockedReason = "被弾リアクション中"; return null; }
        if (pc.AssistEscapeCharging) { CurrentStatus = Status.Blocked; BlockedReason = "脱出チャージ中"; return null; }
        if (Time.deltaTime <= 0f) { CurrentStatus = Status.Blocked; BlockedReason = "停止中"; return null; }
        TerrainManager tm = TerrainManager.Instance;
        if (tm == null) return null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        PlayerController.FlickDirection? result = null;
        stepsUsed = 0;
        try { result = DecideInner(pc, tm); }
        finally
        {
            LastDecideMs = (float)sw.Elapsed.TotalMilliseconds;
            DecideCount++;
            if (DecideCount > 30)
            {
                TotalDecideMs += LastDecideMs;
                if (LastDecideMs > MaxDecideMs) MaxDecideMs = LastDecideMs;
                if (LastDecideMs >= 2f) SlowDecides++;
            }
        }
        bool manual = Time.time < manualAttackUntil || Time.time < manualJumpUntil;
        CurrentStatus = manual ? Status.ManualPriority : Status.Active;
        return result;
    }

    // ---- 予測に使う世界の断面(このフレームだけ有効) ----
    struct Box { public float minX, maxX, minY, maxY; public bool breakable; public bool quickBreak; public ObstacleController oc; }
    ObstacleController ignoreOc;   // 予測で「壊れる」前提にする障害物
    float simMustPassX = float.NaN; // 行動ありの予測で「ここを越えたら安全」とする位置(障害物の後端+体の半幅)
    string breakFailText = "", failurePrefix = "";
    readonly Dictionary<ObstacleController, int> decisions = new Dictionary<ObstacleController, int>(); // 1:壊す 2:跳ぶ 3:どちらも無理

    public int DecisionFor(ObstacleController oc) => oc != null && decisions.TryGetValue(oc, out int k) ? k : 0;

    // 障害物ごとの最終判断を記録(同じ障害物は最後の判断で数える)
    void Decision(ObstacleController oc, int kind)
    {
        if (oc == null) return;
        decisions.TryGetValue(oc, out int prev);
        if (prev == kind) return;
        decisions[oc] = kind;
        if (prev == 1) ObstacleBreakPlans--; else if (prev == 2) ObstacleJumpPlans--; else if (prev == 3) ObstacleNoPlan--;
        if (kind == 1) ObstacleBreakPlans++; else if (kind == 2) ObstacleJumpPlans++; else ObstacleNoPlan++;
        if (decisions.Count > 512) decisions.Clear();
    }

    // 2026-09-29: 空中で「危険は着地の後」の時、着地してからの地上ジャンプでは間に合わない(着地の直後に大岩/壁がある)か。
    // 間に合わないなら、着地を待たずに今の二段ジャンプで越える計画へ回す(従来は着地まで待ち、跳べずに当たることがあった)。
    bool LandingTooLate(SimState start, SimResult run, float horizonT)
    {
        if (!earlyDoubleJump) return false;
        if (start.jumpsLeft <= 0) return false;                                   // 二段ジャンプが無いなら待つしかない
        if (run.hazardTime - run.firstLandTime > 0.45f) return false;            // 着地後に十分な時間がある
        float keepPass = simMustPassX;
        simMustPassX = run.hazard == Hazard.Obstacle && requirePassObstacle ? run.obMaxX + halfW : float.NaN;
        try
        {
            float[] after = { 0.02f, 0.07f, 0.12f };
            foreach (float d in after)
            {
                if (run.firstLandTime + d >= run.hazardTime) break;
                if (Simulate(start, run.firstLandTime + d, -1f, horizonT, false).safe) return false;
            }
            return true;
        }
        finally { simMustPassX = keepPass; }
    }

    // 接触までに壊し切れるか。耐久力(JOINの予測込み)・攻撃力×前攻撃の倍率・発生・攻撃の間隔・弾の到達時間・接触までの時間から。
    bool EvaluateBreak(PlayerController pc, Vector3 p, SimResult run, out string text)
    {
        var oc = run.obOc;
        int hp = oc.PredictedHp >= 0 ? Mathf.Min(oc.PredictedHp, oc.Hp) : oc.Hp;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(pc.AssistAttackPower * pc.AssistForwardDamageScale));
        int hits = Mathf.Max(1, Mathf.CeilToInt(hp / (float)dmg));
        float startup = pc.AssistForwardStartup + inputLatency;
        float reach = pc.AssistForwardReach;
        bool proj = pc.AssistForwardIsProjectile;
        if (proj) reach = Mathf.Max(reach, vx * obstacleAimSeconds); // 障害物は速度に応じて早めに撃ち始める(弾が接触までに届くように)
        float cycle = LearnedAttackCycle > 0f ? LearnedAttackCycle : defaultAttackCycle;
        float sinceAtk = Time.time - lastAutoAttackTime;
        float ready = pc.AssistAttackReadyIn;
        if (ready < 0f) ready = Mathf.Clamp(cycle - sinceAtk, 0f, cycle);
        ready = Mathf.Max(ready, manualAttackUntil - Time.time);
        float dc = run.obMinX - p.x;
        float contact = run.hazardTime;
        float vp = pc.AssistForwardProjectileSpeed; if (vp <= 0f) vp = defaultProjectileSpeed;
        float inReach = (dc - reach) / vx; // 障害物の手前が間合い(飛び道具は狙い始める距離)に入る時刻
        // fire秒後に出した攻撃が当たる時刻。近接: 判定が出ていれば、障害物が間合いに入った瞬間に当たる(すり抜け対策の掃引あり)。
        // 飛び道具: 撃った位置からの残りの距離を「弾速+走行速度」で詰める。
        float HitAt(float fire)
        {
            if (!proj) return Mathf.Max(fire + startup, inReach);
            float gap = Mathf.Max(0f, dc - vx * Mathf.Max(0f, fire + startup));
            return Mathf.Max(0f, fire + startup) + gap / (vp + vx);
        }
        // この障害物へ出した直前の自動攻撃がまだ当たっていない(発生中/弾が飛んでいる)なら、それを1回目に数える
        bool pending = lastAttackOc == oc && oc.HitsTaken == hitsAtLastAttack && sinceAtk < startup + (proj ? 1.0f : 0.35f);
        float fireT = pending ? -sinceAtk : Mathf.Max(ready, proj ? inReach : inReach - startup);
        // 飛び道具: 弾は撃った高さのまま水平に飛ぶ。撃つ地点の(予測)高さ+発射口の高さが障害物の高さの範囲に入る
        // 最初の発射時刻を、接触までの間で探す(丘の上から撃つと低い障害物の上を越えてしまうため、下りてから撃つ)。
        bool heightOk = true; float yFire = 0f;
        if (proj && !pending)
        {
            heightOk = false;
            for (float t = Mathf.Max(0f, fireT); t < contact; t += 0.03f)
            {
                yFire = PathYAt(p.x + vx * t, p.y) + pc.AssistForwardMuzzleY;
                if (yFire >= run.obMinY + 0.1f && yFire <= run.obMaxY - 0.15f) { fireT = t; heightOk = true; break; } // 絵の形(幹が細い等)の分だけ内側
            }
        }
        float lastHit = HitAt(fireT);
        for (int i = 1; i < hits; i++)
        {
            fireT = Mathf.Max(fireT + cycle, 0f);
            lastHit = Mathf.Max(lastHit, HitAt(fireT));
        }
        text = $"耐久{hp}÷{dmg}={hits}回 最終{lastHit:F2}s/接触{contact:F2}s{(pending ? "(攻撃中)" : "")}";
        if (pc.AssistForwardLunge > 0f && hits > 1) { text += "(瞬身は1回で壊せる時だけ)"; return false; }
        if (!heightOk) { text += $"(弾の高さ{yFire:F1}が合わない)"; return false; }
        // 飛び道具は弾の到達時間の見積もりに誤差があるので余裕を大きめに(ぎりぎりなら跳ぶ方を選ぶ)
        float margin = pending ? 0f : proj ? Mathf.Max(breakMarginSeconds, projectileMarginSeconds) : breakMarginSeconds;
        return lastHit + margin <= contact; // 出した攻撃が当たる途中なら余裕は見ない(直前で断念して跳ぶと、かえって当たる)
    }
    readonly List<Box> obstacles = new List<Box>(16);
    readonly List<Box> enemies = new List<Box>(16);
    readonly Collider2D[] overlap = new Collider2D[512]; // 2026-09-29: 96だと地形などで溢れ、障害物を見落とすことがあった
    public int CollectOverflows, CollectMaxCount;
    readonly Dictionary<Collider2D, int> tagCache = new Dictionary<Collider2D, int>(); // 0:無関係 1:障害物 2:敵 3:壊せる障害物

    float halfW, bodyH, jumpForce, gravity, failY, headH, vx, aerialRemain, aerialScale;
    bool hasCave;
    float ceilKnownUntil, groundKnownUntil;

    PlayerController.FlickDirection? DecideInner(PlayerController pc, TerrainManager tm)
    {
        Vector3 p = pc.transform.position;
        vx = Mathf.Max(0.5f, pc.AssistHorizontalSpeed);
        Vector2 body = pc.AssistBodySize;
        halfW = body.x * 0.5f; bodyH = body.y;
        jumpForce = pc.AssistJumpForce; gravity = Mathf.Max(0.1f, pc.AssistGravity);
        aerialRemain = pc.AssistAerialAssistRemaining; aerialScale = pc.AssistAerialGravityScale;
        failY = pc.AssistFailY; hasCave = tm.HasCave; headH = tm.CavePlayerHeadHeight;
        ceilKnownUntil = tm.CeilingKnownUntil; groundKnownUntil = tm.GeneratedEndX;
        float horizonM = LookAheadMeters(pc.CurrentAutoRunSpeed);
        float horizonT = horizonM / vx;
        CollectBodies(p, horizonM);

        var start = new SimState { x = p.x, y = p.y, vy = pc.AssistVelocityY, grounded = pc.IsGrounded, onSky = pc.AssistOnSky, jumpsLeft = pc.AssistMaxJumps - pc.AssistJumpsUsed, maxJumps = pc.AssistMaxJumps, offset = pc.AssistGroundOffset };
        if (pc.AssistIsDiveOrHover) { PlanText = "急降下/ホバー中"; return null; }

        // ---- 何もしない場合の軌道 ----
        SimResult run = Simulate(start, -1f, -1f, horizonT, recordPath: true);
        bool jumpAllowed = Time.time >= manualJumpUntil && !pc.AssistIsMageFlight;
        float frameDt = Mathf.Max(Time.deltaTime, 1f / 240f);

        // 自動攻撃の間隔を学習(出してから次に出せるようになるまで)
        if (waitingReady && pc.AssistCanStartForwardAttack)
        {
            waitingReady = false;
            float c = Time.time - lastAutoAttackTime;
            if (c > 0.05f && c < 2f) LearnedAttackCycle = LearnedAttackCycle <= 0f ? c : Mathf.Lerp(LearnedAttackCycle, c, 0.3f);
        }

        // ---- 障害物: 接触までに壊し切れる見込みがあれば、跳ばずに前攻撃で壊して通る ----
        bool breaking = false;
        breakFailText = "";
        if (run.hazard != Hazard.Obstacle) LastBreakWhy = "";
        if (run.hazard == Hazard.Obstacle && run.obOc != null && breakObstacles && Time.time >= manualAttackUntil)
        {
            string txt;
            breaking = EvaluateBreak(pc, p, run, out txt);
            LastBreakWhy = txt;
            if (breaking)
            {
                committedBreak = run.obOc;
                cachedTakeoffTime = -1f; cachedHazardX = float.NaN;
                Decision(run.obOc, 1);
                PlanText = "壊して通る: " + txt;
            }
            else
            {
                if (committedBreak == run.obOc) { BreakAbandoned++; committedBreak = null; FreezeDiagnostics.LogEvent($"[Assist] 破壊を断念 {txt}"); }
                breakFailText = "壊し切れない(" + txt + ")";
            }
        }

        if (!breaking && run.hazard != Hazard.None && run.hazard != Hazard.Unknown)
        {
            bool obstacleHazard = run.hazard == Hazard.Obstacle && run.obOc != null;
            if (!jumpAllowed)
            {
                PlanText = pc.AssistIsMageFlight ? $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s(浮遊キャラはジャンプ補助なし)" : $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s(手動ジャンプ優先)";
                if (obstacleHazard && pc.AssistIsMageFlight)
                {
                    Decision(run.obOc, 3);
                    if (run.hazardTime < 0.25f) ReportFailure(run, breakFailText + "/浮遊キャラはジャンプ補助なし");
                }
            }
            else if (!start.grounded && run.firstLandTime >= 0f && run.hazardTime > run.firstLandTime + landingRunwaySeconds
                && !LandingTooLate(start, run, horizonT))
            {
                // 空中だが、危険は着地の後: 着地してから地上のジャンプで避ける(二段ジャンプは使わずに残す)
                PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s(着地後に判断)";
            }
            else if (start.grounded || start.jumpsLeft > 0)
            {
                failurePrefix = obstacleHazard ? breakFailText + "/跳べない: " : "";
                simMustPassX = obstacleHazard && requirePassObstacle ? run.obMaxX + halfW : float.NaN;
                var jump = PlanJump(start, run, horizonT, frameDt);
                simMustPassX = float.NaN;
                failurePrefix = "";
                if (obstacleHazard)
                {
                    if (jump.HasValue || cachedTakeoffTime > 0f) Decision(run.obOc, 2);
                    else if (run.leaveTime <= frameDt * 1.5f) Decision(run.obOc, 3);
                }
                if (jump.HasValue) return jump;
            }
            else
            {
                PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s(ジャンプ回数なし)";
                if (obstacleHazard) Decision(run.obOc, 3);
                ReportFailure(run, (obstacleHazard ? breakFailText + "/" : "") + "空中でジャンプ回数を使い切っている");
            }
        }
        else if (!breaking) PlanText = run.hazard == Hazard.Unknown ? $"先読み{horizonM:F0}m 安全(先は未生成)" : $"先読み{horizonM:F0}m 安全";

        // ---- 攻撃(地形の回避を優先し、ジャンプしないフレームだけ) ----
        // 越えられない「壊せる障害物」が迫っていて、跳ぶ計画も無い時は、敵と同じように前攻撃で壊す。
        // 2026-09-29: 壊して通ると決めた障害物だけを狙う(跳び越える予定の障害物は叩かない)。
        breakTarget = breaking
            ? new Box { minX = run.obMinX, maxX = run.obMaxX, minY = run.obMinY, maxY = run.obMaxY, breakable = true, oc = run.obOc } : (Box?)null;
        if (Time.time >= manualAttackUntil && pc.AssistCanStartForwardAttack)
        {
            if (TryPickAttack(pc, p, run, out string why))
            {
                // 空中で攻撃が当たると滞空補助(落下速度のリセット+弱い重力)で軌道が伸びる。その軌道でも
                // 安全に着地できる時だけ空中攻撃を出す(着地点が障害物/穴へずれるのを防ぐ)。
                // 壊しに行く障害物は「壊れる」前提で予測から外す(当たらない前提ではなく、壊し切れる見込みの判定は済み)。
                if (!start.grounded)
                {
                    var hitState = start;
                    hitState.vy = Mathf.Max(start.vy, pc.AssistAerialFallResetSpeed);
                    float keepRemain = aerialRemain;
                    aerialRemain = Mathf.Max(aerialRemain, pc.AssistAerialWindow);
                    ignoreOc = breaking ? run.obOc : null;
                    SimResult withHit = Simulate(hitState, -1f, -1f, horizonT, false);
                    ignoreOc = null;
                    aerialRemain = keepRemain;
                    bool hazardBeforeLanding = withHit.hazard != Hazard.None && withHit.hazard != Hazard.Unknown
                        && (withHit.firstLandTime < 0f || withHit.hazardTime <= withHit.firstLandTime + landingRunwaySeconds);
                    if (hazardBeforeLanding)
                    {
                        PlanText = $"空中攻撃は見送り(当たると着地が{HazardName(withHit.hazard)}へずれる)";
                        return null;
                    }
                }
                AutoAttacks++;
                lastAutoAttackTime = Time.time; waitingReady = true;
                lastAttackOc = breaking ? run.obOc : null; hitsAtLastAttack = lastAttackOc != null ? lastAttackOc.HitsTaken : 0;
                Act("前攻撃", why);
                return PlayerController.FlickDirection.Forward;
            }
        }
        return null;
    }

    // ---- ジャンプの計画 ----
    PlayerController.FlickDirection? PlanJump(SimState start, SimResult run, float horizonT, float frameDt)
    {
        float tApex = jumpForce / gravity;
        if (!start.grounded)
        {
            // 空中: 今の軌道の着地が危険な時だけ二段ジャンプ。今が安全ならそれ、後なら待つ。
            float bestT = -1f; string lastWhy = "";
            int n = Mathf.Clamp(Mathf.CeilToInt(run.hazardTime / Mathf.Max(frameDt, 0.03f)), 1, maxTakeoffCandidates);
            float step = run.hazardTime / n;
            for (int i = 0; i <= n; i++)
            {
                if (OverBudget) { BudgetCutoffs++; break; }
                float tau = i * step;
                SimResult r = Simulate(start, tau, -1f, horizonT, false);
                if (r.safe) { bestT = tau; break; }
                lastWhy = r.reason;
            }
            if (bestT < 0f)
            {
                PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s 空中: 安全な二段ジャンプなし";
                ReportFailure(run, lastWhy);
                return null;
            }
            if (bestT <= frameDt * 0.5f)
            {
                AutoDoubleJumps++;
                Act("二段ジャンプ", $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s");
                return PlayerController.FlickDirection.Up;
            }
            PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s 二段ジャンプ予定 {bestT:F2}s後";
            return null;
        }

        // 前のフレームで決めた踏み切り時刻がまだ先で、同じ危険に対するものなら、探し直さずに待つ
        // (4フレームごとに探し直す)。踏み切りの直前は「今」の安全だけ確かめて跳ぶ。
        bool sameHazard = !float.IsNaN(cachedHazardX) && Mathf.Abs(cachedHazardX - run.hazardX) < 1.0f;
        if (sameHazard && cachedTakeoffTime > 0f)
        {
            float remain = cachedTakeoffTime - Time.time;
            if (remain > frameDt * 0.5f && ++cacheAge % 4 != 0)
            {
                PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s 踏み切り{remain:F2}s後{(cachedDouble ? "(二段)" : "")}";
                return null;
            }
            if (remain <= frameDt * 0.5f)
            {
                SimResult now = cachedDouble ? Simulate(start, 0f, 0.25f, horizonT, false) : Simulate(start, 0f, -1f, horizonT, false);
                if (!now.safe && !cachedDouble) now = Simulate(start, 0f, 0.25f, horizonT, false);
                if (now.safe)
                {
                    cachedTakeoffTime = -1f; cachedHazardX = float.NaN;
                    AutoJumps++;
                    Act(cachedDouble ? "ジャンプ(二段予定)" : "ジャンプ", $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s/{run.hazardX - start.x:F1}m");
                    return PlayerController.FlickDirection.Up;
                }
                // 今は安全でなくなった → 下で探し直す
            }
        }

        // 地上: 踏み切り時刻の候補を順に試し、頂点が危険の中心の上に来る時刻に最も近い安全な候補を選ぶ。
        // 補助が弱い速度帯では「間に合う最後の踏み切り」寄りにする(下で安全な候補の最も遅い時刻との間を強さで補間)。
        float ideal = Mathf.Max(0f, run.hazardCenterTime - tApex);
        float strength = Strength;
        int count = Mathf.Clamp(Mathf.CeilToInt(run.leaveTime / Mathf.Max(frameDt, 0.02f)) + 1, 1, maxTakeoffCandidates);
        float dtau = count > 1 ? run.leaveTime / (count - 1) : 0f;
        float best = -1f, bestScore = float.MaxValue; bool bestDouble = false; string why = "";
        safeTaus.Clear();
        for (int i = 0; i < count; i++)
        {
            if (OverBudget) { BudgetCutoffs++; break; }
            float tau = i * dtau;
            SimResult r = Simulate(start, tau, -1f, horizonT, false);
            if (r.safe) safeTaus.Add(tau);
            else why = r.reason;
        }
        if (safeTaus.Count > 0)
        {
            float latest = safeTaus[safeTaus.Count - 1];
            float target = Mathf.Lerp(latest, ideal, strength);
            foreach (float tau in safeTaus)
            {
                float score = Mathf.Abs(tau - target);
                if (score < bestScore) { bestScore = score; best = tau; bestDouble = false; }
            }
        }
        if (best < 0f)
        {
            // 一段では届かない/越えられない: 踏み切り+二段ジャンプの組み合わせ(候補を間引いて試す)
            float[] seconds = { 0.12f, 0.25f, 0.4f };
            for (int i = 0; i < count && !OverBudget; i += 2)
            {
                float tau = i * dtau;
                foreach (float d in seconds)
                {
                    SimResult r = Simulate(start, tau, tau + d, horizonT, false);
                    if (!r.safe) { why = r.reason; continue; }
                    float score = Mathf.Abs(tau - ideal);
                    if (score < bestScore) { bestScore = score; best = tau; bestDouble = true; }
                }
            }
        }
        if (best < 0f)
        {
            cachedTakeoffTime = -1f; cachedHazardX = float.NaN;
            PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s: 安全な踏み切りなし";
            if (run.leaveTime <= frameDt * 1.5f) ReportFailure(run, why);
            return null;
        }
        if (best <= frameDt * 0.5f)
        {
            cachedTakeoffTime = -1f; cachedHazardX = float.NaN;
            AutoJumps++;
            Act(bestDouble ? "ジャンプ(二段予定)" : "ジャンプ", $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s/{run.hazardX - start.x:F1}m");
            return PlayerController.FlickDirection.Up;
        }
        cachedTakeoffTime = Time.time + best; cachedHazardX = run.hazardX; cachedDouble = bestDouble; cacheAge = 0;
        PlanText = $"{HazardName(run.hazard)}まで{run.hazardTime:F2}s 踏み切り{best:F2}s後{(bestDouble ? "(二段)" : "")}";
        return null;
    }

    readonly List<float> safeTaus = new List<float>(32);

    // ---- 攻撃の対象選び ----
    Box? breakTarget;
    readonly List<Box> targets = new List<Box>(16);
    bool TryPickAttack(PlayerController pc, Vector3 p, SimResult run, out string why)
    {
        why = "";
        float startup = pc.AssistForwardStartup + inputLatency;
        float reach = pc.AssistForwardReach;
        bool projectile = pc.AssistForwardIsProjectile;
        float bestDist = float.MaxValue; bool found = false;
        targets.Clear(); targets.AddRange(enemies);
        // 壊して通ると決めた障害物も前攻撃の対象(EvaluateBreakで接触までに壊し切れると見込んだ物だけ)
        if (breakTarget.HasValue) targets.Add(breakTarget.Value);
        foreach (Box e in targets)
        {
            if (e.maxX < p.x - 0.2f) continue;                           // もう通り過ぎた
            float dc = e.minX - p.x;                                       // 体の中心から敵の手前側まで
            // 途中の穴/壁/障害物の向こう(危険の地点より先)は狙わない(壊す対象の障害物そのものは除く)
            if (!e.breakable && run.hazard != Hazard.None && run.hazard != Hazard.Unknown && e.minX > run.hazardX + 0.5f) continue;
            // 敵の位置に着く時の自分の高さ(予測軌道)で、体+攻撃の高さの帯と重なるか(別の階層の敵を狙わない)
            float ex = Mathf.Clamp(e.minX, p.x, p.x + reach + vx * startup);
            float py = PathYAt(ex, p.y);
            if (e.maxY < py - attackBandBelow || e.minY > py + bodyH + attackBandAbove) continue;
            // 障害物を弾で狙う時は、今撃った弾の高さが障害物に届くか(坂/浮遊で上下を素通りしない)
            if (projectile && e.oc != null)
            {
                float yNow = p.y + pc.AssistForwardMuzzleY;
                if (yNow < e.minY + 0.1f || yNow > e.maxY - 0.15f) continue;
            }
            float atActive = dc - vx * startup;                            // 判定が出る瞬間の距離
            bool inRange;
            if (projectile) inRange = dc <= (e.oc != null ? Mathf.Max(reach, vx * obstacleAimSeconds) : reach) && atActive > -0.5f;
            else inRange = atActive <= reach + attackReachMargin && (e.maxX - (p.x + vx * startup)) > -0.3f;
            if (!inRange) continue;
            if (dc < bestDist) { bestDist = dc; found = true; why = $"{(e.breakable ? "壊せる障害物" : "敵")}まで{Mathf.Max(0f, dc):F1}m(発生{startup:F2}s/間合い{reach:F1}m)"; }
        }
        return found;
    }

    // 何もしない場合の軌道(記録済み)上の、x地点での高さ。範囲外なら現在の高さ。
    readonly List<Vector2> path = new List<Vector2>(512);
    float PathYAt(float x, float fallback)
    {
        if (path.Count == 0) return fallback;
        if (x <= path[0].x) return path[0].y;
        for (int i = 1; i < path.Count; i++)
            if (path[i].x >= x) return path[i].y;
        return path[path.Count - 1].y;
    }

    void CollectBodies(Vector3 p, float horizonM)
    {
        obstacles.Clear(); enemies.Clear();
        var filter = new ContactFilter2D { useTriggers = true };
        filter.NoFilter();
        Vector2 center = new Vector2(p.x + horizonM * 0.5f, p.y + 2f);
        Vector2 size = new Vector2(horizonM + 4f, 16f);
        int n = Physics2D.OverlapBox(center, size, 0f, filter, overlap);
        if (n > CollectMaxCount) CollectMaxCount = n;
        if (n >= overlap.Length) CollectOverflows++;
        for (int i = 0; i < n; i++)
        {
            Collider2D c = overlap[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            int kind = KindOf(c);
            if (kind == 0) continue;
            Bounds b = c.bounds;
            var box = new Box { minX = b.min.x, maxX = b.max.x, minY = b.min.y, maxY = b.max.y, breakable = kind == 3 };
            if (kind != 2)
            {
                box.oc = c.GetComponentInParent<ObstacleController>();
                if (box.oc != null && (box.oc.Broken || !box.oc.isActiveAndEnabled)) continue;
                if (box.oc != null && PlayerController.Instance != null)
                {
                    var pcq = PlayerController.Instance;
                    int dmg = Mathf.Max(1, Mathf.RoundToInt(pcq.AssistAttackPower * pcq.AssistForwardDamageScale));
                    box.quickBreak = box.breakable && box.oc.Hp <= dmg && pcq.AssistForwardLunge <= 0f;
                }
            }
            if (kind == 2) enemies.Add(box); else obstacles.Add(box);
        }
    }

    int KindOf(Collider2D c)
    {
        if (tagCache.TryGetValue(c, out int k)) return k;
        k = 0;
        Transform t = c.transform;
        for (int depth = 0; depth < 4 && t != null; depth++, t = t.parent)
        {
            if (t.CompareTag("Obstacle"))
            {
                var oc = t.GetComponent<ObstacleController>();
                k = oc != null && oc.breakable ? 3 : 1; break;
            }
            if (t.CompareTag("Enemy") || t.CompareTag("Boss")) { k = 2; break; }
            if (t.CompareTag("Player") || t.CompareTag("PlayerAttack") || t.CompareTag("Ground")) break;
        }
        if (tagCache.Count > 4000) tagCache.Clear();
        tagCache[c] = k;
        return k;
    }

    // ===================================================================== //
    // 軌道予測(既存のジャンプ物理: velocityY=jumpForce、重力gravity、天井で上昇停止、上から横切った面に着地)
    // ===================================================================== //
    struct SimState { public float x, y, vy, offset; public bool grounded, onSky; public int jumpsLeft, maxJumps; }
    struct SimResult
    {
        public bool safe;            // (行動あり)安全に着地し、着地後の余裕もある /(行動なし)先読み範囲内に危険なし
        public Hazard hazard;
        public float hazardX, hazardTime, hazardCenterTime;
        public float leaveTime;      // (行動なし)危険に関わる最後の足場を離れる時刻(踏み切り候補の上限)
        public float firstLandTime;  // 空中から始めた時、最初に着地する時刻(着地しなければ-1)
        public bool breakable;       // 危険が「壊せる障害物」
        public ObstacleController obOc;
        public float obMinX, obMaxX, obMinY, obMaxY;
        public string reason;
    }

    SimResult Simulate(SimState s, float jumpAt, float doubleAt, float horizonT, bool recordPath)
    {
        if (recordPath) path.Clear();
        var res = new SimResult { hazard = Hazard.None, leaveTime = horizonT, firstLandTime = -1f };
        bool planned = jumpAt >= 0f;
        float stepM = Mathf.Clamp(vx / 240f, simStepMeters, simStepMaxMeters);
        float dt = Mathf.Clamp(stepM / vx, 1f / 600f, 1f / 60f);
        float x = s.x, y = s.y, vy = s.vy;
        bool grounded = s.grounded, onSky = s.onSky;
        int jumpsLeft = s.jumpsLeft;
        bool jumped = false, doubled = false, landedAfter = false;
        float runway = -1f;
        float lastGroundT = 0f;
        float pitStartX = float.NaN;
        int steps = Mathf.Min(4000, Mathf.CeilToInt(horizonT / dt) + 2);
        float t = 0f;
        TerrainManager tmS = TerrainManager.Instance;
        // 1つ前の刻みの地点(x)の面の値を持ち回す(毎刻み2回ずつ問い合わせない)
        float? prevSky = tmS.GetSkyHeightFast(x);
        bool prevGOk = tmS.TryGetGroundFast(x, out float prevGh, out bool prevPit);
        for (int i = 0; i < steps; i++, t += dt)
        {
            stepsUsed++;
            if (recordPath && (i & 1) == 0) path.Add(new Vector2(x, y));
            if (planned && !jumped && t >= jumpAt)
            {
                jumped = true;
                if (jumpsLeft <= 0) { res.reason = "ジャンプ回数なし"; return Fail(res, Hazard.FallOut, x, t); }
                vy = jumpForce; grounded = false; jumpsLeft--;
            }
            if (doubleAt >= 0f && !doubled && t >= doubleAt && !grounded)
            {
                doubled = true;
                if (jumpsLeft > 0) { vy = jumpForce; jumpsLeft--; }
            }
            float nx = x + vx * dt;
            float ny;
            if (nx > groundKnownUntil) return EndUnknown(res, planned, x, t, "地形が未生成");
            // この刻みの地点の面(上ルート/地面)は1回だけ問い合わせ、次の刻みの「1つ前」として持ち回す
            float? sky = tmS.GetSkyHeightFast(nx);
            bool gOk = tmS.TryGetGroundFast(nx, out float gh, out bool pit);
            if (grounded)
            {
                float? surf = onSky ? sky : (gOk && !pit ? gh : (float?)null);
                if (surf.HasValue) { ny = surf.Value + s.offset; lastGroundT = t; }
                else
                {
                    // 足場が無い(穴/上ルートの終わり): 落下開始(既存と同じくvelocityY=0から)
                    grounded = false; vy = 0f; ny = y;
                    if (float.IsNaN(pitStartX) && gOk && pit) pitStartX = nx;
                }
            }
            else
            {
                // 空中攻撃が当たった直後の滞空補助(重力が弱い)も、今残っている分だけ予測に入れる
                vy -= gravity * (t < aerialRemain ? aerialScale : 1f) * dt;
                ny = y + vy * dt;
                if (hasCave)
                {
                    if (nx > ceilKnownUntil) return EndUnknown(res, planned, x, t, "天井が未生成");
                    float? lim = TerrainManager.Instance.GetCeilingLimitY(nx);
                    if (lim.HasValue && ny > lim.Value) { ny = lim.Value; if (vy > 0f) vy = 0f; }
                }
                // 着地(上から面を横切った時だけ)。上ルートと地面の高い方。
                bool landSky = false, landGround = false; float skyY = 0f, groundY = 0f;
                if (sky.HasValue)
                {
                    float ps = (prevSky ?? sky.Value) + s.offset;
                    if (y - ps >= 0f && ny - (sky.Value + s.offset) <= 0f) { landSky = true; skyY = sky.Value + s.offset; }
                }
                if (gOk && !pit)
                {
                    float prevG = prevGOk && !prevPit ? prevGh : gh;
                    float gy = gh + s.offset;
                    if (y - (prevG + s.offset) >= 0f && ny - gy <= 0f) { landGround = true; groundY = gy; }
                    else if (ny < gy - 0.05f && y < gy - 0.05f)
                    {
                        // 地面の中へ横から入る = 段差/穴の向こう側の壁に当たる(既存の処理では床下に落ち続ける)
                        res.reason = "段差/壁に当たる";
                        return Fail(res, Hazard.Wall, nx, t, pitStartX);
                    }
                }
                if (landSky && (!landGround || skyY >= groundY)) { ny = skyY; vy = 0f; grounded = true; onSky = true; }
                else if (landGround) { ny = groundY; vy = 0f; grounded = true; onSky = false; }
                if (grounded)
                {
                    if (res.firstLandTime < 0f) res.firstLandTime = t;
                    jumpsLeft = s.maxJumps; // 着地でジャンプ回数が戻る(既存と同じ)
                    if (planned && jumped && !landedAfter) { landedAfter = true; runway = landingRunwaySeconds; }
                }
                if (ny < failY) { res.reason = "穴に落ちる"; return Fail(res, Hazard.FallOut, nx, t, pitStartX); }
            }
            // 天井の針(頭が針の三角形に触れる)
            if (hasCave && TerrainManager.Instance.IsCeilingSpikeHit(nx, ny)) { res.reason = "天井の針"; return Fail(res, Hazard.Spike, nx, t); }
            // 障害物(体の箱と重なる)
            for (int k = 0; k < obstacles.Count; k++)
            {
                Box b = obstacles[k];
                // 跳んだ後、着地して地上で出会う「壊せる障害物」は前攻撃で壊す前提にする(攻撃の対象選びが拾う)
                if (b.oc != null && b.oc == ignoreOc) continue;
                if (b.quickBreak && planned && landedAfter && grounded) continue; // 着地後の1撃で壊れる物だけ(大岩/壁は数えない)
                // 跳ぶ計画の予測では障害物の上と手前に余裕を見る(入力の1フレームの遅れ等で角をかすめて当たらないように)
                float top = planned ? b.maxY + jumpClearance : b.maxY - 0.05f;
                float front = planned ? b.minX - jumpClearance : b.minX + 0.05f;
                if (nx + halfW > front && nx - halfW < b.maxX - 0.05f && ny + bodyH > b.minY + 0.05f && ny < top)
                {
                    res.reason = b.breakable ? "壊せる障害物に当たる" : "障害物に当たる";
                    res.hazardCenterTime = t + ((b.minX + b.maxX) * 0.5f - nx) / vx;
                    var f = Fail(res, Hazard.Obstacle, nx, t);
                    f.breakable = b.breakable; f.obOc = b.oc; f.obMinX = b.minX; f.obMaxX = b.maxX; f.obMinY = b.minY; f.obMaxY = b.maxY;
                    f.hazardCenterTime = Mathf.Max(t, (((b.minX + b.maxX) * 0.5f) - s.x) / vx);
                    f.leaveTime = grounded ? t : lastGroundT;
                    return f;
                }
            }
            x = nx; y = ny;
            prevSky = sky; prevGOk = gOk; prevGh = gh; prevPit = pit;
            if (runway >= 0f)
            {
                runway -= dt;
                // 2026-09-29: 障害物を越える計画は、その障害物の先まで進めて初めて安全(手前に着地して直後に当たる跳び方を選ばない)
                if (runway <= 0f && (float.IsNaN(simMustPassX) || x > simMustPassX)) { res.safe = true; return res; }
            }
        }
        if (planned) { res.safe = false; res.reason = "着地が先読み範囲外"; res.hazard = Hazard.Unknown; return res; }
        res.safe = true;
        return res;
    }

    SimResult Fail(SimResult r, Hazard h, float x, float t, float pitStartX = float.NaN)
    {
        r.safe = false; r.hazard = h; r.hazardX = x; r.hazardTime = t;
        // 危険の中心(穴なら穴の中央、それ以外はその地点)までの時刻。踏み切り候補の上限は穴の手前の足場を離れる時刻。
        if (!float.IsNaN(pitStartX))
        {
            float pitEnd = PitEndFrom(pitStartX);
            r.hazardCenterTime = Mathf.Max(0f, ((pitStartX + pitEnd) * 0.5f - (x - vx * t)) / vx);
            r.leaveTime = Mathf.Max(0f, (pitStartX - (x - vx * t)) / vx);
            if (h == Hazard.FallOut || h == Hazard.Wall) r.hazard = h == Hazard.Wall && pitEnd - pitStartX < 0.01f ? Hazard.Wall : Hazard.Pit;
            r.hazardX = pitStartX;
        }
        else
        {
            if (r.hazardCenterTime <= 0f) r.hazardCenterTime = t;
            r.leaveTime = t;
        }
        return r;
    }

    SimResult EndUnknown(SimResult r, bool planned, float x, float t, string why)
    {
        if (planned) { r.safe = false; r.hazard = Hazard.Unknown; r.reason = why; r.hazardX = x; r.hazardTime = t; return r; }
        r.safe = true; r.hazard = Hazard.Unknown; r.reason = why; r.hazardX = x; r.hazardTime = t;
        return r;
    }

    // 穴の終わり(次の地面の始まり)。最大20m先まで0.25m刻みで探す。
    float PitEndFrom(float x0)
    {
        TerrainManager tm = TerrainManager.Instance;
        for (float x = x0; x < x0 + 20f; x += 0.25f)
        {
            if (!tm.TryGetGroundFast(x, out _, out bool pit)) return x;
            if (!pit) return x;
        }
        return x0 + 20f;
    }

    static string HazardName(Hazard h) => h switch
    {
        Hazard.Pit => "穴",
        Hazard.Obstacle => "障害物",
        Hazard.Wall => "段差/壁",
        Hazard.Spike => "天井の針",
        Hazard.FallOut => "落下",
        Hazard.Unknown => "未生成",
        _ => "-",
    };

    void Act(string what, string why)
    {
        LastAction = what; LastActionTime = Time.time;
        PlanText = why;
        FreezeDiagnostics.LogEvent($"[Assist] {what} ({why}) v={JudgedKmh:F0}km/h x={(PlayerController.Instance != null ? PlayerController.Instance.transform.position.x : 0f):F1}");
        LastActionReason = why;
    }
    public string LastActionReason { get; private set; } = "";

    void ReportFailure(SimResult run, string why)
    {
        // 同じ危険については1回だけ数える
        if (!float.IsNaN(lastFailureHazardX) && Mathf.Abs(lastFailureHazardX - run.hazardX) < 1f) return;
        lastFailureHazardX = run.hazardX;
        NoSafeActionCount++;
        LastFailure = $"{HazardName(run.hazard)}: {failurePrefix}{(string.IsNullOrEmpty(why) ? "安全な行動なし" : why)}";
        LastFailureTime = Time.time;
        FreezeDiagnostics.LogEvent($"[Assist] NO SAFE ACTION {LastFailure} v={JudgedKmh:F0}km/h hazardX={run.hazardX:F1}");
    }

    public string StatusText()
    {
        switch (CurrentStatus)
        {
            case Status.Off: return "OFF";
            case Status.WaitingSpeed: return $"待機(速度不足 {engageKmh:F0}km/h未満)";
            case Status.ManualPriority: return "手動操作優先";
            case Status.Blocked: return $"停止({BlockedReason})";
            default: return "発動中";
        }
    }
}

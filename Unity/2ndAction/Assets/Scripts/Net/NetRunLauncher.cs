using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// マルチプレイ対応Phase 1(2026-09-25) - 「全員で同じRunを同時に始める」係。
//
// 開始の完全同期(2026-09-28改修) - RunStateをHOSTが決める:
//   None → WaitingForPlayers → Countdown → Running → Finished
//  1. HOSTがStage Selectで出発 → GameManager.DepartFromStageSelectがInterceptDepartを呼ぶ
//  2. HOSTがWorldSeed(地形の種)とステージID、モードを全員へ送る(この時点ではGO!の時刻は決めない)
//  3. 全端末がシーンを読み込み直す(RESULT→TOPと同じ既存のリロード経路) - 読み込み直後に
//     WorldRngをそのSeedで初期化するので、TerrainManager.Start以降の地形が全端末で一致する。
//     ここでRunState=WaitingForPlayers(世界は止まったまま = GameManager.CountdownActive=true)
//  4. 読み込み完了の1フレーム後にGameManager.BeginMultiplayerRunでRunを準備し、ReadyForRunを
//     HOSTへ送る(HOSTは自分の分を記録。GOの時刻が届くまで1秒ごとに送り直す)
//  5. HOSTは接続中の全員がReadyになった時点で RunStartNetworkTime = 共通時計(NetNow) + 3秒 を決め、
//     全員へ配る(RunState=Countdown)。HOSTが先に読み終えても、JOINが先に読み終えても、
//     全員が揃うまで誰も数え始めない
//  6. 各端末は「RunStartNetworkTime - 共通時計」から毎フレーム READY/3/2/1 を求めて表示し、
//     共通時計がRunStartNetworkTimeに達したフレームでRunState=Running(=世界の開始)へ進む
//     (コルーチンの待ち時間の積み上げではないので、処理落ちがあってもGO!の時刻自体はずれない)
//
// 共通時計(NetNow): HOSTの実時間(realtimeSinceStartup)を基準にする。JOINは接続中ずっと、
// HOSTと往復の時刻合わせ(ClockPing/Pong)を行い、往復時間が最小のサンプルから差分を求める(NTPと同じ考え方)。
// NGOのServerTimeは、参加側では受信バッファの分だけ実際のサーバー時刻より遅れて進む設計のため
// (実測で約60ms)、開始時刻の基準には使わない。
//  7. HOSTがRun終了(RunOver)を決めたら、またはこの端末のRunが終わったらRunState=Finished
//
// シングルプレイ(セッション無し)では一切何もしない(RunState=None)。
//
// Phase 3(2026-09-27): HOSTが選んだゲームモード(CO-OP/VERSUS)もRun開始の通知に載せて配る。
// 各端末はRun中そのモードだけを使う(JOIN側の設定は使わない = HOSTの選択がセッション全体の正解)。
public enum MultiplayerGameMode : byte { Coop = 0, Versus = 1 }

public enum NetRunState : byte { None = 0, WaitingForPlayers = 1, Countdown = 2, Running = 3, Finished = 4 }

[DefaultExecutionOrder(-900)] // Countdown→Runningの切り替えを、同じフレームのPlayerController等より先に行う
public class NetRunLauncher : MonoBehaviour
{
    const string RunStartMessage = "OMM.RunStart";
    const string RunReadyMessage = "OMM.RunReady"; // JOIN→HOST: シーンの準備ができた(ReadyForRun)
    const string RunGoMessage = "OMM.RunGo";       // HOST→JOIN: RunStartNetworkTime(全員Ready確認後)
    public const double CountdownLeadSeconds = 3.0; // 全員Readyを確認してからGO!までの秒数(READY→3→2→1)
    const float WaitForPlayersTimeout = 30f;        // 準備完了が届かない人を待つ上限(切断などの安全策)
    const float ReadyResendInterval = 1f;
    const string ClockPingMessage = "OMM.ClockPing"; // JOIN→HOST: 送信時刻(JOINの実時間)
    const string ClockPongMessage = "OMM.ClockPong"; // HOST→JOIN: 送信時刻の折り返し + HOSTの実時間

    // 現在のRunの状態(HOSTが決める。JOINはHOSTの通知でしか Countdown/Running へ進まない)。
    public static NetRunState RunState { get; private set; } = NetRunState.None;
    // 全員共通のGO!の時刻(共通時計NetNow上の時刻、0=未定)。
    public static double RunStartNetworkTime => goServerTime;
    // GO!までの残り秒数(未定、または時計合わせ前なら+∞)。カウントダウン表示はこれから求める。
    public static double SecondsToGo => goServerTime > 0.0 && ClockReady ? goServerTime - NetNow : double.PositiveInfinity;

    // ===== 共通時計 =====
    static double clockOffset;             // JOIN: HOSTの実時間 - 自分の実時間
    static double clockBestRtt = double.MaxValue;
    static int clockSamples;
    static readonly double[] clockRtt = new double[12], clockOff = new double[12];
    static int clockRing;
    public static bool ClockReady => NetSession.IsActive && (NetSession.Manager.IsServer || clockSamples > 0);
    public static double NetNow => NetSession.IsActive && NetSession.Manager.IsServer ? Time.realtimeSinceStartupAsDouble : Time.realtimeSinceStartupAsDouble + clockOffset;
    public static double ClockBestRttMs => clockSamples > 0 ? clockBestRtt * 1000.0 : -1.0;
    float clockPingTimer;

    // この端末が実際にRunningへ進んだ瞬間のサーバー時刻/UTC(開始時刻差の測定用)。
    public static double LocalRunningServerTime { get; private set; }
    public static System.DateTime LocalRunningUtc { get; private set; }
    // GameManager.CountdownActiveの解除条件(マルチRunでは「RunState=Running以降」だけ)。
    public static bool ReleasedForRun => IsMultiplayerRun && RunState >= NetRunState.Running;

    public static NetRunLauncher Instance { get; private set; }

    // 現在のRunがマルチプレイRunか(シングルのチェックポイント保存等を止めるのに使う)。
    public static bool IsMultiplayerRun { get; private set; }
    public static int ActiveRunSeed { get; private set; }
    public static string ActiveRunStageId { get; private set; } = "";
    // 出発の通知は受け取ったがシーン読み込みがまだのRunのSeed(0=無し)。Phase 2の共有敵の
    // 出現通知がシーン読み込みより先に届いた場合に、捨てずに保持しておくのに使う。
    public static int PendingRunSeed => pending ? pendingSeed : 0;

    // Phase 3: HOSTが次のRunに使うモード(HOST端末で選ぶ。保存される)と、現在のRunのモード(HOSTから届いた値)。
    const string ModePrefKey = "net.mode";
    public static MultiplayerGameMode SelectedMode
    {
        get => (MultiplayerGameMode)Mathf.Clamp(PlayerPrefs.GetInt(ModePrefKey, 0), 0, 1);
        set { PlayerPrefs.SetInt(ModePrefKey, (int)value); PlayerPrefs.Save(); }
    }
    public static MultiplayerGameMode ActiveMode { get; private set; } = MultiplayerGameMode.Coop;
    static MultiplayerGameMode pendingMode;

    // HOSTの状態表が別のモードを示していたら、HOSTに合わせる(通常は起きない安全策)。
    public static void ForceActiveMode(MultiplayerGameMode mode)
    {
        if (!IsMultiplayerRun || ActiveMode == mode) return;
        NetSession.Log($"Game mode corrected to HOST's {mode} (was {ActiveMode})");
        ActiveMode = mode;
    }

    static bool pending;
    static string pendingStageId;
    static int pendingSeed;
    static double goServerTime;
    static bool localReady;
    static readonly System.Collections.Generic.HashSet<ulong> readyClients = new System.Collections.Generic.HashSet<ulong>();
    float waitElapsed, readyResendTimer;
    // テスト用: シーンの読み込みをわざと遅らせる秒数(-netLoadDelay N、開発ビルドのみ)。
    float debugLoadDelay;
    static int CurrentSeed => pending ? pendingSeed : ActiveRunSeed;

    bool handlerRegistered;
    NetworkManager registeredManager;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-netLoadDelay") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out debugLoadDelay);
#endif
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Update()
    {
        // CustomMessagingManagerは接続開始後にしか存在しないため、セッション開始/終了に合わせて登録し直す。
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm.CustomMessagingManager != null;
        if (active && (!handlerRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(RunStartMessage, NetStats.Counted(RunStartMessage, OnRunStartMessage));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(RunReadyMessage, NetStats.Counted(RunReadyMessage, OnRunReadyMessage));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(RunGoMessage, NetStats.Counted(RunGoMessage, OnRunGoMessage));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(ClockPingMessage, NetStats.Counted(ClockPingMessage, OnClockPing));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(ClockPongMessage, NetStats.Counted(ClockPongMessage, OnClockPong));
            handlerRegistered = true;
            clockSamples = 0; clockRing = 0; clockBestRtt = double.MaxValue; clockOffset = 0.0;
            registeredManager = nm;
        }
        else if (!active && handlerRegistered)
        {
            handlerRegistered = false;
            registeredManager = null;
        }
        if (active) UpdateClockSync(nm);
        UpdateRunState();
    }

    // ===================================================================== //
    // 共通時計(JOINがHOSTの実時間との差を測る)
    // ===================================================================== //

    void UpdateClockSync(NetworkManager nm)
    {
        if (nm.IsServer || !nm.IsConnectedClient) return;
        clockPingTimer -= Time.unscaledDeltaTime;
        if (clockPingTimer > 0f) return;
        // 最初は細かく(0.2秒ごとに12回)、その後は2秒ごとに測り直す(長時間の時計のずれも追う)。
        clockPingTimer = clockSamples < 12 ? 0.2f : 2f;
        using (var writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(Time.realtimeSinceStartupAsDouble);
            NetStats.SendNamed(nm.CustomMessagingManager, ClockPingMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
        }
    }

    void OnClockPing(ulong senderClientId, FastBufferReader reader)
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || !nm.IsServer) return;
        reader.ReadValueSafe(out double t0);
        using (var writer = new FastBufferWriter(24, Allocator.Temp))
        {
            writer.WriteValueSafe(t0);
            writer.WriteValueSafe(Time.realtimeSinceStartupAsDouble);
            NetStats.SendNamed(nm.CustomMessagingManager, ClockPongMessage, senderClientId, writer, NetworkDelivery.Unreliable);
        }
    }

    void OnClockPong(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out double t0);
        reader.ReadValueSafe(out double hostTime);
        double t1 = Time.realtimeSinceStartupAsDouble;
        double rtt = t1 - t0;
        if (rtt < 0.0 || rtt > 2.0) return;
        // 片道 = 往復の半分とみなす。直近12回のうち往復が最も短いサンプルの差分を使う(混雑で遅れた回を捨てる)。
        clockRtt[clockRing] = rtt;
        clockOff[clockRing] = hostTime + rtt * 0.5 - t1;
        clockRing = (clockRing + 1) % clockRtt.Length;
        clockSamples++;
        int n = Mathf.Min(clockSamples, clockRtt.Length);
        int best = 0;
        for (int i = 1; i < n; i++) if (clockRtt[i] < clockRtt[best]) best = i;
        double prev = clockOffset;
        clockBestRtt = clockRtt[best];
        clockOffset = clockOff[best];
        if (clockSamples == 1 || clockSamples == 12 || System.Math.Abs(prev - clockOffset) > 0.005)
            NetSession.Log($"[CLOCK] sample#{clockSamples} rtt={rtt * 1000.0:F1}ms best={clockBestRtt * 1000.0:F1}ms offset={clockOffset:F4}s (changed {(clockOffset - prev) * 1000.0:F1}ms)");
    }

    // ===================================================================== //
    // RunState(WaitingForPlayers → Countdown → Running)
    // ===================================================================== //

    void UpdateRunState()
    {
        if (!IsMultiplayerRun || RunState == NetRunState.None || RunState >= NetRunState.Running) return;

        // セッションが切れた(HOSTが居なくなった等)ら、止まったままにせずこの端末だけで始める。
        if (!NetSession.IsActive)
        {
            NetSession.Log($"[RUN] session lost during {RunState} - starting locally");
            EnterRunning(double.NaN);
            return;
        }
        NetworkManager nm = NetSession.Manager;

        if (RunState == NetRunState.WaitingForPlayers)
        {
            if (!localReady) return;
            waitElapsed += Time.unscaledDeltaTime;
            if (nm.IsServer)
            {
                bool allReady = true;
                string missing = "";
                foreach (ulong id in nm.ConnectedClientsIds)
                    if (!readyClients.Contains(id)) { allReady = false; missing += id + " "; }
                if (allReady || waitElapsed > WaitForPlayersTimeout)
                {
                    if (!allReady) NetSession.Log($"[RUN] WaitingForPlayers timeout ({WaitForPlayersTimeout:F0}s) - starting without clientIds [{missing.Trim()}]");
                    HostDecideStartTime(nm);
                }
            }
            else
            {
                readyResendTimer -= Time.unscaledDeltaTime;
                if (readyResendTimer <= 0f) { readyResendTimer = ReadyResendInterval; SendReady(nm); }
            }
        }
        else if (RunState == NetRunState.Countdown)
        {
            if (!ClockReady) return;
            double now = NetNow;
            if (now >= goServerTime) EnterRunning(now);
        }
    }

    // HOST: 全員Ready → 共通のGO!の時刻を決めて配る。
    void HostDecideStartTime(NetworkManager nm)
    {
        double now = NetNow;
        goServerTime = now + CountdownLeadSeconds;
        RunState = NetRunState.Countdown;
        NetSession.Log($"[RUN] all players ready ({readyClients.Count}/{nm.ConnectedClientsIds.Count}) after {waitElapsed:F2}s -> RunStartNetworkTime={goServerTime:F3} (NetNow={now:F3}) state=Countdown");
        foreach (ulong clientId in nm.ConnectedClientsIds)
            if (clientId != NetworkManager.ServerClientId) SendGo(nm, clientId);
    }

    static void SendGo(NetworkManager nm, ulong clientId)
    {
        using (var writer = new FastBufferWriter(32, Allocator.Temp))
        {
            writer.WriteValueSafe(CurrentSeed);
            writer.WriteValueSafe(goServerTime);
            NetStats.SendNamed(nm.CustomMessagingManager, RunGoMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    static void SendReady(NetworkManager nm)
    {
        using (var writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(CurrentSeed);
            NetStats.SendNamed(nm.CustomMessagingManager, RunReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    // HOST: JOINからのReadyForRun。
    void OnRunReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || !nm.IsServer) return;
        reader.ReadValueSafe(out int seed);
        if (seed != CurrentSeed) { NetSession.Log($"[RUN] ignored ReadyForRun from clientId={senderClientId} (seed {seed} != {CurrentSeed})"); return; }
        if (readyClients.Add(senderClientId)) NetSession.Log($"[RUN] ReadyForRun from clientId={senderClientId} (state={RunState}, ready={readyClients.Count}/{nm.ConnectedClientsIds.Count})");
        // 既に時刻を決めた後に届いた(GOの通知を取りこぼした等) → もう一度その人へ送る。
        if (RunState >= NetRunState.Countdown && goServerTime > 0.0) SendGo(nm, senderClientId);
    }

    // JOIN: HOSTが決めたRunStartNetworkTime。
    void OnRunGoMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out int seed);
        reader.ReadValueSafe(out double go);
        if (seed != CurrentSeed) { NetSession.Log($"[RUN] ignored RunGo (seed {seed} != {CurrentSeed})"); return; }
        if (RunState != NetRunState.WaitingForPlayers) return;
        goServerTime = go;
        RunState = NetRunState.Countdown;
        double now = NetNow;
        NetSession.Log($"[RUN] RunStartNetworkTime={go:F3} received (NetNow={now:F3}, in {go - now:F3}s, clockSamples={clockSamples} bestRtt={ClockBestRttMs:F1}ms) state=Countdown");
    }

    void EnterRunning(double serverNow)
    {
        RunState = NetRunState.Running;
        LocalRunningServerTime = serverNow;
        LocalRunningUtc = System.DateTime.UtcNow;
        double ngo = NetSession.IsActive ? NetSession.Manager.ServerTime.Time : 0.0;
        NetSession.Log($"[RUN] state=Running utc={LocalRunningUtc:HH:mm:ss.fff} serverTime={serverNow:F4} RunStartNetworkTime={goServerTime:F4} late={(serverNow - goServerTime) * 1000.0:F1}ms frame={Time.frameCount} ngoServerTime={ngo:F4}");
    }

    // BeginMultiplayerRunの後に呼ぶ: この端末の準備完了(ReadyForRun)。
    static void MarkLocalReady()
    {
        if (!IsMultiplayerRun || localReady) return;
        localReady = true;
        if (!NetSession.IsActive) return;
        NetworkManager nm = NetSession.Manager;
        NetSession.Log($"[RUN] local ReadyForRun (server={nm.IsServer}) NetNow={NetNow:F3} clockSamples={clockSamples}");
        if (nm.IsServer) readyClients.Add(NetworkManager.ServerClientId);
        else { SendReady(nm); if (Instance != null) Instance.readyResendTimer = ReadyResendInterval; }
    }

    // Run終了(HOSTのRunOver/この端末のRun終了)。以降このRunで選択UIなどを出さない。
    public static void MarkFinished(string reason)
    {
        if (!IsMultiplayerRun || RunState == NetRunState.Finished || RunState == NetRunState.None) return;
        RunState = NetRunState.Finished;
        NetSession.Log($"[RUN] state=Finished ({reason})");
    }

    // GameManager.DepartFromStageSelectの先頭から呼ばれる。trueを返したらシングル用の出発処理は行わない。
    public static bool InterceptDepart(string stageId)
    {
        if (!NetSession.IsActive) return false;
        if (NetSession.IsClientOnly)
        {
            NetDebugUI.Toast("HOSTの出発を待っています");
            return true;
        }
        if (!NetSession.IsConnected) return false;
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (!p.IsOwner && p.Phase.Value == NetPlayer.PhaseInRun)
            {
                NetDebugUI.Toast("まだ走っているプレイヤーがいます");
                return true;
            }
        }

        NetworkManager nm = NetSession.Manager;
        int seed = Random.Range(1, int.MaxValue);
        double go = 0.0; // GO!の時刻は全員の準備完了をHOSTが確認してから決める(RunGo)
        MultiplayerGameMode mode = SelectedMode;
        readyClients.Clear();
        using (var writer = new FastBufferWriter(128, Allocator.Temp))
        {
            var stage = new FixedString64Bytes(stageId ?? "");
            writer.WriteValueSafe(stage);
            writer.WriteValueSafe(seed);
            writer.WriteValueSafe(go);
            writer.WriteValueSafe((byte)mode);
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                NetStats.SendNamed(nm.CustomMessagingManager, RunStartMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }
        NetSession.Log($"Run start broadcast stage={stageId} seed={seed} mode={mode} (start time is decided after all players are ready)");
        BeginRunReload(stageId, seed, go, mode);
        return true;
    }

    void OnRunStartMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes stage);
        reader.ReadValueSafe(out int seed);
        reader.ReadValueSafe(out double go);
        reader.ReadValueSafe(out byte mode);
        NetSession.Log($"Run start received from clientId={senderClientId} stage={stage} seed={seed} mode={(MultiplayerGameMode)mode}");
        BeginRunReload(stage.ToString(), seed, go, (MultiplayerGameMode)mode);
    }

    static void BeginRunReload(string stageId, int seed, double go, MultiplayerGameMode mode)
    {
        pending = true;
        pendingStageId = stageId;
        pendingSeed = seed;
        pendingMode = mode;
        goServerTime = 0.0; // RunStart通知の値は使わない(旧版との互換のため項目だけ残っている)
        localReady = false;
        RunState = NetRunState.None;
        TimeControl.ResetAll();
        int buildIndex = SceneManager.GetActiveScene().buildIndex;
        if (Instance != null && Instance.debugLoadDelay > 0f)
        {
            NetSession.Log($"[RUN] test: delaying scene load by {Instance.debugLoadDelay:F1}s");
            Instance.StartCoroutine(Instance.DelayedReload(buildIndex, Instance.debugLoadDelay));
            return;
        }
        ScreenTransitionManager stm = ScreenTransitionManager.Instance;
        if (stm != null && !stm.IsTransitioning) stm.PlayCloseThenReload(() => SceneManager.LoadScene(buildIndex));
        else SceneManager.LoadScene(buildIndex);
    }

    IEnumerator DelayedReload(int buildIndex, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        SceneManager.LoadScene(buildIndex);
    }

    // sceneLoadedは読み込んだシーンのAwake後・Start前に呼ばれる - TerrainManager.Startが
    // 初期地形を生成する前にWorldRngを初期化できる。
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pending)
        {
            pending = false;
            IsMultiplayerRun = true;
            ActiveRunSeed = pendingSeed;
            ActiveRunStageId = pendingStageId;
            ActiveMode = pendingMode;
            WorldRng.BeginDeterministic(pendingSeed);
            waitElapsed = 0f;
            RunState = NetRunState.WaitingForPlayers;
            NetSession.Log($"Multiplayer run scene ready stage={pendingStageId} seed={pendingSeed} mode={ActiveMode} state=WaitingForPlayers");
            StartCoroutine(BeginRunNextFrame(pendingStageId));
        }
        else
        {
            IsMultiplayerRun = false;
            ActiveRunSeed = 0;
            ActiveRunStageId = "";
            goServerTime = 0.0;
            localReady = false;
            RunState = NetRunState.None;
            WorldRng.EndDeterministic();
        }
    }

    IEnumerator BeginRunNextFrame(string stageId)
    {
        // 全オブジェクトのStart(地形の初期生成を含む)を終えてから開始する。
        yield return null;
        if (GameManager.Instance != null) GameManager.Instance.BeginMultiplayerRun(stageId);
        MarkLocalReady();
    }
}

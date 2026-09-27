using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// マルチプレイ対応Phase 1(2026-09-25) - 「全員で同じRunを同時に始める」係。
//
// 流れ:
//  1. HOSTがStage Selectで出発 → GameManager.DepartFromStageSelectがInterceptDepartを呼ぶ
//  2. HOSTがWorldSeed(地形の種)とステージID、GO!の時刻(サーバー時刻)を全員へ送る
//  3. 全端末がシーンを読み込み直す(RESULT→TOPと同じ既存のリロード経路) - 読み込み直後に
//     WorldRngをそのSeedで初期化するので、TerrainManager.Start以降の地形が全端末で一致する
//  4. 読み込み完了の1フレーム後にGameManager.BeginMultiplayerRunでRunを開始し、
//     カウントダウンは「GO!の時刻」に合わせて待つ(ShouldHoldCountdown) - 読み込み速度の差を吸収
//
// シングルプレイ(セッション無し)では一切何もしない。
//
// Phase 3(2026-09-27): HOSTが選んだゲームモード(CO-OP/VERSUS)もRun開始の通知に載せて配る。
// 各端末はRun中そのモードだけを使う(JOIN側の設定は使わない = HOSTの選択がセッション全体の正解)。
public enum MultiplayerGameMode : byte { Coop = 0, Versus = 1 }

public class NetRunLauncher : MonoBehaviour
{
    const string RunStartMessage = "OMM.RunStart";
    const double StartLeadSeconds = 4.0; // 出発からGO!までの余裕(シーン再読み込み+カウントダウン)
    const float MaxCountdownHold = 8f;   // 通信が途切れても無限に待たない安全策

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
    float holdElapsed;

    bool handlerRegistered;
    NetworkManager registeredManager;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
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
            nm.CustomMessagingManager.RegisterNamedMessageHandler(RunStartMessage, OnRunStartMessage);
            handlerRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlerRegistered)
        {
            handlerRegistered = false;
            registeredManager = null;
        }
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
                NetDebugUI.Toast("相手プレイヤーがまだ走っています");
                return true;
            }
        }

        NetworkManager nm = NetSession.Manager;
        int seed = Random.Range(1, int.MaxValue);
        double go = nm.ServerTime.Time + StartLeadSeconds;
        MultiplayerGameMode mode = SelectedMode;
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
                nm.CustomMessagingManager.SendNamedMessage(RunStartMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }
        NetSession.Log($"Run start broadcast stage={stageId} seed={seed} goServerTime={go:F2} mode={mode}");
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
        goServerTime = go;
        TimeControl.ResetAll();
        int buildIndex = SceneManager.GetActiveScene().buildIndex;
        ScreenTransitionManager stm = ScreenTransitionManager.Instance;
        if (stm != null && !stm.IsTransitioning) stm.PlayCloseThenReload(() => SceneManager.LoadScene(buildIndex));
        else SceneManager.LoadScene(buildIndex);
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
            holdElapsed = 0f;
            NetSession.Log($"Multiplayer run scene ready stage={pendingStageId} seed={pendingSeed} mode={ActiveMode}");
            StartCoroutine(BeginRunNextFrame(pendingStageId));
        }
        else
        {
            IsMultiplayerRun = false;
            ActiveRunSeed = 0;
            ActiveRunStageId = "";
            goServerTime = 0.0;
            WorldRng.EndDeterministic();
        }
    }

    IEnumerator BeginRunNextFrame(string stageId)
    {
        // 全オブジェクトのStart(地形の初期生成を含む)を終えてから開始する。
        yield return null;
        if (GameManager.Instance != null) GameManager.Instance.BeginMultiplayerRun(stageId);
    }

    // GameManagerのカウントダウン開始前に毎フレーム呼ばれる。leadSeconds=「3」表示からGO!までの秒数。
    public static bool ShouldHoldCountdown(float leadSeconds)
    {
        if (!IsMultiplayerRun || goServerTime <= 0.0 || Instance == null) return false;
        if (!NetSession.IsActive) return false;
        Instance.holdElapsed += Time.unscaledDeltaTime;
        if (Instance.holdElapsed > MaxCountdownHold) return false;
        return NetSession.Manager.ServerTime.Time < goServerTime - leadSeconds;
    }
}

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 高速時の自動スローモーション(2026-09-27 試験実装)。
//
// 走行速度(スロー適用前の継続的なAuto Run速度 = PlayerController.CurrentAutoRunSpeed。
// 突進/ノックバック/落下/復帰の座標補正は含まない)が startSpeed を超えたら、fullSlowSpeed で
// minScale になるよう、ゲーム世界全体の時間倍率(TimeControlの自動スロー層)を下げる。
//  - 目標倍率は速度の一次関数。fullSlowSpeed >= 1.5 × startSpeed を保証しているので、
//    「速度 × 倍率」(画面上の前進速度)は速度が上がるほど必ず増える(速度アップの優位を保つ)。
//  - 速度は低域通過で平滑化し、開始付近にはヒステリシスを設ける(境界での頻繁な切り替えを防ぐ)。
//  - 倍率の変化は実時間で transitionTime かけて1.0⇔minScale を移動する速さに制限する。
//  - 完全停止(カード選択/ポーズ/HitStop)とボス登場演出はTimeControl側で優先され、
//    解除後はここで決めた倍率へ戻る。この処理自体は常に実時間(unscaled)で動く。
//
// マルチ: HOSTだけが「参加中かつ走行中のプレイヤーのうち最も速い走行速度」で倍率を決め、
// 10Hzで全員へ送る(OMM.WorldTime)。参加側は受け取った倍率をそのまま使い、自分では判断しない。
// ON/OFFもHOSTだけが切り替え、全員へ反映する。
[DefaultExecutionOrder(-900)]
public class AutoSlowMotion : MonoBehaviour
{
    public static AutoSlowMotion Instance { get; private set; }

    [Header("調整値(実時間/メートル毎秒)")]
    public bool autoSlowEnabled = true;
    [Tooltip("この走行速度(m/s)を超えるとスローが始まる。基本5m/s(18km/h)×1.4倍=約900m地点")]
    public float startSpeed = 7f;
    [Tooltip("この走行速度(m/s)で最小倍率に達する(startSpeedの1.5倍以上に自動補正)")]
    public float fullSlowSpeed = 12f;
    [Range(0.5f, 1f)] public float minScale = 0.75f;
    [Tooltip("1.0⇔最小倍率を移動するのにかかる実時間(秒)")]
    public float transitionTime = 1.5f;
    [Tooltip("走行速度の平滑化の時定数(実時間秒)。カード取得などの段差をなめらかにする")]
    public float speedSmoothing = 0.6f;
    [Tooltip("スロー開始付近のヒステリシス(m/s)")]
    public float hysteresis = 0.3f;

    // ---- 状態(デバッグ表示用に公開) ----
    public float CurrentAutoScale { get; private set; } = 1f;
    public float TargetScale { get; private set; } = 1f;
    public float JudgedSpeed { get; private set; }       // 平滑化前: 判定に使っている最高走行速度
    public float SmoothedSpeed { get; private set; }
    public bool IsNetworkFollower { get; private set; }   // 参加側としてHOSTの値に従っている
    public int ContributingPlayers { get; private set; }

    bool engaged;
    int seenResetGeneration = -1;
    float netSendTimer;
    float lastNetReceiveRealtime = -99f;
    float netScale = 1f;
    bool handlersRegistered;
    NetworkManager registeredManager;

    const string MsgWorldTime = "OMM.WorldTime"; // HOST→JOIN: 自動スローの倍率/ON-OFF/判定速度

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[AutoSlowMotion]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<AutoSlowMotion>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        autoSlowEnabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
        // リトライ/ホーム帰還はシーンの読み直し - 前のRunのスロー状態を持ち越さない
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => TimeControl.ResetAutoScale();
    }

    const string PrefKey = "AutoSlowMotionEnabled";

    // デバッグ画面のON/OFF。マルチの参加側は変更できない(HOSTの設定に従う)。
    public bool CanToggle => !NetCombat.Replica;

    public void SetEnabled(bool on)
    {
        if (!CanToggle) return;
        autoSlowEnabled = on;
        PlayerPrefs.SetInt(PrefKey, on ? 1 : 0);
        PlayerPrefs.Save();
        netSendTimer = 999f; // マルチのHOSTなら次のフレームですぐ全員へ伝える
    }

    // 速度→目標倍率。fullSlowSpeedはstartSpeedの1.5倍以上に補正(画面上の前進速度 v×s(v) が単調増加する条件)。
    public float ScaleForSpeed(float v)
    {
        float v0 = Mathf.Max(0.1f, startSpeed);
        float v1 = Mathf.Max(fullSlowSpeed, v0 * 1.5f);
        if (v <= v0) return 1f;
        float k = Mathf.Clamp01((v - v0) / (v1 - v0));
        return Mathf.Lerp(1f, minScale, k);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        RegisterHandlers();

        if (TimeControl.ResetGeneration != seenResetGeneration)
        {
            // リトライ/ホーム帰還/ゲームオーバー: スロー状態を持ち越さない
            seenResetGeneration = TimeControl.ResetGeneration;
            CurrentAutoScale = 1f; TargetScale = 1f; SmoothedSpeed = 0f; JudgedSpeed = 0f; engaged = false;
        }

        bool replica = NetCombat.Replica;
        // 参加側でなくなった(切断/Run終了)ら、HOSTから受け取ったON/OFFではなく自分の設定へ戻す
        if (IsNetworkFollower && !replica) autoSlowEnabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
        IsNetworkFollower = replica;
        float rate = (1f - minScale) / Mathf.Max(0.05f, transitionTime);

        if (replica)
        {
            // 参加側: HOSTが決めた倍率に従う(届かない間は最後の値を保つ。1.5秒以上途絶えたら1へ戻す)。
            float target = Time.realtimeSinceStartup - lastNetReceiveRealtime < 1.5f ? netScale : 1f;
            TargetScale = target;
            // 10Hzの段差を目立たせないよう、同じ速さの制限で追従する
            CurrentAutoScale = Mathf.MoveTowards(CurrentAutoScale, target, rate * 2f * dt);
        }
        else
        {
            bool running = IsRunActive();
            int n = 0;
            float v = running ? MaxParticipantSpeed(out n) : 0f;
            ContributingPlayers = running ? n : 0;
            JudgedSpeed = v;
            // 低域通過(実時間)
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, speedSmoothing));
            SmoothedSpeed = running ? Mathf.Lerp(SmoothedSpeed, v, a) : 0f;

            // ヒステリシス: 一度入ったら startSpeed - hysteresis を下回るまで抜けない
            if (!engaged && SmoothedSpeed > startSpeed + hysteresis) engaged = true;
            else if (engaged && SmoothedSpeed < startSpeed - hysteresis) engaged = false;

            float target = autoSlowEnabled && running && engaged ? ScaleForSpeed(SmoothedSpeed) : 1f;
            TargetScale = target;
            CurrentAutoScale = Mathf.MoveTowards(CurrentAutoScale, target, rate * dt);

            if (NetCombat.Authority) BroadcastWorldTime(dt);
        }

        TimeControl.SetAutoScale(CurrentAutoScale);
    }

    static bool IsRunActive()
    {
        GameManager gm = GameManager.Instance;
        PlayerController pc = PlayerController.Instance;
        if (gm == null || pc == null) return false;
        if (!gm.HasStarted || gm.IsGameOver || gm.CountdownActive) return false;
        return true;
    }

    // 判定に使う走行速度: 自分(走行中なら) + マルチのHOSTでは参加中かつ走行中の相手(直近1秒以内に
    // 走行中として報告してきた人だけ)。ダウン/離脱/切断した人の古い速度は残さない。
    float MaxParticipantSpeed(out int count)
    {
        count = 0;
        float v = 0f;
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        if (pc != null && gm != null && !gm.IsGameOver && !pc.IsDeadPosing && !pc.IsFinishing)
        {
            v = Mathf.Max(v, pc.CurrentAutoRunSpeed);
            count++;
        }
        if (NetCombat.Authority)
        {
            float now = Time.realtimeSinceStartup;
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p == null || p.IsOwner || !p.IsSpawned) continue;
                if (!p.IsRunningRemote(now)) continue;
                v = Mathf.Max(v, p.RemoteRunSpeed);
                count++;
            }
        }
        return v;
    }

    // ===================== マルチ: HOST→参加側 ===================== //

    void RegisterHandlers()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!handlersRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgWorldTime, OnWorldTime);
            handlersRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlersRegistered)
        {
            handlersRegistered = false;
            registeredManager = null;
            lastNetReceiveRealtime = -99f;
        }
    }

    void BroadcastWorldTime(float dt)
    {
        netSendTimer += dt;
        if (netSendTimer < 0.1f) return;
        netSendTimer = 0f;
        NetworkManager nm = NetSession.Manager;
        if (nm == null || nm.CustomMessagingManager == null) return;
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(autoSlowEnabled);
            w.WriteValueSafe(CurrentAutoScale);
            w.WriteValueSafe(JudgedSpeed);
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(MsgWorldTime, clientId, w, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    void OnWorldTime(ulong sender, FastBufferReader r)
    {
        if (sender != NetworkManager.ServerClientId) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out bool enabledOnHost);
        r.ReadValueSafe(out float scale);
        r.ReadValueSafe(out float judged);
        if (seed != 0 && seed != NetRunLauncher.ActiveRunSeed) return; // 別のRunの値は使わない
        autoSlowEnabled = enabledOnHost;
        netScale = Mathf.Clamp(scale, 0.05f, 1f);
        JudgedSpeed = judged;
        lastNetReceiveRealtime = Time.realtimeSinceStartup;
    }
}

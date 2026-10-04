using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 接続の窓口(HOST開始/JOIN/切断/承認/ログ)。
//
// 方式: Unity Netcode for GameObjects 2.x + Unity Transport(UDP)。
//  - LOCAL(同一Wi-Fi/テザリング)は直接IP指定で接続する。インターネットは不要。
//  - 将来ONLINEへ拡張する場合も、ゲーム側のコード(NetPlayer/NetRunLauncher等)はそのままで、
//    ここのTransport設定(UnityTransport.SetRelayServerData等)を差し替えるだけで済む構造にしてある。
//
// シングルプレイへの影響を避けるため、NetworkManagerはシーンに置かず、プレイヤーが
// 「LOCAL MULTIPLAYER」からHOST/JOINを押した時に初めて実行時に生成する。
// 一度もマルチプレイを使わなければNetcodeは一切動かない。
public class NetSession : MonoBehaviour
{
    // 2026-10-02: 正式なマルチは最大8人の予定(PlannedMaxPlayers)。
    // 2026-10-05(LAN の自動発見): 部屋は最大8人(WAITING FOR PLAYERS 1/8)。開発版だけ起動引数 -netMaxPlayers N(2〜8)で人数を絞れる。
    public const int DefaultMaxPlayers = PlannedMaxPlayers;
    public const int PlannedMaxPlayers = 8;
    static int maxPlayersOverride = -1;
    public static int MaxPlayers
    {
        get
        {
            if (maxPlayersOverride < 0)
            {
                maxPlayersOverride = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-netMaxPlayers" && int.TryParse(a[i + 1], out int n)) maxPlayersOverride = Mathf.Clamp(n, 2, PlannedMaxPlayers);
#endif
            }
            return maxPlayersOverride > 0 ? maxPlayersOverride : DefaultMaxPlayers;
        }
    }
    public const ushort DefaultPort = 7777;
    // 通信仕様のバージョン。互換性の無い変更をしたら上げること(承認時に一致を確認する)。
    public static readonly string ProtocolVersion = "OMM-NET-" + LanDiscovery.AdvertisedProtocol; // LAN の部屋の知らせと同じ版(2026-10-05。開発ビルドの -lanProtocol で版違いを試せる)
    const string PlayerPrefabResourcePath = "Net/NetPlayer";

    public static NetSession Instance { get; private set; }
    public static NetworkManager Manager { get; private set; }

    public static bool IsActive => Manager != null && (Manager.IsServer || Manager.IsClient) && !Manager.ShutdownInProgress;
    public static bool IsHost => IsActive && Manager.IsHost;
    public static bool IsClientOnly => IsActive && Manager.IsClient && !Manager.IsHost;
    public static bool IsConnected => IsHost || (IsClientOnly && Manager.IsConnectedClient);
    public static int ConnectedPlayerCount => IsActive ? NetPlayer.All.Count : 0;

    public string StatusText { get; private set; } = "";
    // LAN の部屋へ JOIN した時の結果(2026-10-05): 接続できなかった/断られた理由(VERSION MISMATCH / ROOM FULL / RUN IN PROGRESS / 接続できない)
    public static string LastJoinFailure { get; private set; } = "";
    public static bool JoinPending { get; private set; }
    public static ConnectionType Connection { get; private set; } = ConnectionType.Local;
    public static void ClearJoinFailure() => LastJoinFailure = "";
    public string LastHostAddress { get; private set; } = "";
    public ushort LastPort { get; private set; } = DefaultPort;

    // 相手の切断/自分の切断を画面に出すための通知(NetDebugUIが表示し、プレイヤーが閉じる)。
    public bool ConnectionLostPending { get; private set; }
    public string ConnectionLostMessage { get; private set; } = "";

    UnityTransport transport;

    public static void Log(string message) => Debug.Log("[NET] " + message);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[NetSession]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetSession>();
        go.AddComponent<NetDebugUI>();
        go.AddComponent<NetRunLauncher>();
        go.AddComponent<LanDiscovery>(); // LAN の部屋の自動発見(2026-10-05、ゲームの同期とは別)
        if (NetAutoTest.ShouldRun) go.AddComponent<NetAutoTest>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    NetworkManager EnsureManager()
    {
        if (Manager != null) return Manager;

        GameObject prefab = Resources.Load<GameObject>(PlayerPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[NET] Resources/" + PlayerPrefabResourcePath + " が見つかりません(Tools/OneMoreMile/Build Net Prefabs を実行してください)");
            return null;
        }

        var go = new GameObject("[NetworkManager]");
        DontDestroyOnLoad(go);
        transport = go.AddComponent<UnityTransport>();
        var nm = go.AddComponent<NetworkManager>();
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = prefab,
            ConnectionApproval = true,
            // シーンは両端末が同じものを自分で読み込む(ネット越しにシーンを同期しない)。
            EnableSceneManagement = false,
            TickRate = 30,
        };
        nm.ConnectionApprovalCallback = ApproveConnection;
        nm.OnServerStarted += () => { Log($"Host started (port {LastPort}, max players {MaxPlayers})"); StatusText = "HOST: 参加待ち"; };
        nm.OnServerStopped += _ => { Log("Host stopped"); LanDiscovery.StopAdvertising("host stopped"); };
        nm.OnClientStopped += _ => { Log("Client stopped"); if (JoinPending) { JoinPending = false; if (string.IsNullOrEmpty(LastJoinFailure)) LastJoinFailure = "CONNECTION FAILED"; Debug.Log($"[LAN] Join failed {LastHostAddress}:{LastPort} ({LastJoinFailure})"); } };
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        nm.OnTransportFailure += OnTransportFailure;
        Manager = nm;
        return nm;
    }

    void ConfigureTransport(string address, ushort port, string listenAddress)
    {
        // 接続試行は1秒×10回(=約10秒で諦める)、無通信8秒で切断扱い。
        transport.ConnectTimeoutMS = 1000;
        transport.MaxConnectAttempts = 10;
        transport.DisconnectTimeoutMS = 8000;
        transport.SetConnectionData(address, port, listenAddress);
        Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(ProtocolVersion);
    }

    public bool StartHost(ushort port)
    {
        if (IsActive) return false;
        NetworkManager nm = EnsureManager();
        if (nm == null) { StatusText = "初期化エラー"; return false; }
        if (nm.ShutdownInProgress) { StatusText = "前の接続を終了中です。少し待ってから再試行してください"; return false; }

        ClearConnectionLost();
        LastPort = port;
        // 0.0.0.0で待ち受け - 同じWi-Fi/テザリング内の他端末から接続できる。
        ConfigureTransport("127.0.0.1", port, "0.0.0.0");
        Application.runInBackground = true;
        bool ok = nm.StartHost();
        if (!ok)
        {
            Log("Host start failed");
            StatusText = "HOSTを開始できませんでした(ポート使用中の可能性)";
        }
        return ok;
    }

    public bool StartClient(string hostAddress, ushort port)
    {
        if (IsActive) return false;
        hostAddress = (hostAddress ?? "").Trim();
        if (!IPAddress.TryParse(hostAddress, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            StatusText = "IPアドレスの形式が正しくありません(例: 192.168.1.23)";
            return false;
        }
        NetworkManager nm = EnsureManager();
        if (nm == null) { StatusText = "初期化エラー"; return false; }
        if (nm.ShutdownInProgress) { StatusText = "前の接続を終了中です。少し待ってから再試行してください"; return false; }

        ClearConnectionLost();
        LastHostAddress = hostAddress;
        LastPort = port;
        ConfigureTransport(hostAddress, port, null);
        Application.runInBackground = true;
        Log($"Client connecting to {hostAddress}:{port}");
        StatusText = $"接続中… {hostAddress}:{port}";
        LastJoinFailure = ""; JoinPending = true;
        bool ok = nm.StartClient();
        if (!ok)
        {
            Log("Client start failed");
            StatusText = "JOINを開始できませんでした";
            JoinPending = false; LastJoinFailure = "CONNECTION FAILED";
            Debug.Log($"[LAN] Join failed {hostAddress}:{port} (client start failed)");
        }
        return ok;
    }

    // 自分から切断する(HOSTならセッション終了)。
    public void Leave()
    {
        if (Manager == null || !(Manager.IsServer || Manager.IsClient)) return;
        Log(Manager.IsHost ? "Host stopping (local leave)" : "Client leaving");
        LanDiscovery.StopAdvertising("leave");
        JoinPending = false;
        Manager.Shutdown();
        StatusText = "";
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool isHostSelf = request.ClientNetworkId == NetworkManager.ServerClientId;
        string payload = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
        response.Pending = false;
        response.CreatePlayerObject = true;
        if (!isHostSelf && payload != ProtocolVersion)
        {
            response.Approved = false;
            response.Reason = "VERSION MISMATCH";
            Log($"Rejected clientId={request.ClientNetworkId}: version '{payload}' != '{ProtocolVersion}'");
            return;
        }
        // 途中参加は不可(Run開始の名簿に入っていない人は、Readyを送れず/走れない)。マルチのRunのシーンにいる間は断る(2026-10-02)
        if (!isHostSelf && (NetRunLauncher.IsMultiplayerRun || NetRunLauncher.RunState != NetRunState.None))
        {
            response.Approved = false;
            response.Reason = "RUN IN PROGRESS";
            Log($"Rejected clientId={request.ClientNetworkId}: a run is in progress ({NetRunLauncher.RunState})");
            return;
        }
        if (!isHostSelf && Manager.ConnectedClientsIds.Count >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "ROOM FULL";
            Log($"Rejected clientId={request.ClientNetworkId}: room full ({MaxPlayers})");
            return;
        }
        response.Approved = true;
    }

    void OnClientConnected(ulong clientId)
    {
        if (Manager.IsServer)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                Log($"Client connected clientId={clientId}");
                StatusText = $"HOST: {Manager.ConnectedClientsIds.Count}/{MaxPlayers}人 接続中";
            }
        }
        else if (clientId == Manager.LocalClientId)
        {
            Log($"Client connected (local clientId={clientId})");
            StatusText = $"JOIN: 接続完了 {LastHostAddress}";
            if (JoinPending) Debug.Log($"[LAN] Join succeeded {LastHostAddress}:{LastPort}");
            JoinPending = false; LastJoinFailure = "";
            LanDiscovery.StopDiscovery("joined");
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (Manager.IsServer && clientId != NetworkManager.ServerClientId)
        {
            int pn = NetCombat.PlayerNumberOfClient(clientId);
            if (pn <= 0 && NetMatch.Instance != null) foreach (var kv in NetMatch.Instance.Records) if (kv.Value.ClientId == clientId) pn = kv.Key; // 退出時は NetPlayer が先に消えていることがある
            int others = 0;
            foreach (ulong id in Manager.ConnectedClientsIds) if (id != NetworkManager.ServerClientId && id != clientId) others++;
            Log($"Client disconnected clientId={clientId} (P{pn}) remaining others={others}");
            StatusText = $"HOST: {1 + others}/{MaxPlayers}人 接続中";
            // 2026-10-02(最大8人の予定): まだ他の参加者がいる時は通知だけ。全員いなくなった時は従来どおりの画面
            if (others > 0) NetDebugUI.Toast($"P{(pn > 0 ? pn.ToString() : "?")} が切断しました(残り{1 + others}人)");
            else RaiseConnectionLost("MULTIPLAYER CONNECTION LOST\n" + (pn > 0 ? $"P{pn}" : "相手プレイヤー") + "が切断しました");
            return;
        }
        if (!Manager.IsServer)
        {
            string reason = Manager.DisconnectReason;
            bool wasConnected = clientId == Manager.LocalClientId || clientId == NetworkManager.ServerClientId;
            Log($"Connection lost (clientId={clientId}{(string.IsNullOrEmpty(reason) ? "" : ", reason=" + reason)})");
            StatusText = "";
            string msg = string.IsNullOrEmpty(reason) ? "HOSTとの接続が切れました" : "接続が拒否されました: " + reason;
            if (JoinPending)
            {
                // LAN の部屋の一覧から JOIN して接続できなかった → 一覧の画面で「CONNECTION FAILED」(全画面の切断表示は出さない)
                JoinPending = false;
                LastJoinFailure = reason == "VERSION MISMATCH" || reason == "ROOM FULL" || reason == "RUN IN PROGRESS" ? reason : "CONNECTION FAILED"; // 通信の生の理由(接続の試行切れ等)は表示しない
                Debug.Log($"[LAN] Join failed {LastHostAddress}:{LastPort} ({LastJoinFailure})");
                return;
            }
            RaiseConnectionLost("MULTIPLAYER CONNECTION LOST\n" + (wasConnected ? msg : "HOSTへ接続できませんでした"));
        }
    }

    void OnTransportFailure()
    {
        Log("Transport failure");
        StatusText = "";
        RaiseConnectionLost("MULTIPLAYER CONNECTION LOST\n通信エラーが発生しました");
    }

    void RaiseConnectionLost(string message)
    {
        ConnectionLostPending = true;
        ConnectionLostMessage = message;
    }

    public void ClearConnectionLost()
    {
        ConnectionLostPending = false;
        ConnectionLostMessage = "";
    }

    // 画面に表示する「この端末のIPアドレス」候補。Androidでは一部APIが制限されているため、
    // 取れる方法を順に試して重複を除く(どれも失敗したら空 - 端末のWi-Fi設定画面で確認してもらう)。
    public static List<string> GetLocalIPv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = ua.Address.ToString();
                    if (s.StartsWith("127.") || s.StartsWith("169.254.")) continue;
                    if (!result.Contains(s)) result.Add(s);
                }
            }
        }
        catch (Exception) { }

        if (result.Count == 0)
        {
            // 経路表から既定の送信元アドレスを得る(実際にはパケットを送らない)。
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("10.255.255.255", 65530);
                    if (socket.LocalEndPoint is IPEndPoint ep)
                    {
                        string s = ep.Address.ToString();
                        if (!s.StartsWith("127.") && !result.Contains(s)) result.Add(s);
                    }
                }
            }
            catch (Exception) { }
        }
        return result;
    }
}

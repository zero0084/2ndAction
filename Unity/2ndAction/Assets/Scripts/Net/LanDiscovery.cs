using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// LAN の部屋の自動発見(2026-10-05)。ゲームの同期(NGO/UTP)とは別の、小さな UDP の知らせだけを扱う。
//  ・HOST: 部屋の情報(RoomId/名前/モード/人数/最大/版/ポート/状態)を 1 秒ごとに LAN へブロードキャスト(255.255.255.255 と
//    各ネットワークの「その網の全員」宛て。テザリングの親機の網にも届くように)。探している側の「誰かいますか」にもすぐ答える。
//  ・探す側: 知らせを受けて部屋の一覧を作る(差出人の IP を接続先にする)。lostSeconds 秒聞こえなければ一覧から消す。
//  ・Android: 受信には MulticastLock が要る端末が多い → 探している間/知らせている間だけ取り、止めたら必ず放す(LanMulticastLock)。
//  ・Unity Android(IL2CPP)でそのまま動く System.Net.Sockets の UDP だけ(Java の NSD/mDNS は使わない = ネイティブのプラグイン不要)。
//  ・ソケットは非ブロッキングで、メインスレッドの Update から 0.1 秒ごとに読む(スレッドを使わない)。
public enum ConnectionType { Local, Online } // ONLINE は将来(今は LOCAL = 同じ LAN だけ)

public class LanRoom
{
    public string roomId, roomName, gameVersion, address;
    public MultiplayerGameMode mode;
    public int players, maxPlayers, protocol;
    public ushort port;
    public bool inProgress;
    public float lastSeen;
    public bool Full => players >= maxPlayers;
    public bool VersionOk => protocol == LanDiscovery.MultiplayerProtocolVersion;
    public bool Joinable => VersionOk && !Full && !inProgress;
    public string StateLabel => !VersionOk ? "VERSION MISMATCH" : inProgress ? "IN PROGRESS" : Full ? "FULL" : "OPEN";
}

public class LanDiscovery : MonoBehaviour
{
    public const int MultiplayerProtocolVersion = 1;   // 部屋の知らせ+ゲームの同期の版(違えば一覧で VERSION MISMATCH、接続も断る)
    // 知らせに載せる版(開発ビルドだけ -lanProtocol N で変えて、版違いの表示を確かめられる)
    public static int AdvertisedProtocol
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-lanProtocol" && int.TryParse(a[i + 1], out int v)) return v;
#endif
            return MultiplayerProtocolVersion;
        }
    }
    public const int DiscoveryPort = 47777;
    const string Magic = "OMMLAN";
    public static float AdvertiseInterval = 1.0f;     // 知らせる間隔(毎フレームではない)
    public static float LostSeconds = 3.5f;           // これだけ聞こえなければ一覧から消す
    public static float ProbeInterval = 1.5f;

    public static LanDiscovery Instance { get; private set; }
    public static bool Advertising { get; private set; }
    public static bool Discovering { get; private set; }
    public static readonly List<LanRoom> Rooms = new List<LanRoom>();
    public static float DiscoveryStartedAt { get; private set; }
    public static string LastError { get; private set; } = "";
    public static int PacketsSent { get; private set; }
    public static int PacketsReceived { get; private set; }

    // HOST の部屋(一度決めたら同じ部屋の間は変えない)
    public static string RoomId { get; private set; } = "";
    public static string RoomName
    {
        get { var n = PlayerPrefs.GetString("net.roomName", ""); return string.IsNullOrEmpty(n) ? DefaultRoomName() : n; }
        set { PlayerPrefs.SetString("net.roomName", value ?? ""); PlayerPrefs.Save(); }
    }

    Socket sock;
    float advTimer, probeTimer, pollTimer;
    readonly byte[] buf = new byte[1024];

    public static void Ensure(GameObject host)
    {
        if (Instance == null && host != null) host.AddComponent<LanDiscovery>();
    }

    void Awake() { Instance = this; }

    static string DefaultRoomName()
    {
        string who = SystemInfo.deviceName;
        if (string.IsNullOrEmpty(who) || who == SystemInfo.unsupportedIdentifier || who.Length > 18) who = "PLAYER";
        return who + "'s Room";
    }

    // ---- HOST ----
    public static void StartAdvertising()
    {
        if (Instance == null) return;
        if (string.IsNullOrEmpty(RoomId)) RoomId = Guid.NewGuid().ToString("N").Substring(0, 8);
        if (!Advertising) Debug.Log($"[LAN] Advertising room {RoomId} '{RoomName}' {NetRunLauncher.SelectedMode} port {NetSession.Instance?.LastPort}");
        Advertising = true;
        Instance.advTimer = 0f;
        Instance.Refresh();
    }

    public static void StopAdvertising(string why)
    {
        if (!Advertising) return;
        Advertising = false;
        Debug.Log($"[LAN] Advertising stopped ({why})");
        RoomId = "";
        if (Instance != null) Instance.Refresh();
    }

    // ---- 探す側 ----
    public static void StartDiscovery()
    {
        if (Instance == null) return;
        Rooms.Clear();
        DiscoveryStartedAt = Time.unscaledTime;
        if (!Discovering) Debug.Log("[LAN] Discovery started");
        Discovering = true;
        Instance.probeTimer = 0f;
        Instance.Refresh();
    }

    public static void StopDiscovery(string why)
    {
        if (!Discovering) return;
        Discovering = false;
        Rooms.Clear();
        Debug.Log($"[LAN] Discovery stopped ({why})");
        if (Instance != null) Instance.Refresh();
    }

    // 再検索(一覧を空にしてすぐに問い合わせる)
    public static void Rescan()
    {
        if (!Discovering) { StartDiscovery(); return; }
        Rooms.Clear();
        DiscoveryStartedAt = Time.unscaledTime;
        if (Instance != null) Instance.probeTimer = 0f;
        Debug.Log("[LAN] Discovery rescan");
    }

    // 必要な時だけソケット/MulticastLock を持つ
    void Refresh()
    {
        bool need = Advertising || Discovering;
        if (need && sock == null) Open();
        else if (!need && sock != null) Close();
        LanMulticastLock.Set(need);
    }

    void Open()
    {
        try
        {
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sock.EnableBroadcast = true;
            sock.Blocking = false;
            sock.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            LastError = "";
        }
        catch (Exception e)
        {
            // 同じ端末で2つ動かす(Windows のテスト)と、2つ目は受信用のポートを取れないことがある → 送信だけの臨時ポートで続ける
            LastError = e.Message;
            Debug.LogWarning($"[LAN] bind {DiscoveryPort} failed ({e.Message}) - using an ephemeral port");
            try
            {
                sock?.Close();
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, Blocking = false };
                sock.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            catch (Exception e2) { LastError = e2.Message; Debug.LogWarning("[LAN] socket failed: " + e2.Message); sock = null; }
        }
    }

    void Close()
    {
        try { sock?.Close(); } catch { }
        sock = null;
    }

    void OnDisable() { Close(); LanMulticastLock.Set(false); }
    void OnApplicationQuit() { StopAdvertising("quit"); StopDiscovery("quit"); Close(); LanMulticastLock.Set(false); }
    void OnApplicationPause(bool paused)
    {
        // バックグラウンドの間は知らせない/探さない(MulticastLock も放す)。戻ったら再開
        if (paused) { Close(); LanMulticastLock.Set(false); }
        else Refresh();
    }

    void Update()
    {
        if (sock == null) return;
        float dt = Time.unscaledDeltaTime;
        if (Advertising)
        {
            advTimer -= dt;
            if (advTimer <= 0f) { advTimer = AdvertiseInterval; Broadcast(Encode(), "advertise"); }
        }
        if (Discovering)
        {
            probeTimer -= dt;
            if (probeTimer <= 0f) { probeTimer = ProbeInterval; Broadcast(Encoding.UTF8.GetBytes(Magic + "?|" + MultiplayerProtocolVersion), "probe"); }
            for (int i = Rooms.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - Rooms[i].lastSeen > LostSeconds)
                {
                    Debug.Log($"[LAN] Room lost {Rooms[i].roomId} '{Rooms[i].roomName}' {Rooms[i].address}");
                    Rooms.RemoveAt(i);
                }
        }
        pollTimer -= dt;
        if (pollTimer <= 0f) { pollTimer = 0.1f; Poll(); }
    }

    void Poll()
    {
        for (int n = 0; n < 32 && sock != null; n++)
        {
            int len;
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                if (sock.Available <= 0) return;
                len = sock.ReceiveFrom(buf, ref from);
            }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }
            if (len <= 0) continue;
            PacketsReceived++;
            string msg;
            try { msg = Encoding.UTF8.GetString(buf, 0, len); } catch { continue; }
            var ip = ((IPEndPoint)from).Address.ToString();
            if (msg.StartsWith(Magic + "?|"))
            {
                // 誰かが探している → 部屋を持っていればすぐ答える(その人へ直接)
                if (Advertising) SendTo(Encode(), (IPEndPoint)from);
                continue;
            }
            if (Discovering && msg.StartsWith(Magic + "|")) OnRoomPacket(msg, ip);
        }
    }

    // OMMLAN|v|roomId|mode|players|max|inProgress|port|gameVersion|name
    byte[] Encode()
    {
        var ns = NetSession.Instance;
        int players = Mathf.Max(1, NetSession.ConnectedPlayerCount);
        bool inProgress = NetRunLauncher.IsMultiplayerRun || NetRunLauncher.RunState != NetRunState.None;
        string name = RoomName.Replace("|", "/");
        string s = $"{Magic}|{AdvertisedProtocol}|{RoomId}|{(int)NetRunLauncher.SelectedMode}|{players}|{NetSession.MaxPlayers}|{(inProgress ? 1 : 0)}|{(ns != null ? ns.LastPort : NetSession.DefaultPort)}|{Application.version}|{name}";
        return Encoding.UTF8.GetBytes(s);
    }

    void OnRoomPacket(string msg, string ip)
    {
        var p = msg.Split('|');
        if (p.Length < 10) return;
        if (!int.TryParse(p[1], out int ver)) return;
        string id = p[2];
        if (id == RoomId && Advertising) return; // 自分の部屋
        int.TryParse(p[3], out int mode); int.TryParse(p[4], out int players); int.TryParse(p[5], out int max);
        ushort.TryParse(p[7], out ushort port);
        string name = string.Join("|", p, 9, p.Length - 9);
        var r = Rooms.Find(x => x.roomId == id);
        bool isNew = r == null;
        if (isNew) { r = new LanRoom { roomId = id }; Rooms.Add(r); }
        bool changed = !isNew && (r.players != players || r.inProgress != (p[6] == "1") || (int)r.mode != mode || r.roomName != name);
        r.protocol = ver; r.mode = (MultiplayerGameMode)Mathf.Clamp(mode, 0, 1); r.players = players; r.maxPlayers = Mathf.Max(1, max);
        r.inProgress = p[6] == "1"; r.port = port == 0 ? NetSession.DefaultPort : port; r.gameVersion = p[8]; r.roomName = name;
        r.address = ip; r.lastSeen = Time.unscaledTime;
        if (isNew) Debug.Log($"[LAN] Room found {id} '{name}' {r.mode} {players}/{r.maxPlayers} {ip}:{r.port} v{ver} {r.StateLabel}");
        else if (changed) Debug.Log($"[LAN] Room updated {id} '{name}' {r.mode} {players}/{r.maxPlayers} {r.StateLabel}");
    }

    void Broadcast(byte[] data, string what)
    {
        if (sock == null) return;
        foreach (var ep in BroadcastTargets())
        {
            try { sock.SendTo(data, ep); PacketsSent++; }
            catch (Exception e) { if (Time.unscaledTime - lastSendWarn > 10f) { lastSendWarn = Time.unscaledTime; Debug.LogWarning($"[LAN] send {what} to {ep} failed: {e.Message}"); } }
        }
    }
    float lastSendWarn = -99f;

    void SendTo(byte[] data, IPEndPoint to)
    {
        try { sock.SendTo(data, new IPEndPoint(to.Address, DiscoveryPort)); if (to.Port != DiscoveryPort) sock.SendTo(data, to); PacketsSent++; }
        catch { }
    }

    // 255.255.255.255 + 各ネットワークの「その網の全員」宛て(テザリングの親機は 255.255.255.255 を自分の網へ出さない端末がある)
    List<IPEndPoint> targets;
    float targetsAt = -99f;
    List<IPEndPoint> BroadcastTargets()
    {
        if (targets != null && Time.unscaledTime - targetsAt < 5f) return targets;
        targetsAt = Time.unscaledTime;
        targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, DiscoveryPort) };
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                    byte[] a = ua.Address.GetAddressBytes();
                    byte[] m = ua.IPv4Mask != null ? ua.IPv4Mask.GetAddressBytes() : new byte[] { 255, 255, 255, 0 };
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    var ep = new IPEndPoint(new IPAddress(b), DiscoveryPort);
                    if (!targets.Exists(t => t.Address.Equals(ep.Address))) targets.Add(ep);
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[LAN] interface list failed: " + e.Message); }
        // 同じ PC の 2 プロセス(Windows のテスト)にも届くように
        targets.Add(new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
        return targets;
    }
}

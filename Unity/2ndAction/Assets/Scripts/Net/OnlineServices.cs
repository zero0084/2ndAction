using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Multiplayer;
using Unity.Services.Qos;
using UnityEngine;

// ONLINE PLAY(マルチプレイ Phase 4、2026-10-07)。インターネット越しの部屋(Unity Multiplayer Services の Session + Relay)。
//  ・LOCAL PLAY(LAN の発見 + 直接接続)はこのファイルを一切使わない。UGS の初期化はプレイヤーが ONLINE PLAY を選んだ時だけ行い、
//    失敗しても LOCAL は今までどおり遊べる。
//  ・接続の後(NGO の HOST/Client)は LOCAL と全く同じ: 開始の同期/敵ボス共有/被弾/LastHit/CO-OP/VERSUS は同じコードが動く。
//    違うのは「つなぎ方」だけ: Session を作る/探す/入る → Relay の割り当て → UnityTransport へ Relay の接続先 → NetSession が HOST/Client を開始。
//  ・HOST/Client の開始は自前の INetworkHandler(OnlineNetworkHandler)で NetSession に任せる(版の照合/満員/途中参加の拒否/ログ/切断の処理を LOCAL と共通に)。
//  ・Relay の通信方式: DTLS(暗号化。SDK の既定。Android でも使える)。リージョンは自動(QoS で一番近い所)。
//  ・Session の公開情報(一覧の検索/表示に使う。秘密の接続情報は入れない。Relay の参加コードは SDK が「メンバーだけ」の項目に入れる):
//      名前=部屋の名前 / GameMode(String1, coop|versus) / ProtocolVersion(String2) / GameVersion(String3) / RunState(String4, open|inprogress)
//      / ConnectionType=online / StageId / 最大8人。人数は MaxPlayers - AvailableSlots。
//  ・認証は匿名(Anonymous)。トークンは SDK の正式な保存の仕組みに任せ、こちらでは PlayerPrefs/ファイル/公開の項目に保存しない。
//    将来 Google Play 等の連携へは Authentication の別のサインインに差し替える(このクラスの SignInAsync の中だけ)。
//  ・UGS の環境: 既定 "production"(UGS の既定で必ずある)。開発版は起動引数 -ugsEnv development で切り替えられる(Dashboard に作っておくこと)。
public static class OnlineServices
{
    public enum State { Off, Initializing, Ready, Unavailable }
    public static State Status { get; private set; } = State.Off;
    public static string ErrorText { get; private set; } = "";   // プレイヤー向けの短い理由
    public static string ErrorDetail { get; private set; } = ""; // 開発版の表示/ログ用(秘密は含めない)
    public static string EnvironmentName { get; private set; } = "production";
    public static string PlayerId => Status == State.Ready && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : "";

    public const string KeyMode = "GameMode", KeyProtocol = "ProtocolVersion", KeyGameVersion = "GameVersion", KeyRunState = "RunState", KeyConnection = "ConnectionType", KeyStage = "StageId", KeyRegion = "RelayRegion";
    public const string SessionType = "omm-online"; // 同時に入れる Session は1つ

    public static ISession Current { get; private set; }
    public static bool InSession => Current != null;
    public static bool Busy { get; private set; }
    public static string LastJoinFailure { get; private set; } = "";
    public static void ClearJoinFailure() => LastJoinFailure = "";

    static void Log(string m) => Debug.Log("[ONLINE] " + m);

    // ---------------------------------------------------------------- 初期化 + 匿名サインイン
    public static async Task<bool> EnsureReadyAsync()
    {
        if (Status == State.Ready && UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) return true;
        if (Status == State.Initializing) { while (Status == State.Initializing) await Task.Yield(); return Status == State.Ready; }
        Status = State.Initializing; ErrorText = ""; ErrorDetail = "";
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                EnvironmentName = EnvFromArgs();
                Log($"UGS Initialize (environment {EnvironmentName})");
                var opt = new InitializationOptions().SetEnvironmentName(EnvironmentName);
                await UnityServices.InitializeAsync(opt);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Log($"Authentication succeeded (anonymous)");
            }
            Status = State.Ready;
            return true;
        }
        catch (Exception e) { Fail("init", e); return false; }
    }

    static string EnvFromArgs()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-ugsEnv" && !string.IsNullOrEmpty(a[i + 1])) return a[i + 1];
#endif
        return "production";
    }

    public static void ResetError() { if (Status == State.Unavailable) Status = State.Off; ErrorText = ""; ErrorDetail = ""; }

    static void Fail(string where, Exception e)
    {
        Status = State.Unavailable;
        string all = e.ToString();
        bool notLinked = all.Contains("link your Unity project") || all.Contains("UnityProjectNotLinkedException") || all.Contains("project ID");
        ErrorText = "ONLINE SERVICE UNAVAILABLE";
        ErrorDetail = notLinked ? "project is not linked to Unity Cloud (Dashboard setup needed)" : $"{e.GetType().Name}: {FirstLine(e.Message)}";
        Debug.LogWarning($"[ONLINE] Service error ({where}): {ErrorDetail}");
    }

    static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }

    // ---------------------------------------------------------------- 部屋を作る(HOST)
    public static async Task<bool> CreateAsync(MultiplayerGameMode mode, string roomName)
    {
        if (Busy || InSession || NetSession.IsActive) return false;
        if (!await EnsureReadyAsync()) return false;
        Busy = true;
        try
        {
            // Relay のリージョン: 一番近い所を測って指定する(SDK の自動選択と同じ QoS。画面/ログに出せるよう自分で持つ)。測れなければ SDK の自動選択
            string region = null;
            try
            {
                var qos = await QosService.Instance.GetSortedRelayQosResultsAsync(null);
                if (qos != null && qos.Count > 0) { region = qos[0].Region; Log($"Relay region {region} (QoS {qos[0].AverageLatencyMs}ms, {qos.Count} regions measured)"); }
            }
            catch (Exception qe) { Log($"QoS failed, relay region auto ({qe.GetType().Name})"); }
            RelayRegion = region ?? "auto";
            var opts = new SessionOptions
            {
                Name = string.IsNullOrEmpty(roomName) ? "ONE MORE MILE" : roomName,
                MaxPlayers = NetSession.MaxPlayers,
                IsPrivate = false, // 将来 PRIVATE / FRIENDS(参加コード)を足す時はここ
                IsLocked = false,
                Type = SessionType,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { KeyMode, new SessionProperty(ModeValue(mode), VisibilityPropertyOptions.Public, PropertyIndex.String1) },
                    { KeyProtocol, new SessionProperty(LanDiscovery.AdvertisedProtocol.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String2) },
                    { KeyGameVersion, new SessionProperty(Application.version, VisibilityPropertyOptions.Public, PropertyIndex.String3) },
                    { KeyRunState, new SessionProperty("open", VisibilityPropertyOptions.Public, PropertyIndex.String4) },
                    { KeyConnection, new SessionProperty("online", VisibilityPropertyOptions.Public) },
                    { KeyRegion, new SessionProperty(RelayRegion, VisibilityPropertyOptions.Public) },
                    { KeyStage, new SessionProperty(GameManager.Instance != null ? GameManager.Instance.SelectedStageId ?? "" : "", VisibilityPropertyOptions.Public) },
                },
            }
            .WithRelayNetwork(new RelayNetworkOptions(RelayProtocol.DTLS, region, false)) // 通信方式は DTLS(暗号化)
            .WithNetworkHandler(new OnlineNetworkHandler());
            NetRunLauncher.SelectedMode = mode;
            NetSession.Connection = ConnectionType.Online;
            Current = await MultiplayerService.Instance.CreateSessionAsync(opts);
            Hook(Current);
            Log($"Session created id={Current.Id} name='{Current.Name}' mode={ModeValue(mode)} max={Current.MaxPlayers} protocol={LanDiscovery.AdvertisedProtocol}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ONLINE] Create failed: {e.GetType().Name}: {FirstLine(e.Message)}");
            ErrorText = "CONNECTION FAILED";
            await SafeCleanup();
            return false;
        }
        finally { Busy = false; }
    }

    // ---------------------------------------------------------------- 探す
    public class Room
    {
        public string id, name, mode, gameVersion, runState;
        public int players, maxPlayers, protocol;
        public bool locked;
        public bool VersionOk => protocol == LanDiscovery.MultiplayerProtocolVersion;
        public bool Full => players >= maxPlayers;
        public bool InProgress => runState == "inprogress" || locked;
        public bool Joinable => VersionOk && !Full && !InProgress;
        public string StateLabel => !VersionOk ? "VERSION MISMATCH" : InProgress ? "IN PROGRESS" : Full ? "FULL" : "OPEN";
        public MultiplayerGameMode GameMode => mode == "versus" ? MultiplayerGameMode.Versus : MultiplayerGameMode.Coop;
    }
    public static readonly List<Room> Rooms = new List<Room>();
    public static float LastQueryAt { get; private set; } = -100f;
    public static bool Querying { get; private set; }
    public static int Queries { get; private set; }
    public const float AutoRefreshSeconds = 10f, MinQueryInterval = 2f; // クラウドの API なので LAN のように頻繁に呼ばない(Lobby の Query は 1回/秒 が上限の目安)

    public static async Task QueryAsync(MultiplayerGameMode mode)
    {
        if (Querying || Time.realtimeSinceStartup - LastQueryAt < MinQueryInterval) return;
        if (!await EnsureReadyAsync()) return;
        Querying = true; LastQueryAt = Time.realtimeSinceStartup; Queries++;
        try
        {
            var q = new QuerySessionsOptions
            {
                Count = 30,
                FilterOptions = new List<FilterOption> { new FilterOption(FilterField.StringIndex1, ModeValue(mode), FilterOperation.Equal) }, // 選んだモードだけ
                SortOptions = new List<SortOption> { new SortOption(SortOrder.Descending, SortField.CreationTime) },
            };
            Log($"Session search mode={ModeValue(mode)}");
            var res = await MultiplayerService.Instance.QuerySessionsAsync(q);
            Rooms.Clear();
            foreach (var s in res.Sessions)
            {
                var r = new Room { id = s.Id, name = s.Name, maxPlayers = s.MaxPlayers, players = s.MaxPlayers - s.AvailableSlots, locked = s.IsLocked };
                r.mode = Prop(s.Properties, KeyMode, "coop");
                int.TryParse(Prop(s.Properties, KeyProtocol, "0"), out r.protocol);
                r.gameVersion = Prop(s.Properties, KeyGameVersion, "");
                r.runState = Prop(s.Properties, KeyRunState, "open");
                Rooms.Add(r);
            }
            Log($"Session found {Rooms.Count} (mode {ModeValue(mode)})");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ONLINE] Service error (query): {e.GetType().Name}: {FirstLine(e.Message)}");
            if (e is SessionException se && se.Error == SessionError.RateLimitExceeded) LastQueryAt = Time.realtimeSinceStartup + 5f; // 少し待つ
            else Fail("query", e);
        }
        finally { Querying = false; }
    }

    static string Prop(IReadOnlyDictionary<string, SessionProperty> p, string k, string def) => p != null && p.TryGetValue(k, out var v) && v != null ? v.Value : def;

    // ---------------------------------------------------------------- 入る(1タップ)
    public static async Task<bool> JoinAsync(Room r)
    {
        if (r == null || !r.Joinable || Busy || InSession || NetSession.IsActive) return false;
        if (!await EnsureReadyAsync()) return false;
        Busy = true; LastJoinFailure = "";
        Log($"Join requested id={r.id} '{r.name}' mode={r.mode} players={r.players}/{r.maxPlayers}");
        try
        {
            NetRunLauncher.SelectedMode = r.GameMode;
            NetSession.Connection = ConnectionType.Online;
            var opts = new JoinSessionOptions { Type = SessionType }.WithNetworkHandler(new OnlineNetworkHandler());
            Current = await MultiplayerService.Instance.JoinSessionByIdAsync(r.id, opts);
            Hook(Current);
            RelayRegion = Current.Properties != null && Current.Properties.TryGetValue(KeyRegion, out var rp) && rp != null ? rp.Value : "auto";
            Log($"Join succeeded id={Current.Id} players={Current.PlayerCount}/{Current.MaxPlayers}");
            return true;
        }
        catch (Exception e)
        {
            string why = NetSession.LastJoinFailure;
            LastJoinFailure = !string.IsNullOrEmpty(why) && why != "CONNECTION FAILED" ? why : "CONNECTION FAILED";
            Log($"Join failed ({LastJoinFailure}): {e.GetType().Name}: {FirstLine(e.Message)}");
            await SafeCleanup();
            return false;
        }
        finally { Busy = false; }
    }

    // ---------------------------------------------------------------- 出る / 閉じる
    // HOST: 部屋を消す(一覧に残さない)。JOIN: 抜ける(人数が更新される)
    public static async Task LeaveAsync(string why)
    {
        var s = Current;
        if (s == null) return;
        Current = null;
        try
        {
            if (s.IsHost) { await s.AsHost().DeleteAsync(); Log($"Session closed (deleted by host, {why})"); }
            else { await s.LeaveAsync(); Log($"Session closed (left, {why})"); }
        }
        catch (Exception e) { Debug.LogWarning($"[ONLINE] Session close failed ({why}): {e.GetType().Name}: {FirstLine(e.Message)}"); }
        finally { if (NetSession.IsActive) NetSession.Instance?.ShutdownNetwork("online leave"); NetSession.Connection = ConnectionType.Local; }
    }

    static async Task SafeCleanup()
    {
        var s = Current; Current = null;
        try { if (s != null) { if (s.IsHost) await s.AsHost().DeleteAsync(); else await s.LeaveAsync(); } } catch { }
        if (NetSession.IsActive) NetSession.Instance?.ShutdownNetwork("online failed");
        NetSession.Connection = ConnectionType.Local;
    }

    static void Hook(ISession s)
    {
        s.PlayerJoined += id => Log($"Player joined ({s.PlayerCount}/{s.MaxPlayers})");
        s.PlayerHasLeft += id => Log($"Player left ({s.PlayerCount}/{s.MaxPlayers})");
        s.RemovedFromSession += () => { Log("Removed from session"); if (Current == s) Current = null; };
        s.Deleted += () => { Log("Session deleted"); if (Current == s) Current = null; };
    }

    // ---------------------------------------------------------------- HOST: 状態の更新(開始したら IN PROGRESS + 締め切る)
    static bool reportedInProgress;
    public static async void Tick()
    {
        var s = Current;
        if (s == null || !s.IsHost) { reportedInProgress = false; return; }
        bool running = NetRunLauncher.RunState != NetRunState.None || NetRunLauncher.IsMultiplayerRun; // 承認(NetSession)と同じ条件
        if (running == reportedInProgress) return;
        reportedInProgress = running;
        try
        {
            var h = s.AsHost();
            h.IsLocked = running; // 途中参加はしない
            h.SetProperty(KeyRunState, new SessionProperty(running ? "inprogress" : "open", VisibilityPropertyOptions.Public, PropertyIndex.String4));
            await h.SavePropertiesAsync();
            Log($"RunState -> {(running ? "inprogress (locked)" : "open")}");
        }
        catch (Exception e) { Debug.LogWarning($"[ONLINE] RunState update failed: {e.GetType().Name}: {FirstLine(e.Message)}"); reportedInProgress = !running; }
    }

    // HOST: 通信が切れた JOIN を Session から外す(自分から抜けた人は既に居ないので、その時は何もしない)
    public static async void HostRemovePlayer(string playerId)
    {
        var s = Current;
        if (s == null || !s.IsHost || string.IsNullOrEmpty(playerId)) return;
        bool member = false;
        foreach (var p in s.Players) if (p.Id == playerId) { member = true; break; }
        if (!member) return;
        try { await s.AsHost().RemovePlayerAsync(playerId); Log($"Player removed after disconnect ({s.PlayerCount}/{s.MaxPlayers})"); }
        catch (Exception e) { Debug.LogWarning($"[ONLINE] Remove player failed: {e.GetType().Name}: {FirstLine(e.Message)}"); }
    }

    // 将来の QUICK PLAY 用: 選んだモードで入れる部屋を探して最初の1つへ入る(画面はまだ無い。Matchmaker は使わない)
    public static async Task<bool> QuickJoinAsync(MultiplayerGameMode mode)
    {
        LastQueryAt = -100f;
        await QueryAsync(mode);
        foreach (var r in Rooms) if (r.Joinable && r.GameMode == mode) return await JoinAsync(r);
        return false;
    }

    public static string ModeValue(MultiplayerGameMode m) => m == MultiplayerGameMode.Versus ? "versus" : "coop";

    // 開発版の表示用(Ping/接続先)
    public static string RelayHost { get; set; } = "";
    public static string RelayRegion { get; private set; } = "";
    public static string DebugLine()
    {
        if (NetSession.Connection != ConnectionType.Online) return "connection LOCAL";
        return $"connection ONLINE (Relay DTLS, region {RelayRegion}, relay {RelayHost}) session {(Current != null ? Current.Id : "-")} players {(Current != null ? $"{Current.PlayerCount}/{Current.MaxPlayers}" : "-")} env {EnvironmentName}";
    }
}

// SDK が作った Relay の接続先を受け取り、HOST/Client の開始は NetSession(LOCAL と共通の窓口)に任せる
public class OnlineNetworkHandler : INetworkHandler
{
    public async Task StartAsync(NetworkConfiguration configuration)
    {
        var ns = NetSession.Instance;
        if (ns == null) throw new InvalidOperationException("NetSession missing");
        RelayServerData data = configuration.RelayServerData;
        OnlineServices.RelayHost = data.Endpoint.Address;
        Debug.Log($"[ONLINE] Relay allocation created (role {configuration.Role})");
        if (configuration.Role == NetworkRole.Client)
        {
            if (!ns.StartRelayClient(data)) throw new Exception("client start failed");
            // 接続(承認)まで待つ。断られた/切れた/時間切れは失敗(一覧へ戻す)
            float t0 = Time.realtimeSinceStartup;
            while (NetSession.JoinPending && Time.realtimeSinceStartup - t0 < 20f) await Task.Yield();
            if (!NetSession.IsConnected) throw new Exception("not connected: " + NetSession.LastJoinFailure);
            Debug.Log("[ONLINE] Client connected");
        }
        else
        {
            if (!ns.StartRelayHost(data)) throw new Exception("host start failed");
            Debug.Log("[ONLINE] Host started");
        }
    }

    public Task StopAsync()
    {
        if (NetSession.IsActive) NetSession.Instance?.ShutdownNetwork("session stop");
        return Task.CompletedTask;
    }
}

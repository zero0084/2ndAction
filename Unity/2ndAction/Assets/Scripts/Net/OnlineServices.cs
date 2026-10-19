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
//  ・UGS の環境(2026-10-14 改め): 製品版は必ず "production"、開発版は既定 "development"(production は選べない)。UgsEnvironment が決める。
//    選んだ環境が無い/失敗した時も production へは切り替えない(初期化は1回だけ、この環境のまま「使えない」にする)。
public static class OnlineServices
{
    public enum State { Off, Initializing, Ready, Unavailable }
    public static State Status { get; private set; } = State.Off;
    public static string ErrorText { get; private set; } = "";   // プレイヤー向けの短い理由
    public static string ErrorDetail { get; private set; } = ""; // 開発版の表示/ログ用(秘密は含めない)
    public static string EnvironmentName => UgsEnvironment.Name;
    static bool initializedHere; // UnityServices をこのクラスが(UgsEnvironment の環境で)初期化した
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
    // 2026-10-19: 初期化だけ(サインインしない = 新しい匿名アカウントを作らない)。データの削除の画面で「この端末にアカウントがあるか」を見るのに使う
    public static async Task<bool> InitializeServicesAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return initializedHere;
        if (Status == State.Initializing) { while (Status == State.Initializing) await Task.Yield(); return UnityServices.State == ServicesInitializationState.Initialized && initializedHere; }
        try { return await InitCoreAsync(); }
        catch (Exception e) { Fail("init", e); return false; }
    }

    // この端末に匿名アカウント(サインインの記録)があるか。初期化の後だけ正しい
    public static bool HasStoredAccount => UnityServices.State == ServicesInitializationState.Initialized && initializedHere
        && (AuthenticationService.Instance.IsSignedIn || AuthenticationService.Instance.SessionTokenExists);

    // 2026-10-19: アカウントを削除した後。次にオンラインを使う時に、新しい匿名アカウントでサインインし直す
    public static void ForgetAfterDeletion() { Status = State.Off; ErrorText = ""; ErrorDetail = ""; }

    static async Task<bool> InitCoreAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            string profile = ProfileFromArgs();
            Log($"UGS Initialize ({UgsEnvironment.Describe()}{(profile != null ? ", profile " + profile : "")})");
            var opt = new InitializationOptions().SetEnvironmentName(EnvironmentName);
            if (profile != null) opt.SetProfile(profile); // 開発版: 同じ PC の2つ目のプロセスを別の匿名プレイヤーにする(サインインの記録はプロフィールごと)
            await UnityServices.InitializeAsync(opt);
            initializedHere = true;
            return true;
        }
        if (!initializedHere)
        {
            // 他の所が先に(環境を指定せずに)初期化した = どの環境か確かめられない → 使わない(production に書くおそれがある)
            Status = State.Unavailable; ErrorText = "ONLINE SERVICE UNAVAILABLE";
            ErrorDetail = "Unity Services were initialized elsewhere; the environment cannot be confirmed";
            Debug.LogWarning("[ONLINE] " + ErrorDetail + " (not used)");
            return false;
        }
        return true;
    }

    public static async Task<bool> EnsureReadyAsync()
    {
        if (Status == State.Ready && UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) return true;
        if (Status == State.Initializing) { while (Status == State.Initializing) await Task.Yield(); return Status == State.Ready; }
        Status = State.Initializing; ErrorText = ""; ErrorDetail = "";
        try
        {
            if (!await InitCoreAsync()) return false;
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Log($"Authentication succeeded (anonymous, player {ShortId(AuthenticationService.Instance.PlayerId)})");
            }
            Status = State.Ready;
            return true;
        }
        catch (Exception e) { Fail("init", e); return false; }
    }

    // PlayerId は公開の識別子(秘密ではない)。ログには先頭だけ出す(別のプレイヤーか見分ける用)
    public static string ShortId(string id) => string.IsNullOrEmpty(id) ? "-" : (id.Length > 6 ? id.Substring(0, 6) + "…" : id);

    static string ProfileFromArgs()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-ugsProfile" && !string.IsNullOrEmpty(a[i + 1])) return a[i + 1];
#endif
        return null;
    }

    public static void ResetError() { if (Status == State.Unavailable) Status = State.Off; ErrorText = ""; ErrorDetail = ""; }

    static void Fail(string where, Exception e)
    {
        Status = State.Unavailable;
        string all = e.ToString();
        bool notLinked = all.Contains("link your Unity project") || all.Contains("UnityProjectNotLinkedException") || all.Contains("project ID");
        bool noEnv = all.IndexOf("environment", StringComparison.OrdinalIgnoreCase) >= 0 && (all.Contains("not found") || all.Contains("NotFound") || all.Contains("404") || all.Contains("invalid"));
        ErrorText = "ONLINE SERVICE UNAVAILABLE";
        ErrorDetail = notLinked ? "project is not linked to Unity Cloud (Dashboard setup needed)"
            : noEnv ? $"UGS environment '{EnvironmentName}' is missing (create it in the Dashboard; production is not used instead)"
            : $"{e.GetType().Name}: {FirstLine(e.Message)}";
        Debug.LogWarning($"[ONLINE] Service error ({where}, environment {EnvironmentName}): {ErrorDetail}");
    }

    static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }

    // ---------------------------------------------------------------- 部屋を作る(HOST)
    public static async Task<bool> CreateAsync(MultiplayerGameMode mode, string roomName)
    {
        if (Busy || InSession || NetSession.IsActive) return false;
        if (!await EnsureReadyAsync()) return false;
        Busy = true;
        if (!Recreating) recreatesThisRoom = 0;
        try
        {
            // Relay のリージョン: 一番近い所を測って指定する(SDK の自動選択と同じ QoS。画面/ログに出せるよう自分で持つ)。測れなければ SDK の自動選択
            string region = null;
            // 2026-10-11(本物の Cloud で確認): 重い時は1回目の測定が応答を待ちきれずに空で返ることがあった → もう1回。結果の数も記録する
            for (int attempt = 1; attempt <= 2 && region == null; attempt++)
            {
                try
                {
                    var qos = await QosService.Instance.GetSortedRelayQosResultsAsync(null);
                    int n = qos != null ? qos.Count : 0;
                    if (n > 0) { region = qos[0].Region; Log($"Relay region {region} (QoS {qos[0].AverageLatencyMs}ms, {n} regions measured, attempt {attempt})"); }
                    else Log($"QoS returned no regions (attempt {attempt})");
                }
                catch (Exception qe) { Log($"QoS failed (attempt {attempt}): {qe.GetType().Name}: {FirstLine(qe.Message)}"); }
            }
            if (region == null) Log("Relay region auto (chosen by the SDK)");
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
            .WithRelayNetwork(new RelayNetworkOptions(RelayProto, region, false)) // 通信方式は DTLS(暗号化)。開発版は -relayProto udp で切り分け用
            .WithNetworkHandler(new OnlineNetworkHandler());
            NetRunLauncher.SelectedMode = mode;
            NetSession.Connection = ConnectionType.Online;
            lastCreateMode = mode; lastCreateName = opts.Name;
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

    // ---------------------------------------------------------------- HOST: Relay の接続に失敗した時の作り直し(2026-10-11)
    // 本物の Cloud で、HOST が Relay サーバーへつなぐ最初の合図(BIND)に応答が無く通信が止まることが、ときどきあった
    // (同じ設定で次は成功する)。まだ誰も入っていない待機中なら、同じ名前/モードで部屋を自動で作り直す(最大2回)。
    // 入っている人がいる/出発した後は今までどおり CONNECTION LOST
    static MultiplayerGameMode lastCreateMode;
    static string lastCreateName = "";
    public static int AutoRecreates { get; private set; }
    static int recreatesThisRoom;
    public static bool Recreating { get; private set; }
    public static bool CanAutoRecreate => Current != null && Current.IsHost && recreatesThisRoom < 2 &&
        NetRunLauncher.RunState == NetRunState.None && !NetRunLauncher.IsMultiplayerRun && NetSession.ConnectedPlayerCount <= 1;
    public static async void RecreateAfterRelayFailure()
    {
        if (Recreating) return;
        Recreating = true; recreatesThisRoom++; AutoRecreates++;
        try
        {
            var mode = lastCreateMode; string name = lastCreateName;
            Log($"Relay connection failed while waiting -> recreating the room (try {recreatesThisRoom})");
            await LeaveAsync("relay failure, recreating");
            float t0 = Time.realtimeSinceStartup;
            while ((NetSession.IsActive || (NetSession.Instance != null && NetSession.Instance.ShutdownInProgress)) && Time.realtimeSinceStartup - t0 < 5f) await Task.Yield();
            int keep = recreatesThisRoom;
            bool ok = await CreateAsync(mode, name);
            recreatesThisRoom = keep; // 作り直した部屋も同じ回数で数える(無限に作り直さない)
            Log($"Room recreated: {(ok ? "ok" : "failed")}");
            if (!ok) NetSession.Instance?.NotifyOnlineLost();
        }
        finally { Recreating = false; }
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
    // 2026-10-11(本物の Cloud で確認): Relay への接続が、HOST/JOIN のどちらでも数回に1回失敗することがあった(次は成功する)。
    // 1タップ JOIN が時々失敗しないよう、Relay の接続の失敗(断られた/満員/版違いではない)なら自動でもう1回だけ試す
    public static int JoinAutoRetries { get; private set; }
    public static async Task<bool> JoinAsync(Room r)
    {
        bool ok = await JoinOnceAsync(r);
        if (ok || LastJoinFailure != "CONNECTION FAILED" || r == null) return ok;
        JoinAutoRetries++;
        Log("Join: relay connection failed -> trying once more");
        float w = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w < 1.5f) await Task.Yield();
        return await JoinOnceAsync(r);
    }

    static async Task<bool> JoinOnceAsync(Room r)
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
            OnlineNetworkHandler.ClientFailed = false;
            Current = await MultiplayerService.Instance.JoinSessionByIdAsync(r.id, opts);
            Hook(Current);
            RelayRegion = Current.Properties != null && Current.Properties.TryGetValue(KeyRegion, out var rp) && rp != null ? rp.Value : "auto";
            // 2026-10-11(本物の Cloud で確認): HOST がまだ部屋を作っている途中(Relay の接続先を出す前)だと、SDK は Session への参加だけ先に
            // 返し、通信の接続は後から裏で行う。その接続が失敗すると何も知らされず「部屋に居るのに通信が無い」まま待ち続けていた
            // → 通信が実際につながるまで待つ(最大 30秒)。つながらなければ抜けて CONNECTION FAILED(一覧へ戻す)
            float tw = Time.realtimeSinceStartup;
            while (!NetSession.IsConnected && !OnlineNetworkHandler.ClientFailed && Current != null && Time.realtimeSinceStartup - tw < 30f) await Task.Yield();
            if (!NetSession.IsConnected)
            {
                LastJoinFailure = !string.IsNullOrEmpty(NetSession.LastJoinFailure) && NetSession.LastJoinFailure != "CONNECTION FAILED" ? NetSession.LastJoinFailure : "CONNECTION FAILED";
                Log($"Join: network did not connect in {Time.realtimeSinceStartup - tw:F1}s ({(OnlineNetworkHandler.ClientFailed ? "relay connect failed" : Current == null ? "removed from session" : "timed out")})");
                await SafeCleanup();
                return false;
            }
            Log($"Join succeeded id={Current.Id} players={Current.PlayerCount}/{Current.MaxPlayers} (network connected after {Time.realtimeSinceStartup - tw:F1}s)");
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

    // ---------------------------------------------------------------- アプリを閉じる時(2026-10-11、本物の Cloud で確認)
    // 以前は OnApplicationQuit で Session の削除/退出を始めるだけで、終わる前にアプリが閉じて部屋が一覧に残っていた
    // (HOST の生存確認が止まって約30秒で消えるまで)。閉じる操作を一度止め、後始末が終わるまで(最大 2.5秒)待ってから閉じる
    static bool quitLeaving, quitReady;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookQuit() { Application.wantsToQuit -= WantsToQuit; Application.wantsToQuit += WantsToQuit; }
    static bool WantsToQuit()
    {
        if (quitReady || Current == null) return true;
        if (!quitLeaving) { quitLeaving = true; LeaveThenQuit(); }
        return false;
    }
    static async void LeaveThenQuit()
    {
        float t0 = Time.realtimeSinceStartup;
        var t = LeaveAsync("app quit");
        while (!t.IsCompleted && Time.realtimeSinceStartup - t0 < 2.5f) await Task.Yield();
        Log($"Quit after session cleanup ({(t.IsCompleted ? "done" : "timed out")} in {Time.realtimeSinceStartup - t0:F2}s)");
        quitReady = true;
        Application.Quit();
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
    static RelayProtocol RelayProto
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-relayProto" && a[i + 1] == "udp") return RelayProtocol.UDP;
#endif
            return RelayProtocol.DTLS;
        }
    }

    public static string DebugLine()
    {
        if (NetSession.Connection != ConnectionType.Online) return $"connection LOCAL (UGS env {EnvironmentName})";
        return $"connection ONLINE (Relay {RelayProto}, region {RelayRegion}, relay {RelayHost}) session {(Current != null ? Current.Id : "-")} players {(Current != null ? $"{Current.PlayerCount}/{Current.MaxPlayers}" : "-")} env {EnvironmentName}";
    }
}

// SDK が作った Relay の接続先を受け取り、HOST/Client の開始は NetSession(LOCAL と共通の窓口)に任せる
public class OnlineNetworkHandler : INetworkHandler
{
    public static bool ClientFailed; // 裏で行われた JOIN の通信の接続が失敗した(OnlineServices.JoinAsync が待つ)
    public async Task StartAsync(NetworkConfiguration configuration)
    {
        try { await StartInner(configuration); }
        catch { if (configuration.Role == NetworkRole.Client) ClientFailed = true; throw; }
    }

    async Task StartInner(NetworkConfiguration configuration)
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

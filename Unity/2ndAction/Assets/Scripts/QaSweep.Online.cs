#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Services.Core;
using UnityEngine;

// ONLINE PLAY(2026-10-07)の自動テスト: -qaOnline <dir>
//  A 起動しただけでは UGS を初期化しない(LOCAL/シングルはクラウドを使わない)
//  B ONLINE PLAY → サービスが使えない時(この PC のプロジェクトは Unity Cloud 未連携)は ONLINE SERVICE UNAVAILABLE / RETRY / BACK
//  C 失敗の後も LOCAL PLAY の部屋が作れる(接続の種類は LOCAL)
//  D 部屋の一覧の判定(VERSION MISMATCH / FULL / IN PROGRESS は参加不可)と一覧画面の見た目(仮の部屋で撮る)
//  E シングルのランが普通に動く。ログに秘密(トークン)が出ていない、必要な [ONLINE] ログが出ている
public partial class QaSweep
{
    IEnumerator OnlineModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        var logs = new List<string>();
        Application.LogCallback cb = (m, st, t) => { if (m != null && (m.StartsWith("[ONLINE]") || m.StartsWith("[NET]"))) logs.Add(m); };
        Application.logMessageReceived += cb;
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        var ui = FindFirstObjectByType<NetDebugUI>();
        const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
        void Call(string name, params object[] a) => typeof(NetDebugUI).GetMethod(name, NPI).Invoke(ui, a);

        // ---- A
        L("== A ==");
        Check(UnityServices.State == ServicesInitializationState.Uninitialized && OnlineServices.Status == OnlineServices.State.Off, $"A: UGS is not initialized at boot ({UnityServices.State})");
        NetDebugUI.OpenPanel();
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("online_top");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(UnityServices.State == ServicesInitializationState.Uninitialized, "A: opening the MULTIPLAYER panel does not touch UGS");

        // ---- B
        L("== B ==");
        Call("StartOnline");
        yield return WaitTut(() => NetDebugUI.CurrentScreen == NetDebugUI.MultiScreen.OnlineUnavailable || OnlineServices.Status == OnlineServices.State.Ready, 30f);
        yield return new WaitForSecondsRealtime(0.5f);
        L($"[B] status={OnlineServices.Status} error='{OnlineServices.ErrorText}' detail='{OnlineServices.ErrorDetail}' screen={NetDebugUI.CurrentScreen}");
        if (OnlineServices.Status == OnlineServices.State.Ready)
        {
            L("[B] online service is available on this machine: the failure path is not exercised here");
        }
        else
        {
            Check(NetDebugUI.CurrentScreen == NetDebugUI.MultiScreen.OnlineUnavailable && OnlineServices.ErrorText == "ONLINE SERVICE UNAVAILABLE", "B: service failure -> ONLINE SERVICE UNAVAILABLE screen");
            Shot("online_unavailable");
            // RETRY: もう一度試して、また同じ画面(固まらない/例外を出さない)
            OnlineServices.ResetError();
            Call("StartOnline");
            yield return WaitTut(() => OnlineServices.Status == OnlineServices.State.Unavailable || OnlineServices.Status == OnlineServices.State.Ready, 30f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(NetDebugUI.CurrentScreen == NetDebugUI.MultiScreen.OnlineUnavailable, "B: RETRY tries again and returns to the same screen");
            // 新しい画面の文の訳(英語で撮る。保存はしない)
            string lang0 = Loc.Current;
            Loc.Set("en", save: false);
            string enCheck = Loc.Auto("通信を確認してください"), enRoom = Loc.Auto("オンラインの部屋を探しています…");
            Check(enCheck != "通信を確認してください" && enRoom != "オンラインの部屋を探しています…", $"B: the new ONLINE texts are translated ({enCheck} / {enRoom})");
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("online_unavailable_en");
            yield return new WaitForSecondsRealtime(0.3f);
            Loc.Set(lang0, save: false);
            // BACK
            Call("Go", NetDebugUI.MultiScreen.Top);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(NetDebugUI.CurrentScreen == NetDebugUI.MultiScreen.Top && !NetDebugUI.OnlineFlow, "B: BACK returns to MULTIPLAYER");
        }

        // ---- C
        L("== C ==");
        Call("Go", NetDebugUI.MultiScreen.Mode);
        NetRunLauncher.SelectedMode = MultiplayerGameMode.Coop;
        Call("Go", NetDebugUI.MultiScreen.Choose);
        yield return new WaitForSecondsRealtime(0.3f);
        Call("CreateGame");
        yield return WaitTut(() => NetSession.IsHost, 10f);
        yield return new WaitForSecondsRealtime(0.8f);
        Check(NetSession.IsHost && NetSession.Connection == ConnectionType.Local && LanDiscovery.Advertising, $"C: LOCAL PLAY still creates a LAN room after the online failure (conn {NetSession.Connection})");
        Shot("online_local_room");
        NetSession.Instance.Leave();
        yield return WaitTut(() => !NetSession.IsActive, 10f);
        yield return new WaitForSecondsRealtime(1f);
        Check(!NetSession.IsActive, "C: the LAN room closes");

        // ---- D
        L("== D ==");
        var ok = new OnlineServices.Room { name = "QA OPEN", mode = "coop", players = 1, maxPlayers = 8, protocol = LanDiscovery.MultiplayerProtocolVersion, runState = "open" };
        var ver = new OnlineServices.Room { name = "QA OLD VERSION", mode = "coop", players = 2, maxPlayers = 8, protocol = LanDiscovery.MultiplayerProtocolVersion + 1, runState = "open" };
        var full = new OnlineServices.Room { name = "QA FULL", mode = "coop", players = 8, maxPlayers = 8, protocol = LanDiscovery.MultiplayerProtocolVersion, runState = "open" };
        var prog = new OnlineServices.Room { name = "QA RUNNING", mode = "coop", players = 3, maxPlayers = 8, protocol = LanDiscovery.MultiplayerProtocolVersion, runState = "inprogress", locked = true };
        Check(ok.Joinable && ok.StateLabel == "OPEN", "D: open room is joinable");
        Check(!ver.Joinable && ver.StateLabel == "VERSION MISMATCH", "D: other version -> VERSION MISMATCH, not joinable");
        Check(!full.Joinable && full.StateLabel == "FULL", "D: 8/8 -> FULL, not joinable");
        Check(!prog.Joinable && prog.StateLabel == "IN PROGRESS", "D: running -> IN PROGRESS, not joinable");
        Check(OnlineServices.ModeValue(MultiplayerGameMode.Versus) == "versus" && ok.GameMode == MultiplayerGameMode.Coop, "D: mode values");
        // 一覧の見た目(サービスに問い合わせないよう、状態を Ready・最後の検索を今にして仮の部屋を並べる)
        var st = typeof(OnlineServices);
        st.GetProperty("Status").SetValue(null, OnlineServices.State.Ready);
        st.GetProperty("LastQueryAt").SetValue(null, Time.realtimeSinceStartup + 600f);
        st.GetProperty("Queries").SetValue(null, 1);
        OnlineServices.Rooms.Clear(); OnlineServices.Rooms.AddRange(new[] { ok, ver, full, prog });
        typeof(NetDebugUI).GetField("online", NPI).SetValue(ui, true);
        typeof(NetDebugUI).GetField("screen", NPI).SetValue(ui, NetDebugUI.MultiScreen.OnlineFind);
        yield return new WaitForSecondsRealtime(0.6f);
        Shot("online_find_list");
        yield return new WaitForSecondsRealtime(0.3f); // 撮影はフレームの最後なので、消すのは次のフレーム以降
        OnlineServices.Rooms.Clear();
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("online_find_empty");
        st.GetProperty("Status").SetValue(null, OnlineServices.State.Off);
        st.GetProperty("LastQueryAt").SetValue(null, -100f);
        typeof(NetDebugUI).GetField("online", NPI).SetValue(ui, false);
        typeof(NetDebugUI).GetField("screen", NPI).SetValue(ui, NetDebugUI.MultiScreen.Top);
        NetDebugUI.ClosePanel();
        yield return new WaitForSecondsRealtime(0.4f);

        // ---- E
        L("== E ==");
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(4f);
        Check(gm.HasStarted && !gm.IsGameOver && gm.MaxDistance > 5f, $"E: a single-player run works ({gm.MaxDistance:F0}m)");
        Check(NetSession.Connection == ConnectionType.Local && !NetSession.IsActive, "E: single play uses no network");
        yield return EndRun();
        Application.logMessageReceived -= cb;
        bool secret = false;
        foreach (var m in logs) { string lo = m.ToLowerInvariant(); if (lo.Contains("token") || lo.Contains("joincode") || lo.Contains("join code") || lo.Contains("hmac") || lo.Contains("key=")) { secret = true; L("[E] suspicious log: " + m); } }
        Check(!secret, $"E: no secrets in the [ONLINE]/[NET] logs ({logs.Count} lines)");
        bool sawInit = logs.Exists(m => m.StartsWith("[ONLINE] UGS Initialize"));
        bool sawErr = logs.Exists(m => m.StartsWith("[ONLINE] Service error")) || OnlineServices.Status == OnlineServices.State.Ready;
        Check(sawInit && sawErr, "E: [ONLINE] UGS Initialize / Service error are logged");
        foreach (var m in logs) if (m.StartsWith("[ONLINE]")) L("  log: " + m);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

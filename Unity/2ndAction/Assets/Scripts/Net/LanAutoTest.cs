#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// LAN の部屋の自動発見の確認用(開発ビルドだけ、2026-10-05)。ゲームは始めず、部屋を探して一覧の変化をログに出すだけの「見張り」。
//   -lanWatch <秒>            部屋を探し、Room found / updated / lost を [LANWATCH] で記録して終了
//   -lanForceJoin <秒>        その時点で見えている最初の部屋へ、一覧の可否を無視して JOIN を試す(FULL/IN PROGRESS/版違いの拒否と
//                             CONNECTION FAILED の確認)。-lanForceJoinPort N で違うポートへ(接続できない時の確認)
// 同じ PC で HOST/JOIN の 2 プロセス(NetAutoTest)と一緒に動かす。
public class LanAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Arg("-lanWatch", null) == null && Arg("-lanUiShots", null) == null && Arg("-lanRoomShots", null) == null) return;
        var go = new GameObject("LanAutoTest");
        DontDestroyOnLoad(go);
        go.AddComponent<LanAutoTest>();
    }

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }
    static void L(string s) => Debug.Log($"[LANWATCH] utc={System.DateTime.UtcNow:HH:mm:ss.fff} t={Time.realtimeSinceStartup:F1} {s}");

    float until, forceAt = -1f, started;
    ushort forcePort;
    bool forced, discoveryOn;
    readonly Dictionary<string, string> last = new Dictionary<string, string>();
    int exceptions;

    bool uiShots;
    void Start()
    {
        Application.runInBackground = true;
        string shots = Arg("-lanUiShots", null);
        if (shots != null) { uiShots = true; StartCoroutine(UiShots(shots)); return; }
        string room = Arg("-lanRoomShots", null);
        if (room != null) { uiShots = true; StartCoroutine(RoomShots(room, Arg("-lanRole", "host"), Arg("-lanTag", ""))); return; }
        float.TryParse(Arg("-lanWatch", "30"), out float secs);
        started = Time.realtimeSinceStartup;
        until = started + Mathf.Max(5f, secs);
        if (float.TryParse(Arg("-lanForceJoin", "-1"), out float fa) && fa > 0f) forceAt = started + fa;
        ushort.TryParse(Arg("-lanForceJoinPort", "0"), out forcePort);
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) exceptions++; };
    }

    // -lanUiShots <dir>: 新しいマルチの画面を順に開いてスクリーンショット(見た目の確認用)
    System.Collections.IEnumerator UiShots(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        yield return new WaitForSecondsRealtime(4f);
        var f = typeof(NetDebugUI).GetField("screen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var ui = FindFirstObjectByType<NetDebugUI>();
        NetDebugUI.OpenPanel();
        int i = 0;
        foreach (var sc in new[] { NetDebugUI.MultiScreen.Top, NetDebugUI.MultiScreen.Mode, NetDebugUI.MultiScreen.Choose, NetDebugUI.MultiScreen.Find })
        {
            if (sc == NetDebugUI.MultiScreen.Find) { LanDiscovery.StartDiscovery(); f.SetValue(ui, sc); yield return new WaitForSecondsRealtime(0.6f); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{i++}_find_searching.png")); yield return new WaitForSecondsRealtime(5f); }
            else { f.SetValue(ui, sc); yield return new WaitForSecondsRealtime(0.8f); }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{i++}_{sc}.png"));
            yield return new WaitForSecondsRealtime(0.4f);
        }
        LanDiscovery.StopDiscovery("shots");
        f.SetValue(ui, NetDebugUI.MultiScreen.Choose);
        yield return new WaitForSecondsRealtime(0.3f);
        if (NetSession.Instance.StartHost(NetSession.DefaultPort + 1)) LanDiscovery.StartAdvertising();
        yield return new WaitForSecondsRealtime(1.5f);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{i++}_room_host.png"));
        yield return new WaitForSecondsRealtime(1f);
        NetSession.Instance.Leave();
        yield return new WaitForSecondsRealtime(1f);
        L("UI SHOTS done");
        Application.Quit();
    }

    // 2026-10-29: 部屋を作った後/参加した後/退出した時の画面(同じ PC の2つの起動で HOST と JOIN)。-lanRoomShots <dir> -lanRole host|join [-lanTag 486x1080]
    //  HOST: 部屋を作る → 1人の部屋 → JOIN が入った部屋 → JOIN が出た後の部屋 → 自分が出た後のメニュー
    //  JOIN: 部屋を探す → 見つけた一覧 → 参加した部屋 → 出た後のメニュー
    System.Collections.IEnumerator RoomShots(string dir, string role, string tag)
    {
        System.IO.Directory.CreateDirectory(dir);
        int i = 0;
        System.Collections.IEnumerator Shot(string n)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{i++:00}_{role}{tag}_{n}.png"));
            L($"ROOM SHOT {role} {n} screen={NetDebugUI.CurrentScreen} active={NetSession.IsActive} host={NetSession.IsHost} connected={NetSession.IsConnected} players={(NetSession.Manager != null && NetSession.IsHost ? NetSession.Manager.ConnectedClientsIds.Count : -1)}");
            yield return new WaitForSecondsRealtime(0.4f);
        }
        yield return new WaitForSecondsRealtime(4f);
        var f = typeof(NetDebugUI).GetField("screen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var ui = FindFirstObjectByType<NetDebugUI>();
        NetDebugUI.OpenPanel();
        ushort port = (ushort)(NetSession.DefaultPort + 3);
        if (role == "host")
        {
            f.SetValue(ui, NetDebugUI.MultiScreen.Choose);
            yield return new WaitForSecondsRealtime(0.5f);
            bool ok = NetSession.Instance.StartHost(port);
            if (ok) LanDiscovery.StartAdvertising();
            L($"ROOM host started={ok} port={port}");
            yield return Shot("room_created");
            float w = 0f; while (w < 60f && (NetSession.Manager == null || NetSession.Manager.ConnectedClientsIds.Count < 2)) { yield return null; w += Time.unscaledDeltaTime; }
            L($"ROOM a player joined={(NetSession.Manager != null && NetSession.Manager.ConnectedClientsIds.Count >= 2)} after {w:F1}s");
            yield return Shot("room_with_join");
            w = 0f; while (w < 40f && NetSession.Manager != null && NetSession.Manager.ConnectedClientsIds.Count >= 2) { yield return null; w += Time.unscaledDeltaTime; }
            L($"ROOM the player left after {w:F1}s");
            yield return Shot("room_after_join_left");
            NetSession.Instance.Leave();
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("after_leave");
        }
        else
        {
            LanDiscovery.StartDiscovery(); f.SetValue(ui, NetDebugUI.MultiScreen.Find);
            float w = 0f; while (w < 40f && LanDiscovery.Rooms.Count == 0) { yield return null; w += Time.unscaledDeltaTime; }
            yield return Shot("find_list");
            if (LanDiscovery.Rooms.Count > 0)
            {
                var r = LanDiscovery.Rooms[0];
                L($"ROOM join {r.roomId} {r.address}:{r.port}");
                NetSession.Instance.StartClient(r.address, r.port);
                f.SetValue(ui, NetDebugUI.MultiScreen.Room);
                w = 0f; while (w < 20f && !NetSession.IsConnected) { yield return null; w += Time.unscaledDeltaTime; }
                L($"ROOM connected={NetSession.IsConnected} after {w:F1}s");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("room_joined");
                yield return new WaitForSecondsRealtime(3f);
                NetSession.Instance.Leave();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("after_leave");
            }
            LanDiscovery.StopDiscovery("room shots");
        }
        yield return new WaitForSecondsRealtime(2f);
        L("ROOM SHOTS done");
        Application.Quit();
    }

    void Update()
    {
        if (uiShots) return;
        if (!discoveryOn && Time.realtimeSinceStartup - started > 2f && LanDiscovery.Instance != null)
        {
            discoveryOn = true;
            LanDiscovery.StartDiscovery();
            L("discovery started");
        }
        if (!discoveryOn) return;
        var seen = new HashSet<string>();
        foreach (var r in LanDiscovery.Rooms)
        {
            seen.Add(r.roomId);
            string st = $"{r.roomName} {r.mode} {r.players}/{r.maxPlayers} {r.StateLabel} {r.address}:{r.port} joinable={r.Joinable}";
            if (!last.TryGetValue(r.roomId, out string prev)) L($"FOUND {r.roomId} {st} after {Time.unscaledTime - LanDiscovery.DiscoveryStartedAt:F2}s");
            else if (prev != st) L($"UPDATED {r.roomId} {st}");
            last[r.roomId] = st;
        }
        foreach (var id in new List<string>(last.Keys)) if (!seen.Contains(id)) { L($"LOST {id}"); last.Remove(id); }

        if (!forced && forceAt > 0f && Time.realtimeSinceStartup >= forceAt)
        {
            forced = true;
            if (LanDiscovery.Rooms.Count > 0)
            {
                var r = LanDiscovery.Rooms[0];
                ushort port = forcePort > 0 ? forcePort : r.port;
                L($"FORCE JOIN {r.roomId} {r.StateLabel} -> {r.address}:{port}");
                Debug.Log($"[LAN] Join requested {r.roomId} '{r.roomName}' {r.address}:{port} (forced)");
                NetSession.Instance.StartClient(r.address, port);
            }
            else L("FORCE JOIN: no room visible");
        }
        if (forced && !string.IsNullOrEmpty(NetSession.LastJoinFailure)) { L($"JOIN RESULT failed: {NetSession.LastJoinFailure}"); NetSession.ClearJoinFailure(); }
        if (forced && NetSession.IsConnected && !NetSession.IsHost) { L("JOIN RESULT connected"); forced = false; forceAt = -1f; NetSession.Instance.Leave(); }

        if (Time.realtimeSinceStartup >= until)
        {
            L($"SUMMARY rooms={LanDiscovery.Rooms.Count} sent={LanDiscovery.PacketsSent} received={LanDiscovery.PacketsReceived} multicastLock={LanMulticastLock.Held} exceptions={exceptions} lastError='{LanDiscovery.LastError}'");
            LanDiscovery.StopDiscovery("watch done");
            L($"after stop: multicastLock={LanMulticastLock.Held}");
            Application.Quit();
            until = float.MaxValue;
        }
    }
}
#endif

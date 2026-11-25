#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using UnityEngine;
using UnityEngine.Networking;

// 2026-11-25 セキュリティ監査 B 案: 本物の UGS で、プレイヤーの権限の直接の書き込み/偽の送信が拒否されるかを確かめる(development だけ。-fileSave と -ugsProfile で)。
//  -qaAcProbe <dir>
//   ① Leaderboards へ直接書く(AddPlayerScore)→ 拒否  ② Cloud Save(プレイヤーのデータ)へ直接書く → 拒否
//   ③ omm_submit_run: 受付なし / 存在しない受付 / 受付の直後の大きすぎる距離 → rejected
//   ④ omm_start_run: 1分に7回 → 7回目は rejected
//  トークンはログに出さない
public partial class QaSweep
{
    IEnumerator AcProbeMode()
    {
        bool safe = FileSaveStore.Active && !UgsEnvironment.IsProduction;
        Check(safe, $"runs on a dedicated save file, not on production ({UgsEnvironment.Describe()})");
        if (!safe) yield break;
        var ready = OnlineServices.EnsureReadyAsync(); while (!ready.IsCompleted) yield return null;
        Check(ready.Result, "signed in to UGS (development)");
        if (!ready.Result) yield break;

        // ① Leaderboards へ直接
        var t1 = Try(async () => { await LeaderboardsService.Instance.AddPlayerScoreAsync("omm_best_wasteland_road", 1999999); return "written"; });
        while (!t1.IsCompleted) yield return null;
        L($"[ac] direct leaderboard write -> {t1.Result}");
        Check(t1.Result != "written", $"a direct leaderboard write by the player is denied ({t1.Result})");

        // ② Cloud Save(プレイヤーのデータ)へ直接
        string url = $"https://cloud-save.services.api.unity.com/v1/data/projects/{Application.cloudProjectId}/players/{AuthenticationService.Instance.PlayerId}/items";
        var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes("{\"key\":\"omm_runs\",\"value\":[]}"));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", "Bearer " + AuthenticationService.Instance.AccessToken);
        req.SetRequestHeader("unity-environment", UgsEnvironment.Name);
        yield return req.SendWebRequest();
        L($"[ac] direct cloud save write -> HTTP {req.responseCode}");
        Check(req.responseCode == 403 || req.responseCode == 401, $"a direct Cloud Save write by the player is denied (HTTP {req.responseCode})");
        req.Dispose();

        // ③ 偽の送信
        string now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        Dictionary<string, object> Run(string ticket, int meters, float play) => new Dictionary<string, object> { { "run", new Dictionary<string, object> {
            { "runId", System.Guid.NewGuid().ToString("N") }, { "stageId", "wasteland_road" }, { "characterId", "swordsman" }, { "meters", meters },
            { "playSeconds", play }, { "sprintFrom", 0 }, { "usedAuto", false }, { "usedSprint", false },
            { "startedUtc", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)play - 30 }, { "committedUtc", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            { "appVersion", Application.version }, { "name", "QA AC" }, { "partySize", 1 }, { "allies", "" },
            { "buildKind", UgsEnvironment.BuildKind }, { "env", UgsEnvironment.Name }, { "ticket", ticket }, { "continuation", false } } } };
        foreach (var c in new[] { ("no ticket", "", 50000, 3000f), ("unknown ticket", "0123456789abcdef0123456789abcdef", 50000, 3000f) })
        {
            var t = Call("omm_submit_run", Run(c.Item2, c.Item3, c.Item4)); while (!t.IsCompleted) yield return null;
            L($"[ac] forged submit ({c.Item1}) -> {t.Result}");
            Check(t.Result.Contains("rejected"), $"a forged submission ({c.Item1}) is rejected ({t.Result})");
        }
        var ts = Call("omm_start_run", new Dictionary<string, object> { { "run", new Dictionary<string, object> { { "stageId", "wasteland_road" }, { "characterId", "swordsman" }, { "speedPercent", 100 }, { "appVersion", Application.version }, { "buildKind", UgsEnvironment.BuildKind }, { "env", UgsEnvironment.Name } } } });
        while (!ts.IsCompleted) yield return null;
        string tk = Extract(ts.Result, "ticket");
        Check(tk.Length == 32, "a ticket is issued for a normal start");
        var tf = Call("omm_submit_run", Run(tk, 100000, 3000f)); while (!tf.IsCompleted) yield return null;
        L($"[ac] 100km right after the ticket -> {tf.Result}");
        Check(tf.Result.Contains("rejected"), $"a distance impossible for the server time is rejected ({tf.Result})");
        // ③' 同じ受付の同時の提出(本物の Cloud Save の書き込みの鍵)→ 1つだけ通る
        var tc = Call("omm_start_run", new Dictionary<string, object> { { "run", new Dictionary<string, object> { { "stageId", "sky_corridor" }, { "characterId", "mage" }, { "speedPercent", 100 }, { "appVersion", Application.version }, { "buildKind", UgsEnvironment.BuildKind }, { "env", UgsEnvironment.Name } } } });
        while (!tc.IsCompleted) yield return null;
        string tk2 = Extract(tc.Result, "ticket");
        yield return new WaitForSecondsRealtime(3f);
        Dictionary<string, object> Legit(string ticket, int meters)
        {
            var d = Run(ticket, meters, 2f);
            var run = (Dictionary<string, object>)d["run"]; run["stageId"] = "sky_corridor"; run["characterId"] = "mage";
            return d;
        }
        var c1 = Call("omm_submit_run", Legit(tk2, 300)); var c2 = Call("omm_submit_run", Legit(tk2, 310));
        while (!c1.IsCompleted || !c2.IsCompleted) yield return null;
        int through = (c1.Result.Contains("accepted") || c1.Result.Contains("not_better") ? 1 : 0) + (c2.Result.Contains("accepted") || c2.Result.Contains("not_better") ? 1 : 0);
        L($"[ac] simultaneous submits with one ticket -> '{Flat(c1.Result)}' / '{Flat(c2.Result)}'");
        Check(through == 1, $"two simultaneous submits with the same ticket: only one goes through ({through})");
        var c3 = Call("omm_submit_run", Legit(tk2, 320)); while (!c3.IsCompleted) yield return null;
        Check(c3.Result.Contains("rejected"), $"... and the ticket cannot be used again ({Flat(c3.Result)})");
        // ③'' 端末の時計のずれ(進んでいる / 遅れている)で正当な記録を拒否しない(2026-11-25 第4回)
        foreach (var (label, shift, stage, chr) in new[] { ("fast +1h", 3600L, "natural_cave", "archer"), ("slow -1day", -86400L, "wasteland_road", "fighter") })
        {
            var tq = Call("omm_start_run", new Dictionary<string, object> { { "run", new Dictionary<string, object> { { "stageId", stage }, { "characterId", chr }, { "speedPercent", 100 }, { "appVersion", Application.version }, { "buildKind", UgsEnvironment.BuildKind }, { "env", UgsEnvironment.Name } } } });
            while (!tq.IsCompleted) yield return null;
            string tkq = Extract(tq.Result, "ticket");
            yield return new WaitForSecondsRealtime(3f);
            var d = Run(tkq, 200, 2f); var run = (Dictionary<string, object>)d["run"];
            run["stageId"] = stage; run["characterId"] = chr;
            long nowS = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + shift;
            run["startedUtc"] = nowS - 40; run["committedUtc"] = nowS;
            var tr = Call("omm_submit_run", d); while (!tr.IsCompleted) yield return null;
            L($"[ac] device clock {label} -> {Flat(tr.Result)}");
            Check(tr.Result.Contains("accepted") || tr.Result.Contains("not_better"), $"a fair run with the device clock {label} is accepted ({Flat(tr.Result)})");
        }
        // ④ 開始の連打(上の4回 + 3回 = 7回目で拒否)
        string last = "";
        for (int i = 0; i < 3; i++)
        {
            var tt = Call("omm_start_run", new Dictionary<string, object> { { "run", new Dictionary<string, object> { { "stageId", "natural_cave" }, { "characterId", "ninja" }, { "speedPercent", 100 }, { "appVersion", Application.version }, { "buildKind", UgsEnvironment.BuildKind }, { "env", UgsEnvironment.Name } } } });
            while (!tt.IsCompleted) yield return null;
            last = tt.Result;
        }
        L($"[ac] 7th start within a minute -> {last}");
        Check(last.Contains("rejected"), $"too many starts are rejected ({last})");
    }

    static async Task<string> Try(System.Func<Task<string>> f) { try { return await f(); } catch (System.Exception e) { return "denied: " + e.GetType().Name; } }
    static async Task<string> Call(string script, Dictionary<string, object> args)
    {
        try { return await CloudCodeService.Instance.CallEndpointAsync(script, args) ?? ""; }
        catch (CloudCodeException e) { return "error: " + e.Reason; }
        catch (System.Exception e) { return "error: " + e.GetType().Name; }
    }
    static string Flat(string s) => (s ?? "").Replace("\n", " ").Replace("\r", " ").Replace("  ", " ");
    static string Extract(string json, string key)
    {
        int i = json.IndexOf("\"" + key + "\""); if (i < 0) return "";
        int a = json.IndexOf('"', json.IndexOf(':', i) + 1); int b = json.IndexOf('"', a + 1);
        return a >= 0 && b > a ? json.Substring(a + 1, b - a - 1) : "";
    }
}
#endif

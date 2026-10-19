#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using UnityEngine;

// 本物の UGS(development 環境)でのランキングの確認(2026-10-14)。マスターが development の設定を終えた後に使う。
//  -qaUgsLive <dir> -ugsLiveConfirm development-ready [-ugsProfile <名前>]   ※ production では絶対に動かない(開発版は production を選べない + ここでも確かめる)
//  結果は「アプリ側だけの確認」([app])と「サーバーが実際に返した結果」([server])を分けて書く。
//   1 認証(匿名)と環境
//   2 ソロの記録の送信(Cloud Code omm_submit_run)→ 受け付け / 取得(Leaderboards)に自分が出る
//   3 サーバーの拒否: CPU サポートの記録(partySize 3)/ 仲間あり(allies)/ 速すぎ / 同じ runId
//   4 直接書き込みの拒否(Access Control): プレイヤーが Leaderboards へ直接書く → 拒否される
//   5 別の環境・分ける前の送信待ちは送らない(アプリ)→ サーバーの表にも届いていない(サーバー)
//   6 表示名の更新(Cloud Code omm_set_name)
public partial class QaSweep
{
    [System.Serializable] class LiveReply { public string status = "", message = ""; }

    IEnumerator WaitTask(Task t, float timeout = 30f)
    {
        float w = 0f;
        while (!t.IsCompleted && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator UgsLiveMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        bool safe = (FileSaveStore.Active || MemorySaveStore.Active) && !UgsEnvironment.IsProduction && Arg("-ugsLiveConfirm", "") == "development-ready";
        Check(safe, $"runs only on a dedicated save, not on production, and only with -ugsLiveConfirm development-ready ({UgsEnvironment.Describe()})");
        if (!safe) yield break;
        L($"[live] {UgsEnvironment.Describe()}");

        // 1 認証
        var ti = OnlineServices.EnsureReadyAsync();
        yield return WaitTask(ti, 40f);
        bool ready = ti.IsCompleted && ti.Result;
        L($"[server] init+sign-in: {(ready ? "ok" : "failed")} env={OnlineServices.EnvironmentName} player={OnlineServices.ShortId(OnlineServices.PlayerId)} detail='{OnlineServices.ErrorDetail}'");
        Check(ready && OnlineServices.EnvironmentName == UgsEnvironment.Name && !UgsEnvironment.IsProduction, "1: signed in on the development environment");
        if (!ready) yield break;

        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string Id() => System.Guid.NewGuid().ToString("N");
        int baseM = 1000 + (int)(now % 500); // 走るたびに少し違う距離(前回の記録より長い時だけ新記録になる)
        Leaderboard.Entry E(int m, string env) => new Leaderboard.Entry
        {
            runId = Id(), stageId = "wasteland_road", characterId = "swordsman", meters = m, playSeconds = m / 20f + 30f,
            startedUtc = now - (long)(m / 20f) - 120, committedUtc = now, env = env, appVersion = Application.version,
        };
        var ugs = new UgsLeaderboardBackend();

        // 2 ソロの送信と取得
        var solo = E(baseM, UgsEnvironment.Name);
        var ts = ugs.Submit(solo, "QA Live");
        yield return WaitTask(ts);
        string st2 = ts.IsCompleted ? ts.Result.status + " " + ts.Result.message : "timeout";
        L($"[server] 2 solo submit {solo.meters}m -> {st2}");
        Check(ts.IsCompleted && (ts.Result.status == Leaderboard.SubmitStatus.Accepted || ts.Result.status == Leaderboard.SubmitStatus.NotBetter), "2: the server accepts a valid solo run (or keeps a longer earlier best)");
        var tl = ugs.Load("wasteland_road", 20);
        yield return WaitTask(tl);
        if (tl.IsCompleted && !tl.IsFaulted)
        {
            var p = tl.Result;
            L($"[server] 2 load: total {p.total}, me {(p.me != null ? p.me.meters + "m rank " + p.me.rank : "-")}");
            Check(p.me != null && p.me.meters >= solo.meters, "2: the board shows my record");
        }
        else { L($"[server] 2 load failed: {tl.Exception?.GetBaseException().GetType().Name}"); Check(false, "2: the board can be read"); }

        // 3 サーバーの拒否(端末の窓口はソロしか送らないので、Cloud Code を直接呼ぶ)
        IEnumerator Raw(string what, Dictionary<string, object> args, string expect)
        {
            var tc = CloudCodeService.Instance.CallEndpointAsync(UgsLeaderboardBackend.SubmitScript, args);
            yield return WaitTask(tc);
            string got = tc.IsCompleted && !tc.IsFaulted ? tc.Result : "error " + tc.Exception?.GetBaseException().GetType().Name;
            var r = tc.IsCompleted && !tc.IsFaulted ? JsonUtility.FromJson<LiveReply>(tc.Result ?? "{}") : null;
            L($"[server] 3 {what}: {got}");
            Check(r != null && (r.status == "rejected" || r.status == "duplicate") && r.message == expect, $"3: the server rejects {what} ('{expect}')");
        }
        Dictionary<string, object> Args(Leaderboard.Entry e, int party, string allies) => UgsLeaderboardBackend.SubmitArgs(e, "QA Live", party, allies);
        yield return Raw("a CPU support run (partySize 3)", Args(E(baseM + 900, UgsEnvironment.Name), 3, "archer,mage"), "solo only");
        yield return Raw("a run with allies (partySize 1 + allies)", Args(E(baseM + 900, UgsEnvironment.Name), 1, "ninja"), "solo only");
        var fast = E(baseM + 900, UgsEnvironment.Name); fast.playSeconds = 10f;
        yield return Raw("a too fast run", Args(fast, 1, ""), "too fast");
        yield return Raw("the same runId again", Args(solo, 1, ""), "duplicate");

        // 4 直接書き込み(Access Control で禁止のはず)
        var td = LeaderboardsService.Instance.AddPlayerScoreAsync(Leaderboard.BoardId("wasteland_road"), 1999999);
        yield return WaitTask(td);
        bool denied = td.IsCompleted && td.IsFaulted;
        string why = denied ? td.Exception.GetBaseException().GetType().Name + ": " + FirstLineOf(td.Exception.GetBaseException().Message) : (td.IsCompleted ? "WRITTEN (not denied)" : "timeout");
        L($"[server] 4 direct write by the player: {why}");
        Check(denied, "4: a direct write to the leaderboard by the player is denied");

        // 5 別の環境/分ける前の送信待ち
        var other = E(baseM + 1700, "production"); var legacy = E(baseM + 1800, "");
        string J(params Leaderboard.Entry[] es) { var parts = new List<string>(); foreach (var x in es) parts.Add(JsonUtility.ToJson(x)); return "{\"items\":[" + string.Join(",", parts) + "]}"; }
        SaveStore.SetString(Leaderboard.PendingKey, J(other, legacy));
        SaveStore.SetInt(Leaderboard.JoinedKey, 1);
        Leaderboard.Backend = ugs;
        float wf = 0f; while (Leaderboard.Sending && wf < 10f) { yield return null; wf += Time.unscaledDeltaTime; }
        var tf = Leaderboard.FlushAsync("live");
        yield return WaitTask(tf);
        wf = 0f; while (Leaderboard.Sending && wf < 20f) { yield return null; wf += Time.unscaledDeltaTime; }
        L($"[app] 5 after flush: pending here {Leaderboard.PendingCount}, held {Leaderboard.HeldCount}");
        Check(Leaderboard.HeldCount == 2, "5 [app]: the other-environment and pre-separation entries are held, not sent");
        tl = ugs.Load("wasteland_road", 50);
        yield return WaitTask(tl);
        if (tl.IsCompleted && !tl.IsFaulted)
        {
            var me = tl.Result.me;
            L($"[server] 5 board after flush: me {(me != null ? me.meters + "m" : "-")}");
            Check(me == null || (me.meters != other.meters && me.meters != legacy.meters), "5 [server]: those distances did not reach the board");
        }

        // 6 表示名
        var tn = ugs.UpdateName("QA Live 2");
        yield return WaitTask(tn);
        L($"[server] 6 rename -> {(tn.IsCompleted ? tn.Result.ToString() : "timeout")}");
        Check(tn.IsCompleted && tn.Result, "6: the display name update works");

        Leaderboard.DevReset();
        yield break;
    }

    static string FirstLineOf(string s) { if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }
}
#endif

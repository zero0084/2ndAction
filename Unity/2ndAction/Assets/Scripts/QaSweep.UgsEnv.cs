#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// 開発版/製品版の UGS の環境の分離(2026-10-14)。専用の保存(-fileSave / -memSave)と模擬サーバーで行う(本物のサービスへは書かない)。
//  -qaUgsEnv <dir> [-ugsEnv production|staging|...] [-ugsEnvExpect <名前>] [-ugsEnvInit 1]
//   ・環境の決まり方(開発版は production を選べない)
//   ・送信待ち: 今の環境で作った物だけを送る / 別の環境・分ける前の物は残して送らない / 参加した時の自己ベストの投稿も今の環境の物だけ
//   ・自己ベストは環境ごと(開発用の環境の長い記録が別の環境の送信を止めない)
//   ・通信失敗の後の再送でも、別の環境の物は送らない
//   ・本物の送信の窓口(UgsLeaderboardBackend.Submit)も、別の環境の物は通信する前に止める
//   ・-ugsEnvInit 1: 本物の UGS の初期化だけを試す(読み書きはしない)。選んだ環境が無い時に production へ切り替わらないこと
public partial class QaSweep
{
    // 送信(ホームの自動の再送が動いている時は、その終わりを待ってから。終わるまで待つ)
    IEnumerator EnvFlush(string why)
    {
        float w = 0f;
        while (Leaderboard.Sending && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        Task t = Leaderboard.FlushAsync(why);
        w = 0f;
        while ((!t.IsCompleted || Leaderboard.Sending) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator UgsEnvMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;

        // ① 環境の決まり方
        string expect = Arg("-ugsEnvExpect", UgsEnvironment.Development);
        L($"[env] {UgsEnvironment.Describe()}");
        Check(!UgsEnvironment.IsReleaseBuild, "this is a dev build");
        Check(UgsEnvironment.Name == expect, $"the environment is '{expect}' ({UgsEnvironment.Name})");
        Check(!UgsEnvironment.IsProduction, "a dev build never uses production");
        Check(OnlineServices.EnvironmentName == UgsEnvironment.Name, "the online services use the same environment");
        if (Arg("-ugsEnv", "") == "production") Check(UgsEnvironment.Ignored.Contains("production"), $"'-ugsEnv production' is ignored and reported ({UgsEnvironment.Ignored})");
        Check(UgsEnvironment.MaySubmit(UgsEnvironment.Name, out _), "entries made in this environment may be sent");
        Check(!UgsEnvironment.MaySubmit("production", out string wp), $"entries made for production are not sent from a dev build ({wp})");
        Check(!UgsEnvironment.MaySubmit("", out string wl), $"entries made before the separation are not sent ({wl})");

        // ② 送信待ち
        string mock = System.IO.Path.Combine(outDir, "lb_mock");
        var me = new MockBackend(mock, "player-me");
        Leaderboard.Backend = me;
        Leaderboard.DevReset(); Leaderboard.Backend = me;
        SaveStore.SetInt(Leaderboard.JoinedKey, 1);
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string Id() => System.Guid.NewGuid().ToString("N");
        Leaderboard.Entry E(string env, string stage, int m) => new Leaderboard.Entry
        {
            runId = Id(), stageId = stage, characterId = "swordsman", meters = m, playSeconds = m / 25f + 10f, startedUtc = now - (long)(m / 25f) - 60, committedUtc = now, env = env,
        };
        string J(params Leaderboard.Entry[] es) => "{\"items\":[" + string.Join(",", es.Select(x => JsonUtility.ToJson(x))) + "]}";
        var legacy = E("", "wasteland_road", 4000);        // 分ける前の版(環境なし)
        var prod = E("production", "natural_cave", 5000);  // 製品版が production で作った物(この端末に移ってきた等)
        var other = E(UgsEnvironment.Name == "staging" ? "qa-other" : "staging", "sky_corridor", 3000); // 別の開発用の環境
        var here = E(UgsEnvironment.Name, "wasteland_road", 1500);
        SaveStore.SetString(Leaderboard.PendingKey, J(legacy, prod, other, here));
        SaveStore.SetString(Leaderboard.VerifiedKey, J(legacy, prod, other));
        Check(Leaderboard.PendingCount == 1 && Leaderboard.HeldCount == 3, $"1 pending for this environment, 3 held ({Leaderboard.PendingCount}/{Leaderboard.HeldCount})");
        Leaderboard.LogEnvironmentAtBoot();

        // 通信失敗 → 残る(別の環境の物も消えない)
        MockBackend.SimulateOffline = true;
        float w = 0f;
        yield return EnvFlush("qa offline");
        Check(Leaderboard.PendingCount == 1 && Leaderboard.HeldCount == 3 && Leaderboard.LastSendError != "", "offline: the post is kept for retry, the held ones are untouched");
        MockBackend.SimulateOffline = false;
        // 再送(自動の再送と同じ FlushAsync)
        yield return EnvFlush("retry"); // ホームの自動の再送(30秒ごと)が先に動いていれば、その終わりを待つ
        string db = System.IO.File.Exists(System.IO.Path.Combine(mock, "leaderboard.json")) ? System.IO.File.ReadAllText(System.IO.Path.Combine(mock, "leaderboard.json")) : "";
        Check(db.Contains(here.runId), "retry: the post made in this environment is sent");
        Check(!db.Contains(legacy.runId) && !db.Contains(prod.runId) && !db.Contains(other.runId), "retry: nothing from another environment / before the separation is sent");
        Check(Leaderboard.PendingCount == 0 && Leaderboard.HeldCount == 3, $"after sending: 0 pending, the 3 held entries are kept as they were ({Leaderboard.PendingCount}/{Leaderboard.HeldCount})");
        string pend = SaveStore.GetString(Leaderboard.PendingKey, "");
        Check(pend.Contains(legacy.runId) && pend.Contains(prod.runId) && pend.Contains(other.runId), "the held entries are not deleted or changed");
        Check(pend.Contains("\"env\":\"production\"") && pend.Contains("\"env\":\"" + other.env + "\""), "the held entries keep their own environment (not re-tagged)");

        // ③ 参加し直し: 自己ベストの投稿も今の環境の物だけ
        Leaderboard.Leave(); Leaderboard.Join();
        w = 0f; while (Leaderboard.Sending && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Leaderboard.PendingCount == 0, "joining again queues no verified best of another environment");

        // ④ 自己ベストは環境ごと: 別の環境の 4000m があっても、この環境の 2000m は新記録として送る
        var run = new RunLedger.Run { runId = Id(), stageId = "wasteland_road", characterId = "swordsman", maxReached = 2000, playSeconds = 2000 / 25f + 10f, startedUtc = now - 200, officialStage = true };
        Leaderboard.OnSuccessCommitted(run);
        Check(Leaderboard.VerifiedBest("wasteland_road")?.meters == 2000 && Leaderboard.VerifiedBest("wasteland_road").env == UgsEnvironment.Name, "the verified best is per environment (the legacy 4000m does not block it)");
        Check(Leaderboard.PendingCount == 1, "the new best is queued for this environment");
        yield return EnvFlush("commit");
        Check(Leaderboard.PendingCount == 0, "and sent");

        // ⑤ 本物の送信の窓口: 別の環境の物は通信する前に止める(失敗扱い = 残る)
        var ugs = new UgsLeaderboardBackend();
        var ts = ugs.Submit(E("production", "wasteland_road", 100), "QA");
        w = 0f; while (!ts.IsCompleted && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(ts.IsCompleted && ts.Result.status == Leaderboard.SubmitStatus.Failed && ts.Result.message.StartsWith("blocked"), $"UGS backend: a production entry is blocked before any network call ({(ts.IsCompleted ? ts.Result.message : "not completed")})");
        Check(OnlineServices.Status == OnlineServices.State.Off, "the blocked submit did not even initialize the online services");

        Leaderboard.DevReset();

        // ⑥ 本物の初期化(読み書きはしない): 環境が無い/失敗しても production にならない
        if (Arg("-ugsEnvInit", "0") == "1")
        {
            var ti = OnlineServices.EnsureReadyAsync();
            w = 0f; while (!ti.IsCompleted && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[env] real init: completed={ti.IsCompleted} ok={(ti.IsCompleted && ti.Result)} status={OnlineServices.Status} env={OnlineServices.EnvironmentName} detail='{OnlineServices.ErrorDetail}'");
            Check(OnlineServices.EnvironmentName == expect, $"after the real init the environment is still '{expect}' (no fallback)");
            if (ti.IsCompleted && !ti.Result)
            {
                // 失敗した後にもう一度試しても同じ環境(production へ切り替えない)
                var ti2 = OnlineServices.EnsureReadyAsync();
                w = 0f; while (!ti2.IsCompleted && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
                L($"[env] retry init: ok={(ti2.IsCompleted && ti2.Result)} status={OnlineServices.Status} env={OnlineServices.EnvironmentName} detail='{OnlineServices.ErrorDetail}'");
                Check(OnlineServices.EnvironmentName == expect, "a retry after the failure still uses the same environment");
            }
        }
        yield break;
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// ランキングのラン開始の受付(2026-11-25 セキュリティ監査 B 案)。
//  ・ランキングに参加している人のソロのランの開始の時に、サーバー(Cloud Code omm_start_run)から受付番号(ticket)を受け取り、
//    ランの台帳(RunLedger.Run.ticket)に入れる。台帳は中断データにも入るので、CONTINUE でもそのまま使う。
//  ・オフライン/失敗の時はランを止めない(通常どおり遊べる)。開始の直後(1000m まで・6回まで)だけ取り直す。
//    取れなかったランはランキングに載らないだけ(ローカルの BEST・MILE・解放・報酬は今まで通り)。
//  ・受付の無い記録は送らない(Leaderboard.Enqueue)。サーバーも受付の無い記録を拒否する(omm_submit_run)。
public static class RankingTicket
{
    public const string StartScript = "omm_start_run";
    const int MaxAttempts = 6;
    const float RetrySeconds = 20f;
    const double MaxMetersForTicket = 1000.0; // サーバーの検査(PRE_TICKET_METERS)と同じ

    static string inFlightRun;
    static string attemptsRun; static int attempts; static float nextTry;
    public static int Issued { get; private set; }  // 確認用
    public static string LastResult { get; private set; } = "";

    // 受付が要るランか(ランキングに参加・ソロ・ランキングのマップ・ローカルで分かる資格)
    static bool Wanted(RunLedger.Run r)
    {
        if (r == null || !string.IsNullOrEmpty(r.ticket) || !Leaderboard.Joined || r.multiplayer || r.Support || r.legacy || r.debugUsed) return false;
        if (Array.IndexOf(Leaderboard.Stages, r.stageId) < 0 || DebugRun.WritesBlocked) return false;
        if (r.testProfile && UgsEnvironment.IsProduction) return false;
        return r.maxReached < MaxMetersForTicket;
    }

    // RunLedger.BeginNew の直後と、ラン中(RunLedger.Tick)から
    public static void Tick()
    {
        var r = RunLedger.Current;
        if (!Wanted(r) || inFlightRun != null) return;
        if (attemptsRun != r.runId) { attemptsRun = r.runId; attempts = 0; nextTry = 0f; }
        if (attempts >= MaxAttempts || Time.realtimeSinceStartup < nextTry) return;
        attempts++;
        nextTry = Time.realtimeSinceStartup + RetrySeconds;
        _ = RequestAsync(r);
    }

    static async Task RequestAsync(RunLedger.Run r)
    {
        inFlightRun = r.runId;
        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-rtOffline") >= 0) { LastResult = "offline (simulated)"; Debug.Log("[Ranking] run ticket: offline (simulated)"); return; } // 試験: ラン開始の時にオフライン
#endif
            var backend = Leaderboard.Backend as UgsLeaderboardBackend;
            if (backend == null) { LastResult = "no backend"; return; }
            if (!UgsEnvironment.MaySubmit(UgsEnvironment.Name, out string block)) { LastResult = "blocked: " + block; return; }
            string ticket = await backend.StartRun(r);
            if (string.IsNullOrEmpty(ticket)) return;
            // 返事を待つ間に別のランになっていたら使わない
            if (RunLedger.Current == r && string.IsNullOrEmpty(r.ticket))
            {
                r.ticket = ticket; Issued++; LastResult = "ok";
                Debug.Log($"[Ranking] run ticket received for {r.runId.Substring(0, 8)} (attempt {attempts})");
            }
        }
        catch (Exception ex) { LastResult = ex.GetType().Name; Debug.LogWarning("[Ranking] run ticket failed: " + ex.GetType().Name); }
        finally { inFlightRun = null; }
    }
}

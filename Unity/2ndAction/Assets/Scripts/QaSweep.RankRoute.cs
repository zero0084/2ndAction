#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// ランキングの実際の経路(2026-10-27、APK 386 の実機で「脱出しても記録がありません」): -qaRankRoute <dir> -rrPhase run|reopen
//  本物の UGS(development)。専用の保存ファイル(-fileSave)と別の匿名プレイヤー(-ugsProfile)で動かすこと。
//   run    : ランキングに参加(画面の「参加する」と同じ処理)→ ホームの START → 普通に走る(ワープ/無敵なし)→ 長押しの脱出
//            → RESULT → 「ホームへ」(タップ)→ ホームでランキングの画面を開く → 自分の記録が出るか
//   reopen : 起動し直して、ランキングの画面だけ開く → 自分の記録(My Best / My Rank)が出るか
//   cont   : 走る → 途中で中断(ホームへ戻る)→ CONTINUE → 走る → 脱出 → … (-rrPauseAt 既定 180m。-rrRestart 1 なら中断で終わり、次の起動で cont2)
//   cont2  : 起動し直して CONTINUE → 走る → 脱出 → …
//  脱出は最初のボスの撃破で使えるようになる。試験では距離 -rrMeters(既定 350m)まで普通に走った後に脱出を使える状態にする
//  (記録の対象外の印は付かない。ボス戦の分の時間を省くだけ)
public partial class QaSweep
{
    IEnumerator RankRouteMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(2f);
        string phase = Arg("-rrPhase", "run");
        bool safe = FileSaveStore.Active && !UgsEnvironment.IsProduction;
        Check(safe, $"runs on a dedicated save file, not on production ({UgsEnvironment.Describe()})");
        if (!safe) yield break;
        L($"[rr] phase {phase}: {UgsEnvironment.Describe()} joined={Leaderboard.Joined} pending={Leaderboard.PendingCount} codename='{Codename.Current}'");

        if (phase == "run" || phase == "cont" || phase == "cont2")
        {
            if (!Leaderboard.Joined) { Codename.Set("QA Route"); Leaderboard.Join(); }
            float wj = 0f; while (Leaderboard.Sending && wj < 30f) { yield return null; wj += Time.unscaledDeltaTime; }
            StartCoroutine(AutoPickCards());
            float target = float.Parse(Arg("-rrMeters", "350"), System.Globalization.CultureInfo.InvariantCulture);
            float w = 0f;
            if (phase != "cont2")
            {
                yield return BeginRun("swordsman", "wasteland_road");
                if (phase == "cont")
                {
                    // 2026-10-27: 途中で中断(一時停止 → ホームへ戻る = 中断の保存)→ CONTINUE。-rrRestart 1 ならここで終わり、次の起動の cont2 で続ける
                    float pauseAt = float.Parse(Arg("-rrPauseAt", "180"), System.Globalization.CultureInfo.InvariantCulture);
                    while (gm.MaxDistance < pauseAt && !gm.IsGameOver && w < 240f) { yield return null; w += Time.unscaledDeltaTime; }
                    var r0 = RunLedger.Current;
                    L($"[rr] interrupting at {gm.MaxDistance:F0}m (run {(r0 != null ? r0.runId.Substring(0, 8) : "-")})");
                    UdId("RR_cont_run", r0 != null ? r0.runId : "");
                    stopKeepAlive = true;
                    yield return ResumeGoHome(); // 一時停止の画面の「ホームへ戻る」と同じ(ReturnToHome = 中断の保存)
                    Check(gm != null && !gm.HasStarted && RunCheckpoint.HasActiveRun, "cont: back at home with the interrupted run saved (CONTINUE offered)");
                    if (Arg("-rrRestart", "0") == "1") { L("[rr] interrupted; ending this start (the next start continues)"); yield break; }
                }
            }
            if (phase == "cont" || phase == "cont2")
            {
                Check(RunCheckpoint.HasActiveRun, "cont: an interrupted run is waiting (CONTINUE)");
                yield return ResumeContinue("rank");
                gm = GameManager.Instance; pc = PlayerController.Instance;
                stopKeepAlive = false; StartCoroutine(KeepAlive());
                var r1 = RunLedger.Current;
                L($"[rr] continued at {gm.MaxDistance:F0}m: ledger run={(r1 != null ? r1.runId.Substring(0, 8) : "-")} legacy={r1?.legacy} debug={r1?.debugUsed}");
                Check(r1 != null && !r1.legacy && !r1.debugUsed, "cont: the continued run keeps its ledger (official run, not 'legacy')");
                string before = UdRead(Arg("-udShared", outDir), "RR_cont_run");
                if (!string.IsNullOrEmpty(before)) Check(r1 != null && r1.runId == before, "cont: same run id as before the interruption");
                w = 0f;
            }
            while (gm.MaxDistance < target && !gm.IsGameOver && w < 240f) { yield return null; w += Time.unscaledDeltaTime; }
            var run = RunLedger.Current;
            L($"[rr] ran {gm.MaxDistance:F0}m in {w:F0}s; ledger run={(run != null ? run.runId.Substring(0, 8) : "-")} debug={run?.debugUsed} '{run?.debugWhat}' official={run?.officialStage} test={run?.testProfile} multi={run?.multiplayer} support={run?.Support}");
            Check(!gm.IsGameOver, "run: the run is still going (not a game over)");
            if (gm.IsGameOver) yield break;
            SetPrivate(gm, "escapeUnlocked", true);
            PlayerController.QaEscapeHold = true; // 長押しの脱出(本物の入力の経路: TryConfirmEscape("hold") → 着地 → FinishRun)
            w = 0f; while (!gm.IsGameOver && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            PlayerController.QaEscapeHold = false;
            stopKeepAlive = true;
            var c = RunLedger.LastResult;
            L($"[rr] finished: win={gm.IsWin} distance={gm.MaxDistanceExact:F1} committed={c?.committed} ranking={c?.rankingEligible} skipped='{c?.skippedWhy}' joined={Leaderboard.Joined} pending={Leaderboard.PendingCount}");
            Check(gm.IsGameOver && gm.IsWin, "run: the escape finished the run as a success");
            Check(c != null && c.committed && c.rankingEligible, $"run: the success was committed and is eligible for the ranking ({c?.skippedWhy})");
            yield return new WaitForSecondsRealtime(4f);
            Shot("rr_result");
            yield return null; yield return null;
            // RESULT の「ホームへ」(タップと同じ)
            var old = gm;
            float wt = 0f;
            while ((GameManager.Instance == null || GameManager.Instance == old) && wt < 30f)
            {
                if (((int)(wt * 2f)) % 2 == 0) GameManager.DebugResultTapPending = true;
                yield return new WaitForSecondsRealtime(0.5f); wt += 0.5f;
            }
            gm = GameManager.Instance;
            Check(gm != null && gm != old && !gm.HasStarted, "run: back at home from the RESULT");
            // -rrHomeWait 0: ホームに着いた瞬間に画面を開く(送信の途中で開いた時に、送信の後で読み直すか)
            float hw = float.Parse(Arg("-rrHomeWait", "2"), System.Globalization.CultureInfo.InvariantCulture);
            if (hw > 0f) yield return new WaitForSecondsRealtime(hw); else yield return null;
            L($"[rr] at home: joined={Leaderboard.Joined} pending={Leaderboard.PendingCount} sending={Leaderboard.Sending} lastError='{Leaderboard.LastSendError}'");
        }

        // ランキングの画面(ホームのボタンと同じ)
        RankingPanel.OpenStatic();
        var cacheF = typeof(RankingPanel).GetField("cache", BindingFlags.NonPublic | BindingFlags.Instance);
        Leaderboard.Page page = null;
        float wp = 0f;
        while (wp < 6f) { yield return null; wp += Time.unscaledDeltaTime; }
        page = PageOf(cacheF, "wasteland_road");
        L($"[rr] panel right after opening: {(page == null ? "not loaded" : $"total {page.total}, me {(page.me == null ? "none" : $"#{page.me.rank} {page.me.meters}m '{page.me.name}'")}")}; pending={Leaderboard.PendingCount} sending={Leaderboard.Sending} lastError='{Leaderboard.LastSendError}'");
        Shot($"rr_{phase}_panel");
        yield return null; yield return null;
        // 少し待つ(送信が後から終わる場合に、画面が読み直すか)
        while (wp < 30f) { yield return null; wp += Time.unscaledDeltaTime; }
        page = PageOf(cacheF, "wasteland_road");
        string me = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : "";
        L($"[rr] panel after 30s: {(page == null ? "not loaded" : $"total {page.total}, me {(page.me == null ? "none" : $"#{page.me.rank} {page.me.meters}m '{page.me.name}'")}")}; player {OnlineServices.ShortId(me)} env={OnlineServices.EnvironmentName} pending={Leaderboard.PendingCount}");
        Shot($"rr_{phase}_panel30");
        yield return null; yield return null;
        Check(page != null && page.me != null && page.me.meters > 0, $"{phase}: the ranking screen shows my record (My Best / My Rank)");
        if (phase == "run" && page?.me != null) Check(Mathf.Abs(page.me.meters - (float)RunLedger.LastResult.best) < 2f || page.me.meters >= 1, "run: the shown distance is the escaped distance");
        UdId(phase == "run" ? "RR_send" : "RR_reopen", me);
    }

    static Leaderboard.Page PageOf(FieldInfo f, string stage)
    {
        var d = f?.GetValue(RankingPanel.Instance) as System.Collections.Generic.Dictionary<string, Leaderboard.Page>;
        return d != null && d.TryGetValue(stage, out var p) ? p : null;
    }
}
#endif

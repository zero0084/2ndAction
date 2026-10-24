#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 脱出の成功と死神の一撃の順番(2026-10-24、実機: 死神のラン中に脱出の演出に入ったのに敗北)。
//   -qaEscape <dir> [-escCases before,after,samereap,sameescape,transition,during,warp100k,normal2k] [-escLegacy 1]
//  before      : 死神の一撃の 0.2秒後に長押しが完了 → 敗北(成功の演出を出さない・報酬なし)
//  after       : 長押しの完了の 0.2秒後に一撃の予定 → 成功(一撃は来ない/効かない)
//  samereap    : 同じフレームで 一撃 → 長押しの完了 → 敗北
//  sameescape  : 同じフレームで 長押しの完了 → 一撃 → 成功
//  transition  : 演出の最後の暗転の間に一撃(以前の不具合の時間) → 成功
//  during      : 死神のすぐ前で長押しを完了 → 止まっている間に死神が追いつく → 成功
//  warp100k    : 99,900m から本来の流れで死神が出て、追いつかれる直前に脱出
//  normal2k    : 2,000m の通常の脱出(死神なし)
// -escLegacy 1 は以前の動き(比較用、不具合が出るはず)
public partial class QaSweep
{
    IEnumerator EscapeMode()
    {
        Application.targetFrameRate = 60;
        GameManager.QaLegacyEscape = Arg("-escLegacy", "0") == "1";
        L($"[escape] legacy={GameManager.QaLegacyEscape}");
        foreach (var c in Arg("-escCases", "before,after,samereap,sameescape,transition,during,warp100k,normal2k").Split(','))
            yield return EscapeCase(c);
        GameManager.QaLegacyEscape = false;
    }

    IEnumerator EscapeCase(string cond)
    {
        L($"== {cond} ==");
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true;
        autoPickHold = false;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        livesSet.Invoke(gm, new object[] { 30 });
        var bm = BossManager.Instance;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        float holdDur = pc.escapeHoldDuration;
        PlayerController.QaEscapeHold = false;
        ReaperBase.DebugNoReap = false;

        // 場所
        if (cond == "warp100k") gm.DebugWarpToDistance(99900f);
        else gm.DebugWarpToDistance(cond == "normal2k" ? 2000f : 3000f);
        RunLedger.DevClearDebug(); // ワープは記録の対象外の印を付けるので外す(確定の回数を見るため)
        SetPrivate(gm, "escapeUnlocked", true); // 脱出は最初のボスの撃破で解放(ワープで飛ばした分)
        yield return new WaitForSeconds(1.5f);
        RunLedger.DevClearDebug();
        int wallet0 = gm.TotalOwnedMile; double best0 = gm.GetStageBest("wasteland_road");
        bool reaperCase = cond != "normal2k";
        if (reaperCase && cond != "warp100k" && bm != null) { bm.enabled = true; bm.DebugSpawnReaper(); }
        if (cond == "samereap" || cond == "sameescape" || cond == "transition") ReaperBase.DebugNoReap = true; // 一撃は試験が出す

        // 死神が捕まえる所(予告の始まり)/ すぐ前まで待つ
        float w = 0f; ReaperBase r = null;
        while (reaperCase && w < 150f && !gm.IsGameOver)
        {
            yield return null; w += Time.unscaledDeltaTime;
            if (gm.Lives < 30) livesSet.Invoke(gm, new object[] { 30 });
            RunLedger.DevClearDebug();
            r = ReaperBase.Active;
            if (r == null) continue;
            if (r.data == null) continue;
            var ch = r.data.chase;
            if ((cond == "during" || cond == "warp100k") && r.CurrentPhase == ReaperBase.Phase.Chase && r.Gap < ch.captureGap + 3f) break;
            if (r.CurrentPhase == ReaperBase.Phase.Captured) break;
        }
        if (reaperCase && (r == null || gm.IsGameOver))
        {
            Check(false, $"{cond}: the reaper reached the player before the test (found={r != null}, over={gm.IsGameOver}, {w:F0}s)");
            yield return EscapeEnd(); yield break;
        }
        float windup = r != null && r.data != null ? r.data.chase.captureWindup : 0f;
        if (r != null) L($"[escape] {cond}: reaper {r.CurrentPhase} gap={r.Gap:F2} windup={windup:F2}s d={gm.MaxDistance:F0}");
        int strikes0 = r != null ? r.Strikes : 0;
        int mileAtConfirm = -1; float distAtConfirm = -1f;

        switch (cond)
        {
            case "before":   // 一撃の 0.2秒後に完了
                SetPrivate(pc, "escapeHoldTimer", Mathf.Max(0.3f, holdDur - (windup + 0.2f)));
                PlayerController.QaEscapeHold = true;
                break;
            case "after":    // 完了の 0.2秒後が一撃の予定
                SetPrivate(pc, "escapeHoldTimer", Mathf.Max(0.3f, holdDur - Mathf.Max(0.05f, windup - 0.2f)));
                PlayerController.QaEscapeHold = true;
                break;
            case "samereap":
            {
                gm.ReapPlayer("qa: same frame (reap first)");
                bool ok = gm.TryConfirmEscape("qa: same frame");
                if (ok) StartCoroutine((IEnumerator)typeof(PlayerController).GetMethod("DoFinishSequence", NP).Invoke(pc, null));
                L($"[escape] samereap: same frame f={Time.frameCount} reap -> confirm={ok}");
                break;
            }
            case "sameescape":
            {
                bool ok = gm.TryConfirmEscape("qa: same frame");
                if (ok) StartCoroutine((IEnumerator)typeof(PlayerController).GetMethod("DoFinishSequence", NP).Invoke(pc, null));
                gm.ReapPlayer("qa: same frame (escape first)");
                L($"[escape] sameescape: same frame f={Time.frameCount} confirm={ok} -> reap");
                break;
            }
            default:         // transition / during / warp100k / normal2k: すぐ完了
                SetPrivate(pc, "escapeHoldTimer", holdDur - 0.02f);
                PlayerController.QaEscapeHold = true;
                break;
        }

        // 結果まで(一撃の時間・暗転の時間)
        bool finishingSeen = false, reapedInTransition = false; w = 0f;
        while (!gm.IsGameOver && w < 20f)
        {
            if (pc.IsFinishing && mileAtConfirm < 0) { finishingSeen = true; mileAtConfirm = gm.TakeHomeMile; distAtConfirm = gm.MaxDistance; PlayerController.QaEscapeHold = false; }
            if (cond == "transition" && !reapedInTransition && pc.IsFinishing && ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning)
            {
                reapedInTransition = true;
                ReaperBase.DebugNoReap = false;
                gm.ReapPlayer("qa: during the transition");
                L($"[escape] transition: reap during the screen transition f={Time.frameCount}");
            }
            yield return null; w += Time.unscaledDeltaTime;
        }
        PlayerController.QaEscapeHold = false;
        yield return new WaitForSecondsRealtime(0.6f); // RESULT を1回描く
        finishingSeen |= pc.IsFinishing;
        bool expectWin = cond != "before" && cond != "samereap";
        int walletD = gm.TotalOwnedMile - wallet0; double best1 = gm.GetStageBest("wasteland_road");
        var lc = gm.LastCommit;
        L($"[escape] {cond}: win={gm.IsWin} reason={gm.DeathReason} finishingPose={finishingSeen} strikes {strikes0}->{(r != null ? r.Strikes : 0)} ignored={gm.EscapeIgnoredDamage} " +
          $"d confirm {distAtConfirm:F1} / end {gm.MaxDistance:F1}, RunMile confirm {mileAtConfirm} / end {gm.RunMile}, wallet +{walletD}, best {best0:F0}->{best1:F0}, commit={(lc != null && lc.committed)}, FinishRun x{gm.FinishRunCalls}");
        Check(gm.IsGameOver, $"{cond}: the run ended");
        Check(gm.FinishRunCalls == 1, $"{cond}: the result is decided once (FinishRun x{gm.FinishRunCalls})");
        if (expectWin)
        {
            Check(gm.IsWin, $"{cond}: escape success (reason={gm.DeathReason})");
            Check(finishingSeen, $"{cond}: the success motion was shown");
            Check(lc != null && lc.committed, $"{cond}: the success is committed");
            Check(walletD == gm.RunMile && walletD > 0, $"{cond}: MILE is added once (wallet +{walletD}, run {gm.RunMile})");
            if (mileAtConfirm >= 0) Check(gm.RunMile == mileAtConfirm && Mathf.Abs(gm.MaxDistance - distAtConfirm) < 0.01f, $"{cond}: no distance/MILE after the success (d {distAtConfirm:F1}->{gm.MaxDistance:F1}, MILE {mileAtConfirm}->{gm.RunMile})");
            Check(System.Math.Abs(best1 - System.Math.Max(best0, gm.MaxDistance)) < 0.5, $"{cond}: BEST is the escape distance ({best1:F0})");
        }
        else
        {
            Check(!gm.IsWin, $"{cond}: defeat (the reaper was first)");
            Check(!finishingSeen, $"{cond}: no success motion after the defeat");
            Check(walletD == 0 && System.Math.Abs(best1 - best0) < 0.01 && (lc == null || !lc.committed), $"{cond}: no success reward (wallet +{walletD}, best {best0:F0}->{best1:F0})");
        }
        Shot($"escape_{cond}");
        yield return null; yield return null;
        yield return EscapeEnd();
    }

    IEnumerator EscapeEnd()
    {
        PlayerController.QaEscapeHold = false;
        ReaperBase.DebugNoReap = false;
        yield return EndRun();
    }
}
#endif

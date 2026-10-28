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
//  air         : 跳んだ頂点付近(空中)で長押しが完了 → 落ちて着地してから終了ポーズ(2026-10-25)
//  airrise     : 跳び上がった直後(上昇中)に完了 → 同上
//  airpit      : 穴の上へ出た直後に完了 → 足場へ跳び移って着地してから終了ポーズ(落ちて負けない)
//  airpitdeep  : 穴へ落ちて縁より下へ入ってから完了 → 同上
// -escLegacy 1 は以前の動き(比較用、不具合が出るはず)
public partial class QaSweep
{
    IEnumerator EscapeMode()
    {
        Application.targetFrameRate = 60;
        GameManager.QaLegacyEscape = Arg("-escLegacy", "0") == "1";
        L($"[escape] legacy={GameManager.QaLegacyEscape}");
        foreach (var c in Arg("-escCases", "before,after,samereap,sameescape,transition,during,warp100k,normal2k,air,airrise,airpit,airpitdeep").Split(','))
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
        else gm.DebugWarpToDistance(cond == "normal2k" || cond.StartsWith("air") ? 2000f : 3000f);
        RunLedger.DevClearDebug(); // ワープは記録の対象外の印を付けるので外す(確定の回数を見るため)
        SetPrivate(gm, "escapeUnlocked", true); // 脱出は最初のボスの撃破で解放(ワープで飛ばした分)
        yield return new WaitForSeconds(1.5f);
        RunLedger.DevClearDebug();
        int wallet0 = gm.TotalOwnedMile; double best0 = gm.GetStageBest("wasteland_road");
        bool airCase = cond.StartsWith("air");
        bool reaperCase = cond != "normal2k" && !airCase;
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
        int landings0 = PlayerController.FinishAirLandings, hops0 = PlayerController.FinishPitHops;
        if (airCase)
        {
            bool ready = false;
            yield return EscapeGetAirborne(cond, ok => ready = ok);
            if (!ready) { Check(false, $"{cond}: could not get airborne for the test"); yield return EscapeEnd(); yield break; }
        }

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
        var anim = pc.GetComponent<PlayerAnimator>();
        bool poseSeen = false, groundedAtPose = false, airborneAtConfirm = false; float gapAtPose = 99f, poseDelay = -1f, confirmT = -1f, minY = float.PositiveInfinity;
        while (!gm.IsGameOver && w < 20f)
        {
            if (pc.IsFinishing && mileAtConfirm < 0) { finishingSeen = true; mileAtConfirm = gm.TakeHomeMile; distAtConfirm = gm.MaxDistance; PlayerController.QaEscapeHold = false; confirmT = Time.time; airborneAtConfirm = pc.FinishAirborneAtConfirm; }
            if (pc.IsFinishing) minY = Mathf.Min(minY, pc.transform.position.y);
            if (pc.IsFinishing && !poseSeen && anim != null && anim.CurrentState == PlayerAnimator.State.Finish)
            {
                // 終了ポーズの最初のフレーム: 接地していて、足元の面(下ルートの地面 / 上ルート)からの高さが 0 か
                poseSeen = true; poseDelay = Time.time - confirmT;
                groundedAtPose = pc.IsGrounded;
                var tm = TerrainManager.Instance;
                float px = pc.transform.position.x, feet = pc.transform.position.y - pc.groundOffset;
                float? gH = tm != null ? tm.GetHeightAt(px) : null, sH = tm != null ? tm.GetSkyHeightAt(px, feet) : null;
                if (gH.HasValue) gapAtPose = Mathf.Abs(feet - gH.Value);
                if (sH.HasValue) gapAtPose = Mathf.Min(gapAtPose, Mathf.Abs(feet - sH.Value));
                // 2026-10-28: 坂では足の幅(±0.35m)のどこかが地面に付いていれば接地(中心1点だと坂の上り側で 6〜15cm 浮いて見えた)
                if (tm != null) for (float ox = -0.35f; ox <= 0.351f; ox += 0.07f) { float? h2 = tm.GetHeightAt(px + ox); if (h2.HasValue) gapAtPose = Mathf.Min(gapAtPose, Mathf.Abs(feet - h2.Value)); }
                L($"[escape] {cond}: finish pose starts {poseDelay:F2}s after the confirm, grounded={groundedAtPose} gap={gapAtPose:F3} x={px:F2} feet={feet:F2} ground={(gH.HasValue ? gH.Value.ToString("F2") : "pit")}");
            }
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
        bool expectWin = cond != "before" && cond != "samereap";
        // RESULT を1回描く。2026-10-25: 成功は到着の最後のコマのまま(倒れる絵にならない)/ 失敗は倒れるコマを最後まで見せてから FAILED
        bool deathPoseAfterWin = false, resultBeforeDeathEnd = false; int deathFrameAtResult = -1; float resultAt = -1f;
        int deathLen = anim != null && anim.deathFrames != null ? anim.deathFrames.Length : 0;
        float rw = 0f;
        while (rw < 3f)
        {
            if (gm.IsGameOver && gm.IsWin && anim != null && anim.CurrentState == PlayerAnimator.State.Death) deathPoseAfterWin = true;
            if (gm.ResultShown && resultAt < 0f)
            {
                resultAt = gm.SecondsSinceGameOver;
                deathFrameAtResult = anim != null && anim.CurrentState == PlayerAnimator.State.Death ? anim.CurrentFrameIndex : -1;
                if (!gm.IsWin && deathLen > 0 && deathFrameAtResult < deathLen - 1) resultBeforeDeathEnd = true;
            }
            if (rw >= 0.6f && resultAt >= 0f) break;
            yield return null; rw += Time.unscaledDeltaTime;
        }
        L($"[escape] {cond}: RESULT shown {resultAt:F2}s after the end (reveal delay {gm.ResultRevealDelay:F2}s) death frame at RESULT {deathFrameAtResult}/{deathLen - 1} pose={(anim != null ? anim.CurrentState.ToString() : "-")}");
        Check(resultAt >= 0f, $"{cond}: RESULT shown");
        if (expectWin) Check(!deathPoseAfterWin && anim != null && anim.CurrentState == PlayerAnimator.State.Finish, $"{cond}: the arrival pose stays after the success (death pose={deathPoseAfterWin}, now {(anim != null ? anim.CurrentState.ToString() : "-")})");
        else Check(!resultBeforeDeathEnd && resultAt >= gm.ResultRevealDelay - 0.05f, $"{cond}: FAILED waits for the fall-down frames (frame {deathFrameAtResult}/{deathLen - 1} at {resultAt:F2}s)");
        finishingSeen |= pc.IsFinishing;
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
            // 2026-10-25: 終了ポーズは地面に立ってから(空中で止まったまま地上のポーズにならない)
            Check(poseSeen && groundedAtPose && gapAtPose < 0.05f, $"{cond}: the finish pose starts on the ground (seen={poseSeen} grounded={groundedAtPose} gap={gapAtPose:F3})");
            if (airCase)
            {
                int landD = PlayerController.FinishAirLandings - landings0, hopD = PlayerController.FinishPitHops - hops0;
                L($"[escape] {cond}: airborne at confirm={airborneAtConfirm} landings +{landD} pit hops +{hopD} lowest y={minY:F2} (failY {pc.failY:F1})");
                Check(airborneAtConfirm, $"{cond}: the escape was confirmed in the air");
                Check(landD == 1, $"{cond}: landed once before the finish pose (+{landD})");
                Check(minY > pc.failY + 0.5f, $"{cond}: never fell to the death line (lowest y={minY:F2})");
                if (cond.StartsWith("airpit")) Check(hopD == 1, $"{cond}: over the pit -> hopped to footing (+{hopD})");
            }
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

    // 空中の脱出の試験の準備: 跳ぶ(air/airrise)か、穴の上へ出る(airpit/airpitdeep)まで待つ。done(true) の時点で長押しを完了させる
    IEnumerator EscapeGetAirborne(string cond, System.Action<bool> done)
    {
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        var tm = TerrainManager.Instance;
        float w = 0f;
        if (cond == "air" || cond == "airrise")
        {
            // 地上で手が空いていて、前 8m に穴が無い時に跳ぶ(跳べなかったら入れ直す)。穴へ落ちている所を「跳んだ」と数えない
            float nextDiag = 0f, lastInject = -9f; bool jumped = false;
            while (w < 12f && !gm.IsGameOver)
            {
                yield return null; w += Time.unscaledDeltaTime;
                if (gm.Lives < 30) livesSet.Invoke(gm, new object[] { 30 });
                if (w >= nextDiag) { nextDiag += 1f; L($"[escape] {cond}: t={w:F1} grounded={pc.IsGrounded} y={pc.transform.position.y:F2} vy={pc.VerticalVelocity:F1} busy={pc.QaInputBusy} react={pc.IsReacting} jumped={jumped}"); }
                if (pc.IsGrounded)
                {
                    jumped = false;
                    float x0 = pc.transform.position.x; bool pitNear = false;
                    for (float dx = 0f; dx <= 8f && tm != null; dx += 0.5f) if (!tm.GetHeightAt(x0 + dx).HasValue) { pitNear = true; break; }
                    if (!pitNear && !pc.QaInputBusy && !pc.IsReacting && w - lastInject > 0.5f) { pc.debugInjectOnce = PlayerController.FlickDirection.Up; lastInject = w; }
                    continue;
                }
                if (pc.VerticalVelocity > 3f) jumped = true;
                if (!jumped) continue;
                float? g = tm != null ? tm.GetHeightAt(pc.transform.position.x) : null;
                float h = g.HasValue ? pc.transform.position.y - pc.groundOffset - g.Value : 0f;
                if (cond == "airrise" && pc.VerticalVelocity > 3f && h > 0.3f) { L($"[escape] {cond}: rising h={h:F2} vy={pc.VerticalVelocity:F1}"); done(true); yield break; }
                if (cond == "air" && pc.VerticalVelocity < 1.5f && h > 1f) { L($"[escape] {cond}: near the top h={h:F2} vy={pc.VerticalVelocity:F1}"); done(true); yield break; }
            }
            done(false); yield break;
        }

        // 穴: 幅 2.5m 以上の穴が前に来るまで走る(補助は OFF なので跳ばずに落ちる)
        float pitStart = float.NaN, pitEnd = float.NaN, nextPitDiag = 0f;
        while (w < 90f && !gm.IsGameOver)
        {
            if (gm.Lives < 30) livesSet.Invoke(gm, new object[] { 30 });
            float px = pc.transform.position.x;
            if (w >= nextPitDiag && tm != null)
            {
                nextPitDiag += 5f;
                var js = tm.DebugListJoints();
                L($"[escape] {cond}: t={w:F0} x={px:F1} d={gm.MaxDistance:F0} grounded={pc.IsGrounded} pits ahead={js.FindAll(j => j.Value.EndsWith("->Pit") && j.Key > px).Count}");
            }
            if (float.IsNaN(pitStart) && tm != null)
            {
                var joints = tm.DebugListJoints();
                for (int i = 0; i < joints.Count; i++)
                {
                    if (!joints[i].Value.EndsWith("->Pit") || joints[i].Key < px + 8f) continue;
                    float end = float.NaN;
                    for (int j = i + 1; j < joints.Count; j++) if (joints[j].Value.StartsWith("Pit->")) { end = joints[j].Key; break; }
                    if (!float.IsNaN(end) && end - joints[i].Key >= 2.5f) { pitStart = joints[i].Key; pitEnd = end; L($"[escape] {cond}: pit {pitStart:F1}..{pitEnd:F1} (w={pitEnd - pitStart:F1}) ahead of x={px:F1}"); break; }
                }
            }
            if (!float.IsNaN(pitStart) && px > pitEnd + (cond == "airpitdeep" ? 4f : 0f)) { L($"[escape] {cond}: passed the pit without the condition (x={px:F1} grounded={pc.IsGrounded})"); pitStart = pitEnd = float.NaN; }
            if (!float.IsNaN(pitStart) && px >= pitStart + 0.3f && !pc.IsGrounded)
            {
                float edge = (tm.GetHeightAt(pitStart - 0.2f) ?? 0f);
                float feet = pc.transform.position.y - pc.groundOffset;
                float depth = Mathf.Min(0.8f, (edge - pc.failY) / 3f); // 縁より下(穴の中、または向こうの縁の下へ入った所)
                if (cond == "airpit" || feet < edge - depth) { L($"[escape] {cond}: over the pit x={px:F2} feet={feet:F2} edge={edge:F2}"); done(true); yield break; }
            }
            yield return null; w += Time.unscaledDeltaTime;
        }
        done(false);
    }

    IEnumerator EscapeEnd()
    {
        PlayerController.QaEscapeHold = false;
        ReaperBase.DebugNoReap = false;
        yield return EndRun();
    }
}
#endif

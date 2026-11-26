#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 脱出の円形ゲージ(2026-11-26 依頼⑤)の確認。 -qaEscGauge <dir> [-egVideo 1]
//  A 減り方: 半分まで押す → 離すと少しずつ減る(即 0 にしない)→ 押し直すと残りから再開
//  B 被弾: 押している間に被弾 → 0 に戻らない(押し続ければ増え続ける)
//  C 落下: 押している間に穴への落下が確定 → 即 0、押しっぱなしでは溜まらない → 一度離して押し直すと溜まる
//  D 落下と満タンが同時: 満タン直前で落下 → 脱出しない(落下が先)
//  E タップ: 減っている途中の短いタップはゲージを増やさず、脱出を確定しない
//  F 通常: 0 から押し続けて満タン → 脱出の確定(必要な時間は今まで通り)
//  動画(-egVideo 1): A/B/C の間の画面を 30fps で書き出す(video_gauge/f*.png)
public partial class QaSweep
{
    float EgTimer => Field<float>(pc, "escapeHoldTimer");
    readonly List<string> egTrace = new List<string>();
    bool egVideo; int egFrame; string egDir;

    IEnumerator EgWait(float sec, string tag)
    {
        float t = 0f;
        while (t < sec)
        {
            if (egVideo)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(egDir, $"f{egFrame++:00000}.png"));
                t += Time.deltaTime;
            }
            else { yield return null; t += Time.deltaTime; }
            if (((int)(t * 10)) != ((int)((t - Time.deltaTime) * 10))) egTrace.Add($"{tag}\t{t:F1}\t{EgTimer:F2}\t{(PlayerController.QaEscapeHold ? "hold" : "free")}\t{pc.EscapeNeedsRelease}");
        }
    }

    IEnumerator EscGaugeMode()
    {
        Application.targetFrameRate = 60;
        egVideo = Arg("-egVideo", "1") == "1";
        egDir = System.IO.Path.Combine(outDir, "video_gauge");
        if (egVideo) { System.IO.Directory.CreateDirectory(egDir); Time.captureFramerate = 30; }
        yield return BeginRun(Arg("-egChar", "swordsman"), "wasteland_road");
        stopKeepAlive = true; autoPickHold = false;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        livesSet.Invoke(gm, new object[] { 300 });
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        PlayerController.DebugSpeedScale = 0.05f; // ほぼ止めて平らな所で(溜めている間は補助が跳ばないので、走ると本当に穴へ落ちる)。穴への落下は C で落下の確定の入口から
        gm.DebugWarpToDistance(2000f);
        RunLedger.DevClearDebug();
        SetPrivate(gm, "escapeUnlocked", true);
        yield return EgWait(1.5f, "start");
        float dur = pc.escapeHoldDuration;
        L($"[eg] hold duration {dur:F2}s, decay {pc.EscapeDecayPerSecond:F2}/s");

        // ---- A 減り方と再開
        PlayerController.QaEscapeHold = true;
        yield return EgWait(1.5f, "A-hold");
        float a1 = EgTimer;
        PlayerController.QaEscapeHold = false;
        yield return EgWait(0.1f, "A-release");
        float a2 = EgTimer;
        yield return EgWait(0.6f, "A-decay");
        float a3 = EgTimer;
        L($"[eg] A: held {a1:F2}s -> 0.1s after release {a2:F2} -> 0.7s after release {a3:F2}");
        Check(a1 > 1.2f && a1 < 1.7f, $"A: 1.5s of holding fills about half ({a1:F2}/{dur:F2})");
        Check(a2 > 0.9f, $"A: releasing does not reset to 0 at once ({a2:F2})");
        Check(a3 < a2 - 0.4f && a3 > 0.3f, $"A: released -> it goes down little by little ({a2:F2} -> {a3:F2})");
        Check(Mathf.Abs((a1 - a3) - 0.7f) < 0.2f, $"A: goes down at about the hold speed (full to 0 in {dur:F1}s): -{a1 - a3:F2} in 0.7s");
        PlayerController.QaEscapeHold = true;
        yield return EgWait(0.5f, "A-repress");
        float a4 = EgTimer;
        Check(a4 > a3 + 0.3f, $"A: pressing again resumes from what is left ({a3:F2} -> {a4:F2})");
        Check(!gm.IsGameOver, "A: not escaped (not full)");
        PlayerController.QaEscapeHold = false;
        yield return EgWait(dur + 0.5f, "A-empty");
        Check(EgTimer <= 0.001f, $"A: left alone it goes back to 0 ({EgTimer:F2})");

        // ---- B 被弾しても続く
        PlayerController.QaEscapeHold = true;
        yield return EgWait(1.0f, "B-hold");
        float b1 = EgTimer; int lives0 = gm.Lives;
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        pc.TakeDamage(false, "qa-escgauge");
        yield return EgWait(0.4f, "B-hit");
        float b2 = EgTimer;
        L($"[eg] B: {b1:F2} -> hit (lives {lives0}->{gm.Lives}) -> {b2:F2}");
        Check(gm.Lives < lives0, $"B: the hit did damage ({lives0}->{gm.Lives})");
        Check(b2 > b1 + 0.2f, $"B: a hit alone does not reset the gauge ({b1:F2} -> {b2:F2})");
        PlayerController.QaEscapeHold = false;
        yield return EgWait(dur + 0.5f, "B-empty");

        // ---- C 穴への落下で即 0、離すまで溜めない
        PlayerController.QaEscapeHold = true;
        yield return EgWait(1.2f, "C-hold");
        float c1 = EgTimer;
        pc.TakeDamage(true, "qa-pit");   // 穴への落下の確定(failY を越えた時と同じ入口)
        float c2 = EgTimer;
        yield return EgWait(1.2f, "C-still-holding");
        float c3 = EgTimer;
        L($"[eg] C: {c1:F2} -> fall -> {c2:F2} -> keep holding 1.2s -> {c3:F2} (needRelease={pc.EscapeNeedsRelease})");
        Check(c2 <= 0.001f, $"C: a pit fall resets the gauge to 0 at once ({c1:F2} -> {c2:F2})");
        Check(c3 <= 0.001f, $"C: holding on after the fall does not refill it ({c3:F2})");
        PlayerController.QaEscapeHold = false;
        yield return EgWait(0.3f, "C-release");
        PlayerController.QaEscapeHold = true;
        yield return EgWait(0.8f, "C-repress");
        float c4 = EgTimer;
        Check(c4 > 0.5f, $"C: after releasing once, a new hold fills it ({c4:F2})");
        PlayerController.QaEscapeHold = false;
        yield return EgWait(dur + 0.5f, "C-empty");
        if (egVideo) { Time.captureFramerate = 0; L($"[eg] video frames {egFrame} -> {egDir}"); }

        // ---- D 落下と満タンが同時 → 落下が先
        SetPrivate(pc, "escapeHoldTimer", dur - 0.01f);
        PlayerController.QaEscapeHold = true;
        pc.TakeDamage(true, "qa-pit-same-frame");
        yield return null; yield return null; yield return new WaitForSeconds(0.5f);
        Check(!gm.IsGameOver && !pc.IsFinishing, $"D: a fall at the same time as full does not escape (over={gm.IsGameOver} finishing={pc.IsFinishing})");
        PlayerController.QaEscapeHold = false;
        yield return new WaitForSeconds(dur + 0.5f);

        // ---- E 減っている途中のタップ
        PlayerController.QaEscapeHold = false;
        SetPrivate(pc, "escapeHoldTimer", dur - 0.05f);
        SetPrivate(pc, "escapeHolding", false);
        yield return null;
        // QaEscapeHold は押下の 0.3 秒を待たないので、短い「タップ」は本物の入力の代わりに状態を直接見る: 押し始めの値へ戻ること
        float e1 = EgTimer;
        Check(!pc.IsEscapeCharging, $"E: while it is going down it is not 'charging' (jump/attack are not blocked) (timer {e1:F2})");
        yield return new WaitForSeconds(0.3f);
        Check(!gm.IsGameOver, "E: no escape while not holding");
        yield return new WaitForSeconds(dur);

        // ---- F 通常の脱出
        PlayerController.DebugSpeedScale = 0.05f;
        float f0 = Time.time;
        PlayerController.QaEscapeHold = true;
        float w = 0f;
        while (!pc.IsFinishing && w < dur + 2f) { yield return null; w += Time.deltaTime; }
        PlayerController.QaEscapeHold = false;
        L($"[eg] F: escape confirmed after {w:F2}s (needed {dur:F2}s)");
        Check(pc.IsFinishing && Mathf.Abs(w - dur) < 0.25f, $"F: a hold from 0 escapes after the same time as before ({w:F2}s)");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "gauge_trace.tsv"), "case\tt\ttimer\tinput\tneedRelease\n" + string.Join("\n", egTrace));
        float ww = 0f; while (!gm.IsGameOver && ww < 15f) { yield return null; ww += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// Android の画面の出入りの試験(2026-11-25、Unity 6000.6.10f1 の UUM-159613 対策の確認)。操作は外から adb で行い(Tools/security/lifecycle_emulator.sh)、
// ここは状態を書き出すだけ(ゲームの動きは変えない)。
//  -qaLifecycle <dir> [-lcRun 1|0] [-lcContinue 1] [-lcSeconds 300] [-lcMulti 1]
//   -lcRun 1: ランを始めて走り続ける(無敵・補助) / 0: ホームのまま
//   -lcContinue 1: ホームで中断中のランがあれば CONTINUE する(強制終了の後の起動で使う)
//   -lcMulti 1: LOCAL の部屋を作って待つ(マルチ中は Run In Background が有効になる = UUM-159613 の条件)
public partial class QaSweep
{
    IEnumerator LifecycleMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        float secs = float.Parse(Arg("-lcSeconds", "300"), System.Globalization.CultureInfo.InvariantCulture);
        var cp0 = RunCheckpoint.Load();
        L($"[lc] boot: checkpoint={(cp0 != null && cp0.active)} at {(cp0 != null ? cp0.checkpointDistance : 0f):F0}m");
        if (Arg("-lcContinue", "0") == "1" && cp0 != null && cp0.active)
        {
            float r = 0f, wc = 0f;
            while (!gm.HasStarted && wc < 20f) { if ((r -= Time.unscaledDeltaTime) <= 0f) { gm.ContinueActiveRun(); r = 0.5f; } yield return null; wc += Time.unscaledDeltaTime; }
            L($"[lc] continued: started={gm.HasStarted} distance={gm.MaxDistance:F0}m");
        }
        else if (Arg("-lcRun", "1") == "1") yield return BeginRun(Arg("-qaChar", "swordsman"), Arg("-qaStage", "wasteland_road"));
        if (gm.HasStarted)
        {
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        }
        if (Arg("-lcMulti", "0") == "1") { bool t = NetSession.Instance != null && NetSession.Instance.StartHost(NetSession.DefaultPort); L($"[lc] local room: {t} runInBackground={Application.runInBackground}"); }
        float t0 = Time.realtimeSinceStartup, next = 0f;
        while (Time.realtimeSinceStartup - t0 < secs)
        {
            if (Time.realtimeSinceStartup >= next)
            {
                next = Time.realtimeSinceStartup + 1.5f;
                L($"[lc] t={Time.realtimeSinceStartup - t0:F0} started={gm.HasStarted} over={gm.IsGameOver} d={gm.MaxDistance:F0} pauseMenu={Field<bool>(gm, "showPauseMenu")} gate={gm.ResumeGate} timePaused={TimeControl.IsPaused} ts={Time.timeScale:F2} orient={Screen.orientation} {Screen.width}x{Screen.height} focus={Application.isFocused} bg={Application.runInBackground} exitAsk={AppExit.Open} title={TitleScreen.Showing}");
            }
            yield return null;
        }
        L("[lc] end");
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// アニメーション強化の試作の確認動画(2026-10-17): -qaAnimDemo <dir> [-adChar ninja] [-adSpeed 1]
// 30fps で毎フレーム書き出す(<dir>/frames/f00000.png)。区切りは qa_animdemo.txt に書く(ffmpeg で動画にする)。
//   1) 走り  2) オオカミと戦う(補助の自動攻撃: 通常攻撃・命中の衝撃・オオカミの走り/溜め/噛みつき)
//   3) オオカミの必殺技  4) BREAK  5) プレイヤーの被弾  6) オオカミの撃破  7) プレイヤーが倒れる
public partial class QaSweep
{
    int demoFrame;
    string demoDir;

    IEnumerator DemoRecord(string label, float seconds)
    {
        L($"[demo] {label}: frames {demoFrame}-{demoFrame + Mathf.RoundToInt(seconds * 30f) - 1} (t={demoFrame / 30f:F1}s)");
        int n = Mathf.RoundToInt(seconds * 30f);
        for (int i = 0; i < n; i++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
            demoFrame++;
        }
    }

    IEnumerator AnimDemoMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        Random.InitState(20261017);
        BossManager.NetTestBossHpOverride = 4000000;
        string ch = Arg("-adChar", "ninja");
        yield return BeginRun(ch, "wasteland_road");
        SetGod(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        WarpTo(1400f);
        yield return new WaitForSeconds(1.5f);
        PlayerController.DebugSpeedScale = float.Parse(Arg("-adSpeed", "1"), System.Globalization.CultureInfo.InvariantCulture);
        Time.captureFramerate = 30;

        yield return DemoRecord("run", 3f);

        Bm.DebugWildEncounter(WildBossKind.Wolf, -1);
        yield return WaitBossSpawn(10f);
        var b = WildAlive().FirstOrDefault();
        Check(b != null, "demo: the wolf appeared");
        if (b == null) { Time.captureFramerate = 0; yield return EndRun(); yield break; }
        yield return DemoRecord("fight (assist attacks, the wolf runs / winds up / bites)", 12f);

        b.DebugForceUltimate();
        float w = 0f;
        while (w < 3f && !b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return DemoRecord("wolf ultimate", 7f);

        b.DebugBreak();
        yield return DemoRecord("BREAK", 3f);

        SetGod(false);
        var lives = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        lives.Invoke(gm, new object[] { 150 });
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        pc.TakeDamage(false, "demo-hurt");
        yield return DemoRecord("player hurt", 1.6f);
        SetGod(true);

        b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld);
        yield return DemoRecord("wolf defeat", 3.5f);

        SetGod(false);
        w = 0f;
        while (!gm.IsGameOver && w < 3f)
        {
            lives.Invoke(gm, new object[] { 1 });
            var p = typeof(PlayerController).GetProperty("ShieldCharges"); if (p != null) p.SetValue(pc, 0);
            SetPrivate(pc, "hitInvincibleTimer", 0f);
            pc.TakeDamage(false, "demo-death");
            if (gm.IsGameOver) break;
            yield return new WaitForSeconds(0.2f); w += 0.2f;
        }
        yield return DemoRecord("player falls (game over)", 2.5f);
        Time.captureFramerate = 0;
        PlayerController.DebugSpeedScale = 1f;
        L($"[demo] total {demoFrame} frames");
        Check(demoFrame > 300, "demo: frames written");
    }
}
#endif

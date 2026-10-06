#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// GameOver → FAILED → Tap to Retry の確認(2026-10-06、実機で Boss 戦中の死亡後に Retry が効かなかった件)。 -qaRetry <dir>
// いろいろな Gameplay の状態で死亡 → 結果画面 → 「実際のタップと同じ判定」を1回通す(GameManager.DebugResultTapPending)
// → シーンの読み直し → 新しいランの状態(時間/停止理由/ヒットストップ/ボス/保留/補助/遷移/入力)を確かめる。
//  A 雑魚  B Boss戦の開始直後  C Boss必殺技中  D Boss段階移行中  E Boss崩し中  F ラン再開後  G ボス生存+次の関門を通過(保留)
//  H 再戦のボス  I 高速+補助ON(実機の再現: 自然洞窟 61km の関門 → ラン再開 → 127km/h)  J 補助OFF
//  K DEBUG 表示中(+パネルを閉じた直後の指のラッチ)  L DEBUG 表示なし  M/N/O 自然洞窟/荒野街道/天空回廊
public partial class QaSweep
{
    readonly List<string> retryReport = new List<string>();

    IEnumerator RetryMode()
    {
        Application.targetFrameRate = 60;
        var cases = new List<(string name, string stage, System.Func<IEnumerator> setup)>();
        foreach (var st in new[] { "natural_cave", "wasteland_road", "sky_corridor" })
        {
            string s = st;
            cases.Add(($"A zako ({s})", s, () => RtWarp(2400f)));
            cases.Add(($"B boss start ({s})", s, () => RtBoss(960f, null)));
            cases.Add(($"F run resumed ({s})", s, () => RtResumed(960f)));
        }
        cases.Add(("C boss ultimate (cave 10k)", "natural_cave", () => RtBoss(9960f, b => b.DebugForceUltimate())));
        cases.Add(("D boss phase change (cave 10k)", "natural_cave", () => RtBoss(9960f, b => b.DebugSetPhase(Mathf.Max(1, b.PhaseCount - 1)))));
        cases.Add(("E boss stagger (cave 10k)", "natural_cave", () => RtBoss(9960f, b => b.DebugForceBreak())));
        cases.Add(("G boss alive + passed next gate (wasteland)", "wasteland_road", RtPending));
        cases.Add(("H rematch boss (wasteland 5k)", "wasteland_road", RtRematch));
        cases.Add(("I cave 61k resumed + 127km/h assist ON (device repro)", "natural_cave", () => RtDevice(true)));
        cases.Add(("J cave 61k resumed + assist OFF", "natural_cave", () => RtDevice(false)));
        cases.Add(("K debug overlays + panel just closed (latch)", "natural_cave", () => RtDebugUi(true)));
        cases.Add(("L no debug UI", "natural_cave", () => RtDebugUi(false)));
        cases.Add(("Z sky boss (dragon 1k) resumed", "sky_corridor", () => RtResumed(960f)));
        string only = Arg("-qaRetryOnly", "");
        foreach (var c in cases)
        {
            if (only != "" && !c.name.StartsWith(only)) continue;
            yield return RetryCase(c.name, c.stage, c.setup);
        }
        L("");
        foreach (var r in retryReport) L("[retry] " + r);
    }

    IEnumerator RtWarp(float d) { WarpTo(d); yield return new WaitForSeconds(1.5f); }

    IEnumerator WaitBossAlive(float timeout = 25f)
    {
        var bm = BossManager.Instance; float w = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(2.6f); // 登場の演出の後
    }

    IBossBattleDebug AliveBossDebug() => FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IBossBattleDebug>().FirstOrDefault(b => b.DebugAlive);

    IEnumerator RtBoss(float d, System.Action<IBossBattleDebug> act)
    {
        WarpTo(d);
        yield return WaitBossAlive();
        if (act != null)
        {
            var b = AliveBossDebug();
            if (b == null) { Warn("no IBossBattleDebug boss alive"); yield break; }
            act(b);
            yield return new WaitForSeconds(0.25f);
            L($"   boss {b.DebugName} phase {b.Phase}/{b.PhaseCount} ult={b.UltimateRunning} broken={b.Broken}");
        }
    }

    IEnumerator RtResumed(float d)
    {
        yield return RtBoss(d, null);
        BossManager.Instance.DebugForceResume();
        yield return new WaitForSeconds(1.5f);
        Check(BossManager.Instance.RunResumed, "   setup: run resumed with the boss alive");
    }

    IEnumerator RtPending()
    {
        yield return RtResumed(960f);
        PlayerController.DebugSpeedScale = 4f;
        float w = 0f;
        while (gm.MaxDistance < 2150f && w < 40f) { yield return null; w += Time.deltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        L($"   passed {gm.MaxDistance:F0}m with the 1km boss alive (bossPhase={BossManager.Instance.IsBossPhase}, alive={BossManager.Instance.AliveBossCount})");
    }

    IEnumerator RtRematch()
    {
        yield return RtBoss(960f, null);
        BossManager.Instance.DebugFinishEncounter();
        float w = 0f;
        while ((BossManager.Instance.IsBossPhase || gm.IsRewardSequenceRunning) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.5f);
        yield return RtBoss(4960f, null);
        L($"   5km encounter rematch={BossManager.Instance.CurrentEncounterIsRematch} key={BossManager.Instance.CurrentEncounterKey}");
        if (!BossManager.Instance.CurrentEncounterIsRematch) Warn("H: the 5km gate did not draw a rematch this time (pool is random)");
    }

    IEnumerator RtDevice(bool assist)
    {
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(assist);
        yield return RtResumed(60960f);
        PlayerController.DebugSpeedScale = 1.27f;
        yield return new WaitForSeconds(3f);
        L($"   {gm.MaxDistance:F0}m {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h assist={(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.StatusText() : "-")} engaged={HighSpeedAssist.Instance?.Engaged} encounterDirector={(EncounterDirector.Instance != null ? EncounterDirector.Instance.enabled.ToString() : "-")}");
    }

    IEnumerator RtDebugUi(bool on)
    {
        typeof(GameManager).GetProperty("DebugMode").SetValue(gm, on);
        yield return RtResumed(960f);
        if (on)
        {
            // DEBUG パネルを開いて閉じる(閉じた時の指のラッチが残った状態)
            UiInputGate.DebugPanelOpen = true; yield return null;
            UiInputGate.DebugPanelOpen = false; UiInputGate.LatchUntilRelease();
        }
    }

    IEnumerator RetryCase(string name, string stage, System.Func<IEnumerator> setup)
    {
        L($"== {name}");
        int exc0 = excCount;
        yield return BeginRun("swordsman", stage);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.NetTestBossHpOverride = 4000000; // 準備の間にボスが倒れないように(ラン再開/保留の状態を作る)
        yield return setup();
        BossManager.NetTestBossHpOverride = 0;
        // 死亡
        stopKeepAlive = true; yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var old = gm;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 1 });
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        gm.ReapPlayer("qa-retry");
        float w = 0f;
        while (!(gm.IsGameOver && gm.ResultShown && gm.RetryAllowedNow) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        var bm = BossManager.Instance;
        string before = $"over={gm.IsGameOver} shown={gm.ResultShown} retryAllowed={gm.RetryAllowedNow} ts={Time.timeScale:F2} reasons={TimeControl.DescribeActiveReasons()} transitioning={ScreenTransitionManager.Instance?.IsTransitioning} gate={UiInputGate.Blocked} bossStopped={bm.RunEndStopped} cleanups={gm.GameOverCleanups} assist={HighSpeedAssist.Instance?.Engaged}";
        L("   at result: " + before);
        Check(gm.IsGameOver && gm.ResultShown && gm.RetryAllowedNow, $"{name}: FAILED result is shown and retry is allowed");
        Check(gm.GameOverCleanups == 1 && bm.RunEndStopped && !(HighSpeedAssist.Instance != null && HighSpeedAssist.Instance.Engaged), $"{name}: GameOver cleanup ran once (bosses/encounter/assist stopped)");
        yield return new WaitForSecondsRealtime(0.6f);
        // Tap to Retry(実際のタップと同じ判定)
        GameManager.DebugResultTapPending = true;
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        bool reloaded = GameManager.Instance != null && GameManager.Instance != old;
        L($"   tap -> reloaded={reloaded} in {w:F1}s (accepted {(old != null ? old.ResultTapsAccepted : -1)}, ignored {(old != null ? old.ResultTapsIgnored : -1)} {(old != null ? old.LastResultTapBlock : "")})");
        Check(reloaded, $"{name}: Tap to Retry reloads into a new run");
        // 新しいランの初期状態
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        gm = GameManager.Instance; bm = BossManager.Instance;
        int bosses = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Length + FindObjectsByType<DragonController>(FindObjectsSortMode.None).Length + FindObjectsByType<MajinController>(FindObjectsSortMode.None).Length;
        string after = $"started={gm.HasStarted} over={gm.IsGameOver} ts={Time.timeScale:F2} reasons={TimeControl.DescribeActiveReasons()} hitStop={HitStop.ActiveCount} bosses={bosses} bossPhase={bm.IsBossPhase} resumed={bm.RunResumed} stopped={bm.RunEndStopped} assist={HighSpeedAssist.Instance?.Engaged} transitioning={ScreenTransitionManager.Instance?.IsTransitioning} gate={UiInputGate.Blocked} d={gm.MaxDistance:F0}";
        L("   after retry: " + after);
        bool clean = !gm.IsGameOver && Mathf.Approximately(Time.timeScale, 1f) && HitStop.ActiveCount == 0 && bosses == 0 && !bm.IsBossPhase && !bm.RunResumed && !bm.RunEndStopped
                     && !(HighSpeedAssist.Instance != null && HighSpeedAssist.Instance.Engaged) && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) && !UiInputGate.Blocked && gm.MaxDistance < 1f;
        Check(clean, $"{name}: the new run starts clean (no boss/pending/assist/pause/hitstop/transition/input block)");
        // 新しいランが普通に始まって走れる(入力/ボスの出現)
        PlayerController.DebugSpeedScale = 1f;
        typeof(GameManager).GetProperty("DebugMode").SetValue(gm, false);
        yield return BeginRun("swordsman", stage);
        float d0 = gm.MaxDistance; yield return new WaitForSeconds(1.5f);
        Check(gm.MaxDistance > d0 + 5f, $"{name}: the next run runs normally ({d0:F0} -> {gm.MaxDistance:F0}m)");
        Check(excCount == exc0, $"{name}: no exceptions");
        retryReport.Add($"{name}: reloaded={reloaded} clean={clean}");
        yield return EndRun();
        gm = GameManager.Instance;
    }
}
#endif

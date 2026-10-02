#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 「カードを選んだ後に止まる」の再現(2026-10-01)。 -qaStall <dir> [-qaStallMinutes 6] [-qaChars a,b]
// 高速で長く走り、ボスはいろいろな時機に倒し(即/再開直前/再開後/倒さず次の関門を越える)、カードもいろいろな間で選ぶ。
// 選択画面/一時停止メニュー/結果/カウントダウン等ではないのにプレイヤーが3秒動かない「停止」を見つけたら、
// その瞬間の状態をすべて書き出して撮影し、自然に戻るか(各watchdog)を見届ける。
public partial class QaSweep
{
    int stallCount, stallRecovered;

    IEnumerator StallMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        float minutes = float.Parse(Arg("-qaStallMinutes", "6"));
        string[] chars = Arg("-qaChars", "swordsman,ninja,gunslinger,dragon_lancer").Split(',');
        // まず原因の再現と安全ネットの確認(修正前の動き→修正後)
        yield return StallRaceChecks(true);
        yield return StallRaceChecks(false);
        if (Arg("-qaStallRaceOnly", "0") == "1") yield break;
        int guardBase = GameManager.StallGuardRecoveries;
        foreach (string ch in chars)
        {
            yield return BeginRun(ch, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            L($"[stall] {ch}: start");
            StartCoroutine(StallBossKiller());
            float t0 = Time.realtimeSinceStartup, lastX = pc.transform.position.x, still = 0f;
            float kmh = 160f, nextKmh = 0f;
            while (Time.realtimeSinceStartup - t0 < minutes * 60f && !gm.IsGameOver)
            {
                yield return null;
                if (Time.realtimeSinceStartup > nextKmh) { nextKmh = Time.realtimeSinceStartup + Random.Range(8f, 20f); kmh = Random.Range(0, 3) == 0 ? 60f : Random.Range(120f, 320f); SetKmh(kmh); }
                float x = pc.transform.position.x;
                bool moved = Mathf.Abs(x - lastX) > 0.002f;
                lastX = x;
                if (moved || LegitStop()) { still = 0f; continue; }
                still += Time.unscaledDeltaTime;
                if (still > 3f)
                {
                    stallCount++;
                    L($"[STALL] {ch} #{stallCount} d={gm.MaxDistance:F0} {StallState()}");
                    Shot($"stall_{ch}_{stallCount}");
                    float w = 0f;
                    while (w < 40f && !gm.IsGameOver) { yield return null; w += Time.unscaledDeltaTime; if (Mathf.Abs(pc.transform.position.x - lastX) > 0.05f) break; }
                    bool rec = Mathf.Abs(pc.transform.position.x - lastX) > 0.05f;
                    if (rec) stallRecovered++;
                    L($"[STALL] {ch} #{stallCount} {(rec ? $"resumed by itself after {w + 3f:F1}s" : "did NOT resume within 43s")} | now: {StallState()}");
                    if (!rec) { TimeControl.ResetAll(); }
                    still = 0f; lastX = pc.transform.position.x;
                }
            }
            stopKiller = true;
            L($"[stall] {ch}: ran to {gm.MaxDistance:F0}m, card picks so far {cardPicks}");
            yield return EndRun();
            stopKiller = false;
        }
        L($"[stall] total stalls={stallCount} recovered={stallRecovered} picks={cardPicks}");
        Check(stallCount == 0, $"no stall after card choices / boss rewards ({stallCount})");
        // 通常の長いランで安全ネットが働いた = どこかで本当に食い違いが起きている(誤作動も含めて0であるべき)
        L($"[stall] StallGuard recoveries during the long runs: {GameManager.StallGuardRecoveries - guardBase} (last: {GameManager.LastStallGuard})");
        Check(GameManager.StallGuardRecoveries - guardBase == 0, "no StallGuard recovery was needed during normal long runs");
    }

    bool autoPickHold;

    // 「選んだ後の閉じる演出(約0.34秒)の間に次のLevel Upが来る」を狙って起こす。
    // legacy=true は修正前の動き(GameManager.QaLegacyStallBehaviour)。
    IEnumerator StallRaceChecks(bool legacy)
    {
        string tag = legacy ? "legacy" : "fixed";
        GameManager.QaLegacyStallBehaviour = legacy;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        autoPickHold = true;
        var seq = FindFirstObjectByType<RewardCardSequence>(FindObjectsInactive.Include);
        int recBefore = GameManager.StallGuardRecoveries;

        // --- 1) 選択→閉じる演出中にEXPでLevel Up(実際の「敵を倒した直後」と同じ経路) ---
        for (int round = 0; round < 3; round++)
        {
            gm.GrantBonusCardChoice();
            float w = 0f;
            while (!gm.IsRewardSequenceWaitingForSelection && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
            object saved = GetPrivate(gm, "pendingChoices");
            if (round == 0) savedChoices = saved;
            seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.4f); seq.OnCardClicked(0);
            w = 0f;
            while (!seq.IsClosingAfterApply && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
            bool inWindow = seq.IsClosingAfterApply;
            float give = gm.ExpToNext * 1.05f;
            float got = gm.GrantBonusExp(give / Mathf.Max(0.01f, ExpMul()));
            L($"[race:{tag}] round {round}: closingWindow={inWindow} gaveExp={got:F1} -> {StallState()}");
            Shot($"race_{tag}_{round}_a");
            // 次の選択画面が出るか(出るまでの実時間)
            w = 0f;
            while (!gm.IsRewardSequenceWaitingForSelection && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            bool opened = gm.IsRewardSequenceWaitingForSelection;
            L($"[race:{tag}] round {round}: next choice {(opened ? $"opened after {w:F2}s" : "did NOT open within 8s (STALL)")} timeScale={Time.timeScale:F2} levelUpPending={gm.LevelUpPending}");
            if (!opened) Shot($"race_{tag}_{round}_stall");
            if (legacy)
            {
                if (!opened) raceLegacyStalls++;
                // 修正前は30秒watchdogまで止まる。待たずに後片付け
                if (!opened) { seq.ForceReset(); SetPrivate(gm, "levelUpPending", false); SetPrivate(gm, "pendingChoices", null); TimeControl.ResetAll(); SetPrivate(gm, "pendingLevelUpCount", 0); }
                else { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.4f); seq.OnCardClicked(0); }
            }
            else
            {
                Check(opened, $"fixed: next Level Up opens after picking during the closing fade (round {round})");
                if (opened) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.4f); seq.OnCardClicked(0); }
            }
            w = 0f;
            while ((gm.LevelUpPending || seq.IsRunning) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.8f);
        }

        if (!legacy)
        {
            var gmT = typeof(GameManager);
            object choiceOwner = gmT.GetField("pendingChoiceTimeOwner", NP | BindingFlags.Static).GetValue(null);
            // --- 2) 安全ネットA: 選択待ちなのに画面が無い → 同じ3枚を出し直す ---
            SetPrivate(gm, "pendingChoices", savedChoices);
            SetPrivate(gm, "pendingChoiceKind", System.Enum.Parse(gmT.GetNestedType("PendingChoiceKind"), "LevelUp"));
            SetPrivate(gm, "levelUpPending", true);
            TimeControl.Pause(choiceOwner);
            float w = 0f;
            while (!gm.IsRewardSequenceWaitingForSelection && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[race:fixed] guard A (re-offer): {(gm.IsRewardSequenceWaitingForSelection ? $"choice re-opened after {w:F2}s" : "NOT recovered")} last={GameManager.LastStallGuard}");
            Check(gm.IsRewardSequenceWaitingForSelection && w < 4f, "guard A re-offers a missing choice screen (~1s + intro animation)");
            if (gm.IsRewardSequenceWaitingForSelection) { seq.OnCardClicked(1); yield return new WaitForSecondsRealtime(0.4f); seq.OnCardClicked(1); }
            w = 0f; while ((gm.LevelUpPending || seq.IsRunning) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.6f);

            // --- 2b) 安全ネットA: 3枚の情報も無い → 選択無しで解決 ---
            SetPrivate(gm, "pendingChoices", null);
            SetPrivate(gm, "levelUpPending", true);
            TimeControl.Pause(choiceOwner);
            w = 0f;
            while ((gm.LevelUpPending || Time.timeScale == 0f) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[race:fixed] guard A (resolve): {(!gm.LevelUpPending && Time.timeScale > 0f ? $"resolved after {w:F2}s" : "NOT recovered")} last={GameManager.LastStallGuard}");
            Check(!gm.LevelUpPending && Time.timeScale > 0f && w < 2.5f, "guard A resolves an unrecoverable pending choice within ~1s");
            yield return new WaitForSecondsRealtime(0.6f);

            // --- 3) 安全ネットB: 選択待ちの停止理由だけが残った ---
            TimeControl.Pause(choiceOwner);
            w = 0f;
            while (Time.timeScale == 0f && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[race:fixed] guard B: {(Time.timeScale > 0f ? $"resumed after {w:F2}s" : "NOT recovered")} last={GameManager.LastStallGuard}");
            Check(Time.timeScale > 0f && w < 2.5f, "guard B removes an orphaned pause reason within ~1s");
            yield return new WaitForSecondsRealtime(0.6f);

            // --- 4) 安全ネットC: ボス演出のスロー(倍率0)だけが残った ---
            var fake = new object();
            TimeControl.BeginPresentationDrive(fake);
            TimeControl.SetPresentationScale(fake, 0f);
            w = 0f;
            while (Time.timeScale == 0f && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[race:fixed] guard C: {(Time.timeScale > 0f ? $"resumed after {w:F2}s" : "NOT recovered")} last={GameManager.LastStallGuard}");
            Check(Time.timeScale > 0f && !TimeControl.IsPresentationDriving && w < 2.5f, "guard C ends an orphaned presentation slow-down within ~1s");
            L($"[race:fixed] StallGuard recoveries during checks: {GameManager.StallGuardRecoveries - recBefore}");
        }
        else
        {
            L($"[race:legacy] stalls reproduced with the old behaviour: {raceLegacyStalls}/3");
        }
        autoPickHold = false;
        GameManager.QaLegacyStallBehaviour = false;
        yield return EndRun();
    }
    object savedChoices;
    int raceLegacyStalls;
    float ExpMul() { object v = GetPrivate(gm, "expGainMultiplier"); return v is float f ? f : 1f; }

    bool stopKiller;
    // ボスを色々な時機に倒す: 0=すぐ / 1=ラン再開の直前 / 2=ラン再開の後 / 3=次の関門を越えてから
    IEnumerator StallBossKiller()
    {
        int policy = 0;
        while (!stopKiller)
        {
            yield return null;
            var bm = BossManager.Instance;
            if (bm == null || !bm.IsBossPhase || bm.AliveBossCount <= 0 || gm == null || gm.IsGameOver) continue;
            policy = (policy + 1) % 4;
            float w = 0f;
            int gateAtStart = Mathf.FloorToInt(gm.MaxDistance / 1000f);
            while (!stopKiller && bm.IsBossPhase && bm.AliveBossCount > 0 && w < 120f)
            {
                yield return null;
                w += Time.deltaTime;
                bool kill = policy == 0 ? w > 1.5f
                          : policy == 1 ? bm.EncounterSeconds > bm.ResumeSecondsTotal - 0.6f
                          : policy == 2 ? bm.RunResumed && w > bm.ResumeSecondsTotal + 4f
                          : Mathf.FloorToInt(gm.MaxDistance / 1000f) >= gateAtStart + 2 || w > 90f;
                if (kill) { KillAllBosses(); break; }
            }
        }
    }

    // 止まっていて当然の状態
    bool LegitStop()
    {
        if (gm == null || !gm.HasStarted || gm.IsGameOver || gm.CountdownActive) return true;
        if (gm.IsRewardSequenceWaitingForSelection) return true;
        var seqL = FindFirstObjectByType<RewardCardSequence>();
        if (seqL != null && seqL.IsRunning) return true; // 選択画面の出入りの演出中(選択の待ちが無いのに levelUpPending だけ残るのは停止として数える)
        if (pc != null && (pc.IsFinishing || pc.NetIsDowned)) return true;
        var f = typeof(GameManager).GetField("showPauseMenu", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null && (bool)f.GetValue(gm)) return true;
        if (SettingsPanel.IsOpen) return true;
        return false;
    }

    string StallState()
    {
        var bm = BossManager.Instance;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        object pend = typeof(GameManager).GetField("pendingLevelUpCount", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gm);
        object bossDef = typeof(GameManager).GetField("bossRewardDeferredPending", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gm);
        object choiceKind = typeof(GameManager).GetField("pendingChoiceKind", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gm);
        return $"timeScale={Time.timeScale:F2} pauseReasons={TimeControl.DescribeActiveReasons()} presentation={TimeControl.IsPresentationDriving} hitStop={HitStop.ActiveCount} "
            + $"levelUpPending={gm.LevelUpPending} pendingLevelUps={pend} bossRewardDeferred={bossDef} choiceKind={choiceKind} seqRunning={(seq != null && seq.IsRunning)} seqWaiting={gm.IsRewardSequenceWaitingForSelection} seqStep={RewardCardSequence.DebugStep} "
            + $"bossPhase={(bm != null && bm.IsBossPhase)} resumed={(bm != null && bm.RunResumed)} alive={(bm != null ? bm.AliveBossCount : 0)} defeated={(bm != null && bm.BossDefeatedThisPhase)} bossPresentation={(BossMilestonePresentation.Instance != null && BossMilestonePresentation.Instance.IsRunning)} "
            + $"speed={pc.CurrentAutoRunSpeed:F1} reacting={pc.IsReacting} grounded={pc.IsGrounded} y={pc.transform.position.y:F1} bonus={(BonusZone.Instance != null ? BonusZone.Instance.State.ToString() : "-")}";
    }
}
#endif

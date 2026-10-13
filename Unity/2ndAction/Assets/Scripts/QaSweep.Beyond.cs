#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// ラスダン「ONE MORE MILE?」の YES / NO の後の確認(2026-10-13 第3弾)。 -qaBeyond <dir> [-byOnly ABCD] [-byVideo 1] [-byChar swordsman]
//  -memSave で動かす(通常のセーブには触れない)。三姉妹/エンドロールは済んだ扱いで問いかけから始め(開発用の DebugBeginChoice)、
//  ワープ/無敵の印は外して「普通に走って来た」扱いにする(確定の確認のため)。
//  A: YES → 続き(地形/敵/ボスが戻る)→ 脱出   B: YES → 続き → ゲームオーバー   C: YES → 続き → 中断 → 再開 → 脱出   D: NO → ホーム
public partial class QaSweep
{
    bool BY(char c) { string o = Arg("-byOnly", ""); return o == "" || o.IndexOf(c) >= 0; }
    const string LdStage = "last_corridor";
    int byEnemySpawns;

    IEnumerator BeyondMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        UnlockRules.DevUnlockAll = true;
        SaveStore.SetInt(SaveKeys.DevFinalDungeonAlwaysOpen, 1);
        TutorialProgress.MarkOffered(); TutorialProgress.MarkEscapeGuideShown();
        EncounterDirector.Spawned += (r, f, list) => byEnemySpawns += list != null ? list.Count : 0;
        string ch = Arg("-byChar", "swordsman");
        if (BY('A')) yield return BeyondCase('A', ch, true);
        if (BY('B')) yield return BeyondCase('B', ch, true);
        if (BY('C')) yield return BeyondCase('C', ch, true);
        if (BY('D')) yield return BeyondCase('D', ch, false);
    }

    void ByClean() { RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false; RunLedger.DevSet(r => { r.walked = System.Math.Max(r.walked, r.maxReached); r.playSeconds = Mathf.Max(r.playSeconds, 3900f); }); }

    IEnumerator BeyondCase(char c, string ch, bool yes)
    {
        L($"== {c}: {(yes ? "YES" : "NO")} ==");
        yield return BeginRun(ch, LdStage);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(98900f);
        float w = 0f;
        var flow = LastDungeonFlow.Instance;
        while ((flow == null || gm.MaxDistance < 99030f) && w < 30f) { yield return null; w += Time.deltaTime; flow = LastDungeonFlow.Instance; }
        Check(flow != null && flow.Enabled, $"{c}: the last dungeon flow is running ({(flow != null ? flow.Current.ToString() : "none")})");
        if (flow == null) { yield return EndRun(); yield break; }
        flow.DebugBeginChoice();
        w = 0f;
        while ((flow.Choice == null || !flow.Choice.Risen) && w < 20f) { yield return null; w += Time.deltaTime; }
        Check(flow.Choice != null && flow.Choice.Risen, $"{c}: ONE MORE MILE? appears");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        ByClean();
        bool video = c == 'A' && Arg("-byVideo", "0") == "1";
        Coroutine rec = video ? StartCoroutine(RecordUltimate($"beyond_{ch}_{Screen.width}x{Screen.height}", float.Parse(Arg("-byVideoSec", "80"), System.Globalization.CultureInfo.InvariantCulture))) : null;
        if (video) yield return new WaitForSeconds(2f);
        Shot($"by{c}_choice");
        int mile0 = gm.TotalOwnedMile; double best0 = GameManager.ReadStageBest(LdStage);
        string run0 = RunLedger.Current.runId; float dYes = gm.MaxDistance; int take0 = gm.TakeHomeMile;
        flow.Choice.Decide(yes ? flow.Choice.Yes : flow.Choice.No);
        yield return null; yield return null;
        if (!yes)
        {
            w = 0f; while (!gm.IsGameOver && w < 30f) { yield return null; w += Time.deltaTime; }
            Check(gm.IsGameOver && gm.IsWin && gm.QuietFinish, $"{c}: NO ends the run quietly as a success (gameOver={gm.IsGameOver} win={gm.IsWin} quiet={gm.QuietFinish})");
            Check(gm.LastCommit != null && gm.LastCommit.committed && RunLedger.IsCommitted(run0), $"{c}: NO commits the clear ({gm.LastCommit?.skippedWhy})");
            Check(gm.TotalOwnedMile == mile0 + take0, $"{c}: NO pays the run MILE once ({gm.TotalOwnedMile - mile0} of {take0})");
            Check(!RunCheckpoint.HasActiveRun, $"{c}: NO leaves no suspended run");
            var oldGm = gm;
            w = 0f; while ((GameManager.Instance == null || GameManager.Instance == oldGm || GameManager.Instance.HasStarted) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; } // 読み直しの間は Instance が無い/古い
            yield return new WaitForSecondsRealtime(1.5f);
            gm = GameManager.Instance;
            Check(gm != null && !gm.HasStarted, $"{c}: NO returns to the home screen");
            Shot($"by{c}_home");
            yield return new WaitForSecondsRealtime(1f);
            yield break;
        }
        // ---- YES: その場でクリアを確定し、同じランのまま続ける ----
        L($"[{c}] YES at {dYes:F0}m: clear committed={gm.ClearCommit != null && gm.ClearCommit.committed} ({gm.ClearCommit?.skippedWhy}) MILE {mile0}->{gm.TotalOwnedMile} (+{gm.ClearMilePaid}, take-home was {take0}) best {best0:F0}->{GameManager.ReadStageBest(LdStage):F0} run {run0.Substring(0, 8)}->{RunLedger.Current.runId.Substring(0, 8)} world from {gm.LdBeyondFrom:F0}m");
        Check(gm.ClearCommit != null && gm.ClearCommit.committed && RunLedger.IsCommitted(run0), $"{c}: YES commits the clear at once");
        Check(gm.TotalOwnedMile == mile0 + take0 && gm.ClearMilePaid == take0, $"{c}: YES pays the MILE earned so far once ({gm.TotalOwnedMile - mile0} of {take0})");
        Check(GameManager.ReadStageBest(LdStage) >= dYes - 1.0, $"{c}: YES writes the last dungeon BEST ({GameManager.ReadStageBest(LdStage):F0})");
        Check(RunLedger.Current != null && RunLedger.Current.runId != run0 && !RunLedger.IsCommitted(RunLedger.Current.runId), $"{c}: the continuation is a new, unconfirmed ledger");
        Check(!gm.IsGameOver && gm.HasStarted && gm.ActiveRunStageId == LdStage && gm.ActiveRunCharacterId == ch, $"{c}: the same run continues (not home, not another map)");
        Check(gm.TakeHomeMile < 5, $"{c}: the continuation's take-home MILE starts from 0 ({gm.TakeHomeMile})");
        int level0 = gm.Level, lives0 = gm.Lives, cards0 = gm.DeckCards.Count;
        int mileAfterYes = gm.TotalOwnedMile; double bestAfterYes = GameManager.ReadStageBest(LdStage);
        string runCont = RunLedger.Current.runId;
        // 走り出す → 地形/敵/ボスが戻る
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        int spawns0 = byEnemySpawns; float firstEnemyAt = -1f, t0 = Time.time;
        w = 0f;
        float until = c == 'A' ? (gm.LdBeyondFrom + 1900f) : (gm.LdBeyondFrom + 300f);
        bool sawBoss = false; float bossAt = -1f; int pitsAhead = 0;
        while (gm.MaxDistance < until && w < 240f && !gm.IsGameOver)
        {
            if (gm.IsRewardSequenceWaitingForSelection) yield return AutoPickOnce();
            if (firstEnemyAt < 0f && byEnemySpawns > spawns0) { firstEnemyAt = gm.MaxDistance; L($"[{c}] first enemies after YES at {firstEnemyAt:F0}m ({Time.time - t0:F1}s)"); Shot($"by{c}_first_enemies"); }
            if (!sawBoss && BossManager.Instance != null && BossManager.Instance.IsBossPhase) { sawBoss = true; bossAt = gm.MaxDistance; L($"[{c}] boss gate after YES at {bossAt:F0}m"); Shot($"by{c}_boss_gate"); }
            yield return null; w += Time.deltaTime;
        }
        var tm = TerrainManager.Instance;
        if (tm != null) { var p = PlayerController.Instance.transform.position; for (float x = p.x; x < p.x + 400f; x += 1f) if (tm.TryGetGroundFast(x, out _, out bool pit) && pit) { pitsAhead++; x += 4f; } }
        L($"[{c}] after YES ran to {gm.MaxDistance:F0}m in {w:F0}s: enemies spawned {byEnemySpawns - spawns0}, first at {firstEnemyAt:F0}m, boss gate {(sawBoss ? bossAt.ToString("F0") : "-")}m, pits in the next 400m {pitsAhead}, next gate {(BossManager.Instance != null ? BossManager.Instance.NextBossDistance : -1f):F0}m, flow {flow.Current}, Lv {level0}->{gm.Level}");
        Check(byEnemySpawns > spawns0, $"{c}: enemies appear again after YES");
        Check(flow.Current == LastDungeonFlow.State.Beyond && flow.Finale == null, $"{c}: the ending does not play again ({flow.Current})");
        Check(BossManager.Instance != null && !BossManager.SuppressGates && BossManager.Instance.NextBossDistance > 100000f, $"{c}: boss gates are back after 100km (next {(BossManager.Instance != null ? BossManager.Instance.NextBossDistance : -1f):F0}m)");
        if (c == 'A') { Check(sawBoss, $"{c}: a boss gate actually starts after YES"); Check(pitsAhead > 0 || gm.MaxDistance > gm.LdBeyondFrom + 1500f, $"{c}: the terrain (pits) is back ({pitsAhead} pits ahead)"); }
        Check(gm.DeckCards.Count == cards0 && gm.Level >= level0, $"{c}: cards/level are kept (Lv {level0}->{gm.Level})");
        if (rec != null) { float rw = 0f; while (Time.captureFramerate != 0 && rw < 200f) { yield return null; rw += Time.unscaledDeltaTime; } }
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        ByClean();
        if (c == 'C')
        {
            // 中断 → 再開: YES の後から続く(三姉妹/エンドロールは繰り返さない)
            float dSusp = gm.MaxDistance;
            typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
            yield return ResumeGoHome();
            Check(RunCheckpoint.HasActiveRun && RunCheckpoint.Load().ldBeyond, $"{c}: the suspended run remembers it is after YES");
            yield return ResumeContinue("beyond-c");
            flow = LastDungeonFlow.Instance;
            w = 0f; while ((flow == null || !flow.Enabled) && w < 5f) { yield return null; w += Time.deltaTime; flow = LastDungeonFlow.Instance; }
            L($"[{c}] resumed at {gm.MaxDistance:F0}m (suspended at {dSusp:F0}m) flow={(flow != null ? flow.Current.ToString() : "none")} beyond={gm.LdBeyond} next gate {(BossManager.Instance != null ? BossManager.Instance.NextBossDistance : -1f):F0}m take-home {gm.TakeHomeMile} run {RunLedger.Current?.runId.Substring(0, 8)}");
            Check(gm.LdBeyond && gm.MaxDistance >= dYes - 1f && flow != null && flow.Current == LastDungeonFlow.State.Beyond, $"{c}: CONTINUE resumes after YES ({gm.MaxDistance:F0}m, {flow?.Current})");
            Check(RunLedger.Current != null && RunLedger.Current.runId == runCont, $"{c}: the resumed run is the continuation ledger");
            Check(BossManager.Instance != null && !BossManager.SuppressGates && BossManager.Instance.NextBossDistance > 100000f, $"{c}: gates continue after resuming");
            mileAfterYes = gm.TotalOwnedMile;
            yield return new WaitForSeconds(3f);
            ByClean();
        }
        if (c == 'B')
        {
            int take = gm.TakeHomeMile;
            stopKeepAlive = true; yield return null;
            for (int i = 0; i < 60 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 99999); yield return new WaitForSecondsRealtime(0.05f); }
            yield return new WaitForSecondsRealtime(1f);
            L($"[{c}] game over after YES at {gm.MaxDistance:F0}m: MILE {mileAfterYes}->{gm.TotalOwnedMile} (lost take-home {take}) best {GameManager.ReadStageBest(LdStage):F0}");
            Check(gm.IsGameOver && !gm.IsWin, $"{c}: game over after YES");
            Check(gm.TotalOwnedMile == mileAfterYes && GameManager.ReadStageBest(LdStage) >= bestAfterYes - 0.5 && RunLedger.IsCommitted(run0), $"{c}: the clear committed at YES is kept (MILE/BEST/committed)");
            Check(!RunLedger.IsCommitted(runCont), $"{c}: the continuation after YES is not committed");
            Check(!RunCheckpoint.HasActiveRun, $"{c}: no suspended run after the game over");
            yield return EndRun();
            yield break;
        }
        // 脱出: 続きの分だけ確定
        int take2 = gm.TakeHomeMile; float dEnd = gm.MaxDistance; int mileBefore = gm.TotalOwnedMile;
        gm.Win();
        yield return WaitTut(() => gm.ResultShown, 8f);
        L($"[{c}] escaped after YES at {dEnd:F0}m: committed={gm.LastCommit?.committed} MILE {mileBefore}->{gm.TotalOwnedMile} (+{take2}) best {GameManager.ReadStageBest(LdStage):F0}");
        Check(gm.LastCommit != null && gm.LastCommit.committed && RunLedger.IsCommitted(runCont), $"{c}: escaping after YES commits the continuation ({gm.LastCommit?.skippedWhy})");
        Check(gm.TotalOwnedMile == mileBefore + take2 && gm.TotalOwnedMile - mileAfterYes == take2, $"{c}: only the continuation's MILE is paid (no double count of the clear: +{gm.TotalOwnedMile - mileBefore})");
        Check(GameManager.ReadStageBest(LdStage) >= dEnd - 1.0, $"{c}: the BEST includes the distance after YES ({GameManager.ReadStageBest(LdStage):F0})");
        Check(!RunCheckpoint.HasActiveRun, $"{c}: no suspended run after escaping");
        Shot($"by{c}_result");
        yield return EndRun();
    }

    IEnumerator AutoPickOnce()
    {
        var ids = gm.DebugPendingChoiceIds;
        if (ids != null && ids.Length > 0) yield return PickInChoice(ids[0]);
        else yield return null;
    }
}
#endif

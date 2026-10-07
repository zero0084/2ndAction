#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 記録の確定と解放(2026-10-08 仕様変更)の自動テスト: -qaUnlock <dir>(テスト用データの中で行う)
//  A 新規: 荒野街道と黒剣士だけが見える(他は一覧に出ない・闘技場ボタンも無い・総数も出ない)
//  B 同じ 30,000m でも、倒れたら記録/解放なし、帰還したら確定(BEST/累計/解放)。結果画面の表示
//  C 同じランを二度確定しない / 確定済みの中断データは再開しない
//  D 中断(ホームへ)は未確定 → CONTINUE で同じランの続き(使ったデバッグ機能も持ち越す)→ 帰還で確定
//  E お嬢様騎士(1,000m ちょうど/1,000.5m/リタイア/闘技場)
//  F 疾走出発の範囲 = 成功して確定した BEST まで(55km なら 50km まで)
//  G NEW の表示(一覧を開くと消える)と、解放後の一覧
//  H ラスダン(死神の遭遇は戦闘開始で保存、成功ランの累計 1,000,000m)
//  I 既存データの移行(持っている物/正式な解放は消さない)
public partial class QaSweep
{
    const string UW = UnlockRules.Wasteland, UC = UnlockRules.Cave, US = UnlockRules.Sky, ULD = "last_corridor";

    // ワープで準備した距離を「走った」ことにして終える(win=帰還 / false=倒れる)
    IEnumerator UnlockRun(string stage, string ch, float meters, bool win, float walked = -1f)
    {
        yield return BeginRun(ch, stage);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        if (meters > 50f) { WarpTo(meters); yield return null; yield return null; }
        UnlockRules.RunSkipped = false;
        RunLedger.DevClearDebug();
        float wk = walked >= 0f ? walked : meters;
        RunLedger.DevSet(r => { r.maxReached = System.Math.Max(r.maxReached, meters); r.walked = wk; r.playSeconds = Mathf.Max(r.playSeconds, meters / 25f + 5f); });
        UnlockRules.OnRunDistance(stage, meters);
        yield return null;
        if (win) gm.Win();
        else
        {
            stopKeepAlive = true; yield return null;
            for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 99999); yield return new WaitForSecondsRealtime(0.05f); }
        }
        yield return WaitTut(() => gm.ResultShown, 6f);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    IEnumerator UnlockModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();

        // ---- A 新規
        L("== A new data ==");
        var visibleStages = StageDatabase.AllStages.Where(d => StageDatabase.IsAvailable(d)).Select(d => d.stageId).ToList();
        Check(visibleStages.Count == 1 && visibleStages[0] == UW && !UnlockRules.IsArenaUnlocked, $"A: only Wasteland Road is available ({string.Join(",", visibleStages)})");
        var visibleChars = CharacterDatabase.AllCharacters.Where(c => UnlockRules.IsCharacterVisible(c.characterId)).Select(c => c.characterId).ToList();
        Check(visibleChars.Count == 1 && visibleChars[0] == "swordsman", $"A: only the Swordsman is visible ({string.Join(",", visibleChars)})");
        gm.SetSelectedCharacter("dual_blade");
        Check(gm.SelectedCharacterId == "swordsman", "A: a locked character cannot be selected");
        gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.2f);
        var ss = FindFirstObjectByType<StageSelectUI>();
        int activeCards = ss.cardSlotRects.Count(r => r != null && r.gameObject.activeSelf);
        Check(activeCards == 1, $"A: map select shows exactly one card (no locked frames) ({activeCards})");
        string ui = AllUiText();
        Check(!ui.Contains("LOCKED") && !ui.Contains("30,000") && !ui.Contains("?" + "??") && !ui.Contains("LAST CORRIDOR"), "A: no lock label/condition/???/hidden names in map select");
        Shot("unlock_A_stage_select_new");
        yield return new WaitForSecondsRealtime(0.3f);
        typeof(StageSelectUI).GetMethod("Close").Invoke(ss, null); yield return new WaitForSecondsRealtime(1.2f);
        gm.OpenCharacterSelect(); yield return new WaitForSecondsRealtime(1.2f);
        var cs = FindFirstObjectByType<CharacterSelectUI>();
        int activeChars = cs.cardSlotRects.Count(r => r != null && r.gameObject.activeSelf);
        Check(cs.VisibleCount == 1 && activeChars == 1, $"A: character select shows only the Swordsman ({cs.VisibleCount}/{activeChars})");
        ui = AllUiText();
        Check(!ui.Contains("LOCKED") && !ui.Contains("/ 12") && !ui.Contains("DUAL BLADE") && !ui.Contains("解放の条件"), "A: no locked characters, conditions or total count in character select");
        Shot("unlock_A_char_select_new");
        yield return new WaitForSecondsRealtime(0.3f);
        cs.Close(); yield return new WaitForSecondsRealtime(1.2f);

        // ---- B 同じ 30,000m: 倒れる vs 帰還
        L("== B fail vs success ==");
        double best0 = GameManager.ReadStageBest(UW), life0 = ProgressStats.LifetimeDistance;
        int toasts0 = NoticeQueue.ToastsShown;
        yield return UnlockRun(UW, "swordsman", 30500f, win: false);
        Check(NoticeQueue.ToastsShown > toasts0, "B: reaching 30,000m shows only a provisional banner (no names)");
        Check(gm.LastCommit != null && !gm.LastCommit.committed, "B: game over -> not committed");
        Check(System.Math.Abs(GameManager.ReadStageBest(UW) - best0) < 0.5 && System.Math.Abs(ProgressStats.LifetimeDistance - life0) < 0.5, $"B: game over -> map BEST and lifetime unchanged (best {GameManager.ReadStageBest(UW):F0}, lifetime {ProgressStats.LifetimeDistance:F0})");
        Check(!UnlockRules.OfficialStageUnlocked(UC) && !UnlockRules.IsCharacterUnlocked("dual_blade"), "B: game over after 30,000m -> no Cave / Dual Blade");
        Shot("unlock_B_result_failed");
        yield return new WaitForSecondsRealtime(0.3f);
        yield return EndRun();
        yield return UnlockRun(UW, "swordsman", 30500f, win: true, walked: 30500f);
        var com = gm.LastCommit;
        Check(com != null && com.committed && com.newBest, "B: returned safely -> committed as a new BEST");
        Check(GameManager.ReadStageBest(UW) >= 30500 && GameManager.ReadStageBest(UW) < 30520, $"B: map BEST = the distance reached ({GameManager.ReadStageBest(UW):F0})");
        Check(System.Math.Abs(ProgressStats.LifetimeDistance - life0 - 30500) < 1.0, $"B: lifetime += the distance actually run ({ProgressStats.LifetimeDistance - life0:F0})");
        Check(UnlockRules.OfficialStageUnlocked(UC) && UnlockRules.IsCharacterUnlocked("dual_blade") && com.unlockedStages.Contains(UC) && com.unlockedChars.Contains("dual_blade"), "B: success -> Cave and Dual Blade unlocked (shown on the result)");
        Check(!UnlockRules.IsCharacterUnlocked("gunslinger"), "B: the 50,000m character is still locked");
        Shot("unlock_B_result_success");
        yield return new WaitForSecondsRealtime(0.3f);

        // ---- C 二度確定しない
        L("== C once ==");
        string runId = RunLedger.Current != null ? RunLedger.Current.runId : "";
        double life1 = ProgressStats.LifetimeDistance;
        var again = RunLedger.CommitSuccess(30500);
        Check(!again.committed && again.skippedWhy == "already committed" && System.Math.Abs(ProgressStats.LifetimeDistance - life1) < 0.5, $"C: the same run is never committed twice ({again.skippedWhy})");
        var fake = new RunCheckpoint.Data { active = true, stageId = UW, characterId = "swordsman", checkpointDistance = 30000 };
        fake.ledger = new RunLedger.Run { runId = runId, stageId = UW };
        RunCheckpoint.Save(fake); RunCheckpoint.Reload();
        Check(!RunCheckpoint.HasActiveRun, "C: a suspended save of an already committed run cannot be resumed");
        RunCheckpoint.Clear();
        yield return EndRun();

        // ---- D 中断 → CONTINUE → 帰還
        L("== D suspend ==");
        yield return BeginRun("swordsman", UW);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        WarpTo(3000f); yield return null;
        RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
        RunLedger.DevSet(r => { r.maxReached = 3200; r.walked = 3200; r.playSeconds = 200; });
        string dRun = RunLedger.Current.runId;
        double bestD0 = GameManager.ReadStageBest(UW), lifeD0 = ProgressStats.LifetimeDistance;
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        yield return ResumeGoHome();
        Check(RunCheckpoint.HasActiveRun && RunCheckpoint.Load().ledger.runId == dRun, "D: returning home keeps the run unconfirmed in the suspend data (with its ledger)");
        Check(System.Math.Abs(GameManager.ReadStageBest(UW) - bestD0) < 0.5 && System.Math.Abs(ProgressStats.LifetimeDistance - lifeD0) < 0.5, "D: suspending writes no records");
        yield return ResumeContinue("unlock-d");
        Check(RunLedger.Current != null && RunLedger.Current.runId == dRun && RunLedger.Current.walked >= 3200, $"D: CONTINUE resumes the same run ledger ({RunLedger.Describe(RunLedger.Current)})");
        RunLedger.MarkDebug("qa-debug");
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        yield return ResumeGoHome();
        yield return ResumeContinue("unlock-d2");
        Check(RunLedger.Current != null && RunLedger.Current.debugUsed, "D: a debug feature used before suspending is still marked after CONTINUE");
        gm.Win();
        yield return WaitTut(() => gm.ResultShown, 6f);
        Check(gm.LastCommit != null && !gm.LastCommit.committed, "D: a run that used a debug feature is not recorded even after returning safely");
        yield return EndRun();

        // ---- E お嬢様騎士
        L("== E noble lady ==");
        UnlockRules.OnRealGameOver(UW, 1000.5);
        Check(!UnlockRules.IsCharacterUnlocked("noble_lady"), "E: a game over at 1,000.5m does not unlock");
        DebugRun.Begin("qa arena-like");
        UnlockRules.OnRealGameOver(UW, 300);
        DebugRun.End("qa");
        Check(!UnlockRules.IsCharacterUnlocked("noble_lady"), "E: arena/practice/debug runs do not count");
        UnlockRules.OnRealGameOver(UW, 1000.0);
        Check(UnlockRules.IsCharacterUnlocked("noble_lady"), "E: a game over at exactly 1,000m unlocks the Noble Lady");
        SaveStore.SetString(UnlockRules.CharsKey, string.Join(",", CharacterDatabase.AllCharacters.Select(c => c.characterId).Where(id => id != "noble_lady" && UnlockRules.IsCharacterUnlocked(id) && id != "swordsman")));
        UnlockRules.Reload();
        Check(!UnlockRules.IsCharacterUnlocked("noble_lady"), "E: (reset for the real run)");
        yield return UnlockRun(UW, "swordsman", 900f, win: false);
        Check(UnlockRules.IsCharacterUnlocked("noble_lady"), $"E: a real game over at {RunLedger.Current?.maxReached:F0}m (<=1,000m) unlocks the Noble Lady");
        yield return EndRun();

        // ---- F 疾走出発の範囲
        L("== F sprint range ==");
        GameManager.WriteStageBest(UW, 55000);
        SprintRecords.DevUnlockAll = false;
        foreach (int g in new[] { 10, 20, 30, 40, 50 }) SprintRecords.MarkGateCleared(UW, g);
        bool ok50 = SprintRecords.IsUnlocked(UW, 50000, out string why50);
        bool ok60 = SprintRecords.IsUnlocked(UW, 60000, out string why60);
        Check(ok50 && !ok60, $"F: confirmed best 55km -> 50km selectable, 60km not ({why50} / {why60})");

        // ---- G NEW の表示と、解放後の一覧
        L("== G NEW ==");
        yield return ReloadHome();
        for (int i = 0; i < 12 && (NoticeQueue.Open || NoticeQueue.Pending > 0); i++) { yield return WaitTut(() => NoticeQueue.Open, 2f); NoticeQueue.DebugAnswer(); yield return null; }
        Check(UnlockRules.IsNewStage(UC) && UnlockRules.AnyNewStage && UnlockRules.AnyNewCharacter, "G: newly unlocked map/characters are NEW");
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("unlock_G_home_new");
        yield return new WaitForSecondsRealtime(0.3f);
        gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.2f);
        ss = FindFirstObjectByType<StageSelectUI>();
        int caveIdx = StageDatabase.AllStages.ToList().FindIndex(d => d.stageId == UC);
        var badge = ss.cardSlotRects[caveIdx].Find("NewBadge");
        Check(ss.cardSlotRects.Count(r => r != null && r.gameObject.activeSelf) == 2 && badge != null && badge.gameObject.activeSelf, "G: map select now shows 2 maps, the Cave with NEW");
        Shot("unlock_G_stage_select_new");
        yield return new WaitForSecondsRealtime(0.3f);
        typeof(StageSelectUI).GetMethod("Close").Invoke(ss, null); yield return new WaitForSecondsRealtime(1.2f);
        Check(!UnlockRules.IsNewStage(UC), "G: opening the list marks it seen (no NEW next time)");
        gm.OpenCharacterSelect(); yield return new WaitForSecondsRealtime(1.2f);
        cs = FindFirstObjectByType<CharacterSelectUI>();
        Check(cs.VisibleCount == 3, $"G: character select shows Swordsman + Dual Blade + Noble Lady ({cs.VisibleCount})");
        Shot("unlock_G_char_select_new");
        yield return new WaitForSecondsRealtime(0.3f);
        cs.Close(); yield return new WaitForSecondsRealtime(1.2f);
        Check(!UnlockRules.AnyNewCharacter, "G: characters seen");

        // ---- H ラスダン
        L("== H last dungeon ==");
        yield return BeginRun("swordsman", UW);
        BossManager.Instance.DebugSpawnReaper();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(UnlockRules.ReaperMetOn(UW) && !UnlockRules.ReaperMetOn(UC), "H: the reaper fight start is recorded for that map (no defeat needed)");
        yield return EndRun();
        UnlockRules.OnReaperMet(UC); UnlockRules.OnReaperMet(US);
        double need = ProgressStats.UnlockDistance - ProgressStats.LifetimeDistance;
        yield return UnlockRun(US, "swordsman", 40000f, win: false, walked: (float)need + 10f);
        Check(!ProgressStats.FinalDungeonUnlocked, "H: a failed run's distance does not count toward the 1,000,000m");
        yield return EndRun();
        UnlockRules.DevUnlockAll = true; // 天空を選ぶため(正式な解放とは別)
        yield return UnlockRun(US, "swordsman", 500f, win: true, walked: (float)need + 10f);
        UnlockRules.DevUnlockAll = false;
        Check(ProgressStats.FinalDungeonUnlocked && UnlockRules.IsCharacterUnlocked("dragonkin"), $"H: reaper on all 3 maps + successful runs' lifetime >= 1,000,000m -> last dungeon + Dragonkin ({ProgressStats.LifetimeDistance:F0})");
        yield return EndRun();
        yield return ReloadHome();
        bool sawReveal = false;
        for (int i = 0; i < 10 && !sawReveal; i++)
        {
            yield return WaitTut(() => NoticeQueue.Open, 4f);
            if (NoticeQueue.CurrentId == "lastdungeon_reveal") { sawReveal = true; break; }
            if (!NoticeQueue.Open) break;
            NoticeQueue.DebugAnswer(); yield return null;
        }
        Check(sawReveal, $"H: the special reveal is shown ({NoticeQueue.CurrentId})");
        Shot("unlock_H_reveal");
        yield return new WaitForSecondsRealtime(0.4f);
        for (int i = 0; i < 6 && (NoticeQueue.Open || NoticeQueue.Pending > 0); i++) { NoticeQueue.DebugAnswer(); yield return WaitTut(() => NoticeQueue.Open, 2f); }
        yield return ReloadHome(); yield return new WaitForSecondsRealtime(1.5f);
        Check(!NoticeQueue.Open && SaveStore.GetInt(UnlockRules.RevealShownKey, 0) == 1, "H: the reveal is saved as shown");

        // ---- I 移行
        L("== I migration ==");
        SaveProfile.Switch(true, true); yield return ReloadHome();
        SaveStore.DeleteKey(UnlockRules.InitKey);
        SaveStore.SetString(SaveKeys.LifetimeDistance, "54321");
        SaveStore.SetString(SaveKeys.StageBestPrefix + UC, "12345");
        string note = UnlockRules.EnsureInitialized();
        L("[migration] " + note);
        Check(UnlockRules.OfficialStageUnlocked(UC) && UnlockRules.IsArenaUnlocked && CharacterDatabase.AllCharacters.Where(c => c.characterId != "dragonkin").All(c => UnlockRules.IsCharacterUnlocked(c.characterId)),
            "I: an existing player keeps every map/character (nothing taken away)");
        Check(!UnlockRules.AnyNewStage && !UnlockRules.AnyNewCharacter, "I: no NEW marks for an existing player's unlocks");
        Check(System.Math.Abs(GameManager.ReadStageBest(UC) - 12345) < 0.5 && System.Math.Abs(ProgressStats.LifetimeDistance - 54321) < 0.5, "I: old records are kept as they are (not guessed/removed)");
        Check(Leaderboard.VerifiedBest(UC) == null, "I: old records are not eligible for the ranking");

        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    static string AllUiText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None)) if (t != null && t.isActiveAndEnabled) sb.Append(t.text).Append('|');
        return sb.ToString();
    }
}
#endif

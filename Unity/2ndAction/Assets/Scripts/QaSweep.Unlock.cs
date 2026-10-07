#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 初期状態と解放条件(2026-10-07)の自動テスト: -qaUnlock <dir>(テスト用データの中で行う)
public partial class QaSweep
{
    IEnumerator UnlockModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        const string W = UnlockRules.Wasteland, C = UnlockRules.Cave, S = UnlockRules.Sky, LD = "last_corridor";

        // ---- 新規
        L("== new data ==");
        Check(StageDatabase.IsAvailable(StageDatabase.FindById(W)) && !StageDatabase.IsAvailable(StageDatabase.FindById(C)) && !StageDatabase.IsAvailable(StageDatabase.FindById(S)) && !UnlockRules.IsArenaUnlocked && !StageDatabase.IsAvailable(StageDatabase.FindById(LD)),
            "new: only Wasteland Road is open (cave/sky/arena/last dungeon locked)");
        var unlockedChars = CharacterDatabase.AllCharacters.Where(c => UnlockRules.IsCharacterUnlocked(c.characterId)).Select(c => c.characterId).ToList();
        Check(unlockedChars.Count == 1 && unlockedChars[0] == "swordsman", $"new: only the Swordsman is unlocked ({string.Join(",", unlockedChars)})");
        Check(!UnlockRules.IsCharacterVisible("dragonkin"), "new: Dragonkin is hidden");
        gm.SetSelectedCharacter("dual_blade");
        Check(gm.SelectedCharacterId == "swordsman", "new: a locked character cannot be selected");
        // 画面: マップ選択(ラスダンは枠ごと無い)/ キャラ選択(竜人なし、LOCKED)
        gm.OpenStageSelect();
        yield return new WaitForSecondsRealtime(1.2f);
        var ss = FindFirstObjectByType<StageSelectUI>();
        int ldIndex = StageDatabase.AllStages.ToList().FindIndex(d => d.stageId == LD);
        Check(ss != null && !ss.LastDungeonVisible && (ldIndex < 0 || !ss.cardSlotRects[ldIndex].gameObject.activeSelf), "new: the last dungeon is not shown in map select at all");
        int caveIndex = StageDatabase.AllStages.ToList().FindIndex(d => d.stageId == C);
        var lockText = ss.cardSlotRects[caveIndex].Find("LockLabel")?.GetComponent<UnityEngine.UI.Text>();
        Check(lockText != null && lockText.gameObject.activeSelf && lockText.text.Contains("30,000"), $"new: the cave card shows its condition and progress ({lockText?.text.Replace("\n", " / ")})");
        Check(!AllUiText().Contains("LAST CORRIDOR") && !AllUiText().Contains("ラスダン"), "new: no text about the last dungeon anywhere in the open UI");
        Shot("unlock_stage_select_new");
        typeof(StageSelectUI).GetMethod("Close").Invoke(ss, null);
        yield return new WaitForSecondsRealtime(1.2f);
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.2f);
        var cs = FindFirstObjectByType<CharacterSelectUI>();
        int dk = CharacterDatabase.AllCharacters.ToList().FindIndex(c => c.characterId == "dragonkin");
        Check(cs != null && cs.VisibleCount == CharacterDatabase.AllCharacters.Count - 1 && !cs.cardSlotRects[dk].gameObject.activeSelf, $"new: character select hides Dragonkin ({cs?.VisibleCount} visible)");
        Check(!AllUiText().Contains("DRAGONKIN"), "new: no Dragonkin name in the UI");
        Shot("unlock_char_select_new");
        cs.Close();
        yield return new WaitForSecondsRealtime(1.2f);

        // ---- 距離の解放(実際のランで到達した瞬間)
        L("== distance unlocks ==");
        yield return BeginRun("swordsman", W);
        gm.DebugWarpToDistance(29960f);
        UnlockRules.RunSkipped = false; // 実際に走った扱い(ワープの分は QA の準備)
        Check(!UnlockRules.OfficialStageUnlocked(C), "dist: 29,960m -> cave still locked");
        float t = 0f;
        while (gm.MaxDistance < 30010f && t < 20f) { yield return null; t += Time.unscaledDeltaTime; }
        Check(UnlockRules.OfficialStageUnlocked(C) && UnlockRules.IsCharacterUnlocked("dual_blade"), $"dist: reaching 30,000m on Wasteland unlocks the Cave and the Dual Blade at that moment ({gm.MaxDistance:F0}m)");
        Check(!UnlockRules.IsCharacterUnlocked("gunslinger"), "dist: 50,000m character still locked");
        Check(SaveStore.GetString(UnlockRules.StagesKey, "").Contains(C), "dist: saved immediately");
        Check(NoticeQueue.ToastsShown > 0, "dist: a small in-run banner (no stop)");
        // ゲームオーバーでも取り消さない
        stopKeepAlive = true; yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 9999); yield return new WaitForSecondsRealtime(0.05f); }
        Check(gm.IsGameOver && UnlockRules.OfficialStageUnlocked(C), "dist: the unlock stays after a game over");
        yield return EndRun();
        yield return ReloadHome();
        Check(UnlockRules.OfficialStageUnlocked(C) && UnlockRules.IsCharacterUnlocked("dual_blade"), "dist: still unlocked after reload");
        // お知らせ: 重ならず順番に
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        string n1 = NoticeQueue.CurrentId;
        Shot("unlock_notice_1");
        yield return new WaitForSecondsRealtime(0.4f);
        NoticeQueue.DebugAnswer();
        yield return WaitTut(() => NoticeQueue.Open, 3f);
        string n2 = NoticeQueue.CurrentId;
        NoticeQueue.DebugAnswer();
        Check(new[] { n1, n2 }.Contains("unlock_s:natural_cave") && new[] { n1, n2 }.Contains("unlock_c:dual_blade"), $"dist: both unlock notices shown one after another at home ({n1}, {n2})");
        yield return ReloadHome();
        yield return new WaitForSecondsRealtime(2f);
        Check(!NoticeQueue.Open, "dist: notices not repeated after reload");
        // ワープ(開発版)で飛ばしたランは数えない
        yield return BeginRun("swordsman", W);
        gm.DebugWarpToDistance(49990f);
        t = 0f; while (gm.MaxDistance < 50020f && t < 15f) { yield return null; t += Time.unscaledDeltaTime; }
        Check(!UnlockRules.IsCharacterUnlocked("gunslinger"), "dist: a run that used the debug warp does not unlock");
        yield return EndRun();
        // 闘技場/Debug Run の距離は数えない
        DebugRun.Begin("qa unlock");
        UnlockRules.OnRunDistance(C, 31000);
        DebugRun.End("qa");
        Check(!UnlockRules.OfficialStageUnlocked(S), "dist: Debug Run / arena distances do not count");
        // 天空 / 闘技場 / 50k / 100k(到達の瞬間)
        Check(!UnlockRules.RunSkipped, "dist: the 'skipped run' mark is cleared when the run ends");
        UnlockRules.OnRunDistance(C, 29999.9);
        Check(!UnlockRules.OfficialStageUnlocked(S), "dist: 29,999.9m is not enough");
        UnlockRules.OnRunDistance(C, 30000);
        Check(UnlockRules.OfficialStageUnlocked(S) && UnlockRules.IsCharacterUnlocked("archer"), "dist: exactly 30,000m on the Cave unlocks Sky + Archer");
        UnlockRules.OnRunDistance(S, 30000);
        Check(UnlockRules.IsArenaUnlocked && UnlockRules.IsCharacterUnlocked("ninja"), "dist: 30,000m on Sky unlocks the arena + Ninja");
        UnlockRules.OnRunDistance(W, 100000);
        Check(UnlockRules.IsCharacterUnlocked("gunslinger") && UnlockRules.IsCharacterUnlocked("dragon_lancer"), "dist: 100,000m on Wasteland unlocks 50k and 100k characters");

        // ---- お嬢様騎士
        L("== noble lady ==");
        // リタイア(中断)はゲームオーバーではない: 何も呼ばない(2026-10-08)
        Check(!UnlockRules.IsCharacterUnlocked("noble_lady"), "lady: retiring at 500m does not unlock");
        UnlockRules.OnRealGameOver(W, 1000.5);
        Check(!UnlockRules.IsCharacterUnlocked("noble_lady"), "lady: a game over at 1,000.5m does not unlock");
        yield return BeginRun("swordsman", W);
        yield return new WaitForSecondsRealtime(1f);
        stopKeepAlive = true; yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 9999); yield return new WaitForSecondsRealtime(0.05f); }
        Check(gm.IsGameOver && UnlockRules.IsCharacterUnlocked("noble_lady"), $"lady: a real game over at {gm.MaxDistance:F0}m (<=1,000m) unlocks the Noble Lady");
        yield return EndRun();
        yield return ReloadHome();
        for (int i = 0; i < 12 && (NoticeQueue.Open || NoticeQueue.Pending > 0); i++) { yield return WaitTut(() => NoticeQueue.Open, 2f); NoticeQueue.DebugAnswer(); yield return null; }

        // ---- ラスダン(隠し)
        L("== last dungeon ==");
        ProgressStats.DevSetLifetime(1000000);
        UnlockRules.OnReaperMet(W); UnlockRules.OnReaperMet(C);
        Check(!ProgressStats.FinalDungeonUnlocked, "ld: 1,000,000m + reaper on 2 maps -> still locked");
        ProgressStats.DevSetLifetime(999999);
        UnlockRules.OnReaperMet(S);
        Check(!ProgressStats.FinalDungeonUnlocked && !UnlockRules.IsCharacterVisible("dragonkin"), "ld: reaper on all 3 maps + 999,999m -> still locked, Dragonkin hidden");
        gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.2f);
        ss = FindFirstObjectByType<StageSelectUI>();
        Check(!ss.LastDungeonVisible, "ld: still not shown in map select");
        typeof(StageSelectUI).GetMethod("Close").Invoke(ss, null); yield return new WaitForSecondsRealtime(1.2f);
        ProgressStats.DevSetLifetime(1000000); // Flush で判定
        Check(ProgressStats.FinalDungeonUnlocked && UnlockRules.IsCharacterUnlocked("dragonkin"), "ld: both conditions -> unlocked + Dragonkin unlocked");
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        Check(NoticeQueue.CurrentId == "lastdungeon_reveal", $"ld: the one-time reveal shows at home ({NoticeQueue.CurrentId})");
        Shot("unlock_lastdungeon_reveal");
        yield return new WaitForSecondsRealtime(0.4f);
        NoticeQueue.DebugAnswer();
        yield return WaitTut(() => NoticeQueue.Open, 3f);
        Check(NoticeQueue.CurrentId == "unlock_c:dragonkin", $"ld: then the Dragonkin notice ({NoticeQueue.CurrentId})");
        NoticeQueue.DebugAnswer();
        yield return ReloadHome();
        yield return new WaitForSecondsRealtime(2f);
        Check(!NoticeQueue.Open && SaveStore.GetInt(UnlockRules.RevealShownKey, 0) == 1, "ld: the reveal is not shown again");
        gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.2f);
        ss = FindFirstObjectByType<StageSelectUI>();
        Check(ss.LastDungeonVisible, "ld: now in map select");
        Shot("unlock_stage_select_all");
        typeof(StageSelectUI).GetMethod("Close").Invoke(ss, null); yield return new WaitForSecondsRealtime(1.2f);
        // 再起動(印の読み直し)でも維持
        SaveProfile.ResetCacheForTests(); UnlockRules.Reload();
        Check(ProgressStats.FinalDungeonUnlocked && UnlockRules.IsCharacterUnlocked("dragonkin") && UnlockRules.IsArenaUnlocked, "ld: kept after restart");

        // ---- 実際の死神の出現でマップ別に記録
        L("== reaper ==");
        SaveProfile.Switch(true, true); yield return ReloadHome(); TutorialProgress.MarkOffered();
        yield return BeginRun("swordsman", W);
        BossManager.Instance.DebugSpawnReaper();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(UnlockRules.ReaperMetOn(W) && !UnlockRules.ReaperMetOn(C), "reaper: the reaper fight on Wasteland is recorded for Wasteland only");
        yield return EndRun();

        // ---- 累計距離: CONTINUE で同じ区間を二重に足さない / ラスダンは数えない
        L("== lifetime ==");
        yield return ReloadHome();
        yield return BeginRun("swordsman", W);
        WarpTo(2000f);
        yield return new WaitForSecondsRealtime(0.5f);
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        float cpD = gm.MaxDistance;
        t = 0f; while (gm.MaxDistance < cpD + 150f && t < 15f) { yield return null; t += Time.unscaledDeltaTime; }
        float high = gm.MaxDistance;
        yield return ResumeGoHome();
        double life0 = ProgressStats.LifetimeDistance;
        yield return ResumeContinue("lifetime");
        t = 0f; while (gm.MaxDistance < high + 100f && t < 25f) { yield return null; t += Time.unscaledDeltaTime; }
        double add = ProgressStats.LifetimeDistance - life0, fresh = gm.MaxDistance - high;
        L($"[lifetime] checkpoint {cpD:F0} highest {high:F0} now {gm.MaxDistance:F0}: lifetime +{add:F0} (new ground {fresh:F0})");
        Check(System.Math.Abs(add - fresh) < 5.0, $"lifetime: CONTINUE does not add the already-run stretch again (+{add:F0} vs new {fresh:F0})");
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        yield return EndRun();

        // ---- 既存データの移行
        L("== migration ==");
        SaveProfile.Switch(true, true); yield return ReloadHome();
        SaveStore.DeleteKey(UnlockRules.InitKey);
        SaveStore.SetString(SaveKeys.LifetimeDistance, "54321");
        SaveStore.SetString(SaveKeys.StageBestPrefix + C, "12345");
        string note = UnlockRules.EnsureInitialized();
        L("[migration] " + note);
        Check(UnlockRules.OfficialStageUnlocked(C) && UnlockRules.OfficialStageUnlocked(S) && UnlockRules.IsArenaUnlocked && CharacterDatabase.AllCharacters.All(c => UnlockRules.IsCharacterUnlocked(c.characterId)),
            "migration: an existing player keeps every map/character available");
        Check(!ProgressStats.FinalDungeonUnlocked && UnlockRules.ReaperMapsMet == 0, "migration: last dungeon follows its own flag; reaper-per-map is not invented");
        Check(System.Math.Abs(UnlockRules.Reach(C) - 12345) < 0.5, "migration: progress taken from the map BEST");
        UnlockRules.RequeuePending();
        Check(NoticeQueue.Pending == 0, "migration: no unlock notices for an existing player");

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

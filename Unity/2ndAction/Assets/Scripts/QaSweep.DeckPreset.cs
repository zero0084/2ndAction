#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// デッキのプリセット(2026-10-31)の自動テスト: -qaDeckPreset <dir> [-dpPhase all|ui|plant|migrate|check] [-dpShared <dir>]
//  all  … テスト用データ(SaveProfile)の中で A〜M
//  ui   … 画面だけ(縦画面の確認用。-screen-width 486 -screen-height 1080 で起動)
//  plant/migrate/check … 本当の再起動(専用の保存ファイル -fileSave だけ。3回起動する)
//    plant = 旧版(プリセット無し)のデッキを保存 / migrate = 起動でプリセット 1 へ・2〜8 は空・案内1回 / check = 再起動で変わらない
public partial class QaSweep
{
    const string DpA = "attack_up", DpB = "speed_up", DpC = "heart_up", DpD = "jump_power_up", DpE = "attack_speed_up";

    IEnumerator DeckPresetMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        string phase = Arg("-dpPhase", "all");
        if (phase == "plant" || phase == "migrate" || phase == "check") { yield return DeckPresetRestart(phase); yield break; }

        UnlockRules.QaOverride = true; // テスト用データでは洞窟/天空が未解放のため(保存しない)
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        SaveStore.Save();
        foreach (var id in new[] { DpA, DpB, DpC, DpD, DpE }) if (CardInventory.GetTotalCount(id) < 2) CardInventory.AddCard(id, 1, 2);
        if (phase == "ui") { yield return DeckPresetUiShots(); SaveProfile.Switch(false, true); yield return ReloadHome(); yield break; }

        // ---- A: 8 セット ----
        Check(DeckPresets.Loaded && Enumerable.Range(0, 8).All(i => DeckPresets.Get(i) != null) && DeckPresets.Get(8) == null, "A: there are exactly 8 deck presets");
        L($"[A] active {DeckPresets.Active + 1}, names: {string.Join(" / ", Enumerable.Range(0, 8).Select(DeckPresets.DisplayName))}");

        // ---- B: 切り替えと同じカードを別のプリセットで ----
        if (DeckPresets.Active != 0) gm.ActivatePreset(0);
        gm.SetDeck(new[] { DpA, DpB, DpC });
        Check(gm.ActivatePreset(1) && gm.DeckCards.Count == 0 && DeckPresets.Active == 1, "B: switching to an empty preset 2 gives an empty deck");
        Check(gm.AddToDeck(DpA) && gm.AddToDeck(DpD), "B: card A (also in preset 1) can be put into preset 2");
        Check(!gm.AddToDeck(DpA) && gm.LastDeckAddResult == GameManager.DeckAddResult.SameCardInDeck, "C: the same card cannot be added twice to one preset");
        Check(gm.ActivatePreset(0) && gm.DeckCards.SequenceEqual(new[] { DpA, DpB, DpC }), "B: back to preset 1 = A,B,C");
        Check(DeckPresets.Get(1).cards.SequenceEqual(new[] { DpA, DpD }), "B: preset 2 keeps A,D");
        Check(SaveStore.GetString("DeckCardIds", "") == $"{DpA},{DpB},{DpC}" && SaveStore.GetInt(DeckPresets.ActiveKey, -1) == 0, "B: DeckCardIds and the active number are saved together");
        Check(!gm.ActivatePreset(0), "B: choosing the preset already in use does nothing");

        // ---- D: 名前 ----
        Check(gm.RenamePreset(1, "荒野疾走") && DeckPresets.Get(1).name == "荒野疾走", "D: preset 2 can be renamed");
        gm.RenamePreset(2, "  ABCDEFGHIJKLMNOPQRSTUVWXYZ\n  ");
        Check(DeckPresets.Get(2).name == "ABCDEFGHIJKLMNOP", $"D: names are trimmed, control characters removed and cut at 16 ({DeckPresets.Get(2).name})");
        gm.RenamePreset(2, "");
        Check(DeckPresets.DisplayName(2).EndsWith(" 3"), $"D: an empty name shows the number ({DeckPresets.DisplayName(2)})");

        // ---- E: コピー ----
        Check(gm.CopyPreset(1, 2) && DeckPresets.Get(2).cards.SequenceEqual(new[] { DpA, DpD }) && DeckPresets.Get(2).name == "荒野疾走", "E: copy preset 2 -> 3 copies cards and name");
        Check(gm.CopyPreset(2, 0) && gm.DeckCards.SequenceEqual(new[] { DpA, DpD }), "E: copying onto the preset in use replaces the deck");
        gm.SetDeck(new[] { DpA, DpB, DpC });

        // ---- F: 初期化 ----
        Check(gm.ClearPreset(2) && DeckPresets.Get(2).cards.Count == 0 && DeckPresets.Get(2).name == "", "F: clearing preset 3 removes cards and name");
        Check(gm.ClearPreset(0) && gm.DeckCards.Count == 0 && SaveStore.GetString("DeckCardIds", "x") == "", "F: clearing the preset in use empties the deck");
        gm.SetDeck(new[] { DpA, DpB, DpC });

        // ---- G: 使用中でないプリセットのカードは合成/変換から 1 枚守る ----
        gm.ActivatePreset(1); // 使用中 = A,D(プリセット1 = A,B,C)
        int lockedB = gm.GetDeckLockedCountForStack(DpB, 1), lockedE = gm.GetDeckLockedCountForStack(DpE, 1);
        Check(lockedB == 1 && lockedE == 0, $"G: a card only in an inactive preset is protected by 1 copy (B {lockedB}, E {lockedE})");
        gm.ActivatePreset(0);

        // ---- H: 持っていないカード(別の Lv があれば替える / 無ければ外す)----
        string b3 = DdKey(DpB, 3);
        CardInventory.AddCard(b3, 3, 1);
        var p4 = DeckPresets.Get(3);
        p4.cards = new List<string> { DdKey(DpB, 7), DdKey("no_such_card_x", 1), DpE };
        Check(gm.ActivatePreset(3), "H: switch to preset 4 with a card at a level not owned and an unknown card");
        L($"[H] deck {string.Join(",", gm.DeckCards)} dropped {gm.LastPresetDropped} swapped {gm.LastPresetSwapped}");
        Check(gm.DeckCards.Count == 2 && GameManager.DeckKindOf(gm.DeckCards[0]) == DpB && gm.DeckCards[1] == DpE && gm.LastPresetSwapped == 1 && gm.LastPresetDropped == 1,
            "H: the not-owned level is replaced by an owned level of the same card, the unknown card is removed");
        gm.ActivatePreset(0);

        // ---- I: 全部埋めて読み直す ----
        var ids = new[] { DpA, DpB, DpC, DpD, DpE };
        for (int i = 0; i < 8; i++)
        {
            gm.ActivatePreset(i);
            gm.SetDeck(ids.Skip(i % 5).Concat(ids.Take(i % 5)).Take(1 + i % 5));
            gm.RenamePreset(i, "P" + (i + 1));
        }
        gm.ActivatePreset(5);
        var before = Enumerable.Range(0, 8).Select(i => DeckPresets.DisplayName(i) + ":" + string.Join(",", DeckPresets.Get(i).cards)).ToList();
        yield return ReloadHome();
        gm = GameManager.Instance;
        var after = Enumerable.Range(0, 8).Select(i => DeckPresets.DisplayName(i) + ":" + string.Join(",", DeckPresets.Get(i).cards)).ToList();
        L("[I] " + string.Join(" | ", after));
        Check(before.SequenceEqual(after) && DeckPresets.Active == 5 && gm.DeckCards.SequenceEqual(DeckPresets.Get(5).cards), "I: all 8 presets, names and the preset in use survive a reload");

        // ---- J: マップ/モードの記録 ----
        gm.ActivatePreset(2);
        DeckPresets.Remember("solo", "natural_cave");
        gm.ActivatePreset(4);
        DeckPresets.Remember("solo", "sky_corridor");
        gm.ActivatePreset(0);
        gm.SetSelectedStage("natural_cave");
        Check(DeckPresets.Active == 2, $"J: choosing the cave switches to the preset used there last ({DeckPresets.Active + 1})");
        string content2 = string.Join(",", DeckPresets.Get(2).cards);
        gm.SetSelectedStage("sky_corridor");
        Check(DeckPresets.Active == 4 && string.Join(",", DeckPresets.Get(2).cards) == content2, "J: choosing the sky corridor switches to its preset, contents unchanged");
        gm.ActivatePreset(1);
        gm.SetSelectedStage("wasteland_road");
        Check(DeckPresets.Active == 1, "J: a map with no memory keeps the preset in use (no surprise switch)");
        DeckPresets.Remember("local", "wasteland_road");
        Check(DeckPresets.Remembered("local", "") == 1 && DeckPresets.Remembered("online", "") < 0, "J: LOCAL/ONLINE keep their own memory (mode only, the HOST picks the map)");

        // ---- K: ラン中は切り替えない / ランの開始で記録 ----
        gm.ActivatePreset(6);
        yield return BeginRun("swordsman", "wasteland_road");
        string deckRun = string.Join(",", gm.DeckCards);
        Check(!gm.CanChangeDeckNow && !gm.ActivatePreset(1) && string.Join(",", gm.DeckCards) == deckRun, "K: during a run the preset cannot be switched");
        Check(DeckPresets.Remembered("solo", "wasteland_road") == 6, $"K: starting a run remembers preset 7 for solo + wasteland road ({DeckPresets.Remembered("solo", "wasteland_road") + 1})");
        yield return EndRun();
        yield return ReloadHome();
        gm = GameManager.Instance;
        gm.ActivatePreset(0);
        gm.SetSelectedStage("wasteland_road");
        Check(DeckPresets.Active == 6, "K: after the run, choosing wasteland road again picks preset 7");

        // ---- L: 引き継ぎのファイルの確認 ----
        var cv = typeof(SaveTransfer).GetMethod("CheckValue", BindingFlags.NonPublic | BindingFlags.Static);
        string good = SaveStore.GetString(DeckPresets.PresetsKey, "");
        string Res(string k, SaveType t, string v) => (string)cv.Invoke(null, new object[] { k, t, v });
        Check(Res(DeckPresets.PresetsKey, SaveType.String, good) == null, "L: the saved presets pass the data-transfer check");
        Check(Res(DeckPresets.PresetsKey, SaveType.String, "{\"v\":1,\"presets\":[{\"name\":\"x\",\"cards\":[\"no_such_card_x\"]}]}") != null, "L: a preset with an unknown card is rejected");
        Check(Res(DeckPresets.ActiveKey, SaveType.Int, "8") != null && Res(DeckPresets.ActiveKey, SaveType.Int, "7") == null, "L: the preset number must be 0..7");

        // ---- M: 画面 ----
        yield return DeckPresetUiShots();
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    IEnumerator DeckPresetUiShots()
    {
        gm.ActivatePreset(0);
        gm.RenamePreset(0, "荒野疾走");
        gm.RenamePreset(1, "Cave Run");
        gm.SetDeck(new[] { DpA, DpB, DpC });
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.2f);
        var pu = gm.deckEditUI.DebugPresetUi;
        Check(pu != null, "M: the preset bar is on the deck screen");
        Shot("dp_bar");
        yield return new WaitForSecondsRealtime(0.3f);
        pu.DebugOpenList();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(DeckPresetUi.Open && UiInputGate.Blocked, "M: the preset list is open and blocks the deck screen behind it");
        Shot("dp_list");
        yield return new WaitForSecondsRealtime(0.3f);
        pu.DebugRename(1);
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("dp_rename");
        yield return new WaitForSecondsRealtime(0.3f);
        pu.DebugPickCopy(0);
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("dp_copy_pick");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(pu.HandleBack() && DeckPresetUi.Open && pu.HandleBack() && !DeckPresetUi.Open, "M: back closes the copy step, then the list");
        gm.ActivatePreset(1);
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("dp_bar_preset2");
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(0.8f);
    }

    // 本当の再起動(専用の保存ファイルだけ)
    IEnumerator DeckPresetRestart(string phase)
    {
        string shared = Arg("-dpShared", outDir);
        System.IO.Directory.CreateDirectory(shared);
        string expectPath = System.IO.Path.Combine(shared, "deckpreset_expect.txt");
        Check(FileSaveStore.Active, "runs on the dedicated save file (-fileSave), not the normal save");
        if (!FileSaveStore.Active) yield break;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        if (phase == "plant")
        {
            foreach (var id in new[] { DpA, DpB, DpC }) CardInventory.AddCard(id, 1, 1);
            var keep = gm.DeckCards.Where(d => GameManager.DeckKindOf(d) != DpA && GameManager.DeckKindOf(d) != DpB && GameManager.DeckKindOf(d) != DpC).Take(5).ToList();
            var old = new List<string> { DpA, DpB }; old.AddRange(keep); old.Add(DpC);
            SaveStore.SetString("DeckCardIds", string.Join(",", old));
            SaveStore.DeleteKey(DeckPresets.PresetsKey); SaveStore.DeleteKey(DeckPresets.ActiveKey); SaveStore.DeleteKey(DeckPresets.MemoryKey); // 前の版 = プリセットの保存が無い
            SaveStore.Save();
            System.IO.File.WriteAllText(expectPath, string.Join(",", old) + "\n" + DdOwned());
            L($"[dp] planted old deck {string.Join(",", old)}");
            Check(!SaveStore.HasKey(DeckPresets.PresetsKey), "plant: an old-version save (deck, no presets) is written");
            yield break;
        }
        var lines = System.IO.File.ReadAllLines(expectPath);
        string deck = string.Join(",", gm.DeckCards);
        L($"[dp] {phase}: deck {deck} migrated {DeckPresets.MigratedFromOldSave} active {DeckPresets.Active + 1} p1 {string.Join(",", DeckPresets.Get(0).cards)}");
        Check(deck == lines[0], $"{phase}: the deck in use is unchanged ({deck})");
        Check(string.Join(",", DeckPresets.Get(0).cards) == lines[0] && DeckPresets.Active == 0, $"{phase}: preset 1 holds the old deck and is in use");
        Check(Enumerable.Range(1, 7).All(i => DeckPresets.Get(i).cards.Count == 0 && DeckPresets.Get(i).name == ""), $"{phase}: presets 2-8 are empty");
        Check(DdOwned() == lines[1], $"{phase}: no owned card is lost");
        Check(SaveStore.HasKey(DeckPresets.PresetsKey) && SaveStore.GetString("DeckCardIds", "") == lines[0], $"{phase}: the presets are saved next to the old deck key");
        if (phase == "migrate")
        {
            Check(DeckPresets.MigratedFromOldSave == 1, "migrate: the presets were created from the old save on this start");
            yield return WaitTut(() => NoticeQueue.Open, 6f);
            Check(NoticeQueue.CurrentId == "deck_presets", $"migrate: the player is told once ({NoticeQueue.CurrentId})");
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("dp_restart_notice");
            if (NoticeQueue.Open) NoticeQueue.DebugAnswer();
        }
        else
        {
            yield return new WaitForSecondsRealtime(3f);
            Check(DeckPresets.MigratedFromOldSave == 0 && NoticeQueue.CurrentId != "deck_presets", "check: after another restart nothing is migrated again and no notice is shown");
        }
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// デッキの同一カード重複禁止(2026-10-30)の自動テスト: -qaDeckDup <dir> [-ddPhase all|ui|plant|migrate|check] [-ddShared <dir>]
//  all  … テスト用データ(SaveProfile)の中で A〜J と K(ホームの読み直し)と L(画面)を行う
//  ui   … 画面だけ(縦画面の確認用。-screen-width 486 -screen-height 1080 で起動)
//  plant/migrate/check … 本当の再起動の確認(専用の保存ファイル -fileSave だけを使う。3回起動する)
//    plant   = 前の版の重複入りデッキを保存して終了 / migrate = 起動で最初の1枚だけ残る・所持は変わらない・案内1回
//    check   = もう一度起動しても同じデッキ・案内も保存もしない
public partial class QaSweep
{
    const string DdA = "attack_up", DdB = "speed_up", DdC = "heart_up";

    static string DdKey(string id, int lv)
    {
        var d = CardDatabase.FindBaseById(id);
        var v = new CardVariant { mainId = id, level = lv, rarity = Mathf.Clamp(d != null ? d.rarity : 1, 1, 5) };
        v.AddAbility(id, lv);
        return v.ToKey();
    }
    static string DdOwned() => string.Join(";", CardInventory.Stacks.Where(s => s.count > 0).OrderBy(s => s.cardId).ThenBy(s => s.level).Select(s => $"{s.cardId}@{s.level}x{s.count}"));
    static bool DdDistinct(IEnumerable<string> deck) { var l = deck.ToList(); return l.Select(GameManager.DeckKindOf).Distinct().Count() == l.Count; }

    IEnumerator DeckDupMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        string phase = Arg("-ddPhase", "all");
        if (phase == "plant" || phase == "migrate" || phase == "check") { yield return DeckDupRestart(phase); yield break; }

        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1); // 「12枚になりました」の案内はこのテストでは出さない
        SaveStore.Save();
        if (phase == "ui") { yield return DeckDupUi(); SaveProfile.Switch(false, true); yield return ReloadHome(); yield break; }

        // ---- J: 新規データの初期デッキ ----
        L($"[J] starting deck {gm.DeckCards.Count}: {string.Join(",", gm.DeckCards)}");
        Check(gm.DeckCards.Count > 0 && DdDistinct(gm.DeckCards), $"J: a new player's starting deck has no duplicate card ({gm.DeckCards.Count} cards)");

        // ---- A〜D ----
        foreach (var id in new[] { DdA, DdA, DdA, DdB, DdC }) CardInventory.AddCard(id, 1, 1);
        string a3 = DdKey(DdA, 3);
        CardInventory.AddCard(a3, 3, 1);
        gm.SetDeck(new string[0]);
        string owned0 = DdOwned();
        Check(gm.AddToDeck(DdA) && gm.DeckCards.Count == 1, "A: card A goes into an empty deck");
        Check(!gm.AddToDeck(DdA) && gm.LastDeckAddResult == GameManager.DeckAddResult.SameCardInDeck && gm.DeckCards.Count == 1,
            $"B: the same card A cannot be added twice (owned {CardInventory.GetTotalCount(DdA)} copies, result {gm.LastDeckAddResult})");
        Check(!gm.AddToDeck(a3) && gm.LastDeckAddResult == GameManager.DeckAddResult.SameCardInDeck && gm.DeckCards.Count == 1,
            $"C: card A at another level (Lv3) counts as the same card ({gm.LastDeckAddResult})");
        Check(gm.DeckCards[0] == DdA, "C: the deck keeps the card it had (no automatic swap)");
        Check(gm.AddToDeck(DdB) && gm.DeckCards.Count == 2, "D: a different card B can be added");
        Check(DdOwned() == owned0, "A-D: owned cards are unchanged by deck edits");

        // ---- E: 12枚すべて別のカード / おすすめ編成・一括の設定は重複を入れない ----
        var twelve = CardDatabase.AllCards.Take(12).Select(c => c.cardId).ToList();
        foreach (var id in twelve) if (CardInventory.GetTotalCount(id) < 2) CardInventory.AddCard(id, 1, 2);
        gm.SetDeck(twelve);
        Check(gm.DeckCards.Count == 12 && gm.DeckCards.SequenceEqual(twelve), $"E: 12 different cards make a full deck ({gm.DeckCards.Count})");
        int skip0 = GameManager.SetDeckDuplicatesSkipped;
        gm.SetDeck(new[] { DdA, a3, DdB, DdA, DdC });
        Check(gm.DeckCards.SequenceEqual(new[] { DdA, DdB, DdC }) && GameManager.SetDeckDuplicatesSkipped - skip0 == 2,
            $"E: a bulk set with duplicates keeps only the first of each card ({string.Join(",", gm.DeckCards)})");
        var rec = CardDatabase.BuildRecommendedDeck(GameManager.DeckCapacity);
        gm.SetDeck(rec);
        Check(DdDistinct(rec) && DdDistinct(gm.DeckCards), $"E: the recommended deck has no duplicates ({gm.DeckCards.Count})");
        var start = DefaultSave.StartingDeck(GameManager.DeckCapacity);
        Check(DdDistinct(start), "J: the starting-deck generator has no duplicates");

        // ---- G: デッキとキャラカード(別の入れ物)に同じカード ----
        gm.SetDeck(new[] { DdC });
        bool eq = gm.EquipCharacterCard(0, DdC, 1);
        Check(eq && gm.DeckCards.Contains(DdC), "G: the same card can be in the deck and in a character card slot at the same time (only one deck exists; presets do not exist)");
        gm.EquipCharacterCard(0, null, 1);

        // ---- H: Lvの上がったカードを手に入れても、デッキはそのまま / 外して入れ直せる ----
        gm.SetDeck(new[] { DdA, DdB });
        string a5 = DdKey(DdA, 5);
        CardInventory.AddCard(a5, 5, 1);
        yield return ReloadHome();
        Check(gm.DeckCards.SequenceEqual(new[] { DdA, DdB }), $"H: getting a higher-level copy does not change the deck ({string.Join(",", gm.DeckCards)})");
        Check(gm.RemoveFromDeck(DdA) && gm.AddToDeck(a5) && gm.DeckCards.Count == 2 && gm.DeckCards.Contains(a5),
            "H: remove the old one, then re-register the higher-level copy");
        yield return ReloadHome();
        Check(gm.DeckCards.Contains(a5) && DdDistinct(gm.DeckCards), "H: the swapped deck is saved");

        // ---- F: 前の版の重複入りデッキ(読み込みで最初の1枚だけ残る) ----
        string c2 = DdKey(DdC, 2);
        CardInventory.AddCard(c2, 2, 1);
        var old = new[] { DdA, a3, DdB, DdA, DdC, a5, c2, DdC };
        SaveStore.SetString("DeckCardIds", string.Join(",", old));
        SaveStore.Save();
        string ownedF = DdOwned();
        yield return ReloadHome();
        L($"[F] {string.Join(",", old)} -> {string.Join(",", gm.DeckCards)} (dropped {gm.DeckDuplicatesDropped})");
        Check(gm.DeckCards.SequenceEqual(new[] { DdA, DdB, DdC }), $"F: only the first copy of each card stays ({string.Join(",", gm.DeckCards)})");
        Check(gm.DeckDuplicatesDropped == 5, $"F: 5 duplicates removed ({gm.DeckDuplicatesDropped})");
        Check(DdOwned() == ownedF, "F: no owned card is lost");
        Check(gm.DeckCards.Count == 3, "F: freed slots stay empty (no other card is put in)");
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        Check(NoticeQueue.CurrentId == "deck_duplicates", $"F: the player is told once ({NoticeQueue.CurrentId})");
        yield return new WaitForSecondsRealtime(1.2f); // お知らせが出きってから撮る
        Shot("dd_notice");
        NoticeQueue.DebugAnswer();
        Check(SaveStore.GetString("DeckCardIds", "") == $"{DdA},{DdB},{DdC}", "F: the cleaned deck is saved");

        // ---- K: 読み直しても同じ(2回) ----
        for (int k = 0; k < 2; k++)
        {
            yield return ReloadHome();
            yield return new WaitForSecondsRealtime(1.5f);
            Check(gm.DeckCards.SequenceEqual(new[] { DdA, DdB, DdC }) && gm.DeckDuplicatesDropped == 0 && NoticeQueue.CurrentId != "deck_duplicates",
                $"K: reload {k + 1}: the same deck, nothing removed, no notice ({string.Join(",", gm.DeckCards)})");
        }

        // ---- F2: カード合成改修より前の形式(Lvを持たないデッキ、同じIDを複数) ----
        CardInventory.RemoveCard(a3, 3, CardInventory.GetCount(a3, 3));
        CardInventory.RemoveCard(a5, 5, CardInventory.GetCount(a5, 5));
        var wrap = JsonUtility.FromJson<CardInventory.SaveWrapper>(SaveStore.GetString(CardInventory.SaveKey, ""));
        wrap.stacks.Add(new CardInventory.Stack { cardId = DdA, level = 3, count = 1 }); // 旧形式: (cardId, Lv)
        SaveStore.SetString(CardInventory.SaveKey, JsonUtility.ToJson(wrap));
        SaveStore.SetString("DeckCardIds", $"{DdA},{DdA},{DdB}");
        SaveStore.SetInt(CardDataMigration.FormatKey, 1);
        SaveStore.Save();
        CardInventory.ReloadFromPrefs();
        int totalF2 = TotalOwned();
        yield return ReloadHome();
        L($"[F2] old format deck A,A,B -> {string.Join(",", gm.DeckCards)} owned {DdOwned()}");
        Check(gm.DeckCards.Count == 2 && GameManager.DeckKindOf(gm.DeckCards[0]) == DdA && gm.DeckCards[1] == DdB, $"F2: an old-format deck keeps the first A and B ({string.Join(",", gm.DeckCards)})");
        Check(TotalOwned() == totalF2 && CardInventory.GetCount(a3, 3) == 1, "F2: the converted Lv3 copy stays owned");
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        if (NoticeQueue.Open) NoticeQueue.DebugAnswer();

        // ---- I: ラン中は同じカードを取るたびに Lv が上がる(今まで通り) ----
        gm.SetDeck(new[] { DdA, DdB });
        yield return BeginRun("swordsman", "wasteland_road");
        var pick = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
        int lv0 = gm.GetAbilityRunStack(DdA);
        pick.Invoke(gm, new object[] { DdA });
        pick.Invoke(gm, new object[] { DdA });
        pick.Invoke(gm, new object[] { DdA });
        int lv1 = gm.GetAbilityRunStack(DdA);
        L($"[I] run ability Lv of {DdA}: {lv0} -> {lv1}");
        Check(lv1 == lv0 + 3, $"I: picking the same card 3 times in a run raises its Lv by 3 ({lv0} -> {lv1})");
        Check(gm.DeckCards.SequenceEqual(new[] { DdA, DdB }), "I: the run does not change the deck");
        yield return EndRun();
        yield return ReloadHome(); // EndRun は新しいシーンを待つだけ(gm を取り直す)

        // ---- L: 画面(横) ----
        yield return DeckDupUi();
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // L: 同じカードの別Lvを押した時の案内 / 灰色のチェック / 詳細の [IN DECK: Lv.n]
    IEnumerator DeckDupUi()
    {
        foreach (var id in new[] { DdA, DdB }) if (CardInventory.GetTotalCount(id) < 2) CardInventory.AddCard(id, 1, 2);
        string a3 = DdKey(DdA, 3);
        if (CardInventory.GetCount(a3, 3) == 0) CardInventory.AddCard(a3, 3, 1);
        gm.SetDeck(new[] { DdA, DdB });
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.2f);
        var ui = gm.deckEditUI;
        int shown0 = DeckEditUI.AlreadyInDeckShown;
        bool tapped = ui.DebugTapOwnedKey(a3);
        yield return new WaitForSecondsRealtime(0.25f);
        Check(tapped && DeckEditUI.AlreadyInDeckShown == shown0 + 1 && gm.DeckCards.SequenceEqual(new[] { DdA, DdB }), "L: tapping card A Lv3 while A is in the deck shows the notice and changes nothing");
        string detail = ui.DebugDetailText;
        Check(detail.Contains(Loc.T(Loc.Reviewed(LocPendingReview.DeckAlreadyIn, LocPendingReview.DeckAlreadyInOld))), "L: the 'already in the deck' line is in the detail panel");
        int muted = ui.DebugMutedMarks(), green = ui.DebugGreenMarks();
        Check(muted >= 1 && green >= 2, $"L: the deck copies have a green check, the other level of the same card a grey check (green {green}, grey {muted})");
        Shot("dd_already_in_deck");
        yield return new WaitForSecondsRealtime(0.6f);
        Shot("dd_already_in_deck_after");
        bool again = ui.DebugTapOwnedKey(DdA);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(again && gm.DeckCards.Count == 2, "L: tapping the very same card again does not add it");
        Shot("dd_same_card_again");
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(0.8f);
    }

    // 本当の再起動(専用の保存ファイルだけ)
    IEnumerator DeckDupRestart(string phase)
    {
        string shared = Arg("-ddShared", outDir);
        System.IO.Directory.CreateDirectory(shared);
        string expectPath = System.IO.Path.Combine(shared, "deckdup_expect.txt");
        Check(FileSaveStore.Active, "runs on the dedicated save file (-fileSave), not the normal save");
        if (!FileSaveStore.Active) yield break;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        if (phase == "plant")
        {
            foreach (var id in new[] { DdA, DdA, DdB, DdC }) CardInventory.AddCard(id, 1, 1);
            string a3 = DdKey(DdA, 3), c2 = DdKey(DdC, 2);
            CardInventory.AddCard(a3, 3, 1); CardInventory.AddCard(c2, 2, 1);
            var keep = gm.DeckCards.Where(d => GameManager.DeckKindOf(d) != DdA && GameManager.DeckKindOf(d) != DdB && GameManager.DeckKindOf(d) != DdC).Take(4).ToList();
            var old = new List<string> { DdA, a3, DdB }; old.AddRange(keep); old.AddRange(new[] { DdA, DdC, c2, DdC });
            SaveStore.SetString("DeckCardIds", string.Join(",", old));
            SaveStore.Save();
            var expect = new List<string> { DdA, DdB }; expect.AddRange(keep); expect.Add(DdC);
            System.IO.File.WriteAllText(expectPath, string.Join(",", expect) + "\n" + DdOwned() + "\n" + (old.Count - expect.Count));
            L($"[dd] planted {string.Join(",", old)} (expect {string.Join(",", expect)})");
            Check(true, "plant: an old deck with duplicates is saved");
            yield break;
        }
        var lines = System.IO.File.ReadAllLines(expectPath);
        string deck = string.Join(",", gm.DeckCards);
        L($"[dd] {phase}: deck {deck} dropped {gm.DeckDuplicatesDropped} owned {DdOwned()}");
        Check(deck == lines[0], $"{phase}: the deck is the first copy of each card, in the same order ({deck})");
        Check(DdOwned() == lines[1], $"{phase}: no owned card is lost");
        Check(SaveStore.GetString("DeckCardIds", "") == lines[0], $"{phase}: the saved deck is the cleaned one");
        if (phase == "migrate")
        {
            Check(gm.DeckDuplicatesDropped == int.Parse(lines[2]), $"migrate: {lines[2]} duplicates removed ({gm.DeckDuplicatesDropped})");
            yield return WaitTut(() => NoticeQueue.Open, 6f);
            Check(NoticeQueue.CurrentId == "deck_duplicates", $"migrate: the player is told ({NoticeQueue.CurrentId})");
            yield return new WaitForSecondsRealtime(1.2f); // お知らせが出きってから撮る
        Shot("dd_restart_notice");
            if (NoticeQueue.Open) NoticeQueue.DebugAnswer();
        }
        else
        {
            yield return new WaitForSecondsRealtime(3f);
            Check(gm.DeckDuplicatesDropped == 0 && NoticeQueue.CurrentId != "deck_duplicates", "check: after another restart nothing is removed and no notice is shown");
        }
    }
}
#endif

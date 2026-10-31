#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// デッキ編集で★5が表示されない不具合(2026-11-01)の確認: -qaRare5 <dir> -fileSave <file> [-r5Phase all|plant|check]
//  all   … 所持の束を 160 より多くした状態で、★5 全8枚(#100 ULTIMATE 含む)の 表示(全て/種類)・タップ追加・ドラッグ追加・
//          同じ★5の別Lvの重複禁止・長押しの詳細・分解(空き/使用中)・★1〜★4 の表示・ラン中の効果
//  plant … ★5(Lv1 と Lv3 の合成のキー)をデッキとキャラカード枠に入れて保存して終わる / check … 次の起動で同じ内容が残っている
public partial class QaSweep
{
    static List<CardDefinition> R5Cards() => CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && c.rarity >= 5).OrderBy(c => c.sortOrder).ToList();

    // 合否を1行ずつ残す(報告に全項目の結果を載せるため)
    void R5Check(bool ok, string what) { Check(ok, what); if (ok) L("[ok] " + what); }

    IEnumerator Rare5Mode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1); SaveStore.Save();
        string phase = Arg("-r5Phase", "all");
        var r5 = R5Cards();
        L($"[r5] ★5 cards ({r5.Count}): {string.Join(", ", r5.Select(c => $"{c.cardId}({c.cardName}/{c.category})"))}");
        R5Check(r5.Count == 8, $"there are 8 ★5 cards ({r5.Count})");
        if (phase == "check") { yield return Rare5Check(r5); yield break; }

        // 所持: 全カード Lv1 ×2 と Lv2 ×1(束が 160 を超える = 以前は後ろが出なかった状態)
        CardInventory.DebugOwnEveryAtLevel(1, 2);
        CardInventory.DebugOwnEveryAtLevel(2, 1);
        string r5a = r5[0].cardId, ult = "character_ultimate";
        string r5lv3 = DdKey(r5a, 3);
        if (CardInventory.GetCount(r5lv3, 3) == 0) CardInventory.AddCard(r5lv3, 3, 1);
        int stacks = DeckEditUI.OwnedStacksSorted().Count;
        L($"[r5] owned stacks {stacks}");
        R5Check(stacks > 160, $"the owned stacks exceed the old 160 slots ({stacks})");

        if (phase == "plant")
        {
            gm.SetDeck(new[] { r5lv3, ult, r5[2].cardId });
            gm.EquipCharacterCard(0, r5[3].cardId, 1);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Arg("-r5Shared", outDir), "r5_expect.txt"), string.Join(",", gm.DeckCards) + "|" + gm.CharacterCardIds[0]);
            L($"[r5] planted deck {string.Join(",", gm.DeckCards)} char {gm.CharacterCardIds[0]}");
            yield break;
        }

        gm.SetDeck(new string[0]);
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.4f);
        var ui = gm.deckEditUI;
        R5Check(ui.DebugOwnedShownCount == stacks && ui.DebugOwnedSlotCount >= stacks, $"C: 'ALL' shows every owned stack ({ui.DebugOwnedShownCount}/{stacks}, slots {ui.DebugOwnedSlotCount})");
        // A/B/C: ★1〜★5 の表示(全て)
        for (int r = 1; r <= 4; r++)
        {
            var c = CardDatabase.AllCards.FirstOrDefault(x => !CardVariant.IsVariantKey(x.cardId) && x.rarity == r);
            if (c != null) R5Check(ui.DebugOwnedShows(c.cardId), $"A: ★{r} {c.cardId} is shown in 'ALL'");
        }
        foreach (var c in r5)
        {
            bool fusionBase = DeckEditUI.OwnedStacksSorted().Any(st => st.cardId == c.cardId); // カード合成の一覧の元(同じ条件)
            bool unlocked = GachaStage.IsCardEligible(c, gm.BestDistance, gm.CurrentGachaStage);
            R5Check(ui.DebugOwnedShows(c.cardId), $"B: ★5 {c.cardId} is shown in 'ALL' (owned {CardInventory.GetTotalCountByName(c.cardId)}, gacha-unlocked {unlocked}, in the fusion list {fusionBase})");
        }
        yield return ShotW("r5_all");
        // D: 種類ごと
        foreach (var c in r5)
        {
            ui.DebugSetFilter(c.category.ToString());
            yield return null;
            R5Check(ui.DebugOwnedShows(c.cardId), $"D: ★5 {c.cardId} is shown under its category {c.category}");
        }
        ui.DebugSetFilter(r5[0].category.ToString()); yield return new WaitForSecondsRealtime(0.3f);
        yield return ShotW("r5_category_" + r5[0].category);
        ui.DebugSetFilter("ALL"); yield return new WaitForSecondsRealtime(0.3f);

        // E: タップで追加
        R5Check(ui.DebugTapOwnedKey(r5a) && gm.DeckCards.Contains(r5a), $"E: tapping ★5 {r5a} adds it");
        yield return new WaitForSecondsRealtime(0.3f);
        // M: ULTIMATE(タップ)
        R5Check(ui.DebugTapOwnedKey(ult) && gm.DeckCards.Contains(ult), "M: tapping #100 ULTIMATE adds it");
        // F: ドラッグで追加
        string r5b = r5[1].cardId == ult ? r5[2].cardId : r5[1].cardId;
        int n0 = gm.DeckCards.Count;
        int bi = ui.DebugOwnedIndex(r5b);
        yield return ui.DebugDrag(false, bi, ui.DebugDeckSlotPos(11));
        yield return new WaitForSecondsRealtime(0.3f);
        R5Check(gm.DeckCards.Contains(r5b) && gm.DeckCards.Count == n0 + 1, $"F: dragging ★5 {r5b} onto an empty slot adds it ({DeckEditUI.LastDrop})");
        // G: 同じ★5の別Lv
        int shown0 = DeckEditUI.AlreadyInDeckShown;
        R5Check(ui.DebugTapOwnedKey(r5lv3) && !gm.DeckCards.Contains(r5lv3) && DeckEditUI.AlreadyInDeckShown == shown0 + 1, "G: the same ★5 at another level is refused (duplicate rule)");
        yield return ui.DebugDrag(false, ui.DebugOwnedIndex(r5lv3), ui.DebugDeckSlotPos(11));
        yield return new WaitForSecondsRealtime(0.3f);
        R5Check(!gm.DeckCards.Contains(r5lv3), $"G: dragging the other level is refused too ({DeckEditUI.LastDrop})");
        // H: 長押しの詳細
        ui.DebugLongPress(false, false, ui.DebugOwnedIndex(ult));
        yield return new WaitForSecondsRealtime(0.4f);
        R5Check(ui.BigDetailOpen && ui.DebugBigInfo.Contains("Lv1"), "H: a long press on #100 ULTIMATE opens the large detail");
        yield return ShotW("r5_big_detail_ultimate");
        ui.CloseBigDetail();
        ui.DebugLongPress(false, false, ui.DebugOwnedIndex(r5[4].cardId));
        yield return new WaitForSecondsRealtime(0.3f);
        R5Check(ui.BigDetailOpen, $"H: a long press on ★5 {r5[4].cardId} opens the large detail");
        ui.CloseBigDetail();
        // I: 分解(空きのある★5: 2枚のうち1枚)
        string dis = r5[5].cardId;
        int c0 = CardInventory.GetCount(dis, 1), m0 = gm.TotalOwnedMile;
        bool ok = CardDismantle.Execute(dis, 1, 1, out int got);
        R5Check(ok && CardInventory.GetCount(dis, 1) == c0 - 1 && got == 250 && gm.TotalOwnedMile == m0 + 250, $"I: a free ★5 can be dismantled for 250 MILE ({c0}->{CardInventory.GetCount(dis, 1)}, +{got})");
        // J: 使用中(デッキに入っていて残り1枚 / キャラカード)
        while (CardInventory.GetCount(r5a, 1) > 1) CardInventory.RemoveCard(r5a, 1, 1);
        R5Check(CardDismantle.Check(r5a, 1) == CardDismantle.Block.Equipped && !CardDismantle.Execute(r5a, 1, 1, out _), "J: a ★5 used in the deck cannot be dismantled");
        string chr = r5[6].cardId;
        while (CardInventory.GetCount(chr, 1) > 1) CardInventory.RemoveCard(chr, 1, 1);
        gm.EquipCharacterCard(0, chr, 1);
        R5Check(CardDismantle.Check(chr, 1) == CardDismantle.Block.Equipped, "J: a ★5 equipped as a character card cannot be dismantled");
        gm.EquipCharacterCard(0, null, 1);
        ui.Close();
        yield return new WaitForSecondsRealtime(1.2f);

        // L / M: ラン中の効果(デッキの★5がレベルアップの候補に出て、取ると効く)
        var deck = gm.DeckCards.ToList();
        L($"[r5] deck for the run: {string.Join(",", deck)}");
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(1.5f);
        var apply = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
        int h0 = gm.UpgradeCount;
        foreach (var id in deck) apply.Invoke(gm, new object[] { id });
        yield return new WaitForSeconds(0.8f);
        R5Check(gm.UpgradeCount == h0 + deck.Count, $"L: every ★5 card in the deck applies during a run ({h0} -> {gm.UpgradeCount})");
        float g0 = UltimateArt.Instance != null ? UltimateArt.Instance.Gauge : -1f;
        yield return new WaitForSeconds(3f);
        float g1 = UltimateArt.Instance != null ? UltimateArt.Instance.Gauge : -1f;
        R5Check(UltimateArt.HasCard && UltimateArt.Instance != null && g1 > g0, $"M: after taking #100 ULTIMATE in the run it holds the card (Lv{UltimateArt.Level}) and its gauge fills ({g0:0.000} -> {g1:0.000})");
        yield return ShotW("r5_run");
        yield return EndRun();
    }

    IEnumerator Rare5Check(List<CardDefinition> r5)
    {
        string p = System.IO.Path.Combine(Arg("-r5Shared", outDir), "r5_expect.txt");
        R5Check(System.IO.File.Exists(p), "K: the planted deck file exists");
        if (!System.IO.File.Exists(p)) yield break;
        var parts = System.IO.File.ReadAllText(p).Split('|');
        string now = string.Join(",", gm.DeckCards) + "|" + gm.CharacterCardIds[0];
        L($"[r5] after restart: {now} / planted {string.Join("|", parts)}");
        R5Check(now == string.Join("|", parts), "K: after a restart, the ★5 deck (incl. a Lv3 fused key and ULTIMATE) and the character card are unchanged");
        R5Check(gm.DeckDuplicatesDropped == 0 && gm.DeckOverflowDropped == 0, "K: the duplicate-ban migration removed nothing");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.4f);
        yield return ShotW("r5_after_restart");
        gm.deckEditUI.Close();
        yield return new WaitForSecondsRealtime(1.0f);
    }
}
#endif

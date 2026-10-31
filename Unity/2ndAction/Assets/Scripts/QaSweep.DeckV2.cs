#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// デッキ編集の操作の仕上げ / カードの分解(2026-10-31)の自動テスト: -qaDeckV2 <dir>(テスト用データ SaveProfile の中だけで行う)
//  H 配置(デッキ 6×2・キャラカード1行・一覧は下・小さな詳細・畳む)
//  I タップで入れる/外す(今までどおり)
//  J 所持 → 空き枠へドラッグ = 入れる / K 埋まった枠 = 確認して入れ替え(いいえなら何も変わらない)
//  L 同じカードを別の枠へ = 入れない(案内・光る) / M デッキ → 一覧 = 外す / N デッキの中で並び替え
//  O 長押しの大きな詳細(所持/デッキ/キャラカード)と中身
//  P〜X 分解(空き/装備中/最後の1枚/高いレア度/払い戻しの表/すぐに保存/MAX の記録/ガチャとの往復で増えない/キャラカード)
public partial class QaSweep
{
    IEnumerator DeckV2Mode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        SaveStore.Save();
        bool portrait = Screen.height > Screen.width;
        string tag = portrait ? "pt" : "ls";

        // ---- 用意: 所持カード(★1 の空き2枚 / 最後の1枚 / ★4以上 / Lv9)
        var bases = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && c.cardId != "character_ultimate").ToList();
        foreach (var c in bases.Take(16)) if (CardInventory.GetTotalCount(c.cardId) < 2) CardInventory.AddCard(c.cardId, 1, 2);
        var deck0 = bases.Take(10).Select(c => c.cardId).ToList();
        gm.SetDeck(deck0);
        string free1 = bases.Skip(12).First(c => c.rarity <= 2).cardId;          // 空きの ★1/★2(2枚)
        var rareDef = bases.Where(c => c.rarity >= 4).Skip(1).FirstOrDefault() ?? bases.Last();
        string rare = rareDef.cardId;
        while (CardInventory.GetTotalCountByName(rare) > 0) { var s = CardInventory.Stacks.First(x => CardMastery.BaseIdOf(x.cardId) == rare); CardInventory.RemoveCard(s.cardId, s.level, s.count); }
        CardInventory.AddCard(rare, 1, 1);                                         // 最後の1枚の ★4+
        var lastDef = bases.Skip(17).First(c => c.rarity <= 2 && c.cardId != free1);
        string last = lastDef.cardId;
        while (CardInventory.GetTotalCountByName(last) > 0) { var s = CardInventory.Stacks.First(x => CardMastery.BaseIdOf(x.cardId) == last); CardInventory.RemoveCard(s.cardId, s.level, s.count); }
        CardInventory.AddCard(last, 1, 1);                                         // 最後の1枚(低いレア度)
        string lv9Base = bases.Skip(20).First().cardId;
        string lv9 = DdKey(lv9Base, 9);
        if (CardInventory.GetCount(lv9, 9) == 0) CardInventory.AddCard(lv9, 9, 1);
        gm.AddMile(0);
        L($"[setup] deck {gm.DeckCards.Count}, free1 {free1}, rare {rare} ★{rareDef.rarity}, last {last}, lv9 {lv9}");

        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.4f);
        var ui = gm.deckEditUI;
        if (Arg("-dvPhase", "all") == "real") { yield return DeckV2Real(ui, free1); yield break; }

        // ---- H 配置
        var slotPos = Enumerable.Range(0, 12).Select(ui.DebugDeckSlotPos).ToList();
        int rows = slotPos.Select(p => Mathf.RoundToInt(p.y / 4f)).Distinct().Count();
        int cols = slotPos.Select(p => Mathf.RoundToInt(p.x / 4f)).Distinct().Count();
        Check(rows == 2 && cols == 6, $"H: the deck is 6 columns x 2 rows (rows {rows}, cols {cols})");
        var charPos = Enumerable.Range(0, 3).Select(ui.DebugCharSlotPos).ToList();
        Check(charPos.Select(p => Mathf.RoundToInt(p.y / 4f)).Distinct().Count() == 1, "H: the 3 character cards are in one row");
        Rect deckR = ScreenRectOf(ui.DebugDeckPanel), ownR = ScreenRectOf(ui.DebugOwnedViewport), detR = ScreenRectOf(ui.DebugDetailPanel);
        Check(ownR.yMax <= deckR.yMin + 2f, $"H: the owned list is below the deck (deck {deckR}, list {ownR})");
        Check(!detR.Overlaps(ScreenRectOf(ui.DebugDeckSlotRect(0))) && !detR.Overlaps(ScreenRectOf(ui.DebugDeckSlotRect(11))), "H: the small detail does not cover the deck");
        Check(charPos.All(p => p.y > ownR.yMax), "H: the character cards are in the upper part");
        bool inScreen = slotPos.All(p => p.x > 0 && p.x < Screen.width && p.y > 0 && p.y < Screen.height) && charPos.All(p => p.x > 0 && p.x < Screen.width);
        Check(inScreen, "H: every deck slot and character card is on screen");
        yield return null;
        yield return ShotW($"dv_{tag}_layout");
        float ownH0 = ownR.height, ownW0 = ownR.width;
        ui.DebugSetDetailCollapsed(true);
        yield return new WaitForSecondsRealtime(0.4f);
        Rect ownR2 = ScreenRectOf(ui.DebugOwnedViewport);
        Check(portrait ? ownR2.height > ownH0 + 20f : ownR2.width > ownW0 + 20f, $"H: folding the small detail gives the list more room ({ownW0:F0}x{ownH0:F0} -> {ownR2.width:F0}x{ownR2.height:F0})");
        yield return ShotW($"dv_{tag}_detail_folded");
        ui.DebugSetDetailCollapsed(false);
        yield return new WaitForSecondsRealtime(0.4f);

        // ---- I タップ(今までどおり)
        int n0 = gm.DeckCards.Count;
        Check(ui.DebugTapOwnedKey(free1) && gm.DeckCards.Count == n0 + 1 && gm.DeckCards.Last() == free1, "I: tapping an owned card still adds it to the deck");
        yield return new WaitForSecondsRealtime(0.3f);
        yield return ShotW($"dv_{tag}_tap_added");
        gm.RemoveFromDeck(free1);
        yield return new WaitForSecondsRealtime(0.2f);

        // ---- J ドラッグ: 所持 → 空き枠
        int fi = ui.DebugOwnedIndex(free1);
        n0 = gm.DeckCards.Count;
        yield return ui.DebugDrag(false, fi, ui.DebugDeckSlotPos(11), hold: 0.3f, whileHeld: () => Shot($"dv_{tag}_drag_add"));
        yield return new WaitForSecondsRealtime(0.3f);
        Check(gm.DeckCards.Count == n0 + 1 && gm.DeckCards.Contains(free1) && !ui.DebugGhostShown, $"J: dragging an owned card onto an empty slot adds it ({DeckEditUI.LastDrop})");

        // ---- K 埋まった枠: 確認して入れ替え(いいえ / はい)
        string kNew = bases.Skip(13).First().cardId;
        string slot2 = gm.DeckCards[2];
        int ki = ui.DebugOwnedIndex(kNew);
        yield return ui.DebugDrag(false, ki, ui.DebugDeckSlotPos(2));
        yield return new WaitForSecondsRealtime(0.3f);
        Check(ui.confirmDialog.IsOpen && gm.DeckCards[2] == slot2, $"K: dropping onto a filled slot asks first and does not replace yet ({DeckEditUI.LastDrop})");
        yield return ShotW($"dv_{tag}_replace_confirm");
        ui.HandleBack(); // いいえ
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!ui.confirmDialog.IsOpen && gm.DeckCards[2] == slot2, "K: answering No keeps the deck as it was");
        yield return ui.DebugDrag(false, ui.DebugOwnedIndex(kNew), ui.DebugDeckSlotPos(2));
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugConfirmYes();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(gm.DeckCards[2] == kNew && !gm.DeckCards.Contains(slot2) && gm.DeckCards.Count == n0 + 1, $"K: answering Yes replaces that slot only ({string.Join(",", gm.DeckCards)})");

        // ---- L 同じカード(Lv違い)を空き枠へ: 入れない、案内、光る
        string sameLv3 = DdKey(gm.DeckCards[0], 3);
        if (CardInventory.GetCount(sameLv3, 3) == 0) CardInventory.AddCard(sameLv3, 3, 1);
        ui.RefreshAfterPresetChange();
        yield return new WaitForSecondsRealtime(0.2f);
        var deckL = gm.DeckCards.ToList();
        int shown0 = DeckEditUI.AlreadyInDeckShown;
        string hover = "";
        yield return ui.DebugDrag(false, ui.DebugOwnedIndex(sameLv3), ui.DebugDeckSlotPos(11), hold: 0.25f, whileHeld: () => { hover = ui.DebugHoverState; Shot($"dv_{tag}_drag_duplicate"); });
        yield return new WaitForSecondsRealtime(0.2f);
        Check(hover.EndsWith("Duplicate") && gm.DeckCards.SequenceEqual(deckL) && DeckEditUI.AlreadyInDeckShown == shown0 + 1
              && DeckEditUI.LastToast == Loc.T(Loc.Reviewed(LocPendingReview.DeckAlreadyIn, LocPendingReview.DeckAlreadyInOld)),
              $"L: the same card (other Lv) is refused with the notice ({hover}, toast '{DeckEditUI.LastToast}')");
        yield return ShotW($"dv_{tag}_duplicate_toast");

        // ---- M デッキ → 一覧 = 外す
        string m = gm.DeckCards[1];
        yield return ui.DebugDrag(true, 1, ui.DebugOwnedAreaPos, hold: 0.25f, whileHeld: () => Shot($"dv_{tag}_drag_remove"));
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!gm.DeckCards.Contains(m) && gm.DeckCards.Count == deckL.Count - 1, $"M: dragging a deck card onto the list removes it ({DeckEditUI.LastDrop})");

        // ---- N デッキの中で並び替え(入れ替え / 空き枠へは末尾へ)
        var before = gm.DeckCards.ToList();
        yield return ui.DebugDrag(true, 0, ui.DebugDeckSlotPos(3));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(gm.DeckCards[0] == before[3] && gm.DeckCards[3] == before[0] && gm.DeckCards.Count == before.Count, $"N: dragging deck slot 1 onto slot 4 swaps them ({DeckEditUI.LastDrop})");
        before = gm.DeckCards.ToList();
        yield return ui.DebugDrag(true, 0, ui.DebugDeckSlotPos(11));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(gm.DeckCards.Last() == before[0] && gm.DeckCards.Count == before.Count, $"N: dragging onto an empty slot moves the card to the end ({DeckEditUI.LastDrop})");

        // ---- O 長押しの大きな詳細
        ui.DebugLongPress(false, false, ui.DebugOwnedIndex(free1));
        yield return new WaitForSecondsRealtime(0.4f);
        string info = ui.DebugBigInfo;
        Check(ui.BigDetailOpen, "O: a long press on an owned card opens the large detail");
        foreach (var label in new[] { Loc.T(LocDeckEdit.LvEffects), "Lv1", "Lv9", "COMBO", "FINAL EVOLUTION", Loc.T(LocDeckEdit.StateHead), Loc.T(LocDeckEdit.UnlockHead) })
            Check(info.Contains(label.Split('(')[0]), $"O: the large detail shows '{label}'");
        yield return ShotW($"dv_{tag}_big_detail_owned");
        ui.HandleBack();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!ui.BigDetailOpen && ui.root.activeInHierarchy, "O: Back closes only the large detail");
        ui.DebugLongPress(true, false, 0);
        yield return new WaitForSecondsRealtime(0.4f);
        Check(ui.BigDetailOpen && ui.DebugBigInfo.Contains(Loc.T(LocDeckEdit.StateInDeck)), "O: a long press on a deck card opens the detail (shows 'in the deck')");
        yield return ShotW($"dv_{tag}_big_detail_deck");
        ui.CloseBigDetail();
        gm.EquipCharacterCard(0, free1, 1);
        ui.RefreshAfterPresetChange();
        ui.DebugLongPress(false, true, 0);
        yield return new WaitForSecondsRealtime(0.4f);
        Check(ui.BigDetailOpen && ui.DebugBigInfo.Contains(Loc.T(LocDeckEdit.StateEquipped)), "O: a long press on a character card opens the detail (shows 'equipped')");
        ui.CloseBigDetail();

        // ---- P 分解(空きのある★1/★2)
        gm.EquipCharacterCard(0, null, 1);
        if (gm.DeckCards.Contains(free1)) gm.RemoveFromDeck(free1);
        ui.RefreshAfterPresetChange();
        int c0 = CardInventory.GetCount(free1, 1), mile0 = gm.TotalOwnedMile;
        int expect = CardDismantle.RefundFor(free1, 1);
        ui.DebugOpenBigDetailOwned(free1);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return ShotW($"dv_{tag}_dismantle_button");
        ui.DebugDismantleTap();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(ui.confirmDialog.IsOpen && !ui.confirmDialog.Danger, "P: dismantling asks for confirmation (normal warning for a common, non-last card)");
        yield return ShotW($"dv_{tag}_dismantle_confirm");
        ui.DebugConfirmYes();
        yield return new WaitForSecondsRealtime(0.4f);
        Check(CardInventory.GetCount(free1, 1) == c0 - 1 && gm.TotalOwnedMile == mile0 + expect, $"P: count -1 and MILE +{expect} ({c0}->{CardInventory.GetCount(free1, 1)}, {mile0}->{gm.TotalOwnedMile})");
        Check(CardDismantle.LastLog.Contains(free1) && CardDismantle.LastLog.Contains("MILE +" + expect), $"P: debug log has the card, count and MILE ({CardDismantle.LastLog})");
        Check(DeckEditUI.LastToast == Loc.F(LocDeckEdit.DismantleDone, expect), "P: the result is shown");
        yield return ShotW($"dv_{tag}_dismantle_done");
        // U すぐに保存(読み直しても同じ)
        CardInventory.ReloadFromPrefs();
        Check(CardInventory.GetCount(free1, 1) == c0 - 1 && SaveStore.GetInt(GameManager.TotalMileKey, -1) == gm.TotalOwnedMile, "U: the inventory and MILE were saved right away (re-read from the save)");
        ui.CloseBigDetail();

        // ---- Q 装備中(デッキに1枚だけ)は分解しない
        string inDeck = gm.DeckCards[0];
        int qc = CardInventory.GetCount(inDeck, 1);
        while (CardInventory.GetCount(inDeck, 1) > 1) CardInventory.RemoveCard(inDeck, 1, 1);
        var deckQ = gm.DeckCards.ToList(); int mileQ = gm.TotalOwnedMile;
        ui.RefreshAfterPresetChange();
        ui.DebugOpenBigDetailOwned(inDeck);
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugDismantleTap();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!ui.confirmDialog.IsOpen && CardInventory.GetCount(inDeck, 1) == 1 && gm.DeckCards.SequenceEqual(deckQ) && gm.TotalOwnedMile == mileQ
              && DeckEditUI.LastToast == Loc.T(LocDeckEdit.DismantleEquipped), "Q: a card used in the deck cannot be dismantled, the deck is not changed, and the reason is shown");
        yield return ShotW($"dv_{tag}_dismantle_equipped");
        ui.CloseBigDetail();

        // ---- X キャラカードに装備中も同じ
        while (CardInventory.GetCount(free1, 1) > 1) CardInventory.RemoveCard(free1, 1, 1);
        gm.EquipCharacterCard(0, free1, 1);
        ui.RefreshAfterPresetChange();
        Check(CardDismantle.Check(free1, 1) == CardDismantle.Block.Equipped, "X: a card equipped as a character card cannot be dismantled");
        Check(!CardDismantle.Execute(free1, 1, 1, out _) && CardInventory.GetCount(free1, 1) == 1 && gm.CharacterCardIds[0] == free1, "X: even a direct request is refused and the slot stays");
        gm.EquipCharacterCard(0, null, 1);

        // ---- R 最後の1枚(強い確認)
        ui.RefreshAfterPresetChange();
        ui.DebugOpenBigDetailOwned(last);
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugDismantleTap();
        yield return new WaitForSecondsRealtime(0.3f);
        string msgR = ui.confirmDialog.messageText != null ? ui.confirmDialog.messageText.text : "";
        Check(ui.confirmDialog.IsOpen && ui.confirmDialog.Danger && msgR.Contains(Loc.T(LocDeckEdit.DismantleLast).Substring(0, 6)), "R: the last copy shows the strong warning");
        yield return ShotW($"dv_{tag}_dismantle_last");
        ui.DebugConfirmYes();
        yield return new WaitForSecondsRealtime(0.4f);
        Check(CardInventory.GetTotalCountByName(last) == 0 && !ui.BigDetailOpen && ui.DebugOwnedIndex(last) < 0, "R: the last copy can be dismantled; it leaves the list and the detail closes");

        // ---- S 高いレア度
        ui.DebugOpenBigDetailOwned(rare);
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugDismantleTap();
        yield return new WaitForSecondsRealtime(0.3f);
        string msgS = ui.confirmDialog.messageText != null ? ui.confirmDialog.messageText.text : "";
        Check(ui.confirmDialog.IsOpen && ui.confirmDialog.Danger && msgS.Contains("★" + rareDef.rarity), $"S: a high-rarity card (★{rareDef.rarity}) shows the rarity warning");
        yield return ShotW($"dv_{tag}_dismantle_rare");
        ui.HandleBack();
        yield return new WaitForSecondsRealtime(0.2f);
        Check(CardInventory.GetTotalCountByName(rare) == 1, "S: answering No keeps the card");
        ui.CloseBigDetail();

        // ---- T 払い戻しの表 / W ガチャとの往復で増えない
        for (int r = 1; r <= 5; r++)
        {
            var def = bases.FirstOrDefault(c => c.rarity == r);
            if (def == null) continue;
            int lv1 = CardDismantle.RefundFor(def.cardId, 1);
            Check(lv1 == r * 50 && lv1 < GameManager.GachaCostMile, $"T: ★{r} Lv1 refunds {lv1} MILE (< {GameManager.GachaCostMile})");
        }
        int worst = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId)).Max(c => CardDismantle.RefundFor(c.cardId, 1));
        Check(worst < GameManager.GachaCostMile, $"W: no Lv1 card refunds as much as a pull ({worst} < {GameManager.GachaCostMile})");
        gm.AddMile(GameManager.GachaCostMile * 2);
        int mw0 = gm.TotalOwnedMile;
        var stacks0 = CardInventory.Stacks.ToDictionary(s => s.cardId + "@" + s.level, s => s.count);
        typeof(GameManager).GetMethod("GachaPullOnce", NP).Invoke(gm, null);
        var got = CardInventory.Stacks.FirstOrDefault(s => !stacks0.TryGetValue(s.cardId + "@" + s.level, out int n) || n < s.count);
        bool dis = got != null && CardDismantle.Execute(got.cardId, got.level, 1, out int back);
        Check(got != null && dis && gm.TotalOwnedMile < mw0, $"W: pull ({GameManager.GachaCostMile}) then dismantle the drawn card ends with less MILE ({mw0} -> {gm.TotalOwnedMile}, card {got?.cardId})");

        // ---- V Lv9 を分解しても MAX の記録は減らない
        int max0 = CardMastery.MaxCount;
        bool v9 = CardDismantle.Execute(lv9, 9, 1, out int mile9);
        Check(v9 && mile9 == CardDismantle.RarityOf(lv9) * 9 * 50 && CardMastery.MaxCount == max0 && CardMastery.IsMaxReached(lv9Base),
              $"V: dismantling a Lv9 card refunds ★×9×50 ({mile9}) and keeps its MAX record ({max0} -> {CardMastery.MaxCount})");

        ui.RefreshAfterPresetChange();
        yield return new WaitForSecondsRealtime(0.3f);
        yield return ShotW($"dv_{tag}_end");
        ui.Close();
        yield return new WaitForSecondsRealtime(1.0f);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // 本物の指/マウス(外から: Windows = OS のマウス入力、Android = adb の input motionevent)。
    //  置き場所を pos.txt(窓の左上 0,0 からの割合)へ書き、外の操作が done.txt を書くまで待って結果を確かめる
    //  操作の順: 1 所持(free1)→ 空き枠12 / 2 デッキ2枚目 → 一覧 / 3 デッキ1枚目 → 4枚目 / 4 所持(free1 以外)を長押し → 戻る / 5 所持を1回タップ
    IEnumerator DeckV2Real(DeckEditUI ui, string free1)
    {
        var deck0 = gm.DeckCards.ToList();
        string tapKey = displayedKeyNotInDeck(ui, free1);
        System.Func<Vector2, string> N = p => $"{p.x / Screen.width:F4},{1f - p.y / Screen.height:F4}";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("own_free " + N(ui.DebugOwnedPos(ui.DebugOwnedIndex(free1))));
        sb.AppendLine("own_tap " + N(ui.DebugOwnedPos(ui.DebugOwnedIndex(tapKey))));
        sb.AppendLine("slot0 " + N(ui.DebugDeckSlotPos(0)));
        sb.AppendLine("slot1 " + N(ui.DebugDeckSlotPos(1)));
        sb.AppendLine("slot3 " + N(ui.DebugDeckSlotPos(3)));
        sb.AppendLine("slot11 " + N(ui.DebugDeckSlotPos(11)));
        sb.AppendLine("ownarea " + N(ui.DebugOwnedAreaPos));
        string posPath = System.IO.Path.Combine(outDir, "pos.txt"), donePath = System.IO.Path.Combine(outDir, "done.txt");
        if (System.IO.File.Exists(donePath)) System.IO.File.Delete(donePath);
        System.IO.File.WriteAllText(posPath, sb.ToString());
        L("[real] positions written, waiting for the external input\n" + sb);
        yield return ShotW("dvr_start");
        int drags0 = DeckEditUI.DragsStarted, big0 = DeckEditUI.BigDetailOpened;
        float w = 0f;
        while (!System.IO.File.Exists(donePath) && w < 240f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        var d = gm.DeckCards.ToList();
        L($"[real] deck {string.Join(",", deck0)} -> {string.Join(",", d)}; drags {DeckEditUI.DragsStarted - drags0}, big {DeckEditUI.BigDetailOpened - big0}, last drop {DeckEditUI.LastDrop}");
        Check(System.IO.File.Exists(donePath), "real: the external input finished");
        Check(DeckEditUI.DragsStarted - drags0 >= 3, $"real: 3 drags were recognized ({DeckEditUI.DragsStarted - drags0})");
        Check(d.Contains(free1), "real 1: the owned card dragged onto an empty slot is in the deck");
        Check(!d.Contains(deck0[1]), "real 2: the deck card dragged onto the list was removed");
        // 2 で2枚目を外した後なので、4枚目の枠には元の5枚目がいる
        Check(d.IndexOf(deck0[0]) == 3 && d.IndexOf(deck0[4]) == 0, $"real 3: deck card 1 and the card in slot 4 swapped places ({d.IndexOf(deck0[0])}, {d.IndexOf(deck0[4])})");
        Check(DeckEditUI.BigDetailOpened - big0 >= 1 && !ui.BigDetailOpen, "real 4: the long press opened the large detail and Back/Esc closed it");
        Check(d.Contains(tapKey), "real 5: a plain tap/click still adds the card");
        Shot("dvr_end");
        ui.Close();
        yield return new WaitForSecondsRealtime(1.0f);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // 撮影は描画の終わりに行われるので、撮った直後の操作が写らないよう 2 フレーム待つ
    IEnumerator ShotW(string name) { Shot(name); yield return null; yield return null; }

    static string displayedKeyNotInDeck(DeckEditUI ui, string exclude)
    {
        var gm = GameManager.Instance;
        foreach (var s in CardInventory.Stacks)
            if (s.cardId != exclude && s.level == 1 && !CardVariant.IsVariantKey(s.cardId) && !gm.DeckHasKind(s.cardId) && ui.DebugOwnedIndex(s.cardId) >= 0 && ui.DebugOwnedIndex(s.cardId) < 6) return s.cardId;
        return exclude;
    }
}
#endif

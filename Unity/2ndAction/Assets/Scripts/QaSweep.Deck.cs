#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// デッキ12枚(2026-10-07)の自動テスト: -qaDeck <dir>(テスト用データの中で行う)
//  N 新規データ: 初期デッキ12枚 / 編集画面の枠12 / 12枚まで入る、13枚目は入らない / 保存→読み直しで同じ
//  O 旧データ(10枚): そのまま10枚で読む、カードは消えない、ホームで1回だけ「12枚になりました」
//  X 旧データ(14枚、上限超え): 12枚で読み、外した2枚は所持のまま、案内が出る、保存し直される
public partial class QaSweep
{
    IEnumerator DeckModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();

        L("== N: new data ==");
        L($"[N] deck {gm.DeckCards.Count}: {string.Join(",", gm.DeckCards)}");
        Check(GameManager.DeckCapacity == 12 && gm.DeckCards.Count == 12, $"N: a new player's starting deck has 12 cards ({gm.DeckCards.Count})");
        Check(gm.DeckCards.All(id => CardInventory.GetTotalCount(id) >= gm.DeckCards.Count(d => d == id)), "N: every starting deck card is owned");
        Check(NoticeQueue.Pending == 0 && !NoticeQueue.Open, "N: no deck notice for new data");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.2f);
        var ui = gm.deckEditUI;
        int slots = ui.deckSlotCards.Length, active = ui.deckSlotCards.Count(c => c != null && c.gameObject.activeInHierarchy);
        Check(slots == 12 && active == 12, $"N: deck edit shows 12 slots ({slots} slots, {active} active)");
        // 全スロットが画面内(重なりなし)
        var rects = ui.deckSlotCards.Select(c => RectTransformUtility.WorldToScreenPoint(null, c.rect.position)).ToList();
        int overlaps = 0;
        for (int i = 0; i < rects.Count; i++) for (int j = i + 1; j < rects.Count; j++) if ((rects[i] - rects[j]).sqrMagnitude < 4f) overlaps++;
        Check(overlaps == 0, $"N: deck slots do not overlap ({overlaps})");
        Shot("deck_edit_12");
        // スクロールして下の段も撮る
        if (ui.deckScrollRect != null) { ui.deckScrollRect.verticalNormalizedPosition = 0f; yield return new WaitForSecondsRealtime(0.3f); Shot("deck_edit_12_bottom"); }
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(1f);
        // 13枚目は入らない、外して入れ直せる
        var owned = CardInventory.Stacks.Select(s => s.cardId).Distinct().ToList();
        string extra = owned.FirstOrDefault(id => gm.GetTotalUsedCount(id) < CardInventory.GetTotalCount(id));
        if (extra == null) { CardInventory.AddCard("attack_up", 1, 1); extra = "attack_up"; }
        Check(!gm.AddToDeck(extra), "N: a 13th card cannot be added");
        string first = gm.DeckCards[0];
        Check(gm.RemoveFromDeck(first) && gm.DeckCards.Count == 11 && gm.AddToDeck(first) && gm.DeckCards.Count == 12, "N: remove/add back keeps 12");
        string saved = SaveStore.GetString("DeckCardIds", "");
        yield return ReloadHome();
        Check(string.Join(",", gm.DeckCards) == saved && gm.DeckCards.Count == 12, "N: 12-card deck saved and reloaded identically");

        L("== O: old 10-card deck ==");
        var ten = gm.DeckCards.Take(10).ToList();
        SaveStore.SetString("DeckCardIds", string.Join(",", ten));
        SaveStore.DeleteKey(DeckCapacityNotice.Key);
        SaveStore.Save();
        int ownedTotal = TotalOwned();
        yield return ReloadHome();
        Check(gm.DeckCards.Count == 10 && string.Join(",", gm.DeckCards) == string.Join(",", ten), "O: an old 10-card deck loads unchanged (no auto-fill)");
        Check(TotalOwned() == ownedTotal, "O: no owned card lost");
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        Check(NoticeQueue.CurrentId == "deck12", $"O: home shows the 'deck is now 12' notice ({NoticeQueue.CurrentId})");
        Shot("deck_notice_12");
        NoticeQueue.DebugAnswer();
        yield return ReloadHome();
        yield return new WaitForSecondsRealtime(2f);
        Check(!NoticeQueue.Open && NoticeQueue.Pending == 0, "O: the notice is shown only once");

        L("== X: over the limit (14) ==");
        foreach (var id in new[] { "attack_up", "heart_up", "speed_up", "attack_up" }) CardInventory.AddCard(id, 1, 1);
        var fourteen = gm.DeckCards.Concat(new[] { "attack_up", "heart_up", "speed_up", "attack_up" }).Take(14).ToList();
        SaveStore.SetString("DeckCardIds", string.Join(",", fourteen));
        SaveStore.Save();
        ownedTotal = TotalOwned();
        yield return ReloadHome();
        Check(gm.DeckCards.Count == 12 && gm.DeckOverflowDropped == fourteen.Count - 12, $"X: loads the first 12, drops {fourteen.Count - 12} ({gm.DeckCards.Count}, dropped {gm.DeckOverflowDropped})");
        Check(TotalOwned() == ownedTotal, "X: the dropped cards stay owned");
        Check(SaveStore.GetString("DeckCardIds", "").Split(',').Length == 12, "X: the trimmed deck is saved");
        yield return WaitTut(() => NoticeQueue.Open, 5f);
        Check(NoticeQueue.CurrentId == "deck_overflow", $"X: the player is told to re-edit ({NoticeQueue.CurrentId})");
        NoticeQueue.DebugAnswer();
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

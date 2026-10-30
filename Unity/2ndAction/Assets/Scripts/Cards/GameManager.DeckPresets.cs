using System.Collections.Generic;
using UnityEngine;

// デッキのプリセット(2026-10-31)の切り替え/コピー/初期化。データは DeckPresets、使用中のデッキは deckCards。
//  ・切り替えはホームでだけ(ランの間・マルチの疾走の間はできない)。マルチは出発前(部屋の画面)で選ぶ。
//  ・切り替えた時、プリセットの中に「今は持っていない/空きの無い」カードがあれば、同じカードの別の Lv(一番高い物)に替え、
//    それも無ければ外す(外した数は案内に出す)。重複禁止・12 枚の上限は AddToDeck/SetDeck と同じ。
public partial class GameManager
{
    // 疾走の間(ソロ/マルチ)とマルチのランの間は切り替えない
    public bool CanChangeDeckNow => !HasStarted && !NetRunLauncher.IsMultiplayerRun;

    public int LastPresetDropped { get; private set; }  // 確認用: 最後の切り替えで外した枚数
    public int LastPresetSwapped { get; private set; }  // 確認用: 最後の切り替えで別の Lv に替えた枚数

    // 戻り値: 切り替えたら true(同じ番号/切り替えできない時は false)
    public bool ActivatePreset(int index)
    {
        if (!CanChangeDeckNow || !DeckPresets.Loaded) return false;
        var p = DeckPresets.Get(index);
        if (p == null || index == DeckPresets.Active) return false;
        var list = ResolvePresetCards(p.cards, out int dropped, out int swapped);
        LastPresetDropped = dropped; LastPresetSwapped = swapped;
        DeckPresets.SetActiveIndex(index);
        deckCards.Clear();
        deckCards.AddRange(list);
        SaveDeck(); // 使用中の番号・プリセットの中身・DeckCardIds を1回の保存で
        Debug.Log($"[DeckPreset] active -> {index + 1} ({deckCards.Count} cards, dropped {dropped}, other level {swapped})");
        if (dropped > 0) NoticeQueue.Toast(Loc.Shape(Loc.Plain(LocPendingReview.PresetDroppedToast).Replace("{0}", dropped.ToString())));
        return true;
    }

    public bool CopyPreset(int from, int to)
    {
        if (!CanChangeDeckNow || !DeckPresets.Copy(from, to)) return false;
        if (to == DeckPresets.Active) ApplyActivePresetCards();
        Debug.Log($"[DeckPreset] copied {from + 1} -> {to + 1}");
        return true;
    }

    public bool ClearPreset(int index)
    {
        if (!CanChangeDeckNow || !DeckPresets.Clear(index)) return false;
        if (index == DeckPresets.Active) { deckCards.Clear(); SaveDeck(); }
        Debug.Log($"[DeckPreset] cleared {index + 1}");
        return true;
    }

    public bool RenamePreset(int index, string name) => CanChangeDeckNow && DeckPresets.Rename(index, name);

    void ApplyActivePresetCards()
    {
        var p = DeckPresets.Get(DeckPresets.Active);
        if (p == null) return;
        var list = ResolvePresetCards(p.cards, out int dropped, out int swapped);
        LastPresetDropped = dropped; LastPresetSwapped = swapped;
        deckCards.Clear();
        deckCards.AddRange(list);
        SaveDeck();
    }

    // マップ/モードを選んだ時: そこで前回使ったプリセットへ(中身は書き換えない)。stageId が空ならモードだけで探す
    public void ApplyRememberedPreset(string stageId)
    {
        if (!CanChangeDeckNow || !DeckPresets.Loaded) return;
        int r = DeckPresets.Remembered(DeckPresets.ModeNow(), stageId);
        if (r < 0 || r == DeckPresets.Active) return;
        if (ActivatePreset(r)) NoticeQueue.Toast(Loc.Shape(Loc.Plain(LocPendingReview.PresetAppliedToast).Replace("{0}", DeckPresets.DisplayName(r))));
    }

    List<string> ResolvePresetCards(List<string> src, out int dropped, out int swapped)
    {
        var picked = new List<string>();
        var kinds = new HashSet<string>();
        dropped = swapped = 0;
        foreach (string key in src)
        {
            string kind = DeckKindOf(key);
            if (string.IsNullOrEmpty(kind) || kinds.Contains(kind) || picked.Count >= DeckCapacity) { dropped++; continue; }
            string use = CardDatabase.FindById(key) != null && HasFreeCopyFor(key, picked) ? key : null;
            if (use == null) { use = BestFreeOfKind(kind, picked); if (use != null) swapped++; }
            if (use == null) { dropped++; continue; }
            kinds.Add(kind);
            picked.Add(use);
        }
        return picked;
    }

    bool HasFreeCopyFor(string key, List<string> picked)
    {
        int inDeck = 0;
        foreach (var k in picked) if (k == key) inDeck++;
        return CardInventory.GetTotalCount(key) > inDeck + MaxCharacterSlotUses(key, -1);
    }

    string BestFreeOfKind(string kind, List<string> picked)
    {
        string best = null; int bestLv = -1;
        foreach (CardInventory.Stack s in CardInventory.Stacks)
        {
            if (s == null || s.count <= 0 || DeckKindOf(s.cardId) != kind || CardDatabase.FindById(s.cardId) == null) continue;
            if (!HasFreeCopyFor(s.cardId, picked)) continue;
            if (s.level > bestLv) { best = s.cardId; bestLv = s.level; }
        }
        return best;
    }
}

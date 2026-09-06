using System;
using System.Collections.Generic;
using UnityEngine;

// Reward/Card Ownership/Gacha/Fusion System Ver.1 - real owned-copy
// inventory, separate from UnlockManager (distance-based Deck/LevelUp
// unlock flags - see its own class comment) and separate from
// GameManager.DeckCards (the existing Deck pool, unchanged mechanic).
// Cards obtained via Gacha/Fusion live here as "stacks" keyed by
// (cardId, level) - e.g. owning Speed Up x5 at Lv.1 and Speed Up x1 at
// Lv.2 (after fusing two Lv.1 copies) are two separate stacks, since a
// same-name fusion always consumes from one level and produces the next.
//
// This is deliberately NOT full individual-instance tracking (no unique ID
// per physical card) - the spec explicitly says stack/count management is
// fine for Ver.1 as long as the structure can grow toward per-instance
// management later (e.g. a future compound-fusion result with its own
// unique ability). Splitting a stack's `count` into N individual instance
// rows later is a straightforward extension of this same save format
// (each stack becomes N rows of count=1 with an added instanceId) without
// needing to redesign the persistence key.
public static class CardInventory
{
    const string SaveKey = "OwnedCardsV1";

    // Card Level Ver.1 - "カードLv = そのカードを何回取得した状態として
    // 扱うか" (Lv.N == N stacked applications of the card's own existing
    // CardEffect list - see GameManager.ApplyCardEffectsStacked). Lv.1-5,
    // MAX at 5; same-name fusion (CardFusionUI.DoSameNameFusion) refuses to
    // level up a card already at this cap. Hoisted here (not on a UI
    // script) since Card Level is fundamentally inventory/data, not
    // presentation - GameManager and CardFusionUI both reference this same
    // constant so the cap can never drift between the two.
    public const int MaxCardLevel = 5;

    // Card Level Ver.1, item 7 - future cross-name fusion may want a single
    // owned card to carry more than one (CardId, EffectLevel) pair at once
    // (e.g. a Main effect at one level plus an inherited Sub effect at
    // another) - that inheritance rule isn't decided yet, so this Stack
    // deliberately stays "one cardId, one level" for now. The natural
    // extension when it IS decided is adding a second (subCardId,
    // subLevel) pair here (or a small list of them) rather than redesigning
    // this class - `level`/`count` and the (cardId, level) keying scheme
    // don't need to change for that.
    [Serializable]
    public class Stack
    {
        public string cardId;
        public int level = 1;
        public int count;
    }

    [Serializable]
    class SaveWrapper
    {
        public List<Stack> stacks = new List<Stack>();
    }

    static List<Stack> stacks;

    public static IReadOnlyList<Stack> Stacks
    {
        get { EnsureLoaded(); return stacks; }
    }

    static void EnsureLoaded()
    {
        if (stacks != null) return;
        stacks = new List<Stack>();
        string json = PlayerPrefs.GetString(SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            SaveWrapper wrapper = null;
            try { wrapper = JsonUtility.FromJson<SaveWrapper>(json); }
            catch (Exception e) { Debug.LogWarning("[CardInventory] Failed to parse save, starting empty: " + e.Message); }
            if (wrapper != null && wrapper.stacks != null) stacks = wrapper.stacks;
        }
    }

    static void Save()
    {
        var wrapper = new SaveWrapper { stacks = stacks };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(wrapper));
        PlayerPrefs.Save();
    }

    public static Stack Find(string cardId, int level)
    {
        EnsureLoaded();
        foreach (Stack s in stacks)
        {
            if (s.cardId == cardId && s.level == level) return s;
        }
        return null;
    }

    // Total copies owned across every level - used by the "is this card
    // owned at all" checks (Character Card equip eligibility, etc.).
    public static int GetTotalCount(string cardId)
    {
        EnsureLoaded();
        int total = 0;
        foreach (Stack s in stacks)
        {
            if (s.cardId == cardId) total += s.count;
        }
        return total;
    }

    public static int GetCount(string cardId, int level)
    {
        Stack s = Find(cardId, level);
        return s != null ? s.count : 0;
    }

    // Card UI / Rarity Frame pass - display-only lookup (no logic change to
    // Deck itself, which stores cardId only, no level - see GameManager.
    // deckCards) for showing "Lv.N" on a Deck slot: the HIGHEST level this
    // cardId is owned at across all stacks, or 0 if not owned at all.
    public static int GetHighestLevel(string cardId)
    {
        EnsureLoaded();
        int highest = 0;
        foreach (Stack s in stacks)
        {
            if (s.cardId == cardId && s.count > 0 && s.level > highest) highest = s.level;
        }
        return highest;
    }

    public static void AddCard(string cardId, int level, int count = 1)
    {
        if (string.IsNullOrEmpty(cardId) || count <= 0) return;
        EnsureLoaded();
        Stack existing = Find(cardId, level);
        if (existing != null) existing.count += count;
        else stacks.Add(new Stack { cardId = cardId, level = level, count = count });
        Save();
    }

    // Consumes `count` copies at exactly (cardId, level) - fails (no
    // partial consumption) if fewer than that many are owned.
    public static bool RemoveCard(string cardId, int level, int count = 1)
    {
        EnsureLoaded();
        Stack existing = Find(cardId, level);
        if (existing == null || existing.count < count) return false;
        existing.count -= count;
        if (existing.count <= 0) stacks.Remove(existing);
        Save();
        return true;
    }

    public static void ResetAll()
    {
        EnsureLoaded();
        stacks.Clear();
        Save();
    }

    // Debug only - grants exactly 1 copy of every card currently in
    // CardDatabase at Lv.1, for quickly populating a test inventory.
    public static void DebugAddOneOfEvery()
    {
        foreach (CardDefinition card in CardDatabase.AllCards)
        {
            AddCard(card.cardId, 1, 1);
        }
    }
}

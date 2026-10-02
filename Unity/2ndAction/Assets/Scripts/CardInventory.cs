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
    public const string SaveKey = "OwnedCardsV1";

    // Card Level Ver.1 - "カードLv = そのカードを何回取得した状態として
    // 扱うか" (Lv.N == N stacked applications of the card's own existing
    // CardEffect list - see GameManager.ApplyCardEffectsStacked). Lv.1-5,
    // MAX at 5; same-name fusion (CardFusionUI.DoSameNameFusion) refuses to
    // level up a card already at this cap. Hoisted here (not on a UI
    // script) since Card Level is fundamentally inventory/data, not
    // presentation - GameManager and CardFusionUI both reference this same
    // constant so the cap can never drift between the two.
    // カード合成改修(2026-09-26) - 合成Lvの上限を5→9へ(CardVariant.MaxLevel)。
    public const int MaxCardLevel = CardVariant.MaxLevel;

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
    public class SaveWrapper
    {
        public List<Stack> stacks = new List<Stack>();
    }

    // CardDataMigration等がPlayerPrefsを書き換えた後に、次のアクセスで読み直させる。
    public static void ReloadFromPrefs() { stacks = null; }

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
        WriteWithoutFlush();
        PlayerPrefs.Save();
    }

    // 合成の確定処理(CardFusionLogic.Commit)は所持カードとMILEを両方書いてから
    // PlayerPrefs.Save()を1回だけ呼ぶ(途中までしか保存されない状態を作らない)。
    public static void WriteWithoutFlush()
    {
        EnsureLoaded();
        var wrapper = new SaveWrapper { stacks = stacks };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(wrapper));
    }

    // 合成用: 素材2枚を消費して完成品(addKey、nullなら無し)を1枚加える変更を
    // メモリ上だけで行う。足りなければ何も変えずにfalse。保存は呼び出し側。
    public static bool ApplyFusionInMemory(string mainKey, string materialKey, string addKey)
    {
        EnsureLoaded();
        Stack a = FindByKey(mainKey);
        Stack b = FindByKey(materialKey);
        if (a == null || b == null) return false;
        if (a == b ? a.count < 2 : (a.count < 1 || b.count < 1)) return false;
        a.count--;
        b.count--;
        if (a.count <= 0) stacks.Remove(a);
        if (b != a && b.count <= 0) stacks.Remove(b);
        if (!string.IsNullOrEmpty(addKey))
        {
            CardVariant v = CardVariant.Parse(addKey);
            int level = v != null ? v.level : 1;
            Stack existing = FindByKey(addKey);
            if (existing != null) existing.count++;
            else stacks.Add(new Stack { cardId = addKey, level = level, count = 1 });
        }
        return true;
    }

    // キー(cardIdまたはv2キー)で引く - 1つのキーは必ず1つのLvにしか対応しない。
    public static Stack FindByKey(string key)
    {
        EnsureLoaded();
        foreach (Stack s in stacks) if (s.cardId == key && s.count > 0) return s;
        return null;
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
        // 素のカードIDをLv.2以上で追加する旧来の呼び方は、その性能のv2キーへ読み替える
        // (素のIDは常にLv.1・強化量1を意味する)。
        if (!CardVariant.IsVariantKey(cardId) && level > 1)
        {
            cardId = CardDataMigration.LegacyToKey(cardId, level);
            CardVariant v = CardVariant.Parse(cardId);
            if (v != null) level = v.level;
        }
        // カードVisual最終調整依頼(2026-09-18), item1 - 「候補に出た=NEW」
        // ではなく「実際に取得した=NEW」にするための判定はここ一箇所に
        // 集約する。AddCardはLevel Up選択確定時/Gacha抽選時のどちらから
        // 呼ばれても必ずここを通る唯一の入口なので、呼び出し側ごとに
        // 個別判定させる必要がない。「このカードを1枚でも既に所持して
        // いたか」を加算前に見て、初めてなら新規取得済み(未確認)として
        // マークする。
        bool wasOwnedBefore = GetTotalCount(cardId) > 0;
        Stack existing = Find(cardId, level);
        if (existing != null) existing.count += count;
        else stacks.Add(new Stack { cardId = cardId, level = level, count = count });
        if (!wasOwnedBefore) MarkNewUnconfirmed(cardId);
        Save();
    }

    // カードVisual最終調整依頼(2026-09-18), item1 - 「実際に新規取得した
    // が、まだプレイヤーが確認していないカード」の集合。AddCard内で自動的
    // に追加され、Collection等でそのカードを実際に見た(タップした)時点で
    // ClearNewを呼んで解除する想定。CardInventory本体(所持数)とは別の
    // 軽量な状態なので、別のPlayerPrefsキーに分けて保存する。
    const string NewUnconfirmedSaveKey = "NewUnconfirmedCardsV1";
    static HashSet<string> newUnconfirmed;

    static void EnsureNewUnconfirmedLoaded()
    {
        if (newUnconfirmed != null) return;
        newUnconfirmed = new HashSet<string>();
        string raw = PlayerPrefs.GetString(NewUnconfirmedSaveKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string id in raw.Split(','))
        {
            if (!string.IsNullOrEmpty(id)) newUnconfirmed.Add(id);
        }
    }

    static void SaveNewUnconfirmed()
    {
        PlayerPrefs.SetString(NewUnconfirmedSaveKey, string.Join(",", newUnconfirmed));
        PlayerPrefs.Save();
    }

    static void MarkNewUnconfirmed(string cardId)
    {
        EnsureNewUnconfirmedLoaded();
        if (newUnconfirmed.Add(cardId)) SaveNewUnconfirmed();
    }

    public static bool IsNewUnconfirmed(string cardId)
    {
        EnsureNewUnconfirmedLoaded();
        return !string.IsNullOrEmpty(cardId) && newUnconfirmed.Contains(cardId);
    }

    // プレイヤーがCollection等でこのカードを実際に確認した時に呼ぶ -
    // NEW状態を解除する。
    public static void ClearNewUnconfirmed(string cardId)
    {
        EnsureNewUnconfirmedLoaded();
        if (newUnconfirmed.Remove(cardId)) SaveNewUnconfirmed();
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

    // 開発版の全カード開放(2026-10-02): まだ1枚も持っていないカードだけ Lv.1 を1枚(押すたびに増やさない)
    public static int DebugOwnEveryMissing()
    {
        int n = 0;
        foreach (CardDefinition card in CardDatabase.AllCards)
            if (!CardVariant.IsVariantKey(card.cardId) && GetTotalCount(card.cardId) <= 0) { AddCard(card.cardId, 1, 1); n++; }
        return n;
    }

    // Lv2以上の素のカードは「Lv分の強化量を持つ合成カード」のキー(v2|id|Lv|…)で持つので、そのキーで数える
    public static int DebugCountAtLevel(string cardId, int level)
    {
        if (level <= 1) return GetCount(cardId, 1);
        string key = CardDataMigration.LegacyToKey(cardId, level);
        CardVariant v = CardVariant.Parse(key);
        return GetCount(key, v != null ? v.level : level);
    }

    // 開発版: 全カードを Lv.9 で count 枚ずつ(キャラカード枠・デッキの確認用)。足りない分だけ足す
    public static int DebugOwnEveryAtLevel(int level, int count)
    {
        int n = 0;
        foreach (CardDefinition card in CardDatabase.AllCards)
        {
            if (CardVariant.IsVariantKey(card.cardId)) continue;
            int have = DebugCountAtLevel(card.cardId, level);
            if (have < count) { AddCard(card.cardId, level, count - have); n++; }
        }
        return n;
    }
}

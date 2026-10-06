using System.Collections.Generic;
using UnityEngine;

// カード合成改修(2026-09-26) - 旧形式の所持カード/デッキ/キャラクターカードを、
// 能力一式を持つ新形式(CardVariantのキー)へ一度だけ変換する。
//
// 旧形式:
//   所持カード   … (cardId, Lv, 枚数)。Lv.N = そのカードをN回取得した状態(キャラカード装備時にN回適用)。
//   複合カード   … cardIdが"主+副"(旧異名合成の両方成功)。主と副の効果を1回ずつ持つ。
//   デッキ       … cardIdの並び(Lvを持たず、同じcardIdの低いLvから順に割り当てていた)。
//   キャラカード … "cardId:Lv"。
// 新形式への対応:
//   素のカードLv.N          → 主能力×N、合成Lv.N
//   複合カード"主+副" Lv.N  → 主能力×N + サブ能力(副)×N、合成Lv.N、レア度は旧複合の値
// 所持枚数・デッキの枚数・キャラカード装備・NEW表示は一切減らさない。
// 所持カード/デッキ/キャラカードをまとめて書き換えてからPlayerPrefs.Save()を1回だけ
// 呼ぶ(途中で落ちても旧形式のまま次回やり直せる)。
public static class CardDataMigration
{
    public const string FormatKey = "CardDataFormat";
    public const int CurrentFormat = 2;
    const string DeckKey = "DeckCardIds";
    const string CharacterCardSlotsKey = "CharacterCardSlots";

    public static string LastReport { get; private set; } = "";

    public static string LegacyToKey(string id, int level)
    {
        if (string.IsNullOrEmpty(id)) return id;
        if (CardVariant.IsVariantKey(id)) return id;
        level = Mathf.Clamp(level, 1, CardVariant.MaxLevel);
        int plus = id.IndexOf('+');
        CardVariant v;
        if (plus > 0)
        {
            string mainId = FirstPlainId(id.Substring(0, plus));
            string subId = FirstPlainId(id.Substring(plus + 1));
            CardDefinition oldCompound = CardDatabase.FindById(id);
            if (CardDatabase.FindBaseById(mainId) == null) return id;
            v = new CardVariant { mainId = mainId, level = level, rarity = oldCompound != null ? Mathf.Clamp(oldCompound.rarity, 1, 5) : 1 };
            v.AddAbility(mainId, level);
            if (CardDatabase.FindBaseById(subId) != null) v.AddAbility(subId, level);
        }
        else
        {
            CardDefinition def = CardDatabase.FindBaseById(id);
            if (def == null) return id;
            v = new CardVariant { mainId = id, level = level, rarity = Mathf.Clamp(def.rarity, 1, 5) };
            v.AddAbility(id, level);
        }
        return v.ToKey();
    }

    // 旧複合IDの片側から主になるカードIDを取り出す("a+b"の入れ子なら先頭)。
    static string FirstPlainId(string part)
    {
        int plus = part.IndexOf('+');
        return plus > 0 ? part.Substring(0, plus) : part;
    }

    public static bool IsLegacy(string id, int level) =>
        !string.IsNullOrEmpty(id) && !CardVariant.IsVariantKey(id) && (level > 1 || id.IndexOf('+') >= 0);

    public static void RunIfNeeded()
    {
        if (SaveStore.GetInt(FormatKey, 0) >= CurrentFormat) return;
        var report = new System.Text.StringBuilder();

        // ---- 所持カード ----
        var wrapper = new CardInventory.SaveWrapper();
        string json = SaveStore.GetString(CardInventory.SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { wrapper = JsonUtility.FromJson<CardInventory.SaveWrapper>(json) ?? new CardInventory.SaveWrapper(); }
            catch (System.Exception e) { Debug.LogWarning("[CardDataMigration] 所持カードの読み込みに失敗、変換を中止: " + e.Message); return; }
        }
        // 旧(cardId, Lv) → 新キー の対応(デッキの割り当てに使う)。
        var oldStacks = new List<CardInventory.Stack>();
        var merged = new List<CardInventory.Stack>();
        int converted = 0;
        foreach (var s in wrapper.stacks)
        {
            if (s == null || s.count <= 0 || string.IsNullOrEmpty(s.cardId)) continue;
            oldStacks.Add(new CardInventory.Stack { cardId = s.cardId, level = s.level, count = s.count });
            string key = LegacyToKey(s.cardId, s.level);
            if (key != s.cardId) converted++;
            CardVariant v = CardVariant.Parse(key);
            int level = v != null ? v.level : s.level;
            var existing = merged.Find(m => m.cardId == key);
            if (existing != null) existing.count += s.count;
            else merged.Add(new CardInventory.Stack { cardId = key, level = level, count = s.count });
        }
        report.Append($"stacks {wrapper.stacks.Count}->{merged.Count} (converted {converted}); ");

        // ---- キャラクターカード("id:Lv") ----
        string slots = SaveStore.GetString(CharacterCardSlotsKey, "");
        string[] entries = slots.Split(',');
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.IsNullOrEmpty(entries[i])) continue;
            string[] parts = entries[i].Split(':');
            int lv = 1;
            if (parts.Length > 1) int.TryParse(parts[1], out lv);
            if (!IsLegacy(parts[0], lv)) continue;
            string key = LegacyToKey(parts[0], lv);
            CardVariant v = CardVariant.Parse(key);
            entries[i] = $"{key}:{(v != null ? v.level : lv)}";
            report.Append($"charSlot{i} {parts[0]}:{lv}->{key}; ");
        }
        string newSlots = string.Join(",", entries);

        // ---- デッキ(旧: 同じcardIdの低いLvから順に割り当て) ----
        string deckRaw = SaveStore.GetString(DeckKey, "");
        bool hadDeck = SaveStore.HasKey(DeckKey);
        var deck = new List<string>();
        if (!string.IsNullOrEmpty(deckRaw))
        {
            var remaining = new Dictionary<CardInventory.Stack, int>();
            foreach (var s in oldStacks) remaining[s] = s.count;
            foreach (string id in deckRaw.Split(','))
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (CardVariant.IsVariantKey(id)) { deck.Add(id); continue; }
                CardInventory.Stack pick = null;
                foreach (var s in oldStacks)
                {
                    if (s.cardId != id || remaining[s] <= 0) continue;
                    if (pick == null || s.level < pick.level) pick = s;
                }
                if (pick != null) { remaining[pick]--; deck.Add(LegacyToKey(pick.cardId, pick.level)); }
                else deck.Add(id.IndexOf('+') >= 0 ? LegacyToKey(id, 1) : id);
            }
            report.Append($"deck {deckRaw} -> {string.Join(",", deck)}; ");
        }

        // ---- まとめて書き込み ----
        SaveStore.SetString(CardInventory.SaveKey, JsonUtility.ToJson(new CardInventory.SaveWrapper { stacks = merged }));
        if (!string.IsNullOrEmpty(slots)) SaveStore.SetString(CharacterCardSlotsKey, newSlots);
        if (hadDeck) SaveStore.SetString(DeckKey, string.Join(",", deck));
        SaveStore.SetInt(FormatKey, CurrentFormat);
        SaveStore.Save();
        CardInventory.ReloadFromPrefs();
        LastReport = report.ToString();
        Debug.Log("[CardDataMigration] " + LastReport);
    }
}

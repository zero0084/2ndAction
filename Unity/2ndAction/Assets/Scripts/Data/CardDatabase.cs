using System.Collections.Generic;
using UnityEngine;

// Loads every CardDefinition asset under Resources/Cards once and caches
// it, sorted by sortOrder. Both the level-up draw pool and the Deck Edit
// screen read through this instead of GameManager holding a hand-written
// list, so a new card asset (built by CardDatabaseBuilder, or hand-added
// later the same way) shows up everywhere automatically.
public static class CardDatabase
{
    static List<CardDefinition> cachedCards;

    public static IReadOnlyList<CardDefinition> AllCards
    {
        get
        {
            if (cachedCards == null) Load();
            return cachedCards;
        }
    }

    static void Load()
    {
        CardDefinition[] loaded = Resources.LoadAll<CardDefinition>("Cards");
        cachedCards = new List<CardDefinition>(loaded);
        cachedCards.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
    }

    // Clears the cache so the next AllCards access re-reads from Resources -
    // called by CardDatabaseBuilder right after it creates new assets, so a
    // scene rebuild's later steps (e.g. the Deck Edit grid) see them
    // without needing a domain reload first.
    public static void Reset()
    {
        cachedCards = null;
        compoundCache.Clear();
        variantCache.Clear();
    }

    // Resources上の実カード(アセット)だけを引く。合成で作ったカード(v2キー)は含まない。
    public static CardDefinition FindBaseById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;
        foreach (CardDefinition card in AllCards)
        {
            if (card.cardId == cardId) return card;
        }
        return null;
    }

    public static CardDefinition FindById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;
        CardDefinition baseCard = FindBaseById(cardId);
        if (baseCard != null) return baseCard;
        if (CardVariant.IsVariantKey(cardId)) return FindOrBuildVariant(cardId);
        return FindOrBuildCompound(cardId);
    }

    // カード合成改修(2026-09-26) - 合成カード(CardVariantのv2キー)を、既存の
    // CardDefinitionとして扱えるようにその場で組み立てる。effectsには各能力の
    // 元カードのeffects一式を「強化量」回ぶん並べる - 既存のApplyCardEffectsが
    // 1回呼ばれるだけで「各能力を強化量ぶん取得した」のと同じ結果になる
    // (同じカードを何回か拾った時の既存の重ねがけ規則そのまま)。合成Lvは
    // 効果には掛けない。
    static readonly Dictionary<string, CardDefinition> variantCache = new Dictionary<string, CardDefinition>();

    static CardDefinition FindOrBuildVariant(string key)
    {
        if (variantCache.TryGetValue(key, out CardDefinition cached) && cached != null) return cached;
        CardVariant v = CardVariant.Parse(key);
        if (v == null) return null;
        CardDefinition main = FindBaseById(v.mainId);
        if (main == null) return null;

        var def = ScriptableObject.CreateInstance<CardDefinition>();
        def.cardId = key;
        def.cardName = main.cardName;
        def.icon = main.icon;
        def.category = main.category;
        def.sortOrder = main.sortOrder;
        def.recommendPriority = main.recommendPriority;
        def.rarity = v.rarity;
        def.element = main.element;
        def.description = v.Describe();
        def.effects = new List<CardEffect>();
        foreach (var a in v.abilities)
        {
            CardDefinition src = FindBaseById(a.id);
            if (src == null) continue;
            for (int n = 0; n < a.stacks; n++)
                foreach (CardEffect e in src.effects) def.effects.Add(new CardEffect { type = e.type, value = e.value });
        }
        variantCache[key] = def;
        return def;
    }

    // Fusion Ver.1 restoration (2026-09-06), item "Fusionの仕様を本来の設
    // 計へ戻す" - a compound card (both Main and Sub inheritance rolls
    // succeed in CardFusionUI.DoCrossNameFusion) is never saved as its own
    // asset or as a separate "recipe" record; its cardId IS the recipe
    // ("mainId+subId"), and CardInventory only ever needs to remember that
    // one string (its Stack rows are just (cardId, level, count), exactly
    // like any real card) - so this regenerates an equivalent
    // CardDefinition on demand, purely by splitting the id at its first '+'
    // and resolving each half back through FindById itself. That
    // recursion is intentional and safe: each half is always strictly
    // shorter than the original id, so it terminates once a half contains
    // no more '+' and resolves directly out of AllCards (or fails) - which
    // also means fusing an already-compound card as a further Main/Sub
    // works with no extra code, nesting arbitrarily deep, even though nothing
    // currently requires that. Real (non-compound) card ids must never
    // contain '+' for this scheme to stay unambiguous - none currently do
    // (see CardDatabaseBuilder's Specs, all snake_case).
    //
    // Capped, per the brief's own "Main 1 Effect + Sub 1 Effect" (no need
    // for unlimited stacking), at each half's own FIRST effect only - a
    // card with multiple effects (e.g. BERSERKER) only contributes its
    // primary one to a compound. Cached (keyed by the full id) so repeated
    // lookups (every RewardCardUI refresh) don't reallocate a new
    // ScriptableObject instance each time.
    static readonly Dictionary<string, CardDefinition> compoundCache = new Dictionary<string, CardDefinition>();

    static CardDefinition FindOrBuildCompound(string cardId)
    {
        int plus = cardId.IndexOf('+');
        if (plus <= 0 || plus >= cardId.Length - 1) return null; // no '+', or '+' at either end - not a compound id

        if (compoundCache.TryGetValue(cardId, out CardDefinition cached) && cached != null) return cached;

        string mainId = cardId.Substring(0, plus);
        string subId = cardId.Substring(plus + 1);
        CardDefinition mainCard = FindById(mainId);
        CardDefinition subCard = FindById(subId);
        if (mainCard == null || subCard == null) return null; // dangling reference to a card that no longer exists

        var compound = ScriptableObject.CreateInstance<CardDefinition>();
        compound.cardId = cardId;
        compound.cardName = $"{mainCard.cardName}【{subCard.cardName}】";
        compound.icon = mainCard.icon;
        compound.description = $"{mainCard.description}\n+ {subCard.description}";
        compound.category = mainCard.category;
        compound.sortOrder = mainCard.sortOrder;
        compound.recommendPriority = Mathf.Min(mainCard.recommendPriority, subCard.recommendPriority);
        // A compound reads as a cut above either ingredient, capped at ★5
        // like every other card's rarity.
        compound.rarity = Mathf.Clamp(Mathf.Max(mainCard.rarity, subCard.rarity) + 1, 1, 5);
        compound.element = mainCard.element != ElementType.None ? mainCard.element : subCard.element;
        compound.effects = new List<CardEffect>();
        if (mainCard.effects.Count > 0) compound.effects.Add(new CardEffect { type = mainCard.effects[0].type, value = mainCard.effects[0].value });
        if (subCard.effects.Count > 0) compound.effects.Add(new CardEffect { type = subCard.effects[0].type, value = subCard.effects[0].value });
        // unlockDistance/gachaStage are left at CardDefinition's own bare
        // defaults (0/1) - harmless, since a compound is never a member of
        // AllCards (only ever synthesized on demand here), so it can never
        // actually appear in BuildGachaPool/UnlockedCards regardless of
        // these values.

        compoundCache[cardId] = compound;
        return compound;
    }

    // Bugfix 2026-09-06, item "Card Unlockシステムを一元化" - the subset of
    // AllCards currently unlocked, used for the starter-deck fill
    // (GameManager.LoadDeck) and the corrupted-deck recovery pool
    // (RunLevelUpChoice/RunBossRewardChoice). Previously gated by a
    // SEPARATE UnlockManager/UnlockDefinition entry
    // (unlock_pathfinder_card_500m, UnlockType.Card) that duplicated - and
    // could disagree with - the Gacha draw pool's own eligibility rule
    // (GachaStage.IsCardEligible, driven by CardDefinition.unlockDistance/
    // gachaStage). That duplication is exactly the "Gachaで引いたのに
    // Deckで使えない" risk the brief called out: a card could be eligible
    // to draw from Gacha but still fail this check (or vice versa) if the
    // two definitions ever drifted. Now unified onto the single source of
    // truth (CardDefinition.unlockDistance/gachaStage, the same rule
    // BuildGachaPool uses) - UnlockManager/UnlockDefinition entries for
    // UnlockType.Enemy (goblin_elite, etc.) are untouched, this only
    // changes Card-type gating. Falls back to "every card" if
    // GameManager.Instance isn't available yet (shouldn't happen in
    // practice - Awake sets Instance before LoadDeck runs - but avoids a
    // null-reference if some caller ever runs earlier).
    public static List<CardDefinition> UnlockedCards
    {
        get
        {
            var result = new List<CardDefinition>();
            GameManager gm = GameManager.Instance;
            foreach (CardDefinition card in AllCards)
            {
                bool eligible = gm == null || GachaStage.IsCardEligible(card, gm.BestDistance, gm.CurrentGachaStage);
                if (eligible) result.Add(card);
            }
            return result;
        }
    }

    // "おすすめ編成" (DeckEditUI) - a simple, non-optimizing auto-build for
    // players who don't want to hand-pick a deck: every unlocked card,
    // ranked by CardDefinition.recommendPriority (ties broken by sortOrder
    // for a stable/predictable result), taking the top `capacity`. This is
    // deliberately NOT a "compute the strongest possible deck" algorithm -
    // just a beginner-friendly default that avoids handing out the
    // trade-off/pure-drawback cards (see recommendPriority's own comment)
    // without the player asking for them specifically.
    public static List<string> BuildRecommendedDeck(int capacity)
    {
        var ranked = new List<CardDefinition>(UnlockedCards);
        ranked.Sort((a, b) =>
        {
            int byPriority = b.recommendPriority.CompareTo(a.recommendPriority);
            return byPriority != 0 ? byPriority : a.sortOrder.CompareTo(b.sortOrder);
        });

        var result = new List<string>();
        for (int i = 0; i < ranked.Count && result.Count < capacity; i++)
        {
            result.Add(ranked[i].cardId);
        }
        return result;
    }
}

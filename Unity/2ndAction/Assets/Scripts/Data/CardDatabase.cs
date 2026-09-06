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
    }

    public static CardDefinition FindById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;
        foreach (CardDefinition card in AllCards)
        {
            if (card.cardId == cardId) return card;
        }
        return null;
    }

    // The subset of AllCards currently unlocked (see UnlockManager) - what
    // the Deck Edit "owned cards" list and any deck-default/fallback pool
    // should actually offer, so a distance-gated card can't be added to a
    // deck or drawn on level-up before it's earned. Cards with no matching
    // UnlockDefinition are always included, so every pre-existing card
    // keeps working unchanged. Allocates a fresh list and re-filters every
    // call (unlocks can happen mid-run) rather than caching - fine for the
    // occasional callers this is meant for (deck screen refresh, level-up
    // trigger), not a per-frame call.
    public static List<CardDefinition> UnlockedCards
    {
        get
        {
            var result = new List<CardDefinition>();
            foreach (CardDefinition card in AllCards)
            {
                if (UnlockManager.IsTargetUnlocked(UnlockType.Card, card.cardId)) result.Add(card);
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

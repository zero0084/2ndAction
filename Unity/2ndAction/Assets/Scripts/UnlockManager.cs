using System.Collections.Generic;
using UnityEngine;

// Runtime + persisted tracking of which UnlockDefinitions have been earned.
// Static (not a MonoBehaviour) so CardDatabase/EnemyDatabase/DeckEditUI can
// all query it directly without needing a scene reference, the same way
// CardDatabase/UnlockDatabase are static.
//
// Two things happen once a distance threshold is crossed for the first
// time: (1) it's added to the persisted set immediately (survives the run
// ending, a crash, whatever), (2) it's queued in newlyUnlocked so
// GameManager can show a one-time "NEW UNLOCK" announcement instead of
// repeating it every time the condition is re-checked or on a future run.
public static class UnlockManager
{
    const string UnlockedIdsKey = "UnlockedIds";

    static HashSet<string> unlockedIds;
    static readonly List<UnlockDefinition> newlyUnlocked = new List<UnlockDefinition>();

    // Must be called once before any of the query methods below are
    // trusted - GameManager.Awake() does this right after BestDistance is
    // loaded, before LoadDeck() (which needs IsTargetUnlocked to already
    // work correctly for its default-deck fallback).
    public static void Initialize(float bestDistanceEver)
    {
        unlockedIds = new HashSet<string>();
        string saved = SaveStore.GetString(UnlockedIdsKey, "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string id in saved.Split(','))
            {
                if (!string.IsNullOrEmpty(id)) unlockedIds.Add(id);
            }
        }

        // Silent backfill: anything the player's persisted best distance
        // already qualifies for (e.g. they'd already gone 5000m before
        // this system - or before a new low-distance unlock - existed)
        // unlocks immediately, with no announcement - it isn't "new" to
        // them, CheckUnlocks below is what queues an actual fresh crossing
        // during play.
        bool changed = false;
        foreach (UnlockDefinition def in UnlockDatabase.AllUnlocks)
        {
            if (!unlockedIds.Contains(def.unlockId) && bestDistanceEver >= def.requiredDistance)
            {
                unlockedIds.Add(def.unlockId);
                changed = true;
            }
        }
        if (changed) Save();
    }

    public static bool IsUnlocked(string unlockId)
    {
        return unlockedIds != null && !string.IsNullOrEmpty(unlockId) && unlockedIds.Contains(unlockId);
    }

    // Whether a specific piece of content (a card id, an enemy id, ...) is
    // currently available. Content with no matching UnlockDefinition is
    // always available - unlocks are opt-in, so every card/enemy that
    // existed before this system keeps working unchanged.
    public static bool IsTargetUnlocked(UnlockType type, string targetId)
    {
        UnlockDefinition def = UnlockDatabase.FindByTarget(type, targetId);
        return def == null || IsUnlocked(def.unlockId);
    }

    // Called whenever the run's distance advances (see
    // GameManager.ReportDistance) - unlocks anything newly reached and
    // queues it for the on-screen announcement.
    public static void CheckUnlocks(float currentRunDistance)
    {
        if (unlockedIds == null) return;

        bool changed = false;
        foreach (UnlockDefinition def in UnlockDatabase.AllUnlocks)
        {
            if (!unlockedIds.Contains(def.unlockId) && currentRunDistance >= def.requiredDistance)
            {
                unlockedIds.Add(def.unlockId);
                newlyUnlocked.Add(def);
                changed = true;
            }
        }
        if (changed) Save();
    }

    // Returns (and clears) whatever unlocked for the first time since the
    // last call - null if nothing new. GameManager polls this to drive a
    // brief toast without ever showing the same unlock twice.
    public static List<UnlockDefinition> DrainNewlyUnlocked()
    {
        if (newlyUnlocked.Count == 0) return null;
        var result = new List<UnlockDefinition>(newlyUnlocked);
        newlyUnlocked.Clear();
        return result;
    }

    static void Save()
    {
        if (DebugRun.BlocksSave("UnlockedIds")) return;
        var ids = new List<string>(unlockedIds);
        SaveStore.SetString(UnlockedIdsKey, string.Join(",", ids));
        SaveStore.Save();
    }
}

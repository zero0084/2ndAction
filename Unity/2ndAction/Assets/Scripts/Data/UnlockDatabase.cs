using System.Collections.Generic;
using UnityEngine;

// Loads every UnlockDefinition asset under Resources/Unlocks once and
// caches it, sorted by requiredDistance - mirrors CardDatabase exactly, see
// its own comment for why (new content works with zero code changes).
public static class UnlockDatabase
{
    static List<UnlockDefinition> cached;

    public static IReadOnlyList<UnlockDefinition> AllUnlocks
    {
        get
        {
            if (cached == null) Load();
            return cached;
        }
    }

    static void Load()
    {
        UnlockDefinition[] loaded = Resources.LoadAll<UnlockDefinition>("Unlocks");
        cached = new List<UnlockDefinition>(loaded);
        cached.Sort((a, b) => a.requiredDistance.CompareTo(b.requiredDistance));
    }

    // Clears the cache so the next AllUnlocks access re-reads from
    // Resources - called by UnlockDatabaseBuilder right after it creates
    // new assets, same reason as CardDatabase.Reset().
    public static void Reset()
    {
        cached = null;
    }

    public static UnlockDefinition FindById(string unlockId)
    {
        if (string.IsNullOrEmpty(unlockId)) return null;
        foreach (UnlockDefinition def in AllUnlocks)
        {
            if (def.unlockId == unlockId) return def;
        }
        return null;
    }

    // The unlock (if any) gating this specific piece of content - null
    // means that content has no unlock requirement at all (always
    // available), which is how every card/enemy that predates this system
    // keeps working with zero changes.
    public static UnlockDefinition FindByTarget(UnlockType type, string targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return null;
        foreach (UnlockDefinition def in AllUnlocks)
        {
            if (def.unlockType == type && def.targetId == targetId) return def;
        }
        return null;
    }
}

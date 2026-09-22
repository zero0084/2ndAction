using System.Collections.Generic;
using UnityEngine;

// Loads every EnemyDefinition asset under Resources/Enemies once and
// caches it - mirrors CardDatabase/UnlockDatabase.
public static class EnemyDatabase
{
    static List<EnemyDefinition> cached;

    public static IReadOnlyList<EnemyDefinition> AllEnemies
    {
        get
        {
            if (cached == null) Load();
            return cached;
        }
    }

    static void Load()
    {
        EnemyDefinition[] loaded = Resources.LoadAll<EnemyDefinition>("Enemies");
        cached = new List<EnemyDefinition>(loaded);
    }

    public static void Reset()
    {
        cached = null;
    }

    public static EnemyDefinition FindById(string enemyId)
    {
        if (string.IsNullOrEmpty(enemyId)) return null;
        foreach (EnemyDefinition def in AllEnemies)
        {
            if (def.enemyId == enemyId) return def;
        }
        return null;
    }

    static readonly List<EnemyDefinition> candidatesBuffer = new List<EnemyDefinition>();

    // 自然洞窟雑魚敵追加(2026-09-22) - EnemyDefinition.stageIdsが空/nullなら
    // 従来どおり常に利用可(既存8種は全てこの分岐)。1つ以上指定されている
    // 場合は、現在のActiveRunStageIdがその中に含まれる時だけ利用可。
    // GameManager.Instanceがまだ無い場面(タイトル等)では安全側(空なら通す)。
    static bool StageAllows(EnemyDefinition def)
    {
        if (def.stageIds == null || def.stageIds.Length == 0) return true;
        string stageId = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : null;
        if (string.IsNullOrEmpty(stageId)) return false;
        foreach (string s in def.stageIds) if (s == stageId) return true;
        return false;
    }

    // Picks one random definition from `pool` that's currently unlocked -
    // `pool` is a caller-supplied list (typically a scene reference set
    // once at build time; see TerrainManager.enemyPool /
    // EnemyWallManager.enemyPool) rather than AllEnemies directly, so each
    // spawner can hold its own reference while still re-checking unlock
    // state fresh every single call (unlocks can happen mid-run). Returns
    // null if the pool is empty or nothing in it is unlocked yet - callers
    // fall back to their own legacy single-sprite field in that case, so
    // this never silently spawns nothing.
    public static EnemyDefinition PickRandomUnlocked(IReadOnlyList<EnemyDefinition> pool)
    {
        if (pool == null || pool.Count == 0) return null;

        candidatesBuffer.Clear();
        foreach (EnemyDefinition def in pool)
        {
            if (def != null && StageAllows(def) && UnlockManager.IsTargetUnlocked(UnlockType.Enemy, def.enemyId))
            {
                candidatesBuffer.Add(def);
            }
        }
        if (candidatesBuffer.Count == 0) return null;
        return candidatesBuffer[Random.Range(0, candidatesBuffer.Count)];
    }

    // Distance Level Design Ver.1 - same as PickRandomUnlocked above, but
    // additionally filtered to a single EnemyCategory (see
    // DistanceTierManager.TryStartFormation, which decides WHICH category
    // a spawn slot wants; this just resolves that category to an actual
    // unlocked species/asset from the pool). Returns null if
    // nothing in `pool` both matches `category` and is currently unlocked -
    // callers fall back to whatever their own legacy single-sprite field
    // is, same fail-safe pattern as PickRandomUnlocked.
    public static EnemyDefinition PickRandomUnlockedOfCategory(IReadOnlyList<EnemyDefinition> pool, EnemyCategory category)
    {
        if (pool == null || pool.Count == 0) return null;

        candidatesBuffer.Clear();
        foreach (EnemyDefinition def in pool)
        {
            if (def != null && def.category == category && StageAllows(def) && UnlockManager.IsTargetUnlocked(UnlockType.Enemy, def.enemyId))
            {
                candidatesBuffer.Add(def);
            }
        }
        if (candidatesBuffer.Count == 0) return null;
        return candidatesBuffer[Random.Range(0, candidatesBuffer.Count)];
    }
}

using System.Collections.Generic;
using UnityEngine;

// ステージ選択導線追加(2026-09-12) - Resources/Stagesに置かれた全
// StageDefinitionを読み込みキャッシュする。CharacterDatabaseと完全に
// 同じパターン。
public static class StageDatabase
{
    static List<StageDefinition> cached;

    public static IReadOnlyList<StageDefinition> AllStages
    {
        get
        {
            if (cached == null) Load();
            return cached;
        }
    }

    static void Load()
    {
        StageDefinition[] loaded = Resources.LoadAll<StageDefinition>("Stages");
        cached = new List<StageDefinition>(loaded);
        cached.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
    }

    public static void Reset()
    {
        cached = null;
    }

    // 今選べるか(2026-10-01): アセットの unlocked に加え、ラスダン(LAST CORRIDOR)は進行の解放フラグを見る
    // (開発版は Dev.FinalDungeonAlwaysOpen で常に選べる。ProgressStats.FinalDungeonAvailable)。
    public static bool IsAvailable(StageDefinition def)
    {
        if (def == null || !def.unlocked) return false;
        if (def.stageId == BossManager.LastStageId) return ProgressStats.FinalDungeonAvailable;
        return true;
    }

    public static StageDefinition FindById(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return null;
        foreach (StageDefinition def in AllStages)
        {
            if (def.stageId == stageId) return def;
        }
        return null;
    }
}

using UnityEditor;
using UnityEngine;

// ステージ選択導線追加(2026-09-12) - CharacterDatabaseSelfTestと同じ
// パターン。StageDatabaseBuilder.Buildが生成したResources/Stagesのアセット
// 群を、実際にResources.LoadAll経由で正しく読み込めるか(件数・表示順・
// unlockedフラグ)、およびRunCheckpoint.Data.stageIdがJsonUtility経由で
// 正しく往復するかを検証する。
public static class StageDatabaseSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Stage Database")]
    public static void Run()
    {
        StageDatabaseBuilder.Build();
        StageDatabase.Reset();
        var all = StageDatabase.AllStages;

        bool countOk = all.Count == 4;
        bool orderOk = countOk
            && all[0].stageId == "wasteland_road"
            && all[1].stageId == "natural_cave"
            && all[2].stageId == "sky_corridor"
            && all[3].stageId == "last_corridor" && all[3].unlocked;

        StageDefinition wasteland = StageDatabase.FindById("wasteland_road");
        bool wastelandUnlockedOk = wasteland != null && wasteland.unlocked;

        // 天空回廊解禁(2026-09-13) - 「いままでの天空マップ」を選択可能な
        // 別枠として解禁したため、ロックされたままなのは地下遺跡のみ。
        StageDefinition ruins = StageDatabase.FindById("natural_cave");
        StageDefinition sky = StageDatabase.FindById("sky_corridor");
        bool ruinsLockedOk = ruins != null && ruins.unlocked; // 自然洞窟(2026-09-21): 地下遺跡枠を自然洞窟に置き換え、解禁済み
        bool skyUnlockedOk = sky != null && sky.unlocked;

        bool missingIdOk = StageDatabase.FindById("no_such_stage") == null;

        var saved = new RunCheckpoint.Data { active = true, characterId = "swordsman", stageId = "wasteland_road" };
        string json = JsonUtility.ToJson(saved);
        var restored = JsonUtility.FromJson<RunCheckpoint.Data>(json);
        bool checkpointRoundTripOk = restored != null && restored.stageId == "wasteland_road";

        bool pass = countOk && orderOk && wastelandUnlockedOk && ruinsLockedOk && skyUnlockedOk && missingIdOk && checkpointRoundTripOk;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[StageDatabaseSelfTest] {result} - count={all.Count} orderOk={orderOk} " +
                  $"wastelandUnlockedOk={wastelandUnlockedOk} ruinsLockedOk={ruinsLockedOk} skyUnlockedOk={skyUnlockedOk} " +
                  $"missingIdOk={missingIdOk} checkpointRoundTripOk={checkpointRoundTripOk}");

        if (!pass)
        {
            Debug.LogError("[StageDatabaseSelfTest] FAIL - StageDatabase did not load the expected 4 stages in " +
                            "the expected order with the expected unlocked flags, or RunCheckpoint.Data.stageId " +
                            "did not round-trip through JsonUtility correctly.");
        }
    }
}

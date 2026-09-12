using UnityEditor;
using UnityEngine;

// キャラクター選択画面(2026-09-12) - CharacterDatabaseBuilder.Buildが生成
// したResources/Charactersのアセット群を、実際にResources.LoadAll経由で
// 正しく読み込めるか(件数・表示順・ポートレート画像の割り当て)を検証
// する。GameObject/MonoBehaviourのAwakeを必要としない純粋な静的クラスの
// 検証なので、他の自己診断テストと違いGroundFactory.CreateEnemy等は不要。
public static class CharacterDatabaseSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Character Database")]
    public static void Run()
    {
        CharacterDatabase.Reset();
        var all = CharacterDatabase.AllCharacters;

        bool countOk = all.Count == 3;
        bool orderOk = countOk
            && all[0].characterId == "swordsman"
            && all[1].characterId == "dual_blade"
            && all[2].characterId == "noble_lady";

        CharacterDefinition swordsman = CharacterDatabase.FindById("swordsman");
        bool findByIdOk = swordsman != null && swordsman.portrait != null && swordsman.mainVisual != null;

        CharacterDefinition nobleLady = CharacterDatabase.FindById("noble_lady");
        bool challengeFlagOk = nobleLady != null && nobleLady.challengeFlag;

        CharacterDefinition dualBlade = CharacterDatabase.FindById("dual_blade");
        bool nonChallengeFlagOk = dualBlade != null && !dualBlade.challengeFlag;

        bool missingIdOk = CharacterDatabase.FindById("no_such_character") == null;

        bool pass = countOk && orderOk && findByIdOk && challengeFlagOk && nonChallengeFlagOk && missingIdOk;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[CharacterDatabaseSelfTest] {result} - count={all.Count} orderOk={orderOk} " +
                  $"findByIdOk={findByIdOk} challengeFlagOk={challengeFlagOk} nonChallengeFlagOk={nonChallengeFlagOk} " +
                  $"missingIdOk={missingIdOk}");

        if (!pass)
        {
            Debug.LogError("[CharacterDatabaseSelfTest] FAIL - CharacterDatabase did not load the expected 3 " +
                            "characters in the expected order with the expected portrait/challengeFlag data. " +
                            "Run Tools/OneMoreMile/Build Character Database (or SceneBuilder.Build) first if the " +
                            "Resources/Characters assets don't exist yet.");
        }
    }
}

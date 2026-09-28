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

        // 4人目の拳銃士追加(2026-09-23)に伴い3→4へ更新(このテスト自体は
        // 2026-09-12時点のまま放置されており、拳銃士追加後もcount==3の
        // ハードコードのままFAILし続けていた - 発見のたび更新すること)。
        // 5人目の竜騎士追加(2026-09-26)で4→5。新4人(弓使い/魔法使い/格闘家/忍者、2026-09-27)で5→9。
        // 追加3人(巫女/吸血鬼/竜人、2026-09-28)で9→12。
        bool countOk = all.Count == 12;
        bool orderOk = countOk
            && all[0].characterId == "swordsman"
            && all[1].characterId == "dual_blade"
            && all[2].characterId == "noble_lady"
            && all[3].characterId == "gunslinger"
            && all[4].characterId == "dragon_lancer"
            && all[5].characterId == "archer"
            && all[6].characterId == "mage"
            && all[7].characterId == "fighter"
            && all[8].characterId == "ninja"
            && all[9].characterId == "miko"
            && all[10].characterId == "vampire"
            && all[11].characterId == "dragonkin";

        CharacterDefinition swordsman = CharacterDatabase.FindById("swordsman");
        bool findByIdOk = swordsman != null && swordsman.portrait != null && swordsman.mainVisual != null;

        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - マスターの新
        // 明示指示「CHALLENGE HERO/特別枠/専用バッジは不要」により、前回
        // パスで立てていたnoble_lady.challengeFlag=trueを撤回した。現在は
        // 3人とも「普通に並ぶ」通常キャラクターであることを検証する。
        CharacterDefinition nobleLady = CharacterDatabase.FindById("noble_lady");
        bool nobleLadyNoBadgeOk = nobleLady != null && !nobleLady.challengeFlag;

        CharacterDefinition dualBlade = CharacterDatabase.FindById("dual_blade");
        bool nonChallengeFlagOk = dualBlade != null && !dualBlade.challengeFlag;

        bool missingIdOk = CharacterDatabase.FindById("no_such_character") == null;

        bool pass = countOk && orderOk && findByIdOk && nobleLadyNoBadgeOk && nonChallengeFlagOk && missingIdOk;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[CharacterDatabaseSelfTest] {result} - count={all.Count} orderOk={orderOk} " +
                  $"findByIdOk={findByIdOk} nobleLadyNoBadgeOk={nobleLadyNoBadgeOk} nonChallengeFlagOk={nonChallengeFlagOk} " +
                  $"missingIdOk={missingIdOk}");

        if (!pass)
        {
            Debug.LogError("[CharacterDatabaseSelfTest] FAIL - CharacterDatabase did not load the expected 4 " +
                            "characters in the expected order with the expected portrait/challengeFlag data. " +
                            "Run Tools/OneMoreMile/Build Character Database (or SceneBuilder.Build) first if the " +
                            "Resources/Characters assets don't exist yet.");
        }
    }
}

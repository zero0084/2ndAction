using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Creates (once) the UnlockDefinition assets under Assets/Resources/Unlocks/
// - mirrors CardDatabaseBuilder exactly, including the "never overwrite an
// existing asset" rule (so a distance value tuned by hand in the Inspector
// survives a rebuild). Adding a new Spec entry here (or a new .asset by
// hand) is the entire process for adding a new distance-gated unlock -
// UnlockManager/CardDatabase/EnemyDatabase already read through
// UnlockDatabase generically, no other code changes needed.
//
// Ver.0.1 test data only, per the current spec: 1000m unlocks a placeholder
// enemy variant. Distance and target are meant to be replaced/retuned once
// real content exists.
//
// Bugfix 2026-09-06, item "Card Unlockシステムを一元化" - UnlockType.Card
// entries used to live here too (unlock_pathfinder_card_500m), duplicating
// CardDefinition.unlockDistance/gachaStage (the rule BuildGachaPool/
// CardDatabase.UnlockedCards actually use) with a second, independent gate
// that could disagree with it. Card-type unlocks are no longer read from
// this system at all (see CardDatabase.UnlockedCards's own comment) -
// UnlockType.Enemy/Feature entries are untouched, this system still owns
// those.
public static class UnlockDatabaseBuilder
{
    const string UnlocksFolder = "Assets/Resources/Unlocks";

    struct Spec
    {
        public string id;
        public float distance;
        public UnlockType type;
        public string targetId;
        public string displayName;
    }

    [MenuItem("Tools/OneMoreMile/Build Unlock Database")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(UnlocksFolder))
        {
            Directory.CreateDirectory(UnlocksFolder);
            AssetDatabase.Refresh();
        }

        foreach (Spec spec in Specs())
        {
            string assetPath = $"{UnlocksFolder}/{spec.id}.asset";
            if (AssetDatabase.LoadAssetAtPath<UnlockDefinition>(assetPath) != null)
            {
                continue; // already exists - never overwrite, see class comment
            }

            var def = ScriptableObject.CreateInstance<UnlockDefinition>();
            def.unlockId = spec.id;
            def.requiredDistance = spec.distance;
            def.unlockType = spec.type;
            def.targetId = spec.targetId;
            def.displayName = spec.displayName;

            AssetDatabase.CreateAsset(def, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnlockDatabase.Reset();
    }

    static IEnumerable<Spec> Specs()
    {
        yield return new Spec
        {
            id = "unlock_elite_goblin_1000m",
            distance = 1000f,
            type = UnlockType.Enemy,
            targetId = "goblin_elite", // see EnemyDatabaseBuilder's Specs()
            displayName = "新しい敵: ELITE GOBLIN"
        };
    }
}

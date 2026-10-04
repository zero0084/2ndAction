using UnityEditor;
using UnityEngine;

// ラスダンのボス構成(2026-10-05): 調整値のアセット(Resources/Bosses/LastDungeonBossTuning.asset)を作る。
// 既にあれば作り直さない(Inspector で変えた値を消さない)。 -executeMethod LastDungeonBossTuningBuild.Run
public static class LastDungeonBossTuningBuild
{
    const string Path = "Assets/Resources/Bosses/LastDungeonBossTuning.asset";

    [MenuItem("Tools/OneMoreMile/Create Last Dungeon Boss Tuning")]
    public static void Run()
    {
        var a = AssetDatabase.LoadAssetAtPath<LastDungeonBossTuning>(Path);
        if (a != null) { Debug.Log("[LastDungeonBossTuningBuild] exists: " + Path); return; }
        a = ScriptableObject.CreateInstance<LastDungeonBossTuning>();
        AssetDatabase.CreateAsset(a, Path);
        AssetDatabase.SaveAssets();
        Debug.Log("[LastDungeonBossTuningBuild] created: " + Path);
    }
}

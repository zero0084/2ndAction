using UnityEditor;
using UnityEngine;

// 障害物の耐久力の調整アセット(Resources/Obstacles/ObstacleBalance.asset)を既定値で作る(2026-09-29)。
// 既にあれば上書きしない(調整した値を消さない)。値はInspectorで変更できる。
public static class ObstacleBalanceBuilder
{
    const string Dir = "Assets/Resources/Obstacles";
    const string Path = Dir + "/ObstacleBalance.asset";

    [MenuItem("Tools/OneMoreMile/Create Obstacle Balance Asset")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<ObstacleBalance>(Path) != null) { Debug.Log("[ObstacleBalance] already exists: " + Path); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Obstacles");
        AssetDatabase.CreateAsset(ObstacleBalance.CreateDefault(), Path);
        AssetDatabase.SaveAssets();
        Debug.Log("[ObstacleBalance] created " + Path);
    }
}

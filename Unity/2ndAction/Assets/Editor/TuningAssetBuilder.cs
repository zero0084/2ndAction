using System.IO;
using UnityEditor;
using UnityEngine;

// 2026-11-03: 難易度(DifficultyTuning)と背景の旅(JourneyProfile)の値を Inspector で直せるように、コードの既定値から asset を作る。
//  既にある asset は上書きしない(Inspector で直した値を守る)。作り直したい時は asset を消してからもう一度。
//  -executeMethod TuningAssetBuilder.CreateMissing(バッチでも使える)
public static class TuningAssetBuilder
{
    [MenuItem("OneMoreMile/Balance/Create Missing Tuning Assets")]
    public static void CreateMissing()
    {
        Make("Assets/Resources/Balance/DifficultyTuning.asset", () => ScriptableObject.CreateInstance<DifficultyTuning>());
        foreach (var st in new[] { "wasteland_road", "natural_cave", "sky_corridor" })
            Make($"Assets/Resources/Journey/{st}_journey.asset", () => JourneyProfile.Default(st));
        AssetDatabase.SaveAssets();
    }

    static void Make(string path, System.Func<ScriptableObject> create)
    {
        if (File.Exists(path)) { Debug.Log($"[TuningAssetBuilder] keep {path}"); return; }
        var o = create();
        if (o == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssetDatabase.CreateAsset(o, path);
        Debug.Log($"[TuningAssetBuilder] created {path}");
    }
}

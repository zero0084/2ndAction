using UnityEditor;
using UnityEngine;

// ボス戦の強化(2026-10-01)の調整値アセットを作る/既定値の種類を足す。
//  Tools/OneMoreMile/Build Boss Battle Tuning  (batch: -executeMethod BossBattleTuningBuilder.Build)
// 既にあるアセットの値は上書きしない(手で調整した値を守る)。足りない種類だけ既定値で追加する。
public static class BossBattleTuningBuilder
{
    const string Dir = "Assets/Resources/Bosses";
    const string Path = Dir + "/BossBattleTuning.asset";

    [MenuItem("Tools/OneMoreMile/Build Boss Battle Tuning")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Bosses");
        var t = AssetDatabase.LoadAssetAtPath<BossBattleTuning>(Path);
        bool create = t == null;
        if (create)
        {
            t = ScriptableObject.CreateInstance<BossBattleTuning>();
            t.entries = BossBattleTuning.DefaultEntries();
            AssetDatabase.CreateAsset(t, Path);
        }
        else
        {
            int added = 0;
            foreach (var e in BossBattleTuning.DefaultEntries())
                if (t.entries.Find(x => x != null && x.key == e.key) == null) { t.entries.Add(e); added++; }
            Debug.Log($"[BossBattleTuningBuilder] kept existing values, added {added} missing entries");
        }
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BossBattleTuningBuilder] {(create ? "created" : "updated")} {Path}: {t.entries.Count} entries");
    }
}

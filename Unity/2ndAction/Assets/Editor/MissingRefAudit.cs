using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 参照切れの監査(2026-11-25、Unity 6000.6.10f1 へ更新した時に画像の .meta が書き直されたため)。
//  Assets 以下の Prefab / ScriptableObject / シーンの、オブジェクトへの参照(ObjectReference)のうち、
//  「何かを指しているのに読み込めない」物を数えて書き出す。読むだけ(何も変えない)。
//  使い方: Unity -batchmode -quit -projectPath ... -executeMethod MissingRefAudit.Run -mraReport <path>
public static class MissingRefAudit
{
    public static void Run()
    {
        string report = Arg("-mraReport", "missing_refs.txt");
        var sb = new StringBuilder();
        int assets = 0, refs = 0, missing = 0, spriteRefs = 0;
        var paths = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab t:ScriptableObject", new[] { "Assets" })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        foreach (var p in paths)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (o == null) continue;
                assets++;
                Scan(new SerializedObject(o), p, sb, ref refs, ref missing, ref spriteRefs);
            }
        }
        // シーンは中の物を読み込まずに、YAML の参照先(guid)が存在するかだけ見る
        int sceneRefs = 0, sceneMissing = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            foreach (var line in File.ReadAllLines(p))
            {
                int i = line.IndexOf("guid: ");
                if (i < 0) continue;
                string guid = line.Substring(i + 6, 32);
                sceneRefs++;
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) { sceneMissing++; if (sceneMissing <= 20) sb.AppendLine($"SCENE {p}: unknown guid {guid}"); }
            }
        }
        sb.Insert(0, $"assets {assets}, object refs {refs} (sprites {spriteRefs}), MISSING {missing}; scene refs {sceneRefs}, scene unknown guids {sceneMissing}\n");
        File.WriteAllText(report, sb.ToString());
        Debug.Log("MissingRefAudit: " + sb.ToString().Split('\n')[0]);
    }

    static void Scan(SerializedObject so, string path, StringBuilder sb, ref int refs, ref int missing, ref int spriteRefs)
    {
        var it = so.GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (it.objectReferenceValue == null && !PointsSomewhere(it)) continue; // 空(何も指していない)
            refs++;
            if (it.objectReferenceValue is Sprite) spriteRefs++;
            if (it.objectReferenceValue == null)
            {
                missing++;
                if (missing <= 200) sb.AppendLine($"MISSING {path} :: {so.targetObject.name} . {it.propertyPath}");
            }
        }
    }

    // 参照が何かを指しているか(版で API の名前が違う: 6000.5 は objectReferenceInstanceIDValue、6000.6 は objectReferenceEntityIdValue)
    static System.Reflection.PropertyInfo idProp;
    static bool PointsSomewhere(SerializedProperty p)
    {
        if (idProp == null)
        {
            idProp = typeof(SerializedProperty).GetProperty("objectReferenceEntityIdValue") ?? typeof(SerializedProperty).GetProperty("objectReferenceInstanceIDValue");
        }
        object v = idProp.GetValue(p);
        if (v == null) return false;
        object zero = v.GetType().IsValueType ? System.Activator.CreateInstance(v.GetType()) : null;
        return !v.Equals(zero);
    }

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }
}

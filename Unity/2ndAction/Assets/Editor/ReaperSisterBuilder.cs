using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 死神三姉妹(2026-09-29)のデータ/Prefabを作る。
//  Resources/Reapers/<Eldest|Second|Youngest>Data.asset(ReaperSisterData: 絵・アニメ・追跡の調整値)
//  Resources/Reapers/Reaper<Eldest|Second|Youngest>.prefab(独立したPrefab: ReaperBaseの各サブクラス+ReaperAnimator+Visual/Body)
// 絵は Assets/Art/Reapers/<Eldest|Second|Youngest>/ の idle.png と move_00.png, move_01.png ...(右向き、透明背景)。
// データが既にあれば調整値は上書きせず、絵の参照だけ入れ直す(Inspectorで調整した値を消さない)。
public static class ReaperSisterBuilder
{
    const string ResDir = "Assets/Resources/Reapers";
    const string ArtDir = "Assets/Art/Reapers";

    [MenuItem("Tools/OneMoreMile/Build Reaper Sisters")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/Resources", "Reapers");
        BuildOne(ReaperSister.Eldest, "Eldest", "荒野街道の長女", "wasteland_road", ReaperMotion.Walk, typeof(ReaperEldest));
        BuildOne(ReaperSister.Second, "Second", "自然洞窟の次女", "natural_cave", ReaperMotion.Float, typeof(ReaperSecond));
        BuildOne(ReaperSister.Youngest, "Youngest", "天空回廊の三女", "sky_corridor", ReaperMotion.Skip, typeof(ReaperYoungest));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ReaperSisterBuilder] done");
    }

    static void BuildOne(ReaperSister sister, string key, string label, string stage, ReaperMotion motion, System.Type type)
    {
        string dataPath = $"{ResDir}/{key}Data.asset";
        var data = AssetDatabase.LoadAssetAtPath<ReaperSisterData>(dataPath);
        bool fresh = data == null;
        if (fresh)
        {
            data = ScriptableObject.CreateInstance<ReaperSisterData>();
            ApplyDefaults(data, sister);
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.sister = sister; data.displayName = label; data.stageId = stage; data.motion = motion;

        // 絵(あれば)
        string dir = $"{ArtDir}/{key}";
        if (AssetDatabase.IsValidFolder(dir))
        {
            data.idle = LoadSprite($"{dir}/idle.png");
            var frames = new List<Sprite>();
            for (int i = 0; i < 32; i++)
            {
                var s = LoadSprite($"{dir}/move_{i:00}.png");
                if (s == null) break;
                frames.Add(s);
            }
            data.moveFrames = frames.ToArray();
        }
        EditorUtility.SetDirty(data);

        // Prefab(毎回作り直す。調整値はデータ側にあるので消えない)
        string prefabPath = $"{ResDir}/Reaper{key}.prefab";
        var root = new GameObject("Reaper" + key);
        var comp = (ReaperBase)root.AddComponent(type);
        comp.data = data;
        root.AddComponent<ReaperAnimator>();
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        var body = new GameObject("Body");
        body.transform.SetParent(visual.transform, false);
        var sr = body.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;
        sr.sprite = data.moveFrames != null && data.moveFrames.Length > 0 ? data.moveFrames[0] : data.idle;
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[ReaperSisterBuilder] {key}: data={(fresh ? "new" : "kept")} idle={(data.idle != null)} moveFrames={data.moveFrames.Length} prefab={prefabPath}");
    }

    static void ApplyDefaults(ReaperSisterData d, ReaperSister s)
    {
        switch (s)
        {
            case ReaperSister.Eldest:
                d.heightWorld = 2.8f; d.moveFps = 5f; d.walkBob = 0.05f; d.walkSwayDeg = 1.0f;
                d.placeholderTint = new Color(1f, 0.82f, 0.9f);
                break;
            case ReaperSister.Second:
                d.heightWorld = 2.4f; d.moveFps = 4f; d.floatHeight = 0.35f; d.bobAmount = 0.12f; d.bobFrequency = 0.55f;
                d.placeholderTint = new Color(0.78f, 0.84f, 1f);
                break;
            default:
                d.heightWorld = 1.95f; d.moveFps = 8f; d.skipRate = 2.2f; d.skipHeight = 0.14f; d.skipFloatEvery = 4; d.skipFloatHeightMul = 2.4f; // 跳ねは控えめ(絵のコマ自体が上下に弾む)
                d.placeholderTint = new Color(1f, 0.95f, 0.78f);
                break;
        }
    }

    // 右向き・透明背景の全身絵。足元(下端中央)を基準点に。
    static Sprite LoadSprite(string path)
    {
        if (!File.Exists(path)) return null;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool dirty = imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single
                || imp.spritePivot != new Vector2(0.5f, 0f) || imp.npotScale != TextureImporterNPOTScale.None || imp.mipmapEnabled;
            if (dirty)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                var st = new TextureImporterSettings();
                imp.ReadTextureSettings(st);
                st.spriteAlignment = (int)SpriteAlignment.Custom;
                st.spritePivot = new Vector2(0.5f, 0f);
                st.spriteMeshType = SpriteMeshType.FullRect;
                imp.SetTextureSettings(st);
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.spritePixelsPerUnit = 512f;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}

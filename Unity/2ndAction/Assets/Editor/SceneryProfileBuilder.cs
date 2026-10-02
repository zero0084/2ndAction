using UnityEditor;
using UnityEngine;

// 荒野街道の「距離で進む昼夜と景色」(2026-10-01)の予定表と画像の取り込み設定を作る。
//  Tools/OneMoreMile/Build Scenery Profiles  (batch: -executeMethod SceneryProfileBuilder.Build)
// 画像: Assets/Resources/Scenery/Wasteland/scn_<景色>_<時間帯>.png(A昼だけは既存の Art/Background/WastelandBackground.png)
// 予定(既定): 1区間10,000m、A昼→A夕→A夜→A夜明け→B昼…→C夜明け(120,000m)→A昼へ戻る。境目の1,000m手前から移り変わる。
public static class SceneryProfileBuilder
{
    const string Dir = "Assets/Resources/Scenery";
    const string ImgDir = Dir + "/Wasteland";
    static readonly string[] Times = { "day", "evening", "night", "dawn" };

    [MenuItem("Tools/OneMoreMile/Build Scenery Profiles")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Scenery");
        if (!AssetDatabase.IsValidFolder(ImgDir)) AssetDatabase.CreateFolder(Dir, "Wasteland");

        string[] ids = { "A", "B", "C" };
        string[] labels = { "荒野と城の川辺", "川沿いの丘陵", "岩山の峠" };
        var sceneries = new SceneryProfile.Scenery[ids.Length];
        for (int s = 0; s < ids.Length; s++)
        {
            var sc = new SceneryProfile.Scenery { id = ids[s], label = labels[s], timeResources = new string[4] };
            for (int t = 0; t < 4; t++)
            {
                if (s == 0 && t == 0) { sc.timeResources[t] = ""; continue; } // A昼=ステージの元の背景
                string file = $"{ImgDir}/scn_{ids[s]}_{Times[t]}.png";
                if (System.IO.File.Exists(file))
                {
                    ConfigureImport(file);
                    sc.timeResources[t] = $"Scenery/Wasteland/scn_{ids[s]}_{Times[t]}";
                }
                else
                {
                    Debug.LogWarning($"[SceneryProfileBuilder] missing {file} (falls back to the stage background)");
                    sc.timeResources[t] = "";
                }
            }
            sceneries[s] = sc;
        }

        var segs = new SceneryProfile.Segment[ids.Length * 4];
        for (int s = 0; s < ids.Length; s++)
            for (int t = 0; t < 4; t++)
                segs[s * 4 + t] = new SceneryProfile.Segment { scenery = s, time = (SceneryTime)t, length = 10000f };

        string path = Dir + "/wasteland_road_scenery.asset";
        var p = AssetDatabase.LoadAssetAtPath<SceneryProfile>(path);
        bool create = p == null;
        if (create) p = ScriptableObject.CreateInstance<SceneryProfile>();
        p.stageId = "wasteland_road";
        p.sceneries = sceneries;
        p.segments = segs;
        p.loopFromSegment = 0;
        p.blendMeters = 1000f;
        p.mistPeakAlpha = 0.6f;
        p.mistColor = new Color(1f, 0.96f, 0.9f, 1f);
        p.prefetchMeters = 2500f;
        // 昼=ステージ既定の乗算(0.8,0.82,0.85: 前景を目立たせる控えめな落とし)。夜の絵は元から暗いので落としを弱める。
        p.timeTint = new[]
        {
            new Color(0f, 0f, 0f, 0f),
            new Color(0.84f, 0.83f, 0.84f, 1f),
            new Color(0.95f, 0.95f, 1f, 1f),
            new Color(0.86f, 0.86f, 0.9f, 1f),
        };
        p.cloudTint = new[] { Color.white, new Color(1f, 0.8f, 0.7f, 1f), new Color(0.5f, 0.58f, 0.82f, 1f), new Color(0.98f, 0.86f, 0.88f, 1f) };
        p.nightAmount = new[] { 0f, 0.3f, 1f, 0.45f };
        if (create) AssetDatabase.CreateAsset(p, path);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SceneryProfileBuilder] built {path}: {segs.Length} segments, loop {p.TotalLength:F0}m");
    }

    static void ConfigureImport(string file)
    {
        var ti = AssetImporter.GetAtPath(file) as TextureImporter;
        if (ti == null) { AssetDatabase.ImportAsset(file); ti = AssetImporter.GetAtPath(file) as TextureImporter; }
        if (ti == null) return;
        bool dirty = false;
        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (Mathf.Abs(ti.spritePixelsPerUnit - 1000f) > 0.01f) { ti.spritePixelsPerUnit = 1000f; dirty = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
        if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
        if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.Compressed) { ti.textureCompression = TextureImporterCompression.Compressed; dirty = true; }
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        if (settings.spriteAlignment != (int)SpriteAlignment.Center || settings.spriteMeshType != SpriteMeshType.FullRect)
        {
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
            dirty = true;
        }
        // Android: ASTC 6x6(1枚 約0.7MB。高さ941が4の倍数でないため、PCのDXT系では無圧縮になる=PCのみ1枚約6MB)
        var android = ti.GetPlatformTextureSettings("Android");
        if (!android.overridden || android.format != TextureImporterFormat.ASTC_6x6 || android.maxTextureSize != 2048)
        {
            android.overridden = true;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.maxTextureSize = 2048;
            ti.SetPlatformTextureSettings(android);
            dirty = true;
        }
        if (dirty) ti.SaveAndReimport();
    }
}

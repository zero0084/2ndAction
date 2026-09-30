using System.IO;
using UnityEditor;
using UnityEngine;

// ラストダンジョン(2026-09-30): 追加データの作成(batch用)。
//  ・Resources/LastDungeon/Glyphs の文字の絵をSpriteとして取り込む(キャップハイト=1単位: PPU 207、ピボット=下端中央)
//  ・Resources/LastDungeon/Fx の石板(PPU 100、中央)
//  ・Resources/LastDungeon/StaffCredits.asset(無ければ仮の表記で作る。あれば編集内容を残す)
//  ・LAST CORRIDORのEncounter Profileを作り直す(複合Formation/難易度の波に合わせた間隔)
// 文字の絵の元: Assets/Art/LastDungeon/Source/make_glyphs.py(Palatino Linotype Bold で描画) / make_cracked.py / make_fx.py
public static class LastDungeonBuild
{
    const string GlyphDir = "Assets/Resources/LastDungeon/Glyphs";
    const string FxDir = "Assets/Resources/LastDungeon/Fx";
    const string CreditsPath = "Assets/Resources/LastDungeon/StaffCredits.asset";
    public const float GlyphPpu = 207f;

    [MenuItem("Tools/OneMoreMile/Last Dungeon/Build Data (glyphs + credits + profile)")]
    public static void Run()
    {
        ImportGlyphs();
        ImportFx();
        EnsureCredits();
        EncounterProfileBuilder.BuildLastForce();
        var f = typeof(StageEncounterProfile).GetField("byStage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (f != null) f.SetValue(null, null);
        AssetDatabase.SaveAssets();
        Debug.Log("[LastDungeonBuild] done");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void ImportGlyphs()
    {
        if (!Directory.Exists(GlyphDir)) { Debug.LogWarning("[LastDungeonBuild] no glyph dir"); return; }
        int n = 0;
        foreach (var file in Directory.GetFiles(GlyphDir, "*.png"))
        {
            string path = file.Replace('\\', '/');
            if (ConfigureSprite(path, GlyphPpu, SpriteAlignment.BottomCenter, 512)) n++;
        }
        Debug.Log($"[LastDungeonBuild] glyph sprites configured: {n}");
    }

    static void ImportFx()
    {
        if (!Directory.Exists(FxDir)) return;
        foreach (var file in Directory.GetFiles(FxDir, "*.png"))
            ConfigureSprite(file.Replace('\\', '/'), 100f, SpriteAlignment.Center, 1024);
    }

    static bool ConfigureSprite(string path, float ppu, SpriteAlignment align, int maxSize)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { AssetDatabase.ImportAsset(path); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
        if (ti == null) return false;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single; // Sprite化はモードの設定も必須
        ti.spritePixelsPerUnit = ppu;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = maxSize;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)align;
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        ti.SaveAndReimport();
        return true;
    }

    static void EnsureCredits()
    {
        var existing = AssetDatabase.LoadAssetAtPath<StaffCreditsData>(CreditsPath);
        if (existing != null) { Debug.Log("[LastDungeonBuild] keep " + CreditsPath); return; }
        AssetDatabase.CreateAsset(StaffCreditsData.CreateDefault(), CreditsPath);
        Debug.Log("[LastDungeonBuild] created " + CreditsPath);
    }
}

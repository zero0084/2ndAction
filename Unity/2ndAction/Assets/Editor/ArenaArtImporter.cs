using UnityEditor;
using UnityEngine;

// 闘技場の本番の絵(Assets/Resources/Arena/、2026-10-06 ChatGPT 生成)の取り込み設定。置けば自動で掛かる。
//  arena_stands.png … 背景(観客席/壁/門)。左右がつながるように切ってある。大きさは ArenaStage がカメラに合わせる
//  arena_floor.png  … 床の断面の石積み(上下左右につながる)。1024px = 4m(ArenaStage が絵の幅で並べる)
public class ArenaArtImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Arena/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        bool floor = assetPath.EndsWith("arena_floor.png");
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = floor ? 256f : 100f;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = false;
        ti.wrapMode = floor ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Center;
        s.spriteMeshType = SpriteMeshType.FullRect; // Tiled 表示に必要
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 2048;
        ti.SetPlatformTextureSettings(android);
    }
}

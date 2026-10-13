using UnityEditor;
using UnityEngine;

// メカドラゴンの絵(Assets/Resources/Bosses/MechDragon/、2026-10-13 ChatGPT 生成)の取り込み設定。置けば自動で掛かる。
// 姿勢の絵(idle 等)は全コマ同じキャンバスで、下端の中央がピボット(大きさは bodyHeight から決まる)。
// fx_*(弾・爆弾・照準・爆発)は中心ピボット(大きさはコードで絵の幅から決める)。
public class MechDragonArtImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Bosses/MechDragon/";

    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith(Dir)) return;
        bool fx = System.IO.Path.GetFileName(p).StartsWith("fx_");
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 256f;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)(fx ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 1024;
        ti.SetPlatformTextureSettings(android);
    }
}

using UnityEditor;
using UnityEngine;

// ボスの攻撃の絵(Assets/Resources/Effects/Atk/<set>_<frame>.png、2026-10-11 ChatGPT 生成)の取り込み設定。置けば自動で掛かる。
// 同じ組のコマは同じ大きさ(切り出しの時に全コマを同じ範囲で切っている)。中心ピボット・四角いメッシュ(引き伸ばしても形がずれない)。
public class AttackArtImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Effects/Atk/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 512f;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Center;
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 1024;
        ti.SetPlatformTextureSettings(android);
    }
}

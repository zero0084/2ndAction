using UnityEditor;
using UnityEngine;

// 命中の衝撃のコマ(Assets/Resources/Effects/HitImpact/impact_NN.png、2026-10-17 アニメーション強化、ChatGPT 生成・黒い背景から切り出し)の取り込み設定。置けば自動で掛かる。
// 同じ組のコマは同じ大きさ(切り出しの時に全コマを同じ範囲で切っている)。中心ピボット・四角いメッシュ(引き伸ばしても形がずれない)。
public class HitImpactImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Effects/HitImpact/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
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
        s.spriteAlignment = (int)SpriteAlignment.Center;
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 256;
        ti.SetPlatformTextureSettings(android);
    }
}

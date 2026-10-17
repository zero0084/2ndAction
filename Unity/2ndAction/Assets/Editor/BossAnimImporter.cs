using UnityEditor;
using UnityEngine;

// ボスの姿勢ごとのコマ(Assets/Resources/BossAnim/<key>/<姿勢>_<番号>.png、2026-10-17 アニメーション強化)の取り込み設定。置けば自動で掛かる。
// 今の1枚絵(Art/WildBoss/*.png)と同じ PPU 100・下端中央ピボット。絵は待機の絵と同じピクセルの縮尺で、足元(地面の線)が下端。
// 格子(BossRig)で切るので四角いメッシュ。NPOT は引き伸ばさない(幅/高さが絵ごとに違うため)。
public class BossAnimImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/BossAnim/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.maxTextureSize = 2048;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 1024;
        ti.SetPlatformTextureSettings(android);
    }
}

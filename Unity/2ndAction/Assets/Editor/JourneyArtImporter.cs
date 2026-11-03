using UnityEditor;
using UnityEngine;

// 背景の旅の絵(Resources/Journey/<stageId>/、2026-11-03)を Sprite として取り込む。
//  内部の絵は横に並べるので端を伸ばさない(Clamp)。
//  圧縮は TextureImportRules(背景と同じ ASTC 5x5 / Windows は BC7。幅と高さは Tools/journey/key.py で4の倍数にそろえる)
//  ミップマップは付けない(2026-11-04): Unity 6.5 では 2のべき乗でない絵にミップマップを付けると ASTC を指定しても
//  無圧縮(RGBA32)に落ち、APK が 65MB 増えていた。目的地の縮小(0km で画面の高さの 12%)もミップマップ無しで見た目を確かめた
public class JourneyArtImporter : AssetPostprocessor
{
    public override uint GetVersion() => 2; // 設定を変えたら上げる(取り込み直しの合図)

    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith("Assets/Resources/Journey/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.spritePixelsPerUnit = 100f;
        if (ti.maxTextureSize < 2048) ti.maxTextureSize = 2048;
    }
}

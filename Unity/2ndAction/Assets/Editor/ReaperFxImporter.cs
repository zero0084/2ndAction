using UnityEditor;
using UnityEngine;

// 死神三姉妹の弾/効果の正式な絵(Assets/Resources/Effects/Reaper/*.png、2026-10-20、ChatGPT 生成・マゼンタ背景から切り出し)の取り込み設定。
// どれも正方形 = 1 ワールド単位(256px、魔法陣と顎は 512px)(手続きの仮の絵と同じ大きさ)。地面から生える牙(fangs*)だけ下端中央、他は中心
public class ReaperFxImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Effects/Reaper/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        string fn = System.IO.Path.GetFileName(assetPath);
        bool big = fn.StartsWith("ring") || fn.StartsWith("jaws"); // 細い線の魔法陣/大きな顎は 512px(これも 1 ワールド単位)
        ti.spritePixelsPerUnit = big ? 512f : 256f;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        bool bottom = System.IO.Path.GetFileName(assetPath).StartsWith("fangs");
        s.spriteAlignment = (int)(bottom ? SpriteAlignment.BottomCenter : SpriteAlignment.Center);
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = big ? 512 : 256;
        ti.SetPlatformTextureSettings(android);
    }
}

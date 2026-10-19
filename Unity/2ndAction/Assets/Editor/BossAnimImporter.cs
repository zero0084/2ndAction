using UnityEditor;
using UnityEngine;

// ボスの姿勢ごとのコマ(Assets/Resources/BossAnim/<key>/<姿勢>_<番号>.png、2026-10-17 アニメーション強化)の取り込み設定。置けば自動で掛かる。
// 今の1枚絵(Art/WildBoss/*.png)と同じ PPU 100・下端中央ピボット。絵は待機の絵と同じピクセルの縮尺で、足元(地面の線)が下端。
// 格子(BossRig)で切るので四角いメッシュ。NPOT は引き伸ばさない(幅/高さが絵ごとに違うため)。
public class BossAnimImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/BossAnim/";

    // 2026-10-19: 待機の絵の PPU がボスごとに違う(死神三姉妹は 512)。同じフォルダに ppu.txt(数字だけ)があればその値。無ければ 100
    // (ppu.txt を後から変えた時は、そのフォルダの絵を Reimport する)
    static float FolderPpu(string path)
    {
        try
        {
            string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "ppu.txt");
            if (System.IO.File.Exists(f) && float.TryParse(System.IO.File.ReadAllText(f).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 1f) return v;
        }
        catch { }
        return 100f;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = FolderPpu(assetPath);
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

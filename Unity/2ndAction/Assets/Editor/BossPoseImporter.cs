using UnityEditor;
using UnityEngine;

// ボスの姿勢の絵(Assets/Resources/BossPose/<ボス>/、2026-10-28 エステル生成)の取り込み設定。置けば自動で掛かる。
// 竜/魔人/天空のボスのように BossAnim(コマの連番)を持たないボスへ、技ごとの構え・攻撃・戻りの1枚絵を足す。
// 各フォルダの絵は、そのボスの今の絵と同じキャンバス・同じ大きさで置いてある(Tools/bosspose/fit.py)。
//  竜/魔人は中心、天空のボスは下端の中央がピボット(今の絵と同じ)。PPU はボスの今の絵に合わせる(竜 175.65 / 魔人 208.70 / 天空のボスは今の待機の絵と同じ)
public class BossPoseImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/BossPose/";

    public static float PpuFor(string path)
    {
        string p = path.Replace('\\', '/');
        if (p.Contains("/BossPose/Dragon/")) return 175.65218f;
        if (p.Contains("/BossPose/Majin/")) return 208.69565f;
        return 100f; // 天空のボス: キャンバスを今の絵と同じ大きさにしてあるので、今の絵の PPU(SkyBoss の .meta は 100)
    }

    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = PpuFor(p);
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)(p.Contains("/BossPose/Sky/") ? SpriteAlignment.BottomCenter : SpriteAlignment.Center); // 天空のボスは今の絵と同じ下端の中央
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.maxTextureSize = 1024;
        ti.SetPlatformTextureSettings(android);
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 画像の取り込みの決まり(Android、2026-10-18 容量の調査)。Assets/Art と Assets/Resources の画像に、置けば自動で掛かる。
//
// 調べて分かったこと: Android の既定の圧縮(ETC2)は「幅と高さが4の倍数」の画像しか圧縮できず、
// それ以外(今の絵の大半)は無圧縮(1画素 4バイト)のまま APK に入っていた(883枚・1.41GB)。
// ASTC はどの大きさでも圧縮できる → 用途ごとに ASTC のブロックの大きさを決める。
//   4x4(1画素 1バイト、一番きれい): キャラ・敵・ボス・UI・カード・アイコン(細かい絵、拡大して見る物)
//   5x5(0.64バイト): 背景・地面・飾り(画面いっぱいに出すので 6x6 より一段上)
//   6x6(0.44バイト): エフェクト・砂ぼこり(動きが速い・ぼかしが多い。今の専用の取り込みと同じ)
// Android 以外(Windows など)と、Default の設定(Max Size・PPU・ピボット)には触らない。
// 専用の取り込み(BossAnimImporter など)が決めている所は、そちらを優先する(下の Owned)。
// ASTC を使えない古い端末では、Unity が読み込み時に展開する(今の無圧縮と同じメモリ)。
public class TextureImportRules : AssetPostprocessor
{
    // 専用の取り込みが Android の設定まで決めている所(触らない)
    static readonly string[] Owned =
    {
        "Assets/Resources/BossAnim/", "Assets/Resources/Arena/", "Assets/Resources/Bosses/MechDragon/",
        "Assets/Resources/Effects/Atk/", "Assets/Resources/Effects/HitImpact/", "Assets/Resources/Scenery/",
    };
    static readonly string[] Background =
    {
        "Assets/Art/Background/", "Assets/Art/LastCorridor/", "Assets/Art/VisualStyleV1/Ground/", "Assets/Art/Ground/",
        "Assets/Art/Cave/", "Assets/Art/Decoration/", "Assets/Art/Obstacles_v1/",
    };
    static readonly string[] Effect =
    {
        "Assets/Art/Effects/", "Assets/Resources/Effects/", "Assets/Resources/LastDungeon/Fx/",
        "Assets/Art/JumpDust/", "Assets/Art/DoubleJumpDust/", "Assets/Art/LandDust", "Assets/Art/AscensionSmoke/",
    };
    // 表示がとても小さいのに元が大きい物: Android だけ上限を下げる(表示の約3倍を残す)
    static readonly Dictionary<string, int> AndroidMax = new Dictionary<string, int>
    {
        { "Assets/Resources/CardCategoryIcons/", 256 }, // 1254px の絵を、カードの上で最大でも約80pxで出している
    };

    public static bool Applies(string path)
    {
        path = path.Replace('\\', '/');
        if (!(path.StartsWith("Assets/Art/") || path.StartsWith("Assets/Resources/"))) return false;
        return !Owned.Any(path.StartsWith);
    }

    public static TextureImporterFormat FormatFor(string path)
    {
        path = path.Replace('\\', '/');
        if (Effect.Any(path.StartsWith)) return TextureImporterFormat.ASTC_6x6;
        if (Background.Any(path.StartsWith)) return TextureImporterFormat.ASTC_5x5;
        return TextureImporterFormat.ASTC_4x4;
    }

    void OnPreprocessTexture()
    {
        if (!Applies(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string path = assetPath.Replace('\\', '/');
        int max = ti.maxTextureSize; // 既定と同じ(縮めない)
        foreach (var kv in AndroidMax) if (path.StartsWith(kv.Key)) max = Mathf.Min(max, kv.Value);
        var android = ti.GetPlatformTextureSettings("Android");
        var format = FormatFor(path);
        if (android.overridden && android.format == format && android.maxTextureSize == max) return;
        android.overridden = true;
        android.format = format;
        android.maxTextureSize = max;
        ti.SetPlatformTextureSettings(android);
    }

    // 今ある画像へ当てる(新しく置いた画像は、取り込みの時に自動で掛かる)
    [MenuItem("Tools/2ndAction/Apply Texture Import Rules (Android)")]
    public static void ApplyAll()
    {
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art", "Assets/Resources" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => AssetImporter.GetAtPath(p) is TextureImporter && Applies(p)).ToList();
        int changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var p in paths)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var a = ti.GetPlatformTextureSettings("Android");
                int max = ti.maxTextureSize;
                foreach (var kv in AndroidMax) if (p.StartsWith(kv.Key)) max = Mathf.Min(max, kv.Value);
                if (a.overridden && a.format == FormatFor(p) && a.maxTextureSize == max) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                changed++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        Debug.Log($"TextureImportRules: {changed} of {paths.Count} textures re-imported with the Android rules");
    }
}

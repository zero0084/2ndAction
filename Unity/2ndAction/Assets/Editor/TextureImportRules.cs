using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 画像の取り込みの決まり(Android 2026-10-18 / Windows 2026-10-25 容量の調査)。Assets/Art と Assets/Resources の画像に、置けば自動で掛かる。
//
// 調べて分かったこと: Android の既定の圧縮(ETC2)は「幅と高さが4の倍数」の画像しか圧縮できず、
// それ以外(今の絵の大半)は無圧縮(1画素 4バイト)のまま APK に入っていた(883枚・1.41GB)。
// ASTC はどの大きさでも圧縮できる → 用途ごとに ASTC のブロックの大きさを決める。
//   4x4(1画素 1バイト、一番きれい): キャラ・敵・ボス・UI・カード・アイコン(細かい絵、拡大して見る物)
//   5x5(0.64バイト): 背景・地面・飾り(画面いっぱいに出すので 6x6 より一段上)
//   6x6(0.44バイト): エフェクト・砂ぼこり(動きが速い・ぼかしが多い。今の専用の取り込みと同じ)
// Default の設定(Max Size・PPU・ピボット)には触らない。Windows(Standalone)は下の「Windows」の決まり(BC7、2026-10-25)。
// 専用の取り込み(BossAnimImporter など)が決めている所は、そちらを優先する(下の Owned)。
// ASTC を使えない古い端末では、Unity が読み込み時に展開する(今の無圧縮と同じメモリ)。
public class TextureImportRules : AssetPostprocessor
{
    // 専用の取り込みが Android の設定まで決めている所(触らない)
    static readonly string[] Owned =
    {
        "Assets/Resources/BossAnim/", "Assets/Resources/Arena/", "Assets/Resources/Bosses/MechDragon/", "Assets/Resources/BossPose/",
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

    // ---------------------------------------------------------------- Windows(Standalone、2026-10-25 v1.0 容量監査)
    // Windows の GPU 圧縮(BC7)も 4x4 のブロック単位で、幅・高さが4の倍数でない画像は無圧縮(1画素 4バイト)に落ちていた
    // (1,409枚・3.4GB。Windows 版のフォルダ 3.87GB の大半)。→ 画像を4の倍数へそろえ(Tools/art/pad4.py、透明な画素を足すだけ)、BC7 にする。
    //  ・BC7: DXT5 より色の段差が少なく、Android の ASTC 4x4 とほぼ同じ見た目(2倍に拡大して比べた)。1画素 1バイト
    //  ・4の倍数でない画像には付けない(BC7 を指定すると RGBA32 に落ちる。指定しなければ不透明な物は RGB24 のまま)
    //  ・専用の取り込み(Owned)の画像にも付ける(Android は今までどおり各取り込みが決める)。起動ロゴ(Brand)は除く
    //  ・Max Size は Android と同じ(AndroidMax の縮小も同じに掛ける)
    public static bool StandaloneApplies(string path)
    {
        path = path.Replace('\\', '/');
        return (path.StartsWith("Assets/Art/") || path.StartsWith("Assets/Resources/")) && !path.StartsWith("Assets/Art/Brand/");
    }

    static int MaxFor(TextureImporter ti, string path)
    {
        int max = ti.maxTextureSize;
        foreach (var kv in AndroidMax) if (path.StartsWith(kv.Key)) max = Mathf.Min(max, kv.Value);
        return max;
    }

    // この画像の Windows の設定があるべき形か(true = 直す必要なし)
    public static bool StandaloneOk(TextureImporter ti, string path)
    {
        var pc = ti.GetPlatformTextureSettings("Standalone");
        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
        bool blockOk = w % 4 == 0 && h % 4 == 0;
        if (!blockOk) return !pc.overridden;
        return pc.overridden && pc.format == TextureImporterFormat.BC7 && pc.maxTextureSize == MaxFor(ti, path);
    }

    static void ApplyStandalone(TextureImporter ti, string path)
    {
        if (StandaloneOk(ti, path)) return;
        var pc = ti.GetPlatformTextureSettings("Standalone");
        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
        if (w % 4 != 0 || h % 4 != 0) { pc.overridden = false; ti.SetPlatformTextureSettings(pc); return; }
        pc.overridden = true;
        pc.format = TextureImporterFormat.BC7;
        pc.maxTextureSize = MaxFor(ti, path);
        pc.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SetPlatformTextureSettings(pc);
    }

    void OnPreprocessTexture()
    {
        var ti = (TextureImporter)assetImporter;
        string path = assetPath.Replace('\\', '/');
        if (StandaloneApplies(path)) ApplyStandalone(ti, path);
        if (!Applies(assetPath)) return;
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
    [MenuItem("Tools/2ndAction/Apply Texture Import Rules (Android + Windows)")]
    public static void ApplyAll()
    {
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art", "Assets/Resources" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => AssetImporter.GetAtPath(p) is TextureImporter && (Applies(p) || StandaloneApplies(p))).ToList();
        int changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var p in paths)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (StandaloneApplies(p) && !StandaloneOk(ti, p)) { AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); changed++; continue; }
                if (!Applies(p)) continue;
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

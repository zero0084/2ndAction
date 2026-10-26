using UnityEditor;
using UnityEngine;

// 起動ロゴ(方法A、2026-10-24): Unity の起動画面の仕組みで MIXGame のロゴを出す。Unity のロゴは出さない。
// 起動直後の読み込みの間(約3〜5秒の黒い画面だった所)にロゴを出すので、ゲーム開始までの時間をほとんど増やさない。
//  ・ロゴは「背景の絵」として登録する(横画面用 1920x1080 / 縦画面用 1080x1920、中央に C案のロゴ、ほぼ黒の濃紺)。
//    ロゴの欄(logos)に登録すると、ロゴを出し終わってからシーンの読み込みが始まり、ロゴの後に約2秒の無地の画面が残った。
//    背景の絵にすると読み込みと同時に進み、ロゴの後の無地の時間が短い(この PC で 約0.5〜1秒)
//  ・背景の絵のぼかし(Unity の既定は ON)は切る(ロゴがぼける)
//  ・背景色はロゴの絵と同じ #060810(画面の比率が違って絵の外に出る所も同じ色)
// メニュー Tools/2ndAction/Apply Brand Splash(方法A)。バッチ: -executeMethod BrandSplashSetup.Apply
//
// 2026-10-26 Android は方法B(ゲーム側で描く BootSplash)へ: Android の実機でロゴがほとんど見えなかった。
//  Unity の起動画面は「最初のシーンの読み込みが終わった時点」で終わり(ロゴの欄が空なので最低の時間が無い)、
//  背景の絵は読み込みが済んでから描かれる → 読み込みの速さ(端末・ファイルのキャッシュ)で、見える時間が 0〜一瞬に変わる。
//  → Android のビルドの間だけ背景の絵を外し(背景色 #060810 だけの起動画面にする)、ロゴは BootSplash が毎回描く(AndroidScope)。
//    絵を残すと、速い端末で Unity のロゴが一瞬出てから BootSplash のロゴがもう一度出る(二重)ため。Windows は今までどおり方法A。
//  ゲーム側のロゴの絵は Resources/Brand/MIXGame_Logo(ApplyGameLogo で取り込みの設定)
public static class BrandSplashSetup
{
    public const string LandscapePath = "Assets/Art/Brand/MIXGame_SplashBg_Landscape.png";
    public const string PortraitPath = "Assets/Art/Brand/MIXGame_SplashBg_Portrait.png";
    public static readonly Color Background = new Color(6f / 255f, 8f / 255f, 16f / 255f, 1f);
    public const string GameLogoPath = "Assets/Resources/Brand/MIXGame_Logo.png";

    // ゲーム側のロゴ(BootSplash)の取り込み: 2のべき乗へ引き伸ばさない(縦横比を保つ)・縮小してもきれいなようにミップマップ・端は透明のまま
    // 圧縮は TextureImportRules(Android ASTC 4x4 / Windows BC7。絵は 744x576 で4の倍数)
    [MenuItem("Tools/2ndAction/Apply Boot Logo Import (MIXGame, method B)")]
    public static void ApplyGameLogo()
    {
        AssetDatabase.ImportAsset(GameLogoPath, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(GameLogoPath) as TextureImporter;
        if (ti == null) { Debug.LogError("BrandSplashSetup: not found: " + GameLogoPath); return; }
        ti.textureType = TextureImporterType.Default;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = true;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.maxTextureSize = 1024;
        ti.SaveAndReimport();
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(GameLogoPath);
        Debug.Log($"BrandSplashSetup: game logo {t.width}x{t.height} mip={t.mipmapCount}");
    }

    // Android のビルドの間だけ、Unity の起動画面の背景の絵を外す(背景色だけにする)。終わったら必ず元へ戻す(using で囲む)
    public sealed class AndroidScope : System.IDisposable
    {
        readonly Sprite land, port;
        public AndroidScope()
        {
            land = PlayerSettings.SplashScreen.background; port = PlayerSettings.SplashScreen.backgroundPortrait;
            PlayerSettings.SplashScreen.background = null;
            PlayerSettings.SplashScreen.backgroundPortrait = null;
            Debug.Log($"BrandSplashSetup: Android build - Unity splash shows the background color only (logo is drawn by BootSplash). was bg={(land != null)} portrait={(port != null)}");
        }
        public void Dispose()
        {
            PlayerSettings.SplashScreen.background = land;
            PlayerSettings.SplashScreen.backgroundPortrait = port;
            AssetDatabase.SaveAssets();
        }
    }

    static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError("BrandSplashSetup: not found: " + path); return null; }
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.CompressedHQ; // 暗い無地 + ロゴ。圧縮しても目立たない(容量を抑える)
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    [MenuItem("Tools/2ndAction/Apply Brand Splash (MIXGame, method A)")]
    public static void Apply()
    {
        var land = ImportSprite(LandscapePath);
        var port = ImportSprite(PortraitPath);
        if (land == null || port == null) return;
        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];
        PlayerSettings.SplashScreen.background = land;
        PlayerSettings.SplashScreen.backgroundPortrait = port;
        PlayerSettings.SplashScreen.blurBackgroundImage = false;
        PlayerSettings.SplashScreen.backgroundColor = Background;
        PlayerSettings.SplashScreen.overlayOpacity = 0f;
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
        AssetDatabase.SaveAssets();
        Debug.Log($"BrandSplashSetup: show={PlayerSettings.SplashScreen.show} unityLogo={PlayerSettings.SplashScreen.showUnityLogo} logos={PlayerSettings.SplashScreen.logos.Length} bg={land.texture.width}x{land.texture.height} bgPortrait={port.texture.width}x{port.texture.height} blur={PlayerSettings.SplashScreen.blurBackgroundImage} color={ColorUtility.ToHtmlStringRGB(Background)}");
    }
}

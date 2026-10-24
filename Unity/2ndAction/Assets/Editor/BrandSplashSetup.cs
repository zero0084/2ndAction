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
public static class BrandSplashSetup
{
    public const string LandscapePath = "Assets/Art/Brand/MIXGame_SplashBg_Landscape.png";
    public const string PortraitPath = "Assets/Art/Brand/MIXGame_SplashBg_Portrait.png";
    public static readonly Color Background = new Color(6f / 255f, 8f / 255f, 16f / 255f, 1f);

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

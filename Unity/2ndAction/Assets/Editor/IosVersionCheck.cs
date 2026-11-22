using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// IosVersion の手元の確認(2026-11-15)。iOS の Xcode プロジェクトを書き出すだけ(Mac での compile/署名はしない)。
//  "Unity.exe -batchmode -buildTarget iOS -projectPath ... -executeMethod IosVersionCheck.Run -quit"
//  ・出力: Builds/iOSVersionCheck(試験用。配らない)
//  ・見ること: ビルドが成功する(以前は Postprocess built player で版の形の例外)/ Info.plist の CFBundleShortVersionString = IosVersion.MarketingVersion /
//    CFBundleVersion = IosVersion.BuildNumber(2026-11-15 夜)/ Bundle ID(Xcode の PRODUCT_BUNDLE_IDENTIFIER)/ GADApplicationIdentifier = 設定の iOS 欄 /
//    ビルドの後に PlayerSettings.bundleVersion・iOS のビルド番号が元の値へ戻っている / Android の versionCode・Bundle ID が変わっていない
public static class IosVersionCheck
{
    public const string ExpectedBundleId = "com.zero0084.action2nd";

    public static void Run()
    {
        const string outPath = "Builds/iOSVersionCheck";
        string before = PlayerSettings.bundleVersion, buildNo = PlayerSettings.iOS.buildNumber;
        int androidCode = PlayerSettings.Android.bundleVersionCode;
        string iosId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS), androidId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
        Debug.Log($"[IosVersionCheck] before: bundleVersion='{before}' (valid for iOS: {IosVersion.IsValid(before)}) iOS buildNumber={buildNo} iOS id={iosId} Android versionCode={androidCode}");
        var r = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.iOS, BuildOptions.None);
        string plist = Path.Combine(outPath, "Info.plist");
        string shortVer = "(none)", bundleVer = "(none)", gadId = "(none)", pbxId = "(none)";
        if (File.Exists(plist))
        {
            string t = File.ReadAllText(plist);
            var m = Regex.Match(t, @"<key>CFBundleShortVersionString</key>\s*<string>([^<]*)</string>"); if (m.Success) shortVer = m.Groups[1].Value;
            var b = Regex.Match(t, @"<key>CFBundleVersion</key>\s*<string>([^<]*)</string>"); if (b.Success) bundleVer = b.Groups[1].Value;
            // 2026-11-15: GoogleMobileAdsSettings の iOS アプリ ID(今は Google 公式のテスト用)が Info.plist へ入ったか
            var g = Regex.Match(t, @"<key>GADApplicationIdentifier</key>\s*<string>([^<]*)</string>"); if (g.Success) gadId = g.Groups[1].Value;
        }
        string pbx = Path.Combine(outPath, "Unity-iPhone.xcodeproj/project.pbxproj");
        if (File.Exists(pbx))
        {
            // アプリ本体の設定の PRODUCT_BUNDLE_IDENTIFIER(Info.plist は $(PRODUCT_BUNDLE_IDENTIFIER) を参照する)
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (Match x in Regex.Matches(File.ReadAllText(pbx), @"PRODUCT_BUNDLE_IDENTIFIER = ""?([^;""]+)""?;")) ids.Add(x.Groups[1].Value);
            pbxId = string.Join(",", ids);
        }
        // 2026-11-22 iOS の広告: アプリ ID は本番(MonetizationSetup.ProdIosAppId)/ SKAdNetwork の一覧が入る / ATT の文言(NSUserTrackingUsageDescription)は入れない /
        //  Podfile に Google Mobile Ads と UMP / この iOS ビルドの広告ユニットはテスト用(OMM_ADS_PROD を iOS の定義に足していない)
        string pt = File.Exists(plist) ? File.ReadAllText(plist) : "";
        bool skad = pt.Contains("<key>SKAdNetworkItems</key>"), att = pt.Contains("NSUserTrackingUsageDescription");
        string pod = Path.Combine(outPath, "Podfile");
        string podText = File.Exists(pod) ? File.ReadAllText(pod) : "";
        bool pods = podText.Contains("Google-Mobile-Ads-SDK") && podText.Contains("GoogleUserMessagingPlatform");
        bool iosTestAds = !MonetizationConfig.UseProductionAds && MonetizationConfig.UnitsLabel == "ios-test"
                          && MonetizationConfig.RewardedUnit == MonetizationConfig.IosTestRewardedUnit && MonetizationConfig.InterstitialUnit == MonetizationConfig.IosTestInterstitialUnit
                          && !PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.iOS).Contains("OMM_ADS_PROD");
        bool adsOk = gadId == MonetizationSetup.ProdIosAppId && skad && !att && pods && iosTestAds;
        Debug.Log($"[IosVersionCheck] ads: GADApplicationIdentifier='{gadId}' (prod iOS app id: {gadId == MonetizationSetup.ProdIosAppId}) SKAdNetworkItems={skad} NSUserTrackingUsageDescription={att} Podfile(GMA+UMP)={pods} units={MonetizationConfig.UnitsLabel} rewarded={MonetizationConfig.RewardedUnit} interstitial={MonetizationConfig.InterstitialUnit} iOS defines='{PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.iOS)}' -> {(adsOk ? "OK" : "NG")}");
        string after = PlayerSettings.bundleVersion;
        bool idOk = pbxId.Split(',').Length >= 1 && System.Array.IndexOf(pbxId.Split(','), ExpectedBundleId) >= 0;
        bool ok = r.summary.result == BuildResult.Succeeded && r.summary.totalErrors == 0
                  && shortVer == IosVersion.MarketingVersion && bundleVer == IosVersion.BuildNumber
                  && after == before && PlayerSettings.iOS.buildNumber == buildNo
                  && iosId == ExpectedBundleId && idOk
                  && PlayerSettings.Android.bundleVersionCode == androidCode && PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) == androidId
                  && !string.IsNullOrEmpty(GmaIosAppId()) && gadId == GmaIosAppId() && adsOk;
        Debug.Log($"[IosVersionCheck] result={r.summary.result} errors={r.summary.totalErrors} warnings={r.summary.totalWarnings} Info.plist CFBundleShortVersionString='{shortVer}' CFBundleVersion='{bundleVer}' GADApplicationIdentifier='{gadId}' PRODUCT_BUNDLE_IDENTIFIER='{pbxId}' | after: bundleVersion='{after}' (restored={after == before}) iOS buildNumber={PlayerSettings.iOS.buildNumber} (restored={PlayerSettings.iOS.buildNumber == buildNo}) Android versionCode={PlayerSettings.Android.bundleVersionCode} -> {(ok ? "OK" : "NG")}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
    }

    // GoogleMobileAdsSettings の iOS 欄(Info.plist の GADApplicationIdentifier になる値)
    static string GmaIosAppId()
    {
        const string p = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
        if (!File.Exists(p)) return null;
        foreach (var l in File.ReadAllLines(p)) { var t = l.Trim(); if (t.StartsWith("adMobIOSAppId:")) return t.Substring("adMobIOSAppId:".Length).Trim(); }
        return null;
    }
}

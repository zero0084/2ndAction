using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// IosVersion の手元の確認(2026-11-15)。iOS の Xcode プロジェクトを書き出すだけ(Mac での compile/署名はしない)。
//  "Unity.exe -batchmode -buildTarget iOS -projectPath ... -executeMethod IosVersionCheck.Run -quit"
//  ・ビルド番号は変えない(BuildIOS.Build と違い +1 しない)。出力: Builds/iOSVersionCheck(試験用。配らない)
//  ・見ること: ビルドが成功する(以前は Postprocess built player で版の形の例外)/ Info.plist の CFBundleShortVersionString = IosVersion.MarketingVersion /
//    ビルドの後に PlayerSettings.bundleVersion が元の値(Android/Windows の版)へ戻っている / ビルド番号が変わっていない
public static class IosVersionCheck
{
    public static void Run()
    {
        const string outPath = "Builds/iOSVersionCheck";
        string before = PlayerSettings.bundleVersion, buildNo = PlayerSettings.iOS.buildNumber;
        Debug.Log($"[IosVersionCheck] before: bundleVersion='{before}' (valid for iOS: {IosVersion.IsValid(before)}) iOS buildNumber={buildNo}");
        var r = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.iOS, BuildOptions.None);
        string plist = Path.Combine(outPath, "Info.plist");
        string shortVer = "(none)", bundleVer = "(none)";
        if (File.Exists(plist))
        {
            string t = File.ReadAllText(plist);
            var m = Regex.Match(t, @"<key>CFBundleShortVersionString</key>\s*<string>([^<]*)</string>"); if (m.Success) shortVer = m.Groups[1].Value;
            var b = Regex.Match(t, @"<key>CFBundleVersion</key>\s*<string>([^<]*)</string>"); if (b.Success) bundleVer = b.Groups[1].Value;
        }
        string after = PlayerSettings.bundleVersion;
        bool ok = r.summary.result == BuildResult.Succeeded && shortVer == IosVersion.MarketingVersion && after == before && PlayerSettings.iOS.buildNumber == buildNo;
        Debug.Log($"[IosVersionCheck] result={r.summary.result} errors={r.summary.totalErrors} Info.plist CFBundleShortVersionString='{shortVer}' CFBundleVersion='{bundleVer}' | after: bundleVersion='{after}' (restored={after == before}) buildNumber={PlayerSettings.iOS.buildNumber} -> {(ok ? "OK" : "NG")}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
    }
}

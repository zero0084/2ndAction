using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildIOS
{
    const string OutputPath = "Builds/iOS";

    [MenuItem("Tools/2ndAction/Build iOS Xcode Project")]
    public static void Build()
    {
        if (!Directory.Exists(OutputPath)) Directory.CreateDirectory(OutputPath);

        // iOS の版(CFBundleShortVersionString)は IosVersion(ビルドの直前に MarketingVersion = App Store の形へ差し替え、後で戻す)が決める。
        // 2026-11-15: 以前はここで日付の版(yyyyMMdd.HHmm)を PlayerSettings.bundleVersion へ書いていた
        //  (Android/Windows と共通の値を書き換える・App Store の版として不自然)→ やめた。ビルド番号を進めるのは今までどおり
        int buildNumber = int.Parse(PlayerSettings.iOS.buildNumber == "" ? "0" : PlayerSettings.iOS.buildNumber) + 1;
        PlayerSettings.iOS.buildNumber = buildNumber.ToString();
        Debug.Log("BuildIOS: version " + IosVersion.MarketingVersion + " (IosVersion) buildNumber " + buildNumber);

        // On Windows this only emits the Xcode project source (source files,
        // no compiled/signed app) - actually compiling and signing into an
        // .ipa requires Xcode running on macOS. Copy the OutputPath folder to
        // a Mac (or a rented cloud Mac) and open Unity-iPhone.xcodeproj there.
        BuildReport report = BuildPipeline.BuildPlayer(
            EditorBuildSettings.scenes,
            OutputPath,
            BuildTarget.iOS,
            BuildOptions.None);

        Debug.Log("BuildIOS: result=" + report.summary.result +
                   " size=" + report.summary.totalSize +
                   " path=" + report.summary.outputPath);
    }
}

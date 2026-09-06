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

        // iOS requires CFBundleShortVersionString to be digits and dots only
        // (no letters, spaces, or dashes), unlike Android's free-form
        // versionName - so this uses a different stamp format than
        // BuildAndroid, but still traces back to an exact build.
        string stamp = System.DateTime.Now.ToString("yyyyMMdd.HHmm");
        PlayerSettings.bundleVersion = stamp;
        int buildNumber = int.Parse(PlayerSettings.iOS.buildNumber == "" ? "0" : PlayerSettings.iOS.buildNumber) + 1;
        PlayerSettings.iOS.buildNumber = buildNumber.ToString();
        Debug.Log("BuildIOS: stamping version " + stamp + " buildNumber " + buildNumber);

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

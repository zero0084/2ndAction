using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAndroid
{
    const string OutputPath = "Builds/Android/2ndAction.apk";

    [MenuItem("Tools/2ndAction/Build Android APK")]
    public static void Build()
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Stamp every build with a unique, human-readable version and an
        // ever-increasing version code, so it's always possible to tell
        // exactly which build is installed on a device (via the on-screen
        // build tag in GameManager) instead of guessing from behavior.
        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd HHmm");
        PlayerSettings.bundleVersion = stamp;
        PlayerSettings.Android.bundleVersionCode += 1;
        Debug.Log("BuildAndroid: stamping version " + stamp + " code " + PlayerSettings.Android.bundleVersionCode);

        // マルチプレイ対応Phase 1(2026-09-25) - LAN内のUDP通信(Unity Transport)に必要な
        // INTERNET権限を常に付ける(「Auto」だとWebRequest等を使わない限り付与されない)。
        // インターネット接続そのものは不要 - 同一Wi-Fi/テザリング内の直接通信でも権限は要る。
        PlayerSettings.Android.forceInternetPermission = true;
        NetPrefabBuilder.Build();

        // Distance Level Design Ver.1 - BuildOptions.Development so
        // Debug.isDebugBuild is true on-device, which is what gates the new
        // Distance Warp debug UI (GameManager.DrawDistanceWarpDebugUI) -
        // "Editor / Development Build限定で" from the brief, and this
        // project's own APKs are how the user actually tests on real
        // hardware, so a Release-style build here would ship with no way
        // to reach that UI at all. Switch back to BuildOptions.None for an
        // actual release build later - this is a real behavior change
        // (larger build, profiler/debugger connectable), not just a flag.
        BuildReport report = BuildPipeline.BuildPlayer(
            EditorBuildSettings.scenes,
            OutputPath,
            BuildTarget.Android,
            BuildOptions.Development);

        Debug.Log("BuildAndroid: result=" + report.summary.result +
                   " size=" + report.summary.totalSize +
                   " path=" + report.summary.outputPath);
    }
}

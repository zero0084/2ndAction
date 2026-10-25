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

    // ストア用の AAB(2026-10-25 v1.0 容量監査、まだ使っていない。公開・署名・番号はマスターが決める)
    //  ・製品版(Development なし)、Split Application Binary = ON(中身を install-time asset pack へ。base を 200MB 未満にする:
    //    分けないと base が約 396MB で Google Play の上限 200MB を超える。分けると base 約 168MB)
    //  ・番号は進めない / 署名の設定は今の PlayerSettings のまま(鍵は触らない)。出力: Builds/Store/OneMoreMile.aab
    [MenuItem("Tools/2ndAction/Build Android AAB (Store, split)")]
    public static void BuildStoreAab()
    {
        const string outPath = "Builds/Store/OneMoreMile.aab";
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        bool wasAab = EditorUserBuildSettings.buildAppBundle, wasSplit = PlayerSettings.Android.splitApplicationBinary;
        PlayerSettings.Android.forceInternetPermission = true;
        NetPrefabBuilder.Build();
        try
        {
            EditorUserBuildSettings.buildAppBundle = true;
            PlayerSettings.Android.splitApplicationBinary = true;
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.Android, BuildOptions.None);
            Debug.Log("BuildAndroid: store aab result=" + report.summary.result + " path=" + report.summary.outputPath);
        }
        finally { EditorUserBuildSettings.buildAppBundle = wasAab; PlayerSettings.Android.splitApplicationBinary = wasSplit; AssetDatabase.SaveAssets(); }
    }

    // 容量の調査(2026-10-18): 番号を進めずに、指定の場所へ APK / AAB を作る(比較用。配布しない)
    //   -executeMethod BuildAndroid.BuildProbe -probeOut Builds/SizeProbe/x.apk|x.aab
    public static void BuildProbe()
    {
        string outPath = "Builds/SizeProbe/probe.apk";
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-probeOut") outPath = a[i + 1];
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        bool aab = outPath.EndsWith(".aab");
        bool was = EditorUserBuildSettings.buildAppBundle;
        // 2026-10-25 v1.0 容量監査: -probeSplit 1 = Split Application Binary(AAB の中身を install-time asset pack へ分ける。
        // Google Play は base モジュールのダウンロードを 200MB までしか受け付けないため、ストアの AAB はこれが要る)
        bool split = false;
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-probeSplit") split = a[i + 1] == "1";
        bool wasSplit = PlayerSettings.Android.splitApplicationBinary;
        PlayerSettings.Android.forceInternetPermission = true;
        NetPrefabBuilder.Build();
        try
        {
            EditorUserBuildSettings.buildAppBundle = aab;
            if (split) PlayerSettings.Android.splitApplicationBinary = true;
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.Android, BuildOptions.Development);
            Debug.Log("BuildAndroid: probe result=" + report.summary.result + " size=" + report.summary.totalSize + " path=" + report.summary.outputPath + " split=" + split);
        }
        finally { EditorUserBuildSettings.buildAppBundle = was; PlayerSettings.Android.splitApplicationBinary = wasSplit; AssetDatabase.SaveAssets(); }
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// ストア素材(Steam のスクリーンショット/紹介動画)の撮影用 Windows ビルド(2026-10-23)。製品用ではない(バージョン番号も変更しない)。
// 開発版(Development Build)にすると Unity が画面の右下に「Development Build」を必ず描くので、開発版にはしない。
// 代わりに、このビルドだけ記号を足す:
//   DEVELOPMENT_BUILD … 撮影の進行役(QaSweep -storeCapture)と開発用の道具をコンパイルに含める
//   OMM_CAPTURE       … DevGate.On を true にする(距離ワープ等。実行中の Debug.isDebugBuild は false のまま)
// 製品のビルド(BuildAndroidApk など)はこの記号を付けないので、撮影の仕組みは入らない。
// 出力: Builds/StoreCapture/OneMoreMile.exe(1920x1080 のウィンドウ)
public static class BuildStoreCapture
{
    const string OutputPath = "Builds/StoreCapture/OneMoreMile.exe";

    [MenuItem("Tools/2ndAction/Build Windows Store Capture")]
    public static void Build()
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        NetPrefabBuilder.Build();

        FullScreenMode prevMode = PlayerSettings.fullScreenMode;
        int prevW = PlayerSettings.defaultScreenWidth, prevH = PlayerSettings.defaultScreenHeight;
        bool prevRunInBackground = PlayerSettings.runInBackground;
        try
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "DEVELOPMENT_BUILD", "OMM_CAPTURE" },
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("BuildStoreCapture: result=" + report.summary.result + " path=" + report.summary.outputPath);
        }
        finally
        {
            PlayerSettings.fullScreenMode = prevMode;
            PlayerSettings.defaultScreenWidth = prevW;
            PlayerSettings.defaultScreenHeight = prevH;
            PlayerSettings.runInBackground = prevRunInBackground;
            AssetDatabase.SaveAssets();
        }
    }
}

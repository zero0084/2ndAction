using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - PC上でHOST(エディタ)とJOIN(このビルド)の2つを
// 同時に動かして通信を確認するための、ウィンドウ表示のWindowsビルド。
// Android実機が1台しか無い時の開発用。製品用ではない(バージョン番号も変更しない)。
public static class BuildWindowsNetTest
{
    const string OutputPath = "Builds/WindowsNetTest/OneMoreMile.exe";

    [MenuItem("Tools/2ndAction/Build Windows Net Test Client")]
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
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            BuildReport report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, OutputPath, BuildTarget.StandaloneWindows64, BuildOptions.Development);
            Debug.Log("BuildWindowsNetTest: result=" + report.summary.result + " path=" + report.summary.outputPath);
        }
        finally
        {
            PlayerSettings.fullScreenMode = prevMode;
            PlayerSettings.defaultScreenWidth = prevW;
            PlayerSettings.defaultScreenHeight = prevH;
            PlayerSettings.runInBackground = prevRunInBackground;
            // ビルド中に一時設定のままProjectSettings.assetへ書き出されるため、戻した値で保存し直す。
            AssetDatabase.SaveAssets();
        }
    }
}

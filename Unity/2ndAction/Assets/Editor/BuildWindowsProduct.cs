using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 通常の Windows 製品ビルド(2026-10-23、Steam 向けの確認用)。開発版にしない・記号を足さない・設定を変えない。
// 撮影用ビルド(BuildStoreCapture)の機能(撮影の進行役/距離ワープ/無敵/開発用の表示)が入っていないことの確認に使う。
// 出力: Builds/WindowsProduct/OneMoreMile.exe(バージョン番号は変更しない)
public static class BuildWindowsProduct
{
    const string OutputPath = "Builds/WindowsProduct/OneMoreMile.exe";

    [MenuItem("Tools/2ndAction/Build Windows Product")]
    public static void Build()
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        NetPrefabBuilder.Build();
        var opts = new BuildPlayerOptions
        {
            scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BuildWindowsProduct: result=" + report.summary.result + " path=" + report.summary.outputPath);
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 製品版と同じ中身の Windows ビルドを、本物の保存と混ざらない名前で作る(2026-10-24、起動ロゴの確認用)。
// PlayerPrefs(レジストリ HKCU\Software\<会社名>\<製品名>)と保存のフォルダ(LocalLow\<会社名>\<製品名>)は製品名で分かれるので、
// 製品名だけをビルドの間「One More Mile Test」にして、終わったら必ず元へ戻す(会社名・パッケージ名は変えない)。
// 出力: Builds/WindowsProductSandbox/OneMoreMile.exe
public static class BuildWindowsProductSandbox
{
    public const string SandboxProductName = "One More Mile Test";
    const string OutputPath = "Builds/WindowsProductSandbox/OneMoreMile.exe";

    [MenuItem("Tools/2ndAction/Build Windows Product (Sandbox save)")]
    public static void Build()
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string original = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = SandboxProductName;
            NetPrefabBuilder.Build();
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("BuildWindowsProductSandbox: result=" + report.summary.result + " path=" + report.summary.outputPath + " product=" + SandboxProductName);
        }
        finally
        {
            PlayerSettings.productName = original;
            AssetDatabase.SaveAssets();
            Debug.Log("BuildWindowsProductSandbox: product name restored to '" + PlayerSettings.productName + "'");
        }
    }
}

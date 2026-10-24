using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 製品版と同じ中身の Windows ビルドを、本物の保存と混ざらない名前で作る(2026-10-24、起動ロゴの確認用)。
// PlayerPrefs(レジストリ HKCU\Software\<会社名>\<製品名>)と保存のフォルダ(LocalLow\<会社名>\<製品名>)は製品名で分かれるので、
// 製品名だけをビルドの間「One More Mile Test」にして、終わったら必ず元へ戻す(会社名・パッケージ名は変えない)。
// 出力: Builds/WindowsProductSandbox/OneMoreMile.exe(製品版)/ Builds/WindowsDevSandbox/OneMoreMile.exe(開発版)
public static class BuildWindowsProductSandbox
{
    public const string SandboxProductName = "One More Mile Test";
    const string OutputPath = "Builds/WindowsProductSandbox/OneMoreMile.exe";

    [MenuItem("Tools/2ndAction/Build Windows Product (Sandbox save)")]
    public static void Build() => BuildTo(OutputPath, BuildOptions.None);

    // 開発版(自動テスト -qaInput 等が使える)も同じ別名で。画面設定の試験で Unity が書く画面の記録(レジストリ)も本物と混ざらない(2026-10-25)
    const string DevOutputPath = "Builds/WindowsDevSandbox/OneMoreMile.exe";
    [MenuItem("Tools/2ndAction/Build Windows Dev (Sandbox save)")]
    public static void BuildDev() => BuildTo(DevOutputPath, BuildOptions.Development);

    static void BuildTo(string path, BuildOptions options)
    {
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string original = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = SandboxProductName;
            NetPrefabBuilder.Build();
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = options,
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

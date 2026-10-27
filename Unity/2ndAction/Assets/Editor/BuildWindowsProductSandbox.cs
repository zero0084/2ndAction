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

    // 2026-10-27 production の最終試験だけ: 配らない確認用の製品版(製品版と同じ・production 接続)。アプリの中の削除だけ有効
    //  (OMM_PROD_VERIFY → AccountDeletion.ProductionReady = true。リポジトリの既定は false のまま)。
    //  -ommVerifySlot 1|2 で製品名を「One More Mile Test」/「One More Mile Test 2」に分ける(保存・匿名プレイヤーが別 = ONLINE の2人)
    //  出力: Builds/WindowsProductVerify<slot>/OneMoreMile.exe
    public static void BuildVerify()
    {
        string slot = "1";
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-ommVerifySlot") slot = a[i + 1];
        if (slot != "1" && slot != "2") { Debug.LogError("BuildWindowsProductSandbox: -ommVerifySlot must be 1 or 2"); return; }
        BuildTo($"Builds/WindowsProductVerify{slot}/OneMoreMile.exe", BuildOptions.None, slot == "1" ? SandboxProductName : SandboxProductName + " 2", new[] { "OMM_PROD_VERIFY" });
    }

    // 2026-10-27 製品版の最終回帰: 製品版そのまま(追加の定義なし = 自動操縦なし)を2つ目の別名でも作る(LOCAL / ONLINE の2人目)
    //  出力: Builds/WindowsProductSandbox2/OneMoreMile.exe(製品名「One More Mile Test 2」)
    public static void BuildSlot2() => BuildTo("Builds/WindowsProductSandbox2/OneMoreMile.exe", BuildOptions.None, SandboxProductName + " 2");

    static void BuildTo(string path, BuildOptions options, string productName = SandboxProductName, string[] defines = null)
    {
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string original = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = productName;
            NetPrefabBuilder.Build();
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = options,
                extraScriptingDefines = defines,
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("BuildWindowsProductSandbox: result=" + report.summary.result + " path=" + report.summary.outputPath + " product=" + productName + (defines != null ? " defines=" + string.Join(",", defines) : ""));
        }
        finally
        {
            PlayerSettings.productName = original;
            AssetDatabase.SaveAssets();
            Debug.Log("BuildWindowsProductSandbox: product name restored to '" + PlayerSettings.productName + "'");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 広告なしパスのレシートの検証の準備(2026-11-18)。
//  Google Play Console の「収益化のセットアップ → ライセンス」の公開鍵(Base64 の RSA 公開鍵。秘密の値ではない)から、
//  Unity IAP の難読化ツールと同じ方法で Assets/Scripts/UnityPurchasing/generated/GooglePlayTangle.cs を作り、
//  Android の定義に OMM_IAP_TANGLE を足す(= 製品版でも広告なしパスの購入欄を出し、購入を検証してから有効にする)。
//
//  使い方(マスター): 公開鍵をテキストファイル(1行)に保存して
//   Unity.exe -batchmode -quit -projectPath ... -executeMethod IapReceiptSetup.ImportFromArgs -licenseKeyFile <ファイル>
//   または Unity のメニュー Tools/OneMoreMile/IAP: Import Google Play license key...
//  ・鍵は検証の形(RSA 公開鍵の DER)かを確かめてから使う。ログには鍵そのものは出さない(指紋だけ)
//  ・Unity IAP の生成の関数は internal なので、名前で探して呼ぶ(パッケージの版が変わって見つからない時は止めて理由を出す)
public static class IapReceiptSetup
{
    public const string TanglePath = "Assets/Scripts/UnityPurchasing/generated/GooglePlayTangle.cs";
    public const string Define = "OMM_IAP_TANGLE";

    public static bool TangleExists => File.Exists(TanglePath) && File.ReadAllText(TanglePath).Contains("class GooglePlayTangle");

    [MenuItem("Tools/OneMoreMile/IAP: Import Google Play license key...")]
    static void ImportMenu()
    {
        string p = EditorUtility.OpenFilePanel("Google Play のライセンスの公開鍵(Base64)を保存したテキストファイル", "", "txt");
        if (string.IsNullOrEmpty(p)) return;
        string err = Import(File.ReadAllText(p));
        EditorUtility.DisplayDialog("IAP", err ?? "取り込みました(GooglePlayTangle.cs と OMM_IAP_TANGLE)。", "OK");
    }

    public static void ImportFromArgs()
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, "-licenseKeyFile");
        string err = i >= 0 && i + 1 < a.Length && File.Exists(a[i + 1]) ? Import(File.ReadAllText(a[i + 1])) : "-licenseKeyFile <file> が見つかりません";
        Debug.Log("[IapReceiptSetup] " + (err == null ? "OK" : "NG: " + err));
        if (Application.isBatchMode) EditorApplication.Exit(err == null ? 0 : 3);
    }

    // 戻り値: null = 成功 / 文字列 = 失敗の理由
    public static string Import(string raw)
    {
        string key = new string((raw ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        byte[] der;
        try { der = Convert.FromBase64String(key); } catch { return "Base64 として読めません(Play Console の「ライセンス」の公開鍵をそのまま貼ってください)"; }
        string shape = CheckRsaPublicKey(der);
        if (shape != null) return shape;
        string fp;
        using (var sha = System.Security.Cryptography.SHA256.Create()) fp = BitConverter.ToString(sha.ComputeHash(der), 0, 8).Replace("-", "");

        var t = AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType("UnityEditor.Purchasing.ObfuscationGenerator", false)).FirstOrDefault(x => x != null);
        var m = t?.GetMethod("ObfuscateGoogleSecrets", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) return "Unity IAP の難読化の関数(ObfuscationGenerator.ObfuscateGoogleSecrets)が見つかりません(パッケージの版を確認)";
        string genErr = m.Invoke(null, new object[] { key }) as string;
        if (!string.IsNullOrEmpty(genErr)) return "GooglePlayTangle の生成に失敗: " + genErr;
        AssetDatabase.Refresh();
        if (!TangleExists) return "GooglePlayTangle.cs ができていません(" + TanglePath + ")";

        var nt = UnityEditor.Build.NamedBuildTarget.Android;
        string defines = PlayerSettings.GetScriptingDefineSymbols(nt);
        if (!defines.Split(';').Contains(Define)) PlayerSettings.SetScriptingDefineSymbols(nt, string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
        AssetDatabase.SaveAssets();
        Debug.Log($"[IapReceiptSetup] imported the Google Play license key (RSA {der.Length} bytes DER, sha256 {fp}…) -> {TanglePath}; Android defines: {PlayerSettings.GetScriptingDefineSymbols(nt)}");
        return null;
    }

    // コンパイルの確認(Android の 製品版 / 開発版 + Windows の製品版)。今の Android の定義(OMM_IAP_TANGLE があれば検証つき)で通るか
    //  Unity.exe -batchmode -quit -projectPath ... -executeMethod IapReceiptSetup.CompileCheck
    public static void CompileCheck()
    {
        var nt = UnityEditor.Build.NamedBuildTarget.Android;
        string defines = PlayerSettings.GetScriptingDefineSymbols(nt);
        bool ok = true;
        foreach (var (name, target, group, opt) in new[]
        {
            ("android-release", BuildTarget.Android, BuildTargetGroup.Android, UnityEditor.Build.Player.ScriptCompilationOptions.None),
            ("android-dev", BuildTarget.Android, BuildTargetGroup.Android, UnityEditor.Build.Player.ScriptCompilationOptions.DevelopmentBuild),
            ("windows-release", BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, UnityEditor.Build.Player.ScriptCompilationOptions.None),
        })
        {
            var s = new UnityEditor.Build.Player.ScriptCompilationSettings { target = target, group = group, options = opt };
            var r = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(s, "Builds/IapCompileCheck/" + name);
            int n = r.assemblies != null ? r.assemblies.Count : 0;
            ok &= n > 0;
            Debug.Log($"[IapCompileCheck] {name}: {(n > 0 ? "OK (" + n + " assemblies)" : "FAILED")}");
        }
        Debug.Log($"[IapCompileCheck] android defines='{defines}' tangle={TangleExists} -> {(ok ? "OK" : "NG")}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
    }

    // RSA 公開鍵(X.509 SubjectPublicKeyInfo、OID 1.2.840.113549.1.1.1)の形か
    static string CheckRsaPublicKey(byte[] der)
    {
        if (der.Length < 160 || der[0] != 0x30) return "RSA 公開鍵の形ではありません(長さ " + der.Length + ")";
        byte[] rsaOid = { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01 };
        bool found = false;
        for (int i = 0; i + rsaOid.Length <= Math.Min(der.Length, 40) && !found; i++) found = rsaOid.SequenceEqual(der.Skip(i).Take(rsaOid.Length));
        if (!found) return "RSA 公開鍵(rsaEncryption)ではありません";
        // Unity IAP の検証の部品(Unity.Purchasing.Security)は Editor では空の版(SecurityStub)になるので、ここでは形だけ。
        // 実際に読めるかは Android の開発版の自己試験(-qaIapValidate)で確かめる
        return null;
    }
}

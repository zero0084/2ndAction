using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;

// 本番用ビルドの設定の点検(2026-11-14、マスター依頼)。読むだけ: ビルドしない・設定を書き換えない・本番広告を出さない。
//  "Unity.exe -batchmode -projectPath ... -executeMethod ProductionConfigAudit.Run -auditOut <file> -quit"
//  1. Android の PlayerSettings(パッケージ名/版/SDK/IL2CPP/64bit/署名の設定/定義/シーン)
//  2. Android の製品版のスクリプトを3通りコンパイル(開発版 / 製品版・テスト広告 / 製品版・本番広告 = OMM_ADS_PROD を足しただけ)して、
//     出来た Assembly-CSharp.dll の定数を読む: 本番広告の切り替え・UGS の環境・削除の案内・開発用の型が製品版に入っていないこと
//  3. 本番広告の版の判定の材料(ReleaseBuildGuard と同じ条件)を、止めずに一覧にする
public static class ProductionConfigAudit
{
    static readonly StringBuilder sb = new StringBuilder();
    static int ng, warn;
    static void Row(string area, string item, string status, string detail)
    {
        if (status == "NG") ng++; else if (status == "WARN") warn++;
        sb.AppendLine($"| {area} | {item} | {status} | {detail.Replace("|", "/")} |");
    }

    public static void Run()
    {
        sb.Clear(); ng = 0; warn = 0;
        string outPath = Arg("-auditOut") ?? "Builds/ProductionAudit/audit.md";
        sb.AppendLine($"# 本番設定の点検(自動: ProductionConfigAudit、{DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        sb.AppendLine("| 区分 | 項目 | 結果 | 内容 |");
        sb.AppendLine("|---|---|---|---|");
        var nt = NamedBuildTarget.Android;

        // ---- 1. PlayerSettings
        Row("ビルド設定", "パッケージ名", PlayerSettings.GetApplicationIdentifier(nt) == "com.zero0084.action2nd" ? "OK" : "NG", PlayerSettings.GetApplicationIdentifier(nt));
        Row("ビルド設定", "版(versionCode / versionName)", "INFO", $"{PlayerSettings.Android.bundleVersionCode} / {PlayerSettings.bundleVersion}(本番用のビルドで +1 される)");
        Row("ビルド設定", "最小 / 対象 SDK", (int)PlayerSettings.Android.targetSdkVersion >= 35 ? "OK" : "NG", $"min {(int)PlayerSettings.Android.minSdkVersion} / target {(int)PlayerSettings.Android.targetSdkVersion}(Play の新規アプリは 35 以上)");
        var backend = PlayerSettings.GetScriptingBackend(nt);
        Row("ビルド設定", "スクリプトの方式", backend == ScriptingImplementation.IL2CPP ? "OK" : "NG", backend.ToString());
        var arch = PlayerSettings.Android.targetArchitectures;
        Row("ビルド設定", "64bit(ARM64)", (arch & AndroidArchitecture.ARM64) != 0 ? "OK" : "NG", arch.ToString());
        Row("ビルド設定", "署名(PlayerSettings)", !PlayerSettings.Android.useCustomKeystore && string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) ? "OK" : "WARN",
            $"useCustomKeystore={PlayerSettings.Android.useCustomKeystore}(設定には鍵を保存しない。本番用のビルドの間だけ環境変数 OMM_UPLOAD_KEYSTORE 等から入れて、終わったら戻す)");
        string ks = Environment.GetEnvironmentVariable("OMM_UPLOAD_KEYSTORE");
        Row("ビルド設定", "アップロード鍵(この起動の環境)", !string.IsNullOrEmpty(ks) && File.Exists(ks) ? "OK" : "INFO", string.IsNullOrEmpty(ks) ? "未指定(本番用のビルドはマスターが鍵を指定して実行。指定が無いと BuildProduction は止まる)" : "指定あり(ファイル名のみ確認)");
        string defines = PlayerSettings.GetScriptingDefineSymbols(nt);
        Row("ビルド設定", "Android の定義", !defines.Contains("OMM_ADS_PROD") && !defines.Contains("OMM_RELEASE_PROBE") && !defines.Contains("OMM_CAPTURE") ? "OK" : "NG",
            $"{defines}(本番広告 OMM_ADS_PROD は設定に書かず、本番用のビルドの間だけ足す)");
        Row("ビルド設定", "Development Build(エディターの設定)", "INFO", $"{EditorUserBuildSettings.development}(本番用/クローズドテスト用のビルドは BuildOptions.None で作るので、この設定は使わない)");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToArray();
        Row("ビルド設定", "シーン", scenes.Length > 0 ? "OK" : "NG", string.Join(", ", scenes));
        Row("ビルド設定", "インターネット権限", PlayerSettings.Android.forceInternetPermission ? "OK" : "INFO", $"forceInternetPermission={PlayerSettings.Android.forceInternetPermission}(ビルドの時に true にする)");

        // ---- 広告の ID
        Row("広告", "AdMob アプリ ID(本番・テスト広告の版も同じ)", !string.IsNullOrEmpty(MonetizationSetup.ProdAndroidAppId) ? "OK" : "NG", MonetizationSetup.ProdAndroidAppId);
        string gmaId = ReadGmaAppId();
        Row("広告", "GoogleMobileAdsSettings のアプリ ID", gmaId == MonetizationSetup.ProdAndroidAppId ? "OK" : "NG", gmaId ?? "(読めない)");
        Row("広告", "本番の広告ユニット", !string.IsNullOrEmpty(MonetizationConfig.ProdInterstitialUnit) && !string.IsNullOrEmpty(MonetizationConfig.ProdRewardedUnit) ? "OK" : "NG",
            $"強制 {MonetizationConfig.ProdInterstitialUnit} / 報酬 {MonetizationConfig.ProdRewardedUnit}");
        Row("広告", "テストの広告ユニット", MonetizationConfig.TestInterstitialUnit.StartsWith("ca-app-pub-3940256099942544/") && MonetizationConfig.TestRewardedUnit.StartsWith("ca-app-pub-3940256099942544/") ? "OK" : "NG", "Google 公式のテスト用ユニット");

        // ---- 課金
        bool tangle = defines.Contains("OMM_IAP_TANGLE");
        bool tangleFile = AssetDatabase.FindAssets("GooglePlayTangle").Length > 0;
        Row("課金", "レシートの検証(GooglePlayTangle)", tangle && tangleFile ? "OK" : "INFO", tangle ? (tangleFile ? "あり" : "定義はあるがファイルが無い") : "未設定 → 製品版では広告なしパスの購入欄を出さない(販売停止の状態)。既に持っている権利は保たれる(確認できない時は外さない)");

        // ---- 翻訳など ReleaseBuildGuard と同じ条件
        int pend = 0;
        foreach (var pr in LocPendingReview.Pending) foreach (var lang in Loc.Languages) if (lang.code != "ja" && !Loc.HasTranslation(lang.code, pr.key)) { pend++; break; }
        Row("ビルドの止め", "翻訳の反映待ち(LocPendingReview)", pend == 0 ? "OK" : "NG", $"{pend} 件");
        Row("ビルドの止め", "プロフィールの訳の下書き", !LocPendingReview.ProfileDraftsUnreviewed ? "OK" : "NG", LocPendingReview.ProfileDraftsUnreviewed ? "レビュー待ち" : "済み");

        // ---- 2. 3通りのコンパイル
        string baseDir = "Builds/ProductionAudit";
        var dev = Compile(Path.Combine(baseDir, "dev"), ScriptCompilationOptions.DevelopmentBuild, null);
        var relTest = Compile(Path.Combine(baseDir, "release_testads"), ScriptCompilationOptions.None, null);
        var relProd = Compile(Path.Combine(baseDir, "release_prodads"), ScriptCompilationOptions.None, new[] { "OMM_ADS_PROD" });
        Row("コンパイル", "Android 開発版 / 製品版(テスト広告)/ 製品版(本番広告)", dev != null && relTest != null && relProd != null ? "OK" : "NG",
            $"dev={(dev != null ? "ok" : "失敗")} release_testads={(relTest != null ? "ok" : "失敗")} release_prodads={(relProd != null ? "ok" : "失敗")}");
        if (relTest != null && relProd != null && dev != null)
        {
            Row("広告", "テスト広告の版: 本番広告の切り替え", ConstOf(relTest, "MonetizationConfig", "UseProductionAds") is bool b1 && !b1 ? "OK" : "NG", $"UseProductionAds={ConstOf(relTest, "MonetizationConfig", "UseProductionAds")}");
            Row("広告", "本番広告の版: 本番広告の切り替え", ConstOf(relProd, "MonetizationConfig", "UseProductionAds") is bool b2 && b2 ? "OK" : "NG", $"UseProductionAds={ConstOf(relProd, "MonetizationConfig", "UseProductionAds")}");
            Row("広告", "開発版: 本番広告にならない", ConstOf(DevWithProd(baseDir), "MonetizationConfig", "UseProductionAds") is bool b3 && !b3 ? "OK" : "NG", "開発版に OMM_ADS_PROD を足しても UseProductionAds=false(#if OMM_ADS_PROD && !DEVELOPMENT_BUILD)");
            Row("オンライン", "UGS の環境(製品版)", ConstOf(relProd, "UgsEnvironment", "IsReleaseBuild") is bool r1 && r1 ? "OK" : "NG", "IsReleaseBuild=true → 必ず production(起動引数で変えられない)");
            Row("オンライン", "UGS の環境(開発版)", ConstOf(dev, "UgsEnvironment", "IsReleaseBuild") is bool r2 && !r2 ? "OK" : "NG", "開発版は development(production は選べない)");
            Row("オンライン", "データ削除のアプリ内の案内(production)", ConstOf(relProd, "AccountDeletion", "ProductionReady") is bool r3 && r3 ? "OK" : "WARN", $"ProductionReady={ConstOf(relProd, "AccountDeletion", "ProductionReady")}");
            Row("開発用", "撮影用/確認用の版の印", ConstOf(relProd, "DevGate", "CaptureBuild") is bool c1 && !c1 && ConstOf(relProd, "DevGate", "ReleaseProbeBuild") is bool c2 && !c2 ? "OK" : "NG", "CaptureBuild=false ReleaseProbeBuild=false(DevGate.On は製品版で Debug.isDebugBuild=false なので常に false)");
            var devTypes = TypeNames(dev); var relTypes = TypeNames(relProd);
            var only = devTypes.Except(relTypes).OrderBy(x => x).ToList();
            string[] mustBeGone = { "QaSweep", "DebugPanel", "MockAdService", "MockStoreService", "MemorySaveStore", "FileSaveStore", "QaArgs", "EndgameDebug" };
            var leaked = mustBeGone.Where(n => relTypes.Contains(n)).ToList();
            Row("開発用", "開発用の型が製品版に入っていない", leaked.Count == 0 ? "OK" : "NG", leaked.Count == 0 ? $"開発版だけの型 {only.Count} 個(QaSweep/DebugPanel/模擬の広告・課金/試験用の保存 等)は製品版に無い" : "製品版に残っている: " + string.Join(", ", leaked));
            var suspicious = relTypes.Where(n => n.StartsWith("Qa") || n.Contains("AutoTest") || n.Contains("SelfTest") || n.StartsWith("Mock") || n.Contains("Debug")).OrderBy(x => x).ToList();
            Row("開発用", "製品版に残る、名前に Debug/Qa/Mock/Test を含む型", suspicious.Count == 0 ? "OK" : "INFO", suspicious.Count == 0 ? "なし" : string.Join(", ", suspicious) + "(中身は Debug.isDebugBuild / DevGate.On で止まる物。下の一覧で確認)");
            Row("開発用", "製品版に意図して残す診断", relTypes.Contains("FreezeDiagnostics") ? "INFO" : "OK", "FreezeDiagnostics: 処理落ち/位置の飛びを端末のファイル(hitch_log.txt、2MB で古い分へ回す)に記録するだけ。画面の表示は開発版の DEBUG の時だけ。送信しない");
            File.WriteAllLines(Path.Combine(baseDir, "dev_only_types.txt"), only);
            File.WriteAllLines(Path.Combine(baseDir, "release_types_named_debug.txt"), suspicious);
        }

        sb.AppendLine();
        sb.AppendLine($"NG {ng} 件 / WARN {warn} 件。読むだけの点検(ビルド・設定の書き換え・本番広告の表示はしていない)。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log($"[ProductionConfigAudit] NG={ng} WARN={warn} -> {outPath}");
        if (Application.isBatchMode) EditorApplication.Exit(ng == 0 ? 0 : 2);
    }

    static string devProdDir;
    static string DevWithProd(string baseDir) => devProdDir ??= Compile(Path.Combine(baseDir, "dev_prodads"), ScriptCompilationOptions.DevelopmentBuild, new[] { "OMM_ADS_PROD" });

    static string Compile(string dir, ScriptCompilationOptions opt, string[] extra)
    {
        var s = new ScriptCompilationSettings { target = BuildTarget.Android, group = BuildTargetGroup.Android, options = opt };
        if (extra != null) s.extraScriptingDefines = extra;
        var r = PlayerBuildInterface.CompilePlayerScripts(s, dir);
        string dll = Path.Combine(dir, "Assembly-CSharp.dll");
        bool ok = r.assemblies != null && r.assemblies.Contains("Assembly-CSharp.dll") && File.Exists(dll);
        Debug.Log($"[ProductionConfigAudit] compile {dir} options={opt} extra={(extra != null ? string.Join(";", extra) : "-")} -> {(ok ? "ok" : "FAILED")}");
        if (!ok) return null;
        // 次のコンパイルが前の出力のフォルダを消すことがある → すぐ別の場所へ写す
        string keep = Path.Combine(Path.GetDirectoryName(dir), "dll", Path.GetFileName(dir) + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(keep));
        File.Copy(dll, keep, true);
        return keep;
    }

    static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();
    static Assembly Load(string dll)
    {
        if (dll == null) return null;
        if (!loaded.TryGetValue(dll, out var a)) loaded[dll] = a = Assembly.Load(File.ReadAllBytes(dll));
        return a;
    }
    static object ConstOf(string dll, string type, string field)
    {
        try
        {
            var t = Load(dll)?.GetType(type);
            var f = t?.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f == null ? null : f.IsLiteral ? f.GetRawConstantValue() : null;
        }
        catch (Exception e) { return "error " + e.GetType().Name; }
    }
    static HashSet<string> TypeNames(string dll)
    {
        var a = Load(dll);
        Type[] ts;
        try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types.Where(t => t != null).ToArray(); }
        return new HashSet<string>(ts.Where(t => !t.IsNested && !t.Name.StartsWith("<")).Select(t => t.Name));
    }
    static string ReadGmaAppId()
    {
        const string p = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
        if (!File.Exists(p)) return null;
        foreach (var l in File.ReadAllLines(p)) { var t = l.Trim(); if (t.StartsWith("adMobAndroidAppId:")) return t.Substring("adMobAndroidAppId:".Length).Trim(); }
        return null;
    }
    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }
}

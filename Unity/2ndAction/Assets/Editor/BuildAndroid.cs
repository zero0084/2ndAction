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
        BuildReport report;
        using (new BrandSplashSetup.AndroidScope()) // 起動ロゴは BootSplash(Unity の起動画面は背景色だけ、2026-10-26)
            report = BuildPipeline.BuildPlayer(
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
            using var splash = new BrandSplashSetup.AndroidScope();
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.Android, BuildOptions.None);
            Debug.Log("BuildAndroid: store aab result=" + report.summary.result + " path=" + report.summary.outputPath);
        }
        finally { EditorUserBuildSettings.buildAppBundle = wasAab; PlayerSettings.Android.splitApplicationBinary = wasSplit; AssetDatabase.SaveAssets(); }
    }

    // クローズドテスト用の製品版(2026-10-26): Development Build / debuggable なし、広告は「テスト広告」(実際のアプリ ID + Google のテスト用広告ユニット)。
    //  ・本番広告(OMM_ADS_PROD)の定義が付いていたら作らない(切り替えはマスターの指示の後)
    //  ・署名: マスターのアップロード鍵を環境変数で渡した時だけ AAB(Play へ上げる物)と APK を同じ鍵で作る。パスワードはファイル/ログに残さない
    //      OMM_UPLOAD_KEYSTORE(鍵のファイルのパス)/ OMM_UPLOAD_KEYSTORE_PASS / OMM_UPLOAD_KEY_ALIAS / OMM_UPLOAD_KEY_PASS
    //    渡されていない時は、端末で確かめる APK だけをデバッグ鍵で作る(Play には上げられない。ファイル名に debugsigned)
    //  ・署名の設定はビルドの後に必ず元へ戻す(ProjectSettings に鍵の場所・パスワードを残さない)
    //  ・出力: Builds/ClosedTest/OneMoreMile_ct<番号>.aab / .apk と、使った設定の記録 OneMoreMile_ct<番号>_build.txt
    [MenuItem("Tools/2ndAction/Build Android Closed Test (release, test ads)")]
    public static void BuildClosedTest()
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android);
        if (defines.Contains("OMM_ADS_PROD")) { Debug.LogError("BuildAndroid: closed test build refused - OMM_ADS_PROD (production ads) is defined"); return; }
        // AdMob のアプリ ID を設定へ(テスト広告でも実際のアプリ ID)。2026-10-26: 同じ起動の中で ID を書き換えた直後のビルドには
        // 古い ID が入った(APK の AndroidManifest で確認)→ 書き換えた時はビルドせず、もう一度実行してもらう
        if (MonetizationSetup.ApplyAndReport()) { Debug.LogError("BuildAndroid: the AdMob app id was just updated - run BuildClosedTest again (a fresh editor session picks it up)"); return; }
        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd HHmm");
        PlayerSettings.bundleVersion = stamp;
        PlayerSettings.Android.bundleVersionCode += 1;
        int code = PlayerSettings.Android.bundleVersionCode;
        PlayerSettings.Android.forceInternetPermission = true;
        NetPrefabBuilder.Build();
        Directory.CreateDirectory("Builds/ClosedTest");

        string ks = System.Environment.GetEnvironmentVariable("OMM_UPLOAD_KEYSTORE");
        string ksPass = System.Environment.GetEnvironmentVariable("OMM_UPLOAD_KEYSTORE_PASS");
        string alias = System.Environment.GetEnvironmentVariable("OMM_UPLOAD_KEY_ALIAS");
        string aliasPass = System.Environment.GetEnvironmentVariable("OMM_UPLOAD_KEY_PASS");
        bool upload = !string.IsNullOrEmpty(ks) && File.Exists(ks) && !string.IsNullOrEmpty(ksPass) && !string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(aliasPass);
        if (!upload) Debug.LogWarning("BuildAndroid: no upload key given (OMM_UPLOAD_KEYSTORE etc.) - building a debug-signed APK only (cannot be uploaded to Google Play)");

        bool wasCustom = PlayerSettings.Android.useCustomKeystore, wasAab = EditorUserBuildSettings.buildAppBundle, wasSplit = PlayerSettings.Android.splitApplicationBinary;
        string wasKs = PlayerSettings.Android.keystoreName, wasAlias = PlayerSettings.Android.keyaliasName;
        var outputs = new System.Collections.Generic.List<string>();
        try
        {
            if (upload)
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks; PlayerSettings.Android.keystorePass = ksPass;
                PlayerSettings.Android.keyaliasName = alias; PlayerSettings.Android.keyaliasPass = aliasPass;
            }
            else PlayerSettings.Android.useCustomKeystore = false;
            using (new BrandSplashSetup.AndroidScope())
            {
                if (upload)
                {
                    EditorUserBuildSettings.buildAppBundle = true; PlayerSettings.Android.splitApplicationBinary = true; // base を 200MB 未満に(Play の上限)
                    string aab = $"Builds/ClosedTest/OneMoreMile_ct{code}.aab";
                    var r1 = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, aab, BuildTarget.Android, BuildOptions.None);
                    Debug.Log("BuildAndroid: closed test aab result=" + r1.summary.result + " size=" + r1.summary.totalSize + " path=" + aab);
                    outputs.Add($"aab={aab} result={r1.summary.result}");
                }
                EditorUserBuildSettings.buildAppBundle = false; PlayerSettings.Android.splitApplicationBinary = false;
                string apk = $"Builds/ClosedTest/OneMoreMile_ct{code}{(upload ? "" : "_debugsigned")}.apk";
                var r2 = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, apk, BuildTarget.Android, BuildOptions.None);
                Debug.Log("BuildAndroid: closed test apk result=" + r2.summary.result + " size=" + r2.summary.totalSize + " path=" + apk);
                outputs.Add($"apk={apk} result={r2.summary.result}");
            }
        }
        finally
        {
            PlayerSettings.Android.useCustomKeystore = wasCustom;
            if (upload)
            {
                // 鍵を設定した時だけ戻す(触っていない時に空を書き戻すと ProjectSettings に '{inproject}: ' と書かれるため)
                PlayerSettings.Android.keystoreName = wasKs; PlayerSettings.Android.keyaliasName = wasAlias;
                PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = "";
            }
            EditorUserBuildSettings.buildAppBundle = wasAab; PlayerSettings.Android.splitApplicationBinary = wasSplit;
            AssetDatabase.SaveAssets();
        }
        // 使った設定の記録(パスワードは書かない)
        var info = new System.Text.StringBuilder();
        info.AppendLine($"versionCode={code} versionName={stamp}");
        info.AppendLine($"developmentBuild=false debuggable=false");
        info.AppendLine($"adsProfile={MonetizationConfig.AdsProfile} (test = Google test ad units; production units are NOT used)");
        info.AppendLine($"admobAppId={MonetizationSetup.ProdAndroidAppId}");
        info.AppendLine($"interstitialUnit={MonetizationConfig.TestInterstitialUnit} rewardedUnit={MonetizationConfig.TestRewardedUnit}");
        info.AppendLine($"defines={defines}");
        info.AppendLine($"noAdsPass={(defines.Contains("OMM_IAP_TANGLE") ? "purchase enabled" : "preparing (no receipt validation)")}");
        info.AppendLine($"signing={(upload ? "upload key (" + Path.GetFileName(ks) + ", alias set)" : "debug key - NOT uploadable")}");
        foreach (var o in outputs) info.AppendLine(o);
        File.WriteAllText($"Builds/ClosedTest/OneMoreMile_ct{code}_build.txt", info.ToString());
        Debug.Log("BuildAndroid: closed test build info: " + info.ToString().Replace(System.Environment.NewLine, " | "));
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
            using var splash = new BrandSplashSetup.AndroidScope();
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, outPath, BuildTarget.Android, BuildOptions.Development);
            Debug.Log("BuildAndroid: probe result=" + report.summary.result + " size=" + report.summary.totalSize + " path=" + report.summary.outputPath + " split=" + split);
        }
        finally { EditorUserBuildSettings.buildAppBundle = was; PlayerSettings.Android.splitApplicationBinary = wasSplit; AssetDatabase.SaveAssets(); }
    }
}

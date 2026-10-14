using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 製品版(開発版でない)の Android ビルドの前の確認(2026-10-14)。公開の準備が終わっていない設定のまま製品版を作らない。
//  ・広告: 本番の広告枠 ID(MonetizationConfig.ProdRewardedUnit / ProdInterstitialUnit)と本番のアプリ ID(MonetizationSetup.ProdAndroidAppId)。
//    空のままだと製品版でも Google のテスト広告が出る(収益にならない)。
//  ・課金: レシートの検証(GooglePlayTangle + OMM_IAP_TANGLE)。無いと本物の購入を確かめられず、購入の画面は「準備中」になる。
//  開発版(Development Build)のビルドは今までどおり(テスト広告/検証なしで作れる)。値の設定はマスターの作業(Docs の公開準備の一覧)。
public class ReleaseBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        if ((report.summary.options & BuildOptions.Development) != 0) return;
        var missing = new List<string>();
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android);
        if (defines.Contains("OMM_ADMOB"))
        {
            if (string.IsNullOrEmpty(MonetizationConfig.ProdRewardedUnit)) missing.Add("AdMob の本番の報酬広告の広告枠 ID(MonetizationConfig.ProdRewardedUnit)");
            if (string.IsNullOrEmpty(MonetizationConfig.ProdInterstitialUnit)) missing.Add("AdMob の本番のインタースティシャル広告の広告枠 ID(MonetizationConfig.ProdInterstitialUnit)");
            if (string.IsNullOrEmpty(MonetizationSetup.ProdAndroidAppId)) missing.Add("AdMob の本番のアプリ ID(MonetizationSetup.ProdAndroidAppId、入れた後に Tools/OneMoreMile/Monetization: Apply SDK Settings)");
        }
        if (defines.Contains("OMM_UNITY_IAP") && !defines.Contains("OMM_IAP_TANGLE"))
            missing.Add("課金のレシート検証(Play Console のライセンスキーから GooglePlayTangle を作り、スクリプトの定義に OMM_IAP_TANGLE を足す)");
        if (missing.Count == 0) return;
        string msg = "[ReleaseBuildGuard] 製品版の Android ビルドを止めました。公開の前に次の設定が必要です:\n - " + string.Join("\n - ", missing)
            + "\n(開発版のビルドは Development Build にすれば今までどおり作れます)";
        Debug.LogError(msg);
        throw new BuildFailedException(msg);
    }
}

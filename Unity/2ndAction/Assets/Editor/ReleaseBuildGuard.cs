using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 製品版(開発版でない)の Android ビルドの前の確認(2026-10-14)。公開の準備が終わっていない設定のまま製品版を作らない。
//  ・広告: 本番のアプリ ID(MonetizationSetup.ProdAndroidAppId)。本番広告の版(OMM_ADS_PROD)は本番の広告枠 ID も。
//    2026-10-26: OMM_ADS_PROD の無い製品版は「テスト広告の版」(クローズドテスト用)として作れる。ビルドの記録に広告の設定を出す。
//  ・課金: レシートの検証(GooglePlayTangle + OMM_IAP_TANGLE)。無いと本物の購入を確かめられず、購入の画面は「準備中」になる。
//  ・翻訳: LocPendingReview の文(エステルのレビュー待ち)の訳が全言語に入っていること(2026-10-15)。
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
        // 2026-10-26: 広告の設定は2つ。OMM_ADS_PROD なし = テスト広告(実際のアプリ ID + Google のテスト用広告ユニット。クローズドテスト用)、
        //  あり = 本番広告(マスターの指示の後だけ)。テスト広告の版では課金の検証(GooglePlayTangle)が無くても作れる(購入の画面は「購入の準備中」のまま)
        bool prodAds = defines.Contains("OMM_ADS_PROD");
        if (defines.Contains("OMM_ADMOB"))
        {
            if (string.IsNullOrEmpty(MonetizationSetup.ProdAndroidAppId)) missing.Add("AdMob の本番のアプリ ID(MonetizationSetup.ProdAndroidAppId、入れた後に Tools/OneMoreMile/Monetization: Apply SDK Settings)");
            if (prodAds && (string.IsNullOrEmpty(MonetizationConfig.ProdRewardedUnit) || string.IsNullOrEmpty(MonetizationConfig.ProdInterstitialUnit))) missing.Add("AdMob の本番の広告枠 ID(MonetizationConfig.ProdRewardedUnit / ProdInterstitialUnit)");
            Debug.Log($"[ReleaseBuildGuard] ads profile = {(prodAds ? "PRODUCTION ADS(本番広告)" : "TEST ADS(テスト広告: Google のテスト用広告ユニット)")} appId={MonetizationSetup.ProdAndroidAppId}");
        }
        if (defines.Contains("OMM_UNITY_IAP") && !defines.Contains("OMM_IAP_TANGLE"))
        {
            if (prodAds) missing.Add("課金のレシート検証(Play Console のライセンスキーから GooglePlayTangle を作り、スクリプトの定義に OMM_IAP_TANGLE を足す)");
            else Debug.Log("[ReleaseBuildGuard] no receipt validation (OMM_IAP_TANGLE) - the no-ads pass stays 'preparing' in this test-ads build");
        }
        // 2026-10-15: 翻訳の反映待ち(LocPendingReview)。訳が無い言語では前の仕様の説明が出るので、公開版は止める
        foreach (var pr in LocPendingReview.Pending)
        {
            var langs = new List<string>();
            foreach (var lang in Loc.Languages) if (lang.code != "ja" && !Loc.HasTranslation(lang.code, pr.key)) langs.Add(lang.code);
            if (langs.Count > 0) missing.Add($"翻訳の反映待ち: ID {pr.id} の訳が {langs.Count} 言語で未反映({string.Join(",", langs)})。エステルのレビュー後に反映(Docs/LocReview_2026-10-15b)");
        }
        if (missing.Count == 0) return;
        string msg = "[ReleaseBuildGuard] 製品版の Android ビルドを止めました。公開の前に次の設定が必要です:\n - " + string.Join("\n - ", missing)
            + "\n(開発版のビルドは Development Build にすれば今までどおり作れます)";
        Debug.LogError(msg);
        throw new BuildFailedException(msg);
    }
}

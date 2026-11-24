using System;
using System.Collections.Generic;
using UnityEngine;

// ===== 広告 / 広告なしパス(依頼I、2026-10-10) =====
// Android 版 = 無料 + 広告(+ 買い切りの「広告なしパス」)、Steam 版 = 広告もモバイル課金も無し。
// iOS 版(2026-11-22)= 無料 + 広告(リワード/インタースティシャル)。iOS の購入(広告なしパス)はまだ無い
// ゲーム側は IAdService / IStoreService だけを見る。実際の SDK(AdMob / Unity IAP)は別ファイルで、
// パッケージを入れて定義(OMM_ADMOB / OMM_UNITY_IAP)を付けた Android ビルドだけに入る。
// 開発版は「模擬」(MockAdService / MockStoreService)で全部の流れを試せる。模擬の購入は本物の購入権利と別の保存で、製品版には出ない。
public enum AdKind { Interstitial, Rewarded }
public enum AdOutcome { Shown, NotReady, Failed }          // 広告の画面の結果(報酬とは別)
public enum MonetizationMode { None, Store, Mock }          // None = 何も出さない(Steam/PC/SDK 無し) / Store = 本物の SDK / Mock = 開発用の模擬

public interface IAdService
{
    string Name { get; }
    void Initialize();
    bool IsReady(AdKind kind);
    void Load(AdKind kind);
    // onReward: SDK の正規の報酬通知(報酬広告だけ。閉じた時ではない)。onFinished: 広告の画面が終わった(必ず1回、報酬の前後は不定)
    void Show(AdKind kind, Action onReward, Action<AdOutcome, string> onFinished);
    // 同意(UMP など)の「プライバシーの設定」を出す必要があるか / 出す
    bool PrivacyOptionsRequired { get; }
    void ShowPrivacyOptions(Action<string> done); // done(null) = 開いて閉じた / done(エラー) = 開けなかった(2026-11-24)
}

public enum PurchaseStatus { Purchased, Pending, Cancelled, Failed, AlreadyOwned, NotReady }
public struct PurchaseResult { public PurchaseStatus status; public string productId; public string token; public string message; }
// pending(2026-11-18): ストアに「支払いの完了待ち」の注文がある(持っているとは扱わない)。ok かつ !owned かつ !pending なら、完了待ちの印は外してよい
public struct RestoreResult { public bool ok; public bool owned; public bool pending; public string token; public string message; }

public interface IStoreService
{
    string Name { get; }
    bool Ready { get; }
    void Initialize(Action<bool> done);
    // ストアから取った現地通貨の価格(取れていない間は null)
    string LocalizedPrice(string productId);
    void Purchase(string productId, Action<PurchaseResult> done);
    // 購入権利の確認(起動時/復帰時/「購入を復元」)。ok=false はストアに繋がらなかった(オフライン等)= 持っていないとは限らない
    void Restore(string productId, Action<RestoreResult> done);
    // あとから届いた購入(支払い待ちが完了した/前回の起動で終わらなかった購入)。検証と完了処理(acknowledge)の後に呼ぶ
    event Action<PurchaseResult> LatePurchase;
}

public static class Monetization
{
    public static MonetizationMode Mode { get; private set; } = MonetizationMode.None;

    // 2026-10-14: 本物のストアでレシートを検証できる状態か(GooglePlayTangle と OMM_IAP_TANGLE)。できない間は購入させない
    // (払っても検証できず付与されない → Google 側で自動返金、になるのを防ぐ)。模擬/ストアなしは関係ない
    public static bool PurchaseSetupComplete
    {
        get
        {
            // 2026-11-22: iOS は広告だけ(購入の仕組みが無い = Store が null)→ パスの購入欄を出さない
            if (Mode == MonetizationMode.Store && Store == null) return false;
#if UNITY_ANDROID && OMM_UNITY_IAP && !OMM_IAP_TANGLE
            return Mode != MonetizationMode.Store;
#else
            return true;
#endif
        }
    }
    public static IAdService Ads { get; private set; }
    public static IStoreService Store { get; private set; }
    public static bool AdsOn => Ads != null;
    public static bool StoreOn => Store != null;

    // 開発版の模擬: 起動引数 -monetizationMock 1 か、DEBUG の切り替え(Dev キー)
    public const string DevMockKey = "Dev.MonetizationMock";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Mode != MonetizationMode.None || Ads != null || Store != null) return;
        Mode = Decide();
        switch (Mode)
        {
            case MonetizationMode.Mock:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Ads = MockAdService.Instance; Store = MockStoreService.Instance;
#endif
                break;
            case MonetizationMode.Store:
#if (UNITY_ANDROID && OMM_ADMOB) || UNITY_IOS
                Ads = new AdMobAdService(); // 2026-11-22: iOS も広告(リワード/インタースティシャル)だけ。iOS の購入(IAP)は入れない
#endif
#if UNITY_ANDROID && OMM_UNITY_IAP
                Store = new UnityIapStoreService();
#endif
                break;
        }
        Debug.Log($"[Monetization] mode={Mode} ads={(Ads != null ? Ads.Name : "none")} store={(Store != null ? Store.Name : "none")} platform={Platform.Name} adsProfile={MonetizationConfig.AdsProfile} units={MonetizationConfig.UnitsLabel}");
        if (Ads != null) AdTrace.Note($"ads {Platform.Name} profile={MonetizationConfig.AdsProfile} units={MonetizationConfig.UnitsLabel}"); // 2026-11-22: お問い合わせの「広告の記録」で、どの広告ユニットだったか分かる
        if (Mode == MonetizationMode.None) return; // Steam/PC: 広告の SDK も時刻の確認も始めない
        AdManager.Ensure();
        TrustedClock.Ensure();
        if (Ads != null) Ads.Initialize();
        if (Store != null) NoAdsPass.StartStore();
    }

    static MonetizationMode Decide()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool mockArg = false;
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-monetizationMock") mockArg = a[i + 1] != "0";
        if (mockArg || SaveStore.GetInt(DevMockKey, 0) == 1) return MonetizationMode.Mock;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR && (OMM_ADMOB || OMM_UNITY_IAP)
        return MonetizationMode.Store;
#elif UNITY_IOS && !UNITY_EDITOR
        return MonetizationMode.Store; // 2026-11-22: iOS は広告だけ(Store = null。広告なしパスの購入は出さない)
#else
        return MonetizationMode.None; // Steam / PC / SDK を入れていない Android: 広告も課金も出さない
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版: 模擬の切り替え(次の起動から)
    public static void DevSetMock(bool on) { SaveStore.SetInt(DevMockKey, on ? 1 : 0); SaveStore.Save(); }
#endif
}

// 設定値(プラットフォームごと。Steam 版は Mode=None なので使われない)
public static class MonetizationConfig
{
    public const float AdCooldownSeconds = 300f;      // 前回の広告(強制/報酬とも)が終わってから(実時間)
    public const float SessionWarmupSeconds = 300f;   // 起動から最初の強制広告まで(実時間)
    public const int AdGachaDailyLimit = 3;           // 広告ガチャ(パスの無料分と共通の枠)
    public const int DayResetUtcOffsetHours = 9;      // 日付の切り替え = 日本時間 0時(世界共通)
    // 本番の値はマスターが登録した後に入れる(未決定)。商品 ID は Play Console で作った後は変えられない
    public static string NoAdsProductId = "noads_pass";
    // 2026-11-19 Play Console の登録(マスター): 商品 ID noads_pass / 購入オプション ID standard(購入・下位互換あり)/ 非消費型として扱う。
    //  商品 ID と購入オプション ID は別物: アプリが問い合わせ・購入に使うのは商品 ID だけ。Unity IAP 5.4.4(Play Billing 9.0.0)は
    //  購入オプションを指定せずに ProductDetails.getOneTimePurchaseOfferDetails() の価格を読み、offer token なしで購入画面を出す
    //  = Play が「下位互換」の購入オプション(standard)を使う。下位互換を外す/別の購入オプションへ変える時は、ここと IAP の対応を見直すこと。
    //  この値は記録・ログ用(コードの判定には使わない)
    public const string NoAdsPurchaseOptionId = "standard";
    public const string TestInterstitialUnit = "ca-app-pub-3940256099942544/1033173712"; // Google のテスト用(公式のサンプル)
    public const string TestRewardedUnit = "ca-app-pub-3940256099942544/5224354917";
    // 2026-10-26 マスターが AdMob で作った本番の広告ユニット(将来の切り替え用。今は使わない)
    //  OneMoreMile_Android_Interstitial_GameOver / OneMoreMile_Android_Rewarded(管理画面の報酬の「数量1/項目Reward」は 1 MILE の意味ではない。
    //  ゲーム内の報酬は MILE 2倍・広告ガチャそれぞれの仕様のまま)
    public const string ProdInterstitialUnit = "ca-app-pub-2863190967263764/5403818490";
    public const string ProdRewardedUnit = "ca-app-pub-2863190967263764/3391276558";
    // 広告の設定(2026-10-26): 既定は「テスト広告」= 実際のアプリ ID + Google のテスト用広告ユニット(クローズドテストの全テスターもこれ)。
    // 本番広告は、マスターの指示の後に Android の定義へ OMM_ADS_PROD を足した製品版だけ(開発版は常にテスト広告)。
#if OMM_ADS_PROD && !DEVELOPMENT_BUILD
    public const bool UseProductionAds = true;
#else
    public const bool UseProductionAds = false;
#endif
    public static string AdsProfile => UseProductionAds ? "production" : "test";
    // 2026-11-22 iOS(マスターが AdMob で作成: アプリ OneMoreMile / com.zero0084.action2nd / ca-app-pub-2863190967263764~1910097904)
    //  OMM_iOS_Rewarded / OMM_iOS_Interstitial。テストと本番の切り替えは Android と同じ UseProductionAds(OMM_ADS_PROD)。
    //  iOS の定義に OMM_ADS_PROD を足すまでは、TestFlight の版も含めて必ず Google 公式の iOS 用テスト広告ユニット(Editor/開発版の判定には頼らない)。
    //  切り替えの手順と公開前の確認: Docs/Release_2026-11-22/iOS_Ads.md
    public const string IosTestInterstitialUnit = "ca-app-pub-3940256099942544/4411468910"; // Google 公式(iOS)
    public const string IosTestRewardedUnit = "ca-app-pub-3940256099942544/1712485313";      // Google 公式(iOS)
    public const string IosProdInterstitialUnit = "ca-app-pub-2863190967263764/6532915373"; // OMM_iOS_Interstitial
    public const string IosProdRewardedUnit = "ca-app-pub-2863190967263764/2045829270";      // OMM_iOS_Rewarded
#if UNITY_IOS
    public static string InterstitialUnit => UseProductionAds ? IosProdInterstitialUnit : IosTestInterstitialUnit;
    public static string RewardedUnit => UseProductionAds ? IosProdRewardedUnit : IosTestRewardedUnit;
    public static string UnitsLabel => UseProductionAds ? "ios-production" : "ios-test";
#else
    public static string InterstitialUnit => UseProductionAds ? ProdInterstitialUnit : TestInterstitialUnit;
    public static string RewardedUnit => UseProductionAds ? ProdRewardedUnit : TestRewardedUnit;
    public static string UnitsLabel => UseProductionAds ? "android-production" : "android-test";
#endif
}

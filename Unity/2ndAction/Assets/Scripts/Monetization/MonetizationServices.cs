using System;
using System.Collections.Generic;
using UnityEngine;

// ===== 広告 / 広告なしパス(依頼I、2026-10-10) =====
// Android 版 = 無料 + 広告(+ 買い切りの「広告なしパス」)、Steam 版 = 広告もモバイル課金も無し。
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
    void ShowPrivacyOptions();
}

public enum PurchaseStatus { Purchased, Pending, Cancelled, Failed, AlreadyOwned, NotReady }
public struct PurchaseResult { public PurchaseStatus status; public string productId; public string token; public string message; }
public struct RestoreResult { public bool ok; public bool owned; public string token; public string message; }

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
#if UNITY_ANDROID && OMM_ADMOB
                Ads = new AdMobAdService();
#endif
#if UNITY_ANDROID && OMM_UNITY_IAP
                Store = new UnityIapStoreService();
#endif
                break;
        }
        Debug.Log($"[Monetization] mode={Mode} ads={(Ads != null ? Ads.Name : "none")} store={(Store != null ? Store.Name : "none")} platform={Platform.Name}");
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
    public const string TestInterstitialUnit = "ca-app-pub-3940256099942544/1033173712"; // Google のテスト用(本番では使わない)
    public const string TestRewardedUnit = "ca-app-pub-3940256099942544/5224354917";
    public static string ProdInterstitialUnit = "";   // 未設定 = テスト広告のまま
    public static string ProdRewardedUnit = "";
    public static string InterstitialUnit => !Debug.isDebugBuild && !string.IsNullOrEmpty(ProdInterstitialUnit) ? ProdInterstitialUnit : TestInterstitialUnit;
    public static string RewardedUnit => !Debug.isDebugBuild && !string.IsNullOrEmpty(ProdRewardedUnit) ? ProdRewardedUnit : TestRewardedUnit;
}

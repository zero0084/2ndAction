#if UNITY_ANDROID && OMM_ADMOB
using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

// 本物の広告(Google Mobile Ads Unity plugin = AdMob、2026-10-10、依頼I)。
// パッケージ(com.google.ads.mobile)を入れて、Android の Scripting Define Symbols に OMM_ADMOB を足した時だけ入る(Steam/PC には入らない)。
// ・起動ごとに同意(UMP)を確認 → 広告を要求してよい時だけ MobileAds を初期化
// ・報酬は Show の報酬コールバックだけで渡す(閉じた/失敗では渡さない)。通知は主スレッド以外から来るので AdManager.Post で戻す
// ・ユニット ID は MonetizationConfig(開発版/未設定はテスト用 ID)
public class AdMobAdService : IAdService
{
    public string Name => "admob";
    InterstitialAd interstitial;
    RewardedAd rewarded;
    bool initialized, loadingI, loadingR;

    public void Initialize()
    {
        var req = new ConsentRequestParameters();
#if DEVELOPMENT_BUILD
        // 開発版: EEA 扱いの同意画面を試す時は DebugGeography を設定する(テスト端末の ID が必要)
#endif
        ConsentInformation.Update(req, err =>
        {
            if (err != null) Debug.LogWarning($"[AdMob] consent update failed: {err.Message}");
            ConsentForm.LoadAndShowConsentFormIfRequired(formErr =>
            {
                if (formErr != null) Debug.LogWarning($"[AdMob] consent form: {formErr.Message}");
                if (ConsentInformation.CanRequestAds()) InitAds();
                else Debug.Log("[AdMob] consent does not allow ads yet");
            });
        });
        if (ConsentInformation.CanRequestAds()) InitAds(); // 前回の同意で要求できる時は先に始める
    }

    void InitAds()
    {
        if (initialized) return;
        initialized = true;
        MobileAds.RaiseAdEventsOnUnityMainThread = false; // 通知は AdManager.Post で主スレッドへ(収益の計測のずれを避ける)
        MobileAds.Initialize(status => AdManager.Post(() => { Debug.Log("[AdMob] initialized"); Load(AdKind.Interstitial); Load(AdKind.Rewarded); }));
    }

    public bool IsReady(AdKind k) => k == AdKind.Interstitial ? interstitial != null && interstitial.CanShowAd() : rewarded != null && rewarded.CanShowAd();

    public void Load(AdKind k)
    {
        if (!initialized) return;
        if (k == AdKind.Interstitial)
        {
            if (loadingI || IsReady(k)) return;
            loadingI = true;
            InterstitialAd.Load(MonetizationConfig.InterstitialUnit, new AdRequest(), (ad, err) => AdManager.Post(() =>
            {
                loadingI = false;
                if (err != null || ad == null) { Debug.Log($"[AdMob] interstitial load failed: {err?.GetMessage()}"); return; }
                interstitial?.Destroy(); interstitial = ad;
            }));
        }
        else
        {
            if (loadingR || IsReady(k)) return;
            loadingR = true;
            RewardedAd.Load(MonetizationConfig.RewardedUnit, new AdRequest(), (ad, err) => AdManager.Post(() =>
            {
                loadingR = false;
                if (err != null || ad == null) { Debug.Log($"[AdMob] rewarded load failed: {err?.GetMessage()}"); return; }
                rewarded?.Destroy(); rewarded = ad;
            }));
        }
    }

    public void Show(AdKind k, Action onReward, Action<AdOutcome, string> onFinished)
    {
        if (!IsReady(k)) { onFinished(AdOutcome.NotReady, "not loaded"); return; }
        if (k == AdKind.Interstitial)
        {
            var ad = interstitial; interstitial = null;
            ad.OnAdFullScreenContentClosed += () => { onFinished(AdOutcome.Shown, ""); ad.Destroy(); };
            ad.OnAdFullScreenContentFailed += e => { onFinished(AdOutcome.Failed, e?.GetMessage()); ad.Destroy(); };
            ad.Show();
        }
        else
        {
            var ad = rewarded; rewarded = null;
            ad.OnAdFullScreenContentClosed += () => { onFinished(AdOutcome.Shown, ""); ad.Destroy(); };
            ad.OnAdFullScreenContentFailed += e => { onFinished(AdOutcome.Failed, e?.GetMessage()); ad.Destroy(); };
            ad.Show(reward => onReward()); // 正規の報酬通知
        }
    }

    public bool PrivacyOptionsRequired => ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;
    public void ShowPrivacyOptions() => ConsentForm.ShowPrivacyOptionsForm(err => { if (err != null) Debug.LogWarning($"[AdMob] privacy options: {err.Message}"); });
}
#endif

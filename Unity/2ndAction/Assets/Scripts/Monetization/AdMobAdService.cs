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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 開発版だけ(2026-10-26): 欧州(EEA)扱いの同意画面を試す。qa_args.txt / 起動引数で
        //   -umpGeo eea|other   … 地域を EEA / EEA 以外として扱う(テスト端末だけに効く。エミュレーターは自動でテスト端末)
        //   -umpTestDevice <ハッシュ ID> … 実機を試す時の端末(ログの「Use new ConsentDebugSettings.Builder().addTestDeviceHashedId(...)」の値)
        //   -umpReset 1 … 前回の同意を消して最初から(端末の同意の記録だけ。ゲームの保存には触れない)
        // 製品版(クローズドテストの版を含む)ではこの設定は入らない
        {
            string[] qa = QaArgs.All; string geo = null, dev = null; bool reset = false;
            for (int i = 0; i < qa.Length - 1; i++)
            {
                if (qa[i] == "-umpGeo") geo = qa[i + 1];
                else if (qa[i] == "-umpTestDevice") dev = qa[i + 1];
                else if (qa[i] == "-umpReset") reset = qa[i + 1] == "1";
            }
            if (reset) { ConsentInformation.Reset(); Debug.Log("[AdMob] consent reset (dev)"); }
            if (geo != null || dev != null)
            {
                var dbg = new ConsentDebugSettings { DebugGeography = geo == "eea" ? DebugGeography.EEA : geo == "other" ? DebugGeography.Other : DebugGeography.Disabled };
                if (dev != null) dbg.TestDeviceHashedIds = new System.Collections.Generic.List<string> { dev };
                req.ConsentDebugSettings = dbg;
                Debug.Log($"[AdMob] consent debug geography={dbg.DebugGeography} testDevice={(dev != null ? "set" : "emulator")}");
            }
        }
#endif
        // UMP の通知は Java のスレッドから来る(RaiseAdEventsOnUnityMainThread=false の時)→ 必ず主スレッドへ戻してから次へ
        ConsentInformation.Update(req, err => AdManager.Post(() =>
        {
            if (err != null) Debug.LogWarning($"[AdMob] consent update failed: {err.Message}");
            ConsentForm.LoadAndShowConsentFormIfRequired(formErr => AdManager.Post(() =>
            {
                if (formErr != null) Debug.LogWarning($"[AdMob] consent form: {formErr.Message}");
                Debug.Log($"[AdMob] consent: canRequestAds={ConsentInformation.CanRequestAds()} privacyOptions={ConsentInformation.PrivacyOptionsRequirementStatus}");
                if (ConsentInformation.CanRequestAds()) InitAds();
                else Debug.Log("[AdMob] consent does not allow ads yet");
            }));
        }));
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
        if (!ConsentInformation.CanRequestAds()) return; // 2026-10-26: 同意で要求できない間(同意を取り消した後など)は要求しない
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
            ad.OnAdFullScreenContentClosed += () => AdManager.Post(() => { onFinished(AdOutcome.Shown, ""); ad.Destroy(); });
            ad.OnAdFullScreenContentFailed += e => { string m = e?.GetMessage(); AdManager.Post(() => { onFinished(AdOutcome.Failed, m); ad.Destroy(); }); };
            ad.Show();
        }
        else
        {
            var ad = rewarded; rewarded = null;
            ad.OnAdFullScreenContentClosed += () => AdManager.Post(() => { onFinished(AdOutcome.Shown, ""); ad.Destroy(); });
            ad.OnAdFullScreenContentFailed += e => { string m = e?.GetMessage(); AdManager.Post(() => { onFinished(AdOutcome.Failed, m); ad.Destroy(); }); };
            ad.Show(reward => onReward()); // 正規の報酬通知(AdManager 側で主スレッドへ)
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版の確認用: 用意できている広告を捨てる(通信なしの時の読み込みの試験)
    public void DevDropLoaded() { interstitial?.Destroy(); interstitial = null; rewarded?.Destroy(); rewarded = null; Debug.Log("[AdMob] dev: dropped loaded ads"); }
#endif
    public bool PrivacyOptionsRequired => ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;
    public void ShowPrivacyOptions() => ConsentForm.ShowPrivacyOptionsForm(err => { string m = err?.Message; AdManager.Post(() => { if (m != null) Debug.LogWarning($"[AdMob] privacy options: {m}"); }); });
}
#endif

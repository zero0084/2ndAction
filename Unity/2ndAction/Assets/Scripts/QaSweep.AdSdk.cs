#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 本物の AdMob(テスト用の広告ユニット)の確認(2026-10-10、依頼I): Android の開発版で、qa_args.txt に「-qaAdSdk out」を書いて起動。
// 広告の「閉じる」は外から押す(adb のタップ)。この中は 表示の開始/報酬の通知/閉じた の時刻と、ゲーム側の結果だけを記録する。
//  R1 読み込み(初期化 → 報酬広告/強制広告が用意できるまでの時間)
//  R2 MILE 2倍: 本物の報酬広告 → 報酬の通知で1回だけ +base、音は広告中に止まり後で戻る
//  R3 広告ガチャ: 本物の報酬広告 → 1回分(カード+回数)
//  R4 強制広告: ゲームオーバー → ホームへ の時に本物のインタースティシャル → 閉じたらホーム
// 2026-10-26(クローズドテストの準備): 外の操作は Tools/ads/addrive.sh がログの「>>> <名前> <操作>」を見て行う
//  (close=閉じる / early=報酬の前に閉じる / home=外へ出て戻る / rotate=表示中に回す / netoff・neton=通信を切る・戻す)
//  R0 広告の設定がテスト広告 / R2 連打でも1回 / R2b 途中で閉じる / R3 広告ガチャ 1日3回(外へ出て戻る・回転を含む)/ R4 強制広告
//  R5 通信なし: 読み込みの要求の間隔が延びる・報酬広告は「準備中」で止まらない・強制広告は待たずにホームへ → 通信を戻すと読み込める
public partial class QaSweep
{
    IEnumerator WaitAdShownAndClosed(string tag, float timeout = 120f, bool checkInput = true)
    {
        float w = 0f;
        while (!AdManager.Showing && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[{tag}] showing={AdManager.Showing} audioPaused={AudioListener.pause} modal={UiInputGate.ModalOpen} note='{AdManager.LastNote}'");
        if (AdManager.Showing) Check(AudioListener.pause && UiInputGate.ModalOpen, $"{tag}: audio paused and input gated during the ad");
        w = 0f;
        while (AdManager.Showing && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[{tag}] closed after {w:F1}s note='{AdManager.LastNote}' rewards={AdManager.RewardsDelivered} dupIgnored={AdManager.DuplicateRewardsIgnored} forced={AdManager.ForcedFinishes}");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(!AudioListener.pause, $"{tag}: audio resumed after the ad");
        if (checkInput) Check(!AdManager.Showing && !UiInputGate.ModalOpen, $"{tag}: input usable after the ad"); // 強制広告の後はホームへ切り替え中(読み込みの覆い)なので、ホームに着いてから見る
    }

    // 外の操作の合図(Tools/ads/addrive.sh が見る)
    void AdCue(string name, string act) => L($">>> {name} {act}");

    IEnumerator WaitReady(AdKind k, float max)
    {
        float w = 0f; while (!Monetization.Ads.IsReady(k) && w < max) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator AdSdkModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        L($"[R0] mode={Monetization.Mode} ads={(Monetization.Ads != null ? Monetization.Ads.Name : "none")} store={(Monetization.Store != null ? Monetization.Store.Name : "none")} platform={Platform.Name} profile={MonetizationConfig.AdsProfile} unitI={MonetizationConfig.InterstitialUnit} unitR={MonetizationConfig.RewardedUnit}");
        Check(Monetization.Mode == MonetizationMode.Store && Monetization.Ads != null && Monetization.Ads.Name == "admob", "R0: real AdMob service in the Android build");
        Check(MonetizationConfig.AdsProfile == "test" && MonetizationConfig.InterstitialUnit == MonetizationConfig.TestInterstitialUnit && MonetizationConfig.RewardedUnit == MonetizationConfig.TestRewardedUnit, "R0: test ad units (production units not used)");
        if (Monetization.Ads == null) { L("adsdk done"); yield break; }
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        for (int i = 0; i < 5 && FirstRunGuide.Open; i++) { FirstRunGuideDebug.Answer(0); yield return new WaitForSecondsRealtime(0.5f); }

        // R1
        float t0 = Time.realtimeSinceStartup; float tr = -1f, ti = -1f;
        while ((tr < 0f || ti < 0f) && Time.realtimeSinceStartup - t0 < 90f)
        {
            if (tr < 0f && Monetization.Ads.IsReady(AdKind.Rewarded)) tr = Time.realtimeSinceStartup - t0;
            if (ti < 0f && Monetization.Ads.IsReady(AdKind.Interstitial)) ti = Time.realtimeSinceStartup - t0;
            yield return null;
        }
        L($"[R1] ready: rewarded {tr:F1}s interstitial {ti:F1}s (since home, app up {Time.realtimeSinceStartup:F0}s) loadRequests={AdManager.LoadRequests}");
        Check(tr >= 0f && ti >= 0f, "R1: both test ads loaded");
        Shot("ad_home_ready");

        // R2 MILE 2倍: 連打しても1回
        yield return MonetSuccessRun(200);
        yield return new WaitForSecondsRealtime(3.2f);
        int b = gm.DoubleBase, m0 = gm.TotalOwnedMile;
        Shot("ad_result_offer");
        AdCue("R2", "close");
        for (int i = 0; i < 5; i++) Claim(false); // 連打
        yield return WaitAdShownAndClosed("R2");
        yield return new WaitForSecondsRealtime(1.5f); // 遅れて届く報酬も待つ
        L($"[R2] wallet {m0} -> {gm.TotalOwnedMile} base {b} claimed={gm.DoubleClaimed} note='{gm.DoubleNote}'");
        Check(gm.DoubleClaimed ? gm.TotalOwnedMile == m0 + b : gm.TotalOwnedMile == m0, "R2: +base exactly once if rewarded, otherwise nothing");
        int m1 = gm.TotalOwnedMile; Claim(false); yield return new WaitForSecondsRealtime(2f);
        Check(!AdManager.Showing && gm.TotalOwnedMile == m1, "R2: no second MILE x2 for the same run");
        Shot("ad_result_after");
        yield return EndRun(); yield return ReloadHome();

        // R2b 報酬の前に閉じる
        yield return WaitReady(AdKind.Rewarded, 60f);
        yield return MonetSuccessRun(200);
        yield return new WaitForSecondsRealtime(3.2f);
        b = gm.DoubleBase; m0 = gm.TotalOwnedMile; int rew0 = AdManager.RewardsDelivered;
        AdCue("R2b", "early");
        Claim(false);
        yield return WaitAdShownAndClosed("R2b");
        yield return new WaitForSecondsRealtime(1.5f);
        bool rewardedB = AdManager.RewardsDelivered > rew0;
        L($"[R2b] early close: rewarded={rewardedB} wallet {m0} -> {gm.TotalOwnedMile} base {b}");
        Check(rewardedB ? gm.TotalOwnedMile == m0 + b : gm.TotalOwnedMile == m0, "R2b: closing early gives nothing (or exactly once if the reward had already come)");
        yield return EndRun(); yield return ReloadHome();
        for (int i = 0; i < 5 && FirstRunGuide.Open; i++) { FirstRunGuideDebug.Answer(0); yield return new WaitForSecondsRealtime(0.5f); }

        // R3 広告ガチャ: 1日3回(外へ出て戻る/回転を含む)、4回目は出さない
        string[] acts = { "close", "home", "rotate", "close", "close" };
        for (int k = 0; k < acts.Length && Remaining > 0; k++)
        {
            yield return WaitReady(AdKind.Rewarded, 90f);
            int left0 = Remaining, own0 = TotalOwned();
            AdCue($"R3_{k}", acts[k]);
            AdGacha();
            yield return WaitAdShownAndClosed($"R3_{k}");
            yield return new WaitForSecondsRealtime(2f);
            L($"[R3_{k}] ({acts[k]}) left {left0} -> {Remaining}, cards +{TotalOwned() - own0}, pulls {gm.AdGachaPulls}");
            Check((Remaining == left0 - 1 && TotalOwned() == own0 + 1) || (Remaining == left0 && TotalOwned() == own0), $"R3_{k}: one pull if rewarded, none otherwise");
            Shot($"ad_gacha_after_{k}");
            typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            yield return new WaitForSecondsRealtime(1f);
        }
        Check(Remaining == 0, $"R3: daily limit used up (left {Remaining})");
        int own4 = TotalOwned();
        AdGacha(); yield return new WaitForSecondsRealtime(3f);
        Check(!AdManager.Showing && Remaining == 0 && TotalOwned() == own4, "R3: 4th ad gacha is refused (3 per day)");

        // R4 強制広告
        AdManager.DevIgnoreTiming = true;
        yield return MonetDeathRun();
        yield return EndRun(); yield return ReloadHome();
        yield return MonetDeathRun();
        yield return WaitReady(AdKind.Interstitial, 60f);
        L($"[R4] why='{AdSkip()}'");
        Check(AdSkip() == "", "R4: game over ad allowed (timing ignored for the test)");
        var old = gm;
        AdCue("R4", "close");
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null);
        yield return WaitAdShownAndClosed("R4", 120f, false);
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        Check(gm != old && !gm.HasStarted && Time.timeScale == 1f, $"R4: home after the interstitial (timeScale {Time.timeScale})");
        w = 0f; while (UiInputGate.ModalOpen && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(!AdManager.Showing && !UiInputGate.ModalOpen, $"R4: home is usable after the interstitial (waited {w:F1}s for the loading cover)");
        Shot("ad_home_after_interstitial");

        // R5 通信なし
#if UNITY_ANDROID && OMM_ADMOB
        AdCue("R5", "netoff");
        yield return new WaitForSecondsRealtime(6f);
        (Monetization.Ads as AdMobAdService)?.DevDropLoaded();
        int req0 = AdManager.LoadRequests;
        yield return new WaitForSecondsRealtime(75f);
        L($"[R5] offline 75s: load requests +{AdManager.LoadRequests - req0}, next wait rewarded {AdManager.LoadWaitOf(AdKind.Rewarded):F0}s interstitial {AdManager.LoadWaitOf(AdKind.Interstitial):F0}s, ready R={Monetization.Ads.IsReady(AdKind.Rewarded)} I={Monetization.Ads.IsReady(AdKind.Interstitial)}");
        Check(AdManager.LoadRequests - req0 <= 10 && AdManager.LoadWaitOf(AdKind.Rewarded) >= 40f, "R5: load retries back off while offline (not every 2s)");
        // 報酬広告: 用意できていない → 「準備中」で止まらない、MILE は変わらない
        yield return MonetSuccessRun(200);
        yield return new WaitForSecondsRealtime(3.2f);
        m0 = gm.TotalOwnedMile; Claim(false); yield return new WaitForSecondsRealtime(2f);
        L($"[R5] claim offline: showing={AdManager.Showing} wallet {m0} -> {gm.TotalOwnedMile} note='{gm.DoubleNote}'");
        Check(!AdManager.Showing && gm.TotalOwnedMile == m0 && !UiInputGate.ModalOpen, "R5: rewarded ad not ready offline - no reward, no lock");
        yield return EndRun(); yield return ReloadHome();
        // 強制広告: 用意できていない → 待たずにホーム
        yield return MonetDeathRun();
        old = gm; float tl = Time.realtimeSinceStartup;
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null);
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        L($"[R5] game over -> home offline in {Time.realtimeSinceStartup - tl:F1}s (ad shown={AdManager.Showing})");
        Check(gm != old && !AdManager.Showing && Time.realtimeSinceStartup - tl < 6f, "R5: offline game over goes home without waiting for an ad");
        AdCue("R5b", "neton");
        float tn = Time.realtimeSinceStartup;
        yield return WaitReady(AdKind.Rewarded, 330f);
        L($"[R5b] back online: rewarded ready={Monetization.Ads.IsReady(AdKind.Rewarded)} after {Time.realtimeSinceStartup - tn:F0}s");
        Check(Monetization.Ads.IsReady(AdKind.Rewarded), "R5b: ads load again after the network comes back");
#endif
        AdManager.DevIgnoreTiming = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
        L("adsdk done");
    }
}
#endif

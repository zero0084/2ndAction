#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 本物の AdMob(テスト用の広告ユニット)の確認(2026-10-10、依頼I): Android の開発版で、qa_args.txt に「-qaAdSdk out」を書いて起動。
// 広告の「閉じる」は外から押す(adb のタップ)。この中は 表示の開始/報酬の通知/閉じた の時刻と、ゲーム側の結果だけを記録する。
//  R1 読み込み(初期化 → 報酬広告/強制広告が用意できるまでの時間)
//  R2 MILE 2倍: 本物の報酬広告 → 報酬の通知で1回だけ +base、音は広告中に止まり後で戻る
//  R3 広告ガチャ: 本物の報酬広告 → 1回分(カード+回数)
//  R4 強制広告: ゲームオーバー → ホームへ の時に本物のインタースティシャル → 閉じたらホーム
public partial class QaSweep
{
    IEnumerator WaitAdShownAndClosed(string tag, float timeout = 120f)
    {
        float w = 0f;
        while (!AdManager.Showing && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[{tag}] showing={AdManager.Showing} audioPaused={AudioListener.pause} modal={UiInputGate.ModalOpen} note='{AdManager.LastNote}'");
        if (AdManager.Showing) Check(AudioListener.pause && UiInputGate.ModalOpen, $"{tag}: audio paused and input gated during the ad");
        w = 0f;
        while (AdManager.Showing && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[{tag}] closed after {w:F1}s note='{AdManager.LastNote}' rewards={AdManager.RewardsDelivered} dupIgnored={AdManager.DuplicateRewardsIgnored}");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(!AudioListener.pause, $"{tag}: audio resumed after the ad");
    }

    IEnumerator AdSdkModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        L($"[R0] mode={Monetization.Mode} ads={(Monetization.Ads != null ? Monetization.Ads.Name : "none")} store={(Monetization.Store != null ? Monetization.Store.Name : "none")} platform={Platform.Name} unitI={MonetizationConfig.InterstitialUnit} unitR={MonetizationConfig.RewardedUnit}");
        Check(Monetization.Mode == MonetizationMode.Store && Monetization.Ads != null && Monetization.Ads.Name == "admob", "R0: real AdMob service in the Android build");
        if (Monetization.Ads == null) yield break;
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
        L($"[R1] ready: rewarded {tr:F1}s interstitial {ti:F1}s (since home, app up {Time.realtimeSinceStartup:F0}s)");
        Check(tr >= 0f && ti >= 0f, "R1: both test ads loaded");
        Shot("ad_home_ready");

        // R2
        yield return MonetSuccessRun(200);
        yield return new WaitForSecondsRealtime(3.2f);
        int b = gm.DoubleBase, m0 = gm.TotalOwnedMile;
        Shot("ad_result_offer");
        L("[R2] >>> rewarded ad for MILE x2 (close it from outside after the reward)");
        Claim(false); Claim(false);
        yield return WaitAdShownAndClosed("R2");
        yield return new WaitForSecondsRealtime(1.5f); // 遅れて届く報酬も待つ
        L($"[R2] wallet {m0} -> {gm.TotalOwnedMile} base {b} claimed={gm.DoubleClaimed} note='{gm.DoubleNote}'");
        Check(gm.DoubleClaimed ? gm.TotalOwnedMile == m0 + b : gm.TotalOwnedMile == m0, "R2: +base exactly once if rewarded, otherwise nothing");
        Shot("ad_result_after");
        yield return EndRun();
        yield return ReloadHome();
        for (int i = 0; i < 5 && FirstRunGuide.Open; i++) { FirstRunGuideDebug.Answer(0); yield return new WaitForSecondsRealtime(0.5f); }

        // R3
        w = 0f; while (!Monetization.Ads.IsReady(AdKind.Rewarded) && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        int left0 = Remaining, own0 = TotalOwned();
        L($"[R3] >>> rewarded ad for the ad gacha (left {left0})");
        AdGacha();
        yield return WaitAdShownAndClosed("R3");
        yield return new WaitForSecondsRealtime(2f);
        L($"[R3] left {left0} -> {Remaining}, cards +{TotalOwned() - own0}, pulls {gm.AdGachaPulls}");
        Check((Remaining == left0 - 1 && TotalOwned() == own0 + 1) || (Remaining == left0 && TotalOwned() == own0), "R3: one pull if rewarded, none otherwise");
        Shot("ad_gacha_after");
        typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);

        // R4
        AdManager.DevIgnoreTiming = true;
        yield return MonetDeathRun();
        yield return EndRun(); yield return ReloadHome();
        yield return MonetDeathRun();
        w = 0f; while (!Monetization.Ads.IsReady(AdKind.Interstitial) && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[R4] why='{AdSkip()}'");
        Check(AdSkip() == "", "R4: game over ad allowed (timing ignored for the test)");
        var old = gm;
        L("[R4] >>> interstitial on the way home (close it from outside)");
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null);
        yield return WaitAdShownAndClosed("R4");
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        Check(gm != old && !gm.HasStarted && Time.timeScale == 1f, $"R4: home after the interstitial (timeScale {Time.timeScale})");
        Shot("ad_home_after_interstitial");
        AdManager.DevIgnoreTiming = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

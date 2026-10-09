#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 広告/広告なしパス(依頼I、2026-10-10)の自動テスト: -qaMonet <dir> -monetizationMock 1(テスト用データの中で行う。模擬の広告/購入だけ)
//  C 強制広告: 初回ランの後は出ない/起動5分/間隔5分/パス/1結果1回/在庫なしは待たない/終わったらホームへ
//  A MILE 2倍: 通常分は保存済み、報酬の通知でだけ同じ額を1回。閉じた/失敗/在庫なし/二重/遅れ/連打/死亡
//  D 広告ガチャ: 1日3回、失敗は数えない、再起動、日付、時計の戻し、パスと共通の枠(購入しても回数は戻らない)
//  E 購入: 成功/支払い待ち/キャンセル/失敗/オフライン/復元/オフライン起動/返金、本物の権利のキーに触れない
//  F 広告の後の 音/入力/時間、G 通常のデータに混ざらない
//  -qaMonetNone: 模擬なしの起動(PC/Steam と同じ)で何も始めない/出さない
public partial class QaSweep
{
    static readonly string[] MonetNormalKeys = { "TotalOwnedMile", "OwnedCardsV1", AdRewards.Key, AdRewards.RunsFinishedKey };

    IEnumerator WaitAdDone(float timeout = 6f)
    {
        float w = 0f;
        yield return null;
        while (AdManager.Showing && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
    }

    IEnumerator MonetSuccessRun(int enemyMile)
    {
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(1.5f);
        typeof(GameManager).GetProperty("RunEnemyMile").SetValue(gm, enemyMile);
        stopKeepAlive = true;
        gm.Win();
        yield return new WaitForSecondsRealtime(0.5f);
    }

    IEnumerator MonetDeathRun()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(1.5f);
        stopKeepAlive = true;
        typeof(GameManager).GetMethod("FinishRun", NP).Invoke(gm, null);
        yield return new WaitForSecondsRealtime(0.5f);
    }

    IEnumerator LeaveToHome()
    {
        var old = gm;
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null);
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance; pc = PlayerController.Instance;
    }

    string AdSkip() { gm.ShouldShowGameOverAd(out string why); return why; }
    void Claim(bool pass) => typeof(GameManager).GetMethod("StartDoubleClaim", NP).Invoke(gm, new object[] { pass });
    void AdGacha() => gm.OnAdGachaTapped();
    int Remaining => AdRewards.GachaRemaining(TrustedClock.DayKey);

    IEnumerator MonetizationModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        if (Monetization.Mode != MonetizationMode.Mock) { Check(false, "start with -monetizationMock 1 (mode is " + Monetization.Mode + ")"); yield break; }
        var normalRaw = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var k in MonetNormalKeys) normalRaw[k] = Platform.Save.GetString(k, "") + "|" + Platform.Save.GetInt(k, -12345);
        bool realKey0 = Platform.Save.HasKey(NoAdsPass.Key);
        // 端末で共有の値(前回の広告の時刻/見た一番新しい時刻)は最後に元へ戻す
        string lastAd0 = Platform.Save.HasKey(AdManager.LastAdEndUtcKey) ? Platform.Save.GetString(AdManager.LastAdEndUtcKey, "") : null, seen0 = Platform.Save.HasKey(TrustedClock.LastSeenKey) ? Platform.Save.GetString(TrustedClock.LastSeenKey, "") : null;
        AdManager.DevForgetLastAd();
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        MockAdService.AdSeconds = 0.6f; MockAdService.AutoComplete = true; MockAdService.Mode = MockAdService.Behaviour.Normal;
        MockStoreService.Mode = MockStoreService.Behaviour.Success; MockStoreService.DevClearAccount(); NoAdsPass.DevMockSet(false);
        AdManager.DevIgnoreTiming = false; AdManager.DevSessionAgeBonus = 0f; TrustedClock.DevShiftHours = 0;
        yield return new WaitForSecondsRealtime(2.5f); // 模擬の広告の用意(AdManager が 120 フレームごとに読み込む)
        Check(Monetization.Ads.IsReady(AdKind.Interstitial) && Monetization.Ads.IsReady(AdKind.Rewarded), "setup: mock ads preloaded");

        // ================================================================ C
        L("== C: game over ad ==");
        yield return MonetDeathRun();
        L($"[C1] first run: offered={gm.DoubleOffered} why='{AdSkip()}' runsFinished={AdRewards.RunsFinished}");
        Check(!gm.DoubleOffered, "C1: no MILE x2 after a game over");
        Check(AdSkip() == "first run", "C1: no ad after the very first run's game over");
        int shows0 = AdManager.ShowsStarted;
        yield return LeaveToHome();
        Check(AdManager.ShowsStarted == shows0, "C1: went home without an ad");

        yield return MonetDeathRun();
        L($"[C2] second run, fresh session: why='{AdSkip()}'");
        Check(AdSkip().StartsWith("session"), "C2: no ad within 5 minutes of launch");
        AdManager.DevSessionAgeBonus = 400f;
        Check(AdSkip() == "", $"C2: after 5 minutes (simulated) the ad is allowed ('{AdSkip()}')");
        float ts0 = Time.timeScale;
        var old = gm;
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null);
        yield return null;
        // F: 広告の間
        Check(AdManager.Showing && AudioListener.pause && UiInputGate.ModalOpen, "F: during the ad: showing, audio paused, input gated");
        typeof(GameManager).GetMethod("LeaveResult", NP).Invoke(gm, null); // 連打
        Check(AdManager.ShowsStarted == shows0 + 1, "C2: repeated taps start only one ad");
        Shot("monet_gameover_ad");
        yield return WaitAdDone();
        Check(!AudioListener.pause, "F: audio resumed after the ad");
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        Check(gm != old && !gm.HasStarted, "C2: home after the ad");
        Check(Mathf.Approximately(Time.timeScale, ts0) || Time.timeScale == 1f, $"F: time scale is normal after the ad ({Time.timeScale})");

        yield return MonetDeathRun();
        L($"[C3] right after an ad: why='{AdSkip()}'");
        Check(AdSkip().StartsWith("cooldown"), "C3: no ad within 5 minutes of the previous ad");
        AdManager.DevIgnoreTiming = true;
        MockAdService.Mode = MockAdService.Behaviour.NoFill;
        Check(AdSkip().StartsWith("not loaded"), $"C4: no fill -> skip without waiting ('{AdSkip()}')");
        float t0 = Time.realtimeSinceStartup;
        int shows1 = AdManager.ShowsStarted;
        yield return LeaveToHome();
        Check(AdManager.ShowsStarted == shows1 && Time.realtimeSinceStartup - t0 < 4f, "C4: went home immediately with no ad");
        MockAdService.Mode = MockAdService.Behaviour.Normal;
        yield return new WaitForSecondsRealtime(2.5f);

        yield return MonetDeathRun();
        MockAdService.Mode = MockAdService.Behaviour.ShowFail;
        old = gm;
        yield return LeaveToHome();
        Check(gm != old && !gm.HasStarted, "C5: ad failed to show -> still goes home");
        MockAdService.Mode = MockAdService.Behaviour.Normal;
        yield return new WaitForSecondsRealtime(2.5f);

        yield return MonetDeathRun();
        NoAdsPass.DevMockSet(true);
        Check(AdSkip() == "no-ads pass", "C6: no game over ad with the no-ads pass");
        NoAdsPass.DevMockSet(false);
        typeof(GameManager).GetField("resultAdTried", NP).SetValue(gm, true);
        Check(AdSkip().StartsWith("already"), "C7: at most once per result screen");
        yield return EndRun();
        yield return ReloadHome();

        // ================================================================ A
        L("== A: MILE x2 ==");
        AdManager.DevIgnoreTiming = false; AdManager.DevSessionAgeBonus = 0f;
        yield return new WaitForSecondsRealtime(2.5f);
        yield return MonetSuccessRun(123);
        int b = gm.DoubleBase, m0 = gm.TotalOwnedMile;
        L($"[A1] base={b} runMile={gm.RunMile} wallet={m0} offered={gm.DoubleOffered} why='{AdSkip()}'");
        Check(b == gm.RunMile && b >= 123, "A1: the base is the carried MILE of the run");
        Check(SaveStore.GetInt("TotalOwnedMile", -1) == m0, "A1: the normal MILE is saved before any ad");
        Check(gm.DoubleOffered && !gm.DoubleClaimed, "A1: MILE x2 is offered after a success (no cooldown for a chosen ad)");
        Check(AdSkip().StartsWith("success"), "A1: no forced ad after a success");
        Shot("monet_double_offer");
        MockAdService.Mode = MockAdService.Behaviour.CloseEarly;
        Claim(false); yield return WaitAdDone();
        Check(gm.TotalOwnedMile == m0 && !gm.DoubleClaimed, $"A2: closed early -> nothing ({gm.TotalOwnedMile}) note='{gm.DoubleNote}'");
        MockAdService.Mode = MockAdService.Behaviour.NoFill;
        string why;
        Check(!AdManager.CanShowRewardedNow(out why), $"A3: no fill -> button shows a reason ('{why}')");
        Claim(false); yield return WaitAdDone();
        Check(gm.TotalOwnedMile == m0 && !string.IsNullOrEmpty(gm.DoubleNote), $"A3: no fill -> nothing, reason '{gm.DoubleNote}'");
        MockAdService.Mode = MockAdService.Behaviour.ShowFail;
        Claim(false); yield return WaitAdDone();
        Check(gm.TotalOwnedMile == m0, "A4: show failure -> nothing");
        MockAdService.Mode = MockAdService.Behaviour.DuplicateReward;
        yield return new WaitForSecondsRealtime(2.5f);
        int dup0 = AdManager.DuplicateRewardsIgnored;
        Claim(false); Claim(false); // 連打
        yield return WaitAdDone();
        L($"[A5] wallet {m0} -> {gm.TotalOwnedMile}, dupIgnored {AdManager.DuplicateRewardsIgnored - dup0}");
        Check(gm.TotalOwnedMile == m0 + b && gm.DoubleClaimed, "A5: rewarded -> exactly +base once (taps x2, reward x2)");
        Check(AdManager.DuplicateRewardsIgnored == dup0 + 1, "A5: the duplicate reward callback was ignored");
        Check(SaveStore.GetInt("TotalOwnedMile", -1) == gm.TotalOwnedMile, "A5: the extra MILE is saved");
        Shot("monet_double_done");
        MockAdService.Mode = MockAdService.Behaviour.Normal;
        Claim(false); Claim(true); yield return WaitAdDone();
        Check(gm.TotalOwnedMile == m0 + b, "A6: no second x2 for the same run (ad or pass)");
        string runId = RunLedger.Current.runId;
        AdRewards.Reload();
        Check(AdRewards.IsDoubled(runId), "A6: the claim survives a restart (saved with the run ID)");
        yield return EndRun();
        yield return ReloadHome();

        // 遅れて届く報酬
        yield return new WaitForSecondsRealtime(2.5f);
        yield return MonetSuccessRun(50);
        b = gm.DoubleBase; m0 = gm.TotalOwnedMile;
        MockAdService.Mode = MockAdService.Behaviour.LateReward;
        Claim(false); yield return WaitAdDone();
        yield return new WaitForSecondsRealtime(1.6f);
        Check(gm.TotalOwnedMile == m0 + b && gm.DoubleClaimed, $"A7: a late reward (after close, twice) adds exactly once ({m0} -> {gm.TotalOwnedMile}, base {b})");
        MockAdService.Mode = MockAdService.Behaviour.Normal;
        yield return EndRun();
        yield return ReloadHome();

        // パス: 広告なしで1回
        yield return MonetSuccessRun(40);
        b = gm.DoubleBase; m0 = gm.TotalOwnedMile;
        NoAdsPass.DevMockSet(true);
        int s2 = AdManager.ShowsStarted;
        Claim(true); Claim(true);
        yield return null;
        Check(gm.TotalOwnedMile == m0 + b && AdManager.ShowsStarted == s2, "A8: pass -> x2 without an ad, once");
        NoAdsPass.DevMockSet(false);
        yield return EndRun();
        yield return ReloadHome();

        // 死亡では出ない
        yield return MonetDeathRun();
        Check(!gm.DoubleOffered, "A9: no MILE x2 on death");
        yield return EndRun();
        yield return ReloadHome();

        // ================================================================ D
        L("== D: ad gacha ==");
        yield return new WaitForSecondsRealtime(2.5f);
        AdRewards.DevResetGacha();
        int mileG = gm.TotalOwnedMile, owned0 = TotalOwned();
        Check(Remaining == 3, $"D1: 3 per day ({Remaining})");
        AdGacha(); AdGacha(); // 連打
        yield return WaitAdDone();
        Check(Remaining == 2 && TotalOwned() == owned0 + 1 && gm.TotalOwnedMile == mileG, $"D1: one ad pull -> 1 card, no MILE cost, 2 left ({Remaining}, cards +{TotalOwned() - owned0})");
        Shot("monet_adgacha");
        MockAdService.Mode = MockAdService.Behaviour.CloseEarly;
        yield return new WaitForSecondsRealtime(2.5f);
        AdGacha(); yield return WaitAdDone();
        MockAdService.Mode = MockAdService.Behaviour.NoFill;
        AdGacha(); yield return WaitAdDone();
        Check(Remaining == 2 && TotalOwned() == owned0 + 1, "D2: closed early / no fill do not use a pull");
        MockAdService.Mode = MockAdService.Behaviour.Normal;
        for (int i = 0; i < 2; i++) { yield return new WaitForSecondsRealtime(2.5f); AdGacha(); yield return WaitAdDone(); }
        Check(Remaining == 0 && TotalOwned() == owned0 + 3, $"D3: 3 pulls used ({Remaining})");
        yield return new WaitForSecondsRealtime(2.5f);
        int s3 = AdManager.ShowsStarted;
        AdGacha(); yield return WaitAdDone();
        Check(AdManager.ShowsStarted == s3 && TotalOwned() == owned0 + 3, "D3: a 4th pull is refused without an ad");
        AdRewards.Reload();
        yield return ReloadHome();
        Check(Remaining == 0, "D4: the count survives a restart");
        string day0 = TrustedClock.DayKey;
        TrustedClock.DevShiftHours = 24; TrustedClock.NoteUse();
        Check(TrustedClock.DayKey != day0 && Remaining == 3, $"D5: next day (JST 0:00) -> 3 again ({day0} -> {TrustedClock.DayKey})");
        AdGacha(); yield return WaitAdDone();
        Check(Remaining == 2, "D6: one ad pull on the new day");
        TrustedClock.DevShiftHours = 0;
        Check(TrustedClock.DayKey != day0 && Remaining == 2, $"D7: setting the clock back does not bring back pulls ({TrustedClock.DayKey})");
        // 購入しても残りは戻らない/パスと共通の枠
        NoAdsPass.Purchase(null);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(NoAdsPass.Owned && Remaining == 2, "D8: buying the pass does not reset the remaining pulls");
        int s4 = AdManager.ShowsStarted, o4 = TotalOwned();
        AdGacha(); AdGacha(); AdGacha();
        yield return null;
        Check(Remaining == 0 && TotalOwned() == o4 + 2 && AdManager.ShowsStarted == s4, $"D8: pass pulls share the same 3 (2 more, no ad, {Remaining} left)");
        Check(gm.TotalOwnedMile == mileG, "D8: no MILE spent by ad/pass pulls");

        // ================================================================ E
        L("== E: purchase (mock store) ==");
        MockStoreService.DevClearAccount(); NoAdsPass.DevMockSet(false);
        foreach (var mode in new[] { MockStoreService.Behaviour.Cancel, MockStoreService.Behaviour.Fail, MockStoreService.Behaviour.Offline })
        {
            MockStoreService.Mode = mode;
            NoAdsPass.Purchase(null);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(!NoAdsPass.Owned, $"E1: {mode} -> not granted ('{NoAdsPass.LastNote}')");
        }
        MockStoreService.Mode = MockStoreService.Behaviour.PendingThenComplete;
        NoAdsPass.Purchase(null);
        yield return new WaitForSecondsRealtime(0.4f);
        Check(!NoAdsPass.Owned && NoAdsPass.PendingPayment, "E2: pending -> not granted yet");
        yield return new WaitForSecondsRealtime(4.5f);
        Check(NoAdsPass.Owned && !NoAdsPass.PendingPayment, "E2: payment completed later -> granted");
        MockStoreService.Mode = MockStoreService.Behaviour.Success;
        NoAdsPass.DevMockSet(false); // 再インストール/機種変更(端末の控えが無い)
        NoAdsPass.Check("qa restore");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(NoAdsPass.Owned, "E3: restore after reinstall -> owned");
        MockStoreService.Mode = MockStoreService.Behaviour.Offline;
        NoAdsPass.Check("qa offline");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(NoAdsPass.Owned, "E4: offline check keeps the pass (no ads coming back)");
        MockStoreService.Mode = MockStoreService.Behaviour.Success; MockStoreService.Refunded = true;
        NoAdsPass.Check("qa refund 1");
        yield return new WaitForSecondsRealtime(0.3f);
        bool after1 = NoAdsPass.Owned;
        NoAdsPass.Check("qa refund 2");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(after1 && !NoAdsPass.Owned, "E5: refund -> removed after 2 confirmed 'not owned' answers");
        MockStoreService.Refunded = false;
        Check(Platform.Save.HasKey(NoAdsPass.Key) == realKey0, "E6: the real entitlement key was never written by the mock");

        // ================================================================ G
        L("== G: test data separation ==");
        yield return EndRun();
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
        int changed = 0;
        foreach (var k in MonetNormalKeys) if (Platform.Save.GetString(k, "") + "|" + Platform.Save.GetInt(k, -12345) != normalRaw[k]) { changed++; L($"[G] normal key changed: {k}"); }
        Check(changed == 0, "G: the normal data (MILE/cards/ad records) is unchanged after all ad/pass tests");
        AdManager.DevIgnoreTiming = false; MockAdService.AutoComplete = false; TrustedClock.DevShiftHours = 0;
        if (lastAd0 == null) Platform.Save.DeleteKey(AdManager.LastAdEndUtcKey); else Platform.Save.SetString(AdManager.LastAdEndUtcKey, lastAd0);
        if (seen0 == null) Platform.Save.DeleteKey(TrustedClock.LastSeenKey); else Platform.Save.SetString(TrustedClock.LastSeenKey, seen0);
        Platform.Save.Save();
    }

    // 模擬なし(PC/Steam と同じ): 何も始めない/出さない
    IEnumerator MonetizationNoneQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        L($"[N] mode={Monetization.Mode} ads={Monetization.Ads} store={Monetization.Store} platform={Platform.Name}");
        Check(Monetization.Mode == MonetizationMode.None && Monetization.Ads == null && Monetization.Store == null, "N: no ad/store service");
        Check(GameObject.Find("AdManager") == null && GameObject.Find("TrustedClock") == null && GameObject.Find("MockAdService") == null, "N: nothing started (no ad manager / clock / mock)");
        Check(!gm.AdGachaAvailable && !NoAdsPass.Owned, "N: no ad gacha, no pass");
        MonetizationUi.OpenPass();
        Check(!MonetizationUi.Open, "N: the pass screen cannot open");
        Shot("monet_none_home");
        var sp = FindAnyObjectByType<SettingsPanel>();
        if (sp != null)
        {
            float h = (float)typeof(SettingsPanel).GetMethod("MeasureNoAds", NP).Invoke(sp, null);
            Check(h == 0f, "N: no ad/pass section in settings");
        }
        yield return MonetSuccessRun(100);
        Check(!gm.DoubleOffered, "N: no MILE x2 on the result screen");
        Shot("monet_none_result");
        yield return EndRun();
    }
    // 見た目の確認(-qaMonetShots <dir> -monetizationMock 1): 結果画面のボタン/ホームの広告ガチャ/設定/パスの画面/受け取り後
    IEnumerator MonetizationShotsQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        string lastAd0 = Platform.Save.HasKey(AdManager.LastAdEndUtcKey) ? Platform.Save.GetString(AdManager.LastAdEndUtcKey, "") : null;
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        MockAdService.AdSeconds = 0.6f; MockAdService.AutoComplete = true;
        yield return new WaitForSecondsRealtime(2.5f);
        yield return MonetSuccessRun(1234);
        yield return new WaitForSecondsRealtime(3.2f);
        Shot("s1_result_double"); yield return new WaitForSecondsRealtime(0.5f);
        Claim(false); yield return WaitAdDone(); yield return new WaitForSecondsRealtime(0.3f);
        Shot("s2_result_claimed"); yield return new WaitForSecondsRealtime(0.5f);
        yield return EndRun();
        yield return ReloadHome();
        for (int i = 0; i < 5 && FirstRunGuide.Open; i++) { FirstRunGuideDebug.Answer(0); yield return new WaitForSecondsRealtime(0.5f); }
        yield return new WaitForSecondsRealtime(1f);
        Shot("s3_home_adgacha"); yield return new WaitForSecondsRealtime(0.5f);
        AdGacha(); yield return WaitAdDone(); yield return new WaitForSecondsRealtime(1.5f);
        Shot("s4_home_adgacha_card"); yield return new WaitForSecondsRealtime(0.5f);
        typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
        yield return new WaitForSecondsRealtime(0.5f);
        SettingsPanel.Instance.Open();
        yield return new WaitForSecondsRealtime(1f);
        SetPrivate(SettingsPanel.Instance, "scroll", new Vector2(0f, 99999f)); yield return new WaitForSecondsRealtime(0.3f);
        Shot("s5_settings"); yield return new WaitForSecondsRealtime(0.5f);
        MonetizationUi.OpenPass();
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("s6_pass"); yield return new WaitForSecondsRealtime(0.5f);
        NoAdsPass.Purchase(null);
        yield return new WaitForSecondsRealtime(0.8f);
        Shot("s7_pass_owned"); yield return new WaitForSecondsRealtime(0.5f);
        MonetizationUi.Close();
        SettingsPanel.Instance.Close();
        yield return new WaitForSecondsRealtime(1f);
        Shot("s8_home_pass"); yield return new WaitForSecondsRealtime(0.5f);
        yield return MonetDeathRun();
        yield return new WaitForSecondsRealtime(3.2f);
        Shot("s9_result_failed"); yield return new WaitForSecondsRealtime(0.5f);
        yield return EndRun();
        NoAdsPass.DevMockSet(false); MockStoreService.DevClearAccount();
        SaveProfile.Switch(false, true);
        if (lastAd0 == null) Platform.Save.DeleteKey(AdManager.LastAdEndUtcKey); else Platform.Save.SetString(AdManager.LastAdEndUtcKey, lastAd0);
        Platform.Save.Save();
        yield return ReloadHome();
    }
}
#endif

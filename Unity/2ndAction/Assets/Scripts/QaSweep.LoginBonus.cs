#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// ログイン特典(2026-11-18)の自動試験: -qaLoginBonus <dir> -loginBonusOn -lbCase <case>(-memSave / -fileSave で本物のセーブに触れない)
//  windows     : Windows 版 → ホームで +5,000(1回)/ ホームへ入り直し・読み直しで二重にならない / 初期化しても同じ日は無い / 翌日にまた +5,000
//  restart1/2  : 本当の再起動(-fileSave で同じファイル)。1回目で受け取り、2回目の起動の同じ日では受け取らない
//  mobilefree  : 無料のモバイル版(-loginBonusAs mobile、パスなし)→ 何日たっても付かない
//  mobilepass  : モバイル + 広告なしパス(-loginBonusAs mobile -monetizationMock 1)→ 購入の前は無し / 失敗・キャンセル・支払い待ちでは無し /
//                購入した時にその場で +5,000(MILE 2倍は掛からない)/ 復元を繰り返しても二重にならない / 翌日はホームで +5,000
//  bothsame    : Windows 版 + パス(-monetizationMock 1)→ 1日 5,000 だけ
public partial class QaSweep
{
    IEnumerator LoginBonusModeQa()
    {
        yield return WaitHome();
        while (TitleScreen.Showing) yield return null;
        string c = Arg("-lbCase", "windows");
        L($"[lb] case={c} platform={Application.platform} windowsEdition={DailyLoginBonus.IsWindowsEdition} mode={Monetization.Mode} {DailyLoginBonus.DevSummary}");
        string amt = DailyLoginBonus.Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        string toast = Loc.F(DailyLoginBonus.ToastText, amt);

        if (c == "windows" || c == "restart1" || c == "bothsame")
        {
            Check(DailyLoginBonus.IsWindowsEdition, "W0: Windows edition");
            // ホームに入った後、見回り(1秒ごと)で付く(試験がホームを確かめる前に付いていることがある → その時は付く前の額を逆算)
            int m0 = gm.TotalOwnedMile - (DailyLoginBonus.ClaimedToday ? DailyLoginBonus.Amount : 0);
            L($"[lb] at home: claimed already={DailyLoginBonus.ClaimedToday} claims={DailyLoginBonus.Claims} wallet={gm.TotalOwnedMile}");
            yield return WaitFor(() => DailyLoginBonus.ClaimedToday, 4f);
            Check(DailyLoginBonus.Claims == 1 && DailyLoginBonus.Total == DailyLoginBonus.Amount, $"W1: exactly one claim of {DailyLoginBonus.Amount} recorded (claims {DailyLoginBonus.Claims}, total {DailyLoginBonus.Total})");
            Shot("lb_windows_home_toast");
            Check(DailyLoginBonus.ClaimedToday && gm.TotalOwnedMile == m0 + DailyLoginBonus.Amount, $"W1: first home today -> +{DailyLoginBonus.Amount} MILE (wallet {m0} -> {gm.TotalOwnedMile})");
            Check(NoticeQueue.ToastsShown > 0, $"W1: toast shown: '{toast}'");
            int m1 = gm.TotalOwnedMile;
            if (c == "restart1") { L("[lb] restart1 done (run restart2 with the same -fileSave)"); yield break; }
            if (c == "bothsame")
            {
                MockStoreService.DevClearAccount(); NoAdsPass.DevMockSet(false); MockStoreService.Mode = MockStoreService.Behaviour.Success;
                NoAdsPass.Purchase(null);
                yield return new WaitForSecondsRealtime(0.6f);
                Check(NoAdsPass.Owned && gm.TotalOwnedMile == m1, $"B1: Windows + pass bought the same day -> still 5,000 total today (wallet {gm.TotalOwnedMile})");
                TrustedClock.DevShiftHours += 24;
                yield return new WaitForSecondsRealtime(1.6f);
                Check(gm.TotalOwnedMile == m1 + DailyLoginBonus.Amount, $"B2: next day with both -> +5,000 once (wallet {gm.TotalOwnedMile})");
                yield break;
            }
            // 同じ日: もう一度呼ぶ/読み直し/ホームへ入り直し
            Check(!DailyLoginBonus.TryGrant("qa again"), "W2: second try the same day -> nothing");
            DailyLoginBonus.Reload();
            yield return new WaitForSecondsRealtime(1.5f);
            Check(gm.TotalOwnedMile == m1, $"W2: reload of the record -> nothing (wallet {gm.TotalOwnedMile})");
            yield return MonetDeathRun();
            yield return LeaveToHome();
            while (TitleScreen.Showing) yield return null;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m1, $"W3: back to home after a run the same day -> nothing (wallet {gm.TotalOwnedMile})");
            // 同じ日にセーブを初期化(進行の記録だけ消える)しても、設定の控えで2回目は無い
            SaveStore.DeleteKey(DailyLoginBonus.Key); SaveStore.Save(); DailyLoginBonus.Reload();
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m1 && DailyLoginBonus.ClaimedToday, "W4: progress record erased (data reset) the same day -> no second bonus");
            // 翌日
            string d0 = DailyLoginBonus.Today;
            TrustedClock.DevShiftHours += 24;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(DailyLoginBonus.Today != d0 && gm.TotalOwnedMile == m1 + DailyLoginBonus.Amount, $"W5: next day ({d0} -> {DailyLoginBonus.Today}) at home -> +5,000 again (wallet {gm.TotalOwnedMile})");
            Shot("lb_windows_nextday");
            int m2 = gm.TotalOwnedMile;
            // 2日あけた(来なかった日は持ち越さない)
            TrustedClock.DevShiftHours += 48;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m2 + DailyLoginBonus.Amount, $"W6: after missing a day -> only today's 5,000 (no carry-over, wallet {gm.TotalOwnedMile})");
            // 日付の切り替え: 日本時間 0時の前後
            L($"[lb] day key now {DailyLoginBonus.Today}, until next day {TrustedClock.UntilNextDay}");
            L("[lb] log: " + string.Join(" | ", DailyLoginBonus.Log));
            yield break;
        }
        // 2026-11-19: 本物の「進行の初期化」(設定は残す。DefaultSave.WriteNewProgress)を同じ日に通しても2回目は無い。翌日はまた付く
        if (c == "reset")
        {
            Check(DailyLoginBonus.IsWindowsEdition, "X0: Windows edition");
            yield return WaitFor(() => DailyLoginBonus.ClaimedToday, 4f);
            Check(DailyLoginBonus.ClaimedToday && DailyLoginBonus.Claims == 1, $"X1: claimed today before the reset (claims {DailyLoginBonus.Claims})");
            SaveSystem.DevResetProgress();
            yield return new WaitForSecondsRealtime(0.5f);
            int mr = gm.TotalOwnedMile;
            L($"[lb] after progress reset: wallet {mr} claims(progress record) {DailyLoginBonus.Claims} claimedToday {DailyLoginBonus.ClaimedToday}");
            yield return new WaitForSecondsRealtime(2.0f);
            Check(gm.TotalOwnedMile == mr && DailyLoginBonus.ClaimedToday && !DailyLoginBonus.TryGrant("qa after reset"), $"X2: progress reset the same day -> no second bonus (wallet {mr} -> {gm.TotalOwnedMile}, '{DailyLoginBonus.LastResult}')");
            TrustedClock.DevShiftHours += 24;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == mr + DailyLoginBonus.Amount, $"X3: next day after the reset -> +5,000 (wallet {gm.TotalOwnedMile})");
            yield break;
        }
        if (c == "restart2")
        {
            int m0 = gm.TotalOwnedMile;
            yield return new WaitForSecondsRealtime(2.5f);
            Check(DailyLoginBonus.ClaimedToday && gm.TotalOwnedMile == m0 && DailyLoginBonus.Claims == 1, $"R2: after a real restart the same day -> no second bonus (wallet {m0} -> {gm.TotalOwnedMile}, claims {DailyLoginBonus.Claims})");
            yield break;
        }
        if (c == "mobilefree")
        {
            Check(!DailyLoginBonus.IsWindowsEdition && !NoAdsPass.Owned, "F0: free mobile (no pass)");
            int m0 = gm.TotalOwnedMile;
            yield return new WaitForSecondsRealtime(2.5f);
            TrustedClock.DevShiftHours += 24;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m0 && DailyLoginBonus.Claims == 0 && !DailyLoginBonus.TryGrant("qa"), $"F1: free mobile gets nothing (wallet {gm.TotalOwnedMile}, '{DailyLoginBonus.LastResult}')");
            Shot("lb_mobilefree_home");
            yield break;
        }
        if (c == "mobilepass")
        {
            Check(!DailyLoginBonus.IsWindowsEdition && Monetization.Mode == MonetizationMode.Mock, "P0: mobile with the mock store");
            MockStoreService.DevClearAccount(); NoAdsPass.DevMockSet(false);
            int m0 = gm.TotalOwnedMile;
            yield return new WaitForSecondsRealtime(2.5f);
            Check(gm.TotalOwnedMile == m0 && !DailyLoginBonus.ClaimedToday, "P1: before buying the pass -> nothing");
            foreach (var bad in new[] { MockStoreService.Behaviour.Cancel, MockStoreService.Behaviour.Fail, MockStoreService.Behaviour.Offline, MockStoreService.Behaviour.PendingThenDecline })
            {
                MockStoreService.Mode = bad;
                NoAdsPass.Purchase(null);
                yield return new WaitForSecondsRealtime(bad == MockStoreService.Behaviour.PendingThenDecline ? 2.6f : 0.4f);
                if (bad == MockStoreService.Behaviour.PendingThenDecline) { NoAdsPass.Check("qa declined"); yield return new WaitForSecondsRealtime(0.4f); }
                Check(!NoAdsPass.Owned && gm.TotalOwnedMile == m0 && !NoAdsPass.PendingPayment, $"P2: {bad} -> no pass, no bonus, not stuck waiting (wallet {gm.TotalOwnedMile}, '{NoAdsPass.LastNote}')");
            }
            MockStoreService.Mode = MockStoreService.Behaviour.Success;
            if (SettingsPanel.Instance != null) SettingsPanel.Instance.Open();
            yield return new WaitForSecondsRealtime(0.8f);
            MonetizationUi.OpenPass();
            yield return new WaitForSecondsRealtime(0.6f);
            L($"[lb] pass screen open={MonetizationUi.Open} purchasing={NoAdsPass.Purchasing} adShowing={AdManager.Showing} busy={NoAdsPass.Busy} ui={(FindAnyObjectByType<MonetizationUi>() != null)}");
            Check(MonetizationUi.Open, "P3: the pass screen is open");
            Shot("lb_pass_screen_before");
            yield return new WaitForSecondsRealtime(0.3f); // 撮影はフレームの終わり: 撮る前に購入(画面を隠す)へ進まない
            NoAdsPass.Purchase(null);
            yield return new WaitForSecondsRealtime(0.6f);
            Check(NoAdsPass.Owned && gm.TotalOwnedMile == m0 + DailyLoginBonus.Amount, $"P3: purchase completed -> today's +5,000 right away (not doubled; wallet {m0} -> {gm.TotalOwnedMile})");
            Shot("lb_pass_screen_after");
            yield return new WaitForSecondsRealtime(0.3f);
            MonetizationUi.Close();
            int m1 = gm.TotalOwnedMile;
            for (int i = 0; i < 3; i++) { NoAdsPass.Check("restore button"); yield return new WaitForSecondsRealtime(0.4f); }
            NoAdsPass.DevMockSet(false); NoAdsPass.Check("restore button"); // 再インストール後の復元
            yield return new WaitForSecondsRealtime(0.4f);
            Check(NoAdsPass.Owned && gm.TotalOwnedMile == m1, $"P4: restore x4 (incl. after reinstall) the same day -> no second bonus (wallet {gm.TotalOwnedMile})");
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m1, "P5: home poll the same day -> nothing");
            TrustedClock.DevShiftHours += 24;
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m1 + DailyLoginBonus.Amount, $"P6: next day at home with the pass -> +5,000 (wallet {gm.TotalOwnedMile})");
            // 翌々日: 起動時の確認でパスを確かめた(start)だけでは即時に付けない → ホームの見回りで付く(どちらでも1回)
            TrustedClock.DevShiftHours += 24;
            int m2 = gm.TotalOwnedMile;
            NoAdsPass.Check("restore button");
            yield return new WaitForSecondsRealtime(0.5f);
            Check(gm.TotalOwnedMile == m2 + DailyLoginBonus.Amount, $"P7: restore on a new day -> +5,000 once (wallet {gm.TotalOwnedMile})");
            yield return new WaitForSecondsRealtime(1.6f);
            Check(gm.TotalOwnedMile == m2 + DailyLoginBonus.Amount, "P7: and the home poll does not add again");
            L("[lb] log: " + string.Join(" | ", DailyLoginBonus.Log));
            yield break;
        }
        if (c == "passlayout")
        {
            // 広告なしパスの画面: 全言語で、特典4つの本文が下のボタンに重ならず、ボタンと注記が板の中に入る
            MockStoreService.DevClearAccount(); NoAdsPass.DevMockSet(false); MockStoreService.Mode = MockStoreService.Behaviour.Success;
            string[] langs = Arg("-lbLangs", "").Length > 0 ? Arg("-lbLangs", "").Split(',') : System.Array.ConvertAll(Loc.Languages, l => l.code);
            string shots = "," + Arg("-lbShotLangs", "ja,en,ta,my,ar") + ",";
            MonetizationUi.OpenPass();
            foreach (var lang in langs)
            {
                Loc.Set(lang, false);
                yield return new WaitForSecondsRealtime(0.35f); // 1フレーム目で測った高さで板が決まる
                var pr = MonetizationUi.LastPanelRect;
                bool ok = MonetizationUi.LastContentBottom + 4f <= MonetizationUi.LastButtonsTop && MonetizationUi.LastButtonsBottom <= pr.yMax + 0.5f;
                Check(ok, $"PL {lang}: pass screen text ends {MonetizationUi.LastContentBottom:F0} above the buttons {MonetizationUi.LastButtonsTop:F0}, buttons+note end {MonetizationUi.LastButtonsBottom:F0} inside the panel {pr.yMax:F0} (panel h {pr.height:F0}, screen {Screen.width}x{Screen.height})");
                if (shots.Contains("," + lang + ",")) { Shot($"lb_pass_{lang}_{Screen.width}x{Screen.height}"); yield return new WaitForSecondsRealtime(0.3f); }
            }
            MonetizationUi.Close();
            Loc.Set("ja", false);
            yield break;
        }
        Check(false, "unknown -lbCase " + c);
    }

    IEnumerator WaitFor(System.Func<bool> cond, float timeout)
    {
        float w = 0f;
        while (!cond() && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
    }
}
#endif

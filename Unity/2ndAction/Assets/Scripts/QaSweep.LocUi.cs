#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 2026-10-23 の訳(RESULT の成功の見出し 10059 / ライセンスの画面 10056〜10058)の表示の確認:
//   -qaLocUi <dir> [-luLangs ja,en,de,...]   専用の保存ファイル(-fileSave)で動かすこと(成功の確定で記録を書く)
//  P 設定の「データとプライバシー」の欄 / データの削除の画面と確認(模擬: AccountDeletion.DebugMock。通信はしない)
//  A ライセンスの画面(開いた所 / 本文の終わりまで送った所)  L 読み込みの失敗の枠(10060/10061/10062。見た目だけ出す)
//  B 成功した時の結果画面(脱出成功！)
public partial class QaSweep
{
    IEnumerator LocUiMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3f);
        Check(FileSaveStore.Active || MemorySaveStore.Active, "runs on a dedicated save only");
        if (!(FileSaveStore.Active || MemorySaveStore.Active)) yield break;
        var inst = typeof(LicensesUi).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        var scrollF = typeof(LicensesUi).GetField("scroll", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var lang in Arg("-luLangs", "ja,en").Split(','))
        {
            Loc.Set(lang, false);
            TextFit.Issues.Clear();
            // ---- P ----
            var spScroll = typeof(SettingsPanel).GetField("scroll", BindingFlags.NonPublic | BindingFlags.Instance);
            SettingsPanel.OpenStatic();
            yield return new WaitForSecondsRealtime(0.6f);
            if (SettingsPanel.Instance != null) spScroll?.SetValue(SettingsPanel.Instance, new Vector2(0f, 99999f));
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"lu_{lang}_settings_privacy");
            yield return null; yield return null;
            AccountDeletion.DebugMock = "ok";
            DataDeletionUi.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            Shot($"lu_{lang}_delete");
            yield return null; yield return null;
            AccountDeletion.AskConfirm();
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"lu_{lang}_delete_confirm");
            yield return null; yield return null;
            AccountDeletion.CancelConfirm();
            DataDeletionUi.Close();
            AccountDeletion.DebugMock = null;
            yield return new WaitForSecondsRealtime(0.3f);
            // ---- A ----
            LicensesUi.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            Shot($"lu_{lang}_licenses_top");
            yield return null; yield return null;
            var sc = scrollF?.GetValue(inst?.GetValue(null)) as UiScroll;
            if (sc != null) sc.y = 999999f; // Begin で中身の高さへ詰める
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"lu_{lang}_licenses_end");
            yield return null; yield return null;
            Check(sc != null && sc.y > 0f, $"A {lang}: the license text scrolls (y={sc?.y:F0})");
            LicensesUi.Close();
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.3f);
            // ---- L 読み込みの失敗の枠(実際には読み込まない: 状態だけ立てて撮り、すぐ戻す) ----
            var ls = typeof(LoadingScreen).GetField("inst", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as LoadingScreen;
            if (ls != null)
            {
                var T = typeof(LoadingScreen);
                foreach (var title in new[] { "読み込みに時間がかかっています", "シーンを読み込めませんでした" })
                {
                    T.GetField("failText", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, title);
                    T.GetField("failed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, true);
                    T.GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, true);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Shot($"lu_{lang}_loadfail_{(title.StartsWith("読み込みに") ? "slow" : "error")}");
                    yield return null; yield return null;
                }
                T.GetField("failed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, false);
                T.GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, false);
                T.GetField("failText", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ls, "");
                foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (es != null) es.enabled = true;
                yield return new WaitForSecondsRealtime(0.4f);
            }
            // ---- B ----
            yield return BeginRun("swordsman", "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            GameManager.BlockExpGain = true;
            yield return new WaitForSeconds(2f);
            gm.Win();
            yield return new WaitForSecondsRealtime(4f);
            Shot($"lu_{lang}_result_win");
            yield return null; yield return null;
            Check(gm.IsWin, $"B {lang}: the run ended as a success");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"textfit_lu_{lang}.tsv"), TextFit.Report());
            L($"[locui {lang}] textfit issues {TextFit.Issues.Count} (overflow {Count("OVERFLOW")}) headline='{Loc.T(Loc.Reviewed(LocPendingReview.ResultEscaped, LocPendingReview.ResultEscapedOld))}'");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            GameManager.BlockExpGain = false;
            yield return EndRun();
            yield return new WaitForSecondsRealtime(1f);
        }
        Loc.Set("ja", false);
    }
}
#endif

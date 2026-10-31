#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 終了・中断の入口と画面設定(2026-10-31)の自動テスト: -qaExit <dir>(試験用の保存ファイル -fileSave と一緒に)[-exitStyle pc|android|ios]
//  X1 ホームの一番上の戻る(Esc 等)で確認が開く / 初めの選択はキャンセル / 押しっぱなしの決定で確定しない / 戻るでキャンセル
//  X2 ラン中: 一時停止 → 終了の確認の文は「チェックポイントから再開」(ソロ)/ iOS は「中断を保存」で保存の結果を出す
//  X3 画面設定(PC): 変えて確定しない → 15秒(実時間。一時停止中も)で元に戻る / Alt+Enter と同じ切り替えでウィンドウの大きさが戻る
//  X4 文字・UI の大きさ: 大きめで設定の画面と確認の窓が画面に収まる(撮影)
public partial class QaSweep
{
    IEnumerator ExitModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        L($"exit style {AppExit.Current}, exit button {AppExit.HasExitButton}, window mode {PlatformCaps.SupportsWindowMode}");

        // ---- X1 ホーム
        if (AppExit.HasExitButton)
        {
            GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
            gm.HandleBackButtonForTest();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(AppExit.Open, "X1: Back at the top of home opens the exit confirm");
            yield return Key((int)KeyCode.DownArrow, after: 0.3f); // キーで選び始める(初めの選択が出る)
            Rect fr = PadNav.FocusRect, cr = AppExit.CancelRect;
            L($"[X1] focus {fr} cancel {cr} confirm {AppExit.ConfirmRect} candidates {PadNav.DebugCandidates()}");
            Check(cr.width > 0f && cr.Contains(fr.center), "X1: the first selection is Cancel");
            yield return ShotW("exit_home_confirm");
            int c0 = AppExit.Confirmed;
            // 確定のボタンを選んで、Enter を押したまま開き直す(開いた入力では確定しない)
            AppExit.Cancel(); yield return new WaitForSecondsRealtime(0.4f);
            // ホームの「ゲーム終了」を選んで Enter を押し続ける(押した Enter で窓が開き、押したままでは確定しない)
            int o0 = AppExit.Opened;
            yield return FocusNear(gm.DebugHomeExitButtonRect.center);
            GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
            InputBindings.DebugPress((int)KeyCode.Return);
            yield return new WaitForSecondsRealtime(1.2f);
            InputBindings.DebugRelease((int)KeyCode.Return);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(AppExit.Opened == o0 + 1 && AppExit.Open && AppExit.Confirmed == c0, $"X1: Enter on the home exit button opens the confirm, and holding it does not confirm (opened {AppExit.Opened - o0})");
            Check(AppExit.DebugArmLeft == 0f, "X1: the confirm button becomes active after a short wait");
            gm.HandleBackButtonForTest(); // Esc / 戻る
            yield return new WaitForSecondsRealtime(0.4f);
            Check(!AppExit.Open && AppExit.Confirmed == c0, "X1: Back cancels the exit confirm (one step)");
            Check(!gm.HasStarted && !SettingsPanel.IsVisible, "X1: still at home after cancelling");
        }
        else
        {
            gm.HandleBackButtonForTest();
            yield return new WaitForSecondsRealtime(0.4f);
            Check(!AppExit.Open, "X1(iOS): no exit confirm at home (the app is left with the device's own home gesture)");
        }

        // ---- X4 文字・UI の大きさ(設定の画面と確認の窓)
        UiScalePref.Set(1);
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.8f);
        yield return ShotW("exit_settings_large");
        UiScalePref.Set(0);
        for (int i = 1; i <= 3; i++) { SettingsPanel.DebugScroll(i * 330f); yield return new WaitForSecondsRealtime(0.4f); yield return ShotW("exit_settings_scroll" + i); } // 画面/表示/操作の欄
        SettingsPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.5f);
        if (AppExit.HasExitButton)
        {
            AppExit.RequestFromHome();
            yield return new WaitForSecondsRealtime(0.5f);
            Rect all = new Rect(0, 0, Screen.width, Screen.height);
            Check(all.Contains(AppExit.ConfirmRect.min) && all.Contains(AppExit.CancelRect.max), $"X4: with the large UI size, the confirm buttons are on screen ({AppExit.ConfirmRect} {AppExit.CancelRect})");
            UiScalePref.Set(1);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return ShotW("exit_confirm_large");
            AppExit.Cancel();
            yield return new WaitForSecondsRealtime(0.4f);
        }
        UiScalePref.Set(0);

        // ---- X2 ラン中(ソロ)
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSecondsRealtime(3f);
        gm.HandleBackButtonForTest(); // 一時停止
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("exit_pause_menu");
        if (AppExit.Current == AppExit.Style.IosSuspend)
        {
            AppExit.RequestFromPause();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(AppExit.LastAction.StartsWith("ios suspend saved") && RunCheckpoint.HasActiveRun && !AppExit.Open, $"X2(iOS): 'save suspend' saves and says so, the app stays ({AppExit.LastAction})");
        }
        else
        {
            AppExit.RequestFromPause();
            yield return new WaitForSecondsRealtime(0.6f);
            string body = AppExit.LastBody;
            L($"[X2] body: {body.Replace('\n', ' ')}");
            Check(AppExit.Open && body.Contains("0"), "X2: the exit confirm during a solo run says it can be resumed from the checkpoint");
            yield return ShotW("exit_run_confirm");
            AppExit.Cancel();
            yield return new WaitForSecondsRealtime(0.4f);
            Check(!AppExit.Open && gm.HasStarted, "X2: cancelling keeps the run (paused)");
        }
        gm.HandleBackButtonForTest(); // 一時停止を閉じる
        yield return new WaitForSecondsRealtime(0.4f);

        // ---- X3 画面設定(PC)
        if (PlatformCaps.SupportsWindowMode)
        {
            var m0 = DisplaySettings.CurrentMode; var r0 = DisplaySettings.CurrentRes;
            gm.HandleBackButtonForTest(); // 一時停止(時間が止まっていても数える)
            yield return new WaitForSecondsRealtime(0.4f);
            int rv = DisplaySettings.Reverts;
            var target = new Vector2Int(r0.x == 1024 ? 1152 : 1024, r0.x == 1024 ? 648 : 576);
            DisplaySettings.Request(DisplaySettings.Mode.Windowed, target);
            yield return new WaitForSecondsRealtime(1.0f);
            Check(DisplaySettings.PendingConfirm && DisplaySettings.CurrentRes == target, $"X3: the new size is applied for trying ({DisplaySettings.CurrentRes.x}x{DisplaySettings.CurrentRes.y}), keep-or-revert is asked ({DisplaySettings.ConfirmLeft:F1}s left)");
            Check(DisplaySettings.ConfirmLeft > 12f, "X3: the keep-or-revert time is 15 seconds");
            DisplaySettings.DebugSetConfirmLeft(1.0f);
            yield return new WaitForSecondsRealtime(2.0f); // timeScale 0 の一時停止中
            Check(!DisplaySettings.PendingConfirm && DisplaySettings.Reverts == rv + 1 && DisplaySettings.CurrentRes == r0, $"X3: without confirming, it goes back to the previous size even while paused ({DisplaySettings.CurrentRes.x}x{DisplaySettings.CurrentRes.y})");
            gm.HandleBackButtonForTest(); // 一時停止を閉じる
            yield return new WaitForSecondsRealtime(0.4f);
            // Alt+Enter と同じ切り替え: ウィンドウ → 枠なし全画面 → ウィンドウ(前の大きさ)
            if (m0 == DisplaySettings.Mode.Windowed)
            {
                DisplaySettings.ToggleFullscreen("qa");
                yield return new WaitForSecondsRealtime(1.2f);
                var fm = DisplaySettings.CurrentMode;
                DisplaySettings.ToggleFullscreen("qa");
                yield return new WaitForSecondsRealtime(1.2f);
                var back = DisplaySettings.ClampToDisplay(r0, DisplaySettings.Mode.Windowed); // モニターに収まる大きさ(元がモニターの 95% を超える縦長の窓は縮む)
                Check(fm != DisplaySettings.Mode.Windowed && DisplaySettings.CurrentMode == DisplaySettings.Mode.Windowed && (DisplaySettings.CurrentRes - back).sqrMagnitude <= 8, $"X3: Alt+Enter toggles to full screen and back to the previous window size ({fm} -> {DisplaySettings.CurrentMode} {DisplaySettings.CurrentRes.x}x{DisplaySettings.CurrentRes.y})");
            }
        }
        yield return EndRun();
    }
}
#endif

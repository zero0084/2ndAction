#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// Windows のキーボード/マウスの操作(2026-10-31)の自動テスト: -qaKeyNav <dir>(テスト用データの中で行う)
//  入力は本物の割り当ての表を通す(InputBindings.DebugPress = キーを押していることにする)。タッチ/左クリックは使わない。
//  H1 決定の持ち越し: 決定を押したまま次の画面を開いても、次の画面で勝手に決定されない
//  H2 カードの3択: 矢印でカードを選び Enter で決定
//  H3 RESULT: 「ホームへ戻る」を選んで Enter でホームへ / 初めのフォーカスは「ホームへ戻る」
//  H4 マウスの戻るボタン: 一時停止を開く → 閉じる(1回で1段)、ホームの画面を1段だけ戻す
//  H5 文字の入力中は矢印/Space でゲームの操作・項目の移動をしない(デッキの名前の変更)
//  H6 Tab / Shift+Tab: 次/前の項目へ(端で反対の端へ)
public partial class QaSweep
{
    IEnumerator Key(int code, float hold = 0.06f, float after = 0.25f)
    {
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        InputBindings.DebugPress(code);
        yield return new WaitForSecondsRealtime(hold);
        InputBindings.DebugRelease(code);
        yield return new WaitForSecondsRealtime(after);
    }

    IEnumerator KeyNavMode()
    {
        Application.targetFrameRate = 60;
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        RunCheckpoint.Clear();
        int Enter = (int)KeyCode.Return, Down = (int)KeyCode.DownArrow, Right = (int)KeyCode.RightArrow, Left = (int)KeyCode.LeftArrow;

        // ---------------------------------------------------------------- H6 Tab
        L("== H6: Tab ==");
        yield return Key(Down);
        Check(PadNav.MenuActive && PadNav.HasFocus, "H6: arrow keys give a visible focus at home");
        Rect t0 = PadNav.FocusRect;
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        PadNav.DebugTab(false); yield return null; yield return null;
        Rect t1 = PadNav.FocusRect;
        PadNav.DebugTab(true); yield return null; yield return null;
        Check(t1 != t0 && PadNav.FocusRect == t0, $"H6: Tab moves to the next item and Shift+Tab comes back ({t0.center} -> {t1.center} -> {PadNav.FocusRect.center})");

        // ---------------------------------------------------------------- H4 マウスの戻るボタン(ホーム)
        L("== H4: mouse back ==");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.2f);
        Check((bool)GetPrivate(gm, "deckEditOpen"), "H4: deck edit opened");
        GameInput.DebugMouseBackFrame = Time.frameCount + 1;
        yield return new WaitForSecondsRealtime(1.2f);
        Check(!(bool)GetPrivate(gm, "deckEditOpen") && !gm.HasStarted, "H4: the mouse back button closes the deck edit (one step back to home)");

        // ---------------------------------------------------------------- H1 決定の持ち越し
        L("== H1: held Enter ==");
        var room0 = (Rect)GetPrivate(gm, "bgRoomRect");
        Rect door = new Rect(room0.x + room0.width * 0.40f, room0.y + room0.height * 0.14f, room0.width * 0.165f, room0.height * 0.51f);
        Rect sign = gm.LastHomeSignRects != null && gm.LastHomeSignRects.Length > 4 ? gm.LastHomeSignRects[4] : default;
        yield return FocusNear(sign.width > 0f ? sign.center : door.center);
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        yield return Key(Enter, hold: 1.6f, after: 1.0f); // 押したまま 1.6 秒(ステージ選択が開いた後も押している)
        bool stageOpen = (bool)GetPrivate(gm, "stageSelectOpen");
        Check(stageOpen && !gm.HasStarted, $"H1: holding Enter opens the stage select once and does not also depart (stage {stageOpen}, started {gm.HasStarted})");

        // ---------------------------------------------------------------- 出発(キー)
        var ssui = FindFirstObjectByType<StageSelectUI>();
        Rect dep = ssui != null && ssui.departButtonRect != null ? PadNav.ScreenRectOf(ssui.departButtonRect) : default;
        if (dep.width > 0f) yield return FocusNear(dep.center);
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        yield return Key(Enter, after: 0.5f);
        w = 0f; while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(gm.HasStarted, "run: Enter on 出発 starts the run");
        pc = PlayerController.Instance;
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);

        // ---------------------------------------------------------------- H4 マウスの戻るボタン(ラン中)
        GameInput.DebugMouseBackFrame = Time.frameCount + 1;
        yield return new WaitForSecondsRealtime(0.6f);
        Check((bool)GetPrivate(gm, "showPauseMenu"), "H4: during a run the mouse back button opens the pause menu (like Esc)");
        Check(gm.HasStarted, "H4: the mouse back button does not end the run");
        GameInput.DebugMouseBackFrame = Time.frameCount + 1;
        yield return new WaitForSecondsRealtime(0.6f);
        Check(!(bool)GetPrivate(gm, "showPauseMenu") && Time.timeScale > 0f, "H4: the mouse back button again closes the pause menu");

        // ---------------------------------------------------------------- H2 カードの3択
        L("== H2: card choice ==");
        autoPickHold = true;
        gm.GrantBonusCardChoice();
        w = 0f; while (!gm.IsRewardSequenceWaitingForSelection && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        int picks0 = gm.UpgradeCount;
        yield return Key(Right);
        yield return Key(Enter, after: 0.5f);
        yield return Key(Enter, after: 1.5f); // 1回目で選ぶ、2回目で決める(タップと同じ2段)
        w = 0f; while (gm.IsRewardSequenceRunning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(!gm.IsRewardSequenceRunning && gm.UpgradeCount > picks0, $"H2: the arrow keys and Enter pick a card ({picks0} -> {gm.UpgradeCount})");
        autoPickHold = false;

        // ---------------------------------------------------------------- H3 RESULT
        L("== H3: result ==");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 0 });
        typeof(GameManager).GetMethod("FinishRun", NP).Invoke(gm, null);
        w = 0f; while (!(gm.IsGameOver && gm.ResultShown) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        w = 0f; while (!gm.RetryAllowedNow && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.4f); // キーボードは使ったまま(H2)なので、矢印を押さずに最初のフォーカスを見る
        Rect hr = gm.LastResultHomeRect;
        L($"[H3] candidates {PadNav.DebugCandidates()} retry={gm.RetryAllowedNow}");
        bool onHome = (PadNav.FocusRect.center - hr.center).sqrMagnitude < 400f;
        Check(onHome, $"H3: on RESULT the first focus is ホームへ戻る ({PadNav.FocusRect} vs {hr})");
        if (!onHome) yield return FocusNear(hr.center);
        var old = gm;
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        yield return Key(Enter, after: 0.5f);
        w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        Check(gm != null && !gm.HasStarted, "H3: Enter on ホームへ戻る returns home");
        yield return new WaitForSecondsRealtime(1.5f);

        // ---------------------------------------------------------------- H5 文字の入力中
        L("== H5: text input ==");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.2f);
        var ui = gm.deckEditUI;
        var pu = ui != null ? ui.DebugPresetUi : null;
        if (pu != null)
        {
            pu.DebugRename(0); yield return new WaitForSecondsRealtime(0.6f);
            Check(GameInput.TextInputActive, "H5: the rename field has keyboard focus");
            Rect f0 = PadNav.FocusRect;
            InputBindings.DebugPress(Down); yield return null; yield return null;
            bool navHeld = GameInput.Held(GameAction.NavDown) || GameInput.Held(GameAction.AttackDown);
            InputBindings.DebugRelease(Down); yield return new WaitForSecondsRealtime(0.2f);
            Check(!navHeld && PadNav.FocusRect == f0, "H5: while typing, the arrow keys do not move the focus or act as game input");
            pu.DebugClose(); yield return new WaitForSecondsRealtime(0.4f);
            Check(!GameInput.TextInputActive, "H5: after leaving the field, keys work again");
        }
        else Check(false, "H5: deck preset UI not found");
        if (ui != null) ui.Close();
        yield return new WaitForSecondsRealtime(1.0f);

        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

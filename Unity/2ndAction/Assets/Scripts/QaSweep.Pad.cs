#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// ゲームパッド/キーボードだけで遊べるかの自動テスト(2026-10-06): -qaPad <dir> [-qaPadShots 1]
//  入力は GameInput.Inject(実際のパッドのボタンと同じ「行動」)で入れる。タッチ/マウスは一切使わない。
//  A ホーム: 候補が集まりフォーカスが出る / 十字キーで動く / 扉 → ステージ選択(uGUI)→ B で戻る
//  B ステージ選択 → 出発(A)→ ラン: A=ジャンプ / X=前攻撃 / START=停止メニュー → B で閉じる → START → RETURN TO HOME → 確認 → ホーム
//  C デッキ編集(ベッド): カードを A で入れる/外す、B で閉じる
//  D 設定: スライダーを左右で動かす(元へ戻す)、B で閉じる
//  E 闘技場: ホームの「闘技場」→ 準備画面 / LB・RB でタブ / START で閉じて戦闘 / START で開く / ホームへ
//  F ラン中は HUD のボタンにフォーカスが行かない(A はジャンプのまま)
public partial class QaSweep
{
    IEnumerator Press(GameAction a, float after = 0.25f)
    {
        GameInput.Inject(a);
        yield return null; yield return null;
        yield return new WaitForSecondsRealtime(after);
    }

    // GUI 座標(左上が原点)の点に一番近い候補へフォーカスを合わせる(画面の決まった場所のボタンを押すため)
    IEnumerator FocusNear(Vector2 guiPoint)
    {
        GameInput.DebugMarkDevice(InputDeviceKind.Gamepad);
        PadNav.DebugFocusNear(guiPoint);
        yield return null; yield return null;
    }

    IEnumerator PadMode()
    {
        Application.targetFrameRate = 60;
        var snap = SaveSystem.Capture();
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(2f);
        bool shots = Arg("-qaPadShots", "0") == "1";
        RunCheckpoint.Clear(); // 扉 → ステージ選択(CONTINUE ではなく)

        // ---------------------------------------------------------------- A ホーム
        L("== A: ホーム ==");
        yield return Press(GameAction.NavRight);
        L($"[A] candidates {PadNav.Candidates}, menu {PadNav.MenuActive}, focus {PadNav.FocusRect}, device {GameInput.LastDevice}");
        Check(PadNav.MenuActive && PadNav.HasFocus && PadNav.Candidates >= 6, "A: at home the pad has focusable buttons and a visible focus");
        Rect f0 = PadNav.FocusRect;
        yield return Press(GameAction.NavDown);
        Check(PadNav.FocusRect != f0, $"A: the d-pad moves the focus ({f0.center} -> {PadNav.FocusRect.center})");
        if (shots) { Shot("pad_home_focus"); yield return new WaitForSecondsRealtime(0.3f); }
        var room0 = (Rect)GetPrivate(gm, "bgRoomRect");
        Rect door = new Rect(room0.x + room0.width * 0.40f, room0.y + room0.height * 0.14f, room0.width * 0.165f, room0.height * 0.51f); // GameManager の doorRect と同じ
        yield return FocusNear(door.center);
        yield return Press(GameAction.Confirm, 1.2f);
        bool stageOpen = (bool)GetPrivate(gm, "stageSelectOpen");
        Check(stageOpen, "A: A on the door opens the stage select");
        if (shots) { Shot("pad_stage_select"); yield return new WaitForSecondsRealtime(0.3f); }
        L($"[A] stage select candidates {PadNav.Candidates}, focus {PadNav.FocusRect}, char select open {GetPrivate(gm, "characterSelectOpen")}");
        Check(PadNav.MenuActive && PadNav.Candidates >= 2, "A: the stage select (uGUI) buttons are focusable");
        yield return Press(GameAction.Cancel, 1.2f);
        Check(!(bool)GetPrivate(gm, "stageSelectOpen"), "A: B closes the stage select");

        // ---------------------------------------------------------------- B ラン
        L("== B: ラン ==");
        yield return FocusNear(door.center);
        yield return Press(GameAction.Confirm, 1.2f);
        var ss = FindFirstObjectByType<StageSelectUI>();
        if (ss != null && ss.departButtonRect != null)
        {
            yield return FocusNear(PadNav.ScreenRectOf(ss.departButtonRect).center);
            yield return Press(GameAction.Confirm, 0.5f);
        }
        w = 0f; while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        Check(gm.HasStarted, "B: A on DEPART starts a run");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(1f);
        Check(!PadNav.MenuActive, $"B/F: during the run the pad is not in menu mode (candidates {PadNav.Candidates}) - A stays jump");
        yield return WaitGrounded();
        float y0 = pc.transform.position.y, yMax = y0;
        GameInput.Inject(GameAction.Jump);
        w = 0f; while (w < 0.8f) { yMax = Mathf.Max(yMax, pc.transform.position.y); yield return null; w += Time.deltaTime; }
        Check(yMax > y0 + 0.8f, $"B: A = jump ({yMax - y0:F2}m)");
        yield return WaitGrounded();
        int pf0 = pc.PadFlicks;
        GameInput.Inject(GameAction.AttackForward);
        bool attacked = false; w = 0f;
        while (w < 0.6f) { attacked |= pc.IsAttacking; yield return null; w += Time.deltaTime; }
        L($"[B] forward: pad flicks {pf0} -> {pc.PadFlicks} last {pc.LastPadFlick} attacking seen {attacked}");
        Check(pc.PadFlicks > pf0 && pc.LastPadFlick == PlayerController.FlickDirection.Forward, "B: X = forward attack (the same input as a forward flick)");
        if (shots) { Shot("pad_run"); yield return new WaitForSecondsRealtime(0.3f); }
        yield return Press(GameAction.Pause, 0.4f);
        Check((bool)GetPrivate(gm, "showPauseMenu") && PadNav.MenuActive, "B: START opens the pause menu and the pad switches to menu mode");
        if (shots) { Shot("pad_pause"); yield return new WaitForSecondsRealtime(0.3f); }
        yield return Press(GameAction.Cancel, 0.4f);
        Check(!(bool)GetPrivate(gm, "showPauseMenu") && Time.timeScale > 0f, "B: B closes the pause menu and the run continues");
        yield return Press(GameAction.Pause, 0.4f);
        yield return Press(GameAction.NavDown, 0.2f);   // RESUME → RETURN TO HOME
        yield return Press(GameAction.Confirm, 0.4f);
        Check((bool)GetPrivate(gm, "showReturnHomeConfirm"), "B: RETURN TO HOME opens the confirm");
        var old = gm;
        yield return Press(GameAction.Confirm, 0.2f);    // 左 = RETURN TO HOME
        w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        Check(!gm.HasStarted, "B: the pad alone returns to home");

        // ---------------------------------------------------------------- C デッキ編集
        L("== C: デッキ編集 ==");
        var bgRoom = (Rect)GetPrivate(gm, "bgRoomRect");
        yield return FocusNear(new Vector2(bgRoom.x + bgRoom.width * 0.16f, bgRoom.y + bgRoom.height * 0.76f)); // ベッド
        yield return Press(GameAction.Confirm, 1.2f);
        bool deckOpen = (bool)GetPrivate(gm, "deckEditOpen");
        Check(deckOpen, "C: A on the bed opens the deck edit");
        var de = FindFirstObjectByType<DeckEditUI>();
        if (deckOpen && de != null)
        {
            if (shots) { Shot("pad_deck"); yield return new WaitForSecondsRealtime(0.3f); }
            // デッキの枠を A で外す → 所持の一覧のカードを A で入れる
            var slot = de.deckSlotCards.FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy);
            int deck0 = gm.DeckCards.Count;
            if (slot != null)
            {
                yield return FocusNear(PadNav.ScreenRectOf(slot.rect).center);
                yield return Press(GameAction.Confirm, 0.6f);
                int deck1 = gm.DeckCards.Count;
                var card = de.ownedCards.FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy);
                if (card != null) { yield return FocusNear(PadNav.ScreenRectOf(card.rect).center); yield return Press(GameAction.Confirm, 0.6f); }
                L($"[C] deck {deck0} -> remove {deck1} -> add {gm.DeckCards.Count}");
                Check(deck1 == deck0 - 1 && gm.DeckCards.Count == deck0, "C: A on a deck slot removes it and A on an owned card adds one");
            }
            else Check(false, "C: no deck slot card found");
            for (int i = 0; i < 6 && (bool)GetPrivate(gm, "deckEditOpen"); i++) yield return Press(GameAction.Cancel, 0.8f);
            Check(!(bool)GetPrivate(gm, "deckEditOpen"), "C: B closes the deck edit");
        }

        // ---------------------------------------------------------------- D 設定
        L("== D: 設定 ==");
        yield return FocusNear(GetPrivateRect("GetHomeSettingsButtonRect").center);
        yield return Press(GameAction.Confirm, 0.8f);
        Check(SettingsPanel.IsVisible, "D: A on 設定 opens the settings");
        var am = AudioManager.Instance;
        float vol0 = am != null ? am.MasterVolume : 0f;
        // 一番上の行(全体音量)のスライダーへ: 上から順に下へ
        yield return Press(GameAction.NavDown, 0.15f);
        int guard = 0;
        while (guard++ < 6 && am != null && Mathf.Approximately(am.MasterVolume, vol0))
        {
            yield return Press(GameAction.NavLeft, 0.15f);
            if (!Mathf.Approximately(am.MasterVolume, vol0)) break;
            yield return Press(GameAction.NavDown, 0.15f);
        }
        float vol1 = am != null ? am.MasterVolume : 0f;
        L($"[D] master volume {vol0:F2} -> {vol1:F2}");
        Check(am != null && !Mathf.Approximately(vol0, vol1), "D: left/right on a focused slider changes the value");
        if (shots) { Shot("pad_settings"); yield return new WaitForSecondsRealtime(0.3f); }
        if (am != null) am.SetMasterVolume(vol0, true);
        yield return Press(GameAction.Cancel, 0.8f);
        Check(!SettingsPanel.IsVisible, "D: B closes the settings");

        // ---------------------------------------------------------------- E 闘技場
        L("== E: 闘技場 ==");
        yield return FocusNear(GetPrivateRect("GetHomeArenaButtonRect").center);
        yield return Press(GameAction.Confirm, 0.5f);
        w = 0f; while ((ArenaController.Instance == null || ArenaLauncher.Instance.Launching) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        var arena = ArenaController.Instance;
        Check(arena != null && arena.PanelOpen && PadNav.MenuActive, "E: A on 闘技場 opens the arena setup with pad focus");
        if (arena != null)
        {
            int t0 = arena.Tab;
            yield return Press(GameAction.TabNext, 0.3f);
            Check(arena.Tab == (t0 + 1) % 5, $"E: RB switches the tab ({t0} -> {arena.Tab})");
            if (shots) { Shot("pad_arena_setup"); yield return new WaitForSecondsRealtime(0.3f); }
            yield return Press(GameAction.Pause, 0.6f);
            Check(!arena.PanelOpen && Time.timeScale > 0f && !PadNav.MenuActive, "E: START closes the setup and the battle runs (pad back to gameplay)");
            yield return Press(GameAction.Pause, 0.4f);
            Check(arena.PanelOpen, "E: START opens the setup again");
            arena.ExitHome();
            w = 0f; while ((ArenaMode.Active || GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1.5f);
            gm = GameManager.Instance;
        }

        // ---------------------------------------------------------------- G タッチに戻すと枠が消える
        PadNav.DebugTouchUsed();
        yield return null; yield return null;
        Check(!PadNav.ShowFocus, "G: after a touch/mouse input the focus frame is hidden (phone look unchanged)");

        SaveSystem.Restore(snap);
        SaveSystem.ReloadCaches(); CardMastery.ReloadFromPrefs();
        L("[pad] test machine save restored");
    }

    Rect GetPrivateRect(string method)
    {
        var m = typeof(GameManager).GetMethod(method, NP);
        return m != null ? (Rect)m.Invoke(gm, null) : default;
    }
}
#endif

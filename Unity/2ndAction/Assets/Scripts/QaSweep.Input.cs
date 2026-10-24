#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 操作・設定の整理(2026-10-25)の自動テスト: -qaInput <dir> [-qaInputPhase 1|2|android] [-qaInputDisplay 0]
//  入力は InputBindings.DebugPress(本物の割り当ての表 → KeyboardInputSource / MouseInputSource / パッド → GameInput → PlayerController)で入れる。
//  左クリックは DebugPointer(mouse = true)= 本物のマウスの左ボタンと同じ PlayerController のタップ/フリックの道。
//  保存は専用の試験ファイル(-fileSave)だけ。本物の保存では動かない。
//  phase 1: G〜K(キーボード)/ M・N(マウス)/ S・U(パッド・途中の切替)/ 二重発火 / O・Q・R(キー設定・初期化・重複)/ V〜X(画面設定)
//  phase 2: P(再起動後も割り当てと画面設定が残る)→ 最後に初期設定へ戻す
//  phase android(-caps android と一緒に): A〜F の代わり(今までの表のまま・設定の出し分け・タップ=前)
public partial class QaSweep
{
    readonly List<(PlayerController.FlickDirection d, PlayerController.FlickFate f)> qaFates = new List<(PlayerController.FlickDirection, PlayerController.FlickFate)>();
    static int Kc(KeyCode k) => (int)k;

    IEnumerator CodeTap(params int[] codes)
    {
        foreach (int c in codes) InputBindings.DebugPress(c);
        yield return null; yield return null;
        yield return new WaitForSecondsRealtime(0.06f);
        foreach (int c in codes) InputBindings.DebugRelease(c);
        yield return null; yield return null;
    }

    IEnumerator ReadyGround()
    {
        float g = 0f;
        while ((!pc.IsGrounded || pc.QaInputBusy || pc.IsReacting) && g < 3f) { yield return null; g += Time.deltaTime; }
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator ReadyAir()
    {
        yield return ReadyGround();
        pc.debugInjectOnce = PlayerController.FlickDirection.Up;
        float a = 0f;
        while (pc.IsGrounded && a < 0.6f) { yield return null; a += Time.deltaTime; }
        yield return new WaitForSeconds(0.12f);
    }

    bool Executed(PlayerController.FlickDirection d) =>
        qaFates.Exists(x => x.d == d && (x.f == PlayerController.FlickFate.Immediate || x.f == PlayerController.FlickFate.FiredLater || x.f == PlayerController.FlickFate.Reserved || x.f == PlayerController.FlickFate.LandBuffered));

    // 入力 → 期待するフリック(GameInput の道)と、技が出たか
    IEnumerator ExpectFlick(string id, int code, PlayerController.FlickDirection want, bool air, InputDeviceKind dev)
    {
        if (air) yield return ReadyAir(); else yield return ReadyGround();
        int n0 = pc.PadFlicks; qaFates.Clear();
        yield return CodeTap(code);
        yield return new WaitForSeconds(0.1f);
        bool got = pc.PadFlicks > n0 && pc.LastPadFlick == want;
        L($"[{id}] {InputBindings.CodeName(code)} -> {pc.LastPadFlick} (flicks +{pc.PadFlicks - n0}) executed={Executed(want)} device={GameInput.LastDevice}");
        Check(got, $"{id}: {InputBindings.CodeName(code)} gives {want}");
        Check(Executed(want), $"{id}: {InputBindings.CodeName(code)} actually starts the {want} move");
        Check(GameInput.LastDevice == dev, $"{id}: the last device is {dev} (got {GameInput.LastDevice})");
    }

    // 左クリック(タップ)。DebugPointer = PlayerController の指/マウスの道
    IEnumerator MouseClick()
    {
        var pos = new Vector2(Screen.width * 0.55f, Screen.height * 0.45f);
        PlayerController.DebugPointer.mouse = true;
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.pos = pos; PlayerController.DebugPointer.down = true;
        yield return null; yield return null;
        PlayerController.DebugPointer.down = false;
        yield return null; yield return null;
        PlayerController.DebugPointer.active = false; PlayerController.DebugPointer.mouse = false;
        yield return new WaitForSeconds(0.1f);
    }

    bool PauseOpen => gm != null && (bool)GetPrivate(gm, "showPauseMenu");

    IEnumerator InputMode()
    {
        Application.targetFrameRate = 60;
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        PlayerController.QaFlickFate = (id, d, f) => qaFates.Add((d, f));
        string phase = Arg("-qaInputPhase", "1");
        L($"caps: {PlatformCaps.Describe()}");
        L($"bindings: {InputBindings.Serialize()}");
        if (phase == "android") yield return InputAndroid();
        else if (phase == "real") yield return InputReal();
        else if (phase == "loc") yield return InputLocShots();
        else if (phase == "2") yield return InputPhase2();
        else yield return InputPhase1();
        PlayerController.QaFlickFate = null;
        InputBindings.DebugReleaseAll();
    }

    // ---------------------------------------------------------------- phase 1
    IEnumerator InputPhase1()
    {
        Check(PlatformCaps.SupportsKeyRebind && InputBindings.Rebindable, "PC: key settings are available and the PC table is used");
        InputBindings.ResetToDefaults();
        Check(InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.Space), "defaults: Jump = Space");
        Check(InputBindings.Mouse0Action() == GameAction.AttackForward, "defaults: left click = forward attack (same as the tap)");

        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        yield return new WaitForSeconds(1.2f);
        var K = InputDeviceKind.Keyboard; var M = InputDeviceKind.Mouse; var P = InputDeviceKind.Gamepad;
        var Up = PlayerController.FlickDirection.Up; var Fw = PlayerController.FlickDirection.Forward; var Bk = PlayerController.FlickDirection.Backward; var Dn = PlayerController.FlickDirection.Down;

        L("== G/H: keyboard ==");
        yield return ExpectFlick("G jump", Kc(KeyCode.Space), Up, false, K);
        yield return ExpectFlick("H up W", Kc(KeyCode.W), Up, false, K);
        yield return ExpectFlick("H up ↑", Kc(KeyCode.UpArrow), Up, false, K);
        yield return ExpectFlick("H fwd D", Kc(KeyCode.D), Fw, false, K);
        yield return ExpectFlick("H fwd →", Kc(KeyCode.RightArrow), Fw, false, K);
        yield return ExpectFlick("H back A", Kc(KeyCode.A), Bk, false, K);
        yield return ExpectFlick("H back ←", Kc(KeyCode.LeftArrow), Bk, false, K);
        yield return ExpectFlick("H down S (air)", Kc(KeyCode.S), Dn, true, K);
        yield return ExpectFlick("H down ↓ (air)", Kc(KeyCode.DownArrow), Dn, true, K);

        L("== I: pause ==");
        yield return ReadyGround();
        yield return CodeTap(Kc(KeyCode.P));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(PauseOpen && Time.timeScale == 0f, "I: P opens the pause menu (time stops)");
        Shot("I_pause_keyboard");
        yield return new WaitForSecondsRealtime(0.3f);
        yield return CodeTap(Kc(KeyCode.P));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(!PauseOpen, "I: P again closes the pause menu");
        gm.HandleBackButtonForTest();
        yield return new WaitForSecondsRealtime(0.2f);
        Check(PauseOpen, "I: Esc (fixed back) opens the pause menu");
        gm.HandleBackButtonForTest();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!PauseOpen, "I: Esc again resumes");

        L("== M/N: mouse ==");
        yield return ReadyGround();
        int taps0 = PlayerController.MouseTapActions; qaFates.Clear();
        yield return MouseClick();
        Check(PlayerController.MouseTapActions > taps0 && Executed(Fw), "M: left click (default) = forward attack");
        yield return ExpectFlick("N right click", Kc(KeyCode.Mouse1), Up, false, M);
        yield return ExpectFlick("N mouse 4", Kc(KeyCode.Mouse3), Bk, false, M);
        yield return ExpectFlick("N wheel up", InputBindings.WheelUp, Up, false, M);
        yield return ExpectFlick("N wheel down (air)", InputBindings.WheelDown, Dn, true, M);
        InputBindings.DebugPress(Kc(KeyCode.Mouse2)); yield return null; yield return null;
        Check(GameInput.Held(GameAction.Ultimate), "N: middle click holds ULTIMATE");
        InputBindings.DebugRelease(Kc(KeyCode.Mouse2)); yield return null;
        // 左クリックをジャンプへ
        InputBindings.Assign(GameAction.Jump, InputBindings.Column.Mouse, Kc(KeyCode.Mouse0), true);
        Check(InputBindings.Mouse0Action() == GameAction.Jump && InputBindings.Get(GameAction.AttackForward, InputBindings.Column.Mouse) == Kc(KeyCode.Mouse1), "M: left click moved to Jump (right click swapped to forward)");
        yield return ReadyGround(); qaFates.Clear();
        yield return MouseClick();
        Check(Executed(Up) && !Executed(Fw), "M: left click now jumps");
        yield return ExpectFlick("M right click now forward", Kc(KeyCode.Mouse1), Fw, false, M);
        InputBindings.ResetToDefaults();

        L("== S: gamepad ==");
        yield return ExpectFlick("S A", Kc(KeyCode.JoystickButton0), Up, false, P);
        yield return ExpectFlick("S X", Kc(KeyCode.JoystickButton2), Fw, false, P);
        yield return ExpectFlick("S RS left", InputBindings.RSLeft, Bk, false, P);
        yield return ExpectFlick("S B (air)", Kc(KeyCode.JoystickButton1), Dn, true, P);
        yield return ReadyGround();
        yield return CodeTap(Kc(KeyCode.JoystickButton7));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(PauseOpen, "S: START opens the pause menu");
        yield return CodeTap(Kc(KeyCode.JoystickButton7));
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!PauseOpen, "S: START closes it");

        L("== U: switching devices during the run ==");
        float d0 = pc.transform.position.x;
        yield return ExpectFlick("U keyboard", Kc(KeyCode.D), Fw, false, K);
        yield return ExpectFlick("U pad", Kc(KeyCode.JoystickButton2), Fw, false, P);
        yield return ExpectFlick("U mouse", Kc(KeyCode.Mouse3), Bk, false, M);
        yield return ExpectFlick("U keyboard again", Kc(KeyCode.Space), Up, false, K);
        Check(Time.timeScale > 0.5f && !PauseOpen && pc.transform.position.x > d0 + 1f, "U: switching devices never pauses the run (still moving)");

        L("== 17: same frame from several devices = one move ==");
        yield return ReadyGround(); qaFates.Clear();
        yield return CodeTap(Kc(KeyCode.D), Kc(KeyCode.JoystickButton2), InputBindings.RSRight);
        yield return new WaitForSeconds(0.1f);
        int fw = qaFates.FindAll(x => x.d == Fw && x.f == PlayerController.FlickFate.Immediate).Count;
        L($"[17] D + pad X + RS→ in one frame: forward moves {fw}");
        Check(fw == 1, "17: D + pad X + RS→ in the same frame start one forward move");
        yield return ReadyGround(); qaFates.Clear();
        InputBindings.DebugPress(Kc(KeyCode.D));
        yield return MouseClick();
        InputBindings.DebugRelease(Kc(KeyCode.D));
        yield return new WaitForSeconds(0.1f);
        int fw2 = qaFates.FindAll(x => x.d == Fw && x.f == PlayerController.FlickFate.Immediate).Count;
        L($"[17] D held + left click: forward moves {fw2}");
        Check(fw2 <= 2 && fw2 >= 1, "17: holding D while clicking does not repeat attacks (one press = one move)");
        Shot("U_run");
        yield return new WaitForSeconds(0.3f);
        yield return EndRun();

        L("== J/K: menus with the keyboard ==");
        yield return new WaitForSecondsRealtime(1.0f);
        gm = GameManager.Instance;
        yield return CodeTap(Kc(KeyCode.S));
        Check(PadNav.MenuActive && PadNav.HasFocus && GameInput.LastDevice == InputDeviceKind.Keyboard, "J: at home S (move down) shows the keyboard focus");
        Rect f0 = PadNav.FocusRect;
        foreach (var k in new[] { KeyCode.D, KeyCode.A, KeyCode.W })
        {
            yield return CodeTap(Kc(k));
            yield return new WaitForSecondsRealtime(0.1f);
            if (PadNav.FocusRect != f0) { L($"[J] {k} moved the focus {f0.center} -> {PadNav.FocusRect.center}"); break; }
        }
        Check(PadNav.FocusRect != f0, $"J: the WASD keys move the focus ({f0.center} -> {PadNav.FocusRect.center})");
        yield return FocusNear(GetPrivateRect("GetHomeSettingsButtonRect").center);
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        yield return CodeTap(Kc(KeyCode.Return));
        yield return new WaitForSecondsRealtime(0.5f);
        Check(SettingsPanel.IsOpen, "K: Enter (fixed confirm) on the gear opens the settings");
        Shot("K_settings_pc");
        yield return new WaitForSecondsRealtime(0.3f);
        yield return InputKeyPage();
        yield return CodeTap(Kc(KeyCode.Backspace));
        yield return new WaitForSecondsRealtime(0.4f);
        Check(!SettingsPanel.IsVisible, "K: Backspace (cancel) closes the settings");

        if (Arg("-qaInputDisplay", "1") == "1") yield return InputDisplay();

        // 再起動の確認用に、ジャンプ = J・左クリック = ジャンプを残す(phase 2 で確かめて初期設定へ戻す)
        InputBindings.Assign(GameAction.Jump, InputBindings.Column.Key0, Kc(KeyCode.J), true);
        InputBindings.Assign(GameAction.Jump, InputBindings.Column.Mouse, Kc(KeyCode.Mouse0), true);
        L($"[P] saved for the restart check: {InputBindings.Serialize()}");
    }

    // ---------------------------------------------------------------- O / Q / R: キー設定のページ
    IEnumerator InputKeyPage()
    {
        var sp = SettingsPanel.Instance;
        var t = typeof(SettingsPanel);
        t.GetMethod("OpenKeyView", NP).Invoke(sp, null);
        yield return new WaitForSecondsRealtime(0.4f);
        Check(SettingsPanel.KeyViewOpen, "O: the key settings page opens from the settings");
        Shot("O_keys_keyboard");
        yield return new WaitForSecondsRealtime(0.3f);
        SetPrivate(sp, "scroll", new Vector2(0f, 2000f)); // 下の端(メニューの操作と説明)
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("O_keys_keyboard_bottom");
        yield return new WaitForSecondsRealtime(0.3f);
        SetPrivate(sp, "scroll", Vector2.zero);
        SetPrivate(sp, "keyTab", 1);
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("O_keys_gamepad");
        yield return new WaitForSecondsRealtime(0.3f);
        SetPrivate(sp, "keyTab", 0);

        // O: ジャンプのキー1 を J へ(待ち受け → 押す)
        t.GetMethod("BeginCapture", NP).Invoke(sp, new object[] { GameAction.Jump, InputBindings.Column.Key0 });
        yield return new WaitForSecondsRealtime(0.4f);
        Check(SettingsPanel.CapturingInput && PadNav.Suspended, "O: pressing a cell waits for a new key (menu navigation suspended)");
        Shot("O_capture");
        yield return new WaitForSecondsRealtime(0.3f);
        InputBindings.DebugPoll(Kc(KeyCode.J));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(!SettingsPanel.CapturingInput && InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.J), "O: the next key (J) becomes Jump");
        Check(SaveStore.GetString(InputBindings.PrefKey, "").Contains("Jump=" + Kc(KeyCode.J) + ","), "O: the new key is saved (SettingsSave)");

        // R: 重複(ジャンプ ← D は前攻撃が使っている)→ 確認 → 入れ替える
        t.GetMethod("BeginCapture", NP).Invoke(sp, new object[] { GameAction.Jump, InputBindings.Column.Key0 });
        yield return new WaitForSecondsRealtime(0.4f);
        InputBindings.DebugPoll(Kc(KeyCode.D));
        yield return new WaitForSecondsRealtime(0.2f);
        bool conflictOpen = (bool)GetPrivate(sp, "conflictOpen");
        Check(conflictOpen, "R: a key already used in the same scene asks first (swap / cancel)");
        Check(InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.J), "R: nothing changes until the player chooses");
        Shot("R_conflict");
        yield return new WaitForSecondsRealtime(0.3f);
        SetPrivate(sp, "conflictOpen", false);
        InputBindings.Assign(GameAction.Jump, InputBindings.Column.Key0, Kc(KeyCode.D), true); // 「入れ替える」と同じ
        Check(InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.D) && InputBindings.Get(GameAction.AttackForward, InputBindings.Column.Key0) == Kc(KeyCode.J), "R: swap gives D to Jump and J to forward attack");
        Check(InputBindings.FindConflict(GameAction.Confirm, InputBindings.Column.Key0, Kc(KeyCode.Space)).any == false, "R: the menu can use Space although the run uses Space too (different scenes)");
        var fx = InputBindings.FindConflict(GameAction.Cancel, InputBindings.Column.Key0, Kc(KeyCode.UpArrow));
        Check(fx.any && fx.isFixed, "R: a fixed key (↑ = menu up) cannot be taken");
        Check(!InputBindings.Assign(GameAction.Cancel, InputBindings.Column.Key0, Kc(KeyCode.UpArrow), true), "R: assigning a fixed key is refused even with swap");
        Check(!InputBindings.Accepts(InputBindings.Column.Key0, Kc(KeyCode.Escape)), "R: Esc cannot be assigned (it always goes back)");
        var pc2 = InputBindings.FindConflict(GameAction.Jump, InputBindings.Column.Pad0, Kc(KeyCode.JoystickButton2));
        Check(pc2.any && pc2.action == GameAction.AttackForward, "R: the same check works for the gamepad (X is forward)");

        // Esc で待ち受けを取り消す(戻る)
        t.GetMethod("BeginCapture", NP).Invoke(sp, new object[] { GameAction.Pause, InputBindings.Column.Key0 });
        yield return new WaitForSecondsRealtime(0.3f);
        gm.HandleBackButtonForTest();
        yield return new WaitForSecondsRealtime(0.2f);
        Check(!SettingsPanel.CapturingInput && SettingsPanel.KeyViewOpen && InputBindings.Get(GameAction.Pause, InputBindings.Column.Key0) == Kc(KeyCode.P), "O: Esc cancels the waiting (no change, stays on the page)");

        // Q: 初期設定に戻す
        SetPrivate(sp, "resetAsk", true);
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("Q_reset_confirm");
        yield return new WaitForSecondsRealtime(0.3f);
        SetPrivate(sp, "resetAsk", false);
        InputBindings.ResetToDefaults();
        Check(InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.Space) && InputBindings.Get(GameAction.AttackForward, InputBindings.Column.Key0) == Kc(KeyCode.D) && !SaveStore.HasKey(InputBindings.PrefKey), "Q: reset restores the defaults");

        gm.HandleBackButtonForTest(); // ページ → 設定
        yield return new WaitForSecondsRealtime(0.3f);
        Check(SettingsPanel.IsVisible && !SettingsPanel.KeyViewOpen, "O: back from the key page returns to the settings");
    }

    // ---------------------------------------------------------------- V / W / X: 画面設定
    IEnumerator InputDisplay()
    {
        if (!PlatformCaps.SupportsWindowMode || DisplaySettings.Instance == null) { Warn("display settings not available here"); yield break; }
        var startMode = DisplaySettings.CurrentMode; var startRes = DisplaySettings.CurrentRes;
        L($"[display] start {startMode} {startRes.x}x{startRes.y}, desktop {Display.main.systemWidth}x{Display.main.systemHeight}, list {string.Join(" ", DisplaySettings.Resolutions(DisplaySettings.Mode.Windowed).ConvertAll(v => v.x + "x" + v.y))}");
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.5f);
        SetPrivate(SettingsPanel.Instance, "scroll", new Vector2(0f, 330f)); // 画面の設定が見える所まで
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("V_settings_screen_section");
        yield return new WaitForSecondsRealtime(0.3f);

        // W: ウィンドウ 1280x720 → 確定
        DisplaySettings.Request(DisplaySettings.Mode.Windowed, new Vector2Int(1280, 720));
        yield return new WaitForSecondsRealtime(1.2f);
        L($"[W] {Screen.fullScreenMode} {Screen.width}x{Screen.height}");
        Check(Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == 1280 && Screen.height == 720, "W: windowed 1280x720");
        Check(DisplaySettings.PendingConfirm, "W: asks to keep the new display");
        Shot("W_windowed_confirm");
        yield return new WaitForSecondsRealtime(0.3f);
        DisplaySettings.Keep();
        Check(SaveStore.GetInt(DisplaySettings.ModeKey, -1) == (int)DisplaySettings.Mode.Windowed && SaveStore.GetString(DisplaySettings.ResKey, "") == "1280x720", "W: keep saves the mode and resolution");

        // X: 解像度を変えて「元に戻す」/ 時間切れ
        DisplaySettings.Request(DisplaySettings.Mode.Windowed, new Vector2Int(1600, 900));
        yield return new WaitForSecondsRealtime(1.0f);
        L($"[X] {Screen.width}x{Screen.height}");
        Check(Screen.width == 1600 && Screen.height == 900, "X: resolution changes to 1600x900");
        DisplaySettings.Revert("qa");
        yield return new WaitForSecondsRealtime(1.0f);
        Check(Screen.width == 1280 && Screen.height == 720 && SaveStore.GetString(DisplaySettings.ResKey, "") == "1280x720", "X: revert goes back to 1280x720 (nothing saved)");
        DisplaySettings.Request(DisplaySettings.Mode.Windowed, new Vector2Int(1600, 900));
        yield return new WaitForSecondsRealtime(DisplaySettings.ConfirmSeconds + 1.5f);
        Check(!DisplaySettings.PendingConfirm && Screen.width == 1280, "X: without an answer it goes back by itself after 10 s");

        // V: フルスクリーン(全画面のウィンドウ = ボーダーレス)で確定
        var full = new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
        DisplaySettings.Request(DisplaySettings.Mode.Fullscreen, full);
        yield return new WaitForSecondsRealtime(1.5f);
        L($"[V] {Screen.fullScreenMode} {Screen.width}x{Screen.height}");
        Check(Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == full.x, "V: fullscreen (borderless, desktop size)");
        Shot("V_fullscreen");
        yield return new WaitForSecondsRealtime(0.3f);
        DisplaySettings.Keep();
        // 低い解像度のフルスクリーン(画面いっぱいに引き伸ばす)
        DisplaySettings.Request(DisplaySettings.Mode.Fullscreen, new Vector2Int(1280, 720));
        yield return new WaitForSecondsRealtime(1.5f);
        L($"[V] {Screen.fullScreenMode} {Screen.width}x{Screen.height}");
        Check(Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == 1280, "V: fullscreen at 1280x720");
        DisplaySettings.Revert("qa");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(Screen.width == full.x, "V: revert back to the desktop size");

        // 垂直同期 / FPS 上限
        DisplaySettings.SetVSync(false); DisplaySettings.SetFpsCap(60);
        yield return new WaitForSecondsRealtime(1.0f);
        Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 60, "FPS: vSync off + cap 60");
        DisplaySettings.SetVSync(true);
        Check(QualitySettings.vSyncCount == 1, "FPS: vSync on");
        Shot("V_settings_screen");
        yield return new WaitForSecondsRealtime(0.3f);
        SettingsPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // ---------------------------------------------------------------- 本物の OS の入力(realinput.ps1 が送る)
    //  ラン: Space / D / A / W / 右クリック / 左クリック / 右クリック / P / P → ホーム: 設定のボタンを左クリック → Esc
    IEnumerator InputReal()
    {
        InputBindings.ResetToDefaults();
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        var hsa = HighSpeedAssist.Instance; if (hsa != null) hsa.SetEnabled(false);
        yield return new WaitForSeconds(1f);
        qaFates.Clear();
        var devs = new List<string>(); var last = GameInput.LastDevice; devs.Add(last.ToString());
        bool pauseSeen = false; int taps0 = PlayerController.MouseTapActions;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "real_ready.txt"), "1");
        float t = 0f; string sent = System.IO.Path.Combine(outDir, "real_sent.txt");
        while (t < 60f && !System.IO.File.Exists(sent))
        {
            if (GameInput.LastDevice != last) { last = GameInput.LastDevice; devs.Add(last.ToString()); }
            if (PauseOpen) pauseSeen = true;
            yield return null; t += Time.unscaledDeltaTime;
        }
        yield return new WaitForSecondsRealtime(1f);
        var sb = new System.Text.StringBuilder();
        foreach (var x in qaFates) sb.Append(x.d).Append(':').Append(x.f).Append(' ');
        L($"[real] devices {string.Join(" > ", devs)} | moves {sb} | mouse taps +{PlayerController.MouseTapActions - taps0}");
        Check(System.IO.File.Exists(sent), "real: the OS input script ran");
        int ups = qaFates.FindAll(x => x.d == PlayerController.FlickDirection.Up && x.f != PlayerController.FlickFate.Cleared).Count;
        Check(ups >= 3, $"real G/M: Space, W and right clicks start up moves ({ups})");
        Check(Executed(PlayerController.FlickDirection.Forward) && PlayerController.MouseTapActions > taps0, "real H/M: D and the left click attack forward");
        Check(Executed(PlayerController.FlickDirection.Backward), "real H: A attacks backward");
        Check(devs.Contains("Keyboard") && devs.Contains("Mouse"), "real U: the device switches between keyboard and mouse by itself");
        Check(pauseSeen && !PauseOpen, "real I: P pauses and P resumes");
        if (hsa != null) hsa.SetEnabled(true);
        yield return EndRun();
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        var r = GetPrivateRect("GetHomeSettingsButtonRect");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "home_ready.txt"), $"{Mathf.RoundToInt(r.center.x)} {Mathf.RoundToInt(r.center.y)}");
        t = 0f; while (t < 30f && !SettingsPanel.IsOpen) { yield return null; t += Time.unscaledDeltaTime; }
        Check(SettingsPanel.IsOpen, "real L: a real left click on the settings button opens the settings");
        Shot("real_settings_by_click");
        yield return new WaitForSecondsRealtime(0.3f);
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "settings_open.txt"), "1");
        t = 0f; while (t < 15f && SettingsPanel.IsVisible) { yield return null; t += Time.unscaledDeltaTime; }
        Check(!SettingsPanel.IsVisible, "real K: the real Esc key closes the settings");
    }

    // ---------------------------------------------------------------- 30言語の見た目(-qaInputPhase loc [-qaInputLangs ja,en,...])
    //  キー設定のページ / 重複の確認 / 画面の確認 / 設定の「画面」と「操作」を各言語で撮る。訳が抜けて日本語が出た文を数える
    IEnumerator InputLocShots()
    {
        string[] langs = Arg("-qaInputLangs", string.Join(",", System.Array.ConvertAll(Loc.Languages, l => l.code))).Split(',');
        var sp = SettingsPanel.Instance; var t = typeof(SettingsPanel);
        foreach (var lang in langs)
        {
            Loc.Set(lang, false);
            LocDebug.Reset();
            SettingsPanel.OpenStatic();
            yield return new WaitForSecondsRealtime(0.5f);
            SetPrivate(sp, "scroll", new Vector2(0f, 330f));
            yield return new WaitForSecondsRealtime(0.3f);
            Shot(lang + "_settings_screen");
            yield return new WaitForSecondsRealtime(0.3f);
            t.GetMethod("OpenKeyView", NP).Invoke(sp, null);
            yield return new WaitForSecondsRealtime(0.4f);
            Shot(lang + "_keys");
            yield return new WaitForSecondsRealtime(0.3f);
            SetPrivate(sp, "scroll", new Vector2(0f, 2000f));
            yield return new WaitForSecondsRealtime(0.3f);
            Shot(lang + "_keys_bottom");
            yield return new WaitForSecondsRealtime(0.3f);
            SetPrivate(sp, "conflict", InputBindings.FindConflict(GameAction.Jump, InputBindings.Column.Key0, Kc(KeyCode.D)));
            SetPrivate(sp, "conflictCode", Kc(KeyCode.D)); SetPrivate(sp, "capAction", GameAction.Jump); SetPrivate(sp, "conflictOpen", true);
            yield return new WaitForSecondsRealtime(0.3f);
            Shot(lang + "_conflict");
            yield return new WaitForSecondsRealtime(0.3f);
            SetPrivate(sp, "conflictOpen", false);
            t.GetMethod("BeginCapture", NP).Invoke(sp, new object[] { GameAction.Ultimate, InputBindings.Column.Mouse });
            yield return new WaitForSecondsRealtime(0.4f);
            Shot(lang + "_capture_mouse");
            yield return new WaitForSecondsRealtime(0.3f);
            t.GetMethod("EndCapture", NP).Invoke(sp, new object[] { "qa" });
            SetPrivate(sp, "keyView", false);
            if (DisplaySettings.Instance != null)
            {
                DisplaySettings.Request(DisplaySettings.CurrentMode, DisplaySettings.CurrentRes);
                yield return new WaitForSecondsRealtime(0.4f);
                Shot(lang + "_display_confirm");
                yield return new WaitForSecondsRealtime(0.3f);
                DisplaySettings.Revert("qa");
            }
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.4f);
            var jp = new List<string>(LocDebug.UntranslatedShown); jp.AddRange(LocDebug.JapaneseShown);
            L($"[loc] {lang}: japanese shown {jp.Count} {(jp.Count > 0 ? string.Join(" / ", jp.GetRange(0, Mathf.Min(6, jp.Count))) : "")}");
            // 「・」(中点)は韓国語/ベトナム語などでも使うので数えない。出た文に仮名が残った物だけ
            var kana = new System.Text.RegularExpressions.Regex("[぀-ヺー-ヿ]");
            int real = jp.FindAll(x => { int k = x.IndexOf("=>"); string shown = k >= 0 ? x.Substring(k + 2) : x; return kana.IsMatch(shown) || (k < 0 && lang != "zh-Hans" && lang != "zh-Hant"); }).Count;
            if (lang != "ja") Check(real == 0, $"loc {lang}: no untranslated Japanese on the new screens ({real})");
        }
        Loc.Set("ja", false);
    }

    // ---------------------------------------------------------------- phase 2(再起動の後)
    IEnumerator InputPhase2()
    {
        L($"[P] after restart: {InputBindings.Serialize()}");
        Check(InputBindings.Get(GameAction.Jump, InputBindings.Column.Key0) == Kc(KeyCode.J) && InputBindings.Mouse0Action() == GameAction.Jump, "P: key settings survive a restart (Jump = J, left click = Jump)");
        if (Arg("-qaInputDisplay", "1") == "1")
        {
            L($"[P] display after restart: {Screen.fullScreenMode} {Screen.width}x{Screen.height} vSync {QualitySettings.vSyncCount}");
            Check(SaveStore.GetInt(DisplaySettings.ModeKey, -1) == (int)DisplaySettings.Mode.Fullscreen && Screen.fullScreenMode == FullScreenMode.FullScreenWindow, "P: the kept display mode (fullscreen) is applied at start");
        }
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(1.2f);
        yield return ExpectFlick("P J jumps", Kc(KeyCode.J), PlayerController.FlickDirection.Up, false, InputDeviceKind.Keyboard);
        yield return ReadyGround(); qaFates.Clear();
        yield return CodeTap(Kc(KeyCode.Space));
        yield return new WaitForSeconds(0.1f);
        Check(!Executed(PlayerController.FlickDirection.Up), "P: Space no longer jumps");
        yield return ReadyGround(); qaFates.Clear();
        yield return MouseClick();
        Check(Executed(PlayerController.FlickDirection.Up), "P: left click jumps after the restart");
        yield return EndRun();
        InputBindings.ResetToDefaults();
        foreach (var k in new[] { DisplaySettings.ModeKey, DisplaySettings.ResKey, DisplaySettings.VSyncKey, DisplaySettings.FpsKey }) SaveStore.DeleteKey(k);
        SaveStore.Save();
        L("[P] reset to defaults for the next test");
    }

    // ---------------------------------------------------------------- Android の見せ方(-caps android)
    IEnumerator InputAndroid()
    {
        Check(!PlatformCaps.SupportsKeyRebind && !InputBindings.Rebindable && PlatformCaps.SupportsOrientation && !PlatformCaps.SupportsWindowMode, "A: Android profile: no key settings / no window settings / orientation available");
        Check(InputBindings.Keys(GameAction.Jump).Length == 3 && InputBindings.Keys(GameAction.AttackBack).Length == 4, "A: the old fixed table is kept (Bluetooth keyboards unchanged)");
        Check(InputBindings.Mouse0Action() == GameAction.AttackForward, "A: tap = forward attack (unchanged)");
        Check(DisplaySettings.Instance == null, "A: no display settings object");
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.8f);
        Shot("A_settings_android_profile");
        yield return new WaitForSecondsRealtime(0.3f);
        SettingsPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.4f);
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(1.2f);
        yield return ReadyGround(); qaFates.Clear();
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f); PlayerController.DebugPointer.down = true;
        yield return null; yield return null;
        PlayerController.DebugPointer.down = false; yield return null; yield return null; PlayerController.DebugPointer.active = false;
        yield return new WaitForSeconds(0.1f);
        Check(Executed(PlayerController.FlickDirection.Forward), "A/B: a touch tap attacks forward");
        yield return ExpectFlick("A keyboard Z (old table)", Kc(KeyCode.Z), PlayerController.FlickDirection.Forward, false, InputDeviceKind.Keyboard);
        yield return ExpectFlick("A keyboard B (old table)", Kc(KeyCode.B), PlayerController.FlickDirection.Backward, false, InputDeviceKind.Keyboard);
        yield return EndRun();
    }

    // ---------------------------------------------------------------- FPS: 同じ実時間で進む距離(-qaFps <dir> -qaFpsCap 60|144|0)
    IEnumerator FpsMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        int cap = int.Parse(Arg("-qaFpsCap", "60"));
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = cap > 0 ? cap : -1;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        var hsa = HighSpeedAssist.Instance; if (hsa != null) hsa.SetEnabled(false);
        float t0 = Time.time; int f0 = Time.frameCount; float rt0 = Time.realtimeSinceStartup;
        float d0 = gm.MaxDistance, x0 = pc.transform.position.x, react = 0f, minDt = 1f, maxDt = 0f;
        // 同じ実時間で進む距離。被弾の反応/障害物で止まった時間も数える(FPS と関係のない差を見分ける)
        while (Time.time - t0 < 10f)
        {
            yield return null;
            if (pc.IsReacting) react += Time.deltaTime;
            minDt = Mathf.Min(minDt, Time.unscaledDeltaTime); maxDt = Mathf.Max(maxDt, Time.unscaledDeltaTime);
        }
        float dist = gm.MaxDistance - d0;
        L($"[fps] cap {(cap > 0 ? cap.ToString() : "unlimited")}: x moved {pc.transform.position.x - x0:F2} world units, reacting {react:F2}s, frame time {minDt * 1000f:F1}..{maxDt * 1000f:F1} ms");
        L($"[fps] cap {(cap > 0 ? cap.ToString() : "unlimited")}: game time {Time.time - t0:F2}s real {Time.realtimeSinceStartup - rt0:F2}s frames {Time.frameCount - f0} ({(Time.frameCount - f0) / (Time.realtimeSinceStartup - rt0):F0} fps) distance {dist:F1}m ({dist / (Time.time - t0):F2} m per game second)");
        // ジャンプの高さ(重力の補正 GravityRefStep が効いているか)
        yield return ReadyGround();
        float y0 = pc.transform.position.y, yMax = y0;
        pc.debugInjectOnce = PlayerController.FlickDirection.Up;
        float a = 0f; while (a < 1.2f) { yield return null; a += Time.deltaTime; yMax = Mathf.Max(yMax, pc.transform.position.y); }
        L($"[fps] cap {(cap > 0 ? cap.ToString() : "unlimited")}: jump height {yMax - y0:F3}");
        if (hsa != null) hsa.SetEnabled(true);
        yield return EndRun();
    }
}
#endif

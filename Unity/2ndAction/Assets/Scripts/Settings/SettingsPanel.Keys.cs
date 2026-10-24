using UnityEngine;

// 設定 → 操作 → キー設定 / 設定 → 画面(PC)(2026-10-25、Android/Windows の操作・設定の整理)。
//  同じ設定パネルの中で中身だけ「キー設定」のページに切り替える(見た目は設定と同じ UiTheme / UiKit)。
//  ・タブ: キーボード・マウス(キー1 / キー2 / マウス)と ゲームパッド(ボタン1 / ボタン2)
//  ・欄を押す → 「新しいキーを入力してください」→ 次に押した物を割り当てる(押した指/ボタンが離れてから受け付ける。Esc で取り消し、10秒で取り消し)
//  ・同じ場面(ラン中/メニュー)でもう使っているキー → 「このキーはすでに「○○」に使われています」→ 入れ替える / キャンセル
//  ・Esc / Enter / 矢印キー / 左スティック・十字キー はいつでも使える固定の入力(変えられない。戻れなくならない)
//  ・初期設定に戻す(確認あり)
public partial class SettingsPanel
{
    bool keyView;                 // キー設定のページ
    int keyTab;                   // 0 = キーボード・マウス / 1 = ゲームパッド
    bool capturing, capArmed;
    float capT;
    GameAction capAction;
    InputBindings.Column capColumn;
    bool conflictOpen; InputBindings.Conflict conflict; int conflictCode;
    bool resetAsk;
    Rect capButtonsGui;           // 待ち受けの窓のボタン(マウスの待ち受けで、このボタンへのクリックを割り当てにしない)
    const float CaptureTimeout = 10f;

    public static bool KeyViewOpen => Instance != null && Instance.state != St.Closed && Instance.keyView;
    public static bool CapturingInput => Instance != null && Instance.state != St.Closed && Instance.capturing;
    bool PanelModalOpen => capturing || conflictOpen || resetAsk || DisplaySettings.PendingConfirm;

    // 戻る(Esc / Android の戻る / B): 一番手前の物だけを閉じる。閉じた物があれば true
    public static bool HandleBack()
    {
        var p = Instance;
        if (p == null || p.state == St.Closed) return false;
        if (p.capturing) { p.EndCapture("back"); return true; }
        if (p.conflictOpen) { p.conflictOpen = false; return true; }
        if (p.resetAsk) { p.resetAsk = false; return true; }
        if (DisplaySettings.PendingConfirm) { DisplaySettings.Revert("back"); return true; }
        if (p.keyView) { p.keyView = false; p.scroll = Vector2.zero; p.drawnH = 0f; return true; }
        return false;
    }

    void OpenKeyView()
    {
        keyView = true; keyTab = GameInput.LastDevice == InputDeviceKind.Gamepad ? 1 : 0; scroll = Vector2.zero; drawnH = 0f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiOpen);
    }

    void BeginCapture(GameAction a, InputBindings.Column col)
    {
        capturing = true; capArmed = false; capT = 0f; capAction = a; capColumn = col;
        PadNav.Suspended = true;
        Debug.Log($"[KeySettings] waiting for {a} {col}");
    }

    void EndCapture(string why)
    {
        if (!capturing) return;
        capturing = false; PadNav.Suspended = false;
        UiInputGate.LatchUntilRelease();
        Debug.Log($"[KeySettings] capture ended ({why})");
    }

    // 待ち受け(Update から)
    void UpdateCapture()
    {
        if (!capturing) return;
        capT += Time.unscaledDeltaTime;
        if (capT > CaptureTimeout) { EndCapture("timeout"); return; }
        bool padCol = InputBindings.IsPadColumn(capColumn), mouseCol = capColumn == InputBindings.Column.Mouse;
        if (!capArmed)
        {
            if (capT > 0.15f && !InputBindings.AnyHeld()) capArmed = true;
            InputBindings.PollPress(!padCol && !mouseCol, mouseCol, padCol); // 軸の前の状態を覚えるだけ
            return;
        }
        int c = InputBindings.PollPress(!padCol && !mouseCol, mouseCol, padCol);
        if (c == -1) { if (GameManager.Instance == null) EndCapture("esc"); return; } // Esc は GameManager の戻る(HandleBack)が取り消す
        if (c <= 0 || !InputBindings.Accepts(capColumn, c)) return;
        if (mouseCol && c == (int)KeyCode.Mouse0)
        {
            float s = S;
            var mp = new Vector2(Input.mousePosition.x / s, (Screen.height - Input.mousePosition.y) / s);
            if (capButtonsGui.Contains(mp)) return; // 窓のボタンを押した(GUI のボタンが受け取る)
        }
        OnCaptured(c);
    }

    void OnCaptured(int code)
    {
        var a = capAction; var col = capColumn;
        EndCapture("got " + InputBindings.CodeName(code));
        var cf = InputBindings.FindConflict(a, col, code);
        if (cf.any) { conflictOpen = true; conflict = cf; conflictCode = code; capAction = a; capColumn = col; return; }
        InputBindings.Assign(a, col, code, false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
    }

    // ---------------------------------------------------------------- キー設定のページ
    float DrawKeyPage(Rect view, bool interactive)
    {
        bool live = interactive && !PanelModalOpen;
        float w = view.width;
        float contentH = Mathf.Max(drawnH, 1200f);
        BeginScroll(view, contentH);
        float y = 0f;
        int t = UiKit.Choice(new Rect(0f, y, w * 0.66f, RowH - 10f), keyTab, new[] { "キーボード・マウス", "ゲームパッド" }, 18f);
        if (live && t != keyTab) { keyTab = t; if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle); }
        if (UiKit.Button(new Rect(w * 0.66f + 12f, y, w * 0.34f - 12f, RowH - 10f), "初期設定に戻す", 17f, false, false, live)) resetAsk = true;
        y += RowH;

        bool pad = keyTab == 1;
        var cols = pad ? new[] { InputBindings.Column.Pad0, InputBindings.Column.Pad1 } : new[] { InputBindings.Column.Key0, InputBindings.Column.Key1, InputBindings.Column.Mouse };
        string[] colNames = pad ? new[] { "ボタン1", "ボタン2" } : new[] { "キー1", "キー2", "マウス" };
        float nameW = Mathf.Min(LabelW + 40f, w * 0.34f), gap = 8f;
        float cw = (w - nameW - gap * (cols.Length - 1)) / cols.Length;
        if (pad && !PadConnected()) y = Note(0f, y, w, "ゲームパッドがつながっていません") + 6f;

        foreach (var group in new[] { BindGroup.Run, BindGroup.Menu })
        {
            y = Head(0f, y, w, group == BindGroup.Run ? "ラン中の操作" : "メニューの操作");
            for (int i = 0; i < cols.Length; i++)
                LocGUI.Label(new Rect(nameW + i * (cw + gap), y, cw, 26f), colNames[i], UiKit.Label(15f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
            y += 28f;
            foreach (var r in InputBindings.Rows)
            {
                if (r.group != group) continue;
                RowLabelW(0f, y, nameW, r.name);
                for (int i = 0; i < cols.Length; i++)
                {
                    var cell = new Rect(nameW + i * (cw + gap), y + 6f, cw, RowH - 12f);
                    if (cols[i] == InputBindings.Column.Mouse && !r.mouse)
                    {
                        LocGUI.Label(cell, "—", UiKit.Label(18f, TextAnchor.MiddleCenter, false, UiTheme.Disabled));
                        continue;
                    }
                    int code = InputBindings.Get(r.action, cols[i]);
                    bool waiting = capturing && capAction == r.action && capColumn == cols[i];
                    string label = waiting ? "…" : InputBindings.CodeName(code);
                    float fs = 18f;
                    var probe = UiKit.Label(fs, TextAnchor.MiddleCenter, true);
                    while (fs > 11f && probe.CalcSize(new GUIContent(label)).x > cw - 10f) { fs -= 1f; probe = UiKit.Label(fs, TextAnchor.MiddleCenter, true); }
                    if (UiKit.Button(cell, label, fs, waiting, false, live)) BeginCapture(r.action, cols[i]);
                }
                y += RowH;
            }
            y += 10f;
        }
        y = Note(0f, y, w, pad ? "左スティックと十字キー(メニューの移動)はいつでも使えます。" : "Esc(戻る・一時停止)、Enter(決定)、矢印キー(メニューの移動)はいつでも使えます。");
        if (!pad) y = Note(0f, y, w, "左ボタンのドラッグはフリックです(上下左右の攻撃・ジャンプ)。左クリックは、上の表で左クリックに割り当てた操作になります。");
        y = Note(0f, y, w, "ジャンプと上攻撃は同じ動きです(地上では上へ攻撃しながら跳びます)。");
        y = Note(0f, y, w, "ラン中の操作とメニューの操作は、それぞれ同じキーを重複して割り当てられません。ただし、ラン中とメニューの間では同じキーを使えます。");
        EndScroll(view, contentH);
        if (Event.current.type == EventType.Repaint) drawnH = y + 12f;
        return y;
    }

    void RowLabelW(float x, float y, float w, string text)
    {
        var st = new GUIStyle(UiKit.Label(19f)); st.wordWrap = true;
        string shown = Loc.Auto(text);
        if (st.CalcSize(new GUIContent(shown)).x > w - 8f) { st = new GUIStyle(UiKit.Label(15f)); st.wordWrap = true; }
        LocGUI.Label(new Rect(x, y, w - 8f, RowH), text, st);
    }

    static bool PadConnected()
    {
        foreach (var n in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(n)) return true;
        return false;
    }

    // ---------------------------------------------------------------- パネルの中の確認の窓(待ち受け/重複/初期化/画面の確認)
    void DrawPanelModal(Rect panel)
    {
        if (!PanelModalOpen) return;
        int padLayer = PadNav.BeginLayer(11);
        UiKit.Fill(panel, new Color(0.02f, 0.03f, 0.08f, 0.55f));
        float bw = Mathf.Min(560f, panel.width - 40f), bh = 250f;
        var box = new Rect(panel.center.x - bw * 0.5f, panel.center.y - bh * 0.5f, bw, bh);
        OrnateUi.DrawPanel(box, 0.97f);
        var title = new GUIStyle(UiKit.Label(22f, TextAnchor.MiddleCenter, true, UiTheme.GoldText)) { wordWrap = true };
        var body = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleCenter, false, UiTheme.Text)) { wordWrap = true };
        var b1 = new Rect(box.x + 24f, box.yMax - 70f, (bw - 60f) * 0.5f, 50f);
        var b2 = new Rect(b1.xMax + 12f, b1.y, b1.width, 50f);
        if (capturing)
        {
            bool padCol = InputBindings.IsPadColumn(capColumn), mouseCol = capColumn == InputBindings.Column.Mouse;
            LocGUI.Label(new Rect(box.x + 20f, box.y + 18f, bw - 40f, 40f), InputBindings.RowOf(capAction).name, title);
            LocGUI.Label(new Rect(box.x + 20f, box.y + 62f, bw - 40f, 50f), padCol ? "新しいボタンを入力してください" : mouseCol ? "マウスのボタンを押すか、ホイールを回してください" : "新しいキーを入力してください", body);
            GUI.Label(new Rect(box.x + 20f, box.y + 112f, bw - 40f, 40f), Loc.F("Esc でキャンセル({0})", Mathf.CeilToInt(CaptureTimeout - capT).ToString()), UiKit.Label(15f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
            capButtonsGui = new Rect(b1.x, b1.y, b2.xMax - b1.x, b1.height);
            // 待ち受け中はパッドのボタンを割り当てに使うので、窓のボタンはマウス/タッチだけ(PadNav に出さない)
            if (GUI.Button(b1, GUIContent.none, GUIStyle.none)) { InputBindings.Clear(capAction, capColumn); EndCapture("cleared"); }
            DrawPlainButton(b1, "割り当てなし");
            if (GUI.Button(b2, GUIContent.none, GUIStyle.none)) EndCapture("cancel button");
            DrawPlainButton(b2, "キャンセル");
        }
        else if (conflictOpen)
        {
            string other = Loc.Plain(InputBindings.RowOf(conflict.action).name);
            LocGUI.Label(new Rect(box.x + 20f, box.y + 18f, bw - 40f, 40f), InputBindings.CodeName(conflictCode), title);
            GUI.Label(new Rect(box.x + 20f, box.y + 62f, bw - 40f, 70f), Loc.Shape(Loc.Plain("このキーはすでに「{0}」に使われています").Replace("{0}", other)), body);
            if (conflict.isFixed)
            {
                GUI.Label(new Rect(box.x + 20f, box.y + 128f, bw - 40f, 40f), Loc.T("この操作のキーは変えられません"), UiKit.Label(15f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
                if (UiKit.Button(new Rect(box.center.x - b1.width * 0.5f, b1.y, b1.width, 50f), "キャンセル", Fit("キャンセル", b1.width, 19f))) conflictOpen = false;
            }
            else
            {
                if (UiKit.Button(b1, "入れ替える", Fit("入れ替える", b1.width, 19f), true)) { conflictOpen = false; InputBindings.Assign(capAction, capColumn, conflictCode, true); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide); }
                if (UiKit.Button(b2, "キャンセル", Fit("キャンセル", b2.width, 19f))) conflictOpen = false;
            }
        }
        else if (resetAsk)
        {
            LocGUI.Label(new Rect(box.x + 20f, box.y + 30f, bw - 40f, 60f), "初期設定に戻しますか？", title);
            LocGUI.Label(new Rect(box.x + 20f, box.y + 92f, bw - 40f, 60f), "キーボード・マウス・ゲームパッドの割り当てを、すべて最初の状態に戻します。", body);
            if (UiKit.Button(b1, "初期設定に戻す", Fit("初期設定に戻す", b1.width, 19f), true)) { resetAsk = false; InputBindings.ResetToDefaults(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide); }
            if (UiKit.Button(b2, "キャンセル", Fit("キャンセル", b2.width, 19f))) resetAsk = false;
        }
        else if (DisplaySettings.PendingConfirm)
        {
            LocGUI.Label(new Rect(box.x + 20f, box.y + 26f, bw - 40f, 60f), "この表示のままにしますか？", title);
            GUI.Label(new Rect(box.x + 20f, box.y + 92f, bw - 40f, 50f), Loc.F("{0}秒後に元の表示に戻ります", Mathf.CeilToInt(DisplaySettings.ConfirmLeft).ToString()), body);
            if (UiKit.Button(b1, "このままにする", Fit("このままにする", b1.width, 19f), true)) { DisplaySettings.Keep(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide); }
            if (UiKit.Button(b2, "元に戻す", Fit("元に戻す", b2.width, 19f))) DisplaySettings.Revert("button");
        }
        PadNav.EndLayer(padLayer);
    }

    static void DrawPlainButton(Rect r, string text)
    {
        UiBackdrop.Draw(r, 0.6f);
        LocGUI.Label(r, text, UiKit.Label(Fit(text, r.width, 19f), TextAnchor.MiddleCenter, true, UiTheme.Text));
    }

    // 長い言語でもボタンの1行に収まる字の大きさ(2026-10-25: fr / it の「元に戻す」が2行になってはみ出した)
    static float Fit(string text, float width, float size)
    {
        string shown = Loc.Auto(text);
        var st = new GUIStyle(UiKit.Label(size, TextAnchor.MiddleCenter, true)) { wordWrap = false };
        while (st.fontSize > 12 && st.CalcSize(new GUIContent(shown)).x > width - 16f) st.fontSize--;
        return st.fontSize;
    }

    // ---------------------------------------------------------------- 画面(PC)
    int selMode = -1; int selResIdx = -1;
    float MeasureScreen() => PlatformCaps.SupportsWindowMode ? HeadH + 4f + RowH * 5f + 22f * 2f + 16f : 0f;
    float DrawScreen(float x, float y, float w, bool interactive)
    {
        if (!PlatformCaps.SupportsWindowMode) return y;
        y = Head(x, y, w, "画面");
        if (selMode < 0 || DisplaySettings.PendingConfirm) { selMode = (int)DisplaySettings.CurrentMode; selResIdx = -1; }
        var mode = (DisplaySettings.Mode)selMode;
        RowLabel(x, y, "表示モード");
        float half = (w - LabelW - 6f) * 0.5f;
        int c = UiKit.Choice(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), selMode, new[] { "フルスクリーン", "ウィンドウ" }, Mathf.Min(Fit("フルスクリーン", half, 18f), Fit("ウィンドウ", half, 18f)));
        if (interactive && c != selMode) { selMode = c; selResIdx = -1; }
        y += RowH;

        var list = DisplaySettings.Resolutions((DisplaySettings.Mode)selMode);
        if (selResIdx < 0 || selResIdx >= list.Count) { selResIdx = list.IndexOf(DisplaySettings.CurrentRes); if (selResIdx < 0) selResIdx = list.Count - 1; }
        RowLabel(x, y, "解像度");
        float bx = x + LabelW, bw = w - LabelW, btn = Mathf.Min(64f, bw * 0.25f);
        if (UiKit.Button(new Rect(bx, y + 7f, btn, RowH - 14f), "−", 22f, false, false, interactive && selResIdx > 0)) selResIdx--;
        if (UiKit.Button(new Rect(bx + bw - btn, y + 7f, btn, RowH - 14f), "+", 22f, false, false, interactive && selResIdx < list.Count - 1)) selResIdx++;
        var res = list.Count > 0 ? list[Mathf.Clamp(selResIdx, 0, list.Count - 1)] : DisplaySettings.CurrentRes;
        GUI.Label(new Rect(bx + btn + 4f, y, bw - btn * 2f - 8f, RowH), $"{res.x} x {res.y}", UiKit.Label(19f, TextAnchor.MiddleCenter, true, Color.white));
        y += RowH;
        bool changed = (DisplaySettings.Mode)selMode != DisplaySettings.CurrentMode || res != DisplaySettings.CurrentRes;
        if (UiKit.Button(new Rect(x + LabelW, y + 5f, w - LabelW, RowH - 10f), "適用", 19f, changed, false, interactive && changed))
            DisplaySettings.Request((DisplaySettings.Mode)selMode, res);
        y += RowH;

        y = ChoiceRow(x, y, w, "垂直同期", DisplaySettings.VSync ? 1 : 0, "OFF", "ON", interactive, v => DisplaySettings.SetVSync(v == 1));
        RowLabel(x, y, "FPS上限");
        int fi = System.Array.IndexOf(DisplaySettings.FpsOptions, DisplaySettings.FpsCap); if (fi < 0) fi = DisplaySettings.FpsOptions.Length - 1;
        bool fpsOn = !DisplaySettings.VSync;
        var names = new string[DisplaySettings.FpsOptions.Length];
        for (int i = 0; i < names.Length; i++) names[i] = DisplaySettings.FpsOptions[i] > 0 ? DisplaySettings.FpsOptions[i].ToString() : Loc.T("無制限");
        int f = ChoiceEnabled(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), fi, names, 16f, interactive && fpsOn);
        if (interactive && fpsOn && f != fi) DisplaySettings.SetFpsCap(DisplaySettings.FpsOptions[f]);
        y += RowH;
        y = Note(x, y, w, fpsOn ? "ゲームの速さはFPSに関係なく同じです。" : "垂直同期がONの間は、画面のリフレッシュレートに合わせます。");
        return Note(x, y, w, "表示モードと解像度は「適用」の後、10秒以内に確定しないと元に戻ります。");
    }

    static int ChoiceEnabled(Rect r, int current, string[] options, float size, bool enabled)
    {
        int n = Mathf.Max(1, options.Length);
        float gap = 6f, w = (r.width - gap * (n - 1)) / n;
        int result = current;
        for (int i = 0; i < n; i++)
            if (UiKit.Button(new Rect(r.x + i * (w + gap), r.y, w, r.height), options[i], Fit(options[i], w, size), current == i, false, enabled)) result = i;
        return result;
    }

    // ---------------------------------------------------------------- 操作のキー設定の行(PC)
    float MeasureKeyRow() => PlatformCaps.SupportsKeyRebind ? RowH + 22f * 2f : 0f;
    float DrawKeyRow(float x, float y, float w, bool interactive)
    {
        if (!PlatformCaps.SupportsKeyRebind) return y;
        RowLabel(x, y, "キー設定");
        if (UiKit.Button(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), "変更する", 19f, false, false, interactive)) OpenKeyView();
        y += RowH;
        return Note(x, y, w, "キーボード・マウス・ゲームパッドの割り当てを変えられます。");
    }
}

using UnityEngine;

// 設定画面(2026-10-01)。ホーム/各メニュー/ポーズ/リザルトから開く共通のパネル(IMGUI、ほかのUIより手前)。
//  ・全画面は切り替えず、背景を薄く暗くしてパネルが少し浮き上がる(0.18秒)。閉じる時は短く消える(0.12秒)。
//    時間は実時間(ポーズ中=timeScale 0でも開閉の演出が止まらない)。
//  ・開いている間は背後へタップを通さない: IMGUIは最後に全画面の透明ボタンで吸い取り、uGUIのメニュー画面は UiInputGate を見る。
//  ・値はそれぞれの持ち主に直接反映・保存する(音量=AudioManager、向き=GameManager、補助=HighSpeedAssist、
//    揺れ/発光=GameSettings)。スライダーは動かしている間に反映し、離した時に保存する。
//  ・時間は止めない(マルチプレイ中に開いても共有世界を止めない。ポーズからはポーズのまま開く)。
//  ・縦長の画面では1列、横長では2列。収まらない時は内容部分だけ指でスクロールできる。
public partial class SettingsPanel : MonoBehaviour
{
    public static SettingsPanel Instance { get; private set; }

    enum St { Closed, Opening, Open, Closing }
    St state = St.Closed;
    float t;
    Vector2 scroll;
    bool dragging, pressed;
    float lastDragY, pressY;

    const float OpenTime = 0.18f, CloseTime = 0.12f;

    public static bool IsVisible => Instance != null && Instance.state != St.Closed;
    public static bool IsOpen => Instance != null && (Instance.state == St.Open || Instance.state == St.Opening);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[SettingsPanel]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SettingsPanel>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.ForceClose(); };
    }

    public static void OpenStatic() { if (Instance != null) Instance.Open(); }
    public static void CloseStatic() { if (Instance != null) Instance.Close(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugScroll(float y) { if (Instance != null) Instance.scroll.y = y; } // 撮影用(2026-10-31)
#endif

    public void Open()
    {
        if (state == St.Open || state == St.Opening) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        state = St.Opening; t = 0f; scroll = Vector2.zero; dragging = false; nameEditing = false;
        keyView = false; capturing = false; conflictOpen = false; resetAsk = false; selMode = -1; drawnH = 0f; // 2026-10-25
        PlaySe(true);
    }

    public void Close()
    {
        if (state == St.Closed || state == St.Closing) return;
        state = St.Closing; t = 0f;
        if (capturing) EndCapture("close");
        if (DisplaySettings.PendingConfirm) DisplaySettings.Revert("settings closed");
        GameSettings.Save();
        PortraitRunView.NotifyIfPending(); // 2026-10-08: ラン中に変えた縦のラン表示は閉じた時に反映
        UiInputGate.LatchUntilRelease();
        PlaySe(false);
    }

    void ForceClose() { state = St.Closed; t = 0f; dragging = false; if (capturing) EndCapture("force close"); if (DisplaySettings.PendingConfirm) DisplaySettings.Revert("force close"); }

    static void PlaySe(bool open)
    {
        var am = AudioManager.Instance;
        if (am != null) am.PlaySe(open ? SeId.UiOpen : SeId.UiClose); // 2026-10-06: 開く/閉じる
    }

    void Update()
    {
        if (state == St.Closed) return;
        t += Time.unscaledDeltaTime;
        UpdateCapture();
        if (state == St.Opening && t >= OpenTime) { state = St.Open; t = 0f; }
        else if (state == St.Closing && t >= CloseTime) { state = St.Closed; t = 0f; }
    }

    float S => UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f));

    // メニュー画面(デッキ編集/カード合成/キャラ選択/ステージ選択)の設定ボタン: どの画面も同じ右上の角(戻る/所持MILEと重ならない)。
    public static Rect MenuGearRect()
    {
        float size = Mathf.Clamp(Screen.height * 0.085f, 48f, 110f);
        Rect safe = StableSafeArea.Rect;
        return new Rect(safe.xMax - 14f - size, (Screen.height - safe.yMax) + 12f, size, size);
    }

    void DrawMenuGear()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.HasStarted || !gm.IsOverlayOpen) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (UiInputGate.DebugPanelOpen) return;
        GUI.depth = -2000;
        Rect r = MenuGearRect();
        if (UiKit.Button(r, "", 20f, false, true)) Open();
        UiKit.DrawGear(r, 0.6f, new Color(1f, 0.88f, 0.55f));
    }

    void OnGUI()
    {
        if (state == St.Closed) { DrawMenuGear(); return; }
        GUI.depth = -2000;
        int padLayer = PadNav.BeginLayer(10); // 設定は一番手前(後ろの画面へフォーカスが行かない)
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float sl = safe.xMin / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.yMin / s;

        // 開閉の進み(0..1)。開く: 暗さ/不透明度が上がり、少し下から浮き上がる。閉じる: 短く消える。
        float k = state == St.Opening ? Mathf.SmoothStep(0f, 1f, t / OpenTime) : state == St.Closing ? 1f - Mathf.Clamp01(t / CloseTime) : 1f;
        float rise = state == St.Opening ? (1f - k) * 14f : state == St.Closing ? (1f - k) * 6f : 0f;

        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        Color keepColor = GUI.color;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.55f * k));

        bool wide = w - sl - sr >= 980f;
        float pw = wide ? Mathf.Min(940f, w - sl - sr - 32f) : Mathf.Min(600f, w - sl - sr - 24f);
        float ph = Mathf.Min(h - st - sb - 28f, wide ? 600f : 900f);
        var panel = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f + rise, pw, ph);
        float scale = Mathf.Lerp(0.97f, 1f, k);
        GUI.color = new Color(1f, 1f, 1f, k);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), panel.center * s);

        OrnateUi.DrawPanel(panel, 0.94f);
        LocGUI.Label(new Rect(panel.x + 28f, panel.y + 14f, panel.width - 120f, 44f), keyView ? "キー設定" : "設定", UiKit.Label(28f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
        if (langOpen) LangPopupInput(panel);
        bool interactive = (state == St.Open || state == St.Opening) && !langOpen && !PanelModalOpen;
        if (UiIcons.CloseButton(new Rect(panel.xMax - 70f, panel.y + 14f, 50f, 46f)) && interactive) Close();

        // 内容(スクロール部分)
        var view = new Rect(panel.x + 20f, panel.y + 70f, panel.width - 40f, panel.height - 70f - 76f);
        float contentH;
        if (keyView) DrawKeyPage(view, interactive);
        else if (wide)
        {
            float colW = (view.width - 30f) * 0.5f;
            contentH = Mathf.Max(drawnH, Mathf.Max(MeasureAudio() + 16f + MeasureLanguage() + 16f + MeasureScreen() + 16f + MeasureProfile() + MeasureTransfer() + MeasureNoAds(), MeasureHud(colW) + 16f + MeasureDisplay() + MeasureControls() + 16f + MeasurePrivacy() + MeasureAbout()));
            BeginScroll(view, contentH);
            float ya = DrawAudio(0f, 0f, colW, interactive);
            ya = DrawLanguage(0f, ya + 16f, colW, interactive);
            if (PlatformCaps.SupportsWindowMode) ya = DrawScreen(0f, ya + 16f, colW, interactive);
            ya = DrawProfile(0f, ya + 16f, colW, interactive);
            ya = DrawTransfer(0f, ya, colW, interactive);
            ya = DrawNoAds(0f, ya, colW, interactive);
            float y2 = DrawHud(colW + 30f, 0f, colW, interactive); // 2026-10-31: 画面情報の表示(ラン中の一時停止から開いた時に最初に見える所)
            y2 = DrawDisplay(colW + 30f, y2 + 16f, colW, interactive);
            y2 = DrawControls(colW + 30f, y2 + 16f, colW, interactive);
            y2 = DrawPrivacy(colW + 30f, y2, colW, interactive);
            y2 = DrawAbout(colW + 30f, y2, colW, interactive);
            EndScroll(view, contentH);
            if (Event.current.type == EventType.Repaint) drawnH = Mathf.Max(ya, y2) + 12f;
        }
        else
        {
            contentH = Mathf.Max(drawnH, MeasureAudio() + MeasureLanguage() + MeasureScreen() + MeasureHud(view.width) + 16f + MeasureDisplay() + MeasureControls() + MeasureProfile() + MeasureTransfer() + MeasureNoAds() + MeasurePrivacy() + MeasureAbout() + 64f);
            BeginScroll(view, contentH);
            float y = DrawAudio(0f, 0f, view.width, interactive);
            y = DrawLanguage(0f, y + 16f, view.width, interactive);
            if (PlatformCaps.SupportsWindowMode) y = DrawScreen(0f, y + 16f, view.width, interactive);
            y = DrawHud(0f, y + 16f, view.width, interactive); // 2026-10-31: 画面情報の表示
            y = DrawDisplay(0f, y + 16f, view.width, interactive);
            y = DrawControls(0f, y + 16f, view.width, interactive);
            y = DrawProfile(0f, y + 16f, view.width, interactive);
            y = DrawTransfer(0f, y, view.width, interactive);
            y = DrawNoAds(0f, y, view.width, interactive);
            y = DrawPrivacy(0f, y, view.width, interactive);
            y = DrawAbout(0f, y, view.width, interactive);
            EndScroll(view, contentH);
            if (Event.current.type == EventType.Repaint) drawnH = y + 12f;
        }

        if (UiKit.Button(new Rect(panel.center.x - 110f, panel.yMax - 66f, 220f, 52f), keyView ? "戻る" : "閉じる", 22f, true) && interactive) { if (keyView) HandleBack(); else Close(); }
        if (langOpen) DrawLangPopup(panel);
        DrawPanelModal(panel);

        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        GUI.color = keepColor;
        // 背後へタップを通さない(最後に全画面で吸い取る。パネルの中の操作は上で先に受け取っている)
        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none);
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
        PadNav.EndLayer(padLayer);
    }

    // ---------------------------------------------------------------- スクロール(指でドラッグ)
    void BeginScroll(Rect view, float contentH)
    {
        float max = Mathf.Max(0f, contentH - view.height);
        scroll.y = Mathf.Clamp(scroll.y, 0f, max);
        Event e = Event.current;
        if (max > 0f)
        {
            // 2026-10-08: ボタンの上から始めたドラッグでもスクロールする。閾値を越えたら押していたボタンを取り消す(離しても押されない)
            if (e.type == EventType.MouseDown && view.Contains(e.mousePosition)) { pressed = true; dragging = false; pressY = lastDragY = e.mousePosition.y; }
            else if (e.type == EventType.MouseDrag && pressed)
            {
                if (!dragging && Mathf.Abs(e.mousePosition.y - pressY) > UiScroll.DragThreshold) dragging = true;
                if (dragging) { scroll.y = Mathf.Clamp(scroll.y - (e.mousePosition.y - lastDragY), 0f, max); lastDragY = e.mousePosition.y; GUIUtility.hotControl = 0; e.Use(); }
            }
            else if (e.type == EventType.MouseUp) { if (dragging) e.Use(); pressed = dragging = false; }
            else if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition) && !langOpen && !PanelModalOpen) { scroll.y = Mathf.Clamp(scroll.y + e.delta.y * 20f, 0f, max); e.Use(); }
            // ゲームパッド: 端から先へ進もうとした時/右スティックで動かす(2026-10-06)
            if (e.type == EventType.Layout) { int req = PadNav.ScrollRequestFor(PadNav.ToScreen(view)); if (req != 0) scroll.y = Mathf.Clamp(scroll.y + req * view.height * 0.5f, 0f, max); }
        }
        PadNav.PushClip(view);
        GUI.BeginGroup(view);
        GUI.BeginGroup(new Rect(0f, -scroll.y, view.width, Mathf.Max(view.height, contentH) + scroll.y));
    }

    void EndScroll(Rect view, float contentH)
    {
        GUI.EndGroup();
        GUI.EndGroup();
        PadNav.PopClip();
        float max = contentH - view.height;
        if (max > 1f)
        {
            // 位置の目安(細いバー)
            float barH = Mathf.Max(30f, view.height * view.height / contentH);
            float y = view.y + (view.height - barH) * (scroll.y / max);
            UiKit.Fill(new Rect(view.xMax + 6f, y, 5f, barH), new Color(1f, 0.85f, 0.45f, 0.6f));
        }
    }

    // ---------------------------------------------------------------- 行
    const float RowH = 54f, HeadH = 40f, LabelW = 160f, ValueW = 84f;

    float Head(float x, float y, float w, string text)
    {
        LocGUI.Label(new Rect(x, y, w, HeadH), text, UiKit.Label(22f, TextAnchor.MiddleLeft, true, new Color(1f, 0.82f, 0.4f)));
        UiKit.Fill(new Rect(x, y + HeadH - 4f, w, 2f), new Color(1f, 0.8f, 0.4f, 0.45f));
        return y + HeadH + 4f;
    }

    // 2026-10-08: 長い見出し(長い言語/狭い縦画面)は右の操作に重ならないよう、小さめの字で2行に折り返す
    void RowLabel(float x, float y, string text)
    {
        var st = new GUIStyle(UiKit.Label(20f));
        float max = LabelW - 8f;
        string shown = Loc.Auto(text);
        if (st.CalcSize(new GUIContent(shown)).x > max)
        {
            st = new GUIStyle(UiKit.Label(16f)); st.wordWrap = true;
            // 2026-10-24: 1語が長くて折り返せない時(ドイツ語の Höchstgeschwindigkeit など)は語の途中で切れないように小さくする
            if (shown.IndexOf(' ') < 0) while (st.fontSize > 12 && st.CalcSize(new GUIContent(shown)).x > max) st.fontSize--;
        }
        LocGUI.Label(new Rect(x, y, max, RowH), text, st);
    }

    float SliderRow(float x, float y, float w, string text, float value, string valueText, bool enabled, bool interactive, System.Action<float, bool> set)
    {
        RowLabel(x, y, text);
        var r = new Rect(x + LabelW, y + 6f, w - LabelW - ValueW - 8f, RowH - 12f);
        float v = UiKit.Slider(r, value, enabled && interactive, out bool released);
        if (interactive && enabled && (Mathf.Abs(v - value) > 1e-5f || released)) set(v, released);
        LocGUI.Label(new Rect(x + w - ValueW, y, ValueW, RowH), valueText, UiKit.Label(19f, TextAnchor.MiddleRight, false, enabled ? Color.white : new Color(0.6f, 0.6f, 0.65f)));
        return y + RowH;
    }

    float ChoiceRow(float x, float y, float w, string text, int current, string a, string b, bool interactive, System.Action<int> set)
    {
        RowLabel(x, y, text);
        int c = UiKit.Choice(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), current, a, b, 18f);
        if (interactive && c != current) set(c);
        return y + RowH;
    }

    // 2026-10-08: 説明は幅で折り返す(長い言語でも縮めずに全部出す。内容全体は BeginScroll でスクロールする)
    float Note(float x, float y, float w, string text)
    {
        var st = new GUIStyle(UiKit.Label(15f, TextAnchor.UpperLeft, false, new Color(0.75f, 0.8f, 0.9f)));
        st.wordWrap = true;
        string t = Loc.Auto(text);
        float h = Mathf.Max(22f, st.CalcHeight(new GUIContent(t), w) + 2f);
        GUI.Label(new Rect(x, y - 4f, w, h + 4f), t, st);
        return y + h;
    }
    float drawnH; // 前のフレームで実際に描いた内容の高さ(折り返しで Measure より伸びた分もスクロールに入れる)

    static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

    // ---------------------------------------------------------------- 音
    float MeasureAudio() => HeadH + 4f + RowH * 5f;
    float DrawAudio(float x, float y, float w, bool interactive)
    {
        var am = AudioManager.Instance;
        y = Head(x, y, w, "音");
        if (am == null) return Note(x, y, w, "(音声が使えません)");
        y = ChoiceRow(x, y, w, "全体ミュート", am.Muted ? 1 : 0, "OFF", "ON", interactive, c => { am.SetMuted(c == 1); am.PlaySe(SeId.UiToggle); });
        bool on = !am.Muted;
        y = SliderRow(x, y, w, "全体", am.MasterVolume, Pct(am.MasterVolume), on, interactive, (v, rel) => { am.SetMasterVolume(v, rel); if (rel) TestSe(); });
        y = SliderRow(x, y, w, "BGM", am.BgmVolume, Pct(am.BgmVolume), on, interactive, (v, rel) => am.SetBgmVolume(v, rel));
        y = SliderRow(x, y, w, "SE", am.SfxVolume, Pct(am.SfxVolume), on, interactive, (v, rel) => { am.SetSfxVolume(v, rel); if (rel) TestSe(); });
        y = SliderRow(x, y, w, "環境音", am.EnvVolume, Pct(am.EnvVolume), on, interactive, (v, rel) => am.SetEnvVolume(v, rel));
        return y;
    }

    static void TestSe()
    {
        var am = AudioManager.Instance;
        if (am != null) am.PlaySe(SeId.Hit); // 離した時に今の音量で1回鳴らす(2026-10-06: ゲーム中の基準の音=通常ヒット)
    }

    // ---------------------------------------------------------------- 言語(2026-10-07)
    // 言語の名前はその言語自身の名前で。選ぶとすぐ切り替わる(再起動なし)。端末の言語に合わせている間は「(端末)」と出す
    const float LangRowH = 54f;
    float MeasureLanguage() => RowH + 26f;
    // 2026-10-09(依頼G): 30言語のボタンが設定の大部分を占めていた → 普段は「言語 / Language  日本語 ▼」の1行。押すと縦の一覧
    float DrawLanguage(float x, float y, float w, bool interactive)
    {
        string label = Loc.IsJapanese ? "言語 / Language" : Loc.T("言語") + " / Language";
        var lab = new GUIStyle(UiKit.Label(20f)); lab.wordWrap = true;
        float lw = Mathf.Min(w * 0.45f, 220f);
        GUI.Label(new Rect(x, y, lw, RowH), label, lab);
        var r = new Rect(x + lw, y + 6f, w - lw, RowH - 12f);
        string cur = CurrentLangName();
        OrnateUi.DrawPanel(r, 0.75f);
        var st = new GUIStyle(UiKit.Label(19f, TextAnchor.MiddleLeft, true, new Color(1f, 0.9f, 0.6f)));
        var keepFont = st.font; st.font = LocFonts.FontFor(Loc.Current);
        GUI.Label(new Rect(r.x + 16f, r.y, r.width - 56f, r.height), cur, st);
        st.font = keepFont;
        DrawTriangle(new Rect(r.xMax - 40f, r.y, 28f, r.height), new Color(1f, 0.85f, 0.45f));
        if (interactive && (GUI.Button(r, GUIContent.none, GUIStyle.none) | PadNav.Button(r)))
        {
            langOpen = true; langScroll = -1f; langPress = false;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiOpen);
        }
        y += RowH;
        return Note(x, y, w, Loc.UserChose ? Loc.T("選んだ言語で表示します") : Loc.T("端末の言語に合わせています"));
    }

    static string CurrentLangName()
    {
        foreach (var l in Loc.Languages) if (l.code == Loc.Current) return Loc.NativeName(l);
        return Loc.Current;
    }

    // 下向きの三角(▼ が無いフォントがあるので描く)
    static void DrawTriangle(Rect r, Color c)
    {
        float cx = r.center.x, cy = r.center.y, s = Mathf.Min(r.width, r.height) * 0.42f;
        for (int i = 0; i < 8; i++)
        {
            float t = i / 8f, ww = s * 2f * (1f - t);
            UiKit.Fill(new Rect(cx - ww * 0.5f, cy - s * 0.5f + t * s, ww, s / 8f + 0.5f), c);
        }
    }

    // ---- 言語の一覧(縦のスクロール)。タップで適用して閉じる / スクロールして離しただけでは選ばない / 外側か「閉じる」で元のまま閉じる
    bool langOpen, langPress, langDragged;
    public static bool LangListOpen => Instance != null && Instance.state != St.Closed && Instance.langOpen;
    public static void CloseLangList() { if (Instance != null && Instance.langOpen) Instance.CloseLang(false); }
    float langScroll = -1f, langPressY, langPressScroll;
    Vector2 langPressPos;
    Rect LangBox(Rect panel) => new Rect(panel.x + 24f, panel.y + 66f, panel.width - 48f, panel.height - 66f - 84f);
    Rect LangList(Rect panel) { var b = LangBox(panel); return new Rect(b.x + 10f, b.y + 54f, b.width - 20f, b.height - 54f - 10f); }
    Rect LangClose(Rect panel) => new Rect(panel.center.x - 110f, panel.yMax - 72f, 220f, 54f);
    float LangMax(Rect panel) => Mathf.Max(0f, Loc.Languages.Length * LangRowH - LangList(panel).height);

    void LangPopupInput(Rect panel)
    {
        var e = Event.current;
        if (e == null) return;
        var list = LangList(panel);
        float max = LangMax(panel);
        if (langScroll < 0f)
        {
            // 開いた時: 今の言語が見える位置へ
            int ci = 0; for (int i = 0; i < Loc.Languages.Length; i++) if (Loc.Languages[i].code == Loc.Current) ci = i;
            langScroll = Mathf.Clamp(ci * LangRowH - list.height * 0.4f, 0f, max);
        }
        // パッド/キーボード: 決定で今フォーカスの言語、戻るで閉じる(PadNav の層の中)
        switch (e.type)
        {
            case EventType.MouseDown:
                langPress = true; langDragged = false; langPressPos = e.mousePosition; langPressY = e.mousePosition.y; langPressScroll = langScroll;
                e.Use();
                break;
            case EventType.MouseDrag:
                if (langPress)
                {
                    if (Mathf.Abs(e.mousePosition.y - langPressPos.y) > 10f) langDragged = true;
                    if (langDragged) langScroll = Mathf.Clamp(langPressScroll - (e.mousePosition.y - langPressY), 0f, max);
                }
                e.Use();
                break;
            case EventType.MouseUp:
                if (langPress && !langDragged)
                {
                    Vector2 m = e.mousePosition;
                    if (LangClose(panel).Contains(m) || !LangBox(panel).Contains(m)) CloseLang(false);
                    else if (list.Contains(m))
                    {
                        int i = Mathf.FloorToInt((m.y - list.y + langScroll) / LangRowH);
                        if (i >= 0 && i < Loc.Languages.Length) PickLang(i);
                    }
                }
                langPress = false;
                e.Use();
                break;
            case EventType.ScrollWheel:
                langScroll = Mathf.Clamp(langScroll + e.delta.y * 20f, 0f, max);
                e.Use();
                break;
        }
    }

    void PickLang(int i)
    {
        var l = Loc.Languages[i];
        if (Loc.Current != l.code) { Loc.Set(l.code); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle); }
        CloseLang(true);
    }

    void CloseLang(bool picked)
    {
        langOpen = false; langPress = false;
        if (!picked && AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiClose);
        UiInputGate.LatchUntilRelease(); // 一覧を閉じた指で後ろの設定を押さない
    }

    void DrawLangPopup(Rect panel)
    {
        var box = LangBox(panel); var list = LangList(panel);
        UiKit.Fill(panel, new Color(0.01f, 0.02f, 0.05f, 0.6f));
        OrnateUi.DrawPanel(box, 0.97f);
        GUI.Label(new Rect(box.x + 20f, box.y + 8f, box.width - 40f, 40f), Loc.IsJapanese ? "言語 / Language" : Loc.T("言語") + " / Language", UiKit.Label(22f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
        GUI.BeginClip(list);
        // UiKit.Label は使い回しの1つの GUIStyle を返すので、名前用と英語名用は別に複製する
        var st = new GUIStyle(UiKit.Label(20f, TextAnchor.MiddleLeft, true, Color.white));
        var en = new GUIStyle(UiKit.Label(14f, TextAnchor.MiddleRight, false, new Color(0.7f, 0.75f, 0.85f)));
        var keepFont = st.font;
        for (int i = 0; i < Loc.Languages.Length; i++)
        {
            float ry = i * LangRowH - langScroll;
            if (ry + LangRowH < 0f || ry > list.height) continue;
            var l = Loc.Languages[i];
            bool sel = l.code == Loc.Current;
            var row = new Rect(0f, ry + 3f, list.width, LangRowH - 6f);
            UiKit.Fill(row, sel ? new Color(0.45f, 0.35f, 0.12f, 0.75f) : new Color(0.08f, 0.1f, 0.2f, 0.85f));
            if (sel) { UiKit.Fill(new Rect(row.x, row.y, 4f, row.height), new Color(1f, 0.85f, 0.4f)); DrawCheck(new Rect(row.xMax - 44f, row.y + 6f, 32f, row.height - 12f)); }
            st.font = LocFonts.FontFor(l.code);
            st.normal.textColor = sel ? new Color(1f, 0.92f, 0.65f) : Color.white;
            GUI.Label(new Rect(row.x + 18f, row.y, row.width - 80f, row.height), Loc.NativeName(l), st);
            st.font = keepFont;
            GUI.Label(new Rect(row.xMax - 240f, row.y, 180f, row.height), l.english, en);
        }
        GUI.EndClip();
        float max = LangMax(panel);
        if (max > 0f)
        {
            float bh = Mathf.Max(30f, list.height * list.height / (list.height + max));
            float by = list.y + (list.height - bh) * (langScroll / max);
            UiKit.Fill(new Rect(list.xMax + 3f, by, 4f, bh), new Color(1f, 0.85f, 0.4f, 0.6f));
        }
        var c = LangClose(panel);
        OrnateUi.DrawPanel(c, 0.85f);
        GUI.Label(c, Loc.Auto("閉じる"), UiKit.Label(22f, TextAnchor.MiddleCenter, true, new Color(1f, 0.93f, 0.75f)));
    }

    static void DrawCheck(Rect r)
    {
        // チェック印(✓ が無いフォントがあるので描く)
        Color c = new Color(1f, 0.88f, 0.45f);
        Vector2 a = new Vector2(r.x + r.width * 0.18f, r.y + r.height * 0.55f), m = new Vector2(r.x + r.width * 0.42f, r.y + r.height * 0.80f), b = new Vector2(r.x + r.width * 0.90f, r.y + r.height * 0.22f);
        void Seg(Vector2 p0, Vector2 p1)
        {
            // 回転を使わず、小さな四角を並べて線にする(拡大/切り抜きの中でもずれない)
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p0, p1) / 1.5f));
            for (int i = 0; i <= n; i++) { var q = Vector2.Lerp(p0, p1, i / (float)n); UiKit.Fill(new Rect(q.x - 2.5f, q.y - 2.5f, 5f, 5f), c); }
        }
        Seg(a, m); Seg(m, b);
    }

    // ---------------------------------------------------------------- 表示
    float MeasureDisplay() => RowH + (PlatformCaps.SupportsOrientation ? HeadH + 4f + RowH * 5f + 22f + RowH * 2f - 14f + 90f + 44f + 24f + 22f : HeadH + 4f + RowH * 4f + 24f); // 説明が2行に折り返す分。ラン表示は見出しの行+選択の行
    float DrawDisplay(float x, float y, float w, bool interactive)
    {
        var gm = GameManager.Instance;
        y = Head(x, y, w, "表示");
        // 2026-10-31: 文字・UI の大きさ(設定・お知らせ・確認の窓など。小さい画面では効かない)
        if (Loc.Ready(LocExit.UiSize)) y = ChoiceRow(x, y, w, LocExit.UiSize, UiScalePref.Index, LocExit.UiStandard, Loc.Reviewed(LocExit.UiLarge, "大"), interactive, v => UiScalePref.Set(v));
        if (gm != null && PlatformCaps.SupportsOrientation)
        {
            // 2026-10-09(依頼G): 横 / 縦 は端末の自動回転に関係なくその向きに固定。自動 だけ端末の回転の設定に従う
            // 2026-10-31: 自動 / 縦固定 / 横固定(分かりやすい言葉と並び。訳が入るまでは前の言葉)
            RowLabel(x, y, "画面の向き");
            var order = new[] { OrientationControl.Mode.Auto, OrientationControl.Mode.Portrait, OrientationControl.Mode.Landscape };
            int cur = System.Array.IndexOf(order, gm.OrientationMode);
            string[] on = Loc.Ready(LocExit.OrientPortrait) && Loc.Ready(LocExit.OrientLandscape)
                ? new[] { LocExit.OrientAuto, LocExit.OrientPortrait, LocExit.OrientLandscape } : new[] { "自動", "縦画面", "横画面" };
            int c = UiKit.Choice(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), cur, on, 18f);
            if (interactive && c != cur && c >= 0) gm.SetOrientationMode(order[c]);
            y += RowH;
            y = Note(x, y, w, "自動: 端末の自動回転の設定に従います");
        }
        else if (PlatformCaps.SupportsOrientation) y += RowH;
        if (PlatformCaps.SupportsOrientation) y = DrawPortraitRunView(x, y, w, interactive); // 2026-10-25: 画面の向き/縦画面のラン表示は向きを変えられる環境だけ
        y = ChoiceRow(x, y, w, "画面揺れ", GameSettings.ScreenShake ? 1 : 0, "OFF", "ON", interactive, c => GameSettings.SetScreenShake(c == 1));
        y = ChoiceRow(x, y, w, "障害物の警告", GameSettings.ObstacleWarn == 1 ? 0 : 1, "点滅あり", "輪郭のみ", interactive, c => GameSettings.SetObstacleWarn(c == 0 ? 1 : 2)); // 2026-10-11
        y = SliderRow(x, y, w, "発光演出", GameSettings.GlowIntensity, Pct(GameSettings.GlowIntensity), true, interactive, (v, rel) => GameSettings.SetGlowIntensity(v, rel));
        y = SliderRow(x, y, w, "仲間の識別オーラ", GameSettings.AuraIntensity, Pct(GameSettings.AuraIntensity), true, interactive, (v, rel) => GameSettings.SetAuraIntensity(v, rel)); // 2026-10-14: 0 でも名前と番号は出る
        return Note(x, y, w, "※敵の攻撃予兆や操作に必要な表示は弱くなりません");
    }

    // ---------------------------------------------------------------- 縦画面のラン表示(2026-10-08、依頼E-4)
    // 3つの見せ方(横から/上下2段/斜め上)を名前と短い説明と小さな図で選ぶ。縦の画面の時だけ効く(横画面の表示は変わらない)。ラン中は設定を閉じた時に反映
    // 2026-10-09: 3つ並べると名前が入らないので、見出しの行の下に全幅で並べる
    static readonly int[] RunViewOrder = { PortraitRunView.Side, PortraitRunView.Fold, PortraitRunView.Oblique };
    float DrawPortraitRunView(float x, float y, float w, bool interactive)
    {
        int cur = PortraitRunView.Mode;
        RowLabel(x, y, "縦画面のラン表示");
        y += RowH - 8f;
        int idx = System.Array.IndexOf(RunViewOrder, cur);
        int c = UiKit.Choice(new Rect(x, y, w, RowH - 14f), idx, new[] { "横から見る", "上下2段で見る", "斜め上から見る" }, 17f);
        if (interactive && c != idx && c >= 0) PortraitRunView.Set(RunViewOrder[c]);
        y += RowH - 6f;
        // 比較図(選んでいる方を明るく)
        float gap = 10f, bw = (w - gap * 2f) / 3f, bh = 86f;
        for (int i = 0; i < 3; i++) DrawRunViewIcon(new Rect(x + i * (bw + gap), y, bw, bh), RunViewOrder[i], cur == RunViewOrder[i]);
        y += bh + 6f;
        y = Note(x, y, w, cur == PortraitRunView.Side ? "横から見る: 道が横に流れる。足もとと上下の段が見やすい"
            : cur == PortraitRunView.Fold ? "上下2段で見る: 下が今の走り、上がその先(右端で折り返し)。先の敵や穴が早めに見える"
            : "斜め上から見る: 道が奥へ続く。先の敵や穴を早めに見通せる");
        return Note(x, y, w, PortraitRunView.IsPortraitScreen ? (HasStartedRun ? "ラン中の変更は設定を閉じると反映されます" : "縦画面のランで使われます") : "縦画面にした時に使われます(横画面の表示は変わりません)");
    }
    static bool HasStartedRun => GameManager.Instance != null && GameManager.Instance.HasStarted;

    static void DrawRunViewIcon(Rect r, int mode, bool on)
    {
        Color frame = on ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.6f, 0.65f, 0.75f, 0.6f);
        UiKit.Fill(r, new Color(0.25f, 0.45f, 0.75f, on ? 0.55f : 0.25f));            // 空
        UiKit.Fill(new Rect(r.x, r.y, r.width, 2f), frame); UiKit.Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), frame);
        UiKit.Fill(new Rect(r.x, r.y, 2f, r.height), frame); UiKit.Fill(new Rect(r.xMax - 2f, r.y, 2f, r.height), frame);
        Color ground = new Color(0.45f, 0.33f, 0.22f, on ? 1f : 0.6f), grass = new Color(0.55f, 0.75f, 0.3f, on ? 1f : 0.6f);
        if (mode == 0)
        {
            float gy = r.y + r.height * 0.74f;
            UiKit.Fill(new Rect(r.x + 2f, gy, r.width - 4f, r.yMax - gy - 2f), ground);
            UiKit.Fill(new Rect(r.x + 2f, gy, r.width - 4f, 3f), grass);
            UiKit.Fill(new Rect(r.x + r.width * 0.3f, gy - 14f, 7f, 14f), new Color(0.9f, 0.2f, 0.2f, on ? 1f : 0.6f)); // キャラ
            UiKit.Fill(new Rect(r.x + r.width * 0.75f, gy - 10f, 9f, 10f), new Color(0.2f, 0.2f, 0.25f, on ? 1f : 0.6f)); // 敵
        }
        else if (mode == PortraitRunView.Fold)
        {
            // 上下2段: 下の段(キャラ)と上の段(その先、左右反転)。右端の金の印で折り返す
            float mid = r.y + r.height * 0.5f;
            Color gold = new Color(1f, 0.82f, 0.38f, on ? 1f : 0.6f);
            foreach (var (top, bottom) in new[] { (r.y + 2f, mid - 1f), (mid + 1f, r.yMax - 2f) })
            {
                float gy = Mathf.Lerp(top, bottom, 0.7f);
                UiKit.Fill(new Rect(r.x + 2f, gy, r.width - 4f, bottom - gy), ground);
                UiKit.Fill(new Rect(r.x + 2f, gy, r.width - 4f, 2f), grass);
            }
            UiKit.Fill(new Rect(r.x + 2f, mid - 1f, r.width - 4f, 2f), gold);
            UiKit.Fill(new Rect(r.xMax - 5f, mid - 12f, 3f, 24f), gold);
            float g2 = Mathf.Lerp(mid + 1f, r.yMax - 2f, 0.7f), g1 = Mathf.Lerp(r.y + 2f, mid - 1f, 0.7f);
            UiKit.Fill(new Rect(r.x + r.width * 0.25f, g2 - 12f, 6f, 12f), new Color(0.9f, 0.2f, 0.2f, on ? 1f : 0.6f)); // キャラ(下の段)
            UiKit.Fill(new Rect(r.x + r.width * 0.3f, g1 - 8f, 8f, 8f), new Color(0.2f, 0.2f, 0.25f, on ? 1f : 0.6f)); // 先の敵(上の段)
        }
        else
        {
            // 斜め上(2026-10-09 採用 B): 道が画面の下から上の奥へ、縦に近く伸びる(遠いほど細く)
            int n = 12;
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n;
                float px = Mathf.Lerp(r.x + r.width * 0.3f, r.x + r.width * 0.72f, t0 * t0), py = Mathf.Lerp(r.yMax - 8f, r.y + 8f, t0);
                float w = Mathf.Lerp(r.width * 0.34f, 4f, t0);
                UiKit.Fill(new Rect(px - w * 0.5f, py, w, (r.height - 16f) / n + 1f), ground);
                UiKit.Fill(new Rect(px - w * 0.5f, py, 2f, (r.height - 16f) / n + 1f), grass);
            }
            UiKit.Fill(new Rect(r.x + r.width * 0.3f, r.yMax - 26f, 7f, 14f), new Color(0.9f, 0.2f, 0.2f, on ? 1f : 0.6f)); // キャラ(手前)
            UiKit.Fill(new Rect(r.x + r.width * 0.62f, r.y + 20f, 4f, 5f), new Color(0.2f, 0.2f, 0.25f, on ? 1f : 0.6f));   // 先の敵(奥)
        }
    }

    // ---------------------------------------------------------------- 操作(2026-10-08: オートの個別設定)
    // 全体の ON/OFF と開始の速さ(今までの「補助開始速度」。距離ではなく走る速さで始まる)は今までどおり。
    // ボス戦でもオート / 自動攻撃 / 自動回避 を個別に切り替える。説明は実際に対応している範囲だけ(必ず避けられるとは書かない)
    float MeasureControls() => HeadH + 4f + MeasureKeyRow() + MeasureMaxSpeed() + RowH * 5f + 22f * 6f + 12f + RowH + 22f;
    float DrawControls(float x, float y, float w, bool interactive)
    {
        var hsa = HighSpeedAssist.Instance;
        y = Head(x, y, w, "操作");
        y = DrawKeyRow(x, y, w, interactive); // 2026-10-25: キー設定(PC)
        y = DrawMaxSpeed(x, y, w, interactive, hsa);
        if (hsa == null) return Note(x, y, w, "(自動補助は使えません)");
        y = ChoiceRow(x, y, w, "オートモード", hsa.assistEnabled ? 1 : 0, "OFF", "ON", interactive, c => hsa.SetEnabled(c == 1));
        bool on = hsa.assistEnabled;
        float range = HighSpeedAssist.MaxEngageKmh - HighSpeedAssist.MinEngageKmh;
        float v01 = (hsa.EngageSettingKmh - HighSpeedAssist.MinEngageKmh) / range;
        y = SliderRow(x, y, w, "オート開始速度", v01, $"{hsa.EngageSettingKmh:F0}km/h", on, interactive,
            (v, rel) => hsa.SetEngageKmh(HighSpeedAssist.MinEngageKmh + v * range, rel));
        y = ChoiceRow(x, y, w, "ボス戦でもオート", hsa.autoInBoss ? 1 : 0, "OFF", "ON", interactive && on, c => hsa.SetAutoInBoss(c == 1));
        y = ChoiceRow(x, y, w, "自動攻撃", hsa.autoAttack ? 1 : 0, "OFF", "ON", interactive && on, c => hsa.SetAutoAttack(c == 1));
        y = ChoiceRow(x, y, w, "自動回避", hsa.autoAvoid ? 1 : 0, "OFF", "ON", interactive && on, c => hsa.SetAutoAvoid(c == 1));
        y = Note(x, y, w, "走る速さが開始速度以上になると、ONにした操作だけ自動で行います。");
        y = Note(x, y, w, "自動攻撃: 前の敵・壊せる障害物・ボスへの攻撃。");
        y = Note(x, y, w, "自動回避: 穴・障害物・壁・トゲへのジャンプ。ボス戦では弾や攻撃範囲も。");
        y = Note(x, y, w, "通常の敵の攻撃は避けません。必ず避けられるわけではありません。");
        y = Note(x, y, w, "手動操作を優先します。「ボス戦でもオート」がOFFなら、ボス戦中は自動操作しません。");
        y = Note(x, y, w, "※ ボス戦でもオートは、ボスと戦っている間は速さに関係なく働きます。");
        return DrawHowToPlay(x, y + 12f, w, interactive);
    }

    // ---------------------------------------------------------------- 最高速度(2026-10-24)
    // 自然に加速した時の最高速度の倍率(60〜120%、5刻み)。難易度ではなく遊びやすい速さの好み(EASY/HARD とは書かない)。
    //  ・−/+ で 5% ずつ(タッチでも中途半端な値にならない)。100% は「(標準)」
    //  ・下に、選んでいるキャラの目安(カードなし)と、ゆっくり/標準/速い
    //  ・ラン中は変えられない(次のランから)。高速補助の設定とは独立(勝手に変えない)。目安が補助の開始速度より低ければ小さく知らせる
    float MeasureMaxSpeed() => RowH + 22f * 3f;
    float DrawMaxSpeed(float x, float y, float w, bool interactive, HighSpeedAssist hsa)
    {
        int pct = GameSettings.MaxSpeedPercent;
        bool canEdit = interactive && !HasStartedRun;
        RowLabel(x, y, LocPendingReview.MaxSpeedTitle);
        float bx = x + LabelW, bw = w - LabelW, btn = Mathf.Min(64f, bw * 0.3f);
        if (UiKit.Button(new Rect(bx, y + 7f, btn, RowH - 14f), "−", 22f, false, false, canEdit && pct > GameSettings.MaxSpeedMin))
        { GameSettings.SetMaxSpeedPercent(pct - GameSettings.MaxSpeedStep); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle); }
        if (UiKit.Button(new Rect(bx + bw - btn, y + 7f, btn, RowH - 14f), "+", 22f, false, false, canEdit && pct < GameSettings.MaxSpeedMax))
        { GameSettings.SetMaxSpeedPercent(pct + GameSettings.MaxSpeedStep); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle); }
        pct = GameSettings.MaxSpeedPercent;
        string val = pct == GameSettings.MaxSpeedDefault ? Loc.Shape($"{pct}% " + Loc.Plain(LocPendingReview.MaxSpeedStandardSuffix)) : $"{pct}%";
        // 長い言語(「100% (Стандарт)」など)でも1行に収まるまで小さくする
        var vst = new GUIStyle(UiKit.Label(20f, TextAnchor.MiddleCenter, true, canEdit ? Color.white : new Color(0.65f, 0.65f, 0.7f))) { wordWrap = false };
        float vw = bw - btn * 2f - 8f;
        while (vst.fontSize > 12 && vst.CalcSize(new GUIContent(val)).x > vw) vst.fontSize--;
        GUI.Label(new Rect(bx + btn + 4f, y, vw, RowH), val, vst);
        y += RowH;
        // 選んでいるキャラの目安(自然に加速した時の最高速度、カードなし)
        var gm = GameManager.Instance;
        var def = gm != null ? CharacterDatabase.FindById(gm.SelectedCharacterId) : null;
        var pc = PlayerController.Instance;
        float capKmh = pc != null ? pc.naturalSpeedCapKmh : 100f;
        float kmh = capKmh * (def != null ? Mathf.Max(0.01f, def.groundMobilityMultiplier) : 1f) * pct / 100f;
        string tag = pct < GameSettings.MaxSpeedDefault ? LocPendingReview.MaxSpeedSlow : pct > GameSettings.MaxSpeedDefault ? LocPendingReview.MaxSpeedFast : LocPendingReview.MaxSpeedStandard;
        // 名前と目安の言葉は処理前の訳でつないで、最後に1回だけ並べ替え/字形の処理をする(アラビア文字・インドの文字で崩れないように)
        string who = def != null ? Loc.Plain(def.displayName) : "";
        string est = Loc.Plain(LocPendingReview.MaxSpeedEstimate).Replace("{0}", who).Replace("{1}", Mathf.RoundToInt(kmh).ToString());
        y = Note(x, y, w, Loc.Shape(est + "  " + Loc.Plain(tag)));
        if (HasStartedRun) y = Note(x, y, w, LocPendingReview.MaxSpeedLocked);
        else if (hsa != null && hsa.assistEnabled && kmh < hsa.EngageSettingKmh - 0.5f) y = Note(x, y, w, LocPendingReview.MaxSpeedBelowAssist);
        else y = Note(x, y, w, LocPendingReview.MaxSpeedNote);
        return y;
    }

    // ---------------------------------------------------------------- プロフィール(2026-10-08: コードネーム)
    string nameEdit; bool nameEditing;
    float MeasureProfile() => HeadH + 4f + RowH + 22f * 2f;
    float DrawProfile(float x, float y, float w, bool interactive)
    {
        y = Head(x, y, w, "プロフィール");
        RowLabel(x, y, "コードネーム");
        float fw = w - LabelW - 120f;
        var fr = new Rect(x + LabelW, y + 7f, fw, RowH - 14f);
        if (!nameEditing) nameEdit = Codename.Current;
        GUI.SetNextControlName("codename");
        var style = new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        PadNav.NoteTextField();
        string edited = GUI.TextField(fr, nameEdit ?? "", 40, style);
        if (edited != nameEdit) { nameEdit = edited; nameEditing = true; }
        if (GUI.GetNameOfFocusedControl() == "codename") nameEditing = true;
        bool changed = nameEditing && Codename.Sanitize(nameEdit) != Codename.Current;
        if (UiKit.Button(new Rect(x + LabelW + fw + 8f, y + 7f, 112f, RowH - 14f), "保存", 18f, changed, true, changed) && interactive && changed)
        {
            nameEdit = Codename.Set(nameEdit);
            nameEditing = false;
            GUI.FocusControl(null);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        }
        y += RowH;
        y = Note(x, y, w, $"ランキングとマルチで表示する名前(最大{Codename.MaxLength}文字)。未設定でも遊べます。");
        return Note(x, y, w, "本名などの個人情報は入れないでください。");
    }

    // データ引き継ぎ(2026-10-14): 機種変更に備えた手動の書き出し/読み込み(SaveTransfer)。ホームでだけ
    float MeasureTransfer() => 16f + HeadH + 4f + RowH * 2f + 22f * 4f;
    float DrawTransfer(float x, float y, float w, bool interactive)
    {
        y = Head(x, y + 16f, w, "データ引き継ぎ");
        bool can = SaveTransfer.CanRunNow(out string why);
        RowLabel(x, y, "書き出し");
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "ファイルに書き出す", 19f, false, true, can) && interactive && can) SaveTransferUi.StartExport();
        y += RowH;
        RowLabel(x, y, "読み込み");
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "ファイルから読み込む", 19f, false, true, can) && interactive && can) SaveTransferUi.StartImport();
        y += RowH;
        if (!can) y = Note(x, y, w, why);
        y = Note(x, y, w, "MILE・カード・デッキ・解放・記録をファイルにして、別の端末へ移せます。読み込むと今の進行はファイルの内容に置き換わります。");
        y = Note(x, y, w, "端末の故障に備えるには、書き出したファイルを別の端末やパソコン、外部ストレージなどに保管してください。");
        return Note(x, y, w, "設定・中断中のラン・広告なしパス(ストアで復元)・ランキングの登録は引き継がれません。");
    }

    // 広告なしパス/購入の復元/プライバシー(2026-10-10、依頼I)。Steam/PC(広告も課金も無い)では出さない
    static bool PrivacyRow => Monetization.Ads != null && Monetization.Ads.PrivacyOptionsRequired;
    float MeasureNoAds() => Monetization.Mode == MonetizationMode.None ? 0f : 16f + HeadH + 4f + RowH * (PrivacyRow ? 3f : 2f) + 22f * 2f;
    float DrawNoAds(float x, float y, float w, bool interactive)
    {
        if (Monetization.Mode == MonetizationMode.None) return y;
        y = Head(x, y + 16f, w, "広告なしパス");
        RowLabel(x, y, "広告なしパス");
        bool owned = NoAdsPass.Owned;
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), owned ? "有効(内容を見る)" : "内容と購入", 19f, !owned, true) && interactive) MonetizationUi.OpenPass();
        y += RowH;
        RowLabel(x, y, "購入の復元");
        bool busy = NoAdsPass.Busy;
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), busy ? "確認中…" : "購入を復元", 19f, false, true, !busy) && interactive && !busy) NoAdsPass.Check("settings restore");
        y += RowH;
        if (PrivacyRow)
        {
            RowLabel(x, y, "広告のプライバシー");
            if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "プライバシーの設定", 19f, false, true) && interactive) Monetization.Ads.ShowPrivacyOptions();
            y += RowH;
        }
        y = Note(x, y, w, string.IsNullOrEmpty(NoAdsPass.LastNote) ? "パスはストアのアカウントに紐づきます(購入の復元でセーブデータは戻りません)" : NoAdsPass.LastNote);
        return y;
    }

    // データとプライバシー(2026-10-19、Google Play の公開準備): プライバシーポリシー(ブラウザで開く)/ オンラインのデータの削除
    bool policyTried;
    float MeasurePrivacy() => 16f + HeadH + 4f + RowH * 2f + 22f * (policyTried ? 3f : 1f);
    float DrawPrivacy(float x, float y, float w, bool interactive)
    {
        y = Head(x, y + 16f, w, "データとプライバシー");
        RowLabel(x, y, "プライバシーポリシー");
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "ブラウザで開く", 19f, false, true) && interactive) { PrivacyLinks.OpenPolicy(); policyTried = true; }
        y += RowH;
        RowLabel(x, y, "データの削除");
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "内容を確認する", 19f, false, true) && interactive) DataDeletionUi.Open();
        y += RowH;
        if (policyTried)
        {
            // ブラウザが開かない端末向け: URL を見せてコピーできるようにする
            y = Note(x, y, w, $"ページが開かない時は、次の URL をブラウザで開いてください: {PrivacyLinks.PolicyUrl}"); // 訳は Loc の型(…{0})で当たる
            if (UiKit.Button(new Rect(x + w - 160f, y, 160f, 34f), "URLをコピー", 15f, false, false) && interactive) PrivacyLinks.Copy(PrivacyLinks.PolicyUrl);
            y += 40f;
        }
        y = Note(x, y, w, AccountDeletion.InAppAvailable ? "ランキングの記録など、サーバー上のデータを削除できます(この端末のカードや進行は消えません)。" : LocPendingReview.DeletionNoteByMail); // 2026-10-26: production は未配置
        // 2026-10-26: 「ライセンス」の行は「このゲームについて」へ移した(同じ画面を開く。重複して作らない)
        return y;
    }

    // このゲームについて(2026-10-26 公開前の案内の整備): バージョン(ビルドの情報から)/ 制作クレジット / ライセンス / お問い合わせ
    float copiedUntil = -1f; string copiedWhat = "";
    float MeasureAbout() => 16f + HeadH + 4f + RowH * 4f + 22f * 4f + 44f;
    float DrawAbout(float x, float y, float w, bool interactive)
    {
        y = Head(x, y + 16f, w, LocPendingReview.AboutTitle);
        // バージョン(問い合わせの時に確かめやすいように、コピーもできる)
        RowLabel(x, y, LocPendingReview.VersionLabel);
        float cw = 120f;
        // 狭い列(横画面の2列)でも版番号まで見えるよう、入らない時は字を小さくする
        var vst = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleLeft, false, Color.white));
        float vw = w - LabelW - cw - 8f;
        while (vst.fontSize > 11 && vst.CalcSize(new GUIContent(AppInfo.VersionLabel)).x > vw) vst.fontSize--;
        GUI.Label(new Rect(x + LabelW, y, vw, RowH), AppInfo.VersionLabel, vst);
        if (UiKit.Button(new Rect(x + w - cw, y + 8f, cw, RowH - 16f), LocPendingReview.CopyButton, 16f, false, false) && interactive) { PrivacyLinks.Copy(AppInfo.VersionLabel); copiedWhat = "version"; copiedUntil = Time.unscaledTime + 2f; }
        y += RowH;
        if (copiedWhat == "version" && Time.unscaledTime < copiedUntil) y = Note(x, y, w, LocPendingReview.Copied);
        // 制作クレジット / ライセンス(同じ画面を2つの内容で使う)
        RowLabel(x, y, LocPendingReview.CreditsTitle);
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), LocPendingReview.LicensesOpen, 19f, false, true) && interactive) LicensesUi.OpenCredits();
        y += RowH;
        // 2026-10-23: 使っている素材の著作権表示とライセンス(MS Basic など、原文のまま)
        RowLabel(x, y, LocPendingReview.LicensesTitle);
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), LocPendingReview.LicensesOpen, 19f, false, true) && interactive) LicensesUi.Open();
        y += RowH;
        // お問い合わせ(メールアプリを開く。件名・バージョン・利用環境は下書きに入る)
        RowLabel(x, y, LocPendingReview.ContactTitle);
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), LocPendingReview.ContactOpen, 19f, false, true) && interactive) AppInfo.OpenContactMail();
        y += RowH;
        y = Note(x, y, w, LocPendingReview.ContactHint);
        y = Note(x, y, w, LocPendingReview.ContactNote.Replace("{0}", PrivacyLinks.ContactEmail)); // 訳は Loc の型(…{0})で当たる
        if (UiKit.Button(new Rect(x + w - 200f, y + 2f, 200f, 34f), LocPendingReview.CopyAddress, 15f, false, false) && interactive) { PrivacyLinks.Copy(PrivacyLinks.ContactEmail); copiedWhat = "mail"; copiedUntil = Time.unscaledTime + 2f; }
        y += 40f;
        if (copiedWhat == "mail" && Time.unscaledTime < copiedUntil) y = Note(x, y, w, LocPendingReview.Copied);
        // 著作権の表記(ゲーム自身の ©。素材のライセンスとは別)
        GUI.Label(new Rect(x, y + 4f, w, 24f), AppInfo.Copyright, UiKit.Label(14f, TextAnchor.MiddleLeft, false, new Color(0.75f, 0.8f, 0.9f)));
        return y + 28f;
    }

    // 遊び方(2026-10-07): 操作の練習をもう一度。ホームでだけ始められる(終わるとホームへ戻る)
    float DrawHowToPlay(float x, float y, float w, bool interactive)
    {
        RowLabel(x, y, "遊び方");
        bool can = TutorialMode.CanLaunchFromHome;
        if (UiKit.Button(new Rect(x + LabelW, y + 6f, w - LabelW, RowH - 12f), "操作を練習する", 19f, can, true, can) && interactive && can)
        {
            Close();
            TutorialLauncher.Launch(false, "settings");
        }
        y += RowH;
        return Note(x, y, w, can ? "前後攻撃・ジャンプ・下攻撃・打ち上げ・カード選択を練習できます(報酬・記録なし)。" : "練習はホームでのみ始められます(マルチ接続中は不可)");
    }
}

using UnityEngine;

// 設定画面(2026-10-01)。ホーム/各メニュー/ポーズ/リザルトから開く共通のパネル(IMGUI、ほかのUIより手前)。
//  ・全画面は切り替えず、背景を薄く暗くしてパネルが少し浮き上がる(0.18秒)。閉じる時は短く消える(0.12秒)。
//    時間は実時間(ポーズ中=timeScale 0でも開閉の演出が止まらない)。
//  ・開いている間は背後へタップを通さない: IMGUIは最後に全画面の透明ボタンで吸い取り、uGUIのメニュー画面は UiInputGate を見る。
//  ・値はそれぞれの持ち主に直接反映・保存する(音量=AudioManager、向き=GameManager、補助=HighSpeedAssist、
//    揺れ/発光=GameSettings)。スライダーは動かしている間に反映し、離した時に保存する。
//  ・時間は止めない(マルチプレイ中に開いても共有世界を止めない。ポーズからはポーズのまま開く)。
//  ・縦長の画面では1列、横長では2列。収まらない時は内容部分だけ指でスクロールできる。
public class SettingsPanel : MonoBehaviour
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

    public void Open()
    {
        if (state == St.Open || state == St.Opening) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        state = St.Opening; t = 0f; scroll = Vector2.zero; dragging = false; nameEditing = false;
        PlaySe(true);
    }

    public void Close()
    {
        if (state == St.Closed || state == St.Closing) return;
        state = St.Closing; t = 0f;
        GameSettings.Save();
        UiInputGate.LatchUntilRelease();
        PlaySe(false);
    }

    void ForceClose() { state = St.Closed; t = 0f; dragging = false; }

    static void PlaySe(bool open)
    {
        var am = AudioManager.Instance;
        if (am != null) am.PlaySe(open ? SeId.UiOpen : SeId.UiClose); // 2026-10-06: 開く/閉じる
    }

    void Update()
    {
        if (state == St.Closed) return;
        t += Time.unscaledDeltaTime;
        if (state == St.Opening && t >= OpenTime) { state = St.Open; t = 0f; }
        else if (state == St.Closing && t >= CloseTime) { state = St.Closed; t = 0f; }
    }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    // メニュー画面(デッキ編集/カード合成/キャラ選択/ステージ選択)の設定ボタン: どの画面も同じ右上の角(戻る/所持MILEと重ならない)。
    public static Rect MenuGearRect()
    {
        float size = Mathf.Clamp(Screen.height * 0.085f, 48f, 110f);
        Rect safe = Screen.safeArea;
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
        Rect safe = Screen.safeArea;
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
        LocGUI.Label(new Rect(panel.x + 28f, panel.y + 14f, panel.width - 120f, 44f), "設定", UiKit.Label(28f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
        bool interactive = state == St.Open || state == St.Opening;
        if (UiKit.Button(new Rect(panel.xMax - 70f, panel.y + 14f, 50f, 46f), "×", 26f, false, false) && interactive) Close();

        // 内容(スクロール部分)
        var view = new Rect(panel.x + 20f, panel.y + 70f, panel.width - 40f, panel.height - 70f - 76f);
        float contentH;
        if (wide)
        {
            float colW = (view.width - 30f) * 0.5f;
            contentH = Mathf.Max(drawnH, Mathf.Max(MeasureAudio() + 16f + MeasureLanguage() + 16f + MeasureProfile(), MeasureDisplay() + MeasureControls() + 16f));
            BeginScroll(view, contentH);
            float ya = DrawAudio(0f, 0f, colW, interactive);
            ya = DrawLanguage(0f, ya + 16f, colW, interactive);
            ya = DrawProfile(0f, ya + 16f, colW, interactive);
            float y2 = DrawDisplay(colW + 30f, 0f, colW, interactive);
            y2 = DrawControls(colW + 30f, y2 + 16f, colW, interactive);
            EndScroll(view, contentH);
            if (Event.current.type == EventType.Repaint) drawnH = Mathf.Max(ya, y2) + 12f;
        }
        else
        {
            contentH = Mathf.Max(drawnH, MeasureAudio() + MeasureLanguage() + MeasureDisplay() + MeasureControls() + MeasureProfile() + 64f);
            BeginScroll(view, contentH);
            float y = DrawAudio(0f, 0f, view.width, interactive);
            y = DrawLanguage(0f, y + 16f, view.width, interactive);
            y = DrawDisplay(0f, y + 16f, view.width, interactive);
            y = DrawControls(0f, y + 16f, view.width, interactive);
            y = DrawProfile(0f, y + 16f, view.width, interactive);
            EndScroll(view, contentH);
            if (Event.current.type == EventType.Repaint) drawnH = y + 12f;
        }

        if (UiKit.Button(new Rect(panel.center.x - 110f, panel.yMax - 66f, 220f, 52f), "閉じる", 22f, true) && interactive) Close();

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
            else if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition)) { scroll.y = Mathf.Clamp(scroll.y + e.delta.y * 20f, 0f, max); e.Use(); }
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

    void RowLabel(float x, float y, string text) => LocGUI.Label(new Rect(x, y, LabelW, RowH), text, UiKit.Label(20f));

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
        var st = UiKit.Label(15f, TextAnchor.UpperLeft, false, new Color(0.75f, 0.8f, 0.9f));
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
    const int LangCols = 3; const float LangRowH = 44f;
    float MeasureLanguage() => HeadH + 4f + Mathf.Ceil(Loc.Languages.Length / (float)LangCols) * LangRowH + 26f;
    float DrawLanguage(float x, float y, float w, bool interactive)
    {
        y = Head(x, y, w, Loc.IsJapanese ? "言語 / Language" : Loc.T("言語") + " / Language");
        float cw = (w - (LangCols - 1) * 6f) / LangCols;
        for (int i = 0; i < Loc.Languages.Length; i++)
        {
            var l = Loc.Languages[i];
            var r = new Rect(x + (i % LangCols) * (cw + 6f), y + (i / LangCols) * LangRowH, cw, LangRowH - 6f);
            bool sel = Loc.Current == l.code;
            if (UiKit.Button(r, Loc.NativeName(l), 16f, sel, false) && interactive && !sel) { Loc.Set(l.code); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle); }
        }
        y += Mathf.Ceil(Loc.Languages.Length / (float)LangCols) * LangRowH;
        return Note(x, y, w, Loc.UserChose ? Loc.T("選んだ言語で表示します") : Loc.T("端末の言語に合わせています"));
    }

    // ---------------------------------------------------------------- 表示
    float MeasureDisplay() => HeadH + 4f + RowH * 3f + 22f;
    float DrawDisplay(float x, float y, float w, bool interactive)
    {
        var gm = GameManager.Instance;
        y = Head(x, y, w, "表示");
        if (gm != null) y = ChoiceRow(x, y, w, "画面の向き", gm.PreferPortrait ? 1 : 0, "横画面", "縦画面", interactive, c => gm.SetPreferredOrientation(c == 1));
        else y += RowH;
        y = ChoiceRow(x, y, w, "画面揺れ", GameSettings.ScreenShake ? 1 : 0, "OFF", "ON", interactive, c => GameSettings.SetScreenShake(c == 1));
        y = SliderRow(x, y, w, "発光演出", GameSettings.GlowIntensity, Pct(GameSettings.GlowIntensity), true, interactive, (v, rel) => GameSettings.SetGlowIntensity(v, rel));
        return Note(x, y, w, "※敵の攻撃予兆や操作に必要な表示は弱くなりません");
    }

    // ---------------------------------------------------------------- 操作(2026-10-08: オートの個別設定)
    // 全体の ON/OFF と開始の速さ(今までの「補助開始速度」。距離ではなく走る速さで始まる)は今までどおり。
    // ボス戦でもオート / 自動攻撃 / 自動回避 を個別に切り替える。説明は実際に対応している範囲だけ(必ず避けられるとは書かない)
    float MeasureControls() => HeadH + 4f + RowH * 5f + 22f * 6f + 12f + RowH + 22f;
    float DrawControls(float x, float y, float w, bool interactive)
    {
        var hsa = HighSpeedAssist.Instance;
        y = Head(x, y, w, "操作");
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
        y = Note(x, y, w, "走る速さが開始速度を超えると、ONの操作だけ自動で行います。");
        y = Note(x, y, w, "自動攻撃: 前の敵・壊せる障害物・ボスへの攻撃。");
        y = Note(x, y, w, "自動回避: 穴・障害物・壁・トゲへのジャンプ。ボス戦では弾や攻撃範囲も。");
        y = Note(x, y, w, "通常の敵の攻撃は避けません。必ず避けられるわけではありません。");
        y = Note(x, y, w, "自分の操作が優先。ボス戦OFFならボス戦の間は自動操作しません。");
        y = Note(x, y, w, "※ ボス戦でもオートは、ボスと戦っている間は速さに関係なく働きます。");
        return DrawHowToPlay(x, y + 12f, w, interactive);
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
        return Note(x, y, w, can ? "前後の攻撃/ジャンプ/下攻撃/打ち上げ/カードを練習(報酬なし)" : "練習はホームで始められます(ラン中/マルチ中は不可)");
    }
}

using UnityEngine;

// 設定画面(2026-10-01)の「表示」の設定のうち、ほかに置き場所の無いもの(音量はAudioManager、画面の向きはGameManager、
// 高速時の自動補助はHighSpeedAssistがそれぞれ持っている値を使う=二重に持たない)。
//  ・ScreenShake: 画面揺れ(CameraFollow.Shake)のON/OFF
//  ・GlowIntensity: 発光演出(攻撃の命中の光/残像、高速時の流線、BONUS ZONEの画面端の光など)の強さ 0〜1。
//    敵の攻撃予兆(警告表示)や操作に必要な表示には掛けない。
public static class GameSettings
{
    const string ShakeKey = "ScreenShakeEnabled";
    const string GlowKey = "GlowIntensity";
    const string ObstacleWarnKey = "ObstacleWarnMode"; // 2026-10-11: 障害物の警告 1 = 点滅あり / 2 = 輪郭のみ(点滅なし)
    public const string AuraKey = "AuraIntensityV1";   // 2026-10-14: 識別用オーラ(マルチ/CPU サポート)の濃さ 0〜1。0 は名前と番号だけ
    // 2026-10-24: 最高速度(自然に加速した時の上限)の倍率 %。60〜120、5刻み。値が無い(既存のセーブ)= 100(今までと同じ)。
    // 難易度ではなく「遊びやすい速さの好み」。キャラごとの速さの違い・速さのカード・一時的な加速には掛けない(PlayerController.NaturalMultiplierAt)
    public const string MaxSpeedKey = "MaxSpeedPercentV1";
    public const int MaxSpeedMin = 60, MaxSpeedMax = 120, MaxSpeedStep = 5, MaxSpeedDefault = 100;
    static int maxSpeed = MaxSpeedDefault;
    static float aura = 0.6f;
    static bool loaded;
    static bool shake = true;
    static float glow = 1f;
    static int obstacleWarn = 1;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        shake = SaveStore.GetInt(ShakeKey, 1) != 0;
        glow = Mathf.Clamp01(SaveStore.GetFloat(GlowKey, 1f));
        obstacleWarn = Mathf.Clamp(SaveStore.GetInt(ObstacleWarnKey, 1), 1, 2);
        aura = Mathf.Clamp01(SaveStore.GetFloat(AuraKey, 0.6f));
        maxSpeed = SnapMaxSpeed(SaveStore.GetInt(MaxSpeedKey, MaxSpeedDefault));
    }

    // 5% 刻みへ丸めて 60〜120 に収める(保存の値が壊れていても / タッチでも中途半端な値にしない)
    public static int SnapMaxSpeed(int pct) => Mathf.Clamp(Mathf.RoundToInt(pct / (float)MaxSpeedStep) * MaxSpeedStep, MaxSpeedMin, MaxSpeedMax);
    public static int MaxSpeedPercent { get { Load(); return maxSpeed; } }
    public static void SetMaxSpeedPercent(int pct)
    {
        Load();
        maxSpeed = SnapMaxSpeed(pct);
        SaveStore.SetInt(MaxSpeedKey, maxSpeed);
        SaveStore.Save();
    }

    public static void Reload() { loaded = false; } // セーブの初期化の後に読み直す(2026-10-01)

    public static bool ScreenShake { get { Load(); return shake; } }
    public static float GlowIntensity { get { Load(); return glow; } }
    public static int ObstacleWarn { get { Load(); return obstacleWarn; } }
    public static float AuraIntensity { get { Load(); return aura; } }
    public static void SetAuraIntensity(float v, bool save = true)
    {
        Load();
        aura = Mathf.Clamp01(v);
        SaveStore.SetFloat(AuraKey, aura);
        if (save) SaveStore.Save();
    }

    public static void SetObstacleWarn(int mode)
    {
        Load();
        obstacleWarn = Mathf.Clamp(mode, 1, 2);
        SaveStore.SetInt(ObstacleWarnKey, obstacleWarn);
        SaveStore.Save();
    }

    public static void SetScreenShake(bool on)
    {
        Load();
        shake = on;
        SaveStore.SetInt(ShakeKey, on ? 1 : 0);
        SaveStore.Save();
    }

    // save=false: スライダーを動かしている間(離した時に Save)
    public static void SetGlowIntensity(float v, bool save = true)
    {
        Load();
        glow = Mathf.Clamp01(v);
        SaveStore.SetFloat(GlowKey, glow);
        if (save) SaveStore.Save();
    }

    public static void Save() => SaveStore.Save();
}

// 設定/DEBUGパネルが開いている間(と、閉じた時に押していた指が離れるまで)、背後の画面の入力を止める。
// uGUIのメニュー画面(デッキ編集/カード合成/キャラ選択/ステージ選択)は自前でタップを判定しているので、それぞれがここを見る。
// IMGUIのホーム/ポーズ等は、パネルが一番手前で全画面のクリックを吸い取るので届かない。
public static class UiInputGate
{
    public static bool DebugPanelOpen;
    static bool latch;
    public static bool SprintPanelOpen; // 疾走出発の行き先パネル(2026-10-05)
    public static bool SupportPanelOpen; // CPU サポートの仲間選び(2026-10-14)
    public static bool ModalOpen => SettingsPanel.IsVisible || DebugPanelOpen || SprintPanelOpen || SupportPanelOpen || SaveTransferUi.IsOpen || TutorialRun.PanelOpen || FirstRunGuide.Open || NoticeQueue.Open || RankingPanel.IsOpen || AdManager.Showing || MonetizationUi.Open || DataDeletionUi.IsOpen || LoadingScreen.BlocksInput || BootSplash.BlocksInput; // 練習の説明/初回の案内(2026-10-07)、広告の表示中/広告なしパスの画面(2026-10-10)、起動ロゴ(2026-10-24)

    // パネルを閉じた: 今押している指が離れるまで背後へ通さない
    public static void LatchUntilRelease() { latch = true; }
    public static void ClearLatch() { latch = false; } // GameOver の結果画面へ持ち越さない(2026-10-06)
    public static bool DebugLatched => latch; // 確認用(2026-10-15)

    public static bool Blocked
    {
        get
        {
            if (latch && Input.touchCount == 0 && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) latch = false;
            return ModalOpen || latch;
        }
    }
}

// 新しいIMGUIパネル共通の見た目(既存のHUDと同じ濃紺+金枠)。
public static class UiKit
{
    static GUIStyle label, centered;
    static Texture2D white;
    public static Texture2D White
    {
        get
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; }
            return white;
        }
    }

    public static GUIStyle Label(float size, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, Color? color = null)
    {
        if (label == null) label = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip };
        label.fontSize = Mathf.RoundToInt(size);
        label.alignment = anchor;
        // 2026-10-09: 使い回しの1つの GUIStyle。呼んだ側が折り返し等を変えても、次の呼び出しでは既定へ戻す
        //  (以前は wordWrap = true が残り、後の文まで折り返されていた)。同時に複数の見た目が要る時は new GUIStyle(...) で複製する
        label.wordWrap = false; label.clipping = TextClipping.Clip;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.normal.textColor = color ?? Color.white;
        return label;
    }

    // 既存の DrawStyledButton と同じ見た目(濃紺/金枠)。押されたら true。
    // 2026-10-09(依頼G-2): 状態の見分け 通常 / 選択中(primary: 金の縁+淡い金) / 押している間(暗く+文字が少し沈む) / 使用不可(薄く+灰の文字)
    public static bool Button(Rect r, string text, float size, bool primary = false, bool ornate = true, bool enabled = true)
        => ButtonCore(r, text, size, primary, ornate, enabled, primary && !ornate, 0f);

    // ボタンの本体(2026-10-19: GameManager.DrawStyledButton もここを通す。以前は2つのほぼ同じ描き方で、文字の金/板の濃さが少しずつ違った)
    //  showSelected: 選択中の上塗り(金の太い縁)を出すか / flashAlpha: 一瞬光らせる(押せるようになった時など)
    public static bool ButtonCore(Rect r, string text, float size, bool primary, bool ornate, bool enabled, bool showSelected, float flashAlpha)
    {
        Color keep = GUI.color;
        if (!enabled) GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * 0.45f);
        if (ornate) OrnateUi.DrawPanel(r, primary ? 0.9f : 0.7f);
        else UiBackdrop.Draw(r, primary ? 0.85f : 0.6f);
        bool pressed = ButtonStateOverlay(r, showSelected, enabled);
        if (centered == null) centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        centered.fontSize = Mathf.RoundToInt(size);
        centered.normal.textColor = !enabled ? UiTheme.Disabled : primary ? UiTheme.GoldText : UiTheme.Text;
        LocGUI.Label(pressed ? new Rect(r.x, r.y + 2f, r.width, r.height) : r, text, centered);
        if (flashAlpha > 0.001f) Fill(r, new Color(1f, 0.95f, 0.8f, flashAlpha * 0.5f));
        GUI.color = keep;
        bool pad = enabled && PadNav.Button(r); // ゲームパッド/キーボードの決定(2026-10-06)
        return (GUI.Button(r, GUIContent.none, GUIStyle.none) && enabled) || pad;
    }

    // GUILayout.Button 用の見た目(2026-10-09、依頼G-2): 灰色の標準ボタンの代わりに 濃紺+金の縁。押している間は暗く、上に乗せると金の文字
    static Texture2D btnNormal, btnHover, btnActive;
    static Texture2D FramedTex(Color fill, Color edge)
    {
        const int n = 12, e = 2;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            t.SetPixel(x, y, x < e || y < e || x >= n - e || y >= n - e ? edge : fill);
        t.Apply();
        return t;
    }
    // 文字の入力欄(2026-10-22 画面側の完成度向上): Unity 既定の白い欄ではなく、ボタンと同じ「濃紺 + 金の縁」。入力中は縁が明るい金
    static Texture2D fieldNormal, fieldFocus;
    static GUIStyle fieldStyle;
    public static string TextField(Rect r, string value, float size, int maxLength = 64)
    {
        Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        if (fieldNormal == null) fieldNormal = FramedTex(A(UiTheme.NavyDeep, 0.96f), UiTheme.GoldEdge);
        if (fieldFocus == null) fieldFocus = FramedTex(A(UiTheme.ButtonFill, 0.98f), UiTheme.GoldHot);
        if (fieldStyle == null)
        {
            fieldStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft, border = new RectOffset(3, 3, 3, 3), padding = new RectOffset(10, 10, 3, 3), clipping = TextClipping.Clip };
            fieldStyle.normal.background = fieldStyle.hover.background = fieldNormal;
            fieldStyle.focused.background = fieldStyle.onFocused.background = fieldFocus;
            fieldStyle.normal.textColor = fieldStyle.hover.textColor = fieldStyle.focused.textColor = fieldStyle.onFocused.textColor = UiTheme.Text;
        }
        fieldStyle.fontSize = Mathf.RoundToInt(size);
        return GUI.TextField(r, value ?? "", maxLength, fieldStyle);
    }

    // 一覧の行の板(2026-10-22): Unity 既定の灰色の箱(GUI.skin.box)の代わり。濃紺 + 薄い金の縁
    static GUIStyle rowBox;
    public static GUIStyle RowBox
    {
        get
        {
            if (rowBox == null)
            {
                rowBox = new GUIStyle(GUI.skin.box) { border = new RectOffset(3, 3, 3, 3), padding = new RectOffset(10, 10, 8, 8) };
                rowBox.normal.background = FramedTex(new Color(UiTheme.Navy.r, UiTheme.Navy.g, UiTheme.Navy.b, 0.92f), new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.6f));
            }
            return rowBox;
        }
    }

    public static GUIStyle MakeButtonStyle(int fontSize)
    {
        // 2026-10-22: 色は UiTheme に(値は同じ)
        Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        if (btnNormal == null) btnNormal = FramedTex(A(UiTheme.ButtonFill, 0.96f), UiTheme.GoldEdge);
        if (btnHover == null) btnHover = FramedTex(A(UiTheme.ButtonHoverFill, 0.98f), UiTheme.GoldHot);
        if (btnActive == null) btnActive = FramedTex(A(UiTheme.NavyDeep, 0.98f), UiTheme.GoldHot);
        var st = new GUIStyle(GUI.skin.button) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, border = new RectOffset(3, 3, 3, 3) };
        st.normal.background = btnNormal; st.normal.textColor = UiTheme.TextButton;
        st.hover.background = btnHover; st.hover.textColor = UiTheme.GoldHover;
        st.focused.background = btnHover; st.focused.textColor = UiTheme.GoldHover;
        st.active.background = btnActive; st.active.textColor = UiTheme.GoldPressed;
        st.onNormal.background = btnHover; st.onNormal.textColor = UiTheme.GoldHover;
        return st;
    }

    // ボタンの状態の上塗り(UiKit.Button / GameManager.DrawStyledButton 共通)。押している間なら true(文字を沈める)
    //  selected: 選択中の選択肢(ON/OFF、横画面/縦画面 など)。金の縁を太く+淡い金を敷いて、選ばれていない方とはっきり分ける
    public static bool ButtonStateOverlay(Rect r, bool selected, bool enabled)
    {
        var e = Event.current;
        if (e == null || e.type != EventType.Repaint) return false;
        if (selected)
        {
            Fill(r, UiTheme.SelectedTint);
            Color g = new Color(UiTheme.GoldBright.r, UiTheme.GoldBright.g, UiTheme.GoldBright.b, 0.95f); float t = 3f;
            Fill(new Rect(r.x, r.y, r.width, t), g); Fill(new Rect(r.x, r.yMax - t, r.width, t), g);
            Fill(new Rect(r.x, r.y, t, r.height), g); Fill(new Rect(r.xMax - t, r.y, t, r.height), g);
        }
        bool pressed = enabled && GUI.enabled && Input.GetMouseButton(0) && r.Contains(e.mousePosition) && !UiInputGate.Blocked;
        // (選択中の金は UiTheme.GoldBright。上の値はそれと同じ)
        if (pressed) Fill(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), new Color(0f, 0f, 0.02f, 0.32f));
        return pressed;
    }

    public static void Fill(Rect r, Color c)
    {
        Color keep = GUI.color;
        GUI.color = new Color(c.r, c.g, c.b, c.a * keep.a);
        GUI.DrawTexture(r, White);
        GUI.color = keep;
    }

    // スマホでも掴みやすい太いスライダー(0..1)。掴んでいる間は dragging=true。
    public static float Slider(Rect r, float value, bool enabled, out bool released)
    {
        released = false;
        int id = GUIUtility.GetControlID(FocusType.Passive, r);
        Event e = Event.current;
        float trackH = Mathf.Max(6f, r.height * 0.22f);
        var track = new Rect(r.x, r.center.y - trackH * 0.5f, r.width, trackH);
        float knob = r.height * 0.8f;
        float usable = r.width - knob;
        float x = r.x + knob * 0.5f + usable * Mathf.Clamp01(value);
        Color A(Color c, float a) => new Color(c.r, c.g, c.b, a); // 2026-10-22: 色は UiTheme に(値は同じ)
        Fill(track, A(UiTheme.SliderTrack, 0.95f));
        Fill(new Rect(track.x, track.y, x - track.x, track.height), enabled ? A(UiTheme.SliderFill, 0.95f) : A(UiTheme.Locked, 0.9f));
        var k = new Rect(x - knob * 0.5f, r.center.y - knob * 0.5f, knob, knob);
        Fill(k, enabled ? UiTheme.SliderKnob : new Color(0.6f, 0.6f, 0.65f, 1f));
        Fill(new Rect(k.x + 3f, k.y + 3f, k.width - 6f, k.height - 6f), UiTheme.SliderKnobInner);
        if (!enabled) return value;
        // ゲームパッド/キーボード: フォーカス中は左右で 5% ずつ(2026-10-06)
        PadNav.Button(r, slider: true);
        float nudge = PadNav.SliderNudge(r);
        if (nudge != 0f) { value = Mathf.Clamp01(value + nudge * 0.05f); GUI.changed = true; released = true; }
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; value = ValueAt(e.mousePosition.x, r, knob); GUI.changed = true; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { value = ValueAt(e.mousePosition.x, r, knob); GUI.changed = true; e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; released = true; e.Use(); }
                break;
        }
        return value;
    }

    static float ValueAt(float mx, Rect r, float knob) => Mathf.Clamp01((mx - r.x - knob * 0.5f) / Mathf.Max(1f, r.width - knob));

    // 歯車のアイコン(フォントに⚙が無い端末があるため、実行時に描いた画像)。rの中央に scale 倍で描く。
    static Texture2D gear;
    public static void DrawGear(Rect r, float scale, Color c)
    {
        // 2026-10-20: 正式な絵(金の歯車、UiIcons.Gear)があればそれ。絵が金色なので、色は明るさと透明度だけ使う(無効の暗い色の時は暗く)
        if (UiIcons.Gear != null)
        {
            float gs = Mathf.Min(r.width, r.height) * scale * 1.08f;
            float v = Mathf.Min(1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * 1.15f);
            Color k0 = GUI.color;
            GUI.color = new Color(v, v, v, c.a * k0.a);
            UiIcons.Draw(new Rect(r.center.x - gs * 0.5f, r.center.y - gs * 0.5f, gs, gs), UiIcons.Gear);
            GUI.color = k0;
            return;
        }
        if (gear == null)
        {
            const int n = 64;
            gear = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
                float ang = Mathf.Atan2(dy, dx);
                float tooth = Mathf.Cos(ang * 8f) > 0.3f ? 1f : 0f;
                float outer = Mathf.Lerp(0.72f, 0.95f, tooth);
                float a = Mathf.Clamp01((outer - d) * 24f) * Mathf.Clamp01((d - 0.3f) * 24f);
                gear.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            gear.Apply();
        }
        float s = Mathf.Min(r.width, r.height) * scale;
        Color keep = GUI.color;
        GUI.color = new Color(c.r, c.g, c.b, c.a * keep.a);
        GUI.DrawTexture(new Rect(r.center.x - s * 0.5f, r.center.y - s * 0.5f, s, s), gear);
        GUI.color = keep;
    }

    // ON/OFF などの2択スイッチ(左右の2ボタン)。選択中を金色に。
    // 3つ以上の選択(2026-10-09)
    public static int Choice(Rect r, int current, string[] options, float size)
    {
        int n = Mathf.Max(1, options.Length);
        float gap = 6f, w = (r.width - gap * (n - 1)) / n;
        int result = current;
        for (int i = 0; i < n; i++)
            if (Button(new Rect(r.x + i * (w + gap), r.y, w, r.height), options[i], size, current == i, false)) result = i;
        return result;
    }

    public static int Choice(Rect r, int current, string a, string b, float size)
    {
        float half = (r.width - 6f) * 0.5f;
        int result = current;
        if (Button(new Rect(r.x, r.y, half, r.height), a, size, current == 0, false)) result = 0;
        if (Button(new Rect(r.x + half + 6f, r.y, half, r.height), b, size, current == 1, false)) result = 1;
        return result;
    }
}

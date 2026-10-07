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
    static bool loaded;
    static bool shake = true;
    static float glow = 1f;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        shake = SaveStore.GetInt(ShakeKey, 1) != 0;
        glow = Mathf.Clamp01(SaveStore.GetFloat(GlowKey, 1f));
    }

    public static void Reload() { loaded = false; } // セーブの初期化の後に読み直す(2026-10-01)

    public static bool ScreenShake { get { Load(); return shake; } }
    public static float GlowIntensity { get { Load(); return glow; } }

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
    public static bool ModalOpen => SettingsPanel.IsVisible || DebugPanelOpen || SprintPanelOpen || TutorialRun.PanelOpen || FirstRunGuide.Open || NoticeQueue.Open; // 練習の説明/初回の案内(2026-10-07)

    // パネルを閉じた: 今押している指が離れるまで背後へ通さない
    public static void LatchUntilRelease() { latch = true; }
    public static void ClearLatch() { latch = false; } // GameOver の結果画面へ持ち越さない(2026-10-06)

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
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.normal.textColor = color ?? Color.white;
        return label;
    }

    // 既存の DrawStyledButton と同じ見た目(濃紺/金枠)。押されたら true。
    public static bool Button(Rect r, string text, float size, bool primary = false, bool ornate = true, bool enabled = true)
    {
        Color keep = GUI.color;
        if (!enabled) GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * 0.45f);
        if (ornate) OrnateUi.DrawPanel(r, primary ? 0.9f : 0.7f);
        else UiBackdrop.Draw(r, primary ? 0.85f : 0.6f);
        if (centered == null) centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        centered.fontSize = Mathf.RoundToInt(size);
        centered.normal.textColor = primary ? new Color(1f, 0.9f, 0.6f) : Color.white;
        LocGUI.Label(r, text, centered);
        GUI.color = keep;
        bool pad = enabled && PadNav.Button(r); // ゲームパッド/キーボードの決定(2026-10-06)
        return (GUI.Button(r, GUIContent.none, GUIStyle.none) && enabled) || pad;
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
        Fill(track, new Color(0.07f, 0.09f, 0.18f, 0.95f));
        Fill(new Rect(track.x, track.y, x - track.x, track.height), enabled ? new Color(0.95f, 0.8f, 0.4f, 0.95f) : new Color(0.5f, 0.5f, 0.55f, 0.9f));
        var k = new Rect(x - knob * 0.5f, r.center.y - knob * 0.5f, knob, knob);
        Fill(k, enabled ? new Color(1f, 0.92f, 0.65f, 1f) : new Color(0.6f, 0.6f, 0.65f, 1f));
        Fill(new Rect(k.x + 3f, k.y + 3f, k.width - 6f, k.height - 6f), new Color(0.12f, 0.15f, 0.3f, 1f));
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
    public static int Choice(Rect r, int current, string a, string b, float size)
    {
        float half = (r.width - 6f) * 0.5f;
        int result = current;
        if (Button(new Rect(r.x, r.y, half, r.height), a, size, current == 0, false)) result = 0;
        if (Button(new Rect(r.x + half + 6f, r.y, half, r.height), b, size, current == 1, false)) result = 1;
        return result;
    }
}

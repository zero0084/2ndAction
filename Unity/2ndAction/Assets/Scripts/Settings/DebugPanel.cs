#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 開発版だけの DEBUG パネル(2026-10-01)。ホームの「DEBUG」ボタンから開く。リリース版ではこのファイルごと存在しない
// (ボタンも起動経路も無い)。以前ホームの右上/BESTの下に直接並んでいた開発用の操作をここへ集めた。
//  ・無敵 / DEBUGモード(ラン中のSPD/RUN/ASSIST/CARD TEST/DEBUG TOOLS列の表示) / GAMEFEEL検証(演出の誇張) / VIEW(斜め上視点の試作カメラ)
//  ・BESTの距離を設定(ガチャの段階の確認) / ガチャ候補のログ / MILE追加
//  ・スコアリセット(確認あり)
// プレイヤー向けの音/表示/操作は通常の設定画面(SettingsPanel)へ移した。
public partial class DebugPanel : MonoBehaviour
{
    public static DebugPanel Instance { get; private set; }
    bool open;
    float t;
    bool confirmReset;
    int page;            // 0=一般 / 1=セーブ・進行(2026-10-01)
    int confirmSave;     // 0=なし / 1=進行だけ初期化 / 2=完全初期化

    public static bool IsOpen => Instance != null && Instance.open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[DebugPanel]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DebugPanel>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.SetOpen(false); };
    }

    public static void OpenStatic() { if (Instance != null) Instance.SetOpen(true); }
    public static void CloseStatic() { if (Instance != null) Instance.SetOpen(false); }

    public void SetOpen(bool on)
    {
        if (on && ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (open && !on) UiInputGate.LatchUntilRelease();
        open = on; t = 0f; confirmReset = false; confirmSave = 0; if (on) page = 0;
        UiInputGate.DebugPanelOpen = on;
    }

    // Androidの戻る: 確認 → パネルの順に閉じる
    public bool Back()
    {
        if (!open) return false;
        if (confirmReset) confirmReset = false;
        else if (confirmSave != 0) confirmSave = 0;
        else if (page != 0) page = 0;
        else SetOpen(false);
        return true;
    }

    void Update() { if (open) t += Time.unscaledDeltaTime; }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!open) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        GUI.depth = -2100;
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        float k = Mathf.SmoothStep(0f, 1f, t / 0.15f);
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.05f, 0.02f, 0.02f, 0.5f * k));
        float pw = Mathf.Min(620f, w - 24f), ph = Mathf.Min(470f, h - 24f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f + (1f - k) * 12f, pw, ph);
        Color keepColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, k);
        OrnateUi.DrawPanel(p, 0.95f);
        GUI.Label(new Rect(p.x + 24f, p.y + 12f, 300f, 40f), "DEBUG(開発版のみ)", UiKit.Label(24f, TextAnchor.MiddleLeft, true, new Color(1f, 0.55f, 0.45f)));
        if (UiKit.Button(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), "×", 24f, false, false)) SetOpen(false);
        if (UiKit.Button(new Rect(p.xMax - 200f, p.y + 12f, 126f, 42f), page == 0 ? "セーブ…" : "← 一般", 17f, page == 1, false)) { page = page == 0 ? 1 : 0; confirmSave = 0; confirmReset = false; }
        if (page == 1)
        {
            DrawSavePage(p);
            GUI.color = keepColor;
            GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へ通さない
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
            GUI.matrix = keep;
            return;
        }

        float x = p.x + 24f, y = p.y + 64f, bw = (p.width - 48f - 12f) / 2f, bh = 46f;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"無敵: {(gm.InvincibleMode ? "ON" : "OFF")}", 18f, gm.InvincibleMode, false)) gm.DebugToggleInvincible();
        if (UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), $"DEBUGモード: {(gm.DebugMode ? "ON" : "OFF")}", 18f, gm.DebugMode, false)) gm.DebugToggleDebugMode();
        y += bh + 10f;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"GAMEFEEL検証: {(GameFeelDebug.VisibilityBoost ? "ON" : "OFF")}", 17f, GameFeelDebug.VisibilityBoost, false)) GameFeelDebug.VisibilityBoost = !GameFeelDebug.VisibilityBoost;
        var view = FindFirstObjectByType<ViewModeToggle>();
        if (view != null && UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), $"VIEW: {(view.PortraitActive ? "斜め上(試作)" : "通常")}", 17f, view.PortraitActive, false)) view.Toggle();
        y += bh + 6f;
        GUI.Label(new Rect(x, y, p.width - 48f, 24f), "GAMEFEEL検証=演出を誇張して確認する開発用(プレイヤー向けの強さは設定の「発光演出」)", UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(0.8f, 0.8f, 0.85f)));
        y += 28f;

        GUI.Label(new Rect(x, y, 300f, 26f), "BESTを設定(ガチャの段階の確認)", UiKit.Label(16f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 28f;
        float[] stops = { 0f, 5000f, 20000f, 50000f, 100000f };
        float sw = (p.width - 48f - 4f * 8f) / 5f;
        for (int i = 0; i < stops.Length; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), stops[i] >= 1000f ? $"{stops[i] / 1000f:0}K" : "0", 16f, false, false)) gm.DebugSetBestDistance(stops[i]);
        y += 48f;
        if (UiKit.Button(new Rect(x, y, bw, bh), "ガチャ候補をログへ", 17f, false, false)) gm.DebugLogGachaPoolPublic();
        if (UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), "MILE +5000", 17f, false, false)) gm.AddMile(5000);
        y += bh + 14f;

        if (!confirmReset)
        {
            if (UiKit.Button(new Rect(x, y, p.width - 48f, bh), "スコアをリセット…", 18f, false, false)) confirmReset = true;
        }
        else
        {
            // データを変える操作は確認してから
            GUI.Label(new Rect(x, y - 2f, p.width - 48f, 24f), "BEST記録(全ステージ)を消します。よろしいですか?", UiKit.Label(16f, TextAnchor.UpperLeft, true, new Color(1f, 0.6f, 0.5f)));
            if (UiKit.Button(new Rect(x, y + 24f, bw, 44f), "リセットする", 18f, true, false)) { gm.DebugResetHighScores(); confirmReset = false; }
            if (UiKit.Button(new Rect(x + bw + 12f, y + 24f, bw, 44f), "やめる", 18f, false, false)) confirmReset = false;
        }

        GUI.color = keepColor;
        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
    }
}

// DebugPanel: セーブ/進行のページ(2026-10-01)
public partial class DebugPanel
{
    string cardNote = "";
    void DrawSavePage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 62f, full = p.width - 48f, bh = 44f;
        var boot = SaveSystem.LastBoot;
        string info = $"セーブ形式 v{PlayerPrefs.GetInt(SaveKeys.SchemaVersion, 0)} / リリース世代 {PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, 0)}(このビルド {SaveSystem.BuildReleaseGeneration})"
            + (boot != null ? $" / 起動時: {boot.kind}" : "");
        GUI.Label(new Rect(x, y, full, 22f), info, UiKit.Label(14f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
        y += 26f;

        // 累計走行距離
        GUI.Label(new Rect(x, y, full, 24f), $"累計走行距離 {ProgressStats.LifetimeDistance:N0} m", UiKit.Label(16f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 26f;
        double[] stops = { 0, 999999, 1000000 };
        string[] names = { "0", "999,999", "1,000,000" };
        float sw = (full - 16f) / 3f;
        for (int i = 0; i < 3; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), names[i], 16f, false, false)) ProgressStats.DevSetLifetime(stops[i]);
        y += 48f;

        // 三姉妹の遭遇
        string[] jp = { "長女", "次女", "三女" };
        for (int i = 0; i < 3; i++)
        {
            var sister = ProgressStats.Sisters[i];
            bool met = ProgressStats.HasMet(sister);
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), $"{jp[i]}: {(met ? "遭遇済" : "未遭遇")}", 15f, met, false)) ProgressStats.DevSetMet(sister, !met);
        }
        y += 48f;

        // ラスダン
        float bw = (full - 8f) / 2f;
        bool unlocked = ProgressStats.FinalDungeonUnlocked;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"ラスダン解放: {(unlocked ? "ON" : "OFF")}", 16f, unlocked, false)) ProgressStats.DevSetFinalDungeonUnlocked(!unlocked);
        bool always = ProgressStats.DevAlwaysOpen;
        if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), $"ラスダン常に選択: {(always ? "ON" : "OFF")}", 15f, always, false)) ProgressStats.DevAlwaysOpen = !always;
        y += bh + 4f;
        GUI.Label(new Rect(x, y, full, 20f), "解放の条件: 累計1,000,000m + 三姉妹全員と遭遇(一度解放したら戻らない)。「常に選択」は開発版だけ", UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(0.75f, 0.75f, 0.8f)));
        y += 26f;

        // カード全開放(2026-10-02)
        float tw = (full - 16f) / 3f;
        bool open = GachaStage.DevAllCardsOpen;
        if (UiKit.Button(new Rect(x, y, tw, bh), $"全カード開放: {(open ? "ON" : "OFF")}", 15f, open, false))
        {
            GachaStage.DevAllCardsOpen = !open;
            if (!open) { int n = CardInventory.DebugOwnEveryMissing(); cardNote = $"全{CardDatabase.AllCards.Count}枚を解放、未所持{n}枚をLv1で追加"; }
            else cardNote = "全カード開放OFF(所持したカードはそのまま)";
        }
        if (UiKit.Button(new Rect(x + tw + 8f, y, tw, bh), "全カード Lv9×3枚", 15f, false, false))
        {
            int n = CardInventory.DebugOwnEveryAtLevel(9, 3);
            cardNote = $"全カードをLv9で3枚ずつ所持({n}種を追加)";
        }
        if (UiKit.Button(new Rect(x + (tw + 8f) * 2f, y, tw, bh), "全カード Lv1×1枚", 15f, false, false))
        {
            int n = CardInventory.DebugOwnEveryMissing();
            cardNote = $"未所持{n}枚をLv1で追加";
        }
        y += bh + 2f;
        GUI.Label(new Rect(x, y, full, 18f), string.IsNullOrEmpty(cardNote) ? "全カード開放=距離/ガチャ段階に関係なく全カードを解放済みにして、未所持のカードを1枚ずつ渡す(開発版のみ)" : cardNote,
            UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.85f, 0.5f)));
        y += 22f;

        // 初期化(確認あり)
        if (confirmSave == 0)
        {
            if (UiKit.Button(new Rect(x, y, bw, bh), "進行だけ初期化…", 16f, false, false)) confirmSave = 1;
            if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), "完全初期化…", 16f, false, false)) confirmSave = 2;
        }
        else
        {
            string msg = confirmSave == 1 ? "キャラ/カード/MILE/BEST/累計距離/遭遇/解放を消します(設定は残る)。よろしいですか?" : "設定も含めて全部消します(新規インストールと同じ)。よろしいですか?";
            GUI.Label(new Rect(x, y - 4f, full, 22f), msg, UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.6f, 0.5f)));
            if (UiKit.Button(new Rect(x, y + 18f, bw, 40f), "初期化する", 17f, true, false))
            {
                if (confirmSave == 1) SaveSystem.DevResetProgress(); else SaveSystem.DevResetAll();
                confirmSave = 0;
                SetOpen(false);
                // シーン上の読み込み済みの値(GameManager等)を読み直す
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            if (UiKit.Button(new Rect(x + bw + 8f, y + 18f, bw, 40f), "やめる", 17f, false, false)) confirmSave = 0;
        }
    }
}

// DebugPanel から使う GameManager の開発用操作の入口(開発版のみ)
public partial class GameManager
{
    public void DebugToggleInvincible() => ToggleInvincible();
    public void DebugToggleDebugMode() => ToggleDebugMode();
    public void DebugResetHighScores() => ResetHighScores();
    public void DebugLogGachaPoolPublic() => DebugLogGachaPool();
}
#endif

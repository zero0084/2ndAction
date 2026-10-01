#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 開発版だけの DEBUG パネル(2026-10-01)。ホームの「DEBUG」ボタンから開く。リリース版ではこのファイルごと存在しない
// (ボタンも起動経路も無い)。以前ホームの右上/BESTの下に直接並んでいた開発用の操作をここへ集めた。
//  ・無敵 / DEBUGモード(ラン中のSPD/RUN/ASSIST/CARD TEST/DEBUG TOOLS列の表示) / GAMEFEEL検証(演出の誇張) / VIEW(斜め上視点の試作カメラ)
//  ・BESTの距離を設定(ガチャの段階の確認) / ガチャ候補のログ / MILE追加
//  ・スコアリセット(確認あり)
// プレイヤー向けの音/表示/操作は通常の設定画面(SettingsPanel)へ移した。
public class DebugPanel : MonoBehaviour
{
    public static DebugPanel Instance { get; private set; }
    bool open;
    float t;
    bool confirmReset;

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
        open = on; t = 0f; confirmReset = false;
        UiInputGate.DebugPanelOpen = on;
    }

    // Androidの戻る: 確認 → パネルの順に閉じる
    public bool Back()
    {
        if (!open) return false;
        if (confirmReset) confirmReset = false; else SetOpen(false);
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

// DebugPanel から使う GameManager の開発用操作の入口(開発版のみ)
public partial class GameManager
{
    public void DebugToggleInvincible() => ToggleInvincible();
    public void DebugToggleDebugMode() => ToggleDebugMode();
    public void DebugResetHighScores() => ResetHighScores();
    public void DebugLogGachaPoolPublic() => DebugLogGachaPool();
}
#endif

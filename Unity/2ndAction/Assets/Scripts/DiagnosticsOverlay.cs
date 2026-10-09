using System.Text;
using UnityEngine;

// 診断ログの表示(2026-09-27 改修)。Debug Mode中のRunでだけ描く。
//  - 異常を検知しても自動では開かない。画面の右端に「ログ保存済み」を小さく短時間だけ出す
//    (ボタンではなくただの文字 - タップ/フリックを奪わない)。
//  - 左下の小さな「診断ログ」ボタンを押した時だけ詳細画面を開く。詳細画面の「閉じる」は
//    右上に大きく固定(スクロールしても動かない)、最前面(GUI.depthを最小)に描く。
//  - 「BOSS診断」ボタンでBug#001用の診断パネルを開閉する(以前は常時表示だった)。
//  - 詳細画面を開いている間だけ、プレイヤーの入力(フリック)を受け付けない(スクロール操作が
//    ジャンプ/攻撃にならないように)。ゲームの時間は止めない。
public class DiagnosticsOverlay : MonoBehaviour
{
    static DiagnosticsOverlay instance;
    public static bool DetailOpen { get; private set; }
    public static bool BossPanelOpen { get; private set; }
    int tab; // 0 = Freeze/Warp, 1 = Boss
    Vector2 scroll;
    GUIStyle small, text, header, button, bigButton, toast;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("[DiagnosticsOverlay]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DiagnosticsOverlay>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => Ensure();

    void Update()
    {
        DiagnosticsWriter.Pump();
        GameManager gm = GameManager.Instance;
        bool usable = gm != null && gm.DebugMode && gm.HasStarted;
        if (!usable) { DetailOpen = false; BossPanelOpen = false; }
        // Android の戻るボタン / Esc でも閉じられる。
        if (DetailOpen && Input.GetKeyDown(KeyCode.Escape)) DetailOpen = false;
    }

    // デバッグ表示の拡大率(他のデバッグUIと同じ考え方: 短辺720pxを基準)。
    static float Scale => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);

    // 初めて通知を出すフレームで、日本語の文字をフォントへ焼き込む処理/スレッドの準備が重なって
    // 数十msかかるのを避けるため、Debug ModeのRunが始まった時点で先に済ませておく。
    bool warmed;
    void Warm()
    {
        if (warmed) return;
        warmed = true;
        const string glyphs = "ログ保存済み: HITCH POSITION JUMP BOSS MANUAL 診断ログ(0123456789) BOSS診断 ▼ 閉じる 今すぐ記録 異常件詳細省略意図した移動開始復帰直後のヒッチ保存先最近新しい順最後に";
        foreach (var st in new[] { small, text, header, button, bigButton, toast })
        {
            Font f = st.font != null ? st.font : GUI.skin.font;
            if (f != null) f.RequestCharactersInTexture(glyphs, st.fontSize, st.fontStyle);
            st.CalcSize(new GUIContent(glyphs));
        }
        System.Threading.ThreadPool.QueueUserWorkItem(_ => { });
    }

    void EnsureStyles()
    {
        if (small != null) return;
        small = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
        button = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
        bigButton = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
        header.normal.textColor = new Color(1f, 0.8f, 0.45f);
        text = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, alignment = TextAnchor.UpperLeft };
        text.normal.textColor = Color.white;
        toast = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        toast.normal.textColor = new Color(1f, 0.85f, 0.4f);
    }

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.DebugMode || !gm.HasStarted) return;
        EnsureStyles();
        Warm();
        GUI.depth = -5000; // 他のどのデバッグ表示よりも手前
        float s = Scale;
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float left = safe.xMin / s + 8f, right = w - (Screen.width - safe.xMax) / s - 8f, bottom = h - safe.yMin / s - 8f;

        // ---- 左下: 小さなボタン(一時停止ボタン(右下)/HUD(上)と重ならない位置) ----
        int n = FreezeDiagnostics.AnomalyCount + BossDiagnostics.SnapshotCount;
        Rect logBtn = new Rect(left + 60f, bottom - 30f, 104f, 28f);
        Rect bossBtn = new Rect(logBtn.xMax + 6f, logBtn.y, 84f, 28f);
        if (!DetailOpen)
        {
            if (GUI.Button(logBtn, n > 0 ? $"診断ログ({n})" : "診断ログ", small)) { DetailOpen = true; tab = BossDiagnostics.SnapshotCount > 0 && FreezeDiagnostics.AnomalyCount == 0 ? 1 : 0; scroll = Vector2.zero; }
            if (GUI.Button(bossBtn, BossPanelOpen ? "BOSS診断 ▼" : "BOSS診断", small)) BossPanelOpen = !BossPanelOpen;
            if (BossPanelOpen) BossDiagnostics.DrawDebugPanel(new Rect(left, logBtn.y - 226f, 360f, 220f));
        }

        // ---- 右端: 「ログ保存済み」(短時間だけ、ただの文字) ----
        float remain = FreezeDiagnostics.ToastUntilRealtime - Time.realtimeSinceStartup;
        if (remain > 0f && !DetailOpen)
        {
            Color c = toast.normal.textColor; c.a = Mathf.Clamp01(remain / 0.5f);
            toast.normal.textColor = c;
            string t = FreezeDiagnostics.ToastText;
            Vector2 sz = toast.CalcSize(new GUIContent(t));
            Rect r = new Rect(right - sz.x - 10f, h * 0.62f, sz.x + 10f, sz.y + 4f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f * c.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y, r.width - 5f, r.height), t, toast);
            c.a = 1f; toast.normal.textColor = c;
        }

        if (DetailOpen) DrawDetail(w, h);
        GUI.matrix = prev;
    }

    void DrawDetail(float w, float h)
    {
        Rect area = new Rect(w * 0.03f, h * 0.04f, w * 0.94f, h * 0.9f);
        UiBackdrop.Draw(area, 0.95f);
        // 背後のタップがゲーム側のIMGUIボタンへ抜けないよう、この範囲のクリックはここで止める。
        Event e = Event.current;

        const float closeW = 150f, closeH = 58f;
        Rect closeRect = new Rect(area.xMax - closeW - 8f, area.y + 8f, closeW, closeH);
        float headerW = area.width - closeW - 30f;
        GUI.Label(new Rect(area.x + 12f, area.y + 8f, headerW, 22f), "Freeze/Warp・ボス診断ログ(自動では開きません。スクリーンショットで共有できます)", header);

        // タブと手動記録
        Rect tab0 = new Rect(area.x + 12f, area.y + 36f, 150f, 30f);
        Rect tab1 = new Rect(tab0.xMax + 6f, tab0.y, 110f, 30f);
        Rect dumpBtn = new Rect(tab1.xMax + 16f, tab0.y, 130f, 30f);
        if (GUI.Toggle(tab0, tab == 0, $"Freeze/Warp ({FreezeDiagnostics.AnomalyCount})", button)) tab = 0;
        if (GUI.Toggle(tab1, tab == 1, $"Boss ({BossDiagnostics.SnapshotCount})", button)) tab = 1;
        if (GUI.Button(dumpBtn, "今すぐ記録", button)) FreezeDiagnostics.ManualDump();

        var sb = new StringBuilder();
        if (tab == 0)
        {
            sb.AppendLine(FreezeDiagnostics.Summary());
            sb.AppendLine();
            sb.AppendLine("最近の異常(新しい順):");
            var list = FreezeDiagnostics.RecentAnomalies;
            for (int i = list.Count - 1; i >= 0 && i >= list.Count - 12; i--) sb.AppendLine($"  {list[i].time} {(list[i].dumped ? "" : "(省略) ")}{list[i].reason}");
            if (list.Count == 0) sb.AppendLine("  (なし)");
            sb.AppendLine();
            sb.AppendLine("最後に保存した詳細:");
            sb.Append(FreezeDiagnostics.LastDumpText);
        }
        else
        {
            sb.Append(BossDiagnostics.LastSnapshotText);
        }
        string body = sb.ToString();
        Rect view = new Rect(area.x + 12f, area.y + 74f, area.width - 24f, area.height - 84f);
        float innerW = Mathf.Max(view.width - 20f, text.CalcSize(new GUIContent("frame  realtime  rawDt(ms)  scaledDt(ms)  timeScale  pauseReasons  hitStop  logicalX  playerY  camX  distance  lives  runSpeed")).x + 20f);
        float innerH = Mathf.Max(view.height, text.CalcHeight(new GUIContent(body), innerW) + 20f);
        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, innerW, innerH));
        GUI.Label(new Rect(0f, 0f, innerW, innerH), body, text);
        GUI.EndScrollView();

        // 「閉じる」は最後に描く(=スクロール内容より手前、常に同じ場所)。
        if (GUI.Button(closeRect, "閉じる", bigButton)) { DetailOpen = false; }
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp) && area.Contains(e.mousePosition)) e.Use();
    }
}

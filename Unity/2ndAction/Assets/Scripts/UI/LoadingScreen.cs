using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 読み込み中の表示(2026-10-11、実機の要望「画面の切り替えで暗いまま止まったように見える」)。
//  ・シーンの読み直し(ラン開始/ホームへ戻る/Retry/練習/マルチ等)は、以前は SceneManager.LoadScene(同期)で、読み込みの間は
//    画面が一切描かれず、暗い覆い(画面の切り替え)のまま止まって見えた → LoadScene をここに通し、裏で読み込む(LoadSceneAsync)。
//    読み込みの間も「読み込み中…」と回る印を描き続ける。
//  ・ShowAfter 秒より早く終わる時は何も出さない(一瞬のちらつきを出さない)。見せるための待ち時間は足さない。
//  ・読み込みの間と新しいシーンの最初の重いフレームの間は時間を止め(TimeControl)、タップを通さない
//    (タッチ/IMGUI/uGUI)。終わった時に押したままの指は、離すまで次の画面へ通さない(UiInputGate.LatchUntilRelease)。
//  ・同じシーンの読み直しなので、切り替える直前に古いシーンの物を片付けてから新しいシーンを有効にする
//    (古いシーンの部品と新しいシーンの部品が同時に居ると、「既に Instance がある」で新しい方が自分を消すため)。
//  ・失敗(時間切れ/例外)の時は暗いまま放置せず、状況と「もう一度読み込む」を出す。
//  ・起動直後(最初のシーンの最初の重いフレーム)も、長くかかった時は同じ表示を出す。
//  ・進み具合は Unity の読み込みの割合しか分からず、シーンを有効にする所の重さは分からないので、架空の%は出さない。
public class LoadingScreen : MonoBehaviour
{
    static LoadingScreen inst;
    public const float ShowAfter = 0.3f;     // これより早く終わる読み込みは何も見せない
    public const float FailAfter = 25f;      // これより長い読み込みは失敗として扱う

    public static bool Busy => inst != null && inst.busy;
    public static bool BlocksInput => inst != null && (inst.busy || Time.realtimeSinceStartup < inst.blockUntil);
    public static bool Visible => inst != null && inst.alpha > 0.01f;
    public static bool Failed => inst != null && inst.failed;
    // 確認用
    public static int Loads { get; private set; }
    public static int TimesShown { get; private set; }
    public static float LastWaitSeconds { get; private set; }
    public static float LongestWait { get; private set; }
    public static string LastReason { get; private set; } = "";
    public static bool QaForceFail; // 開発の確認用: 失敗の表示を出す
    public static float QaExtraHold; // 開発の確認用: 読み込みの後も表示を残す秒数(撮影用)
    public static float WorstFrameWhileBusy { get; private set; } // 読み込み中のいちばん長いフレーム(表示が固まっていないか)

    bool busy, failed, shownThisLoad;
    float since, alpha, blockUntil;
    string reason = "", failText = "";
    int buildIndex;
    readonly object pauseOwner = new object();
    Camera gapCam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Ensure();
        inst.StartCoroutine(inst.StartupRoutine());
    }

    static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("[LoadingScreen]");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<LoadingScreen>();
    }

    // シーンを読み直す(SceneManager.LoadScene の代わり)
    public static void LoadScene(int index, string why)
    {
        Ensure();
        if (inst.busy && !inst.failed) { Debug.Log($"[Loading] ignored a second load ({why}) while '{inst.reason}' is loading"); return; }
        inst.StopAllCoroutines();
        inst.StartCoroutine(inst.LoadRoutine(index, why));
    }
    public static void ReloadActive(string why) => LoadScene(SceneManager.GetActiveScene().buildIndex, why);

    void Begin(string why)
    {
        busy = true; failed = false; shownThisLoad = false; failText = ""; WorstFrameWhileBusy = 0f;
        since = Time.realtimeSinceStartup; reason = why; LastReason = why;
        TimeControl.Pause(pauseOwner);
        SetEventSystems(false);
    }

    void Finish()
    {
        LastWaitSeconds = Time.realtimeSinceStartup - since;
        LongestWait = Mathf.Max(LongestWait, LastWaitSeconds);
        TimeControl.Resume(pauseOwner);
        SetEventSystems(true);
        busy = false;
        blockUntil = Time.realtimeSinceStartup + 0.15f;
        UiInputGate.LatchUntilRelease(); // 押したままの指は離すまで次の画面へ通さない
        if (gapCam != null) { Destroy(gapCam.gameObject); gapCam = null; }
        Debug.Log($"[Loading] done '{reason}' in {LastWaitSeconds:F2}s (shown={shownThisLoad})");
    }

    IEnumerator LoadRoutine(int index, string why)
    {
        Loads++;
        buildIndex = index;
        Begin(why);
        yield return null; // 時間を止めて入力を閉じた状態を1フレーム描いてから
        AsyncOperation op = null;
        try
        {
            if (QaForceFail) throw new System.Exception("QA forced failure");
            op = SceneManager.LoadSceneAsync(index, LoadSceneMode.Single);
            if (op != null) op.allowSceneActivation = false;
        }
        catch (System.Exception e) { Fail("シーンを読み込めませんでした", e.Message); yield break; }
        if (op == null) { Fail("シーンを読み込めませんでした", "LoadSceneAsync returned null"); yield break; }
        while (op.progress < 0.9f)
        {
            if (Time.realtimeSinceStartup - since > FailAfter) { Fail("読み込みに時間がかかっています", $"progress {op.progress:P0}"); yield break; }
            yield return null;
        }
        // 切り替える直前: 古いシーンの物を片付ける(同じ部品の新旧が同時に居ないように)。その1フレームは何も写らないので暗い背景だけを描く
        MakeGapCamera();
        var old = SceneManager.GetActiveScene();
        foreach (var root in old.GetRootGameObjects()) if (root != null) Destroy(root);
        yield return null;
        op.allowSceneActivation = true;
        while (!op.isDone)
        {
            if (Time.realtimeSinceStartup - since > FailAfter) { Fail("読み込みに時間がかかっています", "activation"); yield break; }
            yield return null;
        }
        yield return SettleRoutine();
    }

    // 新しいシーンの最初の重いフレーム(各部品の準備)が落ち着くまで。最大 3秒
    IEnumerator SettleRoutine()
    {
        float t0 = Time.realtimeSinceStartup;
        int calm = 0;
        while (calm < 2 && Time.realtimeSinceStartup - t0 < 3f)
        {
            yield return null;
            calm = Time.unscaledDeltaTime < 0.1f && GameManager.Instance != null ? calm + 1 : 0;
        }
        if (QaExtraHold > 0f) { float h = QaExtraHold; QaExtraHold = 0f; float t1 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t1 < h) yield return null; }
        Finish();
    }

    IEnumerator StartupRoutine()
    {
        Begin("startup");
        yield return SettleRoutine();
    }

    void Fail(string title, string detail)
    {
        failed = true; failText = title;
        Debug.LogWarning($"[Loading] FAILED '{reason}': {title} ({detail})");
        MakeGapCamera();
    }

    void MakeGapCamera()
    {
        if (gapCam != null) return;
        var go = new GameObject("LoadingGapCamera");
        DontDestroyOnLoad(go);
        gapCam = go.AddComponent<Camera>();
        gapCam.clearFlags = CameraClearFlags.SolidColor; gapCam.backgroundColor = new Color(0.04f, 0.05f, 0.11f, 1f);
        gapCam.cullingMask = 0; gapCam.depth = -100;
    }

    static void SetEventSystems(bool on)
    {
        foreach (var es in FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (es != null) es.enabled = on;
    }

    void Update()
    {
        if (busy && Time.realtimeSinceStartup - since > 0.05f) WorstFrameWhileBusy = Mathf.Max(WorstFrameWhileBusy, Time.unscaledDeltaTime);
                bool want = busy && (failed || Time.realtimeSinceStartup - since >= ShowAfter);
        if (want && !shownThisLoad) { shownThisLoad = true; TimesShown++; }
        alpha = Mathf.MoveTowards(alpha, want ? 1f : 0f, Time.unscaledDeltaTime / (want ? 0.15f : 0.12f));
    }

    void LateUpdate() { if (busy) SetEventSystems(false); } // 新しいシーンで有効になった EventSystem も読み込みの間は止める

    // ===== 描画(IMGUI、いちばん手前) =====
    static Texture2D white;
    static Texture2D White() { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; } return white; }

    void OnGUI()
    {
        GUI.depth = -10000;
        if (busy)
        {
            // 読み込みの間のタップ/クリックは、ほかの画面へ通さない
            var e = Event.current;
            if (e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag || e.type == EventType.TouchDown || e.type == EventType.TouchUp || e.type == EventType.TouchMove)) e.Use();
        }
        if (alpha <= 0.01f) return;
        float a = alpha;
        Color keep = GUI.color;
        float sw = Screen.width, sh = Screen.height;
        GUI.color = new Color(0.04f, 0.05f, 0.11f, 0.94f * a);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), White());
        // 中央の飾り枠(縦横どちらでも中央に収まる大きさ)
        float k = Mathf.Clamp(Mathf.Min(sw, sh) / 720f, 0.8f, 2f);
        float pw = Mathf.Min(sw - 32f, 420f * k), ph = (failed ? 230f : 120f) * k;
        Rect p = new Rect((sw - pw) * 0.5f, (sh - ph) * 0.5f, pw, ph);
        Color gold = new Color(0.95f, 0.8f, 0.42f, a);
        GUI.color = new Color(0.07f, 0.09f, 0.18f, 0.96f * a); GUI.DrawTexture(p, White());
        GUI.color = gold;
        float t = 2f * k;
        GUI.DrawTexture(new Rect(p.x, p.y, p.width, t), White()); GUI.DrawTexture(new Rect(p.x, p.yMax - t, p.width, t), White());
        GUI.DrawTexture(new Rect(p.x, p.y, t, p.height), White()); GUI.DrawTexture(new Rect(p.xMax - t, p.y, t, p.height), White());
        GUI.color = new Color(gold.r, gold.g, gold.b, 0.45f * a);
        float inset = 6f * k;
        Rect q = new Rect(p.x + inset, p.y + inset, p.width - inset * 2f, p.height - inset * 2f);
        GUI.DrawTexture(new Rect(q.x, q.y, q.width, 1f * k), White()); GUI.DrawTexture(new Rect(q.x, q.yMax - 1f * k, q.width, 1f * k), White());
        // 角の飾り(小さな菱形)
        GUI.color = gold;
        foreach (var c in new[] { new Vector2(p.x, p.y), new Vector2(p.xMax, p.y), new Vector2(p.x, p.yMax), new Vector2(p.xMax, p.yMax) })
        {
            Matrix4x4 m = GUI.matrix; float d = 9f * k;
            GUIUtility.RotateAroundPivot(45f, c);
            GUI.DrawTexture(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), White());
            GUI.matrix = m;
        }
        if (!failed)
        {
            // 回る印(金の点が円を回る)
            Vector2 center = new Vector2(p.x + 50f * k, p.center.y);
            float rr = 16f * k, now = Time.realtimeSinceStartup;
            for (int i = 0; i < 8; i++)
            {
                float ang = now * 5f - i * 0.55f;
                float s = Mathf.Lerp(7f, 3f, i / 7f) * k;
                GUI.color = new Color(gold.r, gold.g, gold.b, a * Mathf.Lerp(1f, 0.2f, i / 7f));
                GUI.DrawTexture(new Rect(center.x + Mathf.Cos(ang) * rr - s * 0.5f, center.y + Mathf.Sin(ang) * rr - s * 0.5f, s, s), White());
            }
            GUI.color = new Color(1f, 1f, 1f, a);
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.9f, 0.62f) } };
            Rect tr = new Rect(center.x + rr + 22f * k, p.y, p.xMax - (center.x + rr + 22f * k) - 16f * k, p.height);
            FitLabel(tr, "読み込み中…", st);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, a);
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(1f, 0.85f, 0.6f) } };
            FitLabel(new Rect(p.x + 20f * k, p.y + 24f * k, p.width - 40f * k, 60f * k), failText, st);
            var bs = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(20f * k), fontStyle = FontStyle.Bold };
            Rect b1 = new Rect(p.x + 24f * k, p.yMax - 80f * k, p.width - 48f * k, 56f * k);
            if (GUI.Button(b1, Loc.Auto("もう一度読み込む"), bs))
            {
                QaForceFail = false;
                StopAllCoroutines();
                busy = false;
                StartCoroutine(LoadRoutine(buildIndex, reason + " (retry)"));
            }
        }
        GUI.color = keep;
    }

    static void FitLabel(Rect r, string text, GUIStyle st)
    {
        string s = Loc.Auto(text);
        Vector2 sz = st.CalcSize(new GUIContent(s));
        if (!st.wordWrap && sz.x > r.width && sz.x > 1f) st.fontSize = Mathf.Max(12, Mathf.FloorToInt(st.fontSize * r.width / sz.x));
        // 2026-10-24: 折り返す見出しも枠の高さに収まるまで縮める(ミャンマー語などは字が高く、2行目が枠の下で切れていた)
        if (st.wordWrap) while (st.fontSize > 12 && st.CalcHeight(new GUIContent(s), r.width) > r.height) st.fontSize--;
        GUI.Label(r, s, st);
    }
}

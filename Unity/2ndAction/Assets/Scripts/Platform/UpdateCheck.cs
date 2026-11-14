using UnityEngine;

// 更新の確認と案内(2026-11-14)。Android(Google Play から入れた時)だけ。
//  ・起動して、タイトルが消えてホームに落ち着いてから 1 回だけ確かめる(起動を止めない・待たない)。ランの途中には出さない
//  ・Google Play の公式の「アプリ内アップデート」(UpdateCheckBridge.java、com.google.android.play:app-update)に
//    「その人が Play から実際に入手できる新しい版」があるかを聞く。クローズドテスト/段階配信はその人に配信済みの版だけが返る
//  ・Play 以外から入れた(開発の APK を直接入れた等)時は確かめない = 未配信の版への更新を求めない。Windows/エディターでは何もしない
//  ・ある時だけ「更新する / あとで」。更新するはストアのこのアプリのページを開くだけ。強制しない(あとで/戻る でそのまま遊べる)。
//    通信できない/確かめられない時は何も出さない。1 回の起動で 1 回だけ
public class UpdateCheck : MonoBehaviour
{
    public static string State { get; private set; } = "idle"; // 確認用: idle / skipped / checking / available / none / notplay / error
    public static string LastResult { get; private set; } = "";
    public static bool PromptOpen { get; private set; }
    public static int PromptsShown { get; private set; }
    public static int StoreOpened { get; private set; }
    static UpdateCheck inst;
    static bool started, promptDue, promptDone;
    float calmFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("[UpdateCheck]");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<UpdateCheck>();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版の確認: -updateMock available|none|notplay|error で、確かめた結果を決める(Windows でも案内の画面を確かめられる)
    static string MockArg()
    {
        var a = QaArgs.All;
        int i = System.Array.IndexOf(a, "-updateMock");
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
#endif

    // ホームに落ち着いている(タイトル/起動ロゴ/読み込み/ランの途中/ほかの窓 ではない)
    static bool HomeCalm()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.HasStarted) return false;
        if (TitleScreen.Showing || BootSplash.Showing || LoadingScreen.Busy) return false;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return false;
        if (UiInputGate.ModalOpen && !PromptOpen) return false;
        return true;
    }

    void Update()
    {
        if (!started)
        {
            if (!HomeCalm()) { calmFor = 0f; return; }
            calmFor += Time.unscaledDeltaTime;
            if (calmFor < 1.5f) return;
            started = true;
            StartCheck();
            return;
        }
        if (promptDue && !promptDone && !PromptOpen)
        {
            if (!HomeCalm() || NoticeQueue.Open) { calmFor = 0f; return; }
            calmFor += Time.unscaledDeltaTime;
            if (calmFor < 0.8f) return;
            PromptOpen = true; promptDone = true; PromptsShown++;
            Debug.Log("[Update] showing the update prompt");
        }
        if (PromptOpen && GameManager.Instance != null && GameManager.Instance.HasStarted) PromptOpen = false; // 念のため(ランへ出たら閉じる)
    }

    void StartCheck()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string mock = MockArg();
        if (mock != null) { State = "checking"; OnUpdateResult(mock == "available" ? "available|9999|3" : mock == "notplay" ? "notplay|" : mock == "error" ? "error|mock" : "none|1"); return; }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
        State = "checking";
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var b = new AndroidJavaClass("com.zero0084.omm.UpdateCheckBridge"))
                b.CallStatic("check", act, gameObject.name, nameof(OnUpdateResult));
        }
        catch (System.Exception e) { State = "error"; LastResult = "error|" + e.GetType().Name; Debug.Log("[Update] check failed to start: " + e.Message); }
#else
        State = "skipped";
#endif
    }

    // UpdateCheckBridge から(主スレッド。UnitySendMessage)
    public void OnUpdateResult(string msg)
    {
        LastResult = msg ?? "";
        string kind = LastResult.Split('|')[0];
        State = kind == "available" ? "available" : kind == "none" ? "none" : kind == "notplay" ? "notplay" : "error";
        Debug.Log($"[Update] result: {LastResult}");
        if (State == "available") { promptDue = true; calmFor = 0f; }
    }

    static void OpenStore()
    {
        StoreOpened++;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var b = new AndroidJavaClass("com.zero0084.omm.UpdateCheckBridge"))
                b.CallStatic<bool>("openStorePage", act);
        }
        catch (System.Exception e) { Debug.Log("[Update] could not open the store: " + e.Message); }
#else
        Debug.Log("[Update] (not Android) the store page would open here");
#endif
    }

    public static void Later() { if (!PromptOpen) return; PromptOpen = false; UiInputGate.LatchUntilRelease(); Debug.Log("[Update] later"); }
    static void Update_() { PromptOpen = false; UiInputGate.LatchUntilRelease(); OpenStore(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugChoose(bool update) { if (!PromptOpen) return; if (update) Update_(); else Later(); }
#endif

    void OnGUI()
    {
        if (!PromptOpen) return;
        GUI.depth = -1860; // お知らせ(NoticeQueue -1850)と同じくらい手前
        int padLayer = PadNav.BeginLayer(71);
        float s = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f));
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.6f));
        float pw = Mathf.Min(620f, w - 32f), ph = 300f;
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.97f);
        LocGUI.Label(new Rect(p.x + 24f, p.y + 18f, p.width - 48f, 44f), LocUpdate.Title, UiKit.Label(26f, TextAnchor.MiddleCenter, true, UiTheme.GoldText));
        var bodySt = new GUIStyle(UiKit.Label(21f, TextAnchor.UpperCenter, false, UiTheme.Text)) { wordWrap = true };
        LocGUI.Label(new Rect(p.x + 28f, p.y + 74f, p.width - 56f, 120f), LocUpdate.Body, bodySt);
        float bw = (p.width - 28f * 2f - 20f) * 0.5f, by = p.yMax - 24f - 56f;
        if (UiKit.Button(new Rect(p.x + 28f, by, bw, 56f), LocUpdate.Later, 20f, false, false)) Later();
        if (UiKit.Button(new Rect(p.x + 28f + bw + 20f, by, bw, 56f), LocUpdate.Update, 20f, true, true)) Update_();
        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}

// 更新の案内の文言(2026-11-14)。訳は Tools/loc/keys_extra42.tsv(エステルの確認の後に反映)
public static class LocUpdate
{
    public const string Title = "新しいバージョンがあります";                                   // ID 11030
    // 文言はエステルと相談して決めた物(2026-11-14)。ボタンはストアを開くだけなので「ストアを開く」
    public const string Body = "ストアで最新版に更新できます。\n更新しなくても、このまま遊べます。"; // ID 11031
    public const string Update = "ストアを開く";                                                 // ID 11032
    public const string Later = "あとで";                                                         // ID 11033
    public static readonly string[] All = { Title, Body, Update, Later };
}

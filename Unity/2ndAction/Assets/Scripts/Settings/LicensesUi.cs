using UnityEngine;

// ライセンスの画面(2026-10-23)。設定 →「データとプライバシー」→「ライセンス」から開く。
//  使っている他者の素材の著作権表示とライセンス本文を、原文(英語)のまま見せる(Resources/Licenses/ThirdPartyNotices.txt)。
//  今は MS Basic(MuseScore の音源。BGM の書き出しに使用、MIT ライセンス: 謝辞と著作権表示の掲載が必要)。
//  経緯と権利管理表: Docs/AudioProvenance_2026-10-23
public class LicensesUi : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static LicensesUi instance;
    readonly UiScroll scroll = new UiScroll();
    string text;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("LicensesUi");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LicensesUi>();
    }

    public static string NoticeText
    {
        get
        {
            if (instance != null && instance.text != null) return instance.text;
            var ta = Resources.Load<TextAsset>("Licenses/ThirdPartyNotices");
            string t = ta != null ? ta.text.Replace("\r", "") : "";
            if (instance != null) instance.text = t;
            return t;
        }
    }

    public static void Open() { IsOpen = true; if (instance != null) instance.scroll.Reset(); }
    public static void Close() { IsOpen = false; UiInputGate.LatchUntilRelease(); }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -2300; // 設定(-2000)より手前
        int padLayer = PadNav.BeginLayer(12);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.72f));
        float pw = Mathf.Min(760f, w - 24f), ph = Mathf.Min(680f, h - 24f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.96f);
        LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), LocPendingReview.LicensesTitle, UiKit.Label(26f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiIcons.CloseButton(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), true)) Close();

        float x = p.x + 28f, full = p.width - 56f;
        var note = new GUIStyle(UiKit.Label(14f, TextAnchor.UpperLeft, false, UiTheme.TextSub)) { wordWrap = true };
        string noteText = Loc.Auto(LocPendingReview.LicensesNote);
        float noteH = note.CalcHeight(new GUIContent(noteText), full);
        GUI.Label(new Rect(x, p.y + 60f, full, noteH), noteText, note);

        // 本文は原文のまま(訳さない)。英語の文字の幅で折り返す
        var body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = UiTheme.Text }, richText = false };
        float by = Mathf.Max(p.yMax - 24f - 48f, p.y + 60f + noteH + 80f);
        var view = new Rect(x, p.y + 66f + noteH, full, by - 12f - (p.y + 66f + noteH));
        string t = NoticeText;
        float contentH = body.CalcHeight(new GUIContent(t), view.width - 12f) + 8f;
        scroll.Begin(view, contentH, "licenses");
        GUI.Label(new Rect(0f, 0f, view.width - 12f, contentH), t, body);
        scroll.End(view, contentH);

        if (UiKit.Button(new Rect(x + full - 200f, by, 200f, 48f), "閉じる", 18f, false, false)) Close();

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}

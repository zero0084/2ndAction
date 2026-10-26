using UnityEngine;

// ライセンスの画面(2026-10-23)。設定 →「データとプライバシー」→「ライセンス」から開く。
//  使っている他者の素材の著作権表示とライセンス本文を、原文(英語)のまま見せる(Resources/Licenses/ThirdPartyNotices.txt)。
//  今は MS Basic(MuseScore の音源。BGM の書き出しに使用、MIT ライセンス: 謝辞と著作権表示の掲載が必要)。
//  経緯と権利管理表: Docs/AudioProvenance_2026-10-23
// 2026-10-26: 同じ画面で「制作クレジット」も出す(設定 →「このゲームについて」)。中身はエンドロールの表(StaffCreditsData)から作る
public class LicensesUi : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public enum Page { Licenses, Credits }
    public static Page Showing { get; private set; } = Page.Licenses;
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
            // 2026-10-26: Android にだけ入るライブラリ(Google Play services の中の OSS、AndroidX など)の表示は Android の時だけ足す
            //  (調べた記録: Docs/Licenses_2026-10-26/license_audit.md)。開発版は -caps android で Windows でも確かめられる
            if (PlatformCaps.Profile == "mobile")
            {
                var tb = Resources.Load<TextAsset>("Licenses/ThirdPartyNotices_Android");
                if (tb != null) t = t.TrimEnd() + "\n\n" + tb.text.Replace("\r", "");
            }
            if (instance != null) instance.text = t;
            return t;
        }
    }

    // 長い本文(約50万字)は1つのラベルでは描けない(途中で切れる)→ 40行ずつの塊ごとに高さを測り、見えている塊だけ描く
    string chunkSrc; float chunkW = -1f; int chunkFont;
    readonly System.Collections.Generic.List<string> chunks = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.List<float> chunkH = new System.Collections.Generic.List<float>();
    float ChunkedHeight(string t, GUIStyle st, float width)
    {
        if (t != chunkSrc || Mathf.Abs(width - chunkW) > 0.5f || st.fontSize != chunkFont)
        {
            chunkSrc = t; chunkW = width; chunkFont = st.fontSize; chunks.Clear(); chunkH.Clear();
            var lines = t.Split('\n');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
                if (i == lines.Length - 1 || (i + 1) % 40 == 0)
                {
                    string c = sb.ToString();
                    chunks.Add(c);
                    chunkH.Add(st.CalcHeight(new GUIContent(c.Length == 0 ? " " : c), width)); // 塊の間の改行1つ分は高さに含まれる(次の塊の先頭は新しい行)
                    sb.Length = 0;
                }
            }
        }
        float h = 0f; foreach (var v in chunkH) h += v; return h;
    }
    void DrawChunked(GUIStyle st, float width, float top, float viewH)
    {
        float y = 0f;
        for (int i = 0; i < chunks.Count; i++)
        {
            float h = chunkH[i];
            if (y + h >= top && y <= top + viewH) GUI.Label(new Rect(0f, y, width, h), chunks[i], st);
            y += h;
        }
    }

    public static void Open() { Showing = Page.Licenses; IsOpen = true; if (instance != null) instance.scroll.Reset(); }
    public static void OpenCredits() { Showing = Page.Credits; IsOpen = true; if (instance != null) instance.scroll.Reset(); }

    // 制作クレジットの本文: エンドロールと同じ表(役職は英語のまま、日本語の時は説明を添える)+ 著作権の表記
    public static string CreditsText
    {
        get
        {
            var d = StaffCreditsData.LoadOrDefault();
            bool ja = Loc.Current == "ja";
            var sb = new System.Text.StringBuilder();
            foreach (var e in d.entries)
            {
                if (e.names == null || e.names.Length == 0) continue;
                if (string.IsNullOrEmpty(e.role)) { sb.AppendLine(string.Join("  ", e.names)); sb.AppendLine(); continue; } // 題字(ONE MORE MILE)
                sb.AppendLine(ja && !string.IsNullOrEmpty(e.subtitle) ? $"{e.role} / {e.subtitle}" : e.role);
                foreach (var n in e.names) sb.AppendLine("    " + n);
                sb.AppendLine();
            }
            sb.AppendLine(AppInfo.Copyright);
            return sb.ToString();
        }
    }
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
        bool credits = Showing == Page.Credits;
        LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), credits ? LocPendingReview.CreditsTitle : LocPendingReview.LicensesTitle, UiKit.Label(26f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiIcons.CloseButton(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), true)) Close();

        float x = p.x + 28f, full = p.width - 56f;
        var note = new GUIStyle(UiKit.Label(14f, TextAnchor.UpperLeft, false, UiTheme.TextSub)) { wordWrap = true };
        string noteText = Loc.Auto(credits ? LocPendingReview.CreditsNote : LocPendingReview.LicensesNote);
        float noteH = note.CalcHeight(new GUIContent(noteText), full);
        GUI.Label(new Rect(x, p.y + 60f, full, noteH), noteText, note);

        // 本文は原文のまま(訳さない)。英語の文字の幅で折り返す。クレジットは名前を大きめに
        var body = new GUIStyle(GUI.skin.label) { fontSize = credits ? 18 : 14, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = UiTheme.Text }, richText = false };
        float by = Mathf.Max(p.yMax - 24f - 48f, p.y + 60f + noteH + 80f);
        var view = new Rect(x, p.y + 66f + noteH, full, by - 12f - (p.y + 66f + noteH));
        string t = credits ? CreditsText : NoticeText;
        float contentH = (credits ? body.CalcHeight(new GUIContent(t), view.width - 12f) : ChunkedHeight(t, body, view.width - 12f)) + 8f;
        scroll.Begin(view, contentH, credits ? "credits" : "licenses");
        if (credits) LocGUI.Label(new Rect(0f, 0f, view.width - 12f, contentH), t, body); // 日本語の名前・説明があるので文字の当て方は LocGUI
        else DrawChunked(body, view.width - 12f, scroll.y, view.height);
        scroll.End(view, contentH);

        if (UiKit.Button(new Rect(x + full - 200f, by, 200f, 48f), "閉じる", 18f, false, false)) Close();

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}

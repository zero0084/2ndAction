using System.Collections.Generic;
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

    // ===================================================================== 制作クレジット(2026-11-18 作り直し)
    // マスターの決定: クレジットの中(見出し・役職・説明・名前)は英語で統一し、ゲームの言語の設定によらず同じ表記にする。
    //  中身はエンドロールと同じ表(StaffCreditsData。PLAYTESTERS は Resources/Credits/Playtesters.txt の1行1名)。
    //  字の決まりはここだけ: 題字 / 役職(見出し)/ 説明 / 名前 / 著作権 で、それぞれ全項目で同じ字の種類・大きさ・色・太さ。
    //  すべて中央揃え。名前は枠の幅で折り返す(字を小さくして詰めない)。同じ役職が続く時は見出しを1回だけ出す
    public const string CreditsHeading = "Credits";
    public const string CreditsIntro = "The people and resources behind One More Mile.";
    public const float CreditsTitleSize = 30f, CreditsRoleSize = 17f, CreditsDescSize = 14f, CreditsNameSize = 22f, CreditsFootSize = 14f;
    public const float CreditsGapSection = 28f, CreditsGapRoleToDesc = 2f, CreditsGapToNames = 8f, CreditsGapName = 6f, CreditsGapFoot = 30f, CreditsPadBottom = 28f;

    public enum CreditKind { Title, Role, Desc, Name, Foot }
    public struct CreditLine { public CreditKind kind; public string text; public float gapBefore; }

    // 表示する行の並び(試験もこれを使う)
    public static List<CreditLine> CreditLines()
    {
        var d = StaffCreditsData.LoadOrDefault();
        var o = new List<CreditLine>();
        string prevRole = null;
        foreach (var e in d.entries)
        {
            if (e == null || e.names == null || e.names.Length == 0) continue;
            if (string.IsNullOrEmpty(e.role))
            {
                foreach (var n in e.names) o.Add(new CreditLine { kind = CreditKind.Title, text = n, gapBefore = o.Count == 0 ? 0f : CreditsGapSection });
                prevRole = null;
                continue;
            }
            bool sameRole = e.role == prevRole && string.IsNullOrEmpty(e.subtitle);
            if (!sameRole)
            {
                o.Add(new CreditLine { kind = CreditKind.Role, text = e.role, gapBefore = o.Count == 0 ? 0f : CreditsGapSection });
                if (!string.IsNullOrEmpty(e.subtitle)) o.Add(new CreditLine { kind = CreditKind.Desc, text = e.subtitle, gapBefore = CreditsGapRoleToDesc });
            }
            for (int i = 0; i < e.names.Length; i++)
                o.Add(new CreditLine { kind = CreditKind.Name, text = e.names[i], gapBefore = i == 0 && !sameRole ? CreditsGapToNames : CreditsGapName });
            prevRole = e.role;
        }
        o.Add(new CreditLine { kind = CreditKind.Foot, text = AppInfo.Copyright, gapBefore = CreditsGapFoot });
        return o;
    }

    // 文字だけの本文(確認用・試験用)
    public static string CreditsText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in CreditLines()) { if (l.gapBefore >= CreditsGapSection) sb.AppendLine(); sb.AppendLine(l.kind == CreditKind.Name ? "    " + l.text : l.text); }
            return sb.ToString();
        }
    }

    static GUIStyle cTitle, cRole, cDesc, cName, cFoot;
    public static GUIStyle CreditStyle(CreditKind k)
    {
        if (cTitle == null)
        {
            GUIStyle Make(float size, bool bold, Color c) => new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(size), fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = TextAnchor.UpperCenter, wordWrap = true, richText = false, clipping = TextClipping.Overflow, normal = { textColor = c }, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            cTitle = Make(CreditsTitleSize, true, UiTheme.GoldText);
            cRole = Make(CreditsRoleSize, true, UiTheme.GoldText);
            cDesc = Make(CreditsDescSize, false, UiTheme.TextSub);
            cName = Make(CreditsNameSize, false, UiTheme.Text);
            cFoot = Make(CreditsFootSize, false, UiTheme.TextSub);
        }
        switch (k) { case CreditKind.Title: return cTitle; case CreditKind.Role: return cRole; case CreditKind.Desc: return cDesc; case CreditKind.Name: return cName; default: return cFoot; }
    }

    // 並べた結果(試験用): 各行の矩形(内容の座標)と全体の高さ
    public static float LastCreditsContentHeight { get; private set; }
    public static float LastCreditsViewHeight { get; private set; }
    public static float LastCreditsWidth { get; private set; }
    public static readonly List<(CreditLine line, Rect rect)> LastCreditsLayout = new List<(CreditLine, Rect)>();

    static float LayoutCredits(float width, List<(CreditLine line, Rect rect)> into)
    {
        into.Clear();
        float y = 0f;
        foreach (var l in CreditLines())
        {
            y += l.gapBefore;
            var st = CreditStyle(l.kind);
            float h = Mathf.Ceil(st.CalcHeight(new GUIContent(l.text), width));
            into.Add((l, new Rect(0f, y, width, h)));
            y += h;
        }
        return y + CreditsPadBottom;
    }
    public static void Close() { IsOpen = false; UiInputGate.LatchUntilRelease(); }

    float S => UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f));

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
        // 2026-11-18: クレジットの見出しと案内は英語に固定(中身と同じく言語の設定によらない)
        if (credits) GUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), CreditsHeading, UiKit.Label(26f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        else LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), LocPendingReview.LicensesTitle, UiKit.Label(26f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiIcons.CloseButton(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), true)) Close();

        float x = p.x + 28f, full = p.width - 56f;
        var note = new GUIStyle(UiKit.Label(14f, TextAnchor.UpperLeft, false, UiTheme.TextSub)) { wordWrap = true };
        string noteText = credits ? CreditsIntro : Loc.Auto(LocPendingReview.LicensesNote);
        float noteH = note.CalcHeight(new GUIContent(noteText), full);
        GUI.Label(new Rect(x, p.y + 60f, full, noteH), noteText, note);

        float by = Mathf.Max(p.yMax - 24f - 48f, p.y + 60f + noteH + 80f);
        var view = new Rect(x, p.y + 66f + noteH, full, by - 12f - (p.y + 66f + noteH));
        if (credits)
        {
            // 行ごとに決まった字で並べる(最後の行の下に余白。内容の高さ = 実際に並べた高さなので、最後まで送れる)
            float cw = view.width - 12f;
            float contentH = LayoutCredits(cw, LastCreditsLayout);
            LastCreditsContentHeight = contentH; LastCreditsViewHeight = view.height; LastCreditsWidth = cw;
            scroll.Begin(view, contentH, "credits");
            foreach (var (line, r) in LastCreditsLayout)
                if (r.yMax >= scroll.y - 4f && r.y <= scroll.y + view.height + 4f) GUI.Label(r, line.text, CreditStyle(line.kind));
            scroll.End(view, contentH);
        }
        else
        {
            // 本文は原文のまま(訳さない)。英語の文字の幅で折り返す
            var body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = UiTheme.Text }, richText = false };
            string t = NoticeText;
            float contentH = ChunkedHeight(t, body, view.width - 12f) + 8f;
            scroll.Begin(view, contentH, "licenses");
            DrawChunked(body, view.width - 12f, scroll.y, view.height);
            scroll.End(view, contentH);
        }

        if (UiKit.Button(new Rect(x + full - 200f, by, 200f, 48f), "閉じる", 18f, false, false)) Close();

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}

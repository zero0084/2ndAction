using System.Collections.Generic;
using UnityEngine;

// 文字の見切れの記録(2026-10-13、開発版だけ)。LocGUI.Label が「縮めた/折り返した/それでも収まらない/内側へ寄せた」表示を、
// 文ごとに1件(回数と最悪の値)だけ覚える。-qaTextFit の報告と、撮影の時の確認に使う。通常版では何もしない。
public static class TextFit
{
    public class Issue
    {
        public string text, where, kind; public int count, fontFrom, fontTo; public Rect rect; public Vector2 screen; public bool overflow;
    }
    // 画面の短い辺(縦画面でも横画面でも同じ大きさの文字にするための基準。以前は高さを使い、縦画面で文字が大きすぎて横へはみ出した)
    // 横画面は高さ(今まで通り)、縦画面は「幅×1.3」(高さで決めると横にはみ出し、短い辺だと小さすぎた)
    public static float Short => Legacy ? Screen.height : Mathf.Min(Screen.height, Screen.width * 1.3f);
    // 修正前の表示で撮り比べるため(開発版だけ -tsLegacy 1)。LocGUI の収め方/短い辺の基準/BONUS の枠を以前の形に戻す
    static int legacy = -1;
    public static bool Legacy
    {
        get
        {
            if (legacy < 0)
            {
                legacy = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = QaArgs.All;
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-tsLegacy" && a[i + 1] == "1") legacy = 1;
#endif
            }
            return legacy == 1;
        }
    }
    public static readonly Dictionary<string, Issue> Issues = new Dictionary<string, Issue>();
    static int enabled = -1; // static の初期化で Debug.isDebugBuild を読まない(2026-10-03 の落とし穴)。最初に使う時に決める
    public static bool Enabled { get { if (enabled < 0) enabled = Debug.isDebugBuild ? 1 : 0; return enabled == 1; } set { enabled = value ? 1 : 0; } }

    public static void Note(string text, Rect r, int from, int to, bool wrapped, bool stillOverflow)
    {
        if (!Enabled) return;
        string kind = stillOverflow ? "OVERFLOW" : wrapped ? "wrapped" : "shrunk";
        Add(text, r, kind, from, to, stillOverflow);
    }

    public static void NoteShift(string text, Rect r, float shift)
    {
        if (!Enabled) return;
        Add(text, r, "shifted", 0, 0, false);
    }

    static void Add(string text, Rect r, string kind, int from, int to, bool overflow)
    {
        string key = kind + "|" + text + "|" + Screen.width + "x" + Screen.height;
        if (Issues.TryGetValue(key, out var i)) { i.count++; if (to < i.fontTo) i.fontTo = to; return; }
        string where = "";
        try
        {
            var st = new System.Diagnostics.StackTrace(3, false);
            for (int f = 0; f < st.FrameCount && f < 4; f++)
            {
                var m = st.GetFrame(f).GetMethod();
                if (m == null) continue;
                string n = m.DeclaringType != null ? m.DeclaringType.Name + "." + m.Name : m.Name;
                if (n.StartsWith("LocGUI") || n.StartsWith("UiKit") || n.StartsWith("GUI.")) continue;
                where = n; break;
            }
        }
        catch { }
        Issues[key] = new Issue { text = text, where = where, kind = kind, count = 1, fontFrom = from, fontTo = to, rect = r, screen = new Vector2(Screen.width, Screen.height), overflow = overflow };
    }

    public static string Report()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var i in Issues.Values)
            sb.AppendLine($"{i.kind}\t{i.screen.x}x{i.screen.y}\t{i.where}\tfont {i.fontFrom}->{i.fontTo}\trect {i.rect.x:F0},{i.rect.y:F0},{i.rect.width:F0}x{i.rect.height:F0}\tx{i.count}\t{i.text.Replace("\n", "\n")}");
        return sb.ToString();
    }
}

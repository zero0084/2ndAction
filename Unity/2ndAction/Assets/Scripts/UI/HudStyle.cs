using UnityEngine;

// ラン中の HUD の見た目(2026-10-31、UI 全体の改善)。ホームの濃紺+金を引き継ぎ、戦闘の画面では豪華な飾り枠は使わない:
//  半透明の濃紺の板 + 細い金の線 + 角の小さな金の印。文字は濃い影付き(背景を薄くしても読める)。
//  板の濃さは設定「HUD の背景」(HudPrefs.PanelAlpha)で変わるが、文字と枠は薄くしない。
public static class HudStyle
{
    public const float Margin = 18f;     // 画面の端(セーフエリア)からの余白(設計の単位)
    public const float Gap = 8f;         // 板と板の間
    public const int LabelSize = 12, ValueSize = 17, BigSize = 24;
    public static readonly Color Label = new Color(0.78f, 0.82f, 0.92f, 0.92f);
    public static readonly Color Value = Color.white;
    public static Color Gold => UiTheme.Mile;
    static readonly Color Shadow = new Color(0f, 0f, 0f, 0.75f);

    public static void Panel(Rect r, float fill = 0.62f)
    {
        Color keep = GUI.color;
        float amb = keep.a;
        Color navy = UiTheme.Navy;
        GUI.color = new Color(navy.r, navy.g, navy.b, fill * HudPrefs.PanelAlpha * amb);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        // 細い金の線(背景を「最小」にしても枠は少し残す = どこまでが HUD か分かる)
        Color g = UiTheme.GoldEdge;
        float ea = Mathf.Lerp(0.38f, 0.7f, HudPrefs.PanelAlpha) * amb;
        GUI.color = new Color(g.r, g.g, g.b, ea);
        const float e = 1f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, e), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - e, r.width, e), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, e, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - e, r.y, e, r.height), Texture2D.whiteTexture);
        // 角の小さな印(左上と右下だけ。飾りは控えめに)
        GUI.color = new Color(g.r, g.g, g.b, Mathf.Min(1f, ea + 0.25f));
        float c = Mathf.Min(10f, r.height * 0.3f);
        GUI.DrawTexture(new Rect(r.x, r.y, c, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 2f, c), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - c, r.yMax - 2f, c, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - 2f, r.yMax - c, 2f, c), Texture2D.whiteTexture);
        GUI.color = keep;
    }

    // 板の中の区切りの細い線
    public static void Divider(float x, float y, float w)
    {
        Color keep = GUI.color;
        Color g = UiTheme.GoldEdge;
        GUI.color = new Color(g.r, g.g, g.b, 0.28f * keep.a);
        GUI.DrawTexture(new Rect(x, y, w, 1f), Texture2D.whiteTexture);
        GUI.color = keep;
    }

    // 帯(HP/EXP/BREAK)。枠付きの暗い下地 + 塗り
    public static void Bar(Rect r, float frac, Color fill, float backAlpha = 0.7f)
    {
        Color keep = GUI.color;
        float amb = keep.a;
        GUI.color = new Color(0.02f, 0.03f, 0.07f, backAlpha * amb);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        frac = Mathf.Clamp01(frac);
        if (frac > 0f)
        {
            GUI.color = new Color(fill.r, fill.g, fill.b, fill.a * amb);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * frac, r.height - 2f), Texture2D.whiteTexture);
            // 上半分を少し明るく(平らに見えないように)
            GUI.color = new Color(1f, 1f, 1f, 0.16f * amb);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * frac, Mathf.Max(1f, (r.height - 2f) * 0.4f)), Texture2D.whiteTexture);
        }
        Color g = UiTheme.GoldEdge;
        GUI.color = new Color(g.r, g.g, g.b, 0.45f * amb);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
        GUI.color = keep;
    }

    // 影付きの文字(LocGUI = 訳/字形の処理は今までの HUD と同じ)
    public static void Text(Rect r, string text, GUIStyle st, Color c)
    {
        Color keep = st.normal.textColor;
        st.normal.textColor = new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * c.a);
        LocGUI.Label(new Rect(r.x + 1f, r.y + 1.5f, r.width, r.height), text, st);
        st.normal.textColor = c;
        LocGUI.Label(r, text, st);
        st.normal.textColor = keep;
    }

    static GUIStyle label, value;
    public static GUIStyle LabelStyle(int size = LabelSize, TextAnchor a = TextAnchor.MiddleLeft)
    {
        if (label == null) label = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Overflow, wordWrap = false };
        label.fontSize = size; label.alignment = a; label.fontStyle = FontStyle.Normal; label.richText = true;
        return label;
    }
    public static GUIStyle ValueStyle(int size = ValueSize, TextAnchor a = TextAnchor.MiddleRight)
    {
        if (value == null) value = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Overflow, wordWrap = false };
        value.fontSize = size; value.alignment = a; value.fontStyle = FontStyle.Bold; value.richText = true;
        return value;
    }
    // 幅に入るまで文字を小さくする(枠の大きさは変えない)
    public static void Fit(GUIStyle st, string text, float width, int min = 9)
    {
        var gc = new GUIContent(Loc.Auto(text));
        float w = st.CalcSize(gc).x;
        if (w > width && w > 1f) st.fontSize = Mathf.Max(min, Mathf.FloorToInt(st.fontSize * width / w));
    }
}

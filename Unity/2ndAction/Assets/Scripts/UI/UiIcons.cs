using UnityEngine;

// 画面の共通アイコン(2026-10-20、エステルさんと相談して決めた絵)。Resources/UI/Icons/<名前>.png
// 決まり: 白 = 操作(一時停止・閉じる・矢印。これらはコードで描く)、金 = 価値/機能(MILE・歯車・星)、赤 = HP。
// 絵が無い時は null(呼ぶ側が今までのコードの描き方に戻る)
public static class UiIcons
{
    static Texture2D mile, heartFull, heartEmpty, gear, newBadge;
    static Sprite newBadgeSprite;
    static bool loaded;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        mile = Resources.Load<Texture2D>("UI/Icons/mile");
        heartFull = Resources.Load<Texture2D>("UI/Icons/heart_full");
        heartEmpty = Resources.Load<Texture2D>("UI/Icons/heart_empty");
        gear = Resources.Load<Texture2D>("UI/Icons/gear");
        newBadge = Resources.Load<Texture2D>("UI/Icons/new_badge");
    }

    public static Texture2D Mile { get { Load(); return mile; } }
    public static Texture2D HeartFull { get { Load(); return heartFull; } }
    public static Texture2D HeartEmpty { get { Load(); return heartEmpty; } }
    public static Texture2D Gear { get { Load(); return gear; } }
    // NEW の札(金橙の帯。文字は描いていない = 「NEW」は呼ぶ側が重ねる)。横:縦 は NewBadgeAspect
    public static Texture2D NewBadge { get { Load(); return newBadge; } }
    public static float NewBadgeAspect => newBadge != null ? newBadge.width / (float)newBadge.height : 2.3f;
    public static Sprite NewBadgeSprite
    {
        get
        {
            Load();
            if (newBadgeSprite == null && newBadge != null)
                newBadgeSprite = Sprite.Create(newBadge, new Rect(0, 0, newBadge.width, newBadge.height), new Vector2(0.5f, 0.5f), 100f);
            return newBadgeSprite;
        }
    }

    // 操作の記号の色(白銀)。一時停止・閉じる・矢印など、コードで描く記号はこの色と太さにそろえる
    public static readonly Color ControlColor = new Color(0.93f, 0.95f, 1f, 1f);

    // 一時停止の記号: ボタン r の中央に、角の丸い縦棒2本(高さ = ボタンの42%、太さ = 高さの28%、間 = 太さの0.9倍)
    public static void DrawPause(Rect r)
    {
        float h = Mathf.Min(r.width, r.height) * 0.42f, w = h * 0.28f, gap = w * 0.9f;
        float x0 = r.center.x - gap * 0.5f - w, y0 = r.center.y - h * 0.5f;
        Color c = ControlColor; c.a *= GUI.color.a;
        Vector4 rad = Vector4.one * (w * 0.35f);
        Color shadow = new Color(0f, 0f, 0f, 0.45f * GUI.color.a);
        for (int i = 0; i < 2; i++)
        {
            float x = x0 + i * (w + gap);
            GUI.DrawTexture(new Rect(x + 1f, y0 + 1.5f, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, shadow, Vector4.zero, rad);
            GUI.DrawTexture(new Rect(x, y0, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, rad);
        }
    }

    // 閉じるの記号: ボタン r の中央に、白銀の太い×(線の太さは一時停止の棒と同じ割合)。無効の時は薄く
    public static void DrawClose(Rect r, bool enabled = true)
    {
        float s = Mathf.Min(r.width, r.height) * 0.44f, w = s * 0.2f;
        Color c = ControlColor; c.a *= GUI.color.a * (enabled ? 1f : 0.4f);
        Color shadow = new Color(0f, 0f, 0f, 0.45f * c.a);
        Vector4 rad = Vector4.one * (w * 0.45f);
        Vector2 ctr = r.center;
        Matrix4x4 m = GUI.matrix;
        foreach (float ang in new[] { 45f, -45f })
        {
            GUI.matrix = m;
            GUIUtility.RotateAroundPivot(ang, ctr);
            GUI.DrawTexture(new Rect(ctr.x - s * 0.5f + 1f, ctr.y - w * 0.5f + 1.5f, s, w), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, shadow, Vector4.zero, rad);
            GUI.DrawTexture(new Rect(ctr.x - s * 0.5f, ctr.y - w * 0.5f, s, w), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, rad);
        }
        GUI.matrix = m;
    }

    // ダイアログの右上の閉じるボタン(今までの UiKit.Button(r, "×", …, false, false) と同じボタンの見た目に、記号だけ描く)
    public static bool CloseButton(Rect r, bool enabled = true)
    {
        bool hit = UiKit.Button(r, "", 24f, false, false, enabled);
        if (Event.current.type == EventType.Repaint) DrawClose(r, enabled);
        return hit;
    }

    // r の中に縦横比を保って描く(GUI.color の色が掛かる)
    public static void Draw(Rect r, Texture2D tex)
    {
        if (tex == null) return;
        GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
    }
}

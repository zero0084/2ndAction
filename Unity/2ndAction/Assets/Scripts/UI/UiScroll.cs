using UnityEngine;

// IMGUI の縦スクロール(2026-10-08、長い説明文/一覧の共通部品)。
//  ・指(マウス)のドラッグとタップを区別する: 押してから DragThreshold を越えて動いたらスクロール。その時はボタンの押下を取り消し、
//    離した時にボタンが押されない(=スクロールでカードの選択/画面を閉じる/背後の操作が起きない)。
//  ・ホイール、ゲームパッド(PadNav のスクロール要求)にも対応。続きがある時は右に細いバーを出す。
//  ・内容の鍵(言語/表示対象)が変わったら先頭へ戻す: Begin(..., key) に表示対象の ID を渡す(言語は自動で含める)。
//  使い方: s.Begin(view, contentHeight, key); …内容は (0,0) からの座標で描く… s.End(view, contentHeight);
public class UiScroll
{
    public float y;
    public const float DragThreshold = 10f;
    bool pressed, dragging;
    Vector2 pressPos; float lastY;
    string lastKey;
    public bool Dragging => dragging;
    public bool CanScroll { get; private set; }

    public void Reset() { y = 0f; pressed = dragging = false; }

    // 今のフレームでドラッグとして扱われた指か(タップで閉じる画面などが、閉じる前に確認する)
    public bool ConsumedPointer { get; private set; }

    public void Begin(Rect view, float contentH, string key = null)
    {
        string k = (key ?? "") + "|" + Loc.Current;
        if (k != lastKey) { lastKey = k; Reset(); }
        float max = Mathf.Max(0f, contentH - view.height);
        CanScroll = max > 0.5f;
        y = Mathf.Clamp(y, 0f, max);
        Event e = Event.current;
        if (e.type == EventType.Layout) ConsumedPointer = false;
        if (CanScroll)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (view.Contains(e.mousePosition)) { pressed = true; dragging = false; pressPos = e.mousePosition; lastY = e.mousePosition.y; }
                    break;
                case EventType.MouseDrag:
                    if (pressed)
                    {
                        if (!dragging && Mathf.Abs(e.mousePosition.y - pressPos.y) > DragThreshold) dragging = true;
                        if (dragging)
                        {
                            y = Mathf.Clamp(y - (e.mousePosition.y - lastY), 0f, max);
                            lastY = e.mousePosition.y;
                            GUIUtility.hotControl = 0; // 押していたボタンを取り消す(離しても押されない)
                            ConsumedPointer = true;
                            e.Use();
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (dragging) { ConsumedPointer = true; e.Use(); }
                    pressed = dragging = false;
                    break;
                case EventType.ScrollWheel:
                    if (view.Contains(e.mousePosition)) { y = Mathf.Clamp(y + e.delta.y * 20f, 0f, max); e.Use(); }
                    break;
                case EventType.Layout:
                    int req = PadNav.ScrollRequestFor(PadNav.ToScreen(view));
                    if (req != 0) y = Mathf.Clamp(y + req * view.height * 0.5f, 0f, max);
                    break;
            }
        }
        else { pressed = dragging = false; }
        PadNav.PushClip(view);
        GUI.BeginGroup(view);
        GUI.BeginGroup(new Rect(0f, -y, view.width, Mathf.Max(view.height, contentH) + y));
    }

    public void End(Rect view, float contentH)
    {
        GUI.EndGroup();
        GUI.EndGroup();
        PadNav.PopClip();
        float max = contentH - view.height;
        if (max > 1f)
        {
            float barH = Mathf.Max(28f, view.height * view.height / contentH);
            float by = view.y + (view.height - barH) * (y / max);
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f * keep.a);
            GUI.DrawTexture(new Rect(view.xMax - 6f, view.y, 5f, view.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.45f, 0.75f * keep.a);
            GUI.DrawTexture(new Rect(view.xMax - 6f, by, 5f, barH), Texture2D.whiteTexture);
            // まだ下に続く時は、下端に小さな ▼
            if (y < max - 1f)
            {
                var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 14 };
                st.normal.textColor = new Color(1f, 0.85f, 0.45f, 0.8f * keep.a);
                GUI.Label(new Rect(view.x, view.yMax - 18f, view.width - 10f, 18f), "▼", st);
            }
            GUI.color = keep;
        }
    }

    // 長い文章を幅で折り返して、収まらなければスクロールして最後まで読めるように描く(文字は小さくしない)
    public void Text(Rect view, string text, GUIStyle style, string key = null)
    {
        string t = Loc.Auto(text ?? "");
        var st = new GUIStyle(style) { wordWrap = true };
        float contentH = st.CalcHeight(new GUIContent(t), view.width - 12f);
        if (contentH <= view.height)
        {
            // 収まる時は今までどおり(揃え方もそのまま)。スクロールの状態だけ合わせる
            string k = (key ?? text ?? "") + "|" + Loc.Current;
            if (k != lastKey) { lastKey = k; Reset(); }
            CanScroll = false;
            GUI.Label(view, t, st);
            return;
        }
        Begin(view, contentH, key ?? text);
        GUI.Label(new Rect(0f, 0f, view.width - 12f, contentH), t, st);
        End(view, contentH);
    }
}

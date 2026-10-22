using UnityEngine;
using UnityEngine.UI;

// 画面の色と文字の決まり(2026-10-19 見た目の完成度向上: 画面ごとにばらばらだった「金」(約6種)を2種へ)。
//  縁の金 = 枠・区切り・選択中の縁(落ち着いた金) / 文字の金 = 題名・押せる強調の文字(明るい金)
// 新しく画面を作る時・直す時はここの色を使う(数値を直接書かない)。IMGUI と uGUI で同じ値。
// 2026-10-22 画面側の完成度向上 第2段階: 共通の部品(板・ボタン・スライダー・選択中)と状態(未解放/警告/成功)、
//  カテゴリ7色・レア度5色、文字の役割(Title/Heading/Body/Caption/Button/Warning)を追加。値は今の見た目と同じものから
public static class UiTheme
{
    // ---- 地・板 ----
    public static readonly Color Navy = new Color(0.06f, 0.08f, 0.17f);        // 板の地
    public static readonly Color NavyDeep = new Color(0.03f, 0.04f, 0.09f);    // 押している間・暗幕
    public static readonly Color PanelFill = new Color(0.05f, 0.06f, 0.13f);   // 飾り枠の板(OrnateUi)の地
    public static readonly Color ButtonFill = new Color(0.07f, 0.09f, 0.19f);  // 標準ボタンの地
    public static readonly Color ButtonHoverFill = new Color(0.11f, 0.13f, 0.26f);

    // ---- 金 ----
    public static readonly Color GoldEdge = new Color(0.83f, 0.68f, 0.32f);    // 縁の金
    public static readonly Color GoldText = new Color(1f, 0.88f, 0.55f);       // 文字の金(題名・強調)
    public static readonly Color GoldBright = new Color(1f, 0.82f, 0.38f);     // 選択中の縁(太い)
    public static readonly Color GoldHover = new Color(1f, 0.9f, 0.6f);        // 上に乗せた時/強調の文字
    public static readonly Color GoldHot = new Color(1f, 0.84f, 0.42f);        // 上に乗せた時/押している間の縁
    public static readonly Color GoldPressed = new Color(1f, 0.86f, 0.5f);     // 押している間の文字
    public static readonly Color Mile = new Color(1f, 0.85f, 0.35f);           // MILE・数値の金(HUD と同じ)

    // ---- 文字 ----
    public static readonly Color Text = new Color(0.96f, 0.96f, 1f);           // 本文
    public static readonly Color TextButton = new Color(0.95f, 0.95f, 1f);     // ボタンの文字
    public static readonly Color TextBack = new Color(0.92f, 0.93f, 0.98f);    // 戻るボタンの文字(UiConventions)
    public static readonly Color TextSub = new Color(0.78f, 0.8f, 0.88f);      // 補足
    public static readonly Color TextSubBlue = new Color(0.85f, 0.9f, 1f);     // 補足(青み、説明文)

    // ---- 状態 ----
    public static readonly Color Disabled = new Color(0.62f, 0.64f, 0.7f);     // 使えない
    public static readonly Color Locked = new Color(0.5f, 0.5f, 0.55f);        // 未解放(灰)
    public static readonly Color Danger = new Color(1f, 0.45f, 0.38f);         // 取り消せない操作の注意
    public static readonly Color Warning = new Color(1f, 0.6f, 0.5f);          // 注意(読めば分かる程度)
    public static readonly Color Success = new Color(0.55f, 0.95f, 0.6f);      // 成功・増えた
    public static readonly Color SelectedTint = new Color(1f, 0.78f, 0.3f, 0.16f); // 選択中の淡い金の敷き

    // ---- スライダー ----
    public static readonly Color SliderTrack = new Color(0.07f, 0.09f, 0.18f);
    public static readonly Color SliderFill = new Color(0.95f, 0.8f, 0.4f);
    public static readonly Color SliderKnob = new Color(1f, 0.92f, 0.65f);
    public static readonly Color SliderKnobInner = new Color(0.12f, 0.15f, 0.3f);

    // ---- カテゴリ7色(アイコンは形で見分ける金色。色はタブの下線・小さな印など補助だけに使う) ----
    public static Color Category(CardCategory c)
    {
        switch (c)
        {
            case CardCategory.Attack: return new Color(1f, 0.5f, 0.35f);
            case CardCategory.Defense: return new Color(0.45f, 0.7f, 1f);
            case CardCategory.Movement: return new Color(0.4f, 0.9f, 0.75f);
            case CardCategory.Growth: return new Color(0.7f, 0.95f, 0.4f);
            case CardCategory.Heal: return new Color(1f, 0.55f, 0.7f);
            case CardCategory.Special: return new Color(0.8f, 0.55f, 1f);
            case CardCategory.Risk: return new Color(1f, 0.3f, 0.3f);
            default: return GoldText;
        }
    }

    // ---- レア度5色(★1〜5)。ガチャの値(★4 青 / ★5 橙)を基準に、★1〜3 を足した ----
    public static Color Rarity(int r)
    {
        switch (Mathf.Clamp(r, 1, 5))
        {
            case 1: return new Color(0.8f, 0.82f, 0.88f);
            case 2: return new Color(0.55f, 0.9f, 0.6f);
            case 3: return new Color(1f, 0.85f, 0.4f);
            case 4: return new Color(0.65f, 0.8f, 1f);
            default: return new Color(1f, 0.65f, 0.2f);
        }
    }

    // ---- 文字の役割 ----
    // 大きさは「720p の画面(IMGUI の基準)」の値。uGUI(1920x1080 基準)では ×1.5 で使う(UguiSize)。
    public enum TextRole { Title, Heading, Body, Caption, Button, Warning, Number }
    public struct TextStyle
    {
        public float size; public Color color; public bool bold; public float lineSpacing;
        public TextStyle(float size, Color color, bool bold, float lineSpacing) { this.size = size; this.color = color; this.bold = bold; this.lineSpacing = lineSpacing; }
    }
    public static TextStyle Style(TextRole role)
    {
        switch (role)
        {
            case TextRole.Title: return new TextStyle(28f, GoldText, true, 1f);
            case TextRole.Heading: return new TextStyle(20f, GoldText, true, 1f);
            case TextRole.Body: return new TextStyle(16f, Text, false, 1.15f);
            case TextRole.Caption: return new TextStyle(13f, TextSub, false, 1.1f);
            case TextRole.Button: return new TextStyle(18f, TextButton, true, 1f);
            case TextRole.Warning: return new TextStyle(15f, Warning, true, 1.1f);
            default: return new TextStyle(20f, Mile, true, 1f); // Number
        }
    }
    public const float UguiScale = 1.5f;
    public static int UguiSize(TextRole role) => Mathf.RoundToInt(Style(role).size * UguiScale);

    // uGUI の Text に役割を当てる(大きさ・色・太さ・行間)。sizeMul で画面ごとの微調整
    public static void Apply(Text t, TextRole role, float sizeMul = 1f)
    {
        if (t == null) return;
        var s = Style(role);
        t.fontSize = Mathf.RoundToInt(s.size * UguiScale * sizeMul);
        if (t.resizeTextForBestFit) t.resizeTextMaxSize = t.fontSize;
        t.color = s.color;
        t.fontStyle = s.bold ? FontStyle.Bold : FontStyle.Normal;
        t.lineSpacing = s.lineSpacing;
    }

    // IMGUI の文字の見た目(scale = 画面の倍率。使い回しの GUIStyle なので、同時に違う見た目が要る時は new GUIStyle(..) で複製)
    static GUIStyle im;
    public static GUIStyle Im(TextRole role, float scale, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
    {
        if (im == null) im = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip };
        var s = Style(role);
        im.fontSize = Mathf.RoundToInt(s.size * scale);
        im.fontStyle = s.bold ? FontStyle.Bold : FontStyle.Normal;
        im.normal.textColor = s.color;
        im.alignment = anchor; im.wordWrap = wrap;
        return im;
    }
}

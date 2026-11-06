using UnityEngine;
using UnityEngine.UI;

// キャラ紹介文の収め方(2026-11-07、キャラ文言の30言語の確認)。
//  紹介文は元の文(英語/日本語)の改行のまま訳されている。訳の1行が枠より長いと、その行だけが折り返されて
//  1語だけの行(「musuh.」「врага.」など)が並び、縦画面ではスクロールになる言語もあった(36 言語×向きの組で確認)。
//  ① 書いた改行のまま収まる(どの行も折り返さない)なら、そのまま(日本語/英語/中国語/韓国語など)
//  ② 折り返す行があり、単語の間に空白を入れる言語(右から左の言語を除く)なら、改行を空白にして1つの段落として流し込む
//     (自然な位置で折り返す。右から左の言語は RtlTextWrap が行ごとに並べ替えるのでつながない。空白で区切らない
//      タイ語/ラオス語/クメール語/ビルマ語/日本語/中国語は語の途中で切れるのでつながない)
//  ③ それでも枠(高さ)を超える時だけ、文字を少し小さくする(最小 MinFlavorFontSize)。超える分はスクロールのまま
//  名前/一行(1行の枠): 幅に入らない時だけ文字を小さくする(縦画面でフィリピン語/タミル語の名前の2行目が切れていた)
public partial class CharacterSelectUI
{
    const int MinFlavorFontSize = 18;
    int flavorBaseFontSize;
    CharacterDefinition flavorDef;
    public static bool FlavorFitDisabled; // 比較用(開発版の確認)
    public bool FlavorJoined { get; private set; }
    TextGenerator fitGen;
    TextGenerator FitGen => fitGen ??= new TextGenerator(); // 測るだけ(Text 自身の生成結果には触れない)

    static bool NoSpaceScript(string code) => code == "ja" || code == "zh-Hans" || code == "zh-Hant" || code == "th" || code == "lo" || code == "km" || code == "my";

    int titleBaseFontSize, subtitleBaseFontSize;
    const float MinTitleScale = 0.68f, MinSubtitleScale = 0.8f;

    // 1行の枠の文字: 幅に入るまで小さくする(元の大きさ × minScale まで)
    void FitOneLine(Text t, ref int baseSize, float minScale)
    {
        if (t == null) return;
        if (baseSize <= 0) baseSize = t.fontSize;
        t.fontSize = baseSize;
        float w = t.rectTransform.rect.width;
        if (w < 10f || string.IsNullOrEmpty(t.text)) return;
        int min = Mathf.Max(8, Mathf.RoundToInt(baseSize * minScale));
        int fs = baseSize;
        while (fs > min && OneLineWidth(t, fs) > w) fs--;
        t.fontSize = fs;
    }

    float OneLineWidth(Text t, int fs)
    {
        var st = t.GetGenerationSettings(Vector2.zero);
        st.fontSize = fs;
        return FitGen.GetPreferredWidth(t.text, st) / Mathf.Max(0.01f, t.pixelsPerUnit);
    }

    void SetFlavor(CharacterDefinition def)
    {
        flavorDef = def;
        if (!FlavorFitDisabled) { FitOneLine(titleText, ref titleBaseFontSize, MinTitleScale); FitOneLine(subtitleText, ref subtitleBaseFontSize, MinSubtitleScale); }
        if (flavorText == null) return;
        if (flavorBaseFontSize <= 0) flavorBaseFontSize = flavorText.fontSize;
        string shown = Loc.Auto(def.flavorText);
        flavorText.fontSize = flavorBaseFontSize;
        FlavorJoined = false;
        if (FlavorFitDisabled) { flavorText.text = shown; return; }
        float w = flavorText.rectTransform.rect.width;
        var scroll = flavorText.GetComponentInParent<UguiScrollText>();
        float viewH = scroll != null && scroll.Viewport != null ? scroll.Viewport.rect.height : flavorText.rectTransform.rect.height;
        if (w < 10f || viewH < 10f) { flavorText.text = shown; return; }
        string text = shown;
        int authored = shown.Split('\n').Length;
        if (!Loc.IsRtl && !NoSpaceScript(Loc.Current) && authored > 1 && Lines(shown, w, flavorBaseFontSize) > authored)
        {
            text = shown.Replace("\r", "").Replace("\n", " ");
            FlavorJoined = true;
        }
        int fs = flavorBaseFontSize;
        while (fs > MinFlavorFontSize && Height(text, w, fs) + 4f > viewH) fs--;
        flavorText.fontSize = fs;
        flavorText.text = text;
        if (scroll != null) scroll.ResetForNewContent(); // 文字の大きさだけ変わった時も高さを測り直す
    }

    // 向き/幅が変わった時(ApplyOrientationLayout)
    void RefreshFlavorFit()
    {
        if (flavorDef != null) SetFlavor(flavorDef);
    }

    TextGenerationSettings FlavorSettings(float w, int fs)
    {
        var st = flavorText.GetGenerationSettings(new Vector2(w, 0f));
        st.fontSize = fs; // 画面の倍率は settings.scaleFactor が持つ
        st.generateOutOfBounds = true;
        return st;
    }

    int Lines(string s, float w, int fs)
    {
        var gen = FitGen;
        gen.Populate(s, FlavorSettings(w, fs));
        return gen.lineCount;
    }

    float Height(string s, float w, int fs) => FitGen.GetPreferredHeight(s, FlavorSettings(w, fs)) / Mathf.Max(0.01f, flavorText.pixelsPerUnit);
}

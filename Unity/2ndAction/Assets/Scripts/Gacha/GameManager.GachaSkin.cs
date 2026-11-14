using System.Collections.Generic;
using UnityEngine;

// ガチャのマップ別の見た目(2026-11-06)の GameManager 側のつなぎ。見た目の本体は GachaSkinFx。
//  ・揺れ(回している間)に、見せる順番待ちの先頭のカードの土地で予兆 → 結果を開いた時に排出・台の見た目の切り替え
//  ・台の下の「分類の列」(2026-11-14 から押して選ぶ。GameManager.GachaCategory.cs): 選んでいる分類を強調し、その分類のカードだけを引く。
//    まだ候補のカードが無い分類は暗く、マップ自体が未解放なら鍵(中身のカードは出さない = ネタバレしない、選べない)
public partial class GameManager
{
    readonly GachaSkinFx gachaSkinFx = new GachaSkinFx();
    public GachaSkinFx GachaSkinState => gachaSkinFx; // 確認用

    static readonly CardMapCategory[] GachaLands = { CardMapCategory.Common, CardMapCategory.Wilderness, CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon };
    readonly int[] gachaLandCounts = new int[5];
    float gachaLandCountsAt = -99f;

    void GachaSkinTick()
    {
        gachaSkinFx.Update(Time.unscaledDeltaTime);
        if (gachaMachineShakeTimer > 0f && gachaRevealQueue.Count > 0 && !gachaResultOpen)
        {
            var head = gachaRevealQueue[0];
            gachaSkinFx.OnSpin(head, CardMapCategories.Of(head), gachaMachineShakeTimer);
        }
    }

    // 候補のカードの数(土地ごと)。重いので 2 秒ごと / 引いた後に数え直す
    void RefreshGachaLandCounts(bool force = false)
    {
        if (!force && Time.unscaledTime - gachaLandCountsAt < 2f) return;
        gachaLandCountsAt = Time.unscaledTime;
        System.Array.Clear(gachaLandCounts, 0, gachaLandCounts.Length);
        foreach (var c in BuildGachaPool()) if (c != null) gachaLandCounts[(int)CardMapCategories.Of(c)]++;
    }
    public int GachaLandCount(CardMapCategory k) { RefreshGachaLandCounts(); return gachaLandCounts[(int)k]; }

    public static bool GachaLandMapOpen(CardMapCategory k)
    {
        switch (k)
        {
            case CardMapCategory.Cave: return UnlockRules.IsStageUnlocked(CardMapCategories.StageIdOf(k));
            case CardMapCategory.Sky: return UnlockRules.IsStageUnlocked(CardMapCategories.StageIdOf(k));
            case CardMapCategory.LastDungeon: return ProgressStats.FinalDungeonUnlocked;
            default: return true;
        }
    }

    // 2026-11-14 夜(マスターの訂正): 分類を選ぶ列/帯は外した。抽選の対象は最後に出発したマップ(GameManager.GachaCategory.cs)。
    //  今どの分類のカードが出るかは、横画面は「ガチャ」の札の2行目(GachaSignSubLine)+ 札の左の「提供割合」、
    //  縦画面はガチャのボタンの列の上の帯(DrawGachaInfoStrip、2026-11-15 マスター決定の案B)。
    public Rect LastGachaLandRowRect { get; private set; } // 縦画面の帯(確認用)
    public Rect LastGachaOddsButtonRect { get; private set; } // 「提供割合」のボタン(確認用)
    Rect SignRectNowOrPrev(int i) => LastHomeSignRects[i].width > 0f ? LastHomeSignRects[i] : prevHomeSignHitRects[i];
    public static float Dp(float dp) => dp * (Screen.dpi > 1f ? Screen.dpi / 160f : 1.5f);

    // 案内の色: 普段は金、引けない時/断った理由は明るい橙
    Color GachaLineColor(bool warn, float fade) => warn ? new Color(1f, 0.74f, 0.6f, fade) : new Color(1f, 0.92f, 0.66f, fade);
    string GachaLineNow(out bool warn)
    {
        EnsureGachaCategory();
        bool noteOn = !string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil;
        bool empty = GachaCategoryEmpty;
        warn = noteOn || empty;
        return Loc.Auto(noteOn ? gachaCategoryNote : GachaTargetLine(gachaCategory, empty));
    }

    // 「Aのカード 70%・共通 30%」を区切りの記号の所で2行に(記号は消す)。区切りが見つからない時はそのまま(折り返しに任せる)
    static readonly string[] MixSeparators = { "・", " · ", "·", " / ", "／", "、", "，", ", ", " - ", " – " };
    static string SplitMixLine(string line)
    {
        var p = SplitMixParts(line);
        return p == null ? line : p[0] + "\n" + p[1];
    }
    static string[] SplitMixParts(string line)
    {
        foreach (var sep in MixSeparators)
        {
            int i = line.IndexOf(sep, System.StringComparison.Ordinal);
            if (i > 0 && i + sep.Length < line.Length) return new[] { line.Substring(0, i).TrimEnd(), line.Substring(i + sep.Length).TrimStart() };
        }
        return null;
    }

    // 右から左の言語: 並べ替える前の文(元の順)を区切りで2つに分けてから、それぞれを並べ替えて2行に(エステル: 他の言語と同じ分け方)
    string RtlMixLine(float width, GUIStyle st)
    {
        int cs = Mathf.RoundToInt(GachaCommonShare * 100f);
        string raw = Loc.Src(Loc.Plain(LocGacha.MixLine), Loc.AutoPlain(CardMapCategories.LabelSrc(gachaCategory)), 100 - cs, cs);
        var p = SplitMixParts(raw);
        if (p == null) return null;
        return LocGUI.RtlFix(Loc.Shape(p[0]), width, st) + "\n" + LocGUI.RtlFix(Loc.Shape(p[1]), width, st);
    }

    // 「提供割合」の小さなボタン(押すと GachaOddsPanel)
    void DrawGachaOddsButton(Rect r, float fs, bool interactable)
    {
        LastGachaOddsButtonRect = r;
        if (UiKit.Button(r, LocGacha.Odds, fs, false, false, true) && interactable) { GachaOddsPanel.Open(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide); }
    }

    // 縦画面(案B): ガチャのボタン(と、その上の「広告で1回引く」)と同じ幅・同じ右端の帯を、その真上に置く。
    //  1行目 = マップの紋 + 「荒野のカード 70%・共通 30%」(引けない時は理由と条件)、2行目 = 「提供割合」。
    //  下のボタンと同じ列にそろえて「ガチャのまとまり」として読めるようにする(帯の左に金の縦線)。広告の案内(adGachaNote)はこの帯の上に出す
    void DrawGachaInfoStrip(Rect gachaBtn, float fade, bool interactable)
    {
        if (fade < 0.05f || gachaBtn.width < 1f) { LastGachaLandRowRect = Rect.zero; return; }
        float s = GachaUiScale;
        float w = Mathf.Max(gachaBtn.width, 190f * s);
        float x = Mathf.Clamp(gachaBtn.xMax - w, SafeLeft() + 8f, Screen.width - SafeRight() - 8f - w);
        float bottom = AdGachaAvailable ? gachaBtn.y - 54f * s - 8f - 5f : gachaBtn.y - 8f; // エステル: 広告ボタンとの間に 4〜6px
        string line = GachaLineNow(out bool warn);
        bool empty = GachaCategoryEmpty;
        string hint = empty ? GachaMapUnlockHint(gachaCategory) : "";
        if (empty) { line = Loc.T(LocGacha.NoCardsMap); if (!string.IsNullOrEmpty(hint)) line += "\n" + Loc.F(LocGacha.Condition, hint); warn = true; } // 引けない時はいつも理由と条件の2行
        string condLine = empty && !string.IsNullOrEmpty(hint) ? Loc.F(LocGacha.Condition, hint) : null;
        float fs = Mathf.Round(14f * s);
        float em = fs * 1.5f, pad = fs * 0.5f;
        var st = new GUIStyle(UiKit.Label(fs, TextAnchor.MiddleLeft, true, GachaLineColor(warn, fade))) { wordWrap = true };
        float textW = w - pad * 3f - em;
        // エステル: 1行に入らない時は「荒野のカード 70%」「共通 30%」のまとまりで改行(区切りの記号を行末に残さない)
        // 右から左の言語: 表示用に並べ替えた文を分けると行の順番が入れ替わる → 元の順で幅に合わせて行に分けてから並べ替える(LocGUI.RtlFix)
        if (Loc.IsRtl)
        {
            line = empty ? LocGUI.RtlFix(Loc.T(LocGacha.NoCardsMap), textW, st) + (condLine != null ? "\n" + LocGUI.RtlFix(condLine, textW, st) : "")
                         : RtlMixLine(textW, st) ?? LocGUI.RtlFix(line, textW, st);
        }
        else if (!empty && !line.Contains("\n") && st.CalcSize(new GUIContent(line)).x > textW) line = SplitMixLine(line);
        float lineH = Mathf.Max(em, st.CalcHeight(new GUIContent(line), textW));
        float btnH = Mathf.Max(Dp(36f), fs * 2.1f);
        float h = pad + lineH + pad * 0.6f + btnH + pad;
        var back = new Rect(x, bottom - h, w, h);
        LastGachaLandRowRect = back;
        Color keep = GUI.color;
        GUI.color = new Color(0.03f, 0.05f, 0.12f, 0.82f * fade);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        // 下のボタンの列と同じまとまりに見せる金の縦線(帯の左端から、ガチャのボタンの上端まで)
        GUI.color = new Color(0.95f, 0.78f, 0.4f, 0.85f * fade);
        GUI.DrawTexture(new Rect(back.x, back.y, Mathf.Max(2f, 3f * s), gachaBtn.y - back.y), Texture2D.whiteTexture);
        GUI.color = keep;
        GachaSkinFx.DrawEmblem(new Rect(back.x + pad, back.y + pad + (lineH - em) * 0.5f, em, em), gachaCategory, false, fade);
        GUI.Label(new Rect(back.x + pad * 2f + em, back.y + pad, textW, lineH), line, st);
        DrawGachaOddsButton(new Rect(back.x + pad * 2f + em, back.yMax - pad - btnH, w - pad * 3f - em, btnH), Mathf.Round(13f * s), interactable);
        GUI.color = keep;
    }

    // 対象のカードが無い分類: ガチャ/広告のボタンを「引けない」見た目に(押しても引かない・何も使わない)
    public bool GachaCategoryEmpty { get { EnsureGachaCategory(); RefreshGachaLandCounts(); return gachaLandCounts[(int)gachaCategory] == 0; } }
    void DimIfGachaEmpty(Rect r, float fade)
    {
        if (!GachaCategoryEmpty || r.width < 1f) return;
        Color keep = GUI.color;
        GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.55f * fade);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = keep;
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
public partial class GameManager
{
    // 確認用(-qaGachaSkin): 支払い/抽選なしで、指定のカードを「引いた」順番待ちへ(見せ方だけ)
    public void DebugGachaInject(CardDefinition c)
    {
        if (c == null) return;
        gachaRevealQueue.Add(c); gachaRevealNewQ.Add(true);
        if (!gachaResultOpen && gachaMachineShakeTimer <= 0f) gachaMachineShakeTimer = gachaMachineShakeDuration;
    }
    public void DebugGachaClose() { CloseGachaResult(); gachaRevealQueue.Clear(); gachaRevealNewQ.Clear(); gachaMachineShakeTimer = 0f; }
    public void DebugGachaSetSkin(CardMapCategory k) => gachaSkinFx.DebugSetSkin(k);
    public Rect DebugGachaMachineRectNow => lastGachaMachineRect;
}
#endif

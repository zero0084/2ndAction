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

    // 分類の列(2026-11-14 から押して選ぶ。エステルと相談した形): 紋 + 分類名を5つ横並び。選んでいる分類は金の枠と光。
    //  未解禁(マップ未解放)は鍵で、押すと「この分類はまだ解禁されていません」(選べない)。結果の確認中は出さない。
    //  ・横画面: 「ガチャ」の札の左。案内(「天空のカードだけが出ます」)は札の2行目(GachaSignSubLine)
    //  ・縦画面: 下のガチャ/広告のボタンの上に、画面の幅いっぱいの帯(見出し「ガチャの分類」/ 紋 + 名前 / 案内 2 行まで)
    //  紋の押せる範囲は 48dp 以上(Android の推奨)。隣と重ならない
    public Rect LastGachaLandRowRect { get; private set; }
    public Rect[] LastGachaLandCellRects { get; } = new Rect[5]; // 確認用
    Rect SignRectNowOrPrev(int i) => LastHomeSignRects[i].width > 0f ? LastHomeSignRects[i] : prevHomeSignHitRects[i];
    public static float Dp(float dp) => dp * (Screen.dpi > 1f ? Screen.dpi / 160f : 1.5f);
    const float LandGap = 0.42f; // 横画面の紋の間隔(紋の大きさに対する割合)。名前(長い言語)の幅を取る
    public static float GachaLandRowWidth(float unit) => unit * 5f + unit * LandGap * 4f + unit * 0.8f;

    // 紋 1 つ(r = 紋の枠、nameH = 名前の行の高さ)
    void DrawGachaCell(int i, Rect r, float nameH, float fade, bool interactable, float nameW = 0f)
    {
        var k = GachaLands[i];
        bool open = GachaLandMapOpen(k);
        bool has = gachaLandCounts[(int)k] > 0;
        bool cur = gachaCategory == k;
        Color keep = GUI.color;
        float a = !open ? 0.9f : cur ? 1f : has ? 0.7f : 0.4f;
        if (cur && open)
        {
            Color ac = GachaSkinFx.Accent(k);
            GUI.color = new Color(ac.r, ac.g, ac.b, 0.45f * fade);
            GUI.DrawTexture(new Rect(r.x - r.width * 0.25f, r.y - r.height * 0.25f, r.width * 1.5f, r.height * 1.5f), GachaSkinFx.GlowTex);
            // 選んでいる印: 金の枠(光が弱い瞬間も残る)
            GUI.color = new Color(1f, 0.85f, 0.45f, 0.95f * fade);
            float t = Mathf.Max(2f, r.width * 0.06f);
            Rect f = new Rect(r.x - t, r.y - t, r.width + t * 2f, r.height + t * 2f + nameH);
            GUI.DrawTexture(new Rect(f.x, f.y, f.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(f.x, f.yMax - t, f.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(f.x, f.y, t, f.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(f.xMax - t, f.y, t, f.height), Texture2D.whiteTexture);
        }
        GUI.color = keep;
        float pad = r.width * 0.12f;
        GachaSkinFx.DrawEmblem(new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height - pad * 2f), k, !open, fade * a);
        // 名前(長い言語は小さくして1行に収める)
        string name = Loc.T(CardMapCategories.LabelSrc(k));
        var ns = UiKit.Label(Mathf.Round(nameH * 0.72f), TextAnchor.MiddleCenter, true, cur && open ? new Color(1f, 0.9f, 0.6f, fade) : new Color(0.86f, 0.88f, 0.94f, fade * (open ? 1f : 0.6f)));
        // 名前の幅 = 紋の間隔いっぱい(隣の名前と重ならない)。入らない長い言語は字を小さくして1行に(切らない)
        if (nameW <= 0f) nameW = r.width * 1.18f;
        string shown = Loc.Auto(name);
        for (int g = 0; g < 12 && ns.fontSize > 8 && ns.CalcSize(new GUIContent(shown)).x > nameW - 2f; g++) ns.fontSize = Mathf.Max(8, Mathf.FloorToInt(ns.fontSize * 0.9f));
        ns.clipping = TextClipping.Overflow;
        GUI.Label(new Rect(r.center.x - nameW * 0.5f, r.yMax, nameW, nameH), shown, ns);
        // 押せる範囲: 紋 + 名前、横は名前の幅(隣と重ならない)。48dp 以上(Android の推奨)
        Rect hit = new Rect(r.center.x - Mathf.Max(r.width, nameW) * 0.5f, r.y, Mathf.Max(r.width, nameW), r.height + nameH);
        LastGachaLandCellRects[i] = hit;
        if (interactable && fade > 0.99f && ((PadNav.Button(hit) | GUI.Button(hit, GUIContent.none, GUIStyle.none))))
            SelectGachaCategory(k);
        GUI.color = keep;
    }

    // 横画面: 札の左(topCenter = 列の上の中央)
    void DrawGachaLandRow(Vector2 topCenter, float unit, float fade, bool interactable, bool withLine = true)
    {
        if (!GachaSkinFx.Enabled || fade < 0.05f) { LastGachaLandRowRect = Rect.zero; return; }
        RefreshGachaLandCounts();
        EnsureGachaCategory();
        float gap = unit * LandGap, nameH = Mathf.Max(16f, unit * 0.4f);
        float w = GachaLandRowWidth(unit), h = unit + nameH + unit * 0.24f;
        float x = Mathf.Clamp(topCenter.x - w * 0.5f, SafeLeft() + 4f, Screen.width - SafeRight() - w - 4f);
        var back = new Rect(x, topCenter.y, w, h);
        LastGachaLandRowRect = back;
        Color keep = GUI.color;
        GUI.color = new Color(0.03f, 0.05f, 0.12f, 0.7f * fade);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = keep;
        for (int i = 0; i < GachaLands.Length; i++)
            DrawGachaCell(i, new Rect(back.x + unit * 0.4f + i * (unit + gap), back.y + unit * 0.1f, unit, unit), nameH, fade, interactable, unit + gap * 0.8f);
    }

    // 縦画面: 下のボタンの上の帯。bottom = 帯の下端
    public Rect LastGachaPanelRect { get; private set; }
    void DrawGachaCategoryPanel(float bottom, float fade, bool interactable)
    {
        if (!GachaSkinFx.Enabled || fade < 0.05f) { LastGachaLandRowRect = Rect.zero; return; }
        RefreshGachaLandCounts();
        EnsureGachaCategory();
        float L = SafeLeft() + 12f, R = Screen.width - SafeRight() - 12f, aw = R - L;
        float unit = Mathf.Clamp(Mathf.Max(Dp(48f), aw * 0.13f), 40f, aw / 6.2f);
        float gap = (aw - unit * 5f) / 6f;
        float headH = Mathf.Round(unit * 0.36f), nameH = Mathf.Round(unit * 0.34f), lineH = Mathf.Round(unit * 0.33f);
        bool empty = gachaLandCounts[(int)gachaCategory] == 0;
        bool noteOn = !string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil;
        string line = noteOn ? gachaCategoryNote : GachaTargetLine(gachaCategory, empty);
        var lst = new GUIStyle(UiKit.Label(Mathf.Round(lineH * 0.8f), TextAnchor.UpperCenter, true, noteOn || empty ? new Color(1f, 0.74f, 0.6f, fade) : new Color(1f, 0.92f, 0.66f, fade))) { wordWrap = true };
        float infoH = Mathf.Min(lineH * 2.4f, lst.CalcHeight(new GUIContent(Loc.Auto(line)), aw - 16f) + 4f);
        float h = headH + unit * 0.12f + unit + nameH + unit * 0.12f + infoH + unit * 0.12f;
        // 上の「出発」(続きから/はじめから)の札と重ならない(エステル)。足りなければ紋を少し小さく(48dp までは保つ)、それでも足りなければ見出しを省く
        float minTop = 0f;
        for (int si = 4; si <= 5; si++) { var sr = SignRectNowOrPrev(si); if (sr.width > 1f && sr.yMax < bottom) minTop = Mathf.Max(minTop, sr.yMax + Mathf.Max(8f, Dp(6f))); }
        if (minTop > 0f)
        {
            for (int guard = 0; guard < 20 && bottom - h < minTop && unit > Mathf.Max(Dp(48f), 36f); guard++)
            {
                unit = Mathf.Max(Mathf.Max(Dp(48f), 36f), unit * 0.95f);
                gap = (aw - unit * 5f) / 6f; nameH = Mathf.Round(unit * 0.34f);
                h = headH + unit * 0.12f + unit + nameH + unit * 0.12f + infoH + unit * 0.12f;
            }
            if (bottom - h < minTop) { h -= headH; headH = 0f; }
        }
        var back = new Rect(L, bottom - h, aw, h);
        LastGachaLandRowRect = back; LastGachaPanelRect = back;
        Color keep = GUI.color;
        GUI.color = new Color(0.03f, 0.05f, 0.14f, 0.78f * fade);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = keep;
        if (headH > 0f) LocGUI.Label(new Rect(back.x + 10f, back.y + 2f, back.width - 20f, headH), LocGacha.Pick, UiKit.Label(Mathf.Round(headH * 0.72f), TextAnchor.MiddleLeft, true, new Color(0.95f, 0.85f, 0.6f, fade)));
        float cy = back.y + headH + unit * 0.12f;
        for (int i = 0; i < GachaLands.Length; i++)
            DrawGachaCell(i, new Rect(back.x + gap + i * (unit + gap), cy, unit, unit), nameH, fade, interactable, unit + gap * 0.9f);
        LocGUI.Label(new Rect(back.x + 8f, cy + unit + nameH + unit * 0.12f, back.width - 16f, infoH), line, lst);
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

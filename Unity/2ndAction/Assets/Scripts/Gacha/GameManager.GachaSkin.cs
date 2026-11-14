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
    //  今どの分類のカードが出るかは、横画面は「ガチャ」の札の2行目(GachaSignSubLine)、縦画面は台のすぐ下の1行(DrawGachaTargetCaption)。
    public Rect LastGachaLandRowRect { get; private set; } // 縦画面の台の下の1行(確認用)
    Rect SignRectNowOrPrev(int i) => LastHomeSignRects[i].width > 0f ? LastHomeSignRects[i] : prevHomeSignHitRects[i];
    public static float Dp(float dp) => dp * (Screen.dpi > 1f ? Screen.dpi / 160f : 1.5f);

    // 縦画面: 台のすぐ下に「(紋) 荒野のカードだけが出ます」。押せない(表示だけ)。下の札にかかる時は台の下端の内側へ寄せる
    //  2026-11-14 案の比較: PortraitCaptionAboveButtons = true なら、下のガチャ/広告のボタンのすぐ上に右寄せ(bottomY = ボタンの上端)
    public static bool PortraitCaptionAboveButtons;
    void DrawGachaTargetCaption(Rect machine, float fade, float bottomY = -1f)
    {
        if (!GachaSkinFx.Enabled || fade < 0.05f || machine.width < 1f) { LastGachaLandRowRect = Rect.zero; return; }
        EnsureGachaCategory();
        bool noteOn = !string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil;
        bool empty = GachaCategoryEmpty;
        string line = Loc.Auto(noteOn ? gachaCategoryNote : GachaTargetLine(gachaCategory, empty));
        float fs = Mathf.Round(Mathf.Clamp(HomeSignFont * (bottomY > 0f ? 1.05f : 0.82f), 12f, 24f));
        var st = UiKit.Label(fs, TextAnchor.MiddleLeft, true, noteOn || empty ? new Color(1f, 0.74f, 0.6f, fade) : new Color(1f, 0.92f, 0.66f, fade));
        float maxW = Screen.width - SafeLeft() - SafeRight() - 24f;
        float em = fs * 1.35f, pad = fs * 0.45f;
        for (int g = 0; g < 10 && st.fontSize > 10 && st.CalcSize(new GUIContent(line)).x + em + pad * 3f > maxW; g++) st.fontSize = Mathf.Max(10, Mathf.FloorToInt(st.fontSize * 0.92f));
        Vector2 sz = st.CalcSize(new GUIContent(line));
        float w = em + pad * 3f + sz.x, h = Mathf.Max(em, sz.y) + pad * 0.8f;
        float x = Mathf.Clamp(machine.center.x - w * 0.5f, SafeLeft() + 12f, Screen.width - SafeRight() - 12f - w);
        float y = machine.yMax + 2f;
        if (bottomY > 0f) { x = Screen.width - SafeRight() - 12f - w; y = bottomY - h - 6f; }
        else for (int si = 4; si <= 5; si++) { var sr = SignRectNowOrPrev(si); if (sr.width > 1f && sr.xMax > x && sr.x < x + w && sr.y < y + h && sr.yMax > y) y = Mathf.Min(y, sr.y - h - 4f); }
        var back = new Rect(x, y, w, h);
        LastGachaLandRowRect = back;
        Color keep = GUI.color;
        GUI.color = new Color(0.03f, 0.05f, 0.12f, 0.72f * fade);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = keep;
        GachaSkinFx.DrawEmblem(new Rect(back.x + pad, back.center.y - em * 0.5f, em, em), gachaCategory, false, fade);
        GUI.Label(new Rect(back.x + pad * 2f + em, back.y, sz.x + 2f, back.height), line, st);
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

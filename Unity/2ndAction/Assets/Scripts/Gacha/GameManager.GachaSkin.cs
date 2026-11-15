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
    // 2026-11-15 午前(マスター): ホームには分類・割合の説明文を出さない(「荒野のカード 70%・共通 30%」の2行目/縦画面の帯を外した)。
    //  分類と確率は「提供割合」の画面(GachaOddsPanel)で見る。入口は横画面は「ガチャ」の札の右、縦画面はガチャのボタンの列の上で右寄せ。
    //  対象のカードが無い時に引こうとした理由と条件だけは、その時に短く出す(DrawGachaRefusalNote、2.5 秒)
    public Rect LastGachaLandRowRect { get; private set; } // 縦画面の「提供割合」の入口(広告の案内の位置決め・確認用)。横画面は 0
    public Rect LastGachaOddsButtonRect { get; private set; } // 「提供割合」のボタン(確認用)
    public Rect LastGachaRefusalNoteRect { get; private set; } // 断った理由の短い案内(確認用)
    Rect SignRectNowOrPrev(int i) => LastHomeSignRects[i].width > 0f ? LastHomeSignRects[i] : prevHomeSignHitRects[i];
    public static float Dp(float dp) => dp * (Screen.dpi > 1f ? Screen.dpi / 160f : 1.5f);

    // 案内の色: 普段は金、引けない時/断った理由は明るい橙
    Color GachaLineColor(bool warn, float fade) => warn ? new Color(1f, 0.74f, 0.6f, fade) : new Color(1f, 0.92f, 0.66f, fade);

    // 「提供割合」の小さなボタン(押すと GachaOddsPanel)
    void DrawGachaOddsButton(Rect r, float fs, bool interactable)
    {
        LastGachaOddsButtonRect = r;
        if (UiKit.Button(r, LocGacha.Odds, fs, false, false, true) && interactable) { GachaOddsPanel.Open(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide); }
    }
    // 「提供割合」のボタンの幅(文字 + 左右の余白)
    static float GachaOddsButtonWidth(float fs) => UiKit.Label(fs, TextAnchor.MiddleCenter, true, Color.white).CalcSize(new GUIContent(Loc.T(LocGacha.Odds))).x + fs * 1.4f;

    // 縦画面: ガチャのボタン(と、その上の「広告で1回引く」)の上に、右端をそろえた小さな「提供割合」だけを置く
    void DrawGachaInfoStrip(Rect gachaBtn, float fade, bool interactable)
    {
        if (fade < 0.05f || gachaBtn.width < 1f) { LastGachaLandRowRect = Rect.zero; return; }
        float s = GachaUiScale;
        float fs = Mathf.Round(14f * s);
        float bottom = AdGachaAvailable ? gachaBtn.y - 54f * s - 8f - 5f : gachaBtn.y - 6f; // 広告ボタンとの間は 5px
        float bh = Mathf.Max(Dp(36f), fs * 2.4f); // 押しやすい高さ(以前の帯の中のボタンと同じ基準)
        float bw = Mathf.Min(gachaBtn.width, Mathf.Max(GachaOddsButtonWidth(fs) + fs, bh * 2.4f));
        var r = new Rect(gachaBtn.xMax - bw, bottom - bh, bw, bh);
        r.x = Mathf.Clamp(r.x, SafeLeft() + 8f, Screen.width - SafeRight() - 8f - bw);
        LastGachaLandRowRect = r;
        DrawGachaOddsButton(r, fs, interactable);
        DrawGachaRefusalNote(r, true, fade);
    }

    // 対象のカードが無いマップで引こうとした時の理由と条件(SetGachaCategoryNote、2.5 秒だけ)。above = near の上に出す
    void DrawGachaRefusalNote(Rect near, bool above, float fade)
    {
        if (string.IsNullOrEmpty(gachaCategoryNote) || Time.unscaledTime >= gachaCategoryNoteUntil || fade < 0.05f || near.width < 1f) { LastGachaRefusalNoteRect = Rect.zero; return; }
        float s = GachaUiScale;
        var ns = new GUIStyle(UiKit.Label(Mathf.Round(13f * s), TextAnchor.MiddleCenter, false, GachaLineColor(true, fade))) { wordWrap = true };
        float nw = Mathf.Min(Mathf.Max(near.width, 300f * s), Screen.width - SafeLeft() - SafeRight() - 16f);
        float tw = nw - 12f * s;
        string t = Loc.IsRtl ? LocGUI.RtlFix(gachaCategoryNote, tw, ns) : gachaCategoryNote; // 右から左の言語: 行の順番を保つ
        float nh = Mathf.Max(40f * s, ns.CalcHeight(new GUIContent(t), tw) + 10f * s);
        var nr = new Rect(Mathf.Clamp(near.center.x - nw * 0.5f, SafeLeft() + 8f, Screen.width - SafeRight() - nw - 8f), above ? near.y - nh - 6f : near.yMax + 6f, nw, nh);
        LastGachaRefusalNoteRect = nr;
        UiBackdrop.Draw(nr, 0.85f);
        GUI.Label(new Rect(nr.x + 6f * s, nr.y, tw, nr.height), t, ns);
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

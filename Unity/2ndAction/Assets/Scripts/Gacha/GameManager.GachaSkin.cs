using System.Collections.Generic;
using UnityEngine;

// ガチャのマップ別の見た目(2026-11-06)の GameManager 側のつなぎ。見た目の本体は GachaSkinFx。
//  ・揺れ(回している間)に、見せる順番待ちの先頭のカードの土地で予兆 → 結果を開いた時に排出・台の見た目の切り替え
//  ・台の下の「土地の列」: このガチャに入っている土地(共通/荒野/洞窟/天空/ラスダン)。今の台の土地を強調。
//    まだ候補のカードが無い土地は暗く、マップ自体が未解放なら鍵(中身のカードは出さない = ネタバレしない)。押せない(台を選ぶ仕組みではない)
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

    // 台の下(横画面は「ガチャ」の札の下、縦画面は台のすぐ下)。center.x を中心に横一列
    public Rect LastGachaLandRowRect { get; private set; }
    void DrawGachaLandRow(Vector2 topCenter, float unit, float fade)
    {
        if (!GachaSkinFx.Enabled || fade < 0.05f) { LastGachaLandRowRect = Rect.zero; return; }
        RefreshGachaLandCounts();
        float e = unit, gap = unit * 0.28f;
        float w = e * 5f + gap * 4f + unit * 0.5f, h = e + unit * 0.3f;
        float x = Mathf.Clamp(topCenter.x - w * 0.5f, SafeLeft() + 4f, Screen.width - SafeRight() - w - 4f);
        var row = new Rect(x, topCenter.y, w, h);
        LastGachaLandRowRect = row;
        Color keep = GUI.color;
        GUI.color = new Color(0.03f, 0.04f, 0.08f, 0.62f * fade);
        GUI.DrawTexture(row, Texture2D.whiteTexture);
        for (int i = 0; i < GachaLands.Length; i++)
        {
            var k = GachaLands[i];
            bool open = GachaLandMapOpen(k);
            bool has = gachaLandCounts[(int)k] > 0;
            bool cur = gachaSkinFx.Skin == k;
            var r = new Rect(row.x + unit * 0.25f + i * (e + gap), row.y + unit * 0.15f, e, e);
            // エステル: 下線は「選択中」と誤解されるのでやめ、最後に引いた土地の紋だけ明るく(ほかは控えめ)
            GUI.color = keep;
            float a = !open ? 1f : !has ? 0.3f : cur ? 1f : 0.55f;
            if (cur && open) { Color ac = GachaSkinFx.Accent(k); GUI.color = new Color(ac.r, ac.g, ac.b, 0.35f * fade); GUI.DrawTexture(new Rect(r.x - r.width * 0.25f, r.y - r.height * 0.25f, r.width * 1.5f, r.height * 1.5f), GachaSkinFx.GlowTex); GUI.color = keep; }
            GachaSkinFx.DrawEmblem(r, k, !open, fade * a);
        }
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

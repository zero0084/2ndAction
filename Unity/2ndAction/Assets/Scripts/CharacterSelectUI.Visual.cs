using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// キャラ選択の立ち絵の大きさと見切れ(2026-10-31)。
//  立ち絵(<id>_main.png、全員 700×1100・足もとが下端)は人物の大きさがばらばらで(ルカ 490px〜巫女 1078px)、
//  以前は全員を同じ枠いっぱいに出していた → 低い構え(忍者/弓)・ルカ・ペンタが小さく、枠が情報パネル/カルーセルに重なって絵の端が切れて見えた。
//  ・立ち絵は「表示領域」(MainVisualClip、RectMask2D の左右の端をぼかす)の中に置く。領域はカルーセルと情報パネルの間で、どちらにも重ならない
//  ・キャラごとの表: 切れてはいけない範囲(体・持っている武器・服。光や粒の効果は含めない)の左右と上端、体格をそろえる倍率(desired)
//    倍率は「頭・胴の大きさ」を見て決めた(全員を同じ高さにはしない: 忍者・弓は低い構えのまま本体の縮尺をそろえる、竜騎士は槍で小さくしない)。
//    実際の倍率 = min(desired, 領域の幅に切れてはいけない範囲が入る倍率, 領域の高さに上端が入る倍率)。横は切れてはいけない範囲の中心を領域の中心へ
//  ・縦横比はそのまま。足もとは領域の下端。ゲーム中のキャラの大きさ・当たり判定には関係しない(この画面の表示だけ)
public partial class CharacterSelectUI
{
    // x0/x1/top: 700×1100 の絵の中の画素。desired: 体格をそろえる倍率(1 = 絵のまま)
    struct VisualFit { public float x0, x1, top, desired; public VisualFit(float a, float b, float t, float d) { x0 = a; x1 = b; top = t; desired = d; } }
    static readonly Dictionary<string, VisualFit> Fits = new Dictionary<string, VisualFit>
    {
        { "swordsman",     new VisualFit( 20f, 560f, 290f, 1.00f) }, // 剣先(左下)とマント
        { "dual_blade",    new VisualFit( 60f, 600f, 230f, 1.00f) }, // しゃがみ+双剣。青い弧は効果
        { "noble_lady",    new VisualFit( 50f, 640f,  70f, 1.00f) }, // 剣とマント、光輪
        { "gunslinger",    new VisualFit(150f, 600f, 150f, 1.00f) },
        { "dragon_lancer", new VisualFit( 80f, 590f,  30f, 1.00f) }, // 槍の先(左上)まで。槍のために本体を小さくしない(本体の縮尺は剣士と同じ)
        { "archer",        new VisualFit(170f, 620f, 190f, 1.15f) }, // 低い構え: 頭の大きさを剣士にそろえる(立ち姿の高さまでは上げない)
        { "mage",          new VisualFit(150f, 670f,  60f, 1.00f) }, // 杖の先と本
        { "fighter",       new VisualFit( 90f, 590f, 220f, 1.00f) }, // 炎は効果
        { "ninja",         new VisualFit(110f, 570f, 560f, 1.30f) }, // 低い構え: 本体の縮尺をそろえる(1.47 倍の立ち姿の高さまでは上げない)
        { "miko",          new VisualFit( 30f, 600f, 100f, 1.00f) },
        { "vampire",       new VisualFit( 80f, 640f, 120f, 1.00f) },
        { "dragonkin",     new VisualFit( 80f, 630f, 100f, 1.00f) }, // 翼まで
        { "luka",          new VisualFit( 50f, 660f, 560f, 1.40f) }, // 猫: 人より小さいままで、見やすい大きさ
        { "pentatuttulu",  new VisualFit(100f, 660f, 430f, 1.15f) }, // 雲: 腕まで。泡は効果
    };
    const float SrcW = 700f, SrcH = 1100f, FeetY = 1078f;

    RectTransform visualClip;
    public RectTransform VisualClip => visualClip;
    public static float LastFitScale { get; private set; } // 確認用
    public static string LastFitId { get; private set; } = "";

    void EnsureVisualClip()
    {
        if (visualClip != null || mainVisualImage == null) return;
        var img = mainVisualImage.rectTransform;
        var parent = img.parent;
        var go = new GameObject("MainVisualClip", typeof(RectTransform));
        visualClip = (RectTransform)go.transform;
        visualClip.SetParent(parent, false);
        visualClip.SetSiblingIndex(img.GetSiblingIndex());
        var mask = go.AddComponent<RectMask2D>();
        mask.softness = new Vector2Int(36, 0); // 左右の端だけぼかす(効果の光が枠で直線に切れて見えないように)
        img.SetParent(visualClip, false);
        mainVisualImage.preserveAspect = true;
    }

    // 表示領域を置く(親 = この画面の root。中心と大きさ)
    void PlaceVisualClip(Vector2 center, Vector2 size)
    {
        EnsureVisualClip();
        if (visualClip == null) return;
        visualClip.anchorMin = visualClip.anchorMax = visualClip.pivot = new Vector2(0.5f, 0.5f);
        visualClip.sizeDelta = size;
        visualClip.anchoredPosition = center;
        FitMainVisual(SelectedCharacterId);
    }

    string SelectedCharacterId => selectedIndex >= 0 && selectedIndex < VisibleCount ? DefAt(selectedIndex).characterId : null;

    // 立ち絵を表示領域の中に置く(キャラごとの倍率と横位置)
    void FitMainVisual(string id)
    {
        if (visualClip == null || mainVisualImage == null || string.IsNullOrEmpty(id)) return;
        var f = Fits.TryGetValue(id, out var v) ? v : new VisualFit(80f, 620f, 60f, 1f);
        float W = visualClip.rect.width, H = visualClip.rect.height;
        if (W < 10f || H < 10f) return;
        float pad = 18f;
        float baseK = H / FeetY;                                    // 足もと〜絵の上端 が領域の高さ(倍率 1 の時)
        float k = baseK * f.desired;
        k = Mathf.Min(k, (W - 2f * pad) / Mathf.Max(1f, f.x1 - f.x0));    // 切れてはいけない範囲の幅が入る
        k = Mathf.Min(k, (H - pad) / Mathf.Max(1f, FeetY - f.top));        // 上端が入る
        var r = mainVisualImage.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0f, 0f);
        r.sizeDelta = new Vector2(SrcW * k, SrcH * k);
        float cx = (f.x0 + f.x1) * 0.5f;
        r.anchoredPosition = new Vector2(-cx * k, -(SrcH - FeetY) * k);  // 範囲の中心を領域の中心へ / 足もとを領域の下端へ
        LastFitScale = k / baseK; LastFitId = id;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public float DebugFitFor(string id) { FitMainVisual(id); return LastFitScale; }
#endif
}

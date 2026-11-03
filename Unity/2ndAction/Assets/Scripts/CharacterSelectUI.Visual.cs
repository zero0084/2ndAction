using UnityEngine;
using UnityEngine.UI;

// キャラ選択の立ち絵(2026-11-01 幻想画へ差し替え、エステルと制作)。
//  立ち絵(<id>_main.png)は全員 1024×1536(2:3)の、紙の地まで描いた1枚の絵。武器の先や描き込んだ効果まで含めて、絵の全体を表示領域に収める
//  (以前の切り抜きの立ち絵用だった、キャラごとの倍率と横位置の表は不要になった)。縦横比はそのまま。ゲーム中のキャラの大きさ・当たり判定には関係しない
//  女性7人の Steam(Windows)/Android の衣装の違いは、絵の取り込み(Editor/CharacterCostumeImporter)で切り替える。この画面は同じ絵の参照のまま
//  2026-11-01: 紙の色が黒い背景に四角く浮いて見えた → 表示だけで外周を水彩のように透かす(Shaders/UIPaperFade。元の絵はそのまま)。
//   線・色・武器・布と白い体は透かさず、その周りに淡い紙を残す(剣先/槍先/マント/ペンタの輪郭が黒に溶けない)。PaperFade = false で元の四角い紙
public partial class CharacterSelectUI
{
    RectTransform visualClip;
    public RectTransform VisualClip => visualClip;
    public static float LastFitScale { get; private set; } // 確認用
    public static string LastFitId { get; private set; } = "";
    public static bool PaperFade = true; // 確認用(-csPaperFade 0 で変更前の見た目)
    static Material paperFadeMat;
    // キャラごとの紙の縁(上の縁の幅 = 絵の高さに対する割合、残す所と見なす紙からの離れ方)。表に無いキャラは共通(0.09 / 0.10)
    //  ペンタトゥットゥルー星人: 頭の上に何も描いていない紙が高さの約23%ある → 上だけ 32% の幅で内側から溶かす。紙の粒を拾わないよう 0.14
    static readonly System.Collections.Generic.Dictionary<string, Vector2> PaperEdge = new System.Collections.Generic.Dictionary<string, Vector2>
    {
        { "pentatuttulu", new Vector2(0.32f, 0.14f) },
    };

    void EnsureVisualClip()
    {
        if (visualClip != null || mainVisualImage == null) return;
        var img = mainVisualImage.rectTransform;
        var parent = img.parent;
        var go = new GameObject("MainVisualClip", typeof(RectTransform));
        visualClip = (RectTransform)go.transform;
        visualClip.SetParent(parent, false);
        visualClip.SetSiblingIndex(img.GetSiblingIndex());
        go.AddComponent<RectMask2D>();
        img.SetParent(visualClip, false);
        mainVisualImage.preserveAspect = true;
        if (PaperFade)
        {
            if (paperFadeMat == null)
            {
                var sh = Resources.Load<Shader>("Shaders/UIPaperFade");
                if (sh != null) paperFadeMat = new Material(sh) { name = "PaperFade" };
            }
            if (paperFadeMat != null) mainVisualImage.material = paperFadeMat;
        }
    }

    // 切り替えの時に、前の立ち絵を下に残しておく絵(2026-11-03: 消えてから出る間の空白をなくす)。見た目の設定(大きさ・紙の縁)は前のまま
    UnityEngine.UI.Image prevVisual;
    Material prevMat;
    void HoldPreviousVisual(float alpha)
    {
        EnsureVisualClip();
        if (visualClip == null || mainVisualImage == null) return;
        if (prevVisual == null)
        {
            var go = new GameObject("MainVisualPrev", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(visualClip, false);
            go.transform.SetAsFirstSibling();
            prevVisual = go.GetComponent<Image>();
            prevVisual.raycastTarget = false;
            prevVisual.preserveAspect = true;
        }
        if (mainVisualImage.sprite == null || alpha <= 0.01f) { prevVisual.enabled = false; return; }
        var a = mainVisualImage.rectTransform; var b = prevVisual.rectTransform;
        b.anchorMin = a.anchorMin; b.anchorMax = a.anchorMax; b.pivot = a.pivot;
        b.sizeDelta = a.sizeDelta; b.anchoredPosition = a.anchoredPosition; b.offsetMin = a.offsetMin; b.offsetMax = a.offsetMax;
        prevVisual.sprite = mainVisualImage.sprite;
        if (paperFadeMat != null)
        {
            if (prevMat == null) prevMat = new Material(paperFadeMat) { name = "PaperFade (previous)" };
            prevMat.CopyPropertiesFromMaterial(paperFadeMat);
            prevVisual.material = prevMat;
        }
        prevVisual.color = new Color(1f, 1f, 1f, alpha);
        prevVisual.enabled = true;
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

    // 絵の全体(武器の先・描き込んだ効果まで)を表示領域いっぱいに、縦横比を保って出す
    void FitMainVisual(string id)
    {
        if (visualClip == null || mainVisualImage == null || string.IsNullOrEmpty(id)) return;
        var rect = mainVisualImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var tex = CharacterDatabase.FindById(id)?.mainVisual;
        if (paperFadeMat != null && tex != null) paperFadeMat.SetFloat("_Aspect", (float)tex.width / Mathf.Max(1, tex.height)); // 外周の幅を縦横で同じ太さに
        if (paperFadeMat != null)
        {
            var edge = PaperEdge.TryGetValue(id, out var e) ? e : new Vector2(0.09f, 0.10f);
            paperFadeMat.SetFloat("_FadeTop", edge.x);
            paperFadeMat.SetFloat("_InkLow", edge.y);
        }
        LastFitScale = 1f;
        LastFitId = id;
        if (paperFadeMat != null) { paperFadeMat.SetFloat("_VisOn", 0f); paperFadeMat.SetVector("_Vis", new Vector4(0f, 0f, 1f, 1f)); }
        if (tex == null || !PortraitFit.TryGetValue(id, out var fit)) return;

        // 2026-11-03: 人物(頭の上〜足もと)の大きさをキャラの間でそろえる(槍先まで描いた竜騎士は、絵の全体は大きいのに人物が一回り小さく見えた)。
        //  足もとの高さを変えずに拡大し、上が枠から出る時は足もとが枠の下に着くまで下げる。枠で切れる辺は UIPaperFade が内側で薄れさせる
        Vector2 clip = visualClip.rect.size;
        float aspect = (float)tex.width / Mathf.Max(1, tex.height);
        float w0 = Mathf.Min(clip.x, clip.y * aspect), h0 = w0 / aspect;
        float s = fit.x, feet = fit.y; // feet = 足もとの高さ(絵の下からの割合)
        float w = w0 * s, h = h0 * s;
        float feetY = -h0 * 0.5f + feet * h0;          // 拡大前の足もと(枠の中心が 0)
        float cy = feetY - feet * h + h * 0.5f;          // 足もとを動かさない中心
        float over = cy + h * 0.5f - clip.y * 0.5f;      // 上のはみ出し
        float room = feetY - (-clip.y * 0.5f + h * 0.02f); // 足もとを下げられる余裕
        if (over > 0f && room > 0f) cy -= Mathf.Min(over, room);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(0f, cy);
        LastFitScale = s;
        if (paperFadeMat != null)
        {
            float x0 = -w * 0.5f, y0 = cy - h * 0.5f;
            var vis = new Vector4(Mathf.Clamp01((-clip.x * 0.5f - x0) / w), Mathf.Clamp01((-clip.y * 0.5f - y0) / h),
                                  Mathf.Clamp01((clip.x * 0.5f - x0) / w), Mathf.Clamp01((clip.y * 0.5f - y0) / h));
            paperFadeMat.SetVector("_Vis", vis);
            paperFadeMat.SetFloat("_VisOn", 1f);
        }
    }

    // 立ち絵の人物の大きさ(2026-11-03、エステルと確認): x = 拡大の倍率(頭の上〜足もとの高さを黒剣士にそろえる。
    //  ±5% 以内のキャラは表に入れない)、y = 足もとの高さ(絵の下端からの割合)。忍者の低い構え・ルカ/ペンタの体格はそのまま(表に入れない)
    //  測定: 1024×1536 の絵で 頭の上〜足もと。黒剣士 1312px、竜騎士 1064(槍先は数えない)、弓兵 1055(脚を大きく開いた構え → 頭の大きさで ×1.12)、
    //  双剣士 1200、竜人 1192(角は数えない)、魔法使い 1232
    static readonly System.Collections.Generic.Dictionary<string, Vector2> PortraitFit = new System.Collections.Generic.Dictionary<string, Vector2>
    {
        { "dragon_lancer", new Vector2(1.12f, 0.071f) }, // エステル: ×1.15 は槍とマントの存在感が強すぎる → ×1.12
        { "archer", new Vector2(1.12f, 0.069f) },
        { "dual_blade", new Vector2(1.08f, 0.069f) },
        { "dragonkin", new Vector2(1.08f, 0.069f) },
        { "mage", new Vector2(1.05f, 0.089f) },
    };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public float DebugFitFor(string id) { FitMainVisual(id); return LastFitScale; }
#endif
}

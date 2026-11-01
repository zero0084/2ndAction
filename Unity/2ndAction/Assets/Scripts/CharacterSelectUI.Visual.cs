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
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public float DebugFitFor(string id) { FitMainVisual(id); return LastFitScale; }
#endif
}

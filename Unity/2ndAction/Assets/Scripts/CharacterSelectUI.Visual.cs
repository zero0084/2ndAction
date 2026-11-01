using UnityEngine;
using UnityEngine.UI;

// キャラ選択の立ち絵(2026-11-01 幻想画へ差し替え、エステルと制作)。
//  立ち絵(<id>_main.png)は全員 1024×1536(2:3)の、紙の地まで描いた1枚の絵。武器の先や描き込んだ効果まで含めて、絵の全体を表示領域に収める
//  (以前の切り抜きの立ち絵用だった、キャラごとの倍率と横位置の表は不要になった)。縦横比はそのまま。ゲーム中のキャラの大きさ・当たり判定には関係しない
//  女性7人の Steam(Windows)/Android の衣装の違いは、絵の取り込み(Editor/CharacterCostumeImporter)で切り替える。この画面は同じ絵の参照のまま
public partial class CharacterSelectUI
{
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
        go.AddComponent<RectMask2D>();
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
        LastFitScale = 1f;
        LastFitId = id;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public float DebugFitFor(string id) { FitMainVisual(id); return LastFitScale; }
#endif
}

using UnityEngine;

// Card UI / Rarity Frame pass - holds the 5 Rarity-specific Frame Sprites
// (★1-★5). RewardCardUI reads from here at SetContent() time so every
// screen that already reuses RewardCardUI (Level Up/Boss Reward/
// Collection/Character Card/Deck/Fusion/Gacha Result) gets Rarity-correct
// frames automatically, with no per-screen wiring beyond what
// CreateRewardCard already does.
//
// Bugfix 2026-09-06, item 3 - "Rarity Frameが反映されていない". Root cause:
// the first version of this class populated its Sprite[] from
// SceneBuilder.Build() (an EDITOR-ONLY batchmode process), but a plain
// static C# field is never serialized anywhere - writing to it from the
// Editor process has zero effect on the actual game process (Play mode or
// a real device build), which starts with a completely fresh, empty
// array every time. Every card, everywhere, was silently always falling
// back to the old CardFrame.png - exactly the symptom reported. Fixed by
// loading lazily at RUNTIME via Resources.Load instead (the same pattern
// CardDatabase.Load() already uses for CardDefinition assets), which is
// why the 4 usable frame PNGs live under Assets/Resources/CardFrames/ -
// Resources.Load can only ever find assets physically inside a folder
// literally named "Resources".
public static class CardRarityFrames
{
    // Index 0 unused - index N holds Rarity N's frame (1-5). null until
    // first accessed (EnsureLoaded), then cached for the rest of the
    // process - same lifecycle as CardDatabase.cachedCards.
    static Sprite[] frames;

    // Bugfix note (Card UI / Rarity Frame pass) - the ★1 source image THEN
    // supplied had NO real alpha channel (Format24bppRgb) - its
    // "transparent" interior was a checker pattern baked in as opaque gray
    // pixels, the same class of issue as the earlier Death.jpg problem this
    // project already hit once, and too risky to algorithmically recover.
    // Card UI改修(2026-09-08) - a proper transparent ★1 export now exists
    // (CardFrameRarity1.png, verified via PowerShell/System.Drawing to have
    // real alpha: A=0 at the corners/center, A≈253 on the border art), so
    // Rarity 1 finally has its own entry here like every other tier.
    static void EnsureLoaded()
    {
        if (frames != null) return;
        frames = new Sprite[6];
        for (int r = 1; r <= 5; r++)
        {
            frames[r] = Resources.Load<Sprite>($"CardFrames/CardFrameRarity{r}");
        }
    }

    public static Sprite GetFrame(int rarity, Sprite fallback)
    {
        EnsureLoaded();
        int r = Mathf.Clamp(rarity <= 0 ? 1 : rarity, 1, 5);
        Sprite s = frames[r];
        return s != null ? s : fallback;
    }

    // カードUI最終デザイン改修(2026-09-26) - 5枚のフレーム画像は縦横比がバラバラ
    // (★1/★3=2:3、★2/★4=3:4、★5=ほぼ正方形+左右に透明余白)で、preserveAspect表示だと
    // レア度ごとにフレームの上下位置が変わり、Name Plate/菱形エンブレムの位置を全カードで
    // 揃えられなかった。そこで各フレームを「実際に絵がある範囲」で切り出し、横はカード幅に
    // ぴったり合わせ、縦の余り/不足は宝石の無い無地のレール区間(bandTop〜bandBottom、切り出し
    // 後の上端からの割合)だけで吸収する9-slice(左右ボーダー0/上下ボーダーのみ)にする。
    // 宝石や角の装飾は縦横同じ倍率のまま(verticalScaleが1より大きい★5のみ全体を少し縦に伸ばし、
    // 残りをレール区間で吸収)。範囲はアルファ>100の外接矩形を実測した値(元画像のピクセル)。
    struct SliceSpec
    {
        public int w, h;          // 元画像サイズ
        public int x0, y0, x1, y1; // 絵のある範囲(上端基準のピクセル)
        public float bandTop, bandBottom; // 伸縮させるレール区間(切り出し後の上端からの割合)
        public float verticalScale;
    }

    static readonly SliceSpec[] Specs =
    {
        default,
        new SliceSpec { w = 1024, h = 1536, x0 = 49, y0 = 18, x1 = 975, y1 = 1482, bandTop = 0.60f, bandBottom = 0.82f, verticalScale = 1f },
        new SliceSpec { w = 1086, h = 1448, x0 = 41, y0 = 12, x1 = 1045, y1 = 1406, bandTop = 0.75f, bandBottom = 0.86f, verticalScale = 1f },
        new SliceSpec { w = 1024, h = 1536, x0 = 14, y0 = 2, x1 = 1010, y1 = 1489, bandTop = 0.75f, bandBottom = 0.86f, verticalScale = 1f },
        new SliceSpec { w = 1086, h = 1448, x0 = 36, y0 = 0, x1 = 1051, y1 = 1436, bandTop = 0.71f, bandBottom = 0.84f, verticalScale = 1f },
        new SliceSpec { w = 1237, h = 1272, x0 = 101, y0 = 0, x1 = 1136, y1 = 1258, bandTop = 0.74f, bandBottom = 0.86f, verticalScale = 1.12f },
    };

    static Sprite[] sliced;

    // カード全面を覆う形で表示するためのフレーム(Image.type=Sliced用)。cropWidthPx/verticalScaleは
    // RewardCardUIがpixelsPerUnitMultiplierを計算するのに使う。読めなければnull(従来表示へ)。
    public static Sprite GetSlicedFrame(int rarity, out float cropWidthPx, out float verticalScale)
    {
        EnsureLoaded();
        int r = Mathf.Clamp(rarity <= 0 ? 1 : rarity, 1, 5);
        cropWidthPx = 0f;
        verticalScale = 1f;
        if (sliced == null) sliced = new Sprite[6];
        Sprite src = frames[r];
        if (src == null) return null;
        SliceSpec sp = Specs[r];
        Rect tr = src.rect;
        // 元画像がNPOT拡大などで別サイズで読み込まれていても割合で合わせる
        float sx = tr.width / sp.w, sy = tr.height / sp.h;
        var crop = new Rect(tr.x + sp.x0 * sx, tr.y + (sp.h - sp.y1) * sy, (sp.x1 - sp.x0) * sx, (sp.y1 - sp.y0) * sy);
        cropWidthPx = crop.width;
        verticalScale = sp.verticalScale;
        if (sliced[r] != null) return sliced[r];
        float top = crop.height * sp.bandTop;
        float bottom = crop.height * (1f - sp.bandBottom);
        sliced[r] = Sprite.Create(src.texture, crop, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(0f, bottom, 0f, top));
        sliced[r].name = src.name + "_Sliced";
        return sliced[r];
    }
}

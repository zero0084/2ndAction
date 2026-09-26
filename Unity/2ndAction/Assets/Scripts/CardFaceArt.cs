using UnityEngine;

// カードUI最終デザイン改修(2026-09-26) - カード表面の装飾パーツ(Name Plateの角丸板/
// Category・Levelの菱形エンブレム/選択時の淡いGlow/丸チップ)に使う白塗りスプライトを
// 実行時に手続き生成する。色はImage.colorで付ける。
// SceneBuilder(Editorのbatchmode)でSprite.Createしたものはシーンに保存されないため、
// 必ず実行時に作ってキャッシュする(CardRarityFramesのResources.Loadと同じ理由)。
public static class CardFaceArt
{
    static Sprite rounded, diamond, glow, circle;

    // 角丸の板(9-slice)。角の半径はRoundedRadiusPx - Image.pixelsPerUnitMultiplierで実寸を決める。
    public const int RoundedRadiusPx = 16;

    public static Sprite RoundedRect()
    {
        if (rounded != null) return rounded;
        const int size = 64;
        const int r = RoundedRadiusPx;
        var tex = NewTexture(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, size - r);
            float cy = Mathf.Clamp(y + 0.5f, r, size - r);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f));
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return rounded;
    }

    // 縁が滑らかな菱形(回転させた四角形より輪郭がきれい - UIはMSAAが効かないため)。
    public static Sprite Diamond()
    {
        if (diamond != null) return diamond;
        const int size = 128;
        var tex = NewTexture(size, size);
        var px = new Color[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = (Mathf.Abs(x + 0.5f - h) + Mathf.Abs(y + 0.5f - h)) / h; // 1で菱形の縁
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * h * 0.7f + 0.5f));
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        diamond = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return diamond;
    }

    // 中心から外へ柔らかく消える光(選択中カードの背後)。
    public static Sprite SoftGlow()
    {
        if (glow != null) return glow;
        const int size = 128;
        var tex = NewTexture(size, size);
        var px = new Color[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 角丸の長方形に沿って減衰させる(カードの形に合う光にする)
            float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - h) - h * 0.62f) / (h * 0.38f);
            float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - h) - h * 0.62f) / (h * 0.38f);
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            float a = 1f - d;
            px[y * size + x] = new Color(1f, 1f, 1f, a * a);
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        glow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return glow;
    }

    public static Sprite Circle()
    {
        if (circle != null) return circle;
        const int size = 64;
        var tex = NewTexture(size, size);
        var px = new Color[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(h, h));
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(h - d - 0.5f));
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circle;
    }

    static Texture2D NewTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.DontSave;
        return tex;
    }
}

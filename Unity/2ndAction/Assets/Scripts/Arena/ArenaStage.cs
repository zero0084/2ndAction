using UnityEngine;

// 闘技場の見た目(2026-10-04 手続き生成 → 2026-10-06 本番の絵: Resources/Arena/arena_stands(背景)・arena_floor(床)。無ければ手続き生成)。石畳の床 + 奥の観客席(段・アーチ・観客・柱・旗)。
// どちらもカメラに合わせて横へ並べ直すので、走り続けても途切れない(床は世界に固定した模様、観客席は少し遅れて流れる)。
// 床の高さは闘技場の地面(平地)の高さ。地形の当たり判定は通常の地面のまま(見た目だけを重ねる)。
public class ArenaStage : MonoBehaviour
{
    const float FloorTileW = 4f, FloorDepth = 14f;
    const float BgTileW = 48f;
    float floorY;
    SpriteRenderer floor, floorTop, floorShade;
    readonly SpriteRenderer[] bg = new SpriteRenderer[5]; // 横に5枚(縦長の絵でも画面の端まで切れ目なく)
    static Sprite floorSprite, floorTopSprite, bgSprite, shadeSprite;
    static bool bgIsArt;
    float tileW = FloorTileW;
    float camOffset0 = float.NaN;
    // 本番の背景絵で、闘技場の床と壁の境目がある高さ(上からの割合)。ここを地面の高さに合わせて置く
    const float ArtGroundFrac = 0.66f; // (壁の足元は 0.62。少し奥の砂地が見えるように)

    public static ArenaStage Create(float groundY)
    {
        var go = new GameObject("ArenaStage");
        var s = go.AddComponent<ArenaStage>();
        s.floorY = groundY;
        s.Build();
        return s;
    }

    void Build()
    {
        // 本番の床の絵(Resources/Arena/arena_floor = 上下左右につながる石積み)があればそれを使う
        if (floorSprite == null) floorSprite = Resources.Load<Sprite>("Arena/arena_floor");
        if (floorSprite == null) floorSprite = MakeFloor();
        tileW = Mathf.Max(0.5f, floorSprite.bounds.size.x);
        if (floorTopSprite == null) floorTopSprite = MakeFloorTop();
        // 本番の絵(Resources/Arena/arena_stands = 横長の観客席の背景、左右がつながる絵)があればそれを使う。無ければ手続き生成の絵
        if (bgSprite == null) { bgSprite = Resources.Load<Sprite>("Arena/arena_stands"); bgIsArt = bgSprite != null; }
        if (bgSprite == null) bgSprite = MakeBackground();
        floor = NewRenderer("Floor", floorSprite, RenderOrder.Ground);
        floor.drawMode = SpriteDrawMode.Tiled;
        floor.size = new Vector2(tileW * Mathf.Ceil(FloorTileW * 30f / tileW), FloorDepth);
        // 床の奥行きの影(地面の下は暗くして、戦う場所(床の上)へ目が行くように。通常のステージの地面と同じ考え方)
        if (shadeSprite == null) shadeSprite = MakeShade();
        floorShade = NewRenderer("FloorShade", shadeSprite, RenderOrder.Ground);
        floorShade.drawMode = SpriteDrawMode.Simple;
        floorTop = NewRenderer("FloorEdge", floorTopSprite, RenderOrder.Ground);
        floorTop.drawMode = SpriteDrawMode.Tiled;
        floorTop.size = new Vector2(FloorTileW * 30f, 0.5f);
        for (int i = 0; i < bg.Length; i++)
        {
            bg[i] = NewRenderer("Stands" + i, bgSprite, RenderOrder.SkyCloud + 1);
            bg[i].drawMode = SpriteDrawMode.Simple;
            if (bgIsArt) bg[i].color = new Color(0.86f, 0.86f, 0.9f, 1f); // 奥の絵は少し落として、キャラ/攻撃の予兆を読みやすく
        }
        LateUpdate();
    }

    SpriteRenderer NewRenderer(string name, Sprite s, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingOrder = order;
        return sr;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float cx = cam.transform.position.x;
        // 床: 模様の継ぎ目を世界の位置にそろえる(走っても模様が床に貼り付いて見える)
        float snap = Mathf.Floor(cx / tileW) * tileW;
        floor.transform.position = new Vector3(snap, floorY - FloorDepth * 0.5f, -0.5f);
        snap = Mathf.Floor(cx / FloorTileW) * FloorTileW;
        floorTop.transform.position = new Vector3(snap, floorY - 0.05f, -0.6f);
        floorShade.transform.position = new Vector3(cx, floorY - FloorDepth * 0.5f, -0.55f);
        floorShade.transform.localScale = new Vector3(FloorTileW * 30f, FloorDepth / 64f, 1f);
        // 観客席: カメラの高さに合わせ、横は少し遅れて流れる(視差)。3枚を並べて切れ目を見せない
        float h = cam.orthographicSize * 2f;
        float scale = h * 1.05f / (bgSprite.bounds.size.y);
        // 本番の絵: 絵の上端〜床の境目が、画面の上端〜地面にちょうど収まる大きさ(空と旗の先まで見える)
        // カメラの上下の揺れで大きさが変わらないよう、最初のカメラの高さを基準にする(大型ボスでカメラが引くと一緒に大きくなる)
        if (bgIsArt)
        {
            if (float.IsNaN(camOffset0)) camOffset0 = cam.transform.position.y - floorY;
            scale = Mathf.Max(0.5f, camOffset0 + h * 0.5f) * 1.02f / (ArtGroundFrac * bgSprite.bounds.size.y);
        }
        float w = bgSprite.bounds.size.x * scale;
        float par = cx * 0.85f;
        float baseX = cx - Mathf.Repeat(cx - par, w);
        // 本番の絵は、絵の中の床と壁の境目を地面の高さに合わせる(カメラが上がって絵の上が空いてしまう時だけ上へずらす)
        float by = cam.transform.position.y;
        if (bgIsArt)
        {
            float hImg = bgSprite.bounds.size.y * scale;
            by = floorY + (ArtGroundFrac - 0.5f) * hImg;
            float camTop = cam.transform.position.y + h * 0.5f;
            if (by + hImg * 0.5f < camTop) by = camTop - hImg * 0.5f;
        }
        for (int i = 0; i < bg.Length; i++)
        {
            bg[i].transform.localScale = new Vector3(scale, scale, 1f);
            bg[i].transform.position = new Vector3(baseX + (i - 2) * w, by, 5f);
        }
    }

    // ===================================================================== 仮素材
    static Sprite MakeFloor()
    {
        const int W = 256, H = 128;
        var tex = NewTex(W, H, TextureWrapMode.Repeat);
        var px = new Color[W * H];
        var rnd = new System.Random(7);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // 2段の石の積み(上の段は半分ずらす)
                int row = y / 64; int ox = row == 0 ? 0 : 64;
                int bx = ((x + ox) % 128), by = y % 64;
                bool mortar = bx < 3 || by < 3;
                float n = Noise(x, y) * 0.12f;
                float shade = 0.42f + n - (y / (float)H) * 0.18f;
                Color stone = new Color(shade * 1.02f, shade * 0.96f, shade * 0.88f, 1f);
                px[y * W + x] = mortar ? new Color(0.16f, 0.14f, 0.13f, 1f) : stone;
            }
        tex.SetPixels(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W / FloorTileW, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    static Sprite MakeFloorTop()
    {
        const int W = 128, H = 16;
        var tex = NewTex(W, H, TextureWrapMode.Repeat);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // 砂を敷いた床の縁(上が明るい砂、下の端に細い影)。本番の床の石積み/背景の砂地と同じ色味
                float t = y / (float)H;
                float n = Noise(x * 3, y * 3) * 0.06f;
                Color c = Color.Lerp(new Color(0.66f, 0.52f, 0.33f), new Color(0.88f, 0.76f, 0.54f), Mathf.SmoothStep(0f, 1f, t)) * (1f - n);
                if (y < 2) c = new Color(0.38f, 0.29f, 0.18f);
                c.a = 1f;
                px[y * W + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W / FloorTileW, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    // 横 2:1 の観客席。上: 空、中: 段になった観客席(観客の点)、下寄り: アーチの壁、手前の柱と旗
    static Sprite MakeBackground()
    {
        const int W = 1024, H = 512;
        var tex = NewTex(W, H, TextureWrapMode.Clamp);
        var px = new Color[W * H];
        var rnd = new System.Random(11);
        Color[] crowd = { new Color(0.75f, 0.3f, 0.25f), new Color(0.3f, 0.45f, 0.75f), new Color(0.85f, 0.75f, 0.4f), new Color(0.4f, 0.6f, 0.35f), new Color(0.9f, 0.85f, 0.8f), new Color(0.5f, 0.35f, 0.6f) };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)H; // 0=下 1=上
                Color c;
                if (v > 0.82f) c = Color.Lerp(new Color(0.55f, 0.7f, 0.9f), new Color(0.32f, 0.5f, 0.82f), (v - 0.82f) / 0.18f); // 空
                else if (v > 0.8f) c = new Color(0.36f, 0.32f, 0.28f); // 縁
                else if (v > 0.38f)
                {
                    // 段(8段)。段ごとに少し暗く、観客の点
                    float tv = (v - 0.38f) / 0.42f;
                    int tier = Mathf.FloorToInt(tv * 8f);
                    float inTier = tv * 8f - tier;
                    float stone = 0.46f - tier * 0.012f;
                    c = new Color(stone, stone * 0.93f, stone * 0.84f);
                    if (inTier < 0.12f) c *= 0.7f;
                    else if (((x / 7) + tier * 3) % 2 == 0 && inTier > 0.25f && inTier < 0.85f && Hash(x / 7, tier) % 10 < 7)
                    {
                        Color cc = crowd[Hash(x / 7, tier * 13) % crowd.Length];
                        float hd = Mathf.Abs(inTier - 0.65f) < 0.12f ? 1.1f : 0.9f;
                        c = Color.Lerp(c, cc * hd, 0.85f);
                    }
                    c.a = 1f;
                }
                else if (v > 0.36f) c = new Color(0.3f, 0.27f, 0.24f); // 手すり
                else
                {
                    // アーチの壁
                    float wall = 0.38f + Noise(x, y) * 0.05f;
                    c = new Color(wall, wall * 0.92f, wall * 0.82f);
                    int ax = x % 128; float ay = v / 0.36f;
                    float cxp = (ax - 64) / 40f;
                    bool arch = ay < 0.75f && (ay < 0.5f ? Mathf.Abs(cxp) < 1f : cxp * cxp + ((ay - 0.5f) / 0.25f) * ((ay - 0.5f) / 0.25f) < 1f);
                    if (arch) c = new Color(0.08f, 0.07f, 0.07f);
                    if (x % 128 < 6 || x % 128 > 121) c *= 0.8f;
                    c.a = 1f;
                }
                // 柱と旗(256px ごと)
                int px256 = x % 256;
                if (v < 0.86f && px256 > 118 && px256 < 138) { float pv = 0.55f - Mathf.Abs(px256 - 128) * 0.012f; c = new Color(pv, pv * 0.95f, pv * 0.86f); }
                if (v > 0.6f && v < 0.8f && px256 > 138 && px256 < 166) c = Hash(x / 256, 1) % 2 == 0 ? new Color(0.65f, 0.12f, 0.12f) : new Color(0.12f, 0.2f, 0.55f);
                px[y * W + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    // 縦 1px 幅のグラデーション(上=透明 → 下=暗い)。床の上 0.6m は明るいまま
    static Sprite MakeShade()
    {
        const int H = 64;
        var tex = NewTex(1, H, TextureWrapMode.Clamp);
        var px = new Color[H];
        for (int y = 0; y < H; y++)
        {
            float depth = 1f - y / (float)(H - 1); // 0=上 1=下
            float a = Mathf.Clamp01((depth - 0.04f) / 0.35f);
            px[y] = new Color(0.04f, 0.035f, 0.05f, Mathf.Lerp(0f, 0.86f, Mathf.SmoothStep(0f, 1f, a)));
        }
        tex.SetPixels(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    static Texture2D NewTex(int w, int h, TextureWrapMode wrap) => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
    static int Hash(int a, int b) { unchecked { int h = a * 73856093 ^ b * 19349663; h ^= h >> 13; return (h & 0x7fffffff); } }
    static float Noise(int x, int y) => (Hash(x / 4, y / 4) % 1000) / 1000f;
}

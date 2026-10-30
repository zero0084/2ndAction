using UnityEngine;

// 属性の演出と状態マークの絵(2026-10-30)。コードで描く(外部の素材なし)。最初に使う時に1回だけ作る。
//  粒: Flame / Spark / Crystal / Shard / Ring / Glow / Swirl / Streak / Drop(白〜明るい色で描き、頂点色で色を付ける)
//  マーク: 暗い丸の台 + 縁の色 + 中の印(文字は使わない = 30言語に影響しない)。明るい背景でも暗いボスでも見えるように、台は暗く縁は明るい
//  正式な絵(エステルと相談して作る物)があれば Resources/Elements/mark_<name>.png を優先する
public static class ElementArt
{
    public enum Mark { Burn, Chill, Freeze, Shock, Bleed, Wind }

    static Sprite flame, spark, crystal, shard, ring, glow, swirl, streak, drop;
    static readonly Sprite[] marks = new Sprite[6];

    public static Sprite Flame { get { if (flame == null) flame = Make(64, 96, FlameMask, Color.white, 1.2f); return flame; } }
    public static Sprite Spark { get { if (spark == null) spark = Make(32, 32, (x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)) * 1.0f, Color.white, 1.6f, soft: true); return spark; } }
    public static Sprite Glow { get { if (glow == null) glow = Make(64, 64, (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 1.6f), Color.white, 1f, soft: true); return glow; } }
    public static Sprite Crystal { get { if (crystal == null) crystal = Make(96, 96, CrystalMask, Color.white, 1f); return crystal; } }
    public static Sprite Shard { get { if (shard == null) shard = Make(32, 64, (x, y) => Mathf.Abs(x) + Mathf.Abs(y) * 0.45f < 0.5f ? 1f : 0f, Color.white, 1f); return shard; } }
    public static Sprite Ring { get { if (ring == null) ring = Make(96, 96, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.12f); }, Color.white, 1f, soft: true); return ring; } }
    public static Sprite Swirl { get { if (swirl == null) swirl = Make(96, 96, SwirlMask, Color.white, 1f, soft: true); return swirl; } }
    public static Sprite Streak { get { if (streak == null) streak = Make(96, 16, (x, y) => Mathf.Clamp01(1f - Mathf.Abs(y) * 1.4f) * Mathf.Clamp01(1f - Mathf.Abs(x)) * Mathf.Clamp01((x + 1f) * 2f), Color.white, 1f, soft: true); return streak; } }
    public static Sprite Drop { get { if (drop == null) drop = Make(32, 44, DropMask, Color.white, 1f); return drop; } }

    // ---- 形(-1..1 の座標。返り値 0..1 = 濃さ)
    // 炎(エステルの案): 大きい山と小さい山の2つ。先が横へ曲がる(滴に見えない)
    static float FlameMask(float x, float y)
    {
        float yy = (y + 1f) * 0.5f;                                     // 0(下)..1(上)
        float baseW = yy < 0.3f ? Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow((0.3f - yy) / 0.3f, 2f))) * 0.6f : 0.6f;
        // 大きい山: 中心が上へ行くほど右へ曲がる / 小さい山: 左に低く
        float big = Tongue(x, yy, 0.05f, 0.95f, 0.55f, 0.32f);
        float small = Tongue(x, yy, -0.32f, 0.62f, 0.32f, -0.25f);
        float body = yy < 0.42f && Mathf.Abs(x) < baseW ? Mathf.Clamp01((baseW - Mathf.Abs(x)) / 0.1f) : 0f;
        return Mathf.Max(body, Mathf.Max(big, small));
    }
    // 舌の形: cx=根元の中心、top=先の高さ、w=根元の幅、bend=先の横へのずれ
    static float Tongue(float x, float yy, float cx, float top, float w, float bend)
    {
        if (yy > top) return 0f;
        float k = yy / top;
        float c = cx + bend * k * k;
        float half = w * Mathf.Pow(1f - k, 0.8f);
        float d = Mathf.Abs(x - c);
        return d < half ? Mathf.Clamp01((half - d) / 0.08f) : 0f;
    }
    static float OldFlameMask(float x, float y)
    {
        // 下が丸く上がとがった炎(左右に少し揺れた形)
        float yy = (y + 1f) * 0.5f;                 // 0(下)..1(上)
        float w = yy < 0.35f ? Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow((0.35f - yy) / 0.35f, 2f))) * 0.62f : 0.62f * Mathf.Pow(1f - (yy - 0.35f) / 0.65f, 1.4f);
        float cx = 0.08f * Mathf.Sin(yy * 6f);
        float d = Mathf.Abs(x - cx);
        return d < w ? Mathf.Clamp01((w - d) / 0.12f) * Mathf.Clamp01(1.05f - yy * 0.25f) : 0f;
    }
    static float CrystalMask(float x, float y)
    {
        // 6方向の細い結晶の光
        float a = Mathf.Atan2(y, x); float r = Mathf.Sqrt(x * x + y * y);
        float arm = Mathf.Abs(Mathf.Sin(a * 3f));
        float line = Mathf.Clamp01(1f - arm * 9f) * Mathf.Clamp01(1f - r);
        float core = Mathf.Clamp01(1f - r * 3.2f);
        return Mathf.Max(line, core);
    }
    static float SwirlMask(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y); if (r > 0.95f || r < 0.12f) return 0f;
        float a = Mathf.Atan2(y, x);
        float phase = Mathf.Repeat(a / (Mathf.PI * 2f) + r * 1.1f, 0.5f) / 0.5f;    // 2本の腕の渦
        float band = Mathf.Clamp01(1f - Mathf.Abs(phase - 0.5f) * 4f);
        return band * Mathf.Clamp01((0.95f - r) * 6f);
    }
    static float DropMask(float x, float y)
    {
        // 下が丸い滴(上がとがる)
        float yy = (y + 1f) * 0.5f;
        float w = yy < 0.45f ? Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow((0.45f - yy) / 0.45f, 2f))) * 0.85f : 0.85f * (1f - (yy - 0.45f) / 0.55f);
        return Mathf.Abs(x) < w ? 1f : 0f;
    }

    // ---- マーク(台 + 印)
    static Sprite iceChunk;
    public static Sprite IceChunk { get { if (iceChunk == null) iceChunk = MakeIce(); return iceChunk; } }
    // 凍結の時に体の端に付く氷: 角ばった結晶(3本の柱)。中は水色→白、縁は濃い青(明るいボスの上でも形が見える)
    static Sprite MakeIce()
    {
        const int W = 64, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[W * H];
        System.Func<float, float, float> col = (x, y) =>
        {
            float best = 0f;
            foreach (var (cx, w, h, lean) in new[] { (0f, 0.26f, 0.95f, 0f), (-0.45f, 0.2f, 0.6f, -0.35f), (0.45f, 0.2f, 0.7f, 0.3f) })
            {
                float yy = (y + 1f) * 0.5f; float xx = x - cx - lean * yy;
                float top = h - Mathf.Abs(xx) * 1.4f;          // 先のとがった柱
                if (Mathf.Abs(xx) < w && yy < top) best = Mathf.Max(best, Mathf.Clamp01((w - Mathf.Abs(xx)) / 0.06f) * Mathf.Clamp01((top - yy) / 0.06f));
            }
            return best;
        };
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                float x = ((i + 0.5f) / W) * 2f - 1f, y = ((j + 0.5f) / H) * 2f - 1f;
                float v = col(x, y);
                float edge = Mathf.Max(col(x + 0.07f, y), col(x - 0.07f, y), col(x, y + 0.07f), col(x, y - 0.07f));
                Color c = Color.clear;
                if (edge > 0.5f) c = new Color(0.12f, 0.35f, 0.65f, 0.95f);                      // 縁
                if (v > 0.5f) c = Color.Lerp(new Color(0.55f, 0.85f, 1f, 0.92f), new Color(0.95f, 1f, 1f, 0.95f), (y + 1f) * 0.5f + (x > 0.1f ? 0.2f : 0f));
                px[j * W + i] = c;
            }
        tex.SetPixels(px); tex.Apply(false, true); tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.1f), W);
    }

    public static Sprite MarkSprite(Mark m)
    {
        int i = (int)m;
        if (marks[i] != null) return marks[i];
        var tex = Resources.Load<Texture2D>("Elements/mark_" + m.ToString().ToLowerInvariant());
        if (tex != null) return marks[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        Color rim, ink;
        System.Func<float, float, float> icon;
        switch (m)
        {
            case Mark.Burn: rim = ElementVfx.FireEdge; ink = new Color(1f, 0.72f, 0.25f); icon = (x, y) => FlameMask(x * 1.25f, y * 1.15f + 0.05f); break;
            case Mark.Chill: rim = ElementVfx.IceEdge; ink = new Color(0.75f, 0.93f, 1f); icon = SnowMask; break;
            case Mark.Freeze: rim = new Color(0.85f, 0.97f, 1f); ink = new Color(0.8f, 0.95f, 1f); icon = IceBlockMask; break;
            case Mark.Shock: rim = ElementVfx.BoltEdge; ink = new Color(1f, 0.95f, 0.4f); icon = BoltMask; break;
            case Mark.Bleed: rim = new Color(0.8f, 0.08f, 0.2f); ink = new Color(0.95f, 0.25f, 0.32f); icon = (x, y) => DropMask(x * 1.35f, y * 1.2f + 0.05f); break;
            default: rim = ElementVfx.WindEdge; ink = new Color(0.7f, 1f, 0.9f); icon = OpenSwirlMask; break;
        }
        return marks[i] = MakeMark(rim, ink, icon);
    }
    // 冷え(エステルの案): 太い6方向の雪結晶。細かな枝は付けない
    static float SnowMask(float x, float y)
    {
        float a = Mathf.Atan2(y, x); float r = Mathf.Sqrt(x * x + y * y); if (r > 0.82f) return 0f;
        float arm = Mathf.Abs(Mathf.Sin(a * 3f)) * r;           // 3本の線 = 6方向
        return arm < 0.13f || r < 0.2f ? 1f : 0f;
    }
    // 凍結(エステルの案): 太い菱形の氷塊(冷えの結晶とは別の外形)。中に1本の面の線
    static float IceBlockMask(float x, float y)
    {
        float d = Mathf.Abs(x) / 0.62f + Mathf.Abs(y) / 0.85f;
        if (d > 1f) return 0f;
        float facet = Mathf.Abs(x - y * 0.25f) < 0.05f && y > -0.2f ? 0f : 1f; // 面の切れ目(ハイライトの線)
        return facet;
    }
    // 風(エステルの案): 太い線の開いた渦。中心は空ける
    static float OpenSwirlMask(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y); if (r > 0.86f || r < 0.22f) return 0f;
        float a = Mathf.Atan2(y, x);
        float t = Mathf.Repeat(a / (Mathf.PI * 2f), 1f);                 // 0..1(1周)
        float want = 0.25f + 0.6f * t;                                   // 1周で外へ開く螺旋
        return Mathf.Abs(r - want) < 0.11f && t < 0.92f ? 1f : 0f;
    }
    static float BoltMask(float x, float y)
    {
        // 稲妻(ジグザグの帯)
        Vector2 p = new Vector2(x, y);
        Vector2[] pts = { new Vector2(0.25f, 0.85f), new Vector2(-0.3f, 0.05f), new Vector2(0.12f, 0.05f), new Vector2(-0.22f, -0.85f) };
        float best = 9f;
        for (int k = 0; k + 1 < pts.Length; k++)
        {
            Vector2 a = pts[k], b = pts[k + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
        }
        return best < 0.13f ? 1f : 0f;
    }

    static Sprite MakeMark(Color rim, Color ink, System.Func<float, float, float> icon)
    {
        const int N = 64; const int SS = 3;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[N * N];
        Color baseC = new Color(0.07f, 0.06f, 0.1f, 0.92f);
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                Color acc = Color.clear;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float x = ((i + (sx + 0.5f) / SS) / N) * 2f - 1f, y = ((j + (sy + 0.5f) / SS) / N) * 2f - 1f;
                        float r = Mathf.Sqrt(x * x + y * y);
                        Color c = Color.clear;
                        if (r < 1f)
                        {
                            c = r > 0.82f ? rim : baseC;                                     // 台と縁
                            if (r <= 0.82f) { float v = icon(x / 0.72f, y / 0.72f); if (v > 0.5f) c = Color.Lerp(ink, Color.white, Mathf.Clamp01(0.3f - y * 0.25f)); }
                        }
                        acc += c;
                    }
                px[j * N + i] = acc / (SS * SS);
            }
        tex.SetPixels(px); tex.Apply(false, true); tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
    }

    // 粒の絵: mask(-1..1) の濃さで白く塗る。soft = 濃さをそのままアルファに
    static Sprite Make(int w, int h, System.Func<float, float, float> mask, Color c, float gamma, bool soft = false)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = ((i + 0.5f) / w) * 2f - 1f, y = ((j + 0.5f) / h) * 2f - 1f;
                float v = Mathf.Clamp01(mask(x, y));
                if (gamma != 1f) v = Mathf.Pow(v, 1f / gamma);
                px[j * w + i] = new Color(c.r, c.g, c.b, soft ? v : Mathf.SmoothStep(0f, 1f, v));
            }
        tex.SetPixels(px); tex.Apply(false, true); tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(w, h));
    }
}

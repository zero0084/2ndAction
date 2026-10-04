using System.Collections.Generic;
using UnityEngine;

// #100 ULTIMATE(2026-10-04)の見た目。すべて手続き的な仮素材(点/輪/線/花びら/破片/札など)で、ゲーム上の処理(UltimateArt)とは独立。
// 本番の絵/VFX が届いたら、キャラごとの関数(Startup/Burst/Dash/Hit/Finish/Buff)の中身を差し替える(必要な素材の一覧は Docs/Ultimate100_2026-10-04.md)。
// 見た目の命中(HitVisual)は何度出してもよい(ゲーム上のダメージの回数とは別)。粒の数は MaxParticles で頭打ち。
public static class UltimateFx
{
    const int MaxParticles = 420;
    const int FxOrder = RenderOrder.SlashFx + 3;

    // ===================================================================== キャラの色
    public struct Theme { public Color main, sub, flash; }
    public static Theme ThemeOf(string ch) => ch switch
    {
        "swordsman" => new Theme { main = new Color(0.16f, 0.06f, 0.26f), sub = Color.white, flash = new Color(0.65f, 0.45f, 1f) },
        "noble_lady" => new Theme { main = new Color(1f, 0.55f, 0.75f), sub = new Color(1f, 0.25f, 0.42f), flash = new Color(1f, 0.86f, 0.92f) },
        "dual_blade" => new Theme { main = new Color(0.5f, 0.9f, 1f), sub = new Color(0.88f, 0.96f, 1f), flash = new Color(0.62f, 0.86f, 1f) },
        "gunslinger" => new Theme { main = new Color(1f, 0.62f, 0.15f), sub = new Color(1f, 0.95f, 0.5f), flash = new Color(1f, 0.85f, 0.35f) },
        "dragon_lancer" => new Theme { main = new Color(1f, 0.96f, 0.85f), sub = new Color(1f, 0.8f, 0.3f), flash = Color.white },
        "mage" => new Theme { main = new Color(0.7f, 0.42f, 1f), sub = new Color(1f, 0.86f, 0.42f), flash = new Color(0.86f, 0.72f, 1f) },
        "archer" => new Theme { main = new Color(0.5f, 1f, 0.5f), sub = new Color(1f, 0.95f, 0.6f), flash = new Color(0.82f, 1f, 0.72f) },
        "fighter" => new Theme { main = new Color(1f, 0.52f, 0.15f), sub = new Color(1f, 0.9f, 0.42f), flash = new Color(1f, 0.72f, 0.32f) },
        "ninja" => new Theme { main = new Color(0.22f, 0.16f, 0.34f), sub = new Color(0.78f, 0.72f, 0.92f), flash = new Color(0.62f, 0.52f, 0.92f) },
        "dragonkin" => new Theme { main = new Color(1f, 0.36f, 0.1f), sub = new Color(1f, 0.86f, 0.22f), flash = new Color(1f, 0.52f, 0.2f) },
        "vampire" => new Theme { main = new Color(0.78f, 0.05f, 0.12f), sub = new Color(0.16f, 0f, 0.06f), flash = new Color(1f, 0.22f, 0.28f) },
        "miko" => new Theme { main = new Color(1f, 0.3f, 0.3f), sub = new Color(1f, 1f, 0.95f), flash = new Color(1f, 0.8f, 0.5f) },
        _ => new Theme { main = new Color(1f, 0.8f, 0.3f), sub = Color.white, flash = Color.white },
    };
    public static Color ThemeColor(string ch) { var t = ThemeOf(ch); return ch == "swordsman" || ch == "ninja" || ch == "vampire" ? t.flash : t.main; }

    // 技の名前(画面に一瞬出す)
    public static string ArtName(string ch) => ch switch
    {
        "swordsman" => "黒一閃",
        "noble_lady" => "桜花乱咲薔薇ノ一太刀",
        "dual_blade" => "双舞",
        "gunslinger" => "オールレンジファイア",
        "dragon_lancer" => "龍殺凸",
        "mage" => "最高位魔法グラウンドゼロ",
        "archer" => "レインロード",
        "fighter" => "気合の連続パンチ",
        "ninja" => "高速影分身",
        "dragonkin" => "オーバーヒート",
        "vampire" => "ジ・エンド",
        "miko" => "爆撃結界",
        _ => "ULTIMATE",
    };

    // ===================================================================== 仮素材(手続き生成)
    static Sprite dot, disc, ring, streak, petal, shard, star, rect, bat;
    static Material glowMat;
    static bool spritesReady;

    static void EnsureSprites()
    {
        if (spritesReady && dot != null) return;
        spritesReady = true;
        dot = Make(64, 64, (u, v) => { float r = Mathf.Sqrt(u * u + v * v); return Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r); });
        disc = Make(64, 64, (u, v) => Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * 10f));
        ring = Make(96, 96, (u, v) => { float r = Mathf.Sqrt(u * u + v * v); return Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.1f); });
        // 1×1 単位の正方形(拡大率=長さ×太さ がそのまま m になる)。横は端だけ柔らかく、縦は中心が濃い
        streak = Make(64, 64, (u, v) => Mathf.Clamp01(1f - Mathf.Abs(v)) * Mathf.Clamp01((1f - Mathf.Abs(u)) * 3f) * Mathf.Clamp01(1.2f - Mathf.Abs(v) * 1.1f));
        petal = Make(48, 64, (u, v) => { float w = 0.75f * (1f - (v + 1f) * 0.5f * (v + 1f) * 0.5f) + 0.15f * (1f - v); return Mathf.Clamp01((w - Mathf.Abs(u)) * 8f) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 6f); });
        shard = Make(48, 48, (u, v) => { float half = (v + 1f) * 0.5f * 0.85f; return Mathf.Clamp01((half - Mathf.Abs(u)) * 10f) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 8f); });
        star = Make(64, 64, (u, v) => { float a = Mathf.Abs(u), b = Mathf.Abs(v); float s = Mathf.Max(Mathf.Clamp01(1f - a * 9f) * Mathf.Clamp01(1f - b), Mathf.Clamp01(1f - b * 9f) * Mathf.Clamp01(1f - a)); return Mathf.Clamp01(s + Mathf.Clamp01(1f - Mathf.Sqrt(a * a + b * b) * 3f)); });
        rect = Make(32, 32, (u, v) => Mathf.Clamp01((1f - Mathf.Abs(u)) * 8f) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 8f));
        bat = Make(64, 32, (u, v) => { float wing = Mathf.Abs(u); float top = 0.15f + 0.55f * Mathf.Sin(wing * Mathf.PI) - 0.25f * wing; float bot = -0.35f + 0.5f * wing * wing - 0.25f * Mathf.Abs(Mathf.Sin(wing * 9f)); return v < top && v > bot ? 1f : 0f; });
        glowMat = Resources.Load<Material>("Effects/SpriteGlow");
    }

    static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(w, h));
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    // HUD のボタン用(IMGUI)
    public static Texture2D HudDisc(int size, bool ringOnly)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                float a = ringOnly ? Mathf.Clamp01(1f - Mathf.Abs(r - 0.93f) / 0.05f) : Mathf.Clamp01((1f - r) * size * 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // ===================================================================== 粒(1つずつ動く)
    class P : MonoBehaviour
    {
        public SpriteRenderer sr;
        public float life, t, a0, a1, rotSpeed, drag;
        public Vector2 s0, s1;
        public Vector3 vel, acc, followOffset;
        public Color color;
        public Transform follow;
        public bool fadeIn;
        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            float u = t / Mathf.Max(0.0001f, life);
            if (u >= 1f) { Destroy(gameObject); return; }
            vel += acc * dt;
            if (drag > 0f) vel *= Mathf.Exp(-drag * dt);
            if (follow != null) { followOffset += vel * dt; transform.position = follow.position + followOffset; }
            else transform.position += vel * dt;
            if (rotSpeed != 0f) transform.Rotate(0f, 0f, rotSpeed * dt);
            Vector2 s = Vector2.Lerp(s0, s1, 1f - (1f - u) * (1f - u));
            transform.localScale = new Vector3(s.x, s.y, 1f);
            float a = Mathf.Lerp(a0, a1, u);
            if (fadeIn && u < 0.2f) a *= u / 0.2f;
            var c = color; c.a *= a; sr.color = c;
        }
        void OnDestroy() { live--; }
    }
    static int live;
    static Transform root;
    static Transform Root
    {
        get
        {
            if (root == null) { root = new GameObject("UltimateFxRoot").transform; live = 0; }
            return root;
        }
    }

    static P Spawn(Sprite s, Vector3 pos, Color c, float life, Vector2 s0, Vector2 s1, float a0 = 1f, float a1 = 0f, Vector3 vel = default, float rot = 0f, float rotSpeed = 0f, bool glow = false, Transform follow = null, int order = FxOrder, bool force = false)
    {
        EnsureSprites();
        if (!force && live >= MaxParticles) return null;
        var go = new GameObject("ufx");
        go.transform.SetParent(Root, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, 0f, rot);
        go.transform.localScale = new Vector3(s0.x, s0.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingOrder = order;
        if (glow && glowMat != null) sr.sharedMaterial = glowMat;
        var p = go.AddComponent<P>();
        p.sr = sr; p.life = life; p.a0 = a0; p.a1 = a1; p.s0 = s0; p.s1 = s1; p.vel = vel; p.rotSpeed = rotSpeed; p.color = c;
        p.follow = follow; if (follow != null) p.followOffset = pos - follow.position;
        sr.color = new Color(c.r, c.g, c.b, c.a * a0);
        live++;
        return p;
    }

    static void Streak(Vector3 pos, float angle, float length, float width, Color c, float life, bool glow = true, Vector3 vel = default, float grow = 1.15f)
        => Spawn(streak, pos, c, life, new Vector2(length * 0.7f, width), new Vector2(length * grow, width * 0.4f), 1f, 0f, vel, angle, 0f, glow);
    static void Ring(Vector3 pos, float r0, float r1, Color c, float life, bool glow = true)
        => Spawn(ring, pos, c, life, Vector2.one * r0 * 2f, Vector2.one * r1 * 2f, 1f, 0f, default, 0f, 0f, glow);
    static void Glow(Vector3 pos, float size0, float size1, Color c, float life, float a0 = 1f)
        => Spawn(dot, pos, c, life, Vector2.one * size0, Vector2.one * size1, a0, 0f, default, 0f, 0f, true);
    static void Scatter(Sprite s, Vector3 pos, int n, float speed, Color c, float life, float size, bool glow = false, float upBias = 0f, float spin = 0f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            var v = new Vector3(Mathf.Cos(a), Mathf.Sin(a) + upBias, 0f) * speed * Random.Range(0.5f, 1f);
            var p = Spawn(s, pos, c, life * Random.Range(0.7f, 1.1f), Vector2.one * size, Vector2.one * size * 0.4f, 1f, 0f, v, Random.value * 360f, spin * Random.Range(-1f, 1f), glow);
            if (p != null) p.drag = 2.5f;
        }
    }

    // ===================================================================== 画面の色(背景の暗転 / 手前の白飛び)
    class Tint : MonoBehaviour
    {
        public SpriteRenderer sr; public Color target; public float speed = 6f;
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
                float h = cam.orthographicSize * 2.4f;
                transform.localScale = new Vector3(h * cam.aspect + 2f, h, 1f);
            }
            sr.color = Color.Lerp(sr.color, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        }
    }
    static Tint backTint, frontTint;
    static Tint MakeTint(string name, int order)
    {
        EnsureSprites();
        var go = new GameObject(name);
        go.transform.SetParent(Root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = rect; sr.sortingOrder = order; sr.color = new Color(0, 0, 0, 0);
        var t = go.AddComponent<Tint>(); t.sr = sr; t.target = new Color(0, 0, 0, 0);
        return t;
    }
    static void BackTint(Color c, float speed = 6f) { if (backTint == null) backTint = MakeTint("ufxBackTint", RenderOrder.SkyCloud + 5); backTint.target = c; backTint.speed = speed; }
    static void Flash(Color c, float alpha, float fadeSpeed = 5f)
    {
        if (!GameSettings.ScreenShake && alpha > 0.5f) alpha = 0.5f; // 画面揺れOFFの人には強い白飛びも控えめに
        if (frontTint == null) frontTint = MakeTint("ufxFlash", 60);
        frontTint.sr.color = new Color(c.r, c.g, c.b, alpha);
        frontTint.target = new Color(c.r, c.g, c.b, 0f); frontTint.speed = fadeSpeed;
    }
    static void Shake(float mag, float dur) { var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null; if (cf != null) cf.Shake(mag, dur); }

    // ===================================================================== 残像
    static SpriteRenderer playerSr;
    static Transform playerT;
    static void Afterimage(Color c, float life, Vector3 offset = default, float a0 = 0.6f)
    {
        if (playerSr == null || playerSr.sprite == null) return;
        var p = Spawn(playerSr.sprite, playerSr.transform.position + offset, c, life, (Vector2)playerSr.transform.lossyScale, (Vector2)playerSr.transform.lossyScale, a0, 0f, default, playerSr.transform.eulerAngles.z, 0f, false, null, RenderOrder.Player - 1);
        if (p != null) p.sr.flipX = playerSr.flipX;
    }

    static Vector3 PlayerPos => playerT != null ? playerT.position + Vector3.up * 1.0f : Vector3.zero;
    static Rect View => UltimateArt.ViewRect(1f);
    static Vector3 RandomInView(float yMin = 0.15f, float yMax = 0.85f) { var v = View; return new Vector3(Random.Range(v.xMin, v.xMax), Mathf.Lerp(v.yMin, v.yMax, Random.Range(yMin, yMax)), 0f); }
    // 地面の少し上(敵がいそうな所)。プレイヤーより前を多めに
    static Vector3 ActionPoint(float up0 = 0.3f, float up1 = 2.6f)
    {
        var v = View;
        float x = Mathf.Lerp(v.xMin, v.xMax, Random.Range(0.3f, 0.97f));
        float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (g ?? (playerT != null ? playerT.position.y : v.center.y)) + Random.Range(up0, up1);
        return new Vector3(x, Mathf.Min(y, v.yMax - 1f), 0f);
    }
    static Vector3 OnGround(Vector3 p) { float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(p.x) : null; return g.HasValue ? new Vector3(p.x, g.Value + 0.05f, 0f) : p; }

    // 技の名前の表示(IMGUI、UltimateFxLabel が描く)
    public static string BannerText { get; private set; } = "";
    public static float BannerUntil { get; private set; }
    public static Color BannerColor { get; private set; } = Color.white;

    // ===================================================================== 流れ
    static string cur = "";
    static float afterT, trailT, buffT;

    public static void Begin(string ch, PlayerController pc)
    {
        cur = ch; afterT = trailT = 0f;
        playerT = pc != null ? pc.transform : null;
        var anim = pc != null ? pc.GetComponentInChildren<PlayerAnimator>() : null;
        playerSr = anim != null ? anim.GetComponentInChildren<SpriteRenderer>() : (pc != null ? pc.GetComponentInChildren<SpriteRenderer>() : null);
        var th = ThemeOf(ch);
        CameraFollow.UltimateZoom = 0.86f;
        BannerText = ArtName(ch); BannerUntil = Time.unscaledTime + 1.6f; BannerColor = ThemeColor(ch);
        UltimateFxLabel.Ensure();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        Vector3 p = PlayerPos;
        switch (ch)
        {
            case "swordsman": BackTint(new Color(0f, 0f, 0f, 0.55f)); Streak(p + new Vector3(0.6f, 0.2f, 0f), 0f, 2.5f, 0.12f, Color.white, 0.4f); break;
            case "noble_lady": BackTint(new Color(0.35f, 0.05f, 0.15f, 0.35f)); for (int i = 0; i < 18; i++) { float a = i / 18f * Mathf.PI * 2f; Spawn(petal, p + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 2.2f, th.main, 0.6f, Vector2.one * 0.5f, Vector2.one * 0.3f, 1f, 0f, new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * 3f, a * Mathf.Rad2Deg, 200f); } break;
            case "dual_blade": BackTint(new Color(0f, 0.05f, 0.15f, 0.4f)); for (int i = 0; i < 4; i++) Spawn(star, p + (Vector3)Random.insideUnitCircle * 1.2f, th.sub, 0.4f, Vector2.one * 0.3f, Vector2.one * 1.2f, 1f, 0f, default, 0f, 90f, true); break;
            case "gunslinger": BackTint(new Color(0.12f, 0.06f, 0f, 0.35f)); for (int i = 0; i < 6; i++) { float a = Mathf.Lerp(-70f, 110f, i / 5f) * Mathf.Deg2Rad; Spawn(rect, p + new Vector3(Mathf.Cos(a) * 1.7f, Mathf.Sin(a) * 1.4f, 0f), new Color(0.25f, 0.22f, 0.2f), 2.6f, new Vector2(0.15f, 0.15f), new Vector2(0.9f, 0.28f), 1f, 0.6f, default, a * Mathf.Rad2Deg * 0.2f, 0f, false, playerT); } break;
            case "dragon_lancer": BackTint(new Color(0.15f, 0.1f, 0f, 0.35f)); DragonChain(p, th, 0.9f); break;
            case "mage": BackTint(new Color(0.08f, 0.02f, 0.18f, 0.6f), 3f); MagicCircle(new Vector3(p.x, p.y - 0.95f, 0f), 1.8f, th, 1.2f); MagicCircle(new Vector3(p.x + 4f, View.yMax - 2.5f, 0f), 3f, th, 1.2f); break;
            case "archer": BackTint(new Color(0.02f, 0.1f, 0.04f, 0.35f)); MagicCircle(new Vector3(p.x + 3f, View.yMax - 2f, 0f), 2.6f, th, 1.4f); break;
            case "fighter": BackTint(new Color(0.2f, 0.06f, 0f, 0.35f)); for (int i = 0; i < 3; i++) Ring(p, 0.3f, 2.2f + i * 0.8f, th.main, 0.35f + i * 0.12f); Flash(th.flash, 0.25f, 8f); break;
            case "ninja": BackTint(new Color(0.02f, 0f, 0.06f, 0.5f)); Scatter(dot, p + Vector3.down * 0.6f, 14, 4f, new Color(0.6f, 0.6f, 0.65f, 0.8f), 0.6f, 1.1f); for (int i = 0; i < 4; i++) Afterimage(new Color(0.15f, 0.1f, 0.25f, 1f), 0.8f, new Vector3((i - 1.5f) * 1.2f, 0f, 0f), 0.7f); break;
            case "dragonkin": BackTint(new Color(0.3f, 0.06f, 0f, 0.35f)); Scatter(dot, p, 16, 3f, th.main, 0.8f, 0.4f, true, 1.2f); break;
            case "vampire": BackTint(new Color(0.05f, 0f, 0.02f, 0.7f), 4f); Bats(p, 12, th, 1.0f); break;
            case "miko": BackTint(new Color(0.15f, 0.02f, 0.02f, 0.3f)); for (int i = 0; i < 8; i++) { float a = i / 8f * Mathf.PI * 2f; Ofuda(p + new Vector3(Mathf.Cos(a) * 2f, Mathf.Sin(a) * 1.6f, 0f), th, 1.0f, playerT); } Ring(p, 2.2f, 2.4f, th.sub, 0.9f); break;
            default: BackTint(new Color(0, 0, 0, 0.4f)); break;
        }
        Glow(p, 0.5f, 3.5f, th.flash, 0.45f, 0.8f);
    }

    public static void Burst(string ch)
    {
        var th = ThemeOf(ch);
        Vector3 p = PlayerPos;
        var v = View;
        CameraFollow.UltimateZoom = 0.93f;
        Shake(0.18f, 0.25f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit);
        switch (ch)
        {
            case "swordsman":
                Streak(new Vector3(v.center.x, p.y, 0f), 0f, v.width * 1.1f, 0.9f, new Color(0.05f, 0f, 0.1f, 0.95f), 0.45f, false);
                Streak(new Vector3(v.center.x, p.y, 0f), 0f, v.width * 1.1f, 0.18f, Color.white, 0.4f);
                for (int i = 0; i < 6; i++) Streak(ActionPoint(0.2f, 4f), Random.Range(-35f, 35f), Random.Range(4f, 9f), 0.08f, Color.white, 0.35f);
                Flash(Color.white, 0.35f, 7f);
                break;
            case "noble_lady":
                for (int i = 0; i < 3; i++) PetalRing(p + Vector3.right * 2f, 1f + i, th, 0.7f + i * 0.15f);
                for (int i = 0; i < 40; i++) Spawn(petal, RandomInView(), Random.value < 0.5f ? th.main : th.sub, Random.Range(0.8f, 1.4f), Vector2.one * 0.45f, Vector2.one * 0.3f, 1f, 0f, new Vector3(Random.Range(-6f, -2f), Random.Range(-1.5f, 1f), 0f), Random.value * 360f, Random.Range(-300f, 300f));
                break;
            case "dual_blade":
                for (int i = 0; i < 14; i++) Streak(ActionPoint(0.2f, 3.5f), Random.Range(0f, 180f), Random.Range(3f, 7f), 0.12f, i % 2 == 0 ? th.main : th.sub, 0.25f + Random.value * 0.15f);
                Flash(th.flash, 0.25f, 9f);
                break;
            case "gunslinger":
                Glow(p + Vector3.right * 1.2f, 0.6f, 2.5f, th.sub, 0.2f);
                for (int i = 0; i < 12; i++) { var to = ActionPoint(); Tracer(p, to, th); }
                Scatter(dot, p + Vector3.left * 0.5f, 10, 2f, new Color(0.6f, 0.6f, 0.6f, 0.6f), 0.9f, 1f);
                break;
            case "dragon_lancer":
                Ring(p + Vector3.right * 2f, 0.5f, 7f, Color.white, 0.4f);
                Ring(p + Vector3.right * 2f, 0.3f, 4f, th.sub, 0.35f);
                Flash(Color.white, 0.6f, 4f);
                break;
            case "mage":
                for (int i = 0; i < 5; i++) { var q = OnGround(ActionPoint()); MagicCircle(q, 1.4f, th, 0.8f); Pillar(q, th, 0.7f); }
                Flash(th.flash, 0.3f, 6f);
                break;
            case "archer":
                ArrowRain(v, 40, th, 0.0f);
                break;
            case "fighter":
                for (int i = 0; i < 12; i++) Spawn(disc, p + new Vector3(1.2f + Random.value * 3.5f, Random.Range(-0.6f, 1.2f), 0f), th.main, 0.18f, Vector2.one * 0.5f, Vector2.one * 1.3f, 0.9f, 0f, new Vector3(10f, 0f, 0f), 0f, 0f, true);
                Ring(p + Vector3.right * 2f, 0.4f, 3f, th.sub, 0.3f);
                break;
            case "ninja":
                for (int i = 0; i < 10; i++) { var to = ActionPoint(); float a = Random.value * Mathf.PI * 2f; var from = to + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 5f; ShadowDash(from, to, th); }
                break;
            case "dragonkin":
                for (int i = 0; i < 46; i++) { var vel = Quaternion.Euler(0, 0, Random.Range(-14f, 14f)) * Vector3.right * Random.Range(12f, 22f); var q = Spawn(dot, p + Vector3.right * 0.8f, Random.value < 0.6f ? th.main : th.sub, Random.Range(0.4f, 0.7f), Vector2.one * 0.5f, Vector2.one * 1.6f, 1f, 0f, vel, 0f, 0f, true); if (q != null) q.drag = 1.5f; }
                Flash(th.flash, 0.25f, 6f);
                break;
            case "vampire":
                for (int i = 0; i < 7; i++) Crack(ActionPoint(0.5f, 4f), th, 0.9f);
                Bats(p, 10, th, 0.9f);
                break;
            case "miko":
                for (int i = 0; i < 10; i++) { var to = ActionPoint(); var q = Spawn(rect, p, th.sub, 0.35f, new Vector2(0.18f, 0.32f), new Vector2(0.18f, 0.32f), 1f, 1f, (to - p) / 0.35f, Random.value * 360f, 720f); }
                Ring(p, 1f, 6f, th.sub, 0.5f);
                break;
            default: Ring(p, 0.5f, 5f, th.flash, 0.4f); break;
        }
    }

    public static void DashStart(string ch, bool arena)
    {
        CameraFollow.UltimateZoom = arena ? 0.96f : 1.06f;
        var th = ThemeOf(ch);
        // 前進中の体のまわりの光(技の勢いで走っている見え方)
        Spawn(dot, PlayerPos, ThemeColor(ch), UltimateTuning.I.dashSeconds + 0.4f, Vector2.one * 4f, Vector2.one * 3f, 0.55f, 0.1f, default, 0f, 0f, true, playerT, RenderOrder.Player - 2, true);
        Spawn(streak, PlayerPos + Vector3.left * 3f, ThemeColor(ch), UltimateTuning.I.dashSeconds + 0.3f, new Vector2(7f, 1.2f), new Vector2(5f, 0.8f), 0.5f, 0f, default, 0f, 0f, true, playerT, RenderOrder.Player - 2, true);
        if (ch == "dragon_lancer") Flash(Color.white, 0.45f, 5f);
        if (ch == "archer") MagicCircle(PlayerPos + new Vector3(5f, 5f, 0f), 2.2f, th, 2f, playerT);
        if (ch == "dragonkin") { var q = Spawn(disc, PlayerPos + Vector3.right * 2.5f, th.sub, 2.2f, Vector2.one * 1.6f, Vector2.one * 2.2f, 0.95f, 0.6f, default, 0f, 0f, true, playerT); }
    }

    public static void ArenaPass(string ch)
    {
        var th = ThemeOf(ch);
        Vector3 p = PlayerPos;
        Streak(p + Vector3.left * 2f, 0f, 9f, 0.6f, th.flash, 0.35f);
        Streak(p + Vector3.left * 2f, 0f, 9f, 0.15f, Color.white, 0.3f);
        Ring(p, 0.6f, 4f, th.main, 0.35f);
        Shake(0.22f, 0.25f);
    }

    public static void Tick(UltimatePhase ph, float t, float dt)
    {
        var th = ThemeOf(cur);
        Vector3 p = PlayerPos;
        var v = View;
        if (ph == UltimatePhase.Startup)
        {
            // 力を溜める: まわりから粒が集まる
            trailT -= dt;
            if (trailT <= 0f)
            {
                trailT = 0.03f;
                float a = Random.value * Mathf.PI * 2f;
                var from = p + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 3.5f;
                Spawn(dot, from, th.flash, 0.3f, Vector2.one * 0.35f, Vector2.one * 0.1f, 0.2f, 1f, (p - from) / 0.3f, 0f, 0f, true);
            }
            return;
        }
        if (ph != UltimatePhase.Dash && ph != UltimatePhase.Arena) return;
        afterT -= dt; trailT -= dt;
        if (afterT <= 0f)
        {
            afterT = 0.035f;
            Color ac = cur switch { "swordsman" => new Color(0.08f, 0.02f, 0.15f, 1f), "ninja" => new Color(0.12f, 0.08f, 0.2f, 1f), "vampire" => new Color(0.6f, 0.05f, 0.1f, 1f), _ => th.main };
            Afterimage(ac, 0.22f);
            // 画面の流れる線
            for (int k = 0; k < 2; k++) Streak(new Vector3(Random.Range(v.center.x, v.xMax + 2f), Random.Range(v.yMin + 1f, v.yMax - 1f), 0f), 0f, Random.Range(5f, 11f), Random.Range(0.06f, 0.16f), new Color(1f, 1f, 1f, 0.45f), 0.22f, true, new Vector3(-90f, 0f, 0f), 1f);
        }
        if (trailT > 0f) return;
        trailT = 0.05f;
        switch (cur)
        {
            case "swordsman": Streak(p + Vector3.left * 1.5f, 0f, 4f, 0.5f, new Color(0.05f, 0f, 0.1f, 0.8f), 0.2f, false); if (Random.value < 0.3f) Streak(RandomInView(), Random.Range(-30f, 30f), 5f, 0.06f, Color.white, 0.2f); break;
            case "noble_lady": for (int i = 0; i < 2; i++) Spawn(petal, p + (Vector3)Random.insideUnitCircle, Random.value < 0.5f ? th.main : th.sub, 0.9f, Vector2.one * 0.4f, Vector2.one * 0.25f, 1f, 0f, new Vector3(Random.Range(-8f, -3f), Random.Range(-1f, 2f), 0f), Random.value * 360f, 260f); break;
            case "dual_blade": Streak(p + (Vector3)Random.insideUnitCircle * 1.2f, Random.Range(0f, 180f), 3f, 0.1f, th.sub, 0.15f); Afterimage(new Color(0.6f, 0.85f, 1f, 1f), 0.4f, new Vector3(-1.2f, 0.15f, 0f), 0.3f); break;
            case "gunslinger": for (int i = 0; i < 2; i++) Tracer(p, p + Quaternion.Euler(0, 0, Random.Range(-50f, 60f)) * Vector3.right * 14f, th); if (Random.value < 0.4f) Spawn(dot, p + Vector3.left, new Color(0.55f, 0.55f, 0.55f, 0.5f), 0.7f, Vector2.one * 0.6f, Vector2.one * 1.6f, 0.7f, 0f, new Vector3(-6f, 1f, 0f)); break;
            case "dragon_lancer": Ring(p + Vector3.right * 2.2f, 0.6f, 2f, Color.white, 0.15f); if (Random.value < 0.5f) DragonChain(p, th, 0.35f); break;
            case "mage": if (Random.value < 0.35f) MagicCircle(new Vector3(p.x, p.y - 0.95f, 0f), 1.2f, th, 0.5f); Spawn(star, p + (Vector3)Random.insideUnitCircle * 1.5f, th.sub, 0.4f, Vector2.one * 0.3f, Vector2.one * 0.1f, 1f, 0f, new Vector3(-5f, 0, 0), 0f, 180f, true); break;
            case "archer": ArrowRain(new Rect(p.x + 2f, v.yMin, v.width * 0.6f, v.height), 3, th, 0f); break;
            case "fighter": Spawn(disc, p + new Vector3(1.2f + Random.value * 2f, Random.Range(-0.4f, 1.1f), 0f), th.main, 0.12f, Vector2.one * 0.5f, Vector2.one * 1.1f, 0.9f, 0f, new Vector3(8f, 0f, 0f), 0f, 0f, true); break;
            case "ninja": Afterimage(new Color(0.12f, 0.08f, 0.2f, 1f), 0.3f, new Vector3(Random.Range(-2f, 2f), Random.Range(0f, 2.5f), 0f), 0.6f); if (Random.value < 0.4f) Spawn(dot, p + Vector3.down * 0.5f, new Color(0.6f, 0.6f, 0.65f, 0.6f), 0.5f, Vector2.one * 0.8f, Vector2.one * 1.6f, 0.6f, 0f, new Vector3(-4f, 1f, 0f)); break;
            case "dragonkin": for (int i = 0; i < 3; i++) Spawn(dot, new Vector3(p.x - Random.Range(0f, 3f), p.y - 1f, 0f), Random.value < 0.6f ? th.main : th.sub, 0.6f, Vector2.one * 0.6f, Vector2.one * 0.2f, 1f, 0f, new Vector3(-2f, Random.Range(2f, 4f), 0f), 0f, 0f, true); break;
            case "vampire": Bats(p, 2, th, 0.6f); Streak(p + Vector3.left * 1.5f, 0f, 3f, 0.25f, th.main, 0.2f); break;
            case "miko": if (Random.value < 0.5f) Explosion(new Vector3(p.x + Random.Range(4f, 12f), p.y + Random.Range(-0.5f, 2.5f), 0f), th, 0.8f); Ofuda(p + (Vector3)Random.insideUnitCircle, th, 0.5f, null); break;
        }
    }

    public static void Finish(string ch)
    {
        var th = ThemeOf(ch);
        Vector3 p = PlayerPos;
        var v = View;
        CameraFollow.UltimateZoom = 0.95f;
        Shake(0.25f, 0.3f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit);
        Vector3 front = p + Vector3.right * 4f;
        switch (ch)
        {
            case "swordsman": Streak(front, -32f, 16f, 1.2f, new Color(0.05f, 0f, 0.1f, 0.95f), 0.5f, false); Streak(front, -32f, 16f, 0.2f, Color.white, 0.45f); Flash(Color.white, 0.55f, 4f); break;
            case "noble_lady": for (int i = 0; i < 4; i++) PetalRing(front, 1.2f + i * 1.1f, th, 0.9f); Flash(th.flash, 0.35f, 4f); break;
            case "dual_blade": Streak(front, 45f, 12f, 0.4f, th.sub, 0.45f); Streak(front, -45f, 12f, 0.4f, th.main, 0.45f); Flash(th.flash, 0.35f, 5f); break;
            case "gunslinger": Ring(front, 0.6f, 6f, th.main, 0.45f); Glow(front, 2f, 7f, th.sub, 0.35f); Scatter(dot, front, 18, 5f, new Color(0.55f, 0.55f, 0.55f, 0.7f), 1.2f, 1.4f); break;
            case "dragon_lancer": Ring(front, 1f, 10f, Color.white, 0.5f); DragonChain(front, th, 0.7f); Flash(Color.white, 0.8f, 3f); break;
            case "mage": MagicCircle(front, 3.5f, th, 0.9f); Pillar(front, th, 0.8f, 3f); Flash(Color.white, 0.6f, 3f); break;
            case "archer": Streak(new Vector3(v.center.x, p.y + 0.5f, 0f), 0f, v.width, 0.5f, th.sub, 0.45f); Ring(front, 0.5f, 4f, th.main, 0.4f); break;
            case "fighter": Spawn(disc, front, th.main, 0.35f, Vector2.one * 1.5f, Vector2.one * 5f, 1f, 0f, Vector3.right * 6f, 0f, 0f, true); Ring(front, 1f, 8f, th.sub, 0.45f); Flash(th.flash, 0.4f, 5f); break;
            case "ninja": Scatter(dot, front, 30, 6f, new Color(0.65f, 0.65f, 0.7f, 0.85f), 1.1f, 1.8f); Ring(front, 0.5f, 5f, th.flash, 0.4f); break;
            case "dragonkin": Ring(front, 1f, 7f, th.main, 0.5f); Glow(front, 2f, 9f, th.sub, 0.45f); Scatter(dot, front, 24, 8f, th.main, 1f, 0.5f, true, 0.6f); break;
            case "vampire": for (int i = 0; i < 26; i++) Spawn(shard, new Vector3(v.center.x, v.center.y, 0f), Random.value < 0.5f ? Color.white : th.flash, 0.9f, Vector2.one * Random.Range(0.4f, 1.1f), Vector2.one * 0.2f, 1f, 0f, (Vector3)Random.insideUnitCircle.normalized * Random.Range(8f, 18f), Random.value * 360f, Random.Range(-400f, 400f)); Flash(Color.white, 0.5f, 4f); break;
            case "miko": for (int i = 0; i < 7; i++) Explosion(new Vector3(Mathf.Lerp(v.xMin + 2f, v.xMax - 2f, i / 6f), p.y + Random.Range(-0.5f, 2.5f), 0f), th, 1.3f); Shake(0.3f, 0.35f); break;
            default: Ring(front, 0.5f, 6f, th.flash, 0.45f); break;
        }
    }

    public static void End(string ch)
    {
        BackTint(new Color(0, 0, 0, 0), 4f);
        CameraFollow.UltimateZoom = 1f;
    }

    // ===================================================================== 命中(見た目だけ)
    public static void HitVisual(string ch, Vector3 at, bool killed, bool boss = false)
    {
        var th = ThemeOf(ch);
        float s = boss ? 1.8f : 1f;
        switch (ch)
        {
            case "swordsman": Streak(at, 30f, 2.6f * s, 0.1f, Color.white, 0.25f); Streak(at, -40f, 2.2f * s, 0.1f, th.flash, 0.25f); break;
            case "noble_lady": PetalRing(at, 0.6f * s, th, 0.5f); break;
            case "dual_blade": Streak(at, 45f, 2.4f * s, 0.12f, th.sub, 0.22f); Streak(at, -45f, 2.4f * s, 0.12f, th.main, 0.22f); break;
            case "gunslinger": for (int i = 0; i < 3; i++) Spawn(star, at + (Vector3)Random.insideUnitCircle * 0.5f * s, th.sub, 0.15f, Vector2.one * 0.3f, Vector2.one * 0.8f * s, 1f, 0f, default, Random.value * 90f, 0f, true); break;
            case "dragon_lancer": Ring(at, 0.3f, 1.6f * s, Color.white, 0.25f); break;
            case "mage": Pillar(at, th, 0.45f, s); break;
            case "archer": Streak(at + new Vector3(-0.4f, 0.6f, 0f), -60f, 1.4f, 0.1f, th.sub, 0.35f, true, default, 1f); Spawn(star, at, th.main, 0.2f, Vector2.one * 0.3f, Vector2.one * s, 1f, 0f, default, 0f, 0f, true); break;
            case "fighter": Ring(at, 0.2f, 1.4f * s, th.sub, 0.2f); Spawn(disc, at, th.main, 0.12f, Vector2.one * 0.4f, Vector2.one * 1.2f * s, 0.9f, 0f, default, 0f, 0f, true); break;
            case "ninja": Streak(at, 35f, 2.2f * s, 0.12f, th.flash, 0.2f); Streak(at, -35f, 2.2f * s, 0.12f, new Color(0.1f, 0.05f, 0.2f, 1f), 0.2f, false); break;
            case "dragonkin": Scatter(dot, at, 6, 4f, th.main, 0.4f, 0.4f * s, true, 0.8f); break;
            case "vampire": Crack(at, th, 0.5f * s); break;
            case "miko": Explosion(at, th, 0.7f * s); break;
            default: Ring(at, 0.2f, 1.2f * s, th.flash, 0.2f); break;
        }
        if (killed) Glow(at, 0.4f, 1.6f, th.flash, 0.25f, 0.7f);
    }

    // ===================================================================== BUFF(余韻)
    public static void BuffStart(string ch) { buffT = 0f; }
    public static void BuffEnd(string ch) { }
    public static void BuffTick(string ch, float dt, float frac)
    {
        if (playerT == null) { var pc = PlayerController.Instance; if (pc == null) return; playerT = pc.transform; var anim = pc.GetComponentInChildren<PlayerAnimator>(); playerSr = anim != null ? anim.GetComponentInChildren<SpriteRenderer>() : null; }
        buffT -= dt;
        if (buffT > 0f) return;
        var th = ThemeOf(ch);
        Vector3 p = PlayerPos;
        buffT = 0.12f;
        switch (ch)
        {
            case "swordsman": Afterimage(new Color(0.08f, 0.02f, 0.15f, 1f), 0.25f, default, 0.45f); break;
            case "noble_lady": Spawn(petal, p + new Vector3(Random.Range(-1f, 1.5f), 1.4f, 0f), Random.value < 0.5f ? th.main : th.sub, 1.0f, Vector2.one * 0.3f, Vector2.one * 0.2f, 0.9f, 0f, new Vector3(-2.5f, -1.2f, 0f), Random.value * 360f, 200f); break;
            case "dual_blade": Afterimage(new Color(0.6f, 0.85f, 1f, 1f), 0.25f, new Vector3(-0.9f, 0f, 0f), 0.3f); break;
            case "gunslinger": if (Random.value < 0.35f) Spawn(star, p + new Vector3(0.8f, 0.1f, 0f), th.sub, 0.1f, Vector2.one * 0.3f, Vector2.one * 0.7f, 1f, 0f, default, 0f, 0f, true); break;
            case "dragon_lancer": if (Random.value < 0.3f) Ring(p + Vector3.right * 1.2f, 0.3f, 1.3f, Color.white, 0.2f); break;
            case "mage": buffT = 0.6f; MagicCircle(new Vector3(p.x, p.y - 0.95f, 0f), 0.9f, th, 0.6f, playerT); break;
            case "archer": buffT = 0.45f; ArrowRain(new Rect(p.x + 3f, p.y - 1f, 6f, 6f), 1, th, 0f); break;
            case "fighter": Spawn(dot, p + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.8f, 0.6f), 0f), th.main, 0.5f, Vector2.one * 0.4f, Vector2.one * 0.15f, 0.8f, 0f, new Vector3(-1f, 2.5f, 0f), 0f, 0f, true); break;
            case "ninja": buffT = 0.08f; Afterimage(new Color(0.12f, 0.08f, 0.2f, 1f), 0.3f, new Vector3(-1.1f, 0f, 0f), 0.4f); break;
            case "dragonkin": buffT = Mathf.Lerp(0.5f, 0.06f, frac); Spawn(dot, p + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.6f, 0.8f), 0f), Random.value < 0.6f ? th.main : th.sub, 0.6f, Vector2.one * 0.35f, Vector2.one * 0.1f, 1f, 0f, new Vector3(-1.5f, 2.5f, 0f), 0f, 0f, true); break;
            case "vampire": buffT = 0.35f; Bats(p, 1, th, 0.8f); break;
            case "miko": buffT = 0.5f; for (int i = 0; i < 3; i++) { float a = Time.time * 2f + i * 2.09f; Ofuda(p + new Vector3(Mathf.Cos(a) * 1.2f, 0.6f + Mathf.Sin(a) * 0.6f, 0f), th, 0.5f, playerT); } break;
        }
    }

    // ===================================================================== ポーズ(PlayerAnimator)
    public static Sprite[] PoseFrames(UltimatePhase ph, string ch, PlayerAnimator anim)
    {
        if (anim == null) return null;
        Sprite[] atk = anim.attackFramesLarge != null && anim.attackFramesLarge.Length > 0 ? anim.attackFramesLarge : anim.attackFrames;
        if (atk == null || atk.Length == 0) return null;
        switch (ph)
        {
            case UltimatePhase.Startup: case UltimatePhase.Burst: case UltimatePhase.Finish: return atk;
            case UltimatePhase.Arena: return atk;
            case UltimatePhase.Dash: return ch == "dragon_lancer" || ch == "fighter" || ch == "dual_blade" ? atk : null;
        }
        return null;
    }

    // ===================================================================== 部品
    static void PetalRing(Vector3 c, float r, Theme th, float life)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(r * 8f), 6, 26);
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Spawn(petal, c + dir * r * 0.3f, i % 3 == 0 ? th.sub : th.main, life, Vector2.one * 0.5f, Vector2.one * 0.35f, 1f, 0f, dir * r * 2.2f, a * Mathf.Rad2Deg - 90f, 120f);
        }
    }
    static void Tracer(Vector3 from, Vector3 to, Theme th)
    {
        Vector3 d = to - from; float len = d.magnitude; if (len < 0.1f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Spawn(streak, from + d * 0.5f, th.sub, 0.09f, new Vector2(len, 0.1f), new Vector2(len, 0.03f), 1f, 0f, default, ang, 0f, true);
        Spawn(star, to, th.main, 0.12f, Vector2.one * 0.3f, Vector2.one * 0.7f, 1f, 0f, default, 0f, 0f, true);
    }
    static void DragonChain(Vector3 head, Theme th, float life)
    {
        for (int i = 0; i < 16; i++)
        {
            float k = i / 15f;
            var q = head + new Vector3(-k * 6f, Mathf.Sin(k * Mathf.PI * 2.2f) * 1.2f + 1.5f * k, 0f);
            Spawn(dot, q, Color.Lerp(th.sub, th.main, k), life, Vector2.one * Mathf.Lerp(1.1f, 0.35f, k), Vector2.one * Mathf.Lerp(1.4f, 0.1f, k), 0.9f, 0f, new Vector3(4f, 0f, 0f), 0f, 0f, true);
        }
    }
    static void MagicCircle(Vector3 c, float r, Theme th, float life, Transform follow = null)
    {
        Spawn(ring, c, th.main, life, new Vector2(r * 2f, r * 0.8f), new Vector2(r * 2.1f, r * 0.85f), 0.95f, 0f, default, 0f, 0f, true, follow);
        Spawn(ring, c, th.sub, life, new Vector2(r * 1.4f, r * 0.56f), new Vector2(r * 1.45f, r * 0.58f), 0.9f, 0f, default, 0f, 0f, true, follow);
        var st = Spawn(star, c, th.sub, life, new Vector2(r * 1.2f, r * 0.5f), new Vector2(r * 1.25f, r * 0.52f), 0.8f, 0f, default, 0f, 0f, true, follow);
        if (st != null) st.fadeIn = true;
    }
    static void Pillar(Vector3 at, Theme th, float life, float s = 1f)
    {
        var v = View;
        float h = (v.yMax - at.y) + 2f;
        Spawn(streak, new Vector3(at.x, at.y + h * 0.5f, 0f), th.sub, life, new Vector2(h, 0.9f * s), new Vector2(h, 0.1f), 1f, 0f, default, 90f, 0f, true);
        Spawn(streak, new Vector3(at.x, at.y + h * 0.5f, 0f), Color.white, life * 0.7f, new Vector2(h, 0.3f * s), new Vector2(h, 0.05f), 1f, 0f, default, 90f, 0f, true);
    }
    static void ArrowRain(Rect area, int n, Theme th, float delay)
    {
        for (int i = 0; i < n; i++)
        {
            var start = new Vector3(Random.Range(area.xMin, area.xMax) + 3f, area.yMax + 1f, 0f);
            var vel = new Vector3(-9f, -26f, 0f);
            float ang = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            Spawn(streak, start, i % 3 == 0 ? th.sub : th.main, Random.Range(0.45f, 0.75f), new Vector2(1.6f, 0.08f), new Vector2(1.6f, 0.06f), 1f, 0.7f, vel, ang, 0f, true);
        }
    }
    static void ShadowDash(Vector3 from, Vector3 to, Theme th)
    {
        Vector3 d = to - from; float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Spawn(streak, from + d * 0.5f, new Color(0.1f, 0.05f, 0.2f, 0.9f), 0.25f, new Vector2(d.magnitude * 0.6f, 0.35f), new Vector2(d.magnitude, 0.1f), 1f, 0f, default, ang, 0f, false);
        Spawn(star, to, th.flash, 0.2f, Vector2.one * 0.3f, Vector2.one * 1.1f, 1f, 0f, default, 0f, 0f, true);
    }
    static void Bats(Vector3 c, int n, Theme th, float life)
    {
        for (int i = 0; i < n; i++)
        {
            var v = (Vector3)Random.insideUnitCircle.normalized * Random.Range(3f, 7f) + new Vector3(-1f, 1f, 0f);
            var p = Spawn(bat, c + (Vector3)Random.insideUnitCircle, Random.value < 0.6f ? new Color(0.08f, 0f, 0.04f, 1f) : th.main, life, new Vector2(0.7f, 0.35f), new Vector2(0.5f, 0.15f), 1f, 0f, v, Random.Range(-20f, 20f), 0f, false);
            if (p != null) p.drag = 1f;
        }
    }
    static void Crack(Vector3 at, Theme th, float s)
    {
        Vector3 q = at;
        float ang = Random.value * 360f;
        for (int i = 0; i < 5; i++)
        {
            float len = Random.Range(0.8f, 1.8f) * s;
            ang += Random.Range(-55f, 55f);
            var dir = Quaternion.Euler(0, 0, ang) * Vector3.right;
            Spawn(streak, q + dir * len * 0.5f, th.flash, 0.6f, new Vector2(len, 0.12f), new Vector2(len, 0.08f), 1f, 0f, default, ang, 0f, true);
            q += dir * len;
        }
    }
    static void Ofuda(Vector3 at, Theme th, float life, Transform follow)
    {
        Spawn(rect, at, th.sub, life, new Vector2(0.22f, 0.4f), new Vector2(0.22f, 0.4f), 1f, 0.4f, default, Random.Range(-15f, 15f), 0f, false, follow);
        Spawn(rect, at, th.main, life, new Vector2(0.1f, 0.22f), new Vector2(0.1f, 0.22f), 1f, 0.4f, default, 0f, 0f, false, follow, FxOrder + 1);
    }
    static void Explosion(Vector3 at, Theme th, float s)
    {
        Glow(at, 0.5f * s, 2.2f * s, th.flash, 0.3f);
        Ring(at, 0.3f * s, 1.8f * s, th.main, 0.3f);
        Scatter(dot, at, 5, 4f * s, th.flash, 0.35f, 0.35f * s, true, 0.5f);
    }

    public static void ClearAll()
    {
        if (root != null) Object.Destroy(root.gameObject);
        root = null; backTint = null; frontTint = null; live = 0;
        CameraFollow.UltimateZoom = 1f; CameraFollow.UltimateLookAhead = 0f;
        BannerUntil = 0f;
    }
}

// 技の名前(画面中央の少し上に一瞬)。IMGUI なので時間停止の影響を受けない
public class UltimateFxLabel : MonoBehaviour
{
    static UltimateFxLabel inst;
    public static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("UltimateFxLabel");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<UltimateFxLabel>();
    }
    GUIStyle st;
    // 初めて技の名前を出すフレームで日本語の文字の焼き込みが重ならないよう、カードを持ってボタンが出た時に先に済ませる(DiagnosticsOverlay と同じ)
    static int warmedSize = -1;
    public static void WarmIfNeeded()
    {
        int size = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.065f, 26f, 90f));
        if (warmedSize == size) return;
        warmedSize = size;
        var sb = new System.Text.StringBuilder("ULTIMATE READY BUFF MULTI× 0123456789.%s!");
        foreach (var ch in new[] { "swordsman", "noble_lady", "dual_blade", "gunslinger", "dragon_lancer", "mage", "archer", "fighter", "ninja", "dragonkin", "vampire", "miko" }) sb.Append(UltimateFx.ArtName(ch));
        Font f = GUI.skin.font;
        if (f != null) { f.RequestCharactersInTexture(sb.ToString(), size, FontStyle.Bold); f.RequestCharactersInTexture("カードなし発動中選択中ボス登場中今は使えない被弾中BONUS中準備中", Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.12f, 84f, 160f) * 0.22f), FontStyle.Bold); }
    }
    void OnGUI()
    {
        float left = UltimateFx.BannerUntil - Time.unscaledTime;
        if (left <= 0f || string.IsNullOrEmpty(UltimateFx.BannerText)) return;
        GUI.depth = -45;
        if (st == null) st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        float a = Mathf.Clamp01(left / 0.4f) * Mathf.Clamp01((1.6f - left) / 0.15f);
        st.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.065f, 26f, 90f));
        Rect r = new Rect(0f, Screen.height * 0.24f, Screen.width, Screen.height * 0.12f);
        var c = UltimateFx.BannerColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.7f * a);
        GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), UltimateFx.BannerText, st);
        st.normal.textColor = new Color(Mathf.Lerp(c.r, 1f, 0.35f), Mathf.Lerp(c.g, 1f, 0.35f), Mathf.Lerp(c.b, 1f, 0.35f), a);
        GUI.Label(r, UltimateFx.BannerText, st);
    }
}

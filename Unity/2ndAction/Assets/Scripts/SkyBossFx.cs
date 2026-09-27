using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 天空回廊ボス追加(2026-09-25) - 天空回廊ボス群(SkyBosses.cs)専用の見た目/音/ハザード部品。
// 荒野街道(BossCombatParts.cs)・自然洞窟(CaveBossFx.cs)と同じ方針:
//  - VFXは Assets/Resources/Effects/<name>.png があればそれを使い、無ければ手続き的に生成する。
//  - ダメージは「見えている攻撃」と同じ範囲だけ(落雷=稲妻の柱、拳=着弾点、光柱=光の柱)。
//  - 地面に置く予告/判定は、プレイヤーの基本オートラン速度で一緒に流れる(TrackedHazardと同じ前提)。
public static class SkyBossFx
{
    static Sprite bolt, groundGlow, fist, feather, cloudPuff, fin, spear, beam, tentacle, shadow;
    static readonly Dictionary<SkyBossKind, Sprite> placeholders = new Dictionary<SkyBossKind, Sprite>();

    static Sprite LoadEffectArt(string name) => Resources.Load<Sprite>("Effects/" + name);

    static Sprite Make(int w, int h, System.Func<float, float, float> alphaAt)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(alphaAt((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        // 常に「scale=1で1x1unit」(BossFx.Makeと同じ契約) - 呼び出し側がlocalScaleで寸法を決める。
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(w, h), 0, SpriteMeshType.FullRect, Vector4.zero, false);
    }

    static Sprite MakeRect(int w, int h, System.Func<float, float, float> alphaAt)
    {
        // 非正方形でも1x1unitに収まるよう、PPUを幅/高さ別に揃えられないため正方形テクスチャに描く。
        int s = Mathf.Max(w, h);
        return Make(s, s, alphaAt);
    }

    static float Ellipse(float u, float v, float cx, float cy, float rx, float ry, float edge = 0.08f)
    {
        float dx = (u - cx) / Mathf.Max(0.001f, rx), dy = (v - cy) / Mathf.Max(0.001f, ry);
        return Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) / edge);
    }

    // 縦の稲妻(下=着弾点)。ギザギザの芯+にじみ。
    public static Sprite Bolt()
    {
        if (bolt != null) return bolt;
        bolt = LoadEffectArt("skybolt");
        if (bolt != null) return bolt;
        bolt = Make(96, 96, (u, v) =>
        {
            float zig = 0.5f + 0.16f * Mathf.Sin(v * 23f) + 0.08f * Mathf.Sin(v * 57f + 1.3f);
            float d = Mathf.Abs(u - zig);
            float core = Mathf.Clamp01(1f - d / 0.05f);
            float glow = Mathf.Clamp01(1f - d / 0.22f) * 0.45f;
            float branch = 0f;
            if (v > 0.35f && v < 0.6f) branch = Mathf.Clamp01(1f - Mathf.Abs(u - (zig + (v - 0.35f) * 1.3f)) / 0.035f) * 0.8f;
            return Mathf.Max(core, Mathf.Max(glow, branch));
        });
        return bolt;
    }

    // 地面の予告(横長の光る楕円)。
    public static Sprite GroundGlow()
    {
        if (groundGlow != null) return groundGlow;
        groundGlow = Make(64, 64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.18f);
            float fill = Mathf.Clamp01(1f - r) * 0.55f;
            return Mathf.Max(ring, fill);
        });
        return groundGlow;
    }

    // 天空タイタンの拳(上から落ちてくる巨大な握り拳)。
    public static Sprite Fist()
    {
        if (fist != null) return fist;
        fist = LoadEffectArt("titanfist");
        if (fist != null) return fist;
        fist = Make(96, 96, (u, v) =>
        {
            float palm = Ellipse(u, v, 0.5f, 0.38f, 0.40f, 0.30f);
            float knuckles = 0f;
            for (int i = 0; i < 4; i++) knuckles = Mathf.Max(knuckles, Ellipse(u, v, 0.22f + i * 0.19f, 0.16f, 0.1f, 0.12f));
            float arm = (u > 0.3f && u < 0.7f && v > 0.55f) ? 1f : 0f;
            return Mathf.Max(Mathf.Max(palm, knuckles), arm);
        });
        return fist;
    }

    public static Sprite Feather()
    {
        if (feather != null) return feather;
        feather = LoadEffectArt("flamefeather");
        if (feather != null) return feather;
        feather = Make(64, 64, (u, v) =>
        {
            float body = Ellipse(u, v, 0.5f, 0.5f, 0.46f, 0.16f);
            float shaft = Mathf.Abs(v - 0.5f) < 0.02f ? 1f : 0f;
            return Mathf.Max(body * (0.6f + 0.4f * u), shaft);
        });
        return feather;
    }

    public static Sprite CloudPuff()
    {
        if (cloudPuff != null) return cloudPuff;
        cloudPuff = LoadEffectArt("cloudpuff");
        if (cloudPuff != null) return cloudPuff;
        cloudPuff = Make(96, 96, (u, v) =>
        {
            float a = Ellipse(u, v, 0.5f, 0.42f, 0.42f, 0.22f, 0.3f);
            a = Mathf.Max(a, Ellipse(u, v, 0.32f, 0.55f, 0.2f, 0.2f, 0.3f));
            a = Mathf.Max(a, Ellipse(u, v, 0.6f, 0.6f, 0.24f, 0.24f, 0.3f));
            return a;
        });
        return cloudPuff;
    }

    // 雲海から突き出る背びれ(三角)。
    public static Sprite Fin()
    {
        if (fin != null) return fin;
        fin = Make(64, 64, (u, v) =>
        {
            float edge = Mathf.Lerp(0.15f, 0.62f, v);
            float left = 0.12f + v * 0.5f;
            return (u > left && u < 0.9f - v * 0.3f && v < 0.92f) ? Mathf.Clamp01((0.92f - v) * 8f) : 0f;
        });
        return fin;
    }

    public static Sprite Spear()
    {
        if (spear != null) return spear;
        spear = LoadEffectArt("thunderspear");
        if (spear != null) return spear;
        spear = Make(96, 96, (u, v) =>
        {
            float dv = Mathf.Abs(v - 0.5f);
            float taper = 0.1f * (1f - Mathf.Abs(u - 0.45f) * 1.9f);
            float core = dv < taper ? 1f : 0f;
            float glow = Mathf.Clamp01(1f - dv / (taper + 0.08f)) * 0.5f;
            return u < 0.03f || u > 0.97f ? 0f : Mathf.Max(core, glow);
        });
        return spear;
    }

    // 横長のブレス/ビーム(中心が明るく、縁がにじむ)。
    public static Sprite Beam()
    {
        if (beam != null) return beam;
        beam = Make(64, 64, (u, v) =>
        {
            float dv = Mathf.Abs(v - 0.5f) * 2f;
            float core = Mathf.Clamp01(1f - dv / 0.45f);
            float wobble = 0.85f + 0.15f * Mathf.Sin(u * 40f);
            return Mathf.Clamp01(core * wobble * 1.2f);
        });
        return beam;
    }

    public static Sprite Tentacle()
    {
        if (tentacle != null) return tentacle;
        tentacle = Make(96, 96, (u, v) =>
        {
            float center = 0.5f + 0.18f * Mathf.Sin(u * 11f);
            float thick = Mathf.Lerp(0.14f, 0.03f, u);
            float d = Mathf.Abs(v - center);
            return Mathf.Clamp01(1f - d / thick) + Mathf.Clamp01(1f - d / (thick * 2.2f)) * 0.3f;
        });
        return tentacle;
    }

    // 雲海の下を進む巨大な影(リヴァイアサン)。
    public static Sprite Shadow()
    {
        if (shadow != null) return shadow;
        shadow = Make(96, 96, (u, v) => Ellipse(u, v, 0.5f, 0.5f, 0.48f, 0.22f, 0.6f));
        return shadow;
    }

    // 素材(skyArt[])が無いkindの暫定ボディ。種類ごとに輪郭だけ変える(実イラストが入れば到達しない)。
    public static Sprite Placeholder(SkyBossKind kind)
    {
        if (placeholders.TryGetValue(kind, out Sprite s) && s != null) return s;
        switch (kind)
        {
            case SkyBossKind.Behemoth:
                s = Make(128, 128, (u, v) => Mathf.Max(Mathf.Max(Ellipse(u, v, 0.5f, 0.5f, 0.38f, 0.2f), Ellipse(u, v, 0.14f, 0.6f, 0.12f, 0.12f)),
                    Mathf.Max(Box(u, v, 0.26f, 0.2f, 0.05f, 0.18f), Box(u, v, 0.72f, 0.2f, 0.05f, 0.18f))));
                break;
            case SkyBossKind.Titan:
                s = Make(128, 128, (u, v) => Mathf.Max(Mathf.Max(Ellipse(u, v, 0.5f, 0.52f, 0.26f, 0.3f), Ellipse(u, v, 0.5f, 0.88f, 0.1f, 0.1f)),
                    Mathf.Max(Ellipse(u, v, 0.2f, 0.5f, 0.08f, 0.28f), Ellipse(u, v, 0.8f, 0.5f, 0.08f, 0.28f))));
                break;
            case SkyBossKind.Jellyfish:
                s = Make(128, 128, (u, v) =>
                {
                    float bell = v > 0.5f ? Ellipse(u, v, 0.5f, 0.6f, 0.36f, 0.3f) : 0f;
                    float tent = 0f;
                    for (int i = 0; i < 5; i++) tent = Mathf.Max(tent, Box(u, v, 0.26f + i * 0.12f + Mathf.Sin(v * 12f + i) * 0.02f, 0.3f, 0.015f, 0.26f));
                    return Mathf.Max(bell, tent);
                });
                break;
            case SkyBossKind.Leviathan:
                s = Make(160, 160, (u, v) =>
                {
                    float body = 0f;
                    for (int i = 0; i < 12; i++) { float t = i / 11f; body = Mathf.Max(body, Ellipse(u, v, 0.12f + t * 0.8f, 0.5f + Mathf.Sin(t * 4f) * 0.08f, Mathf.Lerp(0.12f, 0.05f, t), Mathf.Lerp(0.14f, 0.05f, t))); }
                    return body;
                });
                break;
            case SkyBossKind.Fenrir:
                s = Make(128, 128, (u, v) => Mathf.Max(Mathf.Max(Ellipse(u, v, 0.5f, 0.5f, 0.32f, 0.16f), Ellipse(u, v, 0.16f, 0.62f, 0.12f, 0.1f)),
                    Mathf.Max(Box(u, v, 0.3f, 0.24f, 0.03f, 0.16f), Box(u, v, 0.7f, 0.24f, 0.03f, 0.16f))));
                break;
            case SkyBossKind.SkyGolem:
                s = Make(128, 128, (u, v) => Mathf.Max(Mathf.Max(Box(u, v, 0.5f, 0.5f, 0.24f, 0.24f), Box(u, v, 0.5f, 0.84f, 0.1f, 0.08f)),
                    Mathf.Max(Box(u, v, 0.2f, 0.46f, 0.07f, 0.22f), Mathf.Max(Box(u, v, 0.8f, 0.46f, 0.07f, 0.22f), Box(u, v, 0.5f, 0.14f, 0.18f, 0.12f)))));
                break;
            case SkyBossKind.Phoenix:
                s = Make(128, 128, (u, v) => Mathf.Max(Ellipse(u, v, 0.5f, 0.45f, 0.1f, 0.18f), Mathf.Max(Ellipse(u, v, 0.26f, 0.62f, 0.24f, 0.08f), Ellipse(u, v, 0.74f, 0.62f, 0.24f, 0.08f))));
                break;
            case SkyBossKind.SkySerpent:
                s = Make(160, 160, (u, v) =>
                {
                    float body = 0f;
                    for (int i = 0; i < 16; i++) { float t = i / 15f; body = Mathf.Max(body, Ellipse(u, v, 0.08f + t * 0.86f, 0.5f + Mathf.Sin(t * 7f) * 0.14f, 0.06f, 0.06f)); }
                    return body;
                });
                break;
            default:
                s = Make(128, 128, (u, v) => Mathf.Max(Mathf.Max(Ellipse(u, v, 0.5f, 0.45f, 0.14f, 0.28f), Ellipse(u, v, 0.5f, 0.82f, 0.08f, 0.08f)),
                    Mathf.Max(Ellipse(u, v, 0.28f, 0.6f, 0.2f, 0.12f), Ellipse(u, v, 0.72f, 0.6f, 0.2f, 0.12f))));
                break;
        }
        // 足元ピボット(実イラストのConfigureAndLoadSpriteWithFootPivotと合わせる)
        s = Sprite.Create(s.texture, new Rect(0, 0, s.texture.width, s.texture.height), new Vector2(0.5f, 0.1f), s.texture.width / 2f);
        placeholders[kind] = s;
        return s;
    }

    static float Box(float u, float v, float cx, float cy, float hw, float hh, float edge = 0.04f)
    {
        float d = Mathf.Max(Mathf.Abs(u - cx) - hw, Mathf.Abs(v - cy) - hh);
        return Mathf.Clamp01(-d / edge + 0.5f);
    }

    // ===== 共通の小演出 =====
    public static void Flash(Vector3 pos, Color color, float size, float duration = 0.18f)
    {
        OneShotSpriteEffect.CreateTweened(BossFx.Orb(), pos, color, duration, size * 0.6f, size, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
    }

    public static void Sparks(Vector3 pos, Color color, int count, float scale)
    {
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), pos, color, count, 0.35f, scale * 0.5f, scale, 3.5f, 1.2f, RenderOrder.CombatFx);
    }

    // 画面遠方の落雷(演出のみ、判定なし)。viewport座標で指定。
    public static void FarLightning(Vector2 viewport, float height = 9f)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 top = cam.ViewportToWorldPoint(new Vector3(viewport.x, 1.05f, 10f)); top.z = 0f;
        Vector3 bottom = cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 10f)); bottom.z = 0f;
        float len = Mathf.Max(2f, top.y - bottom.y);
        var go = new GameObject("FarLightning");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Bolt();
        sr.sortingOrder = -40;
        sr.color = new Color(0.8f, 0.9f, 1f, 0.9f);
        go.transform.position = (top + bottom) * 0.5f;
        go.transform.localScale = new Vector3(len * 0.35f, len, 1f);
        go.AddComponent<SkyFadeOut>().Init(0.35f, true);
        SkyBossSfx.Play(SkyBossSfx.Thunder(), 0.55f);
    }
}

// 短時間で消える見た目専用オブジェクト(点滅しながらフェード)。
public class SkyFadeOut : MonoBehaviour
{
    float life, t; bool flicker; SpriteRenderer sr; Color baseColor;
    public void Init(float lifetime, bool flickering)
    {
        life = lifetime; flicker = flickering;
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }
    void Update()
    {
        t += Time.deltaTime;
        float f = t / Mathf.Max(0.01f, life);
        if (f >= 1f) { Destroy(gameObject); return; }
        if (sr == null) return;
        Color c = baseColor;
        c.a = baseColor.a * (1f - f) * (flicker ? (0.6f + 0.4f * Mathf.Sin(t * 90f)) : 1f);
        sr.color = c;
    }
}

// ===== 地面の予告 → 攻撃(落雷/光柱/拳/爆発/落石) =====
// 着弾点を地面の光る楕円で予告し、warn秒後に「見えている攻撃」と同じ範囲だけ判定する。
public class SkyStrike : MonoBehaviour
{
    public enum Look { Bolt, Pillar, Fist, Burst, Rock }

    float warn, active, width, height, timer, groundY;
    // 拳/落石が落ち始める高さ(地面から)。浮遊岩のように「上空で待機→落下」する攻撃では低くする。
    public float incomingHeight = 12f;
    Look look;
    Color color;
    SpriteRenderer marker, column, strikeSr;
    BoxCollider2D col;
    bool fired, hitDone;
    public System.Action<Vector3> onFire;

    public float WorldX => transform.position.x;
    public float GroundY => groundY;

    public static SkyStrike Create(float worldX, float width, float height, float warn, float active, Color color, Look look)
    {
        var go = new GameObject("SkyStrike_" + look);
        var s = go.AddComponent<SkyStrike>();
        s.warn = warn; s.active = active; s.width = width; s.height = height; s.color = color; s.look = look;
        s.groundY = GroundAt(worldX, PlayerController.Instance != null ? PlayerController.Instance.transform.position.y : 0f);
        go.transform.position = new Vector3(worldX, s.groundY, 0f);

        var m = new GameObject("Marker");
        m.transform.SetParent(go.transform, false);
        s.marker = m.AddComponent<SpriteRenderer>();
        s.marker.sprite = SkyBossFx.GroundGlow();
        s.marker.sortingOrder = RenderOrder.Boss - 1;
        m.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        m.transform.localScale = new Vector3(width * 1.15f, 0.5f, 1f);

        if (look == Look.Bolt || look == Look.Pillar)
        {
            var c = new GameObject("Column");
            c.transform.SetParent(go.transform, false);
            s.column = c.AddComponent<SpriteRenderer>();
            s.column.sprite = BossFx.Block();
            s.column.sortingOrder = RenderOrder.Boss - 1;
            c.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            c.transform.localScale = new Vector3(width, height, 1f);
            s.column.color = new Color(color.r, color.g, color.b, 0f);
        }

        var sgo = new GameObject("Strike");
        sgo.transform.SetParent(go.transform, false);
        s.strikeSr = sgo.AddComponent<SpriteRenderer>();
        s.strikeSr.sortingOrder = RenderOrder.CombatFx;
        s.strikeSr.enabled = false;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        s.col = go.AddComponent<BoxCollider2D>();
        s.col.isTrigger = true;
        s.col.size = new Vector2(width, height);
        s.col.offset = new Vector2(0f, height * 0.5f);
        s.col.enabled = false;
        var dbg = go.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.1f, 0.1f);
        return s;
    }

    static float GroundAt(float x, float fallback)
    {
        if (TerrainManager.Instance == null) return fallback;
        float? h = TerrainManager.Instance.GetHeightAt(x);
        return h ?? fallback;
    }

    // マルチプレイPhase 2.5: 落雷/柱などの地点攻撃をJOINにも出す。
    void Start() { NetAttackSync.Register(gameObject, NetAttackSync.AType.SkyStrike); }

    void Update()
    {
        float baseSpeed = NetTargets.IsMulti ? NetTargets.FrameSpeedNear(transform.position) : (PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f);
        Vector3 p = transform.position;
        p.x += baseSpeed * Time.deltaTime;
        groundY = GroundAt(p.x, groundY);
        p.y = groundY;
        transform.position = p;

        timer += Time.deltaTime;
        if (!fired)
        {
            float f = Mathf.Clamp01(timer / Mathf.Max(0.01f, warn));
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 28f, f));
            marker.color = new Color(1f, Mathf.Lerp(0.55f, 0.15f, f), 0.12f, Mathf.Lerp(0.35f, 0.9f, f) * Mathf.Lerp(0.6f, 1f, blink));
            if (column != null) column.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0.02f, 0.16f, f) * Mathf.Lerp(0.6f, 1f, blink));
            UpdateIncoming(f);
            if (timer >= warn) Fire();
            return;
        }

        float af = Mathf.Clamp01((timer - warn) / Mathf.Max(0.01f, active));
        if (col.enabled && af >= 1f) col.enabled = false;
        float fade = Mathf.Clamp01((timer - warn - active) / 0.3f);
        Color sc = color; sc.a = color.a * (1f - fade) * (look == Look.Bolt ? 0.75f + 0.25f * Mathf.Sin(Time.time * 80f) : 1f);
        strikeSr.color = sc;
        marker.color = new Color(1f, 0.2f, 0.1f, 0.5f * (1f - fade));
        if (fade >= 1f) Destroy(gameObject);
    }

    // 拳/落石: 着弾直前に上空から落ちてくる。
    void UpdateIncoming(float f)
    {
        if (look != Look.Fist && look != Look.Rock) return;
        float fallStart = 0.72f;
        if (f < fallStart) return;
        float k = (f - fallStart) / (1f - fallStart);
        strikeSr.enabled = true;
        strikeSr.sprite = look == Look.Fist ? SkyBossFx.Fist() : CaveBossFx.RockChunk();
        strikeSr.color = look == Look.Fist ? color : new Color(0.62f, 0.58f, 0.55f, 1f);
        float size = look == Look.Fist ? width * 1.1f : width * 0.9f;
        strikeSr.transform.localScale = new Vector3(size, size, 1f);
        strikeSr.transform.localPosition = new Vector3(0f, Mathf.Lerp(incomingHeight, size * 0.45f, k * k), 0f);
    }

    void Fire()
    {
        fired = true;
        col.enabled = true;
        if (column != null) column.enabled = false;
        strikeSr.enabled = true;
        switch (look)
        {
            case Look.Bolt:
                strikeSr.sprite = SkyBossFx.Bolt();
                strikeSr.transform.localScale = new Vector3(width * 1.6f, height + 8f, 1f);
                strikeSr.transform.localPosition = new Vector3(0f, (height + 8f) * 0.5f, 0f);
                SkyBossSfx.Play(SkyBossSfx.Thunder(), 0.8f);
                Shake(0.1f, 0.15f);
                break;
            case Look.Pillar:
                strikeSr.sprite = SkyBossFx.Beam();
                strikeSr.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                strikeSr.transform.localScale = new Vector3(height + 6f, width * 1.3f, 1f);
                strikeSr.transform.localPosition = new Vector3(0f, (height + 6f) * 0.5f, 0f);
                SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.7f);
                Shake(0.06f, 0.12f);
                break;
            case Look.Fist:
            case Look.Rock:
                strikeSr.transform.localPosition = new Vector3(0f, width * 0.4f, 0f);
                Shake(look == Look.Fist ? 0.2f : 0.08f, 0.2f);
                OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), transform.position + Vector3.up * 0.1f, new Color(0.85f, 0.85f, 0.9f, 0.85f), 12, 0.5f, 0.6f, 1.2f, 3.5f, 2.2f, RenderOrder.CombatFx);
                OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position + new Vector3(0f, 0.25f, 0f), new Color(1f, 1f, 1f, 0.8f), 0.35f, width * 0.5f, width * 1.4f, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.15f);
                SkyBossSfx.Play(SkyBossSfx.Impact(), 0.8f);
                break;
            default:
                strikeSr.sprite = BossFx.Ring();
                strikeSr.transform.localScale = new Vector3(width * 1.3f, height * 1.1f, 1f);
                strikeSr.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                SkyBossFx.Flash(transform.position + Vector3.up * height * 0.4f, color, width * 1.4f);
                SkyBossSfx.Play(SkyBossSfx.Impact(), 0.7f);
                Shake(0.1f, 0.15f);
                break;
        }
        onFire?.Invoke(transform.position);
    }

    static void Shake(float m, float d)
    {
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(m, d);
    }

    void OnTriggerEnter2D(Collider2D other) => TryHit(other);
    void OnTriggerStay2D(Collider2D other) => TryHit(other);

    void TryHit(Collider2D other)
    {
        if (hitDone || !col.enabled) return;
        if (!other.CompareTag("Player") || PlayerController.Instance == null) return;
        hitDone = true;
        PlayerController.Instance.TakeDamage(source: "SkyStrike:" + look);
    }
}

// 見た目専用の漂う粒子(突風の筋/炎の残り火/復活時に集まる火の粉)。
public class SkyDrift : MonoBehaviour
{
    Vector3 velocity; float life, t; SpriteRenderer sr; Color baseColor; bool relativeToRun;

    public static SkyDrift Spawn(Sprite sprite, Vector3 pos, Vector2 scale, Color color, Vector3 velocity, float life, int order, bool relativeToRun = false, float rotation = 0f)
    {
        var go = new GameObject("SkyDrift");
        go.transform.position = pos;
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        var d = go.AddComponent<SkyDrift>();
        d.sr = go.AddComponent<SpriteRenderer>();
        d.sr.sprite = sprite;
        d.sr.sortingOrder = order;
        d.sr.color = color;
        d.baseColor = color;
        d.velocity = velocity;
        d.life = life;
        d.relativeToRun = relativeToRun;
        return d;
    }

    void Update()
    {
        t += Time.deltaTime;
        float f = t / Mathf.Max(0.01f, life);
        if (f >= 1f) { Destroy(gameObject); return; }
        Vector3 v = velocity;
        if (relativeToRun && PlayerController.Instance != null) v.x += PlayerController.Instance.CurrentAutoRunSpeed;
        transform.position += v * Time.deltaTime;
        Color c = baseColor; c.a = baseColor.a * Mathf.Sin(Mathf.PI * Mathf.Clamp01(f));
        sr.color = c;
    }
}

// ===== 空中の帯の予告(身体の横断/ブレスの通過範囲) =====
// プレイヤー基準で流れる、地面からの高さ[bottom,top]の赤い帯。警告のみ(判定なし)。
public class SkyWarnBand : MonoBehaviour
{
    float duration, t, bottom, top;
    SpriteRenderer sr;

    public static SkyWarnBand Create(float x0, float x1, float bottomAboveGround, float topAboveGround, float duration)
    {
        var go = new GameObject("SkyWarnBand");
        var b = go.AddComponent<SkyWarnBand>();
        b.duration = duration; b.bottom = bottomAboveGround; b.top = topAboveGround;
        b.sr = go.AddComponent<SpriteRenderer>();
        b.sr.sprite = BossFx.Block();
        b.sr.sortingOrder = RenderOrder.Boss - 1;
        float cx = (x0 + x1) * 0.5f;
        go.transform.position = new Vector3(cx, RefGround(cx) + (b.bottom + b.top) * 0.5f, 0f);
        go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Max(0.2f, b.top - b.bottom), 1f);
        return b;
    }

    static float RefGround(float x)
    {
        // 帯は地形の凹凸に沿わせず、プレイヤーの現在の足場高さを基準にする(横断は水平に来るため)。
        if (PlayerController.Instance != null && TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(PlayerController.Instance.transform.position.x);
            if (h.HasValue) return h.Value;
        }
        return PlayerController.Instance != null ? PlayerController.Instance.transform.position.y : 0f;
    }

    void Start() { NetAttackSync.Register(gameObject, NetAttackSync.AType.WarnBand); }

    void Update()
    {
        float baseSpeed = NetTargets.IsMulti ? NetTargets.FrameSpeedNear(transform.position) : (PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f);
        Vector3 p = transform.position;
        p.x += baseSpeed * Time.deltaTime;
        p.y = RefGround(p.x) + (bottom + top) * 0.5f;
        transform.position = p;
        t += Time.deltaTime;
        float f = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, f));
        sr.color = new Color(1f, Mathf.Lerp(0.5f, 0.1f, f), 0.08f, Mathf.Lerp(0.08f, 0.34f, f) * Mathf.Lerp(0.7f, 1f, blink));
        if (t >= duration) Destroy(gameObject);
    }
}

// ===== 遠方シルエットの通過演出(ドラゴン/魔人など既存コントローラーの登場前に使う) =====
// 背景レイヤー(空より手前・回廊より奥)を、小さく霞んだシルエットが近づいてくる。判定なし。
public class SkyFlyby : MonoBehaviour
{
    Sprite[] frames;
    Vector2 fromVp, toVp;
    float duration, t, fromScale, toScale, bob;
    Color tint;
    SpriteRenderer sr;
    bool flip;

    public static SkyFlyby Create(Sprite[] frames, Vector2 fromViewport, Vector2 toViewport, float fromScale, float toScale, float duration, Color tint, bool flipX)
    {
        if (frames == null || frames.Length == 0) return null;
        var go = new GameObject("SkyFlyby");
        var f = go.AddComponent<SkyFlyby>();
        f.frames = frames; f.fromVp = fromViewport; f.toVp = toViewport; f.fromScale = fromScale; f.toScale = toScale;
        f.duration = duration; f.tint = tint; f.flip = flipX;
        f.bob = Random.Range(0f, 10f);
        f.sr = go.AddComponent<SpriteRenderer>();
        f.sr.sortingOrder = -40;
        f.sr.sprite = frames[0];
        f.Apply();
        return f;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= duration) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float f = Mathf.Clamp01(t / duration);
        float e = f * f; // 加速しながら近づく
        Vector2 vp = Vector2.Lerp(fromVp, toVp, e);
        Vector3 p = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, 10f));
        p.z = 0f;
        p.y += Mathf.Sin(Time.time * 5f + bob) * 0.15f;
        transform.position = p;
        float s = Mathf.Lerp(fromScale, toScale, e);
        transform.localScale = new Vector3(flip ? -s : s, s, 1f);
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 4f + bob) * 3f);
        sr.sprite = frames[(int)(Time.time * 8f) % frames.Length];
        Color c = Color.Lerp(tint, new Color(1f, 1f, 1f, 0.85f), e * 0.6f);
        c.a = tint.a * Mathf.Clamp01(f * 5f) * Mathf.Clamp01((1f - f) * 6f);
        sr.color = c;
    }
}

// ===== 手続き的SE(専用の音素材が無いため実行時に合成、初回のみ生成) =====
public static class SkyBossSfx
{
    static AudioClip thunder, roar, wind, whoosh, impact, flame;
    const int Rate = 22050;

    public static void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySfxVolume(clip, volume);
    }

    static AudioClip Make(string name, float seconds, System.Func<float, float, float> sample)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
        float[] data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, i / (float)n), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 決定的なノイズ(Random状態を汚さない)
    static float Noise(int i) { uint x = (uint)i * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; x = (x >> 22) ^ x; return (x / (float)uint.MaxValue) * 2f - 1f; }

    public static AudioClip Thunder()
    {
        if (thunder != null) return thunder;
        float lp = 0f;
        int idx = 0;
        thunder = Make("SkyThunder", 1.3f, (t, f) =>
        {
            float n = Noise(idx++);
            lp += (n - lp) * 0.08f;
            float crack = n * Mathf.Exp(-t * 18f);
            float rumble = lp * 2.2f * Mathf.Exp(-t * 2.4f);
            return (crack * 0.8f + rumble) * 0.7f;
        });
        return thunder;
    }

    public static AudioClip Roar()
    {
        if (roar != null) return roar;
        float lp = 0f; int idx = 0;
        roar = Make("SkyRoar", 1.0f, (t, f) =>
        {
            float n = Noise(idx++ + 99991);
            lp += (n - lp) * 0.05f;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(f * 1.1f));
            float tone = Mathf.Sin(2f * Mathf.PI * (70f + 25f * Mathf.Sin(t * 9f)) * t);
            return (lp * 2.5f + tone * 0.35f) * env * 0.6f;
        });
        return roar;
    }

    public static AudioClip Wind()
    {
        if (wind != null) return wind;
        float lp = 0f, lp2 = 0f; int idx = 0;
        wind = Make("SkyWind", 1.8f, (t, f) =>
        {
            float n = Noise(idx++ + 555);
            lp += (n - lp) * 0.12f;
            lp2 += (lp - lp2) * 0.2f;
            float env = Mathf.Sin(Mathf.PI * f);
            return (lp - lp2) * 3f * env * 0.7f;
        });
        return wind;
    }

    public static AudioClip Whoosh()
    {
        if (whoosh != null) return whoosh;
        float lp = 0f; int idx = 0;
        whoosh = Make("SkyWhoosh", 0.45f, (t, f) =>
        {
            float n = Noise(idx++ + 1234);
            float k = Mathf.Lerp(0.05f, 0.4f, f);
            lp += (n - lp) * k;
            return lp * Mathf.Sin(Mathf.PI * f) * 0.9f;
        });
        return whoosh;
    }

    public static AudioClip Impact()
    {
        if (impact != null) return impact;
        float lp = 0f; int idx = 0;
        impact = Make("SkyImpact", 0.6f, (t, f) =>
        {
            float n = Noise(idx++ + 4242);
            lp += (n - lp) * 0.1f;
            float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 40f, f) * t) * Mathf.Exp(-t * 9f);
            return (thump * 0.8f + lp * 1.5f * Mathf.Exp(-t * 6f)) * 0.8f;
        });
        return impact;
    }

    public static AudioClip Flame()
    {
        if (flame != null) return flame;
        float lp = 0f; int idx = 0;
        flame = Make("SkyFlame", 0.9f, (t, f) =>
        {
            float n = Noise(idx++ + 777);
            lp += (n - lp) * 0.25f;
            float crackle = Noise(idx * 7) > 0.96f ? 0.6f : 0f;
            return (lp * 1.3f + crackle) * Mathf.Sin(Mathf.PI * f) * 0.6f;
        });
        return flame;
    }
}

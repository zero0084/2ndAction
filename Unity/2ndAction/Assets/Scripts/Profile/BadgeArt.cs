using System;
using System.Collections.Generic;
using UnityEngine;

// バッヂの絵(2026-11-07)。本番の絵は Resources/Badges/badge_<チャレンジID>.png(正方形・透明背景)を置けばそれを使う(コードの変更は不要)。
//  無い間は仮の絵: カテゴリ色のメダルの枠 + 手続きで描いた簡単なモチーフ(DISTANCE=道標と一本道 / NO DAMAGE=盾 / COMBO=斬撃 / SPEED=翼と速度線 /
//  AIR=羽 / BREAK=砕けた鎖 / BOSS=王冠 / REAPER=鎌 / SPECIAL=星・紋章)+ 段の印(下の小さな点 1〜3)。
//  未獲得: 暗い色+シルエット+鍵(本番の絵は暗く塗った絵、仮の絵は灰色の枠と影のモチーフを同じ形から作る)。
public static class BadgeArt
{
    const int N = 112;                       // 仮の絵の大きさ(画素)
    static readonly Dictionary<string, Texture2D> owned = new Dictionary<string, Texture2D>(), locked = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Texture2D> finalArt = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> noFinal = new HashSet<string>();
    static Texture2D lockTex;
    public static int Generated { get; private set; } // 確認用

    public static string ResourcePath(string id) => "Badges/badge_" + id;

    // 本番の絵(無ければ null)
    public static Texture2D Final(ChallengeDef d)
    {
        if (d == null || noFinal.Contains(d.id)) return null;
        if (finalArt.TryGetValue(d.id, out var t) && t != null) return t;
        t = Resources.Load<Texture2D>(ResourcePath(d.id));
        if (t == null) { noFinal.Add(d.id); return null; }
        finalArt[d.id] = t;
        return t;
    }

    public static Texture2D Get(ChallengeDef d, bool isOwned)
    {
        if (d == null) return null;
        var dict = isOwned ? owned : locked;
        if (dict.TryGetValue(d.id, out var t) && t != null) return t;
        Make(d, out var o, out var l);
        owned[d.id] = o; locked[d.id] = l;
        return isOwned ? o : l;
    }

    // r に描く。owned=false は未獲得(暗い+シルエット+鍵)
    public static void Draw(Rect r, ChallengeDef d, bool isOwned)
    {
        if (d == null || Event.current == null || Event.current.type != EventType.Repaint) return;
        Color keep = GUI.color;
        var fin = Final(d);
        if (fin != null)
        {
            if (!isOwned) GUI.color = new Color(0.16f, 0.17f, 0.22f, keep.a); // 暗く塗る(形は残る = シルエット)
            GUI.DrawTexture(r, fin, ScaleMode.ScaleToFit);
        }
        else GUI.DrawTexture(r, Get(d, isOwned), ScaleMode.ScaleToFit);
        GUI.color = keep;
        if (!isOwned)
        {
            float s = Mathf.Min(r.width, r.height) * 0.42f;
            GUI.DrawTexture(new Rect(r.center.x - s * 0.5f, r.center.y - s * 0.5f + s * 0.05f, s, s), LockTex, ScaleMode.ScaleToFit);
        }
    }

    // ===================================================================== 仮の絵の作り方
    // 座標は中心 (0,0)・幅 1(-0.5〜0.5)、上が +y。各形は符号付き距離(内側が負)
    delegate float Sdf(Vector2 p);
    struct Layer { public Sdf f; public Color c; public bool motif; }

    static void Make(ChallengeDef d, out Texture2D ownedTex, out Texture2D lockedTex)
    {
        Color cat = ChallengeDefs.CategoryColor(d.cat);
        Color rim = Color.Lerp(cat, Color.black, 0.15f), rimHi = Color.Lerp(cat, Color.white, 0.35f);
        Color disc = new Color(0.07f, 0.09f, 0.17f), motif = Color.Lerp(cat, Color.white, 0.55f), dark = new Color(0.07f, 0.09f, 0.17f);
        var L = new List<Layer>
        {
            new Layer { f = p => Circle(p, Vector2.zero, 0.48f), c = rim },
            new Layer { f = p => Ring(p, Vector2.zero, 0.445f, 0.012f), c = rimHi },
            new Layer { f = p => Circle(p, Vector2.zero, 0.40f), c = disc },
            new Layer { f = p => Ring(p, Vector2.zero, 0.385f, 0.006f), c = new Color(cat.r, cat.g, cat.b, 0.7f) },
        };
        void M(Sdf f) => L.Add(new Layer { f = f, c = motif, motif = true });
        void Dk(Sdf f) => L.Add(new Layer { f = f, c = dark, motif = true });
        void Col(Sdf f, Color c) => L.Add(new Layer { f = f, c = c, motif = true });
        switch (d.cat)
        {
            case ChallengeCategory.Distance:
                M(p => Poly(p, V(-0.25f, -0.29f), V(0.25f, -0.29f), V(0.035f, 0.16f), V(-0.035f, 0.16f)));          // 一本道
                Dk(p => Capsule(p, V(0f, -0.25f), V(0f, -0.17f), 0.016f)); Dk(p => Capsule(p, V(0f, -0.11f), V(0f, -0.04f), 0.012f)); Dk(p => Capsule(p, V(0f, 0.01f), V(0f, 0.06f), 0.008f));
                M(p => Capsule(p, V(0.2f, -0.2f), V(0.2f, 0.24f), 0.022f));                                            // 道標
                M(p => Poly(p, V(0.06f, 0.15f), V(0.27f, 0.15f), V(0.32f, 0.2f), V(0.27f, 0.25f), V(0.06f, 0.25f)));
                if (d.id == "dist_total") { for (int i = 0; i < 4; i++) { float y = -0.14f + i * 0.07f; Dk(p => Capsule(p, V(0.17f, y), V(0.23f, y), 0.007f)); } }
                if (d.tier >= 3 && d.id != "dist_total") M(p => Star(p, V(0f, 0.25f), 0.07f, 0.03f));                 // 地平線の星
                break;
            case ChallengeCategory.NoDamage:
                if (d.tier >= 2) M(p => Poly(p, V(-0.26f, 0.27f), V(0.26f, 0.27f), V(0.26f, 0.03f), V(0f, -0.31f), V(-0.26f, 0.03f)));
                (d.tier >= 2 ? (Action<Sdf>)Dk : M)(p => Poly(p, V(-0.2f, 0.22f), V(0.2f, 0.22f), V(0.2f, 0.03f), V(0f, -0.24f), V(-0.2f, 0.03f)));
                (d.tier >= 2 ? (Action<Sdf>)M : Dk)(p => Capsule(p, V(0f, 0.15f), V(0f, -0.14f), 0.03f));
                (d.tier >= 2 ? (Action<Sdf>)M : Dk)(p => Capsule(p, V(-0.12f, 0.06f), V(0.12f, 0.06f), 0.03f));
                if (d.id == "nodmg_boss") Col(p => Poly(p, V(-0.12f, 0.3f), V(0.12f, 0.3f), V(0.12f, 0.38f), V(0.06f, 0.33f), V(0f, 0.4f), V(-0.06f, 0.33f), V(-0.12f, 0.38f)), new Color(1f, 0.85f, 0.35f));
                break;
            case ChallengeCategory.Combo:
            {
                int n = d.tier == 1 ? 2 : d.tier == 2 ? 3 : 4;
                for (int i = 0; i < n; i++)
                {
                    float o = (i - (n - 1) * 0.5f) * 0.12f;
                    if (d.tier >= 3 && i % 2 == 1) M(p => Blade(p, V(0.24f + o, -0.22f), V(-0.24f + o, 0.24f), 0.035f));
                    else M(p => Blade(p, V(-0.24f + o, -0.22f), V(0.24f + o, 0.24f), 0.035f));
                }
                break;
            }
            case ChallengeCategory.Speed:
            {
                // 翼(根元から右上へ羽根を扇に)+ 左の速度線
                int feathers = 4;
                for (int i = 0; i < feathers; i++)
                {
                    float ang = Mathf.Deg2Rad * (20f + i * 22f), len = 0.32f - i * 0.04f;
                    var a = V(-0.08f, -0.06f); var b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len; float rr = 0.04f - i * 0.004f;
                    M(p => Capsule(p, a, b, rr));
                }
                if (d.tier >= 3) for (int i = 0; i < 3; i++) { float ang = Mathf.Deg2Rad * (160f - i * 22f), len = 0.24f - i * 0.04f; var a = V(-0.08f, -0.06f); var b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len; float rr = 0.032f - i * 0.004f; M(p => Capsule(p, a, b, rr)); }
                int lines = d.tier == 1 ? 0 : d.tier == 2 ? 2 : 3;
                for (int i = 0; i < lines; i++) { float y = -0.18f - i * 0.065f, x0 = -0.3f + i * 0.04f; M(p => Capsule(p, V(x0, y), V(0.14f, y), 0.012f)); }
                break;
            }
            case ChallengeCategory.Air:
            {
                int n = d.tier;
                for (int i = 0; i < n; i++)
                {
                    float ang = n == 1 ? 60f : n == 2 ? 40f + i * 50f : 90f + i * 120f;
                    float r = n == 3 ? 0.13f : n == 2 ? 0.07f : 0f;
                    Vector2 c = n == 3 ? new Vector2(Mathf.Cos(Mathf.Deg2Rad * ang), Mathf.Sin(Mathf.Deg2Rad * ang)) * r : V((i - (n - 1) * 0.5f) * 0.14f, 0f);
                    float rot = n == 3 ? ang - 90f : ang - 90f;
                    float sc = n == 3 ? 0.6f : 1f;
                    M(p => Feather(Rotate(p - c, -rot) / sc) * sc);
                    Dk(p => Capsule(Rotate(p - c, -rot) / sc, V(0f, -0.2f), V(0f, 0.17f), 0.008f) * sc);
                }
                break;
            }
            case ChallengeCategory.Break:
            {
                int links = d.tier >= 2 ? 2 : 1;
                for (int k = 0; k < links; k++)
                {
                    float oy = links == 1 ? 0f : (k == 0 ? 0.12f : -0.12f);
                    M(p => Max(Ring(Scale(p - V(-0.14f, oy), 1f, 1.6f), Vector2.zero, 0.1f, 0.026f), -Box(p - V(-0.03f, oy), 0.03f, 0.06f)));
                    M(p => Max(Ring(Scale(p - V(0.14f, oy), 1f, 1.6f), Vector2.zero, 0.1f, 0.026f), -Box(p - V(0.03f, oy), 0.03f, 0.06f)));
                }
                M(p => Poly(p, V(-0.02f, 0.27f), V(0.04f, 0.2f), V(-0.01f, 0.18f)));   // 破片
                M(p => Poly(p, V(0.05f, -0.27f), V(-0.02f, -0.2f), V(0.02f, -0.18f)));
                break;
            }
            case ChallengeCategory.Boss:
            {
                Color gem = d.id == "boss_garmud" ? new Color(0.95f, 0.3f, 0.2f) : d.id == "boss_noctaris" ? new Color(0.4f, 0.9f, 1f) : d.id == "boss_seraphiel" ? new Color(1f, 0.97f, 0.8f) : new Color(0.9f, 0.5f, 1f);
                if (d.id == "boss_rush")
                {
                    for (int i = -1; i <= 1; i++) { var o = V(i * 0.2f, i == 0 ? 0.05f : -0.06f); M(p => Crown(p - o, 0.55f)); Col(p => Circle(p, o + V(0f, -0.02f), 0.025f), gem); }
                }
                else
                {
                    M(p => Crown(p, 1f));
                    Col(p => Circle(p, V(0f, -0.05f), 0.045f), gem);
                    if (d.id == "boss_garmud") { M(p => Fang(p - V(-0.11f, -0.21f))); M(p => Fang(p - V(0.11f, -0.21f))); }
                    if (d.id == "boss_noctaris") Col(p => Poly(p, V(0f, -0.33f), V(0.05f, -0.24f), V(0f, -0.15f), V(-0.05f, -0.24f)), gem);
                    if (d.id == "boss_seraphiel") Col(p => Ring(p, V(0f, 0.27f), 0.09f, 0.015f), gem);
                }
                break;
            }
            case ChallengeCategory.Reaper:
                M(p => Capsule(p, V(0.18f, -0.28f), V(-0.06f, 0.26f), 0.025f));                                              // 柄
                M(p => Max(Max(Circle(p, V(-0.02f, 0.12f), 0.24f), -Circle(p, V(-0.08f, 0.04f), 0.23f)), -(p.y - 0.0f)));   // 刃(三日月)
                if (d.tier >= 2) for (int i = 0; i < 3; i++) { float y = -0.08f - i * 0.07f, x1 = -0.06f - i * 0.03f; M(p => Capsule(p, V(-0.32f, y), V(x1, y), 0.011f)); } // 振り切る軌跡
                break;
            default: // Special
                if (d.tier >= 2) { M(p => Poly(p, V(-0.25f, 0.28f), V(0.25f, 0.28f), V(0.25f, 0.02f), V(0f, -0.3f), V(-0.25f, 0.02f))); Dk(p => Poly(p, V(-0.2f, 0.23f), V(0.2f, 0.23f), V(0.2f, 0.03f), V(0f, -0.23f), V(-0.2f, 0.03f))); }
                M(p => Star(p, V(0f, d.tier >= 2 ? 0.03f : 0f), d.tier >= 2 ? 0.17f : 0.27f, d.tier >= 2 ? 0.07f : 0.11f));
                break;
        }
        // 段の印(下に 1〜3 個の点)
        for (int i = 0; i < d.tier; i++) { float x = (i - (d.tier - 1) * 0.5f) * 0.06f; L.Add(new Layer { f = p => Circle(p, V(x, -0.355f), 0.016f), c = new Color(1f, 0.86f, 0.4f) }); }

        // 描く(未獲得は同じ形から: 灰色の枠・暗い円盤・モチーフは影)
        var po = new Color32[N * N]; var pl = new Color32[N * N];
        float aa = 1.2f / N;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f);
                Color co = new Color(0, 0, 0, 0), cl = new Color(0, 0, 0, 0);
                for (int k = 0; k < L.Count; k++)
                {
                    float cov = Mathf.Clamp01(0.5f - L[k].f(p) / aa);
                    if (cov <= 0f) continue;
                    co = Over(co, L[k].c, cov);
                    Color lc = k == 0 ? new Color(0.34f, 0.35f, 0.4f) : k == 1 ? new Color(0.44f, 0.45f, 0.5f) : k == 2 ? new Color(0.17f, 0.18f, 0.23f)
                        : k == 3 ? new Color(0.26f, 0.27f, 0.32f, 0.7f) : L[k].motif ? new Color(0.05f, 0.055f, 0.08f) : new Color(0.3f, 0.3f, 0.34f);
                    cl = Over(cl, lc, cov);
                }
                po[y * N + x] = co; pl[y * N + x] = cl;
            }
        ownedTex = ToTex(po, "badge_" + d.id); lockedTex = ToTex(pl, "badge_" + d.id + "_locked");
        Generated++;
    }

    static Texture2D ToTex(Color32[] px, string name)
    {
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        t.SetPixels32(px); t.Apply(true, false);
        return t;
    }

    static Color Over(Color dst, Color src, float cov)
    {
        float a = src.a * cov, oa = a + dst.a * (1f - a);
        if (oa <= 1e-5f) return new Color(0, 0, 0, 0);
        return new Color((src.r * a + dst.r * dst.a * (1f - a)) / oa, (src.g * a + dst.g * dst.a * (1f - a)) / oa, (src.b * a + dst.b * dst.a * (1f - a)) / oa, oa);
    }

    // ---- ホームの「プロフィール」のアイコン(Resources/UI/Icons/home_profile.png を置けばそれを使う。無い間は仮の絵) ----
    static Texture2D profileIcon;
    public static Texture2D ProfileIcon
    {
        get
        {
            var t = UiIcons.Home("profile");
            if (t != null) return t;
            if (profileIcon != null) return profileIcon;
            const int n = 96; var px = new Color32[n * n]; float aa = 1.2f / n;
            Color gold = new Color(1f, 0.82f, 0.4f), goldDk = new Color(0.55f, 0.38f, 0.12f), navy = new Color(0.1f, 0.13f, 0.28f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                Color c = new Color(0, 0, 0, 0);
                float cardF = Box(p, 0.44f, 0.32f) - 0.04f;
                c = Over(c, goldDk, Mathf.Clamp01(0.5f - (cardF - 0.02f) / aa));
                c = Over(c, gold, Mathf.Clamp01(0.5f - cardF / aa));
                c = Over(c, navy, Mathf.Clamp01(0.5f - (cardF + 0.035f) / aa));
                float person = Mathf.Min(Circle(p, V(-0.2f, 0.07f), 0.09f), Max(Circle(p, V(-0.2f, -0.2f), 0.17f), -(p.y + 0.24f)));
                c = Over(c, gold, Mathf.Clamp01(0.5f - person / aa));
                for (int i = 0; i < 3; i++) { float ly = 0.12f - i * 0.13f, lx = i == 0 ? 0.3f : 0.24f; c = Over(c, i == 0 ? gold : new Color(0.85f, 0.85f, 0.95f), Mathf.Clamp01(0.5f - Capsule(p, V(0f, ly), V(lx, ly), 0.03f) / aa)); }
                px[y * n + x] = c;
            }
            profileIcon = new Texture2D(n, n, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            profileIcon.SetPixels32(px); profileIcon.Apply(true, false);
            return profileIcon;
        }
    }

    // ---- 鍵の絵 ----
    static Texture2D LockTex
    {
        get
        {
            if (lockTex != null) return lockTex;
            const int n = 64; var px = new Color32[n * n]; float aa = 1.2f / n;
            Color body = new Color(0.86f, 0.8f, 0.62f), edge = new Color(0.1f, 0.08f, 0.05f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                Color c = new Color(0, 0, 0, 0);
                float shackle = Max(Ring(p, V(0f, 0.08f), 0.17f, 0.045f), -(p.y - 0.06f));
                float bodyF = Box(p - V(0f, -0.13f), 0.27f, 0.2f);
                c = Over(c, edge, Mathf.Clamp01(0.5f - (Mathf.Min(shackle, bodyF) - 0.035f) / aa));
                c = Over(c, body, Mathf.Clamp01(0.5f - shackle / aa));
                c = Over(c, body, Mathf.Clamp01(0.5f - bodyF / aa));
                c = Over(c, edge, Mathf.Clamp01(0.5f - Mathf.Min(Circle(p, V(0f, -0.1f), 0.045f), Capsule(p, V(0f, -0.1f), V(0f, -0.21f), 0.02f)) / aa));
                px[y * n + x] = c;
            }
            lockTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            lockTex.SetPixels32(px); lockTex.Apply(true, false);
            return lockTex;
        }
    }

    // ---- 形(符号付き距離) ----
    static Vector2 V(float x, float y) => new Vector2(x, y);
    static float Max(float a, float b) => Mathf.Max(a, b);
    static Vector2 Rotate(Vector2 p, float deg) { float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c); }
    static Vector2 Scale(Vector2 p, float sx, float sy) => new Vector2(p.x * sx, p.y / sy * 1f);
    static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
    static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;
    static float Box(Vector2 p, float hw, float hh) { var d = new Vector2(Mathf.Abs(p.x) - hw, Mathf.Abs(p.y) - hh); return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f); }
    static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        Vector2 pa = p - a, ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
        return (pa - ba * h).magnitude - r;
    }
    // 斬撃: 両端が細い刃(中央が太い)
    static float Blade(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        Vector2 pa = p - a, ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
        return (pa - ba * h).magnitude - r * Mathf.Sin(h * Mathf.PI);
    }
    static float Poly(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]); float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i], w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-9f, Vector2.Dot(e, e)));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
    static float Star(Vector2 p, Vector2 c, float ro, float ri)
    {
        var v = new Vector2[10];
        for (int i = 0; i < 10; i++) { float a = Mathf.Deg2Rad * (90f + i * 36f), r = i % 2 == 0 ? ro : ri; v[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return Poly(p, v);
    }
    static float Crown(Vector2 p, float s)
    {
        p /= s;
        return Poly(p, V(-0.24f, -0.14f), V(0.24f, -0.14f), V(0.26f, 0.16f), V(0.12f, 0.02f), V(0f, 0.22f), V(-0.12f, 0.02f), V(-0.26f, 0.16f)) * s;
    }
    static float Fang(Vector2 p) => Poly(p, V(-0.035f, 0.05f), V(0.035f, 0.05f), V(0f, -0.07f));
    // 羽: 細長い葉の形(2つの円の重なり)を縦に
    static float Feather(Vector2 p) => Max(Circle(p, V(0.16f, 0f), 0.26f), Circle(p, V(-0.16f, 0f), 0.26f)) + 0f;
}

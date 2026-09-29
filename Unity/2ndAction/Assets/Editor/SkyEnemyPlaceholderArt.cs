using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 天空回廊Enemyの仮素材(手続き的なシルエット)。実イラストが置かれたファイルは上書きしない。
// 全種「左向き」(プレイヤーは左から来る)。ポーズの違いはシルエットで分かるようにする
// (予兆=のけぞり+発光、攻撃=前へ伸びる、硬直=うなだれ、被弾=白く傾く、撃破=灰色で倒れる、
//  休眠=灰色の石像、起動=目が光り翼を広げる、溜め=光輪、急降下=前傾)。
public static class SkyEnemyPlaceholderArt
{
    public static void EnsureArt(SkyEnemyDatabase.SkySpec spec, string root)
    {
        string dir = $"{root}/{spec.id}";
        Directory.CreateDirectory(dir);
        Write($"{dir}/idle.png", spec, new Pose());
        if (spec.moveFrames > 0)
        {
            Directory.CreateDirectory($"{dir}/move");
            for (int i = 0; i < spec.moveFrames; i++)
                Write($"{dir}/move/move_{i:00}.png", spec, new Pose { phase = i / (float)spec.moveFrames, wing = 0.5f + 0.5f * Mathf.Sin(i * 2f * Mathf.PI / spec.moveFrames) });
        }
        foreach (string p in spec.poses) Write($"{dir}/{p}.png", spec, PoseFor(p));
    }

    class Pose
    {
        public float rot, lean, squash = 1f, wing = 0.5f, phase, reach, raise, alpha = 1f;
        public bool glowEyes, halo, gray, weaponUp, weaponOut, dark;
        public Color mix = Color.clear; public float mixAmt;
    }

    static Pose PoseFor(string name)
    {
        switch (name)
        {
            case "telegraph": return new Pose { lean = -0.18f, raise = 1f, weaponUp = true, glowEyes = true, halo = false, wing = 1f, mix = new Color(1f, 0.5f, 0.2f), mixAmt = 0.18f };
            case "attack": return new Pose { lean = 0.28f, reach = 1f, weaponOut = true, wing = 0.2f, glowEyes = true };
            case "recover": return new Pose { lean = 0.1f, squash = 0.88f, wing = 0.15f };
            case "hit": return new Pose { rot = 14f, mix = Color.white, mixAmt = 0.55f };
            case "death": return new Pose { rot = 38f, gray = true, alpha = 0.85f, squash = 0.92f };
            case "dormant": return new Pose { gray = true, wing = 0f, dark = true };
            case "wake": return new Pose { glowEyes = true, wing = 1.2f, raise = 0.6f, mix = new Color(1f, 0.75f, 0.3f), mixAmt = 0.12f };
            case "charge": return new Pose { halo = true, glowEyes = true, raise = 0.5f, mix = new Color(1f, 1f, 0.6f), mixAmt = 0.2f };
            case "dive": return new Pose { rot = -32f, lean = 0.2f, wing = 0.05f, glowEyes = true, weaponOut = true };
        }
        return new Pose();
    }

    static void Write(string path, SkyEnemyDatabase.SkySpec spec, Pose pose)
    {
        if (File.Exists(path)) return;
        var c = new Canvas(spec.canvasW, spec.canvasH);
        c.pose = pose;
        c.pivot = new Vector2(spec.canvasW * 0.5f, 10f);
        Color body = spec.body, acc = spec.accent;
        if (pose.gray && spec.id != "mimic") { /* ミミックの休眠はふつうの宝箱の色のまま */ float g = body.grayscale * 0.85f; body = new Color(g, g, g * 1.05f); acc = pose.dark ? new Color(0.25f, 0.25f, 0.28f) : new Color(g * 0.9f, g * 0.9f, g); }
        switch (spec.id)
        {
            case "sky_slime": Slime(c, body, acc, pose); break;
            case "sky_hound": Hound(c, body, acc, pose); break;
            case "harpy": Harpy(c, body, acc, pose); break;
            case "gargoyle": Gargoyle(c, body, acc, pose); break;
            case "celestial_knight": Knight(c, body, acc, pose); break;
            case "ancient_sentinel": Sentinel(c, body, acc, pose); break;
            case "storm_spirit": Storm(c, body, acc, pose); break;
            case "sky_hunter": Hunter(c, body, acc, pose); break;
            case "treasure_goblin": TreasureGoblin(c, body, acc, pose); break;
            case "mimic": Mimic(c, body, acc, pose); break;
            case "golden_slime": GoldenSlime(c, body, acc, pose); break;
            case "card_fairy": CardFairy(c, body, acc, pose); break;
        }
        c.Finish(pose);
        File.WriteAllBytes(path, c.tex.EncodeToPNG());
        Object.DestroyImmediate(c.tex);
    }

    // ---- 各種のシルエット(左向き、足元y≈10) ----
    static void Slime(Canvas c, Color b, Color a, Pose p)
    {
        float up = 26f + p.raise * 10f;
        c.Ellipse(128, up + 30, 70, 26, 0, a * 0.9f);
        c.Ellipse(128, up + 70, 78, 58, 0, b);
        c.Ellipse(88, up + 110, 44, 40, 0, b);
        c.Ellipse(160, up + 118, 50, 46, 0, b);
        c.Ellipse(124, up + 150, 36, 30, 0, b);
        Eyes(c, 98, up + 92, 132, up + 92, p, new Color(0.2f, 0.3f, 0.55f));
    }

    static void Hound(Canvas c, Color b, Color a, Pose p)
    {
        float lunge = p.reach * 26f;
        for (int i = 0; i < 4; i++)
        {
            float lx = 110 + i * 42 - lunge * 0.3f;
            float sw = Mathf.Sin((p.phase + i * 0.25f) * Mathf.PI * 2f) * 14f;
            c.Line(lx, 95, lx + sw, 12, 12, b * 0.85f);
        }
        c.Ellipse(170 - lunge * 0.4f, 110 + p.raise * 8, 92, 44, 0, b);
        c.Ellipse(190, 132, 70, 20, 0, a);                          // たてがみ
        c.Line(258, 118, 300, 150 + p.raise * 10, 10, a);            // 尾
        float hx = 72 - lunge, hy = 150 + p.raise * 16 - p.reach * 12;
        c.Ellipse(hx, hy, 42, 36, 0, b);
        c.Tri(hx + 10, hy + 26, hx + 26, hy + 64, hx + 36, hy + 20, a);  // 耳
        c.Ellipse(hx - 36, hy - 12 - p.reach * 4, 26, 16 + p.reach * 8, 0, b);  // 口(攻撃で開く)
        if (p.reach > 0) c.Tri(hx - 60, hy - 8, hx - 34, hy - 2, hx - 36, hy - 18, Color.white);
        Eyes(c, hx - 12, hy + 8, -1, 0, p, new Color(0.1f, 0.15f, 0.35f));
    }

    static void Wings(Canvas c, float sx, float sy, float span, float spread, Color col)
    {
        float up = Mathf.Lerp(-20f, 70f, Mathf.Clamp01(spread));
        c.Tri(sx, sy, sx - span * 0.55f, sy + up + 40, sx - span * 0.3f, sy - 30, col);
        c.Tri(sx, sy, sx + span, sy + up + 30, sx + span * 0.6f, sy - 40, col);
    }

    static void Harpy(Canvas c, Color b, Color a, Pose p)
    {
        Wings(c, 165, 150, 140, p.wing, a);
        c.Ellipse(160, 120, 36, 58, 0, b);                            // 胴(芯)
        c.Ellipse(150, 196, 30, 30, 0, b);                            // 頭
        c.Ellipse(168, 210, 34, 22, 0, a * 0.8f);                      // 髪
        c.Line(150, 66, 140, 18, 8, a * 0.7f); c.Line(172, 66, 180, 18, 8, a * 0.7f);   // 脚/爪
        Eyes(c, 138, 198, -1, 0, p, new Color(0.3f, 0.1f, 0.2f));
    }

    static void Gargoyle(Canvas c, Color b, Color a, Pose p)
    {
        float wing = p.dark ? 0f : p.wing;
        if (wing > 0.05f) Wings(c, 175, 170, 150 * wing, wing, b * 0.8f);
        else c.Ellipse(190, 150, 40, 70, -10, b * 0.85f);             // たたんだ翼
        c.Rect(128, 12, 196, 60, b * 0.9f);                           // 台座/脚
        c.Ellipse(165, 120, 55, 62, 0, b);                            // 胴(うずくまり)
        float hx = 120 - p.reach * 20, hy = 175 + p.raise * 12;
        c.Ellipse(hx, hy, 34, 30, 0, b);
        c.Tri(hx + 10, hy + 20, hx + 30, hy + 58, hx + 26, hy + 14, b * 0.8f);  // 角
        if (p.weaponOut || p.weaponUp) c.Line(120, 130, 60 - p.reach * 30, 120 + p.raise * 50, 12, b * 0.95f);  // 腕/爪
        if (p.glowEyes) { c.Ellipse(hx - 12, hy + 4, 7, 5, 0, a); c.Ellipse(hx + 6, hy + 4, 7, 5, 0, a); }
        else c.Ellipse(hx - 12, hy + 4, 6, 4, 0, b * 0.5f);
    }

    static void Knight(Canvas c, Color b, Color a, Pose p)
    {
        float step = Mathf.Sin(p.phase * Mathf.PI * 2f) * 14f;
        c.Line(118 + step, 90, 112 + step * 1.4f, 12, 16, b * 0.8f);
        c.Line(142 - step, 90, 148 - step * 1.4f, 12, 16, b * 0.8f);
        c.Rect(100, 88, 160, 170, b);                                 // 胴(鎧)
        c.Rect(100, 150, 160, 160, a);                                // 金の縁
        c.Ellipse(128, 196, 26, 28, 0, b);                            // 兜
        c.Tri(134, 214, 170, 250, 148, 206, a);                       // 羽飾り
        c.Rect(106, 190, 128, 196, new Color(0.15f, 0.2f, 0.3f));     // 面頬のすき間
        c.Ellipse(160, 120, 16, 34, 0, a * 0.9f);                     // 盾
        // 剣: 予兆=頭上に振り上げ / 攻撃=前へ振り抜く / 通常=下げる
        if (p.weaponUp) c.Line(104, 160, 120, 250, 8, new Color(0.9f, 0.95f, 1f));
        else if (p.weaponOut) c.Line(100, 140, 20 - p.reach * 10, 120, 8, new Color(0.9f, 0.95f, 1f));
        else c.Line(100, 130, 84, 50, 8, new Color(0.9f, 0.95f, 1f));
        if (p.glowEyes) c.Rect(108, 191, 124, 195, new Color(0.6f, 0.9f, 1f));
    }

    static void Sentinel(Canvas c, Color b, Color a, Pose p)
    {
        c.Rect(110, 12, 150, 110, b * 0.85f); c.Rect(180, 12, 220, 110, b * 0.85f);   // 柱の脚
        c.Rect(90, 100, 240, 230, b);                                                   // 胴
        c.Rect(140, 230, 190, 270, b * 0.9f);                                           // 頭
        c.Rect(150, 245, 180, 252, a);                                                  // 目の光
        c.Rect(120, 150, 210, 160, a * 0.9f); c.Rect(150, 120, 160, 200, a * 0.9f);    // 紋様
        // 腕: 予兆=高く振り上げ / 攻撃=前の地面へ叩きつけ / 通常=下げる
        if (p.weaponUp) { c.Line(95, 210, 70, 300, 30, b * 0.95f); c.Line(235, 210, 260, 300, 30, b * 0.95f); }
        else if (p.weaponOut) { c.Line(95, 190, 30, 30, 32, b * 0.95f); c.Ellipse(30, 26, 26, 18, 0, a); }
        else { c.Line(92, 210, 70, 90, 28, b * 0.95f); c.Line(238, 210, 258, 90, 28, b * 0.95f); }
    }

    static void Storm(Canvas c, Color b, Color a, Pose p)
    {
        if (p.halo) { c.Ring(128, 150, 100, 12, a); c.Ring(128, 150, 70, 6, a * 0.9f); }
        c.Ellipse(128, 150, 58, 60, 0, b);                            // 核
        c.Ellipse(128, 160, 34, 36, 0, Color.Lerp(b, Color.white, 0.5f));
        c.Tri(100, 110, 156, 110, 128, 20, b * 0.85f);                // 渦の尾(下は細く)
        c.Line(80, 170, 40, 200 + p.raise * 20, 8, a); c.Line(176, 170, 216, 200 + p.raise * 20, 8, a);  // 腕の稲妻
        Eyes(c, 110, 162, 140, 162, p, new Color(0.1f, 0.1f, 0.35f));
    }

    static void Hunter(Canvas c, Color b, Color a, Pose p)
    {
        Wings(c, 180, 150, 150, p.wing * 0.8f + 0.1f, b * 0.8f);
        c.Ellipse(165, 120, 28, 62, 12, b);                           // 細い胴
        c.Ellipse(140, 190, 24, 22, 0, b);                            // 頭
        c.Tri(128, 200, 100, 206, 124, 188, b);                       // くちばし状の兜
        c.Line(160, 70, 150, 16, 7, b * 0.8f);
        if (p.weaponOut || p.weaponUp) c.Line(140, 140, 70 - p.reach * 30, 130, 7, a);   // 爪を突き出す
        if (true) { c.Ellipse(132, 194, 6, 4, 0, a); }
    }

    // ---- BONUS ZONEの報酬Enemy(2026-09-29、仮素材)。素材は他と同じ左向きで描く ----
    static void TreasureGoblin(Canvas c, Color b, Color a, Pose p)
    {
        float step = Mathf.Sin(p.phase * Mathf.PI * 2f) * 16f;
        c.Line(135 + step, 70, 128 + step * 1.3f, 12, 13, b * 0.8f);
        c.Line(160 - step, 70, 166 - step * 1.3f, 12, 13, b * 0.8f);
        c.Ellipse(190, 150, 70, 66, 0, new Color(0.55f, 0.38f, 0.2f));      // 背中の大きな宝袋
        c.Ellipse(190, 206, 26, 12, 0, new Color(0.45f, 0.3f, 0.15f));       // 袋の口
        c.Ellipse(178, 222, 12, 10, 0, a); c.Ellipse(200, 226, 11, 9, 0, a); c.Ellipse(192, 236, 9, 8, 0, a); // こぼれる金貨
        c.Ellipse(145, 110, 36, 42, 0, b);                                   // 胴
        c.Ellipse(118, 160, 30, 28, 0, b);                                   // 頭
        c.Tri(128, 176, 160, 196, 138, 164, b);                              // とがった耳
        c.Line(125, 120, 170, 150, 9, b * 0.9f);                             // 袋をつかむ腕
        Eyes(c, 104, 164, -1, 0, p, new Color(0.9f, 0.85f, 0.2f));
    }

    static void Mimic(Canvas c, Color b, Color a, Pose p)
    {
        bool closed = p.dark || p.gray;                                      // 休眠=ただの宝箱
        float open = closed ? 0f : (p.raise > 0f ? 0.7f : 1f);
        c.Rect(40, 12, 240, 110, b);                                          // 箱
        c.Rect(40, 60, 240, 70, a); c.Rect(130, 12, 150, 110, a);            // 金具
        float lid = 110 + open * 60;
        c.Poly4(40, 110, 240, 110, 240 - open * 30, lid + 40, 40 - open * 10, lid + 30, b * 0.9f); // ふた
        if (open > 0f)
        {
            c.Rect(50, 108, 230, 112 + open * 30, new Color(0.35f, 0.05f, 0.08f));   // 口の中
            for (int i = 0; i < 7; i++) c.Tri(55 + i * 25, 112, 67 + i * 25, 112, 61 + i * 25, 130, Color.white);  // 歯
            c.Ellipse(90, 150 + open * 30, 10, 12, 0, new Color(1f, 0.85f, 0.2f)); // 目
            c.Ellipse(80, 118, 26, 10, -20, new Color(0.85f, 0.25f, 0.35f));       // 舌
        }
    }

    static void GoldenSlime(Canvas c, Color b, Color a, Pose p)
    {
        float up = 20f + p.raise * 10f;
        c.Ellipse(128, up + 58, 96, 62, 0, b);
        c.Ellipse(128, up + 110, 52, 34, 0, b);
        c.Ellipse(100, up + 100, 22, 16, -20, a);                            // つやの光
        c.Ellipse(96, up + 70, 8, 11, 0, new Color(0.35f, 0.2f, 0.05f)); c.Ellipse(128, up + 70, 8, 11, 0, new Color(0.35f, 0.2f, 0.05f));
        c.Ring(128, up + 150, 22, 4, a);                                     // 小さな光輪
    }

    static void CardFairy(Canvas c, Color b, Color a, Pose p)
    {
        Wings(c, 140, 140, 110, 0.4f + p.wing * 0.6f, a * 0.9f);
        c.Ellipse(130, 120, 22, 38, 0, b);                                   // 体
        c.Ellipse(124, 176, 24, 24, 0, b);                                   // 頭
        c.Ellipse(132, 192, 28, 14, 0, new Color(1f, 0.9f, 0.4f));           // 髪
        c.Rect(78, 90, 108, 132, Color.white); c.Rect(82, 94, 104, 128, new Color(0.6f, 0.4f, 0.95f)); // 抱えたカード
        c.Ring(92, 111, 26, 4, new Color(1f, 0.95f, 0.6f));
        Eyes(c, 114, 178, -1, 0, p, new Color(0.3f, 0.1f, 0.4f));
    }

    static void Eyes(Canvas c, float x1, float y1, float x2, float y2, Pose p, Color normal)
    {
        Color e = p.glowEyes ? new Color(1f, 0.85f, 0.3f) : normal;
        c.Ellipse(x1, y1, 7, 9, 0, e);
        if (x2 >= 0) c.Ellipse(x2, y2, 7, 9, 0, e);
    }

    // ---- 描画 ----
    class Canvas
    {
        public Texture2D tex; public Color[] px; public int w, h; public Pose pose; public Vector2 pivot;
        public Canvas(int w, int h) { this.w = w; this.h = h; tex = new Texture2D(w, h, TextureFormat.RGBA32, false); px = new Color[w * h]; }

        // ポーズの変形(のけぞり/前傾=せん断、回転、縮み)を点に掛ける。
        Vector2 T(float x, float y)
        {
            float dy = y - pivot.y;
            x += dy * pose.lean;
            y = pivot.y + dy * pose.squash;
            if (Mathf.Abs(pose.rot) > 0.01f)
            {
                float r = pose.rot * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
                Vector2 d = new Vector2(x - pivot.x, y - (pivot.y + 60f));
                x = pivot.x + d.x * cs - d.y * sn; y = pivot.y + 60f + d.x * sn + d.y * cs;
            }
            return new Vector2(x, y);
        }

        void Put(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || c.a <= 0f) return;
            int i = y * w + x; Color d = px[i];
            float a = c.a + d.a * (1f - c.a);
            px[i] = a > 0f ? new Color((c.r * c.a + d.r * d.a * (1f - c.a)) / a, (c.g * c.a + d.g * d.a * (1f - c.a)) / a, (c.b * c.a + d.b * d.a * (1f - c.a)) / a, a) : Color.clear;
        }

        public void Ellipse(float cx, float cy, float rx, float ry, float deg, Color col)
        {
            col.a = 1f;
            Vector2 c0 = T(cx, cy);
            float sy = pose.squash;
            float rr = (deg + pose.rot) * Mathf.Deg2Rad, cs = Mathf.Cos(rr), sn = Mathf.Sin(rr);
            float ry2 = ry * sy, m = Mathf.Max(rx, ry2) + 2;
            for (int y = (int)(c0.y - m); y <= c0.y + m; y++)
                for (int x = (int)(c0.x - m); x <= c0.x + m; x++)
                {
                    float dx = x - c0.x, dy = y - c0.y;
                    float u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
                    float d = (u * u) / (rx * rx) + (v * v) / (ry2 * ry2);
                    if (d <= 1f) Put(x, y, Shade(col, d));
                }
        }

        public void Ring(float cx, float cy, float r, float th, Color col)
        {
            Vector2 c0 = T(cx, cy);
            for (int y = (int)(c0.y - r - th); y <= c0.y + r + th; y++)
                for (int x = (int)(c0.x - r - th); x <= c0.x + r + th; x++)
                {
                    float d = Mathf.Abs(Vector2.Distance(new Vector2(x, y), c0) - r);
                    if (d <= th) { Color k = col; k.a = 0.85f * (1f - d / th); Put(x, y, k); }
                }
        }

        public void Rect(float x0, float y0, float x1, float y1, Color col) =>
            Poly(new[] { T(x0, y0), T(x1, y0), T(x1, y1), T(x0, y1) }, col);

        public void Poly4(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, Color col) =>
            Poly(new[] { T(ax, ay), T(bx, by), T(cx, cy), T(dx, dy) }, col);

        public void Tri(float ax, float ay, float bx, float by, float cx, float cy, Color col) =>
            Poly(new[] { T(ax, ay), T(bx, by), T(cx, cy) }, col);

        public void Line(float x0, float y0, float x1, float y1, float width, Color col)
        {
            Vector2 a = T(x0, y0), b = T(x1, y1);
            Vector2 n = (b - a).normalized; n = new Vector2(-n.y, n.x) * width * 0.5f;
            Poly(new[] { a + n, b + n, b - n, a - n }, col);
            Ellipse2(a, width * 0.5f, col); Ellipse2(b, width * 0.5f, col);
        }

        void Ellipse2(Vector2 c0, float r, Color col)
        {
            col.a = 1f;
            for (int y = (int)(c0.y - r); y <= c0.y + r; y++)
                for (int x = (int)(c0.x - r); x <= c0.x + r; x++)
                    if ((x - c0.x) * (x - c0.x) + (y - c0.y) * (y - c0.y) <= r * r) Put(x, y, col);
        }

        void Poly(Vector2[] pts, Color col)
        {
            col.a = 1f;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            for (int y = (int)minY; y <= maxY; y++)
                for (int x = (int)minX; x <= maxX; x++)
                    if (Inside(pts, x + 0.5f, y + 0.5f)) Put(x, y, Shade(col, (y - minY) / Mathf.Max(1f, maxY - minY) * 0.5f));
        }

        static bool Inside(Vector2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > y) != (p[j].y > y)) && (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) c = !c;
            return c;
        }

        // 立体感(縁を少し暗く)
        static Color Shade(Color c, float d) { float k = Mathf.Lerp(1.05f, 0.82f, Mathf.Clamp01(d)); return new Color(c.r * k, c.g * k, c.b * k, 1f); }

        public void Finish(Pose p)
        {
            // 輪郭線(背景に埋もれない)+ 色の混ぜ(被弾の白/予兆の橙)+ 透明度
            var outp = (Color[])px.Clone();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (px[i].a > 0.5f)
                    {
                        Color c = px[i];
                        if (p.mixAmt > 0f) c = Color.Lerp(c, p.mix, p.mixAmt);
                        c.a *= p.alpha;
                        outp[i] = c;
                        continue;
                    }
                    bool edge = px[i - 1].a > 0.5f || px[i + 1].a > 0.5f || px[i - w].a > 0.5f || px[i + w].a > 0.5f;
                    if (edge) outp[i] = new Color(0.08f, 0.1f, 0.16f, 0.95f * p.alpha);
                }
            tex.SetPixels(outp);
            tex.Apply();
        }
    }
}

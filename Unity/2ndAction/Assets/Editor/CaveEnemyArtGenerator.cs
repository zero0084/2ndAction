using System.IO;
using UnityEditor;
using UnityEngine;

// 自然洞窟雑魚敵追加(2026-09-22) - この環境からはChatGPT等の外部画像生成を
// 直接呼び出せないため、暫定素材として「手続き的に描画したシルエットPNG」
// を実際にディスクへ書き出し、既存のスプライト取り込みパイプライン
// (ConfigureAndLoadSpriteWithFootPivot等)へそのまま流せる形にする。
//
// 荒野街道の既存6種(Goblin/Flying/Irregular/Shooter/Heavy/Runner)と同じ
// 構成 - 1枚の静止ポートレート + 5枚の走行/羽ばたきコマ(同一キャラクター・
// 同一比率・同一向き、関節位相だけをコマごとにずらす)。手続き的生成なので
// 「同じデザイン・同じ比率・同じカメラ角度」は構造的に保証される。
//
// 重要: これはあくまで仮素材(シルエットのみ、写実的な描き込みなし)。
// 将来ChatGPT/Grok等で本物のイラストを生成したら、Assets/Art/Enemy/配下の
// 同名ファイルを差し替えるだけで良い(EnemyDatabaseBuilderはパス参照のみ)。
public static class CaveEnemyArtGenerator
{
    const string EnemyDir = "Assets/Art/Enemy";

    [MenuItem("Tools/OneMoreMile/Generate Cave Enemy Art (placeholder)")]
    public static void Generate()
    {
        Directory.CreateDirectory(EnemyDir);

        GenerateSet("CaveAnt", 220, 140, new Color32(40, 30, 24, 255), new Color32(70, 55, 42, 255), DrawAnt);
        GenerateSet("SoldierAnt", 240, 160, new Color32(58, 26, 18, 255), new Color32(110, 55, 30, 255), DrawSoldierAnt);
        GenerateSet("CaveHopper", 200, 190, new Color32(50, 55, 48, 255), new Color32(90, 100, 85, 255), DrawHopper);
        GenerateSet("CaveBat", 260, 200, new Color32(30, 28, 34, 255), new Color32(60, 55, 65, 255), DrawBat);
        GenerateSet("BurrowWorm", 320, 170, new Color32(55, 40, 34, 255), new Color32(90, 95, 100, 255), DrawWorm);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CaveEnemyArtGenerator] Generated placeholder portraits + run-cycle frames under " + EnemyDir);
    }

    delegate void DrawFn(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent);

    static void GenerateSet(string name, int w, int h, Color32 body, Color32 accent, DrawFn draw)
    {
        string portraitPath = $"{EnemyDir}/{name}.png";
        if (!File.Exists(portraitPath))
        {
            WriteFrame(portraitPath, w, h, 0.2f, body, accent, draw);
        }

        string runDir = $"Assets/Art/{name}Run";
        Directory.CreateDirectory(runDir);
        for (int i = 0; i < 5; i++)
        {
            string framePath = $"{runDir}/{name.ToLowerInvariant()}_run_{i}.png";
            if (File.Exists(framePath)) continue;
            float phase = i / 5f;
            WriteFrame(framePath, w, h, phase, body, accent, draw);
        }
    }

    static void WriteFrame(string path, int w, int h, float phase, Color32 body, Color32 accent, DrawFn draw)
    {
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        draw(px, w, h, phase, body, accent);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ---- shape helpers (0..1 alpha, u/v in 0..1 canvas space) ---- //
    static float Ellipse(float u, float v, float cx, float cy, float rx, float ry, float edge = 0.02f)
    {
        float dx = (u - cx) / Mathf.Max(0.001f, rx), dy = (v - cy) / Mathf.Max(0.001f, ry);
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01((1f - r) / edge);
    }

    static float Box(float u, float v, float cx, float cy, float hw, float hh, float edge = 0.015f)
    {
        float dx = Mathf.Abs(u - cx) - hw, dy = Mathf.Abs(v - cy) - hh;
        float d = Mathf.Max(dx, dy);
        return Mathf.Clamp01(-d / edge + 0.5f);
    }

    // 線分(太さ2*hw)へのざっくりした距離ベースの棒 - 脚/触角/羽の骨組み用。
    static float Bar(float u, float v, float x0, float y0, float x1, float y1, float halfWidth)
    {
        Vector2 p = new Vector2(u, v), a = new Vector2(x0, y0), b = new Vector2(x1, y1);
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        Vector2 closest = a + ab * t;
        float d = Vector2.Distance(p, closest);
        return Mathf.Clamp01((halfWidth - d) / 0.012f);
    }

    static void Paint(Color32[] px, int w, int h, int x, int y, float a, Color32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h || a <= 0f) return;
        int idx = y * w + x;
        byte newA = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
        if (newA <= px[idx].a) return; // 既存の方が濃ければ上書きしない(輪郭の重なりを綺麗に保つ)
        px[idx] = new Color32(c.r, c.g, c.b, newA);
    }

    static void Fill(Color32[] px, int w, int h, System.Func<float, float, float> shapeAt, Color32 c)
    {
        for (int y = 0; y < h; y++)
        {
            float v = 1f - (y + 0.5f) / h; // v=0が画像下端(地面側)になるようY反転
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float a = shapeAt(u, v);
                if (a > 0f) Paint(px, w, h, x, y, a, c);
            }
        }
    }

    // ============ Cave Ant / 洞窟アリ ============
    static void DrawAnt(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent)
    {
        float legSwing = Mathf.Sin(phase * Mathf.PI * 2f) * 0.05f;
        float antennaSwing = Mathf.Sin(phase * Mathf.PI * 2f + 1f) * 0.03f;
        float jaw = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f * 0.5f); // 0..1、ゆっくり開閉

        Fill(px, w, h, (u, v) =>
        {
            float thorax = Ellipse(u, v, 0.46f, 0.42f, 0.16f, 0.13f);
            float abdomen = Ellipse(u, v, 0.72f, 0.40f, 0.20f, 0.16f);
            float head = Ellipse(u, v, 0.20f, 0.44f, 0.11f, 0.10f);
            return Mathf.Max(Mathf.Max(thorax, abdomen), head);
        }, body);

        // 顎(開閉)
        Fill(px, w, h, (u, v) =>
        {
            float jawGap = 0.03f + jaw * 0.05f;
            float upper = Bar(u, v, 0.11f, 0.44f + jawGap, 0.01f, 0.40f + jawGap, 0.012f);
            float lower = Bar(u, v, 0.11f, 0.44f - jawGap, 0.01f, 0.40f - jawGap, 0.012f);
            return Mathf.Max(upper, lower);
        }, accent);

        // 触角(揺れる)
        Fill(px, w, h, (u, v) =>
        {
            float a1 = Bar(u, v, 0.20f, 0.52f, 0.10f, 0.66f + antennaSwing, 0.008f);
            float a2 = Bar(u, v, 0.18f, 0.52f, 0.06f, 0.62f - antennaSwing, 0.008f);
            return Mathf.Max(a1, a2);
        }, accent);

        // 脚6本(左右3対、位相で上下)
        Fill(px, w, h, (u, v) =>
        {
            float legs = 0f;
            for (int i = 0; i < 3; i++)
            {
                float lx = 0.36f + i * 0.14f;
                float sw = Mathf.Sin(phase * Mathf.PI * 2f + i * 2.1f) * 0.06f;
                legs = Mathf.Max(legs, Bar(u, v, lx, 0.34f, lx - 0.10f + sw, 0.10f, 0.012f));
                legs = Mathf.Max(legs, Bar(u, v, lx, 0.50f, lx - 0.08f - sw, 0.66f + legSwing, 0.012f));
            }
            return legs;
        }, body);

        // ハイライト(背景に埋もれないよう甲殻の縁を少し明るく)
        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.72f, 0.46f, 0.16f, 0.10f) * 0.25f, accent);
    }

    // ============ Soldier Ant / 兵隊アリ ============
    static void DrawSoldierAnt(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent)
    {
        float legSwing = Mathf.Sin(phase * Mathf.PI * 2f) * 0.05f;
        float jaw = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f * 0.5f);

        Fill(px, w, h, (u, v) =>
        {
            float thorax = Ellipse(u, v, 0.46f, 0.42f, 0.17f, 0.14f);
            float abdomen = Ellipse(u, v, 0.74f, 0.40f, 0.21f, 0.17f);
            float head = Ellipse(u, v, 0.18f, 0.46f, 0.14f, 0.13f); // 頭部/顎が大きい
            return Mathf.Max(Mathf.Max(thorax, abdomen), head);
        }, body);

        // 巨大な顎(2本、開閉が大きく見えるように長め)
        Fill(px, w, h, (u, v) =>
        {
            float jawGap = 0.04f + jaw * 0.09f;
            float upper = Bar(u, v, 0.10f, 0.46f + jawGap * 0.3f, -0.06f, 0.40f + jawGap, 0.016f);
            float lower = Bar(u, v, 0.10f, 0.46f - jawGap * 0.3f, -0.06f, 0.40f - jawGap, 0.016f);
            return Mathf.Max(upper, lower);
        }, accent);

        Fill(px, w, h, (u, v) =>
        {
            float a1 = Bar(u, v, 0.20f, 0.56f, 0.12f, 0.70f, 0.010f);
            float a2 = Bar(u, v, 0.16f, 0.56f, 0.06f, 0.68f, 0.010f);
            return Mathf.Max(a1, a2);
        }, accent);

        Fill(px, w, h, (u, v) =>
        {
            float legs = 0f;
            for (int i = 0; i < 3; i++)
            {
                float lx = 0.38f + i * 0.15f;
                float sw = Mathf.Sin(phase * Mathf.PI * 2f + i * 2.1f) * 0.07f;
                legs = Mathf.Max(legs, Bar(u, v, lx, 0.32f, lx - 0.11f + sw, 0.08f, 0.014f));
                legs = Mathf.Max(legs, Bar(u, v, lx, 0.50f, lx - 0.09f - sw, 0.68f + legSwing, 0.014f));
            }
            return legs;
        }, body);

        // 頭部/胸の装甲質感(明るい帯)
        Fill(px, w, h, (u, v) => Mathf.Max(Ellipse(u, v, 0.18f, 0.50f, 0.13f, 0.11f), Ellipse(u, v, 0.74f, 0.46f, 0.17f, 0.11f)) * 0.3f, accent);
    }

    // ============ Cave Hopper / 跳ね虫 ============
    static void DrawHopper(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent)
    {
        float legCrouch = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f); // 0..1、後脚の溜め具合
        float antenna = Mathf.Sin(phase * Mathf.PI * 2f + 0.6f) * 0.03f;

        Fill(px, w, h, (u, v) =>
        {
            float body1 = Ellipse(u, v, 0.44f, 0.50f, 0.20f, 0.15f);
            float head = Ellipse(u, v, 0.20f, 0.56f, 0.10f, 0.09f);
            return Mathf.Max(body1, head);
        }, body);

        // 発達した後脚(太腿+すね、しゃがみ具合で角度が変わる)
        Fill(px, w, h, (u, v) =>
        {
            float kneeX = 0.58f, kneeY = Mathf.Lerp(0.38f, 0.28f, legCrouch);
            float thigh = Bar(u, v, 0.52f, 0.46f, kneeX, kneeY, 0.030f);
            float footX = Mathf.Lerp(0.72f, 0.66f, legCrouch);
            float shin = Bar(u, v, kneeX, kneeY, footX, 0.06f, 0.018f);
            return Mathf.Max(thigh, shin);
        }, body);

        // 前脚(細い、2本)
        Fill(px, w, h, (u, v) =>
        {
            float f1 = Bar(u, v, 0.30f, 0.44f, 0.22f, 0.20f, 0.010f);
            float f2 = Bar(u, v, 0.34f, 0.44f, 0.30f, 0.18f, 0.010f);
            return Mathf.Max(f1, f2);
        }, body);

        // 触角
        Fill(px, w, h, (u, v) => Bar(u, v, 0.20f, 0.62f, 0.08f, 0.76f + antenna, 0.008f), accent);

        // 目(弱い発光アクセント)
        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.16f, 0.58f, 0.02f, 0.02f), accent);

        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.44f, 0.54f, 0.19f, 0.13f) * 0.25f, accent);
    }

    // ============ Cave Bat / 洞窟コウモリ ============
    static void DrawBat(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent)
    {
        float flap = Mathf.Sin(phase * Mathf.PI * 2f); // -1..1

        Fill(px, w, h, (u, v) => Mathf.Max(Ellipse(u, v, 0.5f, 0.46f, 0.09f, 0.13f), Ellipse(u, v, 0.5f, 0.62f, 0.07f, 0.07f)), body);

        // 翼(左右、羽ばたき角度で先端が上下)
        Fill(px, w, h, (u, v) =>
        {
            float wings = 0f;
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f;
                float span = Mathf.Lerp(0.10f, 0.40f, t);
                float dy = flap * 0.18f * t - 0.02f;
                wings = Mathf.Max(wings, Ellipse(u, v, 0.5f - span, 0.46f + dy, 0.045f, 0.13f - t * 0.06f));
                wings = Mathf.Max(wings, Ellipse(u, v, 0.5f + span, 0.46f + dy, 0.045f, 0.13f - t * 0.06f));
            }
            return wings;
        }, body);

        // 耳
        Fill(px, w, h, (u, v) => Mathf.Max(Ellipse(u, v, 0.44f, 0.72f, 0.025f, 0.05f), Ellipse(u, v, 0.56f, 0.72f, 0.025f, 0.05f)), accent);

        // 目(発光)
        Fill(px, w, h, (u, v) => Mathf.Max(Ellipse(u, v, 0.46f, 0.62f, 0.012f, 0.012f), Ellipse(u, v, 0.54f, 0.62f, 0.012f, 0.012f)), accent);

        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.5f, 0.46f, 0.085f, 0.12f) * 0.2f, accent);
    }

    // ============ Burrow Worm / 地中ワーム ============
    static void DrawWorm(Color32[] px, int w, int h, float phase, Color32 body, Color32 accent)
    {
        float undulate = phase * Mathf.PI * 2f;

        Fill(px, w, h, (u, v) =>
        {
            float b = 0f;
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                float bx = 0.12f + t * 0.72f;
                float by = 0.40f + Mathf.Sin(undulate + t * 4.2f) * 0.05f;
                float rad = Mathf.Lerp(0.075f, 0.13f, 1f - Mathf.Abs(t - 0.2f) * 0.7f);
                b = Mathf.Max(b, Ellipse(u, v, bx, by, rad, rad));
            }
            return b;
        }, body);

        // 節の輪(セグメント感)
        Fill(px, w, h, (u, v) =>
        {
            float rings = 0f;
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float bx = 0.20f + t * 0.58f;
                float by = 0.40f + Mathf.Sin(undulate + t * 4.2f) * 0.05f;
                rings = Mathf.Max(rings, Box(u, v, bx, by, 0.006f, 0.11f) * 0.5f);
            }
            return rings;
        }, accent);

        // 口(大きく開いた円、先端側)
        Fill(px, w, h, (u, v) =>
        {
            float mouthY = 0.40f + Mathf.Sin(undulate + 4.2f) * 0.05f;
            return Ellipse(u, v, 0.90f, mouthY, 0.075f, 0.075f);
        }, accent);
        Fill(px, w, h, (u, v) =>
        {
            float mouthY = 0.40f + Mathf.Sin(undulate + 4.2f) * 0.05f;
            return Ellipse(u, v, 0.90f, mouthY, 0.05f, 0.05f);
        }, new Color32(20, 12, 10, 255));
    }
}

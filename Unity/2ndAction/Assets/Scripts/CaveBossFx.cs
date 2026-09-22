using System.Collections;
using UnityEngine;

// 自然洞窟ボス追加(2026-09-22) - 荒野街道ボス(WildBossBase/WildBosses.cs)を
// そのまま流用して洞窟ボス群を実装するための、洞窟専用の追加分だけをここに
// まとめる。
//
// ■ ボディ素材について(重要・引継ぎ事項)
// 荒野街道の各ボスは「1〜3枚の実イラスト(idle/windup/moveのPNG、Assets/Art/
// WildBoss/配下)+ BossRig(WildBossBase.cs参照)による手続き的な脚/身体の
// 変形」という構成。今回はイラスト素材(ChatGPT等での生成)をこの環境からは
// 直接生成できないため、暫定として下のCaveBossFxが手続き的に生成するシル
// エット(輪郭のみの単色スプライト、既存のBossFx.Make()と同じ仕組み)を
// idle/windup素材として使う。BossRigによる脚の動き/伸縮等のアニメーション
// 自体は本物のイラストと同じ仕組みでそのまま機能する(=「歩いているように
// 見える」という要件は満たす)が、見た目の作り込みは仮のプレースホルダー
// である旨、実装完了報告で明記する。将来ChatGPT/Grok等で本物のイラストを
// 生成したら、BossManagerのcaveArt[]へ差し替えるだけで済むようになっている
// (荒野街道のwildArt[]と全く同じ仕組み)。
public static class CaveBossFx
{
    static Sprite centipede, scorpion, mole, troll, worm, crystalGolem, bat, scorpionKing, basilisk, drake, ancientDemon;
    static Sprite rockChunk, crystalShard;

    static Sprite Make(int size, System.Func<float, float, float> alphaAt)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Clamp01(alphaAt((x + 0.5f) / size, (y + 0.5f) / size));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // 中心(cx,cy)・半径(rx,ry)の楕円内=1、縁で滑らかに0へ。
    static float Ellipse(float u, float v, float cx, float cy, float rx, float ry, float edge = 0.06f)
    {
        float dx = (u - cx) / Mathf.Max(0.001f, rx), dy = (v - cy) / Mathf.Max(0.001f, ry);
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01((1f - r) / edge);
    }

    static float Box(float u, float v, float cx, float cy, float hw, float hh, float edge = 0.04f)
    {
        float dx = Mathf.Abs(u - cx) - hw, dy = Mathf.Abs(v - cy) - hh;
        float d = Mathf.Max(dx, dy);
        return Mathf.Clamp01(-d / edge + 0.5f);
    }

    static float Max(float a, float b) => a > b ? a : b;

    // ============ 1,000m 巨大ムカデ ============
    public static Sprite Centipede()
    {
        if (centipede != null) return centipede;
        centipede = Make(160, (u, v) =>
        {
            float body = Ellipse(u, v, 0.52f, 0.5f, 0.42f, 0.12f);
            float head = Ellipse(u, v, 0.90f, 0.5f, 0.10f, 0.10f);
            float legs = 0f;
            for (int i = 0; i < 9; i++)
            {
                float lx = 0.14f + i * 0.085f;
                legs = Max(legs, Box(u, v, lx, 0.62f, 0.012f, 0.10f));
                legs = Max(legs, Box(u, v, lx, 0.38f, 0.012f, 0.10f));
            }
            float segLines = 0f;
            for (int i = 0; i < 9; i++)
            {
                float lx = 0.16f + i * 0.085f;
                segLines = Max(segLines, Box(u, v, lx, 0.5f, 0.006f, 0.11f) * 0.5f);
            }
            return Mathf.Max(Mathf.Max(body, head) - segLines * 0.3f, legs * 0.9f);
        });
        return centipede;
    }

    // ============ 5,000m 巨大サソリ ============
    public static Sprite Scorpion()
    {
        if (scorpion != null) return scorpion;
        scorpion = Make(160, (u, v) =>
        {
            float body = Ellipse(u, v, 0.42f, 0.42f, 0.22f, 0.16f);
            float clawL = Ellipse(u, v, 0.12f, 0.30f, 0.10f, 0.08f);
            float clawR = Ellipse(u, v, 0.12f, 0.56f, 0.10f, 0.08f);
            float tail = 0f;
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                float tx = 0.60f + t * 0.28f;
                float ty = 0.42f + Mathf.Sin(t * 2.0f) * 0.22f + t * 0.10f;
                tail = Max(tail, Ellipse(u, v, tx, ty, 0.055f - t * 0.02f, 0.055f - t * 0.02f));
            }
            float sting = Ellipse(u, v, 0.90f, 0.86f, 0.035f, 0.035f);
            float legs = 0f;
            for (int i = 0; i < 4; i++)
            {
                float lx = 0.30f + i * 0.06f;
                legs = Max(legs, Box(u, v, lx, 0.20f, 0.01f, 0.08f));
                legs = Max(legs, Box(u, v, lx, 0.64f, 0.01f, 0.08f));
            }
            return Mathf.Max(Mathf.Max(Mathf.Max(body, clawL), Mathf.Max(clawR, tail)), Mathf.Max(sting, legs * 0.85f));
        });
        return scorpion;
    }

    // ============ 10,000m 巨大モグラ ============
    public static Sprite Mole()
    {
        if (mole != null) return mole;
        mole = Make(128, (u, v) =>
        {
            float body = Ellipse(u, v, 0.46f, 0.44f, 0.34f, 0.28f);
            float snout = Ellipse(u, v, 0.86f, 0.44f, 0.12f, 0.10f);
            float pawL = Ellipse(u, v, 0.28f, 0.14f, 0.09f, 0.07f);
            float pawR = Ellipse(u, v, 0.46f, 0.12f, 0.09f, 0.07f);
            return Mathf.Max(Mathf.Max(body, snout), Mathf.Max(pawL, pawR));
        });
        return mole;
    }

    // ============ 20,000m ケイブトロル ============
    public static Sprite Troll()
    {
        if (troll != null) return troll;
        troll = Make(160, (u, v) =>
        {
            float legs = Box(u, v, 0.42f, 0.12f, 0.16f, 0.12f);
            float torso = Box(u, v, 0.44f, 0.42f, 0.24f, 0.20f, 0.08f);
            float armL = Ellipse(u, v, 0.20f, 0.42f, 0.09f, 0.16f);
            float armR = Ellipse(u, v, 0.70f, 0.40f, 0.10f, 0.20f);
            float head = Ellipse(u, v, 0.44f, 0.70f, 0.15f, 0.14f);
            float club = Box(u, v, 0.82f, 0.62f, 0.06f, 0.22f);
            return Mathf.Max(Mathf.Max(legs, torso), Mathf.Max(Mathf.Max(armL, armR), Mathf.Max(head, club)));
        });
        return troll;
    }

    // ============ 30,000m 巨大地底ワーム ============
    public static Sprite Worm()
    {
        if (worm != null) return worm;
        worm = Make(160, (u, v) =>
        {
            float body = 0f;
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                float bx = 0.15f + t * 0.7f;
                float by = 0.5f + Mathf.Sin(t * 5.5f) * 0.22f;
                float rad = Mathf.Lerp(0.085f, 0.14f, 1f - Mathf.Abs(t - 0.15f));
                body = Max(body, Ellipse(u, v, bx, by, rad, rad));
            }
            float maw = Ellipse(u, v, 0.90f, 0.5f + Mathf.Sin(0.15f * 5.5f) * 0.22f, 0.08f, 0.08f);
            return Mathf.Max(body, maw);
        });
        return worm;
    }

    // ============ 40,000m クリスタルゴーレム ============
    public static Sprite CrystalGolem()
    {
        if (crystalGolem != null) return crystalGolem;
        crystalGolem = Make(160, (u, v) =>
        {
            float legs = Box(u, v, 0.44f, 0.12f, 0.18f, 0.12f);
            float torso = Box(u, v, 0.46f, 0.46f, 0.26f, 0.24f, 0.10f);
            float shardL = Box(u, v, 0.24f, 0.6f, 0.05f, 0.14f);
            float shardR = Box(u, v, 0.70f, 0.62f, 0.05f, 0.18f);
            float shardTop = Box(u, v, 0.46f, 0.76f, 0.06f, 0.10f);
            float head = Box(u, v, 0.46f, 0.68f, 0.12f, 0.10f);
            float armL = Box(u, v, 0.18f, 0.42f, 0.07f, 0.18f);
            float armR = Box(u, v, 0.76f, 0.40f, 0.08f, 0.20f);
            return Mathf.Max(Mathf.Max(Mathf.Max(legs, torso), Mathf.Max(shardL, shardR)), Mathf.Max(shardTop, Mathf.Max(head, Mathf.Max(armL, armR))));
        });
        return crystalGolem;
    }

    // ============ 50,000m 巨大コウモリ ============
    public static Sprite Bat()
    {
        if (bat != null) return bat;
        bat = Make(160, (u, v) =>
        {
            float body = Ellipse(u, v, 0.5f, 0.5f, 0.10f, 0.16f);
            float head = Ellipse(u, v, 0.5f, 0.68f, 0.08f, 0.08f);
            float wingL = 0f, wingR = 0f;
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                float span = Mathf.Lerp(0.14f, 0.46f, t);
                float dy = -0.18f * t;
                wingL = Max(wingL, Ellipse(u, v, 0.5f - span, 0.5f + dy, 0.05f, 0.14f - t * 0.08f));
                wingR = Max(wingR, Ellipse(u, v, 0.5f + span, 0.5f + dy, 0.05f, 0.14f - t * 0.08f));
            }
            float earL = Ellipse(u, v, 0.44f, 0.80f, 0.03f, 0.05f);
            float earR = Ellipse(u, v, 0.56f, 0.80f, 0.03f, 0.05f);
            return Mathf.Max(Mathf.Max(body, head), Mathf.Max(Mathf.Max(wingL, wingR), Mathf.Max(earL, earR)));
        });
        return bat;
    }

    // ============ 60,000m スコーピオンキング ============
    public static Sprite ScorpionKing()
    {
        if (scorpionKing != null) return scorpionKing;
        scorpionKing = Make(160, (u, v) =>
        {
            float body = Ellipse(u, v, 0.42f, 0.42f, 0.26f, 0.20f);
            float shellRidge = Box(u, v, 0.42f, 0.54f, 0.20f, 0.02f) * 0.6f;
            float clawL = Ellipse(u, v, 0.10f, 0.26f, 0.12f, 0.10f);
            float clawR = Ellipse(u, v, 0.10f, 0.60f, 0.12f, 0.10f);
            float crownL = Box(u, v, 0.30f, 0.70f, 0.02f, 0.06f);
            float crownR = Box(u, v, 0.40f, 0.72f, 0.02f, 0.07f);
            float tail = 0f;
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                float tx = 0.62f + t * 0.30f;
                float ty = 0.42f + Mathf.Sin(t * 2.0f) * 0.26f + t * 0.12f;
                tail = Max(tail, Ellipse(u, v, tx, ty, 0.065f - t * 0.02f, 0.065f - t * 0.02f));
            }
            float sting = Ellipse(u, v, 0.94f, 0.90f, 0.045f, 0.045f);
            return Mathf.Max(Mathf.Max(Mathf.Max(body, clawL), Mathf.Max(clawR, tail)), Mathf.Max(sting, Mathf.Max(crownL, crownR)) + shellRidge);
        });
        return scorpionKing;
    }

    // ============ 70,000m バジリスク ============
    public static Sprite Basilisk()
    {
        if (basilisk != null) return basilisk;
        basilisk = Make(160, (u, v) =>
        {
            float body = Ellipse(u, v, 0.44f, 0.36f, 0.32f, 0.13f);
            float tail = Ellipse(u, v, 0.14f, 0.34f, 0.14f, 0.05f);
            float neck = Ellipse(u, v, 0.72f, 0.42f, 0.14f, 0.08f);
            float head = Ellipse(u, v, 0.88f, 0.48f, 0.10f, 0.07f);
            float jaw = Box(u, v, 0.92f, 0.42f, 0.08f, 0.02f);
            float legs = 0f;
            for (int i = 0; i < 2; i++)
            {
                float lx = 0.36f + i * 0.22f;
                legs = Max(legs, Box(u, v, lx, 0.20f, 0.03f, 0.10f));
            }
            float eye = Ellipse(u, v, 0.90f, 0.52f, 0.015f, 0.015f);
            return Mathf.Max(Mathf.Max(Mathf.Max(body, tail), Mathf.Max(neck, head)), Mathf.Max(Mathf.Max(jaw, legs), eye));
        });
        return basilisk;
    }

    // ============ 80,000m 地底竜 ============
    public static Sprite Drake()
    {
        if (drake != null) return drake;
        drake = Make(176, (u, v) =>
        {
            float body = Ellipse(u, v, 0.42f, 0.42f, 0.28f, 0.18f);
            float tail = 0f;
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float tx = 0.16f - t * 0.06f;
                float ty = 0.40f - t * 0.16f;
                tail = Max(tail, Ellipse(u, v, tx, ty, 0.07f - t * 0.02f, 0.07f - t * 0.02f));
            }
            float neck = Ellipse(u, v, 0.70f, 0.52f, 0.14f, 0.09f);
            float head = Ellipse(u, v, 0.88f, 0.58f, 0.10f, 0.08f);
            float jaw = Box(u, v, 0.94f, 0.52f, 0.06f, 0.02f);
            float wing = Box(u, v, 0.44f, 0.62f, 0.20f, 0.03f) * 0.7f;
            float legs = 0f;
            for (int i = 0; i < 2; i++)
            {
                float lx = 0.30f + i * 0.26f;
                legs = Max(legs, Box(u, v, lx, 0.16f, 0.04f, 0.14f));
            }
            return Mathf.Max(Mathf.Max(Mathf.Max(body, tail), Mathf.Max(neck, head)), Mathf.Max(Mathf.Max(jaw, wing), legs));
        });
        return drake;
    }

    // ============ 90,000m 古代地底悪魔 ============
    public static Sprite AncientDemon()
    {
        if (ancientDemon != null) return ancientDemon;
        ancientDemon = Make(176, (u, v) =>
        {
            float cloak = Box(u, v, 0.46f, 0.28f, 0.26f, 0.24f, 0.10f);
            float torso = Ellipse(u, v, 0.46f, 0.54f, 0.18f, 0.18f);
            float head = Ellipse(u, v, 0.46f, 0.76f, 0.12f, 0.11f);
            float hornL = Box(u, v, 0.36f, 0.90f, 0.02f, 0.07f);
            float hornR = Box(u, v, 0.56f, 0.90f, 0.02f, 0.07f);
            float armL = Ellipse(u, v, 0.20f, 0.48f, 0.08f, 0.16f);
            float armR = Ellipse(u, v, 0.72f, 0.48f, 0.08f, 0.16f);
            float eyeL = Ellipse(u, v, 0.42f, 0.76f, 0.018f, 0.018f);
            float eyeR = Ellipse(u, v, 0.50f, 0.76f, 0.018f, 0.018f);
            return Mathf.Max(Mathf.Max(cloak, torso), Mathf.Max(Mathf.Max(head, Mathf.Max(hornL, hornR)), Mathf.Max(Mathf.Max(armL, armR), Mathf.Max(eyeL, eyeR))));
        });
        return ancientDemon;
    }

    // ---- 攻撃VFX追加分(既存BossFx.Fang/Slash/Ring/Block/Orbで足りない形だけ) ----
    public static Sprite RockChunk()
    {
        if (rockChunk != null) return rockChunk;
        rockChunk = Make(64, (u, v) =>
        {
            float a = Box(u, v, 0.5f, 0.5f, 0.34f, 0.30f, 0.10f);
            float bevel = Box(u, v, 0.36f, 0.62f, 0.10f, 0.08f) * 0.5f;
            return Mathf.Clamp01(a - bevel);
        });
        return rockChunk;
    }

    public static Sprite CrystalShard()
    {
        if (crystalShard != null) return crystalShard;
        crystalShard = Make(64, (u, v) =>
        {
            float dx = Mathf.Abs(u - 0.5f), dy = v - 0.5f;
            float d = dx * 1.4f + Mathf.Abs(dy);
            return Mathf.Clamp01((0.46f - d) / 0.06f);
        });
        return crystalShard;
    }
}

// ============ 落石/天井由来の落下ハザード(トロル/クリスタルゴーレム/古代地底悪魔で共有) ============
// TrackedHazard(WildBossBase.cs)と同じ「プレイヤー基準速度で流れる」前提の
// 地面側の着弾警告に、天井から落ちてくる岩(見た目)を追加する。着弾の瞬間
// だけプレイヤーとの水平重なりを見て被弾判定する(見えている落下物=判定、
// という既存方針どおり)。
public class CeilingFallRock : MonoBehaviour
{
    SpriteRenderer sr;
    float targetWorldX, floorY, startY, fallDuration, width;
    float t;
    bool landed;
    public System.Action<Vector3> onLand; // 着弾位置を渡す

    Color tint = new Color(0.55f, 0.48f, 0.42f, 1f);

    public static CeilingFallRock Create(float worldX, float ceilingY, float floorY, float fallDuration, float width, float height, Color? color = null)
    {
        GameObject go = new GameObject("CeilingFallRock");
        var rock = go.AddComponent<CeilingFallRock>();
        rock.sr = go.AddComponent<SpriteRenderer>();
        rock.sr.sprite = CaveBossFx.RockChunk();
        rock.tint = color ?? rock.tint;
        rock.sr.color = rock.tint;
        rock.sr.sortingOrder = RenderOrder.Boss + 1;
        go.transform.localScale = new Vector3(width, height, 1f);
        go.transform.position = new Vector3(worldX, ceilingY - height * 0.5f, 0f);
        rock.targetWorldX = worldX;
        rock.floorY = floorY;
        rock.startY = ceilingY - height * 0.5f;
        rock.fallDuration = Mathf.Max(0.1f, fallDuration);
        rock.width = width;
        return rock;
    }

    void Update()
    {
        if (landed) return;
        t += Time.deltaTime;
        float f = Mathf.Clamp01(t / fallDuration);
        // 加速しながら落下(重力っぽさ)。
        float eased = f * f;
        Vector3 p = transform.position;
        p.y = Mathf.Lerp(startY, floorY, eased);
        transform.position = p;
        if (f >= 1f)
        {
            landed = true;
            Land();
        }
    }

    void Land()
    {
        Vector3 landPos = new Vector3(targetWorldX, floorY, 0f);
        if (PlayerController.Instance != null && Mathf.Abs(PlayerController.Instance.transform.position.x - targetWorldX) <= width * 0.5f)
        {
            PlayerController.Instance.TakeDamage(source: "CaveBossFallRock");
        }
        ExplosionEffect.Create(CaveBossFx.RockChunk(), landPos, tint, count: 10, duration: 0.6f, sizeScale: 0.6f, sortingOrder: RenderOrder.CombatFx);
        onLand?.Invoke(landPos);
        Destroy(gameObject);
    }
}

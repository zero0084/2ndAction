using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ===== 10〜12人目(2026-09-28)の「置く」攻撃 ===== //
// KitZone: 地面に置く範囲(巫女の結界 / 竜人の燃える地面)。一定間隔で範囲内の敵へ小ダメージ
//   (HitStop・ノックバックなし)、結界は範囲内の敵の横移動を遅くする(敵側のコードは変えず、
//   毎フレームの移動量を縮めるだけ)。種類ごとに同時に1つまで(置き直すと古い方は消える)。
// KitSealMark: 巫女の御札が敵に貼り付いた状態(時間差で浄化爆発、2枚目で即爆発)。
// KitArt: 御札・式神・コウモリの簡単な絵(実行時に1回だけ作る)。
public class KitZone : MonoBehaviour
{
    public enum Kind { Barrier, Fire }

    static readonly List<KitZone> active = new List<KitZone>();
    public static int ActiveCount(Kind k) { int n = 0; foreach (var z in active) if (z != null && z.kind == k) n++; return n; }
    public static KitZone Find(Kind k) { foreach (var z in active) if (z != null && z.kind == k) return z; return null; }

    public Kind kind;
    public float width, height, duration, tick, slow = 1f;
    public int TicksDone { get; private set; }
    public float Age => age;
    BoxCollider2D col;
    float age, tickTimer, hitWindow;
    readonly Dictionary<EnemyController, float> lastX = new Dictionary<EnemyController, float>();
    readonly List<SpriteRenderer> fx = new List<SpriteRenderer>();
    public static int SlowedFrames; // テスト用: 敵の移動を実際に遅くしたフレーム数

    // center = 地面の上の中心(yは地面)。
    public static KitZone Create(Kind k, Vector3 groundCenter, float width, float height, float duration, float tick, float damageScale, float slow)
    {
        ClearKind(k);
        var go = new GameObject(k == Kind.Barrier ? "MikoBarrier" : "BurningGround");
        go.tag = "PlayerAttack";
        go.transform.position = groundCenter;
        var z = go.AddComponent<KitZone>();
        z.kind = k; z.width = width; z.height = height; z.duration = duration; z.tick = tick; z.slow = slow;
        z.col = go.AddComponent<BoxCollider2D>();
        z.col.isTrigger = true;
        z.col.size = new Vector2(width, height);
        z.col.offset = new Vector2(0f, height * 0.5f);
        z.col.enabled = false;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        var info = go.AddComponent<PlayerAttackInfo>();
        info.kind = PlayerAttackKind.Normal;
        info.damageScale = damageScale;
        info.suppressHitStop = true;
        info.suppressKnockback = true;
        go.AddComponent<ColliderDebugView>().color = k == Kind.Barrier ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.2f);
        z.BuildVisual();
        active.Add(z);
        return z;
    }

    public static void ClearKind(Kind k)
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (active[i] == null) active.RemoveAt(i);
            else if (active[i].kind == k) { Destroy(active[i].gameObject); active.RemoveAt(i); }
    }

    public static void ClearAll()
    {
        foreach (var z in active) if (z != null) Destroy(z.gameObject);
        active.Clear();
    }

    void OnDestroy() { active.Remove(this); }

    void BuildVisual()
    {
        if (kind == Kind.Barrier)
        {
            // 地面から立ち上る半透明の光の壁(下が濃く上へ消える)+両端の御札+足元の輪
            var wall = AddFx(KitArt.GradientSprite(), new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 1f), new Color(1f, 0.82f, 0.45f, 0.5f), RenderOrder.CombatFx - 1);
            fx.Add(wall);
            if (KitProjectile.Ring != null)
            {
                var ring = AddFx(KitProjectile.Ring, new Vector3(0f, 0.05f, 0f), new Vector3(width * 1.05f, 0.45f, 1f), new Color(1f, 0.35f, 0.3f, 0.85f), RenderOrder.CombatFx);
                fx.Add(ring);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                var tag = AddFx(KitArt.OfudaSprite(), new Vector3(s * width * 0.5f, height * 0.55f, 0f), new Vector3(0.28f, 0.55f, 1f), Color.white, RenderOrder.CombatFx);
                fx.Add(tag);
            }
        }
        else
        {
            Sprite flame = Resources.Load<Sprite>("Effects/fireball");
            int n = Mathf.Max(3, Mathf.RoundToInt(width / 0.45f));
            for (int i = 0; i < n; i++)
            {
                float x = -width * 0.5f + width * (i + 0.5f) / n;
                var f = AddFx(flame != null ? flame : KitArt.GradientSprite(), new Vector3(x, 0.25f, 0f), new Vector3(0.55f, 0.7f, 1f), new Color(1f, 0.55f, 0.2f, 0.9f), RenderOrder.CombatFx);
                fx.Add(f);
            }
            var glow = AddFx(KitArt.GradientSprite(), new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 1f), new Color(1f, 0.35f, 0.1f, 0.35f), RenderOrder.CombatFx - 1);
            fx.Add(glow);
        }
    }

    SpriteRenderer AddFx(Sprite s, Vector3 local, Vector3 scale, Color c, int order)
    {
        var go = new GameObject("fx");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = c; sr.sortingOrder = order;
        return sr;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        // 判定は短い間だけ出す→消す、を一定間隔で繰り返す(重なっている敵にも毎回当たる)。
        // 1フレームだけだと物理の更新(FixedUpdate)を挟まずに消えて当たらないことがあるので、
        // 物理の更新を必ず2回はまたぐ長さだけ出しておく。
        if (col.enabled)
        {
            hitWindow -= dt;
            if (hitWindow <= 0f) col.enabled = false;
        }
        tickTimer -= dt;
        if (tickTimer <= 0f && age < duration && !col.enabled)
        {
            tickTimer = tick;
            col.enabled = true;
            hitWindow = Mathf.Max(0.05f, Time.fixedDeltaTime * 2.5f);
            TicksDone++;
        }
        // 見た目: 消える前の0.4秒でフェード、揺らめき
        float fade = Mathf.Clamp01((duration - age) / 0.4f);
        for (int i = 0; i < fx.Count; i++)
        {
            if (fx[i] == null) continue;
            Color c = fx[i].color;
            float flicker = kind == Kind.Fire ? 0.75f + 0.25f * Mathf.Sin(Time.time * 18f + i * 1.7f) : 0.85f + 0.15f * Mathf.Sin(Time.time * 5f);
            fx[i].color = new Color(c.r, c.g, c.b, Mathf.Min(1f, BaseAlpha(i) * fade * flicker));
            if (kind == Kind.Fire && i < fx.Count - 1) fx[i].transform.localScale = new Vector3(0.55f, 0.6f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 11f + i)), 1f);
        }
        if (age >= duration) Destroy(gameObject);
    }

    float BaseAlpha(int i) => kind == Kind.Barrier ? (i == 0 ? 0.45f : 0.9f) : (i == fx.Count - 1 ? 0.35f : 0.9f);

    // 結界: 範囲内の敵の横移動を遅くする(敵のUpdateで動いた分をLateUpdateで縮める)。
    void LateUpdate()
    {
        if (slow >= 0.999f) return;
        Vector2 c = (Vector2)transform.position + new Vector2(0f, height * 0.5f);
        var hits = Physics2D.OverlapBoxAll(c, new Vector2(width, height), 0f);
        var seen = new HashSet<EnemyController>();
        foreach (var h in hits)
        {
            var e = h.GetComponentInParent<EnemyController>();
            if (e == null || seen.Contains(e)) continue;
            seen.Add(e);
            Vector3 p = e.transform.position;
            if (lastX.TryGetValue(e, out float lx))
            {
                float dx = p.x - lx;
                if (Mathf.Abs(dx) > 0.0001f) { p.x = lx + dx * slow; e.transform.position = p; SlowedFrames++; }
            }
            lastX[e] = p.x;
        }
        // 出て行った敵は忘れる
        var gone = new List<EnemyController>();
        foreach (var k in lastX.Keys) if (k == null || !seen.Contains(k)) gone.Add(k);
        foreach (var k in gone) lastX.Remove(k);
    }
}

// 巫女の御札が貼り付いた敵。markDelay後に浄化爆発。貼られている間に2枚目の御札が当たると即、大きく爆発。
public class KitSealMark : MonoBehaviour
{
    static readonly List<KitSealMark> active = new List<KitSealMark>();
    public static int ActiveCount { get { active.RemoveAll(m => m == null); return active.Count; } }
    public static int Detonations;

    float timer;
    KitBlast.Spec normal, big;
    SpriteRenderer tag;
    bool done;

    public static KitSealMark Attach(GameObject target, float delay, KitBlast.Spec normal, KitBlast.Spec big)
    {
        var m = target.GetComponent<KitSealMark>();
        if (m != null) { m.Detonate(true); return null; }
        m = target.AddComponent<KitSealMark>();
        m.timer = delay; m.normal = normal; m.big = big;
        var go = new GameObject("SealTag");
        go.transform.SetParent(target.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        m.tag = go.AddComponent<SpriteRenderer>();
        m.tag.sprite = KitArt.OfudaSprite();
        m.tag.sortingOrder = RenderOrder.SlashFx;
        // 親(敵)の拡大率に左右されず、いつも同じ大きさで見えるように
        Vector3 ls = target.transform.lossyScale;
        go.transform.localScale = new Vector3(0.32f / Mathf.Max(0.01f, Mathf.Abs(ls.x)), 0.6f / Mathf.Max(0.01f, Mathf.Abs(ls.y)), 1f);
        active.Add(m);
        return m;
    }

    public static void ClearAll()
    {
        // Remove()がactiveから自分を外すので、写しを回す
        foreach (var m in active.ToArray()) if (m != null) m.Remove();
        active.Clear();
    }

    void Update()
    {
        if (done) return;
        timer -= Time.deltaTime;
        if (tag != null) tag.color = new Color(1f, 1f, 1f, timer < 0.4f ? (Mathf.Sin(Time.time * 40f) > 0f ? 1f : 0.3f) : 1f);
        if (timer <= 0f) Detonate(false);
    }

    public void Detonate(bool second)
    {
        if (done) return;
        done = true;
        Detonations++;
        Vector3 c = transform.position + new Vector3(0f, 0.6f, 0f);
        KitBlast.Create(c, second ? big : normal);
        if (KitProjectile.Ring != null)
            OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, c, new Color(1f, 0.9f, 0.55f, 0.95f), duration: 0.3f, startScale: 0.3f, endScale: (second ? big : normal).radius * 2.3f, sortingOrder: RenderOrder.SlashFx);
        Remove();
    }

    void Remove()
    {
        done = true;
        if (tag != null) Destroy(tag.gameObject);
        active.Remove(this);
        Destroy(this);
    }
}

public static class KitArt
{
    static Sprite ofuda, gradient, bird, bat, white;

    // 1unit四方の白(ゲージの棒など、拡大率で大きさと色を決める)。
    public static Sprite WhiteSprite()
    {
        if (white != null) return white;
        white = Make(4, 4, (x, y) => new Color32(255, 255, 255, 255), 4);
        return white;
    }

    static Sprite Make(int w, int h, System.Func<int, int, Color32> f, float ppu)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
    }

    // 御札: 白い紙に赤い帯と文字風の線、金の縁。1unit四方の枠に縦長で収まる(拡大率で大きさを決める)。
    public static Sprite OfudaSprite()
    {
        if (ofuda != null) return ofuda;
        ofuda = Make(32, 64, (x, y) =>
        {
            bool border = x < 2 || x > 29 || y < 2 || y > 61;
            if (border) return new Color32(210, 170, 70, 255);
            bool band = y > 52 && y < 58;
            bool glyph = (x > 13 && x < 18 && y > 12 && y < 48) || (y > 26 && y < 30 && x > 8 && x < 24) || (y > 38 && y < 41 && x > 10 && x < 22);
            if (band || glyph) return new Color32(200, 30, 40, 255);
            return new Color32(248, 244, 230, 255);
        }, 64);
        return ofuda;
    }

    // 下が濃く上へ消える縦のグラデーション(結界の光の壁/炎の照り返し)。
    public static Sprite GradientSprite()
    {
        if (gradient != null) return gradient;
        // 32x32px@PPU32 = 1unit四方(拡大率がそのまま幅・高さになる)
        gradient = Make(32, 32, (x, y) =>
        {
            float t = y / 31f;
            byte a = (byte)(Mathf.Clamp01(1f - t) * Mathf.Clamp01(1f - t) * 255f);
            return new Color32(255, 255, 255, a);
        }, 32);
        return gradient;
    }

    // 式神: 白い紙の鳥(右向き)。
    public static Sprite PaperBirdSprite()
    {
        if (bird != null) return bird;
        bird = Make(64, 48, (x, y) =>
        {
            float u = x / 63f, v = y / 47f;
            bool body = Mathf.Abs(v - 0.45f) < 0.12f * (1f - Mathf.Abs(u - 0.55f) * 1.4f) && u > 0.15f && u < 0.95f;
            bool wing = v > 0.45f && v < 0.45f + (0.5f * (1f - Mathf.Abs(u - 0.45f) * 2.2f)) && u > 0.2f && u < 0.7f;
            bool head = (u - 0.9f) * (u - 0.9f) + (v - 0.5f) * (v - 0.5f) < 0.004f;
            if (body || wing || head)
            {
                bool edge = wing && v > 0.45f + (0.46f * (1f - Mathf.Abs(u - 0.45f) * 2.2f));
                return edge ? new Color32(200, 40, 50, 255) : new Color32(250, 246, 235, 255);
            }
            return new Color32(0, 0, 0, 0);
        }, 64);
        return bird;
    }

    // コウモリ(右向き): 黒い体に深紅の縁取りの翼。
    public static Sprite BatSprite()
    {
        if (bat != null) return bat;
        bat = Make(64, 40, (x, y) =>
        {
            float u = (x - 32f) / 32f, v = (y - 20f) / 20f;
            float au = Mathf.Abs(u);
            float wingTop = 0.55f - au * 0.3f + 0.25f * Mathf.Abs(Mathf.Sin(au * 9f));
            bool wing = v < wingTop && v > -0.1f - (1f - au) * 0.5f && au < 0.98f;
            bool body = au < 0.16f && v > -0.7f && v < 0.6f;
            bool ear = au < 0.14f && au > 0.05f && v >= 0.6f && v < 0.85f;
            if (body || ear) return new Color32(25, 10, 18, 255);
            if (wing) return (v > wingTop - 0.12f || v < -0.1f - (1f - au) * 0.5f + 0.1f) ? new Color32(150, 20, 35, 255) : new Color32(35, 12, 22, 255);
            return new Color32(0, 0, 0, 0);
        }, 64);
        return bat;
    }
}

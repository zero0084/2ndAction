using System.Collections.Generic;
using UnityEngine;

// Enemy FINISH System(2026-10-06): 雑魚を倒した瞬間の「ぶっ飛ばした」演出(見た目だけ)。
//
//  ゲームの処理と見た目の分離:
//   ・HP 0 の瞬間、敵本体(EnemyController)は死亡確定 → その場で非表示(SetActive(false))→ 報酬(EXP/MILE/BONUS/カード)確定。
//     当たり判定/AI/攻撃/接触ダメージ/狙いの対象/敵の数は、その瞬間から消える(本体がもう居ない)。
//   ・飛んでいく敵は、ここが持つ「絵だけの分身」(プールした SpriteRenderer。当たり判定もスクリプトも無い)。
//     途中で消えても(シーンの読み直し/GAME OVER/カードの選択)ゲームには何も影響しない。
//  動き: カメラ基準(画面に対する相対位置)で動かす → 走行速度(100〜300km/h 以上)や FloatingOrigin に影響されない。
//        時間は Time.deltaTime(HitStop/停止/カード選択の間は止まる)。物理(Rigidbody)は使わない。
//  プール: 体/残像/粒/光は最初に作った SpriteRenderer を使い回す(Instantiate/Destroy しない)。足りない時は粒を減らす。
[DefaultExecutionOrder(1000)] // カメラが動いた後に置く
public class FinishFx : MonoBehaviour
{
    public static FinishFx Instance { get; private set; }
    public static FinishTuning T => FinishTuning.I;

    // 確認用(自動テスト)
    public static int Plays { get; private set; }
    public static int Bursts { get; private set; }
    public static int PoolExhausted { get; private set; }
    public static float LastStopRequested { get; private set; }
    public int ActiveSprites => used;
    public int ActiveBodies { get { int n = 0; foreach (var b in bodies) if (b.active) n++; return n; } }
    public int PoolSize => pool != null ? pool.Length : 0;
    public static int StreakCount { get; private set; }
    public static System.Action<string> QaEvent; // "burst x,y,type" など(自動テストの記録)

    SpriteRenderer[] pool;
    readonly Stack<int> free = new Stack<int>();
    int used;
    Material glowMat, spriteMat;
    static Texture2D glowTex, starTex, star8Tex, ringTex, sqTex;
    static Sprite glowSp, starSp, star8Sp, ringSp, sqSp;

    class Body
    {
        public bool active; public int sr = -1, flash = -1;
        public Vector2 o, v; public float age, rot, spin, scale0X, scale0Y, gravity, dur, ghostT, mul, shrink;
        public FinishInfo fi; public int phase; public float slamTargetY, slamT; public Color tint, burstCol;
        public Sprite sprite; public bool flipX; public int seq; public bool topple;
    }
    struct Part
    {
        public bool active; public int sr; public Vector2 o, v; public float age, life, size0, size1, sy, drag, grav, a0, rot, spin; public Color c; public bool twinkle, glow;
    }
    readonly List<Body> bodies = new List<Body>();
    Part[] parts = new Part[0];

    public static FinishFx Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("[FinishFx]");
        Instance = go.AddComponent<FinishFx>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        int n = Mathf.Clamp(T.poolSize, 64, 2000);
        pool = new SpriteRenderer[n];
        parts = new Part[n];
        glowMat = Resources.Load<Material>("Effects/SpriteGlow");
        var tmp = new GameObject("tmp").AddComponent<SpriteRenderer>(); spriteMat = tmp.sharedMaterial; Destroy(tmp.gameObject);
        MakeTextures();
        for (int i = n - 1; i >= 0; i--)
        {
            var go = new GameObject("fx");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            go.SetActive(false);
            pool[i] = sr;
            free.Push(i);
        }
        gameObject.AddComponent<FloatingOriginExempt>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    int Alloc()
    {
        if (free.Count == 0) { PoolExhausted++; return -1; }
        int i = free.Pop(); used++;
        pool[i].gameObject.SetActive(true);
        return i;
    }
    void Release(int i)
    {
        if (i < 0) return;
        pool[i].gameObject.SetActive(false);
        free.Push(i); used--;
    }

    // GAME OVER / シーンの切り替え / リトライ: 残っている見た目を全部プールへ返す(ゲームの処理は何も待たない)
    public static void ReleaseAll()
    {
        var f = Instance;
        if (f == null) return;
        foreach (var b in f.bodies) if (b.active) { b.active = false; f.Release(b.sr); f.Release(b.flash); b.sr = b.flash = -1; }
        for (int i = 0; i < f.parts.Length; i++) if (f.parts[i].active) { f.parts[i].active = false; f.Release(f.parts[i].sr); }
    }

    // ===================================================================== 入口
    // 敵本体が死亡確定した瞬間に呼ぶ(本体は直後に非表示になる)。見た目の分身を作って飛ばす。
    public static void Play(Vector3 bodyPos, Sprite sprite, bool flipX, Vector3 lossyScale, Color bodyColor, Color burstColor, Vector3 contact, FinishInfo fi)
    {
        var f = Ensure();
        f.PlayInternal(bodyPos, sprite, flipX, lossyScale, bodyColor, burstColor, contact, fi);
    }

    static int sameFrameIndex, sameFrame = -1;
    readonly Queue<float> killTimes = new Queue<float>();

    void PlayInternal(Vector3 bodyPos, Sprite sprite, bool flipX, Vector3 lossyScale, Color bodyColor, Color burstColor, Vector3 contact, FinishInfo fi)
    {
        var cam = Camera.main;
        if (cam == null || sprite == null) return;
        Plays++;
        var t = T;
        // 連続撃破
        float now = Time.unscaledTime;
        killTimes.Enqueue(now);
        while (killTimes.Count > 0 && now - killTimes.Peek() > Mathf.Max(0.1f, t.streakWindow)) killTimes.Dequeue();
        StreakCount = killTimes.Count;
        if (Time.frameCount != sameFrame) { sameFrame = Time.frameCount; sameFrameIndex = 0; } else sameFrameIndex++;
        int seq = sameFrameIndex;

        float mul = fi.type == FinishType.Overkill ? t.overkillMultiplier : fi.type == FinishType.Heavy ? t.heavyFinishMultiplier : 1f;
        Vector2 camP = cam.transform.position;
        var b = new Body
        {
            active = true, o = (Vector2)bodyPos - camP, fi = fi, mul = mul, sprite = sprite, flipX = flipX,
            scale0X = lossyScale.x, scale0Y = Mathf.Abs(lossyScale.y), tint = bodyColor, burstCol = burstColor, seq = seq,
        };
        float mass = Mathf.Max(0.3f, fi.mass);
        float dirX = fi.dir < 0 ? -1f : 1f;
        float speed = t.finishKnockback * mul / Mathf.Sqrt(mass) * (1f - 0.07f * (seq % 4)) * Random.Range(0.94f, 1.06f);
        float scatter = (seq == 0 ? 0f : ((seq % 2 == 0) ? 1f : -1f) * Mathf.Min(3, (seq + 1) / 2) * t.slamScatter * 0.5f) + Random.Range(-4f, 4f);
        b.gravity = fi.flying ? 0f : t.finishGravity;
        b.dur = t.finishDuration * (fi.type == FinishType.Overkill ? 1.15f : 1f);
        b.shrink = t.finishShrink;
        b.spin = t.finishRotation / mass * dirX * -1f * Random.Range(0.8f, 1.2f);
        b.topple = mass >= t.massTopple;
        switch (fi.shape)
        {
            case FinishShape.Up:
                {
                    float a = (90f - 12f * dirX + scatter * 0.5f) * Mathf.Deg2Rad;
                    b.v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * t.upSpeedMultiplier;
                    b.gravity = 0f; b.shrink = t.upShrink; b.spin *= 0.6f;
                    break;
                }
            case FinishShape.Aerial:
                {
                    float a = (-12f + scatter) * Mathf.Deg2Rad;
                    b.v = new Vector2(Mathf.Cos(a) * dirX, Mathf.Sin(a)) * speed;
                    b.gravity *= 0.5f;
                    break;
                }
            case FinishShape.Slam:
                {
                    // まず地面へ叩きつける → 衝撃 → 跳ね返って斜め上へ
                    b.phase = 1;
                    float gy = bodyPos.y - 0.6f;
                    var tm = TerrainManager.Instance;
                    float? h = tm != null ? tm.GetHeightAt(bodyPos.x) : null;
                    if (h.HasValue) gy = Mathf.Max(h.Value + Mathf.Abs(lossyScale.y) * 0.25f, bodyPos.y - 6f);
                    b.slamTargetY = gy - camP.y;
                    b.v = new Vector2(dirX, Mathf.Tan((t.slamBounceAngle + scatter) * Mathf.Deg2Rad)).normalized * speed * t.slamBounceSpeedMultiplier;
                    break;
                }
            default:
                {
                    float up = t.finishVerticalForce + scatter * 0.01f;
                    b.v = new Vector2(dirX, up).normalized * speed;
                    break;
                }
        }
        if (b.topple)
        {
            // 巨大な敵: 短く大きく後退して倒れ、その場で大きく弾ける
            b.v = new Vector2(dirX * 5f, 2f); b.gravity = t.finishGravity * 0.6f; b.dur = 0.45f; b.spin = -dirX * 220f; b.phase = 0;
        }
        b.sr = Alloc();
        if (b.sr < 0) return;
        var sr = pool[b.sr];
        sr.sprite = sprite; sr.flipX = flipX; sr.color = bodyColor; sr.sharedMaterial = spriteMat; sr.sortingOrder = RenderOrder.Enemy;
        // 命中の白い光(体の形のまま、ほんの一瞬)
        b.flash = glowMat != null ? Alloc() : -1;
        if (b.flash >= 0) { var fsr = pool[b.flash]; fsr.sprite = sprite; fsr.flipX = flipX; fsr.sharedMaterial = glowMat; fsr.color = new Color(1f, 1f, 1f, 0.9f); fsr.sortingOrder = RenderOrder.CombatFx; }
        bodies.Add(b);
        PlaceBody(b, camP);

        // 最後の一撃の光(当たった所)
        Vector2 co = (Vector2)contact - camP;
        float hs = 0.9f * mul;
        Spawn(glowSp, co, Vector2.zero, 0.12f, hs * 0.8f, hs * 1.6f, new Color(1f, 0.97f, 0.85f, 0.95f), true);
        Spawn(star8Sp, co, Vector2.zero, 0.16f, hs * 0.5f, hs * 1.3f, Color.Lerp(burstColor, Color.white, 0.5f), true, rot: Random.Range(0f, 45f));

        // HitStop(最大値だけ)/ 揺れ(上限あり)
        if (!fi.noStop)
            RequestStop(fi.shape == FinishShape.Slam ? t.slamHitStop : fi.type == FinishType.Overkill ? t.overkillHitStop : fi.type == FinishType.Heavy ? t.heavyHitStop : t.killHitStop);
        RequestShake(fi.shape == FinishShape.Slam ? t.shakeSlam : fi.type == FinishType.Overkill ? t.shakeOverkill : fi.type == FinishType.Heavy ? t.shakeHeavy : t.shakeNormal);
    }

    // ===================================================================== 公開の光(BOSS FINISH など。ワールド座標で指定、以後はカメラ基準で動く)
    static Vector2 Rel(Vector3 w) { var c = Camera.main; return c != null ? (Vector2)w - (Vector2)c.transform.position : (Vector2)w; }
    public static void Flash(Vector3 w, float size, Color c, float life = 0.2f) => Ensure().Spawn(glowSp, Rel(w), Vector2.zero, life, size * 0.5f, size, c, true);
    public static void Star(Vector3 w, float size, Color c, float life = 0.3f) => Ensure().Spawn(star8Sp, Rel(w), Vector2.zero, life, size * 0.4f, size, c, true, rot: Random.Range(0f, 45f), spin: 90f);
    public static void Twinkle(Vector3 w, float size, Color c, float life = 0.5f) => Ensure().Spawn(starSp, Rel(w), Vector2.zero, life, size * 0.2f, size, c, true, spin: 40f);
    public static void Ring(Vector3 w, float size, Color c, float life = 0.3f, float sy = 1f) => Ensure().Spawn(ringSp, Rel(w), Vector2.zero, life, size * 0.3f, size, c, true, sy: sy);
    public static void Spark(Vector3 w, Vector2 v, float size, float life, Color c, float drag = 2f, float grav = 0f, bool twinkle = false)
        => Ensure().Spawn(glowSp, Rel(w), v, life, size, size * 0.15f, c, true, drag: drag, grav: grav, twinkle: twinkle);
    public static void Debris(Vector3 w, Vector2 v, float size, float life, Color c, float grav = 20f)
        => Ensure().Spawn(sqSp, Rel(w), v, life, size, size * 0.6f, c, false, rot: Random.Range(0f, 90f), spin: Random.Range(-540f, 540f), grav: grav);
    public static void Pillar(Vector3 w, float width, float height, Color c, float life = 0.35f)
        => Ensure().Spawn(glowSp, Rel(w), Vector2.zero, life, width, width * 0.6f, c, true, sy: height / Mathf.Max(0.05f, width));
    public static void Cloud(Vector3 w, Vector2 v, float size, float life, Color c)
        => Ensure().Spawn(glowSp, Rel(w), v, life, size * 0.6f, size, c, false, drag: 1.5f);
    public static void BurstAt(Vector3 w, float size, Color c, int amount, bool firework) => Ensure().BurstCore(Rel(w), size, c, amount, firework);
    public static void StopFor(float seconds) => Ensure().RequestStop(seconds);
    public static void ShakeCapped(float magnitude) => Ensure().RequestShake(magnitude);

    // ===================================================================== HitStop / 揺れ
    float stopEnd;
    void RequestStop(float dur)
    {
        if (dur <= 0f) return;
        float now = Time.realtimeSinceStartup;
        LastStopRequested = dur;
        if (now + dur <= stopEnd + 0.001f) return; // もう止まっている(長い方だけを使う)
        float add = now + dur - Mathf.Max(now, stopEnd);
        stopEnd = now + dur;
        StartCoroutine(HitStop.Freeze(add));
    }
    float shakeUntil, shakeMag;
    void RequestShake(float mag)
    {
        mag = Mathf.Min(mag, T.shakeCap);
        if (mag <= 0f) return;
        if (Time.unscaledTime < shakeUntil && mag <= shakeMag) return;
        shakeMag = mag; shakeUntil = Time.unscaledTime + 0.16f;
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(mag, 0.16f);
    }

    // ===================================================================== 毎フレーム
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector2 camP = cam.transform.position;
        float dt = Time.deltaTime;
        var t = T;
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (!b.active) { bodies.RemoveAt(i); continue; }
            if (dt > 0f) StepBody(b, dt, cam, camP);
            if (b.active) PlaceBody(b, camP);
        }
        for (int i = 0; i < parts.Length; i++)
        {
            if (!parts[i].active) continue;
            ref Part p = ref parts[i];
            if (dt > 0f)
            {
                p.age += dt;
                if (p.age >= p.life) { p.active = false; Release(p.sr); continue; }
                p.v *= Mathf.Max(0f, 1f - p.drag * dt);
                p.v.y -= p.grav * dt;
                p.o += p.v * dt;
                p.rot += p.spin * dt;
            }
            float k = p.age / p.life;
            float size = Mathf.Lerp(p.size0, p.size1, 1f - (1f - k) * (1f - k));
            float a = p.a0 * (1f - k * k);
            if (p.twinkle) a *= 0.55f + 0.45f * Mathf.Sin((p.age * 38f) + p.rot);
            var sr = pool[p.sr];
            sr.transform.position = new Vector3(camP.x + p.o.x, camP.y + p.o.y, -0.2f);
            sr.transform.localScale = new Vector3(size, size * p.sy, 1f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, p.rot);
            var c = p.c; c.a *= Mathf.Clamp01(a); sr.color = c;
        }
    }

    void PlaceBody(Body b, Vector2 camP)
    {
        var sr = pool[b.sr];
        float k = Mathf.Clamp01(b.age / Mathf.Max(0.05f, b.dur));
        float s = 1f - b.shrink * k;
        sr.transform.position = new Vector3(camP.x + b.o.x, camP.y + b.o.y, -0.1f);
        sr.transform.localScale = new Vector3(b.scale0X * s, b.scale0Y * s, 1f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, b.rot);
        if (b.flash >= 0)
        {
            var f = pool[b.flash];
            f.transform.position = sr.transform.position + new Vector3(0, 0, -0.01f);
            f.transform.localScale = sr.transform.localScale; f.transform.rotation = sr.transform.rotation;
        }
    }

    void StepBody(Body b, float dt, Camera cam, Vector2 camP)
    {
        var t = T;
        b.age += dt;
        if (b.flash >= 0 && b.age > 0.09f) { Release(b.flash); b.flash = -1; }
        if (b.phase == 1)
        {
            // 叩きつけ: 地面まで一気に
            b.slamT += dt;
            float k = Mathf.Clamp01(b.slamT / Mathf.Max(0.01f, t.slamDownSeconds));
            float y0 = b.o.y;
            b.o.y = Mathf.Lerp(b.o.y, b.slamTargetY, k);
            if (k >= 1f)
            {
                b.phase = 2; b.age = 0f;
                SlamImpact(new Vector2(b.o.x, b.slamTargetY - Mathf.Abs(b.scale0Y) * 0.3f), b);
            }
            return;
        }
        b.v.y -= b.gravity * dt;
        b.o += b.v * dt;
        b.rot += b.spin * dt;
        // 残像(画面に対する位置に残る = 走行速度で伸びない)
        b.ghostT -= dt;
        if (b.ghostT <= 0f && Budget > 0.2f && b.age < t.trailDuration * (b.fi.type == FinishType.Overkill ? 1.6f : b.fi.type == FinishType.Heavy ? 1.25f : 1f) + 0.05f)
        {
            b.ghostT = t.ghostInterval;
            int gi = Alloc();
            if (gi >= 0)
            {
                var g = pool[gi]; g.sprite = b.sprite; g.flipX = b.flipX; g.sortingOrder = RenderOrder.Enemy;
                g.sharedMaterial = glowMat != null ? glowMat : spriteMat;
                Color gc = b.fi.type == FinishType.Overkill ? new Color(1f, 0.75f, 0.3f) : b.fi.shape == FinishShape.Up ? new Color(0.75f, 0.9f, 1f) : Color.Lerp(b.burstCol, Color.white, 0.55f);
                float k = Mathf.Clamp01(b.age / Mathf.Max(0.05f, b.dur));
                float s = 1f - b.shrink * k;
                AddPart(gi, b.o, Vector2.zero, t.trailDuration * 0.8f, 1f, 1f, gc, t.ghostAlpha * (b.fi.type == FinishType.Overkill ? 1.3f : 1f), b.rot, 0f, sx: b.scale0X * s, sy: b.scale0Y * s);
            }
            if (b.fi.type != FinishType.Normal && Budget > 0.35f)
            {
                // HEAVY/OVERKILL: 太めの光の筋
                float w = b.fi.type == FinishType.Overkill ? 1.3f : 0.9f;
                Spawn(glowSp, b.o, -b.v * 0.05f, 0.22f, w, w * 0.6f, b.fi.type == FinishType.Overkill ? new Color(1f, 0.8f, 0.4f, 0.6f) : new Color(1f, 1f, 1f, 0.45f), true);
            }
        }
        // 画面の端 / 時間切れ → 弾ける
        Vector3 vp = cam.WorldToViewportPoint(new Vector3(camP.x + b.o.x, camP.y + b.o.y, 0f));
        float m = t.edgeMargin;
        bool atEdge = vp.x < m || vp.x > 1f - m || vp.y > 1f - m || vp.y < m * 0.5f;
        if (atEdge || b.age >= b.dur)
        {
            Vector3 clamped = new Vector3(Mathf.Clamp(vp.x, m, 1f - m), Mathf.Clamp(vp.y, m, 1f - m), vp.z);
            Vector3 w = cam.ViewportToWorldPoint(clamped);
            Vector2 at = new Vector2(w.x - camP.x, w.y - camP.y);
            if (b.topple) at = b.o;
            Burst(at, b);
            b.active = false; Release(b.sr); Release(b.flash); b.sr = b.flash = -1;
        }
    }

    // ===================================================================== 光
    // プールの空き(0〜1)。少ない時は粒/残像を減らす(画面が埋まらないように/Android の負荷)
    float Budget => pool == null || pool.Length == 0 ? 1f : (float)free.Count / pool.Length;

    float StreakMul()
    {
        float e = T.streakEffectMultiplier;
        return StreakCount >= 5 ? 1f + e : StreakCount >= 3 ? 1f + e * 0.5f : 1f;
    }

    void Burst(Vector2 at, Body b)
    {
        Bursts++;
        var t = T;
        float sm = StreakMul();
        float size = t.burstSize * Mathf.Sqrt(b.mul) * (b.topple ? 1.4f : 1f) * Mathf.Lerp(1f, sm, 0.6f);
        int amount = Mathf.RoundToInt(t.particleAmount * b.mul * sm * Mathf.Clamp(Budget * 2.2f, 0.25f, 1f));
        Color col = b.fi.type == FinishType.Overkill ? new Color(1f, 0.78f, 0.3f) : Color.Lerp(b.burstCol, Color.white, 0.25f);
        QaEvent?.Invoke($"burst {b.fi.type} {b.fi.shape} at {at.x:F1},{at.y:F1}");
        if (b.fi.shape == FinishShape.Up && !b.topple)
        {
            // 星になる: 小さな「キラーン」(4方向の光 + 小さな光粒)
            Spawn(starSp, at, Vector2.zero, 0.55f, size * 0.2f, size * 1.15f, new Color(1f, 0.97f, 0.8f, 1f), true, rot: 0f, spin: 40f);
            Spawn(glowSp, at, Vector2.zero, 0.3f, size * 0.4f, size * 1.0f, new Color(1f, 0.95f, 0.7f, 0.8f), true);
            for (int i = 0; i < Mathf.Max(4, amount / 2); i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Spawn(glowSp, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(1.5f, 4f) * size, Random.Range(0.4f, 0.75f), 0.18f * size, 0.02f, new Color(1f, 0.95f, 0.75f, 0.9f), true, drag: 3f, twinkle: true);
            }
            return;
        }
        BurstCore(at, size, col, amount, b.fi.type == FinishType.Overkill || sm > 1.01f);
    }

    void BurstCore(Vector2 at, float size, Color col, int amount, bool firework)
    {
        // 中心の光 → 星形の衝撃 → 輪 → 円状に散る粒(花火のように少し残る)
        Spawn(glowSp, at, Vector2.zero, 0.2f, size * 0.9f, size * 2.0f, new Color(1f, 0.98f, 0.9f, 0.95f), true);
        Spawn(star8Sp, at, Vector2.zero, 0.26f, size * 0.5f, size * 1.5f, col, true, rot: Random.Range(0f, 45f), spin: 120f);
        Spawn(ringSp, at, Vector2.zero, 0.3f, size * 0.4f, size * 2.2f, new Color(col.r, col.g, col.b, 0.8f), true);
        for (int i = 0; i < amount; i++)
        {
            float a = (i + Random.value * 0.5f) / Mathf.Max(1, amount) * Mathf.PI * 2f;
            float sp = Random.Range(7f, 12f) * size * (firework ? 1.15f : 1f);
            Color c = i % 3 == 0 ? Color.white : col;
            Spawn(glowSp, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, Random.Range(0.3f, 0.5f), 0.22f * size, 0.04f, c, true, drag: 5f, grav: 2f);
        }
        if (firework && Budget > 0.3f)
        {
            // 小さな光粒が少し残る
            for (int i = 0; i < amount / 2; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Spawn(glowSp, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(2f, 6f) * size, Random.Range(0.55f, 0.9f), 0.12f * size, 0.03f, Color.Lerp(col, Color.white, 0.5f), true, drag: 3f, grav: 1.5f, twinkle: true);
            }
        }
    }

    void SlamImpact(Vector2 at, Body b)
    {
        var t = T;
        QaEvent?.Invoke("slam impact");
        float size = 1.2f * Mathf.Sqrt(b.mul);
        Spawn(glowSp, at, Vector2.zero, 0.18f, size * 1.2f, size * 2.6f, new Color(1f, 0.95f, 0.8f, 0.9f), true, sy: 0.45f);
        Spawn(ringSp, at, Vector2.zero, 0.3f, size * 0.6f, size * 3.4f, new Color(1f, 0.9f, 0.6f, 0.85f), true, sy: 0.3f);
        for (int i = 0; i < 10; i++)
        {
            float dir = i % 2 == 0 ? 1f : -1f;
            Spawn(glowSp, at + new Vector2(0f, 0.1f), new Vector2(dir * Random.Range(4f, 9f), Random.Range(1f, 4f)), Random.Range(0.25f, 0.4f), 0.35f, 0.05f, new Color(0.85f, 0.7f, 0.45f, 0.8f), false, drag: 4f, grav: 8f);
        }
        RequestShake(t.shakeSlam);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayStrongHit();
    }

    void Spawn(Sprite sp, Vector2 o, Vector2 v, float life, float size0, float size1, Color c, bool glow, float rot = 0f, float spin = 0f, float drag = 0f, float grav = 0f, bool twinkle = false, float sy = 1f)
    {
        int i = Alloc();
        if (i < 0) return;
        var sr = pool[i];
        sr.sprite = sp; sr.flipX = false; sr.sortingOrder = RenderOrder.CombatFx;
        sr.sharedMaterial = glow && glowMat != null ? glowMat : spriteMat;
        AddPart(i, o, v, life, size0, size1, c, c.a, rot, spin, drag, grav, twinkle, sy: sy);
    }

    void AddPart(int sri, Vector2 o, Vector2 v, float life, float size0, float size1, Color c, float a0, float rot, float spin, float drag = 0f, float grav = 0f, bool twinkle = false, float sx = 0f, float sy = 1f)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].active) continue;
            if (sx != 0f) { size0 = size1 = sx; sy = sy / sx; } // 残像(体の大きさ: 横 sx / 縦 sy)
            parts[i] = new Part { active = true, sr = sri, o = o, v = v, life = Mathf.Max(0.02f, life), size0 = size0, size1 = size1, sy = sy, c = new Color(c.r, c.g, c.b, 1f), a0 = a0, rot = rot, spin = spin, drag = drag, grav = grav, twinkle = twinkle };
            return;
        }
        Release(sri);
    }

    // ===================================================================== 絵(実行時に作る)
    static void MakeTextures()
    {
        if (glowTex != null) return;
        const int N = 64;
        glowTex = NewTex(N); starTex = NewTex(N); star8Tex = NewTex(N); ringTex = NewTex(N);
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float g = Mathf.Clamp01(1f - d); g *= g;
                glowTex.SetPixel(x, y, new Color(1, 1, 1, g));
                // 4方向の光(キラーン)
                float s4 = Mathf.Clamp01(1f - Mathf.Abs(dx) * 7f) * Mathf.Clamp01(1f - Mathf.Abs(dy)) + Mathf.Clamp01(1f - Mathf.Abs(dy) * 7f) * Mathf.Clamp01(1f - Mathf.Abs(dx));
                starTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(s4 + g * 0.8f)));
                // 8方向の星形
                float ang = Mathf.Atan2(dy, dx);
                float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 6f);
                float s8 = Mathf.Clamp01((0.35f + 0.65f * spikes) - d) * 2.2f;
                star8Tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(s8)));
                float r = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) * 9f);
                ringTex.SetPixel(x, y, new Color(1, 1, 1, r));
            }
        glowTex.Apply(); starTex.Apply(); star8Tex.Apply(); ringTex.Apply();
        sqTex = NewTex(4); for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) sqTex.SetPixel(x, y, Color.white); sqTex.Apply();
        sqSp = Sprite.Create(sqTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        glowSp = Sprite.Create(glowTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        starSp = Sprite.Create(starTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        star8Sp = Sprite.Create(star8Tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        ringSp = Sprite.Create(ringTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
    }
    static Texture2D NewTex(int n) => new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
}

public enum FinishType : byte { None = 0, Normal = 1, Heavy = 2, Overkill = 3 }
public enum FinishShape : byte { Side = 0, Aerial = 1, Up = 2, Slam = 3 }

// 最後の一撃の結果(撃破の種類/形/向き)。マルチでは HOST がこれを1つの数に詰めて OpDeath で送る(粒そのものは送らない)。
public struct FinishInfo
{
    public FinishType type; public FinishShape shape; public sbyte dir; public bool noStop, flying; public float mass;
    public ushort Pack() => (ushort)((byte)type | ((byte)shape << 2) | ((dir < 0 ? 1 : 0) << 4) | ((noStop ? 1 : 0) << 5) | ((flying ? 1 : 0) << 6) | (Mathf.Clamp(Mathf.RoundToInt(mass * 10f), 0, 255) << 8));
    public static FinishInfo Unpack(ushort v) => new FinishInfo
    {
        type = (FinishType)(v & 3), shape = (FinishShape)((v >> 2) & 3), dir = (sbyte)(((v >> 4) & 1) == 1 ? -1 : 1),
        noStop = ((v >> 5) & 1) == 1, flying = ((v >> 6) & 1) == 1, mass = Mathf.Max(0.3f, (v >> 8) / 10f),
    };
    public override string ToString() => $"{type}/{shape}/{(dir < 0 ? "L" : "R")}{(flying ? "/fly" : "")} m{mass:F1}";
}

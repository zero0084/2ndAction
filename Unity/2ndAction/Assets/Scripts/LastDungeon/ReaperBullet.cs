using System.Collections.Generic;
using UnityEngine;

// 死神三姉妹の飛び道具(2026-10-20、マスター依頼: 「避けるだけ」ではなく「攻撃で消す/弾き返す」ボス戦)。
//  3種類。見た目だけで分かるように、色・形・光り方を分ける:
//   Breakable   … 壊せる弾。姉妹の色の玉/刃(白い芯なし)。プレイヤーの攻撃が当たると砕ける
//   Reflectable … 返せる弾。白い芯 + 明るい輪郭 + きらめき。攻撃が当たると向きが変わり、撃った姉妹へ飛び返る
//                 (当たるとダメージ + 崩し(BREAK)。WildBossBase.TakeReflectedHit)
//   Hazard      … 返せない弾。黒いとげの形 + 点滅する赤い縁。攻撃では消えない(避ける)
//  色: 長女 = 赤黒 / 次女 = 紫黒 / 三女 = 青白(返せない弾はどの姉妹でも黒 + 赤い点滅)。
//  動き: 走りの座標系で動く(BossProjectile と同じ。走る速さで流れる)。地面を這う弾(hugGround)は地形の高さに沿う。
//  プレイヤーの攻撃との当たりは、物理の接触に加えて毎フレーム判定の範囲の重なりで確かめる(体と攻撃が同じ瞬間に触れた時は攻撃を優先)。
//  多段の攻撃でも、返す/砕くのは1回だけ(返した弾はもうプレイヤーの攻撃に反応しない)。
//  三姉妹の正式戦はシングル専用(マルチでは出ない)なので、通信の同期はしない。
public enum ReaperBulletKind { Breakable, Reflectable, Hazard }
public enum ReaperBulletShape { Orb, Crescent, Blade, Spike, Fissure }

public class ReaperBullet : MonoBehaviour
{
    public ReaperBulletKind kind;
    public ReaperBulletShape shape;
    public ReaperSister sister;
    public ReaperSisterBoss owner;
    public Vector2 velocity;          // 走りの座標系での速さ(m/s)
    public float lifetime = 4f;
    public bool hugGround;            // 地面に沿って進む(地割れ/衝撃波)
    public float groundOffset = 0.4f;
    public bool alignToVelocity = true;
    public bool reflected { get; private set; }
    public float armDelay;            // 出てから当たり判定が付くまで(発射口で光ってから飛ぶ)

    // 確認用の数
    public static int Spawned, BrokenCount, ReflectedCount, ReflectHits, PlayerHits, HazardIgnored;
    public static readonly Dictionary<ReaperBulletKind, int> SpawnedByKind = new Dictionary<ReaperBulletKind, int>();
    public static readonly List<ReaperBullet> Live = new List<ReaperBullet>();
    public static void ResetStats() { Spawned = BrokenCount = ReflectedCount = ReflectHits = PlayerHits = HazardIgnored = 0; SpawnedByKind.Clear(); }

    SpriteRenderer body, rim, core;
    BoxCollider2D col;
    float age, pulseSeed;
    ReaperSisterBoss target;
    Color bodyColor, rimColor;
    Vector2 size;

    // 見た目の大きさの倍率(2026-10-20: 100km/h の画面では 1m ≒ 24px。1m の弾は小さすぎて読めなかった)。当たりは見た目より小さめ(ColliderFrac)
    public static float VisualScale = 2.0f;
    public static float ArmDelay = 0.12f;

    public static ReaperBullet Spawn(ReaperSisterBoss owner, ReaperBulletKind kind, ReaperBulletShape shape, Vector3 pos, Vector2 size, Vector2 velocity, float lifetime)
    {
        size *= VisualScale;
        var go = new GameObject("ReaperBullet_" + kind);
        go.transform.position = pos;
        var b = go.AddComponent<ReaperBullet>();
        b.owner = owner;
        b.sister = owner != null ? owner.sister : ReaperSister.Eldest;
        b.kind = kind;
        b.shape = kind == ReaperBulletKind.Hazard && shape != ReaperBulletShape.Fissure ? ReaperBulletShape.Spike : shape;
        b.size = size;
        b.velocity = velocity;
        b.lifetime = lifetime;
        b.pulseSeed = Random.value * 10f;
        b.Build();
        Spawned++;
        SpawnedByKind[kind] = (SpawnedByKind.TryGetValue(kind, out int n) ? n : 0) + 1;
        b.armDelay = ArmDelay; b.col.enabled = false; // 出た瞬間はまだ当たらない(目の前に出ても反応できるように)
        return b;
    }

    void Build()
    {
        ReaperFx.Colors(sister, kind, out bodyColor, out rimColor);
        int order = RenderOrder.CombatFx + 2; // 背景/ボスの体/攻撃の光より手前(埋もれない)
        body = Layer("Body", ReaperFx.Fill(shape), bodyColor, order);
        rim = Layer("Rim", ReaperFx.Rim(shape), rimColor, order + 1);
        if (kind == ReaperBulletKind.Reflectable) core = Layer("Core", ReaperFx.Core(shape), Color.white, order + 2);
        transform.localScale = new Vector3(size.x, size.y, 1f);
        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        // 当たり(見た目の 1×1 に対する割合): 玉/とげ 0.56、三日月 0.62×0.5、刃 0.42×0.8、地割れ 0.85×0.7(下が地面)
        switch (shape)
        {
            case ReaperBulletShape.Crescent: col.size = new Vector2(0.62f, 0.5f); break;
            case ReaperBulletShape.Blade: col.size = new Vector2(0.42f, 0.8f); break;
            case ReaperBulletShape.Fissure: col.size = new Vector2(0.85f, 0.7f); col.offset = new Vector2(0f, 0.35f); break;
            default: col.size = new Vector2(0.56f, 0.56f); break;
        }
        col.enabled = armDelay <= 0f;
        Face();
        Live.Add(this);
    }

    // 確認用(見比べの並び): 姉妹を変えた後に色を付け直す
    void Recolor()
    {
        ReaperFx.Colors(sister, kind, out bodyColor, out rimColor);
        if (body != null) body.color = bodyColor;
        if (rim != null) rim.color = rimColor;
    }

    SpriteRenderer Layer(string name, Sprite s, Color c, int order)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = s; r.color = c; r.sortingOrder = order;
        return r;
    }

    void OnDestroy() { Live.Remove(this); }

    void Face()
    {
        if (!alignToVelocity || velocity.sqrMagnitude < 0.01f || shape == ReaperBulletShape.Fissure) return;
        float a = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        // 三日月は右向き、刃は下向きで描いてある
        if (shape == ReaperBulletShape.Blade) a += 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, a);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (!reflected && !col.enabled && age >= armDelay) col.enabled = true;
        if (reflected) Home(dt);
        Vector3 p = transform.position;
        p += (Vector3)((velocity + new Vector2(NetTargets.FrameSpeedNear(p), 0f)) * dt);
        if (hugGround && TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(p.x);
            if (!h.HasValue) { Destroy(gameObject); return; } // 穴の上では消える(浮いたまま進まない)
            p.y = h.Value + groundOffset;
        }
        transform.position = p;
        Face();
        Animate();
        if (!reflected && CheckPlayerAttacks()) return; // 出たばかり(まだプレイヤーに当たらない間)でも、攻撃では返せる/壊せる
        if (age > lifetime) { Destroy(gameObject); return; }
        // 地面より下へ入った(降ってきた刃/下向きの弾)は地面で砕ける
        if (!hugGround && !reflected && velocity.y < -0.5f && TerrainManager.Instance != null)
        {
            float? g = TerrainManager.Instance.GetHeightAt(p.x);
            if (g.HasValue && p.y < g.Value + 0.1f) { Burst(0.6f); Destroy(gameObject); }
        }
    }

    void Animate()
    {
        float t = Time.time * 1f + pulseSeed;
        switch (kind)
        {
            case ReaperBulletKind.Reflectable:
                // 芯が脈打つ + 輪郭が明るく光る(返せる合図)
                float k = 0.75f + 0.25f * Mathf.Sin(t * 12f);
                if (core != null) { core.color = new Color(1f, 1f, 1f, k); core.transform.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(t * 12f)); }
                rim.color = Color.Lerp(rimColor, Color.white, 0.35f + 0.25f * Mathf.Sin(t * 12f));
                if (Random.value < 0.08f && !reflected)
                    OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), transform.position + (Vector3)(Random.insideUnitCircle * size.x * 0.45f), Color.white, 0.18f, 0.25f, 0.05f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx + 4, 0.1f);
                break;
            case ReaperBulletKind.Hazard:
                // 赤い縁が速く点滅(危険の合図)。とげがゆっくり回る
                float blink = Mathf.PingPong(t * 7f, 1f);
                rim.color = Color.Lerp(new Color(0.45f, 0.02f, 0.06f, 1f), rimColor, blink);
                if (shape == ReaperBulletShape.Spike) body.transform.localRotation = rim.transform.localRotation = Quaternion.Euler(0f, 0f, age * 140f);
                break;
            default:
                rim.color = Color.Lerp(rimColor, bodyColor, 0.25f + 0.25f * Mathf.Sin(t * 5f));
                break;
        }
    }

    // ---------------------------------------------------------------- プレイヤーの攻撃
    bool hazardTouched;
    // 当たりの範囲(当たりを止めている間も同じ範囲で攻撃との重なりを見る。止めた Collider2D の bounds は空になる)
    Bounds MyBounds()
    {
        Vector3 sc = transform.lossyScale;
        Vector2 sz = new Vector2(col.size.x * Mathf.Abs(sc.x), col.size.y * Mathf.Abs(sc.y));
        float r = Mathf.Max(sz.x, sz.y); // 回っている弾もあるので、向きに関係なく長い辺の正方形で見る(少し甘め)
        Vector3 c = transform.TransformPoint(col.offset);
        return new Bounds(c, new Vector3(r, r, 1f));
    }

    bool CheckPlayerAttacks()
    {
        Bounds me = MyBounds();
        if (kind == ReaperBulletKind.Hazard)
        {
            // 返せない弾: 攻撃が触れても消えない(数えるだけ)
            if (!hazardTouched)
                foreach (var a in PlayerAttackInfo.Active)
                {
                    var c = a != null ? (a.col != null ? a.col : a.GetComponent<Collider2D>()) : null;
                    if (c != null && c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("PlayerAttack") && c.bounds.Intersects(me)) { hazardTouched = true; HazardIgnored++; break; }
                }
            return false;
        }
        foreach (var a in PlayerAttackInfo.Active)
        {
            if (a == null) continue;
            var c = a.col != null ? a.col : a.GetComponent<Collider2D>();
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || !c.CompareTag("PlayerAttack")) continue;
            if (!c.bounds.Intersects(me)) continue;
            OnPlayerAttack(c, a);
            return true;
        }
        return false;
    }

    void OnPlayerAttack(Collider2D attack, PlayerAttackInfo info)
    {
        if (reflected) return;
        if (kind == ReaperBulletKind.Breakable) { BrokenCount++; Burst(1f); Destroy(gameObject); return; }
        if (kind == ReaperBulletKind.Reflectable) Reflect(info);
    }

    void Reflect(PlayerAttackInfo info)
    {
        reflected = true;
        ReflectedCount++;
        target = PickTarget();
        float spd = Mathf.Max(18f, velocity.magnitude * 2f);
        Vector3 aim = target != null ? target.CenterWorld : transform.position + Vector3.right * 10f;
        velocity = ((Vector2)(aim - transform.position)).normalized * spd;
        lifetime = age + 2.5f;
        if (col != null) col.enabled = false; // 返した弾はプレイヤーにもう当たらない(ボスへの命中は Home で確かめる)
        // 返した合図: 白く光る + 火花 + 短い止め
        bodyColor = Color.Lerp(bodyColor, Color.white, 0.55f); body.color = bodyColor;
        rimColor = new Color(1f, 0.95f, 0.6f, 1f); rim.color = rimColor;
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), transform.position, new Color(1f, 0.95f, 0.7f, 1f), 10, 0.35f, 0.15f, 0.4f, 6f, 1.2f, RenderOrder.CombatFx + 4);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position, new Color(1f, 1f, 1f, 0.9f), 0.22f, size.x * 0.6f, size.x * 2.2f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx + 4, 0.05f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, transform.position);
        if (GameManager.Instance != null) HitStop.Begin(0.035f);
        Debug.Log($"[ReaperBullet] reflected ({sister} {shape}) by {(info != null ? info.kind.ToString() : "?")} -> {(target != null ? target.sister.ToString() : "none")}");
    }

    ReaperSisterBoss PickTarget()
    {
        if (owner != null && owner.isActiveAndEnabled && !owner.IsDead && !owner.Retreating) return owner;
        ReaperSisterBoss best = null; float bd = float.MaxValue;
        foreach (var s in ReaperSisterBoss.All)
        {
            if (s == null || s.IsDead || s.Retreating || !s.isActiveAndEnabled) continue;
            float d = (s.CenterWorld - transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    void Home(float dt)
    {
        if (target == null || target.IsDead || target.Retreating || !target.isActiveAndEnabled) target = PickTarget();
        if (target == null) return;
        Vector2 to = (Vector2)(target.CenterWorld - transform.position);
        float spd = velocity.magnitude;
        velocity = Vector2.Lerp(velocity.normalized, to.normalized, Mathf.Clamp01(dt * 10f)).normalized * spd;
        float hitR = Mathf.Max(0.9f, target.bodyHeight * 0.32f) + size.x * 0.4f;
        if (to.magnitude < hitR)
        {
            ReflectHits++;
            target.TakeReflectedHit(this);
            Burst(1.3f);
            Destroy(gameObject);
        }
    }

    void Burst(float scale)
    {
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), transform.position, Color.Lerp(rimColor, Color.white, 0.3f), Mathf.RoundToInt(8 * scale), 0.35f, 0.12f * scale, 0.32f * scale, 5f, 1.4f, RenderOrder.CombatFx + 3);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.HitProjectile, transform.position);
    }

    // ---------------------------------------------------------------- プレイヤーの体
    void OnTriggerEnter2D(Collider2D other)
    {
        if (reflected || !other.CompareTag("Player") || PlayerController.Instance == null) return;
        if (CheckPlayerAttacks()) return; // 同じ瞬間に攻撃も触れていれば攻撃を優先
        PlayerHits++;
        PlayerController.Instance.TakeDamage(source: "ReaperBullet:" + sister + "/" + kind, amount: BossManager.ScaleDamage(CombatScale.PlayerHit));
        Burst(0.8f);
        Destroy(gameObject);
    }

    // 戦いの始めに絵を作っておく(1回目の技で作ると、その瞬間だけ処理が重くなった)
    public static void Prewarm()
    {
        foreach (ReaperBulletShape sh in System.Enum.GetValues(typeof(ReaperBulletShape))) { ReaperFx.Fill(sh); ReaperFx.Rim(sh); ReaperFx.Core(sh); }
        ReaperFx.Disc(); BossFx.Ring(); BossFx.Fang(); BossFx.WarnZone(); BossFx.Block(); OneShotSpriteEffect.SoftDotSprite();
    }

    public static void ClearAll()
    {
        foreach (var b in Live.ToArray()) if (b != null) Destroy(b.gameObject);
        Live.Clear();
    }
}

// 走りの座標系に乗せる(予兆の印/地面の跡/噛みつき): プレイヤーの近くの走る速さで流す。高さは任意で地面に合わせる
public class ReaperFrameFollower : MonoBehaviour
{
    public static readonly List<ReaperFrameFollower> Live = new List<ReaperFrameFollower>();
    void OnEnable() { Live.Add(this); }
    void OnDisable() { Live.Remove(this); }
    public bool snapGround;
    public float groundOffset;
    public float life = 1f;
    public System.Action<float> onTick; // 経過の割合 0..1
    float age;
    void Update()
    {
        age += Time.deltaTime;
        Vector3 p = transform.position;
        p.x += NetTargets.FrameSpeedNear(p) * Time.deltaTime;
        if (snapGround && TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(p.x);
            if (h.HasValue) p.y = h.Value + groundOffset;
        }
        transform.position = p;
        onTick?.Invoke(Mathf.Clamp01(age / Mathf.Max(0.01f, life)));
        if (age >= life) Destroy(gameObject);
    }

    public static ReaperFrameFollower Make(string name, Sprite s, Color c, Vector3 pos, Vector2 scale, float life, int order, bool snapGround = false, float groundOffset = 0f)
    {
        var g = new GameObject(name);
        g.transform.position = pos;
        g.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = s; r.color = c; r.sortingOrder = order;
        var f = g.AddComponent<ReaperFrameFollower>();
        f.life = life; f.snapGround = snapGround; f.groundOffset = groundOffset;
        return f;
    }
}

// 三姉妹の飛び道具/予兆の仮の絵(手続きで作る。正式な絵ができたら差し替え。白で作り、色は SpriteRenderer で掛ける)
public static class ReaperFx
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // 形の「中からの距離」: 1 = 縁、<1 = 中
    static float Field(ReaperBulletShape s, float u, float v)
    {
        float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
        switch (s)
        {
            case ReaperBulletShape.Orb: return Mathf.Sqrt(dx * dx + dy * dy) / 0.92f;
            case ReaperBulletShape.Spike:
            {
                float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float star = 0.5f + 0.42f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4f)), 2.5f); // 8本のとげ
                return r / star;
            }
            case ReaperBulletShape.Blade: return Mathf.Abs(dx) / 0.34f + Mathf.Abs(dy) / 0.98f; // 細長いひし形(下向き)
            case ReaperBulletShape.Fissure:
            {
                // 地面から生える3本の黒い牙(下が地面)
                float cell = u * 3f; float tri = 1f - Mathf.Abs(cell - Mathf.Floor(cell) - 0.5f) * 2f;
                float top = 0.25f + 0.72f * tri;
                return Mathf.Max(v / Mathf.Max(0.05f, top), Mathf.Abs(dx) / 0.98f);
            }
            default: // Crescent: 右向きの三日月
            {
                float cx = dx + 0.55f, r = Mathf.Sqrt(cx * cx + dy * dy);
                if (cx < 0f) return 9f;
                float band = Mathf.Abs(r - 1.05f) / 0.32f;
                float taper = Mathf.Abs(dy) / 0.95f;
                return Mathf.Max(band, taper * 1.02f);
            }
        }
    }

    static Sprite Make(string key, ReaperBulletShape s, System.Func<float, float> alphaOfField)
    {
        if (cache.TryGetValue(key, out var sp) && sp != null) return sp;
        const int N = 96;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float a = Mathf.Clamp01(alphaOfField(Field(s, (x + 0.5f) / N, (y + 0.5f) / N)));
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, s == ReaperBulletShape.Fissure ? 0f : 0.5f), N);
        cache[key] = sp;
        return sp;
    }

    public static Sprite Fill(ReaperBulletShape s) => Make("fill_" + s, s, f => (1f - f) / 0.08f);
    public static Sprite Rim(ReaperBulletShape s) => Make("rim_" + s, s, f => f > 1f ? (1.0f - (f - 1f) / 0.06f) : (f - 0.74f) / 0.06f);
    public static Sprite Core(ReaperBulletShape s) => Make("core_" + s, s, f => (0.46f - f) / 0.12f);

    public static Color SisterBody(ReaperSister s) => s == ReaperSister.Eldest ? new Color(0.42f, 0.03f, 0.07f, 0.97f)
        : s == ReaperSister.Second ? new Color(0.27f, 0.06f, 0.42f, 0.97f) : new Color(0.26f, 0.48f, 0.95f, 0.97f);
    public static Color SisterRim(ReaperSister s) => s == ReaperSister.Eldest ? new Color(1f, 0.32f, 0.26f, 1f)
        : s == ReaperSister.Second ? new Color(0.86f, 0.5f, 1f, 1f) : new Color(0.82f, 0.94f, 1f, 1f);
    // 予兆の色(姉妹の色を明るく)
    public static Color SisterGlow(ReaperSister s) => Color.Lerp(SisterRim(s), Color.white, 0.2f);

    public static void Colors(ReaperSister s, ReaperBulletKind k, out Color body, out Color rim)
    {
        switch (k)
        {
            case ReaperBulletKind.Hazard: body = new Color(0.06f, 0.02f, 0.05f, 0.98f); rim = new Color(1f, 0.12f, 0.22f, 1f); break;
            case ReaperBulletKind.Reflectable: body = SisterBody(s); rim = Color.Lerp(SisterRim(s), Color.white, 0.55f); break;
            default: body = SisterBody(s); rim = SisterRim(s); break;
        }
    }

    static Sprite softDisc;
    // 地面の影/予兆の円(横長に潰して使う)
    public static Sprite Disc()
    {
        if (softDisc != null) return softDisc;
        softDisc = Make("disc", ReaperBulletShape.Orb, f => (1f - f) / 0.35f);
        return softDisc;
    }
}

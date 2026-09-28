using System.Collections;
using UnityEngine;

// ===== 新4人(2026-09-27)の飛び道具/範囲攻撃 ===== //
// 既存のPlayerBullet(拳銃士、1体に当たったら消える)は一切変えず、弓の貫通矢・魔法弾の
// 小爆発・手裏剣のために別クラスを用意する。ダメージ計算は既存どおり相手側(タグ
// "PlayerAttack"+PlayerAttackInfo)が行い、ここでは「何体まで貫くか」「命中/着弾で爆発するか」
// だけを扱う。判定ごとの威力/ノックバック/HitStopはPlayerAttackInfoの倍率に持たせる。
public class KitProjectile : MonoBehaviour
{
    public Vector2 velocity;
    public float lifetime = 1.2f;
    // 追加で何体まで貫くか(0=最初の1体で消える、-1=無制限)。
    public int pierce;
    // 走行速度を引き継ぐ(PlayerBulletと同じ。高速走行中も自分の弾を追い越さない)。
    public bool inheritRunSpeed = true;
    // 見た目の回転(度/秒、手裏剣)。
    public float spin;
    // 地面に当たったら消える(斜め下の矢/下向きの魔法)。
    public bool stopAtGround;
    // 命中/着弾/寿命で爆発する範囲攻撃(radius<=0なら爆発しない)。
    public KitBlast.Spec blast;
    public bool blastOnExpire;
    // ボス(EnemyController以外)に当たった時だけ自前でHitStop(雑魚はPlayerAttackInfo.hitStopで敵側が処理)。
    public float bossHitStop;
    public System.Action<KitProjectile, Vector3> onGround;
    // 敵/ボスに当たった瞬間(巫女の御札の貼り付け等)。
    public System.Action<KitProjectile, Collider2D> onHitEnemy;
    // 上下にゆらゆら揺れる(式神の紙の鳥)。振幅と周波数。
    public float wobbleAmp, wobbleFreq;

    public int HitCount { get; private set; }
    Transform visual;
    float age;
    bool done;

    void Update()
    {
        if (done) return;
        float dt = Time.deltaTime;
        Vector2 v = velocity + (inheritRunSpeed ? new Vector2(PlayerController.RunFrameSpeed, 0f) : Vector2.zero);
        transform.position += (Vector3)(v * dt);
        if (visual != null && spin != 0f) visual.Rotate(0f, 0f, spin * dt);
        if (wobbleAmp > 0f) transform.position += new Vector3(0f, Mathf.Cos(age * wobbleFreq * Mathf.PI * 2f) * wobbleAmp * wobbleFreq * Mathf.PI * 2f * dt, 0f);
        age += dt;
        if (stopAtGround && TerrainManager.Instance != null && velocity.y < 0f)
        {
            float? g = TerrainManager.Instance.GetHeightAt(transform.position.x);
            if (g.HasValue && transform.position.y <= g.Value + 0.05f)
            {
                Vector3 p = new Vector3(transform.position.x, g.Value + 0.05f, 0f);
                onGround?.Invoke(this, p);
                Finish(p, true);
                return;
            }
        }
        if (age > lifetime) Finish(transform.position, blastOnExpire);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (done) return;
        bool enemy = other.GetComponentInParent<EnemyController>() != null;
        bool boss = !enemy && (other.GetComponentInParent<WildBossBase>() != null || other.GetComponentInParent<DragonController>() != null || other.GetComponentInParent<MajinController>() != null);
        if (!enemy && !boss) return;
        HitCount++;
        onHitEnemy?.Invoke(this, other);
        if (boss && bossHitStop > 0f && PlayerController.Instance != null) PlayerController.Instance.StartCoroutine(HitStop.Freeze(bossHitStop));
        if (blast.radius > 0f) { Finish(other.ClosestPoint(transform.position), true); return; }
        if (pierce == 0) { Finish(transform.position, false); return; }
        if (pierce > 0) pierce--;
    }

    void Finish(Vector3 at, bool explode)
    {
        if (done) return;
        done = true;
        if (explode && blast.radius > 0f) KitBlast.Create(at, blast);
        Destroy(gameObject);
    }

    // sprite は右向きの絵(Resources/Effects/*、PPU512=1unit四方)。visualScaleで見た目の大きさ、
    // colliderSizeで判定の大きさ(回転は進行方向)。
    public static KitProjectile Create(Sprite sprite, Vector3 position, Vector2 velocity, float lifetime,
        Vector2 visualScale, Vector2 colliderSize, Color tint, PlayerAttackKind kind,
        float damageScale, float knockbackScale, float hitStop, Color? trailColor = null, bool rotateToVelocity = true)
    {
        var go = new GameObject("KitProjectile");
        go.tag = "PlayerAttack";
        go.transform.position = position;
        if (rotateToVelocity) go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);

        var vis = new GameObject("Visual");
        vis.transform.SetParent(go.transform, false);
        vis.transform.localScale = new Vector3(visualScale.x, visualScale.y, 1f);
        var sr = vis.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = tint;
        sr.sortingOrder = RenderOrder.CombatFx;

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = colliderSize;

        if (trailColor.HasValue)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.minVertexDistance = 0.03f;
            trail.startWidth = Mathf.Min(0.22f, colliderSize.y * 0.9f);
            trail.endWidth = 0.01f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.sortingOrder = RenderOrder.CombatFx - 1;
            trail.numCapVertices = 4;
            Color c = trailColor.Value;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
        }

        // 敵にはRigidbody2Dが無いので、弾側にKinematicを付けないとトリガーが発火しない(PlayerBulletと同じ)。
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var info = go.AddComponent<PlayerAttackInfo>();
        info.kind = kind;
        info.damageScale = damageScale;
        info.knockbackScale = knockbackScale;
        info.hitStop = hitStop;

        var p = go.AddComponent<KitProjectile>();
        p.velocity = velocity;
        p.lifetime = lifetime;
        p.visual = vis.transform;
        return p;
    }

    // 手裏剣の絵(実行時に1回だけ作る4枚刃の星)。
    static Sprite shurikenSprite;
    public static Sprite ShurikenSprite()
    {
        if (shurikenSprite != null) return shurikenSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx);
                // 4枚の刃: 角度に応じて許される半径が変わる星形
                float blade = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2f)), 6f);
                float limit = Mathf.Lerp(0.28f, 1f, blade);
                float edge = limit - r;
                byte alpha = (byte)(Mathf.Clamp01(edge * 12f) * 255f);
                bool hole = r < 0.12f;
                float shade = Mathf.Lerp(0.95f, 0.45f, r) * (1f - 0.25f * Mathf.Abs(Mathf.Sin(a * 2f)));
                px[y * n + x] = hole ? new Color32(0, 0, 0, 0) : new Color32((byte)(shade * 200), (byte)(shade * 210), (byte)(shade * 230), alpha);
            }
        tex.SetPixels32(px);
        tex.Apply();
        shurikenSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        shurikenSprite.name = "shuriken(runtime)";
        return shurikenSprite;
    }

    static Sprite LoadFx(string name) => Resources.Load<Sprite>("Effects/" + name);
    static Sprite arrowArt, orbArt, burstArt, boltArt, spearArt, slashArt, puffArt, ringArt;
    public static Sprite Arrow => arrowArt != null ? arrowArt : (arrowArt = LoadFx("arrow"));
    public static Sprite Orb => orbArt != null ? orbArt : (orbArt = LoadFx("orb"));
    public static Sprite Burst => burstArt != null ? burstArt : (burstArt = LoadFx("burst"));
    public static Sprite SkyBolt => boltArt != null ? boltArt : (boltArt = LoadFx("skybolt"));
    public static Sprite ThunderSpear => spearArt != null ? spearArt : (spearArt = LoadFx("thunderspear"));
    public static Sprite Slash => slashArt != null ? slashArt : (slashArt = LoadFx("slash"));
    public static Sprite Puff => puffArt != null ? puffArt : (puffArt = LoadFx("cloudpuff"));
    public static Sprite Ring => ringArt != null ? ringArt : (ringArt = LoadFx("ring"));
}

// 一瞬だけ出る円形の範囲判定(魔法の爆発、ダイブキックの着地、カウンター等)+見た目。
public class KitBlast : MonoBehaviour
{
    [System.Serializable]
    public struct Spec
    {
        public float radius;
        public float active;
        public PlayerAttackKind kind;
        public float damageScale, knockbackScale, hitStop;
        public Sprite fx;
        public Color tint;
        public float fxScale;
        public float shake;
    }

    public static KitBlast Create(Vector3 center, Spec s)
    {
        var go = new GameObject("KitBlast");
        go.tag = "PlayerAttack";
        go.transform.position = center;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = s.radius;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        var info = go.AddComponent<PlayerAttackInfo>();
        info.kind = s.kind;
        info.damageScale = s.damageScale <= 0f ? 1f : s.damageScale;
        info.knockbackScale = s.knockbackScale <= 0f ? 1f : s.knockbackScale;
        info.hitStop = s.hitStop;
        go.AddComponent<ColliderDebugView>().color = new Color(1f, 0.6f, 0.3f);
        var b = go.AddComponent<KitBlast>();
        b.StartCoroutine(b.Life(Mathf.Max(0.04f, s.active)));
        if (s.fx != null)
        {
            float size = s.radius * 2f * (s.fxScale > 0f ? s.fxScale : 1f);
            OneShotSpriteEffect.CreateTweened(s.fx, center, s.tint, duration: 0.26f, startScale: size * 0.45f, endScale: size, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.25f);
        }
        if (s.shake > 0f)
        {
            var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cam != null) cam.Shake(s.shake, 0.14f);
        }
        return b;
    }

    IEnumerator Life(float t)
    {
        yield return new WaitForSeconds(t);
        Destroy(gameObject);
    }
}

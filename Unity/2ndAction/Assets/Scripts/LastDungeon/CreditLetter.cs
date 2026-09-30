using UnityEngine;

// エンドロールの巨大文字1文字(2026-09-30)。「文字もゲーム世界に存在している」:
//  ・上面に乗れる(WorldPlatforms: 上から落ちてきた時だけ乗れる面。横からは通り抜けて、文字の手前を走る)
//  ・壁にもできる(THANK YOU FOR PLAYINGの石板、YES/NO)
//  ・BoxCollider2D(トリガー)がプレイヤーの攻撃判定を受ける: 揺れる/光る、壊せる文字は砕ける
//  ・宙に浮く文字はゆっくり上下する(乗っていれば一緒に動く)
// 見た目はResources/LastDungeon/Glyphsの1文字1枚の絵(無い文字は置かない)。
public class CreditLetter : MonoBehaviour, IWorldSolid
{
    public char Char { get; private set; }
    public float CapHeight { get; private set; }
    public bool platform = true;
    public bool wall;
    public bool attackable = true;
    public bool breakable;
    public int hp = 1;
    public bool Broken { get; private set; }
    public int CrackStage { get; private set; }
    public float bobAmplitude, bobSpeed = 0.7f;
    // 攻撃を受けた時の通知(YES/NO・石板はまとまりとしてHPを数える)。trueを返すと、この文字自身の揺れ/砕けは行わない。
    public System.Func<CreditLetter, Collider2D, bool> HitRouter;
    public static int TotalHits, TotalBroken;

    SpriteRenderer sr, glow, shadow;
    BoxCollider2D col;
    Vector3 basePos;
    float wobble, wobbleVel, flash, bobPhase, shakeT;
    Color baseColor = Color.white;
    readonly Collider2D[] hitCols = new Collider2D[6];
    readonly int[] hitSwings = new int[6];
    int hitNext;

    public SpriteRenderer Renderer => sr;

    public static CreditLetter Create(Transform parent, char c, Vector3 bottomCenter, float capHeight, int sortingOrder, bool withShadow)
    {
        var sprite = GlyphFont.Get(c);
        if (sprite == null) return null;
        var go = new GameObject("Letter_" + c);
        go.transform.SetParent(parent, false);
        go.transform.position = bottomCenter;
        var l = go.AddComponent<CreditLetter>();
        l.Char = c;
        l.CapHeight = capHeight;
        l.Build(sprite, sortingOrder, withShadow);
        return l;
    }

    void Build(Sprite sprite, int order, bool withShadow)
    {
        basePos = transform.position;
        bobPhase = basePos.x * 0.61f;
        var body = new GameObject("Glyph").transform;
        body.SetParent(transform, false);
        sr = body.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        body.localScale = Vector3.one * CapHeight;
        // ほのかな光(文字の後ろ)
        var g = new GameObject("Glow").transform;
        g.SetParent(transform, false);
        glow = g.gameObject.AddComponent<SpriteRenderer>();
        glow.sprite = OneShotSpriteEffect.SoftDotSprite();
        glow.sortingOrder = order - 1;
        float w = sprite.bounds.size.x * CapHeight;
        g.localScale = new Vector3(w * 2.2f, CapHeight * 2.4f, 1f);
        g.localPosition = new Vector3(0f, CapHeight * 0.62f, 0f);
        glow.color = new Color(1f, 0.85f, 0.45f, 0.16f);
        if (withShadow)
        {
            var s = new GameObject("Shadow").transform;
            s.SetParent(transform, false);
            shadow = s.gameObject.AddComponent<SpriteRenderer>();
            shadow.sprite = OneShotSpriteEffect.SoftDotSprite();
            shadow.sortingOrder = RenderOrder.EnvironmentFx;
            s.localScale = new Vector3(w * 1.3f, 0.35f, 1f);
            s.localPosition = new Vector3(0f, 0.04f, 0f);
            shadow.color = new Color(0f, 0f, 0f, 0.35f);
        }
        // 攻撃を受ける判定(見た目の文字の形に近い箱)
        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        float faceH = CapHeight * 1.05f;
        col.size = new Vector2(Mathf.Max(0.3f, w * 0.9f), faceH);
        col.offset = new Vector2(0f, CapHeight * GlyphFont.BaselineLift + faceH * 0.5f);
        WorldPlatforms.Register(this);
    }

    void OnDestroy() { WorldPlatforms.Unregister(this); }

    // ---- IWorldSolid ----
    public bool SolidActive => !Broken && isActiveAndEnabled && (platform || wall);
    public bool IsPlatform => platform;
    public bool IsWall => wall;
    public Rect SolidRect
    {
        get
        {
            Vector2 c = (Vector2)transform.position + col.offset;
            Vector2 h = col.size * 0.5f;
            return Rect.MinMaxRect(c.x - h.x, c.y - h.y, c.x + h.x, c.y + h.y);
        }
    }
    public Bounds VisualBounds => sr != null ? sr.bounds : new Bounds(transform.position, Vector3.zero);

    // ---- 攻撃 ----
    void OnTriggerEnter2D(Collider2D other)
    {
        if (Broken || !other.CompareTag("PlayerAttack")) return;
        var info = other.GetComponent<PlayerAttackInfo>();
        int swing = info != null ? info.SwingId : 0;
        for (int i = 0; i < hitCols.Length; i++) if (hitCols[i] == other && hitSwings[i] == swing) return; // この振りでは当たり済み
        hitCols[hitNext] = other; hitSwings[hitNext] = swing; hitNext = (hitNext + 1) % hitCols.Length;
        if (HitRouter != null && HitRouter(this, other)) return;
        if (!attackable) return;
        ReceiveHit(other.bounds.ClosestPoint(transform.position + Vector3.up * CapHeight * 0.6f), 1);
    }

    // 揺れ/光/火花。壊せる文字は耐久を減らす。
    public void ReceiveHit(Vector3 at, int damage)
    {
        if (Broken) return;
        TotalHits++;
        Kick(1f);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, new Color(1f, 0.9f, 0.55f, 1f), 7, 0.35f, 0.18f, 0.45f, 4f, 1.2f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.EnemyHit, 0.6f);
        if (!breakable) return;
        hp -= Mathf.Max(1, damage);
        if (hp <= 0) Shatter();
    }

    public void Kick(float strength)
    {
        wobbleVel += (Random.value < 0.5f ? -1f : 1f) * 260f * strength;
        flash = 1f;
        shakeT = 0.18f * strength;
    }

    public void SetCrackStage(int stage)
    {
        stage = Mathf.Clamp(stage, 0, 3);
        if (stage == CrackStage) return;
        CrackStage = stage;
        var s = GlyphFont.Get(Char, stage);
        if (s != null && sr != null) sr.sprite = s;
    }

    public void SetTint(Color c) { baseColor = c; }

    // 砕ける: 文字が回りながら飛び、金の粒が散る
    public void Shatter(float dir = 0f)
    {
        if (Broken) return;
        Broken = true;
        TotalBroken++;
        WorldPlatforms.Unregister(this);
        if (col != null) col.enabled = false;
        Vector3 c = transform.position + new Vector3(0f, CapHeight * 0.6f, 0f);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, new Color(1f, 0.85f, 0.4f, 1f), 18, 0.7f, 0.25f, 0.7f, 7f, 2.2f, RenderOrder.CombatFx);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, new Color(0.3f, 0.2f, 0.12f, 1f), 8, 0.5f, 0.2f, 0.5f, 5f, 1.6f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossDefeat, 0.45f);
        flyVel = new Vector2((dir != 0f ? dir : Random.Range(-1f, 1f)) * Random.Range(2f, 5f), Random.Range(5f, 9f));
        flySpin = Random.Range(-420f, 420f);
        flyT = 0f;
    }

    // 沈む/消える(選ばれなかった側)
    public void Sink(float depth, float duration)
    {
        sinkFrom = transform.position.y; sinkDepth = depth; sinkDur = Mathf.Max(0.05f, duration); sinkT = 0f;
        Broken = true;
        WorldPlatforms.Unregister(this);
        if (col != null) col.enabled = false;
    }

    Vector2 flyVel; float flySpin, flyT = -1f;
    float sinkFrom, sinkDepth, sinkDur, sinkT = -1f;

    void Update()
    {
        float dt = Time.deltaTime;
        if (flyT >= 0f)
        {
            flyT += dt;
            flyVel.y -= 18f * dt;
            transform.position += (Vector3)(flyVel * dt);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, sr.transform.localEulerAngles.z + flySpin * dt);
            float a = Mathf.Clamp01(1f - flyT / 1.1f);
            SetAlpha(a);
            if (flyT > 1.2f) gameObject.SetActive(false);
            return;
        }
        if (sinkT >= 0f)
        {
            sinkT += dt;
            float k = Mathf.Clamp01(sinkT / sinkDur);
            Vector3 p = transform.position; p.y = sinkFrom - sinkDepth * k * k; transform.position = p;
            SetAlpha(1f - k);
            if (k >= 1f) gameObject.SetActive(false);
            return;
        }
        // 揺れ(ばね)
        wobbleVel += (-wobble * 180f - wobbleVel * 9f) * dt;
        wobble += wobbleVel * dt;
        float shake = 0f;
        if (shakeT > 0f) { shakeT -= dt; shake = Mathf.Sin(Time.time * 70f) * 0.06f * CapHeight * Mathf.Clamp01(shakeT / 0.18f); }
        if (sr != null)
        {
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(wobble, -25f, 25f));
            sr.transform.localPosition = new Vector3(shake, 0f, 0f);
            flash = Mathf.Max(0f, flash - dt * 4f);
            sr.color = Color.Lerp(baseColor, Color.white * 1.2f, flash * 0.6f);
        }
        if (bobAmplitude > 0f)
        {
            Vector3 p = transform.position;
            // basePosは原点移動で古くなるので、Xは今の位置のまま、Yだけ基準からゆらす
            p.y = basePos.y + Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f + bobPhase) * bobAmplitude;
            transform.position = p;
        }
        if (glow != null) glow.color = new Color(1f, 0.85f, 0.45f, 0.14f + 0.2f * flash);
    }

    void SetAlpha(float a)
    {
        if (sr != null) { var c = sr.color; c.a = a; sr.color = c; }
        if (glow != null) { var c = glow.color; c.a = 0.14f * a; glow.color = c; }
        if (shadow != null) { var c = shadow.color; c.a = 0.35f * a; shadow.color = c; }
    }
}

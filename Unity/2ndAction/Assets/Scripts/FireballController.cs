using UnityEngine;

// A dragon fireball projectile. Flies in a straight line toward wherever it
// was aimed; if it touches the player's attack hitbox before reaching them,
// it reverses direction and becomes a threat to the dragon instead.
//
// 弾のアニメーション追加(2026-09-10) - マスターの「ボスの火球や雑魚敵の
// 攻撃にアニメーションを生成し、追加して」対応。ボスの火球も雑魚敵
// (Shooter種)の飛び道具も同じこのFireballControllerを使っているので、
// ここに手を入れれば両方に効く。専用のコマ送り素材は用意せず、手続き的な
// アニメーション(自転 + 脈動スケール + 後方へ散る燃えかす)で「動いている
// 塊」に見せる。色はsr.color(ボス=橙、Shooter=紫、反射後=青)をそのまま
// 使うので、種類ごとの見た目の違いは保たれる。
public class FireballController : MonoBehaviour
{
    public Vector2 velocity;
    public bool reflected;
    public float lifetime = 6f;
    public float reflectSpeedMultiplier = 2f;
    // If >0, the fireball sits still (already at `velocity`'s eventual
    // heading, just not moving yet) for this long before actually launching -
    // used for the majin's "ring of fireballs pauses, then flies out" attacks.
    public float holdDuration = 0f;

    [Header("Animation (手続き的 - 専用素材なし)")]
    // 自転(度/秒)。Awakeで符号をランダム化。
    public float spinSpeed = 220f;
    // 脈動:baseScaleに対する増減割合と速さ。当たり判定(BoxCollider2Dは
    // Root同居)も一緒に伸縮するため、体感に影響しない小さめの値に留める。
    public float pulseAmount = 0.09f;
    public float pulseSpeed = 14f;
    // 後方へ散る燃えかす:この間隔ごとに1個、寿命trailLifetimeで縮小フェード。
    public float trailInterval = 0.035f;
    public float trailLifetime = 0.28f;
    public float trailScaleFraction = 0.62f;

    float age;
    float holdTimer;
    bool launched;
    SpriteRenderer sr;

    Vector3 baseScale;
    float spinDir;
    float pulseSeed;
    float trailTimer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        launched = holdDuration <= 0f;
        baseScale = transform.localScale;
        spinDir = Random.value < 0.5f ? -1f : 1f;
        pulseSeed = Random.Range(0f, 100f);
    }

    void Update()
    {
        if (!launched)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdDuration) launched = true;
        }
        else
        {
            transform.position += (Vector3)(velocity * Time.deltaTime);
        }

        Animate();

        age += Time.deltaTime;
        if (age > lifetime) Destroy(gameObject);
    }

    void Animate()
    {
        // 自転
        transform.Rotate(0f, 0f, spinSpeed * spinDir * Time.deltaTime);

        // 脈動スケール(X反転などしていないのでbaseScaleにそのまま係数)
        float pulse = 1f + Mathf.Sin((Time.time + pulseSeed) * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * pulse;

        // 後方へ散る燃えかす(実際に動いている間だけ)
        if (launched && velocity.sqrMagnitude > 0.0001f)
        {
            trailTimer += Time.deltaTime;
            if (trailTimer >= trailInterval)
            {
                trailTimer = 0f;
                SpawnTrailMote();
            }
        }
    }

    void SpawnTrailMote()
    {
        Color tint = sr != null ? sr.color : new Color(1f, 0.5f, 0.15f);
        // 進行方向の少し後ろに置くと尾を引いて見える。
        Vector3 back = velocity.sqrMagnitude > 0.0001f ? -(Vector3)velocity.normalized : Vector3.zero;
        float trailScale = Mathf.Max(Mathf.Abs(baseScale.x), Mathf.Abs(baseScale.y)) * trailScaleFraction;
        // 縮みながらフェード(火球の尾らしく) - CreateTweenedにstartScale>
        // endScaleを渡す。
        OneShotSpriteEffect.CreateTweened(
            OneShotSpriteEffect.SoftDotSprite(),
            transform.position + back * (trailScale * 0.4f),
            tint,
            duration: trailLifetime,
            startScale: trailScale,
            endScale: trailScale * 0.28f,
            startAlpha: 0.7f,
            endAlpha: 0f,
            sortingOrder: (sr != null ? sr.sortingOrder : 5) - 1,
            holdFraction: 0.15f);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!reflected && other.CompareTag("PlayerAttack"))
        {
            reflected = true;
            launched = true; // a held fireball that gets hit immediately flies back
            velocity = -velocity * reflectSpeedMultiplier;
            if (sr != null) sr.color = new Color(0.4f, 0.75f, 1f); // recolor to signal it's now a threat to the boss
            return;
        }

        if (!reflected && other.CompareTag("Player"))
        {
            if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage(source: "Fireball:" + name);
            Destroy(gameObject);
        }
        // Hitting the dragon/majin (once reflected) is handled by their own
        // controller, which checks this component's `reflected` flag on
        // overlap.
    }

    public static GameObject Create(Sprite sprite, Vector3 position, Vector2 velocity, float holdDuration = 0f)
    {
        GameObject go = new GameObject("Fireball");
        go.transform.position = position;
        // 攻撃エフェクト本番素材化(2026-09-26) - 単色四角(square)が渡された場合、火球の実イラスト
        // (Resources/Effects/fireball、白基調で色はsr.colorで乗算)があればそちらを使う。
        // 当たり判定の大きさ(0.6x0.45)は従来どおりに保つ。
        bool useArt = sprite != null && sprite.name == "square" && FireballArt() != null;
        if (useArt) sprite = FireballArt();
        go.transform.localScale = useArt ? new Vector3(0.8f, 0.8f, 1f) : new Vector3(0.6f, 0.45f, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 0.45f, 0.1f);
        sr.sortingOrder = 5;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        if (useArt) col.size = new Vector2(0.6f / 0.8f, 0.45f / 0.8f);

        FireballController fb = go.AddComponent<FireballController>();
        fb.velocity = velocity;
        fb.holdDuration = holdDuration;

        return go;
    }

    static Sprite fireballArt;
    static bool fireballLoaded;
    public static Sprite FireballArt()
    {
        if (!fireballLoaded) { fireballLoaded = true; fireballArt = Resources.Load<Sprite>("Effects/fireball"); }
        return fireballArt;
    }

    // 回転/脈動しない飛翔体(矢など)として見せる。scaleは見た目の大きさ、angleは進行方向。
    public void SetStaticVisual(Vector3 scale, float angleDegrees)
    {
        spinSpeed = 0f;
        pulseAmount = 0f;
        baseScale = scale;
        transform.localScale = scale;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.size = new Vector2(0.8f, 0.25f);
    }
}

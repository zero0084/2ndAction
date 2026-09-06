using UnityEngine;

// A dragon fireball projectile. Flies in a straight line toward wherever it
// was aimed; if it touches the player's attack hitbox before reaching them,
// it reverses direction and becomes a threat to the dragon instead.
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

    float age;
    float holdTimer;
    bool launched;
    SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        launched = holdDuration <= 0f;
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

        age += Time.deltaTime;
        if (age > lifetime) Destroy(gameObject);
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
            if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage();
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
        go.transform.localScale = new Vector3(0.6f, 0.45f, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 0.45f, 0.1f);
        sr.sortingOrder = 5;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        FireballController fb = go.AddComponent<FireballController>();
        fb.velocity = velocity;
        fb.holdDuration = holdDuration;

        return go;
    }
}

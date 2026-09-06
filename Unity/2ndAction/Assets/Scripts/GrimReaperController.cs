using UnityEngine;

// Distance Level Design Ver.1, item 8 - the 100,000m "Death出現" beat.
// Deliberately minimal: no Collider (can't hurt or be hurt by anything),
// no HP, no chase - purely a visual confirmation that appeared and keeps
// existing ("Deathからどこまで逃げられるか" Endless Challenge is a FUTURE
// pass per the brief; this only needs to prove it can appear without
// stalling or ending the run). A gentle idle bob is the only motion, so it
// doesn't read as a completely static prop.
public class GrimReaperController : MonoBehaviour
{
    public float bobAmplitude = 0.3f;
    public float bobSpeed = 0.8f;
    float bobSeed;
    Vector3 basePos;

    void Start()
    {
        basePos = transform.position;
        bobSeed = Random.Range(0f, 1000f);
    }

    void Update()
    {
        float bob = Mathf.Sin((Time.time + bobSeed) * bobSpeed) * bobAmplitude;
        transform.position = basePos + new Vector3(0f, bob, 0f);
    }

    public static GameObject Create(Sprite sprite, Vector3 position, float scale, bool defaultFacingRight = true, Transform player = null)
    {
        GameObject go = new GameObject("GrimReaper");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = RenderOrder.Boss;

        go.AddComponent<GrimReaperController>();

        // Distance Level Design Ver.1.1 - facing fix. No Collider on this
        // GameObject at all (see the class comment), so flipping
        // localScale.x's sign has zero gameplay effect either way -
        // alwaysFacePlayer=true since Death doesn't move (see
        // GrimReaperController's bob), so "face the player" is the only
        // meaningful rule for it.
        var facing = go.AddComponent<EnemyFacing>();
        facing.visual = go.transform;
        facing.defaultFacingRight = defaultFacingRight;
        facing.alwaysFacePlayer = true;
        facing.player = player;

        return go;
    }
}

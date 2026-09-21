using UnityEngine;

// Distance Level Design Ver.1.1 - "新規Enemy/Boss画像が進行方向に対して後
// ろ向き" fix. Visual-only: flips ONLY the assigned `visual` transform's
// own localScale.x sign - never Root/Collider/AttackHitbox/GroundCheck (for
// the regular ground enemies, `visual` is the SpriteRenderer-holding child
// GroundFactory.CreateEnemy already builds; for Mechanical Dragon/Grim
// Reaper, which have no separate Visual child, `visual` is their own
// Transform instead - see the class comment on why that's still safe for
// them specifically).
//
// Added only where the brief's own "対象" list names it
// (EnemyDefinition.enableVisualFacing - see GroundFactory.CreateEnemy) -
// the original goblin's already-correct art, and every pre-existing Boss
// (real Dragon/Majin), are completely untouched.
//
// Runs in LateUpdate specifically so it always applies AFTER
// EnemyAnimator.Update's own idle squash/sway (which resets
// visual.localScale.y and rewrites .x back to its own captured
// baseVisualScale.x every frame, sign included) - LateUpdate is guaranteed
// to run after every component's Update this same frame, so this always
// has the final say on which way the sprite faces regardless of component
// execution order. Taking Mathf.Abs(current X) before reapplying sign
// (rather than assuming a fixed magnitude) is what keeps this correct even
// while something else (EnemyAnimator's squash, DragonController's death
// scale-punch) is also animating that same axis's magnitude.
public class EnemyFacing : MonoBehaviour
{
    public Transform visual;
    // Whether the SOURCE ART's character faces +X (screen right) in its
    // native orientation - per-species, since each new sprite was drawn
    // independently ("Enemyごとに元画像の向きが違っても対応できるよう" from
    // the brief). Set from EnemyDefinition.defaultFacingRight (or directly
    // by BossManager for Mechanical Dragon/Grim Reaper, which have no
    // EnemyDefinition of their own).
    public bool defaultFacingRight = true;
    public Transform player;
    // Bosses (Mechanical Dragon/Grim Reaper) drift forward continuously
    // just to hold pace with the player's own auto-run (AdvanceTrackedX),
    // not because they're "walking" anywhere - treating that incidental
    // scroll-following as a facing direction would make them face the
    // direction of travel (screen-right) instead of the player they're
    // actually fighting. Set true only for those two (see BossManager) so
    // they always face the player regardless of their own position drift;
    // every regular Formation-spawned enemy leaves this false so
    // "現在の移動方向に合わせて反転" still applies to their real walking.
    public bool alwaysFacePlayer = false;

    Vector3 lastPosition;
    float lastFacingDir = -1f; // -1=left, 1=right - defaults to facing left (the player's usual approach side) until the first real frame resolves it

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }
    void OnOriginShifted(float s) { lastPosition.x -= s; }

    void Start()
    {
        lastPosition = transform.position;
        if (player == null && PlayerController.Instance != null) player = PlayerController.Instance.transform;
    }

    void LateUpdate()
    {
        if (visual == null) return;

        float dx = transform.position.x - lastPosition.x;
        float facingDir;
        if (alwaysFacePlayer && player != null)
        {
            float toPlayer = player.position.x - transform.position.x;
            facingDir = Mathf.Abs(toPlayer) > 0.01f ? Mathf.Sign(toPlayer) : lastFacingDir;
        }
        else if (Mathf.Abs(dx) > 0.0008f)
        {
            // Actually moved this frame - face the direction of travel
            // ("現在の移動方向...に合わせてVisualだけ反転" - covers
            // Chaser/Rusher/Irregular/Heavy/Flying/Shooter's own retreat).
            facingDir = Mathf.Sign(dx);
        }
        else if (player != null)
        {
            // Stationary this frame (or no movement Behavior at all,
            // e.g. Mechanical Dragon/Grim Reaper/a static Normal-role
            // Formation member) - face the player instead ("Player側を向
            // く" - the brief's baseline rule).
            float toPlayer = player.position.x - transform.position.x;
            facingDir = Mathf.Abs(toPlayer) > 0.01f ? Mathf.Sign(toPlayer) : lastFacingDir;
        }
        else
        {
            facingDir = lastFacingDir;
        }

        lastFacingDir = facingDir;
        lastPosition = transform.position;

        bool wantFacingRight = facingDir > 0f;
        bool shouldFlip = wantFacingRight != defaultFacingRight;
        Vector3 s = visual.localScale;
        float absX = Mathf.Abs(s.x);
        s.x = shouldFlip ? -absX : absX;
        visual.localScale = s;
    }
}

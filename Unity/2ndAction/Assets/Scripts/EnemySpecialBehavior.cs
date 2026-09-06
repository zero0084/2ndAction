using System.Collections;
using UnityEngine;

// Distance Level Design Ver.1.1 - the movement/attack Behavior layered on
// top of a regular ground/flying EnemyController for every non-static
// species (Flying/Irregular/Shooter/Heavy/Chaser/Rusher - see
// EnemyBehaviorKind on EnemyDefinition). One component with a switch
// instead of several separate classes, mainly so EnemyController.
// OnTriggerEnter2D has exactly one thing to disable on death regardless of
// which kind this enemy actually is.
//
// EnemyVisual/EnemyBehavior separation (item 3 of the Ver.1.1 brief): this
// component only ever moves the Root transform (this.transform) - it never
// touches the Visual child's own localScale/localRotation, which stays
// EnemyAnimator's exclusive territory (idle squash/sway) the same way it
// already was for the static goblin. Movement and "look" are fully
// independent layers.
//
// Every tunable below (speed/range/cooldown/interval) is a public
// Inspector field with no bare constants buried in the logic - "コードに
// 固定値を大量に埋め込まないでください" from the brief.
//
// Normal category (behaviorKind None) never gets this component at all -
// the original "spawns once, sits static on its chunk" goblin behavior is
// completely unchanged.
public class EnemySpecialBehavior : MonoBehaviour
{
    public EnemyBehaviorKind kind = EnemyBehaviorKind.None;
    public Transform player;

    [Header("Flying - stays airborne, drifts toward the player")]
    public float flyingBobAmplitude = 0.35f;
    public float flyingBobSpeed = 1.2f;
    public float flyingApproachSpeed = 0.8f;
    // Never closes past this X distance - reads as "circling/threatening"
    // rather than eventually landing on top of the player.
    public float flyingStopDistance = 3f;
    public float flyingMinHeight = 1.2f; // world units above ground - never allowed to sink below this (item 3 - "地面へ着地しない")

    [Header("Irregular - random Move/Pause/Hop, action-based (not per-frame)")]
    public float irregularMoveSpeed = 1.6f;
    public float irregularLeashRange = 2.5f; // never wanders further than this from its own spawn X
    public float irregularHopHeight = 0.5f;
    public float irregularHopDuration = 0.35f;
    // "Action決定 -> 0.5〜1.5秒実行 -> 次Action" from the brief.
    public float minActionDuration = 0.5f;
    public float maxActionDuration = 1.5f;

    [Header("Shooter - holds range, fires on Cooldown, backs off if crowded")]
    public Sprite projectileSprite;
    public Color projectileColor = new Color(0.72f, 0.5f, 0.95f);
    public float shooterProjectileSpeed = 7f;
    // Only fires while the player is within this X distance - "Shooterが
    // 画面外から一方的に撃つ配置は禁止" (item 2) / "画面外から攻撃し続けな
    // いように" (item 3) satisfied the same way: no range, no shot.
    public float shooterRange = 11f;
    public float shooterCooldown = 1.6f;
    // Retreats slowly once the player closes inside this - "Playerが近づい
    // た場合は軽く後退しても構いません".
    public float shooterRetreatDistance = 3.5f;
    public float shooterRetreatSpeed = 1.2f;

    [Header("Heavy - slow, deliberate approach (never fully static)")]
    public float heavyApproachSpeed = 0.7f;
    public float heavyDetectionRange = 14f; // starts advancing once the player is within this
    public float heavyStopDistance = 1.4f;

    [Header("Chaser - steady pursuit, slightly slower than the player")]
    public float chaseSpeed = 3.2f; // tuned to sit just under the player's own base auto-run speed by default
    public float chaseDetectionRange = 16f;
    public float chaseStopDistance = 1.2f;

    [Header("Rusher - telegraph, then a fast burst, then a short recover")]
    public float rusherIdleSpeed = 0.6f;
    public float rusherDetectionRange = 8f;
    public float rusherTelegraphDuration = 0.35f;
    public float rusherDashSpeed = 7.5f;
    public float rusherDashDuration = 0.5f;
    public float rusherRecoverDuration = 1f;

    [Header("Safety - Despawn (item 3)")]
    // Chaser/Rusher give up and self-despawn if they end up this far behind
    // the player (off-screen-left, behind the auto-run direction) - "永久
    // に画面後方へ残り続けないようDespawn条件を用意".
    public float giveUpDistanceBehindPlayer = 20f;

    float spawnX;
    // Captured at Start from (spawn Y - ground height at spawn X), so
    // SetGroundedX can reproduce whatever foot-pivot/offset TerrainManager
    // originally spawned this enemy at without needing to duplicate its
    // private groundEnemyHeight constant here.
    float groundYOffset;
    float actionTimer;

    enum IrregularState { Pause, Move, Hop }
    IrregularState irregularState;
    float irregularMoveDir = 1f;
    bool hopping;

    float shooterTimer;

    enum RusherState { Idle, Telegraph, Dash, Recover }
    RusherState rusherState;
    float rusherStateTimer;
    float rusherDashDir = 1f;

    float flyingBobSeed;
    float flyingBaseY;

    void Start()
    {
        spawnX = transform.position.x;
        float? spawnGroundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(spawnX) : null;
        groundYOffset = spawnGroundY.HasValue ? transform.position.y - spawnGroundY.Value : 0f;
        if (player == null && PlayerController.Instance != null) player = PlayerController.Instance.transform;
        shooterTimer = shooterCooldown * 0.5f; // stagger first shot instead of every Shooter firing in lockstep
        flyingBobSeed = Random.Range(0f, 1000f);
        flyingBaseY = transform.position.y;
        PickIrregularAction();
    }

    void Update()
    {
        if (player == null) return;

        // Safety - a Chaser/Rusher that somehow ended up hopelessly behind
        // the auto-scrolling player is despawned rather than left running
        // forever off-screen (item 3).
        if ((kind == EnemyBehaviorKind.Chaser || kind == EnemyBehaviorKind.Rusher)
            && player.position.x - transform.position.x > giveUpDistanceBehindPlayer)
        {
            gameObject.SetActive(false);
            return;
        }

        switch (kind)
        {
            case EnemyBehaviorKind.Flying: UpdateFlying(); break;
            case EnemyBehaviorKind.Irregular: UpdateIrregular(); break;
            case EnemyBehaviorKind.Shooter: UpdateShooter(); break;
            case EnemyBehaviorKind.Heavy: UpdateHeavy(); break;
            case EnemyBehaviorKind.Chaser: UpdateChaser(); break;
            case EnemyBehaviorKind.Rusher: UpdateRusher(); break;
        }
    }

    // "空中に浮き続ける...完全停止ではなく、軽い上下移動 + Player方向へゆっ
    // くり接近...地面へ着地しない" (item 3). Horizontal approach uses
    // SetGroundedX's OWN ground-tracking only as the floor reference (via
    // flyingMinHeight added on top), never actually snapping to it.
    void UpdateFlying()
    {
        // flyingBaseY is a FIXED resting altitude (set once in Start, never
        // reassigned here) - horizontal drift only ever touches X, and Y is
        // always recomputed fresh from that fixed baseline + the current
        // bob offset, so nothing can accumulate/drift over time.
        float dx = player.position.x - transform.position.x;
        float x = transform.position.x;
        if (Mathf.Abs(dx) > flyingStopDistance)
        {
            x += Mathf.Sign(dx) * flyingApproachSpeed * Time.deltaTime;
        }

        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float floor = (groundY ?? (flyingBaseY - flyingMinHeight)) + flyingMinHeight;
        float bob = Mathf.Sin((Time.time + flyingBobSeed) * flyingBobSpeed) * flyingBobAmplitude;
        float targetY = Mathf.Max(flyingBaseY, floor) + bob;
        transform.position = new Vector3(x, targetY, transform.position.z);
    }

    void PickIrregularAction()
    {
        float roll = Random.value;
        irregularState = roll < 0.35f ? IrregularState.Pause : (roll < 0.75f ? IrregularState.Move : IrregularState.Hop);
        irregularMoveDir = Random.value < 0.5f ? -1f : 1f;
        actionTimer = Random.Range(minActionDuration, maxActionDuration);
        if (irregularState == IrregularState.Hop && !hopping) StartCoroutine(HopRoutine());
    }

    void UpdateIrregular()
    {
        actionTimer -= Time.deltaTime;

        if (irregularState == IrregularState.Move)
        {
            float nextX = transform.position.x + irregularMoveDir * irregularMoveSpeed * Time.deltaTime;
            // Leashed to spawnX so it reads as "wandering near here", not
            // drifting away indefinitely - reverses direction at the leash
            // edge instead of just stopping dead.
            if (Mathf.Abs(nextX - spawnX) > irregularLeashRange) irregularMoveDir = -irregularMoveDir;
            SetGroundedX(transform.position.x + irregularMoveDir * irregularMoveSpeed * Time.deltaTime);
        }

        if (actionTimer <= 0f) PickIrregularAction();
    }

    IEnumerator HopRoutine()
    {
        hopping = true;
        float baseY = transform.position.y;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, irregularHopDuration);
            float arc = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
            Vector3 p = transform.position;
            p.y = baseY + arc * irregularHopHeight;
            transform.position = p;
            yield return null;
        }
        Vector3 final = transform.position;
        final.y = baseY;
        transform.position = final;
        hopping = false;
    }

    // "Playerとの一定距離を保ちながらProjectileを発射...近づいた場合は軽く
    // 後退...画面外から攻撃し続けない...最低限Cooldown" (item 3).
    void UpdateShooter()
    {
        float dx = player.position.x - transform.position.x;
        float absDx = Mathf.Abs(dx);

        if (absDx < shooterRetreatDistance)
        {
            float away = -Mathf.Sign(dx);
            SetGroundedX(transform.position.x + away * shooterRetreatSpeed * Time.deltaTime);
        }

        if (absDx > shooterRange) return;

        shooterTimer -= Time.deltaTime;
        if (shooterTimer <= 0f)
        {
            shooterTimer = shooterCooldown;
            if (projectileSprite == null) return; // fail-safe - no asset, no throw, just skip firing
            Vector2 dir = ((Vector2)player.position - (Vector2)transform.position).normalized;
            GameObject fb = FireballController.Create(projectileSprite, transform.position, dir * shooterProjectileSpeed);
            var fbSr = fb.GetComponent<SpriteRenderer>();
            if (fbSr != null) fbSr.color = projectileColor;
        }
    }

    // "移動速度は遅い、HPは高い、Player方向へゆっくり前進...完全棒立ちには
    // しない" (item 3) - HP itself comes from EnemyDefinition.hpMultiplier
    // (unchanged this pass), this only owns the slow, deliberate approach.
    void UpdateHeavy()
    {
        float dx = player.position.x - transform.position.x;
        float absDx = Mathf.Abs(dx);
        if (absDx > heavyDetectionRange || absDx <= heavyStopDistance) return;
        float dir = Mathf.Sign(dx);
        SetGroundedX(transform.position.x + dir * heavyApproachSpeed * Time.deltaTime);
    }

    // "Playerを継続的に追跡...若干遅い、または条件によって追いつける程度"
    // (item 3) - chaseSpeed is Inspector-tunable relative to the player's
    // own base auto-run speed, not hardcoded here.
    void UpdateChaser()
    {
        float dx = player.position.x - transform.position.x;
        float absDx = Mathf.Abs(dx);
        if (absDx > chaseDetectionRange || absDx <= chaseStopDistance) return;
        float dir = Mathf.Sign(dx);
        SetGroundedX(transform.position.x + dir * chaseSpeed * Time.deltaTime);
    }

    // "Player検知 -> 短い予備動作 -> 高速突進 -> 少し停止 -> 再突進" (item
    // 3) - the Telegraph state is what makes the dash avoidable (a beat the
    // player can actually react to) rather than an instant lunge.
    void UpdateRusher()
    {
        float dx = player.position.x - transform.position.x;
        float absDx = Mathf.Abs(dx);

        switch (rusherState)
        {
            case RusherState.Idle:
                if (absDx <= rusherDetectionRange)
                {
                    rusherState = RusherState.Telegraph;
                    rusherStateTimer = rusherTelegraphDuration;
                    rusherDashDir = Mathf.Sign(dx);
                }
                else if (absDx > chaseStopDistance)
                {
                    SetGroundedX(transform.position.x + Mathf.Sign(dx) * rusherIdleSpeed * Time.deltaTime);
                }
                break;

            case RusherState.Telegraph:
                // Deliberately motionless during the telegraph - a visible
                // "about to move" beat (EnemyAnimator's own idle sway still
                // plays on the Visual child, untouched) before the burst.
                rusherStateTimer -= Time.deltaTime;
                if (rusherStateTimer <= 0f)
                {
                    rusherState = RusherState.Dash;
                    rusherStateTimer = rusherDashDuration;
                }
                break;

            case RusherState.Dash:
                SetGroundedX(transform.position.x + rusherDashDir * rusherDashSpeed * Time.deltaTime);
                rusherStateTimer -= Time.deltaTime;
                if (rusherStateTimer <= 0f)
                {
                    rusherState = RusherState.Recover;
                    rusherStateTimer = rusherRecoverDuration;
                }
                break;

            case RusherState.Recover:
                rusherStateTimer -= Time.deltaTime;
                if (rusherStateTimer <= 0f) rusherState = RusherState.Idle;
                break;
        }
    }

    // Moves along X and follows the actual ground height at the new X - the
    // same TerrainManager.GetHeightAt query the player/dust effects already
    // use. If the destination isn't over any known ground (a pit, or past
    // the far edge of generated terrain), the move is simply skipped this
    // frame rather than stepping into it.
    void SetGroundedX(float x)
    {
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        if (!groundY.HasValue) return;
        transform.position = new Vector3(x, groundY.Value + groundYOffset, transform.position.z);
    }
}

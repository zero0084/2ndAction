using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    public enum AttackDirection { Neutral, Forward, Backward }

    [Header("Move")]
    public float runSpeed = 5f;
    public float jumpForce = 9f;
    public float gravity = 20f;
    // Vertical distance from the ground line (TerrainManager.GetHeightAt)
    // up to where the player's transform sits when grounded. Left at 0
    // because every player sprite now uses a per-frame "foot pivot"
    // (ComputeLowestContentPivotY in SceneBuilder) - transform.position.y
    // IS the sprite's visible foot position, so any positive value here
    // would lift the visible foot that many units above the actual ground
    // surface. This used to be 0.5 back when the sprite had a dead-center
    // pivot and needed lifting so a 1-unit-tall BoxCollider2D's bottom
    // edge would land on the ground line - that assumption no longer
    // holds now that grounding is purely visual/math-driven (GetHeightAt),
    // not collider/physics-driven.
    public float groundOffset = 0f;
    public float failY = -8f;
    public int maxJumps = 2;
    // Visually tilts the whole player to match the ground slope while
    // grounded (never while airborne/jumping, and never on the always-flat
    // sky path), so an upright sprite doesn't show a wedge-shaped gap on
    // one side of the feet on an incline. Degrees/sec for the smoothing.
    public float slopeTiltSpeed = 720f;

    // Kept on during the boss fight too - the dragon tracks the player's
    // base auto-run speed every frame, so it holds a constant distance
    // while running regardless.
    public bool autoRunEnabled = true;

    [Header("Speed Ramp")]
    public float speedUpStartDistance = 100f;
    public float speedUpPer100m = 0.05f;
    public float maxSpeedMultiplier = 2f;

    [Header("Attack")]
    public Collider2D attackHitbox;
    public AttackSlashVisual attackSlashVisual;
    public float attackActiveTime = 0.4f;
    public float attackCooldown = 0.3f;
    // Fraction of attackActiveTime after which a fresh attack input is
    // buffered to chain immediately into the next attack (the combo window).
    public float comboWindowStart = 0.5f;
    // After this many chained attacks in a row, the combo window is skipped
    // so the player is forced through the normal cooldown gap before
    // attacking again, instead of chaining forever.
    public int maxComboChain = 3;
    public float lungeDistance = 1.2f;
    public float recoilDistance = 1.0f;
    // Attack range grows across the combo chain (small -> medium -> large),
    // matching the growth of the slash FX, so a bigger-looking swing also
    // actually reaches further.
    public float hitboxScaleStep = 0.18f;
    public float hitboxReachStep = 0.25f;

    [Header("Touch Controls")]
    public float swipeThreshold = 60f;

    [Header("Death")]
    public Sprite explosionParticleSprite;
    public Color explosionColor = new Color(0.3f, 0.7f, 1f);

    [Header("Hit / Lives")]
    public float hitInvincibleDuration = 5f;
    public float hitFlickerInterval = 0.1f;

    [Header("Game Feel - Damage Feedback (tunable)")]
    public bool damageFlashEnabled = true;
    public Color damageFlashColor = new Color(1f, 0.35f, 0.35f);
    public float damageFlashDuration = 0.12f;
    // Small - "小さなKnockback" per the brief, applied as a brief backward
    // velocity (see ApplyKnockback/Move) rather than a direct
    // transform.position write, since Move() overwrites transform.position
    // from scratch every frame anyway (a raw position offset would just get
    // stomped the very next frame).
    public bool damageKnockbackEnabled = true;
    public float damageKnockbackSpeed = 3.5f;
    public float damageKnockbackDuration = 0.15f;

    [Header("Escape (Run Continuation/Checkpoint Ver.1, items 2-4)")]
    // Renamed in spirit from the original "Ascension" win condition (which
    // this directly evolved from - see DoEscapeSuccess's own comment) to
    // match the brief's "1000m以降、3秒間長押しすると脱出" wording.
    // Gate is GameManager.EscapeAvailable - originally purely distance-
    // based (MaxDistance>=1000m), changed 2026-09-06 to require the first
    // Boss Reward to have actually completed (see EscapeAvailable's own
    // comment) - was BossesDefeated>0 even further back, before that.
    public float escapeHoldDuration = 3f;
    // Bugfix 2026-09-05, items 1/2 - root cause of "Jump/Attack stop
    // responding past 1000m (Boss出現後)" and "everything needs a long
    // press": escapeHoldTimer used to start accumulating the instant ANY
    // touch went down (see UpdateEscapeInput), and IsEscapeCharging used to
    // go true from escapeHoldTimer>0 - i.e. after a SINGLE frame of any
    // ordinary tap/swipe. Since wasEscapeChargingLastFrame is snapshotted
    // at the top of the NEXT frame, a normal tap's own release frame (which
    // is when touchJumpRequested actually gets set) almost always landed on
    // a frame where the PREVIOUS frame had already made escapeHoldTimer
    // positive - silently blocking Move's jump and skipping
    // HandleAttackInput() entirely for what was really just a normal tap or
    // swipe, not a genuine Escape attempt. escapeHoldTimer itself still
    // starts counting from the very first frame of any touch (needed so a
    // genuine hold's total real-world time to success is still exactly
    // escapeHoldDuration), but IsEscapeCharging - the ONLY thing that gates
    // Jump/Attack and drives the ring/gauge visuals - now requires the hold
    // to have already outlasted this grace window before it's treated as a
    // real Escape attempt. Comfortably longer than any real tap or swipe
    // takes to complete, comfortably shorter than escapeHoldDuration.
    public float escapeChargeConfirmDelay = 0.3f;
    public float ascendRiseSpeed = 11f;
    // Extra clearance above the (frozen) camera's top edge to rise past,
    // so the player visibly clears the screen instead of just touching the
    // edge - the actual rise distance is computed at runtime from the
    // camera's current framing, so it works regardless of orientation/aspect.
    public float ascendClearMargin = 3f;
    // Safety cap in case the camera can't be found, so the coroutine can't
    // spin forever.
    public float ascendMaxDuration = 6f;
    // Item 4 - Dark Navy/Gold/Cyan magic circle at the player's feet,
    // brightening/scaling up as the charge progresses. Reuses the existing
    // Double Jump Ring effect sprite (already in the project, see
    // CardFusionUI/DeckEditUI's own "shared magic circle" comment for the
    // same reuse elsewhere) rather than new dedicated art.
    public Sprite escapeRingSprite;
    SpriteRenderer escapeRingRenderer;

    public static PlayerController Instance { get; private set; }

    public bool IsGrounded => isGrounded;
    public bool IsAttacking => isAttacking;
    public bool IsHitInvincible => hitInvincibleTimer > 0f;
    public bool IsAscending => isAscending;
    // Item 3/4 - true while actively holding the escape charge (not yet
    // committed to the fly-up itself). 0..1 progress for the circular
    // gauge/magic circle brightness.
    // Bugfix 2026-09-05, items 1/2 - gated on escapeChargeConfirmDelay, not
    // 0f, so a normal tap/swipe (which resolves well within that window)
    // never trips this - see escapeChargeConfirmDelay's own comment.
    public bool IsEscapeCharging => escapeHoldTimer > escapeChargeConfirmDelay && !isAscending;
    public float EscapeChargeProgress01 => Mathf.Clamp01(escapeHoldTimer / Mathf.Max(0.01f, escapeHoldDuration));
    // The combo stage (1-3) of the attack currently playing - stays at
    // whatever the last attack's stage was in between attacks, so animator
    // code reading it exactly while isAttacking is true always gets the
    // right value.
    public int CurrentAttackStage => comboCount;

    // Grown by the "ATTACK UP" card (see GameManager) - read by
    // DragonController/MajinController at hit time instead of a fixed
    // damage value.
    public int AttackPower { get; private set; } = 2;
    public void AddAttackPower(int amount) => AttackPower += amount;

    // Grown by "ATTACK RANGE UP" - multiplies both the hitbox scale-up and
    // the reach offset that already grow across the combo chain (see
    // ApplyComboStageToHitbox), so a bigger range card makes every combo
    // stage reach further, not just add a flat bonus to one stage.
    public float AttackRangeMultiplier { get; private set; } = 1f;
    public void AddAttackRangeBonus(float delta) => AttackRangeMultiplier = Mathf.Max(0.1f, AttackRangeMultiplier + delta);

    // Grown by "ATTACK SPEED UP" - shrinks both attackActiveTime and
    // attackCooldown by the same factor (see DoAttack), so the whole combo
    // tempo speeds up without changing the relative timing of the combo
    // window inside it. Stacks multiplicatively (diminishing returns) and
    // is floored so it can never reach zero/negative duration.
    public float AttackSpeedMultiplier { get; private set; } = 1f;
    public void AddAttackSpeedBonus(float fractionFaster) => AttackSpeedMultiplier = Mathf.Max(0.25f, AttackSpeedMultiplier * (1f - fractionFaster));

    // Grown by "AIR ATTACK UP" - only added on top of AttackPower while
    // airborne (see EffectiveAttackPower); grounded attacks are unaffected.
    public int AirAttackPowerBonus { get; private set; }
    public void AddAirAttackPowerBonus(int amount) => AirAttackPowerBonus += amount;

    // ===== Card Expansion/Gacha Evolution Ver.1 additions ===== //
    // Mirrors AirAttackPowerBonus exactly, just the GROUNDED-only half
    // ("Ground Zero"/"Heavy Impact"/"Ground Fighter").
    public int GroundAttackPowerBonus { get; private set; }
    public void AddGroundAttackPowerBonus(int amount) => GroundAttackPowerBonus += amount;

    // "Combo Master"/"Combo Edge" - only on the LAST stage of the combo
    // chain (comboCount reaches maxComboChain).
    public int ComboFinalStageBonus { get; private set; }
    public void AddComboFinalStageBonus(int amount) => ComboFinalStageBonus += amount;

    // "First Strike"/"Sonic Blade" - only on the FIRST hit of a fresh combo.
    public int FirstHitBonus { get; private set; }
    public void AddFirstHitBonus(int amount) => FirstHitBonus += amount;

    // "Last Stand"/"Berserk Drive"/"Blood Rush"/"Adrenaline" - scales from
    // 0 (full HP) up to its full value (0 HP), via GameManager.Lives/
    // maxLives - a "fights harder while hurt" berserk-style bonus.
    public int LowHpAttackBonus { get; private set; }
    public void AddLowHpAttackBonus(int amount) => LowHpAttackBonus += amount;

    // "Iron Will"/"Blood Blade" - only while at full HP.
    public int FullHpAttackBonus { get; private set; }
    public void AddFullHpAttackBonus(int amount) => FullHpAttackBonus += amount;

    // "Momentum"/"Overdrive" - scales with how much of the Speed Up ramp
    // (GetSpeedMultiplier) has accumulated so far.
    public int MomentumBonus { get; private set; }
    public void AddMomentumBonus(int amount) => MomentumBonus += amount;

    // "Boss Killer" - only added against a Boss (see EffectiveBossAttackPower,
    // which DragonController/MajinController read instead of
    // EffectiveAttackPower directly).
    public int BossDamageBonus { get; private set; }
    public void AddBossDamageBonus(int amount) => BossDamageBonus += amount;

    // What DragonController/MajinController should actually read instead of
    // AttackPower directly, so AIR ATTACK UP/GROUND ATTACK/combo-stage/HP-
    // conditional/Momentum bonuses all take effect without any of them
    // needing to know about the player's current state themselves.
    public int EffectiveAttackPower
    {
        get
        {
            int power = AttackPower + (!isGrounded ? AirAttackPowerBonus : GroundAttackPowerBonus);
            if (comboCount == 1) power += FirstHitBonus;
            if (comboCount >= maxComboChain) power += ComboFinalStageBonus;

            GameManager gm = GameManager.Instance;
            if (gm != null && gm.maxLives > 0)
            {
                if (gm.Lives >= gm.maxLives)
                {
                    power += FullHpAttackBonus;
                }
                else
                {
                    float missingFraction = 1f - (float)gm.Lives / gm.maxLives;
                    power += Mathf.RoundToInt(LowHpAttackBonus * missingFraction);
                }
            }

            power += Mathf.RoundToInt(MomentumBonus * Mathf.Max(0f, GetSpeedMultiplier() - 1f));
            return power;
        }
    }

    // "Boss Killer" - Dragon/Majin/Mechanical Dragon damage calculations
    // use this instead of EffectiveAttackPower.
    public int EffectiveBossAttackPower => EffectiveAttackPower + BossDamageBonus;

    // Grown by "SHIELD" - each charge absorbs exactly one hit (see
    // GameManager.TryDamagePlayer) before any life is lost.
    public int ShieldCharges { get; private set; }
    public void AddShieldCharges(int amount) => ShieldCharges += amount;
    public bool TryConsumeShield()
    {
        if (ShieldCharges <= 0) return false;
        ShieldCharges--;
        return true;
    }

    // Fired at the moment a jump input actually launches the player, so
    // PlayerAnimator can play a short one-shot animation instead of just
    // reacting to "is grounded" every frame. JumpStarted = first jump (feet
    // leave the ground), DoubleJumped = second jump (already airborne).
    public event System.Action JumpStarted;
    public event System.Action DoubleJumped;
    public event System.Action Landed;
    // The player's base auto-scroll speed this frame, NOT including attack
    // lunge/recoil. Used by the boss to keep pace with ordinary running
    // without also cancelling out the player's attack-driven movement.
    public float CurrentAutoRunSpeed => autoRunEnabled ? runSpeed * GetSpeedMultiplier() : 0f;

    Rigidbody2D rb;
    SpriteRenderer sr;
    float velocityY;
    // Game Feel pass - a brief backward velocity on taking damage (see
    // ApplyKnockback), decayed linearly to 0 over knockbackTimer rather
    // than cut off sharply, folded into Move()'s own newX alongside
    // lungeVelocityX.
    float knockbackVelocityX;
    float knockbackTimer;
    float knockbackDuration = 1f;
    bool isGrounded;
    // Which surface isGrounded currently refers to - the main ground path,
    // or an elevated sky-path platform (see TerrainManager.GetSkyHeightAt).
    bool onSky;
    int jumpsUsed;
    bool isAttacking;
    float attackCooldownTimer;
    float startX;
    bool hasDied;
    float lungeVelocityX;
    float hitInvincibleTimer;
    float escapeHoldTimer;
    bool isAscending;
    // Item 2 - "脱出チャージ中はPlayerは通常操作を行えない" - snapshotted at
    // the TOP of Update() (before this frame's escape-charge state is
    // itself updated - see Update()'s own comment for why the ordering
    // matters), so the exact frame a charge is cancelled-by-release still
    // correctly suppresses that frame's jump, rather than un-suppressing
    // it one frame early just because escapeHoldTimer already reset to 0.
    bool wasEscapeChargingLastFrame;

    bool comboWindowOpen;
    bool comboBuffered;
    AttackDirection bufferedDirection;
    int comboCount;

    Vector3 hitboxBaseScale = Vector3.one;
    Vector3 hitboxBaseLocalPos;

    Vector2 touchStartPos;
    bool touchActive;
    bool swipeFiredThisTouch;
    bool touchJumpRequested;
    bool touchAttackRequested;
    float touchAttackDeltaX;
    bool wasStarted;

    // Input fix - "連続攻撃中の誤ジャンプ". A real swipe already never
    // re-classifies as a tap for the rest of THAT touch (swipeFiredThisTouch
    // stays true until release, see below - this part already worked). The
    // actual bug: rapid successive swipes can produce a brief stray touch-
    // down/up pair that never crosses swipeThreshold, which a fresh touch
    // legitimately reads as a tap -> Jump. This window suppresses ONLY that
    // Jump interpretation for a short beat right after a real swipe fires -
    // never the attack/swipe detection itself, so the very next real swipe
    // still fires instantly regardless of this timer ("攻撃をロックするの
    // ではなくJump判定だけ抑制する" from the brief).
    public float jumpSuppressionAfterAttack = 0.18f;
    float jumpSuppressedUntil = -1f;

    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see SceneBuilder.CreatePlayer. Only used here for the hit-
        // invincibility flicker and hiding the sprite on death.
        sr = GetComponentInChildren<SpriteRenderer>();
        startX = transform.position.x;
        isGrounded = true;

        if (attackHitbox != null)
        {
            attackHitbox.enabled = false;
            hitboxBaseScale = attackHitbox.transform.localScale;
            hitboxBaseLocalPos = attackHitbox.transform.localPosition;
        }
    }

    void Update()
    {
        bool hasStarted = GameManager.Instance == null || GameManager.Instance.HasStarted;
        if (!hasStarted)
        {
            wasStarted = false;
            return;
        }
        if (!wasStarted)
        {
            // Just started this frame: discard any pointer state left over
            // from the tap that started the game, so it doesn't also count
            // as a jump/attack input.
            touchActive = false;
            swipeFiredThisTouch = false;
            wasStarted = true;
        }

        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
        {
            if (!hasDied)
            {
                hasDied = true;
                if (!GameManager.Instance.IsWin) OnDeath();
            }
            return;
        }

        // Paused for a level-up choice (see GameManager) - skip reading
        // input entirely rather than just letting Move() no-op on a zero
        // deltaTime, so a tap on a choice card doesn't also get recorded as
        // a leftover jump/attack gesture that fires the instant play resumes.
        if (Time.timeScale <= 0f) return;

        if (hitInvincibleTimer > 0f)
        {
            hitInvincibleTimer = Mathf.Max(0f, hitInvincibleTimer - Time.deltaTime);
        }

        if (knockbackTimer > 0f) knockbackTimer = Mathf.Max(0f, knockbackTimer - Time.deltaTime);

        if (isAscending) return; // the ascend coroutine drives position directly

        // Item 2 - snapshot BEFORE this frame's UpdateEscapeInput() runs
        // (which may transition escapeHoldTimer back to 0 this very frame,
        // e.g. on release) - see wasEscapeChargingLastFrame's own comment
        // for why the ordering matters.
        wasEscapeChargingLastFrame = IsEscapeCharging;

        UpdatePointerInput();
        Move(allowJump: !wasEscapeChargingLastFrame);
        if (!wasEscapeChargingLastFrame) HandleAttackInput();

        UpdateEscapeInput();
        UpdateEscapeVisuals();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReportDistance(transform.position.x - startX);
        }
    }

    // Reads either the first touch (on device) or the mouse (in the Editor, for
    // easy testing) and turns it into a tap (jump) or swipe (attack) request.
    // A swipe fires the moment the finger crosses the threshold while still
    // down, so it feels immediate; a short, small movement counts as a tap
    // only once the finger is lifted.
    void UpdatePointerInput()
    {
        touchJumpRequested = false;
        touchAttackRequested = false;

        Vector2 pointerPos;
        bool pointerJustDown, pointerJustUp, pointerDown;

        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            pointerPos = t.position;
            pointerJustDown = t.phase == TouchPhase.Began;
            pointerJustUp = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            pointerDown = !pointerJustUp;
        }
        else
        {
            pointerPos = Input.mousePosition;
            pointerJustDown = Input.GetMouseButtonDown(0);
            pointerJustUp = Input.GetMouseButtonUp(0);
            pointerDown = Input.GetMouseButton(0);
        }

        if (pointerJustDown)
        {
            touchStartPos = pointerPos;
            touchActive = true;
            swipeFiredThisTouch = false;
        }
        else if (pointerDown && touchActive && !swipeFiredThisTouch)
        {
            if (Vector2.Distance(pointerPos, touchStartPos) >= swipeThreshold)
            {
                touchAttackRequested = true;
                touchAttackDeltaX = pointerPos.x - touchStartPos.x;
                swipeFiredThisTouch = true;
                jumpSuppressedUntil = Time.unscaledTime + jumpSuppressionAfterAttack;
            }
        }

        if (pointerJustUp && touchActive)
        {
            if (!swipeFiredThisTouch && Time.unscaledTime >= jumpSuppressedUntil)
            {
                touchJumpRequested = true;
            }
            touchActive = false;
        }
    }

    float GetSpeedMultiplier()
    {
        float distance = transform.position.x - startX;
        if (distance <= speedUpStartDistance) return 1f;

        float multiplier = 1f + (distance - speedUpStartDistance) / 100f * speedUpPer100m;
        return Mathf.Min(multiplier, maxSpeedMultiplier);
    }

    // Item 2 - allowJump=false while escape-charging (or just released one
    // this same frame - see the snapshot in Update()) skips ONLY the jump
    // trigger below; auto-run/gravity/landing all keep running normally
    // (the brief's "無防備になるリスク" wouldn't make sense if the player
    // could also just stand still safely during the charge).
    void Move(bool allowJump = true)
    {
        float dt = Time.deltaTime;
        float autoSpeed = autoRunEnabled ? runSpeed * GetSpeedMultiplier() : 0f;
        // Linear ease-out over knockbackDuration, not a flat velocity for
        // the whole window - reads as a shove that fades, not a sustained
        // shove-then-stop.
        float knockbackFrac = knockbackDuration > 0f ? knockbackTimer / knockbackDuration : 0f;
        float effectiveKnockback = knockbackVelocityX * knockbackFrac;
        float newX = transform.position.x + (autoSpeed + lungeVelocityX + effectiveKnockback) * dt;
        float prevX = transform.position.x;

        // Two independent, parallel surfaces the player can stand on - the
        // main ground path, and (optionally) an elevated sky-path platform
        // floating above it. Which one is actually "the ground" beneath the
        // player depends on which they last landed on (onSky).
        float? groundHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(newX) : null;
        float? prevGroundHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(prevX) : null;
        float? skyHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetSkyHeightAt(newX) : null;
        float? prevSkyHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetSkyHeightAt(prevX) : null;
        float prevY = transform.position.y;

        bool jumpPressed = allowJump && (Input.GetKeyDown(KeyCode.Space) || touchJumpRequested);
        if (jumpPressed && jumpsUsed < maxJumps)
        {
            velocityY = jumpForce;
            isGrounded = false;
            jumpsUsed++;
            if (jumpsUsed == 1)
            {
                JumpStarted?.Invoke();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayJump();
            }
            else
            {
                DoubleJumped?.Invoke();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayDoubleJump();
            }
        }
        else if (isGrounded)
        {
            float? currentSurface = onSky ? skyHeight : groundHeight;
            if (!currentSurface.HasValue)
            {
                velocityY = 0f;
                isGrounded = false;
            }
        }

        float newY;
        if (isGrounded)
        {
            float currentSurfaceHeight = (onSky ? skyHeight : groundHeight) ?? (prevY - groundOffset);
            newY = currentSurfaceHeight + groundOffset;
        }
        else
        {
            velocityY -= gravity * dt;
            newY = prevY + velocityY * dt;

            // Only land if we actually crossed a surface this frame. We
            // compare *surface-relative* height (position minus the terrain
            // height directly below) rather than raw Y, because on an
            // upward slope the terrain itself is rising each frame -
            // comparing raw Y against only this frame's (higher) surface
            // let the player clip straight through it. Checked against
            // BOTH surfaces since either could be what's below the player
            // right now; if both qualify, land on whichever is higher
            // (physically reached first while falling).
            bool landedSky = false;
            float skySurfaceY = 0f;
            if (skyHeight.HasValue)
            {
                float newSurfaceY = skyHeight.Value + groundOffset;
                float prevSurfaceY = (prevSkyHeight ?? skyHeight.Value) + groundOffset;
                if (prevY - prevSurfaceY >= 0f && newY - newSurfaceY <= 0f)
                {
                    landedSky = true;
                    skySurfaceY = newSurfaceY;
                }
            }

            bool landedGround = false;
            float groundSurfaceY = 0f;
            if (groundHeight.HasValue)
            {
                float newSurfaceY = groundHeight.Value + groundOffset;
                float prevSurfaceY = (prevGroundHeight ?? groundHeight.Value) + groundOffset;
                if (prevY - prevSurfaceY >= 0f && newY - newSurfaceY <= 0f)
                {
                    landedGround = true;
                    groundSurfaceY = newSurfaceY;
                }
            }

            if (landedSky && (!landedGround || skySurfaceY >= groundSurfaceY))
            {
                newY = skySurfaceY;
                velocityY = 0f;
                isGrounded = true;
                jumpsUsed = 0;
                onSky = true;
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
                Landed?.Invoke();
            }
            else if (landedGround)
            {
                newY = groundSurfaceY;
                velocityY = 0f;
                isGrounded = true;
                jumpsUsed = 0;
                onSky = false;
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
                Landed?.Invoke();
            }
        }

        transform.position = new Vector3(newX, newY, 0f);
        UpdateSlopeTilt(newX);

        // Bugfix 2026-09-06, item 2 - "下り坂走行中に突然GAME OVER". Root
        // cause: this check used to fire on raw newY alone, with no
        // isGrounded guard. TerrainManager's downhill-slope RNG walk isn't
        // hard-capped (PickNextType only gently biases back toward Y=0,
        // never forces it), so a real, unbroken, fully-grounded downhill
        // run can legitimately drift the ground height itself below failY -
        // and every single frame of that ordinary walk was tripping this
        // exact "fall death" check even though the player never left the
        // ground even once. A genuine fall (a Pit, the only place ground
        // height isn't continuously sampled under the player) always shows
        // up as isGrounded=false first - gating on that alone fixes this
        // without needing failY itself to become relative/dynamic.
        if (!isGrounded && newY < failY)
        {
            TakeDamage(isFall: true);
        }
    }

    // Rotates the player to visually sit flush against a sloped ground
    // segment - only while grounded on the main path (never mid-jump, and
    // never on the flat sky path, both of which snap back to upright).
    // Smoothed rather than snapped so a chunk boundary between a flat and
    // sloped segment doesn't pop instantly.
    void UpdateSlopeTilt(float x)
    {
        float targetAngle = 0f;
        if (isGrounded && !onSky && TerrainManager.Instance != null)
        {
            targetAngle = TerrainManager.Instance.GetSlopeAngleAt(x);
        }

        float currentAngle = transform.eulerAngles.z;
        float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * slopeTiltSpeed);
        transform.rotation = Quaternion.Euler(0f, 0f, newAngle);
    }

    // Single entry point for every "the player got hurt" source (enemy
    // contact, boss contact, fireball, falling). Non-fatal hits snap the
    // player back onto solid ground right where the hit happened, with a
    // few seconds of flickering invincibility; running out of lives lets
    // GameManager end the run, which the normal per-frame IsGameOver check
    // above then reacts to.
    public void TakeDamage(bool isFall = false)
    {
        if (hasDied) return;
        // A fall past failY must always respawn the player, even mid-flicker
        // from a previous hit - otherwise falling while still hit-invincible
        // silently no-ops every frame and the player free-falls forever
        // instead of ever landing back on solid ground.
        if (!isFall && hitInvincibleTimer > 0f) return;
        if (GameManager.Instance == null) return;

        // Bugfix 2026-09-06, item 2 - GameOverReason passthrough for the
        // debug log in GameManager.TryDamagePlayer (isFall is already the
        // one signal this project has to distinguish a fall death from
        // every other damage source - enemy/boss/fireball contact all call
        // TakeDamage() with isFall left at its false default).
        GameManager.DamageResult result = GameManager.Instance.TryDamagePlayer(bypassInvincibleMode: isFall, reason: isFall ? "DeathY" : "HPZero");
        if (result != GameManager.DamageResult.Hit) return;

        RespawnAtCurrentPosition();
        hitInvincibleTimer = hitInvincibleDuration;
        StartCoroutine(FlickerWhileInvincible());

        // Game Feel pass - flash/knockback/SE synchronized with this same
        // "the hit actually landed" moment, same as EnemyController's own
        // hit feedback (see section 20's "Feedbackの同期" brief).
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerDamage();
        if (damageFlashEnabled && sr != null) StartCoroutine(DamageFlashRoutine());
        if (damageKnockbackEnabled) ApplyKnockback(-damageKnockbackSpeed, damageKnockbackDuration);
    }

    // A brief backward push, decayed over its own duration rather than
    // fighting Move()'s own per-frame position write (see
    // knockbackVelocityX's field comment for why a raw transform.position
    // offset wouldn't survive the next frame here).
    public void ApplyKnockback(float velocityX, float duration)
    {
        knockbackVelocityX = velocityX;
        knockbackDuration = Mathf.Max(0.001f, duration);
        knockbackTimer = knockbackDuration;
    }

    IEnumerator DamageFlashRoutine()
    {
        Color normal = sr.color;
        sr.color = damageFlashColor;
        yield return new WaitForSecondsRealtime(damageFlashDuration);
        // FlickerWhileInvincible (started alongside this) only ever toggles
        // sr.enabled, never sr.color, so restoring the normal color here is
        // safe regardless of which one finishes first.
        sr.color = normal;
    }

    // Snaps back onto solid ground at (roughly) the X position where the hit
    // happened, rather than sending the player all the way back to the start
    // of the run. If that X is inside a pit (the falling case), it finds the
    // solid ground just behind the pit instead of respawning into empty air.
    void RespawnAtCurrentPosition()
    {
        velocityY = 0f;
        isGrounded = true;
        jumpsUsed = 0;
        lungeVelocityX = 0f;
        onSky = false;
        transform.localScale = Vector3.one;

        float x = transform.position.x;
        if (TerrainManager.Instance != null)
        {
            x = TerrainManager.Instance.FindSafeRespawnX(x);
        }
        float groundY = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(x) ?? 0f) : 0f;
        transform.position = new Vector3(x, groundY + groundOffset, 0f);
    }

    IEnumerator FlickerWhileInvincible()
    {
        while (hitInvincibleTimer > 0f)
        {
            if (sr != null) sr.enabled = !sr.enabled;
            yield return new WaitForSeconds(hitFlickerInterval);
        }
        if (sr != null) sr.enabled = true;
    }

    // Item 2 - "1000m以降、3秒間長押しすると脱出", distance-gated via
    // GameManager.EscapeAvailable instead of the original Ascension's
    // BossesDefeated>0. Releasing early ("途中で指を離した場合") just
    // resets the timer back to 0 with no separate cancel step needed -
    // UpdateEscapeVisuals/OnGUI below already read escapeHoldTimer<=0 as
    // "not charging" and disappear on their own the very next frame.
    void UpdateEscapeInput()
    {
        if (isAscending) return;

        bool canEscape = GameManager.Instance != null && GameManager.Instance.EscapeAvailable;
        bool down = Input.touchCount > 0
            ? Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled
            : Input.GetMouseButton(0);

        if (!canEscape || !down)
        {
            escapeHoldTimer = 0f;
            return;
        }

        escapeHoldTimer += Time.deltaTime;
        if (escapeHoldTimer >= escapeHoldDuration)
        {
            StartCoroutine(DoEscapeSuccess());
        }
    }

    // Item 2 - "3秒完了 -> 魔法陣完成 -> Playerを光で包む -> FINISH演出へ".
    // Directly reuses the original Ascension fly-up-and-off-screen visual
    // (camera-relative rise) as that "FINISH演出" - Win() is the same
    // method a Boss-clear win already called, so the Result screen's
    // existing "GAME CLEAR" headline and the full-RunMile-banking behavior
    // (see GameManager.FinishRun) both already apply correctly here too.
    IEnumerator DoEscapeSuccess()
    {
        isAscending = true;
        escapeHoldTimer = 0f;
        if (escapeRingRenderer != null) escapeRingRenderer.enabled = false;
        lungeVelocityX = 0f;
        if (attackHitbox != null) attackHitbox.enabled = false;

        // CameraFollow freezes the camera the instant isAscending flips true
        // (above), so its current framing IS the frame the player needs to
        // rise clear of. Deriving the target from it (rather than a fixed
        // distance) means this clears the top of the screen regardless of
        // orientation or aspect ratio.
        Camera cam = Camera.main;
        float targetY = cam != null
            ? cam.transform.position.y + cam.orthographicSize + ascendClearMargin
            : transform.position.y + 20f;

        float t = 0f;
        while (transform.position.y < targetY && t < ascendMaxDuration)
        {
            t += Time.deltaTime;
            transform.position += Vector3.up * ascendRiseSpeed * Time.deltaTime;
            yield return null;
        }

        if (GameManager.Instance != null) GameManager.Instance.Win();
    }

    // Item 4 - "長押し開始 -> 足元付近に薄い青+金の帰還魔法陣...0秒->1秒->
    // 2秒->3秒と進むにつれて...円形Gaugeが埋まる/魔法陣が明るくなる".
    // Lazily creates one reusable world-space ring (Assets/Art/Effects/
    // DoubleJumpRing.png via escapeRingSprite), tinted/scaled by charge
    // progress, hidden whenever not actively charging.
    void UpdateEscapeVisuals()
    {
        if (escapeRingSprite == null) return;

        if (!IsEscapeCharging)
        {
            if (escapeRingRenderer != null) escapeRingRenderer.enabled = false;
            return;
        }

        if (escapeRingRenderer == null)
        {
            GameObject go = new GameObject("EscapeRing");
            go.transform.SetParent(transform, false);
            escapeRingRenderer = go.AddComponent<SpriteRenderer>();
            escapeRingRenderer.sprite = escapeRingSprite;
            escapeRingRenderer.sortingOrder = RenderOrder.WorldUi;
        }

        escapeRingRenderer.enabled = true;
        float progress = EscapeChargeProgress01;
        // Dark Navy -> Gold/Cyan per the brief's palette, brightening as
        // the charge nears completion ("中央へ光が集まる" approximated by
        // the ring itself brightening/growing rather than a separate
        // particle count, per the brief's own "重いPost Processing等は不
        // 要" - a simple color/scale lerp instead).
        Color navy = new Color(0.2f, 0.35f, 0.55f, 0.5f);
        Color goldCyan = new Color(0.75f, 0.95f, 1f, 0.95f);
        escapeRingRenderer.color = Color.Lerp(navy, goldCyan, progress);
        escapeRingRenderer.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.25f, progress);
        escapeRingRenderer.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        escapeRingRenderer.transform.Rotate(Vector3.forward, 70f * Time.deltaTime);
    }

    void OnGUI()
    {
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

        if (IsEscapeCharging)
        {
            DrawEscapeChargeGauge();
        }
    }

    // Item 3 - "実際には文字主体ではなく、円形ゲージを中心にしてくださ
    // い". IMGUI has no native radial/pie-slice fill, so this approximates
    // "円形Gaugeが埋まる" with the same ring sprite scaling up and
    // brightening (Dark Navy -> Gold/Cyan) as progress increases, with the
    // %/remaining-seconds text as secondary annotation around it rather
    // than the main event - a disclosed simplification, not a true radial
    // wipe.
    void DrawEscapeChargeGauge()
    {
        float progress = EscapeChargeProgress01;
        float remaining = Mathf.Max(0f, escapeHoldDuration - escapeHoldTimer);

        Camera cam = Camera.main;
        Vector3 screenPos = cam != null ? cam.WorldToScreenPoint(transform.position) : new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        float guiY = Screen.height - screenPos.y;

        const float ringSize = 90f;
        Rect ringRect = new Rect(screenPos.x - ringSize / 2f, guiY - ringSize - 90f, ringSize, ringSize);

        if (escapeRingSprite != null)
        {
            Color prev = GUI.color;
            GUI.color = Color.Lerp(new Color(0.55f, 0.75f, 1f, 0.6f), new Color(1f, 0.92f, 0.5f, 1f), progress);
            GUI.DrawTexture(ringRect, escapeRingSprite.texture, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 16;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = new Color(0.75f, 0.95f, 1f);
        GUI.Label(new Rect(ringRect.x - 30f, ringRect.y - 26f, ringRect.width + 60f, 24f), "ESCAPE", labelStyle);

        GUIStyle pctStyle = new GUIStyle(GUI.skin.label);
        pctStyle.fontSize = 20;
        pctStyle.fontStyle = FontStyle.Bold;
        pctStyle.alignment = TextAnchor.MiddleCenter;
        pctStyle.normal.textColor = Color.white;
        GUI.Label(ringRect, $"{Mathf.RoundToInt(progress * 100f)}%", pctStyle);

        GUIStyle secStyle = new GUIStyle(GUI.skin.label);
        secStyle.fontSize = 14;
        secStyle.alignment = TextAnchor.MiddleCenter;
        secStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        GUI.Label(new Rect(ringRect.x - 30f, ringRect.yMax + 2f, ringRect.width + 60f, 22f), $"{remaining:0.0}s", secStyle);
    }

    void OnDeath()
    {
        if (sr != null) sr.enabled = false;
        if (attackHitbox != null) attackHitbox.enabled = false;

        // "最後の被弾 -> Player死亡状態 -> Death SE -> 短い死亡Visual ->
        // GAME OVER" (Game Feel pass, section 12) - Death SE plays instead
        // of another Player Damage SE for this final hit (see section 13's
        // role separation), and the BGM fades out alongside it rather than
        // cutting or continuing to play under the results screen.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayPlayerDeath();
            AudioManager.Instance.FadeOutBgm();
        }

        if (explosionParticleSprite != null)
        {
            // 0.6s (was ExplosionEffect.Create's own 3s default) - "短い死
            // 亡Visual" per the brief; GameManager's own game-over flow
            // doesn't wait on this either way (see EnemyController's
            // matching comment on its own death burst).
            ExplosionEffect.Create(explosionParticleSprite, transform.position, explosionColor, count: 16, duration: 0.6f);
        }
    }

    // Returns the attack direction requested THIS frame, or null if no
    // attack input happened this frame.
    AttackDirection? GetRequestedAttackDirection()
    {
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.B)) return AttackDirection.Neutral;
        if (touchAttackRequested)
        {
            return touchAttackDeltaX >= 0f ? AttackDirection.Forward : AttackDirection.Backward;
        }
        return null;
    }

    void HandleAttackInput()
    {
        attackCooldownTimer -= Time.deltaTime;
        AttackDirection? requested = GetRequestedAttackDirection();
        if (requested == null) return;

        if (!isAttacking && attackCooldownTimer <= 0f)
        {
            comboCount = 0;
            StartCoroutine(DoAttack(requested.Value));
        }
        else if (isAttacking && comboWindowOpen && !comboBuffered)
        {
            // Buffer only one input at a time - timing it right chains the
            // next attack immediately, but you can't stack up a queue of
            // unlimited attacks by mashing.
            comboBuffered = true;
            bufferedDirection = requested.Value;
        }
    }

    IEnumerator DoAttack(AttackDirection dir)
    {
        isAttacking = true;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount++;
        attackCooldownTimer = attackCooldown * AttackSpeedMultiplier;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(comboCount);

        ApplyAttackDirection(dir);
        if (attackHitbox != null) attackHitbox.enabled = true;
        ApplyComboStageToHitbox(comboCount);
        // Bugfix 2026-09-06 - pass the SAME AttackRangeMultiplier the hitbox
        // itself uses (see ApplyComboStageToHitbox below) so the visible
        // slash and the actual hit range can never disagree.
        if (attackSlashVisual != null) attackSlashVisual.SetComboStage(comboCount, AttackRangeMultiplier);

        // Once the chain has reached the cap, skip re-opening the combo
        // window: no input gets buffered this swing, so the player falls
        // through to the normal cooldown gap before the next attack.
        bool allowChain = comboCount < maxComboChain;

        // AttackSpeedMultiplier (from "ATTACK SPEED UP") shrinks the whole
        // swing uniformly, so the combo window still opens at the same
        // relative point in the (now shorter) swing.
        float effectiveActiveTime = attackActiveTime * AttackSpeedMultiplier;
        float t = 0f;
        while (t < effectiveActiveTime)
        {
            t += Time.deltaTime;
            if (allowChain && t >= effectiveActiveTime * comboWindowStart) comboWindowOpen = true;
            yield return null;
        }

        if (attackHitbox != null) attackHitbox.enabled = false;
        lungeVelocityX = 0f;
        transform.localScale = Vector3.one;

        isAttacking = false;
        comboWindowOpen = false;

        if (comboBuffered)
        {
            comboBuffered = false;
            StartCoroutine(DoAttack(bufferedDirection));
        }
    }

    // Grows the (invisible) attack hitbox across the combo chain - stage 1
    // is the base size/reach, each further stage scales it up and pushes it
    // a bit further out, matching the growing slash FX so the visible reach
    // and the actual hit reach agree.
    void ApplyComboStageToHitbox(int stage)
    {
        if (attackHitbox == null) return;
        int step = Mathf.Max(0, stage - 1);
        float scaleMul = (1f + step * hitboxScaleStep) * AttackRangeMultiplier;
        attackHitbox.transform.localScale = hitboxBaseScale * scaleMul;
        attackHitbox.transform.localPosition = hitboxBaseLocalPos + new Vector3(step * hitboxReachStep * AttackRangeMultiplier, 0f, 0f);
    }

    // Backward attacks flip the whole player transform, which also mirrors
    // the (forward-facing) hitbox/slash child objects onto the back side for
    // free - no separate backward hitbox needed.
    void ApplyAttackDirection(AttackDirection dir)
    {
        if (dir == AttackDirection.Backward)
        {
            transform.localScale = new Vector3(-1f, 1f, 1f);
            lungeVelocityX = -recoilDistance / attackActiveTime;
        }
        else
        {
            transform.localScale = Vector3.one;
            lungeVelocityX = dir == AttackDirection.Forward ? lungeDistance / attackActiveTime : 0f;
        }
    }
}

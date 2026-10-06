using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public partial class DragonController : MonoBehaviour, IBossBattleDebug
{
    enum State { Entering, Idle, Telegraphing, Charging, Firing, Landing, Dead, Stunned }
    public int AttacksStarted { get; private set; } // 確認用(攻撃を始めた回数)

    [Header("Animation")]
    public Sprite[] idleFrames;
    public Sprite[] chargeFrames;
    public Sprite[] fireFrames;
    public float animFps = 8f;

    [Header("Health / Damage")]
    public int maxHp = 200;
    public int playerAttackDamage = 20; // 10倍スケール
    public int fireballDamage = 20; // 跳ね返した火球がドラゴンに与える量(10倍スケール)

    [Header("Behaviour Timing")]
    // Distance Level Design Ver.1 - Mechanical Dragon's placeholder: "出現
    //確認用の最低限Placeholder Behaviorでも構いません" (item 7 of that
    // brief) - it uses this exact same DragonController (entrance/HP/hit/
    // death all identical) with attacksEnabled=false (see BossManager.
    // SpawnMechanicalDragon), so it appears, can be fought and defeated,
    // but never attacks - no attack Pattern has been designed for it yet,
    // and this field is what keeps that true rather than silently
    // inheriting the fire-breath Dragon's own attacks. true (unchanged) for
    // every real Dragon.
    public bool attacksEnabled = true;
    // Reward/MILE System Ver.1 - "ボスMILE: Dragon 50、Mechanical Dragon
    // 200". BossManager.SpawnMechanicalDragon overrides this to 200 right
    // alongside setting attacksEnabled=false; every real Dragon keeps the
    // default 50.
    public int mileReward = 50;
    public float attackIntervalMin = 1.5f;
    public float attackIntervalMax = 3f;
    // Charge attack is currently disabled (design is being revisited) - the
    // dragon only ever fires. Flip this back on to bring it back.
    public bool chargeAttackEnabled = false;

    // 荒野街道ボス追加(2026-09-20) - 80,000mのドラゴン用。天空回廊の既存
    // ドラゴン(炎/突進)の挙動はそのまま、「飛行→着地→噛みつき→再上昇」を
    // 追加攻撃として足す。既存ドラゴンはlandingAttackEnabled=falseのまま無影響。
    [Header("Landing Attack (荒野街道ドラゴン)")]
    public bool landingAttackEnabled = false;
    public float landingAttackChance = 0.3f;
    public float landingWarnDuration = 1.3f;
    public float landedStandoffDistance = 3.5f;
    public float landedDuration = 3.2f;

    [Header("Attack Telegraph")]
    public float telegraphDuration = 3f;
    public float telegraphBlinkInterval = 0.3f;

    [Header("Charge Attack (dive under, then swoop away)")]
    public float diveDuration = 1.1f;
    public float riseDuration = 0.9f;
    public float chargeReturnDuration = 0.6f;
    // Height above ground the dragon dives down to - low enough to be a
    // contact threat for a player who doesn't jump over it.
    public float diveHeight = 0.5f;
    // How far inside the camera's right edge the dragon rises up at, after
    // diving under the player - so the whole charge reads as "swoop in,
    // then bank away off-screen" rather than stopping mid-air nearby.
    public float riseEdgeMargin = 1.5f;

    [Header("Fire Attack")]
    public float fireWindupDuration = 0.5f;
    public float fireRecoverDuration = 0.6f;
    // 弾速の走行補正(2026-09-26) - 弾は走行速度で流れる座標系の中を飛ぶようになったため(PlayerController.
    // RunFrameSpeed)、正面から迫る見た目の速さが従来(=走行速度ぶん上乗せ)より遅くならないよう底上げ。
    public float fireballSpeed = 9f;
    public Vector2 fireballSpawnOffset = new Vector2(-1.2f, 0.1f);
    public float fireballInterval = 0.25f;

    [Header("Fire Attack Patterns")]
    // 5 patterns: single shot, a burst of tripleCount, a burst of
    // quintupleCount, a spread fan, and (only once at/below half HP) a
    // double fan. Picked uniformly at random each time from whichever set
    // is currently available.
    public int tripleCount = 3;
    public int quintupleCount = 5;
    public int fanCount = 5;
    public float fanSpreadDegrees = 60f;
    public float fanRepeatDelay = 0.4f;

    [Header("Positioning")]
    // Ground clearance (world units) kept under the dragon's belly while
    // hovering idle - derived together with the sprite's actual half-height
    // so resizing the dragon doesn't require re-tuning this by hand.
    public float groundClearance = 0.6f;
    public float standoffDistance = 8f;
    // Hard caps on how far ahead of / behind the player's actual position
    // the dragon's home spot can drift (see AdvanceTrackedX) - keeps it
    // from ever getting pushed off the visible screen by accumulated
    // lunge/recoil attacks in either direction.
    public float maxAheadOfPlayer = 20f;
    public float maxBehindPlayer = 10f;

    [Header("Idle Bob")]
    // The dragon drifts up and down while hovering instead of holding a
    // perfectly flat line, using per-instance Perlin noise so several
    // dragons on screen at once don't bob in visible lockstep.
    public float bobAmplitude = 1.4f;
    public float bobSpeedMin = 0.5f;
    public float bobSpeedMax = 1.4f;
    // A second, faster/smaller noise layered on top of the main bob so the
    // motion doesn't read as one smooth, predictable up-down drift.
    public float bobAmplitude2 = 0.6f;
    public float bobSpeedMin2 = 1.5f;
    public float bobSpeedMax2 = 3f;

    [Header("Entrance")]
    // On spawn the dragon starts off past the camera's right edge and flies
    // in to its hover position, instead of just popping into view.
    public float entranceDuration = 1.2f;
    public float entranceOffscreenMargin = 3f;

    // Boss Milestone Presentation pass - the "所定位置到達" beat, once
    // ReturnToHome finishes (see EnterThenSchedule/ArrivalPresentation).
    // Scale Emphasis (item 6 of the brief) is deliberately NOT implemented
    // here - this GameObject has no separate Root/Visual split (the
    // SpriteRenderer/Collider/Rigidbody all live on this same Transform,
    // unlike Player/ground Enemy), so a scale punch here would also punch
    // the trigger Collider, which the brief explicitly says not to touch.
    // Left out rather than done wrong.
    [Header("Boss Milestone Presentation - Arrival")]
    public float hpBarRevealDuration = 0.25f;
    public float arrivalShakeMagnitude = 0.06f;
    public float arrivalShakeDuration = 0.15f;

    // Boss Defeat Presentation pass - "最後のHitを強調" (item 1). Scale
    // Emphasis on Death (item 2's Scale 1.0->1.05->0.9) DOES touch this
    // GameObject's own transform.localScale, unlike Arrival's - safe here
    // specifically because state is already Dead by the time it runs (see
    // TakeDamage/FinalHitAndDie), so the Collider/Rigidbody riding along
    // with it can no longer register or deal any damage either way.
    [Header("Boss Defeat Presentation - Final Hit")]
    public float finalHitStopDuration = 0.14f;
    public float finalHitShakeStrength = 0.14f;
    public float finalHitShakeDuration = 0.16f;
    public Sprite finalHitSparkSprite;
    public float finalHitSparkScale = 0.75f;
    public AudioClip finalHitSe;

    [Header("Boss Defeat Presentation - Death")]
    public float bossDeathFlashDuration = 0.08f;
    public Color bossDeathFlashColor = new Color(0.7f, 0.9f, 1f, 1f);
    public float bossDeathDuration = 0.6f;
    public float bossDeathPunchScale = 1.05f;
    public float bossDeathFinalScale = 0.9f;
    public Sprite bossDeathSmokeSprite;
    public float bossDeathSmokeScale = 1f;
    public AudioClip bossDefeatSe;
    // ボス撃破時の飛散パーティクル(2026-09-10) - 雑魚敵と同じ
    // ExplosionEffect.CreateForDefeatをボスの実寸で。色はBossManagerが
    // スポーン時に設定する(通常ドラゴン=赤、機械龍=黄)。
    public bool defeatBurstEnabled = true;
    public Color defeatBurstColor = new Color(1f, 0.3f, 0.18f, 1f);

    [Header("Boss Defeat Presentation - HP Bar")]
    public float hpBarEmptyHoldDuration = 0.15f;
    public float bossHpBarFadeDuration = 0.25f;

    [Header("Refs")]
    public Sprite squareSprite;
    public Transform player;
    public Color explosionColor = new Color(1f, 0.5f, 0.1f);

    public int Hp { get; private set; }
    public bool IsDead => state == State.Dead;

    State state = State.Idle;
    SpriteRenderer sr;
    SpriteRenderer flashOverlay;
    SpriteRenderer hitFlashOverlay;
    PlayerController playerController;
    Vector3 homePos;
    float hoverHeight;
    float riseHeight;
    float nextAttackTime;
    int frameIndex;
    float frameTimer;
    Sprite[] currentFrames;
    DragonHealthBar hpBar;
    float bobSeed;
    float bobSpeed;
    float bobSeed2;
    float bobSpeed2;

    // Tracks the player's BASE auto-run progress only (not attack lunges),
    // so ordinary running keeps a constant gap while a forward/backward
    // attack lunge actually changes the distance by that same amount.
    float trackedX;

    // Floating Origin: 座標を戻した分、追従基準X/待機位置も戻す。
    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; BossBattle.Living.Add(this); }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; BossBattle.Living.Remove(this); }
    void OnOriginShifted(float s) { trackedX -= s; homePos.x -= s; }

    public void Init(Transform playerTransform)
    {
        player = playerTransform;
        playerController = player != null ? player.GetComponent<PlayerController>() : null;
        if (playerController == null) playerController = PlayerController.Instance; // マルチ: 相手の分身の前に出した時(速度は狙いの相手から取る)
        sr = GetComponent<SpriteRenderer>();
        SetFrames(idleFrames);

        CreateFlashOverlay();
        CreateHitFlashOverlay();
        RecomputeVerticalOffsets();

        trackedX = player.position.x;
        bobSeed = Random.Range(0f, 1000f);
        bobSpeed = Random.Range(bobSpeedMin, bobSpeedMax);
        bobSeed2 = Random.Range(0f, 1000f);
        bobSpeed2 = Random.Range(bobSpeedMin2, bobSpeedMax2);

        homePos = ComputeHomePosition();
        transform.position = ComputeOffscreenEntryPosition(homePos);

        Hp = maxHp;

        hpBar = DragonHealthBar.Create(squareSprite, transform, 2.6f, 0.24f);
        hpBar.offset = new Vector3(0f, hoverHeight * 0.5f + 0.5f, 0f);
        // Boss Milestone Presentation pass - "Boss HP BarはDragon出現前か
        // ら表示しない" (see ArrivalPresentation for the reveal).
        hpBar.SetHidden();
        if (battle != null)
        {
            if (battle.staggerMax > 0f) hpBar.EnableSub(squareSprite, 0.08f);
            hpBar.SetPhaseTicks(squareSprite, battle.phaseThresholds);
        }

        state = State.Entering;
        // マルチプレイPhase 2 - HOSTでは共有ボスとして登録。JOINでパペットとして作っている時はAIを始めない。
        if (NetCombat.OnBossInit(this)) return;
        StartCoroutine(EnterThenSchedule());
    }

    // Starts past the camera's right edge (never behind the player, even if
    // the dragon's hover spot is close by) so it always flies in from
    // off-screen instead of popping into view.
    Vector3 ComputeOffscreenEntryPosition(Vector3 target)
    {
        Camera cam = Camera.main;
        float rightEdge = cam != null
            ? cam.transform.position.x + cam.orthographicSize * cam.aspect
            : target.x + 6f;
        float startX = Mathf.Max(target.x, rightEdge) + entranceOffscreenMargin;
        return new Vector3(startX, target.y, 0f);
    }

    IEnumerator EnterThenSchedule()
    {
        yield return ReturnToHome(entranceDuration);
        yield return ArrivalPresentation();
        state = State.Idle;
        ScheduleNextAttack();
    }

    // Boss Milestone Presentation pass - fires once, right as the dragon
    // settles into its hover spot: reveal the HP bar, and one small camera
    // shake ("Bossが所定位置に入った瞬間に一度だけ使用" - never repeated,
    // never on every attack). Runs before ScheduleNextAttack, so the dragon
    // still can't attack until this finishes.
    IEnumerator ArrivalPresentation()
    {
        if (hpBar != null) StartCoroutine(hpBar.RevealRoutine(hpBarRevealDuration));

        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(arrivalShakeMagnitude, arrivalShakeDuration);

        yield return null;
    }

    void CreateFlashOverlay()
    {
        GameObject go = new GameObject("FlashOverlay");
        go.transform.SetParent(transform, false);
        flashOverlay = go.AddComponent<SpriteRenderer>();
        flashOverlay.color = Color.white;
        flashOverlay.sortingOrder = sr.sortingOrder + 1;
        flashOverlay.enabled = false;
    }

    void CreateHitFlashOverlay()
    {
        GameObject go = new GameObject("HitFlashOverlay");
        go.transform.SetParent(transform, false);
        hitFlashOverlay = go.AddComponent<SpriteRenderer>();
        hitFlashOverlay.color = new Color(1f, 0.15f, 0.15f, 0.85f);
        hitFlashOverlay.sortingOrder = sr.sortingOrder + 1;
        hitFlashOverlay.enabled = false;
    }

    // Derives hover/rise heights from the sprite's actual world-space bounds
    // (which already reflect the GameObject's scale), so a bigger or smaller
    // dragon doesn't need its clearance values re-tuned by hand.
    void RecomputeVerticalOffsets()
    {
        float halfHeight = 1.5f;
        if (sr != null && sr.sprite != null)
        {
            halfHeight = sr.sprite.bounds.extents.y * transform.lossyScale.y;
        }
        hoverHeight = groundClearance + halfHeight;
        riseHeight = hoverHeight + halfHeight * 1.5f;
    }

    void Update()
    {
        if (NetPuppet) { if (state != State.Dead) AnimateSprite(); return; }
        if (state == State.Dead) return;

        AnimateSprite();
        AdvanceTrackedX();
        BattleTick();

        if (state == State.Idle || state == State.Telegraphing || state == State.Firing)
        {
            ApplyHomePosition();
        }

        if (attacksEnabled && state == State.Idle && Time.time >= nextAttackTime)
        {
            // 天空回廊ボス追加(2026-09-25) - 複数体(最大4体)が同時に予備動作を始めて
            // 回避不能にならないよう、ドラゴン同士で攻撃開始をずらす(1体のみなら従来どおり)。
            if (Time.time < BossStaggerGate.NextDragonTime)
            {
                nextAttackTime = BossStaggerGate.NextDragonTime + Random.Range(0.1f, 0.5f);
                return;
            }
            BossStaggerGate.NextDragonTime = Time.time + BossStaggerGate.DragonInterval;
            // ボス戦の強化(2026-10-01): 第2段階の必殺技「煉獄の巨大火球」
            if (battle != null && phase >= 2 && battle.ultimateCooldown > 0f && Time.time - lastUltimateTime >= battle.ultimateCooldown
                && Time.time - phaseAt >= battle.firstUltimateDelay && BossBattle.TryBeginUltimate(this))
            {
                StartCoroutine(GiantFireball());
                return;
            }
            if (landingAttackEnabled && Random.value < landingAttackChance)
            {
                StartCoroutine(LandingAttack());
                return;
            }
            bool isCharge = chargeAttackEnabled && Random.value < 0.5f;
            StartCoroutine(TelegraphAndAttack(isCharge));
        }
    }

    // ===== 着地攻撃(荒野街道ドラゴン): 予告 → 降下 → 着地衝撃 → 噛みつき → 隙 → 再上昇 =====
    BossHitbox landHitbox, biteHitbox;

    void EnsureLandingHitboxes()
    {
        if (landHitbox != null) return;
        float sc = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.x));
        float halfW = sr.sprite != null ? sr.sprite.bounds.extents.x : 1f;   // ローカル単位
        float halfH = sr.sprite != null ? sr.sprite.bounds.extents.y : 1f;

        // ローカル座標(親スケール込み)で指定 - 判定サイズ=見えるVFXサイズ
        landHitbox = BossHitbox.Create(transform, BossFx.Ring(), new Color(1f, 0.7f, 0.3f, 0.9f), "Land", RenderOrder.Boss + 1);
        landHitbox.Configure(new Vector2(0f, -halfH * 0.4f), new Vector2(halfW * 2.0f, halfH * 0.9f));

        // 顔は素材の向き通り左(-x)。親スケールのx反転に自動で追従する。
        biteHitbox = BossHitbox.Create(transform, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f), "Bite", RenderOrder.Boss + 1);
        biteHitbox.Configure(new Vector2(-(halfW + 0.9f / sc), -halfH * 0.1f), new Vector2(2.6f / sc, 2.0f / sc));
    }

    IEnumerator BlinkFlash(float duration)
    {
        float t = 0f;
        bool flash = false;
        while (t < duration && state != State.Dead)
        {
            flash = !flash;
            if (flashOverlay != null) flashOverlay.enabled = flash;
            yield return new WaitForSeconds(0.12f);
            t += 0.12f;
        }
        if (flashOverlay != null) flashOverlay.enabled = false;
    }

    IEnumerator LandingAttack()
    {
        AttacksStarted++;
        EnsureLandingHitboxes();
        float sc = Mathf.Abs(transform.lossyScale.x);
        float halfHWorld = sr.sprite != null ? sr.sprite.bounds.extents.y * transform.lossyScale.y : 1.5f;
        float origStandoff = standoffDistance;

        state = State.Telegraphing; // ホバー追従を続けたまま予告
        float landX = trackedX + landedStandoffDistance;
        TrackedHazard.Create(landX, halfHWorld * 2.4f, halfHWorld * 1.2f, landingWarnDuration, 0f, Color.clear);
        yield return BlinkFlash(landingWarnDuration);
        if (state == State.Dead) yield break;

        // 降下(間合いを詰めながら地面へ)
        state = State.Landing;
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.8f;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            standoffDistance = Mathf.Lerp(origStandoff, landedStandoffDistance, e);
            float x = trackedX + standoffDistance;
            float y = Mathf.Lerp(start.y, GroundYAt(x) + halfHWorld * 0.95f, e);
            transform.position = new Vector3(x, y, 0f);
            yield return null;
            if (state == State.Dead) yield break;
        }

        // 着地衝撃
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.2f, 0.25f);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), transform.position + Vector3.down * halfHWorld * 0.8f, new Color(0.75f, 0.65f, 0.5f, 0.8f), 12, 0.5f, 0.7f, 1.3f, 3.5f, 2.4f, RenderOrder.CombatFx);
        yield return landHitbox.Strike(1f, 0.35f);
        if (state == State.Dead) yield break;

        // 着地状態: 地面に張り付いて追従(プレイヤーが殴れる隙)。途中で噛みつき。
        float grounded = 0f;
        bool biteDone = false;
        while (grounded < landedDuration)
        {
            grounded += Time.deltaTime;
            float x = trackedX + standoffDistance;
            transform.position = new Vector3(x, GroundYAt(x) + halfHWorld * 0.95f, 0f);

            if (!biteDone && grounded > 0.8f)
            {
                biteDone = true;
                float frontDir = -Mathf.Sign(transform.lossyScale.x);
                float halfWWorld = sr.sprite.bounds.extents.x * sc;
                TrackedHazard.Create(transform.position.x + frontDir * (halfWWorld + 0.9f), 2.6f, 2.0f, 0.8f, 0f, Color.clear);
                yield return BlinkFlash(0.8f);
                if (state == State.Dead) yield break;
                yield return biteHitbox.Strike(1f, 0.3f);
                if (state == State.Dead) yield break;
                grounded += 1.1f;
                continue;
            }
            yield return null;
            if (state == State.Dead) yield break;
        }

        // 再上昇
        standoffDistance = origStandoff;
        state = State.Landing;
        yield return ReturnToHome(0.9f);
        if (state == State.Dead) yield break;
        state = State.Idle;
        ScheduleNextAttack();
    }

    // Advances at the player's current BASE auto-run speed only (never their
    // attack-lunge velocity), keeping the standoff distance's baseline fixed
    // regardless of how far the player has actually run.
    void AdvanceTrackedX()
    {
        float runSpeed = TargetBaseSpeed();
        float baseSpeed = runSpeed;
        // マルチ Phase 3.1: 置き去り防止(BossLeash)。シングル/JOINでは何もしない
        bool leashOn = BossLeash.Enabled;
        BossLeash.Result leash = default;
        if (leashOn)
        {
            leash = BossLeash.Evaluate(transform.position.x, player, netTarget != null ? netTarget.TargetPlayer : 0, ref leashTime);
            if (netTarget != null) netTarget.SetPreferred(leash.PreferTarget);
            if (leash.Active) baseSpeed *= leash.SpeedFactor;
        }
        trackedX += baseSpeed * Time.deltaTime;

        // Repeated attack lunges in the same direction can push the home spot
        // off-screen; clamp every frame, symmetrically, against the player's
        // CURRENT actual position.
        if (player != null)
        {
            float minTrackedX = player.position.x - maxBehindPlayer - standoffDistance;
            float maxTrackedX = player.position.x + maxAheadOfPlayer - standoffDistance;
            if (leashOn && leash.AllowBehindTarget) minTrackedX = float.MinValue;
            float clamped = Mathf.Clamp(trackedX, minTrackedX, maxTrackedX);
            if (leashOn && Mathf.Abs(clamped - trackedX) > 6f)
                trackedX = BossLeash.Approach(trackedX, player.position.x - standoffDistance, -maxBehindPlayer, maxAheadOfPlayer, runSpeed, Time.deltaTime, leash.AllowBehindTarget, maxAheadOfPlayer + 20f);
            else trackedX = clamped;
        }
    }

    // Derives the current hover target from tracked distance, ground height,
    // and a per-instance Perlin-noise bob so the dragon drifts up and down
    // instead of holding a dead-flat line - shared by idle tracking, the
    // charge-attack return glide, and the entrance flight-in.
    Vector3 ComputeHomePosition()
    {
        float groundY = GroundYAt(trackedX);
        float bobOffset = (Mathf.PerlinNoise(Time.time * bobSpeed + bobSeed, 0f) * 2f - 1f) * bobAmplitude
            + (Mathf.PerlinNoise(Time.time * bobSpeed2 + bobSeed2, 0f) * 2f - 1f) * bobAmplitude2;
        return new Vector3(trackedX + standoffDistance, groundY + hoverHeight + bobOffset, 0f);
    }

    void ApplyHomePosition()
    {
        homePos = ComputeHomePosition();
        transform.position = homePos;
    }

    void SetFrames(Sprite[] frames)
    {
        currentFrames = frames;
        frameIndex = 0;
        frameTimer = 0f;
        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    void AnimateSprite()
    {
        if (currentFrames == null || currentFrames.Length == 0) return;

        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / animFps)
        {
            frameTimer = 0f;
            frameIndex = (frameIndex + 1) % currentFrames.Length;
            sr.sprite = currentFrames[frameIndex];
        }

        if (flashOverlay != null) flashOverlay.sprite = sr.sprite;
        if (hitFlashOverlay != null) hitFlashOverlay.sprite = sr.sprite;
    }

    void ScheduleNextAttack()
    {
        nextAttackTime = Time.time + Random.Range(attackIntervalMin, attackIntervalMax);
    }

    float GroundYAt(float x)
    {
        if (TerrainManager.Instance == null) return 0f;
        return TerrainManager.Instance.GetHeightAt(x) ?? 0f;
    }

    // World-space X of the camera's current right edge, so the charge
    // attack's rise-away phase can target "right at the edge of the visible
    // screen" regardless of zoom/aspect.
    float CameraRightEdgeX()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position.x + 6f;
        return cam.transform.position.x + cam.orthographicSize * cam.aspect - riseEdgeMargin;
    }

    IEnumerator TelegraphAndAttack(bool isCharge)
    {
        AttacksStarted++;
        state = State.Telegraphing;

        float t = 0f;
        bool flash = false;
        while (t < telegraphDuration)
        {
            flash = !flash;
            if (flashOverlay != null) flashOverlay.enabled = flash;
            yield return new WaitForSeconds(telegraphBlinkInterval);
            t += telegraphBlinkInterval;
        }
        if (flashOverlay != null) flashOverlay.enabled = false;

        if (state != State.Telegraphing) yield break; // dragon died mid-telegraph

        yield return isCharge ? ChargeAttack() : FireAttack();
    }

    // Dives down to ground level right at the player's position (dodgeable
    // by jumping over it), then rises up at the edge of the screen, then
    // glides back down into its normal tracked hover position. The target
    // X is captured once here, at the moment the actual attack motion
    // begins (after the telegraph has already finished) - not re-read as
    // the player keeps moving during the dive itself.
    IEnumerator ChargeAttack()
    {
        state = State.Charging;
        SetFrames(chargeFrames);

        Vector3 start = transform.position;
        float diveX = player != null ? player.position.x : start.x;
        Vector3 divePos = new Vector3(diveX, GroundYAt(diveX) + diveHeight, 0f);
        yield return LerpPosition(start, divePos, diveDuration);

        float riseX = CameraRightEdgeX();
        Vector3 risePos = new Vector3(riseX, GroundYAt(riseX) + riseHeight, 0f);
        yield return LerpPosition(transform.position, risePos, riseDuration);

        yield return ReturnToHome(chargeReturnDuration);

        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    // Glides back toward the CURRENT tracked home position (recomputed each
    // frame) rather than a fixed snapshot, so it lands exactly where normal
    // idle-tracking would already be and there's no post-attack snap.
    IEnumerator ReturnToHome(float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            Vector3 target = ComputeHomePosition();
            transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    enum FirePattern { Single, TripleLine, QuintupleLine, Fan, DoubleFanLowHp }

    // DoubleFanLowHp only enters the pool once HP is at/below half, per the
    // 5th-pattern requirement - otherwise it picks uniformly among the first 4.
    FirePattern PickFirePattern()
    {
        bool lowHp = Hp <= maxHp / 2;
        int optionCount = lowHp ? 5 : 4;
        return (FirePattern)Random.Range(0, optionCount);
    }

    IEnumerator FireAttack()
    {
        state = State.Firing;
        SetFrames(fireFrames);

        yield return new WaitForSeconds(fireWindupDuration);

        FirePattern pattern = PickFirePattern();
        switch (pattern)
        {
            case FirePattern.Single:
                if (state == State.Firing) SpawnFireball(0f);
                break;
            case FirePattern.TripleLine:
                yield return FireLine(tripleCount);
                break;
            case FirePattern.QuintupleLine:
                yield return FireLine(quintupleCount);
                break;
            case FirePattern.Fan:
                FireFan();
                break;
            case FirePattern.DoubleFanLowHp:
                FireFan();
                yield return new WaitForSeconds(fanRepeatDelay);
                if (state == State.Firing) FireFan();
                break;
        }

        yield return new WaitForSeconds(fireRecoverDuration);

        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    IEnumerator FireLine(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (state != State.Firing) yield break;
            SpawnFireball(0f);
            if (i < count - 1) yield return new WaitForSeconds(fireballInterval);
        }
    }

    void FireFan()
    {
        if (player == null || squareSprite == null || fanCount <= 0) return;

        float startOffset = -fanSpreadDegrees / 2f;
        for (int i = 0; i < fanCount; i++)
        {
            float t = fanCount > 1 ? i / (float)(fanCount - 1) : 0.5f;
            float angleOffset = startOffset + fanSpreadDegrees * t;
            SpawnFireball(angleOffset);
        }
    }

    void SpawnFireball(float angleOffsetDegrees)
    {
        if (player == null || squareSprite == null) return;

        // fireballSpawnOffset is defined relative to the dragon's own art, so
        // it needs to scale with the dragon's size.
        Vector3 scaledOffset = Vector3.Scale((Vector3)fireballSpawnOffset, transform.lossyScale);
        Vector3 spawnPos = transform.position + scaledOffset;
        Vector2 dir = ((Vector2)player.position - (Vector2)spawnPos).normalized;

        if (Mathf.Abs(angleOffsetDegrees) > 0.01f)
        {
            float rad = angleOffsetDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            dir = new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
        }

        FireballController.Create(squareSprite, spawnPos, dir * fireballSpeed).GetComponent<FireballController>().bossOwned = true;
    }

    IEnumerator LerpPosition(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (state == State.Dead) return;

        if (other.CompareTag("PlayerAttack") && PlayerAttackInfo.AlreadyHit(other, this)) return; // one hit per attack instance (2026-10-03)
        if (other.CompareTag("PlayerAttack") && NetPuppet)
        {
            NetPuppetHit(other);
            return;
        }

        // マルチプレイPhase 2.5: JOINのパペットは、HOSTの竜が突進中(状態をスナップショットで受信)の時だけ
        // 体当たりとして被弾を申告する(HOSTが実在と無敵を確かめてHPを確定)。
        if (NetPuppet)
        {
            if (other.CompareTag("Player") && netPose == (byte)State.Charging && PlayerController.Instance != null)
            {
                NetMatch.SetClaimContext(NetMatch.ClaimKind.EnemyContact, NetId);
                try { PlayerController.Instance.TakeDamage(source: "Dragon:" + name, amount: BossManager.ScaleDamage(CombatScale.PlayerHit)); }
                finally { NetMatch.ClearClaimContext(); }
            }
            return;
        }

        if (other.CompareTag("PlayerAttack"))
        {
            // Grows with the player's "Attack Power UP" level-up choice;
            // playerAttackDamage is only the fallback if that's unavailable.
            int damage = PlayerAttackInfo.ScaleDamage(other, this, PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamage);
            var info = other.GetComponent<PlayerAttackInfo>();
            bool air = PlayerController.Instance != null && !PlayerController.Instance.IsGrounded;
            pendingStagger = BossBattleTuning.I.StaggerFor(info != null ? info.kind : PlayerAttackKind.Normal, air);
            NoteFinalAttack(other, info); // BOSS FINISH
            TakeDamage(damage);
            return;
        }

        // The dragon's body only hurts the player while it's actively
        // charging (diving in/swooping through) - just standing/hovering
        // near it, or being near it while it breathes fire, is safe.
        if (other.CompareTag("Player") && state == State.Charging)
        {
            if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage(source: "Dragon:" + name, amount: BossManager.ScaleDamage(CombatScale.PlayerHit));
            return;
        }

        FireballController fb = other.GetComponent<FireballController>();
        if (fb != null && fb.reflected)
        {
            // 跳ね返した巨大火球: 大ダメージ+大きく崩れる(迎撃という選択肢)
            bool giant = fb.transform.localScale.x > 1.4f;
            pendingStagger = BossBattleTuning.I.staggerReflect * (giant ? 2.2f : 1f);
            if (giant) { Debug.Log("[BossBattle] Dragon hit by its own giant fireball (reflected)"); BossBattleHud.Banner("跳ね返した!", new Color(0.5f, 0.85f, 1f), 1.0f); }
            TakeDamage(giant ? fireballDamage * 4 : fireballDamage);
            Destroy(fb.gameObject);
        }
    }

    public void TakeDamage(int amount)
    {
        if (state == State.Dead || NetPuppet) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit); // ボス被弾(共通、連打は間引き)
        if (state == State.Stunned) amount = Mathf.CeilToInt(amount * BossBattleTuning.I.breakDamageScale);
        float stg = pendingStagger; pendingStagger = 0f;

        Hp = Mathf.Max(0, Hp - amount);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);
        if (Hp <= 0) BossFinishCode = BossDeathAdapter.Decide(pendingFinal, pendingFinalSet, netAttacker, transform).Pack(); // BOSS FINISH(OpDeath に載せる)
        NetCombat.AuthorityDamaged(NetId, netAttacker, amount, Hp, 0, transform.position, Hp <= 0);

        if (Hp <= 0)
        {
            // Claimed immediately (not at the end of the death coroutine
            // any more) - OnTriggerEnter2D/Update's own state==Dead guards
            // then stop the dragon attacking/taking further damage right
            // away, while FinalHitAndDie below keeps it visually on screen
            // for its short death presentation instead of vanishing this
            // same frame.
            state = State.Dead;
            BossBattle.EndUltimate(this);
            if (hpBar != null) hpBar.SetSub(0f, false);
            if (BossFinish.Enabled) BeginBossFinish(false); else StartCoroutine(FinalHitAndDie());
            return;
        }
        CheckBattlePhase();
        if (stg > 0f) AddStagger(stg);

        // Game Feel pass, section 19 - a very small camera shake, boss hits
        // only (regular EnemyController kills deliberately never do this -
        // "通常攻撃では基本Shakeなし").
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.08f, 0.1f);
        StartCoroutine(HitFlash());
    }

    // ===================================================================== //
    // ボス戦の強化(2026-10-01): 荒野街道のドラゴン(80,000m)だけ。第2段階/必殺技「煉獄の巨大火球」/崩し
    //  必殺技: 上空へ → 巨大な火球を溜める → プレイヤーへゆっくり撃つ。跳んでかわす or 攻撃で跳ね返す(当たると大ダメージ+大きく崩れる)。
    //  崩し(BREAK): 地面へ墜ちて数秒無防備。
    // ===================================================================== //
    BossBattleTuning.Entry battle;
    int phase = 1;
    float phaseAt, lastUltimateTime = -99f, stagger, lastStaggerTime, pendingStagger;
    public int Phase => phase;
    public int BreakCount { get; private set; }
    public int UltimatesUsed { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DebugAddStagger(float v) => AddStagger(v);
#endif
    public bool Broken => state == State.Stunned;
    SpriteRenderer chargeOrb;

    // 天空ボス強化(2026-10-05): 1,000m の天空ドラゴン。必殺技の名前/後の低空の隙/再戦の段階
    [System.NonSerialized] public bool skyMode;
    float staggerMul = 1f;
    public int RematchTier { get; private set; } = -1;
    public void ApplyRematchTier(BossRematchTuning.Tier tier, int tierIndex)
    {
        if (battle == null || tier == null) return;
        battle = battle.Clone();
        battle.staggerMax *= Mathf.Max(0.1f, tier.staggerMul);
        if (battle.ultimateCooldown > 0f) battle.ultimateCooldown *= Mathf.Max(0.1f, tier.cooldownMul);
        attackIntervalMin *= Mathf.Max(0.5f, tier.cooldownMul); attackIntervalMax *= Mathf.Max(0.5f, tier.cooldownMul);
        if (tier.extraPhase && battle.phaseThresholds != null && battle.phaseThresholds.Length < 3)
        {
            var th = new System.Collections.Generic.List<float>(battle.phaseThresholds);
            th.Add(Mathf.Clamp((th.Count > 0 ? th[th.Count - 1] : 1f) * 0.5f, 0.12f, 0.9f));
            battle.phaseThresholds = th.ToArray();
        }
        RematchTier = tierIndex;
    }

    public void EnableWastelandBattle(BossBattleTuning.Entry entry)
    {
        battle = entry;
        if (battle != null && battle.hpScale > 0f && Mathf.Abs(battle.hpScale - 1f) > 0.001f) maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * battle.hpScale));
    }

    void BattleTick()
    {
        if (battle == null) return;
        if (state != State.Stunned && stagger > 0f && Time.time - lastStaggerTime > BossBattleTuning.I.staggerRecoveryDelay)
            stagger = Mathf.Max(0f, stagger - battle.staggerRecoveryPerSec * Time.deltaTime);
        if (hpBar != null && battle.staggerMax > 0f) hpBar.SetSub(battle.staggerMax > 0f ? stagger / battle.staggerMax : 0f, state == State.Stunned);
        if (state == State.Stunned) StunFollow();
    }

    void CheckBattlePhase()
    {
        if (battle == null || battle.phaseThresholds == null || phase > battle.phaseThresholds.Length) return;
        float f = (float)Hp / Mathf.Max(1, maxHp);
        int p = 1;
        foreach (float th in battle.phaseThresholds) if (f <= th) p++;
        if (p <= phase) return;
        phase = p;
        phaseAt = Time.time;
        attackIntervalMin *= 0.7f; attackIntervalMax *= 0.7f;
        landingAttackChance = Mathf.Min(0.5f, landingAttackChance + 0.1f);
        Debug.Log($"[BossBattle] Dragon PHASE {phase} (hp {Hp}/{maxHp})");
        if (hpBar != null) hpBar.Flash(0.8f);
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.22f, 0.4f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position, new Color(1f, 0.4f, 0.2f, 0.95f), 0.5f, 1f, 6f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        BossBattleHud.Banner("最終段階!", new Color(1f, 0.4f, 0.25f), 1.2f);
    }

    void AddStagger(float v)
    {
        if (BossBattle.DebugNoStagger) return;
        if (battle == null || battle.staggerMax <= 0f || state == State.Stunned || state == State.Entering) return;
        stagger += v * staggerMul;
        lastStaggerTime = Time.time;
        if (stagger < battle.staggerMax) return;
        stagger = battle.staggerMax;
        BreakCount++;
        Debug.Log($"[BossBattle] Dragon BREAK #{BreakCount}");
        BossBattleHud.Banner("BREAK!", new Color(1f, 0.85f, 0.3f), 1.0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossFinalHit);
        StopAllCoroutines();
        if (flashOverlay != null) flashOverlay.enabled = false;
        if (hitFlashOverlay != null) hitFlashOverlay.enabled = false;
        if (chargeOrb != null) chargeOrb.enabled = false;
        BossBattle.EndUltimate(this);
        ultimateRunningFlag = false;
        state = State.Stunned;
        SetFrames(idleFrames);
        StartCoroutine(StunRoutine());
    }

    float stunY;
    void StunFollow()
    {
        Vector3 home = ComputeHomePosition();
        float x = home.x;
        float gy = GroundYAt(x) + hoverHeight * 0.45f;
        stunY = Mathf.MoveTowards(transform.position.y, gy, 9f * Time.deltaTime);
        transform.position = new Vector3(Mathf.MoveTowards(transform.position.x, Mathf.Min(x, player != null ? player.position.x + 4f : x), 14f * Time.deltaTime), stunY, 0f);
    }

    IEnumerator StunRoutine()
    {
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.18f, 0.25f);
        float t = 0f, star = 0f;
        float dur = battle != null ? battle.breakDuration : 3f;
        while (t < dur)
        {
            t += Time.deltaTime; star -= Time.deltaTime;
            if (star <= 0f)
            {
                star = 0.25f;
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), 0f), new Color(1f, 0.95f, 0.45f, 1f), 0.4f, 0.25f, 0.05f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            }
            yield return null;
        }
        stagger = 0f;
        state = State.Landing; // 戻りの間は通常の追従をしない(体当たり判定の無い状態)
        yield return ReturnToHome(0.6f);
        state = State.Idle;
        ScheduleNextAttack();
    }

    IEnumerator GiantFireball()
    {
        state = State.Telegraphing;
        UltimatesUsed++;
        lastUltimateTime = Time.time;
        string ultName = skyMode ? "DRAGON FIRE CHARGE" : "煉獄の巨大火球";
        Debug.Log($"[BossBattle] Dragon ULTIMATE '{ultName}' #{UltimatesUsed}");
        BossBattleHud.Banner(ultName, new Color(1f, 0.45f, 0.15f), 1.6f);
        ultimateRunningFlag = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        // 上空へ
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            Vector3 target = ComputeHomePosition() + new Vector3(1.5f, 1.0f, 0f);
            transform.position = Vector3.Lerp(transform.position, target, Mathf.Clamp01(t / 0.6f));
            yield return null;
        }
        // 巨大な火球を溜める
        if (chargeOrb == null)
        {
            var go = new GameObject("GiantCharge");
            go.transform.SetParent(transform, false);
            chargeOrb = go.AddComponent<SpriteRenderer>();
            chargeOrb.sprite = FireballController.FireballArt() != null ? FireballController.FireballArt() : BossFx.Orb();
            chargeOrb.sortingOrder = sr.sortingOrder + 2;
        }
        chargeOrb.enabled = true;
        SetFrames(fireFrames);
        t = 0f;
        Vector3 mouth = Vector3.Scale((Vector3)fireballSpawnOffset, transform.lossyScale);
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            transform.position = ComputeHomePosition() + new Vector3(1.5f, 1.0f, 0f);
            float k = Mathf.Clamp01(t / 1.5f);
            chargeOrb.transform.position = transform.position + mouth + new Vector3(-0.6f, 0f, 0f);
            float s = Mathf.Lerp(0.3f, 2.4f, k) * (1f + 0.08f * Mathf.Sin(t * 30f));
            chargeOrb.transform.localScale = new Vector3(s / Mathf.Max(0.01f, transform.lossyScale.x), s / Mathf.Max(0.01f, transform.lossyScale.y), 1f);
            chargeOrb.color = Color.Lerp(new Color(1f, 0.7f, 0.2f), new Color(1f, 0.3f, 0.1f), Mathf.PingPong(t * 4f, 1f));
            if (flashOverlay != null) flashOverlay.enabled = Mathf.PingPong(t * 6f, 1f) > 0.5f;
            yield return null;
        }
        if (flashOverlay != null) flashOverlay.enabled = false;
        Vector3 from = chargeOrb.transform.position;
        chargeOrb.enabled = false;
        // 発射: ゆっくり迫る巨大火球(かわす or 跳ね返す)
        Vector3 aim = player != null ? player.position + new Vector3(0f, 0.6f, 0f) : from + Vector3.left;
        Vector2 dir = ((Vector2)(aim - from)).normalized;
        GameObject fbGo = FireballController.Create(squareSprite, from, dir * 6.5f);
        var fbc = fbGo.GetComponent<FireballController>();
        fbc.bossOwned = true;
        fbc.ScaleUp(3f);
        fbc.damageAmount = CombatScale.PlayerHeavyHit;
        fbc.lifetime = 7f;
        var camF = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camF != null) camF.Shake(0.15f, 0.3f);
        state = State.Firing;
        yield return new WaitForSeconds(2.2f);
        BossBattle.EndUltimate(this);
        ultimateRunningFlag = false;
        if (skyMode) yield return LowRecovery(2.8f); // 天空: 低空へ降りる(反撃のチャンス)
        state = State.Landing;
        yield return ReturnToHome(0.6f);
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    // 天空ボス強化(2026-10-05): DRAGON FIRE CHARGE の後、低空へ降りてくる(崩れやすい/攻撃が届く)
    bool ultimateRunningFlag;
    IEnumerator LowRecovery(float seconds)
    {
        state = State.Landing;
        Vector3 start = transform.position;
        float halfHWorld = sr.sprite != null ? sr.sprite.bounds.extents.y * transform.lossyScale.y : 1.5f;
        float t = 0f;
        Debug.Log("[CaveBoss] Dragon RECOVERY 'DRAGON FIRE CHARGE'");
        BossBattleHud.Banner("ドラゴンが低空へ! 反撃のチャンス!", new Color(0.6f, 1f, 0.6f), 1.2f);
        staggerMul = 2f;
        while (t < seconds && state != State.Dead)
        {
            t += Time.deltaTime;
            float x = (player != null ? player.position.x : transform.position.x) + landedStandoffDistance;
            Vector3 target = new Vector3(x, GroundYAt(x) + halfHWorld * 0.95f, 0f);
            transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.5f)));
            if (t > 0.5f) start = target;
            if (sr != null) sr.color = Color.Lerp(Color.white, new Color(0.75f, 0.85f, 1f), 0.5f + 0.5f * Mathf.Sin(t * 5f));
            yield return null;
        }
        staggerMul = 1f;
        if (sr != null) sr.color = Color.white;
    }

    public string DebugName => "Dragon";
    public bool DebugAlive => state != State.Dead && isActiveAndEnabled;
    public int PhaseCount => (battle != null && battle.phaseThresholds != null ? battle.phaseThresholds.Length : 0) + 1;
    public bool UltimateRunning => ultimateRunningFlag;
    public float StaggerFraction => battle != null && battle.staggerMax > 0f ? Mathf.Clamp01(stagger / battle.staggerMax) : 0f;
    public void DebugSetPhase(int p)
    {
        if (battle == null || state == State.Dead) return;
        p = Mathf.Clamp(p, 1, PhaseCount);
        float frac = p == 1 ? 1f : battle.phaseThresholds[p - 2] - 0.03f;
        Hp = Mathf.Clamp(Mathf.FloorToInt(maxHp * frac), 1, maxHp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp));
        if (p < phase) phase = p; else CheckBattlePhase();
    }
    public bool DebugForceUltimate()
    {
        if (battle == null || state == State.Dead || battle.ultimateCooldown <= 0f) return false;
        if (phase < 2) DebugSetPhase(2);
        if (state == State.Stunned || state == State.Entering) return false;
        StopAllCoroutines();
        if (flashOverlay != null) flashOverlay.enabled = false;
        if (chargeOrb != null) chargeOrb.enabled = false;
        if (ultimateRunningFlag) { ultimateRunningFlag = false; BossBattle.EndUltimate(this); }
        staggerMul = 1f;
        if (sr != null) sr.color = Color.white;
        lastUltimateTime = -99f; phaseAt = Time.time - 99f;
        BossBattle.LastUltimateEnd = -99f;
        BossStaggerGate.NextDragonTime = 0f;
        state = State.Idle;
        nextAttackTime = Time.time;
        return true;
    }
    public void DebugForceBreak() { if (battle != null && battle.staggerMax > 0f) { bool keep = BossBattle.DebugNoStagger; BossBattle.DebugNoStagger = false; AddStagger(battle.staggerMax * 1.5f / Mathf.Max(0.1f, staggerMul)); BossBattle.DebugNoStagger = keep; } }

    // Brief red flash to signal "that hit landed" while the boss is still
    // alive - a separate overlay from the (white) attack telegraph so the
    // two never fight over the same renderer if their timing overlaps.
    IEnumerator HitFlash()
    {
        if (hitFlashOverlay == null) yield break;
        hitFlashOverlay.enabled = true;
        yield return new WaitForSeconds(0.15f);
        hitFlashOverlay.enabled = false;
    }

    // Boss Defeat Presentation pass - replaces the old instant Die()
    // (StopAllCoroutines + immediate SetActive(false)) with a short,
    // visible sequence: Final Hit emphasis -> Death flash/scale/fade+smoke
    // -> HP Bar empty-hold-then-fade -> only THEN deactivate and notify
    // GameManager/BossManager. state is already Dead by the time this
    // starts (set in TakeDamage), so every other coroutine on this object
    // (attack scheduling, telegraph blink, idle bob via Update) is already
    // inert - StopAllCoroutines is no longer called here, since this
    // coroutine itself needs to keep running.
    // Bugfix 2026-09-07 (Bug #001, root cause) - this coroutine used to have
    // NO exception/early-exit protection at all around the two calls at its
    // very end (RegisterBossDefeat/OnDragonDefeated) - if ANYTHING threw
    // partway through (or this GameObject got disabled/destroyed - e.g. a
    // scene transition, GAME OVER racing the same frame), those two calls
    // would simply never run. Since BossManager.CheckEncounterComplete
    // (called from OnDragonDefeated) is the ONLY thing that ever calls
    // GameManager.TriggerBossRewardChoice - which is in turn the ONLY thing
    // that starts the bossRewardStuckTimer/pendingChoiceStuckTimer safety
    // nets - a failure here meant NONE of the existing timeouts would ever
    // even begin counting. IsBossPhase (and therefore Distance/Enemy Spawn)
    // would then stay frozen forever with no recovery path whatsoever, the
    // single most severe gap found while investigating Bug #001. Wrapped in
    // try/finally (RegisterDefeatOnce guards against calling it twice - once
    // normally at the end of try, once as the fail-safe in finally) so the
    // reward pipeline is now GUARANTEED to be reached exactly once no matter
    // what happens above it. Also switched every WaitForSeconds/Time.deltaTime
    // in this coroutine to WaitForSecondsRealtime/Time.unscaledDeltaTime -
    // this sequence starts the instant the boss's HP hits 0, independent of
    // any OTHER system that might be holding Time.timeScale at 0 at that
    // exact moment (e.g. the Pause Menu, or a concurrent HitStop) - it must
    // never be at the mercy of an unrelated pause to even START the reward
    // pipeline.
    bool bossDefeatRegistered;
    void RegisterDefeatOnce()
    {
        if (bossDefeatRegistered) return;
        bossDefeatRegistered = true;
        if (NetPuppet) return; // JOINのパペット: 撃破報酬/ボス戦終了はHOSTとラストヒットの本人が処理する
        if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        if (BossManager.Instance != null) BossManager.Instance.OnDragonDefeated();
    }

    IEnumerator FinalHitAndDie()
    {
        try
        {
            if (landHitbox != null) landHitbox.Deactivate();
            if (biteHitbox != null) biteHitbox.Deactivate();
            if (flashOverlay != null) flashOverlay.enabled = false;
            if (hitFlashOverlay != null) hitFlashOverlay.enabled = false;

            // ===== Item 1 - Final Hit: a slightly longer Hit Stop, a boosted
            // Hit Spark, and a stronger Camera Shake than a normal hit. Only
            // the camera and a separate one-shot VFX object are touched - this
            // GameObject's own Collider/Rigidbody are untouched here. =====
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(finalHitSe);
            var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (camFollow != null) camFollow.Shake(finalHitShakeStrength, finalHitShakeDuration);
            Sprite spark = finalHitSparkSprite != null ? finalHitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, transform.position, Color.white, duration: 0.18f, startScale: finalHitSparkScale * 0.7f, endScale: finalHitSparkScale, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.2f);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Boss defeated");

            yield return HitStop.Freeze(finalHitStopDuration);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Final hit presentation");

            // ===== Item 2 - Death Presentation: flash (white->blue/cyan) ->
            // scale punch+fade (1.0->1.05->0.9, Alpha->0) -> a Death Smoke
            // accent, sized up from a regular enemy's own (Boss用は少し大き
            // く). Collider scaling along with this is safe now - state is
            // already Dead, so it can neither deal nor take any further
            // damage regardless of its current size. =====
            if (flashOverlay != null)
            {
                flashOverlay.sprite = sr.sprite;
                flashOverlay.color = bossDeathFlashColor;
                flashOverlay.enabled = true;
            }
            yield return new WaitForSecondsRealtime(bossDeathFlashDuration);
            if (flashOverlay != null) flashOverlay.enabled = false;

            Vector3 baseScale = transform.localScale;
            Color startColor = sr.color;
            float punchDuration = bossDeathDuration * 0.3f;
            float settleDuration = Mathf.Max(0.05f, bossDeathDuration - punchDuration);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, punchDuration);
                transform.localScale = baseScale * Mathf.Lerp(1f, bossDeathPunchScale, Mathf.Clamp01(t));
                yield return null;
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, settleDuration);
                float f = Mathf.Clamp01(t);
                transform.localScale = baseScale * Mathf.Lerp(bossDeathPunchScale, bossDeathFinalScale, f);
                Color c = startColor; c.a = Mathf.Lerp(startColor.a, 0f, f);
                sr.color = c;
                yield return null;
            }

            if (bossDeathSmokeSprite != null)
            {
                OneShotSpriteEffect.CreateTweened(bossDeathSmokeSprite, transform.position, Color.white, duration: 0.4f, startScale: bossDeathSmokeScale * 0.7f, endScale: bossDeathSmokeScale, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.3f);
            }

            // ボス撃破時の飛散パーティクル(2026-09-10) - ボスのワールド高さ
            // (sr.boundsはlossyScale込み)で数/粒サイズ/速度をスケール。色は
            // defeatBurstColor(BossManagerが通常=赤/機械龍=黄で設定)。
            if (defeatBurstEnabled)
            {
                float subjectHeight = sr != null ? sr.bounds.size.y : 6f;
                ExplosionEffect.CreateForDefeat(transform.position, defeatBurstColor, subjectHeight, sortingOrder: RenderOrder.CombatFx);
            }

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(bossDefeatSe);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Death presentation");

            // ===== Item 3 - Boss HP Bar: sit at empty a moment (already 0 from
            // TakeDamage's SetFraction above) before fading out. =====
            if (hpBar != null)
            {
                yield return new WaitForSecondsRealtime(hpBarEmptyHoldDuration);
                yield return hpBar.FadeOutRoutine(bossHpBarFadeDuration);
                Destroy(hpBar.gameObject);
            }

            gameObject.SetActive(false);
            RegisterDefeatOnce();
        }
        finally
        {
            RegisterDefeatOnce(); // no-op if already done above - guarantees the Boss Reward pipeline is always reached even on an early exit/exception
        }
    }

    // ===================================================================== //
    // マルチプレイPhase 2(共有ボス)
    // ===================================================================== //
    [System.NonSerialized] public int NetId;
    [System.NonSerialized] public bool NetPuppet;

    // マルチプレイPhase 2.5: HOSTのAIが狙う相手(全ての活動中プレイヤーから選ばれる)。並走の基準速度もその相手。
    EnemyTargetSelector netTarget;
    float leashTime; // マルチ Phase 3.1: 置き去り防止の減速が続いている秒数(BossLeash)
    public void NetSetTarget(Transform t, EnemyTargetSelector selector) { if (t != null) player = t; netTarget = selector; }
    float TargetBaseSpeed() => netTarget != null ? netTarget.TargetRunSpeed() : (playerController != null ? playerController.CurrentAutoRunSpeed : 0f);
    int netAttacker; // 0 = この端末のプレイヤー
    int netFramesSet = -1;
    Collider2D netLastHitCollider;
    float netHitCooldown;

    byte CurrentFramesSet()
    {
        if (currentFrames == idleFrames) return 0;
        if (currentFrames == chargeFrames) return 1;
        if (currentFrames == fireFrames) return 2;
        return 0;
    }

    // HOST: JOINのプレイヤーの攻撃を、この端末の攻撃と同じ被弾処理へ流す。
    public void NetApplyRemoteHit(int attacker, int damage)
    {
        if (state == State.Dead || NetPuppet) return;
        netAttacker = attacker;
        try { TakeDamage(damage); }
        finally { netAttacker = 0; }
    }

    public void NetMakePuppet(int id, int hp, int maxHpValue)
    {
        NetId = id;
        NetPuppet = true;
        maxHp = Mathf.Max(1, maxHpValue);
        Hp = hp;
        StopAllCoroutines();
        if (hpBar != null) { hpBar.SetFraction((float)Hp / maxHp); hpBar.SetHidden(); }
    }

    void NetPuppetHit(Collider2D other)
    {
        if (other == netLastHitCollider && netHitCooldown > Time.time) return;
        netLastHitCollider = other;
        netHitCooldown = Time.time + 0.18f;
        int damage = PlayerAttackInfo.ScaleDamage(other, this, PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamage);
        PlayerAttackKind kind = PlayerAttackKind.Normal;
        var info = other.GetComponent<PlayerAttackInfo>();
        if (info != null) kind = info.kind;
        NetCombat.RequestHit(NetId, damage, kind, transform.position);
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.08f, 0.1f);
        StartCoroutine(HitFlash());
    }

    public void NetSetHp(int hp, bool showHitFx)
    {
        if (state == State.Dead) return;
        Hp = Mathf.Max(0, hp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp));
        if (showHitFx && isActiveAndEnabled) StartCoroutine(HitFlash());
    }

    public void NetPuppetDie()
    {
        if (state == State.Dead) return;
        Hp = 0;
        if (hpBar != null) hpBar.SetFraction(0f);
        StopAllCoroutines();
        state = State.Dead;
        if (BossFinish.Enabled) { BeginBossFinish(true); return; }
        StartCoroutine(FinalHitAndDie());
    }

    public void NetCaptureVisual(ref NetCombat.State s)
    {
        s.FramesSet = CurrentFramesSet();
        s.Pose = (byte)state;
        if (state != State.Entering) s.Flags |= NetCombat.FlagHpBar;
        if (sr != null) s.Color = NetPlayerSnapshot.PackColor(sr.color);
        if (flashOverlay != null && flashOverlay.enabled) { s.Flags |= NetCombat.FlagFlash; s.Flash = NetPlayerSnapshot.PackColor(flashOverlay.color); }
        if (hitFlashOverlay != null && hitFlashOverlay.enabled) s.Flags |= NetCombat.FlagHitFlash;
    }

    bool netHpRevealed;
    byte netPose;
    public void NetApplyVisual(NetCombat.State s)
    {
        if (state == State.Dead) return;
        netPose = s.Pose;
        if (s.FramesSet != netFramesSet)
        {
            netFramesSet = s.FramesSet;
            switch (s.FramesSet)
            {
            case 0: SetFrames(idleFrames); break;
            case 1: SetFrames(chargeFrames); break;
            case 2: SetFrames(fireFrames); break;
            }
        }
        if (sr != null) sr.color = NetPlayerSnapshot.UnpackColor(s.Color);
        if (flashOverlay != null)
        {
            bool on = (s.Flags & NetCombat.FlagFlash) != 0;
            flashOverlay.enabled = on;
            if (on) { flashOverlay.color = NetPlayerSnapshot.UnpackColor(s.Flash); flashOverlay.sprite = sr.sprite; }
        }
        if (!netHpRevealed && (s.Flags & NetCombat.FlagHpBar) != 0)
        {
            netHpRevealed = true;
            if (hpBar != null) StartCoroutine(hpBar.RevealRoutine(0.25f));
        }
    }
}

// 天空回廊ボス追加(2026-09-25) - ドラゴン/魔人が複数体いる時に攻撃開始をずらす共有ゲート。
public static class BossStaggerGate
{
    public static float NextDragonTime;
    public static float DragonInterval = 1.3f;
    public static float NextMajinTime;
    public static float MajinInterval = 1.8f;
}

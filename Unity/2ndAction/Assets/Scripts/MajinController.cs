using System.Collections;
using UnityEngine;

// A larger, tougher boss than the dragon - no charge/dive attack, just 5
// fireball patterns, and it wanders up/down/left/right around its tracked
// standoff spot instead of holding a fixed line (see ComputeHomePosition).
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public partial class MajinController : MonoBehaviour
{
    enum State { Entering, Idle, Telegraphing, Firing, Dead, Exposed, Special } // Special: 天空ボス強化(2026-10-05)の瞬間移動/必殺技(追従の代わりに自分で位置を決める)

    // 近接キャラの反撃の時間(2026-10-03)。以前は常にプレイヤーの約10〜18m前を漂い、近接キャラは跳ね返した火球でしか
    // 攻撃できなかった(カードなしで撃破まで10〜33分の見込み)。exposeEveryAttacks 回の攻撃ごとに、攻撃の後で
    // 体の手前の端がプレイヤーの exposeReach m先に来る所・地面近くまで降りてきて exposeHoldTime 秒とどまる(暗い色=今は撃ってこない)。HPは変えていない。
    [Header("近接の反撃の時間(2026-10-03)")]
    public int exposeEveryAttacks = 2;
    public float exposeReach = 0.8f; // 体の手前の端までの距離(間合いの短いお嬢様騎士でも届く)
    public float exposeClearance = 0.25f;
    public float exposeDescendTime = 0.7f;
    public float exposeHoldTime = 2.8f;
    public float exposeReturnTime = 0.8f;
    int attacksSinceExpose;
    public int ExposeCount { get; private set; } // 確認用
    public bool IsExposed => state == State.Exposed;

    [Header("Animation")]
    public Sprite[] idleFrames;
    public Sprite[] attackFrames;
    public float animFps = 10f;

    [Header("Health / Damage")]
    public int maxHp = 1200;
    public int playerAttackDamage = 20; // 10倍スケール
    public int fireballDamage = 20; // 10倍スケール
    // Reward/MILE System Ver.1 - "ボスMILE: Demon/魔人 100".
    public int mileReward = 100;

    [Header("Behaviour Timing")]
    public float attackIntervalMin = 1.8f;
    public float attackIntervalMax = 3.5f;

    [Header("Attack Telegraph")]
    public float telegraphDuration = 3f;
    public float telegraphBlinkInterval = 0.3f;

    public Color recoveryTint = new Color(0.72f, 0.66f, 0.8f, 1f);

    [Header("Fire Attack")]
    public float fireWindupDuration = 0.5f;
    public float fireRecoverDuration = 0.6f;
    // 弾速の走行補正(2026-09-26) - 弾は走行速度で流れる座標系の中を飛ぶようになったため(PlayerController.
    // RunFrameSpeed)、正面から迫る見た目の速さが従来(=走行速度ぶん上乗せ)より遅くならないよう底上げ。
    public float fireballSpeed = 9f;
    public Vector2 fireballSpawnOffset = new Vector2(-1.4f, 0.2f);
    public float fireballInterval = 0.2f;

    [Header("Fire Attack Patterns")]
    // 5 patterns, picked uniformly at random each attack: a straight line of
    // 5, a straight line of 10, a 12-fireball ring that pauses then flies at
    // the player, a 12-fireball ring that pauses then scatters outward, and
    // a 24-fireball ring that pauses then scatters outward.
    public int lineCountSmall = 5;
    public int lineCountLarge = 10;
    public int ringCountSmall = 12;
    public int ringCountLarge = 24;
    public float ringRadius = 2.5f;
    public float ringHoldDuration = 1f;

    [Header("Positioning")]
    public float groundClearance = 0.8f;
    public float standoffDistance = 14f;
    public float maxAheadOfPlayer = 24f;
    public float maxBehindPlayer = 12f;

    [Header("Idle Bob (vertical)")]
    public float bobAmplitude = 1.2f;
    public float bobSpeedMin = 0.4f;
    public float bobSpeedMax = 1.1f;

    [Header("Roaming (horizontal drift on top of the tracked standoff spot)")]
    // Together with the vertical bob above, this is what makes the majin
    // wander up/down/left/right around its spot instead of holding a flat
    // line like the dragon does - the underlying distance-tracking
    // (AdvanceTrackedX/standoffDistance) is otherwise identical to it.
    public float roamAmplitudeX = 3.5f;
    public float roamSpeedXMin = 0.25f;
    public float roamSpeedXMax = 0.6f;

    [Header("Entrance")]
    public float entranceDuration = 1.4f;
    public float entranceOffscreenMargin = 3f;

    // Boss Milestone Presentation pass - see DragonController's matching
    // fields/comment (Scale Emphasis deliberately skipped - no separate
    // Root/Visual split on this GameObject either).
    [Header("Boss Milestone Presentation - Arrival")]
    public float hpBarRevealDuration = 0.25f;
    public float arrivalShakeMagnitude = 0.06f;
    public float arrivalShakeDuration = 0.15f;

    // Boss Defeat Presentation pass - see DragonController's matching
    // fields/comments.
    [Header("Boss Defeat Presentation - Final Hit")]
    public float finalHitStopDuration = 0.14f;
    public float finalHitShakeStrength = 0.14f;
    public float finalHitShakeDuration = 0.16f;
    public Sprite finalHitSparkSprite;
    public float finalHitSparkScale = 0.85f;
    public AudioClip finalHitSe;

    [Header("Boss Defeat Presentation - Death")]
    public float bossDeathFlashDuration = 0.08f;
    public Color bossDeathFlashColor = new Color(0.7f, 0.9f, 1f, 1f);
    public float bossDeathDuration = 0.6f;
    public float bossDeathPunchScale = 1.05f;
    public float bossDeathFinalScale = 0.9f;
    public Sprite bossDeathSmokeSprite;
    public float bossDeathSmokeScale = 1.2f;
    public AudioClip bossDefeatSe;
    // ボス撃破時の飛散パーティクル(2026-09-10) - 魔人は紫。数/サイズ/速度は
    // ExplosionEffect.CreateForDefeatが実寸から算出。
    public bool defeatBurstEnabled = true;
    public Color defeatBurstColor = new Color(0.7f, 0.35f, 1f, 1f);

    [Header("Boss Defeat Presentation - HP Bar")]
    public float hpBarEmptyHoldDuration = 0.15f;
    public float bossHpBarFadeDuration = 0.25f;

    [Header("Refs")]
    public Sprite squareSprite;
    public Transform player;
    public Color explosionColor = new Color(0.6f, 0.1f, 0.7f);

    public int Hp { get; private set; }
    public bool IsDead => state == State.Dead;

    State state = State.Idle;
    SpriteRenderer sr;
    SpriteRenderer flashOverlay;
    SpriteRenderer hitFlashOverlay;
    PlayerController playerController;
    Vector3 homePos;
    float hoverHeight;
    float nextAttackTime;
    int frameIndex;
    float frameTimer;
    Sprite[] currentFrames;
    DragonHealthBar hpBar;
    float bobSeed;
    float bobSpeed;
    float roamSeedX;
    float roamSpeedX;

    float trackedX;

    // Floating Origin: 座標を戻した分、追従基準X/待機位置も戻す。
    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }
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
        roamSeedX = Random.Range(0f, 1000f);
        roamSpeedX = Random.Range(roamSpeedXMin, roamSpeedXMax);

        homePos = ComputeHomePosition();
        transform.position = ComputeOffscreenEntryPosition(homePos);

        Hp = maxHp;

        hpBar = DragonHealthBar.Create(squareSprite, transform, 3.2f, 0.28f);
        hpBar.offset = new Vector3(0f, hoverHeight * 0.5f + 0.7f, 0f);
        hpBar.SetHidden();

        state = State.Entering;
        // マルチプレイPhase 2 - HOSTでは共有ボスとして登録。JOINでパペットとして作っている時はAIを始めない。
        if (NetCombat.OnBossInit(this)) return;
        StartCoroutine(EnterThenSchedule());
    }

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

    // Boss Milestone Presentation pass - see DragonController's matching
    // method comment.
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

    void RecomputeVerticalOffsets()
    {
        float halfHeight = 1.8f;
        if (sr != null && sr.sprite != null)
        {
            halfHeight = sr.sprite.bounds.extents.y * transform.lossyScale.y;
        }
        hoverHeight = groundClearance + halfHeight;
    }

    void Update()
    {
        if (NetPuppet) { if (state != State.Dead) AnimateSprite(); return; }
        if (state == State.Dead) return;

        AnimateSprite();
        AdvanceTrackedX();
        BattleTick(); // 天空ボス強化: 崩しの回復/ゲージ

        if (state == State.Idle || state == State.Telegraphing || state == State.Firing)
        {
            ApplyHomePosition();
        }

        if (state == State.Idle && Time.time >= nextAttackTime)
        {
            // 天空回廊ボス追加(2026-09-25) - 複数体の魔人が同時に火球を撃たないようずらす。
            if (Time.time < BossStaggerGate.NextMajinTime)
            {
                nextAttackTime = BossStaggerGate.NextMajinTime + Random.Range(0.1f, 0.6f);
                return;
            }
            BossStaggerGate.NextMajinTime = Time.time + BossStaggerGate.MajinInterval;
            if (BattleTryStart()) return; // 天空ボス強化: 必殺技/第2段階の技
            StartCoroutine(TelegraphAndAttack());
        }
    }

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

    Vector3 ComputeHomePosition()
    {
        float roamOffsetX = (Mathf.PerlinNoise(Time.time * roamSpeedX + roamSeedX, 0f) * 2f - 1f) * roamAmplitudeX;
        float x = trackedX + standoffDistance + roamOffsetX;
        float groundY = GroundYAt(x);
        float bobOffset = (Mathf.PerlinNoise(Time.time * bobSpeed + bobSeed, 0f) * 2f - 1f) * bobAmplitude;
        return new Vector3(x, groundY + hoverHeight + bobOffset, 0f);
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

    IEnumerator TelegraphAndAttack()
    {
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

        if (state != State.Telegraphing) yield break; // died mid-telegraph

        yield return FireAttack();
    }

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

    enum FirePattern { Line5, Line10, Ring12ToPlayer, Ring12Scatter, Ring24Scatter }

    IEnumerator FireAttack()
    {
        state = State.Firing;
        SetFrames(attackFrames);

        yield return new WaitForSeconds(fireWindupDuration);

        FirePattern pattern = (FirePattern)Random.Range(0, 5);
        switch (pattern)
        {
            case FirePattern.Line5:
                yield return FireLine(lineCountSmall);
                break;
            case FirePattern.Line10:
                yield return FireLine(lineCountLarge);
                break;
            case FirePattern.Ring12ToPlayer:
                SpawnRing(ringCountSmall, towardPlayer: true);
                yield return new WaitForSeconds(ringHoldDuration + 0.15f);
                break;
            case FirePattern.Ring12Scatter:
                SpawnRing(ringCountSmall, towardPlayer: false);
                yield return new WaitForSeconds(ringHoldDuration + 0.15f);
                break;
            case FirePattern.Ring24Scatter:
                SpawnRing(ringCountLarge, towardPlayer: false);
                yield return new WaitForSeconds(ringHoldDuration + 0.15f);
                break;
        }

        // 天空回廊ボス追加(2026-09-25) - Recovery(攻撃後の隙)を視覚的に区別する:
        // 待機コマへ戻し、少し暗く沈んだ色で「今は撃ってこない」ことを見せる。
        SetFrames(idleFrames);
        if (sr != null && state == State.Firing) sr.color = recoveryTint;
        yield return new WaitForSeconds(fireRecoverDuration);
        if (sr != null && state == State.Firing) sr.color = Color.white;
        if (state != State.Firing) yield break; // 撃破された

        attacksSinceExpose++;
        if (exposeEveryAttacks > 0 && attacksSinceExpose >= exposeEveryAttacks && player != null)
        {
            attacksSinceExpose = 0;
            yield return DescendExposed();
            if (state == State.Dead) yield break;
        }

        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    Vector3 ExposedPosition()
    {
        var box = GetComponent<BoxCollider2D>();
        float halfW = box != null ? box.bounds.extents.x : 1.2f;
        float x = player.position.x + exposeReach + halfW;
        float halfHeight = Mathf.Max(0.3f, hoverHeight - groundClearance);
        return new Vector3(x, GroundYAt(x) + exposeClearance + halfHeight, 0f);
    }

    IEnumerator DescendExposed()
    {
        state = State.Exposed;
        ExposeCount++;
        SetFrames(idleFrames);
        if (sr != null) sr.color = recoveryTint;
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f && state == State.Exposed && player != null)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, exposeDescendTime);
            transform.position = Vector3.Lerp(start, ExposedPosition(), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        float h = 0f;
        while (h < exposeHoldTime && state == State.Exposed && player != null)
        {
            transform.position = ExposedPosition();
            h += Time.deltaTime;
            yield return null;
        }
        if (state != State.Exposed) yield break;
        if (sr != null) sr.color = Color.white;
        yield return ReturnToHome(exposeReturnTime);
    }

    IEnumerator FireLine(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (state != State.Firing) yield break;
            SpawnFireball(0f, 0f);
            if (i < count - 1) yield return new WaitForSeconds(fireballInterval);
        }
    }

    // A ring of `count` fireballs placed evenly around the majin, held in
    // place for ringHoldDuration, then launched either all straight at the
    // player (towardPlayer) or scattered outward radially from the ring.
    void SpawnRing(int count, bool towardPlayer)
    {
        if (squareSprite == null || count <= 0) return;

        Vector3 center = transform.position;
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
            Vector3 spawnPos = center + (Vector3)offset;

            Vector2 dir = towardPlayer && player != null
                ? ((Vector2)player.position - (Vector2)spawnPos).normalized
                : offset.normalized;

            FireballController.Create(squareSprite, spawnPos, dir * fireballSpeed, ringHoldDuration);
        }
    }

    void SpawnFireball(float angleOffsetDegrees, float holdDuration)
    {
        if (player == null || squareSprite == null) return;

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

        FireballController.Create(squareSprite, spawnPos, dir * fireballSpeed, holdDuration);
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

        if (other.CompareTag("PlayerAttack"))
        {
            int damage = PlayerAttackInfo.ScaleDamage(other, this, PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamage);
            var info = other.GetComponent<PlayerAttackInfo>();
            bool air = PlayerController.Instance != null && !PlayerController.Instance.IsGrounded;
            pendingStagger = BossBattleTuning.I.StaggerFor(info != null ? info.kind : PlayerAttackKind.Normal, air); // 天空ボス強化: 崩し
            TakeDamage(damage);
            return;
        }

        FireballController fb = other.GetComponent<FireballController>();
        if (fb != null && fb.reflected)
        {
            pendingStagger = BossBattleTuning.I.staggerReflect;
            TakeDamage(fireballDamage);
            Destroy(fb.gameObject);
        }
    }

    public void TakeDamage(int amount)
    {
        if (state == State.Dead || NetPuppet) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit); // ボス被弾(共通、連打は間引き)
        amount = ScaleIncoming(amount); // 天空ボス強化: BREAK/硬直中は大きく入る

        Hp = Mathf.Max(0, Hp - amount);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);
        NetCombat.AuthorityDamaged(NetId, netAttacker, amount, Hp, 0, transform.position, Hp <= 0);

        if (Hp <= 0)
        {
            state = State.Dead;
            if (UltimateRunning) { UltimateRunning = false; BossBattle.EndUltimate(this); }
            if (hpBar != null) hpBar.SetSub(0f, false);
            StartCoroutine(FinalHitAndDie());
            return;
        }
        BattleOnDamaged();

        // Game Feel pass, section 19 - see DragonController.TakeDamage's
        // matching comment.
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.08f, 0.1f);
        StartCoroutine(HitFlash());
    }

    IEnumerator HitFlash()
    {
        if (hitFlashOverlay == null) yield break;
        hitFlashOverlay.enabled = true;
        yield return new WaitForSeconds(0.15f);
        hitFlashOverlay.enabled = false;
    }

    // Boss Defeat Presentation pass - see DragonController.FinalHitAndDie's
    // matching comment.
    // Bugfix 2026-09-07 (Bug #001, root cause) - see DragonController.
    // FinalHitAndDie's matching comment for the full reasoning (identical
    // gap existed here too - no exception/early-exit protection around the
    // reward-pipeline calls at the end, and scaled WaitForSeconds/
    // Time.deltaTime that an unrelated external pause could stall
    // indefinitely before the reward pipeline even starts).
    bool bossDefeatRegistered;
    void RegisterDefeatOnce()
    {
        if (bossDefeatRegistered) return;
        bossDefeatRegistered = true;
        if (NetPuppet) return; // JOINのパペット: 撃破報酬/ボス戦終了はHOSTとラストヒットの本人が処理する
        if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        if (BossManager.Instance != null) BossManager.Instance.OnMajinDefeated();
    }

    IEnumerator FinalHitAndDie()
    {
        try
        {
            if (flashOverlay != null) flashOverlay.enabled = false;
            if (hitFlashOverlay != null) hitFlashOverlay.enabled = false;

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(finalHitSe);
            var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (camFollow != null) camFollow.Shake(finalHitShakeStrength, finalHitShakeDuration);
            Sprite spark = finalHitSparkSprite != null ? finalHitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, transform.position, Color.white, duration: 0.18f, startScale: finalHitSparkScale * 0.7f, endScale: finalHitSparkScale, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.2f);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Boss defeated");

            yield return HitStop.Freeze(finalHitStopDuration);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Final hit presentation");

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

            // ボス撃破時の飛散パーティクル(2026-09-10) - 魔人は紫
            // (defeatBurstColor)。数/粒サイズ/速度は魔人のワールド高さで
            // スケール。
            if (defeatBurstEnabled)
            {
                float subjectHeight = sr != null ? sr.bounds.size.y : 6f;
                ExplosionEffect.CreateForDefeat(transform.position, defeatBurstColor, subjectHeight, sortingOrder: RenderOrder.CombatFx);
            }

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(bossDefeatSe);

            if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[BossDefeat] Death presentation");

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
        if (currentFrames == attackFrames) return 1;
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
    public void NetApplyVisual(NetCombat.State s)
    {
        if (state == State.Dead) return;
        if (s.FramesSet != netFramesSet)
        {
            netFramesSet = s.FramesSet;
            switch (s.FramesSet)
            {
            case 0: SetFrames(idleFrames); break;
            case 1: SetFrames(attackFrames); break;
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

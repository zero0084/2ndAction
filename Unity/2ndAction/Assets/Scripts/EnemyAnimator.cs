using UnityEngine;

// Simple procedural idle motion (bob + squash + a slight rotational sway)
// since only a single still frame is available for the enemy art - this is
// the only source of "life" a lineup of otherwise-identical, non-animated
// enemies has.
//
// Squash/sway are applied to `visual` (the child GameObject actually
// holding the SpriteRenderer - see GroundFactory.CreateEnemy), never to
// this component's own transform (Root, which also carries the
// BoxCollider2D and whatever slope-tilt rotation TerrainManager set at
// spawn) - so idle "breathing" can never subtly resize/rotate the hit
// detection collider along with it. visual's local rotation composes with
// Root's own world rotation automatically through the transform hierarchy,
// so the slope tilt and the sway never need to know about each other.
public class EnemyAnimator : MonoBehaviour
{
    public Transform visual;

    public float flapSpeed = 6f;
    public float bobAmount = 0.15f;
    public float squashAmount = 0.12f;
    // A slower, separately-phased idle sway - breaks up the "several
    // enemies pulsing in unison" look without needing any new art, since
    // it's a different frequency from the bob/squash and randomized per
    // instance below.
    public float swayDegrees = 4f;
    public float swaySpeed = 2.2f;

    // Ground enemies keep their feet planted - no vertical bob, so they
    // don't violate the "always touching the ground except mid-jump" rule
    // the player itself follows. Only a Flying enemy bobs vertically (it's
    // meant to look airborne already) - the bob DOES move Root (and the
    // collider with it), which is intentional for Flying: an airborne
    // enemy's hit detection is supposed to track where it visibly is.
    // Read once from EnemyController - the one place that actually knows
    // which this enemy is - rather than needing to be told separately by
    // every spawner.
    bool isFlying;

    // Distance Level Design Ver.1.1 bug fix - true whenever an
    // EnemySpecialBehavior (ANY kind - Flying/Irregular/Shooter/Heavy/
    // Chaser/Rusher, not just Flying) is present on this same GameObject.
    // Every one of those kinds now owns Root's transform.position itself
    // (see EnemySpecialBehavior's own Update methods) - this class's own
    // `transform.position = basePos + bob` below, previously UNCONDITIONAL,
    // was resetting Root back to its captured spawn position every single
    // frame regardless of isFlying, which silently undid ALL movement from
    // every Behavior kind the instant it was added (a enemy would compute
    // one frame's worth of movement, then get snapped back to basePos
    // before the next frame could see it moved at all - reading as
    // "static" even though the Behavior code was running correctly). Ground
    // enemies with NO EnemySpecialBehavior (behaviorKind None - the
    // original goblin, or an Any-role Formation member that resolved to a
    // non-special species) are completely unaffected by this fix - they
    // never moved via bob anyway (isFlying was already false for them).
    bool ownsRootMotion;

    // Runner Enemy Run Animation - "移動中は常にRun Animation再生". Assigned
    // only for the Runner-art species (Chaser/Rusher - see GroundFactory.
    // CreateEnemy/EnemyDefinition.runFrames); every other species leaves
    // this empty and is completely unaffected (still the same procedural
    // squash/sway idle it always had). Each frame carries its OWN foot
    // pivot (see SceneBuilder.ConfigureSpriteFolderImportWithFootPivot),
    // computed independently per file, so swapping between them keeps the
    // ground-contact point stable instead of jittering ("足元が上下にズレ
    // ない" from the brief) - the same technique this project already uses
    // for the Player/Dragon/Majin's own multi-frame animations.
    public Sprite[] runFrames;
    public float runFrameRate = 10f;
    // 攻撃ポーズ(2026-09-26) - EnemySpecialBehavior.IsInAttackPose(予備動作〜攻撃中)の間だけ表示。
    public Sprite attackSprite;
    // 天空回廊Enemy(2026-09-28): 状態ごとの絵と待機中の浮遊(EnemyDefinition.poses/idleHoverAmplitude)。
    // 未設定(既存Enemy)なら下の従来処理のまま。
    public EnemyPoseSprites poses;
    public float idleHoverAmplitude;
    public float idleHoverSpeed = 1.6f;
    EnemyController controllerForPose;
    Sprite baseSprite;
    Vector3 baseVisualLocalPos;
    bool inPoseSprite;
    EnemySpecialBehavior specialForPose;
    Sprite poseRestoreSprite;
    bool inAttackPose;
    SpriteRenderer visualRenderer;
    float runFrameTimer;
    Vector3 lastRootPos;

    Vector3 basePos;
    Vector3 baseVisualScale;
    float phase;
    float swayPhase;

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }
    // Floating Origin: 座標を戻した1フレームだけ「大きく動いた」と誤判定しないようキャッシュも戻す。
    void OnOriginShifted(float s) { lastRootPos.x -= s; basePos.x -= s; }

    void Start()
    {
        var controller = GetComponent<EnemyController>();
        isFlying = controller != null && controller.movementType == EnemyMovementType.Flying;

        var special = GetComponent<EnemySpecialBehavior>();
        specialForPose = special;
        ownsRootMotion = special != null && special.kind != EnemyBehaviorKind.None;
        // A Flying-Behavior enemy's own UpdateFlying already includes its
        // own bob - this class's bob would otherwise fight it for the same
        // Transform every frame depending on component execution order.
        if (ownsRootMotion) isFlying = false;

        if (visual == null) visual = transform; // fallback so this never silently no-ops if unwired
        visualRenderer = visual.GetComponent<SpriteRenderer>();
        lastRootPos = transform.position;
        controllerForPose = controller;
        baseSprite = visualRenderer != null ? visualRenderer.sprite : null;
        baseVisualLocalPos = visual.localPosition;

        basePos = transform.position;
        baseVisualScale = visual.localScale;
        phase = Random.value * Mathf.PI * 2f;
        swayPhase = Random.value * Mathf.PI * 2f;
        // Slight per-instance speed variation on top of the random phase
        // above, so several enemies' cycles drift apart over time instead
        // of just repeating the same rhythm offset by a fixed amount.
        flapSpeed *= Random.Range(0.85f, 1.15f);
        swaySpeed *= Random.Range(0.8f, 1.2f);
    }

    void Update()
    {
        phase += Time.deltaTime * flapSpeed;
        swayPhase += Time.deltaTime * swaySpeed;
        float s = Mathf.Sin(phase);

        if (!ownsRootMotion && isFlying)
        {
            // 不具合修正(2026-09-12) - 「地上通常攻撃のノックバックが効いて
            // いない、敵がその場に留まりやすい」の根本原因だった、Y軸だけを
            // Start()時の固定basePosへ毎フレーム書き戻す処理(X軸は既に
            // このパスで対象外)。
            //
            // 実機フィードバック(2026-09-12第4弾) - 「足場のない場所へ敵が
            // 吹き飛ばされた後もその場で立った状態になることがある」の
            // 根本原因もこれだった。EnemyController側にLaunch/Slam以外の
            // 状態でも常時接地判定(UpdateNormalGroundCheck)を行う仕組みを
            // 新設したため、Y軸の管理は完全にEnemyController(通常時は各
            // Behaviorの配置ロジック、地面が消えた場合はUpdateNormalGround
            // Check自身の重力落下)に一本化した - ここでの固定basePos.yへの
            // 書き戻しは、地形追従にも落下にも対応できない古い代替実装
            // だったため完全に撤去し、Flying種のY方向bobのみ残す(Flyingは
            // UpdateNormalGroundCheckの対象外 - EnemySpecialBehavior.
            // UpdateFlyingが独自の高度管理を持つため)。
            Vector3 pos = transform.position;
            pos.y = basePos.y + s * bobAmount;
            transform.position = pos;
        }

        if (poses != null && poses.Any && visualRenderer != null) { UpdatePoseAware(s); return; }

        if (attackSprite != null && visualRenderer != null && specialForPose != null && specialForPose.IsInAttackPose)
        {
            if (!inAttackPose) { inAttackPose = true; poseRestoreSprite = visualRenderer.sprite; }
            visualRenderer.sprite = attackSprite;
            visual.localScale = baseVisualScale;
            visual.localRotation = Quaternion.identity;
            lastRootPos = transform.position;
            return;
        }

        if (inAttackPose)
        {
            inAttackPose = false;
            if (visualRenderer != null && poseRestoreSprite != null) visualRenderer.sprite = poseRestoreSprite;
        }

        bool hasRunAnimation = runFrames != null && runFrames.Length > 0 && visualRenderer != null;
        bool moving = hasRunAnimation && Mathf.Abs(transform.position.x - lastRootPos.x) > 0.0008f;
        lastRootPos = transform.position;

        if (moving)
        {
            runFrameTimer += Time.deltaTime * runFrameRate;
            int idx = Mathf.FloorToInt(runFrameTimer) % runFrames.Length;
            visualRenderer.sprite = runFrames[idx];
            // The run frames themselves already carry the motion/liveliness
            // - layering the idle squash/sway on top of an already-
            // animated pose would read as a wobble fighting the art, so
            // this holds Visual at its neutral scale/rotation while
            // actively cycling instead.
            visual.localScale = baseVisualScale;
            visual.localRotation = Quaternion.identity;
            return;
        }

        if (hasRunAnimation)
        {
            runFrameTimer = 0f;
            visualRenderer.sprite = runFrames[0];
        }

        // Scaling happens around visual's own origin, which sits at the
        // sprite's foot pivot - so squash stretches/compresses the
        // character from the foot upward and never moves the foot itself.
        // Applies regardless of ownsRootMotion - this only ever touches
        // Visual, which no Behavior kind ever owns.
        visual.localScale = new Vector3(baseVisualScale.x, baseVisualScale.y * (1f + s * squashAmount), baseVisualScale.z);
        visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(swayPhase) * swayDegrees);
    }

    // ===== 天空回廊Enemy(2026-09-28): 状態ごとの絵 =====
    // 優先順: 被弾(EnemyControllerが出す短い被弾ポーズ) > 行動の状態(予兆/攻撃/硬直/休眠/起動/溜め/急降下) > 移動コマ > 静止絵。
    // 撃破の絵はEnemyController.HitAndDieが直接出す(撃破中はこのコンポーネントが止まるため)。
    void UpdatePoseAware(float s)
    {
        Sprite want = null;
        EnemyPose pose = specialForPose != null ? specialForPose.CurrentPose : EnemyPose.None;
        if (controllerForPose != null && Time.time < controllerForPose.HitPoseUntil && poses.hit != null) want = poses.hit;
        else
        {
            switch (pose)
            {
                case EnemyPose.Telegraph: want = poses.telegraph; break;
                case EnemyPose.Attack: want = poses.attack != null ? poses.attack : attackSprite; break;
                case EnemyPose.Recover: want = poses.recover; break;
                case EnemyPose.Dormant: want = poses.dormant; break;
                case EnemyPose.Wake: want = poses.wake; break;
                case EnemyPose.Charge: want = poses.charge != null ? poses.charge : poses.telegraph; break;
                case EnemyPose.Dive: want = poses.dive != null ? poses.dive : poses.attack; break;
            }
            if (want == null && specialForPose != null && specialForPose.IsInAttackPose) want = attackSprite;
        }

        // 休眠(石像)は完全に静止。それ以外は待機中の浮遊だけ絵に足す(当たり判定=Rootは動かさない)。
        float hover = pose == EnemyPose.Dormant || idleHoverAmplitude <= 0f ? 0f : Mathf.Sin(phase * idleHoverSpeed / Mathf.Max(0.01f, flapSpeed)) * idleHoverAmplitude;
        visual.localPosition = baseVisualLocalPos + new Vector3(0f, hover, 0f);

        if (want != null)
        {
            inPoseSprite = true;
            visualRenderer.sprite = want;
            visual.localScale = baseVisualScale;
            visual.localRotation = Quaternion.identity;
            lastRootPos = transform.position;
            return;
        }

        bool hasRun = runFrames != null && runFrames.Length > 0;
        // 飛ぶ敵(ハーピー等)は横にほとんど動かなくても羽ばたきのコマを回し続ける
        bool flapping = controllerForPose != null && controllerForPose.movementType == EnemyMovementType.Flying;
        bool moving = hasRun && (flapping || Mathf.Abs(transform.position.x - lastRootPos.x) > 0.0008f);
        lastRootPos = transform.position;
        if (moving)
        {
            runFrameTimer += Time.deltaTime * runFrameRate;
            visualRenderer.sprite = runFrames[Mathf.FloorToInt(runFrameTimer) % runFrames.Length];
            visual.localScale = baseVisualScale;
            visual.localRotation = Quaternion.identity;
            inPoseSprite = false;
            return;
        }
        runFrameTimer = 0f;
        if (inPoseSprite || hasRun) visualRenderer.sprite = hasRun && baseSprite == null ? runFrames[0] : baseSprite;
        inPoseSprite = false;
        // 待機: 浮遊する種は伸縮を控えめに(浮遊と二重に揺れないよう)
        float squash = idleHoverAmplitude > 0f ? squashAmount * 0.4f : squashAmount;
        visual.localScale = new Vector3(baseVisualScale.x, baseVisualScale.y * (1f + s * squash), baseVisualScale.z);
        visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(swayPhase) * swayDegrees * (idleHoverAmplitude > 0f ? 0.5f : 1f));
    }
}

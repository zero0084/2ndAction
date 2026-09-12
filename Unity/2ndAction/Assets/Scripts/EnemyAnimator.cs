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
    SpriteRenderer visualRenderer;
    float runFrameTimer;
    Vector3 lastRootPos;

    Vector3 basePos;
    Vector3 baseVisualScale;
    float phase;
    float swayPhase;

    void Start()
    {
        var controller = GetComponent<EnemyController>();
        isFlying = controller != null && controller.movementType == EnemyMovementType.Flying;

        var special = GetComponent<EnemySpecialBehavior>();
        ownsRootMotion = special != null && special.kind != EnemyBehaviorKind.None;
        // A Flying-Behavior enemy's own UpdateFlying already includes its
        // own bob - this class's bob would otherwise fight it for the same
        // Transform every frame depending on component execution order.
        if (ownsRootMotion) isFlying = false;

        if (visual == null) visual = transform; // fallback so this never silently no-ops if unwired
        visualRenderer = visual.GetComponent<SpriteRenderer>();
        lastRootPos = transform.position;

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

        if (!ownsRootMotion)
        {
            // 不具合修正(2026-09-12) - 「地上通常攻撃のノックバックが効いて
            // いない、敵がその場に留まりやすい」の根本原因。従来はここで
            // transform.position全体(X/Y/Z)を、Start()時にキャプチャした
            // 固定のbasePosへ毎フレーム無条件で書き戻していた。
            // EnemyController.KnockbackRoutine(通常ヒットの軽いノックバッ
            // ク)実行中はコルーチン側の書き込みが同フレーム内で後勝ちする
            // ため一瞬だけ効いて見えるが、コルーチンが終わった直後の次の
            // フレームでここが即座にX座標をスポーン時の位置まで巻き戻し
            // ていた - 「攻撃するたびに敵と主人公が一緒に前へ移動する」が
            // 一切蓄積せず、ヒットのたびにスポーン地点へ引き戻されていた
            // (エリアルコンボのLaunchが機能しなかった不具合と全く同じ
            // パターン - 参照: EnemyControllerのAwake/AddComponentの説明)。
            // 「足を地面に固定する」という本来の意図はY軸(浮かない)だけの
            // ためであり、X軸まで固定する設計上の理由はない(Flyingのbobも
            // Yのみ) - Y軸だけbasePosへ固定し、X軸はKnockbackRoutine等の
            // 他ロジックによる書き込みをそのまま尊重する。
            float bobY = isFlying ? s * bobAmount : 0f;
            Vector3 pos = transform.position;
            pos.y = basePos.y + bobY;
            transform.position = pos;
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
}

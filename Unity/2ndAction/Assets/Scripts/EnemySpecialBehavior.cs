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
    // 敵AI行動Tier試験実装(2026-09-16) - kindが StationaryMelee の場合の
    // 強度切り替え(T1=移動なし/T2=+ランダム小移動)。EnemyDefinition.aiTier
    // からGroundFactory.CreateEnemy経由でそのまま渡される。
    public EnemyAiTier aiTier = EnemyAiTier.T0;

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

    [Header("Stationary Melee (T1/T2) - Telegraph→Attack→Recoveryの近接攻撃")]
    // 頭上に表示するTelegraph警告マーカー用Sprite - 新規アートは追加せず、
    // TerrainManager.squareSprite(単色四角、他のVFXでも使い回されている
    // 汎用Sprite)をGroundFactory.CreateEnemy経由でそのまま受け取る。
    public Sprite telegraphMarkerSprite;
    // 「攻撃間隔：約2〜4秒のランダム」(仮値、実機確認後に調整する前提) -
    // Recovery終了後、次のTelegraph開始までのIdle待機時間をここから
    // 毎回抽選し直す。
    public float meleeAttackIntervalMin = 2f;
    public float meleeAttackIntervalMax = 4f;
    // 「Telegraphは仮値として0.4〜0.7秒程度」 - 固定値ではなく範囲から
    // 毎回抽選することで、Telegraphの長さ自体が完全に一定のリズムになって
    // 学習で無効化されるのを防ぐ。
    public float meleeTelegraphDurationMin = 0.4f;
    public float meleeTelegraphDurationMax = 0.7f;
    // Telegraph中、頭上マーカーが最大何倍まで膨らむか。
    public float meleeTelegraphMarkerScale = 0.4f;
    // 攻撃判定(EnemyMeleeHitbox)が実際に有効な時間(=「攻撃」フェーズの
    // 長さ)。
    public float meleeAttackActiveDuration = 0.2f;
    // 攻撃後の短い硬直("短いRecoveryを入れてください")。
    public float meleeRecoverDuration = 0.45f;
    // 攻撃判定の大きさ・本体からのオフセット距離。どちら側(Player寄り)に
    // 出すかはTelegraph開始時にPlayerとの相対位置から自動判定する。
    public float meleeHitboxWidth = 1.1f;
    public float meleeHitboxHeight = 1.0f;
    public float meleeHitboxOffsetX = 0.75f;

    [Header("Stationary Melee T2 - 基本位置周辺のランダムな小移動/Hop")]
    // 「元の配置位置から大きく離れず」 - spawnXからこの範囲を超えない
    // (IrregularのirregularLeashRangeと同じ考え方だが、T2は「少し動く」
    // 程度に留めるため既定値はIrregularよりずっと小さい)。
    public float meleeMoveLeashRange = 0.8f;
    public float meleeMoveSpeed = 0.9f;
    public float meleeHopHeight = 0.35f;
    public float meleeHopDuration = 0.3f;
    // 1つの行動(待機/小移動)が続く時間の範囲。Hopだけは固定でmeleeHop
    // Duration秒(1回のジャンプが自然に終わる長さ)を使う。
    public float meleeMoveActionDurationMin = 0.8f;
    public float meleeMoveActionDurationMax = 1.8f;

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

    // 敵AI行動Tier試験実装(2026-09-16) - StationaryMelee(T1/T2)専用の状態。
    // 攻撃サイクルと移動サイクルは独立した2つの小さなステートマシンとして
    // 並行に進める(T1は移動サイクル自体を回さない)。RusherState等と同じ
    // Update()駆動のタイマー方式(コルーチン不使用) - EnemyController.
    // DisableMotionComponents()がこのコンポーネント自体をenabled=false
    // にした瞬間、Update()が呼ばれなくなるだけでタイマーはその場で完全に
    // 凍結される(コルーチンだと無効化後も裏で進み続ける恐れがあるため、
    // 意図的にコルーチンを使っていない)。
    enum MeleeAttackState { Idle, Telegraph, Attack, Recover }
    MeleeAttackState meleeAttackState;
    float meleeAttackTimer;
    float meleeTelegraphTotalDuration;
    float meleeAttackFacingDir = -1f;

    enum MeleeMoveState { Idle, MoveSmall, Hop }
    MeleeMoveState meleeMoveState;
    float meleeMoveTimer;
    float meleeMoveDirSign = 1f;
    float meleeHopElapsed;

    GameObject meleeHitboxGO;
    GameObject meleeTelegraphMarkerGO;
    Transform meleeTelegraphMarkerTransform;

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
        if (kind == EnemyBehaviorKind.StationaryMelee) InitStationaryMelee();
    }

    // 敵AI行動Tier試験実装(2026-09-16) - Hit Reaction/Knockback/Launchに
    // よる中断(EnemyController.DisableMotionComponents/RestoreMotion
    // Componentsがこのコンポーネント自体をenabled=false/trueで切り替える)
    // から復帰するたびに必ず呼ばれる。「攻撃を中断された後にAI Stateが
    // 壊れて棒立ちになったり、永遠にAttack状態になったりしない」ことを
    // 保証するため、途中状態からの再開は一切試みずIdleへ安全にリセット
    // する。meleeHitboxGO!=nullのチェックにより、初回起動時(Start()より
    // 前にOnEnableが走るUnityのライフサイクル順)は何もしない - 初期化は
    // Start()/InitStationaryMeleeが一度だけ担う。
    void OnEnable()
    {
        if (kind != EnemyBehaviorKind.StationaryMelee || meleeHitboxGO == null) return;
        meleeAttackState = MeleeAttackState.Idle;
        meleeAttackTimer = Random.Range(meleeAttackIntervalMin, meleeAttackIntervalMax);
        meleeMoveState = MeleeMoveState.Idle;
        meleeMoveTimer = Random.Range(meleeMoveActionDurationMin, meleeMoveActionDurationMax);
        meleeHitboxGO.SetActive(false);
        if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(false);
    }

    // Hit Reaction/Knockback/Launch中は攻撃判定・Telegraph表示を即座に
    // 消す - 吹き飛ばされている最中の敵に攻撃判定が生き残ったまま追加
    // ヒットが発生する事故を防ぐ。
    void OnDisable()
    {
        if (meleeHitboxGO != null) meleeHitboxGO.SetActive(false);
        if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(false);
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
            case EnemyBehaviorKind.StationaryMelee: UpdateStationaryMelee(); break;
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

    // SetGroundedXの亜種 - Hop中に一時的な上方向オフセット(extraY)を
    // 加えた高さへ置く。groundYOffset自体はそのまま使うので、Hopの
    // 山なり軌道が終わってextraY=0に戻れば、必ずSetGroundedXと寸分違わず
    // 同じ着地Yになる(「T2のHop後に正常に地面へ戻る」の保証)。
    void SetGroundedXWithExtraY(float x, float extraY)
    {
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        if (!groundY.HasValue) return;
        transform.position = new Vector3(x, groundY.Value + groundYOffset + extraY, transform.position.z);
    }

    // ===== Stationary Melee (T1/T2) - 敵AI行動Tier試験実装(2026-09-16) =====

    void InitStationaryMelee()
    {
        meleeAttackState = MeleeAttackState.Idle;
        meleeAttackTimer = Random.Range(meleeAttackIntervalMin, meleeAttackIntervalMax);
        meleeMoveState = MeleeMoveState.Idle;
        meleeMoveTimer = Random.Range(meleeMoveActionDurationMin, meleeMoveActionDurationMax);

        // Root(this.transform)の子として作る - Visual(EnemyAnimator/
        // EnemyFacingの専有物、idle squash/swayや反転で常時localScale/
        // localPositionが動く)には一切触れないための意図的な選択。攻撃
        // 方向はfacingDirを自前で毎回計算して使うので、Visualの反転状態に
        // 依存しない。
        meleeHitboxGO = new GameObject("MeleeHitbox");
        meleeHitboxGO.transform.SetParent(transform, false);
        var hitboxCol = meleeHitboxGO.AddComponent<BoxCollider2D>();
        hitboxCol.isTrigger = true;
        hitboxCol.size = new Vector2(meleeHitboxWidth, meleeHitboxHeight);
        meleeHitboxGO.AddComponent<EnemyMeleeHitbox>();
        meleeHitboxGO.SetActive(false);

        if (telegraphMarkerSprite != null)
        {
            meleeTelegraphMarkerGO = new GameObject("TelegraphMarker");
            meleeTelegraphMarkerGO.transform.SetParent(transform, false);
            // 敵本体の少し上(仮値、実機確認後に調整) - 高速走行中でも
            // 頭上の警告として視認しやすい位置を狙う。
            meleeTelegraphMarkerGO.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var markerSr = meleeTelegraphMarkerGO.AddComponent<SpriteRenderer>();
            markerSr.sprite = telegraphMarkerSprite;
            markerSr.color = new Color(1f, 0.35f, 0.15f, 0.95f);
            markerSr.sortingOrder = RenderOrder.CombatFx;
            meleeTelegraphMarkerTransform = meleeTelegraphMarkerGO.transform;
            meleeTelegraphMarkerGO.SetActive(false);
        }
    }

    void UpdateStationaryMelee()
    {
        UpdateMeleeAttackCycle();
        // T1は基本位置から一切移動しない - 「T2：Random Movement + Slow
        // Random Attack」の追加要素(基本位置周辺のランダムな小移動/Hop)は
        // T2でだけ回す。
        if (aiTier == EnemyAiTier.T2) UpdateMeleeMovement();
    }

    void UpdateMeleeAttackCycle()
    {
        meleeAttackTimer -= Time.deltaTime;
        switch (meleeAttackState)
        {
            case MeleeAttackState.Idle:
                // T2がHop中(空中に浮いている瞬間)は新しい攻撃を開始しない
                // - 地に足がついていない状態で攻撃を始める不自然さを防ぐ
                // だけで、Hop自体の進行はUpdateMeleeMovement側で独立に
                // 続く(攻撃サイクルを止めているわけではない)。
                if (meleeAttackTimer <= 0f && meleeMoveState != MeleeMoveState.Hop)
                {
                    StartMeleeTelegraph();
                }
                break;

            case MeleeAttackState.Telegraph:
                if (meleeTelegraphMarkerTransform != null)
                {
                    float total = Mathf.Max(0.05f, meleeTelegraphTotalDuration);
                    float progress = Mathf.Clamp01(1f - meleeAttackTimer / total);
                    float pulse = Mathf.Sin(progress * Mathf.PI);
                    meleeTelegraphMarkerTransform.localScale = Vector3.one * meleeTelegraphMarkerScale * (0.4f + 0.6f * pulse);
                }
                if (meleeAttackTimer <= 0f) StartMeleeAttack();
                break;

            case MeleeAttackState.Attack:
                if (meleeAttackTimer <= 0f) EndMeleeAttack();
                break;

            case MeleeAttackState.Recover:
                if (meleeAttackTimer <= 0f)
                {
                    meleeAttackState = MeleeAttackState.Idle;
                    meleeAttackTimer = Random.Range(meleeAttackIntervalMin, meleeAttackIntervalMax);
                }
                break;
        }
    }

    void StartMeleeTelegraph()
    {
        meleeAttackState = MeleeAttackState.Telegraph;
        meleeTelegraphTotalDuration = Random.Range(meleeTelegraphDurationMin, meleeTelegraphDurationMax);
        meleeAttackTimer = meleeTelegraphTotalDuration;
        // 攻撃方向をここで一度だけ決めて固定する - Telegraph中にPlayerが
        // 通り過ぎても攻撃の向きを追従させない(「振りかぶった後に狙いを
        // 変える」不自然さを避けるため)。
        float dx = player != null ? player.position.x - transform.position.x : meleeAttackFacingDir;
        meleeAttackFacingDir = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : meleeAttackFacingDir;
        if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(true);
    }

    void StartMeleeAttack()
    {
        meleeAttackState = MeleeAttackState.Attack;
        meleeAttackTimer = meleeAttackActiveDuration;
        if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(false);
        if (meleeHitboxGO != null)
        {
            meleeHitboxGO.transform.localPosition = new Vector3(meleeAttackFacingDir * meleeHitboxOffsetX, 0f, 0f);
            meleeHitboxGO.SetActive(true);
        }
    }

    void EndMeleeAttack()
    {
        meleeAttackState = MeleeAttackState.Recover;
        meleeAttackTimer = meleeRecoverDuration;
        if (meleeHitboxGO != null) meleeHitboxGO.SetActive(false);
    }

    void UpdateMeleeMovement()
    {
        meleeMoveTimer -= Time.deltaTime;

        if (meleeMoveState == MeleeMoveState.Hop)
        {
            meleeHopElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(meleeHopElapsed / Mathf.Max(0.05f, meleeHopDuration));
            float arc = Mathf.Sin(t * Mathf.PI) * meleeHopHeight;
            SetGroundedXWithExtraY(transform.position.x, arc);
            if (t >= 1f)
            {
                // sin(π)は理論上0だが浮動小数の誤差を確実に消し、
                // ちょうど地面の高さへ戻す。
                SetGroundedXWithExtraY(transform.position.x, 0f);
            }
        }
        else if (meleeMoveState == MeleeMoveState.MoveSmall)
        {
            float nextX = transform.position.x + meleeMoveDirSign * meleeMoveSpeed * Time.deltaTime;
            // spawnXからmeleeMoveLeashRangeを超えたら向きを反転する
            // (Irregularのleash-and-reverseと同じ考え方)。
            if (Mathf.Abs(nextX - spawnX) > meleeMoveLeashRange) meleeMoveDirSign = -meleeMoveDirSign;
            SetGroundedX(transform.position.x + meleeMoveDirSign * meleeMoveSpeed * Time.deltaTime);
        }

        if (meleeMoveTimer <= 0f) PickMeleeMoveAction();
    }

    void PickMeleeMoveAction()
    {
        float roll = Random.value;
        if (roll < 0.4f)
        {
            meleeMoveState = MeleeMoveState.Idle;
            meleeMoveTimer = Random.Range(meleeMoveActionDurationMin, meleeMoveActionDurationMax);
        }
        else if (roll < 0.8f)
        {
            meleeMoveState = MeleeMoveState.MoveSmall;
            meleeMoveDirSign = Random.value < 0.5f ? -1f : 1f;
            meleeMoveTimer = Random.Range(meleeMoveActionDurationMin, meleeMoveActionDurationMax);
        }
        else
        {
            meleeMoveState = MeleeMoveState.Hop;
            meleeHopElapsed = 0f;
            meleeMoveTimer = meleeHopDuration;
        }
    }
}

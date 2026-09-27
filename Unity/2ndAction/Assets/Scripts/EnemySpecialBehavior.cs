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

    // 自然洞窟雑魚敵追加(2026-09-22) - Cave Bat用。天井のある洞窟ステージ
    // でだけ効く追加の高度クランプ(GetEffectiveCeilingHeightAtがnullを返す
    // 荒野街道/天空回廊では何も変わらない、既存Flying種は無改造のまま)。
    [Header("Flying - Cave ceiling awareness (洞窟以外では常にno-op)")]
    public bool flyingRespectCeiling = true;
    public float flyingCeilingMargin = 0.6f;

    // 自然洞窟雑魚敵追加(2026-09-22) - Cave Bat用の任意のDive攻撃。既定
    // false(=既存Flying種と完全に同じ、単に飛び続けるだけ)。有効な場合、
    // 一定間隔でTelegraph(頭上マーカー点滅)→Dive(Player方向へ急降下)→
    // Hold(攻撃判定)→Returnを1サイクルとして繰り返す。
    [Header("Flying - Dive Attack (任意、既定OFF)")]
    public bool flyingDiveEnabled = false;
    public float flyingDiveIntervalMin = 3.5f;
    public float flyingDiveIntervalMax = 6f;
    public float flyingDiveRange = 9f; // このX距離内にPlayerがいる時だけ次のDiveを狙う
    public float flyingDiveTelegraphDuration = 0.55f;
    public float flyingDiveMarkerScale = 0.5f;
    public float flyingDiveSpeed = 7f;
    public float flyingDiveMinAltitude = 1.0f; // 急降下の最低高度(地面/床からの高さ)
    public float flyingDiveHoldDuration = 0.16f; // 最下点付近で攻撃判定が出る時間
    public float flyingDiveRecoverSpeed = 3.5f;
    public float flyingDiveHitboxWidth = 1.5f;
    public float flyingDiveHitboxHeight = 1.2f;

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
    // 弾速の走行補正(2026-09-26) - 弾は走行速度で流れる座標系の中を飛ぶようになったため(PlayerController.
    // RunFrameSpeed)、正面から迫る見た目の速さが従来(=走行速度ぶん上乗せ)より遅くならないよう底上げ。
    public float shooterProjectileSpeed = 9f;
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

    [Header("Cave Hopper - 基本位置周辺のランダム移動+小Hop+時々近接攻撃")]
    public float hopperMoveSpeed = 1.4f;
    public float hopperLeashRange = 2.2f;
    public float hopperHopHeight = 0.55f;
    public float hopperHopDuration = 0.3f;
    public float hopperHopDistanceMin = 0.6f;
    public float hopperHopDistanceMax = 1.6f;
    // 「Action決定 -> 0.4〜1.2秒実行 -> 次Action」T2よりやや短め(不規則さを強調)。
    public float hopperActionDurationMin = 0.4f;
    public float hopperActionDurationMax = 1.2f;
    public float hopperAttackIntervalMin = 2.5f;
    public float hopperAttackIntervalMax = 5f;
    public float hopperTelegraphDurationMin = 0.35f;
    public float hopperTelegraphDurationMax = 0.6f;
    public float hopperTelegraphMarkerScale = 0.4f;
    public float hopperAttackActiveDuration = 0.2f;
    public float hopperRecoverDuration = 0.4f;
    public float hopperHitboxWidth = 1.0f;
    public float hopperHitboxHeight = 0.9f;
    public float hopperHitboxOffsetX = 0.7f;

    [Header("Burrow Worm - 地中待機→予兆→出現→攻撃→退避")]
    public Sprite wormDustSprite; // 予兆VFX用(未指定ならSoftDotSpriteで代用)
    public float wormUndergroundMin = 1.2f;
    public float wormUndergroundMax = 2.5f;
    // このX距離内にPlayerがいる時だけ出現を狙う - 「Playerを長時間追跡する
    // 敵ではなく、地面から突然出現する危険」として、画面外からの一方的な
    // 奇襲にならない範囲に留める。
    public float wormAttackRange = 8f;
    public float wormTelegraphDuration = 0.9f;
    public float wormEmergeDuration = 0.35f;
    public float wormAttackActiveDuration = 0.3f;
    public float wormRecoverDuration = 0.35f;
    public float wormRetreatDuration = 0.35f;
    public float wormHitboxWidth = 1.6f;
    public float wormHitboxHeight = 1.4f;
    public float wormBuriedDepth = 1.3f; // 地中にいる間、地面からどれだけ沈めるか

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

    // 自然洞窟雑魚敵追加(2026-09-22) - Flying Dive攻撃の状態。
    enum FlyingDiveState { None, Telegraph, Diving, Holding, Returning }
    FlyingDiveState flyingDiveState;
    float flyingDiveTimer;
    float flyingDiveTargetY;
    GameObject flyingDiveHitboxGO;
    GameObject flyingDiveMarkerGO;
    Transform flyingDiveMarkerTransform;

    // 自然洞窟雑魚敵追加(2026-09-22) - Cave Hopperの移動サイクル(Irregular/
    // T2と同じUpdate()駆動のタイマー方式)。
    enum HopperMoveState { Pause, Move, Hop }
    HopperMoveState hopperMoveState;
    float hopperActionTimer;
    float hopperMoveDirSign = 1f;
    float hopperHopStartX, hopperHopTargetX, hopperHopElapsed;

    // 自然洞窟雑魚敵追加(2026-09-22) - Cave Hopperの攻撃サイクル(Stationary
    // Melee攻撃サイクルと同形、独立したTimer/Stateを持つ - Hop中は新規攻撃
    // を開始しない)。
    enum HopperAttackState { Idle, Telegraph, Attack, Recover }
    HopperAttackState hopperAttackState;
    float hopperAttackTimer;
    float hopperTelegraphTotalDuration;
    float hopperAttackFacingDir = -1f;
    GameObject hopperHitboxGO;
    GameObject hopperTelegraphMarkerGO;
    Transform hopperTelegraphMarkerTransform;

    // 自然洞窟雑魚敵追加(2026-09-22) - Burrow Wormの状態機械。
    enum WormState { Underground, Telegraph, Emerge, Attack, Recover, Retreat }
    WormState wormState;
    float wormTimer;
    float wormFacingDir = -1f;
    float wormGroundY;
    GameObject wormHitboxGO;
    SpriteRenderer wormVisualRenderer;
    BoxCollider2D wormBodyCollider;
    float wormDustCooldown;

    void Start()
    {
        FloatingOrigin.Shifted += OnOriginShifted;
        spawnX = transform.position.x;
        float? spawnGroundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(spawnX) : null;
        groundYOffset = spawnGroundY.HasValue ? transform.position.y - spawnGroundY.Value : 0f;
        if (player == null && PlayerController.Instance != null) player = PlayerController.Instance.transform;
        shooterTimer = shooterCooldown * 0.5f; // stagger first shot instead of every Shooter firing in lockstep
        flyingBobSeed = Random.Range(0f, 1000f);
        flyingBaseY = transform.position.y;
        PickIrregularAction();
        if (kind == EnemyBehaviorKind.StationaryMelee) InitStationaryMelee();
        if (kind == EnemyBehaviorKind.CaveHopper) InitCaveHopper();
        if (kind == EnemyBehaviorKind.BurrowWorm) InitBurrowWorm();
        if (kind == EnemyBehaviorKind.Flying && flyingDiveEnabled) InitFlyingDive();
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
    // Floating Origin: 一時的にDisableされる間も追従するようStart~OnDestroyで購読する。
    void OnOriginShifted(float s) { spawnX -= s; }
    void OnDestroy() { FloatingOrigin.Shifted -= OnOriginShifted; }

    void OnEnable()
    {
        if (kind == EnemyBehaviorKind.StationaryMelee && meleeHitboxGO != null)
        {
            meleeAttackState = MeleeAttackState.Idle;
            meleeAttackTimer = Random.Range(meleeAttackIntervalMin, meleeAttackIntervalMax);
            meleeMoveState = MeleeMoveState.Idle;
            meleeMoveTimer = Random.Range(meleeMoveActionDurationMin, meleeMoveActionDurationMax);
            meleeHitboxGO.SetActive(false);
            if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(false);
        }

        // 自然洞窟雑魚敵追加(2026-09-22) - CaveHopper/BurrowWorm/Flying Dive
        // も同じ「中断されたら途中状態から再開せず、必ず安全なIdle相当へ
        // 戻す」方針(StationaryMeleeの既存コメント参照)。
        if (kind == EnemyBehaviorKind.CaveHopper && hopperHitboxGO != null)
        {
            hopperAttackState = HopperAttackState.Idle;
            hopperAttackTimer = Random.Range(hopperAttackIntervalMin, hopperAttackIntervalMax);
            hopperMoveState = HopperMoveState.Pause;
            hopperActionTimer = Random.Range(hopperActionDurationMin, hopperActionDurationMax);
            hopperHitboxGO.SetActive(false);
            if (hopperTelegraphMarkerGO != null) hopperTelegraphMarkerGO.SetActive(false);
        }

        if (kind == EnemyBehaviorKind.BurrowWorm && wormHitboxGO != null)
        {
            // 被弾で中断された場合、安全に地中へ戻す(地上に出た状態のまま
            // Colliderが変な状態で固着することを避ける) - 「地中に完全に
            // 潜っている間は攻撃対象外でも構いません」に沿った安全なリセット。
            wormState = WormState.Underground;
            wormTimer = Random.Range(wormUndergroundMin, wormUndergroundMax);
            wormHitboxGO.SetActive(false);
            if (wormBodyCollider != null) wormBodyCollider.enabled = false;
            if (wormVisualRenderer != null) wormVisualRenderer.enabled = false;
            Vector3 p = transform.position;
            p.y = wormGroundY - wormBuriedDepth;
            transform.position = p;
        }

        if (kind == EnemyBehaviorKind.Flying && flyingDiveEnabled && flyingDiveHitboxGO != null)
        {
            flyingDiveState = FlyingDiveState.None;
            flyingDiveTimer = Random.Range(flyingDiveIntervalMin, flyingDiveIntervalMax);
            flyingDiveHitboxGO.SetActive(false);
            if (flyingDiveMarkerGO != null) flyingDiveMarkerGO.SetActive(false);
        }
    }

    // Hit Reaction/Knockback/Launch中は攻撃判定・Telegraph表示を即座に
    // 消す - 吹き飛ばされている最中の敵に攻撃判定が生き残ったまま追加
    // ヒットが発生する事故を防ぐ。
    void OnDisable()
    {
        if (meleeHitboxGO != null) meleeHitboxGO.SetActive(false);
        if (meleeTelegraphMarkerGO != null) meleeTelegraphMarkerGO.SetActive(false);
        if (hopperHitboxGO != null) hopperHitboxGO.SetActive(false);
        if (hopperTelegraphMarkerGO != null) hopperTelegraphMarkerGO.SetActive(false);
        if (wormHitboxGO != null) wormHitboxGO.SetActive(false);
        if (flyingDiveHitboxGO != null) flyingDiveHitboxGO.SetActive(false);
        if (flyingDiveMarkerGO != null) flyingDiveMarkerGO.SetActive(false);
    }

    void Update()
    {
        if (player == null) return;

        // Safety - a Chaser/Rusher that somehow ended up hopelessly behind
        // the auto-scrolling player is despawned rather than left running
        // forever off-screen (item 3).
        // マルチプレイPhase 2 - HOSTでは最後尾のプレイヤーを基準にする(後ろのプレイヤーの敵を消さない)。
        if ((kind == EnemyBehaviorKind.Chaser || kind == EnemyBehaviorKind.Rusher)
            && NetCombat.RearmostAlivePlayerX(player.position.x) - transform.position.x > giveUpDistanceBehindPlayer)
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
            case EnemyBehaviorKind.CaveHopper: UpdateCaveHopper(); break;
            case EnemyBehaviorKind.BurrowWorm: UpdateBurrowWorm(); break;
        }
    }

    // "空中に浮き続ける...完全停止ではなく、軽い上下移動 + Player方向へゆっ
    // くり接近...地面へ着地しない" (item 3). Horizontal approach uses
    // SetGroundedX's OWN ground-tracking only as the floor reference (via
    // flyingMinHeight added on top), never actually snapping to it.
    void UpdateFlying()
    {
        // 自然洞窟雑魚敵追加(2026-09-22) - Dive中/Telegraph中はこの通常の
        // 徘徊ロジックを完全に止め、専用のDive状態機械(UpdateFlyingDive)
        // だけがRootを動かす。Diveが無効/Noneの間は従来どおり無改造。
        if (flyingDiveEnabled && flyingDiveState != FlyingDiveState.None)
        {
            UpdateFlyingDive();
            return;
        }

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
        // 自然洞窟雑魚敵追加(2026-09-22) - 天井のある洞窟ステージでだけ効く
        // 高度クランプ(GetEffectiveCeilingHeightAtは非洞窟ステージ/未生成
        // 区間ではnullを返すため、荒野街道/天空回廊のFlyingEnemyは完全に
        // 無改造のまま)。ObstacleSpawner.CaveEffectiveCeilingと同じ考え方。
        if (flyingRespectCeiling && TerrainManager.Instance != null)
        {
            float? ceil = TerrainManager.Instance.GetEffectiveCeilingHeightAt(x);
            if (ceil.HasValue) targetY = Mathf.Min(targetY, ceil.Value - flyingCeilingMargin);
        }
        transform.position = new Vector3(x, targetY, transform.position.z);

        if (flyingDiveEnabled)
        {
            flyingDiveTimer -= Time.deltaTime;
            if (flyingDiveTimer <= 0f && Mathf.Abs(dx) <= flyingDiveRange)
            {
                StartFlyingDiveTelegraph();
            }
        }
    }

    void InitFlyingDive()
    {
        flyingDiveHitboxGO = new GameObject("DiveHitbox");
        flyingDiveHitboxGO.transform.SetParent(transform, false);
        var hitboxCol = flyingDiveHitboxGO.AddComponent<BoxCollider2D>();
        hitboxCol.isTrigger = true;
        hitboxCol.size = new Vector2(flyingDiveHitboxWidth, flyingDiveHitboxHeight);
        flyingDiveHitboxGO.AddComponent<EnemyMeleeHitbox>();
        flyingDiveHitboxGO.SetActive(false);

        if (telegraphMarkerSprite != null)
        {
            flyingDiveMarkerGO = new GameObject("DiveTelegraphMarker");
            flyingDiveMarkerGO.transform.SetParent(transform, false);
            flyingDiveMarkerGO.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var markerSr = flyingDiveMarkerGO.AddComponent<SpriteRenderer>();
            markerSr.sprite = telegraphMarkerSprite;
            markerSr.color = new Color(1f, 0.35f, 0.15f, 0.95f);
            ApplyWarningArt(markerSr);
            markerSr.sortingOrder = RenderOrder.CombatFx;
            flyingDiveMarkerTransform = flyingDiveMarkerGO.transform;
            flyingDiveMarkerGO.SetActive(false);
            NetAttackSync.Register(flyingDiveMarkerGO, NetAttackSync.AType.Telegraph); // マルチプレイPhase 2.5: 予兆をJOINにも出す
        }

        flyingDiveTimer = Random.Range(flyingDiveIntervalMin, flyingDiveIntervalMax);
    }

    void StartFlyingDiveTelegraph()
    {
        flyingDiveState = FlyingDiveState.Telegraph;
        flyingDiveTimer = flyingDiveTelegraphDuration;
        if (flyingDiveMarkerGO != null) flyingDiveMarkerGO.SetActive(true);
    }

    void UpdateFlyingDive()
    {
        switch (flyingDiveState)
        {
            case FlyingDiveState.Telegraph:
                flyingDiveTimer -= Time.deltaTime;
                if (flyingDiveMarkerTransform != null)
                {
                    float total = Mathf.Max(0.05f, flyingDiveTelegraphDuration);
                    float progress = Mathf.Clamp01(1f - flyingDiveTimer / total);
                    float pulse = Mathf.Sin(progress * Mathf.PI);
                    flyingDiveMarkerTransform.localScale = Vector3.one * flyingDiveMarkerScale * MarkerArtScale * (0.4f + 0.6f * pulse);
                }
                if (flyingDiveTimer <= 0f)
                {
                    if (flyingDiveMarkerGO != null) flyingDiveMarkerGO.SetActive(false);
                    float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(transform.position.x) : null;
                    float minY = (groundY ?? (transform.position.y - flyingDiveMinAltitude)) + flyingDiveMinAltitude;
                    flyingDiveTargetY = Mathf.Max(minY, player.position.y);
                    flyingDiveState = FlyingDiveState.Diving;
                }
                break;

            case FlyingDiveState.Diving:
            {
                Vector3 p = transform.position;
                p.y = Mathf.MoveTowards(p.y, flyingDiveTargetY, flyingDiveSpeed * Time.deltaTime);
                transform.position = p;
                if (Mathf.Abs(p.y - flyingDiveTargetY) < 0.05f)
                {
                    flyingDiveState = FlyingDiveState.Holding;
                    flyingDiveTimer = flyingDiveHoldDuration;
                    if (flyingDiveHitboxGO != null) flyingDiveHitboxGO.SetActive(true);
                }
                break;
            }

            case FlyingDiveState.Holding:
                flyingDiveTimer -= Time.deltaTime;
                if (flyingDiveTimer <= 0f)
                {
                    if (flyingDiveHitboxGO != null) flyingDiveHitboxGO.SetActive(false);
                    flyingDiveState = FlyingDiveState.Returning;
                }
                break;

            case FlyingDiveState.Returning:
            {
                Vector3 p = transform.position;
                p.y = Mathf.MoveTowards(p.y, flyingBaseY, flyingDiveRecoverSpeed * Time.deltaTime);
                transform.position = p;
                if (Mathf.Abs(p.y - flyingBaseY) < 0.1f)
                {
                    flyingDiveState = FlyingDiveState.None;
                    flyingDiveTimer = Random.Range(flyingDiveIntervalMin, flyingDiveIntervalMax);
                }
                break;
            }
        }
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
        shooterInRange = absDx <= shooterRange;

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
            shooterPoseUntil = Time.time + 0.3f;
            Sprite arrow = ArrowArt();
            if (arrow != null)
            {
                // 攻撃エフェクト本番素材化(2026-09-26) - 弓兵は回転する単色四角ではなく、
                // 進行方向を向いた矢(Resources/Effects/arrow)を撃つ。反射などの挙動は火球と同じ。
                GameObject ar = FireballController.Create(arrow, transform.position + (Vector3)(dir * 0.4f), dir * shooterProjectileSpeed);
                var fc = ar.GetComponent<FireballController>();
                fc.SetStaticVisual(new Vector3(1.1f, 1.1f, 1f), Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                var arSr = ar.GetComponent<SpriteRenderer>();
                if (arSr != null) arSr.color = Color.white;
                return;
            }
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
            ApplyWarningArt(markerSr);
            markerSr.sortingOrder = RenderOrder.CombatFx;
            meleeTelegraphMarkerTransform = meleeTelegraphMarkerGO.transform;
            meleeTelegraphMarkerGO.SetActive(false);
            NetAttackSync.Register(meleeTelegraphMarkerGO, NetAttackSync.AType.Telegraph); // マルチプレイPhase 2.5: 予兆をJOINにも出す
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
                    meleeTelegraphMarkerTransform.localScale = Vector3.one * meleeTelegraphMarkerScale * MarkerArtScale * (0.4f + 0.6f * pulse);
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
            SpawnMeleeSlash(meleeHitboxGO.transform.position, meleeAttackFacingDir, Mathf.Max(meleeHitboxWidth, meleeHitboxHeight));
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

    // ===== Cave Hopper - 自然洞窟雑魚敵追加(2026-09-22) ===== //
    // 「不規則に動く」を、Irregularのランダム行動選択(Pause/Move/Hop)と
    // StationaryMeleeの攻撃サイクル(Telegraph→Attack→Recovery)を1つの
    // Behaviorへ合成する形で実装。Hop自体は「その場での垂直跳躍」を既定
    // としつつ、安全確認が取れた時だけ水平方向の短い移動を混ぜる
    // (SetGroundedX/SetGroundedXWithExtraYの「着地先に地面が無ければ移動
    // しない」フェイルセーフに完全に乗るため、穴の上で空中停止することは
    // 構造上起こらない)。
    void InitCaveHopper()
    {
        hopperMoveState = HopperMoveState.Pause;
        hopperActionTimer = Random.Range(hopperActionDurationMin, hopperActionDurationMax);
        hopperAttackState = HopperAttackState.Idle;
        hopperAttackTimer = Random.Range(hopperAttackIntervalMin, hopperAttackIntervalMax);

        hopperHitboxGO = new GameObject("HopperHitbox");
        hopperHitboxGO.transform.SetParent(transform, false);
        var hitboxCol = hopperHitboxGO.AddComponent<BoxCollider2D>();
        hitboxCol.isTrigger = true;
        hitboxCol.size = new Vector2(hopperHitboxWidth, hopperHitboxHeight);
        hopperHitboxGO.AddComponent<EnemyMeleeHitbox>();
        hopperHitboxGO.SetActive(false);

        if (telegraphMarkerSprite != null)
        {
            hopperTelegraphMarkerGO = new GameObject("TelegraphMarker");
            hopperTelegraphMarkerGO.transform.SetParent(transform, false);
            hopperTelegraphMarkerGO.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            var markerSr = hopperTelegraphMarkerGO.AddComponent<SpriteRenderer>();
            markerSr.sprite = telegraphMarkerSprite;
            markerSr.color = new Color(1f, 0.35f, 0.15f, 0.95f);
            ApplyWarningArt(markerSr);
            markerSr.sortingOrder = RenderOrder.CombatFx;
            hopperTelegraphMarkerTransform = hopperTelegraphMarkerGO.transform;
            hopperTelegraphMarkerGO.SetActive(false);
            NetAttackSync.Register(hopperTelegraphMarkerGO, NetAttackSync.AType.Telegraph); // マルチプレイPhase 2.5: 予兆をJOINにも出す
        }
    }

    void UpdateCaveHopper()
    {
        UpdateHopperAttackCycle();
        UpdateHopperMovement();
    }

    void UpdateHopperAttackCycle()
    {
        hopperAttackTimer -= Time.deltaTime;
        switch (hopperAttackState)
        {
            case HopperAttackState.Idle:
                // Hop中(空中)は新規攻撃を開始しない - StationaryMelee T2と同じ理由。
                if (hopperAttackTimer <= 0f && hopperMoveState != HopperMoveState.Hop)
                {
                    StartHopperTelegraph();
                }
                break;

            case HopperAttackState.Telegraph:
                if (hopperTelegraphMarkerTransform != null)
                {
                    float total = Mathf.Max(0.05f, hopperTelegraphTotalDuration);
                    float progress = Mathf.Clamp01(1f - hopperAttackTimer / total);
                    float pulse = Mathf.Sin(progress * Mathf.PI);
                    hopperTelegraphMarkerTransform.localScale = Vector3.one * hopperTelegraphMarkerScale * MarkerArtScale * (0.4f + 0.6f * pulse);
                }
                if (hopperAttackTimer <= 0f) StartHopperAttack();
                break;

            case HopperAttackState.Attack:
                if (hopperAttackTimer <= 0f) EndHopperAttack();
                break;

            case HopperAttackState.Recover:
                if (hopperAttackTimer <= 0f)
                {
                    hopperAttackState = HopperAttackState.Idle;
                    hopperAttackTimer = Random.Range(hopperAttackIntervalMin, hopperAttackIntervalMax);
                }
                break;
        }
    }

    void StartHopperTelegraph()
    {
        hopperAttackState = HopperAttackState.Telegraph;
        hopperTelegraphTotalDuration = Random.Range(hopperTelegraphDurationMin, hopperTelegraphDurationMax);
        hopperAttackTimer = hopperTelegraphTotalDuration;
        float dx = player != null ? player.position.x - transform.position.x : hopperAttackFacingDir;
        hopperAttackFacingDir = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : hopperAttackFacingDir;
        if (hopperTelegraphMarkerGO != null) hopperTelegraphMarkerGO.SetActive(true);
    }

    void StartHopperAttack()
    {
        hopperAttackState = HopperAttackState.Attack;
        hopperAttackTimer = hopperAttackActiveDuration;
        if (hopperTelegraphMarkerGO != null) hopperTelegraphMarkerGO.SetActive(false);
        if (hopperHitboxGO != null)
        {
            hopperHitboxGO.transform.localPosition = new Vector3(hopperAttackFacingDir * hopperHitboxOffsetX, 0f, 0f);
            hopperHitboxGO.SetActive(true);
            SpawnMeleeSlash(hopperHitboxGO.transform.position, hopperAttackFacingDir, Mathf.Max(hopperHitboxWidth, hopperHitboxHeight));
        }
    }

    void EndHopperAttack()
    {
        hopperAttackState = HopperAttackState.Recover;
        hopperAttackTimer = hopperRecoverDuration;
        if (hopperHitboxGO != null) hopperHitboxGO.SetActive(false);
    }

    void UpdateHopperMovement()
    {
        hopperActionTimer -= Time.deltaTime;

        if (hopperMoveState == HopperMoveState.Hop)
        {
            hopperHopElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(hopperHopElapsed / Mathf.Max(0.05f, hopperHopDuration));
            float x = Mathf.Lerp(hopperHopStartX, hopperHopTargetX, t);
            float arc = Mathf.Sin(t * Mathf.PI) * hopperHopHeight;
            SetGroundedXWithExtraY(x, arc);
            if (t >= 1f) SetGroundedXWithExtraY(hopperHopTargetX, 0f); // 浮動小数誤差を消して確実に地面へ
        }
        else if (hopperMoveState == HopperMoveState.Move)
        {
            float nextX = transform.position.x + hopperMoveDirSign * hopperMoveSpeed * Time.deltaTime;
            if (Mathf.Abs(nextX - spawnX) > hopperLeashRange) hopperMoveDirSign = -hopperMoveDirSign;
            SetGroundedX(transform.position.x + hopperMoveDirSign * hopperMoveSpeed * Time.deltaTime);
        }

        if (hopperActionTimer <= 0f) PickHopperMoveAction();
    }

    void PickHopperMoveAction()
    {
        float roll = Random.value;
        if (roll < 0.35f)
        {
            hopperMoveState = HopperMoveState.Pause;
            hopperActionTimer = Random.Range(hopperActionDurationMin, hopperActionDurationMax);
        }
        else if (roll < 0.65f)
        {
            hopperMoveState = HopperMoveState.Move;
            hopperMoveDirSign = Random.value < 0.5f ? -1f : 1f;
            hopperActionTimer = Random.Range(hopperActionDurationMin, hopperActionDurationMax);
        }
        else
        {
            // Hop先を事前に決めて安全確認する - 地面が無い/穴に近い/leash外
            // ならその場(水平移動なし)でHopする(「穴や地形を無視して空中
            // 停止しない」を構造的に保証)。
            float dir = Random.value < 0.5f ? -1f : 1f;
            float dist = Random.Range(hopperHopDistanceMin, hopperHopDistanceMax);
            float destX = transform.position.x + dir * dist;
            bool safe = TerrainManager.Instance != null
                && TerrainManager.Instance.GetHeightAt(destX).HasValue
                && !TerrainManager.Instance.IsNearPit(destX, 1.0f)
                && Mathf.Abs(destX - spawnX) <= hopperLeashRange;
            hopperHopStartX = transform.position.x;
            hopperHopTargetX = safe ? destX : transform.position.x;
            hopperMoveState = HopperMoveState.Hop;
            hopperHopElapsed = 0f;
            hopperActionTimer = hopperHopDuration;
        }
    }

    // ===== Burrow Worm - 自然洞窟雑魚敵追加(2026-09-22) ===== //
    // Underground(地中待機、Collider/Attackとも無効・非表示) -> Telegraph
    // (地面に土煙、まだ無害) -> Emerge(浮上、この時点でCollider再有効化=
    // 攻撃可能) -> Attack(AttackHitbox有効) -> Recover -> Retreat(沈降) ->
    // Undergroundへ戻る、を繰り返す。PlayerがwormAttackRange内にいる時だけ
    // 出現を狙う(画面外からの一方的な奇襲を避ける)。
    void InitBurrowWorm()
    {
        wormVisualRenderer = GetComponentInChildren<SpriteRenderer>();
        wormBodyCollider = GetComponent<BoxCollider2D>();
        wormGroundY = transform.position.y;

        wormHitboxGO = new GameObject("WormHitbox");
        wormHitboxGO.transform.SetParent(transform, false);
        var hitboxCol = wormHitboxGO.AddComponent<BoxCollider2D>();
        hitboxCol.isTrigger = true;
        hitboxCol.size = new Vector2(wormHitboxWidth, wormHitboxHeight);
        wormHitboxGO.AddComponent<EnemyMeleeHitbox>();
        wormHitboxGO.SetActive(false);

        wormState = WormState.Underground;
        wormTimer = Random.Range(wormUndergroundMin, wormUndergroundMax);
        if (wormBodyCollider != null) wormBodyCollider.enabled = false;
        if (wormVisualRenderer != null) wormVisualRenderer.enabled = false;
        Vector3 p = transform.position;
        p.y = wormGroundY - wormBuriedDepth;
        transform.position = p;
    }

    void UpdateBurrowWorm()
    {
        wormTimer -= Time.deltaTime;
        switch (wormState)
        {
            case WormState.Underground:
                if (wormTimer <= 0f)
                {
                    if (player != null && Mathf.Abs(player.position.x - transform.position.x) <= wormAttackRange)
                    {
                        StartWormTelegraph();
                    }
                    else
                    {
                        // Playerが射程外 - 奇襲にならないよう再抽選して待つ。
                        wormTimer = Random.Range(wormUndergroundMin, wormUndergroundMax) * 0.5f;
                    }
                }
                break;

            case WormState.Telegraph:
                wormDustCooldown -= Time.deltaTime;
                if (wormDustCooldown <= 0f)
                {
                    wormDustCooldown = 0.15f;
                    Sprite dust = wormDustSprite != null ? wormDustSprite : OneShotSpriteEffect.SoftDotSprite();
                    Vector3 dustPos = new Vector3(transform.position.x + Random.Range(-0.4f, 0.4f), wormGroundY + 0.05f, 0f);
                    OneShotSpriteEffect.CreateTweened(dust, dustPos, new Color(0.55f, 0.45f, 0.35f), duration: 0.35f, startScale: 0.3f, endScale: 0.7f, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.1f);
                }
                if (wormTimer <= 0f) StartWormEmerge();
                break;

            case WormState.Emerge:
            {
                float total = Mathf.Max(0.05f, wormEmergeDuration);
                float f = Mathf.Clamp01(1f - wormTimer / total);
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(wormGroundY - wormBuriedDepth, wormGroundY, f);
                transform.position = p;
                if (wormTimer <= 0f) StartWormAttack();
                break;
            }

            case WormState.Attack:
                if (wormTimer <= 0f) EndWormAttack();
                break;

            case WormState.Recover:
                if (wormTimer <= 0f) StartWormRetreat();
                break;

            case WormState.Retreat:
            {
                float total = Mathf.Max(0.05f, wormRetreatDuration);
                float f = Mathf.Clamp01(1f - wormTimer / total);
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(wormGroundY, wormGroundY - wormBuriedDepth, f);
                transform.position = p;
                if (wormTimer <= 0f)
                {
                    if (wormBodyCollider != null) wormBodyCollider.enabled = false;
                    if (wormVisualRenderer != null) wormVisualRenderer.enabled = false;
                    wormState = WormState.Underground;
                    wormTimer = Random.Range(wormUndergroundMin, wormUndergroundMax);
                }
                break;
            }
        }
    }

    void StartWormTelegraph()
    {
        wormState = WormState.Telegraph;
        wormTimer = wormTelegraphDuration;
        wormDustCooldown = 0f;
        float dx = player != null ? player.position.x - transform.position.x : wormFacingDir;
        wormFacingDir = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : wormFacingDir;
    }

    void StartWormEmerge()
    {
        wormState = WormState.Emerge;
        wormTimer = wormEmergeDuration;
        // 出現し始めた瞬間から攻撃対象にする(「地上へ出ているAttack/Emerge
        // 中はPlayerから攻撃可能に」)。
        if (wormBodyCollider != null) wormBodyCollider.enabled = true;
        if (wormVisualRenderer != null) wormVisualRenderer.enabled = true;
    }

    void StartWormAttack()
    {
        wormState = WormState.Attack;
        wormTimer = wormAttackActiveDuration;
        if (wormHitboxGO != null)
        {
            wormHitboxGO.transform.localPosition = new Vector3(wormFacingDir * (wormHitboxWidth * 0.4f), 0f, 0f);
            wormHitboxGO.SetActive(true);
        }
    }

    void EndWormAttack()
    {
        wormState = WormState.Recover;
        wormTimer = wormRecoverDuration;
        if (wormHitboxGO != null) wormHitboxGO.SetActive(false);
    }

    void StartWormRetreat()
    {
        wormState = WormState.Retreat;
        wormTimer = wormRetreatDuration;
    }

    // ===== 攻撃の見た目(2026-09-26) =====
    // EnemyAnimatorが攻撃ポーズ(EnemyDefinition.attackSprite)を出すかどうか。予備動作〜攻撃の間。
    float shooterPoseUntil;
    bool shooterInRange;
    public bool IsInAttackPose
    {
        get
        {
            switch (kind)
            {
                case EnemyBehaviorKind.StationaryMelee:
                    return meleeAttackState == MeleeAttackState.Telegraph || meleeAttackState == MeleeAttackState.Attack;
                case EnemyBehaviorKind.CaveHopper:
                    return hopperAttackState == HopperAttackState.Telegraph || hopperAttackState == HopperAttackState.Attack;
                case EnemyBehaviorKind.Shooter:
                    return (shooterInRange && shooterTimer <= 0.4f) || Time.time < shooterPoseUntil;
                case EnemyBehaviorKind.BurrowWorm:
                    return wormState == WormState.Emerge || wormState == WormState.Attack;
                default:
                    return false;
            }
        }
    }

    static Sprite warningArt, arrowArt;
    static bool warningLoaded, arrowLoaded;
    static Sprite WarningArt() { if (!warningLoaded) { warningLoaded = true; warningArt = Resources.Load<Sprite>("Effects/warning"); } return warningArt; }
    static Sprite ArrowArt() { if (!arrowLoaded) { arrowLoaded = true; arrowArt = Resources.Load<Sprite>("Effects/arrow"); } return arrowArt; }
    // 警告マーク素材(1x1unit)は単色四角(マーカー倍率0.4前後)より見やすい大きさにする。
    float MarkerArtScale => WarningArt() != null ? 1.9f : 1f;

    // 予告マーカーを警告アイコン素材に差し替える(素材が無ければ従来の単色四角のまま)。
    static void ApplyWarningArt(SpriteRenderer markerSr)
    {
        Sprite art = WarningArt();
        if (art == null || markerSr == null) return;
        markerSr.sprite = art;
        markerSr.color = Color.white;
    }

    // 近接攻撃の判定が出た瞬間に、判定と同じ位置へ斬撃エフェクトを出す(見えている攻撃=判定)。
    void SpawnMeleeSlash(Vector3 pos, float facingDir, float size)
    {
        var fx = OneShotSpriteEffect.CreateTweened(BossFx.Slash(), pos, new Color(1f, 0.85f, 0.7f, 0.95f), 0.2f, size * 0.9f, size * 1.25f, 1f, 0f, default,
            0f, RenderOrder.CombatFx, 0.25f);
        var fxSr = fx != null ? fx.GetComponent<SpriteRenderer>() : null;
        if (fxSr != null) fxSr.flipX = facingDir < 0f; // 斬撃素材は右向き
    }
}

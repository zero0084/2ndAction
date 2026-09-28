using System.Collections.Generic;
using UnityEngine;

// ===== 天空回廊の固有Enemy(2026-09-28) ===== //
// EnemySpecialBehavior(1つのコンポーネント)のうち、天空回廊の7種の行動だけをこのファイルに分けた。
// 被弾で中断された時の扱い(EnemyController.DisableMotionComponents→このコンポーネントがenabled=false→
// 復帰時OnEnableで安全な待機へ戻す)、JOINのパペット(このコンポーネントは止まる)、攻撃判定の同期
// (EnemyMeleeHitbox/SkyStrike/SkyWarnBandが自分でNetAttackSyncへ登録)は、既存の敵と同じ仕組みのまま。
//
// 共通の考え方:
//  - 予兆(Telegraph/溜め/起動)は「画面に見えていて」「予兆+発生の時間 × 走行速度 + 余裕」より手前で始める。
//    高速になるほど早めに始めるので、見てから対応できる(プレイヤーの速度は変えない)。
//  - 攻撃の後には必ず硬直(Recover)があり、反撃できる時間を作る。長距離の追跡はしない。
//  - T0〜T5はEnemyAiTier(HPとは別軸)。各種の基本Tierを安定させることを優先している。
public partial class EnemySpecialBehavior
{
    bool IsSkyKind => kind >= EnemyBehaviorKind.SkyHound;

    [Header("天空回廊Enemy - 共通")]
    [Tooltip("予兆を始める距離の余裕(m)。予兆+発生の時間×走行速度 + この値")]
    public float skyLeadMargin = 2.5f;
    public float skyMarkerScale = 0.45f;

    [Header("Sky Hound - 予兆→短い踏み込み→噛みつき→硬直")]
    public float houndBaseRange = 4f;
    public float houndTelegraphMin = 0.45f, houndTelegraphMax = 0.6f;
    public float houndLungeDistance = 1.6f;
    public float houndLungeDuration = 0.18f;
    public float houndRecover = 0.55f;
    public float houndCooldownMin = 1.4f, houndCooldownMax = 2.4f;
    public Vector2 houndHitbox = new Vector2(1.2f, 0.8f);
    public float houndHitboxOffsetX = 0.75f;
    public float houndWanderLeash = 1.6f, houndWanderSpeed = 1.6f;

    [Header("Harpy - ホバー→予兆→短いダイブ→硬直→元の高度")]
    public float harpyBobAmplitude = 0.3f, harpyBobSpeed = 1.4f;
    public float harpyDriftLeash = 1f, harpyDriftSpeed = 0.6f;
    public float harpyBaseRange = 7f;
    public float harpyTelegraph = 0.6f;
    public float harpyDiveSpeed = 9f;
    public float harpyDiveMinAltitude = 0.9f;
    public float harpyDiveMaxHorizontal = 4f;
    public float harpyHold = 0.15f;
    public float harpyRecover = 0.45f;
    public float harpyReturnSpeed = 4f;
    public float harpyCooldownMin = 2.2f, harpyCooldownMax = 3.5f;
    public Vector2 harpyHitbox = new Vector2(1.0f, 0.9f);

    [Header("Gargoyle - 石像→接近で起動(発光+翼)→攻撃")]
    public float gargWakeBaseRange = 7f;
    public float gargWakeDuration = 0.75f;
    public float gargWakeStaggerMax = 0.35f;
    public float gargTelegraphMin = 0.55f, gargTelegraphMax = 0.7f;
    public float gargAttackActive = 0.2f;
    public float gargRecover = 0.6f;
    public float gargCooldownMin = 1.6f, gargCooldownMax = 2.8f;
    public Vector2 gargHitbox = new Vector2(1.5f, 1.3f);
    public float gargHitboxOffsetX = 0.9f;
    public float gargAttackRange = 3.2f;
    public float gargWanderLeash = 1.2f, gargWanderSpeed = 1.2f;
    public float gargApproachSpeed = 2f, gargApproachLeash = 3f;
    [Tooltip("近くのガーゴイル同士が同時に攻撃しないよう、この距離(m)以内は1体ずつ")]
    public float gargShareRange = 14f;

    [Header("Celestial Knight - 短距離だけ接近→予兆→斬撃→硬直")]
    public float knightEngageRange = 10f;
    public float knightSpeed = 2.6f;
    public float knightLeash = 6f;
    public float knightStrikeRange = 2.1f;
    public float knightTelegraphMin = 0.5f, knightTelegraphMax = 0.6f;
    public float knightAttackActive = 0.22f;
    public float knightRecover = 0.7f;
    public float knightCooldownMin = 1.0f, knightCooldownMax = 1.6f;
    public Vector2 knightHitbox = new Vector2(1.7f, 1.4f);
    public float knightHitboxOffsetX = 1.0f;
    public float knightStepDistance = 0.6f;

    [Header("Ancient Sentinel - 遅い/長い予兆/広い叩きつけ/長い硬直")]
    public float sentinelBaseRange = 5f;
    public float sentinelSpeed = 0.6f, sentinelLeash = 2.5f;
    public float sentinelTelegraphMin = 1.15f, sentinelTelegraphMax = 1.3f;
    public float sentinelAttackActive = 0.28f;
    public float sentinelRecover = 1.2f;
    public float sentinelCooldownMin = 2.0f, sentinelCooldownMax = 2.8f;
    public Vector2 sentinelHitbox = new Vector2(3.4f, 1.8f);
    public float sentinelHitboxOffsetX = 1.9f;

    [Header("Storm Spirit - その場で帯電→地面の予兆→落雷→硬直(追跡しない)")]
    public float stormBaseRange = 12f;
    public float stormBobAmplitude = 0.18f, stormBobSpeed = 1.1f;
    public float stormCharge = 1.0f;
    public float stormStrikeWidth = 2.4f;
    public float stormStrikeActive = 0.3f;
    public float stormRecover = 1.3f;
    public float stormCooldownMin = 1.6f, stormCooldownMax = 2.4f;

    [Header("Sky Hunter - 接近→予兆→高速突進→硬直→離脱→再接近(回数上限あり)")]
    public float hunterEngageRange = 22f;
    public float hunterStalkAhead = 5.5f, hunterStalkHeight = 2.6f;
    [Tooltip("位置取りの最高速度 = プレイヤーの走行速度 + この値(プレイヤーより遥かに速く追い続けない)")]
    public float hunterSpeedBonus = 2.5f;
    public float hunterTelegraph = 0.5f;
    public float hunterDashSpeed = 14f;
    public float hunterDashMaxDuration = 0.45f;
    public float hunterRecover = 0.8f;
    public float hunterRetreat = 0.8f;
    public int hunterMaxAttacks = 3;
    public float hunterMaxEngageTime = 14f;
    public Vector2 hunterHitbox = new Vector2(1.0f, 0.9f);

    // ---- 状態 ----
    enum SkyState { Idle, Dormant, Wake, Approach, Telegraph, Attack, Hold, Recover, Retreat, Leave }
    SkyState skyState;
    float skyTimer, skyCooldown, skyStateTotal, skyFacing = -1f;
    float skyBaseY, skyBob;
    int skyAttacks;
    float skyEngageTime;
    bool skyEngaged, skyInitialized, skyWokenOnce;
    Vector3 skyDiveFrom, skyDiveTo;
    float skyWanderDir = 1f, skyWanderTimer;
    GameObject skyHitboxGO;
    GameObject skyMarkerGO;
    Transform skyMarkerT;
    SkyStrike skyStrike;

    // ガーゴイル同士の「同時に攻撃しない」順番(近くの1体だけが予兆〜攻撃に入れる)。
    static readonly List<EnemySpecialBehavior> gargoyleAttackers = new List<EnemySpecialBehavior>();

    // ガーゴイルは出現した最初のフレーム(Start前)から石像として扱う(一瞬でも起きた姿を見せない)。
    SkyState SkyStateNow => !skyInitialized && kind == EnemyBehaviorKind.Gargoyle ? SkyState.Dormant : skyState;
    public string DebugState => IsSkyKind ? $"{kind}/{aiTier} {SkyStateNow}" : kind.ToString();

    // EnemyAnimatorが出す絵(天空回廊Enemyの状態 → EnemyPose)。既存の種は常にNone(従来の攻撃ポーズのまま)。
    public EnemyPose CurrentPose
    {
        get
        {
            if (!IsSkyKind) return EnemyPose.None;
            switch (SkyStateNow)
            {
                case SkyState.Dormant: return EnemyPose.Dormant;
                case SkyState.Wake: return EnemyPose.Wake;
                case SkyState.Telegraph: return kind == EnemyBehaviorKind.StormSpirit ? EnemyPose.Charge : EnemyPose.Telegraph;
                case SkyState.Attack:
                case SkyState.Hold:
                    return kind == EnemyBehaviorKind.Harpy || kind == EnemyBehaviorKind.SkyHunter ? EnemyPose.Dive : EnemyPose.Attack;
                case SkyState.Recover: return EnemyPose.Recover;
                default: return EnemyPose.None;
            }
        }
    }

    bool SkyInAttackPose => skyState == SkyState.Telegraph || skyState == SkyState.Attack || skyState == SkyState.Hold;

    // ===================================================================== //
    // 初期化 / 中断からの復帰
    // ===================================================================== //
    void InitSky()
    {
        skyInitialized = true;
        skyBaseY = transform.position.y;
        skyBob = Random.Range(0f, 100f);
        skyCooldown = Random.Range(0.4f, 1.2f);
        switch (kind)
        {
            case EnemyBehaviorKind.SkyHound: skyHitboxGO = SkyMakeHitbox("BiteHitbox", houndHitbox); break;
            case EnemyBehaviorKind.Harpy: skyHitboxGO = SkyMakeHitbox("DiveHitbox", harpyHitbox); break;
            case EnemyBehaviorKind.Gargoyle: skyHitboxGO = SkyMakeHitbox("ClawHitbox", gargHitbox); skyState = SkyState.Dormant; break;
            case EnemyBehaviorKind.CelestialKnight: skyHitboxGO = SkyMakeHitbox("SlashHitbox", knightHitbox); break;
            case EnemyBehaviorKind.AncientSentinel: skyHitboxGO = SkyMakeHitbox("SlamHitbox", sentinelHitbox); break;
            case EnemyBehaviorKind.SkyHunter: skyHitboxGO = SkyMakeHitbox("DashHitbox", hunterHitbox); break;
        }
        SkyMakeMarker();
    }

    // 被弾/打ち上げ等で止められた後: 途中の攻撃は再開せず、安全な待機へ戻す。
    // ガーゴイルは攻撃されたら起きる(石像へは戻らない)。
    void ResetSkyAfterInterrupt()
    {
        if (!skyInitialized) return;
        if (skyHitboxGO != null) skyHitboxGO.SetActive(false);
        if (skyMarkerGO != null) skyMarkerGO.SetActive(false);
        gargoyleAttackers.Remove(this);
        if (kind == EnemyBehaviorKind.Gargoyle) skyWokenOnce = true;
        bool flying = kind == EnemyBehaviorKind.Harpy || kind == EnemyBehaviorKind.StormSpirit || kind == EnemyBehaviorKind.SkyHunter;
        if (kind == EnemyBehaviorKind.SkyHunter && skyState == SkyState.Leave) return; // 離脱中はそのまま去る
        skyState = flying && kind != EnemyBehaviorKind.StormSpirit ? SkyState.Retreat : SkyState.Idle;
        skyTimer = kind == EnemyBehaviorKind.SkyHunter ? hunterRetreat : 0.5f;
        skyCooldown = Mathf.Max(skyCooldown, 0.8f);
    }

    void SkyOnDisable()
    {
        if (skyHitboxGO != null) skyHitboxGO.SetActive(false);
        if (skyMarkerGO != null) skyMarkerGO.SetActive(false);
        gargoyleAttackers.Remove(this);
    }

    GameObject SkyMakeHitbox(string name, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = size;
        go.AddComponent<EnemyMeleeHitbox>();
        go.SetActive(false);
        return go;
    }

    void SkyMakeMarker()
    {
        if (telegraphMarkerSprite == null) return;
        skyMarkerGO = new GameObject("SkyTelegraphMarker");
        skyMarkerGO.transform.SetParent(transform, false);
        var body = GetComponent<BoxCollider2D>();
        float top = body != null ? body.offset.y + body.size.y * 0.5f : 1.1f;
        skyMarkerGO.transform.localPosition = new Vector3(0f, top + 0.45f, 0f);
        var sr = skyMarkerGO.AddComponent<SpriteRenderer>();
        sr.sprite = telegraphMarkerSprite;
        sr.color = new Color(1f, 0.35f, 0.15f, 0.95f);
        ApplyWarningArt(sr);
        sr.sortingOrder = RenderOrder.CombatFx;
        skyMarkerT = skyMarkerGO.transform;
        skyMarkerGO.SetActive(false);
        NetAttackSync.Register(skyMarkerGO, NetAttackSync.AType.Telegraph);
    }

    // ===================================================================== //
    // 共通の道具
    // ===================================================================== //
    float SkyPlayerSpeed()
    {
        float v = NetTargets.IsMulti ? NetTargets.FrameSpeedNear(transform.position) : PlayerController.RunFrameSpeed;
        return Mathf.Max(0f, v);
    }

    // プレイヤーから見て、この敵がどれだけ前方にいるか(+ = 前方)。
    float SkyDx => transform.position.x - player.position.x;

    // 予兆を始める距離: 予兆+発生の時間 × 走行速度 + 余裕(下限baseRange)。
    float SkyLead(float leadTime, float baseRange) => Mathf.Max(baseRange, SkyPlayerSpeed() * leadTime + skyLeadMargin);

    // 画面に見えているか(予兆は見える所でだけ始める=画面外から突然攻撃しない)。
    bool SkyOnScreen(float margin = 0.6f)
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        float half = cam.orthographicSize * cam.aspect;
        float x = transform.position.x, cx = cam.transform.position.x;
        return x < cx + half - margin && x > cx - half + margin;
    }

    void SkyMarker(bool on, float progress = 0f)
    {
        if (skyMarkerGO == null) return;
        if (skyMarkerGO.activeSelf != on) skyMarkerGO.SetActive(on);
        if (on && skyMarkerT != null)
        {
            float pulse = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            skyMarkerT.localScale = Vector3.one * skyMarkerScale * MarkerArtScale * (0.45f + 0.55f * pulse);
        }
    }

    void SkyHitbox(bool on, float offsetX = 0f, float offsetY = 0f)
    {
        if (skyHitboxGO == null) return;
        if (on) skyHitboxGO.transform.localPosition = new Vector3(offsetX, offsetY, 0f);
        if (skyHitboxGO.activeSelf != on) skyHitboxGO.SetActive(on);
    }

    // 地上の敵の横移動(足場が無い所へは進まない。浮島の敵は浮島の外へ歩き出さない)。
    bool SkyMoveGround(float x)
    {
        if (surfaceOwner == null) surfaceOwner = GetComponent<EnemyController>();
        if (surfaceOwner != null && surfaceOwner.onIsland && TerrainManager.Instance != null && !TerrainManager.Instance.GetSkyHeightAt(x + Mathf.Sign(x - transform.position.x) * 0.4f).HasValue) return false;
        float? g = Surface(x);
        if (!g.HasValue) return false;
        transform.position = new Vector3(x, g.Value + groundYOffset, transform.position.z);
        return true;
    }

    float SkyFacingToPlayer() { float dx = player.position.x - transform.position.x; return Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : skyFacing; }

    void SkySlash(Vector3 pos, float facing, float size) => SpawnMeleeSlash(pos, facing, size);

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //
    void UpdateSky()
    {
        if (!skyInitialized) return;
        skyCooldown -= Time.deltaTime;
        switch (kind)
        {
            case EnemyBehaviorKind.SkyHound: UpdateHound(); break;
            case EnemyBehaviorKind.Harpy: UpdateHarpy(); break;
            case EnemyBehaviorKind.Gargoyle: UpdateGargoyle(); break;
            case EnemyBehaviorKind.CelestialKnight: UpdateKnight(); break;
            case EnemyBehaviorKind.AncientSentinel: UpdateSentinel(); break;
            case EnemyBehaviorKind.StormSpirit: UpdateStorm(); break;
            case EnemyBehaviorKind.SkyHunter: UpdateHunter(); break;
        }
    }

    // 地上の近接攻撃の共通サイクル(予兆→攻撃→硬直)。戻り値=攻撃サイクル中か。
    bool GroundAttackCycle(float telegraph, float active, float recover, float cooldownMin, float cooldownMax, float hitOffsetX, float slashSize,
        float lunge = 0f, float lungeDuration = 0.1f)
    {
        switch (skyState)
        {
            case SkyState.Telegraph:
                skyTimer -= Time.deltaTime;
                SkyMarker(true, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    skyState = SkyState.Attack;
                    skyTimer = Mathf.Max(active, lunge > 0f ? lungeDuration : 0f);
                    skyStateTotal = skyTimer;
                    SkyHitbox(true, skyFacing * hitOffsetX, 0.45f);
                    SkySlash(skyHitboxGO != null ? skyHitboxGO.transform.position : transform.position, skyFacing, slashSize);
                }
                return true;
            case SkyState.Attack:
                if (lunge > 0f) SkyMoveGround(transform.position.x + skyFacing * lunge / Mathf.Max(0.05f, lungeDuration) * Time.deltaTime);
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f)
                {
                    SkyHitbox(false);
                    skyState = SkyState.Recover;
                    skyTimer = recover;
                }
                return true;
            case SkyState.Recover:
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f)
                {
                    skyState = SkyState.Idle;
                    skyCooldown = Random.Range(cooldownMin, cooldownMax);
                    gargoyleAttackers.Remove(this);
                }
                return true;
        }
        return false;
    }

    void StartTelegraph(float duration)
    {
        skyState = SkyState.Telegraph;
        skyTimer = skyStateTotal = duration;
        skyFacing = SkyFacingToPlayer();
        SkyMarker(true, 0f);
    }

    // T2等の「狭い範囲をうろうろ」(元の位置からleash以内)。
    void SkyWander(float leash, float speed)
    {
        skyWanderTimer -= Time.deltaTime;
        if (skyWanderTimer <= 0f)
        {
            skyWanderTimer = Random.Range(0.6f, 1.6f);
            skyWanderDir = Random.value < 0.35f ? 0f : (Random.value < 0.5f ? -1f : 1f);
        }
        if (skyWanderDir == 0f) return;
        float nx = transform.position.x + skyWanderDir * speed * Time.deltaTime;
        if (Mathf.Abs(nx - spawnX) > leash || !SkyMoveGround(nx)) skyWanderDir = -skyWanderDir;
    }

    // ---- Sky Hound ----
    void UpdateHound()
    {
        float tele = Random.Range(houndTelegraphMin, houndTelegraphMax);
        if (GroundAttackCycle(0f, 0.12f, houndRecover, houndCooldownMin, houndCooldownMax, houndHitboxOffsetX, 1.2f, houndLungeDistance, houndLungeDuration)) return;
        // 待機: T2は狭い範囲を移動、T1はほぼ固定
        if (aiTier >= EnemyAiTier.T2) SkyWander(houndWanderLeash, houndWanderSpeed);
        float dx = SkyDx;
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && dx > -1.2f && dx <= SkyLead(tele + houndLungeDuration, houndBaseRange) && SkyOnScreen())
            StartTelegraph(tele);
    }

    // ---- Harpy ----
    void UpdateHarpy()
    {
        float bob = Mathf.Sin((Time.time + skyBob) * harpyBobSpeed) * harpyBobAmplitude;
        switch (skyState)
        {
            case SkyState.Idle:
            {
                // 緩い上下 + 元の位置の周りを小さく漂う(プレイヤーへ寄っては来ない)
                float nx = transform.position.x + Mathf.Sin((Time.time + skyBob) * 0.5f) * harpyDriftSpeed * Time.deltaTime;
                if (Mathf.Abs(nx - spawnX) > harpyDriftLeash) nx = transform.position.x;
                transform.position = new Vector3(nx, skyBaseY + bob, transform.position.z);
                float dx = SkyDx;
                float diveTime = 2.2f / Mathf.Max(1f, harpyDiveSpeed);
                if (aiTier >= EnemyAiTier.T2 && skyCooldown <= 0f && dx > 0.5f && dx <= SkyLead(harpyTelegraph + diveTime, harpyBaseRange) && SkyOnScreen())
                    StartTelegraph(harpyTelegraph);
                break;
            }
            case SkyState.Telegraph:
                skyTimer -= Time.deltaTime;
                // 予兆: 少し上へ身構える(見て分かる動き)
                transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, skyBaseY + 0.35f, Time.deltaTime * 6f), transform.position.z);
                SkyMarker(true, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    float? g = Surface(transform.position.x);
                    float floor = (g ?? (skyBaseY - 2f)) + harpyDiveMinAltitude;
                    float targetX = transform.position.x;
                    float targetY = Mathf.Max(floor, player.position.y + 0.4f);
                    if (aiTier >= EnemyAiTier.T3)
                    {
                        // T3: プレイヤーが着く位置を狙う(横の移動は短く制限)
                        float t = Mathf.Abs(transform.position.y - targetY) / Mathf.Max(1f, harpyDiveSpeed);
                        float px = player.position.x + SkyPlayerSpeed() * t;
                        targetX = Mathf.Clamp(px, transform.position.x - harpyDiveMaxHorizontal, transform.position.x + 1f);
                    }
                    skyDiveFrom = transform.position;
                    skyDiveTo = new Vector3(targetX, Mathf.Min(targetY, transform.position.y - 0.5f), transform.position.z);
                    skyState = SkyState.Attack;
                    SkyHitbox(true);
                }
                break;
            case SkyState.Attack:
                transform.position = Vector3.MoveTowards(transform.position, skyDiveTo, harpyDiveSpeed * Time.deltaTime);
                if ((transform.position - skyDiveTo).sqrMagnitude < 0.01f) { skyState = SkyState.Hold; skyTimer = harpyHold; }
                break;
            case SkyState.Hold:
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f) { SkyHitbox(false); skyState = SkyState.Recover; skyTimer = harpyRecover; }
                break;
            case SkyState.Recover:
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f) { skyState = SkyState.Retreat; }
                break;
            case SkyState.Retreat:
            {
                Vector3 p = transform.position;
                p.y = Mathf.MoveTowards(p.y, skyBaseY + bob, harpyReturnSpeed * Time.deltaTime);
                transform.position = p;
                if (Mathf.Abs(p.y - (skyBaseY + bob)) < 0.08f) { skyState = SkyState.Idle; skyCooldown = Random.Range(harpyCooldownMin, harpyCooldownMax); spawnX = p.x; }
                break;
            }
        }
    }

    // ---- Gargoyle ----
    void UpdateGargoyle()
    {
        float dx = SkyDx;
        switch (skyState)
        {
            case SkyState.Dormant:
                // 石像: 動かない。プレイヤーが近づいたら(見える所で、起動+予兆の時間ぶん手前から)起動する。
                if (dx > -1f && dx <= SkyLead(gargWakeDuration + gargTelegraphMin, gargWakeBaseRange) && SkyOnScreen())
                {
                    skyState = SkyState.Wake;
                    skyTimer = skyStateTotal = gargWakeDuration + Random.Range(0f, gargWakeStaggerMax); // 複数体が同時に起きない
                    SkyBossFx.Flash(transform.position + Vector3.up * 0.9f, new Color(1f, 0.8f, 0.35f, 0.9f), 1.6f, 0.25f);
                    skyWokenOnce = true;
                }
                return;
            case SkyState.Wake:
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f) { skyState = SkyState.Idle; skyCooldown = Random.Range(0.1f, 0.35f); }
                return;
        }
        if (GroundAttackCycle(0f, gargAttackActive, gargRecover, gargCooldownMin, gargCooldownMax, gargHitboxOffsetX, 1.5f)) return;
        // T2: 狭い範囲の移動 / T3: プレイヤーへ短距離接近(元の位置からleash以内)
        if (aiTier >= EnemyAiTier.T3 && dx > 1.2f && dx < SkyLead(1f, 8f))
        {
            float nx = transform.position.x - gargApproachSpeed * Time.deltaTime;
            if (Mathf.Abs(nx - spawnX) <= gargApproachLeash) SkyMoveGround(nx);
        }
        else if (aiTier >= EnemyAiTier.T2) SkyWander(gargWanderLeash, gargWanderSpeed);

        float tele = Random.Range(gargTelegraphMin, gargTelegraphMax);
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && dx > -1.2f && dx <= SkyLead(tele + gargAttackActive, gargAttackRange) && SkyOnScreen() && GargoyleMayAttack())
        {
            gargoyleAttackers.Add(this);
            StartTelegraph(tele);
        }
    }

    // 近く(gargShareRange以内)で別のガーゴイルが予兆〜攻撃中なら待つ(同時に全員が攻撃しない)。
    bool GargoyleMayAttack()
    {
        for (int i = gargoyleAttackers.Count - 1; i >= 0; i--)
        {
            var o = gargoyleAttackers[i];
            if (o == null || !o.isActiveAndEnabled) { gargoyleAttackers.RemoveAt(i); continue; }
            if (o != this && Mathf.Abs(o.transform.position.x - transform.position.x) < gargShareRange) return false;
        }
        return true;
    }

    // ---- Celestial Knight ----
    void UpdateKnight()
    {
        if (GroundAttackCycle(0f, knightAttackActive, knightRecover, knightCooldownMin, knightCooldownMax, knightHitboxOffsetX, 1.7f, knightStepDistance, knightAttackActive)) return;
        float dx = SkyDx;
        float tele = Random.Range(knightTelegraphMin, knightTelegraphMax);
        float strikeAt = Mathf.Max(knightStrikeRange, SkyPlayerSpeed() * (tele + knightAttackActive) * 0.8f + 1f);
        if (dx < -3f)
        {
            // もう通り過ぎた: 追いかけない(元の位置へゆっくり戻る)
            float back = Mathf.MoveTowards(transform.position.x, spawnX, knightSpeed * 0.5f * Time.deltaTime);
            SkyMoveGround(back);
            return;
        }
        if (skyCooldown <= 0f && Mathf.Abs(dx) <= strikeAt && SkyOnScreen()) { StartTelegraph(tele); return; }
        // 見える所まで来たら短距離だけ接近(元の位置からknightLeash以内)
        if (dx > 0f && dx <= SkyLead(1.2f, knightEngageRange) && SkyOnScreen())
        {
            float nx = transform.position.x - knightSpeed * Time.deltaTime;
            if (Mathf.Abs(nx - spawnX) <= knightLeash) SkyMoveGround(nx);
        }
    }

    // ---- Ancient Sentinel ----
    void UpdateSentinel()
    {
        if (skyState == SkyState.Telegraph && skyTimer == skyStateTotal)
        {
            // 予兆の開始: 叩きつける範囲を地面に表示(判定は無い警告だけ)
            float cx = transform.position.x + skyFacing * sentinelHitboxOffsetX;
            var band = SkyWarnBand.Create(cx - sentinelHitbox.x * 0.5f, cx + sentinelHitbox.x * 0.5f, 0f, sentinelHitbox.y, skyStateTotal);
            if (band != null) band.fixedInWorld = true;
        }
        bool wasTelegraph = skyState == SkyState.Telegraph;
        if (GroundAttackCycle(0f, sentinelAttackActive, sentinelRecover, sentinelCooldownMin, sentinelCooldownMax, sentinelHitboxOffsetX, 2.6f))
        {
            if (wasTelegraph && skyState == SkyState.Attack)
            {
                // 叩きつけの衝撃
                Vector3 at = transform.position + new Vector3(skyFacing * sentinelHitboxOffsetX, 0.15f, 0f);
                OneShotSpriteEffect.CreateTweened(BossFx.Ring(), at, new Color(1f, 0.92f, 0.7f, 0.85f), 0.35f, 1.2f, sentinelHitbox.x * 1.1f, 0.6f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
                var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
                if (cf != null) cf.Shake(0.08f, 0.15f);
            }
            return;
        }
        float dx = SkyDx;
        if (aiTier >= EnemyAiTier.T2 && dx > 1f && dx < 9f)
        {
            float nx = transform.position.x - sentinelSpeed * Time.deltaTime;
            if (Mathf.Abs(nx - spawnX) <= sentinelLeash) SkyMoveGround(nx);
        }
        float tele = Random.Range(sentinelTelegraphMin, sentinelTelegraphMax);
        float reach = sentinelHitboxOffsetX + sentinelHitbox.x * 0.5f;
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && dx > -reach && dx <= SkyLead(tele * 0.9f, sentinelBaseRange) && SkyOnScreen())
        {
            StartTelegraph(tele); // 次のフレームの先頭(skyTimer==skyStateTotal)で範囲の警告を出す
        }
    }

    // ---- Storm Spirit ----
    void UpdateStorm()
    {
        float bob = Mathf.Sin((Time.time + skyBob) * stormBobSpeed) * stormBobAmplitude;
        transform.position = new Vector3(transform.position.x, skyBaseY + bob, transform.position.z);
        switch (skyState)
        {
            case SkyState.Idle:
            {
                float dx = SkyDx;
                if (skyCooldown <= 0f && dx > -1.5f && dx <= SkyLead(stormCharge + 0.2f, stormBaseRange) && SkyOnScreen())
                {
                    skyState = SkyState.Telegraph;
                    skyTimer = skyStateTotal = stormCharge;
                    // 真下の地面に落雷の予兆(帯電の間ずっと見える)→落雷。位置はこの精霊の真下で固定(追いかけない)。
                    float? g = Surface(transform.position.x);
                    float groundY = g ?? (transform.position.y - 3f);
                    skyStrike = SkyStrike.Create(transform.position.x, stormStrikeWidth, Mathf.Max(2f, transform.position.y - groundY), stormCharge, stormStrikeActive, new Color(0.75f, 0.85f, 1f, 1f), SkyStrike.Look.Bolt);
                    if (skyStrike != null) skyStrike.fixedInWorld = true;
                    SkyMarker(true, 0f);
                }
                break;
            }
            case SkyState.Telegraph:
                skyTimer -= Time.deltaTime;
                SkyMarker(true, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                if (skyTimer <= 0f) { SkyMarker(false); skyState = SkyState.Recover; skyTimer = stormStrikeActive + stormRecover; }
                break;
            case SkyState.Recover:
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f) { skyState = SkyState.Idle; skyCooldown = Random.Range(stormCooldownMin, stormCooldownMax); }
                break;
        }
    }

    // ---- Sky Hunter ----
    void UpdateHunter()
    {
        float v = SkyPlayerSpeed();
        float maxSpeed = v + hunterSpeedBonus;
        Vector3 p = transform.position;
        float playerGround = player.position.y;
        switch (skyState)
        {
            case SkyState.Idle:
                // 見える所まで来たら狙い始める
                p.y = skyBaseY + Mathf.Sin((Time.time + skyBob) * 1.3f) * 0.25f;
                transform.position = p;
                if (SkyDx <= hunterEngageRange && SkyOnScreen(0.2f)) { skyEngaged = true; skyState = SkyState.Retreat; skyTimer = 0.2f; }
                return;
            case SkyState.Retreat: // 位置取り(プレイヤーの少し前・上へ)
            {
                if (skyEngaged) skyEngageTime += Time.deltaTime;
                Vector3 target = new Vector3(player.position.x + hunterStalkAhead + v * 0.15f, playerGround + hunterStalkHeight, p.z);
                transform.position = Vector3.MoveTowards(p, target, maxSpeed * Time.deltaTime);
                skyTimer -= Time.deltaTime;
                if (skyAttacks >= hunterMaxAttacks || skyEngageTime >= hunterMaxEngageTime) { skyState = SkyState.Leave; return; }
                if (skyTimer <= 0f && skyCooldown <= 0f && (transform.position - target).magnitude < 1.6f && SkyOnScreen(0.8f))
                {
                    skyState = SkyState.Telegraph;
                    skyTimer = skyStateTotal = hunterTelegraph;
                    SkyMarker(true, 0f);
                    SkyBossFx.Flash(transform.position, new Color(1f, 0.5f, 0.3f, 0.8f), 1.1f, 0.2f);
                }
                return;
            }
            case SkyState.Telegraph:
            {
                // 予兆中もプレイヤーとの位置関係を保つ(同じ速さで流れる)
                transform.position = new Vector3(p.x + v * Time.deltaTime, p.y, p.z);
                skyTimer -= Time.deltaTime;
                SkyMarker(true, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    // 突進の狙い: 予兆が終わった瞬間のプレイヤーの位置(+突進中に進む分)。以後は追尾しない。
                    float dist = Vector2.Distance(transform.position, player.position);
                    float t = Mathf.Min(hunterDashMaxDuration, dist / Mathf.Max(1f, hunterDashSpeed + v));
                    skyDiveTo = new Vector3(player.position.x + v * t, player.position.y + 0.5f, p.z);
                    skyTimer = hunterDashMaxDuration;
                    skyState = SkyState.Attack;
                    SkyHitbox(true);
                    skyFacing = Mathf.Sign(skyDiveTo.x - transform.position.x);
                }
                return;
            }
            case SkyState.Attack:
                transform.position = Vector3.MoveTowards(p, skyDiveTo, (hunterDashSpeed + v) * Time.deltaTime);
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f || (transform.position - skyDiveTo).sqrMagnitude < 0.02f)
                {
                    SkyHitbox(false);
                    skyAttacks++;
                    skyState = SkyState.Recover;
                    skyTimer = hunterRecover;
                }
                return;
            case SkyState.Recover:
                // 硬直: プレイヤーより遅く流れる(反撃できる時間)。
                transform.position = new Vector3(p.x + v * 0.6f * Time.deltaTime, p.y + 0.4f * Time.deltaTime, p.z);
                skyTimer -= Time.deltaTime;
                if (skyTimer <= 0f) { skyState = SkyState.Retreat; skyTimer = hunterRetreat; skyCooldown = 0.2f; }
                return;
            case SkyState.Leave:
                // 離脱: 上前方へ去り、画面外で消える(報酬なし)
                transform.position = p + new Vector3(maxSpeed + 4f, 3f, 0f) * Time.deltaTime;
                if (!SkyOnScreen(-6f) && transform.position.x > player.position.x) gameObject.SetActive(false);
                return;
        }
    }
}

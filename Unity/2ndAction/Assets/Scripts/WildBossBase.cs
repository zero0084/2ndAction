using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum WildBossKind { Wolf, GoblinRider, Serpent, Cyclops, Spider, Golem, Griffin, Hydra, Demon, Dragon, BlackKnight }

// 荒野街道ボス追加(2026-09-20) - 新ボス群(巨大オオカミ〜黒騎士)共通の基盤。
//
// 構成(1体=1つのGameObject、シングルトンなし・全状態がインスタンス内):
//   Root(this)          : 位置(worldX, 地面Y+yOffset)だけ。Colliderなし。
//   ├ Visual            : SpriteRenderer + 手続き的アニメーション(待機/移動/
//   │                     予備動作/攻撃/被弾)。向き反転もここだけ。
//   ├ Hurtbox           : プレイヤー攻撃を受けるだけのTrigger(ダメージは与えない)
//   ├ Hitbox_xxx        : BossHitbox - 攻撃中だけ有効、見えているVFXと同範囲
//   └ TelegraphMarker   : 攻撃予告ゾーン(赤い半透明)
//
// 走行との関係(既存Dragon/Majinと同じ思想): プレイヤーの基本オートラン速度
// に合わせてボスも進み続け(=間合い一定)、relVelocityぶんだけ間合いが変化
// する。プレイヤーの攻撃ロンジ等でプレイヤー側が動いた分は、そのまま間合い
// の変化になる。
public abstract partial class WildBossBase : MonoBehaviour, IBossBattleDebug
{
    public enum Pose { Idle, Move, Windup, Attack, Fly, Landing }

    [Header("Identity")]
    public string bossName = "Boss";
    public int maxHp = 300;
    public int mileReward = 50;
    public float bodyHeight = 3f;
    public float hurtWidthRatio = 0.9f;   // スプライト幅に対する被弾判定の幅
    public float hurtHeightRatio = 0.9f;
    public float frontReachRatio = 0.95f; // 半幅に対する「顔/正面」の位置

    [Header("Art (省略可: 無い姿勢はIdleで代用)")]
    public Sprite idleSprite;
    public Sprite moveSprite;
    public Sprite windupSprite;
    public Sprite attackSprite;
    public bool artFacesLeft = true;

    [Header("Refs")]
    public Sprite squareSprite;
    public Sprite hitSparkSprite;
    public Sprite deathSmokeSprite;
    public AudioClip finalHitSe;
    public AudioClip defeatSe;
    public Color defeatBurstColor = new Color(0.9f, 0.5f, 0.2f, 1f);
    public Transform player;

    [Header("Spacing (プレイヤーとの間合い = worldX - player.x)")]
    public float startGap = 9f;
    public float minGap = -4f;
    public float maxGap = 15f;
    public float enterSpeed = 5f;

    [Header("Procedural Animation")]
    public float leanDegrees = 12f;
    public float pullBackRatio = 0.08f; // bodyHeight比
    public float lungeRatio = 0.10f;
    public float hitStopOnHit = 0.03f;

    [Header("Locomotion (移動アニメーション: 棒立ち移動禁止)")]
    public LocoStyle locoStyle = LocoStyle.None;
    [Range(0f, 1f)] public float windupMoveFactor = 0.45f;  // 予備動作中も脚/身体を動かす割合
    [Range(0f, 1f)] public float attackMoveFactor = 0.4f;
    public int rigCols = 8, rigRows = 8;
    public float speedLineInterval = 0.10f;
    public bool footstepShake = false;                       // 重量級: 一歩ごとの小さな揺れ+砂埃

    [Header("Multi (複数出現時)")]
    public int slotIndex = 0;                // 同時出現の何体目か
    public float slotSpacing = 3.2f;         // 体ごとに戦闘位置をずらす間隔
    public float initialDelayPerSlot = 1.1f; // 体ごとに最初の攻撃を遅らせる秒数

    [Header("Behaviour")]
    public bool interruptible = false; // 予備動作中の被弾で攻撃キャンセル
    public int playerAttackDamageFallback = 20; // 10倍スケール

    public int Hp { get; private set; }
    public bool IsDead => dead;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // CARD BALANCE TEST のボス試験(2026-10-02): 狙ったHPちょうどにする
    public void DebugSetHp(int hp) { Hp = Mathf.Clamp(hp, 0, Mathf.Max(1, maxHp)); }
#endif
    public int Alive => dead ? 0 : 1;

    // ---- runtime ----
    protected float worldX;
    protected float relVelocity;
    const float BreakReach = 3f; // BREAK 中: 体の手前の端までこれより遠ければ、届く位置へ寄せる(m)
    bool breakPulling;
    protected float yOffset;
    protected float facing = -1f;   // -1=左(プレイヤー側), +1=右
    protected bool facingLocked;
    protected bool windingUp;
    protected bool interrupted;
    protected bool invulnerable;
    protected float attackProgress; // 攻撃直後1→0
    protected float windupProgress; // 予備動作中0→1
    protected float scaleFactor = 1f;
    protected float halfWidth;      // 世界単位
    protected float lastGroundY;
    protected PlayerController pc;
    protected Pose pose = Pose.Idle;
    protected float poseTime;

    Transform visual;
    protected SpriteRenderer sr;
    BossRig rig;
    float speedLineTimer, stepDustTimer, lastStepSin;
    protected float slotOffset => slotIndex * slotSpacing;
    BoxCollider2D hurtCol;
    DragonHealthBar hpBar;
    float hitTimer;
    bool dead;
    bool entering = true;
    Color baseColor = Color.white;
    readonly List<BossHitbox> hitboxes = new List<BossHitbox>();
    readonly List<BossTelegraphMarker> markers = new List<BossTelegraphMarker>();

    protected float FrontReach => halfWidth * frontReachRatio;
    protected float Gap => player != null ? worldX - player.position.x : 0f;
    protected float PlayerX => player != null ? player.position.x : 0f;
    protected float PlayerY => player != null ? player.position.y : 0f;
    // 顔(正面)からプレイヤーまでの距離。正=プレイヤーが顔の前方、負=顔より奥(重なり)。
    protected float FrontDist => (PlayerX - (worldX + facing * FrontReach)) * facing;
    protected float GroundY => TerrainGround(worldX);
    public Vector3 CenterWorld => transform.position + new Vector3(0f, bodyHeight * 0.5f, 0f);
    // 自動操作補助(ボス戦、2026-10-04): 今攻撃が通るか(無敵/地中/天井/登場中は通らない)と、被弾範囲
    public bool AssistTargetable => !dead && !invulnerable && !entering && hurtCol != null && hurtCol.enabled;
    public Bounds AssistBounds => hurtCol != null ? hurtCol.bounds : new Bounds(CenterWorld, new Vector3(1f, bodyHeight, 1f));

    protected abstract IEnumerator AI();
    // Init末尾(スプライト/寸法確定後)に呼ばれる。Hitbox/Marker生成用。
    protected virtual void OnInit() { }
    protected virtual void OnDamaged(int amount) { }

    // ================= 生成 =================
    public void Init(Transform playerTransform)
    {
        player = playerTransform;
        pc = PlayerController.Instance;

        BuildVisual();
        BuildHurtbox();

        Hp = maxHp;
        hpBar = DragonHealthBar.Create(squareSprite, transform, Mathf.Clamp(bodyHeight * 0.9f, 2.6f, 5f), 0.24f);
        hpBar.offset = new Vector3(0f, bodyHeight + 0.6f, 0f);
        hpBar.SetHidden();
        SetupBattleUi();

        Camera cam = Camera.main;
        float rightEdge = cam != null ? cam.transform.position.x + cam.orthographicSize * cam.aspect : PlayerX + 12f;
        if (cam != null && pc != null && player != pc.transform) rightEdge = PlayerX + (rightEdge - pc.transform.position.x); // マルチ: 最前の相手の画面の右端
        worldX = Mathf.Max(PlayerX + startGap, rightEdge) + 3f;
        lastGroundY = TerrainGround(worldX);

        OnInit();
        // ボス戦の強化(2026-10-01): 通常攻撃の被弾(ハートの数)を種類ごとに変えられる(必殺技の判定は各ボスが2に設定済み)
        if (tune != null && tune.normalDamage > CombatScale.PlayerHit) foreach (var hb in hitboxes) if (hb != null && hb.damageAmount == CombatScale.PlayerHit) hb.damageAmount = tune.normalDamage;
        ApplyTransform();
        // マルチプレイPhase 2 - HOSTでは共有ボスとして登録。JOINでパペットとして作っている時はAIを始めない。
        if (NetCombat.OnBossInit(this)) return;
        StartCoroutine(Run());
    }

    void BuildVisual()
    {
        GameObject v = new GameObject("Visual");
        v.transform.SetParent(transform, false);
        visual = v.transform;
        sr = v.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;
        sr.sprite = idleSprite;
        sr.enabled = false; // 表示は格子セル(BossRig)が担当
        rig = new BossRig(visual, rigCols, rigRows, RenderOrder.Boss);
        rig.SetSprite(idleSprite);

        float spriteH = idleSprite != null ? idleSprite.bounds.size.y : 1f;
        float spriteW = idleSprite != null ? idleSprite.bounds.size.x : 1f;
        scaleFactor = bodyHeight / Mathf.Max(0.01f, spriteH);
        halfWidth = spriteW * scaleFactor * 0.5f;
    }

    void BuildHurtbox()
    {
        GameObject h = new GameObject("Hurtbox");
        h.transform.SetParent(transform, false);
        var rb = h.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        hurtCol = h.AddComponent<BoxCollider2D>();
        hurtCol.isTrigger = true;
        hurtCol.size = new Vector2(Mathf.Max(0.5f, halfWidth * 2f * hurtWidthRatio), bodyHeight * hurtHeightRatio);
        hurtCol.offset = Vector2.zero;
        h.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
        hurtBaseLocal = h.transform.localPosition;
        var fwd = h.AddComponent<BossHurtbox>();
        fwd.owner = this;
        var dbg = h.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.6f, 0f);
    }

    // ================= Hitbox / Marker 生成 =================
    protected BossHitbox NewHitbox(string name, Vector2 relCenter, Vector2 size, Sprite vfx, Color color)
    {
        BossHitbox hb = BossHitbox.Create(transform, vfx, color, name, RenderOrder.Boss + 1);
        hb.Configure(relCenter, size);
        hitboxes.Add(hb);
        return hb;
    }

    protected BossTelegraphMarker NewMarker(Vector2 relCenter, Vector2 size)
    {
        BossTelegraphMarker m = BossTelegraphMarker.Create(transform, RenderOrder.Boss - 1);
        m.Configure(relCenter, size);
        markers.Add(m);
        return m;
    }

    void DisableAllHitboxes()
    {
        foreach (var hb in hitboxes) if (hb != null) hb.Deactivate();
        foreach (var m in markers) if (m != null) m.Hide();
    }

    // ================= Run / 入場 =================
    IEnumerator Run()
    {
        SetPose(Pose.Move);
        yield return Enter();
        relVelocity = 0f;
        SetPose(Pose.Move);

        if (hpBar != null) StartCoroutine(hpBar.RevealRoutine(0.25f));
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.06f, 0.15f);
        entering = false;

        // 複数体のときは体ごとに最初の攻撃を遅らせる(同時攻撃で回避不能にならないように)。
        if (slotIndex > 0) yield return Wait(slotIndex * initialDelayPerSlot);
        battleStartedAt = Time.time;
        phaseUnlockedAt = Time.time;
        yield return AI();
    }

    // 登場: 既定は前方から高速で走り込んで間合い(startGap)まで詰める。ボスごとに上書きする。
    protected virtual IEnumerator Enter()
    {
        SetPose(Pose.Move);
        float safety = 0f;
        while (Gap > startGap && safety < 6f)
        {
            relVelocity = -enterSpeed;
            safety += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
    }

    // ================= 毎フレーム =================
    void Update()
    {
        if (NetPuppet) { NetPuppetUpdate(); return; }
        if (dead) return;
        float dt = Time.deltaTime;
        float baseSpeed = TargetBaseSpeed();
        float runSpeed = baseSpeed;
        // マルチ Phase 3.1: 後ろの人が離れていたら並走を少し遅くし、追い越されたら後ろの人へ狙いを替える(BossLeash)
        BossLeash.Result leash = default;
        bool leashOn = BossLeash.Enabled && !entering;
        if (leashOn)
        {
            leash = BossLeash.Evaluate(worldX, player, netTarget != null ? netTarget.TargetPlayer : 0, ref leashTime);
            if (netTarget != null) netTarget.SetPreferred(leash.PreferTarget);
            if (leash.Active) baseSpeed *= leash.SpeedFactor;
        }
        worldX += (baseSpeed + relVelocity * MoveScale) * dt; // MoveScale: ラスダンの移動速度(接近/間合い/突進)

        float px = PlayerX;
        float gap = worldX - px;
        if (freeGap) lastFreeGapTime = Time.time;
        // #100 ULTIMATE: ボス戦でプレイヤーがボスを通り抜けて戻る間は、間合いの即時補正をしない(終わった後は素早く戻す)
        if (UltimateArt.ArenaFreeGap) lastFreeGapTime = Time.time;
        if (!entering && !freeGap && !UltimateArt.ArenaFreeGap)
        {
            float lo = leashOn && leash.AllowBehindTarget ? float.MinValue : minGap;
            float hi = maxGap;
            // BREAK 中: 普段の間合い(minGap/maxGap)より近くへ寄れる(届く位置へ寄せる処理のため。2026-10-07)
            if (Broken) { lo = Mathf.Min(lo, -halfWidth - 3f); hi = Mathf.Max(hi, halfWidth + 3f); }
            float excess = gap > hi ? gap - hi : gap < lo ? lo - gap : 0f;
            if (leashOn && excess > 6f)
            {
                // 狙いの相手が替わった/大きく離れた: 瞬間移動せず、速度で間合いへ戻る(誰にも見えていない時だけ位置を直す)
                worldX = BossLeash.Approach(worldX, px, minGap, maxGap, runSpeed, dt, leash.AllowBehindTarget, OffscreenAheadGap());
            }
            else
            {
                // ボス戦の強化(2026-10-01): 追い越し/画面の外からの突進の直後だけ、瞬間移動させずに素早く戻す(それ以外は従来どおり即座に範囲内へ)
                bool soft = Time.time - lastFreeGapTime < 1.5f;
                if (gap > hi) worldX = soft && gap > hi + 3f ? Mathf.MoveTowards(worldX, px + hi, 22f * dt) : px + hi;
                else if (gap < lo) worldX = soft && gap < lo - 3f ? Mathf.MoveTowards(worldX, px + lo, 22f * dt) : px + lo;
            }
        }
        BattleTick(dt);
        OffArenaWatch(dt, px);

        if (!facingLocked && player != null)
        {
            float dx = px - worldX;
            if (dx < -0.3f) facing = -1f;
            else if (dx > 0.3f) facing = 1f;
        }

        poseTime += dt;
        if (hitTimer > 0f) hitTimer -= dt;
        ApplyTransform();
        AnimateVisual();
        LocomotionFx(dt);
    }

    // 戦えない所へ消えたボスを戻す(2026-10-07): 画面から大きく外れた(地形の中/画面の外)まま 5 秒続いたら、
    // プレイヤーの前の戦える位置・普段の高さへ戻す。HP/報酬はそのまま(回復しない/報酬も出ない)。
    // 正常な行動(追い越し/画面の外からの突進=freeGap、地中/無敵の技、登場、BREAK、撃破の見た目)は数えない。
    float offArenaTime;
    public static int OffArenaRecovered { get; private set; } // 確認用
    protected virtual bool IntentionallyAway => false; // 地中に潜る等(洞窟ボスが上書き)
    void OffArenaWatch(float dt, float px)
    {
        var cam = Camera.main;
        if (cam == null || entering || dead || freeGap || invulnerable || IntentionallyAway || NetPuppet || !BossManager.Instance || !BossManager.Instance.IsBossPhase) { offArenaTime = 0f; return; }
        Vector3 vp = cam.WorldToViewportPoint(new Vector3(worldX, TerrainGround(worldX) + yOffset + bodyHeight * 0.5f, 0f));
        bool away = vp.x < -0.15f || vp.x > 1.15f || vp.y < -0.3f || vp.y > 1.3f;
        offArenaTime = away ? offArenaTime + dt : 0f;
        if (offArenaTime < 5f) return;
        offArenaTime = 0f; OffArenaRecovered++;
        float gapTo = Mathf.Clamp(6f, minGap, maxGap);
        Debug.LogWarning($"[BossBattle] {bossName} was out of the fighting area for 5s (viewport {vp.x:F2},{vp.y:F2}) -> back in front of the player (hp {Hp} unchanged)");
        worldX = px + gapTo;
        yOffset = restAltitude;
        relVelocity = 0f;
        if (supportsInterrupt) InterruptAI(Recover(0.4f));
    }

    void ApplyTransform()
    {
        float g = TerrainGround(worldX);
        transform.position = new Vector3(worldX, g + yOffset, 0f);
        // 被弾判定は体の見た目に合わせる(天井にぶら下がる=体を上下反転した時は、判定も根元から下側へ。2026-10-07)
        if (hurtCol != null)
        {
            float sy = Mathf.Clamp(extraScale.y, -1f, 1f); // 反転の途中(登る/降りる)も見た目と同じく連続で
            hurtCol.transform.localPosition = new Vector3(hurtBaseLocal.x, hurtBaseLocal.y * sy, 0f);
        }
    }
    Vector2 hurtBaseLocal;

    // 自然洞窟ボス拡張(2026-09-22) - 荒野街道は単一のflatな下ルートしか
    // 想定していなかったため`protected virtual`にし、洞窟ボス(上下ルート/
    // 地中移動)側で「今どのルートの床を基準にするか」を差し替えられる
    // ようにした。既定の挙動(下ルートのGetHeightAt)は完全に不変。
    protected virtual float TerrainGround(float x)
    {
        if (TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(x);
            if (h.HasValue) lastGroundY = h.Value;
        }
        return lastGroundY;
    }

    // 自然洞窟ボス拡張(2026-09-22) - 天井を利用する攻撃(落石/天井めり込み
    // 回避判定)用のヘルパー。荒野街道では常にnull(天井なし)を返すだけで
    // 既存ボスの挙動には一切影響しない。cave==null(非洞窟ステージ)でも
    // 安全にnullを返す。
    protected float? CeilingWorldYAt(float x) => TerrainManager.Instance != null ? TerrainManager.Instance.GetEffectiveCeilingHeightAt(x) : null;
    protected float? CeilingWorldY => CeilingWorldYAt(worldX);
    // 通路の最低垂直間隔(自然洞窟のみ有効な値、それ以外はfloat.MaxValueで
    // 「制限なし」を表す) - 大型ボスが天井に埋まる/狭すぎる場所で攻撃不能
    // になるのを避けるための目安として使う。
    protected float PassageMinClearance => TerrainManager.Instance != null && TerrainManager.Instance.cave != null ? TerrainManager.Instance.cave.MinPassageHeight : float.MaxValue;

    // ================= 姿勢/手続き的アニメーション =================
    protected void SetPose(Pose p)
    {
        if (pose == p) return;
        pose = p;
        poseTime = 0f;
    }

    // ラストダンジョン(2026-09-30): 移動/待機/浮遊の姿勢で、1枚絵の代わりに一定のfpsで回すコマ(死神三姉妹の歩き/浮遊/スキップ)。
    protected Sprite[] loopFrames;
    protected float loopFps = 5f;
    protected float loopTimeOffset;
    // 撃破(撃破演出の最後)で、通常のボス戦の終了処理(BossManager.OnWildBossDefeated)の代わりに呼ぶ。
    // MILE/EXP(GameManager.RegisterBossDefeat)は通常どおり。死神三姉妹の最終戦で使う。
    [System.NonSerialized] public System.Action<WildBossBase> DefeatOverride;

    Sprite PickSprite()
    {
        if (loopFrames != null && loopFrames.Length > 0 && (pose == Pose.Move || pose == Pose.Idle || pose == Pose.Fly))
            return loopFrames[Mathf.FloorToInt((Time.time + loopTimeOffset) * loopFps) % loopFrames.Length];
        switch (pose)
        {
            case Pose.Move:
                if (moveSprite != null) return ((int)(Time.time * 6f) % 2 == 0) ? idleSprite : moveSprite;
                return idleSprite;
            case Pose.Windup: return windupSprite != null ? windupSprite : idleSprite;
            case Pose.Attack: return attackSprite != null ? attackSprite : idleSprite;
            case Pose.Fly: return moveSprite != null ? moveSprite : idleSprite;
            case Pose.Landing: return idleSprite;
            default:
                // 待機(Idle)は「棒立ち」にしない: 移動アニメーションを持つボスはMove姿勢で表示する。
                if (locoStyle != LocoStyle.None && moveSprite != null) return ((int)(Time.time * 6f) % 2 == 0) ? idleSprite : moveSprite;
                return idleSprite;
        }
    }

    void AnimateVisual()
    {
        if (visual == null || rig == null) return;
        rig.SetSprite(PickSprite());

        float h = bodyHeight;
        float t = poseTime;
        Vector2 off = Vector2.zero;
        float rot = 0f, sx = 1f, sy = 1f;
        Color tint = baseColor;

        switch (pose)
        {
            case Pose.Idle:
                if (locoStyle != LocoStyle.None)
                {
                    off.y = Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.03f * h;
                    rot = -leanDegrees * 0.25f * facing;   // 前傾して走る
                }
                else
                {
                    sy = 1f + 0.025f * Mathf.Sin(Time.time * 2.4f);
                    sx = 1f - 0.012f * Mathf.Sin(Time.time * 2.4f);
                }
                break;
            case Pose.Move:
                off.y = Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.03f * h;
                rot = locoStyle == LocoStyle.None ? Mathf.Sin(Time.time * 8f) * 1.8f : -leanDegrees * 0.25f * facing;
                sy = 1f + 0.02f * Mathf.Sin(Time.time * 16f);
                break;
            case Pose.Windup:
            {
                float p = windupProgress;
                float e = p * p * (3f - 2f * p);
                rot = leanDegrees * e * facing;              // 後ろへ反る
                off.x = -facing * pullBackRatio * h * e;     // 後ろへ引く
                sy = 1f - 0.08f * e;                          // 溜めて沈む
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(10f, 30f, p));
                tint = Color.Lerp(baseColor, new Color(1f, 0.45f, 0.35f, baseColor.a), blink * Mathf.Clamp01(p * 1.4f));
                break;
            }
            case Pose.Attack:
            {
                float q = attackProgress;
                rot = -leanDegrees * 0.9f * q * facing;       // 前へ突っ込む
                off.x = facing * lungeRatio * h * q;
                sx = 1f + 0.06f * q;
                sy = 1f - 0.03f * q;
                break;
            }
            case Pose.Fly:
                off.y = Mathf.Sin(Time.time * 5f) * 0.10f * h;
                rot = Mathf.Sin(Time.time * 4f) * 4f;
                sy = 1f + 0.05f * Mathf.Sin(Time.time * 12f);
                break;
            case Pose.Landing:
            {
                float k = Mathf.Clamp01(t / 0.35f);
                sy = Mathf.Lerp(0.82f, 1f, k);
                sx = Mathf.Lerp(1.12f, 1f, k);
                break;
            }
        }

        if (hitTimer > 0f)
        {
            float k = hitTimer / 0.16f;
            off.x += Mathf.Sin(Time.time * 70f) * 0.05f * h * k;
            tint = Color.Lerp(tint, new Color(1f, 0.25f, 0.25f, baseColor.a), 0.7f * Mathf.Clamp01(k));
        }

        sx *= extraScale.x;
        sy *= extraScale.y;
        rig.SetColor(tint);
        float artSign = artFacesLeft ? (facing < 0f ? 1f : -1f) : (facing < 0f ? -1f : 1f);
        visual.localScale = new Vector3(scaleFactor * artSign * sx, scaleFactor * sy, 1f);
        visual.localRotation = Quaternion.Euler(0f, 0f, rot);
        visual.localPosition = new Vector3(off.x, off.y, 0f);

        // 格子リグの変形(走り/這い/羽ばたき/なびき)。姿勢に応じて強さを変える。
        float loco = 1f;
        switch (pose)
        {
            case Pose.Windup: loco = windupMoveFactor; break;
            case Pose.Attack: loco = attackMoveFactor; break;
            case Pose.Landing: loco = 0.15f; break;
        }
        if (hitTimer > 0f) loco *= 0.5f;
        rig.Update(locoStyle, locoStyle == LocoStyle.None ? 0f : loco, Time.deltaTime, artFacesLeft ? -1f : 1f);
    }

    protected void SetVisualColor(Color c)
    {
        if (rig != null) rig.SetColor(c);
    }

    // 移動中の演出: 足元の砂埃、速度線、重量級の足音(小さな画面揺れ)。
    void LocomotionFx(float dt)
    {
        if (locoStyle == LocoStyle.None || dead || pose == Pose.Landing) return;
        float baseSpeed = TargetBaseSpeed();
        float speed = Mathf.Abs(baseSpeed + relVelocity);
        if (speed < 1f) return;

        // 速度線(ボスの後方、身体の高さのどこかに)
        speedLineTimer -= dt;
        if (speedLineTimer <= 0f)
        {
            speedLineTimer = speedLineInterval * Random.Range(0.8f, 1.4f);
            float hy = GroundY + yOffset + bodyHeight * Random.Range(0.15f, 0.9f);
            Vector3 p = new Vector3(worldX - facing * halfWidth * Random.Range(0.4f, 1.1f), hy, 0f);
            SpeedLine.Spawn(p, Random.Range(1.2f, 2.6f) * Mathf.Clamp(speed / 8f, 0.6f, 1.6f), new Color(1f, 1f, 1f, 0.22f));
        }

        bool ground = locoStyle != LocoStyle.Wing && locoStyle != LocoStyle.Cloth && !suppressLocoDust;
        if (ground && yOffset < 0.5f)
        {
            stepDustTimer -= dt;
            float interval = footstepShake ? 0.55f : 0.16f;
            if (stepDustTimer <= 0f)
            {
                stepDustTimer = interval;
                Vector3 fp = new Vector3(worldX - facing * halfWidth * 0.5f, GroundY + 0.1f, 0f);
                ImpactDust(fp, footstepShake ? 6 : 2, footstepShake ? 0.9f : 0.45f);
                if (footstepShake) Shake(0.035f, 0.1f);
            }
        }
        else if (locoStyle == LocoStyle.Wing)
        {
            // 低空飛行: 翼の起こす風(足元の砂埃)
            stepDustTimer -= dt;
            if (stepDustTimer <= 0f && yOffset < 2.5f)
            {
                stepDustTimer = 0.3f;
                ImpactDust(new Vector3(worldX, GroundY + 0.1f, 0f), 2, 0.5f);
            }
        }
    }

    // ================= AI用ヘルパー =================
    // 属性(2026-10-03): 氷(Chill/Freeze)を受けたボスは、待ち/予備動作の時間だけ少し長くなる(止まらない・並走の速さは同じ)
    protected float AiDt => Time.deltaTime * ElementStatus.AiTimeScaleOf(gameObject);

    protected IEnumerator Wait(float seconds)
    {
        float t = 0f;
        seconds *= AiWaitScale; // ラスダンの攻撃の間隔(予告=Telegraph の時間は変えない)
        while (t < seconds && !dead) { t += AiDt; yield return null; }
    }

    // ラストダンジョンの倍率(2026-10-05, LastDungeonBossTuning): 攻撃の頻度(特殊攻撃/必殺技の間隔を割る)・攻撃の間隔(待ち時間)・移動速度
    public float AiWaitScale { get; private set; } = 1f;
    public float MoveScale { get; private set; } = 1f;
    public void ApplyPace(float frequencyMul, float intervalMul, float moveMul)
    {
        AiWaitScale = Mathf.Clamp(intervalMul, 0.5f, 1.5f);
        MoveScale = Mathf.Clamp(moveMul, 0.5f, 1.5f);
        if (tune != null && frequencyMul > 0.01f && Mathf.Abs(frequencyMul - 1f) > 0.001f)
        {
            var c = tune.Clone();
            c.specialCooldown /= frequencyMul;
            if (c.ultimateCooldown > 0f) c.ultimateCooldown /= frequencyMul;
            c.firstUltimateDelay /= frequencyMul;
            tune = c;
        }
    }

    // 顔からプレイヤーまでの距離がstopDist以下になるまで接近(relVelocityで間合いを詰める)
    protected IEnumerator Approach(float stopDist, float speed, float timeout = 6f)
    {
        SetPose(Pose.Move);
        float t = 0f;
        while (t < timeout && FrontDist > stopDist + slotOffset)
        {
            relVelocity = facing * speed;
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        SetPose(Pose.Idle);
    }

    // 目標の間合い(worldX - player.x)へ向けて速度speedで移動
    protected IEnumerator MoveToGap(float targetGap, float speed, float timeout = 4f)
    {
        SetPose(Pose.Move);
        float t = 0f;
        while (t < timeout && Mathf.Abs(Gap - (targetGap + slotOffset)) > 0.25f)
        {
            relVelocity = Mathf.Sign(targetGap + slotOffset - Gap) * speed;
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        SetPose(Pose.Idle);
    }

    protected IEnumerator Retreat(float speed, float duration)
    {
        SetPose(Pose.Move);
        float t = 0f;
        while (t < duration && !dead)
        {
            relVelocity = -facing * speed;
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        SetPose(Pose.Idle);
    }

    // 予備動作: 向き固定・その場で(間合い保持)溜める。markersは進行に応じて
    // 点滅しながら濃くなる。interruptible時にプレイヤー攻撃を受けたら中断。
    protected IEnumerator Telegraph(float duration, params BossTelegraphMarker[] zones)
    {
        // 複数体: 予備動作の開始を全体で少しずらす(同時に攻撃が重ならないように)。待つ間は走り続ける。
        float gateWait = 0f;
        while (Time.time < BossAttackGate.NextTime && !dead && gateWait < 2.5f)
        {
            gateWait += Time.deltaTime;
            relVelocity = 0f;
            SetPose(Pose.Move);
            yield return null;
        }
        BossAttackGate.NextTime = Time.time + BossAttackGate.Interval;
        interrupted = false;
        windingUp = true;
        facingLocked = true;
        relVelocity = 0f;
        SetPose(Pose.Windup);
        foreach (var z in zones) if (z != null) z.Show(facing);
        // 音の再設計(2026-10-06): 予兆は必ず聞こえるように(長い溜め = 重い予兆)
        if (AudioManager.Instance != null && !NetPuppet) AudioManager.Instance.PlaySeAt(duration >= 0.8f ? SeId.BossTelegraphHeavy : SeId.BossTelegraph, CenterWorld);

        float t = 0f;
        while (t < duration && !dead)
        {
            t += AiDt;
            windupProgress = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
            foreach (var z in zones) if (z != null) z.SetProgress(windupProgress);
            if (interrupted) break;
            yield return null;
        }
        windingUp = false;
        windupProgress = 0f;
        foreach (var z in zones) if (z != null) z.Hide();
    }

    // 攻撃実行: Attack姿勢+Hitbox有効化。shake/hitStopは着弾の重さ演出。
    protected IEnumerator Strike(BossHitbox hb, float active, float shake = 0f, float hitStop = 0f)
    {
        if (AudioManager.Instance != null && !NetPuppet) AudioManager.Instance.PlaySe(SeId.BossAttack); // ボスの攻撃(共通)
        SetPose(Pose.Attack);
        attackProgress = 1f;
        StartCoroutine(FadeAttackProgress(Mathf.Max(0.15f, active + 0.1f)));
        if (shake > 0f)
        {
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(shake, 0.2f);
        }
        if (hitStop > 0f) RunHitStop(hitStop);
        yield return hb.Strike(facing, active);
    }

    // Hitboxなしで攻撃姿勢だけ見せる(飛び道具の発射など)。
    protected void PlayAttackPose(float duration)
    {
        SetPose(Pose.Attack);
        attackProgress = 1f;
        StartCoroutine(FadeAttackProgress(duration));
    }

    // 正面へ一定速度で突進(間合いが縮む方向)。終了時にrelVelocity=0。
    protected IEnumerator DashMove(float duration, float speed)
    {
        float t = 0f;
        while (t < duration && !dead)
        {
            t += Time.deltaTime;
            relVelocity = facing * speed;
            yield return null;
        }
        relVelocity = 0f;
    }

    IEnumerator FadeAttackProgress(float duration)
    {
        float t = 0f;
        while (t < duration && !dead)
        {
            t += Time.deltaTime;
            attackProgress = 1f - Mathf.Clamp01(t / duration);
            yield return null;
        }
        attackProgress = 0f;
    }

    protected void EndAttack()
    {
        facingLocked = false;
        attackProgress = 0f;
        relVelocity = 0f;
        SetPose(Pose.Idle);
    }

    protected IEnumerator Recover(float seconds)
    {
        EndAttack();
        yield return Wait(seconds);
    }

    // 予備動作を中断された時の怯み(後退して隙を見せる)
    protected IEnumerator Stagger(float seconds, float backSpeed = 2.5f)
    {
        interrupted = false;
        facingLocked = false;
        foreach (var hb in hitboxes) if (hb != null) hb.Deactivate();
        SetPose(Pose.Idle);
        float t = 0f;
        while (t < seconds && !dead)
        {
            t += Time.deltaTime;
            relVelocity = -facing * backSpeed * (1f - t / seconds);
            yield return null;
        }
        relVelocity = 0f;
    }

    // 放物線ジャンプ(間合いをgapDelta変化、頂点peak)。空中はPose.Fly。
    protected IEnumerator Leap(float duration, float peak, float gapDelta)
    {
        SetPose(Pose.Fly);
        float t = 0f;
        while (t < duration && !dead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            yOffset = 4f * peak * f * (1f - f);
            relVelocity = gapDelta / duration;
            yield return null;
        }
        yOffset = 0f;
        relVelocity = 0f;
    }

    protected IEnumerator SetAltitude(float target, float duration)
    {
        float start = yOffset;
        float t = 0f;
        while (t < duration && !dead)
        {
            t += Time.deltaTime;
            yOffset = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        yOffset = target;
    }

    protected void SetHurtboxEnabled(bool on)
    {
        if (hurtCol != null) hurtCol.enabled = on;
    }

    // ===== 天空回廊ボス追加(2026-09-25): サブクラス用の追加フック(既存ボスは使わない) =====
    // 被弾判定の範囲を差し替える(Root基準のローカル座標)。本体の一部だけが見えている
    // 超大型ボス(タイタン/リヴァイアサン等)で「見えている部分=被弾範囲」にするため。
    protected void ConfigureHurtbox(Vector2 localCenter, Vector2 size)
    {
        if (hurtCol == null) return;
        hurtCol.transform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
        hurtBaseLocal = localCenter;
        hurtCol.size = new Vector2(Mathf.Max(0.3f, size.x), Mathf.Max(0.3f, size.y));
    }

    protected void SetVisualSortingOrder(int sortingOrder)
    {
        if (rig != null) rig.SetSortingOrder(sortingOrder);
    }

    // 致死ダメージを受けた瞬間に呼ばれる。trueを返すと撃破処理に進まない(復活演出用)。
    protected virtual bool OnLethalDamage() => false;

    protected void RestoreHp(int hp)
    {
        Hp = Mathf.Clamp(hp, 1, maxHp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);
    }

    // 実行中の攻撃判定/予告をすべて止める(StopAllCoroutinesで攻撃を中断する時用)。
    protected void DisableCombatParts()
    {
        DisableAllHitboxes();
        windingUp = false;
        windupProgress = 0f;
    }

    // 身体の伸縮(クラゲの脈動等)。AnimateVisualの姿勢変形に掛け合わせる。既定=変化なし。
    protected Vector2 extraScale = Vector2.one;
    // 雲海の中を進むボス等で、足元の砂埃/足音を出さない。既定=false(従来どおり)。
    protected bool suppressLocoDust;

    // HPバーの高さ(Root基準)。雲海に半分沈んだ超大型ボスで画面外に出ないよう調整する。
    protected void SetHpBarOffset(float heightAboveRoot)
    {
        if (hpBar != null) hpBar.offset = new Vector3(0f, heightAboveRoot, 0f);
    }

    protected Sprite CurrentBodySprite => idleSprite;
    protected Vector3 VisualLocalScale => visual != null ? visual.localScale : Vector3.one;

    protected void SetAlpha(float a)
    {
        baseColor.a = a;
    }

    protected void SetBodyTint(Color c)
    {
        float a = baseColor.a;
        baseColor = c; baseColor.a = a;
    }

    // HitStop.Freezeはこのボス自身のコルーチンで回さない(撃破時のStopAllCoroutines
    // で途中停止するとTimeScale固着の原因になる - HitStop.cs冒頭のコメント参照)。
    // 常に生存しているGameManager上で実行する。
    protected static void RunHitStop(float seconds)
    {
        if (GameManager.Instance != null) GameManager.Instance.StartCoroutine(HitStop.Freeze(seconds));
    }

    protected void Shake(float magnitude, float duration = 0.2f)
    {
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(magnitude, duration);
    }

    // 着地/叩きつけの砂煙(足元ワールド座標)
    protected void ImpactDust(Vector3 pos, int count = 8, float scale = 0.9f)
    {
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), pos, new Color(0.75f, 0.65f, 0.5f, 0.8f), count, 0.5f, scale * 0.6f, scale, 3.2f, 2.2f, RenderOrder.CombatFx);
    }

    protected void GroundRing(Vector3 pos, float width, Color color)
    {
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), pos + new Vector3(0f, 0.25f, 0f), color, 0.35f, width * 0.5f, width, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.15f);
    }

    protected Vector3 FrontWorld(float forward, float height)
    {
        return new Vector3(worldX + facing * (FrontReach + forward), GroundY + yOffset + height, 0f);
    }

    protected Vector2 AimFrom(Vector3 from)
    {
        Vector3 target = player != null ? player.position + new Vector3(0f, 0.9f, 0f) : from + Vector3.left;
        return ((Vector2)(target - from)).normalized;
    }

    // ================= 被弾 =================
    public void OnHurtboxTrigger(Collider2D other)
    {
        if (dead || invulnerable) return;
        if (other.CompareTag("PlayerAttack") && PlayerAttackInfo.AlreadyHit(other, this)) return; // one hit per attack instance (2026-10-03)

        if (other.CompareTag("PlayerAttack") && NetPuppet)
        {
            NetPuppetHit(other);
            return;
        }

        if (other.CompareTag("PlayerAttack"))
        {
            int dmg = PlayerAttackInfo.ScaleDamage(other, this, PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamageFallback);
            var info = other.GetComponent<PlayerAttackInfo>();
            bool air = PlayerController.Instance != null && !PlayerController.Instance.IsGrounded;
            pendingStagger = BossBattleTuning.I.StaggerFor(info != null ? info.kind : PlayerAttackKind.Normal, air);
            NoteFinalAttack(other, info); // BOSS FINISH: 最後の一撃の向き/種類
            TakeDamage(dmg, other.bounds.center);
            return;
        }

        FireballController fb = other.GetComponent<FireballController>();
        if (fb != null && fb.reflected)
        {
            bool giant = fb.transform.localScale.x > 1.4f;
            pendingStagger = BossBattleTuning.I.staggerReflect * (giant ? 1.6f : 1f);
            TakeDamage(giant ? 6 * CombatScale.K : 2 * CombatScale.K, other.bounds.center); // 跳ね返した火球(10倍スケール)
            Destroy(fb.gameObject);
        }
    }

    // 属性(2026-10-03): 炎上/出血の継続ダメージ・連鎖の落雷。HP/段階/撃破は通常と同じ。被弾の音/揺れ/ヒットストップ/
    // コンボ数/予備動作の中断は起こさない(1秒に何度も入るため)。
    bool quietHit;
    public void TakeElementDamage(int amount, Vector3 hitPos)
    {
        quietHit = true;
        try { TakeDamage(amount, hitPos); }
        finally { quietHit = false; }
    }

    public void TakeDamage(int amount, Vector3 hitPos)
    {
        if (dead || NetPuppet) return;
        if (!quietHit && AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit); // ボス被弾(共通、連打は間引き)
        // ボス戦の強化(2026-10-01): BREAK中/必殺技の後の隙は大きく入る
        float dmgScale = (Broken ? BossBattleTuning.I.breakDamageScale : 1f) * vulnerableScale;
        if (dmgScale > 1.001f) amount = Mathf.CeilToInt(amount * dmgScale);
        float stg = pendingStagger; pendingStagger = 0f;
        int hpBeforeHit = Hp;
        Hp = Mathf.Max(0, Hp - amount);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);
        if (netAttacker <= 0) UltimateArt.OnBossDamaged(hpBeforeHit - Hp, maxHp); // #100 ULTIMATE の Gauge(発動中は溜まらない)

        // マルチプレイPhase 2 - 相手プレイヤーの攻撃では、この端末のプレイヤーの空中補助/コンボは進めない。
        if (netAttacker <= 0 && !quietHit)
        {
            if (PlayerController.Instance != null) PlayerController.Instance.NotifyAerialHit();
            if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();
        }

        if (Hp <= 0 && OnLethalDamage())
        {
            // 天空回廊ボス追加(2026-09-25) - フェニックスの復活など、致死ダメージを
            // サブクラスが引き受けた場合は撃破処理に進まない(既定はfalse=従来どおり)。
            NetCombat.AuthorityDamaged(NetId, netAttacker, amount, Hp, 0, hitPos, false);
            return;
        }

        if (Hp <= 0) { LastFinishInfo = DecideFinishInfo(); BossFinishCode = LastFinishInfo.Pack(); } // BOSS FINISH(OpDeath に載せる)
        NetCombat.AuthorityDamaged(NetId, netAttacker, amount, Hp, 0, hitPos, Hp <= 0);

        if (Hp <= 0)
        {
            dead = true;
            StopAllCoroutines();
            DisableAllHitboxes();
            relVelocity = 0f;
            if (UltimateRunning) { UltimateRunning = false; BossBattle.EndUltimate(this); }
            if (hpBar != null) hpBar.SetSub(0f, false);
            Debug.Log($"[BossBattle] {bossName} defeated phase={Phase} breaks={BreakCount} ultimates={UltimatesUsed} t={Time.time - battleStartedAt:F1}s");
            if (BossFinish.Enabled) BeginBossFinish(hitPos); // BOSS FINISH(2026-10-06): 報酬はこの瞬間、遭遇の終了は見た目の後
            else StartCoroutine(FinalHitAndDie());
            return;
        }

        CheckPhase();
        if (stg > 0f && !dead) AddStagger(stg);
        hitTimer = 0.16f;
        if (quietHit) { OnDamaged(amount); return; }
        Shake(0.06f, 0.1f);
        Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
        OneShotSpriteEffect.CreateTweened(spark, hitPos, Color.white, 0.14f, 0.35f, 0.6f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
        if (hitStopOnHit > 0f) RunHitStop(hitStopOnHit);

        if (windingUp && interruptible) interrupted = true;
        OnDamaged(amount);
    }

    // ================= ボス戦の強化(2026-10-01): 段階 / 崩し(BREAK) / 必殺技 =================
    // 調整値は BossBattleTuning(種類ごと)。tuningKeyはBossManagerが生成時に入れる(無い=段階/崩しなし=従来どおり)。
    // 割り込み(段階移行の咆哮/BREAK)は、状態の戻し方を保証したボス(supportsInterrupt)だけ: 行動のコルーチンを止めて
    // 攻撃判定/予告を消し、透明化/潜行/高度などを ResetCombatState で戻してから、咆哮/BREAKの後に行動をやり直す。
    // 対応していないボスは、段階の演出(光/揺れ/表示)だけ出して行動は止めない(崩しは種類ごとの調整値が無ければ溜まらない)。
    [System.NonSerialized] public string tuningKey;
    protected BossBattleTuning.Entry tune;
    public int Phase { get; private set; } = 1;
    public int PhaseCount => (tune != null && tune.phaseThresholds != null ? tune.phaseThresholds.Length : 0) + 1;
    public bool Broken { get; private set; }
    public int BreakCount { get; private set; }
    public float StaggerFraction => tune != null && tune.staggerMax > 0f ? Mathf.Clamp01(stagger / tune.staggerMax) : 0f;
    public bool UltimateRunning { get; private set; }
    public int UltimatesUsed { get; private set; }
    public int SpecialsUsed { get; private set; }
    public bool IsEntering => entering;
    public float HalfWidth => halfWidth;
    protected bool supportsInterrupt;
    protected bool freeGap;               // 間合いの制限を外す(追い越し/画面の反対側からの突進)
    float lastFreeGapTime = -99f;
    protected float restAltitude;         // 浮いているボスの普段の高さ(BREAKで地面へ落ちた後に戻る)
    protected float staggerDefense = 1f;  // 崩しの溜まりやすさ(必殺技の後の隙で上がる)
    protected float vulnerableScale = 1f; // 受けるダメージ倍率(ゴーレムのコア露出など)
    protected float battleStartedAt = -1f, phaseUnlockedAt, lastSpecialTime = -99f, lastUltimateTime = -99f;
    float stagger, lastStaggerTime, pendingStagger;
    bool phaseRoaring;

    // 再戦の強化(2026-10-02): この個体だけの調整値の写しを作って、崩し/間隔/段階を変える(共有の調整値は変えない)
    public int RematchTierApplied { get; private set; } = -1;
    public void ApplyRematch(BossRematchTuning.Tier t)
    {
        if (t == null || tune == null) return;
        var c = tune.Clone();
        c.staggerMax *= Mathf.Max(0.1f, t.staggerMul);
        c.specialCooldown *= Mathf.Max(0.1f, t.cooldownMul);
        if (c.ultimateCooldown > 0f) c.ultimateCooldown *= Mathf.Max(0.1f, t.cooldownMul);
        c.firstUltimateDelay *= Mathf.Max(0.1f, t.cooldownMul);
        var th = new System.Collections.Generic.List<float>(c.phaseThresholds ?? new float[0]);
        if (t.extraPhase)
        {
            float last = th.Count > 0 ? th[th.Count - 1] : 1f;
            if (th.Count < 3) th.Add(Mathf.Clamp(last * 0.5f, 0.12f, 0.9f));
        }
        for (int i = 0; i < th.Count; i++) th[i] = Mathf.Clamp(th[i] + t.phaseShift, 0.05f, 0.95f);
        th.Sort((a, b) => b.CompareTo(a));
        c.phaseThresholds = th.ToArray();
        tune = c;
        RematchTierApplied = BossRematchTuning.I.tiers.IndexOf(t);
        Debug.Log($"[BossPool] {bossName} rematch tier={t.label} stagger={c.staggerMax:F0} special={c.specialCooldown:F1}s ult={c.ultimateCooldown:F1}s phases=[{string.Join(",", c.phaseThresholds)}]");
    }

    public void ApplyTuning(string key)
    {
        tuningKey = key;
        tune = BossBattleTuning.I.For(key);
        if (tune.hpScale > 0f && Mathf.Abs(tune.hpScale - 1f) > 0.001f) maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * tune.hpScale));
    }

    // Init(HPバーができた直後): 崩しゲージ/段階の目盛り
    void SetupBattleUi()
    {
        if (hpBar == null || tune == null) return;
        if (tune.staggerMax > 0f) hpBar.EnableSub(squareSprite, 0.08f);
        if (tune.phaseThresholds != null && tune.phaseThresholds.Length > 0) hpBar.SetPhaseTicks(squareSprite, tune.phaseThresholds);
    }

    void BattleTick(float dt)
    {
        OnBattleTick(dt);
        if (tune == null) return;
        if (!Broken && stagger > 0f && Time.time - lastStaggerTime > BossBattleTuning.I.staggerRecoveryDelay)
            stagger = Mathf.Max(0f, stagger - tune.staggerRecoveryPerSec * dt);
        if (hpBar != null && tune.staggerMax > 0f) hpBar.SetSub(StaggerFraction, Broken);
    }

    void CheckPhase()
    {
        if (tune == null || tune.phaseThresholds == null || tune.phaseThresholds.Length == 0) return;
        float f = (float)Hp / Mathf.Max(1, maxHp);
        int p = 1;
        foreach (float th in tune.phaseThresholds) if (f <= th) p++;
        if (p <= Phase) return;
        Phase = p;
        phaseUnlockedAt = Time.time;
        Debug.Log($"[BossBattle] {bossName} PHASE {Phase} (hp {Hp}/{maxHp}) t={Time.time - battleStartedAt:F1}s");
        if (hpBar != null) hpBar.Flash(0.8f);
        if (supportsInterrupt && !entering && !Broken) InterruptAI(PhaseRoar());
        else PhaseFx();
    }

    void PhaseFx()
    {
        Shake(0.22f, 0.4f);
        Vector3 c = CenterWorld;
        Color col = Phase >= PhaseCount ? new Color(1f, 0.35f, 0.25f, 0.95f) : new Color(1f, 0.8f, 0.35f, 0.95f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), c, col, 0.5f, bodyHeight * 0.4f, bodyHeight * 2.4f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), c, new Color(1f, 1f, 1f, 0.8f), 0.35f, bodyHeight * 0.3f, bodyHeight * 1.6f, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossPhase); // 2026-10-06: 段階が上がる専用の音
        BossBattleHud.Banner(Phase >= PhaseCount ? "最終段階!" : "激昂!", col, 1.2f);
    }

    // 段階移行: 咆哮(短い無敵+発光+揺れ)。新しい攻撃はPhaseを見て各ボスのAIが解禁する。
    IEnumerator PhaseRoar()
    {
        phaseRoaring = true;
        invulnerable = true;
        if (Mathf.Abs(yOffset - restAltitude) > 0.05f) yield return SetAltitude(restAltitude, 0.2f);
        SetPose(Pose.Windup);
        PhaseFx();
        float t = 0f;
        Color glow = Phase >= PhaseCount ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.85f, 0.45f);
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            windupProgress = Mathf.Clamp01(t / 0.6f);
            SetBodyTint(Color.Lerp(Color.white, glow, Mathf.PingPong(t * 6f, 1f)));
            if (Random.value < 0.25f) ImpactDust(new Vector3(worldX + Random.Range(-halfWidth, halfWidth), GroundY, 0f), 3, 0.8f);
            yield return null;
        }
        windupProgress = 0f;
        SetBodyTint(Color.white);
        invulnerable = false;
        phaseRoaring = false;
        SetPose(Pose.Idle);
        if (tune != null) lastSpecialTime = Time.time - tune.specialCooldown + 0.6f; // 新しい攻撃をすぐ見せる
    }

    void AddStagger(float v)
    {
        if (BossBattle.DebugNoStagger) return;
        if (tune == null || tune.staggerMax <= 0f || Broken || phaseRoaring || entering) return;
        stagger += v * staggerDefense;
        lastStaggerTime = Time.time;
        if (stagger < tune.staggerMax) return;
        stagger = tune.staggerMax;
        Broken = true;
        BreakCount++;
        Debug.Log($"[BossBattle] {bossName} BREAK #{BreakCount} t={Time.time - battleStartedAt:F1}s");
        BossBattleHud.Banner("BREAK!", new Color(1f, 0.85f, 0.3f), 1.0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossBreak); // 2026-10-06: 崩しの専用の音(割れる)
        RunHitStop(0.08f);
        if (supportsInterrupt) InterruptAI(BreakRoutine());
        else StartCoroutine(BreakTimer());
    }

    IEnumerator BreakTimer()
    {
        yield return Wait(tune.breakDuration);
        Broken = false; stagger = 0f;
    }

    // BREAK: 地面へ落ちて数秒無防備(攻撃しない、ダメージ増し)
    IEnumerator BreakRoutine()
    {
        SetPose(Pose.Landing);
        Shake(0.18f, 0.25f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        if (yOffset > 0.05f) StartCoroutine(SetAltitude(0f, 0.3f));
        float t = 0f, starT = 0f;
        float dur = tune.breakDuration;
        while (t < dur && !dead)
        {
            t += Time.deltaTime;
            starT -= Time.deltaTime;
            relVelocity = -facing * 1.2f * Mathf.Clamp01(1f - t / 0.5f); // 少しよろけて下がる
            // 2026-10-07: BREAK はプレイヤーへのご褒美の時間。体の手前の端が届かない距離(3m超)にいる時だけ、
            // 届く位置(手前の端がプレイヤーから2m)へ滑らかに寄せる(瞬間移動しない)。届く位置にいるボスは動かさない
            if (t > 0.3f)
            {
                float gapNow = worldX - PlayerX;
                // 手前の端は被弾判定(hurtCol)で測る(絵の幅とは違うことがある)
                float half = hurtCol != null && hurtCol.enabled ? hurtCol.bounds.extents.x : halfWidth;
                float centerOff = hurtCol != null && hurtCol.enabled ? hurtCol.bounds.center.x - worldX : 0f;
                float g = gapNow + centerOff;
                float edge = Mathf.Abs(g) - half;
                if (edge > BreakReach) breakPulling = true;   // 届かない → 寄せ始める
                else if (edge <= 2.2f) breakPulling = false;  // 手前の端が2m付近まで来たら止める(届くぎりぎりで止めない)
                if (breakPulling)
                {
                    float want = Mathf.Sign(g == 0f ? 1f : g) * (half + 2f) - centerOff;
                    relVelocity = Mathf.Clamp((want - gapNow) / 0.25f, -45f, 45f);
                }
            }
            SetBodyTint(Color.Lerp(new Color(0.7f, 0.8f, 1f), Color.white, Mathf.PingPong(t * 2.5f, 0.6f)));
            if (starT <= 0f)
            {
                starT = 0.22f;
                Vector3 hp = transform.position + new Vector3(Random.Range(-halfWidth * 0.5f, halfWidth * 0.5f), bodyHeight * Random.Range(0.75f, 1.05f), 0f);
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), hp, new Color(1f, 0.95f, 0.45f, 1f), 0.4f, 0.25f, 0.05f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            }
            if (pose == Pose.Landing && poseTime > 0.45f) SetPose(Pose.Idle);
            yield return null;
        }
        relVelocity = 0f; breakPulling = false;
        SetBodyTint(Color.white);
        Broken = false;
        stagger = 0f;
        staggerDefense = 1f;
        if (restAltitude > 0.01f) yield return SetAltitude(restAltitude, 0.4f);
        SetPose(Pose.Idle);
    }

    // 行動の割り込み: 実行中の攻撃をすべて止め、状態を戻し、thenの後に行動(AI)をやり直す。
    protected void InterruptAI(IEnumerator then)
    {
        if (dead || NetPuppet) return;
        StopAllCoroutines();
        if (hpBar != null && !entering) hpBar.ForceShown(); // 表示の演出の途中で止めても、HPバーは出たままにする
        ResetCombatState();
        StartCoroutine(InterruptThen(then));
    }

    IEnumerator InterruptThen(IEnumerator then)
    {
        yield return then;
        if (!dead) yield return AI();
    }

    protected void ResetCombatState()
    {
        DisableAllHitboxes();
        windingUp = false; windupProgress = 0f; interrupted = false;
        facingLocked = false; attackProgress = 0f; relVelocity = 0f;
        invulnerable = false; freeGap = false;
        SetHurtboxEnabled(true);
        SetAlpha(1f);
        SetBodyTint(Color.white);
        extraScale = Vector2.one;
        if (yOffset < 0f) yOffset = 0f;
        if (UltimateRunning) { UltimateRunning = false; BossBattle.EndUltimate(this); }
        vulnerableScale = 1f;
        staggerDefense = 1f;
        OnInterrupted();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DebugAddStagger(float v) => AddStagger(v);
    public float DebugStagger => stagger;
#endif
    // 自然洞窟ボス強化(2026-10-04): 開発用の強制操作(段階/必殺技/BREAK)。荒野街道のボスにも使える(IBossBattleDebug。ゲーム中は呼ばれない)
    public string DebugName => bossName;
    public bool DebugAlive => !dead && isActiveAndEnabled;
    public void DebugSetPhase(int p)
    {
        if (tune == null || dead) return;
        p = Mathf.Clamp(p, 1, PhaseCount);
        float frac = p == 1 ? 1f : tune.phaseThresholds[p - 2] - 0.03f;
        Hp = Mathf.Clamp(Mathf.FloorToInt(maxHp * frac), 1, maxHp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp));
        if (p < Phase) { Phase = p; Debug.Log($"[BossBattle] {bossName} DEBUG phase back to {p}"); }
        else CheckPhase();
    }
    public bool DebugForceUltimate()
    {
        if (tune == null || dead || tune.ultimateCooldown <= 0f) return false;
        if (Phase < 2) DebugSetPhase(2);
        lastUltimateTime = -99f;
        phaseUnlockedAt = Time.time - 99f;
        // BREAKの途中で割り込むとBREAKの終わりの処理が走らないので、ここで戻す
        if (Broken) { Broken = false; stagger = 0f; staggerDefense = 1f; }
        if (supportsInterrupt && !entering) InterruptAI(Wait(0.05f));
        BossBattle.LastUltimateEnd = -99f; // 必殺技の連続の間隔(2.5秒)を待たない(割り込みで今の必殺技を終わらせた後に戻す)
        return true;
    }
    public void DebugForceBreak() { if (tune != null && tune.staggerMax > 0f) { bool keep = BossBattle.DebugNoStagger; BossBattle.DebugNoStagger = false; AddStagger(tune.staggerMax * 1.5f / Mathf.Max(0.1f, staggerDefense)); BossBattle.DebugNoStagger = keep; } }
    // 各ボスが攻撃中に出した自分の物(溜めの玉など)を片付ける
    protected virtual void OnInterrupted() { }
    // 毎フレーム(行動のコルーチンとは別。割り込みで止まらない見た目の更新用)
    protected virtual void OnBattleTick(float dt) { }

    // ---- 特殊攻撃/必殺技の順番 ----
    protected bool SpecialReady(int minPhase) => tune != null && Phase >= minPhase && Time.time - lastSpecialTime >= tune.specialCooldown;
    protected void MarkSpecial() { lastSpecialTime = Time.time; SpecialsUsed++; }
    protected bool UltimateReady(int minPhase) => tune != null && tune.ultimateCooldown > 0f && Phase >= minPhase
        && Time.time - lastUltimateTime >= tune.ultimateCooldown && Time.time - phaseUnlockedAt >= tune.firstUltimateDelay && !BossBattle.UltimateActive;

    // 必殺技の開始(他のボスが必殺技中なら始めない)。名前を大きく出し、溜めの間から雑魚の攻撃を遅らせる。
    protected bool BeginUltimate(string title, Color color)
    {
        if (!BossBattle.TryBeginUltimate(this)) return false;
        UltimateRunning = true;
        UltimatesUsed++;
        lastUltimateTime = Time.time;
        Debug.Log($"[BossBattle] {bossName} ULTIMATE '{title}' #{UltimatesUsed} t={Time.time - battleStartedAt:F1}s");
        BossBattleHud.Banner(title, color, 1.6f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossUltimate); // 2026-10-06: ボスの必殺技の専用の音
        Shake(0.12f, 0.35f);
        return true;
    }

    protected void EndUltimate()
    {
        if (!UltimateRunning) return;
        UltimateRunning = false;
        lastUltimateTime = Time.time;
        BossBattle.EndUltimate(this);
    }

    // 必殺技/大技の後の大きな隙: その場で崩れて動かない。崩しが溜まりやすく、ダメージも少し増える。
    protected IEnumerator Exhausted(float seconds, float staggerMul = 2f, float dmgMul = 1.2f)
    {
        EndUltimate();
        EndAttack();
        freeGap = false;
        SetPose(Pose.Landing);
        staggerDefense = staggerMul;
        vulnerableScale = dmgMul;
        float t = 0f, starT = 0f;
        while (t < seconds && !dead)
        {
            t += Time.deltaTime;
            starT -= Time.deltaTime;
            // 隙はプレイヤーが叩ける位置で見せる(間合いが空いていたら、滑りながら手前まで寄ってくる)
            float fd = FrontDist;
            relVelocity = fd > 1.2f ? facing * Mathf.Min(8f, fd * 2.5f) : 0f;
            if (restAltitude <= 0.01f && yOffset > 0.02f) yOffset = Mathf.MoveTowards(yOffset, 0f, 6f * Time.deltaTime);
            SetBodyTint(Color.Lerp(Color.white, new Color(0.75f, 0.85f, 1f), 0.5f + 0.5f * Mathf.Sin(t * 5f)));
            if (starT <= 0f)
            {
                starT = 0.35f;
                Vector3 hp = transform.position + new Vector3(Random.Range(-halfWidth * 0.4f, halfWidth * 0.4f), bodyHeight * Random.Range(0.8f, 1.05f), 0f);
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), hp, new Color(0.8f, 0.9f, 1f, 0.9f), 0.45f, 0.2f, 0.05f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            }
            if (pose == Pose.Landing && poseTime > 0.4f) SetPose(Pose.Idle);
            yield return null;
        }
        SetBodyTint(Color.white);
        staggerDefense = 1f;
        vulnerableScale = 1f;
        SetPose(Pose.Idle);
    }

    // 必殺技の部品(重い一撃=ultimateDamage)
    protected int UltimateDamage => Mathf.Max(1, BossBattleTuning.I.ultimateDamage); // 2026-10-02: 20(旧2)

    protected BossProjectile Projectile(Sprite sprite, Color color, Vector3 pos, Vector2 size, Vector2 velocity, float life, bool heavy)
    {
        var p = BossProjectile.Create(sprite, color, pos, size, velocity, life, RenderOrder.Boss + 1);
        if (heavy) p.damageAmount = UltimateDamage;
        return p;
    }

    protected TrackedHazard Hazard(float worldXPos, float width, float height, float warn, float active, Color activeColor, bool heavy)
    {
        var h = TrackedHazard.Create(worldXPos, width, height, warn, active, activeColor);
        if (heavy) h.damageAmount = UltimateDamage;
        return h;
    }

    // 予告だけの地面の帯(画面上の位置に固定して走行と一緒に流れる)。dur秒で消える
    protected void WarnZone(float worldXPos, float width, float height, float dur)
    {
        TrackedHazard.Create(worldXPos, width, height, dur, 0f, Color.clear);
    }

    // 走行と一緒に流れる予告帯(プレイヤーからrelX、高さ yLow〜yHigh)。tint: 低い攻撃=赤 / 高い攻撃=紫 など、避け方の違いを色で示す
    protected void LaneWarn(float relX, float width, float yLow, float yHigh, float dur, Color tint)
    {
        var h = TrackedHazard.CreateLifted(PlayerX + relX, width, Mathf.Max(0.2f, yHigh - yLow), yLow, dur, 0f, Color.clear);
        h.warnTint = tint;
    }

    // 進路を横切る帯状の攻撃(前方 startRelX から、走行の座標系で speed で迫る)。yLow〜yHigh の高さ、長さ length。
    // 当たっても消えない(体の一部/衝撃波)。heavy=必殺技のダメージ。
    protected BossProjectile LaneWave(Sprite sprite, Color c, float yLow, float yHigh, float length, float startRelX, float speed, bool heavy)
    {
        float h = Mathf.Max(0.3f, yHigh - yLow);
        float x = PlayerX + startRelX;
        float g = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(x) ?? GroundY) : GroundY;
        var p = Projectile(sprite, c, new Vector3(x, g + yLow + h * 0.5f, 0f), new Vector2(length, h), new Vector2(-Mathf.Sign(startRelX) * speed, 0f), 6f, heavy);
        p.passThrough = true;
        p.hugGround = true;
        p.groundOffset = yLow + h * 0.5f;
        return p;
    }

    public static readonly Color LowLaneColor =new Color(1f, 0.25f, 0.12f, 1f);   // 地面すれすれ = 跳ぶ
    public static readonly Color HighLaneColor = new Color(0.75f, 0.3f, 1f, 1f);   // 高い = 地面にいる/下攻撃で降りる
    public static readonly Color TallLaneColor = new Color(1f, 0.75f, 0.1f, 1f);   // 背が高い/長い = 二段ジャンプ

    // 画面の外(前方/後方)にあたる間合い
    // マルチ Phase 3.1: 「狙っている人の画面」の外。この端末のカメラとプレイヤーの差(全員同じ構図)を、狙いの相手に当てはめる。
    // 以前はこの端末(HOST)のカメラの端そのものだったため、遠くの相手を狙うボスがHOSTの画面の端へ飛んでいた。自分が狙いなら従来と同じ値。
    float CamCenterOffset(Camera cam) => pc != null ? cam.transform.position.x - pc.transform.position.x : cam.transform.position.x - PlayerX;
    protected float OffscreenAheadGap()
    {
        Camera cam = Camera.main;
        if (cam == null) return 26f;
        return CamCenterOffset(cam) + cam.orthographicSize * cam.aspect + halfWidth + 2f;
    }
    protected float OffscreenBehindGap()
    {
        Camera cam = Camera.main;
        if (cam == null) return -18f;
        return CamCenterOffset(cam) - cam.orthographicSize * cam.aspect - halfWidth - 2f;
    }

    // 画面を横切る突進/滑空(相対速度を使う攻撃)。fromGap→toGapを高さlaneで。hbは通過中ずっと有効。
    // fromGapへは瞬間移動するので、呼ぶ時は画面の外にいること。
    protected IEnumerator Swoop(float fromGap, float toGap, float speed, float lane, BossHitbox hb)
    {
        freeGap = true;
        worldX = PlayerX + fromGap;
        float dir = Mathf.Sign(toGap - fromGap);
        facing = dir; facingLocked = true;
        yOffset = lane;
        SetPose(Pose.Attack); attackProgress = 1f;
        float dur = Mathf.Abs(toGap - fromGap) / Mathf.Max(1f, speed);
        if (hb != null) StartCoroutine(hb.Strike(facing, dur));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossAttack);
        float t = 0f;
        while (t < dur && !dead)
        {
            t += Time.deltaTime;
            relVelocity = dir * speed;
            yield return null;
        }
        relVelocity = 0f;
        if (hb != null) hb.Deactivate();
        attackProgress = 0f;
    }

    // 画面の外まで素早く出る(次のSwoopの準備)。ahead=前方へ/false=後方へ
    protected IEnumerator ExitScreen(bool ahead, float speed, float lane, float timeout = 2.5f)
    {
        freeGap = true;
        facingLocked = true;
        facing = ahead ? 1f : -1f;
        SetPose(lane > 0.5f ? Pose.Fly : Pose.Move);
        float t = 0f;
        float target = ahead ? OffscreenAheadGap() : OffscreenBehindGap();
        while (t < timeout && !dead && (ahead ? Gap < target : Gap > target))
        {
            t += Time.deltaTime;
            relVelocity = facing * speed;
            yOffset = Mathf.MoveTowards(yOffset, lane, 8f * Time.deltaTime);
            yield return null;
        }
        relVelocity = 0f;
    }

    // 画面の外から戻ってくる(Swoopの後など)。普段の間合いへ
    protected IEnumerator ReturnToBattle(float gap, float speed)
    {
        freeGap = true;
        if (Gap < OffscreenBehindGap() + 1f || Gap > OffscreenAheadGap() - 1f) { worldX = PlayerX + OffscreenAheadGap(); yOffset = restAltitude; }
        facingLocked = false;
        SetPose(Pose.Move);
        float t = 0f;
        while (t < 3f && !dead && Mathf.Abs(Gap - gap) > 0.4f)
        {
            t += Time.deltaTime;
            relVelocity = Mathf.Sign(gap - Gap) * speed;
            yOffset = Mathf.MoveTowards(yOffset, restAltitude, 6f * Time.deltaTime);
            yield return null;
        }
        relVelocity = 0f;
        freeGap = false;
        SetPose(Pose.Idle);
    }

    // ================= 撃破演出(既存Dragonと同じ流れ) =================
    bool defeatRegistered;
    void RegisterDefeatOnce()
    {
        if (defeatRegistered) return;
        defeatRegistered = true;
        if (NetPuppet) return; // JOINのパペット: 撃破報酬/ボス戦終了はHOSTとラストヒットの本人が処理する
        if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        if (DefeatOverride != null) { DefeatOverride(this); return; }
        if (BossManager.Instance != null) BossManager.Instance.OnWildBossDefeated(this);
    }

    IEnumerator FinalHitAndDie()
    {
        try
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(finalHitSe);
            Shake(0.14f, 0.16f);
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, CenterWorld, Color.white, 0.18f, 0.5f, 0.9f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            yield return HitStop.Freeze(0.14f);

            // 撃破ポーズ: 崩れ落ちる(Hit扱い、暗転して沈みながらフェード)
            SetPose(Pose.Idle);
            baseColor = new Color(1f, 1f, 1f, 1f);
            SetVisualColor(new Color(0.7f, 0.9f, 1f, 1f));
            yield return new WaitForSecondsRealtime(0.08f);

            Vector3 startScale = transform.localScale;
            float duration = 0.8f;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float f = Mathf.Clamp01(t);
                float squash = Mathf.Lerp(1f, 0.6f, f);
                transform.localScale = new Vector3(Mathf.Lerp(1f, 1.15f, f), squash, 1f);
                Color c = Color.Lerp(Color.white, new Color(0.35f, 0.3f, 0.3f), f); c.a = Mathf.Lerp(1f, 0f, f);
                SetVisualColor(c);
                yield return null;
            }

            if (deathSmokeSprite != null)
            {
                OneShotSpriteEffect.CreateTweened(deathSmokeSprite, CenterWorld, Color.white, 0.4f, 0.7f * bodyHeight / 3f, bodyHeight / 3f, -1f, 0f, default, 0f, RenderOrder.CombatFx, 0.3f);
            }
            ExplosionEffect.CreateForDefeat(CenterWorld, defeatBurstColor, bodyHeight, RenderOrder.CombatFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(defeatSe);

            if (hpBar != null)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                yield return hpBar.FadeOutRoutine(0.25f);
                Destroy(hpBar.gameObject);
            }

            gameObject.SetActive(false);
            RegisterDefeatOnce();
        }
        finally
        {
            RegisterDefeatOnce();
        }
    }

    // Floating Origin: 座標を戻した分、ボス自身のワールドXも戻す(プレイヤーとの間合いは不変)。
    // ================= マルチプレイPhase 2(共有ボス) =================
    [System.NonSerialized] public int NetId;
    [System.NonSerialized] public bool NetPuppet;

    // マルチプレイPhase 2.5: HOSTのAIが狙う相手(全ての活動中プレイヤーから選ばれる)。並走の基準速度もその相手。
    EnemyTargetSelector netTarget;
    float leashTime; // マルチ Phase 3.1: 置き去り防止の減速が続いている秒数(BossLeash)
    public void NetSetTarget(Transform t, EnemyTargetSelector selector) { if (t != null) player = t; netTarget = selector; }
    protected float TargetBaseSpeed() => netTarget != null ? netTarget.TargetRunSpeed() : (pc != null ? pc.CurrentAutoRunSpeed : 0f);
    int netAttacker; // 0 = この端末のプレイヤー / それ以外 = プレイヤー番号(HOSTでリモートの攻撃を処理中)
    int netVisualOrder = int.MinValue;
    Collider2D netLastHitCollider;
    float netHitCooldown;

    // HOST: JOINのプレイヤーの攻撃を、この端末の攻撃と同じ被弾処理へ流す。
    public void NetApplyRemoteHit(int attacker, int damage, Vector3 hitPos)
    {
        if (dead || invulnerable || NetPuppet) return;
        netAttacker = attacker;
        pendingStagger = BossBattleTuning.I.staggerNormal;
        try { TakeDamage(damage, hitPos); }
        finally { netAttacker = 0; }
    }

    // JOIN: HOSTから届いたボスを「見た目と被弾判定だけ」のパペットにする(AIは動かさない)。
    public void NetMakePuppet(int id, int hp, int maxHpValue)
    {
        NetId = id;
        NetPuppet = true;
        maxHp = Mathf.Max(1, maxHpValue);
        Hp = hp;
        entering = true;
        StopAllCoroutines();
        DisableAllHitboxes();
        if (hpBar != null) { hpBar.SetFraction((float)Hp / maxHp); hpBar.SetHidden(); }
    }

    void NetPuppetUpdate()
    {
        float dt = Time.deltaTime;
        poseTime += dt;
        if (hitTimer > 0f) hitTimer -= dt;
        if (!dead) AnimateVisual();
    }

    void NetPuppetHit(Collider2D other)
    {
        if (other == netLastHitCollider && netHitCooldown > Time.time) return;
        netLastHitCollider = other;
        netHitCooldown = Time.time + 0.18f;
        int dmg = PlayerAttackInfo.ScaleDamage(other, this, PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamageFallback);
        PlayerAttackKind kind = PlayerAttackKind.Normal;
        var info = other.GetComponent<PlayerAttackInfo>();
        if (info != null) kind = info.kind;
        Vector3 hitPos = other.bounds.center;
        NetCombat.RequestHit(NetId, dmg, kind, hitPos);
        if (PlayerController.Instance != null) PlayerController.Instance.NotifyAerialHit();
        if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();
        NetHitFx(hitPos, true);
    }

    void NetHitFx(Vector3 hitPos, bool withHitStop)
    {
        hitTimer = 0.16f;
        Shake(0.06f, 0.1f);
        Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
        OneShotSpriteEffect.CreateTweened(spark, hitPos, Color.white, 0.14f, 0.35f, 0.6f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
        if (withHitStop && hitStopOnHit > 0f) RunHitStop(hitStopOnHit);
    }

    // JOIN: HOSTが確定したHP(HPバーはこの値を表示する)。
    public void NetSetHp(int hp, bool showHitFx, Vector3 hitPos)
    {
        if (dead) return;
        Hp = Mathf.Max(0, hp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp));
        if (showHitFx) NetHitFx(hitPos, false);
    }

    // JOIN: HOSTが撃破を確定した → 撃破演出(報酬/ボス戦終了はRegisterDefeatOnceで行わない)。
    public void NetPuppetDie()
    {
        if (dead) return;
        dead = true;
        Hp = 0;
        if (hpBar != null) hpBar.SetFraction(0f);
        StopAllCoroutines();
        DisableAllHitboxes();
        if (hurtCol != null) hurtCol.enabled = false;
        if (BossFinish.Enabled && BossFinishInfo.TryUnpack(NetBossFinishCode, out var fi))
        {
            LastFinishInfo = fi;
            PrepareDeathVisual();
            BossFinish.ClearBossHazards();
            BossFinish.Begin(this, fi, CenterWorld, localImpact: NetBossFinishLocal); // 同じ見た目をこの端末で(報酬/遭遇の終了は HOST とラストヒットの本人)
            return;
        }
        StartCoroutine(FinalHitAndDie());
    }

    public void NetCaptureVisual(ref NetCombat.State s)
    {
        s.Pose = (byte)pose;
        s.Facing = (sbyte)(facing < 0f ? -1 : 1);
        s.Windup = (byte)Mathf.RoundToInt(Mathf.Clamp01(windupProgress) * 255f);
        s.Attack = (byte)Mathf.RoundToInt(Mathf.Clamp01(attackProgress) * 255f);
        s.BaseColor = NetPlayerSnapshot.PackColor(baseColor);
        s.ExtraX = extraScale.x; s.ExtraY = extraScale.y;
        s.Order = (short)Mathf.Clamp(rig != null ? rig.SortingOrder : RenderOrder.Boss, short.MinValue, short.MaxValue);
        if (!entering) s.Flags |= NetCombat.FlagHpBar;
        if (hurtCol != null && hurtCol.enabled) s.Flags |= NetCombat.FlagHurtbox;
    }

    public void NetApplyVisual(NetCombat.State s)
    {
        if (dead) return;
        Pose p = (Pose)s.Pose;
        if (p != pose) { pose = p; poseTime = 0f; }
        facing = s.Facing < 0 ? -1f : 1f;
        windupProgress = s.Windup / 255f;
        attackProgress = s.Attack / 255f;
        baseColor = NetPlayerSnapshot.UnpackColor(s.BaseColor);
        extraScale = new Vector2(s.ExtraX, s.ExtraY);
        if (s.Order != netVisualOrder) { netVisualOrder = s.Order; SetVisualSortingOrder(s.Order); }
        bool hpShown = (s.Flags & NetCombat.FlagHpBar) != 0;
        if (hpShown && entering)
        {
            entering = false;
            if (hpBar != null) StartCoroutine(hpBar.RevealRoutine(0.25f));
        }
        if (hurtCol != null) hurtCol.enabled = (s.Flags & NetCombat.FlagHurtbox) != 0;
    }

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; BossBattle.Living.Add(this); }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; BossBattle.Living.Remove(this); }
    void OnOriginShifted(float s) { worldX -= s; }

    void OnDestroy()
    {
        if (hpBar != null) Destroy(hpBar.gameObject);
        OnDestroyFinish(); // BOSS FINISH: 見た目の途中で消された時も遭遇を進める
    }
}

// Hurtbox子オブジェクトのTrigger通知をボス本体へ転送するだけの部品。
public class BossHurtbox : MonoBehaviour
{
    public WildBossBase owner;
    void OnTriggerEnter2D(Collider2D other)
    {
        if (owner != null) owner.OnHurtboxTrigger(other);
    }
}

// プレイヤーの基本速度に合わせて流れる、地面上の警告/範囲攻撃ゾーン。
// warnDuration秒だけ警告(赤の半透明)、その後activeDurationだけ判定有効
// (activeDuration=0なら警告のみ)。
public class TrackedHazard : MonoBehaviour
{
    float warnDuration, activeDuration, height, timer;
    public float yLift; // ボス戦の強化(2026-10-01): 地面から浮かせた帯(空中の攻撃の予告/判定)
    public Color warnTint; // a>0: 予告の色(高い攻撃=紫など)
    BoxCollider2D col;
    SpriteRenderer sr;
    Color activeColor;
    bool activated;
    public bool damages = true;
    public int damageAmount = CombatScale.PlayerHit; // ボス戦の強化(2026-10-01): 必殺技は強い一撃
    public System.Action onActivate;

    public static TrackedHazard Create(float worldX, float width, float heightSize, float warn, float active, Color activeColor)
    {
        GameObject go = new GameObject("TrackedHazard");
        var hz = go.AddComponent<TrackedHazard>();
        hz.warnDuration = warn;
        hz.activeDuration = active;
        hz.height = heightSize;
        hz.activeColor = activeColor;
        hz.sr = go.AddComponent<SpriteRenderer>();
        hz.sr.sprite = BossFx.Block();
        hz.sr.sortingOrder = RenderOrder.Boss - 1;
        hz.sr.color = new Color(1f, 0.15f, 0.08f, 0.1f);
        go.transform.localScale = new Vector3(width, heightSize, 1f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        hz.col = go.AddComponent<BoxCollider2D>();
        hz.col.isTrigger = true;
        hz.col.enabled = false;
        var dbg = go.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.1f, 0.1f);

        go.transform.position = new Vector3(worldX, GroundAt(worldX) + heightSize * 0.5f, 0f);
        return go.GetComponent<TrackedHazard>();
    }

    // 地面からliftだけ浮いた帯(高い位置の攻撃の予告など)。色は予告の赤とは別にwarnTintで変えられる。
    public static TrackedHazard CreateLifted(float worldX, float width, float heightSize, float lift, float warn, float active, Color activeColor)
    {
        var hz = Create(worldX, width, heightSize, warn, active, activeColor);
        hz.yLift = lift;
        Vector3 p = hz.transform.position; p.y += lift; hz.transform.position = p;
        return hz;
    }

    static float GroundAt(float x)
    {
        if (TerrainManager.Instance == null) return 0f;
        return TerrainManager.Instance.GetHeightAt(x) ?? 0f;
    }

    // マルチプレイPhase 2.5: 予兆/範囲攻撃ゾーンをJOINにも出す(有効になった瞬間から判定あり)。
    void Start() { NetAttackSync.Register(gameObject, NetAttackSync.AType.TrackedHazard, noDamage: !damages || activeDuration <= 0f); }

    void Update()
    {
        // マルチでは近くの活動中プレイヤーの走行速度で流れる(シングルは従来どおり自分の速度)。
        float baseSpeed = NetTargets.IsMulti ? NetTargets.FrameSpeedNear(transform.position) : (PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f);
        Vector3 p = transform.position;
        p.x += baseSpeed * Time.deltaTime;
        p.y = GroundAt(p.x) + yLift + height * 0.5f;
        transform.position = p;

        timer += Time.deltaTime;
        if (timer < warnDuration)
        {
            float f = timer / Mathf.Max(0.01f, warnDuration);
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, f));
            float wa = Mathf.Lerp(0.10f, 0.42f, f) * Mathf.Lerp(0.7f, 1f, blink);
            sr.color = warnTint.a > 0f ? new Color(warnTint.r, warnTint.g, warnTint.b, wa * 1.15f) : new Color(1f, Mathf.Lerp(0.5f, 0.1f, f), 0.08f, wa);
            return;
        }

        if (activeDuration <= 0f) { Destroy(gameObject); return; }

        if (!activated)
        {
            activated = true;
            col.enabled = true;
            sr.color = activeColor;
            onActivate?.Invoke();
        }
        float ft = (timer - warnDuration) / activeDuration;
        Color c = activeColor; c.a = activeColor.a * Mathf.Lerp(1f, 0.3f, ft);
        sr.color = c;
        if (timer >= warnDuration + activeDuration) Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!damages || !activated) return;
        if (other.CompareTag("Player") && PlayerController.Instance != null) PlayerController.Instance.TakeDamage(source: "WildBoss:" + name, amount: BossManager.ScaleDamage(damageAmount));
    }
}

// 複数体のボスが同時に予備動作を始めないよう、開始タイミングを全体でずらす共有ゲート。
public static class BossAttackGate
{
    public static float NextTime;
    public static float Interval = 0.8f; // 調整用: 次の体が予備動作を始めるまでの最短間隔(秒)
}

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
    public enum Pose { Idle, Move, Windup, Attack, Fly, Landing, Recover } // Recover(2026-10-20): 攻撃の後の戻りのコマ(recover_ がある時だけ)

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
    // 診断(2026-10-12、ボスへの命中不良の調査): 今の動き/間合い。テスト・診断の表示用
    public string DebugAct { get; protected set; } = "";
    public float DebugGap => Gap;
    public bool DebugFreeGap => freeGap; // 技でわざと間合いの制限を外している間(追い越し/潜って離れる 等)
    public float DebugFrontDist => FrontDist;
    public float DebugRelVelocity => relVelocity;
    public Vector4 DebugMoveSplit { get; private set; }
    public bool DebugInvulnerable => invulnerable;
    // 切り分け用(2026-10-15): 必殺技の各段の開始(開発版だけ。テストは「その段が実際に出たか」をこれで確かめる)
    float ultBeatT0;
    public readonly List<string> UltSteps = new List<string>(); // 最後の必殺技で実際に行った段
    protected void UltBeat(string what)
    {
        if (!Debug.isDebugBuild) return;
        if (what.StartsWith("start")) { ultBeatT0 = Time.time; UltSteps.Clear(); }
        UltSteps.Add(what.Split(' ')[0] == "start" ? "start" : what);
        Debug.Log($"[{GetType().Name}] ULT {what} @{Time.time - ultBeatT0:F2}s gap={Gap:F1}");
    }
    public string DebugPose => pose.ToString();
    public int DebugRT => RematchTierApplied;
    public void DebugScaleHp(float mul) { maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * mul)); Hp = maxHp; }
    float behindTime; bool frontReturning;
    public static int FrontReturns { get; private set; } // 確認用: 後ろから前へ回り込んだ回数
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
    protected bool IsEnteringForVoice => entering; // 2026-10-14: 声(BossVoice)が入場中かを見る
    Color baseColor = Color.white;
    readonly List<BossHitbox> hitboxes = new List<BossHitbox>();
    readonly List<BossTelegraphMarker> markers = new List<BossTelegraphMarker>();

    protected float FrontReach => halfWidth * frontReachRatio;
    protected float Gap => player != null ? worldX - player.position.x : 0f;
    protected float PlayerX => player != null ? player.position.x : 0f;
    protected float PlayerY => player != null ? player.position.y : 0f;
    // 顔(正面)からプレイヤーまでの距離。正=プレイヤーが顔の前方、負=顔より奥(重なり)。
    protected float FrontDist => (PlayerX - (worldX + facing * FrontReach)) * facing;
    // 近づかないまま遠くの技を続ける形の防止(2026-10-12、攻略の再検証)。上位強化の再戦などで必殺技の待ちが短いと、
    // 「遠くからの技(必殺技/飛び道具)→ 次も遠くからの技」が噛み合い、ボスが一度も近づかないまま近接キャラが数分当てられなかった
    // (自然洞窟のサソリ: 毒だまり(間合い 7.5m)→ 毒弾(6.5m より遠い時)→ 毒だまり …)。被弾で足が止まると 15m 先まで離れ、形が固定される。
    // 体の前端がプレイヤーから NearFrontDist m 以内に来ないまま FarTooLongSeconds 秒続いたら、遠くからの技を選ぶ所(各ボス)で近接の技を選ぶ。
    public static float NearFrontDist = 3.5f, FarTooLongSeconds = 7f;
    float lastNearTime = -1f;
    public static int FarLoopBreaks { get; private set; } // 確認用
    protected bool FarTooLong
    {
        get
        {
            if (lastNearTime < 0f) lastNearTime = Time.time;
            bool far = Time.time - lastNearTime > FarTooLongSeconds;
            if (far) { FarLoopBreaks++; lastNearTime = Time.time; } // 1回近接を選ばせたら数え直す(近づけなかった時もまた7秒後)
            return far;
        }
    }
    protected float GroundY => TerrainGround(worldX);
    // 2026-10-10(全体点検): 天井にぶら下がっている(上下反転)時は体が根元より下にある → 中心/攻撃の出る位置も下向きに
    protected float UpSign => extraScale.y < 0f ? -1f : 1f;
    public Vector3 CenterWorld => transform.position + new Vector3(0f, bodyHeight * 0.5f * UpSign, 0f);
    // 自動操作補助(ボス戦、2026-10-04): 今攻撃が通るか(無敵/地中/天井/登場中は通らない)と、被弾範囲
    public bool AssistTargetable => !dead && !invulnerable && !entering && hurtCol != null && hurtCol.enabled;
    public Collider2D DebugHurtCol => hurtCol; // 確認用(部分試験)
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
        float rightEdge = cam != null ? GameView.Right(cam) : PlayerX + 12f;
        if (cam != null && pc != null && player != pc.transform) rightEdge = PlayerX + (rightEdge - pc.transform.position.x); // マルチ: 最前の相手の画面の右端
        worldX = Mathf.Max(PlayerX + startGap, rightEdge) + 3f;
        lastGroundY = TerrainGround(worldX);

        loopTimeOffset = Random.Range(0f, 4f); // 2026-10-22: 同じボスが並んでも動きがそろわない(三姉妹などは OnInit で上書き)
        OnInit();
        // ボス戦の強化(2026-10-01): 通常攻撃の被弾(ハートの数)を種類ごとに変えられる(必殺技の判定は各ボスが2に設定済み)
        if (tune != null && tune.normalDamage > CombatScale.PlayerHit) foreach (var hb in hitboxes) if (hb != null && hb.damageAmount == CombatScale.PlayerHit) hb.damageAmount = tune.normalDamage;
        ApplyTransform();
        // マルチプレイPhase 2 - HOSTでは共有ボスとして登録。JOINでパペットとして作っている時はAIを始めない。
        if (NetCombat.OnBossInit(this)) { BossVoice.ArriveAfter(this, DeathKey, 1.2f); return; } // 2026-10-14: JOIN の端末でも登場の咆哮を自分で鳴らす(入場の時間ぶん後)
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
        hurtBaseSize = hurtCol.size;
        var fwd = h.AddComponent<BossHurtbox>();
        fwd.owner = this;
        var dbg = h.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.6f, 0f);
    }

    // ================= Hitbox / Marker 生成 =================
    // 叩きつけ/噴出の本番の絵(2026-10-11、以前は色付きの四角)。絵は色付きなので、絵がある時は色を薄く乗せるだけにする
    protected static Sprite BurstArt => AttackArt.First(AtkArtSet.Burst, BossFx.Block());
    protected static Color BurstTint(Color old) => AttackArt.Has(AtkArtSet.Burst) ? new Color(Mathf.Lerp(1f, old.r, 0.25f), Mathf.Lerp(1f, old.g, 0.25f), Mathf.Lerp(1f, old.b, 0.25f), 0.97f) : old;

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
        BossVoice.Arrive(this, DeathKey); // 2026-10-14: 画面に入った時の咆哮(ボスごとに1回。天空のボスが入場で吠えた時はそれが1回目)

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
        // 2026-10-29: BREAK 中は、プレイヤーの実際の前進の速さで並ぶ(走る速さの決まった値だと、攻撃でプレイヤーが一瞬止まった
        //  フレームに膝をついたボスだけが前へ滑り、150km/h では1フレームで約1.5mずれて見えた。リヴァイアサンの BREAK の試験)
        if (Broken && netTarget == null && pc != null && !DebugBreakNominalSpeed) baseSpeed = Mathf.Max(0f, pc.MotionVelocity.x);
        // マルチ Phase 3.1: 後ろの人が離れていたら並走を少し遅くし、追い越されたら後ろの人へ狙いを替える(BossLeash)
        BossLeash.Result leash = default;
        bool leashOn = BossLeash.Enabled && !entering;
        if (leashOn)
        {
            leash = BossLeash.Evaluate(worldX, player, netTarget != null ? netTarget.TargetPlayer : 0, ref leashTime);
            if (netTarget != null) netTarget.SetPreferred(leash.PreferTarget);
            if (leash.Active) baseSpeed *= leash.SpeedFactor;
        }
        float wx0 = worldX;
        worldX += (baseSpeed + relVelocity * MoveScale) * dt; // MoveScale: ラスダンの移動速度(接近/間合い/突進)
        breakSyncBaseMove = baseSpeed * dt;
        breakSyncPending = Broken && netTarget == null && pc != null && !DebugBreakNominalSpeed && !ComboRevision.Legacy;
        float wxB = worldX;

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
            if (Broken || breakRecovering) { lo = Mathf.Min(lo, -halfWidth - 3f); hi = Mathf.Max(hi, halfWidth + 3f); }
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
                // 2026-10-18: BREAK の後(立ち直ってから約2.5秒)は、近くへ寄った位置から普段の間合いへ滑らかに戻る(一瞬で数m戻って見えた)
                if (Time.time - BreakEndedAt < 2.5f && (gap > hi || gap < lo)) worldX = Mathf.MoveTowards(worldX, gap > hi ? px + hi : px + lo, 12f * dt);
                else if (gap > hi) worldX = soft && gap > hi + 3f ? Mathf.MoveTowards(worldX, px + hi, 22f * dt) : px + hi;
                else if (gap < lo) worldX = soft && gap < lo - 3f ? Mathf.MoveTowards(worldX, px + lo, 22f * dt) : px + lo;
            }
        }
        // 2026-10-11(攻略の再検証): プレイヤーの後ろ(前攻撃が届かない所)に張り付いたまま数分 当てられないことがあった
        // (自然洞窟の古代の魔人が 480秒 HP100% のまま 等)。技で後ろへ回る間(freeGap)/登場/BREAK/地中などは数えない。
        // 後ろに BossBehindSeconds 続いたら、走る速さに少し足して前の届く位置まで回り込む(瞬間移動はしない)
        float wxC = worldX;
        if (BalanceTuning.BossFrontReturn && !entering && !freeGap && !Broken && !IntentionallyAway && !UltimateArt.ArenaFreeGap && !(leashOn && leash.AllowBehindTarget))
        {
            float g2 = worldX - px;
            float front = halfWidth + 2.0f;
            if (frontReturning)
            {
                if (g2 >= front) frontReturning = false;
                else worldX += Mathf.Min(BalanceTuning.BossReturnSpeed * dt, front - g2);
            }
            else if (g2 < -0.5f) { behindTime += dt; if (behindTime >= BalanceTuning.BossBehindSeconds) { frontReturning = true; behindTime = 0f; FrontReturns++; } }
            else behindTime = 0f;
        }
        else { behindTime = 0f; frontReturning = false; }
        if (entering || Broken || FrontDist <= NearFrontDist) lastNearTime = Time.time;
        float wxS = worldX;
        BreakComboStep(dt); // BREAK 中に打ち上げたボスの位置 / 大型の膝つき(2026-10-18)
        DebugMoveSplit = new Vector4(wxB - wx0, wxC - wxB, wxS - wxC, worldX - wxS); // 確認用: このフレームの移動(並走/間合いの補正/回り込み/BREAK の寄せ)
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
        ApplyHurtShape();
    }

    void ApplyHurtShape()
    {
        // 被弾判定は体の見た目に合わせる(天井にぶら下がる=体を上下反転した時は、判定も根元から下側へ。2026-10-07)
        if (hurtCol != null)
        {
            float sy = Mathf.Clamp(extraScale.y, -1f, 1f); // 反転の途中(登る/降りる)も見た目と同じく連続で
            float kn = 1f - KneelSquash * kneel; // BREAK の膝つき(2026-10-18): 絵と同じだけ低くなる
            // 2026-10-21: 止め絵の膝つきは上半身が前へ倒れる → 判定も少し前へ(絵と判定を合わせる)
            float lean = kneel > 0f && !ComboRevision.Legacy && !(Anim != null && (Anim.brk != null || Anim.breakLoop != null)) ? facing * 0.06f * hurtBaseSize.x * kneel : 0f;
            hurtCol.transform.localPosition = new Vector3(hurtBaseLocal.x + lean, hurtBaseLocal.y * sy * kn, 0f);
            if (kneel > 0f || hurtKneelApplied) { hurtCol.size = new Vector2(hurtBaseSize.x, hurtBaseSize.y * kn); hurtKneelApplied = kneel > 0f; }
        }
    }
    Vector2 hurtBaseLocal, hurtBaseSize;
    bool hurtKneelApplied;
    public static bool DebugBreakNominalSpeed; // 試験用: 前の作り(BREAK 中も決まった走る速さで並ぶ)で比べる

    // 2026-10-29: BREAK 中の並走の補正。Update ではプレイヤーの前のフレームの速さで並ぶため(更新の順番による1フレームの遅れ)、
    //  プレイヤーが攻撃で止まったフレームにボスだけ前へ滑っていた。全員が動いた後(LateUpdate)に、このフレームのプレイヤーの
    //  実際の移動とボスの並走の差を足して合わせる。浮動原点の移しなど大きな跳びは補正しない
    float breakSyncLastPx = float.NaN, breakSyncBaseMove;
    bool breakSyncPending;
    public static int BreakSyncCorrections; // 確認用
    protected virtual void LateUpdate()
    {
        float pxNow = PlayerX;
        // 打ち上げた/寄せて浮かせているボス(Launch)は、位置を打ち上げの処理が決めるので足さない(二重に動かさない)
        bool juggledNow = juggle != null && juggle.style == BossJuggle.Style.Launch && (juggle.Anchored || juggle.Airborne);
        if (breakSyncPending && !juggledNow && !dead && !float.IsNaN(breakSyncLastPx))
        {
            float corr = (pxNow - breakSyncLastPx) - breakSyncBaseMove;
            if (Mathf.Abs(corr) > 0.001f && Mathf.Abs(corr) < 3f) { worldX += corr; ApplyTransform(); BreakSyncCorrections++; }
        }
        breakSyncPending = false;
        breakSyncLastPx = pxNow;
    }

    // 自然洞窟ボス拡張(2026-09-22) - 荒野街道は単一のflatな下ルートしか
    // 想定していなかったため`protected virtual`にし、洞窟ボス(上下ルート/
    // 地中移動)側で「今どのルートの床を基準にするか」を差し替えられる
    // ようにした。既定の挙動(下ルートのGetHeightAt)は完全に不変。
    protected virtual float TerrainGround(float x)
    {
        if (TerrainManager.Instance != null)
        {
            // 2026-10-10: 穴の上は「最後に見た地面」ではなく、穴の両側の地面を結んだ高さ(穴の向こうで高さが違うと、出口で一気に跳んでいた)
            lastGroundY = TerrainManager.Instance.GetSupportHeightAt(x);
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

    // 技ごとの姿勢の絵(2026-10-13、メカドラゴン): null 以外を返すと、その絵を使う(被弾/BREAK/技の姿勢を持つボス用)。既存のボスは null
    protected virtual Sprite PoseOverride() => null;
    protected float HitFlashTime => hitTimer; // 被弾の赤み/揺れの残り時間(姿勢の絵の切り替えに使う)

    // アニメーション強化(2026-10-17): 姿勢ごとのコマ(Resources/BossAnim/<animKey>/)。各ボスの OnInit で animKey を入れる。無ければ今までどおり
    protected string animKey;
    // 2026-10-19: 技ごとの2つ目の溜め/攻撃のコマ(windupB/attackB)を使う時は 1(2026-10-20: 2 = C、3 = D も)。各技の始めで決める(無ければ1つ目のまま)
    protected int animVariant;
    protected float diveArtT = -1f, emergeArtUntil; // 潜る/飛び出すのコマ(PickAnimSprite)
    protected bool exhaustArt; // 専用の隙のコマ(exhaust_)を出している間
    protected Sprite[] exhaustArtFrames; // 2026-10-22: exhaustArt の間、exhaust_ の代わりに出すコマ(グリフォン: 急降下の着地=land_ / 必殺技の墜落=exhaust_)
    protected Sprite emergeArtOverride; // emergeArtUntil までの間、emerge_ の代わりに出すコマ(技ごとの飛び出し。期限が過ぎたら消える)
    protected bool HasDiveArt => Anim != null && Anim.dive != null;
    BossAnimSet anim; bool animLoaded;
    float hurtArtAt = -9f, hurtArtUntil = -9f;
    protected BossAnimSet Anim
    {
        get { if (!animLoaded) { animLoaded = true; anim = BossAnimSet.Load(animKey); } return anim; }
    }

    Sprite PickAnimSprite()
    {
        var a = Anim;
        if (a == null) return null;
        if (UltimateRunning && a.ult != null && pose != Pose.Landing && animVariant == 0) // 2026-10-20: 必殺技の中の2つ目の技(ライダーの魔法弾)はその技のコマ
        {
            if (pose == Pose.Windup) return a.ult[0];
            // 2026-10-20: 3枚以上なら突進中は全部を繰り返す(ゴブリンライダーの騎乗突撃: 構える → 蹴る → 伸びる。脚が止まって見えないように)
            if (a.ult.Length >= 3 && (pose == Pose.Attack || pose == Pose.Move || pose == Pose.Fly))
                return BossAnimSet.Loop(a.ult, a.moveFps, moveClock + loopTimeOffset);
            // 2026-10-28: 2枚の必殺技のコマは攻撃の時だけ(以前は歩く/下がる間も「振り下ろした」コマのまま滑っていた: サイクロプス/オオカミ)
            if (pose == Pose.Attack || pose == Pose.Fly) return a.ult[Mathf.Min(1, a.ult.Length - 1)];
        }
        // 2026-10-20: 必殺技の後の専用の隙(exhaust_、サソリの尾が刺さる・モグラが目を回す)。CaveRecovery の間ずっと
        var exArt = exhaustArtFrames ?? a.exhaust;
        if (exhaustArt && !Broken && exArt != null) return BossAnimSet.Loop(exArt, 3f, Time.time + loopTimeOffset);
        // 2026-10-20: BREAK からの立ち直り(rise_)/ BREAK の間の繰り返し(breakloop_)。どちらも無いボスは今までどおり
        if (breakRecovering && a.rise != null) return BossAnimSet.Progress(a.rise, poseTime / BreakRecoverSeconds);
        // 打ち上げられて空中にいる間(BREAK のエリアルコンボ)は、崩れた1コマ目(地面に座り込むコマは地面に着いてから)
        if (pose == Pose.Landing && a.brk != null && juggle != null && juggle.Open && juggle.Airborne) return a.brk[0];
        if (pose == Pose.Landing && a.brk != null) return BossAnimSet.Progress(a.brk, poseTime / 0.45f); // BREAK・必殺技の後の隙
        // BREAK の間は、寄せ(移動)の間も移動のコマを回さない(以前は寄せの間に浮遊/歩きのコマが出て、BREAK に見えなかった)
        if (Broken && !breakRecovering && a.breakLoop != null && (pose == Pose.Idle || pose == Pose.Move)) return BossAnimSet.Loop(a.breakLoop, 3f, Time.time + loopTimeOffset);
        // 2026-10-20: 潜る/飛び出すのコマ(dive_/emerge_)。diveArtT >= 0 の間は潜るコマを進み具合で、emergeArtUntil までは飛び出すコマ(潜るボス共通)
        if (!Broken && diveArtT >= 0f && a.dive != null) return BossAnimSet.Progress(a.dive, diveArtT);
        if (Time.time >= emergeArtUntil) emergeArtOverride = null;
        if (!Broken && Time.time < emergeArtUntil && emergeArtOverride != null) return emergeArtOverride; // 2026-10-21: 技ごとの飛び出しのコマ(ワームの斜めの飛び出し等)
        if (!Broken && Time.time < emergeArtUntil && a.emerge != null) return a.emerge[0];
        if (flinchTimer > 0f && a.hurt != null && pose != Pose.Landing) // 怯み(2026-10-18)。2枚以上なら崩れる → 立て直すの順に
            return a.hurt.Length > 1 ? BossAnimSet.Progress(a.hurt, 1f - flinchTimer / FlinchSeconds) : a.hurt[0];
        // 被弾の絵: 連続で当たっても毎回は出さない(0.7秒に1回、最初の一撃の赤みの前半だけ)。毎回出すと走りがカクつく
        if (hitTimer > 0.08f && a.hurt != null && (pose == Pose.Idle || pose == Pose.Move))
        {
            float hurtLen = a.hurt.Length > 1 ? 0.28f : 0.12f; // 2枚以上: 少し崩れて立て直すまで(0.28秒。長く止まって見えない長さ)
            if (hurtArtUntil < Time.time && Time.time - hurtArtAt > 0.7f) { hurtArtAt = Time.time; hurtArtUntil = Time.time + hurtLen; }
        }
        if (a.hurt != null && Time.time < hurtArtUntil && (pose == Pose.Idle || pose == Pose.Move))
            return a.hurt.Length > 1 ? BossAnimSet.Progress(a.hurt, (Time.time - hurtArtAt) / Mathf.Max(0.01f, hurtArtUntil - hurtArtAt)) : a.hurt[0];
        switch (pose)
        {
            case Pose.Windup:
            {
                var w = a.WindupFor(animVariant);
                if (w != null) return BossAnimSet.Progress(w, windupProgress);
                break;
            }
            case Pose.Attack:
            {
                var at = a.AttackFor(animVariant);
                if (at != null) return BossAnimSet.Progress(at, 1f - attackProgress);
                break;
            }
            case Pose.Recover:
            {
                var r = a.RecoverFor(animVariant);
                if (r != null) return BossAnimSet.Progress(r, poseTime / Mathf.Max(0.05f, recoverArtSeconds));
                // 2026-10-22: 戻りのコマが無いボスは、攻撃の最後のコマを少し保ってから待機へ(攻撃の絵からいきなり走りへ飛ばない)
                var ah = a.AttackFor(animVariant);
                if (ah != null) return ah[ah.Length - 1];
                break;
            }
            case Pose.Move:
            case Pose.Fly:
                if (pose == Pose.Fly && a.fly != null) return BossAnimSet.Loop(a.fly, a.moveFps, moveClock + loopTimeOffset); // 2026-10-22: 跳んでいる間の専用のコマ(黒騎士)
                if (a.HasMove) return BossAnimSet.Loop(a.move, a.moveFps, moveClock + loopTimeOffset); break;
            case Pose.Idle:
                if (locoStyle != LocoStyle.None && a.HasMove) return BossAnimSet.Loop(a.move, a.moveFps, moveClock + loopTimeOffset); // 走りながら待つボス
                if (a.idle != null) return BossAnimSet.Loop(a.idle, a.idleFps, moveClock + loopTimeOffset);
                break;
        }
        return null;
    }

    Sprite PickSprite()
    {
        var po = PoseOverride();
        if (po != null) return po;
        var an = PickAnimSprite();
        lastSpriteFromAnim = an != null;
        lastSpriteFromLoop = false;
        if (an != null) return an;
        if (loopFrames != null && loopFrames.Length > 0 && (pose == Pose.Move || pose == Pose.Idle || pose == Pose.Fly))
        {
            lastSpriteFromLoop = true;
            return loopFrames[Mathf.FloorToInt((Time.time + loopTimeOffset) * loopFps) % loopFrames.Length];
        }
        switch (pose)
        {
            case Pose.Move:
                if (moveSprite != null) return ((int)(Time.time * 6f) % 2 == 0) ? idleSprite : moveSprite;
                return idleSprite;
            case Pose.Windup: return windupSprite != null ? windupSprite : idleSprite;
            case Pose.Attack: return attackSprite != null ? attackSprite : idleSprite;
            case Pose.Fly: return moveSprite != null ? moveSprite : idleSprite;
            case Pose.Landing: return idleSprite;
            case Pose.Recover: return attackSprite != null ? attackSprite : idleSprite;
            default:
                // 待機(Idle)は「棒立ち」にしない: 移動アニメーションを持つボスはMove姿勢で表示する。
                if (locoStyle != LocoStyle.None && moveSprite != null) return ((int)(Time.time * 6f) % 2 == 0) ? idleSprite : moveSprite;
                return idleSprite;
        }
    }

    void AnimateVisual()
    {
        if (visual == null || rig == null) return;
        moveClock += Time.deltaTime * UpdateMoveTempo(Time.deltaTime);
        rig.SetSprite(PickSprite());

        float h = bodyHeight;
        float t = poseTime;
        Vector2 off = Vector2.zero;
        float rot = 0f, sx = 1f, sy = 1f;
        Color tint = baseColor;
        // 2026-10-22 動きの見直し: 本物のコマ(走り/這い/羽ばたき)がある時は、1枚絵の頃の跳ね/前傾/伸び縮みを足さない(絵の動きと二重になってバタついていた)。
        // 浮遊(羽ばたき/布)だけは、ゆっくりした上下と小さな揺れを残す。BREAK 中・隙の絵の間は揺らさない
        bool framed = lastSpriteFromAnim || lastSpriteFromLoop;
        bool airy = locoStyle == LocoStyle.Wing || locoStyle == LocoStyle.Cloth;
        bool calm = Broken || exhaustArt;
        float ph = loopTimeOffset * 1.7f;

        switch (pose)
        {
            case Pose.Idle:
            case Pose.Move:
                if (calm) break;
                if (framed)
                {
                    if (airy) { off.y = Mathf.Sin(Time.time * 2.4f + ph) * 0.035f * h; rot = Mathf.Sin(Time.time * 1.3f + ph) * 1.2f; }
                }
                else if (airy)
                {
                    off.y = Mathf.Sin(Time.time * 2.6f + ph) * 0.05f * h; // 浮遊は床で跳ねない(なめらかに上下)
                    rot = -leanDegrees * 0.2f * facing + Mathf.Sin(Time.time * 1.4f + ph) * 1.5f;
                }
                else if (pose == Pose.Idle)
                {
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
                }
                else
                {
                    off.y = Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.03f * h;
                    rot = locoStyle == LocoStyle.None ? Mathf.Sin(Time.time * 8f) * 1.8f : -leanDegrees * 0.25f * facing;
                    sy = 1f + 0.02f * Mathf.Sin(Time.time * 16f);
                }
                break;
            case Pose.Windup:
            {
                float p = windupProgress;
                float e = p * p * (3f - 2f * p);
                rot = leanDegrees * e * facing;              // 後ろへ反る
                off.x = -facing * pullBackRatio * h * e;     // 後ろへ引く
                sy = 1f - 0.08f * e;                          // 溜めて沈む
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(10f, 30f, p));
                tint = Color.Lerp(baseColor, new Color(windupTint.r, windupTint.g, windupTint.b, baseColor.a), blink * Mathf.Clamp01(p * 1.4f) * windupTintScale);
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
                if (framed) { off.y = Mathf.Sin(Time.time * 2.6f + ph) * 0.03f * h; rot = Mathf.Sin(Time.time * 1.6f + ph) * 1.5f; break; } // 2026-10-22: 絵の羽ばたき/跳躍の上で波打たない
                off.y = Mathf.Sin(Time.time * 5f) * 0.10f * h;
                rot = Mathf.Sin(Time.time * 4f) * 4f;
                sy = 1f + 0.05f * Mathf.Sin(Time.time * 12f);
                break;
            case Pose.Recover:
            {
                // 攻撃の前のめりから少しずつ戻る(戻りのコマと一緒に)
                float k = Mathf.Clamp01(t / Mathf.Max(0.05f, recoverArtSeconds));
                rot = -leanDegrees * 0.35f * (1f - k) * facing;
                if (k >= 1f) SetPose(Pose.Idle);
                break;
            }
            case Pose.Landing:
            {
                if (breakRecovering) break; // 2026-10-22: 立ち直りの絵の上ではつぶさない
                float k = Mathf.Clamp01(t / 0.35f);
                float amt = framed && Anim != null && Anim.brk != null ? artPoseProceduralScale : 1f; // 崩れる絵がある時は弱く(二重につぶれない)
                sy = 1f - 0.18f * (1f - k) * amt;
                sx = 1f + 0.12f * (1f - k) * amt;
                break;
            }
        }

        // 2026-10-20: 溜め/攻撃/戻りに本物のコマがある時は、手続きの反り/踏み込み/伸び縮みを弱める(絵の姿勢と二重にならない。死神三姉妹は 0.3)
        if (lastSpriteFromAnim && artPoseProceduralScale < 0.999f && (pose == Pose.Windup || pose == Pose.Attack || pose == Pose.Recover))
        {
            float k = artPoseProceduralScale;
            rot *= k; off *= k; sx = 1f + (sx - 1f) * k; sy = 1f + (sy - 1f) * k;
        }
        // 2026-10-22: 姿勢が変わる時に、傾き/位置/伸び縮みが1フレームで飛ばないように短く寄せる(約0.15秒)。攻撃の始まり(踏み込み)だけは即座に
        if (pose != smPose) { smSnap = pose == Pose.Attack; smPose = pose; }
        float bk = !smInit || smSnap ? 1f : 1f - Mathf.Exp(-Time.deltaTime / 0.05f);
        smInit = true; smSnap = false;
        smOff = Vector2.Lerp(smOff, off, bk); smRot = Mathf.Lerp(smRot, rot, bk); smSx = Mathf.Lerp(smSx, sx, bk); smSy = Mathf.Lerp(smSy, sy, bk);
        off = smOff; rot = smRot; sx = smSx; sy = smSy;
        if (hitTimer > 0f)
        {
            float k = hitTimer / 0.16f;
            off.x += Mathf.Sin(Time.time * 70f) * 0.05f * h * k;
            tint = Color.Lerp(tint, new Color(1f, 0.25f, 0.25f, baseColor.a), 0.7f * Mathf.Clamp01(k));
        }
        // 2026-10-18: 怯み(後ろへのけぞる) / BREAK で打ち上がっている間(上がる時は反り、落ちる時は前へ) / 大型の膝つき(低く)
        if (flinchTimer > 0f) { float k = Mathf.Clamp01(flinchTimer / FlinchSeconds); rot += facing * 9f * k; off.x -= facing * 0.05f * h * k; }
        if (juggle != null && juggle.Open && juggle.Airborne) rot += facing * Mathf.Clamp(juggle.VelY * 0.8f, -12f, 12f);
        bool kneelArt = Anim != null && (Anim.brk != null || Anim.breakLoop != null);
        if (kneel > 0f && ComboRevision.Legacy && !kneelArt) { sy *= 1f - KneelSquash * kneel; sx *= 1f + 0.06f * kneel; } // 改修前: 縦につぶす
        if (kneel > 0f && kneelArt)
        {
            // 2026-10-20: BREAK の絵(膝つき等)があるボスは、絵が膝をついているので手続きのつぶしを弱める(artPoseProceduralScale)
            float kn = kneel * artPoseProceduralScale;
            sy *= 1f - KneelSquash * kn; sx *= 1f + 0.06f * kn;
        }
        // 2026-10-21: 止め絵のボスは縦につぶさず、格子リグで脚を折って上半身を前へ倒す(頭が下がる。高さは同じく約2割低く)
        rig.Kneel = kneelArt || ComboRevision.Legacy ? 0f : kneel;
        if (kneel > 0f && !kneelArt && !ComboRevision.Legacy) rot += -facing * 4f * kneel;

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
        if (Anim != null && Anim.HasMove) loco *= Anim.rigLocoScale; // 本物の走りのコマがある時は、格子の偽の脚の動きを弱める
        if (calm) loco = 0f; // 2026-10-22: BREAK 中・隙の絵の間は格子も揺らさない
        rig.Update(locoStyle, locoStyle == LocoStyle.None ? 0f : loco, Time.deltaTime, artFacesLeft ? -1f : 1f);
    }

    public Transform DebugVisual => visual; // 動きの測定用(QaSweep の撮影)
    // 2026-10-22: 移動のループのテンポ。重いボスはゆっくり、軽いボスは速く。走る速さでも少し変える(足が滑って見えない)。
    // 時計(moveClock)に掛けるので、テンポが変わってもコマの位置は飛ばない
    float moveClock, moveTempo = 1f;
    Vector2 smOff; float smRot, smSx = 1f, smSy = 1f; bool smInit, smSnap; Pose smPose;
    bool lastSpriteFromLoop;
    protected float moveTempoScale = 1f; // ボスごとの調整(1 = 既定)
    float UpdateMoveTempo(float dt)
    {
        float k = locoStyle switch
        {
            LocoStyle.Stride => 0.62f, LocoStyle.Run => 0.85f, LocoStyle.Gallop => 0.95f, LocoStyle.Crawl => 0.85f,
            LocoStyle.Slither => 0.8f, LocoStyle.Wing => 0.68f, LocoStyle.Cloth => 0.8f, _ => 1f,
        };
        float speed = Mathf.Abs(TargetBaseSpeed() + relVelocity);
        bool airy = locoStyle == LocoStyle.Wing || locoStyle == LocoStyle.Cloth;
        float sf = airy ? Mathf.Clamp(Mathf.Sqrt(speed / 9f), 0.92f, 1.12f) : Mathf.Clamp(Mathf.Sqrt(speed / 9f), 0.8f, 1.35f);
        moveTempo = Mathf.Lerp(moveTempo, k * sf * moveTempoScale, 1f - Mathf.Exp(-dt / 0.3f));
        return moveTempo;
    }
    // 移動の1周の長さ(秒)。足音の間隔に使う。コマが無ければ -1
    float MoveCycleSeconds => Anim != null && Anim.HasMove ? Anim.move.Length / Mathf.Max(0.1f, Anim.moveFps * moveTempo) : -1f;

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
            float cyc = MoveCycleSeconds;
            float interval = footstepShake ? (cyc > 0f ? Mathf.Max(0.45f, cyc * 0.5f) : 0.55f) : 0.16f; // 2026-10-22: 重い足音は歩きの絵の周期に合わせる
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
        DebugAct = "Wait";
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
        DebugAct = "Approach";
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
        DebugAct = "MoveToGap";
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
        DebugAct = "Telegraph";
        NoteTelegraphForBreakQa();
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
        DebugAct = "Strike";
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

    // 2026-10-20: 戻りのコマ(recover_/recoverB_)がある時は、Recover の始めに順に見せてから待機へ(無いボスは今までどおりすぐ待機)
    protected float recoverArtSeconds = 0.4f;
    protected float artPoseProceduralScale = 1f; // 本物のコマの溜め/攻撃/戻りに掛ける手続きの変形の強さ(1 = 今までどおり)
    bool lastSpriteFromAnim;
    // 溜めの間の体の点滅(赤)。絵のある溜めでは弱める(死神三姉妹は弱め + 姉妹の色)
    protected float windupTintScale = 1f;
    protected Color windupTint = new Color(1f, 0.45f, 0.35f, 1f);
    // 撃破の倒れ方の表(BossDeathTuning)のキー。既定は声と同じ DeathKey
    public virtual string DeathProfileKey => DeathKey;

    protected IEnumerator Recover(float seconds)
    {
        DebugAct = "Recover";
        bool fromAttack = pose == Pose.Attack;
        EndAttack();
        var ra = Anim;
        var rf = ra != null ? ra.RecoverFor(animVariant) : null;
        if (rf != null) { recoverArtSeconds = Mathf.Clamp(seconds * 0.6f, 0.25f, 0.6f); SetPose(Pose.Recover); }
        else if (fromAttack && ra != null && ra.AttackFor(animVariant) != null) { recoverArtSeconds = Mathf.Clamp(seconds * 0.3f, 0.15f, 0.28f); SetPose(Pose.Recover); } // 2026-10-22: 攻撃の余韻(最後のコマを少し保つ)
        else if (HasRecoverArt) SetPose(Pose.Recover); // 2026-10-28: コマの連番を持たないボス(天空)の戻りの1枚絵
        yield return Wait(seconds);
        if (pose == Pose.Recover && rf == null && HasRecoverArt) SetPose(Pose.Idle); // 天空の戻りの絵は戻りの間だけ
        OnMoveEnded();
    }

    // 2026-10-28: 技ごとの1枚絵(天空のボス)。戻りの絵があるか / 技が終わった(または怯んだ)時に技の絵の指定を外す
    protected virtual bool HasRecoverArt => false;
    protected virtual void OnMoveEnded() { }

    // 予備動作を中断された時の怯み(後退して隙を見せる)
    protected IEnumerator Stagger(float seconds, float backSpeed = 2.5f)
    {
        OnMoveEnded();
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
        hurtBaseSize = hurtCol.size;
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
        if (GameManager.Instance != null) HitStop.Begin(seconds); // 2026-10-10: 消えない実行役で
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
        return new Vector3(worldX + facing * (FrontReach + forward), GroundY + yOffset + height * UpSign, 0f);
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
            // 2026-10-18: BREAK のコンボ用。カードの追加攻撃(elementProc)/結界や燃える地面(suppressHitStop)は手動の攻撃ではない
            pendingKind = info != null ? info.kind : PlayerAttackKind.Normal;
            pendingManual = info == null || (!info.elementProc && !info.suppressHitStop);
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

    // 死神三姉妹(2026-10-20): プレイヤーが弾き返した弾が当たった(ReaperBullet)。ダメージ + 崩し(怯み/BREAK)。
    //  弾き返しはプレイヤーの手動の操作なので、BREAK 中のコンボ/命中の数にも数える
    public void TakeReflectedHit(int amount, float staggerMul, Vector3 at)
    {
        if (dead || invulnerable || NetPuppet) return;
        pendingStagger = BossBattleTuning.I.staggerReflect * staggerMul;
        pendingKind = PlayerAttackKind.Normal;
        pendingManual = true;
        TakeDamage(amount, at);
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
        PlayerAttackKind hitKind = pendingKind; bool manualHit = pendingManual && netAttacker <= 0 && !quietHit;
        bool remoteManual = !ComboRevision.Legacy && pendingManual && netAttacker > 0 && !quietHit; // 2026-10-21: 協力の相手(人)の手動の攻撃(HOST の端末)。改修前は HOST だけ
        pendingKind = PlayerAttackKind.Normal; pendingManual = false;
        int hpBeforeHit = Hp;
        Hp = Mathf.Max(0, Hp - amount);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);
        if (netAttacker <= 0) UltimateArt.OnBossDamaged(hpBeforeHit - Hp, maxHp); // #100 ULTIMATE の Gauge(発動中は溜まらない)

        // マルチプレイPhase 2 - 相手プレイヤーの攻撃では、この端末のプレイヤーの空中補助/コンボは進めない。
        if (netAttacker <= 0 && !quietHit)
        {
            if (manualHit && PlayerController.Instance != null) PlayerController.Instance.NotifyAerialHit(); // 手動の攻撃だけ(カードの追加攻撃で滞空を延ばさない)
            if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (manualHit) QaLocalHit?.Invoke(this, hitKind, Hp <= 0);
#endif

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
            BreakComboOnDeath(); // 打ち上げ中でも寄せをやめる(撃破の見た目はその位置から)
            StopAllCoroutines();
            DisableAllHitboxes();
            HideAttackGlows();
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
        if (manualHit && !dead) BreakComboOnHit(hitKind); // BREAK 中の打ち上げ/追撃/叩きつけ(2026-10-18)
        else if (remoteManual && !dead) BreakComboOnHit(hitKind, netAttacker); // 2026-10-21: JOIN の人も打ち上げ/追撃/締め(位置は HOST が決める)
        hitTimer = 0.16f;
        if (quietHit) { OnDamaged(amount); return; }
        Shake(0.06f, 0.1f);
        if (!HitImpactFx.Spawn(hitPos, 1.3f * ChainFxScale, Color.white)) // アニメーション強化(2026-10-17): 命中の衝撃のコマ(無ければ今までの火花)。BREAK のコンボ中は段ごとに少し大きく
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, hitPos, Color.white, 0.14f, 0.35f, 0.6f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
        }
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
    public int BreakPulledFromBehind { get; private set; } // 確認用: BREAK 中に後ろから前へ寄せたフレーム数
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
        BossVoice.Growl(this, DeathKey); // 2026-10-14: 段階が上がる時のうなり
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
        if (tune == null || tune.staggerMax <= 0f || Broken || phaseRoaring || entering || breakRecovering) return;
        float before = StaggerFraction;
        stagger += v * staggerDefense * (Time.time < breakResistUntil ? PostBreakStaggerScale : 1f); // BREAK の直後は溜まりにくい(2026-10-18、続けて BREAK し続けない)
        lastStaggerTime = Time.time;
        if (stagger < tune.staggerMax) { TryFlinch(before, StaggerFraction); return; } // 怯み(2026-10-18)
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

    // BREAK の本体(地面へ崩れる/打ち上げ/膝つき/回復の予告/立ち直り)は WildBossBase.BreakCombo.cs(2026-10-18)

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
        BreakComboOnReset(); // 2026-10-18: BREAK/立ち直りの途中で止められても状態を残さない
        DisableAllHitboxes();
        windingUp = false; windupProgress = 0f; interrupted = false;
        phaseRoaring = false; // 2026-10-13: 咆哮の途中で割り込まれると true のまま残り、以後ずっと崩し(BREAK)が溜まらなかった
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
        diveArtT = -1f; emergeArtUntil = 0f; exhaustArt = false; exhaustArtFrames = null; emergeArtOverride = null; // 潜る/飛び出す/隙のコマも止める
        HideAttackGlows();
        OnInterrupted();
    }

    // 2026-10-28: BREAK/撃破で技が止まった時に、溜めの光(口/コア/剣などの "...Glow")が残らないように消す(全ボス共通)
    protected void HideAttackGlows()
    {
        foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
            if (r != null && r.gameObject != gameObject && r.gameObject.name.EndsWith("Glow") && r.gameObject.name != "DarkGlow") r.enabled = false; // DarkGlow = 死神の常時の闇の光(技の光ではない)
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DebugAddStagger(float v) => AddStagger(v);
    public float DebugStagger => stagger;
#endif
    // 自然洞窟ボス強化(2026-10-04): 開発用の強制操作(段階/必殺技/BREAK)。荒野街道のボスにも使える(IBossBattleDebug。ゲーム中は呼ばれない)
    public string DebugName => bossName;
    public bool DebugAlive => !dead && isActiveAndEnabled;
    // 確認用(2026-10-17、アニメーションの確認動画): すぐに BREAK させる
    public void DebugBreak() { if (!Broken && !dead) AddStagger(1e9f); }

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
    public void DebugForceBreak() { breakResistUntil = -99f; if (tune != null && tune.staggerMax > 0f) { bool keep = BossBattle.DebugNoStagger; BossBattle.DebugNoStagger = false; AddStagger(tune.staggerMax * 1.5f / Mathf.Max(0.1f, staggerDefense)); BossBattle.DebugNoStagger = keep; } }
    // 各ボスが攻撃中に出した自分の物(溜めの玉など)を片付ける
    protected virtual void OnInterrupted() { }
    // 毎フレーム(行動のコルーチンとは別。割り込みで止まらない見た目の更新用)
    protected virtual void OnBattleTick(float dt) { }

    // ---- 特殊攻撃/必殺技の順番 ----
    // 2026-10-16(にらみ合いの修正): 特殊攻撃を続けて使う時は、間に普通の技を1回はさむ(BalanceTuning.BossSpecialBreather)。
    // 同じフレームの中で何度呼ばれても同じ答え(AI の1回の選びで、別の段階の特殊攻撃の確かめが続く書き方があるため)
    bool specialStreak; int specialSkipFrame = -1;
    public int SpecialsDeferred { get; private set; } // 確認用: 普通の技をはさむために特殊攻撃を後回しにした回数
    protected bool SpecialReady(int minPhase)
    {
        if (!(tune != null && Phase >= minPhase && Time.time - lastSpecialTime >= tune.specialCooldown)) return false;
        if (!BalanceTuning.BossSpecialBreather) return true;
        if (specialSkipFrame == Time.frameCount) return false;
        if (specialStreak) { specialStreak = false; specialSkipFrame = Time.frameCount; SpecialsDeferred++; return false; }
        return true;
    }
    protected void MarkSpecial() { lastSpecialTime = Time.time; SpecialsUsed++; specialStreak = true; }
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
        BossVoice.Growl(this, DeathKey); // 2026-10-14: 必殺技の始まりのうなり(同じボスは 6秒に1回まで)
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
        DebugAct = "Exhausted";
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
    float CamCenterOffset(Camera cam) => pc != null ? GameView.CenterX(cam) - pc.transform.position.x : GameView.CenterX(cam) - PlayerX; // 2026-10-08: 見た目のカメラでなく判定の画面
    protected float OffscreenAheadGap()
    {
        Camera cam = Camera.main;
        if (cam == null) return 26f;
        return CamCenterOffset(cam) + GameView.HalfWidth(cam) + halfWidth + 2f;
    }
    protected float OffscreenBehindGap()
    {
        Camera cam = Camera.main;
        if (cam == null) return -18f;
        return CamCenterOffset(cam) - GameView.HalfWidth(cam) - halfWidth - 2f;
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
        RewardLedger.BossTag = RewardLedger.Tag(this, bossName);
        if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward, RunRewards.BossExpFraction(RewardClass)); // 2026-10-15: EXP は格ごとの割合(第2弾: 関門の役割)
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
            yield return HitStop.Run(0.14f); // 2026-10-10: 消えない実行役で(このボスが途中で消えても停止が残らない)

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
            Destroy(gameObject, DefeatedBodyLifetime); // 2026-10-08(メモリの修正): 非表示のまま溜まらないように
        }
        finally
        {
            RegisterDefeatOnce();
        }
    }

    // Floating Origin: 座標を戻した分、ボス自身のワールドXも戻す(プレイヤーとの間合いは不変)。
    // ================= マルチプレイPhase 2(共有ボス) =================
    [System.NonSerialized] public int NetId;
    // 2026-10-15 第2弾: EXP の格(BossManager が関門の役割で決める。無ければ種類から)
    [System.NonSerialized] public RunRewards.BossClass? rewardClassOverride;
    public RunRewards.BossClass RewardClass => rewardClassOverride ?? RunRewards.ClassOf(DeathKey);
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
    public void NetApplyRemoteHit(int attacker, int damage, Vector3 hitPos, PlayerAttackKind kind = PlayerAttackKind.Normal, bool manual = true)
    {
        if (dead || invulnerable || NetPuppet) return;
        netAttacker = attacker;
        pendingKind = kind;
        pendingManual = manual && PlayerController.AllyContext <= 0; // 2026-10-21: 人の手動の攻撃だけ BREAK のコンボへ(CPU の仲間はダメージと崩しだけ)
        // 2026-10-18: 協力の相手の攻撃は種類で崩しが溜まる(上/下攻撃は多め)。CPU の仲間は今までどおり通常の量
        pendingStagger = PlayerController.AllyContext > 0 ? BossBattleTuning.I.staggerNormal : BossBattleTuning.I.StaggerFor(kind, false);
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
        // 2026-10-21: 膝つき(HOST の BREAK の状態から)。見た目と被弾判定を HOST と同じだけ低く
        float kneelWant = juggle != null && juggle.Open && juggle.style == BossJuggle.Style.Kneel ? 1f : 0f;
        kneel = Mathf.MoveTowards(kneel, kneelWant, dt / (kneelWant > 0f ? 0.25f : BreakRecoverSeconds));
        ApplyHurtShape();
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
        bool manual = info == null || (!info.elementProc && !info.suppressHitStop);
        NetCombat.RequestHit(NetId, dmg, kind, hitPos, manual);
        if (manual && juggle != null) juggle.NoteReplicaLocalHit(); // 2026-10-21: 空中の補助(再上昇/狙い)の相手として覚える
        if (PlayerController.Instance != null) PlayerController.Instance.NotifyAerialHit();
        if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();
        NetHitFx(hitPos, true);
    }

    void NetHitFx(Vector3 hitPos, bool withHitStop)
    {
        hitTimer = 0.16f;
        Shake(0.06f, 0.1f);
        if (!HitImpactFx.Spawn(hitPos, 1.3f, Color.white)) // アニメーション強化(2026-10-17): 命中の衝撃のコマ(無ければ今までの火花)
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, hitPos, Color.white, 0.14f, 0.35f, 0.6f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
        }
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
        s.Combo = juggle != null ? juggle.NetPack() : (byte)0; // 2026-10-21: BREAK コンボの状態(JOIN の空中の補助/膝つきの見た目)
        s.SpriteIdx = NetPoseArtIndex(); // 2026-10-28(同期の版 11): 技ごとの1枚絵(天空のボス。0 = なし)
    }
    protected virtual byte NetPoseArtIndex() => 0;
    protected virtual void NetApplyPoseArt(byte idx) { }

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
        NetApplyPoseArt(s.SpriteIdx);
        if (s.Combo != 0 || (juggle != null && juggle.Open))
        {
            var st = (s.Combo & 128) != 0 ? BossJuggle.Style.Kneel : BossJuggle.Style.Launch;
            if (juggle == null || juggle.style != st) juggle = new BossJuggle(st, () => LiveHurtBounds, bossName, this);
            juggle.NetApplyReplica(s.Combo);
        }
    }

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; BossBattle.Living.Add(this); RewardLedger.Note("BossEnable", RewardLedger.Tag(this, bossName)); }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; BossBattle.Living.Remove(this); if (juggle != null) juggle.CloseBreak(); }
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
    // 攻撃の本番の絵(2026-10-11、以前は発動中も色付きの四角だった): 紫/青系の色 = 魔炎の柱、それ以外 = 岩の柱。
    // 四角(根元の SpriteRenderer)は予告の間だけ。絵は子(Art)で、作った時点で用意する(マルチのミラーが見た目の部品を数えるのは Start の時)
    SpriteRenderer art;
    AtkArtSet artSet;
    Vector2 artUnit = Vector2.one;

    public static TrackedHazard Create(float worldX, float width, float heightSize, float warn, float active, Color activeColor)
    {
        GameObject go = new GameObject("TrackedHazard");
        var hz = go.AddComponent<TrackedHazard>();
        hz.warnDuration = warn;
        hz.activeDuration = active;
        hz.height = heightSize;
        hz.activeColor = activeColor;
        hz.sr = go.AddComponent<SpriteRenderer>();
        hz.sr.sprite = BossFx.WarnZone(); // 予告の範囲(発動中は Art の絵)
        hz.sr.sortingOrder = RenderOrder.Boss - 1;
        hz.sr.color = new Color(1f, 0.15f, 0.08f, 0.1f);
        go.transform.localScale = new Vector3(width, heightSize, 1f);
        if (active > 0f)
        {
            hz.artSet = activeColor.b > activeColor.g + 0.25f ? AtkArtSet.MagicPillar : AtkArtSet.RockPillar;
            Sprite a = AttackArt.First(hz.artSet, null);
            if (a != null)
            {
                var ag = new GameObject("Art");
                ag.transform.SetParent(go.transform, false);
                hz.art = ag.AddComponent<SpriteRenderer>();
                hz.art.sprite = a;
                hz.art.sortingOrder = RenderOrder.Boss + 1;
                hz.art.enabled = false;
                hz.artUnit = AttackArt.Unit(a);
            }
        }

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
        return TerrainManager.Instance.GetSupportHeightAt(x); // 予兆の帯: 穴の上でも途切れない高さ(2026-10-10)
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
            if (art != null)
            {
                sr.enabled = false; // 四角は予告だけ
                art.enabled = true;
                AttackArt.Animate(art, once: artSet == AtkArtSet.RockPillar, duration: Mathf.Max(0.2f, activeDuration));
                Vector3 foot = transform.position + new Vector3(0f, -height * 0.5f + 0.1f, 0f);
                if (artSet == AtkArtSet.RockPillar)
                    OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.RockChunk(), foot, new Color(0.75f, 0.66f, 0.55f, 1f), 5, 0.45f, 0.1f, 0.25f, 3.5f, 2f, RenderOrder.CombatFx);
                else
                    OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), foot, new Color(activeColor.r, activeColor.g, activeColor.b, 0.9f), 7, 0.45f, 0.08f, 0.18f, 3f, 3f, RenderOrder.CombatFx);
            }
            onActivate?.Invoke();
        }
        float ft = (timer - warnDuration) / activeDuration;
        Color c = activeColor; c.a = activeColor.a * Mathf.Lerp(1f, 0.3f, ft);
        sr.color = c;
        if (art != null)
        {
            // 根元からせり上がる(0.08秒)→ 終わりに向けて薄くなる。岩は色付きの絵なので色は乗せない
            float rise = Mathf.Clamp01((timer - warnDuration) / 0.08f);
            float k = Mathf.Lerp(0.35f, 1f, rise);
            art.transform.localScale = new Vector3(1f / artUnit.x, k / artUnit.y, 1f);
            art.transform.localPosition = new Vector3(0f, -(1f - k) * 0.5f, 0f);
            Color ac = AttackArt.Natural(artSet) ? new Color(1f, 1f, 1f, 1f) : new Color(Mathf.Min(1f, activeColor.r + 0.1f), activeColor.g, Mathf.Min(1f, activeColor.b + 0.1f), 1f);
            ac.a = Mathf.Lerp(1f, 0.35f, ft * ft);
            art.color = ac;
        }
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

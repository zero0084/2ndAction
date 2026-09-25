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
public abstract class WildBossBase : MonoBehaviour
{
    public enum Pose { Idle, Move, Windup, Attack, Fly, Landing }

    [Header("Identity")]
    public string bossName = "Boss";
    public int maxHp = 30;
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
    public int playerAttackDamageFallback = 2;

    public int Hp { get; private set; }
    public bool IsDead => dead;
    public int Alive => dead ? 0 : 1;

    // ---- runtime ----
    protected float worldX;
    protected float relVelocity;
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

        Camera cam = Camera.main;
        float rightEdge = cam != null ? cam.transform.position.x + cam.orthographicSize * cam.aspect : PlayerX + 12f;
        worldX = Mathf.Max(PlayerX + startGap, rightEdge) + 3f;
        lastGroundY = TerrainGround(worldX);

        OnInit();
        ApplyTransform();
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
        if (dead) return;
        float dt = Time.deltaTime;
        float baseSpeed = pc != null ? pc.CurrentAutoRunSpeed : 0f;
        worldX += (baseSpeed + relVelocity) * dt;

        float px = PlayerX;
        float gap = worldX - px;
        if (!entering)
        {
            if (gap > maxGap) worldX = px + maxGap;
            else if (gap < minGap) worldX = px + minGap;
        }

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

    void ApplyTransform()
    {
        float g = TerrainGround(worldX);
        transform.position = new Vector3(worldX, g + yOffset, 0f);
    }

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

    Sprite PickSprite()
    {
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
        float baseSpeed = pc != null ? pc.CurrentAutoRunSpeed : 0f;
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
    protected IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds && !dead) { t += Time.deltaTime; yield return null; }
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

        float t = 0f;
        while (t < duration && !dead)
        {
            t += Time.deltaTime;
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

        if (other.CompareTag("PlayerAttack"))
        {
            int dmg = PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : playerAttackDamageFallback;
            TakeDamage(dmg, other.bounds.center);
            return;
        }

        FireballController fb = other.GetComponent<FireballController>();
        if (fb != null && fb.reflected)
        {
            TakeDamage(2, other.bounds.center);
            Destroy(fb.gameObject);
        }
    }

    public void TakeDamage(int amount, Vector3 hitPos)
    {
        if (dead) return;
        Hp = Mathf.Max(0, Hp - amount);
        if (hpBar != null) hpBar.SetFraction((float)Hp / maxHp);

        if (PlayerController.Instance != null) PlayerController.Instance.NotifyAerialHit();
        if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();

        if (Hp <= 0 && OnLethalDamage())
        {
            // 天空回廊ボス追加(2026-09-25) - フェニックスの復活など、致死ダメージを
            // サブクラスが引き受けた場合は撃破処理に進まない(既定はfalse=従来どおり)。
            return;
        }

        if (Hp <= 0)
        {
            dead = true;
            StopAllCoroutines();
            DisableAllHitboxes();
            relVelocity = 0f;
            StartCoroutine(FinalHitAndDie());
            return;
        }

        hitTimer = 0.16f;
        Shake(0.06f, 0.1f);
        Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
        OneShotSpriteEffect.CreateTweened(spark, hitPos, Color.white, 0.14f, 0.35f, 0.6f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
        if (hitStopOnHit > 0f) RunHitStop(hitStopOnHit);

        if (windingUp && interruptible) interrupted = true;
        OnDamaged(amount);
    }

    // ================= 撃破演出(既存Dragonと同じ流れ) =================
    bool defeatRegistered;
    void RegisterDefeatOnce()
    {
        if (defeatRegistered) return;
        defeatRegistered = true;
        if (GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        if (BossManager.Instance != null) BossManager.Instance.OnWildBossDefeated();
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
    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }
    void OnOriginShifted(float s) { worldX -= s; }

    void OnDestroy()
    {
        if (hpBar != null) Destroy(hpBar.gameObject);
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
    BoxCollider2D col;
    SpriteRenderer sr;
    Color activeColor;
    bool activated;
    public bool damages = true;
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

    static float GroundAt(float x)
    {
        if (TerrainManager.Instance == null) return 0f;
        return TerrainManager.Instance.GetHeightAt(x) ?? 0f;
    }

    void Update()
    {
        float baseSpeed = PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f;
        Vector3 p = transform.position;
        p.x += baseSpeed * Time.deltaTime;
        p.y = GroundAt(p.x) + height * 0.5f;
        transform.position = p;

        timer += Time.deltaTime;
        if (timer < warnDuration)
        {
            float f = timer / Mathf.Max(0.01f, warnDuration);
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, f));
            sr.color = new Color(1f, Mathf.Lerp(0.5f, 0.1f, f), 0.08f, Mathf.Lerp(0.10f, 0.42f, f) * Mathf.Lerp(0.7f, 1f, blink));
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
        if (other.CompareTag("Player") && PlayerController.Instance != null) PlayerController.Instance.TakeDamage(source: "WildBoss:" + name);
    }
}

// 複数体のボスが同時に予備動作を始めないよう、開始タイミングを全体でずらす共有ゲート。
public static class BossAttackGate
{
    public static float NextTime;
    public static float Interval = 0.8f; // 調整用: 次の体が予備動作を始めるまでの最短間隔(秒)
}

using UnityEngine;

// BONUS ZONE(2026-09-29) - 報酬Enemyの動き。Playerを倒すことを目的にしない(攻撃しない、体当たりもしない)。
//  Treasure Goblin: 同じ方向へ逃げる。近づかれると少し前を走り続け、殴られると一瞬加速して離れる → 追いついてまた殴る
//  Mimic          : 宝箱のふり → 近づくと起きて、しばらくPlayerの少し前に居続ける(Comboを入れる時間) → 逃げる
//  Golden Slime   : その場で跳ねながら少しずつ進むだけ(とても弱い)
//  Card Fairy     : 少し前の空中を飛び回り、近づかれるとかわす(回数で疲れる)。捕まらなければ逃げ去る
// 地上の2種は穴を跳び越える(跳んでいる間はEnemyControllerの足場確認で落とさない: behaviourOwnsAir)。
public partial class EnemySpecialBehavior
{
    bool IsBonusKind => kind >= EnemyBehaviorKind.TreasureGoblin && kind <= EnemyBehaviorKind.CardFairy;

    [Header("BONUS - Treasure Goblin")]
    [Tooltip("遠くにいる時、Playerが近づく速さ(Playerとの速度差 m/s)")] public float goblinFarCatchUp = 4.5f;
    [Tooltip("近くでPlayerが追いつく速さ(速度差 m/s)")] public float goblinCatchUp = 2.2f;
    [Tooltip("捕まった時に保つ前方の距離(m)")] public float goblinHoldLead = 1.2f;
    [Tooltip("殴られた後の逃げる速さ(Playerより速い分 m/s)と時間")] public float goblinHitBurst = 3.6f;
    public float goblinHitBurstTime = 0.6f;
    public float goblinMinSpeed = 1.5f;
    // 2026-11-04(マスター: 「追いつく → 一撃が届く → 少し離れる → また狙える」。近づいた瞬間に逃げない・追い越さない・細かい加減速/前へのワープをしない)
    //  間合いの段: 近づかれる(Chase)→ 攻撃が届く距離で並走(Window: 一撃を入れる余裕)→ 殴られた/時間切れで少し離れる(Pull)→ また近づかれる
    //  速さは目標の相対速度へ加速度の上限つきで寄せる(段の切り替えで速さが跳ばない)。プレイヤーの速度と攻撃の射程は変えない
    [Tooltip("並走する時の前方の距離(m、体の中心どうし)。黒剣士の前攻撃1段目が届く距離の内側")] public float goblinWindowLead = 2.0f;
    [Tooltip("並走して待つ秒数(この間に一撃を入れる)")] public float goblinWindowSeconds = 1.3f;
    [Tooltip("殴られた後/待ち切った後に離れる距離(m)。攻撃が届かず、画面に見えている距離")] public float goblinAwayLead = 5.5f;
    [Tooltip("離れる時の速さ(プレイヤーより速い分 m/s)")] public float goblinPullSpeed = 3.2f;
    [Tooltip("相対速度を変える加速度の上限(m/s²)。小さいほどなめらか")] public float goblinRelAccel = 7f;
    [Tooltip("これより近い/後ろになったら前へ戻る(追い越されない)")] public float goblinMinLead = 0.8f;
    [Tooltip("画面の外(前)にいる間に寄る速さ(プレイヤーの速さに対する割合)。見えてからは goblinFarCatchUp/goblinCatchUp")] public float goblinOffscreenCatchUp = 0.6f;
    [Tooltip("高速(時速 108km 以上)で並走の距離/近すぎる距離に足す量(m)。速さに比例して 0 から")] public float goblinWindowLeadFast = 0.9f, goblinMinLeadFast = 0.6f;
    [Header("BONUS - Mimic")]
    public float mimicWakeDistance = 6.5f;
    public float mimicWakeTime = 0.45f;
    [Tooltip("起きている間に保つPlayerの前方の距離(m)")] public float mimicStayLead = 2.2f;
    [Header("BONUS - Golden Slime")]
    public float slimeHopIntervalMin = 0.7f, slimeHopIntervalMax = 1.2f;
    public float slimeHopHeight = 0.45f, slimeDrift = 0.6f;
    [Header("BONUS - Card Fairy")]
    public float fairyLead = 5.5f, fairyTiredLead = 2.2f, fairyHeight = 2.3f;
    public float fairyDodgeDistance = 2.6f, fairyDodgeSpeed = 6.5f, fairyDodgeTime = 0.35f, fairyDodgeCooldown = 0.9f;
    public int fairyMaxDodges = 3;
    [Header("BONUS - 逃走")]
    [Tooltip("BONUS終了/上限で逃げる時の速さ(Playerより速い分 m/s)")] public float bonusLeaveSpeed = 7f;

    enum BonusState { Run, Dormant, Wake, Stay, Hop, Fly, Dodge, Leave }
    BonusState bonusState;
    BonusEnemy bonusComp;
    EnemyController bonusEc;
    bool bonusInitialized;
    float bonusTimer, bonusStateTime, bonusBurstUntil, bonusLastHitSeen = -100f, bonusSpeedNow;
    float bonusJumpT = -1f, bonusJumpDur, bonusJumpStartY, bonusJumpLandX, bonusJumpStartX, bonusJumpHeight;
    enum GoblinPace { Chase, Window, Pull }
    GoblinPace goblinPace;
    float goblinRel, goblinPaceT, goblinHitSeen = -100f, goblinPlayerV = -1f, goblinPrevPx;
    int goblinPrevFrame = -10;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static readonly bool QaGoblinLegacy = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bonusChaseLegacy") >= 0; // 前後比較: 直す前の動き
#else
    const bool QaGoblinLegacy = false;
#endif
    public string DebugGoblinPace => goblinPace + $" rel {goblinRel:F2}";
    public string DebugGoblinGround { get { float x = transform.position.x; var a = Surface(x); var b = Surface(x + 1f); return $"jump={bonusJumpT:F2} s(x)={(a.HasValue ? a.Value.ToString("F1") : "none")} s(x+1)={(b.HasValue ? b.Value.ToString("F1") : "none")} y={transform.position.y:F1} speedNow={bonusSpeedNow:F1}"; } }
    int fairyDodges;
    float fairyDodgeDir = 1f, fairyEscapeAt, fairyBaseGroundY;
    SpriteRenderer fairyGlow;

    EnemyPose BonusPose => bonusState == BonusState.Dormant ? EnemyPose.Dormant : bonusState == BonusState.Wake ? EnemyPose.Wake : EnemyPose.None;
    public bool IsBonusDormant => IsBonusKind && (!bonusInitialized ? kind == EnemyBehaviorKind.Mimic : bonusState == BonusState.Dormant);

    void InitBonus()
    {
        bonusInitialized = true;
        bonusComp = GetComponent<BonusEnemy>();
        bonusEc = GetComponent<EnemyController>();
        bonusStateTime = 0f;
        switch (kind)
        {
            case EnemyBehaviorKind.TreasureGoblin: bonusState = BonusState.Run; goblinPace = GoblinPace.Chase; goblinRel = 0f; goblinPaceT = 0f; goblinPlayerV = -1f; goblinHitSeen = bonusComp != null ? bonusComp.LastHitTime : -100f; break;
            case EnemyBehaviorKind.Mimic: bonusState = BonusState.Dormant; break;
            case EnemyBehaviorKind.GoldenSlime: bonusState = BonusState.Hop; bonusTimer = Random.Range(0f, slimeHopIntervalMax); break;
            case EnemyBehaviorKind.CardFairy:
                bonusState = BonusState.Fly;
                var zone = BonusZone.Instance;
                // 強力なので1回のBONUS ZONEで出す数に上限(超えた分は出さない)
                if (zone != null && zone.SpawningAllowed && !zone.TryReserveFairy()) { gameObject.SetActive(false); return; }
                fairyEscapeAt = Time.time + (zone != null && zone.Profile != null ? zone.Profile.fairyEscapeSeconds : 9f);
                float? g = Surface(transform.position.x);
                fairyBaseGroundY = g ?? transform.position.y - fairyHeight;
                MakeFairyGlow();
                break;
        }
    }

    // 被弾/打ち上げから戻った: 途中のジャンプは捨てて、今の場所から続ける(ミミックは起きる)
    void ResetBonusAfterInterrupt()
    {
        if (!bonusInitialized) return;
        EndBonusJump();
        if (kind == EnemyBehaviorKind.Mimic && (bonusState == BonusState.Dormant || bonusState == BonusState.Wake)) { bonusState = BonusState.Stay; bonusStateTime = 0f; }
        if (kind == EnemyBehaviorKind.CardFairy && bonusState == BonusState.Dodge) bonusState = BonusState.Fly;
    }

    void BonusOnDisable()
    {
        if (bonusEc != null) bonusEc.behaviourOwnsAir = false;
        bonusJumpT = -1f;
    }

    void UpdateBonus()
    {
        if (!bonusInitialized || player == null) return;
        float dt = EDt;
        if (dt <= 0f) return;
        bonusStateTime += dt;
        bool leaving = bonusComp != null && (bonusComp.Leaving || bonusComp.Capped);
        if (leaving && bonusState != BonusState.Leave && bonusState != BonusState.Dormant) { bonusState = BonusState.Leave; bonusStateTime = 0f; }
        if (leaving && bonusState == BonusState.Dormant) { gameObject.SetActive(false); return; } // 寝たままの宝箱は静かに消す
        if (bonusComp != null && bonusComp.LastHitTime > bonusLastHitSeen) { bonusLastHitSeen = bonusComp.LastHitTime; bonusBurstUntil = Time.time + goblinHitBurstTime; }

        float v = SkyPlayerSpeed();
        float dx = transform.position.x - player.position.x;
        switch (kind)
        {
            case EnemyBehaviorKind.TreasureGoblin: UpdateGoblin(v, dx, dt); break;
            case EnemyBehaviorKind.Mimic: UpdateMimic(v, dx, dt); break;
            case EnemyBehaviorKind.GoldenSlime: UpdateGoldenSlime(v, dt); break;
            case EnemyBehaviorKind.CardFairy: UpdateCardFairy(v, dx, dt); break;
        }
        // 逃げ切った(画面外の前方/はるか後方)ら消す
        if (bonusState == BonusState.Leave && !SkyOnScreen(-1.5f)) gameObject.SetActive(false);
        else if (dx < -12f && !SkyOnScreen(-1f)) gameObject.SetActive(false); // 追い越されて画面外の後ろ: 片付ける(同時に居る数に数えない)
    }

    // ---- Treasure Goblin ----
    void UpdateGoblin(float v, float dx, float dt)
    {
        if (!QaGoblinLegacy) { UpdateGoblinPaced(v, dx, dt); return; }
        float speed;
        if (bonusState == BonusState.Leave) speed = v + bonusLeaveSpeed;
        else if (Time.time < bonusBurstUntil) speed = v + goblinHitBurst;                // 殴られた: 一瞬加速して離れる
        else if (dx > 9f) speed = v - goblinFarCatchUp;                                  // 遠い: 見える所まで早めに近づく
        else if (dx > goblinHoldLead) speed = v - goblinCatchUp;                         // 追いつかれていく
        else speed = v + Mathf.Min(1.5f, (goblinHoldLead - dx) * 1.5f);                 // 捕まった: すぐ前(攻撃が届く距離)を走り続ける
        speed = Mathf.Max(goblinMinSpeed, speed);
        bonusSpeedNow = speed;
        BonusGroundMove(speed, dt);
    }

    void UpdateGoblinPaced(float v, float dx, float dt)
    {
        // プレイヤーの実際の前進(攻撃の踏み込み・ダッシュで自動の走りより速くなる)に合わせる。合わせないと踏み込みの分だけ追い越されていた
        float px = player.position.x;
        if (goblinPlayerV < 0f) goblinPlayerV = v;
        else if (dt > 0f && Time.frameCount == goblinPrevFrame + 1) // 被弾で止まっていた間の移動は速さに数えない
        {
            float pv = (px - goblinPrevPx) / dt;
            if (pv >= 0f && pv < v + 15f) goblinPlayerV = Mathf.Lerp(goblinPlayerV, pv, 1f - Mathf.Exp(-dt / 0.08f));
        }
        goblinPrevPx = px; goblinPrevFrame = Time.frameCount;
        v = Mathf.Max(v, goblinPlayerV);
        float target, accel = goblinRelAccel;
        // 速いほど 1 フレームの前進と攻撃の踏み込みが大きいので、並走の距離と「近すぎる」距離を少し広げる(時速 36km 以下は既定のまま、時速 108km で +0.9m / +0.6m)
        float fastK = Mathf.Clamp01((v - 10f) / 20f);
        float windowLead = goblinWindowLead + goblinWindowLeadFast * fastK, minLead = goblinMinLead + goblinMinLeadFast * fastK;
        if (bonusState == BonusState.Leave) target = bonusLeaveSpeed;
        else
        {
            bool hit = bonusComp != null && bonusComp.LastHitTime > goblinHitSeen;
            // 殴られた: 離れ始める。被弾の間(ノックバック)に遅れた分は持ち越さない(後ろ向きの相対速度を捨てる = 被弾の後に追い越されない)
            if (hit) { goblinHitSeen = bonusComp.LastHitTime; goblinPace = GoblinPace.Pull; goblinPaceT = 0f; goblinRel = Mathf.Max(goblinRel, 0f); }
            goblinPaceT += dt;
            switch (goblinPace)
            {
                case GoblinPace.Chase:
                    target = dx > 9f ? -goblinFarCatchUp : -goblinCatchUp; // 遠い時は早めに、近くはゆっくり追いつかれる
                    // 画面の外(前)にいる間は速く寄る: 高速では 80〜150m 先に出て 4.5m/s で寄るので、BONUS(320m)の間に届かなかった
                    {
                        Camera gc = Camera.main;
                        float viewAhead = gc != null ? GameView.Right(gc) - player.position.x : 20f;
                        if (dx > viewAhead + 8f) { target = -Mathf.Max(goblinFarCatchUp, v * goblinOffscreenCatchUp); accel = goblinRelAccel * 4f; }
                        else if (goblinRel < -goblinFarCatchUp - 0.1f) accel = goblinRelAccel * 4f; // 画面に入る手前で速さを戻す(見えている間は速く寄らない)
                    }
                    // 並走の距離の手前で、相対速度 0 へ減速し始める(止まる距離 = rel² / 2a)
                    float stopDist = goblinRel < 0f ? goblinRel * goblinRel / (2f * accel) : 0f;
                    if (dx - windowLead <= stopDist + 0.05f) { goblinPace = GoblinPace.Window; goblinPaceT = 0f; }
                    break;
                case GoblinPace.Window:
                    target = Mathf.Clamp((windowLead - dx) * 2.5f, -1.2f, 1.5f); // 並走(少しずれたらゆっくり戻す)
                    if (goblinPaceT >= goblinWindowSeconds) { goblinPace = GoblinPace.Pull; goblinPaceT = 0f; }
                    break;
                default: // Pull
                    target = goblinPullSpeed;
                    float brake = goblinRel > 0f ? goblinRel * goblinRel / (2f * accel) : 0f;
                    if (dx + brake >= goblinAwayLead) { goblinPace = GoblinPace.Chase; goblinPaceT = 0f; }
                    break;
            }
            // 追い越されない(攻撃の踏み込み等でプレイヤーが前へ出た): 前へ戻る。強めに加速するが瞬間移動はしない
            if (dx < minLead) { target = Mathf.Max(target, Mathf.Min(8f, 1.5f + (minLead - dx) * 4f)); accel = goblinRelAccel * 3f; }
        }
        goblinRel = Mathf.MoveTowards(goblinRel, target, accel * dt);
        float speed = Mathf.Max(goblinMinSpeed, v + goblinRel);
        bonusSpeedNow = speed;
        BonusGroundMove(speed, dt);
    }

    // ---- Mimic ----
    void UpdateMimic(float v, float dx, float dt)
    {
        switch (bonusState)
        {
            case BonusState.Dormant:
                // 宝箱のふり(動かない)。近づかれる/殴られると起きる(殴られた時はResetBonusAfterInterruptで起きる)
                if (dx < mimicWakeDistance && SkyOnScreen()) { bonusState = BonusState.Wake; bonusStateTime = 0f; }
                return;
            case BonusState.Wake:
                SetGroundedXWithExtraY(transform.position.x + v * dt, Mathf.Sin(Mathf.Clamp01(bonusStateTime / mimicWakeTime) * Mathf.PI) * 0.5f);
                if (bonusStateTime >= mimicWakeTime) { bonusState = BonusState.Stay; bonusStateTime = 0f; }
                return;
            case BonusState.Stay:
            {
                var zone = BonusZone.Instance;
                float stay = zone != null && zone.Profile != null ? zone.Profile.mimicStaySeconds : 8f;
                if (bonusStateTime >= stay) { bonusState = BonusState.Leave; bonusStateTime = 0f; if (zone != null) zone.Popup(transform.position + Vector3.up * 1.2f, "BYE!", new Color(0.8f, 0.85f, 1f), bonusComp, true); return; }
                // Playerの少し前に居続ける(殴りやすい距離)。小さく跳ねて生き物らしく
                float target = player.position.x + mimicStayLead;
                float speed = v + Mathf.Clamp((target - transform.position.x) * 3f, -3f, 4.5f);
                BonusGroundMove(Mathf.Max(0f, speed), dt, Mathf.Abs(Mathf.Sin(bonusStateTime * 5f)) * 0.18f);
                return;
            }
            case BonusState.Leave:
                BonusGroundMove(v + bonusLeaveSpeed, dt);
                return;
        }
    }

    // ---- Golden Slime ----
    void UpdateGoldenSlime(float v, float dt)
    {
        if (bonusState == BonusState.Leave)
        {
            // 終了: その場で縮んで消える(ポン)
            float k = 1f - Mathf.Clamp01(bonusStateTime / 0.35f);
            transform.localScale = new Vector3(Mathf.Sign(transform.localScale.x) * Mathf.Max(0.05f, k), Mathf.Max(0.05f, k), 1f);
            if (k <= 0.05f) gameObject.SetActive(false);
            return;
        }
        bonusTimer -= dt;
        float hopLen = 0.42f;
        float extra = 0f;
        if (bonusTimer < hopLen) extra = Mathf.Sin(Mathf.Clamp01(1f - bonusTimer / hopLen) * Mathf.PI) * slimeHopHeight;
        if (bonusTimer <= 0f) bonusTimer = Random.Range(slimeHopIntervalMin, slimeHopIntervalMax) + hopLen;
        float nx = transform.position.x + slimeDrift * dt;
        if (Surface(nx + 0.4f).HasValue) SetGroundedXWithExtraY(nx, extra);
        else SetGroundedXWithExtraY(transform.position.x, extra); // 穴の手前では進まない
    }

    // ---- Card Fairy ----
    void UpdateCardFairy(float v, float dx, float dt)
    {
        Vector3 p = transform.position;
        if (fairyGlow != null)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 7f);
            fairyGlow.transform.localScale = Vector3.one * (1.25f + 0.2f * pulse);
            var c = fairyGlow.color; c.a = 0.55f + 0.3f * pulse; fairyGlow.color = c;
        }
        if (bonusState != BonusState.Leave && Time.time >= fairyEscapeAt && bonusState != BonusState.Dodge) { bonusState = BonusState.Leave; bonusStateTime = 0f; }
        float? g = Surface(player.position.x);
        if (g.HasValue) fairyBaseGroundY = Mathf.Lerp(fairyBaseGroundY, g.Value, dt * 2f);
        switch (bonusState)
        {
            case BonusState.Fly:
            {
                float lead = fairyDodges >= fairyMaxDodges ? fairyTiredLead : fairyLead;  // かわし疲れると捕まえやすくなる
                float tx = player.position.x + lead + Mathf.Sin(Time.time * 1.3f + skyBob) * 1.2f;
                float ty = fairyBaseGroundY + fairyHeight + Mathf.Sin(Time.time * 2.1f + skyBob) * 0.8f;
                float speed = dx > 9f ? v - 4f : v + Mathf.Clamp((tx - p.x) * 2f, -3f, 3f);
                p.x += speed * dt;
                p.y = Mathf.MoveTowards(p.y, ty, 3.5f * dt);
                transform.position = p;
                float dist = Vector2.Distance(p, player.position + Vector3.up * 0.7f);
                if (dist < fairyDodgeDistance && fairyDodges < fairyMaxDodges && bonusStateTime > fairyDodgeCooldown && SkyOnScreen())
                {
                    bonusState = BonusState.Dodge; bonusStateTime = 0f; fairyDodges++;
                    fairyDodgeDir = p.y > player.position.y + 1.4f ? 1f : -1f;
                }
                break;
            }
            case BonusState.Dodge:
                // ひらりとかわす: 前へ加速 + 上下へ(次の一撃の位置をずらす)
                p.x += (v + fairyDodgeSpeed) * dt;
                p.y += fairyDodgeDir * 3.2f * dt;
                p.y = Mathf.Clamp(p.y, fairyBaseGroundY + 1.1f, fairyBaseGroundY + fairyHeight + 1.6f);
                transform.position = p;
                if (bonusStateTime >= fairyDodgeTime) { bonusState = BonusState.Fly; bonusStateTime = 0f; }
                break;
            case BonusState.Leave:
                transform.position = p + new Vector3(v + bonusLeaveSpeed, 3f, 0f) * dt;
                break;
        }
    }

    void MakeFairyGlow()
    {
        var go = new GameObject("FairyGlow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        fairyGlow = go.AddComponent<SpriteRenderer>();
        fairyGlow.sprite = BonusZone.SoftCircleSprite();
        fairyGlow.color = new Color(1f, 0.55f, 0.95f, 0.7f);
        var mainSr = GetComponentInChildren<SpriteRenderer>();
        fairyGlow.sortingLayerID = mainSr != null ? mainSr.sortingLayerID : 0;
        fairyGlow.sortingOrder = mainSr != null ? mainSr.sortingOrder - 1 : 0;
    }

    // ---- 地上の移動(穴は跳び越える) ----
    void BonusGroundMove(float speed, float dt, float extraY = 0f)
    {
        if (bonusJumpT >= 0f) { StepBonusJump(dt); return; }
        float x = transform.position.x;
        float nx = x + speed * dt;
        // 少し先に足場が無い → 向こう岸まで跳ぶ
        if (speed > 0.1f && !Surface(nx + 0.7f).HasValue && Surface(x).HasValue)
        {
            float land = -1f;
            // 2026-11-04: 高速の穴は 10m より広い(間隔は速さに比例)。向こう岸を探す範囲も速さに合わせる(以前は縁で待って追い越されていた)
            float reach = QaGoblinLegacy ? 10f : Mathf.Max(10f, speed * 1.1f + 4f);
            for (float d = 1.2f; d < reach; d += 0.4f) if (Surface(x + d).HasValue && Surface(x + d + 0.6f).HasValue) { land = x + d + 0.8f; break; }
            if (land > 0f) { StartBonusJump(land, Mathf.Max(speed, 3f)); StepBonusJump(dt); return; }
            return; // 向こう岸が見えない(生成前): 縁で待つ
        }
        float? g = Surface(nx);
        if (!g.HasValue) return;
        transform.position = new Vector3(nx, g.Value + groundYOffset + extraY, transform.position.z);
    }

    void StartBonusJump(float landX, float speed)
    {
        bonusJumpStartX = transform.position.x;
        bonusJumpStartY = transform.position.y;
        bonusJumpLandX = landX;
        bonusJumpDur = Mathf.Clamp((landX - bonusJumpStartX) / speed, 0.35f, 1.4f);
        bonusJumpHeight = 1.0f + 0.12f * (landX - bonusJumpStartX);
        bonusJumpT = 0f;
        if (bonusEc != null) bonusEc.behaviourOwnsAir = true;
    }

    void StepBonusJump(float dt)
    {
        bonusJumpT += dt / bonusJumpDur;
        float t = Mathf.Clamp01(bonusJumpT);
        float? landY = Surface(bonusJumpLandX);
        float endY = landY.HasValue ? landY.Value + groundYOffset : bonusJumpStartY;
        float x = Mathf.Lerp(bonusJumpStartX, bonusJumpLandX, t);
        float y = Mathf.Lerp(bonusJumpStartY, endY, t) + 4f * bonusJumpHeight * t * (1f - t);
        transform.position = new Vector3(x, y, transform.position.z);
        if (t >= 1f) EndBonusJump();
    }

    void EndBonusJump()
    {
        if (bonusJumpT < 0f) return;
        bonusJumpT = -1f;
        if (bonusEc != null) bonusEc.behaviourOwnsAir = false;
    }
}

using UnityEngine;

// エリアルコンボ改修(2026-10-18): 打ち上げた敵を、打ち上げた本人(この端末のプレイヤー)の「斜め前の当たる所」へ
// 短時間だけ寄せる(コンボの間だけの相対速度の補正)。画面全体の走行速度は変えない。
//  - 寄せるのは、この端末のプレイヤーが地上/空中で打ち上げた敵だけ(仲間/他の参加者の打ち上げは従来どおり)
//  - 追撃が当たるたびに続く。当たらない時間が ComboAnchorWindow を超える/合計 ComboAnchorMaxTotal 秒/
//    プレイヤーが着地/遠く離れた(ComboAnchorEngageDistance)/叩き落とし で終わり、あとは普通に落ちる
//  - 補正の速さは ComboAnchorMaxCorrection まで(瞬間移動しない)。天井/段差/地面にめり込まない
public partial class EnemyController : IAirComboTarget
{
    public const float ComboAnchorWindow = 0.9f;        // 打ち上げ/追撃のあと、寄せ続ける時間(秒)。竜騎士の突きのように1回が長い技でも次が間に合う
    public const float ComboAnchorMaxTotal = 30f;       // 安全装置だけ(2026-10-18 追加の依頼: 手動の攻撃が当たり続ける間は長いコンボを許す)
    public const float ComboAnchorEngageDistance = 4.5f; // これより離れていたら寄せない(遠くの敵を引き寄せない)
    public const float ComboAnchorGain = 9f;            // 位置のずれ → 補正の速さ(1/秒)
    public const float ComboAnchorMaxCorrection = 7f;   // 補正の速さの上限(m/秒、プレイヤーの速さに足す分)
    public const float ComboFinisherBurst = 7f;
    public const float ComboMinHeight = 0.45f;
    public const float ComboRiseTime = 0.15f, ComboRiseMinSpeed = 6f;
    public const float CeilingPushDownPerSec = 20f;
    public const float ComboDownPushTime = 0.4f, ComboDownPushDepth = 1.6f; // ↓が当たった後、敵を下へ押す時間と深さ(地面に届けば叩きつけ)       // 寄せている間の、地面からの最低の高さ(足元)         // 叩き落としの着地で生き残った時、前へ大きく飛ばす速さ(m/秒)

    // 今この端末のプレイヤーがコンボ中の敵(1体)。遠距離の技の狙いの補正に使う
    public static EnemyController LocalComboTarget { get; private set; }
    // 確認用(QA): 寄せた回数 / 寄せが途中で切れた理由
    public static int ComboAnchorsStarted, ComboAnchorEndedFar, ComboAnchorEndedTimeout, ComboAnchorEndedLanded;

    bool comboAnchored;
    [System.NonSerialized] public string ComboEndReason = ""; // 確認用: 寄せが終わった理由
    float comboAnchorTimer, comboAnchorTotal, comboLift, comboLandedFor;
    bool comboPlayerWasAirborne;
    float comboRiseTimer; // 地上から打ち上げた直後: 必ず上がる時間(この間は着地しない)
    float comboLastFloorY = float.MinValue; // プレイヤーの足元の床(最後に分かった高さ)
    float comboDownTimer; // ↓が当たった後: 下へ押している間(この間に地面へ届けば叩きつけ = 締め)
    // この命中がプレイヤー自身の手動の攻撃か(カードの追加攻撃/結界や燃える地面の継続ダメージは false = 滞空を延ばさない)
    [System.NonSerialized] public bool comboManualHit = true;
    public static int ComboDownPushes, ComboGroundSlams;
    public static int ComboBlockedStep, ComboBlockedCeiling, ComboDucked, ComboCeilLookAhead; // 確認用(2026-10-21): 段差/天井の段で横の寄せが止まった/くぐったフレーム数
    float comboBlockedAt = -9f; char comboBlockedKind = '-';
    int comboAirHits;

    public bool IsComboAnchored => comboAnchored && !dying;
    public bool IsLaunchedForCombo => isLaunched && !dying;
    public int ComboAirHits => comboAirHits;
    public bool DebugMovingWithPlayer => groundKnockbackTimer > 0f || groundFollowAssistTimer > 0f; // 確認用: プレイヤーと並走中(地上のノックバック/追従)
    public string DebugAirState => $"end={ComboEndReason} launched={isLaunched} anchored={comboAnchored} slam={isSlamming} kb={groundKnockbackTimer:F2} follow={groundFollowAssistTimer:F2} vac={beingVacuumed} active={gameObject.activeSelf}";
    // 遠距離の技で狙う所(体の中心)
    public Vector3 ComboAimPoint => transform.position + BodyCenterOffset();

    Vector3 BodyCenterOffset()
    {
        var b = BodyBounds();
        return b.HasValue ? b.Value.center - transform.position : new Vector3(0f, 0.5f, 0f);
    }

    // 2026-10-29: 当たりの箱の範囲は今の位置から作る。Collider2D.bounds は物理の更新(50回/秒)の時の位置のままで、
    //  60fps では6フレームに1回ほど1フレーム前の位置になる → 高速では天井を 0.7m 後ろで測り、細い針の天井に頭が入っていた
    public static bool DebugStaleBounds; // 試験用: 前の作り(Collider2D.bounds)で比べる
    public static bool DebugNoCeilLookAhead; // 試験用: 先の天井を見て早めに下げるのをやめる(前の作り)
    public const float CeilingLookAheadSec = 0.15f;
    public static Bounds FreshBounds(Collider2D c)
    {
        if (DebugStaleBounds || !(c is BoxCollider2D b)) return c.bounds;
        Vector3 ls = b.transform.lossyScale;
        Vector3 center = b.transform.TransformPoint(b.offset);
        Vector3 size = new Vector3(Mathf.Abs(b.size.x * ls.x) + b.edgeRadius * 2f, Mathf.Abs(b.size.y * ls.y) + b.edgeRadius * 2f, 0f);
        return new Bounds(new Vector3(center.x, center.y, 0f), size);
    }

    Bounds? BodyBounds()
    {
        if (bodyCols == null) bodyCols = GetComponentsInChildren<Collider2D>(true);
        Bounds? acc = null;
        foreach (var c in bodyCols)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            if (c.GetComponent<EnemyMeleeHitbox>() != null) continue;
            Bounds cb = FreshBounds(c);
            if (acc.HasValue) { var b = acc.Value; b.Encapsulate(cb); acc = b; } else acc = cb;
        }
        return acc;
    }

    // この端末のプレイヤーの攻撃が当たった(ProcessHit の最初、倒した時も含む)
    void ComboNoteLocalHit(PlayerAttackKind kind, bool killed)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        if (kind == PlayerAttackKind.Up && !isLaunched)
        {
            comboAirHits = 1; // 打ち上げ = 1段目
            if (!killed) AerialComboFx.Launch(this);
            return;
        }
        if (!isLaunched) return;
        comboAirHits = Mathf.Max(1, comboAirHits) + 1;
        if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterAir(comboAirHits, kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact || killed);
    }

    // 地上/空中で打ち上げた(この端末のプレイヤー)。wasLaunched = 当たる前から浮いていた(空中の打ち上げ直し)
    void ComboAnchorBegin(bool wasLaunched)
    {
        var pc = PlayerController.Instance;
        if (pc == null || NetReplica) return;
        if (!comboAnchored) { comboAnchorTotal = 0f; comboPlayerWasAirborne = !pc.IsGrounded; ComboAnchorsStarted++; }
        comboLandedFor = 0f;
        comboAnchored = true;
        comboAnchorTimer = ComboAnchorWindow;
        comboLift = wasLaunched ? 1.0f : 0f; // 空中の打ち上げ直しは少しだけ上へ(その後プレイヤーの二段ジャンプで追いつく)
        if (!wasLaunched) comboRiseTimer = ComboRiseTime;
        if (LocalComboTarget != null && LocalComboTarget != this) LocalComboTarget.ComboAnchorEnd();
        LocalComboTarget = this;
        BossJuggle.ReleaseLocal(); // ボスの BREAK のコンボから雑魚へ移った(同時に2体を寄せない。2026-10-18 ボス対応)
    }

    // ボスの BREAK のコンボが始まった: 寄せていた雑魚を手放す(普通に落ちる)
    public static void ReleaseLocalComboTarget()
    {
        if (LocalComboTarget != null) LocalComboTarget.ComboAnchorEnd();
        LocalComboTarget = null;
    }


    // ↓が当たった(この端末のプレイヤー、手動、コンボ中の敵): その場で終わらせず下へ押す。地面へ届いたら叩きつけ(締め)、
    // 届く前なら → / ↑ でつなぎ直せる。地面が無い所(穴の上)では衝撃を出さない
    public bool ComboTryDownPush()
    {
        if (!comboAnchored && LocalComboTarget != this) return false;
        if (!comboAnchored) ComboAnchorBegin(true);
        comboLift = 0f;
        comboDownTimer = ComboDownPushTime;
        comboAnchorTimer = ComboAnchorWindow;
        ComboDownPushes++;
        AerialComboFx.DownHit(this);
        return true;
    }

    // 空中の追撃が当たった(この端末のプレイヤー)
    void ComboAnchorRefresh()
    {
        if (!comboAnchored) { if (isLaunched && LocalComboTarget == this) ComboAnchorBegin(true); comboLift = 0f; return; }
        comboAnchorTimer = ComboAnchorWindow;
        comboDownTimer = 0f; // ↓の後に → / ↑ が当たった: つなぎ直し
    }

    void ComboAnchorEnd()
    {
        comboAnchored = false;
        comboAnchorTimer = 0f;
        comboLift = 0f;
        comboDownTimer = 0f;
        if (LocalComboTarget == this) LocalComboTarget = null;
    }

    // Update の打ち上げ中の処理の代わり。寄せた時は true(位置はここで決めた)
    bool ComboAnchorStep()
    {
        var pc = PlayerController.Instance;
        float dt = Time.deltaTime;
        if (pc == null || dying || isSlamming || NetReplica || beingVacuumed) { ComboEndReason = "state"; ComboAnchorEnd(); return false; }
        comboAnchorTimer -= dt;
        comboAnchorTotal += dt;
        bool playerAir = !pc.IsGrounded;
        if (playerAir) comboPlayerWasAirborne = true;
        if (comboAnchorTimer <= 0f || comboAnchorTotal > ComboAnchorMaxTotal) { ComboEndReason = "timeout"; ComboAnchorEndedTimeout++; ComboAnchorEnd(); return false; }
        // 2026-10-21: 着地してから 0.25秒の猶予(高速の段差で一瞬浮いて着いただけで終わらない。跳び直せば続く)
        if (comboPlayerWasAirborne && !playerAir) comboLandedFor += dt; else comboLandedFor = 0f;
        if (comboPlayerWasAirborne && !playerAir && comboLandedFor > (ComboRevision.Legacy ? 0f : BossJuggle.LandGrace)) { ComboEndReason = "playerLanded"; ComboAnchorEndedLanded++; ComboAnchorEnd(); return false; }

        // プレイヤーが穴へ落ちた(足元の床より1m以上下): 寄せをやめる(敵を一緒に地面の中/穴の中へ引きずり込まない)
        var tmA = TerrainManager.Instance;
        if (tmA != null)
        {
            float? gp = tmA.GetHeightAt(pc.transform.position.x);
            if (gp.HasValue) comboLastFloorY = gp.Value;
            else if (pc.transform.position.y < comboLastFloorY - 1f) { ComboEndReason = "playerInPit"; ComboAnchorEnd(); return false; }
        }
        Vector2 sweet = pc.AerialComboSweetSpot();
        comboLift = Mathf.MoveTowards(comboLift, 0f, 2.2f * dt);
        bool pushingDown = comboDownTimer > 0f || pc.IsAerialDiving;
        if (comboDownTimer > 0f) comboDownTimer -= dt;
        float push = comboDownTimer > 0f ? -ComboDownPushDepth : 0f;
        Vector2 targetCenter = (Vector2)pc.transform.position + new Vector2(sweet.x, sweet.y + comboLift + push);
        Vector2 target = targetCenter - (Vector2)BodyCenterOffset();
        // 寄せている間は地面より少し上に保つ(プレイヤーが着地する直前に先に着いて、締めが地上の攻撃になっていた)。
        // ↓で下へ押している間/急降下の間は保たない(地面まで届けば叩きつけ)
        float? gT = LandingSurfaceAt(target.x, target.y + 1f);
        if (!pushingDown && gT.HasValue && target.y < gT.Value + ComboMinHeight) target.y = gT.Value + ComboMinHeight;
        Vector2 pos = transform.position;
        Vector2 diff = target - pos;
        if (diff.magnitude > ComboAnchorEngageDistance) { ComboEndReason = $"far({diff.x:F1},{diff.y:F1}{(Time.time - comboBlockedAt < 0.5f ? ",blk" + comboBlockedKind : "")})"; ComboAnchorEndedFar++; ComboAnchorEnd(); return false; }

        Vector2 corr = Vector2.ClampMagnitude(diff * ComboAnchorGain, ComboAnchorMaxCorrection);
        Vector2 pv = pc.MotionVelocity;
        float vx = pv.x + corr.x;
        float vy;
        if (playerAir) vy = pv.y + corr.y;
        else { launchVelocityY -= launchGravity * dt; vy = launchVelocityY; } // まだ地上(打ち上げた直後): 普通に上がる
        if (comboRiseTimer > 0f) { comboRiseTimer -= dt; vy = Mathf.Max(vy, ComboRiseMinSpeed); } // 落ち始めたプレイヤーに引かれて、打ち上げた瞬間に着地しない
        vy = Mathf.Clamp(vy, -22f, 18f);
        launchVelocityX = vx;
        launchVelocityY = vy;
        launchForwardBurstTimer = 0f;

        Vector3 np = transform.position + new Vector3(vx * dt, vy * dt, 0f);
        // 段差(壁のような高い地面)へは横に入らない
        float? gAhead = LandingSurfaceAt(np.x, transform.position.y + 0.3f);
        if (gAhead.HasValue && gAhead.Value > transform.position.y + 0.6f) { np.x = transform.position.x; ComboBlockedStep++; comboBlockedAt = Time.time; comboBlockedKind = 's'; }
        // 天井の段(急に0.6m以上低くなる所)は壁と同じ: 横から入らない(入ると一瞬で数m下へ押し下げられて見えた)
        if (CeilingStepAhead(np.x))
        {
            // 2026-10-21: 天井の段の下へ体が入るなら、横に止めずに下げてくぐらせる(下げる速さは毎秒 CeilingPushDownPerSec まで。
            // 入りきるまでは横に進まない = 天井を突き抜けない)。以前は段の前で横に止まり続け、走るプレイヤーに置いていかれて寄せが切れていた
            float? cA = LowestCeilingOverBody(np.x);
            var bbA = BodyBounds();
            float topOff = bbA.HasValue ? bbA.Value.max.y - transform.position.y : 1f;
            float wantY = cA.HasValue ? cA.Value - topOff - 0.05f : transform.position.y;
            float? floorA = cA.HasValue ? LandingSurfacePure(np.x, wantY + 0.05f, out _, out _) : null;
            bool fits = !ComboRevision.Legacy && cA.HasValue && floorA.HasValue && wantY >= floorA.Value;
            if (fits)
            {
                np.y = Mathf.Max(wantY, Mathf.Min(np.y, transform.position.y - CeilingPushDownPerSec * dt));
                if (launchVelocityY > 0f) launchVelocityY = 0f;
                if (np.y > wantY + 0.01f) np.x = transform.position.x; // まだ下がりきっていない
                ComboDucked++;
            }
            else { np.x = transform.position.x; ComboBlockedCeiling++; comboBlockedAt = Time.time; comboBlockedKind = 'c'; }
        }
        // 2026-10-29: 速く走っている時は、少し先(0.15秒先)の天井が下がっているなら、今から毎秒 CeilingPushDownPerSec で下げ始める。
        //  以前は天井の真下に来たフレームで、横の移動の 1.2倍まで一度に押し下げ、150km/h では1フレームに約1m下がって瞬間移動に見えた
        if (!ComboRevision.Legacy && !DebugNoCeilLookAhead)
        {
            float runAhead = Mathf.Max(0f, pv.x) * CeilingLookAheadSec;
            if (runAhead > 0.5f)
            {
                float? cF = LowestCeilingOverBody(np.x + runAhead);
                var bbF = BodyBounds();
                if (cF.HasValue)
                {
                    float topF = bbF.HasValue ? bbF.Value.max.y - transform.position.y : 1f;
                    float wantF = cF.Value - topF - 0.05f;
                    float? flF = LandingSurfacePure(np.x, np.y + 0.05f, out _, out _);
                    if (flF.HasValue) wantF = Mathf.Max(wantF, flF.Value + ComboMinHeight); // 床へは押し付けない
                    if (np.y > wantF) { np.y = Mathf.Max(wantF, Mathf.Min(np.y, transform.position.y - CeilingPushDownPerSec * dt)); ComboCeilLookAhead++; }
                }
            }
        }
        // 天井にめり込まない(頭が天井の少し下で止まる)
        np.y = CeilingClampY(np.x, np.y, transform.position.y, hard: true); // 寄せている間は天井の下へしっかり収める(天井が下がってきても、動きは連続なので跳ばない)
        transform.position = np;

        float? g = LandingSurfaceAt(np.x, transform.position.y + 0.3f);
        // 一番下の地面は、穴でなければ必ず床(高速の上り坂で、地面が「今の高さ+0.3」より速く上がっても坂の中へ入らない)
        float? gLow = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(np.x) : null;
        if (gLow.HasValue && (!g.HasValue || gLow.Value > g.Value) && np.y < gLow.Value)
        { g = gLow; onUpperRoute = false; onIsland = false; }
        if (g.HasValue && np.y <= g.Value)
        {
            // 下へ押していない時は、地面(上り坂)に沿って乗り上げるだけで着地しない(高速の上り坂で、打ち上げた直後に着地していた)。
            // 着地(コンボの終わり)は、↓で押した時/プレイヤーが着地した時/寄せの時間が切れた後
            if (!pushingDown) { transform.position = new Vector3(np.x, g.Value, np.z); if (launchVelocityY < 0f) launchVelocityY = 0f; }
            else if (launchVelocityY <= 0f && comboRiseTimer <= 0f)
            {
                ComboAnchorEndedLanded++;
                ComboEndReason = $"enemyLanded(pAir={playerAir} vy={pc.MotionVelocity.y:F1})";
                bool slam = pushingDown; // ↓/急降下で地面まで届いた = 叩きつけ(締め)
                ComboAnchorEnd();
                if (slam) { isSlamming = true; ComboGroundSlams++; AerialComboFx.Slam(this); }
                LandFromLaunch(g.Value);
            }
            else transform.position = new Vector3(np.x, g.Value, np.z);
        }
        else if (np.y < enemyDeathBelowY)
        {
            ComboAnchorEnd();
            dying = true;
            gameObject.SetActive(false);
            RegisterKillReward(fallDeath: true);
        }
        return true;
    }

    // 叩き落としの着地で生き残った: 最後の一撃として前へ大きく飛ばす(そのまま走り抜けて体当たりにならない)
    void ComboFinisherKnockback()
    {
        groundKnockbackVelocityX = PlayerForwardSpeed() + ComboFinisherBurst * Mathf.Clamp(hitKnockbackScale, 0.6f, 1.5f);
        groundKnockbackTimer = groundKnockbackDuration * 1.5f;
        DisableMotionComponents();
    }

    // 非表示/撃破(EnemyController.OnDisable から)
    void ComboOnDisable()
    {
        if (LocalComboTarget == this) LocalComboTarget = null;
        comboAnchored = false;
    }

    // 浮いている敵が降りる床: 上ルート/地面/浮島のうち、今の高さ(+少し)以下で一番高い面。その面のルートへ乗り換える。
    // 以前は敵が覚えているルートの面だけを見ていたため、上ルートの下で叩き落とした敵が上の床へ数m吸い上げられることがあった
    float? LandingSurfaceAt(float x, float fromY)
    {
        float? best = LandingSurfacePure(x, fromY, out bool bestUpper, out bool bestIsland);
        if (best.HasValue) { onUpperRoute = bestUpper; onIsland = bestIsland; }
        return best;
    }

    // 確認用(QA)にも使う: 床の高さだけ(敵のルートの記憶は変えない)
    public float? LandingSurfacePure(float x, float fromY, out bool bestUpper, out bool bestIsland)
    {
        bestUpper = false; bestIsland = false;
        var tm = TerrainManager.Instance;
        if (tm == null) return null;
        float? best = null; bool bu = false, bi = false;
        void Consider(float? h, bool upper, bool island)
        {
            if (!h.HasValue || h.Value > fromY) return;
            if (!best.HasValue || h.Value > best.Value) { best = h; bu = upper; bi = island; }
        }
        Consider(tm.GetHeightAt(x), false, false);
        Consider(tm.GetSurfaceAt(x, true), true, false);
        Consider(tm.GetSkyHeightAt(x, fromY), false, true);
        bestUpper = bu; bestIsland = bi;
        return best;
    }

    // 浮いている敵の頭が天井(針の先を含む)より上へ出ない高さ。上へ向かう速さは止める
    float CeilingClampY(float x, float y, float prevY, bool hard = false)
    {
        float? c = LowestCeilingOverBody(x);
        if (!c.HasValue) return y;
        var bb = BodyBounds();
        float top = bb.HasValue ? bb.Value.max.y - transform.position.y : 1f;
        float maxY = c.Value - top - 0.05f;
        if (y <= maxY) return y;
        if (launchVelocityY > 0f) launchVelocityY = 0f;
        // 下へ押すのは毎秒 CeilingPushDownPerSec、または横の移動の2.2倍まで(下がってくる天井に沿って下がる。一瞬で数m下がって見えない)
        if (hard) return Mathf.Max(maxY, Mathf.Min(y, prevY - Mathf.Max(CeilingPushDownPerSec * Time.deltaTime, Mathf.Abs(x - transform.position.x) * CeilingSlopeFactor)));
        return Mathf.Max(maxY, Mathf.Min(prevY, y)); // 前のフレームから既に上にいた時は、急に下げず上がるのを止めるだけ
    }

    // 空中で次につなげられる敵: 今のコンボの敵(近ければ)、なければプレイヤーの前の近くにいる敵(浮いている/地上どちらでも)
    static readonly Collider2D[] candBuf = new Collider2D[24];
    public static EnemyController FindComboCandidate(PlayerController pc, float reachX, float reachY)
    {
        if (pc == null) return null;
        Vector2 p = pc.transform.position;
        var t = LocalComboTarget;
        if (t != null && t.isActiveAndEnabled && !t.dying)
        {
            Vector2 d = (Vector2)t.ComboAimPoint - p;
            if (d.x > -0.8f && d.x < reachX && Mathf.Abs(d.y - 0.6f) < reachY) return t;
        }
        int n = Physics2D.OverlapBoxNonAlloc(p + new Vector2(reachX * 0.5f - 0.4f, 0.6f), new Vector2(reachX + 0.8f, reachY * 2f), 0f, candBuf);
        EnemyController best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var e = candBuf[i] != null ? candBuf[i].GetComponentInParent<EnemyController>() : null;
            if (e == null || e.dying || !e.isActiveAndEnabled || e.NetReplica || e.bonus != null) continue;
            float dd = ((Vector2)e.ComboAimPoint - p).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = e; }
        }
        return best;
    }

    // この x の天井が、今の頭の高さより 0.6m 以上低い(天井の段 = 横から入れない)
    // 段かどうかは、このフレームの横の移動に比べて急に低いか(坂: 横の移動の2.2倍まで下がるのは、天井に沿って下がるだけ)
    bool CeilingStepAhead(float x)
    {
        float? c = LowestCeilingOverBody(x);
        if (!c.HasValue) return false;
        var bb = BodyBounds();
        float top = bb.HasValue ? bb.Value.max.y : transform.position.y + 1f;
        float dxAbs = Mathf.Abs(x - transform.position.x);
        return c.Value < top - Mathf.Max(0.6f, dxAbs * CeilingSlopeFactor);
    }
    public const float CeilingSlopeFactor = 1.2f; // 約50°より急に下がる天井は壁と同じ(1フレームで大きく押し下げて見えない)

    // 体の幅(左端・中央・右端)の上の天井のうち一番低い所(x = 敵の基準点の x)
    float? LowestCeilingOverBody(float x)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return null;
        var bb = BodyBounds();
        float halfW = bb.HasValue ? bb.Value.extents.x : 0.4f;
        float cx = bb.HasValue ? x + (bb.Value.center.x - transform.position.x) : x;
        float? c = null;
        foreach (float sx in new[] { cx - halfW, cx, cx + halfW })
        {
            float? ci = tm.GetEffectiveCeilingHeightAt(sx);
            if (ci.HasValue && (!c.HasValue || ci.Value < c.Value)) c = ci;
        }
        return c;
    }
}

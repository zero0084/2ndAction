using System.Collections;
using UnityEngine;

// エリアルコンボ改修(2026-10-18): 打ち上げ → 空中追撃 → 締め を全12人でつなぎやすくする、プレイヤー側の共通の層。
//  1) 先行入力: 攻撃の最中/ヒットストップの最中に入れたフリックを捨てず、動ける瞬間に1回だけ出す
//     (受け付けられたかは「入力の前後で技の状態が変わったか」で見る = 同じ技が2回出ない)
//  2) コンボの狙いの位置: 打ち上げた敵を寄せる「プレイヤーの斜め前」(キャラの攻撃が届く所)
//  3) 実際の移動速度: 敵をプレイヤーと並走させる基準(踏み込み/減速/補助の移動を含む)
public partial class PlayerController
{
    [Header("Aerial Combo 2026-10-18 (先行入力)")]
    // フリックを覚えておく時間(秒、ゲームの時間)。攻撃の最中に早めに入れた次の入力を、動ける瞬間に出す。
    public float flickBufferWindow = 0.45f; // 2026-10-18 追加の依頼: 前の技(約0.4秒)の最中に入れた次の入力が、出る前に消えないよう 0.3 → 0.45秒

    // 2026-10-21 コンボのつながり改修: 予約は1つだけ。技の最中は時間を減らさずに待ち(長い技の竜騎士でも消えない)、
    // 入れてから FlickBufferMaxAge 秒たった入力は出さない(操作をやめたのに古い入力で攻撃し続けない)
    public const float FlickBufferMaxAge = 0.9f;
    FlickDirection? bufferedFlick;
    float bufferedFlickTimer, bufferedFlickAge;
    bool bufferedFlickOnce; // 上フリック/ヒットストップ中の入力: 1回だけ試して捨てる(ジャンプの二重発動を防ぐ)
    // 確認用(QA): 先行入力から出た技の数 / 捨てた数
    public static int FlickBufferFired, FlickBufferExpired, AerialAimUsed;

    // 入力の前後で比べる「技の状態」。どれかが変わったら入力は受け付けられた
    struct ActSig
    {
        public bool attacking, comboBuffered, dive, hover, kitDive, lanceDive;
        public int comboCount, kitGen, lanceGen, jumps, mageLevel, hoverShots, jumpSerial;
        public float cooldown;
        public LanceMoveKind lanceMove;
    }

    ActSig CurrentSig() => new ActSig
    {
        attacking = isAttacking, comboBuffered = comboBuffered, dive = isDiveAttacking, hover = isHoverShooting,
        kitDive = kitDiveActive, lanceDive = lanceDiving, comboCount = comboCount, kitGen = kitGeneration,
        lanceGen = lanceGeneration, jumps = jumpsUsed, mageLevel = mageLevel, hoverShots = hoverShotsUsedThisAirtime, jumpSerial = fireJumpSerial,
        cooldown = attackCooldownTimer, lanceMove = LanceMove,
    };

    static bool SigAccepted(ActSig a, ActSig b) =>
        a.attacking != b.attacking || a.comboBuffered != b.comboBuffered || a.dive != b.dive || a.hover != b.hover ||
        a.kitDive != b.kitDive || a.lanceDive != b.lanceDive || a.comboCount != b.comboCount || a.kitGen != b.kitGen ||
        a.lanceGen != b.lanceGen || a.jumps != b.jumps || a.mageLevel != b.mageLevel || a.hoverShots != b.hoverShots ||
        a.lanceMove != b.lanceMove || a.jumpSerial != b.jumpSerial || b.cooldown > a.cooldown + 0.0001f;

    // 技の最中(この間の入力は、終わった/次の段の受付が開いた瞬間に出す)
    bool InputBusy => isAttacking || attackCooldownTimer > 0f || isDiveAttacking || kitDiveActive || lanceDiving || isHoverShooting;

    // 被弾/停止/カード選択など: 覚えている入力を捨てる
    public void FlickBufferClear()
    {
        if (bufferedFlick.HasValue) TraceFlick(bufferedFlickId, bufferedFlick.Value, FlickFate.Cleared);
        FlickBufferForget();
    }
    void FlickBufferForget() { bufferedFlick = null; bufferedFlickTimer = 0f; bufferedFlickId = 0; }

    // 新しい入力を覚える(予約は1つだけ: 前に覚えていた入力は新しい入力に置き換える)
    void FlickBufferStore(FlickDirection f, int id, bool once)
    {
        if (bufferedFlick.HasValue) TraceFlick(bufferedFlickId, bufferedFlick.Value, FlickFate.Replaced);
        bufferedFlick = f; bufferedFlickTimer = flickBufferWindow; bufferedFlickAge = 0f; bufferedFlickOnce = once; bufferedFlickId = id;
        TraceFlick(id, f, FlickFate.Reserved);
    }

    // ヒットストップ(時間が止まっている間)に入ったフリック: 止まりが明けた最初のフレームで1回だけ試す
    void FlickBufferStoreFrozen(FlickDirection f)
    {
        FlickBufferStore(f, NextFlickId(), true);
    }

    // 入力の処理の前: 今のフレームに入力が無ければ、覚えている入力を入れる
    bool FlickBufferBefore()
    {
        if (bufferedFlick.HasValue)
        {
            bufferedFlickAge += Time.deltaTime;
            if (!InputBusy || bufferedFlickOnce || ComboRevision.Legacy) bufferedFlickTimer -= Time.deltaTime; // 技の最中は待つ(次の段の受付/技の終わりまで)
            if (bufferedFlickTimer <= 0f || (bufferedFlickAge > FlickBufferMaxAge && !ComboRevision.Legacy)) { TraceFlick(bufferedFlickId, bufferedFlick.Value, FlickFate.Expired); FlickBufferForget(); FlickBufferExpired++; }
        }
        if (requestedFlick.HasValue || !bufferedFlick.HasValue) return false;
        requestedFlick = bufferedFlick;
        return true;
    }

    // 入力の処理の後: 受け付けられたら忘れる。技の最中で受け付けられなかった入力は覚える
    void FlickBufferAfter(FlickDirection? flick, bool fromBuffer, bool busyBefore, ActSig before)
    {
        if (!flick.HasValue) return;
        bool accepted = SigAccepted(before, CurrentSig());
        int id = fromBuffer ? bufferedFlickId : (LastFlickWasAssist ? 0 : NextFlickId());
        if (accepted)
        {
            if (fromBuffer) { FlickBufferFired++; TraceFlick(id, flick.Value, FlickFate.FiredLater); FlickBufferForget(); }
            else
            {
                if (bufferedFlick.HasValue) TraceFlick(bufferedFlickId, bufferedFlick.Value, FlickFate.Replaced); // 新しい入力が先に出た
                FlickBufferForget();
                TraceFlick(id, flick.Value, FlickFate.Immediate);
            }
            return;
        }
        if (fromBuffer)
        {
            // 上フリックとヒットストップ中の入力は1回だけ(上は着地の予約が別にある)。
            // 2026-10-21: 技が終わって動けるのに出なかった入力(着地した後の↓など)も捨てる(後でジャンプした瞬間に古い入力が出ない)
            if (!busyBefore && (bufferedFlickOnce || !ComboRevision.Legacy)) { TraceFlick(id, flick.Value, FlickFate.Ignored); FlickBufferForget(); }
            return;
        }
        // 新しい入力: 上は既存の着地の予約に任せる(ここで覚えると着地後に二段目が出てしまう)
        if (flick.Value == FlickDirection.Up) { TraceFlick(id, flick.Value, bufferedUpAttackTimer > 0f ? FlickFate.LandBuffered : FlickFate.Ignored); return; }
        if (!busyBefore) { TraceFlick(id, flick.Value, FlickFate.Ignored); return; } // 動ける時に受け付けられなかった入力(地上の下など)は捨てる
        // 2026-10-21: 連撃の次の段が既に同じ向きで予約されている(comboBuffered)なら、重ねて覚えない(予約は1つだけ)
        if (!ComboRevision.Legacy && comboBuffered && (flick.Value == FlickDirection.Forward || flick.Value == FlickDirection.Backward) &&
            bufferedDirection == (flick.Value == FlickDirection.Forward ? AttackDirection.Forward : AttackDirection.Backward))
        { TraceFlick(id, flick.Value, FlickFate.Merged); return; }
        FlickBufferStore(flick.Value, id, false);
    }

    // ---------------------------------------------------------------- 並走の基準

    // このフレームの実際の移動速度(踏み込み/減速/補助の移動を含む)。浮動原点のずらしなど大きな跳びは無視する
    public Vector2 MotionVelocity { get; private set; }
    Vector3 motionPrevPos; bool motionPrevValid;

    void UpdateMotionVelocity()
    {
        float dt = Time.deltaTime;
        Vector3 p = transform.position;
        if (motionPrevValid && dt > 0f)
        {
            Vector3 d = p - motionPrevPos;
            if (d.sqrMagnitude < 25f) MotionVelocity = new Vector2(d.x / dt, d.y / dt);
        }
        motionPrevPos = p; motionPrevValid = true;
    }

    // ---------------------------------------------------------------- コンボの狙いの位置

    // 打ち上げた敵の体の中心を寄せる位置(プレイヤーの足元基準、前方向 +x)。キャラの空中攻撃が一番当たる所
    public Vector2 AerialComboSweetSpot() => SweetSpotFor(isRangedCharacter, isLancerCharacter, kit, IsNoble, lanceDiving, isDiveAttacking || kitDiveActive);

    // 2026-10-21: 協力の相手(HOST から見た JOIN のプレイヤー)でも同じ「当たる所」を使えるように、キャラの定義から求める
    public static Vector2 SweetSpotFor(CharacterDefinition def, bool diving)
    {
        if (def == null) return new Vector2(1.1f, 0.55f);
        return SweetSpotFor(def.isRanged, def.isLancer, def.kit, def.characterId == "noble_lady", def.isLancer && diving, !def.isLancer && diving);
    }

    static Vector2 SweetSpotFor(bool ranged, bool lancer, CharacterKit kit, bool noble, bool lanceDive, bool dive)
    {
        // 急降下の最中: 落ちていく先(前の少し下)へ。真下へ落ちる技(竜騎士/剣士の急降下)でも叩き落とせる
        if (lanceDive) return new Vector2(ComboRevision.Legacy ? 0f : LanceComboDiveX, -0.05f); // 竜騎士の急降下: 真下への突きの判定の真ん中(足元の少し上〜下)。コンボ中は少し前(2026-10-21)
        if (dive) return new Vector2(0.55f, -0.3f);
        if (ranged) return new Vector2(2.6f, 0.75f);           // 拳銃士: 横撃ちの弾の高さ
        if (lancer) return new Vector2(1.7f, 0.7f);            // 竜騎士: 突きの先
        switch (kit)
        {
            case CharacterKit.Archer: return new Vector2(2.8f, 0.7f);
            case CharacterKit.Mage: return new Vector2(2.6f, 0.75f);
            case CharacterKit.Miko: return new Vector2(2.6f, 0.75f);
            case CharacterKit.Fighter: return new Vector2(1.0f, 0.62f);
            case CharacterKit.Ninja: return new Vector2(1.1f, 0.6f);
            case CharacterKit.Vampire: return new Vector2(1.05f, 0.65f);
            case CharacterKit.Dragonkin: return new Vector2(1.15f, 0.7f);
        }
        if (noble && BalanceTuning.NobleMoves) return new Vector2(1.05f, 0.35f); // お嬢様: 空中の薙ぎ払い(前と下)
        return new Vector2(1.1f, 0.55f);                                   // 黒剣士/双剣士: 通常攻撃の箱
    }

    // 空中で遠距離の技を出す時の狙いの補正(近くのコンボ対象だけ、角度の差が小さい時だけ)
    public Vector2 AerialComboAim(Vector2 origin, Vector2 dir) => AerialComboAim(origin, dir, AerialAimMaxAngle);
    public Vector2 AerialComboAim(Vector2 origin, Vector2 dir, float maxAngle)
    {
        if (IsAlly || dir.sqrMagnitude < 1e-6f) return dir;
        var t = AirComboTargets.Local; // 雑魚 or BREAK 中のボス(2026-10-18)
        if (t == null || !t.IsComboAnchored || isGrounded) return dir; // 空中の追撃の間だけ
        if (t is BossJuggle) maxAngle = Mathf.Max(maxAngle, BossAimMaxAngle); // BREAK 中のボスは体が大きい: 頭の上から撃つ時も体へ向ける(2026-10-18)
        AerialAimUsed++;
        Vector2 to = (Vector2)t.ComboAimPoint - origin;
        float dist = to.magnitude;
        if (dist < 0.3f || dist > AerialAimMaxDistance) return dir;
        float ang = Vector2.SignedAngle(dir, to);
        if (Mathf.Abs(ang) > maxAngle) return dir;
        return to.normalized * dir.magnitude;
    }
    // 打ち上げの弾/矢/札/雷(Up): 前の近く(4.5m)の敵へ向ける(角度の差 55°まで)。速いほど敵がすぐ弾の道を通り過ぎ、
    // 打ち上げが当たらなかった(150km/h で 2割以下)。近くに敵がいなければ今までどおりの角度
    public Vector2 AerialLaunchAim(Vector2 origin, Vector2 dir)
    {
        if (IsAlly || dir.sqrMagnitude < 1e-6f) return dir;
        var e = AirComboTargets.FindCandidate(this, 4.5f, 2.2f);
        if (e == null) return dir;
        Vector2 rel = (Vector2)e.ComboAimPoint - origin;
        // 弾は走りと一緒に流れるので、止まっている敵は自動前進の速さで近づいてくる。届く瞬間の位置へ向ける(数回の繰り返しで解く)
        // BREAK 中のボスはプレイヤーと並走している(近づいてこない)ので、先回りしない(2026-10-18)
        float sp = dir.magnitude, R = e is BossJuggle ? 0f : CurrentAutoRunSpeed;
        Vector2 to = rel;
        if (sp > 0.1f)
            for (int k = 0; k < 4; k++) { float tt = to.magnitude / sp; to = new Vector2(rel.x - R * tt, rel.y); }
        if (to.sqrMagnitude < 0.04f) return dir;
        if (Mathf.Abs(Vector2.SignedAngle(dir, to)) > 70f) return dir;
        AerialLaunchAimUsed++;
        return to.normalized * dir.magnitude;
    }
    public static int AerialLaunchAimUsed;

    public const float AerialAimMaxDistance = 9f;
    public const float AerialAimMaxAngle = 40f;
    public const float BossAimMaxAngle = 70f;
    public const float AerialSlamAimAngle = 95f; // 叩き落としの技(斜め下の弾/矢/魔法)は、前にいる対象へ向けてよい

    // ---------------------------------------------------------------- キャラごとの締め(足りない所の追加)

    // この端末のプレイヤーが空中で、打ち上げた敵を寄せている最中(締めの技の切り替えに使う)
    public bool IsAerialDiving => isDiveAttacking || lanceDiving || kitDiveActive;
    public bool AerialComboActive => !IsAlly && !isGrounded && AirComboTargets.Local is IAirComboTarget t && t.IsComboAnchored;

    // 竜人: 空中のコンボ中の下 = 翼をたたんで急降下(爪で叩き落とし、着地で衝撃)。コンボ中でなければ今までどおり炎のブレス
    public static int DragonDives;
    IEnumerator DragonDive()
    {
        int gen = attackGeneration;
        int token = BeginKitMove(FindPose("dive") != null ? "dive" : "claw3", true);
        DragonDives++;
        try
        {
            kitDiveActive = true;
            kitVerticalVelocity = -17f;
            lungeVelocityX = 3f;
            dragonGlideTimer = 0f;
            ArmKitBox(new Vector2(0.65f, 0.25f), new Vector2(1.7f, 1.6f), -40f, PlayerAttackKind.Down, 1.3f, 1.3f, 0.06f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.AttackDown);
            float t = 0f;
            while (!kitDiveLanded && t < 0.9f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            kitDiveActive = false;
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            if (!kitDiveLanded) yield break;
            SetKitPose(FindPose("dive") != null ? "dive" : "claw3", 1);
            KitBlast.Create(transform.position + new Vector3(0.4f, 0.35f, 0f), new KitBlast.Spec { radius = 1.4f * AttackRangeMultiplier, active = 0.1f, kind = PlayerAttackKind.DownImpact, damageScale = 0.8f, knockbackScale = 1.5f, hitStop = 0.05f, fx = KitProjectile.Burst, tint = new Color(1f, 0.55f, 0.25f, 0.9f), fxScale = 1.2f, shake = 0.1f }, owner: this);
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(0f, 0.1f, 0f), new Color(0.85f, 0.6f, 0.45f, 0.55f), 4, 0.45f, 0.3f, 0.55f, 2f, 2.5f, RenderOrder.EnvironmentFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.LandHeavy);
            attackCooldownTimer = 0.2f;
            t = 0f;
            while (t < 0.18f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 巫女: 空中のコンボ中の下 = 打ち上げた敵の真上に結界の印を押し当てて叩き落とす(地面の結界も今までどおり張る)
    public static int MikoSeals;
    void MikoSealStamp()
    {
        var t = AirComboTargets.Local;
        if (t == null || !t.IsComboAnchored) return;
        MikoSeals++;
        Vector3 c = t.ComboAimPoint;
        KitBlast.Create(c, new KitBlast.Spec { radius = 0.95f * AttackRangeMultiplier, active = 0.1f, kind = PlayerAttackKind.Down, damageScale = 1.2f, knockbackScale = 1.3f, hitStop = 0.06f, fx = KitProjectile.Ring, tint = new Color(1f, 0.85f, 0.45f, 0.95f), fxScale = 1.4f, shake = 0.06f }, owner: this);
    }

    public static int AirFinisherCancels;
    // 締めのために、出ている技を切る(被弾の時の切り方と同じ。向き/判定/踏み込みを戻す)
    void CancelForAirFinisher()
    {
        AirFinisherCancels++;
        attackGeneration++;
        isAttacking = false;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount = 0;
        lungeVelocityX = 0f;
        attackCooldownTimer = 0f;
        transform.localScale = Vector3.one;
        CancelKitMoves();
        CancelLanceMoves();
        kitDiveActive = false;
        if (isDiveAttacking) EndDiveAttack();
    }

    // ---------------------------------------------------------------- 2026-10-18 追加の依頼: 長い空中コンボ

    // ↑: 空中の再打ち上げ・再上昇。近くに追撃できる敵がいる時は、二段ジャンプを使い切っていても出せる
    // (空振りで上がり続けないよう、届く所に敵がいる時だけ。足元の床から高すぎる時は上がらない)
    public const float ComboRelaunchReachX = 3.2f, ComboRelaunchReachY = 2.6f;
    public const float ComboMaxHeight = 5.5f; // コンボで上がれる、足元の床からの高さ(画面の外へ上がり続けない)
    public static int AerialRelaunches, DiveReconnects;

    bool AerialRelaunchAvailable()
    {
        if (IsAlly || isGrounded || kit == CharacterKit.Mage) return false;
        return AirComboTargets.FindCandidate(this, ComboRelaunchReachX, ComboRelaunchReachY) != null;
    }

    void AerialRelaunch()
    {
        AerialRelaunches++;
        if (kitDiveActive || lanceDiving || isDiveAttacking) CancelForAirFinisher();
        jumpsUsed = Mathf.Max(0, maxJumps - 1); // 次の FireJump が「空中の上攻撃」になる(ジャンプの回数そのものは増えない)
        FireJump();
        CapComboHeight();
    }

    // 足元の床から ComboMaxHeight より上へは進まない(天井はいつもの移動の処理で止まる)
    void CapComboHeight()
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return;
        float floor = tm.GetFloorTopAt(transform.position.x);
        float h = transform.position.y - groundOffset - floor;
        // 2026-10-21: 膝をついた大型のボス(動かない)とのコンボ: 上がりすぎて体の上を空振りしないよう、
        // 攻撃の当たる高さ(足元 + 当たる所)が体の上端の少し下で止まる高さまで(上がる速さを、その高さで止まる速さに抑える)
        if (!ComboRevision.Legacy && AirComboTargets.Local is BossJuggle bj && bj.IsComboAnchored && bj.style == BossJuggle.Style.Kneel)
        {
            float maxFeet = bj.TopY - 0.4f - AerialComboSweetSpot().y;
            float room = maxFeet - (transform.position.y - groundOffset);
            float vmax = room > 0f ? Mathf.Sqrt(2f * gravity * room) : 0.5f;
            if (velocityY > vmax) { velocityY = vmax; KneelHeightCaps++; }
        }
        if (h > ComboMaxHeight) { if (velocityY > 1.5f) velocityY = 1.5f; }
        else if (h > ComboMaxHeight - 2f && velocityY > 0f) velocityY *= 0.3f + 0.7f * Mathf.Clamp01((ComboMaxHeight - h) / 2f);
    }
    public static int KneelHeightCaps; // 確認用

    // 2026-10-21 追加調査: 低い天井の下で、打ち上げたボス(体が大きい)が天井につかえている時は、プレイヤーも攻撃の当たる高さ
    // (足元 + 当たる所)がボスの体の上の方に収まる高さまでしか上がらない(以前はプレイヤーだけ天井近くまで上がり、ボスの上を空振りして
    // 寄せの時間が切れていた: 洞窟の大コウモリ × 格闘家)。毎フレーム、上がる速さをその高さで止まる速さに抑える(急に止めない)
    public static int CeilingComboCaps; // 確認用
    void ComboCeilingCap()
    {
        if (!ComboRevision.Followup || velocityY <= 0f || !AerialComboActive) return;
        if (!(AirComboTargets.Local is BossJuggle bj) || bj.style != BossJuggle.Style.Launch || float.IsInfinity(bj.CeilCap)) return;
        float maxFeet = bj.CeilCap + bj.HalfHeight * 0.4f - AerialComboSweetSpot().y;
        float room = maxFeet - (transform.position.y - groundOffset);
        float vmax = room > 0f ? Mathf.Sqrt(2f * gravity * room) : 0f;
        if (velocityY > vmax) { velocityY = vmax; CeilingComboCaps++; }
    }

    // ↓の急降下の最中に → / ↑: 急降下をやめて、その場の空中の技へつなぎ直す(コンボ中だけ)
    bool TryDiveReconnect()
    {
        if (!AerialComboActive) return false;
        if (!(isDiveAttacking || kitDiveActive || lanceDiving)) return false;
        DiveReconnects++;
        CancelForAirFinisher();
        if (velocityY < -2f) velocityY = -2f;
        return true;
    }
}

using System.Collections;
using UnityEngine;

// ボスの怯み / BREAK 中のエリアルコンボ / BREAK の終わり(2026-10-18)
//  ・怯み(通常時): 崩しのゲージが 40% / 75% を越えた時に、短く(約0.6秒)よろける。攻撃の溜め/攻撃中/必殺技/咆哮/無敵/
//    画面の外へ回る技/天井や地中の技の間は怯まない(スーパーアーマー: 白い輪だけ出す)。次の怯みまで 2.5秒。
//    ゲージの目盛りで決まるので、多段の攻撃だけで怯み続けることはない(1本のゲージで最大2回)。
//  ・BREAK: 今までどおり崩しが満タンで地面へ崩れる。小〜中型/飛行(Launch)は ↑で体ごと打ち上がり、
//    この端末のプレイヤーの空中の追撃に合わせて「当たる所」へ寄る(BossJuggle)。大型/固定(Kneel)は膝をついて低くなる。
//    手動の攻撃が当たると BREAK が少し延びる(1回 0.15秒、合計で元の長さの 60% まで。無限には続かない)。
//  ・終わり: 残り 0.8秒で回復の予告(白い輪+点滅、寄せをやめて落ちる)→ 地面で短く立ち直る(0.5秒)→ 少し間(0.3秒)→ 行動。
//    立ち直る間は崩しが溜まらず、立ち直ってから 6秒は崩しの溜まりが半分(続けて BREAK し続けない)。
public abstract partial class WildBossBase
{
    public const float BreakWarnSeconds = 0.8f;
    public const float BreakExtendPerHit = 0.15f, BreakExtendMaxRatio = 0.6f;
    public const float BreakRecoverSeconds = 0.5f, BreakGraceSeconds = 0.3f;
    public const float PostBreakResistSeconds = 6f, PostBreakStaggerScale = 0.5f;
    public const float FlinchSeconds = 0.42f, FlinchAfterWait = 0.2f, FlinchCooldown = 2.5f;
    public const float KneelSquash = 0.2f;
    public const float BreakComboEdge = 0.9f; // BREAK 中の位置: 体の手前の端がプレイヤーの前 0.9m(地上の↑が届く)
    public const float BreakLaunchMinRemain = 2.2f; // 打ち上げた時、BREAK の残りをこれだけは確保する(延長の上限の中で)
    public const float BreakExtendMinCap = 1.8f;    // 延長の上限(秒)の最小。上限 = max(元の長さ×0.6, 1.8秒)
    public const float LaunchMaxBodyHeight = 4.6f; // これ以下の体の高さ = 体ごと打ち上げる(より大きいボスは膝をつく)
    static readonly float[] FlinchMarks = { 0.4f, 0.75f };

    BossJuggle juggle;
    public BossJuggle Juggle => juggle; // 確認用(QA)
    // BREAK 中の扱い: 既定は体の高さで分ける。ボスごとに上書きできる(地中/雲海に沈む等)
    protected virtual BossJuggle.Style BreakComboStyle => bodyHeight <= LaunchMaxBodyHeight ? BossJuggle.Style.Launch : BossJuggle.Style.Kneel;
    public BossJuggle.Style DebugBreakStyle => BreakComboStyle;
    // 怯ませない状態(天井/地中の技など、割り込むと位置が跳ぶ状態)。ボスごとに上書き
    protected virtual bool FlinchBlocked => false;

    bool breakRoutineRunning, breakRecovering, breakWarned, breakAltitudeReturning;
    float breakExtra, kneel, flinchTimer;
    float breakResistUntil = -99f, flinchReadyAt = -99f;
    PlayerAttackKind pendingKind;
    bool pendingManual;

    // 確認用(QA)
    public int FlinchCount { get; private set; }
    public int FlinchArmored { get; private set; }
    public float LastBreakSeconds { get; private set; }
    public float LastBreakExtra { get; private set; }
    public float BreakElapsed { get; private set; }
    public bool BreakRecovering => breakRecovering;
    // BREAK/立ち直りの間は、ボスごとの浮遊の動き(高さを毎フレーム決める処理)を止める(打ち上げ/落下/立ち直りの高さを BREAK の処理が決める)
    protected bool BreakHoldsAltitude => Broken || breakRecovering || breakAltitudeReturning;
    public bool BreakWarned => breakWarned;
    public float BreakEndedAt { get; private set; } = -99f;
    public float FirstAttackAfterBreak { get; private set; } = -1f; // BREAK が終わってから最初の攻撃の予告(Telegraph)までの秒数
    public static int FlinchesTotal, FlinchArmoredTotal;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static System.Action<WildBossBase, PlayerAttackKind, bool> QaLocalHit; // 確認用: この端末のプレイヤーの手動の攻撃が当たった(倒した時も)
    public float DebugStaggerMax => tune != null ? tune.staggerMax : 0f;
    public void DebugResetResist() { breakResistUntil = -99f; }
    public float DebugBreakDuration => tune != null ? tune.breakDuration : 0f;
#endif

    // ---------------------------------------------------------------- 怯み

    void TryFlinch(float before, float after)
    {
        if (!BossJuggle.Enabled) return; // 比較動画の「改修前」
        bool crossed = false;
        foreach (float m in FlinchMarks) if (before < m && after >= m) crossed = true;
        if (!crossed) return;
        if (Time.time < flinchReadyAt) return;
        if (FlinchAllowed()) { InterruptAI(FlinchRoutine()); return; }
        // スーパーアーマー(溜め/攻撃中/必殺技など): 怯まない。耐えたことだけ見せる(予告を隠さない薄い輪)
        FlinchArmored++; FlinchArmoredTotal++;
        flinchReadyAt = Time.time + 0.8f;
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, new Color(1f, 1f, 1f, 0.55f), 0.25f, bodyHeight * 0.3f, bodyHeight * 0.9f, 0.6f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
    }

    bool FlinchAllowed()
    {
        if (!supportsInterrupt || dead || entering || Broken || breakRecovering || NetPuppet) return false;
        if (UltimateRunning || phaseRoaring || invulnerable || freeGap || windingUp || UltimateArt.ArenaFreeGap) return false;
        if (FlinchBlocked || IntentionallyAway || yOffset < -0.05f) return false;
        foreach (var hb in hitboxes) if (hb != null && hb.IsActive) return false;
        // 行動の合間(待つ/近づく/離れる/構え直す)だけ。技の途中(突進/跳躍/飛び道具の連射など)は割り込まない
        switch (DebugAct)
        {
            case "Wait": case "Recover": case "Approach": case "MoveToGap": case "CaveRecovery": return true;
        }
        return false;
    }

    IEnumerator FlinchRoutine()
    {
        DebugAct = "Flinch";
        FlinchCount++; FlinchesTotal++;
        flinchReadyAt = Time.time + FlinchCooldown;
        flinchTimer = FlinchSeconds;
        SetPose(Pose.Idle);
        Shake(0.07f, 0.12f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, CenterWorld);
        float y0 = yOffset, t = 0f;
        bool hover = y0 > 0.3f;
        while (t < FlinchSeconds && !dead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / FlinchSeconds);
            relVelocity = -facing * 3.2f * (1f - f); // 少し後ろへよろける
            if (hover) yOffset = y0 - 0.35f * Mathf.Sin(Mathf.PI * f); // 浮いているボスは少し沈む
            yield return null;
        }
        relVelocity = 0f;
        if (hover) yOffset = y0;
        flinchTimer = 0f;
        yield return Wait(FlinchAfterWait);
    }

    // ---------------------------------------------------------------- BREAK

    IEnumerator BreakRoutine()
    {
        breakRoutineRunning = true;
        breakWarned = false;
        breakExtra = 0f;
        SetPose(Pose.Landing);
        Shake(0.18f, 0.25f);
        var style = BreakComboStyle;
        bool air = yOffset > 0.05f;
        if (juggle == null || juggle.style != style) juggle = new BossJuggle(style, () => LiveHurtBounds, bossName, this);
        juggle.OpenBreak(air);
        AerialComboFx.BreakBurst(CenterWorld, Mathf.Clamp(bodyHeight, 2.5f, 7f));
        if (!air) ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        else if (style == BossJuggle.Style.Kneel) StartCoroutine(SetAltitude(0f, 0.3f)); // 大型の飛行: 今までどおり地面へ(体ごとは落とさない)
        float t = 0f, starT = 0f;
        float baseDur = tune.breakDuration;
        while (!dead)
        {
            float dur = baseDur + breakExtra;
            if (t >= dur && !juggle.Airborne) break;
            if (t >= dur + 1.5f) break; // 安全装置(落ちきらない時も BREAK は終わる)
            t += Time.deltaTime;
            starT -= Time.deltaTime;
            BreakElapsed = t;
            if (!breakWarned && t >= dur - BreakWarnSeconds && BossJuggle.Enabled)
            {
                breakWarned = true;
                juggle.StopAccepting(); // 寄せをやめる(浮いていれば落ちて、地面で立ち直る)
                AerialComboFx.RecoverWarn(CenterWorld, Mathf.Clamp(bodyHeight, 2.5f, 7f));
            }
            bool juggled = juggle.style == BossJuggle.Style.Launch && (juggle.Anchored || juggle.Airborne); // 大型は寄せの位置のまま(位置はここで決める)
            if (juggled) { relVelocity = 0f; breakPulling = false; }
            else
            {
                relVelocity = -facing * 1.2f * Mathf.Clamp01(1f - t / 0.5f); // 少しよろけて下がる
                // 2026-10-07: BREAK はプレイヤーへのご褒美の時間。体の手前の端が届かない距離(3m超)にいる時だけ、
                // 届く位置(手前の端がプレイヤーから2m)へ滑らかに寄せる(瞬間移動しない)。届く位置にいるボスは動かさない
                if (t > 0.3f)
                {
                    float gapNow = worldX - PlayerX;
                    // 手前の端は被弾判定(hurtCol)で測る(絵の幅とは違うことがある)。物理の更新を待たない今の範囲で(2026-10-18、高速で数フレーム前の位置だった)
                    Bounds hb = LiveHurtBounds;
                    float half = hurtCol != null && hurtCol.enabled ? hb.extents.x : halfWidth;
                    float centerOff = hurtCol != null && hurtCol.enabled ? hb.center.x - transform.position.x : 0f;
                    float g = gapNow + centerOff;
                    float edge = Mathf.Abs(g) - half;
                    // 2026-10-10(全体点検): 後ろ側(突進/すり抜けの途中で BREAK した等)にいる時は、その場に留めず必ず前へ寄せる
                    bool behind = g < -0.5f;
                    // 2026-10-18: 地上の↑(踏み込まない技)も届く所(手前の端がプレイヤーの前 約0.9m)まで寄る(以前は2m = 前攻撃の踏み込みでしか届かなかった)
                    bool legacy = !BossJuggle.Enabled; // 比較動画の「改修前」: 手前の端 2m(3m より遠い時だけ寄せる)
                    float wantEdge = legacy ? 2f : BreakComboEdge;
                    float pullFrom = legacy ? BreakReach : BreakComboEdge + 0.6f, stopAt = legacy ? 2.2f : BreakComboEdge + 0.15f;
                    if (edge > pullFrom || behind) breakPulling = true;   // 届かない/後ろ → 寄せ始める
                    else if (edge <= stopAt) breakPulling = false;  // 手前の端が届く所まで来たら止める(届くぎりぎりで止めない)
                    if (breakPulling)
                    {
                        float want = (half + wantEdge) - centerOff; // いつも前(プレイヤーの進む側)の届く位置
                        BreakPulledFromBehind += behind ? 1 : 0;
                        relVelocity = Mathf.Clamp((want - gapNow) / 0.25f, -45f, 45f);
                    }
                    else if (edge < 0.3f && !legacy)
                    {
                        // プレイヤーが体の中へ入り込んだ(踏み込み/空中の前進): 少しだけ前へずれる(遠距離の弾が体の内側から出て当たらない)
                        float want = (half + 0.5f) - centerOff;
                        relVelocity = Mathf.Clamp((want - gapNow) / 0.25f, 0f, 20f);
                    }
                }
            }
            // 回復が近い: 白く速く点滅(体の色だけ。予告の赤とは違う)
            if (breakWarned) SetBodyTint(Color.Lerp(Color.white, new Color(0.7f, 0.85f, 1f), Mathf.PingPong(t * 9f, 1f)));
            else SetBodyTint(Color.Lerp(new Color(0.7f, 0.8f, 1f), Color.white, Mathf.PingPong(t * 2.5f, 0.6f)));
            if (starT <= 0f && !breakWarned)
            {
                starT = 0.22f;
                Vector3 hp = transform.position + new Vector3(Random.Range(-halfWidth * 0.5f, halfWidth * 0.5f), bodyHeight * Random.Range(0.75f, 1.05f), 0f);
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), hp, new Color(1f, 0.95f, 0.45f, 1f), 0.4f, 0.25f, 0.05f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            }
            if (juggle.Airborne) { if (pose != Pose.Landing) SetPose(Pose.Landing); }
            else if (pose == Pose.Landing && poseTime > 0.45f) SetPose(Pose.Idle);
            yield return null;
        }
        LastBreakSeconds = t;
        LastBreakExtra = breakExtra;
        bool wasAir = juggle.Airborne;
        juggle.CloseBreak();
        relVelocity = 0f; breakPulling = false;
        SetBodyTint(Color.white);
        Broken = false;
        stagger = 0f;
        staggerDefense = 1f;
        breakRoutineRunning = false;
        BreakEndedAt = Time.time; FirstAttackAfterBreak = -1f;
        if (dead) yield break;
        if (wasAir && yOffset > 0.02f) yield return SetAltitude(0f, 0.2f); // 安全装置で終わった時だけ(普通は落ちきっている)
        if (!BossJuggle.Enabled) { if (restAltitude > 0.01f) yield return SetAltitude(restAltitude, 0.4f); SetPose(Pose.Idle); yield break; } // 比較動画の「改修前」

        // 立ち直り: 地面で短く起き上がる。この間は攻撃しない・崩しは溜まらない
        breakRecovering = true;
        DebugAct = "BreakRecover";
        SetPose(Pose.Landing);
        BossVoice.Growl(this, DeathKey);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 6, 0.9f);
        float r = 0f;
        while (r < BreakRecoverSeconds && !dead)
        {
            r += Time.deltaTime;
            float f = Mathf.Clamp01(r / BreakRecoverSeconds);
            relVelocity = -facing * 2.4f * (1f - f); // 一歩下がって構え直す
            SetBodyTint(Color.Lerp(new Color(1f, 0.95f, 0.85f), Color.white, f));
            yield return null;
        }
        relVelocity = 0f;
        SetBodyTint(Color.white);
        breakRecovering = false;
        breakResistUntil = Time.time + PostBreakResistSeconds;
        flinchReadyAt = Time.time + FlinchCooldown;
        if (restAltitude > 0.01f) { breakAltitudeReturning = true; yield return SetAltitude(restAltitude, 0.4f); breakAltitudeReturning = false; }
        SetPose(Pose.Idle);
        yield return Wait(BreakGraceSeconds);
    }

    // 毎フレーム(Update の中、間合いの補正の後): BREAK 中に打ち上げたボスの位置 / 大型の膝つき
    void BreakComboStep(float dt)
    {
        float kneelWant = Broken && juggle != null && juggle.Open && juggle.style == BossJuggle.Style.Kneel ? 1f : 0f;
        kneel = Mathf.MoveTowards(kneel, kneelWant, dt / (kneelWant > 0f ? 0.25f : BreakRecoverSeconds));
        if (flinchTimer > 0f) flinchTimer -= dt;
        if (juggle == null || !juggle.Open || juggle.style != BossJuggle.Style.Launch) return;
        if (!juggle.Airborne && !juggle.Anchored) return;
        Bounds b = LiveHurtBounds;
        Vector3 off = b.center - transform.position; // 体の中心の根元からのずれ(どちらも前のフレームの位置なので、ずれは正しい)
        float ground = TerrainGround(worldX);
        float rootY = ground + yOffset;
        Vector2 center = new Vector2(worldX + off.x, rootY + off.y);
        float restCenterY = ground + off.y;
        float ceilMax = float.PositiveInfinity;
        float ahead = TargetBaseSpeed() * 0.12f; // 少し先の天井も見る(走りながら低い天井へ入る前に下がり始める)
        for (int i = -1; i <= 2; i++)
        {
            float? c = CeilingWorldYAt(i <= 1 ? center.x + b.extents.x * i : center.x + b.extents.x + ahead);
            if (c.HasValue) ceilMax = Mathf.Min(ceilMax, c.Value - (bodyHeight - off.y) - 0.1f);
        }
        Vector2 d = juggle.Step(center, b.extents, restCenterY, ceilMax, dt, out bool slammed, out bool landed);
        worldX += d.x;
        yOffset = Mathf.Max(0f, rootY + d.y - TerrainGround(worldX));
        if (slammed)
        {
            Vector3 gp = new Vector3(worldX + off.x, TerrainGround(worldX), 0f);
            AerialComboFx.BossSlam(new Vector3(center.x, rootY + d.y + off.y, 0f), gp, Mathf.Clamp(bodyHeight, 2.5f, 6f));
            Shake(0.16f, 0.22f);
            RunHitStop(0.05f);
            SetPose(Pose.Landing);
        }
        else if (landed)
        {
            ImpactDust(new Vector3(worldX, TerrainGround(worldX), 0f), 8, 1f);
            Shake(0.07f, 0.12f);
            SetPose(Pose.Landing);
        }
    }

    // TakeDamage から(この端末のプレイヤーの手動の攻撃だけ)
    void BreakComboOnHit(PlayerAttackKind kind)
    {
        if (juggle == null || !juggle.Open || !Broken) return;
        int up0 = BossJuggle.Launches + BossJuggle.Relaunches, down0 = BossJuggle.DownPushes, l0 = BossJuggle.Launches;
        bool kneelSlam = juggle.OnLocalHit(kind);
        float cap = BreakExtendCap;
        if (juggle.Anchored && juggle.Accepting)
            breakExtra = Mathf.Min(breakExtra + BreakExtendPerHit, cap);
        // 打ち上げた(コンボの始まり): BREAK の終わり際でも、コンボをつなぐ時間を残す(上限の中で)
        if (BossJuggle.Launches > l0 || (kind == PlayerAttackKind.Up && juggle.Anchored && juggle.style == BossJuggle.Style.Kneel))
            breakExtra = Mathf.Clamp(BreakElapsed + BreakLaunchMinRemain - tune.breakDuration, breakExtra, cap);
        float fxScale = Mathf.Clamp(bodyHeight * 0.4f, 1f, 2.2f);
        if (BossJuggle.Launches + BossJuggle.Relaunches > up0)
        {
            AerialComboFx.Launch(juggle.ComboAimPoint, fxScale);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.HitLaunch, CenterWorld);
            SetPose(Pose.Landing);
        }
        else if (BossJuggle.DownPushes > down0) AerialComboFx.DownHit(juggle.ComboAimPoint, fxScale);
        if (kneelSlam)
        {
            AerialComboFx.BossSlam(juggle.ComboAimPoint, new Vector3(juggle.ComboAimPoint.x, GroundY, 0f), Mathf.Clamp(bodyHeight, 2.5f, 6f));
            Shake(0.14f, 0.2f);
        }
    }

    float BreakExtendCap => Mathf.Max(tune.breakDuration * BreakExtendMaxRatio, BreakExtendMinCap);
    public Bounds LiveHurtBounds => hurtCol != null ? BossJuggle.LiveBounds(hurtCol) : new Bounds(CenterWorld, new Vector3(1f, bodyHeight, 1f));
    public float DebugBreakMax => tune != null ? tune.breakDuration + BreakExtendCap : 0f;

    // 連続で当てている間は命中の衝撃を少しずつ大きく(BREAK のコンボの手応え)
    float ChainFxScale => juggle != null && juggle.IsComboAnchored ? 1f + 0.07f * Mathf.Min(juggle.AirHits, 6) : 1f;

    // 割り込み(ResetCombatState)で BREAK の途中の処理が止められた時: BREAK の状態が残らないように戻す
    void BreakComboOnReset()
    {
        if (breakRoutineRunning)
        {
            breakRoutineRunning = false;
            Broken = false; stagger = 0f; breakPulling = false;
            if (juggle != null) juggle.CloseBreak();
        }
        breakRecovering = false; breakAltitudeReturning = false;
        flinchTimer = 0f;
    }

    void BreakComboOnDeath()
    {
        if (juggle != null) juggle.CloseBreak();
        flinchTimer = 0f;
    }

    // 立ち直ってから最初の攻撃の予告までの時間(確認用)
    void NoteTelegraphForBreakQa()
    {
        if (BreakEndedAt > 0f && FirstAttackAfterBreak < 0f) FirstAttackAfterBreak = Time.time - BreakEndedAt;
    }
}

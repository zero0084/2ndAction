using UnityEngine;

// カードバランス v3(2026-10-03): カードの値をプレイヤーへ反映する側。
//  ・速度/ジャンプ/攻撃時間/範囲/ジャンプ回数は「前の値との比/差」で反映する(CARD BALANCE TEST の層を壊さない)。
//  ・攻撃力 = キャラの基礎 × (1 + 無条件の枠 A) × (1 + 条件の枠 C)。C は「今の状態」(空中/地上/HP/速度など)と、
//    命中した時の「相手/技」(初撃/締め/ボス/空中の敵/下攻撃)を足してから一度だけ曲線を通す(PlayerAttackInfo.ScaleDamage)。
//  ・OVERDRIVE / AIR DOMINION / SKY MASTER / BRAKE ATTACK / HUNTER などの状態もここ。
public partial class PlayerController
{
    float cardSpeedFactor = 1f, cardJumpFactor = 1f, cardDurationFactor = 1f, cardRangeFactor = 1f;
    int cardExtraJumps;
    int baseJumpCount = 2;
    CardTotals cardT;

    public void ResetCardFactors()
    {
        cardSpeedFactor = cardJumpFactor = cardDurationFactor = cardRangeFactor = 1f;
        cardExtraJumps = 0;
        cardT = null;
        ShieldCharges = 0; shieldCapacityApplied = 0; shieldRechargeTimer = 0f;
        overdriveActive = false; overdriveCharge = 0f; overdriveExitTimer = 0f;
        airDominionStacks = 0; skyMasterRefreshUsed = 0; brakeTimer = 0f;
    }

    static float Ratio(float target, float applied) => applied > 1e-6f ? target / applied : 1f;

    public void ApplyCardTotals(CardTotals t)
    {
        cardT = t;
        float sp = 1f + CardRules.SoftSpeed(t.Get(EffectType.SpeedPct));
        runSpeed *= Ratio(sp, cardSpeedFactor); cardSpeedFactor = sp;
        float jf = 1f + CardRules.SoftJump(t.Get(EffectType.JumpPct));
        jumpForce *= Ratio(jf, cardJumpFactor); cardJumpFactor = jf;
        float dur = 1f / (1f + CardRules.SoftAttackSpeed(t.Get(EffectType.AttackSpeedPct)));
        attackSpeedStored *= Ratio(dur, cardDurationFactor); cardDurationFactor = dur;
        float rf = 1f + CardRules.SoftRange(t.Get(EffectType.RangePct));
        AttackRangeMultiplier = Mathf.Max(0.1f, AttackRangeMultiplier * Ratio(rf, cardRangeFactor)); cardRangeFactor = rf;
        int j = Mathf.Max(0, t.Int(EffectType.JumpCount));
        maxJumps = Mathf.Max(1, maxJumps + j - cardExtraJumps); cardExtraJumps = j;
        UpdateShieldCapacity();
    }

    float CT(EffectType e) => cardT != null ? cardT.Get(e) : 0f;
    public float CardValue(EffectType e) => CT(e);

    // ===================================================================== 攻撃力

    public float CardAttackFactor => 1f + CardRules.SoftAttack(CT(EffectType.AttackPct));

    // 今の状態の条件(空中/地上/HP/速度/OVERDRIVE/連続ヒット/封印)
    public float CardStateCondition()
    {
        if (cardT == null) return 0f;
        float c = 0f;
        if (!isGrounded) c += CT(EffectType.AirPct) + airDominionStacks * CT(EffectType.AirDominionPct);
        else c += CT(EffectType.GroundPct);
        var gm = GameManager.Instance;
        if (gm != null && gm.maxLives > 0)
        {
            float f = (float)gm.Lives / gm.maxLives;
            if (gm.Lives >= gm.maxLives) c += CT(EffectType.FullHpPct);
            if (f <= 0.5f) c += CT(EffectType.LowHp50Pct);
            if (f <= 0.25f) c += CT(EffectType.LowHp25Pct);
            c += gm.SealedHearts * CT(EffectType.SealedHeartPct);
        }
        float mom = CT(EffectType.MomentumPct);
        if (mom != 0f) c += mom * MomentumFactor(CurrentRunKmh);
        if (overdriveActive) c += CT(EffectType.OverdrivePct);
        float edge = CT(EffectType.ComboEdgePct);
        if (edge != 0f && ComboCounterUI.Instance != null) c += edge * Mathf.Min(CardRules.ComboEdgeMaxSteps, ComboCounterUI.Instance.ComboCount);
        return c;
    }

    // 100km/h 以下 = 0、150km/h で 1、その先は伸びを大きく鈍らせる
    public static float MomentumFactor(float kmh)
    {
        if (kmh <= CardRules.MomentumFromKmh) return 0f;
        float span = Mathf.Max(1f, CardRules.MomentumFullKmh - CardRules.MomentumFromKmh);
        if (kmh <= CardRules.MomentumFullKmh) return (kmh - CardRules.MomentumFromKmh) / span;
        return 1f + (kmh - CardRules.MomentumFullKmh) / span * CardRules.MomentumBeyondSlope;
    }

    // 命中した相手/技による条件(初撃/締めは PlayerAttackInfo.ScaleDamage が1押しにつき1回だけ足す)
    public float CardTargetCondition(PlayerAttackInfo info, Component victim)
    {
        if (cardT == null) return 0f;
        float c = 0f;
        bool boss = victim is WildBossBase || victim is DragonController || victim is MajinController || victim is BossHurtbox;
        c += boss ? CT(EffectType.BossPct) : CT(EffectType.MobPct);
        bool airTarget = victim is DragonController || victim is MajinController;
        if (victim is EnemyController en) airTarget = en.AerialState != EnemyController.EnemyAerialState.Grounded || en.movementType == EnemyMovementType.Flying;
        if (airTarget) c += CT(EffectType.AntiAirPct);
        if (info != null && (info.kind == PlayerAttackKind.Down || info.kind == PlayerAttackKind.DownImpact)) c += CT(EffectType.DownPct);
        float az = CT(EffectType.AbsoluteZeroLevel);
        if (az > 0f && victim != null)
        {
            var st = victim.GetComponent<ElementStatus>();
            if (st != null && (st.Chilled || st.Frozen)) c += 0.03f * az;
        }
        return c;
    }

    public float SeqPct(AttackSeqTag tag)
    {
        switch (tag)
        {
            case AttackSeqTag.First: return CT(EffectType.FirstPct);
            case AttackSeqTag.Combo: return CT(EffectType.ComboPct);
            case AttackSeqTag.Finisher: return CT(EffectType.FinisherPct);
        }
        return 0f;
    }

    // ===================================================================== 攻撃速度

    // HP/OVERDRIVE で変わる分(攻撃時間の倍率。小さいほど速い)。曲線は固定の分と合わせて一度だけ通す
    float CardDynamicDurationFactor
    {
        get
        {
            if (cardT == null) return 1f;
            float dyn = 0f;
            var gm = GameManager.Instance;
            float low = CT(EffectType.LowHpAttackSpeedPct);
            if (low != 0f && gm != null && gm.maxLives > 0)
            {
                float f = (float)gm.Lives / gm.maxLives;
                if (f <= 0.5f) dyn += low;
                if (f <= 0.25f) dyn += low;
            }
            if (overdriveActive) dyn += CT(EffectType.OverdriveAttackSpeedPct);
            if (dyn == 0f) return 1f;
            float st = CT(EffectType.AttackSpeedPct);
            return (1f + CardRules.SoftAttackSpeed(st)) / (1f + CardRules.SoftAttackSpeed(st + dyn));
        }
    }

    // 主攻撃(前/後)だけの攻撃速度(RAPID EDGE / COMBO RUSH)。上/下/空中の特殊技/叩きつけには掛けない
    float CardMainDurationFactor
    {
        get
        {
            float m = CT(EffectType.MainAttackSpeedPct);
            if (m == 0f) return 1f;
            float st = CT(EffectType.AttackSpeedPct);
            return (1f + CardRules.SoftAttackSpeed(st)) / (1f + CardRules.SoftAttackSpeed(st + m));
        }
    }
    public float MainAttackSpeedMultiplier => AttackSpeedMultiplier * CardMainDurationFactor;

    // 単発キャラの攻撃シーケンスの窓(攻撃の速さに合わせて伸び縮み。速くても SequenceResetMinScale より短くしない)
    float charBaseAttackSpeed = 1f;
    public float SequenceResetWindow => CardRules.SingleAttackSequenceReset * Mathf.Max(CardRules.SequenceResetMinScale, MainAttackSpeedMultiplier / Mathf.Max(0.05f, charBaseAttackSpeed));

    // ===================================================================== Shield

    int shieldCapacityApplied;
    float shieldRechargeTimer;
    public int ShieldCapacity
    {
        get
        {
            int cap = Mathf.Clamp(Mathf.RoundToInt(CT(EffectType.ShieldCapacity)), 0, 3);
            if (cap == 0 && CT(EffectType.ShieldGuard) > 0f) cap = 1;
            return cap;
        }
    }
    public float ShieldRechargeSeconds => Mathf.Max(CardRules.ShieldRechargeMinSeconds, CardRules.ShieldRechargeBaseSeconds - CT(EffectType.ShieldRecharge));
    public float ShieldRechargeProgress => ShieldCharges < ShieldCapacity ? Mathf.Clamp01(shieldRechargeTimer / ShieldRechargeSeconds) : 1f;

    void UpdateShieldCapacity()
    {
        int cap = ShieldCapacity;
        if (cap > shieldCapacityApplied) ShieldCharges += cap - shieldCapacityApplied; // 増えた分は満タンで
        shieldCapacityApplied = cap;
    }

    // OVERHEAL: 満タンで溢れた回復を Shield 1つへ(最大数 +1 まで)
    public bool AddOverhealShield()
    {
        if (ShieldCharges >= ShieldCapacity + 1) return false;
        ShieldCharges++;
        return true;
    }

    // ===================================================================== 状態(毎フレーム)

    bool overdriveActive;
    float overdriveCharge, overdriveExitTimer;
    public bool OverdriveActive => overdriveActive;
    public float OverdriveCharge01 => Mathf.Clamp01(overdriveCharge / Mathf.Max(0.1f, OverdriveActivateSeconds));
    float OverdriveActivateSeconds => Mathf.Max(0.5f, CardRules.OverdriveBaseActivateSeconds - CardRules.OverdriveActivatePerLevel * (CT(EffectType.OverdriveLevel) - 1f));
    public static int OverdriveActivations;

    int airDominionStacks, skyMasterRefreshUsed;
    float airDominionLastHit = -9f;
    public int AirDominionStacks => airDominionStacks;
    float brakeTimer;
    float cardLastGroundY;
    bool cardWasGrounded;

    void CardTick(float dt)
    {
        if (cardT == null) return;
        // Shield の回復
        int cap = ShieldCapacity;
        if (ShieldCharges < cap)
        {
            shieldRechargeTimer += dt;
            if (shieldRechargeTimer >= ShieldRechargeSeconds) { shieldRechargeTimer = 0f; ShieldCharges++; }
        }
        else shieldRechargeTimer = 0f;

        // OVERDRIVE: 一定以上の速さを続けると発動、遅くなると解除
        if (CT(EffectType.OverdriveLevel) > 0f)
        {
            float kmh = CurrentRunKmh;
            if (!overdriveActive)
            {
                if (kmh >= CardRules.OverdriveThresholdKmh && !IsReacting) overdriveCharge += dt; else overdriveCharge = Mathf.Max(0f, overdriveCharge - dt * 2f);
                if (overdriveCharge >= OverdriveActivateSeconds) { overdriveActive = true; overdriveExitTimer = 0f; OverdriveActivations++; CardProcs.OverdriveStart(this); }
            }
            else
            {
                if (kmh < CardRules.OverdriveExitKmh) overdriveExitTimer += dt; else overdriveExitTimer = 0f;
                if (overdriveExitTimer >= CardRules.OverdriveExitGraceSeconds) { overdriveActive = false; overdriveCharge = 0f; }
            }
        }
        else { overdriveActive = false; overdriveCharge = 0f; }

        if (brakeTimer > 0f) brakeTimer -= dt;
        if (isGrounded)
        {
            cardLastGroundY = transform.position.y;
            if (!cardWasGrounded) { airDominionStacks = 0; skyMasterRefreshUsed = 0; }
        }
        cardWasGrounded = isGrounded;
    }

    // 主攻撃を出した瞬間(SetSeqTag で First/Combo/Finisher が付いた時 = 全キャラ共通の「主攻撃の1押し」)
    void OnMainAttackPress(AttackSeqTag tag)
    {
        if (cardT == null) return;
        if (CT(EffectType.BrakeLevel) > 0f) brakeTimer = 0.4f;
        float sonic = CT(EffectType.SonicBladeLevel);
        if (sonic > 0f) CardProcs.TrySonicSlash(this, Mathf.RoundToInt(sonic));
    }

    // 攻撃が敵/ボスに命中した(PlayerAttackInfo.ScaleDamage → NotifyAttackLanded)
    void OnCardAttackLanded(Component target, PlayerAttackInfo attack)
    {
        if (cardT == null || target == null || isGrounded) return;
        if (attack != null && attack.elementProc) return;
        // AIR DOMINION: 着地するまでの空中での命中を数える
        if (CT(EffectType.AirDominionPct) > 0f && Time.time - airDominionLastHit > 0.15f)
        {
            airDominionLastHit = Time.time;
            airDominionStacks = Mathf.Min(CardRules.AirDominionMaxStacks, airDominionStacks + 1);
        }
        // SKY MASTER: 空中で当てると空中ジャンプを1回取り戻す(1回の滞空で ceil(Lv/3) 回まで)
        int sky = Mathf.RoundToInt(CT(EffectType.SkyMasterLevel));
        if (sky > 0 && jumpsUsed > 1 && skyMasterRefreshUsed < Mathf.CeilToInt(sky / 3f))
        {
            jumpsUsed--;
            skyMasterRefreshUsed++;
            SkyMasterRefreshes++;
        }
    }
    public static int SkyMasterRefreshes;

    // BRAKE ATTACK: 主攻撃の直後だけ少し減速
    float CardAutoSpeedScale => brakeTimer > 0f ? 0.9f : 1f;

    // HUNTER: ボス戦の前進/後退の伸び、遠いボスへ寄る速さ
    float CardHunterLungeScale => CT(EffectType.HunterLevel) > 0f && BossBattle.AnyBossFighting ? 1f + 0.04f * CT(EffectType.HunterLevel) : 1f;
    float CardHunterApproachScale(float playerX)
    {
        float h = CT(EffectType.HunterLevel);
        if (h <= 0f || !BossBattle.AnyBossFighting) return 1f;
        float d = BossBattle.NearestBossFrontAheadPublic(playerX);
        return d != float.MaxValue && d > 6f ? 1f + 0.03f * h : 1f;
    }

    // CLOSE CALL / FORTRESS
    float CardHurtInvincibleBonus => Mathf.Max(0f, CT(EffectType.HurtInvincibleSeconds));
    float CardHurtKnockbackScale => Mathf.Clamp(1f - CT(EffectType.HurtKnockbackReduce), 0.3f, 1f);

    // JOB COUNT UP の減衰: キャラ本来の空中ジャンプ+無料分を超えた空中ジャンプほど弱く。足場より下へ落ちている時は減衰しない
    float CardJumpImpulseScale(int jumpIndex)
    {
        int free = baseJumpCount + CardRules.JumpDecayFreeJumps;
        if (jumpIndex <= free) return 1f;
        if (IsBelowRecoveryLine()) return 1f;
        return Mathf.Max(CardRules.JumpDecayMin, 1f - CardRules.JumpDecayPerJump * (jumpIndex - free));
    }

    bool IsBelowRecoveryLine()
    {
        // 足元に地面があればその高さ、無ければ(穴の上)最後に立っていた高さ
        float refY = cardLastGroundY;
        var tm = TerrainManager.Instance;
        if (tm != null)
        {
            float? g = tm.GetHeightAt(transform.position.x);
            if (g.HasValue) refY = g.Value + groundOffset;
        }
        return transform.position.y < refY - CardRules.JumpRecoveryBelow;
    }
    public static int JumpDecayed;

    // 魔法使い(浮遊): ジャンプ力 → 高度の上下の速さ、ジャンプ回数 → 上がれる段の数(3Lvごとに+1)
    public int MageExtraAltitudeLevels => cardT != null ? Mathf.CeilToInt(Mathf.Max(0, cardT.Int(EffectType.JumpCount)) / 3f) : 0;
    public float MageAltitudeSpeedScale => 1f + 2f * Mathf.Max(0f, cardJumpFactor - 1f);

    // 確認用
    public float CardSpeedFactor => cardSpeedFactor;
    public float CardJumpFactor => cardJumpFactor;
    public float CardDurationFactor => cardDurationFactor;
    public float CardRangeFactor => cardRangeFactor;
    // FINAL EVOLUTION(ATTACK RANGE UP): 射程へ一時的な倍率(前の倍率との比で。終われば戻す)
    public void ApplyFinalEvolutionRange(float ratio) { AttackRangeMultiplier = Mathf.Max(0.1f, AttackRangeMultiplier * ratio); }
}

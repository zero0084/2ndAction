using UnityEngine;

// ===== 高速時の自動操作補助(2026-09-28 試作) - PlayerController側の窓口 ===== //
// HighSpeedAssist(判断役)が、ジャンプ軌道の予測や攻撃の間合いの見積もりに使う値を読むための
// 読み取り専用プロパティと、Update()の入力処理へ自動入力を差し込む1か所(ApplyHighSpeedAssist)だけを持つ。
// 自動入力は手動のフリックと同じ requestedFlick に入るので、攻撃/ジャンプの処理・威力・射程・
// クールダウン・キャンセル条件・ネットワーク同期はすべて既存のまま(自動専用の処理は無い)。
public partial class PlayerController
{
    // ---- 軌道予測用(既存の物理値そのもの) ----
    public float AssistJumpForce => jumpForce;
    public float AssistGravity => gravity;
    // 空中攻撃が当たった直後の滞空補助(重力が弱まる残り時間と倍率)
    public float AssistAerialAssistRemaining => isGrounded ? 0f : Mathf.Min(aerialAssistTimer, Mathf.Max(0f, aerialAssistMaxTotalDuration - aerialAssistTotalUsed));
    public float AssistAerialGravityScale => aerialAssistGravityScale;
    public float AssistAerialFallResetSpeed => aerialAssistFallResetSpeed;
    public float AssistAerialWindow => aerialAssistTotalUsed >= aerialAssistMaxTotalDuration ? 0f : Mathf.Min(aerialAssistWindowDuration, aerialAssistMaxTotalDuration - aerialAssistTotalUsed);
    public float AssistVelocityY => velocityY;
    public int AssistJumpsUsed => jumpsUsed;
    public int AssistMaxJumps => maxJumps;
    public bool AssistOnSky => onSky;
    public float AssistFailY => failY;
    // 魔法使いは地面から浮いている(最低高度+段数)。軌道予測の「地面を走る高さ」に含める(2026-09-29)。
    public float AssistGroundOffset => kit == CharacterKit.Mage && kitDef != null ? groundOffset + kitDef.mage.hoverBase + mageLevel * kitDef.mage.altitudeStep : groundOffset;
    // 前攻撃の弾が出る高さ(足元から)。弾は水平に飛ぶので、障害物の高さと合うかの判定に使う。
    public float AssistForwardMuzzleY
    {
        get
        {
            switch (kit)
            {
                case CharacterKit.Archer: return kitDef.archer.muzzle.y;
                case CharacterKit.Mage: return MageStaff.y;
                case CharacterKit.Miko: return 0.8f;
            }
            return isRangedCharacter ? rangedMuzzleOffset.y : 0.5f;
        }
    }
    public bool AssistIsMageFlight => kit == CharacterKit.Mage;
    public bool AssistIsDiveOrHover => isDiveAttacking || isHoverShooting || (isLancerCharacter && lanceDiving) || kitVerticalVelocity.HasValue;
    public bool AssistEscapeCharging => IsEscapeCharging;

    // 体の当たり判定(ワールド、足元基準)。半幅と高さ。
    public Vector2 AssistBodySize
    {
        get
        {
            var col = bodyCol != null ? bodyCol : GetComponent<BoxCollider2D>();
            if (col == null) return new Vector2(0.6f, 1.6f);
            Vector3 s = transform.lossyScale;
            return new Vector2(Mathf.Abs(col.size.x * s.x), Mathf.Abs(col.size.y * s.y));
        }
    }

    // 軌道予測に使う前進速度 = Move()の自動前進の式(減速効果込み)。攻撃の踏み込み/ノックバックは一瞬で終わるので
    // 含めない(含めると飛行中ずっと続くものとして予測され、着地点が数メートル先にずれる)。
    public float AssistHorizontalSpeed
    {
        get
        {
            float v = autoRunEnabled ? runSpeed * EffectiveSpeedMultiplier() * DebugRunOnlyScale : 0f;
            if (moveSlowTimer > 0f) v *= moveSlowFactor;
            if (groundHitConnectSlowdownTimer > 0f) v *= 1f - groundHitConnectSlowdownFactor;
            if (IsReacting) v = 0f;
            if (isLancerCharacter) v *= lanceMoveSlowFactor;
            if (HasKit) v *= kitMoveSlowFactor * VampireRunBoost;
            return v;
        }
    }

    // ---- 前攻撃の見積もり(既存のキャラ別パラメータから読むだけ。値は変えない) ----
    // 入力から判定が出るまでの時間(ゲーム内秒)。
    public float AssistForwardStartup
    {
        get
        {
            float s = AttackSpeedMultiplier;
            switch (kit)
            {
                case CharacterKit.Archer: return kitDef.archer.forwardWindup * s;
                case CharacterKit.Mage: return kitDef.mage.boltCastTime * s;
                case CharacterKit.Fighter: return ArcherPick(kitDef.fighter.comboWindup, 0, 0.05f) * s;
                case CharacterKit.Vampire: return ArcherPick(kitDef.vampire.comboWindup, 0, 0.05f) * s;
                case CharacterKit.Dragonkin: return ArcherPick(kitDef.dragonkin.comboWindup, 0, 0.1f) * s;
                case CharacterKit.Miko: return kitDef.miko.ofudaWindup * s;
                case CharacterKit.Ninja: return 0f;
            }
            if (isLancerCharacter && lancerDef != null) return Pick(lancerDef.lanceComboWindup, 1, lancerDef.lanceWindup) * s;
            return 0f;
        }
    }

    // 飛び道具で攻撃するキャラか(間合いの考え方が近接と違う)。
    public bool AssistForwardIsProjectile =>
        isRangedCharacter || kit == CharacterKit.Archer || kit == CharacterKit.Mage || kit == CharacterKit.Miko;

    // 前攻撃が届く前方の距離(体の中心から、ワールド)。飛び道具は「狙い始める距離」。
    public float AssistForwardReach
    {
        get
        {
            if (AssistForwardIsProjectile) return 12f;
            switch (kit)
            {
                case CharacterKit.Fighter: return ArcherPick(kitDef.fighter.comboReach, 0, 1f) + 0.3f;
                case CharacterKit.Vampire: return ArcherPick(kitDef.vampire.comboReach, 0, 1.1f) + 0.3f;
                case CharacterKit.Dragonkin: return ArcherPick(kitDef.dragonkin.comboReach, 0, 1.3f) + 0.3f;
                case CharacterKit.Ninja: return kitDef.ninja.dashDistance + 0.8f;
            }
            if (isLancerCharacter && lancerDef != null) return lancerDef.lanceReachStart + lancerDef.lanceReach * Pick(lancerDef.lanceComboReachScale, 1, 1f);
            if (attackHitbox is BoxCollider2D box)
            {
                Vector3 sc = hitboxBaseScale * AttackRangeMultiplier;
                return hitboxBaseLocalPos.x + (box.offset.x + box.size.x * 0.5f) * sc.x;
            }
            return 1.6f;
        }
    }

    // 障害物の破壊の見積もり(2026-09-29): 飛び道具の弾速(走行速度の上乗せ前)と、攻撃力。
    public float AssistForwardProjectileSpeed
    {
        get
        {
            switch (kit)
            {
                case CharacterKit.Archer: return kitDef.archer.arrowSpeed;
                case CharacterKit.Mage: return kitDef.mage.boltSpeed;
                case CharacterKit.Miko: return kitDef.miko.ofudaSpeed;
            }
            return isRangedCharacter ? rangedBulletSpeed : 0f;
        }
    }
    public int AssistAttackPower => EffectiveAttackPower;
    // 前攻撃1回(1段目)の威力倍率(キャラ別の既存値を読むだけ)
    public float AssistForwardDamageScale
    {
        get
        {
            switch (kit)
            {
                case CharacterKit.Mage: return kitDef.mage.boltDamageScale;
                case CharacterKit.Fighter: return ArcherPick(kitDef.fighter.comboDamageScale, 0, 1f);
                case CharacterKit.Vampire: return ArcherPick(kitDef.vampire.comboDamageScale, 0, 1f);
                case CharacterKit.Dragonkin: return ArcherPick(kitDef.dragonkin.comboDamageScale, 0, 1f);
                case CharacterKit.Ninja: return kitDef.ninja.slashDamageScale;
                case CharacterKit.Miko: return kitDef.miko.ofudaDamageScale;
            }
            return 1f;
        }
    }
    // 前攻撃で体ごと前へ跳び込む距離(忍者の瞬身)。壊し切れない障害物へ跳び込まないために使う。
    public float AssistForwardLunge => kit == CharacterKit.Ninja ? kitDef.ninja.dashDistance : 0f;
    // 次の前攻撃を始められるまでの秒数(攻撃中は-1=不明)
    public float AssistAttackReadyIn => isAttacking ? -1f : Mathf.Max(0f, attackCooldownTimer);

    // 今この瞬間に前攻撃を新しく始められるか(連打・予約にならないよう、実行中/硬直中は出さない)。
    public bool AssistCanStartForwardAttack => !isAttacking && attackCooldownTimer <= 0f && !IsReacting;

    // ---- 入力への差し込み(Update()から1回だけ呼ぶ) ----
    // 手動入力(タッチ/テスト用の注入)があればそれを優先し、補助へ「手動操作があった」と知らせるだけ。
    // 無ければ補助の判断を1つだけ requestedFlick に入れる。予約はしない(毎フレームその場で判断する)。
    public bool LastFlickWasAssist { get; private set; }
    public static int AssistJumpCount, AssistAttackCount;

    void ApplyHighSpeedAssist()
    {
        LastFlickWasAssist = false;
        HighSpeedAssist assist = HighSpeedAssist.Instance;
        if (assist == null) return;
        if (requestedFlick.HasValue)
        {
            assist.NotifyManualInput(requestedFlick.Value);
            return;
        }
        FlickDirection? auto = assist.Decide(this);
        if (auto.HasValue)
        {
            requestedFlick = auto;
            LastFlickWasAssist = true;
            if (auto.Value == FlickDirection.Up) AssistJumpCount++; else AssistAttackCount++; // 自動テスト用の数え(2026-10-08)
            RunLedger.MarkAuto(); // 2026-10-08: オート(補助)を使ったラン(ランキングに記録する)
        }
    }
}

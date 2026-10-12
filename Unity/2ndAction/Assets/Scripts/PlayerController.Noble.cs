using System.Collections;
using UnityEngine;

// お嬢様騎士の強化の候補(2026-10-11、攻略の再検証の重点調査)。BalanceTuning の切り替えで比べてから採用する。
//  ・覚醒(NobleAwaken): 「伝説がまだ鎧に追いついていない英雄」。走った距離で攻撃力と大剣の間合いが伸びる(序盤は今までどおり弱い・短い)
//  ・鎧(NobleArmor): 振っている間の被弾では攻撃が止まらない(押し戻されない)。受けるダメージも少し減る
//  ・大剣(NobleHeavy): 一撃の重さ(比較用。距離に関係なく攻撃力を上げる)
//  ・間合い(NobleReach): 大剣の届く距離(比較用)
public partial class PlayerController
{
    public string CharId { get; private set; }
    bool IsNoble => CharId == "noble_lady";

    // EffectiveAttackPower の最後に掛ける
    float NobleAttackMul
    {
        get
        {
            if (!IsNoble) return 1f;
            float m = 1f;
            if (BalanceTuning.NobleHeavy) m *= BalanceTuning.NobleHeavyMul;
            if (BalanceTuning.NobleGreatsword) m *= BalanceTuning.NobleGreatswordDamage; // 2026-10-12: 大剣の一撃の重さ
            if (BalanceTuning.NobleAwaken) m *= 1f + BalanceTuning.NobleAwakenMax * NobleAwakenK;
            return m;
        }
    }

    // 覚醒の進み具合 0〜1(雑魚の HP と同じ距離 = GameManager.MaxDistance)
    float NobleAwakenK
    {
        get
        {
            float d = GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;
            return Mathf.InverseLerp(BalanceTuning.NobleAwakenFrom, BalanceTuning.NobleAwakenTo, d);
        }
    }

    // 前攻撃の間合い(当たり判定の大きさ)に掛ける。覚醒で 0.75 → NobleAwakenRange(基礎の間合いに対する倍率)
    public float NobleRangeMul => IsNoble && BalanceTuning.NobleAwaken ? Mathf.Lerp(1f, BalanceTuning.NobleAwakenRange, NobleAwakenK) : 1f;

    // ApplyCharacterBaseStats の基礎の間合い
    float NobleBaseRange(float r) => CharIdIsNoble && BalanceTuning.NobleReach ? BalanceTuning.NobleReachValue : CharIdIsNoble && BalanceTuning.NobleGreatsword ? BalanceTuning.NobleGreatswordRange : r;
    bool CharIdIsNoble => CharId == "noble_lady";

    // 受けるダメージ(ハートの量)
    int NobleDamage(int amount) => IsNoble && BalanceTuning.NobleArmor && amount > 0
        ? Mathf.Max(1, Mathf.RoundToInt(amount * BalanceTuning.NobleArmorDamage)) : amount;

    // 振っている間の被弾: 攻撃を止めず、押し戻さず、無敵の点滅だけ(落下は対象外)
    bool NobleArmorHit(bool isFall)
    {
        if (isFall || !IsNoble || !BalanceTuning.NobleArmor || !isAttacking) return false;
        NobleArmorHits++;
        float inv = charHurtInvincible > 0f ? charHurtInvincible : hurtInvincibleDuration;
        hitInvincibleTimer = Mathf.Max(hitInvincibleTimer, inv + CardHurtInvincibleBonus);
        StartCoroutine(FlickerWhileInvincible());
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerDamage();
        Platform.Haptics.Play(HapticKind.Hit);
        if (damageFlashEnabled && sr != null) StartCoroutine(DamageFlashRoutine());
        return true;
    }
    public static int NobleArmorHits { get; private set; } // 確認用: 鎧で攻撃が止まらなかった回数

    // ===================================================================== //
    // 空中の薙ぎ払い / 振り下ろし急降下(2026-10-12、BalanceTuning.NobleMoves)
    //  操作は全キャラ共通: 上フリック=ジャンプ(お嬢様は上攻撃なし)、空中の前/後フリック=空中攻撃、空中の下フリック=急降下。専用の入力は無い
    //  ・薙ぎ払い: 大剣を頭上から前方〜下方へ大きく振る。判定は前方 0.2〜1.7m × 上下 -1.2〜+1.2m(地上の攻撃より下へ広い)。
    //    威力は地上の一振りと同じ(1.0倍)。構え 0.07秒・判定 0.20秒・硬直 0.12秒(攻撃速度の倍率が掛かる)。落下の速さは変えない
    //  ・急降下: 大剣を真下へ向けて落下(落下速度 16m/秒、黒剣士と同じ)。落下中の当たり 1.2倍。着地で衝撃波 0.8倍(左右 1.6m)。
    //    同じ敵に両方当たると合計 2.0倍(1回の急降下で地上の一振り2回分)。無敵は無い(被弾・穴・天井は通常どおり)。
    //    着地の構え 0.22秒(この間も入力を受け付ける)
    //  間合いは攻撃範囲のカード(AttackRangeMultiplier)と覚醒(NobleRangeMul)で伸びる。鎧(振っている間の被弾で止まらない)は薙ぎ払いにも効く
    // ===================================================================== //
    public static int NobleAirSwings, NobleAirHits, NobleDives, NobleDiveHits, NobleShockHits; // 確認用
    float noblePoseUntil = -1f;
    const float NobleDiveDamage = 1.2f, NobleShockDamage = 0.8f;

    bool NobleUsesAirSweep => IsNoble && BalanceTuning.NobleMoves && !isGrounded;

    IEnumerator NobleAirSweep(AttackDirection dir)
    {
        AttackStarted?.Invoke(dir);
        int gen = attackGeneration;
        isAttacking = true;
        comboWindowOpen = false; comboBuffered = false; comboCount = 1;
        SetSeqTag(ChainTag(1, maxComboChain));
        NobleAirSwings++;
        float sp = AttackSpeedMultiplier;
        float windup = 0.07f * sp, active = 0.20f * sp, recovery = 0.12f * sp;
        attackCooldownTimer = windup + active + recovery;
        ApplyAttackDirection(dir);
        // 踏み込みは地上の一振りと同じ(以前の空中の攻撃と同じだけ前へ進む。減らすと穴を越える距離が縮む)
        SetKitPose("air", 0); noblePoseUntil = float.MaxValue;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.AttackAir);
        float t = 0f;
        while (t < windup) { if (gen != attackGeneration) yield break; t += Time.deltaTime; yield return null; }
        SetKitPose("air", 1);
        float R = AttackRangeMultiplier * NobleRangeMul;
        if (attackHitbox != null)
        {
            attackHitbox.transform.localPosition = new Vector3(0.2f + 0.75f * R, 0f, 0f);
            attackHitbox.transform.localScale = new Vector3(1.5f * R, 2.4f * R, 1f);
            var ai = attackHitbox.GetComponent<PlayerAttackInfo>();
            if (ai != null) { ai.Rearm(); ai.onHit = () => NobleAirHits++; }
            TagHitbox(attackHitbox);
            attackHitbox.enabled = true;
        }
        if (attackSlashVisual != null)
        {
            attackSlashVisual.transform.localPosition = new Vector3(0.2f + 0.7f * R, -0.1f, 0f);
            attackSlashVisual.transform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            attackSlashVisual.PlayFrames(1.35f, R);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
        t = 0f;
        while (t < active)
        {
            if (gen != attackGeneration) yield break;
            if (t >= active * 0.5f && KitPoseFrame != 2) SetKitPose("air", 2);
            t += Time.deltaTime; yield return null;
        }
        NobleEndAirSweep();
        t = 0f;
        while (t < recovery) { if (gen != attackGeneration) yield break; t += Time.deltaTime; yield return null; }
        isAttacking = false;
        SetSeqTag(AttackSeqTag.None);
        noblePoseUntil = Time.time + 0.02f;
        transform.localScale = Vector3.one;
    }

    void NobleEndAirSweep()
    {
        if (attackHitbox != null)
        {
            attackHitbox.enabled = false;
            var ai = attackHitbox.GetComponent<PlayerAttackInfo>();
            if (ai != null) ai.onHit = null;
            ApplyComboStageToHitbox(1); // 地上の攻撃の位置/大きさへ戻す
        }
        lungeVelocityX = 0f;
    }

    // DoDiveAttack の最後(急降下の当たり: 間合いの倍率・威力)
    void NobleOnDiveStart()
    {
        if (!IsNoble || !BalanceTuning.NobleMoves) return;
        NobleDives++;
        float R = AttackRangeMultiplier * NobleRangeMul;
        if (downAttackHitbox != null)
        {
            downAttackHitbox.transform.localScale = downHitboxBaseScale * R;
            downAttackHitbox.transform.localPosition = downHitboxBaseLocalPos * R;
            var ai = downAttackHitbox.GetComponent<PlayerAttackInfo>();
            if (ai != null) { ai.damageScale = NobleDiveDamage; ai.onHit = () => NobleDiveHits++; }
        }
        SetKitPose("plunge", 0); noblePoseUntil = float.MaxValue;
    }

    // TriggerDiveImpact の中(着地の衝撃波)。true なら共通の処理の代わり
    bool NobleOnDiveImpact()
    {
        if (!IsNoble || !BalanceTuning.NobleMoves) return false;
        float R = AttackRangeMultiplier * NobleRangeMul;
        if (downAttackLandHitbox != null)
        {
            downAttackLandHitbox.transform.localScale = new Vector3(3.2f * R, 1.1f, 1f); // 左右 1.6m × R、高さ 1.1m
            var ai = downAttackLandHitbox.GetComponent<PlayerAttackInfo>();
            if (ai != null) { ai.damageScale = NobleShockDamage; ai.onHit = () => NobleShockHits++; }
            downAttackLandHitbox.enabled = true; PlayerAttackInfo.RearmOf(downAttackLandHitbox);
            StartCoroutine(DisableDiveImpactHitboxAfterDelay());
        }
        if (downAttackLandSlashVisual != null) downAttackLandSlashVisual.PlaySingle(1.15f, R);
        // 大剣が地面に刺さる衝撃: 金色の輪+土煙+かけら
        Vector3 foot = transform.position + new Vector3(0.35f * (transform.localScale.x < 0f ? -1f : 1f), 0.08f, 0f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), foot + new Vector3(0f, 0.2f, 0f), new Color(1f, 0.85f, 0.45f, 0.9f), duration: 0.28f, startScale: 0.4f, endScale: 1.9f * R, sortingOrder: RenderOrder.SlashFx);
        OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.RockChunk(), foot, new Color(0.85f, 0.78f, 0.65f, 1f), 7, 0.4f, 0.08f, 0.2f, 4f, 2.5f, RenderOrder.CombatFx);
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null) cam.Shake(0.07f, 0.12f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.SlamImpact);
        SetKitPose("plunge", 1); noblePoseUntil = Time.time + 0.22f;
        StartCoroutine(NobleRisePose());
        return true;
    }

    IEnumerator NobleRisePose()
    {
        yield return new WaitForSeconds(0.11f);
        if (KitPoseName == "plunge" && KitPoseFrame == 1) SetKitPose("plunge", 2);
    }

    // 毎フレーム(Update から): 技のポーズの片付け(キットの無いお嬢様用。キャラのキットは KitUpdate が持つ)
    void NobleUpdate()
    {
        if (!IsNoble || HasKit) return;
        if (KitPoseFrames != null)
        {
            // 技が続いている間だけポーズを持つ。技が別の経路(穴/復帰/演出)で終わった時は「終わるまで」の印(MaxValue)でも片付ける
            bool live = (KitPoseName == "air" && isAttacking) || (KitPoseName == "plunge" && isDiveAttacking);
            if (!live && (Time.time >= noblePoseUntil || noblePoseUntil == float.MaxValue))
            {
                if (KitPoseName == "air" && attackHitbox != null && attackHitbox.enabled) NobleEndAirSweep();
                KitPoseFrames = null; KitPoseName = null; KitPoseFrame = 0; noblePoseUntil = -1f;
            }
        }
        // 急降下が着地以外で終わった(被弾/穴/復帰)時: 当たりの設定を戻す
        if (!isDiveAttacking && downAttackHitbox != null && !downAttackHitbox.enabled)
        {
            var ai = downAttackHitbox.GetComponent<PlayerAttackInfo>();
            if (ai != null && ai.onHit != null) { ai.onHit = null; ai.damageScale = 1f; }
        }
    }

    // 被弾/死亡/復帰で技が打ち切られた時
    void NobleCancel()
    {
        if (!IsNoble) return;
        if (KitPoseName == "air") NobleEndAirSweep();
        noblePoseUntil = -1f;
        KitPoseFrames = null; KitPoseName = null; KitPoseFrame = 0;
    }
}

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
    float NobleBaseRange(float r) => IsNoble && BalanceTuning.NobleReach ? BalanceTuning.NobleReachValue : r;

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
}

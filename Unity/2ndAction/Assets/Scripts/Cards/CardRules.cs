using UnityEngine;

// カードバランス v3(2026-10-03)の共通の規則と調整値。
//  ・カードの値はLvから毎回計算する(Scale)。取得のたびに整数へ丸めて足し込むことはしない。
//  ・同じ系統の値は1つの枠へ足してから、枠ごとに一度だけ曲線を通す(Soft*)。曲線は「上限」ではなく、
//    大きくなるほど伸びが鈍る折れ線(knee1 まではそのまま、knee2 までは slope2、その先は slope3)。
//    カードごとに倍率を掛け合わせないので、取る順番で結果は変わらない。
//  ・目安(仕様): 攻撃は 普通のRun ×1.2〜2 / ある程度 ×2〜3 / 特化 ×3〜4 / 極端 ×4〜6 / 条件が揃った一撃 ×6〜10。
//    速度は 自然上限100km/h から SPEED UP Lv9 で約127、速度特化で140〜150、極端で150〜160前後。
// 値はすべて public static で、開発版のテストや今後の調整で変えられる(保存はしない)。
public static class CardRules
{
    // ---- 曲線(raw → effective)
    public static float AttackKnee1 = 0.8f, AttackKnee2 = 2.8f, AttackSlope2 = 0.5f, AttackSlope3 = 0.15f;      // 無条件の攻撃 A
    public static float CondKnee1 = 0.4f, CondKnee2 = 1.4f, CondSlope2 = 0.4f, CondSlope3 = 0.15f;              // 条件の攻撃 C
    public static float SpeedKnee1 = 0.30f, SpeedKnee2 = 0.60f, SpeedSlope2 = 0.5f, SpeedSlope3 = 0.15f;        // 移動速度
    public static float SpeedFloor = -0.45f;
    public static float AttackSpeedKnee1 = 0.30f, AttackSpeedKnee2 = 0.90f, AttackSpeedSlope2 = 0.5f, AttackSpeedSlope3 = 0.25f;
    public static float AttackSpeedFloor = -0.40f;
    public static float RangeKnee1 = 0.30f, RangeKnee2 = 0.90f, RangeSlope2 = 0.5f, RangeSlope3 = 0.25f;
    public static float JumpKnee1 = 0.30f, JumpKnee2 = 0.90f, JumpSlope2 = 0.5f, JumpSlope3 = 0.25f;
    public static float JumpFloor = -0.40f;
    public static float CondMultiplierFloor = 0.4f;   // 条件がマイナスの時(SKYBOUND の地上など)の下限(×0.4)

    // ---- HP(ハート)。10倍スケール: ハート1つ = CombatScale.HpPerHeart
    public static int MaxHeartsCap = 20;              // 最大HPの上限(ハート20)
    public static int MinHearts = 1;                  // 封印しても最大HPはこれより下がらない

    // ---- 吸収/回復
    public static float LifestealChanceCap = 0.75f;
    public static int BaseHealHearts = 1;             // 吸収1回の基本回復(ハート)

    // ---- 単発キャラの攻撃シーケンス(First/Combo/Finisher)。PlayerController.AttackSeq
    public static float SingleAttackSequenceReset = 1.2f; // この秒数以内に続けた3回を1つのシーケンスとして数える(ATTACK SPEED系で短くなる: SequenceResetFor)
    public static float SequenceResetMinScale = 0.75f;    // 攻撃速度が速くても、窓はこの割合より短くしない

    // ---- 条件系の上限の回数
    public static int ComboEdgeMaxSteps = 10;
    public static int AirDominionMaxStacks = 4;

    // ---- 速度→攻撃(MOMENTUM / OVERDRIVE / SONIC BLADE)
    public static float MomentumFromKmh = 100f, MomentumFullKmh = 150f, MomentumBeyondSlope = 0.25f;
    public static float OverdriveThresholdKmh = 115f;     // この速さ以上を続けると OVERDRIVE
    public static float OverdriveExitKmh = 107f;          // この速さを下回ると解除(上と少し差をつけてちらつかない)
    public static float OverdriveBaseActivateSeconds = 3.0f, OverdriveActivatePerLevel = 0.15f;
    public static float OverdriveExitGraceSeconds = 0.8f;

    // ---- Shield
    public static float ShieldRechargeBaseSeconds = 24f, ShieldRechargeMinSeconds = 8f;

    // ---- SECOND WIND / LAST CHANCE / PHOENIX
    public static float SecondWindHpFraction = 0.30f;
    public static float SecondWindBaseCooldownMeters = 10000f, SecondWindCooldownPerLevel = 500f;
    public static float LastChanceBaseSeconds = 1.0f, LastChancePerLevel = 0.15f, LastChanceRearmSeconds = 20f;
    public static float PhoenixInvincibleSeconds = 2.0f;
    public static float PerfectGuardBaseSeconds = 0.4f, PerfectGuardPerLevel = 0.1f;

    // ---- JUMP COUNT UP の空中ジャンプの減衰(通常の高さで、追加の空中ジャンプほど弱く)。穴に落ちている時は減衰しない
    public static int JumpDecayFreeJumps = 1;             // キャラ本来の空中ジャンプに加えて、この回数までは減衰なし
    public static float JumpDecayPerJump = 0.10f;         // その先、1回ごとに上昇の初速をこの割合ずつ弱める
    public static float JumpDecayMin = 0.45f;             // 最も弱くてもこの割合
    public static float JumpRecoveryBelow = 0.6f;         // 足元の地面(無ければ直前の地面)よりこの距離(m)以上下なら減衰なし

    // ---- 追加攻撃(カードの効果で出る攻撃)の基準。キャラによらない(どのキャラでも同じカードは同じ強さ)
    public static int ProcReferenceDamage = 20;           // 標準の1発(黒剣士の基礎攻撃)
    public static float ProcAttackShare = 0.5f;           // 無条件の攻撃の枠を半分だけ乗せる(属性/追加攻撃が攻撃×条件×…で乗算爆発しない)

    // ---- 性能の安全
    public static int ProcBudgetPerHalfSecond = 14;       // 追加攻撃(爆発/連鎖/竜巻/衝撃波)を0.5秒にこの回数まで(論理ヒット)
    public static int FxBudgetPerHalfSecond = 10;         // 見た目はさらに少なく(超えた分は見た目を出さずにダメージだけ)
    public static int ChainExplosionMaxGeneration = 3;
    public static int MaxLivingEnemiesForSpawn = 36;      // これ以上いる時は Encounter を後回し(Android の負荷)

    public static float Scale(CardScaling s, float value, int lv)
    {
        if (lv <= 0) return 0f;
        switch (s)
        {
            case CardScaling.Once: return value;
            case CardScaling.Every3: return value * Mathf.CeilToInt(lv / 3f);
            case CardScaling.Every2: return value * Mathf.CeilToInt(lv / 2f);
            case CardScaling.Phoenix: return value * (1 + lv / 2);
            case CardScaling.After3: return value * ((lv - 1) / 3);
            default: return value * lv;
        }
    }

    // 折れ線: x<=k1 はそのまま、k1..k2 は傾き s2、k2.. は傾き s3。マイナスはそのまま(下限は呼ぶ側)
    public static float Soft(float x, float k1, float k2, float s2, float s3)
    {
        if (x <= k1) return x;
        if (x <= k2) return k1 + (x - k1) * s2;
        return k1 + (k2 - k1) * s2 + (x - k2) * s3;
    }

    public static float SoftAttack(float a) => Soft(a, AttackKnee1, AttackKnee2, AttackSlope2, AttackSlope3);
    public static float SoftCondition(float c) => Soft(c, CondKnee1, CondKnee2, CondSlope2, CondSlope3);
    public static float SoftSpeed(float s) => Mathf.Max(SpeedFloor, Soft(s, SpeedKnee1, SpeedKnee2, SpeedSlope2, SpeedSlope3));
    public static float SoftAttackSpeed(float s) => Mathf.Max(AttackSpeedFloor, Soft(s, AttackSpeedKnee1, AttackSpeedKnee2, AttackSpeedSlope2, AttackSpeedSlope3));
    public static float SoftRange(float s) => Mathf.Max(-0.5f, Soft(s, RangeKnee1, RangeKnee2, RangeSlope2, RangeSlope3));
    public static float SoftJump(float s) => Mathf.Max(JumpFloor, Soft(s, JumpKnee1, JumpKnee2, JumpSlope2, JumpSlope3));

    // 条件の倍率(1 + C)。マイナスは下限あり
    public static float CondMultiplier(float c) => Mathf.Max(CondMultiplierFloor, 1f + SoftCondition(c));
}

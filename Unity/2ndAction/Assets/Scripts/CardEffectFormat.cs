using UnityEngine;

// レベルアップ選択UI改修(2026-09-11) - 横長3択UI(Vampire Survivors風)の
// 右端に出す「主要な強化数値」の文字列化。CardDefinition.effects[0](その
// カードの一番代表的な効果)を、EffectTypeの意味(百分率かどうか)に応じて
// "+12%"/"+3" のような短いラベルへ変換する。複数効果を持つカード
// (BERSERKER/HEAVY ARMOR等)もここでは先頭の1つだけを代表値として見せる -
// 内訳の全体はDescriptionテキスト側(CardDatabaseBuilderの説明文)で読める
// 前提。今後「下攻撃威力+落下ダメージ+着地衝撃波」のような複合カードが
// 増えても、代表値が1つ出せるならこの仕組みのまま流用でき、代表値を出す
// 意味が薄いカードはValueLineを空にして数値欄自体を隠せばよい
// (RewardCardData.ValueLine/LevelUpChoiceRowUI.SetContent参照)。
// カードバランス v3(2026-10-03): 1Lvあたりの値を出す(Lvから値を決めるカード)。「持っていれば」「3Lvごと」は印を付ける。
// 挙動のカード(衝撃波/復活など、Lv が強さになるもの)は数値欄を出さない(説明文で読む)。
public static class CardEffectFormat
{
    public static string FormatPrimaryValue(CardDefinition card)
    {
        if (card == null || card.effects == null || card.effects.Count == 0) return "";
        return Format(card.effects[0]);
    }

    public static bool IsPercent(EffectType t)
    {
        switch (t)
        {
            case EffectType.MoveSpeed: case EffectType.JumpPower: case EffectType.AttackRange: case EffectType.AttackSpeed:
            case EffectType.ExpGain: case EffectType.LifestealChance: case EffectType.EnemySpawnRate: case EffectType.EnemyHpMultiplier:
            case EffectType.BossHpMultiplier: case EffectType.MileGainMultiplier: case EffectType.BossMileGainMultiplier:
            case EffectType.SpeedPct: case EffectType.AttackPct: case EffectType.JumpPct: case EffectType.AttackSpeedPct: case EffectType.MainAttackSpeedPct:
            case EffectType.RangePct: case EffectType.AirPct: case EffectType.GroundPct: case EffectType.FirstPct: case EffectType.ComboPct:
            case EffectType.FinisherPct: case EffectType.BossPct: case EffectType.MobPct: case EffectType.AntiAirPct: case EffectType.DownPct:
            case EffectType.FullHpPct: case EffectType.LowHp50Pct: case EffectType.LowHp25Pct: case EffectType.LowHpAttackSpeedPct:
            case EffectType.LowHp25LifestealChance: case EffectType.SealedHeartPct: case EffectType.ComboEdgePct: case EffectType.AirDominionPct:
            case EffectType.MomentumPct: case EffectType.OverdrivePct: case EffectType.OverdriveAttackSpeedPct: case EffectType.DoubleAttackChance:
            case EffectType.HurtKnockbackReduce: case EffectType.EnemyActionPct: case EffectType.EliteChance: case EffectType.TreasureMilePct:
            case EffectType.DistanceMilePct: case EffectType.DistanceExpPct: case EffectType.KillExpPct: case EffectType.BurnPowerPct:
            case EffectType.LightningDamagePct: case EffectType.WindSpeedPct:
            case EffectType.BurnChance: case EffectType.ChillChance: case EffectType.ChillSlow: case EffectType.LightningChance:
            case EffectType.WindBladeChance: case EffectType.BleedChance: case EffectType.BleedLifesteal:
                return true;
        }
        return false;
    }

    // Lv が強さになる「挙動」のカード(数値欄を出さない)
    public static bool IsBehaviorLevel(EffectType t)
    {
        switch (t)
        {
            case EffectType.OverdriveLevel: case EffectType.SonicBladeLevel: case EffectType.CounterLevel: case EffectType.PerfectGuardLevel:
            case EffectType.FlameCounterLevel: case EffectType.SecondWindLevel: case EffectType.LastChanceLevel: case EffectType.PhoenixLevel:
            case EffectType.OverhealLevel: case EffectType.PredatorLevel: case EffectType.PierceLevel: case EffectType.ShockwaveLevel:
            case EffectType.ChainExplosionLevel: case EffectType.AerialBladeLevel: case EffectType.SkyMasterLevel: case EffectType.GroundBreakerLevel:
            case EffectType.ComboMasterLevel: case EffectType.HunterLevel: case EffectType.BrakeLevel: case EffectType.HeavyArmorLevel:
            case EffectType.BossRushLevel: case EffectType.WantedLevel: case EffectType.InfernoLevel: case EffectType.AbsoluteZeroLevel:
            case EffectType.TornadoLevel: case EffectType.BloodBladeLevel: case EffectType.ShieldGuard:
                return true;
        }
        return false;
    }

    public static string Format(CardEffect effect)
    {
        if (effect == null) return "";
        if (IsBehaviorLevel(effect.type)) return "";
        float v = effect.value;
        string sign = v >= 0f ? "+" : ""; // マイナス値は Mathf.Abs しない - "-"がそのまま数値に付く
        string num;
        if (IsPercent(effect.type))
        {
            float p = v * 100f;
            num = Mathf.Approximately(p, Mathf.Round(p)) ? $"{sign}{Mathf.RoundToInt(p)}%" : $"{sign}{p:0.#}%";
        }
        else if (Mathf.Approximately(v, Mathf.Round(v))) num = $"{sign}{Mathf.RoundToInt(v)}";
        else num = $"{sign}{v:0.##}";
        switch (effect.scaling)
        {
            case CardScaling.Once: return num + "(固定)";
            case CardScaling.Every3: return num + "/3Lv";
            case CardScaling.Every2: return num + "/2Lv";
            case CardScaling.After3: return num + "(Lv4,7)";
            case CardScaling.Phoenix: return "Lv";
            default: return num;
        }
    }
}

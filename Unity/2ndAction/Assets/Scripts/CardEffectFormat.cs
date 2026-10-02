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
public static class CardEffectFormat
{
    public static string FormatPrimaryValue(CardDefinition card)
    {
        if (card == null || card.effects == null || card.effects.Count == 0) return "";
        return Format(card.effects[0]);
    }

    public static string Format(CardEffect effect)
    {
        if (effect == null) return "";
        float v = effect.value;
        string sign = v >= 0f ? "+" : ""; // マイナス値は Mathf.Abs しない - "-"がそのまま数値に付く

        switch (effect.type)
        {
            // 倍率/割合系のEffectType - 0.12のような小数を "+12%" へ。
            case EffectType.MoveSpeed:
            case EffectType.JumpPower:
            case EffectType.AttackRange:
            case EffectType.AttackSpeed:
            case EffectType.ExpGain:
            case EffectType.LifestealChance:
            case EffectType.EnemySpawnRate:
            case EffectType.EnemyHpMultiplier:
            case EffectType.BossHpMultiplier:
            case EffectType.MileGainMultiplier:
            case EffectType.BossMileGainMultiplier:
                return $"{sign}{Mathf.RoundToInt(v * 100f)}%";

            // それ以外(攻撃力/回数/HP等の固定値系)はそのまま整数表示。
            default:
                if (Mathf.Approximately(v, Mathf.Round(v)))
                    return $"{sign}{Mathf.RoundToInt(v)}";
                return $"{sign}{v:0.#}";
        }
    }
}

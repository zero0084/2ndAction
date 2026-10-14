using System.Collections.Generic;
using UnityEngine;

// CPU サポート(2026-10-14): 仲間のカードの効果の合計。GameManager.RecomputeCardStats(プレイヤー)と同じ「Lv から毎回計算」の考え方で、
// 仲間の能力(攻撃/攻撃速度/射程/ジャンプ/条件付きの攻撃/Shield など PlayerController.ApplyCardTotals が読むもの)と最大 HP だけを作る。
// ランに1つしか無い物(MILE/EXP の倍率、復活、吸収、FINAL EVOLUTION、属性など)は仲間には付けない。
// HP を削って強くなるカード(封印)は仲間には付けない(払う HP の仕組みが無いのに利点だけ得ないように)。
public static class AllyCardStats
{
    public static CardTotals Compute(Dictionary<string, int> levels, CharacterDefinition def, out int maxHp)
    {
        var t = new CardTotals();
        int per = CombatScale.HpPerHeart;
        float growthHearts = 0f;
        foreach (var kv in levels)
        {
            var c = CardDatabase.FindBaseById(kv.Key);
            int lv = Mathf.Clamp(kv.Value, 0, CardInventory.MaxCardLevel);
            if (c == null || lv <= 0 || c.effects == null || IsSacrificeCard(c)) continue;
            foreach (var e in c.effects)
            {
                if (e.type == EffectType.MaxHpHearts) { growthHearts += CardRules.Scale(e.scaling, e.value, lv); continue; }
                if (e.type == EffectType.MaxHp) { if (e.value > 0f) growthHearts += e.value / per * lv; continue; }
                if (e.type == EffectType.SacrificeHearts) continue;
                float amount = CardRules.Scale(e.scaling, e.value, lv);
                if (BalanceTuning.AttackUpHighLevel && c.cardId == "attack_up" && e.type == EffectType.AttackPct && e.scaling == CardScaling.PerLevel)
                    amount = e.value * BalanceTuning.AttackUpLevelFactor(lv);
                AddLegacyOrV3(t, e.type, amount, per);
            }
        }
        int baseMax = def != null ? def.baseMaxLives : 50;
        maxHp = Mathf.Clamp(baseMax + Mathf.RoundToInt(growthHearts * per), CardRules.MinHearts * per, CardRules.MaxHeartsCap * per);
        return t;
    }

    static bool IsSacrificeCard(CardDefinition c)
    {
        foreach (var e in c.effects)
            if (e.type == EffectType.SacrificeHearts || (e.type == EffectType.MaxHp && e.value < 0f)) return true;
        return false;
    }

    // GameManager.AddLegacyOrV3 と同じ読み替え(旧形式のアセットを v3 の枠へ)
    static void AddLegacyOrV3(CardTotals card, EffectType t, float amount, int per)
    {
        switch (t)
        {
            case EffectType.MoveSpeed: card.Add(EffectType.SpeedPct, amount); break;
            case EffectType.AttackPower: card.Add(EffectType.AttackPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.JumpPower: card.Add(EffectType.JumpPct, amount); break;
            case EffectType.MaxHp: break;
            case EffectType.AttackRange: card.Add(EffectType.RangePct, amount); break;
            case EffectType.AttackSpeed: card.Add(EffectType.AttackSpeedPct, amount); break;
            case EffectType.AirAttackPower: card.Add(EffectType.AirPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.GroundAttackPower: card.Add(EffectType.GroundPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.ComboFinalStageBonus: card.Add(EffectType.FinisherPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.FirstHitBonus: card.Add(EffectType.FirstPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.LowHpAttackBonus: card.Add(EffectType.LowHp50Pct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.FullHpAttackBonus: card.Add(EffectType.FullHpPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.MomentumBonus: card.Add(EffectType.MomentumPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.BossDamageBonus: card.Add(EffectType.BossPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.Shield: card.Add(EffectType.ShieldCapacity, amount); break;
            case EffectType.LifestealAmount: card.Add(EffectType.HealBonusHearts, amount / per); break;
            default: card.Add(t, amount); break;
        }
    }

    // プレイヤーがそのカード(元のカードの ID)を持っている一番高い Lv(合成カードは主の能力で数える)。持っていなければ 0
    public static int OwnedLevel(string baseId)
    {
        int best = 0;
        foreach (var s in CardInventory.Stacks)
        {
            if (s == null || s.count <= 0) continue;
            string main = s.cardId;
            if (CardVariant.IsVariantKey(main)) { var v = CardVariant.Parse(main); main = v != null ? v.mainId : null; }
            if (main == baseId && s.level > best) best = s.level;
        }
        return best;
    }
}

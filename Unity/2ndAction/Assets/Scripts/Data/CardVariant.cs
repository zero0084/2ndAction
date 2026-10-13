using System.Collections.Generic;
using System.Text;
using UnityEngine;

// カード合成改修(2026-09-26) - 所持カード1種類ぶんの「性能」を表す。
// 合成Lv(1〜9)・レア度(★1〜5)・能力一覧(主能力1つ+サブ能力最大8つ)を持ち、
// 能力ごとに「強化量(=そのカードを何回取得した状態として扱うか)」を別管理する。
// 合成Lvは合成の上限管理と表示のためだけの値で、効果には直接掛けない
// (効果に効くのは各能力の強化量だけ - 二重適用を防ぐ)。
//
// 保存・デッキ・キャラカード・Run中の取得履歴はどれも「文字列のID」で
// カードを参照しているので、性能一式をそのまま1本の文字列キーにして、
// 既存の仕組み(CardDatabase.FindById→CardDefinition)へそのまま流す。
//   素のカード(Lv.1・主能力×1・元のレア度)  … 既存のcardIdそのもの("speed_up")
//   それ以外                                   … "v2|主カードID|Lv|レア度|能力ID*強化量/能力ID*強化量..."
// 能力の並びは主能力を先頭、サブ能力はID順に正規化するので、同じ性能なら
// 必ず同じキーになり、所持一覧でもまとめて数えられる。
public sealed class CardVariant
{
    public const string KeyPrefix = "v2|";
    public const int MaxLevel = 9;
    public const int MaxAbilities = 9; // 主能力1 + サブ能力8

    public struct Ability
    {
        public string id;   // 能力の元になったカードのcardId(そのカードのeffects一式が1回分)
        public int stacks;  // 強化量(何回分として適用するか)
    }

    public string mainId;
    public int level = 1;
    public int rarity = 1;
    public readonly List<Ability> abilities = new List<Ability>(); // [0]が主能力

    public int AbilityCount => abilities.Count;
    public Ability Main => abilities.Count > 0 ? abilities[0] : default;

    public int StacksOf(string abilityId)
    {
        foreach (var a in abilities) if (a.id == abilityId) return a.stacks;
        return 0;
    }

    public static bool IsVariantKey(string key) => !string.IsNullOrEmpty(key) && key.StartsWith(KeyPrefix);

    // 素のカードID / v2キーのどちらからでも復元する。旧形式の複合ID("a+b")や
    // 存在しないカードはnull(旧形式はCardDataMigrationが起動時に変換済み)。
    public static CardVariant Parse(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!IsVariantKey(key))
        {
            if (key.IndexOf('+') >= 0) return null;
            CardDefinition def = CardDatabase.FindBaseById(key);
            if (def == null) return null;
            var plain = new CardVariant { mainId = key, level = 1, rarity = Mathf.Clamp(def.rarity, 1, 5) };
            plain.abilities.Add(new Ability { id = key, stacks = 1 });
            return plain;
        }

        string[] parts = key.Split('|');
        if (parts.Length < 5) return null;
        var v = new CardVariant { mainId = parts[1] };
        int.TryParse(parts[2], out v.level);
        int.TryParse(parts[3], out v.rarity);
        v.level = Mathf.Clamp(v.level, 1, MaxLevel);
        v.rarity = Mathf.Clamp(v.rarity, 1, 5);
        foreach (string entry in parts[4].Split('/'))
        {
            if (string.IsNullOrEmpty(entry)) continue;
            int star = entry.LastIndexOf('*');
            string id = star > 0 ? entry.Substring(0, star) : entry;
            int stacks = 1;
            if (star > 0) int.TryParse(entry.Substring(star + 1), out stacks);
            if (CardDatabase.FindBaseById(id) == null) continue; // DBから消えたカードの能力は読み飛ばす
            v.AddAbility(id, Mathf.Max(1, stacks));
        }
        if (v.abilities.Count == 0 || CardDatabase.FindBaseById(v.mainId) == null) return null;
        v.Normalize();
        return v;
    }

    // 同じ能力は枠を増やさず強化量を合算する。
    public void AddAbility(string id, int stacks)
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            if (abilities[i].id == id)
            {
                abilities[i] = new Ability { id = id, stacks = abilities[i].stacks + stacks };
                return;
            }
        }
        abilities.Add(new Ability { id = id, stacks = stacks });
    }

    // 主能力(mainId)を先頭、サブ能力をID順へ並べ替える。
    public void Normalize()
    {
        int mainIndex = abilities.FindIndex(a => a.id == mainId);
        Ability main = mainIndex >= 0 ? abilities[mainIndex] : new Ability { id = mainId, stacks = 1 };
        if (mainIndex >= 0) abilities.RemoveAt(mainIndex);
        abilities.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        abilities.Insert(0, main);
    }

    public CardVariant Clone()
    {
        var c = new CardVariant { mainId = mainId, level = level, rarity = rarity };
        c.abilities.AddRange(abilities);
        return c;
    }

    public string ToKey()
    {
        Normalize();
        CardDefinition def = CardDatabase.FindBaseById(mainId);
        bool plain = def != null && level == 1 && abilities.Count == 1 && abilities[0].stacks == 1 && rarity == Mathf.Clamp(def.rarity, 1, 5);
        if (plain) return mainId;
        var sb = new StringBuilder(KeyPrefix);
        sb.Append(mainId).Append('|').Append(level).Append('|').Append(rarity).Append('|');
        for (int i = 0; i < abilities.Count; i++)
        {
            if (i > 0) sb.Append('/');
            sb.Append(abilities[i].id).Append('*').Append(abilities[i].stacks);
        }
        return sb.ToString();
    }

    // ===== 表示用 ===== //

    public static string AbilityName(string abilityId)
    {
        CardDefinition def = CardDatabase.FindBaseById(abilityId);
        return def != null ? def.cardName : abilityId;
    }

    // 「移動速度 +12%」のような1回分の効果を並べたもの。
    public static string AbilityEffectText(string abilityId)
    {
        CardDefinition def = CardDatabase.FindBaseById(abilityId);
        if (def == null || def.effects == null || def.effects.Count == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < def.effects.Count; i++)
        {
            if (i > 0) sb.Append(" / ");
            sb.Append(EffectPart(def.effects[i]));
        }
        return sb.ToString();
    }

    // 1回分(Lv1)の効果の表示。挙動のカードは Lv1 で「何がどれだけ」
    public static string EffectPart(CardEffect e)
    {
        if (e == null) return "";
        if (CardEffectFormat.IsBehaviorLevel(e.type)) return CardBehaviorText.Describe(e.type, 1) + Loc.T("(Lvで伸びる)");
        return EffectLabel(e.type) + " " + CardEffectFormat.Format(e);
    }

    // 2026-10-13: 強化量 = その能力の Lv。表示は「1回分 ×強化量」をやめ、その Lv で実際に効く値(固定/毎Lv/2Lvごと/3Lvごと・上限込み)。
    // 1ランで能力ごとに Lv9 まで(それより大きい強化量は Lv9 として効く: GameManager.ApplyRunCardCapped)
    public static string AbilityLine(string abilityId, int stacks)
    {
        int lv = Mathf.Clamp(stacks, 1, MaxLevel);
        string over = stacks > MaxLevel ? Loc.F("(強化量{0}・効くのはLv9まで)", stacks) : "";
        return $"{AbilityName(abilityId)} Lv{lv}{over}  ({AbilityEffectTextAt(abilityId, stacks)})";
    }

    // 2026-10-11: 強化量ぶんの効果の表示。ATTACK UP は高い Lv で伸びが大きい(BalanceTuning)ので「1回分 ×強化量」ではなく合計で出す
    public static string AbilityEffectTextAt(string abilityId, int stacks)
    {
        if (BalanceTuning.AttackUpHighLevel && abilityId == "attack_up")
        {
            CardDefinition def = CardDatabase.FindBaseById(abilityId);
            if (def != null && def.effects != null && def.effects.Count > 0)
            {
                float p = def.effects[0].value * BalanceTuning.AttackUpLevelFactor(stacks) * 100f;
                return $"{EffectLabel(def.effects[0].type)} +{p:0.#}%";
            }
        }
        return EffectsAtLevel(abilityId, Mathf.Clamp(stacks, 1, MaxLevel));
    }

    // その Lv で実際に効く効果(同じ種類の「毎Lv」と「固定」は合計して1つに)。挙動のカードは「何がどれだけ」(CardBehaviorText)
    public static string EffectsAtLevel(string abilityId, int lv)
    {
        CardDefinition def = CardDatabase.FindBaseById(abilityId);
        if (def == null || def.effects == null || def.effects.Count == 0) return "";
        var order = new List<EffectType>();
        var sum = new Dictionary<EffectType, float>();
        foreach (var e in def.effects)
        {
            if (e == null) continue;
            if (!order.Contains(e.type)) order.Add(e.type);
            if (!CardEffectFormat.IsBehaviorLevel(e.type)) sum[e.type] = (sum.TryGetValue(e.type, out float v) ? v : 0f) + CardRules.Scale(e.scaling, e.value, lv);
        }
        var sb = new StringBuilder();
        foreach (var t in order)
        {
            string part;
            if (CardEffectFormat.IsBehaviorLevel(t)) part = CardBehaviorText.Describe(t, lv);
            else
            {
                float v = sum[t];
                float cap = DisplayCap(t);
                bool capped = cap > 0f && v > cap;
                if (capped) v = cap;
                part = EffectLabel(t) + " " + CardEffectFormat.FormatTotal(t, v) + (capped ? Loc.T("(効果上限)") : "");
            }
            if (string.IsNullOrEmpty(part)) continue;
            if (sb.Length > 0) sb.Append(" / ");
            sb.Append(part);
        }
        return sb.ToString();
    }

    // 1つの効果の上限(実際の計算でこれ以上は効かない物。0 = 上限なし)
    static float DisplayCap(EffectType t)
    {
        switch (t)
        {
            case EffectType.ShieldCapacity: return 3f;
            case EffectType.BurnChance: case EffectType.ChillChance: case EffectType.LightningChance: case EffectType.WindBladeChance: case EffectType.BleedChance: return 1f;
            case EffectType.DoubleAttackChance: return 0.9f;
            case EffectType.LifestealChance: return 0.75f;
            case EffectType.ChillSlow: return 0.7f;
            case EffectType.LightningChains: return 8f;
            case EffectType.LightningSplash: return 3f;
            case EffectType.LightningShock: return 0.6f;
            case EffectType.WindPierce: return 20f;
            case EffectType.FreezeThresholdReduce: return 3f; // 凍結に必要な回数は最少2(基本5)
        }
        return 0f;
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < abilities.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(i == 0 ? "【主】" : "【副】").Append(AbilityLine(abilities[i].id, abilities[i].stacks));
        }
        return sb.ToString();
    }

    public static string EffectLabel(EffectType type)
    {
        switch (type)
        {
            case EffectType.MoveSpeed: return "移動速度";
            case EffectType.AttackPower: return "攻撃力";
            case EffectType.JumpPower: return "ジャンプ力";
            case EffectType.JumpCount: return "ジャンプ回数";
            case EffectType.MaxHp: return "最大HP";
            case EffectType.AttackRange: return "攻撃範囲";
            case EffectType.AttackSpeed: return "攻撃速度";
            case EffectType.AirAttackPower: return "空中攻撃力";
            case EffectType.Shield: return "シールド";
            case EffectType.ExpGain: return "獲得EXP";
            case EffectType.LifestealChance: return "吸収確率";
            case EffectType.LifestealAmount: return "吸収量";
            case EffectType.EnemySpawnRate: return "敵の出現頻度";
            case EffectType.GroundAttackPower: return "地上攻撃力";
            case EffectType.ComboFinalStageBonus: return "コンボ締め威力";
            case EffectType.FirstHitBonus: return "初撃威力";
            case EffectType.LowHpAttackBonus: return "瀕死時攻撃力";
            case EffectType.FullHpAttackBonus: return "HP満タン時攻撃力";
            case EffectType.MomentumBonus: return "加速時攻撃力";
            case EffectType.BossDamageBonus: return "ボスへの攻撃力";
            case EffectType.EnemyHpMultiplier: return "敵HP";
            case EffectType.BossHpMultiplier: return "ボスHP";
            case EffectType.MileGainMultiplier: return "敵撃破・BONUSのMILE";
            case EffectType.BossMileGainMultiplier: return "ボス撃破MILE";
            case EffectType.BurnChance: return "炎上確率";
            case EffectType.BurnPower: return "炎上の基礎威力";
            case EffectType.BurnDuration: return "炎上の時間";
            case EffectType.ChillChance: return "冷えの確率";
            case EffectType.ChillSlow: return "冷えによる減速";
            case EffectType.ChillDuration: return "冷気の時間";
            case EffectType.FreezeStacks: return "凍結までの蓄積";
            case EffectType.FreezeDuration: return "凍結の時間";
            case EffectType.LightningChance: return "落雷確率";
            case EffectType.LightningPower: return "落雷の威力";
            case EffectType.LightningChains: return "連鎖数";
            case EffectType.LightningRange: return "連鎖の距離";
            case EffectType.WindBladeChance: return "風刃確率";
            case EffectType.WindBladePower: return "風刃の威力";
            case EffectType.WindPierce: return "貫通数";
            case EffectType.WindRange: return "風刃の射程";
            case EffectType.BleedChance: return "出血確率";
            case EffectType.BleedPower: return "出血の威力";
            case EffectType.BleedDuration: return "出血の時間";
            case EffectType.BleedLifesteal: return "出血の吸収";
            case EffectType.BloodLowHpBonus: return "瀕死時の出血強化";
            // カードバランス v3(2026-10-03)
            case EffectType.SpeedPct: return "移動速度";
            case EffectType.AttackPct: return "攻撃力";
            case EffectType.JumpPct: return "ジャンプ力";
            case EffectType.AttackSpeedPct: return "攻撃速度";
            case EffectType.MainAttackSpeedPct: return "前攻撃・後攻撃の速度";
            case EffectType.RangePct: return "攻撃範囲";
            case EffectType.MaxHpHearts: return "最大HP(ハート)";
            case EffectType.SacrificeHearts: return "封印するハート";
            case EffectType.AirPct: return "空中での攻撃力";
            case EffectType.GroundPct: return "地上での攻撃力";
            case EffectType.FirstPct: return "初撃";
            case EffectType.ComboPct: return "連撃中";
            case EffectType.FinisherPct: return "連続攻撃の締め";
            case EffectType.BossPct: return "ボスへの攻撃";
            case EffectType.MobPct: return "雑魚への攻撃力";
            case EffectType.AntiAirPct: return "空中の敵への攻撃";
            case EffectType.DownPct: return "下攻撃・叩きつけ";
            case EffectType.FullHpPct: return "満HPの攻撃";
            case EffectType.LowHp50Pct: return "HP50%以下の攻撃";
            case EffectType.LowHp25Pct: return "HP25%以下の攻撃";
            case EffectType.LowHpAttackSpeedPct: return "HP50%以下の攻撃速度";
            case EffectType.LowHp25LifestealChance: return "HP25%以下の吸収確率";
            case EffectType.SealedHeartPct: return "封印1つごとの攻撃";
            case EffectType.ComboEdgePct: return "連続HITごとの攻撃力";
            case EffectType.AirDominionPct: return "着地まで空中で当てるごとの攻撃力";
            case EffectType.MomentumPct: return "150km/hでの攻撃";
            case EffectType.OverdrivePct: return "OVERDRIVE中の攻撃";
            case EffectType.OverdriveAttackSpeedPct: return "OVERDRIVE中の攻撃速度";
            case EffectType.ShieldCapacity: return "Shieldの最大数";
            case EffectType.ShieldRecharge: return "Shield回復の短縮(秒)";
            case EffectType.HealBonusHearts: return "回復量(ハート)";
            case EffectType.DoubleAttackChance: return "追加攻撃の確率";
            case EffectType.HurtInvincibleSeconds: return "被弾後の無敵(秒)";
            case EffectType.HurtKnockbackReduce: return "のけぞりの軽減";
            case EffectType.EnemyActionPct: return "敵の行動速度";
            case EffectType.EliteChance: return "精鋭の出現率";
            case EffectType.TreasureMilePct: return "宝・報酬のMILE";
            case EffectType.DistanceMilePct: return "走行距離MILE";
            case EffectType.DistanceExpPct: return "走行距離EXP";
            case EffectType.KillExpPct: return "敵撃破EXP";
            case EffectType.BurnPowerPct: return "炎上の基礎威力";
            case EffectType.FreezeThresholdReduce: return "凍結に必要な回数の減少";
            case EffectType.LightningDamagePct: return "落雷ダメージ倍率";
            case EffectType.LightningShock: return "落雷の停止(秒)";
            case EffectType.LightningSplash: return "落雷の範囲(m)";
            case EffectType.WindSpeedPct: return "風刃の速さ";
            default:
                if (CardEffectFormat.IsBehaviorLevel(type)) return ""; // 2026-10-13: 挙動のカードは CardBehaviorText で「何がどれだけ」を出す
                return type.ToString();
        }
    }
}

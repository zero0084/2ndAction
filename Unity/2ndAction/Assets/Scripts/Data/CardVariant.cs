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
            sb.Append(EffectLabel(def.effects[i].type)).Append(' ').Append(CardEffectFormat.Format(def.effects[i]));
        }
        return sb.ToString();
    }

    // 強化量ぶんを合計した効果(倍率系は既存の「取得回数ぶん重ねがけ」をそのまま適用する
    // ため、表示は「1回分 ×強化量」で出す)。
    public static string AbilityLine(string abilityId, int stacks)
    {
        return $"{AbilityName(abilityId)} ×{stacks}  ({AbilityEffectText(abilityId)} ×{stacks})";
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
            case EffectType.EnemySpawnRate: return "敵出現率";
            case EffectType.GroundAttackPower: return "地上攻撃力";
            case EffectType.ComboFinalStageBonus: return "コンボ締め威力";
            case EffectType.FirstHitBonus: return "初撃威力";
            case EffectType.LowHpAttackBonus: return "瀕死時攻撃力";
            case EffectType.FullHpAttackBonus: return "HP満タン時攻撃力";
            case EffectType.MomentumBonus: return "加速時攻撃力";
            case EffectType.BossDamageBonus: return "ボスへの攻撃力";
            case EffectType.EnemyHpMultiplier: return "敵HP";
            case EffectType.BossHpMultiplier: return "ボスHP";
            case EffectType.MileGainMultiplier: return "獲得MILE";
            case EffectType.BossMileGainMultiplier: return "ボス獲得MILE";
            default: return type.ToString();
        }
    }
}

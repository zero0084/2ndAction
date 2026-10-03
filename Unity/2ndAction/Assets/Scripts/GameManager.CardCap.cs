using System.Collections.Generic;
using UnityEngine;

// カードLv9上限を「能力」ごとに数える(2026-10-03)。
// 以前は GetCurrentRunStack が「カードIDの文字列」ごとに数えていたため、同じ能力でも
//   素のカード "attack_up" / 合成カード "v2|attack_up|9|1|attack_up*9" / レア度だけ違う "v2|attack_up|9|2|..." /
//   旧形式の複合ID "attack_up+speed_up"
// がそれぞれ別々に Lv9 まで入り、キャラカード枠とラン中の取得を合わせて同じ能力を最大117回分重ねられた。
// ここでは、ラン中に実際に適用した回数を能力(元カードの cardId)ごとに数え、どの経路(キャラカード枠/
// レベルアップ/ボス報酬/CONTINUE の再適用)でも、1つの能力は合計 MaxRunCardLevel(9)回分までしか適用しない。
//   ・合成カードは能力ごとに残りの枠まで適用する(主能力が上限でもサブ能力に空きがあればそこだけ効く)
//   ・候補に出すかどうか / 表示のLvは、そのカードの主能力のLvで決める
// 効果の中身(1回分の値)は変えていない。CARD BALANCE TEST などの開発用ツールは従来どおり ApplyCardEffectsStacked で
// 上限なしに掛ける(数値の確認用)。
public partial class GameManager
{
    readonly Dictionary<string, int> runAbilityStacks = new Dictionary<string, int>();
    // 上限で適用しなかった回数(確認用)
    public int CardCapDiscardedStacks { get; private set; }

    // そのカードIDに含まれる能力と強化量(素のカード=自分×1、合成=各能力×強化量、旧形式 "a+b" = a と b)
    public static List<CardVariant.Ability> AbilitiesOf(string cardId)
    {
        var list = new List<CardVariant.Ability>();
        CollectAbilities(cardId, 1, list, 0);
        return list;
    }

    static void CollectAbilities(string cardId, int mul, List<CardVariant.Ability> into, int depth)
    {
        if (string.IsNullOrEmpty(cardId) || depth > 8) return;
        if (CardVariant.IsVariantKey(cardId))
        {
            CardVariant v = CardVariant.Parse(cardId);
            if (v == null) return;
            foreach (var a in v.abilities) AddAbility(into, a.id, a.stacks * mul);
            return;
        }
        if (CardDatabase.FindBaseById(cardId) != null) { AddAbility(into, cardId, mul); return; }
        int plus = cardId.IndexOf('+');
        if (plus > 0 && plus < cardId.Length - 1)
        {
            CollectAbilities(cardId.Substring(0, plus), mul, into, depth + 1);
            CollectAbilities(cardId.Substring(plus + 1), mul, into, depth + 1);
        }
    }

    static void AddAbility(List<CardVariant.Ability> into, string id, int stacks)
    {
        for (int i = 0; i < into.Count; i++)
            if (into[i].id == id) { into[i] = new CardVariant.Ability { id = id, stacks = into[i].stacks + stacks }; return; }
        into.Add(new CardVariant.Ability { id = id, stacks = stacks });
    }

    // そのカードの主能力(表示名の元になっている能力)
    public static string MainAbilityOf(string cardId)
    {
        var list = AbilitiesOf(cardId);
        return list.Count > 0 ? list[0].id : cardId;
    }

    // このランで適用済みの能力の回数(0〜9)
    public int GetAbilityRunStack(string abilityId) => abilityId != null && runAbilityStacks.TryGetValue(abilityId, out int n) ? n : 0;
    // PHOENIX の消費(2026-10-03): その能力をこのランで取っていない状態へ戻す(候補に再び出る。取り直しは Lv1 から)
    void ResetAbilityRunStack(string abilityId) { if (abilityId != null) runAbilityStacks.Remove(abilityId); }

    // カード1枚を times 回分、能力ごとの上限を守って適用する。適用した回数の合計を返す。
    int ApplyRunCardCapped(CardDefinition card, int times, string source)
    {
        if (card == null || times <= 0) return 0;
        var abilities = AbilitiesOf(card.cardId);
        if (abilities.Count == 0)
        {
            // 能力に分解できない(想定外の形式): 従来どおり1回分ずつ。上限はカードIDで数える
            ApplyCardEffectsStacked(card, times);
            return times;
        }
        int applied = 0;
        foreach (var a in abilities)
        {
            CardDefinition src = CardDatabase.FindBaseById(a.id);
            if (src == null) continue;
            int want = a.stacks * times;
            int have = GetAbilityRunStack(a.id);
            int n = Mathf.Clamp(MaxRunCardLevel - have, 0, want);
            if (n > 0) ApplyCardEffectsStacked(src, n);
            runAbilityStacks[a.id] = have + n;
            applied += n;
            if (n < want)
            {
                CardCapDiscardedStacks += want - n;
                Debug.Log($"[CardCap] {source}: {a.id} at Lv{have} - applied {n} of {want} (max {MaxRunCardLevel})");
            }
        }
        return applied;
    }
}

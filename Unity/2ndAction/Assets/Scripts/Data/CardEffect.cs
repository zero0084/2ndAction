using System;

// One (EffectType, value) pair inside a CardDefinition.effects list. A card
// with multiple entries (e.g. BERSERKER: AttackPower +3 and MaxHp -1) just
// applies each in turn - see GameManager.ApplyCardEffects.
[Serializable]
public class CardEffect
{
    public EffectType type;
    // Meaning depends on `type` - see the comments on each EffectType.
    // Placeholder magnitude for now; tune freely in the Inspector.
    public float value;
    // カードバランス v3(2026-10-03): カードのLvからの値の出し方(CardRules.Scale)。
    // 既定の PerLevel は「1Lvごとに value」= 以前の「1回取るごとに足す」と同じ(古いアセットはこのまま)。
    public CardScaling scaling;
}

public enum CardScaling
{
    PerLevel,     // value × Lv
    Once,         // 持っていれば value(Lvによらない)
    Every3,       // value × ceil(Lv/3)   … Lv1-3=1, 4-6=2, 7-9=3
    Every2,       // value × ceil(Lv/2)   … Lv1-2=1, 3-4=2, 5-6=3, 7-8=4, 9=5
    Phoenix,      // value × (1 + floor(Lv/2)) … Lv1=1, 2-3=2, 4-5=3, 6-7=4, 8-9=5
    After3        // value × floor((Lv-1)/3) … Lv1-3=0, 4-6=1, 7-9=2
}

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
}

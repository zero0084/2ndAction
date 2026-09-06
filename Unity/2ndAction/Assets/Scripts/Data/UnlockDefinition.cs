using UnityEngine;

// One entry in the distance-unlock system: "once the player has reached
// requiredDistance meters (in any single run), TargetID of UnlockType
// becomes available." Data-only, same philosophy as CardDefinition - a
// designer creates one of these (via UnlockDatabaseBuilder or by hand) and
// nothing else needs code changes for it to take effect.
[CreateAssetMenu(fileName = "UnlockDefinition", menuName = "OneMoreMile/Unlock Definition")]
public class UnlockDefinition : ScriptableObject
{
    // Stable identifier for this unlock itself (not the thing it unlocks) -
    // what gets persisted in the save file. Never reuse/repurpose an id
    // once shipped, the same rule as CardDefinition.cardId.
    public string unlockId;
    public float requiredDistance;
    public UnlockType unlockType;
    // Matches CardDefinition.cardId, EnemyDefinition.enemyId, or (once an
    // Area system exists) an area id - whatever id-space UnlockType points
    // into. Plain string rather than a typed reference so this asset never
    // needs to know about CardDefinition/EnemyDefinition/etc. directly.
    public string targetId;
    // Shown in the "new unlock" announcement (see GameManager) - e.g.
    // "新しいカード: FAR RUNNER".
    public string displayName;
}

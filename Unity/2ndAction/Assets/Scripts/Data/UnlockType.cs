// What kind of content an UnlockDefinition gates. Add new cases here as
// new unlockable content categories are introduced (e.g. a future Skill or
// Cosmetic type) - UnlockManager/UnlockDefinition don't need to change,
// only wherever that new type is actually consumed (see CardDatabase's/
// EnemyDatabase's unlock-filtered accessors for the pattern to follow).
public enum UnlockType
{
    Card,
    Enemy,
    Area
}

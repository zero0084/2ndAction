using UnityEngine;

// Distance Level Design Ver.1 - which special movement/attack Behavior (see
// EnemySpecialBehavior) a species uses on top of the base EnemyController
// (hit/HP/death handling, shared by every category). None = the original
// goblin-style "stands on its spawn chunk, no special Behavior" species.
public enum EnemyBehaviorKind
{
    None,
    Flying,
    Irregular,
    Shooter,
    Heavy,
    Chaser,
    Rusher
}

// Distance Level Design Ver.1 - the "Enemy Type" DistanceTierManager's
// AvailableEnemyTypes filters by (item 3/5 of the brief). Separate from
// EnemyBehaviorKind above: Normal/Flying/Heavy all use
// EnemyBehaviorKind.None (no special movement Behavior) but still need
// their own category here for tier-availability and HP-multiplier lookup.
public enum EnemyCategory
{
    Normal,
    Flying,
    Irregular,
    Shooter,
    Heavy,
    Chaser,
    Rusher
}

// One enemy "species" - data-only, same philosophy as CardDefinition. Only
// the goblin (always unlocked, no matching UnlockDefinition) exists today;
// this exists so a future species can be added as pure data (a new asset +
// an UnlockDefinition pointing at its enemyId) instead of needing new code
// in TerrainManager/EnemyWallManager.
[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "OneMoreMile/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public string enemyId;
    public string displayName;
    // Already-import-configured (foot pivot, PPU) by SceneBuilder before
    // EnemyDatabaseBuilder assigns it here - see SceneBuilder's enemy
    // sprite setup block.
    public Sprite sprite;
    // Lets a placeholder species reuse existing art (e.g. the test "elite
    // goblin" unlock recolors the same goblin sprite) without needing new
    // art just to read as visually distinct.
    public Color tint = Color.white;
    public EnemyMovementType movementType = EnemyMovementType.Ground;

    // Distance Level Design Ver.1 additions below - EnemyController/
    // EnemySpecialBehavior read these at spawn time (see GroundFactory.
    // CreateEnemy's new params); nothing here changes how the ORIGINAL
    // goblin (category Normal, behaviorKind None, hpMultiplier 1,
    // bigKnockbackOnHit false) spawns or fights, so every existing species/
    // save/scene is unaffected.
    public EnemyCategory category = EnemyCategory.Normal;
    // Kept alongside `category` (not derived) so this stays plain data any
    // EnemyDatabaseBuilder-style code can set directly - EnemyDatabaseBuilder
    // is what keeps behaviorKind/movementType consistent with category for
    // every species it builds; see its own comment.
    public EnemyBehaviorKind behaviorKind = EnemyBehaviorKind.None;
    // Multiplies the tier's base HP (see DistanceTierManager.CurrentEnemyHp)
    // for this species specifically - Heavy's "この基本HPへ追加倍率" from
    // the brief. 1 for every other species.
    public float hpMultiplier = 1f;
    // Heavy Enemy only - "画面端まで吹っ飛ばすような感じ" on a non-lethal
    // hit, implemented as a much larger hitKnockbackDistance/Duration on
    // EnemyController (see GroundFactory.CreateEnemy) rather than a new
    // mechanic - reuses the existing small-knockback code path at a bigger
    // magnitude.
    public bool bigKnockbackOnHit = false;

    // Distance Level Design Ver.1.1 - facing fix ("新規Enemy/Boss画像が進
    // 行方向に対して後ろ向き"). false (unchanged) for the original goblin/
    // goblin_elite - their art was already correct, and this whole system
    // is opt-in per species specifically so it can never regress them.
    // defaultFacingRight only matters when enableVisualFacing is true - see
    // EnemyFacing/GroundFactory.CreateEnemy.
    public bool enableVisualFacing = false;
    public bool defaultFacingRight = true;

    // Runner Enemy Run Animation - "移動中は常にRun Animation再生". Empty
    // (default) for every species except Chaser/Rusher, which both point
    // at the SAME 5-Sprite array ("Runner Enemy素材...Chaser/Rusherの両
    // Behaviorで共用" - Ver.1's own instruction, still true here) - see
    // GroundFactory.CreateEnemy/EnemyAnimator.runFrames for how this plays.
    public Sprite[] runFrames;

    // Reward/MILE System Ver.1 - MILE granted to GameManager.RegisterEnemyKill
    // when a spawn from this species dies (see GroundFactory.CreateEnemy /
    // EnemyController.mileReward). Per-species and Inspector-tunable, per
    // the brief's "Enemy/BossのMILE値はEnemyDataのようなデータから変更可能
    // に". 1 for every existing species (goblin/goblin_elite), matching
    // "Normal種は1" - no behavior change for anything already shipped.
    public int mileReward = 1;

    // Enemy Visual Size Unification pass - uniform scale applied ONLY to
    // the Visual child (GroundFactory.CreateEnemy sets visualGO.transform.
    // localScale = Vector3.one * visualScaleMultiplier, before any other
    // component's Start() runs, so EnemyAnimator's captured "baseVisualScale"
    // and EnemyFacing's sign-flip both already account for it correctly -
    // see their own comments). Root/Collider/AttackHitbox/GroundCheck are
    // completely unaffected, per the brief's explicit "見た目だけを揃える
    // 修正" - a species whose sprite has notably more/less transparent
    // padding than its Collider assumes will end up with a visibly
    // mismatched hitbox after this; see EnemyDatabaseBuilder's Specs for
    // which species that applies to (flagged there, not silently
    // "corrected" by also resizing the Collider). 1 for goblin/goblin_elite
    // (the size baseline every other species is measured against).
    public float visualScaleMultiplier = 1f;
}

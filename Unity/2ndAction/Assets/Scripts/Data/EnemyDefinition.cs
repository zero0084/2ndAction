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
    Rusher,
    // 敵AI行動Tier試験実装(2026-09-16) - 「その場から動かず、時々ゆっくり
    // Telegraph→Attack→Recoveryの近接攻撃を行う」パターン。Goblin専用では
    // なく、将来ほかの種族がT1/T2的な挙動を使いたくなった場合にも再利用
    // できるよう、種族名ではなく行動パターン名で命名した(Flying/Irregular
    // 等、既存の値と同じ命名方針)。実際の強度差(T1=移動なし/T2=+ランダム
    // 小移動)はEnemyAiTierで切り替える - こちらは「近接攻撃できる」という
    // 種族側の能力フラグに過ぎない。
    StationaryMelee,
    // 自然洞窟雑魚敵追加(2026-09-22) - 基本位置周辺をランダムに動き回り、
    // 時々Hopし、たまに近接攻撃も行う(StationaryMelee T2の移動+攻撃を
    // 1種にまとめた挙動)。Cave Hopper用。
    CaveHopper,
    // 自然洞窟雑魚敵追加(2026-09-22) - 地中で待機→予兆→出現→攻撃→退避を
    // 繰り返す。地中の間はCollider/AttackHitboxとも無効(攻撃対象外)。
    // Burrow Worm用。
    BurrowWorm
}

// 敵AI行動Tier試験実装(2026-09-16) - 「敵AIの行動・攻撃性の段階」を表す、
// HP・サイズ・武器・近接/遠距離とは完全に独立した軸(EnemyBehaviorKind/
// EnemyCategoryのどちらとも別物)。将来的に「T5だが柔らかい敵」「T2だが
// 硬いHeavy」のような組み合わせが成立するよう、他のフィールドと掛け合わせ
// で使う前提。今回はT0〜T2のみ実装(EnemySpecialBehavior参照)、T3〜T5は
// 将来の拡張用に列挙子だけ用意してある。
public enum EnemyAiTier
{
    T0, // Passive - 棒立ち、自発的攻撃なし(既存ゴブリンの挙動そのもの)
    T1, // Slow Random Attack - その場から動かず、遅いランダム近接攻撃
    T2, // Random Movement + Slow Random Attack - T1の攻撃 + 基本位置周辺のランダムな小移動/Hop
    T3, // 将来実装 - Playerを対象に通常速度で攻撃しつつゆっくり接近
    T4, // 将来実装 - 棒立ちのまま激しいランダム攻撃
    T5  // 将来実装 - Playerを積極的に追跡し、高速かつ積極的に攻撃
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

    // 攻撃ポーズ(2026-09-26) - 予備動作〜攻撃中にEnemyAnimatorが表示する1枚絵(任意)。
    // runFramesの1コマ目と同じ縮尺(同じPPU・足元ピボット)で読み込む(EnemyDatabaseBuilder)。
    public Sprite attackSprite;

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

    // 敵AI行動Tier試験実装(2026-09-16) - HP/サイズ/種族とは独立した「行動
    // Tier」。EnemyAiTierの型コメント参照。デフォルトT0(既存ゴブリンと
    // 完全に同じ、追加コンポーネントなし)なので、この値を明示的に設定
    // しない既存の全EnemyDefinitionアセットは今までどおり無改造で動く。
    public EnemyAiTier aiTier = EnemyAiTier.T0;

    // 自然洞窟雑魚敵追加(2026-09-22) - このEnemyDefinitionを出現させて良い
    // ステージIDの一覧。null/空 = 従来どおり全ステージ(既存8種は全てこの
    // まま、挙動無変更)。1つ以上指定した場合、GameManager.ActiveRunStageId
    // がこの中に含まれる場合だけ抽選対象になる(EnemyDatabase.PickRandom*
    // 参照)。
    public string[] stageIds;
}

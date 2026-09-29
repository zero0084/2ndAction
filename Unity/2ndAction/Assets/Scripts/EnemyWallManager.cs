using System.Collections.Generic;
using UnityEngine;

// Every wallInterval meters, places a vertical column ("wall") of regular
// grunt enemies ahead of the player - spawned the same way as ordinary
// TerrainManager enemies (placed ahead of generateAheadDistance, so they
// scroll into view rather than popping in), but skipped whenever that
// milestone coincides with a boss encounter (the boss takes that slot
// instead).
public class EnemyWallManager : MonoBehaviour
{
    public Transform player;
    public Sprite squareSprite;
    // Legacy fallback - see TerrainManager.enemySprite's own comment.
    public Sprite enemySprite;
    // Distance-unlock system - same pool TerrainManager uses (SceneBuilder
    // wires both from EnemyDatabase.AllEnemies). One species is picked per
    // wall (not per-enemy-in-the-column), so a column reads as one themed
    // group rather than a mixed grab-bag.
    public List<EnemyDefinition> enemyPool = new List<EnemyDefinition>();

    public float wallInterval = 500f;
    public int columnCount = 3;
    public float columnSpacing = 1.6f;
    public float groundClearance = 0.5f;
    public float spawnAheadDistance = 28f;
    // Tolerance (meters) for treating a wall milestone as "the same spot" as
    // the boss's next scheduled encounter.
    public float bossCollisionTolerance = 1f;
    // Stage01次段階調整(2026-09-16), item2/3 - ObstacleSpawner.
    // pitObstacleClearanceと同じ考え方 - 穴の縁からこの距離以内には敵の
    // 列を置かない(「穴を避けようとしたら必ず敵に当たる」を避ける)。
    public float pitEnemyClearance = 2.5f;
    public float maxPitAvoidSearch = 16f;

    float startX;
    float nextWallDistance;

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; FloatingOrigin.Warped += OnOriginWarped; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; FloatingOrigin.Warped -= OnOriginWarped; }
    // Floating Origin: 座標を戻した分、配置の基準X(startX)も戻す。
    void OnOriginShifted(float s) { startX -= s; }
    void OnOriginWarped(float d)
    {
        startX -= d;
        if (GameManager.Instance != null) nextWallDistance = Mathf.Max(nextWallDistance, GameManager.Instance.MaxDistance);
    }

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextWallDistance = EffectiveWallInterval();
    }

    float EffectiveWallInterval()
    {
        // (2026-09-26) Debug Modeでの間隔短縮(100m)は廃止 - 距離ワープボタンで代替する。
        float baseInterval = wallInterval;
        // "GREED" raises GameManager.EnemySpawnRateMultiplier above 1, which
        // shortens this interval - more frequent walls, as its downside.
        float multiplier = GameManager.Instance != null ? GameManager.Instance.EnemySpawnRateMultiplier : 1f;
        return baseInterval / Mathf.Max(0.1f, multiplier);
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        if (GameManager.Instance.CountdownActive) return; // Stage01地形挙動修整(2026-09-17), item4
        if (player == null) return;
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;
        if (BonusZone.SuppressesNormalSpawns) return; // BONUS ZONE中は通常の敵/障害物を出さない

        while (GameManager.Instance.MaxDistance >= nextWallDistance)
        {
            bool takenByBoss = BossManager.Instance != null
                && Mathf.Abs(BossManager.Instance.NextBossDistance - nextWallDistance) < bossCollisionTolerance;
            // Run Continuation/Checkpoint Ver.1, item 14 - milestones that
            // fall inside the post-CONTINUE safe zone are skipped (not
            // deferred - nextWallDistance still advances normally below),
            // so nothing piles up to spawn all at once the moment the zone
            // ends.
            bool inSafeZone = GameManager.Instance.IsInSafeZone;

            // 共通Encounter System(2026-09-27) - EncounterDirectorが敵の出方(波/休憩)を管理するステージでは、
            // 500mごとの敵の壁は出さない(ProfileのreplacesMilestoneWallsで切り替え)。
            bool encounterOwned = EncounterDirector.HandlesMilestoneWalls(GameManager.Instance.ActiveRunStageId);
            if (!takenByBoss && !inSafeZone && !encounterOwned) SpawnWall(nextWallDistance);

            nextWallDistance += EffectiveWallInterval();
        }
    }

    void SpawnWall(float milestoneDistance)
    {
        float worldX = startX + milestoneDistance + HighSpeedAssist.SpawnAhead(spawnAheadDistance); // 高速時の自動操作補助: 先読み範囲に置く

        // Stage01次段階調整(2026-09-16) - ObstacleSpawner.SpawnObstacleと
        // 同じ理由の安全策(TerrainManager.generateAheadDistanceのコメント
        // 参照)。まだ地形が生成されていない位置を狙っている間はこの回の
        // Wallを諦める。
        if (TerrainManager.Instance != null && !TerrainManager.Instance.IsGenerated(worldX)) return;

        // If that exact X lands in a pit, or too close to one, nudge
        // forward in small steps to find solid ground rather than spawning
        // enemies over empty air or right next to a hole (item2/3 - 「穴を
        // 避けようとしたら必ず敵に当たる」を避ける)。
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        bool nearPit = TerrainManager.Instance != null && TerrainManager.Instance.IsNearPit(worldX, pitEnemyClearance);
        float searched = 0f;
        while ((!groundY.HasValue || nearPit) && searched < maxPitAvoidSearch)
        {
            worldX += 0.5f;
            searched += 0.5f;
            groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
            nearPit = TerrainManager.Instance != null && TerrainManager.Instance.IsNearPit(worldX, pitEnemyClearance);
        }
        if (!groundY.HasValue || nearPit) return;

        EnemyDefinition enemyDef = EnemyDatabase.PickRandomUnlocked(enemyPool);
        Sprite eSprite = enemyDef != null ? enemyDef.sprite : (enemySprite != null ? enemySprite : squareSprite);
        Color eColor = enemyDef != null ? enemyDef.tint : (enemySprite != null ? Color.white : new Color(0.9f, 0.15f, 0.15f));
        // Distance Level Design Ver.1, item 4 - wall enemies use the same
        // distance-scaled HP as everything else TerrainManager spawns,
        // instead of staying a permanent 1-hit-kill regardless of distance.
        int wallEnemyHp = DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(enemyDef != null ? enemyDef.hpMultiplier : 1f) : 1;

        // Distance Level Design Ver.1.1 - same facing fix as the main
        // Formation spawn path, for whenever the wall's random pick lands
        // on one of the new species.
        bool enableVisualFacing = enemyDef != null && enemyDef.enableVisualFacing;
        bool defaultFacingRight = enemyDef == null || enemyDef.defaultFacingRight;

        int mileReward = enemyDef != null ? enemyDef.mileReward : 1;

        // 雑魚敵配置修正(2026-09-10) - 「Flying以外は地上の道の上に、Flying
        // は空中どこでもランダムに」。以前はmovementTypeを一切渡していな
        // かったため(常にデフォルトのGround)、Flying種が壁として選ばれて
        // もFlyingの浮遊/追尾Behaviorが付かず、かつ縦に積み上げる位置計算
        // (i*columnSpacing)がGround種にもそのまま適用されて地上専用アニメ
        // ーションの種族が宙に浮いて見える不具合があった。Flying種は
        // TerrainManagerと同じ高さ範囲でランダムな高度に、それ以外は縦積
        // みではなく横に並べて必ず地面に接地させる(見た目・当たり判定と
        // も他のGround種と同じ扱いにする)。
        bool isFlying = enemyDef != null && enemyDef.movementType == EnemyMovementType.Flying;
        EnemyMovementType movementType = isFlying ? EnemyMovementType.Flying : EnemyMovementType.Ground;
        EnemyBehaviorKind behaviorKind = enemyDef != null ? enemyDef.behaviorKind : EnemyBehaviorKind.None;
        float flyMinY = groundY.Value + (TerrainManager.Instance != null ? TerrainManager.Instance.flyingSpawnMinHeight : 1.2f);
        float flyMaxY = groundY.Value + (TerrainManager.Instance != null ? TerrainManager.Instance.flyingSpawnMaxHeight : 4f);

        for (int i = 0; i < columnCount; i++)
        {
            Vector2 pos = isFlying
                ? new Vector2(worldX, Random.Range(flyMinY, flyMaxY))
                : new Vector2(worldX + i * columnSpacing, groundY.Value);
            GameObject wallEnemyGO = GroundFactory.CreateEnemy(transform, eSprite, pos, eColor, squareSprite, movementType: movementType, maxHp: wallEnemyHp, behaviorKind: behaviorKind, enableVisualFacing: enableVisualFacing, defaultFacingRight: defaultFacingRight, runFrames: enemyDef != null ? enemyDef.runFrames : null, mileReward: mileReward, visualScaleMultiplier: enemyDef != null ? enemyDef.visualScaleMultiplier : 1f);
            GroundFactory.ApplyAttackSprite(wallEnemyGO, enemyDef);
        }
    }
}

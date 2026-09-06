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
    public float wallIntervalDebug = 100f;
    public int columnCount = 3;
    public float columnSpacing = 1.6f;
    public float groundClearance = 0.5f;
    public float spawnAheadDistance = 28f;
    // Tolerance (meters) for treating a wall milestone as "the same spot" as
    // the boss's next scheduled encounter.
    public float bossCollisionTolerance = 1f;

    float startX;
    float nextWallDistance;

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextWallDistance = EffectiveWallInterval();
    }

    float EffectiveWallInterval()
    {
        float baseInterval = GameManager.Instance != null && GameManager.Instance.DebugMode ? wallIntervalDebug : wallInterval;
        // "GREED" raises GameManager.EnemySpawnRateMultiplier above 1, which
        // shortens this interval - more frequent walls, as its downside.
        float multiplier = GameManager.Instance != null ? GameManager.Instance.EnemySpawnRateMultiplier : 1f;
        return baseInterval / Mathf.Max(0.1f, multiplier);
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        if (player == null) return;
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;

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

            if (!takenByBoss && !inSafeZone) SpawnWall(nextWallDistance);

            nextWallDistance += EffectiveWallInterval();
        }
    }

    void SpawnWall(float milestoneDistance)
    {
        float worldX = startX + milestoneDistance + spawnAheadDistance;

        // If that exact X lands in a pit, nudge forward in small steps to
        // find solid ground rather than spawning enemies over empty air.
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        float searched = 0f;
        while (!groundY.HasValue && searched < 10f)
        {
            worldX += 0.5f;
            searched += 0.5f;
            groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        }
        if (!groundY.HasValue) return;

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
        for (int i = 0; i < columnCount; i++)
        {
            Vector2 pos = new Vector2(worldX, groundY.Value + groundClearance + i * columnSpacing);
            GroundFactory.CreateEnemy(transform, eSprite, pos, eColor, squareSprite, maxHp: wallEnemyHp, enableVisualFacing: enableVisualFacing, defaultFacingRight: defaultFacingRight, runFrames: enemyDef != null ? enemyDef.runFrames : null, mileReward: mileReward, visualScaleMultiplier: enemyDef != null ? enemyDef.visualScaleMultiplier : 1f);
        }
    }
}

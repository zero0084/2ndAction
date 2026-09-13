using System.Collections.Generic;
using UnityEngine;

// ルート構造再調整(2026-09-13) - 上ルート(Easy)にも申し訳程度の敵を置く、
// ごく軽量な専用スポナー。ObstacleSpawner/EnemyWallManagerと全く同じ
// 「マイルストーン距離に到達するたびに1体だけ置く」パターンをそのまま
// 踏襲した別建てのコンポーネントで、既存のFormation/DistanceTierManager
// (戦闘システムの中核)には一切手を加えていない。TerrainManager.
// IsInBranchRouteがfalseの区間(=上ルートが存在しない区間、天空回廊選択
// 中も含む)では常に何もしない。
public class UpperRouteEnemySpawner : MonoBehaviour
{
    public Transform player;
    public Sprite squareSprite;
    public List<EnemyDefinition> enemyPool = new List<EnemyDefinition>();

    public string stageId = "wasteland_road";
    public float spawnInterval = 24f;
    public float spawnAheadDistance = 28f;

    float startX;
    float nextSpawnDistance;

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextSpawnDistance = spawnInterval;
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        if (player == null) return;
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;
        if (GameManager.Instance.ActiveRunStageId != stageId) return;

        while (GameManager.Instance.MaxDistance >= nextSpawnDistance)
        {
            bool inSafeZone = GameManager.Instance.IsInSafeZone;
            if (!inSafeZone) TrySpawn(nextSpawnDistance);

            nextSpawnDistance += spawnInterval;
        }
    }

    void TrySpawn(float milestoneDistance)
    {
        float worldX = startX + milestoneDistance + spawnAheadDistance;
        if (TerrainManager.Instance == null || !TerrainManager.Instance.IsInBranchRoute(worldX)) return;

        float? skyY = TerrainManager.Instance.GetSkyHeightAt(worldX);
        if (!skyY.HasValue) return;

        EnemyDefinition enemyDef = EnemyDatabase.PickRandomUnlocked(enemyPool);
        if (enemyDef == null) return;

        Sprite eSprite = enemyDef.sprite != null ? enemyDef.sprite : squareSprite;
        int maxHp = DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(enemyDef.hpMultiplier) : 1;
        bool isFlying = enemyDef.movementType == EnemyMovementType.Flying;
        EnemyMovementType movementType = isFlying ? EnemyMovementType.Flying : EnemyMovementType.Ground;
        // Flying種は上ルートの上空に少し余裕を持たせた高さへ、地上種は
        // 上ルートの路面へそのまま接地させる。
        Vector2 pos = isFlying
            ? new Vector2(worldX, skyY.Value + Random.Range(0.8f, 1.6f))
            : new Vector2(worldX, skyY.Value);

        GameObject enemyGO = GroundFactory.CreateEnemy(transform, eSprite, pos, enemyDef.tint, movementType: movementType,
            maxHp: maxHp, behaviorKind: enemyDef.behaviorKind, enableVisualFacing: enemyDef.enableVisualFacing,
            defaultFacingRight: enemyDef.defaultFacingRight, runFrames: enemyDef.runFrames, mileReward: enemyDef.mileReward,
            visualScaleMultiplier: enemyDef.visualScaleMultiplier);

        if (movementType == EnemyMovementType.Ground) enemyGO.transform.rotation = Quaternion.identity;
    }
}

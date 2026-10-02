using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Stage01オブジェクト配置整理依頼(2026-09-17) - 「同じ問題が再発しないよう
// 配置ルールを固定する」に対応する回帰テスト。RouteBranchSelfTest/
// RouteBranchPitSafetySelfTestと同じ「TerrainManager/ObstacleSpawner単体
// をEdit-mode上に生成し、privateメソッドをreflection経由で直接叩く」方針。
//
// Wall/GiantRock(大型障害物)の重みを極端に大きくして、実際の
// ObstacleSpawner.SpawnObstacle/SpawnUpperObstacleのロジックを繰り返し
// 走らせ、生成された実際のGameObject群から以下を検証する:
// (1) 障害物どうしの最低間隔(minGapBetweenObstacles)を下回らない
// (2) 大型障害物の前後は、さらに広い間隔(minGapBeforeLargeObstacle/
//     minGapAfterLargeObstacle)を下回らない
// (3) 大型障害物が分岐(fork)/合流(merge)地点の近く(branchEdgeClearance)
//     に置かれない
// (4) 大型障害物が坂の途中(GetSlopeAngleAt!=0)に置かれない
// (5) 大型障害物が穴の近く(pitObstacleClearanceForLarge)に置かれない
public static class ObstaclePlacementRulesSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Obstacle Placement Rules")]
    public static void Run()
    {
        GameObject terrainGO = new GameObject("ObstaclePlacementSelfTest_Terrain", typeof(TerrainManager));
        TerrainManager terrain = terrainGO.GetComponent<TerrainManager>();

        // ObstacleSpawnerは(実運用どおり)TerrainManager.Instanceという
        // 静的シングルトン経由でTerrainManagerを参照する。通常はAwake()が
        // Play Mode/実機で設定するが、このテストはEdit-mode単発実行
        // (Awake()は走らない)なので、reflectionで直接セットしてやる必要が
        // ある - 怠るとGetHeightAt等の呼び出しがInstance==nullで静かに
        // スキップされ続け、SpawnObstacleが常に無反応になる(実際に一度
        // これで全滅した)。
        PropertyInfo instanceProp = typeof(TerrainManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        instanceProp.SetValue(null, terrain);

        GameObject playerGO = new GameObject("ObstaclePlacementSelfTest_Player");
        terrain.player = playerGO.transform;

        terrain.branchStartDistance = 20f;
        terrain.branchLength = 20f;
        terrain.branchRampLength = 4f;
        terrain.branchSegmentLength = 8f;
        terrain.generateAheadDistance = 10f;
        // Pitも普段どおり発生させ、大型障害物の穴回避ロジックも一緒に
        // 検証する(極端な値にはしない - RouteBranchPitSafetySelfTestの
        // ような統計的極限テストは既に別途ある)。

        InvokePrivate(terrain, "Start");

        terrain.stageThemes = new[]
        {
            new TerrainManager.TerrainThemeSet
            {
                stageId = "test_wasteland",
                platformArt = default,
                groundSprite = null,
                groundColor = Color.white,
                backgroundSprite = null,
                decorationSprites = null,
                enableRouteBranch = true,
                branchMarkerSprite = null,
            }
        };
        terrain.ApplyStageTheme("test_wasteland");

        float forkX = 20f;
        float mergeX = forkX + 4f * 2f + 20f; // = 48 (RouteBranchSelfTestと同じ決定的な式)

        // ObstacleSpawner.Start()はこの時点のplayer.position.xをstartXとして
        // 固定する - 地形生成のためにplayerを先へ動かす前に呼ぶ必要がある
        // (先に動かしてからStart()すると、startXが地形生成側と噛み合わず
        // worldXがIsGenerated()の範囲を超えてSpawnObstacleが常に無反応に
        // なる不具合があった)。
        GameObject spawnerGO = new GameObject("ObstaclePlacementSelfTest_Spawner", typeof(ObstacleSpawner));
        ObstacleSpawner spawner = spawnerGO.GetComponent<ObstacleSpawner>();
        spawner.player = playerGO.transform;
        spawner.squareSprite = null;
        InvokePrivate(spawner, "Start");

        // 分岐区間を跨いで十分先まで地形を生成しておく(SpawnObstacleループ
        // が要求する最遠worldX = startX+(mergeX+80)+spawnAheadDistanceより
        // 先まで)。
        playerGO.transform.position = new Vector3(mergeX + 160f, 0f, 0f);
        for (int i = 0; i < 10; i++) InvokePrivate(terrain, "Update");
        // 大型障害物(Wall/GiantRock)の重みを極端に大きくし、新設した
        // ガード(間隔/分岐/坂/穴)を確実に何度も踏ませる。石/小木/壊せる木
        // もわずかに残す(格下げ先が枯渇しないように)。
        var specs = spawner.specs;
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].name == "Wall" || specs[i].name == "GiantRock") specs[i].weight = 1000f;
            else specs[i].weight = 1f;
        }
        spawner.specs = specs;

        // milestoneDistanceを刻んで、分岐前〜分岐区間〜分岐後を跨いで繰り
        // 返しSpawnObstacle/SpawnUpperObstacleを呼ぶ。ステップは実際の
        // obstacleInterval(既定18f、Danger時10.8f)に近い8fにした - これより
        // 狭いと、1回の呼び出し内の間隔調整(前方への探索)が次の呼び出しの
        // 基準位置を大きく追い越してしまい、後続呼び出しがしばらく
        // 「前回配置に近すぎる」判定のまま空振りし続ける(実際のゲームプレイ
        // では起こらない、テスト特有の人工的な高密度によるカスケード)。
        for (float m = 2f; m <= mergeX + 80f; m += 8f)
        {
            InvokePrivateWithArg(spawner, "SpawnObstacle", m);
            InvokePrivateWithArg(spawner, "SpawnUpperObstacle", m);
        }

        List<(float x, float y, float height)> obstacles = new List<(float x, float y, float height)>();
        foreach (Transform child in spawnerGO.transform)
        {
            if (!child.CompareTag("Obstacle")) continue;
            var col = child.GetComponent<BoxCollider2D>();
            float height = col != null ? col.size.y : 0f;
            obstacles.Add((child.position.x, child.position.y, height));
        }
        obstacles.Sort((a, b) => a.x.CompareTo(b.x));

        const float largeHeightThreshold = 2.0f;
        const float minGapBetween = 6f;
        const float minGapLarge = 9f;
        const float branchEdgeClearance = 5f;
        const float pitClearanceForLarge = 4.5f;

        bool gapOk = true;
        bool branchEdgeOk = true;
        bool slopeOk = true;
        bool pitOk = true;
        int largeCount = 0;

        for (int i = 0; i < obstacles.Count; i++)
        {
            bool isLarge = obstacles[i].height >= largeHeightThreshold;
            if (isLarge)
            {
                largeCount++;
                if (terrain.IsNearBranchEdge(obstacles[i].x, branchEdgeClearance)) branchEdgeOk = false;
                if (Mathf.Abs(terrain.GetSlopeAngleAt(obstacles[i].x)) > 0.01f) slopeOk = false;
                if (terrain.IsNearPit(obstacles[i].x, pitClearanceForLarge)) pitOk = false;
            }

            if (i == 0) continue;
            float gap = obstacles[i].x - obstacles[i - 1].x;
            bool eitherLarge = isLarge || obstacles[i - 1].height >= largeHeightThreshold;
            float requiredGap = eitherLarge ? minGapLarge : minGapBetween;
            // 下ルート/上ルートは別レーン(地面のGetHeightAtと上ルートの
            // GetSkyHeightAtは高さが大きく違う、branchHeightAboveGround
            // 基準で最低2ユニット以上離れる)ため、同じXでも干渉しない -
            // Y座標が近い(同一レーン)隣接ペアだけを間隔チェックの対象に
            // する。
            bool sameLane = Mathf.Abs(obstacles[i].y - obstacles[i - 1].y) < 1.0f;
            if (sameLane && gap < requiredGap - 0.01f) gapOk = false;
        }

        bool pass = gapOk && branchEdgeOk && slopeOk && pitOk && largeCount > 0;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[ObstaclePlacementRulesSelfTest] {result} - totalObstacles={obstacles.Count} largeCount={largeCount} " +
                  $"gapOk={gapOk} branchEdgeOk={branchEdgeOk} slopeOk={slopeOk} pitOk={pitOk}");

        if (!pass)
        {
            Debug.LogError("[ObstaclePlacementRulesSelfTest] FAIL - see individual flags above for which rule was violated.");
        }

        instanceProp.SetValue(null, null);
        Object.DestroyImmediate(spawnerGO);
        Object.DestroyImmediate(terrainGO);
        Object.DestroyImmediate(playerGO);
    }

    static void InvokePrivate(object target, string methodName)
    {
        MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null)
        {
            Debug.LogError($"[ObstaclePlacementRulesSelfTest] Method not found: {methodName}");
            return;
        }
        m.Invoke(target, null);
    }

    static void InvokePrivateWithArg(object target, string methodName, float arg)
    {
        MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null)
        {
            Debug.LogError($"[ObstaclePlacementRulesSelfTest] Method not found: {methodName}");
            return;
        }
        m.Invoke(target, new object[] { arg });
    }
}

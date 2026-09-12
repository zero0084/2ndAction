using UnityEngine;

// Stage01 荒野街道 最小実装(2026-09-13) - 石/小木/壁/壊せる木/巨大石を
// 一定間隔でプレイヤーの前方へ配置する。EnemyWallManagerと全く同じ
// 「マイルストーン距離に到達するたび、生成予定地点がPitの真上でないか
// TerrainManager.GetHeightAtで確認しながら前方へ少しずつ探す」パターン
// (実質そのマイルストーン方式をEnemyからObstacleへ転用したもの) -
// TerrainManager本体のチャンク生成ロジックには一切触れていない。
//
// 専用アートはまだ無いため(マスター指示「まずは仮素材でもよい」に対応)、
// 全種類GroundFactory.CreateObstacleの単色四角プレースホルダーで表現 -
// 種類ごとの見た目差はサイズ/色のみ。将来専用スプライトを用意する際は
// ここのColor/Sprite指定を差し替えるだけで済む。
//
// 穴(Pit)は既存のTerrainManager.ChunkType.Pit(非致死・被弾+安全地点復帰)
// をそのまま使う - このSpawnerが直接手を出すのは新規5種のみ。
public class ObstacleSpawner : MonoBehaviour
{
    public Transform player;
    public Sprite squareSprite;

    // ステージ別ビジュアル差し替え(2026-09-13) - 「いままでの天空マップ
    // (天空回廊)」は無改造で温存する、というマスター指示のため、この
    // Spawnerは荒野街道のRun中だけ稼働する(GameManager.ActiveRunStageId
    // で判定)。天空回廊選択中は障害物を一切配置しない。
    public string obstacleStageId = "wasteland_road";

    public float obstacleInterval = 18f;
    public float spawnAheadDistance = 28f;

    [System.Serializable]
    public struct ObstacleSpec
    {
        public string name;
        public Vector2 size;
        public Color color;
        public bool breakable;
        public int hp;
        public float weight;
    }

    // 「石/小木」は低くジャンプで簡単に越えられる(避けるかどうかの判断
    // 練習)、「壊せる木」はやや高いが攻撃で先に消せる、「壁/巨大石」は
    // 高めで避けなければ確実に被弾する(ObstacleControllerのコメント
    // 参照)。weightは合計100基準の比率(石/小木を主体に、壁/巨大石は
    // たまに、というマスター指示の密度感に合わせた)。
    public ObstacleSpec[] specs = new[]
    {
        new ObstacleSpec { name = "Rock", size = new Vector2(0.8f, 0.6f), color = new Color(0.5f, 0.5f, 0.52f), breakable = false, hp = 1, weight = 30f },
        new ObstacleSpec { name = "SmallTree", size = new Vector2(0.6f, 1.0f), color = new Color(0.42f, 0.28f, 0.16f), breakable = false, hp = 1, weight = 25f },
        new ObstacleSpec { name = "BreakableTree", size = new Vector2(0.7f, 1.3f), color = new Color(0.25f, 0.45f, 0.22f), breakable = true, hp = 2, weight = 20f },
        new ObstacleSpec { name = "Wall", size = new Vector2(0.5f, 1.3f), color = new Color(0.25f, 0.25f, 0.3f), breakable = false, hp = 1, weight = 15f },
        new ObstacleSpec { name = "GiantRock", size = new Vector2(1.3f, 1.5f), color = new Color(0.35f, 0.35f, 0.38f), breakable = false, hp = 1, weight = 10f },
    };

    float startX;
    float nextObstacleDistance;

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextObstacleDistance = obstacleInterval;
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        if (player == null) return;
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;
        if (GameManager.Instance.ActiveRunStageId != obstacleStageId) return;

        while (GameManager.Instance.MaxDistance >= nextObstacleDistance)
        {
            // Run Continuation/Checkpoint Ver.1, item 14と同じ配慮 -
            // CONTINUE直後の安全地帯では新規障害物を置かない(EnemyWallManager
            // のinSafeZoneガードと同じ理由)。
            bool inSafeZone = GameManager.Instance.IsInSafeZone;
            if (!inSafeZone) SpawnObstacle(nextObstacleDistance);

            nextObstacleDistance += obstacleInterval;
        }
    }

    void SpawnObstacle(float milestoneDistance)
    {
        float worldX = startX + milestoneDistance + spawnAheadDistance;

        // Pitの真上に置かないよう、EnemyWallManager.SpawnWallと同じ探索。
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        float searched = 0f;
        while (!groundY.HasValue && searched < 10f)
        {
            worldX += 0.5f;
            searched += 0.5f;
            groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        }
        if (!groundY.HasValue) return;

        ObstacleSpec spec = PickWeightedSpec();
        if (string.IsNullOrEmpty(spec.name)) return;

        GroundFactory.CreateObstacle(transform, squareSprite, new Vector2(worldX, groundY.Value), spec.size, spec.color, spec.breakable, spec.hp);
    }

    ObstacleSpec PickWeightedSpec()
    {
        float total = 0f;
        foreach (ObstacleSpec s in specs) total += Mathf.Max(0f, s.weight);
        if (total <= 0f) return default;

        float roll = Random.value * total;
        float acc = 0f;
        foreach (ObstacleSpec s in specs)
        {
            acc += Mathf.Max(0f, s.weight);
            if (roll <= acc) return s;
        }
        return specs[specs.Length - 1];
    }
}

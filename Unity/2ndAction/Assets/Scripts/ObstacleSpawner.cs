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

    // ルート構造再調整(2026-09-13) - 下ルート(Danger)側の危険度ブースト。
    // TerrainManager.IsInBranchRouteがtrueの区間(=分岐中)だけ、間隔を
    // 縮めて(より頻繁に)、かつ壁/巨大石/壊せる木の出現率を上げる。
    // 分岐が無い区間(routeBranchEnabled=false、または分岐と分岐の間)
    // では従来と完全に同じ密度・比率のまま。
    public float dangerIntervalMultiplier = 0.6f;
    public float dangerHeavyWeightMultiplier = 1.8f;

    // ルート構造再調整(2026-09-13) - 上ルート(Easy)専用の軽い障害物配置。
    // 分岐区間の中でだけ、間隔を大きく(疎に)取り、石/小木のみをTerrain
    // Manager.GetSkyHeightAtの高さへ置く。分岐が無い区間では上ルート自体
    // が存在しない(GetSkyHeightAtがnullを返す)ので何も置かれない。
    public float upperObstacleInterval = 26f;
    float nextUpperObstacleDistance;

    [System.Serializable]
    public struct ObstacleSpec
    {
        public string name;
        // Stage01 荒野街道 完成版素材(2026-09-13) - sprite未設定(null)の
        // 場合のみ、GroundFactory.CreateObstacleがsquareSprite+colorの
        // 単色四角プレースホルダーへフォールバックする(専用アート未生成の
        // 別ステージ/テストシーンでも壊れないようにするための保険)。
        // 本番のwasteland_road用specsはSceneBuilderが実スプライトを
        // ロードして上書きする。
        public Sprite sprite;
        public float targetHeight;
        public Color color;
        public bool breakable;
        public int hp;
        public float weight;
    }

    // 「石/小木」は低くジャンプで簡単に越えられる(避けるかどうかの判断
    // 練習)、「壊せる木」はやや高いが攻撃で先に消せる、「壁/巨大石」は
    // 高めで避けなければ確実に被弾する(ObstacleControllerのコメント
    // 参照)。weightは合計100基準の比率(石/小木を主体に、壁/巨大石は
    // たまに、というマスター指示の密度感に合わせた)。targetHeightは
    // ゴブリンの実効表示高さ(~1.41 world units)を基準にした完成版
    // 要求仕様書の相対サイズ(石70%/小木90%/壁150%/壊せる木100%/
    // 巨大石200%+)に合わせて調整済み - spriteがSceneBuilderで
    // 差し替えられなかった場合のみ使われるフォールバック値。
    public ObstacleSpec[] specs = new[]
    {
        new ObstacleSpec { name = "Rock", sprite = null, targetHeight = 1.0f, color = new Color(0.5f, 0.5f, 0.52f), breakable = false, hp = 1, weight = 30f },
        new ObstacleSpec { name = "SmallTree", sprite = null, targetHeight = 1.25f, color = new Color(0.42f, 0.28f, 0.16f), breakable = false, hp = 1, weight = 25f },
        new ObstacleSpec { name = "BreakableTree", sprite = null, targetHeight = 1.4f, color = new Color(0.25f, 0.45f, 0.22f), breakable = true, hp = 2, weight = 20f },
        new ObstacleSpec { name = "Wall", sprite = null, targetHeight = 2.1f, color = new Color(0.25f, 0.25f, 0.3f), breakable = false, hp = 1, weight = 15f },
        new ObstacleSpec { name = "GiantRock", sprite = null, targetHeight = 2.8f, color = new Color(0.35f, 0.35f, 0.38f), breakable = false, hp = 1, weight = 10f },
    };

    float startX;
    float nextObstacleDistance;

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextObstacleDistance = obstacleInterval;
        nextUpperObstacleDistance = upperObstacleInterval;
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
            float milestoneDistance = nextObstacleDistance;
            if (!inSafeZone) SpawnObstacle(milestoneDistance);

            // ルート構造再調整(2026-09-13) - この地点が分岐中の下ルート
            // (Danger)なら、次のマイルストーンまでの間隔を縮めてより頻繁に
            // 障害物を置く。分岐が無い区間では従来どおりobstacleIntervalの
            // まま。
            float worldX = startX + milestoneDistance + spawnAheadDistance;
            bool danger = TerrainManager.Instance != null && TerrainManager.Instance.IsInBranchRoute(worldX);
            nextObstacleDistance += danger ? obstacleInterval * dangerIntervalMultiplier : obstacleInterval;
        }

        // ルート構造再調整(2026-09-13) - 上ルート(Easy)専用の軽い障害物。
        // 分岐が存在しない区間ではSpawnUpperObstacle内で即スキップされる
        // (=何も置かれない)ので、このループ自体は常時回っていて問題ない。
        while (GameManager.Instance.MaxDistance >= nextUpperObstacleDistance)
        {
            bool inSafeZone = GameManager.Instance.IsInSafeZone;
            if (!inSafeZone) SpawnUpperObstacle(nextUpperObstacleDistance);

            nextUpperObstacleDistance += upperObstacleInterval;
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

        bool danger = TerrainManager.Instance != null && TerrainManager.Instance.IsInBranchRoute(worldX);
        ObstacleSpec spec = PickWeightedSpec(danger);
        if (string.IsNullOrEmpty(spec.name)) return;

        GroundFactory.CreateObstacle(transform, squareSprite, spec.sprite, new Vector2(worldX, groundY.Value), spec.targetHeight, spec.color, spec.breakable, spec.hp);
    }

    // ルート構造再調整(2026-09-13) - 上ルート(Easy)側。TerrainManager.
    // GetSkyHeightAt(=分岐区間の上ルート面)が存在する場所にだけ、石/
    // 小木のみを置く。壁/巨大石/壊せる木は上ルートには一切出さない
    // (マスター指示「上ルート：壁/巨大石は基本少なめ」に対応、今回は
    // ゼロにして安全側に倒した)。
    void SpawnUpperObstacle(float milestoneDistance)
    {
        float worldX = startX + milestoneDistance + spawnAheadDistance;
        if (TerrainManager.Instance == null || !TerrainManager.Instance.IsInBranchRoute(worldX)) return;

        float? skyY = TerrainManager.Instance.GetSkyHeightAt(worldX);
        if (!skyY.HasValue) return;

        ObstacleSpec spec = PickEasySpec();
        if (string.IsNullOrEmpty(spec.name)) return;

        GroundFactory.CreateObstacle(transform, squareSprite, spec.sprite, new Vector2(worldX, skyY.Value), spec.targetHeight, spec.color, spec.breakable, spec.hp);
    }

    ObstacleSpec PickEasySpec()
    {
        float total = 0f;
        foreach (ObstacleSpec s in specs)
        {
            if (s.name != "Rock" && s.name != "SmallTree") continue;
            total += Mathf.Max(0f, s.weight);
        }
        if (total <= 0f) return default;

        float roll = Random.value * total;
        float acc = 0f;
        foreach (ObstacleSpec s in specs)
        {
            if (s.name != "Rock" && s.name != "SmallTree") continue;
            acc += Mathf.Max(0f, s.weight);
            if (roll <= acc) return s;
        }
        return default;
    }

    // dangerBoost中は壁/巨大石/壊せる木の重みを底上げして「下ルートは
    // やや多め・強め」を表現する(石/小木の重みはそのまま=既存の密度感を
    // 維持しつつ、危険物の割合だけ増やす)。
    ObstacleSpec PickWeightedSpec(bool dangerBoost)
    {
        float total = 0f;
        foreach (ObstacleSpec s in specs) total += EffectiveWeight(s, dangerBoost);
        if (total <= 0f) return default;

        float roll = Random.value * total;
        float acc = 0f;
        foreach (ObstacleSpec s in specs)
        {
            acc += EffectiveWeight(s, dangerBoost);
            if (roll <= acc) return s;
        }
        return specs[specs.Length - 1];
    }

    float EffectiveWeight(ObstacleSpec s, bool dangerBoost)
    {
        float w = Mathf.Max(0f, s.weight);
        if (dangerBoost && (s.name == "Wall" || s.name == "GiantRock" || s.name == "BreakableTree")) w *= dangerHeavyWeightMultiplier;
        return w;
    }
}

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
    // 共通Encounter System(2026-09-27) - 敵のEncounterの範囲(+この余白)には障害物を置かない
    // (敵と障害物が重なって「必ず被弾する」組み合わせを作らない)。Encounterを使わないステージでは何もしない。
    const float EncounterClear = 2.5f;
    // Stage01次段階調整(2026-09-16), item2/3 - 穴(Pit)の縁からこの距離
    // 以内には障害物を置かない(「穴の上」だけでなく「穴の直前/直後」も
    // 避ける)。TerrainManager.IsNearPit参照。pitReactionBuffer(2.5f、
    // 落下後の安全復帰位置)と同程度の「見てから反応できる間合い」を狙った
    // 値。
    public float pitObstacleClearance = 2.5f;
    // 穴+穴回避の探索を合わせても十分抜けられるよう、既存の10fから拡大。
    public float maxPitAvoidSearch = 16f;

    // ルート構造再調整(2026-09-13) - 下ルート(Danger)側の危険度ブースト。
    // TerrainManager.IsInBranchRouteがtrueの区間(=分岐中)だけ、間隔を
    // 縮めて(より頻繁に)、かつ壁/巨大石/壊せる木の出現率を上げる。
    // 分岐が無い区間(routeBranchEnabled=false、または分岐と分岐の間)
    // では従来と完全に同じ密度・比率のまま。
    public float dangerIntervalMultiplier = 0.6f;
    // Stage01仕上げ調整(2026-09-13深夜) - マスター指摘「下ルートの危険が
    // 敵の過密に寄りすぎている」に対応し、TerrainManager.branchDanger
    // EnemyMultiplierを弱めた分、こちらを1.8→2.4へ引き上げて「敵+障害物
    // +穴の複合」で危険度を作るバランスへ調整。
    public float dangerHeavyWeightMultiplier = 2.4f;

    // Stage01オブジェクト配置整理依頼(2026-09-17) - 「回避不能配置の撤去 +
    // 配置ルール固定」。既存のIsNearPitチェックだけでは、(a)障害物どうし
    // の間隔(前の障害物との実際の距離)が一切見られていない、(b)分岐
    // (fork)/合流(merge)地点の近くにも普通に置かれ得る、(c)壁/巨大石が
    // 坂の途中に置かれ得る、という3つの穴があり、「連続配置」「分岐地点
    // に大型障害物」「坂の途中の巨大石」といった回避不能配置の原因になって
    // いた。これらを毎回の配置判定に固定ルールとして追加する。
    //
    // minGapBetweenObstacles: 障害物どうしの最低間隔(種類を問わず)。
    // minGapBeforeLargeObstacle/AfterLargeObstacle: 壁/巨大石(GiantRock/
    // Wall)の前後だけ、より広い間隔を要求する - 大型障害物は「見て避ける
    // 大きな障害」であるべきで、直前直後に別の障害物が詰まっていると
    // 反応時間が無くなる(依頼書item4)。
    // branchEdgeClearance: 分岐/合流地点(fork/merge)からこの距離以内には
    // 大型障害物も含め障害物を一切置かない(依頼書item3「ルート選択区間」)。
    // pitObstacleClearanceForLarge: 穴の近くでは大型障害物により大きな
    // 安全マージンを要求する(依頼書item2)。
    // 大型障害物がこれらの条件を満たせない場合は、配置自体を諦めるのでは
    // なく石/小木/壊せる木へ格下げする(密度は維持しつつ「詰む」配置だけ
    // を排除する)。
    public float minGapBetweenObstacles = 6f;
    public float minGapBeforeLargeObstacle = 9f;
    public float minGapAfterLargeObstacle = 9f;
    public float branchEdgeClearance = 5f;
    public float pitObstacleClearanceForLarge = 4.5f;
    // 洞窟専用: 大型障害物(壁/巨大石)を置くのに必要な、地面から天井までの最小高さ。
    public float largeObstacleMinCeilingClearance = 5.6f;
    // 洞窟専用: 障害物の上面から天井(針があれば針の先端)までに残す最低間隔。これを満たせない高さの障害物は
    // より低いものへ格下げし、それでも満たせなければ置かない(低い天井の下で通路を塞がない)。
    public float caveObstaclePassClearance = 3.0f;

    // 置いた障害物の見た目の上端(描画範囲)。取れなければ指定の高さ。
    static float RenderTop(GameObject obstacle, float fallbackTop)
    {
        float top = float.NegativeInfinity;
        foreach (var r in obstacle.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
        return float.IsNegativeInfinity(top) ? fallbackTop : top;
    }

    // 同じフレームのうちに当たり判定も見た目も消す(Destroyはフレーム末まで残るため先に非表示にする)。
    static void DestroyObstacleNow(GameObject obstacle)
    {
        obstacle.SetActive(false);
        Destroy(obstacle);
    }

    // 洞窟が有効なら、worldXの前後(障害物の幅ぶん)まで天井が生成済みか。洞窟でなければ常にtrue。
    bool CaveCeilingKnown(float worldX)
    {
        TerrainManager tm = TerrainManager.Instance;
        if (tm == null || tm.cave == null || !tm.cave.Active) return true;
        return tm.cave.GeneratedEndX >= worldX + 2.5f;
    }

    // 位置worldX付近(障害物の幅ぶん)の通れる天井の最低の高さ。洞窟でなければnull。
    float? CaveEffectiveCeiling(float worldX)
    {
        if (TerrainManager.Instance == null) return null;
        float? best = null;
        for (float dx = -2f; dx <= 2.001f; dx += 0.5f) // 大きい障害物(幅~4m)の端の低い天井/針も含める
        {
            float? c = TerrainManager.Instance.GetEffectiveCeilingHeightAt(worldX + dx);
            if (c.HasValue) best = best.HasValue ? Mathf.Min(best.Value, c.Value) : c.Value;
        }
        return best;
    }

    ObstacleSpec PickLowestSpec()
    {
        ObstacleSpec best = default;
        float h = float.MaxValue;
        foreach (ObstacleSpec s in specs)
            if (!string.IsNullOrEmpty(s.name) && s.targetHeight < h) { h = s.targetHeight; best = s; }
        return best;
    }

    float lastLowerObstacleX = float.NegativeInfinity;
    bool lastLowerObstacleWasLarge;
    float lastUpperObstacleX = float.NegativeInfinity;

    static bool IsLarge(ObstacleSpec s) => s.name == "Wall" || s.name == "GiantRock";

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

    // 高速走行の視認性補正(2026-09-22) - 配置間隔を「メートル固定」ではなく「反応時間」基準にする。
    // Auto Run速度が基礎の何倍かをGapScaleとし、障害物の間隔/最低間隔/穴・分岐からの余裕を同じ倍率で
    // 広げる(=速く走っていても、次の危険が見えてから反応できる秒数は変わらない)。
    // 速度そのものは落とさない。倍率1(基礎速度)では従来と完全に同じ値。
    public float highSpeedHazardFocusMargin = 6f; // 高速時、大型障害物を穴からさらに離す量(倍率-1あたり)
    float GapScale => PlayerController.Instance != null ? Mathf.Max(1f, PlayerController.Instance.SpeedRatio) : 1f;
    float MinGapBetween => minGapBetweenObstacles * GapScale;
    float MinGapBeforeLarge => minGapBeforeLargeObstacle * GapScale;
    float MinGapAfterLarge => minGapAfterLargeObstacle * GapScale;
    float PitClear => pitObstacleClearance * GapScale;
    float PitClearLarge => pitObstacleClearanceForLarge * GapScale + highSpeedHazardFocusMargin * (GapScale - 1f);
    float BranchEdgeClear => branchEdgeClearance * GapScale;

    void OnEnable()
    {
        FloatingOrigin.Shifted += OnOriginShifted;
        FloatingOrigin.Warped += OnOriginWarped;
    }

    void OnDisable()
    {
        FloatingOrigin.Shifted -= OnOriginShifted;
        FloatingOrigin.Warped -= OnOriginWarped;
    }

    void OnOriginShifted(float s)
    {
        startX -= s;
        if (!float.IsNegativeInfinity(lastLowerObstacleX)) lastLowerObstacleX -= s;
        if (!float.IsNegativeInfinity(lastUpperObstacleX)) lastUpperObstacleX -= s;
    }

    // デバッグワープ: MaxDistanceが飛ぶので、次の配置マイルストーンも現在距離まで進める(過去位置への大量配置を防ぐ)。
    void OnOriginWarped(float d)
    {
        startX -= d;
        float md = GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;
        nextObstacleDistance = Mathf.Max(nextObstacleDistance, md);
        nextUpperObstacleDistance = Mathf.Max(nextUpperObstacleDistance, md);
    }

    void Start()
    {
        startX = player != null ? player.position.x : 0f;
        nextObstacleDistance = obstacleInterval;
        nextUpperObstacleDistance = upperObstacleInterval;
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        if (GameManager.Instance.CountdownActive) return; // Stage01地形挙動修整(2026-09-17), item4
        if (player == null) return;
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;
        if (BonusZone.SuppressesNormalSpawns) return; // BONUS ZONE中は通常の敵/障害物を出さない
        if (GameManager.Instance.ActiveRunStageId != obstacleStageId) return;
        if (NetObstacles.SuppressLocalSpawn) return; // マルチのJOIN: 障害物はHOSTが置いて共有する(NetObstacles)
        // マルチのHOST: 先頭のプレイヤー(JOINが前にいることもある)を基準に置く(地形の先行生成と同じ。JOINの目の前に突然出さない)
        float md = GameManager.Instance.MaxDistance;
        if (NetObstacles.Authority) md += Mathf.Max(0f, NetCombat.ForemostPlayerX(player.position.x) - player.position.x);

        while (md >= nextObstacleDistance)
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
            float worldX = startX + milestoneDistance + HighSpeedAssist.SpawnAhead(spawnAheadDistance); // 高速時の自動操作補助: 先読み範囲に置く
            bool danger = TerrainManager.Instance != null && TerrainManager.Instance.IsInBranchRoute(worldX);
            nextObstacleDistance += (danger ? obstacleInterval * dangerIntervalMultiplier : obstacleInterval) * GapScale;
        }

        // ルート構造再調整(2026-09-13) - 上ルート(Easy)専用の軽い障害物。
        // 分岐が存在しない区間ではSpawnUpperObstacle内で即スキップされる
        // (=何も置かれない)ので、このループ自体は常時回っていて問題ない。
        while (md >= nextUpperObstacleDistance)
        {
            bool inSafeZone = GameManager.Instance.IsInSafeZone;
            if (!inSafeZone) SpawnUpperObstacle(nextUpperObstacleDistance);

            nextUpperObstacleDistance += upperObstacleInterval * GapScale;
        }
    }

    void SpawnObstacle(float milestoneDistance)
    {
        WorldRng.Obstacle.ReseedAt(Mathf.RoundToInt(milestoneDistance * 4f)); // 固定シード時: 同じ地点には同じ種類
        float worldX = startX + milestoneDistance + HighSpeedAssist.SpawnAhead(spawnAheadDistance); // 高速時の自動操作補助: 先読み範囲に置く

        // Stage01次段階調整(2026-09-16) - TerrainManager側の生成がまだ
        // worldXまで届いていない場合、GetHeightAtは「実際に後で生成される
        // 地形」とは無関係な末尾チャンクの高さへ静かにフォールバックして
        // しまう(TerrainManager.generateAheadDistanceのコメント参照)。
        // 通常はgenerateAheadDistanceに十分な余裕を持たせてあるので発生
        // しないはずだが、二重の安全策としてここでも確認し、まだなら今回
        // は諦める(次のマイルストーンで再挑戦されるので恒久的に消えは
        // しない)。
        if (TerrainManager.Instance != null && !TerrainManager.Instance.IsGenerated(worldX)) return;

        // Pitの真上・Pitの縁からpitObstacleClearance以内、分岐/合流地点
        // からbranchEdgeClearance以内、前の障害物からminGapBetween
        // Obstacles未満の場所には置かない(item2「穴の直前/直後」、item3
        // 「分岐/合流地点」、item4「連続配置しない」)。いずれも満たすまで
        // 少しずつ前方へ探す - 既存のPit回避探索と同じ仕組みを拡張した。
        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
        bool nearPit = TerrainManager.Instance != null && TerrainManager.Instance.IsNearPit(worldX, PitClear);
        bool nearBranchEdge = TerrainManager.Instance != null && TerrainManager.Instance.IsNearBranchEdge(worldX, BranchEdgeClear) || EncounterDirector.IsInEncounterSpan(worldX, EncounterClear);
        bool tooCloseToLast = (worldX - lastLowerObstacleX) < MinGapBetween;
        float searched = 0f;
        while ((!groundY.HasValue || nearPit || nearBranchEdge || tooCloseToLast) && searched < maxPitAvoidSearch)
        {
            worldX += 0.5f;
            searched += 0.5f;
            groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(worldX) : null;
            nearPit = TerrainManager.Instance != null && TerrainManager.Instance.IsNearPit(worldX, PitClear);
            nearBranchEdge = TerrainManager.Instance != null && TerrainManager.Instance.IsNearBranchEdge(worldX, BranchEdgeClear) || EncounterDirector.IsInEncounterSpan(worldX, EncounterClear);
            tooCloseToLast = (worldX - lastLowerObstacleX) < MinGapBetween;
        }
        if (!groundY.HasValue || nearPit || nearBranchEdge || tooCloseToLast) return;
        // 洞窟: その場所の天井がまだ決まっていない(生成前)なら置かない(天井の高さを確かめられないまま
        // 置くと、後から低い天井が来て通路を塞ぐ)。地形の未生成と同じ扱い(次のマイルストーンで再挑戦)。
        if (!CaveCeilingKnown(worldX)) return;

        bool danger = TerrainManager.Instance != null && TerrainManager.Instance.IsInBranchRoute(worldX);
        // item3 - 穴のすぐ近くでは、既にnearPitチェックで確保した間合いに
        // 加えて、壁/巨大石のような重い障害物の出現率ブースト自体も掛けな
        // い(「穴の直前:大型障害物を置きすぎない」)。石/小木/壊せる木は
        // 通常どおり出現し得る。
        ObstacleSpec spec = PickWeightedSpec(danger && !nearPit);
        if (string.IsNullOrEmpty(spec.name)) return;

        // item2/4 - 大型障害物(壁/巨大石)だけは、さらに厳しい条件
        // (前後の広い間隔・穴からの余裕・坂の途中でないこと)を満たさない
        // 限り採用しない。満たせない場合は配置自体をやめるのではなく
        // 石/小木/壊せる木へ格下げする(密度は保ちつつ詰む配置だけを除く)。
        if (IsLarge(spec))
        {
            bool onSlope = TerrainManager.Instance != null && Mathf.Abs(TerrainManager.Instance.GetSlopeAngleAt(worldX)) > 0.01f;
            bool gapBeforeOk = (worldX - lastLowerObstacleX) >= MinGapBeforeLarge;
            bool gapAfterPrevLargeOk = !lastLowerObstacleWasLarge || (worldX - lastLowerObstacleX) >= MinGapAfterLarge;
            bool pitOk = TerrainManager.Instance == null || !TerrainManager.Instance.IsNearPit(worldX, PitClearLarge);
            // 自然洞窟(2026-09-21) - 低い天井の下では大型障害物を置かない(2段ジャンプの
            // 頭上が天井に当たって越えにくくなるため)。洞窟以外ではceilがnullで常にfalse。
            bool lowCeiling = false;
            if (TerrainManager.Instance != null)
            {
                float? ceil = TerrainManager.Instance.GetCeilingHeightAt(worldX);
                if (ceil.HasValue) lowCeiling = ceil.Value - groundY.Value < largeObstacleMinCeilingClearance;
            }
            if (onSlope || !gapBeforeOk || !gapAfterPrevLargeOk || !pitOk || lowCeiling)
            {
                spec = PickSmallOnlySpec();
                if (string.IsNullOrEmpty(spec.name)) return;
            }
        }

        // 洞窟: 天井(針先)までの間隔が足りない高さの障害物は使わない。
        float? capCeil = CaveEffectiveCeiling(worldX);
        if (capCeil.HasValue && capCeil.Value - (groundY.Value + spec.targetHeight) < caveObstaclePassClearance)
        {
            spec = PickLowestSpec();
            if (string.IsNullOrEmpty(spec.name) || capCeil.Value - (groundY.Value + spec.targetHeight) < caveObstaclePassClearance) return;
        }

        // 基礎品質修整(2026-09-14) - 坂の上でも障害物が地面の傾きに沿って
        // 自然に見えるよう、その場所の地面角度を取得して渡す。
        float groundAngle = TerrainManager.Instance != null ? TerrainManager.Instance.GetSlopeAngleAt(worldX) : 0f;
        GameObject obstacle = GroundFactory.CreateObstacle(transform, squareSprite, spec.sprite, new Vector2(worldX, groundY.Value), spec.targetHeight, spec.color, spec.breakable, spec.hp, groundAngle);
        // 洞窟(2026-09-27): 障害物の絵はtargetHeightより少し高く描かれる(台座/余白)ため、実際に置いた見た目の
        // 上端で天井(針先)までの間隔を確かめ直す。足りなければ一番低い障害物に替え、それでも足りなければ置かない。
        if (capCeil.HasValue && obstacle != null && capCeil.Value - RenderTop(obstacle, groundY.Value + spec.targetHeight) < caveObstaclePassClearance)
        {
            DestroyObstacleNow(obstacle);
            obstacle = null;
            ObstacleSpec lowest = PickLowestSpec();
            if (string.IsNullOrEmpty(lowest.name) || lowest.name == spec.name) return;
            obstacle = GroundFactory.CreateObstacle(transform, squareSprite, lowest.sprite, new Vector2(worldX, groundY.Value), lowest.targetHeight, lowest.color, lowest.breakable, lowest.hp, groundAngle);
            if (obstacle != null && capCeil.Value - RenderTop(obstacle, groundY.Value + lowest.targetHeight) < caveObstaclePassClearance) { DestroyObstacleNow(obstacle); return; }
            spec = lowest;
        }

        Register(obstacle, spec, groundAngle, false);
        lastLowerObstacleX = worldX;
        lastLowerObstacleWasLarge = IsLarge(spec);
    }

    // item4/5 - 大型障害物を格下げする際の代わり(石/小木/壊せる木のみ)。
    // PickEasySpecは石/小木しか選ばない(上ルート専用)ため、壊せる木を
    // 含むこの専用の絞り込みを別に用意した。
    ObstacleSpec PickSmallOnlySpec()
    {
        float total = 0f;
        foreach (ObstacleSpec s in specs)
        {
            if (IsLarge(s)) continue;
            total += Mathf.Max(0f, s.weight);
        }
        if (total <= 0f) return default;

        float roll = WorldRng.Obstacle.Value * total;
        float acc = 0f;
        foreach (ObstacleSpec s in specs)
        {
            if (IsLarge(s)) continue;
            acc += Mathf.Max(0f, s.weight);
            if (roll <= acc) return s;
        }
        return default;
    }

    // ルート構造再調整(2026-09-13) - 上ルート(Easy)側。TerrainManager.
    // GetSkyHeightAt(=分岐区間の上ルート面)が存在する場所にだけ、石/
    // 小木のみを置く。壁/巨大石/壊せる木は上ルートには一切出さない
    // (マスター指示「上ルート：壁/巨大石は基本少なめ」に対応、今回は
    // ゼロにして安全側に倒した)。
    void SpawnUpperObstacle(float milestoneDistance)
    {
        WorldRng.Obstacle.ReseedAt(Mathf.RoundToInt(milestoneDistance * 4f) ^ 0x40000000);
        float worldX = startX + milestoneDistance + HighSpeedAssist.SpawnAhead(spawnAheadDistance); // 高速時の自動操作補助: 先読み範囲に置く
        if (TerrainManager.Instance == null || !TerrainManager.Instance.IsInBranchRoute(worldX)) return;
        if (!TerrainManager.Instance.IsGenerated(worldX)) return;

        // item3/6 - 上ルートでも、分岐(fork)直後・合流(merge)直前は
        // ジャンプで登り切る/降り切るための空間として空けておく。前の
        // 上ルート障害物からの最低間隔も、下ルートと同じ発想で確保する。
        bool nearBranchEdge = TerrainManager.Instance.IsNearBranchEdge(worldX, BranchEdgeClear) || EncounterDirector.IsInUpperEncounterSpan(worldX, EncounterClear);
        bool tooCloseToLast = (worldX - lastUpperObstacleX) < MinGapBetween;
        float searched = 0f;
        float? skyY = TerrainManager.Instance.GetSkyHeightAt(worldX);
        while ((!skyY.HasValue || nearBranchEdge || tooCloseToLast) && searched < maxPitAvoidSearch)
        {
            worldX += 0.5f;
            searched += 0.5f;
            skyY = TerrainManager.Instance.GetSkyHeightAt(worldX);
            nearBranchEdge = TerrainManager.Instance.IsNearBranchEdge(worldX, BranchEdgeClear) || EncounterDirector.IsInUpperEncounterSpan(worldX, EncounterClear);
            tooCloseToLast = (worldX - lastUpperObstacleX) < MinGapBetween;
        }
        if (!skyY.HasValue || nearBranchEdge || tooCloseToLast) return;

        ObstacleSpec spec = PickEasySpec();
        if (string.IsNullOrEmpty(spec.name)) return;
        // 洞窟: 上ルートの障害物も、上面から天井(針先)までの間隔を確保できなければ置かない。
        float? upCeil = CaveEffectiveCeiling(worldX);
        if (upCeil.HasValue && upCeil.Value - (skyY.Value + spec.targetHeight) < caveObstaclePassClearance) return;

        GameObject upperObstacle = GroundFactory.CreateObstacle(transform, squareSprite, spec.sprite, new Vector2(worldX, skyY.Value), spec.targetHeight, spec.color, spec.breakable, spec.hp);
        Register(upperObstacle, spec, 0f, true);
        lastUpperObstacleX = worldX;
    }

    // 障害物の耐久力(2026-09-29): 置いた障害物に種類ごとの耐久力/素材を設定し、マルチならHOSTが全員へ共有する。
    void Register(GameObject obstacle, ObstacleSpec spec, float angle, bool upper)
    {
        if (obstacle == null) return;
        var oc = obstacle.GetComponent<ObstacleController>();
        if (oc == null) return;
        oc.Setup(spec.name);
        NetObstacles.OnSpawned(oc, obstacleStageId, SpecIndex(spec.name), angle, upper);
    }

    int SpecIndex(string n) { for (int i = 0; i < specs.Length; i++) if (specs[i].name == n) return i; return -1; }

    public static ObstacleSpawner FindForStage(string stageId)
    {
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) if (s.obstacleStageId == stageId) return s;
        return null;
    }

    // マルチのJOIN: HOSTから届いた障害物を同じ見た目/判定で作る(位置/角度はHOSTの確定値)。
    public ObstacleController CreateReplica(int specIndex, Vector2 pos, float angle, bool upper)
    {
        if (specIndex < 0 || specIndex >= specs.Length) return null;
        var spec = specs[specIndex];
        var go = GroundFactory.CreateObstacle(transform, squareSprite, spec.sprite, pos, spec.targetHeight, spec.color, spec.breakable, spec.hp, angle);
        if (go == null) return null;
        var oc = go.GetComponent<ObstacleController>();
        oc.Setup(spec.name);
        return oc;
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

        float roll = WorldRng.Obstacle.Value * total;
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

        float roll = WorldRng.Obstacle.Value * total;
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

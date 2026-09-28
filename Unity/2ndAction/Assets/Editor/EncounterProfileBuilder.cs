using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 共通Encounter System(2026-09-27) - 初期データの作成。
//  Assets/Resources/Encounters/GenericFormations.asset   … 全ステージ共通のFormation
//  Assets/Resources/Encounters/Profile_natural_cave.asset … 自然洞窟のProfile(一本道・距離Band・洞窟専用Formation)
// 通常の「Build」は既存のアセットを上書きしない(Inspectorでの調整を消さない)。
// 初期値へ戻したい時だけ「Rebuild (overwrite)」を使う。
public static class EncounterProfileBuilder
{
    const string Dir = "Assets/Resources/Encounters";

    [MenuItem("Tools/2ndAction/Encounter/Build Encounter Profiles (keep existing)")]
    public static void Build() => BuildAll(false);

    [MenuItem("Tools/2ndAction/Encounter/Rebuild Encounter Profiles (overwrite)")]
    public static void BuildForce() => BuildAll(true);

    // 荒野街道のProfileだけ初期値へ作り直す(自然洞窟/汎用Formationには触れない)。
    [MenuItem("Tools/2ndAction/Encounter/Rebuild Wasteland Profile (overwrite)")]
    public static void BuildWastelandForce()
    {
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        Save(Dir + "/Profile_wasteland_road.asset", BuildWasteland(), true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void BuildAll(bool overwrite)
    {
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        Save(Dir + "/GenericFormations.asset", BuildLibrary(), overwrite);
        Save(Dir + "/Profile_natural_cave.asset", BuildNaturalCave(), overwrite);
        Save(Dir + "/Profile_wasteland_road.asset", BuildWasteland(), overwrite);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("EncounterProfileBuilder: done (overwrite=" + overwrite + ")");
    }

    static void Save(string path, ScriptableObject so, bool overwrite)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (existing != null && !overwrite) { Debug.Log("EncounterProfileBuilder: keep " + path); return; }
        if (existing != null)
        {
            EditorUtility.CopySerialized(so, existing);
            EditorUtility.SetDirty(existing);
            Debug.Log("EncounterProfileBuilder: overwrote " + path);
            return;
        }
        AssetDatabase.CreateAsset(so, path);
        Debug.Log("EncounterProfileBuilder: created " + path);
    }

    static EncounterSlot S(EncounterSlotKind k, float x, EncounterIntensity min = EncounterIntensity.Easy, float jitter = 0.3f, string[] prefer = null, bool preferOnly = false)
        => new EncounterSlot { kind = k, xOffset = x, minIntensity = min, jitter = jitter, preferEnemyIds = prefer ?? new string[0], preferOnly = preferOnly };

    const EncounterSlotKind GF = EncounterSlotKind.GroundFront, GM = EncounterSlotKind.GroundMiddle, GR = EncounterSlotKind.GroundRear;
    const EncounterSlotKind AL = EncounterSlotKind.AirLow, AH = EncounterSlotKind.AirHigh, BU = EncounterSlotKind.Burrow;
    const EncounterIntensity E = EncounterIntensity.Easy, M = EncounterIntensity.Medium, H = EncounterIntensity.Hard;

    // ===================================================================== //
    // 汎用Formation(全ステージ)
    // ===================================================================== //
    static EncounterFormationLibrary BuildLibrary()
    {
        var lib = ScriptableObject.CreateInstance<EncounterFormationLibrary>();
        var list = lib.formations;

        // Ground Line: 地上の敵を進行方向へ2〜4体。連続攻撃/コンボで斬り抜ける基本形。
        list.Add(new EncounterFormation
        {
            formationId = "ground_line", displayName = "Ground Line (Ant Line)",
            slots = { S(GF, 0f), S(GM, 2.3f), S(GM, 4.6f, M), S(GR, 6.9f, H) },
            requiredHeight = 3.0f, requiresGround = true, continuousGround = true, minGroundGap = 1.6f,
            narrowAffinity = 1.6f, wideAffinity = 0.9f, straightAffinity = 1.1f,
        });
        // Ground Cluster: 狭い範囲に3〜5体(重ならない最低間隔あり)。打ち上げ/空中コンボ/叩きつけを複数に。
        list.Add(new EncounterFormation
        {
            formationId = "ground_cluster", displayName = "Ground Cluster (Ant Colony)",
            slots = { S(GM, 0f, E, 0.2f), S(GM, 1.4f, E, 0.2f), S(GM, 2.8f, E, 0.2f), S(GM, 4.2f, M, 0.2f), S(GR, 5.6f, H, 0.2f) },
            requiredHeight = 4.4f, requiresGround = true, continuousGround = true, requiresFlat = true, minGroundGap = 1.25f,
            narrowAffinity = 0.5f, wideAffinity = 1.6f, straightAffinity = 1f,
        });
        // Staggered: 少しずつ間隔を空けて「攻撃→攻撃→攻撃」のリズム。強い時は数と間の詰まりが増える。
        list.Add(new EncounterFormation
        {
            formationId = "staggered", displayName = "Staggered",
            slots = { S(GF, 0f, E, 0.6f), S(GM, 5.5f, E, 0.6f), S(GM, 11f, E, 0.6f), S(GR, 16.5f, M, 0.6f), S(GR, 22f, H, 0.6f) },
            requiredHeight = 3.0f, requiresGround = true, minGroundGap = 3f,
            narrowAffinity = 1.4f, wideAffinity = 1f, straightAffinity = 1.3f,
        });
        // Ground + Air: 地上の敵は必ず地上、空中Slotには飛ぶ敵だけ。上攻撃/ジャンプ/空中攻撃の機会。
        list.Add(new EncounterFormation
        {
            formationId = "ground_air", displayName = "Ground + Air (Ground + Bat)",
            slots = { S(GF, 0f), S(AL, 4.5f), S(GM, 2.4f, M), S(AH, 7.5f, H), S(GR, 6f, H) },
            requiredHeight = 5.6f, requiresGround = true, continuousGround = true, requiresAir = true, minGroundGap = 1.6f,
            narrowAffinity = 0f, wideAffinity = 1.7f, straightAffinity = 1f,
        });
        // Air Swarm: 飛ぶ敵を2〜3体。画面を埋めず、位置/予兆が見える密度。
        list.Add(new EncounterFormation
        {
            formationId = "air_swarm", displayName = "Air Swarm (Bat Swarm)",
            slots = { S(AL, 0f, E, 0.5f), S(AH, 4f, E, 0.5f), S(AL, 8.5f, M, 0.5f) },
            requiredHeight = 6.0f, requiresGround = false, requiresAir = true,
            narrowAffinity = 0f, wideAffinity = 1.8f, straightAffinity = 1f,
        });
        // Gauntlet: 少数→移動→少数→移動→少数の連戦(一度に大量には出さない)。
        list.Add(new EncounterFormation
        {
            formationId = "gauntlet", displayName = "Gauntlet",
            minIntensity = M,
            slots = { S(GF, 0f), S(GM, 1.8f), S(GF, 12f), S(AL, 14f, H), S(GF, 24f), S(GM, 25.8f, H) },
            requiredHeight = 3.6f, requiresGround = true, minGroundGap = 1.6f,
            narrowAffinity = 1f, wideAffinity = 1f, straightAffinity = 2.2f,
        });
        // Rest: 敵を置かない意図的な休憩(Intensity=Restで選ばれる。デバッグの強制用にも置く)。
        list.Add(new EncounterFormation { formationId = "rest", displayName = "Rest", minIntensity = EncounterIntensity.Rest, maxIntensity = EncounterIntensity.Rest });
        return lib;
    }

    // ===================================================================== //
    // 自然洞窟
    // ===================================================================== //
    static readonly string[] Ants = { "cave_ant", "soldier_ant" };

    static EncounterEnemyEntry Enemy(string id, float w, EnemyAiTier min = EnemyAiTier.T0, EnemyAiTier max = EnemyAiTier.T0, bool melee = false, params EncounterSlotKind[] slots)
        => new EncounterEnemyEntry { enemyId = id, weight = w, minTier = min, maxTier = max, tierDrivesMelee = melee, allowedSlots = new List<EncounterSlotKind>(slots) };

    static EncounterFormationWeight F(string id, float w) => new EncounterFormationWeight { formationId = id, weight = w };

    static StageEncounterProfile BuildNaturalCave()
    {
        var p = ScriptableObject.CreateInstance<StageEncounterProfile>();
        p.stageId = "natural_cave";
        p.routeLayout = StageRouteLayout.SingleRoute;
        p.replacesChunkSpawns = true;
        p.replacesMilestoneWalls = true;

        // ---- 洞窟専用Formation ----
        // Guard + Hopper: 前方にアリ、後方にHopper(前の敵と戦っている間に後ろのHopperが動く)。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "guard_hopper", displayName = "Guard + Hopper", stageSpecific = true,
            slots = { S(GF, 0f, E, 0.3f, Ants, true), S(GF, 1.7f, M, 0.2f, Ants, true), S(GR, 6f, E, 0.4f, new[] { "cave_hopper" }, true), S(GR, 9f, H, 0.4f, new[] { "cave_hopper" }, true) },
            requiredHeight = 4.6f, requiresGround = true, continuousGround = true, minGroundGap = 1.5f,
            narrowAffinity = 0.8f, wideAffinity = 1.3f, straightAffinity = 1f,
        });
        // Burrow Ambush: 安全で平らな地面から、予兆(亀裂+土煙)を見せてWormが出る。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "burrow_ambush", displayName = "Burrow Ambush", stageSpecific = true,
            slots = { S(BU, 2f, E, 0.5f), S(GM, 8f, M, 0.4f, Ants, true), S(BU, 14f, H, 0.5f) },
            requiredHeight = 3.6f, requiresGround = true, requiresBurrowGround = true, minGroundGap = 2f,
            narrowAffinity = 1f, wideAffinity = 1.2f, straightAffinity = 1.6f,
        });

        // ---- 敵(Slotの置き場所) ----
        var groundAnt = new[] { GF, GM, GR };
        var soldierSlots = new[] { GF, GM };
        EncounterEnemyEntry ant(float w) => Enemy("cave_ant", w, EnemyAiTier.T0, EnemyAiTier.T0, false, groundAnt);          // 攻撃絵が無いのでT0固定
        EncounterEnemyEntry soldier(float w) => Enemy("soldier_ant", w, EnemyAiTier.T0, EnemyAiTier.T2, true, soldierSlots);  // T0=棒立ち T1=その場で攻撃 T2=+移動
        EncounterEnemyEntry hopper(float w) => Enemy("cave_hopper", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GR);
        EncounterEnemyEntry bat(float w) => Enemy("cave_bat", w, EnemyAiTier.T0, EnemyAiTier.T0, false, AL, AH);
        EncounterEnemyEntry worm(float w) => Enemy("burrow_worm", w, EnemyAiTier.T0, EnemyAiTier.T0, false, BU);

        EncounterTierWeights T(float t0, float t1, float t2) => new EncounterTierWeights { t0 = t0, t1 = t1, t2 = t2 };

        // 0〜500m: Cave Ant中心 / Line・Colony・Staggered・Rest
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "0-500 ants", startDistance = 0f, endDistance = 500f,
            enemies = { ant(1f) },
            formations = { F("ground_line", 1.2f), F("ground_cluster", 1f), F("staggered", 1f) },
            restWeight = 1f, easyWeight = 1.6f, mediumWeight = 0.8f, hardWeight = 0.15f,
            easyTiers = T(1, 0, 0), mediumTiers = T(1, 0, 0), hardTiers = T(1, 0, 0),
        });
        // 500〜1500m: + Soldier Ant / Cave Hopper、Guard + Hopper、強めのStaggered
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "500-1500 +soldier/hopper", startDistance = 500f, endDistance = 1500f,
            enemies = { ant(1f), soldier(0.7f), hopper(0.8f) },
            formations = { F("ground_line", 1f), F("ground_cluster", 0.9f), F("staggered", 1.1f), F("guard_hopper", 1.2f) },
            restWeight = 1f, easyWeight = 1.2f, mediumWeight = 1.1f, hardWeight = 0.4f,
            easyTiers = T(1f, 0.25f, 0f), mediumTiers = T(0.6f, 0.4f, 0.1f), hardTiers = T(0.4f, 0.5f, 0.3f),
        });
        // 1500〜3000m: + Cave Bat、Ground + Bat / Bat Swarm
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "1500-3000 +bat", startDistance = 1500f, endDistance = 3000f,
            enemies = { ant(1f), soldier(0.8f), hopper(0.8f), bat(1f) },
            formations = { F("ground_line", 0.9f), F("ground_cluster", 0.8f), F("staggered", 0.9f), F("guard_hopper", 1f), F("ground_air", 1.2f), F("air_swarm", 0.8f) },
            restWeight = 1f, easyWeight = 1.1f, mediumWeight = 1.2f, hardWeight = 0.5f,
            easyTiers = T(1f, 0.3f, 0f), mediumTiers = T(0.5f, 0.4f, 0.15f), hardTiers = T(0.35f, 0.5f, 0.35f),
        });
        // 3000〜5000m: + Burrow Worm、Burrow Ambush / Gauntlet
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "3000-5000 +worm", startDistance = 3000f, endDistance = 5000f,
            enemies = { ant(1f), soldier(0.9f), hopper(0.8f), bat(0.9f), worm(0.8f) },
            formations = { F("ground_line", 0.8f), F("ground_cluster", 0.8f), F("staggered", 0.9f), F("guard_hopper", 0.9f), F("ground_air", 1f), F("air_swarm", 0.7f), F("burrow_ambush", 1.1f), F("gauntlet", 0.8f) },
            restWeight = 1f, easyWeight = 1f, mediumWeight = 1.2f, hardWeight = 0.55f,
            easyTiers = T(1f, 0.3f, 0.05f), mediumTiers = T(0.5f, 0.4f, 0.2f), hardTiers = T(0.3f, 0.5f, 0.4f),
        });
        // 5000m以降: 解禁済みの混成。後半でもT0/Easy/Restを残す(常に最大/Hardにはしない)。
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "5000+ mixed", startDistance = 5000f, endDistance = -1f,
            enemies = { ant(1f), soldier(1f), hopper(0.8f), bat(1f), worm(0.8f) },
            formations = { F("ground_line", 0.8f), F("ground_cluster", 0.9f), F("staggered", 0.9f), F("guard_hopper", 0.9f), F("ground_air", 1f), F("air_swarm", 0.8f), F("burrow_ambush", 0.9f), F("gauntlet", 0.9f) },
            restWeight = 0.9f, easyWeight = 1f, mediumWeight = 1.1f, hardWeight = 0.6f,
            easyTiers = T(1f, 0.35f, 0.1f), mediumTiers = T(0.45f, 0.45f, 0.25f), hardTiers = T(0.3f, 0.5f, 0.45f),
        });
        return p;
    }

    // ===================================================================== //
    // 荒野街道(Stage01) - 2026-09-28
    // 開けた屋外・上下に完全分岐したルート・地上戦中心・穴/坂/障害物。
    // 一本道の区間は通常のFormation、分岐区間は「上ルート/下ルートに別々の内容」(RoutePair)を置き、
    // どちらを走るかを選ばせる。上下の組み合わせは毎回「上=楽」にならないよう、多くは上下を入れ替えてよい。
    // ===================================================================== //
    static readonly string[] Goblins = { "goblin", "goblin_elite" };
    static readonly string[] Heavies = { "heavy_ogre" };
    static readonly string[] Shooters = { "shooter_archer" };

    static EncounterFormation Pair(string id, string name, EncounterIntensity nominal, string upF, EncounterIntensity upI, string lowF, EncounterIntensity lowI, bool mirror)
        => new EncounterFormation
        {
            formationId = id, displayName = name, stageSpecific = true, routeMode = EncounterRouteMode.RoutePair,
            minIntensity = nominal, maxIntensity = nominal,
            upperSide = new EncounterRouteSide { formationId = upF, intensity = upI },
            lowerSide = new EncounterRouteSide { formationId = lowF, intensity = lowI },
            allowMirror = mirror, requiresGround = false, requiredHeight = 0f,
        };

    static StageEncounterProfile BuildWasteland()
    {
        var p = ScriptableObject.CreateInstance<StageEncounterProfile>();
        p.stageId = "wasteland_road";
        p.routeLayout = StageRouteLayout.Default;   // 上下完全分岐はそのまま(自然洞窟の一本道化は適用しない)
        p.routeEncounters = true;
        p.replacesChunkSpawns = true;
        p.replacesMilestoneWalls = true;
        // 開けた景色と速度感を楽しむ区間を残す(敵で埋め尽くさない)
        p.spawnAheadDistance = 46f;
        p.gapAfterEasy = new Vector2(12f, 18f);
        p.gapAfterMedium = new Vector2(14f, 20f);
        p.gapAfterHard = new Vector2(18f, 26f);
        p.restLength = new Vector2(30f, 48f);
        p.airLowHeight = new Vector2(1.8f, 2.6f);
        p.airHighHeight = new Vector2(3.2f, 4.2f);
        p.routeLead = 8f;
        p.routeFrontLoad = 14f;
        p.afterMergeGap = new Vector2(6f, 12f);

        // ---- 荒野専用Formation ----
        // Frontline + Rear: 前にゴブリン/重装、後ろに射手(前の敵を斬りながら、後ろの射手を先に倒すか判断する)。
        // 射手は前衛から9m以上後ろ(高速でも矢を見てから対処できる距離)。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "frontline_rear", displayName = "Frontline + Rear", stageSpecific = true, minIntensity = M,
            slots = { S(GF, 0f, E, 0.3f, new[] { "goblin", "goblin_elite", "heavy_ogre" }, true), S(GF, 1.9f, M, 0.2f, Goblins, true),
                      S(GR, 9.5f, E, 0.4f, Shooters, true), S(GR, 13f, H, 0.4f, Shooters, true) },
            requiredHeight = 0f, requiresGround = true, continuousGround = true, minGroundGap = 1.6f, spacingSpeedScale = 0.45f,
            wideAffinity = 1f, straightAffinity = 1.3f,
        });
        // Goblin Horde: ゴブリンを3〜5体まとめて(重ならない最低間隔あり)。荒野の地面は6mごとに坂が混ざるので、
        // 汎用のGround Cluster(平地必須)ではなく、坂を許して穴だけ避ける荒野版を使う。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "goblin_horde", displayName = "Goblin Horde", stageSpecific = true,
            slots = { S(GM, 0f, E, 0.2f), S(GM, 1.5f, E, 0.2f), S(GM, 3.0f, E, 0.2f), S(GM, 4.5f, M, 0.2f), S(GR, 6.0f, H, 0.2f) },
            requiredHeight = 0f, requiresGround = true, continuousGround = true, minGroundGap = 1.3f,
            wideAffinity = 1.4f, straightAffinity = 1f,
        });
        // Heavy + Horde: 重装1体+周りにゴブリン少数(重装を大量には置かない)。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "heavy_horde", displayName = "Heavy + Horde", stageSpecific = true, minIntensity = M,
            slots = { S(GM, 0f, E, 0.2f, Goblins, true), S(GM, 2.6f, E, 0.2f, Heavies, true), S(GM, 5.4f, E, 0.2f, Goblins, true), S(GR, 7.4f, H, 0.2f, Goblins, true) },
            requiredHeight = 0f, requiresGround = true, continuousGround = true, minGroundGap = 1.8f,
            straightAffinity = 1.2f,
        });
        // Gap Guard: 穴の手前と向こう岸に敵。手前の敵は縁から6m以上手前、向こう岸の敵は縁ぎりぎりで跳んだ着地点より先。
        p.stageFormations.Add(new EncounterFormation
        {
            formationId = "gap_guard", displayName = "Gap Guard", stageSpecific = true, minIntensity = M,
            slots = { new EncounterSlot { kind = GF, xOffset = 7f, jitter = 0.6f, pitAnchor = EncounterPitAnchor.BeforePit, preferEnemyIds = Goblins, preferOnly = true },
                      new EncounterSlot { kind = GM, xOffset = 8.5f, jitter = 0.4f, minIntensity = H, pitAnchor = EncounterPitAnchor.BeforePit, preferEnemyIds = Goblins, preferOnly = true },
                      new EncounterSlot { kind = GF, xOffset = 3f, jitter = 0.6f, pitAnchor = EncounterPitAnchor.AfterPit },
                      new EncounterSlot { kind = GR, xOffset = 6.5f, jitter = 0.4f, minIntensity = H, pitAnchor = EncounterPitAnchor.AfterPit, preferEnemyIds = Goblins, preferOnly = true } },
            requiresPit = true, pitSearchStart = 9f, pitSearchEnd = 32f, maxPitWidth = 6.5f, pitMinBefore = 6f, pitLandingMargin = 2.5f,
            requiredHeight = 0f, requiresGround = true, minGroundGap = 2f, spacingSpeedScale = 0.15f,
        });

        // ---- 上下ルートの組み合わせ(分岐区間) ----
        // 例A: 上=群れ / 下=少数、例B: 上=少数 / 下=重装+ゴブリン、例C: 上=飛ぶ敵 / 下=地上の群れ、例D: 上=休憩 / 下=中くらい。
        // mirror=true は上下を入れ替えてよい(上が楽とは限らない)。飛ぶ敵は上ルートの上空にだけ置ける(下ルートの上は足場)。
        p.stageFormations.Add(Pair("route_line_vs_stagger", "Route: Line / Staggered", E, "ground_line", E, "staggered", E, true));
        p.stageFormations.Add(Pair("route_easy_vs_rest", "Route: Easy / Rest", E, "ground_line", E, "rest", EncounterIntensity.Rest, true));
        p.stageFormations.Add(Pair("route_horde_vs_few", "Route: Horde / Few", M, "goblin_horde", M, "ground_line", E, true));
        p.stageFormations.Add(Pair("route_air_vs_horde", "Route: Flying / Ground Horde", M, "air_swarm", M, "goblin_horde", M, false));
        p.stageFormations.Add(Pair("route_rest_vs_medium", "Route: Rest / Medium", M, "rest", EncounterIntensity.Rest, "frontline_rear", M, true));
        p.stageFormations.Add(Pair("route_few_vs_heavy", "Route: Few / Heavy + Horde", M, "staggered", E, "heavy_horde", M, true));
        p.stageFormations.Add(Pair("route_upper_fight", "Route: Upper Encounter", H, "heavy_horde", H, "staggered", E, false));
        p.stageFormations.Add(Pair("route_lower_fight", "Route: Lower Encounter", H, "ground_air", E, "frontline_rear", H, false));
        p.stageFormations.Add(Pair("route_line_vs_gap", "Route: Line / Gap Guard", M, "ground_line", E, "gap_guard", M, false)); // 穴は下ルートにだけある
        p.stageFormations.Add(Pair("route_twin_threat", "Route: Horde / Heavy (both hard)", H, "goblin_horde", H, "heavy_horde", H, true));

        // ---- 敵(Slotの置き場所) ----
        EncounterEnemyEntry goblin(float w) => Enemy("goblin", w, EnemyAiTier.T0, EnemyAiTier.T2, true, GF, GM, GR);          // T0=棒立ち T1/T2=その場で攻撃(HPは不変)
        EncounterEnemyEntry elite(float w) => Enemy("goblin_elite", w, EnemyAiTier.T0, EnemyAiTier.T2, true, GF, GM, GR);
        EncounterEnemyEntry imp(float w) => Enemy("irregular_imp", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GM, GR);
        EncounterEnemyEntry shooter(float w) => Enemy("shooter_archer", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GR);        // 射手は後方だけ
        EncounterEnemyEntry heavy(float w) => Enemy("heavy_ogre", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GF, GM);
        EncounterEnemyEntry chaser(float w) => Enemy("chaser_runner", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GR);
        EncounterEnemyEntry rusher(float w) => Enemy("rusher_runner", w, EnemyAiTier.T0, EnemyAiTier.T0, false, GR);
        EncounterEnemyEntry wyvern(float w) => Enemy("flying_wyvern", w, EnemyAiTier.T0, EnemyAiTier.T0, false, AL, AH);
        EncounterTierWeights T(float t0, float t1, float t2) => new EncounterTierWeights { t0 = t0, t1 = t1, t2 = t2 };

        // 0〜500m: ゴブリンだけ。Goblin Line / Horde / Staggered / Rest + 簡単な上下の組み合わせ
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "0-500 goblins", startDistance = 0f, endDistance = 500f,
            enemies = { goblin(1f) },
            formations = { F("ground_line", 1.2f), F("goblin_horde", 1f), F("staggered", 1f), F("route_line_vs_stagger", 1f), F("route_easy_vs_rest", 1f) },
            restWeight = 1f, easyWeight = 1.6f, mediumWeight = 0.7f, hardWeight = 0.1f,
            easyTiers = T(1, 0, 0), mediumTiers = T(1, 0, 0), hardTiers = T(1, 0, 0),
        });
        // 500〜1500m: +攻撃する敵(T1)、Elite/Imp/Wyvern、Ground + Flying、簡単なRoute Choice
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "500-1500 +elite/imp/wyvern", startDistance = 500f, endDistance = 1500f,
            enemies = { goblin(1f), elite(0.6f), imp(0.6f), wyvern(0.8f) },
            formations = { F("ground_line", 1f), F("goblin_horde", 0.9f), F("staggered", 1f), F("ground_air", 1.1f), F("air_swarm", 0.5f),
                           F("route_line_vs_stagger", 0.8f), F("route_easy_vs_rest", 0.7f), F("route_horde_vs_few", 1f), F("route_air_vs_horde", 1f), F("route_rest_vs_medium", 0.8f) },
            restWeight = 1f, easyWeight = 1.2f, mediumWeight = 1.1f, hardWeight = 0.35f,
            easyTiers = T(1f, 0.25f, 0f), mediumTiers = T(0.6f, 0.4f, 0.1f), hardTiers = T(0.4f, 0.5f, 0.3f),
        });
        // 1500〜3000m: +射手/重装、Frontline + Rear / Heavy + Horde / Gap Guard、複雑なRoute Choice
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "1500-3000 +shooter/heavy", startDistance = 1500f, endDistance = 3000f,
            enemies = { goblin(1f), elite(0.8f), imp(0.6f), wyvern(0.8f), shooter(0.8f), heavy(0.6f) },
            formations = { F("ground_line", 0.9f), F("goblin_horde", 0.8f), F("staggered", 0.9f), F("ground_air", 1f), F("air_swarm", 0.4f),
                           F("frontline_rear", 1.2f), F("heavy_horde", 1.1f), F("gap_guard", 1f),
                           F("route_line_vs_stagger", 0.5f), F("route_easy_vs_rest", 0.5f), F("route_horde_vs_few", 0.9f), F("route_air_vs_horde", 0.9f), F("route_rest_vs_medium", 0.9f),
                           F("route_few_vs_heavy", 1f), F("route_line_vs_gap", 0.8f), F("route_upper_fight", 0.8f), F("route_lower_fight", 0.8f) },
            restWeight = 1f, easyWeight = 1.1f, mediumWeight = 1.2f, hardWeight = 0.5f,
            easyTiers = T(1f, 0.3f, 0f), mediumTiers = T(0.5f, 0.4f, 0.15f), hardTiers = T(0.35f, 0.5f, 0.35f),
        });
        // 3000〜5000m: +追いかける敵、Gauntlet(少数→走る→少数の連戦)
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "3000-5000 +chaser/gauntlet", startDistance = 3000f, endDistance = 5000f,
            enemies = { goblin(1f), elite(0.9f), imp(0.6f), wyvern(0.8f), shooter(0.8f), heavy(0.6f), chaser(0.6f) },
            formations = { F("ground_line", 0.8f), F("goblin_horde", 0.8f), F("staggered", 0.8f), F("ground_air", 0.9f), F("air_swarm", 0.4f),
                           F("frontline_rear", 1f), F("heavy_horde", 1f), F("gap_guard", 1f), F("gauntlet", 1f),
                           F("route_line_vs_stagger", 0.4f), F("route_easy_vs_rest", 0.4f), F("route_horde_vs_few", 0.8f), F("route_air_vs_horde", 0.8f), F("route_rest_vs_medium", 0.8f),
                           F("route_few_vs_heavy", 0.9f), F("route_line_vs_gap", 0.8f), F("route_upper_fight", 0.8f), F("route_lower_fight", 0.8f), F("route_twin_threat", 0.3f) },
            restWeight = 1f, easyWeight = 1f, mediumWeight = 1.2f, hardWeight = 0.55f,
            easyTiers = T(1f, 0.3f, 0.05f), mediumTiers = T(0.5f, 0.4f, 0.2f), hardTiers = T(0.3f, 0.5f, 0.4f),
        });
        // 5000m以降: 解禁済みの混成。後半でもT0/Easy/Restを残す(敵数を増やし続けるだけにしない)。
        p.bands.Add(new EncounterDistanceBand
        {
            bandName = "5000+ mixed", startDistance = 5000f, endDistance = -1f,
            enemies = { goblin(1f), elite(1f), imp(0.6f), wyvern(0.8f), shooter(0.8f), heavy(0.6f), chaser(0.6f), rusher(0.5f) },
            formations = { F("ground_line", 0.8f), F("goblin_horde", 0.8f), F("staggered", 0.8f), F("ground_air", 0.9f), F("air_swarm", 0.4f),
                           F("frontline_rear", 1f), F("heavy_horde", 0.9f), F("gap_guard", 0.9f), F("gauntlet", 1f),
                           F("route_line_vs_stagger", 0.4f), F("route_easy_vs_rest", 0.4f), F("route_horde_vs_few", 0.8f), F("route_air_vs_horde", 0.8f), F("route_rest_vs_medium", 0.8f),
                           F("route_few_vs_heavy", 0.9f), F("route_line_vs_gap", 0.8f), F("route_upper_fight", 0.8f), F("route_lower_fight", 0.8f), F("route_twin_threat", 0.4f) },
            restWeight = 0.9f, easyWeight = 1f, mediumWeight = 1.1f, hardWeight = 0.6f,
            easyTiers = T(1f, 0.35f, 0.1f), mediumTiers = T(0.45f, 0.45f, 0.25f), hardTiers = T(0.3f, 0.5f, 0.45f),
        });
        return p;
    }
}

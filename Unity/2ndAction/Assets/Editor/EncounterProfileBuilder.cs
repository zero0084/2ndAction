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

    static void BuildAll(bool overwrite)
    {
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        Save(Dir + "/GenericFormations.asset", BuildLibrary(), overwrite);
        Save(Dir + "/Profile_natural_cave.asset", BuildNaturalCave(), overwrite);
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
}

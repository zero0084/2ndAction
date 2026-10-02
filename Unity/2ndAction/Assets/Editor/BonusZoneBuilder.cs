using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// BONUS ZONE(2026-09-29)の調整データ(Resources/Encounters/BonusZone.asset)を作る。
// 既にある場合は上書きしない(Inspectorで調整した値を守る)。作り直すのはメニューの(overwrite)だけ。
public static class BonusZoneBuilder
{
    const string Path = "Assets/Resources/Encounters/BonusZone.asset";

    [MenuItem("Tools/2ndAction/Encounter/Build Bonus Zone Profile (keep existing)")]
    public static void Build() => Build(false);

    [MenuItem("Tools/2ndAction/Encounter/Rebuild Bonus Zone Profile (overwrite)")]
    public static void BuildForce() => Build(true);

    // バッチ用: 報酬Enemyの素材/データ + BONUS ZONEのデータ
    public static void BuildAllBatch()
    {
        SkyEnemyDatabase.Build();
        Build(false);
    }

    static void Build(bool overwrite)
    {
        Directory.CreateDirectory("Assets/Resources/Encounters");
        var p = Create();
        var existing = AssetDatabase.LoadAssetAtPath<BonusZoneProfile>(Path);
        if (existing != null && !overwrite) { Debug.Log("BonusZoneBuilder: keep " + Path); return; }
        if (existing != null) { EditorUtility.CopySerialized(p, existing); EditorUtility.SetDirty(existing); Debug.Log("BonusZoneBuilder: overwrote " + Path); }
        else { AssetDatabase.CreateAsset(p, Path); Debug.Log("BonusZoneBuilder: created " + Path); }
        AssetDatabase.SaveAssets();
    }

    static readonly string[] Goblin = { "treasure_goblin" };
    static readonly string[] Mimic = { "mimic" };
    static readonly string[] Slime = { "golden_slime" };
    static readonly string[] Fairy = { "card_fairy" };
    const EncounterSlotKind GF = EncounterSlotKind.GroundFront, GM = EncounterSlotKind.GroundMiddle, GR = EncounterSlotKind.GroundRear;
    const EncounterSlotKind AL = EncounterSlotKind.AirLow, AM = EncounterSlotKind.AirMiddle;

    static EncounterSlot S(EncounterSlotKind k, float x, string[] only, float jitter = 0.4f)
        => new EncounterSlot { kind = k, xOffset = x, minIntensity = EncounterIntensity.Easy, jitter = jitter, preferEnemyIds = only, preferOnly = true };

    static EncounterFormation F(string id, string name, params EncounterSlot[] slots)
    {
        var f = new EncounterFormation { formationId = id, displayName = name, stageSpecific = false, minIntensity = EncounterIntensity.Easy, requiredHeight = 0f, requiresGround = true, minGroundGap = 1.2f, spacingSpeedScale = 0.3f };
        f.slots.AddRange(slots);
        foreach (var s in slots) if (EncounterSlots.IsAir(s.kind)) f.requiresAir = true;
        return f;
    }

    static BonusEncounterType T(string id, string name, float weight, Vector2 gap, float mile, float exp, Color color, params string[] waves)
        => new BonusEncounterType { id = id, displayName = name, weight = weight, waveGap = gap, mileMultiplier = mile, expMultiplier = exp, color = color, waves = new List<string>(waves) };

    static BonusZoneProfile Create()
    {
        var p = ScriptableObject.CreateInstance<BonusZoneProfile>();
        var f = p.formations;
        // Treasure Goblin: 1体 → 少し先に2体(追跡のリズム)
        f.Add(F("bonus_goblin_1", "Goblin x1", S(GF, 0f, Goblin)));
        f.Add(F("bonus_goblin_2", "Goblin x2", S(GF, 0f, Goblin), S(GM, 4f, Goblin)));
        // Mimic: 1体(しばらくPlayerの前に居座る)
        f.Add(F("bonus_mimic_1", "Mimic", S(GM, 0f, Mimic, 0.2f)));
        // Golden Slime: 小集団(大量Activeにしない)
        f.Add(F("bonus_slime_3", "Golden Slime x3", S(GF, 0f, Slime), S(GM, 1.8f, Slime), S(GR, 3.6f, Slime)));
        f.Add(F("bonus_slime_4", "Golden Slime x4", S(GF, 0f, Slime), S(GM, 1.6f, Slime), S(GM, 3.2f, Slime), S(GR, 4.8f, Slime)));
        // Card Fairy: スライムの中に1体(空中、他と光で見分けられる)
        f.Add(F("bonus_fairy_hunt", "Card Hunt", S(GF, 0f, Slime), S(GM, 2f, Slime), S(AL, 4f, Fairy, 0.2f), S(GR, 5.5f, Slime)));
        // Treasure Parade: ゴブリン + スライム
        f.Add(F("bonus_parade", "Parade", S(GF, 0f, Goblin), S(GM, 2.6f, Slime), S(GR, 4.4f, Slime)));

        Color gold = new Color(1f, 0.84f, 0.3f);
        p.types.Add(T("mile_rush", "MILE RUSH", 1f, new Vector2(7f, 10f), 1f, 1f, gold, "bonus_goblin_1", "bonus_goblin_2", "bonus_goblin_1", "bonus_goblin_2"));
        p.types.Add(T("mimic_bash", "MIMIC BASH", 0.8f, new Vector2(40f, 50f), 1f, 1f, new Color(1f, 0.7f, 0.35f), "bonus_mimic_1"));
        p.types.Add(T("exp_fever", "EXP FEVER", 1f, new Vector2(6f, 9f), 1f, 1f, new Color(0.6f, 1f, 0.55f), "bonus_slime_3", "bonus_slime_4"));
        p.types.Add(T("card_hunt", "CARD HUNT", 0.55f, new Vector2(7f, 10f), 1f, 0.7f, new Color(1f, 0.6f, 1f), "bonus_slime_3", "bonus_fairy_hunt", "bonus_slime_3", "bonus_slime_4"));
        p.types.Add(T("treasure_parade", "TREASURE PARADE", 0.9f, new Vector2(8f, 11f), 0.8f, 0.8f, new Color(1f, 0.9f, 0.45f), "bonus_parade", "bonus_slime_3", "bonus_parade", "bonus_goblin_1"));
        // JACKPOT: 4種すべて(画面を埋めない程度に順に)。抽選は別枠の低確率
        p.types.Add(T("jackpot", "JACKPOT", 0f, new Vector2(9f, 12f), 2f, 2f, new Color(1f, 0.95f, 0.5f), "bonus_parade", "bonus_mimic_1", "bonus_fairy_hunt", "bonus_slime_4", "bonus_goblin_2"));

        // 報酬の強化(2026-10-01): 敵の報酬、BONUS CLEAR、PERFECT BONUS(Card Choiceは出さない)
        p.goblinHitMile = 5; p.goblinKillMile = 45;
        p.mimicHitMile = 6; p.mimicMaxMile = 150; p.mimicKillMile = 30;
        p.goldenSlimeExp = 15f; p.goldenSlimeExpPerLevel = 0.06f;
        p.resultSeconds = 3f;
        void R(string id, int clearMile, float clearPerLv, int perfectMile, float perfectPerLv, float ratio, int minKills, int mimicMile, string label, params BonusEnemyKind[] kinds)
        {
            var t = p.FindType(id);
            t.clearMile = clearMile; t.clearExpPerLevel = clearPerLv; t.perfectMile = perfectMile; t.perfectExpPerLevel = perfectPerLv;
            t.perfectKillRatio = ratio; t.perfectMinKills = minKills; t.perfectMimicMile = mimicMile; t.perfectLabel = label;
            t.perfectKinds = new List<BonusEnemyKind>(kinds);
        }
        R("mile_rush", 100, 0.1f, 150, 0f, 1f, 0, 0, "Treasure Goblin 全撃破", BonusEnemyKind.TreasureGoblin);
        R("mimic_bash", 90, 0.1f, 120, 0f, 1f, 0, 200, "Mimicから合計200 MILE以上 or 撃破");
        R("exp_fever", 30, 0.4f, 40, 0.25f, 0.8f, 0, 0, "Golden Slime 8割撃破", BonusEnemyKind.GoldenSlime);
        R("card_hunt", 60, 0.25f, 80, 0.15f, 0f, 1, 0, "Card Fairy 撃破", BonusEnemyKind.CardFairy);
        R("treasure_parade", 80, 0.3f, 120, 0.2f, 0.7f, 0, 0, "Goblin / Slime 7割回収", BonusEnemyKind.TreasureGoblin, BonusEnemyKind.GoldenSlime);
        R("jackpot", 120, 0.4f, 160, 0.3f, 0.6f, 0, 0, "Goblin / Slime 6割回収", BonusEnemyKind.TreasureGoblin, BonusEnemyKind.GoldenSlime);

        EncounterEnemyEntry E(string id, params EncounterSlotKind[] slots) => new EncounterEnemyEntry { enemyId = id, weight = 1f, minTier = EnemyAiTier.T0, maxTier = EnemyAiTier.T0, allowedSlots = new List<EncounterSlotKind>(slots) };
        p.band = new EncounterDistanceBand
        {
            bandName = "BONUS", startDistance = 0f, endDistance = -1f,
            enemies = { E("treasure_goblin", GF, GM, GR), E("mimic", GF, GM, GR), E("golden_slime", GF, GM, GR), E("card_fairy", AL, AM) },
        };
        return p;
    }
}

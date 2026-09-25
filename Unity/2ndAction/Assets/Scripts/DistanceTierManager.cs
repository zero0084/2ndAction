using System.Collections.Generic;
using UnityEngine;

// Distance Level Design Ver.1.1 - the 10 Formation shapes kept from Ver.1
// (Gap+Enemy/Pincer/Route Choice are still deferred - see the class comment
// on DistanceTierManager for why).
public enum EnemyFormationType
{
    Single,
    SmallGroup,
    HorizontalLine,
    VerticalLine,
    Cluster,
    DiagonalUp,
    GroundAir,
    FrontlineShooter,
    HeavyNormal,
    Rush
}

// Distance Level Design Ver.1.1 - what a SpawnPoint's category should
// resolve to at spawn time. Frontline/Any both resolve to whatever the
// CURRENT tier actually allows (see DistanceTierManager.ResolveRole) rather
// than a fixed category, so a Formation authored once still adapts as more
// EnemyCategory values unlock at higher tiers - exactly the "Formation ×
// Enemy Typeの組み合わせで難易度を上げる" idea from the Ver.1 brief, now
// expressed as data instead of ad hoc per-formation-type code.
public enum EnemyRole
{
    Any,
    Normal,
    Flying,
    Heavy,
    Shooter,
    Frontline
}

// Distance Level Design Ver.1.1, item 2 - one fixed position within a
// Formation, relative to that instance's own anchor point (see
// FormationData's class comment). xOffset/yOffset are WORLD UNITS, not
// chunk-relative ratios any more - this is the core fix for "Formationが意
// 図した形になっていない": Ver.1 spread a Formation's members across
// whichever real terrain chunks happened to generate next, so the actual
// on-screen shape drifted with the terrain instead of matching the
// authored pattern. Ver.1.1 instead RESERVES a guaranteed-flat run wide
// enough for the whole Formation up front (see TerrainManager.
// RequestFlatRun) and places every member from ONE single anchor, so the
// shape is exact every time.
[System.Serializable]
public class FormationSpawnPoint
{
    public float xOffset;
    public float yOffset;
    public EnemyRole role = EnemyRole.Any;
}

// Distance Level Design Ver.1.1, item 2 - one Formation definition, plain
// data shown directly on DistanceTierManager's Inspector (a reorderable
// array), matching the brief's own field list (FormationID/FormationType/
// SpawnPoints/Weight/MinDistance/MaxDistance) exactly. Formations are no
// longer nested under DistanceTier - each one carries its own valid
// distance range instead, so adding/tuning one Formation never requires
// touching the Tier table at all.
[System.Serializable]
public class FormationData
{
    public string formationId = "formation";
    public EnemyFormationType formationType;
    public FormationSpawnPoint[] spawnPoints = new FormationSpawnPoint[0];
    public float weight = 1f;
    public float minDistance = 0f;
    public float maxDistance = 999999f;
}

// One row of the Distance Tier table (Ver.1 item 3) - still just Enemy
// Category availability + HP scaling input; Formation availability moved
// to FormationData's own minDistance/maxDistance above (Ver.1.1 item 2).
[System.Serializable]
public class DistanceTier
{
    public string tierName = "Tier";
    public float startDistance;
    public float endDistance;
    public EnemyCategory[] availableEnemyTypes = new EnemyCategory[0];
}

// Distance Level Design Ver.1.1 - central, data-driven "what should exist
// at this distance" authority: Enemy HP scaling, which EnemyCategory is
// currently available, and (via TryStartFormation) the actual pre-defined-
// pattern Formation picker TerrainManager's per-chunk enemy-spawn code
// calls into.
//
// Why Gap+Enemy/Pincer/Route Choice are still not here: those need either
// enemies on both sides of the player at once or multiple simultaneous
// terrain paths, neither of which a single forward-only terrain generator
// can do without a much larger terrain-generation change - still out of
// scope for a fix-existing-systems pass (see the brief's own "新しい
// Formation Typeは追加しない").
public class DistanceTierManager : MonoBehaviour
{
    public static DistanceTierManager Instance { get; private set; }

    [Header("Enemy HP Scaling")]
    public float hpIncreaseDistance = 2000f;
    // エリアルコンボ改修(2026-09-11) - 「ゴブリンを一撃で倒れないように、
    // 通常攻撃2〜3発程度で倒れるように」。PlayerController.AttackPower
    // の既定値(2)に対し、CurrentEnemyHp = 1+baseHpBonus (距離0時点) が
    // ちょうど「通常攻撃→上方向攻撃(打ち上げ)→下方向攻撃(叩き落とし)」
    // の3発で倒せる値になるよう、旧2(不具合修正2026-09-09の値)から4へ
    // 引き上げた(HP=5、2ダメージ×3発=6≧5)。将来の強敵は
    // EnemyDefinition.hpMultiplierを上げるだけ(既存の仕組みのまま)で
    // 4〜6発相当にできる - ここは変更不要。数値はあくまで暫定値なので、
    // 実際にプレイして「硬すぎる/柔らかすぎる」と感じたらここか
    // PlayerController.AttackPowerをInspector/コードで調整すること。
    public int baseHpBonus = 4;

    [Header("Tiers - Enemy Category availability only (item 3, Ver.1)")]
    public DistanceTier[] tiers = new DistanceTier[0];

    [Header("Formations - item 2 (Ver.1.1, Inspector/Data driven)")]
    public FormationData[] formations = new FormationData[0];

    [Header("Formation Safety (item 2 - Spawn Validation)")]
    // How far past the widest member's own xOffset to pad the reserved
    // flat run - "Formation全体が極端に長くならない" is enforced by the
    // authored offsets themselves (see SceneBuilder.BuildFormations), this
    // margin only avoids a member landing exactly on a chunk seam.
    public float formationEdgeMargin = 1f;
    // "画面上下端の外に出ない" - a member whose absolute Y would exceed the
    // camera's own top edge (minus this margin) is skipped rather than
    // spawned off-screen.
    public float screenTopMargin = 0.6f;

    [Header("Debug")]
    public bool debugLogEnabled = true;

    string currentTierName = "";

    void Awake()
    {
        Instance = this;
    }

    float CurrentDistance => GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;

    public int CurrentEnemyHp => Mathf.Max(1, 1 + baseHpBonus + Mathf.FloorToInt(CurrentDistance / Mathf.Max(1f, hpIncreaseDistance)));

    public DistanceTier CurrentTier
    {
        get
        {
            float d = CurrentDistance;
            for (int i = 0; i < tiers.Length; i++)
            {
                if (d >= tiers[i].startDistance && d < tiers[i].endDistance) return tiers[i];
            }
            return tiers.Length > 0 ? tiers[tiers.Length - 1] : null;
        }
    }

    void Update()
    {
        DistanceTier tier = CurrentTier;
        string name = tier != null ? tier.tierName : "(none)";
        if (name != currentTierName)
        {
            currentTierName = name;
            if (debugLogEnabled) Debug.Log($"[DistanceTier] Enter {name} ({(tier != null ? tier.startDistance : 0)}-{(tier != null ? tier.endDistance : 0)})");
        }
    }

    public bool IsCategoryAvailable(EnemyCategory category)
    {
        DistanceTier tier = CurrentTier;
        if (tier == null || tier.availableEnemyTypes == null) return category == EnemyCategory.Normal; // fail-safe - never fully stop spawning
        foreach (EnemyCategory c in tier.availableEnemyTypes)
        {
            if (c == category) return true;
        }
        return false;
    }

    public int EnemyHpFor(float hpMultiplier)
    {
        // Card Expansion/Gacha Evolution Ver.1 - "Tough Enemies"-family
        // cards (EnemyHpMultiplier) apply here, on top of the species'
        // own hpMultiplier, so they scale every grunt uniformly regardless
        // of species.
        float globalMultiplier = GameManager.Instance != null ? GameManager.Instance.EnemyHpMultiplier : 1f;
        return Mathf.Max(1, Mathf.RoundToInt(CurrentEnemyHp * Mathf.Max(0.01f, hpMultiplier) * Mathf.Max(0.01f, globalMultiplier)));
    }

    // The main entry point TerrainManager's per-chunk enemy-spawn code
    // calls instead of its own single roll-and-spawn. spacingOk/chance
    // mirror the EXACT same gate the original single-enemy spawn always
    // had, so overall enemy DENSITY is unaffected by this system - only
    // WHAT/HOW spawns did. Returns false (no `out` values touched) if
    // nothing should spawn at this chunk. `requests` carries WORLD-UNIT
    // offsets from an anchor the caller controls (TerrainManager owns
    // actually placing that anchor safely - see its own SpawnFormation).
    public bool TryStartFormation(bool spacingOk, float chance, out List<EnemySpawnRequest> requests, out float halfWidthNeeded)
    {
        requests = null;
        halfWidthNeeded = 0f;
        if (!spacingOk || Random.value >= chance) return false;

        FormationData chosen = PickWeightedFormation();
        return BuildFormationRequests(chosen, out requests, out halfWidthNeeded);
    }

    // マルチプレイ対応Phase 1(2026-09-25) - TerrainManager.ReserveFormationDeterministic用。
    // 出現判定と編成の選択を「チャンクの論理距離」と地形専用の決定的乱数(WorldRng.Formation)
    // だけで行い、全端末で同じ結果(=同じ平地予約)にする。乱数は出現しない場合も含めて毎回
    // 同じ回数だけ消費し、端末間で乱数列がずれないようにしている。編成の中身(どの敵種に
    // するか)は地形に影響しないため、従来どおり端末ごとのUnityEngine.Randomのまま。
    public bool TryStartFormationWorld(bool spacingOk, float chance, float distance, out List<EnemySpawnRequest> requests, out float halfWidthNeeded)
    {
        requests = null;
        halfWidthNeeded = 0f;
        float spawnRoll = WorldRng.Formation.Value;
        float pickRoll = WorldRng.Formation.Value;
        if (!spacingOk || spawnRoll >= chance) return false;
        return BuildFormationRequests(PickWeightedFormationAt(distance, pickRoll), out requests, out halfWidthNeeded);
    }

    FormationData PickWeightedFormationAt(float d, float roll01)
    {
        float total = 0f;
        for (int i = 0; i < formations.Length; i++)
        {
            FormationData f = formations[i];
            if (f == null || d < f.minDistance || d >= f.maxDistance) continue;
            total += Mathf.Max(0f, f.weight);
        }
        if (total <= 0f) return null;

        float roll = roll01 * total;
        float acc = 0f;
        for (int i = 0; i < formations.Length; i++)
        {
            FormationData f = formations[i];
            if (f == null || d < f.minDistance || d >= f.maxDistance) continue;
            acc += Mathf.Max(0f, f.weight);
            if (roll <= acc) return f;
        }
        return null;
    }

    bool BuildFormationRequests(FormationData chosen, out List<EnemySpawnRequest> requests, out float halfWidthNeeded)
    {
        requests = null;
        halfWidthNeeded = 0f;
        if (chosen == null || chosen.spawnPoints == null || chosen.spawnPoints.Length == 0) return false;

        if (debugLogEnabled) Debug.Log($"[Formation] Spawn {chosen.formationType} ({chosen.formationId})");

        requests = new List<EnemySpawnRequest>(chosen.spawnPoints.Length);
        float maxAbsX = 0f;
        foreach (FormationSpawnPoint sp in chosen.spawnPoints)
        {
            maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(sp.xOffset));
            requests.Add(new EnemySpawnRequest
            {
                category = ResolveRole(sp.role),
                xOffset = sp.xOffset,
                yOffset = sp.yOffset
            });
        }
        halfWidthNeeded = maxAbsX + formationEdgeMargin;
        return true;
    }

    FormationData PickWeightedFormation()
    {
        float d = CurrentDistance;
        float total = 0f;
        for (int i = 0; i < formations.Length; i++)
        {
            FormationData f = formations[i];
            if (f == null) continue;
            if (d < f.minDistance || d >= f.maxDistance) continue;
            total += Mathf.Max(0f, f.weight);
        }
        if (total <= 0f) return null;

        float roll = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < formations.Length; i++)
        {
            FormationData f = formations[i];
            if (f == null) continue;
            if (d < f.minDistance || d >= f.maxDistance) continue;
            acc += Mathf.Max(0f, f.weight);
            if (roll <= acc) return f;
        }
        return null;
    }

    static readonly EnemyCategory[] GroundLikeCategories = { EnemyCategory.Normal, EnemyCategory.Irregular, EnemyCategory.Shooter, EnemyCategory.Heavy, EnemyCategory.Chaser, EnemyCategory.Rusher };
    static readonly EnemyCategory[] FrontlineCategories = { EnemyCategory.Normal, EnemyCategory.Heavy };
    List<EnemyCategory> categoryScratch = new List<EnemyCategory>();

    EnemyCategory ResolveRole(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Normal: return IsCategoryAvailable(EnemyCategory.Normal) ? EnemyCategory.Normal : PickFromAvailable(GroundLikeCategories);
            case EnemyRole.Flying: return IsCategoryAvailable(EnemyCategory.Flying) ? EnemyCategory.Flying : EnemyCategory.Normal;
            case EnemyRole.Heavy: return IsCategoryAvailable(EnemyCategory.Heavy) ? EnemyCategory.Heavy : PickFromAvailable(GroundLikeCategories);
            case EnemyRole.Shooter: return IsCategoryAvailable(EnemyCategory.Shooter) ? EnemyCategory.Shooter : PickFromAvailable(GroundLikeCategories);
            case EnemyRole.Frontline: return PickFromAvailable(FrontlineCategories);
            default: return PickFromAvailable(GroundLikeCategories);
        }
    }

    // A random AVAILABLE (per the current tier) category from `pool` -
    // Normal is always in GroundLikeCategories/FrontlineCategories as the
    // guaranteed fallback so this can never return "nothing" even on the
    // very first tier.
    EnemyCategory PickFromAvailable(EnemyCategory[] pool)
    {
        categoryScratch.Clear();
        foreach (EnemyCategory c in pool)
        {
            if (IsCategoryAvailable(c)) categoryScratch.Add(c);
        }
        if (categoryScratch.Count == 0) return EnemyCategory.Normal;
        return categoryScratch[Random.Range(0, categoryScratch.Count)];
    }

    // ===== Debug - item 10/11 (Development Build / Editor only; see
    // GameManager.DrawDistanceWarpDebugUI) =====
    public static readonly float[] DebugWarpStops = { 1000f, 5000f, 10000f, 20000f, 40000f, 50000f, 70000f, 90000f, 99000f };
}

public struct EnemySpawnRequest
{
    public EnemyCategory category;
    // World units, relative to whatever anchor TerrainManager placed for
    // this Formation instance (see TerrainManager.SpawnFormation) - NOT a
    // 0-1 ratio any more (Ver.1's xRatioInChunk/heightSteps int are gone).
    public float xOffset;
    public float yOffset;
}

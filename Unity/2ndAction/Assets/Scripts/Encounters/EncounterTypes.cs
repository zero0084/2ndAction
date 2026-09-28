using System;
using System.Collections.Generic;
using UnityEngine;

// 共通Encounter / Formation System(2026-09-27)の型定義。
//
// 考え方(ステージ非依存):
//   Enemy      = 何の敵か(EnemyDefinition)
//   AI Tier    = どう行動するか(EnemyAiTier。HPとは別軸)
//   Formation  = どこに配置されるか(Spawn Slotの並び。敵の種類は固定しない)
//   Encounter  = いつ、どのFormationを、どの強度(Intensity)で出すか(EncounterDirector)
//   Stage Profile = そのステージで何が使えるか(StageEncounterProfile)
//
// Formationは「どのSlotに何系の敵が入るか」だけを持ち、実際の敵はStage Profileの
// Distance Bandが持つ敵リストから、Slotに置ける敵(地上/空中/地中)を選ぶ。
// → 同じGround Lineが荒野ならGoblin、洞窟ならCave Antになる。

public enum EncounterSlotKind
{
    GroundFront = 0,
    GroundMiddle = 1,
    GroundRear = 2,
    AirLow = 3,
    AirHigh = 4,
    Burrow = 5,
    Special = 6,
}

public enum EncounterIntensity { Rest = 0, Easy = 1, Medium = 2, Hard = 3 }

// ステージの通路構成(地形生成側が参照)。Defaultは従来どおり(テーマの設定に従う)。
public enum StageRouteLayout { Default = 0, SingleRoute = 1 }

// 上下ルート分岐のあるステージ(荒野街道)用(2026-09-28)。
//  Main      = 分岐の外の一本道に置く通常のFormation
//  RoutePair = 分岐区間で上ルート/下ルートそれぞれに別の内容(Formation+Intensity、Restも可)を置く組み合わせ
public enum EncounterRouteMode { Main = 0, RoutePair = 1 }
// 敵を置いたルート(デバッグ/テスト用の記録)。
public enum EncounterRoute { Main = 0, Upper = 1, Lower = 2 }
// Gap Guard用: Slotの位置を穴の縁から測る(BeforePit=穴の手前の縁から手前へxOffset、AfterPit=穴の向こう岸からxOffset先)。
public enum EncounterPitAnchor { None = 0, BeforePit = 1, AfterPit = 2 }

[Serializable]
public class EncounterRouteSide
{
    [Tooltip("このルートに置くFormation(\"rest\"=何も置かない)")]
    public string formationId = "rest";
    public EncounterIntensity intensity = EncounterIntensity.Easy;
}

public static class EncounterSlots
{
    public static bool IsGround(EncounterSlotKind k) => k == EncounterSlotKind.GroundFront || k == EncounterSlotKind.GroundMiddle || k == EncounterSlotKind.GroundRear;
    public static bool IsAir(EncounterSlotKind k) => k == EncounterSlotKind.AirLow || k == EncounterSlotKind.AirHigh;

    // この敵をこのSlotに置けるか(地上の敵を空中Slotへ置かない / 空中Slotには飛行する敵だけ /
    // 地中Slotには潜る敵だけ)。Specialは地上扱い(将来ステージ固有の置き方を足す枠)。
    public static bool Accepts(EncounterSlotKind k, EnemyDefinition def)
    {
        if (def == null) return false;
        bool flying = def.movementType == EnemyMovementType.Flying;
        bool burrow = def.behaviorKind == EnemyBehaviorKind.BurrowWorm;
        if (IsAir(k)) return flying;
        if (k == EncounterSlotKind.Burrow) return burrow;
        return !flying && !burrow;
    }
}

[Serializable]
public class EncounterSlot
{
    public EncounterSlotKind kind = EncounterSlotKind.GroundFront;
    [Tooltip("Formationの基準点からの前方距離(m)。高速時はFormationの間隔倍率を掛ける")]
    public float xOffset;
    [Tooltip("前後のゆらぎ(m)")]
    public float jitter = 0.3f;
    [Tooltip("このSlotを使う最低Intensity(Easyなら常に、Hardなら強い時だけ)")]
    public EncounterIntensity minIntensity = EncounterIntensity.Easy;
    [Tooltip("このSlotに優先して置く敵ID(候補にあればこの中から選ぶ)")]
    public string[] preferEnemyIds = new string[0];
    [Tooltip("trueなら優先IDの敵が居ない時はこのSlotを使わない")]
    public bool preferOnly;
    [Tooltip("Gap Guard用: 位置を穴の縁から測る")]
    public EncounterPitAnchor pitAnchor = EncounterPitAnchor.None;
}

[Serializable]
public class EncounterFormation
{
    public string formationId = "";
    public string displayName = "";
    [Tooltip("特定ステージ専用のFormation(情報用)")]
    public bool stageSpecific;
    public EncounterIntensity minIntensity = EncounterIntensity.Easy;
    public EncounterIntensity maxIntensity = EncounterIntensity.Hard;
    public List<EncounterSlot> slots = new List<EncounterSlot>();

    [Header("地形条件")]
    [Tooltip("Formationが占める前方の幅(m、間隔倍率適用前)。0ならSlotから自動")]
    public float requiredWidth;
    [Tooltip("床から天井までに必要な高さ(m)。天井の無いステージでは常に満たす")]
    public float requiredHeight = 3.2f;
    [Tooltip("地上の敵はそれぞれ穴から離れた地面に置く")]
    public bool requiresGround = true;
    [Tooltip("Formation全体の範囲に穴が無いこと(密集した隊形用。間隔の広い隊形は各敵の足元だけ確認)")]
    public bool continuousGround;
    [Tooltip("範囲内が平地であること")]
    public bool requiresFlat;
    [Tooltip("空中Slotを置く空間が必要")]
    public bool requiresAir;
    [Tooltip("地中Slotを置ける平らで穴から離れた地面が必要")]
    public bool requiresBurrowGround;
    [Tooltip("地上の敵どうしの最低間隔(m)。重なり防止")]
    public float minGroundGap = 1.2f;

    [Header("地形との相性(Weight倍率)")]
    public float narrowAffinity = 1f;
    public float wideAffinity = 1f;
    public float straightAffinity = 1f;

    [Header("高速時")]
    [Tooltip("走行速度倍率1増えるごとに敵の間隔を何割広げるか")]
    public float spacingSpeedScale = 0.35f;

    [Header("上下ルート(分岐区間の組み合わせ)")]
    public EncounterRouteMode routeMode = EncounterRouteMode.Main;
    public EncounterRouteSide upperSide = new EncounterRouteSide();
    public EncounterRouteSide lowerSide = new EncounterRouteSide();
    [Tooltip("上下を入れ替えてもよい(毎回「上=楽、下=きつい」に固定しない)")]
    public bool allowMirror = true;

    [Header("穴の前後(Gap Guard)")]
    [Tooltip("基準点の先で穴を探し、穴の手前/向こう岸にSlotを置く")]
    public bool requiresPit;
    public float pitSearchStart = 8f;
    public float pitSearchEnd = 34f;
    [Tooltip("この幅より広い穴は使わない(m)")]
    public float maxPitWidth = 6.5f;
    [Tooltip("穴の手前の敵から穴の縁まで最低何m空けるか(敵を倒してから助走して跳べる)")]
    public float pitMinBefore = 6f;
    [Tooltip("穴の向こうの敵は、縁ぎりぎりで跳んだ時の着地点からさらに何m先に置くか(跳んだら必ずぶつかる、を防ぐ)")]
    public float pitLandingMargin = 2.5f;

    public float Width()
    {
        if (requiredWidth > 0f) return requiredWidth;
        if (requiresPit) return pitSearchEnd + maxPitWidth + 14f;
        float w = 0f;
        foreach (var s in slots) w = Mathf.Max(w, s.xOffset + Mathf.Abs(s.jitter));
        return w + 1f;
    }
}

[Serializable]
public class EncounterEnemyEntry
{
    public string enemyId = "";
    public float weight = 1f;
    [Tooltip("この敵が取り得るAI Tierの範囲")]
    public EnemyAiTier minTier = EnemyAiTier.T0;
    public EnemyAiTier maxTier = EnemyAiTier.T0;
    [Tooltip("trueならTierで行動を切り替える(T0=棒立ち、T1=その場で攻撃、T2=+移動)。HPは変えない")]
    public bool tierDrivesMelee;
    [Tooltip("この敵を置いてよいSlot(空=置ける全Slot)。例: Hopperは後方(GroundRear)だけ")]
    public List<EncounterSlotKind> allowedSlots = new List<EncounterSlotKind>();
}

[Serializable]
public class EncounterFormationWeight
{
    public string formationId = "";
    public float weight = 1f;
}

[Serializable]
public class EncounterTierWeights
{
    public float t0 = 1f, t1, t2;
}

[Serializable]
public class EncounterDistanceBand
{
    public string bandName = "";
    public float startDistance;
    [Tooltip("負なら上限なし")]
    public float endDistance = -1f;
    public List<EncounterEnemyEntry> enemies = new List<EncounterEnemyEntry>();
    public List<EncounterFormationWeight> formations = new List<EncounterFormationWeight>();

    [Header("Intensityの基本Weight")]
    public float restWeight = 1f;
    public float easyWeight = 1f;
    public float mediumWeight = 1f;
    public float hardWeight = 0.4f;

    [Header("Intensity別のAI Tier Weight")]
    public EncounterTierWeights easyTiers = new EncounterTierWeights { t0 = 1f };
    public EncounterTierWeights mediumTiers = new EncounterTierWeights { t0 = 1f };
    public EncounterTierWeights hardTiers = new EncounterTierWeights { t0 = 1f };

    public bool Contains(float d) => d >= startDistance && (endDistance < 0f || d < endDistance);

    public EncounterTierWeights TiersFor(EncounterIntensity i) =>
        i == EncounterIntensity.Hard ? hardTiers : i == EncounterIntensity.Medium ? mediumTiers : easyTiers;
}

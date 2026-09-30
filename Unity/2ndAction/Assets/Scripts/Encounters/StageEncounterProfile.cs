using System.Collections.Generic;
using UnityEngine;

// ステージごとのEncounter設定(Inspectorで調整できるData)。
// Resources/Encounters/Profile_<stageId>.asset を置いたステージだけEncounterDirectorが担当する。
// 置いていないステージ(現時点では天空回廊)は従来のSpawn(DistanceTierManager等)のまま。
// 荒野街道(2026-09-28)は上下ルートの組み合わせ(routeEncounters)を使う。
// → 他ステージへ展開する時は、このProfileを作ってFormation/Bandを埋めるだけでよい。
[CreateAssetMenu(menuName = "OneMoreMile/Stage Encounter Profile", fileName = "Profile_stage")]
public class StageEncounterProfile : ScriptableObject
{
    public string stageId = "";
    [Tooltip("falseにすると従来のSpawnへ戻る(切り戻し用)")]
    public bool enabled = true;

    [Header("地形")]
    [Tooltip("SingleRoute = 上下ルートを作らない一本道(自然洞窟)")]
    public StageRouteLayout routeLayout = StageRouteLayout.Default;
    [Tooltip("一本道のとき、長い直線(平地の連続)を作る確率(平地の後の判定ごと)")]
    public float longStraightChance = 0.12f;
    public int longStraightChunksMin = 3;
    public int longStraightChunksMax = 6;

    [Header("上下ルート(分岐のあるステージ)")]
    [Tooltip("分岐区間では上ルート/下ルートに別々の内容(RoutePair Formation)を置く。分岐の外は通常のFormation")]
    public bool routeEncounters;
    [Tooltip("分岐(fork)の手前、何mを通常Encounterから空けておくか(両ルートの中身を見て選ぶ区間)")]
    public float routeLead = 8f;
    [Tooltip("上ルートの坂の上り切り/下り始めから何m離して置くか")]
    public float routeEdgeMargin = 1.5f;
    [Tooltip("ルートの中身をどこまで手前に寄せるか(ルート開始から何m以内で置き場所を探す)。分岐の手前から見えるように")]
    public float routeFrontLoad = 14f;
    [Tooltip("合流(merge)の後、次の通常Encounterまでの間隔(m)")]
    public Vector2 afterMergeGap = new Vector2(6f, 12f);
    [Tooltip("片方のルートに予定のFormationが置けない時(下ルートの穴など)に代わりに使うFormation(穴をまたいでも置ける間隔の広いもの)")]
    public string routeFallbackFormation = "staggered";
    [Tooltip("分岐区間で「両ルートとも休憩」を選ぶWeightの倍率(ルート選択が荒野の中心なので控えめ)")]
    public float branchRestScale = 0.4f;

    [Header("既存Spawnとの関係")]
    [Tooltip("地形チャンクごとの従来Formation Spawnを止め、このProfileで出す")]
    public bool replacesChunkSpawns = true;
    [Tooltip("500mごとの敵の壁(EnemyWallManager)を止める")]
    public bool replacesMilestoneWalls = true;

    [Header("Formation")]
    [Tooltip("このステージ専用のFormation(汎用FormationはGenericFormationsから同じIDで参照)")]
    public List<EncounterFormation> stageFormations = new List<EncounterFormation>();

    [Header("Distance Band(距離による段階的解禁。距離は仮値)")]
    public List<EncounterDistanceBand> bands = new List<EncounterDistanceBand>();

    [Header("ペース")]
    [Tooltip("これより手前の距離では出さない(m)")]
    public float noEncounterBeforeDistance = 60f;
    [Tooltip("プレイヤーの何m先に出すか(画面右端より手前にはならないよう自動で伸ばす)")]
    public float spawnAheadDistance = 46f;
    [Tooltip("画面右端からさらに何m先に出すか(目の前で突然出現させない)")]
    public float offscreenMargin = 8f;
    [Tooltip("Encounter後の間隔(m)。Rest/Easy/Medium/Hardの順")]
    public Vector2 gapAfterEasy = new Vector2(10f, 16f);
    public Vector2 gapAfterMedium = new Vector2(12f, 18f);
    public Vector2 gapAfterHard = new Vector2(16f, 24f);
    [Tooltip("Rest(意図的な休憩区間)の長さ(m)")]
    public Vector2 restLength = new Vector2(26f, 42f);
    [Tooltip("走行速度倍率に対して間隔/休憩をどれだけ伸ばすか(1=速度に比例)")]
    public float gapSpeedScale = 1f;
    // 2026-09-30: 自然加速の上限が100km/h(倍率約5.6)になり、Formationの中の間隔まで速度に合わせて広げると
    // 上下ルートの分岐区間(長さは固定)に収まらなくなった。中の間隔だけはこの倍率で頭打ちにする
    // (Encounterどうしの間隔/休憩は上のgapSpeedScaleのまま速度に比例)。それ以上の速さは高速操作補助が受け持つ。
    [Tooltip("Formationの中の敵どうしの間隔を広げる時に使う速度倍率の上限")]
    public float maxSpacingSpeed = 2.8f;

    [Header("Intensityの波")]
    public float afterRestRestMultiplier = 0.15f;
    public float afterHardHardMultiplier = 0.1f;
    public float afterHardRestMultiplier = 2.5f;
    public float afterEasyMediumMultiplier = 1.4f;
    public float afterMediumHardMultiplier = 1.3f;
    [Tooltip("休憩(Rest)を挟まずに続く戦闘の最大数(これを超えたら必ずRest)")]
    public int maxEncountersWithoutRest = 4;

    [Header("同じFormationの連続防止")]
    public int historySize = 3;
    [Tooltip("直前と同じFormationのWeight倍率")]
    public float repeatPenaltyLast = 0.05f;
    [Tooltip("2〜3回前と同じFormationのWeight倍率")]
    public float repeatPenaltyOlder = 0.35f;

    [Header("空中Slot")]
    public Vector2 airLowHeight = new Vector2(1.6f, 2.4f);
    public Vector2 airHighHeight = new Vector2(3.0f, 4.0f);
    [Tooltip("中空Slot(AirMiddle)の高さ")]
    public Vector2 airMiddleHeight = new Vector2(2.5f, 3.1f);
    [Tooltip("浮島のあるステージ(天空回廊): 空中Slotは真下に浮島があれば浮島の上面から測り、Island Slotは浮島の上に置く")]
    public bool islandAware;
    [Tooltip("天井から離す距離(m)")]
    public float airCeilingMargin = 0.9f;

    [Header("地形の分類(天井までの高さ)")]
    public float narrowClearance = 5.5f;
    public float wideClearance = 8.5f;
    [Tooltip("これ以上平地が続けば「長い直線」")]
    public float straightLength = 26f;

    [Header("ボスとの競合防止")]
    [Tooltip("次のボス出現距離のこの手前からは出さない(m)")]
    public float bossPreBuffer = 90f;
    [Tooltip("ボス戦が終わった直後の休憩(m)")]
    public float bossPostRest = 30f;

    public EncounterDistanceBand BandFor(float distance)
    {
        EncounterDistanceBand best = null;
        foreach (var b in bands) if (b != null && b.Contains(distance)) best = b;
        if (best == null && bands.Count > 0) best = bands[bands.Count - 1].startDistance <= distance ? bands[bands.Count - 1] : bands[0];
        return best;
    }

    public EncounterFormation FindFormation(string id)
    {
        foreach (var f in stageFormations) if (f != null && f.formationId == id) return f;
        var lib = EncounterFormationLibrary.Instance;
        return lib != null ? lib.Find(id) : null;
    }

    // ---- 読み込み ----
    static Dictionary<string, StageEncounterProfile> byStage;

    public static StageEncounterProfile Find(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return null;
        if (byStage == null)
        {
            byStage = new Dictionary<string, StageEncounterProfile>();
            foreach (var p in Resources.LoadAll<StageEncounterProfile>("Encounters"))
                if (p != null && !string.IsNullOrEmpty(p.stageId)) byStage[p.stageId] = p;
        }
        return byStage.TryGetValue(stageId, out var prof) && prof.enabled ? prof : null;
    }

    // ステージの通路構成(地形側が参照)。Profileが無い/無効なら従来どおり。
    public static StageRouteLayout LayoutFor(string stageId)
    {
        var p = Find(stageId);
        return p != null ? p.routeLayout : StageRouteLayout.Default;
    }
}

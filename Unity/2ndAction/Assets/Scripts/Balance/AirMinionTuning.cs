using System.Collections.Generic;
using UnityEngine;

// 空中雑魚(2026-11-05、マスターの依頼「通常3マップ+ラスダンに空中雑魚を追加」)の種類・出現距離・動きの数値。
//  値は Resources/Balance/AirMinionTuning(このクラスの asset。Inspector で直せる)。無ければこのコードの既定値。
//  出し方: EncounterDirector が空中の枠(AirLow/AirMiddle/AirHigh)の敵を選ぶ時、その距離で出せる空中雑魚が居れば
//   substituteChance の確率で今までの空の敵の代わりに置く(群れは 1 枠を数体に広げる)。地上の敵の数は変えない。
//  止める: enabled = false、または開発版の起動引数 -airMinions 0(比較用)
public enum AirRole { Low, High, Diver, Crosser, Swarm }

[CreateAssetMenu(menuName = "OneMoreMile/Air Minion Tuning")]
public class AirMinionTuning : ScriptableObject
{
    [System.Serializable]
    public class Species
    {
        public string id = "";
        public string stage = "";
        public AirRole role;
        [Tooltip("この距離(km)から出る")] public float fromKm;
        [Tooltip("この距離(km)まで(負 = 上限なし)")] public float untilKm = -1f;
        public float weight = 1f;
        [Tooltip("体の中心の高さ(地面/足場から、m)")] public Vector2 height = new Vector2(2.5f, 2.9f);
        [Tooltip("待機中の前へ寄る速さ(低空/群れ)・横切る速さ(横切り)・急降下の速さ(急降下)。m/秒")] public float speed = 1.2f;
        [Tooltip("予兆の秒数")] public float telegraph = 0.5f;
        [Tooltip("群れの数(最小〜最大)")] public Vector2Int group = new Vector2Int(1, 1);
        [Tooltip("急降下: 真下へ降りる(天井の蜘蛛)")] public bool vertical;
        [Tooltip("群れ: V 字の編隊で横切る(突っ込みなし)")] public bool vFormation;
        [Tooltip("群れ: 個体どうしの間隔に掛ける(重なって黒い塊に見える種は広め)")] public float spread = 1f;
        public bool Allows(string stageId, float meters)
            => stage == stageId && meters >= fromKm * 1000f && (untilKm < 0f || meters < untilKm * 1000f);
    }

    [Header("全体")]
    public bool enabled = true;
    [Tooltip("空中の枠で、今までの空の敵の代わりに空中雑魚を置く確率(距離 km → 確率)。間は直線でつなぐ")]
    public Vector2[] substituteChance = { new Vector2(0f, 0.45f), new Vector2(20f, 0.55f), new Vector2(50f, 0.7f), new Vector2(90f, 0.5f) };
    [Tooltip("通常マップの 90km〜(雑魚の大群/ボス3連戦)では群れと横切りを出さない")]
    public float normalRushFromKm = 90f;
    [Tooltip("ラスダンのこの距離(km)からは出さない(99〜100km の静かな道)")]
    public float lastCorridorQuietFromKm = 99f;
    [Tooltip("ラスダン 90〜99km(ボスラッシュ)の置き換えの確率に掛ける")]
    public float lastCorridorRushMul = 0.4f;
    [Tooltip("群れの数に掛ける")] public float groupMul = 1f;
    [Tooltip("横切り: 地上の高さ(跳んで避ける)を選ぶ確率。残りは跳躍の高さ(地上にいれば当たらない)")]
    [Range(0f, 1f)] public float crosserGroundLaneChance = 0.55f;
    [Tooltip("横切り: 地上の高さの体の中心(m)。プレイヤーの体は足元から約 1.0m(立っていれば当たり、跳べば越えられる)")] public float crosserGroundLaneHeight = 0.5f;
    [Tooltip("横切り: 跳躍の高さの体の中心(m)。立っている体の上(当たらない)、跳ぶと当たる")] public float crosserJumpLaneHeight = 2.3f;
    [Tooltip("横切り: 予告の線を出す秒数")] public float crosserWarnSeconds = 0.9f;
    [Tooltip("急降下: 1体が急降下する最大回数")] public int diverMaxDives = 2;
    [Tooltip("判断型の道中イベント(上空封鎖/空の群れ)の空の敵の候補に入れる重み(1種あたり。今までの空の敵は帯の重み 1 前後)")]
    public float decisionEventWeight = 1.5f;

    public List<Species> species = new List<Species>(Defaults());

    // マップごとの 5 役(A 低空 / B 高空 / C 急降下 / D 横切り / E 群れ)。天空は C/D を今のハーピー/狩人が担当するので 3 種。
    //  出現: 0〜20km は A だけ / 20〜50km で B・C / 50km〜 で D・E(判断型の道中イベントと同じ区間から)。ラスダンは 0〜50km で 1〜2 種ずつ
    public static IEnumerable<Species> Defaults()
    {
        // 荒野街道
        yield return new Species { id = "air_sand_swallow", stage = "wasteland_road", role = AirRole.Low, fromKm = 1f, height = new Vector2(2.5f, 2.9f), speed = 1.4f, telegraph = 0.45f };
        yield return new Species { id = "air_vulture", stage = "wasteland_road", role = AirRole.High, fromKm = 20f, height = new Vector2(4.1f, 4.7f), speed = 0.7f, telegraph = 0.5f };
        yield return new Species { id = "air_red_eagle", stage = "wasteland_road", role = AirRole.Diver, fromKm = 30f, height = new Vector2(4.3f, 5.0f), speed = 11f, telegraph = 0.75f };
        yield return new Species { id = "air_sand_spirit", stage = "wasteland_road", role = AirRole.Crosser, fromKm = 50f, speed = 13f, telegraph = 0.9f };
        yield return new Species { id = "air_locust", stage = "wasteland_road", role = AirRole.Swarm, fromKm = 50f, height = new Vector2(2.5f, 4.0f), speed = 1.6f, telegraph = 0.5f, group = new Vector2Int(5, 7) };
        // 自然洞窟
        yield return new Species { id = "air_moth", stage = "natural_cave", role = AirRole.Low, fromKm = 1f, height = new Vector2(2.5f, 2.9f), speed = 1.0f, telegraph = 0.5f };
        yield return new Species { id = "air_jellyfish", stage = "natural_cave", role = AirRole.High, fromKm = 20f, height = new Vector2(3.9f, 4.5f), speed = 0.5f, telegraph = 0.55f };
        yield return new Species { id = "air_thread_spider", stage = "natural_cave", role = AirRole.Diver, fromKm = 30f, height = new Vector2(4.2f, 4.8f), speed = 10f, telegraph = 0.8f, vertical = true };
        yield return new Species { id = "air_crystal_dragonfly", stage = "natural_cave", role = AirRole.Crosser, fromKm = 50f, speed = 14f, telegraph = 0.9f };
        yield return new Species { id = "air_bat_swarm", stage = "natural_cave", role = AirRole.Swarm, fromKm = 50f, height = new Vector2(2.5f, 3.9f), speed = 1.8f, telegraph = 0.45f, group = new Vector2Int(5, 7) };
        // 天空回廊
        yield return new Species { id = "air_sky_wisp", stage = "sky_corridor", role = AirRole.Low, fromKm = 1f, height = new Vector2(2.5f, 2.9f), speed = 1.1f, telegraph = 0.5f };
        yield return new Species { id = "air_cloud_manta", stage = "sky_corridor", role = AirRole.High, fromKm = 20f, height = new Vector2(4.2f, 4.9f), speed = 0.8f, telegraph = 0.55f };
        yield return new Species { id = "air_sky_swallow", stage = "sky_corridor", role = AirRole.Swarm, fromKm = 50f, height = new Vector2(2.6f, 4.0f), speed = 5.5f, telegraph = 0.6f, group = new Vector2Int(5, 5), vFormation = true };
        // ラスダン(LAST CORRIDOR): 0〜50km で 1〜2 種ずつ解放、50km〜 は全種
        yield return new Species { id = "air_wraith", stage = "last_corridor", role = AirRole.Low, fromKm = 2f, height = new Vector2(2.5f, 2.9f), speed = 1.3f, telegraph = 0.45f };
        yield return new Species { id = "air_black_imp", stage = "last_corridor", role = AirRole.Diver, fromKm = 15f, height = new Vector2(4.3f, 5.0f), speed = 12f, telegraph = 0.7f };
        yield return new Species { id = "air_ancient_drone", stage = "last_corridor", role = AirRole.High, fromKm = 30f, height = new Vector2(4.1f, 4.8f), speed = 0.8f, telegraph = 0.5f };
        yield return new Species { id = "air_mech_drake", stage = "last_corridor", role = AirRole.Crosser, fromKm = 45f, speed = 15f, telegraph = 0.85f };
        yield return new Species { id = "air_reaper_familiar", stage = "last_corridor", role = AirRole.Swarm, fromKm = 60f, height = new Vector2(2.5f, 4.0f), speed = 1.9f, telegraph = 0.45f, group = new Vector2Int(5, 6), spread = 1.2f }; // エステル: 鎌が重なると塊に見える → 間隔 1.2 倍
    }

    public float SubstituteChanceAt(float km)
    {
        if (substituteChance == null || substituteChance.Length == 0) return 0f;
        if (km <= substituteChance[0].x) return substituteChance[0].y;
        for (int i = 1; i < substituteChance.Length; i++)
            if (km < substituteChance[i].x)
            {
                var a = substituteChance[i - 1]; var b = substituteChance[i];
                return Mathf.Lerp(a.y, b.y, (km - a.x) / Mathf.Max(0.001f, b.x - a.x));
            }
        return substituteChance[substituteChance.Length - 1].y;
    }

    public Species Find(string id)
    {
        foreach (var s in species) if (s != null && s.id == id) return s;
        foreach (var s in Defaults()) if (s.id == id) return s; // asset が古くて新しい種を持っていない時
        return null;
    }

    static AirMinionTuning inst;
    static bool loaded;
    public static AirMinionTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                inst = Resources.Load<AirMinionTuning>("Balance/AirMinionTuning");
                if (inst == null) { inst = CreateInstance<AirMinionTuning>(); inst.hideFlags = HideFlags.DontSave; }
            }
            return inst;
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static int argState = -1;
    public static bool DisabledByArg
    {
        get
        {
            if (argState < 0)
            {
                argState = 0;
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-airMinions" && a[i + 1] == "0") argState = 1;
            }
            return argState == 1;
        }
    }
#else
    public const bool DisabledByArg = false;
#endif
    public static bool On => I.enabled && !DisabledByArg;
}

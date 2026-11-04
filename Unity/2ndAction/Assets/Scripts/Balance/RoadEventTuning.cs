using System.Collections.Generic;
using UnityEngine;

// 道中イベント(2026-11-04、マスターの依頼「5km ごとのボス以外にも、走っている途中で何かが起きる」)の調整値。
//  値は Resources/Balance/RoadEventTuning(このクラスの asset。Inspector で直せる)。無ければこのコードの既定値。
//  止める: asset の enabled = false、または開発版の起動引数 -roadEvents 0(比較用)。空中ボスの乱入だけ止める: -airRaid 0
//  対象は通常マップ(荒野街道/自然洞窟/天空回廊)の 20〜90km だけ(ラスダン・闘技場・操作の練習・90km 以降の終盤は今までどおり)
[CreateAssetMenu(menuName = "OneMoreMile/Road Event Tuning")]
public class RoadEventTuning : ScriptableObject
{
    [Header("全体")]
    public bool enabled = true;
    [Tooltip("この距離(km)から")]
    public float fromKm = 20f;
    [Tooltip("この距離(km)まで(90km から先は今までの終盤)")]
    public float untilKm = 90f;
    [Tooltip("区間の長さ(km)。区間ごとに1回だけ(5km = 5km ごとのボスの間に1回)")]
    public float sectionKm = 5f;
    [Tooltip("区間にイベントが起きる確率")]
    [Range(0f, 1f)] public float sectionChance = 0.9f;
    [Tooltip("区間の始めからこの距離(km)より後に起こす(5km の関門の直後)")]
    public float startAfterKm = 0.8f;
    [Tooltip("10km の関門の後(門 → 内部 1.2km)はこの距離(km)より後に起こす")]
    public float startAfterBigGateKm = 1.6f;
    [Tooltip("次の関門のこの距離(km)より手前で終える")]
    public float endBeforeKm = 1.0f;

    [Header("種類の重み(区間ごとに1つを選ぶ)")]
    public float rushWeight = 1f;
    public float flyingWeight = 1f;
    public float eliteWeight = 0.8f;
    public float chaseWeight = 0.6f;
    public float airRaidWeight = 0.9f;
    [Tooltip("目的地(城/結晶/神殿)へ近づく後半の区間(章の 5〜10km): 関門の手前の敵ラッシュ・強敵の重みに掛ける")]
    public float approachRushMul = 2f, approachEliteMul = 1.5f;
    [Tooltip("章の前半の区間(0〜5km、内部を抜けた後): 飛行編隊・追跡・乱入の重みに掛ける")]
    public float openFlyingMul = 1.5f, openChaseMul = 1.3f, openRaidMul = 1.2f;
    [Tooltip("天空回廊: 飛行編隊・乱入の重みに掛ける(空の旅)")]
    public float skyFlyingMul = 1.6f, skyRaidMul = 1.5f;
    [Tooltip("自然洞窟: 強敵の重みに掛ける(結晶/地底の大物)")]
    public float caveEliteMul = 1.3f;
    [Tooltip("目的地の手前の敵ラッシュを置く位置(章の km。城門の前)")]
    public Vector2 approachRushAtKm = new Vector2(8.2f, 9.0f);

    [Header("敵ラッシュ(数十秒だけ敵の間隔を詰め、休憩を入れない)")]
    public float rushSeconds = 24f;
    [Tooltip("Encounter の間隔に掛ける(小さいほど多い)")]
    public float rushPace = 0.5f;
    [Tooltip("激しさの寄せ(0〜1。終盤の大群の段階 0〜1 と同じ物差し)")]
    [Range(0f, 1f)] public float rushSurge = 0.8f;

    [Header("飛行編隊(飛ぶ敵の組を多く)")]
    public float flyingSeconds = 22f;
    public float flyingPace = 0.75f;
    [Tooltip("飛ぶ敵の枠がある Formation の重み / 地上だけの Formation の重み")]
    public float flyingAirFormationMul = 4f, flyingGroundOnlyMul = 0.25f;
    [Tooltip("飛ぶ敵(movementType = Flying)を選ぶ重みに掛ける")]
    public float flyingEnemyMul = 4f;

    [Header("強敵の混成(HP の多い敵が数体混ざる)")]
    public float eliteSeconds = 22f;
    [Tooltip("この HP 倍率以上の敵を強敵とみなす")]
    public float eliteHpMultiplier = 1.5f;
    public float eliteEnemyMul = 5f;
    [Tooltip("激しい(Hard)Encounter の重みに掛ける")]
    public float eliteHardMul = 2f;

    [Header("追跡(速い敵が多い)")]
    public float chaseSeconds = 20f;
    public float chasePace = 0.75f;
    public float chaseEnemyMul = 5f;

    [Header("空中ボスの乱入(走ったまま戦う。距離は止めない)")]
    [Tooltip("この距離(km)から")]
    public float raidUnlockKm = 30f;
    [Tooltip("1回出たら、この距離(km)は次を出さない")]
    public float raidCooldownKm = 8f;
    [Tooltip("次の関門(5km/10km のボス)までこの距離(km)より近い時は出さない")]
    public float raidMinGateGapKm = 1.8f;
    [Tooltip("戦いの途中で次の関門のこの距離(m)まで来たら、乱入ボスは飛び去る(報酬なし)")]
    public float raidFleeBeforeGateM = 250f;
    [Tooltip("この秒数で倒せなければ飛び去る(報酬なし。長く足止めしない)")]
    public float raidTimeLimit = 60f;
    [Tooltip("強さ: その距離での再戦の強さ(関門のボスと同じ計算)× この倍率")]
    public float raidHpMul = 0.4f;
    [Tooltip("ボスごとの HP の倍率(raidHpMul にさらに掛ける)。天空の大蛇/フェニックスは高い所を飛ぶので 60 秒で倒しきれないことが多かった → まず 90%(エステル: 「倒せる時もあれば逃げられる時もある」緊張感は残す)")]
    public List<RaidHp> raidHpByKey = new List<RaidHp> { new RaidHp("Sky/SkySerpent", 0.9f), new RaidHp("Sky/Phoenix", 0.9f) };
    [System.Serializable] public class RaidHp { public string key; public float mul = 1f; public RaidHp(string key, float mul) { this.key = key; this.mul = mul; } }
    public float RaidHpFor(string key) { if (raidHpByKey != null) foreach (var r in raidHpByKey) if (r != null && r.key == key) return Mathf.Max(0.05f, r.mul); return 1f; }
    [Tooltip("崩し(BREAK)のゲージの大きさに掛ける(小さいほど BREAK しやすい)")]
    public float raidStaggerMul = 0.7f;
    [Tooltip("必殺技を使わない(技を絞って短い事件にする)")]
    public bool raidNoUltimate = true;
    [Tooltip("予告(影が空を横切る〜画面に入る)の秒数")]
    public float raidTelegraphSeconds = 1.6f;
    [Tooltip("報酬: ボスの MILE に掛ける(関門のボスより軽く)")]
    public float raidMileMul = 0.5f;
    [Tooltip("報酬: 撃破で回復するハートの数")]
    public int raidHealHearts = 1;
    [Tooltip("報酬: EXP の格(Small = 1km の関門のボスと同じ割合)")]
    public RunRewards.BossClass raidExpClass = RunRewards.BossClass.Small;
    [Tooltip("乱入できるボス(ステージ: 空を飛んで追いついてきても自然なボスだけ)")]
    public List<RaidPool> raidPools = DefaultRaidPools();

    [System.Serializable]
    public class RaidPool
    {
        public string stageId;
        public List<string> keys = new List<string>();
        public RaidPool(string stageId, params string[] keys) { this.stageId = stageId; this.keys.AddRange(keys); }
    }

    // 荒野: グリフォン(鷲獅子)/デーモン(翼の悪魔)。洞窟: 大コウモリ/飛竜(ドレイク)。天空: フェニックス/天空の大蛇/クラゲ
    // (タグ AIR のボスから。地上のゴーレム/巨人/狼、最後の大ボス(古代の魔王/天界の守護者)、別の仕組みの竜/魔人は入れない)
    public static List<RaidPool> DefaultRaidPools() => new List<RaidPool>
    {
        new RaidPool("wasteland_road", "Wild/Griffin", "Wild/Demon"),
        new RaidPool("natural_cave", "Cave/Bat", "Cave/Drake"),
        new RaidPool("sky_corridor", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Jellyfish"),
    };

    public List<string> RaidKeys(string stageId)
    {
        if (raidPools != null) foreach (var p in raidPools) if (p != null && p.stageId == stageId) return p.keys;
        return null;
    }

    // ---- 読み込み ----
    static RoadEventTuning inst; static bool loaded;
    public static bool DisabledByArg, RaidDisabledByArg;
    public static RoadEventTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                inst = Resources.Load<RoadEventTuning>("Balance/RoadEventTuning");
                if (inst == null) { inst = CreateInstance<RoadEventTuning>(); inst.hideFlags = HideFlags.DontSave; }
                if (inst.raidPools == null || inst.raidPools.Count == 0) inst.raidPools = DefaultRaidPools();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(a, "-roadEvents");
                if (i >= 0 && i + 1 < a.Length) DisabledByArg = a[i + 1] == "0";
                i = System.Array.IndexOf(a, "-airRaid");
                if (i >= 0 && i + 1 < a.Length) RaidDisabledByArg = a[i + 1] == "0";
#endif
            }
            return inst;
        }
    }
    public static bool On => I.enabled && !DisabledByArg;
}

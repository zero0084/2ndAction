using UnityEngine;

// 判断型の道中イベント(2026-11-05、マスターの依頼「50km 以降は少しだけ人が画面を見て判断する」)の調整値。
//  値は Resources/Balance/DecisionEventTuning(このクラスの asset。Inspector で直せる)。無ければこのコードの既定値。
//  止める: asset の enabled = false、または開発版の起動引数 -decisionEvents 0(比較用)
//  対象は通常マップ(荒野街道/自然洞窟/天空回廊)の 50〜90km のソロ(CPU サポート含む)。ラスダン/マルチ/90km 以降の終盤は今までどおり
[CreateAssetMenu(menuName = "OneMoreMile/Decision Event Tuning")]
public class DecisionEventTuning : ScriptableObject
{
    [Header("全体")]
    public bool enabled = true;
    [Tooltip("この距離(km)から")]
    public float fromKm = 50f;
    [Tooltip("この距離(km)まで(90km から先は今までの終盤のラッシュ)")]
    public float untilKm = 90f;
    [Tooltip("放置時の想定被弾量: 敵の数・掃討の数に掛ける(1 = 既定。上げると放置で早く倒れる)")]
    public float intensity = 1f;

    [System.Serializable]
    public class Band
    {
        public float fromKm;
        [Tooltip("候補になった時に起きる確率")] [Range(0f, 1f)] public float chance = 0.8f;
        [Tooltip("次の候補までの走行時間(秒、最小〜最大)。距離 = 速さ × 秒(下の minGapMeters より短くしない)")]
        public Vector2 gapSeconds = new Vector2(18f, 30f);
        [Tooltip("イベントが終わってから、次の候補まで必ず空ける時間(秒)")]
        public float restAfterSeconds = 5f;
        [Tooltip("予兆の時間に掛ける(50〜60km は長め = 仕組みを覚える区間)")]
        public float warnMul = 1f;
        [Tooltip("安全な帯の広さ・敵の間隔に掛ける(大きいほど易しい)")]
        public float safeMul = 1f;
        [Tooltip("2種類を重ねる確率(75km〜)")]
        [Range(0f, 1f)] public float comboChance = 0f;
        [Tooltip("敵の数に掛ける")]
        public float countMul = 1f;
        [Header("種類の重み")]
        public float skyBlock = 1f, groundBlock = 1f, alternate = 0f, telegraphRush = 1f, heavyHitter = 0.6f, aerialBrute = 0.6f;
        [Tooltip("今までの道中イベント(敵ラッシュ/飛行編隊/強敵/追跡/空中ボスの乱入)もこの管理で選ぶ。その重み")]
        public float legacyRush = 0.35f, legacyFlying = 0.25f, legacyElite = 0.25f, legacyChase = 0.25f, airRaid = 0.6f;
        [Tooltip("通常の Encounter の敵を高火力敵にする確率(1体ずつ)")]
        [Range(0f, 1f)] public float heavyHitterInNormal = 0f;
        [Tooltip("空中雑魚(2026-11-05): 急降下の連続 AirDive / 横切りの連続 AirCross の重み(その距離で急降下/横切りの種が居る時だけ)")]
        public float airDive = 0.8f, airCross = 0.8f;
    }

    [Tooltip("距離の帯(fromKm の小さい順)")]
    public Band[] bands = DefaultBands();
    public static Band[] DefaultBands() => new[]
    {
        // 50〜60km: 少なめ・予兆長め・安全帯広め(仕組みを理解する区間)。種類は単純な物だけ
        new Band { fromKm = 50f, chance = 0.7f, gapSeconds = new Vector2(24f, 38f), restAfterSeconds = 7f, warnMul = 1.3f, safeMul = 1.25f, countMul = 0.85f,
                   skyBlock = 1f, groundBlock = 1f, alternate = 0f, telegraphRush = 1.2f, heavyHitter = 0.5f, aerialBrute = 0.7f,
                   legacyRush = 0.25f, legacyFlying = 0.2f, legacyElite = 0.2f, legacyChase = 0.2f, airRaid = 0.5f, heavyHitterInNormal = 0.02f },
        // 60〜75km: 通常頻度。種類を増やす(上下交互が出始める)
        new Band { fromKm = 60f, chance = 0.85f, gapSeconds = new Vector2(12f, 22f), restAfterSeconds = 5f, warnMul = 1f, safeMul = 1f, countMul = 1f,
                   skyBlock = 1f, groundBlock = 1f, alternate = 0.8f, telegraphRush = 1.2f, heavyHitter = 0.8f, aerialBrute = 0.9f,
                   legacyRush = 0.3f, legacyFlying = 0.25f, legacyElite = 0.25f, legacyChase = 0.25f, airRaid = 0.6f, heavyHitterInNormal = 0.06f },
        // 75〜90km: やや頻繁。2種類の組み合わせもある
        new Band { fromKm = 75f, chance = 0.9f, gapSeconds = new Vector2(8f, 15f), restAfterSeconds = 4f, warnMul = 0.95f, safeMul = 0.95f, comboChance = 0.35f, countMul = 1.15f,
                   skyBlock = 1f, groundBlock = 1f, alternate = 1.1f, telegraphRush = 1.2f, heavyHitter = 1f, aerialBrute = 1f,
                   legacyRush = 0.3f, legacyFlying = 0.25f, legacyElite = 0.25f, legacyChase = 0.25f, airRaid = 0.6f, heavyHitterInNormal = 0.1f },
    };
    [Header("ラスダン(LAST CORRIDOR, 2026-11-05 空中雑魚と一緒に): ボスが多いので控えめ。種類は 帯/上空封鎖/地上封鎖/急降下/横切り だけ")]
    public bool lastCorridor = true;
    public Band[] lastCorridorBands = DefaultLastCorridorBands();
    public static Band[] DefaultLastCorridorBands() => new[]
    {
        // 50〜65km: まれに(地上 + 空の混成が増える区間。Encounter が主役)
        new Band { fromKm = 50f, chance = 0.5f, gapSeconds = new Vector2(34f, 50f), restAfterSeconds = 8f, warnMul = 1.2f, safeMul = 1.15f, countMul = 0.85f,
                   skyBlock = 1f, groundBlock = 0.6f, alternate = 0f, telegraphRush = 1f, heavyHitter = 0f, aerialBrute = 0f,
                   legacyRush = 0f, legacyFlying = 0f, legacyElite = 0f, legacyChase = 0f, airRaid = 0f, airDive = 0.6f, airCross = 0.4f },
        // 65〜80km: 少し増やす
        new Band { fromKm = 65f, chance = 0.6f, gapSeconds = new Vector2(26f, 40f), restAfterSeconds = 6f, warnMul = 1.05f, safeMul = 1f, countMul = 1f,
                   skyBlock = 1f, groundBlock = 0.7f, alternate = 0.6f, telegraphRush = 1f, heavyHitter = 0f, aerialBrute = 0f,
                   legacyRush = 0f, legacyFlying = 0f, legacyElite = 0f, legacyChase = 0f, airRaid = 0f, airDive = 0.9f, airCross = 0.8f },
        // 80〜90km: 空中封鎖・編隊・急降下(集大成)。90km からはボスラッシュが優先(出さない)
        new Band { fromKm = 80f, chance = 0.7f, gapSeconds = new Vector2(20f, 32f), restAfterSeconds = 5f, warnMul = 1f, safeMul = 1f, countMul = 1.05f,
                   skyBlock = 1.3f, groundBlock = 0.5f, alternate = 0.9f, telegraphRush = 0.9f, heavyHitter = 0f, aerialBrute = 0f,
                   legacyRush = 0f, legacyFlying = 0f, legacyElite = 0f, legacyChase = 0f, airRaid = 0f, airDive = 1.3f, airCross = 1.2f },
    };
    [Tooltip("急降下の連続: 数(最小〜最大)と、1体ずつの間隔(走行の秒)")]
    public Vector2 airDiveCount = new Vector2(3f, 4f);
    public float airDiveSpacingSeconds = 1.5f;
    [Tooltip("横切りの連続: 数(最小〜最大)と、1体ずつの間隔(秒)。高さは 地上 → 跳躍 → 地上 … と交互")]
    public Vector2 airCrossCount = new Vector2(3f, 4f);
    public float airCrossIntervalSeconds = 1.5f;

    public Band BandFor(string stage, float km)
    {
        if (stage == "last_corridor")
        {
            var arr = lastCorridorBands != null && lastCorridorBands.Length > 0 ? lastCorridorBands : DefaultLastCorridorBands();
            Band lb = arr[0];
            foreach (var x in arr) if (x != null && km >= x.fromKm) lb = x;
            return lb;
        }
        return BandAt(km);
    }

    public Band BandAt(float km)
    {
        Band b = bands != null && bands.Length > 0 ? bands[0] : DefaultBands()[0];
        if (bands != null) foreach (var x in bands) if (x != null && km >= x.fromKm) b = x;
        return b;
    }

    [Header("間隔の安全装置")]
    [Tooltip("イベントどうしの最低の間隔(m)。遅い時の下限")]
    public float minGapMeters = 260f;
    [Tooltip("次の関門(ボス)までこの距離(m)より近い時は始めない(+ 速さ × gateClearSeconds)")]
    public float gateClearMeters = 500f;
    public float gateClearSeconds = 6f;
    [Tooltip("ボス戦の後、この秒数は始めない")]
    public float afterBossSeconds = 5f;
    [Tooltip("今までの道中イベント(空中ボスの乱入など)が終わってから、この秒数は始めない(エステル: 乱入の後は最低 5 秒)")]
    public float afterLegacySeconds = 5f;
    [Tooltip("この秒数で終わらなければイベントを閉じる(安全装置)")]
    public float maxEventSeconds = 25f;

    [Header("予兆(帯)")]
    [Tooltip("予兆が出てから敵が来るまでの秒数(通常速度)。0.8〜1.5 秒の範囲で帯ごとの warnMul を掛ける")]
    public float warnSeconds = 1.1f;
    [Tooltip("速い時に予兆を早める: この速さ(km/h)で warnHighSpeedExtra 秒足す(間は直線)")]
    public float warnHighSpeedKmh = 300f;
    public float warnHighSpeedExtra = 0.25f;
    [Tooltip("予兆の上限・下限(秒)")]
    public float warnMin = 0.8f, warnMax = 1.6f;
    [Tooltip("帯の不透明度(目立ちすぎないように)")]
    [Range(0f, 1f)] public float bandAlpha = 0.26f;

    [Header("危険帯の高さ(地面からの m)")]
    [Tooltip("地上の帯の上端。ここより上(ジャンプ)は安全")]
    public float groundLaneTop = 1.25f;
    [Tooltip("空中の帯の下端。立っている(地上を走る)人の頭より上")]
    public float airLaneBottom = 2.15f;
    public float airLaneTop = 4.6f;

    [Header("予兆付き突進群(帯を敵の群れが横切る)")]
    [Tooltip("群れの数(帯ごとの countMul を掛ける)")]
    public Vector2Int sweepCount = new Vector2Int(5, 8);
    [Tooltip("群れが画面を横切る秒数")]
    public float sweepSeconds = 0.75f;
    [Tooltip("群れの先頭から最後までがプレイヤーの位置を通る秒数(短いほど1回のジャンプで避けやすい)")]
    public float sweepSpreadSeconds = 0.28f;
    [Tooltip("横切る敵の高さ(m)")]
    public float sweeperHeight = 1.15f;
    [Tooltip("地上の帯の確率(残りは空中の帯)")]
    [Range(0f, 1f)] public float groundLaneChance = 0.65f;
    [Tooltip("この距離(km)から、群れの体当たりを大型の敵と同じ重さにする(60〜90km = 2 ハート、90km = 3)。それより前は普通の体当たり(1)")]
    public float sweepHeavyFromKm = 60f;

    [Header("上下交互")]
    [Tooltip("帯の数(地上→空→地上 など)")]
    public int alternateSteps = 3;
    [Tooltip("1つの帯が終わってから次の帯の予兆までの秒数")]
    public float alternateGapSeconds = 0.25f;

    [Header("上空封鎖(飛ぶ敵を上に並べる。地面は空ける)")]
    public Vector2Int skyCount = new Vector2Int(6, 9);
    [Tooltip("封鎖の長さ = 速さ × この秒数(下限 skyMinLength m)")]
    public float skySeconds = 1.6f;
    public float skyMinLength = 24f;
    [Tooltip("飛ぶ敵の高さ(地面からの m)")]
    public Vector2 skyHeights = new Vector2(2.8f, 4.6f); // 下端は立っている頭より十分上(上下に揺れても帯の中)

    [Header("地上封鎖(重い敵を地面に並べる。空は空ける)")]
    public Vector2Int groundCount = new Vector2Int(3, 5);
    [Tooltip("封鎖の長さ = 速さ × この秒数(ジャンプ1回で越えられる長さ。下限/上限 m)")]
    public float groundSeconds = 0.34f;
    public float groundMinLength = 5f, groundMaxLength = 60f;
    [Tooltip("地上の攻撃で後ろへ下がらない(体当たりが当たる)。空中へ逃げれば簡単に抜けられる")]
    public bool groundBlockersResistKnockback = true;

    [Header("高火力敵")]
    [Tooltip("体当たり・攻撃のダメージに足すハートの数(1発で即死はしない)")]
    public int heavyExtraHearts = 1;
    [Tooltip("イベントで出す数")]
    public Vector2Int heavyCount = new Vector2Int(2, 3);
    [Tooltip("HP の倍率(極端に上げない)")]
    public float heavyHpMul = 1f;

    [Header("エリアルで退かす敵")]
    public Vector2Int bruteCount = new Vector2Int(1, 2);
    [Tooltip("HP = 今の距離の大型の HP × この倍率(異常に高くしない)")]
    public float bruteHpMul = 1.6f;
    [Tooltip("地上の攻撃のダメージに掛ける(ほとんど効かない)")]
    public float bruteGroundDamageMul = 0.3f;
    [Tooltip("打ち上げ(Launch)の高さに掛ける")]
    public float bruteLaunchMul = 1.45f;
    [Tooltip("空中の攻撃のダメージに掛ける")]
    public float bruteAirDamageMul = 2.5f;
    [Tooltip("空中の命中がこの回数で、大きく吹き飛ぶ(撃破)")]
    public int bruteAirHitsToFling = 2;

    [Header("背景の旅との連携(重み)")]
    [Tooltip("目的地(城/結晶/神殿)へ近づく後半(章の 5〜10km): 地上の封鎖/突進/重い敵の重みに掛ける")]
    public float approachGroundMul = 1.4f;
    [Tooltip("天空回廊: 上空封鎖・空中の帯の重みに掛ける")]
    public float skyStageAirMul = 1.4f;
    [Tooltip("自然洞窟: 地上封鎖の重みに掛ける(狭い地底)")]
    public float caveGroundMul = 1.35f;

    // ---- 読み込み ----
    static DecisionEventTuning inst; static bool loaded;
    public static bool DisabledByArg;
    public static DecisionEventTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                inst = Resources.Load<DecisionEventTuning>("Balance/DecisionEventTuning");
                if (inst == null) { inst = CreateInstance<DecisionEventTuning>(); inst.hideFlags = HideFlags.DontSave; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(a, "-decisionEvents");
                if (i >= 0 && i + 1 < a.Length) DisabledByArg = a[i + 1] == "0";
                i = System.Array.IndexOf(a, "-deIntensity");
                if (i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) inst.intensity = v;
#endif
            }
            return inst;
        }
    }
    public static bool On => I.enabled && !DisabledByArg;
}

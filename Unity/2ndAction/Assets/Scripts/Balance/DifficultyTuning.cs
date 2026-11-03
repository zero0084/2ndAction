using UnityEngine;

// 通常マップの中盤以降の難易度(2026-11-03、テスターの感想「65km まで余裕」「雑魚は硬いが怖くない」「1km ごとのボスが多い」)。
//  方針: 敵は倒しやすく(雑魚の HP を中盤から下げる)、攻撃は痛く(距離の帯で被ダメージを段階的に)、数は多く(間隔を詰める)。
//  ボスは 0〜20km は 1km ごと、20〜90km は 5km ごと(10km は大ボス)。90km 以降(90km ボス/大群/3連戦/死神)は今までどおり。
//  対象は通常マップ(荒野街道/自然洞窟/天空回廊)だけ。ラストダンジョン・闘技場・操作の練習・死神の接触(即死)・落下は対象外。
//  値は Resources/Balance/DifficultyTuning(このクラスの asset。Inspector で直せる)。無ければこのコードの既定値。
//  旧仕様へ戻す: asset の legacy = true、または開発版の起動引数 -diffLegacy 1(比較用)
[CreateAssetMenu(menuName = "OneMoreMile/Difficulty Tuning")]
public class DifficultyTuning : ScriptableObject
{
    [Header("全体")]
    public bool legacy = false;

    [Header("被ダメージ(ハートの数。x = 距離 km、y = ハート。間は直線で、最後にハート単位へ丸める)")]
    [Tooltip("雑魚の体当たり(軽い接触)")]
    public Vector2[] contactHearts = { new Vector2(0, 1), new Vector2(60, 1), new Vector2(90, 2) };
    [Tooltip("雑魚の通常攻撃(近接の攻撃判定・弾)")]
    public Vector2[] normalHearts = { new Vector2(0, 1), new Vector2(30, 1), new Vector2(60, 2) };
    [Tooltip("大型の雑魚(HP 倍率が largeHpMultiplier 以上)の攻撃・接触")]
    public Vector2[] largeHearts = { new Vector2(0, 1), new Vector2(29.9f, 1), new Vector2(30, 2), new Vector2(60, 2), new Vector2(90, 3) };
    [Tooltip("ボスの通常の攻撃(元のダメージがハート1つ)。2026-11-03 の比較: 60km 台から2にすると、不注意なプレイで 63〜65km のボス1体に2回倒された → 70km から2")]
    public Vector2[] bossHearts = { new Vector2(0, 1), new Vector2(69.9f, 1), new Vector2(70, 2) };
    [Tooltip("ボスの大技(元のダメージがハート2つ以上)")]
    public Vector2[] bossHeavyHearts = { new Vector2(0, 2), new Vector2(60, 2), new Vector2(90, 3) };
    [Tooltip("障害物/地形の針/壁(今までどおり)")]
    public Vector2[] obstacleHearts = { new Vector2(0, 1) };
    public float largeHpMultiplier = 1.75f;

    [Header("雑魚の HP(今の距離の HP に掛ける倍率。x = km)")]
    public Vector2[] smallHpScale = { new Vector2(0, 1f), new Vector2(20, 1f), new Vector2(40, 0.8f), new Vector2(65, 0.65f), new Vector2(90, 0.6f) };
    public Vector2[] largeHpScale = { new Vector2(0, 1f), new Vector2(20, 1f), new Vector2(60, 0.85f), new Vector2(90, 0.8f) };

    [Header("敵の密度(Encounter の間隔と休憩の長さに掛ける。小さいほど多い)")]
    public Vector2[] gapScale = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(45, 0.85f), new Vector2(60, 0.75f), new Vector2(80, 0.66f), new Vector2(90, 0.58f) };
    [Tooltip("休憩(Rest)を選ぶ重みに掛ける")]
    public Vector2[] restWeightScale = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(60, 0.7f), new Vector2(80, 0.5f), new Vector2(90, 0.4f) };
    [Tooltip("激しい(Hard)Encounter の重みに掛ける")]
    public Vector2[] hardWeightScale = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(60, 1.4f), new Vector2(90, 1.8f) };

    [Header("同時に攻撃できる雑魚の数(ZakoAttackLimiter)をこの距離から効かせる")]
    public float attackLimiterFromKm = 50f;
    [Tooltip("密度(間隔/休憩/激しさ)と同時攻撃の上限を変えるのはここまで。この先(90km ボス/大群/3連戦/死神)は今までどおり")]
    public float densityUntilKm = 90f;

    [Header("ボスの関門")]
    [Tooltip("ここまでは 1km ごと")]
    public float everyKmUntil = 20f;
    [Tooltip("ここまでは 5km ごと(10km は大ボス)。この先は今までどおり(90km ボス・3連戦)")]
    public float everyFiveKmUntil = 90f;
    [Tooltip("飛ばした 1km の地点で BONUS ZONE の抽選をする(ボスの後の抽選と同じ確率)")]
    public bool bonusRollAtSkippedGates = true;

    // ---- 読み込み ----
    static DifficultyTuning inst; static bool loaded;
    public static bool LegacyOverride; // 開発版 -diffLegacy 1
    public static DifficultyTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                inst = Resources.Load<DifficultyTuning>("Balance/DifficultyTuning");
                if (inst == null) inst = CreateInstance<DifficultyTuning>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(a, "-diffLegacy");
                if (i >= 0 && i + 1 < a.Length) LegacyOverride = a[i + 1] == "1";
#endif
            }
            return inst;
        }
    }

    // 今のランで新しい難易度を使うか(通常マップのソロ/マルチ。ラスダン/闘技場/練習は対象外)
    public static bool Active
    {
        get
        {
            if (LegacyOverride || I.legacy) return false;
            var gm = GameManager.Instance;
            if (gm == null || !gm.HasStarted || ArenaMode.Active || TutorialMode.Active) return false;
            string st = gm.ActiveRunStageId;
            return st == "wasteland_road" || st == "natural_cave" || st == "sky_corridor";
        }
    }

    public static float Eval(Vector2[] keys, float km)
    {
        if (keys == null || keys.Length == 0) return 1f;
        if (km <= keys[0].x) return keys[0].y;
        for (int i = 1; i < keys.Length; i++)
            if (km <= keys[i].x) return Mathf.Lerp(keys[i - 1].y, keys[i].y, Mathf.InverseLerp(keys[i - 1].x, keys[i].x, km));
        return keys[keys.Length - 1].y;
    }

    public enum Hit { None, Contact, Normal, Large, Boss, BossHeavy, Obstacle }
    // 敵の側が TakeDamage の直前に書く(どの敵か)。1回使ったら消える
    static float nextHp = -1f; static int nextHpFrame = -1;
    public static float NextAttackerHpMul { get => nextHpFrame == Time.frameCount ? nextHp : -1f; set { nextHp = value; nextHpFrame = Time.frameCount; } } // 同じフレームの被弾にだけ効く
    public static int Adjusted, Unchanged; // 確認用
    public static string LastAdjust = "";

    // GameManager.TryDamagePlayer から: 元のダメージ(HP)→ 帯と攻撃の種類で決めたダメージ
    public static int Adjust(int amount, string reason, float distance)
    {
        float attackerHp = NextAttackerHpMul; nextHpFrame = -1;
        if (!Active || string.IsNullOrEmpty(reason)) { Unchanged++; return amount; }
        // PlayerController.TakeDamage は "HPZero:<出どころ>" で渡す(落下は "DeathY:...")。マルチの HOST 判定は "...(host-judged)" が付く
        if (reason.StartsWith("HPZero:")) reason = reason.Substring(7);
        if (reason.StartsWith("DeathY") || reason.StartsWith("Reaper") || reason.StartsWith("Fall") || reason.StartsWith("HPZero")) { Unchanged++; return amount; }
        var t = I; float km = distance / 1000f;
        bool large = attackerHp >= t.largeHpMultiplier;
        Hit kind = Classify(reason, amount, large);
        Vector2[] curve = kind switch
        {
            Hit.Contact => large ? t.largeHearts : t.contactHearts,
            Hit.Normal => large ? t.largeHearts : t.normalHearts,
            Hit.Large => t.largeHearts,
            Hit.Boss => t.bossHearts,
            Hit.BossHeavy => t.bossHeavyHearts,
            Hit.Obstacle => t.obstacleHearts,
            _ => null,
        };
        if (curve == null) { Unchanged++; return amount; }
        int hearts = Mathf.Max(1, Mathf.RoundToInt(Eval(curve, km)));
        int result = hearts * CombatScale.HpPerHeart;
        Adjusted++;
        LastAdjust = $"{reason} {kind}{(large ? "(large)" : "")} @{km:F1}km: {amount} -> {result}";
        return result;
    }

    // 元のダメージの大きさ(ボスは再戦の倍率を戻して)と出どころで種類を決める
    static Hit Classify(string reason, int amount, bool large)
    {
        if (reason.StartsWith("Enemy:")) return Hit.Contact;
        if (reason.StartsWith("EnemyMelee:")) return Hit.Normal;
        if (reason.StartsWith("Obstacle") || reason.StartsWith("TerrainWall") || reason.StartsWith("CeilingSpike")) return Hit.Obstacle;
        bool boss = reason.StartsWith("Boss") || reason.StartsWith("WildBoss") || reason.StartsWith("CaveBoss") || reason.StartsWith("Dragon")
            || reason.StartsWith("SkyStrike") || reason.StartsWith("Majin") || reason.StartsWith("Mech");
        // 弾(雑魚とボスで共通)とマルチの JOIN が受けた攻撃(Net<種類>#番号: HOST の本物の攻撃の写し)は、ボス戦中ならボスの攻撃とみなす
        bool shared = reason.StartsWith("Fireball:") || reason.StartsWith("Net");
        if (shared) boss = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        if (shared && !boss) return large ? Hit.Large : Hit.Normal;
        if (!boss) return Hit.None;
        float baseAmount = amount / Mathf.Max(1f, BossManager.DamageMul);
        return baseAmount >= CombatScale.PlayerHeavyHit - 0.5f ? Hit.BossHeavy : Hit.Boss;
    }

    // 雑魚の HP の倍率(DistanceTierManager.EnemyHpFor)
    public static float HpScale(float hpMultiplier, float distance)
    {
        if (LegacyOverride || I.legacy) return 1f;
        var gm = GameManager.Instance;
        string st = gm != null ? gm.ActiveRunStageId : "";
        if (!(st == "wasteland_road" || st == "natural_cave" || st == "sky_corridor") || ArenaMode.Active) return 1f;
        if (hpMultiplier < 0.7f || hpMultiplier >= 5f) return 1f; // BONUS の宝の敵などはそのまま
        return Eval(hpMultiplier >= I.largeHpMultiplier ? I.largeHpScale : I.smallHpScale, distance / 1000f);
    }

    // 関門 k(k km)を飛ばすか(20〜90km は 5km ごとだけ)
    public static bool SkipsGate(int k, float gateMeters)
    {
        if (!Active) return false;
        float km = k * gateMeters / 1000f;
        if (km <= I.everyKmUntil || km >= I.everyFiveKmUntil) return false;
        return Mathf.RoundToInt(km) % 5 != 0;
    }

    static bool DensityOn(float distance) => Active && distance < I.densityUntilKm * 1000f;
    public static float GapScale(float distance) => DensityOn(distance) ? Mathf.Max(0.2f, Eval(I.gapScale, distance / 1000f)) : 1f;
    public static float RestWeightScale(float distance) => DensityOn(distance) ? Mathf.Max(0f, Eval(I.restWeightScale, distance / 1000f)) : 1f;
    public static float HardWeightScale(float distance) => DensityOn(distance) ? Mathf.Max(0f, Eval(I.hardWeightScale, distance / 1000f)) : 1f;
    public static bool LimiterOn(float distance) => DensityOn(distance) && distance >= I.attackLimiterFromKm * 1000f;
}

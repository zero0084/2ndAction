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
    public Vector2[] contactHearts = { new Vector2(0, 1), new Vector2(60, 1), new Vector2(85, 1.8f), new Vector2(97, 2.3f) };
    [Tooltip("2026-11-05 デッキ 3 段階の調整: ハートの曲線の間の値(例 1.4)を、確率で 1 か 2 にする(0.4 の確率で 2)。"
           + "四捨五入だと 70km のボスでいきなり 1→2 になり、放置の倒れる地点がそこへ集中した。false = 四捨五入(前の作り)")]
    public bool stochasticHearts = true;
    [Tooltip("雑魚の通常攻撃(近接の攻撃判定・弾)")]
    public Vector2[] normalHearts = { new Vector2(0, 1), new Vector2(30, 1), new Vector2(60, 2), new Vector2(85, 2.3f), new Vector2(97, 2.6f) };
    [Tooltip("大型の雑魚(HP 倍率が largeHpMultiplier 以上)の攻撃・接触")]
    public Vector2[] largeHearts = { new Vector2(0, 1), new Vector2(22, 1), new Vector2(40, 2), new Vector2(60, 2), new Vector2(90, 3), new Vector2(97, 3.3f) };
    [Tooltip("ボスの通常の攻撃(元のダメージがハート1つ)。2026-11-03 の比較: 60km 台から2にすると、不注意なプレイで 63〜65km のボス1体に2回倒された → 70km から2")]
    public Vector2[] bossHearts = { new Vector2(0, 1), new Vector2(50, 1), new Vector2(80, 2), new Vector2(92, 2.5f), new Vector2(99, 2.8f) };
    [Tooltip("ボスの大技(元のダメージがハート2つ以上)")]
    public Vector2[] bossHeavyHearts = { new Vector2(0, 2), new Vector2(60, 2), new Vector2(90, 3) };
    [Tooltip("障害物/地形の針/壁(今までどおり)")]
    public Vector2[] obstacleHearts = { new Vector2(0, 1) };
    [Header("高速時の事故ダメージ(2026-11-06、Balance/AccidentDamage.cs)")]
    [Tooltip("走行速度で事故(接触/障害物/壁/落下)のダメージを決める")] public bool accidentSpeedDamage = true;
    [Tooltip("1段階目の速度(km/h): これ以上で accidentHearts[1]")] public float accidentKmh1 = 100f;
    [Tooltip("2段階目の速度(km/h): これ以上で accidentHearts[2]")] public float accidentKmh2 = 130f;
    [Tooltip("段階ごとのハート数: [0]=1段階目未満 [1]=1段階目以上 [2]=2段階目以上")] public int[] accidentHearts = { 1, 2, 3 };
    [Tooltip("対象: 雑魚への体当たり/接触")] public bool accidentContact = true;
    [Tooltip("対象: 障害物")] public bool accidentObstacle = true;
    [Tooltip("対象: 地形の壁")] public bool accidentWall = true;
    [Tooltip("対象: 穴/落下")] public bool accidentFall = true;
    [Tooltip("この距離(km)までは速度の段階を掛けず事故は1ハート(序盤の最大HPが少ない間。2026-11-06 マスター決定 15km。0 = 最初から。開発版 -accidentFromKm N で上書き)")] public float accidentFromKm = 15f;
    public float largeHpMultiplier = 1.75f;
    [Header("ボス撃破の回復(2026-11-06 初期デッキから HP/SHIELD/VAMPIRE を外した後の検討。既定 0 = なし)")]
    [Tooltip("通常マップでボス(関門のボス)を倒した時に回復するハートの数。0 = しない")] public int bossDefeatHealHearts = 0;
    [Tooltip("この距離(km)まで")] public float bossDefeatHealUntilKm = 40f;
    public static int BossHealOverride = -1; // 開発版 -bossHeal N
    public static int BossDefeatHealHearts(float distance)
    {
        int h = BossHealOverride >= 0 ? BossHealOverride : I.bossDefeatHealHearts;
        return h > 0 && distance < I.bossDefeatHealUntilKm * 1000f ? h : 0;
    }

    [Header("雑魚の HP(今の距離の HP に掛ける倍率。x = km)")]
    public Vector2[] smallHpScale = { new Vector2(0, 1f), new Vector2(20, 1f), new Vector2(40, 0.8f), new Vector2(65, 0.65f), new Vector2(90, 0.6f) };
    public Vector2[] largeHpScale = { new Vector2(0, 1f), new Vector2(20, 1f), new Vector2(60, 0.85f), new Vector2(90, 0.8f) };

    // 2026-11-04(マスター「序盤から走りながら一撃で斬り抜ける爽快感」): 普通の小型雑魚の HP に上限を置く。
    //  基準 = カードなし・初期状態の黒剣士の前攻撃1段目の威力(oneShotRefDamage)。上限 = 基準 × smallHpCapHits(x = km)。
    //  0〜20km は 0.95(黒剣士なら基本1発)/ 20〜40km は 1〜2発 / 40km からは上の smallHpScale(APK 429 の値)へつながる(約 50km で上限が効かなくなる)。
    //  キャラの個性は残す(攻撃の弱い/手数の多いキャラは 1〜2発・連撃の序盤)。大型・強敵(HP 倍率 > smallCapMaxHpMul)/ボスは対象外。
    //  上限はカードの成長に関係なく決まった値 → カードで攻撃が上がると、より遠く/より多くの敵が一撃圏内に入る
    [Header("ボスの HP(2026-11-05 デッキ 3 段階の調整)")]
    [Tooltip("ボスの HP に掛ける倍率(x = km)。初期デッキでは序盤のボス戦が長く(平均 28 秒)、40km の手前で倒れた → 序盤だけ短く。後半は今のまま")]
    public Vector2[] bossHpScale = { new Vector2(0, 0.75f), new Vector2(25, 0.75f), new Vector2(45, 0.9f), new Vector2(55, 1f) };
    public static float BossHpScale(float distance) => Active && !HeartsV1 ? Mathf.Max(0.2f, Eval(I.bossHpScale, distance / 1000f)) : 1f;

    [Header("吸収(VAMPIRE 等、撃破で回復)の間隔(2026-11-05 デッキ 3 段階の調整)")]
    [Tooltip("回復した後、次に回復できるまでの秒数(x = km)。撃破ごとに最大 75% でハート 1 つ回復するため、敵の多い終盤はオート放置でも回復が被弾を上回り、"
           + "中程度/強いデッキが 100km まで安定していた。間隔を空けて、回復の価値は残しつつ放置で無限にはしない")]
    public Vector2[] lifestealCooldown = { new Vector2(0, 0.5f), new Vector2(40, 1f), new Vector2(60, 2f), new Vector2(75, 3.5f), new Vector2(85, 5f), new Vector2(95, 6f) }; // 2026-11-06(T9): 終盤をもう一段長く
    // 2026-11-06(T9): 中/強デッキの放置が 100km まで安定 = SHIELD(最大3・最短8秒で回復)が終盤の被弾を200回以上消していた。
    //  終盤ほど SHIELD の回復を遅くする(倍率)。序盤の初期デッキには関係しない(SHIELD を育てた人の終盤だけ)。-shieldMul 0 で無効
    public Vector2[] shieldRechargeMul = { new Vector2(0, 1f), new Vector2(50, 1f), new Vector2(70, 1.6f), new Vector2(85, 2.2f), new Vector2(95, 2.5f) };
    public static float ShieldRechargeMul(float distance) => Active && !HeartsV1 && !ShieldMulOff ? Mathf.Max(1f, Eval(I.shieldRechargeMul, distance / 1000f)) : 1f;
    public static bool ShieldMulOff;
    // 2026-11-06(T10): 回復構成の放置が 100km 安定 = 命中の吸収(与えたダメージの割合)が終盤の大きな与ダメージで膨らみ、40km で 360 ハート回復していた
    //  → 終盤ほど命中の吸収の割合を下げる(通常マップ)。-shieldMul 0 で SHIELD と一緒に無効
    public Vector2[] hitLifestealMul = { new Vector2(0, 1f), new Vector2(50, 1f), new Vector2(70, 0.85f), new Vector2(85, 0.7f), new Vector2(95, 0.6f) };
    // 2026-11-06(T11): 回復の種類ごとに削っても中/強デッキの放置が 100km 安定 → 回復(カード/吸収/撃破の回復)全体の効きを終盤ほど下げる。
    //  最大 HP が増えた分の回復(HEART UP 等)と、ハート1つ未満への端数は持ち越し(GameManager.HealScaled)
    public Vector2[] lateHealMul = { new Vector2(0, 1f), new Vector2(50, 1f), new Vector2(70, 0.8f), new Vector2(85, 0.6f), new Vector2(95, 0.5f) };
    public static float LateHealMul(float distance) => Active && !HeartsV1 && !ShieldMulOff ? Mathf.Clamp01(Eval(I.lateHealMul, distance / 1000f)) : 1f;
    public static float HitLifestealMul(float distance) => Active && !HeartsV1 && !ShieldMulOff ? Mathf.Clamp01(Eval(I.hitLifestealMul, distance / 1000f)) : 1f;
    public static float LifestealCooldown(float distance) => Active && !HeartsV1 ? Mathf.Max(0f, Eval(I.lifestealCooldown, distance / 1000f)) : 0f;

    [Header("序盤の小型雑魚の HP の上限(2026-11-04)")]
    public bool smallHpCap = true;
    [Tooltip("基準: カードなしの黒剣士の前攻撃1段目の威力(HP)")]
    public int oneShotRefDamage = 20;
    [Tooltip("この HP 倍率以下の雑魚だけ(普通の小型。大型・強敵は今までどおり)")]
    public float smallCapMaxHpMul = 1.25f;
    [Tooltip("中型(HP 倍率が smallCapMaxHpMul より大きく大型未満)の上限は小型の何倍か(3 = 黒剣士で約3発)。大型(largeHpMultiplier 以上)は上限なし")]
    public float mediumCapMul = 3f;
    [Tooltip("上限 = 基準 × この値(x = km)。0.95 = 基準の1発で倒れる")]
    public Vector2[] smallHpCapHits = { new Vector2(0, 0.95f), new Vector2(20, 0.95f), new Vector2(30, 1.9f), new Vector2(40, 4.5f), new Vector2(50, 12f) };
    public static bool SmallCapOff; // 開発版 -hpCapOff 1(比較用)

    // 小型雑魚の HP の上限(DistanceTierManager.EnemyHpFor: 種族の倍率と距離の倍率を掛けた後、「敵が硬くなる」カードの倍率の前)
    public static int CapSmallHp(int hp, float hpMultiplier, float distance)
    {
        var t = I;
        if (SmallCapOff || !t.smallHpCap || LegacyOverride || t.legacy) return hp;
        var gm = GameManager.Instance;
        string st = gm != null ? gm.ActiveRunStageId : "";
        if (!(st == "wasteland_road" || st == "natural_cave" || st == "sky_corridor") || ArenaMode.Active) return hp;
        if (hpMultiplier < 0.7f || hpMultiplier >= t.largeHpMultiplier) return hp; // BONUS の宝の敵/大型はそのまま
        float hits = Eval(t.smallHpCapHits, distance / 1000f);
        if (hpMultiplier > t.smallCapMaxHpMul) hits *= Mathf.Max(1f, t.mediumCapMul); // 中型(兵隊アリ/ガーゴイル等): 小型の数倍は耐える(差を残す)
        int cap = Mathf.Max(1, Mathf.FloorToInt(t.oneShotRefDamage * hits));
        return Mathf.Min(hp, cap);
    }

    // 2026-11-04(道中の変化): 距離とともに増える感じを強めた(20〜40km 少し / 40〜60km 混成 / 60〜80km 明確に / 80〜90km 終盤の前触れ)。
    //  APK 429 の値は下の V1(開発版 -densityV1 1 で比較)。HP は上げない(敵は倒しやすく、数が多く、被弾は痛い)
    //  序盤の小型雑魚が一撃で倒れる(下の HP の上限)ので画面から早く消える分、20km からの間隔を少し多めに詰めた(比較: 0〜20km の画面の平均が 0.88→0.69 体、20〜40km が 1.03→0.82 体に減っていた → 0km から少し詰めて「0〜20km は今くらい / 20〜40km は少し多い」に戻す)
    [Header("敵の密度(Encounter の間隔と休憩の長さに掛ける。小さいほど多い)")]
    public Vector2[] gapScale = { new Vector2(0, 0.82f), new Vector2(20, 0.82f), new Vector2(25, 0.68f), new Vector2(30, 0.64f), new Vector2(40, 0.6f), new Vector2(50, 0.57f), new Vector2(60, 0.55f), new Vector2(70, 0.51f), new Vector2(80, 0.46f), new Vector2(90, 0.43f) };
    [Tooltip("休憩(Rest)を選ぶ重みに掛ける")]
    public Vector2[] restWeightScale = { new Vector2(0, 0.85f), new Vector2(20, 0.85f), new Vector2(25, 0.75f), new Vector2(40, 0.6f), new Vector2(60, 0.45f), new Vector2(70, 0.35f), new Vector2(80, 0.25f), new Vector2(90, 0.2f) };
    [Tooltip("激しい(Hard)Encounter の重みに掛ける")]
    public Vector2[] hardWeightScale = { new Vector2(0, 1f), new Vector2(20, 1f), new Vector2(40, 1.25f), new Vector2(60, 1.6f), new Vector2(80, 2f), new Vector2(90, 2.3f) };
    [Tooltip("地上 + 空の混成の Formation の重みに掛ける(40〜60km「混成が増える」)")]
    public Vector2[] mixedBias = { new Vector2(0, 1f), new Vector2(35, 1f), new Vector2(45, 1.6f), new Vector2(60, 2f), new Vector2(90, 2.2f) };
    static readonly Vector2[] GapV1 = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(45, 0.85f), new Vector2(60, 0.75f), new Vector2(80, 0.66f), new Vector2(90, 0.58f) };
    static readonly Vector2[] RestV1 = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(60, 0.7f), new Vector2(80, 0.5f), new Vector2(90, 0.4f) };
    static readonly Vector2[] HardV1 = { new Vector2(0, 1f), new Vector2(30, 1f), new Vector2(60, 1.4f), new Vector2(90, 1.8f) };
    public static bool DensityV1; // 開発版 -densityV1 1: APK 429 の密度(比較用)

    [Header("同時に攻撃できる雑魚の数(ZakoAttackLimiter)をこの距離から効かせる")]
    [Tooltip("2026-11-04: 敵が増えるので 50 → 30km から(見た目の数と実際の攻撃の圧を分ける)")]
    public float attackLimiterFromKm = 30f;
    [Tooltip("密度(間隔/休憩/激しさ)と同時攻撃の上限を変えるのはここまで。この先(90km ボス/大群/3連戦/死神)は今までどおり")]
    public float densityUntilKm = 90f;

    [Header("ボスの関門")]
    [Tooltip("ここまでは 1km ごと")]
    public float everyKmUntil = 20f;
    [Tooltip("ここまでは 5km ごと(10km は大ボス)。この先は今までどおり(90km ボス・3連戦)")]
    public float everyFiveKmUntil = 90f;
    [Tooltip("飛ばした 1km の地点で BONUS ZONE の抽選をする(ボスの後の抽選と同じ確率)")]
    public bool bonusRollAtSkippedGates = true;
    [Tooltip("2026-11-04: 飛ばした 1km の地点の抽選の確率(ボスの後の確率 0.5 と同じだと 20〜90km で約 2km ごとに BONUS になり、道中イベントが押し出されていた)")]
    [Range(0f, 1f)] public float bonusSkippedGateChance = 0.25f;

    [Header("空の雑魚(2026-11-04、実機「道中の空中敵を見かけない」)")]
    [Tooltip("空の敵が居ない戦いがこの回数続いたら、次は空の枠がある組を選びやすくする(0 = しない)")]
    public int flyerDueAfterEncounters = 2;
    [Tooltip("その時に空の枠がある組の重みに掛ける倍率")]
    public float flyerDueAirMul = 6f;
    [Tooltip("この距離(m)から")]
    public float flyerDueFromMeters = 500f;
    [Tooltip("空の敵の高さの上限(画面の下からの割合。1 = 画面の上端)。上の HUD に隠れる高さへ行かない")]
    [Range(0.5f, 1f)] public float flyerMaxViewportY = 0.8f;
    [Tooltip("空の敵が画面を横切るのに最低これだけの秒数がかかるよう、プレイヤーの前にいる間だけ前へ流す(画面が狭い縦画面/高速の時だけ効く。0 = しない)")]
    public float flyerMinVisibleSeconds = 2.4f;
    [Tooltip("前へ流す速さの上限(プレイヤーの速さに対する割合)")]
    [Range(0f, 0.8f)] public float flyerMaxCarry = 0.5f;

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
                i = System.Array.IndexOf(a, "-hpCapOff");
                if (i >= 0 && i + 1 < a.Length) SmallCapOff = a[i + 1] == "1";
                i = System.Array.IndexOf(a, "-densityV1");
                if (i >= 0 && i + 1 < a.Length) DensityV1 = a[i + 1] == "1";
                i = System.Array.IndexOf(a, "-shieldMul");
                if (i >= 0 && i + 1 < a.Length && a[i + 1] == "0") ShieldMulOff = true;
                i = System.Array.IndexOf(a, "-heartsV1");
                if (i >= 0 && i + 1 < a.Length && a[i + 1] == "1")
                {
                    HeartsV1 = true;
                    inst.largeHearts = new[] { new Vector2(0, 1), new Vector2(29.9f, 1), new Vector2(30, 2), new Vector2(60, 2), new Vector2(90, 3) };
                    inst.bossHearts = new[] { new Vector2(0, 1), new Vector2(69.9f, 1), new Vector2(70, 2) };
                }
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
    // 2026-11-05 判断型の道中イベントの高火力敵: そのフレームの被弾にだけ足すハート
    static int nextExtra; static int nextExtraFrame = -1;
    public static int NextAttackerExtraHearts { get => nextExtraFrame == Time.frameCount ? nextExtra : 0; set { nextExtra = value; nextExtraFrame = Time.frameCount; } }
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
        float hv = Eval(curve, km);
        if (kind == Hit.Boss || kind == Hit.BossHeavy) hv *= BossManager.SetRushHeartMul; // 終盤 95〜98km の組のボス(マップ別)
        int hearts = Mathf.Max(1, t.stochasticHearts && !HeartsV1 ? StochasticRound(hv) : Mathf.RoundToInt(hv));
        int extra = NextAttackerExtraHearts; nextExtraFrame = -1;
        if (extra > 0 && (kind == Hit.Contact || kind == Hit.Normal || kind == Hit.Large)) hearts += extra; // 高火力敵(2026-11-05)
        int result = hearts * CombatScale.HpPerHeart;
        Adjusted++;
        LastAdjust = $"{reason} {kind}{(large ? "(large)" : "")} @{km:F1}km: {amount} -> {result}";
        return result;
    }

    // 1.4 → 60% で 1、40% で 2(期待値はそのまま、段差を作らない)
    static int StochasticRound(float v) { int f = Mathf.FloorToInt(v); return f + (Random.value < v - f ? 1 : 0); }
    public static bool HeartsV1; // 開発版 -heartsV1 1 = 前の作り(四捨五入・前の曲線)。比較用

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
    public static float GapScale(float distance) => DensityOn(distance) ? Mathf.Max(0.2f, Eval(DensityV1 ? GapV1 : I.gapScale, distance / 1000f)) : 1f;
    public static float RestWeightScale(float distance) => DensityOn(distance) ? Mathf.Max(0f, Eval(DensityV1 ? RestV1 : I.restWeightScale, distance / 1000f)) : 1f;
    public static float HardWeightScale(float distance) => DensityOn(distance) ? Mathf.Max(0f, Eval(DensityV1 ? HardV1 : I.hardWeightScale, distance / 1000f)) : 1f;
    public static float MixedBias(float distance) => DensityOn(distance) && !DensityV1 ? Mathf.Max(0f, Eval(I.mixedBias, distance / 1000f)) : 1f;
    public static bool LimiterOn(float distance) => DensityOn(distance) && distance >= (DensityV1 ? 50f : I.attackLimiterFromKm) * 1000f;
}

using UnityEngine;

// 共通カードの追加(2026-11-06、マスターの依頼「シンプルな単一条件カード10枚・リスク付き複合・ノーリスク複合」)。
// 条件つきの MILE/EXP の報酬と、リスクのカードの値の式はここだけに置く(表示は CardBehaviorText、資料の書き出しも同じ式を読む)。
//  ・Lv はこのランのカードの Lv の合計(GameManager.Card。v3 と同じく取得のたびに Lv から計算し直す)。値は Lv に比例(1Lvごと)
//  ・MILE の固定の報酬は BONUS と同じ経路(分配の人数で割る。対戦は割らない)。MILE の倍率のカード(EXECUTIONER 等)は掛けない
//  ・EXP の固定の報酬は距離/撃破の EXP と同じ「EXP の量」。EXP の倍率のカード(EXP UP 等)は掛けない
//  ・CONTINUE/疾走出発/ワープなどで距離が一度に飛んだ時は、飛んだ区間の分は払わない(その位置から数え直す)
//  ・数え途中の端数(あと何体/何秒)は中断データに入れない(CONTINUE ではその位置から数え直す)
public static class CardRewards
{
    // ---------------------------------------------------------------- 値(1Lvあたり)
    public const int ComboStep = 10;               // 連続ヒット10回ごと
    public const int ComboMaxSteps = 10;           // 1回の連続ヒットで払うのは100HITまで(ボスの長いコンボで増えすぎない)
    public const int HuntStep = 10;                // 雑魚10体ごと
    public const float RoadStepMeters = 1000f;     // 1kmごと
    public const float SurvivalStepSeconds = 30f;  // 30秒ごと(走っている間だけ数える)
    public const int ChainMinHits = 10;            // CHAIN BOUNTY: 10HIT 以上の連続ヒット中の撃破
    public const int TrophyMaxKills = 100;         // TROPHY HUNT: 数える雑魚は前のボスから100体まで
    public const float HorizonStepMeters = 10000f; // HORIZON: 10kmごと
    public const int HorizonMaxSteps = 10;         // 100kmで最大

    public static float ComboPayMile(float lv) => 1f * lv;
    public static float HuntPayMile(float lv) => 1f * lv;
    public static float RoadPayMile(float lv) => 2f * lv;
    public static float SurvivalPayMile(float lv) => 1f * lv;
    public static float ComboTrainingExp(float lv) => 20f * lv;
    public static float HuntTrainingExp(float lv) => 30f * lv;
    public static float RoadTrainingExp(float lv) => 100f * lv;
    public static float SurvivalTrainingExp(float lv) => 30f * lv;

    // リスク(★3)
    public static float DangerHeavyChance(float lv) => Mathf.Min(0.30f, 0.03f * lv);              // 雑魚が高火力敵になる確率(足す)
    public static float SkySiegeAirChance(float lv) => 0.05f * lv;                                 // 空中の枠に空中雑魚が出る確率(足す。上限 95%)
    public static float SkySiegeFormationMul(float lv) => 1f + 0.10f * lv;                         // 空の枠がある組の選ばれやすさ
    public static float CrossroadsFromKm(float lv) => Mathf.Max(10f, 50f - 4f * lv);                // 判断イベントが始まる距離(通常 50km)
    public static float CrossroadsGapMul(float lv) => Mathf.Max(0.5f, 1f - 0.05f * lv);              // 判断イベントの候補の間隔
    public static float HighStakesPct(float lv) => 0.10f * lv;                                     // ボスから受けるダメージ
    public const float SkySiegeAirChanceCap = 0.95f;

    // 複合(★4)
    public static float ChainBountyPct(float lv) => 0.15f * lv;    // MILE と撃破の EXP
    public static float IronMarathonMile(float lv) => 3f * lv;
    public static float IronMarathonExp(float lv) => 150f * lv;
    public static float TrophyMilePerKill(float lv) => 0.2f * lv;
    public static float TrophyExpPerKill(float lv) => 3f * lv;
    public static float HorizonPerStep(float lv) => 0.02f * lv;    // 10kmごとに MILE と EXP の割合

    // ---------------------------------------------------------------- このランの Lv
    static float L(GameManager gm, EffectType t) => gm != null ? Mathf.Max(0f, gm.Card.Get(t)) : 0f;
    static float L(EffectType t) => L(GameManager.Instance, t);
    public static bool Has(EffectType t) => L(t) > 0f;

    // ---------------------------------------------------------------- 倍率(報酬の経路から読む)
    public static int HorizonSteps(GameManager gm) => gm == null ? 0 : Mathf.Clamp(Mathf.FloorToInt(gm.MaxDistance / HorizonStepMeters), 0, HorizonMaxSteps);
    public static float HorizonBonus(GameManager gm) { float lv = L(gm, EffectType.HorizonLevel); return lv > 0f ? HorizonPerStep(lv) * HorizonSteps(gm) : 0f; }
    public static float HorizonMileMul(GameManager gm) => 1f + HorizonBonus(gm);
    // 10HIT 以上の連続ヒット中の撃破(CHAIN BOUNTY)。MILE と撃破の EXP の枠に足す割合
    public static float ChainBonusNow(GameManager gm)
    {
        float lv = L(gm, EffectType.ChainBountyLevel);
        if (lv <= 0f || ComboCounterUI.Instance == null || ComboCounterUI.Instance.ComboCount < ChainMinHits) return 0f;
        return ChainBountyPct(lv);
    }
    // ボスから受けるダメージ(HIGH STAKES)。ハート単位で、端数は確率で切り上げ(期待値どおり)
    public static int ApplyBossRisk(int dmg, string reason)
    {
        float lv = L(EffectType.HighStakesLevel);
        if (lv <= 0f || dmg <= 0 || !IsBossHit(reason)) return dmg;
        int per = CombatScale.HpPerHeart;
        float hearts = dmg / (float)per * (1f + HighStakesPct(lv));
        int h = Mathf.FloorToInt(hearts);
        if (Random.value < hearts - h) h++;
        int result = Mathf.Max(dmg, h * per);
        if (result > dmg) { HighStakesExtraHp += result - dmg; HighStakesHits++; }
        return result;
    }
    // DifficultyTuning.Classify と同じ見分け方(ボス/ボスの技。弾とマルチの写しはボス戦中だけ)
    public static bool IsBossHit(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return false;
        string r = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        if (r.StartsWith("DeathY") || r.StartsWith("Reaper")) return false;
        if (r.StartsWith("Boss") || r.StartsWith("WildBoss") || r.StartsWith("CaveBoss") || r.StartsWith("Dragon") || r.StartsWith("SkyStrike") || r.StartsWith("Majin") || r.StartsWith("Mech")) return true;
        if (r.StartsWith("Fireball:") || r.StartsWith("Net")) return BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        return false;
    }
    // 高火力敵(DANGER BOUNTY)・空中雑魚(SKY SIEGE)・判断イベント(CROSSROADS)。読むのは DecisionEvents / EncounterDirector
    public static float HeavyChanceBonus => L(EffectType.DangerBountyLevel) > 0f ? DangerHeavyChance(L(EffectType.DangerBountyLevel)) : 0f;
    public static float AirChanceBonus => L(EffectType.SkySiegeLevel) > 0f ? SkySiegeAirChance(L(EffectType.SkySiegeLevel)) : 0f;
    public static float AirFormationMul => L(EffectType.SkySiegeLevel) > 0f ? SkySiegeFormationMul(L(EffectType.SkySiegeLevel)) : 1f;
    public static float DecisionFromKm(float normalFromKm) { float lv = L(EffectType.CrossroadsLevel); return lv > 0f ? Mathf.Min(normalFromKm, CrossroadsFromKm(lv)) : normalFromKm; }
    public static float DecisionGapMul { get { float lv = L(EffectType.CrossroadsLevel); return lv > 0f ? CrossroadsGapMul(lv) : 1f; } }

    // ---------------------------------------------------------------- このランの数え
    static GameManager boundGm;
    static int comboStepsPaid, lastCombo;
    static int huntKills, trophyKills;
    static int roadKm, ironKm;
    static float ironFrom;      // 被弾せずに走り始めた距離(IRON MARATHON)
    static float survivalTimer;
    static float lastDistance;
    static bool started;

    // 確認用(QA の -qaNewCards)
    public static float MilePaid, ExpPaid;
    public static int ComboPays, HuntPays, RoadPays, SurvivalPays, IronPays, TrophyPays, DistanceJumps, HighStakesHits, HighStakesExtraHp;
    public static void ResetCounters() { MilePaid = ExpPaid = 0f; ComboPays = HuntPays = RoadPays = SurvivalPays = IronPays = TrophyPays = DistanceJumps = HighStakesHits = HighStakesExtraHp = 0; }
    public static int TrophyKillsNow => trophyKills;
    public static float SurvivalTimerNow => survivalTimer;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void QaResetRunState() => ResetRun(GameManager.Instance); // 確認用: 数えをこの位置から
#endif

    static void ResetRun(GameManager gm)
    {
        comboStepsPaid = 0; lastCombo = 0; huntKills = 0; trophyKills = 0; survivalTimer = 0f;
        float d = gm != null ? gm.MaxDistance : 0f;
        Resync(d);
    }
    // 距離の数えをこの位置から(払わずに)
    static void Resync(float d)
    {
        roadKm = Mathf.FloorToInt(d / RoadStepMeters);
        ironKm = 0; ironFrom = d;
        lastDistance = d;
    }

    static void Pay(GameManager gm, float mile, float exp)
    {
        if (gm == null) return;
        if (mile > 0f) MilePaid += gm.AddCardRewardMile(mile);
        if (exp > 0f) { gm.AddCardRewardExp(exp); ExpPaid += exp; }
    }

    // ComboCounterUI.RegisterHit から(命中の直後の HIT 数)
    public static void OnHit(int comboCount)
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (comboCount < lastCombo || comboCount <= 1) comboStepsPaid = 0; // 新しい連続ヒット
        lastCombo = comboCount;
        if (comboCount % ComboStep != 0) return;
        int step = comboCount / ComboStep;
        if (step > ComboMaxSteps || step <= comboStepsPaid) return;
        comboStepsPaid = step;
        float a = L(gm, EffectType.ComboPayLevel), b = L(gm, EffectType.ComboTrainingLevel);
        if (a <= 0f && b <= 0f) return;
        Pay(gm, a > 0f ? ComboPayMile(a) : 0f, b > 0f ? ComboTrainingExp(b) : 0f);
        ComboPays++;
    }

    // GameManager.RegisterEnemyKill から(撃破した本人だけ。協力の分け前は数えない)
    public static void OnEnemyKilled(GameManager gm)
    {
        if (gm == null) return;
        huntKills++;
        if (trophyKills < TrophyMaxKills) trophyKills++;
        if (huntKills < HuntStep) return;
        huntKills = 0;
        float a = L(gm, EffectType.HuntPayLevel), b = L(gm, EffectType.HuntTrainingLevel);
        if (a <= 0f && b <= 0f) return;
        Pay(gm, a > 0f ? HuntPayMile(a) : 0f, b > 0f ? HuntTrainingExp(b) : 0f);
        HuntPays++;
    }

    // GameManager.RegisterBossDefeat から(撃破した本人だけ)。TROPHY HUNT: 前のボスから倒した雑魚の数
    public static void OnBossKilled(GameManager gm)
    {
        if (gm == null) return;
        int k = trophyKills;
        trophyKills = 0;
        float lv = L(gm, EffectType.TrophyHuntLevel);
        if (lv <= 0f || k <= 0) return;
        Pay(gm, TrophyMilePerKill(lv) * k, TrophyExpPerKill(lv) * k);
        TrophyPays++;
    }

    // 被弾(GameManager.NoteHudDamage)。IRON MARATHON はここから数え直す
    public static void OnPlayerHit()
    {
        var gm = GameManager.Instance;
        float d = gm != null ? gm.MaxDistance : 0f;
        ironKm = 0; ironFrom = d;
    }

    // 毎フレーム(CardRewardsDriver)
    internal static void Tick(float dt)
    {
        var gm = GameManager.Instance;
        if (gm != boundGm) { boundGm = gm; started = false; }
        if (gm == null || !gm.HasStarted) { started = false; return; }
        if (!started) { started = true; ResetRun(gm); }
        if (gm.IsGameOver) return;
        float d = gm.MaxDistance;
        // 距離が一度に大きく飛んだ(疾走出発の到着/CONTINUE/ワープ): 払わずに数え直す。最高速でも1フレームは数mなので 150m を目安にする
        if (d - lastDistance > 150f || d < lastDistance - 1f) { DistanceJumps++; Resync(d); }
        lastDistance = d;

        // 1kmごと
        int km = Mathf.FloorToInt(d / RoadStepMeters);
        if (km > roadKm)
        {
            int n = km - roadKm; roadKm = km;
            float a = L(gm, EffectType.RoadPayLevel), b = L(gm, EffectType.RoadTrainingLevel);
            if (a > 0f || b > 0f) { Pay(gm, a > 0f ? RoadPayMile(a) * n : 0f, b > 0f ? RoadTrainingExp(b) * n : 0f); RoadPays += n; }
        }
        // 被弾せずに 1kmごと
        int ik = Mathf.FloorToInt((d - ironFrom) / RoadStepMeters);
        if (ik > ironKm)
        {
            int n = ik - ironKm; ironKm = ik;
            float lv = L(gm, EffectType.IronMarathonLevel);
            if (lv > 0f) { Pay(gm, IronMarathonMile(lv) * n, IronMarathonExp(lv) * n); IronPays += n; }
        }
        // 30秒ごと(走っている間だけ。カード選択/カウントダウン/一時停止の間は数えない)
        if (dt > 0f && Time.timeScale > 0f && !gm.IsRewardSequenceRunning && !gm.CountdownActive && !gm.ResumeGateActive)
        {
            survivalTimer += dt;
            if (survivalTimer >= SurvivalStepSeconds)
            {
                survivalTimer -= SurvivalStepSeconds;
                float a = L(gm, EffectType.SurvivalPayLevel), b = L(gm, EffectType.SurvivalTrainingLevel);
                if (a > 0f || b > 0f) { Pay(gm, a > 0f ? SurvivalPayMile(a) : 0f, b > 0f ? SurvivalTrainingExp(b) : 0f); SurvivalPays++; }
            }
        }
    }
}

// CardRewards の毎フレームの数え(シーンをまたいで1つ)
public class CardRewardsDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<CardRewardsDriver>() != null) return;
        var go = new GameObject("[CardRewards]");
        DontDestroyOnLoad(go);
        go.AddComponent<CardRewardsDriver>();
    }

    void Update() => CardRewards.Tick(Time.deltaTime);
}

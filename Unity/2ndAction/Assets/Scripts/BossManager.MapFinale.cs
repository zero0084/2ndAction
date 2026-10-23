using System.Collections.Generic;
using UnityEngine;

// 通常マップの終盤(2026-10-24、マスターの依頼「90km 以降で最後のビルドを完成させ、その力で 100km の死神から逃げる」)。
// 荒野街道 / 自然洞窟 / 天空回廊(ソロ・CPU サポート・協力マルチの HOST)。ラスダン(last_corridor)は今までどおり(変えない)。
//  ・90,000m … そのマップ本来の 90km のボス(通常の関門。特別な複数戦にしない)。
//  ・90〜95km … 雑魚の大群(ZakoRushAt。関門は出さない)。EncounterDirector が 1km ごとの段階(波)で密度を上げる。
//               同時に攻撃できる雑魚の数は ZakoAttackLimiter で上限を持つ。95km に入ったら雑魚は出さない。
//  ・95〜99km … 1,000m ごとの関門で、ボス3体を1体ずつ順番に(同時には出さない)。距離は関門で止まる(通常の関門と同じ)。
//               1体倒す → FINISH → 少しの間(TrioGapSeconds + 予告)→ 次。その間に溜まったレベルアップの選択はここで出す。
//               3体目を倒すと関門の突破(ボス報酬の3択は関門ごとに1回)→ 距離の再開 → 次の関門。99km の3体目の後は 100km の死神(今の仕様)。
//  ・3体の格: 本来の出現距離で3つに分けた格(C1 = 序盤の大ボス+1km/5km のボス / C2 = 中盤 / C3 = 終盤)を、関門ごとに弱→強へ並べる。
//               強さ = その関門の距離の再戦の強さ(BossRematchTuning)× 格の倍率(TrioHpMul)。
//  ・抽選: そのマップのボスだけ。このランで倒した/今までに会ったボスを優先し、直近に戦ったボスと同じ関門の中の重複を外す(HOST/シングル)。
//  ・以前の「90〜98km を走りながら9体」(BossManager.MapRush.cs)は MapFinaleEnabled = false で戻せる(比較用 -mapFinale 0)。
public partial class BossManager
{
    public static bool MapFinaleEnabled = true;
    public const int FinaleBossK = 90, FinaleTrioFirstK = 95, FinaleTrioLastK = 99;
    public const float ZakoRushFrom = 90000f, ZakoRushTo = 95000f;
    public static float ZakoRushHpMul = 0.35f; // 大群の雑魚の HP(ふだんの雑魚に対して。高速で1〜2回の攻撃で倒れるくらい)
    public static float TrioGapSeconds = 0.8f, TrioTelegraphSeconds = 0.9f;
    public static float[] TrioHpMul = { 0.45f, 0.6f, 0.75f };  // 格 C1/C2/C3(その関門の距離の再戦の強さに掛ける)
    public static int TrioRecentExclude = 3;
    // 関門ごとの3体の格(弱 → 強)
    static readonly int[][] TrioClasses =
    {
        new[] { 0, 0, 1 }, // 95km
        new[] { 0, 1, 2 }, // 96km
        new[] { 1, 1, 2 }, // 97km
        new[] { 1, 2, 2 }, // 98km
        new[] { 2, 2, 2 }, // 99km(そのマップの強いボス)
    };
    static readonly string[][] WastelandClasses =
    {
        new[] { "Wild/Wolf", "Wild/GoblinRider", "Wild/Serpent", "Wild/Cyclops" },
        new[] { "Wild/Spider", "Wild/Golem", "Wild/Griffin" },
        new[] { "Wild/Hydra", "Wild/Demon", "Wild/Dragon", "Wild/BlackKnight" },
    };
    static readonly string[][] CaveClasses =
    {
        new[] { "Cave/Centipede", "Cave/Scorpion", "Cave/Mole", "Cave/Troll" },
        new[] { "Cave/Worm", "Cave/CrystalGolem", "Cave/Bat" },
        new[] { "Cave/ScorpionKing", "Cave/Basilisk", "Cave/Drake", "Cave/AncientDemon" },
    };
    static readonly string[][] SkyClasses =
    {
        new[] { "Sky/Dragon", "Sky/Majin", "Sky/Behemoth", "Sky/Titan" },
        new[] { "Sky/Jellyfish", "Sky/Leviathan", "Sky/Fenrir" },
        new[] { "Sky/SkyGolem", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Guardian" },
    };
    public static IReadOnlyList<string> FinaleClassPool(string stageId, int cls)
    {
        var t = stageId == "natural_cave" ? CaveClasses : stageId == "sky_corridor" ? SkyClasses : WastelandClasses;
        return t[Mathf.Clamp(cls, 0, 2)];
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void FinaleDevArgs()
    {
        var a = QaArgs.All;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-mapFinale") MapFinaleEnabled = a[i + 1] != "0";
            if (a[i] == "-trioHp") { var p = a[i + 1].Split(','); for (int j = 0; j < p.Length && j < 3; j++) if (float.TryParse(p[j], System.Globalization.NumberStyles.Float, inv, out float v)) TrioHpMul[j] = v; }
        }
    }
#endif

    // 対象のマップ/人数(各マップのラッシュと同じ: ソロ・CPU・協力の HOST。対戦は今までどおりの関門)
    bool MapFinaleStage => MapFinaleEnabled && MapRushStage;
    // 91〜94km は関門を作らない(雑魚の大群の区間)
    bool FinaleSkipsGate(int k) => MapFinaleStage && k > FinaleBossK && k < FinaleTrioFirstK;
    bool FinaleTrioGate(int k) => MapFinaleStage && k >= FinaleTrioFirstK && k <= FinaleTrioLastK;

    // ---- 90〜95km: 雑魚の大群 ----
    // 90km のボスを倒した後(次の関門が 95km)から 95,000m の手前まで
    public bool ZakoRushAt(double runDistance) =>
        MapFinaleStage && !IsBossPhase && gateK >= FinaleTrioFirstK && runDistance >= ZakoRushFrom && runDistance < ZakoRushTo;
    public static bool ZakoRushNow
    {
        get
        {
            var bm = Instance; var gm = GameManager.Instance;
            return bm != null && gm != null && gm.HasStarted && !gm.IsGameOver && bm.ZakoRushAt(gm.MaxDistance);
        }
    }
    // 95,000m〜100,000m(3連戦の区間)は雑魚を出さない(関門と関門の間も)。100km の死神の後は今までどおり
    public bool FinaleNoZakoAt(double runDistance) => MapFinaleStage && runDistance >= ZakoRushTo && runDistance < deathSpawnDistance;
    public static bool FinaleQuietAt(float runDistance) => Instance != null && Instance.FinaleNoZakoAt(runDistance);
    // 上下ルートの分岐を作らない区間(90km のボスの少し手前〜95km の少し先)。地形の生成(TerrainManager)が呼ぶ
    public static bool FinaleNoBranchBetween(float fromRun, float toRun)
    {
        var bm = Instance;
        return bm != null && bm.MapFinaleStage && !NetRunLauncher.IsMultiplayerRun && toRun >= ZakoRushFrom - 300f && fromRun <= ZakoRushTo + 200f;
    }
    public const float FinaleBranchResumeRun = ZakoRushTo + 250f;
    // 段階(0〜4): 90-91 / 91-92 / 92-93 / 93-94 / 94-95km
    public static int ZakoRushWave(double runDistance) => Mathf.Clamp((int)((runDistance - ZakoRushFrom) / 1000.0), 0, 4);
    public int ZakoRushBanners { get; private set; }
    public static int RushEnemiesCleared; // 確認用: 走り過ぎて片付けた大群の雑魚
    // 一番後ろの人より behind(m)より前に居る雑魚の数(大群の出現の上限の数え方)
    public static int RushEnemiesAhead(float behind)
    {
        float back = WorldRange.WorldBackSceneX - behind;
        int n = 0;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e != null && e.isActiveAndEnabled && e.transform.position.x >= back) n++;
        return n;
    }
    float rushCleanAt;
    // 大群の間: 一番後ろの人より 45m 以上後ろへ置いていかれた雑魚を片付ける(戻ってくることはなく、数と処理の重さだけが増える)
    void ZakoRushCleanup()
    {
        if (Time.time < rushCleanAt || NetCombat.Replica) return;
        rushCleanAt = Time.time + 0.5f;
        float back = WorldRange.WorldBackSceneX - 45f;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e != null && !e.IsDying && e.transform.position.x < back && e.GetComponent<BonusEnemy>() == null) { Destroy(e.gameObject); RushEnemiesCleared++; }
    }
    int zakoRushBannerWave = -1;
    void ZakoRushTick()
    {
        var gm = GameManager.Instance;
        if (gm == null || !ZakoRushAt(gm.MaxDistance)) { if (gm != null && gm.MaxDistance < ZakoRushFrom) zakoRushBannerWave = -1; return; }
        ZakoRushCleanup();
        int w = ZakoRushWave(gm.MaxDistance);
        if (w == zakoRushBannerWave) return;
        bool first = zakoRushBannerWave < 0;
        zakoRushBannerWave = w;
        ZakoRushBanners++;
        Debug.Log($"[MapFinale] zako rush wave {w} at {gm.MaxDistance:F0}m (alive enemies {EnemyController.ActiveCount})");
        if (first) { BossBattleHud.Banner("FINAL RUSH  >> 95km", new Color(1f, 0.55f, 0.3f), 2.0f); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning); }
        else if (w == 4) BossBattleHud.Banner("FINAL RUSH  MAX", new Color(1f, 0.45f, 0.3f), 1.4f);
    }

    // ---- 95〜99km: ボス3体の関門 ----
    public class FinaleTrio
    {
        public int k, idx;
        public readonly List<string> plan = new List<string>();
        public readonly List<int> classes = new List<int>();
        public bool waiting, telegraph;
        public float readyAt, telegraphEnd;
        public int choiceHolds;
        public float holdFrom = -1f, closedAt = -99f; public bool wasChoosing;
        public readonly List<float> killSeconds = new List<float>();
        public float spawnedAt;
    }
    public FinaleTrio CurrentTrio { get; private set; }
    public bool TrioActive => CurrentTrio != null && IsBossPhase;
    public int TriosStarted { get; private set; }
    public int TrioBossesSpawned { get; private set; }
    public string LastTrioPlan { get; private set; } = "";
    public static readonly List<string> TrioLog = new List<string>(); // 確認用(関門ごとの構成と撃破までの秒)

    List<string> BuildTrioPlan(int k, out List<int> classes)
    {
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        classes = new List<int>(TrioClasses[Mathf.Clamp(k - FinaleTrioFirstK, 0, TrioClasses.Length - 1)]);
        var plan = new List<string>();
        foreach (int c in classes)
        {
            var all = new List<string>();
            foreach (var key in FinaleClassPool(stage, c)) if (ParseKey(key, out _, out _)) all.Add(key);
            // 会ったボス(このランで倒した / 今までに会った)を優先。どちらも無い時は全部から
            var seen = all.FindAll(x => defeatedPool.Contains(x) || ProgressStats.HasSeenBoss(x));
            var cands = seen.Count > 0 ? seen : all;
            string pick = null;
            // 直近に戦った TrioRecentExclude 体は外す(候補が無ければ緩める)。同じ関門の中では重ねない
            for (int ex = Mathf.Max(0, TrioRecentExclude); ex >= 0 && pick == null; ex--)
            {
                int exNow = ex;
                bool Ok(string x) { int ri = recentFought.IndexOf(x); return !plan.Contains(x) && (ri < 0 || ri >= exNow); }
                var ok = cands.FindAll(Ok);
                if (ok.Count == 0 && cands != all) ok = all.FindAll(Ok);
                if (ok.Count > 0) pick = ok[Random.Range(0, ok.Count)]; // HOST/シングルだけが呼ぶ
            }
            plan.Add(pick ?? all[Random.Range(0, all.Count)]);
        }
        return plan;
    }

    void StartTrioGate(int k)
    {
        var t = new FinaleTrio { k = k };
        t.plan.AddRange(BuildTrioPlan(k, out var cls));
        t.classes.AddRange(cls);
        CurrentTrio = t;
        TriosStarted++;
        MapRushesStarted++; // 協力マルチの完走報酬(全員に1回)の通し番号
        LastTrioPlan = $"{k}km: " + string.Join(" > ", t.plan.ConvertAll(x => $"{ShortName(x)}(C{t.classes[t.plan.IndexOf(x)] + 1})"));
        Debug.Log($"[MapFinale] trio gate {k * 1000}m start: {LastTrioPlan}");
        BossBattleHud.Banner($"BOSS RUSH  {k}km  ×3", new Color(1f, 0.45f, 0.3f), 1.8f);
        SpawnTrioBoss(t, 0);
    }

    // その関門の距離の再戦の強さ × 格の倍率
    void SetTrioStrength(string key, int k, int cls)
    {
        if (!ParseKey(key, out GateFamily f, out int kind)) return;
        var tn = BossRematchTuning.I;
        float d = k * gateIntervalMeters;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(f, kind)))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul) * Mathf.Max(0.05f, TrioHpMul[Mathf.Clamp(cls, 0, 2)]);
        CurrentRematchTier = currentTier.label;
        DamageMul = Mathf.Max(0.1f, currentTier.damageMul);
        CurrentEncounterIsRematch = true;
    }

    void SpawnTrioBoss(FinaleTrio t, int i)
    {
        t.idx = i; t.waiting = false; t.telegraph = false;
        string key = t.plan[i];
        int cls = t.classes[i];
        ParseKey(key, out GateFamily fam, out int kind);
        SetTrioStrength(key, t.k, cls);
        LastRematchDecision = $"{t.k}km: BOSS RUSH {i + 1}/3 {key}(C{cls + 1} HP×{CurrentRematchHpMul:0.0} 被弾×{DamageMul:0.0})";
        BeginEncounterKey(key, true);
        TrioBossesSpawned++;
        t.spawnedAt = Time.time;
        Debug.Log($"[MapFinale] trio {t.k}km boss {i + 1}/3 spawn {key} C{cls + 1} hp×{CurrentRematchHpMul:0.00} dmg×{DamageMul:0.00}");
        if (fam == GateFamily.Sky) StartSkyGate((SkyBossKind)kind, 1);
        else if (fam == GateFamily.Cave) StartCaveGate((CaveBossKind)kind, 1);
        else StartWildGate((WildBossKind)kind, 1);
        // 曲: 3体目(と 99km)は特別なボス曲、それ以外は強敵の曲
        BossMusicTier = i == 2 || t.k == FinaleTrioLastK ? BossBgmTier.Special : BossBgmTier.Strong;
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        BossMusicKey = $"{stage}/Trio{t.k}";
    }

    // CheckEncounterComplete から(遭遇のボスが全員倒れた時): まだ次のボスがいれば関門を終わらせずに次へ
    bool TrioContinues()
    {
        var t = CurrentTrio;
        if (t == null || !IsBossPhase || t.idx >= t.plan.Count - 1) return false;
        t.killSeconds.Add(Time.time - t.spawnedAt);
        RegisterEncounterDefeated(); // 再戦プールへ
        if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null) TerrainManager.Instance.cave.ClearBossClearZone();
        t.idx++;
        t.waiting = true; t.telegraph = false;
        t.readyAt = Time.time + Mathf.Max(0f, TrioGapSeconds);
        Debug.Log($"[MapFinale] trio {t.k}km: boss {t.idx}/3 down - next {t.plan[t.idx]} after a short gap (pending level-ups {(GameManager.Instance != null ? GameManager.Instance.PendingLevelUpCount : 0)})");
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("TrioNext");
        return true;
    }

    // 3体目を倒した(関門の突破): 記録と知らせ
    void TrioCleared()
    {
        var t = CurrentTrio;
        if (t == null) return;
        t.killSeconds.Add(Time.time - t.spawnedAt);
        string line = $"{t.k}km: " + string.Join(" > ", t.plan.ConvertAll(ShortName)) + " | kill s: " + string.Join("/", t.killSeconds.ConvertAll(x => x.ToString("0.0")));
        TrioLog.Add(line);
        Debug.Log($"[MapFinale] trio cleared {line}");
        BossBattleHud.Banner($"BOSS RUSH  {t.k}km  CLEAR!", new Color(1f, 0.85f, 0.4f), 1.8f);
    }

    // 毎フレーム(BossManager.Update): 次のボスまでの間 → 予告 → 出現
    void TrioTick()
    {
        var t = CurrentTrio;
        if (t == null) return;
        if (!IsBossPhase) { CurrentTrio = null; return; }
        if (!t.waiting || NetCombat.Replica) return;
        if (TrioChoiceHold(t)) return; // ボスとボスの間: 溜まったレベルアップの選択を先に
        if (Time.time < t.readyAt) return;
        if (!t.telegraph)
        {
            t.telegraph = true;
            t.telegraphEnd = Time.time + Mathf.Max(0f, TrioTelegraphSeconds);
            BossBattleHud.Banner($"NEXT  {ShortName(t.plan[t.idx])}  ({t.idx + 1}/3)", new Color(1f, 0.35f, 0.25f), Mathf.Max(0.8f, TrioTelegraphSeconds + 0.4f));
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
            PrewarmBossAssets(t.plan[t.idx]);
            return;
        }
        if (Time.time < t.telegraphEnd) return;
        SpawnTrioBoss(t, t.idx);
    }

    // 次のボスの前に、レベルアップの選択が残っている/開いている/閉じた直後は待つ(各マップのラッシュと同じ考え方)
    bool TrioChoiceHold(FinaleTrio t)
    {
        var gm = GameManager.Instance;
        if (gm == null || t.telegraph) return false;
        bool multi = NetRunLauncher.IsMultiplayerRun;
        var pc = PlayerController.Instance;
        bool hostDown = multi && pc != null && pc.NetIsDowned;
        bool local = !hostDown && (gm.PendingLevelUpCount > 0 || gm.LevelUpPending || gm.IsRewardSequenceRunning);
        if (t.wasChoosing && !local) t.closedAt = Time.unscaledTime;
        t.wasChoosing = local;
        bool remote = false;
        if (multi) foreach (var p in NetPlayer.All) if (p != null && NetMatch.IsPlayerChoosing(p.PlayerNumber)) remote = true;
        bool hold = local || remote || Time.unscaledTime < t.closedAt + MapRushChoiceGrace;
        if (!hold)
        {
            if (t.holdFrom >= 0f) t.readyAt = Mathf.Max(t.readyAt, Time.time + 0.5f); // 選択を閉じた直後に出さない
            t.holdFrom = -1f; return false;
        }
        if (t.holdFrom < 0f) { t.holdFrom = Time.unscaledTime; t.choiceHolds++; }
        if (multi && Time.unscaledTime - t.holdFrom > MapRushChoiceWaitCap) return false;
        return true;
    }

    // ボスとボスの間(次のボスの予告の前): 後回しのレベルアップの選択を出してよい
    bool TrioChoiceWindow => CurrentTrio != null && IsBossPhase && CurrentTrio.waiting && !CurrentTrio.telegraph && !BossDefeatedThisPhase;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用: 今のボスを倒す(3連戦の確認を速く)
    public int DebugKillTrioBoss()
    {
        int n = 0;
        foreach (var wb in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
            if (wb != null && !wb.IsDead && wb.gameObject.activeInHierarchy && !(wb is ReaperSisterBoss)) { wb.TakeDamage(99999999, wb.CenterWorld); n++; }
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && !d.IsDead && !d.NetPuppet) { d.TakeDamage(99999999); n++; }
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && !m.IsDead && !m.NetPuppet) { m.TakeDamage(99999999); n++; }
        return n;
    }
#endif
}

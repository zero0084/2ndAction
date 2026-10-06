using System.Collections.Generic;
using UnityEngine;

// ラストダンジョンのボス構成(2026-10-05)。調整値は LastDungeonBossTuning。
//  ・遭遇(BossEncounter): 1つの関門のボス全員をまとめて持つ(出ているボス/まだ出ていないボス/倒した数)。
//    すべての関門で作る(1体だけの関門も同じ形)。ボスラッシュはこの遭遇の「待っているボス」を条件(同時/時間/HP/撃破)で出す。
//  ・0〜89km: 3マップの既存ボスを格(1,000m/5,000m/10,000m)ごとのプールから抽選し、今の距離の強さにする。
//  ・90〜98km: ボスラッシュ。同時に戦うのは maxSimultaneous 体まで、タグ(大型/空中/範囲攻撃/飛び道具/追跡)の上限も守る。
//    超える時は先のボスが倒れるまで待たせる(待っているボスも遭遇の残り数に入るので、途中で関門が終わらない)。
//  ・ラン再開: 0〜89km の関門だけ(LastDungeonBossTuning.milestoneRunResume)。ラン再開したボスが残ったまま
//    ボスラッシュの入口の手前まで来たら再び足止めする(ボスラッシュの関門を保留/飛ばしにしない)。ボスラッシュはラン再開しない。
//  ・走りながらのボスラッシュ(2026-10-06、LastDungeonBossTuning.continuousRush): 90〜99km を「1つの遭遇」にする。
//    距離を止めない(ラン再開と同じ状態で始める)。1,000mごとの節目(90〜98km)で予定のボスを1体ずつ待機キューへ(高速で
//    複数跨いでも順番に全部)。出すのは同時の上限+タグの規則を満たした時だけ、予告(警告+SE)の後に1体ずつ。98kmが最後の増援。
//    99kmでボスが残って(待機中も含む)いればそこで足止めし、全員倒すと報酬 → 静寂区間へ。
public partial class BossManager
{
    public class BossEncounter
    {
        public int gateK;
        public bool rush;
        public string label = "";
        public float startedAt;
        public readonly List<LastDungeonBossTuning.Slot> waiting = new List<LastDungeonBossTuning.Slot>(); // まだ出ていない
        public readonly List<WildBossBase> activeBosses = new List<WildBossBase>();                         // 出ている(生きている)
        public readonly List<string> spawnedKeys = new List<string>();
        public int total, spawned, defeated, maxTogether, killTokens, deferredByRules, watchdogFixes;
        public int Remaining => Mathf.Max(0, total - defeated);
        // 走りながらのボスラッシュ(2026-10-06)
        public bool continuous;
        public int firstK, nextK;                       // 予定の最初の節目 / 次に予約する節目(km)
        public readonly List<string> plan = new List<string>(); // 節目ごとのボス(firstK から)
        public bool lastReinforcementDone;              // 98kmの増援を予約した
        public bool held99;                             // 99kmで足止めした
        public LastDungeonBossTuning.Slot telegraphing; // 予告中(出現の直前)
        public float telegraphEnd, lastSpawnAt = -99f;
        public int milestonesReserved, maxQueue, maxCatchUp;
        public int Queued => waiting.Count + (telegraphing != null ? 1 : 0);
    }
    public BossEncounter CurrentBossEncounter { get; private set; }
    static readonly List<WildBossBase> noBosses = new List<WildBossBase>();
    public IReadOnlyList<WildBossBase> ActiveBosses => CurrentBossEncounter != null ? CurrentBossEncounter.activeBosses : (IReadOnlyList<WildBossBase>)noBosses;
    public int RemainingBossCount => CurrentBossEncounter != null && CurrentBossEncounter.total > 0 ? CurrentBossEncounter.Remaining : AliveBossCount;

    // ボスラッシュ(LastDungeonQa などが読む)
    public const int RushFirstK = 90, RushLastK = 98;
    public static bool ContinuousRush => LastDungeonBossTuning.I.continuousRush;
    public static bool RushEnabled;
    public static bool SuppressGates;
    public static System.Action FinaleAt100k;
    public int RushGateK { get; private set; }         // 今のボスラッシュの関門(0=ボスラッシュではない)
    public int RushSpawnedThisGate { get; private set; }  // 関門が終わった後も読めるように残す(次の関門の開始で0)
    public int RushMaxSimultaneous { get; private set; }
    public static int MaxBossesTogetherSeen;            // 確認用(ラン全体で同時に居たボスの最大)
    public int RushEntranceHolds { get; private set; }
    WildBossBase lastSpawnedWild;

    public static string RushGateLabel(int k)
    {
        if (k < RushFirstK || k > RushLastK) return "";
        if (Instance != null && Instance.CurrentBossEncounter != null && Instance.CurrentBossEncounter.rush && Instance.CurrentBossEncounter.gateK == k) return Instance.CurrentBossEncounter.label;
        var g = LastDungeonBossTuning.I.RushAt(k);
        if (g == null) return "?";
        var parts = new List<string>();
        foreach (var v in g.variants) parts.Add(Describe(v));
        return string.Join(" | ", parts);
    }

    static string Describe(LastDungeonBossTuning.Variant v)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in v.slots)
        {
            if (sb.Length > 0)
                sb.Append(s.trigger == LastDungeonBossTuning.Trigger.Start ? " + " : s.trigger == LastDungeonBossTuning.Trigger.Time ? $" +({s.value:F0}s) " :
                          s.trigger == LastDungeonBossTuning.Trigger.HpBelow ? $" +(HP{s.value * 100f:F0}%) " : " -> ");
            sb.Append(s.key.Substring(s.key.IndexOf('/') + 1));
        }
        return sb.ToString();
    }

    void BeginEncounter(int k, bool rush)
    {
        CurrentBossEncounter = new BossEncounter { gateK = k, rush = rush, startedAt = Time.time };
    }

    // 生成の共通の入口(ApplyRematchTo)から: 出たボスを遭遇へ。ラスダンなら倍率も
    void TrackSpawned(WildBossBase boss)
    {
        lastSpawnedWild = boss;
        if (IsLastStage) ApplyLastDungeonPace(boss);
        var enc = CurrentBossEncounter;
        if (enc == null || boss == null || NetCombat.CreatingPuppet) return;
        if (!enc.activeBosses.Contains(boss)) enc.activeBosses.Add(boss);
        if (!enc.rush) { enc.spawned++; enc.total = Mathf.Max(enc.total, enc.spawned); }
        int together = 0;
        foreach (var b in enc.activeBosses) if (b != null && !b.IsDead) together++;
        enc.maxTogether = Mathf.Max(enc.maxTogether, together);
        MaxBossesTogetherSeen = Mathf.Max(MaxBossesTogetherSeen, together);
    }

    void ApplyLastDungeonPace(WildBossBase boss)
    {
        if (boss == null) return;
        var ld = LastDungeonBossTuning.I;
        boss.ApplyPace(ld.attackFrequencyMul, ld.attackIntervalMul, ld.moveSpeedMul);
    }

    void EncounterOnDefeated(WildBossBase who)
    {
        var enc = CurrentBossEncounter;
        if (enc == null) return;
        if (who != null) enc.activeBosses.Remove(who);
        enc.defeated++;
        enc.killTokens++;
    }

    // ===================================================================== //
    // 0〜89km: 格ごとのプールから抽選
    // ===================================================================== //
    // その種類が「どの格か」は問わず、関門の格のプールから選ぶ。強さは今の距離に合わせる(再戦と同じ計算)+ラスダンの倍率。
    bool ResolveLastMilestone(int k, out GateFamily family, out int kind, out string why)
    {
        family = GateFamily.Wild; kind = 0;
        var ld = LastDungeonBossTuning.I;
        var pool = k % 10 == 0 ? ld.pool10k : k % 5 == 0 ? ld.pool5k : ld.pool1k;
        string cls = k % 10 == 0 ? "10,000m格" : k % 5 == 0 ? "5,000m格" : "1,000m格";
        var cands = new List<string>();
        if (pool != null) foreach (var key in pool) if (ParseKey(key, out _, out _) && !cands.Contains(key)) cands.Add(key);
        if (cands.Count == 0) { why = $"{cls}: プールが空"; return false; }
        string seenNote = "";
        if (!Debug.isDebugBuild && ld.releasePreferSeen)
        {
            var seen = cands.FindAll(ProgressStats.HasSeenBoss);
            if (seen.Count >= Mathf.Max(1, ld.minSeenCandidates)) { cands = seen; seenNote = " 会ったボスだけ"; }
            else seenNote = $" 会ったボスが{seen.Count}体なので全部から";
        }
        int ex = Mathf.Clamp(ld.recentExclude, 0, Mathf.Max(0, cands.Count - 2));
        var pick = new List<string>(cands);
        for (int i = 0; i < Mathf.Min(ex, recentFought.Count); i++) if (pick.Count > 1) pick.Remove(recentFought[i]);
        string key0 = pick[Random.Range(0, pick.Count)]; // HOST/シングルだけが呼ぶ(JOINはボスの関門の処理をしない)
        ParseKey(key0, out family, out kind);
        why = $"{cls} {key0}(候補{pick.Count}/{cands.Count}、直近{ex}体を除外{seenNote})";
        return true;
    }

    // ラスダンのボス1体の強さ(関門の距離に合わせる。再戦と同じ式 + ラスダンの倍率)。生成の直前に呼ぶ
    void SetLastDungeonStrength(string key, float d, bool rush, bool continuous = false)
    {
        if (!ParseKey(key, out GateFamily f, out int kind)) return;
        var tn = BossRematchTuning.I;
        var ld = LastDungeonBossTuning.I;
        currentTier = tn.TierAt(d);
        float first = FirstDistanceOf(f, kind);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(first))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul) * Mathf.Max(0.1f, ld.hpMul) * (rush ? Mathf.Max(0.05f, continuous ? ld.runRushHpMulPerBoss : ld.rushHpMulPerBoss) : 1f);
        CurrentRematchTier = "ラスダン " + currentTier.label;
        DamageMul = Mathf.Max(0.1f, rush ? ld.rushDamageMul : currentTier.damageMul) * Mathf.Max(0.1f, ld.damageMul);
        CurrentEncounterIsRematch = true; // 生成側の「再戦の強さ」(HP/段階/必殺技の間隔/崩し)の経路をそのまま使う
    }

    void StartLastMilestoneGate()
    {
        if (!ResolveLastMilestone(gateK, out GateFamily fam, out int kind, out string why)) { IsBossPhase = false; return; }
        string key = Key(fam, kind);
        SetLastDungeonStrength(key, gateK * gateIntervalMeters, false);
        LastRematchDecision = $"{gateK}km: ラスダン {why} HP×{CurrentRematchHpMul:0.0} 被弾×{DamageMul:0.0}";
        BeginEncounterKey(key, true);
        ProgressStats.MarkBossSeen(key);
        CurrentBossEncounter.label = key;
        Debug.Log($"[LDBoss] Gate {gateK * 1000}m milestone: {why} hp×{CurrentRematchHpMul:0.00} dmg×{DamageMul:0.00} tier={currentTier.label}");
        if (fam == GateFamily.Sky) StartSkyGate((SkyBossKind)kind, 1);
        else if (fam == GateFamily.Cave) StartCaveGate((CaveBossKind)kind, 1);
        else StartWildGate((WildBossKind)kind, 1);
    }

    // ===================================================================== //
    // 90〜98km: ボスラッシュ
    // ===================================================================== //
    void StartRushGate(int k)
    {
        var ld = LastDungeonBossTuning.I;
        var g = ld.RushAt(k);
        BeginEncounter(k, true);
        var enc = CurrentBossEncounter;
        RushGateK = k;
        RushMaxSimultaneous = 0;
        RushSpawnedThisGate = 0;
        LastDungeonBossTuning.Variant v = null;
        if (g != null && g.variants != null && g.variants.Count > 0) v = g.variants[Random.Range(0, g.variants.Count)]; // HOST/シングル
        if (v != null)
            foreach (var s in v.slots)
            {
                if (s == null || !ParseKey(s.key, out _, out _)) { Debug.LogWarning($"[LDBoss] rush {k}km: unknown key {s?.key}"); continue; }
                if ((ld.TagsOf(s.key) & LastDungeonBossTuning.Tag.NoRush) != 0) { Debug.LogWarning($"[LDBoss] rush {k}km: {s.key} is not allowed in the rush (NoRush)"); continue; }
                enc.waiting.Add(s);
            }
        if (enc.waiting.Count == 0) enc.waiting.Add(new LastDungeonBossTuning.Slot("Wild/BlackKnight")); // 表が壊れていても関門は成り立たせる
        enc.total = enc.waiting.Count;
        enc.label = v != null ? $"{v.label}: {Describe(v)}" : "fallback";
        aliveWildThisEncounter = enc.total; // 全員(後から出る分も含む)を倒すまで関門は終わらない
        CurrentEncounterKey = "";
        BossMusicTier = BossBgmTier.Final; // 音の再設計(2026-10-06): ラスダンのラッシュは最終ボス曲
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        BossMusicKey = $"{stage}/Rush{k}";
        BossDefeatedThisPhase = false;
        BeginEncounterClock("Rush" + k); // ボスラッシュはラン再開しない(BeginEncounterClock が RushGateK を見る)
        Debug.Log($"[BossRush] Gate {k * 1000}m Start: {enc.label} (bosses {enc.total}, together ≤{ld.maxSimultaneous})");
        rushZakoCheckAt = 0f; RushClearZako(); // 入口の手前で置かれた雑魚もここで
        RushSpawnDue(enc);
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("CombatStart");
    }

    float ActiveHpFraction(BossEncounter enc)
    {
        long hp = 0, max = 0;
        foreach (var b in enc.activeBosses) if (b != null && !b.IsDead) { hp += Mathf.Max(0, b.Hp); max += Mathf.Max(1, b.maxHp); }
        return max > 0 ? (float)hp / max : 0f;
    }

    int ActiveCount(BossEncounter enc)
    {
        int n = 0;
        foreach (var b in enc.activeBosses) if (b != null && !b.IsDead) n++;
        return n;
    }

    // 同時の上限とタグの規則(大型/空中/範囲攻撃/飛び道具/追跡が重なりすぎない)
    bool RushCanAdd(BossEncounter enc, string key, out string why)
    {
        var ld = LastDungeonBossTuning.I;
        int alive = ActiveCount(enc);
        why = "";
        if (alive == 0) return true;
        if (alive >= Mathf.Max(1, ld.maxSimultaneous)) { why = $"together {alive}/{ld.maxSimultaneous}"; return false; }
        var t = ld.TagsOf(key);
        int large = 0, air = 0, area = 0, proj = 0, chase = 0;
        foreach (var b in enc.activeBosses)
        {
            if (b == null || b.IsDead) continue;
            var bt = ld.TagsOf(KeyOf(b));
            if ((bt & LastDungeonBossTuning.Tag.Large) != 0) large++;
            if ((bt & LastDungeonBossTuning.Tag.Air) != 0) air++;
            if ((bt & LastDungeonBossTuning.Tag.AreaAttack) != 0) area++;
            if ((bt & LastDungeonBossTuning.Tag.Projectile) != 0) proj++;
            if ((bt & LastDungeonBossTuning.Tag.Chaser) != 0) chase++;
        }
        if ((t & LastDungeonBossTuning.Tag.Large) != 0 && large + 1 > ld.maxLarge) { why = "LARGE"; return false; }
        if ((t & LastDungeonBossTuning.Tag.Air) != 0 && air + 1 > ld.maxAir) { why = "AIR"; return false; }
        if ((t & LastDungeonBossTuning.Tag.AreaAttack) != 0 && area + 1 > ld.maxAreaAttack) { why = "AREA_ATTACK"; return false; }
        if ((t & LastDungeonBossTuning.Tag.Projectile) != 0 && proj + 1 > ld.maxProjectile) { why = "PROJECTILE"; return false; }
        if ((t & LastDungeonBossTuning.Tag.Chaser) != 0 && chase + 1 > ld.maxChaser) { why = "CHASER"; return false; }
        return true;
    }

    readonly Dictionary<WildBossBase, string> bossKeys = new Dictionary<WildBossBase, string>();
    string KeyOf(WildBossBase b) => b != null && bossKeys.TryGetValue(b, out var k) ? k : "";

    // 待っているボスのうち、条件を満たしたものを出す(前から順に。撃破で次は撃破1回につき1体)
    void RushSpawnDue(BossEncounter enc)
    {
        if (enc != null && enc.continuous) { ContinuousRushTick(enc); return; }
        if (enc == null || !enc.rush || enc.waiting.Count == 0 || !IsBossPhase) return;
        float elapsed = Time.time - enc.startedAt;
        for (int i = 0; i < enc.waiting.Count; i++)
        {
            var s = enc.waiting[i];
            bool empty = ActiveCount(enc) == 0;
            bool due;
            switch (s.trigger)
            {
                case LastDungeonBossTuning.Trigger.Time: due = elapsed >= s.value; break;
                case LastDungeonBossTuning.Trigger.HpBelow: due = ActiveHpFraction(enc) <= s.value; break;
                case LastDungeonBossTuning.Trigger.OnKill: due = enc.killTokens > 0; break;
                default: due = true; break;
            }
            if (!due && !empty) continue; // 誰も居なくなったら、条件を待たずに次を出す(待ち時間で棒立ちにさせない)
            if (!RushCanAdd(enc, s.key, out string why))
            {
                enc.deferredByRules++;
                if (Time.time - lastDeferLog > 2f) { lastDeferLog = Time.time; Debug.Log($"[BossRush] wait {s.key} ({s.trigger}) - {why}"); }
                continue;
            }
            if (s.trigger == LastDungeonBossTuning.Trigger.OnKill && enc.killTokens > 0) enc.killTokens--;
            enc.waiting.RemoveAt(i); i--;
            SpawnRushBoss(enc, s);
        }
    }
    float lastDeferLog;

    // ボスの生成にかかった時間(確認用: 出現の瞬間の処理落ち)
    public static float LastBossSpawnMs, MaxBossSpawnMs;
    static readonly System.Diagnostics.Stopwatch spawnWatch = new System.Diagnostics.Stopwatch();

    void SpawnRushBoss(BossEncounter enc, LastDungeonBossTuning.Slot s)
    {
        spawnWatch.Restart();
        try { SpawnRushBossImpl(enc, s); }
        finally
        {
            spawnWatch.Stop();
            LastBossSpawnMs = (float)spawnWatch.Elapsed.TotalMilliseconds;
            MaxBossSpawnMs = Mathf.Max(MaxBossSpawnMs, LastBossSpawnMs);
            Debug.Log($"[BossRush] spawn {s.key} took {LastBossSpawnMs:F1}ms");
        }
    }

    void SpawnRushBossImpl(BossEncounter enc, LastDungeonBossTuning.Slot s)
    {
        ParseKey(s.key, out GateFamily fam, out int kind);
        // 同時に並ぶ間合いの枠(出ているボスが使っていない一番小さい番号)
        int slot = 0;
        for (; slot < 4; slot++) { bool used = false; foreach (var b in enc.activeBosses) if (b != null && !b.IsDead && b.slotIndex == slot) used = true; if (!used) break; }
        SetLastDungeonStrength(s.key, (enc.continuous ? s.value : enc.gateK) * gateIntervalMeters, true, enc.continuous); // 走りながら: その節目の距離の強さ
        lastSpawnedWild = null;
        if (fam == GateFamily.Cave)
        {
            SpawnCaveBoss((CaveBossKind)kind, slot);
            if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null) TerrainManager.Instance.cave.SetBossClearZone(SpawnRef().position.x, 40f);
            encounterHasCave = true;
        }
        else if (fam == GateFamily.Sky) SpawnSkyBoss((SkyBossKind)kind, slot);
        else SpawnWild((WildBossKind)kind, slot);
        var boss = lastSpawnedWild;
        if (boss != null) bossKeys[boss] = s.key;
        enc.spawned++;
        RushSpawnedThisGate = enc.spawned;
        enc.spawnedKeys.Add(s.key);
        ProgressStats.MarkBossSeen(s.key);
        int alive = ActiveCount(enc);
        RushMaxSimultaneous = Mathf.Max(RushMaxSimultaneous, alive);
        enc.lastSpawnAt = Time.time;
        Debug.Log($"[BossRush] Boss {enc.spawned}/{enc.total} Spawn {s.key}{(enc.continuous ? $" (milestone {s.value:F0}km, queue {enc.Queued}, d={(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0})" : "")} ({s.trigger}{(s.trigger == LastDungeonBossTuning.Trigger.Time ? $" {s.value:F0}s" : s.trigger == LastDungeonBossTuning.Trigger.HpBelow ? $" {s.value * 100f:F0}%" : "")}) gate {enc.gateK * 1000}m alive={alive} hp={(boss != null ? boss.maxHp : 0)} t={Time.time - enc.startedAt:F1}s");
    }
    bool encounterHasCave;

    // 毎フレーム(BossManager.Update): 待っているボスの条件 / 消えたボスの見張り / ボスラッシュの入口の足止め
    void LastDungeonTick()
    {
        var enc = CurrentBossEncounter;
        if (enc != null && IsBossPhase && !NetCombat.Replica)
        {
            // 見張り: 撃破の処理を通らずに消えたボス(破棄)を残り数から外す(「ボスが残っている扱いで次が出ない」を防ぐ)
            for (int i = enc.activeBosses.Count - 1; i >= 0; i--)
            {
                var b = enc.activeBosses[i];
                if (b != null) continue;
                enc.activeBosses.RemoveAt(i);
                enc.watchdogFixes++;
                Debug.LogWarning($"[LDBoss] watchdog: a boss of gate {enc.gateK * 1000}m vanished without the defeat callback - counted as defeated");
                OnWildBossDefeated(null);
                if (CurrentBossEncounter != enc) return;
            }
            if (enc.rush) RushSpawnDue(enc);
        }
        if (RushGateK > 0) RushClearZako();
        if (enc != null && enc.continuous && IsBossPhase) PrewarmNextRushBoss(enc);
        RushEntranceHold();
    }

    // 90〜99km は雑魚なし: ボスラッシュの入口の手前(ラン再開中など)で置かれた編成が、関門の開始の後で出てくることがある
    // (関門の開始の片付けは地形の区画に登録された雑魚だけ) → ボスラッシュの間は見つけ次第片付ける
    float rushZakoCheckAt;
    public int RushZakoCleared { get; private set; }
    void RushClearZako()
    {
        if (Time.time < rushZakoCheckAt || NetCombat.Replica) return;
        rushZakoCheckAt = Time.time + 0.1f;
        int n = 0;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e != null && e.gameObject.activeInHierarchy && !e.IsDying && e.GetComponent<BonusEnemy>() == null) { Destroy(e.gameObject); n++; }
        if (n > 0) { RushZakoCleared += n; Debug.Log($"[BossRush] cleared {n} normal enemies during the rush gate {RushGateK * 1000}m (no zako 90-99km)"); }
    }

    // ラン再開したボスが残ったまま、ボスラッシュの入口まで来た → 再び足止め(ボスラッシュの関門を飛ばさない/保留にしない)
    void RushEntranceHold()
    {
        if (!RushEnabled || !IsLastStage || !IsBossPhase || !RunResumed || RushGateK != 0 || AliveBossCount <= 0) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.MaxDistance < RushFirstK * gateIntervalMeters - Mathf.Max(0f, LastDungeonBossTuning.I.rushEntranceHoldMeters)) return;
        RunResumed = false;
        encounterResumable = false;
        gm.BeginBossDistanceExclusion();
        RushEntranceHolds++;
        Debug.Log($"[LDBoss] rush entrance hold: the resumed boss is still alive at {gm.MaxDistance:F0}m - the run stops until it is defeated");
        BossBattleHud.Banner("ボスを倒さないと先へ進めない!", new Color(1f, 0.45f, 0.35f), 2.2f);
    }

    // ===================================================================== //
    // 90〜99km: 走りながらのボスラッシュ(2026-10-06)
    // ===================================================================== //
    // BossManager.Update(関門の判定の前)から: 90kmの節目に着いたら、距離を止めずに始める
    bool TryStartContinuousRush()
    {
        if (!RushEnabled || !ContinuousRush || !IsLastStage || !useWildSchedule || ArenaMode.Active) return false;
        if (gateK < RushFirstK || gateK > RushLastK) return false;
        if (GateDistance() < gateK * gateIntervalMeters) return false;
        StartContinuousRush(gateK);
        return true;
    }

    // 節目ごとのボスの予定(90〜98km の表の抽選した組から、まだ使っていないボスを1体ずつ。重ならない/NoRush は使わない)
    List<string> BuildRushPlan(int fromK)
    {
        var ld = LastDungeonBossTuning.I;
        var plan = new List<string>();
        bool Usable(string key) => !string.IsNullOrEmpty(key) && ParseKey(key, out _, out _) && (ld.TagsOf(key) & LastDungeonBossTuning.Tag.NoRush) == 0 && !plan.Contains(key);
        for (int k = fromK; k <= RushLastK; k++)
        {
            string pick = null;
            var g = ld.RushAt(k);
            if (g != null && g.variants != null && g.variants.Count > 0)
            {
                int vi = Random.Range(0, g.variants.Count); // HOST/シングル
                for (int n = 0; n < g.variants.Count && pick == null; n++)
                {
                    var v = g.variants[(vi + n) % g.variants.Count];
                    if (v != null) foreach (var s in v.slots) if (s != null && Usable(s.key)) { pick = s.key; break; }
                }
            }
            if (pick == null && ld.pool10k != null) // 表が足りない時: 10,000m格のプールから(まだ使っていない/ラッシュ可)
            {
                var c = ld.pool10k.FindAll(x => Usable(x));
                if (c.Count > 0) pick = c[Random.Range(0, c.Count)];
            }
            plan.Add(pick ?? "Wild/BlackKnight");
        }
        return plan;
    }

    void StartContinuousRush(int fromK)
    {
        var gm = GameManager.Instance;
        IsBossPhase = true;
        if (gm != null) gm.LogBoss("PhaseStart");
        gateK = fromK;
        currentGateK = RushLastK; // 遭遇の終わりの次の関門の決め方(ChooseNextGate)を 99km 以降にする(ボスラッシュの関門を保留にしない)
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0;
        ClearEnemiesForBoss();
        BeginEncounter(fromK, true);
        var enc = CurrentBossEncounter;
        enc.continuous = true;
        enc.firstK = fromK; enc.nextK = fromK;
        enc.plan.AddRange(BuildRushPlan(fromK));
        enc.total = RushLastK - fromK + 1;
        enc.label = "RUN RUSH: " + string.Join(" > ", enc.plan.ConvertAll(k => ShortName(k)));
        aliveWildThisEncounter = enc.total; // 予定の全員(まだ節目に着いていない分も)を倒すまで遭遇は終わらない
        ResetRematchEncounter(); CurrentEncounterKey = "";
        RushGateK = fromK; RushMaxSimultaneous = 0; RushSpawnedThisGate = 0; RushTelegraphs = 0; prewarmedK = 0;
        BossMusicTier = BossBgmTier.Final; // 音の再設計(2026-10-06): ラスダンのラッシュは最終ボス曲
        BossMusicKey = $"{(gm != null ? gm.ActiveRunStageId : "")}/Rush{fromK}";
        BossDefeatedThisPhase = false;
        BeginEncounterClock("RunRush" + fromK); // ラン再開の時計は使わない(RushGateK)
        // 距離は止めない: ラン再開と同じ状態(除外0を「折りたたみ済み」にして、報酬の後で二重に除外しない)
        if (gm != null) { gm.BeginBossDistanceExclusion(); gm.ResumeBossDistance(); }
        RunResumed = true; resumedAt = Time.time;
        Debug.Log($"[BossRush] RUN RUSH Start at {(gm != null ? gm.MaxDistance : 0f):F0}m from {fromK}km: {enc.label} (bosses {enc.total}, together <={LastDungeonBossTuning.I.maxSimultaneous})");
        BossBattleHud.Banner("BOSS RUSH  走りながら戦え!", new Color(1f, 0.45f, 0.3f), 2.0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        rushZakoCheckAt = 0f; RushClearZako();
        if (gm != null) gm.LogBoss("CombatStart");
        ContinuousRushTick(enc);
    }

    static string ShortName(string key) => string.IsNullOrEmpty(key) ? "?" : key.Substring(key.IndexOf('/') + 1).ToUpperInvariant();

    // 毎フレーム(LastDungeonTick)/撃破の時: 節目の予約 → 99kmの足止め → 予告 → 出現
    void ContinuousRushTick(BossEncounter enc)
    {
        if (enc == null || !enc.continuous || !IsBossPhase || NetCombat.Replica) return;
        var ld = LastDungeonBossTuning.I;
        float d = GateDistance();
        // 1) 節目の予約: 高速で 1,000m を何度も跨いでも、跨いだ節目の分を順番に全部(取りこぼさない)
        int added = 0;
        while (enc.nextK <= RushLastK && d >= enc.nextK * gateIntervalMeters - 0.5f)
        {
            int k = enc.nextK;
            ReserveMilestone(enc, k);
            added++;
            Debug.Log($"[BossRush] milestone {k}km reached at {d:F0}m -> {enc.waiting[enc.waiting.Count - 1].key} reserved (alive {ActiveCount(enc)}, queue {enc.Queued})");
        }
        enc.maxCatchUp = Mathf.Max(enc.maxCatchUp, added);
        enc.maxQueue = Mathf.Max(enc.maxQueue, enc.waiting.Count);
        // 2) 99km: ボスが残っていれば(待機中も含む)ここだけ足止め
        if (!enc.held99 && RunResumed && d >= (RushLastK + 1) * gateIntervalMeters - 0.5f && enc.Remaining > 0)
        {
            enc.held99 = true;
            RunResumed = false;
            var gm = GameManager.Instance;
            if (gm != null) { gm.ClampMaxDistanceTo((RushLastK + 1) * gateIntervalMeters); gm.BeginBossDistanceExclusion(); }
            Debug.Log($"[BossRush] 99km hold: {enc.Remaining} boss(es) remain (alive {ActiveCount(enc)}, queue {enc.Queued}) - the run stops until all are defeated");
            BossBattleHud.Banner("残ったボスを倒せ!", new Color(1f, 0.45f, 0.35f), 2.2f);
        }
        // 3) 予告 → 出現(1体ずつ。予告の間もゲームは止めない)
        if (enc.telegraphing != null)
        {
            if (Time.time < enc.telegraphEnd) return;
            if (!RushCanAdd(enc, enc.telegraphing.key, out _)) return; // 予告の間に空きが無くなった(通常は起きない)→ 空くまで待つ
            var s = enc.telegraphing;
            enc.telegraphing = null;
            SpawnRushBoss(enc, s);
            return;
        }
        if (enc.waiting.Count == 0 || Time.time - enc.lastSpawnAt < Mathf.Max(0f, ld.minSpawnSpacingSeconds)) return;
        for (int i = 0; i < enc.waiting.Count; i++)
        {
            var s = enc.waiting[i];
            if (!RushCanAdd(enc, s.key, out string why))
            {
                enc.deferredByRules++;
                if (Time.time - lastDeferLog > 2f) { lastDeferLog = Time.time; Debug.Log($"[BossRush] wait {s.key} (milestone {s.value:F0}km) - {why}"); }
                continue; // 待たせる(順番は保つ。後ろのボスが規則に合えば先に出る)
            }
            enc.waiting.RemoveAt(i);
            enc.telegraphing = s;
            enc.telegraphEnd = Time.time + Mathf.Max(0f, ld.telegraphSeconds);
            BossBattleHud.Banner($"WARNING  {ShortName(s.key)} が迫る!", new Color(1f, 0.35f, 0.25f), Mathf.Max(0.8f, ld.telegraphSeconds + 0.4f));
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
            RushTelegraphs++;
            Debug.Log($"[BossRush] telegraph {s.key} (milestone {s.value:F0}km) - spawn in {ld.telegraphSeconds:F1}s");
            break;
        }
    }
    public int RushTelegraphs { get; private set; }

    void ReserveMilestone(BossEncounter enc, int k)
    {
        int idx = k - enc.firstK;
        string key = idx >= 0 && idx < enc.plan.Count ? enc.plan[idx] : "Wild/BlackKnight";
        enc.waiting.Add(new LastDungeonBossTuning.Slot(key, LastDungeonBossTuning.Trigger.Start, k));
        enc.milestonesReserved++;
        enc.nextK = k + 1;
        if (k >= RushLastK) { enc.lastReinforcementDone = true; Debug.Log($"[BossRush] {k}km: last reinforcement reserved"); }
    }

    // 次の節目のボスの絵/データを先に読み込んでおく(出現の瞬間の読み込みを減らす)。重い生成そのものは出現の時(1体ずつ)
    int prewarmedK;
    void PrewarmNextRushBoss(BossEncounter enc)
    {
        if (enc.nextK > RushLastK || prewarmedK == enc.nextK) return;
        float d = GateDistance();
        if (d < enc.nextK * gateIntervalMeters - 200f) return;
        prewarmedK = enc.nextK;
        int idx = enc.nextK - enc.firstK;
        if (idx < 0 || idx >= enc.plan.Count) return;
        PrewarmBossAssets(enc.plan[idx]);
    }

    // 出現の時に読み込む絵(効果の絵は Resources から遅れて読む)を先に読んでおく。1回だけ
    static bool effectsPrewarmed;
    static readonly List<Object> prewarmed = new List<Object>();
    void PrewarmBossAssets(string key)
    {
        if (!effectsPrewarmed)
        {
            effectsPrewarmed = true;
            prewarmed.AddRange(Resources.LoadAll<Sprite>("Effects"));
        }
        Debug.Log($"[BossRush] prewarm before the next milestone: {key} (effects {prewarmed.Count})");
    }

    // GameOver/クリアの確定(GameManager.GameOverCleanup、2026-10-06): ボスのAI/攻撃の予約/増援/関門の判定を止める。
    // 撃破としては数えない(報酬/保存/次の関門の決定を通さない)。何度呼んでも同じ。次のランはシーンの読み直しで最初から。
    public bool RunEndStopped { get; private set; }
    public void StopForRunEnd()
    {
        if (RunEndStopped) return;
        RunEndStopped = true;
        var enc = CurrentBossEncounter;
        if (enc != null) { enc.waiting.Clear(); enc.telegraphing = null; }
        RunResumed = false; encounterResumable = false; pendingChosen = false;
        int n = 0;
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
            if (b != null && b.enabled) { b.StopAllCoroutines(); b.enabled = false; n++; }
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && d.enabled) { d.StopAllCoroutines(); d.enabled = false; n++; }
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && m.enabled) { m.StopAllCoroutines(); m.enabled = false; n++; }
        Debug.Log($"[Death] GameOver cleanup: stopped {n} boss AI(s), encounter={(enc != null ? (enc.continuous ? "run rush" : enc.rush ? "rush gate" : "gate " + enc.gateK) : "none")} bossPhase={IsBossPhase}");
    }

    // 開発用(LAST DUNGEON 99km): fromK から始めた状態(fromK〜98km のボスが今の距離までの分だけ予約済み)にする
    // → 99kmの足止め → 全滅 → 静寂を確かめる
    public void DebugStartRushLate(int fromK)
    {
        if (!RushEnabled || !ContinuousRush || !IsLastStage) return;
        StartContinuousRush(Mathf.Clamp(fromK, RushFirstK, RushLastK));
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // NEXT BOSS(開発用、走りながらのボスラッシュ): 出ているボスを倒して、次の増援へ進める
    // (待機中/予告中があればそれが出る。無ければ次の節目のボスを今すぐ予約する)。距離はワープしない
    public string DebugRushAdvance()
    {
        var enc = CurrentBossEncounter;
        if (enc == null || !enc.continuous) return null;
        int killed = 0;
        foreach (var b in new List<WildBossBase>(enc.activeBosses)) if (b != null && !b.IsDead) { b.TakeDamage(99999999, b.CenterWorld); killed++; }
        string what;
        if (enc.Queued > 0) what = $"待機中の増援 {enc.Queued} 体が出ます";
        else if (enc.nextK <= RushLastK)
        {
            int k = enc.nextK;
            ReserveMilestone(enc, k);
            enc.lastSpawnAt = -99f;
            what = $"{k}km の増援({ShortName(enc.waiting[enc.waiting.Count - 1].key)})を今すぐ予約";
            Debug.Log($"[BossRush] DEBUG next boss: {k}km reserved early (killed {killed})");
        }
        else what = "増援はもうありません(98kmが最後)";
        return $"倒した {killed} 体 / {what}";
    }

    // テスト: ボスの予定を差し替える(同じ並びで比べる)
    public void DebugSetRushPlan(List<string> keys)
    {
        var enc = CurrentBossEncounter;
        if (enc == null || !enc.continuous || keys == null) return;
        for (int i = 0; i < enc.plan.Count && i < keys.Count; i++) enc.plan[i] = keys[i];
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // NEXT BOSS(開発用): 今の遭遇のボスを全員(まだ出ていないボスも)片付けて、関門を終わらせる
    public int DebugFinishEncounter()
    {
        int n = 0;
        var enc = CurrentBossEncounter;
        if (enc != null && enc.waiting.Count > 0)
        {
            n += enc.waiting.Count;
            aliveWildThisEncounter -= enc.waiting.Count;
            enc.total -= enc.waiting.Count;
            enc.waiting.Clear();
        }
        foreach (var wb in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
            if (wb != null && !wb.IsDead && wb.gameObject.activeInHierarchy && !(wb is ReaperSisterBoss)) { wb.TakeDamage(99999999, wb.CenterWorld); n++; }
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && !d.IsDead && !d.NetPuppet) { d.TakeDamage(99999999); n++; }
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && !m.IsDead && !m.NetPuppet) { m.TakeDamage(99999999); n++; }
        // 全員が撃破の演出の前に消えていた時(残り数だけ残った)も関門を終わらせる
        if (IsBossPhase && AliveBossCount > 0 && enc != null && ActiveCount(enc) == 0 && FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Length == 0)
        {
            aliveWildThisEncounter = 0;
            CheckEncounterComplete();
        }
        return n;
    }
#endif
}

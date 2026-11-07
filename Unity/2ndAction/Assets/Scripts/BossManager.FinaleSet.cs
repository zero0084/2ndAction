using System.Collections.Generic;
using UnityEngine;

// 通常マップの終盤 95〜99km「3体同時・走りながらのボスラッシュ」(2026-11-06、マスターの依頼)。
// 荒野街道 / 自然洞窟 / 天空回廊(ソロ・CPU サポート・協力の HOST)。ラスダンは変えない(あちらは最大3体まで溜まっていく長い戦い)。
//  ・95 / 96 / 97km の節目ごとに「組(セット)」= ボス3体。距離は止めない。前の組の残りがいれば空いた分だけ補充(同時は最大3体)。
//    出ていない残りは次の組の予約で捨てる(短く濃い組を続ける。溜め込まない)= 既定の "refill"。-setMode stack で溜める方式と比べる。
//  ・98km: そのマップのラスボス(2026-11-06: 専用のラスボス 荒野王ガルムード/深淵の主ノクタリス/天帝セラフィエル、BossManager.FinalBoss.cs)。場に組のボスが 0体の時だけ出る(1対1)。
//    98.5km で組のボスが残っていればそこで足止め → 全員倒すとラスボスが出て走行再開。99km でまだ残っていれば今までどおり足止め。
//  ・ラスボスを倒すと関門の突破(報酬の選択は1回)→ 99〜100km は静かな道 → 100km の死神(今まで通り)。
//  ・強さ: その節目の距離での再戦の強さ × 組の格の倍率(SetHpMul)。ラスボスは FinalHpMul。
//  ・攻撃の同時の上限: 通常攻撃は同時でよい / 強攻撃(溜め 0.8 秒以上)は同時に2体まで(BossAttackGate)/ 必殺技は1体ずつ(BossBattle)。
//  ・組のボスを倒すたびに小さな回復(SetKillHealFrac)。レベルアップの選択は場のボスが 0体の合間にまとめて出す。
public partial class BossManager
{
    public static bool FinaleSetRush = true;            // -setRush 0 で以前の「3体を1体ずつ」(FinaleTrio)
    public static bool SetRefill = true;                // -setMode stack で溜める方式
    public const int SetFirstK = 95, SetLastSetK = 97, SetFinalK = 98;
    public static int SetMaxAlive = 3;
    public static float[] SetHpMul = { 0.55f, 0.65f, 0.75f }; // 格 C1/C2/C3(その節目の距離の再戦の強さに掛ける)
    public static float FinalHpMul = 1.35f; // 2026-11-06(F2): 強デッキで 28 秒だった → 約 40 秒へ
    public static float SetSpawnSpacing = 0.45f, SetTelegraphSeconds = 0.8f;
    public static float SetHoldMeters = 98500f;
    public static float SetKillHealFrac = 0.05f; // 2026-11-06(F2): 0.08 → 0.05(組の撃破の回復が終盤の被弾の4割を戻していた)
    // マップ別(F2 の測定: 荒野は強デッキの放置でも余裕、天空は中デッキの人でも 98.5km で倒れた)
    //  荒野 = 組のボスの攻撃の頻度を上げる / 天空 = 組のボスの HP を少し下げる
    public static float WastelandSetFreqMul = 1.6f, WastelandSetWaitMul = 0.7f, WastelandSetHpMul = 1.12f, SkySetHpMul = 0.72f, SkySetDamageMul = 0.6f, WastelandSetDamageMul = 1.2f, WastelandFinalHpMul = 1.15f, CaveSetDamageMul = 0.7f; // R(人の操作): 洞窟/天空は強デッキの人でも 97km 前後で倒れた → 0.85→0.7 / 0.7→0.6 // F4: 天空は強デッキの人でも 97km で倒れた(マジンの光の柱など)// F3: 荒野はまだ放置で余裕、天空は人でも倒れた
    static readonly string[] FinalBossFallback = { "Wild/BlackKnight", "Cave/AncientDemon", "Sky/Guardian" };

    public bool SetRushActive => CurrentBossEncounter != null && CurrentBossEncounter.continuous && CurrentBossEncounter.setRush;
    readonly Dictionary<LastDungeonBossTuning.Slot, int> setSlotClass = new Dictionary<LastDungeonBossTuning.Slot, int>();
    public int SetsStarted { get; private set; }
    public int SetBossesSpawned { get; private set; }
    public int SetLeftoversDropped { get; private set; }
    public int SetHolds98 { get; private set; }
    public int MaxSetAlive { get; private set; }
    public float SetStartedAt { get; private set; } = -1f;
    public float FinalSpawnedAt { get; private set; } = -1f;
    public int SetKillHeals { get; private set; }
    public static readonly List<string> SetLog = new List<string>(); // 確認用

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void SetDevArgs()
    {
        var a = QaArgs.All;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-setRush") FinaleSetRush = a[i + 1] != "0";
            if (a[i] == "-setMode") SetRefill = a[i + 1] != "stack";
            if (a[i] == "-setHp") { var p = a[i + 1].Split(','); for (int j = 0; j < p.Length && j < 3; j++) if (float.TryParse(p[j], System.Globalization.NumberStyles.Float, inv, out float v)) SetHpMul[j] = v; }
            if (a[i] == "-finalHp" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, inv, out float fh)) FinalHpMul = fh;
            if (a[i] == "-setHeart") { var p = a[i + 1].Split(','); if (p.Length >= 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out float hw) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float hc) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float hs)) { WastelandSetHeartMul = hw; CaveSetHeartMul = hc; SkySetHeartMul = hs; } } // 荒野,洞窟,天空
            if (a[i] == "-heavyMax" && int.TryParse(a[i + 1], out int hm)) BossAttackGate.MaxHeavy = hm;
            if (a[i] == "-finalBossOld") FinalBossOld = a[i + 1] == "1";
        }
    }
#endif

    bool SetRushStage => MapFinaleStage && FinaleSetRush && !IsLastStage;

    // そのマップのラスボスの鍵(2026-11-06: 専用のラスボス。-finalBossOld 1 で以前の仮の強いボス = 比較用)
    public static string FinalBossKeyFor(string stageId)
    {
        int i = stageId == "natural_cave" ? 1 : stageId == "sky_corridor" ? 2 : 0;
        return FinalBossOld ? FinalBossFallback[i] : FinalBossKeys[i];
    }

    // BossManager.Update(関門の判定の前)から: 95km の節目に着いたら、距離を止めずに始める
    bool TryStartSetRush()
    {
        if (!SetRushStage || !useWildSchedule || ArenaMode.Active) return false;
        if (gateK != SetFirstK) return false;
        if (GateDistance() < gateK * gateIntervalMeters) return false;
        StartSetRush();
        return true;
    }

    void StartSetRush()
    {
        var gm = GameManager.Instance;
        IsBossPhase = true;
        if (gm != null) gm.LogBoss("PhaseStart");
        gateK = SetFirstK;
        currentGateK = RushLastK + 1; // 次の関門は 99km 以降で決める(99km の関門は FinaleTrioGate → 下で飛ばす)
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0;
        ClearEnemiesForBoss();
        BeginEncounter(SetFirstK, true);
        var enc = CurrentBossEncounter;
        enc.continuous = true;
        enc.mapRush = true;   // 各マップのラッシュの流れ(雑魚なし・死神の待ち・選択の合間・完了の処理)を使う
        enc.setRush = true;
        enc.firstK = SetFirstK; enc.nextK = SetFirstK;
        enc.total = (SetLastSetK - SetFirstK + 1) * 3 + 1; // 予定の全員(まだ節目に着いていない組とラスボスも)。refill で捨てた分は引く
        MapRushesStarted++;
        SetsStarted++;
        setSlotClass.Clear();
        enc.label = "SET RUSH";
        aliveWildThisEncounter = enc.total; // 予定の全員を倒すまで遭遇は終わらない
        ResetRematchEncounter(); CurrentEncounterKey = "";
        RushGateK = SetFirstK; RushMaxSimultaneous = 0; RushSpawnedThisGate = 0; RushTelegraphs = 0; prewarmedK = 0;
        BossMusicTier = BossBgmTier.Special;
        BossMusicKey = $"{(gm != null ? gm.ActiveRunStageId : "")}/SetRush";
        BossDefeatedThisPhase = false;
        BeginEncounterClock("SetRush");
        if (gm != null) { gm.BeginBossDistanceExclusion(); gm.ResumeBossDistance(); }
        RunResumed = true; resumedAt = Time.time;
        SetStartedAt = Time.time; FinalSpawnedAt = -1f;
        Debug.Log($"[SetRush] start at {(gm != null ? gm.MaxDistance : 0f):F0}m ({(SetRefill ? "refill" : "stack")}, alive<={SetMaxAlive})");
        BossBattleHud.Banner("BOSS RUSH  ×3  走りながら戦え!", new Color(1f, 0.4f, 0.28f), 2.0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        rushZakoCheckAt = 0f; RushClearZako();
        if (gm != null) gm.LogBoss("CombatStart");
        SetRushTick(enc);
    }

    // 組の予約(3体)。refill: まだ出ていない前の組の残りは捨てる
    void ReserveSet(BossEncounter enc, int k)
    {
        if (SetRefill && enc.waiting.Count > 0)
        {
            int drop = enc.waiting.Count;
            foreach (var w in enc.waiting) setSlotClass.Remove(w);
            enc.waiting.Clear();
            enc.total -= drop; aliveWildThisEncounter -= drop;
            SetLeftoversDropped += drop;
            Debug.Log($"[SetRush] {k}km: dropped {drop} unspawned boss(es) of the previous set (refill)");
        }
        var avoid = new HashSet<string>();
        foreach (var b in enc.activeBosses) if (b != null && !b.IsDead) avoid.Add(KeyOf(b));
        foreach (var w in enc.waiting) avoid.Add(w.key);
        var plan = BuildTrioPlan(k, out var cls, avoid);
        string desc = "";
        for (int i = 0; i < plan.Count; i++)
        {
            var s = new LastDungeonBossTuning.Slot(plan[i], LastDungeonBossTuning.Trigger.Start, k);
            setSlotClass[s] = cls[i];
            enc.waiting.Add(s);
            desc += (i > 0 ? " / " : "") + $"{ShortName(plan[i])}(C{cls[i] + 1})";
        }
        enc.milestonesReserved++;
        enc.nextK = k + 1;
        enc.lastSpawnAt = -99f;
        SetLog.Add($"{k}km set: {desc}");
        Debug.Log($"[SetRush] {k}km set reserved: {desc} (alive {ActiveCount(enc)}, queue {enc.Queued})");
        BossBattleHud.Banner($"BOSS SET  {k}km", new Color(1f, 0.45f, 0.3f), 1.4f);
    }

    static readonly float[] noHolds = new float[0];
    static readonly float[] setHolds = { 98500f, 99000f };
    // 地形(TerrainManager)が平地にする所: 組のラッシュの区間に入ってから(またはその手前)の足止めの地点
    public float[] SetRushHoldPoints => SetRushStage && (SetRushActive || (gateK == SetFirstK && !IsBossPhase)) ? setHolds : noHolds;

    void ReserveFinal(BossEncounter enc)
    {
        // refill: まだ出ていない組の残りはここで捨てる(ラスボスの前に場のボスだけを倒せばよい)
        if (SetRefill && enc.waiting.Count > 0)
        {
            int drop = enc.waiting.Count;
            foreach (var w in enc.waiting) setSlotClass.Remove(w);
            enc.waiting.Clear();
            enc.total -= drop; aliveWildThisEncounter -= drop;
            SetLeftoversDropped += drop;
            Debug.Log($"[SetRush] {SetFinalK}km: dropped {drop} unspawned set boss(es) before the final boss (refill)");
        }
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        var s = new LastDungeonBossTuning.Slot(FinalBossKeyFor(stage), LastDungeonBossTuning.Trigger.Start, SetFinalK);
        setSlotClass[s] = 3; // 3 = ラスボス
        enc.waiting.Add(s);
        enc.nextK = SetFinalK + 1;
        enc.lastReinforcementDone = true;
        PrewarmBossAssets(s.key);
        Debug.Log($"[SetRush] {SetFinalK}km: final boss {s.key} reserved (waits for 0 alive; alive {ActiveCount(enc)}, queue {enc.Queued})");
    }

    bool IsFinalSlot(LastDungeonBossTuning.Slot s) => s != null && setSlotClass.TryGetValue(s, out int c) && c == 3;
    bool FinalWaiting(BossEncounter enc) { foreach (var w in enc.waiting) if (IsFinalSlot(w)) return true; return IsFinalSlot(enc.telegraphing); }

    // 出してよいか(同時の上限・タグの規則・ラスボスは1対1)
    bool SetCanAdd(BossEncounter enc, LastDungeonBossTuning.Slot s, out string why)
    {
        int alive = ActiveCount(enc);
        why = "";
        if (IsFinalSlot(s)) { if (alive > 0) { why = $"final waits for 0 alive ({alive})"; return false; } return true; }
        if (enc.finalSpawned) { why = "final boss is up"; return false; }
        if (alive >= SetMaxAlive) { why = $"together {alive}/{SetMaxAlive}"; return false; }
        return true;
    }

    // 毎フレーム(LastDungeonTick → RushSpawnDue)/撃破の時
    void SetRushTick(BossEncounter enc)
    {
        if (enc == null || !enc.setRush || !IsBossPhase || NetCombat.Replica) return;
        float d = GateDistance();
        var gm = GameManager.Instance;
        // 1) 節目の予約(高速で跨いでも順番に)
        while (enc.nextK <= SetFinalK && d >= enc.nextK * gateIntervalMeters - 0.5f)
        {
            if (enc.nextK <= SetLastSetK) ReserveSet(enc, enc.nextK);
            else ReserveFinal(enc);
        }
        // 2) 98.5km: 組のボスが残っていれば足止め(ラスボスを1対1で始めるため)。全員倒してラスボスが出たら再開
        if (!enc.held98 && RunResumed && d >= SetHoldMeters - 0.5f && FinalWaiting(enc) && ActiveCount(enc) + enc.waiting.Count - 1 > 0)
        {
            enc.held98 = true; SetHolds98++;
            RunResumed = false;
            if (gm != null)
            {
                float md = gm.MaxDistance;
                if (!NetRunLauncher.IsMultiplayerRun || (md >= SetHoldMeters - 0.5f && md - SetHoldMeters < 30f)) gm.ClampMaxDistanceTo(SetHoldMeters);
                gm.BeginBossDistanceExclusion();
            }
            Debug.Log($"[SetRush] 98.5km hold: {ActiveCount(enc)} set boss(es) alive, queue {enc.Queued} - the run stops until they fall");
            BossBattleHud.Banner("残ったボスを倒せ!", new Color(1f, 0.45f, 0.35f), 2.0f);
        }
        // 3) 99km: まだ残っていれば足止め(今までと同じ)
        if (!enc.held99 && RunResumed && d >= (RushLastK + 1) * gateIntervalMeters - 0.5f && enc.Remaining > 0)
        {
            enc.held99 = true;
            RunResumed = false;
            if (gm != null)
            {
                float target = (RushLastK + 1) * gateIntervalMeters, md = gm.MaxDistance;
                if (!NetRunLauncher.IsMultiplayerRun || (md >= target - 0.5f && md - target < 30f)) gm.ClampMaxDistanceTo(target);
                gm.BeginBossDistanceExclusion();
            }
            Debug.Log($"[SetRush] 99km hold: {enc.Remaining} boss(es) remain - the run stops until all are defeated");
            BossBattleHud.Banner("残ったボスを倒せ!", new Color(1f, 0.45f, 0.35f), 2.2f);
        }
        // 4) 予告 → 出現
        if (enc.telegraphing != null)
        {
            if (Time.time < enc.telegraphEnd) return;
            if (!SetCanAdd(enc, enc.telegraphing, out _)) return;
            var s = enc.telegraphing;
            enc.telegraphing = null;
            SpawnRushBoss(enc, s);
            OnSetSpawned(enc, s);
            return;
        }
        // レベルアップの選択は「場のボスが 0体」の合間にだけ(組の最中に補充を止めない)
        if (ActiveCount(enc) == 0 && MapRushChoiceHold(enc)) return;
        if (enc.waiting.Count == 0 || Time.time - enc.lastSpawnAt < SetSpawnSpacing) return;
        for (int i = 0; i < enc.waiting.Count; i++)
        {
            var s = enc.waiting[i];
            if (!SetCanAdd(enc, s, out string why))
            {
                enc.deferredByRules++;
                if (Time.time - lastDeferLog > 2f) { lastDeferLog = Time.time; Debug.Log($"[SetRush] wait {s.key} - {why}"); }
                continue;
            }
            enc.waiting.RemoveAt(i);
            enc.telegraphing = s;
            bool fin = IsFinalSlot(s);
            float tele = fin ? 1.6f : SetTelegraphSeconds;
            enc.telegraphEnd = Time.time + tele;
            PrewarmBossAssets(s.key);
            if (fin) BossBattleHud.Banner($"FINAL BOSS  {ShortName(s.key)}", new Color(1f, 0.25f, 0.2f), tele + 0.8f);
            else BossBattleHud.Banner($"WARNING  {ShortName(s.key)}", new Color(1f, 0.35f, 0.25f), tele + 0.3f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
            RushTelegraphs++;
            break;
        }
    }

    void OnSetSpawned(BossEncounter enc, LastDungeonBossTuning.Slot s)
    {
        SetBossesSpawned++;
        if (!IsFinalSlot(s) && lastSpawnedWild != null && GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == "wasteland_road")
            lastSpawnedWild.ApplyPace(WastelandSetFreqMul, WastelandSetWaitMul, 1f);
        int alive = ActiveCount(enc);
        MaxSetAlive = Mathf.Max(MaxSetAlive, alive);
        if (!IsFinalSlot(s)) return;
        enc.finalSpawned = true;
        FinalSpawnedAt = Time.time;
        BossMusicTier = BossBgmTier.Final;
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        BossMusicKey = $"{stage}/FinalBoss";
        // 98.5km の足止めを解く(ラスボスとは走りながら)
        if (enc.held98 && !enc.held99 && !RunResumed)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.ResumeBossDistance();
            RunResumed = true; resumedAt = Time.time;
            Debug.Log("[SetRush] final boss up - the 98.5km hold is released (fight while running)");
        }
    }

    // SpawnRushBossImpl から: 組/ラスボスの強さ
    void SetSetStrength(LastDungeonBossTuning.Slot s)
    {
        int cls = setSlotClass.TryGetValue(s, out int c) ? c : 1;
        if (!ParseKey(s.key, out GateFamily f, out int kind)) return;
        var tn = BossRematchTuning.I;
        float d = s.value * gateIntervalMeters;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(f, kind)))) : 1f;
        float mul = cls >= 3 ? FinalHpMul : SetHpMul[Mathf.Clamp(cls, 0, 2)];
        string stg = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        if (cls < 3 && stg == "sky_corridor") mul *= SkySetHpMul;
        if (cls < 3 && stg == "wasteland_road") mul *= WastelandSetHpMul;
        if (cls >= 3 && stg == "wasteland_road") mul *= WastelandFinalHpMul;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul) * Mathf.Max(0.05f, mul);
        CurrentRematchTier = cls >= 3 ? "FINAL" : currentTier.label;
        DamageMul = Mathf.Max(0.1f, currentTier.damageMul) * (cls >= 3 ? 1f : stg == "sky_corridor" ? SkySetDamageMul : stg == "natural_cave" ? CaveSetDamageMul : stg == "wasteland_road" ? WastelandSetDamageMul : 1f);
        CurrentEncounterIsRematch = true;
        Debug.Log($"[SetRush] strength {s.key} C{cls + 1} at {d:F0}m: HP x{CurrentRematchHpMul:0.00} dmg x{DamageMul:0.0}");
    }

    // 2026-11-06(再測定): 組のボスの被ダメージの倍率は、ハートの数へ直接掛ける。
    //  通常マップの被ダメージは DifficultyTuning.Adjust が「攻撃の種類と距離の帯」でハートの数を決め直すので、元のダメージに掛ける DamageMul は
    //  重い一撃かどうかの判定にしか効いていなかった(洞窟 0.7 / 天空 0.6 が効かず、強デッキの人の操作で 95〜97km で倒れていた)。
    //  ラスボス(98km)には掛けない。荒野は前の測定(強デッキの人 100km)のまま 1.0
    public static float CaveSetHeartMul = 0.6f, SkySetHeartMul = 0.6f, WastelandSetHeartMul = 1f; // TEMP-APK444: 帯と一緒に調整中(0.5/0.5/0.9 案)。 // 2026-11-07: 組の間の帯(見れば避けられる)を足した分、ボスの攻撃を少し軽く(0.6/0.6/1.0 → 0.5/0.5/0.9)
    public static float SetRushHeartMul
    {
        get
        {
            var bm = Instance; var enc = bm != null ? bm.CurrentBossEncounter : null;
            if (enc == null || !enc.setRush || enc.finalSpawned) return 1f;
            var gm = GameManager.Instance; string st = gm != null ? gm.ActiveRunStageId : "";
            return st == "natural_cave" ? CaveSetHeartMul : st == "sky_corridor" ? SkySetHeartMul : st == "wasteland_road" ? WastelandSetHeartMul : 1f;
        }
    }

    // 撃破の時(OnWildBossDefeated から): 組のボスは小さな回復
    void SetOnBossDefeated(WildBossBase who)
    {
        var enc = CurrentBossEncounter;
        if (enc == null || !enc.setRush) return;
        var gm = GameManager.Instance;
        if (gm != null && SetKillHealFrac > 0f && !enc.finalSpawned)
        {
            int heal = Mathf.Max(1, Mathf.RoundToInt(gm.MaxLives * SetKillHealFrac));
            gm.KitHeal(heal);
            SetKillHeals += heal;
        }
    }

    // 96〜99km の関門は組のラッシュが代わりに受け持つ(95km は組の開始の節目)
    bool SetRushSkipsGate(int k) => SetRushStage && k > SetFirstK && k <= FinaleTrioLastK;
}

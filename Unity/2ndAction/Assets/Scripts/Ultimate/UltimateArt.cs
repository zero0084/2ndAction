using System.Collections.Generic;
using UnityEngine;

// ===== #100 ULTIMATE(2026-10-04) =====
// カード「ULTIMATE」(cardId "character_ultimate"。#79 ALMIGHTY の旧ID "ultimate" とは別物)を持っていると、
// Gauge(距離・撃破・ボスへのダメージ)が溜まり、100% で HUD 左下のボタンから発動できる。
//   CUT1 構え(少し減速・ズーム・暗転) → CUT2 一撃目(画面内の敵) → CUT3 前進(100〜200m を高速で走り抜けながら、画面に入った敵にも当てる)
//   → CUT4 締め(通常の速さへ戻る) → BUFF(しばらく攻撃/攻撃速度/勢いが少し上がる余韻)
// 移動は瞬間移動ではなく「地面に沿って高速で走る」(PlayerController.UltimateMoveTick)。距離・EXP・関門・BONUS ZONE・昼夜などは
// 通常の走行と同じ経路で進む(ReportDistance)。次のボス関門/100km/BONUS ZONE の手前で止まる。穴の上は前の地面の高さで渡る。
// ボス戦中は「ボスを通り抜けて、安全な間合いへ戻る」動き(アリーナ)。ボスへのダメージは最大HPの割合で上限を決める。
// ゲーム上のダメージは1体につき数回(mobDamageEvents)だけ。見た目の命中は UltimateFx が別に何度でも出す。
// マルチ: 現状は使えない(候補に出ない/ボタンは「マルチ未対応」)。理由は Docs/Ultimate100_2026-10-04.md。
public enum UltimatePhase { None, Startup, Burst, Dash, Arena, Finish }

public class UltimateArt : MonoBehaviour
{
    public const string CardId = "character_ultimate";
    public static UltimateArt Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("UltimateArt");
        DontDestroyOnLoad(go);
        go.AddComponent<UltimateArt>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    static UltimateTuning T => UltimateTuning.I;

    // ===================================================================== 状態
    public float Gauge { get; private set; }
    public UltimatePhase Phase { get; private set; }
    public bool Active => Phase != UltimatePhase.None;
    public static bool IsActive => Instance != null && Instance.Active;
    public string CharacterId { get; private set; } = "";
    float phaseT, totalT;
    float lastDriveRealtime;
    bool arena;

    public static int Level
    {
        get
        {
            var gm = GameManager.Instance;
            return gm == null ? 0 : Mathf.Clamp(gm.Card.Int(EffectType.UltimateLevel), 0, 9);
        }
    }
    public static bool HasCard => Level > 0;
    // マルチでは候補に出さない(現状は使えない。Docs/Ultimate100_2026-10-04.md)
    public static bool Offerable(CardDefinition c) => c == null || c.cardId != CardId || !NetMatch.Active;
    public bool Ready => HasCard && Gauge >= 100f - 1e-3f;

    // BUFF(ゲームの時間で減る: 停止/カード選択の間は減らない)
    float buffLeft, buffTotal;
    int buffLevel;
    public bool BuffActive => buffLeft > 0f;
    public float BuffRemaining => buffLeft;
    public float BuffTotal => buffTotal;
    float protectLeft;

    // 読む側(PlayerController): BUFF の倍率
    public static float BuffAttackMul => Instance != null && Instance.BuffActive ? 1f + UltimateTuning.At(T.buffAttack, Instance.buffLevel) : 1f;
    public static float BuffDurationFactor => Instance != null && Instance.BuffActive ? 1f / (1f + UltimateTuning.At(T.buffAttackSpeed, Instance.buffLevel)) : 1f;
    public static float BuffRunSpeedMul => Instance != null && Instance.BuffActive ? 1f + UltimateTuning.At(T.buffRunSpeed, Instance.buffLevel) : 1f;
    // PlayerController.Update: この間は通常の移動/入力の代わりに DriveStep で動く
    public static bool Driving => Instance != null && Instance.Active;
    // 接触/ノックバックを受けない(発動中 + 終わってから少し)。落下は発動中だけ(地面に沿って動くので起きないはずだが念のため)
    public static bool ProtectsFromHit => Instance != null && (Instance.Active || Instance.protectLeft > 0f);
    public static bool ProtectsFromFall => Instance != null && Instance.Active;
    // レベルアップ/報酬の選択を発動中は出さない(終わってから出す)
    public static bool DefersChoices => Instance != null && Instance.Active;

    // ===================================================================== 記録(テスト/DEBUG)
    public class Record
    {
        public string character; public int level; public bool arena; public string stage;
        public float d0, d1, plannedAdvance, limitedAdvance; public string limitReason = "";
        public float x0, x1; public float seconds;
        public int pulses, damageEvents, mobsHit, mobsKilled, bossesHit; public long mobDamage, bossDamage;
        public float bossFractionMax; public int visualHits;
        public bool aborted; public string abortReason = "";
        public float buffSeconds; public bool landedOnGround; public float landY, landGroundY;
        public int livesBefore, livesAfter;
    }
    public Record Last { get; private set; }
    public int Activations { get; private set; }
    public float GaugeFromDistance { get; private set; }
    public float GaugeFromKills { get; private set; }
    public float GaugeFromBoss { get; private set; }
    public string LastBlockReason { get; private set; } = "";

    // ===================================================================== ランの始まり / CONTINUE
    public void ResetRun()
    {
        if (Active) ForceEnd("reset");
        Gauge = 0f; buffLeft = 0f; protectLeft = 0f; buffTotal = 0f;
        lastDistance = -1f; distanceBudget = 0f;
        GaugeFromDistance = GaugeFromKills = GaugeFromBoss = 0f;
        UltimateFx.ClearAll();
    }
    public float ExportGauge() => Gauge;
    public void ImportGauge(float g) { Gauge = Mathf.Clamp(g, 0f, 100f); lastDistance = -1f; }

    // ===================================================================== Gauge
    float lastDistance = -1f, distanceBudget;

    float GainScale()
    {
        if (Active) return 0f;
        float s = 1f + T.gaugeLevelBonus * Mathf.Max(0, Level - 1);
        if (BuffActive) s *= T.gaugeDuringBuff;
        return s;
    }

    void AddGauge(float pct, int source)
    {
        if (!HasCard || pct <= 0f) return;
        if (NetMatch.Active) return;
        float add = pct * GainScale();
        if (add <= 0f) return;
        float before = Gauge;
        Gauge = Mathf.Min(100f, Gauge + add);
        float real = Gauge - before;
        if (source == 0) GaugeFromDistance += real; else if (source == 1) GaugeFromKills += real; else GaugeFromBoss += real;
        if (before < 100f && Gauge >= 100f) OnBecameReady();
    }

    void OnBecameReady()
    {
        readyFlash = 1f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
    }

    public static void OnEnemyKilled(bool elite) { if (Instance != null) Instance.AddGauge(elite ? T.gaugePerEliteKill : T.gaugePerKill, 1); }
    public static void OnBossKilled() { if (Instance != null) Instance.AddGauge(T.gaugePerBossKill, 2); }
    public static void OnBossDamaged(int amount, int maxHp)
    {
        if (Instance == null || maxHp <= 0 || amount <= 0) return;
        Instance.AddGauge(Mathf.Min(T.gaugeBossHitMax, T.gaugePerBossBar * amount / (float)maxHp), 2);
    }

    // 開発用: Gauge を直接
    public void DebugSetGauge(float g) { Gauge = Mathf.Clamp(g, 0f, 100f); if (Gauge >= 100f) readyFlash = 1f; }
    public void DebugEndBuff() { buffLeft = 0f; }

    void TickGauge(float dt)
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) { lastDistance = -1f; return; }
        float d = gm.MaxDistance;
        if (lastDistance < 0f || d < lastDistance - 1f) { lastDistance = d; return; }
        float delta = d - lastDistance;
        lastDistance = d;
        distanceBudget = Mathf.Min(T.gaugeDistanceMaxPerSecond, distanceBudget + T.gaugeDistanceMaxPerSecond * dt);
        if (delta <= 0f || Active) return;
        float want = delta * T.gaugePerMeter;
        float take = Mathf.Min(want, distanceBudget);
        distanceBudget -= take;
        AddGauge(take, 0);
    }

    // ===================================================================== 発動できるか
    public bool CanActivate(out string reason)
    {
        reason = "";
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        if (gm == null || pc == null) { reason = "準備中"; return false; }
        if (!HasCard) { reason = "カードなし"; return false; }
        if (NetMatch.Active) { reason = "マルチ未対応"; return false; }
        if (Active) { reason = "発動中"; return false; }
        if (Gauge < 100f - 1e-3f) { reason = $"{Mathf.FloorToInt(Gauge)}%"; return false; }
        if (!gm.HasStarted || gm.IsGameOver || gm.CountdownActive || gm.ResumeGateActive) { reason = "今は使えない"; return false; }
        if (Time.timeScale <= 0f || gm.UltimateChoiceOpen) { reason = "選択中"; return false; }
        if (gm.IsBossPresentationActivePublic) { reason = "ボス登場中"; return false; }
        if (pc.IsFinishing || pc.IsAscending || pc.IsDeadPosing || pc.NetIsDowned || pc.NetIsChoosing) { reason = "今は使えない"; return false; }
        if (pc.IsReacting) { reason = "被弾中"; return false; }
        var bz = BonusZone.Instance;
        if (bz != null && (bz.State == BonusZone.Phase.Intro || bz.State == BonusZone.Phase.Active)) { reason = "BONUS中"; return false; }
        var flow = LastDungeonFlow.Instance;
        if (flow != null && flow.Enabled && flow.Current != LastDungeonFlow.State.Idle && flow.Current != LastDungeonFlow.State.Run && flow.Current != LastDungeonFlow.State.Rush) { reason = "今は使えない"; return false; }
        var bm = BossManager.Instance;
        if (bm != null && bm.DeathSpawned && !bm.IsBossPhase) { reason = "今は使えない"; return false; }
        return true;
    }

    // ===================================================================== 発動
    public bool TryActivate(string via)
    {
        if (!CanActivate(out string why))
        {
            LastBlockReason = why;
            Debug.Log($"[ULTIMATE] blocked ({via}): {why}");
            return false;
        }
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        Gauge = 0f;
        Activations++;
        CharacterId = gm.ActiveRunCharacterIdForUltimate;
        int lv = Level;
        var r = new Record { character = CharacterId, level = lv, stage = gm.ActiveRunStageId, d0 = gm.MaxDistance, x0 = pc.transform.position.x, livesBefore = gm.Lives };
        Last = r;
        targets.Clear(); bossBudget.Clear();
        arenaStep = 0; totalT = 0f;
        arenaBoss = PickArenaBoss(pc.transform.position.x);
        arena = arenaBoss != null;
        r.arena = arena;
        // ボスが走りを止めている(関門の戦闘中)のに通り抜ける相手が見つからない時(登場前/撃破の演出中など): その場で撃つだけ(前進なし)
        heldAtStart = BossManager.Instance != null && BossManager.Instance.HoldsRun;
        // 前進の距離: Lv の値を、次のボス関門/100km/BONUS ZONE の手前までに抑える
        float plan = UltimateTuning.At(T.advance, lv);
        r.plannedAdvance = plan;
        float vAuto = Mathf.Max(4f, pc.CurrentAutoRunSpeed);
        // 構え/一撃目/締めの間も通常に近い速さで進む分。前進の合計(= 画面上で進む距離)が Lv の値になるよう、高速の区間はその分短くする
        float cutsMove = vAuto * (0.55f * T.cutStartup + 0.9f * T.cutBurst + T.cutFinish);
        string lim = "";
        float adv = arena || heldAtStart ? 0f : LimitAdvance(gm, plan, Mathf.Max(T.stopBeforeGate, cutsMove + 12f), out lim);
        if (heldAtStart && !arena) lim = "boss holds the run (in place)";
        r.limitReason = lim;
        r.limitedAdvance = adv;
        advanceTarget = Mathf.Max(0f, adv - cutsMove);
        traveled = 0f;
        float T3 = Mathf.Max(0.6f, T.dashSeconds);
        // 速さの形: 立ち上がり15% / 一定60% / 減速25%(終わりは通常の速さへ繋ぐ)。∫v = vAuto*T + (vPeak - vAuto)*T*0.8
        dashPeak = vAuto + Mathf.Max(0f, advanceTarget - vAuto * T3) / (0.8f * T3);
        dashSeconds = T3;
        if (!arena && adv > 1f)
        {
            // 着地点の足場: 穴/坂を作らない区間 + 着地後しばらく敵/障害物を出さない
            var tm = TerrainManager.Instance;
            float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
            if (tm != null && !WorldRng.IsDeterministic) tm.SetResumeFlatZone(lx + adv - 25f, lx + adv + 45f);
            gm.UltimateSetSafeUntil(gm.MaxDistance + adv + T.landingSafeMeters);
        }
        else gm.UltimateSetSafeUntil(gm.MaxDistance + 10f);
        pc.UltimateBegin();
        SetPhase(UltimatePhase.Startup);
        lastDriveRealtime = Time.realtimeSinceStartup;
        UltimateFx.Begin(CharacterId, pc);
        Debug.Log($"[ULTIMATE] ACTIVATE ({via}) char={CharacterId} Lv{lv} d={r.d0:F0} {(arena ? "ARENA vs " + arenaBoss.bossName : $"advance {adv:F0}m (plan {plan:F0}{(string.IsNullOrEmpty(r.limitReason) ? "" : ", " + r.limitReason)})")} vAuto={vAuto:F1} peak={dashPeak:F1}m/s");
        return true;
    }

    // gateMargin: 関門の何m手前で止まるか(締めの間も進むので、速いほど手前)
    float LimitAdvance(GameManager gm, float plan, float gateMargin, out string reason)
    {
        reason = "";
        float d = gm.MaxDistance;
        float limit = d + plan;
        var bm = BossManager.Instance;
        if (bm != null && bm.enabled && !BossManager.SuppressGates && !bm.IsBossPhase)
        {
            float nb = bm.NextBossDistance;
            if (nb > d && nb - gateMargin < limit) { limit = nb - gateMargin; reason = $"boss gate {nb:F0}m"; }
        }
        if (bm != null && !bm.DeathSpawned)
        {
            float death = bm.deathSpawnDistance;
            if (death > d && death - 60f < limit) { limit = death - 60f; reason = $"100km {death:F0}m"; }
        }
        float bonus = BonusZone.Instance != null ? BonusZone.Instance.PendingStartDistance : -1f;
        if (bonus > d && bonus - 10f < limit) { limit = bonus - 10f; reason = $"BONUS ZONE {bonus:F0}m"; }
        return Mathf.Max(0f, limit - d);
    }

    // ===================================================================== 毎フレーム(PlayerController.Update から)
    float advanceTarget, traveled, dashPeak, dashSeconds, nextPulseT;
    WildBossBase arenaBoss;
    int arenaStep;
    bool heldAtStart;

    void SetPhase(UltimatePhase p)
    {
        Phase = p; phaseT = 0f;
        if (p == UltimatePhase.Burst) { Pulse(false); UltimateFx.Burst(CharacterId); }
        if (p == UltimatePhase.Dash || p == UltimatePhase.Arena) { nextPulseT = T.pulseInterval; UltimateFx.DashStart(CharacterId, arena); }
        if (p == UltimatePhase.Finish) { Pulse(true); UltimateFx.Finish(CharacterId); }
    }

    // その時の移動量(m、+が前)を返す。地面に沿わせるのは PlayerController 側
    public float DriveStep(float dt)
    {
        lastDriveRealtime = Time.realtimeSinceStartup;
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        if (pc == null || gm == null) { ForceEnd("no player"); return 0f; }
        phaseT += dt; totalT += dt;
        float vAuto = pc.CurrentAutoRunSpeed;
        float dx = 0f;
        string abort = AbortReason(gm);
        switch (Phase)
        {
            case UltimatePhase.Startup:
                dx = vAuto * 0.55f * dt;
                UltimateFx.Tick(Phase, phaseT, dt);
                if (phaseT >= T.cutStartup) SetPhase(UltimatePhase.Burst);
                break;
            case UltimatePhase.Burst:
                dx = vAuto * 0.9f * dt;
                UltimateFx.Tick(Phase, phaseT, dt);
                if (phaseT >= T.cutBurst) SetPhase(arena ? UltimatePhase.Arena : UltimatePhase.Dash);
                break;
            case UltimatePhase.Dash:
            {
                float u = phaseT / dashSeconds;
                float bell = u < 0.15f ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u / 0.15f) : u < 0.75f ? 1f : u < 1f ? 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (u - 0.75f) / 0.25f) : 0f;
                float v = vAuto + (dashPeak - vAuto) * bell;
                dx = v * dt;
                if (traveled + dx > advanceTarget) dx = Mathf.Max(0f, advanceTarget - traveled);
                traveled += dx;
                CameraFollow.UltimateLookAhead = Mathf.Lerp(CameraFollow.UltimateLookAhead, 4f * bell, 1f - Mathf.Exp(-10f * dt));
                UltimateFx.Tick(Phase, phaseT, dt);
                PulseTimer();
                if (abort != null) { Abort(abort); break; }
                if (traveled >= advanceTarget - 0.01f || phaseT > dashSeconds + 1.5f) SetPhase(UltimatePhase.Finish);
                break;
            }
            case UltimatePhase.Arena:
                dx = ArenaStep(pc, vAuto, dt);
                UltimateFx.Tick(Phase, phaseT, dt);
                PulseTimer();
                if (abort != null && abort != "boss phase") { Abort(abort); break; }
                if (arenaStep >= 2 || phaseT > 2.4f) SetPhase(UltimatePhase.Finish);
                break;
            case UltimatePhase.Finish:
                dx = vAuto * dt;
                CameraFollow.UltimateLookAhead = Mathf.Lerp(CameraFollow.UltimateLookAhead, 0f, 1f - Mathf.Exp(-6f * dt));
                UltimateFx.Tick(Phase, phaseT, dt);
                if (phaseT >= T.cutFinish)
                {
                    if (pc.HasGroundUnderForUltimate()) End();
                    else if (phaseT > T.cutFinish + 4f) { pc.UltimateRescueToGround(); End(); }
                }
                break;
        }
        return dx;
    }

    string AbortReason(GameManager gm)
    {
        if (gm.IsGameOver) return "game over";
        var bz = BonusZone.Instance;
        if (Phase == UltimatePhase.Dash && bz != null && (bz.State == BonusZone.Phase.Intro || bz.State == BonusZone.Phase.Active)) return "bonus zone";
        var bm = BossManager.Instance;
        if (bm != null && bm.HoldsRun && !arena && !heldAtStart) return "boss phase";
        var flow = LastDungeonFlow.Instance;
        if (flow != null && flow.Enabled && flow.Current != LastDungeonFlow.State.Idle && flow.Current != LastDungeonFlow.State.Run && flow.Current != LastDungeonFlow.State.Rush) return "last dungeon " + flow.Current;
        return null;
    }

    void Abort(string why)
    {
        if (Last != null) { Last.aborted = true; Last.abortReason = why; }
        Debug.Log($"[ULTIMATE] cut short: {why} (traveled {traveled:F0}/{advanceTarget:F0}m)");
        SetPhase(UltimatePhase.Finish);
    }

    void PulseTimer()
    {
        if (phaseT >= nextPulseT) { nextPulseT += T.pulseInterval; Pulse(false); }
    }

    // ---- ボス戦: ボスを通り抜けて(ボスの奥 arenaPassBeyond m)、ボスの手前 arenaReturnGap m へ戻る
    public static bool ArenaFreeGap => Instance != null && (Instance.Phase == UltimatePhase.Arena || (Instance.Phase == UltimatePhase.Finish && Instance.arena));

    WildBossBase PickArenaBoss(float px)
    {
        var bm = BossManager.Instance;
        if (bm == null || !bm.IsBossPhase) return null;
        WildBossBase best = null; float bd = float.MaxValue;
        Rect view = ViewRect(1.15f);
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
        {
            if (b == null || b.IsDead || !b.isActiveAndEnabled) continue;
            float bx = b.transform.position.x;
            if (bx < view.xMin || bx > view.xMax + 30f) continue; // 画面の中、または前から走り込んでくる途中
            float d = Mathf.Abs(bx - px);
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    float ArenaStep(PlayerController pc, float vAuto, float dt)
    {
        float px = pc.transform.position.x;
        if (arenaBoss == null || arenaBoss.IsDead || !arenaBoss.isActiveAndEnabled) { arenaStep = 2; return vAuto * dt; }
        float bx = arenaBoss.transform.position.x, hw = Mathf.Max(0.5f, arenaBoss.HalfWidth);
        float target = arenaStep == 0 ? bx + hw + T.arenaPassBeyond : bx - hw - T.arenaReturnGap;
        float speed = arenaStep == 0 ? 70f : 45f;
        float frame = vAuto * dt; // ボスも基本の速さで進むので、その分を足す
        float want = target - px;
        float step = Mathf.Clamp(want, -speed * dt, speed * dt);
        if (Mathf.Abs(want) < 0.6f || (arenaStep == 0 && phaseT > 1.1f)) { arenaStep++; if (arenaStep == 1) { Pulse(false); UltimateFx.ArenaPass(CharacterId); } }
        CameraFollow.UltimateLookAhead = Mathf.Lerp(CameraFollow.UltimateLookAhead, arenaStep == 0 ? 2.5f : 0f, 1f - Mathf.Exp(-8f * dt));
        return frame + step;
    }

    // ===================================================================== 当たり判定(画面内の敵)
    class Hit { public int events; public float lastT; public long dealt; }
    readonly Dictionary<Component, Hit> targets = new Dictionary<Component, Hit>();
    readonly Dictionary<Component, (long budget, long dealt, int events, int hp0)> bossBudget = new Dictionary<Component, (long, long, int, int)>();
    readonly List<Component> buf = new List<Component>();

    public static float BuildFactor
    {
        get
        {
            var pc = PlayerController.Instance;
            float a = pc != null ? pc.CardAttackFactor - 1f : 0f;
            return Mathf.Clamp(1f + T.buildAttackShare * Mathf.Max(0f, a), 1f, T.buildFactorMax);
        }
    }

    // 通常の敵への合計ダメージ(その距離の HP倍率1 の雑魚 × Lv の倍率 × カードの攻撃力の一部)
    public static int MobDamageTotal(int lv)
    {
        var dtm = DistanceTierManager.Instance;
        int baseHp = dtm != null ? dtm.EnemyHpFor(1f) : 6 * CombatScale.K;
        return Mathf.Max(1, Mathf.RoundToInt(baseHp * UltimateTuning.At(T.mobDamage, lv) * BuildFactor));
    }

    public static Rect ViewRect(float margin = 1.08f)
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return new Rect(-20f, -12f, 40f, 24f);
        float h = cam.orthographicSize * 2f * margin, w = h * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
    }

    void Pulse(bool final)
    {
        var r = Last;
        if (r != null) r.pulses++;
        int lv = Mathf.Max(1, r != null ? r.level : Level);
        Rect v = ViewRect();
        buf.Clear();
        CardProcs.CollectTargets(v.center, v.size, buf);
        int total = MobDamageTotal(lv);
        int events = Mathf.Max(1, T.mobDamageEvents);
        foreach (var c in buf)
        {
            if (c == null || !ElementSystem.IsAlive(c)) continue;
            Vector3 at = CardProcs.CenterOf(c);
            if (c is WildBossBase || c is DragonController || c is MajinController) { BossPulse(c, lv, final, at); continue; }
            if (!targets.TryGetValue(c, out var h)) { h = new Hit { lastT = -9f }; targets[c] = h; if (r != null) r.mobsHit++; }
            if (h.events >= events || totalT - h.lastT < 0.25f) continue;
            int amount = h.events == events - 1 ? (int)Mathf.Max(1, total - h.dealt)
                : h.events == 0 ? Mathf.Max(1, Mathf.CeilToInt(total * (events == 1 ? 1f : Mathf.Clamp01(T.mobFirstShare))))
                : Mathf.Max(1, Mathf.CeilToInt((total - h.dealt) / (float)(events - h.events)));
            bool dealt = false, killed = false;
            switch (c)
            {
                case EnemyController en:
                    int hp0 = en.CurrentHpForUltimate;
                    dealt = en.ApplyElementDamage(amount, at);
                    killed = dealt && hp0 <= amount;
                    break;
            }
            if (!dealt) continue;
            h.events++; h.lastT = totalT; h.dealt += amount;
            if (r != null) { r.damageEvents++; r.mobDamage += amount; if (killed) r.mobsKilled++; }
            UltimateFx.HitVisual(CharacterId, at, killed);
            if (r != null) r.visualHits += 3;
        }
    }

    // ボス(荒野/洞窟/天空のボス・三姉妹 = WildBossBase、旧ドラゴン/魔人): 最大HPの割合で上限(1回で戦闘を終わらせない)
    static int BossHp(Component c) => c is WildBossBase w ? w.Hp : c is DragonController d ? d.Hp : c is MajinController m ? m.Hp : 0;
    static int BossMaxHp(Component c) => c is WildBossBase w ? w.maxHp : c is DragonController d ? d.maxHp : c is MajinController m ? m.maxHp : 1;
    void BossPulse(Component c, int lv, bool final, Vector3 at)
    {
        var r = Last;
        int maxHp = Mathf.Max(1, BossMaxHp(c));
        if (!bossBudget.TryGetValue(c, out var b))
        {
            float frac = Mathf.Min(T.bossDamageCap, UltimateTuning.At(T.bossDamageFraction, lv) * Mathf.Sqrt(BuildFactor));
            b = (Mathf.Max(1, Mathf.RoundToInt(maxHp * frac)), 0, 0, BossHp(c));
            if (r != null) r.bossesHit++;
        }
        const int BossEvents = 4;
        if (b.events >= BossEvents || b.dealt >= b.budget) { bossBudget[c] = b; return; }
        bool loud = final || b.events == BossEvents - 1;
        // 締め(または最後の回)は残りを全部(当たった回数が少なくても、Lv の割合どおりに入る)
        int amount = (int)Mathf.Max(1, loud ? b.budget - b.dealt : Mathf.Min(b.budget - b.dealt, Mathf.CeilToInt(b.budget / (float)BossEvents)));
        int before = BossHp(c);
        switch (c)
        {
            case WildBossBase wb: if (loud) wb.TakeDamage(amount, at); else wb.TakeElementDamage(amount, at); break;
            case DragonController d: d.TakeDamage(amount); break;
            case MajinController m: m.TakeDamage(amount); break;
        }
        int lost = Mathf.Max(0, before - BossHp(c));
        b.dealt += Mathf.Max(lost, amount); b.events++;
        bossBudget[c] = b;
        if (r != null)
        {
            r.damageEvents++; r.bossDamage += lost;
            r.bossFractionMax = Mathf.Max(r.bossFractionMax, (b.hp0 - BossHp(c)) / (float)maxHp);
        }
        UltimateFx.HitVisual(CharacterId, at, false, boss: true);
    }

    // ===================================================================== 終わり
    void End()
    {
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        int lv = Last != null ? Mathf.Max(1, Last.level) : Mathf.Max(1, Level);
        buffLevel = lv;
        buffTotal = buffLeft = UltimateTuning.At(T.buffSeconds, lv);
        protectLeft = T.afterProtectSeconds;
        if (Last != null && pc != null && gm != null)
        {
            Last.d1 = gm.MaxDistance; Last.x1 = pc.transform.position.x; Last.seconds = totalT; Last.buffSeconds = buffTotal;
            float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(pc.transform.position.x) : null;
            Last.landedOnGround = g.HasValue; Last.landGroundY = g ?? float.NaN; Last.landY = pc.transform.position.y;
            Last.livesAfter = gm.Lives;
        }
        Phase = UltimatePhase.None; totalT = 0f;
        if (pc != null) pc.UltimateEnd();
        CameraFollow.UltimateLookAhead = 0f; CameraFollow.UltimateZoom = 1f;
        UltimateFx.End(CharacterId);
        UltimateFx.BuffStart(CharacterId);
        if (Last != null) Debug.Log($"[ULTIMATE] END char={Last.character} Lv{Last.level} {(Last.arena ? "arena" : $"advanced {Last.d1 - Last.d0:F0}m ({Last.d0:F0}->{Last.d1:F0})")} pulses={Last.pulses} events={Last.damageEvents} mobs hit/killed={Last.mobsHit}/{Last.mobsKilled} bosses={Last.bossesHit} bossDmg={Last.bossDamage} ({Last.bossFractionMax * 100f:F0}%) t={Last.seconds:F2}s buff={buffTotal:F1}s{(Last.aborted ? " CUT SHORT: " + Last.abortReason : "")}");
    }

    // 異常時(死亡/シーン切り替え/ランのリセット): 守りや見た目を残さずに終える(BUFF は付けない)
    public void ForceEnd(string why)
    {
        if (!Active) return;
        Debug.Log($"[ULTIMATE] force end: {why}");
        if (Last != null) { Last.aborted = true; Last.abortReason = why; }
        Phase = UltimatePhase.None; totalT = 0f;
        var pc = PlayerController.Instance;
        if (pc != null) pc.UltimateEnd();
        CameraFollow.UltimateLookAhead = 0f; CameraFollow.UltimateZoom = 1f;
        UltimateFx.ClearAll();
    }

    // ===================================================================== Update(Gauge / BUFF / 見張り / ボタン)
    float readyFlash;
    void Update()
    {
        float dt = Time.deltaTime;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted)
        {
            if (Active) ForceEnd("not running");
            buffLeft = 0f; protectLeft = 0f;
            return;
        }
        TickGauge(dt);
        if (Active)
        {
            // PlayerController が動かしていない(停止中は除く)まま時間が経ったら、安全に終える
            if (Time.timeScale > 0f && Time.realtimeSinceStartup - lastDriveRealtime > 1.0f) ForceEnd("player not driving");
            if (gm.IsGameOver) ForceEnd("game over");
        }
        else
        {
            if (buffLeft > 0f) { buffLeft = Mathf.Max(0f, buffLeft - dt); UltimateFx.BuffTick(CharacterId, dt, buffLeft / Mathf.Max(0.01f, buffTotal)); if (buffLeft <= 0f) UltimateFx.BuffEnd(CharacterId); }
            if (protectLeft > 0f) protectLeft = Mathf.Max(0f, protectLeft - dt);
        }
        if (readyFlash > 0f) readyFlash = Mathf.Max(0f, readyFlash - Time.unscaledDeltaTime * 1.5f);
        UpdateButtonInput();
    }

    // ===================================================================== HUD ボタン(左下)
    // 画面座標(IMGUI: 左上が原点)。左上 BEST/DISTANCE/SPEED・上中央 Lv/EXP・右上 HP/カード・右下 II と重ならない左下。
    public static Rect ButtonRect()
    {
        float size = Mathf.Clamp(Screen.height * 0.12f, 84f, 160f);
        float margin = 28f;
        float x = Screen.safeArea.x + margin;
        float bottom = Screen.safeArea.y + margin + (DebugRun.IsActive ? Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f) * 36f : 0f);
        return new Rect(x, Screen.height - bottom - size, size, size);
    }
    // ボタンの周り(少し広め)で始まったタッチは、攻撃/ジャンプのフリックにしない
    public static bool BlocksPointer(Vector2 screenPosYUp)
    {
        if (Instance == null || !HasCard || !Instance.ButtonVisible) return false;
        Rect r = ButtonRect();
        r.xMin -= 10f; r.yMin -= 10f; r.xMax += 10f; r.yMax += 10f;
        return r.Contains(new Vector2(screenPosYUp.x, Screen.height - screenPosYUp.y));
    }
    public bool ButtonVisible
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && HasCard && gm.UltimateHudVisible;
        }
    }

    bool pressing; int pressFinger = -1; Vector2 pressStart;
    public int ButtonActivations { get; private set; }

    void UpdateButtonInput()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.U)) TryActivate("key U");
#endif
        if (!ButtonVisible) { pressing = false; return; }
        Rect r = ButtonRect();
        // タッチ(どの指でも)
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            Vector2 gp = new Vector2(t.position.x, Screen.height - t.position.y);
            if (t.phase == TouchPhase.Began && !pressing && r.Contains(gp)) { pressing = true; pressFinger = t.fingerId; pressStart = t.position; }
            else if (pressing && t.fingerId == pressFinger)
            {
                if ((t.position - pressStart).magnitude > 40f) pressing = false; // フリック/ドラッグなら取り消し
                else if (t.phase == TouchPhase.Ended) { pressing = false; if (r.Contains(gp)) ButtonPressed(); }
                else if (t.phase == TouchPhase.Canceled) pressing = false;
            }
        }
        if (Input.touchCount == 0)
        {
            Vector2 mp = Input.mousePosition;
            Vector2 gp = new Vector2(mp.x, Screen.height - mp.y);
            if (Input.GetMouseButtonDown(0) && r.Contains(gp)) { pressing = true; pressFinger = -2; pressStart = mp; }
            else if (pressing && pressFinger == -2)
            {
                if ((mp - pressStart).magnitude > 40f) pressing = false;
                else if (Input.GetMouseButtonUp(0)) { pressing = false; if (r.Contains(gp)) ButtonPressed(); }
            }
        }
    }

    void ButtonPressed()
    {
        if (TryActivate("button")) ButtonActivations++;
        else if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
    }

    static GUIStyle labelStyle;
    static Texture2D ringTex, discTex;
    void OnGUI()
    {
        if (!ButtonVisible) return;
        GUI.depth = -40;
        UltimateFxLabel.WarmIfNeeded();
        Rect r = ButtonRect();
        bool can = CanActivate(out string why);
        bool ready = Ready;
        if (discTex == null) discTex = UltimateFx.HudDisc(128, false);
        if (ringTex == null) ringTex = UltimateFx.HudDisc(128, true);
        var keep = GUI.color;
        float pulse = ready ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
        Color theme = UltimateFx.ThemeColor(GameManager.Instance != null ? GameManager.Instance.ActiveRunCharacterIdForUltimate : "");
        // 外側の光(溜まった時)
        if (ready)
        {
            float g = 1.18f + 0.08f * pulse + readyFlash * 0.4f;
            Rect gr = new Rect(r.center.x - r.width * g * 0.5f, r.center.y - r.height * g * 0.5f, r.width * g, r.height * g);
            GUI.color = new Color(theme.r, theme.g, theme.b, 0.35f + 0.3f * pulse);
            GUI.DrawTexture(gr, discTex);
        }
        GUI.color = new Color(0.06f, 0.07f, 0.13f, 0.82f);
        GUI.DrawTexture(r, discTex);
        // Gauge: 下から溜まる円
        float f = Mathf.Clamp01(Gauge / 100f);
        if (f > 0f)
        {
            Rect clip = new Rect(r.x, r.yMax - r.height * f, r.width, r.height * f);
            GUI.BeginGroup(clip);
            GUI.color = ready ? new Color(theme.r, theme.g, theme.b, 0.85f) : new Color(theme.r * 0.6f, theme.g * 0.6f, theme.b * 0.6f, 0.7f);
            GUI.DrawTexture(new Rect(0f, -(r.height - clip.height), r.width, r.height), discTex);
            GUI.EndGroup();
        }
        GUI.color = ready ? new Color(1f, 0.85f, 0.35f, 1f) : new Color(0.85f, 0.75f, 0.45f, 0.9f);
        GUI.DrawTexture(r, ringTex);
        GUI.color = keep;
        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        labelStyle.fontSize = Mathf.RoundToInt(r.height * 0.15f);
        labelStyle.normal.textColor = ready ? Color.white : new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(r.x - 10f, r.y + r.height * 0.22f, r.width + 20f, r.height * 0.3f), "ULTIMATE", labelStyle);
        labelStyle.fontSize = Mathf.RoundToInt(r.height * (ready ? 0.2f : 0.22f));
        string mid = Active ? "!!" : ready ? (can ? "READY" : why) : (NetMatch.Active ? "MULTI×" : $"{Mathf.FloorToInt(Gauge)}%");
        labelStyle.normal.textColor = ready ? new Color(1f, 0.92f, 0.5f) : Color.white;
        GUI.Label(new Rect(r.x - 10f, r.y + r.height * 0.48f, r.width + 20f, r.height * 0.32f), mid, labelStyle);
        // BUFF の残り(ボタンの上)
        if (BuffActive)
        {
            labelStyle.fontSize = Mathf.RoundToInt(r.height * 0.14f);
            labelStyle.normal.textColor = theme;
            GUI.Label(new Rect(r.x - 20f, r.y - r.height * 0.24f, r.width + 40f, r.height * 0.22f), $"BUFF {buffLeft:0.0}s", labelStyle);
        }
    }
}

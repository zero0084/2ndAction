using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 判断型の道中イベント(2026-11-05、マスターの依頼)。調整値は DecisionEventTuning。
//  目的: オートは補助のまま活かし、50km 以降は「上か下か、避けるか倒すか、打ち上げるか」の一瞬の判断をこまめに求める。
//        完全に放置するとダメージが少しずつ積み重なり、人が見て少し操作すれば簡単に抜けられる。1回のミスで倒れない(1〜3ハート)。
//  種類(RoadEventKind に追加。今までの道中イベントと同じ管理 = RoadEvents.Active で1つずつ):
//   ・上空封鎖 SkyBlock      飛ぶ敵を上に並べる。地面は空ける(赤い帯は上半分 = 下を走る)
//   ・地上封鎖 GroundBlock   重い敵を地面に並べる(地上の攻撃で下がらない)。空は空ける(赤い帯は地面 = 跳ぶ)
//   ・上下交互 Alternate     地上の帯 → 空の帯 → 地上の帯(画面に固定した帯を敵の群れが横切る。速さに関係なく同じ秒数)
//   ・予兆付き突進群 TelegraphRush  帯の予兆(0.8〜1.5秒)→ 群れが帯を横切る(地上の帯 = 跳ぶ / 空の帯 = 地上にいる)
//   ・高火力敵 HeavyHitter    赤紫のオーラ。ダメージ +1 ハート(HP は普通 = 倒せばすぐ消える)。通常の Encounter にも少し混ぜる
//   ・エリアルで退かす敵 AerialBrute  鋼の鎧 + 上向きの印。地上の攻撃は効かず下がらない / 打ち上げ → 空中2回で大きく吹き飛ぶ
//   ・今までの敵ラッシュ/飛行編隊/強敵/追跡/空中ボスの乱入も、50km からはここで選ぶ(RoadEvents の 5km の区間の予定は 50km まで)
//  発生: 走行時間で候補(帯ごとの gapSeconds、距離の下限あり)→ 確率 → 種類を重みで選ぶ(同じ種類の連続なし)。終わったら必ず休み。
//        ボス戦/BONUS ZONE/門/カウントダウン/関門の手前/ボスの直後は始めない(待つ)。途中でボス戦が始まったら帯と群れは消す。
//  見た目: 敵は今のマップの距離の帯の敵(荒野 = 騎兵/大型獣/飛竜、洞窟 = アリ/兵隊アリ/コウモリ、天空 = 神殿の守護者/ハーピー)。
//          帯の縁と矢印・砂ぼこり/結晶のかけら/風の筋はマップの色。説明文は出さない(赤い帯 = 危ない所)。
//  マルチ: 今は対象外(ソロ/CPU サポートだけ)。出現の判断は HOST が持てるよう、ロジックはこのクラスにまとめてある。
public class DecisionEvents : MonoBehaviour
{
    public static DecisionEvents Instance { get; private set; }
    static DecisionEventTuning T => DecisionEventTuning.I;

    // ---- 確認用(QA) ----
    public static int Started;
    public static readonly Dictionary<RoadEventKind, int> StartedByKind = new Dictionary<RoadEventKind, int>();
    public static readonly List<string> History = new List<string>(); // 「km\t種類\t詳細」
    public static int Postponed, Rolled, RolledOff, Combos, SweepHits, SweepKills, AbortedByBoss;
    public static string LastBlockReason = "";
    public static RoadEventKind Running { get; private set; } = RoadEventKind.None;
    // 今の予兆(QA の「人の操作」が読む): 地上の帯 = 跳ぶ / 空の帯・上空封鎖 = 地上にいる / 地上封鎖 = 跳ぶ / 打ち上げ
    public enum Cue { None, JumpOver, StayLow, Launch }
    public static Cue CurrentCue { get; private set; }
    public static float CueUntil { get; private set; }      // その予兆が有効な時刻(Time.time)
    public static float CueDangerAt { get; private set; }   // 危険がプレイヤーの位置へ来る時刻(Time.time)
    public static float CueDangerEnd { get; private set; }
    public static Vector2 CueRegionLogical { get; private set; } // 地上封鎖/エリアル: 区間(論理 X)
    public static event System.Action<RoadEventKind, string> OnStarted;

    float nextCandidateAt = -1f, restUntil, lastBossSeen = -99f, lastEventD = -99999f;
    RoadEventKind lastKind = RoadEventKind.None;
    bool legacyWas;
    int eventToken;
    bool boundStarted; GameManager boundGm;
    System.Random rng = new System.Random();
    readonly List<GameObject> transient = new List<GameObject>(); // 帯/群れ(中止の時に消す)
    // EncounterDirector が普通の敵を置かない区間(論理 X)。障害物も置かない(EncounterDirector の span へも足す)
    readonly List<Vector2> reserved = new List<Vector2>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[DecisionEvents]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DecisionEvents>();
        EncounterDirector.Spawned += Instance.OnDirectorSpawned;
    }

    public static bool Usable
    {
        get
        {
            if (!DecisionEventTuning.On || !RoadEventTuning.On || !DifficultyTuning.Active) return false;
            if (NetRunLauncher.IsMultiplayerRun) return false; // マルチは今は対象外
            return true;
        }
    }
    public static bool InRange(float d) => d >= T.fromKm * 1000f && d < T.untilKm * 1000f;
    // RoadEvents の 5km の区間の予定はこの距離まで(この先はここで選ぶ)
    public static float LegacyUntilMeters => Usable ? T.fromKm * 1000f : float.PositiveInfinity;

    void ResetRun()
    {
        StopAllCoroutines();
        ClearTransient();
        reserved.Clear();
        nextCandidateAt = -1f; restUntil = 0f; lastBossSeen = -99f; lastEventD = -99999f; lastKind = RoadEventKind.None;
        Running = RoadEventKind.None; CurrentCue = Cue.None; eventToken++;
        rng = new System.Random(System.Environment.TickCount ^ 0x5eed);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm != boundGm) { boundGm = gm; boundStarted = false; }
        if (gm == null || !gm.HasStarted) { if (boundStarted) { boundStarted = false; ResetRun(); } return; }
        if (!boundStarted) { boundStarted = true; ResetRun(); }
        if (gm.IsGameOver) { if (Running != RoadEventKind.None) Abort("game over"); return; }
        var bm = BossManager.Instance;
        bool boss = bm != null && bm.IsBossPhase && !bm.AirRaidActive;
        if (boss) lastBossSeen = Time.time;
        if (Running != RoadEventKind.None && Running != RoadEventKind.AirRaid && (boss || (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle)))
        { AbortedByBoss++; Abort(boss ? "boss gate" : "bonus zone"); }
        if (CurrentCue != Cue.None && Time.time > CueUntil) CurrentCue = Cue.None;
        PruneReserved();
        if (!Usable) return;
        float d = gm.MaxDistance;
        if (!InRange(d)) return;
        var band = T.BandAt(d / 1000f);
        var pc = PlayerController.Instance;
        float speed = pc != null ? Mathf.Max(5f, pc.CurrentAutoRunSpeed) : 25f;
        if (nextCandidateAt < 0f) { nextCandidateAt = Time.time + Range(band.gapSeconds) * 0.5f; return; } // 50km に入ってすぐは少し待つ
        // 今までの道中イベント(乱入/ラッシュ等)が終わった時: 最低 afterLegacySeconds は空ける
        bool legacyNow = RoadEvents.Active != RoadEventKind.None && Running == RoadEventKind.None;
        if (legacyWas && !legacyNow) { restUntil = Mathf.Max(restUntil, Time.time + Mathf.Max(T.afterLegacySeconds, band.restAfterSeconds)); nextCandidateAt = Mathf.Max(nextCandidateAt, restUntil); }
        legacyWas = legacyNow;
        if (Running != RoadEventKind.None || RoadEvents.Active != RoadEventKind.None) return;
        if (Time.time < restUntil || Time.time < nextCandidateAt) return;
        if (d - lastEventD < T.minGapMeters) return;
        if (Blocked(gm, bm, d, speed, out string why))
        {
            if (LastBlockReason != why) LastBlockReason = why;
            Postponed++; nextCandidateAt = Time.time + 1.5f; return;
        }
        Rolled++;
        if (R01() >= band.chance) { RolledOff++; nextCandidateAt = Time.time + Range(band.gapSeconds) * 0.5f; return; }
        var kind = PickKind(band, d);
        if (kind == RoadEventKind.None) { nextCandidateAt = Time.time + 3f; return; }
        StartKind(kind, band, d, R01() < band.comboChance);
    }

    bool Blocked(GameManager gm, BossManager bm, float d, float speed, out string why)
    {
        why = "";
        if (bm != null && bm.IsBossPhase) { why = "boss"; return true; }
        if (Time.time - lastBossSeen < T.afterBossSeconds) { why = "after boss"; return true; }
        if (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle) { why = "bonus zone"; return true; }
        if (LandmarkJourney.GateVisible) { why = "gate"; return true; }
        if (gm.CountdownActive || gm.ResumeGateActive) { why = "countdown"; return true; }
        if (gm.IsRewardSequenceRunning) { why = "card choice"; return true; }
        if (bm != null)
        {
            float gap = bm.NextBossDistance - d;
            if (gap > 0f && gap < T.gateClearMeters + speed * T.gateClearSeconds) { why = $"gate in {gap:F0}m"; return true; }
        }
        if (UltimateArt.Driving) { why = "ultimate"; return true; }
        return false;
    }

    // ---- 種類の選択 ----
    RoadEventKind PickKind(DecisionEventTuning.Band b, float d)
    {
        var gm = GameManager.Instance;
        string stage = gm != null ? gm.ActiveRunStageId : "";
        bool sky = stage == "sky_corridor", cave = stage == "natural_cave";
        bool approach = Mathf.Repeat(d, 10000f) >= 5000f; // 章の後半: 目的地(城/結晶/神殿)へ近づく = 地上の敵が増える
        float g = approach ? T.approachGroundMul : 1f;
        bool flyers = FindDef(d, IsFlyer) != null, heavy = FindDef(d, IsHeavyGround) != null, ground = FindDef(d, IsGroundNormal) != null;
        var w = new List<(RoadEventKind k, float w)>
        {
            (RoadEventKind.SkyBlock, flyers ? b.skyBlock * (sky ? T.skyStageAirMul : 1f) : 0f),
            (RoadEventKind.GroundBlock, heavy ? b.groundBlock * g * (cave ? T.caveGroundMul : 1f) : 0f),
            (RoadEventKind.Alternate, ground && flyers ? b.alternate : 0f),
            (RoadEventKind.TelegraphRush, ground ? b.telegraphRush * (approach ? Mathf.Sqrt(g) : 1f) : 0f),
            (RoadEventKind.HeavyHitter, ground ? b.heavyHitter : 0f),
            (RoadEventKind.AerialBrute, heavy ? b.aerialBrute * Mathf.Sqrt(g) : 0f),
            (RoadEventKind.Rush, b.legacyRush * (approach ? 1.3f : 1f)),
            (RoadEventKind.Flying, flyers ? b.legacyFlying : 0f),
            (RoadEventKind.Elite, b.legacyElite),
            (RoadEventKind.Chase, b.legacyChase),
            (RoadEventKind.AirRaid, RoadEvents.RaidPossibleNow(d) ? b.airRaid * (sky ? 1.3f : 1f) : 0f),
        };
        float sum = 0f;
        foreach (var x in w) if (x.k != lastKind) sum += x.w; // 同じ種類の連続なし
        if (sum <= 0f) return RoadEventKind.None;
        float r = R01() * sum;
        foreach (var x in w)
        {
            if (x.k == lastKind) continue;
            if (r < x.w) return x.k;
            r -= x.w;
        }
        return RoadEventKind.None;
    }

    void StartKind(RoadEventKind kind, DecisionEventTuning.Band b, float d, bool combo)
    {
        lastKind = kind; lastEventD = d;
        Started++; StartedByKind[kind] = (StartedByKind.TryGetValue(kind, out int c) ? c : 0) + 1;
        if (kind == RoadEventKind.Rush || kind == RoadEventKind.Flying || kind == RoadEventKind.Elite || kind == RoadEventKind.Chase || kind == RoadEventKind.AirRaid)
        {
            // 今までの道中イベント(RoadEvents が持つ。終わりは RoadEvents.Active が None に戻った時)
            bool ok = RoadEvents.StartLegacy(kind, d);
            Log(kind, d, ok ? "legacy " + RoadEvents.ActiveLabel : "legacy not started");
            if (ok && OnStarted != null) OnStarted(kind, "legacy");
            FinishLater(b, ok ? 0f : 3f);
            return;
        }
        int token = ++eventToken;
        Running = kind;
        RoadEvents.MarkExternal(kind, kind.ToString().ToUpperInvariant(), T.maxEventSeconds);
        IEnumerator main = kind switch
        {
            RoadEventKind.SkyBlock => SkyBlock(token, b, d, false),
            RoadEventKind.GroundBlock => GroundBlock(token, b, d),
            RoadEventKind.Alternate => Alternate(token, b),
            RoadEventKind.TelegraphRush => LaneSweep(token, b, PickLane(b), 1),
            RoadEventKind.HeavyHitter => HeavyHitters(token, b, d),
            RoadEventKind.AerialBrute => Brutes(token, b, d),
            _ => null,
        };
        if (main == null) { Running = RoadEventKind.None; RoadEvents.EndExternal("none"); return; }
        StartCoroutine(Run(token, kind, b, main, combo ? ComboFor(kind, token, b, d) : null));
    }

    IEnumerator ComboFor(RoadEventKind kind, int token, DecisionEventTuning.Band b, float d)
    {
        // 2種類の組み合わせ(75km〜): 一目で安全な所が分かる物だけ
        switch (kind)
        {
            case RoadEventKind.TelegraphRush: return HeavyHitters(token, b, d, quiet: true);
            // 鎧の敵 + 高い所の飛ぶ敵(低く打ち上げて退かす)は上級者向け → 半分は「鎧の敵 + 高火力敵」(地上で判断できる組。エステル)
            case RoadEventKind.AerialBrute: return FindDef(d, IsFlyer) != null && R01() < 0.5f ? SkyBlock(token, b, d, true) : HeavyHitters(token, b, d, quiet: true);
            case RoadEventKind.GroundBlock: return HeavyHitters(token, b, d, quiet: true);
            case RoadEventKind.SkyBlock: return HeavyHitters(token, b, d, quiet: true);
        }
        return null;
    }

    IEnumerator Run(int token, RoadEventKind kind, DecisionEventTuning.Band b, IEnumerator main, IEnumerator combo)
    {
        if (OnStarted != null) OnStarted(kind, "");
        if (combo != null) { Combos++; StartCoroutine(combo); }
        yield return main;
        if (token != eventToken) yield break;
        Running = RoadEventKind.None;
        RoadEvents.EndExternal("done");
        FinishLater(b, 0f);
    }

    void FinishLater(DecisionEventTuning.Band b, float extra)
    {
        restUntil = Time.time + b.restAfterSeconds + extra;
        nextCandidateAt = Time.time + Mathf.Max(b.restAfterSeconds, Range(b.gapSeconds)) + extra;
    }

    void Abort(string why)
    {
        Debug.Log($"[Decision] ABORT {Running} ({why})");
        eventToken++;
        StopAllCoroutines();
        ClearTransient();
        Running = RoadEventKind.None; CurrentCue = Cue.None;
        RoadEvents.EndExternal(why);
        restUntil = Time.time + 4f;
        nextCandidateAt = Mathf.Max(nextCandidateAt, Time.time + 6f);
    }

    void ClearTransient()
    {
        foreach (var go in transient) if (go != null) Destroy(go);
        transient.Clear();
    }

    public static event System.Action<RoadEventKind, string> OnDetail; // 確認用(QA の記録)
    void Log(RoadEventKind kind, float d, string detail)
    {
        if (OnDetail != null) OnDetail(kind, detail);
        History.Add($"{d / 1000f:F2}\t{kind}\t{detail}");
        Debug.Log($"[Decision] START {kind} at {d:F0}m {detail}");
    }

    // ===================================================================== //
    // 帯の予兆 + 群れの横切り(画面に固定: 速さに関係なく同じ秒数)
    // ===================================================================== //
    public enum Lane { Ground, Air }

    Lane PickLane(DecisionEventTuning.Band b)
    {
        float chance = T.groundLaneChance;
        var gm = GameManager.Instance;
        if (gm != null && gm.ActiveRunStageId == "sky_corridor") chance = 1f - (1f - chance) * T.skyStageAirMul;
        Lane lane = R01() < Mathf.Clamp01(chance) ? Lane.Ground : Lane.Air;
        if (lane == Lane.Air && !AirLaneSafe(WarnFor(b) + T.sweepSeconds + 0.4f)) lane = Lane.Ground; // 穴があって跳ぶしかない所に空の帯は置かない
        return lane;
    }

    float WarnFor(DecisionEventTuning.Band b)
    {
        var pc = PlayerController.Instance;
        float kmh = pc != null ? pc.CurrentAutoRunSpeed * 3.6f : 100f;
        float extra = Mathf.Lerp(0f, T.warnHighSpeedExtra, Mathf.InverseLerp(100f, Mathf.Max(101f, T.warnHighSpeedKmh), kmh));
        return Mathf.Clamp(T.warnSeconds * b.warnMul + extra, T.warnMin, T.warnMax);
    }

    // 空の帯を置けるか(地上にいれば安全、が守れるか)。穴/障害物は帯の前に平らにして片付ける(LaneSweep の Flatten)ので、
    //  ここで見るのは「洞窟の天井が低い」「下の道の上に上の道がある(空が無い)」だけ
    bool AirLaneSafe(float seconds)
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) return false;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed), px = pc.transform.position.x;
        float baseY = LaneBand.PlayerBaseY();
        for (float t = 0f; t <= seconds; t += 0.1f)
        {
            float x = px + v * t;
            var ceil = tm.GetEffectiveCeilingHeightAt(x);
            if (ceil.HasValue && ceil.Value - baseY < T.airLaneBottom + 1.4f) return false; // 洞窟の天井が低い
            if (OverheadBlocks(tm, x, baseY, pc.AssistOnSky)) return false;                   // 下の道のすぐ上に上の道(天空回廊の浮島は除く)
        }
        return true;
    }

    IEnumerator Alternate(int token, DecisionEventTuning.Band b)
    {
        Lane lane = R01() < 0.5f ? Lane.Ground : Lane.Air;
        if (lane == Lane.Air && !AirLaneSafe(WarnFor(b) + T.sweepSeconds + 0.4f)) lane = Lane.Ground;
        Log(RoadEventKind.Alternate, CurD(), $"steps {T.alternateSteps} first {lane}");
        for (int i = 0; i < Mathf.Max(2, T.alternateSteps); i++)
        {
            if (token != eventToken) yield break;
            yield return LaneSweep(token, b, lane, i == 0 ? 1f : 0.8f, logIt: false);
            yield return new WaitForSeconds(T.alternateGapSeconds);
            lane = lane == Lane.Ground ? Lane.Air : Lane.Ground;
            if (lane == Lane.Air && !AirLaneSafe(WarnFor(b) * 0.8f + T.sweepSeconds + 0.4f)) lane = Lane.Ground; // 穴が来るなら地上の帯のまま
        }
    }

    IEnumerator LaneSweep(int token, DecisionEventTuning.Band b, Lane lane, float warnScale, bool logIt = true)
    {
        var pc = PlayerController.Instance; var cam = Camera.main;
        if (pc == null || cam == null) yield break;
        float d = CurD();
        float warn = WarnFor(b) * warnScale;
        var def = lane == Lane.Ground ? (FindDef(d, IsRunner) ?? FindDef(d, IsGroundNormal)) : FindDef(d, IsFlyer);
        if (def == null) def = FindDef(d, lane == Lane.Ground ? (System.Func<EnemyDefinition, bool>)IsGroundNormal : IsFlyer);
        if (def == null) yield break;
        int n = Mathf.Max(3, Mathf.RoundToInt(Range(T.sweepCount) * b.countMul * Mathf.Max(0.2f, T.intensity)));
        if (logIt) Log(RoadEventKind.TelegraphRush, d, $"lane {lane} warn {warn:F2}s n {n} enemy {def.enemyId}");
        var style = Style();
        // 危険が来る時刻の走る範囲を、画面の外の先で平らにする(穴を埋める)。跳ばされた後に穴へ落ちる/穴を越える跳躍が空の帯へ入る、を無くす。
        //  範囲がまだ画面の中(遅い時)なら、範囲が画面の外へ出るまで予兆を少し遅らせる
        float arriveFrac = Mathf.Clamp01((GameView.Right(cam) + 1.2f - pc.transform.position.x) / Mathf.Max(1f, GameView.Right(cam) - GameView.Left(cam) + 2.4f));
        float arrive = T.sweepSeconds * arriveFrac;
        for (int w = 0; w < 30; w++)
        {
            float v0 = Mathf.Max(5f, pc.CurrentAutoRunSpeed), px0 = pc.transform.position.x;
            if (px0 + v0 * (warn + arrive - 0.3f) > GameView.Right(cam) + 1f) break;
            yield return new WaitForSeconds(0.05f);
            if (token != eventToken) yield break;
        }
        {
            float v0 = Mathf.Max(5f, pc.CurrentAutoRunSpeed), px0 = pc.transform.position.x;
            float a = px0 + v0 * (warn + arrive - 0.3f), z = px0 + v0 * (warn + arrive + T.sweepSpreadSeconds + 0.9f);
            if (!pc.AssistOnSky) Flatten(a, z, lane == Lane.Air);
        }
        float bottom = lane == Lane.Ground ? 0f : T.airLaneBottom;
        float top = lane == Lane.Ground ? T.groundLaneTop * b.safeMul : T.airLaneTop;
        if (lane == Lane.Ground) top = Mathf.Min(top, T.groundLaneTop * 1.15f);
        var bandGo = LaneBand.CreateScreen(lane == Lane.Ground ? bottom : bottom, top, warn, style, lane == Lane.Ground);
        transient.Add(bandGo.gameObject);
        float t0 = Time.time;
        SetCue(lane == Lane.Ground ? Cue.JumpOver : Cue.StayLow, t0 + warn + arrive, warn + T.sweepSeconds + 0.2f, T.sweepSpreadSeconds + 0.25f);
        PlayWarnSfx(lane == Lane.Ground);
        yield return new WaitForSeconds(warn);
        if (token != eventToken) yield break;
        // 群れ: 画面の右から左へ。先頭がプレイヤーの位置へ来るのは sweepSeconds の約半分の時点
        var group = new LaneSweeper.Group();
        for (int i = 0; i < n; i++)
        {
            float delay = n <= 1 ? 0f : T.sweepSpreadSeconds * i / (n - 1);
            float y01 = lane == Lane.Ground ? 0f : (n <= 1 ? 0.5f : (float)(i % 3) / 2f); // 空の帯は3段に散らす(どこへ跳んでも当たる)
            var s = LaneSweeper.Create(def, group, lane == Lane.Ground, bottom, top, y01, delay, T.sweepSeconds, T.sweeperHeight, style);
            if (s != null) transient.Add(s.gameObject);
        }
        if (bandGo != null) bandGo.Strike(T.sweepSeconds + T.sweepSpreadSeconds);
        yield return new WaitForSeconds(T.sweepSeconds + T.sweepSpreadSeconds + 0.15f);
        if (group.hit) SweepHits++;
        SweepKills += group.kills;
    }

    // ===================================================================== //
    // 世界に置く物(上空封鎖 / 地上封鎖 / 高火力敵 / エリアル敵)
    // ===================================================================== //
    struct Region { public float x0, x1; public bool ok, upper; }
    public static readonly Dictionary<string, int> RegionRejects = new Dictionary<string, int>(); // 確認用: 区間が見つからなかった理由
    static void Reject(string why) { RegionRejects[why] = (RegionRejects.TryGetValue(why, out int c) ? c : 0) + 1; }

    // プレイヤーが走っている面(上下の分岐では、今いる方のルート)の高さ。無ければ null(穴/ルートの終わり)
    static float? SurfaceAt(TerrainManager tm, float x, bool upper)
    {
        if (upper)
        {
            var s = tm.GetSkyHeightAt(x);
            if (!s.HasValue || !tm.GetSkyHeightAt(x - 0.8f).HasValue || !tm.GetSkyHeightAt(x + 0.8f).HasValue) return null;
            return s;
        }
        var h = tm.GetHeightAt(x);
        if (!h.HasValue || tm.IsNearPit(x, 1.2f)) return null;
        return h;
    }

    // プレイヤーの前(画面の外)に、穴/上の道の無い区間を探す。予兆に必要な秒数だけ先から
    Region FindRegion(float length, float leadSeconds, bool needAir, bool needJump = false)
    {
        var r = new Region();
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance; var cam = Camera.main;
        if (pc == null || tm == null || cam == null) return r;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed), px = pc.transform.position.x;
        float visible = GameView.Right(cam) - px;
        bool upper = pc.AssistOnSky; // 上の道を走っている時はその面に置く
        float start = px + Mathf.Max(visible + 4f, v * leadSeconds);
        for (int tries = 0; tries < 8; tries++, start += Mathf.Max(6f, length * 0.4f))
        {
            float x0 = start, x1 = start + length;
            if (!tm.IsGenerated(x1 + 6f)) tm.GenerateNow(x1 + 10f);
            if (!tm.IsGenerated(x1 + 4f)) { Reject("not generated"); return r; } // まだ地形が無い(次のフレームでまた試す)
            // 下の道(普通の地面)は穴を埋めて平らにできる → 穴は理由にしない。上の道は途切れたら使わない
            if (!upper) Flatten(x0 - 4f, x1 + 4f, needAir);
            string bad = null;
            for (float x = x0 - 3f; x <= x1 + 3f && bad == null; x += 0.75f)
            {
                var s = SurfaceAt(tm, x, upper);
                if (!s.HasValue) bad = upper ? "upper route ends" : "pit";
                else if ((needAir || needJump) && OverheadBlocks(tm, x, s.Value, upper)) bad = "upper route overhead"; // 下の道のすぐ上に上の道がある(空/跳ぶ所が無い)
                else if (needAir)
                {
                    var ceil = tm.GetEffectiveCeilingHeightAt(x);
                    if (ceil.HasValue && ceil.Value - s.Value < T.skyHeights.x + 1.2f) bad = "low ceiling"; // 天井が低い
                }
            }
            if (bad != null) { Reject(bad); continue; }
            r.x0 = x0; r.x1 = x1; r.ok = true; r.upper = upper;
            return r;
        }
        return r;
    }

    // 下の道を走る時、すぐ上に上の道(荒野の分岐)があって空の帯/跳ぶ空間が無いか。天空回廊の浮島は足場なので対象外
    //  (浮島に乗れば画面の帯はプレイヤーの足元から測り直す)
    static bool OverheadBlocks(TerrainManager tm, float x, float surfaceY, bool upper)
    {
        if (upper) return false;
        var gm = GameManager.Instance;
        if (gm != null && gm.ActiveRunStageId == "sky_corridor") return false;
        var o = tm.GetSkyHeightAt(x);
        return o.HasValue && o.Value - surfaceY < T.airLaneTop + 0.5f;
    }

    // 画面の外の先 [a, b](シーンの X)の穴を埋めて平らにする(これから作る地面も穴/坂なし)。障害物は置かない(空の帯は今ある物も片付ける)
    public static int Flattened;
    void Flatten(float a, float b, bool clearObstacles)
    {
        var tm = TerrainManager.Instance; var cam = Camera.main;
        if (tm == null || cam == null) return;
        float right = GameView.Right(cam);
        a = Mathf.Max(a, right + 0.5f); // 画面の中の地面は変えない
        if (b <= a) return;
        if (!tm.IsGenerated(b + 2f)) tm.GenerateNow(b + 6f);
        Flattened += tm.SetResumeFlatZone(FloatingOrigin.ToLogical(a), FloatingOrigin.ToLogical(b));
        EncounterDirector.AddExternalSpan(FloatingOrigin.ToLogical(a), FloatingOrigin.ToLogical(b));
        if (clearObstacles)
            foreach (var o in ObstacleController.All.ToArray())
                if (o != null && o.transform.position.x > right + 1f && o.transform.position.x > a - 3f && o.transform.position.x < b + 3f) Destroy(o.gameObject);
    }

    // 区間を空ける: 画面の外の普通の敵と障害物を片付け、普通の敵/障害物を置かない区間にする
    void Claim(Region r)
    {
        var cam = Camera.main;
        float right = cam != null ? GameView.Right(cam) : float.NegativeInfinity;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (e == null || e.GetComponent<DecisionEnemy>() != null) continue;
            float x = e.transform.position.x;
            if (x > right + 1f && x > r.x0 - 4f && x < r.x1 + 4f) Destroy(e.gameObject);
        }
        foreach (var o in ObstacleController.All.ToArray())
            if (o != null && o.transform.position.x > right + 1f && o.transform.position.x > r.x0 - 4f && o.transform.position.x < r.x1 + 4f) Destroy(o.gameObject);
        double a = FloatingOrigin.ToLogical(r.x0) - 4.0, z = FloatingOrigin.ToLogical(r.x1) + 4.0;
        reserved.Add(new Vector2((float)a, (float)z));
        EncounterDirector.AddExternalSpan(a, z);
    }

    public static bool IsReservedLogical(double lx)
    {
        if (Instance == null) return false;
        foreach (var r in Instance.reserved) if (lx >= r.x && lx <= r.y) return true;
        return false;
    }
    public static double ReservedEndLogical(double lx)
    {
        if (Instance == null) return lx;
        double end = lx;
        foreach (var r in Instance.reserved) if (lx >= r.x && lx <= r.y) end = System.Math.Max(end, r.y);
        return end;
    }
    void PruneReserved()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        double pl = FloatingOrigin.ToLogical(pc.transform.position.x);
        reserved.RemoveAll(r => r.y < pl - 30.0);
    }

    IEnumerator WaitRegion(int token, float length, float lead, bool needAir, System.Action<Region> got, bool needJump = false)
    {
        for (int i = 0; i < 40; i++)
        {
            if (token != eventToken) yield break;
            var r = FindRegion(length, lead, needAir, needJump);
            if (r.ok) { got(r); yield break; }
            yield return new WaitForSeconds(0.1f);
        }
        got(new Region());
    }

    IEnumerator SkyBlock(int token, DecisionEventTuning.Band b, float d, bool highOnly)
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) yield break;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed);
        float warn = WarnFor(b);
        float len = Mathf.Max(T.skyMinLength, v * T.skySeconds);
        Region r = default;
        yield return WaitRegion(token, len, warn + 0.3f, true, x => r = x);
        if (token != eventToken) yield break;
        if (!r.ok)
        {
            if (highOnly) yield break;
            Log(RoadEventKind.SkyBlock, d, "no room -> air lane"); // 区間が無い(分岐/穴が続く): 同じ意味の「空の帯」へ
            if (AirLaneSafe(WarnFor(b) + T.sweepSeconds + 0.4f)) yield return LaneSweep(token, b, Lane.Air, 1f, logIt: false);
            yield break;
        }
        Claim(r);
        int n = Mathf.Max(3, Mathf.RoundToInt(Range(T.skyCount) * b.countMul * Mathf.Max(0.2f, T.intensity)));
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.Lerp(r.x0, r.x1, (i + 0.5f) / n) + Range(-0.6f, 0.6f);
            var def = FindDef(d, IsFlyer); if (def == null) break;
            float g = SurfaceAt(tm, x, r.upper) ?? tm.GetGroundLineAt(x);
            float lo = highOnly ? Mathf.Max(T.skyHeights.x, 3.3f) : T.skyHeights.x;
            float h = Mathf.Lerp(lo, T.skyHeights.y, (i % 3) / 2f) + Range(-0.25f, 0.25f);
            var ceil = tm.GetEffectiveCeilingHeightAt(x);
            float y = g + h; if (ceil.HasValue) y = Mathf.Min(y, ceil.Value - 0.8f);
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, y), EnemyAiTier.T0, EnemyBehaviorKind.None);
            if (go == null) continue;
            MarkRoute(go, r.upper);
            DecisionEnemy.Attach(go, DecisionRole.SkyBlocker); placed++;
        }
        if (!highOnly) Log(RoadEventKind.SkyBlock, d, $"flyers {placed} len {len:F0}m warn {warn:F2}s");
        var band = LaneBand.CreateWorld(r.x0, r.x1, highOnly ? 3.0f : T.airLaneBottom, T.airLaneTop + 0.6f, Style(), false, SurfaceAt(tm, (r.x0 + r.x1) * 0.5f, r.upper));
        transient.Add(band.gameObject);
        float arrive = (r.x0 - pc.transform.position.x) / v, pass = (r.x1 - r.x0) / v;
        if (!highOnly) SetCue(Cue.StayLow, Time.time + arrive, arrive + pass + 0.3f, pass);
        yield return WaitPassed(token, r.x1 + 2f, 12f);
    }

    IEnumerator GroundBlock(int token, DecisionEventTuning.Band b, float d)
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) yield break;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed);
        float warn = WarnFor(b);
        float len = Mathf.Clamp(v * T.groundSeconds / Mathf.Max(0.5f, b.safeMul), T.groundMinLength, T.groundMaxLength);
        Region r = default;
        yield return WaitRegion(token, len, warn + 0.3f, false, x => r = x, needJump: true);
        if (token != eventToken) yield break;
        if (!r.ok)
        {
            Log(RoadEventKind.GroundBlock, d, "no room -> ground lane"); // 同じ意味の「地上の帯」へ
            yield return LaneSweep(token, b, Lane.Ground, 1f, logIt: false);
            yield break;
        }
        Claim(r);
        int n = Mathf.Max(2, Mathf.RoundToInt(Range(T.groundCount) * b.countMul * Mathf.Max(0.2f, T.intensity)));
        n = Mathf.Min(n, Mathf.Max(2, Mathf.FloorToInt(len / 1.4f) + 1)); // 重ならない数
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float x = n <= 1 ? r.x0 : Mathf.Lerp(r.x0, r.x1, (float)i / (n - 1));
            var def = FindDef(d, IsHeavyGround) ?? FindDef(d, IsGroundNormal); if (def == null) break;
            float? gy = SurfaceAt(tm, x, r.upper); if (!gy.HasValue) continue;
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, gy.Value), EnemyAiTier.T0, EnemyBehaviorKind.None);
            if (go == null) continue;
            MarkRoute(go, r.upper);
            DecisionEnemy.Attach(go, DecisionRole.GroundBlocker); placed++;
        }
        Log(RoadEventKind.GroundBlock, d, $"heavies {placed} len {len:F1}m warn {warn:F2}s");
        var band = LaneBand.CreateWorld(r.x0 - 1.2f, r.x1 + 1.2f, 0f, 2.1f, Style(), true, SurfaceAt(tm, (r.x0 + r.x1) * 0.5f, r.upper));
        transient.Add(band.gameObject);
        float arrive = (r.x0 - 1.2f - pc.transform.position.x) / v, pass = (r.x1 - r.x0 + 2.4f) / v;
        CueRegionLogical = new Vector2(FloatingOrigin.ToLogical(r.x0), FloatingOrigin.ToLogical(r.x1));
        SetCue(Cue.JumpOver, Time.time + arrive, arrive + pass + 0.3f, pass);
        yield return WaitPassed(token, r.x1 + 2f, 12f);
    }

    IEnumerator HeavyHitters(int token, DecisionEventTuning.Band b, float d, bool quiet = false)
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) yield break;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed);
        int n = Mathf.Max(1, Mathf.RoundToInt(Range(T.heavyCount) * b.countMul * Mathf.Max(0.2f, T.intensity)));
        float spacing = Mathf.Max(6f, v * 0.7f);
        Region r = default;
        yield return WaitRegion(token, spacing * (n - 1) + 2f, WarnFor(b) + (quiet ? 1.6f : 0.3f), false, x => r = x);
        if (!r.ok || token != eventToken) { if (!quiet) Log(RoadEventKind.HeavyHitter, d, "no room"); yield break; }
        if (!quiet) Claim(r);
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float x = r.x0 + spacing * i;
            var def = FindDef(d, IsGroundNormal); if (def == null) break;
            float? gy = SurfaceAt(tm, x, r.upper); if (!gy.HasValue) continue;
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, gy.Value), EnemyAiTier.T2, def.behaviorKind);
            if (go == null) continue;
            MarkRoute(go, r.upper);
            DecisionEnemy.Attach(go, DecisionRole.Heavy); placed++;
        }
        if (!quiet) Log(RoadEventKind.HeavyHitter, d, $"heavy hitters {placed}");
        else History.Add($"{d / 1000f:F2}\t+HeavyHitter\t{placed}");
        if (!quiet) yield return WaitPassed(token, r.x1 + 2f, 12f);
    }

    IEnumerator Brutes(int token, DecisionEventTuning.Band b, float d)
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) yield break;
        float v = Mathf.Max(5f, pc.CurrentAutoRunSpeed);
        int n = Mathf.Max(1, Mathf.RoundToInt(Range(T.bruteCount) * Mathf.Max(0.2f, Mathf.Min(1.2f, T.intensity))));
        float spacing = Mathf.Max(10f, v * 1.4f);
        Region r = default;
        yield return WaitRegion(token, spacing * (n - 1) + 2f, WarnFor(b) + 0.6f, false, x => r = x);
        if (!r.ok || token != eventToken) { Log(RoadEventKind.AerialBrute, d, "no room"); yield break; }
        Claim(r);
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float x = r.x0 + spacing * i;
            var def = FindDef(d, IsHeavyGround) ?? FindDef(d, IsGroundNormal); if (def == null) break;
            float? gy = SurfaceAt(tm, x, r.upper); if (!gy.HasValue) continue;
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, gy.Value), EnemyAiTier.T0, EnemyBehaviorKind.None);
            if (go == null) continue;
            MarkRoute(go, r.upper);
            var ec = go.GetComponent<EnemyController>();
            if (ec != null) ec.maxHp = Mathf.Max(ec.maxHp, Mathf.RoundToInt(LargeHpNow() * T.bruteHpMul));
            DecisionEnemy.Attach(go, DecisionRole.Brute); placed++;
        }
        Log(RoadEventKind.AerialBrute, d, $"brutes {placed}");
        CueRegionLogical = new Vector2(FloatingOrigin.ToLogical(r.x0), FloatingOrigin.ToLogical(r.x1));
        float arrive = (r.x0 - pc.transform.position.x) / v;
        SetCue(Cue.Launch, Time.time + arrive, arrive + (r.x1 - r.x0) / v + 0.5f, (r.x1 - r.x0) / v);
        yield return WaitPassed(token, r.x1 + 2f, 14f);
    }

    static void MarkRoute(GameObject go, bool upper) { if (!upper) return; var ec = go.GetComponent<EnemyController>(); if (ec != null) ec.onUpperRoute = true; }

    static int LargeHpNow() => DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(DifficultyTuning.I.largeHpMultiplier) : CombatScale.K;

    // プレイヤーが x を越える(または時間切れ)まで待つ
    IEnumerator WaitPassed(int token, float sceneX, float maxSeconds)
    {
        double lx = FloatingOrigin.ToLogical(sceneX);
        float t = 0f;
        while (t < maxSeconds && token == eventToken)
        {
            var pc = PlayerController.Instance;
            if (pc == null || FloatingOrigin.ToLogical(pc.transform.position.x) > lx) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ---- 通常の Encounter に高火力敵を混ぜる ----
    void OnDirectorSpawned(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> list)
    {
        if (!Usable || list == null) return;
        var gm = GameManager.Instance;
        if (gm == null || !InRange(gm.MaxDistance)) return;
        float chance = T.BandAt(gm.MaxDistance / 1000f).heavyHitterInNormal;
        if (chance <= 0f) return;
        foreach (var m in list)
        {
            if (m.go == null || m.def == null || !IsGroundNormal(m.def)) continue;
            if (R01() < chance) { DecisionEnemy.Attach(m.go, DecisionRole.Heavy); HeavyInNormal++; }
        }
    }
    public static int HeavyInNormal;

    // ---- 予兆(QA の人の操作が読む) ----
    static void SetCue(Cue c, float dangerAt, float validFor, float dangerLen)
    {
        CurrentCue = c; CueDangerAt = dangerAt; CueDangerEnd = dangerAt + Mathf.Max(0.05f, dangerLen); CueUntil = Time.time + validFor;
    }

    // ---- 敵の選び方(今のマップの距離の帯から) ----
    static bool IsFlyer(EnemyDefinition d) => d.movementType == EnemyMovementType.Flying && d.hpMultiplier < 5f;
    static bool IsGround(EnemyDefinition d) => d.movementType != EnemyMovementType.Flying && d.behaviorKind != EnemyBehaviorKind.BurrowWorm && d.hpMultiplier < 5f && d.hpMultiplier >= 0.7f;
    static bool IsGroundNormal(EnemyDefinition d) => IsGround(d) && d.hpMultiplier < DifficultyTuning.I.largeHpMultiplier;
    static bool IsHeavyGround(EnemyDefinition d) => IsGround(d) && d.hpMultiplier >= 1.3f;
    static bool IsRunner(EnemyDefinition d) => IsGround(d) && (d.behaviorKind == EnemyBehaviorKind.Rusher || d.behaviorKind == EnemyBehaviorKind.Chaser || d.behaviorKind == EnemyBehaviorKind.SkyHound);

    EnemyDefinition FindDef(float d, System.Func<EnemyDefinition, bool> pred)
    {
        var dir = EncounterDirector.Instance;
        var band = dir != null && dir.Profile != null ? dir.Profile.BandFor(d) : null;
        if (band == null) return null;
        float sum = 0f;
        var list = new List<(EnemyDefinition def, float w)>();
        foreach (var e in band.enemies)
        {
            if (e == null || e.weight <= 0f) continue;
            var def = EnemyDatabase.FindById(e.enemyId);
            if (def == null || !pred(def)) continue;
            list.Add((def, e.weight)); sum += e.weight;
        }
        if (list.Count == 0) return null;
        float r = R01() * sum;
        foreach (var x in list) { if (r < x.w) return x.def; r -= x.w; }
        return list[list.Count - 1].def;
    }

    // ---- マップの見た目 ----
    public struct MapStyle { public Color accent, dust; public int kind; } // kind 0 = 荒野(砂ぼこり)/ 1 = 洞窟(結晶)/ 2 = 天空(風)
    static MapStyle Style()
    {
        var gm = GameManager.Instance;
        string st = gm != null ? gm.ActiveRunStageId : "";
        if (st == "natural_cave") return new MapStyle { accent = new Color(0.75f, 0.55f, 1f), dust = new Color(0.7f, 0.6f, 1f, 0.75f), kind = 1 };
        if (st == "sky_corridor") return new MapStyle { accent = new Color(0.6f, 0.9f, 1f), dust = new Color(0.9f, 0.97f, 1f, 0.7f), kind = 2 };
        return new MapStyle { accent = new Color(1f, 0.78f, 0.45f), dust = new Color(0.62f, 0.5f, 0.36f, 0.7f), kind = 0 };
    }

    static void PlayWarnSfx(bool ground)
    {
        var am = AudioManager.Instance;
        if (am != null) am.PlaySe(SeId.EnemyTelegraph, 0.75f);
        if (ground) SkyBossSfx.Play(SkyBossSfx.Impact(), 0.25f); else SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.35f);
    }

    static float CurD() => GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;
    float R01() => (float)rng.NextDouble();
    float Range(float a, float b) => a + (b - a) * R01();
    float Range(Vector2 v) => Range(v.x, v.y);
    int Range(Vector2Int v) => v.x + Mathf.FloorToInt(R01() * (v.y - v.x + 1));

    // 開発用: 今すぐ起こす(QA)
    public static bool DebugStart(RoadEventKind kind, bool combo = false)
    {
        if (Instance == null || GameManager.Instance == null) return false;
        if (Running != RoadEventKind.None) Instance.Abort("debug");
        RoadEvents.EndExternal("debug");
        float d = GameManager.Instance.MaxDistance;
        Instance.StartKind(kind, T.BandAt(Mathf.Max(T.fromKm, d / 1000f)), d, combo);
        return true;
    }

    public static string Describe() => Running == RoadEventKind.None ? "-" : $"{Running} cue {CurrentCue}";
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// ラストダンジョン(2026-09-30)の自動確認。開発ビルド/Editor専用。起動引数 -ldQa <dir> -ldQaMode <mode> [-ldChar id]
//  density … 0〜90,000mの難易度: 7か所(3k/15k/30k/45k/60k/75k/88k)へワープして各2,000mを実際に走り、100mごとの
//             難易度の波(激しさ)/敵/障害物/穴(大きな穴)/天井の圧迫/落下物/被弾/FPSを記録(-lcLegacyDifficultyで従来と比較)
//  flow    … 89,300mから: ボスラッシュ(構成/同時数/時間/負荷) → 静寂(何も出ないか/距離表示/BGM) → 三姉妹戦(移行/段階)
//             → RESULTへ行かないか → エンドロール(文字に乗る/攻撃/石板) → ONE MORE MILE?(YES/NOを交互に攻撃→YES) → 走り続けるか
//  stop    … 99,950mから: 三姉妹(HPを小さく) → エンドロール → NOを壊す → 減速 → 停止 → ホームへ戻るか(正常終了/ゲームオーバーでない)
// ボット: 攻撃力+12、高速補助を全速度で有効(穴を跳ぶ上手なプレイヤーの代わり)、近いボスへフリック攻撃。ライフは減ったら戻す(被弾は数える)。
public class LastDungeonQa : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string dir = Arg("-ldQa", null);
        if (dir == null) return;
        Application.runInBackground = true;
        var go = new GameObject("LastDungeonQa");
        DontDestroyOnLoad(go);
        var q = go.AddComponent<LastDungeonQa>();
        q.outDir = dir;
        q.mode = Arg("-ldQaMode", "flow");
    }

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    string outDir, mode;
    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    readonly List<string> exceptions = new List<string>();
    GameManager gm;
    PlayerController pc;
    int shotNo, hits;
    float warpedTo = -99999f;
    readonly Dictionary<string, int> hitWhy = new Dictionary<string, int>();

    void L(string s) { log.AppendLine(s); Debug.Log("[LDQA] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"{shotNo++:0000}_{name}.png")); }

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        Application.logMessageReceived += (c, tr, type) =>
        {
            if (type == LogType.Exception) { anyException = true; if (exceptions.Count < 30) exceptions.Add(c + " | " + tr.Split('\n')[0]); }
        };
        FreezeDiagnostics.EventTap += m =>
        {
            if (!m.StartsWith("[Damage] Hit reason=")) return;
            hits++;
            string r = m.Substring("[Damage] Hit reason=".Length); int sp = r.IndexOf(' '); if (sp > 0) r = r.Substring(0, sp);
            hitWhy[r] = hitWhy.TryGetValue(r, out int c) ? c + 1 : 1;
        };
        yield return new WaitForSecondsRealtime(2f);
        StartCoroutine(AutoPickCards());
        if (mode == "density") yield return DensityMode();
        else if (mode == "stop") yield return StopMode();
        else yield return FlowMode();
        L("");
        foreach (var e in exceptions) L("[EXC] " + e);
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"ld_{mode}.txt"), log.ToString());
        yield return new WaitForSecondsRealtime(0.5f);
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===================================================================== //
    // 共通
    // ===================================================================== //
    IEnumerator BeginRun(string ch)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage(LastCorridorDirector.StageId);
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        keepAlive = true;
        StartCoroutine(KeepAlive());
        // 上手なプレイヤーの代わり: 高速補助を全速度で最大にする(穴を跳ぶ/障害物を壊す)
        var a = HighSpeedAssist.Instance;
        if (a != null) { a.SetEnabled(true); a.engageKmh = 1f; a.releaseKmh = 0.5f; a.fullAssistKmh = 2f; }
        pc.AddAttackPower(int.Parse(Arg("-ldPower", "12")));
    }

    bool keepAlive;
    IEnumerator KeepAlive()
    {
        var setter = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        while (keepAlive)
        {
            if (gm != null && gm.Lives < 500 && !gm.IsGameOver) setter.Invoke(gm, new object[] { 990 });
            yield return null;
        }
    }

    int cardPicks;
    IEnumerator AutoPickCards()
    {
        while (true)
        {
            var g = GameManager.Instance;
            if (g != null && g.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    // SPEED DOWN系を避けて、なるべく移動系以外の最初のカードを取る(ボットで遅くならないように)
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.25f);
                    seq.OnCardClicked(0);
                    cardPicks++;
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }
            }
            yield return null;
        }
    }

    IEnumerator Flick(PlayerController.FlickDirection f)
    {
        pc.debugInjectFlick = f;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
    }

    Vector3? NearestBoss()
    {
        Vector3? best = null; float bd = float.MaxValue;
        Vector3 p = pc.transform.position;
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (w != null && !w.IsDead && w.gameObject.activeInHierarchy) { float d = (w.CenterWorld - p).sqrMagnitude; if (d < bd) { bd = d; best = w.CenterWorld; } }
        foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) { float d = (x.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = x.transform.position; } }
        foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) { float d = (x.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = x.transform.position; } }
        return best;
    }

    float lastFlick;
    void BotAttackBoss()
    {
        if (Time.time - lastFlick < 0.16f) return;
        Vector3? tp = NearestBoss();
        if (!tp.HasValue) return;
        lastFlick = Time.time;
        float dx = tp.Value.x - pc.transform.position.x, dy = tp.Value.y - pc.transform.position.y;
        var f = dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
        StartCoroutine(Flick(f));
    }

    // 性能(FPS/メモリ)
    float fpsSum; int fpsN; float fpsMin = 999f;
    void SampleFps() { float f = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime); fpsSum += f; fpsN++; if (f < fpsMin) fpsMin = f; }
    string PerfLine()
    {
        string s = $"fps avg {(fpsN > 0 ? fpsSum / fpsN : 0f):F0} min {fpsMin:F0} | mem {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F0}MB mono {System.GC.GetTotalMemory(false) / 1048576f:F0}MB";
        fpsSum = 0f; fpsN = 0; fpsMin = 999f;
        return s;
    }
    int ActiveEnemies() { int n = 0; foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e.gameObject.activeInHierarchy && !e.IsDying) n++; return n; }
    int ActiveBosses() { int n = 0; foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (w != null && !w.IsDead && w.gameObject.activeInHierarchy) n++; return n; }

    // ===================================================================== //
    // density: 0〜90,000mの難易度
    // ===================================================================== //
    // ワープを繰り返すと「ボス戦中に走った距離」の除外分が溜まり、ワープ後に距離が進まなくなる(テスト側の都合)ので0へ戻す
    void ResetDistanceExclusion()
    {
        foreach (var n in new[] { "distanceExclusionOffset", "bossPhaseEntryRawDistance" }) { var f = typeof(GameManager).GetField(n, NP); if (f != null) f.SetValue(gm, 0f); }
        foreach (var n in new[] { "distanceExclusionOffsetExact", "bossPhaseEntryRawDistanceExact" }) { var f = typeof(GameManager).GetField(n, NP); if (f != null) f.SetValue(gm, 0.0); }
    }

    class Acc { public int bins, enemies, obstacles, pits, huge, hits, debris; public float press, secs; }

    IEnumerator DensityMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        yield return BeginRun(ch);
        var lc = LastCorridorDirector.Instance;
        bool hard = lc != null && lc.hardMode;
        BonusZone.BlockedAt = d => true; // 計測ではBONUS ZONEを出さない(敵の数に混ざる)
        L($"[density] char={ch} hardMode={hard} (legacy={!hard}). 3,000m(難易度の波1周)ずつ、100mごとに記録");
        var tm = TerrainManager.Instance;
        var dir = EncounterDirector.Instance;
        int obstacles = 0;
        System.Action<ObstacleController, string> onObs = (o, st) => obstacles++;
        ObstacleSpawner.Created += onObs;
        float[] points = Arg("-ldPoints", "1200,12000,27000,42000,57000,72000,84000").Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var byPhase = new Dictionary<string, Acc> { { "calm", new Acc() }, { "build", new Acc() }, { "surge", new Acc() } };
        var all = new Acc();
        foreach (float at in points)
        {
            gm.DebugWarpToDistance(at);
            ResetDistanceExclusion();
            float w = 0f;
            while (w < 3f) { yield return null; w += Time.deltaTime; }
            float segStart = Mathf.Ceil(gm.MaxDistance / 100f) * 100f;
            string waves = lc != null ? string.Concat(Enumerable.Range(0, 30).Select(i => lc.WaveLabel(segStart + i * 100f + 50f).Substring(0, 1))) : "-";
            L($"[{at / 1000f:F0}k] ---- wave per 100m: {waves}");
            var seg = new Acc();
            int f0 = FallingDebris.Started, g0 = LastCorridorDirector.AmbushGatesPlaced, rec0 = dir.Recent.Count;
            float bossTime = 0f, t0 = Time.time;
            for (int bin = 0; bin < 30; bin++)
            {
                float b0 = segStart + bin * 100f, b1 = b0 + 100f;
                string label = lc != null ? lc.WaveLabel(b0 + 50f) : "calm";
                // この100mの地形(生成されたらすぐ、プレイヤーが着く前に測る)
                float guard = 0f;
                while (FloatingOrigin.ToLogical(tm.GeneratedEndX) < b1 + 1f && guard < 30f) { yield return null; guard += Time.deltaTime; }
                int pits = 0, huge = 0; float press = 0f, run = 0f; bool inPit = false;
                for (float lx = b0; lx < b1; lx += 0.5f)
                {
                    float x = (float)(lx - FloatingOrigin.Offset);
                    float? h = tm.GetHeightAt(x);
                    if (!h.HasValue) { if (!inPit) { pits++; inPit = true; run = 0f; } run += 0.5f; }
                    else { if (inPit && run >= 4.9f) huge++; inPit = false; }
                    float? c = tm.GetEffectiveCeilingHeightAt(x);
                    if (c.HasValue && h.HasValue && c.Value - h.Value < 4.6f) press += 0.5f;
                }
                // 走る
                while (gm.MaxDistance < b0 && guard < 60f) { yield return null; guard += Time.deltaTime; }
                int be = dir.SpawnedEnemies, bo = obstacles, bh = hits, bd = FallingDebris.Started;
                float bt = Time.time;
                guard = 0f;
                while (gm.MaxDistance < b1 && guard < 60f)
                {
                    if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) { bossTime += Time.deltaTime; BotAttackBoss(); }
                    SampleFps();
                    yield return null; guard += Time.deltaTime;
                }
                var a = byPhase[label];
                foreach (var acc in new[] { a, seg, all })
                {
                    acc.bins++; acc.enemies += dir.SpawnedEnemies - be; acc.obstacles += obstacles - bo; acc.pits += pits; acc.huge += huge; acc.press += press;
                    acc.hits += hits - bh; acc.debris += FallingDebris.Started - bd; acc.secs += Time.time - bt;
                }
            }
            var formations = new Dictionary<string, int>();
            for (int i = rec0; i < dir.Recent.Count; i++) { string f = dir.Recent[i].formation; formations[f] = formations.TryGetValue(f, out int c) ? c + 1 : 1; }
            L($"[{at / 1000f:F0}k] 3000m in {Time.time - t0:F0}s (now {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h) {Fmt(seg)} ambushGates={LastCorridorDirector.AmbushGatesPlaced - g0} bossTime={bossTime:F0}s | {PerfLine()} activeEnemiesNow={ActiveEnemies()}");
            L("   formations: " + string.Join(", ", formations.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
            Shot($"density_{at / 1000f:F0}k");
        }
        ObstacleSpawner.Created -= onObs;
        L("");
        L("[per 1km / per minute of running] (100m bins grouped by the wave)");
        foreach (var kv in byPhase) if (kv.Value.bins > 0) L($"  {kv.Key,-5} ({kv.Value.bins * 100}m): {Fmt(kv.Value)}");
        L($"  ALL   ({all.bins * 100}m): {Fmt(all)}");
        L("hit reasons: " + string.Join(", ", hitWhy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
        L($"falling structures: attached fall={LastCorridorDirector.FallingAttached} gate={LastCorridorDirector.GateAttached} started={FallingDebris.Started} lowered={FallingDebris.Lowered} landed={FallingDebris.Landed} skipped={FallingDebris.Skipped} minLead={FallingDebris.MinLeadSeconds:F2}s | obstacles created={obstacles} broken={ObstacleController.TotalBroken}");
        Check(!anyException, "no exceptions while running the last dungeon");
    }

    static string Fmt(Acc a)
    {
        float km = Mathf.Max(0.1f, a.bins * 0.1f), min = Mathf.Max(0.05f, a.secs / 60f);
        return $"enemies {a.enemies / km:F1}/km ({a.enemies / min:F1}/min), obstacles {a.obstacles / km:F1}/km, pits {a.pits / km:F1}/km (huge {a.huge / km:F1}/km), pressed ceiling {a.press / km:F0}m/km, falling debris {a.debris / km:F1}/km, hits {a.hits / km:F2}/km";
    }

    // ===================================================================== //
    // flow: ボスラッシュ → 静寂 → 三姉妹 → エンドロール → YES
    // ===================================================================== //
    IEnumerator FlowMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        yield return BeginRun(ch);
        var flow = LastDungeonFlow.Instance;
        Check(flow != null, "LastDungeonFlow exists on the last dungeon");
        float warp = float.Parse(Arg("-ldFrom", "89300"), System.Globalization.CultureInfo.InvariantCulture);
        gm.DebugWarpToDistance(warp);
        ResetDistanceExclusion();
        L($"[flow] char={ch} warp to {warp:F0}m, flow state={flow?.Current}");
        yield return RushAndSilence(flow, warp < 90000f);
        yield return Finale(flow);
        yield return CreditsAndChoice(flow, true);
    }

    IEnumerator RushAndSilence(LastDungeonFlow flow, bool expectRush)
    {
        var bm = BossManager.Instance;
        var dir = EncounterDirector.Instance;
        int enemies90 = -1, obstacles99 = 0;
        System.Action<ObstacleController, string> onObs = (o, s) => { if (gm.MaxDistance >= 99000f) obstacles99++; };
        ObstacleSpawner.Created += onObs;
        int gatesSeen = 0, lastRushK = 0, maxSimul = 0;
        float gateStart = 0f;
        var gateLog = new List<string>();
        float silenceStartT = -1f;
        int pitsIn99 = 0, pressIn99 = 0; bool terrainChecked = false;
        int enemiesAt99 = -1, debris99 = -1;
        float guard = 0f;
        bool shotMulti = false;
        while (gm.MaxDistance < 100000f && guard < 1500f && !gm.IsGameOver)
        {
            float d = gm.MaxDistance;
            if (d >= 90000f && enemies90 < 0) { enemies90 = dir.SpawnedEnemies; L($"  reached 90,000m: normal encounters stop (spawned so far {enemies90})"); }
            if (bm.IsBossPhase)
            {
                BotAttackBoss();
                if (bm.RushGateK > 0 && bm.RushGateK != lastRushK) { lastRushK = bm.RushGateK; gatesSeen++; gateStart = Time.time; L($"  rush gate {lastRushK * 1000}m: {BossManager.RushGateLabel(lastRushK)}"); fpsSum = 0f; fpsN = 0; fpsMin = 999f; }
                int ab = ActiveBosses(); maxSimul = Mathf.Max(maxSimul, ab);
                if (ab >= 2 && !shotMulti) { shotMulti = true; StartCoroutine(ShotLater("rush_two_bosses", 1.2f)); }
            }
            else if (lastRushK > 0 && gateStart > 0f)
            {
                gateLog.Add($"{lastRushK * 1000}m {BossManager.RushGateLabel(lastRushK)} -> {Time.time - gateStart:F0}s, spawned {bm.RushSpawnedThisGate}, max together {bm.RushMaxSimultaneous} | {PerfLine()}");
                L("   cleared: " + gateLog[gateLog.Count - 1]);
                gateStart = 0f;
            }
            if (d >= 99000f && silenceStartT < 0f && flow.Current == LastDungeonFlow.State.Silence)
            {
                silenceStartT = Time.time; enemiesAt99 = dir.SpawnedEnemies; debris99 = FallingDebris.Started;
                L($"  silence from {d:F0}m: bgm={(AudioManager.Instance != null ? AudioManager.Instance.GetComponent<BgmDirector>()?.Reason : "-")}");
                Shot("silence_99k");
            }
            if (silenceStartT > 0f && !terrainChecked && TerrainManager.Instance.GeneratedEndX > (float)(100000.0 - FloatingOrigin.Offset))
            {
                terrainChecked = true;
                var tm = TerrainManager.Instance;
                bool inPit = false;
                // ワープ直後は、ワープ前に作られていた地形(通常の区間の地形)が少し残るので、その先から測る
                for (float lx = Mathf.Max(99000f, warpedTo + 400f); lx < 100000f; lx += 0.5f)
                {
                    float x = (float)(lx - FloatingOrigin.Offset);
                    float? h = tm.GetHeightAt(x);
                    if (!h.HasValue) { if (!inPit) pitsIn99++; inPit = true; } else inPit = false;
                    float? c = tm.GetEffectiveCeilingHeightAt(x);
                    if (c.HasValue && h.HasValue && c.Value - h.Value < 6f) pressIn99++;
                }
            }
            if (d >= 99890f && d < 99905f) Shot("callout_99900");
            SampleFps();
            yield return null;
            guard += Time.unscaledDeltaTime;
        }
        ObstacleSpawner.Created -= onObs;
        L($"  rush: gates={gatesSeen} max bosses at once={maxSimul}");
        foreach (var g in gateLog) L("   " + g);
        if (expectRush) Check(gatesSeen == 9, $"boss rush has 9 gates (90k-98k) ({gatesSeen})");
        if (expectRush) Check(maxSimul >= 2 && maxSimul <= 3, $"some rush gates have 2 bosses together, never a flood (max {maxSimul})");
        if (expectRush) Check(enemies90 >= 0 && enemiesAt99 >= 0 && enemiesAt99 == enemies90, $"no normal enemies during the boss rush (90k: {enemies90}, 99k: {enemiesAt99})");
        Check(dir.SpawnedEnemies == enemiesAt99 || enemiesAt99 < 0, $"no enemies in the silence (99k-100k): +{dir.SpawnedEnemies - enemiesAt99}");
        Check(obstacles99 == 0, $"no obstacles in the silence ({obstacles99})");
        Check(debris99 < 0 || FallingDebris.Started == debris99, "no falling debris in the silence");
        Check(terrainChecked && pitsIn99 == 0, $"no pits between 99,000 and 100,000m ({pitsIn99})");
        Check(terrainChecked && pressIn99 == 0, $"no ceiling/spikes pressing between 99,000 and 100,000m ({pressIn99 * 0.5f:F0}m)");
        Check(flow.CalloutsShown >= 4, $"distance callouts shown during the silence ({flow.CalloutsShown})");
        Check(!gm.IsGameOver, "run still going at 100,000m");
    }

    IEnumerator ShotLater(string name, float delay) { yield return new WaitForSeconds(delay); Shot(name); }

    IEnumerator Finale(LastDungeonFlow flow)
    {
        float w = 0f;
        while (flow.Current != LastDungeonFlow.State.Finale && w < 20f) { yield return null; w += Time.deltaTime; }
        Check(flow.Current == LastDungeonFlow.State.Finale && ReaperFinaleBattle.Instance != null, $"100,000m -> the Reaper Sisters fight starts (state {flow.Current})");
        Check(ReaperBase.Active == null, "the chasing reaper is not spawned on the last dungeon");
        var bm = BossManager.Instance;
        var fin = ReaperFinaleBattle.Instance;
        string lastStage = ""; float stageT = Time.time;
        var stages = new List<string>();
        bool shotSolo = false, shotGroup = false;
        float t0 = Time.time;
        while (fin != null && fin.Current != ReaperFinaleBattle.Stage.Done && Time.time - t0 < 400f && !gm.IsGameOver)
        {
            string st = fin.Current + (fin.Current == ReaperFinaleBattle.Stage.Solo ? "#" + fin.SoloIndex : "");
            if (st != lastStage)
            {
                if (lastStage != "") stages.Add($"{lastStage} {Time.time - stageT:F0}s");
                lastStage = st; stageT = Time.time;
                L($"  finale stage {st} (bosses {ActiveBosses()}, bgm {(AudioManager.Instance != null ? AudioManager.Instance.GetComponent<BgmDirector>()?.Reason : "-")})");
            }
            if (fin.Current == ReaperFinaleBattle.Stage.Solo && !shotSolo && Time.time - stageT > 2.5f) { shotSolo = true; Shot("finale_solo"); }
            if (fin.Current == ReaperFinaleBattle.Stage.Group && !shotGroup && ActiveBosses() >= 3 && Time.time - stageT > 3f) { shotGroup = true; Shot("finale_group"); }
            BotAttackBoss();
            SampleFps();
            yield return null;
        }
        if (lastStage != "") stages.Add($"{lastStage} {Time.time - stageT:F0}s");
        L("  finale: " + string.Join(" -> ", stages) + $" | total {Time.time - t0:F0}s | {PerfLine()}");
        Check(stages.Any(s => s.StartsWith("Solo#0")) && stages.Any(s => s.StartsWith("Solo#1")) && stages.Any(s => s.StartsWith("Solo#2")) && stages.Any(s => s.StartsWith("Group")), "finale: three solo fights then all three together");
        Check(fin != null && fin.Current == ReaperFinaleBattle.Stage.Done && fin.GroupDefeated == 3, "all three sisters defeated");
        Check(!gm.IsGameOver && !gm.IsWin, "no RESULT after defeating the sisters (run continues)");
        Check(!bm.IsBossPhase, "boss phase ended after the finale");
    }

    IEnumerator CreditsAndChoice(LastDungeonFlow flow, bool chooseYes)
    {
        float w = 0f;
        while (flow.Current != LastDungeonFlow.State.Credits && w < 10f) { yield return null; w += Time.deltaTime; }
        Check(flow.Current == LastDungeonFlow.State.Credits && flow.Credits != null, "credits road starts right after the finale (no fade/result)");
        var road = flow.Credits;
        if (road == null) yield break;
        L($"  credits: {road.Letters.Count} letters, speed now {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h, bgm {(AudioManager.Instance != null ? AudioManager.Instance.GetComponent<BgmDirector>()?.Reason : "-")}");
        int stoodOn = 0, jumpsAt = 0, letterHits0 = CreditLetter.TotalHits, broken0 = CreditLetter.TotalBroken;
        float blockedFor = 0f; bool wallShot = false, standShot = false, underShot = false;
        var standLetters = new HashSet<string>();
        float lastJump = -9f, lastAtk = -9f;
        float t0 = Time.time;
        float maxSpeed = 0f;
        while (flow.Current == LastDungeonFlow.State.Credits && Time.time - t0 < 180f && !gm.IsGameOver)
        {
            float px = pc.transform.position.x, py = pc.transform.position.y;
            if (Time.time - t0 > 4f) maxSpeed = Mathf.Max(maxSpeed, pc.CurrentAutoRunSpeed); // 最初の3秒は100km/hから減速中
            float? g = TerrainManager.Instance.GetHeightAt(px);
            // 文字の上に立っている(地面より高い所で接地)
            if (pc.IsGrounded && g.HasValue && py > g.Value + 0.4f)
            {
                string sec = road.SectionAt(px);
                if (standLetters.Add(sec)) { stoodOn++; L($"   stood on letters of '{sec}' at height {py - g.Value:F2}m"); }
                if (!standShot) { standShot = true; Shot("credits_standing_on_letter"); }
            }
            // ボット: 前方の文字へ跳び乗る/文字を攻撃する
            // 1段ジャンプで乗れる高さ(上面が足元+1.95m以内)の文字へ、頂点を過ぎて降りる所で上面に来るように跳ぶ
            CreditLetter ahead = null; float best = 99f;
            float v = Mathf.Max(1f, pc.CurrentAutoRunSpeed);
            foreach (var l in road.Letters)
            {
                if (l == null || l.Broken || !l.platform) continue;
                float top = l.SolidRect.yMax - py;
                if (top < 0.3f || top > 1.95f) continue;
                float dx = l.transform.position.x - px;
                if (dx > 0.5f && dx < best) { best = dx; ahead = l; }
            }
            float lead = v * 0.6f; // 跳んでから上面の高さまで降りてくるまで約0.6秒
            if (ahead != null && pc.IsGrounded && best > lead - 0.8f && best < lead + 0.6f && Time.time - lastJump > 0.5f) { lastJump = Time.time; jumpsAt++; StartCoroutine(Flick(PlayerController.FlickDirection.Up)); }
            else if (ahead != null && best < 2f && Time.time - lastAtk > 0.5f) { lastAtk = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            if (!underShot && road.SectionAt(px) == "GAME DESIGN" && pc.IsGrounded) { underShot = true; Shot("credits_arch_run_under"); }
            // 石板: 止められていたら前へ攻撃
            var wall = WorldPlatforms.WallTouching(px, py, py + 1.5f, 0.35f, 0.15f);
            if (wall != null)
            {
                blockedFor += Time.deltaTime;
                if (!wallShot && blockedFor > 0.4f) { wallShot = true; Shot("credits_wall"); }
                if (Time.time - lastAtk > 0.22f) { lastAtk = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            }
            SampleFps();
            yield return null;
        }
        L($"  credits: {Time.time - t0:F0}s, max speed {GameManager.SpeedKmh(maxSpeed):F0}km/h, stood on {stoodOn} groups of letters, jumps {jumpsAt}, letter hits {CreditLetter.TotalHits - letterHits0}, letters broken {CreditLetter.TotalBroken - broken0}, blocked by the wall {blockedFor:F1}s, wall hits {CreditWall.Hits} | {PerfLine()}");
        Check(stoodOn >= 1, $"the player can land on and ride the credit letters ({stoodOn} groups; the bot only tries letters reachable with one jump)");
        Check(CreditLetter.TotalHits - letterHits0 > 0, "credit letters react to attacks");
        Check(CreditLetter.TotalBroken - broken0 > 0, "some credit letters can be broken (YOU / the wall text)");
        Check(blockedFor > 0.2f && road.Wall != null && road.Wall.Broken, $"THANK YOU FOR PLAYING wall blocks the road until broken (blocked {blockedFor:F1}s, broken {road.Wall?.Broken})");
        Check(GameManager.SpeedKmh(maxSpeed) < 40f, $"credits are run at a calm speed ({GameManager.SpeedKmh(maxSpeed):F0}km/h)");

        // ---- ONE MORE MILE? ----
        w = 0f;
        while ((flow.Choice == null || !flow.Choice.Risen) && w < 15f) { yield return null; w += Time.deltaTime; }
        var choice = flow.Choice;
        Check(choice != null && choice.Risen, "ONE MORE MILE? area appears");
        if (choice == null) yield break;
        Check(!pc.autoRunEnabled && pc.CurrentAutoRunSpeed == 0f && pc.IsStandingIdle, "auto-run stops in the choice area (the player stands)");
        yield return new WaitForSeconds(0.6f);
        Shot("choice_area");
        float x0 = pc.transform.position.x;
        yield return new WaitForSeconds(2f);
        Check(Mathf.Abs(pc.transform.position.x - x0) < 0.2f, "no forced movement / no time limit while choosing");
        Check(ActiveEnemies() == 0 && ActiveBosses() == 0, "no enemies in the choice area");
        // YESを少し → NOを少し → (迷う) → 最後に選ぶ方を壊す
        yield return AttackTimes(PlayerController.FlickDirection.Forward, 3);
        int yesHits = choice.Yes.Hits;
        yield return AttackTimes(PlayerController.FlickDirection.Backward, 3);
        int noHits = choice.No.Hits;
        L($"  choice: after 3 forward + 3 backward attacks: YES {choice.Yes.Hits}/{choice.Yes.MaxHp} (crack {choice.Yes.Letters[0].CrackStage}), NO {choice.No.Hits}/{choice.No.MaxHp} (crack {choice.No.Letters[0].CrackStage})");
        Check(yesHits >= 2 && noHits >= 2 && !choice.Decided, "both YES and NO can be damaged part way without deciding");
        Shot("choice_both_damaged");
        yield return new WaitForSeconds(1.5f);
        var dirF = chooseYes ? PlayerController.FlickDirection.Forward : PlayerController.FlickDirection.Backward;
        float wt = 0f;
        while (!choice.Decided && wt < 30f) { yield return AttackTimes(dirF, 1); wt += 0.35f; }
        Check(choice.Decided && choice.ChoseYes == chooseYes, $"the last hit decides ({(chooseYes ? "YES" : "NO")})");
        Shot(chooseYes ? "choice_yes_broken" : "choice_no_broken");
        // もう一方は受け付けない
        int ignored0 = ChoiceWord.LockedHitsIgnored;
        var other = chooseYes ? choice.No : choice.Yes;
        int otherHits = other.Hits;
        yield return AttackTimes(chooseYes ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward, 2);
        Check(other.Hits == otherHits, $"the other choice can no longer be damaged (hits stayed {otherHits}, ignored {ChoiceWord.LockedHitsIgnored - ignored0})");

        if (chooseYes)
        {
            float d0 = gm.MaxDistance;
            yield return new WaitForSeconds(8f);
            float d1 = gm.MaxDistance;
            L($"  beyond: distance {d0:F0} -> {d1:F0}m in 8s, speed {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h, state {flow.Current}, bgm {(AudioManager.Instance != null ? AudioManager.Instance.GetComponent<BgmDirector>()?.Reason : "-")}");
            Shot("beyond");
            Check(flow.Current == LastDungeonFlow.State.Beyond && pc.autoRunEnabled && d1 - d0 > 40f, "YES: the run continues (auto-run and distance resume)");
            yield return new WaitForSeconds(10f);
            float d2 = gm.MaxDistance;
            Check(d2 - d1 > 100f && !gm.IsGameOver, $"BEYOND: keeps running ({d1:F0} -> {d2:F0}m in 10s)");
            L($"  beyond after 18s: {d2:F0}m, active enemies {ActiveEnemies()}, bosses {ActiveBosses()} | {PerfLine()}");
        }
    }

    IEnumerator AttackTimes(PlayerController.FlickDirection f, int n)
    {
        for (int i = 0; i < n; i++)
        {
            yield return Flick(f);
            yield return new WaitForSeconds(0.45f);
        }
    }

    // ===================================================================== //
    // stop: NO → 減速 → 停止 → ホーム
    // ===================================================================== //
    IEnumerator StopMode()
    {
        string ch = Arg("-ldChar", "gunslinger");
        ReaperFinaleBattle.DebugHpOverride = 12;
        yield return BeginRun(ch);
        var flow = LastDungeonFlow.Instance;
        gm.DebugWarpToDistance(98990f);
        warpedTo = 98990f;
        L($"[stop] char={ch} warp to 98,990m (sisters' HP 12 for this test)");
        yield return RushAndSilence(flow, false);
        yield return Finale(flow);
        yield return CreditsAndChoice(flow, false);
        // 減速 → 停止
        var speeds = new List<float>();
        float t0 = Time.time; bool stopped = false; float stopT = -1f;
        double bestBefore = gm.GetStageBest(LastCorridorDirector.StageId);
        while (Time.time - t0 < 20f && GameManager.Instance == gm && !gm.QuietFinish)
        {
            speeds.Add(pc.CurrentAutoRunSpeed);
            if (!stopped && flow.StoppedAtDistance > 0f) { stopped = true; stopT = Time.time; L($"  stopped at {flow.StoppedAtDistance:F1}m after {Time.time - t0:F1}s"); }
            if (stopped && pc.IsFinishing && Time.time - stopT > 1.2f && Time.time - stopT < 1.3f) Shot("no_stopped_pose");
            yield return null;
        }
        float maxRun = speeds.Count > 0 ? speeds.Max() : 0f;
        int peak = speeds.IndexOf(maxRun);
        bool monotoneDown = true;
        for (int i = peak + 1; i < speeds.Count; i++) if (speeds[i] > speeds[i - 1] + 0.05f) { monotoneDown = false; break; }
        L($"  NO: ran up to {maxRun:F1}m/s then {(monotoneDown ? "slowed down smoothly" : "speed jumped")} to {(speeds.Count > 0 ? speeds[speeds.Count - 1] : -1f):F2}m/s");
        Check(maxRun > 3f && monotoneDown && stopped, "NO: runs a little, slows down, and stops");
        Check(gm.QuietFinish && gm.IsWin && gm.IsGameOver, "NO: formal finish (IsWin, no game over screen)");
        double bestAfter = gm.GetStageBest(LastCorridorDirector.StageId);
        L($"  stage best {bestBefore:F0} -> {bestAfter:F0}");
        // ホームへ
        float w = 0f;
        var old = gm;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        var home = GameManager.Instance;
        Check(home != null && home != old && !home.HasStarted, "NO: faded out and returned to HOME");
        Shot("home_after_no");
        keepAlive = false;
    }
}
#endif

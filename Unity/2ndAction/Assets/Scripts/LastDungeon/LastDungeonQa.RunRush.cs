#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 走りながらのボスラッシュ(2026-10-06)の自動確認。 -ldQa <dir> -ldQaMode runrush [-ldRushOld 1](旧方式の FPS も測る)
//  L 0〜89km の関門は今まで通り(距離が止まる/遭遇は1関門)
//  A 90km: 距離を止めずにボスA        B 91km: A が残っていれば B を追加        C 3体の時の次の節目は待機
//  D 1体倒すと待機が出る             E 高速で節目を跨いでも順番に全部予約       F 98km が最後の増援
//  H 99km にボスが残っていれば足止め → 全員倒すと静寂   I 99〜100km はボス/雑魚なし   J 100km で三姉妹戦
//  G 99km に着いた時にボス0 → そのまま静寂(別のラン)    K 3体+飛び道具の負荷/出現の瞬間の FPS(旧方式と比べる)
//  追従: ボスごとに プレイヤーとの距離(最小/最大)/画面外の時間/置き去り/先へ行きすぎ/落下 を記録
public partial class LastDungeonQa
{
    BossManager.BossEncounter Enc => BossManager.Instance != null ? BossManager.Instance.CurrentBossEncounter : null;
    List<WildBossBase> AliveRush() => BossManager.Instance.ActiveBosses.Where(x => x != null && !x.IsDead).ToList();

    // ボスの予定を変えずに距離だけを先へ(高速で節目を跨いだ状態): DebugWarpToDistance と同じ動かし方で、ボスの関門は戻さない
    void JumpKeepBosses(float d)
    {
        float excl = (float)typeof(GameManager).GetField("distanceExclusionOffset", NP).GetValue(gm);
        float cur = pc.DistanceFromStart;
        gm.ClampMaxDistanceTo(d);
        FloatingOrigin.LogicalWarp(d + excl - cur);
    }

    // 追従の記録
    class Follow { public string name; public float minDx = 999f, maxDx = -999f, off, total, behindLong, aheadLong, behindT, aheadT, minDy = 999f; public bool fell; }
    readonly Dictionary<WildBossBase, Follow> follow = new Dictionary<WildBossBase, Follow>();
    void TrackFollow()
    {
        var cam = Camera.main; if (cam == null || pc == null) return;
        float hw = cam.orthographicSize * cam.aspect, cx = cam.transform.position.x;
        float dt = Time.deltaTime;
        foreach (var b in AliveRush())
        {
            if (!follow.TryGetValue(b, out var f)) { f = new Follow { name = b.bossName }; follow[b] = f; }
            float dx = b.transform.position.x - pc.transform.position.x;
            float dy = b.transform.position.y - pc.transform.position.y;
            f.minDx = Mathf.Min(f.minDx, dx); f.maxDx = Mathf.Max(f.maxDx, dx); f.minDy = Mathf.Min(f.minDy, dy);
            f.total += dt;
            bool offscreen = Mathf.Abs(b.CenterWorld.x - cx) > hw + 2f;
            if (offscreen) f.off += dt;
            f.behindT = dx < -25f ? f.behindT + dt : 0f; f.behindLong = Mathf.Max(f.behindLong, f.behindT);
            f.aheadT = dx > 40f ? f.aheadT + dt : 0f; f.aheadLong = Mathf.Max(f.aheadLong, f.aheadT);
            if (dy < -20f) f.fell = true;
        }
    }

    // 出現の瞬間の FPS(出現の前後1秒の最小)
    readonly List<(float t, float fps)> frameLog = new List<(float, float)>();
    readonly List<float> spawnTimes = new List<float>();
    readonly List<float> spawnMsList = new List<float>();
    int lastSpawnCount, spikeLogs, lastGc;
    void SampleFrame()
    {
        frameLog.Add((Time.unscaledTime, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)));
        int gcNow = System.GC.CollectionCount(0);
        if (Time.unscaledDeltaTime > 0.05f && spikeLogs < 40)
        {
            spikeLogs++;
            L($"[spike] {Time.unscaledDeltaTime * 1000f:F0}ms d={gm.MaxDistance:F0} alive={AliveRush().Count} spawnedThisFrame={(Enc != null && Enc.spawned > lastSpawnCount)} reward={gm.IsRewardSequenceRunning} levelUp={gm.DebugChoiceOpen} ts={Time.timeScale:F2} gc0+{gcNow - lastGc} objs={FindObjectsByType<Transform>(FindObjectsSortMode.None).Length}");
        }
        lastGc = gcNow;
        var enc = Enc;
        int sp = enc != null ? enc.spawned : 0;
        if (sp > lastSpawnCount) { spawnTimes.Add(Time.unscaledTime); spawnMsList.Add(BossManager.LastBossSpawnMs); }
        lastSpawnCount = sp;
    }
    (float min, float avg, float spawnMin, float spawnMsMax) FrameStats()
    {
        float min = frameLog.Count > 0 ? frameLog.Min(x => x.fps) : 0f, avg = frameLog.Count > 0 ? frameLog.Average(x => x.fps) : 0f;
        float smin = 999f;
        foreach (var st in spawnTimes) foreach (var f in frameLog) if (f.t >= st - 0.05f && f.t <= st + 1f) smin = Mathf.Min(smin, f.fps);
        return (min, avg, smin >= 999f ? 0f : smin, spawnMsList.Count > 0 ? spawnMsList.Max() : 0f);
    }
    void ResetFrames() { frameLog.Clear(); spawnTimes.Clear(); spawnMsList.Clear(); lastSpawnCount = Enc != null ? Enc.spawned : 0; }

    // 走らせながら待つ(毎フレーム 追従/FPS/雑魚/上限 を見る)
    int rrOverCap, rrTagBad, rrZako;
    IEnumerator RunFor(System.Func<bool> until, float timeout)
    {
        var ld = LastDungeonBossTuning.I;
        float w = 0f;
        while (!until() && w < timeout)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            TrackFollow(); SampleFrame();
            var alive = AliveRush();
            if (alive.Count > ld.maxSimultaneous) rrOverCap++;
            if (!RushTagsOk(alive, ld)) rrTagBad++;
            rrZako = Mathf.Max(rrZako, OnScreenEnemies());
        }
    }

    // 旧方式/新方式の FPS を同じ条件で(新しいプロセスで 89.9km → 93km、初めて出るボスの読み込みも含む)。 -ldRushPerfOnly old|new
    IEnumerator RushPerfOnly(string which)
    {
        var ld = LastDungeonBossTuning.I;
        ld.continuousRush = which != "old";
        yield return BeginRun(Arg("-ldChar", "swordsman"));
        var bm = BossManager.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.NetTestBossHpOverride = 4000000;
        WarpQa(89900f);
        ResetFrames(); lastSpawnCount = 0;
        float t = 0f, lastK = Time.time; int lastGate = 0, spawnsTotal = 0;
        while (gm.MaxDistance < 93000f && t < 240f)
        {
            yield return null; t += Time.unscaledDeltaTime; SampleFrame();
            if (Enc != null && Enc.spawned < lastSpawnCount) lastSpawnCount = 0; // 旧方式: 関門ごとに遭遇が新しくなる
            if (!ld.continuousRush)
            {
                // 旧方式: 関門のボスは 6秒ごとに1体倒す(関門が終わって次へ進む)
                if (bm.RushGateK != lastGate) { lastGate = bm.RushGateK; }
                if (bm.IsBossPhase && Time.time - lastK > 6f) { var a = AliveRush(); if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); lastK = Time.time; } }
            }
        }
        var st = FrameStats();
        L($"[perf-{which}] 89.9->{gm.MaxDistance:F0}m in {t:F0}s: fps avg {st.avg:F0} min {st.min:F0}, at boss spawns min {st.spawnMin:F0}, spawn cost max {st.spawnMsMax:F1}ms (spawns {spawnTimes.Count}: [{string.Join(",", spawnMsList.Select(x => x.ToString("F0")))}]ms), max bosses together {BossManager.MaxBossesTogetherSeen}");
        BossManager.NetTestBossHpOverride = 0;
        ld.continuousRush = true;
    }

    // 1体のボスを走りながらのボスラッシュで出して、状態を0.5秒ごとに記録する(Worm が近づかない件の調査)。 -ldRushProbe Cave/Worm
    IEnumerator RushProbe(string key)
    {
        yield return BeginRun(Arg("-ldChar", "swordsman"));
        var bm = BossManager.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.NetTestBossHpOverride = 4000000;
        WarpQa(89900f);
        float w = 0f;
        while (!(bm.IsBossPhase && Enc != null && Enc.continuous) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        bm.DebugSetRushPlan(new List<string> { Enc.plan[0], key, key, key, key, key, key, key, key });
        yield return RunFor(() => AliveRush().Count > 0, 8f);
        foreach (var a in AliveRush()) a.TakeDamage(99999999, a.CenterWorld);
        yield return RunFor(() => gm.MaxDistance >= 91000f, 80f);
        yield return RunFor(() => AliveRush().Count > 0, 8f);
        var b = AliveRush().FirstOrDefault();
        if (b == null) { L("[probe] no boss"); yield break; }
        var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        System.Func<string, object> get = n => { var t = b.GetType(); while (t != null) { var f = t.GetField(n, F); if (f != null) return f.GetValue(b); var pr = t.GetProperty(n, F); if (pr != null) return pr.GetValue(b); t = t.BaseType; } return "?"; };
        for (int i = 0; i < 60; i++)
        {
            L($"[probe] t={i * 0.5f:F1}s {b.bossName} dx={b.transform.position.x - pc.transform.position.x:F1} gap={get("Gap")} startGap={get("startGap")} entering={get("entering")} buried={get("buried")} invuln={get("invulnerable")} relV={get("relVelocity")} freeGap={get("freeGap")} y={b.transform.position.y - pc.transform.position.y:F1} slot={b.slotIndex} hp={b.Hp} kmh={pc.CurrentRunKmh:F0}");
            yield return RunFor(() => false, 0.5f);
        }
        BossManager.NetTestBossHpOverride = 0;
    }

    // ボスラッシュに入れるボスを順に出して、走りながら「当てられる時間の割合」(画面内 かつ 無敵でない)を測る。 -ldRushProbeAll 0|1|2(9体ずつ)
    IEnumerator RushProbeAll(int part)
    {
        var ld = LastDungeonBossTuning.I;
        var keys = ld.tags.Where(t => (t.tags & LastDungeonBossTuning.Tag.NoRush) == 0).Select(t => t.key).Skip(part * 8).Take(8).ToList();
        yield return BeginRun(Arg("-ldChar", "swordsman"));
        var bm = BossManager.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.NetTestBossHpOverride = 4000000;
        WarpQa(89900f);
        float w = 0f;
        while (!(bm.IsBossPhase && Enc != null && Enc.continuous) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        var plan = new List<string> { Enc.plan[0] }; plan.AddRange(keys); while (plan.Count < 9) plan.Add(keys[0]);
        bm.DebugSetRushPlan(plan);
        // 90km の分は抽選済みなので、最初に出た1体は倒してから
        yield return RunFor(() => AliveRush().Count > 0, 8f);
        foreach (var x in AliveRush()) x.TakeDamage(99999999, x.CenterWorld);
        var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var invF = typeof(WildBossBase).GetField("invulnerable", F);
        var cam = Camera.main;
        foreach (var key in keys)
        {
            yield return RunFor(() => false, 0.5f);
            bm.DebugRushAdvance(); // 次の節目の分を今すぐ予約 → 予告の後に出る
            yield return RunFor(() => AliveRush().Count > 0, 8f);
            var b = AliveRush().FirstOrDefault();
            if (b == null) { L($"[probeall] {key}: did not spawn"); Check(false, $"probe {key} spawned"); continue; }
            float t = 0f, hittable = 0f, minDx = 999f, maxDx = -999f;
            while (t < 12f && b != null && !b.IsDead)
            {
                yield return null;
                float dt = Time.deltaTime; t += dt;
                float hw = cam.orthographicSize * cam.aspect, cx = cam.transform.position.x;
                bool onScreen = Mathf.Abs(b.CenterWorld.x - cx) < hw;
                bool inv = (bool)invF.GetValue(b);
                if (onScreen && !inv) hittable += dt;
                float dx = b.transform.position.x - pc.transform.position.x; minDx = Mathf.Min(minDx, dx); maxDx = Mathf.Max(maxDx, dx);
            }
            float frac = t > 0f ? hittable / t : 0f;
            L($"[probeall] {key,-18} hittable {frac * 100f:F0}% of {t:F1}s, dx {minDx:F1}..{maxDx:F1}m");
            Check(frac >= 0.25f, $"probe {key}: hittable at least 25% of the time while running ({frac * 100f:F0}%)");
            if (b != null && !b.IsDead) b.TakeDamage(99999999, b.CenterWorld);
            yield return RunFor(() => false, 1.5f);
        }
        BossManager.NetTestBossHpOverride = 0;
    }

    IEnumerator RunRushMode()
    {
        string probeAll = Arg("-ldRushProbeAll", "");
        if (probeAll != "") { yield return RushProbeAll(int.Parse(probeAll)); yield break; }
        string probe = Arg("-ldRushProbe", "");
        if (probe != "") { yield return RushProbe(probe); yield break; }
        string perfOnly = Arg("-ldRushPerfOnly", "");
        if (perfOnly != "") { yield return RushPerfOnly(perfOnly); yield break; }
        string ch = Arg("-ldChar", "swordsman");
        var ld = LastDungeonBossTuning.I;
        ld.continuousRush = true;
        yield return BeginRun(ch);
        var bm = BossManager.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.NetTestBossHpOverride = 4000000; // ボスはテストが倒す時だけ倒れる(補助の自動攻撃で数が変わらないように)
        int exc0 = exceptions.Count;

        // ---- L: 0〜89km の関門は今まで通り
        WarpQa(20960f);
        float wl = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && wl < 25f) { yield return null; wl += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(2f);
        float dl0 = gm.MaxDistance; yield return new WaitForSeconds(1.5f); float dl1 = gm.MaxDistance;
        L($"[L] 21km gate: boss={bm.IsBossPhase} holds={bm.HoldsRun} continuous={Enc?.continuous} d {dl0:F0}->{dl1:F0}");
        Check(bm.IsBossPhase && bm.HoldsRun && Enc != null && !Enc.continuous && Mathf.Abs(dl1 - dl0) < 1f, "L: a 0-89km milestone gate still stops the distance (unchanged)");
        yield return KillAllQa(bm);

        // ---- A: 90km
        yield return WaitNoBoss(bm);
        WarpQa(89900f);
        float wa = 0f;
        while (!(bm.IsBossPhase && Enc != null && Enc.continuous) && wa < 30f) { yield return null; wa += Time.unscaledDeltaTime; }
        var enc = Enc;
        if (enc == null || !enc.continuous) { Check(false, "A: the run rush starts at 90km"); yield break; }
        // 91km 以降の並びを決めておく(同時/タグの規則の確認を毎回同じにする)
        bm.DebugSetRushPlan(new List<string> { enc.plan[0], "Cave/Mole", "Cave/Scorpion", "Wild/Cyclops", "Wild/Golem", "Wild/Spider", "Cave/Basilisk", "Cave/Worm", "Wild/BlackKnight" });
        L($"[A] start at {gm.MaxDistance:F0}m: {enc.label} -> plan {string.Join(" > ", enc.plan)} | resumed={bm.RunResumed} holds={bm.HoldsRun}");
        ResetFrames();
        float da0 = gm.MaxDistance;
        yield return RunFor(() => AliveRush().Count >= 1, 6f);
        float da1 = gm.MaxDistance;
        Check(bm.RunResumed && !bm.HoldsRun && da1 > da0 + 20f, $"A: at 90km the distance keeps going ({da0:F0} -> {da1:F0}m)");
        Check(AliveRush().Count == 1 && enc.spawnedKeys.Count == 1 && enc.spawnedKeys[0] == enc.plan[0], $"A: boss A appears ({string.Join(",", enc.spawnedKeys)}) after a telegraph ({bm.RushTelegraphs})");
        Shot("runrush_A_90km");

        // ---- B: A が残ったまま 91km → B
        yield return RunFor(() => gm.MaxDistance >= 91000f, 80f);
        yield return RunFor(() => AliveRush().Count >= 2, 8f);
        L($"[B] {gm.MaxDistance:F0}m alive {AliveRush().Count} spawned [{string.Join(",", enc.spawnedKeys)}] queue {enc.Queued}");
        Check(AliveRush().Count == 2 && enc.spawnedKeys.Count == 2, "B: with A alive, B is added at 91km");
        Shot("runrush_B_91km");

        // ---- C: 92km で3体 → 93km は待機
        yield return RunFor(() => gm.MaxDistance >= 93000f, 90f);
        yield return RunFor(() => false, 5f);
        int aliveC = AliveRush().Count;
        L($"[C] {gm.MaxDistance:F0}m alive {aliveC} queue {enc.Queued} reserved {enc.milestonesReserved} spawned [{string.Join(",", enc.spawnedKeys)}] deferred {enc.deferredByRules}");
        Check(aliveC == ld.maxSimultaneous && enc.Queued >= 1 && enc.milestonesReserved == 4, $"C: with {ld.maxSimultaneous} bosses alive the 93km boss waits in the queue (alive {aliveC}, queue {enc.Queued})");
        Shot("runrush_C_3bosses");
        // ---- K(新方式): 3体と戦いながら 8秒(飛び道具などの最大負荷)
        var kFrames = new List<float>();
        float kt = 0f;
        while (kt < 8f) { yield return null; kt += Time.unscaledDeltaTime; kFrames.Add(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)); TrackFollow(); }
        L($"[K] 3 bosses alive for 8s: fps avg {kFrames.Average():F0} min {kFrames.Min():F0} (projectiles {FindObjectsByType<KitProjectile>(FindObjectsSortMode.None).Length + FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None).Length} bodies)");

        // ---- D: 1体倒すと待機が出る
        int queued0 = enc.Queued, spawned0 = enc.spawned;
        var victim = AliveRush()[0];
        victim.TakeDamage(99999999, victim.CenterWorld);
        yield return RunFor(() => enc.spawned > spawned0, ld.minSpawnSpacingSeconds + ld.telegraphSeconds + 4f);
        L($"[D] killed {victim.bossName}: spawned {spawned0} -> {enc.spawned}, queue {queued0} -> {enc.Queued}, alive {AliveRush().Count}");
        Check(enc.spawned == spawned0 + 1 && enc.Queued == queued0 - 1, "D: a defeated boss frees a slot and the queued boss comes in");

        // ---- E: 高速で節目を跨ぐ(93.x → 95,050m: 94/95km を順番に予約)
        int res0 = enc.milestonesReserved;
        float before = gm.MaxDistance;
        JumpKeepBosses(95050f);
        yield return null; yield return null;
        var q = enc.waiting.Select(s => s.value).ToList();
        L($"[E] jumped {before:F0} -> {gm.MaxDistance:F0}m: reserved {res0} -> {enc.milestonesReserved}, catch-up max {enc.maxCatchUp}, queue kms [{string.Join(",", q)}]");
        Check(enc.milestonesReserved == res0 + 2 && enc.maxCatchUp >= 2 && q.Count >= 2 && q[q.Count - 2] == 94f && q[q.Count - 1] == 95f, "E: crossing 94km and 95km in one jump reserves both, in order");

        // ---- F: 98km が最後の増援(倒しながら進む)
        float lastKill = Time.time;
        yield return RunFor(() =>
        {
            if (Time.time - lastKill > 9f) { var a = AliveRush(); if (a.Count >= 2) { a[0].TakeDamage(99999999, a[0].CenterWorld); lastKill = Time.time; } }
            return gm.MaxDistance >= 98300f;
        }, 200f);
        L($"[F] {gm.MaxDistance:F0}m: reserved {enc.milestonesReserved}/{enc.total}, last reinforcement {enc.lastReinforcementDone}, nextK {enc.nextK}, alive {AliveRush().Count}, queue {enc.Queued}, defeated {enc.defeated}");
        Check(enc.lastReinforcementDone && enc.milestonesReserved == 9 && enc.nextK == 99, "F: 98km is the last reinforcement (9 bosses reserved, nothing after)");

        // ---- H: 99km にボスが残っている → 足止め → 全員倒す → 静寂
        yield return RunFor(() => gm.MaxDistance >= 99000f || enc.held99, 60f);
        yield return RunFor(() => false, 2f);
        float dh0 = gm.MaxDistance; yield return RunFor(() => false, 2f); float dh1 = gm.MaxDistance;
        L($"[H] held={enc.held99} holds={bm.HoldsRun} d {dh0:F0}->{dh1:F0}, remaining {enc.Remaining} (alive {AliveRush().Count}, queue {enc.Queued})");
        Check(enc.held99 && bm.HoldsRun && Mathf.Abs(dh1 - dh0) < 1f && dh0 <= 99000.5f, "H: bosses remaining at 99km stop the run there");
        Shot("runrush_H_99km_hold");
        // 全員(待機中も)倒す
        float wk = 0f;
        while (bm.IsBossPhase && !bm.BossDefeatedThisPhase && wk < 60f)
        {
            foreach (var a in AliveRush()) a.TakeDamage(99999999, a.CenterWorld);
            yield return RunFor(() => false, 0.5f);
            wk += 0.5f;
        }
        Check(bm.BossDefeatedThisPhase || !bm.IsBossPhase, $"H: the whole rush can be cleared (defeated {enc.defeated}/{enc.total})");
        yield return WaitNoBoss(bm);
        yield return RunFor(() => LastDungeonFlow.Instance != null && LastDungeonFlow.Instance.Current == LastDungeonFlow.State.Silence, 10f);
        float ds0 = gm.MaxDistance; yield return new WaitForSeconds(1.5f); float ds1 = gm.MaxDistance;
        L($"[H] after the last boss: flow={LastDungeonFlow.Instance?.Current} d {ds0:F0}->{ds1:F0}");
        Check(LastDungeonFlow.Instance != null && LastDungeonFlow.Instance.Current == LastDungeonFlow.State.Silence && ds1 > ds0 + 5f, "H: after the last boss the silence starts and the run moves again");
        var st = FrameStats();
        string newPerf = $"run rush: fps avg {st.avg:F0} min {st.min:F0}, at boss spawns min {st.spawnMin:F0}, spawn cost max {st.spawnMsMax:F1}ms (spawns {spawnTimes.Count}, each [{string.Join(",", spawnMsList.Select(x => x.ToString("F0")))}]ms), K 3 bosses avg {kFrames.Average():F0} min {kFrames.Min():F0}";
        L("[K] " + newPerf);

        // ---- 追従
        int behind = 0, ahead = 0, fell = 0;
        foreach (var f in follow.Values)
        {
            L($"[follow] {f.name,-14} dx {f.minDx:F1}..{f.maxDx:F1}m  minDy {f.minDy:F1}  offscreen {f.off:F1}/{f.total:F1}s  behind>25m {f.behindLong:F1}s  ahead>40m {f.aheadLong:F1}s{(f.fell ? "  FELL" : "")}");
            if (f.behindLong > 3f) behind++;
            if (f.aheadLong > 3f) ahead++;
            if (f.fell) fell++;
        }
        Check(behind == 0, $"follow: no boss is left behind (>25m for >3s) ({behind})");
        Check(ahead == 0, $"follow: no boss runs too far ahead (>40m for >3s) ({ahead})");
        Check(fell == 0, $"follow: no ground boss falls out of the world ({fell})");
        L($"[rush] over-cap frames {rrOverCap}, tag-rule frames {rrTagBad}, zako on screen max {rrZako}, cleared {bm.RushZakoCleared}, max together {BossManager.MaxBossesTogetherSeen}");
        Check(rrOverCap == 0 && rrTagBad == 0, "max 3 together and the tag rules hold during the run rush");
        Check(rrZako == 0, "no normal enemies on screen during the run rush");

        // ---- I: 99〜100km はボス/雑魚なし
        PlayerController.DebugSpeedScale = 3f;
        int bossFrames = 0, zakoFrames = 0;
        float wi = 0f;
        while (gm.MaxDistance < 99940f && wi < 60f) { yield return null; wi += Time.deltaTime; if (ActiveBosses() > 0) bossFrames++; if (OnScreenEnemies() > 0) zakoFrames++; }
        PlayerController.DebugSpeedScale = 1f;
        L($"[I] 99-100km: boss frames {bossFrames}, zako frames {zakoFrames}, d={gm.MaxDistance:F0}");
        Check(bossFrames == 0 && zakoFrames == 0, "I: no bosses and no normal enemies between 99km and 100km");
        // ---- J: 100km の三姉妹
        BossManager.NetTestBossHpOverride = 0;
        float wj = 0f;
        while (FindObjectsByType<ReaperSisterBoss>(FindObjectsSortMode.None).Length == 0 && wj < 30f) { yield return null; wj += Time.deltaTime; }
        L($"[J] at {gm.MaxDistance:F0}m: sisters {FindObjectsByType<ReaperSisterBoss>(FindObjectsSortMode.None).Length}, flow={LastDungeonFlow.Instance?.Current}");
        Check(FindObjectsByType<ReaperSisterBoss>(FindObjectsSortMode.None).Length > 0 && LastDungeonFlow.Instance.Current == LastDungeonFlow.State.Finale, "J: the reaper sisters fight starts at 100km");
        Shot("runrush_J_sisters");

        // ---- G: 99km に着いた時にボス0 → そのまま静寂(別のラン)
        keepAlive = false;
        gm.Retry();
        yield return new WaitForSecondsRealtime(2f);
        yield return BeginRun(ch);
        bm = BossManager.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpQa(98500f);
        float wg = 0f;
        while (!BossManager.RushEnabled && wg < 5f) { yield return null; wg += Time.deltaTime; }
        bm.DebugStartRushLate(98);
        yield return RunFor(() => AliveRush().Count > 0, 8f);
        foreach (var a in AliveRush()) a.TakeDamage(99999999, a.CenterWorld);
        var encG = Enc;
        yield return WaitNoBoss(bm);
        yield return RunFor(() => gm.MaxDistance >= 99050f, 40f);
        L($"[G] cleared before 99km: held={(encG != null && encG.held99)} flow={LastDungeonFlow.Instance?.Current} d={gm.MaxDistance:F0}");
        Check(encG != null && !encG.held99 && LastDungeonFlow.Instance.Current == LastDungeonFlow.State.Silence, "G: no bosses at 99km -> straight into the silence (no hold)");

        // ---- K(旧方式): 同じ距離で関門ごとに止める方式の FPS(比べる)
        if (Arg("-ldRushOld", "1") == "1")
        {
            keepAlive = false;
            gm.Retry();
            yield return new WaitForSecondsRealtime(2f);
            ld.continuousRush = false;
            yield return BeginRun(ch);
            bm = BossManager.Instance;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            BossManager.NetTestBossHpOverride = 4000000;
            WarpQa(89900f);
            ResetFrames(); lastSpawnCount = 0;
            float wo = 0f;
            while (bm.RushGateK != 90 && wo < 30f) { yield return null; wo += Time.unscaledDeltaTime; }
            for (int k = 90; k <= 92; k++)
            {
                if (bm.RushGateK != k) { yield return WaitNoBoss(bm); WarpQa(k * 1000f - 40f); float ww = 0f; while (bm.RushGateK != k && ww < 30f) { yield return null; ww += Time.unscaledDeltaTime; } }
                var oe = Enc;
                lastSpawnCount = 0;
                float tg = 0f, lastK = Time.time;
                while (bm.IsBossPhase && !bm.BossDefeatedThisPhase && tg < 90f)
                {
                    yield return null; tg += Time.unscaledDeltaTime; SampleFrame();
                    if (Time.time - lastK > 4f) { var a = AliveRush(); if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); lastK = Time.time; } }
                }
                L($"[K-old] gate {k}km: spawned {oe?.spawned}/{oe?.total} in {tg:F0}s");
            }
            var so = FrameStats();
            string oldPerf = $"gate rush (old): fps avg {so.avg:F0} min {so.min:F0}, at boss spawns min {so.spawnMin:F0}, spawn cost max {so.spawnMsMax:F1}ms (spawns {spawnTimes.Count}, each [{string.Join(",", spawnMsList.Select(x => x.ToString("F0")))}]ms)";
            L("[K] " + oldPerf);
            L("[K] " + newPerf);
            ld.continuousRush = true;
        }
        BossManager.NetTestBossHpOverride = 0;
        Check(exceptions.Count == exc0, "no exceptions");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
    }
}
#endif

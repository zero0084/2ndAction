#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 自然洞窟ボス強化(2026-10-04)の確認。 -qaCaveBoss <dir> [-qaCaveOnly Centipede,Mole...] [-qaCaveVideo 1]
// 各ボス: A 初登場 / B 通常攻撃 / C 段階の移行+第2段階の技 / D 必殺技 / E 必殺技が避けられる(床と天井が同時に来ない・判断2回以上)
//         F 必殺技後の隙 / G 崩し(BREAK)と復帰 / H ラン再開(ボスが残って追ってくる)
// 全体:   I 雑魚との混在(ラン再開後に雑魚・必殺技中は雑魚を出さない/雑魚の攻撃を遅らせる) / J 再戦の抽選(撃破済みから)
//         K 高距離の再戦(段階が増える・崩しが大きい) / L 高速/低速(地形の攻撃が走行の座標系で流れる・必殺技が成立)
//         M ボスが残ったまま次の関門を通過(重ならない) / N 撃破後に保留の関門が1つだけ出る / O 例外なし
//         P 使い回し(大量生成をしない)
public partial class QaSweep
{
    struct OverRec { public float t; public bool ceil; public string src; }
    readonly List<OverRec> caveOver = new List<OverRec>();
    readonly List<string> caveRecoveries = new List<string>();
    bool caveHooked;
    int caveEncPlanned;

    void HookCaveLogs()
    {
        if (caveHooked) return;
        caveHooked = true;
        CaveHazard.OverPlayer += (ceil, src) => caveOver.Add(new OverRec { t = Time.time, ceil = ceil, src = src });
        Application.logMessageReceived += (c, tr, type) => { if (c.StartsWith("[ENCOUNTER] #") && !c.Contains("members=0")) caveEncPlanned++; };
        Application.logMessageReceived += (c, tr, type) => { if (c.StartsWith("[CaveBoss] ") && c.Contains(" RECOVERY ")) caveRecoveries.Add(c); };
    }

    WildBossBase CaveBossAlive() => WildAlive().FirstOrDefault();

    // ボス本体の判定(突進/薙ぎ払い等)がプレイヤーの列に重なり始めた瞬間も「判断」として記録する
    readonly HashSet<BossHitbox> hbOver = new HashSet<BossHitbox>();
    void TrackBossHitboxes(WildBossBase b)
    {
        if (b == null || pc == null) return;
        float px = pc.transform.position.x;
        float g = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(px) ?? pc.transform.position.y) : pc.transform.position.y;
        foreach (var hb in b.GetComponentsInChildren<BossHitbox>(true))
        {
            var c = hb != null ? hb.GetComponent<Collider2D>() : null;
            bool over = hb != null && hb.IsActive && c != null && c.bounds.min.x - 0.4f <= px && c.bounds.max.x + 0.4f >= px;
            if (over && !hbOver.Contains(hb)) { hbOver.Add(hb); caveOver.Add(new OverRec { t = Time.time, ceil = c.bounds.min.y > g + 1.9f, src = "body:" + hb.name }); }
            else if (!over) hbOver.Remove(hb);
        }
    }
    // 残りHPぶんを1発で(試験はボスHPを大きく固定する)
    void KillCaveBosses() { foreach (var b in WildAlive()) b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld); }

    IEnumerator CaveBossMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        HookCaveLogs();
        Random.InitState(20261004);
        BossManager.NetTestBossHpOverride = 50000000; // 試験側が倒し方を決める(段階は割合なのでそのまま)
        yield return BeginRun("swordsman", "natural_cave");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var bm = Bm;
        bm.DebugPoolReset();
        var tn = BossBattleTuning.I;
        L($"[tuning] resumeStages=[{string.Join(",", tn.resumeStages)}] caveEntries={tn.entries.Count(e => System.Enum.IsDefined(typeof(CaveBossKind), e.key))}/11 battleTuned=[{string.Join(",", BossManager.BattleTunedStages)}]");
        Check(System.Array.IndexOf(tn.resumeStages, "natural_cave") >= 0, "run resume is enabled in the natural cave");
        Check(tn.entries.Count(e => System.Enum.IsDefined(typeof(CaveBossKind), e.key)) == 11, "all 11 cave bosses have battle tuning (phases/ultimate/stagger)");
        WarpTo(1500f);
        yield return new WaitForSeconds(1f);
        int spawned0 = CaveHazard.Spawned;

        string only = Arg("-qaCaveOnly", "");
        foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind)))
        {
            if (only != "" && !only.Split(',').Contains(k.ToString())) continue;
            yield return OneCaveBoss(k);
        }

        // ---- I: 雑魚との混在 ----
        yield return CaveZakoCase();
        // ---- J: 再戦の抽選(撃破済みから) ----
        yield return CaveRematchCase();
        // ---- K: 高距離の再戦(最終強化再戦) ----
        yield return CaveHighTierCase();
        // ---- L: 高速/低速 ----
        yield return CaveSpeedCase("L-fast", 300f);
        yield return CaveSpeedCase("L-slow", 30f);
        // ---- M/N: ボスが残ったまま次の関門を通過 → 撃破後に保留が1つ ----
        yield return CaveGateCarryCase();

        // ---- P: 使い回し ----
        int made = CaveHazard.Spawned - spawned0;
        L($"[pool] hazards built={made} reused={CaveHazard.Reused} live={CaveHazard.LiveCount} delayedBySafety={CaveBossSafety.Delayed} pitSkips={CaveBossSafety.PitSkips} playerHitsWhileInvincible={CaveHazard.PlayerHits}");
        Check(CaveHazard.Reused > made, $"P: terrain attacks are pooled (built {made}, reused {CaveHazard.Reused})");
        Check(CaveHazard.Violations == 0, $"E(all): floor and ceiling attacks never covered the player in the same frame ({CaveHazard.Violations})");
        L($"[summary] resumes={bossLogResumed} ultimates={bossLogUltimates} breaks={bossLogBreaks} pending={bossLogPending} recoveries={caveRecoveries.Count}");
        BossManager.NetTestBossHpOverride = 0;
        yield return EndRun();
    }

    IEnumerator OneCaveBoss(CaveBossKind k)
    {
        string n = k.ToString();
        var bm = Bm;
        var tune = BossBattleTuning.I.For(n);
        bm.DebugCaveEncounter(k, -1);
        yield return WaitBossSpawn(10f);
        var b = CaveBossAlive();
        // A: 初登場
        Check(b != null && b is CaveBossBase, $"{n} A: spawned as a cave boss");
        if (b == null) yield break;
        Check(b.PhaseCount >= 2 && tune.ultimateCooldown > 0f && tune.staggerMax > 0f, $"{n} A: battle tuning applied (phases {b.PhaseCount}, ultimate cd {tune.ultimateCooldown}, stagger {tune.staggerMax})");
        Check(bm.IsBossPhase && !bm.RunResumed, $"{n} A: boss phase holds the run");
        yield return new WaitForSeconds(0.6f);
        Shot($"cave_{n}_A_spawn");

        // B: 通常攻撃(第1段階)
        int hz0 = CaveHazard.Spawned + CaveHazard.Reused;
        int hbFrames = 0; float w = 0f;
        var hbs = b.GetComponentsInChildren<BossHitbox>(true);
        int proj0 = FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length;
        bool sawProj = false;
        while (w < 9f && !b.IsDead)
        {
            if (hbs.Any(h => h != null && h.IsActive)) hbFrames++;
            if (FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length > proj0) sawProj = true;
            if (hbFrames > 0 && w > 3f) break;
            w += Time.deltaTime; yield return null;
        }
        int hzB = CaveHazard.Spawned + CaveHazard.Reused - hz0;
        Check(hbFrames > 0 || hzB > 0 || sawProj, $"{n} B: phase-1 attacks happen (hitbox frames {hbFrames}, terrain attacks {hzB}, projectile {sawProj})");
        Check(b.Phase == 1 && b.UltimatesUsed == 0, $"{n} B: no ultimate in phase 1 (phase {b.Phase}, ultimates {b.UltimatesUsed})");

        // C: 段階の移行 → 第2段階の技
        int sp0 = b.SpecialsUsed;
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(0.4f);
        Check(b.Phase == 2, $"{n} C: phase 2 after the HP threshold (now {b.Phase})");
        Shot($"cave_{n}_C_phase2");
        w = 0f;
        while (w < 16f && b.SpecialsUsed == sp0 && b.UltimatesUsed == 0 && !b.IsDead) { w += Time.deltaTime; yield return null; }
        Check(b.SpecialsUsed > sp0 || b.UltimatesUsed > 0, $"{n} C: a phase-2 move is used ({b.SpecialsUsed - sp0} specials, {b.UltimatesUsed} ultimates in {w:F1}s)");
        // 第2段階の技が終わるまで(必殺技中なら終わるまで)
        w = 0f; while (w < 14f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(2.5f);

        // D/E/F: 必殺技
        int ult0 = b.UltimatesUsed, viol0 = CaveHazard.Violations, rec0 = caveRecoveries.Count;
        w = 0f;
        // 割り込みは1回だけ(毎フレーム呼ぶと始まった必殺技をまた止めてしまう)。2秒で始まらなければもう1回
        b.DebugForceUltimate();
        bool retried = false;
        while (w < 6f && !b.UltimateRunning && b.UltimatesUsed == ult0) { if (!retried && w > 2f) { retried = true; b.DebugForceUltimate(); } w += Time.deltaTime; yield return null; }
        Check(b.UltimateRunning || b.UltimatesUsed > ult0, $"{n} D: ultimate starts");
        float uStart = Time.time;
        caveOver.Clear();
        bool shotA = false, shotB = false;
        bool video = Arg("-qaCaveVideo", "") != "";
        if (video && b.UltimateRunning) StartCoroutine(RecordUltimate("cave_" + n, 10f));
        w = 0f;
        var seenKinds = new HashSet<string>();
        int brU0 = b.BreakCount;
        while (w < 25f && b.UltimateRunning && !b.IsDead)
        {
            foreach (var h in CaveHazard.Live) if (h != null && h.Damaging) seenKinds.Add(h.Kind.ToString());
            if (Arg("-qaCaveTrace", "") != "" && Time.frameCount % 12 == 0)
                L($"[trace {n} {w:F1}] " + string.Join(" | ", CaveHazard.Live.Where(h => h != null && h.Damaging).Select(h => $"{h.Kind}:{h.transform.position.x - pc.transform.position.x:F1}{(h.IsActive ? "*" : "")}")) + $" boss={b.transform.position.x - pc.transform.position.x:F1}");
            if (!shotA && w > 1.2f) { shotA = true; Shot($"cave_{n}_D_ultimate_a"); }
            if (!shotB && w > 3.6f) { shotB = true; Shot($"cave_{n}_D_ultimate_b"); }
            TrackBossHitboxes(b);
            w += Time.deltaTime; yield return null;
        }
        float uLen = Time.time - uStart;
        Check(!b.UltimateRunning, $"{n} D: ultimate ends ({uLen:F1}s)");
        Check(uLen < 22f, $"{n} D: ultimate is not too long ({uLen:F1}s)");
        // E: 判断の回数(プレイヤーの列に攻撃が重なったまとまり: 0.35秒以上空いたら別の判断)
        var overs = caveOver.Where(o => o.t >= uStart - 0.1f && o.t <= uStart + uLen + 0.5f).OrderBy(o => o.t).ToList();
        int decisions = 0; float last = -9f; bool sawFloor = false, sawCeil = false;
        foreach (var o in overs) { if (o.t - last > 0.35f) decisions++; last = o.t; if (o.ceil) sawCeil = true; else sawFloor = true; }
        // ボス本体の突進(地形の攻撃でない)で判断を求める必殺技もある: その時は本体の判定も数える
        L($"[{n}] ultimate {uLen:F1}s: terrain decisions={decisions} floor={sawFloor} ceiling={sawCeil} srcs=[{string.Join(",", overs.Select(o => o.src.Split(':').Last()).Distinct())}] seen=[{string.Join(",", seenKinds)}] breakDuringUlt={b.BreakCount > brU0}");
        if (b.BreakCount > brU0 && decisions < 2) L($"[{n}] E: ultimate was cut by a BREAK ({decisions} decisions before it)");
        else Check(decisions >= 2, $"{n} E: the ultimate asks for at least 2 decisions ({decisions}, terrain+body)");
        Check(CaveHazard.Violations == viol0, $"{n} E: no floor+ceiling overlap on the player during the ultimate ({CaveHazard.Violations - viol0})");
        // F: 隙
        yield return new WaitForSeconds(0.5f);
        Shot($"cave_{n}_F_recovery");
        // BREAKで必殺技が止まった時は、BREAKが隙の代わり(崩しで止めるのが正しい遊び方)
        Check(caveRecoveries.Count > rec0 || b.BreakCount > brU0, $"{n} F: a big recovery window (or a BREAK) follows the ultimate");
        w = 0f; while (w < 6f && caveRecoveries.Count > rec0 && b.isActiveAndEnabled && !b.IsDead && w < 5f) { w += Time.deltaTime; yield return null; }

        // G: 崩し(BREAK)
        w = 0f; while (w < 6f && b.Broken) { w += Time.deltaTime; yield return null; } // 自然にBREAKしていたら終わるのを待つ
        int br0 = b.BreakCount;
        b.DebugForceBreak();
        yield return new WaitForSeconds(0.4f);
        Check(b.BreakCount > br0 && b.Broken, $"{n} G: BREAK by stagger");
        Shot($"cave_{n}_G_break");
        yield return new WaitForSeconds(tune.breakDuration + 0.8f);
        Check(!b.Broken, $"{n} G: recovers from BREAK");
        Check(Mathf.Abs(b.transform.position.y - (TerrainManager.Instance.GetHeightAt(b.transform.position.x) ?? b.transform.position.y)) < 6f, $"{n} G: not launched far by the break");

        // H: ラン再開
        float d0 = gm.MaxDistance;
        bm.DebugForceResume();
        yield return new WaitForSeconds(3f);
        Check(bm.RunResumed, $"{n} H: run resumed");
        Check(gm.MaxDistance > d0 + 10f, $"{n} H: distance counts again ({d0:F0} -> {gm.MaxDistance:F0})");
        Check(!b.IsDead && Mathf.Abs(b.transform.position.x - pc.transform.position.x) < 30f, $"{n} H: the boss keeps fighting near the player (gap {b.transform.position.x - pc.transform.position.x:F1})");
        L($"[kind] {n}: phase={b.Phase}/{b.PhaseCount} specials={b.SpecialsUsed} ultimates={b.UltimatesUsed} breaks={b.BreakCount} resumed={bm.RunResumed}");

        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        Check(!bm.IsBossPhase, $"{n}: boss phase ends after the kill");
        yield return new WaitForSeconds(1.0f);
        // 関門に近づき過ぎたら戻す(次の試験が関門のボスと重ならないように)
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator CaveZakoCase()
    {
        var bm = Bm;
        float w0 = 0f; // 前の戦闘の報酬が終わるまで(終わる前に始めると、前の戦闘の終わりでボス戦の状態が解除される)
        while ((bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(1.5f);
        bm.DebugCaveEncounter(CaveBossKind.Troll, -1);
        yield return WaitBossSpawn(10f);
        var b = CaveBossAlive();
        if (b == null) { Check(false, "I: troll spawned"); yield break; }
        int enc0 = caveEncPlanned;
        bm.DebugForceResume();
        Check(bm.RunResumed, "I: run resumed with the troll alive");
        int zako = 0; float wz = 0f;
        while (wz < 12f) { zako = Mathf.Max(zako, FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled && e.GetComponent<BonusEnemy>() == null)); wz += Time.deltaTime; yield return null; }
        Check(zako > 0 || caveEncPlanned > enc0, $"I: normal enemies come back after the run resumes with the boss (max alive {zako}, encounters planned {caveEncPlanned - enc0})");
        b.DebugForceUltimate();
        float w = 0f; while (w < 4f && !b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(0.5f);
        Check(b.UltimateRunning && CaveBossSafety.HoldZako && BossBattle.ZakoAttackScale < 1f && BossBattle.SuppressObstacles, $"I: during the ultimate no new enemies/obstacles and slower enemy attacks (hold={CaveBossSafety.HoldZako} scale={BossBattle.ZakoAttackScale:F2})");
        Check(CaveBossSafety.ForceFlatTerrain, "I: no new pits during the ultimate (single player)");
        Shot("cave_I_zako_mix_ultimate");
        w = 0f; while (w < 25f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
    }

    IEnumerator CaveRematchCase()
    {
        var bm = Bm;
        var pool = bm.DefeatedPool.ToList();
        L($"[J] pool: {string.Join(", ", pool)}");
        Check(pool.Count >= 3 && pool.All(p => p.StartsWith("Cave/")), $"J: defeated cave bosses are in the pool ({pool.Count})");
        // 次の 1,000m 系の関門まで進める(本来のムカデは倒しているので、撃破済みから抽選される)
        float next = bm.NextBossDistance;
        int k = Mathf.RoundToInt(next / 1000f);
        while (k % 5 == 0) k++;
        WarpTo(k * 1000f - 50f);
        yield return WaitBossSpawn(25f);
        Check(bm.IsBossPhase && bm.AliveBossCount > 0, $"J: gate {k}km boss spawned");
        Check(bm.CurrentEncounterIsRematch && pool.Contains(bm.CurrentEncounterKey), $"J: the {k}km gate drew a defeated boss ({bm.CurrentEncounterKey} rematch={bm.CurrentEncounterIsRematch}) | {bm.LastRematchDecision}");
        var b = CaveBossAlive();
        Check(b != null && b.RematchTierApplied >= 0, $"J: rematch scaling applied (tier {(b != null ? b.RematchTierApplied : -2)})");
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator CaveHighTierCase()
    {
        var bm = Bm;
        int top = BossRematchTuning.I.tiers.Count - 1;
        var baseTune = BossBattleTuning.I.For("Worm");
        bm.DebugCaveEncounter(CaveBossKind.Worm, top);
        yield return WaitBossSpawn(10f);
        var b = CaveBossAlive();
        if (b == null) { Check(false, "K: worm spawned"); yield break; }
        int basePhases = baseTune.phaseThresholds.Length + 1;
        Check(b.RematchTierApplied == top && b.PhaseCount > basePhases, $"K: top rematch tier adds a phase ({basePhases} -> {b.PhaseCount})");
        Check(bm.CurrentEncounterIsRematch && BossManager.DamageMul > 1f, $"K: top rematch hits harder (x{BossManager.DamageMul:F1})");
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(2.5f);
        float w = 0f; while (w < 16f && b.UltimateRunning) { w += Time.deltaTime; yield return null; } // 段階2で自分から始めた分は見送る
        yield return new WaitForSeconds(1f);
        b.DebugForceUltimate();
        w = 0f; while (w < 4f && !b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        int c0 = caveOver.Count; float t0 = Time.time;
        w = 0f; while (w < 25f && b.UltimateRunning) { TrackBossHitboxes(b); w += Time.deltaTime; yield return null; }
        int decisions = 0; float last = -9f;
        foreach (var o in caveOver.Skip(c0).OrderBy(o => o.t)) { if (o.t - last > 0.35f) decisions++; last = o.t; }
        L($"[K] upgraded EARTH BREAKER: {Time.time - t0:F1}s decisions={decisions}");
        Check(decisions >= 5, $"K: the upgraded ultimate has an extra beat ({decisions} decisions)");
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator CaveSpeedCase(string tag, float kmh)
    {
        var bm = Bm;
        bm.DebugCaveEncounter(CaveBossKind.Troll, -1);
        yield return WaitBossSpawn(10f);
        SetKmh(kmh);
        var b = CaveBossAlive();
        if (b == null) { Check(false, $"{tag}: troll spawned"); yield break; }
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(0.5f);
        b.DebugForceUltimate();
        float w = 0f, maxDrift = 0f; int farFrames = 0, samples = 0;
        var startRel = new Dictionary<int, float>(); // 使い回しで同じ物が別の場所に出るので、出るたびの番号で
        while (w < 25f && (w < 4f || b.UltimateRunning) && !b.IsDead)
        {
            float px = pc.transform.position.x;
            foreach (var h in CaveHazard.Live)
            {
                if (h == null || (h.Kind != CaveHazardKind.Floor && h.Kind != CaveHazardKind.Ceiling)) continue;
                float rel = h.transform.position.x - px;
                if (!startRel.TryGetValue(h.Serial, out float r0)) startRel[h.Serial] = rel;
                else { maxDrift = Mathf.Max(maxDrift, Mathf.Abs(rel - r0)); samples++; }
            }
            if (Mathf.Abs(b.transform.position.x - px) > 30f && !b.UltimateRunning) farFrames++;
            w += Time.deltaTime; yield return null;
        }
        Check(b.UltimatesUsed > 0, $"{tag}: the ultimate happens at {kmh:0}km/h");
        Check(samples > 0 && maxDrift < 1.0f, $"{tag}: floor/ceiling attacks stay where they were telegraphed at {kmh:0}km/h (max drift {maxDrift:F2}m, {samples} samples)");
        Check(farFrames <= 3, $"{tag}: boss stays near the player ({farFrames} far frames)");
        Shot($"cave_{tag}_after");
        PlayerController.DebugSpeedScale = 1f;
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator CaveGateCarryCase()
    {
        var bm = Bm;
        // 関門の少し手前で倒さずにおき、次の関門を2つ越える
        float next = bm.NextBossDistance;
        WarpTo(next - 50f);
        yield return WaitBossSpawn(25f);
        Check(bm.AliveBossCount > 0, $"M: boss at {next:F0}m spawned");
        int first = bm.AliveBossCount;
        string firstKey = bm.CurrentEncounterKey;
        bm.DebugForceResume();
        int pend0 = bossLogPending;
        float passTo = next + 2150f;
        int maxAlive = 0; float w = 0f;
        yield return new WaitForSeconds(0.5f);
        SetKmh(400f); // ワープは関門の記録を作り直すので使わず、速く走って通過する
        while (gm.MaxDistance < passTo && w < 120f)
        {
            maxAlive = Mathf.Max(maxAlive, bm.AliveBossCount);
            w += Time.deltaTime; yield return null;
        }
        PlayerController.DebugSpeedScale = 1f;
        Check(maxAlive <= first && bm.IsBossPhase, $"M: no second boss while the first is alive (max alive {maxAlive}, first {first}) passing to {gm.MaxDistance:F0}m");
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        Check(bossLogPending > pend0, $"N: one passed gate is held as pending ({bossLogPending - pend0}) | {bm.LastGateDecision}");
        w = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(bm.IsBossPhase && bm.AliveBossCount > 0, $"N: the pending boss appears after the reward ({bm.CurrentEncounterKey}, first was {firstKey})");
        KillCaveBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1.5f);
        Check(bm.NextBossDistance > gm.MaxDistance, $"N: next gate is ahead after the pending boss ({bm.NextBossDistance:F0} > {gm.MaxDistance:F0})");
    }
}
#endif

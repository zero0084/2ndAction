#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ボス戦の強化(2026-10-01)の確認。
//  -qaBoss <dir>        … A〜I(即撃破/再開直前/再開後/1,000m/5,000m/10,000m通過/複数体/高速/低速)と距離・二重開始の監視
//  -qaBossKinds <dir>   … 荒野街道の各ボス: 段階/特殊攻撃/必殺技/BREAK/隙を出して撮影(-qaBossOnly Wolf,Serpent... で絞る)
public partial class QaSweep
{
    int bossLogResumed, bossLogNewRunReset, bossLogUltimates, bossLogBreaks, bossLogPending;
    bool bossLogHooked;
    float distWatchLast = -1f;
    bool distWatchAllowDrop;
    int distDrops;

    void HookBossLogs()
    {
        if (bossLogHooked) return;
        bossLogHooked = true;
        Application.logMessageReceived += (c, tr, type) =>
        {
            if (c.StartsWith("[BossRun] RunResumed")) bossLogResumed++;
            else if (c.StartsWith("[Encounter] NewRunReset")) bossLogNewRunReset++;
            else if (c.Contains("ULTIMATE '")) bossLogUltimates++;
            else if (c.Contains(" BREAK #")) bossLogBreaks++;
            else if (c.StartsWith("[BossRun] NextGate pending")) bossLogPending++;
        };
        StartCoroutine(DistanceWatch());
    }

    IEnumerator DistanceWatch()
    {
        while (true)
        {
            var g = GameManager.Instance;
            if (g != null && g.HasStarted)
            {
                float d = g.MaxDistance;
                if (distWatchLast >= 0f && d < distWatchLast - 0.5f && !distWatchAllowDrop) { distDrops++; L($"[WARN-DIST] distance went back {distWatchLast:F1} -> {d:F1}"); }
                distWatchLast = d;
            }
            else distWatchLast = -1f;
            yield return null;
        }
    }

    void WarpTo(float d)
    {
        distWatchAllowDrop = true;
        gm.DebugWarpToDistance(d);
        distWatchLast = -1f;
        distWatchAllowDrop = false;
    }

    BossManager Bm => BossManager.Instance;
    List<WildBossBase> WildAlive() => FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Where(b => b != null && !b.IsDead && b.isActiveAndEnabled).ToList();
    List<DragonController> DragonsAlive() => FindObjectsByType<DragonController>(FindObjectsSortMode.None).Where(b => b != null && !b.IsDead).ToList();

    IEnumerator WaitBossSpawn(float timeout = 25f)
    {
        float w = 0f;
        while (Bm.AliveBossCount <= 0 && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        // 登場の演出が終わるまで
        w = 0f;
        while (WildAlive().Any(b => b.IsEntering) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    // 残りHPぶんを1発で入れる(-qaBossはボスHPを大きく固定するので、99999では倒れない)
    void KillAllBosses()
    {
        foreach (var b in WildAlive()) b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld);
        foreach (var d in DragonsAlive()) d.TakeDamage(Mathf.Max(99999, d.Hp));
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) m.TakeDamage(Mathf.Max(99999, m.Hp)); // 天空の魔人(2026-10-02)
    }

    IEnumerator WaitPhaseEnd(float timeout = 30f)
    {
        float w = 0f;
        while (Bm.IsBossPhase && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator BossMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        // 2026-10-02: 結果を毎回同じにする。以前は自動補助の攻撃+ランダムに出るカードの取得でプレイヤーの火力がばらつき、
        // 強い回はボスが「ラン再開」の前に倒れて C〜H が連鎖して落ちていた(0〜18件)。ボスの倒れ方は試験側が決める:
        // ボスHPを大きく固定し(開発用のNetTestBossHpOverride。BossHpPlan/再戦の強化/カードの倍率より優先)、倒す時は KillAllBosses。
        // 段階/必殺技はHPの割合で決まるので、割合で削る G/H はそのまま成り立つ。
        Random.InitState(QaBossSeed);
        BossManager.NetTestBossHpOverride = QaBossHp;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var tn = BossBattleTuning.I;
        L($"[tuning] resume normal/strong/special = {tn.resumeNormal}/{tn.resumeStrong}/{tn.resumeSpecial}s pending={tn.pendingMode} safe={tn.pendingSafeDelay}s entries={tn.entries.Count}");

        // ---- 重い一撃(ハート2)の確認: 満タンからは倒れない
        {
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            stopKeepAlive = true;
            yield return null;
            var livesProp = typeof(GameManager).GetProperty("Lives");
            livesProp.GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
            var r = gm.TryDamagePlayer(false, "qa-heavy", CombatScale.PlayerHeavyHit);
            Check(gm.Lives == gm.MaxLives - CombatScale.PlayerHeavyHit && r == GameManager.DamageResult.Hit, $"heavy hit takes 2 hearts ({gm.Lives}/{gm.MaxLives})");
            livesProp.GetSetMethod(true).Invoke(gm, new object[] { 2 * CombatScale.HpPerHeart });
            int full = gm.MaxLives;
            gm.maxLives = 2 * CombatScale.HpPerHeart;
            r = gm.TryDamagePlayer(false, "qa-heavy-full", CombatScale.PlayerHeavyHit);
            Check(gm.Lives == CombatScale.HpPerHeart && r == GameManager.DamageResult.Hit, $"heavy hit never kills from full hearts (lives {gm.Lives})");
            gm.maxLives = full;
            livesProp.GetSetMethod(true).Invoke(gm, new object[] { 99 });
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            stopKeepAlive = false;
            StartCoroutine(KeepAlive());
        }

        // ---- A: 即撃破(ラン再開なし、次は2,000m)
        WarpTo(940f);
        yield return WaitBossSpawn();
        Check(Bm.AliveBossCount > 0 && Bm.IsBossPhase, "A: 1,000m boss spawned");
        float dA = gm.MaxDistance;
        KillAllBosses();
        yield return WaitPhaseEnd();
        Check(!Bm.IsBossPhase && Bm.ResumeCount == 0, $"A: boss phase ended without a run resume (resumes {Bm.ResumeCount})");
        Check(Mathf.Abs(Bm.NextBossDistance - 2000f) < 1f, $"A: next gate 2000 (got {Bm.NextBossDistance})");
        yield return new WaitForSeconds(1.5f);
        Check(gm.MaxDistance > dA + 5f, $"A: distance moves again after the reward ({dA:F0} -> {gm.MaxDistance:F0})");

        // ---- B: ラン再開の直前に撃破
        WarpTo(1940f);
        yield return WaitBossSpawn();
        float w = 0f;
        while (Bm.EncounterSeconds < Bm.ResumeSecondsTotal - 0.8f && w < 40f) { yield return null; w += Time.deltaTime; }
        Check(!Bm.RunResumed, $"B: not resumed yet at {Bm.EncounterSeconds:F1}/{Bm.ResumeSecondsTotal:F0}s");
        Shot("B_countdown_before_resume");
        float dB = gm.MaxDistance;
        KillAllBosses();
        yield return WaitPhaseEnd();
        Check(Bm.ResumeCount == 0 && bossLogResumed == 0, $"B: killed just before resume -> no resume (resumes {bossLogResumed})");
        Check(Mathf.Abs(dB - 2000f) < 2f, $"B: distance stayed at the gate during the fight ({dB:F1})");

        // ---- C(2026-10-07 ラン再開の廃止): 従来の制限時間を過ぎても戦いが続く(距離/雑魚は止まったまま)。
        //      戦えない所へ消えたボスは戻る(HP/報酬はそのまま)。撃破の後に一度だけ再開し、次の関門は正しく 4,000m
        WarpTo(2940f);
        yield return WaitBossSpawn();
        float dC0 = gm.MaxDistance;
        w = 0f;
        while (Bm.EncounterSeconds < Bm.ResumeSecondsTotal + 5f && w < 60f) { yield return null; w += Time.deltaTime; }
        int zako = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled && e.GetComponent<BonusEnemy>() == null);
        L($"[C] {Bm.EncounterSeconds:F1}s into the fight (old limit {Bm.ResumeSecondsTotal:F0}s): resumed {Bm.RunResumed}, distance {dC0:F0} -> {gm.MaxDistance:F0}, zako {zako}, bosses {Bm.AliveBossCount}");
        Check(!Bm.RunResumed && Bm.ResumeCount == 0 && bossLogResumed == 0, "C: no run resume after the old time limit (the fight goes on until the boss is down)");
        Check(gm.MaxDistance <= dC0 + 2f, $"C: distance stays during the fight ({dC0:F0} -> {gm.MaxDistance:F0})");
        Check(zako == 0 && Bm.AliveBossCount > 0, $"C: no normal enemies, the boss is still there ({zako} zako)");
        // 戦えない所へ消えた(地形の中/画面の外)ボスは、5秒で戦える位置へ戻る。HP はそのまま
        var bC = WildAlive().FirstOrDefault();
        if (bC != null)
        {
            var yF = typeof(WildBossBase).GetField("yOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int hp0 = bC.Hp, rec0 = WildBossBase.OffArenaRecovered, kills0 = gm.BossKillCount;
            float lostT = 0f; bool back = false;
            while (lostT < 9f)
            {
                // 毎フレーム地形の中(画面の下の外)へ押し込む(地形に埋まって戦えない状態)。戻されたら終わり
                if (WildBossBase.OffArenaRecovered > rec0) { back = true; break; }
                yF.SetValue(bC, -25f);
                yield return null; lostT += Time.deltaTime;
            }
            yield return null;
            var cam = Camera.main; float vx = cam != null ? cam.WorldToViewportPoint(bC.transform.position).x : -1f;
            L($"[C] boss pushed out of the area: recovered {back} after {lostT:F1}s, now viewport x {vx:F2}, hp {hp0} -> {bC.Hp}, boss kills {kills0} -> {gm.BossKillCount}");
            Check(back && vx > 0f && vx < 1f && cam.WorldToViewportPoint(bC.transform.position).y > 0f, "C: a boss stuck outside the fighting area comes back in front of the player");
            Check(bC.Hp == hp0 && gm.BossKillCount == kills0, "C: coming back does not heal the boss or give a reward");
        }
        float speedBefore = pc.CurrentAutoRunSpeed;
        int resetsBefore = bossLogNewRunReset;
        KillAllBosses();
        yield return WaitPhaseEnd();
        yield return new WaitForSeconds(3f);
        Check(bossLogNewRunReset == resetsBefore, "C: killing the boss does not restart the run");
        Check(bossLogResumed == 0, "C: no run resume at all (the run goes on through the normal end of the fight)");
        Check(gm.MaxDistance > dC0 + 5f, $"C: distance moves again after the kill ({dC0:F0} -> {gm.MaxDistance:F0})");
        Check(Mathf.Abs(Bm.NextBossDistance - 4000f) < 1f, $"C: next gate 4000 (got {Bm.NextBossDistance}), no gate skipped or doubled");

        // ---- D/E/F(2026-10-07): 以前は「倒さずに次の関門を通過 → 保留」を見ていたが、ラン再開の廃止で通過自体が起きない。
        //      代わりに 5,000m の強敵を長く戦ってから倒し、次の関門が 6,000m に1つだけ出ることを見る
        WarpTo(4940f);
        yield return WaitBossSpawn();
        float dD = gm.MaxDistance;
        w = 0f;
        while (Bm.EncounterSeconds < Bm.ResumeSecondsTotal + 3f && w < 70f) { yield return null; w += Time.deltaTime; }
        Check(!Bm.RunResumed && gm.MaxDistance <= dD + 2f, $"D: the 5,000m fight goes on past the old limit ({Bm.EncounterSeconds:F0}s)");
        KillAllBosses();
        yield return WaitPhaseEnd();
        Check(Mathf.Abs(Bm.NextBossDistance - 6000f) < 1f && bossLogPending == 0, $"D: next gate 6000 (got {Bm.NextBossDistance}), nothing pending ({bossLogPending})");

        // ---- G: 複数体(13,000mはオオカミ2体): 同時に必殺技(突進)を始めない
        Bm.DebugPoolReset(); // 2026-10-02: 再戦の抽選にせず、本来のオオカミの群れを出す
        WarpTo(12940f);
        yield return WaitBossSpawn();
        var wolves = WildAlive();
        L($"[G] bosses at 13,000m: {wolves.Count} ({string.Join(",", wolves.Select(b => b.bossName))})");
        Check(wolves.Count >= 2, "G: two wolves at 13,000m");
        foreach (var b in wolves) b.TakeDamage(Mathf.Max(1, b.Hp - Mathf.FloorToInt(b.maxHp * 0.45f)), b.CenterWorld);
        int overlap = 0; float gT = 0f; int ultSeen = 0;
        while (gT < 22f && Bm.AliveBossCount > 0)
        {
            var alive = WildAlive();
            int running = alive.Count(b => b.UltimateRunning);
            if (running > 1) overlap++;
            ultSeen = alive.Sum(b => b.UltimatesUsed);
            if (running == 1 && gT > 3f && gT < 3.1f) Shot("G_one_wolf_charging");
            gT += Time.deltaTime;
            yield return null;
        }
        Check(overlap == 0, $"G: never two ultimates at once (overlap frames {overlap})");
        Check(ultSeen >= 2, $"G: two overtake charges one after another ({ultSeen})");
        // 2026-10-02: 合計だけでは「同じ1体が2回」でも通るので、1体ずつの回数も残す(順番が回らないのは仕様の判断待ち: 警告どまり)
        string perWolf = string.Join(",", wolves.Select(b => b.UltimatesUsed));
        L($"[G] ultimates per wolf: {perWolf} in {gT:F1}s");
        if (wolves.Count >= 2 && wolves.Any(b => b.UltimatesUsed == 0)) Warn($"G: one wolf never got a turn for the overtake charge (per wolf {perWolf})");
        KillAllBosses();
        yield return WaitPhaseEnd();

        // ---- H/I: 高速/低速(必殺技が成立する・ボスが画面に残る)
        yield return SpeedCase("H", 300f);
        yield return SpeedCase("I", 30f);

        L($"[summary] resumes={bossLogResumed} pendingChosen={bossLogPending} ultimates={bossLogUltimates} breaks={bossLogBreaks} distanceDrops={distDrops} newRunResets={bossLogNewRunReset}");
        Check(distDrops == 0, "distance never went backwards (outside of debug warps)");
        BossManager.NetTestBossHpOverride = 0;
        yield return EndRun();
    }

    const int QaBossSeed = 20261002;
    const int QaBossHp = 50000000; // 自動補助の攻撃では試験の時間内に削り切れない量

    // 1つ目のボスを倒さずに passTo まで走り、途中の関門が重ならないこと/撃破後に保留(wantPending)が1つだけ出ることを確かめる
    IEnumerator GateCarryCase(string tag, float warp, float gate, float passTo, float wantPending)
    {
        WarpTo(warp);
        yield return WaitBossSpawn();
        Check(Bm.AliveBossCount > 0, $"{tag}: boss at {gate:F0}m spawned");
        int firstCount = Bm.AliveBossCount;
        string firstName = string.Join(",", WildAlive().Select(b => b.bossName).Concat(DragonsAlive().Select(_ => "Dragon")));
        float w = 0f;
        while (!Bm.RunResumed && w < 45f) { yield return null; w += Time.deltaTime; }
        Check(Bm.RunResumed, $"{tag}: run resumed");
        SetKmh(260f);
        int maxAlive = 0; w = 0f;
        while (gm.MaxDistance < passTo && w < 90f)
        {
            maxAlive = Mathf.Max(maxAlive, Bm.AliveBossCount);
            yield return null; w += Time.deltaTime;
        }
        PlayerController.DebugSpeedScale = 1f;
        Check(gm.MaxDistance >= passTo, $"{tag}: ran to {gm.MaxDistance:F0}m with the boss alive");
        Check(maxAlive <= firstCount, $"{tag}: no extra boss spawned while passing gates (max alive {maxAlive}, first {firstCount})");
        Check(WildAlive().Count + DragonsAlive().Count == firstCount, $"{tag}: still only the first boss ({firstName})");
        Shot($"{tag}_boss_kept_past_gates");
        float dKill = gm.MaxDistance;
        KillAllBosses();
        yield return WaitPhaseEnd();
        L($"[{tag}] decision: {Bm.LastGateDecision}");
        Check(Mathf.Abs(Bm.NextBossDistance - wantPending) < 1f, $"{tag}: pending gate is {wantPending:F0} (got {Bm.NextBossDistance:F0})");
        // 保留ボスは安全時間の後に出る(距離は関門まで戻さない)
        float before = gm.MaxDistance;
        w = 0f;
        while (Bm.AliveBossCount <= 0 && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Bm.AliveBossCount > 0, $"{tag}: pending boss appeared after the safe delay ({w:F1}s)");
        Check(gm.MaxDistance >= before - 0.5f, $"{tag}: distance not pulled back to the pending gate ({before:F0} -> {gm.MaxDistance:F0})");
        yield return WaitBossSpawn();
        L($"[{tag}] pending boss: {string.Join(",", WildAlive().Select(b => b.bossName).Concat(DragonsAlive().Select(_ => "Dragon")))}");
        KillAllBosses();
        yield return WaitPhaseEnd();
        float next = Bm.NextBossDistance;
        Check(next > gm.MaxDistance, $"{tag}: after the pending boss, the next gate is ahead ({next:F0} > {gm.MaxDistance:F0}) - no backlog");
        L($"[{tag}] after pending: next gate {next:F0}, d={gm.MaxDistance:F0}, killed first at {dKill:F0}");
    }

    IEnumerator SpeedCase(string tag, float kmh)
    {
        WarpTo(19940f); // 20,000m サイクロプス
        yield return WaitBossSpawn();
        SetKmh(kmh);
        var b = WildAlive().FirstOrDefault();
        if (b == null) { Check(false, $"{tag}: cyclops spawned"); yield break; }
        b.TakeDamage(Mathf.Max(1, b.Hp - Mathf.FloorToInt(b.maxHp * 0.6f)), b.CenterWorld);
        float w = 0f; float maxAbsGap = 0f; bool shot = false; int farRun = 0, farRunMax = 0, spikes = 0;
        int ult0 = b.UltimatesUsed;
        while (w < 26f && !b.IsDead)
        {
            float gap = b.transform.position.x - pc.transform.position.x;
            if (!b.UltimateRunning)
            {
                maxAbsGap = Mathf.Max(maxAbsGap, Mathf.Abs(gap));
                // 落下からの復帰(プレイヤーの瞬間移動)の1フレームだけ離れて見えるのは除く: 続けて離れているフレーム数で見る
                if (Mathf.Abs(gap) > 30f) { farRun++; if (farRun == 1) spikes++; } else farRun = 0;
                farRunMax = Mathf.Max(farRunMax, farRun);
            }
            if (b.UltimateRunning && !shot && w > 1f) { shot = true; StartCoroutine(DelayedShot($"{tag}_{kmh:0}kmh_ultimate", 2.2f)); }
            w += Time.deltaTime;
            yield return null;
        }
        Check(b.UltimatesUsed > ult0, $"{tag}: ultimate happens at {kmh:0}km/h ({b.UltimatesUsed - ult0})");
        Check(farRunMax <= 3, $"{tag}: boss stays near the player at {kmh:0}km/h (longest far streak {farRunMax} frames, one-frame spikes {spikes}, max |gap| {maxAbsGap:F1})");
        PlayerController.DebugSpeedScale = 1f;
        KillAllBosses();
        yield return WaitPhaseEnd();
    }

    readonly HashSet<string> recordedVideo = new HashSet<string>();
    // 必殺技の始まりから seconds 秒を30fpsで毎フレーム書き出す(動画用)
    IEnumerator RecordUltimate(string name, float seconds)
    {
        string vdir = System.IO.Path.Combine(outDir, "video_" + name);
        System.IO.Directory.CreateDirectory(vdir);
        Time.captureFramerate = 30;
        int frames = Mathf.RoundToInt(seconds * 30f);
        for (int f = 0; f < frames; f++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vdir, $"f{f:00000}.png"));
        }
        Time.captureFramerate = 0;
        L($"[video] {name}: {frames} frames");
    }

    IEnumerator DelayedShot(string name, float delay)
    {
        yield return new WaitForSeconds(delay);
        Shot(name);
    }

    // 各ボス: 段階→特殊攻撃→必殺技→隙、BREAK を出して撮る
    IEnumerator BossKindsMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        (string name, float gate)[] list =
        {
            ("Wolf", 1000f), ("GoblinRider", 5000f), ("Serpent", 10000f), ("Cyclops", 20000f), ("Spider", 30000f), ("Golem", 40000f),
            ("Griffin", 50000f), ("Hydra", 60000f), ("Demon", 70000f), ("Dragon", 80000f), ("BlackKnight", 90000f),
        };
        string only = Arg("-qaBossOnly", "");
        foreach (var e in list)
        {
            if (only != "" && !only.Split(',').Contains(e.name)) continue;
            yield return OneBossKind(e.name, e.gate);
        }
        L($"[summary] ultimates={bossLogUltimates} breaks={bossLogBreaks} resumes={bossLogResumed}");
        yield return EndRun();
    }

    IEnumerator OneBossKind(string name, float gate)
    {
        WarpTo(gate - 60f);
        yield return WaitBossSpawn();
        var tune = BossBattleTuning.I.For(name);
        bool isDragon = name == "Dragon";
        var wb = WildAlive().FirstOrDefault();
        var dc = DragonsAlive().FirstOrDefault();
        if (wb == null && dc == null)
        {
            Check(false, $"{name}: spawned at {gate:F0}");
            L($"[diag] d={gm.MaxDistance:F1} timeScale={Time.timeScale:F2} bossPhase={Bm.IsBossPhase} next={Bm.NextBossDistance:F0} started={gm.HasStarted} over={gm.IsGameOver} speed={pc.CurrentAutoRunSpeed:F1} x={pc.transform.position.x:F1} levelUp={gm.LevelUpPending} rewardWait={gm.IsRewardSequenceWaitingForSelection} bonus={(BonusZone.Instance != null ? BonusZone.Instance.State.ToString() : "-")} blocks={(BonusZone.Instance != null && BonusZone.Instance.BlocksBoss)} countdown={gm.CountdownActive} finishing={pc.IsFinishing}");
            Shot($"{name}_spawn_fail");
            yield break;
        }
        yield return new WaitForSeconds(1.5f);
        Shot($"{name}_p1");
        // 段階: 境目のすぐ下まで削る
        int phases = (tune.phaseThresholds != null ? tune.phaseThresholds.Length : 0) + 1;
        int lastPhaseShot = 1;
        int specials0 = wb != null ? wb.SpecialsUsed : 0;
        for (int p = 0; p < phases - 1; p++)
        {
            float th = tune.phaseThresholds[p] - 0.04f;
            if (wb != null) { int target = Mathf.FloorToInt(wb.maxHp * th); if (wb.Hp > target) wb.TakeDamage(wb.Hp - target, wb.CenterWorld); }
            else { int target = Mathf.FloorToInt(dc.maxHp * th); if (dc.Hp > target) dc.TakeDamage(dc.Hp - target); }
            yield return new WaitForSeconds(0.45f);
            Shot($"{name}_phase{p + 2}_roar");
            int phNow = wb != null ? wb.Phase : dc.Phase;
            Check(phNow == p + 2, $"{name}: reached phase {p + 2} (now {phNow})");
            lastPhaseShot = p + 2;
            // この段階で新しい攻撃が出るまで待つ(最大18秒)
            float w = 0f; bool shotSpecial = false;
            while (w < 18f)
            {
                bool ult = wb != null ? wb.UltimateRunning : (dc != null && BossBattle.UltimateActive);
                if (ult && !shotSpecial)
                {
                    shotSpecial = true;
                    if (Arg("-qaBossVideo", "") != "" && !recordedVideo.Contains(name)) { recordedVideo.Add(name); yield return RecordUltimate(name, 8f); }
                    else { StartCoroutine(DelayedShot($"{name}_phase{p + 2}_ultimate_a", 0.9f)); StartCoroutine(DelayedShot($"{name}_phase{p + 2}_ultimate_b", 2.0f)); }
                }
                if (wb != null && wb.SpecialsUsed > specials0 && !shotSpecial) { shotSpecial = true; StartCoroutine(DelayedShot($"{name}_phase{p + 2}_special", 0.8f)); }
                if (shotSpecial && w > 4f) break;
                w += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(2.5f);
        }
        // 必殺技(あるボスは最後の段階で)を待つ
        int ults = wb != null ? wb.UltimatesUsed : dc.UltimatesUsed;
        if (tune.ultimateCooldown > 0f)
        {
            float w = 0f;
            while (w < 26f && (wb != null ? wb.UltimatesUsed : dc.UltimatesUsed) == 0) { w += Time.deltaTime; yield return null; }
            ults = wb != null ? wb.UltimatesUsed : dc.UltimatesUsed;
            Check(ults > 0, $"{name}: used its ultimate");
            // 隙(Exhausted)を撮る
            w = 0f;
            while (w < 12f && (wb != null ? wb.UltimateRunning : BossBattle.UltimateActive)) { w += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.6f);
            Shot($"{name}_after_ultimate_opening");
        }
        int specials = wb != null ? wb.SpecialsUsed : 0;
        // BREAK
        if (tune.staggerMax > 0f)
        {
            int br0 = wb != null ? wb.BreakCount : dc.BreakCount;
            if (wb != null) wb.DebugAddStagger(tune.staggerMax * 1.2f); else dc.DebugAddStagger(tune.staggerMax * 1.2f);
            yield return new WaitForSeconds(0.5f);
            Shot($"{name}_break");
            int br = wb != null ? wb.BreakCount : dc.BreakCount;
            Check(br > br0, $"{name}: BREAK triggered by stagger");
            yield return new WaitForSeconds(tune.breakDuration + 0.5f);
            Check(wb == null || !wb.Broken, $"{name}: recovers from BREAK");
        }
        L($"[kind] {name}: phases={(wb != null ? wb.Phase : dc.Phase)}/{phases} specials={specials} ultimates={ults} breaks={(wb != null ? wb.BreakCount : dc.BreakCount)} hp={(wb != null ? wb.Hp : dc.Hp)}/{(wb != null ? wb.maxHp : dc.maxHp)} resumed={Bm.RunResumed}");
        KillAllBosses();
        yield return WaitPhaseEnd(40f);
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ボス戦の攻撃の前進/後退(2026-10-01)。 -qaLunge <dir> [-qaChars swordsman,...]
public partial class QaSweep
{
    IEnumerator LungeMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        string chars = Arg("-qaChars", "swordsman,dual_blade,dragon_lancer,fighter,ninja,vampire");
        foreach (string ch in chars.Split(','))
        {
            yield return BeginRun(ch, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
            yield return new WaitForSeconds(1.5f);
            float f0 = 0f, b0 = 0f, r;
            yield return MeasureLunge(PlayerController.FlickDirection.Forward, (v, k) => { f0 = v; r = k; });
            yield return MeasureLunge(PlayerController.FlickDirection.Backward, (v, k) => { b0 = v; r = k; });
            WarpTo(940f);
            yield return WaitBossSpawn();
            var boss = WildAlive().FirstOrDefault();
            float gap = boss != null ? boss.transform.position.x - boss.HalfWidth - pc.transform.position.x : 0f;
            // ボスが前方に離れている時(正面まで5以上)に前攻撃
            float wq = 0f;
            while (boss != null && boss.transform.position.x - boss.HalfWidth - pc.transform.position.x < 5.5f && wq < 6f) { wq += Time.deltaTime; yield return null; }
            gap = boss != null ? boss.transform.position.x - boss.HalfWidth - pc.transform.position.x : 0f;
            float f1 = 0f, b1 = 0f, kf = 1f, kb = 1f;
            yield return MeasureLunge(PlayerController.FlickDirection.Forward, (v, k) => { f1 = v; kf = k; });
            yield return new WaitForSeconds(0.6f);
            yield return MeasureLunge(PlayerController.FlickDirection.Backward, (v, k) => { b1 = v; kb = k; });
            L($"[lunge] {ch}: no boss fwd {f0:F2} back {b0:F2} | boss (front {gap:F1} ahead) fwd {f1:F2} back {b1:F2} | same attack scaled: fwd x{kf:F2} back x{kb:F2}");
            float want = Mathf.Lerp(BossBattleTuning.I.lungeForwardNear, BossBattleTuning.I.lungeForwardFar, Mathf.InverseLerp(BossBattleTuning.I.lungeNearDistance, BossBattleTuning.I.lungeFarDistance, gap));
            float kNow = BossBattle.LungeScale(1f, pc.transform.position.x);
            if (Mathf.Abs(f1) > 0.05f) Check(kf >= BossBattleTuning.I.lungeForwardNear * 0.95f, $"{ch}: forward attack step in a boss fight is x{kf:F2} (boss {gap:F1} ahead, x{want:F2} at the start)");
            if (Mathf.Abs(b1) > 0.05f) Check(kb > 1.5f, $"{ch}: backward attack step in a boss fight is x{kb:F2}");
            Check(Mathf.Abs(f0) < 0.05f || Mathf.Abs(f1) > 0.05f, $"{ch}: forward attack moved in the boss fight");
            KillAllBosses();
            yield return WaitPhaseEnd();
            yield return EndRun();
        }
    }

    // 攻撃1回ぶんの「走行以外の移動量」(攻撃の前進/後退)を積算する
    IEnumerator MeasureLunge(PlayerController.FlickDirection dir, System.Action<float, float> result)
    {
        var fLunge = typeof(PlayerController).GetField("lungeVelocityX", NP);
        var fReq = typeof(PlayerController).GetField("requestedFlick", NP);
        while (pc.IsAttacking) yield return null;
        yield return new WaitForSeconds(0.35f);
        pc.debugInjectFlick = dir;
        float sum = 0f, raw = 0f, t = 0f;
        while (t < 0.7f)
        {
            yield return null;
            if (t > 0.04f) pc.debugInjectFlick = null; // 2フレームだけ押す
            float dt = Time.deltaTime;
            float lv = (float)fLunge.GetValue(pc);
            if (Arg("-qaLungeTrace", "") != "") L($"  [trace] {dir} t={t:F2} lunge={lv:F2} scale={pc.LungeScaleNow:F2} attacking={pc.IsAttacking} ts={Time.timeScale:F2} grounded={pc.IsGrounded}");
            sum += lv * pc.LungeScaleNow * dt;
            raw += lv * dt;
            t += dt;
        }
        result(sum, Mathf.Abs(raw) > 0.001f ? sum / raw : 1f);
    }
}
#endif

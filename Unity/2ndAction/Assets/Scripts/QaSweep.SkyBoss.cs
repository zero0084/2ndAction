#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 天空回廊ボス強化(2026-10-05)の確認。 -qaSkyBoss <dir> [-qaSkyOnly Dragon,Behemoth...]
// 各ボス: A 初登場 / B 通常攻撃 / C 段階の移行+第2段階の技 / D 必殺技 / E 必殺技が避けられる(判断の回数・床と天井が同時に来ない)
//         F 必殺技後の隙 / G 崩し(BREAK) / H 空中のボスが攻撃の届かない高さに居続けない / I ラン再開
// 全体:   J 雑魚との混在(必殺技中は雑魚を出さない・穴を作らない) / K 再戦の抽選 / L 高距離の再戦(段階/必殺技の追加の一手)
//         M/N 高速/低速 / O/P ボスが残ったまま次の関門 → 保留 / S 例外 / T 使い回し
public partial class QaSweep
{
    IBossBattleDebug SkyBossAlive()
    {
        foreach (var b in WildAlive()) return b;
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && d.DebugAlive && !d.NetPuppet) return d;
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && m.DebugAlive && !m.NetPuppet) return m;
        return null;
    }

    void KillSkyBosses()
    {
        foreach (var b in WildAlive()) b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld);
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && !d.IsDead) d.TakeDamage(Mathf.Max(99999, d.Hp));
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && !m.IsDead) m.TakeDamage(Mathf.Max(99999, m.Hp));
    }

    bool HurtBounds(IBossBattleDebug b, out Bounds bb, out bool targetable)
    {
        bb = default; targetable = false;
        if (b is WildBossBase w) { bb = w.AssistBounds; targetable = w.AssistTargetable; return true; }
        var mb = b as MonoBehaviour;
        var c = mb != null ? mb.GetComponent<Collider2D>() : null;
        if (c == null) return false;
        bb = c.bounds; targetable = c.enabled;
        return true;
    }

    readonly HashSet<Object> strikeOver = new HashSet<Object>();
    readonly HashSet<Object> fbOver = new HashSet<Object>();
    // 判断の数え方: 攻撃の横幅がプレイヤーの位置を覆った(高さは問わない = 跳んで避けた攻撃も1回の判断として数える)
    readonly HashSet<int> hzCovered = new HashSet<int>();
    readonly HashSet<Object> hbCovering = new HashSet<Object>();
    readonly Dictionary<FireballController, float> fbPrevDx = new Dictionary<FireballController, float>();
    void TrackSkyThreats(IBossBattleDebug b)
    {
        if (b is WildBossBase w) TrackBossHitboxes(w);
        float px = pc.transform.position.x;
        foreach (var h in CaveHazard.Live)
        {
            if (h == null || !h.IsActive || !h.Damaging || hzCovered.Contains(h.Serial)) continue;
            var c = h.GetComponent<Collider2D>();
            if (c != null && c.bounds.min.x - 0.3f <= px && c.bounds.max.x + 0.3f >= px) { hzCovered.Add(h.Serial); caveOver.Add(new OverRec { t = Time.time, ceil = h.CeilingType, src = "cover:" + h.Kind }); }
        }
        if (b is MonoBehaviour mb)
            foreach (var hb in mb.GetComponentsInChildren<BossHitbox>())
            {
                var c = hb.GetComponent<Collider2D>();
                bool cov = hb.IsActive && hb.damagesPlayer && c != null && c.bounds.min.x - 0.3f <= px && c.bounds.max.x + 0.3f >= px;
                if (cov && hbCovering.Add(hb)) caveOver.Add(new OverRec { t = Time.time, ceil = false, src = "cover:" + hb.name });
                else if (!cov) hbCovering.Remove(hb);
            }
        foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None))
        {
            var c = s.GetComponent<Collider2D>();
            bool over = c != null && c.enabled && c.bounds.min.x - 0.4f <= px && c.bounds.max.x + 0.4f >= px;
            var id = s;
            if (over && strikeOver.Add(id)) caveOver.Add(new OverRec { t = Time.time, ceil = false, src = "strike" });
        }
        foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None))
        {
            if (f == null || f.reflected) continue;
            var id = f;
            // 2026-10-15: 速い火球は1フレームで 1.6m を飛び越えることがあり、近くを通ったかがフレームの間隔で変わっていた →
            // 前のフレームとの間にプレイヤーの横の位置を横切った時も数える(高さの差 2.5m 以内)
            float dx = f.transform.position.x - px, dyy = Mathf.Abs(f.transform.position.y - pc.transform.position.y - 0.8f);
            bool crossed = fbPrevDx.TryGetValue(id, out float pdx) && Mathf.Sign(pdx) != Mathf.Sign(dx) && dyy < 2.5f;
            fbPrevDx[id] = dx;
            // 巨大火球(3倍)は当たり判定が大きく、中心が 1.6m に入る前にプレイヤーに当たって消えることがある → 当たり判定どうしの距離も見る
            var fcol = f.GetComponent<Collider2D>(); var pcol = pc.GetComponent<Collider2D>();
            bool touching = fcol != null && pcol != null && fcol.enabled && fcol.Distance(pcol).distance < 0.6f;
            if (((Mathf.Abs(dx) < 1.6f && dyy < 2.5f) || crossed || touching) && fbOver.Add(id))
                caveOver.Add(new OverRec { t = Time.time, ceil = false, src = "fireball" });
        }
    }

    // 2026-10-15 切り分け: 竜の巨大火球の、プレイヤーから見た位置の流れ(0.25秒ごと)
    readonly List<string> fireTrace = new List<string>();
    float fireTraceAt, fireTraceLastX, fireMinDist = float.MaxValue;
    void DragonFireTrace(IBossBattleDebug b)
    {
        var d = b as DragonController;
        if (d == null || d.LastUltFireball == null) return;
        if (!d.LastUltFireball.reflected)
        {
            // 火球の当たり判定とプレイヤーの当たり判定の一番近づいた距離(巨大火球は半径が大きい)
            var fc = d.LastUltFireball.GetComponent<Collider2D>(); var pcol = pc.GetComponent<Collider2D>();
            float dist = fc != null && pcol != null ? Mathf.Max(0f, fc.Distance(pcol).distance) : Vector2.Distance(d.LastUltFireball.transform.position, pc.transform.position);
            fireMinDist = Mathf.Min(fireMinDist, dist);
        }
        if (Time.time < fireTraceAt) return;
        float dtS = Time.time - (fireTraceAt - 0.25f);
        fireTraceAt = Time.time + 0.25f;
        Vector3 f = d.LastUltFireball.transform.position, p = pc.transform.position;
        float vx = fireTrace.Count > 0 ? (p.x - fireTraceLastX) / Mathf.Max(0.01f, dtS) : 0f;
        fireTraceLastX = p.x;
        fireTrace.Add($"({f.x - p.x:F1},{f.y - p.y:F1}|{vx:F1}/{pc.CurrentAutoRunSpeed:F1})");
    }

    IEnumerator SkyBossMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        HookCaveLogs();
        Random.InitState(20261005);
        BossManager.NetTestBossHpOverride = 50000000;
        yield return BeginRun("swordsman", "sky_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var bm = Bm;
        bm.DebugPoolReset();
        var tn = BossBattleTuning.I;
        string[] keys = { "SkyDragon", "Majin", "Behemoth", "Titan", "Jellyfish", "Leviathan", "Fenrir", "SkyGolem", "Phoenix", "SkySerpent", "Guardian" };
        int have = keys.Count(k => tn.For(k).key == k);
        L($"[tuning] resumeStages=[{string.Join(",", tn.resumeStages)}] skyEntries={have}/11 battleTuned=[{string.Join(",", BossManager.BattleTunedStages)}]");
        Check(System.Array.IndexOf(tn.resumeStages, "sky_corridor") >= 0 && have == 11, "sky corridor uses run resume and all 11 sky bosses have battle tuning");
        WarpTo(1500f);
        yield return new WaitForSeconds(1f);
        int spawned0 = CaveHazard.Spawned;

        string only = Arg("-qaSkyOnly", "");
        foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind)))
        {
            if (only != "" && !only.Split(',').Contains(k.ToString())) continue;
            yield return OneSkyBoss(k);
        }
        string cases = Arg("-qaSkyCases", "JKLMNO");
        if (only == "" || only == "none" || Arg("-qaSkyCases", "") != "")
        {
            if (cases.Contains("J")) yield return SkyZakoCase();
            if (cases.Contains("K")) yield return SkyRematchCase();
            if (cases.Contains("L")) yield return SkyHighTierCase();
            if (cases.Contains("M")) yield return SkySpeedCase("M-fast", 300f);
            if (cases.Contains("N")) yield return SkySpeedCase("N-slow", 30f);
            if (cases.Contains("O")) yield return SkyGateCarryCase();
        }
        int made = CaveHazard.Spawned - spawned0;
        L($"[pool] hazards built={made} reused={CaveHazard.Reused} live={CaveHazard.LiveCount} delayedBySafety={CaveBossSafety.Delayed} pitSkips={CaveBossSafety.PitSkips}");
        Check(CaveHazard.Reused > made, $"T: terrain attacks are pooled (built {made}, reused {CaveHazard.Reused})");
        Check(CaveHazard.Violations == 0, $"E(all): floor and ceiling attacks never covered the player in the same frame ({CaveHazard.Violations})");
        L($"[summary] resumes={bossLogResumed} ultimates={bossLogUltimates} breaks={bossLogBreaks} pending={bossLogPending} recoveries={caveRecoveries.Count}");
        BossManager.NetTestBossHpOverride = 0;
        yield return EndRun();
    }

    IEnumerator WaitSkySpawn(float timeout = 14f)
    {
        float w = 0f;
        while (SkyBossAlive() == null && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        w = 0f;
        while (WildAlive().Any(b => b.IsEntering) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(1.5f); // 遠方の接近演出(ドラゴン/魔人は飛来)
    }

    IEnumerator OneSkyBoss(SkyBossKind k)
    {
        string n = k.ToString();
        var bm = Bm;
        float w0 = 0f;
        while ((bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        // 自動操作補助(実際のプレイに近い動き: 跳ぶ/近づいて攻撃する)はそのまま。攻撃で崩し(BREAK)が溜まると
        // 通常攻撃/必殺技の観察が途中で切れるので、ボスごとの確認の間は崩しを溜めない(強制の BREAK は効く)
        BossBattle.DebugNoStagger = true;
        bm.DebugSkyEncounter(k, -1);
        yield return WaitSkySpawn();
        var b = SkyBossAlive();
        Check(b != null, $"{n} A: spawned");
        if (b == null) { BossBattle.DebugNoStagger = false; yield break; }
        Check(b.PhaseCount >= 2, $"{n} A: battle tuning applied (phases {b.PhaseCount})");
        Shot($"sky_{n}_A_spawn");

        // B: 通常攻撃 + H: 攻撃の届く高さ
        int hz0 = CaveHazard.Spawned + CaveHazard.Reused;
        var strikes0 = new HashSet<Object>(FindObjectsByType<SkyStrike>(FindObjectsSortMode.None));
        int dragonAtk0 = b is DragonController dc0 ? dc0.AttacksStarted : 0;
        float unreach = 0f, longestUnreach = 0f;
        bool sawThreat = false; int frames = 0, reach = 0, untarget = 0; float w = 0f, sumDy = 0f, sumDx = 0f;
        while (w < 16f && b.DebugAlive)
        {
            if (FindObjectsByType<SkyStrike>(FindObjectsSortMode.None).Any(x => !strikes0.Contains(x)) || FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length > 0 || FindObjectsByType<FireballController>(FindObjectsSortMode.None).Length > 0) sawThreat = true;
            if (b is WildBossBase wb && wb.GetComponentsInChildren<BossHitbox>().Any(h => h.IsActive)) sawThreat = true;
            if (b is DragonController dc && dc.AttacksStarted > dragonAtk0) sawThreat = true;
            if (HurtBounds(b, out Bounds bb, out bool tg))
            {
                frames++; if (!tg) untarget++; sumDy += bb.min.y - pc.transform.position.y; sumDx += bb.center.x - pc.transform.position.x;
                bool inReach = tg && bb.min.y <= pc.transform.position.y + 4.2f && Mathf.Abs(bb.center.x - pc.transform.position.x) <= 13f;
                if (inReach) { reach++; unreach = 0f; } else { unreach += Time.deltaTime; longestUnreach = Mathf.Max(longestUnreach, unreach); }
            }
            w += Time.deltaTime; yield return null;
        }
        int hzB = CaveHazard.Spawned + CaveHazard.Reused - hz0;
        float reachFrac = frames > 0 ? reach / (float)frames : 0f;
        L($"[{n}] reach: frames={frames} untargetable={untarget} avg bottom-above-player={(frames > 0 ? sumDy / frames : 0f):F1}m avg dx={(frames > 0 ? sumDx / frames : 0f):F1}m");
        Check(sawThreat || hzB > 0, $"{n} B: phase-1 attacks happen (terrain {hzB}, threat seen {sawThreat})");
        // 「攻撃の届かない所に居続けない」: 届く時間がある(5%以上)+届かない時間が続くのは10秒まで(潜る/上空へ行くボスも必ず戻ってくる)
        Check(reachFrac >= 0.05f && longestUnreach <= 10f, $"H {n}: the boss comes back within attack reach ({reachFrac * 100f:F0}% of 16s, longest out of reach {longestUnreach:F1}s)");

        // C: 段階 → 第2段階の技
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(0.5f);
        Check(b.Phase == 2, $"{n} C: phase 2 (now {b.Phase})");
        int ult0 = b.UltimatesUsed;
        w = 0f; while (w < 14f && b.UltimatesUsed == ult0 && b.DebugAlive) { w += Time.deltaTime; yield return null; } // 段階2で自分から必殺技を使うまで(または特殊攻撃)
        w = 0f; while (w < 16f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(2.5f);

        // D/E/F: 必殺技
        ult0 = b.UltimatesUsed;
        int viol0 = CaveHazard.Violations, rec0 = caveRecoveries.Count, br0u = b.BreakCount;
        b.DebugForceUltimate();
        bool retried = false;
        w = 0f;
        while (w < 8f && !b.UltimateRunning && b.UltimatesUsed == ult0) { if (!retried && w > 3f) { retried = true; b.DebugForceUltimate(); } w += Time.deltaTime; yield return null; }
        Check(b.UltimateRunning || b.UltimatesUsed > ult0, $"{n} D: ultimate starts");
        float uStart = Time.time;
        int dragonFired0 = b is DragonController dcU ? dcU.UltFireballsFired : 0;
        fireMinDist = float.MaxValue; fireTrace.Clear();
        caveOver.Clear(); strikeOver.Clear(); fbOver.Clear(); hbOver.Clear(); hzCovered.Clear(); hbCovering.Clear();
        bool shotA = false, shotB = false;
        w = 0f;
        while (w < 28f && (b.UltimateRunning || w < 0.3f) && b.DebugAlive)
        {
            if (!shotA && w > 1.5f) { shotA = true; Shot($"sky_{n}_D_ultimate_a"); }
            if (!shotB && w > 4.5f) { shotB = true; Shot($"sky_{n}_D_ultimate_b"); }
            TrackSkyThreats(b);
            DragonFireTrace(b);
            w += Time.deltaTime; yield return null;
        }
        float uLen = Time.time - uStart;
        // 2026-10-15: 竜の必殺技は巨大火球を1つ放って(2.1秒)、2.2秒後に必殺技としては終わる。火球はゆっくり(6.5m/s、7秒)なので、
        // 遠い位置で放つと必殺技の終わりまでにプレイヤーへ届かない → 必殺技の時間だけ見ていると 0 になっていた(テストの見る時間の問題)。
        // 火球が消える/跳ね返されるまで見続け、跳ね返した(攻撃で返した)のも1回の判断として数える
        var dragon = b as DragonController;
        bool dragonFired = dragon != null && dragon.UltFireballsFired > dragonFired0;
        if (dragon != null && dragon.LastUltFireball != null)
        {
            var uf = dragon.LastUltFireball; float fw = 0f;
            while (fw < 7.5f && uf != null)
            {
                if (uf.reflected) { caveOver.Add(new OverRec { t = Time.time, ceil = false, src = "fireball reflected" }); break; }
                TrackSkyThreats(b);
                DragonFireTrace(b);
                fw += Time.deltaTime; yield return null;
            }
            L($"[{n}] giant fireball followed {fw:F1}s after the ultimate ended (gone={uf == null})");
            L($"[{n}] giant fireball path (dx,dy from the player / player x speed / run speed): {string.Join(" ", fireTrace)}");
            fireTrace.Clear();
            L($"[{n}] giant fireball closest to the player: {(fireMinDist < 1000f ? fireMinDist.ToString("F1") + "m" : "-")} (reflected={uf != null && uf.reflected})");
        }
        var ultWb = b as WildBossBase;
        if (ultWb != null && ultWb.UltSteps.Count > 0) L($"[{n}] ultimate steps: {string.Join(" > ", ultWb.UltSteps)}");
        var overs = caveOver.Where(o => o.t >= uStart - 0.1f).OrderBy(o => o.t).ToList();
        int decisions = 0; float last = -9f;
        foreach (var o in overs) { if (o.t - last > 0.35f) decisions++; last = o.t; }
        L($"[{n}] ultimate {uLen:F1}s: decisions={decisions} srcs=[{string.Join(",", overs.Select(o => o.src.Split(':').Last()).Distinct())}] breakDuringUlt={b.BreakCount > br0u}");
        Check(uLen < 26f, $"{n} D: ultimate ends ({uLen:F1}s)");
        int need = (k == SkyBossKind.Dragon || k == SkyBossKind.Majin) ? 1 : 3;
        if (dragon != null)
        {
            Check(dragonFired, $"{n} E: the giant fireball is fired");
            // 巨大火球は撃った時のプレイヤーの位置を狙う 1発(かわす/跳ね返す)。テストの剣士は自動攻撃の踏み込みで走る速さより速く前へ出るので、
            // 後ろ上から撃たれると背中の 2m ほど後ろへ落ちる(動いてかわした)。近くを通った(3.5m 以内)のも 1回の判断として数える
            if (decisions < 1 && fireMinDist < 3.5f) { decisions = 1; L($"[{n}] E: the giant fireball passed {fireMinDist:F1}m from the player (dodged by moving) - counted as a decision"); }
        }
        // 2026-10-15 切り分け: 「プレイヤーの位置に重なった回数」はプレイヤーの動きで揺れる(ベヒモスの落雷3本は周り2本+足元1本など)。
        // 段の記録があるボスは「必殺技の段が全部出たか」をボスの側で確かめ、重なった回数は 2 以上を目安にする
        bool stepsKnown = ultWb != null && ultWb.UltSteps.Count > 0;
        if (stepsKnown)
        {
            Check(ultWb.UltSteps.Contains("end"), $"{n} E: the ultimate runs to its last step [{string.Join(",", ultWb.UltSteps)}]");
            need = Mathf.Min(need, 2);
        }
        if (b is FenrirBoss fen && !float.IsNaN(fen.LastBiteGap))
            Check(Mathf.Abs(fen.LastBiteGap) < 9f, $"{n} E: the giant bite starts within reach of the player (gap {fen.LastBiteGap:F1}m)");
        if (b.BreakCount > br0u && decisions < need) L($"[{n}] E: ultimate was cut by a BREAK");
        else Check(decisions >= need, $"{n} E: the ultimate asks for at least {need} decisions ({decisions})");
        Check(CaveHazard.Violations == viol0, $"{n} E: no floor+ceiling overlap on the player during the ultimate ({CaveHazard.Violations - viol0})");
        yield return new WaitForSeconds(0.6f);
        Shot($"sky_{n}_F_recovery");
        Check(caveRecoveries.Count > rec0 || b.BreakCount > br0u, $"{n} F: a recovery window (or a BREAK) follows the ultimate");
        w = 0f; while (w < 5f && b.DebugAlive) { w += Time.deltaTime; yield return null; }

        // G: BREAK
        w = 0f; while (w < 6f && b.Broken) { w += Time.deltaTime; yield return null; }
        int br0 = b.BreakCount;
        b.DebugForceBreak();
        yield return new WaitForSeconds(0.4f);
        Check(b.BreakCount > br0 && b.Broken, $"{n} G: BREAK by stagger");
        Shot($"sky_{n}_G_break");
        float bd = BossBattleTuning.I.For(k == SkyBossKind.Dragon ? "SkyDragon" : n).breakDuration;
        w = 0f; while (w < bd + 3f && b.Broken) { w += Time.deltaTime; yield return null; }
        Check(!b.Broken, $"{n} G: recovers from BREAK");

        // I: ラン再開
        float d0 = gm.MaxDistance;
        bm.DebugForceResume();
        yield return new WaitForSeconds(3f);
        Check(bm.RunResumed && gm.MaxDistance > d0 + 10f && b.DebugAlive, $"{n} I: run resumed, distance counts, the boss keeps fighting ({d0:F0} -> {gm.MaxDistance:F0})");
        BossBattle.DebugNoStagger = false;
        L($"[kind] {n}: phase={b.Phase}/{b.PhaseCount} ultimates={b.UltimatesUsed} breaks={b.BreakCount} resumed={bm.RunResumed} reach={reachFrac * 100f:F0}%");
        KillSkyBosses();
        yield return new WaitForSeconds(0.5f);
        KillSkyBosses(); // フェニックスの復活(初登場)
        yield return WaitPhaseEnd(30f);
        Check(!bm.IsBossPhase, $"{n}: boss phase ends after the kill");
        yield return new WaitForSeconds(1.0f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator SkyZakoCase()
    {
        var bm = Bm;
        float w0 = 0f; while ((bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(1.5f);
        bm.DebugSkyEncounter(SkyBossKind.Behemoth, -1);
        yield return WaitSkySpawn();
        var b = SkyBossAlive();
        if (b == null) { Check(false, "J: behemoth spawned"); yield break; }
        int enc0 = caveEncPlanned;
        bm.DebugForceResume();
        int zako = 0, bonusZako = 0; float wz = 0f; bool bonusZone = false;
        while (wz < 12f)
        {
            var es = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.isActiveAndEnabled).ToList();
            zako = Mathf.Max(zako, es.Count(e => e.GetComponent<BonusEnemy>() == null));
            bonusZako = Mathf.Max(bonusZako, es.Count(e => e.GetComponent<BonusEnemy>() != null));
            bonusZone |= BonusZone.SuppressesNormalSpawns;
            wz += Time.deltaTime; yield return null;
        }
        // 2026-10-10: ラン再開の直後に BONUS ZONE(通常の敵の代わりに報酬の敵だけが出る区画、仕様)が始まると、通常の雑魚は出ない → 報酬の敵が出ていれば戻ったとみなす
        Check(zako > 0 || caveEncPlanned > enc0 || (bonusZone && bonusZako > 0), $"J: sky enemies come back after the run resumes with the boss (max {zako}, planned {caveEncPlanned - enc0}, BONUS ZONE {bonusZone} with {bonusZako} reward enemies)");
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(0.5f);
        // 2026-10-15 切り分け: 以前は自動の攻撃で崩しが溜まり、必殺技の開始の 0.3秒後に BREAK で必殺技が終わってから(仕様: BREAK は必殺技を止める)
        // 0.5秒後に「必殺技の間」を確かめていた → 必殺技の間でないので失敗していた。観察の間は攻撃で崩しを溜めない(強制の BREAK は効く)
        BossBattle.DebugNoStagger = true;
        int brJ = b.BreakCount, ultJ = b.UltimatesUsed;
        b.DebugForceUltimate();
        float w = 0f; bool retriedJ = false;
        while (w < 8f && !b.UltimateRunning) { if (!retriedJ && w > 3f) { retriedJ = true; b.DebugForceUltimate(); } w += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(0.5f);
        L($"[J] ultimate running={b.UltimateRunning} started={b.UltimatesUsed - ultJ} breaks={b.BreakCount - brJ} retried={retriedJ}");
        Check(b.UltimateRunning && CaveBossSafety.HoldZako && BossBattle.ZakoAttackScale < 1f && CaveBossSafety.ForceFlatTerrain, $"J: during the ultimate no new enemies / pits and slower enemy attacks (running={b.UltimateRunning} hold={CaveBossSafety.HoldZako} flat={CaveBossSafety.ForceFlatTerrain} breaks={b.BreakCount - brJ})");
        Shot("sky_J_zako_mix_ultimate");
        BossBattle.DebugNoStagger = false;
        w = 0f; while (w < 26f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        KillSkyBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
    }

    IEnumerator SkyRematchCase()
    {
        var bm = Bm;
        var pool = bm.DefeatedPool.ToList();
        L($"[K] pool: {string.Join(", ", pool)}");
        Check(pool.Count >= 3 && pool.All(p => p.StartsWith("Sky/")), $"K: defeated sky bosses are in the pool ({pool.Count})");
        float next = bm.NextBossDistance;
        int kk = Mathf.RoundToInt(next / 1000f);
        while (kk % 5 == 0) kk++;
        WarpTo(kk * 1000f - 50f);
        float w = 0f;
        while (!(bm.IsBossPhase && SkyBossAlive() != null) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(bm.CurrentEncounterIsRematch && pool.Contains(bm.CurrentEncounterKey), $"K: the {kk}km gate drew a defeated boss ({bm.CurrentEncounterKey}) | {bm.LastRematchDecision}");
        yield return new WaitForSeconds(2f);
        KillSkyBosses();
        yield return new WaitForSeconds(0.5f); KillSkyBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator SkyHighTierCase()
    {
        var bm = Bm;
        int top = BossRematchTuning.I.tiers.Count - 1;
        bm.DebugSkyEncounter(SkyBossKind.Behemoth, top);
        yield return WaitSkySpawn();
        var b = SkyBossAlive() as WildBossBase;
        if (b == null) { Check(false, "L: behemoth spawned"); yield break; }
        int basePhases = BossBattleTuning.I.For("Behemoth").phaseThresholds.Length + 1;
        Check(b.RematchTierApplied == top && b.PhaseCount > basePhases && BossManager.DamageMul > 1f, $"L: an early boss at the top rematch tier is stronger (phases {basePhases} -> {b.PhaseCount}, damage x{BossManager.DamageMul:F1})");
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(2.5f);
        float w = 0f; while (w < 18f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(1f);
        BossBattle.DebugNoStagger = true;
        b.DebugForceUltimate();
        w = 0f; while (w < 5f && !b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        int c0 = caveOver.Count; float t0 = Time.time; strikeOver.Clear(); hbOver.Clear(); hzCovered.Clear(); hbCovering.Clear();
        w = 0f; while (w < 28f && b.UltimateRunning) { TrackSkyThreats(b); w += Time.deltaTime; yield return null; }
        int decisions = 0; float last = -9f;
        foreach (var o in caveOver.Skip(c0).OrderBy(o => o.t)) { if (o.t - last > 0.35f) decisions++; last = o.t; }
        BossBattle.DebugNoStagger = false;
        L($"[L] upgraded THUNDER APOCALYPSE: {Time.time - t0:F1}s decisions={decisions}");
        // 2026-10-15 切り分け: 数えた物(何が・いつ・プレイヤーの位置)を全部出す
        L($"[L] counted: {string.Join(", ", caveOver.Skip(c0).OrderBy(o => o.t).Select(o => $"{o.src}@{o.t - t0:F2}"))}");
        // 2026-10-15 切り分け: 「プレイヤーの位置に重なった回数」は、プレイヤーの動き(跳ぶ/前へ出る)で 4〜6 と変わる(変更前の版でも 4 / 5)。
        // 強化の追加の段が実際に出たかを、ボスの側の記録(UltSteps)で確かめる。重なった回数は目安(3 以上)
        var beh = b as BehemothBoss; var steps = beh != null ? beh.UltSteps : new List<string>();
        L($"[L] steps: {string.Join(" > ", steps)}");
        Check(steps.Contains("strikes x5") && steps.Contains("wave behind") && steps.Contains("air band") && steps.Contains("charge") && steps.Contains("end"), $"L: the upgraded ultimate has its extra beats (5 strikes / wave from behind / air lightning) [{string.Join(",", steps)}]");
        Check(decisions >= 3, $"L: the upgraded ultimate threatens the player several times ({decisions} decisions)");
        KillSkyBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator SkySpeedCase(string tag, float kmh)
    {
        var bm = Bm;
        float w0 = 0f; while ((bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        bm.DebugSkyEncounter(SkyBossKind.Titan, -1);
        yield return WaitSkySpawn();
        SetKmh(kmh);
        var b = SkyBossAlive();
        if (b == null) { Check(false, $"{tag}: titan spawned"); yield break; }
        b.DebugSetPhase(2);
        yield return new WaitForSeconds(2.5f);
        float w = 0f; while (w < 20f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        // 攻撃の踏み込みでプレイヤーが前へ出ると予兆が相対的に後ろへずれる(それは避け方として正しい)ので、ここでは攻撃しない
        var hsaS = HighSpeedAssist.Instance; bool hsaSWas = hsaS != null && hsaS.assistEnabled;
        if (hsaS != null) hsaS.assistEnabled = false;
        b.DebugForceUltimate();
        float maxDrift = 0f; int samples = 0, ults0 = b.UltimatesUsed;
        var startRel = new Dictionary<Object, float>(); var startCam = new Dictionary<Object, float>(); var bornAt = new Dictionary<Object, float>();
        float maxCamDrift = 0f; string worst = "";
        float runFrame = 0f, lastPx = pc.transform.position.x + (float)FloatingOrigin.Offset, slowFrames = 0, allFrames = 0; // 走行の座標系(自動走行の速さの積分)
        var startRun = new Dictionary<Object, float>(); float maxRunDrift = 0f;
        w = 0f;
        while (w < 26f && (w < 4f || b.UltimateRunning) && b.DebugAlive)
        {
            float off = (float)FloatingOrigin.Offset; // 原点の移動をまたいでも同じ座標で比べる
            float px = pc.transform.position.x + off, cx = (Camera.main != null ? Camera.main.transform.position.x : pc.transform.position.x) + off;
            runFrame += pc.CurrentAutoRunSpeed * Time.deltaTime; allFrames++;
            if (allFrames > 1 && (px - lastPx) / Mathf.Max(0.0001f, Time.deltaTime) < pc.CurrentAutoRunSpeed * 0.7f) slowFrames++;
            lastPx = px;
            foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None))
            {
                float rel = (s.transform.position.x + off) - px; var id = s;
                if (!startRun.TryGetValue(id, out float q0)) startRun[id] = (s.transform.position.x + off) - runFrame;
                else maxRunDrift = Mathf.Max(maxRunDrift, Mathf.Abs((s.transform.position.x + off) - runFrame - q0));
                if (!startRel.TryGetValue(id, out float r0)) { startRel[id] = rel; startCam[id] = (s.transform.position.x + off) - cx; bornAt[id] = Time.time; }
                else
                {
                    float dr = Mathf.Abs(rel - r0), dc = Mathf.Abs((s.transform.position.x + off) - cx - startCam[id]);
                    if (dr > maxDrift) { maxDrift = dr; worst = $"{s.name} age {Time.time - bornAt[id]:F2}s rel {r0:F1}->{rel:F1} cam {startCam[id]:F1}->{(s.transform.position.x + off) - cx:F1} fired={s.Fired}"; }
                    maxCamDrift = Mathf.Max(maxCamDrift, dc); samples++;
                }
            }
            w += Time.deltaTime; yield return null;
        }
        if (hsaS != null) hsaS.assistEnabled = hsaSWas;
        float slowShare = allFrames > 0 ? slowFrames / allFrames : 0f;
        L($"[{tag}] strikes={startRel.Count} drift vs run frame {maxRunDrift:F2}m / vs player {maxDrift:F2}m / vs camera {maxCamDrift:F2}m, player slower than auto-run {slowShare * 100f:F0}% of frames | worst: {worst}");
        Check(b.UltimatesUsed > 0, $"{tag}: the ultimate happens at {kmh:0}km/h");
        Check(samples == 0 || maxRunDrift < 0.5f, $"{tag}: strike telegraphs move with the auto-run (screen) at {kmh:0}km/h (drift {maxRunDrift:F2}m)");
        if (slowShare < 0.05f) Check(samples == 0 || maxDrift < 2.0f, $"{tag}: strike telegraphs stay where they were shown relative to a running player (knockback ≤2m) at {kmh:0}km/h (max drift {maxDrift:F2}m)");
        else L($"[{tag}] the player was blocked/slowed for {slowShare * 100f:F0}% of the frames (no attacks/jumps in this check) - relative drift not judged");
        PlayerController.DebugSpeedScale = 1f;
        KillSkyBosses();
        yield return WaitPhaseEnd(30f);
        yield return new WaitForSeconds(1f);
        if (bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
    }

    IEnumerator SkyGateCarryCase()
    {
        var bm = Bm;
        float w0 = 0f; while ((bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        float next = bm.NextBossDistance;
        WarpTo(next - 50f);
        float w = 0f;
        while (!(bm.IsBossPhase && SkyBossAlive() != null) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(bm.AliveBossCount > 0, $"O: boss at {next:F0}m spawned");
        int first = bm.AliveBossCount;
        bm.DebugForceResume();
        int pend0 = bossLogPending;
        float passTo = next + 2150f; int maxAlive = 0; w = 0f;
        yield return new WaitForSeconds(0.5f);
        SetKmh(400f);
        while (gm.MaxDistance < passTo && w < 120f) { maxAlive = Mathf.Max(maxAlive, bm.AliveBossCount); w += Time.deltaTime; yield return null; }
        PlayerController.DebugSpeedScale = 1f;
        Check(maxAlive <= first && bm.IsBossPhase, $"O: no second boss while the first is alive (max alive {maxAlive}, first {first})");
        KillSkyBosses(); yield return new WaitForSeconds(0.5f); KillSkyBosses();
        yield return WaitPhaseEnd(30f);
        Check(bossLogPending > pend0, $"P: one passed gate is held as pending | {bm.LastGateDecision}");
        w = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(bm.IsBossPhase && bm.AliveBossCount > 0, $"P: the pending boss appears after the reward ({bm.CurrentEncounterKey})");
        yield return new WaitForSeconds(2f);
        KillSkyBosses(); yield return new WaitForSeconds(0.5f); KillSkyBosses();
        yield return WaitPhaseEnd(30f);
    }
}
#endif

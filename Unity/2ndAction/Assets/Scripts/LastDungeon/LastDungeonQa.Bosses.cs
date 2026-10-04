#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ラスダンのボス構成(2026-10-05)の自動確認。 -ldQa <dir> -ldQaMode bosses
//  A プール(格ごとの中身、キーが正しい)                   B 0〜89km の抽選(格が合う / 直近の重複を避ける / 短いプールは緩める)
//  C 0〜89km の関門で実際に出る(距離の強さ+ラスダンの倍率+攻撃間隔/移動速度)
//  D ラン再開(0〜89km)+ボスラッシュの入口での足止め(ボスラッシュを飛ばさない/保留にしない)
//  E 90〜98km の各関門: 構成どおりの数が出る / 同時の上限 / タグの規則 / 時間・HP・撃破の増援 / 雑魚なし / 二重生成なし / FPS
//  F NEXT BOSS(遭遇のボスを全員片付けて次の関門へ、95km)  G 撃破の処理を通らずに消えたボス(見張り、95km)
//  H 98km の後は関門なし(99km〜静寂)                       S 例外なし
public partial class LastDungeonQa
{
    IEnumerator BossesMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        yield return BeginRun(ch);
        var bm = BossManager.Instance;
        var ld = LastDungeonBossTuning.I;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);

        // ---- A: プール
        foreach (var (name, pool) in new[] { ("1k", ld.pool1k), ("5k", ld.pool5k), ("10k", ld.pool10k) })
        {
            int bad = pool.Count(k => !ParseKeyQa(k));
            L($"[A] pool {name} ({pool.Count}): {string.Join(", ", pool)}");
            Check(pool.Count > 0 && bad == 0, $"A: pool {name} has valid keys ({pool.Count}, bad {bad})");
        }
        Check(ld.pool10k.Count >= 27 && ld.pool1k.Count == 3 && ld.pool5k.Count == 3, "A: 1,000m/5,000m/10,000m classes come from all three maps");

        // ---- B: 抽選(実際の関門の処理を呼ぶ。直近の並びは BeginEncounterKey と同じ recentFought)
        var resolve = typeof(BossManager).GetMethod("ResolveLastMilestone", NP);
        var gateF = typeof(BossManager).GetField("gateK", NP);
        var recent = (List<string>)typeof(BossManager).GetField("recentFought", NP).GetValue(bm);
        var keyM = typeof(BossManager).GetMethod("Key", BindingFlags.NonPublic | BindingFlags.Static);
        recent.Clear();
        int wrongClass = 0, repeat10 = 0, repeatShort = 0, draws = 0;
        var seen10 = new HashSet<string>();
        var drawLog = new List<string>();
        for (int round = 0; round < 3; round++)
            for (int k = 1; k <= 89; k++)
            {
                gateF.SetValue(bm, k);
                object[] args = { k, null, null, null };
                bool ok = (bool)resolve.Invoke(bm, args);
                if (!ok) { wrongClass++; continue; }
                string key = (string)keyM.Invoke(null, new[] { args[1], args[2] });
                var pool = k % 10 == 0 ? ld.pool10k : k % 5 == 0 ? ld.pool5k : ld.pool1k;
                if (!pool.Contains(key)) wrongClass++;
                if (k % 10 == 0) { if (recent.Take(3).Contains(key)) repeat10++; seen10.Add(key); }
                else if (recent.Count > 0 && recent[0] == key && pool.Count > 2) repeatShort++;
                recent.Remove(key); recent.Insert(0, key); while (recent.Count > 6) recent.RemoveAt(recent.Count - 1);
                draws++;
                if (round == 0 && (k <= 12 || k % 10 == 0)) drawLog.Add($"{k}km={key}");
            }
        recent.Clear();
        L($"[B] {string.Join(" ", drawLog)}");
        L($"[B] draws={draws} wrongClass={wrongClass} 10k-repeat-in-last3={repeat10} short-pool-immediate-repeat={repeatShort} distinct 10k={seen10.Count}");
        Check(wrongClass == 0, "B: every gate draws from its own class pool");
        Check(repeat10 == 0, "B: 10,000m class never repeats one of the last 3");
        Check(repeatShort == 0, "B: short pools (3 bosses) never repeat the previous boss");

        // ---- C: 実際の関門(10km=10,000m格 / 15km=5,000m格 / 21km=1,000m格)
        foreach (int k in new[] { 10, 15, 21 })
        {
            yield return WaitNoBoss(bm);
            WarpQa(k * 1000f - 40f);
            float w = 0f;
            while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
            var b = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(x => x != null && !x.IsDead);
            string key = bm.CurrentEncounterKey;
            var pool = k % 10 == 0 ? ld.pool10k : k % 5 == 0 ? ld.pool5k : ld.pool1k;
            L($"[C] {k}km: {bm.LastRematchDecision} | key={key} hpMul={bm.CurrentRematchHpMul:F2} wild={(b != null ? $"{b.bossName} hp={b.maxHp} pace={b.AiWaitScale:F2}/{b.MoveScale:F2}" : "(non-wild boss)")}");
            Check(bm.IsBossPhase && pool.Contains(key), $"C: {k}km gate fights a boss from its class ({key})");
            Check(bm.CurrentRematchHpMul >= ld.hpMul - 0.01f, $"C: {k}km boss has the last-dungeon HP multiplier (×{bm.CurrentRematchHpMul:F2})");
            if (b != null) Check(Mathf.Approximately(b.AiWaitScale, ld.attackIntervalMul) && Mathf.Approximately(b.MoveScale, ld.moveSpeedMul), $"C: {k}km boss uses the last-dungeon attack interval / move speed");
            Shot($"ldboss_C_{k}km");
            yield return KillAllQa(bm);
        }

        // ---- D: ラン再開 → ボスラッシュの入口で足止め → 撃破後に 90km のボスラッシュが飛ばされずに始まる
        yield return WaitNoBoss(bm);
        WarpQa(88960f);
        float wd = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && wd < 25f) { yield return null; wd += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(2.5f);
        bm.DebugForceResume();
        yield return new WaitForSeconds(0.5f);
        Check(bm.RunResumed, "D: a 89km boss can resume the run (milestone gates)");
        PlayerController.DebugSpeedScale = 4f;
        float dStart = gm.MaxDistance; wd = 0f; bool held = false; float heldAt = 0f;
        while (wd < 90f) { if (bm.HoldsRun && bm.RushEntranceHolds > 0) { held = true; heldAt = gm.MaxDistance; break; } wd += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(2f);
        float dHeld = gm.MaxDistance;
        PlayerController.DebugSpeedScale = 1f;
        L($"[D] resumed at {dStart:F0}m, held={held} at {heldAt:F0}m, 2s later {dHeld:F0}m, gate decision: {bm.LastGateDecision}");
        Check(held && heldAt < 90000f && Mathf.Abs(dHeld - heldAt) < 1f, "D: the resumed boss is held before the boss rush (distance stops)");
        yield return KillAllQa(bm);
        yield return WaitNoBoss(bm);
        wd = 0f;
        while (bm.RushGateK == 0 && wd < 40f) { yield return null; wd += Time.unscaledDeltaTime; }
        Check(bm.RushGateK == 90, $"D: after the held boss the 90km rush starts (not skipped/pending) (gate {bm.RushGateK})");

        // ---- E: 90〜98km
        var report = new List<string>();
        int overCap = 0, tagViolations = 0, zakoInRush = 0, doubleSpawn = 0;
        for (int k = 90; k <= 98; k++)
        {
            if (bm.RushGateK != k)
            {
                yield return WaitNoBoss(bm);
                WarpQa(k * 1000f - 40f);
                float w = 0f;
                while (bm.RushGateK != k && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            }
            var enc = bm.CurrentBossEncounter;
            if (enc == null || !enc.rush || enc.gateK != k) { Check(false, $"E: {k}km rush gate started"); continue; }
            if (k == 95)
            {
                // ---- G: 撃破の処理を通らずに1体消える(見張りが残り数から外す)
                yield return new WaitForSeconds(3f);
                var victim = enc.activeBosses.FirstOrDefault(x => x != null && !x.IsDead);
                int remain0 = enc.Remaining;
                if (victim != null) Destroy(victim.gameObject);
                yield return new WaitForSeconds(1f);
                L($"[G] 95km: remaining {remain0} -> {enc.Remaining}, watchdog fixes {enc.watchdogFixes}");
                Check(enc.watchdogFixes >= 1 && enc.Remaining == remain0 - 1, "G: a boss that vanished without the defeat callback is counted (the next can spawn)");
                Shot("ldboss_G_95km");
                // ---- F: NEXT BOSS(待っているボスも含めて一度で片付く → 次の関門へ)
                EndgameDebug.NextBoss();
                float wn = 0f;
                while (bm.IsBossPhase && !bm.BossDefeatedThisPhase && wn < 10f) { yield return null; wn += Time.unscaledDeltaTime; }
                Check(bm.BossDefeatedThisPhase || !bm.IsBossPhase, $"F: NEXT BOSS clears the whole encounter (incl. waiting bosses) in one press ({wn:F1}s)");
                wn = 0f;
                while (bm.RushGateK != 96 && wn < 60f) { yield return null; wn += Time.unscaledDeltaTime; }
                L($"[F] NEXT BOSS: {EndgameDebug.Instance?.Status} -> gate {bm.RushGateK}");
                Check(bm.RushGateK == 96, $"F: NEXT BOSS moves on to the next gate (now {bm.RushGateK})");
                continue;
            }
            int total = enc.total, maxTog = 0, zakoGate = 0; float t0 = Time.time;
            int expect = k <= 91 ? 2 : k <= 92 ? 2 : k <= 94 ? 3 : k <= 95 ? 3 : k <= 97 ? 4 : 5;
            fpsSum = 0f; fpsN = 0; fpsMin = 999f;
            var ids = new HashSet<WildBossBase>();
            var spawnOrder = new List<string>();
            bool shot = false;
            float wg = 0f;
            // 倒し方: 先頭のボスを HP 40% まで削る(HPで増援) → 6秒待つ(時間で増援) → 1体ずつ倒す(撃破で次)
            int stage = 0; float stageT = Time.time;
            while (bm.IsBossPhase && wg < 150f)
            {
                SampleFps();
                var alive = bm.ActiveBosses.Where(x => x != null && !x.IsDead).ToList();
                foreach (var a in alive) if (ids.Add(a)) spawnOrder.Add(a.bossName);
                maxTog = Mathf.Max(maxTog, alive.Count);
                if (alive.Count > ld.maxSimultaneous) overCap++;
                if (!RushTagsOk(alive, ld)) tagViolations++;
                int zn = ActiveEnemies(); zakoGate = Mathf.Max(zakoGate, zn); zakoInRush = Mathf.Max(zakoInRush, OnScreenEnemies());
                if (!shot && alive.Count >= 2 && Time.time - t0 > 3f) { shot = true; Shot($"ldboss_E_{k}km"); }
                if (stage == 0 && alive.Count > 0 && Time.time - stageT > 3f) { var a = alive[0]; a.TakeDamage(Mathf.RoundToInt(a.Hp * 0.6f), a.CenterWorld); stage = 1; stageT = Time.time; }
                else if (stage == 1 && Time.time - stageT > 6f) { stage = 2; stageT = Time.time; }
                else if (stage == 2 && Time.time - stageT > 2.5f && alive.Count > 0) { var a = alive[0]; a.TakeDamage(Mathf.Max(99999999, a.Hp), a.CenterWorld); stageT = Time.time; }
                else if (stage == 2 && alive.Count == 0 && Time.time - stageT > 4f && bm.AliveBossCount > 0) { stageT = Time.time; } // 撃破の演出中/出現待ち
                wg += Time.deltaTime;
                yield return null;
            }
            int spawned = enc.spawned;
            if (ids.Count > total) doubleSpawn++;
            string line = $"{k}km [{enc.label}] bosses {spawned}/{total} (expected {expect}) max together {maxTog} deferred(frames) {enc.deferredByRules} zako {zakoGate} took {Time.time - t0:F0}s order [{string.Join(" > ", spawnOrder)}] | {PerfLine()}";
            report.Add(line);
            L("[E] " + line);
            Check(!bm.IsBossPhase || bm.BossDefeatedThisPhase, $"E: {k}km rush gate completes");
            Check(spawned == total && total >= expect, $"E: {k}km spawns every boss of the encounter once ({spawned}/{total}, expected ≥{expect})");
            yield return new WaitForSeconds(0.5f);
        }
        L($"[E] over-cap frames={overCap} tag-rule frames={tagViolations} zako on screen max={zakoInRush} cleared by the rush={BossManager.Instance.RushZakoCleared} double spawns={doubleSpawn} max together (run)={BossManager.MaxBossesTogetherSeen}");
        Check(overCap == 0 && BossManager.MaxBossesTogetherSeen <= ld.maxSimultaneous, $"E: never more than {ld.maxSimultaneous} bosses together");
        Check(tagViolations == 0, "E: tag rules (LARGE/AIR/AREA_ATTACK/PROJECTILE/CHASER) are respected");
        Check(zakoInRush == 0, "E: no normal enemies on screen during the boss rush (ones placed before the rush are cleared off-screen)");
        Check(doubleSpawn == 0, "E: no double spawns");

        // ---- H: 98km の後は関門なし → 静寂
        yield return WaitNoBoss(bm);
        float wh = 0f; int gatesAfter = 0;
        PlayerController.DebugSpeedScale = 4f;
        while (gm.MaxDistance < 99300f && wh < 90f) { if (bm.IsBossPhase && !bm.BossDefeatedThisPhase) gatesAfter++; wh += Time.deltaTime; yield return null; }
        PlayerController.DebugSpeedScale = 1f;
        var flow = LastDungeonFlow.Instance;
        L($"[H] at {gm.MaxDistance:F0}m flow={flow?.Current} suppress={BossManager.SuppressGates} boss frames after 98k={gatesAfter}");
        Check(gatesAfter == 0 && flow != null && flow.Current == LastDungeonFlow.State.Silence, "H: no gate after 98km, the silence starts at 99km");

        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
    }

    // 画面に映っている(カメラの範囲+1m)通常の敵の数
    int OnScreenEnemies()
    {
        var cam = Camera.main; if (cam == null) return 0;
        float hw = cam.orthographicSize * cam.aspect + 1f, cx = cam.transform.position.x;
        int n = 0;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e.gameObject.activeInHierarchy && !e.IsDying && e.GetComponent<BonusEnemy>() == null && Mathf.Abs(e.transform.position.x - cx) < hw) n++;
        return n;
    }

    static bool ParseKeyQa(string key)
    {
        int i = key.IndexOf('/'); if (i <= 0) return false;
        string f = key.Substring(0, i), n = key.Substring(i + 1);
        return f == "Wild" ? System.Enum.TryParse(n, out WildBossKind _) : f == "Cave" ? System.Enum.TryParse(n, out CaveBossKind _) : f == "Sky" && System.Enum.TryParse(n, out SkyBossKind _);
    }

    bool RushTagsOk(List<WildBossBase> alive, LastDungeonBossTuning ld)
    {
        var keyOf = typeof(BossManager).GetMethod("KeyOf", NP);
        int large = 0, air = 0, area = 0, proj = 0, chase = 0;
        foreach (var b in alive)
        {
            var t = ld.TagsOf((string)keyOf.Invoke(BossManager.Instance, new object[] { b }));
            if ((t & LastDungeonBossTuning.Tag.Large) != 0) large++;
            if ((t & LastDungeonBossTuning.Tag.Air) != 0) air++;
            if ((t & LastDungeonBossTuning.Tag.AreaAttack) != 0) area++;
            if ((t & LastDungeonBossTuning.Tag.Projectile) != 0) proj++;
            if ((t & LastDungeonBossTuning.Tag.Chaser) != 0) chase++;
        }
        return large <= ld.maxLarge && air <= ld.maxAir && area <= ld.maxAreaAttack && proj <= ld.maxProjectile && chase <= ld.maxChaser;
    }

    void WarpQa(float d)
    {
        gm.DebugWarpToDistance(d);
        ResetDistanceExclusion();
        warpedTo = d;
    }

    IEnumerator WaitNoBoss(BossManager bm)
    {
        float w = 0f;
        while ((bm.IsBossPhase || gm.IsRewardSequenceRunning) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator KillAllQa(BossManager bm)
    {
        for (int i = 0; i < 4 && bm.IsBossPhase && !bm.BossDefeatedThisPhase; i++)
        {
            bm.DebugFinishEncounter();
            yield return new WaitForSeconds(0.8f);
        }
        yield return WaitNoBoss(bm);
    }
}
#endif

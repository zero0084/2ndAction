#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ボスの再戦プール(2026-10-02)の確認。 -qaRematch <dir> [-qaRematchKm 62,26,26] [-qaRematchPending 1]
// 各ステージで関門ごとに戦闘を始めさせ、すぐ倒して次へ(関門の手前へワープ)。出てきたボス/再戦か/HP/プールを記録して確かめる。
public partial class QaSweep
{
    struct EncRec { public int km; public string key; public bool rematch; public int hp; public string tier; public List<string> poolBefore; public string decision; }

    IEnumerator RematchMode()
    {
        Application.targetFrameRate = 60;
        string[] kms = Arg("-qaRematchKm", "62,26,26").Split(',');
        var stages = new[] { "wasteland_road", "natural_cave", "sky_corridor" };
        var natives = new Dictionary<string, string[]>
        {
            { "wasteland_road", new[] { "Wild/Wolf", "Wild/GoblinRider", "Wild/Serpent", "Wild/Cyclops", "Wild/Spider", "Wild/Golem", "Wild/Griffin", "Wild/Hydra", "Wild/Demon", "Wild/Dragon", "Wild/BlackKnight" } },
            { "natural_cave", new[] { "Cave/Centipede", "Cave/Scorpion", "Cave/Mole", "Cave/Troll", "Cave/Worm", "Cave/CrystalGolem", "Cave/Bat", "Cave/ScorpionKing", "Cave/Basilisk", "Cave/Drake", "Cave/AncientDemon" } },
            { "sky_corridor", new[] { "Sky/Dragon", "Sky/Majin", "Sky/Behemoth", "Sky/Titan", "Sky/Jellyfish", "Sky/Leviathan", "Sky/Fenrir", "Sky/SkyGolem", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Guardian" } },
        };
        for (int si = 0; si < stages.Length; si++)
        {
            string st = stages[si];
            int limitKm = si < kms.Length ? int.Parse(kms[si]) : 20;
            yield return BeginRun("swordsman", st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            var bm = BossManager.Instance; bm.enabled = true;
            var recs = new List<EncRec>();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 900f)
            {
                // 次の関門の手前へ
                float w = 0f;
                while (bm.IsBossPhase && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
                if (bm.NextBossDistance > limitKm * 1000f + 10f) break;
                if (gm.MaxDistance < bm.NextBossDistance - 60f) gm.DebugWarpToDistance(bm.NextBossDistance - 40f);
                var poolBefore = bm.DefeatedPool.ToList();
                w = 0f;
                while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
                if (!bm.IsBossPhase) { L($"[rm:{st}] no encounter near {bm.NextBossDistance:F0}m"); continue; }
                yield return new WaitForSeconds(0.5f);
                // 空から現れるボス(天空のドラゴン/魔人)は少し遅れて出る
                w = 0f;
                while (BossObjectsAlive() == 0 && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
                int hp = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Where(b => !b.IsDead).Sum(b => b.maxHp)
                       + FindObjectsByType<DragonController>(FindObjectsSortMode.None).Where(b => !b.IsDead).Sum(b => b.maxHp)
                       + FindObjectsByType<MajinController>(FindObjectsSortMode.None).Where(b => !b.IsDead).Sum(b => b.maxHp);
                var r = new EncRec { km = bm.CurrentGateIndex, key = bm.CurrentEncounterKey, rematch = bm.CurrentEncounterIsRematch, hp = hp, tier = bm.CurrentRematchTier, poolBefore = poolBefore, decision = bm.LastRematchDecision };
                recs.Add(r);
                L($"[rm:{st}] {r.km,3}km {r.key,-20} {(r.rematch ? "REMATCH " + r.tier : "")} hp={r.hp} alive={bm.AliveBossCount} | {r.decision}");
                w = 0f;
                while (bm.IsBossPhase && w < 30f) { if (bm.AliveBossCount > 0) KillAllBosses(); yield return new WaitForSeconds(0.25f); w += 0.25f; }
            }
            string[] nat = natives[st];
            // A/B: 1kmと5kmは本来のボスの初登場
            var g1 = recs.FirstOrDefault(x => x.km == 1); var g5 = recs.FirstOrDefault(x => x.km == 5);
            Check(g1.key == nat[0] && !g1.rematch, $"{st} A: 1,000m is the first {nat[0]} ({g1.key})");
            Check(g5.key == nat[1] && !g5.rematch, $"{st} B: 5,000m is the first {nat[1]} ({g5.key})");
            // C: 10kmごとは専用ボス(固定)
            bool cOk = true;
            foreach (var x in recs.Where(x => x.km % 10 == 0 && x.km < 100)) { int i = x.km / 10 + 1; if (i >= nat.Length || x.key != nat[i] || x.rematch) { cOk = false; L($"[rm:{st}] C NG {x.km}km {x.key}"); } }
            Check(cOk && recs.Any(x => x.km == 10), $"{st} C: every 10km gate is its fixed boss (never random)");
            // D: 10km撃破後にプールへ
            var after10 = recs.FirstOrDefault(x => x.km == 11);
            Check(after10.poolBefore != null && after10.poolBefore.Contains(nat[2]), $"{st} D: the 10km boss is in the pool after it is defeated");
            // E: 11km以降で撃破済みが再登場(専用ボスも含む)
            var rem = recs.Where(x => x.rematch).ToList();
            Check(rem.Count > 0 && rem.Any(x => x.km > 10 && x.key == nat[2] || x.key != nat[0] && x.key != nat[1]), $"{st} E: defeated bosses come back as rematches after 10km ({rem.Count} rematches)");
            // F: 未撃破のボスは出ない
            bool fOk = rem.All(x => x.poolBefore.Contains(x.key));
            Check(fOk, $"{st} F: a rematch is always a boss already defeated in this run");
            // G: 直前と同じボスが続かない
            int same = 0;
            for (int i = 1; i < recs.Count; i++) if (recs[i].rematch && recs[i].key == recs[i - 1].key) same++;
            Check(same == 0, $"{st} G: the same boss never comes twice in a row by rematch ({same})");
            // H: 50km以降の偏り(荒野)
            var late = rem.Where(x => x.km >= 50).ToList();
            if (late.Count >= 6)
            {
                var top = late.GroupBy(x => x.key).OrderByDescending(g => g.Count()).First();
                int distinct = late.Select(x => x.key).Distinct().Count();
                L($"[rm:{st}] H: {late.Count} rematches after 50km, {distinct} kinds, most {top.Key} x{top.Count()} | " + string.Join(", ", late.GroupBy(x => x.key).Select(g => $"{g.Key.Split('/')[1]}x{g.Count()}")));
                Check(distinct >= 4 && top.Count() <= late.Count * 0.45f, $"{st} H: after 50km rematches are spread over {distinct} kinds (top {top.Count()}/{late.Count})");
            }
            // I: 再戦は距離に合わせて強い(同じ種類の初登場よりHPが多い)
            var first = recs.Where(x => !x.rematch).GroupBy(x => x.key).ToDictionary(g => g.Key, g => g.First());
            var cmp = rem.Where(x => first.ContainsKey(x.key) && x.km > first[x.key].km + 5).ToList();
            bool iOk = cmp.Count > 0 && cmp.All(x => x.hp > first[x.key].hp);
            foreach (var x in cmp.Take(4)) L($"[rm:{st}] I: {x.key} first {first[x.key].km}km hp={first[x.key].hp} -> rematch {x.km}km hp={x.hp} ({x.tier})");
            Check(iOk, $"{st} I: rematch bosses are stronger than their first appearance ({cmp.Count} compared)");
            // M: プールにはこのステージのボスだけ
            string fam = nat[0].Split('/')[0] + "/";
            Check(bm.DefeatedPool.All(k => k.StartsWith(fam) && nat.Contains(k)), $"{st} M: the pool holds only this stage's bosses ({string.Join(", ", bm.DefeatedPool)})");
            L($"[rm:{st}] encounters={recs.Count} rematches={rem.Count} pool={bm.DefeatedPool.Count}");
            yield return EndRun();
        }

        if (Arg("-qaRematchPending", "1") == "1") yield return RematchPendingTest();
        yield return RematchReaperCheck();
    }

    // J/K/L: ボスが生きたまま次の関門を何本も通過 → 重複して出ない / 保留は1件 / 10kmの専用ボスが優先される
    IEnumerator RematchPendingTest()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var bm = BossManager.Instance; bm.enabled = true;
        bm.DebugUnlockAll();
        gm.DebugWarpToDistance(8960f);
        float w = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        string firstKey = bm.CurrentEncounterKey;
        int startGate = bm.CurrentGateIndex;
        L($"[rm:pending] boss at {startGate}km {firstKey} alive={bm.AliveBossCount}");
        bm.ResumeRun();
        PlayerController.DebugSpeedScale = 8f;
        int maxAlive = 0, spawnsSeen = 0; var seen = new HashSet<WildBossBase>();
        w = 0f;
        while (gm.MaxDistance < 12600f && w < 180f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!b.IsDead && seen.Add(b)) spawnsSeen++;
            maxAlive = Mathf.Max(maxAlive, bm.AliveBossCount);
        }
        PlayerController.DebugSpeedScale = 1f;
        L($"[rm:pending] ran to {gm.MaxDistance:F0}m with the {startGate}km boss alive: spawns={spawnsSeen} maxAlive={maxAlive} next={bm.NextBossDistance:F0} decision='{bm.LastGateDecision}'");
        Check(spawnsSeen <= Mathf.Max(1, maxAlive) && bm.IsBossPhase, "J: passing gates while a boss is alive spawns no new boss");
        KillAllBosses();
        w = 0f; while (bm.IsBossPhase && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[rm:pending] after the kill: next gate {bm.NextBossDistance:F0}m ({bm.LastGateDecision}) pendingSkipped={bm.PendingSkippedCount}");
        Check(Mathf.Approximately(bm.NextBossDistance, 10000f), $"L: the 10km fixed boss is the one pending ({bm.NextBossDistance:F0}m)");
        w = 0f; while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[rm:pending] pending encounter: {bm.CurrentGateIndex}km {bm.CurrentEncounterKey} rematch={bm.CurrentEncounterIsRematch}");
        Check(bm.CurrentGateIndex == 10 && bm.CurrentEncounterKey == "Wild/Serpent" && !bm.CurrentEncounterIsRematch, "L: the pending 10km encounter is the fixed Serpent");
        KillAllBosses();
        w = 0f; while (bm.IsBossPhase && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[rm:pending] then next gate {bm.NextBossDistance:F0}m (d={gm.MaxDistance:F0})");
        Check(bm.NextBossDistance > gm.MaxDistance - 1f, "K: the gates passed meanwhile are not queued (no boss chain after the kill)");
        yield return EndRun();
    }

    int BossObjectsAlive() => FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Count(b => !b.IsDead)
        + FindObjectsByType<DragonController>(FindObjectsSortMode.None).Count(b => !b.IsDead)
        + FindObjectsByType<MajinController>(FindObjectsSortMode.None).Count(b => !b.IsDead);

    // N: 死神はプールへ入らない
    IEnumerator RematchReaperCheck()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var bm = BossManager.Instance; bm.enabled = true;
        bm.DebugSpawnReaper();
        yield return new WaitForSeconds(3f);
        Check(ReaperBase.Active != null && bm.DefeatedPool.All(k => !k.Contains("Reaper")), $"N: the reaper sisters are never in the rematch pool ({string.Join(", ", bm.DefeatedPool)})");
        yield return EndRun();
    }
}
#endif

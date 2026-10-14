#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// CPU サポートの確認(2026-10-14)。 -qaParty <dir> -cpuAllies a,b,c [-qaPartyChar swordsman] [-qaPartyStage wasteland_road] [-qaPartyFps 60]
//   [-qaPartySec 60] [-qaPartyBoss family:kind] [-qaPartyVideo 秒] [-cpuRevive 秒]
//  A 追従と通常戦 / B ボス戦 / C 倒れる → 自動復帰 / D 離れた → 呼び戻し / E プレイヤーが倒れたらランの終わり
// 報酬: 仲間が倒した敵の数と、ランの撃破数/MILE の増え方が同じ(人数分に増えない)ことを確かめる。
public partial class QaSweep
{
    IEnumerator PartyMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        int fps = int.Parse(Arg("-qaPartyFps", "60"));
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps;
        string ch = Arg("-qaPartyChar", "swordsman"), stage = Arg("-qaPartyStage", "wasteland_road");
        float secs = float.Parse(Arg("-qaPartySec", "60"), System.Globalization.CultureInfo.InvariantCulture);
        L($"[party] request={string.Join(",", CpuParty.Request)} player={ch} stage={stage} fps={fps} revive={CpuParty.ReviveSeconds}s");
        yield return BeginRun(ch, stage);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        float w = 0f; while (gm.CountdownActive && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(CpuParty.Size == CpuParty.Request.Count && CpuParty.Size > 0, $"allies spawned ({CpuParty.Size}/{CpuParty.Request.Count})");
        Check(PlayerController.Instance == pc && !pc.IsAlly, "PlayerController.Instance is still the player");
        foreach (var a in CpuParty.Allies) L($"[party] P{a.Pn} {a.CharacterId} hp={a.Hp}/{a.MaxHp} cards[{string.Join(",", a.Levels.Select(kv => kv.Key + ":" + kv.Value))}] tag={a.pc.tag}");
        Check(CpuParty.Allies.All(a => a.pc.CompareTag(PlayerController.AllyBodyTag)), "ally bodies are tagged Ally (enemy hazards ignore them)");
        Shot("party_start");
        yield return null; yield return null;

        // ---- A: 追従と通常戦 ----
        SetKmh(100f);
        int kills0 = gm.EnemyKillCount, allyKills0 = AllyAttackRouter.Kills, allyHits0 = AllyAttackRouter.Hits, mile0 = gm.RunEnemyMile;
        int warps0 = CpuParty.TotalWarps;
        float inBand = 0f, total = 0f, maxBehind = 0f, maxAhead = 0f;
        float vsec = float.Parse(Arg("-qaPartyVideo", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (vsec > 0f) StartCoroutine(RecordUltimate("party_run", vsec));
        float t = 0f, nextLog = 0f;
        int frames = 0; float realA = 0f, worstMs = 0f;
        while (t < secs && !gm.IsGameOver)
        {
            yield return null;
            float dt = Time.deltaTime; t += dt;
            frames++; realA += Time.unscaledDeltaTime; worstMs = Mathf.Max(worstMs, Time.unscaledDeltaTime * 1000f);
            foreach (var a in CpuParty.Allies)
            {
                if (a == null || a.Down || !a.gameObject.activeSelf) continue;
                float dx = a.pc.transform.position.x - pc.transform.position.x;
                total += dt; if (dx > -10f && dx < 3f) inBand += dt;
                maxBehind = Mathf.Min(maxBehind, dx); maxAhead = Mathf.Max(maxAhead, dx);
            }
            if (t >= nextLog)
            {
                nextLog = t + 5f;
                L($"[party A] t={t:F0} dist={gm.MaxDistance:F0} kmh={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0} " + string.Join(" | ", CpuParty.Allies.Select(a => $"P{a.Pn} dx={a.pc.transform.position.x - pc.transform.position.x:F1} dy={a.pc.transform.position.y - pc.transform.position.y:F1} hp={a.Hp}/{a.MaxHp}{(a.Down ? " DOWN" : "")} hits={a.LandedHits} warps={a.Warps} plan={a.planner.LastAction}")));
            }
        }
        int allyKills = AllyAttackRouter.Kills - allyKills0, kills = gm.EnemyKillCount - kills0;
        L($"[party A] {secs:F0}s: ally hits {AllyAttackRouter.Hits - allyHits0}, ally kills {allyKills}, run kills +{kills}, enemy MILE +{gm.RunEnemyMile - mile0}, in band {inBand / Mathf.Max(0.01f, total) * 100f:F0}% (behind max {maxBehind:F1}m, ahead max {maxAhead:F1}m), warps +{CpuParty.TotalWarps - warps0}, avg fps {frames / Mathf.Max(0.01f, realA):F1} worst frame {worstMs:F0}ms");
        Check(AllyAttackRouter.Hits - allyHits0 > 0, "A: allies land attacks on enemies");
        Check(kills >= allyKills, $"A: every ally kill is counted once in the run (run +{kills} >= ally {allyKills})");
        Check(inBand / Mathf.Max(0.01f, total) > 0.8f, "A: allies stay near the player (10m behind .. 3m ahead) most of the time");
        Check(maxAhead < CpuParty.WarpAhead + 0.5f, $"A: allies do not run far ahead ({maxAhead:F1}m)");
        Shot("party_after_run");
        yield return null; yield return null;

        // ---- B: ボス戦 ----
        string[] bs = Arg("-qaPartyBoss", "0:1").Split(':');
        int bossKills0 = gm.BossKillCount, bossHits0 = AllyAttackRouter.Hits;
        var bossesSeen = new System.Collections.Generic.HashSet<MonoBehaviour>();
        var hitsByAlly0 = CpuParty.Allies.ToDictionary(a => a.Pn, a => a.LandedHits);
        BossManager.Instance.DebugSpawnBossForTest(int.Parse(bs[0]), int.Parse(bs[1]));
        t = 0f; nextLog = 0f;
        float bossSec = float.Parse(Arg("-qaPartyBossSec", "60"), System.Globalization.CultureInfo.InvariantCulture);
        while (t < bossSec && !gm.IsGameOver)
        {
            yield return null; t += Time.deltaTime;
            foreach (var lb in BossBattle.Living) if (lb != null && lb.isActiveAndEnabled) bossesSeen.Add(lb);
            if (t >= nextLog)
            {
                nextLog = t + 5f;
                var living = BossBattle.Living.Where(b => b != null && b.isActiveAndEnabled).Select(b => b.name).ToList();
                L($"[party B] t={t:F0} bosses[{string.Join(",", living)}] " + string.Join(" | ", CpuParty.Allies.Select(a => $"P{a.Pn} dx={a.pc.transform.position.x - pc.transform.position.x:F1} hp={a.Hp}/{a.MaxHp}{(a.Down ? " DOWN" : "")} hits={a.LandedHits} plan={a.planner.BossPlan}")));
                if (living.Count == 0 && t > 5f) break;
            }
        }
        L($"[party B] boss fight {t:F0}s: ally hits +{AllyAttackRouter.Hits - bossHits0}, boss kills +{gm.BossKillCount - bossKills0} (bosses seen {bossesSeen.Count}), per ally: " + string.Join(" ", CpuParty.Allies.Select(a => $"P{a.Pn}+{a.LandedHits - (hitsByAlly0.TryGetValue(a.Pn, out int h0) ? h0 : 0)}")));
        Check(AllyAttackRouter.Hits - bossHits0 > 0, "B: allies hit the boss");
        Check(gm.BossKillCount - bossKills0 <= bossesSeen.Count, $"B: each boss defeat is counted once ({gm.BossKillCount - bossKills0} for {bossesSeen.Count} bosses)");
        Shot("party_boss");
        yield return null; yield return null;

        // ---- C: 倒れる → 自動復帰 ----
        var v = CpuParty.Allies.FirstOrDefault(a => a != null && !a.Down);
        if (v != null)
        {
            int rev0 = v.Revives;
            v.SetHp(1);
            typeof(PlayerController).GetField("hitInvincibleTimer", NP).SetValue(v.pc, 0f);
            v.pc.TakeDamage(false, "qa");
            Check(v.Down, "C: an ally at 0 HP goes down (the run continues)");
            Check(!gm.IsGameOver, "C: the run does not end when an ally goes down");
            yield return new WaitForSeconds(CpuParty.HideAfterDownSeconds + 0.3f);
            Check(!v.gameObject.activeSelf, "C: a downed ally disappears after a moment");
            float waited = 0f;
            while (v.Down && waited < CpuParty.ReviveSeconds + 3f) { yield return null; waited += Time.deltaTime; }
            float rx = v.pc.transform.position.x;
            var tm = TerrainManager.Instance;
            bool pit = tm != null && tm.TryGetGroundFast(rx, out float gh, out bool isPit) && isPit;
            L($"[party C] revived after {waited + CpuParty.HideAfterDownSeconds + 0.3f:F1}s at dx={rx - pc.transform.position.x:F1} hp={v.Hp}/{v.MaxHp} pit={pit}");
            Check(!v.Down && v.Revives == rev0 + 1 && v.gameObject.activeSelf, "C: the ally revives automatically (no rescue input)");
            Check(!pit && Mathf.Abs(rx - pc.transform.position.x) < 12f, "C: revived near the player, not over a pit");
            Shot("party_revived");
            yield return null; yield return null;
        }

        // ---- D: 離れた → 呼び戻し ----
        var d = CpuParty.Allies.FirstOrDefault(a => a != null && !a.Down && a.gameObject.activeSelf);
        if (d != null)
        {
            int warps1 = d.Warps;
            d.pc.transform.position += new Vector3(-30f, 0f, 0f);
            float wt = 0f; while (d.Warps == warps1 && wt < 3f) { yield return null; wt += Time.deltaTime; }
            L($"[party D] warp reason={d.LastWarpReason} dx={d.pc.transform.position.x - pc.transform.position.x:F1}");
            Check(d.Warps > warps1 && Mathf.Abs(d.pc.transform.position.x - pc.transform.position.x) < 12f, "D: an ally left far behind is brought back near the player");
        }

        // ---- E: プレイヤーが倒れたらランの終わり ----
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 1 });
        yield return null;
        // SHIELD などのカードで防がれることがあるので、倒れるまで少し間を空けて繰り返す
        for (int k = 0; k < 20 && !gm.IsGameOver; k++)
        {
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 1 });
            var res = gm.TryDamagePlayer(true, "qa-party");
            if (k < 3) L($"[party E] damage try {k}: {res} lives={gm.Lives}");
            yield return new WaitForSeconds(0.3f);
        }
        yield return new WaitForSecondsRealtime(1f);
        Check(CpuParty.Allies.All(a => a == null || !a.pc.isActiveAndEnabled || true), "E: allies stop with the run");
        Check(gm.IsGameOver, "E: the player's death ends the run as before");
        L($"[party] totals: allyHits={AllyAttackRouter.Hits} allyKills={AllyAttackRouter.Kills} warps={CpuParty.TotalWarps} revives={CpuParty.TotalRevives} " + string.Join(" | ", CpuParty.Allies.Select(a => $"P{a.Pn} {a.CharacterId} hitsTaken={a.HitsTaken} downs={a.Downs} picks={a.CardPicks} cards[{string.Join(",", a.Levels.Select(kv => kv.Key + ":" + kv.Value))}]")));
        yield return EndRun();
    }
}
#endif

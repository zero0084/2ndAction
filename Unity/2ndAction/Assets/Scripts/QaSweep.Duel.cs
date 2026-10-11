#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 平地での攻撃性能の切り分け(2026-10-11、お嬢様/格闘家の重点調査。部分試験 = 通しの成功には数えない):
//   -qaDuel <dir> [-duelChars swordsman,noble_lady,fighter] [-duelDecks B] [-duelDists 80000,100000] [-duelEnemies goblin,heavy_ogre]
//                 [-duelN 8] [-duelLv 9] [-duelBosses Cave:Troll,Wild:Golem,Sky:Majin] [-duelBossSecs 180]
// 平らな地面(穴/障害物/他の敵なし)を、その距離の自然な速さで走りながら、自動の補助(既定の設定)だけで前の敵1体と戦う。
// 敵はその距離の本来の HP(DistanceTierManager.EnemyHpFor)で出し、攻撃もしてくる(接触の被弾を数える)。1体ごとに HP を満タンへ戻す。
// 記録(1体 = 1行 duel.tsv / 集計 duel_summary.tsv / ボス duel_boss.tsv):
//   1発のダメージ・必要な命中数・撃破までの秒数 / 攻撃の判定の回数と命中数 / 届く距離にいた時間・その間に攻撃していた時間・接近待ちの時間 /
//   接触の被弾 / 攻撃の中断(被弾のよろけ、補助のジャンプ)/ 当たった連撃の段 / ノックバックで届く距離の外へ押し出した回数
public partial class QaSweep
{
    static readonly Dictionary<string, string[]> DuelDecks = new Dictionary<string, string[]>
    {
        { "B", new[] { "attack_up", "heart_up", "boss_killer", "shield", "attack_speed_up", "vampire", "sonic_blade", "second_wind", "double_attack", "phoenix", "iron_will", "speed_up" } },
        { "S_noble_lady", new[] { "jump_count_up", "speed_up", "ground_fighter", "heart_up", "attack_up", "boss_killer", "shield", "iron_will", "attack_speed_up", "ground_zero", "sonic_blade", "jump_power_up" } },
        { "S_fighter", new[] { "attack_range_up", "long_blade", "combo_rush", "attack_up", "boss_killer", "combo_plus", "ground_fighter", "ground_breaker", "heart_up", "shield", "vampire", "speed_up" } },
        { "S_swordsman", new[] { "combo_plus", "combo_rush", "attack_up", "boss_killer", "first_strike", "combo_master", "sonic_blade", "attack_speed_up", "heart_up", "shield", "vampire", "speed_up" } },
    };

    class DuelHit { public float t, dxAt, dxAfter = float.NaN; public int dmg, stage; }

    IEnumerator DuelModeQa()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] chars = Arg("-duelChars", "swordsman,noble_lady,fighter").Split(',');
        string[] decks = Arg("-duelDecks", "B").Split(',');
        float[] dists = Arg("-duelDists", "80000,100000").Split(',').Select(x => float.Parse(x, inv)).ToArray();
        string[] enemies = Arg("-duelEnemies", "goblin,heavy_ogre").Split(',');
        int perEnemy = int.Parse(Arg("-duelN", "8"));
        int lv = int.Parse(Arg("-duelLv", "9"));
        string[] bosses = Arg("-duelBosses", "").Split(',').Where(x => x.Contains(':')).ToArray();
        float bossSecs = float.Parse(Arg("-duelBossSecs", "180"), inv);
        var rows = new StringBuilder("char\tdeck\tdist\tenemy\tmaxHp\tbuildAtk\tresult\tsecs\thits\tswings\tavgDmg\testHits\tinReachS\tattackInReachS\tidleInReachS\tapproachS\tcontactHits\thurtBreaks\tassistJumps\tpushedOut\tstages\tmaxStage\n");
        var sum = new StringBuilder("char\tdeck\tdist\tenemy\tn\tkilled\tpassed\tmedianSecsToKill\tmedianHits\tavgDmg\tmedianEstHits\tswingsPerEnemy\thitRate\tinReachS\tattackShareInReach\tcontactHitsPerEnemy\thurtBreaksPerEnemy\tassistJumpsPerEnemy\tpushedOutPerHit\tstageHistogram\n");
        var bossRows = new StringBuilder("char\tdeck\tdist\tboss\tbossMaxHp\tresult\tsecs\thpLeft\thpDrops\tswings\tinReachS\tattackInReachS\tbossAboveS\tbossBehindS\tplayerHits\tplayerHpLost\treason\n");
        foreach (var ch in chars)
            foreach (var deckName in decks)
            {
                string key = deckName == "S" ? "S_" + ch : deckName;
                if (!DuelDecks.TryGetValue(key, out var deck)) { L($"[duel] {ch}: deck {key} not defined - skipped"); continue; }
                foreach (float d in dists)
                {
                    // ---- 雑魚
                    foreach (var enemyId in enemies)
                    {
                        yield return DuelBegin(ch, deck, lv, d, false);
                        var list = new List<(bool killed, bool passed, float secs, int hits, int swings, float avgDmg, int est, float inReach, float atkIn, int contact, int hurt, int ajump, int pushed, string stages)>();
                        var stageAll = new Dictionary<int, int>();
                        for (int i = 0; i < perEnemy && gm.HasStarted && !gm.IsGameOver; i++)
                        {
                            DuelResult r = null;
                            yield return DuelOne(enemyId, x => r = x);
                            if (r == null) { L($"[duel] {ch} {enemyId}: could not spawn"); break; }
                            foreach (var kv in r.stageHist) stageAll[kv.Key] = (stageAll.TryGetValue(kv.Key, out int c) ? c : 0) + kv.Value;
                            rows.Append($"{ch}\t{deckName}\t{d}\t{enemyId}\t{r.maxHp}\t{r.buildAtk}\t{(r.killed ? "killed" : r.passed ? "passed" : "timeout")}\t{r.secs:F2}\t{r.hits.Count}\t{r.swings}\t{r.AvgDmg:F0}\t{r.EstHits}\t{r.inReach:F2}\t{r.attackInReach:F2}\t{r.inReach - r.attackInReach:F2}\t{r.approach:F2}\t{r.contactHits}\t{r.hurtBreaks}\t{r.assistJumps}\t{r.pushedOut}\t{string.Join("/", r.hits.Select(h => h.stage))}\t{r.maxStage}\n");
                            list.Add((r.killed, r.passed, r.secs, r.hits.Count, r.swings, r.AvgDmg, r.EstHits, r.inReach, r.attackInReach, r.contactHits, r.hurtBreaks, r.assistJumps, r.pushedOut, ""));
                        }
                        if (list.Count > 0)
                        {
                            float Med(IEnumerable<float> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count > 0 ? l[l.Count / 2] : -1f; }
                            int totalHits = list.Sum(x => x.hits), totalSwings = list.Sum(x => x.swings);
                            float inR = list.Sum(x => x.inReach), atkIn = list.Sum(x => x.atkIn);
                            sum.Append($"{ch}\t{deckName}\t{d}\t{enemyId}\t{list.Count}\t{list.Count(x => x.killed)}\t{list.Count(x => x.passed)}\t{Med(list.Where(x => x.killed).Select(x => x.secs)):F2}\t{Med(list.Select(x => (float)x.hits)):F0}\t{(totalHits > 0 ? list.Where(x => x.hits > 0).Average(x => x.avgDmg) : 0):F0}\t{Med(list.Where(x => x.est > 0).Select(x => (float)x.est)):F0}\t{totalSwings / (float)list.Count:F1}\t{(totalSwings > 0 ? totalHits / (float)totalSwings : 0):F2}\t{inR / list.Count:F2}\t{(inR > 0 ? atkIn / inR : 0):F2}\t{list.Sum(x => x.contact) / (float)list.Count:F2}\t{list.Sum(x => x.hurt) / (float)list.Count:F2}\t{list.Sum(x => x.ajump) / (float)list.Count:F2}\t{(totalHits > 0 ? list.Sum(x => x.pushed) / (float)totalHits : 0):F2}\t{string.Join(" ", stageAll.OrderBy(kv => kv.Key).Select(kv => $"s{kv.Key}:{kv.Value}"))}\n");
                        }
                        L($"[duel] {ch} {deckName} {d:F0} {enemyId}: {list.Count} enemies, killed {list.Count(x => x.killed)}, passed {list.Count(x => x.passed)}");
                        Flush(rows, sum, bossRows);
                        yield return DuelEnd();
                    }
                    // ---- ボス
                    foreach (var bk in bosses)
                    {
                        yield return DuelBegin(ch, deck, lv, d, true);
                        yield return DuelBoss(ch, deckName, d, bk, bossSecs, bossRows);
                        Flush(rows, sum, bossRows);
                        yield return DuelEnd();
                    }
                }
            }
        Flush(rows, sum, bossRows);
        UnlockRules.DevUnlockAll = false;
        GameManager.BlockExpGain = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    void Flush(StringBuilder rows, StringBuilder sum, StringBuilder bossRows)
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "duel.tsv"), rows.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "duel_summary.tsv"), sum.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "duel_boss.tsv"), bossRows.ToString());
    }

    IEnumerator DuelBegin(string ch, string[] deck, int lv, float d, bool withBosses)
    {
        yield return BeginRun(ch, "wasteland_road");
        GameManager.BlockExpGain = true;                                                // レベルアップで他のカードが混ざらない
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);         // 接触の被弾を数える(HP は1体ごとに戻す)
        if (lv > 0)
        {
            foreach (var id in deck.Distinct()) { var c = CardDatabase.FindBaseById(id); if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(lv, GameManager.MaxRunCardLevel)); }
            gm.RecomputeCardStats();
        }
        WarpTo(d); RunLedger.DevClearDebug();
        yield return new WaitForSeconds(1.0f);
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) { hsa.SetEnabled(true); hsa.SetAutoInBoss(true); hsa.SetAutoAttack(true); hsa.SetAutoAvoid(true); }
        var tm = TerrainManager.Instance;
        if (tm != null)
        {
            tm.enemySpawnChance = 0f;
            float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
            tm.SetResumeFlatZone(lx - 5f, lx + 30000f);                                 // 平らな地面(穴/坂なし)
        }
        var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
        foreach (var ob in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(ob.gameObject);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = withBosses;
        stopKeepAlive = true;
        yield return new WaitForSeconds(1.5f);
    }

    IEnumerator DuelEnd()
    {
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        GameManager.BlockExpGain = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
        if (BossManager.Instance != null) BossManager.Instance.enabled = true;
        yield return EndRun();
    }

    void DuelRefillHp() => typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });

    class DuelResult
    {
        public int maxHp, buildAtk, swings, contactHits, hurtBreaks, assistJumps, pushedOut, maxStage;
        public bool killed, passed; public float secs, inReach, attackInReach, approach;
        public List<DuelHit> hits = new List<DuelHit>();
        public Dictionary<int, int> stageHist = new Dictionary<int, int>();
        public float AvgDmg { get { var l = hits.Count > 1 && killed ? hits.Take(hits.Count - 1).ToList() : hits; return l.Count > 0 ? (float)l.Average(h => h.dmg) : 0f; } }
        public int EstHits => AvgDmg > 0f ? Mathf.CeilToInt(maxHp / AvgDmg) : -1;
    }

    int DuelStage() => pc.Kit == CharacterKit.Fighter ? pc.FighterComboStage : pc.CurrentAttackStage;

    IEnumerator DuelOne(string enemyId, System.Action<DuelResult> done)
    {
        { float wi = 0f; while ((pc.IsAttacking || pc.IsReacting) && wi < 3f) { yield return null; wi += Time.deltaTime; } }
        DuelRefillHp();
        var tm = TerrainManager.Instance; var def = EnemyDatabase.FindById(enemyId);
        if (tm == null || def == null || DistanceTierManager.Instance == null) { done(null); yield break; }
        float x = pc.transform.position.x + 30f;
        float gy = tm.GetHeightAt(x) ?? pc.transform.position.y;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, gy + (def.movementType == EnemyMovementType.Flying ? 2.6f : 0f)), EnemyAiTier.T0, def.behaviorKind);
        var en = go != null ? go.GetComponent<EnemyController>() : null;
        if (en == null) { done(null); yield break; }
        int hp = DistanceTierManager.Instance.EnemyHpFor(def.hpMultiplier);
        en.maxHp = hp; HaEnsureHp.Invoke(en, null); HaEnemyHp.SetValue(en, hp);
        var r = new DuelResult { maxHp = hp, buildAtk = pc.EffectiveAttackPower };
        int swing0 = PlayerAttackInfo.SwingCount;
        int lastHp = hp;
        bool wasReacting = pc.IsReacting;
        string lastAction = HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.LastAction : "";
        float lastActionT = -1f;
        System.Action<string, int, int, int> tap = (reason, dmg, before, after) => { if (reason != null && reason.Contains("Enemy")) r.contactHits++; };
        GameManager.QaDamageEvent += tap;
        float t = 0f;
        var pending = new List<DuelHit>();
        while (t < 10f)
        {
            bool dead = en == null || !en.gameObject.activeInHierarchy || en.IsDying || en.IsDyingOrReplica;
            int nowHp = en != null ? HaHp(en) : 0;
            if (en != null && nowHp < lastHp)
            {
                int st = DuelStage();
                float dx0 = (en.transform.position.x - pc.transform.position.x) * HaFacing();
                var h = new DuelHit { t = t, dmg = lastHp - Mathf.Max(0, nowHp), stage = st, dxAt = dx0 };
                r.hits.Add(h); pending.Add(h);
                r.stageHist[st] = (r.stageHist.TryGetValue(st, out int c) ? c : 0) + 1; r.maxStage = Mathf.Max(r.maxStage, st);
                lastHp = nowHp;
            }
            if (dead) { r.killed = true; break; }
            float dx = (en.transform.position.x - pc.transform.position.x) * HaFacing();
            // ノックバックの押し出し: 命中から 0.3秒後に届く距離の外(前)にいるか
            float reach = pc.AssistForwardReach;
            foreach (var ph in pending.ToList())
                if (t - ph.t >= 0.3f) { ph.dxAfter = dx; if (dx > reach + 0.3f && ph.dxAt <= reach + 0.3f) r.pushedOut++; pending.Remove(ph); }
            if (dx < -6f) { r.passed = true; break; }
            // 届く距離(前方 0〜reach、高さが重なる)にいた時間と、その間に攻撃していた時間
            var ec = HaBodyOf(en); var pcol = HaBody();
            bool vert = ec != null && pcol != null && ec.bounds.max.y > pcol.bounds.min.y - 0.3f && ec.bounds.min.y < pcol.bounds.max.y + 1.0f;
            float near = ec != null ? ((ec.bounds.min.x - pc.transform.position.x) * HaFacing()) : dx;
            bool inReach = vert && near <= reach && dx > -1.0f;
            if (inReach) { r.inReach += Time.deltaTime; if (pc.IsAttacking) r.attackInReach += Time.deltaTime; }
            else if (dx > reach) r.approach += Time.deltaTime;
            // 攻撃の中断: 被弾のよろけの始まり / 補助のジャンプ(届く距離にいる間)
            if (pc.IsReacting && !wasReacting) r.hurtBreaks++;
            wasReacting = pc.IsReacting;
            var hsa = HighSpeedAssist.Instance;
            if (hsa != null && hsa.LastAction != lastAction) { lastAction = hsa.LastAction; if (inReach && lastAction.Contains("ジャンプ") && t - lastActionT > 0.2f) { r.assistJumps++; lastActionT = t; } }
            yield return null; t += Time.deltaTime;
        }
        GameManager.QaDamageEvent -= tap;
        r.secs = t; r.swings = PlayerAttackInfo.SwingCount - swing0;
        if (en != null && !r.killed) Destroy(en.gameObject);
        DuelRefillHp();
        yield return new WaitForSeconds(0.4f);
        done(r);
    }

    IEnumerator DuelBoss(string ch, string deckName, float d, string bk, float secs, StringBuilder rowsOut)
    {
        var bm = BossManager.Instance;
        string[] p = bk.Split(':'); string fam = p[0], kind = p[1];
        int top = BossRematchTuning.I != null ? BossRematchTuning.I.tiers.Count - 1 : -1; // 終盤の再戦(最終強化再戦)の段階で出す
        DuelRefillHp();
        int hp0 = gm.Lives, hits0 = 0;
        System.Action<string, int, int, int> tap = (reason, dmg, before, after) => { hits0++; };
        GameManager.QaDamageEvent += tap;
        if (fam == "Cave" && System.Enum.TryParse(kind, out CaveBossKind ck)) bm.DebugCaveEncounter(ck, top);
        else if (fam == "Sky" && System.Enum.TryParse(kind, out SkyBossKind sk)) bm.DebugSkyEncounter(sk, top);
        else if (fam == "Wild" && System.Enum.TryParse(kind, out WildBossKind wk)) bm.DebugWildEncounter(wk, top);
        else { L($"[duel] boss {bk}: unknown"); GameManager.QaDamageEvent -= tap; yield break; }
        float w = 0f; MonoBehaviour boss = null;
        while (w < 10f && boss == null) { boss = BossBattle.Living.FirstOrDefault(b => b != null && b.isActiveAndEnabled && b is IBossBattleDebug); yield return null; w += Time.deltaTime; }
        if (boss == null) { L($"[duel] boss {bk}: did not spawn"); GameManager.QaDamageEvent -= tap; yield break; }
        float maxHp = BlNum(boss, "maxHp"); if (maxHp <= 0f) maxHp = BlNum(boss, "MaxHp");
        int swing0 = PlayerAttackInfo.SwingCount, drops = 0; float lastFrac = 1f, t = 0f, inReach = 0f, atkIn = 0f, above = 0f, behind = 0f; int hpLost = 0;
        while (t < secs)
        {
            var dbg = boss as IBossBattleDebug;
            if (boss == null || dbg == null || !dbg.DebugAlive) break;
            float hp = BlNum(boss, "Hp"); float frac = maxHp > 0f ? hp / maxHp : 1f;
            if (frac < lastFrac - 0.0005f) drops++;
            lastFrac = frac;
            Bounds b = boss is WildBossBase wb ? wb.AssistBounds : (boss.GetComponent<Collider2D>() != null ? boss.GetComponent<Collider2D>().bounds : new Bounds(boss.transform.position, Vector3.one));
            float px = pc.transform.position.x, fx = HaFacing();
            float near = (b.min.x - px) * fx;
            var pcol = HaBody();
            bool vert = pcol != null && b.max.y > pcol.bounds.min.y - 0.3f && b.min.y < pcol.bounds.max.y + 1.0f;
            if (pcol != null && b.min.y >= pcol.bounds.max.y + 1.0f) above += Time.deltaTime;
            if ((b.center.x - px) * fx < -1.5f) behind += Time.deltaTime;
            if (vert && near <= pc.AssistForwardReach && (b.max.x - px) * fx > -0.5f) { inReach += Time.deltaTime; if (pc.IsAttacking) atkIn += Time.deltaTime; }
            if (gm.Lives < gm.maxLives / 3) { hpLost += gm.maxLives - gm.Lives; DuelRefillHp(); } // 倒れないよう戻す(失った量は数える)
            yield return null; t += Time.deltaTime;
        }
        hpLost += gm.maxLives - gm.Lives;
        GameManager.QaDamageEvent -= tap;
        bool killed = boss == null || !(boss as IBossBattleDebug).DebugAlive;
        string reason = killed ? "撃破" : drops == 0 ? "当てられない(HPが減らない)" : (behind > t * 0.4f ? "後ろにいる時間が長い" : above > t * 0.4f ? "頭上にいる時間が長い" : inReach < t * 0.25f ? "届く距離にいる時間が短い" : atkIn < inReach * 0.5f ? "届く距離で攻撃していない" : "火力不足(当たっているが削り切れない)");
        rowsOut.Append($"{ch}\t{deckName}\t{d}\t{bk}\t{maxHp:F0}\t{(killed ? "killed" : "alive")}\t{t:F1}\t{(killed ? 0f : lastFrac):F2}\t{drops}\t{PlayerAttackInfo.SwingCount - swing0}\t{inReach:F1}\t{atkIn:F1}\t{above:F1}\t{behind:F1}\t{hits0}\t{hpLost}\t{reason}\n");
        L($"[duel] {ch} {deckName} {d:F0} boss {bk}: {(killed ? "killed" : "alive")} in {t:F0}s hpLeft {(killed ? 0f : lastFrac):F2} drops {drops} reach {inReach:F0}s attack {atkIn:F0}s above {above:F0}s behind {behind:F0}s hits {hits0} -> {reason}");
        if (!killed) foreach (var lb in BossBattle.Living.ToList()) if (lb is IBossBattleDebug) Destroy(lb.gameObject);
        yield return new WaitForSeconds(1f);
    }
}
#endif

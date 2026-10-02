using System.Collections.Generic;
using UnityEngine;

// マルチ Phase 3.1(2026-10-02)の自動テスト: World Streaming(WorldFront/Back)とボスの置き去り防止。
//  -netAutoLead host|join|swap:S  … 指定した役(HOST/JOIN、swapはS秒ごとに交代)が leadGap m 前を走るよう、走る速さだけを調整する
//                                  (PlayerController.DebugRunOnlyScale。敵/障害物の間隔は変えない)
//  -netAutoLeadGap N               … 先頭と後ろの差(既定120m)
// ログ:
//  WORLD  t= … HOST: 最前/最後尾/出現の基準の人/出現の余裕(最前の人から何m先に出したか)/二重生成の防止回数
//  AHEAD  t= … 各端末: 自分の前200m以内の敵の数、初めて見えた敵が自分から何m先に現れたか(画面内に湧いたら onScreenPops)
//  BOSSPOS t= … HOST: ボスと各プレイヤーの差、狙い、置き去り防止の状態。離れた人が戻ったら BOSS REJOIN
public partial class NetAutoTest
{
    string leadArg = "";
    bool choiceForceOpen;
    float leadGap = 120f;
    float leadLogTimer, worldLogTimer, aheadTimer, bossPosTimer;

    bool ParseWorldArg(string a, string next)
    {
        if (a == "-netAutoLead") { leadArg = next; return true; }
        if (a == "-netAutoLeadGap") { float.TryParse(next, out leadGap); return true; }
        if (a == "-netAutoChoiceForceOpen") { choiceForceOpen = true; return true; } // ボス戦中でも選択を開く(HOSTはボス戦中のレベルアップを後回しにするため)
        if (a == "-netAutoLegacyAnchor") { EncounterDirector.DebugLegacyHostAnchor = true; return true; }
        if (a == "-netAutoNoLeash") { BossLeash.DebugDisabled = true; return true; }
        return false;
    }

    string LeaderRole()
    {
        if (leadArg == "host") return "HOST";
        if (leadArg == "join") return "JOIN";
        if (leadArg.StartsWith("swap:") && float.TryParse(leadArg.Substring(5), out float s) && s > 1f)
            return ((int)(runTime / s)) % 2 == 0 ? "HOST" : "JOIN";
        return "";
    }

    void WorldTick(GameManager gm)
    {
        LeadControl(gm);
        WorldLog(gm);
        AheadMonitor(gm);
        BossPosMonitor(gm);
    }

    void LeadControl(GameManager gm)
    {
        if (leadArg == "" || gm == null) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        float otherX = float.MinValue;
        foreach (NetPlayer p in NetPlayer.All)
            if (p != null && !p.IsOwner && p.Avatar != null && p.Avatar.HasRecentPosition && NetMatch.IsPlayerActive(p.PlayerNumber)) otherX = Mathf.Max(otherX, p.Avatar.transform.position.x);
        bool meAlive = NetMatch.IsLocalAlive;
        if (otherX == float.MinValue || !meAlive) { PlayerController.DebugRunOnlyScale = 1f; return; } // 相手がDOWN/脱落: 普通に走る(救出に向かう)
        string leader = LeaderRole();
        float gap = pc.transform.position.x - otherX;
        float desired = leader == role ? leadGap : -leadGap;
        float err = desired - gap;
        float scale = Mathf.Clamp(1f + 0.9f * err / Mathf.Max(10f, leadGap), 0.5f, 1.7f);
        if (leader != role && err > 0f) scale = Mathf.Max(scale, 1f); // 後ろ役は「遅くする」だけ(追い抜く時は先頭役が引き離す)
        PlayerController.DebugRunOnlyScale = scale;
        leadLogTimer += Time.unscaledDeltaTime;
        if (leadLogTimer >= 2f) { leadLogTimer = 0f; L($"LEAD t={runTime:F0} role={role} leader={leader} gap={gap:F0} scale={scale:F2}"); }
    }

    void WorldLog(GameManager gm)
    {
        worldLogTimer += Time.unscaledDeltaTime;
        if (worldLogTimer < 1f || gm == null) return;
        worldLogTimer = 0f;
        var ed = EncounterDirector.Instance;
        var bm = BossManager.Instance;
        string enc = ed == null ? "-" : $"refP=P{ed.RefPlayer} encounters={ed.Recent.Count} minLead={(ed.MinSpawnLead == float.MaxValue ? 0f : ed.MinSpawnLead):F0} lastLead={ed.LastSpawnLead:F0} dupBlocked={ed.DuplicateAnchorsBlocked} byFront=[{string.Join(",", ed.SpawnsByFront)}]";
        // DOWNの人の周り(±40m)に残っている敵(救出に向かう間に不自然に消えていないか)
        string downNear = "";
        var nc = NetCombat.Instance;
        foreach (var q in WorldRange.GetActivePlayers())
        {
            if (!q.Down || nc == null) continue;
            int n = 0;
            foreach (var e in nc.Entities.Values) if (!e.Dead && e.Go != null && e.Go.activeInHierarchy && Mathf.Abs(e.Go.transform.position.x - q.SceneX) < 40f) n++;
            downNear += $" nearDownP{q.Pn}={n}";
        }
        L($"WORLD t={runTime:F0} role={role} {WorldRange.Describe()}{downNear} | {enc} | nextBoss={(bm != null ? bm.NextBossDistance : 0f):F0} bossPhase={(bm != null && bm.IsBossPhase)} | leash frames={BossLeash.LeashFrames} retarget={BossLeash.RetargetRequests} snaps={BossLeash.Snaps} waits={EnemyTargetSelector.WaitCount}");
    }

    // ---- 自分の前の敵(出現が間に合っているか)
    readonly HashSet<int> seenEnemies = new HashSet<int>();
    int aheadSamples, aheadEmptySamples, onScreenPops, newEnemiesSeen;
    float minAppearAhead = float.MaxValue, emptyStreak, maxEmptyStreak;
    void AheadMonitor(GameManager gm)
    {
        PlayerController pc = PlayerController.Instance;
        var nc = NetCombat.Instance;
        if (pc == null || nc == null || gm == null) return;
        float px = pc.transform.position.x;
        Camera cam = Camera.main;
        float screenRight = cam != null ? cam.transform.position.x + cam.orthographicSize * cam.aspect : px + 16f;
        int ahead = 0;
        foreach (var kv in nc.Entities)
        {
            var e = kv.Value;
            if (e.Kind != NetCombat.Kind.Enemy || e.Dead || e.Go == null || !e.Go.activeInHierarchy) continue;
            float x = e.Go.transform.position.x;
            if (x > px && x < px + 200f) ahead++;
            if (seenEnemies.Add(kv.Key))
            {
                newEnemiesSeen++;
                float dx = x - px;
                if (dx > 0f) minAppearAhead = Mathf.Min(minAppearAhead, dx);
                if (dx > 0f && x < screenRight - 1f && runTime > 3f) { onScreenPops++; if (onScreenPops <= 20) L($"AHEAD POP t={runTime:F1} role={role} id={kv.Key} appeared {dx:F1}m ahead (on screen, right edge {screenRight - px:F1}m) kmh={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}"); }
            }
        }
        aheadTimer += Time.unscaledDeltaTime;
        if (aheadTimer < 1f) return;
        aheadTimer = 0f;
        var bm = BossManager.Instance;
        bool bossAlive = false;
        foreach (var e in nc.Entities.Values) if (e.Kind == NetCombat.Kind.Boss && !e.Dead) { bossAlive = true; break; }
        bool excused = (bm != null && bm.IsBossPhase) || bossAlive || (BonusZone.Instance != null && BonusZone.SuppressesNormalSpawns) || gm.IsInSafeZone || NetMatch.Get(NetCombat.LocalPlayerNumber)?.State != NetMatch.PState.Alive || runTime < 8f;
        if (!excused)
        {
            aheadSamples++;
            if (ahead == 0) { aheadEmptySamples++; emptyStreak += 1f; maxEmptyStreak = Mathf.Max(maxEmptyStreak, emptyStreak); }
            else emptyStreak = 0f;
        }
        L($"AHEAD t={runTime:F0} role={role} me=P{NetCombat.LocalPlayerNumber} enemiesAhead200={ahead} empty={aheadEmptySamples}/{aheadSamples} maxEmptyStreak={maxEmptyStreak:F0}s newSeen={newEnemiesSeen} minAppearAhead={(minAppearAhead == float.MaxValue ? 0f : minAppearAhead):F1} onScreenPops={onScreenPops} kmh={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0} dist={gm.MaxDistance:F0}{(excused ? " (excused)" : "")}");
    }

    // ---- ボスと各プレイヤーの距離(HOST)
    readonly Dictionary<int, float> sepSince = new Dictionary<int, float>();
    float bossMaxNearestGap; int bossRejoins, bossSamples;
    readonly HashSet<int> bossIdsSeen = new HashSet<int>();

    // 二重生成の確認: 同じ位置(1m単位)に同じFormationが2回以上出ていないか
    int DuplicateEncounters()
    {
        var ed = EncounterDirector.Instance;
        if (ed == null) return 0;
        var seen = new HashSet<string>(); int dup = 0;
        foreach (var r in ed.Recent) if (!seen.Add($"{System.Math.Round(r.anchorLogical)}|{r.formation}")) dup++;
        return dup;
    }
    void BossPosMonitor(GameManager gm)
    {
        if (role != "HOST") return;
        var nc = NetCombat.Instance;
        if (nc == null) return;
        bossPosTimer += Time.unscaledDeltaTime;
        if (bossPosTimer < 0.5f) return;
        bossPosTimer = 0f;
        foreach (var kv in nc.Entities)
        {
            var e = kv.Value;
            if (e.Kind != NetCombat.Kind.Boss || e.Dead || e.Go == null || !e.Go.activeInHierarchy) continue;
            if (bossIdsSeen.Add(kv.Key)) L($"BOSS SEEN t={runTime:F1} id={kv.Key} ({e.TypeKey}) total distinct={bossIdsSeen.Count}");
            float bx = e.Go.transform.position.x;
            var sb = new System.Text.StringBuilder();
            float nearest = float.MaxValue;
            foreach (var q in WorldRange.GetActivePlayers())
            {
                float d = bx - q.SceneX;
                sb.Append($" P{q.Pn}{(q.Down ? "(DOWN)" : q.Choosing ? "(choose)" : "")}={d:F0}");
                if (!q.Alive) continue;
                if (!q.Choosing) nearest = Mathf.Min(nearest, Mathf.Abs(d));
                // 離れた(40m超)→ 戻った(20m以内)を記録
                if (Mathf.Abs(d) > 40f) { if (!sepSince.ContainsKey(q.Pn)) { sepSince[q.Pn] = runTime; L($"BOSS SEPARATED t={runTime:F1} P{q.Pn} gap={d:F0}"); } }
                else if (Mathf.Abs(d) < 20f && sepSince.TryGetValue(q.Pn, out float since))
                {
                    sepSince.Remove(q.Pn); bossRejoins++;
                    L($"BOSS REJOIN t={runTime:F1} P{q.Pn} after {runTime - since:F1}s gap={d:F0}");
                }
            }
            if (nearest < float.MaxValue) { bossSamples++; bossMaxNearestGap = Mathf.Max(bossMaxNearestGap, nearest); }
            var selr = e.Go.GetComponent<EnemyTargetSelector>(); int target = selr != null ? selr.TargetPlayer : 0;
            L($"BOSSPOS t={runTime:F1} id={kv.Key} target=P{target} pref=P{(selr != null ? selr.Preferred : 0)}{sb} nearest={(nearest == float.MaxValue ? -1f : nearest):F0}");
        }
    }

    string WorldSummary()
    {
        var ed = EncounterDirector.Instance;
        return $"WORLDSUM role={role} aheadEmpty={aheadEmptySamples}/{aheadSamples} maxEmptyStreak={maxEmptyStreak:F0}s newSeen={newEnemiesSeen} minAppearAhead={(minAppearAhead == float.MaxValue ? 0f : minAppearAhead):F1} onScreenPops={onScreenPops}"
            + (ed != null && role == "HOST" ? $" encounters={ed.Recent.Count} duplicateEncounters={DuplicateEncounters()} distinctBosses={bossIdsSeen.Count} minSpawnLead={(ed.MinSpawnLead == float.MaxValue ? 0f : ed.MinSpawnLead):F0} dupBlocked={ed.DuplicateAnchorsBlocked} byFront=[{string.Join(",", ed.SpawnsByFront)}]" : "")
            + (role == "HOST" ? $" bossSamples={bossSamples} bossMaxNearestGap={bossMaxNearestGap:F0} bossRejoins={bossRejoins} stillSeparated=[{string.Join(",", sepSince.Keys)}] leashFrames={BossLeash.LeashFrames} retarget={BossLeash.RetargetRequests} snaps={BossLeash.Snaps}" : "");
    }
}

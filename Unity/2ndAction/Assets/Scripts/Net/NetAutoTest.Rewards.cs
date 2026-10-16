using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 協力マルチの MILE の分配の確認(2026-10-15 第2弾)。HOST と JOIN が、それぞれ通常の敵/精鋭/ボスを倒す。
//  -netAutoRwKills "9:enemy,14:elite,20:spawnboss,24:boss"  … 各端末が自分の時刻に行う
//     enemy     … 一番近い通常の敵を、この端末のプレイヤーの攻撃として倒す(HOST はその場で、JOIN は HOST へダメージ要求)
//     elite     … 同じ(HOST の -netAutoEliteAt の間に倒すと精鋭)
//     spawnboss … HOST だけ: オオカミを1体出す(HP は -netAutoBossHp)
//     boss      … 生きているボスを、この端末のプレイヤーの攻撃として倒す
//  -netAutoEliteAt "13" … HOST だけ: その時刻から3秒の間、生きている雑魚を毎フレーム精鋭にする(JOIN がどれを狙っても精鋭)
// 撃破の1秒後に [RWK] 行(この端末の MILE/EXP/端数)。終わりに REWARD 行(経路ごとの正確な値も)
public partial class NetAutoTest
{
    readonly List<(float t, string what)> rwKills = new List<(float, string)>();
    bool rushLog;
    int rwKillIndex;
    float eliteFrom = -1f;
    float rwLogAt = -1f; string rwLogWhat = "";

    void ParseRewardArgs(string a, string next)
    {
        if (a == "-netAutoRwKills")
            foreach (var part in next.Split(','))
            {
                var p = part.Split(':');
                if (p.Length == 2 && float.TryParse(p[0], out float t)) rwKills.Add((t, p[1].Trim()));
            }
        else if (a == "-netAutoEliteAt") float.TryParse(next, out eliteFrom);
        else if (a == "-netAutoRushLog") rushLog = next == "1";
        else if (a == "-netAutoRushDupOffer") rushDupOffer = next == "1"; // 2026-10-16: HOST が完走報酬の知らせをもう一度送る(二重の知らせの確認)
        else if (a == "-netAutoKillWhenSeen") { int.TryParse(next, out killWhenSeen); damageTest = true; } // 2026-10-16: N 体目のボスが見えたら自分を倒す(DOWN のまま完走する確認)
    }

    // 2026-10-15 第4弾: 協力のボスラッシュの状態(3秒ごと)。出ているボス/これまでに見たボスの数(HOST の出現と JOIN のパペットで同じになるか)
    int killWhenSeen; bool rushDupOffer; float rushDupAt = -1f;
    float rushLogAt; readonly HashSet<WildBossBase> rushSeen = new HashSet<WildBossBase>(); int rushChoicesWithBoss; bool rushWasOpen;
    void RushLogTick(float runTime, GameManager gm)
    {
        var bm = BossManager.Instance;
        int alive = 0;
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
            if (b != null && !b.IsDead && !(b is ReaperSisterBoss)) { alive++; if (b.mileReward > 0 || true) rushSeen.Add(b); }
        if (killWhenSeen > 0 && rushSeen.Count >= killWhenSeen && killAt < 0f) { killAt = runTime; L($"RUSH kill self when boss #{killWhenSeen} is seen (t={runTime:F0})"); }
        if (rushDupOffer && role == "HOST" && gm.RushRewardOffersReceived > 0 && rushDupAt < 0f) rushDupAt = runTime + 3f;
        if (rushDupAt > 0f && runTime >= rushDupAt && bm != null)
        {
            rushDupAt = float.MaxValue;
            int n = NetMatch.SendRushRewardOffers(bm.MapRushesStarted);
            int n2 = NetMatch.SendRushRewardOffers(bm.MapRushesStarted);
            L($"RUSH duplicate completion reward offers sent (serial {bm.MapRushesStarted}, {n}+{n2} messages)");
        }
        bool open = gm.LevelUpPending;
        if (open && !rushWasOpen && alive > 0 && bm != null && bm.MapRushActive) rushChoicesWithBoss++;
        rushWasOpen = open;
        if (runTime < rushLogAt) return;
        rushLogAt = runTime + 3f;
        L($"RUSH t={runTime:F0} role={role} active={(bm != null && bm.MapRushActive)} phase={(bm != null && bm.IsBossPhase)} holds={(bm != null && bm.HoldsRun)} alive={alive} seen={rushSeen.Count} d={gm.MaxDistance:F0} bossMile={gm.RunBossMile} bossKills={gm.BossKillCount} Lv={gm.Level} choosing={open} pending={gm.PendingLevelUpCount} choicesWithBoss={rushChoicesWithBoss} rewards={gm.BossRewardChoicesStarted} reaper={(bm != null && bm.DeathSpawned)} nextGate={(bm != null ? bm.NextBossDistance : 0f):F0} exclEndsIgnored={gm.BossExclusionEndsIgnored} rushReward[recv/ign/taken/owed]={gm.RushRewardOffersReceived}/{gm.RushRewardOffersIgnored}/{gm.RushRewardsTaken}/{gm.NetRushRewardOwed} graceRejects={NetMatch.ChoiceExitGraceRejects} lives={gm.Lives} table=[{(NetMatch.Instance != null ? NetMatch.Instance.DebugDescribe() : "")}]");
    }

    void RewardKillTick(float runTime)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (rushLog) RushLogTick(runTime, gm);
        if (role == "HOST" && eliteFrom >= 0f && runTime >= eliteFrom && runTime < eliteFrom + 3f)
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                if (e != null && !e.IsDyingOrReplica && e.bonus == null && !e.IsElite) ChallengeSystem.MakeElite(e, 1f);
        if (rwLogAt > 0f && runTime >= rwLogAt) { rwLogAt = -1f; L($"RWK after {rwLogWhat}: {RewardState(gm)}"); }
        if (rwKillIndex >= rwKills.Count || runTime < rwKills[rwKillIndex].t) return;
        string what = rwKills[rwKillIndex++].what;
        var pc = PlayerController.Instance;
        Vector3 me = pc != null ? pc.transform.position : Vector3.zero;
        int pn = NetCombat.LocalPlayerNumber;
        if (what == "wanted")
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (role == "HOST") { ChallengeSystem.DebugSpawnWanted(1); L($"RWK spawned a WANTED (bounty {20 + 10} MILE)"); }
#endif
            return;
        }
        if (what == "wantedeff")
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 2026-10-15 第4弾: ふだんの出現と同じ Lv の決め方(協力は参加者の一番高い Lv)。HOST が持っていなくても JOIN の Lv で出る
            if (role == "HOST")
            {
                int hostOwn = Mathf.RoundToInt(gm.Card.Get(EffectType.WantedLevel));
                int lv = ChallengeSystem.DebugSpawnWantedEffective();
                L($"RWK spawned a WANTED with the effective Lv{lv} (HOST card Lv{hostOwn}, remote max Lv{NetPlayer.MaxRemoteWantedLevel()}) bounty {(lv > 0 ? 20 + 10 * lv : 0)} MILE");
            }
#endif
            return;
        }
        if (what == "killall")
        {
            int n = 0;
            foreach (var x in FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(x => x != null && x.isActiveAndEnabled && x.NetId != 0 && !x.IsDying && x.bonus == null && x.transform.position.x > me.x - 3f && x.transform.position.x < me.x + 45f).ToList())
            {
                if (role == "HOST") x.NetApplyRemoteHit(pn, 9999999, PlayerAttackKind.Normal, x.transform.position);
                else NetCombat.RequestHit(x.NetId, 9999999, PlayerAttackKind.Normal, x.transform.position);
                n++;
            }
            L($"RWK P{pn} kills all {n} enemies ahead (WANTED included)");
            rwLogAt = runTime + 1.5f; rwLogWhat = what;
            return;
        }
        if (what == "spawnboss")
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (role == "HOST" && BossManager.Instance != null && !BossManager.Instance.IsBossPhase) { BossManager.NetTestBossHpOverride = bossHp; BossManager.Instance.NetTestSpawnWild(WildBossKind.Wolf, 1); L("RWK spawned a wolf"); }
#endif
            return;
        }
        if (what == "boss")
        {
            var b = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(x => x != null && !x.IsDead && x.NetId != 0);
            if (b == null) { L("RWK no boss to kill"); return; }
            L($"RWK P{pn} kills boss {b.bossName} id={b.NetId} mileReward={b.mileReward}");
            if (role == "HOST") b.NetApplyRemoteHit(pn, 9999999, b.CenterWorld);
            else NetCombat.RequestHit(b.NetId, 9999999, PlayerAttackKind.Normal, b.CenterWorld);
        }
        else
        {
            var e = FindObjectsByType<EnemyController>(FindObjectsSortMode.None)
                .Where(x => x != null && x.isActiveAndEnabled && x.NetId != 0 && !(x.IsDying) && x.bonus == null && x.transform.position.x > me.x - 3f)
                .OrderBy(x => Mathf.Abs(x.transform.position.x - me.x)).FirstOrDefault();
            if (e == null) { L($"RWK no enemy for {what}"); return; }
            L($"RWK P{pn} kills {what} id={e.NetId} (host view: elite={e.IsElite} base={e.MileRewardBase:F2} expMul={e.KillExpMultiplier:F1})");
            if (role == "HOST") e.NetApplyRemoteHit(pn, 9999999, PlayerAttackKind.Normal, e.transform.position);
            else NetCombat.RequestHit(e.NetId, 9999999, PlayerAttackKind.Normal, e.transform.position);
        }
        rwLogAt = runTime + 1.2f; rwLogWhat = what;
    }

    static string RewardState(GameManager g) =>
        $"share=1/{g.MileShareCount} enemyMile={g.RunEnemyMile} bossMile={g.RunBossMile} bonusMile={g.RunBonusMile} wanted={ChallengeSystem.WantedKilled} kills={g.EnemyKillCount} bossKills={g.BossKillCount} Lv={g.Level} exp={g.TotalExpEarned:F0} carry={g.MileCarryDebug:F3}"
        + $" base[e/b]={g.MileBaseDbg[1]:F3}/{g.MileBaseDbg[2]:F3} exact[e/b]={g.MileExactDbg[1]:F3}/{g.MileExactDbg[2]:F3} takeHome={g.TakeHomeMile} exactTotal={g.TakeHomeExactDebug:F3}";
}

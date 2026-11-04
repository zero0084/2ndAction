#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 道中イベント / 空中ボスの乱入の確認(2026-11-04)
//  -qaRaid <dir> [-rdStages wasteland_road,natural_cave,sky_corridor] [-rdKm 35] [-rdReps 2] [-rdBlink 0.6] [-rdBuild recommended] [-rdSpeed 100] [-rdVid 0]
//    ステージの乱入候補ごとに: 新しいランで -rdKm へワープ → 乱入を始める → 終わるまで(撃破/飛び去る)をオートモード(+うっかり)で戦う。
//    結果は raid.tsv(1行 = 1回: 候補・秒・結果・被弾・死亡・HP 最低・走った距離)。-rdVid 1 で各候補の1回目を 15fps の連番(rd_*)で撮る
//  -qaRoadVid <dir> [-rvStage wasteland_road] [-rvKm 31] [-rvSecs 100] [-rvForce Flying@15,AirRaid@60] [-rvFps 15]
//    走っている画面を連番(rv_*)で撮る(動画用・無敵)。道中イベントは本物のまま。-rvForce で秒を指定して起こす
public partial class QaSweep
{
    void SetupBuild(string build, float km)
    {
        int picks = Mathf.RoundToInt(4 + km / 4f);
        var ids = DfBuilds.TryGetValue(build, out var b) ? b : DfBuilds["recommended"];
        var lv = new Dictionary<string, int>();
        for (int p = 0; p < picks; p++) { string id = ids[p % ids.Length]; lv[id] = Mathf.Min(9, (lv.TryGetValue(id, out int v) ? v : 0) + 1); }
        foreach (var kv in lv) { var c = CardDatabase.FindBaseById(kv.Key); if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(kv.Value, GameManager.MaxRunCardLevel)); }
    }

    void AssistOn()
    {
        var a = HighSpeedAssist.Instance;
        if (a != null) { a.SetEnabled(true); a.SetAutoAttack(true); a.SetAutoAvoid(true); a.SetAutoInBoss(true); a.engageKmh = 0f; a.releaseKmh = -1f; a.fullAssistKmh = 1f; }
    }

    IEnumerator RaidMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] stages = Arg("-rdStages", "wasteland_road,natural_cave,sky_corridor").Split(',');
        float km = float.Parse(Arg("-rdKm", "35"), inv);
        int reps = int.Parse(Arg("-rdReps", "2"));
        string build = Arg("-rdBuild", "recommended"), ch = Arg("-rdChar", "swordsman");
        bool vid = Arg("-rdVid", "0") == "1";
        HighSpeedAssist.QaBlinkPerSec = float.Parse(Arg("-rdBlink", "0.6"), inv);
        GameSettings.SetMaxSpeedPercent(int.Parse(Arg("-rdSpeed", "100")));
        RoadEventTuning.DisabledByArg = true; // 自然の道中イベントは止める(乱入だけを測る)
        float limit = float.Parse(Arg("-rdLimit", "-1"), inv); // 飛び去る演出の確認用(制限時間を短く)
        if (limit > 0f) RoadEventTuning.I.raidTimeLimit = limit;
        GameManager.QaDamageEvent = OnDfDamage;
        var tsv = new System.Text.StringBuilder("stage\tkey\trep\tkm\tspeed\tblink\tsecs\tresult\thp\thits\tdamage\tdeaths\thp_min_pct\tkm_run\tassist_jumps\tbreaks\n");
        foreach (var st in stages)
        {
            var keys = RoadEventTuning.I.RaidKeys(st);
            if (keys == null) continue;
            foreach (var key in keys)
                for (int r = 0; r < reps; r++)
                {
                    yield return BeginRun(ch, st);
                    stopKeepAlive = true; yield return null;
                    typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
                    AssistOn();
                    SetupBuild(build, km);
                    WarpTo(km * 1000f); yield return new WaitForSeconds(1.2f);
                    // 関門が近い時は少し先へ
                    if (Bm.NextBossDistance - gm.MaxDistance < 2500f) { WarpTo(Bm.NextBossDistance + 800f); yield return new WaitForSeconds(1.2f); }
                    while (Bm.IsBossPhase) yield return null;
                    float bw = 0f; // ワープで BONUS ZONE が始まった時は終わるまで待つ(BONUS の間は乱入しない)
                    while (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle && bw < 90f) { yield return null; bw += Time.deltaTime; }
                    typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
                    dfHits = dfDeaths = dfDamage = 0; dfByKind.Clear();
                    int ended0 = BossManager.RaidLog.Count;
                    float d0 = gm.MaxDistance, hpMin = 1f;
                    bool ok = Bm.StartAirRaid(key);
                    Check(ok, $"raid {st} {key}: started");
                    if (!ok) { yield return EndRun(); continue; }
                    bool capture = vid && r == 0;
                    if (capture) Time.captureDeltaTime = 1f / 30f;
                    int f = 0; float t = 0f, after = -1f; int hp = 0, breaks = 0;
                    while (t < 120f)
                    {
                        yield return null; t += Time.deltaTime;
                        hpMin = Mathf.Min(hpMin, gm.Lives / (float)Mathf.Max(1, gm.MaxLives));
                        var b = Bm.AirRaidBoss;
                        if (b != null) { hp = Mathf.Max(hp, b.maxHp); if (b is IBossBattleDebug bd) breaks = Mathf.Max(breaks, bd.BreakCount); }
                        if (capture && (f++ % 2) == 0) Shot($"rd_{st}_{key.Replace('/', '_')}_{f:00000}");
                        if (after < 0f && BossManager.RaidLog.Count > ended0) after = t;
                        if (after >= 0f && t - after > (capture ? 4f : 0.5f)) break;
                    }
                    Time.captureDeltaTime = 0f;
                    string res = BossManager.RaidLog.Count > ended0 ? BossManager.RaidLog[BossManager.RaidLog.Count - 1] : $"{key}\t-1\tnot ended\t-";
                    var parts = res.Split('\t');
                    string row = $"{st}\t{key}\t{r}\t{km:F1}\t{GameSettings.MaxSpeedPercent}\t{HighSpeedAssist.QaBlinkPerSec:F1}\t{parts[1]}\t{parts[2]}\t{hp}\t{dfHits}\t{dfDamage}\t{dfDeaths}\t{hpMin * 100f:F0}\t{(gm.MaxDistance - d0) / 1000f:F2}\t{(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.AutoJumps : 0)}\t{breaks}";
                    tsv.Append(row).Append('\n');
                    L($"[raid] {row}");
                    Check(parts[2] == "defeated" || parts[2].StartsWith("fled"), $"raid {st} {key} #{r}: ended ({parts[2]})");
                    Check(!Bm.IsBossPhase || !Bm.AirRaidActive, $"raid {st} {key} #{r}: boss phase cleared after the raid");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "raid.tsv"), tsv.ToString());
                    yield return EndRun();
                }
        }
        GameManager.QaDamageEvent = null; HighSpeedAssist.QaBlinkPerSec = 0f; RoadEventTuning.DisabledByArg = false;
        Check(true, "raid: done");
    }

    IEnumerator RoadVidMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string st = Arg("-rvStage", "wasteland_road"), ch = Arg("-rvChar", "swordsman");
        float km = float.Parse(Arg("-rvKm", "31"), inv), secs = float.Parse(Arg("-rvSecs", "100"), inv);
        int every = Mathf.Max(1, 30 / Mathf.Max(1, int.Parse(Arg("-rvFps", "15"))));
        var force = Arg("-rvForce", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => { var p = s.Split('@'); return (kind: (RoadEventKind)System.Enum.Parse(typeof(RoadEventKind), p[0]), at: float.Parse(p[1], inv)); }).ToList();
        GameSettings.SetMaxSpeedPercent(int.Parse(Arg("-rvSpeed", "100")));
        bool noShot = Arg("-rvNoShot", "0") == "1"; // 撮らずに数えるだけ(比較用)
        WildBossBase.HaltRouteLiftEnabled = Arg("-haltRouteLift", "1") != "0"; // 2026-11-04: 0 = 前の作り(止まったボス戦でも上ルートへ寄せない)
        yield return BeginRun(ch, st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        AssistOn();
        SetupBuild(Arg("-rvBuild", "recommended"), km);
        GameManager.BlockExpGain = true; // 撮影中にカードの選択画面を出さない
        if (km > 0f) { WarpTo(km * 1000f); yield return new WaitForSeconds(1.5f); }
        if (!noShot) Time.captureDeltaTime = 1f / 30f;
        int f = 0, ev0 = RoadEvents.Started; float t = 0f, logT = 0f, onSum = 0f; int samples = 0, idle = 0;
        var cam = Camera.main;
        while (t < secs)
        {
            yield return null; t += noShot ? Time.deltaTime : 1f / 30f;
            for (int i = force.Count - 1; i >= 0; i--) if (t >= force[i].at) { L($"[rv] force {force[i].kind} at {gm.MaxDistance:F0}m: {RoadEvents.DebugStart(force[i].kind)}"); force.RemoveAt(i); }
            if ((f++ % every) == 0 && !noShot) Shot($"rv_{st}_{km:000}_{f:00000}");
            if ((logT += noShot ? Time.deltaTime : 1f / 30f) >= 1f)
            {
                logT = 0f; samples++;
                int on = 0; cam = Camera.main;
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                {
                    if (e == null || !e.isActiveAndEnabled || cam == null) continue;
                    var vp = cam.WorldToViewportPoint(e.transform.position);
                    if (vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f) on++;
                }
                bool boss = Bm != null && Bm.IsBossPhase;
                onSum += on; if (on == 0 && !boss && RoadEvents.Active == RoadEventKind.None) idle++;
                L($"[rv] t={t:F0}s d={gm.MaxDistance:F0} on={on} boss={boss}{(Bm != null && Bm.AirRaidActive ? " RAID " + BossManager.LastRaidKey : "")} event={RoadEvents.Describe()} journey={LandmarkJourney.Describe()}");
                if (boss && Arg("-rvBossLog", "1") == "1") RvBossState(); // 2026-11-04: 関門が終わらない時の切り分け(ボスごとの状態 + 補助の判断)
            }
        }
        Time.captureDeltaTime = 0f;
        GameManager.BlockExpGain = false;
        L($"[rv] {st} {km}km {secs:F0}s: events {RoadEvents.Started - ev0}, avg on-screen {onSum / Mathf.Max(1, samples):F2}, quiet {100f * idle / Mathf.Max(1, samples):F0}% | {string.Join(" / ", RoadEvents.History)}");
        Check(true, "road video: done");
        yield return EndRun();
    }

    void RvBossState()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        var has = HighSpeedAssist.Instance;
        L($"[rvb] player x={pc.transform.position.x:F1} halt={pc.HaltFactor:F2} y={pc.transform.position.y:F1} attacking={pc.IsAttacking} canAtk={pc.AssistCanStartForwardAttack} react={pc.IsReacting} reach={pc.AssistForwardReach:F1} grounded={pc.IsGrounded} status={(has != null ? has.CurrentStatus.ToString() : "-")} bossAtk={(has != null ? has.BossAttacks : 0)} ultActive={BossBattle.UltimateActive} haltLift={WildBossBase.HaltRouteLiftFrames} plan='{(has != null ? has.BossPlan : "-")}' act='{(has != null ? has.LastAction : "-")}'");
        foreach (var lb in BossBattle.Living)
        {
            if (!(lb is WildBossBase b) || b == null) continue;
            var bb = b.AssistBounds;
            L($"[rvb]   {b.bossName}#{b.slotIndex} hp={b.Hp}/{b.maxHp} ph={b.Phase} act={b.DebugAct} pose={b.DebugPose} gap={b.DebugGap:F1} front={b.DebugFrontDist:F1} relV={b.DebugRelVelocity:F1} freeGap={b.DebugFreeGap} inv={b.DebugInvulnerable} tgt={b.AssistTargetable} ult={b.UltimateRunning} brk={b.Broken} sp={b.SpecialsUsed}/{b.SpecialsDeferred} ults={b.UltimatesUsed} lift={b.DebugBreakRouteLift:F1} bx={bb.min.x - pc.transform.position.x:F1}..{bb.max.x - pc.transform.position.x:F1} by={bb.min.y - pc.transform.position.y:F1}..{bb.max.y - pc.transform.position.y:F1}");
        }
    }
}
#endif

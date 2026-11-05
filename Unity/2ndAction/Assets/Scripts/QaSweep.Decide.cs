#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 判断型の道中イベント(2026-11-05)の検証: -qaDecide <dir>
//   [-deStage wasteland_road] [-deChar swordsman] [-deBuild recommended] [-deStartKm 50.3] [-deEndKm 90] [-deMode idle|human]
//   [-deSecs 2400(上限)] [-deSpeed 100(設定「最高速度」%)] [-deRuns 1] [-deShots 1(イベントの始まりと危険の瞬間を撮る)]
//   [-deVid 1(イベントごとに 3 秒、15fps で連番を撮る = エステルに見せるシート用)] [-deForce Kind@秒,...(その時刻に起こす。撮影用)]
//  オートは本物の設定のまま(自動攻撃/自動回避/ボス戦 ON。発動の速さも既定値)。HP は満タンへ戻さない(倒れたら終わり = 放置での到達距離)。
//  idle  = オートのまま何も操作しない(完全放置)
//  human = オート + 「画面を見ている人」の操作: 地上の帯/地上封鎖 → 跳ぶ(必要なら二段)/ 空の帯/上空封鎖 → 地上にいる(オートの跳躍を止め、空中なら急降下)/
//          鎧の敵 → 近づいたら打ち上げ → 空中で前攻撃
//  結果: decide.tsv(1行 = 1回の走り)・events.tsv(1行 = 1回のイベントとその間の被弾)
public partial class QaSweep
{
    class DeEvent { public float km; public string kind = "", detail = ""; public int hearts, hits; public float hpBefore; }
    readonly List<DeEvent> deEvents = new List<DeEvent>();
    DeEvent deCur; int deHits, deHearts; readonly Dictionary<string, int> deByKind = new Dictionary<string, int>();
    float deLastEventStart = -99f;

    IEnumerator DecideMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stage = Arg("-deStage", "wasteland_road"), ch = Arg("-deChar", "swordsman"), build = Arg("-deBuild", "recommended"), modeArg = Arg("-deMode", "idle");
        float startKm = float.Parse(Arg("-deStartKm", "50.3"), inv), endKm = float.Parse(Arg("-deEndKm", "90"), inv), secs = float.Parse(Arg("-deSecs", "2400"), inv);
        int runs = int.Parse(Arg("-deRuns", "1"));
        bool shots = Arg("-deShots", "0") == "1", vid = Arg("-deVid", "0") == "1";
        deIsolate = Arg("-deIsolate", "0") == "1";
        float periodic = float.Parse(Arg("-dePeriodic", "0"), inv), nextPeriodic = 0f; // 開発用: この秒数ごとに撮る
        GameSettings.SetMaxSpeedPercent(int.Parse(Arg("-deSpeed", "100")));
        float speedScale = float.Parse(Arg("-deSpeedScale", "1"), inv); // 高速の確認(9 = 約900km/h。SpeedRatio に入るのでカメラ/間隔も高速の扱い)
        var forced = Arg("-deForce", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => { var p = s.Split('@'); return (kind: (RoadEventKind)System.Enum.Parse(typeof(RoadEventKind), p[0]), t: float.Parse(p.Length > 1 ? p[1] : "5", inv)); }).ToList();
        var tsv = new System.Text.StringBuilder("mode\tchar\tstage\tspeed\tbuild\tstart_km\tend_km\tdied\tdeath_km\tsecs\tmaxHp\tlv\thits\thearts\tdecision_events\tlegacy_events\tby_kind\tdamage_sources\tsweep_hits\tbrute_flings\tbrute_ground\theavy_hits\tflattened\tregion_rejects\n");
        var ev = new System.Text.StringBuilder("mode\tchar\tstage\tkm\tkind\tdetail\thits\thearts\thp_before\n");
        GameManager.QaDamageEvent = OnDeDamage;
        DecisionEvents.OnStarted += OnDeStarted;
        DecisionEvents.OnDetail += OnDeDetail;
        for (int run = 0; run < runs; run++)
        {
            yield return BeginRun(ch, stage);
            stopKeepAlive = true; yield return null;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            var a = HighSpeedAssist.Instance;
            if (a != null) { a.SetEnabled(true); a.SetAutoAttack(true); a.SetAutoAvoid(true); a.SetAutoInBoss(true); }
            int picks = Mathf.RoundToInt(4 + startKm / 4f);
            var ids = DfBuilds.TryGetValue(build, out var bl) ? bl : DfBuilds["recommended"];
            var lvs = new Dictionary<string, int>();
            for (int p = 0; p < picks; p++) { string id = ids[p % ids.Length]; lvs[id] = Mathf.Min(9, (lvs.TryGetValue(id, out int v) ? v : 0) + 1); }
            foreach (var kv in lvs) { var c = CardDatabase.FindBaseById(kv.Key); if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(kv.Value, GameManager.MaxRunCardLevel)); }
            WarpTo(startKm * 1000f); yield return new WaitForSeconds(1f);
            PlayerController.DebugSpeedScale = speedScale;
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
            deEvents.Clear(); deCur = null; deHits = deHearts = 0; deRefunded = 0; deByKind.Clear();
            int st0 = DecisionEvents.Started, legacy0 = RoadEvents.Started, sweep0 = DecisionEvents.SweepHits, fl0 = DecisionEnemy.BruteFlings, bg0 = DecisionEnemy.BruteGroundHits, hv0 = DecisionEnemy.HeavyHitsDealt;
            var byKind0 = new Dictionary<RoadEventKind, int>(DecisionEvents.StartedByKind);
            float t = 0f; int fi = 0; float vidUntil = -1f, vidNext = 0f; int vidN = 0; string vidTag = "";
            float shotDangerAt = -1f, shotWarnAt = -1f, cueSeenAt = -1f; string lastShotKind = "";
            L($"[de] run {run} mode={modeArg} char={ch} stage={stage} start={startKm}km maxHp={gm.MaxLives} lv={gm.Level}");
            while (t < secs && !gm.IsGameOver && gm.MaxDistance < endKm * 1000f)
            {
                yield return null; t += Time.deltaTime;
                if (fi < forced.Count && t >= forced[fi].t) { DecisionEvents.DebugStart(forced[fi].kind, forced[fi].kind == RoadEventKind.AerialBrute); fi++; }
                if (modeArg == "human") HumanResponder(a);
                if (periodic > 0f && t >= nextPeriodic) { nextPeriodic = t + periodic; Shot($"de_{stage}_{gm.MaxDistance / 1000f:F1}km_p{t:F0}"); }
                // 撮影: イベントの始まり + 危険が来る瞬間 / 連番
                if (DecisionEvents.Running != RoadEventKind.None && DecisionEvents.Running.ToString() != lastShotKind)
                {
                    lastShotKind = DecisionEvents.Running.ToString();
                }
                if (DecisionEvents.Running == RoadEventKind.None) lastShotKind = "";
                // 予兆が新しく出た(危険の時刻が変わった)時: 予兆の途中(危険の 0.45 秒前)と危険の瞬間を撮る
                if (DecisionEvents.CurrentCue != DecisionEvents.Cue.None && Mathf.Abs(DecisionEvents.CueDangerAt - cueSeenAt) > 0.01f)
                {
                    cueSeenAt = DecisionEvents.CueDangerAt; shotDangerAt = cueSeenAt; shotWarnAt = cueSeenAt - 0.45f;
                    // 連番: 予兆が出た瞬間から、危険が過ぎて 0.8 秒後まで(1つの予兆 = 1つのフォルダ)
                    if (vid) { vidUntil = DecisionEvents.CueDangerEnd + 0.8f; vidNext = 0f; vidN = 0; vidTag = $"{stage}_{gm.MaxDistance / 1000f:F1}km_{lastShotKind}_{DecisionEvents.CurrentCue}"; }
                }
                if (shots && shotWarnAt > 0f && Time.time >= shotWarnAt) { Shot($"de_{stage}_{gm.MaxDistance / 1000f:F1}km_{lastShotKind}_{DecisionEvents.CurrentCue}_warn"); shotWarnAt = -1f; }
                if (shots && shotDangerAt > 0f && Time.time >= shotDangerAt) { Shot($"de_{stage}_{gm.MaxDistance / 1000f:F1}km_{lastShotKind}_danger"); shotDangerAt = -1f; }
                if (vid && Time.time < vidUntil && Time.unscaledTime >= vidNext)
                {
                    vidNext = Time.unscaledTime + 1f / 15f;
                    string vd = System.IO.Path.Combine(outDir, "vid_" + vidTag);
                    System.IO.Directory.CreateDirectory(vd);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vd, $"{vidN++:000}.png"));
                }
            }
            bool died = gm.IsGameOver;
            float endD = gm.MaxDistance;
            string byKind = string.Join(" ", DecisionEvents.StartedByKind.Select(kv => (kv.Key, kv.Value - (byKind0.TryGetValue(kv.Key, out int b0) ? b0 : 0))).Where(x => x.Item2 > 0).Select(x => $"{x.Key}:{x.Item2}"));
            string sources = string.Join(" ", deByKind.OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value}")) + (deIsolate ? $" (refunded {deRefunded})" : "");
            string row = $"{modeArg}\t{ch}\t{stage}\t{GameSettings.MaxSpeedPercent}\t{build}\t{startKm:F1}\t{endD / 1000f:F2}\t{(died ? 1 : 0)}\t{(died ? (endD / 1000f).ToString("F2", inv) : "")}\t{t:F0}\t{gm.MaxLives / CombatScale.HpPerHeart}\t{gm.Level}\t{deHits}\t{deHearts}\t{DecisionEvents.Started - st0}\t{RoadEvents.Started - legacy0}\t{byKind}\t{sources}\t{DecisionEvents.SweepHits - sweep0}\t{DecisionEnemy.BruteFlings - fl0}\t{DecisionEnemy.BruteGroundHits - bg0}\t{DecisionEnemy.HeavyHitsDealt - hv0}\t{DecisionEvents.Flattened}\t{string.Join(" ", DecisionEvents.RegionRejects.Select(kv => kv.Key.Replace(" ", "_") + ":" + kv.Value))}";
            tsv.Append(row).Append('\n');
            foreach (var e in deEvents) ev.Append($"{modeArg}\t{ch}\t{stage}\t{e.km:F2}\t{e.kind}\t{e.detail}\t{e.hits}\t{e.hearts}\t{e.hpBefore:F0}\n");
            L($"[de] {row}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "decide.tsv"), tsv.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "events.tsv"), ev.ToString());
            if (a != null) a.SetAutoAvoid(true);
            yield return EndRun();
        }
        GameManager.QaDamageEvent = null;
        DecisionEvents.OnStarted -= OnDeStarted;
        DecisionEvents.OnDetail -= OnDeDetail;
        Check(true, "decide: done");
    }

    void OnDeStarted(RoadEventKind kind, string detail)
    {
        deCur = new DeEvent { km = gm != null ? gm.MaxDistance / 1000f : 0f, kind = kind.ToString(), detail = detail, hpBefore = gm != null ? gm.Lives / (float)CombatScale.HpPerHeart : 0f };
        deEvents.Add(deCur);
        deLastEventStart = Time.time;
    }

    void OnDeDetail(RoadEventKind kind, string detail) { if (deCur != null && deCur.kind == kind.ToString() && Time.time - deLastEventStart < 8f) deCur.detail = detail; }

    void OnDeDamage(string reason, int dmg, int before, int after)
    {
        int hearts = Mathf.RoundToInt(dmg / (float)CombatScale.HpPerHeart);
        deHits++; deHearts += hearts;
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        int colon = k.IndexOf(':');
        string src = colon > 0 ? k.Substring(0, colon) : k;
        if (k.Contains("LaneSweep")) src = "LaneSweep";
        // イベントの間(または終わってから 3 秒)の被弾はそのイベントに数える
        var running = DecisionEvents.Running != RoadEventKind.None ? DecisionEvents.Running : RoadEvents.Active;
        if (running != RoadEventKind.None) src += "@" + running;
        deByKind[src] = (deByKind.TryGetValue(src, out int v) ? v : 0) + hearts;
        if (deCur != null && (running != RoadEventKind.None || Time.time - deLastEventStart < 20f) && running.ToString() == deCur.kind) { deCur.hits++; deCur.hearts += hearts; }
        // -deIsolate 1: 道中イベントの外の被弾(ボス/地形/普通の敵)は戻す = 道中イベントだけの被弾で何 km まで行けるかを測る
        if (deIsolate && running == RoadEventKind.None && gm != null)
        {
            deRefunded += hearts;
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { Mathf.Min(gm.MaxLives, after + dmg) });
        }
    }
    bool deIsolate; int deRefunded;

    // ---- 「画面を見ている人」の操作(オートは ON のまま。予兆にだけ反応する) ----
    float deJumpAt = -1f, deDoubleAt = -1f, deLaunchCool, deAirAttackUntil, deAirAttackNext; bool deHeldAvoid;
    void HumanResponder(HighSpeedAssist a)
    {
        if (pc == null || gm == null || gm.IsRewardSequenceRunning) return;
        var cue = DecisionEvents.CurrentCue;
        float now = Time.time;
        // 空の帯/上空封鎖: 地上にいる(オートの跳躍を止める。空中なら急降下で降りる)
        // 危険が来る少し前〜過ぎるまでだけ(それより前に跳躍を止めると、平らにした区間の手前の穴/壁に当たる)
        bool stayLow = cue == DecisionEvents.Cue.StayLow && now > DecisionEvents.CueDangerAt - 0.35f && now < DecisionEvents.CueDangerEnd + 0.15f;
        // 空中雑魚の予兆(急降下/横切り)は一瞬なので、オートの回避は止めない(止めると穴/壁に当たる)。空中なら急降下で降りるだけ
        bool holdAvoid = stayLow && !DecisionEvents.CueFromAir;
        if (a != null && holdAvoid != deHeldAvoid) { a.SetAutoAvoid(!holdAvoid); deHeldAvoid = holdAvoid; }
        if (stayLow && !pc.IsGrounded && pc.AssistVelocityY > -1f && now > DecisionEvents.CueDangerAt - 0.9f) pc.debugInjectOnce = PlayerController.FlickDirection.Down;
        // 地上の帯/地上封鎖: 危険が来る少し前に跳ぶ(地上封鎖は背が高いので頂点で二段)
        if (cue == DecisionEvents.Cue.JumpOver && deJumpAt < 0f && now < DecisionEvents.CueDangerEnd)
        {
            bool tall = DecisionEvents.Running == RoadEventKind.GroundBlock;
            deJumpAt = DecisionEvents.CueDangerAt - (tall ? 0.32f : 0.16f);
            deDoubleAt = tall ? deJumpAt + 0.42f : (DecisionEvents.CueDangerEnd - DecisionEvents.CueDangerAt > 0.45f ? deJumpAt + 0.45f : -1f);
        }
        if (deJumpAt > 0f && now >= deJumpAt)
        {
            if (pc.IsGrounded || pc.AssistJumpsUsed < pc.AssistMaxJumps) pc.debugInjectOnce = PlayerController.FlickDirection.Up;
            deJumpAt = -2f; // 使った(この予兆の間はもう跳ばない)
        }
        if (deDoubleAt > 0f && now >= deDoubleAt) { if (!pc.IsGrounded) pc.debugInjectOnce = PlayerController.FlickDirection.Up; deDoubleAt = -1f; }
        if (cue != DecisionEvents.Cue.JumpOver || now > DecisionEvents.CueDangerEnd + 0.3f) { if (deJumpAt == -2f) deJumpAt = -1f; }
        // 鎧の敵: 近づいたら打ち上げ → 空中で前攻撃を数回
        if (now > deLaunchCool)
        {
            float px = pc.transform.position.x;
            foreach (var d in FindObjectsByType<DecisionEnemy>(FindObjectsSortMode.None))
            {
                if (d == null || d.role != DecisionRole.Brute) continue;
                var ec = d.GetComponent<EnemyController>();
                if (ec == null || !ec.CanDealContactDamage) continue;
                float dx = d.transform.position.x - px;
                float reach = 1.6f + Mathf.Max(0f, pc.CurrentAutoRunSpeed) * 0.1f;
                if (dx > 0.3f && dx < reach && pc.IsGrounded)
                {
                    pc.debugInjectOnce = PlayerController.FlickDirection.Up; // 打ち上げ(上フリック = 跳んで上攻撃)
                    deLaunchCool = now + 0.9f; deAirAttackUntil = now + 0.8f; deAirAttackNext = now + 0.22f;
                    break;
                }
            }
        }
        if (now < deAirAttackUntil && now >= deAirAttackNext && !pc.IsGrounded && pc.debugInjectOnce == null)
        {
            pc.debugInjectOnce = PlayerController.FlickDirection.Forward;
            deAirAttackNext = now + 0.2f;
        }
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 空中雑魚(2026-11-05)の確認: -qaAir <dir>
//   [-arStage wasteland_road] [-arChar swordsman] [-arStartKm 5] [-arSecs 90] [-arMode idle|human] [-arSpeed 100] [-arSpeedScale 1]
//   [-arInvincible 1(撮影用: 倒れない)] [-arPeriodic 秒(この秒数ごとに撮る)] [-arShots 1(種ごとに 予兆/攻撃/撃破 の最初を撮る)]
//   [-arForce id@秒,...(その時刻にプレイヤーの前の空へ 1 体出す。群れは数体)] [-arVid 開始秒@長さ,...(15fps の連番)]
//   [-arPick 1(カードの選択をすぐ選ぶ: 撮影用)] [-arDecision 1(判断型の道中イベントを 20 秒ごとに起こす: AirDive/SkyBlock/AirCross/TelegraphRush/Alternate の順)]
//  結果: air.tsv(1行 = 1回の走り: 種ごとの出現/撃破/被弾、全体の被弾)・air.log
public partial class QaSweep
{
    readonly Dictionary<string, int> arHitsBy = new Dictionary<string, int>();
    int arHits, arHearts;

    IEnumerator AirMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stage = Arg("-arStage", "wasteland_road"), ch = Arg("-arChar", "swordsman"), modeArg = Arg("-arMode", "idle");
        float startKm = float.Parse(Arg("-arStartKm", "5"), inv), secs = float.Parse(Arg("-arSecs", "90"), inv);
        autoPick = Arg("-arPick", "0") == "1";
        bool invincible = Arg("-arInvincible", "0") == "1", shots = Arg("-arShots", "0") == "1", decision = Arg("-arDecision", "0") == "1";
        float periodic = float.Parse(Arg("-arPeriodic", "0"), inv), nextPeriodic = 2f;
        GameSettings.SetMaxSpeedPercent(int.Parse(Arg("-arSpeed", "100")));
        float speedScale = float.Parse(Arg("-arSpeedScale", "1"), inv);
        var forced = Arg("-arForce", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => { var p = s.Split('@'); return (id: p[0], t: float.Parse(p.Length > 1 ? p[1] : "5", inv)); }).OrderBy(x => x.t).ToList();
        var vids = Arg("-arVid", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => { var p = s.Split('@'); return (t: float.Parse(p[0], inv), len: float.Parse(p.Length > 1 ? p[1] : "4", inv)); }).ToList();

        yield return BeginRun(ch, stage);
        stopKeepAlive = true; yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, invincible);
        var a = HighSpeedAssist.Instance;
        if (a != null) { bool on = Arg("-arAssist", "1") == "1"; a.SetEnabled(on); a.SetAutoAttack(on); a.SetAutoAvoid(on); a.SetAutoInBoss(on); } // -arAssist 0 = オート OFF(操作なし: 空中雑魚が当たるかの確認)
        // 判断型の道中イベントの試験(-qaDecide)と同じ: 距離に応じた枚数のカード(既定 recommended)を持たせる(-arBuild none で無し)
        string build = Arg("-arBuild", "recommended");
        if (build != "none" && startKm >= 5f)
        {
            int picks = Mathf.RoundToInt(4 + startKm / 4f);
            var ids = DfBuilds.TryGetValue(build, out var bl) ? bl : DfBuilds["recommended"];
            var lvs = new Dictionary<string, int>();
            for (int p = 0; p < picks; p++) { string id = ids[p % ids.Length]; lvs[id] = Mathf.Min(9, (lvs.TryGetValue(id, out int v) ? v : 0) + 1); }
            foreach (var kv in lvs) { var c = CardDatabase.FindBaseById(kv.Key); if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(kv.Value, GameManager.MaxRunCardLevel)); }
        }
        WarpTo(startKm * 1000f); yield return new WaitForSeconds(1f);
        if (Arg("-arFlat", "0") == "1") // 当たりの確認用: 先 3km の穴を埋めて平らに(オート OFF でも穴に落ちない)
        {
            var tmz = TerrainManager.Instance; var pcz = PlayerController.Instance;
            if (tmz != null && pcz != null) { var lx0 = FloatingOrigin.ToLogical(pcz.transform.position.x); tmz.SetResumeFlatZone(lx0 - 5f, lx0 + 3000f); var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false; }
        }
        PlayerController.DebugSpeedScale = speedScale;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
        GameManager.QaDamageEvent = OnArDamage;
        GameManager.QaShieldEvent = r => { string k = "SHIELD:" + (r.StartsWith("HPZero:") ? r.Substring(7) : r); arHitsBy[k] = (arHitsBy.TryGetValue(k, out int v0) ? v0 : 0) + 1; };
        arHitsBy.Clear(); arHits = arHearts = 0;
        int placed0 = EncounterDirector.AirMinionsPlaced, swarm0 = EncounterDirector.AirMinionSwarmMembers;
        var counts0 = new Dictionary<string, int>(EncounterDirector.AirMinionCounts);
        var seenStates = new HashSet<string>();
        var spawnedBy = new Dictionary<string, int>(); var killedBy = new Dictionary<string, int>();
        var tracked = new Dictionary<EnemyController, string>(); var everSeen = new HashSet<EnemyController>();
        L($"[air] stage={stage} char={ch} start={startKm}km mode={modeArg} speed={GameSettings.MaxSpeedPercent}% x{speedScale} inv={invincible}");
        float t = 0f; int fi = 0; float nextDecision = 8f; int decIdx = 0;
        RoadEventKind[] decKinds = { RoadEventKind.AirDive, RoadEventKind.SkyBlock, RoadEventKind.AirCross, RoadEventKind.TelegraphRush, RoadEventKind.Alternate };
        int vidN = 0; float vidNext = 0f;
        while (t < secs && !gm.IsGameOver)
        {
            yield return null; t += Time.deltaTime;
            if (fi < forced.Count && t >= forced[fi].t) { ArForceSpawn(forced[fi].id); fi++; }
            if (decision && t >= nextDecision) { nextDecision = t + 20f; DecisionEvents.DebugStart(decKinds[decIdx++ % decKinds.Length], false); }
            if (modeArg == "human") HumanResponder(a);
            if (autoPick && gm.IsRewardSequenceWaitingForSelection && !arPicking) StartCoroutine(ArPick()); // 撮影用: カードの選択で画面が隠れないよう、すぐ選ぶ
            if (periodic > 0f && t >= nextPeriodic) { nextPeriodic = t + periodic; Shot($"air_{stage}_{gm.MaxDistance / 1000f:F2}km_p{t:F0}"); }
            // 空中雑魚の追跡(出現・撃破・状態の撮影)
            foreach (var sb in FindObjectsByType<EnemySpecialBehavior>(FindObjectsSortMode.None))
            {
                if (sb == null || sb.kind < EnemyBehaviorKind.AirLow || sb.kind > EnemyBehaviorKind.AirSwarm) continue;
                var ec = sb.GetComponent<EnemyController>();
                if (ec == null || ec.Definition == null) continue;
                string id = ec.Definition.enemyId;
                if (everSeen.Add(ec)) { tracked[ec] = id; spawnedBy[id] = (spawnedBy.TryGetValue(id, out int s0) ? s0 : 0) + 1; }
                if (shots)
                {
                    string st = sb.DebugState; int sp = st.LastIndexOf(' ');
                    string state = sp >= 0 ? st.Substring(sp + 1) : st;
                    string key = id + ":" + state;
                    if ((state == "Telegraph" || state == "Attack") && !seenStates.Contains(key) && OnScreen(sb.transform.position))
                    { seenStates.Add(key); Shot($"air_{id}_{state}_{gm.MaxDistance / 1000f:F2}km"); }
                }
            }
            foreach (var kv in tracked.ToList())
                if (kv.Key == null || kv.Key.IsDying || !kv.Key.gameObject.activeInHierarchy) { if (kv.Key != null && kv.Key.IsDying) killedBy[kv.Value] = (killedBy.TryGetValue(kv.Value, out int k0) ? k0 : 0) + 1; tracked.Remove(kv.Key); }
            foreach (var v in vids)
                if (t >= v.t && t < v.t + v.len && Time.unscaledTime >= vidNext)
                {
                    vidNext = Time.unscaledTime + 1f / 15f;
                    string vd = System.IO.Path.Combine(outDir, $"vid_{stage}_{v.t:F0}");
                    System.IO.Directory.CreateDirectory(vd);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vd, $"{vidN++:000}.png"));
                }
        }
        bool died = gm.IsGameOver;
        var placed = EncounterDirector.AirMinionCounts.Select(kv => (kv.Key, kv.Value - (counts0.TryGetValue(kv.Key, out int c0) ? c0 : 0))).Where(x => x.Item2 > 0);
        string row = $"{stage}\t{ch}\t{modeArg}\t{GameSettings.MaxSpeedPercent}\t{speedScale}\t{startKm:F1}\t{gm.MaxDistance / 1000f:F2}\t{(died ? 1 : 0)}\t{t:F0}\t{EncounterDirector.AirMinionsPlaced - placed0}\t{EncounterDirector.AirMinionSwarmMembers - swarm0}\t"
            + string.Join(" ", placed.Select(x => $"{x.Key}:{x.Item2}")) + "\t" + string.Join(" ", spawnedBy.Select(x => $"{x.Key}:{x.Value}")) + "\t" + string.Join(" ", killedBy.Select(x => $"{x.Key}:{x.Value}"))
            + $"\t{arHits}\t{arHearts}\t" + string.Join(" ", arHitsBy.OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value}"))
            + "\t" + string.Join(" ", EncounterDirector.AirMinionRejects.Select(x => $"{x.Key}:{x.Value}"));
        L($"[air] {row}");
        string path = System.IO.Path.Combine(outDir, "air.tsv");
        if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "stage\tchar\tmode\tspeed\tscale\tstart_km\tend_km\tdied\tsecs\tair_picks\tswarm_extra\tpicked\tseen\tkilled\thits\thearts\thit_sources\tair_rejects\n");
        System.IO.File.AppendAllText(path, row + "\n");
        GameManager.QaDamageEvent = null; GameManager.QaShieldEvent = null;
        yield return EndRun();
        Check(true, "air: done");
    }

    bool autoPick, arPicking;
    IEnumerator ArPick() { arPicking = true; yield return AutoPickOnce(); arPicking = false; }

    bool OnScreen(Vector3 p)
    {
        var cam = Camera.main;
        if (cam == null) return true;
        Vector3 v = cam.WorldToViewportPoint(p);
        return v.x > 0.05f && v.x < 0.95f && v.y > 0.05f && v.y < 0.95f;
    }

    // プレイヤーの前の空へ 1 体(群れは種の数)出す。高さは種の範囲の真ん中(行動が自分の高さへ寄せる)
    void ArForceSpawn(string id)
    {
        var def = EnemyDatabase.FindById(id);
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        if (def == null || tm == null || pc == null) { L($"[air] force {id}: not found"); return; }
        var sp = AirMinionTuning.I.Find(id);
        float speed = Mathf.Max(5f, pc.CurrentAutoRunSpeed);
        float x0 = pc.transform.position.x + Mathf.Max(14f, speed * 1.2f);
        int n = sp != null && sp.role == AirRole.Swarm ? Mathf.Max(1, sp.group.y) : 1;
        for (int i = 0; i < n; i++)
        {
            float x = x0 + (sp != null && sp.vFormation ? ((i + 1) / 2) * 1.0f : Random.Range(-0.4f, 2.6f));
            float g = tm.GetGroundLineAt(x);
            float h = sp != null ? (sp.vFormation ? (sp.height.x + sp.height.y) * 0.5f + ((i % 2 == 1) ? 1 : -1) * ((i + 1) / 2) * 0.5f : Random.Range(sp.height.x, sp.height.y)) : 2.7f;
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, g + h), EnemyAiTier.T2, def.behaviorKind);
            if (go != null) L($"[air] force {id} at x+{x - pc.transform.position.x:F1} h{h:F1}");
        }
    }

    void OnArDamage(string reason, int dmg, int before, int after)
    {
        int hearts = Mathf.RoundToInt(dmg / (float)CombatScale.HpPerHeart);
        arHits++; arHearts += hearts;
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        arHitsBy[k] = (arHitsBy.TryGetValue(k, out int v) ? v : 0) + hearts;
    }
}
#endif

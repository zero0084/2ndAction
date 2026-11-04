#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 道中の見え方の監査(2026-11-04、実機の不具合「障害物が画面内に突然現れる」「道中の空中敵を見かけない」):
//   -qaRoadAudit <dir> [-raStage wasteland_road] [-raKm 0.3] [-raSecs 240] [-raMode pass|kill] [-raSpeed 1] [-raTag x] [-raShots 1]
//  障害物: 生成された瞬間 / 画面で初めて見えた瞬間(実際のカメラ)/ 接触(プレイヤーが手前の端に届いた)瞬間。
//          見えた時に画面の右端から入ってきたか(=右端の外で生成された)、見えてから接触までの秒数。
//  空中の雑魚: 生成 / 初めて見えた / 画面にいた秒数 / 高さ(画面の縦の位置)/ 倒した or 見逃した(見逃した敵の報酬が出ないこと)。
//  ボス: 関門のボスは数秒戦ってから倒す(ボスの後で障害物が近くに置かれる不具合の再現のため、距離の除外が貯まる)。
//  pass = 攻撃しない(無敵で通過)、kill = 高速時の自動操作補助で戦う。縦は -screen-width 486 -screen-height 1080
public partial class QaSweep
{
    class RaObs { public int id; public string name; public float spawnT, spawnDx, spawnVx, visT = -1f, visVx, visDx, contactT = -1f; public bool popIn; }
    class RaFly { public int id; public string name; public float spawnT, spawnDx, spawnVy, visT = -1f, visVx, onScreen, minVy = 9f, maxVy = -9f, lastT; public bool killed, passed; public int mileAtPass = -1; }

    IEnumerator RoadAuditMode()
    {
        string stage = Arg("-raStage", "wasteland_road");
        float km = float.Parse(Arg("-raKm", "0.3"), System.Globalization.CultureInfo.InvariantCulture);
        float secs = float.Parse(Arg("-raSecs", "240"), System.Globalization.CultureInfo.InvariantCulture);
        string mode = Arg("-raMode", "pass");
        float speed = float.Parse(Arg("-raSpeed", "1"), System.Globalization.CultureInfo.InvariantCulture);
        string tag = Arg("-raTag", stage + "_" + mode);
        bool shots = Arg("-raShots", "0") == "1";
        int vfps = int.Parse(Arg("-raFps", "0")); // 動画用の連番(-raFps 15)。撮る時は 1/30 秒ずつ進める
        int vevery = vfps > 0 ? Mathf.Max(1, 30 / vfps) : 0, vframe = 0;
        yield return BeginRun("swordsman", stage);
        SetGod(true);
        if (km > 0.05f) { gm.DebugWarpToDistance(km * 1000f); yield return new WaitForSeconds(0.6f); }
        if (mode == "kill") AssistOn();
        var exField = typeof(GameManager).GetField("distanceExclusionOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var obs = new Dictionary<int, RaObs>();
        var fly = new Dictionary<int, RaFly>();
        var seenObs = new HashSet<int>(ObstacleController.All.Where(o => o != null).Select(o => o.GetHashCode()));
        var seenEn = new HashSet<int>(FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Select(e => e.GetHashCode()));
        float t = 0f, bossT = 0f, d0 = gm.MaxDistance;
        int bosses = 0, shotN = 0;
        var cam = Camera.main;
        if (vfps > 0) Time.captureDeltaTime = 1f / 30f;
        while (t < secs && gm.HasStarted && !gm.IsGameOver)
        {
            if (vevery > 0 && (vframe++ % vevery) == 0) Shot($"rav_{tag}_{vframe:00000}");
            PlayerController.DebugSpeedScale = speed;
            if (gm.IsLocalChoiceOpen) { yield return null; t += Time.deltaTime; continue; }
            cam = Camera.main;
            float px = pc.transform.position.x;
            // ---- ボス: 少し戦ってから倒す
            var bm = BossManager.Instance;
            if (bm != null && bm.IsBossPhase && !bm.AirRaidActive)
            {
                bossT += Time.deltaTime;
                if (bossT > 4f)
                    foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
                        if (b != null && !b.IsDead && b.isActiveAndEnabled && !b.IsEntering && b.DebugKillWithAttack(BossFinalAttack.Forward)) { bosses++; bossT = -6f; }
            }
            else if (bossT > 0f) bossT = 0f;
            // ---- 障害物
            foreach (var o in ObstacleController.All)
            {
                if (o == null || !o.isActiveAndEnabled) continue;
                int id = o.GetHashCode();
                Bounds bb = RaBounds(o.gameObject);
                if (!obs.TryGetValue(id, out var r))
                {
                    if (seenObs.Contains(id)) continue;
                    seenObs.Add(id);
                    Vector3 v = cam.WorldToViewportPoint(new Vector3(bb.min.x, bb.center.y, 0f));
                    r = new RaObs { id = id, name = o.name, spawnT = t, spawnDx = bb.min.x - px, spawnVx = v.x };
                    obs[id] = r;
                }
                if (r.visT < 0f && RaVisible(cam, bb, out float vx, out _))
                {
                    r.visT = t; r.visVx = vx; r.visDx = bb.min.x - px;
                    r.popIn = r.spawnVx < 1.0f || vx < 0.9f; // 画面の中で生成された or 右端から入ってこないで画面の中で現れた(高速の縦画面は 1 フレームで画面の 4% 進むので端の判定は 0.9)
                    if (r.popIn && shots && shotN < 6) { shotN++; Shot($"ra_{tag}_pop{shotN}"); }
                }
                if (r.contactT < 0f && px + 0.4f >= bb.min.x) r.contactT = t;
            }
            // ---- 空中の雑魚
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                if (e == null || !e.isActiveAndEnabled || e.movementType != EnemyMovementType.Flying || e.GetComponent<BonusEnemy>() != null) continue;
                int id = e.GetHashCode();
                if (!fly.TryGetValue(id, out var f))
                {
                    if (seenEn.Contains(id)) continue;
                    seenEn.Add(id);
                    var v0 = cam.WorldToViewportPoint(e.transform.position);
                    f = new RaFly { id = id, name = e.name.Replace("(Clone)", ""), spawnT = t, spawnDx = e.transform.position.x - px, spawnVy = v0.y };
                    fly[id] = f;
                }
                f.lastT = t;
                if (e.IsDying) f.killed = true;
                Bounds eb = RaBounds(e.gameObject);
                if (RaVisible(cam, eb, out float evx, out float evy))
                {
                    if (f.visT < 0f) { f.visT = t; f.visVx = evx; }
                    f.onScreen += Time.deltaTime;
                    f.minVy = Mathf.Min(f.minVy, evy); f.maxVy = Mathf.Max(f.maxVy, evy);
                }
                if (!f.passed && !f.killed && e.transform.position.x < px - 3f) { f.passed = true; f.mileAtPass = gm.RunMile; }
            }
            yield return null;
            t += Time.deltaTime;
        }
        PlayerController.DebugSpeedScale = 1f;
        Time.captureDeltaTime = 0f;
        float dist = gm.MaxDistance - d0;
        float ex = exField != null ? (float)exField.GetValue(gm) : 0f;

        // ---- まとめ
        var sb = new StringBuilder("id\tname\tspawnT\tspawnDx\tspawnVx\tvisT\tvisVx\tvisDx\tcontactT\tleadSec\tpopIn\n");
        foreach (var r in obs.Values.OrderBy(x => x.spawnT))
            sb.Append($"{r.id}\t{r.name}\t{r.spawnT:F2}\t{r.spawnDx:F1}\t{r.spawnVx:F2}\t{r.visT:F2}\t{r.visVx:F2}\t{r.visDx:F1}\t{r.contactT:F2}\t{(r.visT >= 0 && r.contactT >= 0 ? r.contactT - r.visT : -1f):F2}\t{(r.popIn ? 1 : 0)}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"ra_{tag}_obstacles.tsv"), sb.ToString());
        var seenO = obs.Values.Where(r => r.visT >= 0f).ToList();
        int pops = seenO.Count(r => r.popIn);
        var leads = seenO.Where(r => r.contactT >= 0f).Select(r => r.contactT - r.visT).OrderBy(x => x).ToList();
        L($"[ra] {tag}: {dist:F0}m in {t:F0}s at speed x{speed}, bosses {bosses}, distance excluded {ex:F1}m");
        L($"[ra] {tag} obstacles: spawned {obs.Count}, seen {seenO.Count}, POP-IN {pops} (appeared inside the screen), spawn dx min {(obs.Count > 0 ? obs.Values.Min(r => r.spawnDx) : 0f):F1}m, lead seconds min {(leads.Count > 0 ? leads[0] : 0f):F2} median {(leads.Count > 0 ? leads[leads.Count / 2] : 0f):F2}");
        foreach (var r in seenO.Where(r => r.popIn).Take(8)) L($"   pop-in {r.name} t={r.visT:F1}s spawned {r.spawnDx:F1}m ahead (viewport x {r.spawnVx:F2}) lead {(r.contactT >= 0 ? r.contactT - r.visT : -1f):F2}s");
        Check(pops == 0, $"{tag}: no obstacle appears inside the screen ({pops})");

        sb = new StringBuilder("id\tname\tspawnT\tspawnDx\tspawnVy\tvisT\tvisVx\tonScreen\tminVy\tmaxVy\tkilled\tpassed\n");
        foreach (var f in fly.Values.OrderBy(x => x.spawnT))
            sb.Append($"{f.id}\t{f.name}\t{f.spawnT:F2}\t{f.spawnDx:F1}\t{f.spawnVy:F2}\t{f.visT:F2}\t{f.visVx:F2}\t{f.onScreen:F2}\t{f.minVy:F2}\t{f.maxVy:F2}\t{(f.killed ? 1 : 0)}\t{(f.passed ? 1 : 0)}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"ra_{tag}_flyers.tsv"), sb.ToString());
        var fs = fly.Values.ToList();
        var vis = fs.Where(f => f.visT >= 0f).ToList();
        L($"[ra] {tag} flyers: spawned {fs.Count} ({fs.Count / Mathf.Max(0.1f, dist / 1000f):F1}/km), seen on screen {vis.Count}, killed {fs.Count(f => f.killed)}, passed {fs.Count(f => f.passed)}, avg on-screen {(vis.Count > 0 ? vis.Average(f => f.onScreen) : 0f):F1}s, viewport y {(vis.Count > 0 ? vis.Min(f => f.minVy) : 0f):F2}..{(vis.Count > 0 ? vis.Max(f => f.maxVy) : 0f):F2}, entered from x {(vis.Count > 0 ? vis.Min(f => f.visVx) : 0f):F2}");
        foreach (var g in fs.GroupBy(f => f.name)) L($"   {g.Key}: {g.Count()} (seen {g.Count(f => f.visT >= 0f)})");
        // 空中の敵の間隔(前の敵を見逃しても次が出るか): 生成の間の最大の空き
        var times = fs.Select(f => f.spawnT).OrderBy(x => x).ToList();
        float maxGap = 0f; for (int i = 1; i < times.Count; i++) maxGap = Mathf.Max(maxGap, times[i] - times[i - 1]);
        L($"[ra] {tag} flyers: longest gap between flyer spawns {maxGap:F1}s, encounters with a flyer {EncounterDirector.FlyerEncounters}, flyer-due boosts {EncounterDirector.FlyerDueBoosts}, flyers added {EncounterDirector.DueFlyersAdded}{(EncounterDirector.QaFlyerLegacy ? " (LEGACY)" : "")}");
        if (mode == "pass") Check(vis.Count >= 2, $"{tag}: flyers keep appearing while the previous ones are left behind ({vis.Count} seen)");
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        SetGod(false);
        yield return EndRun();
    }

    static Bounds RaBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<SpriteRenderer>();
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in rs) { if (r == null || !r.enabled || r.sprite == null) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        return b;
    }

    // 実際のカメラに写っているか(縦横どちらも)。vx = 見えている部分の左端(画面の何割の位置)、vy = 中心の高さ
    static bool RaVisible(Camera cam, Bounds b, out float vx, out float vy)
    {
        Vector3 a = cam.WorldToViewportPoint(b.min), c = cam.WorldToViewportPoint(b.max);
        vx = a.x; vy = cam.WorldToViewportPoint(b.center).y;
        return c.x > 0.01f && a.x < 0.99f && c.y > 0.01f && a.y < 0.99f;
    }
}
#endif

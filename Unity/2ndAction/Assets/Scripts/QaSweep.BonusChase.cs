#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// BONUS ZONE の逃げるゴブリンの間合い(2026-11-04、実機の不具合「ボーナスで敵を追い越してしまう」):
//   -qaBonusChase <dir> [-bcKmh 35,100] [-bcSecs 22] [-bcType mile_rush] [-bcFps 0|15] [-bcTag x]
//  黒剣士を人の手の代わりに動かす: 一番近いゴブリンが前攻撃の届く距離(-bcReach 3.0m)に入ったら前攻撃を1回(連打しない)。
//  数える: 届いてから当たるまで / 当たった後に離れたか / 追い越し(ゴブリンがプレイヤーより後ろ)/ 速さの急な変化(相対の加速度)/ 前への瞬間移動
//  -bonusChaseLegacy を付けると直す前の動き(前後比較)。-bcFps で撮影(動画用の連番)
public partial class QaSweep
{
    class BcGob { public int id, hits, reachEnter, overtakes, warps, cycles; public float reachT = -1f, lastHitT = -1f, maxAcc, sumWait; public int waits; public bool away = true, behind; public float prevDx = float.NaN, prevRelV = float.NaN, accT, accDx; public bool prevQuiet; public float lastStillLog = -9f; public int passedOver; }

    IEnumerator BonusChaseMode()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var kmhs = Arg("-bcKmh", "35,100").Split(',').Select(s => float.Parse(s, inv)).ToList();
        float secs = float.Parse(Arg("-bcSecs", "22"), inv), reach = float.Parse(Arg("-bcReach", "3.0"), inv);
        string type = Arg("-bcType", "mile_rush"), tag0 = Arg("-bcTag", BonusChaseLegacyArg ? "legacy" : "new");
        int fps = int.Parse(Arg("-bcFps", "0"));
        // -bcDt 0.05: 1 フレームの時間を固定(エミュレーターの粗いフレームを Windows で再現する。撮影は毎フレーム)。0 = 実時間
        float fixDt = float.Parse(Arg("-bcDt", "0"), inv);
        foreach (float kmh in kmhs)
        {
            string tag = $"{tag0}_{kmh:F0}";
            yield return BeginRun("swordsman", "wasteland_road");
            SetGod(true);
            GameManager.BlockExpGain = true;
            gm.DebugWarpToDistance(2300f);
            float w = 0f;
            while (w < 20f && ((Bm != null && Bm.IsBossPhase) || (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle))) { yield return null; w += Time.deltaTime; }
            yield return new WaitForSeconds(0.5f);
            SetKmh(kmh);
            bool forced = BonusZone.Instance != null && BonusZone.Instance.Force(type);
            Check(forced, $"{tag}: BONUS ZONE {type} started");
            float stepDt = fixDt > 0f ? fixDt : 1f / 30f;
            if (fps > 0 || fixDt > 0f) Time.captureDeltaTime = stepDt;
            int every = fps > 0 ? Mathf.Max(1, Mathf.RoundToInt(1f / stepDt) / fps) : 0, frame = 0;
            var gobs = new Dictionary<int, BcGob>();
            var series = new StringBuilder("t\tdx\tpace\tplayerV\tgoblinV\n");
            float t = 0f, attackCool = 0f; int swings = 0;
            Vector3 prevP = pc.transform.position;
            var prevG = new Dictionary<int, Vector3>();
            while (t < secs && gm.HasStarted)
            {
                SetKmh(kmh);
                float dt = Time.deltaTime;
                Vector3 pp = pc.transform.position;
                float pv = dt > 0f ? (pp.x - prevP.x) / dt : 0f;
                bool pWarp = Mathf.Abs(pp.x - prevP.x) > Mathf.Max(4f, pv * dt * 3f + 1f);
                EnemyController target = null; float best = 99f;
                foreach (var be in FindObjectsByType<BonusEnemy>(FindObjectsSortMode.None))
                {
                    if (be == null || be.kind != BonusEnemyKind.TreasureGoblin || !be.isActiveAndEnabled || be.Killed || be.Dying) continue;
                    var ec = be.GetComponent<EnemyController>();
                    int id = be.GetHashCode();
                    if (!gobs.TryGetValue(id, out var g)) { g = new BcGob { id = id }; gobs[id] = g; }
                    Vector3 gp = be.transform.position;
                    float dx = gp.x - pp.x;
                    if (!be.Leaving)
                    {
                        // 相対速度の変化(なめらかさ)と前への瞬間移動
                        if (prevG.TryGetValue(id, out var pg) && dt > 0f && !pWarp)
                        {
                            // なめらかさ: ゴブリン自身の速さの変化を 0.1 秒ごとに測る(被弾のノックバック 0.6 秒とヒットストップの間は除く)
                            g.accT += dt; g.accDx += gp.x - pg.x;
                            if (g.accT >= 0.1f)
                            {
                                float gv = g.accDx / g.accT;
                                bool quiet = ec != null && !ec.IsLaunchedForCombo && be.LastHitTime < Time.time - 0.6f && Time.timeScale > 0.9f;
                                if (!float.IsNaN(g.prevRelV) && quiet && g.prevQuiet) g.maxAcc = Mathf.Max(g.maxAcc, Mathf.Abs(gv - g.prevRelV) / g.accT);
                                g.prevRelV = gv; g.prevQuiet = quiet; g.accT = 0f; g.accDx = 0f;
                            }
                            if (gp.x - pg.x > Mathf.Max(1.2f, pv * dt + 1f))
                            {
                                g.warps++;
                                // 前への跳び: 画面に見えていたか(跳ぶ前/後)・その時の状態
                                var wc = Camera.main;
                                Vector3 v0 = wc != null ? wc.WorldToViewportPoint(pg) : Vector3.zero, v1 = wc != null ? wc.WorldToViewportPoint(gp) : Vector3.zero;
                                bool on0 = v0.x > 0f && v0.x < 1f && v0.y > 0f && v0.y < 1f, on1 = v1.x > 0f && v1.x < 1f && v1.y > 0f && v1.y < 1f;
                                var sbw = be.GetComponent<EnemySpecialBehavior>();
                                L($"   [warp] {tag} t={t:F2} frame={frame} dx={dx:F2} step={gp.x - pg.x:F2}m (player {pp.x - prevP.x:F2}m) dt={dt:F3} {(on0 || on1 ? "ON SCREEN" : "off screen")} vp {v0.x:F2},{v0.y:F2} -> {v1.x:F2},{v1.y:F2} pace={(sbw != null ? sbw.DebugGoblinPace : "-")} {(sbw != null ? sbw.DebugGoblinGround : "")} behaviour={(sbw != null && sbw.enabled)} {(ec != null ? ec.DebugAirState : "")}");
                            }
                        }
                        // 追い越し = 同じ高さ(高さの差 2m 未満)で前へ出た。空中コンボで上の段/空を通り過ぎるのは数えない(記録だけ)
                        if (dx < -0.3f && !g.behind && Mathf.Abs(gp.y - pp.y) >= 2f) { g.behind = true; g.passedOver++; }
                        if (dx < -0.3f && !g.behind)
                        {
                            g.behind = true; g.overtakes++;
                            var sbh = be.GetComponent<EnemySpecialBehavior>();
                            L($"   [overtake] {tag} t={t:F2} dx={dx:F2} dy={gp.y - pp.y:F1} playerAir={!pc.IsGrounded} pv={pv:F1} pace={(sbh != null ? sbh.DebugGoblinPace : "-")} behaviour={(sbh != null && sbh.enabled)} lastHit={Time.time - be.LastHitTime:F2}s ago {(ec != null ? ec.DebugAirState : "")}");
                        }
                        // 止まっている(プレイヤーの速さの 2 割未満)のに倒れていない時の状態
                        if (prevG.TryGetValue(id, out var pg2) && dt > 0f && pv > 5f && (gp.x - pg2.x) / dt < pv * 0.2f && dx < 4f && (t - g.lastStillLog) > 0.25f)
                        {
                            g.lastStillLog = t;
                            var sbh = be.GetComponent<EnemySpecialBehavior>();
                            L($"   [still] {tag} t={t:F2} dx={dx:F2} gv={(gp.x - pg2.x) / dt:F1} pv={pv:F1} pace={(sbh != null ? sbh.DebugGoblinPace : "-")} {(sbh != null ? sbh.DebugGoblinGround : "")} pY={pp.y:F1} behaviour={(sbh != null && sbh.enabled)} lastHit={Time.time - be.LastHitTime:F2}s ago {(ec != null ? ec.DebugAirState : "")}");
                        }
                        if (dx > 0.3f) g.behind = false;
                        bool inReach = dx > 0f && dx <= reach;
                        if (inReach && g.reachT < 0f) { g.reachT = t; g.reachEnter++; }
                        if (!inReach && dx > reach + 1.5f) { if (g.reachT >= 0f && g.lastHitT >= g.reachT) g.cycles++; g.reachT = -1f; }
                        if (dx > 0f && dx < best) { best = dx; target = ec; }
                    }
                    prevG[id] = gp;
                    if (be.LastHitTime > g.lastHitT && be.LastHitTime > 0f && g.reachT >= 0f)
                    {
                        if (g.lastHitT < g.reachT) { g.sumWait += Mathf.Max(0f, t - g.reachT); g.waits++; }
                        g.lastHitT = be.LastHitTime; g.hits++;
                    }
                }
                // 人の手の代わり: 届く距離に入ったら前攻撃を1回(前の攻撃が終わってから)
                attackCool -= dt;
                if (target != null && best <= reach && attackCool <= 0f && !pc.IsAttacking)
                {
                    StartCoroutine(BcFlick());
                    attackCool = 0.45f; swings++;
                }
                var first = gobs.Count > 0 ? FindObjectsByType<BonusEnemy>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.kind == BonusEnemyKind.TreasureGoblin && b.isActiveAndEnabled && !b.Killed) : null;
                if (first != null)
                {
                    var sb2 = first.GetComponent<EnemySpecialBehavior>();
                    series.Append($"{t:F2}\t{first.transform.position.x - pp.x:F2}\t{(sb2 != null ? sb2.DebugGoblinPace : "-")}\t{pv:F1}\n");
                }
                prevP = pp;
                if (every > 0 && (frame++ % every) == 0) Shot($"bc_{tag}_{frame:00000}");
                yield return null;
                t += fps > 0 || fixDt > 0f ? stepDt : Time.deltaTime;
            }
            Time.captureDeltaTime = 0f;
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"bc_{tag}_series.tsv"), series.ToString());
            var gl = gobs.Values.ToList();
            int hits = gl.Sum(g => g.hits), overt = gl.Sum(g => g.overtakes), warps = gl.Sum(g => g.warps), cyc = gl.Sum(g => g.cycles), reachN = gl.Sum(g => g.reachEnter);
            float wait = gl.Sum(g => g.waits) > 0 ? gl.Sum(g => g.sumWait) / gl.Sum(g => g.waits) : -1f;
            float acc = gl.Count > 0 ? gl.Max(g => g.maxAcc) : 0f;
            L($"[bc] {tag}: goblins {gl.Count}, swings {swings}, hits {hits}, reach entries {reachN}, hit->away->hit cycles {cyc}, OVERTAKES {overt} (passed over/under in the air {gl.Sum(g => g.passedOver)}), forward warps {warps}, avg reach->hit {wait:F2}s, max goblin accel {acc:F1} m/s2 (0.1s windows, outside hits), speed {pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F0} km/h");
            Check(overt == 0, $"{tag}: the player does not overtake a running goblin ({overt})");
            Check(warps == 0, $"{tag}: goblins do not warp forward ({warps})");
            Check(hits >= 2 && reachN > 0 && (float)hits / reachN >= 0.6f, $"{tag}: entering reach gives time for a hit (hits {hits} / reach entries {reachN})");
            if (!BonusChaseLegacyArg) Check(cyc >= 2, $"{tag}: catch up -> hit -> pulls away -> catch up again repeats ({cyc})");
            PlayerController.DebugSpeedScale = 1f;
            GameManager.BlockExpGain = false;
            SetGod(false);
            yield return EndRun();
        }
    }

    static bool BonusChaseLegacyArg => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bonusChaseLegacy") >= 0;

    IEnumerator BcFlick()
    {
        pc.debugInjectFlick = PlayerController.FlickDirection.Forward;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
    }
}
#endif

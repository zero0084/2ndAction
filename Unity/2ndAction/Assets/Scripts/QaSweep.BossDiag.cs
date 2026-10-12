#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ボスへの命中不良の調査(2026-10-12)。闘技場でキャラ×ボス×距離を1戦ずつ、自動操作補助(常時)で戦わせ、0.5秒ごとに記録する。
//  -qaBossDiag <dir> -qaDiagChar dual_blade -qaDiagBoss C:Scorpion[,S:Titan,...] -qaDiagDist 57000 [-qaDiagSec 120] [-qaDiagFps 60] [-qaDiagNoBuild 1]
// 記録: ボスの動き(DebugAct)/間合い(Gap・FrontDist)/段階/無敵/プレイヤーの振り/HP の減り/補助の状態。
// 最後に「振った回数・命中(HP の減った回数)・撃破秒・被弾」をまとめる。
public partial class QaSweep
{
    static readonly string[] DiagDeckB = { "attack_up", "heart_up", "boss_killer", "shield", "attack_speed_up", "vampire", "sonic_blade", "second_wind", "double_attack", "phoenix", "iron_will", "speed_up" };

    IEnumerator BossDiagMode()
    {
        var snapSave = SaveSystem.Capture();
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        int fps = int.Parse(Arg("-qaDiagFps", "0"));
        if (fps > 0) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
        string ch = Arg("-qaDiagChar", "dual_blade");
        float dist = float.Parse(Arg("-qaDiagDist", "57000"), System.Globalization.CultureInfo.InvariantCulture);
        float secs = float.Parse(Arg("-qaDiagSec", "120"), System.Globalization.CultureInfo.InvariantCulture);
        int reps = int.Parse(Arg("-qaDiagReps", "1"));
        var summary = new List<string>();
        foreach (var spec in Arg("-qaDiagBoss", "C:Scorpion").Split(','))
            for (int rep = 0; rep < reps; rep++)
            {
                var parts = spec.Split(':');
                ArenaConfigReset();
                var c = ArenaMode.Config;
                c.character = ch; c.distance = dist; c.invincible = Arg("-qaDiagInv", "0") == "1"; c.devAllEnemies = true; c.assistMode = int.Parse(Arg("-qaDiagAssist", "2"));
                float kmh = float.Parse(Arg("-qaDiagKmh", "0"), System.Globalization.CultureInfo.InvariantCulture);
                c.speedMode = kmh > 0f ? 2 : 1; c.kmh = kmh;
                c.seedFixed = true; c.seed = 7101 + rep;
                c.build.Clear();
                if (Arg("-qaDiagNoBuild", "0") != "1") foreach (var cid in DiagDeckB) c.build.Add(new ArenaBuildEntry { key = cid, times = 9 });
                c.enemies.Clear();
                var kind = parts[0] == "S" ? ArenaEnemyKind.SkyBoss : parts[0] == "W" ? ArenaEnemyKind.WildBoss : ArenaEnemyKind.CaveBoss;
                int id = kind == ArenaEnemyKind.SkyBoss ? (int)System.Enum.Parse(typeof(SkyBossKind), parts[1]) : kind == ArenaEnemyKind.WildBoss ? (int)System.Enum.Parse(typeof(WildBossKind), parts[1]) : (int)System.Enum.Parse(typeof(CaveBossKind), parts[1]);
                c.enemies.Add(new ArenaEnemyEntry { kind = kind, bossKind = id, count = 1 });
                yield return ArenaLaunch($"DIAG {ch} {spec}");
                float w = 0f; while (SkyBossAlive() == null && w < 12f) { yield return null; w += Time.deltaTime; }
                var boss = SkyBossAlive();
                if (boss == null) { L($"[diag {ch} {spec}] boss did not appear"); continue; }
                var wb = boss as WildBossBase;
                // 再戦の強さの段(本番の 5km/1km 関門の再戦と同じ)。-qaDiagRT 0〜3。-qaDiagHpMul でボスの HP を本番並みに
                int rtIdx = int.Parse(Arg("-qaDiagRT", "-1"));
                if (wb != null && rtIdx >= 0 && rtIdx < BossRematchTuning.I.tiers.Count) wb.ApplyRematch(BossRematchTuning.I.tiers[rtIdx]);
                float hpMul = float.Parse(Arg("-qaDiagHpMul", "1"), System.Globalization.CultureInfo.InvariantCulture);
                if (wb != null && hpMul != 1f) wb.DebugScaleHp(hpMul);
                int hp0 = wb != null ? wb.Hp : 0, lastHp = hp0, drops = 0, sw0 = PlayerAttackInfo.SwingCount, lives0 = gm.Lives, hitsTaken = 0, lastLives = gm.Lives;
                float t0 = Time.time, next = 0f, firstDrop = -1f, lastDrop = -1f;
                var hsa = HighSpeedAssist.Instance;
                var acts = new Dictionary<string, float>();
                float fpsAcc = 0f; int fpsN = 0;
                // 判定がボスの被弾範囲に重なった振り(SwingId)ごとに、実際に当たった記録があるかを見る(物理の通知の取りこぼしの確認)
                var ovl = new Dictionary<int, (int frames, int fixedSteps, string name, float t)>();
                int ovlHit = 0, ovlMiss = 0, missLogged = 0; var missByName = new Dictionary<string, int>();
                var activeNow = new HashSet<int>();
                while (boss.DebugAlive && Time.time - t0 < secs && !gm.IsGameOver && gm.Lives > 0)
                {
                    float t = Time.time - t0;
                    if (wb != null)
                    {
                        if (wb.Hp < lastHp) { drops++; if (firstDrop < 0f) firstDrop = t; lastDrop = t; }
                        lastHp = wb.Hp;
                        acts.TryGetValue(wb.DebugAct, out float a); acts[wb.DebugAct] = a + Time.deltaTime;
                    }
                    if (wb != null && wb.DebugHurtCol != null)
                    {
                        activeNow.Clear();
                        var hc = wb.DebugHurtCol;
                        foreach (var a in PlayerAttackInfo.Active)
                        {
                            if (a == null || a.col == null || !a.col.enabled || !a.col.gameObject.activeInHierarchy || !a.col.CompareTag("PlayerAttack")) continue;
                            activeNow.Add(a.SwingId);
                            if (!hc.enabled || wb.DebugInvulnerable) continue;
                            if (!a.col.Distance(hc).isOverlapped) continue;
                            ovl.TryGetValue(a.SwingId, out var o);
                            ovl[a.SwingId] = (o.frames + 1, o.fixedSteps, a.name, o.frames == 0 ? Time.time - t0 : o.t);
                        }
                        foreach (var sid in ovl.Keys.ToList())
                        {
                            if (activeNow.Contains(sid)) continue;
                            var o = ovl[sid]; ovl.Remove(sid);
                            if (PlayerAttackInfo.WasHit(sid, wb)) ovlHit++;
                            else
                            {
                                ovlMiss++; missByName.TryGetValue(o.name, out int mc); missByName[o.name] = mc + 1;
                                if (missLogged++ < 12) L($"[diag {ch} {spec}] overlap-no-hit swing={sid} box={o.name} frames={o.frames} t={o.t:F2}");
                            }
                        }
                    }
                    if (gm.Lives < lastLives) hitsTaken++;
                    lastLives = gm.Lives;
                    fpsAcc += 1f / Mathf.Max(0.001f, Time.unscaledDeltaTime); fpsN++;
                    if (t >= next)
                    {
                        next = t + 0.5f;
                        string bstate = wb != null ? $"act={wb.DebugAct} pose={wb.DebugPose} gap={wb.DebugGap:F1} front={wb.DebugFrontDist:F1} relV={wb.DebugRelVelocity:F1} inv={wb.DebugInvulnerable} targetable={wb.AssistTargetable} y={wb.AssistBounds.center.y - pc.transform.position.y:F1} h={wb.AssistBounds.size.y:F1}" : "";
                        L($"[diag {ch} {spec}] t={t:F1} phase={boss.Phase} hp={(wb != null ? wb.Hp : 0)}/{hp0} drops={drops} swings={PlayerAttackInfo.SwingCount - sw0} lives={gm.Lives} {bstate} assist={(hsa != null ? hsa.CurrentStatus + "/" + hsa.LastAction : "-")} grounded={pc.IsGrounded}");
                    }
                    yield return null;
                }
                float tEnd = Time.time - t0;
                bool killed = !boss.DebugAlive;
                string actStr = string.Join(" ", acts.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}={kv.Value:F0}s"));
                string line = $"{ch} {spec} d={dist:F0} rep={rep} RT={(wb != null ? wb.DebugRT : -1)} killed={killed} sec={tEnd:F1} swings={PlayerAttackInfo.SwingCount - sw0} drops={drops} hitRate={(drops / Mathf.Max(1f, PlayerAttackInfo.SwingCount - sw0)):F2} firstDrop={firstDrop:F1} lastDrop={lastDrop:F1} hpLeft={(wb != null && hp0 > 0 ? (float)wb.Hp / hp0 : -1f):F2} taken={hitsTaken} fps={(fpsN > 0 ? fpsAcc / fpsN : 0f):F0} overlapSwings hit={ovlHit} miss={ovlMiss} [{string.Join(" ", missByName.Select(kv => kv.Key + "=" + kv.Value))}] acts[{actStr}]";
                L("[diag result] " + line);
                summary.Add(line);
                KillSkyBosses();
                yield return new WaitForSeconds(0.5f);
            }
        L("== diag summary ==");
        foreach (var s in summary) L(s);
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        RunCheckpoint.Reload();
    }
}
#endif

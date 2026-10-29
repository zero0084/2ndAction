#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 予兆の先読み(2026-10-29): -qaLead <dir> [-ldBosses Wild/Wolf,…] [-ldSecs 14] [-ldKmh 45,150]
// 同じボス・同じ乱数で、速さだけを変えて(TelegraphLead.DebugForceKmh)戦わせ、予兆ごとに
//  先読みの印が出た時刻 / 予兆(溜め)が始まった時刻 / 予兆が終わった(=攻撃の)時刻 を毎フレーム測る。
//  確かめること: 速さで変わるのは先読みの印の出始めだけ(溜めの長さ=攻撃の時刻は同じ)・先読みは上限以下・
//  BREAK/撃破で印が残らない・決めていない攻撃は出さない(印は必ず同じ範囲の予兆に切り替わるか、中断で消える)
public partial class QaSweep
{
    class LdRec { public float earlyAt = -1f, baseAt = -1f; }

    IEnumerator LeadMode()
    {
        Application.targetFrameRate = 60;
        string[] bosses = Arg("-ldBosses", "Wild/Wolf,Wild/BlackKnight,Wild/Cyclops,Cave/Scorpion,Cave/Troll,Sky/Fenrir,Sky/Titan").Split(',');
        float secs = float.Parse(Arg("-ldSecs", "14"), System.Globalization.CultureInfo.InvariantCulture);
        float[] speeds = Arg("-ldKmh", "45,150").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var tsv = new StringBuilder("boss\tkmh\tlead\tteles\twindupMed\twindupList\tearlyN\tearlyLeadMax\tearlyLeadMed\tcancelled\tleftAfterBreak\tleftAfterKill\n");
        var windups = new Dictionary<string, List<float>>();
        foreach (var grp in bosses.GroupBy(b => b.Split('/')[0]))
        {
            string stage = grp.Key == "Cave" ? "natural_cave" : grp.Key == "Sky" ? "sky_corridor" : "wasteland_road";
            foreach (var key in grp)
                foreach (float kmh in speeds)
                {
                    yield return BeginRun("swordsman", stage);
                    SetGod(true);
                    GameManager.BlockExpGain = true;
                    if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                    TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
                    TerrainManager.Instance.enemySpawnChance = 0f;
                    if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                    yield return new WaitForSeconds(1.0f);
                    TelegraphLead.DebugForceKmh = kmh;
                    float lead = TelegraphLead.Local;
                    if (!BaParse(key, out int fam, out int kind)) continue;
                    Random.InitState(20261029);
                    BossManager.Instance.DebugSpawnBossForTest(fam, kind);
                    BaBoss boss = null; float w = 0f;
                    while (w < 15f) { yield return null; w += Time.unscaledDeltaTime; boss = BaFind(); if (boss.W != null && !boss.W.IsEntering && w > 1.5f) break; }
                    if (boss == null || boss.W == null) { L($"[lead] {key}: no boss"); continue; }
                    boss.ScaleHp(60f);
                    int sh0 = TelegraphLead.EarlyShown, ca0 = TelegraphLead.EarlyCancelled;
                    var markers = boss.W.GetComponentsInChildren<BossTelegraphMarker>(true);
                    var rec = markers.ToDictionary(m => m, m => new LdRec());
                    var wl = new List<float>(); var leads = new List<float>();
                    float t = 0f;
                    while (t < secs && !boss.IsDead)
                    {
                        foreach (var m in markers)
                        {
                            if (m == null) continue;
                            var r = rec[m];
                            bool baseOn = m.GetComponent<SpriteRenderer>() != null && m.GetComponent<SpriteRenderer>().enabled;
                            if (m.EarlyShowing && r.earlyAt < 0f && r.baseAt < 0f) r.earlyAt = Time.time;
                            if (baseOn && r.baseAt < 0f) { r.baseAt = Time.time; if (r.earlyAt >= 0f) leads.Add(r.baseAt - r.earlyAt); }
                            if (!baseOn && r.baseAt >= 0f) { wl.Add(Time.time - r.baseAt); r.baseAt = -1f; r.earlyAt = -1f; }
                            if (!m.EarlyShowing && !baseOn && r.baseAt < 0f) r.earlyAt = -1f;
                        }
                        yield return null; t += Time.deltaTime;
                    }
                    // BREAK と撃破で印が残らないか: 先読みの印が出ている時に起こす(出なければ最後に)
                    int leftBreak = -1, leftKill = -1;
                    if (kmh > speeds.Min())
                    {
                        w = 0f; while (w < 8f && !markers.Any(m => m != null && m.EarlyShowing)) { yield return null; w += Time.deltaTime; }
                        boss.ForceBreak(); yield return null; yield return null;
                        leftBreak = markers.Count(m => m != null && m.EarlyShowing);
                        w = 0f; while (w < 6f && boss.Broken) { yield return null; w += Time.deltaTime; }
                        w = 0f; while (w < 8f && !markers.Any(m => m != null && m.EarlyShowing)) { yield return null; w += Time.deltaTime; }
                        boss.Kill(); yield return null; yield return null;
                        leftKill = markers.Count(m => m != null && m.EarlyShowing);
                    }
                    else if (boss.Valid && !boss.IsDead) boss.Kill();
                    string wkey = key;
                    if (!windups.ContainsKey(wkey)) windups[wkey] = wl;
                    float med = wl.Count > 0 ? wl.OrderBy(x => x).ElementAt(wl.Count / 2) : -1f;
                    float lmax = leads.Count > 0 ? leads.Max() : 0f, lmed = leads.Count > 0 ? leads.OrderBy(x => x).ElementAt(leads.Count / 2) : 0f;
                    L($"[lead] {key} @{kmh:F0}km/h lead {lead:F2}s: teles {wl.Count} windup med {med:F2}s [{string.Join(",", wl.Select(x => x.ToString("F2")))}] early {leads.Count} (lead max {lmax:F2} med {lmed:F2}) cancelled {TelegraphLead.EarlyCancelled - ca0} leftAfterBreak {leftBreak} leftAfterKill {leftKill}");
                    tsv.Append($"{key}\t{kmh:F0}\t{lead:F2}\t{wl.Count}\t{med:F2}\t{string.Join(",", wl.Select(x => x.ToString("F2")))}\t{leads.Count}\t{lmax:F2}\t{lmed:F2}\t{TelegraphLead.EarlyCancelled - ca0}\t{leftBreak}\t{leftKill}\n");
                    Check(lmax <= lead + 0.05f, $"lead {key} @{kmh:F0}: the early mark comes at most the lead before the telegraph ({lmax:F2}s > {lead:F2}s)");
                    if (lead <= 0.001f) Check(leads.Count == 0, $"lead {key} @{kmh:F0}: no early mark at normal speed ({leads.Count})");
                    if (leftBreak >= 0) Check(leftBreak == 0, $"lead {key}: no early mark is left after BREAK ({leftBreak})");
                    if (leftKill >= 0) Check(leftKill == 0, $"lead {key}: no early mark is left after the boss dies ({leftKill})");
                    if (kmh > speeds.Min() && windups.TryGetValue(wkey, out var w0) && w0 != wl && w0.Count > 0 && wl.Count > 0)
                    {
                        // 技の選び方(乱数の順番)は速さで変わりうるので、同じ技なら溜めの長さが同じか(=どの溜めの長さも、もう一方の速さにある値か)で見る
                        var odd = wl.Where(x => !w0.Any(y => Mathf.Abs(x - y) < 0.06f)).ToList();
                        Check(odd.Count == 0 || w0.Count < 3, $"lead {key}: the windups (attack timing) are the same at both speeds (new lengths {string.Join(",", odd.Select(x => x.ToString("F2")))})");
                    }
                    TelegraphLead.DebugForceKmh = -1f;
                    yield return BfWaitEncounterEnd();
                    SetGod(false); GameManager.BlockExpGain = false;
                    yield return EndRun();
                }
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "lead.tsv"), tsv.ToString());
    }
}
#endif

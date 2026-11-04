#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 序盤の小型雑魚の「何発で倒れるか」(2026-11-04): -qaOneShot <dir> [-osChars swordsman,dual_blade,fighter,archer,gunslinger,luka,pentatuttulu]
//   [-osStage wasteland_road] [-osKm 1,10,19,28,38,48] [-osSecs 40] [-hpCapOff 1 で上限なし(比較)]
//  カードなしの初期状態で、距離へワープして本物の Encounter をオートモード(自動攻撃)で -osSecs 秒倒し続ける(ボスは止める・無敵)。
//  敵ごとに: 何発目で倒れたか / 1発目の威力 / 1発目から撃破までの秒。小型(HP 倍率 ≤1.25)/中型/大型(≥1.75)に分けて oneshot.tsv へ
public partial class QaSweep
{
    class OsRec { public int maxHp, hits, firstDmg = -1; public float firstT, killT = -1f; public float hpMul; }

    IEnumerator OneShotMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] chars = Arg("-osChars", "swordsman,dual_blade,fighter,archer,gunslinger,luka,pentatuttulu").Split(',');
        string stage = Arg("-osStage", "wasteland_road");
        float[] kms = Arg("-osKm", "1,10,19,28,38,48").Split(',').Select(s => float.Parse(s, inv)).ToArray();
        float secs = float.Parse(Arg("-osSecs", "40"), inv);
        RoadEventTuning.DisabledByArg = true;
        var tsv = new System.Text.StringBuilder("char\tstage\tkm\tclass\tkilled\thits_avg\thits_max\tone_hit_pct\tfirst_dmg\thp_avg\tkill_s_avg\tatk\n");
        foreach (var ch in chars)
            foreach (float km in kms)
            {
                yield return BeginRun(ch, stage);
                typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
                if (Bm != null) Bm.enabled = false;
                AssistOn();
                GameManager.BlockExpGain = true; // レベルアップ(カード)で強くならない
                if (km > 0f) { WarpTo(km * 1000f); yield return new WaitForSeconds(1f); }
                var recs = new Dictionary<EnemyController, OsRec>();
                System.Action<EnemyController, PlayerAttackKind, bool, bool> onHit = (e, k, air, killed) =>
                {
                    if (e == null || e.GetComponent<BonusEnemy>() != null) return;
                    if (!recs.TryGetValue(e, out var r)) { r = new OsRec { maxHp = e.maxHp, firstT = Time.time, hpMul = e.HpClass }; recs[e] = r; }
                    if (r.killT >= 0f) return;
                    r.hits++;
                    if (r.firstDmg < 0) r.firstDmg = killed ? r.maxHp : r.maxHp - e.CurrentHpForUltimate;
                    if (killed) r.killT = Time.time;
                };
                EnemyController.LocalHit += onHit;
                float t = 0f;
                while (t < secs) { yield return null; t += Time.deltaTime; }
                EnemyController.LocalHit -= onHit;
                GameManager.BlockExpGain = false;
                foreach (var cls in new[] { "small", "medium", "large" })
                {
                    var ks = recs.Values.Where(r => r.killT >= 0f && Cls(r.hpMul) == cls).ToList();
                    if (ks.Count == 0) continue;
                    var dm = ks.Select(r => r.firstDmg).OrderBy(x => x).ToList();
                    string row = $"{ch}\t{stage}\t{km:F0}\t{cls}\t{ks.Count}\t{ks.Average(r => r.hits):F2}\t{ks.Max(r => r.hits)}\t{100f * ks.Count(r => r.hits == 1) / ks.Count:F0}\t{dm[dm.Count / 2]}\t{ks.Average(r => r.maxHp):F0}\t{ks.Average(r => r.killT - r.firstT):F2}\t{pc.EffectiveAttackPower}";
                    tsv.Append(row).Append('\n');
                    L($"[oneshot] {row}");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "oneshot.tsv"), tsv.ToString());
                yield return EndRun();
            }
        RoadEventTuning.DisabledByArg = false;
        Check(true, "oneshot: done");
    }

    string Cls(float hpMul) => hpMul >= DifficultyTuning.I.largeHpMultiplier ? "large" : hpMul > DifficultyTuning.I.smallCapMaxHpMul ? "medium" : "small";
}
#endif

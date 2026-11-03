#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 通常マップの難易度の比較(2026-11-03): -qaDiff <dir> [-dfStage wasteland_road] [-dfChar swordsman] [-dfBuild attack|defense|heal|element|recommended]
//   [-dfBands 0-20:0.4,20-50:23.6,...(帯の名前:始める km)] [-dfSecs 120] [-dfBlink 0.6(うっかりの頻度、0 = オートのまま)] [-diffLegacy 1 で旧仕様]
//  距離の帯ごとに新しいランで帯の始めへワープし、その距離までに取っていそうな数のカード(ビルドの種類)を持たせて、
//  オートモード(自動攻撃/自動回避、0km/h から。-dfBlink で時々操作しない「うっかり」)で -dfSecs 秒走る。ボス・BONUS・Encounter は本物のまま。
//  HP が尽きたら満タンへ戻して「倒れた回数」に数える(帯の最後まで走る)。結果は diff.tsv(1行 = 1つの帯)
public partial class QaSweep
{
    static readonly Dictionary<string, string[]> DfBuilds = new Dictionary<string, string[]>
    {
        { "attack",      new[] { "attack_up", "attack_speed_up", "attack_range_up", "air_attack_up", "mob_killer", "double_attack" } },
        { "defense",     new[] { "attack_up", "heart_up", "shield", "heavy_armor", "iron_will", "attack_speed_up" } },
        { "heal",        new[] { "attack_up", "vampire", "blood_blade", "heart_up", "second_wind", "attack_speed_up" } },
        { "element",     new[] { "attack_up", "flame_blade", "frost_edge", "glacier_guard", "chain_lightning", "blood_rush" } },
        { "recommended", new[] { "attack_up", "attack_speed_up", "heart_up", "shield", "vampire", "air_attack_up" } },
    };

    int dfHits, dfDeaths, dfDamage; readonly Dictionary<string, int> dfByKind = new Dictionary<string, int>();

    IEnumerator DiffMode()
    {
        Application.targetFrameRate = 60;
        string stage = Arg("-dfStage", "wasteland_road"), ch = Arg("-dfChar", "swordsman"), build = Arg("-dfBuild", "recommended");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float secs = float.Parse(Arg("-dfSecs", "120"), inv);
        // 帯の名前:始める距離(km)。ボスの関門(1km/5km/10km)を1つ以上通るように、関門の少し手前から始める
        var bands = Arg("-dfBands", "0-20:0.4,20-50:23.6,50-70:63.6,70-90:73.6,90-100:88.6").Split(',')
            .Select(s => { var p = s.Split(':'); return (label: p[0], km: float.Parse(p.Length > 1 ? p[1] : p[0], inv)); }).ToArray();
        // うっかり: 1秒あたりこの確率で 0.5 秒だけオートが操作を出さない(0 = オートそのまま。被弾がほぼ 0 になる)
        HighSpeedAssist.QaBlinkPerSec = float.Parse(Arg("-dfBlink", "0.6"), inv);
        int speedPct = int.Parse(Arg("-dfSpeed", "100")); // 設定「最高速度」(テスト用データに保存される)
        GameSettings.SetMaxSpeedPercent(speedPct);
        var tsv = new System.Text.StringBuilder("legacy\tbuild\tblink\tspeed\tband\tstart_km\tsecs\tkm_run\tcards\tlv\tmaxHp\tspawned\tkilled\tavg_onscreen\tidle_pct\tkill_s_avg\tkill_s_p90\thits\tdamage\tdeaths\thp_min_pct\thp_end_pct\tbosses\tboss_s_avg\tbreaks\tkinds\n");
        GameManager.QaDamageEvent = OnDfDamage;
        bool legacy = DifficultyTuning.I != null && DifficultyTuning.LegacyOverride; // I を読むと -diffLegacy が読まれる
        foreach (var band in bands)
        {
            float km = band.km;
            yield return BeginRun(ch, stage);
            stopKeepAlive = true; yield return null; // HP を測るので 990 へ戻す見張りは止める
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            var a = HighSpeedAssist.Instance;
            if (a != null) { a.SetEnabled(true); a.SetAutoAttack(true); a.SetAutoAvoid(true); a.SetAutoInBoss(true); a.engageKmh = 0f; a.releaseKmh = -1f; a.fullAssistKmh = 1f; }
            // その距離までのカード(Lv の数 ≒ 4 + km/4、各カード Lv9 まで)
            int picks = Mathf.RoundToInt(4 + km / 4f);
            var ids = DfBuilds.TryGetValue(build, out var b) ? b : DfBuilds["recommended"];
            var lv = new Dictionary<string, int>();
            for (int p = 0; p < picks; p++) { string id = ids[p % ids.Length]; lv[id] = Mathf.Min(9, (lv.TryGetValue(id, out int v) ? v : 0) + 1); }
            foreach (var kv in lv) { var c = CardDatabase.FindBaseById(kv.Key); if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(kv.Value, GameManager.MaxRunCardLevel)); }
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
            if (km > 0f) { WarpTo(km * 1000f); yield return new WaitForSeconds(1f); }
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
            float d0 = gm.MaxDistance; int kill0 = gm.EnemyKillCount, spawn0 = EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies : 0, up0 = gm.UpgradeCount;
            dfHits = dfDeaths = dfDamage = 0; dfByKind.Clear();
            float t = 0f, sampleT = 0f; int samples = 0, idle = 0; float onscreenSum = 0f, hpMin = 1f;
            var firstSeen = new Dictionary<EnemyController, float>(); var killTimes = new List<float>();
            System.Action<EnemyController, PlayerAttackKind, bool, bool> onHit = (e, k, air, killed) => { if (killed && e != null && firstSeen.TryGetValue(e, out float f0)) killTimes.Add(Time.time - f0); };
            EnemyController.LocalHit += onHit;
            int bosses = 0; float bossStart = -1f, bossSum = 0f; int breaks = 0; bool wasBoss = false;
            var bossBreak = new Dictionary<Object, int>();
            var cam = Camera.main;
            while (t < secs && !gm.IsGameOver)
            {
                yield return null; t += Time.deltaTime; sampleT -= Time.deltaTime;
                bool boss = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
                if (boss && !wasBoss) { bosses++; bossStart = t; }
                if (!boss && wasBoss && bossStart >= 0f) bossSum += t - bossStart;
                wasBoss = boss;
                if (sampleT <= 0f)
                {
                    sampleT = 0.25f; samples++;
                    cam = Camera.main;
                    int on = 0;
                    foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    {
                        if (e == null || !e.isActiveAndEnabled || cam == null) continue;
                        var vp = cam.WorldToViewportPoint(e.transform.position);
                        if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                        on++; if (!firstSeen.ContainsKey(e)) firstSeen[e] = Time.time;
                    }
                    onscreenSum += on; if (on == 0 && !boss) idle++;
                    hpMin = Mathf.Min(hpMin, gm.Lives / (float)Mathf.Max(1, gm.MaxLives));
                    foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        if (mb is IBossBattleDebug bd) bossBreak[mb] = Mathf.Max(bossBreak.TryGetValue(mb, out int bb) ? bb : 0, bd.BreakCount);
                }
            }
            if (wasBoss && bossStart >= 0f) bossSum += t - bossStart;
            EnemyController.LocalHit -= onHit;
            breaks = bossBreak.Values.Sum();
            killTimes.Sort();
            float ks = killTimes.Count > 0 ? killTimes.Average() : -1f, k90 = killTimes.Count > 0 ? killTimes[Mathf.Min(killTimes.Count - 1, (int)(killTimes.Count * 0.9f))] : -1f;
            string kinds = string.Join(" ", dfByKind.OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value}"));
            string row = $"{(legacy ? 1 : 0)}\t{build}\t{HighSpeedAssist.QaBlinkPerSec:F1}\t{GameSettings.MaxSpeedPercent}\t{band.label}\t{km:F1}\t{t:F0}\t{(gm.MaxDistance - d0) / 1000f:F2}\t{gm.UpgradeCount - up0}+{picks}\t{gm.Level}\t{gm.MaxLives}\t{(EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies - spawn0 : -1)}\t{gm.EnemyKillCount - kill0}" +
                $"\t{onscreenSum / Mathf.Max(1, samples):F2}\t{100f * idle / Mathf.Max(1, samples):F0}\t{ks:F2}\t{k90:F2}\t{dfHits}\t{dfDamage}\t{dfDeaths}\t{hpMin * 100f:F0}\t{100f * gm.Lives / Mathf.Max(1, gm.MaxLives):F0}\t{bosses}\t{(bosses > 0 ? bossSum / bosses : 0f):F1}\t{breaks}\t{kinds}";
            tsv.Append(row).Append('\n');
            L($"[diff] {row}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "diff.tsv"), tsv.ToString());
            yield return EndRun();
        }
        GameManager.QaDamageEvent = null; HighSpeedAssist.QaBlinkPerSec = 0f;
        Check(true, "diff: done");
    }

    void OnDfDamage(string reason, int dmg, int before, int after)
    {
        dfHits++; dfDamage += dmg;
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        k = k.Contains(":") ? k.Substring(0, k.IndexOf(':')) : k;
        k = $"{k}={dmg / 10}";
        dfByKind[k] = (dfByKind.TryGetValue(k, out int v) ? v : 0) + 1;
        if (after <= 0) { dfDeaths++; typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives }); }
    }
}
#endif

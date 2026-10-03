#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// カードバランス調査(2026-10-02): カードなしで各ボスを最終的に倒せるか / 実測の火力。 -qaBossKill <dir>
//   各キャラ × 各ステージ × 各関門: 毎回ランを新しく始め(カードを一切持ち込まない)、関門の60m手前へワープ → 本来のボスが出る →
//   ボット(近いボスへ向かって攻撃、上にいれば上、後ろなら後ろ)が倒すまで / 制限時間まで戦う。経験値は止める(レベルアップしない)、
//   報酬のカードは選ばない(次の戦いは新しいラン)。プレイヤーは倒れないようにHPを戻す(被弾はする)。
//   引数: -qaBkChars a,b  -qaBkStages wasteland_road,natural_cave,sky_corridor  -qaBkGates 1000,5000,10000,...  -qaBkTimeout 300(ゲーム内秒)  -qaBkTs 3(時間倍率)
//         -qaBkCards attack_up:9,...(火力の実測用: 開始時にこのカードをLvぶん適用)  -qaBkHp 99999(ボスHPを固定して -qaBkTimeout 秒の実測DPS)
//   出力: bosskill.tsv(1行=1戦)
public partial class QaSweep
{
    IEnumerator BossKillMode()
    {
        Application.targetFrameRate = 60;
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        BossHpPlan.Hits = 0; // 再設計案(開発用)は使わない: 現行のボスHP
        string[] chars = Arg("-qaBkChars", string.Join(",", CharacterDatabase.AllCharacters.Select(c => c.characterId))).Split(',');
        string[] stages = Arg("-qaBkStages", "wasteland_road,natural_cave,sky_corridor").Split(',');
        float[] gates = Arg("-qaBkGates", "1000,5000,10000,20000,30000,40000,50000,60000,70000,80000,90000").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        float timeout = float.Parse(Arg("-qaBkTimeout", "300"), System.Globalization.CultureInfo.InvariantCulture);
        float ts = float.Parse(Arg("-qaBkTs", "3"), System.Globalization.CultureInfo.InvariantCulture);
        string cards = Arg("-qaBkCards", "");
        int hpOverride = int.Parse(Arg("-qaBkHp", "0"));
        var tsv = new StringBuilder("char\tstage\tgate\tbosses\tcount\thpTotal\tkilled\ttimeGame\thits\tdmgAvg\tdmgMin\tdmgMax\tremainPct\tdps\tphases\tresumed\tbreaks\tnote\n");
        string tsvPath = System.IO.Path.Combine(outDir, "bosskill.tsv");
        L($"[bosskill] chars={chars.Length} stages={stages.Length} gates={gates.Length} timeout={timeout}s ts={ts} cards='{cards}' hpOverride={hpOverride}");

        foreach (string ch in chars)
            foreach (string st in stages)
                foreach (float gate in gates)
                {
                    yield return BeginRun(ch, st);
                    GameManager.BlockExpGain = true;
                    ApplyBuildForTest(cards);
                    BossManager.NetTestBossHpOverride = hpOverride;
                    WarpTo(gate - 60f);
                    TimeControl.SetDebugTimeScale(ts);
                    float w = 0f;
                    float nudge = 0f; int nudges = 0;
                    while (Bm.AliveBossCount <= 0 && w < 30f)
                    {
                        // 関門までの障害物は普通のプレイヤーと同じく壊す/跳ぶ(ボットは普段ボスにしか攻撃しないため、スタート地点の壁で止まることがあった)
                        nudge -= Time.unscaledDeltaTime;
                        if (nudge <= 0f) { nudge = 0.25f; StartCoroutine(BkFlick(nudges++ % 4 == 3 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward)); }
                        yield return null; w += Time.unscaledDeltaTime;
                    }
                    if (Bm.AliveBossCount <= 0)
                    {
                        L($"[bosskill] {ch} {st} {gate:F0}: NO BOSS appeared | d={gm.MaxDistance:F1} nextBoss={Bm.NextBossDistance:F0} bossPhase={Bm.IsBossPhase} kmh={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F1} autoRun={pc.autoRunEnabled} reacting={pc.IsReacting} x={pc.transform.position.x:F1} y={pc.transform.position.y:F1} grounded={pc.IsGrounded} over={gm.IsGameOver} ts={Time.timeScale:F2} pause=[{TimeControl.DescribeActiveReasons()}] bonus={(BonusZone.Instance != null ? BonusZone.Instance.State.ToString() : "-")} choice={gm.IsLocalChoiceOpen}");
                        tsv.Append($"{ch}\t{st}\t{gate:F0}\t-\t0\t0\tno-boss\t0\t0\t0\t0\t0\t0\t0\t0\tFalse\t0\tno boss\n");
                        TimeControl.SetDebugTimeScale(1f); BossManager.NetTestBossHpOverride = 0;
                        yield return EndRun(); continue;
                    }
                    // 比較用(2026-10-03): -qaBkLegacyBoss 1 で、タイタン/魔人の「近接の反撃の時間」を止めた以前の動きにする
                    if (Arg("-qaBkLegacyBoss", "0") == "1")
                    {
                        foreach (var titan in FindObjectsByType<SkyTitanBoss>(FindObjectsSortMode.None)) titan.kneelEveryAttacks = 0;
                        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) m.exposeEveryAttacks = 0;
                    }
                    // 登場の演出の間も含めて計る(ゲーム内時間)
                    string names =string.Join("+", BossNames().GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key}x{g.Count()}" : g.Key));
                    int count = Bm.AliveBossCount;
                    int hp0 = TotalBossHp(), lastHp = hp0, hits = 0, dmgMin = int.MaxValue, dmgMax = 0, totalDmg = 0, maxPhase = 1, breaks = 0;
                    float t = 0f, lastFlick = -9f; bool wasBroken = false;
                    while (t < timeout && !gm.IsGameOver)
                    {
                        if (Bm.AliveBossCount <= 0 && !Bm.IsBossPhase) break;
                        if (Bm.AliveBossCount <= 0 && t > 3f) break; // 全部倒した(報酬の演出中)
                        int hp = TotalBossHp();
                        if (hp < lastHp) { int d = lastHp - hp; hits++; totalDmg += d; dmgMin = Mathf.Min(dmgMin, d); dmgMax = Mathf.Max(dmgMax, d); }
                        else if (hp > lastHp) hp0 += hp - lastHp; // 復活(フェニックス)/後から出たボス
                        lastHp = hp;
                        foreach (var b in WildAlive()) { maxPhase = Mathf.Max(maxPhase, b.Phase); if (b.Broken && !wasBroken) breaks++; wasBroken = b.Broken; }
                        if (t - lastFlick > 0.12f) { lastFlick = t; BotFlickAtBoss(); }
                        yield return null;
                        t += Time.deltaTime;
                    }
                    bool killed = Bm.AliveBossCount <= 0;
                    int remain = killed ? 0 : TotalBossHp();
                    float pct = hp0 > 0 ? 100f * remain / hp0 : 0f;
                    float dps = t > 0.01f ? totalDmg / t : 0f;
                    string note = killed ? "" : hits == 0 ? "NO DAMAGE" : $"projected {(dps > 0.01f ? remain / dps : -1):F0}s more";
                    L($"[bosskill] {ch,-13} {st,-14} {gate,6:F0} {names,-26} x{count} hp {hp0,6} -> {(killed ? "KILLED" : "alive")} in {t,5:F0}s hits {hits,4} dmg avg {(hits > 0 ? totalDmg / hits : 0),4} min {(hits > 0 ? dmgMin : 0)} max {dmgMax} dps {dps,5:F1} remain {pct:F0}% phase {maxPhase} resumed {Bm.RunResumed} breaks {breaks} {note}");
                    tsv.Append($"{ch}\t{st}\t{gate:F0}\t{names}\t{count}\t{hp0}\t{killed}\t{t:F1}\t{hits}\t{(hits > 0 ? totalDmg / hits : 0)}\t{(hits > 0 ? dmgMin : 0)}\t{dmgMax}\t{pct:F1}\t{dps:F1}\t{maxPhase}\t{Bm.RunResumed}\t{breaks}\t{note}\n");
                    System.IO.File.WriteAllText(tsvPath, tsv.ToString());
                    TimeControl.SetDebugTimeScale(1f);
                    BossManager.NetTestBossHpOverride = 0;
                    GameManager.BlockExpGain = false;
                    yield return EndRun();
                }
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        L("[bosskill] test machine save restored");
    }

    // 火力の実測用: "id:lv,id:lv" を開始時に適用(ラン中の取得と同じ処理。保存はしない)
    void ApplyBuildForTest(string cards)
    {
        if (string.IsNullOrEmpty(cards)) return;
        foreach (var part in cards.Split(','))
        {
            var kv = part.Split(':');
            var c = CardDatabase.FindById(kv[0]);
            if (c == null) { L($"[bosskill] unknown card {kv[0]}"); continue; }
            int lv = kv.Length > 1 ? int.Parse(kv[1]) : 1;
            gm.ApplyCardEffectsStacked(c, lv);
        }
        // カードでHPの上限が変わったら満タンへ(満HP条件を成立させる)
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
    }

    IEnumerable<string> BossNames()
    {
        foreach (var b in WildAlive()) yield return b.bossName;
        foreach (var d in DragonsAlive()) yield return "Dragon";
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) yield return "Majin";
    }

    int TotalBossHp()
    {
        int s = 0;
        foreach (var b in WildAlive()) s += Mathf.Max(0, b.Hp);
        foreach (var d in DragonsAlive()) s += Mathf.Max(0, d.Hp);
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) s += Mathf.Max(0, m.Hp);
        return s;
    }

    // 近いボスへ: 上にいれば上、後ろなら後ろ、それ以外は前へ攻撃。
    // 2026-10-03: 狙う点はボスの中心ではなく「被弾範囲のプレイヤーに一番近い点」(人が狙う所。タイタンのように中心が高いボスで
    // 以前は上攻撃ばかりになっていた)
    void BotFlickAtBoss()
    {
        Vector3? best = null; float bd = float.MaxValue;
        Vector3 p = pc.transform.position;
        Vector3 chest = p + Vector3.up * 0.9f;
        Vector3 Near(Collider2D c, Vector3 fallback) => c != null && c.enabled ? (Vector3)c.bounds.ClosestPoint(chest) : fallback;
        foreach (var b in WildAlive())
        {
            var hb = b.GetComponentInChildren<BossHurtbox>();
            Vector3 t = Near(hb != null ? hb.GetComponent<Collider2D>() : null, b.CenterWorld);
            float d = (t - p).sqrMagnitude; if (d < bd) { bd = d; best = t; }
        }
        foreach (var d in DragonsAlive()) { Vector3 t = Near(d.GetComponent<Collider2D>(), d.transform.position); float dd = (t - p).sqrMagnitude; if (dd < bd) { bd = dd; best = t; } }
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) { Vector3 t = Near(m.GetComponent<Collider2D>(), m.transform.position); float dd = (t - p).sqrMagnitude; if (dd < bd) { bd = dd; best = t; } }
        if (!best.HasValue) return;
        float dx = best.Value.x - p.x, dy = best.Value.y - p.y;
        var f = dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
        StartCoroutine(BkFlick(f));
    }

    IEnumerator BkFlick(PlayerController.FlickDirection f)
    {
        pc.debugInjectFlick = f;
        yield return null; yield return null;
        if (pc != null) pc.debugInjectFlick = null;
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// バランス調査(部分試験、通しの成功には数えない): -qaBalHits <dir>
//   [-qaBalChars swordsman,...] [-qaBalMaps wasteland_road,...] [-qaBalDists 80000,90000,100000] [-qaBalSecs 40]
//   [-qaBalDeck attack_up,heart_up,...(既定 = バランス型の12枚)] [-qaBalLvls 0,9(デッキの全能力に入れるラン中の Lv。0 = カードなし)]
//   [-qaBalOnly attack_up(この1枚だけに Lv を入れる。2026-10-10 の黒剣士の測定と同じ形)]
// 各キャラで各距離へワープして走り、出てきた雑魚ごとに 最大HP・1回の命中の実ダメージ・撃破までの命中回数と時間 を記録。
// 無敵(テスト用。ダメージには影響しない)・経験値停止(レベルアップでカードが混ざらない)・テスト用データ。オートの攻撃(高速時の補助)が攻撃する。
// → bal_hits.tsv(雑魚1体 = 1行)/ bal_summary.tsv(キャラ×マップ×距離×Lv の集計)
public partial class QaSweep
{
    class BalTrack { public string name; public int maxHp; public int lastHp; public int hits; public float firstHit = -1f, death = -1f; public List<int> dmg = new List<int>(); public float hpMul; }

    static readonly string[] BalDefaultDeck = { "attack_up", "heart_up", "boss_killer", "shield", "attack_speed_up", "vampire", "sonic_blade", "second_wind", "double_attack", "phoenix", "iron_will", "speed_up" };

    IEnumerator BalHitsModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        bool unlock0 = UnlockRules.DevUnlockAll;
        UnlockRules.DevUnlockAll = true;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] chars = Arg("-qaBalChars", "swordsman").Split(',');
        string[] maps = Arg("-qaBalMaps", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        float secs = float.Parse(Arg("-qaBalSecs", "40"), inv);
        float[] dists = Arg("-qaBalDists", "80000,90000,100000").Split(',').Select(x => float.Parse(x, inv)).ToArray();
        string[] deck = Arg("-qaBalDeck", string.Join(",", BalDefaultDeck)).Split(',');
        int[] lvls = Arg("-qaBalLvls", "0,9").Split(',').Select(int.Parse).ToArray();
        string only = Arg("-qaBalOnly", "");
        var tsv = new StringBuilder("char\tmap\tdist\tlv\teffAtk\tcurEnemyHp\tenemy\tclass\tspeciesMul\tmaxHp\thits\tavgDmg\testHitsToKill\tdmgPerHit\tsecsToKill\tkilled\n");
        var sum = new StringBuilder("char\tmap\tdist\tlv\teffAtk\tbaseHp\tclass\tenemies\thitEnemies\tkilled\tmedianEstHits\tmedianSecsToKill\tavgDmg\n");
        foreach (var ch in chars)
            foreach (var map in maps)
                foreach (float d in dists)
                    foreach (int lv in lvls)
                    {
                        yield return BeginRun(ch, map);
                        if (!gm.InvincibleMode) gm.DebugToggleInvincible();
                        GameManager.BlockExpGain = true; // レベルアップで他のカードが混ざらない
                        if (lv > 0)
                        {
                            foreach (var id in (only != "" ? new[] { only } : deck).Distinct())
                            {
                                var c = CardDatabase.FindBaseById(id);
                                if (c != null) gm.DebugApplyRunCard(c, Mathf.Min(lv, GameManager.MaxRunCardLevel));
                            }
                            gm.RecomputeCardStats();
                        }
                        WarpTo(d); RunLedger.DevClearDebug();
                        yield return new WaitForSecondsRealtime(2f);
                        var dtm = DistanceTierManager.Instance;
                        int curHp = dtm != null ? dtm.CurrentEnemyHp : -1;
                        int effAtk = pc != null ? pc.EffectiveAttackPower : -1;
                        var tracks = new Dictionary<EnemyController, BalTrack>();
                        float t = 0f;
                        while (t < secs && gm.HasStarted && !gm.IsGameOver)
                        {
                            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                            {
                                if (e == null || !e.isActiveAndEnabled || e.GetComponent<BonusEnemy>() != null) continue;
                                int hp = e.CurrentHpForUltimate;
                                if (!tracks.TryGetValue(e, out var tr))
                                {
                                    var sr = e.GetComponentInChildren<SpriteRenderer>();
                                    string sp = sr != null && sr.sprite != null ? sr.sprite.name : "?";
                                    tr = new BalTrack { name = sp, maxHp = e.maxHp, lastHp = hp, hpMul = curHp > 0 ? e.maxHp / (float)curHp : 0f };
                                    tracks[e] = tr;
                                }
                                if (hp < tr.lastHp) { tr.hits++; tr.dmg.Add(tr.lastHp - hp); if (tr.firstHit < 0f) tr.firstHit = t; tr.lastHp = hp; }
                                if ((hp <= 0 || e.IsDying) && tr.death < 0f && tr.hits > 0) tr.death = t;
                            }
                            // 撃破と同じフレームで非表示になる敵: 消えた後に 撃破中の印/HP で数える(壊されて null の物は数えない)
                            foreach (var kv in tracks)
                            {
                                var e2 = kv.Key; var tr2 = kv.Value;
                                if (tr2.death >= 0f || e2 == null || e2.isActiveAndEnabled) continue;
                                int hp2 = e2.CurrentHpForUltimate;
                                if (e2.IsDying || hp2 <= 0) { if (hp2 < tr2.lastHp) { tr2.hits++; tr2.dmg.Add(tr2.lastHp - Mathf.Max(0, hp2)); tr2.lastHp = hp2; } if (tr2.firstHit < 0f) tr2.firstHit = t; tr2.death = t; }
                            }
                            yield return null; t += Time.deltaTime;
                        }
                        // 普通 = 種族の倍率 1.2 以下 / 耐久型 = それより上(重装/番兵/ワーム等)。精鋭(ELITE)は HP がさらに倍になるので耐久型に入る
                        string Cls(BalTrack x) => x.hpMul <= 1.21f ? "normal" : "tough";
                        int n = 0;
                        foreach (var tr in tracks.Values)
                        {
                            if (tr.hits == 0) continue;
                            n++;
                            bool killed = tr.death >= 0f;
                            float ttk = killed && tr.firstHit >= 0f ? tr.death - tr.firstHit : -1f;
                            // 1回のダメージ: 撃破の一撃(残りHPで切れる)を除いた平均(無ければ全部)
                            var dl = tr.dmg.Count > 1 && killed ? tr.dmg.Take(tr.dmg.Count - 1).ToList() : tr.dmg;
                            float avg = dl.Count > 0 ? (float)dl.Average() : 0f;
                            int est = avg > 0f ? Mathf.CeilToInt(tr.maxHp / avg) : -1;
                            tsv.Append($"{ch}\t{map}\t{d}\t{lv}\t{effAtk}\t{curHp}\t{tr.name}\t{Cls(tr)}\t{tr.hpMul:F2}\t{tr.maxHp}\t{tr.hits}\t{avg:F0}\t{est}\t{string.Join("/", tr.dmg.Take(12))}\t{ttk:F2}\t{killed}\n");
                        }
                        foreach (var g in tracks.Values.Where(x => x.hits > 0).GroupBy(Cls))
                        {
                            var list = g.ToList();
                            var ests = list.Select(x => { var dl = x.dmg.Count > 1 && x.death >= 0f ? x.dmg.Take(x.dmg.Count - 1) : x.dmg; double a = dl.Any() ? dl.Average() : 0; return a > 0 ? Mathf.CeilToInt((float)(x.maxHp / a)) : 999; }).OrderBy(x => x).ToList();
                            var ttks = list.Where(x => x.death >= 0f && x.firstHit >= 0f).Select(x => x.death - x.firstHit).OrderBy(x => x).ToList();
                            double ad = list.SelectMany(x => x.dmg).DefaultIfEmpty(0).Average();
                            sum.Append($"{ch}\t{map}\t{d}\t{lv}\t{effAtk}\t{curHp}\t{g.Key}\t{tracks.Values.Count(x => Cls(x) == g.Key)}\t{list.Count}\t{list.Count(x => x.death >= 0f)}\t{(ests.Count > 0 ? ests[ests.Count / 2] : -1)}\t{(ttks.Count > 0 ? ttks[ttks.Count / 2].ToString("F2", inv) : "-")}\t{ad:F0}\n");
                        }
                        L($"[{ch} {map} {d:F0} Lv{lv}] effAtk {effAtk} enemyHp(base) {curHp} tracked {tracks.Count} hit {n} killed {tracks.Values.Count(x => x.death >= 0f)} enemyHpMul {gm.EnemyHpMultiplier:F2}");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bal_hits.tsv"), tsv.ToString());
                        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bal_summary.tsv"), sum.ToString());
                        yield return EndRun();
                    }
        if (gm != null && gm.InvincibleMode) gm.DebugToggleInvincible();
        UnlockRules.DevUnlockAll = unlock0;
        GameManager.BlockExpGain = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

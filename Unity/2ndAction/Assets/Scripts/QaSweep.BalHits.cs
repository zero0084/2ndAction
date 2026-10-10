#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// バランス調査(2026-10-10、値は変えない): -qaBalHits <dir> [-qaBalMaps wasteland_road,natural_cave,...] [-qaBalSecs 40]
// 黒剣士で 8万/9万/10万m へ。ATTACK UP なし / Lv9 の2通り。出てきた雑魚ごとに 最大HP・1回の命中の実ダメージ・撃破までの命中回数と時間 を記録。
// 無敵(テスト用)・テスト用データ。オートの攻撃(高速時の補助)が攻撃する。 → bal_hits.tsv
public partial class QaSweep
{
    class BalTrack { public string name; public int maxHp; public int lastHp; public int hits; public float firstHit = -1f, death = -1f; public List<int> dmg = new List<int>(); public float hpMul; }

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
        string[] maps = Arg("-qaBalMaps", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        float secs = float.Parse(Arg("-qaBalSecs", "40"));
        float[] dists = { 80000f, 90000f, 100000f };
        var tsv = new StringBuilder("map\tdist\tattackUpLv\teffAtk\tcurEnemyHp\tenemy\tspeciesMul\tmaxHp\thits\tdmgPerHit\tsecsToKill\tkilled\n");
        foreach (var map in maps)
            foreach (float d in dists)
                for (int lvSel = 0; lvSel < 2; lvSel++)
                {
                    int lv = lvSel == 0 ? 0 : 9;
                    yield return BeginRun("swordsman", map);
                    if (!gm.InvincibleMode) gm.DebugToggleInvincible();
                    GameManager.BlockExpGain = true; // レベルアップで他のカードが混ざらない
                    if (lv > 0) { var c = CardDatabase.FindBaseById("attack_up"); if (c != null) gm.DebugApplyRunCard(c, lv); gm.RecomputeCardStats(); }
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
                            if (e == null || !e.isActiveAndEnabled) continue;
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
                        // 消えた(撃破)敵
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
                    int n = 0;
                    foreach (var tr in tracks.Values)
                    {
                        if (tr.hits == 0) continue;
                        n++;
                        bool killed = tr.death >= 0f;
                        float ttk = killed && tr.firstHit >= 0f ? tr.death - tr.firstHit : -1f;
                        tsv.Append($"{map}\t{d}\t{lv}\t{effAtk}\t{curHp}\t{tr.name}\t{tr.hpMul:F2}\t{tr.maxHp}\t{tr.hits}\t{string.Join("/", tr.dmg.Take(12))}\t{ttk:F2}\t{killed}\n");
                    }
                    L($"[{map} {d:F0} ATK+Lv{lv}] effAtk {effAtk} enemyHp(base) {curHp} tracked {tracks.Count} hit {n} killed {tracks.Values.Count(x => x.death >= 0f)} enemyHpMul {gm.EnemyHpMultiplier:F2}");
                    yield return EndRun();
                }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bal_hits.tsv"), tsv.ToString());
        if (gm != null && gm.InvincibleMode) gm.DebugToggleInvincible();
        UnlockRules.DevUnlockAll = unlock0;
        GameManager.BlockExpGain = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

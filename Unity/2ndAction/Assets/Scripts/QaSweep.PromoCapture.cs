#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// X 投稿用の宣伝動画の撮影(2026-11-08): QaSweep -storeCapture の場面を足す(撮影用ビルドだけ。ゲームの仕様は変えない)
//  pv_final_wild / pv_final_cave / pv_final_sky : マップ専用ラスボス(ガルムード/ノクタリス/セラフィエル)との戦い
//     実際のボスの出し方(DebugSpawnBossForTest)→ 必殺技(ボス自身の技)→ BREAK(崩しのゲージを満たす時刻だけ決める)→ 撃破の演出
//  pv_trio_<stage> : 95km からの3体同時の組(実際の終盤の流れ。94.85km へワープして、自然に始まるのを撮る)
//  プレイヤーは撮影用に倒れない(SetGod)、補助(製品にもある自動のジャンプ/攻撃、ボス戦も)を ON
public partial class QaSweep
{
    IEnumerator PvShot(string s) => s switch
    {
        "pv_final_wild" => PvFinalShot(s, Arg("-pvCharW", "swordsman"), "wasteland_road", 0, (int)WildBossKind.Garmud, 12150f),
        "pv_final_cave" => PvFinalShot(s, Arg("-pvCharC", "dual_blade"), "natural_cave", 1, (int)CaveBossKind.Noctaris, 16150f),
        "pv_final_sky" => PvFinalShot(s, Arg("-pvCharS", "miko"), "sky_corridor", 2, (int)SkyBossKind.Seraphiel, 16150f),
        "pv_trio_wild" => PvTrioShot(s, Arg("-pvCharT", "dragon_lancer"), "wasteland_road"),
        "pv_trio_cave" => PvTrioShot(s, Arg("-pvCharT", "ninja"), "natural_cave"),
        "pv_luka" => ScRunShot(s, "luka", "wasteland_road", 9000f, 8, 12f),
        "pv_fighter" => ScRunShot(s, "fighter", "natural_cave", 9000f, 8, 12f),
        "pv_vampire" => ScRunShot(s, "vampire", "sky_corridor", 9000f, 8, 12f),
        "pv_crowd_dual" => ScCrowdShot(s, "dual_blade"),
        "pv_boss_knight" => PvFinalShot(s, Arg("-pvCharK", "dual_blade"), "wasteland_road", 0, (int)WildBossKind.BlackKnight, 12150f),
        // 2026-11-08 第2版: ネタバレを避けて通常ボス(関門のボス)で見せ場を撮る
        "pv_boss_griffin" => PvFinalShot(s, Arg("-pvCharG", "dragon_lancer"), "wasteland_road", 0, (int)WildBossKind.Griffin, 12150f),
        "pv_boss_demon" => PvFinalShot(s, Arg("-pvCharD", "vampire"), "wasteland_road", 0, (int)WildBossKind.Demon, 12150f),
        "pv_boss_cyclops" => PvFinalShot(s, Arg("-pvCharY", "fighter"), "wasteland_road", 0, (int)WildBossKind.Cyclops, 12150f),
        "pv_boss_golem" => PvFinalShot(s, Arg("-pvCharO", "swordsman"), "natural_cave", 1, (int)CaveBossKind.CrystalGolem, 16150f),
        "pv_boss_behemoth" => PvFinalShot(s, Arg("-pvCharB", "archer"), "sky_corridor", 2, (int)SkyBossKind.Behemoth, 16150f),
        "pv_boss_hydra" => PvFinalShot(s, Arg("-pvCharH", "ninja"), "wasteland_road", 0, (int)WildBossKind.Hydra, 12150f),
        "pv_road" => PvRoadShot(s, Arg("-pvCharR", "swordsman"), "wasteland_road", float.Parse(Arg("-pvRoadAt", "6150"), System.Globalization.CultureInfo.InvariantCulture)),
        "pv_road_sky" => PvRoadShot(s, Arg("-pvCharR", "swordsman"), "sky_corridor", float.Parse(Arg("-pvRoadAt", "6150"), System.Globalization.CultureInfo.InvariantCulture)),
        _ => null,
    };

    // 見た目の分かる強化(属性/追加攻撃)を、終盤らしい強さで持たせる(QA の FinalBoss と同じ "mix" の組)
    void PvApplyBuild(int lv)
    {
        var ids = CardBuildPresets.Find("mix");
        if (ids == null) return;
        foreach (var id in ids) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, lv); }
        L($"[pv] build mix Lv{lv}: {string.Join(",", ids)}");
    }

    IEnumerator PvFinalShot(string name, string ch, string stage, int family, int kind, float distance)
    {
        yield return ScBegin(ch, stage, distance, 10);
        PvApplyBuild(int.Parse(Arg("-pvBuildLv", "4")));
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) hsa.SetAutoInBoss(true);
        yield return new WaitForSeconds(1.5f);
        var bm = FindFirstObjectByType<BossManager>();
        if (bm == null) { L("[pv] no BossManager"); yield return ScEnd(); yield break; }
        yield return ScWaitReach(ScFlatStage(160f, 1300f));
        bm.DebugSpawnBossForTest(family, kind);
        yield return null;
        autoPickHold = true; scGuideHold = true;
        ScRecStart(name);
        float ultAt = float.Parse(Arg("-pvUltAt", "4.5"), System.Globalization.CultureInfo.InvariantCulture);
        float breakAfterUlt = float.Parse(Arg("-pvBreakAfter", "1.5"), System.Globalization.CultureInfo.InvariantCulture);
        float breakHold = float.Parse(Arg("-pvBreakHold", "5.5"), System.Globalization.CultureInfo.InvariantCulture);
        float t = 0f, ultEnd = -1f, broke = -1f, ultSeen = -1f;
        bool ult = false, killed = false;
        WildBossBase boss = null;
        while (t < 60f)
        {
            yield return ScWait(0.25f); t += 0.25f;
            if (boss == null || boss.IsDead) boss = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled && !b.IsDead);
            if (boss == null) { if (killed || t > 10f) break; continue; }
            if (boss.IsEntering) continue;
            if (!ult && t >= ultAt) { ult = boss.DebugForceUltimate(); ultSeen = -1f; L($"[pv] {name}: ultimate {(ult ? "requested" : "refused")} at {t:F1}s"); if (!ult && t > ultAt + 4f) ult = true; }
            // 必殺技は予告の後に始まる → 実際に始まった(UltimateRunning)のを見てから、終わるのを待つ(BREAK で必殺技を切らない)
            if (ult && ultSeen < 0f && boss.UltimateRunning) { ultSeen = t; L($"[pv] {name}: ultimate running at {t:F1}s"); }
            if (ult && ultEnd < 0f && (ultSeen >= 0f || t > ultAt + 3f) && !boss.UltimateRunning) { ultEnd = t; L($"[pv] {name}: ultimate ended at {t:F1}s"); }
            if (ultEnd >= 0f && broke < 0f && t >= ultEnd + breakAfterUlt) { boss.DebugForceBreak(); broke = t; L($"[pv] {name}: BREAK at {t:F1}s (broken={boss.Broken})"); ScStill("break"); }
            if (ult && ultEnd < 0f && t > ultAt + 14f) { ultEnd = t; L($"[pv] {name}: ultimate still running at {t:F1}s, go on"); }
            if (broke >= 0f && !killed && t >= broke + breakHold)
            {
                killed = boss.DebugKillWithAttack(BossFinalAttack.Forward);
                L($"[pv] {name}: finish at {t:F1}s ({killed})");
                ScStill("finish");
                if (!killed) break;
                yield return ScWait(5.0f); t += 5f;
                break;
            }
        }
        L($"[pv] {name}: recorded {t:F1}s at {gm.MaxDistance:F0}m");
        yield return ScRecStop();
        autoPickHold = false; scGuideHold = false;
        yield return ScEnd();
    }

    // 95km からの3体同時の組(実際の流れ)。最初のボスが出る少し前から録り、-pvTrioSecs 秒
    IEnumerator PvTrioShot(string name, string ch, string stage)
    {
        yield return ScBegin(ch, stage, 0f, 10);
        PvApplyBuild(int.Parse(Arg("-pvBuildLv", "5")));
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) hsa.SetAutoInBoss(true);
        WarpTo(94850f);
        typeof(GameManager).GetMethod("ApplySprintArrivalLevel", NP)?.Invoke(gm, new object[] { 94850f });
        yield return new WaitForSeconds(1.0f);
        float w = 0f;
        while (gm.MaxDistance < 94960f && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        autoPickHold = false;
        ScRecStart(name);
        float secs = float.Parse(Arg("-pvTrioSecs", "40"), System.Globalization.CultureInfo.InvariantCulture);
        int maxAlive = 0;
        for (float t = 0f; t < secs; t += 0.5f)
        {
            yield return ScWait(0.5f);
            int alive = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Count(b => b != null && b.isActiveAndEnabled && !b.IsDead);
            if (alive > maxAlive) { maxAlive = alive; L($"[pv] {name}: {alive} bosses at {t:F1}s ({gm.MaxDistance:F0}m)"); }
            if (alive >= 2 && Mathf.Repeat(t, 2f) < 0.25f) ScStill($"trio{t:00}");
            if (gm.IsGameOver) break;
        }
        L($"[pv] {name}: max alive {maxAlive}, end at {gm.MaxDistance:F0}m");
        yield return ScRecStop();
        yield return ScEnd();
    }
    // 締めの場面: 敵の出ない区間を走り続ける(景色と遠くまで続く道)。実際のランで、出現を止めた区間を走るだけ
    IEnumerator PvRoadShot(string name, string ch, string stage, float distance)
    {
        yield return ScBegin(ch, stage, distance, 6);
        var tm = TerrainManager.Instance;
        if (tm != null) { tm.enemySpawnChance = 0f; tm.ClearAllEnemies(); }
        yield return new WaitForSeconds(2.5f);
        if (tm != null) tm.ClearAllEnemies();
        ScRecStart(name);
        float secs = float.Parse(Arg("-pvRoadSecs", "12"), System.Globalization.CultureInfo.InvariantCulture);
        for (float t = 0f; t < secs; t += 1f) { yield return ScWait(1f); if (tm != null) tm.ClearAllEnemies(); }
        yield return ScRecStop();
        if (tm != null) tm.enemySpawnChance = 1f;
        yield return ScEnd();
    }
}
#endif

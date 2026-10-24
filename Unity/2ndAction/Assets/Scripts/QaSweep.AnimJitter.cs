#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 敵のアニメーションのドタバタの測定(2026-10-24): ゲームの中で毎フレーム、絵(SpriteRenderer)の画面での位置と大きさを書く。
//   -qaAnimJitter <dir> [-ajBosses Wild:Wolf,Cave:Bat|all|none] [-ajMobs goblin,...|all|none] [-ajSecs 10] [-ajSpeed 1] [-ajBreak 1]
//  出力 <dir>/jitter.tsv: frame t who id pose sprite rootX rootY minY maxY minX maxX cx visLocalY visScaleY visRot groundY
//  解析は Tools/art/animjitter.py(コマが替わった時の 足元/横/高さ の跳び、走りの1枚目への戻り、姿勢の細かい切り替え、上下の震え)
public partial class QaSweep
{
    StringBuilder ajSb;
    int ajFrame;

    static readonly string[] AjAllMobs = { "goblin", "chaser_runner", "rusher_runner", "shooter_archer", "heavy_ogre", "irregular_imp", "goblin_elite",
        "cave_ant", "soldier_ant", "cave_hopper", "burrow_worm", "cave_bat", "harpy", "flying_wyvern", "gargoyle", "sky_hound", "sky_hunter", "celestial_knight", "storm_spirit", "ancient_sentinel" };

    IEnumerator AnimJitterMode()
    {
        Application.targetFrameRate = 60;
        BossManager.NetTestBossHpOverride = 4000000;
        float secs = float.Parse(Arg("-ajSecs", "10"), System.Globalization.CultureInfo.InvariantCulture);
        float speed = float.Parse(Arg("-ajSpeed", "1"), System.Globalization.CultureInfo.InvariantCulture);
        string bossArg = Arg("-ajBosses", "all"), mobArg = Arg("-ajMobs", "all");
        var bosses = new List<string>();
        if (bossArg == "all")
        {
            foreach (var k in System.Enum.GetNames(typeof(WildBossKind))) if (k != "Dragon") bosses.Add("Wild:" + k);
            foreach (var k in System.Enum.GetNames(typeof(CaveBossKind))) bosses.Add("Cave:" + k);
        }
        else if (bossArg != "none") bosses.AddRange(bossArg.Split(','));
        string[] mobs = mobArg == "all" ? AjAllMobs : mobArg == "none" ? new string[0] : mobArg.Split(',');
        ajSb = new StringBuilder("frame\tt\twho\tid\tpose\tsprite\trootX\trootY\tminY\tmaxY\tminX\tmaxX\tcx\tvisY\tvisSy\tvisRot\tgroundY\tspeedKmh\n");

        // 雑魚: マップごとにまとめて、1.0秒ごとに目の前へ出す(倒さない)
        foreach (var group in mobs.GroupBy(MobStage))
        {
            yield return BeginRun("swordsman", group.Key);
            SetGod(true);
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            WarpTo(1400f);
            yield return new WaitForSeconds(1.2f);
            PlayerController.DebugSpeedScale = speed;
            foreach (var id in group)
            {
                L($"[jitter] mob {id} ({group.Key}) x{speed}");
                float t = 0f, nextSpawn = 0f;
                while (t < secs)
                {
                    if (t >= nextSpawn) { var ed = EncounterDirector.Instance; if (ed != null) ed.DebugSpawnEnemy(id, default); nextSpawn += 1.0f; }
                    yield return null; t += Time.deltaTime;
                    foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                        if (ec != null && ec.isActiveAndEnabled && ec.name.StartsWith(id)) AjLogMob(ec, id);
                }
                foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (ec != null) ec.gameObject.SetActive(false);
            }
            PlayerController.DebugSpeedScale = 1f;
            SetGod(false);
            yield return EndRun();
        }

        // ボス: 補助の自動攻撃で戦う(移動・溜め・攻撃・被弾)→ 途中で BREAK(崩れ・膝つき・復帰)
        foreach (var bk in bosses)
        {
            var p = bk.Split(':');
            bool cave = p[0] == "Cave";
            yield return BeginRun("swordsman", cave ? "natural_cave" : "wasteland_road");
            SetGod(true);
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            WarpTo(1400f);
            yield return new WaitForSeconds(1.2f);
            PlayerController.DebugSpeedScale = speed;
            if (cave) Bm.DebugCaveEncounter((CaveBossKind)System.Enum.Parse(typeof(CaveBossKind), p[1]), -1);
            else Bm.DebugWildEncounter((WildBossKind)System.Enum.Parse(typeof(WildBossKind), p[1]), -1);
            yield return WaitBossSpawn(10f);
            var b = WildAlive().FirstOrDefault();
            if (b == null) { Check(false, $"jitter: {bk} appeared"); SetGod(false); yield return EndRun(); continue; }
            L($"[jitter] boss {bk} x{speed}");
            float t = 0f; bool broke = false;
            while (t < secs * 1.6f && b != null && !b.IsDead)
            {
                if (!broke && Arg("-ajBreak", "1") == "1" && t > secs) { broke = true; b.DebugBreak(); }
                yield return null; t += Time.deltaTime;
                AjLogBoss(b, bk);
            }
            KillAllBosses();
            PlayerController.DebugSpeedScale = 1f;
            SetGod(false);
            yield return EndRun();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "jitter.tsv"), ajSb.ToString());
        L($"[jitter] {ajFrame} rows written");
    }

    static string MobStage(string id)
    {
        if (id.StartsWith("cave_") || id == "soldier_ant" || id == "burrow_worm") return "natural_cave";
        if (id.StartsWith("sky_") || id == "harpy" || id == "flying_wyvern" || id == "gargoyle" || id == "celestial_knight" || id == "storm_spirit" || id == "ancient_sentinel") return "sky_corridor";
        return "wasteland_road";
    }

    void AjLogMob(EnemyController ec, string id)
    {
        var sr = ec.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var sb = ec.GetComponent<EnemySpecialBehavior>();
        string pose = sb != null ? sb.CurrentPose.ToString() : "-";
        AjRow("mob" + AjKey(ec), id, pose, sr, ec.transform, sr.transform);
    }

    readonly Dictionary<EnemyController, int> ajKeys = new Dictionary<EnemyController, int>();
    int AjKey(EnemyController ec) { if (!ajKeys.TryGetValue(ec, out int k)) { k = ajKeys.Count; ajKeys[ec] = k; } return k; }

    void AjLogBoss(WildBossBase b, string id)
    {
        var vis = b.DebugVisual;
        var sr = vis != null ? vis.GetComponentInChildren<SpriteRenderer>() : null;
        if (sr == null || sr.sprite == null) return;
        AjRow("boss", id, b.DebugPose, sr, b.transform, vis);
    }

    void AjRow(string who, string id, string pose, SpriteRenderer sr, Transform root, Transform vis)
    {
        var bd = sr.bounds; var tm = TerrainManager.Instance;
        float gy = tm != null ? tm.GetSupportHeightAt(root.position.x) : 0f;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        ajSb.Append(string.Format(ic, "{0}\t{1:F4}\t{2}\t{3}\t{4}\t{5}\t{6:F4}\t{7:F4}\t{8:F4}\t{9:F4}\t{10:F4}\t{11:F4}\t{12:F4}\t{13:F4}\t{14:F4}\t{15:F2}\t{16:F4}\t{17:F1}\n",
            Time.frameCount, Time.time, who, id, pose, sr.sprite.name, root.position.x, root.position.y, bd.min.y, bd.max.y, bd.min.x, bd.max.x, bd.center.x,
            vis.localPosition.y, vis.localScale.y, vis.localEulerAngles.z, gy, pc != null ? GameManager.SpeedKmh(pc.CurrentAutoRunSpeed) : 0f));
        ajFrame++;
    }
}
#endif

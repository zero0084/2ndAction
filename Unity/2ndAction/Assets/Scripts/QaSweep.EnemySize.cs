#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 敵の大きさの確認(2026-11-28 マスター依頼①): 実際の戦闘画面で、ゴブリンと並べて 移動 → 攻撃(予兆/攻撃) → 被弾 → 撃破 を撮り、
//  毎フレームの絵の大きさ(画面の SpriteRenderer.bounds)と当たり判定(Collider2D.bounds)を書く。
//   -qaEnemySize <dir> [-esMobs id,id|all] [-esSecs 4]
//  出力: <dir>/size.tsv(frame t id who phase pose sprite srW srH srCx srMinY colW colH colCx colCy groundY sx0 sy0 sx1 sy1)
//        <dir>/shot_<id>_<phase>.png(move / attack / hurt / death)。縦横の確認は窓の大きさを変えて2回走らせる
public partial class QaSweep
{
    static readonly string[] EsAllMobs = {
        "goblin", "goblin_elite", "chaser_runner", "rusher_runner", "shooter_archer", "heavy_ogre", "irregular_imp", "flying_wyvern",
        "air_locust", "air_sand_swallow", "air_vulture", "air_red_eagle", "air_sand_spirit",
        "cave_ant", "soldier_ant", "cave_hopper", "burrow_worm", "cave_bat", "air_bat_swarm", "air_moth", "air_crystal_dragonfly", "air_thread_spider", "air_jellyfish",
        "harpy", "gargoyle", "sky_hound", "sky_slime", "sky_hunter", "celestial_knight", "storm_spirit", "ancient_sentinel", "air_sky_wisp", "air_sky_swallow", "air_cloud_manta",
        "air_reaper_familiar", "air_black_imp", "air_mech_drake", "air_wraith", "air_ancient_drone",
        "card_fairy", "golden_slime", "mimic", "treasure_goblin" };

    StringBuilder esSb;
    int esFrame;

    IEnumerator EnemySizeMode()
    {
        Application.targetFrameRate = 60;
        float secs = float.Parse(Arg("-esSecs", "4"), System.Globalization.CultureInfo.InvariantCulture);
        string mobArg = Arg("-esMobs", "all");
        string[] mobs = mobArg == "all" ? EsAllMobs : mobArg.Split(',');
        esSb = new StringBuilder("frame\tt\tid\twho\tphase\tpose\tsprite\tsrW\tsrH\tsrCx\tsrMinY\tcolW\tcolH\tcolCx\tcolCy\tgroundY\tsx0\tsy0\tsx1\tsy1\n");
        var groups = new List<KeyValuePair<string, List<string>>>();
        foreach (var id in mobs)
        {
            string st = MobStage(id);
            if (id.StartsWith("air_moth") || id.StartsWith("air_bat") || id.StartsWith("air_crystal") || id.StartsWith("air_thread") || id.StartsWith("air_jelly")) st = "natural_cave";
            if (id.StartsWith("air_sky") || id.StartsWith("air_cloud")) st = "sky_corridor";
            var g = groups.Find(x => x.Key == st);
            if (g.Value == null) { g = new KeyValuePair<string, List<string>>(st, new List<string>()); groups.Add(g); }
            g.Value.Add(id);
        }
        foreach (var g in groups)
        {
            yield return BeginRun("swordsman", g.Key);
            SetGod(true);
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            // 撮る所を平らに(穴を埋める)・障害物と普段の湧きを止める(見比べる2体だけにする)
            WarpTo(1400f);
            yield return new WaitForSeconds(0.5f);
            var tmz = TerrainManager.Instance;
            if (tmz != null && pc != null) { float lx0 = (float)FloatingOrigin.ToLogical(pc.transform.position.x); tmz.SetResumeFlatZone(lx0 + 100f, lx0 + 4000f); }
            WarpTo(1700f); // 平らな区間の中へ飛び直す(地形を作り直す)
            yield return new WaitForSeconds(0.5f);
            var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            yield return new WaitForSeconds(1.0f);
            PlayerController.DebugSpeedScale = 0.05f; // ほぼ止まって見比べる(敵は普段どおり動く)
            foreach (var id in g.Value)
            {
                ClearMobs();
                yield return null;
                // 並べる: ゴブリンはプレイヤーの 5m 先、相手は 8.5m 先(地上の敵は地面の上、空の敵は地面から 3m)
                var gob = id == "goblin" ? null : SpawnAt("goblin", 5f);
                var tgt = SpawnAt(id, 8.5f);
                if (tgt == null) { Check(false, $"size: {id} spawned"); continue; }
                L($"[size] {id} ({g.Key})");
                float t = 0f; string phase = "move"; bool shotMove = false, shotAtk = false, hurtDone = false, shotHurt = false, killDone = false, shotDeath = false;
                float hurtAt = -1f, killAt = -1f;
                while (t < secs + 3f)
                {
                    yield return null; t += Time.deltaTime;
                    var sb = tgt != null ? tgt.GetComponent<EnemySpecialBehavior>() : null;
                    string pose = sb != null ? sb.CurrentPose.ToString() : "-";
                    if (!shotMove && t > 0.35f) { shotMove = true; yield return EsShot(id, "move"); }
                    if (!shotAtk && (pose == "Telegraph" || pose == "Attack" || pose == "Charge" || pose == "Dive") && t > 0.5f) { shotAtk = true; phase = "attack"; yield return EsShot(id, "attack_" + pose); }
                    if (!hurtDone && (t > secs || shotAtk && t > 1.2f)) { hurtDone = true; phase = "hurt"; hurtAt = t; if (tgt != null && tgt.isActiveAndEnabled) tgt.NetApplyRemoteHit(1, 1, PlayerAttackKind.Normal, tgt.transform.position); }
                    if (hurtDone && !shotHurt && t > hurtAt + 0.08f) { shotHurt = true; yield return EsShot(id, "hurt"); }
                    if (shotHurt && !killDone && t > hurtAt + 0.6f) { killDone = true; phase = "death"; killAt = t; if (tgt != null && tgt.isActiveAndEnabled) tgt.NetApplyRemoteHit(1, 999999, PlayerAttackKind.Normal, tgt.transform.position); }
                    if (killDone && !shotDeath && t > killAt + 0.12f) { shotDeath = true; yield return EsShot(id, "death"); }
                    if (gob != null && gob.isActiveAndEnabled) EsRow(gob, "goblin", "ref", phase);
                    if (tgt != null && tgt.isActiveAndEnabled) EsRow(tgt, id, "target", phase);
                    if (shotDeath && t > killAt + 1.2f) break;
                }
                if (!shotAtk) L($"[size] {id}: no attack pose within {secs:F0}s (move/hurt/death only)");
            }
            ClearMobs();
            PlayerController.DebugSpeedScale = 1f;
            SetGod(false);
            yield return EndRun();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "size.tsv"), esSb.ToString());
        L($"[size] {esFrame} rows written");
    }

    EnemyController SpawnAt(string id, float ahead)
    {
        var tm = TerrainManager.Instance; var def = EnemyDatabase.FindById(id);
        if (tm == null || pc == null || def == null) { L($"[size] {id}: no definition/terrain"); return null; }
        float x = pc.transform.position.x + ahead;
        float? h = tm.GetHeightAt(x);
        float gy = h ?? (pc.transform.position.y - 1f);
        bool flying = def.movementType == EnemyMovementType.Flying;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, flying ? gy + 3f : gy), default, def.behaviorKind);
        return go != null ? go.GetComponent<EnemyController>() : null;
    }

    static void ClearMobs()
    {
        foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (ec != null) ec.gameObject.SetActive(false);
    }

    IEnumerator EsShot(string id, string phase)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"shot_{id}_{phase}.png"));
        L($"[size] shot {id} {phase}");
    }

    void EsRow(EnemyController ec, string id, string who, string phase)
    {
        var cam = Camera.main;
        var sr = ec.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null || cam == null) return;
        var col = ec.GetComponent<Collider2D>();
        var tm = TerrainManager.Instance;
        Bounds b = sr.bounds;
        Bounds cb = col != null ? col.bounds : new Bounds();
        var sb = ec.GetComponent<EnemySpecialBehavior>();
        Vector3 s0 = cam.WorldToScreenPoint(b.min), s1 = cam.WorldToScreenPoint(b.max);
        float gy = tm != null ? tm.GetSupportHeightAt(ec.transform.position.x) : 0f;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        esSb.Append(string.Format(ic, "{0}\t{1:F3}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7:F3}\t{8:F3}\t{9:F3}\t{10:F3}\t{11:F3}\t{12:F3}\t{13:F3}\t{14:F3}\t{15:F3}\t{16:F0}\t{17:F0}\t{18:F0}\t{19:F0}\n",
            Time.frameCount, Time.time, id, who, phase, sb != null ? sb.CurrentPose.ToString() : "-", sr.sprite.name,
            b.size.x, b.size.y, b.center.x - ec.transform.position.x, b.min.y, cb.size.x, cb.size.y, cb.center.x - b.center.x, cb.center.y - b.center.y, gy,
            s0.x, Screen.height - s1.y, s1.x, Screen.height - s0.y));
        esFrame++;
    }
}
#endif

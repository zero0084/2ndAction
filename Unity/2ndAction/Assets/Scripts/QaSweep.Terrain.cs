#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 道の継ぎ目の点検(2026-10-11、実機: 90,167m の上ルートで平地→下り坂の継ぎ目が三角に突き出していた)。
//  -qaTerrJoints <dir> [-qaTerrStages wasteland_road,...] [-qaTerrDists 3000,90000] [-legacyBleed 1] [-qaTerrPer 2]
// 各ステージ/距離で地形を先まで作り、下ルート(Flat->DownSlope など)と上ルート(Sky:Flat->Down など)の継ぎ目を拡大して撮る。
// -legacyBleed 1 は以前の隙間埋め(比較用の「修正前」)。走る面の高さの段差も数える(継ぎ目の前後で高さが飛んでいないか)。
public partial class QaSweep
{
    IEnumerator TerrJointsMode()
    {
        TerrainManager.LegacyConvexBleed = Arg("-legacyBleed", "0") == "1";
        string tag = TerrainManager.LegacyConvexBleed ? "before" : "after";
        string[] stages = Arg("-qaTerrStages", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        float[] dists = Arg("-qaTerrDists", "3000,90000").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        int per = int.Parse(Arg("-qaTerrPer", "2"));
        foreach (var stage in stages)
            foreach (var d in dists)
            {
                // 修正前/後を同じ地形で比べる: 地形の乱数を固定してからシーンを読み直す
                WorldRng.BeginDeterministic(int.Parse(Arg("-qaTerrSeed", "777")));
                yield return ReloadHome();
                yield return BeginRun("swordsman", stage);
                typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
                if (BossManager.Instance != null) BossManager.Instance.enabled = false;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
                yield return new WaitForSeconds(0.5f);
                WarpTo(d); RunLedger.DevClearDebug();
                yield return new WaitForSeconds(2f);
                var tm = TerrainManager.Instance;
                // 先の地形を作らせる(プレイヤーを先へ送る)
                for (int i = 0; i < 6; i++) { var p = pc.transform.position; p.x += 50f; pc.transform.position = p; yield return new WaitForSeconds(0.25f); }
                // 走る面の高さの飛び(継ぎ目の前後 0.05m)
                var joints = tm.DebugListJoints().Concat(tm.DebugListSkyJoints()).ToList();
                int jumps = 0;
                foreach (var kv in joints)
                {
                    bool sky = kv.Value.StartsWith("Sky:");
                    float? a = sky ? tm.GetSkyHeightAt(kv.Key - 0.05f) : tm.GetGroundLineAt(kv.Key - 0.05f);
                    float? b = sky ? tm.GetSkyHeightAt(kv.Key + 0.05f) : tm.GetGroundLineAt(kv.Key + 0.05f);
                    if (a.HasValue && b.HasValue && Mathf.Abs(a.Value - b.Value) > 0.08f) { jumps++; L($"[terrain {stage} {d / 1000f:F0}k] surface jump {a.Value - b.Value:F2}m at {kv.Key:F1} ({kv.Value})"); }
                }
                L($"[terrain {stage} {d / 1000f:F0}k {tag}] joints {joints.Count} ({string.Join(" ", joints.GroupBy(j => j.Value).Select(g => g.Key + "x" + g.Count()))}) surface jumps {jumps}");
                Check(jumps == 0, $"terrain {stage} {d}: the walking surface has no height jump at joints");
                // 撮影(時間を止めてカメラを継ぎ目へ)
                Time.timeScale = 0f;
                var cf = FindFirstObjectByType<CameraFollow>(); bool cfOn = cf != null && cf.enabled; if (cf != null) cf.enabled = false;
                Camera cam = Camera.main;
                float size0 = cam.orthographicSize; Vector3 pos0 = cam.transform.position;
                var rt = new RenderTexture(960, 540, 24);
                var seen = new Dictionary<string, int>();
                foreach (var kv in joints)
                {
                    seen.TryGetValue(kv.Value, out int cnt); if (cnt >= per) continue; seen[kv.Value] = cnt + 1;
                    bool sky = kv.Value.StartsWith("Sky:");
                    float y = sky ? (tm.GetSkyHeightAt(kv.Key) ?? tm.GetGroundLineAt(kv.Key)) : tm.GetGroundLineAt(kv.Key);
                    if (cam.orthographic) cam.orthographicSize = 4f;
                    cam.transform.position = new Vector3(kv.Key, y + 0.3f, cam.transform.position.z);
                    var pp = pc.transform.position; pp.x = kv.Key - 40f; pc.transform.position = pp; // プレイヤーを写さない
                    yield return null; yield return null;
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                    RenderTexture.active = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"J_{stage}_{d / 1000f:F0}k_{kv.Value.Replace(">", "").Replace(":", "")}_{cnt}_{tag}.png"), tex.EncodeToPNG());
                    Destroy(tex);
                }
                rt.Release();
                cam.orthographicSize = size0; cam.transform.position = pos0;
                if (cf != null) cf.enabled = cfOn;
                Time.timeScale = 1f;
                // 走り抜け(上ルートの平地→下り坂の継ぎ目の手前へ置いて、走って通る)。引っかかり/跳ね上がり/落下を数える
                if (Arg("-qaTerrVideo", "0") == "1")
                {
                    if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false); // 自動ジャンプを入れない(継ぎ目の段差だけを見る)
                    var cur = tm.DebugListSkyJoints(); // 今ある地形の継ぎ目(後ろの消えた地形を除く)
                    var sj = cur.Where(j => j.Value == "Sky:Flat->Down" || j.Value == "Sky:Up->Flat").Select(j => j.Key).Where(x => tm.GetSkyHeightAt(x - 14f).HasValue && tm.GetSkyHeightAt(x + 10f).HasValue).ToList();
                    if (sj.Count == 0) L($"[terrain {stage} {d / 1000f:F0}k] no upper-route convex joint ahead to run through");
                    foreach (var jx in sj.Take(2))
                    {
                        float sy = tm.GetSkyHeightAt(jx - 12f) ?? tm.GetGroundLineAt(jx - 12f);
                        pc.transform.position = new Vector3(jx - 12f, sy + 0.2f, 0f);
                        yield return new WaitForSeconds(0.2f);
                        float y0 = pc.transform.position.y, maxUp = 0f, minDy = 0f, x0 = pc.transform.position.x; int lives = gm.Lives;
                        var rec = StartCoroutine(RecordUltimate($"T_{stage}_{(int)jx}_{tag}", 2.5f));
                        float t = 0f; float prevY = y0; bool prevG = pc.IsGrounded;
                        // 接地したまま(前後のフレームとも接地)の上下の飛びだけを数える(ジャンプ/落下は別)
                        while (t < 2.5f) { float y = pc.transform.position.y; bool g = pc.IsGrounded; if (g && prevG) { maxUp = Mathf.Max(maxUp, y - prevY); minDy = Mathf.Min(minDy, y - prevY); } prevY = y; prevG = g; t += Time.deltaTime; yield return null; }
                        L($"[terrain {stage} run-through {jx:F0}] moved {pc.transform.position.x - x0:F1}m maxStepUp {maxUp:F3} maxStepDown {minDy:F3} grounded {pc.IsGrounded} hits {lives - gm.Lives}");
                        Check(maxUp < 0.25f && lives == gm.Lives, $"terrain {stage}: no sudden jump up when running through the joint at {jx:F0} ({maxUp:F2})");
                    }
                }
                yield return EndRun();
            }
        TerrainManager.LegacyConvexBleed = false;
        WorldRng.EndDeterministic();
    }
}
#endif

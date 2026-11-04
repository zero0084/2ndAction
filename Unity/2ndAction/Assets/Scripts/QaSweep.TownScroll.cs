#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 城下町(関門の後の内部)の背景の流れ(2026-11-04、実機の不具合「城下町に入ると背景が高速で流れ続けることがある」):
//   -qaTownScroll <dir> [-tsGates 10,20] [-tsSpeeds 1,1.8] [-tsAfter 45] [-tsFps 0|15] [-tsTag x]
//  関門の 400m 手前から、ボス戦(数秒戦って倒す)→ 門 → 内部(城下町)→ 内部の後まで毎フレーム記録する:
//   プレイヤーの実際の移動 / 走行距離(MaxDistance)/ カメラ / 遠景(背景の絵)/ 目的地 / 門 / 内部(城下町)/ 雲 の画面上の速さ。
//  「その層の画面上の速さ ÷ プレイヤーの実際の速さ」が層ごとの決まった割合からずれた区間と、走行距離が止まっている間に動いた層を数える。
//  初回(10km)と2回目(20km)、通常速度と高速。-tsFps で撮影(HUD の速度・距離を出したまま)
public partial class QaSweep
{
    IEnumerator TownScrollMode()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var gates = Arg("-tsGates", "10,20").Split(',').Select(s => float.Parse(s, inv)).ToList();
        var speeds = Arg("-tsSpeeds", "1,1.8").Split(',').Select(s => float.Parse(s, inv)).ToList();
        float after = float.Parse(Arg("-tsAfter", "45"), inv);
        int fps = int.Parse(Arg("-tsFps", "0"));
        string tag0 = Arg("-tsTag", "ts");
        foreach (float sp in speeds)
        {
            yield return BeginRun("swordsman", "wasteland_road");
            SetGod(true);
            GameManager.BlockExpGain = true;
            int entry = 0;
            foreach (float gkm in gates)
            {
                entry++;
                string tag = $"{tag0}_x{sp:F1}_{gkm:F0}km";
                gm.DebugWarpToDistance(gkm * 1000f - 400f);
                yield return new WaitForSeconds(0.4f);
                var sb = new StringBuilder("t\tframe\tdist\tdDist\tplayerDx\tcamDx\tboss\tstate\tfarScr\tinteriorScr\tinteriorA\tgateScr\tdestScr\tcloudScr\tspeedHud\n");
                var cam = Camera.main;
                float t = 0f, bossT = 0f, sinceInterior = -1f;
                float prevD = gm.MaxDistance, prevPx = pc.transform.position.x, prevCx = cam.transform.position.x, prevDD = 0f;
                var prevPos = new Dictionary<string, float>();
                int frame = 0, every = fps > 0 ? Mathf.Max(1, 30 / fps) : 0;
                if (fps > 0) Time.captureDeltaTime = 1f / 30f;
                // 層ごとの記録: 画面上の移動量の合計 / 走行距離が止まっている間の移動 / 想定からのずれ
                var stats = new Dictionary<string, (float sum, float whileStopped, float worst, int badFrames)>();
                bool interiorSeen = false;
                while (t < 120f)
                {
                    PlayerController.DebugSpeedScale = sp;
                    float dt = Time.deltaTime;
                    var bm = BossManager.Instance;
                    bool boss = bm != null && bm.IsBossPhase;
                    if (boss)
                    {
                        bossT += dt;
                        if (bossT > 3f)
                            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
                                if (b != null && !b.IsDead && b.isActiveAndEnabled && !b.IsEntering && b.DebugKillWithAttack(BossFinalAttack.Forward)) bossT = -8f;
                    }
                    if (gm.IsLocalChoiceOpen) { yield return null; t += dt; continue; }
                    cam = Camera.main;
                    float d = gm.MaxDistance, px = pc.transform.position.x, cx = cam.transform.position.x;
                    float dD = d - prevD, pDx = px - prevPx, cDx = cx - prevCx;
                    bool warp = Mathf.Abs(pDx) > 6f || Mathf.Abs(cDx) > 6f; // 浮動原点のずらし/ワープ
                    var layers = LandmarkJourney.DebugLayerScreenX(cam); // 層の名前 → 画面上の位置(カメラからの横のずれ)
                    var row = new Dictionary<string, float>();
                    foreach (var kv in layers)
                    {
                        float scr = prevPos.TryGetValue(kv.Key, out float pv) && !float.IsNaN(kv.Value) && !float.IsNaN(pv) && !warp ? kv.Value - pv : float.NaN;
                        row[kv.Key] = scr;
                        if (!float.IsNaN(scr))
                        {
                            stats.TryGetValue(kv.Key, out var st);
                            st.sum += Mathf.Abs(scr);
                            if (Mathf.Abs(dD) < 1e-4f && Mathf.Abs(prevDD) < 1e-4f) st.whileStopped += Mathf.Abs(scr); // 止まって 2 フレーム目から(1 フレームの遅れを除く)
                            // 想定: 画面上の速さ = -(プレイヤーの実際の前進) × (1 - 層の流れ方の割合)。内部は -距離の進み × interiorParallax
                            float expect = LandmarkJourney.DebugExpectedScreenMove(kv.Key, dD, pDx, cDx);
                            float expectPrev = LandmarkJourney.DebugExpectedScreenMove(kv.Key, prevDD, pDx, cDx); // 背景の更新が走行距離の更新より先に走るフレームは 1 フレーム前の進み
                            if (!float.IsNaN(expect))
                            {
                                float err = Mathf.Min(Mathf.Abs(scr - expect), Mathf.Abs(scr - expectPrev));
                                if (err > st.worst) st.worst = err;
                                if (err > 0.05f + Mathf.Abs(expect) * 0.5f) st.badFrames++;
                            }
                            stats[kv.Key] = st;
                        }
                        prevPos[kv.Key] = kv.Value;
                    }
                    string state = LandmarkJourney.State;
                    if (state.Contains("interior")) { interiorSeen = true; if (sinceInterior < 0f) sinceInterior = 0f; }
                    if (sinceInterior >= 0f) sinceInterior += dt;
                    string F(string k) => row.TryGetValue(k, out float v) && !float.IsNaN(v) ? v.ToString("F3", inv) : "-";
                    sb.Append($"{t:F2}\t{Time.frameCount}\t{d:F1}\t{dD:F3}\t{pDx:F3}\t{cDx:F3}\t{(boss ? 1 : 0)}\t{state.Trim()}\t{F("far")}\t{F("interior")}\t{LandmarkJourney.InteriorAlpha:F2}\t{F("gate")}\t{F("dest")}\t{F("cloud")}\t{pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F0}\n");
                    if (every > 0 && (frame++ % every) == 0) Shot($"ts_{tag}_{frame:00000}");
                    prevD = d; prevPx = px; prevCx = cx; prevDD = dD;
                    if (sinceInterior > after) break;
                    yield return null;
                    t += fps > 0 ? 1f / 30f : dt;
                }
                Time.captureDeltaTime = 0f;
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"ts_{tag}.tsv"), sb.ToString());
                L($"[ts] {tag}: entry #{entry}, interior seen {interiorSeen}, {t:F0}s, distance {gm.MaxDistance:F0}m");
                foreach (var kv in stats.OrderBy(k => k.Key))
                    L($"   {kv.Key}: moved {kv.Value.sum:F1}m on screen, while the distance was stopped {kv.Value.whileStopped:F2}m, worst frame error {kv.Value.worst:F3}m, frames off by >50% {kv.Value.badFrames}");
                Check(interiorSeen, $"{tag}: the town interior was shown");
                if (stats.TryGetValue("interior", out var si))
                {
                    Check(si.badFrames <= 3, $"{tag}: the town street scrolls at its depth speed ({si.badFrames} bad frames)");
                    Check(si.whileStopped < 1f, $"{tag}: the town street stops while the run distance is stopped (moved {si.whileStopped:F2}m)");
                }
            }
            PlayerController.DebugSpeedScale = 1f;
            GameManager.BlockExpGain = false;
            SetGod(false);
            yield return EndRun();
        }
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// ドラゴン/魔人(飛ぶボス)の上下のブレの記録(2026-10-10): -qaFlyJitter <dir> [-qaFlySecs 30]
//  毎フレーム、本体(transform)/当たり判定(BoxCollider2D)/絵(SpriteRenderer)の高さ、状態、基準にしている地面の高さ(穴=null)を記録し、
//  意図した動き(突進/降下/瞬間移動の技)以外で 1フレームに大きく上下した所を「跳び」として数える。
//  場面: 天空回廊(浮島=穴が多い)/荒野街道(坂/段差) × ドラゴン/魔人、途中で BREAK も起こす
public partial class QaSweep
{
    class FlyStat { public int frames, jumps, jumpsAtPit, jumpsAtStep, visualJumps, colliderMismatch; public float maxJump, maxVisualStep, maxMismatch; public List<string> samples = new List<string>(); }

    static readonly System.Reflection.BindingFlags FlyF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

    IEnumerator FlyJitterMode()
    {
        float secs = float.Parse(Arg("-qaFlySecs", "30"), System.Globalization.CultureInfo.InvariantCulture);
        TerrainManager.DebugLegacySupport = Arg("-qaFlyOldGround", "0") == "1"; // 修正前の動きを撮る比較用
        bool video = Arg("-qaFlyVideo", "0") == "1";
        var sb = new StringBuilder("stage\tboss\tframes\tjumps\tatPit\tatStep\tmaxJump\tvisualSteps\tmaxVisualStep\tcolliderMismatch\tmaxMismatch\n");
        var trace = new StringBuilder("stage\tboss\tt\tstate\ty\tcolY\tsprY\tgroundRefX\tgroundRef\tgroundAtX\tdy\n");
        foreach (var stage in new[] { "sky_corridor", "wasteland_road" })
        {
            foreach (var kind in new[] { SkyBossKind.Dragon, SkyBossKind.Majin })
            {
                yield return BeginRun("swordsman", stage);
                typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
                BossManager.NetTestBossHpOverride = 50000000;
                WarpTo(stage == "sky_corridor" ? 2600f : 2300f);
                yield return new WaitForSeconds(1f);
                Bm.DebugSkyEncounter(kind, -1);
                float w = 0f;
                Component boss = null;
                while (boss == null && w < 15f)
                {
                    boss = kind == SkyBossKind.Dragon ? (Component)FindObjectsByType<DragonController>(FindObjectsSortMode.None).FirstOrDefault(d => d.DebugAlive)
                                                      : FindObjectsByType<MajinController>(FindObjectsSortMode.None).FirstOrDefault(m => m.DebugAlive);
                    yield return null; w += Time.unscaledDeltaTime;
                }
                if (boss == null) { Warn($"[fly] {stage} {kind}: no boss"); yield return EndRun(); continue; }
                var st = new FlyStat();
                var tf = boss.transform;
                var box = boss.GetComponent<BoxCollider2D>();
                var sr = boss.GetComponentInChildren<SpriteRenderer>();
                var stateF = boss.GetType().GetField("state", FlyF);
                var trackedF = boss.GetType().GetField("trackedX", FlyF);
                var tm = TerrainManager.Instance;
                float prevY = float.NaN, prevVis = float.NaN, prevCol = float.NaN; float? prevRef = null; float t = 0f; bool broke = false;
                string prevState = ""; float stateSince = 0f; int colOffFrames = 0;
                // 入場の演出が終わるまで待つ
                w = 0f; while (stateF != null && stateF.GetValue(boss).ToString() == "Entering" && w < 10f) { yield return null; w += Time.deltaTime; }
                if (video && stage == "sky_corridor") StartCoroutine(RecordUltimate($"fly_{stage}_{kind}", 8f)); // 浮島(穴)の上を飛ぶ所
                while (t < secs && boss != null && tf != null)
                {
                    yield return null;
                    if (Time.deltaTime <= 0f) continue;
                    t += Time.deltaTime;
                    if (!broke && t > secs * 0.5f && boss is IBossBattleDebug dbg) { broke = true; dbg.DebugForceBreak(); }
                    string state = stateF != null ? stateF.GetValue(boss).ToString() : "?";
                    if (state == "Dead") break;
                    if (state != prevState) { prevState = state; stateSince = t; }
                    float y = tf.position.y;
                    Physics2D.SyncTransforms(); // 当たり判定は物理の同期まで1フレーム遅れて付いてくる(Unity の仕様)。同期してから比べる
                    float colY = box != null ? box.bounds.center.y : y;
                    float sprY = sr != null ? sr.bounds.center.y : y;
                    float refX = kind == SkyBossKind.Dragon && trackedF != null ? (float)trackedF.GetValue(boss) : tf.position.x;
                    float? gRef = tm != null ? tm.GetHeightAt(refX) : null;
                    float? gAt = tm != null ? tm.GetHeightAt(tf.position.x) : null;
                    st.frames++;
                    if (!float.IsNaN(prevY))
                    {
                        float dy = y - prevY;
                        // 意図して速く動く状態(突進/降下/技)は除く
                        // BREAK の直後の落下(崩れて地面へ落ちる 0.6 秒)も意図した動き
                        bool intended = state == "Charging" || state == "Landing" || state == "Special" || state == "Entering" || (state == "Stunned" && t - stateSince < 0.6f);
                        float limit = Mathf.Max(0.35f, 12f * Time.deltaTime); // 1秒に 12m より速い上下は不自然
                        if (!intended && Mathf.Abs(dy) > limit)
                        {
                            st.jumps++; st.maxJump = Mathf.Max(st.maxJump, Mathf.Abs(dy));
                            bool atPit = !gRef.HasValue || !prevRef.HasValue;
                            bool atStep = gRef.HasValue && prevRef.HasValue && Mathf.Abs(gRef.Value - prevRef.Value) > 0.3f;
                            if (atPit) st.jumpsAtPit++; if (atStep) st.jumpsAtStep++;
                            if (st.samples.Count < 12) st.samples.Add($"t={t:F2} state={state} dy={dy:F2} y={y:F2} groundRef({refX:F1})={(gRef.HasValue ? gRef.Value.ToString("F2") : "PIT")} prev={(prevRef.HasValue ? prevRef.Value.ToString("F2") : "PIT")}");
                        }
                        // 絵と本体の差(コマごとの基準位置のずれ)
                        float vis = sprY - y;
                        if (!float.IsNaN(prevVis) && Mathf.Abs(vis - prevVis) > 0.25f) { st.visualJumps++; st.maxVisualStep = Mathf.Max(st.maxVisualStep, Mathf.Abs(vis - prevVis)); }
                        prevVis = vis;
                        // 当たり判定の位置は物理の同期で1フレーム遅れることがある(Unity の仕様)。2フレーム以上ずれたままの時だけ数える
                        float colOff = colY - y;
                        if (!float.IsNaN(prevCol) && Mathf.Abs(colOff - prevCol) > 0.05f) colOffFrames++; else { colOffFrames = 0; prevCol = colOff; }
                        if (colOffFrames >= 2) { st.colliderMismatch++; st.maxMismatch = Mathf.Max(st.maxMismatch, Mathf.Abs(colOff - prevCol)); }
                        if (Arg("-qaFlyTrace", "0") == "1") trace.AppendLine($"{stage}\t{kind}\t{t:F3}\t{state}\t{y:F3}\t{colY:F3}\t{sprY:F3}\t{refX:F1}\t{(gRef.HasValue ? gRef.Value.ToString("F2") : "PIT")}\t{(gAt.HasValue ? gAt.Value.ToString("F2") : "PIT")}\t{dy:F3}");
                    }
                    else { prevVis = sprY - y; prevCol = colY - y; }
                    prevY = y; prevRef = gRef;
                }
                L($"[fly] {stage} {kind}: frames {st.frames} jumps {st.jumps} (at pit {st.jumpsAtPit}, at step {st.jumpsAtStep}) max {st.maxJump:F2}m; visual-vs-body steps {st.visualJumps} (max {st.maxVisualStep:F2}); collider offset changes {st.colliderMismatch} (max {st.maxMismatch:F2})");
                foreach (var s in st.samples) L($"[fly]   {s}");
                Check(st.jumps == 0, $"{stage} {kind}: no sudden up/down jumps outside the intended dives/teleports ({st.jumps}, max {st.maxJump:F2}m)");
                Check(st.colliderMismatch == 0, $"{stage} {kind}: the hitbox stays with the body ({st.colliderMismatch} changes)");
                sb.AppendLine($"{stage}\t{kind}\t{st.frames}\t{st.jumps}\t{st.jumpsAtPit}\t{st.jumpsAtStep}\t{st.maxJump:F2}\t{st.visualJumps}\t{st.maxVisualStep:F2}\t{st.colliderMismatch}\t{st.maxMismatch:F2}");
                BossManager.NetTestBossHpOverride = 0;
                yield return EndRun();
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "fly_jitter.tsv"), sb.ToString());
        if (Arg("-qaFlyTrace", "0") == "1") System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "fly_trace.tsv"), trace.ToString());
    }
}
#endif

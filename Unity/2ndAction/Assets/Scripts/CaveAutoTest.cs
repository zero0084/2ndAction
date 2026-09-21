#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

// Editor-only automated check for the cave stage (spike damage / ceiling clamp). Started from
// CaveStage when EditorPrefs "CaveAutoTest" == 1; writes a report next to the project (CaveAutoTest.txt).
public partial class CaveStage
{

    IEnumerator AutoTest()
    {
        var log = new StringBuilder();
        void L(string s) { log.AppendLine(s); Debug.Log("[CaveAutoTest] " + s); }
        yield return new WaitForSeconds(4f);
        PlayerController pc = PlayerController.Instance;
        TerrainManager tm = TerrainManager.Instance;
        GameManager.Instance.DebugSetLives(999);
        var fire = typeof(PlayerController).GetMethod("FireJump", BindingFlags.NonPublic | BindingFlags.Instance);
        var fGround = typeof(PlayerController).GetField("isGrounded", BindingFlags.NonPublic | BindingFlags.Instance);
        var fJumps = typeof(PlayerController).GetField("jumpsUsed", BindingFlags.NonPublic | BindingFlags.Instance);
        float speed = pc.CurrentAutoRunSpeed;
        L($"start: speed={speed:F2} nodes={nodes.Count} spikes={spikes.Count} torches={torches.Count}");

        int doubleTests = 0, doubleHits = 0, singleTests = 0, singleHits = 0, lowTests = 0, lowViol = 0, lowStuck = 0;
        float lowWorstOver = -99f;
        float cursorX = pc.transform.position.x;
        var doneSpikes = new System.Collections.Generic.HashSet<int>();
        var doneLow = new System.Collections.Generic.HashSet<int>();
        for (int iter = 0; iter < 400 && (doubleTests < 4 || singleTests < 3 || lowTests < 3); iter++)
        {
            // find next target beyond cursor
            int spikeIdx = -1;
            for (int i = 0; i < spikes.Count; i++) if (!doneSpikes.Contains(i) && spikes[i].x > cursorX + 12f) { spikeIdx = i; break; }
            int lowIdx = -1;
            for (int i = 0; i < nodes.Count; i++) if (!doneLow.Contains(i) && nodes[i].mode == 2 && nodes[i].x > cursorX + 12f && i + 2 < nodes.Count) { lowIdx = i; break; }
            float targetX = float.MaxValue; bool isSpike = false;
            if (spikeIdx >= 0 && doubleTests + singleTests < 7) { targetX = spikes[spikeIdx].x; isSpike = true; }
            if (lowIdx >= 0 && lowTests < 3 && nodes[lowIdx].x < targetX) { targetX = nodes[lowIdx].x; isSpike = false; }
            if (targetX == float.MaxValue)
            {
                // nothing generated yet: walk forward
                cursorX = nodes[nodes.Count - 1].x - 30f;
                Warp(pc, tm, cursorX);
                yield return new WaitForSeconds(0.6f);
                continue;
            }
            if (isSpike)
            {
                doneSpikes.Add(spikeIdx);
                bool doubleJump = (doubleTests + singleTests) % 2 == 0 ? doubleTests < 4 : singleTests >= 3;
                if (doubleTests >= 4) doubleJump = false;
                if (singleTests >= 3) doubleJump = true;
                Spike s = spikes[spikeIdx];
                // stand ~4.5m before the spike, on solid ground, no pits within reach
                float sx = s.x - 4.5f;
                Warp(pc, tm, sx);
                cursorX = s.x;
                yield return new WaitForSeconds(0.15f);
                int before = SpikeHitCount;
                fire.Invoke(pc, null);
                if (doubleJump) { yield return new WaitForSeconds(0.42f); fire.Invoke(pc, null); }
                float maxHead = 0f; float t = 0f;
                while (t < 1.6f) { t += Time.deltaTime; maxHead = Mathf.Max(maxHead, pc.transform.position.y + playerHeadHeight - tm.GetGroundLineAt(pc.transform.position.x)); yield return null; }
                int hits = SpikeHitCount - before;
                float tip = s.topY - s.len - tm.GetGroundLineAt(s.x);
                L($"{(doubleJump ? "DOUBLE" : "SINGLE")} jump at spike x={s.x:F1}: tip={tip:F2} above ground, maxHead={maxHead:F2}, spikeHits={hits}");
                if (doubleJump) { doubleTests++; if (hits > 0) doubleHits++; } else { singleTests++; if (hits > 0) singleHits++; }
            }
            else
            {
                doneLow.Add(lowIdx);
                float sx = nodes[lowIdx].x - 5f;
                Warp(pc, tm, sx);
                cursorX = nodes[lowIdx].x + 8f;
                yield return new WaitForSeconds(0.15f);
                fire.Invoke(pc, null);
                yield return new WaitForSeconds(0.42f);
                fire.Invoke(pc, null);
                float over = -99f; float t = 0f; bool landed = false;
                while (t < 2.0f)
                {
                    t += Time.deltaTime;
                    float x = pc.transform.position.x;
                    float? lim = tm.GetCeilingLimitY(x);
                    if (lim.HasValue) over = Mathf.Max(over, pc.transform.position.y - lim.Value);
                    if (t > 0.5f && (bool)fGround.GetValue(pc)) landed = true;
                    yield return null;
                }
                lowTests++;
                lowWorstOver = Mathf.Max(lowWorstOver, over);
                if (over > 0.001f) lowViol++;
                if (!landed) lowStuck++;
                L($"LOW ceiling node x={nodes[lowIdx].x:F1}: max(feetY - limitY)={over:F3} (0 = clamped at the ceiling, <0 = never reached, >0 = penetrated), landedAfterwards={landed}");
            }
        }
        L($"RESULT double-jump spike tests={doubleTests} hits={doubleHits}; single-jump tests={singleTests} hits={singleHits}; low-ceiling tests={lowTests} penetrations={lowViol} stuck={lowStuck} worstOver={lowWorstOver:F3}");
        System.IO.File.WriteAllText("CaveAutoTest.txt", log.ToString());
    }

    static void Warp(PlayerController pc, TerrainManager tm, float x)
    {
        float g = tm.GetHeightAt(x) ?? tm.GetGroundLineAt(x);
        pc.transform.position = new Vector3(x, g, 0f);
    }
}
#endif

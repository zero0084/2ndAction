#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 天井の確認(2026-10-16、マスターの確認依頼): -qaCeilingAudit <dir>
//  A) 走りながら、ムカデが動く範囲(プレイヤーの 10m 後ろ〜16m 前)の天井の状態を毎フレーム数える(速さ 1 / 2 / 3倍)。
//     天井が続いている所で「天井がまだ作られていない」(生成の範囲の都合)が出ないか。
//  B) ムカデの必殺技(天井を這う)を何度も強制する: 落ちた時の理由が本物の地形(抜けている/高い空洞/低い)だけか、
//     落ちた後に隙が出るか、天井が続いていれば最後まで出るか。
public partial class QaSweep
{
    IEnumerator CeilingAuditMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        HookCaveLogs();
        Random.InitState(20261016);
        BossManager.NetTestBossHpOverride = 50000000;
        yield return BeginRun("swordsman", "natural_cave");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        var tm = TerrainManager.Instance;
        var cave = tm != null ? tm.cave : null;
        Check(cave != null, "audit: natural cave stage active");
        if (cave == null) yield break;
        WarpTo(1500f);
        yield return new WaitForSeconds(1.5f);

        // ---- A: 天井の状態(ムカデの動く範囲)
        var probe = new GameObject("CeilingProbe").AddComponent<CentipedeProbe>();
        float[] speeds = { 1f, 2f, 3f };
        foreach (float sp in speeds)
        {
            PlayerController.DebugSpeedScale = sp;
            var tally = new Dictionary<HazardBossBase.CeilingState, int>();
            int frames = 0, notGenInEnvelope = 0; float aheadMin = 999f; float d0 = gm.MaxDistance;
            float t0 = Time.time;
            while (Time.time - t0 < 40f)
            {
                if (Bm.IsBossPhase) { KillCaveBosses(); yield return null; continue; } // 関門のボスはこの調べの対象外(片付けて走り続ける)
                float px = pc.transform.position.x;
                aheadMin = Mathf.Min(aheadMin, cave.GeneratedEndX - px);
                for (float dx = -10f; dx <= 16f; dx += 2f)
                {
                    var st = probe.StateAt(px + dx);
                    tally[st] = tally.TryGetValue(st, out int c) ? c + 1 : 1;
                    if (st == HazardBossBase.CeilingState.NotGenerated) notGenInEnvelope++;
                }
                frames++;
                yield return null;
            }
            L($"[ceil A] speed x{sp}: {frames} frames over {gm.MaxDistance - d0:F0}m, states in the boss envelope [{string.Join(", ", tally.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}], ceiling generated at least {aheadMin:F0}m ahead of the player");
            Check(notGenInEnvelope == 0, $"audit A x{sp}: the ceiling is always generated where the centipede moves (-10..+16m) ({notGenInEnvelope} unknown samples)");
        }
        PlayerController.DebugSpeedScale = 1f;
        Object.Destroy(probe.gameObject);

        // ---- B: ムカデの必殺技を何度も(普段の自然洞窟の天井)
        int fights = int.Parse(Arg("-qaCeilFights", "6"));
        yield return CeilingFights("B", fights, false);

        // ---- C: 天井が本当に抜けた区間(mode 4)を交互に置いた洞窟で(試験の中だけ区間の選び方を差し替える)
        int toggle = 0;
        cave.sectionPicker = (lx, r) => (toggle++ % 2 == 1) ? 4 : 0;
        WarpTo(gm.MaxDistance + 1500f);
        yield return new WaitForSeconds(1.5f);
        int openFights = int.Parse(Arg("-qaCeilOpenFights", "4"));
        yield return CeilingFights("C", openFights, true);
        cave.sectionPicker = null;
        yield return EndRun();
    }

    IEnumerator CeilingFights(string tag, int fights, bool expectOpen)
    {
        int cuts = 0, completed = 0, cutNoRecovery = 0, badReason = 0, openCuts = 0, notStarted = 0, notGenHolds0 = HazardBossBase.CeilingNotGeneratedHolds;
        var reasons = new List<string>(); var skipWhy = new List<string>(); int skippedByGap = 0;
        for (int i = 0; i < fights; i++)
        {
            float wv = 0f; while ((Bm.IsBossPhase || gm.IsRewardSequenceWaitingForSelection) && wv < 20f) { yield return null; wv += Time.unscaledDeltaTime; }
            Bm.DebugCaveEncounter(CaveBossKind.Centipede, -1);
            yield return WaitBossSpawn(10f);
            var b = CaveBossAlive();
            if (b == null) { L($"[ceil {tag}] #{i + 1}: no centipede"); continue; }
            BossBattle.DebugNoStagger = true;
            b.DebugSetPhase(2);
            yield return new WaitForSeconds(0.8f);
            float w = 0f; while (w < 8f && b.Broken) { w += Time.deltaTime; yield return null; }
            int lost0 = HazardBossBase.CeilingLostInUltimate, rec0 = caveRecoveries.Count, u0 = b.UltimatesUsed, skip0 = HazardBossBase.CeilingSkipCount;
            b.DebugForceUltimate();
            w = 0f; bool retried = false;
            while (w < 8f && !b.UltimateRunning && b.UltimatesUsed == u0) { if (!retried && w > 3f) { retried = true; b.DebugForceUltimate(); } w += Time.deltaTime; yield return null; }
            float us = Time.time;
            w = 0f; while (w < 20f && b.UltimateRunning) { w += Time.deltaTime; yield return null; }
            float ulen = Time.time - us;
            w = 0f; while (w < 4f && caveRecoveries.Count == rec0) { w += Time.deltaTime; yield return null; }
            bool cut = HazardBossBase.CeilingLostInUltimate > lost0;
            bool recovered = caveRecoveries.Count > rec0;
            string why = cut ? HazardBossBase.LastCeilingLost : "-";
            bool started = b.UltimatesUsed > u0 || ulen > 0.05f;
            if (cut) { cuts++; reasons.Add(why); if (!recovered) cutNoRecovery++; if (why.StartsWith("NotGenerated")) badReason++; if (why.StartsWith("Open")) openCuts++; }
            else if (!started) { notStarted++; if (HazardBossBase.CeilingSkipCount > skip0) { skippedByGap++; skipWhy.Add(HazardBossBase.LastCeilingSkip.Split(' ')[0]); } }
            else if (recovered) completed++;
            L($"[ceil {tag}] #{i + 1} at {gm.MaxDistance:F0}m: ultimate {ulen:F1}s, {(cut ? $"cut (ceiling: {why})" : started ? "ran to the end" : HazardBossBase.CeilingSkipCount > skip0 ? $"not started - the ceiling check before the move saw a real gap ({HazardBossBase.LastCeilingSkip})" : "did not start")}, recovery window {(recovered ? "yes" : "NO")}");
            BossBattle.DebugNoStagger = false;
            KillCaveBosses();
            yield return WaitPhaseEnd(30f);
            yield return new WaitForSeconds(1f);
            WarpTo(gm.MaxDistance + 700f + 230f * i); // 配置を変える
            yield return new WaitForSeconds(1f);
        }
        int holds = HazardBossBase.CeilingNotGeneratedHolds - notGenHolds0;
        L($"[ceil {tag}] {fights} ultimates: ran to the end {completed}, cut by the real ceiling {cuts} [{string.Join(", ", reasons)}], did not start {notStarted} (of them, the check before the move saw a real gap: {skippedByGap} [{string.Join(", ", skipWhy)}]), cut without a recovery window {cutNoRecovery}, 'not generated' holds {holds}");
        Check(badReason == 0, $"audit {tag}: a centipede never drops because the ceiling was only not generated yet ({badReason})");
        Check(cutNoRecovery == 0, $"audit {tag}: every ultimate cut by a real ceiling gap is followed by the recovery window ({cutNoRecovery} without)");
        // 始める前の確かめ(張り付く位置の前 12m ほど)が本物の切れ目を見て技を選ばなかった回は、正しい動き(天井の無い所へは這い出さない)
        Check(completed + cuts + skippedByGap >= fights - 1, $"audit {tag}: the ultimates run to the end, end at a real gap with the window, or are not chosen because of a real gap ({completed} + {cuts} + {skippedByGap} of {fights})");
        Check(!skipWhy.Any(w => w.StartsWith("NotGenerated")), $"audit {tag}: the check before the move never refuses because the ceiling was only not generated yet");
        if (expectOpen) Check(openCuts >= 1, $"audit {tag}: where the ceiling really opens, the centipede drops and the window follows ({openCuts} open cuts)");
    }
}

// 天井の状態を調べるだけの入れ物(HazardBossBase.CeilingStateAt は個体のメソッドなので、地面の高さの基準だけ自前で持つ)
public class CentipedeProbe : MonoBehaviour
{
    public HazardBossBase.CeilingState StateAt(float x)
    {
        var tm = TerrainManager.Instance;
        if (tm == null || tm.cave == null) return HazardBossBase.CeilingState.NoCave;
        if (!tm.cave.CeilingKnownAt(x)) return HazardBossBase.CeilingState.NotGenerated;
        float? c = tm.GetEffectiveCeilingHeightAt(x);
        if (!c.HasValue) return HazardBossBase.CeilingState.NotGenerated;
        float g = tm.GetHeightAt(x) ?? (PlayerController.Instance != null ? PlayerController.Instance.transform.position.y : 0f);
        float space = c.Value - g;
        if (tm.cave.CeilingModeAt(x) == 4) return HazardBossBase.CeilingState.Open;
        if (space < 4.2f) return HazardBossBase.CeilingState.Low;
        if (space > 9.5f) return HazardBossBase.CeilingState.High;
        return HazardBossBase.CeilingState.Ok;
    }
}
#endif

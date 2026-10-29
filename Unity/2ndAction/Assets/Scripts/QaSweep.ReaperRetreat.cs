#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

// 死神のボス戦中の退避(2026-10-29): -qaReaperRetreat <dir> [-rrStages wasteland_road,natural_cave,sky_corridor] [-rrChar swordsman]
// 100km の先で死神に追われている時に 101km の関門のボス戦が始まる → 死神は画面の外へ下がって待つ(大鎌の判定なし・強化の時間が進まない・
// 攻撃しない) → 撃破 → 報酬が終わって走りへ戻る → 前の間合い(落ち着く位置より近くはしない)へ戻って追跡を続ける、を確かめる
public partial class QaSweep
{
    IEnumerator ReaperRetreatMode()
    {
        Application.targetFrameRate = 60;
        string[] stages = Arg("-rrStages", "wasteland_road,natural_cave,sky_corridor").Split(',');
        string ch = Arg("-rrChar", "swordsman");
        var tsv = new StringBuilder("stage\tgapBefore\tfracBefore\tretreatSec\toffDuring\tstrikesDuring\tsinceSpawnDrift\tgapRestored\tfracRestored\treturnSec\tcapturedSoon\n");
        BossManager.NetTestBossHpOverride = QaBossHp;
        foreach (var stage in stages)
        {
            string tag = stage;
            yield return BeginRun(ch, stage);
            SetGod(true);
            ReaperBase.DebugNoReap = true;
            GameManager.BlockExpGain = true;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            yield return new WaitForSeconds(1.0f);
            WarpTo(100150f);
            float w = 0f;
            while (ReaperBase.Active == null && w < 15f) { yield return null; w += Time.deltaTime; }
            var r = ReaperBase.Active;
            Check(r != null, $"reaper {tag}: the reaper spawns after 100km");
            if (r == null) { yield return EndRun(); continue; }
            w = 0f; while (r.CurrentPhase != ReaperBase.Phase.Chase && r.CurrentPhase != ReaperBase.Phase.Captured && w < 25f) { yield return null; w += Time.deltaTime; }
            // 関門の手前まで(速く走る)
            SetKmh(150f);
            w = 0f;
            while (!Bm.IsBossPhase && w < 40f) { yield return null; w += Time.deltaTime; }
            Check(Bm.IsBossPhase, $"reaper {tag}: the 101km gate starts ({gm.MaxDistance:F0}m)");
            float gapBefore = r.Gap, fracBefore = r.Gap / Mathf.Max(3f, r.LeftEdgeGap);
            float since0 = r.SinceSpawn; int strikes0 = r.Strikes;
            float tGate = Time.time, retreatSec = -1f;
            w = 0f; while (w < 6f && !(r.Retreated && r.OffScreen)) { yield return null; w += Time.deltaTime; }
            if (r.Retreated && r.OffScreen) retreatSec = Time.time - tGate;
            yield return WaitBossSpawn();
            // 戦いの間(8秒): 画面の外のまま・攻撃しない・強化の時間が進まない
            bool offDuring = true; w = 0f;
            while (w < 8f && Bm.AliveBossCount > 0) { if (!r.OffScreen) offDuring = false; yield return null; w += Time.deltaTime; }
            int strikesDuring = r.Strikes - strikes0;
            float sinceDrift = r.SinceSpawn - since0;
            KillAllBosses();
            yield return WaitPhaseEnd();
            float tEnd = Time.time, returnSec = -1f;
            w = 0f; while (w < 12f && r.Retreated) { yield return null; w += Time.deltaTime; }
            if (!r.Retreated) returnSec = Time.time - tEnd;
            float gapRestored = r.Gap, fracRestored = r.Gap / Mathf.Max(3f, r.LeftEdgeGap);
            // 戻った直後に捕まらない(2秒)
            bool capturedSoon = false; w = 0f;
            while (w < 2f) { if (r.CurrentPhase == ReaperBase.Phase.Captured) capturedSoon = true; yield return null; w += Time.deltaTime; }
            L($"[reaperRetreat] {tag}: gap before {gapBefore:F1}m ({fracBefore:F2}) retreat {retreatSec:F2}s offDuring={offDuring} strikes {strikesDuring} sinceSpawn drift {sinceDrift:F2}s | restored {gapRestored:F1}m ({fracRestored:F2}) after {returnSec:F2}s capturedSoon={capturedSoon} retreats={ReaperBase.Retreats} returns={ReaperBase.ReturnsDone}");
            tsv.Append($"{stage}\t{gapBefore:F1}\t{fracBefore:F2}\t{retreatSec:F2}\t{offDuring}\t{strikesDuring}\t{sinceDrift:F2}\t{gapRestored:F1}\t{fracRestored:F2}\t{returnSec:F2}\t{capturedSoon}\n");
            Check(retreatSec >= 0f && retreatSec < 5f, $"reaper {tag}: the reaper leaves the fight area when the boss fight starts ({retreatSec:F2}s)");
            Check(offDuring && strikesDuring == 0, $"reaper {tag}: no reaper contact/attack during the boss fight (off screen {offDuring}, strikes {strikesDuring})");
            Check(sinceDrift < 0.5f, $"reaper {tag}: the chase is paused during the fight (escalation time +{sinceDrift:F2}s)");
            Check(returnSec >= 0f, $"reaper {tag}: the chase resumes after the reward ({returnSec:F2}s)");
            Check(!capturedSoon && fracRestored >= 0.55f, $"reaper {tag}: the reaper does not come back on top of the player (gap {fracRestored:F2} of the left edge, captured {capturedSoon})");
            Check(Mathf.Abs(fracRestored - Mathf.Max(fracBefore, 0.6f)) < 0.15f, $"reaper {tag}: the gap after the fight matches the gap before ({fracBefore:F2} -> {fracRestored:F2})");
            ReaperBase.DebugNoReap = false;
            SetGod(false); GameManager.BlockExpGain = false; PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "reaper_retreat.tsv"), tsv.ToString());
    }
}
#endif

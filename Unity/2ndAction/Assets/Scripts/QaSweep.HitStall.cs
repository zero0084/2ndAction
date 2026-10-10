#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 攻撃が当たった時の「数秒止まる」(2026-10-07)の確認: -qaHitStall <dir>
//  A 命中の HitStop の最中に、その敵が倒れて消える(FINISH は撃破の瞬間に非表示) → 止まりはその HitStop の長さだけ(以前は約3秒)
//  B 多数への同時命中/連続攻撃/複数同時撃破を繰り返す → HitStop だけの停止が 0.4 秒を超えない、重いフレームが無い、自己修復が働かない
public partial class QaSweep
{
    IEnumerator HitStallMode()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        BossManager.Instance.enabled = false;
        var react = typeof(EnemyController).GetMethod("ReactToHit", BindingFlags.NonPublic | BindingFlags.Instance);
        // ---- A0: 原因の再現(以前の作り = 敵自身のコルーチンで HitStop.Freeze)。その敵が途中で消えると停止が漏れる
        L("== A0: 以前の作り(敵のコルーチンで HitStop)で、途中で消えた時 ==");
        {
            var list = FinishDebug.Spawn("goblin", 1, 4f, 1f);
            yield return null;
            var en = list.FirstOrDefault();
            int h0 = HitStop.LeakHeals;
            en.StartCoroutine(HitStop.Freeze(0.1f));
            yield return null;
            FinishDebug.Kill(list, PlayerAttackKind.Normal, false, false, false);
            float t0 = Time.realtimeSinceStartup, w0 = 0f;
            while (Time.timeScale <= 0f && w0 < 6f) { yield return null; w0 += Time.unscaledDeltaTime; }
            L($"[A0] old style: stopped {Time.realtimeSinceStartup - t0:F2}s (HitStop 0.1s), watchdog self-heals {HitStop.LeakHeals - h0} -> this is the 'few seconds' stop (3s before the watchdog change)");
            yield return new WaitForSecondsRealtime(0.4f);
        }
        int heal0 = HitStop.LeakHeals, stall0 = StallProbe.Stalls;
        StallProbe.ResetLongest(); // A0 の再現の分は数えない

        // ---- A
        L("== A: HitStop の最中に倒れて消える ==");
        float worst = 0f;
        for (int i = 0; i < 6; i++)
        {
            var list = FinishDebug.Spawn("goblin", 1, 4f, 1f);
            yield return null;
            var en = list.FirstOrDefault();
            if (en == null) { Check(false, "A: enemy spawned"); break; }
            en.StartCoroutine((IEnumerator)react.Invoke(en, new object[] { en.transform.position, 0.1f })); // 命中の HitStop(その敵のコルーチン)
            yield return null; // HitStop の途中
            FinishDebug.Kill(list, PlayerAttackKind.Normal, false, false, false); // 同じ敵が倒れて非表示
            float t0 = Time.realtimeSinceStartup;
            float w = 0f;
            while (Time.timeScale <= 0f && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
            float stopped = Time.realtimeSinceStartup - t0;
            worst = Mathf.Max(worst, stopped);
            yield return new WaitForSecondsRealtime(0.4f);
        }
        L($"[A] worst stop after the hit enemy vanished mid-HitStop: {worst:F2}s, self-heals {HitStop.LeakHeals - heal0}");
        Check(worst < 0.35f, $"A: the HitStop ends on time even when the hit enemy vanishes (worst {worst:F2}s, was ~3s)");
        Check(HitStop.LeakHeals == heal0, "A: no leaked HitStop (the watchdog never had to step in)");

        // ---- B
        L("== B: 多数への同時命中/連続撃破 ==");
        float maxDt = 0f; float sumDt = 0f; int frames = 0;
        float longest0 = StallProbe.LongestStopSeconds;
        for (int r = 0; r < 8; r++)
        {
            var list = FinishDebug.Spawn("goblin", 10, 4f, 0.8f);
            yield return null;
            // 半分は命中(生き残る)、半分は同じフレームで撃破。命中した残りも次のフレームで撃破(HitStop の最中)
            for (int i = 0; i < list.Count; i++) if (list[i] != null && i % 2 == 0) list[i].StartCoroutine((IEnumerator)react.Invoke(list[i], new object[] { list[i].transform.position, 0.06f }));
            FinishDebug.Kill(list.Where((e, i) => i % 2 == 1).ToList(), PlayerAttackKind.Normal, r % 3 == 0, r % 4 == 0, false);
            yield return null;
            FinishDebug.Kill(list.Where((e, i) => i % 2 == 0).ToList(), PlayerAttackKind.Up, false, false, false);
            float w = 0f;
            while (w < 0.8f) { yield return null; float dt = Mathf.Max(Time.unscaledDeltaTime, StallProbe.RealDt); w += dt; maxDt = Mathf.Max(maxDt, dt); sumDt += dt; frames++; }
        }
        L($"[B] frame avg {(frames > 0 ? sumDt / frames * 1000f : 0f):F1}ms max {maxDt * 1000f:F0}ms; longest HitStop-only stop {StallProbe.LongestStopSeconds:F2}s; stalls logged {StallProbe.Stalls - stall0} ({StallProbe.LastStall}); self-heals {HitStop.LeakHeals - heal0}");
        Check(StallProbe.LongestStopSeconds < 0.4f, $"B: no long stop from overlapping HitStops (longest {StallProbe.LongestStopSeconds:F2}s)");
        Check(HitStop.LeakHeals == heal0, "B: no leaked HitStop");
        Check(maxDt < 0.25f, $"B: no heavy frame from many hits/kills at once (max {maxDt * 1000f:F0}ms)");
        yield return EndRun();
    }
}
#endif

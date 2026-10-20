#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 死神三姉妹のアニメーションの確認用の撮影(2026-10-20)。 -ldQa <dir> -ldQaMode sisrec
//  30コマ/秒で <dir>/frames/f00000.png … を書き出し、場面ごとのコマの範囲を <dir>/sisrec_ranges.txt に残す(動画/一覧表は外で作る)。
//  各姉妹: 移動 / 技1 / 技2 / 被弾 / 怯み / BREAK と立ち直り / 退く。三人同時: 死の舞踏 / 撃破(三人それぞれ)
public partial class LastDungeonQa
{
    string recDir; int recFrame;
    readonly List<string> recRanges = new List<string>();
    readonly List<string> recPos = new List<string>(); // コマごとの撮る相手の画面上の位置(切り抜き用)
    public static ReaperSisterBoss RecFocus;

    IEnumerator Rec(string label, float seconds, System.Func<bool> stopWhen = null)
    {
        int start = recFrame;
        int n = Mathf.RoundToInt(seconds * 30f);
        for (int i = 0; i < n; i++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(recDir, $"f{recFrame:00000}.png"));
            var foc = RecFocus != null && RecFocus.isActiveAndEnabled ? RecFocus : ReaperSisterBoss.All.FirstOrDefault(x => x != null && !x.IsDead && x.isActiveAndEnabled);
            Vector3 sp = foc != null && Camera.main != null ? Camera.main.WorldToScreenPoint(foc.CenterWorld) : Vector3.zero;
            float hpx = foc != null && Camera.main != null ? Mathf.Abs(Camera.main.WorldToScreenPoint(foc.CenterWorld + Vector3.up * foc.bodyHeight).y - sp.y) * 2f : 0f;
            recPos.Add($"{recFrame}	{sp.x:F0}	{Screen.height - sp.y:F0}	{hpx:F0}");
            recFrame++;
            BotIntercept();
            if (stopWhen != null && i > 75 && stopWhen()) break; // 2.5秒は必ず撮る(棺の口は浮き上がりの間 DebugAct が前の Wait のまま)
        }
        recRanges.Add($"{label}\t{start}\t{recFrame - 1}");
        L($"[sisrec] {label}: frames {start}-{recFrame - 1}");
    }

    IEnumerator SistersRecordMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        ReaperFinaleBattle.DebugHpOverride = 99999;
        ReaperFinaleBattle.ComboFirstDelay = 4f; ReaperFinaleBattle.ComboInterval = 40f;
        recDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(recDir);
        yield return BeginRun(ch);
        var flow = LastDungeonFlow.Instance;
        gm.DebugWarpToDistance(99960f);
        float w = 0f;
        while ((flow == null || flow.Current != LastDungeonFlow.State.Finale || ReaperFinaleBattle.Instance == null) && w < 40f) { yield return null; w += Time.deltaTime; if (flow == null) flow = LastDungeonFlow.Instance; }
        var fin = ReaperFinaleBattle.Instance;
        if (fin == null) { Check(false, "finale started"); yield break; }
        Time.captureFramerate = 30;
        BossBattle.DebugNoStagger = true;
        for (int si = 0; si < 3; si++)
        {
            ReaperSisterBoss b = null;
            w = 0f; while (b == null && w < 15f) { yield return null; w += Time.deltaTime; b = fin.Active.FirstOrDefault(x => x != null && !x.Retreating); }
            if (b == null) break;
            string nm = b.sister.ToString();
            yield return new WaitForSeconds(1.0f);
            if (si == 0) yield return Rec(nm + "_entrance_and_move", 3f);
            // 技(待っている所で指定 → 終わって待つまで)
            for (int mv = 0; mv < 2; mv++)
            {
                ReaperSisterBoss.DebugHold = true;
                w = 0f; while (!b.DebugHolding && w < 12f) { BotIntercept(); yield return null; w += Time.deltaTime; }
                ReaperBullet.ClearAll();
                yield return Rec(nm + "_hold_idle", mv == 0 ? 1.5f : 0.5f);
                int started = b.MovesStarted;
                b.DebugForceMove = mv;
                ReaperSisterBoss.DebugHold = false;
                var bb = b;
                yield return Rec(nm + "_" + ReaperSisterBoss.MoveNames[(int)b.sister][mv], 7f, () => bb.MovesStarted > started && bb.DebugAct == "Wait" && ReaperBullet.Live.Count == 0);
            }
            // 被弾(連続で攻撃)
            ReaperSisterBoss.DebugHold = true;
            w = 0f; while (!b.DebugHolding && w < 12f) { yield return null; w += Time.deltaTime; }
            ReaperBullet.ClearAll();
            StartCoroutine(AttackBurst(1.6f));
            yield return Rec(nm + "_hurt_by_attacks", 2.2f);
            // 怯み(崩しを 45% まで)
            BossBattle.DebugNoStagger = false;
            yield return new WaitForSeconds(1.0f);
            b.DebugAddStagger(b.DebugStaggerMax * 0.45f);
            yield return Rec(nm + "_flinch", 1.6f);
            // BREAK と立ち直り(攻撃しない)。待つ間に補助の攻撃で先に BREAK しないよう、崩しを止めておく(強制の BREAK は効く)
            BossBattle.DebugNoStagger = true;
            yield return new WaitForSeconds(2.6f);
            b.DebugForceBreak();
            yield return Rec(nm + "_break_and_recover", 5.0f);
            BossBattle.DebugNoStagger = true;
            ReaperSisterBoss.DebugHold = false;
            // 退く(前半の HP0)
            yield return new WaitForSeconds(1.0f);
            b.TakeDamage(9999999, b.CenterWorld);
            yield return Rec(nm + "_retreat", 2.0f);
        }
        // 三人同時: 死の舞踏
        w = 0f; while (fin.Active.Count(x => x != null && !x.IsDead) < 3 && w < 20f) { yield return null; w += Time.deltaTime; }
        int c0 = fin.CombosCued;
        w = 0f; while (fin.CombosCued == c0 && w < 20f) { BotIntercept(); yield return null; w += Time.deltaTime; }
        yield return Rec("group_danse_macabre", 7.5f);
        yield return new WaitForSeconds(1.5f);
        // 撃破(一人ずつ)
        ReaperSisterBoss.DebugHold = true;
        foreach (var sister in new[] { ReaperSister.Eldest, ReaperSister.Second, ReaperSister.Youngest })
        {
            var b = fin.Active.FirstOrDefault(x => x != null && !x.IsDead && x.sister == sister);
            if (b == null) continue;
            ReaperBullet.ClearAll();
            yield return new WaitForSeconds(0.8f);
            RecFocus = b;
            foreach (var o in fin.Active) if (o != null && o != b && !o.IsDead) o.DebugSetAlpha(0.12f); // 撃破を見やすく(他の姉妹を薄く)
            b.TakeDamage(9999999, b.CenterWorld);
            yield return Rec(sister + "_defeat", 4.5f);
            foreach (var o in fin.Active) if (o != null && !o.IsDead) o.DebugSetAlpha(1f);
            RecFocus = null;
        }
        ReaperSisterBoss.DebugHold = false;
        Time.captureFramerate = 0;
        System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "sisrec_ranges.txt"), recRanges);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "sisrec_pos.txt"), recPos);
        L($"[sisrec] total {recFrame} frames");
        keepAlive = false;
    }

    IEnumerator AttackBurst(float seconds)
    {
        float t = 0f;
        while (t < seconds) { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); yield return new WaitForSeconds(0.2f); t += 0.2f; }
    }
}
#endif

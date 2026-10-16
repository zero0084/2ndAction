#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ラスダンのボスラッシュのカード選択(2026-10-16 第6弾、マスターの依頼「自然にできた合間に選択」)の確認。
// -qaRewards <dir> -rwPhase ldrushreal [-rwChar swordsman] [-rwBuildLv 5] [-ldRushChoices 0 = 以前の「最後にまとめて」]
// 普通の育成の構成(mix の10枚)、Lv50 から、補助の自動攻撃だけ、無敵。三姉妹戦が始まるまで走る。
//  ・途中(ボスとボスの合間)に開いた選択 / ボスが居る間に開いた選択(0 のはず)/ 全部倒した時に残っていた数 / 三姉妹戦の前に処理した数
//  ・選択を閉じてから次のボスが出るまでの一番短い時間(予告 + 出現の間隔を取っているか)/ 選択の後に1秒以内に続けて出た数
public partial class QaSweep
{
    IEnumerator RwLdRushReal()
    {
        string ch = RwChar;
        int lv = int.Parse(Arg("-rwBuildLv", "5"));
        L($"== last dungeon rush choices: {ch} (mix Lv{lv}, ldRushChoices={BossManager.LdRushChoices}) ==");
        yield return BeginRun(ch, "last_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        foreach (var id in CardBuildPresets.Find("mix")) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, lv); }
        WarpTo(89900f);
        typeof(GameManager).GetMethod("ApplySprintArrivalLevel", RwNP).Invoke(gm, new object[] { 89900f });
        float w = 0f; while (!Bm.RushActive && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Bm.RushActive, "ld: the last dungeon rush starts");
        float t0 = Time.time, limit = float.Parse(Arg("-rwRushSec", "900"));
        int k0 = gm.BossKillCount, lv0 = gm.Level;
        int between = 0, withBoss = 0, afterRush = 0, pendingAtClear = -1, maxQueueAfter = 0;
        bool wasOpen = false, rushWas = true; float lastClose = -99f, minSpawnAfterChoice = 99f; int burstAfterChoice = 0;
        var seen = new HashSet<WildBossBase>(); float lastSpawnT = -99f;
        var times = new List<string>();
        var flow = LastDungeonFlow.Instance;
        while (Time.time - t0 < limit && !(flow != null && flow.Current >= LastDungeonFlow.State.Finale))
        {
            bool open = gm.LevelUpPending && gm.CurrentPendingChoiceKind != GameManager.PendingChoiceKind.BossReward;
            bool rush = Bm.RushActive && Bm.IsBossPhase && !Bm.BossDefeatedThisPhase;
            if (rushWas && !rush && pendingAtClear < 0) pendingAtClear = gm.PendingLevelUpCount + (open ? 1 : 0);
            rushWas = rush;
            if (open && !wasOpen)
            {
                int alive = RwRushAlive().Count;
                if (rush && alive > 0) withBoss++; else if (rush) between++; else afterRush++;
                times.Add($"{Time.time - t0:F0}s{(rush ? (alive > 0 ? "!" : "") : "*")}");
            }
            if (!open && wasOpen) lastClose = Time.unscaledTime;
            wasOpen = open;
            foreach (var b in RwRushAlive())
            {
                if (!seen.Add(b)) continue;
                float sinceChoice = Time.unscaledTime - lastClose;
                if (sinceChoice < 30f) { minSpawnAfterChoice = Mathf.Min(minSpawnAfterChoice, sinceChoice); if (Time.time - lastSpawnT < 1f) burstAfterChoice++; }
                lastSpawnT = Time.time;
            }
            yield return null;
        }
        float total = Time.time - t0;
        bool finale = flow != null && flow.Current >= LastDungeonFlow.State.Finale;
        int leftAtFinale = gm.PendingLevelUpCount + (gm.LevelUpPending ? 1 : 0);
        L($"[ld] {ch} mix Lv{lv}: rush {(pendingAtClear >= 0 ? "cleared" : "NOT cleared")} in {total:F0}s, defeated {gm.BossKillCount - k0}, Lv {lv0}->{gm.Level}, choices between bosses {between}, with a boss out {withBoss}, left when the last boss fell {Mathf.Max(0, pendingAtClear)}, handled after the rush (before the sisters) {afterRush}, still left when the sisters began {leftAtFinale}, sisters waited for choices {Bm.FinaleWaitedForChoices}x");
        L($"[ld] {ch}: choice times [{string.Join(", ", times)}] (! = a boss was out, * = after the rush); shortest choice-close -> next boss {(minSpawnAfterChoice < 99f ? minSpawnAfterChoice.ToString("F1") + "s" : "-")}, bosses within 1s of each other right after a choice {burstAfterChoice}, spacing resets {Bm.RushChoiceSpacingResets}");
        Check(finale, "ld: the sisters' fight begins after the rush");
        Check(withBoss == 0, $"ld: no level-up choice opens while a rush boss is out ({withBoss})");
        Check(leftAtFinale == 0, $"ld: every level-up choice is handled before the sisters' fight ({leftAtFinale} left)");
        if (BossManager.LdRushChoices)
            Check(minSpawnAfterChoice >= 2f || minSpawnAfterChoice >= 99f, $"ld: after a choice the next boss keeps its warning and spacing (shortest {minSpawnAfterChoice:F1}s)");
        yield return EndRun();
    }
}
#endif

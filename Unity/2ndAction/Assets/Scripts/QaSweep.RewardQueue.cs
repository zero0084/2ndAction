#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 2026-10-21 追加調査: -qaRewardQueue <dir>
//   ボスの報酬の選択が後回しになっている間に、もう1体のボスが倒された時、選択が倒した数だけ出るか。
//   (以前は後回しが「ある/ない」の1つだけで、2回目の分が消えていた。150km/h で CPU の仲間が次のボスをすぐ倒した時に起きた)
//   レベルアップの選択を開いた状態(後回しになる条件)で、撃破の報酬の選択の呼び出しを 1/2/3 回行い、閉じた後に出た選択の数を数える
public partial class QaSweep
{
    IEnumerator RewardQueueMode()
    {
        foreach (bool legacy in new[] { true, false })
        foreach (int n in new[] { 1, 2, 3 })
        {
            GameManager.QaLegacyRewardMerge = legacy;
            yield return BeginRun("swordsman", "wasteland_road");
            SetGod(true);
            GameManager.BlockExpGain = true;
            yield return new WaitForSecondsRealtime(1.5f);
            var lp = typeof(GameManager).GetField("levelUpPending", NP);
            int started0 = gm.BossRewardChoicesStarted;
            // 後回しの条件(選択の画面が開いている)を作ってから、撃破の報酬の選択を n 回呼ぶ
            autoPickHold = true;
            lp.SetValue(gm, true);
            for (int i = 0; i < n; i++) gm.TriggerBossRewardChoice();
            int extra = gm.BossRewardDeferredExtra; bool pending = gm.BossRewardDeferredPending;
            yield return new WaitForSecondsRealtime(0.3f);
            lp.SetValue(gm, false);
            autoPickHold = false;
            float w = 0f;
            while (w < 25f && (gm.BossRewardChoicesStarted - started0 < n || gm.IsRewardSequenceRunning || gm.BossRewardDeferredPending)) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1f);
            int got = gm.BossRewardChoicesStarted - started0;
            L($"[rewardqueue] {(legacy ? "before" : "after ")} calls={n} deferredPending={pending} queuedExtra={extra} choicesShown={got} stillPending={gm.BossRewardDeferredPending} wait={w:F1}s");
            if (!legacy) Check(got == n, $"rewardqueue: {n} boss reward(s) deferred together -> {n} choice(s) shown (got {got})");
            GameManager.BlockExpGain = false;
            SetGod(false);
            yield return EndRun();
        }
        GameManager.QaLegacyRewardMerge = false;
    }
}
#endif

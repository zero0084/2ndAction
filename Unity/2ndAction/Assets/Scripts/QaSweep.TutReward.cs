#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 初回チュートリアルの完了報酬(2026-11-26 依頼⑧)の確認。保存は -fileSave の専用ファイル(本物のセーブに触れない)。ホームでの付与を動かすため -tutorialRewardOn
//   -qaTutReward <dir> -trwPhase new|restart|legacySeed|legacy -tutorialRewardOn
//  new        : 新しいデータ。練習を最後まで → 練習中は MILE を加えない → ホームで +5,000(倍率なし)・受け取り済み・お知らせ →
//               もう一度練習を最後まで(再プレイ)→ 増えない → ガチャを10回引ける(未出発のまま。分類/解放で止まらない)→ MILE は元に戻る
//  restart    : new の後の本当の再起動(同じファイル)→ 増えない・受け取り済みのまま
//  legacySeed : 既存プレイヤーのデータを作る(練習が済み・報酬のキー無し = 更新前のデータ)。保存して終わる
//  legacy     : legacySeed の後の起動 → 起動時に「既存プレイヤーの受け取り待ち」→ ホームで1回だけ +5,000 + 既存プレイヤー向けのお知らせ → 画面の開き直しで増えない
public partial class QaSweep
{
    IEnumerator TutRewardModeQa()
    {
        yield return WaitHome();
        while (TitleScreen.Showing) yield return null;
        string ph = Arg("-trwPhase", "new");
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        gm = GameManager.Instance;
        int state0 = TutorialClearReward.State;
        L($"[trw] phase={ph} state={state0} practiceDone={TutorialProgress.PracticeDone} wallet={gm.TotalOwnedMile} boot={SaveSystem.LastBoot?.kind}");

        if (ph == "legacySeed")
        {
            SaveStore.SetInt(TutorialProgress.PracticeDoneKey, 1);
            SaveStore.DeleteKey(TutorialClearReward.RewardKey);
            SaveStore.SetInt(GameManager.TotalMileKey, 1234);
            SaveStore.Save();
            Check(!SaveStore.HasKey(TutorialClearReward.RewardKey) && TutorialProgress.PracticeDone, "legacySeed: an old save (practice done, no reward key)");
            yield break;
        }
        if (ph == "legacy")
        {
            Check(SaveSystem.LastBoot != null, "legacy: boot ran");
            float w = 0f; while (TutorialClearReward.State != TutorialClearReward.StateClaimed && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            Check(TutorialClearReward.State == TutorialClearReward.StateClaimed, $"legacy: claimed at home ({TutorialClearReward.LastResult})");
            Check(gm.TotalOwnedMile == 1234 + TutorialClearReward.Amount, $"legacy: +5,000 once (wallet {gm.TotalOwnedMile})");
            Check(TutorialClearReward.GrantsThisSession == 1, $"legacy: granted once ({TutorialClearReward.GrantsThisSession})");
            yield return new WaitForSecondsRealtime(1f);
            Shot("trw_legacy_notice");
            NoticeQueue.QaAutoAcknowledge = true;
            yield return ReloadHome();
            yield return new WaitForSecondsRealtime(2f);
            Check(GameManager.Instance.TotalOwnedMile == 1234 + TutorialClearReward.Amount && TutorialClearReward.GrantsThisSession == 1, $"legacy: reopening the home does not add again ({GameManager.Instance.TotalOwnedMile})");
            yield break;
        }
        if (ph == "restart")
        {
            yield return new WaitForSecondsRealtime(3f);
            Check(TutorialClearReward.State == TutorialClearReward.StateClaimed && TutorialClearReward.GrantsThisSession == 0, $"restart: still claimed, nothing added (state {TutorialClearReward.State}, grants {TutorialClearReward.GrantsThisSession})");
            Check(gm.TotalOwnedMile == SaveStore.GetInt(GameManager.TotalMileKey, -1), $"restart: wallet as saved ({gm.TotalOwnedMile})");
            yield break;
        }

        // ---- new
        Check(state0 == TutorialClearReward.StateNone && !TutorialProgress.PracticeDone, $"new: a new save has nothing to claim yet (state {state0})");
        int wallet0 = gm.TotalOwnedMile;
        yield return TrwPlayTutorial("first");
        Check(TutorialClearReward.State == TutorialClearReward.StatePending || TutorialClearReward.State == TutorialClearReward.StateClaimed, $"new: completing the practice -> pending/claimed ({TutorialClearReward.State})");
        float wg = 0f; while (TutorialClearReward.State != TutorialClearReward.StateClaimed && wg < 8f) { yield return null; wg += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        Check(TutorialClearReward.State == TutorialClearReward.StateClaimed, $"new: claimed when back at home ({TutorialClearReward.LastResult})");
        Check(gm.TotalOwnedMile == wallet0 + TutorialClearReward.Amount, $"new: +5,000 exactly (no multiplier) ({wallet0} -> {gm.TotalOwnedMile})");
        Check(SaveStore.GetInt(GameManager.TotalMileKey, -1) == gm.TotalOwnedMile && SaveStore.GetInt(TutorialClearReward.RewardKey, -1) == TutorialClearReward.StateClaimed, "new: MILE and the claimed mark are saved together");
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("trw_new_toast");
        int wallet1 = gm.TotalOwnedMile;
        // 再プレイ
        yield return TrwPlayTutorial("replay");
        yield return new WaitForSecondsRealtime(3f);
        gm = GameManager.Instance;
        Check(gm.TotalOwnedMile == wallet1 && TutorialClearReward.GrantsThisSession == 1, $"new: replaying the tutorial gives nothing more ({wallet1} -> {gm.TotalOwnedMile}, grants {TutorialClearReward.GrantsThisSession})");
        // ガチャ10回(未出発のまま)
        int pulls0 = gm.GachaPulls, ok = 0;
        var pull = typeof(GameManager).GetMethod("GachaPullOnce", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        for (int i = 0; i < 10; i++) { if ((bool)pull.Invoke(gm, null)) ok++; yield return null; }
        L($"[trw] gacha: {ok}/10 pulls, category {gm.GachaCategory}, wallet {gm.TotalOwnedMile}, refused(no MILE) {gm.GachaRefusedNoMile}");
        Check(ok == 10 && gm.GachaPulls - pulls0 == 10, $"new: 10 gacha pulls work right after (before any departure) ({ok}/10, category {gm.GachaCategory})");
        Check(gm.TotalOwnedMile == wallet1 - 10 * GameManager.GachaCostMile, $"new: 10 pulls cost 5,000 ({wallet1} -> {gm.TotalOwnedMile})");
    }

    IEnumerator TrwPlayTutorial(string tag)
    {
        NoticeQueue.QaAutoAcknowledge = true;
        bool launched = TutorialLauncher.Launch(false, "qa " + tag);
        Check(launched, $"{tag}: the tutorial starts");
        float w = 0f; while (TutorialRun.Instance == null && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        var tr = TutorialRun.Instance;
        if (tr == null) { Check(false, $"{tag}: no tutorial run"); yield break; }
        int walletIn = GameManager.Instance.TotalOwnedMile;
        int guard = 0;
        while (tr.CurrentStep != TutorialRun.Step.End && guard++ < 20) { yield return new WaitForSecondsRealtime(0.3f); tr.SkipStep(); }
        yield return new WaitForSecondsRealtime(0.5f);
        Check(GameManager.Instance.TotalOwnedMile == walletIn, $"{tag}: no MILE is added inside the tutorial ({walletIn} -> {GameManager.Instance.TotalOwnedMile})");
        L($"[trw] {tag}: tutorial ended (step {tr.CurrentStep}), state {TutorialClearReward.State}");
        TutorialLauncher.Exit(false, "qa " + tag);
        w = 0f; while ((TutorialMode.Active || TutorialLauncher.Covering || GameManager.Instance == null) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 画面の重なり順と入力(2026-10-14、実機の不具合2件): 専用の保存(-fileSave / -memSave)で行う。
//  -qaLayers <dir> [-lyPhase all|run|stage|tap] [-lyReps 5] [-cpuAllies archer,mage]
//   run   … 自然洞窟で障害物の赤い縁が出ている所で、カード選択 / ポーズ / 設定 を開閉(5回)。開いている間は縁も名前の札も出ない、閉じると戻る。「!」は無い
//   stage … マップ選択で CPU サポートの一覧を開閉(5回)。開いている間は疾走出発のボタンを描かない・押せない。閉じると戻る
//   tap   … 実機/エミュレーター用(外の adb が tap_req.txt を読んで本当に押す): サポートのボタン → 決定。決定で疾走出発の一覧が開かない・出発しない
public partial class QaSweep
{
    IEnumerator LayersMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active || SaveProfile.IsTest;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        string phase = Arg("-lyPhase", "all");
        int reps = int.Parse(Arg("-lyReps", "5"));
        UnlockRules.DevUnlockAll = true;
        L($"[ly] phase={phase} screen {Screen.width}x{Screen.height} {Screen.orientation}");
        if (phase == "all" || phase == "run") yield return LayersRun(reps);
        if (phase == "all" || phase == "stage") yield return LayersStage(reps);
        if (phase == "tap") yield return LayersTap(reps);
        if (Arg("-lyRotate", "0") == "1") OrientationControl.Apply(OrientationControl.Mode.Landscape);
    }

    // -lyRotate 1(実機/エミュレーター): 繰り返すたびに横/縦を切り替える(本当の画面の回転)
    IEnumerator RotateFor(int i)
    {
        if (Arg("-lyRotate", "0") != "1") yield break;
        var m = i % 2 == 0 ? OrientationControl.Mode.Landscape : OrientationControl.Mode.Portrait;
        int w0 = Screen.width;
        OrientationControl.Apply(m);
        float w = 0f; while (w < 4f && ((m == OrientationControl.Mode.Portrait) != (Screen.height > Screen.width))) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        L($"[ly] rotated to {m}: screen {Screen.width}x{Screen.height} after {w:F1}s");
        Check((m == OrientationControl.Mode.Portrait) == (Screen.height > Screen.width), $"rotation to {m} happened");
    }

    // 時間と、最低のフレーム数の両方を待つ(処理の重いエミュレーターでは 0.3秒が 1〜2 フレームしかなく、「直前のフレームに描いた」を拾っていた)
    IEnumerator Settle(float seconds)
    {
        int f0 = Time.frameCount; float w = 0f;
        while (w < seconds || Time.frameCount - f0 < 6) { yield return null; w += Time.unscaledDeltaTime; }
    }

    bool RimsShown => ObstacleWarning.Instance != null && ObstacleWarning.Instance.VisibleRims > 0;
    bool LabelsDrawnRecently => Time.frameCount - ParticipantLabels.LastLabelFrame <= 2;

    IEnumerator WaitRims(float timeout)
    {
        float w = 0f;
        while (!RimsShown && w < timeout)
        {
            // 待っている間に本物の LEVEL UP が来たら選ぶ(開いている間は縁を出さないのが正しいので)
            if (gm.IsRewardSequenceWaitingForSelection) { var ids = gm.DebugPendingChoiceIds; if (ids.Length > 0) { L("[ly] (a real level up came while waiting - picked)"); yield return PickInChoice(ids[0]); } }
            yield return null; w += Time.unscaledDeltaTime;
        }
    }

    IEnumerator LayersRun(int reps)
    {
        yield return BeginRun("swordsman", "natural_cave");
        if (!gm.InvincibleMode) gm.DebugToggleInvincible();
        autoPickHold = true;
        WarpTo(3200f);
        yield return new WaitForSecondsRealtime(1.5f);
        for (int i = 0; i < reps; i++)
        {
            yield return RotateFor(i);
            // 縁が出るまで走る(近づいた障害物/天井の針)
            yield return WaitRims(25f);
            bool had = RimsShown;
            L($"[ly] run #{i + 1}: rims {ObstacleWarning.Instance.VisibleRims}, warned {ObstacleWarning.WarnedNow}, labels drawn {LabelsDrawnRecently}, at {gm.MaxDistance:F0}m");
            if (!had) { Check(false, $"run #{i + 1}: a hazard rim showed up to test with"); continue; }
            if (i == 0) { Shot("ly_run_before_choice"); yield return null; yield return null; }
            // 解放のお知らせ(画面の上の方の帯)も出しておく: カード選択の上に重ならないこと
            var toast = ScriptableObject.CreateInstance<UnlockDefinition>(); toast.displayName = "QA TEST UNLOCK";
            gm.DebugShowUnlockToast(toast);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(Time.frameCount - gm.LastUnlockToastFrame <= 2, $"run #{i + 1}: the unlock toast is shown during the run");

            // カード選択
            gm.DebugTriggerLevelUp();
            yield return WaitChoiceOpen();
            yield return Settle(0.4f);
            Check(Time.frameCount - gm.LastUnlockToastFrame > 2, $"run #{i + 1}: card choice open -> the unlock toast is not drawn over the cards");
            Check(gm.RunScreenOpen && !RimsShown && ObstacleWarning.HiddenByScreen, $"run #{i + 1}: card choice open -> no hazard rim ({ObstacleWarning.Instance.VisibleRims})");
            Check(!LabelsDrawnRecently, $"run #{i + 1}: card choice open -> no name labels");
            if (i == 0) { Shot("ly_run_card_choice"); yield return null; yield return null; }
            // 選んで閉じる(続けて本物の LEVEL UP が来ていれば、それも選ぶ)
            for (int k = 0; k < 4 && (gm.DebugChoiceOpen || gm.IsRewardSequenceRunning); k++)
            {
                var ids = gm.DebugPendingChoiceIds;
                if (ids.Length > 0) yield return PickInChoice(ids[0]); else yield return new WaitForSecondsRealtime(0.3f);
                float wc = 0f; while (gm.IsRewardSequenceRunning && !gm.IsRewardSequenceWaitingForSelection && wc < 3f) { yield return null; wc += Time.unscaledDeltaTime; }
            }
            yield return new WaitForSecondsRealtime(0.3f);
            // 2026-10-15: 閉じた直後の 0.3秒に本物の LEVEL UP がもう一度来ることがある(走りながらの経験値)→ それも選んでから確かめる
            for (int k = 0; k < 3 && (gm.DebugChoiceOpen || gm.IsRewardSequenceRunning); k++)
            {
                L($"[ly] run #{i + 1}: another real level up opened right after closing - picked");
                var ids2 = gm.DebugPendingChoiceIds;
                if (ids2.Length > 0) yield return PickInChoice(ids2[0]); else yield return new WaitForSecondsRealtime(0.3f);
                float wc2 = 0f; while (gm.IsRewardSequenceRunning && wc2 < 3f) { yield return null; wc2 += Time.unscaledDeltaTime; }
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Check(!gm.RunScreenOpen, $"run #{i + 1}: card choice closed");
            Check(gm.RunScreenOpen || Time.frameCount - gm.LastUnlockToastFrame <= 2, $"run #{i + 1}: after the choice the unlock toast comes back");

            // ポーズ
            typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, true);
            yield return Settle(0.3f);
            Check(!RimsShown && !LabelsDrawnRecently, $"run #{i + 1}: pause menu open -> no rim / labels");
            if (i == 0) { Shot("ly_run_pause"); yield return null; yield return null; }
            typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, false);

            // 設定
            SettingsPanel.OpenStatic();
            yield return Settle(0.5f);
            Check(UiInputGate.ModalOpen && !RimsShown && !LabelsDrawnRecently, $"run #{i + 1}: settings open -> no rim / labels");
            if (i == 0) { Shot("ly_run_settings"); yield return null; yield return null; }
            SettingsPanel.CloseStatic();
            float w = 0f; while (SettingsPanel.IsVisible && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }

            // 閉じた後: 縁を隠す状態は解ける。先読みの範囲に障害物があれば、その縁が出る(止めている間に通り過ぎた物は出ない)
            yield return new WaitForSecondsRealtime(0.4f);
            L($"[ly] run #{i + 1}: after closing, rims {ObstacleWarning.Instance.VisibleRims}, in range {ObstacleWarning.WarnedNow}");
            if (gm.RunScreenOpen) L($"[ly] run #{i + 1}: another screen opened right away (choice {gm.DebugChoiceOpen}, reward seq {gm.IsRewardSequenceRunning}, modal {UiInputGate.ModalOpen}) - rims correctly hidden");
            else Check(!ObstacleWarning.HiddenByScreen && ObstacleWarning.Instance.VisibleRims == ObstacleWarning.WarnedNow, $"run #{i + 1}: after the screens close, every hazard in range shows its rim again ({ObstacleWarning.Instance.VisibleRims}/{ObstacleWarning.WarnedNow})");
        }
        autoPickHold = false;
        stopKeepAlive = true;
        yield return ReloadHome(); // ホームへ(EndRun の Retry は次のランを始めるので使わない)
    }

    bool SprintButtonDrawn => Time.frameCount - SprintDeparturePanel.LastButtonFrame <= 2;

    IEnumerator OpenStageSelectForTest()
    {
        // ランの後はシーンを読み直すので、新しい GameManager を待つ
        float w0 = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        while (NoticeQueue.Open && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        if (!gm.StageSelectIsOpen) gm.OpenStageSelect();
        float w = 0f; while (!gm.StageSelectIsOpen && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.0f);
    }

    IEnumerator LayersStage(int reps)
    {
        yield return OpenStageSelectForTest();
        Check(SprintButtonDrawn, $"stage select: the sprint button is drawn ({SprintDeparturePanel.LastButtonRect})");
        for (int i = 0; i < reps; i++)
        {
            yield return RotateFor(i);
            SupportDeparturePanel.DebugOpen(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(SupportDeparturePanel.Open && !SprintButtonDrawn, $"#{i + 1}: support list open -> the sprint button is not drawn (last frame {SprintDeparturePanel.LastButtonFrame}, now {Time.frameCount})");
            Check(UiInputGate.Blocked, $"#{i + 1}: support list open -> the stage select behind does not take taps");
            if (i == 0) { Shot("ly_stage_support_open"); yield return null; yield return null; }
            SupportDeparturePanel.DebugOpen(false);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(!SupportDeparturePanel.Open && SprintButtonDrawn, $"#{i + 1}: support list closed -> the sprint button is back");
        }
        Shot("ly_stage_closed"); yield return null; yield return null;
        gm.CloseStageSelect();
        yield return new WaitForSecondsRealtime(0.5f);
    }

    // 実機/エミュレーター: 本当に押す(画面の座標は左上が原点)
    IEnumerator LayersTap(int reps)
    {
        yield return OpenStageSelectForTest();
        int sprintOpens0 = SprintDeparturePanel.Opens;
        for (int i = 0; i < reps; i++)
        {
            yield return RotateFor(i);
            yield return TapAndWait($"support{i + 1}", SupportDeparturePanel.LastSummaryRect, () => SupportDeparturePanel.Open, 120f);
            Check(SupportDeparturePanel.Open, $"tap #{i + 1}: the support button opens the list");
            yield return new WaitForSecondsRealtime(0.6f);
            if (i == 0) { Shot("ly_tap_support_open"); yield return null; yield return null; }
            Rect d = SupportDeparturePanel.LastDecideRect, sp = SprintDeparturePanel.LastButtonRect;
            L($"[ly] decide {d} / sprint button (behind) {sp} overlap {d.Overlaps(sp)}");
            int decides = SupportDeparturePanel.Decides;
            yield return TapAndWait($"decide{i + 1}", d, () => !SupportDeparturePanel.Open, 120f);
            yield return new WaitForSecondsRealtime(0.8f);
            Check(!SupportDeparturePanel.Open && SupportDeparturePanel.Decides == decides + 1, $"tap #{i + 1}: 'decide' closes the list");
            Check(!SprintDeparturePanel.Open && SprintDeparturePanel.Opens == sprintOpens0 && !gm.HasStarted && gm.StageSelectIsOpen,
                $"tap #{i + 1}: the sprint departure behind did not react (sprint opens {SprintDeparturePanel.Opens - sprintOpens0}, started {gm.HasStarted})");
            Check(SprintButtonDrawn, $"tap #{i + 1}: the sprint button is back after closing");
        }
        Shot("ly_tap_after"); yield return null; yield return null;
        gm.CloseStageSelect();
        yield return new WaitForSecondsRealtime(0.5f);
    }
}
#endif

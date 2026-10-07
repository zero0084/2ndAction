#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// テスト用データ/初回チュートリアル(2026-10-07)の自動テスト: -qaTutorial <dir> [-qaTutOnly PTGR]
//  P テスト用データ: 新規ユーザーの初期状態 / 通常のデータのキーへ書かない / 設定は共有 / 落ちても(印を読み直しても)テスト用のまま /
//    リセットはテスト用だけ / 通常へ戻ると通常の値 / 自動保存(MILE/累計距離)が反対側へ書かない
//  T 練習: 初回の扉の勧め → 6段階を順に(説明の間は止まる・「やってみる」の指は攻撃にならない)、ダメージ/距離/報酬/記録/選択中のキャラ/デッキ
//    を書かない、終わると通常のランの出発(ステージ選択)。設定の「遊び方」からはホームへ戻る、中断中のランは残る、練習をやめる
//  G 初回の案内: 脱出できるようになったら1回だけ止めて説明 → 初めての脱出の後のホームでMILEの案内(各1回)
//  R 倒れた時の結果画面(MILEは増えない、失ったもの/残るものの表示)のスクリーンショット
public partial class QaSweep
{
    static string Raw(string k) => PlayerPrefs.HasKey(k) ? $"{PlayerPrefs.GetInt(k, int.MinValue)}|{PlayerPrefs.GetFloat(k, float.NaN)}|{PlayerPrefs.GetString(k, "")}" : "(none)";

    IEnumerator TutorialModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        string only = Arg("-qaTutOnly", "PTGR");
        bool wasTest = SaveProfile.IsTest;
        if (wasTest) { SaveProfile.Switch(false, false); yield return ReloadHome(); }
        // 通常のデータの全キー(生の値)を控える。テストの最後に1つも変わっていないことを確かめる
        var normalKeys = SaveKeys.Expanded().Select(e => e.key).Distinct().ToList();
        var normalRaw = normalKeys.ToDictionary(k => k, Raw);

        if (only.Contains('P')) yield return TutProfile(normalRaw);
        if (only.Contains('T')) yield return TutPractice();
        if (only.Contains('G')) yield return TutGuides();
        if (only.Contains('R')) yield return TutDeathResult();

        // 後片付け: テスト用データを消して通常へ
        if (SaveProfile.IsTest) SaveProfile.Switch(false, true); else SaveProfile.DeleteTestData();
        yield return ReloadHome();
        int changed = 0;
        foreach (var k in normalKeys)
        {
            // テストの間に通常のデータで走らせた物は無い(全部テスト用データの中)。BEST/累計は通常のデータのまま
            string now = Raw(k);
            if (now != normalRaw[k]) { changed++; L($"[P] normal key changed: {k} '{normalRaw[k]}' -> '{now}'"); }
        }
        Check(changed == 0, $"END: the normal data is byte-for-byte unchanged after all test-data/tutorial tests ({changed} keys changed)");
        Check(!PlayerPrefs.HasKey("test:TotalOwnedMile") && !PlayerPrefs.HasKey(SaveProfile.TestKeysKey), "END: no test keys left behind");
    }

    IEnumerator ReloadHome()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        yield return null; yield return null;
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        pc = PlayerController.Instance;
    }

    // ===================================================================== P
    IEnumerator TutProfile(System.Collections.Generic.Dictionary<string, string> normalRaw)
    {
        L("== P: test data profile ==");
        int normalMile = PlayerPrefs.GetInt("TotalOwnedMile", 0);
        double normalLife = ProgressStats.LifetimeDistance;
        float vol0 = PlayerPrefs.GetFloat("MasterVolume", -1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        Check(SaveProfile.IsTest, "P: switched to the test data");
        // 画面の確認: DEBUG のテストデータのページ / 設定の「遊び方」(下までスクロール)
        yield return new WaitForSecondsRealtime(1.5f);
        DebugPanel.OpenStatic();
        yield return null;
        SetPrivate(DebugPanel.Instance, "page", 11);
        L($"[P] debug panel open={DebugPanel.IsOpen}");
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("debug_testdata_page");
        yield return new WaitForSecondsRealtime(0.3f); // 撮影はフレームの終わり: 閉じる前に1フレーム以上待つ
        DebugPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.3f);
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.4f);
        SetPrivate(SettingsPanel.Instance, "scroll", new Vector2(0f, 9999f));
        yield return new WaitForSecondsRealtime(0.2f);
        Shot("settings_howto");
        SettingsPanel.Instance.Close();
        yield return new WaitForSecondsRealtime(0.5f);
        L($"[P] test: MILE={gm.TotalOwnedMile} lifetime={ProgressStats.LifetimeDistance} best={gm.BestDistance} char={gm.SelectedCharacterId} stage={SaveStore.GetString("SelectedStageId", "")} deck={SaveStore.GetString("DeckCardIds", "")}");
        Check(gm.TotalOwnedMile == 0 && ProgressStats.LifetimeDistance == 0 && gm.BestDistance <= 0f, $"P: new-user state: MILE 0 / lifetime 0 / BEST 0 (MILE {gm.TotalOwnedMile}, life {ProgressStats.LifetimeDistance}, best {gm.BestDistance})");
        Check(!ProgressStats.FinalDungeonAvailable && !GachaStage.DevAllCardsOpen && !SprintRecords.DevUnlockAll, "P: no dev unlock-all in the test data (last dungeon / all cards / sprint)");
        Check(!RunCheckpoint.HasActiveRun, "P: no CONTINUE in fresh test data");
        Check(TutorialProgress.ShouldOfferOnDoor, "P: a fresh test profile gets the first-run practice offer");
        Check(PlayerPrefs.GetInt("TotalOwnedMile", 0) == normalMile, "P: the normal MILE key is untouched by the switch");
        // 自動保存(MILE / 累計距離)がテスト用データへだけ書かれる
        gm.AddMile(777);
        ProgressStats.DevSetLifetime(1234);
        Check(PlayerPrefs.GetInt("test:TotalOwnedMile", -1) == 777 && PlayerPrefs.GetInt("TotalOwnedMile", 0) == normalMile, $"P: MILE in test data goes to test:TotalOwnedMile only (test {PlayerPrefs.GetInt("test:TotalOwnedMile", -1)}, normal {PlayerPrefs.GetInt("TotalOwnedMile", 0)} = {normalMile})");
        Check(PlayerPrefs.GetString("test:" + SaveKeys.LifetimeDistance, "") == "1234" && System.Math.Abs(ProgressStats.ReadDouble(SaveKeys.LifetimeDistance) - 1234) < 0.5, "P: lifetime distance goes to the test data only");
        // 設定は共有
        float vNew = vol0 < 0.5f ? 0.8f : 0.3f;
        if (AudioManager.Instance != null) AudioManager.Instance.SetMasterVolume(vNew, true);
        Check(Mathf.Abs(PlayerPrefs.GetFloat("MasterVolume", -1f) - vNew) < 0.01f && !PlayerPrefs.HasKey("test:MasterVolume"), "P: settings (volume) are shared, not copied into the test data");
        if (AudioManager.Instance != null) { if (vol0 >= 0f) AudioManager.Instance.SetMasterVolume(vol0, true); else PlayerPrefs.DeleteKey("MasterVolume"); }
        // アプリが落ちた(メモリ上の印を捨てて読み直す) → テスト用のまま
        SaveProfile.ResetCacheForTests();
        Check(SaveProfile.IsTest && SaveStore.GetInt("TotalOwnedMile", 0) == 777, "P: survives an app kill (the profile flag is read back from storage, test values still there)");
        yield return ReloadHome();
        Check(gm.TotalOwnedMile == 777, $"P: after a scene reload the game reads the test values (MILE {gm.TotalOwnedMile})");
        // リセット(テスト用だけ)
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        Check(gm.TotalOwnedMile == 0 && ProgressStats.LifetimeDistance == 0 && PlayerPrefs.GetInt("TotalOwnedMile", 0) == normalMile, $"P: reset clears only the test data (test MILE {gm.TotalOwnedMile}, normal {PlayerPrefs.GetInt("TotalOwnedMile", 0)})");
        // 通常へ戻る
        gm.AddMile(55);
        SaveProfile.Switch(false, false);
        yield return ReloadHome();
        Check(!SaveProfile.IsTest && gm.TotalOwnedMile == normalMile && System.Math.Abs(ProgressStats.LifetimeDistance - normalLife) < 1.0, $"P: back to the normal data: MILE {gm.TotalOwnedMile} (= {normalMile}), lifetime {ProgressStats.LifetimeDistance:F0} (= {normalLife:F0})");
        Check(PlayerPrefs.GetInt("test:TotalOwnedMile", -1) == 55, "P: the test data is kept while using the normal data (continue later)");
        // 通常のデータの時にテスト用だけを消す
        SaveProfile.DeleteTestData();
        Check(!PlayerPrefs.HasKey("test:TotalOwnedMile") && PlayerPrefs.GetInt("TotalOwnedMile", 0) == normalMile, "P: 'reset test data' from the normal data deletes only test keys");
        int diff = normalRaw.Count(kv => Raw(kv.Key) != kv.Value && kv.Key != "MasterVolume");
        Check(diff == 0, $"P: no normal key changed during the profile tests ({diff})");
    }

    // ===================================================================== T
    TutorialRun Tut => TutorialRun.Instance;

    IEnumerator WaitTut(System.Func<bool> cond, float max)
    {
        float w = 0f;
        while (!cond() && w < max) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator TutPress(GameAction a, float after = 0.12f)
    {
        GameInput.Inject(a);
        yield return null;
        yield return new WaitForSecondsRealtime(after);
    }

    IEnumerator TutPractice()
    {
        L("== T: practice ==");
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        string ch0 = gm.SelectedCharacterId == "swordsman" ? "dual_blade" : gm.SelectedCharacterId;
        gm.SetSelectedCharacter(ch0);
        string deck0 = SaveStore.GetString("DeckCardIds", "");
        var prog0 = SaveSystem.CaptureCategory(SaveCategory.Progress).Where(i => !i.k.StartsWith("Tutorial.")).Select(i => i.k + "=" + i.v).ToList();
        // 初回の扉 → 勧め
        bool shown = FirstRunGuide.TryDoorPrompt();
        Check(shown && FirstRunGuide.Showing == FirstRunGuide.Kind.DoorPrompt && UiInputGate.Blocked, "T: first door in new data shows 'practice / start' (input behind it blocked)");
        Shot("tut_door_prompt");
        yield return new WaitForSecondsRealtime(0.3f);
        FirstRunGuideDebug.Answer(0);
        yield return WaitTut(() => Tut != null && TutorialMode.Active && !TutorialLauncher.Covering, 20f);
        Check(Tut != null && TutorialMode.Active, "T: practice started from the first-door offer");
        Check(TutorialProgress.Offered, "T: the offer is recorded (not shown again)");
        Check(!FirstRunGuide.TryDoorPrompt(), "T: the offer does not repeat");
        if (Tut == null) yield break;
        gm = GameManager.Instance;
        pc = PlayerController.Instance;
        yield return new WaitForSecondsRealtime(0.4f);
        Check(Tut.CurrentStep == TutorialRun.Step.Attack && TutorialRun.PanelOpen && Time.timeScale == 0f, $"T: step 1 explanation pauses the game (ts {Time.timeScale})");
        Check(gm.SelectedCharacterId == TutorialRun.CharacterId, "T: practice uses the swordsman");
        Shot("tut_step1_explain");
        // 説明の間の入力は操作にならない
        int attacks = 0;
        System.Action<PlayerController.AttackDirection> countAtk = d => attacks++;
        PlayerController.AttackStarted += countAtk;
        yield return TutPress(GameAction.AttackForward, 0.2f);
        PlayerController.AttackStarted -= countAtk;
        Check(!Tut.AttackForwardDone && attacks == 0, $"T: input while the explanation is open does nothing (attacks {attacks})");
        // 1 攻撃
        Tut.BeginPractice();
        yield return new WaitForSecondsRealtime(0.3f);
        float x0 = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(1f);
        float kmh = (pc.transform.position.x - x0) / 1f * GameManager.KmhPerMps;
        L($"[T] practice speed {kmh:F1} km/h");
        Check(kmh > 5f && kmh < 20f, $"T: the character auto-runs slowly ({kmh:F1} km/h)");
        yield return TutPress(GameAction.AttackForward, 0.6f);
        Shot("tut_step1_practice");
        yield return TutPress(GameAction.AttackBack, 0.6f);
        Check(Tut.AttackForwardDone && Tut.AttackBackDone && Tut.Hits > 0, $"T: step 1 forward+back attacks counted (hits {Tut.Hits})");
        // ダメージ/距離なし
        int lives0 = gm.Lives;
        var dr = gm.TryDamagePlayer(false, "QA", 30);
        Check(dr == GameManager.DamageResult.Ignored && gm.Lives == lives0, "T: no damage during practice");
        Check(gm.MaxDistance < 1f, $"T: distance does not advance ({gm.MaxDistance:F1})");
        yield return WaitTut(() => Tut.CurrentStep == TutorialRun.Step.Jump && TutorialRun.PanelOpen, 5f);
        Check(Tut.CurrentStep == TutorialRun.Step.Jump, "T: advanced to step 2 (jump)");
        // 2 ジャンプ
        Tut.BeginPractice();
        yield return new WaitForSecondsRealtime(0.3f);
        yield return TutPress(GameAction.Jump, 0.25f);
        yield return TutPress(GameAction.Jump, 0.3f);
        Check(Tut.JumpDone && Tut.DoubleJumpDone, "T: step 2 jump + double jump counted");
        yield return WaitTut(() => Tut.CurrentStep == TutorialRun.Step.Dive && TutorialRun.PanelOpen, 6f);
        // 3 下攻撃
        Tut.BeginPractice();
        yield return new WaitForSecondsRealtime(0.3f);
        yield return TutPress(GameAction.Jump, 0.3f);
        yield return TutPress(GameAction.AttackDown, 0.2f);
        yield return WaitTut(() => Tut.DiveDone, 3f);
        Check(Tut.DiveStarted && Tut.DiveDone, "T: step 3 dive attack landed on solid floor");
        yield return WaitTut(() => Tut.CurrentStep == TutorialRun.Step.Launch && TutorialRun.PanelOpen, 6f);
        Shot("tut_step4_explain");
        // 4 打ち上げ → 追撃(最大4回試す。できなければスキップの確認)
        Tut.BeginPractice();
        yield return new WaitForSecondsRealtime(0.4f);
        for (int i = 0; i < 4 && !Tut.FollowUpDone; i++)
        {
            yield return WaitTut(() => pc.IsGrounded, 2f);
            yield return TutPress(GameAction.Jump, 0.18f);
            yield return TutPress(GameAction.AttackForward, 0.25f);
            yield return TutPress(GameAction.AttackForward, 0.6f);
            L($"[T] launch try {i + 1}: launched={Tut.LaunchDone} followUp={Tut.FollowUpDone}");
        }
        if (!Tut.FollowUpDone) { L("[T] WARN: launch+follow-up not achieved by the scripted input -> skip (skip is part of the spec)"); Tut.SkipStep(); }
        else Check(Tut.LaunchDone, "T: step 4 launch then aerial follow-up counted");
        yield return WaitTut(() => Tut.CurrentStep == TutorialRun.Step.Card && TutorialRun.PanelOpen, 6f);
        // 5 カード
        Tut.BeginPractice();
        yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 5f);
        Check(gm.IsRewardSequenceWaitingForSelection && Time.timeScale == 0f, "T: step 5 three practice cards shown (game paused)");
        Shot("tut_step5_cards");
        int atkPow0 = pc.EffectiveAttackPower;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        int owned0 = CardInventory.GetTotalCount("attack_up");
        if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.25f); seq.OnCardClicked(0); }
        yield return WaitTut(() => Tut.CardChosen, 8f);
        L($"[T] chosen {Tut.ChosenCard} atk {atkPow0} -> {pc.EffectiveAttackPower}");
        Check(Tut.CardChosen && Tut.ChosenCard == "attack_up" && pc.EffectiveAttackPower > atkPow0, "T: the chosen card takes effect in the practice");
        Check(CardInventory.GetTotalCount("attack_up") == owned0, "T: the practice card is not added to owned cards");
        yield return WaitTut(() => Tut.CurrentStep == TutorialRun.Step.End && TutorialRun.PanelOpen, 6f);
        Check(Tut.CurrentStep == TutorialRun.Step.End && TutorialProgress.PracticeDone, "T: step 6 end reached, practice recorded as done");
        Shot("tut_step6_end");
        // 書いていない
        var prog1 = SaveSystem.CaptureCategory(SaveCategory.Progress).Where(i => !i.k.StartsWith("Tutorial.")).Select(i => i.k + "=" + i.v).ToList();
        var diff = prog1.Except(prog0).Concat(prog0.Except(prog1)).ToList();
        Check(diff.Count == 0, $"T: nothing in the progress data changed during the practice ({string.Join(" ", diff.Take(6))})");
        // 出発 → ステージ選択
        TutorialLauncher.Exit(true, "qa finished");
        yield return WaitTut(() => GameManager.Instance != null && !TutorialLauncher.Covering && GameManager.Instance.IsOverlayOpen, 12f);
        gm = GameManager.Instance;
        Check(!TutorialMode.Active && !gm.HasStarted && gm.IsOverlayOpen, "T: first-run flow ends at the normal run departure (stage select open)");
        Check(gm.SelectedCharacterId == ch0 && SaveStore.GetString("DeckCardIds", "") == deck0, $"T: the player's selected character/deck are unchanged ({gm.SelectedCharacterId} = {ch0})");
        Check(PlayerController.Instance == null || PlayerController.ScriptedSpeedCapMps > 1e6f, "T: no speed limit left over");
        Shot("tut_after_stage_select");
        yield return ReloadHome();

        // 設定の「遊び方」から: 中断中のランは残り、ホームへ戻る / 練習をやめる
        RunCheckpoint.Save(new RunCheckpoint.Data { active = true, characterId = "swordsman", stageId = "wasteland_road" });
        string ck0 = SaveStore.GetString(RunCheckpoint.Key, "");
        bool ok = TutorialLauncher.Launch(false, "qa settings");
        yield return WaitTut(() => Tut != null && TutorialMode.Active && !TutorialLauncher.Covering, 20f);
        Check(ok && Tut != null, "T: practice can be re-taken from settings");
        gm = GameManager.Instance; pc = PlayerController.Instance;
        Check(!TutorialProgress.ShouldOfferOnDoor, "T: after the practice the door offer does not come back");
        yield return new WaitForSecondsRealtime(0.5f);
        Tut.Quit("qa quit");
        yield return WaitTut(() => GameManager.Instance != null && !TutorialLauncher.Covering && !TutorialMode.Active, 12f);
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        Check(!gm.HasStarted && !gm.IsOverlayOpen, "T: quitting a settings practice returns to home");
        Check(RunCheckpoint.HasActiveRun && SaveStore.GetString(RunCheckpoint.Key, "") == ck0, "T: an existing CONTINUE survives the practice");
        RunCheckpoint.Clear();
        // マルチ中は出せない(条件だけ)
        Check(TutorialMode.CanLaunchFromHome, "T: can launch again from home");
    }

    // ===================================================================== G
    IEnumerator TutGuides()
    {
        L("== G: first-run guides ==");
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        autoPickHold = false;
        yield return BeginRun("swordsman", "wasteland_road");
        Check(FirstRunGuide.Showing == FirstRunGuide.Kind.None, "G: no guide at run start");
        typeof(GameManager).GetMethod("UnlockEscape", NP).Invoke(gm, null); // 初めてのボス報酬の後と同じ(脱出の解禁の入口)
        yield return WaitTut(() => FirstRunGuide.Showing == FirstRunGuide.Kind.EscapeGuide, 8f);
        Check(FirstRunGuide.Showing == FirstRunGuide.Kind.EscapeGuide && Time.timeScale == 0f, $"G: escape guide shown once escape unlocks, game paused (ts {Time.timeScale})");
        Shot("guide_escape");
        // 板が出ている間の長押しで脱出を溜めない
        yield return new WaitForSecondsRealtime(0.5f);
        FirstRunGuideDebug.Answer(0);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(TutorialProgress.EscapeGuideShown && Time.timeScale > 0f && !gm.IsGameOver, "G: 'OK' closes it and the run continues (escape is not forced)");
        yield return new WaitForSecondsRealtime(2.5f);
        Check(FirstRunGuide.EscapeGuidesShown == 1 && FirstRunGuide.Showing == FirstRunGuide.Kind.None, "G: escape guide shown only once");
        // 脱出(Win) → ホームでMILEの案内
        int mile0 = gm.TotalOwnedMile;
        gm.AddRunMileForQa(300);
        gm.Win();
        yield return new WaitForSecondsRealtime(1f);
        Check(gm.IsWin && gm.TotalOwnedMile >= mile0 + 300 && TutorialProgress.MileGuide == 1, $"G: first escape keeps the run MILE and arms the MILE guide (MILE {mile0} -> {gm.TotalOwnedMile})");
        yield return EndRun();
        yield return ReloadHome();
        yield return WaitTut(() => FirstRunGuide.Showing == FirstRunGuide.Kind.MileGuide, 6f);
        Check(FirstRunGuide.Showing == FirstRunGuide.Kind.MileGuide, "G: MILE guide shown at home after the first escape");
        Shot("guide_mile");
        FirstRunGuideDebug.Answer(0);
        yield return new WaitForSecondsRealtime(1.5f);
        Check(TutorialProgress.MileGuide == 2 && FirstRunGuide.Showing == FirstRunGuide.Kind.None, "G: MILE guide closed and recorded");
        int shownBefore = FirstRunGuide.MileGuidesShown;
        yield return ReloadHome();
        yield return new WaitForSecondsRealtime(2f);
        Check(FirstRunGuide.MileGuidesShown == shownBefore && FirstRunGuide.Showing == FirstRunGuide.Kind.None, "G: MILE guide not shown again after it was acknowledged");
        autoPickHold = false;
    }

    // ===================================================================== R
    IEnumerator TutDeathResult()
    {
        L("== R: death result ==");
        if (!SaveProfile.IsTest) { SaveProfile.Switch(true, true); yield return ReloadHome(); }
        TutorialProgress.MarkOffered();
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true; // HPの補充を止める
        yield return null;
        int mile0 = gm.TotalOwnedMile;
        gm.AddRunMileForQa(250);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 9999); yield return new WaitForSecondsRealtime(0.1f); }
        yield return WaitTut(() => gm.ResultShown, 10f);
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("result_failed");
        Check(gm.IsGameOver && !gm.IsWin && gm.TotalOwnedMile == mile0, $"R: death loses the run MILE (wallet {mile0} -> {gm.TotalOwnedMile}, run {gm.RunMile})");
        yield return EndRun();
        yield return ReloadHome();
    }
}
#endif

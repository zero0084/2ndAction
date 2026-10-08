#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 縦画面の見た目の確認(2026-10-08、依頼E): -qaPortrait <dir> [-qaPortraitVideo 1] [-qaPortraitOnly HMRS...]
// 窓の大きさは起動引数(-screen-width/-screen-height)で決める。各画面を開いて撮る(合否より撮影が目的。重なり/はみ出しは目で見る)。
//  H ホーム / P 設定・ランキング・マルチ / M マップ・キャラ選択・デッキ・合成・ガチャ / R ラン(横から・斜め、カード選択、12枚、ポーズ、結果) / S 疾走出発 / T 練習
public partial class QaSweep
{
    bool PCase(char c) { string o = Arg("-qaPortraitOnly", ""); return o == "" || o.IndexOf(c) >= 0; }

    IEnumerator PortraitModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true; SprintRecords.DevUnlockAll = true;
        gm.AddMile(5000);
        L($"[portrait] screen {Screen.width}x{Screen.height} portrait={Screen.height > Screen.width} safe={Screen.safeArea}");
        string tag = $"{Screen.width}x{Screen.height}";
        void S(string n) => Shot($"{tag}_{n}");

        if (PCase('H')) { yield return new WaitForSecondsRealtime(1f); S("home"); yield return new WaitForSecondsRealtime(0.4f); }
        if (PCase('P'))
        {
            SettingsPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.6f); S("settings"); yield return new WaitForSecondsRealtime(0.3f);
            SettingsPanel.Instance.Close(); yield return new WaitForSecondsRealtime(0.5f);
            RankingPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.8f); S("ranking"); yield return new WaitForSecondsRealtime(0.3f);
            typeof(RankingPanel).GetMethod("Close", NP).Invoke(RankingPanel.Instance, null); yield return new WaitForSecondsRealtime(0.5f);
            NetDebugUI.OpenPanel(); yield return new WaitForSecondsRealtime(0.6f); S("multi"); yield return new WaitForSecondsRealtime(0.3f);
            NetDebugUI.ClosePanel(); yield return new WaitForSecondsRealtime(0.5f);
        }
        if (PCase('M'))
        {
            gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.3f); S("stage_select"); yield return new WaitForSecondsRealtime(0.3f);
            FindFirstObjectByType<StageSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);
            gm.OpenCharacterSelect(); yield return new WaitForSecondsRealtime(1.3f); S("char_select"); yield return new WaitForSecondsRealtime(0.3f);
            FindFirstObjectByType<CharacterSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);
            gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); S("deck_edit"); yield return new WaitForSecondsRealtime(0.3f);
            gm.CloseDeckEdit(); yield return new WaitForSecondsRealtime(1.3f);
            gm.OpenCardFusion(); yield return new WaitForSecondsRealtime(1.3f); S("fusion"); yield return new WaitForSecondsRealtime(0.3f);
            var fu = FindFirstObjectByType<CardFusionUI>(); if (fu != null) fu.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return ReloadHome();
            typeof(GameManager).GetMethod("OnGachaMachineTapped", NP).Invoke(gm, null);
            yield return WaitTut(() => gm.GachaResultOpen, 4f); yield return new WaitForSecondsRealtime(0.6f); S("gacha"); yield return new WaitForSecondsRealtime(0.3f);
            yield return ReloadHome();
        }
        if (PCase('R'))
        {
            foreach (int view in new[] { 0, 1 })
            {
                PortraitRunView.Set(view);
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(2.5f);
                S($"run_view{view}_start");
                yield return new WaitForSecondsRealtime(0.3f);
                WarpTo(1600f); RunLedger.DevClearDebug();
                yield return new WaitForSecondsRealtime(3f);
                S($"run_view{view}_1600m");
                if (Arg("-qaPortraitVideo", "0") == "1") yield return RecordUltimate($"{tag}_run_view{view}", 4f);
                yield return new WaitForSecondsRealtime(0.3f);
                if (view == 0)
                {
                    // カード選択 / 12枚のデッキ表示 / ポーズ
                    var apply = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
                    var ids = CardDatabase.UnlockedCards.Select(c => c.cardId).Take(12).ToList();
                    if (apply != null) foreach (var id in ids) { try { apply.Invoke(gm, new object[] { id }); } catch { } yield return null; }
                    yield return new WaitForSecondsRealtime(0.8f);
                    S("run_deck12");
                    yield return new WaitForSecondsRealtime(0.3f);
                    typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
                    yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 6f);
                    yield return new WaitForSecondsRealtime(0.6f);
                    S("card_choice");
                    yield return new WaitForSecondsRealtime(0.3f);
                    autoPickHold = false; StartCoroutine(AutoPickCards());
                    yield return WaitTut(() => !gm.IsRewardSequenceRunning, 8f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    var spm = typeof(GameManager).GetField("showPauseMenu", NP);
                    spm.SetValue(gm, true); TimeControl.Pause(typeof(GameManager).GetField("pauseMenuTimeOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null));
                    yield return new WaitForSecondsRealtime(0.5f); S("pause"); yield return new WaitForSecondsRealtime(0.3f);
                    spm.SetValue(gm, false); TimeControl.ResetAll();
                    // 結果
                    stopKeepAlive = true; yield return null;
                    typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
                    for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 99999); yield return new WaitForSecondsRealtime(0.05f); }
                    yield return WaitTut(() => gm.ResultShown, 6f); yield return new WaitForSecondsRealtime(0.5f);
                    S("result");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                yield return EndRun();
            }
            PortraitRunView.Set(0);
        }
        if (PCase('S'))
        {
            yield return ReloadHome();
            gm.SetSelectedCharacter("swordsman");
            gm.DepartSprint("wasteland_road", 20000);
            yield return new WaitForSecondsRealtime(3f); S("sprint_early");
            yield return new WaitForSecondsRealtime(4f); S("sprint_mid");
            if (Arg("-qaPortraitVideo", "0") == "1") yield return RecordUltimate($"{tag}_sprint", 3f);
            yield return WaitTut(() => gm.HasStarted && SprintRunner.Instance == null, 40f);
            yield return EndRun();
        }
        if (PCase('T'))
        {
            yield return ReloadHome();
            TutorialLauncher.Launch(false, "qa portrait");
            yield return WaitTut(() => TutorialRun.Instance != null && !TutorialLauncher.Covering && TutorialRun.PanelOpen, 25f);
            yield return new WaitForSecondsRealtime(0.8f); S("tutorial");
            yield return new WaitForSecondsRealtime(0.3f);
            TutorialRun.Instance?.Quit("qa portrait");
            yield return WaitTut(() => GameManager.Instance != null && !TutorialMode.Active && !TutorialLauncher.Covering, 12f);
        }
        UnlockRules.DevUnlockAll = false; SprintRecords.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

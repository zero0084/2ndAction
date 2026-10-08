#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 縦画面の見た目の確認(2026-10-08、依頼E): -qaPortrait <dir> [-qaPortraitVideo 1] [-qaPortraitOnly HMRS...]
// 窓の大きさは起動引数(-screen-width/-screen-height)で決める。各画面を開いて撮る(合否より撮影が目的。重なり/はみ出しは目で見る)。
//  H ホーム / P 設定・ランキング・マルチ / M マップ・キャラ選択・デッキ・合成・ガチャ / R ラン(横から・斜め、カード選択、12枚、ポーズ、結果) / S 疾走出発 / T 練習
public partial class QaSweep
{
    float logicalAhead = -1f;
    bool PCase(char c) { string o = Arg("-qaPortraitOnly", ""); return o == "" || o.IndexOf(c) >= 0; }

    IEnumerator PortraitModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        SaveProfile.Switch(true, true);
        string qaLang = Arg("-qaPortraitLang", ""); string langKeep = Loc.Current;
        if (qaLang != "") { Loc.Set(qaLang); L($"[portrait] lang {qaLang}"); }
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
            yield return WaitTut(() => ScreenTransitionManager.Instance == null || !ScreenTransitionManager.Instance.IsTransitioning, 5f);
            SettingsPanel.OpenStatic();
            for (int i = 0; i < 6; i++) { yield return new WaitForSecondsRealtime(0.1f); L($"[portrait] settings state={GetPrivate(SettingsPanel.Instance, "state")} t={GetPrivate(SettingsPanel.Instance, "t")} dt={Time.unscaledDeltaTime:F3} en={SettingsPanel.Instance.isActiveAndEnabled}"); }
            S("settings"); yield return new WaitForSecondsRealtime(0.3f);
            {
                // 表示の段(縦画面のラン表示の行)まで送って撮る
                var sp = SettingsPanel.Instance;
                float ma = (float)typeof(SettingsPanel).GetMethod("MeasureAudio", NP).Invoke(sp, null), ml = (float)typeof(SettingsPanel).GetMethod("MeasureLanguage", NP).Invoke(sp, null);
                SetPrivate(sp, "scroll", new Vector2(0f, ma + 16f + ml + 16f - 8f));
                yield return new WaitForSecondsRealtime(0.3f); S("settings_display");
                PortraitRunView.Set(PortraitRunView.Oblique); yield return new WaitForSecondsRealtime(0.3f); S("settings_display_oblique");
                PortraitRunView.Set(PortraitRunView.Side); yield return new WaitForSecondsRealtime(0.2f);
            }
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
            FindFirstObjectByType<DeckEditUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);
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
                {
                    // 判定の画面(出現/攻撃開始の基準)は見た目のカメラに関係なく横画面と同じ / 描画の負荷(垂直同期を外して測る)
                    var camM = Camera.main;
                    float ahead = GameView.Right(camM) - pc.transform.position.x, behind = pc.transform.position.x - GameView.Left(camM);
                    L($"[portrait] view{view} logical ahead={ahead:F2} behind={behind:F2} half={GameView.HalfWidth(camM):F2} cam={(camM != null ? camM.name : "-")}");
                    if (logicalAhead < 0f) logicalAhead = ahead; else Check(Mathf.Abs(ahead - logicalAhead) < 0.6f, $"logical view ahead same in both views ({logicalAhead:F2} vs {ahead:F2})");
                    int vs = QualitySettings.vSyncCount, tf = Application.targetFrameRate;
                    QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    yield return null;
                    float sum = 0f, worst = 0f; int n = 0;
                    while (n < 240) { yield return null; sum += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); n++; }
                    QualitySettings.vSyncCount = vs; Application.targetFrameRate = tf;
                    L($"[portrait] view{view} frame avg={sum / n * 1000f:F2}ms worst={worst * 1000f:F1}ms renderers={FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Count(r => r.isVisible)}");
                }
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
            yield return new WaitForSecondsRealtime(1.5f); S("sprint_mid");
            if (Arg("-qaPortraitVideo", "0") == "1") yield return RecordUltimate($"{tag}_sprint", 3f);
            yield return WaitTut(() => gm.HasStarted && SprintRunner.Instance == null, 40f);
            yield return EndRun();
        }
        if (PCase('O'))
        {
            // 向きの切り替え(窓の大きさを縦⇔横に): ホーム / キャラ選択(選択が残る・押した所と見た目が合う) / デッキ編集 / ラン中(進行が続く) / ラン中の表示の切替(設定を閉じた時に反映)
            int pw = Screen.width, ph = Screen.height;
            int lw = Mathf.Max(pw, ph), lh = Mathf.Min(pw, ph);
            IEnumerator Rot(bool land) { Screen.SetResolution(land ? lw : lh, land ? lh : lw, false); float w2 = 0f; while ((Screen.width > Screen.height) != land && w2 < 3f) { yield return null; w2 += Time.unscaledDeltaTime; } yield return new WaitForSecondsRealtime(0.8f); }
            yield return ReloadHome();
            yield return Rot(true); Shot("rot_home_land");
            yield return Rot(false); Shot("rot_home_port");

            gm.OpenCharacterSelect(); yield return new WaitForSecondsRealtime(1.3f);
            var cs = FindFirstObjectByType<CharacterSelectUI>();
            var csTap = typeof(CharacterSelectUI).GetMethod("HandleTap", NP);
            int Sel() => (int)GetPrivate(cs, "selectedIndex");
            Vector2 Center(RectTransform rt)
            {
                var c = rt.GetComponentInParent<Canvas>().rootCanvas;
                return RectTransformUtility.WorldToScreenPoint(c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera, rt.TransformPoint(rt.rect.center));
            }
            if (cs != null && cs.cardSlotRects.Length > 2)
            {
                csTap.Invoke(cs, new object[] { Center(cs.cardSlotRects[1]) }); yield return new WaitForSecondsRealtime(0.5f);
                Check(Sel() == 1, $"portrait: char select tap on card 1 selects it (got {Sel()})");
                yield return Rot(true); Shot("rot_char_land");
                Check(Sel() == 1, $"portrait: selection kept after rotate to landscape (got {Sel()})");
                csTap.Invoke(cs, new object[] { Center(cs.cardSlotRects[2]) }); yield return new WaitForSecondsRealtime(0.5f);
                Check(Sel() == 2, $"landscape: tap on card 2 selects it after rotation (got {Sel()})");
                yield return Rot(false); Shot("rot_char_port");
                Check(Sel() == 2, $"portrait: selection kept after rotate back (got {Sel()})");
                csTap.Invoke(cs, new object[] { Center(cs.cardSlotRects[1]) }); yield return new WaitForSecondsRealtime(0.5f); // 端のカードは左右の矢印の下なので隣を押す
                Check(Sel() == 1, $"portrait: tap on card 1 selects it after rotation (got {Sel()})");
                L($"[portrait] char select rotate/tap ok sel={Sel()}");
            }
            cs?.Close(); yield return new WaitForSecondsRealtime(1.3f);

            gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); Shot("rot_deck_port");
            yield return Rot(true); Shot("rot_deck_land");
            yield return Rot(false); Shot("rot_deck_port2");
            yield return new WaitForSecondsRealtime(0.3f); FindFirstObjectByType<DeckEditUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);

            PortraitRunView.Set(PortraitRunView.Side);
            yield return BeginRun("swordsman", "wasteland_road");
            yield return new WaitForSecondsRealtime(2f);
            WarpTo(800f); RunLedger.DevClearDebug(); yield return new WaitForSecondsRealtime(2f);
            float d0 = gm.MaxDistance; string rid = RunLedger.Current != null ? RunLedger.Current.runId : ""; Shot("rot_run_port");
            yield return Rot(true); Shot("rot_run_land");
            var vmt = FindFirstObjectByType<ViewModeToggle>();
            Check(vmt == null || !vmt.PortraitActive, "landscape run uses the side camera");
            yield return new WaitForSecondsRealtime(1f);
            float d1 = gm.MaxDistance;
            Check(gm.HasStarted && !gm.IsGameOver && d1 >= d0 && RunLedger.Current != null && RunLedger.Current.runId == rid, $"run continues across rotation ({d0:F0} -> {d1:F0}, same run)");
            yield return Rot(false); Shot("rot_run_port2");
            // ラン中に表示を変える: 設定を開いている間は変えず、閉じた時に変わる
            SettingsPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.4f);
            PortraitRunView.Set(PortraitRunView.Oblique); yield return new WaitForSecondsRealtime(0.3f);
            Check(vmt == null || !vmt.PortraitActive, "view change is held while settings is open");
            SettingsPanel.Instance.Close(); yield return new WaitForSecondsRealtime(1f);
            Check(vmt == null || vmt.PortraitActive, "view change applied after closing settings");
            Shot("mode_applied_oblique");
            float d2 = gm.MaxDistance;
            Check(gm.HasStarted && !gm.IsGameOver && d2 >= d1 && RunLedger.Current != null && RunLedger.Current.runId == rid, $"run not reset by view change ({d1:F0} -> {d2:F0}, same run)");
            yield return Rot(true); Shot("rot_run_land_from_oblique");
            Check(vmt == null || !vmt.PortraitActive, "landscape after oblique uses the side camera");
            yield return Rot(false); Shot("rot_run_port_oblique");
            Check(vmt == null || vmt.PortraitActive, "portrait again returns to the oblique camera");
            PortraitRunView.Set(PortraitRunView.Side);
            yield return EndRun();
        }
        if (PCase('B'))
        {
            // 地形/ボスの見え方(横から・斜め): 天空回廊(浮島・段差)、自然洞窟(穴/天井)、荒野のボス
            foreach (int view in new[] { 0, 1 })
            {
                PortraitRunView.Set(view);
                foreach (var st in new[] { "sky_corridor", "natural_cave" })
                {
                    yield return BeginRun("swordsman", st);
                    WarpTo(2400f); RunLedger.DevClearDebug(); yield return new WaitForSecondsRealtime(3f);
                    S($"terrain_{st}_view{view}");
                    yield return new WaitForSecondsRealtime(1.5f); S($"terrain_{st}_view{view}_b");
                    yield return EndRun();
                }
                yield return BeginRun("swordsman", "wasteland_road");
                WarpTo(990f); RunLedger.DevClearDebug();
                yield return WaitTut(() => Bm != null && Bm.IsBossPhase, 25f);
                for (int k = 0; k < 4; k++) { yield return new WaitForSecondsRealtime(2.5f); S($"boss_view{view}_{k}"); }
                if (Arg("-qaPortraitVideo", "0") == "1") yield return RecordUltimate($"{tag}_boss_view{view}", 4f);
                yield return EndRun();
            }
            PortraitRunView.Set(0);
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
        if (qaLang != "") Loc.Set(langKeep);
        UnlockRules.DevUnlockAll = false; SprintRecords.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

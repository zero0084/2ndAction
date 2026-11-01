#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// タイトル画面(2026-10-31)の自動テスト: -titleScreen 1 -qaTitle <dir> [-ttInput debug|none]
//  A 起動の直後にタイトルが出ている(起動ロゴの後)/ B ホームの準備ができるまで入力を受けない(早押しは準備後に受ける)
//  C 入力から 0.2〜0.5 秒でホームへ / D 押した入力はホームへ持ち越さない / E 一度だけ(シーンの読み直しでは出ない)
//  ttInput none = 入力は外から(本物のキー/クリック/タップ)。結果を待って確かめるだけ
public partial class QaSweep
{
    IEnumerator TitleModeQa()
    {
        float t0 = Time.realtimeSinceStartup;
        Check(TitleScreen.Showing || TitleScreen.Starts > 0, $"A: the title screen is up at launch (t={t0:F2}s)");
        float w = 0f;
        while (BootSplash.Showing && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return null;
        Shot("title_after_logo");
        string input = Arg("-ttInput", "debug");
        if (input == "none")
        {
            w = 0f;
            while (TitleScreen.Showing && w < 90f) { yield return null; w += Time.unscaledDeltaTime; }
            Check(!TitleScreen.Showing && TitleScreen.Starts == 1, $"C: a real input started the game (by '{TitleScreen.LastStartInput}')");
            yield return new WaitForSecondsRealtime(0.8f);
            var g = GameManager.Instance;
            Check(g != null && !g.HasStarted && !g.IsOverlayOpen && !SettingsPanel.IsVisible, $"D: the start input did not also press a home button (overlay {g?.IsOverlayOpen}, settings {SettingsPanel.IsVisible})");
            Shot("title_home_after_real_input");
            yield break;
        }
        // B: 準備前の早押し(ホームの準備ができるまで待つ)
        while (GameManager.Instance == null && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        Check(TitleScreen.Showing, "B: the title waits for an input (does not go home by itself)");
        Shot("title_waiting");
        // 2026-11-01 キャラの影: -ttExpect <id> = 出ているはずの影(初回は swordsman、前回の起動で -ttSelect したキャラ)
        string expect = Arg("-ttExpect", "");
        if (expect != "")
        {
            Check(TitleScreen.ShownCharacterId == expect, $"F: the title shows the silhouette of {expect} (shown '{TitleScreen.ShownCharacterId}', selected '{GameManager.Instance?.SelectedCharacterId}')");
            // 表示例の撮影: 入力を受け付ける状態になり、画面の向きが回り終わって、影/案内のフェードが済んでから撮る
            //  (エミュレーターは1コマが遅く、準備の途中では影が薄い/回転の途中の画面になる)
            string ori = Arg("-qaOrient", "");
            w = 0f;
            while ((!TitleScreen.InputReady || (ori == "portrait" && Screen.width > Screen.height) || (ori == "landscape" && Screen.width < Screen.height)) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1.5f);
        }
        Shot($"title_{Screen.width}x{Screen.height}_{TitleScreen.ShownCharacterId}");
        float pressAt = Time.realtimeSinceStartup;
        TitleScreen.DebugPress = true;
        w = 0f;
        while (TitleScreen.Showing && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        float took = Time.realtimeSinceStartup - pressAt;
        Check(!TitleScreen.Showing && TitleScreen.Starts == 1, "C: an input goes to home");
        Check(took >= 0.2f && took <= 0.6f, $"C: home follows the input in 0.2-0.5 s ({took:F2}s)");
        yield return null;
        Check(UiInputGate.Blocked || !Input.anyKey, "D: the start input is latched until release (not passed to home)");
        var gm0 = GameManager.Instance;
        yield return new WaitForSecondsRealtime(0.3f);
        Check(gm0 != null && !gm0.HasStarted && !gm0.IsOverlayOpen && !SettingsPanel.IsVisible, "D: still at home (the input did not also press a home button)");
        yield return new WaitForSecondsRealtime(0.6f);
        Shot("title_home");
        yield return ReloadHome();
        yield return new WaitForSecondsRealtime(0.6f);
        Check(!TitleScreen.Showing && TitleScreen.Starts == 1, "E: reloading the home scene does not show the title again");
        // -ttSelect <id>: キャラ選択でそのキャラを決定して終わる(次の起動の -ttExpect で確かめる)。
        //  試験用の保存ファイルの中だけで、そのキャラを正式に解放しておく(開発版の全解放は使わない = 製品と同じ判定で保存/読み込み)
        string pick = Arg("-ttSelect", "");
        if (pick != "")
        {
            if (!UnlockRules.OfficialCharacterUnlocked(pick))
            {
                string cur = SaveStore.GetString(UnlockRules.CharsKey, "");
                SaveStore.SetString(UnlockRules.CharsKey, string.IsNullOrEmpty(cur) ? pick : cur + "," + pick);
                SaveStore.Save(); UnlockRules.Reload();
            }
            var gmS = GameManager.Instance;
            gmS.OpenCharacterSelect();
            yield return new WaitForSecondsRealtime(1.2f);
            var ui = FindFirstObjectByType<CharacterSelectUI>();
            // 一覧は解放済みだけを並べる → 見えている中での位置を探す
            var defAt = typeof(CharacterSelectUI).GetMethod("DefAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int n = ui != null ? ui.VisibleCount : 0, idx = -1;
            for (int i = 0; i < n; i++) if (((CharacterDefinition)defAt.Invoke(ui, new object[] { i })).characterId == pick) idx = i;
            Check(ui != null && idx >= 0, $"G: {pick} is in the character select list (index {idx} of {n})");
            if (ui == null || idx < 0) yield break;
            ui.DebugSelectAndCenter(idx);
            yield return new WaitForSecondsRealtime(0.8f);
            typeof(CharacterSelectUI).GetMethod("Confirm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null); // SELECT と同じ処理
            yield return new WaitForSecondsRealtime(0.8f);
            Check(gmS.SelectedCharacterId == pick && SaveStore.GetString("SelectedCharacterId", "") == pick, $"G: SELECT saved {pick} as the selected character (saved '{SaveStore.GetString("SelectedCharacterId", "")}')");
        }
    }
}
#endif

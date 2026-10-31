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
    }
}
#endif

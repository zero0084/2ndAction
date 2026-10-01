#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// ホーム整理/設定画面/DEBUG/遷移/戻る操作(2026-10-01)の確認。 -qaUiCheck <dir> [-qaUiPhase 1|2]
//  phase 1: 配置(重なり/画面内)、設定の反映と保存、入力の遮断、戻る操作の順番、遷移の往復/連打/扉の出発→カウントダウン、ポーズ中の設定
//  phase 2: (アプリを起動し直して)phase 1 で保存した設定が残っているか
public partial class QaSweep
{
    const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
    object Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, NPI | BindingFlags.Public)?.Invoke(o, a);
    T Field<T>(object o, string f) => (T)o.GetType().GetField(f, NPI | BindingFlags.Public).GetValue(o);
    Rect R(string getter) => (Rect)typeof(GameManager).GetMethod(getter, NPI).Invoke(gm, null);
    bool Transitioning => ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning;
    IEnumerator WaitTransition() { float w = 0f; while (Transitioning && w < 5f) { yield return null; w += Time.unscaledDeltaTime; } yield return null; }

    IEnumerator UiCheckMode()
    {
        string phase = Arg("-qaUiPhase", "1");
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3.5f);
        var am = AudioManager.Instance;
        var hsa = HighSpeedAssist.Instance;

        if (phase == "2")
        {
            L($"[restart] master={am.MasterVolume:F2} bgm={am.BgmVolume:F2} se={am.SfxVolume:F2} env={am.EnvVolume:F2} muted={am.Muted} shake={GameSettings.ScreenShake} glow={GameSettings.GlowIntensity:F2} assist={hsa.assistEnabled} engage={hsa.EngageSettingKmh}");
            Check(Mathf.Abs(am.MasterVolume - 0.6f) < 0.01f && Mathf.Abs(am.BgmVolume - 0.4f) < 0.01f && Mathf.Abs(am.SfxVolume - 0.7f) < 0.01f && Mathf.Abs(am.EnvVolume - 0.5f) < 0.01f, "volumes are kept after a restart");
            Check(!GameSettings.ScreenShake && Mathf.Abs(GameSettings.GlowIntensity - 0.3f) < 0.01f, "display settings are kept after a restart");
            Check(Mathf.Abs(hsa.EngageSettingKmh - 120f) < 0.1f && Mathf.Abs(hsa.releaseKmh - 110f) < 0.1f && Mathf.Abs(hsa.fullAssistKmh - 150f) < 0.1f, "assist start speed is kept after a restart (release/full follow it)");
            // 元に戻す
            am.SetMasterVolume(1f); am.SetBgmVolume(1f); am.SetSfxVolume(1f); am.SetEnvVolume(1f); am.SetMuted(false);
            GameSettings.SetScreenShake(true); GameSettings.SetGlowIntensity(1f); hsa.SetEngageKmh(HighSpeedAssist.DefaultEngageKmh);
            yield break;
        }

        // ---- 1) 配置
        Rect mile = R("GetHomeMileRect"), set = R("GetHomeSettingsButtonRect"), multi = R("GetHomeMultiButtonRect"), dbg = R("GetHomeDebugButtonRect");
        Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
        L($"[layout] {Screen.width}x{Screen.height} mile={mile} settings={set} multi={multi} debug={dbg}");
        Check(!mile.Overlaps(set) && !mile.Overlaps(multi) && !set.Overlaps(multi), "home: MILE / 設定 / マルチ do not overlap");
        Check(Inside(mile, screen) && Inside(set, screen) && Inside(multi, screen) && Inside(dbg, screen), "home: buttons stay inside the screen");
        Check(set.height >= 46f && multi.height >= 46f, "home: 設定/マルチ are big enough to tap");
        Rect door = Frac("0.40,0.14,0.565,0.65"), bed = Frac("0.0,0.52,0.32,1.0"), book = Frac("0.78,0.78,1.0,1.0"), portrait = Frac("0.02,0.14,0.17,0.38");
        Check(!dbg.Overlaps(door) && !dbg.Overlaps(bed) && !dbg.Overlaps(book) && !dbg.Overlaps(portrait), "home: DEBUG does not sit on the door/bed/book/portrait");
        Rect gear = SettingsPanel.MenuGearRect();
        Check(Inside(gear, screen), "menus: settings button inside the screen");

        // ---- 2) 設定の反映
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(SettingsPanel.IsOpen && UiInputGate.Blocked, "settings open: input behind it is blocked");
        Check(!TimeControl.IsPaused, "opening settings does not pause the world");
        am.SetMasterVolume(0.6f); am.SetBgmVolume(0.4f); am.SetSfxVolume(0.7f); am.SetEnvVolume(0.5f);
        yield return null;
        float sfxOn = am.SfxOutputVolume;
        am.SetMuted(true); yield return null;
        float sfxMuted = am.SfxOutputVolume;
        am.SetMuted(false); yield return null;
        L($"[audio] sfx output {sfxOn:F3} -> muted {sfxMuted:F3} -> unmuted {am.SfxOutputVolume:F3}; master={am.MasterVolume:F2}");
        Check(sfxOn > 0f && sfxMuted == 0f && Mathf.Abs(am.SfxOutputVolume - sfxOn) < 1e-4f, "mute silences and unmute restores the previous volume");
        Check(Mathf.Abs(PlayerPrefs.GetFloat("MasterVolume", -1f) - 0.6f) < 0.01f, "volume is saved");
        GameSettings.SetScreenShake(false);
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null) { cam.Shake(0.5f, 0.5f); Check(Field<float>(cam, "shakeTimer") <= 0f, "screen shake OFF: shakes are ignored"); }
        GameSettings.SetGlowIntensity(0.3f);
        Check(Mathf.Abs(GameSettings.GlowIntensity - 0.3f) < 0.01f, "glow intensity is applied");
        hsa.SetEngageKmh(120f);
        Check(Mathf.Abs(hsa.engageKmh - 120f) < 0.1f && Mathf.Abs(hsa.releaseKmh - 110f) < 0.1f && Mathf.Abs(hsa.fullAssistKmh - 150f) < 0.1f, "assist start speed applies (release -10 / full +30)");
        Shot("check_settings"); yield return null; yield return null;

        // ---- 3) 戻る操作: 1回で一番手前だけ
        Call(gm, "HandleBackButtonForTest");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!SettingsPanel.IsVisible, "back closes the settings panel");
        typeof(GameManager).GetMethod("OpenDeckEdit").Invoke(gm, null);
        typeof(GameManager).GetMethod("OpenDeckEdit").Invoke(gm, null); // 連打
        yield return WaitTransition();
        Check(gm.IsOverlayOpen && Field<bool>(gm, "deckEditOpen"), "deck edit opens once (double tap ignored)");
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.3f);
        Call(gm, "HandleBackButtonForTest");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!SettingsPanel.IsVisible && Field<bool>(gm, "deckEditOpen"), "back with settings over a menu: only the settings close, the menu stays");
        Call(gm, "HandleBackButtonForTest");
        yield return WaitTransition();
        Check(!gm.IsOverlayOpen, "a second back closes the menu");

        // ---- 4) 往復(連打/すぐ戻る)で暗幕/入力遮断が残らない
        string[] opens = { "OpenDeckEdit", "OpenCardFusion", "OpenCharacterSelect", "OpenStageSelect" };
        for (int round = 0; round < 3; round++)
            foreach (var o in opens)
            {
                typeof(GameManager).GetMethod(o).Invoke(gm, null);
                yield return WaitTransition();
                Call(gm, "HandleBackButtonForTest");
                yield return WaitTransition();
            }
        Check(!gm.IsOverlayOpen && !Transitioning && !UiInputGate.Blocked && !SettingsPanel.IsVisible, "after 12 menu round trips: nothing left open, no cover, input not blocked");
        // 遷移の途中で画面サイズが変わっても残らない
        typeof(GameManager).GetMethod("OpenCharacterSelect").Invoke(gm, null);
        yield return new WaitForSecondsRealtime(0.1f);
        Screen.SetResolution(900, 1600, FullScreenMode.Windowed);
        yield return WaitTransition();
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("check_portrait_charselect"); yield return null; yield return null;
        Rect setP = R("GetHomeSettingsButtonRect");
        L($"[portrait] {Screen.width}x{Screen.height} gear={SettingsPanel.MenuGearRect()}");
        Check(Inside(SettingsPanel.MenuGearRect(), new Rect(0, 0, Screen.width, Screen.height)), "portrait: menu settings button inside the screen");
        Call(gm, "HandleBackButtonForTest");
        yield return WaitTransition();
        yield return new WaitForSecondsRealtime(0.3f);
        Rect m2 = R("GetHomeMileRect"), s2 = R("GetHomeSettingsButtonRect"), u2 = R("GetHomeMultiButtonRect");
        Check(!m2.Overlaps(s2) && !m2.Overlaps(u2) && !s2.Overlaps(u2) && Inside(s2, new Rect(0, 0, Screen.width, Screen.height)), $"portrait home: MILE/設定/マルチ do not overlap and fit ({s2}, {u2})");
        Shot("check_portrait_home"); yield return null; yield return null;
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("check_portrait_settings"); yield return null; yield return null;
        SettingsPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.3f);
        Screen.SetResolution(1600, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(!Transitioning && !UiInputGate.Blocked && !gm.IsOverlayOpen, "after resizing during a transition: no cover/blocker left");

        // ---- 5) 扉の出発 → カウントダウン(光が引くまで始めない/走り出さない)
        typeof(GameManager).GetMethod("OpenStageSelect").Invoke(gm, null);
        yield return WaitTransition();
        var ss = FindFirstObjectByType<StageSelectUI>(FindObjectsInactive.Include);
        Call(ss, "Confirm");
        yield return null;
        Check(Transitioning && ScreenTransitionManager.Instance.CurrentStyle == ScreenTransitionManager.Style.DoorLight, "depart uses the door-light transition");
        bool startedDuring = false, labelDuring = false; float distDuring = 0f;
        float tt = 0f;
        while (Transitioning && tt < 3f)
        {
            if (gm.HasStarted) { startedDuring = true; distDuring = Mathf.Max(distDuring, gm.MaxDistance); if (!string.IsNullOrEmpty(gm.CountdownLabel)) labelDuring = true; }
            yield return null; tt += Time.unscaledDeltaTime;
        }
        L($"[depart] transition {tt:F2}s, run started under the light={startedDuring}, countdown shown under the light={labelDuring}, distance during={distDuring:F2}m");
        Check(tt > 0.6f && tt < 1.3f, $"door-light transition lasts about 0.7-1s ({tt:F2}s)");
        Check(startedDuring && !labelDuring && distDuring <= 0.01f, "the countdown starts after the light clears and nobody runs under it");
        yield return new WaitForSecondsRealtime(0.4f);
        Check(gm.CountdownActive && gm.CountdownLabel == "3", "then the normal 3-2-1 countdown");
        yield return new WaitForSecondsRealtime(3.5f);
        // ---- 6) ポーズ中の設定: 開閉が止まらない/ポーズのまま/閉じたら元のポーズへ
        typeof(GameManager).GetField("showPauseMenu", NPI).SetValue(gm, true);
        TimeControl.Pause(typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        yield return new WaitForSecondsRealtime(0.2f);
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(SettingsPanel.IsOpen && TimeControl.IsPaused, "settings open over the pause menu (time stays paused)");
        Call(gm, "HandleBackButtonForTest");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!SettingsPanel.IsVisible && Field<bool>(gm, "showPauseMenu") && TimeControl.IsPaused, "back closes only the settings; the pause menu stays");
        Call(gm, "HandleBackButtonForTest");
        yield return new WaitForSecondsRealtime(0.2f);
        Check(!Field<bool>(gm, "showPauseMenu") && !TimeControl.IsPaused, "a second back resumes the run");
        Shot("check_run"); yield return null; yield return null;
    }

    static bool Inside(Rect a, Rect b) => a.xMin >= b.xMin - 0.5f && a.yMin >= b.yMin - 0.5f && a.xMax <= b.xMax + 0.5f && a.yMax <= b.yMax + 0.5f;

    Rect Frac(string spec)
    {
        var v = System.Array.ConvertAll(spec.Split(','), float.Parse);
        Rect bg = Field<Rect>(gm, "bgRoomRect");
        return new Rect(bg.x + bg.width * v[0], bg.y + bg.height * v[1], bg.width * (v[2] - v[0]), bg.height * (v[3] - v[1]));
    }
}
#endif

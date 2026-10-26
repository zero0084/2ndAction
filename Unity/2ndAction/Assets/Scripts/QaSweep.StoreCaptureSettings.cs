#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 設定紹介の動画(第2トレーラー、2026-10-23)の撮影: -storeCapture <dir> -scShots set_auto,set_volume,set_display[,set_portrait]
// 実際の設定の画面を開き、項目を切り替える様子(選んだ方が明るくなる)と、閉じた後のプレイを続けて撮る。
// 切り替えは設定の画面のボタンと同じ処理を呼ぶ(HighSpeedAssist / AudioManager / GameSettings / PortraitRunView の Set)。
// set_portrait は縦長のウィンドウ(例 608x1080)で撮る(縦画面のラン表示は縦の画面の時だけ効くため)。
// 2026-10-26: 縦画面のラン表示の設定は向きを変えられる環境(Android)だけに出る → set_portrait は -caps android を付けて撮る
public partial class QaSweep
{
    static readonly System.Reflection.FieldInfo ScSetScroll = typeof(SettingsPanel).GetField("scroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static System.Reflection.MethodInfo ScMeasure(string n) => typeof(SettingsPanel).GetMethod(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // 設定の画面を開いて、y(内容の上からの位置)が見える所までなめらかに送る
    IEnumerator ScOpenSettings(float y, float scrollSec = 0.5f)
    {
        SettingsPanel.OpenStatic();
        yield return ScWait(0.45f);
        var p = SettingsPanel.Instance;
        if (p == null || ScSetScroll == null) yield break;
        int n = Mathf.Max(1, Mathf.RoundToInt(scrollSec * 60f));
        for (int i = 1; i <= n; i++)
        {
            float k = Mathf.SmoothStep(0f, 1f, i / (float)n);
            ScSetScroll.SetValue(p, new Vector2(0f, y * k));
            yield return null;
        }
    }

    float ScMeasureOf(string method) { var m = ScMeasure(method); var p = SettingsPanel.Instance; return m != null && p != null ? (float)m.Invoke(p, null) : 0f; }

    // オート / 自動攻撃 / 自動回避: OFF で少し走る → 設定で ON にする → 閉じて、高速のまま自動で跳び/攻撃する
    IEnumerator ScSetAutoShot(string name)
    {
        yield return ScBegin("swordsman", "wasteland_road", 6150f, 6, false);
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) { hsa.SetEnabled(false); hsa.SetAutoAttack(false); hsa.SetAutoAvoid(false); hsa.SetAutoInBoss(false); hsa.SetEngageKmh(HighSpeedAssist.MinEngageKmh, false); } // 開始速度は最低(閉じた直後から自動で動く)
        float zone = ScFlatStage(40f, 160f); // OFF の間は平らな所(穴に落ちない)
        yield return ScWaitReach(zone);
        ScRecStart(name);
        yield return ScWait(1.2f);
        // 横画面: 右の列の「表示」の下に「操作」
        yield return ScOpenSettings(ScMeasureOf("MeasureDisplay") + 10f);
        yield return ScWait(0.6f);
        hsa.SetEnabled(true); ScStill("auto_on");
        yield return ScWait(0.7f);
        hsa.SetAutoAttack(true);
        yield return ScWait(0.6f);
        hsa.SetAutoAvoid(true); ScStill("avoid_on");
        yield return ScWait(0.8f);
        SettingsPanel.CloseStatic();
        // 閉じた後は普通の地形(穴/障害物)を自動で越えて攻撃する
        var tm = TerrainManager.Instance;
        if (tm != null) { tm.forceFlatAt = null; tm.skyAllowedAt = null; tm.ClearResumeFlatZone(); }
        for (int i = 0; i < 7; i++) { yield return ScWait(1f); ScStill($"play{i}"); }
        yield return ScRecStop();
        yield return ScEnd();
    }

    // 音量: 設定の「音」で BGM と SE を動かす(この場面だけ BGM も録る)
    IEnumerator ScSetVolumeShot(string name)
    {
        yield return ScBegin("swordsman", "wasteland_road", 6150f, 6, true);
        yield return new WaitForSeconds(2f);
        var am = AudioManager.Instance;
        bool keepMute = scMuteBgm; scMuteBgm = false; ScApplyBgmMute();
        if (am != null) { am.SetBgmVolumeLevel(AudioManager.MaxVolumeLevel); am.SetSfxVolumeLevel(AudioManager.MaxVolumeLevel); }
        ScRecStart(name);
        yield return ScWait(1.5f);
        yield return ScOpenSettings(0f, 0.1f);
        yield return ScWait(0.7f);
        // 設定の画面と同じ段(0〜4)で動かす: BGM 4 → 1 → 3、SE 4 → 3
        foreach (int lv in new[] { 3, 2, 1 }) { am.SetBgmVolumeLevel(lv); yield return ScWait(0.35f); }
        ScStill("bgm_down");
        yield return ScWait(1.3f); // 2026-10-24 エステル: 25% の状態を1秒以上聞かせてから戻す
        foreach (int lv in new[] { 2, 3 }) { am.SetBgmVolumeLevel(lv); yield return ScWait(0.35f); }
        yield return ScWait(0.3f);
        am.SetSfxVolumeLevel(3); ScStill("sfx");
        yield return ScWait(0.8f);
        SettingsPanel.CloseStatic();
        yield return ScWait(2.5f);
        yield return ScRecStop();
        if (am != null) { am.SetBgmVolume(1f); am.SetSfxVolume(1f); }
        scMuteBgm = keepMute; ScApplyBgmMute();
        yield return ScEnd();
    }

    // 表示(横画面): 画面揺れ / 障害物の警告 / 発光演出 を切り替える様子
    IEnumerator ScSetDisplayShot(string name)
    {
        yield return ScBegin("dual_blade", "wasteland_road", 24150f, 8, true);
        yield return new WaitForSeconds(2f);
        ScRecStart(name);
        yield return ScWait(1.2f);
        // 画面揺れ〜発光演出が見える所まで。2026-10-26: PC では「画面の向き/縦画面のラン表示」の行が無い(PlatformCaps)ので右の列の一番上に来る → 送らない
        yield return ScOpenSettings(PlatformCaps.SupportsOrientation ? 230f : 0f, 0.4f);
        yield return ScWait(0.6f);
        GameSettings.SetScreenShake(false);
        yield return ScWait(0.6f);
        GameSettings.SetObstacleWarn(2); ScStill("warn_outline");
        yield return ScWait(0.6f);
        for (int i = 0; i <= 40; i++) { GameSettings.SetGlowIntensity(Mathf.Lerp(1f, 0.4f, i / 40f), false); yield return null; }
        ScStill("glow");
        yield return ScWait(0.7f);
        SettingsPanel.CloseStatic();
        yield return ScWait(3f);
        yield return ScRecStop();
        GameSettings.SetScreenShake(true); GameSettings.SetObstacleWarn(1); GameSettings.SetGlowIntensity(1f);
        yield return ScEnd();
    }

    // 縦画面のラン表示(縦長のウィンドウで): 横から見る → 上下2段 → 斜め上から。設定を閉じた時に切り替わる
    IEnumerator ScSetPortraitShot(string name)
    {
        PortraitRunView.Set(PortraitRunView.Side); PortraitRunView.NotifyIfPending();
        yield return ScBegin("swordsman", "wasteland_road", 6150f, 6, true);
        yield return new WaitForSeconds(2.5f);
        ScRecStart(name);
        yield return ScWait(2.2f); ScStill("side");
        // 縦画面: 1列(音 → 言語 → 表示)。「縦画面のラン表示」は表示の中の下の方
        float y = ScMeasureOf("MeasureAudio") + 16f + ScMeasureOf("MeasureLanguage") + 16f + ScMeasureOf("MeasureDisplay") - 600f;
        foreach (var mode in new[] { PortraitRunView.Fold, PortraitRunView.Oblique })
        {
            yield return ScOpenSettings(y, 0.4f);
            yield return ScWait(0.6f);
            PortraitRunView.Set(mode);
            yield return ScWait(0.8f);
            SettingsPanel.CloseStatic();
            yield return ScWait(3.0f); ScStill(mode == PortraitRunView.Fold ? "fold" : "oblique");
        }
        yield return ScRecStop();
        PortraitRunView.Set(PortraitRunView.Side); PortraitRunView.NotifyIfPending();
        yield return ScEnd();
    }
}
#endif

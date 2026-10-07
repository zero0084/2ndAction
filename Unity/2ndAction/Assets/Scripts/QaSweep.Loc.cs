#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 言語(2026-10-07)の自動テスト: -qaLoc <dir> [-qaLocLangs ja,en,de,ar,...] [-qaLocShots 1]
//  A 端末の言語の読み替え / 対応外は英語 / 保存と優先 / 再起動なしの切り替え(ホームの文字がその場で変わる)
//  B 訳の欠け → 英語へ、開発版で記録 / 右から左(アラビア文字のつながり + 並び)
//  C 各言語で主な画面を開いて撮る。表に無い日本語が画面に出たら記録(言語ごとの報告ファイル)
public partial class QaSweep
{
    IEnumerator LocModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        bool hadPref = SaveStore.HasKey(Loc.PrefKey); string pref0 = SaveStore.GetString(Loc.PrefKey, "");
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true; // 画面を全部見るため(正式な解放は変えない)

        // ---- A
        L("== A ==");
        Check(Loc.MapTag("zh-TW") == "zh-Hant" && Loc.MapTag("zh-CN") == "zh-Hans" && Loc.MapTag("zh-Hant-HK") == "zh-Hant" && Loc.MapTag("pt-PT") == "pt-BR" && Loc.MapTag("in-ID") == "id"
            && Loc.MapTag("fil-PH") == "fil" && Loc.MapTag("tl") == "fil" && Loc.MapTag("ja-JP") == "ja" && Loc.MapTag("fa-IR") == "fa" && Loc.MapTag("nl-NL") == "en" && Loc.MapTag("") == "en",
            "A: device language tags map to the 30 languages, unsupported -> English");
        Check(Loc.Languages.Length == 30 && Loc.Languages.Select(l => l.code).Distinct().Count() == 30, "A: 30 languages");
        L($"[A] device language here: {Loc.DetectDevice()}");
        Loc.Set("de");
        Check(SaveStore.GetString(Loc.PrefKey, "") == "de" && Loc.UserChose, "A: the chosen language is saved");
        Check(!PlayerPrefs.HasKey("test:" + Loc.PrefKey), "A: the language is a shared setting (not per test data)");
        string deSettings = Loc.Auto("設定");
        Check(deSettings != "設定" && !string.IsNullOrEmpty(deSettings), $"A: 'Settings' in German: {deSettings}");
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("loc_home_de");
        Loc.Set("ja");
        Check(Loc.Auto("設定") == "設定", "A: switching back to Japanese changes the text at once (no restart)");
        Loc.Set("fr");
        Check(Loc.Auto("設定") != "設定", "A: live switch to French");
        // 起動時の読み込み: 保存した言語を優先
        Loc.Set("ja", save: false);
        Check(SaveStore.GetString(Loc.PrefKey, "") == "fr", "A: a non-saving switch keeps the stored choice");

        // ---- B
        L("== B ==");
        Loc.Set("de");
        string key = "練習をやめる";
        Loc.DebugDropFromCurrent(key);
        string fb = Loc.Auto(key);
        Check(fb == Loc.EnglishOf(key) && LocDebug.MissingKeys.Contains(key), $"B: a missing translation falls back to English and is recorded ({fb})");
        Loc.Set("ar");
        string ar = Loc.Auto("設定");
        Check(ar.Any(c => c >= 'ﹰ' && c <= '﻿') || ar.Any(c => c >= 'ﭐ' && c <= '﷿'), $"B: Arabic text is shaped into joined letter forms ({ar})");
        string mixed = ArabicShaper.ForDisplay("المسافة 1,000m");
        Check(mixed.StartsWith("1,000m") , $"B: numbers keep their direction inside right-to-left text ({mixed})");

        // ---- C 画面
        string[] langs = Arg("-qaLocLangs", "ja,en,de,ar,th,hi,zh-Hans,ru,fa,my").Split(',');
        foreach (var code in langs)
        {
            Loc.Set(code);
            yield return ReloadHome();
            LocDebug.Reset();
            LocTextBinder.ApplyAll(); // シーンに焼き込まれた文も報告に入れる
            yield return new WaitForSecondsRealtime(0.6f);
            Shot($"loc_{code}_home");
            SettingsPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.5f); Shot($"loc_{code}_settings");
            SettingsPanel.Instance.Close(); yield return new WaitForSecondsRealtime(0.5f);
            gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.2f); Shot($"loc_{code}_stages");
            FindFirstObjectByType<StageSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.2f);
            gm.OpenCharacterSelect(); yield return new WaitForSecondsRealtime(1.2f); Shot($"loc_{code}_chars");
            FindFirstObjectByType<CharacterSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.2f);
            gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.2f); Shot($"loc_{code}_deck");
            gm.CloseDeckEdit(); yield return new WaitForSecondsRealtime(1.2f);
            // ラン: 練習の説明 → 結果(倒れた)
            TutorialLauncher.Launch(false, "qa loc");
            yield return WaitTut(() => TutorialRun.Instance != null && !TutorialLauncher.Covering && TutorialRun.PanelOpen, 25f);
            yield return new WaitForSecondsRealtime(0.8f);
            Shot($"loc_{code}_tutorial");
            TutorialRun.Instance?.Quit("qa loc");
            yield return WaitTut(() => GameManager.Instance != null && !TutorialMode.Active && !TutorialLauncher.Covering, 12f);
            yield return new WaitForSecondsRealtime(0.8f);
            gm = GameManager.Instance;
            yield return BeginRun("swordsman", "wasteland_road");
            yield return new WaitForSecondsRealtime(1f);
            Shot($"loc_{code}_run");
            stopKeepAlive = true; yield return null;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            for (int i = 0; i < 40 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(true, "QA", 9999); yield return new WaitForSecondsRealtime(0.05f); }
            yield return WaitTut(() => gm.ResultShown, 6f);
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"loc_{code}_result");
            yield return EndRun();
            LocDebug.WriteReport(System.IO.Path.Combine(outDir, $"loc_report_{code}.txt"));
            L($"[C] {code}: missing {LocDebug.MissingKeys.Count}, untranslated Japanese shown {LocDebug.UntranslatedShown.Count}");
            if (code != "ja") Check(LocDebug.UntranslatedShown.Count <= 15, $"C: {code}: little untranslated Japanese on the main screens ({LocDebug.UntranslatedShown.Count})");
            yield return ReloadHome();
        }

        UnlockRules.DevUnlockAll = false;
        if (hadPref) SaveStore.SetString(Loc.PrefKey, pref0); else SaveStore.DeleteKey(Loc.PrefKey);
        Loc.Set(hadPref ? pref0 : Loc.DetectDevice(), save: hadPref);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

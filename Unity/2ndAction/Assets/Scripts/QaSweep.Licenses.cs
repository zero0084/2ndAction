#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// ライセンスの画面(2026-10-23): -qaLicenses <dir> [-lcLangs ja,en]
//  ・Resources/Licenses/ThirdPartyNotices.txt が入っていて、MS Basic の謝辞・著作権表示・MIT の本文がある
//  ・設定の「データとプライバシー」から開ける画面が出て、戻る(Android の戻る/Esc)で閉じる。撮影(上端と最後まで送った所)
// 2026-10-26: 設定の「このゲームについて」(バージョン/制作クレジット/ライセンス/お問い合わせ)とホームの © も確かめる。
//  問い合わせは -noOpenUrl で本当には開かない(開いたことと件名・本文の下書きだけ確かめる)
public partial class QaSweep
{
    IEnumerator LicensesModeQa()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string t = LicensesUi.NoticeText;
        L($"[licenses] notice text {t.Length} chars");
        string[] must =
        {
            "MS Basic", "Current version: 0.2", "FluidR3 (original version) by Frank Wen Copyright (c) 2000-02",
            "Mono conversion (FluidR3Mono) by Michael Cowgill Copyright (c) 2014-17",
            "Adaptation for MuseScore_General.sf2 by S. Christian Collins Copyright (c) 2018-19",
            "Temple Blocks instrument provided by Ethan Winer Copyright (c) 2002",
            "Drumline Cymbals provided by Michael Schorsch Copyright (c) 2016",
            "The acknowledgements and copyright notices above must be included in any derivative work.",
            "Copyright (c) 2000-2002, 2008 Frank Wen", "Permission is hereby granted, free of charge",
            "The above copyright notice and this permission notice shall be", "THE SOFTWARE IS PROVIDED \"AS IS\"",
        };
        foreach (var m in must) Check(t.Contains(m), $"the notice contains: {m}");
        // 2026-10-26: 調べ直した第三者の表記(両方のビルド)と、Android だけの表記
        foreach (var m in new[] { "Newtonsoft.Json", "websocket-sharp", "unity-websocket-webgl", "Google Mobile Ads Unity Plugin", "Unity Companion License", "Apache License" }) Check(t.Contains(m), $"the notice contains: {m}");
        bool mobile = PlatformCaps.Profile == "mobile";
        Check(t.Contains("PART B - ANDROID BUILD ONLY") == mobile, $"the Android-only part is shown only on Android (profile {PlatformCaps.Profile})");
        if (mobile) foreach (var m in new[] { "Open source software included in Google Play services", "androidx.core:core", "checker-qual", "jsr305" }) Check(t.Contains(m), $"android notice contains: {m}");
        // バージョン・問い合わせ・クレジット(言語に関係しない所)
        L($"[about] version='{AppInfo.VersionLabel}' copyright='{AppInfo.Copyright}' subject='{AppInfo.ContactSubject}'");
        Check(AppInfo.VersionLabel.StartsWith(Application.version) && !string.IsNullOrEmpty(Application.version), "about: the version comes from the build (Application.version)");
        Check(AppInfo.Copyright == "© 2026 MIXGame", "about: copyright text is (c) 2026 MIXGame with the real (c) sign");
        Check(AppInfo.ContactSubject == "[OneMoreMile] お問い合わせ" && AppInfo.ContactBody.Contains(AppInfo.VersionLabel) && AppInfo.ContactBody.Contains(SystemInfo.operatingSystem), "about: mail subject and body draft (version + environment)");
        int m0 = AppInfo.MailOpened;
        AppInfo.OpenContactMail();
        Check(AppInfo.MailOpened == m0 + 1, "about: contact mail opens (test: not really opened)");
        string ct = LicensesUi.CreditsText;
        L($"[about] credits {ct.Length} chars");
        foreach (var m in new[] { "CREATED BY", "マジル旅人", "SOUNDFONT", "MS BASIC", AppInfo.Copyright }) Check(ct.Contains(m), $"credits contain: {m}");
        // ホームの ©(撮影で確かめる)
        Shot("home_copyright");
        foreach (var lang in Arg("-lcLangs", "ja,en").Split(','))
        {
            Loc.Set(lang, false);
            yield return new WaitForSecondsRealtime(0.4f);
            SettingsPanel.OpenStatic();
            yield return new WaitForSecondsRealtime(0.6f);
            Shot($"licenses_{lang}_settings");
            yield return null;
            LicensesUi.Open();
            yield return new WaitForSecondsRealtime(0.6f);
            Check(LicensesUi.IsOpen, $"{lang}: the license screen opens");
            Shot($"licenses_{lang}_top");
            yield return null;
            var sc = (UiScroll)GetPrivate(FindFirstObjectByType<LicensesUi>(), "scroll");
            if (sc != null) sc.y = 1e6f; // 最後まで(Begin で上限に揃う)
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"licenses_{lang}_end");
            yield return null;
            gm.HandleBackButtonForTest(); // Android の戻る / Esc と同じ
            yield return new WaitForSecondsRealtime(0.5f);
            Check(!LicensesUi.IsOpen, $"{lang}: back closes the license screen");
            Check(SettingsPanel.IsVisible, $"{lang}: back closes only the license screen (settings stays open)");
            // 設定の一番下(このゲームについて)まで送る
            if (SettingsPanel.Instance != null) SetPrivate(SettingsPanel.Instance, "scroll", new Vector2(0f, 1e6f));
            yield return new WaitForSecondsRealtime(0.5f);
            Shot($"about_{lang}_settings");
            yield return null;
            // 制作クレジット: 開く → 上端と最後 → 戻るで閉じる
            LicensesUi.OpenCredits();
            yield return new WaitForSecondsRealtime(0.6f);
            Check(LicensesUi.IsOpen && LicensesUi.Showing == LicensesUi.Page.Credits, $"{lang}: the credits screen opens");
            Shot($"credits_{lang}_top");
            yield return null;
            var sc2 = (UiScroll)GetPrivate(FindFirstObjectByType<LicensesUi>(), "scroll");
            if (sc2 != null) sc2.y = 1e6f;
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"credits_{lang}_end");
            yield return null;
            gm.HandleBackButtonForTest();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(!LicensesUi.IsOpen && SettingsPanel.IsVisible, $"{lang}: back closes the credits screen (settings stays open)");
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.4f);
        }
        Loc.Set("ja", false);
    }
}
#endif

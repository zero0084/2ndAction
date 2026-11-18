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
        // 2026-11-18: クレジットは英語で統一(人名は英字)。PLAYTESTERS は Resources/Credits/Playtesters.txt の順
        foreach (var m in new[] { "CREATED BY", "AI PROGRAMMING", "SOUNDFONT", "MS Basic", "PLAYTESTERS", "SPECIAL THANKS", "Pentatuttulu", "Luka", AppInfo.Copyright }) Check(ct.Contains(m), $"credits contain: {m}");
        Check(System.Text.RegularExpressions.Regex.IsMatch(ct, "[\u3040-\u30ff\u4e00-\u9fff]") == false, "credits: no Japanese characters (names in English letters)");
        var tl = StaffCreditsData.ReadNames("Credits/Playtesters");
        Check(string.Join(",", tl) == "Nari,Moka,Soushi,Itsuki", $"credits: playtesters in this order and spelling (got {string.Join(",", tl)})");
        string ctJa = ct;
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
            CheckCreditsLayout(lang);
            Check(LicensesUi.CreditsText == ctJa, $"{lang}: credits text is the same in every language");
            yield return null;
            var sc2 = (UiScroll)GetPrivate(FindFirstObjectByType<LicensesUi>(), "scroll");
            if (sc2 != null) sc2.y = 1e6f;
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"credits_{lang}_end");
            {
                var scE = (UiScroll)GetPrivate(FindFirstObjectByType<LicensesUi>(), "scroll");
                var lastR = LicensesUi.LastCreditsLayout.Count > 0 ? LicensesUi.LastCreditsLayout[LicensesUi.LastCreditsLayout.Count - 1].rect : Rect.zero;
                Check(scE != null && scE.y + LicensesUi.LastCreditsViewHeight >= lastR.yMax + 4f, $"{lang}: scrolled to the end, the last line (copyright) is fully inside the view (scroll {scE?.y:F0} + view {LicensesUi.LastCreditsViewHeight:F0} >= {lastR.yMax:F0})");
            }
            if (lang == "en") yield return CreditsLongNameQa();
            yield return null;
            gm.HandleBackButtonForTest();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(!LicensesUi.IsOpen && SettingsPanel.IsVisible, $"{lang}: back closes the credits screen (settings stays open)");
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.4f);
        }
        Loc.Set("ja", false);
    }

    // 2026-11-18 クレジットの文字の揃え: 同じ種類の行は同じ字(大きさ・太さ・色・揃え)、行が重ならない、間隔が決まりどおり、内容の高さが最後の行より下
    void CheckCreditsLayout(string lang)
    {
        var lay = LicensesUi.LastCreditsLayout;
        Check(lay.Count > 10, $"{lang}: credits laid out ({lay.Count} lines, width {LicensesUi.LastCreditsWidth:F0})");
        if (lay.Count == 0) return;
        var seen = new System.Collections.Generic.Dictionary<LicensesUi.CreditKind, string>();
        bool same = true, ordered = true, gaps = true; float prevBottom = 0f;
        for (int i = 0; i < lay.Count; i++)
        {
            var (line, r) = lay[i];
            var st = LicensesUi.CreditStyle(line.kind);
            string sig = $"{st.fontSize}/{st.fontStyle}/{st.normal.textColor}/{st.alignment}/{(st.font != null ? st.font.name : "default")}";
            if (seen.TryGetValue(line.kind, out var s0) && s0 != sig) same = false; else seen[line.kind] = sig;
            if (i > 0 && r.y < prevBottom - 0.5f) ordered = false;
            if (i > 0 && Mathf.Abs(r.y - prevBottom - line.gapBefore) > 0.5f) gaps = false;
            if (!st.wordWrap || st.alignment != TextAnchor.UpperCenter) same = false;
            prevBottom = r.yMax;
        }
        Check(same, $"{lang}: every heading / name / description uses one style each (centered, wrapping): {string.Join(" ", seen)}");
        Check(ordered, $"{lang}: credit lines do not overlap");
        Check(gaps, $"{lang}: spacing between lines follows the fixed gaps");
        Check(LicensesUi.LastCreditsContentHeight >= prevBottom + LicensesUi.CreditsPadBottom - 0.5f, $"{lang}: content height {LicensesUi.LastCreditsContentHeight:F0} covers the last line {prevBottom:F0} + padding");
        var names = lay.FindAll(x => x.line.kind == LicensesUi.CreditKind.Name).ConvertAll(x => x.line.text);
        int pi = names.IndexOf("Nari");
        Check(pi >= 0 && pi + 3 < names.Count && names[pi + 1] == "Moka" && names[pi + 2] == "Soushi" && names[pi + 3] == "Itsuki", $"{lang}: playtesters shown in order");
        int roles = lay.FindAll(x => x.line.kind == LicensesUi.CreditKind.Role && x.line.text == "SPECIAL THANKS").Count;
        Check(roles == 1, $"{lang}: consecutive SPECIAL THANKS shown under one heading ({roles})");
    }

    // 長い名前: 字を小さくせず、枠の幅で折り返す(名前の行の高さが1行より高くなる)。試験用に一時的に足して、後で戻す
    IEnumerator CreditsLongNameQa()
    {
        var d = StaffCreditsData.LoadOrDefault();
        var e = d.entries.Find(x => x != null && x.role == "PLAYTESTERS");
        if (e == null) { Check(false, "long name: PLAYTESTERS entry"); yield break; }
        var keep = e.names;
        const string longName = "Maximilian Alexander Featherstonehaugh-Worthington of the Very Long Name Testing Society";
        e.names = new System.Collections.Generic.List<string>(keep) { longName }.ToArray();
        yield return new WaitForSecondsRealtime(0.4f);
        var lay = LicensesUi.LastCreditsLayout;
        var one = lay.Find(x => x.line.text == "Nari"); var lr = lay.Find(x => x.line.text == longName);
        Check(lr.rect.height > one.rect.height * 1.5f && lr.rect.width <= LicensesUi.LastCreditsWidth + 0.5f, $"long name wraps inside the frame (height {lr.rect.height:F0} vs one line {one.rect.height:F0}, width {lr.rect.width:F0} <= {LicensesUi.LastCreditsWidth:F0})");
        Check(LicensesUi.CreditStyle(LicensesUi.CreditKind.Name).fontSize == Mathf.RoundToInt(LicensesUi.CreditsNameSize), "long name keeps the same font size as other names");
        var sc = (UiScroll)GetPrivate(FindFirstObjectByType<LicensesUi>(), "scroll");
        if (sc != null) sc.y = lr.rect.y - 40f;
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("credits_en_longname");
        yield return null;
        e.names = keep;
        yield return new WaitForSecondsRealtime(0.2f);
    }
}
#endif

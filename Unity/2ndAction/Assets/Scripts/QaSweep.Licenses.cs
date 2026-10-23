#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// ライセンスの画面(2026-10-23): -qaLicenses <dir> [-lcLangs ja,en]
//  ・Resources/Licenses/ThirdPartyNotices.txt が入っていて、MS Basic の謝辞・著作権表示・MIT の本文がある
//  ・設定の「データとプライバシー」から開ける画面が出て、戻る(Android の戻る/Esc)で閉じる。撮影(上端と最後まで送った所)
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
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.4f);
        }
        Loc.Set("ja", false);
    }
}
#endif

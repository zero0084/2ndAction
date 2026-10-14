#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ポーズの画面の訳漏れの調査(2026-10-15): 専用の保存(-fileSave / -memSave)で行う。
//  -qaPauseLoc <dir> [-plShotLangs en,de,ar,ta,my]
//  ラン中にポーズの画面を開き、30言語で「表に無い日本語」「一部だけ訳されて日本語が残った文」を集める → pause_loc_survey.tsv
public partial class QaSweep
{
    IEnumerator PauseLocMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(1600f); // 持ち帰り予定の MILE とガチャの回数が出る所
        yield return new WaitForSecondsRealtime(2f);
        string keepLang = Loc.Current;
        var shotLangs = Arg("-plShotLangs", "en,de,ar,ta,my").Split(',');
        var all = new SortedDictionary<string, SortedSet<string>>();
        foreach (var code in Loc.Languages.Select(l => l.code).Where(c => c != "ja"))
        {
            Loc.Set(code, false); LocDebug.Reset();
            typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, true);
            yield return Settle(0.5f);
            if (System.Array.IndexOf(shotLangs, code) >= 0) { Shot($"pause_{code}"); yield return null; yield return null; }
            foreach (var s in LocDebug.UntranslatedShown.Concat(LocDebug.JapaneseShown))
            {
                if (!all.TryGetValue(s, out var set)) all[s] = set = new SortedSet<string>();
                set.Add(code);
            }
            typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, false);
            yield return null;
        }
        Loc.Set(keepLang, false);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# pause screen survey, screen {Screen.width}x{Screen.height}");
        foreach (var kv in all) sb.AppendLine($"JAPANESE_SHOWN\t{kv.Key.Replace("\n", "\\n")}\t{kv.Value.Count}\t{string.Join(",", kv.Value)}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "pause_loc_survey.tsv"), sb.ToString());
        L($"[pl] {all.Count} text(s) with Japanese left on the pause screen: {string.Join(" / ", all.Keys.Select(k => k.Replace("\n", "\\n")))}");
        yield return EndRun();
    }
}
#endif

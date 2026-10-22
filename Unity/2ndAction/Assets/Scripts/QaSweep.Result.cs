using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// RESULT の正式化(2026-10-22)の確認: -qaResult <dir> [-rsLangs ja,en,de,ar] (専用の保存 -fileSave で)。起動の画面の大きさで撮る。
//  W1 成功(初めての記録 = NEW BEST、ラン中の強化あり、距離の解放あり)/ W2 成功(BEST を超えない)/ F 失敗(MILE を失う、記録しない)
//  と強化の多い時(+N)を、言語ごとに撮る。距離/MILE/ホームへのボタンが画面の中にあるか、NEW BEST の判定を確かめる。
public partial class QaSweep
{
    IEnumerator ResultMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        string res = $"{Screen.width}x{Screen.height}";
        var langs = Arg("-rsLangs", "ja,en,de,ar").Split(',');
        int li = 0;
        foreach (var lang in langs)
        {
            Loc.Set(lang, false);
            // W1: 成功・NEW BEST(言語ごとに BEST が上がるので、距離は言語ごとに伸ばす)
            yield return ResultRun(true, 12000 + li * 1000, 9, 340, 120, 60);
            Check(gm.IsNewBestDistance, $"{lang}: W1 success beyond BEST shows NEW BEST");
            CheckResultLayout($"{lang} W1");
            Shot($"rs_{lang}_{res}_win_newbest"); yield return Frames(3);
            yield return EndRun();
            // W2: 成功・BEST を超えない
            yield return ResultRun(true, 3000, 3, 80, 0, 0);
            Check(!gm.IsNewBestDistance, $"{lang}: W2 success below BEST has no NEW BEST");
            CheckResultLayout($"{lang} W2");
            Shot($"rs_{lang}_{res}_win"); yield return Frames(3);
            yield return EndRun();
            // F: 失敗(この距離は記録しない、MILE は失う)
            double bestBefore = gm.GetStageBest("wasteland_road");
            yield return ResultRun(false, 99999, 5, 210, 40, 0);
            Check(!gm.IsNewBestDistance && gm.GetStageBest("wasteland_road") == bestBefore, $"{lang}: F failure does not touch BEST ({bestBefore:F0}) even beyond it");
            CheckResultLayout($"{lang} F");
            Shot($"rs_{lang}_{res}_failed"); yield return Frames(3);
            yield return EndRun();
            li++;
        }
        // 強化が多い時(+N)
        Loc.Set(langs[0], false);
        yield return ResultRun(true, 2000, 30, 50, 0, 0);
        CheckResultLayout("many cards");
        Shot($"rs_{langs[0]}_{res}_manycards"); yield return Frames(3);
        yield return EndRun();
    }

    IEnumerator ResultRun(bool win, double meters, int cards, int enemyMile, int bossMile, int distMile)
    {
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(1.2f);
        var t = typeof(GameManager);
        t.GetProperty("MaxDistance").SetValue(gm, (float)meters);
        t.GetProperty("MaxDistanceExact").SetValue(gm, meters);
        t.GetProperty("RunEnemyMile").SetValue(gm, enemyMile);
        t.GetProperty("RunBossMile").SetValue(gm, bossMile);
        t.GetProperty("RunDistanceMile").SetValue(gm, distMile);
        var hist = (List<CardDefinition>)t.GetField("upgradeHistory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gm);
        var all = CardDatabase.AllCards;
        for (int i = 0; i < cards && all.Count > 0; i++) hist.Add(all[(i * 7) % Mathf.Min(all.Count, 40)]);
        stopKeepAlive = true;
        if (win) gm.Win();
        else t.GetMethod("FinishRun", NP).Invoke(gm, null);
        yield return new WaitForSecondsRealtime(1.6f); // 共通のカード面を描き終えるまで
    }

    void CheckResultLayout(string tag)
    {
        Rect scr = new Rect(0f, 0f, Screen.width, Screen.height);
        Rect p = gm.LastResultPanelRect, h = gm.LastResultHomeRect;
        L($"[result] {tag}: panel {p} home {h} newBest={gm.IsNewBestDistance} mile={gm.RunMile} wallet={gm.TotalOwnedMile}");
        Check(p.width > 0f && scr.Contains(p.min) && scr.Contains(p.max - Vector2.one), $"{tag}: the result panel is inside the screen");
        Check(h.width > 0f && p.Contains(h.min) && p.Contains(h.max - Vector2.one), $"{tag}: the HOME button is inside the panel");
        Check(h.height >= 40f, $"{tag}: the HOME button is big enough ({h.height:F0}px)");
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// ホームの「続きから」「はじめから」(2026-10-14)。専用の保存(-fileSave / -memSave)で行う。
//  -qaHomeChoice <dir> -hcPhase shots|tap [-hcLangs ja,en,de,ru,fr,my]
//   shots … 中断中のランを作り、言語ごとにホームを撮る。札の位置(重ならない・画面の中・間隔・大きさ)と確認の画面を調べる
//   tap   … 実機/エミュレーター用: 押す場所を tap_req.txt に書き、外(adb)から本当に押してもらう。
//           はじめから → 確認 → キャンセル(中断データが残る)→ はじめから → はじめから出発(マップ選択へ、中断データだけが消え、MILE/カードは同じ)
//           → 中断中のランをもう一度作って「続きから」の札を押す → 同じ地点から再開
public partial class QaSweep
{
    IEnumerator HcMakeCheckpoint(float dist)
    {
        yield return BeginRun("swordsman", "wasteland_road");
        WarpTo(dist);
        yield return new WaitForSecondsRealtime(1.2f);
        stopKeepAlive = true;
        yield return null;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { Mathf.Max(1, gm.maxLives - 13) });
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        yield return ResumeGoHome();
        float w = 0f; while ((NoticeQueue.Open || w < 2f) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    static bool Overlap(Rect a, Rect b) => a.width > 0 && b.width > 0 && a.Overlaps(b);
    bool InsideScreen(Rect r) => r.xMin >= 0f && r.yMin >= 0f && r.xMax <= Screen.width && r.yMax <= Screen.height;

    void HcCheckLayout(string tag)
    {
        var vis = gm.LastHomeSignRects; var hit = gm.LastHomeSignHitRects;
        Rect c = vis[4], n = vis[5], ch = hit[4], nh = hit[5];
        L($"[hc] {tag}: screen {Screen.width}x{Screen.height} continue {c} (hit {ch}) new {n} (hit {nh}) deck-sign {vis[1]}");
        Check(c.width > 0 && n.width > 0, $"{tag}: both choices are shown");
        Check(!Overlap(ch, nh) && nh.yMin - ch.yMax >= 6f, $"{tag}: the tap areas do not overlap and are spaced ({nh.yMin - ch.yMax:F0}px)");
        Check(InsideScreen(ch) && InsideScreen(nh), $"{tag}: both tap areas are on the screen");
        bool clash = false;
        for (int i = 0; i < 4; i++) if (Overlap(vis[i], c) || Overlap(vis[i], n)) { clash = true; L($"   overlaps sign {i}: {vis[i]}"); }
        Check(!clash, $"{tag}: no overlap with the other signs");
        if (Debug.isDebugBuild)
        {
            var dbg = (Rect)typeof(GameManager).GetMethod("GetHomeDebugButtonRect", NP).Invoke(gm, null);
            Check(!Overlap(dbg, ch) && !Overlap(dbg, nh), $"{tag}: no overlap with the DEBUG button (dev only) {dbg}");
        }
        float baseH = vis[1].height; // 1行の通常の札(デッキ編集)
        Check(n.height >= baseH * 1.15f && c.height >= baseH * 1.2f, $"{tag}: larger than a normal sign (continue {c.height / Mathf.Max(1f, baseH):F2}x, new {n.height / Mathf.Max(1f, baseH):F2}x of {baseH:F0}px)");
        Check(ch.height > c.height && nh.height > n.height, $"{tag}: the tap areas are larger than the drawn signs");
    }

    IEnumerator HomeChoiceMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active || SaveProfile.IsTest;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        string phase = Arg("-hcPhase", "shots");
        L($"[hc] phase={phase} screen {Screen.width}x{Screen.height}");

        // 中断データが無い時: 「出発」だけ(続きから/はじめからは出ない)
        RunCheckpoint.Clear();
        yield return new WaitForSecondsRealtime(0.6f);
        Check(!gm.HomeShowsContinueChoice && gm.LastHomeSignRects[4].width > 0 && gm.LastHomeSignRects[5].width <= 0, "no suspended run: the normal 'depart' sign only");
        Shot("home_no_continue"); yield return null; yield return null;

        yield return HcMakeCheckpoint(1500f);

        Check(RunCheckpoint.HasActiveRun, "a suspended run exists");
        yield return new WaitForSecondsRealtime(0.6f);

        if (phase == "verify")
        {
            // 2026-10-14: エステルのレビュー後の確認。30言語 × (中断中のランあり: 距離の桁 0 / 1,511 / 99,999 / 1,000,000 / 確認の画面 / 中断データなし)
            string keepLang = Loc.Current;
            var codes = Arg("-hcLangs", "") != "" ? Arg("-hcLangs", "").Split(',') : Loc.Languages.Select(l => l.code).ToArray();
            var cpd = RunCheckpoint.Load();
            float[] dists = { 0f, 1511f, 99999f, 1000000f };
            foreach (var code in codes)
            {
                Loc.Set(code, false);
                foreach (var d in dists)
                {
                    cpd.checkpointDistance = d; RunCheckpoint.Save(cpd); // 表示の確認だけ(専用の保存の中断データの距離を書き換える)
                    yield return new WaitForSecondsRealtime(0.3f);
                    HcCheckLayout($"{code} {d:N0}m");
                    for (int i = 0; i < 4; i++) { var r = gm.LastHomeSignRects[i]; if (r.width > 0) Check(InsideScreen(r), $"{code}: sign {i} is on the screen {r}"); }
                    if (d == 1511f || d == 1000000f) { Shot($"v_{code}_{d:0}"); yield return null; yield return null; }
                }
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, true);
                yield return new WaitForSecondsRealtime(0.35f);
                Rect y = gm.LastNewRunYesRect, n = gm.LastNewRunNoRect;
                L($"[hc] {code} confirm: text {gm.LastNewRunMsgHeight:F0} / area {gm.LastNewRunMsgAreaHeight:F0}");
                if (Loc.IsRtl)
                {
                    string Cp(string s) => string.Join(" ", s.Take(14).Select(ch => ((int)ch).ToString("X4")));
                    string q = Loc.T("中断中のランを終了して、はじめから出発しますか？"), qa = Loc.Auto(q), sign = Loc.Auto("はじめから");
                    L($"[hc] {code} rtl: T='{Cp(q)}' Auto(T)='{Cp(qa)}' same={q == qa} sign='{Cp(sign)}'"); Check(q == qa, $"{code}: an already shaped right-to-left text is not reordered again");
                }
                Check(gm.LastNewRunMsgHeight <= gm.LastNewRunMsgAreaHeight, $"{code}: the confirm text fits without scrolling ({gm.LastNewRunMsgHeight:F0} <= {gm.LastNewRunMsgAreaHeight:F0})");
                Check(InsideScreen(y) && InsideScreen(n) && !Overlap(y, n) && y.height >= 60f, $"{code}: confirm buttons on the screen, separate, tall enough");
                Shot($"v_{code}_confirm"); yield return null; yield return null;
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, false);
                yield return new WaitForSecondsRealtime(0.2f);
            }
            cpd.checkpointDistance = 1500f; RunCheckpoint.Save(cpd);
            // 中断データなし: 「出発」だけ
            RunCheckpoint.Clear();
            foreach (var code in codes)
            {
                Loc.Set(code, false);
                yield return new WaitForSecondsRealtime(0.35f);
                Check(gm.LastHomeSignRects[4].width > 0 && gm.LastHomeSignRects[5].width <= 0 && InsideScreen(gm.LastHomeSignRects[4]), $"{code}: no suspended run -> 'depart' only");
                Shot($"v_{code}_depart"); yield return null; yield return null;
            }
            Loc.Set(keepLang, false);
            yield break;
        }

        if (phase == "loc")
        {
            // 2026-10-14: ホームの訳漏れの調査。30言語 × (中断中のランあり / 確認の画面 / 中断データなし)で、表に無い日本語が画面に出たかを集める
            string keepLang = Loc.Current;
            var shotLangs = Arg("-hcShotLangs", "en,de,ta,my,ar").Split(',');
            var all = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedSet<string>>();
            var unknown = new System.Collections.Generic.SortedSet<string>();
            void Collect(string code)
            {
                foreach (var s in LocDebug.UntranslatedShown) { if (!all.TryGetValue(s, out var set)) all[s] = set = new System.Collections.Generic.SortedSet<string>(); set.Add(code); }
                foreach (var s in LocDebug.UnknownLabels) unknown.Add(code + "\t" + s);
            }
            var codes = Loc.Languages.Select(l => l.code).Where(c => c != "ja").ToArray();
            foreach (var code in codes)
            {
                Loc.Set(code, false); LocDebug.Reset();
                yield return new WaitForSecondsRealtime(0.5f);
                bool shot = System.Array.IndexOf(shotLangs, code) >= 0;
                if (shot) { Shot($"loc_choice_{code}"); yield return null; yield return null; }
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, true);
                yield return new WaitForSecondsRealtime(0.35f);
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, false);
                yield return null;
                Collect(code);
            }
            RunCheckpoint.Clear();
            foreach (var code in codes)
            {
                Loc.Set(code, false); LocDebug.Reset();
                yield return new WaitForSecondsRealtime(0.5f);
                if (System.Array.IndexOf(shotLangs, code) >= 0) { Shot($"loc_depart_{code}"); yield return null; yield return null; }
                Collect(code);
            }
            Loc.Set(keepLang, false);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# home untranslated survey, screen {Screen.width}x{Screen.height}, languages {codes.Length}");
            foreach (var kv in all) sb.AppendLine($"UNTRANSLATED\t{kv.Key.Replace("\n", "\\n")}\t{kv.Value.Count}\t{string.Join(",", kv.Value)}");
            foreach (var u in unknown) sb.AppendLine($"UNKNOWN_EN\t{u}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "home_loc_survey.tsv"), sb.ToString());
            L($"[hc] loc survey: {all.Count} untranslated text(s) on the home: {string.Join(" / ", all.Keys.Select(k => k.Replace("\n", "\\n")))}");
            yield break;
        }

        if (phase == "shots")
        {
            string keepLang = Loc.Current;
            foreach (var code in Arg("-hcLangs", "ja,en,de,ru,fr,my").Split(','))
            {
                Loc.Set(code, false);
                yield return new WaitForSecondsRealtime(0.7f);
                HcCheckLayout($"{code}");
                Shot($"home_choice_{code}"); yield return null; yield return null;
                // 確認の画面
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, true);
                yield return new WaitForSecondsRealtime(0.4f);
                Rect y = gm.LastNewRunYesRect, n = gm.LastNewRunNoRect;
                Check(InsideScreen(y) && InsideScreen(n) && !Overlap(y, n) && y.height >= 60f, $"{code}: confirm buttons on the screen, separate, tall enough ({y.height:F0}px)");
                Shot($"home_confirm_{code}"); yield return null; yield return null;
                typeof(GameManager).GetField("showNewRunConfirm", NP).SetValue(gm, false);
                yield return new WaitForSecondsRealtime(0.2f);
            }
            Loc.Set(keepLang, false);
            Check(RunCheckpoint.HasActiveRun, "the suspended run is still there after the screenshots");
            yield break;
        }

        // ---- tap: 外から本当に押してもらう ----
        int mile0 = gm.TotalOwnedMile; string cards0 = SaveStore.GetString("OwnedCardsV1", ""), unlock0 = SaveStore.GetString(UnlockRules.StagesKey, "") + "|" + SaveStore.GetString(UnlockRules.CharsKey, "");
        yield return TapAndWait("new1", gm.LastHomeSignRects[5], () => gm.NewRunConfirmOpen, 120f);
        Check(gm.NewRunConfirmOpen, "tap 'はじめから' -> the confirmation opens");
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("tap_confirm"); yield return null; yield return null;
        yield return TapAndWait("cancel", gm.LastNewRunNoRect, () => !gm.NewRunConfirmOpen, 120f);
        Check(!gm.NewRunConfirmOpen && RunCheckpoint.HasActiveRun && !gm.StageSelectIsOpen, "cancel -> back home, the suspended run is kept");
        yield return new WaitForSecondsRealtime(0.8f);
        yield return TapAndWait("new2", gm.LastHomeSignRects[5], () => gm.NewRunConfirmOpen, 120f);
        yield return new WaitForSecondsRealtime(0.5f);
        yield return TapAndWait("yes", gm.LastNewRunYesRect, () => gm.StageSelectIsOpen, 120f);
        Check(gm.StageSelectIsOpen && !RunCheckpoint.HasActiveRun, "'はじめから出発' -> the suspended run ends and the map select opens");
        Check(gm.TotalOwnedMile == mile0 && SaveStore.GetString("OwnedCardsV1", "") == cards0 && SaveStore.GetString(UnlockRules.StagesKey, "") + "|" + SaveStore.GetString(UnlockRules.CharsKey, "") == unlock0,
            $"the owned MILE / cards / unlocks are not reset ({mile0} -> {gm.TotalOwnedMile})");
        Shot("tap_stage_select"); yield return null; yield return null;
        gm.CloseStageSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        // 続きから(札)で再開
        yield return HcMakeCheckpoint(2500f);
        float want = RunCheckpoint.Load().checkpointDistance;
        yield return new WaitForSecondsRealtime(0.6f);
        yield return TapAndWait("continue", gm.LastHomeSignRects[4], () => GameManager.Instance != null && GameManager.Instance.HasStarted, 120f);
        float w = 0f; while (w < 5f && (GameManager.Instance == null || !GameManager.Instance.HasStarted)) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        Check(gm.HasStarted && Mathf.Abs(gm.MaxDistance - want) < 2f, $"tap '続きから' -> resumes at the saved point ({gm.MaxDistance:F0} vs {want:F0})");
        yield return new WaitForSecondsRealtime(2f);
        Shot("tap_resumed"); yield return null; yield return null;
        yield return EndRun();
    }

    // 押す場所(画面の座標・左上が原点)を書いて、条件が満たされるまで待つ。外の adb が tap_req.txt を読んで押し、消す
    IEnumerator TapAndWait(string name, Rect r, System.Func<bool> done, float timeout)
    {
        string p = System.IO.Path.Combine(outDir, "tap_req.txt");
        System.IO.File.WriteAllText(p, $"{name} {Mathf.RoundToInt(r.center.x)} {Mathf.RoundToInt(r.center.y)} {Screen.width} {Screen.height}");
        L($"[hc] tap request '{name}' at {r.center} (rect {r})");
        float w = 0f;
        while (!done() && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        try { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch { }
        L($"[hc] '{name}' -> {(done() ? "done" : "TIMEOUT")} after {w:F1}s");
    }
}
#endif

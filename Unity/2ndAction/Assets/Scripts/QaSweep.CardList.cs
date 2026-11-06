#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// カード編集画面の枚数増加の試験(2026-11-06、マスターの依頼 15〜24): -qaCardList <dir> -memSave [-clFake 150] [-clPhase all|plant|check] [-clShared <dir>]
//  all   … 全カードを Lv1/2/3/5/9 + 合成のキー + 試験用の仮のカード(-clFake 種類、各 Lv1/Lv3)で 250 種類以上・束 600 以上にして、
//          開く時間・最後の束(★5)まで届くか・全部の束が一度は見えるか・マップ/レア度/属性/種類の絞り込み・並び替え・選択肢の窓・
//          絞り込み中のドラッグ/タップでデッキ12枚・長押しの詳細・分解・カード合成・保存して画面を読み直し・横/縦の撮影
//  plant … 本物のカードだけで束を増やし、★5 を含むデッキ12枚を保存して終わる(-fileSave で) / check … 次の起動で同じ内容か・一覧の最後まで届くか
//  仮のカードは CardDatabase の読み込み済みの一覧へ足すだけ(この起動の中だけ。保存しない -memSave で使う)
public partial class QaSweep
{
    void ClCheck(bool ok, string what) { Check(ok, what); if (ok) L("[ok] " + what); }

    static bool clWarm, clOpenedOnce;
    static string ClSfx => Arg("-clShotSuffix", "");
    IEnumerator ClShot(string name) { yield return ShotW(name + ClSfx); }
    static readonly List<CardDefinition> clFakes = new List<CardDefinition>();

    // 仮のカード: 本物のカードを写して id/名前/レア度/種類/属性/並び/マップ分類 を変える。最後の5枚は ★5(並び順の最後 = 以前は消えた位置)
    static void ClAddFakeCards(int n)
    {
        var field = typeof(CardDatabase).GetField("cachedCards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var list = field != null ? field.GetValue(null) as List<CardDefinition> : null;
        if (list == null) { _ = CardDatabase.AllCards; list = field != null ? field.GetValue(null) as List<CardDefinition> : null; }
        if (list == null || clFakes.Count > 0) return;
        var templates = list.Where(c => !CardVariant.IsVariantKey(c.cardId) && c.cardId != "character_ultimate").ToList();
        int maxOrder = list.Max(c => c.sortOrder);
        for (int i = 0; i < n; i++)
        {
            var t = templates[i % templates.Count];
            var f = Object.Instantiate(t);
            f.name = f.cardId = $"qa_fake_{i + 1:000}";
            f.cardName = $"QA CARD {i + 1:000}";
            f.rarity = i >= n - 5 ? 5 : 1 + i % 5;
            f.category = (CardCategory)(i % 7);
            f.element = (ElementType)(i % 6);
            f.sortOrder = maxOrder + 1 + i;
            f.unlockDistance = 500f * i;
            f.gachaStage = 1;
            CardMapCategories.Set(f.cardId, (CardMapCategory)(i % 5));
            list.Add(f);
            clFakes.Add(f);
        }
    }

    static void ClOwnMany(bool fakes, int fakeN)
    {
        foreach (int lv in new[] { 1, 2, 3, 5, 9 }) CardInventory.DebugOwnEveryAtLevel(lv, lv == 1 ? 2 : 1);
        var real = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && !c.cardId.StartsWith("qa_fake_")).ToList();
        foreach (var c in real.Take(60)) foreach (int lv in new[] { 4, 6 }) { string k = DdKey(c.cardId, lv); if (CardInventory.GetCount(k, lv) == 0) CardInventory.AddCard(k, lv, 1); }
        if (!fakes) return;
        ClAddFakeCards(fakeN);
        foreach (var f in clFakes) { if (CardInventory.GetCount(f.cardId, 1) == 0) CardInventory.AddCard(f.cardId, 1, 2); if (CardInventory.GetCount(f.cardId, 3) == 0) CardInventory.AddCard(f.cardId, 3, 1); }
    }

    // 期待する一覧(画面とは別に数える)
    static List<CardInventory.Stack> ClExpected(System.Func<CardInventory.Stack, CardDefinition, CardDefinition, bool> pred)
    {
        var r = new List<CardInventory.Stack>();
        foreach (var st in CardInventory.Stacks)
        {
            if (st.count <= 0) continue;
            var d = CardDatabase.FindById(st.cardId); if (d == null) continue;
            var b = CardDatabase.FindBaseById(CardMastery.BaseIdOf(st.cardId)) ?? d;
            if (pred(st, d, b)) r.Add(st);
        }
        return r;
    }

    // 束がすべて一度は「全体が見える」か(上から下まで少しずつ動かす)
    IEnumerator ClSweepAll(DeckEditUI ui, string what)
    {
        int n = ui.DebugOwnedShownCount;
        var seen = new HashSet<int>();
        int rows = Mathf.Max(1, ui.DebugOwnedTotalRows);
        int steps = Mathf.Max(2, rows * 2);
        float worstMs = 0f;
        for (int s = 0; s <= steps; s++)
        {
            float t0 = Time.realtimeSinceStartup;
            ui.DebugSetOwnedScroll(1f - s / (float)steps);
            worstMs = Mathf.Max(worstMs, (Time.realtimeSinceStartup - t0) * 1000f);
            foreach (int d in ui.DebugOwnedBoundIndices().ToList()) if (ui.DebugOwnedFullyVisible(d)) seen.Add(d);
            if (s % 8 == 0) yield return null;
        }
        ClCheck(seen.Count == n, $"{what}: every entry was fully visible at some scroll position ({seen.Count}/{n}, rows {rows}, cols {ui.DebugOwnedCols}, slots {ui.DebugOwnedPoolSize}, worst scroll step {worstMs:F1} ms)");
        if (seen.Count != n) L($"[cl] not seen: {string.Join(",", Enumerable.Range(0, n).Where(i => !seen.Contains(i)).Take(20))}");
        ui.DebugSetOwnedScroll(1f);
    }

    IEnumerator ClOpenDeck(string tag)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        gm.OpenDeckEdit();
        // 画面の切り替え(暗転)の間に開く → 開いた後の 40 フレームで一番長いフレームを測る
        float worst = 0f; int frames = 0; bool opened = false; float openMs = 0f;
        float w = 0f;
        while (w < 3f)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            var ui0 = gm.deckEditUI;
            if (!opened && ui0 != null && ui0.root != null && ui0.root.activeInHierarchy) { opened = true; openMs = (float)sw.Elapsed.TotalMilliseconds; }
            if (opened) { worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); if (++frames >= 40) break; }
        }
        yield return new WaitForSecondsRealtime(0.6f);
        var ui = gm.deckEditUI;
        L($"[cl] {tag}: open breakdown {DeckEditUI.LastOpenTimes}");
        L($"[cl] {tag}: opened after {openMs:F0} ms (transition incl.), list rebuild {DeckEditUI.LastRefreshMs:F1} ms (entries {DeckEditUI.LastEntriesMs:F1} ms), worst frame after opening {worst:F1} ms, entries {ui.DebugOwnedShownCount}, slots {ui.DebugOwnedPoolSize}");
        bool firstInProcess = !clOpenedOnce; clOpenedOnce = true;
        if (clWarm) yield break;
        if (firstInProcess) { L($"[cl] {tag}: first open in this process (builds the screen parts; about the same with 130 stacks) - not judged"); yield break; }
        ClCheck(worst < 250f, $"{tag}: no long hitch when opening the card edit screen (worst frame {worst:F1} ms, list rebuild {DeckEditUI.LastRefreshMs:F1} ms)");
    }

    IEnumerator CardListMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1); SaveStore.Save();
        string phase = Arg("-clPhase", "all");
        int fakeN = int.Parse(Arg("-clFake", "150"));
        string shared = Arg("-clShared", outDir);
        if (phase == "check") { yield return ClCheckPhase(shared); yield break; }

        string lang = Arg("-clLang", "");
        if (lang != "") Loc.Set(lang, false); // 撮影用: 表示の言語(保存しない)
        if (phase == "langshots") // ほかの言語での見た目: マップのタブ・選択肢の窓・未解放の数(横/縦)
        {
            ClOwnMany(true, fakeN);
            foreach (var id in new[] { "qa_fake_010", "qa_fake_015", "qa_fake_020" })
                foreach (var st in CardInventory.Stacks.Where(x => CardMastery.BaseIdOf(x.cardId) == id).ToList()) CardInventory.RemoveCard(st.cardId, st.level, st.count);
            foreach (bool land in new[] { true, false })
            {
                string t = (land ? "ls_" : "pt_") + Loc.Current;
                yield return ClRes(land);
                yield return ClOpenDeck(t);
                var u = gm.deckEditUI;
                u.DebugSetMapFilter((int)CardMapCategory.LastDungeon); yield return null;
                yield return ShotW($"cl_{t}_map_lastdungeon");
                u.DebugOpenFilterPopup(2); yield return null;
                yield return ShotW($"cl_{t}_popup_sort");
                u.CloseFilterPopup();
                u.DebugSetMapFilter(-1); u.DebugSetRarityFilter(5); u.DebugSetElementFilter(4); u.DebugSetMapFilter(4); yield return null;
                yield return ShotW($"cl_{t}_no_match_or_few");
                u.DebugSetRarityFilter(0); u.DebugSetElementFilter(-1); u.DebugSetMapFilter(-1);
                u.DebugSetOwnedScroll(0.5f); yield return null;
                yield return ClShot($"cl_{t}_filters_scrollbar");
                u.DebugLongPress(false, false, 3); yield return new WaitForSecondsRealtime(0.3f);
                yield return ClShot($"cl_{t}_long_press_close");
                u.CloseBigDetail();
                u.DebugSetRarityFilter(0); u.DebugSetElementFilter(-1); u.DebugSetMapFilter(-1);
                u.Close(); yield return new WaitForSecondsRealtime(1.2f);
            }
            yield return ClRes(true);
            yield break;
        }
        if (phase == "open") // 開く時間だけを比べる: -clSmall 1 = 本物のカード Lv1 だけ(以前の普通の量)/ 0 = 大量(仮のカード込み)
        {
            if (Arg("-clSmall", "0") == "1") CardInventory.DebugOwnEveryAtLevel(1, 1); else ClOwnMany(true, fakeN);
            L($"[cl] open-only: stacks {DeckEditUI.OwnedStacksSorted().Count}");
            yield return ClRes(true);
            for (int k = 0; k < 3; k++) { yield return ClOpenDeck("open#" + k); gm.deckEditUI.Close(); yield return new WaitForSecondsRealtime(1.2f); }
            yield return ClRes(false); // 縦へ回した直後の1回目(並べ直しが入る)と2回目
            for (int k = 0; k < 2; k++) { yield return ClOpenDeck("portrait open#" + k); gm.deckEditUI.Close(); yield return new WaitForSecondsRealtime(1.2f); }
            yield return ClRes(true);
            yield break;
        }
        ClOwnMany(phase == "all", fakeN);
        if (phase == "all")
        {
            // 未解放の数の確認用: ラスダンの仮のカード3枚を持っていない状態にする(解放の距離 > BEST なので未解放になる)
            foreach (var id in new[] { "qa_fake_010", "qa_fake_015", "qa_fake_020" })
                foreach (var st in CardInventory.Stacks.Where(x => CardMastery.BaseIdOf(x.cardId) == id).ToList()) CardInventory.RemoveCard(st.cardId, st.level, st.count);
        }
        int types = CardDatabase.AllCards.Count(c => !CardVariant.IsVariantKey(c.cardId));
        int stacks = DeckEditUI.OwnedStacksSorted().Count;
        L($"[cl] card types {types} (fake {clFakes.Count}), owned stacks {stacks}");
        if (phase == "plant") { yield return ClPlant(shared, stacks); yield break; }
        ClCheck(types >= 250 && stacks >= 600, $"the test inventory has 250+ card types and 600+ stacks ({types} types, {stacks} stacks)");

        // ---- 横画面 / 縦画面(窓の大きさ: -clLand 1280x720 / -clPort 576x1024)
        yield return ClRes(true);
        // 起動して最初に開く時は、画面の部品を作る分(カードの量に関係なく 0.8 秒ほど)がかかる → 1回開いて閉じてから測る
        clWarm = true;
        yield return ClOpenDeck("warm-up");
        gm.deckEditUI.Close(); yield return new WaitForSecondsRealtime(1.2f);
        clWarm = false;
        yield return ClScreens("ls", stacks);
        yield return ClRes(false);
        yield return ClScreens("pt", stacks);
        yield return ClRes(true);
    }

    IEnumerator ClScreens(string tag, int stacks0)
    {
        int stacks = DeckEditUI.OwnedStacksSorted().Count; // 前の段で分解/合成した分を数え直す
        gm.SetDeck(new string[0]);
        if (tag == "pt") { clWarm = true; yield return ClOpenDeck("pt first after rotating"); gm.deckEditUI.Close(); yield return new WaitForSecondsRealtime(1.2f); clWarm = false; }
        yield return ClOpenDeck(tag);
        var ui = gm.deckEditUI;
        ClCheck(ui.DebugOwnedShownCount == stacks, $"{tag} A: 'ALL' lists every owned stack ({ui.DebugOwnedShownCount}/{stacks})");
        ClCheck(ui.DebugOwnedPoolSize < stacks, $"{tag} A: card parts are only made for the visible rows ({ui.DebugOwnedPoolSize} parts for {stacks} entries)");
        yield return ShotW($"cl_{tag}_01_list_top");

        // B: 最後の束(★5)まで届く
        ui.DebugSetOwnedScroll(0f);
        yield return null; yield return null;
        int n = ui.DebugOwnedShownCount;
        var lastDef = ui.DebugShownDef(n - 1);
        ClCheck(ui.DebugOwnedFullyVisible(n - 1), $"{tag} B: scrolled to the end, the last entry is fully visible ({lastDef?.cardId} ★{lastDef?.rarity})");
        ClCheck(lastDef != null && lastDef.rarity == 5, $"{tag} B: the last entry in the default order is a ★5 ({lastDef?.cardId})");
        yield return ShotW($"cl_{tag}_02_list_end_rare5");
        // B2: 一覧の右のスクロールバー(最後まで動かした時はつまみが一番下 / つまみの長さ = 見えている割合 / つまみを動かすとその位置へ)
        {
            var tr = ui.DebugScrollbarTrack; var th = ui.DebugScrollbarThumb;
            Canvas.ForceUpdateCanvases();
            bool atBottom = tr != null && th != null && Mathf.Abs(-th.anchoredPosition.y - (tr.rect.height - th.rect.height)) < 2f;
            ClCheck(tr != null && tr.gameObject.activeInHierarchy && atBottom && th.rect.height < tr.rect.height * 0.25f, $"{tag} B2: the scrollbar is shown and its thumb is at the bottom at the end (thumb {th?.rect.height:F0} / track {tr?.rect.height:F0})");
            var mid = RectTransformUtility.WorldToScreenPoint(null, tr.TransformPoint(tr.rect.center));
            ClCheck(ui.DebugScrollbarHit(mid + new Vector2(10f, 0f)), $"{tag} B2: the scrollbar can be grabbed (a little wider than it looks)");
            ui.DebugScrollbarDragTo(mid);
            yield return null;
            float np = ui.ownedScrollRect.verticalNormalizedPosition;
            ClCheck(Mathf.Abs(np - 0.5f) < 0.08f, $"{tag} B2: dragging the thumb to the middle scrolls to the middle ({np:F2})");
            yield return ClShot($"cl_{tag}_02b_scrollbar_middle");
            ui.DebugSetOwnedScroll(1f); yield return null;
            Canvas.ForceUpdateCanvases();
            ClCheck(Mathf.Abs(th.anchoredPosition.y) < 2f, $"{tag} B2: at the top the thumb is at the top ({th.anchoredPosition.y:F1})");
        }
        // B3: 縦画面でも一覧に2段以上見える / 絞り込みのボタンが押しやすい大きさ
        {
            var vp = ui.DebugOwnedViewport; float rowH = ui.DebugOwnedRowHeight;
            float rows = vp.rect.height / rowH;
            // 縦はエステルのレビューで2段の絞り込みを大きくした → 2段以上 / 押しやすい大きさを判定。横は承認済みの配置(9列×2段、2段目の下端が少し切れる)なので記録だけ
            if (tag == "pt") ClCheck(rows >= 2f, $"{tag} B3: the list shows at least 2 card rows ({rows:F2} rows, cols {ui.DebugOwnedCols})");
            Rect mt = ScreenRectOf(ui.DebugMapTabRect(1)), ch = ScreenRectOf(ui.DebugChooserRect(0));
            float minPx = Mathf.Min(Screen.width, Screen.height) * 0.06f;
            L($"[cl] {tag}: map tab {mt.width:F0}x{mt.height:F0}px, chooser {ch.width:F0}x{ch.height:F0}px, list rows visible {rows:F2}");
            if (tag == "pt") ClCheck(mt.height >= minPx && ch.height >= minPx, $"{tag} B3: map tabs and the rarity/element/sort buttons are tall enough to tap ({mt.height:F0}/{ch.height:F0}px >= {minPx:F0})");
            yield return ClShot($"cl_{tag}_02c_filters");
        }
        foreach (var c in CardDatabase.AllCards.Where(x => !CardVariant.IsVariantKey(x.cardId) && x.rarity >= 5))
            if (CardInventory.GetTotalCountByName(c.cardId) > 0 && !ui.DebugOwnedShows(c.cardId)) ClCheck(false, $"{tag} B: ★5 {c.cardId} is reachable");
        ClCheck(true, $"{tag} B: every owned ★5 (real and test) can be scrolled into view");
        ui.DebugSetOwnedScroll(1f);
        if (tag == "ls") yield return ClSweepAll(ui, $"{tag} C");

        // D: マップ別
        string[] mapNames = { "all", "common", "wilderness", "cave", "sky", "lastdungeon" };
        for (int m = -1; m < 5; m++)
        {
            ui.DebugSetMapFilter(m);
            yield return null;
            int mm = m;
            var exp = ClExpected((st, d, b) => mm < 0 || (int)CardMapCategories.Of(b.cardId) == mm);
            bool allMatch = Enumerable.Range(0, ui.DebugOwnedShownCount).All(i => { var st = ui.DebugOwnedStack(i); return mm < 0 || (int)CardMapCategories.Of(CardMastery.BaseIdOf(st.cardId)) == mm; });
            ClCheck(ui.DebugOwnedShownCount == exp.Count && allMatch, $"{tag} D: map filter {mapNames[m + 1]} lists {exp.Count} stacks ({ui.DebugOwnedShownCount}); tab '{ui.DebugMapTabText(m + 1)}', locked kinds {ui.DebugMapLocked(m + 1)}; count line '{Strip(ui.DebugCollectionCountText)}'");
            if (m == (int)CardMapCategory.LastDungeon && clFakes.Count >= 20) ClCheck(ui.DebugMapLocked(m + 1) >= 3, $"{tag} D: LAST DUNGEON counts the 3 test cards that are not unlocked yet ({ui.DebugMapLocked(m + 1)})");
            if (m >= 0 && ui.DebugMapLocked(m + 1) > 0) ClCheck(ui.DebugCollectionCountText.Contains(Loc.F(LocCardFilter.Locked, ui.DebugMapLocked(m + 1))), $"{tag} D: the count line says how many cards of {mapNames[m + 1]} are still locked");
            if (m >= 0 && ui.DebugOwnedShownCount > 0)
            {
                ui.DebugSetOwnedScroll(0f); yield return null;
                ClCheck(ui.DebugOwnedFullyVisible(ui.DebugOwnedShownCount - 1), $"{tag} D: {mapNames[m + 1]}: the last entry is visible at the end");
                ui.DebugSetOwnedScroll(1f);
            }
            if (tag == "pt" || m == -1 || m == 1 || m == 4) yield return ShotW($"cl_{tag}_03_map_{mapNames[m + 1]}");
        }
        // E: マップ + レア度 / 属性 / 種類
        ui.DebugSetMapFilter((int)CardMapCategory.Wilderness);
        for (int r = 1; r <= 5; r++)
        {
            ui.DebugSetRarityFilter(r); yield return null;
            int rr = r;
            var exp = ClExpected((st, d, b) => (int)CardMapCategories.Of(b.cardId) == (int)CardMapCategory.Wilderness && d.rarity == rr);
            ClCheck(ui.DebugOwnedShownCount == exp.Count, $"{tag} E: WILDERNESS + ★{r} lists {exp.Count} ({ui.DebugOwnedShownCount})");
            if (r == 4) yield return ShotW($"cl_{tag}_04_wilderness_rare4");
        }
        ui.DebugSetRarityFilter(0);
        foreach (int e in new[] { 2, 3, 1, 4, 5, 0 })
        {
            ui.DebugSetElementFilter(e); yield return null;
            int ee = e;
            var exp = ClExpected((st, d, b) => (int)CardMapCategories.Of(b.cardId) == (int)CardMapCategory.Wilderness && (int)b.element == ee);
            ClCheck(ui.DebugOwnedShownCount == exp.Count, $"{tag} E: WILDERNESS + element {(ElementType)e} lists {exp.Count} ({ui.DebugOwnedShownCount})");
            if (e == 2) yield return ShotW($"cl_{tag}_05_wilderness_fire");
        }
        ui.DebugSetElementFilter(-1);
        ui.DebugSetFilter("Attack"); yield return null;
        {
            var exp = ClExpected((st, d, b) => (int)CardMapCategories.Of(b.cardId) == (int)CardMapCategory.Wilderness && d.category == CardCategory.Attack);
            ClCheck(ui.DebugOwnedShownCount == exp.Count, $"{tag} E: WILDERNESS + category Attack lists {exp.Count} ({ui.DebugOwnedShownCount})");
        }
        // 0 件の時の案内
        ui.DebugSetRarityFilter(5); ui.DebugSetElementFilter(5); yield return null;
        if (ui.DebugOwnedShownCount == 0) { ClCheck(ui.DebugNoMatchShown, $"{tag} E: an empty result shows the 'no cards match' message"); yield return ShotW($"cl_{tag}_06_no_match"); }
        else L($"[cl] {tag}: the zero-result combination had {ui.DebugOwnedShownCount} entries (message not checked)");
        ui.DebugSetFilter("ALL"); ui.DebugSetRarityFilter(0); ui.DebugSetElementFilter(-1); ui.DebugSetMapFilter(-1);

        // F: 選択肢の窓
        ui.DebugOpenFilterPopup(0); yield return null;
        ClCheck(ui.FilterPopupOpen, $"{tag} F: the rarity chooser opens");
        yield return ShotW($"cl_{tag}_07_popup_rarity");
        ui.DebugTapFilterOption(4); yield return null; // ★4
        ClCheck(!ui.FilterPopupOpen && Enumerable.Range(0, ui.DebugOwnedShownCount).All(i => ui.DebugShownDef(i).rarity == 4), $"{tag} F: picking ★4 closes it and filters ({ui.DebugOwnedShownCount})");
        ui.DebugOpenFilterPopup(1); yield return null;
        yield return ShotW($"cl_{tag}_08_popup_element");
        ui.DebugTapFilterOption(0); yield return null;
        ui.DebugOpenFilterPopup(2); yield return null;
        yield return ShotW($"cl_{tag}_09_popup_sort");
        ui.DebugTapFilterOption(1); yield return null; // レア度
        ui.DebugSetRarityFilter(0);

        // G: 並び替え
        foreach (DeckEditUI.SortKey k in System.Enum.GetValues(typeof(DeckEditUI.SortKey)))
        {
            foreach (bool desc in new[] { false, true })
            {
                ui.DebugSetSort(k, desc); yield return null;
                int cnt = ui.DebugOwnedShownCount, bad = 0;
                for (int i = 1; i < cnt; i++)
                {
                    var a = ui.DebugShownDef(i - 1); var b = ui.DebugShownDef(i);
                    var sa = ui.DebugOwnedStack(i - 1); var sb = ui.DebugOwnedStack(i);
                    var ba = CardDatabase.FindBaseById(CardMastery.BaseIdOf(sa.cardId)) ?? a; var bb = CardDatabase.FindBaseById(CardMastery.BaseIdOf(sb.cardId)) ?? b;
                    float c = k switch
                    {
                        DeckEditUI.SortKey.Rarity => b.rarity - a.rarity,
                        DeckEditUI.SortKey.Level => sb.level - sa.level,
                        DeckEditUI.SortKey.Owned => sb.count - sa.count,
                        DeckEditUI.SortKey.Map => (int)CardMapCategories.Of(bb.cardId) - (int)CardMapCategories.Of(ba.cardId),
                        DeckEditUI.SortKey.Unlock => bb.unlockDistance - ba.unlockDistance,
                        DeckEditUI.SortKey.Name => string.Compare(Loc.Auto(b.cardName), Loc.Auto(a.cardName), System.StringComparison.CurrentCultureIgnoreCase),
                        DeckEditUI.SortKey.Element => ClElemRank((int)bb.element) - ClElemRank((int)ba.element),
                        _ => b.sortOrder - a.sortOrder,
                    };
                    if (desc) c = -c;
                    if (c < 0) bad++;
                }
                ClCheck(bad == 0, $"{tag} G: sort {k} {(desc ? "desc" : "asc")} is in order ({cnt} entries, {bad} out of order)");
                if (k == DeckEditUI.SortKey.Rarity && desc) yield return ShotW($"cl_{tag}_10_sort_rarity");
            }
        }
        ui.DebugSetSort(DeckEditUI.SortKey.Standard, false);

        // H: 絞り込み中のデッキ操作(ドラッグ・タップで12枚 → 外す → 戻す)。デッキの欄は絞り込みに関係なく見える
        ui.DebugSetMapFilter((int)CardMapCategory.Wilderness); yield return null;
        int first = -1;
        for (int i = 0; i < ui.DebugOwnedShownCount; i++) if (!gm.DeckHasKind(ui.DebugOwnedStack(i).cardId)) { first = i; break; }
        string dragKey = first >= 0 ? ui.DebugOwnedStack(first).cardId : null;
        yield return ui.DebugDrag(false, first, ui.DebugDeckSlotPos(0));
        yield return new WaitForSecondsRealtime(0.3f);
        ClCheck(dragKey != null && gm.DeckCards.Contains(dragKey), $"{tag} H: dragging from the WILDERNESS-filtered list puts the card in the deck ({dragKey}, {DeckEditUI.LastDrop})");
        // 一覧の後ろの方(スクロールが要る所)からタップで足す
        for (int i = ui.DebugOwnedShownCount - 1; i >= 0 && gm.DeckCards.Count < GameManager.DeckCapacity; i--)
        {
            string key = ui.DebugOwnedStack(i).cardId;
            if (gm.DeckHasKind(key)) continue;
            ui.DebugTapOwnedKey(key);
            yield return null;
        }
        ClCheck(gm.DeckCards.Count == GameManager.DeckCapacity, $"{tag} H: 12 cards added from the filtered list ({gm.DeckCards.Count})");
        bool deckVisible = Enumerable.Range(0, GameManager.DeckCapacity).All(i => { var r = ui.DebugDeckSlotRect(i); var p = ui.DebugDeckSlotPos(i); return r.gameObject.activeInHierarchy && p.x > 0 && p.y > 0 && p.x < Screen.width && p.y < Screen.height; });
        ClCheck(deckVisible, $"{tag} H: all 12 deck slots stay on screen while the list is filtered");
        ui.DebugSetRarityFilter(4); yield return null;
        yield return ShotW($"cl_{tag}_11_deck12_filtered");
        ui.DebugSetRarityFilter(0);
        int before = gm.DeckCards.Count; string removed = gm.DeckCards[0];
        yield return ui.DebugDrag(true, 0, ui.DebugOwnedAreaPos);
        yield return new WaitForSecondsRealtime(0.3f);
        ClCheck(gm.DeckCards.Count == before - 1 && !gm.DeckCards.Contains(removed), $"{tag} H: dragging a deck card onto the filtered list removes it ({DeckEditUI.LastDrop})");
        ClCheck(ui.DebugTapOwnedKey(removed) || gm.DeckCards.Contains(removed) || CardMapCategories.Of(CardMastery.BaseIdOf(removed)) != CardMapCategory.Wilderness, $"{tag} H: and it can be added back from the list");

        // I: 長押しの大きな詳細(一覧の後ろの方の束)
        int li = ui.DebugOwnedShownCount - 1;
        var lst = ui.DebugOwnedStack(li); var ldef = ui.DebugShownDef(li);
        ui.DebugLongPress(false, false, li);
        yield return new WaitForSecondsRealtime(0.4f);
        ClCheck(ui.BigDetailOpen && ui.DebugBigInfo.Contains(Loc.Auto(ldef.cardName)), $"{tag} I: a long press on the last filtered entry opens its large detail ({lst.cardId} Lv{lst.level})");
        yield return ShotW($"cl_{tag}_12_long_press");
        yield return ClShot($"cl_{tag}_12b_long_press_close");
        {
            var cr = ui.DebugBigCloseRect;
            Rect c = ScreenRectOf(cr);
            Rect g = SettingsPanel.MenuGearRect(); // IMGUI(上が 0)→ 画面の座標(下が 0)
            Rect gs = new Rect(g.x, Screen.height - g.yMax, g.width, g.height);
            ClCheck(cr != null && cr.gameObject.activeInHierarchy && !c.Overlaps(gs) && c.yMax <= Screen.height && c.xMax <= Screen.width, $"{tag} I2: the close button is on screen and not under the settings button (close {c}, gear {gs})");
            ui.DebugBigTap(RectTransformUtility.WorldToScreenPoint(null, cr.TransformPoint(cr.rect.center)));
            yield return null;
            ClCheck(!ui.BigDetailOpen, $"{tag} I2: the close button closes the large detail");
            ui.DebugLongPress(false, false, li); yield return null;
            Rect pr = ScreenRectOf(ui.DebugBigPanel);
            Vector2 outside = new Vector2(4f, 4f);
            ClCheck(!pr.Contains(outside), $"{tag} I2: there is a background area outside the window ({pr})");
            ui.DebugBigTap(outside); yield return null;
            ClCheck(!ui.BigDetailOpen, $"{tag} I2: tapping the background closes the large detail");
            ui.DebugOpenFilterPopup(2); yield return null;
            ui.DebugLongPress(false, false, li); yield return null;
            ClCheck(ui.FilterPopupOpen && !ui.BigDetailOpen, $"{tag} I2: the large detail does not open while a filter choice window is open");
            ui.CloseFilterPopup();
            ui.DebugLongPress(false, false, li); yield return null;
        }
        // J: 分解(空きのある束 = 2枚の Lv1)
        ui.CloseBigDetail();
        string dis = null;
        for (int i = 0; i < ui.DebugOwnedShownCount; i++) { var s = ui.DebugOwnedStack(i); if (s.level == 1 && s.count >= 2 && !gm.DeckHasKind(s.cardId) && CardDismantle.Check(s.cardId, 1) == CardDismantle.Block.None) { dis = s.cardId; break; } }
        if (dis != null)
        {
            int c0 = CardInventory.GetCount(dis, 1), n0 = ui.DebugOwnedShownCount;
            ui.OpenBigDetail(dis, 1);
            yield return null;
            ui.DebugDismantleTap(); yield return null;
            ui.DebugConfirmYes(); yield return new WaitForSecondsRealtime(0.3f);
            ClCheck(CardInventory.GetCount(dis, 1) == c0 - 1 && ui.DebugOwnedShownCount == n0, $"{tag} J: dismantling one of two copies in the filtered list works ({dis} {c0}->{CardInventory.GetCount(dis, 1)}, list {n0}->{ui.DebugOwnedShownCount})");
            // 最後の1枚も分解 → 一覧から消える(束が1つ減る)
            ui.OpenBigDetail(dis, 1); yield return null;
            ui.DebugDismantleTap(); yield return null;
            ui.DebugConfirmYes(); yield return new WaitForSecondsRealtime(0.3f);
            ClCheck(CardInventory.GetCount(dis, 1) == 0 && ui.DebugOwnedShownCount == n0 - 1 && ui.DebugOwnedIndex(dis) < 0, $"{tag} J: dismantling the last copy removes that entry ({ui.DebugOwnedShownCount})");
            if (ui.BigDetailOpen) ui.CloseBigDetail();
        }
        else Warn($"{tag} J: no free stack to dismantle");
        ui.DebugSetMapFilter(-1);

        // K: カード合成(同じカードの Lv1 ×2 → Lv2)。合成の画面の一覧もこの数で開けるか / 合成の後にデッキ編集で新しい束が見えるか
        ui.Close();
        yield return new WaitForSecondsRealtime(1.0f);
        if (tag == "ls")
        {
            var fu = gm.cardFusionUI;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            gm.OpenCardFusion();
            yield return new WaitForSecondsRealtime(1.2f);
            if (fu != null)
            {
                sw.Restart(); fu.Refresh(); float fuMs = (float)sw.Elapsed.TotalMilliseconds;
                L($"[cl] fusion list refresh {fuMs:F1} ms, visible rows {fu.DebugVisibleRows()}, cells {fu.DebugCellCount}");
                ClCheck(fuMs < 200f, $"{tag} K: the fusion list refreshes quickly with {stacks}+ stacks ({fuMs:F1} ms, cells {fu.DebugCellCount})");
                string a = null;
                foreach (var s in CardInventory.Stacks) if (s.level == 1 && s.count >= 2 && !CardVariant.IsVariantKey(s.cardId) && !s.cardId.StartsWith("qa_fake_") && CardFusionLogic.AvailableCount(s.cardId) >= 2 && !gm.DeckHasKind(s.cardId)) { a = s.cardId; break; }
                if (a != null)
                {
                    string ka = CardInventory.Stacks.Where(s => s.cardId == a && s.level == 1).Select(s => s.cardId).First();
                    fu.DebugSelect(ka, ka);
                    yield return null;
                    yield return ShotW($"cl_{tag}_13_fusion_select");
                    fu.DebugFuse();
                    float fw = 0f; while (!fu.IsShowingResult && fw < 12f) { fu.DebugSkip(); yield return null; fw += Time.unscaledDeltaTime; }
                    var res = CardFusionLogic.LastResult;
                    ClCheck(res != null && !string.IsNullOrEmpty(res.resultKey), $"{tag} K: fusing two copies of {a} makes a card ({res?.resultKey})");
                    yield return ShotW($"cl_{tag}_14_fusion_result");
                    fu.DebugContinue();
                    yield return new WaitForSecondsRealtime(0.5f);
                    fu.Close();
                    yield return new WaitForSecondsRealtime(1.2f);
                    if (res != null && !string.IsNullOrEmpty(res.resultKey))
                    {
                        yield return ClOpenDeck(tag + " after fusion");
                        ClCheck(gm.deckEditUI.DebugOwnedShows(res.resultKey), $"{tag} K: the fused card is in the card edit list ({res.resultKey})");
                        gm.deckEditUI.Close();
                        yield return new WaitForSecondsRealtime(1.0f);
                    }
                }
                else { Warn($"{tag} K: no card with two free Lv1 copies"); fu.Close(); yield return new WaitForSecondsRealtime(1.0f); }
            }
        }

        // L: 保存して読み直し(この起動の中: 保存 → シーンを読み直す → 同じデッキ・同じ数)
        var deckBefore = string.Join(",", gm.DeckCards);
        int stacksBefore = DeckEditUI.OwnedStacksSorted().Count;
        SaveStore.Save();
        yield return ReloadHome();
        gm = GameManager.Instance;
        ClCheck(string.Join(",", gm.DeckCards) == deckBefore && DeckEditUI.OwnedStacksSorted().Count == stacksBefore, $"{tag} L: after saving and reloading, the deck and the {stacksBefore} stacks are unchanged");
    }

    IEnumerator ClRes(bool land)
    {
        var v = Arg(land ? "-clLand" : "-clPort", land ? "1280x720" : "576x1024").Split('x');
        int w = int.Parse(v[0]), h = int.Parse(v[1]);
        Screen.SetResolution(w, h, FullScreenMode.Windowed);
        float t = 0f; while ((Screen.width > Screen.height) != land && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        L($"[cl] window {Screen.width}x{Screen.height}");
    }

    static int ClElemRank(int e) { int[] o = { 2, 3, 1, 4, 5, 0 }; int i = System.Array.IndexOf(o, e); return i < 0 ? 99 : i; }
    static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "");

    IEnumerator ClPlant(string shared, int stacks)
    {
        // ★5 を2枚含む 12 枚(同じカードは1枚まで)
        var r5 = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && c.rarity >= 5 && c.cardId != "character_ultimate").Take(2).Select(c => c.cardId).ToList();
        var rest = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && c.rarity < 5).Take(10).Select(c => c.cardId);
        gm.SetDeck(r5.Concat(rest).ToList());
        SaveStore.Save();
        System.IO.File.WriteAllText(System.IO.Path.Combine(shared, "cl_expect.txt"), string.Join(",", gm.DeckCards) + "|" + stacks);
        L($"[cl] planted deck {gm.DeckCards.Count}: {string.Join(",", gm.DeckCards)}; stacks {stacks}");
        ClCheck(gm.DeckCards.Count == 12, "plant: the deck has 12 cards");
        yield return null;
    }

    IEnumerator ClCheckPhase(string shared)
    {
        string p = System.IO.Path.Combine(shared, "cl_expect.txt");
        ClCheck(System.IO.File.Exists(p), "check: the planted file exists");
        if (!System.IO.File.Exists(p)) yield break;
        var parts = System.IO.File.ReadAllText(p).Split('|');
        int stacks = DeckEditUI.OwnedStacksSorted().Count;
        ClCheck(string.Join(",", gm.DeckCards) == parts[0], $"check: after a restart the 12-card deck is unchanged ({gm.DeckCards.Count})");
        ClCheck(stacks.ToString() == parts[1], $"check: after a restart the owned stacks are unchanged ({stacks} / {parts[1]})");
        yield return ClRes(true);
        yield return ClOpenDeck("restart");
        var ui = gm.deckEditUI;
        ui.DebugSetOwnedScroll(0f); yield return null; yield return null;
        int n = ui.DebugOwnedShownCount;
        ClCheck(n == stacks && ui.DebugOwnedFullyVisible(n - 1), $"check: the list reaches its last entry after the restart ({n})");
        yield return ShotW("cl_restart_list_end");
        ui.Close();
        yield return new WaitForSecondsRealtime(1.0f);
    }
}
#endif

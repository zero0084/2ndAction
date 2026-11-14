#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ガチャの分類を選ぶ機能(2026-11-14)の自動試験: -qaGachaCat <dir> -monetizationMock 1(テスト用データの中だけ)
//  A 未解禁の分類は選べない / B 対象が無い分類は引けない(MILE も広告の回数も使わない・自動で切り替えない)
//  C 5分類それぞれ 120 回: 対象外のカードが出ない + レア度の内訳 / D 切り替え直後・連続(順番待ち)・筐体の見た目
//  E 広告ガチャ: タップ時の分類で確定(広告の間に切り替えても変わらない)・パスでも同じ / F 所持(Lv/枚数)とデッキは増えるだけ
public partial class QaSweep
{
    static readonly FieldInfo GcQueue = typeof(GameManager).GetField("gachaRevealQueue", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo GcPull = typeof(GameManager).GetMethod("GachaPullOnce", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo GcClose = typeof(GameManager).GetMethod("ClearGachaPresentation", BindingFlags.NonPublic | BindingFlags.Instance);

    List<CardDefinition> GcQueued => (List<CardDefinition>)GcQueue.GetValue(gm);

    IEnumerator GachaCatModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        bool mock = Monetization.Mode == MonetizationMode.Mock;
        if (mock) { MockAdService.AdSeconds = 0.6f; MockAdService.AutoComplete = true; MockAdService.Mode = MockAdService.Behaviour.Normal; NoAdsPass.DevMockSet(false); AdManager.DevIgnoreTiming = true; AdRewards.DevResetGacha(); }
        SaveStore.SetInt(GameManager.GachaCategoryKey, 0); SaveStore.Save();
        yield return ReloadHome();

        // ---- A: 新しいデータ。洞窟/天空/ラスダンは未解禁
        L("== A: locked categories ==");
        UnlockRules.QaOverride = false; // 自動試験は全マップを開いて始まる → 製品と同じ判定に戻す(新しいデータ = 洞窟/天空/ラスダンは未解放)
        if (UnlockRules.DevUnlockAll) UnlockRules.DevUnlockAll = false;
        L($"[A] cave unlocked={UnlockRules.IsStageUnlocked(UnlockRules.Cave)} sky={UnlockRules.IsStageUnlocked(UnlockRules.Sky)} devAll={UnlockRules.DevUnlockAll} qaOverride={UnlockRules.QaOverride} best={gm.BestDistance:F0} ld={ProgressStats.FinalDungeonUnlocked}");
        foreach (var k in new[] { CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon })
        {
            bool open = GameManager.GachaCategorySelectable(k);
            bool ok = gm.SelectGachaCategory(k);
            Check(!open && !ok && gm.GachaCategory == CardMapCategory.Common, $"A: {k} is locked on a new save and cannot be selected (now {gm.GachaCategory})");
        }
        Check(GameManager.GachaCategorySelectable(CardMapCategory.Common) && GameManager.GachaCategorySelectable(CardMapCategory.Wilderness), "A: Common and Wilderness are selectable from the start");
        yield return ShotW("gc_A_new_save");

        // ---- B: 洞窟のマップだけ開けて、まだ到達していない(洞窟のカードの対象が無い)
        L("== B: empty category ==");
        UnlockRules.QaOverride = true;
        Check(gm.SelectGachaCategory(CardMapCategory.Cave), "B: Cave becomes selectable once the map is open");
        int empty = gm.GachaPoolCount(CardMapCategory.Cave);
        L($"[B] cave pool {empty}");
        if (empty == 0)
        {
            gm.AddMile(GameManager.GachaCostMile * 3);
            int mile0 = gm.TotalOwnedMile, owned0 = TotalOwned(), ref0 = GameManager.GachaCategoryRefusedEmpty;
            bool pulled = (bool)GcPull.Invoke(gm, null);
            Check(!pulled && gm.TotalOwnedMile == mile0 && TotalOwned() == owned0 && GameManager.GachaCategoryRefusedEmpty == ref0 + 1, "B: no cards in the category -> refused, no MILE spent, no card");
            Check(gm.GachaCategory == CardMapCategory.Cave, "B: not switched to another category automatically");
            if (mock)
            {
                string day = TrustedClock.DayKey; int left0 = AdRewards.GachaRemaining(day), shows0 = AdManager.ShowsStarted;
                gm.OnAdGachaTapped();
                Check(AdRewards.GachaRemaining(day) == left0 && AdManager.ShowsStarted == shows0, "B: ad gacha refused before showing an ad (count unchanged)");
            }
            yield return ShotW("gc_B_empty");
        }
        else Check(false, $"B: expected an empty Cave pool on a new save (got {empty})");

        // ---- C: 全部を解禁して 5 分類それぞれ 120 回
        L("== C: per category draws ==");
        gm.DebugSetBestDistance(100500f);
        foreach (var st in UnlockRules.NormalMaps) { GameManager.WriteStageBest(st, 100500); UnlockRules.CommitRun(st, 100500, null); }
        ProgressStats.DevSetFinalDungeonUnlocked(true);
        UnlockRules.QaOverride = false;
        yield return ReloadHome();
        var before = CardInventory.Stacks.Select(s => (s.cardId, s.level, s.count)).ToList();
        var deck0 = gm.DeckCards.ToList();
        var report = new System.Text.StringBuilder();
        foreach (var k in new[] { CardMapCategory.Common, CardMapCategory.Wilderness, CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon })
        {
            Check(gm.SelectGachaCategory(k) && gm.GachaCategory == k, $"C: select {k}");
            Check(gm.GachaSkinState.Skin == k, $"C: machine skin follows the selection ({gm.GachaSkinState.Skin})");
            int pool = gm.GachaPoolCount(k);
            GcClose.Invoke(gm, null);
            gm.AddMile(GameManager.GachaCostMile * 120);
            int bad = 0; var rar = new int[6]; var ids = new HashSet<string>();
            for (int i = 0; i < 120; i++)
            {
                if (!(bool)GcPull.Invoke(gm, null)) { Check(false, $"C: {k} pull {i} refused"); break; }
                var c = GcQueued.Last();
                if (CardMapCategories.Of(c) != k) { bad++; L($"[C] {k}: got {c.cardId} ({CardMapCategories.Of(c)})"); }
                rar[Mathf.Clamp(c.rarity, 1, 5)]++; ids.Add(c.cardId);
            }
            string line = $"{k}: pool {pool} cards, 120 pulls -> ★1 {rar[1]} / ★2 {rar[2]} / ★3 {rar[3]} / ★4 {rar[4]} / ★5 {rar[5]}, distinct {ids.Count}";
            L("[C] " + line); report.AppendLine(line);
            Check(bad == 0, $"C: {k}: no card outside the category in 120 pulls ({bad})");
            GcClose.Invoke(gm, null);
            yield return null;
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "gc_rarity.txt"), report.ToString());

        // ---- D: 切り替え直後・連続(順番待ちに残っている間に切り替え)
        L("== D: switch while cards are queued ==");
        gm.SelectGachaCategory(CardMapCategory.Sky);
        gm.AddMile(GameManager.GachaCostMile * 6);
        for (int i = 0; i < 3; i++) GcPull.Invoke(gm, null);
        gm.SelectGachaCategory(CardMapCategory.Common);
        for (int i = 0; i < 3; i++) GcPull.Invoke(gm, null);
        var q = GcQueued.ToList();
        Check(q.Count == 6 && q.Take(3).All(c => CardMapCategories.Of(c) == CardMapCategory.Sky) && q.Skip(3).All(c => CardMapCategories.Of(c) == CardMapCategory.Common),
            $"D: the 3 cards drawn before the switch stay Sky, the 3 after are Common ({string.Join(",", q.Select(c => CardMapCategories.Of(c)))})");
        Check(gm.GachaSkinState.Skin == CardMapCategory.Common, "D: the machine shows the selected category right after switching");
        // 見せる時の分類の表示はカード自身(Sky のカードを見せている間も「天空」)
        w = 0f;
        while (!gm.GachaResultOpen && w < 4f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("gc_D_reveal_sky_after_switch");
        L($"[D] revealing {gm.GachaResultCardId} while Common is selected");
        GcClose.Invoke(gm, null);
        yield return null;

        // ---- E: 広告ガチャ
        if (mock)
        {
            L("== E: ad gacha ==");
            AdRewards.DevResetGacha();
            gm.SelectGachaCategory(CardMapCategory.Sky);
            int n0 = gm.AdGachaPulls;
            gm.OnAdGachaTapped();
            yield return null;
            gm.SelectGachaCategory(CardMapCategory.Common); // 広告の間に切り替える
            w = 0f;
            while ((AdManager.Showing || gm.AdGachaPulls == n0) && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
            var last = GcQueued.LastOrDefault();
            Check(gm.AdGachaPulls == n0 + 1 && last != null && CardMapCategories.Of(last) == CardMapCategory.Sky, $"E: ad gacha draws from the category chosen when tapped (Sky) even if switched during the ad ({(last != null ? last.cardId + " " + CardMapCategories.Of(last) : "none")})");
            GcClose.Invoke(gm, null);
            NoAdsPass.DevMockSet(true);
            gm.SelectGachaCategory(CardMapCategory.Wilderness);
            gm.OnAdGachaTapped();
            last = GcQueued.LastOrDefault();
            Check(last != null && CardMapCategories.Of(last) == CardMapCategory.Wilderness, "E: pass pull uses the selected category");
            NoAdsPass.DevMockSet(false);
            GcClose.Invoke(gm, null);
        }

        // ---- F: 所持(Lv/枚数)とデッキ
        var after = CardInventory.Stacks.Select(s => (s.cardId, s.level, s.count)).ToList();
        bool kept = before.All(b => after.Any(a => a.cardId == b.cardId && a.level == b.level && a.count >= b.count));
        bool onlyLv1New = after.Where(a => !before.Any(b => b.cardId == a.cardId && b.level == a.level)).All(a => a.level == 1);
        Check(kept && onlyLv1New, "F: existing cards keep their level and count (draws only add Lv1 copies)");
        Check(gm.DeckCards.SequenceEqual(deck0), "F: deck unchanged");

        // ---- G: 見た目(縦/横は起動の窓で)。選んでいる分類の案内
        gm.SelectGachaCategory(CardMapCategory.Sky);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("gc_G_sky_selected");
        gm.SelectGachaCategory(CardMapCategory.LastDungeon);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("gc_G_ld_selected");
        Rect row = gm.LastGachaLandRowRect;
        Check(row.width > 0f && row.xMin >= 0f && row.xMax <= Screen.width && row.yMax <= Screen.height, $"G: the category row and its line are inside the screen ({row}, screen {Screen.width}x{Screen.height})");
        Rect st0 = gm.LastHomeSignRects[4];
        Check(st0.width <= 0f || !row.Overlaps(st0), $"G: the category row does not overlap the Depart sign (row {row}, sign {st0})");

        // 言語ごとの見切れ(-gcLangs en,de,ru,ar 等)
        foreach (var lang in Arg("-gcLangs", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            Loc.Set(lang, false);
            gm.SelectGachaCategory(CardMapCategory.LastDungeon);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return ShotW($"gc_L_{lang}");
            Rect rr = gm.LastGachaLandRowRect, st = gm.LastHomeSignRects[4];
            Check(rr.width > 0f && rr.xMin >= 0f && rr.xMax <= Screen.width, $"L {lang}: the category row is inside the screen ({rr})");
            Check(st.width <= 0f || !rr.Overlaps(st), $"L {lang}: the category row does not overlap the Depart sign (row {rr}, sign {st})");
        }
        Loc.Set("ja", false);
        ProgressStats.DevSetFinalDungeonUnlocked(false);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif

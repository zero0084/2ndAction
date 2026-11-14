#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ガチャの対象 = 最後に出発したマップ(2026-11-14 夜、マスターの訂正)の自動試験: -qaGachaCat <dir> -monetizationMock 1(テスト用データの中だけ)
//  A 新しいデータ: まだ出発していない → 荒野。分類を選ぶ欄は無い
//  B マップを選んだだけでは変わらない / 出発した対象のカードが無い → 引けない(MILE も広告の回数も使わない)
//  C 4マップそれぞれに出発 → 見た目と対象がそのマップ、120 回でマップか共通のカードだけ(2026-11-15 共通 30% / マップ 70%)
//  D 出発で切り替わる前に引いた分は、そのまま / E 広告ガチャはタップ時の対象で確定・パスでも同じ
//  F 所持(Lv/枚数)とデッキは増えるだけ / G ホームへの帰還(読み直し)の後も同じマップ / 見た目と言語ごとの見切れ(-gcLangs)
public partial class QaSweep
{
    static readonly FieldInfo GcQueue = typeof(GameManager).GetField("gachaRevealQueue", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo GcPull = typeof(GameManager).GetMethod("GachaPullOnce", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo GcClose = typeof(GameManager).GetMethod("ClearGachaPresentation", BindingFlags.NonPublic | BindingFlags.Instance);

    List<CardDefinition> GcQueued => (List<CardDefinition>)GcQueue.GetValue(gm);
    static readonly (string stage, CardMapCategory cat)[] GcMaps =
    {
        ("wasteland_road", CardMapCategory.Wilderness), ("natural_cave", CardMapCategory.Cave), ("sky_corridor", CardMapCategory.Sky), ("last_corridor", CardMapCategory.LastDungeon),
    };

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
        SaveStore.DeleteKey(GameManager.GachaLastMapKey); SaveStore.Save();
        UnlockRules.QaOverride = false;
        if (UnlockRules.DevUnlockAll) UnlockRules.DevUnlockAll = false;
        yield return ReloadHome();

        // ---- A: まだ出発していない
        L("== A: no departure yet ==");
        Check(GameManager.GachaLastMapStageId == "" && gm.GachaCategory == CardMapCategory.Wilderness && gm.GachaSkinState.Skin == CardMapCategory.Wilderness, $"A: before any departure the gacha is Wilderness ({gm.GachaCategory}, skin {gm.GachaSkinState.Skin})");
        yield return ShotW("gc_A_new_save");

        // ---- B: 選んだだけでは変わらない / 対象なし
        L("== B: stage selection alone / empty pool ==");
        UnlockRules.QaOverride = true; // 洞窟のマップを開ける(到達はまだ 0m)
        SaveStore.SetString("SelectedStageId", "natural_cave"); SaveStore.Save();
        yield return ReloadHome();
        Check(gm.GachaCategory == CardMapCategory.Wilderness, $"B: choosing a map on the stage select does not change the gacha ({gm.GachaCategory})");
        gm.DevNoteGachaDeparture("natural_cave");
        Check(gm.GachaCategory == CardMapCategory.Cave && gm.GachaSkinState.Skin == CardMapCategory.Cave && GameManager.GachaLastMapStageId == "natural_cave", "B: departing to the cave switches the gacha to Cave (look and target)");
        int empty = gm.GachaPoolCount(CardMapCategory.Cave);
        if (empty == 0)
        {
            gm.AddMile(GameManager.GachaCostMile * 3);
            int mile0 = gm.TotalOwnedMile, owned0 = TotalOwned(), ref0 = GameManager.GachaCategoryRefusedEmpty;
            bool pulled = (bool)GcPull.Invoke(gm, null);
            Check(!pulled && gm.TotalOwnedMile == mile0 && TotalOwned() == owned0 && GameManager.GachaCategoryRefusedEmpty == ref0 + 1, "B: no cards for the cave yet -> refused (not switched to Common only), no MILE spent, no card");
            string hint = gm.GachaMapUnlockHint(CardMapCategory.Cave);
            L($"[B] hint '{hint}' sub '{typeof(GameManager).GetMethod("GachaSignSubLine", NP).Invoke(gm, null)}'");
            Check(hint.Contains("1,000"), $"B: the unlock condition is known for the cave ('{hint}')");
            GachaOddsPanel.Open();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return ShotW("gc_B_odds_empty");
            GachaOddsPanel.Close();
            if (mock)
            {
                string day = TrustedClock.DayKey; int left0 = AdRewards.GachaRemaining(day), shows0 = AdManager.ShowsStarted;
                gm.OnAdGachaTapped();
                Check(AdRewards.GachaRemaining(day) == left0 && AdManager.ShowsStarted == shows0, "B: ad gacha refused before showing an ad (count unchanged)");
            }
            yield return new WaitForSecondsRealtime(0.3f);
            yield return ShotW("gc_B_empty");
        }
        else Check(false, $"B: expected an empty Cave pool on a new save (got {empty})");

        // ---- C: 全部を解禁して 4 マップそれぞれ 120 回
        L("== C: per map draws ==");
        gm.DebugSetBestDistance(100500f);
        foreach (var st in UnlockRules.NormalMaps) { GameManager.WriteStageBest(st, 100500); UnlockRules.CommitRun(st, 100500, null); }
        ProgressStats.DevSetFinalDungeonUnlocked(true);
        UnlockRules.QaOverride = false;
        yield return ReloadHome();
        var before = CardInventory.Stacks.Select(s => (s.cardId, s.level, s.count)).ToList();
        var deck0 = gm.DeckCards.ToList();
        var report = new System.Text.StringBuilder();
        foreach (var (stage, k) in GcMaps)
        {
            gm.DevNoteGachaDeparture(stage);
            Check(gm.GachaCategory == k && gm.GachaSkinState.Skin == k, $"C: departed {stage} -> gacha {k} (now {gm.GachaCategory}, skin {gm.GachaSkinState.Skin})");
            int pool = gm.GachaPoolCount(k);
            GcClose.Invoke(gm, null);
            gm.AddMile(GameManager.GachaCostMile * 120);
            int bad = 0, common = 0, sideMismatch = 0; var rar = new int[6]; var ids = new HashSet<string>();
            for (int i = 0; i < 120; i++)
            {
                if (!(bool)GcPull.Invoke(gm, null)) { Check(false, $"C: {k} pull {i} refused"); break; }
                var c = GcQueued.Last();
                var ck = CardMapCategories.Of(c);
                if (ck != k && ck != CardMapCategory.Common) { bad++; L($"[C] {k}: got {c.cardId} ({ck})"); }
                if (ck == CardMapCategory.Common) common++;
                if ((ck == CardMapCategory.Common ? 1 : 0) != GameManager.LastDrawCommonSide) sideMismatch++;
                rar[Mathf.Clamp(c.rarity, 1, 5)]++; ids.Add(c.cardId);
            }
            string line = $"{k}: pool {pool} cards, 120 pulls -> map {120 - common} / common {common}; ★1 {rar[1]} / ★2 {rar[2]} / ★3 {rar[3]} / ★4 {rar[4]} / ★5 {rar[5]}, distinct {ids.Count}";
            L("[C] " + line); report.AppendLine(line);
            Check(bad == 0, $"C: {k}: only the map's cards or Common cards in 120 pulls ({bad} others)");
            Check(sideMismatch == 0, $"C: {k}: every card matches the side (Common / map) the draw chose ({sideMismatch})");
            GcClose.Invoke(gm, null);
            yield return null;
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "gc_rarity.txt"), report.ToString());

        // ---- D: 出発で切り替わる前に引いた分
        L("== D: departure while cards are queued ==");
        gm.DevNoteGachaDeparture("sky_corridor");
        gm.AddMile(GameManager.GachaCostMile * 6);
        for (int i = 0; i < 3; i++) GcPull.Invoke(gm, null);
        gm.DevNoteGachaDeparture("wasteland_road");
        for (int i = 0; i < 3; i++) GcPull.Invoke(gm, null);
        var q = GcQueued.ToList();
        // 2026-11-15: 共通 30% があるので、前の3枚は 天空か共通、後の3枚は 荒野か共通
        Check(q.Count == 6 && q.Take(3).All(c => CardMapCategories.Of(c) == CardMapCategory.Sky || CardMapCategories.Of(c) == CardMapCategory.Common)
                           && q.Skip(3).All(c => CardMapCategories.Of(c) == CardMapCategory.Wilderness || CardMapCategories.Of(c) == CardMapCategory.Common),
            $"D: the 3 cards drawn before are Sky/Common, the 3 after are Wilderness/Common ({string.Join(",", q.Select(c => CardMapCategories.Of(c)))})");
        GcClose.Invoke(gm, null);
        yield return null;

        // ---- E: 広告ガチャ
        if (mock)
        {
            L("== E: ad gacha ==");
            AdRewards.DevResetGacha();
            gm.DevNoteGachaDeparture("sky_corridor");
            // 天空のカードを指定しておく: タップの時の対象(天空)から引いた時だけ、このカードになる(荒野へ変わっていたら荒野/共通の対象には無い)
            var skyCard = CardDatabase.AllCards.First(c => c != null && CardMapCategories.Of(c) == CardMapCategory.Sky && c.rarity == 2);
            GameManager.DevForceGachaCard = skyCard.cardId;
            int n0 = gm.AdGachaPulls;
            gm.OnAdGachaTapped();
            yield return null;
            gm.DevNoteGachaDeparture("wasteland_road"); // 広告の間に対象が変わっても
            w = 0f;
            while ((AdManager.Showing || gm.AdGachaPulls == n0) && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
            var last = GcQueued.LastOrDefault();
            Check(gm.AdGachaPulls == n0 + 1 && last != null && last.cardId == skyCard.cardId, $"E: ad gacha draws from the target fixed when tapped (Sky) ({(last != null ? last.cardId + " " + CardMapCategories.Of(last) : "none")})");
            GcClose.Invoke(gm, null);
            NoAdsPass.DevMockSet(true);
            gm.DevNoteGachaDeparture("natural_cave");
            var caveCard = CardDatabase.AllCards.First(c => c != null && CardMapCategories.Of(c) == CardMapCategory.Cave && c.rarity == 2 && c.cardId != "vampire");
            GameManager.DevForceGachaCard = caveCard.cardId;
            gm.OnAdGachaTapped();
            last = GcQueued.LastOrDefault();
            Check(last != null && last.cardId == caveCard.cardId, "E: pass pull uses the same target as the normal gacha (Cave)");
            GameManager.DevForceGachaCard = null;
            NoAdsPass.DevMockSet(false);
            GcClose.Invoke(gm, null);
        }

        // ---- F: 所持(Lv/枚数)とデッキ
        var after = CardInventory.Stacks.Select(s => (s.cardId, s.level, s.count)).ToList();
        bool kept = before.All(b => after.Any(a => a.cardId == b.cardId && a.level == b.level && a.count >= b.count));
        bool onlyLv1New = after.Where(a => !before.Any(b => b.cardId == a.cardId && b.level == a.level)).All(a => a.level == 1);
        Check(kept && onlyLv1New, "F: existing cards keep their level and count (draws only add Lv1 copies)");
        Check(gm.DeckCards.SequenceEqual(deck0), "F: deck unchanged");

        // ---- G: 帰還/再起動(読み直し)の後も同じマップ。見た目
        gm.DevNoteGachaDeparture("sky_corridor");
        yield return ReloadHome();
        Check(gm.GachaCategory == CardMapCategory.Sky && gm.GachaSkinState.Skin == CardMapCategory.Sky, $"G: after returning home (scene reload) the gacha is still Sky ({gm.GachaCategory})");
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("gc_G_sky");
        foreach (var (stage, k) in GcMaps)
        {
            gm.DevNoteGachaDeparture(stage);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return ShotW($"gc_G_{k}");
            GcCheckLayout($"G {k}");
        }
        // 提供割合: 開く → 表の数字 = 抽選の表(GachaOddsNow)。閉じる
        gm.DevNoteGachaDeparture("wasteland_road");
        Check(gm.LastGachaOddsButtonRect.width > 0f, $"O: the odds button is on screen ({gm.LastGachaOddsButtonRect})");
        GachaOddsPanel.Open();
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("gc_O_odds_wild");
        var tot = gm.GachaOddsNow(out var cpart, out var mpart, out int mc, out int cc);
        float sum = 0f; foreach (var v in tot) sum += v;
        float cs = 0f, ms = 0f; foreach (var v in cpart) cs += v; foreach (var v in mpart) ms += v;
        L($"[O] map {mc} common {cc} total {string.Join("/", tot.Select(v => (v * 100f).ToString("0.##")))} commonPart {cs:0.###} mapPart {ms:0.###}");
        Check(GachaOddsPanel.IsOpen && Mathf.Abs(sum - 1f) < 1e-4f && Mathf.Abs(cs - GameManager.GachaCommonShare) < 1e-4f && Mathf.Abs(ms - (1f - GameManager.GachaCommonShare)) < 1e-4f, "O: the odds panel shows Common 30% / map 70%, totals 100%");
        Check(UiInputGate.ModalOpen, "O: the home is blocked while the panel is open");
        GachaOddsPanel.Close();
        Check(!GachaOddsPanel.IsOpen, "O: closes");

        // 言語ごとの見切れ(-gcLangs en,de,ru,ar 等)
        foreach (var lang in Arg("-gcLangs", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            Loc.Set(lang, false);
            gm.DevNoteGachaDeparture("last_corridor");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return ShotW($"gc_L_{lang}");
            GcCheckLayout($"L {lang}");
            GachaOddsPanel.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ShotW($"gc_LO_{lang}");
            GachaOddsPanel.Close();
        }
        Loc.Set("ja", false);
        ProgressStats.DevSetFinalDungeonUnlocked(false);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // 縦画面: 台の下の1行が画面の中で「出発」の札に重ならない。横画面: 札の2行目(札は画面の中)
    void GcCheckLayout(string tag)
    {
        Rect st = gm.LastHomeSignRects[4];
        if (Screen.height > Screen.width)
        {
            Rect r = gm.LastGachaLandRowRect;
            Check(r.width > 0f && r.xMin >= 0f && r.xMax <= Screen.width && r.yMax <= Screen.height, $"{tag}: the target line under the machine is inside the screen ({r})");
            Check(st.width <= 0f || !r.Overlaps(st), $"{tag}: the target line does not overlap the Depart sign (line {r}, sign {st})");
            Rect m = gm.DebugGachaMachineRectNow;
            Check(m.width <= 0f || m.xMax <= Screen.width + 1f, $"{tag}: the gacha machine is not cut at the right edge ({m}, screen {Screen.width})");
        }
        else
        {
            Rect g = gm.LastHomeSignRects[3];
            Check(g.width > 0f && g.xMin >= 0f && g.xMax <= Screen.width, $"{tag}: the gacha sign (with the target line) is inside the screen ({g})");
            Check(st.width <= 0f || !g.Overlaps(st), $"{tag}: the gacha sign does not overlap the Depart sign ({g} / {st})");
            var exit = (Rect)typeof(GameManager).GetMethod("GetHomeExitButtonRect", NP).Invoke(gm, null);
            Rect ad = gm.LastAdGachaButtonRect;
            Check(ad.width <= 0f || exit.width <= 0f || !ad.Overlaps(exit), $"{tag}: the ad gacha button does not overlap the right column (ad {ad}, exit {exit})");
        }
    }
}
#endif

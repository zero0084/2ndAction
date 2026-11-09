#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// X 投稿用「カードシステム」紹介動画の撮影(2026-11-09): QaSweep -storeCapture の場面 cp_*(撮影用ビルドだけ。ゲームの仕様は変えない)
//  どれも本物の画面を、本物の操作の処理(タップ/ドラッグ/ボタンの処理と同じ入口)で動かして撮る。
//  ・撮影用の保存(-memSave)を「遊び込んだ人」へ: 3マップとも 52km 到達で帰還済み、いろいろなカードを所持、デッキのプリセット 2 つ、MILE
//    (ラストダンジョン/隠しカード/100km のカードは持たせない = ネタバレしない)
//  ・押した位置: raw/taps.tsv(撮影名・コマ・種類・画面の割合 x,y 左上が 0)。編集で指の印を重ねる
public partial class QaSweep
{
    IEnumerator CpShot(string s) => s switch
    {
        "cp_cards" => CpBigCards(s),
        "cp_gacha" => CpGacha(s),
        "cp_deck" => CpDeck(s),
        "cp_fuse_same" => CpFuse(s, 0),
        "cp_fuse_cross" => CpFuse(s, 1),
        "cp_fuse_fail" => CpFuse(s, 2),
        "cp_fuse_main" => CpFuse(s, 3),
        "cp_mastery" => CpFuse(s, 4),
        "cp_choice" => CpChoice(s, Arg("-cpChoiceChar", "swordsman")),
        "cp_battle" => CpBattle(s, Arg("-cpBattleChar", "swordsman")),
        "cp_home" => CpHome(s),
        _ => null,
    };

    // ---------------------------------------------------------------- 撮影用の保存
    // プリセット 1「炎と雷」(11 枚: 撮影の中で 1 枚足して 12/12)、プリセット 2「風と氷」(12 枚)
    static readonly string[] CpDeckA = { "inferno", "thunder_lord", "chain_lightning", "flame_blade", "thunder_strike", "burning_soul", "double_attack", "attack_up", "attack_speed_up", "heart_up", "giant_slayer" };
    static readonly string[] CpDeckB = { "tornado", "gale", "wind_cutter", "absolute_zero", "frost_edge", "ice_prison", "sonic_blade", "air_dominion", "speed_up", "shield", "vampire", "sky_master" };
    const string CpAddCard = "phoenix";      // 一覧からタップでデッキへ(★5)
    const string CpSwapIn = "combustion";    // ドラッグで入れ替え(burning_soul の枠へ)
    const string CpSwapOut = "burning_soul";
    // 合成: 同名 = TEMPEST Lv3 + TEMPEST Lv2 / 異名 = JUDGMENT BOLT Lv2 + FIRE WHIRL Lv2 / MASTERY = SHATTER STRIKE Lv9 MAX + Lv3
    const string CpSame = "tempest", CpCrossA = "judgment_bolt", CpCrossB = "fire_whirl", CpMastery = "shatter_strike";
    // 見せない(ネタバレ/終盤): ラストダンジョンのカード・100km のカード・キャラ専用の ULTIMATE
    static readonly HashSet<string> CpHidden = new HashSet<string> { "ultimate", "one_more_mile", "deaths_contract", "no_turning_back", "the_long_road", "pandemonium", "character_ultimate" };

    bool cpReady;
    readonly List<string> cpTaps = new List<string>();
    string cpShot = "";

    // 押した位置を記録(screen = Unity の画面座標、左下が 0)
    void CpTap(string kind, Vector2 screen)
    {
        float x = screen.x / Mathf.Max(1, Screen.width), y = 1f - screen.y / Mathf.Max(1, Screen.height);
        cpTaps.Add($"{cpShot}\t{scFrameNow}\t{kind}\t{x:F4}\t{y:F4}");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "raw", "taps.tsv"), cpTaps);
    }
    // IMGUI の座標(左上が 0、GUI.matrix の倍率なし)
    void CpTapGui(string kind, Vector2 gui) => CpTap(kind, new Vector2(gui.x, Screen.height - gui.y));
    static Vector2 CpCenter(RectTransform r) => r == null ? Vector2.zero : RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));

    IEnumerator CpHomeReady()
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return CpSetup();
        NoticeQueue.ClearAll(); ChallengeToast.ClearAll();
        if (!cpQuiet) { cpQuiet = true; StartCoroutine(CpQuietLoop()); }
    }
    bool cpQuiet;
    // 撮影の間、チャレンジ達成の帯は出さない(撮影用の保存で距離の記録をまとめて入れたため、関係のない達成がまとめて出る)
    IEnumerator CpQuietLoop() { while (true) { ChallengeToast.ClearAll(); yield return null; } }

    IEnumerator CpSetup()
    {
        if (cpReady) yield break;
        cpReady = true;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        // 3 マップとも 52km 前後で帰還済み(普通に遊んで確定した記録と同じ入口)
        double[] reach = { 52418.6, 51207.3, 50863.9 };
        for (int i = 0; i < UnlockRules.NormalMaps.Length; i++)
        {
            GameManager.WriteStageBest(UnlockRules.NormalMaps[i], reach[i]);
            UnlockRules.CommitRun(UnlockRules.NormalMaps[i], reach[i], null);
        }
        gm.DebugSetBestDistance(52418.6f);
        // 解放のお知らせ/NEW の印は「もう見た」(遊び込んだ人の状態。撮影中に帯が出ない)
        var markN = typeof(UnlockRules).GetMethod("MarkNotified", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var r in UnlockRules.StageRules) { markN?.Invoke(null, new object[] { "s:" + r.id }); UnlockRules.MarkSeen("s:" + r.id); }
        foreach (var r in UnlockRules.CharRules) { markN?.Invoke(null, new object[] { "c:" + r.id }); UnlockRules.MarkSeen("c:" + r.id); }
        NoticeQueue.ClearAll();
        // 所持カード: 解禁済みの普通のカード(隠し/終盤は除く)をいろいろな Lv で
        int n = 0;
        // 52km の記録で解禁済みのカードだけ(ラストダンジョンの分類・100km のカードは入らない)
        foreach (var c in CardDatabase.UnlockedCards.ToList())
        {
            if (c == null || CpHidden.Contains(c.cardId) || CardMapCategories.Of(c) == CardMapCategory.LastDungeon) continue;
            if (CardInventory.GetTotalCount(c.cardId) > 0) continue;
            // ガチャで NEW が出うるよう、★3 以上の 4 枚に 1 枚は持たせない(デッキ/合成に使うカードは下で別に持たせる)
            if (c.rarity >= 3 && (c.cardId.GetHashCode() & 3) == 3 && System.Array.IndexOf(CpDeckA, c.cardId) < 0 && System.Array.IndexOf(CpDeckB, c.cardId) < 0) { n++; continue; }
            int lv = 1 + (n * 7 + c.cardId.Length) % 5;
            CardInventory.AddCard(c.cardId, lv, 1 + n % 2);
            n++;
        }
        void Have(string id, int lv, int count) { if (CardDatabase.FindById(id) != null) CardInventory.AddCard(id, lv, count); }
        int k = 0;
        foreach (var id in CpDeckA) Have(id, 3 + (k++ % 5), 1);
        foreach (var id in CpDeckB) Have(id, 3 + (k++ % 5), 1);
        Have(CpAddCard, 4, 1); Have(CpSwapIn, 5, 1);
        Have(CpSame, 3, 1); Have(CpSame, 2, 1);
        Have(CpCrossA, 2, 3); Have(CpCrossB, 2, 3);
        Have(CpMastery, 9, 1); Have(CpMastery, 3, 1);
        foreach (var s in CardInventory.Stacks.ToList()) CardInventory.ClearNewUnconfirmed(s.cardId);
        // プリセット
        gm.SetDeck(CpKeys(CpDeckA));
        gm.RenamePreset(0, "炎と雷");
        gm.ActivatePreset(1);
        gm.SetDeck(CpKeys(CpDeckB));
        gm.RenamePreset(1, "風と氷");
        gm.ActivatePreset(0);
        gm.AddMile(GameManager.GachaCostMile * 12 + 380);
        SaveStore.Save();
        L($"[cp] save: stacks {CardInventory.Stacks.Count}, deck {gm.DeckCards.Count} ({string.Join(",", gm.DeckCards)}), MILE {gm.TotalOwnedMile}");
        yield return null;
    }

    // ---------------------------------------------------------------- ホーム(入口の確認用)
    IEnumerator CpHome(string name)
    {
        yield return CpHomeReady();
        cpShot = name;
        ScRecStart(name);
        yield return ScWait(3f);
        ScStill("home");
        yield return ScRecStop();
    }

    // ---------------------------------------------------------------- カードの絵(大きな詳細を次々に)
    IEnumerator CpBigCards(string name)
    {
        yield return CpHomeReady();
        cpShot = name;
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = gm.deckEditUI;
        string[] show = Arg("-cpBig", "phoenix,inferno,thunder_lord,tornado,absolute_zero,sonic_blade,combo_master,giant_slayer").Split(',');
        ScRecStart(name);
        yield return ScWait(0.4f);
        // 所持カードの一覧を ★4 に絞り、レア度順で上から下へ流す(本物の絞り込み/並び替え/スクロール)
        ui.DebugSetRarityFilter(4);
        ui.DebugSetSort(DeckEditUI.SortKey.Rarity, true);
        ui.DebugSetOwnedScroll(1f);
        yield return ScWait(0.8f);
        ScStill("grid");
        int steps = 60 * int.Parse(Arg("-cpGridSecs", "6"));
        for (int i = 0; i <= steps; i++) { ui.DebugSetOwnedScroll(1f - i / (float)steps); yield return ScWait(1f / 60f); }
        yield return ScWait(0.6f);
        ui.DebugSetRarityFilter(0);
        ui.DebugSetSort(DeckEditUI.SortKey.Standard, false);
        ui.DebugSetOwnedScroll(1f);
        yield return ScWait(0.4f);
        foreach (var id in show)
        {
            string key = CpKey(id);
            var st = key == null ? null : CardInventory.Stacks.Where(s => s.cardId == key).OrderByDescending(s => s.level).FirstOrDefault();
            if (st == null) continue;
            ui.OpenBigDetail(st.cardId, st.level);
            yield return ScWait(0.3f);
            ScStill("big_" + id);
            yield return ScWait(1.5f);
            ui.CloseBigDetail();
            yield return ScWait(0.25f);
        }
        yield return ScRecStop();
        yield return CpCloseDeck();
    }

    IEnumerator CpCloseDeck()
    {
        var ui = gm.deckEditUI;
        if (ui != null && ui.root != null && ui.root.activeInHierarchy) ui.Close();
        float w = 0f;
        while (((ui != null && ui.root != null && ui.root.activeInHierarchy) || (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning)) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        NoticeQueue.ClearAll();
    }

    // ---------------------------------------------------------------- ガチャ(ホームのガチャ本体を押す → 1 枚ずつ確認)
    IEnumerator CpGacha(string name)
    {
        yield return CpHomeReady();
        cpShot = name;
        var tap = typeof(GameManager).GetMethod("OnGachaMachineTapped", NP);
        var close = typeof(GameManager).GetMethod("CloseGachaResult", NP);
        var rectF = typeof(GameManager).GetField("lastGachaMachineRect", NP);
        int pulls = int.Parse(Arg("-cpPulls", "10"));
        ScRecStart(name);
        yield return ScWait(1.0f);
        for (int i = 0; i < pulls; i++)
        {
            var r = (Rect)rectF.GetValue(gm);
            CpTapGui("tap", r.center);
            tap.Invoke(gm, null);
            float w = 0f;
            while (!gm.GachaResultOpen && w < 6f) { yield return ScWait(1f / 60f); w += 1f / 60f; }
            L($"[cp] gacha #{i + 1}: {gm.GachaResultCardId} new={gm.GachaResultIsNew} (machine rect {r})");
            yield return ScWait(2.6f);
            ScStill($"gacha{i + 1}");
            yield return ScWait(0.6f);
            CpTapGui("tap", new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            close.Invoke(gm, null);
            yield return ScWait(0.5f);
        }
        yield return ScRecStop();
    }

    // ---------------------------------------------------------------- デッキ編集
    //  ホーム → デッキ編集 → 一覧のカードを長押し(大きな詳細で効果を見る)→ 閉じる → タップでデッキへ(11 → 12/12)
    //  → 一覧のカードをデッキの枠へドラッグ(確認 → はい で入れ替え)→ プリセットを ▶ で切り替え → ◀ で戻す
    IEnumerator CpDeck(string name)
    {
        yield return CpHomeReady();
        cpShot = name;
        if (DeckPresets.Active != 0) gm.ActivatePreset(0);
        gm.SetDeck(CpKeys(CpDeckA));
        string addKey = CpKey(CpAddCard), inKey = CpKey(CpSwapIn), outKey = CpKey(CpSwapOut);
        var ui = gm.deckEditUI;
        ScRecStart(name);
        yield return ScWait(1.0f);
        CpTap("home_deck", Vector2.zero); // ホームの入口(位置は編集で絵から決める)
        gm.OpenDeckEdit();
        yield return ScWait(1.8f);
        ScStill("open");
        // 一覧で ★5 を見える所へ → 長押しで大きな詳細
        int ai = ui.DebugOwnedIndex(addKey);
        if (ai < 0) { gm.CloseDeckEdit(); L("[cp] deck: card to add is not in the list"); yield return ScRecStop(); yield break; }
        ui.ScrollOwnedIntoView(ai);
        yield return ScWait(0.6f);
        Vector2 p = ui.DebugOwnedPos(ai);
        CpTap("hold", p);
        yield return ScWait(0.5f);
        ui.DebugLongPress(false, false, ai);
        yield return ScWait(2.6f);
        ScStill("big");
        ui.CloseBigDetail();
        yield return ScWait(0.6f);
        // タップでデッキへ
        p = ui.DebugOwnedPos(ui.DebugOwnedIndex(addKey));
        CpTap("tap", p);
        ui.DebugTapAt(p);
        L($"[cp] deck: tapped {CpAddCard} -> {gm.DeckCards.Count}/12");
        yield return ScWait(1.6f);
        ScStill("added");
        // ドラッグで入れ替え
        int si = ui.DebugOwnedIndex(inKey);
        int slot = gm.DeckCards.ToList().IndexOf(outKey);
        if (si >= 0 && slot >= 0)
        {
            ui.ScrollOwnedIntoView(si);
            yield return ScWait(0.6f);
            Vector2 from = ui.DebugOwnedPos(si), to = ui.DebugDeckSlotPos(slot);
            int steps = 40;
            CpTap("drag_start", from);
            int f0 = scFrameNow;
            var drag = ui.DebugDrag(false, si, to, steps, 0.15f);
            int stepNo = 0;
            while (drag.MoveNext())
            {
                CpTap("drag", Vector2.Lerp(from, to, Mathf.Clamp01(++stepNo / (float)steps)));
                yield return drag.Current;
            }
            CpTap("drag_end", to);
            yield return ScWait(1.0f);
            ScStill("confirm");
            if (ui.confirmDialog != null && ui.confirmDialog.IsOpen)
            {
                CpTap("tap", CpCenter(ui.confirmDialog.yesRect));
                ui.DebugConfirmYes();
            }
            L($"[cp] deck: dragged {CpSwapIn} onto slot {slot} -> {string.Join(",", gm.DeckCards)}");
            yield return ScWait(1.6f);
            ScStill("swapped");
        }
        // プリセットの切り替え(帯の ▶)
        var bar = FindFirstObjectByType<DeckPresetUi>();
        var use = typeof(DeckPresetUi).GetMethod("Use", NP);
        Vector2 Arrow(bool right)
        {
            var rt = ui.DeckHeaderRect;
            if (rt == null) return Vector2.zero;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            float s = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f));
            float h = Mathf.Clamp((c[1].y - c[0].y) / s, 34f, 46f) * s;
            float x = right ? c[2].x - h * 0.55f : c[0].x + h * 0.55f;
            return new Vector2(x, (c[0].y + c[1].y) * 0.5f);
        }
        if (bar != null && use != null)
        {
            CpTap("tap", Arrow(true));
            use.Invoke(bar, new object[] { 1 });
            L($"[cp] deck: preset -> {DeckPresets.Active + 1} {DeckPresets.DisplayName(DeckPresets.Active)} ({gm.DeckCards.Count})");
            yield return ScWait(2.0f);
            ScStill("preset2");
            CpTap("tap", Arrow(false));
            use.Invoke(bar, new object[] { 0 });
            yield return ScWait(1.6f);
            ScStill("preset1");
        }
        yield return ScRecStop();
        yield return CpCloseDeck();
    }

    // ---------------------------------------------------------------- カード合成
    // mode 0 同名 / 1 異名(両方成功)/ 2 異名(両方失敗)/ 3 異名(メインだけ)/ 4 MASTERY(Lv9 MAX + 同名)
    //  異名の結果は撮影で 3 通りを撮り分けるため、開発用の結果固定(DebugForcedOutcome)を使う。確率そのもの(50%/25%)は変えない
    static readonly System.Reflection.FieldInfo CpCells = typeof(CardFusionUI).GetField("cells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpShown = typeof(CardFusionUI).GetField("shown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpGrid = typeof(CardFusionUI).GetField("gridScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpFuseBtn = typeof(CardFusionUI).GetField("fuseButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpGCols = typeof(CardFusionUI).GetField("gCols", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpGCellH = typeof(CardFusionUI).GetField("gCellH", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpGGap = typeof(CardFusionUI).GetField("gGap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.MethodInfo CpBind = typeof(CardFusionUI).GetMethod("BindVisibleCells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // 一覧のそのカードを見える所へ動かして、画面の位置を返す
    Vector2 CpFusionCell(CardFusionUI ui, string key)
    {
        var shown = CpShown.GetValue(ui) as List<CardInventory.Stack>;
        int idx = shown == null ? -1 : shown.FindIndex(s => s.cardId == key);
        if (idx < 0) return Vector2.zero;
        var grid = CpGrid.GetValue(ui) as ScrollRect;
        int cols = Mathf.Max(1, (int)CpGCols.GetValue(ui));
        float pitch = (float)CpGCellH.GetValue(ui) + (float)CpGGap.GetValue(ui);
        int row = idx / cols;
        if (grid != null)
        {
            var content = grid.content;
            float viewH = grid.viewport.rect.height;
            float top = content.anchoredPosition.y;
            float rowTop = row * pitch, rowBot = rowTop + pitch;
            if (rowTop < top || rowBot > top + viewH)
            {
                float maxY = Mathf.Max(0f, content.rect.height - viewH);
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(rowTop - 6f, 0f, maxY));
                Canvas.ForceUpdateCanvases();
                CpBind.Invoke(ui, new object[] { true });
            }
        }
        if (CpCells.GetValue(ui) is System.Collections.IList cells)
            foreach (var c in cells)
            {
                var t = c.GetType();
                if ((string)t.GetField("key").GetValue(c) == key && (int)t.GetField("index").GetValue(c) == idx)
                    return CpCenter((RectTransform)t.GetField("root").GetValue(c));
            }
        return Vector2.zero;
    }

    // カード ID(主能力)と Lv から所持の束のキー(Lv2 以上は v2 キー)。lv < 0 なら一番高い Lv
    static readonly System.Reflection.FieldInfo CpDetailScroll = typeof(CardFusionUI).GetField("detailScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo CpDetailText = typeof(CardFusionUI).GetField("detailText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    IEnumerator CpScrollDetail(CardFusionUI ui, string marker)
    {
        var sr = CpDetailScroll.GetValue(ui) as ScrollRect;
        var txt = CpDetailText.GetValue(ui) as Text;
        if (sr == null || txt == null || sr.content == null) yield break;
        Canvas.ForceUpdateCanvases();
        int idx = txt.text.IndexOf(marker, System.StringComparison.Ordinal);
        var gen = txt.cachedTextGenerator;
        if (idx < 0 || gen == null || gen.lineCount == 0) { L($"[cp] detail scroll: marker {marker} not found"); yield break; }
        // 文字列の位置 → その行の上端(リッチテキストのタグも文字数に入るので、行の始まりの文字位置で比べる)
        int li = 0;
        for (int i = 0; i < gen.lineCount; i++) if (gen.lines[i].startCharIdx <= idx) li = i;
        float y = gen.lines[li].topY / Mathf.Max(0.01f, txt.pixelsPerUnit);
        var content = sr.content;
        Vector3 cl = content.InverseTransformPoint(txt.rectTransform.TransformPoint(new Vector3(0f, y, 0f)));
        float maxY = Mathf.Max(0f, content.rect.height - sr.viewport.rect.height);
        float target = Mathf.Clamp(content.rect.yMax - cl.y - 10f, 0f, maxY);
        float from = content.anchoredPosition.y;
        L($"[cp] detail scroll to {marker}: line {li}/{gen.lineCount}, {from:F0} -> {target:F0} (max {maxY:F0})");
        if (Mathf.Abs(target - from) < 4f) yield break;
        Vector2 c0 = CpCenter(sr.viewport);
        float h = sr.viewport.rect.height * sr.viewport.lossyScale.y;
        Vector2 f0 = c0 + new Vector2(0f, -0.25f * h), f1 = c0 + new Vector2(0f, 0.25f * h);
        CpTap("drag_start", f0);
        int n = 54;
        for (int i = 1; i <= n; i++)
        {
            float e = Mathf.SmoothStep(0f, 1f, i / (float)n);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Lerp(from, target, e));
            CpTap("drag", Vector2.Lerp(f0, f1, e));
            yield return null;
        }
        CpTap("drag_end", f1);
    }

    static string CpKey(string id, int lv = -1)
    {
        var st = CardInventory.Stacks.Where(s => s.count > 0 && (s.cardId == id || CardVariant.Parse(s.cardId)?.mainId == id) && (lv < 0 || s.level == lv))
            .OrderByDescending(s => s.level).FirstOrDefault();
        return st != null ? st.cardId : null;
    }
    static string[] CpKeys(IEnumerable<string> ids) => ids.Select(i => CpKey(i)).Where(k => k != null).ToArray();

    IEnumerator CpFuse(string name, int mode)
    {
        yield return CpHomeReady();
        cpShot = name;
        // 素材を毎回そろえ直す(撮り直しても同じ所から)
        if (mode == 0 && (CpKey(CpSame, 3) == null || CpKey(CpSame, 2) == null)) { CardInventory.AddCard(CpSame, 3, 1); CardInventory.AddCard(CpSame, 2, 1); }
        string main, mat;
        if (mode == 0) { main = CpSame; mat = CpSame; }
        else if (mode == 4) { main = CpMastery; mat = CpMastery; }
        else { main = CpCrossA; mat = CpCrossB; }
        int mainLv = mode == 0 ? 3 : mode == 4 ? 9 : 2, matLv = mode == 0 ? 2 : mode == 4 ? 3 : 2;
        main = CpKey(main, mainLv); mat = CpKey(mat, matLv);
        L($"[cp] fuse {name}: main {main} / material {mat}");
        if (main == null || mat == null) yield break;
        var ui = gm.cardFusionUI;
        ScRecStart(name);
        yield return ScWait(0.8f);
        CpTap("home_fusion", Vector2.zero);
        gm.OpenCardFusion();
        yield return ScWait(1.6f);
        ScStill("open");
        // メイン → 素材(一覧のタップと同じ処理)
        ui.DebugSetActiveSlot(0);
        Vector2 p = CpFusionCell(ui, main);
        yield return ScWait(0.4f);
        CpTap("tap", p);
        ui.DebugTapCell(main);
        yield return ScWait(1.1f);
        p = CpFusionCell(ui, mat);
        yield return ScWait(0.3f);
        CpTap("tap", p);
        ui.DebugTapCell(mat);
        // 説明欄を指でスクロールして、成功率/継承率の行を見せる(同名は【同名強化】、異名は【異名合成】の行を欄の上へ)
        yield return ScWait(0.9f);
        // 縦は欄が低いので、継承率の行(メイン 50% / 素材 25%)を欄の上へ。横は【異名合成】の見出しから継承率まで入る
        yield return CpScrollDetail(ui, mode == 0 || mode == 4 ? "【同名強化】" : Arg("-cpCrossMarker", PortraitRunView.IsPortraitScreen ? "継承する確率" : "【異名合成】"));
        yield return ScWait(1.8f);
        ScStill("preview");
        L($"[cp] fuse {name}: detail = {ui.DebugDetailText.Replace("\n", " | ")}");
        // 合成する
        CpTap("tap", CpCenter(CpFuseBtn.GetValue(ui) as RectTransform));
        if (mode == 1) CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess;
        else if (mode == 2) CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothFail;
        else if (mode == 3) CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.MainOnly;
        ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        float w = 0f;
        while (!ui.IsShowingResult && w < 12f) { yield return ScWait(1f / 60f); w += 1f / 60f; }
        L($"[cp] fuse {name}: presentation {w:F2}s, result = {ui.DebugResultText.Replace("\n", " | ")}");
        yield return ScWait(0.5f);
        ScStill("result");
        yield return ScWait(2.5f);
        yield return ScRecStop();
        ui.DebugBackToList();
        ui.Close();
        float wc = 0f;
        while (((ui.root != null && ui.root.activeInHierarchy) || (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning)) && wc < 5f) { yield return null; wc += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        // 次の撮影のために素材を戻す
        if (mode == 0) { CardInventory.AddCard(CpSame, 3, 1); CardInventory.AddCard(CpSame, 2, 1); }
        else if (mode == 4) CardInventory.AddCard(CpMastery, 3, 1);
        else { if (CpKey(CpCrossA, 2) == null) CardInventory.AddCard(CpCrossA, 2, 2); if (CpKey(CpCrossB, 2) == null) CardInventory.AddCard(CpCrossB, 2, 2); }
        foreach (var s in CardInventory.Stacks.ToList()) CardInventory.ClearNewUnconfirmed(s.cardId);
    }

    // ---------------------------------------------------------------- ラン中の3択 → その能力で戦う
    IEnumerator CpChoice(string name, string ch)
    {
        yield return CpHomeReady();
        cpShot = name;
        if (DeckPresets.Active != 0) gm.ActivatePreset(0);
        gm.SetDeck(CpKeys(CpDeckA.Concat(new[] { CpAddCard })));
        yield return ScBegin(ch, "wasteland_road", float.Parse(Arg("-cpChoiceAt", "2600"), System.Globalization.CultureInfo.InvariantCulture), 3);
        float zone = ScFlatStage(200f, 700f); // 穴/坂の無い一本道(取った能力で戦う所を見せる)
        yield return ScWaitReach(zone + 10f);
        TerrainManager.Instance?.ClearAllEnemies();
        autoPickHold = true;
        ScRecStart(name);
        yield return ScWait(1.2f);
        typeof(GameManager).GetMethod("GainExp", NP).Invoke(gm, new object[] { gm.ExpToNext + 1f });
        float w = 0f;
        while (!gm.IsRewardSequenceWaitingForSelection && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return ScWait(1.2f);
        ScStill("choice");
        var seq = FindFirstObjectByType<RewardCardSequence>();
        int pick = CpPick(seq);
        Vector2 cp = seq != null && seq.QaCards != null && pick < seq.QaCards.Length && seq.QaCards[pick] != null ? CpCenter(seq.QaCards[pick].rect) : Vector2.zero;
        L($"[cp] choice: pick #{pick}");
        yield return ScWait(0.6f);
        CpTap("tap", cp);
        if (seq != null) seq.OnCardClicked(pick);
        yield return ScWait(0.7f);
        ScStill("picked");
        yield return ScWait(0.5f);
        CpTap("tap", cp);
        if (seq != null) seq.OnCardClicked(pick);
        // 取った能力で戦う(前に敵を並べる)
        yield return ScWait(0.5f);
        for (int wave = 0; wave < 3; wave++)
        {
            float ahead = Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 8f, 18f);
            for (int i = 0; i < 5; i++) HaSpawn(ahead + i * 1.4f, i % 3 == 0 ? "goblin_elite" : "goblin");
            for (int i = 0; i < 4; i++) { yield return ScWait(0.5f); ScStill($"fight{wave}_{i}"); }
        }
        yield return ScWait(1f);
        yield return ScRecStop();
        autoPickHold = false;
        yield return ScEnd();
    }

    static readonly string[] CpPrefer = { "chain_lightning", "thunder_strike", "inferno", "thunder_lord", "flame_blade", "double_attack", "phoenix" };
    int CpPick(RewardCardSequence seq)
    {
        if (seq == null || !(ScCurrentCards?.GetValue(seq) is RewardCardData[] cards) || cards.Length == 0) return 0;
        foreach (var want in CpPrefer)
            for (int i = 0; i < cards.Length; i++)
                if ((cards[i].CardId == want || CardVariant.Parse(cards[i].CardId)?.mainId == want)) return i;
        return 0;
    }

    // ---------------------------------------------------------------- 締めの戦闘(組んだデッキの能力で敵の群れを倒す)
    IEnumerator CpBattle(string name, string ch)
    {
        yield return CpHomeReady();
        cpShot = name;
        if (DeckPresets.Active != 0) gm.ActivatePreset(0);
        gm.SetDeck(CpKeys(CpDeckA.Concat(new[] { CpAddCard })));
        yield return ScBegin(ch, "wasteland_road", float.Parse(Arg("-cpBattleAt", "2500"), System.Globalization.CultureInfo.InvariantCulture), int.Parse(Arg("-cpBattleLv", "9")));
        // 組んだデッキ(炎と雷 + PHOENIX)の能力を、育てた後の強さで持たせる(ラン中の3択で取っていくのと同じ効果の積み上げ)
        int blv = int.Parse(Arg("-cpBattleBuildLv", "5"));
        foreach (var id in CpDeckA.Concat(new[] { CpSwapIn }))
        {
            var cd = CardDatabase.FindBaseById(id);
            if (cd != null && id != CpSwapOut) gm.ApplyCardEffectsStacked(cd, blv);
        }
        float zone = ScFlatStage(220f, 700f);
        yield return ScWaitReach(zone + 10f);
        TerrainManager.Instance?.ClearAllEnemies();
        ScRecStart(name);
        int waves = int.Parse(Arg("-cpBattleWaves", "6"));
        for (int wave = 0; wave < waves; wave++)
        {
            float ahead = Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 8f, 18f);
            for (int i = 0; i < 6; i++)
            {
                var e = HaSpawn(ahead + i * 1.2f, i % 3 == 0 ? "goblin_elite" : "goblin");
                if (e != null) { AeHp.SetValue(e, -1); HaEnsureHp.Invoke(e, null); }
            }
            for (int i = 0; i < 3; i++) { yield return ScWait(0.5f); ScStill($"w{wave}_{i}"); }
        }
        yield return ScWait(1.0f);
        yield return ScRecStop();
        yield return ScEnd();
    }
}
#endif

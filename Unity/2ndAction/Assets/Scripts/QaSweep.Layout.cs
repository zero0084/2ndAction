#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 全画面のレイアウト点検(2026-10-28 夜、マスターの依頼)。-qaPortrait の巡回(ホーム〜リザルト)に、
//  ・撮影のたびの自動の範囲チェック(uGUI): 文字/ボタンが画面・安全領域・親の枠からはみ出していないか、文字が切れていないか、文字同士が重なっていないか
//  ・まだ撮っていなかった画面(お知らせ/キャラカード/確認の画面/合成の結果 4種/疾走・CPU の行き先/デバッグ/データ削除/ライセンス/闘技場と退出/帰還確認)
//  ・開いたまま回す(合成の結果/確認の画面/設定)
// を足す。IMGUI の画面(設定/ランキング/デバッグ/闘技場/HUD など)は自動では測れないので、撮った画面を目で確かめる。
//  画面の大きさは起動の引数(-screen-width/-screen-height)、切り欠きは -fakeSafeLeft/Right/Top/Bottom、長い訳は -qaPortraitLang、
//  多い/最大の状態は -qaLayoutMax 1(MILE を最大桁・所持カードを多く)で変える
public partial class QaSweep
{
    readonly StringBuilder layoutTsv = new StringBuilder("shot\tkind\tpath\tdetail\n");
    int layoutIssues;

    static string LPath(Transform t) { var parts = new List<string>(); for (int i = 0; t != null && i < 6; i++, t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }

    static Rect ScreenRectOf(RectTransform rt)
    {
        var c = rt.GetComponentInParent<Canvas>();
        Camera cam = c != null && c.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? c.rootCanvas.worldCamera : null;
        var w = new Vector3[4]; rt.GetWorldCorners(w);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    static bool LVisible(Graphic g)
    {
        if (g == null || !g.isActiveAndEnabled || g.canvasRenderer.cull) return false;
        if (g.color.a < 0.05f) return false;
        if (g is Text tx && string.IsNullOrWhiteSpace(tx.text)) return false;
        for (var t = g.transform; t != null; t = t.parent)
        {
            var cg = t.GetComponent<CanvasGroup>(); if (cg != null && cg.alpha < 0.05f) return false;
            var m = t.GetComponent<RectMask2D>(); if (m != null && m.transform != g.transform) { /* マスクの中は見える所だけ: 下で切る */ }
        }
        return true;
    }

    // 見える範囲(マスクで切った後)。マスクの外に完全に出ていれば見えない(スクロールの外 = はみ出しではない)
    static bool LClipped(RectTransform rt, ref Rect r)
    {
        for (var t = rt.parent; t != null; t = t.parent)
        {
            if (t.GetComponent<RectMask2D>() == null && t.GetComponent<Mask>() == null) continue;
            var mr = ScreenRectOf(t as RectTransform);
            if (!r.Overlaps(mr)) return true;
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, mr.xMin), Mathf.Max(r.yMin, mr.yMin), Mathf.Min(r.xMax, mr.xMax), Mathf.Min(r.yMax, mr.yMax));
        }
        return false;
    }

    void LNote(string shot, string kind, Transform t, string detail)
    {
        layoutIssues++;
        layoutTsv.Append($"{shot}\t{kind}\t{LPath(t)}\t{detail}\n");
        L($"[layout] {shot} {kind} {LPath(t)} {detail}");
    }

    // 1枚の撮影の時点の uGUI を調べる
    void LayoutAudit(string shot)
    {
        Rect scr = new Rect(0, 0, Screen.width, Screen.height), safe = StableSafeArea.Rect;
        const float tol = 3f;
        var texts = new List<(Text t, Rect r)>();
        int n0 = layoutIssues;
        // IMGUI の画面(設定/一時停止/帰還の確認/ランキング/デバッグ/闘技場など)は uGUI の上に描かれる: その間の uGUI は隠れているので測らない(撮った画面を目で見る)
        bool imguiOnTop = UiInputGate.ModalOpen || (gm != null && ((bool)(typeof(GameManager).GetField("showPauseMenu", NP)?.GetValue(gm) ?? false))) || ArenaMode.Active;
        if (imguiOnTop) { L($"[layout] {shot}: IMGUI on top - uGUI not measured"); return; }
        var all = FindObjectsByType<Graphic>(FindObjectsSortMode.None).Where(g => LVisible(g) && !LUnder(g.transform, "CardFaceRenderer")).ToList();
        // 手前の画面いっぱいの幕/背景(半分以上不透明)より後ろにある物は見えない
        var blockers = all.Where(g => g is Image && g.color.a >= 0.5f && LCovers(ScreenRectOf(g.rectTransform), scr)).ToList();
        foreach (var g in all)
        {
            if (blockers.Any(b => b != g && LBehind(g, b))) continue;
            var rt = g.rectTransform;
            Rect r = g is Text tg0 ? LGlyphRect(tg0) : ScreenRectOf(rt);
            if (r.width < 2f || r.height < 2f) continue;
            if (LClipped(rt, ref r)) continue;
            bool big = r.width >= scr.width * 0.9f && r.height >= scr.height * 0.9f; // 背景/暗幕(画面いっぱいが正しい)
            if (big) continue;
            bool isText = g is Text;
            bool interactive = g.GetComponentInParent<Selectable>() != null || g.transform.name.IndexOf("Button", System.StringComparison.OrdinalIgnoreCase) >= 0
                || (g.transform.parent != null && g.transform.parent.name.IndexOf("Button", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isText && !interactive) continue;
            // 画面の外
            if (r.xMin < scr.xMin - tol || r.xMax > scr.xMax + tol || r.yMin < scr.yMin - tol || r.yMax > scr.yMax + tol)
                LNote(shot, "offscreen", rt, $"rect=({r.xMin:0},{r.yMin:0})-({r.xMax:0},{r.yMax:0}) screen={scr.width:0}x{scr.height:0}");
            // 安全領域の外(切り欠きに文字/ボタンがかかる)
            else if (r.xMin < safe.xMin - tol || r.xMax > safe.xMax + tol || r.yMin < safe.yMin - tol || r.yMax > safe.yMax + tol)
                LNote(shot, "unsafe", rt, $"rect=({r.xMin:0},{r.yMin:0})-({r.xMax:0},{r.yMax:0}) safe=({safe.xMin:0},{safe.yMin:0})-({safe.xMax:0},{safe.yMax:0})");
            // 親の枠(Image を持つ一番近い親で、画面いっぱいではないもの)の外
            for (var p = rt.parent; p != null; p = p.parent)
            {
                var img = p.GetComponent<Image>();
                if (img == null || !img.isActiveAndEnabled || img.color.a < 0.05f) continue;
                Rect pr = ScreenRectOf(p as RectTransform);
                if (pr.width >= scr.width * 0.98f && pr.height >= scr.height * 0.98f) break;
                if (r.xMin < pr.xMin - 6f || r.xMax > pr.xMax + 6f || r.yMin < pr.yMin - 6f || r.yMax > pr.yMax + 6f)
                    LNote(shot, "outside-frame", rt, $"rect=({r.xMin:0},{r.yMin:0})-({r.xMax:0},{r.yMax:0}) frame {LPath(p)}=({pr.xMin:0},{pr.yMin:0})-({pr.xMax:0},{pr.yMax:0})");
                break;
            }
            if (isText)
            {
                var t = (Text)g;
                texts.Add((t, r));
                // 文字が切れている(折り返しの後、枠の高さに入らない)
                if (t.verticalOverflow == VerticalWrapMode.Truncate)
                {
                    var gen = t.cachedTextGenerator;
                    int vis = gen != null ? gen.characterCountVisible : -1;
                    string plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");
                    if (vis >= 0 && vis < plain.Replace("\n", "").Length - 1 && !t.resizeTextForBestFit)
                        LNote(shot, "text-cut", rt, $"visible {vis}/{plain.Length} \"{Short(plain)}\"");
                    else if (t.resizeTextForBestFit && gen != null && gen.fontSizeUsedForBestFit <= t.resizeTextMinSize && vis >= 0 && vis < plain.Replace("\n", "").Length - 1)
                        LNote(shot, "text-cut", rt, $"best fit at min {t.resizeTextMinSize}, visible {vis}/{plain.Length} \"{Short(plain)}\"");
                }
                if (t.horizontalOverflow == HorizontalWrapMode.Overflow && t.preferredWidth > rt.rect.width + 8f && t.alignment != TextAnchor.MiddleCenter)
                    LNote(shot, "text-spill", rt, $"preferred {t.preferredWidth:0} > {rt.rect.width:0} \"{Short(t.text)}\"");
            }
        }
        // 文字同士の重なり(同じ見出しの影など、同じ位置・同じ文は除く)
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                var a = texts[i]; var b = texts[j];
                if (a.t.text == b.t.text || a.t.transform.IsChildOf(b.t.transform) || b.t.transform.IsChildOf(a.t.transform)) continue;
                if (!a.r.Overlaps(b.r)) continue;
                Rect x = Rect.MinMaxRect(Mathf.Max(a.r.xMin, b.r.xMin), Mathf.Max(a.r.yMin, b.r.yMin), Mathf.Min(a.r.xMax, b.r.xMax), Mathf.Min(a.r.yMax, b.r.yMax));
                float small = Mathf.Min(a.r.width * a.r.height, b.r.width * b.r.height);
                // 文字の実際の幅で測る(枠は広くても文字は短いことが多い)
                float aw = Mathf.Min(a.r.width, a.t.preferredWidth * a.t.transform.lossyScale.x), bw = Mathf.Min(b.r.width, b.t.preferredWidth * b.t.transform.lossyScale.x);
                if (x.width * x.height > small * 0.3f && x.width > Mathf.Min(aw, bw) * 0.3f)
                    LNote(shot, "text-overlap", a.t.transform, $"with {LPath(b.t.transform)} \"{Short(a.t.text)}\" / \"{Short(b.t.text)}\"");
            }
        L($"[layout] {shot}: {layoutIssues - n0} issue(s), {texts.Count} texts");
    }

    static bool LUnder(Transform t, string name) { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }
    static bool LCovers(Rect r, Rect scr) => r.width >= scr.width * 0.9f && r.height >= scr.height * 0.9f;
    // g が b より後ろ(先に描かれる)か: 別の根の Canvas なら並び順、同じなら描く順
    static bool LBehind(Graphic g, Graphic b)
    {
        var cg = g.canvas != null ? g.canvas.rootCanvas : null; var cb = b.canvas != null ? b.canvas.rootCanvas : null;
        if (cg == null || cb == null) return false;
        if (cg != cb) return cb.sortingOrder > cg.sortingOrder;
        return b.canvasRenderer.absoluteDepth > g.canvasRenderer.absoluteDepth && !g.transform.IsChildOf(b.transform);
    }
    // 文字が実際に入っている範囲(枠は広くても、文字が短ければその分だけ)
    static Rect LGlyphRect(Text t)
    {
        Rect r = ScreenRectOf(t.rectTransform);
        float sx = r.width / Mathf.Max(1f, t.rectTransform.rect.width), sy = r.height / Mathf.Max(1f, t.rectTransform.rect.height);
        float w = Mathf.Min(r.width, t.preferredWidth * sx), h = Mathf.Min(r.height, t.preferredHeight * sy);
        if (t.horizontalOverflow == HorizontalWrapMode.Overflow) w = t.preferredWidth * sx;
        if (t.verticalOverflow == VerticalWrapMode.Overflow) h = t.preferredHeight * sy;
        int ax = (int)t.alignment % 3, ay = (int)t.alignment / 3; // 0=左/上 1=中 2=右/下
        float x = ax == 0 ? r.xMin : ax == 1 ? r.center.x - w * 0.5f : r.xMax - w;
        float y = ay == 0 ? r.yMax - h : ay == 1 ? r.center.y - h * 0.5f : r.yMin;
        return new Rect(x, y, w, h);
    }

    static string Short(string s) { s = (s ?? "").Replace("\n", " "); return s.Length > 28 ? s.Substring(0, 28) + "…" : s; }

    void LayoutFlush()
    {
        if (outDir == null) return;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "layout_issues.tsv"), layoutTsv.ToString());
        L($"[layout] total issues {layoutIssues} (layout_issues.tsv)");
    }

    IEnumerator LRot(bool land)
    {
        int lw = Mathf.Max(Screen.width, Screen.height), lh = Mathf.Min(Screen.width, Screen.height);
        Screen.SetResolution(land ? lw : lh, land ? lh : lw, false);
        float w = 0f; while ((Screen.width > Screen.height) != land && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
    }

    IEnumerator WaitNoTransition(float max = 6f)
    {
        float w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < max) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // まだ撮っていなかった画面(撮影はフレームの終わりなので、S の後は必ず1フレーム待ってから閉じる/次の操作をする)
    IEnumerator LayoutExtraScreens(System.Action<string> S)
    {
        L("== layout: extra screens ==");
        yield return ReloadHome();
        bool land = Screen.width > Screen.height;
        string longTxt = Loc.T("読み込み中…") + " — The quick brown fox jumps over the lazy dog. 長い文の折り返しの確認です。もう一行ぶんの長さの説明文を入れて、最後まで読めるか、下のボタンに届くかを確かめます。";

        // お知らせ(長い題と本文)
        NoticeQueue.QaAutoAcknowledge = false;
        NoticeQueue.Enqueue("qa_layout_notice", "長いお知らせの題名 — Long notice title for layout", longTxt + "\n\n" + longTxt, null);
        yield return WaitTut(() => NoticeQueue.Open, 4f); yield return new WaitForSecondsRealtime(0.6f);
        S("notice"); yield return null;
        NoticeQueue.ClearAll(); yield return new WaitForSecondsRealtime(0.4f);

        // キャラカード(キャラ選択 → カード設定)
        gm.OpenCharacterCardsFromSelect("swordsman"); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
        S("char_cards"); yield return null;
        var deck = FindFirstObjectByType<DeckEditUI>();
        // 確認の画面(長い文)
        if (deck != null && deck.confirmDialog != null)
        {
            deck.confirmDialog.Show(longTxt, () => { }, () => { });
            yield return new WaitForSecondsRealtime(0.6f); S("confirm_dialog"); yield return null;
            yield return LRot(!land); S("confirm_dialog_rotated"); yield return null; yield return LRot(land);
            Check(deck.confirmDialog.IsOpen, "layout: the confirm dialog stays open after rotation");
            deck.confirmDialog.Cancel(); yield return new WaitForSecondsRealtime(0.4f);
        }
        deck?.Close(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();

        // 合成の結果: 同名強化 / 異名(両方成功)/ 失敗 / MASTERY
        var ids = CardDatabase.UnlockedCards.Select(c => c.cardId).ToList();
        string A = ids.FirstOrDefault(i => i == "attack_up") ?? ids[0], Cc = ids.FirstOrDefault(i => i != A && CardDatabase.FindBaseById(i) != null && CardDatabase.FindBaseById(i).cardName.Length >= 10) ?? ids[1];
        CardInventory.AddCard(A, 1, 6); CardInventory.AddCard(Cc, 1, 4); CardInventory.AddCard(A, 9, 2);
        gm.OpenCardFusion(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
        var fu = FindFirstObjectByType<CardFusionUI>();
        if (fu != null)
        {
            string lv9 = CardInventory.Stacks.Where(s => CardVariant.Parse(s.cardId) != null && CardVariant.Parse(s.cardId).level >= 9 && s.count >= 2).Select(s => s.cardId).FirstOrDefault();
            var cases = new List<(string name, string main, string mat, CardFusionLogic.ForcedOutcome? force)>
            {
                ("same", A, A, null),
                ("cross", A, Cc, CardFusionLogic.ForcedOutcome.BothSuccess),
                ("fail", A, Cc, CardFusionLogic.ForcedOutcome.BothFail),
            };
            if (lv9 != null) cases.Add(("mastery", lv9, lv9, null));
            foreach (var c in cases)
            {
                fu.DebugSelect(c.main, c.mat);
                yield return new WaitForSecondsRealtime(0.3f);
                CardFusionLogic.DebugForcedOutcome = c.force;
                fu.DebugFuse();
                CardFusionLogic.DebugForcedOutcome = null;
                yield return new WaitForSecondsRealtime(0.4f);
                fu.DebugSkip();
                yield return WaitTut(() => fu.DebugInResult, 8f);
                yield return new WaitForSecondsRealtime(0.6f);
                S($"fusion_result_{c.name}"); yield return null;
                CheckFusionButtons($"fusion_result_{c.name}", fu);
                if (c.name == "same")
                {
                    yield return LRot(!land); S("fusion_result_same_rotated"); yield return null; CheckFusionButtons("fusion_result_same_rotated", fu);
                    yield return LRot(land); S("fusion_result_same_back"); yield return null; CheckFusionButtons("fusion_result_same_back", fu);
                }
                fu.DebugBackToList(); yield return new WaitForSecondsRealtime(0.6f);
            }
            fu.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(1.3f);
        }
        yield return ReloadHome();

        // 疾走の行き先 / CPU サポートの仲間選び(どちらもステージ選択の上に開く)
        gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
        SprintDeparturePanel.DebugOpen(true); yield return new WaitForSecondsRealtime(0.6f); S("sprint_panel"); yield return null; SprintDeparturePanel.DebugOpen(false); yield return new WaitForSecondsRealtime(0.4f);
        SupportDeparturePanel.DebugOpen(true); yield return new WaitForSecondsRealtime(0.6f); S("support_panel"); yield return null; SupportDeparturePanel.DebugOpen(false); yield return new WaitForSecondsRealtime(0.4f);
        FindFirstObjectByType<StageSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);

        // 設定 → データ削除 / ライセンスの最初と最後 / 設定を開いたまま回す
        SettingsPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.6f);
        yield return LRot(!land); S("settings_rotated"); yield return null; yield return LRot(land);
        Check(SettingsPanel.IsVisible, "layout: settings stays open after rotation");
        DataDeletionUi.Open(); yield return new WaitForSecondsRealtime(0.6f); S("data_delete"); yield return null; yield return WaitTut(() => !AccountDeletion.Busy, 8f); DataDeletionUi.Close(); // 確認の通信の間は閉じない作り(待ってから閉じる) yield return new WaitForSecondsRealtime(0.3f);
        LicensesUi.Open(); yield return new WaitForSecondsRealtime(0.6f); S("licenses_top"); yield return null;
        { var lu = FindFirstObjectByType<LicensesUi>(); var sc = lu != null ? typeof(LicensesUi).GetField("scroll", NP)?.GetValue(lu) as UiScroll : null; if (sc != null) { sc.y = 1e6f; yield return null; yield return new WaitForSecondsRealtime(0.4f); S("licenses_end"); yield return null; } }
        LicensesUi.Close(); yield return new WaitForSecondsRealtime(0.3f);
        SettingsPanel.Instance?.Close(); yield return new WaitForSecondsRealtime(0.5f);

        // デバッグ画面(最初と最後)
        DebugPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.6f); S("debug_top"); yield return null;
        { var sc = typeof(DebugPanel).GetField("scroll", NP).GetValue(DebugPanel.Instance) as UiScroll; if (sc != null) { sc.y = 1e6f; yield return null; yield return new WaitForSecondsRealtime(0.4f); S("debug_bottom"); yield return null; } }
        DebugPanel.CloseStatic(); yield return new WaitForSecondsRealtime(0.5f);

        // ラン中: 一時停止 → 帰還の確認
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(2f);
        var spm = typeof(GameManager).GetField("showPauseMenu", NP); var srh = typeof(GameManager).GetField("showReturnHomeConfirm", NP);
        object pauseOwner = typeof(GameManager).GetField("pauseMenuTimeOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        spm.SetValue(gm, true); TimeControl.Pause(pauseOwner); yield return new WaitForSecondsRealtime(0.5f);
        srh.SetValue(gm, true); yield return new WaitForSecondsRealtime(0.5f); S("return_confirm"); yield return null;
        yield return LRot(!land); S("return_confirm_rotated"); yield return null; yield return LRot(land);
        srh.SetValue(gm, false); spm.SetValue(gm, false); TimeControl.ResetAll();
        yield return EndRun();

        // 闘技場: 準備の画面(タブごと) → ホームへ(押してからホームが出るまで、毎フレーム覆われているか)
        yield return ReloadHome();
        ArenaLauncher.Launch("qa layout", true);
        yield return WaitTut(() => ArenaMode.Active && !ArenaLauncher.Covering && ArenaController.Instance != null && ArenaController.Instance.PanelOpen, 30f);
        yield return new WaitForSecondsRealtime(0.8f);
        var ac = ArenaController.Instance;
        if (ac != null)
        {
            for (int tb = 0; tb < 5; tb++) { ac.Tab = tb; yield return new WaitForSecondsRealtime(0.4f); S($"arena_tab{tb}"); yield return null; }
            ac.Tab = 0; yield return new WaitForSecondsRealtime(0.3f);
            int exposed = 0, frames = 0, shots = 0; bool modal = true;
            ac.ExitHome();
            float t0 = Time.realtimeSinceStartup;
            bool homeReady = false;
            while (Time.realtimeSinceStartup - t0 < 20f)
            {
                yield return null; frames++;
                bool covered = ArenaLauncher.Covering || LoadingScreen.Visible;
                homeReady = GameManager.Instance != null && !GameManager.Instance.HasStarted && !LoadingScreen.Busy && !ArenaMode.Active;
                if (homeReady && !ArenaLauncher.Covering) break;
                if (!covered) exposed++;
                if (ArenaLauncher.Covering && !UiInputGate.ModalOpen) modal = false;
                if (frames == 1 || frames == 6 || frames == 20) { Shot($"{Screen.width}x{Screen.height}{layoutTagSuffix}_arena_exit_f{frames}"); shots++; }
            }
            L($"[layout] arena exit: {frames} frames until home, uncovered {exposed}, input blocked while covered={modal}, home ready={homeReady}");
            Check(exposed == 0, $"layout: arena -> home never shows the arena screen while switching (uncovered frames {exposed})");
            Check(modal, "layout: taps do not reach the screen behind the cover while leaving the arena");
            Check(homeReady, "layout: home is shown after leaving the arena");
            yield return new WaitForSecondsRealtime(1f); S("arena_exit_home"); yield return null;
        }
        else Check(false, "layout: arena setup opened");
        yield return ReloadHome();
    }

    string layoutTagSuffix = "";

    void CheckFusionButtons(string shot, CardFusionUI fu)
    {
        var panel = fu.DebugResultPanel; if (panel == null) return;
        Rect pr = ScreenRectOf(panel), scr = new Rect(0, 0, Screen.width, Screen.height);
        foreach (var b in new[] { fu.DebugContinueButton, fu.DebugBackButton })
        {
            if (b == null) continue;
            Rect r = ScreenRectOf(b);
            bool inside = r.xMin >= pr.xMin - 1f && r.xMax <= pr.xMax + 1f && r.yMin >= pr.yMin - 1f && r.yMax <= pr.yMax + 1f && r.xMin >= 0f && r.xMax <= scr.width;
            Check(inside, $"layout {shot}: '{b.name}' inside the result panel (button ({r.xMin:0},{r.yMin:0})-({r.xMax:0},{r.yMax:0}) panel ({pr.xMin:0},{pr.yMin:0})-({pr.xMax:0},{pr.yMax:0}))");
            var lbl = b.GetComponentInChildren<Text>();
            if (lbl != null)
            {
                float sf = lbl.canvas != null ? Mathf.Max(0.01f, lbl.canvas.scaleFactor) : 1f;
                float used = lbl.resizeTextForBestFit ? lbl.cachedTextGenerator.fontSizeUsedForBestFit / sf : lbl.fontSize; // 画面の画素ではなく、画面の部品の大きさで比べる
                Check(used >= 20f, $"layout {shot}: '{b.name}' label readable (font {used:0} of {lbl.resizeTextMaxSize})");
            }
        }
        if (fu.DebugContinueButton != null && fu.DebugBackButton != null)
            Check(!ScreenRectOf(fu.DebugContinueButton).Overlaps(ScreenRectOf(fu.DebugBackButton)), $"layout {shot}: the two buttons do not overlap");
    }
}
#endif

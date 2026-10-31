using UnityEngine;
using UnityEngine.UI;

// デッキ編集の配置 V2(2026-10-31、操作感の仕上げ)。
//  上段: キャラカード(3枚を1行) + デッキ12枠(6列×2段) + その横に「デッキ n/12・種類の内訳・おすすめ編成・全て外す」
//  下段: 所持カードの一覧(列を増やして一度に多く見せる) + 小さな詳細(畳める。全部の情報は長押しの大きな詳細で)
//  縦画面: 戻る/デッキ切り替えの帯 → キャラカード | 枚数・内訳・ボタン → デッキ 6×2 → 小さな詳細(畳める) → 絞り込み → 一覧
//  横画面: 上段の左にキャラカード、中央にデッキ、右に枚数・内訳・ボタン / 下段の左に一覧、右に小さな詳細
//  部品はシーンの物をそのまま使い(当たり判定・既存の試験の参照はそのまま)、位置と大きさだけを決める。
//  カードの文字の大きさはカードの幅で作ってあるので、並びの拡大縮小は一覧の中身(content)の localScale で行う(以前のデッキ欄と同じ)
public partial class DeckEditUI
{
    RectTransform panelCollection, headerCollectionRt, panelDeck, headerDeckRt, detailPanelRt;
    readonly RectTransform[] charSlotBackRts = new RectTransform[GameManager.CharacterCardSlotCount];
    readonly RectTransform[] charSlotHolderRts = new RectTransform[GameManager.CharacterCardSlotCount];
    RectTransform catRow;
    readonly Image[] catIcons = new Image[7];
    readonly Text[] catCounts = new Text[7];
    RectTransform toastRt; Text toastText; float toastUntil;
    RectTransform detailToggleRt; Image detailToggleIcon;
    static bool detailCollapsed;
    bool layoutBuilt, layoutApplied, layoutPortrait;
    Vector2 layoutSize;
    public bool CompactDetail => true; // 小さな詳細(カードの絵は出さない。大きな詳細は長押し/「詳細」)。配置 V2 は常に使う
    public static int LayoutApplies { get; private set; } // 確認用

    bool LayoutNeedsApply
    {
        get
        {
            if (root == null) return false;
            var r = (RectTransform)root.transform;
            return !layoutApplied || r.rect.size != layoutSize || layoutPortrait != PortraitRunView.IsPortraitScreen;
        }
    }

    static void PlaceTop(RectTransform rt, float cx, float top, float w, float h)
    {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(cx, top);
    }

    // 親(パネル)の左上からの位置
    static void PlaceIn(RectTransform rt, float x, float top, float w, float h)
    {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, top);
    }

    void BuildLayoutParts()
    {
        if (layoutBuilt || root == null) return;
        layoutBuilt = true;
        var rootRt = (RectTransform)root.transform;
        panelCollection = rootRt.Find("Panel_COLLECTION") as RectTransform;
        headerCollectionRt = rootRt.Find("Header_COLLECTION") as RectTransform;
        panelDeck = rootRt.Find("Panel_DECK") as RectTransform;
        headerDeckRt = deckHeader != null ? deckHeader.rectTransform : rootRt.Find("Header_DECK") as RectTransform;
        detailPanelRt = detailName != null ? detailName.rectTransform.parent as RectTransform : rootRt.Find("DetailPanel") as RectTransform;
        if (headerCollectionRt != null) headerCollectionRt.gameObject.SetActive(false); // 見出しの言葉は枚数の行(collectionCountText)の頭にある
        if (emptyState != null) emptyState.SetActive(false);                           // 種類の内訳はデッキの横へ移した
        Font font = countText != null ? countText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // デッキの種類の内訳(7種類のアイコン × 枚数)
        catRow = NewUi("DeckCategoryRow", rootRt, new Vector2(0.5f, 1f), new Vector2(340f, 100f), Vector2.zero);
        for (int i = 0; i < 7; i++)
        {
            var cell = NewUi("Cat" + i, catRow, new Vector2(0f, 1f), new Vector2(84f, 44f), Vector2.zero);
            var ic = NewUi("Icon", cell, new Vector2(0f, 0.5f), new Vector2(38f, 38f), Vector2.zero);
            ic.pivot = new Vector2(0f, 0.5f); ic.anchoredPosition = new Vector2(2f, 0f);
            catIcons[i] = ic.gameObject.AddComponent<Image>();
            catIcons[i].sprite = CardCategoryIcons.GetIcon((CardCategory)i); catIcons[i].preserveAspect = true; catIcons[i].raycastTarget = false;
            var tr = NewUi("Count", cell, new Vector2(0f, 0.5f), new Vector2(44f, 44f), Vector2.zero);
            tr.pivot = new Vector2(0f, 0.5f); tr.anchoredPosition = new Vector2(42f, 0f);
            catCounts[i] = tr.gameObject.AddComponent<Text>();
            catCounts[i].font = font;
            UiTheme.Apply(catCounts[i], UiTheme.TextRole.Number, 1f);
            catCounts[i].alignment = TextAnchor.MiddleLeft; catCounts[i].raycastTarget = false;
            catCounts[i].horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // 短い案内(重複・分解・入れ替えなど)。デッキの欄の下端に重ねて 2 秒ほど
        toastRt = NewUi("DeckToast", rootRt, new Vector2(0.5f, 1f), new Vector2(600f, 56f), Vector2.zero);
        var tbg = toastRt.gameObject.AddComponent<Image>(); tbg.color = new Color(0.03f, 0.04f, 0.09f, 0.92f); tbg.raycastTarget = false;
        var tol = toastRt.gameObject.AddComponent<Outline>(); tol.effectColor = new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.8f); tol.effectDistance = new Vector2(2f, -2f);
        var tt = NewUi("Text", toastRt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        tt.anchorMin = Vector2.zero; tt.anchorMax = Vector2.one; tt.offsetMin = new Vector2(14f, 4f); tt.offsetMax = new Vector2(-14f, -4f);
        toastText = tt.gameObject.AddComponent<Text>();
        toastText.font = font; UiTheme.Apply(toastText, UiTheme.TextRole.Body, 1.05f);
        toastText.color = UiTheme.GoldText; toastText.alignment = TextAnchor.MiddleCenter; toastText.raycastTarget = false;
        toastText.resizeTextForBestFit = true; toastText.resizeTextMinSize = 12; toastText.resizeTextMaxSize = 26;
        toastText.supportRichText = true;
        toastRt.gameObject.SetActive(false);

        // 小さな詳細の「畳む / 開く」
        if (detailPanelRt != null)
        {
            detailToggleRt = NewUi("DetailToggle", detailPanelRt, new Vector2(1f, 1f), new Vector2(52f, 44f), Vector2.zero);
            var bg = detailToggleRt.gameObject.AddComponent<Image>(); bg.color = new Color(0.12f, 0.14f, 0.28f, 0.95f); bg.raycastTarget = false;
            var ol = detailToggleRt.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.85f); ol.effectDistance = new Vector2(1.5f, -1.5f);
            var ic = NewUi("Icon", detailToggleRt, new Vector2(0.5f, 0.5f), new Vector2(30f, 30f), Vector2.zero);
            ic.pivot = new Vector2(0.5f, 0.5f);
            detailToggleIcon = ic.gameObject.AddComponent<Image>();
            detailToggleIcon.sprite = UiIcons.ChevronSprite; detailToggleIcon.preserveAspect = true; detailToggleIcon.raycastTarget = false;
            detailToggleIcon.color = UiIcons.ControlColor;
        }
        BuildDragParts();
    }

    // 種類の内訳の数字
    void RefreshCategoryRow()
    {
        var gm = GameManager.Instance;
        if (catRow == null || gm == null) return;
        var counts = new int[7];
        foreach (var id in gm.DeckCards) { var c = CardDatabase.FindById(id); if (c != null && (int)c.category >= 0 && (int)c.category < 7) counts[(int)c.category]++; }
        for (int i = 0; i < 7; i++)
        {
            if (catCounts[i] != null) { catCounts[i].text = "×" + counts[i]; catCounts[i].color = counts[i] > 0 ? UiTheme.Mile : UiTheme.Disabled; }
            if (catIcons[i] != null) catIcons[i].color = counts[i] > 0 ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.7f);
        }
    }

    public static int ToastsShown { get; private set; }
    public static string LastToast { get; private set; } = ""; // 確認用
    void ShowToast(string msg, float seconds = 2.4f)
    {
        LastToast = msg ?? "";
        ToastsShown++;
        if (toastRt == null || toastText == null) return;
        toastText.text = msg;
        toastRt.gameObject.SetActive(true);
        toastRt.SetAsLastSibling(); // 大きな詳細の上にも出す(確認の窓だけはさらに上)
        if (confirmDialog != null && confirmDialog.root != null) confirmDialog.root.transform.SetAsLastSibling();
        toastUntil = Time.unscaledTime + seconds;
    }
    void UpdateToast()
    {
        if (toastRt != null && toastRt.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastRt.gameObject.SetActive(false);
    }

    // 一覧(GridLayoutGroup)を cols 列にして、見える幅いっぱいになる倍率で縮める。rowsToFit > 0 なら高さにも収める
    static float FitGrid(ScrollRect sr, int cols, int rowsToFit, float maxScale)
    {
        if (sr == null || sr.content == null) return 1f;
        var grid = sr.content.GetComponent<GridLayoutGroup>();
        var vp = sr.viewport != null ? sr.viewport : (RectTransform)sr.transform;
        if (grid == null || vp == null) return 1f;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        grid.childAlignment = TextAnchor.UpperCenter;
        float gw = cols * grid.cellSize.x + (cols - 1) * grid.spacing.x + grid.padding.horizontal;
        float k = vp.rect.width / gw;
        if (rowsToFit > 0)
        {
            float gh = rowsToFit * grid.cellSize.y + (rowsToFit - 1) * grid.spacing.y + grid.padding.vertical;
            k = Mathf.Min(k, vp.rect.height / gh);
        }
        k = Mathf.Clamp(k, 0.2f, maxScale);
        var c = sr.content;
        c.anchorMin = c.anchorMax = new Vector2(0.5f, 1f);
        c.pivot = new Vector2(0.5f, 1f);
        c.sizeDelta = new Vector2(gw, c.sizeDelta.y);
        c.localScale = new Vector3(k, k, 1f);
        LayoutRebuilder.ForceRebuildLayoutImmediate(c);
        return k;
    }

    // 右上の共通の設定ボタン(SettingsPanel.MenuGearRect、IMGUI)が、この画面の上端中央基準の座標でどこにあるか(x = 中央から、y = 上端から下は負)
    Rect GearInRoot()
    {
        var rootRt = (RectTransform)root.transform;
        Rect g = SettingsPanel.MenuGearRect();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, new Vector2(g.xMin, Screen.height - g.yMax), null, out Vector2 a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, new Vector2(g.xMax, Screen.height - g.yMin), null, out Vector2 b);
        float cx = rootRt.rect.center.x, top = rootRt.rect.yMax;
        return Rect.MinMaxRect(a.x - cx, a.y - top, b.x - cx, b.y - top);
    }

    // 目安の幅(画面の単位)から列の数を決める
    static int ColsFor(ScrollRect sr, float targetCardW, int minCols, int maxCols)
    {
        var grid = sr != null && sr.content != null ? sr.content.GetComponent<GridLayoutGroup>() : null;
        var vp = sr != null ? (sr.viewport != null ? sr.viewport : (RectTransform)sr.transform) : null;
        if (grid == null || vp == null) return minCols;
        float k = targetCardW / grid.cellSize.x;
        int cols = Mathf.FloorToInt((vp.rect.width / k + grid.spacing.x) / (grid.cellSize.x + grid.spacing.x));
        return Mathf.Clamp(cols, minCols, maxCols);
    }

    void ApplyLayoutV2()
    {
        if (root == null) return;
        BuildLayoutParts();
        var rootRt = (RectTransform)root.transform;
        layoutApplied = true; layoutSize = rootRt.rect.size; layoutPortrait = PortraitRunView.IsPortraitScreen;
        LayoutApplies++;
        float W = rootRt.rect.width, H = rootRt.rect.height;
        const float m = 18f, gap = 14f;
        float L = -W * 0.5f + m, R = W * 0.5f - m, UW = R - L;
        deckGridFitted = true; // 以前の自動の並べ方(FitDeckGrid)は使わない
        int nChar = Mathf.Min(GameManager.CharacterCardSlotCount, characterSlotCards.Length);

        if (layoutPortrait)
        {
            // 1段目: 戻る(左上)+ デッキの切り替えの帯(右上の設定ボタンの手前まで)
            Rect gear = GearInRoot();
            float barL = L + UiConventions.BackSize.x + 24f, barR = Mathf.Min(R, gear.xMin - 12f);
            PlaceTop(headerDeckRt, (barL + barR) * 0.5f, -22f, barR - barL, 58f);
            // 2段目: キャラカード | 枚数・内訳・ボタン(設定ボタンの下から)
            float top2 = -110f;
            float slotH = Mathf.Clamp(H * 0.085f, 120f, 190f);
            float charW = CharBlockWidth(slotH, nChar);
            float charH = PlaceCharBlock(L + charW * 0.5f, top2, slotH, nChar);
            float sideL = L + charW + gap, sideW = R - sideL;
            float sideTop = gear.xMax > sideL ? Mathf.Min(top2, gear.yMin - 8f) : top2;
            float sideH = PlaceSideBlock(sideL + sideW * 0.5f, sideTop, sideW, charH, true);
            // 3段目: デッキ 6×2
            float deckTop = Mathf.Min(top2 - charH, sideTop - sideH) - gap;
            float deckVpH = Mathf.Min(H * 0.21f, (UW - 24f) / 1190f * 560f);
            PlaceTop(panelDeck, 0f, deckTop, UW, deckVpH + 24f);
            PlaceTop((RectTransform)deckScrollRect.transform, 0f, deckTop - 12f, UW - 24f, deckVpH);
            FitGrid(deckScrollRect, 6, 2, 1f);
            PlaceTop(toastRt, 0f, deckTop - deckVpH * 0.5f + 28f, Mathf.Min(UW - 40f, 760f), 56f);
            // 4段目: 小さな詳細(畳める)
            float detTop = deckTop - deckVpH - 24f - gap;
            float detH = detailCollapsed ? 60f : Mathf.Clamp(H * 0.095f, 150f, 210f);
            PlaceTop(detailPanelRt, 0f, detTop, UW, detH);
            LayoutDetailStrip(UW, detH);
            // 5段目: 所持カード(枚数 / 絞り込み / 一覧)
            float colTop = detTop - detH - gap;
            float colH = -(-H + m) + colTop; // 下端まで
            PlaceTop(panelCollection, 0f, colTop, UW, colH);
            PlaceTop(collectionCountText != null ? collectionCountText.rectTransform : null, 0f, colTop - 8f, UW - 32f, 34f);
            float tabW = Mathf.Min(64f, (UW - 32f - 7 * 6f) / 8f);
            PlaceTabs(0f, colTop - 48f, tabW, 50f);
            float listTop = colTop - 108f;
            PlaceTop((RectTransform)ownedScrollRect.transform, 0f, listTop, UW - 24f, listTop - (-H + m) - 12f);
            FitGrid(ownedScrollRect, ColsFor(ownedScrollRect, 160f, 4, 7), 0, 1f);
        }
        else
        {
            // 上段
            float topH = Mathf.Clamp(H * 0.42f, 360f, 470f);
            float topBottom = -16f - topH;
            float slotH = Mathf.Clamp(topH - 160f - 50f, 120f, 210f);
            float charW = CharBlockWidth(slotH, nChar);
            float charTop = -108f; // 戻るの下
            PlaceCharBlock(L + charW * 0.5f, charTop, slotH, nChar);
            float sideW = Mathf.Clamp(UW * 0.19f, 300f, 380f);
            float sideL = R - sideW;
            float deckL = L + charW + gap, deckR = sideL - gap, deckW = deckR - deckL;
            float deckTop = -16f;
            PlaceTop(panelDeck, (deckL + deckR) * 0.5f, deckTop, deckW, topH);
            PlaceTop(headerDeckRt, (deckL + deckR) * 0.5f, deckTop - 8f, deckW - 40f, 52f);
            float vpH = topH - 76f;
            PlaceTop((RectTransform)deckScrollRect.transform, (deckL + deckR) * 0.5f, deckTop - 66f, deckW - 24f, vpH);
            FitGrid(deckScrollRect, 6, 2, 1f);
            PlaceTop(toastRt, (deckL + deckR) * 0.5f, deckTop - 66f - vpH * 0.5f + 28f, Mathf.Min(deckW - 40f, 760f), 56f);
            Rect gear = GearInRoot();
            PlaceSideBlock(sideL + sideW * 0.5f, gear.xMax > sideL ? Mathf.Min(-16f, gear.yMin - 8f) : -16f, sideW, topH, false); // 右上の設定ボタンの下から
            // 下段
            float botTop = topBottom - gap;
            float botH = botTop - (-H + m);
            float detW = detailCollapsed ? 60f : Mathf.Clamp(UW * 0.22f, 340f, 430f);
            float colR = R - detW - gap, colW = colR - L;
            PlaceTop(panelCollection, (L + colR) * 0.5f, botTop, colW, botH);
            float tabW = 60f, tabsW = 8 * tabW + 7 * 6f;
            PlaceTabs(colR - 16f - tabsW + tabsW * 0.5f, botTop - 10f, tabW, 46f);
            float cntW = colW - tabsW - 48f;
            PlaceTop(collectionCountText != null ? collectionCountText.rectTransform : null, L + 16f + cntW * 0.5f, botTop - 14f, cntW, 38f);
            float listTop = botTop - 66f;
            PlaceTop((RectTransform)ownedScrollRect.transform, (L + colR) * 0.5f, listTop, colW - 24f, listTop - (-H + m) - 10f);
            FitGrid(ownedScrollRect, ColsFor(ownedScrollRect, 132f, 5, 12), 0, 1f);
            PlaceTop(detailPanelRt, R - detW * 0.5f, botTop, detW, botH);
            LayoutDetailColumn(detW, botH);
        }
        if (collectionCountText != null) { collectionCountText.alignment = TextAnchor.MiddleLeft; collectionCountText.resizeTextForBestFit = false; }
        RefreshCategoryRow();
        RefreshCharSlotHints();
        Canvas.ForceUpdateCanvases();
        Debug.Log($"[DeckEdit] layout v2 {(layoutPortrait ? "portrait" : "landscape")} {W:F0}x{H:F0} owned {ownedScrollRect.content.GetComponent<GridLayoutGroup>().constraintCount} cols x{ownedScrollRect.content.localScale.x:F2}, deck x{deckScrollRect.content.localScale.x:F2}, detail {(detailCollapsed ? "collapsed" : "open")}");
    }

    float CharBlockWidth(float slotH, int n)
    {
        float sw = 72f * slotH / 108f, g = Mathf.Max(10f, sw * 0.12f);
        return n * sw + (n - 1) * g + 28f;
    }

    // キャラカードの欄(見出し ◀ ○○のキャラカード ▶ / 3枠 / 枠の下の案内)。高さを返す
    float PlaceCharBlock(float cx, float top, float slotH, int n)
    {
        float s = slotH / 108f, sw = 72f * s, g = Mathf.Max(10f, sw * 0.12f);
        float rowW = n * sw + (n - 1) * g, blockW = rowW + 28f;
        float blockH = 48f + slotH + 12f + 30f + 8f;
        PlaceTop(charPanel, cx, top, blockW, blockH);
        const float arrowW = 50f;
        float hw = Mathf.Max(120f, blockW - 2f * arrowW - 24f);
        if (charHeader != null)
        {
            PlaceTop(charHeader.rectTransform, cx, top - 4f, hw, 40f);
            charHeader.resizeTextMaxSize = 26; charHeader.resizeTextMinSize = 12; charHeader.resizeTextForBestFit = true;
        }
        foreach (var (ar, dir) in new[] { (charPrevRect, -1), (charNextRect, 1) })
        {
            if (ar == null) continue;
            PlaceTop(ar, cx + dir * (hw * 0.5f + arrowW * 0.5f + 6f), top - 2f, arrowW, 44f);
            var t = ar.GetComponentInChildren<Text>(); if (t != null) UiIcons.ReplaceGlyph(t, dir, 34f);
        }
        float slotsTop = top - 48f;
        for (int i = 0; i < n; i++)
        {
            float x = cx - rowW * 0.5f + sw * 0.5f + i * (sw + g);
            PlaceTop(charSlotBackRts[i], x, slotsTop + 6f, sw + 12f, slotH + 12f);
            var hold = charSlotHolderRts[i];
            if (hold != null)
            {
                hold.anchorMin = hold.anchorMax = new Vector2(0.5f, 1f); hold.pivot = new Vector2(0.5f, 0.5f);
                hold.sizeDelta = new Vector2(72f, 108f);
                hold.anchoredPosition = new Vector2(x, slotsTop - slotH * 0.5f);
                hold.localScale = new Vector3(s, s, 1f);
            }
            if (charSlotPlus[i] != null)
            {
                var pr = charSlotPlus[i].rectTransform;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f); pr.pivot = new Vector2(0.5f, 0.5f);
                pr.sizeDelta = new Vector2(sw, sw); pr.anchoredPosition = new Vector2(x, slotsTop - slotH * 0.5f);
                UiIcons.ReplaceGlyph(charSlotPlus[i], 0, Mathf.Min(sw * 0.7f, 80f));
            }
            if (charSlotHints[i] != null)
            {
                PlaceTop(charSlotHints[i].rectTransform, x, slotsTop - slotH - 10f, sw + g - 4f, 30f);
                charSlotHints[i].resizeTextMaxSize = 16; charSlotHints[i].resizeTextMinSize = 10;
            }
        }
        return blockH;
    }

    // 枚数 / 種類の内訳 / おすすめ編成・全て外す
    float PlaceSideBlock(float cx, float top, float w, float h, bool portrait)
    {
        if (countText != null)
        {
            PlaceTop(countText.rectTransform, cx, top - 6f, w - 16f, 42f);
            countText.alignment = TextAnchor.MiddleCenter;
            countText.resizeTextForBestFit = true; countText.resizeTextMinSize = 14; countText.resizeTextMaxSize = 30;
        }
        float cellW = Mathf.Min(84f, (w - 16f) / 4f);
        PlaceTop(catRow, cx, top - 54f, cellW * 4f, 96f);
        for (int i = 0; i < 7; i++)
        {
            int row = i < 4 ? 0 : 1, col = i < 4 ? i : i - 4, perRow = row == 0 ? 4 : 3;
            var cell = catIcons[i] != null ? catIcons[i].rectTransform.parent as RectTransform : null;
            if (cell == null) continue;
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 1f); cell.pivot = new Vector2(0.5f, 1f);
            cell.sizeDelta = new Vector2(cellW, 44f);
            cell.anchoredPosition = new Vector2((col - (perRow - 1) * 0.5f) * cellW, -row * 50f);
        }
        float btnTop = top - 54f - 104f;
        float btnH = 58f;
        if (portrait)
        {
            float bw = (w - 30f) * 0.5f;
            PlaceTop(recommendButtonRect, cx - bw * 0.5f - 5f, btnTop, bw, btnH);
            PlaceTop(clearButtonRect, cx + bw * 0.5f + 5f, btnTop, bw, btnH);
            return top - (btnTop - btnH);
        }
        PlaceTop(recommendButtonRect, cx, btnTop, w - 24f, btnH);
        PlaceTop(clearButtonRect, cx, btnTop - btnH - 10f, w - 24f, btnH);
        return top - (btnTop - btnH * 2f - 10f);
    }

    void PlaceTabs(float cx, float top, float tabW, float tabH)
    {
        int n = filterButtonRects.Length;
        float sp = 6f, total = n * tabW + (n - 1) * sp;
        for (int i = 0; i < n; i++)
        {
            var r = filterButtonRects[i];
            if (r == null) continue;
            PlaceTop(r, cx - total * 0.5f + tabW * 0.5f + i * (tabW + sp), top, tabW, tabH);
            var ic = i < filterIcons.Length && filterIcons[i] != null ? filterIcons[i].rectTransform : null;
            if (ic != null) ic.sizeDelta = new Vector2(Mathf.Min(tabW, tabH) - 6f, Mathf.Min(tabW, tabH) - 6f);
        }
    }

    // ---- 小さな詳細
    RectTransform DetailDescViewport => detailText != null ? (UguiScrollText.Wrap(detailText) is UguiScrollText s && s != null ? s.Viewport : detailText.rectTransform) : null;

    void SetDetailChildrenVisible(bool on)
    {
        foreach (var t in new Component[] { detailName, detailCategory, detailValue })
            if (t != null) t.gameObject.SetActive(on);
        var vp = DetailDescViewport; if (vp != null) vp.gameObject.SetActive(on);
        if (detailDivider != null) detailDivider.SetActive(on && !string.IsNullOrEmpty(detailCardId));
        if (detailPlaceholderLabel != null) detailPlaceholderLabel.gameObject.SetActive(on && string.IsNullOrEmpty(detailCardId));
    }

    // 横画面: 一覧の右の縦長の欄(名前 / 種類・Lv / 主な効果 / 説明 / 「詳細」)。畳むと細い帯(開くボタンだけ)
    void LayoutDetailColumn(float w, float h)
    {
        if (detailPanelRt == null) return;
        PlaceIn(detailToggleRt, w - 52f - 8f, -8f, 52f, 44f);
        if (detailToggleIcon != null) detailToggleIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, detailCollapsed ? 180f : 0f); // 畳んでいる時は「◀」(開く)
        if (detailCollapsed)
        {
            PlaceIn(detailToggleRt, 4f, -8f, w - 8f, 52f);
            SetDetailChildrenVisible(false);
            if (convertButtonRect != null) convertButtonRect.gameObject.SetActive(false);
            return;
        }
        SetDetailChildrenVisible(true);
        float pad = 16f, iw = w - pad * 2f;
        PlaceIn(detailName != null ? detailName.rectTransform : null, pad, -10f, iw - 56f, 42f);
        PlaceIn(detailCategory != null ? detailCategory.rectTransform : null, pad, -56f, iw, 30f);
        if (detailDivider != null) PlaceIn((RectTransform)detailDivider.transform, pad, -92f, iw, 2f);
        PlaceIn(detailValue != null ? detailValue.rectTransform : null, pad, -100f, iw, 112f);
        float btnH = 58f;
        PlaceIn(DetailDescViewport, pad, -218f, iw, Mathf.Max(40f, h - 218f - btnH - 26f));
        if (convertButtonRect != null) { PlaceIn(convertButtonRect, pad, -(h - btnH - 14f), iw, btnH); }
        if (detailPlaceholderLabel != null) { PlaceIn(detailPlaceholderLabel.rectTransform, pad, -h * 0.35f, iw, 90f); detailPlaceholderLabel.alignment = TextAnchor.MiddleCenter; }
        foreach (var t in new[] { detailName, detailCategory }) if (t != null) t.alignment = TextAnchor.MiddleLeft;
        if (detailName != null) { detailName.resizeTextForBestFit = true; detailName.resizeTextMinSize = 14; }
    }

    // 縦画面: デッキと一覧の間の横長の帯(左に名前・種類・主な効果、右に説明と「詳細」)。畳むと名前の1行だけ
    void LayoutDetailStrip(float w, float h)
    {
        if (detailPanelRt == null) return;
        PlaceIn(detailToggleRt, w - 52f - 10f, -8f, 52f, 44f);
        if (detailToggleIcon != null) detailToggleIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, detailCollapsed ? -90f : 90f); // 畳んでいる時は「▼」(開く)
        float pad = 16f;
        float btnW = 150f;
        if (detailCollapsed)
        {
            SetDetailChildrenVisible(false);
            if (detailName != null) { detailName.gameObject.SetActive(!string.IsNullOrEmpty(detailCardId)); PlaceIn(detailName.rectTransform, pad, -8f, w - pad * 2f - btnW - 80f, 44f); }
            if (convertButtonRect != null) PlaceIn(convertButtonRect, w - btnW - 76f, -6f, btnW, 48f);
            if (detailPlaceholderLabel != null && string.IsNullOrEmpty(detailCardId)) { detailPlaceholderLabel.gameObject.SetActive(true); PlaceIn(detailPlaceholderLabel.rectTransform, pad, -8f, w - pad * 2f - 80f, 44f); }
            return;
        }
        SetDetailChildrenVisible(true);
        float lw = (w - pad * 3f) * 0.5f;
        PlaceIn(detailName != null ? detailName.rectTransform : null, pad, -8f, lw, 40f);
        PlaceIn(detailCategory != null ? detailCategory.rectTransform : null, pad, -50f, lw, 28f);
        if (detailDivider != null) detailDivider.SetActive(false);
        PlaceIn(detailValue != null ? detailValue.rectTransform : null, pad, -82f, lw, h - 92f);
        float rx = pad * 2f + lw, rw = w - rx - pad;
        PlaceIn(DetailDescViewport, rx, -56f, rw, Mathf.Max(30f, h - 56f - 64f));
        if (convertButtonRect != null) PlaceIn(convertButtonRect, w - pad - btnW, -(h - 58f), btnW, 50f);
        if (detailPlaceholderLabel != null) { PlaceIn(detailPlaceholderLabel.rectTransform, pad, -h * 0.5f + 30f, w - pad * 2f - 70f, 60f); detailPlaceholderLabel.alignment = TextAnchor.MiddleCenter; }
        foreach (var t in new[] { detailName, detailCategory }) if (t != null) t.alignment = TextAnchor.MiddleLeft;
        if (detailName != null) { detailName.resizeTextForBestFit = true; detailName.resizeTextMinSize = 14; }
    }

    // 選んだカードが変わった時: 小さな詳細の中だけ並べ直す(畳んだ帯の名前の有無など)
    void RelayoutDetail()
    {
        if (!layoutApplied || detailPanelRt == null) return;
        Vector2 sz = detailPanelRt.sizeDelta;
        if (layoutPortrait) LayoutDetailStrip(sz.x, sz.y); else LayoutDetailColumn(sz.x, sz.y);
    }

    void ToggleDetailCollapsed()
    {
        detailCollapsed = !detailCollapsed;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        layoutApplied = false; // 次の Update で並べ直す
        Debug.Log($"[DeckEdit] detail {(detailCollapsed ? "collapsed" : "opened")}");
    }
    public void DebugSetDetailCollapsed(bool on) { detailCollapsed = on; layoutApplied = false; }
    public static bool DetailCollapsed => detailCollapsed;
}

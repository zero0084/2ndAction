using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// カード編集画面の絞り込みと並び替え(2026-11-06、マスターの依頼 18〜21)。
//  ・マップ別(すべて/共通/荒野/洞窟/天空/ラスダン): 一覧の上の目立つタブ。タブの下の数字は「所持している種類 / そのマップの種類」
//    (分類は CardMapCategories = そのマップで解放する場所。マップ専用ではない)。マップを選んでいる時は、枚数の行に「未解放 n」も出す
//  ・レア度 / 属性 / 並び替え: 小さなボタン(押すと選択肢の窓)。マップ・種類(今までのアイコンのタブ)と組み合わせられる(例: 荒野 + ★4)
//  ・まだ手に入れていないカードは一覧に出さない(今までどおり。存在は数だけで分かる)
//  ・絞り込みは画面を開くたびに「すべて」へ戻す(今までの種類のタブと同じ)。並び替えはアプリを閉じるまで覚える(保存はしない)
//  ・デッキ12枠は一覧の外(上)にあるので、絞り込んでもそのまま見える
public partial class DeckEditUI
{
    public enum SortKey { Standard, Rarity, Level, Element, Name, Owned, Map, Unlock }
    // 並び替えの項目の名前(元の文)。標準/レア度/レベル/名前/所持枚数/並び替え は訳がある
    static readonly string[] SortLabelSrc = { "標準", "レア度", "レベル", LocCardFilter.Element, "名前", "所持枚数", LocCardFilter.Map, LocCardFilter.UnlockOrder };
    static readonly bool[] SortDefaultDesc = { false, true, true, false, false, true, false, false };
    static SortKey sortKey = SortKey.Standard;
    static bool sortDesc;
    int mapFilter = -1;       // -1 = すべて / それ以外 = (int)CardMapCategory
    int rarityFilter;         // 0 = すべて / 1〜5
    int elementFilter = -1;   // -1 = すべて / それ以外 = (int)ElementType
    // 属性の選択肢の並び(すべて / 炎 / 氷 / 雷 / 風 / 血 / なし)
    static readonly int[] ElementOptions = { -1, (int)ElementType.Fire, (int)ElementType.Ice, (int)ElementType.Thunder, (int)ElementType.Wind, (int)ElementType.Blood, (int)ElementType.None };
    const int MapCount = 5;

    bool AnyFilterActive => mapFilter >= 0 || rarityFilter > 0 || elementFilter >= 0 || (activeFilter != null && activeFilter != "ALL");

    // ---- マップごとの数(タブの下の数字と「未解放 n」)。index 0 = すべて、1〜5 = CardMapCategory + 1
    readonly int[] mapOwnedKinds = new int[MapCount + 1], mapTotalKinds = new int[MapCount + 1], mapLockedKinds = new int[MapCount + 1];

    // ---- 部品
    bool filterPartsBuilt;
    readonly RectTransform[] mapTabRts = new RectTransform[MapCount + 1];
    readonly Image[] mapTabBgs = new Image[MapCount + 1];
    readonly Text[] mapTabLabels = new Text[MapCount + 1], mapTabCounts = new Text[MapCount + 1];
    readonly RectTransform[] chooserRts = new RectTransform[3];
    readonly Image[] chooserBgs = new Image[3], chooserIcons = new Image[3];
    readonly Text[] chooserCaps = new Text[3], chooserVals = new Text[3];
    RectTransform popupRt;
    int popupKind = -1;
    readonly List<(RectTransform rt, int value)> popupOptions = new List<(RectTransform, int)>();
    Text noMatchText;
    Font filterFont;
    public bool FilterPopupOpen => popupRt != null && popupRt.gameObject.activeSelf;

    static Color TabOff => new Color(0.08f, 0.09f, 0.16f, 0.9f);
    static Color TabOn => new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.95f);
    static readonly Color TabOnText = new Color(0.08f, 0.06f, 0.02f);

    static ElementArt.Mark? MarkOf(int element) => element switch
    {
        (int)ElementType.Fire => ElementArt.Mark.Burn,
        (int)ElementType.Ice => ElementArt.Mark.Chill,
        (int)ElementType.Thunder => ElementArt.Mark.Shock,
        (int)ElementType.Wind => ElementArt.Mark.Wind,
        (int)ElementType.Blood => ElementArt.Mark.Bleed,
        _ => (ElementArt.Mark?)null,
    };

    Text NewText(RectTransform parent, string name, int size, TextAnchor align, Color color)
    {
        var r = NewUi(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        var t = r.gameObject.AddComponent<Text>();
        t.font = filterFont; t.fontSize = size; t.alignment = align; t.color = color; t.raycastTarget = false;
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.supportRichText = true;
        return t;
    }

    RectTransform NewButton(RectTransform parent, string name, out Image bg)
    {
        var rt = NewUi(name, parent, new Vector2(0.5f, 1f), new Vector2(100f, 46f), Vector2.zero);
        bg = rt.gameObject.AddComponent<Image>(); bg.color = TabOff; bg.raycastTarget = false;
        var ol = rt.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.6f); ol.effectDistance = new Vector2(1.5f, -1.5f);
        return rt;
    }

    void BuildFilterParts(RectTransform rootRt, Font font)
    {
        if (filterPartsBuilt) return;
        filterPartsBuilt = true;
        filterFont = font;
        for (int i = 0; i <= MapCount; i++)
        {
            var rt = NewButton(rootRt, "MapTab_" + i, out mapTabBgs[i]);
            mapTabRts[i] = rt;
            mapTabLabels[i] = NewText(rt, "Label", 20, TextAnchor.MiddleCenter, Color.white);
            mapTabLabels[i].fontStyle = FontStyle.Bold;
            mapTabCounts[i] = NewText(rt, "Count", 15, TextAnchor.MiddleCenter, UiTheme.TextSub);
        }
        for (int i = 0; i < 3; i++)
        {
            var rt = NewButton(rootRt, "Chooser_" + i, out chooserBgs[i]);
            chooserRts[i] = rt;
            chooserCaps[i] = NewText(rt, "Caption", 14, TextAnchor.MiddleCenter, UiTheme.TextSub);
            chooserVals[i] = NewText(rt, "Value", 19, TextAnchor.MiddleCenter, UiTheme.GoldText);
            chooserVals[i].fontStyle = FontStyle.Bold;
            var ic = NewUi("Icon", rt, new Vector2(0.5f, 0f), new Vector2(26f, 26f), Vector2.zero);
            ic.pivot = new Vector2(0.5f, 0f);
            chooserIcons[i] = ic.gameObject.AddComponent<Image>(); chooserIcons[i].preserveAspect = true; chooserIcons[i].raycastTarget = false;
            ic.gameObject.SetActive(false);
        }
        popupRt = NewUi("FilterPopup", rootRt, new Vector2(0.5f, 1f), new Vector2(260f, 300f), Vector2.zero);
        var pbg = popupRt.gameObject.AddComponent<Image>(); pbg.color = new Color(0.04f, 0.05f, 0.11f, 0.98f); pbg.raycastTarget = false;
        var pol = popupRt.gameObject.AddComponent<Outline>(); pol.effectColor = UiTheme.GoldEdge; pol.effectDistance = new Vector2(2f, -2f);
        popupRt.gameObject.SetActive(false);
        if (ownedScrollRect != null)
        {
            var vp = (RectTransform)ownedScrollRect.transform;
            var nr = NewUi("NoMatch", vp, new Vector2(0.5f, 0.5f), new Vector2(700f, 120f), Vector2.zero);
            nr.pivot = new Vector2(0.5f, 0.5f);
            noMatchText = nr.gameObject.AddComponent<Text>();
            noMatchText.font = font; UiTheme.Apply(noMatchText, UiTheme.TextRole.Body, 1.1f);
            noMatchText.color = UiTheme.TextSub; noMatchText.alignment = TextAnchor.MiddleCenter; noMatchText.raycastTarget = false;
            noMatchText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nr.gameObject.SetActive(false);
        }
    }

    // ---- 配置(DeckEditUI.Layout から)。left = 左端(画面の中央からの x)、top = 上端
    // k = 文字の大きさの倍率(縦画面は大きく)。後から大きさを変えるので LocTextBinder.Resize を通す(言語を切り替えても小さく戻らない)
    void PlaceMapTabs(float left, float top, float width, float h, float k)
    {
        const float sp = 6f;
        float w = (width - sp * MapCount) / (MapCount + 1);
        for (int i = 0; i <= MapCount; i++)
        {
            PlaceTop(mapTabRts[i], left + w * 0.5f + i * (w + sp), top, w, h);
            SplitV(mapTabLabels[i], 0.42f, 1f, 2f); SplitV(mapTabCounts[i], 0f, 0.44f, 2f);
            LocTextBinder.Resize(mapTabLabels[i], Mathf.RoundToInt(20f * k)); LocTextBinder.Resize(mapTabCounts[i], Mathf.RoundToInt(15f * k));
        }
    }

    void PlaceChoosers(float left, float top, float width, float h, float k)
    {
        const float sp = 8f;
        float unit = (width - sp * 2f) / 3.4f; // レア度・属性 = 1、並び替え = 1.4
        float x = left;
        for (int i = 0; i < 3; i++)
        {
            float w = i == 2 ? unit * 1.4f : unit;
            PlaceTop(chooserRts[i], x + w * 0.5f, top, w, h);
            SplitV(chooserCaps[i], 0.56f, 1f, 1f); SplitV(chooserVals[i], 0f, 0.6f, 2f);
            LocTextBinder.Resize(chooserCaps[i], Mathf.RoundToInt(14f * k)); LocTextBinder.Resize(chooserVals[i], Mathf.RoundToInt(19f * k));
            var ic = chooserIcons[i] != null ? chooserIcons[i].rectTransform : null;
            if (ic != null) { float s = Mathf.Max(16f, h * 0.5f); ic.sizeDelta = new Vector2(s, s); ic.anchoredPosition = new Vector2(0f, 2f); }
            x += w + sp;
        }
    }

    // Text を親の上下の割合(下 y0 〜 上 y1)の帯へ
    static void SplitV(Text t, float y0, float y1, float padX)
    {
        if (t == null) return;
        var r = t.rectTransform;
        r.anchorMin = new Vector2(0f, y0); r.anchorMax = new Vector2(1f, y1);
        r.offsetMin = new Vector2(padX, 0f); r.offsetMax = new Vector2(-padX, 0f);
    }

    // ---- 一覧の元を作る(所持の束 → 絞り込み → 並び替え)
    struct OwnedEntry { public CardInventory.Stack st; public CardDefinition def, baseDef; public int map, element, order; public string name; }
    readonly List<OwnedEntry> entryBuf = new List<OwnedEntry>();
    readonly Dictionary<string, CardDefinition> baseDefCache = new Dictionary<string, CardDefinition>();

    CardDefinition BaseDefOf(string key)
    {
        string id = CardMastery.BaseIdOf(key) ?? key;
        if (!baseDefCache.TryGetValue(id, out var d)) { d = CardDatabase.FindBaseById(id); baseDefCache[id] = d; }
        return d;
    }

    bool PassesFilters(in OwnedEntry e, bool useRarity = true, bool useElement = true)
    {
        if (!MatchesFilter(e.def.category, activeFilter)) return false;
        if (mapFilter >= 0 && e.map != mapFilter) return false;
        if (useRarity && rarityFilter > 0 && e.def.rarity != rarityFilter) return false;
        if (useElement && elementFilter >= 0 && e.element != elementFilter) return false;
        return true;
    }

    int CompareEntries(OwnedEntry a, OwnedEntry b)
    {
        int p;
        switch (sortKey)
        {
            case SortKey.Rarity: p = a.def.rarity.CompareTo(b.def.rarity); break;
            case SortKey.Level: p = a.st.level.CompareTo(b.st.level); break;
            case SortKey.Element: p = ElementRank(a.element).CompareTo(ElementRank(b.element)); break;
            case SortKey.Name: p = string.Compare(a.name, b.name, System.StringComparison.CurrentCultureIgnoreCase); break;
            case SortKey.Owned: p = a.st.count.CompareTo(b.st.count); break;
            case SortKey.Map: p = a.map.CompareTo(b.map); break;
            case SortKey.Unlock:
                p = (a.baseDef != null ? a.baseDef.unlockDistance : 0f).CompareTo(b.baseDef != null ? b.baseDef.unlockDistance : 0f);
                if (p == 0) p = (a.baseDef != null ? a.baseDef.gachaStage : 0).CompareTo(b.baseDef != null ? b.baseDef.gachaStage : 0);
                break;
            default: p = 0; break;
        }
        if (sortKey == SortKey.Standard) // 今までの並び(sortOrder → Lv)
        {
            p = a.def.sortOrder.CompareTo(b.def.sortOrder);
            if (p == 0) p = a.st.level.CompareTo(b.st.level);
        }
        if (sortDesc) p = -p;
        if (p != 0) return p;
        int o = a.def.sortOrder.CompareTo(b.def.sortOrder);
        if (o != 0) return o;
        int l = b.st.level.CompareTo(a.st.level);
        if (l != 0) return l;
        return a.order.CompareTo(b.order);
    }
    // 属性の並び: 炎 → 氷 → 雷 → 風 → 血 → なし
    static int ElementRank(int e) { int i = System.Array.IndexOf(ElementOptions, e); return i < 0 ? 99 : i; }

    void BuildOwnedEntries()
    {
        var gm = GameManager.Instance;
        entryBuf.Clear();
        int order = 0;
        foreach (var st in CardInventory.Stacks)
        {
            if (st.count <= 0) continue;
            CardDefinition def = CardDatabase.FindById(st.cardId);
            if (def == null) continue;
            var bd = BaseDefOf(st.cardId);
            entryBuf.Add(new OwnedEntry
            {
                st = st, def = def, baseDef = bd, order = order++,
                map = (int)CardMapCategories.Of(bd != null ? bd.cardId : st.cardId),
                element = (int)(bd != null ? bd.element : def.element),
                name = sortKey == SortKey.Name ? Loc.Auto(def.cardName) : null,
            });
        }
        ownedStackTotal = entryBuf.Count;
        var list = new List<OwnedEntry>(entryBuf.Count);
        foreach (var e in entryBuf) if (PassesFilters(e)) list.Add(e);
        list.Sort(CompareEntries);
        displayedStacks.Clear(); displayedDefs.Clear();
        foreach (var e in list) { displayedStacks.Add(e.st); displayedDefs.Add(e.def); }

        // マップごとの種類の数(所持 / 全部 / 未解放)。合成のキーは数えない(元のカードで数える)
        for (int i = 0; i <= MapCount; i++) mapOwnedKinds[i] = mapTotalKinds[i] = mapLockedKinds[i] = 0;
        // 所持している元のカードの id(束を1回なめるだけ。カードごとに GetTotalCountByName を呼ぶと、束の数 × カードの数だけ合成のキーを読み直して数秒かかった)
        var ownedNames = new HashSet<string>();
        foreach (var e in entryBuf) ownedNames.Add(e.baseDef != null ? e.baseDef.cardId : CardMastery.BaseIdOf(e.st.cardId));
        foreach (var c in CardDatabase.AllCards)
        {
            if (c == null || CardVariant.IsVariantKey(c.cardId)) continue;
            int m = (int)CardMapCategories.Of(c.cardId) + 1;
            if (m < 1 || m > MapCount) m = 1;
            bool owned = ownedNames.Contains(c.cardId);
            bool locked = !owned && gm != null && !GachaStage.IsCardEligible(c, gm.BestDistance, gm.CurrentGachaStage);
            foreach (int k in new[] { 0, m })
            {
                mapTotalKinds[k]++;
                if (owned) mapOwnedKinds[k]++;
                if (locked) mapLockedKinds[k]++;
            }
        }
    }

    // 選択肢ごとの件数(今のマップ・種類の絞り込みの中で)。窓の中に小さく出す
    int CountWith(int kind, int value)
    {
        int n = 0;
        foreach (var e in entryBuf)
        {
            if (!PassesFilters(e, kind != 0, kind != 1)) continue;
            if (kind == 0 && value > 0 && e.def.rarity != value) continue;
            if (kind == 1 && value >= 0 && e.element != value) continue;
            n++;
        }
        return n;
    }

    // ---- 見た目(Refresh から)
    void RefreshFilterBar()
    {
        if (!filterPartsBuilt) return;
        for (int i = 0; i <= MapCount; i++)
        {
            bool on = mapFilter == i - 1;
            if (mapTabBgs[i] != null) mapTabBgs[i].color = on ? TabOn : TabOff;
            if (mapTabLabels[i] != null)
            {
                mapTabLabels[i].text = i == 0 ? Loc.T("ALL") : Loc.T(CardMapCategories.LabelSrc((CardMapCategory)(i - 1)));
                mapTabLabels[i].color = on ? TabOnText : Color.white;
            }
            if (mapTabCounts[i] != null)
            {
                // 所持している種類 / そのマップの種類。全部そろったら金、まだ解放できるカードが残っていれば橙
                mapTabCounts[i].text = $"{mapOwnedKinds[i]}/{mapTotalKinds[i]}";
                Color cc = mapTotalKinds[i] > 0 && mapOwnedKinds[i] >= mapTotalKinds[i] ? UiTheme.Mile : mapLockedKinds[i] > 0 ? new Color(1f, 0.66f, 0.38f) : UiTheme.TextSub;
                mapTabCounts[i].color = on ? new Color(0.2f, 0.12f, 0.02f) : cc;
            }
        }
        // レア度
        SetChooser(0, Loc.T("レア度"), rarityFilter == 0 ? Loc.T("ALL") : new string('★', rarityFilter), null, rarityFilter > 0);
        // 属性(炎/氷/雷/風/血 は印の絵だけ。文字は使わない)
        var mark = elementFilter >= 0 ? MarkOf(elementFilter) : null;
        SetChooser(1, Loc.T(LocCardFilter.Element), elementFilter < 0 ? Loc.T("ALL") : mark.HasValue ? "" : Loc.T("なし"), mark.HasValue ? ElementArt.MarkSprite(mark.Value) : null, elementFilter >= 0);
        // 並び替え
        SetChooser(2, Loc.T("並び替え"), Loc.T(SortLabelSrc[(int)sortKey]) + (sortDesc ? " ▼" : " ▲"), null, sortKey != SortKey.Standard || sortDesc);
        if (noMatchText != null)
        {
            bool show = displayedStacks.Count == 0 && AnyFilterActive;
            if (noMatchText.gameObject.activeSelf != show) noMatchText.gameObject.SetActive(show);
            if (show) noMatchText.text = Loc.T(LocCardFilter.NoMatch);
        }
    }

    void SetChooser(int i, string cap, string val, Sprite icon, bool active)
    {
        if (chooserRts[i] == null) return;
        chooserCaps[i].text = cap;
        chooserVals[i].text = val;
        chooserVals[i].color = active ? UiTheme.GoldText : Color.white;
        chooserBgs[i].color = active ? new Color(0.3f, 0.22f, 0.06f, 0.95f) : TabOff;
        if (popupKind == i) chooserBgs[i].color = new Color(0.45f, 0.33f, 0.09f, 0.98f);
        var ic = chooserIcons[i];
        if (ic != null) { ic.gameObject.SetActive(icon != null); ic.sprite = icon; }
    }

    // 枚数の行の後ろ: マップを選んでいる時の「未解放 n」
    string LockedSuffix()
    {
        if (mapFilter < 0) return "";
        int n = mapLockedKinds[mapFilter + 1];
        return n > 0 ? "  <color=#ffb070>" + Loc.F(LocCardFilter.Locked, n) + "</color>" : "";
    }

    // ---- 操作
    // true = 絞り込みの部品が受けた(HandleTap から)
    bool HandleFilterTap(Vector2 p)
    {
        if (!filterPartsBuilt) return false;
        for (int i = 0; i <= MapCount; i++)
        {
            var r = mapTabRts[i];
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            if (UiHit.Hit(r, p)) { SetMapFilter(i - 1); return true; }
        }
        for (int i = 0; i < 3; i++)
        {
            var r = chooserRts[i];
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            if (UiHit.Hit(r, p)) { OpenFilterPopup(i); return true; }
        }
        return false;
    }

    void SetMapFilter(int m)
    {
        if (mapFilter == m) return;
        mapFilter = m;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        AfterFilterChange();
    }

    void AfterFilterChange()
    {
        Refresh();
        if (ownedScrollRect != null) { ownedScrollRect.verticalNormalizedPosition = 1f; ownedScrollRect.velocity = Vector2.zero; }
        UpdateVirtual(false); // 先頭へ戻した分だけ入れ替える
        UpdateOwnedScrollbar();
        Debug.Log($"[DeckEdit] filter map {mapFilter} rarity {rarityFilter} element {elementFilter} category {activeFilter} sort {sortKey}{(sortDesc ? " desc" : "")}: {displayedStacks.Count}/{ownedStackTotal} ({LastRefreshMs:F1} ms, entries {LastEntriesMs:F1} ms, binds {LastBindCount})");
    }

    void OpenFilterPopup(int kind)
    {
        if (popupRt == null) return;
        if (popupKind == kind && FilterPopupOpen) { CloseFilterPopup(); return; }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        popupKind = kind;
        foreach (var o in popupOptions) if (o.rt != null) Destroy(o.rt.gameObject);
        popupOptions.Clear();
        var values = new List<int>();
        if (kind == 0) for (int r = 0; r <= 5; r++) values.Add(r);
        else if (kind == 1) values.AddRange(ElementOptions);
        else for (int s = 0; s < SortLabelSrc.Length; s++) values.Add(s);
        var btn = chooserRts[kind];
        const float optH = 52f, pad = 6f;
        float w = Mathf.Max(btn.sizeDelta.x, kind == 2 ? 270f : 220f);
        float h = values.Count * optH + pad * 2f;
        var rootRt = (RectTransform)root.transform;
        float H = rootRt.rect.height, W = rootRt.rect.width;
        float x = Mathf.Clamp(btn.anchoredPosition.x + btn.sizeDelta.x * 0.5f - w * 0.5f, -W * 0.5f + w * 0.5f + 8f, W * 0.5f - w * 0.5f - 8f);
        float top = btn.anchoredPosition.y - btn.sizeDelta.y - 4f;
        if (top - h < -H + 8f) top = btn.anchoredPosition.y + h + 4f; // 下に入らなければボタンの上へ
        PlaceTop(popupRt, x, top, w, h);
        for (int i = 0; i < values.Count; i++)
        {
            int v = values[i];
            var rt = NewUi("Option_" + i, popupRt, new Vector2(0.5f, 1f), new Vector2(w - pad * 2f, optH - 4f), new Vector2(0f, -pad - i * optH));
            var bg = rt.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
            bool cur = kind == 0 ? v == rarityFilter : kind == 1 ? v == elementFilter : v == (int)sortKey;
            bg.color = cur ? new Color(0.45f, 0.33f, 0.09f, 0.98f) : new Color(0.09f, 0.11f, 0.22f, 0.95f);
            string label; Sprite icon = null; int count = -1;
            if (kind == 0) { label = v == 0 ? Loc.T("ALL") : new string('★', v); count = CountWith(0, v); }
            else if (kind == 1)
            {
                var mk = v >= 0 ? MarkOf(v) : null;
                label = v < 0 ? Loc.T("ALL") : mk.HasValue ? "" : Loc.T("なし");
                if (mk.HasValue) icon = ElementArt.MarkSprite(mk.Value);
                count = CountWith(1, v);
            }
            else label = Loc.T(SortLabelSrc[v]) + (cur ? (sortDesc ? " ▼" : " ▲") : "");
            var t = NewText(rt, "Label", 22, kind == 2 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, cur ? UiTheme.GoldText : Color.white);
            t.rectTransform.offsetMin = new Vector2(14f, 0f); t.rectTransform.offsetMax = new Vector2(-54f, 0f);
            t.text = label;
            if (icon != null)
            {
                var ir = NewUi("Icon", rt, new Vector2(0.5f, 0.5f), new Vector2(38f, 38f), Vector2.zero);
                ir.pivot = new Vector2(0.5f, 0.5f); ir.anchoredPosition = new Vector2(-20f, 0f);
                var img = ir.gameObject.AddComponent<Image>(); img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
            }
            if (count >= 0)
            {
                var c = NewText(rt, "Count", 18, TextAnchor.MiddleRight, count > 0 ? UiTheme.TextSub : UiTheme.Disabled);
                c.rectTransform.offsetMin = new Vector2(10f, 0f); c.rectTransform.offsetMax = new Vector2(-12f, 0f);
                c.text = count.ToString();
            }
            popupOptions.Add((rt, v));
        }
        popupRt.gameObject.SetActive(true);
        popupRt.SetAsLastSibling();
        RefreshFilterBar();
    }

    public void CloseFilterPopup()
    {
        if (popupRt == null || !popupRt.gameObject.activeSelf) return;
        popupRt.gameObject.SetActive(false);
        popupKind = -1;
        RefreshFilterBar();
    }

    // 窓が開いている間は、窓だけが受ける(外を押すと閉じる)
    void HandlePopupTap(Vector2 p)
    {
        foreach (var o in popupOptions)
            if (o.rt != null && UiHit.Hit(o.rt, p)) { ApplyFilterOption(popupKind, o.value); return; }
        if (UiHit.Probing) return;
        CloseFilterPopup();
    }

    void ApplyFilterOption(int kind, int v)
    {
        if (kind == 0) rarityFilter = v;
        else if (kind == 1) elementFilter = v;
        else if (kind == 2)
        {
            var k = (SortKey)v;
            if (k == sortKey) sortDesc = !sortDesc; // 同じ項目をもう一度選ぶと逆の順
            else { sortKey = k; sortDesc = SortDefaultDesc[v]; }
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        CloseFilterPopup();
        AfterFilterChange();
    }

    void ResetFiltersForOpen()
    {
        mapFilter = -1; rarityFilter = 0; elementFilter = -1;
        CloseFilterPopup();
    }

    // ---- 確認用
    public int DebugMapFilter => mapFilter;
    public void DebugSetMapFilter(int m) { mapFilter = m; AfterFilterChange(); }
    public void DebugSetRarityFilter(int r) { rarityFilter = r; AfterFilterChange(); }
    public void DebugSetElementFilter(int e) { elementFilter = e; AfterFilterChange(); }
    public void DebugSetSort(SortKey k, bool desc) { sortKey = k; sortDesc = desc; AfterFilterChange(); }
    public void DebugOpenFilterPopup(int kind) => OpenFilterPopup(kind);
    public void DebugTapFilterOption(int index) { if (index >= 0 && index < popupOptions.Count) ApplyFilterOption(popupKind, popupOptions[index].value); }
    public Vector2 DebugMapTabPos(int i) => i >= 0 && i <= MapCount && mapTabRts[i] != null ? RectTransformUtility.WorldToScreenPoint(null, mapTabRts[i].TransformPoint(mapTabRts[i].rect.center)) : Vector2.zero;
    public RectTransform DebugMapTabRect(int i) => i >= 0 && i <= MapCount ? mapTabRts[i] : null;
    public RectTransform DebugChooserRect(int i) => i >= 0 && i < 3 ? chooserRts[i] : null;
    public RectTransform DebugPopupRect => popupRt;
    public string DebugMapTabText(int i) => i >= 0 && i <= MapCount && mapTabLabels[i] != null ? mapTabLabels[i].text + " " + mapTabCounts[i].text : "";
    public int DebugMapLocked(int i) => i >= 0 && i <= MapCount ? mapLockedKinds[i] : 0;
    public int DebugMapTotal(int i) => i >= 0 && i <= MapCount ? mapTotalKinds[i] : 0;
    public int DebugMapOwned(int i) => i >= 0 && i <= MapCount ? mapOwnedKinds[i] : 0;
    public CardDefinition DebugShownDef(int i) => i >= 0 && i < displayedDefs.Count ? displayedDefs[i] : null;
    public bool DebugNoMatchShown => noMatchText != null && noMatchText.gameObject.activeSelf;
    public string DebugCollectionCountText => collectionCountText != null ? collectionCountText.text : "";
}

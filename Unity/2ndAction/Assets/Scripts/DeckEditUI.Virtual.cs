using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 所持カードの一覧の仮想スクロール(2026-11-06、マスターの依頼「カードが増えても一覧から消えない」)。
//  以前: シーンに置いた枠(160)を並び順の先頭から埋めていた → 束(Lv違い・合成の違い・所持の違いは別の束)が枠の数を超えると後ろ(★5)が消えた。
//   2026-11-01 の応急の直し(足りない枠を複製して足す)は消えなくなったが、束の数だけカードの部品を作って毎回全部を書き直すので、
//   束が 600 を超えると開く時・タップのたびに重くなる。
//  今: 一覧の高さは「表示する束の数」から計算し(固定の上限なし)、カードの部品は「見えている段 + 上下の予備の段」の分だけ置いて、
//   スクロールした時に見える位置へ動かして中身を入れ替える。部品の数は画面の大きさで決まり(足りなければその場で増やす)、束の数には関係しない。
//  ・GridLayoutGroup と ContentSizeFitter は止めて、同じ大きさ・間隔(cellSize/spacing/padding/列の数)で自分で並べる。列の数と縮小は今までどおり FitGrid
//  ・枠 s が見せる束の番号は slotData[s](-1 = 空き)。束の番号 d は枠 d % 部品の数 に入る(見えている範囲の中では重ならない)
//  ・押した/運んだ/詳細などの処理は「束の番号」で持つ(枠は OwnedCardFor で引く)。見えていない束は枠を持たない
public partial class DeckEditUI
{
    int[] slotData = new int[0];
    int[] slotWant = new int[0];
    readonly List<CardDefinition> displayedDefs = new List<CardDefinition>();
    bool virtualReady, ownedDirty = true;
    int vFirstRow = -1, vLastRow = -1, vCols = -1, vPool = -1;
    float vContentW = -1f;
    GridLayoutGroup ownedGrid;
    int focusIndex = -1;
    HashSet<string> deckKeySet = new HashSet<string>();
    public static float LastRefreshMs { get; private set; }   // 確認用: 一覧を作り直すのにかかった時間
    public static float LastEntriesMs { get; private set; }   // 確認用: そのうち束を集めて絞り込み・並び替えるまで
    public static int LastBindCount { get; private set; }     // 確認用: 最後に中身を入れ替えた枠の数
    public static int TotalBinds { get; private set; }

    void EnsureVirtual()
    {
        if (virtualReady || ownedScrollRect == null || ownedScrollRect.content == null || ownedCards == null || ownedCards.Length == 0) return;
        virtualReady = true;
        var c = ownedScrollRect.content;
        ownedGrid = c.GetComponent<GridLayoutGroup>();
        var fit = c.GetComponent<ContentSizeFitter>();
        if (fit != null) fit.enabled = false;
        if (ownedGrid != null) ownedGrid.enabled = false;
        foreach (var card in ownedCards) if (card != null) card.gameObject.SetActive(false);
        slotData = new int[ownedCards.Length];
        for (int i = 0; i < slotData.Length; i++) slotData[i] = -1;
        ownedScrollRect.onValueChanged.AddListener(_ => { UpdateVirtual(false); UpdateOwnedScrollbar(); });
        Debug.Log($"[DeckEdit] owned list is virtual (scene slots {ownedCards.Length} reused as the visible rows)");
    }

    Vector2 CellSize => ownedGrid != null ? ownedGrid.cellSize : new Vector2(130f, 195f);
    Vector2 CellSpacing => ownedGrid != null ? ownedGrid.spacing : new Vector2(22f, 20f);
    RectOffset CellPad => ownedGrid != null ? ownedGrid.padding : new RectOffset();
    int OwnedCols => ownedGrid != null && ownedGrid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? Mathf.Max(1, ownedGrid.constraintCount) : 4;

    // 一覧の枠を need 個まで増やす(同じ枠を複製。上限は置かない)
    void EnsureOwnedPool(int need)
    {
        if (ownedCards == null || ownedCards.Length == 0 || ownedCards.Length >= need) return;
        var src = ownedCards[0];
        var list = new List<RewardCardUI>(ownedCards);
        while (list.Count < need)
        {
            var go = Instantiate(src.gameObject, src.transform.parent);
            go.name = "OwnedCard_" + list.Count;
            go.SetActive(false);
            list.Add(go.GetComponent<RewardCardUI>());
        }
        ownedCards = list.ToArray();
        var sd = new int[ownedCards.Length];
        for (int i = 0; i < sd.Length; i++) sd[i] = i < slotData.Length ? slotData[i] : -1;
        slotData = sd;
        ownedDirty = true;
        Debug.Log($"[DeckEdit] owned card slots grown to {ownedCards.Length}");
    }

    // 束の番号 d を見せている枠(見えていない時は null)
    RewardCardUI OwnedCardFor(int d)
    {
        if (d < 0 || ownedCards.Length == 0) return null;
        int s = d % ownedCards.Length;
        return s < slotData.Length && slotData[s] == d ? ownedCards[s] : null;
    }
    int OwnedDataOfSlot(int s) => s >= 0 && s < slotData.Length ? slotData[s] : -1;

    // 一覧の中の、束 d の左上からの位置(縮小する前の単位)
    Vector2 CellTopLeft(int d, int cols, float contentW)
    {
        Vector2 cs = CellSize, sp = CellSpacing; var pad = CellPad;
        float usedW = cols * cs.x + (cols - 1) * sp.x;
        float x0 = pad.left + Mathf.Max(0f, (contentW - pad.horizontal - usedW) * 0.5f); // 中央寄せ(GridLayoutGroup の UpperCenter と同じ)
        return new Vector2(x0 + (d % cols) * (cs.x + sp.x), pad.top + (d / cols) * (cs.y + sp.y));
    }

    // 見えている段を調べて、変わった枠だけ中身を入れ替える。force = 全部を書き直す(一覧の中身が変わった時)
    void UpdateVirtual(bool force)
    {
        if (!virtualReady) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        var c = ownedScrollRect.content;
        var vp = ownedScrollRect.viewport != null ? ownedScrollRect.viewport : (RectTransform)ownedScrollRect.transform;
        int n = displayedStacks.Count, cols = OwnedCols;
        Vector2 cs = CellSize, sp = CellSpacing; var pad = CellPad;
        float k = Mathf.Max(0.01f, Mathf.Abs(c.localScale.y));
        float pitch = cs.y + sp.y;
        int rows = (n + cols - 1) / cols;
        float h = pad.vertical + rows * cs.y + Mathf.Max(0, rows - 1) * sp.y;
        if (Mathf.Abs(c.rect.height - h) > 0.5f) c.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        float viewH = vp.rect.height / k;
        float top = Mathf.Max(0f, c.anchoredPosition.y / k - pad.top);
        int visRows = Mathf.CeilToInt(viewH / pitch) + 1;
        EnsureOwnedPool((visRows + 2) * cols);                 // 見える段 + 上下に1段ずつの予備(足りなければ増やす)
        int pool = ownedCards.Length;
        int first = Mathf.Clamp(Mathf.FloorToInt(top / pitch) - 1, 0, Mathf.Max(0, rows));
        int last = Mathf.Clamp(Mathf.CeilToInt((top + viewH) / pitch) + 1, first, rows);
        last = Mathf.Min(last, first + pool / cols);
        float contentW = c.rect.width;
        bool sameW = Mathf.Abs(contentW - vContentW) < 0.5f;
        if (!force && !ownedDirty && first == vFirstRow && last == vLastRow && cols == vCols && pool == vPool && sameW) return;
        bool all = force || ownedDirty || cols != vCols || pool != vPool || !sameW;
        if (slotWant.Length != pool) slotWant = new int[pool];
        for (int s = 0; s < pool; s++) slotWant[s] = -1;
        int dEnd = Mathf.Min(n, last * cols);
        for (int d = first * cols; d < dEnd; d++) slotWant[d % pool] = d;
        int binds = 0;
        for (int s = 0; s < pool; s++)
        {
            var card = ownedCards[s];
            if (card == null) continue;
            int d = slotWant[s];
            if (d < 0)
            {
                if (card.gameObject.activeSelf) card.gameObject.SetActive(false);
                slotData[s] = -1;
                continue;
            }
            if (!all && slotData[s] == d) continue;
            BindOwned(gm, s, d, cols, contentW);
            binds++;
        }
        vFirstRow = first; vLastRow = last; vCols = cols; vPool = pool; vContentW = contentW; ownedDirty = false;
        LastBindCount = binds; TotalBinds += binds;
    }

    void BindOwned(GameManager gm, int s, int d, int cols, float contentW)
    {
        var card = ownedCards[s];
        CardInventory.Stack stack = displayedStacks[d];
        CardDefinition def = displayedDefs[d];
        var rt = card.rect;
        Vector2 cs = CellSize;
        Vector2 tl = CellTopLeft(d, cols, contentW);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.sizeDelta = cs;
        rt.anchoredPosition = new Vector2(tl.x + rt.pivot.x * cs.x, -(tl.y + (1f - rt.pivot.y) * cs.y));
        if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
        slotData[s] = d;
        card.ShowFrontImmediate(gm.MakeOwnedCardData(def, stack.level, stack.count));
        // 「デッキに入っている」は小さなチェック印、「いま見ている(タップした)カード」は発光+拡大の選択状態(カードUI最終デザイン改修 2026-09-26)。
        // 同じカードの別のLv/合成の物がデッキにある時は灰色のチェック(2026-10-30)
        bool inDeck = deckKeySet.Contains(stack.cardId);
        card.SetInDeckMark(inDeck, !inDeck && gm.DeckHasKind(stack.cardId));
        card.SetFocused(d == focusIndex);
    }

    // 束 d が見える位置までスクロールする(見えていれば動かさない)。パッド/試験/目視確認のツアー用
    public void ScrollOwnedIntoView(int d)
    {
        if (ownedScrollRect == null || d < 0 || d >= displayedStacks.Count) return;
        var c = ownedScrollRect.content;
        var vp = ownedScrollRect.viewport != null ? ownedScrollRect.viewport : (RectTransform)ownedScrollRect.transform;
        Canvas.ForceUpdateCanvases();
        float k = Mathf.Max(0.01f, Mathf.Abs(c.localScale.y));
        float viewH = vp.rect.height / k, contentH = c.rect.height;
        float range = contentH - viewH;
        if (range <= 0f) { UpdateVirtual(false); return; }
        Vector2 tl = CellTopLeft(d, OwnedCols, c.rect.width);
        float y = c.anchoredPosition.y / k;
        float cellTop = tl.y - 8f, cellBottom = tl.y + CellSize.y + 8f;
        if (cellTop < y) y = cellTop;
        else if (cellBottom > y + viewH) y = cellBottom - viewH;
        ownedScrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(y / range);
        Canvas.ForceUpdateCanvases();
        UpdateVirtual(false);
    }

    // ===== 一覧の右の細いスクロールバー(2026-11-06、エステルのレビュー: 200〜300種類だと今どの辺りか分からない) =====
    //  いつも出す(一覧が短い時はつまみが全体の長さ)。つまみの長さ = 見えている割合、位置 = スクロールの位置。
    //  つまみ(と、その左右の少し広い範囲)を押して動かすと、その位置へ一覧を動かす(一覧の指のスクロールとは別)
    RectTransform barTrack, barThumb;
    bool barDragging;
    const float BarW = 8f, BarHitPad = 18f;

    void EnsureOwnedScrollbar()
    {
        if (barTrack != null || root == null) return;
        var rootRt = (RectTransform)root.transform;
        barTrack = NewUi("OwnedScrollbar", rootRt, new Vector2(0.5f, 1f), new Vector2(BarW, 100f), Vector2.zero);
        var ti = barTrack.gameObject.AddComponent<Image>(); ti.color = new Color(0.12f, 0.14f, 0.26f, 0.75f); ti.raycastTarget = false;
        barThumb = NewUi("Thumb", barTrack, new Vector2(0.5f, 1f), new Vector2(BarW, 40f), Vector2.zero);
        barThumb.anchorMin = new Vector2(0f, 1f); barThumb.anchorMax = new Vector2(1f, 1f); barThumb.pivot = new Vector2(0.5f, 1f);
        barThumb.offsetMin = new Vector2(0f, barThumb.offsetMin.y); barThumb.offsetMax = new Vector2(0f, barThumb.offsetMax.y);
        var hi = barThumb.gameObject.AddComponent<Image>(); hi.color = new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.95f); hi.raycastTarget = false;
    }

    // 一覧(ScrollRect)の右の外側に置く(DeckEditUI.Layout から、一覧を置いた後)
    void PlaceOwnedScrollbar()
    {
        if (ownedScrollRect == null) return;
        EnsureOwnedScrollbar();
        if (barTrack == null) return;
        var sr = (RectTransform)ownedScrollRect.transform;
        PlaceTop(barTrack, sr.anchoredPosition.x + sr.sizeDelta.x * 0.5f + BarW * 0.5f + 3f, sr.anchoredPosition.y, BarW, sr.sizeDelta.y);
        UpdateOwnedScrollbar();
    }

    void UpdateOwnedScrollbar()
    {
        if (barTrack == null || barThumb == null || ownedScrollRect == null) return;
        var c = ownedScrollRect.content;
        var vp = ownedScrollRect.viewport != null ? ownedScrollRect.viewport : (RectTransform)ownedScrollRect.transform;
        float k = Mathf.Max(0.01f, Mathf.Abs(c.localScale.y));
        float contentH = c.rect.height * k, viewH = vp.rect.height;
        float trackH = barTrack.rect.height;
        float frac = contentH > viewH + 0.5f ? Mathf.Clamp(viewH / contentH, 0.06f, 1f) : 1f;
        float thumbH = Mathf.Max(24f, trackH * frac);
        float t = frac >= 1f ? 0f : 1f - Mathf.Clamp01(ownedScrollRect.verticalNormalizedPosition);
        barThumb.sizeDelta = new Vector2(0f, thumbH);
        barThumb.anchoredPosition = new Vector2(0f, -(trackH - thumbH) * t);
    }

    // つまみを押した/動かした(DeckEditUI.Drag の UpdatePointer から)。true = スクロールバーが受けた
    bool OwnedScrollbarHit(Vector2 screenPos)
    {
        if (barTrack == null || !barTrack.gameObject.activeInHierarchy) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(barTrack, screenPos, null, out Vector2 lp)) return false;
        Rect r = barTrack.rect;
        return lp.y <= r.yMax && lp.y >= r.yMin && lp.x >= r.xMin - BarHitPad && lp.x <= r.xMax + BarHitPad;
    }

    void ScrollOwnedFromBar(Vector2 screenPos)
    {
        if (barTrack == null || ownedScrollRect == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(barTrack, screenPos, null, out Vector2 lp);
        float trackH = barTrack.rect.height, thumbH = barThumb.rect.height;
        if (trackH - thumbH <= 1f) return;
        float fromTop = barTrack.rect.yMax - lp.y; // 上端からの距離
        float t = Mathf.Clamp01((fromTop - thumbH * 0.5f) / (trackH - thumbH));
        ownedScrollRect.velocity = Vector2.zero;
        ownedScrollRect.verticalNormalizedPosition = 1f - t;
        UpdateVirtual(false); UpdateOwnedScrollbar();
    }

    public RectTransform DebugScrollbarTrack => barTrack;
    public RectTransform DebugScrollbarThumb => barThumb;
    public void DebugScrollbarDragTo(Vector2 screenPos) => ScrollOwnedFromBar(screenPos);
    public bool DebugScrollbarHit(Vector2 screenPos) => OwnedScrollbarHit(screenPos);

    // 確認用: 一覧の枠の数(部品)/ 束 d の枠(見える位置へ動かしてから)
    public int DebugOwnedPoolSize => ownedCards.Length;
    public int DebugOwnedBoundCount { get { int k = 0; foreach (int d in slotData) if (d >= 0) k++; return k; } }
    public IEnumerable<int> DebugOwnedBoundIndices() { foreach (int d in slotData) if (d >= 0) yield return d; }
    public RewardCardUI DebugOwnedCardAt(int d) { ScrollOwnedIntoView(d); return OwnedCardFor(d); }
    public int DebugOwnedTotalRows => (displayedStacks.Count + OwnedCols - 1) / OwnedCols;
    public int DebugOwnedCols => OwnedCols;
    public float DebugOwnedRowHeight => (CellSize.y + CellSpacing.y) * Mathf.Abs(ownedScrollRect.content.localScale.y); // 1段の高さ(一覧の外の単位)
}

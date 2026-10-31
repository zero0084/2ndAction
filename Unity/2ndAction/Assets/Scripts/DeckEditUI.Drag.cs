using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// デッキ編集の指/マウスの操作(2026-10-31): タップ / 長押し / ドラッグ / 一覧のスクロール を見分ける。
//  ・タップ(動かさずに離す、長押し未満): 今までどおり(所持 → デッキへ入れる、デッキ → 外す、キャラ枠)
//  ・長押し(LongPressSeconds 動かさない): 大きな詳細(所持/デッキ/キャラカード)
//  ・ドラッグ: カードを持ち上げて運ぶ。
//      タッチ: 一覧のカードは PickupSeconds 押してから動かすと持ち上がる(すぐに動かすと一覧のスクロール)。デッキのカードはすぐ持ち上がる
//      マウス: カードの上から動かせばすぐ持ち上がる(一覧のスクロールはホイール/カードの無い所)
//    所持 → デッキの空き枠/デッキの欄: 入れる / 埋まった枠: 確認して入れ替え(勝手に置き換えない) / 同じカードがデッキにある: 赤 + 案内
//    所持 → キャラカード枠: 装備(埋まっていれば確認)
//    デッキ → 一覧の上(デッキの欄の外): 外す / デッキ → デッキの別の枠: 並び替え(入れ替え。空き枠なら末尾へ)
//  ・ゆれ: 動いたとみなす距離は画面の dpi から(Android の指の太さ。最低 dragTapThreshold)
//  ・パッド/キーはドラッグしない(今までのタップ + 小さな詳細の「詳細」で大きな詳細)
public partial class DeckEditUI
{
    public const float LongPressSeconds = 0.5f;
    public const float PickupSeconds = 0.2f;
    float SlopPx => Mathf.Max(dragTapThreshold, Screen.dpi > 0f ? Screen.dpi * 0.07f : dragTapThreshold);

    enum PressKind { None, Owned, Deck, Char }
    enum Gesture { Pending, Scroll, Drag, LongPressed }
    enum DropKind { None, DeckSlot, DeckArea, CharSlot, OwnedArea }
    enum DropState { None, Add, Replace, Swap, Move, Remove, Equip, Duplicate, Denied }

    PressKind pressKind; int pressIndex; float pressTime; bool pressTouch; Gesture gesture;
    RectTransform dragGhost; CanvasGroup dragSourceGroup; RewardCardUI dragSourceCard;
    Image[] deckDropHints = new Image[0];
    Image[] charDropHints = new Image[0];
    RectTransform ownedDropRt; Text ownedDropLabel;
    DropKind hoverKind; int hoverIndex; DropState hoverState;
    int dupFlashSlot = -1;
    public static int DragsStarted { get; private set; }
    public static string LastDrop { get; private set; } = ""; // 確認用: 最後に離した時の結果

    static readonly Color HintAdd = new Color(0.35f, 1f, 0.5f, 0.42f);
    static readonly Color HintReplace = new Color(1f, 0.7f, 0.2f, 0.45f);
    static readonly Color HintDenied = new Color(1f, 0.22f, 0.18f, 0.5f);
    static readonly Color HintMove = new Color(0.4f, 0.75f, 1f, 0.42f);

    Image MakeHint(RectTransform parent, string name)
    {
        var rt = NewUi(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-4f, -4f); rt.offsetMax = new Vector2(4f, 4f);
        var img = rt.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.color = Color.clear;
        var ol = rt.gameObject.AddComponent<Outline>(); ol.effectDistance = new Vector2(3f, -3f); ol.effectColor = Color.clear;
        rt.gameObject.SetActive(false);
        return img;
    }

    void BuildDragParts()
    {
        deckDropHints = new Image[deckSlotCards.Length];
        for (int i = 0; i < deckSlotCards.Length; i++) if (deckSlotCards[i] != null) deckDropHints[i] = MakeHint(deckSlotCards[i].rect, "DropHint");
        charDropHints = new Image[characterSlotCards.Length];
        for (int i = 0; i < characterSlotCards.Length; i++) if (characterSlotCards[i] != null) charDropHints[i] = MakeHint(characterSlotCards[i].rect, "DropHint");
        if (ownedScrollRect != null)
        {
            var img = MakeHint((RectTransform)ownedScrollRect.transform, "RemoveHint");
            ownedDropRt = img.rectTransform;
            var lt = NewUi("Label", ownedDropRt, new Vector2(0.5f, 0.5f), new Vector2(700f, 80f), Vector2.zero);
            lt.pivot = new Vector2(0.5f, 0.5f);
            ownedDropLabel = lt.gameObject.AddComponent<Text>();
            ownedDropLabel.font = countText != null ? countText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UiTheme.Apply(ownedDropLabel, UiTheme.TextRole.Heading, 1f);
            ownedDropLabel.alignment = TextAnchor.MiddleCenter; ownedDropLabel.raycastTarget = false; ownedDropLabel.color = UiTheme.Text;
            ownedDropLabel.resizeTextForBestFit = true; ownedDropLabel.resizeTextMinSize = 14; ownedDropLabel.resizeTextMaxSize = 34;
        }
    }

    // ---- 押した物
    PressKind FindPressTarget(Vector2 p, out int index)
    {
        index = -1;
        var gm = GameManager.Instance;
        if (gm == null) return PressKind.None;
        for (int i = 0; i < characterSlotCards.Length; i++)
        {
            var c = characterSlotCards[i];
            if (c != null && c.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(c.rect, p, null)) { index = i; return PressKind.Char; }
        }
        for (int i = 0; i < deckSlotCards.Length && i < gm.DeckCards.Count; i++)
        {
            var c = deckSlotCards[i];
            if (c != null && c.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(c.rect, p, null)) { index = i; return PressKind.Deck; }
        }
        if (ownedScrollRect != null && RectTransformUtility.RectangleContainsScreenPoint(ownedScrollRect.viewport, p, null))
            for (int i = 0; i < ownedCards.Length && i < displayedStacks.Count; i++)
            {
                var c = ownedCards[i];
                if (c != null && c.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(c.rect, p, null)) { index = i; return PressKind.Owned; }
            }
        return PressKind.None;
    }

    RewardCardUI CardOf(PressKind k, int i) =>
        k == PressKind.Owned ? (i >= 0 && i < ownedCards.Length ? ownedCards[i] : null) :
        k == PressKind.Deck ? (i >= 0 && i < deckSlotCards.Length ? deckSlotCards[i] : null) :
        k == PressKind.Char ? (i >= 0 && i < characterSlotCards.Length ? characterSlotCards[i] : null) : null;

    void OpenBigDetailFor(PressKind k, int i)
    {
        var gm = GameManager.Instance; if (gm == null) return;
        if (k == PressKind.Owned && i >= 0 && i < displayedStacks.Count) { CardInventory.ClearNewUnconfirmed(displayedStacks[i].cardId); OpenBigDetail(displayedStacks[i].cardId, displayedStacks[i].level); Refresh(); }
        else if (k == PressKind.Deck && i >= 0 && i < gm.DeckCards.Count) OpenBigDetail(gm.DeckCards[i], LevelOfKey(gm.DeckCards[i]));
        else if (k == PressKind.Char && i >= 0 && !string.IsNullOrEmpty(gm.CharacterCardIds[i])) OpenBigDetail(gm.CharacterCardIds[i], gm.GetCharacterCardLevel(i));
    }

    // ---- 持ち上げ
    void BeginDrag(Vector2 p)
    {
        var src = CardOf(pressKind, pressIndex);
        if (src == null) { gesture = Gesture.Scroll; return; }
        gesture = Gesture.Drag;
        DragsStarted++;
        var rootRt = (RectTransform)root.transform;
        var holder = new GameObject("DragGhost", typeof(RectTransform));
        dragGhost = (RectTransform)holder.transform;
        dragGhost.SetParent(rootRt, false);
        dragGhost.anchorMin = dragGhost.anchorMax = dragGhost.pivot = new Vector2(0.5f, 0.5f);
        dragGhost.sizeDelta = src.rect.rect.size;
        float k = src.rect.lossyScale.x / Mathf.Max(0.0001f, rootRt.lossyScale.x);
        dragGhost.localScale = new Vector3(k * 1.12f, k * 1.12f, 1f);
        dragGhost.localEulerAngles = new Vector3(0f, 0f, -4f);
        // 影
        var sh = NewUi("Shadow", dragGhost, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        sh.anchorMin = Vector2.zero; sh.anchorMax = Vector2.one; sh.offsetMin = new Vector2(10f, -18f); sh.offsetMax = new Vector2(10f, -18f);
        sh.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        var copy = Instantiate(src.gameObject, dragGhost, false);
        copy.name = "Card";
        var cr = (RectTransform)copy.transform;
        cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.pivot = new Vector2(0.5f, 0.5f); cr.offsetMin = cr.offsetMax = Vector2.zero; cr.localScale = Vector3.one; cr.localEulerAngles = Vector3.zero;
        foreach (var h in copy.GetComponentsInChildren<Image>(true)) if (h.gameObject.name == "DropHint") h.gameObject.SetActive(false);
        var cg = holder.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false; cg.alpha = 0.96f;
        dragGhost.SetAsLastSibling();
        // 元のカードは薄く(どこから持ち上げたか分かるように)
        dragSourceCard = src;
        dragSourceGroup = src.GetComponent<CanvasGroup>();
        if (dragSourceGroup == null) dragSourceGroup = src.gameObject.AddComponent<CanvasGroup>();
        dragSourceGroup.alpha = 0.35f;
        MoveGhost(p);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        Debug.Log($"[DeckEdit] drag start {pressKind} #{pressIndex} ({(pressTouch ? "touch" : "mouse")})");
    }

    void MoveGhost(Vector2 p)
    {
        if (dragGhost == null) return;
        var rootRt = (RectTransform)root.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, p, null, out var local);
        float lift = pressTouch ? dragGhost.sizeDelta.y * dragGhost.localScale.y * 0.3f : 0f; // 指で隠れないよう少し上
        dragGhost.localPosition = new Vector3(local.x, local.y + lift, 0f);
    }

    void EndDrag()
    {
        if (dragGhost != null) Destroy(dragGhost.gameObject);
        dragGhost = null;
        if (dragSourceGroup != null) dragSourceGroup.alpha = 1f;
        dragSourceGroup = null; dragSourceCard = null;
        ClearDropHints();
        gesture = Gesture.Pending;
    }

    void ClearDropHints()
    {
        foreach (var h in deckDropHints) if (h != null && h.gameObject.activeSelf) h.gameObject.SetActive(false);
        foreach (var h in charDropHints) if (h != null && h.gameObject.activeSelf) h.gameObject.SetActive(false);
        if (ownedDropRt != null && ownedDropRt.gameObject.activeSelf) ownedDropRt.gameObject.SetActive(false);
        hoverKind = DropKind.None; hoverState = DropState.None; dupFlashSlot = -1;
    }

    static void SetHint(Image h, Color c)
    {
        if (h == null) return;
        if (!h.gameObject.activeSelf) h.gameObject.SetActive(true);
        h.transform.SetAsLastSibling();
        h.color = new Color(c.r, c.g, c.b, c.a * 0.55f);
        var ol = h.GetComponent<Outline>(); if (ol != null) ol.effectColor = new Color(c.r, c.g, c.b, 1f);
    }

    // ---- 置ける場所と結果
    string DragKey => pressKind == PressKind.Owned && pressIndex >= 0 && pressIndex < displayedStacks.Count ? displayedStacks[pressIndex].cardId
        : pressKind == PressKind.Deck && GameManager.Instance != null && pressIndex < GameManager.Instance.DeckCards.Count ? GameManager.Instance.DeckCards[pressIndex] : null;

    void Evaluate(Vector2 p, out DropKind kind, out int index, out DropState state)
    {
        kind = DropKind.None; index = -1; state = DropState.None;
        var gm = GameManager.Instance; if (gm == null) return;
        var deck = gm.DeckCards;
        string key = DragKey;
        for (int i = 0; i < deckSlotCards.Length; i++)
        {
            var c = deckSlotCards[i];
            if (c != null && c.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(c.rect, p, null)) { kind = DropKind.DeckSlot; index = i; break; }
        }
        if (kind == DropKind.None)
            for (int i = 0; i < characterSlotCards.Length; i++)
            {
                var c = characterSlotCards[i];
                if (c != null && c.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(c.rect, p, null)) { kind = DropKind.CharSlot; index = i; break; }
            }
        if (kind == DropKind.None && panelDeck != null && RectTransformUtility.RectangleContainsScreenPoint(panelDeck, p, null)) kind = DropKind.DeckArea;
        if (kind == DropKind.None && (pressKind == PressKind.Deck || (panelCollection != null && RectTransformUtility.RectangleContainsScreenPoint(panelCollection, p, null)))) kind = DropKind.OwnedArea;
        if (key == null) return;

        if (pressKind == PressKind.Owned)
        {
            int same = gm.DeckIndexOfKind(key);
            if (kind == DropKind.DeckSlot || kind == DropKind.DeckArea)
            {
                bool onFilled = kind == DropKind.DeckSlot && index < deck.Count;
                if (onFilled && same == index) state = deck[index] == key ? DropState.Denied : DropState.Replace; // 同じカードの Lv 違い → 確認して替える
                else if (same >= 0) { state = DropState.Duplicate; dupFlashSlot = same; }
                else if (onFilled) state = DropState.Replace;
                else if (deck.Count >= GameManager.DeckCapacity) state = DropState.Denied;
                else { state = DropState.Add; if (kind == DropKind.DeckArea || index > deck.Count) index = deck.Count; kind = DropKind.DeckSlot; }
            }
            else if (kind == DropKind.CharSlot) state = DropState.Equip;
        }
        else if (pressKind == PressKind.Deck)
        {
            if (kind == DropKind.DeckSlot) state = index == pressIndex ? DropState.None : index < deck.Count ? DropState.Swap : DropState.Move;
            else if (kind == DropKind.CharSlot) state = DropState.Denied;
            else if (kind == DropKind.DeckArea) state = DropState.None;
            else { kind = DropKind.OwnedArea; state = DropState.Remove; }
        }
    }

    void UpdateDrag(Vector2 p)
    {
        MoveGhost(p);
        dupFlashSlot = -1;
        Evaluate(p, out var kind, out int index, out var state);
        hoverKind = kind; hoverIndex = index; hoverState = state;
        for (int i = 0; i < deckDropHints.Length; i++)
        {
            Color? c = null;
            if (kind == DropKind.DeckSlot && i == index)
                c = state == DropState.Add ? HintAdd : state == DropState.Replace ? HintReplace : state == DropState.Swap || state == DropState.Move ? HintMove : state == DropState.Duplicate || state == DropState.Denied ? HintDenied : (Color?)null;
            if (i == dupFlashSlot) c = new Color(1f, 0.22f, 0.18f, 0.35f + 0.35f * Mathf.PingPong(Time.unscaledTime * 4f, 1f));
            if (c.HasValue) SetHint(deckDropHints[i], c.Value);
            else if (deckDropHints[i] != null && deckDropHints[i].gameObject.activeSelf) deckDropHints[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < charDropHints.Length; i++)
        {
            if (kind == DropKind.CharSlot && i == index && state != DropState.None) SetHint(charDropHints[i], state == DropState.Equip ? HintAdd : HintDenied);
            else if (charDropHints[i] != null && charDropHints[i].gameObject.activeSelf) charDropHints[i].gameObject.SetActive(false);
        }
        bool showRemove = pressKind == PressKind.Deck;
        if (ownedDropRt != null)
        {
            if (showRemove)
            {
                SetHint(ownedDropRt.GetComponent<Image>(), state == DropState.Remove ? HintReplace : new Color(1f, 0.7f, 0.2f, 0.18f));
                if (ownedDropLabel != null) ownedDropLabel.text = Loc.T(LocDeckEdit.DropToRemove);
            }
            else if (ownedDropRt.gameObject.activeSelf) ownedDropRt.gameObject.SetActive(false);
        }
    }

    void Drop(Vector2 p)
    {
        var gm = GameManager.Instance;
        string key = DragKey;
        Evaluate(p, out var kind, out int index, out var state);
        PressKind from = pressKind; int fromIndex = pressIndex;
        EndDrag();
        if (gm == null || key == null) return;
        LastDrop = $"{from}->{kind}#{index}:{state}";
        Debug.Log($"[DeckEdit] drop {from} #{fromIndex} ({key}) on {kind} #{index}: {state}");
        var deck = new List<string>(gm.DeckCards);
        switch (state)
        {
            case DropState.Add:
            {
                bool ok = gm.AddToDeck(key);
                if (!ok) ExplainAddFail(key);
                else if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                if (from == PressKind.Owned) { var c = CardDatabase.FindById(key); if (c != null) { ShowDetail(c, displayedStacks[fromIndex].level, displayedStacks[fromIndex].count); focusCardId = key; focusLevel = displayedStacks[fromIndex].level; } }
                Refresh();
                break;
            }
            case DropState.Replace:
            {
                if (index < 0 || index >= deck.Count) break;
                string oldKey = deck[index];
                var oc = CardDatabase.FindById(oldKey); var nc = CardDatabase.FindById(key);
                string msg = Loc.F(LocDeckEdit.ReplaceConfirm, Loc.Auto(oc != null ? oc.cardName : oldKey), Loc.Auto(nc != null ? nc.cardName : key));
                int at = index;
                System.Action doIt = () =>
                {
                    var cur = new List<string>(gm.DeckCards);
                    if (at >= cur.Count || cur[at] != oldKey) return;
                    cur[at] = key;
                    gm.SetDeck(cur);
                    bool placed = at < gm.DeckCards.Count && gm.DeckCards[at] == key;
                    if (!placed) { gm.SetDeck(new List<string>(deck)); ShowToast(Loc.T(LocDeckEdit.NoFreeCopy)); } // 入らなかった時は元のデッキへ
                    Debug.Log($"[DeckEdit] replace slot {at}: {oldKey} -> {key} ({(placed ? "ok" : "refused, restored")})");
                    Refresh();
                };
                if (confirmDialog != null) confirmDialog.Show(msg, doIt); else doIt();
                break;
            }
            case DropState.Swap:
            case DropState.Move:
            {
                if (fromIndex < 0 || fromIndex >= deck.Count) break;
                if (state == DropState.Swap && index < deck.Count) { string t = deck[index]; deck[index] = deck[fromIndex]; deck[fromIndex] = t; }
                else { deck.RemoveAt(fromIndex); deck.Add(key); }
                gm.SetDeck(deck);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
                Refresh();
                break;
            }
            case DropState.Remove:
            {
                var c = CardDatabase.FindById(key);
                bool removed = gm.RemoveFromDeck(key);
                if (removed && AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
                if (removed && ownedScrollRect != null && c != null && fromIndex < deckSlotCards.Length) StartCoroutine(PlayCardFly(deckSlotCards[fromIndex].rect, ownedScrollRect.viewport, c.icon));
                Refresh();
                break;
            }
            case DropState.Equip:
            {
                if (from != PressKind.Owned || fromIndex < 0 || fromIndex >= displayedStacks.Count) break;
                var st = displayedStacks[fromIndex];
                int slot = index;
                System.Action doIt = () =>
                {
                    gm.EquipCharacterCard(slot, st.cardId, st.level);
                    pendingEquipSlot = -1;
                    Refresh();
                    if (!string.IsNullOrEmpty(gm.LastEquipMessage)) ShowToast(gm.LastEquipMessage);
                };
                string cur = gm.CharacterCardIds[slot];
                if (!string.IsNullOrEmpty(cur) && confirmDialog != null)
                {
                    var oc = CardDatabase.FindById(cur); var nc = CardDatabase.FindById(st.cardId);
                    confirmDialog.Show(Loc.F(LocDeckEdit.ReplaceConfirm, Loc.Auto(oc != null ? oc.cardName : cur), Loc.Auto(nc != null ? nc.cardName : st.cardId)), doIt);
                }
                else doIt();
                break;
            }
            case DropState.Duplicate:
                if (from == PressKind.Owned && fromIndex >= 0 && fromIndex < displayedStacks.Count)
                {
                    var dc = CardDatabase.FindById(key); // 案内は運んだカードの詳細の下に出す(何も選んでいない時の案内の上に重ねない)
                    if (dc != null) { ShowDetail(dc, displayedStacks[fromIndex].level, displayedStacks[fromIndex].count); focusCardId = key; focusLevel = displayedStacks[fromIndex].level; Refresh(); }
                }
                ShowAlreadyInDeck(key);
                ShowToast(Loc.T(Loc.Reviewed(LocPendingReview.DeckAlreadyIn, LocPendingReview.DeckAlreadyInOld)));
                break;
            case DropState.Denied:
                if (from == PressKind.Owned && gm.DeckCards.Count >= GameManager.DeckCapacity) ShowToast(Loc.T(LocDeckEdit.DeckFull));
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
                break;
        }
    }

    void ExplainAddFail(string key)
    {
        var gm = GameManager.Instance; if (gm == null) return;
        switch (gm.LastDeckAddResult)
        {
            case GameManager.DeckAddResult.SameCardInDeck: ShowAlreadyInDeck(key); ShowToast(Loc.T(Loc.Reviewed(LocPendingReview.DeckAlreadyIn, LocPendingReview.DeckAlreadyInOld))); break;
            case GameManager.DeckAddResult.Full: ShowToast(Loc.T(LocDeckEdit.DeckFull)); break;
            case GameManager.DeckAddResult.NoFreeCopy: ShowToast(Loc.T(LocDeckEdit.NoFreeCopy)); break;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ---- 自動試験(-qaDeckV2): 本物の指の代わりに、押す → 持ち上げる → 動かす → 離す を同じ処理で通す
    static Vector2 ScreenCenter(RectTransform r) => r == null ? Vector2.zero : RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));
    public Vector2 DebugDeckSlotPos(int i) => i >= 0 && i < deckSlotCards.Length ? ScreenCenter(deckSlotCards[i].rect) : Vector2.zero;
    public Vector2 DebugCharSlotPos(int i) => i >= 0 && i < characterSlotCards.Length ? ScreenCenter(characterSlotCards[i].rect) : Vector2.zero;
    public Vector2 DebugOwnedPos(int i) => i >= 0 && i < ownedCards.Length ? ScreenCenter(ownedCards[i].rect) : Vector2.zero;
    public Vector2 DebugOwnedAreaPos => ownedScrollRect != null ? ScreenCenter(ownedScrollRect.viewport) : Vector2.zero;
    public Vector2 DebugDeckAreaPos => panelDeck != null ? ScreenCenter(panelDeck) : Vector2.zero;
    public int DebugOwnedIndex(string key) { for (int i = 0; i < displayedStacks.Count; i++) if (displayedStacks[i].cardId == key) return i; return -1; }
    public RectTransform DebugDeckSlotRect(int i) => deckSlotCards[i].rect;
    public RectTransform DebugCharSlotRect(int i) => characterSlotCards[i].rect;
    public RectTransform DebugOwnedViewport => ownedScrollRect != null ? ownedScrollRect.viewport : null;
    public RectTransform DebugDeckPanel => panelDeck;
    public RectTransform DebugDetailPanel => detailPanelRt;
    public bool DebugGhostShown => dragGhost != null;
    public string DebugHoverState => $"{hoverKind}#{hoverIndex}:{hoverState}";

    // fromDeck = デッキの枠 index から、でなければ一覧の index から。to へ steps フレームかけて運び、hold 秒止めて(撮影用)離す
    public IEnumerator DebugDrag(bool fromDeck, int index, Vector2 to, int steps = 8, float hold = 0f, System.Action whileHeld = null)
    {
        pressKind = fromDeck ? PressKind.Deck : PressKind.Owned; pressIndex = index;
        pressTouch = false; pressTime = Time.unscaledTime; pointerActive = false;
        Vector2 from = fromDeck ? DebugDeckSlotPos(index) : DebugOwnedPos(index);
        BeginDrag(from);
        for (int s = 1; s <= steps; s++) { UpdateDrag(Vector2.Lerp(from, to, s / (float)steps)); yield return null; }
        whileHeld?.Invoke();
        if (hold > 0f) { float t = 0f; while (t < hold) { UpdateDrag(to); yield return null; t += Time.unscaledDeltaTime; } }
        Drop(to);
        pressKind = PressKind.None;
    }
    public void DebugLongPress(bool fromDeck, bool fromChar, int index) => OpenBigDetailFor(fromChar ? PressKind.Char : fromDeck ? PressKind.Deck : PressKind.Owned, index);
#endif

    // ---- 1フレームごとの指の処理(Update から)。true = この画面の指の処理を終えた
    void UpdatePointer()
    {
        PointerInput.Read(this, out bool down, out bool up, out bool held, out Vector2 screenPos);

        // 確認の窓: 離した時に窓だけが受ける
        if (confirmDialog != null && confirmDialog.IsOpen)
        {
            if (gesture == Gesture.Drag) EndDrag();
            pointerActive = false;
            if (up) confirmDialog.HandleTap(screenPos);
            return;
        }
        // 大きな詳細: 窓だけが受ける(中の説明は指でスクロールできる)
        if (BigDetailOpen)
        {
            if (gesture == Gesture.Drag) EndDrag();
            if (down) { pointerActive = true; pointerDownScreenPos = screenPos; draggedPastThreshold = false; gesture = Gesture.Pending; pressKind = PressKind.None; return; }
            if (!pointerActive) return;
            if (held) { if (Vector2.Distance(screenPos, pointerDownScreenPos) > SlopPx) draggedPastThreshold = true; return; }
            if (up) { pointerActive = false; if (!draggedPastThreshold && gesture != Gesture.LongPressed) HandleBigTap(pointerDownScreenPos); }
            return;
        }

        if (down)
        {
            pointerActive = true;
            pointerDownScreenPos = screenPos;
            draggedPastThreshold = false;
            gesture = Gesture.Pending;
            pressTime = Time.unscaledTime;
            pressTouch = Input.touchCount > 0;
            pressKind = FindPressTarget(screenPos, out pressIndex);
            activeScrollRect = FindScrollRectContaining(screenPos);
            if (activeScrollRect != null) RectTransformUtility.ScreenPointToLocalPointInRectangle(activeScrollRect.viewport, screenPos, null, out lastViewportLocalPos);
            return;
        }
        if (!pointerActive) return;

        if (held)
        {
            float t = Time.unscaledTime - pressTime;
            bool moved = Vector2.Distance(screenPos, pointerDownScreenPos) > SlopPx;
            if (gesture == Gesture.Pending)
            {
                if (!moved && pressKind != PressKind.None && t >= LongPressSeconds)
                {
                    gesture = Gesture.LongPressed;
                    OpenBigDetailFor(pressKind, pressIndex);
                    Platform.Haptics?.Play(HapticKind.Light);
                    return;
                }
                if (moved)
                {
                    draggedPastThreshold = true;
                    bool canDrag = pressKind == PressKind.Deck || (pressKind == PressKind.Owned && (!pressTouch || t >= PickupSeconds));
                    if (canDrag) BeginDrag(screenPos);
                    else gesture = Gesture.Scroll;
                }
                else if (pressTouch && pressKind == PressKind.Owned && t >= PickupSeconds)
                {
                    var c = CardOf(pressKind, pressIndex); // 持ち上げられる合図(少し大きく)
                    if (c != null) c.rect.localScale = Vector3.one * 1.05f;
                }
            }
            if (gesture == Gesture.Drag) { UpdateDrag(screenPos); return; }
            if (gesture == Gesture.Scroll && activeScrollRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(activeScrollRect.viewport, screenPos, null, out Vector2 currentLocalPos);
                float deltaY = currentLocalPos.y - lastViewportLocalPos.y;
                lastViewportLocalPos = currentLocalPos;
                float range = activeScrollRect.content.rect.height * Mathf.Abs(activeScrollRect.content.localScale.y) - activeScrollRect.viewport.rect.height;
                if (range > 0f) activeScrollRect.verticalNormalizedPosition -= deltaY / range;
            }
            return;
        }

        if (up)
        {
            pointerActive = false;
            var pc = CardOf(pressKind, pressIndex); if (pc != null && pressKind == PressKind.Owned) pc.rect.localScale = Vector3.one;
            if (gesture == Gesture.Drag) Drop(screenPos);
            else if (gesture == Gesture.Pending && !draggedPastThreshold && !UguiScrollText.PointerCaptured) HandleTap(pointerDownScreenPos);
            gesture = Gesture.Pending;
            activeScrollRect = null;
            return;
        }

        // 押していたのに、離した知らせが無いまま指が消えた(フォーカスが外れた等): 何もせずに取り消す
        pointerActive = false;
        if (gesture == Gesture.Drag) EndDrag();
        gesture = Gesture.Pending;
    }
}

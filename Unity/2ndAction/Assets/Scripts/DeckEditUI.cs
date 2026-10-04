using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// The Card Edit screen (renamed in spirit, not in code, from the Home Room
// UI reconstruction pass - reached by tapping the bed/scattered cards on the
// TOP room): a left COLLECTION panel of every OWNED (cardId, level) stack
// (item 6 - "プレイヤーが実際に何枚所有しているか" replaces the old
// unlock-flag-based "every card in the database" view) and a right DECK
// panel of the DeckCapacity slots currently filled by the deck. Also owns
// the 3 Character Card slots (item 7) and Convert-to-MILE (item 9), both
// added this pass. Reuses RewardCardUI/RewardCardData exactly as the
// level-up card sequence does (SceneBuilder.CreateRewardCard builds these
// cards too).
//
// Deliberately does NOT use Button.onClick/EventSystem for its taps -
// every interaction here is a toggle (add/remove), and firing both an
// EventSystem click and a raw-touch fallback for the same physical tap
// (the "belt-and-suspenders" approach RewardCardSequence uses for its
// one-shot pick) would add-then-immediately-remove a card. Since
// RewardCardSequence's own comments note the EventSystem/Button pipeline
// isn't always reliably delivered on this project's target devices, this
// screen uses TouchInputUtil's raw hit-test as its ONLY input path instead.
public class DeckEditUI : MonoBehaviour
{
    public GameObject root;
    public CanvasGroup rootGroup;
    public RectTransform backButtonRect;
    // Tunable per Polish Pass 1's "keep transitions short, don't hurt input
    // responsiveness" guidance - short enough to not feel like a load, long
    // enough to read as an intentional transition rather than a flash.
    public float fadeDuration = 0.16f;

    // Left panel - one slot per OWNED (cardId, level) stack, tap to add to
    // the deck (or to fill an armed Character Card slot - see
    // pendingEquipSlot).
    public RewardCardUI[] ownedCards = new RewardCardUI[0];
    // Right panel - fixed GameManager.DeckCapacity slots, filled in deck
    // order; unused slots are just deactivated. Tap a filled slot to
    // remove that card from the deck.
    public RewardCardUI[] deckSlotCards = new RewardCardUI[0];
    public Text countText;
    // "COLLECTION (n)" - n is how many distinct owned (cardId, level)
    // stacks currently exist (not a count of all 16 cards - see Refresh).
    public Text collectionCountText;
    // Center detail column - card icon/name/category/effect for whichever
    // card was most recently tapped in either side panel (see the
    // reference mockup's COLLECTION -> detail -> DECK layout). The grid
    // cards themselves are only 130px wide, too small to read comfortably
    // at a glance.
    public Image detailIcon;
    public Text detailName;
    public Text detailCategory;
    public Text detailText;
    // What detailText shows before anything's been tapped yet this Open().
    public string detailPlaceholder = "カードをタップして詳細を確認";
    // カードUI最終デザイン改修(2026-09-26) - 中央パネルを「カード詳細」として使う:
    // 大きめのカードプレビュー(一覧と同じRewardCardUI) / Card Name / Category・Lv /
    // 主な効果(Main Value、合成カードはSUBも) / 効果説明。未選択時はplaceholderLabelだけ。
    // 値は全てCardDefinition/CardVariant/CardEffectFormatから組み立てる(ハードコードしない)。
    public RewardCardUI detailPreviewCard;
    public Text detailValue;
    public Text detailPlaceholderLabel;
    public GameObject detailDivider;
    // いま見ているカード(一覧で選択状態として光らせる)。focusLevel=-1はDeck側から選んだ
    // 場合 - 同じcardIdのCollectionの束(最初の1つ)を選択状態にする。
    string focusCardId;
    int focusLevel = -1;

    // Home Room UI reconstruction pass, item 9 - CONVERT only shows/works
    // when the current detail selection came from a COLLECTION stack (a
    // Deck-slot tap doesn't know which specific level it holds - Deck
    // itself is level-agnostic - so CONVERT is unavailable from there).
    public RectTransform convertButtonRect;
    public Text convertButtonLabel;
    string detailCardId;
    int detailLevel = -1;
    public const int CardConvertMile = 100;

    // Item 7 - Character Card slots (max 3). Tapping an empty slot arms it
    // (pendingEquipSlot) so the NEXT COLLECTION tap fills it, overriding
    // the normal "add to deck" behavior for exactly that one tap - same
    // pattern the old combined CardMenuUI used. Tapping a filled slot
    // unequips it immediately.
    public RewardCardUI[] characterSlotCards = new RewardCardUI[0];
    int pendingEquipSlot = -1;

    // Shared "magic circle" glow for Convert (item 9's "以前用意した「不
    // 要カード→MILE変換演出」を使用可能なら適用してください") - same
    // tintable ring pattern CardFusionUI uses (Assets/Art/Effects/
    // DoubleJumpRing.png), just a single gold flash here (no card reveal
    // needed - nothing new is obtained, just MILE).
    public Image magicCircleImage;

    List<CardInventory.Stack> displayedStacks = new List<CardInventory.Stack>();

    // The two ScrollRects (COLLECTION/DECK) - scrolled manually via raw
    // touch drag in Update() below rather than relying on ScrollRect's own
    // EventSystem-driven dragging, for the same reliability reason every
    // other input on this screen bypasses EventSystem (see the class
    // comment). Distance-unlock content keeps growing the COLLECTION grid
    // past what fits on screen without this.
    public ScrollRect ownedScrollRect;
    public ScrollRect deckScrollRect;
    // Total pointer movement (screen pixels) below which a release still
    // counts as a tap rather than a scroll/drag.
    public float dragTapThreshold = 14f;

    // ALL/ATTACK/DEFENSE/SUPPORT/SPECIAL filter row under COLLECTION (see
    // SceneBuilder.BuildCategoryFilterTabs) - arrays are parallel (index i
    // in all three refers to the same tab). Only narrows which COLLECTION
    // slots are visible; the "n / m" count above it still counts every
    // unlocked card regardless of the active filter (matching the
    // reference mockup, where the count doesn't change per tab).
    public RectTransform[] filterButtonRects = new RectTransform[0];
    public Image[] filterButtonBackgrounds = new Image[0];
    public Text[] filterButtonLabels = new Text[0];

    // "おすすめ編成" / "全て外す" (DECK panel only) and the shared yes/no
    // confirm modal both actions go through - see ConfirmDialogUI and
    // OnRecommendTapped/OnClearAllTapped.
    public RectTransform recommendButtonRect;
    public RectTransform clearButtonRect;
    public ConfirmDialogUI confirmDialog;
    static readonly string[] FilterNames = { "ALL", "ATTACK", "DEFENSE", "SUPPORT", "SPECIAL" };
    static readonly Color FilterActiveColor = new Color(0.83f, 0.68f, 0.32f, 0.95f);
    static readonly Color FilterInactiveColor = new Color(0.08f, 0.09f, 0.16f, 0.85f);
    static readonly Color FilterActiveTextColor = new Color(0.08f, 0.06f, 0.02f);
    string activeFilter = "ALL";

    // Maps the reference mockup's 5 filter buckets onto the existing
    // CardCategory enum (Movement/Attack/Defense/Growth/Heal/Special/Risk) -
    // ATTACK/DEFENSE map 1:1, SUPPORT groups the utility-ish categories,
    // SPECIAL groups Special+Risk. No new data/enum needed per the brief's
    // "don't invent fields just for this screen" instruction.
    static bool MatchesFilter(CardCategory category, string filter)
    {
        switch (filter)
        {
            case "ATTACK": return category == CardCategory.Attack;
            case "DEFENSE": return category == CardCategory.Defense;
            case "SUPPORT": return category == CardCategory.Movement || category == CardCategory.Growth || category == CardCategory.Heal;
            case "SPECIAL": return category == CardCategory.Special || category == CardCategory.Risk;
            default: return true; // "ALL"
        }
    }

    void RefreshFilterHighlight()
    {
        for (int i = 0; i < filterButtonBackgrounds.Length && i < FilterNames.Length; i++)
        {
            bool active = FilterNames[i] == activeFilter;
            if (filterButtonBackgrounds[i] != null) filterButtonBackgrounds[i].color = active ? FilterActiveColor : FilterInactiveColor;
            if (i < filterButtonLabels.Length && filterButtonLabels[i] != null) filterButtonLabels[i].color = active ? FilterActiveTextColor : Color.white;
        }
    }

    // キャラ選択の「カード設定」から: そのキャラのキャラカード枠を開く(2026-10-02)
    public void OpenForCharacter(string characterId)
    {
        if (GameManager.Instance != null) GameManager.Instance.SetCharacterCardOwner(characterId);
        Open();
    }

    // ===== キャラごとのキャラカード枠(2026-10-02): 見出しを「◀ ○○のキャラカード ▶」にして、ここでキャラを切り替える =====
    Text charHeader;
    RectTransform charPrevRect, charNextRect;
    void EnsureCharHeader()
    {
        if (charHeader != null || root == null) return;
        Transform t = root.transform.Find("CharacterCardsHeader");
        if (t == null) return;
        charHeader = t.GetComponent<Text>();
        var hr = (RectTransform)t;
        hr.sizeDelta = new Vector2(Mathf.Max(hr.sizeDelta.x, 300f), 34f);
        if (charHeader != null) { charHeader.resizeTextForBestFit = true; charHeader.resizeTextMinSize = 12; charHeader.resizeTextMaxSize = 20; }
        charPrevRect = MakeCharArrow(hr, "CharCardsPrev", "◀", -1);
        charNextRect = MakeCharArrow(hr, "CharCardsNext", "▶", +1);
    }
    RectTransform MakeCharArrow(RectTransform header, string name, string label, int dir)
    {
        var go = new GameObject(name);
        go.transform.SetParent(header.parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = r.anchorMax = header.anchorMin;
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(58f, 40f);
        r.anchoredPosition = header.anchoredPosition + new Vector2(dir * (header.sizeDelta.x * 0.5f + 34f), 4f);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.16f, 0.75f);
        bg.raycastTarget = false;
        var lgo = new GameObject("Label");
        lgo.transform.SetParent(go.transform, false);
        var lr = lgo.AddComponent<RectTransform>();
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
        var txt = lgo.AddComponent<Text>();
        txt.font = charHeader != null ? charHeader.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 24; txt.fontStyle = FontStyle.Bold; txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(1f, 0.85f, 0.4f); txt.text = label; txt.raycastTarget = false;
        return r;
    }
    void CycleCharacterCardOwner(int dir)
    {
        var gm = GameManager.Instance; var all = CharacterDatabase.AllCharacters;
        if (gm == null || all.Count == 0) return;
        int i = 0;
        for (int k = 0; k < all.Count; k++) if (all[k].characterId == gm.CharacterCardOwnerId) i = k;
        i = (i + dir + all.Count) % all.Count;
        gm.SetCharacterCardOwner(all[i].characterId);
        pendingEquipSlot = -1;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        Refresh();
    }
    void RefreshCharHeader()
    {
        EnsureCharHeader();
        var gm = GameManager.Instance;
        if (charHeader == null || gm == null) return;
        var def = CharacterDatabase.FindById(gm.CharacterCardOwnerId);
        charHeader.text = $"{(def != null ? def.displayName : "?")} のキャラカード";
    }
    // 自動テスト用
    public string CharHeaderText => charHeader != null ? charHeader.text : "";
    public void DebugCycleCharacter(int dir) => CycleCharacterCardOwner(dir);

    public void Open()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.DeckEdit);
        if (root != null) root.SetActive(true);
        ResetDetail();
        activeFilter = "ALL";
        pendingEquipSlot = -1;
        RefreshFilterHighlight();
        Refresh();
        // Both grids must always open scrolled to the very top - forced
        // again here (not just once at scene-build time in
        // SceneBuilder.BuildDeckPanel) since a previous visit to this
        // screen may have left either one scrolled partway down.
        if (ownedScrollRect != null) ownedScrollRect.verticalNormalizedPosition = 1f;
        if (deckScrollRect != null) deckScrollRect.verticalNormalizedPosition = 1f;
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            StartCoroutine(FadeGroupTo(1f));
        }
    }

    // Androidの戻る(2026-10-01): 確認ダイアログが出ていればそれを閉じる、無ければ画面を閉じる
    public void HandleBack()
    {
        if (confirmDialog != null && confirmDialog.IsOpen) { confirmDialog.Cancel(); return; }
        Close();
    }

    public void Close()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
        // Presentation pass - DECK->TOP now goes through the shared wipe
        // (see ScreenTransitionManager); this screen's own CanvasGroup
        // cross-fade below is skipped in that case (root.SetActive(false)
        // fires only once the navy mask is 100% covering, not concurrently
        // with a separate fade) rather than layering two fades at once.
        // Falls back to the old fade-out if a scene was built before that
        // manager existed.
        if (ScreenTransitionManager.Instance != null && !ScreenTransitionManager.Instance.IsTransitioning)
        {
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                if (root != null) root.SetActive(false);
                if (rootGroup != null) rootGroup.alpha = 1f; // reset so a future Open() (skipped under full cover) starts fresh
                if (GameManager.Instance != null) GameManager.Instance.CloseDeckEdit();
            }, ScreenTransitionManager.Style.Fade);
            return;
        }

        if (rootGroup != null)
        {
            StartCoroutine(CloseAfterFade());
        }
        else
        {
            if (root != null) root.SetActive(false);
            if (GameManager.Instance != null) GameManager.Instance.CloseDeckEdit();
        }
    }

    IEnumerator FadeGroupTo(float target)
    {
        float start = rootGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeDuration);
            rootGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    IEnumerator CloseAfterFade()
    {
        yield return FadeGroupTo(0f);
        if (root != null) root.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.CloseDeckEdit();
    }

    void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        var deck = gm.DeckCards;

        // Home Room UI reconstruction pass, item 6 - COLLECTION is now
        // every OWNED (cardId, level) stack (CardInventory), not "every
        // card in the database with an unlock flag". Sorted for a stable/
        // predictable layout (CardDatabase's own sortOrder, then ascending
        // level) - same convention CardFusionUI's owned list uses.
        bool focusFound = false;
        displayedStacks.Clear();
        displayedStacks.AddRange(CardInventory.Stacks);
        displayedStacks.Sort((a, b) =>
        {
            CardDefinition ca = CardDatabase.FindById(a.cardId);
            CardDefinition cb = CardDatabase.FindById(b.cardId);
            int byOrder = (ca != null ? ca.sortOrder : 0).CompareTo(cb != null ? cb.sortOrder : 0);
            return byOrder != 0 ? byOrder : a.level.CompareTo(b.level);
        });

        for (int i = 0; i < ownedCards.Length; i++)
        {
            if (i >= displayedStacks.Count || displayedStacks[i].count <= 0)
            {
                ownedCards[i].gameObject.SetActive(false);
                continue;
            }
            CardInventory.Stack stack = displayedStacks[i];
            CardDefinition card = CardDatabase.FindById(stack.cardId);
            if (card == null) { ownedCards[i].gameObject.SetActive(false); continue; }

            // The active category filter only hides/shows slots - it never
            // affects the "(n)" count above (see the field comment on
            // filterButtonRects).
            if (!MatchesFilter(card.category, activeFilter))
            {
                ownedCards[i].gameObject.SetActive(false);
                continue;
            }

            ownedCards[i].gameObject.SetActive(true);
            ownedCards[i].ShowFrontImmediate(gm.MakeOwnedCardData(card, stack.level, stack.count));
            bool inDeck = false;
            for (int d = 0; d < deck.Count; d++)
            {
                if (deck[d] == stack.cardId) { inDeck = true; break; }
            }
            // カードUI最終デザイン改修(2026-09-26) - 「デッキに入っている」は小さなチェック印、
            // 「いま見ている(タップした)カード」は発光+拡大の選択状態、と役割を分けた
            // (以前はどちらも同じ金色の枠で、選択状態との差が分かりにくかった)。
            ownedCards[i].SetInDeckMark(inDeck);
            bool isFocus = !focusFound && focusCardId != null && stack.cardId == focusCardId && (focusLevel < 0 || stack.level == focusLevel);
            if (isFocus) focusFound = true;
            ownedCards[i].SetFocused(isFocus);
        }

        for (int i = 0; i < deckSlotCards.Length; i++)
        {
            CardDefinition card = i < deck.Count ? CardDatabase.FindById(deck[i]) : null;
            if (card != null)
            {
                deckSlotCards[i].gameObject.SetActive(true);
                deckSlotCards[i].ShowFrontImmediate(gm.MakeCardData(card));
                // Deck側は全てデッキ内なのでチェック印は不要。表示ルール自体はCollectionと同じ部品。
                deckSlotCards[i].SetInDeckMark(false);
                deckSlotCards[i].SetFocused(false);
            }
            else
            {
                // Kept ACTIVE (not deactivated) so an empty slot still
                // reads as "a slot exists here, just unfilled" per the
                // reference mockup's numbered empty-slot placeholders,
                // instead of just vanishing - OnDeckSlotTapped already
                // no-ops safely for index >= deck.Count either way.
                deckSlotCards[i].gameObject.SetActive(true);
                deckSlotCards[i].ShowEmpty();
            }
        }

        // Item 7 - Character Card slots.
        RefreshCharHeader();
        for (int i = 0; i < characterSlotCards.Length && i < GameManager.CharacterCardSlotCount; i++)
        {
            string id = gm.CharacterCardIds[i];
            CardDefinition card = string.IsNullOrEmpty(id) ? null : CardDatabase.FindById(id);
            if (card != null)
            {
                characterSlotCards[i].gameObject.SetActive(true);
                characterSlotCards[i].ShowFrontImmediate(gm.MakeOwnedCardData(card, gm.GetCharacterCardLevel(i), CardInventory.GetTotalCount(id), equipped: true));
                characterSlotCards[i].SetInDeckMark(false);
                characterSlotCards[i].SetFocused(i == pendingEquipSlot);
            }
            else
            {
                characterSlotCards[i].gameObject.SetActive(true);
                characterSlotCards[i].ShowEmpty();
                // ブラッシュアップ点検(2026-09-18)で発覚した不具合の修正:
                // 空スロットに対しても無条件でSetSelected(i==pendingEquip
                // Slot)を呼んでいたため、装備待ち状態でない空スロットまで
                // SetSelected(false)がShowEmpty()の暗い枠(alpha0.32)を
                // 不透明な白(FrameNormalColor)へ上書きしてしまい、Deck欄の
                // 空スロットと違って明るく浮いて見えていた。装備待ち
                // (ゴールド発光)にする必要がある時だけSetSelected(true)を
                // 呼び、それ以外はShowEmpty()の暗さをそのまま維持する。
                if (i == pendingEquipSlot) characterSlotCards[i].SetFocused(true);
            }
        }

        if (countText != null)
        {
            countText.text = $"DECK {deck.Count} / {GameManager.DeckCapacity}";
        }
        if (collectionCountText != null)
        {
            // カード長期育成(2026-10-04): 育てた証(MAX / AWAKENED の総数。カードの総数は CardDatabase から)
            int total = CardMastery.TotalCards;
            collectionCountText.horizontalOverflow = HorizontalWrapMode.Overflow;
            collectionCountText.supportRichText = true;
            collectionCountText.text = $"COLLECTION ({displayedStacks.Count})  <size=17><color=#ffe6a0>MAX {CardMastery.MaxCount}/{total}  AWAKENED {CardMastery.AwakenedCount}/{total}</color></size>";
        }

        RefreshConvertButton();
    }

    // Item 9 - CONVERT only appears (and only works) when the current
    // detail selection came from a COLLECTION stack (detailLevel >= 1);
    // a Deck-slot-originated selection sets detailLevel back to -1 since
    // Deck itself doesn't know which specific level it holds.
    void RefreshConvertButton()
    {
        bool hasSelection = detailLevel >= 1 && !string.IsNullOrEmpty(detailCardId);
        if (convertButtonRect != null) convertButtonRect.gameObject.SetActive(hasSelection);
        if (!hasSelection) return;

        var gm = GameManager.Instance;
        bool inUse = gm != null && gm.IsCardInUse(detailCardId);
        Image bg = convertButtonRect.GetComponent<Image>();
        if (bg != null) bg.color = inUse ? new Color(0.25f, 0.25f, 0.28f, 0.85f) : new Color(0.55f, 0.42f, 0.14f, 0.9f);
        if (convertButtonLabel != null)
        {
            convertButtonLabel.text = inUse ? "使用中のため変換不可" : $"CONVERT (+{CardConvertMile} MILE)";
        }
    }

    // Clears the center detail column back to its placeholder state - a
    // card selected in a PREVIOUS visit to this screen shouldn't still be
    // showing when it's reopened.
    void ResetDetail()
    {
        if (detailIcon != null)
        {
            if (detailIcon.sprite != null) Destroy(detailIcon.sprite);
            detailIcon.sprite = null;
            detailIcon.enabled = false;
        }
        if (detailName != null) detailName.text = "";
        if (detailCategory != null) detailCategory.text = "";
        if (detailValue != null) detailValue.text = "";
        if (detailDivider != null) detailDivider.SetActive(false);
        if (detailPreviewCard != null) detailPreviewCard.gameObject.SetActive(false);
        if (detailPlaceholderLabel != null)
        {
            detailPlaceholderLabel.gameObject.SetActive(true);
            detailPlaceholderLabel.text = detailPlaceholder;
            if (detailText != null) detailText.text = "";
        }
        else if (detailText != null) detailText.text = detailPlaceholder;
        detailCardId = null;
        detailLevel = -1;
        focusCardId = null;
        focusLevel = -1;
        RefreshConvertButton();
    }

    // level = -1 for a Deck-slot-originated selection (Deck doesn't track
    // level - see the class comment on detailCardId/detailLevel), which
    // also hides CONVERT (RefreshConvertButton).
    void ShowDetail(CardDefinition card, int level, int count)
    {
        if (card == null) return;

        var gmForData = GameManager.Instance;
        // Deck側から選んだ時(level=-1)も、表示用のLvはそのカードの所持Lv(合成カードはキー自体がLvを持つ)
        int shownLevel = level >= 1 ? level : Mathf.Max(1, CardInventory.GetHighestLevel(card.cardId));
        if (detailPlaceholderLabel != null) detailPlaceholderLabel.gameObject.SetActive(false);
        if (detailDivider != null) detailDivider.SetActive(true);
        if (detailPreviewCard != null && gmForData != null)
        {
            detailPreviewCard.gameObject.SetActive(true);
            detailPreviewCard.ShowFrontImmediate(gmForData.MakeOwnedCardData(card, shownLevel, 0));
            detailPreviewCard.SetInteractable(false);
        }

        if (detailIcon != null && detailPreviewCard == null)
        {
            if (detailIcon.sprite != null) Destroy(detailIcon.sprite);
            if (card.icon != null)
            {
                detailIcon.sprite = Sprite.Create(card.icon, new Rect(0f, 0f, card.icon.width, card.icon.height), new Vector2(0.5f, 0.5f));
                detailIcon.enabled = true;
            }
            else
            {
                detailIcon.sprite = null;
                detailIcon.enabled = false;
            }
        }

        // Item 6/8 - Lv and owned count folded into the name/category
        // lines (detailText keeps the plain card description below).
        string levelLabel = shownLevel >= CardInventory.MaxCardLevel ? $"Lv.{shownLevel} MAX" : $"Lv.{shownLevel}";
        if (detailPreviewCard != null)
        {
            // カードUI最終デザイン改修(2026-09-26) - 名前 / Category・Lv / 主な効果 / 説明 の順
            if (detailName != null) detailName.text = card.cardName;
            string countLabel = level >= 1 && count > 1 ? $"   所持 x{count}" : "";
            if (detailCategory != null) detailCategory.text = $"{card.category.ToString().ToUpperInvariant()}  /  {levelLabel}   {card.RarityStars}{countLabel}";
            if (detailValue != null) detailValue.text = MasteryDetailLine(card) + BuildDetailValueText(card);
        }
        else
        {
            if (detailName != null) detailName.text = $"{card.cardName} {card.RarityStars} " + (level >= 1 ? levelLabel : "");
            string countLabel = level >= 1 ? $"  x{count}" : "";
            if (detailCategory != null) detailCategory.text = card.category.ToString().ToUpperInvariant() + countLabel;
        }

        // Item 2/9 - "Character装備状態" / "Deck使用状態" always visible in
        // the detail panel, not just as an error message after a blocked
        // CONVERT attempt. Only meaningful for a COLLECTION-originated
        // selection (level >= 1) - a Deck-slot selection has no specific
        // level to check a stack's lock state against.
        string statusLine = "";
        var gm = GameManager.Instance;
        if (level >= 1 && gm != null)
        {
            bool equipped = gm.GetOwnerCharacterCardCountForStack(card.cardId, level) > 0;
            bool otherChar = !equipped && gm.GetCharacterCardLockedCountForStack(card.cardId, level) > 0;
            bool inDeck = gm.GetDeckLockedCountForStack(card.cardId, level) > 0;
            if (equipped && inDeck) statusLine = "\n\n[EQUIPPED / IN DECK]";
            else if (equipped) statusLine = "\n\n[EQUIPPED]";
            else if (inDeck) statusLine = "\n\n[IN DECK]";
            if (otherChar) statusLine += "\n[他のキャラのキャラカード]";
        }
        if (detailText != null) detailText.text = (detailPreviewCard != null ? DetailDescription(card) : card.description) + statusLine;

        detailCardId = card.cardId;
        detailLevel = level;
        RefreshConvertButton();
    }

    // カード長期育成(2026-10-04): このカード(主能力)をどこまで育てたか。詳細の画面だけで進みの数字まで出す
    static string MasteryDetailLine(CardDefinition card)
    {
        string id = CardMastery.BaseIdOf(card.cardId);
        if (!CardMastery.IsMaxReached(id)) return "";
        if (CardMastery.IsAwakened(id))
            return $"<color=#ffe08a><b>Lv.9 MAX  {CardMastery.StarsFor(5)}  AWAKENED</b></color>{(CardMastery.Overflow(id) > 0 ? $"  <size=16>(★5後の保管 {CardMastery.Overflow(id)})</size>" : "")}\n";
        return $"<color=#ffd76a><b>Lv.9 MAX  {CardMastery.Stars(id)}</b></color>  Mastery {CardMastery.MasteryProgress(id)} / {CardMastery.NeedForNext(id)}\n";
    }

    // 主な効果(Main Value)。合成カードは主能力(MAIN)と引き継いだ能力(SUB)を強化量つきで、
    // 通常カードはCardEffectの一覧を「効果名 +値」で出す。
    static string BuildDetailValueText(CardDefinition card)
    {
        const string mainTag = "<color=#8FE9D6>MAIN</color>  ";
        const string subTag = "<color=#8FB8E9>SUB</color>  ";
        var sb = new System.Text.StringBuilder();
        CardVariant v = CardVariant.IsVariantKey(card.cardId) ? CardVariant.Parse(card.cardId) : null;
        if (v != null && v.abilities.Count > 0)
        {
            for (int i = 0; i < v.abilities.Count; i++)
            {
                var a = v.abilities[i];
                if (i > 0) sb.Append('\n');
                sb.Append(i == 0 ? mainTag : subTag);
                if (i > 0) sb.Append(CardVariant.AbilityName(a.id)).Append("  ");
                sb.Append(CardVariant.AbilityEffectText(a.id));
                if (a.stacks > 1) sb.Append($"  ×{a.stacks}");
            }
            return sb.ToString();
        }
        if (card.effects == null || card.effects.Count == 0) return "";
        sb.Append(mainTag);
        for (int i = 0; i < card.effects.Count; i++)
        {
            if (i > 0) sb.Append(" / ");
            sb.Append(CardVariant.EffectLabel(card.effects[i].type)).Append(' ').Append(CardEffectFormat.Format(card.effects[i]));
        }
        return sb.ToString();
    }

    // 効果説明。合成カードの説明文(【主】【副】の能力一覧)はMain Valueと重なるので、
    // 主能力の元カードの説明文を出す。
    static string DetailDescription(CardDefinition card)
    {
        CardVariant v = CardVariant.IsVariantKey(card.cardId) ? CardVariant.Parse(card.cardId) : null;
        if (v != null)
        {
            CardDefinition main = CardDatabase.FindBaseById(v.mainId);
            if (main != null) return main.description;
        }
        return card.description;
    }

    // 目視確認ツアー(CardVisualTour)用: 一覧のカードを「見ている」状態にする(デッキ操作はしない)。
    public void DebugFocusOwned(int index)
    {
        if (index < 0 || index >= displayedStacks.Count) return;
        CardInventory.Stack stack = displayedStacks[index];
        CardDefinition card = CardDatabase.FindById(stack.cardId);
        if (card == null) return;
        ShowDetail(card, stack.level, stack.count);
        focusCardId = stack.cardId;
        focusLevel = stack.level;
        Refresh();
    }

    public int DebugIndexOfOwned(string cardIdPrefix)
    {
        for (int i = 0; i < displayedStacks.Count; i++)
        {
            string id = displayedStacks[i].cardId;
            if (id == cardIdPrefix || id.Contains("|" + cardIdPrefix + "|")) return i;
        }
        return -1;
    }

    void OnOwnedCardTapped(int index)
    {
        var gm = GameManager.Instance;
        if (gm == null || index < 0 || index >= displayedStacks.Count) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        CardInventory.Stack stack = displayedStacks[index];
        CardDefinition tapped = CardDatabase.FindById(stack.cardId);
        if (tapped == null) return;
        // カードVisual最終調整依頼(2026-09-18), item1 - Collectionで実際に
        // タップして見た時点で「新規取得済み・未確認」状態を解除する。
        // 以降のRefresh()でNEWバッジが消えた状態が反映される。
        CardInventory.ClearNewUnconfirmed(stack.cardId);
        ShowDetail(tapped, stack.level, stack.count);
        focusCardId = stack.cardId;
        focusLevel = stack.level;

        // Item 7 - a Character Card slot is armed - this tap fills it,
        // overriding "add to deck" for exactly this one tap (see
        // OnCharacterSlotTapped).
        if (pendingEquipSlot >= 0)
        {
            bool equipped = gm.EquipCharacterCard(pendingEquipSlot, stack.cardId, stack.level);
            pendingEquipSlot = -1;
            StartCoroutine(ownedCards[index].PulseSelect());
            if (!equipped)
            {
                // Owned copies were all already spent elsewhere (Deck/
                // another Character Card slot) - nothing to show beyond
                // the detail panel already updated above.
            }
            Refresh();
            // カードバランス v3: 能力の Lv9 上限で装備しなかった/超える分が効かない時は、理由を詳細に出す
            if (!string.IsNullOrEmpty(gm.LastEquipMessage) && detailText != null) detailText.text += "\n\n" + gm.LastEquipMessage;
            return;
        }

        // AddToDeck can refuse (already full, or every owned copy of this
        // card is already spent - see GameManager.AddToDeck) - the pulse
        // settles at the card's ACTUAL resulting state either way, rather
        // than assuming it always succeeded.
        bool added = gm.AddToDeck(stack.cardId);
        StartCoroutine(ownedCards[index].PulseSelect());
        if (added && deckScrollRect != null) StartCoroutine(PlayCardFly(ownedCards[index].rect, deckScrollRect.viewport, tapped.icon));
        Refresh();
    }

    void OnDeckSlotTapped(int index)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        var deck = gm.DeckCards;
        if (index < 0 || index >= deck.Count) return;
        CardDefinition tapped = CardDatabase.FindById(deck[index]);
        // level=-1 - Deck doesn't track which specific level this copy
        // is, so CONVERT is unavailable from a Deck-originated selection
        // (see ShowDetail/RefreshConvertButton).
        ShowDetail(tapped, -1, 0);
        if (tapped != null) { focusCardId = tapped.cardId; focusLevel = -1; }
        bool removed = gm.RemoveFromDeck(deck[index]);
        if (removed && ownedScrollRect != null && tapped != null) StartCoroutine(PlayCardFly(deckSlotCards[index].rect, ownedScrollRect.viewport, tapped.icon));
        Refresh();
    }

    // Item 7 - Character Card slot tap: filled -> unequip immediately;
    // empty -> arm it so the next COLLECTION tap fills it (see
    // OnOwnedCardTapped's first check).
    void OnCharacterSlotTapped(int slot)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        string id = gm.CharacterCardIds[slot];
        if (!string.IsNullOrEmpty(id))
        {
            gm.EquipCharacterCard(slot, null, 1);
            pendingEquipSlot = -1;
            Refresh();
            return;
        }
        pendingEquipSlot = pendingEquipSlot == slot ? -1 : slot;
        Refresh();
    }

    // Item 9 - Convert-to-MILE, triggered from the center detail panel's
    // CONVERT button (only visible/enabled for a COLLECTION-originated
    // selection - see RefreshConvertButton). Reuses the confirmDialog
    // field already declared above (shared with おすすめ編成/全て外す).
    void OnConvertTapped()
    {
        if (detailLevel < 1 || string.IsNullOrEmpty(detailCardId)) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.IsCardInUse(detailCardId))
        {
            if (detailText != null) detailText.text = "現在使用中のカードです。外してから使用してください。";
            return;
        }

        CardDefinition card = CardDatabase.FindById(detailCardId);
        string cardId = detailCardId;
        int level = detailLevel;
        string message = $"{card?.cardName}{(level > 1 ? $" Lv.{level}" : "")}\nを{CardConvertMile} MILEに変換しますか？";

        if (confirmDialog == null) { DoConvert(cardId, level); return; }
        confirmDialog.Show(message, () => DoConvert(cardId, level));
    }

    void DoConvert(string cardId, int level)
    {
        if (!CardInventory.RemoveCard(cardId, level, 1))
        {
            if (detailText != null) detailText.text = "カードが不足しています";
            return;
        }
        GameManager.Instance?.AddMile(CardConvertMile);
        ResetDetail();
        StartCoroutine(PlayConvertGlow());
        Refresh();
    }

    IEnumerator PlayConvertGlow()
    {
        if (magicCircleImage == null) yield break;
        Color tint = new Color(1f, 0.85f, 0.3f);
        magicCircleImage.gameObject.SetActive(true);
        RectTransform r = magicCircleImage.rectTransform;
        r.localScale = Vector3.one * 0.6f;
        magicCircleImage.color = new Color(tint.r, tint.g, tint.b, 0f);

        float t = 0f;
        const float dur = 0.35f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float f = Mathf.Clamp01(t);
            magicCircleImage.color = new Color(tint.r, tint.g, tint.b, f);
            r.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.15f, f);
            yield return null;
        }
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float f = Mathf.Clamp01(t);
            magicCircleImage.color = new Color(tint.r, tint.g, tint.b, 1f - f);
            yield return null;
        }
        magicCircleImage.gameObject.SetActive(false);
    }

    // A simple, non-optimizing auto-build (CardDatabase.BuildRecommendedDeck
    // - see its own comment) for players who don't want to hand-pick a
    // deck. Only prompts for confirmation if there's an existing deck that
    // would actually be replaced - nothing to lose by applying straight
    // away over an empty one.
    void OnRecommendTapped()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        List<string> recommended = CardDatabase.BuildRecommendedDeck(GameManager.DeckCapacity);
        if (gm.DeckCards.Count > 0)
        {
            if (confirmDialog == null) return;
            confirmDialog.Show("現在のデッキを\nおすすめ編成に変更しますか？", () =>
            {
                gm.SetDeck(recommended);
                Refresh();
            });
        }
        else
        {
            gm.SetDeck(recommended);
            Refresh();
        }
    }

    // Moves every card currently in the deck back to COLLECTION - card
    // ownership/unlock state is untouched (SetDeck only ever writes the
    // deck list itself). Always confirmed first ("誤操作防止のため") except
    // when the deck is already empty, where there's nothing to confirm.
    void OnClearAllTapped()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.DeckCards.Count == 0) return;
        if (confirmDialog == null) return;

        confirmDialog.Show("デッキからすべてのカードを\n外しますか？", () =>
        {
            gm.SetDeck(Enumerable.Empty<string>());
            Refresh();
        });
    }

    // A small icon flies from the source card's current position to the
    // destination panel's viewport center and fades out at the end,
    // giving "the card moved to the other side" feedback (COLLECTION ->
    // DECK on add, DECK -> COLLECTION on remove) without needing to
    // animate the real, GridLayoutGroup-managed card objects themselves
    // (which would fight the layout system's own positioning). Parented
    // under `root`, so as the most-recently-added child it renders above
    // every panel/card already built under it.
    public float cardFlyDuration = 0.2f;

    IEnumerator PlayCardFly(RectTransform source, RectTransform destination, Texture2D icon)
    {
        if (root == null || source == null || destination == null) yield break;

        GameObject ghostGO = new GameObject("CardFlyGhost");
        ghostGO.transform.SetParent(root.transform, false);
        RectTransform ghostRect = ghostGO.AddComponent<RectTransform>();
        ghostRect.sizeDelta = new Vector2(70f, 70f);
        ghostRect.position = source.position;

        Image ghostImage = ghostGO.AddComponent<Image>();
        ghostImage.raycastTarget = false;
        if (icon != null)
        {
            ghostImage.sprite = Sprite.Create(icon, new Rect(0f, 0f, icon.width, icon.height), new Vector2(0.5f, 0.5f));
        }

        Vector3 startPos = source.position;
        Vector3 endPos = destination.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, cardFlyDuration);
            float f = Mathf.Clamp01(t);
            ghostRect.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, f));
            ghostRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, f);
            if (f > 0.6f)
            {
                Color c = ghostImage.color;
                c.a = Mathf.Lerp(1f, 0f, (f - 0.6f) / 0.4f);
                ghostImage.color = c;
            }
            yield return null;
        }

        if (ghostImage.sprite != null) Destroy(ghostImage.sprite);
        Destroy(ghostGO);
    }

    // Pointer state for the manual press/drag/release tracking below -
    // mirrors the same "start position, track past a threshold, decide tap
    // vs. drag on release" pattern PlayerController already uses to tell a
    // tap from a swipe.
    bool pointerActive;
    Vector2 pointerDownScreenPos;
    Vector2 lastViewportLocalPos;
    ScrollRect activeScrollRect;
    bool draggedPastThreshold;

    // Raw touch/mouse press/drag/release - see the class comment for why
    // this bypasses Button.onClick/ScrollRect's own EventSystem-driven
    // drag entirely, rather than just being a tap-only fallback.
    void Update()
    {
        if (root == null || !root.activeInHierarchy) return;
        // Input Lock - see ScreenTransitionManager's own class comment;
        // ignores every tap/drag on this screen while a transition (in or
        // out) is mid-flight.
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (UiInputGate.Blocked) return; // 設定/DEBUGパネルが手前に開いている(閉じた時の指が離れるまでも)

        bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
        bool held = !down && !up && (Input.GetMouseButton(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled));
        Vector2 screenPos = Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;

        // Modal - while the confirm dialog is open, every tap resolves
        // against it alone (Yes/No, or dismissed) and nothing underneath
        // (cards, scroll, filter tabs) sees it at all. No drag/scroll
        // concern here, so a plain release-to-resolve is enough - no need
        // for the tap-vs-drag threshold logic below.
        if (confirmDialog != null && confirmDialog.IsOpen)
        {
            if (up) confirmDialog.HandleTap(screenPos);
            return;
        }

        if (down)
        {
            pointerActive = true;
            pointerDownScreenPos = screenPos;
            draggedPastThreshold = false;
            activeScrollRect = FindScrollRectContaining(screenPos);
            if (activeScrollRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(activeScrollRect.viewport, screenPos, null, out lastViewportLocalPos);
            }
            return;
        }

        if (!pointerActive) return;

        if (held)
        {
            if (!draggedPastThreshold && Vector2.Distance(screenPos, pointerDownScreenPos) > dragTapThreshold)
            {
                draggedPastThreshold = true;
            }

            if (draggedPastThreshold && activeScrollRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(activeScrollRect.viewport, screenPos, null, out Vector2 currentLocalPos);
                float deltaY = currentLocalPos.y - lastViewportLocalPos.y;
                lastViewportLocalPos = currentLocalPos;

                float contentH = activeScrollRect.content.rect.height;
                float viewportH = activeScrollRect.viewport.rect.height;
                float range = contentH - viewportH;
                // Content follows the finger 1:1 (standard drag-to-pan
                // feel); verticalNormalizedPosition is inverted (1=top,
                // 0=bottom) relative to that, and Unity's own setter
                // already clamps it to [0,1] for us.
                if (range > 0f)
                {
                    activeScrollRect.verticalNormalizedPosition -= deltaY / range;
                }
            }
            return;
        }

        if (up)
        {
            pointerActive = false;
            if (!draggedPastThreshold) HandleTap(pointerDownScreenPos);
            activeScrollRect = null;
        }
    }

    ScrollRect FindScrollRectContaining(Vector2 screenPos)
    {
        if (ownedScrollRect != null && RectTransformUtility.RectangleContainsScreenPoint(ownedScrollRect.viewport, screenPos, null)) return ownedScrollRect;
        if (deckScrollRect != null && RectTransformUtility.RectangleContainsScreenPoint(deckScrollRect.viewport, screenPos, null)) return deckScrollRect;
        return null;
    }

    void HandleTap(Vector2 screenPos)
    {
        if (backButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(backButtonRect, screenPos, null))
        {
            Close();
            return;
        }

        if (recommendButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(recommendButtonRect, screenPos, null))
        {
            OnRecommendTapped();
            return;
        }

        if (clearButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(clearButtonRect, screenPos, null))
        {
            OnClearAllTapped();
            return;
        }

        if (convertButtonRect != null && convertButtonRect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(convertButtonRect, screenPos, null))
        {
            OnConvertTapped();
            return;
        }

        if (charPrevRect != null && RectTransformUtility.RectangleContainsScreenPoint(charPrevRect, screenPos, null)) { CycleCharacterCardOwner(-1); return; }
        if (charNextRect != null && RectTransformUtility.RectangleContainsScreenPoint(charNextRect, screenPos, null)) { CycleCharacterCardOwner(+1); return; }

        for (int i = 0; i < characterSlotCards.Length; i++)
        {
            RewardCardUI slot = characterSlotCards[i];
            if (slot == null || !slot.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(slot.rect, screenPos, null))
            {
                OnCharacterSlotTapped(i);
                return;
            }
        }

        for (int i = 0; i < filterButtonRects.Length; i++)
        {
            RectTransform tabRect = filterButtonRects[i];
            if (tabRect == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(tabRect, screenPos, null))
            {
                if (i < FilterNames.Length && activeFilter != FilterNames[i])
                {
                    activeFilter = FilterNames[i];
                    RefreshFilterHighlight();
                    Refresh();
                }
                return;
            }
        }

        for (int i = 0; i < ownedCards.Length; i++)
        {
            RewardCardUI card = ownedCards[i];
            if (card == null || !card.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(card.rect, screenPos, null))
            {
                OnOwnedCardTapped(i);
                return;
            }
        }

        for (int i = 0; i < deckSlotCards.Length; i++)
        {
            RewardCardUI card = deckSlotCards[i];
            if (card == null || !card.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(card.rect, screenPos, null))
            {
                OnDeckSlotTapped(i);
                return;
            }
        }
    }
}

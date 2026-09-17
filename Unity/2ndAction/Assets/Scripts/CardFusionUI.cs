using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Home Room UI reconstruction pass - the independent "Card Fusion" screen
// (item 10, reached by tapping the book on the TOP room; "ここは独立画面
// でOK"). Renamed from the old combined CardMenuUI - Gacha moved inline onto
// the TOP room itself (see GameManager.OnGachaMachineTapped), Character
// Cards/Convert moved into the Card Edit screen (DeckEditUI). This screen
// now does exactly one thing: same-name level-up fusion and cross-name
// Main/Sub fusion, via two dedicated slots (MAIN CARD / SUB CARD) plus a
// full owned-cards list.
//
// Item 11 - the owned-cards list here shows EVERY owned stack, including
// ones fully or partially "spent" on Deck/Character Card slots (tagged
// EQUIPPED/IN DECK and dimmed rather than hidden - see GameManager.
// GetAvailableCountForStack). Same raw-touch input philosophy as
// DeckEditUI (see its own class comment) - no Button.onClick/EventSystem.
public class CardFusionUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public CanvasGroup rootGroup;
    public RectTransform backButtonRect;
    public float fadeDuration = 0.16f;

    [Header("Status")]
    public Text statusText;
    public string statusPlaceholder = "MainカードとSubカードをタップして選択してください";

    [Header("Main / Sub Slots")]
    public RewardCardUI mainSlotCard;
    public RewardCardUI subSlotCard;
    public RectTransform fuseButtonRect;
    public Text fuseButtonLabel;

    [Header("Owned Cards List")]
    public RewardCardUI[] ownedCards = new RewardCardUI[0];
    public ScrollRect ownedScrollRect;
    public Text ownedCountText;

    [Header("Reveal / Presentation")]
    // Fusion success reveal - prebuilt hidden, centered.
    public RewardCardUI revealCard;
    // A single tintable ring (Assets/Art/Effects/DoubleJumpRing.png, already
    // in the project) standing in for the "青＋金の魔法陣" beat (item 10) -
    // see the brief's own "Unity側のTransform移動、Scale、回転、Particle等
    // で構いません" permission. Tint distinguishes success (gold) from
    // failure (red then gold).
    public Image magicCircleImage;

    // Fusion Ver.1, item 12/13 - tunable constants (all easily adjustable
    // here per the brief's repeated "調整可能に" instruction). The Lv cap
    // itself lives on CardInventory.MaxCardLevel (shared with GameManager).
    public const float MainInheritChance = 0.50f;
    public const float SubInheritChance = 0.25f;
    public const int FusionFailureRefundMile = 200;

    // Selection state - Main/Sub each hold a specific owned (cardId, level)
    // stack, cleared back to unselected by tapping their own slot display.
    // Nothing is actually consumed from CardInventory until FUSE is tapped.
    string mainCardId;
    int mainLevel = -1;
    string subCardId;
    int subLevel = -1;

    List<CardInventory.Stack> displayedStacks = new List<CardInventory.Stack>();

    public void Open()
    {
        if (root != null) root.SetActive(true);
        mainCardId = null;
        mainLevel = -1;
        subCardId = null;
        subLevel = -1;
        fusionInProgress = false; // defensive reset in case a previous visit somehow left this stuck
        SetStatus(statusPlaceholder);
        Refresh();
        if (ownedScrollRect != null) ownedScrollRect.verticalNormalizedPosition = 1f;
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            StartCoroutine(FadeGroupTo(1f));
        }
    }

    public void Close()
    {
        if (ScreenTransitionManager.Instance != null && !ScreenTransitionManager.Instance.IsTransitioning)
        {
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                if (root != null) root.SetActive(false);
                if (rootGroup != null) rootGroup.alpha = 1f;
                if (GameManager.Instance != null) GameManager.Instance.CloseCardFusion();
            });
            return;
        }

        if (rootGroup != null) StartCoroutine(CloseAfterFade());
        else
        {
            if (root != null) root.SetActive(false);
            if (GameManager.Instance != null) GameManager.Instance.CloseCardFusion();
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
        if (GameManager.Instance != null) GameManager.Instance.CloseCardFusion();
    }

    void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    // Single refresh entry point every action below calls once it's done -
    // Main/Sub slots and the owned-cards list all read from
    // GameManager/CardInventory fresh each time.
    void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        RefreshSlot(mainSlotCard, mainCardId, mainLevel);
        RefreshSlot(subSlotCard, subCardId, subLevel);
        if (fuseButtonLabel != null)
        {
            bool ready = !string.IsNullOrEmpty(mainCardId) && !string.IsNullOrEmpty(subCardId);
            fuseButtonLabel.text = ready ? "FUSE" : "SELECT MAIN / SUB";
        }

        // Item 11 - every owned stack, including ones fully or partially
        // spent (never hidden). Sorted for a stable/predictable layout
        // (CardDatabase's own sortOrder, then ascending level).
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

            int available = gm.GetAvailableCountForStack(stack.cardId, stack.level);
            bool equipped = gm.GetCharacterCardLockedCountForStack(stack.cardId, stack.level) > 0;
            bool inDeck = gm.GetDeckLockedCountForStack(stack.cardId, stack.level) > 0;

            ownedCards[i].gameObject.SetActive(true);
            // Card UI改修(2026-09-08) - showDetails:true。この画面には別
            // 途詳細パネルがまだないため、合成素材を選ぶ判断に必要な効果
            // 文を引き続き表示する(RewardCardUI.showDetailsのコメント参
            // 照)。
            ownedCards[i].ShowFrontImmediate(MakeStackCardData(card, stack.level, stack.count, equipped, inDeck), showDetails: true);

            bool isSelected = (stack.cardId == mainCardId && stack.level == mainLevel)
                || (stack.cardId == subCardId && stack.level == subLevel);
            ownedCards[i].SetSelected(isSelected);
            // Fully locked (nothing available) - dim regardless of
            // selection (can't actually happen while selected anyway,
            // since a fully-locked stack can't be selected in the first
            // place - see OnOwnedCardTapped).
            if (available <= 0 && !isSelected)
            {
                ownedCards[i].frameImage.color = new Color(1f, 1f, 1f, 0.35f);
            }
        }

        if (ownedCountText != null) ownedCountText.text = $"OWNED CARDS ({displayedStacks.Count})";
    }

    void RefreshSlot(RewardCardUI slot, string cardId, int level)
    {
        if (slot == null) return;
        CardDefinition card = string.IsNullOrEmpty(cardId) ? null : CardDatabase.FindById(cardId);
        if (card != null)
        {
            slot.gameObject.SetActive(true);
            slot.ShowFrontImmediate(MakeStackCardData(card, level, CardInventory.GetCount(cardId, level), false, false), showDetails: true);
            slot.SetSelected(true);
        }
        else
        {
            slot.gameObject.SetActive(true);
            slot.ShowEmpty();
        }
    }

    // Card Level Ver.1, item 8 / Home Room pass, item 11 - Lv always shown
    // (MAX tag at cap), plus an EQUIPPED/IN DECK tag when locked. Card UI /
    // Rarity Frame pass, item 16 - Rarity/LevelLine populated the same way
    // GameManager.MakeOwnedCardData's Collection mode does ("Lv.N xCount");
    // the dedicated EquippedBadge only covers "equipped" (IN DECK alone
    // still only shows via the Description tag below - no separate badge
    // was requested for that state).
    static RewardCardData MakeStackCardData(CardDefinition card, int level, int count, bool equipped, bool inDeck)
    {
        string levelLabel = level >= CardInventory.MaxCardLevel ? $"Lv.{level} MAX" : $"Lv.{level}";
        string lockTag = equipped && inDeck ? "\nEQUIPPED / IN DECK" : equipped ? "\nEQUIPPED" : inDeck ? "\nIN DECK" : "";
        // Card UI改修(2026-09-08) - 所持枚数はLevelLineから分離しCountへ
        // (RewardCardData.Countのコメント参照)。
        return new RewardCardData
        {
            CardId = card.cardId,
            Icon = card.icon,
            Title = card.cardName,
            Description = card.description + lockTag,
            Rarity = card.rarity,
            LevelLine = levelLabel,
            Count = count,
            ShowEquippedBadge = equipped,
            Category = card.category
        };
    }

    // ===== Owned-card list taps ===== //

    // Ver.1 finishing pass, item 5 - "演出中の二重入力は禁止". Set the
    // instant FUSE actually commits (before the reveal coroutine starts),
    // cleared once that coroutine finishes - every other tap handler below
    // bails out early while true.
    bool fusionInProgress;

    void OnOwnedCardTapped(int index)
    {
        if (fusionInProgress) return;
        if (index < 0 || index >= displayedStacks.Count) return;
        CardInventory.Stack stack = displayedStacks[index];
        CardDefinition card = CardDatabase.FindById(stack.cardId);
        if (card == null) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        bool sameAsMain = stack.cardId == mainCardId && stack.level == mainLevel;
        bool sameAsSub = stack.cardId == subCardId && stack.level == subLevel;

        // Tapping a stack currently selected as Main or Sub deselects it
        // (nothing was actually consumed yet, so this is a free no-op).
        if (sameAsMain) { mainCardId = null; mainLevel = -1; Refresh(); return; }
        if (sameAsSub) { subCardId = null; subLevel = -1; Refresh(); return; }

        int available = gm.GetAvailableCountForStack(stack.cardId, stack.level);
        // How many of THIS stack are already claimed by the other slot -
        // matters for the same-name case, where Main and Sub can both
        // point at the same stack (needs 2 truly available, not 1).
        int alreadyClaimedByOtherSlot = 0;
        if (mainCardId == stack.cardId && mainLevel == stack.level) alreadyClaimedByOtherSlot++;
        if (subCardId == stack.cardId && subLevel == stack.level) alreadyClaimedByOtherSlot++;

        if (available - alreadyClaimedByOtherSlot <= 0)
        {
            SetStatus("現在使用中のカードです。外してから使用してください。");
            return;
        }

        if (string.IsNullOrEmpty(mainCardId))
        {
            mainCardId = stack.cardId;
            mainLevel = stack.level;
            SetStatus(string.IsNullOrEmpty(subCardId) ? "Subカードをタップしてください" : "FUSEをタップして合成してください");
        }
        else if (string.IsNullOrEmpty(subCardId))
        {
            subCardId = stack.cardId;
            subLevel = stack.level;
            SetStatus("FUSEをタップして合成してください");
        }
        else
        {
            SetStatus("Main/Subをタップして外してから選び直してください");
            return;
        }
        Refresh();
    }

    void OnFuseTapped()
    {
        if (fusionInProgress) return;
        if (string.IsNullOrEmpty(mainCardId) || string.IsNullOrEmpty(subCardId)) return;
        fusionInProgress = true;

        if (mainCardId == subCardId && mainLevel == subLevel)
        {
            DoSameNameFusion(mainCardId, mainLevel);
        }
        else
        {
            DoCrossNameFusion(mainCardId, mainLevel, subCardId, subLevel);
        }
        mainCardId = null;
        mainLevel = -1;
        subCardId = null;
        subLevel = -1;
        Refresh();
    }

    // Fusion Ver.1, item 12 - always succeeds; consumes 2 copies at
    // `level`, grants 1 copy at level+1 (capped at MaxCardLevel).
    void DoSameNameFusion(string cardId, int level)
    {
        CardDefinition card = CardDatabase.FindById(cardId);
        if (card == null) { fusionInProgress = false; return; }
        if (level >= CardInventory.MaxCardLevel)
        {
            SetStatus("これ以上レベルアップできません（上限）");
            fusionInProgress = false;
            return;
        }
        if (!CardInventory.RemoveCard(cardId, level, 2))
        {
            SetStatus("カードが2枚必要です");
            fusionInProgress = false;
            return;
        }
        int newLevel = level + 1;
        CardInventory.AddCard(cardId, newLevel, 1);
        SetStatus($"{card.cardName} が Lv.{newLevel} になりました！");
        StartCoroutine(PlayFusionSuccessReveal(card, newLevel));
    }

    // Fusion Ver.1 restoration (2026-09-06), item "Fusionの仕様を本来の設
    // 計へ戻す" - Main+Sub cross-name fusion, both cards consumed as
    // material up front, Main/Sub inheritance rolled independently
    // (MainInheritChance/SubInheritChance). Previously (Ver.1 finishing
    // pass) SubSuccess-only granted a flat MILE bonus instead of any card,
    // and Main+Sub both succeeding just granted a leveled Main card plus
    // that same flat bonus - silently discarding Sub's contribution
    // entirely rather than the compound card the original design called
    // for. Restored to the brief's own 3-outcome table:
    //   Main only succeeds  -> leveled-up MAIN card (Main's effect only)
    //   Sub only succeeds   -> leveled-up SUB card (Sub's effect only)
    //   Both succeed        -> a genuine compound card carrying BOTH
    //                          effects (e.g. "Attack Up【Vampire】"), via
    //                          CardDatabase's on-demand compound synthesis
    //                          (see its own comment for how "mId+sId" as
    //                          the cardId doubles as the save format - no
    //                          separate recipe record needed)
    //   Neither succeeds     -> both cards lost, MILE refund (unchanged)
    void DoCrossNameFusion(string mId, int mLevel, string sId, int sLevel)
    {
        CardDefinition mainCard = CardDatabase.FindById(mId);
        CardDefinition subCard = CardDatabase.FindById(sId);
        if (mainCard == null || subCard == null) { fusionInProgress = false; return; }

        if (!CardInventory.RemoveCard(mId, mLevel, 1)) { SetStatus("Mainカードが不足しています"); fusionInProgress = false; return; }
        if (!CardInventory.RemoveCard(sId, sLevel, 1))
        {
            CardInventory.AddCard(mId, mLevel, 1); // give Main back, nothing was actually spent
            SetStatus("Subカードが不足しています");
            fusionInProgress = false;
            return;
        }

        bool mainSuccess = Random.value < MainInheritChance;
        bool subSuccess = Random.value < SubInheritChance;
        var gm = GameManager.Instance;

        if (mainSuccess && subSuccess)
        {
            string compoundId = mId + "+" + sId;
            CardDefinition compound = CardDatabase.FindById(compoundId);
            if (compound != null)
            {
                CardInventory.AddCard(compoundId, 1, 1);
                SetStatus($"合成成功！複合カード「{compound.cardName}」を獲得");
                StartCoroutine(PlayFusionSuccessReveal(compound, 1));
            }
            else
            {
                // Shouldn't happen (both mainCard/subCard were just
                // confirmed non-null above) - defensive fallback so a
                // Fusion can never silently consume both cards and grant
                // nothing back at all.
                int fallbackLevel = Mathf.Min(CardInventory.MaxCardLevel, mLevel + 1);
                CardInventory.AddCard(mId, fallbackLevel, 1);
                SetStatus($"合成成功！(複合カード生成に失敗したため{mainCard.cardName} Lv.{fallbackLevel} を獲得)");
                StartCoroutine(PlayFusionSuccessReveal(mainCard, fallbackLevel));
            }
        }
        else if (mainSuccess)
        {
            int newLevel = Mathf.Min(CardInventory.MaxCardLevel, mLevel + 1);
            CardInventory.AddCard(mId, newLevel, 1);
            SetStatus($"合成成功！{mainCard.cardName} Lv.{newLevel} を獲得");
            StartCoroutine(PlayFusionSuccessReveal(mainCard, newLevel));
        }
        else if (subSuccess)
        {
            int newLevel = Mathf.Min(CardInventory.MaxCardLevel, sLevel + 1);
            CardInventory.AddCard(sId, newLevel, 1);
            SetStatus($"Mainの継承には失敗しましたが、{subCard.cardName} Lv.{newLevel} を獲得しました");
            StartCoroutine(PlayFusionSuccessReveal(subCard, newLevel));
        }
        else
        {
            if (gm != null) gm.AddMile(FusionFailureRefundMile);
            SetStatus($"合成に失敗しました。使用したカードは失われ、MILEが還元されました（+{FusionFailureRefundMile}）");
            StartCoroutine(PlayFusionFailureReveal());
        }

        Refresh();
    }

    IEnumerator PlayFusionSuccessReveal(CardDefinition card, int level)
    {
        yield return PlayMagicCircle(new Color(1f, 0.85f, 0.4f));
        if (revealCard != null)
        {
            var data = MakeStackCardData(card, level, CardInventory.GetTotalCount(card.cardId), false, false);
            revealCard.gameObject.SetActive(true);
            revealCard.SetContent(data, showDetails: true);
            revealCard.ShowBack();
            yield return revealCard.FlipToFront(0.35f);
            yield return revealCard.FlashFrame(0.3f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return revealCard.FadeTo(0f, 0.25f);
            revealCard.ResetForReuse();
        }
        fusionInProgress = false;
        Refresh();
    }

    // Fusion failure / total-failure MILE-refund presentation (item 13's
    // "光が不安定になる->カードが光の粒子に砕ける->金色に変化->MILEに変換"
    // sequence, reproduced here as an unstable-red glow followed by a gold
    // glow, rather than literal particle/shatter art).
    IEnumerator PlayFusionFailureReveal()
    {
        yield return PlayMagicCircle(new Color(1f, 0.3f, 0.3f));
        yield return PlayMagicCircle(new Color(1f, 0.85f, 0.3f));
        fusionInProgress = false;
        Refresh();
    }

    // Fades a tinted ring in (scaling up slightly) then out - the shared
    // "magic circle" beat both presentations above use.
    IEnumerator PlayMagicCircle(Color tint)
    {
        if (magicCircleImage == null)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            yield break;
        }
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

    // ===== Raw touch input (mirrors DeckEditUI.Update - see its own class
    // comment for why Button.onClick/EventSystem isn't used here) ===== //

    bool pointerActive;
    Vector2 pointerDownScreenPos;
    Vector2 lastViewportLocalPos;
    bool draggedPastThreshold;
    public float dragTapThreshold = 14f;

    void Update()
    {
        if (root == null || !root.activeInHierarchy) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;

        bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
        bool held = !down && !up && (Input.GetMouseButton(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled));
        Vector2 screenPos = Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;

        if (down)
        {
            pointerActive = true;
            pointerDownScreenPos = screenPos;
            draggedPastThreshold = false;
            if (ownedScrollRect != null) RectTransformUtility.ScreenPointToLocalPointInRectangle(ownedScrollRect.viewport, screenPos, null, out lastViewportLocalPos);
            return;
        }

        if (!pointerActive) return;

        if (held)
        {
            if (!draggedPastThreshold && Vector2.Distance(screenPos, pointerDownScreenPos) > dragTapThreshold)
            {
                draggedPastThreshold = true;
            }
            if (draggedPastThreshold && ownedScrollRect != null && RectTransformUtility.RectangleContainsScreenPoint(ownedScrollRect.viewport, pointerDownScreenPos, null))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(ownedScrollRect.viewport, screenPos, null, out Vector2 currentLocalPos);
                float deltaY = currentLocalPos.y - lastViewportLocalPos.y;
                lastViewportLocalPos = currentLocalPos;
                float contentH = ownedScrollRect.content.rect.height;
                float viewportH = ownedScrollRect.viewport.rect.height;
                float range = contentH - viewportH;
                if (range > 0f) ownedScrollRect.verticalNormalizedPosition -= deltaY / range;
            }
            return;
        }

        if (up)
        {
            pointerActive = false;
            if (!draggedPastThreshold) HandleTap(pointerDownScreenPos);
        }
    }

    void HandleTap(Vector2 screenPos)
    {
        if (backButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(backButtonRect, screenPos, null))
        {
            Close();
            return;
        }
        if (fuseButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(fuseButtonRect, screenPos, null))
        {
            OnFuseTapped();
            return;
        }
        if (mainSlotCard != null && mainSlotCard.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(mainSlotCard.rect, screenPos, null))
        {
            if (!string.IsNullOrEmpty(mainCardId)) { mainCardId = null; mainLevel = -1; Refresh(); }
            return;
        }
        if (subSlotCard != null && subSlotCard.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(subSlotCard.rect, screenPos, null))
        {
            if (!string.IsNullOrEmpty(subCardId)) { subCardId = null; subLevel = -1; Refresh(); }
            return;
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
    }
}

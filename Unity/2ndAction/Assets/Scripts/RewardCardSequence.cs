using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Orchestrates the card-draw presentation for a level-up choice:
// LEVEL UP announcement -> deck appears -> 3 cards fade/scale in -> flip
// face-up (each with a small Gold Edge Glow flash) -> idle while waiting ->
// player taps one -> a quick "picked" bump -> the other two lose the
// spotlight -> the pick drifts to center, glows once, shrinks away -> the
// caller's onApply callback actually applies the upgrade (GameManager.
// ApplyUpgradeByCardId, passed in untouched) -> everything closes.
//
// This class only ever receives already-decided data (RewardCardData) and
// hands back the chosen card's id (a plain string) through onApply - it has
// no opinion on what the 3 choices are, how they were picked, or what
// picking one does (deck/probability/effect/EXP logic all live in
// GameManager/CardDatabase, untouched by this file). Level Up Presentation
// pass (2026-09-02): everything below is the Presentation layer for that
// same, unchanged contract - existing StartSequence(cardData, onApply)
// signature and GameManager's call site are both untouched.
//
// Runs entirely on unscaled time/WaitForSecondsRealtime since it's active
// while Time.timeScale == 0.
public class RewardCardSequence : MonoBehaviour
{
    [Header("Root")]
    public CanvasGroup rootGroup;
    public Image dimImage;

    [Header("Level Up Announcement")]
    // Both optional - a scene built before this pass added them just skips
    // the announcement entirely (see PlayLevelUpAnnouncement) rather than
    // throwing a null reference.
    public CanvasGroup levelUpTextGroup;
    public RectTransform levelUpTextRect;
    // Run Continuation/Checkpoint Ver.1 - the announcement text itself
    // ("LEVEL UP" vs "BOSS REWARD") is now set per-call (see StartSequence's
    // announcementText param) instead of a scene-build-time constant, so
    // this same sequence/Canvas can present both without duplicating any of
    // the deck/card/select machinery below.
    public Text levelUpText;

    [Header("Deck")]
    public RectTransform deckRoot;
    public Image[] deckStackImages;

    [Header("Cards")]
    public RewardCardUI[] cards = new RewardCardUI[3];
    public Vector2[] cardSlotPositions = new Vector2[3];

    [Header("Glow")]
    public Image glowImage;

    // No dedicated SE yet - PlaySfx is null-safe, so every field below can
    // stay unassigned without breaking anything.
    [Header("Audio (optional)")]
    public AudioClip levelUpSe;
    public AudioClip deckAppearSe;
    public AudioClip drawSe;
    public AudioClip flipSe;
    public AudioClip selectSe;
    public AudioClip confirmSe;

    // ===== Level Up Presentation pass - Inspector-tunable timing =====
    // "最初は気持ちいいが5回目から邪魔になる演出にはしないでください" - every
    // duration below exists so pace can be re-tuned after watching real
    // playtime footage without touching any code.

    [Header("Timing - Level Up Announcement")]
    // Brief pre-beat before the EXP bar flash/LEVEL UP text - the gameplay
    // is already fully paused by the time this runs (GameManager sets
    // Time.timeScale=0 before calling StartSequence), so this alone reads
    // as the "軽いHit Stop的な停止" the brief asks for; a separate
    // HitStop.Freeze would be redundant at timeScale already 0.
    public float preAnnouncementPause = 0.08f;
    public float levelUpPopDuration = 0.4f;

    [Header("Timing - Background Overlay")]
    [Range(0f, 1f)] public float backgroundOverlayAlpha = 0.6f;
    public float backgroundFadeDuration = 0.2f;

    // Everything up through "cards become tappable" (fade -> deck -> draw ->
    // flip) is the part players just have to wait through before they can
    // act, so it's tuned for pace ("カードが表示されるまで：約0.7〜1.0秒以
    // 内"). Everything from a tap onward is the payoff for the choice they
    // just made ("カード選択後：約0.4〜0.6秒以内").
    [Header("Timing - Wait (target ~0.7-1.0s to Reveal complete)")]
    public float rootFadeDuration = 0.18f;
    public float deckShowPause = 0.15f;
    public float cardDrawDuration = 0.22f;
    // Stagger between each card starting its own appear (fly + scale-in +
    // fade-in) - Left -> Center -> Right, per the brief.
    public float cardAppearInterval = 0.08f;
    public float postDrawPause = 0.1f;
    public float cardFlipDuration = 0.26f;
    public float cardRevealInterval = 0.08f;
    public float revealFlashDuration = 0.22f;
    public float flipFinishPause = 0.12f;

    [Header("Idle - while waiting for a tap")]
    [Range(0f, 1f)] public float idlePulseMinIntensity = 0.08f;
    [Range(0f, 1f)] public float idlePulseMaxIntensity = 0.28f;
    public float idlePulsePeriod = 1.1f;

    [Header("Timing - Selection Payoff (target ~0.4-0.6s total)")]
    // Step 6 - immediate in-place "picked" feedback.
    public float selectedCardScale = 1.12f;
    public float selectedCardDuration = 0.12f;
    // Step 7 - the other two losing the spotlight (runs concurrently with
    // the winner's own move below, not serially).
    public float unselectedFadeDuration = 0.18f;
    // Step 8 - drift toward center, one small glow, then shrink away.
    // Split roughly 35% move / 20% settle-to-glow-scale / 45% shrink-away.
    public float selectedCardExitDuration = 0.42f;
    public float glowDuration = 0.24f;
    public float rootCloseDuration = 0.18f;

    [Header("Debug")]
    // Item 16 - the [LevelUpPresentation] milestone logs; separate from
    // the always-on DebugStep/LogStep diagnostic below (which already
    // existed and stays on regardless of this flag).
    public bool debugLogEnabled = true;

    bool running;
    int selectedIndex = -1;
    bool waitingForSelection;

    // Diagnostic only: the name of whatever step RunSequence is currently
    // on, shown on-screen (see GameManager.OnGUI) and logged, so a freeze
    // can be pinned to an exact step without needing adb/logcat access.
    public static string DebugStep = "";

    static void LogStep(string step)
    {
        DebugStep = step;
        Debug.Log("RewardCardSequence: " + step);
    }

    void LogPresentation(string message)
    {
        if (debugLogEnabled) Debug.Log(message);
    }

    void PlaySfx(AudioClip clip)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(clip);
    }

    // Bugfix 2026-09-06 - last-resort recovery for GameManager's outer
    // stuck-choice watchdog (see UpdatePendingChoiceWatchdog): if
    // RunSequence's coroutine crashed or hung partway through (before ever
    // reaching onApply), this resets every flag/visual this class owns so
    // a LATER, fresh StartSequence call can't be refused by the stale
    // `running` guard, and hides whatever was left on screen. Does NOT
    // call onApply - GameManager's watchdog already resolves the pending
    // choice itself (Checkpoint/EndBossPhase/UnlockEscape) without
    // actually applying a card, exactly like a 0-card pool already does.
    public void ForceReset()
    {
        StopAllCoroutines();
        running = false;
        waitingForSelection = false;
        selectedIndex = -1;
        gameObject.SetActive(false);
    }

    public void StartSequence(RewardCardData[] cardData, System.Action<string> onApply, string announcementText = "LEVEL UP")
    {
        if (running) return; // a level-up choice is already pending; ignore a re-trigger

        // A coroutine cannot be started on an inactive GameObject - Unity
        // just fails to schedule it (logging an error, not throwing), so
        // RunSequence would never run a single line, including the
        // gameObject.SetActive(true) that used to be its first real step.
        // This root GameObject starts inactive (see SceneBuilder), so it
        // must be activated here, before StartCoroutine, not inside the
        // coroutine itself. (Item 13's exact warning - kept unchanged from
        // before this pass.)
        gameObject.SetActive(true);
        StartCoroutine(RunSequence(cardData, onApply, announcementText));
    }

    IEnumerator RunSequence(RewardCardData[] cardData, System.Action<string> onApply, string announcementText)
    {
        LogStep("LevelUp Start");
        running = true;
        selectedIndex = -1;

        // Item 13 safety net - GameManager guarantees at least 1 entry
        // whenever StartSequence is actually called (pool.Count==0 returns
        // before ever reaching here), so this is provably unreachable in
        // practice, but the Presentation layer must never be the reason a
        // paused run gets stuck if that contract is ever violated by a
        // future change on the GameManager side.
        int cardCount = Mathf.Min(cards.Length, cardData != null ? cardData.Length : 0);
        if (cardCount <= 0)
        {
            Debug.LogWarning("RewardCardSequence: StartSequence called with no card data - resolving immediately instead of stalling.");
            running = false;
            gameObject.SetActive(false);
            onApply?.Invoke(null);
            yield break;
        }

        LogPresentation("[LevelUpPresentation] Started");

        rootGroup.alpha = 0f;
        dimImage.color = new Color(dimImage.color.r, dimImage.color.g, dimImage.color.b, 0f);
        glowImage.gameObject.SetActive(false);
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].StopIdlePulse();
            cards[i].gameObject.SetActive(false);
        }
        if (levelUpTextGroup != null) levelUpTextGroup.alpha = 0f;
        LogStep("Pause Complete");

        // ===== Level Up announcement - EXP bar flash + "LEVEL UP"/"BOSS
        // REWARD" pop =====
        yield return PlayLevelUpAnnouncement(announcementText);

        // ===== Background dim + card UI fade in (run together) =====
        StartCoroutine(FadeDim(backgroundOverlayAlpha, backgroundFadeDuration));
        yield return FadeRoot(1f, rootFadeDuration);
        LogStep("Dim Fade Complete");

        PlaySfx(deckAppearSe);
        SetDeckVisible(true);
        LogStep("Deck Show Start");
        yield return new WaitForSecondsRealtime(deckShowPause);
        LogStep("Deck Show Complete");

        // ===== Card Back appear: each card fades/scales in at its own
        // slot, staggered Left -> Center -> Right (cardAppearInterval),
        // riding the same fly-from-deck motion this project already had -
        // "Card Back出現" from the brief layered onto the existing motion
        // rather than replacing it outright. =====
        LogStep("Draw Cards Start");
        for (int i = 0; i < cardCount; i++)
        {
            var card = cards[i];
            card.gameObject.SetActive(true);
            card.ShowBack();
            card.rect.anchoredPosition = deckRoot.anchoredPosition;
            card.rect.localScale = Vector3.one * 0.8f;
            card.canvasGroup.alpha = 0f;
            PlaySfx(drawSe);
            LogStep("Draw Card " + i);
            StartCoroutine(card.FadeTo(1f, cardDrawDuration));
            StartCoroutine(card.ScaleTo(1f, cardDrawDuration));
            yield return card.MoveTo(cardSlotPositions[i], cardDrawDuration);
            if (i < cardCount - 1) yield return new WaitForSecondsRealtime(cardAppearInterval);
        }
        LogStep("Draw Cards Complete");
        LogPresentation("[LevelUpPresentation] Cards spawned");

        yield return new WaitForSecondsRealtime(postDrawPause);
        SetDeckVisible(false);

        // ===== Reveal: flip each face-up, staggered, each with a brief
        // Gold Edge Glow flash right as its content appears - "何が出た？
        // という小さな期待感". =====
        LogStep("Flip Start");
        for (int i = 0; i < cardCount; i++)
        {
            cards[i].SetContent(cardData[i]);
            PlaySfx(flipSe);
            LogStep("Flip Card " + i);
            StartCoroutine(cards[i].FlipToFront(cardFlipDuration));
            StartCoroutine(cards[i].FlashFrame(revealFlashDuration));
            if (i < cardCount - 1) yield return new WaitForSecondsRealtime(cardRevealInterval);
        }
        yield return new WaitForSecondsRealtime(flipFinishPause);
        LogStep("Flip Complete");
        LogPresentation("[LevelUpPresentation] Reveal complete");

        // Input Lock (item 14) - Reveal complete -> Input Enable, exactly
        // at this line; nothing before this point can register a tap
        // (SetInteractable(true) is the only thing that flips
        // button.interactable/canvasGroup.blocksRaycasts on).
        for (int i = 0; i < cardCount; i++)
        {
            cards[i].SetInteractable(true);
            cards[i].StartIdlePulse(idlePulseMinIntensity, idlePulseMaxIntensity, idlePulsePeriod);
        }

        // Belt-and-suspenders: if a tap somehow never gets recognized by
        // either input path above, this guarantees the run can never be
        // stuck paused forever - it just auto-picks the first card after a
        // long wait instead.
        LogStep("Waiting For Selection");
        waitingForSelection = true;
        float waitStart = Time.unscaledTime;
        const float selectionTimeoutSeconds = 20f;
        while (selectedIndex < 0)
        {
            if (Time.unscaledTime - waitStart > selectionTimeoutSeconds)
            {
                selectedIndex = 0;
                break;
            }
            yield return null;
        }
        waitingForSelection = false;
        LogStep("Selected " + selectedIndex);

        // Input Lock (item 14) - selection resolved -> Input Disable
        // immediately, before any animation, so a double-tap can't select a
        // second card or re-enter this block (OnCardClicked's own
        // selectedIndex>=0 guard already blocks it too - belt-and-suspenders
        // against Card Effect double-apply / coroutine double-run).
        PlaySfx(selectSe);
        for (int i = 0; i < cardCount; i++)
        {
            cards[i].SetInteractable(false);
            cards[i].StopIdlePulse();
        }

        var winner = cards[selectedIndex];
        LogPresentation($"[LevelUpPresentation] Card selected: {winner.Data.CardId}");

        // Step 6 - immediate in-place "picked" feedback (scale bump + a
        // border flash), before anything else moves or fades - "これを選ん
        // だ、というFeedbackを明確に".
        LogStep("Select Feedback Start");
        StartCoroutine(winner.FlashFrame(selectedCardDuration + 0.1f));
        yield return winner.ScaleTo(selectedCardScale, selectedCardDuration);
        LogStep("Select Feedback Complete");

        // Step 7 - the other two lose the spotlight quickly, concurrently
        // with the winner's own exit below (fire-and-forget) - "選択カード
        // より先に視線から外す".
        for (int i = 0; i < cardCount; i++)
        {
            if (i == selectedIndex) continue;
            StartCoroutine(cards[i].FadeTo(0f, unselectedFadeDuration));
            StartCoroutine(cards[i].ScaleTo(0.9f, unselectedFadeDuration));
        }

        // Step 8 - drift toward center, one small Gold/Blue glow, then
        // shrink away - "カードがPlayerへ吸収されたというより、Upgradeを獲
        // 得したと感じられる程度で構わない" (no big scale-up/hold any more).
        LogStep("Confirm Move Start");
        float moveDuration = selectedCardExitDuration * 0.35f;
        float settleDuration = selectedCardExitDuration * 0.2f;
        float shrinkDuration = selectedCardExitDuration - moveDuration - settleDuration;
        yield return winner.MoveTo(Vector2.zero, moveDuration);
        PlaySfx(confirmSe);
        StartCoroutine(PlayGlow(winner.rect.anchoredPosition));
        yield return winner.ScaleTo(1.1f, settleDuration);
        yield return ShrinkAway(winner, shrinkDuration);
        LogStep("Confirm Move Complete");

        LogStep("Apply Upgrade");
        onApply(winner.Data.CardId);
        LogPresentation("[LevelUpPresentation] Gameplay resumed");
        LogStep("Apply Upgrade Complete");

        yield return FadeRoot(0f, rootCloseDuration);
        gameObject.SetActive(false);
        running = false;
        LogStep("Sequence Complete");
        LogPresentation("[LevelUpPresentation] Presentation finished");
    }

    // Scale 1.1 -> 0 and alpha 1 -> 0 together (not two separate serial
    // tweens) - "Scale 1.1 -> 0.9 -> 0, Alpha 1 -> 0" from the brief,
    // simplified to one continuous shrink since the card is already at 1.1
    // from the settle step just before this runs.
    IEnumerator ShrinkAway(RewardCardUI card, float duration)
    {
        StartCoroutine(card.FadeTo(0f, duration));
        yield return card.ScaleTo(0f, duration);
    }

    // "EXP Barが一瞬発光 -> LEVEL UP表示" - GameManager owns the actual EXP
    // bar (its own IMGUI HUD), so this only ever calls the one public
    // trigger method it exposes (FlashExpBar) rather than reaching into its
    // fields; the LEVEL UP text itself lives in this Canvas so its
    // pop/fade can be driven directly. Gracefully skipped (not an error) if
    // levelUpTextGroup/Rect weren't wired - a scene built before this pass.
    IEnumerator PlayLevelUpAnnouncement(string announcementText)
    {
        yield return new WaitForSecondsRealtime(preAnnouncementPause);

        // Run Continuation/Checkpoint Ver.1 - only a normal Level Up flashes
        // the EXP bar/plays the Level Up SE (Boss Reward doesn't touch EXP
        // at all, so flashing that bar for it would be misleading).
        bool isLevelUp = announcementText == "LEVEL UP";
        if (isLevelUp)
        {
            if (GameManager.Instance != null) GameManager.Instance.FlashExpBar();
            PlaySfx(levelUpSe);
        }
        if (levelUpText != null) levelUpText.text = announcementText;

        if (levelUpTextGroup == null || levelUpTextRect == null) yield break;

        levelUpTextGroup.gameObject.SetActive(true);
        levelUpTextGroup.alpha = 0f;
        levelUpTextRect.localScale = Vector3.one * 0.8f;

        // Pop: Scale 0.8 -> 1.05 -> 1.0, Alpha 0 -> 1, over the first ~55%
        // of levelUpPopDuration; hold, then fade over the remainder.
        float popDuration = levelUpPopDuration * 0.55f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, popDuration);
            float f = Mathf.Clamp01(t);
            float scale = f < 0.7f ? Mathf.Lerp(0.8f, 1.05f, f / 0.7f) : Mathf.Lerp(1.05f, 1f, (f - 0.7f) / 0.3f);
            levelUpTextRect.localScale = Vector3.one * scale;
            levelUpTextGroup.alpha = Mathf.Min(1f, f / 0.6f);
            yield return null;
        }

        float remaining = Mathf.Max(0f, levelUpPopDuration - popDuration);
        float hold = remaining * 0.35f;
        float fade = remaining - hold;
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fade);
            levelUpTextGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t));
            yield return null;
        }
        levelUpTextGroup.gameObject.SetActive(false);
    }

    void SetDeckVisible(bool visible)
    {
        foreach (Image img in deckStackImages) img.enabled = visible;
    }

    IEnumerator FadeRoot(float target, float duration)
    {
        float start = rootGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            rootGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // Background Dark Overlay (item 2) - separate from FadeRoot above:
    // rootGroup.alpha covers the whole card-UI subtree (deck/cards/glow)
    // fading together, while dimImage's OWN alpha is animated here
    // independently so BackgroundOverlayAlpha/BackgroundFadeDuration can be
    // tuned without affecting how fast the cards themselves fade in.
    IEnumerator FadeDim(float target, float duration)
    {
        Color c = dimImage.color;
        float start = c.a;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            c.a = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            dimImage.color = c;
            yield return null;
        }
    }

    IEnumerator PlayGlow(Vector2 atPosition)
    {
        glowImage.rectTransform.anchoredPosition = atPosition;
        glowImage.gameObject.SetActive(true);
        glowImage.color = new Color(1f, 0.82f, 0.35f, 0f);
        glowImage.rectTransform.localScale = Vector3.one * 0.6f;

        float t = 0f;
        float growDuration = glowDuration * 0.4f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, growDuration);
            float f = Mathf.Clamp01(t);
            glowImage.color = new Color(1f, 0.82f, 0.35f, Mathf.Lerp(0f, 0.9f, f));
            glowImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, f);
            yield return null;
        }

        t = 0f;
        float fadeDuration = glowDuration * 0.6f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeDuration);
            float f = Mathf.Clamp01(t);
            glowImage.color = new Color(1f, 0.82f, 0.35f, Mathf.Lerp(0.9f, 0f, f));
            glowImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1.6f, f);
            yield return null;
        }

        glowImage.gameObject.SetActive(false);
    }

    // Wired to each card's Button.onClick by SceneBuilder. This is the
    // "normal" path via UGUI's EventSystem/GraphicRaycaster.
    public void OnCardClicked(int index)
    {
        if (selectedIndex >= 0) return; // already picked; ignore further taps
        selectedIndex = index;
    }

    // Backup path that doesn't depend on the EventSystem/Button pipeline at
    // all - hit-tests raw touch/mouse-down position directly against each
    // card's RectTransform, the same way the rest of this project (which
    // has no other uGUI anywhere) already handles taps. Whichever path
    // notices the tap first wins; this exists purely so a level-up can
    // never get stuck waiting on a tap that the EventSystem, for whatever
    // reason, didn't deliver.
    void Update()
    {
        if (!waitingForSelection || selectedIndex >= 0) return;

        if (!TouchInputUtil.TryGetTapPosition(out Vector2 screenPos)) return;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null || !cards[i].gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].rect, screenPos, null))
            {
                OnCardClicked(i);
                return;
            }
        }
    }

}

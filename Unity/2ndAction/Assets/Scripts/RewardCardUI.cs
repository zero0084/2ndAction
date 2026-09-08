using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// A single reward card - back/front visuals, and the handful of tweens
// (move/scale/fade/flip) RewardCardSequence choreographs it with. Every
// card on screen is one of these, built by the same factory method
// (RewardCardSequence.CreateCard) with just its content swapped, which is
// what stands in for a "shared Prefab, different data" structure in a
// project where every object is built by code rather than authored
// Prefab assets.
//
// Everything here runs while the game is paused (Time.timeScale == 0), so
// every animation uses unscaled time.
public class RewardCardUI : MonoBehaviour
{
    public RectTransform rect;
    public CanvasGroup canvasGroup;
    public Image backImage;
    // Card UI改修(2026-09-08) - 新しい共通デザイン素材(カード下地/タイト
    // ル帯)。baseImageは表向き時の背景全体(旧: iconBackdrop/textBackdrop
    // の単色パネルに代わるもの、ただしiconBackdropは可読性のため引き続き
    // 併用)、titleBandImageはタイトル文字の背景となる横長の紺+金プレート。
    // どちらもRarityに関係なく共通(frameImageだけがRarityで差し替わる)。
    public Image baseImage;
    public Image titleBandImage;
    public Image frameImage;
    // Card UI / Rarity Frame pass - the sprite CreateRewardCard was
    // originally built with (the project's existing generic CardFrame.png),
    // kept as CardRarityFrames.GetFrame's fallback for the rare case a
    // Rarity Sprite fails to load (every Rarity 1-5 now has its own real
    // frame asset - see CardRarityFrames' own comment).
    public Sprite defaultFrameSprite;
    // Plain dark panels sitting between the frame and the icon/text, purely
    // so the game world behind the card doesn't show through the frame
    // art's own semi-transparent interior - the frame's decorative border
    // itself is unaffected (these only cover the icon/text areas).
    public Image iconBackdrop;
    public Image textBackdrop;
    public Image iconImage;
    public Text titleText;
    public Text descriptionText;
    // Card UI / Rarity Frame pass - Rarity (top-left, ★ text) and Level
    // (top-right, "Lv.2 -> Lv.3" etc.) are now separate rows from Title/
    // Description (item 2 - "Card Nameを最も目立つ情報に、Rarity/Lvは補助
    // 情報として") rather than folded into Title's own string.
    public Text rarityText;
    public Text levelText;
    // Card UI改修(2026-09-08) - 所持枚数「×N」専用表示(RewardCardData.
    // Countが1以下、またはこのカード自体が「所持枚数」の概念を持たない
    // 呼び出し元(Reward/LevelUp選択・Fusionスロット等、Count未設定=0の
    // まま)では非表示)。タイトル帯の右端に固定配置。
    public Text countText;
    // Item 5 - a small "EQUIPPED" tag, toggled on/off rather than built per
    // call - RewardCardData.ShowEquippedBadge is false everywhere except
    // where a caller actually knows equip state.
    public GameObject equippedBadge;
    public Button button;

    RewardCardData data;
    public RewardCardData Data => data;

    // Card UI改修(2026-09-08) - カード表面に常時表示するのはフレーム/イラ
    // スト/タイトル/Lvの4つだけにする新方針(効果文/レア度★は詳細画面での
    // み)。既存のRewardCardSequence(Level Up/Boss Reward選択)や
    // CardFusionUI(合成素材選択)はまだ専用の詳細パネルを持たないため、
    // それらの呼び出し元は引き続きshowDetails:trueを渡して従来どおり効果
    // 文/★を表示させる(情報が全く見えなくなる退行を避けるための意図的な
    // 経過措置 - 詳細パネルが用意され次第simpleに揃えられる)。DeckEditUI
    // (COLLECTION/DECK/CHARACTER CARDS)は既存の中央詳細カラムがそのまま
    // このuseに対応するため、デフォルト(false)のシンプル表示で問題ない。
    bool showDetails;

    public void ShowBack()
    {
        backImage.enabled = true;
        if (baseImage != null) baseImage.enabled = false;
        if (titleBandImage != null) titleBandImage.enabled = false;
        frameImage.enabled = false;
        iconBackdrop.enabled = false;
        textBackdrop.enabled = false;
        iconImage.enabled = false;
        titleText.enabled = false;
        descriptionText.enabled = false;
        if (rarityText != null) rarityText.enabled = false;
        if (levelText != null) levelText.enabled = false;
        if (countText != null) countText.enabled = false;
        if (equippedBadge != null) equippedBadge.SetActive(false);
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
    }

    // An unfilled DECK slot - shows just the card frame art, dimmed, with
    // no icon/text/back image, so the slot still reads as "a slot exists
    // here" (per the reference mockup's numbered empty-slot placeholders)
    // instead of the grid just showing nothing where the slot would be.
    public void ShowEmpty()
    {
        backImage.enabled = false;
        if (baseImage != null) baseImage.enabled = false;
        if (titleBandImage != null) titleBandImage.enabled = false;
        frameImage.enabled = true;
        frameImage.sprite = defaultFrameSprite;
        frameImage.color = new Color(1f, 1f, 1f, 0.32f);
        iconBackdrop.enabled = false;
        textBackdrop.enabled = false;
        iconImage.enabled = false;
        titleText.enabled = false;
        descriptionText.enabled = false;
        if (rarityText != null) rarityText.enabled = false;
        if (levelText != null) levelText.enabled = false;
        if (countText != null) countText.enabled = false;
        if (equippedBadge != null) equippedBadge.SetActive(false);
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
    }

    // showDetails=false (new default) - "常時表示は フレーム/イラスト/
    // タイトル/Lvのみ" (効果文・★は非表示、詳細画面用)。showDetails=true
    // で従来どおり効果文/★も表示する(呼び出し元のコメント参照)。
    public void SetContent(RewardCardData cardData, bool showDetails = false)
    {
        data = cardData;
        this.showDetails = showDetails;
        if (iconImage.sprite != null) Destroy(iconImage.sprite);
        iconImage.sprite = cardData.Icon != null
            ? Sprite.Create(cardData.Icon, new Rect(0f, 0f, cardData.Icon.width, cardData.Icon.height), new Vector2(0.5f, 0.5f))
            : null;
        titleText.text = cardData.Title;
        descriptionText.text = cardData.Description;

        // Card UI / Rarity Frame pass - frame Sprite switches with Rarity;
        // frameImage.color stays whatever SetSelected/FlashFrame/idle pulse
        // last left it (this method never touches color) since those are
        // independent of which frame art is showing.
        frameImage.sprite = CardRarityFrames.GetFrame(cardData.Rarity, defaultFrameSprite);
        // Bugfix 2026-09-06, item 3 - "Card=X / Rarity=N / Frame=Y" so a
        // Data-says-★4-but-Frame-is-still-old case (or the reverse) can be
        // confirmed directly from a real device's logcat rather than review
        // alone. DebugMode-gated (same convention as every other diagnostic
        // log in this project) since SetContent runs once per card shown,
        // on every screen that uses one.
        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
        {
            Debug.Log($"[CardFrame] Card={cardData.Title}  Rarity={cardData.Rarity}  Frame={(frameImage.sprite != null ? frameImage.sprite.name : "null")}");
        }
        // Card UI改修(2026-09-08) - ★はもう常時表示ではなく、showDetails
        // (詳細表示)時のみ。
        if (rarityText != null)
        {
            rarityText.enabled = showDetails;
            rarityText.text = new string('★', Mathf.Clamp(cardData.Rarity <= 0 ? 1 : cardData.Rarity, 1, 5));
        }
        if (levelText != null)
        {
            bool hasLevel = !string.IsNullOrEmpty(cardData.LevelLine);
            levelText.enabled = hasLevel;
            levelText.text = cardData.LevelLine;
        }
        // Card UI改修(2026-09-08) - 所持枚数「×N」。Countが1以下(未設定含
        // む)なら非表示 - 「複数所持している場合」だけ表示する仕様どおり。
        if (countText != null)
        {
            bool hasCount = cardData.Count > 1;
            countText.enabled = hasCount;
            countText.text = hasCount ? $"×{cardData.Count}" : "";
        }
        if (equippedBadge != null) equippedBadge.SetActive(cardData.ShowEquippedBadge);
    }

    // Static (no animation) face-up display for grid-style UI like the Deck
    // Edit screen, which reuses this same component instead of building its
    // own card visuals from scratch. showDetails - see the class-level
    // field comment; defaults to the new simplified face.
    public void ShowFrontImmediate(RewardCardData cardData, bool showDetails = false)
    {
        SetContent(cardData, showDetails);
        backImage.enabled = false;
        if (baseImage != null) baseImage.enabled = true;
        if (titleBandImage != null) titleBandImage.enabled = true;
        frameImage.enabled = true;
        iconBackdrop.enabled = true;
        // Card UI改修(2026-09-08) - textBackdropは今やDescription専用(旧
        // レイアウトのTitle領域はTitleBandImage自身が担うため)。showDetails
        // がfalseの新シンプル表示ではDescription自体を出さないので不要。
        textBackdrop.enabled = showDetails;
        iconImage.enabled = true;
        titleText.enabled = true;
        descriptionText.enabled = showDetails;
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        SetInteractable(true);
    }

    public void SetInteractable(bool value)
    {
        button.interactable = value;
        canvasGroup.blocksRaycasts = value;
    }

    static readonly Color FrameNormalColor = Color.white;
    // Visual Style Ver.1's gold accent, matching the navy+gold+white
    // treatment used everywhere else (HUD, buttons, panels) - was a green
    // tint before, which didn't match anything else in the game.
    static readonly Color FrameSelectedColor = new Color(1f, 0.85f, 0.4f, 1f);
    const float SelectedScale = 1.05f;

    // Used by DeckEditUI to show which cards are currently in the deck -
    // tints the frame art gold and grows it slightly instead of drawing a
    // separate checkmark overlay, so no new art asset is needed.
    public void SetSelected(bool selected)
    {
        frameImage.color = selected ? FrameSelectedColor : FrameNormalColor;
        rect.localScale = Vector3.one * (selected ? SelectedScale : 1f);
    }

    // Scales X down to 0 (looks like an edge-on card), swaps from back to
    // front content at the midpoint, then scales back up - a cheap but
    // convincing flip for Screen Space Overlay UI, which doesn't have a
    // perspective camera to do a true 3D rotation with. totalDuration is
    // split evenly across both halves (Level Up Presentation pass -
    // RewardCardSequence's CardFlipDuration exposes this from the
    // Inspector; the old hardcoded 0.15f+0.15f is just this method's
    // default, unchanged for any other caller).
    public IEnumerator FlipToFront(float totalDuration = 0.3f)
    {
        float half = totalDuration * 0.5f;
        yield return ScaleXTo(0f, half);

        backImage.enabled = false;
        if (baseImage != null) baseImage.enabled = true;
        if (titleBandImage != null) titleBandImage.enabled = true;
        frameImage.enabled = true;
        iconBackdrop.enabled = true;
        textBackdrop.enabled = showDetails;
        iconImage.enabled = true;
        titleText.enabled = true;
        descriptionText.enabled = showDetails;
        // Card UI / Rarity Frame pass - ShowBack() (always called just
        // before this, per every caller's SetContent->ShowBack->
        // FlipToFront sequence) turns these back off, so the flip needs to
        // explicitly restore them here alongside the rest of the front face
        // - data is still whatever SetContent last set (rarityText/
        // levelText's actual displayed text is unaffected by
        // ShowBack/FlipToFront, only their enabled state is). showDetails is
        // likewise whatever the preceding SetContent call was given (Card
        // UI改修2026-09-08).
        if (rarityText != null) rarityText.enabled = showDetails;
        if (levelText != null) levelText.enabled = !string.IsNullOrEmpty(data.LevelLine);
        if (countText != null) countText.enabled = data.Count > 1;
        if (equippedBadge != null) equippedBadge.SetActive(data.ShowEquippedBadge);

        yield return ScaleXTo(1f, half);
    }

    IEnumerator ScaleXTo(float targetX, float duration)
    {
        float startX = rect.localScale.x;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float x = Mathf.Lerp(startX, targetX, Mathf.Clamp01(t));
            Vector3 s = rect.localScale;
            s.x = x;
            rect.localScale = s;
            yield return null;
        }
    }

    public IEnumerator MoveTo(Vector2 targetAnchoredPos, float duration)
    {
        Vector2 start = rect.anchoredPosition;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            rect.anchoredPosition = Vector2.Lerp(start, targetAnchoredPos, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    public IEnumerator ScaleTo(float targetScale, float duration)
    {
        Vector3 start = rect.localScale;
        Vector3 target = Vector3.one * targetScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            rect.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    public IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float start = canvasGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // Quick, self-contained "tap" feedback - scales up briefly then eases
    // down to settleScale (SelectedScale if the card ends up in the deck,
    // 1f otherwise - see SetSelected). Used by DeckEditUI on add/remove
    // instead of a persistent select-then-confirm step, since every tap
    // there already commits immediately.
    public IEnumerator PulseSelect(float settleScale = 1f)
    {
        yield return ScaleTo(1.18f, 0.09f);
        yield return ScaleTo(settleScale, 0.14f);
    }

    // Level Up Presentation pass - a brief, self-contained brightening of
    // the frame border and back to normal, reused for two distinct
    // moments: the "what did I get?" sparkle right as a card's Reveal flip
    // finishes, and the stronger border punch when the player actually taps
    // a card (see RewardCardSequence). Fire-and-forget (StartCoroutine) at
    // both call sites - deliberately NOT tracked/cancelled the way the idle
    // pulse below is, since it always finishes on its own in well under a
    // frame-budget-relevant duration and always ends by restoring
    // FrameNormalColor, so it can't leave the frame stuck off-color even if
    // something else (SetSelected, StopIdlePulse) touches frameImage.color
    // moments later.
    static readonly Color FrameFlashColor = new Color(1f, 0.92f, 0.6f, 1f);

    public IEnumerator FlashFrame(float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float f = Mathf.Clamp01(t);
            // Fast up (first 30%), slower back down - a "flash", not a
            // symmetric pulse.
            float intensity = f < 0.3f ? f / 0.3f : 1f - (f - 0.3f) / 0.7f;
            frameImage.color = Color.Lerp(FrameNormalColor, FrameFlashColor, intensity);
            yield return null;
        }
        frameImage.color = FrameNormalColor;
    }

    // Level Up Presentation pass - a very gentle, continuous frame-border
    // brightness pulse while a card is waiting to be tapped ("選択可能であ
    // ることが自然に分かればOK" - deliberately NOT a scale/position pulse,
    // just a soft alpha-style brightness wobble via Color.Lerp toward
    // FrameFlashColor, so it can't be mistaken for the sharper FlashFrame
    // reveal/select moments above). Tracked via idlePulseCo so
    // StopIdlePulse can cancel it cleanly the instant a card stops waiting
    // (picked, or the other two losing the spotlight) - never left running
    // to fight a later tween for the same frameImage.color.
    Coroutine idlePulseCo;

    public void StartIdlePulse(float minIntensity, float maxIntensity, float period)
    {
        StopIdlePulse();
        idlePulseCo = StartCoroutine(IdlePulseRoutine(minIntensity, maxIntensity, period));
    }

    public void StopIdlePulse()
    {
        if (idlePulseCo != null)
        {
            StopCoroutine(idlePulseCo);
            idlePulseCo = null;
        }
        frameImage.color = FrameNormalColor;
    }

    IEnumerator IdlePulseRoutine(float minIntensity, float maxIntensity, float period)
    {
        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float phase = Mathf.PingPong(t / Mathf.Max(0.001f, period), 1f);
            float intensity = Mathf.Lerp(minIntensity, maxIntensity, phase);
            frameImage.color = Color.Lerp(FrameNormalColor, FrameFlashColor, intensity);
            yield return null;
        }
    }

    public void ResetForReuse()
    {
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        SetInteractable(false);
        gameObject.SetActive(false);
    }
}

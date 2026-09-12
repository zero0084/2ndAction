using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// キャラクター選択画面(2026-09-12) - DeckEditUI/CardFusionUIと同じ
// 「Button.onClick/EventSystemに頼らず自前でタップ位置を各Rectと照合する」
// 方式(このプロジェクトの実機での信頼性の都合)。ただしこの画面はスクロー
// ル可能なグリッドを持たないため、DeckEditUIのようなドラッグ/タップ判別
// までは不要 - RewardCardSequence等と同じ、より単純なTouchInputUtil.
// TryGetTapPosition(「指を下ろした瞬間」を検出するだけ)で十分。
//
// カード一覧(cardSlotRects/cardGlowImages)はCharacterDatabase.AllCharacters
// の件数ぶんSceneBuilder.BuildCharacterSelectCanvasが動的に生成する - 4人
// 目・5人目を追加してもこのクラス自体は変更不要。
//
// 今回のスコープ: 「選ぶ→保存→Homeへ戻る」までの見た目/操作の完成が目的
// で、キャラクターごとの戦闘性能差はまだ実装しない(マスター指示どおり) -
// SELECTが保存するのはGameManager.SelectedCharacterId(表示・次回NEW RUNの
// 既定値としての文字列)のみで、実際のプレイヤーキャラクターのスプライト/
// 性能は今回一切変更しない。
public class CharacterSelectUI : MonoBehaviour
{
    public RectTransform root;
    public CanvasGroup rootGroup;
    public RectTransform backButtonRect;
    public RectTransform selectButtonRect;

    // SceneBuilderがCharacterDatabase.AllCharactersの件数ぶん動的に生成。
    public RectTransform[] cardSlotRects = new RectTransform[0];
    public Image[] cardGlowImages = new Image[0];

    public Image mainVisualImage;
    public CanvasGroup mainVisualGroup;
    public Text titleText;
    public Text subtitleText;
    public Text flavorText;
    public Text roleBadgeText;
    public Image roleBadgeBg;
    public GameObject challengeBadge;
    public Text lifeStarsText;
    public Text powerStarsText;
    public Text speedStarsText;
    public Text comboStarsText;

    // 「派手な演出は不要」との指示どおり短時間 - キャラクター切り替え時の
    // 中央ビジュアルの軽いCrossFade。
    public float crossFadeDuration = 0.1f;
    public float rootFadeDuration = 0.15f;

    int selectedIndex;

    static readonly Color RoleBadgeNormalColor = new Color(0.08f, 0.1f, 0.2f, 0.95f);
    // 「見た目は強そうだが実は最弱」のお嬢様騎士のような特殊枠 - 赤系Badge。
    static readonly Color RoleBadgeChallengeColor = new Color(0.55f, 0.12f, 0.12f, 0.95f);

    Coroutine fadeCoroutine;
    Coroutine visualCrossFadeCoroutine;

    public void Open()
    {
        gameObject.SetActive(true);

        var all = CharacterDatabase.AllCharacters;
        string current = GameManager.Instance != null ? GameManager.Instance.SelectedCharacterId : null;
        selectedIndex = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].characterId == current) { selectedIndex = i; break; }
        }
        RefreshDetail(instant: true);

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            fadeCoroutine = StartCoroutine(FadeCanvasGroup(rootGroup, 1f, rootFadeDuration));
        }
    }

    // BACKでは何も確定しない(選択状態はGameManager.SelectedCharacterIdへ
    // まだ書き込まれていない、Confirm()が呼ばれるまでは画面内のプレビュー
    // に過ぎない) - Acceptance Test「7. BACKではキャラクター変更を確定せず
    // Homeへ戻れる」に対応。
    public void Close()
    {
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                gameObject.SetActive(false);
                if (GameManager.Instance != null) GameManager.Instance.CloseCharacterSelect();
            });
            return;
        }

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(CloseFadeRoutine());
    }

    IEnumerator CloseFadeRoutine()
    {
        if (rootGroup != null) yield return FadeCanvasGroup(rootGroup, 0f, rootFadeDuration);
        gameObject.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.CloseCharacterSelect();
    }

    IEnumerator FadeCanvasGroup(CanvasGroup group, float target, float duration)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    void Confirm()
    {
        var all = CharacterDatabase.AllCharacters;
        if (selectedIndex < 0 || selectedIndex >= all.Count) return;
        // Active Run / Checkpointには一切触れない(GameManager.
        // SetSelectedCharacterのコメント参照) - 「選択キャラクター=次回
        // NEW RUNで使用するキャラクター」という仕様どおり。
        if (GameManager.Instance != null) GameManager.Instance.SetSelectedCharacter(all[selectedIndex].characterId);
        Close();
    }

    void SelectIndex(int index)
    {
        if (index == selectedIndex) return;
        selectedIndex = index;
        RefreshDetail(instant: false);
    }

    void RefreshDetail(bool instant)
    {
        var all = CharacterDatabase.AllCharacters;
        if (selectedIndex < 0 || selectedIndex >= all.Count) return;
        CharacterDefinition def = all[selectedIndex];

        for (int i = 0; i < cardGlowImages.Length; i++)
        {
            if (cardGlowImages[i] != null) cardGlowImages[i].gameObject.SetActive(i == selectedIndex);
        }

        if (titleText != null) titleText.text = def.displayName;
        if (subtitleText != null) subtitleText.text = def.subtitle;
        if (flavorText != null) flavorText.text = def.flavorText;
        if (roleBadgeText != null) roleBadgeText.text = def.role;
        if (roleBadgeBg != null) roleBadgeBg.color = def.challengeFlag ? RoleBadgeChallengeColor : RoleBadgeNormalColor;
        if (challengeBadge != null) challengeBadge.SetActive(def.challengeFlag);

        if (lifeStarsText != null) lifeStarsText.text = StarString(def.lifeRating);
        if (powerStarsText != null) powerStarsText.text = StarString(def.powerRating);
        if (speedStarsText != null) speedStarsText.text = StarString(def.speedRating);
        if (comboStarsText != null) comboStarsText.text = StarString(def.comboRating);

        if (mainVisualImage != null)
        {
            Sprite sprite = ToSprite(def.mainVisual);
            if (instant || mainVisualGroup == null)
            {
                mainVisualImage.sprite = sprite;
                if (mainVisualGroup != null) mainVisualGroup.alpha = 1f;
            }
            else
            {
                if (visualCrossFadeCoroutine != null) StopCoroutine(visualCrossFadeCoroutine);
                visualCrossFadeCoroutine = StartCoroutine(CrossFadeMainVisual(sprite));
            }
        }
    }

    IEnumerator CrossFadeMainVisual(Sprite newSprite)
    {
        yield return FadeCanvasGroup(mainVisualGroup, 0f, crossFadeDuration);
        mainVisualImage.sprite = newSprite;
        yield return FadeCanvasGroup(mainVisualGroup, 1f, crossFadeDuration);
    }

    static string StarString(int rating)
    {
        int filled = Mathf.Clamp(rating, 0, 5);
        return new string('★', filled) + new string('☆', 5 - filled);
    }

    // RewardCardUI.SetContentと同じ「呼び出しのたびにTexture2DからSprite.
    // Createする」方式(このデータはキャラクター選択画面でしか使わない
    // ため、CharacterDefinition自体にSpriteをキャッシュする必要はない)。
    static Sprite ToSprite(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    void HandleTap(Vector2 screenPos)
    {
        if (backButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(backButtonRect, screenPos, null))
        {
            Close();
            return;
        }
        if (selectButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(selectButtonRect, screenPos, null))
        {
            Confirm();
            return;
        }
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] != null && RectTransformUtility.RectangleContainsScreenPoint(cardSlotRects[i], screenPos, null))
            {
                SelectIndex(i);
                return;
            }
        }
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!TouchInputUtil.TryGetTapPosition(out Vector2 screenPos)) return;
        HandleTap(screenPos);
    }
}

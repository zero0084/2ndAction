using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// キャラクター選択画面(2026-09-12) - DeckEditUI/CardFusionUIと同じ
// 「Button.onClick/EventSystemに頼らず自前でタップ位置を各Rectと照合する」
// 方式(このプロジェクトの実機での信頼性の都合)。
//
// カルーセル化(2026-09-24) - 当初はスクロール不要な単純なタップのみの
// 画面だったが、4人目の拳銃士追加を機に「5人目以降も自然に追加できる
// 横スクロールカルーセル」へマスター指示で改修した。ドラッグ/タップ判別
// はDeckEditUI.Update(Assets/Scripts/DeckEditUI.cs)と全く同じ手法
// (指の移動距離がdragTapThresholdを超えたらドラッグ、超えなければ離した
// 瞬間にタップ)を流用している。
//
// カード一覧(cardSlotRects/cardGlowImages)はCharacterDatabase.AllCharacters
// の件数ぶんSceneBuilder.BuildCharacterSelectCanvasが動的に生成する - 5人
// 目以降を追加してもこのクラス自体は変更不要(カード幅/間隔は固定のまま
// 横スクロールで収まる)。
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

    // カルーセル化(2026-09-24) - SceneBuilder.BuildCharacterSelectCanvasが
    // 構築するScrollRect/Content。ドラッグ処理自体はDeckEditUIと同じ
    // 「ScrollRectは状態コンテナとして使うだけで、実際の移動は自前で行う」
    // 方式でこのクラスが担う。
    public ScrollRect carouselScroll;
    public RectTransform carouselContent;
    public float carouselViewportWidth;
    public float cardStride; // = カード幅+間隔
    public float cardWidth = 210f;
    // 12人化(2026-09-28): その先にカードがある側だけ出す矢印(タップで1枚移動)と「何人目/全員」。
    public RectTransform carouselArrowLeft;
    public RectTransform carouselArrowRight;
    public Text carouselPageText;

    public float dragTapThreshold = 14f;
    public float snapDuration = 0.22f;
    public AnimationCurve snapCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float cardSelectedScale = 1.15f;
    static readonly Color CardGlowColor = new Color(1f, 0.85f, 0.4f, 0.95f);

    bool pointerActive;
    Vector2 pointerDownScreenPos;
    Vector2 lastViewportLocalPos;
    bool draggedPastThreshold;
    bool draggingCarousel;

    bool snapping;
    float snapStartX, snapTargetX, snapElapsed;

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

    // デッキ編集のキャラカード枠から戻ってきた時: そのキャラを表示したまま開く(選択は確定しない)
    public void OpenAt(string characterId)
    {
        openAtId = characterId;
        Open();
        if (rootGroup != null) { if (fadeCoroutine != null) StopCoroutine(fadeCoroutine); rootGroup.alpha = 1f; }
    }
    string openAtId;

    // 画面切り替えで覆っている間に、演出なしで閉じる(デッキ編集へ行く時)
    public void HideImmediate()
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        gameObject.SetActive(false);
    }

    // ===== キャラごとのキャラカード(2026-10-02): 情報パネルの下にそのキャラの3枠と「カード設定」 =====
    Text charCardsText;
    RectTransform charCardsButton;
    void EnsureCharCardRow()
    {
        if (charCardsText != null || flavorText == null) return;
        var info = flavorText.rectTransform.parent as RectTransform;
        if (info == null) return;
        var go = new GameObject("CharacterCardsRow");
        go.transform.SetParent(info, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(-80f - 190f, 96f);
        r.anchoredPosition = new Vector2(-95f, -652f);
        charCardsText = go.AddComponent<Text>();
        charCardsText.font = flavorText.font;
        charCardsText.fontSize = 18;
        charCardsText.supportRichText = true;
        charCardsText.alignment = TextAnchor.UpperLeft;
        charCardsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        charCardsText.verticalOverflow = VerticalWrapMode.Truncate;
        charCardsText.resizeTextForBestFit = true; charCardsText.resizeTextMinSize = 12; charCardsText.resizeTextMaxSize = 18;
        charCardsText.color = new Color(0.92f, 0.93f, 0.97f);
        charCardsText.raycastTarget = false;

        var bgo = new GameObject("CharacterCardsButton");
        bgo.transform.SetParent(info, false);
        charCardsButton = bgo.AddComponent<RectTransform>();
        charCardsButton.anchorMin = charCardsButton.anchorMax = new Vector2(1f, 1f);
        charCardsButton.pivot = new Vector2(1f, 1f);
        charCardsButton.sizeDelta = new Vector2(176f, 60f);
        charCardsButton.anchoredPosition = new Vector2(-36f, -660f);
        var bg = bgo.AddComponent<Image>();
        if (roleBadgeBg != null) { bg.sprite = roleBadgeBg.sprite; bg.type = roleBadgeBg.type; }
        bg.color = new Color(0.55f, 0.4f, 0.12f, 0.95f);
        bg.raycastTarget = false;
        var lgo = new GameObject("Label");
        lgo.transform.SetParent(bgo.transform, false);
        var lr = lgo.AddComponent<RectTransform>();
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
        var lt = lgo.AddComponent<Text>();
        lt.font = flavorText.font; lt.fontSize = 20; lt.fontStyle = FontStyle.Bold; lt.alignment = TextAnchor.MiddleCenter;
        lt.color = new Color(1f, 0.93f, 0.7f); lt.text = Loc.T("カード設定") + " ▶"; lt.raycastTarget = false;
    }

    void RefreshCharCards(CharacterDefinition def)
    {
        EnsureCharCardRow();
        var gm = GameManager.Instance;
        if (charCardsText == null || gm == null || def == null) return;
        var slots = gm.GetCharacterCardsOf(def.characterId);
        // 2026-10-22: 「(空き) / (空き) / (空き)」の文字だけだった → 見出しの下に小さなカード枠を3つ(装備中はカードの絵、空きは「+」)
        charCardsText.text = Loc.T("<color=#FFD866><b>キャラカード</b></color>"); // 2026-10-23: 訳の表にあるのはこの形(「キャラカード」だけでは訳が無く日本語のままだった)
        EnsureCharCardSlots(slots.ids.Length);
        for (int i = 0; i < charSlotViews.Length; i++)
        {
            string id = i < slots.ids.Length ? slots.ids[i] : null;
            CardDefinition c = string.IsNullOrEmpty(id) ? null : CardDatabase.FindById(id);
            var v = charSlotViews[i];
            v.art.texture = c != null ? c.icon : null;
            v.art.enabled = c != null && c.icon != null;
            v.plus.enabled = c == null;
            v.frame.effectColor = c != null ? UiTheme.GoldBright : new Color(UiTheme.GoldEdge.r, UiTheme.GoldEdge.g, UiTheme.GoldEdge.b, 0.55f);
            v.level.text = c != null && !CardVariant.IsVariantKey(id) ? "Lv" + Mathf.Max(1, slots.levels[i]) : "";
        }
    }

    struct CharSlotView { public RawImage art; public Image plus; public Outline frame; public Text level; }
    CharSlotView[] charSlotViews = new CharSlotView[0];
    void EnsureCharCardSlots(int n)
    {
        if (charSlotViews.Length == n || charCardsText == null) return;
        var info = charCardsText.rectTransform.parent as RectTransform;
        charSlotViews = new CharSlotView[n];
        const float w = 44f, h = 64f, gap = 12f, left = 40f, top = -652f - 28f;
        for (int i = 0; i < n; i++)
        {
            var r = new GameObject("CharCardSlot" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(info, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(left + i * (w + gap), top);
            var bg = r.gameObject.AddComponent<Image>(); bg.color = UiTheme.ButtonFill; bg.raycastTarget = false;
            var ol = r.gameObject.AddComponent<Outline>(); ol.effectDistance = new Vector2(1.5f, -1.5f); ol.useGraphicAlpha = false;
            var ar = new GameObject("Art", typeof(RectTransform)).GetComponent<RectTransform>();
            ar.SetParent(r, false); ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one; ar.offsetMin = new Vector2(3f, 3f); ar.offsetMax = new Vector2(-3f, -3f);
            var art = ar.gameObject.AddComponent<RawImage>(); art.raycastTarget = false;
            var pr = new GameObject("Plus", typeof(RectTransform)).GetComponent<RectTransform>();
            pr.SetParent(r, false); pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(24f, 24f);
            var plus = pr.gameObject.AddComponent<Image>(); plus.sprite = UiIcons.PlusSprite; plus.preserveAspect = true; plus.raycastTarget = false;
            plus.color = new Color(1f, 1f, 1f, 0.45f);
            var lr = new GameObject("Lv", typeof(RectTransform)).GetComponent<RectTransform>();
            lr.SetParent(r, false); lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(0.5f, 0f);
            lr.sizeDelta = new Vector2(0f, 18f); lr.anchoredPosition = new Vector2(0f, 2f);
            var lv = lr.gameObject.AddComponent<Text>(); lv.font = charCardsText.font; lv.fontSize = 14; lv.fontStyle = FontStyle.Bold;
            lv.alignment = TextAnchor.MiddleCenter; lv.color = UiTheme.GoldText; lv.raycastTarget = false;
            lr.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            charSlotViews[i] = new CharSlotView { art = art, plus = plus, frame = ol, level = lv };
        }
    }

    // ---- 縦画面(2026-10-08、依頼E-1): 上に 立ち絵(左)+情報(右)、その下に横いっぱいのカルーセル。横画面は作った時の配置に戻す
    struct RtState { public Vector2 aMin, aMax, pivot, pos, size; }
    static RtState Grab(RectTransform r) => new RtState { aMin = r.anchorMin, aMax = r.anchorMax, pivot = r.pivot, pos = r.anchoredPosition, size = r.sizeDelta };
    static void Put(RectTransform r, RtState st) { r.anchorMin = st.aMin; r.anchorMax = st.aMax; r.pivot = st.pivot; r.anchoredPosition = st.pos; r.sizeDelta = st.size; }
    RectTransform[] layoutRts; RtState[] layoutHome; float carouselWidthHome;
    bool layoutPortrait, layoutDone;
    Vector2 layoutSize;
    void ApplyOrientationLayout()
    {
        var rootRt = root != null ? root : transform as RectTransform;
        bool portrait = PortraitRunView.IsPortraitScreen;
        if (layoutDone && portrait == layoutPortrait && rootRt.rect.size == layoutSize) return;
        if (layoutRts == null)
        {
            layoutRts = new[] { mainVisualImage != null ? mainVisualImage.rectTransform : null, flavorText != null ? flavorText.rectTransform.parent as RectTransform : null,
                carouselScroll != null ? (RectTransform)carouselScroll.transform : null, carouselArrowLeft, carouselArrowRight, carouselPageText != null ? carouselPageText.rectTransform : null };
            layoutHome = new RtState[layoutRts.Length];
            for (int i = 0; i < layoutRts.Length; i++) if (layoutRts[i] != null) layoutHome[i] = Grab(layoutRts[i]);
            carouselWidthHome = carouselViewportWidth;
        }
        layoutDone = true; layoutPortrait = portrait; layoutSize = rootRt.rect.size;
        // 2026-10-09(依頼G-2): 戻る/決定の置き方を画面共通に(UiConventions)
        UiConventions.PlaceBack(backButtonRect);
        var header = rootRt.Find("HeaderTitle") as RectTransform;
        if (header != null) header.anchoredPosition = new Vector2(UiConventions.AfterBackX, -30f);
        UiConventions.PlaceConfirm(selectButtonRect, portrait, rootRt.rect.width, new Vector2(280f, 76f), new Vector2(-56f, 40f));
        for (int i = 0; i < layoutRts.Length; i++) if (layoutRts[i] != null) Put(layoutRts[i], layoutHome[i]);
        carouselViewportWidth = carouselWidthHome;
        if (portrait)
        {
            float W = rootRt.rect.width, H = rootRt.rect.height, m = 30f;
            float iw = Mathf.Min(560f, (W - 3f * m) * 0.54f), vw = W - 3f * m - iw, vh = Mathf.Min(880f, vw * 1.57f, 780f);
            float top = H * 0.5f - 190f, cy = top - 390f; // 右上の設定ボタンの下から
            var mid = new Vector2(0.5f, 0.5f);
            if (layoutRts[0] != null) { var v = layoutRts[0]; v.anchorMin = v.anchorMax = v.pivot = mid; v.sizeDelta = new Vector2(vw, vh); v.anchoredPosition = new Vector2(-W * 0.5f + m + vw * 0.5f, cy); }
            if (layoutRts[1] != null) { var v = layoutRts[1]; v.anchorMin = v.anchorMax = v.pivot = mid; v.sizeDelta = new Vector2(iw, 780f); v.anchoredPosition = new Vector2(W * 0.5f - m - iw * 0.5f, cy); }
            float rowH = layoutHome[2].size.y;
            float freeTop = top - 780f - 20f, freeBottom = -H * 0.5f + 140f; // 情報の下 〜 戻る/決定の上 の真ん中
            float carY = Mathf.Min(freeTop - rowH * 0.5f, (freeTop + freeBottom) * 0.5f + 20f);
            carouselViewportWidth = W - 2f * m;
            if (layoutRts[2] != null) { var v = layoutRts[2]; v.anchorMin = v.anchorMax = new Vector2(0f, 0.5f); v.pivot = new Vector2(0f, 0.5f); v.sizeDelta = new Vector2(carouselViewportWidth, rowH); v.anchoredPosition = new Vector2(m, carY); }
            if (layoutRts[3] != null) layoutRts[3].anchoredPosition = new Vector2(m + 20f, carY);
            if (layoutRts[4] != null) layoutRts[4].anchoredPosition = new Vector2(m + carouselViewportWidth - 20f, carY);
            if (layoutRts[5] != null) layoutRts[5].anchoredPosition = new Vector2(m + carouselViewportWidth * 0.5f, carY - rowH * 0.5f - 4f);
        }
        if (carouselContent != null && VisibleCount > 0) carouselContent.anchoredPosition = new Vector2(TargetContentXForIndex(selectedIndex), carouselContent.anchoredPosition.y);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        layoutDone = false;
        // 2026-10-22: 動かすのはこのクラス自身。ScrollRect(Clamped)が残っていると、端のカードを中央へ寄せた位置を枠の端へ引き戻すので止める
        // (Viewport と RectMask2D はそのまま使う)
        if (carouselScroll != null) carouselScroll.enabled = false;
        // 2026-10-20: 左右の矢印の文字「<」「>」を白銀の絵へ(一時停止・閉じると同じ操作の記号。2回目以降は何もしない)
        foreach (var (a, d) in new[] { (carouselArrowLeft, -1), (carouselArrowRight, 1) })
            if (a != null) UiIcons.ReplaceGlyph(a.Find("Glyph") != null ? a.Find("Glyph").GetComponent<Text>() : null, d, 76f);
        Canvas.ForceUpdateCanvases();
        ApplyOrientationLayout();

        var all = CharacterDatabase.AllCharacters;
        string current = !string.IsNullOrEmpty(openAtId) ? openAtId : GameManager.Instance != null ? GameManager.Instance.SelectedCharacterId : null;
        openAtId = null;
        markSeenOnApply = true; // 2026-10-08: 開いた時点で NEW を「見た」にする(この表示では NEW を付けたまま)
        ApplyUnlockState();
        markSeenOnApply = false;
        selectedIndex = 0;
        for (int i = 0; i < VisibleCount; i++)
        {
            if (DefAt(i).characterId == current) { selectedIndex = i; break; }
        }
        RefreshDetail(instant: true);

        // カルーセルを開いた瞬間、アニメーションなしで選択中キャラの位置
        // へ即座に合わせる(初回表示でスナップアニメーションが走って
        // しまうのを防ぐ)。
        pointerActive = false;
        draggingCarousel = false;
        snapping = false;
        if (carouselContent != null)
        {
            carouselContent.anchoredPosition = new Vector2(TargetContentXForIndex(selectedIndex), carouselContent.anchoredPosition.y);
        }
        UpdateCardVisuals();

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
            if (!suppressCloseSe) if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
            suppressCloseSe = false;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                gameObject.SetActive(false);
                if (GameManager.Instance != null) GameManager.Instance.CloseCharacterSelect();
            }, ScreenTransitionManager.Style.Fade);
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

    bool suppressCloseSe; // 決定で閉じる時はキャンセル音を鳴らさない
    void Confirm()
    {
        var all = CharacterDatabase.AllCharacters;
        if (selectedIndex < 0 || selectedIndex >= VisibleCount) return;
        if (!UnlockRules.IsCharacterUnlocked(DefAt(selectedIndex).characterId))
        {
            // 未解放: 選べない(条件は右の説明に出ている)
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
            lockDenyFlash = 0.5f;
            return;
        }
        // Active Run / Checkpointには一切触れない(GameManager.
        // SetSelectedCharacterのコメント参照) - 「選択キャラクター=次回
        // NEW RUNで使用するキャラクター」という仕様どおり。
        if (GameManager.Instance != null) GameManager.Instance.SetSelectedCharacter(DefAt(selectedIndex).characterId);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        suppressCloseSe = true;
        Close();
    }

    void SelectIndex(int index)
    {
        if (index == selectedIndex) return;
        selectedIndex = index;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CharacterSelect);
        RefreshDetail(instant: false);
    }

    void RefreshDetail(bool instant)
    {
        var all = CharacterDatabase.AllCharacters;
        if (selectedIndex < 0 || selectedIndex >= VisibleCount) return;
        CharacterDefinition def = DefAt(selectedIndex);

        // カルーセル化(2026-09-24) - 発光の強弱はUpdateCardVisualsが中心
        // からの距離に応じて毎フレーム連続的に更新するため、ここでの
        // バイナリSetActiveは廃止した。

        if (titleText != null) titleText.text = Loc.Auto(def.displayName);
        RefreshCharCards(def);
        bool unlocked = UnlockRules.IsCharacterUnlocked(def.characterId);
        if (subtitleText != null) subtitleText.text = Loc.Auto(unlocked ? def.subtitle : "LOCKED");
        UguiScrollText.Wrap(flavorText); // 2026-10-08: 長い説明は枠の中でスクロール
        if (flavorText != null) flavorText.text = Loc.Auto(def.flavorText); // 2026-10-08: 未解放は一覧に出ないので条件は出さない
        if (roleBadgeText != null) roleBadgeText.text = Loc.Auto(def.role);
        if (roleBadgeBg != null) roleBadgeBg.color = def.challengeFlag ? RoleBadgeChallengeColor : RoleBadgeNormalColor;
        if (challengeBadge != null) challengeBadge.SetActive(def.challengeFlag);

        if (lifeStarsText != null) lifeStarsText.text = Loc.Auto(StarString(def.lifeRating));
        if (powerStarsText != null) powerStarsText.text = Loc.Auto(StarString(def.powerRating));
        if (speedStarsText != null) speedStarsText.text = Loc.Auto(StarString(def.speedRating));
        if (comboStarsText != null) comboStarsText.text = Loc.Auto(StarString(def.comboRating));
        SetGauge(lifeStarsText, def.lifeRating); SetGauge(powerStarsText, def.powerRating);
        SetGauge(speedStarsText, def.speedRating); SetGauge(comboStarsText, def.comboRating);

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

    // 2026-10-22: 能力は ★☆ の文字ではなく 5 段のゲージ(★の文字は隠して残す: 値は同じ)。段は角の無い四角、満ちた段は選択中と同じ金
    readonly System.Collections.Generic.Dictionary<Text, Image[]> gauges = new System.Collections.Generic.Dictionary<Text, Image[]>();
    static readonly Color GaugeEmpty = new Color(1f, 1f, 1f, 0.13f);
    void SetGauge(Text starText, int rating)
    {
        if (starText == null) return;
        if (!gauges.TryGetValue(starText, out var segs))
        {
            var tr = starText.rectTransform;
            var box = new GameObject("Gauge", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(tr.parent, false);
            box.anchorMin = tr.anchorMin; box.anchorMax = tr.anchorMax; box.pivot = tr.pivot;
            box.anchoredPosition = tr.anchoredPosition; box.sizeDelta = tr.sizeDelta;
            float w = Mathf.Max(60f, tr.rect.width), h = Mathf.Max(10f, tr.rect.height);
            const float gap = 5f, segH = 14f;
            float segW = Mathf.Min(30f, (w - gap * 4f) / 5f);
            float total = segW * 5f + gap * 4f;
            segs = new Image[5];
            for (int k = 0; k < 5; k++)
            {
                var s = new GameObject("Seg" + k, typeof(RectTransform)).GetComponent<RectTransform>();
                s.SetParent(box, false);
                s.anchorMin = s.anchorMax = new Vector2(1f, 0.5f); s.pivot = new Vector2(1f, 0.5f); // 右寄せ(今の★と同じ位置)
                s.sizeDelta = new Vector2(segW, segH);
                s.anchoredPosition = new Vector2(-(total - (k + 1) * segW - k * gap), 0f);
                segs[k] = s.gameObject.AddComponent<Image>();
                segs[k].raycastTarget = false;
            }
            gauges[starText] = segs;
            starText.enabled = false;
        }
        int filled = Mathf.Clamp(rating, 0, 5);
        for (int k = 0; k < 5; k++) segs[k].color = k < filled ? UiTheme.GoldBright : GaugeEmpty;
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
        if (backButtonRect != null && UiHit.Hit(backButtonRect, screenPos))
        {
            Close();
            return;
        }
        if (selectButtonRect != null && UiHit.Hit(selectButtonRect, screenPos))
        {
            Confirm();
            return;
        }
        if (charCardsButton != null && UiHit.Hit(charCardsButton, screenPos))
        {
            var all = CharacterDatabase.AllCharacters;
            if (selectedIndex >= 0 && selectedIndex < VisibleCount && GameManager.Instance != null)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                GameManager.Instance.OpenCharacterCardsFromSelect(DefAt(selectedIndex).characterId);
            }
            return;
        }
        // 左右の矢印(カードより先に判定 - 矢印はカード列の端に重なって置いてある)
        if (carouselArrowLeft != null && carouselArrowLeft.gameObject.activeSelf && UiHit.Hit(carouselArrowLeft, screenPos))
        {
            BeginSnap(Mathf.Max(0, selectedIndex - 1));
            return;
        }
        if (carouselArrowRight != null && carouselArrowRight.gameObject.activeSelf && UiHit.Hit(carouselArrowRight, screenPos))
        {
            BeginSnap(Mathf.Min(VisibleCount - 1, selectedIndex + 1));
            return;
        }
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] != null && cardSlotRects[i].gameObject.activeSelf && UiHit.Hit(cardSlotRects[i], screenPos))
            {
                int pos = PosOfSlot(i); if (pos < 0) return;
                // カルーセル化(2026-09-24) - 中央にないカード(見切れて
                // いるカード)をタップした場合も、選択と同時にそのカードを
                // 中央付近へスナップさせる(マスター指示「タップで即座に
                // そのカードが選択・中央化される」)。
                BeginSnap(pos);
                return;
            }
        }
    }

    // カルーセル化(2026-09-24) - DeckEditUI.Update(Assets/Scripts/
    // DeckEditUI.cs)と同じ「Input.GetMouseButton*/Input.GetTouch(0).phase
    // を直接読み、指の移動距離がdragTapThresholdを超えたらドラッグ、
    // 超えなければ離した瞬間にタップとして処理する」手法を横方向の
    // カルーセルへ適用したもの。ScrollRectは状態コンテナ(viewport/
    // content参照)として使うだけで、実際の移動はcarouselContent.
    // anchoredPositionを直接動かす。
    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        // Input Lock(ブラッシュアップ点検2026-09-18で追加) - DeckEditUI.
        // Updateと同じガード。これが無いと、SELECT確定後の画面遷移
        // (ScreenTransitionManagerのCloseRoutine、約0.25秒)が終わる前に
        // 別カードをタップ→再度SELECTタップができてしまい、既に確定した
        // はずの選択が遷移中に別のキャラクターへ静かに上書きされる不具合
        // があった。
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (UiInputGate.Blocked) return; // 設定/DEBUGパネルが手前に開いている(閉じた時の指が離れるまでも)
        // 画面の向き/大きさが変わった: 並べ直して、押しかけの指(ドラッグ)は捨てる(選択はそのまま)
        if (layoutDone && (layoutPortrait != PortraitRunView.IsPortraitScreen || (root != null && root.rect.size != layoutSize)))
        {
            pointerActive = false; draggingCarousel = false; snapping = false;
            ApplyOrientationLayout(); UpdateCardVisuals(); UiInputGate.LatchUntilRelease(); return;
        }

        UiHit.Probe(HandleTap); // パッド操作中: 押せる枠を集める
        // タッチ/マウス + ゲームパッドの決定(フォーカスの中心を叩く)。2026-10-06
        PointerInput.Read(this, out bool down, out bool up, out bool held, out Vector2 screenPos);

        // 不具合修正(2026-09-24、目視確認で発覚) - down/held/upを互いに
        // 排他的なif/else-ifチェーンにしていたため、ごく短い時間で down→up
        // が発生し両方が同一Update()フレーム内でtrueになるケース(自動操作
        // ツールでの高速クリック等)で、down分岐だけが実行されup分岐が
        // 丸ごと読み飛ばされ、タップが一切解決されないまま固まる不具合が
        // あった(ドラッグは複数フレームにまたがるため無関係、SELECT/BACK
        // のような単純な一瞬のクリックだけが影響を受けていた)。downとup
        // を独立したif文にし、同一フレームでも両方処理されるようにした。
        if (down)
        {
            pointerActive = true;
            pointerDownScreenPos = screenPos;
            draggedPastThreshold = false;
            draggingCarousel = carouselScroll != null && RectTransformUtility.RectangleContainsScreenPoint(carouselScroll.viewport, screenPos, null);
            if (draggingCarousel)
            {
                // 新規ドラッグ開始でスナップ中アニメーションは打ち切る
                // (BACK/SELECTタップ自体はここでブロックしない - draggingCarousel
                // はカルーセル領域内で押された場合のみtrueになる)。
                snapping = false;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(carouselScroll.viewport, screenPos, null, out lastViewportLocalPos);
            }
        }
        if (pointerActive && held)
        {
            if (!draggedPastThreshold && Vector2.Distance(screenPos, pointerDownScreenPos) > dragTapThreshold)
            {
                draggedPastThreshold = true;
            }
            if (draggedPastThreshold && draggingCarousel && carouselContent != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(carouselScroll.viewport, screenPos, null, out Vector2 currentLocalPos);
                float deltaX = currentLocalPos.x - lastViewportLocalPos.x;
                lastViewportLocalPos = currentLocalPos;
                Vector2 pos = carouselContent.anchoredPosition;
                pos.x = ClampContentX(pos.x + deltaX);
                carouselContent.anchoredPosition = pos;
            }
        }
        if (pointerActive && up)
        {
            pointerActive = false;
            if (!draggedPastThreshold)
            {
                HandleTap(pointerDownScreenPos);
            }
            else if (draggingCarousel)
            {
                BeginSnap(ComputeNearestCardIndex());
            }
            draggingCarousel = false;
        }

        UpdateCardVisuals();
        if (snapping) UpdateSnapAnimation();
        UpdateCarouselHints();
    }

    // 左右にまだカードがある(=カード列がViewportの外へ続いている)側だけ矢印を出し、軽く明滅させる。
    void UpdateCarouselHints()
    {
        int count = VisibleCount;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
        SetArrow(carouselArrowLeft, selectedIndex > 0, pulse);
        SetArrow(carouselArrowRight, selectedIndex < count - 1, pulse);
        if (carouselPageText != null) carouselPageText.text = Loc.Auto(count > 0 ? $"{selectedIndex + 1} / {count}" : "");
    }

    static void SetArrow(RectTransform arrow, bool show, float pulse)
    {
        if (arrow == null) return;
        if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
        if (show) arrow.localScale = Vector3.one * (0.94f + 0.06f * pulse);
    }

    // content.anchoredPosition.xが取り得る範囲([minX, 0])。カード列全体が
    // Viewportより狭い(キャラ数が少ない)場合はminX>0になり得るため、
    // その場合は0で頭打ちにする(コンテンツを右へはみ出させない)。
    // 2026-10-22(画面側の完成度向上): 端のカードも Viewport の中央まで来られるようにした。以前は端のカードが枠の端に寄って止まり、
    // 選んだ時の拡大(cardSelectedScale)で枠の外へはみ出した分が切れて見えていた。今は選んだカードはいつも中央・全体が見える
    float ClampContentX(float x)
    {
        if (carouselContent == null) return 0f;
        float maxX = (carouselViewportWidth - cardWidth) * 0.5f;
        float minX = maxX - Mathf.Max(0, VisibleCount - 1) * cardStride;
        return Mathf.Clamp(x, minX, maxX);
    }

    // 指定したカードのインデックスをできる限りViewport中央へ持ってくる
    // ためのcontent.anchoredPosition.x目標値(端のカードはClampContentXで
    // 自然に頭打ちになり、そのカードがViewportの端いっぱいに寄る - 「まだ
    // 先がある」ことが伝わる見切れ方になる)。
    float TargetContentXForIndex(int index)
    {
        float centerOffset = (carouselViewportWidth - cardWidth) * 0.5f;
        return ClampContentX(centerOffset - index * cardStride);
    }

    // 不具合修正(2026-09-24、目視確認で発覚、3段階) - 当初はTargetContentX
    // ForIndexの逆算で近似していたが、端のカードはClampContentXで複数の
    // indexが同じclamp後の位置へ収束するため逆算式では末尾のカードへ絶対
    // に辿り着けなかった。次に「現在位置と各カードの目標位置との距離、
    // 同距離なら奥のindex優先」に変えたが、今度は先頭側(index0とindex1が
    // 両方ともcontent.x=0にclampされる)で逆に先頭のキャラ(swordsman)へ
    // 辿り着けなくなった - 「同距離なら奥を優先」という固定ルールが先頭
    // 側には合わなかった。4人という少人数構成(スクロール可能範囲178pxに
    // 対しカード幅210px)では、先頭・末尾どちらの極でも複数indexの目標が
    // 同じ位置に収束しうるため、固定のtie-break方向では両端を同時に正しく
    // 扱えない。最終的に「content.xが実際に左端(0)/右端(minX)いっぱいまで
    // 到達しているかどうか」を先に明示的に判定し、その場合はそれぞれ
    // 先頭/末尾のindexへ直接確定させる(中間位置のみViewport中央への実
    // 距離で判定する)方式にした。
    int ComputeNearestCardIndex()
    {
        var all = CharacterDatabase.AllCharacters;
        if (carouselContent == null || all.Count == 0) return selectedIndex;
        float currentX = carouselContent.anchoredPosition.x; // 2026-10-22: どのカードも中央へ来られるので、中央に一番近いカード

        float viewportCenterX = carouselViewportWidth * 0.5f;
        int nearest = 0;
        float bestDist = float.MaxValue;
        for (int k = 0; k < VisibleCount; k++)
        {
            var slot = SlotAt(k);
            if (slot == null) continue;
            float cardCenterX = currentX + slot.anchoredPosition.x + cardWidth * 0.5f;
            float dist = Mathf.Abs(cardCenterX - viewportCenterX);
            if (dist < bestDist) { bestDist = dist; nearest = k; }
        }
        return nearest;
    }

    void BeginSnap(int index)
    {
        SelectIndex(index);
        snapTargetX = TargetContentXForIndex(index);
        snapStartX = carouselContent != null ? carouselContent.anchoredPosition.x : 0f;
        snapElapsed = 0f;
        snapping = true;
    }

    void UpdateSnapAnimation()
    {
        if (carouselContent == null) { snapping = false; return; }
        snapElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(snapElapsed / Mathf.Max(0.01f, snapDuration));
        float eased = snapCurve.Evaluate(t);
        Vector2 pos = carouselContent.anchoredPosition;
        pos.x = Mathf.Lerp(snapStartX, snapTargetX, eased);
        carouselContent.anchoredPosition = pos;
        if (t >= 1f) snapping = false;
    }

    // 毎フレーム(ドラッグ中・スナップ中・静止中いずれも)、選択中カード
    // (selectedIndex)の現在位置からの距離に応じてカードの拡大率と発光の
    // 濃さを連続的に更新する(旧実装のバイナリSetActiveを置き換え)。
    //
    // 不具合修正(2026-09-24、目視確認で発覚) - 当初はViewportの幾何学的
    // 中央からの距離で計算していたが、先頭/末尾のカードはClampContentXに
    // よってViewport中央まで届かないままスナップが止まる(=選択中でも
    // 中央に来ない)ため、選択中のはずのカードではなく偶然中央に近い別の
    // カードの方が強調される、という選択状態とビジュアルの食い違いが発生
    // した。基準点をViewportの固定中央ではなく「選択中カード自身の現在
    // 位置」に変更し、選択中カードが常にfocus=1になることを保証する。
    void UpdateCardVisuals()
    {
        if (carouselContent == null || cardStride <= 0f) return;
        FitGlows();
        float selectedCardCenterX = carouselContent.anchoredPosition.x + selectedIndex * cardStride + cardWidth * 0.5f;
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] == null) continue;
            float cardCenterX = carouselContent.anchoredPosition.x + cardSlotRects[i].anchoredPosition.x + cardWidth * 0.5f;
            float normalizedDist = Mathf.Clamp01(Mathf.Abs(cardCenterX - selectedCardCenterX) / cardStride);
            float focus = 1f - normalizedDist;
            cardSlotRects[i].localScale = Vector3.one * Mathf.Lerp(1f, cardSelectedScale, focus);
            if (cardGlowImages[i] != null)
            {
                Color c = CardGlowColor;
                c.a = Mathf.Lerp(0f, CardGlowColor.a, focus);
                cardGlowImages[i].color = c;
            }
        }
    }

    // ===== 解放(2026-10-08 仕様変更) =====
    // 未解放のキャラは一覧に出さない(鍵/シルエット/条件/総数も出さない)。解放済みのキャラだけをデータの順に左から詰めて並べる。
    // selectedIndex は「並んでいる位置」。order[位置] = CharacterDatabase.AllCharacters の番号。解放したばかりのキャラには NEW を付ける
    int visibleCount = -1;
    float contentFullWidth = -1f;
    float lockDenyFlash;
    int[] order = new int[0];
    Vector2[] slotHome;
    public int VisibleCount => visibleCount >= 0 ? visibleCount : CharacterDatabase.AllCharacters.Count;
    CharacterDefinition DefAt(int pos) { var all = CharacterDatabase.AllCharacters; int i = pos >= 0 && pos < order.Length ? order[pos] : pos; return all[Mathf.Clamp(i, 0, all.Count - 1)]; }
    RectTransform SlotAt(int pos) { int i = pos >= 0 && pos < order.Length ? order[pos] : -1; return i >= 0 && i < cardSlotRects.Length ? cardSlotRects[i] : null; }
    int PosOfSlot(int slot) { for (int k = 0; k < order.Length; k++) if (order[k] == slot) return k; return -1; }

    void ApplyUnlockState()
    {
        var all = CharacterDatabase.AllCharacters;
        if (slotHome == null || slotHome.Length != cardSlotRects.Length)
        {
            slotHome = new Vector2[cardSlotRects.Length];
            for (int i = 0; i < cardSlotRects.Length; i++) if (cardSlotRects[i] != null) slotHome[i] = cardSlotRects[i].anchoredPosition;
        }
        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < all.Count && i < cardSlotRects.Length; i++) if (UnlockRules.IsCharacterVisible(all[i].characterId)) list.Add(i);
        order = list.ToArray();
        visibleCount = order.Length;
        if (carouselContent != null)
        {
            if (contentFullWidth < 0f) contentFullWidth = carouselContent.sizeDelta.x;
            int hidden = Mathf.Min(all.Count, cardSlotRects.Length) - visibleCount;
            carouselContent.sizeDelta = new Vector2(contentFullWidth - hidden * cardStride, carouselContent.sizeDelta.y);
        }
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            var slot = cardSlotRects[i];
            if (slot == null) continue;
            int pos = PosOfSlot(i);
            bool show = pos >= 0;
            slot.gameObject.SetActive(show);
            if (i < cardGlowImages.Length && cardGlowImages[i] != null && !show) cardGlowImages[i].gameObject.SetActive(false);
            if (!show) continue;
            if (slotHome.Length > 0) slot.anchoredPosition = new Vector2(slotHome[0].x + pos * cardStride, slotHome[i].y);
            var cg = slot.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
            var oldLock = slot.Find("LockLabel"); if (oldLock != null) oldLock.gameObject.SetActive(false);
            bool isNew = UnlockRules.IsNewCharacter(all[i].characterId);
            NewBadge.Set(slot, isNew, new Vector2(0.5f, 1f), new Vector2(0f, -8f), 0.8f);
            if (isNew && markSeenOnApply) UnlockRules.MarkCharacterSeen(all[i].characterId);
        }
        if (selectedIndex >= visibleCount) selectedIndex = Mathf.Max(0, visibleCount - 1);
    }
    bool markSeenOnApply;

    // 2026-10-22: 選択中の金の縁(SelectionGlow)を、カードの絵が実際に描かれている範囲(縦横比を保って枠の中央)の外側 6px に合わせる。
    // 以前は枠(スロット)全体の外側だったので、絵より幅の広いスロットでは金の板が片側に大きくはみ出して見えていた
    bool glowsFitted;
    void FitGlows()
    {
        if (glowsFitted) return;
        glowsFitted = true;
        // 2026-10-28: 並びのカードの絵は シーンを作った時に焼き込まれる → 後から絵を差し替えたキャラ(ルカ/ペンタトゥットゥルー星人は
        //  仮の絵のまま)が古いまま出ていた。開いた時にキャラのデータの絵(portrait)と違えば差し替える(シーンを作り直さなくても最新の絵)
        var defsNow = CharacterDatabase.AllCharacters;
        for (int i = 0; i < cardSlotRects.Length && i < defsNow.Count; i++)
        {
            var pr = cardSlotRects[i] != null ? cardSlotRects[i].Find("Portrait") : null;
            var pim = pr != null ? pr.GetComponent<Image>() : null;
            var tex = defsNow[i] != null ? defsNow[i].portrait : null;
            if (pim != null && tex != null && (pim.sprite == null || pim.sprite.texture != tex)) pim.sprite = ToSprite(tex);
        }
        for (int i = 0; i < cardSlotRects.Length && i < cardGlowImages.Length; i++)
        {
            var slot = cardSlotRects[i]; var glow = cardGlowImages[i];
            if (slot == null || glow == null) continue;
            var p = slot.Find("Portrait") as RectTransform;
            var img = p != null ? p.GetComponent<Image>() : null;
            if (img == null || img.sprite == null) continue;
            Rect r = p.rect;
            float sa = img.sprite.rect.width / Mathf.Max(1f, img.sprite.rect.height), w = r.width, h = r.height;
            if (w / Mathf.Max(1f, h) > sa) w = h * sa; else h = w / sa;
            Vector2 c = (Vector2)p.localPosition + new Vector2((0.5f - p.pivot.x) * r.width, (0.5f - p.pivot.y) * r.height);
            var g = glow.rectTransform;
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
            g.sizeDelta = new Vector2(w + 12f, h + 12f);
            g.localPosition = c;
        }
    }

    // 確認用(QaSweep -qaCharSelect): 選んで、カルーセルもそのカードを中央へ(演出なし)
    public void DebugSelectAndCenter(int index)
    {
        SelectIndex(index);
        snapping = false;
        if (carouselContent != null) carouselContent.anchoredPosition = new Vector2(TargetContentXForIndex(index), carouselContent.anchoredPosition.y);
    }

    // 言語を切り替えた時(2026-10-07)
    void OnEnable() { Loc.Changed += OnLocChanged; }
    void OnDisable() { Loc.Changed -= OnLocChanged; }
    void OnLocChanged() { if (gameObject.activeInHierarchy) RefreshDetail(instant: true); }
}

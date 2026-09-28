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

        // カルーセル化(2026-09-24) - 発光の強弱はUpdateCardVisualsが中心
        // からの距離に応じて毎フレーム連続的に更新するため、ここでの
        // バイナリSetActiveは廃止した。

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
        // 左右の矢印(カードより先に判定 - 矢印はカード列の端に重なって置いてある)
        if (carouselArrowLeft != null && carouselArrowLeft.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(carouselArrowLeft, screenPos, null))
        {
            BeginSnap(Mathf.Max(0, selectedIndex - 1));
            return;
        }
        if (carouselArrowRight != null && carouselArrowRight.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(carouselArrowRight, screenPos, null))
        {
            BeginSnap(Mathf.Min(CharacterDatabase.AllCharacters.Count - 1, selectedIndex + 1));
            return;
        }
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] != null && RectTransformUtility.RectangleContainsScreenPoint(cardSlotRects[i], screenPos, null))
            {
                // カルーセル化(2026-09-24) - 中央にないカード(見切れて
                // いるカード)をタップした場合も、選択と同時にそのカードを
                // 中央付近へスナップさせる(マスター指示「タップで即座に
                // そのカードが選択・中央化される」)。
                BeginSnap(i);
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

        bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
        bool held = !down && !up && (Input.GetMouseButton(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled));
        Vector2 screenPos = Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;

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
        int count = CharacterDatabase.AllCharacters.Count;
        float x = carouselContent != null ? carouselContent.anchoredPosition.x : 0f;
        float minX = carouselContent != null ? Mathf.Min(0f, -(carouselContent.rect.width - carouselViewportWidth)) : 0f;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
        SetArrow(carouselArrowLeft, x < -1f, pulse);
        SetArrow(carouselArrowRight, x > minX + 1f, pulse);
        if (carouselPageText != null) carouselPageText.text = count > 0 ? $"{selectedIndex + 1} / {count}" : "";
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
    float ClampContentX(float x)
    {
        if (carouselContent == null) return 0f;
        float minX = Mathf.Min(0f, -(carouselContent.rect.width - carouselViewportWidth));
        return Mathf.Clamp(x, minX, 0f);
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
        float currentX = carouselContent.anchoredPosition.x;
        float minX = Mathf.Min(0f, -(carouselContent.rect.width - carouselViewportWidth));
        const float edgeEpsilon = 1f;
        if (currentX >= -edgeEpsilon) return 0;
        if (currentX <= minX + edgeEpsilon) return all.Count - 1;

        float viewportCenterX = carouselViewportWidth * 0.5f;
        int nearest = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < all.Count && i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] == null) continue;
            float cardCenterX = currentX + cardSlotRects[i].anchoredPosition.x + cardWidth * 0.5f;
            float dist = Mathf.Abs(cardCenterX - viewportCenterX);
            if (dist < bestDist) { bestDist = dist; nearest = i; }
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
}

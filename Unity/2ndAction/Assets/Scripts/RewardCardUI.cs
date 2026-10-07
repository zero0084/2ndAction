using System.Collections;
using System.Text.RegularExpressions;
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
    // カード選択UI再設計(2026-09-12第3弾) - 「カード本体には短い主要効果
    // だけを常時表示、詳しい説明は共通の詳細パネルへ」。levelText/count
    // Textと同じく"常にこのデータの有無だけで表示可否が決まる"(showDetails
    // に左右されない)独立した行 - RewardCardData.ValueLine(既存、横長行
    // UI用に追加されていたのを流用)をそのまま使う。
    public Text valueLineText;
    // Card UI / Rarity Frame pass - Rarity (top-left, ★ text) and Level
    // (top-right, "Lv.2 -> Lv.3" etc.) are now separate rows from Title/
    // Description (item 2 - "Card Nameを最も目立つ情報に、Rarity/Lvは補助
    // 情報として") rather than folded into Title's own string.
    public Text rarityText;
    public Text levelText;
    // カード裏面Lvバッジ修正(2026-09-17) - levelTextはバッジ内の文字だけで、
    // 菱形の背景(Diamond)+金縁(Border)を持つ親GameObject(SceneBuilderが
    // "LevelBadge"として生成)は別物。以前はlevelText.enabledしか切り替え
    // ていなかったため、裏面でテキストは消えても菱形バッジの枠自体が常時
    // 表示され続けていた(マスター指摘の再現バグ)。この親GOごとisFrontで
    // ON/OFFすることで、バッジ全体(枠含む)を表裏で確実に切り替える。
    public GameObject levelBadge;
    // カードVisual最終調整依頼(2026-09-18), item2/3 - 左上のCategory Icon。
    // LevelBadge(右上)と全く同じ構造(菱形+金縁の親GameObject)を左右対称
    // に配置したもの - 表裏の切り替えもlevelBadgeと同じ扱いにする。
    // categoryIconImageはBorderの内側に重ねるアイコン本体(CardCategoryIcons
    // 参照、未生成のカテゴリはnullのままスキップされる)。
    public GameObject categoryBadge;
    public Image categoryIconImage;
    // Card UI改修(2026-09-08) - 所持枚数「×N」専用表示(RewardCardData.
    // Countが1以下、またはこのカード自体が「所持枚数」の概念を持たない
    // 呼び出し元(Reward/LevelUp選択・Fusionスロット等、Count未設定=0の
    // まま)では非表示)。タイトル帯の右端に固定配置。
    public Text countText;
    // Item 5 - a small "EQUIPPED" tag, toggled on/off rather than built per
    // call - RewardCardData.ShowEquippedBadge is false everywhere except
    // where a caller actually knows equip state.
    // カードVisual最終調整依頼(2026-09-18), item1 - このリボン1本を
    // EQUIPPED/NEWの二役で共用する(同時にはほぼ起こらない状態のため、
    // 新しいUI要素を増やさずに済ませた - EQUIPPEDの方が情報として優先度が
    // 高いのでRewardCardData側で二重にtrueにはしない設計)。表示する文字
    // 列自体はequippedBadgeLabelへSetContentで書き込む。
    public GameObject equippedBadge;
    public Text equippedBadgeLabel;
    public Button button;

    // カードUI最終デザイン改修(2026-09-26) - Name Plate/エンブレム/選択状態の部品
    // (SceneBuilder.CreateRewardCardが生成)。古いシーンで作られたカード(これらがnull)でも
    // 動くよう、全てnullチェックして使う。
    public Image selectGlow;
    public GameObject namePlate;
    public Image namePlateRim;
    public Image namePlateAccent;
    public GameObject countChip;
    public GameObject inDeckMark;
    public Image categoryEmblemRim;
    public Image categoryEmblemAccent;
    public Image levelEmblemRim;
    public Image levelEmblemAccent;
    // 実行時にCardFaceArtのスプライトを割り当てるImage(Editorで作ったSpriteはシーンに残らないため)
    public Image[] roundedImages = new Image[0];
    public Image[] diamondImages = new Image[0];
    public Image[] circleImages = new Image[0];
    // Card Name 1行時の最大フォントサイズ(0 = 旧カード、Best Fit任せ)
    public int titleMaxFontSize;

    RewardCardData data;
    public RewardCardData Data => data;

    // Card UI改修(2026-09-08) - カード表面に常時表示するのはフレーム/イラ
    // スト/タイトル/Lvの4つだけにする新方針(効果文/レア度★は詳細画面での
    // み)。CardFusionUI(合成素材選択)はまだ専用の詳細パネルを持たないため
    // 引き続きshowDetails:trueを渡して従来どおり効果文/★を表示させる
    // (情報が全く見えなくなる退行を避けるための意図的な経過措置 - 詳細
    // パネルが用意され次第simpleに揃えられる)。DeckEditUI(COLLECTION/
    // DECK/CHARACTER CARDS)は既存の中央詳細カラムが、RewardCardSequence
    // (Level Up/Boss Reward選択、カード選択UI再設計2026-09-12第3弾)は
    // 自前のdetail*系フィールドによる専用パネルがそれぞれ既にこのuseに
    // 対応しているため、どちらもデフォルト(false)のシンプル表示を渡す。
    // 【注意】RarityText(y=0.715-0.785)はCategoryBadge追加時にこの
    // showDetails:trueのケース(CardFusionUI)と衝突しないよう調整済み -
    // RewardCardSequence側を将来showDetails:trueへ戻す場合は、valueLine
    // Text(y=0.205-0.30)とdescriptionText(y=0.24-0.385)が重なる範囲を
    // 再確認すること(現状はどちらもfalseなので問題化していないだけ)。
    bool showDetails;

    // 品質改善 Bug #002(2026-09-09), item 1/2 - 「Card裏面表示時にLv表示
    // が貫通する」の根本原因。以前のSetContent()はrarityText/levelText/
    // countText/equippedBadgeの表示状態(.enabled/.SetActive)を、現在
    // カードが表か裏かに関係なく「データの内容(Lvがあるか等)」だけで直接
    // 決めていた - RewardCardSequence等の実際の呼び出し順は
    // 「ShowBack()(裏面表示開始)→...→SetContent()(次に見せるカードの
    // データを先読みで設定)→FlipToFront()(実際にめくる)」であり、
    // SetContent()が呼ばれた瞬間、まだScaleX=1・backImage表示中の"裏面"
    // の上にLevelBadge等が(描画順で最前面にあるため)いきなり出現してし
    // まっていた。マスター提案の"SetFaceState"方式を採用し、"見せてよい
    // かどうか(isFront)"と"表示すべき中身(データ)"を分離 - ApplyFace
    // Visibility()が両方を突き合わせて最終的な.enabledを決める唯一の
    // 場所にした。ShowBack/ShowFrontImmediate/FlipToFrontの3箇所は全て
    // isFrontを切り替えてこれを呼ぶだけになり、個別にフィールドを列挙
    // する重複コードも解消した。
    bool isFront;

    public void ShowBack()
    {
        isFront = false;
        backImage.enabled = true;
        ApplyFaceVisibility();
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
    }

    // isFrontと現在のdata/showDetailsを突き合わせて、表面専用UI全ての
    // 表示状態を一括で確定させる。裏面(isFront=false)の間はここで必ず
    // 全て非表示になる - データ側(SetContent)が何を持っていても、表に
    // なるまでは絶対に見えない。
    void ApplyFaceVisibility()
    {
        EnsureArt();
        if (baseImage != null) baseImage.enabled = isFront;
        if (namePlate != null) namePlate.SetActive(isFront);
        if (inDeckMark != null) inDeckMark.SetActive(isFront && inDeckMarked);
        if (titleBandImage != null) titleBandImage.enabled = isFront;
        frameImage.enabled = isFront;
        iconBackdrop.enabled = isFront;
        iconImage.enabled = isFront;
        titleText.enabled = isFront;
        textBackdrop.enabled = isFront && showDetails;
        descriptionText.enabled = isFront && showDetails;
        if (rarityText != null) rarityText.enabled = isFront && showDetails;
        bool showLevel = isFront && !string.IsNullOrEmpty(data.LevelLine) && !data.FinalEvolution; // FINAL EVOLUTION は Lv を出さない(表記は上の帯)
        if (levelText != null) levelText.enabled = showLevel;
        if (levelBadge != null) levelBadge.SetActive(showLevel);
        // item2/3/6 - Category Iconはlevelと同じ扱い(表面のみ)。スプライト
        // 自体が無い(未生成カテゴリ)場合は空の菱形だけが残らないよう隠す。
        bool showCategory = isFront && categoryIconImage != null && categoryIconImage.sprite != null;
        if (categoryBadge != null) categoryBadge.SetActive(showCategory);
        if (valueLineText != null) valueLineText.enabled = isFront && !string.IsNullOrEmpty(data.ValueLine);
        if (countChip != null) countChip.SetActive(isFront && data.Count > 1);
        else if (countText != null) countText.enabled = isFront && data.Count > 1;
        if (equippedBadge != null) equippedBadge.SetActive(isFront && (data.ShowEquippedBadge || data.ShowNewBadge));
        ApplyMasteryVisual(isFront);
        ApplyFocusVisual();
    }

    // ===== カード長期育成(2026-10-04): Mastery の★ / AWAKENED の光・表記・粒 =====
    // SceneBuilder で作ったカードの部品は変えず、必要になった時にこのカードの子として作る(全画面のカードで同じ見た目)。
    // 一覧では「Lv9 / ★の数 / AWAKENED の印」だけ(進みの数字は詳細の画面で出す)。
    GameObject masteryRow, awakenRoot;
    Text masteryText, awakenLabel;
    Image awakenGlow;
    readonly Image[] awakenSparks = new Image[4];
    static readonly Color AwakenGold = new Color(1f, 0.82f, 0.32f);

    void EnsureMasteryArt()
    {
        if (masteryRow != null || rect == null) return;
        Font font = titleText != null ? titleText.font : null;
        // 光(カードの後ろ、少し外へはみ出す)
        awakenRoot = new GameObject("Awakened", typeof(RectTransform));
        var ar = (RectTransform)awakenRoot.transform;
        ar.SetParent(rect, false); ar.SetAsFirstSibling();
        ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one; ar.offsetMin = ar.offsetMax = Vector2.zero;
        awakenGlow = NewChildImage("Glow", ar, new Vector2(-0.24f, -0.16f), new Vector2(1.24f, 1.16f), CardFaceArt.SoftGlow(), new Color(AwakenGold.r, AwakenGold.g, AwakenGold.b, 0.85f));
        for (int i = 0; i < awakenSparks.Length; i++) awakenSparks[i] = NewChildImage("Spark" + i, ar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), SparkDot(), new Color(1f, 0.95f, 0.7f, 0.95f));
        // ★の帯(カードの下端の内側)
        masteryRow = new GameObject("MasteryRow", typeof(RectTransform));
        var mr = (RectTransform)masteryRow.transform;
        mr.SetParent(rect, false); mr.SetAsLastSibling();
        mr.anchorMin = new Vector2(0.06f, 0.008f); mr.anchorMax = new Vector2(0.94f, 0.118f); mr.offsetMin = mr.offsetMax = Vector2.zero;
        NewChildImage("Back", mr, Vector2.zero, Vector2.one, CardFaceArt.RoundedRect(), new Color(0.02f, 0.015f, 0.05f, 0.94f));
        masteryText = NewChildText("Stars", mr, Vector2.zero, Vector2.one, font, AwakenGold);
        // AWAKENED の表記(上端の中央)
        var lr = new GameObject("AwakenedLabel", typeof(RectTransform));
        var lrt = (RectTransform)lr.transform;
        lrt.SetParent(ar, false);
        lrt.anchorMin = new Vector2(0.27f, 0.928f); lrt.anchorMax = new Vector2(0.73f, 0.988f); lrt.offsetMin = lrt.offsetMax = Vector2.zero; // 上端の中央(左右の菱形の間。名前を隠さない)
        NewChildImage("Back", lrt, Vector2.zero, Vector2.one, CardFaceArt.RoundedRect(), new Color(0.35f, 0.22f, 0.02f, 0.9f));
        awakenLabel = NewChildText("Text", lrt, Vector2.zero, Vector2.one, font, new Color(1f, 0.97f, 0.82f));
        awakenLabel.text = Loc.Auto("AWAKENED");
        lr.transform.SetParent(rect, false); // 表記はカードの手前に出す(光だけ後ろ)
        lr.transform.SetAsLastSibling();
        awakenLabelRoot = lr;
    }
    GameObject awakenLabelRoot;

    // AWAKENED の粒(中心が明るく外へ消える丸)。1回だけ作る
    static Sprite sparkDot;
    static Sprite SparkDot()
    {
        if (sparkDot != null) return sparkDot;
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                px[y * n + x] = new Color(1f, 1f, 1f, d * d);
            }
        tex.SetPixels(px); tex.Apply();
        sparkDot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return sparkDot;
    }

    static Image NewChildImage(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = r.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return img;
    }

    static Text NewChildText(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, Font font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = new Vector2(2f, 1f); r.offsetMax = new Vector2(-2f, -1f);
        var t = go.GetComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.alignment = TextAnchor.MiddleCenter; t.color = color; t.raycastTarget = false;
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 6; t.resizeTextMaxSize = 40; t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    void ApplyMasteryVisual(bool front)
    {
        bool show = front && data.ShowMastery && !data.FinalEvolution;
        bool fe = front && data.FinalEvolution;
        bool awake = front && (data.Awakened || fe);
        if (!show && !awake && masteryRow == null) return;
        EnsureMasteryArt();
        if (masteryRow == null) return;
        masteryRow.SetActive(show);
        if (show) masteryText.text = Loc.Auto(CardMastery.StarsRich(data.MasteryStars)); // 埋まった★は金、残りは暗い★
        awakenRoot.SetActive(awake);
        awakenLabelRoot.SetActive(awake);
        if (awakenLabel != null) awakenLabel.text = Loc.Auto(fe ? (data.Awakened ? "FINAL EVOLUTION ★" : "FINAL EVOLUTION") : "AWAKENED");
        if (awakenGlow != null) { var gc = fe ? new Color(1f, 0.6f, 0.15f, 0.9f) : new Color(AwakenGold.r, AwakenGold.g, AwakenGold.b, 0.85f); awakenGlow.color = gc; }
    }

    // AWAKENED の光の呼吸と、カードのまわりを回る小さな粒
    void UpdateAwakened()
    {
        if (awakenRoot == null || !awakenRoot.activeSelf) return;
        float t = Time.unscaledTime;
        var c = awakenGlow.color; c.a = 0.7f + 0.25f * Mathf.Sin(t * 2.4f); awakenGlow.color = c;
        Vector2 size = rect.rect.size;
        for (int i = 0; i < awakenSparks.Length; i++)
        {
            float a = t * 0.9f + i * Mathf.PI * 0.5f;
            var sr = awakenSparks[i].rectTransform;
            sr.anchoredPosition = new Vector2(Mathf.Cos(a) * size.x * 0.56f, Mathf.Sin(a) * size.y * 0.53f);
            float s = Mathf.Max(8f, size.x * (0.11f + 0.04f * Mathf.Sin(t * 5f + i)));
            sr.sizeDelta = new Vector2(s, s);
        }
    }

    // An unfilled DECK slot - shows just the card frame art, dimmed, with
    // no icon/text/back image, so the slot still reads as "a slot exists
    // here" (per the reference mockup's numbered empty-slot placeholders)
    // instead of the grid just showing nothing where the slot would be.
    public void ShowEmpty()
    {
        isFront = false; // frameだけ個別にtrueへ - ApplyFaceVisibilityは使わず、このメソッド内で明示的に全て指定する
        isEmpty = true;
        EnsureArt();
        backImage.enabled = false;
        if (baseImage != null) baseImage.enabled = false;
        if (titleBandImage != null) titleBandImage.enabled = false;
        frameImage.enabled = true;
        SetFrameSprite(defaultFrameSprite, false, 0f, 1f);
        frameImage.color = new Color(1f, 1f, 1f, 0.32f);
        if (namePlate != null) namePlate.SetActive(false);
        if (countChip != null) countChip.SetActive(false);
        if (inDeckMark != null) inDeckMark.SetActive(false);
        focused = false;
        inDeckMarked = false;
        if (selectGlow != null) selectGlow.gameObject.SetActive(false);
        iconBackdrop.enabled = false;
        textBackdrop.enabled = false;
        iconImage.enabled = false;
        titleText.enabled = false;
        descriptionText.enabled = false;
        if (rarityText != null) rarityText.enabled = false;
        if (levelText != null) levelText.enabled = false;
        if (levelBadge != null) levelBadge.SetActive(false);
        if (categoryBadge != null) categoryBadge.SetActive(false);
        if (valueLineText != null) valueLineText.enabled = false;
        if (countText != null) countText.enabled = false;
        if (equippedBadge != null) equippedBadge.SetActive(false);
        if (masteryRow != null) { masteryRow.SetActive(false); awakenRoot.SetActive(false); awakenLabelRoot.SetActive(false); }
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
    }

    // showDetails=false (new default) - "常時表示は フレーム/イラスト/
    // タイトル/Lvのみ" (効果文・★は非表示、詳細画面用)。showDetails=true
    // で従来どおり効果文/★も表示する(呼び出し元のコメント参照)。
    public void SetContent(RewardCardData cardData, bool showDetails = false)
    {
        EnsureArt();
        if (isEmpty)
        {
            // 空きスロット表示から実カードへ戻る時は、枠の暗さを通常の色へ戻す
            isEmpty = false;
            frameImage.color = focused ? FrameFocusedColor : FrameNormalColor;
        }
        data = cardData;
        this.showDetails = showDetails;
        if (iconImage.sprite != null) Destroy(iconImage.sprite);
        iconImage.sprite = cardData.Icon != null
            ? Sprite.Create(cardData.Icon, new Rect(0f, 0f, cardData.Icon.width, cardData.Icon.height), new Vector2(0.5f, 0.5f))
            : null;
        titleText.text = Loc.Auto(cardData.Title);
        FitTitle();
        descriptionText.text = Loc.Auto(cardData.Description);
        if (valueLineText != null) valueLineText.text = Loc.Auto(cardData.ValueLine);

        // Card UI / Rarity Frame pass - frame Sprite switches with Rarity;
        // frameImage.color stays whatever SetSelected/FlashFrame/idle pulse
        // last left it (this method never touches color) since those are
        // independent of which frame art is showing.
        // カードUI最終デザイン改修(2026-09-26) - レア度フレームはカード全面を覆うSliced表示
        // (全レア度でName Plate/エンブレムの位置が揃う - CardRarityFrames.GetSlicedFrame参照)。
        Sprite slicedFrame = CardRarityFrames.GetSlicedFrame(cardData.Rarity, out float cropWidthPx, out float frameVScale);
        if (slicedFrame != null) SetFrameSprite(slicedFrame, true, cropWidthPx, frameVScale);
        else SetFrameSprite(CardRarityFrames.GetFrame(cardData.Rarity, defaultFrameSprite), false, 0f, 1f);
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
        // 品質改善 Bug #002(2026-09-09) - ここではテキストの"中身"だけを
        // 設定し、実際に見せるかどうか(.enabled)はApplyFaceVisibility()
        // に一本化した(このメソッドの先頭コメント参照) - SetContent()は
        // 「次に表示する内容を仕込む」だけで、現在裏面表示中のカードに
        // 対して呼ばれても何も見た目を変えない。
        if (rarityText != null)
        {
            rarityText.text = Loc.Auto(new string('★', Mathf.Clamp(cardData.Rarity <= 0 ? 1 : cardData.Rarity, 1, 5)));
        }
        if (levelText != null)
        {
            // カードUI最終デザイン改修(2026-09-26) - 右上の菱形 = Levelというルールが左上の
            // Categoryと対になって伝わるため、小カードでは「Lv.」を省いて数字だけを大きく出す
            // (実機の小さいカードでは「Lv.3」の方が文字が小さくなり読みにくかった)。
            // 「Lv.2 -> Lv.3」(Level Up候補)は選んだ後のLv、「Lv.9 MAX」は数字を明るい色に。
            // 詳細な表記は各画面の詳細パネルがLevelLineをそのまま出す。
            string line = cardData.LevelLine ?? "";
            MatchCollection lv = LevelNumberPattern.Matches(line);
            bool isMax = line.Contains("MAX");
            levelText.text = Loc.Auto(lv.Count > 0 ? lv[lv.Count - 1].Groups[1].Value : line);
            levelText.color = isMax ? LevelMaxColor : LevelColor;
        }
        if (countText != null)
        {
            countText.text = Loc.Auto(cardData.Count > 1 ? $"×{cardData.Count}" : "");
        }
        // item2/3 - Category Icon本体。スプライトが無い(未生成カテゴリ)場合
        // はnullのままにしておき、ApplyFaceVisibility側がそれを見てバッジ
        // ごと隠す。
        if (categoryIconImage != null)
        {
            categoryIconImage.sprite = CardCategoryIcons.GetIcon(cardData.Category);
        }
        // item1 - EQUIPPED/NEWの二役リボン。EQUIPPEDが優先(RewardCardData
        // 側で同時にtrueにしない設計だが、念のためここでも同じ優先順位を
        // 踏襲する)。
        if (equippedBadgeLabel != null)
        {
            equippedBadgeLabel.text = Loc.Auto(cardData.ShowEquippedBadge ? "EQUIPPED" : cardData.ShowNewBadge ? "NEW" : "");
            // NEWはエメラルド、EQUIPPEDは金(同じ部品で役割の違いを色で分ける)
            equippedBadgeLabel.color = cardData.ShowEquippedBadge ? new Color(1f, 0.9f, 0.62f) : new Color(0.62f, 1f, 0.86f);
        }
        ApplyFaceVisibility();
    }

    // Static (no animation) face-up display for grid-style UI like the Deck
    // Edit screen, which reuses this same component instead of building its
    // own card visuals from scratch. showDetails - see the class-level
    // field comment; defaults to the new simplified face.
    public void ShowFrontImmediate(RewardCardData cardData, bool showDetails = false)
    {
        isFront = true;
        backImage.enabled = false;
        SetContent(cardData, showDetails); // ApplyFaceVisibility()を内部で呼ぶ(isFront=true済みなので正しく全て表示される)
        rect.localScale = Vector3.one * FocusScale;
        canvasGroup.alpha = 1f;
        SetInteractable(true);
    }

    public void SetInteractable(bool value)
    {
        button.interactable = value;
        canvasGroup.blocksRaycasts = value;
    }

    // カードUI最終デザイン改修(2026-09-26) - 通常状態はフレームを少しだけ落ち着かせる
    // (大量に並べてもギラギラしない)。選択中はFrameFocusedColor(=元の明るさ)へ戻して発光させる。
    static readonly Color FrameNormalColor = new Color(0.86f, 0.84f, 0.80f, 1f);
    static readonly Color FrameFocusedColor = Color.white;
    // Visual Style Ver.1's gold accent, matching the navy+gold+white
    // treatment used everywhere else (HUD, buttons, panels) - was a green
    // tint before, which didn't match anything else in the game.
    static readonly Color FrameSelectedColor = new Color(1f, 0.85f, 0.4f, 1f);
    const float SelectedScale = 1.06f;

    // カードUI最終デザイン改修(2026-09-26) - 「選択状態(いま見ているカード)」を明確にする。
    // 通常: 落ち着いた金+エメラルド / 選択: 金縁が明るく、エメラルドが発光、背後に淡いGlow、
    // 少し拡大。点滅はせず、Glowがごくゆっくり呼吸するだけ。
    // (旧仕様ではDeckEditUIが「デッキに入っている」印にもこれを使っていたが、それは
    // SetInDeckMarkの小さなチェックに分けた。合成画面のメイン/素材の選択はこちらのまま。)
    public void SetSelected(bool selected) => SetFocused(selected);

    bool focused;
    bool inDeckMarked;
    bool isEmpty;
    public bool IsFocused => focused;
    public float FocusScale => focused ? SelectedScale : 1f;

    public void SetFocused(bool value)
    {
        focused = value;
        rect.localScale = Vector3.one * FocusScale;
        if (isEmpty)
        {
            // 空きスロット(キャラカードの装備待ち等): 暗い枠のまま、選択時だけ金色に
            frameImage.color = value ? FrameSelectedColor : new Color(1f, 1f, 1f, 0.32f);
            if (selectGlow != null) selectGlow.gameObject.SetActive(value);
            return;
        }
        frameImage.color = value ? FrameFocusedColor : FrameNormalColor;
        ApplyFocusVisual();
    }

    // デッキに入っているカードの印(Collection一覧用、選択の発光とは別の静かな表示)。
    public void SetInDeckMark(bool value)
    {
        inDeckMarked = value;
        if (inDeckMark != null) inDeckMark.SetActive(isFront && value);
    }

    static readonly Color RimNormal = new Color(0.74f, 0.60f, 0.32f, 1f);
    static readonly Color RimFocused = new Color(1f, 0.86f, 0.48f, 1f);
    static readonly Color EmblemAccentNormal = new Color(0.30f, 0.88f, 0.78f, 0.45f);
    static readonly Color PlateAccentNormal = new Color(0.30f, 0.88f, 0.78f, 0.35f);
    static readonly Color AccentFocused = new Color(0.45f, 1f, 0.88f, 1f);
    static readonly Color LevelColor = new Color(1f, 0.92f, 0.68f);
    static readonly Color LevelMaxColor = new Color(0.72f, 1f, 0.92f);
    static readonly Regex LevelNumberPattern = new Regex(@"Lv\.(\d+)");
    const float GlowAlpha = 0.8f;

    void ApplyFocusVisual()
    {
        bool on = focused && isFront;
        if (selectGlow != null) selectGlow.gameObject.SetActive(on);
        Color rim = on ? RimFocused : RimNormal;
        if (namePlateRim != null) namePlateRim.color = rim;
        if (categoryEmblemRim != null) categoryEmblemRim.color = rim;
        if (levelEmblemRim != null) levelEmblemRim.color = rim;
        if (namePlateAccent != null) namePlateAccent.color = on ? AccentFocused : PlateAccentNormal;
        if (categoryEmblemAccent != null) categoryEmblemAccent.color = on ? AccentFocused : EmblemAccentNormal;
        if (levelEmblemAccent != null) levelEmblemAccent.color = on ? AccentFocused : EmblemAccentNormal;
    }

    void Update()
    {
        UpdateAwakened(); // カード長期育成: AWAKENED の光と粒
        // 選択中のGlowだけ、ごくゆっくり呼吸させる(点滅はさせない)
        if (!focused || selectGlow == null || !selectGlow.gameObject.activeSelf) return;
        Color c = selectGlow.color;
        c.a = GlowAlpha * (0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 2.2f));
        selectGlow.color = c;
    }

    bool artReady;

    // CardFaceArtの手続き生成スプライトを割り当てる(1回だけ)。
    void EnsureArt()
    {
        if (artReady) return;
        artReady = true;
        foreach (var img in roundedImages) if (img != null) img.sprite = CardFaceArt.RoundedRect();
        foreach (var img in diamondImages) if (img != null) img.sprite = CardFaceArt.Diamond();
        foreach (var img in circleImages) if (img != null) img.sprite = CardFaceArt.Circle();
        if (selectGlow != null) selectGlow.sprite = CardFaceArt.SoftGlow();
    }

    // sliced=true: カード全面を覆う9-slice表示(横はカード幅ぴったり、縦はvScale倍+レール区間で吸収)。
    void SetFrameSprite(Sprite sprite, bool sliced, float cropWidthPx, float vScale)
    {
        frameImage.sprite = sprite;
        if (sliced && cropWidthPx > 0f && sprite != null)
        {
            frameImage.type = Image.Type.Sliced;
            frameImage.preserveAspect = false;
            frameImage.fillCenter = true;
            float w = Mathf.Max(1f, frameImage.rectTransform.rect.width);
            Canvas c = frameImage.canvas;
            float refPpu = c != null ? c.referencePixelsPerUnit : 100f;
            // 上下の装飾部(ボーダー)の表示倍率 = カード幅/切り出し幅 × vScale
            frameImage.pixelsPerUnitMultiplier = cropWidthPx / (w * Mathf.Max(0.01f, vScale)) * (refPpu / sprite.pixelsPerUnit);
        }
        else
        {
            frameImage.type = Image.Type.Simple;
            frameImage.preserveAspect = true;
            frameImage.pixelsPerUnitMultiplier = 1f;
        }
    }

    // Card NameをName Plateに収める: まず1行のままフォントを一定範囲(最大の72%まで)縮小し、
    // それでも入らない時だけ2行にする(Legacy TextのBest Fitは縮める前に2行へ折り返してしまう)。
    // 全カードでプレートの位置/高さは同じ - 文字サイズと行数だけが変わる。
    void FitTitle()
    {
        if (titleText == null || titleMaxFontSize <= 0) return;
        string s = titleText.text ?? "";
        Rect r = titleText.rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;
        titleText.resizeTextForBestFit = false;
        titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        titleText.verticalOverflow = VerticalWrapMode.Truncate;
        TextGenerator gen = titleText.cachedTextGeneratorForLayout;
        float ppu = Mathf.Max(0.01f, titleText.pixelsPerUnit);
        int max = titleMaxFontSize;
        int minSingle = Mathf.Max(6, Mathf.RoundToInt(max * 0.72f));
        int minTwo = Mathf.Max(6, Mathf.RoundToInt(max * 0.6f));

        titleText.lineSpacing = 1f;
        for (int size = max; size >= minSingle; size--)
        {
            TextGenerationSettings st = titleText.GetGenerationSettings(Vector2.zero);
            st.fontSize = size;
            st.resizeTextForBestFit = false;
            if (gen.GetPreferredWidth(s, st) / ppu <= r.width)
            {
                titleText.fontSize = size;
                return;
            }
        }

        const float twoLineSpacing = 0.9f;
        titleText.lineSpacing = twoLineSpacing;
        for (int size = minSingle; size >= minTwo; size--)
        {
            TextGenerationSettings st = titleText.GetGenerationSettings(new Vector2(r.width, 0f));
            st.fontSize = size;
            st.resizeTextForBestFit = false;
            st.horizontalOverflow = HorizontalWrapMode.Wrap;
            st.lineSpacing = twoLineSpacing;
            if (gen.GetPreferredHeight(s, st) / ppu <= r.height)
            {
                titleText.fontSize = size;
                return;
            }
        }
        titleText.fontSize = minTwo;
    }

    // カード選択UI再設計(2026-09-12第3弾) - Level Up/Boss Reward選択で
    // 「今どのカードを選んでいるか」を示す青白系の発光(SetSelectedの金色
    // はDeck画面の「所持デッキに入っている」印と意味が違うため、あえて
    // 別の色・別メソッドにした)。Scale/位置/フェードはRewardCardSequence
    // 側が既存のScaleTo/MoveTo/FadeToで個別に制御するため、ここでは色だけ
    // を切り替える。
    static readonly Color FrameChoiceGlowColor = new Color(0.55f, 0.85f, 1f, 1f);

    public void SetChoiceGlow(bool on)
    {
        frameImage.color = on ? FrameChoiceGlowColor : FrameNormalColor;
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

        // 品質改善 Bug #002(2026-09-09), item 2 - Sprite切替(Back非表示/
        // Front表示)とScaleX(=0の瞬間、カードが真横向きで見えない)を同じ
        // タイミングに揃える。ScaleXTo(0,...)が完了した"中央"のこの1点で
        // isFront=trueへ切り替え、ApplyFaceVisibility()で表側UI一式を
        // まとめて表示する - 個別列挙の重複を解消しつつ、ShowBack()と
        // 完全に同じロジックで裏⇔表を切り替えるため、貫通のリスクがない。
        backImage.enabled = false;
        isFront = true;
        ApplyFaceVisibility();

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
    public IEnumerator PulseSelect(float settleScale = -1f)
    {
        yield return ScaleTo(1.18f, 0.09f);
        // 既定(-1): 押した後の選択状態に合わせた大きさへ戻る(選択中なら少し大きいまま)
        yield return ScaleTo(settleScale < 0f ? FocusScale : settleScale, 0.14f);
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
        focused = false;
        inDeckMarked = false;
        if (selectGlow != null) selectGlow.gameObject.SetActive(false);
        rect.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        SetInteractable(false);
        gameObject.SetActive(false);
    }
}

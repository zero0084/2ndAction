using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// カード合成画面(2026-09-26 全面改修)。
//  左 約60%: 所持カード一覧(常に2段以上が完全に見える大きさ、縦スクロール、カテゴリ絞り込み、
//            並び替え、各カードに所持枚数/使用可能枚数/能力数)。同じ性能のカードは1枠にまとめ、
//            性能(Lv・レア度・能力構成・強化量)が違えば別枠。
//  右 約40%: メイン枠/素材枠(タップで選択先を切替、選択中の枠を強調、×で解除)、詳細欄(スクロール)、
//            合成前の説明、固定の合成ボタン。
//  上部: 戻る/「カード合成」/所持MILE。
// 合成の確定はCardFusionLogic.Executeで1回だけ行い、演出とリザルトはその確定結果を見せるだけ。
// 画面部品はSceneBuilderが作ったカードのひな形(cardTemplate)とフレーム画像を使って、初回Open時に
// ここで組み立てる(画面比率に合わせてアンカーで配置、一覧のカードの大きさも毎回計算し直す)。
// 入力はDeckEditUIと同じ「自前でタップ位置を照合する」方式。
public class CardFusionUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public CanvasGroup rootGroup;
    public float fadeDuration = 0.16f;

    [Header("Parts (SceneBuilder)")]
    public RewardCardUI cardTemplate;      // 180x270のカード(非表示のひな形)
    public Sprite panelFrameSprite;        // 濃紺と金の飾り枠(Sliced)
    public Sprite magicCircleSprite;
    public Font uiFont;

    // ===== 色 ===== //
    static readonly Color Gold = new Color(1f, 0.84f, 0.42f);
    static readonly Color PanelFill = new Color(0.03f, 0.035f, 0.07f, 0.92f);
    static readonly Color TextMain = new Color(0.93f, 0.94f, 1f);
    static readonly Color TextDim = new Color(0.7f, 0.74f, 0.85f);
    static readonly Color ErrorRed = new Color(1f, 0.55f, 0.5f);

    // ===== 選択状態 ===== //
    string mainKey, materialKey;
    int activeSlot; // 0=メイン 1=素材
    enum Phase { Select, Presenting, Result }
    Phase phase = Phase.Select;
    // 絞り込み・並び順・スクロール位置は合成後も(画面を閉じても)維持する。
    static int filterIndex;
    static int sortMode;          // 0名前 1レベル 2レア度 3所持枚数
    static bool sortDescending = true;
    static float savedGridScroll = 1f;

    static readonly string[] FilterLabels = { "すべて", "移動", "攻撃", "防御", "成長", "回復", "特殊", "リスク" };
    static readonly string[] SortLabels = { "名前", "レベル", "レア度", "所持枚数" };

    // ===== 組み立てた部品 ===== //
    bool built;
    RectTransform leftPanel, rightPanel;
    Text mileText, listCountText, statusText, detailText, fuseLabel;
    Image fuseFill;
    RectTransform fuseButton, backButton;
    readonly List<(RectTransform rect, Text label, Image fill)> filterButtons = new List<(RectTransform, Text, Image)>();
    readonly List<(RectTransform rect, Text label, Image fill)> sortButtons = new List<(RectTransform, Text, Image)>();
    ScrollRect gridScroll, detailScroll, resultScroll;
    RectTransform gridContent;
    class Cell { public RectTransform root; public RectTransform scaler; public RewardCardUI card; public Text info; public Text tag; public Image tagBg; public string key; }
    readonly List<Cell> cells = new List<Cell>();
    readonly List<CardInventory.Stack> shown = new List<CardInventory.Stack>();
    class Slot { public RectTransform frame; public Image glow; public RewardCardUI card; public Text label; public RectTransform clearButton; public Text activeBadge; public Image activeBadgeBg; }
    readonly Slot[] slots = new Slot[2];

    // 演出/リザルト
    RectTransform overlay; CanvasGroup overlayGroup;
    Image dim, circle, circle2, flash, ring, ring2, burst;
    RewardCardUI fxMain, fxMaterial, fxResult, resultCard;
    Text fxMainLabel, fxMaterialLabel, fxCaption, fxMile;
    RectTransform skipButton;
    RectTransform resultPanel; Text resultTitle, resultBody; RectTransform continueButton, backToListButton;
    Sprite orbSprite, burstSprite, ringSprite;
    bool skipRequested;

    class Tap { public RectTransform rect; public System.Action action; public bool overlay; }
    readonly List<Tap> taps = new List<Tap>();

    public bool IsPresenting => phase == Phase.Presenting;
    public bool IsShowingResult => phase == Phase.Result;
    public string MainKey => mainKey;
    public string MaterialKey => materialKey;

    // ===================================================================== //
    // 開閉
    // ===================================================================== //

    public void Open()
    {
        if (root != null) root.SetActive(true);
        EnsureBuilt();
        mainKey = materialKey = null;
        activeSlot = 0;
        phase = Phase.Select;
        HideOverlay();
        SetStatus("", false);
        Refresh(restoreScroll: true);
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            StartCoroutine(FadeGroupTo(1f));
        }
    }

    // Androidの戻る(2026-10-01): 結果表示中は一覧へ戻る、演出中は何もしない、選択中は画面を閉じる
    public void HandleBack()
    {
        if (phase == Phase.Result) { OnBackToList(); return; }
        Close();
    }

    public void Close()
    {
        if (phase == Phase.Presenting) return;
        if (gridScroll != null) savedGridScroll = gridScroll.verticalNormalizedPosition;
        if (ScreenTransitionManager.Instance != null && !ScreenTransitionManager.Instance.IsTransitioning)
        {
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                if (root != null) root.SetActive(false);
                if (rootGroup != null) rootGroup.alpha = 1f;
                if (GameManager.Instance != null) GameManager.Instance.CloseCardFusion();
            }, ScreenTransitionManager.Style.Fade);
            return;
        }
        if (root != null) root.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.CloseCardFusion();
    }

    IEnumerator FadeGroupTo(float target)
    {
        float start = rootGroup.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeDuration);
            rootGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // ===================================================================== //
    // 画面の組み立て
    // ===================================================================== //

    RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
        return r;
    }

    RectTransform Fixed(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var r = NewRect(name, parent, anchor, anchor, Vector2.zero, Vector2.zero);
        r.pivot = pivot; r.sizeDelta = size; r.anchoredPosition = pos;
        return r;
    }

    Image AddImage(RectTransform r, Color c, Sprite s = null, bool sliced = false)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.color = c; img.sprite = s; img.raycastTarget = false;
        if (sliced && s != null) img.type = Image.Type.Sliced;
        return img;
    }

    Text AddText(RectTransform r, int size, Color c, TextAnchor align = TextAnchor.MiddleCenter, bool bold = false)
    {
        var t = r.gameObject.AddComponent<Text>();
        t.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.color = c; t.alignment = align;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.supportRichText = true; t.raycastTarget = false;
        return t;
    }

    // 濃紺の塗り+金の飾り枠
    RectTransform Panel(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, float frameScale = 1f)
    {
        var r = NewRect(name, parent, aMin, aMax, oMin, oMax);
        var fill = NewRect("Fill", r, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
        AddImage(fill, PanelFill);
        if (panelFrameSprite != null)
        {
            var frame = NewRect("Frame", r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var img = AddImage(frame, Color.white, panelFrameSprite, true);
            img.pixelsPerUnitMultiplier = frameScale;
        }
        return r;
    }

    (RectTransform rect, Text label, Image fill) Button(string name, Transform parent, string label, int fontSize, System.Action action, bool overlayLayer = false)
    {
        var r = NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var fillR = NewRect("Fill", r, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
        var fill = AddImage(fillR, new Color(0.08f, 0.1f, 0.2f, 0.95f));
        if (panelFrameSprite != null)
        {
            var img = AddImage(NewRect("Frame", r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), Color.white, panelFrameSprite, true);
            img.pixelsPerUnitMultiplier = 2.4f;
        }
        var t = AddText(NewRect("Label", r, Vector2.zero, Vector2.one, new Vector2(6, 2), new Vector2(-6, -2)), fontSize, Color.white, TextAnchor.MiddleCenter, true);
        t.text = label;
        // 狭い画面比率でも文字が切れないよう、枠に収まる大きさまで自動で縮める(最小14)
        t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Min(14, fontSize); t.resizeTextMaxSize = fontSize;
        taps.Add(new Tap { rect = r, action = action, overlay = overlayLayer });
        return (r, t, fill);
    }

    ScrollRect MakeScroll(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, out RectTransform content)
    {
        var view = NewRect(name, parent, aMin, aMax, oMin, oMax);
        view.gameObject.AddComponent<RectMask2D>();
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped;
        sr.inertia = false;
        content = NewRect("Content", view, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(0.5f, 1f);
        sr.viewport = view; sr.content = content;
        return sr;
    }

    void EnsureBuilt()
    {
        if (built || root == null) return;
        built = true;
        orbSprite = Resources.Load<Sprite>("Effects/orb");
        burstSprite = Resources.Load<Sprite>("Effects/burst");
        ringSprite = Resources.Load<Sprite>("Effects/ring");
        var rootRect = (RectTransform)root.transform;
        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);

        // 背景
        var bg = NewRect("Backdrop", rootRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddImage(bg, new Color(0.035f, 0.04f, 0.09f, 0.97f));
        bg.SetAsFirstSibling();

        // ===== ヘッダー ===== //
        var header = NewRect("Header", rootRect, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -104), Vector2.zero);
        AddImage(header, new Color(0.02f, 0.025f, 0.06f, 0.9f));
        var gline = NewRect("GoldLine", header, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 3));
        AddImage(gline, new Color(Gold.r, Gold.g, Gold.b, 0.6f));
        var back = Button("BackButton", header, "戻る", 28, () => { if (phase == Phase.Select) Close(); });
        backButton = back.rect;
        back.rect.anchorMin = back.rect.anchorMax = new Vector2(0, 0.5f);
        back.rect.pivot = new Vector2(0, 0.5f); back.rect.sizeDelta = new Vector2(170, 66); back.rect.anchoredPosition = new Vector2(26, 0);
        var title = AddText(NewRect("Title", header, new Vector2(0.3f, 0), new Vector2(0.7f, 1), Vector2.zero, Vector2.zero), 44, Gold, TextAnchor.MiddleCenter, true);
        title.text = "カード合成";
        var milePanel = Panel("MilePanel", header, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero, 2.4f);
        milePanel.pivot = new Vector2(1, 0.5f); milePanel.sizeDelta = new Vector2(330, 66); milePanel.anchoredPosition = new Vector2(-146, 0); // 右端は共通の設定ボタン(SettingsPanel.MenuGearRect)の場所
        mileText = AddText(NewRect("Mile", milePanel, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0)), 28, Gold, TextAnchor.MiddleCenter, true);

        // ===== 左: 所持カード一覧 ===== //
        leftPanel = Panel("ListPanel", rootRect, new Vector2(0.01f, 0.02f), new Vector2(0.595f, 1f), Vector2.zero, new Vector2(0, -116));
        var filterRow = NewRect("FilterRow", leftPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -78), new Vector2(-22, -20));
        for (int i = 0; i < FilterLabels.Length; i++)
        {
            int idx = i;
            var b = Button("Filter" + i, filterRow, FilterLabels[i], 22, () => { filterIndex = idx; Refresh(false, resetScroll: true); });
            float w = 1f / FilterLabels.Length;
            b.rect.anchorMin = new Vector2(w * i, 0); b.rect.anchorMax = new Vector2(w * (i + 1), 1);
            b.rect.offsetMin = new Vector2(3, 0); b.rect.offsetMax = new Vector2(-3, 0);
            filterButtons.Add(b);
        }
        var sortRow = NewRect("SortRow", leftPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -134), new Vector2(-22, -84));
        var sortLabel = AddText(NewRect("SortLabel", sortRow, new Vector2(0, 0), new Vector2(0.14f, 1), Vector2.zero, Vector2.zero), 21, TextDim, TextAnchor.MiddleLeft);
        sortLabel.text = "並び替え";
        for (int i = 0; i < SortLabels.Length; i++)
        {
            int idx = i;
            var b = Button("Sort" + i, sortRow, SortLabels[i], 21, () =>
            {
                if (sortMode == idx) sortDescending = !sortDescending; else { sortMode = idx; sortDescending = idx != 0; }
                Refresh(false, resetScroll: true);
            });
            float x0 = 0.14f + i * 0.135f;
            b.rect.anchorMin = new Vector2(x0, 0); b.rect.anchorMax = new Vector2(x0 + 0.13f, 1);
            b.rect.offsetMin = b.rect.offsetMax = Vector2.zero;
            sortButtons.Add(b);
        }
        listCountText = AddText(NewRect("Count", sortRow, new Vector2(0.69f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero), 21, TextDim, TextAnchor.MiddleRight);
        gridScroll = MakeScroll("Grid", leftPanel, Vector2.zero, Vector2.one, new Vector2(18, 16), new Vector2(-18, -142), out gridContent);

        // ===== 右: 合成操作・詳細 ===== //
        rightPanel = Panel("OpPanel", rootRect, new Vector2(0.605f, 0.02f), new Vector2(0.99f, 1f), Vector2.zero, new Vector2(0, -116));
        var slotArea = NewRect("Slots", rightPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -356), new Vector2(-18, -16));
        string[] slotNames = { "メイン", "素材" };
        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            var s = new Slot();
            s.frame = NewRect("Slot" + i, slotArea, new Vector2(i == 0 ? 0.03f : 0.53f, 0), new Vector2(i == 0 ? 0.47f : 0.97f, 1), Vector2.zero, Vector2.zero);
            s.glow = AddImage(NewRect("Glow", s.frame, Vector2.zero, Vector2.one, new Vector2(-4, -4), new Vector2(4, 4)), new Color(1, 0.8f, 0.3f, 0f));
            var inner = NewRect("Inner", s.frame, Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
            AddImage(inner, new Color(0.05f, 0.06f, 0.12f, 0.95f));
            s.label = AddText(NewRect("Label", s.frame, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -40), new Vector2(-58, 0)), 24, Gold, TextAnchor.MiddleCenter, true);
            s.label.resizeTextForBestFit = true; s.label.resizeTextMinSize = 14; s.label.resizeTextMaxSize = 24;
            s.label.text = slotNames[i] + "カード";
            var badgeR = NewRect("ActiveBadge", s.frame, new Vector2(0.2f, 0), new Vector2(0.8f, 0), new Vector2(0, 6), new Vector2(0, 38));
            s.activeBadgeBg = AddImage(badgeR, new Color(0.55f, 0.36f, 0.08f, 0.95f));
            s.activeBadge = AddText(NewRect("Text", badgeR, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 20, Color.white, TextAnchor.MiddleCenter, true);
            s.activeBadge.text = "選択中";
            var slotScaler = Scaler(s.frame, "Scaler", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            slotScaler.anchoredPosition = new Vector2(0, -4);
            slotScaler.localScale = Vector3.one * 0.9f;
            s.card = CloneCard(slotScaler, "SlotCard" + i);
            taps.Add(new Tap { rect = s.frame, action = () => { activeSlot = idx; Refresh(false); } });
            var clr = Button("Clear" + i, s.frame, "×", 30, () => { SetSlot(idx, null); activeSlot = idx; Refresh(false); });
            clr.rect.anchorMin = clr.rect.anchorMax = new Vector2(1, 1); clr.rect.pivot = new Vector2(1, 1);
            clr.rect.sizeDelta = new Vector2(52, 52); clr.rect.anchoredPosition = new Vector2(-4, -4);
            s.clearButton = clr.rect;
            // ×ボタンは枠そのもののタップより優先(後から登録したものが優先される)
            slots[i] = s;
        }
        var plus = AddText(NewRect("Plus", slotArea, new Vector2(0.47f, 0.3f), new Vector2(0.53f, 0.7f), Vector2.zero, Vector2.zero), 48, Gold, TextAnchor.MiddleCenter, true);
        plus.text = "+";

        detailScroll = MakeScroll("Detail", rightPanel, Vector2.zero, Vector2.one, new Vector2(24, 150), new Vector2(-24, -366), out RectTransform detailContent);
        detailText = AddText(NewRect("DetailText", detailContent, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero), 22, TextMain, TextAnchor.UpperLeft);
        detailText.rectTransform.pivot = new Vector2(0.5f, 1f);
        detailText.verticalOverflow = VerticalWrapMode.Overflow;
        detailText.lineSpacing = 1.12f;
        var fitter = detailText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        statusText = AddText(NewRect("Status", rightPanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 104), new Vector2(-24, 146)), 21, ErrorRed, TextAnchor.MiddleCenter);
        var fuse = Button("FuseButton", rightPanel, "合成する", 34, OnFuseTapped);
        fuse.rect.anchorMin = new Vector2(0, 0); fuse.rect.anchorMax = new Vector2(1, 0);
        fuse.rect.pivot = new Vector2(0.5f, 0); fuse.rect.offsetMin = new Vector2(40, 20); fuse.rect.offsetMax = new Vector2(-40, 100);
        fuseButton = fuse.rect; fuseLabel = fuse.label; fuseFill = fuse.fill;

        BuildOverlay(rootRect);
    }

    RewardCardUI CloneCard(Transform parent, string name)
    {
        if (cardTemplate == null) return null;
        var go = Instantiate(cardTemplate.gameObject, parent, false);
        go.name = name;
        go.SetActive(true);
        var c = go.GetComponent<RewardCardUI>();
        c.rect.anchorMin = c.rect.anchorMax = new Vector2(0.5f, 0.5f);
        c.rect.pivot = new Vector2(0.5f, 0.5f);
        c.rect.sizeDelta = cardTemplate.rect.sizeDelta;
        c.rect.anchoredPosition = Vector2.zero;
        return c;
    }

    // RewardCardUIは表示のたびに自分のlocalScaleを1(選択時1.05)へ戻すので、
    // 大きさは親の「スケーラー」側で変える。
    RectTransform Scaler(Transform parent, string name, Vector2 anchor, Vector2 pivot)
    {
        var r = NewRect(name, parent, anchor, anchor, Vector2.zero, Vector2.zero);
        r.pivot = pivot;
        r.sizeDelta = cardTemplate != null ? cardTemplate.rect.sizeDelta : new Vector2(180, 270);
        return r;
    }

    void PrepareCard(RewardCardUI c)
    {
        c.gameObject.SetActive(true);
        c.rect.localScale = Vector3.one;
        if (c.canvasGroup != null) c.canvasGroup.alpha = 1f;
    }

    void BuildOverlay(RectTransform rootRect)
    {
        overlay = NewRect("FusionOverlay", rootRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
        dim = AddImage(NewRect("Dim", overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), new Color(0.01f, 0.01f, 0.04f, 0.9f));
        circle = AddImage(Fixed("MagicCircle", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(640, 640)), new Color(1, 1, 1, 0), magicCircleSprite);
        circle.preserveAspect = true;
        circle2 = AddImage(Fixed("MagicCircle2", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(420, 420)), new Color(1, 1, 1, 0), magicCircleSprite);
        circle2.preserveAspect = true;
        ring = AddImage(Fixed("Ring", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(300, 300)), new Color(1, 1, 1, 0), ringSprite);
        ring2 = AddImage(Fixed("Ring2", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(300, 300)), new Color(1, 1, 1, 0), ringSprite);
        burst = AddImage(Fixed("Burst", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(700, 700)), new Color(1, 1, 1, 0), burstSprite);
        fxMain = CloneCard(overlay, "FxMain");
        fxMaterial = CloneCard(overlay, "FxMaterial");
        fxMainLabel = AddText(Fixed("FxMainLabel", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 260), new Vector2(420, 70)), 38, Gold, TextAnchor.MiddleCenter, true);
        fxMaterialLabel = AddText(Fixed("FxMaterialLabel", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(300, 260), new Vector2(420, 70)), 38, Gold, TextAnchor.MiddleCenter, true);
        var fxResultScaler = Scaler(overlay, "FxResultScaler", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        fxResultScaler.anchoredPosition = new Vector2(0, 30);
        fxResultScaler.localScale = Vector3.one * 1.7f;
        fxResult = CloneCard(fxResultScaler, "FxResult");
        fxCaption = AddText(Fixed("FxCaption", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(1400, 70)), 40, Gold, TextAnchor.MiddleCenter, true);
        fxMile = AddText(Fixed("FxMile", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(900, 120)), 64, Gold, TextAnchor.MiddleCenter, true);
        flash = AddImage(NewRect("Flash", overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0));

        var skip = Button("Skip", overlay, "スキップ ▶▶", 24, () => skipRequested = true, overlayLayer: true);
        skip.rect.anchorMin = skip.rect.anchorMax = new Vector2(1, 0); skip.rect.pivot = new Vector2(1, 0);
        skip.rect.sizeDelta = new Vector2(230, 64); skip.rect.anchoredPosition = new Vector2(-30, 30);
        skipButton = skip.rect;

        // ===== リザルト ===== //
        resultPanel = Panel("ResultPanel", overlay, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
        resultTitle = AddText(NewRect("Title", resultPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -96), new Vector2(-30, -24)), 48, Gold, TextAnchor.MiddleCenter, true);
        var resultScaler = Scaler(resultPanel, "ResultCardScaler", new Vector2(0.2f, 0.53f), new Vector2(0.5f, 0.5f));
        resultScaler.localScale = Vector3.one * 2.0f;
        resultCard = CloneCard(resultScaler, "ResultCard");
        resultScroll = MakeScroll("ResultBody", resultPanel, new Vector2(0.4f, 0), new Vector2(1, 1), new Vector2(0, 130), new Vector2(-40, -110), out RectTransform bodyContent);
        resultBody = AddText(NewRect("Body", bodyContent, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero), 26, TextMain, TextAnchor.UpperLeft);
        resultBody.rectTransform.pivot = new Vector2(0.5f, 1f);
        resultBody.verticalOverflow = VerticalWrapMode.Overflow;
        resultBody.lineSpacing = 1.15f;
        resultBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var cont = Button("Continue", resultPanel, "続けて合成", 30, OnContinue, overlayLayer: true);
        cont.rect.anchorMin = cont.rect.anchorMax = new Vector2(0.72f, 0); cont.rect.pivot = new Vector2(0.5f, 0);
        cont.rect.sizeDelta = new Vector2(320, 84); cont.rect.anchoredPosition = new Vector2(-180, 30);
        continueButton = cont.rect;
        var back = Button("BackToList", resultPanel, "一覧へ戻る", 30, OnBackToList, overlayLayer: true);
        back.rect.anchorMin = back.rect.anchorMax = new Vector2(0.72f, 0); back.rect.pivot = new Vector2(0.5f, 0);
        back.rect.sizeDelta = new Vector2(320, 84); back.rect.anchoredPosition = new Vector2(180, 30);
        backToListButton = back.rect;

        overlay.SetAsLastSibling();
        HideOverlay();
    }

    // ===================================================================== //
    // 表示の更新
    // ===================================================================== //

    void SetStatus(string text, bool error)
    {
        if (statusText == null) return;
        statusText.text = text;
        statusText.color = error ? ErrorRed : Gold;
    }

    void SetSlot(int i, string key)
    {
        if (i == 0) mainKey = key; else materialKey = key;
    }

    static bool MatchesFilter(CardDefinition def)
    {
        if (filterIndex <= 0 || def == null) return true;
        return (int)def.category == filterIndex - 1;
    }

    public void Refresh(bool restoreScroll = false, bool resetScroll = false)
    {
        var gm = GameManager.Instance;
        if (!built) return;
        if (mileText != null) mileText.text = $"所持MILE  {(gm != null ? gm.TotalOwnedMile : 0):N0}";

        for (int i = 0; i < filterButtons.Count; i++) Highlight(filterButtons[i], i == filterIndex);
        for (int i = 0; i < sortButtons.Count; i++)
        {
            Highlight(sortButtons[i], i == sortMode);
            sortButtons[i].label.text = SortLabels[i] + (i == sortMode ? (sortDescending ? " ▼" : " ▲") : "");
        }

        // ---- 一覧 ----
        float keepScroll = gridScroll != null ? gridScroll.verticalNormalizedPosition : 1f;
        shown.Clear();
        int totalCopies = 0;
        foreach (var s in CardInventory.Stacks)
        {
            if (s.count <= 0) continue;
            CardDefinition def = CardDatabase.FindById(s.cardId);
            if (def == null) continue;
            totalCopies += s.count;
            if (MatchesFilter(def)) shown.Add(s);
        }
        shown.Sort(CompareStacks);
        if (listCountText != null) listCountText.text = $"{shown.Count}種 / 全{totalCopies}枚";
        LayoutGrid();

        // ---- 枠 ----
        for (int i = 0; i < 2; i++)
        {
            var s = slots[i];
            string key = i == 0 ? mainKey : materialKey;
            CardDefinition def = string.IsNullOrEmpty(key) ? null : CardDatabase.FindById(key);
            if (def != null) { s.card.ShowFrontImmediate(MakeData(def, key), showDetails: false); s.card.SetSelected(true); }
            else s.card.ShowEmpty();
            s.clearButton.gameObject.SetActive(def != null);
            bool active = i == activeSlot;
            s.glow.color = active ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(1f, 0.8f, 0.3f, 0.12f);
            s.label.text = i == 0 ? "メインカード" : "素材カード";
            s.label.color = active ? Gold : TextDim;
            s.activeBadgeBg.gameObject.SetActive(active);
        }

        // ---- 詳細・ボタン ----
        string block = CardFusionLogic.BlockReason(mainKey, materialKey);
        detailText.text = BuildDetail(block);
        bool ready = block == null;
        fuseLabel.text = ready ? "合成する" : (string.IsNullOrEmpty(mainKey) || string.IsNullOrEmpty(materialKey) ? "カードを2枚選んでください" : "合成できません");
        fuseLabel.fontSize = ready ? 34 : 26;
        fuseFill.color = ready ? new Color(0.55f, 0.36f, 0.08f, 0.98f) : new Color(0.12f, 0.13f, 0.18f, 0.95f);

        Canvas.ForceUpdateCanvases();
        if (gridScroll != null)
        {
            if (resetScroll) gridScroll.verticalNormalizedPosition = 1f;
            else if (restoreScroll) gridScroll.verticalNormalizedPosition = savedGridScroll;
            else gridScroll.verticalNormalizedPosition = keepScroll;
            savedGridScroll = gridScroll.verticalNormalizedPosition;
        }
    }

    void Highlight((RectTransform rect, Text label, Image fill) b, bool on)
    {
        b.fill.color = on ? new Color(0.5f, 0.33f, 0.07f, 0.98f) : new Color(0.08f, 0.1f, 0.2f, 0.95f);
        b.label.color = on ? Color.white : TextDim;
    }

    int CompareStacks(CardInventory.Stack a, CardInventory.Stack b)
    {
        CardDefinition da = CardDatabase.FindById(a.cardId), db = CardDatabase.FindById(b.cardId);
        int byName = string.CompareOrdinal(da.cardName, db.cardName);
        int primary;
        switch (sortMode)
        {
            case 1: primary = a.level.CompareTo(b.level); break;
            case 2: primary = da.rarity.CompareTo(db.rarity); break;
            case 3: primary = a.count.CompareTo(b.count); break;
            default: primary = byName; break;
        }
        if (sortDescending) primary = -primary;
        if (primary != 0) return primary;
        if (byName != 0) return byName;
        int byLevel = b.level.CompareTo(a.level);
        return byLevel != 0 ? byLevel : string.CompareOrdinal(a.cardId, b.cardId);
    }

    RewardCardData MakeData(CardDefinition def, string key)
    {
        var gm = GameManager.Instance;
        CardVariant v = CardVariant.Parse(key);
        CardInventory.Stack s = CardInventory.FindByKey(key);
        var data = gm != null ? gm.MakeOwnedCardData(def, v != null ? v.level : 1, s != null ? s.count : 0) : new RewardCardData { CardId = key, Icon = def.icon, Title = def.cardName, Rarity = def.rarity, Category = def.category };
        data.LevelLine = v != null ? (v.level >= CardVariant.MaxLevel ? $"Lv.{v.level} MAX" : $"Lv.{v.level}") : data.LevelLine;
        return data;
    }

    // 2段が必ず完全に見える大きさで並べる(画面比率が変わっても毎回計算)。
    void LayoutGrid()
    {
        Canvas.ForceUpdateCanvases();
        var view = gridScroll.viewport.rect;
        const float gap = 16f, infoH = 44f, pad = 6f;
        float cardH = Mathf.Min(330f, (view.height - pad * 2f - gap - infoH * 2f) / 2f);
        cardH = Mathf.Max(150f, cardH);
        float cardW = cardH / 1.5f;
        int cols = Mathf.Max(1, Mathf.FloorToInt((view.width - pad * 2f + gap) / (cardW + gap)));
        float usedW = cols * cardW + (cols - 1) * gap;
        float startX = -usedW / 2f + cardW / 2f;
        float cellH = cardH + infoH;
        int rows = Mathf.CeilToInt(shown.Count / (float)cols);
        gridContent.sizeDelta = new Vector2(0f, Mathf.Max(view.height, pad * 2f + rows * cellH + Mathf.Max(0, rows - 1) * gap));
        float scale = cardW / (cardTemplate != null ? cardTemplate.rect.sizeDelta.x : 180f);

        while (cells.Count < shown.Count) cells.Add(MakeCell(cells.Count));
        for (int i = 0; i < cells.Count; i++)
        {
            Cell c = cells[i];
            if (i >= shown.Count) { c.root.gameObject.SetActive(false); c.key = null; continue; }
            var s = shown[i];
            int col = i % cols, row = i / cols;
            c.root.gameObject.SetActive(true);
            c.root.sizeDelta = new Vector2(cardW, cellH);
            c.root.anchoredPosition = new Vector2(startX + col * (cardW + gap), -pad - row * (cellH + gap));
            c.scaler.localScale = Vector3.one * scale;
            c.scaler.anchoredPosition = Vector2.zero;
            c.key = s.cardId;
            CardDefinition def = CardDatabase.FindById(s.cardId);
            c.card.ShowFrontImmediate(MakeData(def, s.cardId), showDetails: false);

            int available = CardFusionLogic.AvailableCount(s.cardId);
            int selected = (mainKey == s.cardId ? 1 : 0) + (materialKey == s.cardId ? 1 : 0);
            CardVariant v = CardVariant.Parse(s.cardId);
            int abilities = v != null ? v.AbilityCount : 1;
            c.info.text = $"所持{s.count}  使用可<color=#{(available - selected > 0 ? "ffe08a" : "ff8f86")}>{Mathf.Max(0, available - selected)}</color>" + (abilities > 1 ? $"  能力{abilities}" : "");
            bool isMain = mainKey == s.cardId, isMat = materialKey == s.cardId;
            c.card.SetSelected(isMain || isMat);
            c.tag.text = isMain && isMat ? "メイン+素材" : isMain ? "メイン" : isMat ? "素材" : (available <= 0 ? "使用中" : "");
            c.tagBg.color = isMain || isMat ? new Color(0.55f, 0.36f, 0.08f, 0.95f) : new Color(0.3f, 0.08f, 0.08f, 0.9f);
            c.tagBg.gameObject.SetActive(c.tag.text.Length > 0);
            if (available <= 0 && !isMain && !isMat && c.card.frameImage != null) c.card.frameImage.color = new Color(1f, 1f, 1f, 0.35f);
            if (c.card.canvasGroup != null) c.card.canvasGroup.alpha = available <= 0 && !isMain && !isMat ? 0.55f : 1f;
        }
    }

    Cell MakeCell(int index)
    {
        var c = new Cell();
        c.root = NewRect("Cell" + index, gridContent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        c.root.pivot = new Vector2(0.5f, 1f);
        c.scaler = Scaler(c.root, "Scaler", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        c.card = CloneCard(c.scaler, "Card");
        var infoR = NewRect("Info", c.root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-6, 0), new Vector2(6, 40));
        AddImage(infoR, new Color(0.02f, 0.03f, 0.07f, 0.85f));
        c.info = AddText(NewRect("Text", infoR, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 19, TextMain);
        var tagR = NewRect("Tag", c.root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -40), new Vector2(-10, -6));
        c.tagBg = AddImage(tagR, new Color(0.55f, 0.36f, 0.08f, 0.95f));
        c.tag = AddText(NewRect("Text", tagR, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 20, Color.white, TextAnchor.MiddleCenter, true);
        var cell = c;
        taps.Add(new Tap { rect = c.root, action = () => { if (cell.key != null) OnCellTapped(cell.key); } });
        return c;
    }

    string CardDetail(string key, string heading)
    {
        CardVariant v = CardVariant.Parse(key);
        CardDefinition def = CardDatabase.FindById(key);
        CardInventory.Stack s = CardInventory.FindByKey(key);
        if (v == null || def == null) return "";
        var sb = new StringBuilder();
        sb.Append($"<color=#ffd76a><b>{heading}</b></color>  <b>{def.cardName}</b>  {new string('★', v.rarity)}  合成Lv.{v.level}{(v.level >= CardVariant.MaxLevel ? " (上限)" : "")}\n");
        sb.Append($"主能力: {CardVariant.AbilityLine(v.Main.id, v.Main.stacks)}\n");
        if (v.AbilityCount > 1)
        {
            sb.Append($"サブ能力({v.AbilityCount - 1}):\n");
            for (int i = 1; i < v.AbilityCount; i++) sb.Append($"  ・{CardVariant.AbilityLine(v.abilities[i].id, v.abilities[i].stacks)}\n");
        }
        else sb.Append("サブ能力: なし\n");
        if (s != null) sb.Append($"<size=19><color=#b7bfd8>所持{s.count}枚 / 使用可{CardFusionLogic.AvailableCount(key)}枚</color></size>\n");
        return sb.ToString();
    }

    string BuildDetail(string block)
    {
        bool hasMain = !string.IsNullOrEmpty(mainKey), hasMat = !string.IsNullOrEmpty(materialKey);
        if (!hasMain && !hasMat)
            return "左の一覧からカードを選んでください。\n\n選択中の枠(光っている枠)にカードが入ります。枠をタップすると選択先を切り替えられ、×で選択を外せます。\n\n・同じカード同士 … 成功率100%で合成Lvと全能力を合算\n・違うカード同士 … メイン側50%/素材側25%で能力一式を継承(抽選)。片側だけ成功した場合は、成功した側のLvと能力だけが残ります\n・合成Lvの上限はLv.9(2枚の合計がLv.9を超える組み合わせは合成できません)";
        var sb = new StringBuilder();
        if (hasMain) sb.Append(CardDetail(mainKey, "メイン")).Append('\n');
        if (hasMat) sb.Append(CardDetail(materialKey, "素材")).Append('\n');
        if (hasMain && hasMat)
        {
            if (block != null) sb.Append($"<color=#ff8f86><b>{block}</b></color>\n\n");
            else sb.Append("――――――――――――――――\n").Append(CardFusionLogic.Preview(mainKey, materialKey));
        }
        else
        {
            CardVariant v = CardVariant.Parse(hasMain ? mainKey : materialKey);
            if (v != null && v.level >= CardVariant.MaxLevel) sb.Append("<color=#ff8f86>このカードは合成Lv.9(上限)のため、これ以上合成できません。</color>\n");
            else sb.Append(hasMain ? "次に素材カードを選んでください。" : "次にメインカードを選んでください。");
        }
        return sb.ToString();
    }

    // ===================================================================== //
    // 操作
    // ===================================================================== //

    void OnCellTapped(string key)
    {
        if (phase != Phase.Select) return;
        CardInventory.ClearNewUnconfirmed(key);
        FusionSfx.Play(FusionSfx.Tap(), 0.6f);
        string current = activeSlot == 0 ? mainKey : materialKey;
        if (current == key) { SetSlot(activeSlot, null); SetStatus("", false); Refresh(); return; }
        string other = activeSlot == 0 ? materialKey : mainKey;
        string reason = CardFusionLogic.LockReason(key, other == key ? 2 : 1);
        if (reason != null)
        {
            CardDefinition def = CardDatabase.FindById(key);
            SetStatus($"{(def != null ? def.cardName : "")}: {reason}", true);
            Refresh();
            return;
        }
        SetStatus("", false);
        SetSlot(activeSlot, key);
        if (activeSlot == 0 && string.IsNullOrEmpty(materialKey)) activeSlot = 1;
        else if (activeSlot == 1 && string.IsNullOrEmpty(mainKey)) activeSlot = 0;
        Refresh();
    }

    void OnFuseTapped()
    {
        if (phase != Phase.Select) return;
        string block = CardFusionLogic.BlockReason(mainKey, materialKey);
        if (block != null) { SetStatus(block, true); return; }
        phase = Phase.Presenting; // ここから先の入力(連打・戻る・一覧タップ)は受け付けない
        savedGridScroll = gridScroll.verticalNormalizedPosition;
        var fromMain = slots[0].card.rect.position;
        var fromMat = slots[1].card.rect.position;
        CardFusionLogic.FusionResult r = CardFusionLogic.Execute(mainKey, materialKey, out string error);
        if (r == null) { phase = Phase.Select; SetStatus(error, true); Refresh(); return; }
        StartCoroutine(Present(r, fromMain, fromMat));
    }

    void OnContinue()
    {
        if (phase != Phase.Result) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        var r = shownResult;
        HideOverlay();
        phase = Phase.Select;
        if (r != null && r.IsSuccess && CardInventory.FindByKey(r.resultKey) != null && CardFusionLogic.LockReason(r.resultKey) == null)
        {
            mainKey = r.resultKey; materialKey = null; activeSlot = 1;
            SetStatus(r.result.level >= CardVariant.MaxLevel ? "完成カードは合成Lv.9(上限)のため、これ以上合成できません" : "完成カードをメインにセットしました。素材カードを選んでください", r.result.level >= CardVariant.MaxLevel);
        }
        else { mainKey = materialKey = null; activeSlot = 0; SetStatus("", false); }
        Refresh(restoreScroll: true);
    }

    void OnBackToList()
    {
        if (phase != Phase.Result) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
        HideOverlay();
        phase = Phase.Select;
        mainKey = materialKey = null; activeSlot = 0;
        SetStatus("", false);
        Refresh(restoreScroll: true);
    }

    // ===================================================================== //
    // 演出
    // ===================================================================== //

    CardFusionLogic.FusionResult shownResult;

    void HideOverlay()
    {
        if (overlay == null) return;
        overlay.gameObject.SetActive(false);
        ClearParticles();
    }

    // 待ち(スキップされたら即終了)
    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds && !skipRequested) { t += Time.unscaledDeltaTime; yield return null; }
    }

    IEnumerator Tween(float seconds, System.Action<float> step)
    {
        float t = 0f;
        while (t < seconds && !skipRequested)
        {
            t += Time.unscaledDeltaTime;
            step(Mathf.Clamp01(t / seconds));
            yield return null;
        }
        step(1f);
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    static float EaseInOut(float x) => x * x * (3f - 2f * x);

    void ResetFx()
    {
        overlay.gameObject.SetActive(true);
        overlay.SetAsLastSibling();
        overlayGroup.alpha = 1f;
        resultPanel.gameObject.SetActive(false);
        skipButton.gameObject.SetActive(true);
        foreach (var img in new[] { circle, circle2, flash, ring, ring2, burst }) { var c = img.color; c.a = 0f; img.color = c; img.rectTransform.localScale = Vector3.one; img.rectTransform.localRotation = Quaternion.identity; }
        fxMainLabel.text = fxMaterialLabel.text = fxCaption.text = fxMile.text = "";
        fxResult.gameObject.SetActive(false);
        dim.color = new Color(0.01f, 0.01f, 0.04f, 0f);
    }

    IEnumerator Present(CardFusionLogic.FusionResult r, Vector3 fromMain, Vector3 fromMat)
    {
        shownResult = r;
        skipRequested = false;
        ResetFx();
        bool same = r.kind == CardFusionLogic.Kind.SameName;
        bool both = r.kind == CardFusionLogic.Kind.CrossBoth;
        Color accent = same ? new Color(1f, 0.85f, 0.45f) : both ? new Color(0.55f, 1f, 0.95f) : r.IsSuccess ? new Color(1f, 0.85f, 0.45f) : new Color(1f, 0.75f, 0.3f);

        // ---- 2枚を中央へ提示 ----
        PrepareCard(fxMain); PrepareCard(fxMaterial);
        fxMain.ShowFrontImmediate(MakeData(CardDatabase.FindById(r.mainKey), r.mainKey), showDetails: false);
        fxMaterial.ShowFrontImmediate(MakeData(CardDatabase.FindById(r.materialKey), r.materialKey), showDetails: false);
        fxMain.rect.position = fromMain; fxMaterial.rect.position = fromMat;
        Vector2 a0 = fxMain.rect.anchoredPosition, b0 = fxMaterial.rect.anchoredPosition;
        Vector2 aT = new Vector2(-300, 30), bT = new Vector2(300, 30);
        FusionSfx.Play(FusionSfx.Charge(), 0.7f);
        yield return Tween(0.4f, f =>
        {
            float e = EaseOut(f);
            dim.color = new Color(0.01f, 0.01f, 0.04f, 0.9f * f);
            fxMain.rect.anchoredPosition = Vector2.Lerp(a0, aT, e);
            fxMaterial.rect.anchoredPosition = Vector2.Lerp(b0, bT, e);
            fxMain.rect.localScale = fxMaterial.rect.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.3f, e);
            circle.color = new Color(accent.r, accent.g, accent.b, 0.85f * f);
            circle.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, e);
        });

        // ---- 魔法陣に光が集まる ----
        for (int i = 0; i < 26; i++) SpawnParticle(RandomEdge(), new Vector2(0, 30), accent, Random.Range(0.45f, 0.75f), Random.Range(18f, 34f), true);
        yield return Tween(0.45f, f =>
        {
            circle.rectTransform.localRotation = Quaternion.Euler(0, 0, -f * 60f);
            circle2.color = new Color(1f, 1f, 1f, 0.6f * f);
            circle2.rectTransform.localRotation = Quaternion.Euler(0, 0, f * 90f);
        });

        // ---- 継承抽選の結果(両側それぞれ) ----
        if (same)
        {
            fxMainLabel.text = fxMaterialLabel.text = "確定強化";
            fxMainLabel.color = fxMaterialLabel.color = Gold;
            FusionSfx.Play(FusionSfx.Success());
            yield return Wait(0.35f);
        }
        else
        {
            yield return RevealSide(fxMain, fxMainLabel, r.mainInherited, "メイン");
            yield return Wait(0.12f);
            yield return RevealSide(fxMaterial, fxMaterialLabel, r.materialInherited, "素材");
            yield return Wait(0.2f);
        }

        if (r.IsSuccess)
        {
            // ---- 成功した側の光が収束(失敗した側は砕ける) ----
            if (!r.mainInherited) Shatter(fxMain, new Color(0.6f, 0.62f, 0.7f));
            if (!r.materialInherited) Shatter(fxMaterial, new Color(0.6f, 0.62f, 0.7f));
            Vector2 ma = fxMain.rect.anchoredPosition, mb = fxMaterial.rect.anchoredPosition;
            yield return Tween(0.32f, f =>
            {
                float e = EaseInOut(f);
                if (r.mainInherited) { fxMain.rect.anchoredPosition = Vector2.Lerp(ma, new Vector2(0, 30), e); fxMain.rect.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.3f, e); }
                if (r.materialInherited) { fxMaterial.rect.anchoredPosition = Vector2.Lerp(mb, new Vector2(0, 30), e); fxMaterial.rect.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.3f, e); }
                circle.rectTransform.localRotation = Quaternion.Euler(0, 0, -60f - f * 200f);
                circle.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.75f, e);
            });
            fxMain.gameObject.SetActive(false); fxMaterial.gameObject.SetActive(false);

            // ---- 光のバースト+完成カード ----
            FusionSfx.Play(both ? FusionSfx.BigBurst() : FusionSfx.Burst(), 0.85f);
            int n = both ? 46 : same ? 30 : 22;
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Color c = both && i % 2 == 0 ? Gold : accent;
                SpawnParticle(new Vector2(0, 30), new Vector2(0, 30) + dir * Random.Range(420f, 820f), c, Random.Range(0.6f, 1.0f), Random.Range(18f, 40f), false);
            }
            PrepareCard(fxResult);
            fxResult.SetContent(MakeData(CardDatabase.FindById(r.resultKey), r.resultKey), showDetails: false);
            fxResult.ShowBack();
            StartCoroutine(fxResult.FlipToFront(0.3f));
            yield return Tween(0.55f, f =>
            {
                flash.color = new Color(1f, 1f, 1f, 0.85f * (1f - f));
                burst.color = new Color(accent.r, accent.g, accent.b, 0.9f * (1f - f));
                burst.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.6f, EaseOut(f));
                ring.color = new Color(accent.r, accent.g, accent.b, 1f - f);
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, both ? 5f : 3.6f, EaseOut(f));
                if (both)
                {
                    float f2 = Mathf.Clamp01(f * 1.4f - 0.3f);
                    ring2.color = new Color(Gold.r, Gold.g, Gold.b, 1f - f2);
                    ring2.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 4.2f, EaseOut(f2));
                }
                circle.color = new Color(accent.r, accent.g, accent.b, 0.85f * (1f - f));
                circle2.color = new Color(1f, 1f, 1f, 0.6f * (1f - f));
            });
            fxCaption.text = same ? "同名強化 完了！" : both ? "大成功！ 両側の能力を継承" : r.kind == CardFusionLogic.Kind.CrossMainOnly ? "メイン側の能力を継承" : "素材側の能力を継承";
            fxCaption.color = both ? new Color(0.7f, 1f, 0.95f) : Gold;
            yield return Wait(0.45f);
        }
        else
        {
            // ---- 全失敗: カードが粒子になりMILEへ ----
            Shatter(fxMain, Gold); Shatter(fxMaterial, Gold);
            FusionSfx.Play(FusionSfx.Coins(), 0.8f);
            yield return Tween(0.35f, f =>
            {
                circle.color = new Color(1f, 0.6f, 0.3f, 0.85f * (1f - f));
                circle2.color = new Color(1f, 1f, 1f, 0.6f * (1f - f));
            });
            Vector2 mileTarget = MileTargetInOverlay();
            for (int i = 0; i < 30; i++) SpawnParticle(new Vector2(Random.Range(-380f, 380f), Random.Range(-160f, 220f)), mileTarget, Gold, Random.Range(0.5f, 0.85f), Random.Range(16f, 28f), true);
            int before = r.mileAfter - r.refundMile;
            yield return Tween(0.8f, f =>
            {
                int shownMile = Mathf.RoundToInt(Mathf.Lerp(0, r.refundMile, EaseOut(f)));
                fxMile.text = $"+{shownMile:N0} MILE";
                if (mileText != null) mileText.text = $"所持MILE  {before + shownMile:N0}";
            });
            fxCaption.text = "継承失敗…  カードはMILEに還元されました";
            fxCaption.color = new Color(1f, 0.8f, 0.55f);
            yield return Wait(0.5f);
        }

        ShowResult(r);
    }

    IEnumerator RevealSide(RewardCardUI card, Text label, bool success, string side)
    {
        label.text = success ? $"{side}側 継承成功！" : $"{side}側 継承失敗";
        label.color = success ? Gold : new Color(0.62f, 0.66f, 0.76f);
        FusionSfx.Play(success ? FusionSfx.Success() : FusionSfx.Fail());
        Vector2 basePos = card.rect.anchoredPosition;
        yield return Tween(0.22f, f =>
        {
            if (success) card.rect.localScale = Vector3.one * (1.3f + Mathf.Sin(f * Mathf.PI) * 0.12f);
            else card.rect.anchoredPosition = basePos + new Vector2(Mathf.Sin(f * 40f) * 10f * (1f - f), 0f);
            if (!success && card.canvasGroup != null) card.canvasGroup.alpha = Mathf.Lerp(1f, 0.45f, f);
        });
        if (success)
        {
            for (int i = 0; i < 10; i++) SpawnParticle(card.rect.anchoredPosition, card.rect.anchoredPosition + Random.insideUnitCircle * 260f, Gold, 0.5f, 22f, false);
        }
    }

    void Shatter(RewardCardUI card, Color color)
    {
        if (card == null || !card.gameObject.activeSelf) return;
        Vector2 c = card.rect.anchoredPosition;
        for (int i = 0; i < 24; i++)
        {
            Vector2 p = c + new Vector2(Random.Range(-110f, 110f), Random.Range(-160f, 160f));
            SpawnParticle(p, p + new Vector2(Random.Range(-160f, 160f), Random.Range(-60f, 220f)), color, Random.Range(0.5f, 0.9f), Random.Range(14f, 30f), false);
        }
        StartCoroutine(card.FadeTo(0f, 0.25f));
    }

    Vector2 RandomEdge()
    {
        float a = Random.Range(0f, Mathf.PI * 2f);
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(560f, 820f);
    }

    Vector2 MileTargetInOverlay()
    {
        if (mileText == null) return new Vector2(700, 480);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, mileText.rectTransform.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screen, null, out Vector2 local);
        return local;
    }

    // ---- 粒子(UI Image) ----
    class Particle { public RectTransform rt; public Image img; public Vector2 from, to; public float t, life, size; public bool homing; public Color color; }
    readonly List<Particle> particles = new List<Particle>();
    readonly Stack<Particle> particlePool = new Stack<Particle>();

    void SpawnParticle(Vector2 from, Vector2 to, Color color, float life, float size, bool homing)
    {
        Particle p = particlePool.Count > 0 ? particlePool.Pop() : null;
        if (p == null)
        {
            p = new Particle();
            p.rt = Fixed("P", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 20f);
            p.img = AddImage(p.rt, Color.white, orbSprite);
        }
        p.rt.gameObject.SetActive(true);
        p.rt.SetSiblingIndex(Mathf.Max(0, flash.transform.GetSiblingIndex()));
        p.from = from; p.to = to; p.t = 0f; p.life = life; p.size = size; p.homing = homing; p.color = color;
        p.rt.anchoredPosition = from;
        particles.Add(p);
    }

    void UpdateParticles()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.t += dt / Mathf.Max(0.01f, p.life);
            float f = Mathf.Clamp01(p.t);
            float e = p.homing ? f * f : EaseOut(f);
            p.rt.anchoredPosition = Vector2.Lerp(p.from, p.to, e);
            float a = p.homing ? Mathf.Clamp01(f * 3f) * (1f - Mathf.SmoothStep(0.85f, 1f, f)) : 1f - f;
            p.img.color = new Color(p.color.r, p.color.g, p.color.b, a);
            p.rt.sizeDelta = Vector2.one * p.size * (p.homing ? Mathf.Lerp(1f, 0.5f, f) : Mathf.Lerp(1.2f, 0.4f, f));
            if (f >= 1f) { p.rt.gameObject.SetActive(false); particles.RemoveAt(i); particlePool.Push(p); }
        }
    }

    void ClearParticles()
    {
        foreach (var p in particles) { p.rt.gameObject.SetActive(false); particlePool.Push(p); }
        particles.Clear();
    }

    // ===================================================================== //
    // リザルト
    // ===================================================================== //

    void ShowResult(CardFusionLogic.FusionResult r)
    {
        skipRequested = false;
        phase = Phase.Result;
        ClearParticles();
        fxMain.gameObject.SetActive(false); fxMaterial.gameObject.SetActive(false); fxResult.gameObject.SetActive(false);
        foreach (var img in new[] { circle, circle2, flash, ring, ring2, burst }) { var c = img.color; c.a = 0f; img.color = c; }
        fxMainLabel.text = fxMaterialLabel.text = fxCaption.text = fxMile.text = "";
        skipButton.gameObject.SetActive(false);
        dim.color = new Color(0.01f, 0.01f, 0.04f, 0.9f);
        resultPanel.gameObject.SetActive(true);
        if (mileText != null && GameManager.Instance != null) mileText.text = $"所持MILE  {GameManager.Instance.TotalOwnedMile:N0}";

        var sb = new StringBuilder();
        if (r.IsSuccess)
        {
            resultTitle.text = r.kind == CardFusionLogic.Kind.SameName ? "同名強化 完了！" : r.kind == CardFusionLogic.Kind.CrossBoth ? "合成大成功！" : "合成成功！";
            resultTitle.color = r.kind == CardFusionLogic.Kind.CrossBoth ? new Color(0.7f, 1f, 0.95f) : Gold;
            PrepareCard(resultCard);
            resultCard.ShowFrontImmediate(MakeData(CardDatabase.FindById(r.resultKey), r.resultKey), showDetails: false);
            CardVariant v = r.result;
            CardDefinition def = CardDatabase.FindById(r.resultKey);
            sb.Append($"<b><size=34>{def?.cardName}</size></b>\n");
            sb.Append($"合成Lv.<b>{v.level}</b>{(v.level >= CardVariant.MaxLevel ? " <color=#ff8f86>(上限・これ以上合成できません)</color>" : "")}    レア度 <color=#ffd76a>{new string('★', v.rarity)}</color>\n\n");
            if (r.kind == CardFusionLogic.Kind.SameName) sb.Append("<color=#ffd76a>同名強化(確定)</color> … 両方の能力をすべて継承\n\n");
            else
            {
                sb.Append($"メイン側({CardDatabase.FindById(r.mainKey)?.cardName} Lv.{r.main.level}): {(r.mainInherited ? "<color=#ffd76a>継承成功</color>" : "<color=#9aa3b8>継承失敗</color>")}\n");
                sb.Append($"素材側({CardDatabase.FindById(r.materialKey)?.cardName} Lv.{r.material.level}): {(r.materialInherited ? "<color=#ffd76a>継承成功</color>" : "<color=#9aa3b8>継承失敗</color>")}\n");
                // 完成Lvの内訳(2026-09-28改訂: 片側だけ成功なら成功側のLvのみ)
                string lvRule = r.kind == CardFusionLogic.Kind.CrossBoth ? $"Lv.{r.main.level} + Lv.{r.material.level}"
                    : r.kind == CardFusionLogic.Kind.CrossMainOnly ? "メイン側のLvのみ" : "素材側のLvのみ";
                sb.Append($"<size=21>完成Lv.{v.level} = {lvRule}</size>\n\n");
            }
            sb.Append($"<b>主能力</b>\n{AbilityResultLine(v.Main, r.baseline)}\n");
            sb.Append($"\n<b>サブ能力 ({v.AbilityCount - 1}/{CardVariant.MaxAbilities - 1})</b>\n");
            if (v.AbilityCount <= 1) sb.Append("なし\n");
            for (int i = 1; i < v.AbilityCount; i++) sb.Append(AbilityResultLine(v.abilities[i], r.baseline)).Append('\n');
        }
        else
        {
            resultTitle.text = "合成失敗…";
            resultTitle.color = new Color(1f, 0.8f, 0.55f);
            resultCard.gameObject.SetActive(false);
            sb.Append($"メイン側({CardDatabase.FindById(r.mainKey)?.cardName} Lv.{r.main.level}): <color=#9aa3b8>継承失敗</color>\n");
            sb.Append($"素材側({CardDatabase.FindById(r.materialKey)?.cardName} Lv.{r.material.level}): <color=#9aa3b8>継承失敗</color>\n\n");
            sb.Append("選んだ2枚のカードは消費されました。\n\n");
            sb.Append($"還元MILE  <color=#ffd76a><b><size=40>+{r.refundMile:N0}</size></b></color>\n");
            sb.Append($"<size=21>(★{r.main.rarity}×Lv.{r.main.level}×{CardFusionLogic.RefundCoefficient} + ★{r.material.rarity}×Lv.{r.material.level}×{CardFusionLogic.RefundCoefficient})</size>\n\n");
            sb.Append($"所持MILE  <b>{r.mileAfter:N0}</b>\n");
        }
        resultBody.text = sb.ToString();
        resultBody.alignment = r.IsSuccess ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
        var bodyView = resultScroll.viewport;
        bodyView.anchorMin = r.IsSuccess ? new Vector2(0.4f, 0) : new Vector2(0.1f, 0);
        Canvas.ForceUpdateCanvases();
        resultScroll.verticalNormalizedPosition = 1f;
        StartCoroutine(FadeResultIn());
    }

    IEnumerator FadeResultIn()
    {
        var cg = resultPanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = resultPanel.gameObject.AddComponent<CanvasGroup>();
        float t = 0f;
        while (t < 1f) { t += Time.unscaledDeltaTime / 0.2f; cg.alpha = Mathf.Clamp01(t); resultPanel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, Mathf.Clamp01(t)); yield return null; }
    }

    static string AbilityResultLine(CardVariant.Ability a, CardVariant baseline)
    {
        int before = baseline != null ? baseline.StacksOf(a.id) : 0;
        string tag = before <= 0 ? " <color=#7dff9b><b>NEW</b></color>" : a.stacks > before ? $" <color=#ffd76a><b>強化 ×{before}→×{a.stacks}</b></color>" : "";
        return $"・{CardVariant.AbilityName(a.id)} ×{a.stacks}{tag}  <size=21><color=#b7bfd8>({CardVariant.AbilityEffectText(a.id)} ×{a.stacks})</color></size>";
    }

    // ===================================================================== //
    // 入力(DeckEditUIと同じ自前のタップ/ドラッグ判定)
    // ===================================================================== //

    bool pointerActive, draggedPastThreshold;
    Vector2 pointerDownScreenPos, lastLocal;
    ScrollRect dragScroll;
    public float dragTapThreshold = 14f;

    void Update()
    {
        if (root == null || !root.activeInHierarchy) return;
        if (particles.Count > 0) UpdateParticles();
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (UiInputGate.Blocked) return; // 設定/DEBUGパネルが手前に開いている(閉じた時の指が離れるまでも)

        bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
        bool held = !down && !up && (Input.GetMouseButton(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled));
        Vector2 screenPos = Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;

        if (down)
        {
            pointerActive = true;
            pointerDownScreenPos = screenPos;
            draggedPastThreshold = false;
            dragScroll = PickScroll(screenPos);
            if (dragScroll != null) RectTransformUtility.ScreenPointToLocalPointInRectangle(dragScroll.viewport, screenPos, null, out lastLocal);
            return;
        }
        if (!pointerActive) return;
        if (held)
        {
            if (!draggedPastThreshold && Vector2.Distance(screenPos, pointerDownScreenPos) > dragTapThreshold) draggedPastThreshold = true;
            if (draggedPastThreshold && dragScroll != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(dragScroll.viewport, screenPos, null, out Vector2 cur);
                float dy = cur.y - lastLocal.y;
                lastLocal = cur;
                float range = dragScroll.content.rect.height - dragScroll.viewport.rect.height;
                if (range > 0f) dragScroll.verticalNormalizedPosition = Mathf.Clamp01(dragScroll.verticalNormalizedPosition - dy / range);
                if (dragScroll == gridScroll) savedGridScroll = gridScroll.verticalNormalizedPosition;
            }
            return;
        }
        if (up)
        {
            pointerActive = false;
            if (!draggedPastThreshold) HandleTap(pointerDownScreenPos);
        }
    }

    ScrollRect PickScroll(Vector2 screenPos)
    {
        bool overlayOn = overlay != null && overlay.gameObject.activeSelf;
        if (overlayOn)
            return resultPanel.gameObject.activeSelf && Contains(resultScroll.viewport, screenPos) ? resultScroll : null;
        if (Contains(gridScroll.viewport, screenPos)) return gridScroll;
        if (Contains(detailScroll.viewport, screenPos)) return detailScroll;
        return null;
    }

    static bool Contains(RectTransform r, Vector2 screenPos) =>
        r != null && r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, screenPos, null);

    void HandleTap(Vector2 screenPos)
    {
        bool overlayOn = overlay != null && overlay.gameObject.activeSelf;
        for (int i = taps.Count - 1; i >= 0; i--)
        {
            Tap t = taps[i];
            if (t.overlay != overlayOn) continue;
            if (!Contains(t.rect, screenPos)) continue;
            // 一覧のカードは表示範囲(ビューポート)の中だけ有効
            if (!t.overlay && t.rect.IsChildOf(gridScroll.content) && !Contains(gridScroll.viewport, screenPos)) continue;
            t.action?.Invoke();
            return;
        }
    }

    // ===================================================================== //
    // テスト/撮影用
    // ===================================================================== //

    public void DebugSelect(string main, string material)
    {
        mainKey = main; materialKey = material; activeSlot = string.IsNullOrEmpty(material) ? 1 : 0;
        Refresh();
    }
    public void DebugFuse() => OnFuseTapped();
    public void DebugSkip() => skipRequested = true;
    public void DebugContinue() => OnContinue();
    public void DebugBackToList() => OnBackToList();
    public void DebugTapCell(string key) => OnCellTapped(key);
    public void DebugSetActiveSlot(int i) { activeSlot = i; Refresh(); }
    public string DebugDetailText => detailText != null ? detailText.text : "";
    public string DebugResultText => resultBody != null ? resultBody.text : "";
    public string DebugStatusText => statusText != null ? statusText.text : "";
    public int DebugVisibleRows()
    {
        // 一覧のビューポート内に完全に収まっている行数
        if (gridScroll == null || cells.Count == 0) return 0;
        var view = gridScroll.viewport;
        var corners = new Vector3[4]; view.GetWorldCorners(corners);
        float top = corners[1].y, bottom = corners[0].y;
        var rowTops = new HashSet<int>();
        foreach (var c in cells)
        {
            if (!c.root.gameObject.activeSelf) continue;
            var cc = new Vector3[4]; c.root.GetWorldCorners(cc);
            if (cc[1].y <= top + 0.5f && cc[0].y >= bottom - 0.5f) rowTops.Add(Mathf.RoundToInt(cc[1].y));
        }
        return rowTops.Count;
    }
}

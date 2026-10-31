using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 長押しの大きな詳細(2026-10-31)。所持カード / デッキのカード / キャラカードのどれでも。小さな詳細の「詳細」ボタンからも(パッド/キー)。
//  名前・種類・Lv・レア度・所持数 / 主な効果 / Lv1・現在・Lv9 の効果 / 説明 / COMBO / FINAL EVOLUTION / 使用状況 / 解放条件
//  下に「分解 (+N MILE)」(CardDismantle)。装備中で空きが無い時は押すと理由を出す(勝手に外さない)。
//  窓の外を押す / ×  / 戻る で閉じる。窓が開いている間、後ろの一覧は反応しない
public partial class DeckEditUI
{
    GameObject bigRoot;
    RectTransform bigPanel, bigCardHolder, bigCloseRt, bigDismantleRt, bigInfoRt;
    RewardCardUI bigCard;
    Text bigTitle, bigInfo, bigDismantleLabel;
    Image bigDismantleBg;
    string bigKey; int bigLevel;
    public bool BigDetailOpen => bigRoot != null && bigRoot.activeSelf;
    public bool OverlayOpen => BigDetailOpen || (confirmDialog != null && confirmDialog.IsOpen);
    public static int BigDetailOpened { get; private set; } // 確認用
    public string DebugBigInfo => bigInfo != null ? (bigTitle != null ? bigTitle.text : "") + "\n" + bigInfo.text : "";

    void BuildBigDetail()
    {
        if (bigRoot != null || root == null) return;
        var rootRt = (RectTransform)root.transform;
        Font font = detailName != null ? detailName.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bigRoot = new GameObject("BigDetail", typeof(RectTransform));
        var br = (RectTransform)bigRoot.transform;
        br.SetParent(rootRt, false);
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = br.offsetMax = Vector2.zero;
        var dim = bigRoot.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0.02f, 0.78f); dim.raycastTarget = true;

        bigPanel = NewUi("Panel", br, new Vector2(0.5f, 0.5f), new Vector2(1200f, 800f), Vector2.zero);
        bigPanel.pivot = new Vector2(0.5f, 0.5f);
        var pbg = bigPanel.gameObject.AddComponent<Image>(); pbg.color = new Color(0.05f, 0.06f, 0.13f, 0.98f); pbg.raycastTarget = false;
        var pol = bigPanel.gameObject.AddComponent<Outline>(); pol.effectColor = UiTheme.GoldEdge; pol.effectDistance = new Vector2(3f, -3f);

        bigCardHolder = NewUi("CardHolder", bigPanel, new Vector2(0f, 1f), new Vector2(196f, 294f), Vector2.zero);
        bigCardHolder.pivot = new Vector2(0.5f, 0.5f);
        if (detailPreviewCard != null)
        {
            var go = Instantiate(detailPreviewCard.gameObject, bigCardHolder, false);
            go.name = "BigCard";
            go.SetActive(true);
            bigCard = go.GetComponent<RewardCardUI>();
            var cr = bigCard.rect;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = Vector2.zero; cr.sizeDelta = detailPreviewCard.rect.sizeDelta;
            bigCard.SetInteractable(false);
        }

        var tr = NewUi("Title", bigPanel, new Vector2(0f, 1f), new Vector2(600f, 56f), Vector2.zero);
        bigTitle = tr.gameObject.AddComponent<Text>();
        bigTitle.font = font; UiTheme.Apply(bigTitle, UiTheme.TextRole.Title, 0.85f);
        bigTitle.color = UiTheme.GoldText; bigTitle.alignment = TextAnchor.MiddleLeft; bigTitle.raycastTarget = false;
        bigTitle.resizeTextForBestFit = true; bigTitle.resizeTextMinSize = 16; bigTitle.resizeTextMaxSize = 44;
        bigTitle.horizontalOverflow = HorizontalWrapMode.Wrap; bigTitle.supportRichText = true;

        bigInfoRt = NewUi("Info", bigPanel, new Vector2(0f, 1f), new Vector2(600f, 500f), Vector2.zero);
        bigInfo = bigInfoRt.gameObject.AddComponent<Text>();
        bigInfo.font = font; UiTheme.Apply(bigInfo, UiTheme.TextRole.Body, 1.0f);
        bigInfo.color = UiTheme.Text; bigInfo.alignment = TextAnchor.UpperLeft; bigInfo.raycastTarget = false; bigInfo.supportRichText = true;
        UguiScrollText.Wrap(bigInfo);

        bigCloseRt = NewUi("Close", bigPanel, new Vector2(1f, 1f), new Vector2(72f, 72f), Vector2.zero);
        bigCloseRt.pivot = new Vector2(1f, 1f); bigCloseRt.anchoredPosition = new Vector2(-12f, -12f);
        var cbg = bigCloseRt.gameObject.AddComponent<Image>(); cbg.color = new Color(0.12f, 0.14f, 0.28f, 0.95f); cbg.raycastTarget = false;
        var col = bigCloseRt.gameObject.AddComponent<Outline>(); col.effectColor = UiTheme.GoldEdge; col.effectDistance = new Vector2(2f, -2f);
        var ct = NewUi("X", bigCloseRt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ct.anchorMin = Vector2.zero; ct.anchorMax = Vector2.one; ct.offsetMin = ct.offsetMax = Vector2.zero;
        var ctx = ct.gameObject.AddComponent<Text>(); ctx.font = font; ctx.fontSize = 44; ctx.fontStyle = FontStyle.Bold; ctx.alignment = TextAnchor.MiddleCenter;
        ctx.color = UiIcons.ControlColor; ctx.text = "×"; ctx.raycastTarget = false;

        bigDismantleRt = NewUi("Dismantle", bigPanel, new Vector2(0f, 1f), new Vector2(360f, 70f), Vector2.zero);
        bigDismantleBg = bigDismantleRt.gameObject.AddComponent<Image>(); bigDismantleBg.raycastTarget = false;
        var dol = bigDismantleRt.gameObject.AddComponent<Outline>(); dol.effectColor = UiTheme.GoldEdge; dol.effectDistance = new Vector2(2f, -2f);
        var dt = NewUi("Label", bigDismantleRt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        dt.anchorMin = Vector2.zero; dt.anchorMax = Vector2.one; dt.offsetMin = new Vector2(10f, 4f); dt.offsetMax = new Vector2(-10f, -4f);
        bigDismantleLabel = dt.gameObject.AddComponent<Text>(); bigDismantleLabel.font = font;
        UiTheme.Apply(bigDismantleLabel, UiTheme.TextRole.Button, 1.1f);
        bigDismantleLabel.alignment = TextAnchor.MiddleCenter; bigDismantleLabel.raycastTarget = false;
        bigDismantleLabel.resizeTextForBestFit = true; bigDismantleLabel.resizeTextMinSize = 14; bigDismantleLabel.resizeTextMaxSize = 30;
        bigRoot.SetActive(false);
    }

    void LayoutBigDetail()
    {
        if (bigRoot == null) return;
        var rootRt = (RectTransform)root.transform;
        float W = rootRt.rect.width, H = rootRt.rect.height;
        bool portrait = H > W;
        float pw = portrait ? W - 40f : Mathf.Min(W - 120f, 1500f);
        float ph = portrait ? Mathf.Min(H - 140f, 1700f) : H - 90f;
        bigPanel.sizeDelta = new Vector2(pw, ph);
        UiTheme.Apply(bigInfo, UiTheme.TextRole.Body, portrait ? 1.3f : 1.05f); // 縦画面は文字を大きめに(幅が狭く、行は縦にスクロールできる)
        bigInfo.color = UiTheme.Text; bigInfo.alignment = TextAnchor.UpperLeft; bigInfo.supportRichText = true;
        bigPanel.anchoredPosition = Vector2.zero;
        float pad = 28f;
        float cardH = portrait ? Mathf.Min(ph * 0.3f, 460f) : Mathf.Min(ph - 2f * pad - 90f, 560f);
        float k = cardH / 294f;
        bigCardHolder.localScale = new Vector3(k, k, 1f);
        float cardW = 196f * k;
        const float btnH = 76f;
        if (portrait)
        {
            bigCardHolder.anchoredPosition = new Vector2(pw * 0.5f, -pad - cardH * 0.5f - 10f);
            PlaceIn(bigTitle.rectTransform, pad, -pad - cardH - 26f, pw - pad * 2f, 60f);
            float infoTop = -pad - cardH - 92f;
            PlaceIn(BigInfoViewport, pad, infoTop, pw - pad * 2f, ph + infoTop - btnH - pad - 16f);
            PlaceIn(bigDismantleRt, pad, -(ph - pad - btnH), pw - pad * 2f, btnH);
        }
        else
        {
            bigCardHolder.anchoredPosition = new Vector2(pad + cardW * 0.5f + 10f, -pad - cardH * 0.5f - 10f);
            float rx = pad + cardW + 44f, rw = pw - rx - pad;
            PlaceIn(bigTitle.rectTransform, rx, -pad, rw - 90f, 60f);
            PlaceIn(BigInfoViewport, rx, -pad - 70f, rw, ph - pad * 2f - 70f);
            PlaceIn(bigDismantleRt, pad, -(ph - pad - btnH), cardW + 20f, btnH);
        }
    }
    RectTransform BigInfoViewport => bigInfo != null && UguiScrollText.Wrap(bigInfo) is UguiScrollText s && s != null ? s.Viewport : bigInfoRt;

    static int LevelOfKey(string key)
    {
        if (CardVariant.IsVariantKey(key)) { var v = CardVariant.Parse(key); if (v != null) return Mathf.Max(1, v.level); }
        return 1;
    }

    public void OpenBigDetail(string key, int level)
    {
        var gm = GameManager.Instance;
        CardDefinition card = CardDatabase.FindById(key);
        if (gm == null || card == null) return;
        BuildBigDetail();
        if (bigRoot == null) return;
        if (level < 1) level = LevelOfKey(key);
        bigKey = key; bigLevel = level;
        BigDetailOpened++;
        LayoutBigDetail();
        int count = CardInventory.GetCount(key, level);
        if (bigCard != null) { bigCard.ShowFrontImmediate(gm.MakeOwnedCardData(card, level, count)); bigCard.SetInteractable(false); }
        string levelLabel = level >= CardInventory.MaxCardLevel ? $"Lv.{level} MAX" : $"Lv.{level}";
        bigTitle.text = Loc.Auto(card.cardName) + $"  <size=26><color=#ffd76a>{card.RarityStars}</color></size>";
        bigInfo.text = BuildBigInfo(card, key, level, levelLabel, count);
        RefreshBigDismantle();
        bigRoot.SetActive(true);
        bigRoot.transform.SetAsLastSibling();
        if (confirmDialog != null && confirmDialog.root != null) confirmDialog.root.transform.SetAsLastSibling();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.CardSelect);
        Debug.Log($"[DeckEdit] big detail {key} Lv{level} (owned {count}, free {CardDismantle.FreeCount(key, level)})");
    }

    public void CloseBigDetail()
    {
        if (bigRoot == null || !bigRoot.activeSelf) return;
        bigRoot.SetActive(false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
    }

    string BuildBigInfo(CardDefinition card, string key, int level, string levelLabel, int count)
    {
        var gm = GameManager.Instance;
        var sb = new StringBuilder();
        const string head = "<color=#ffd76a><b>", headEnd = "</b></color>";
        // 種類 / Lv / 所持
        sb.Append(card.category.ToString().ToUpperInvariant()).Append("  /  ").Append(levelLabel).Append("  /  ").Append(card.RarityStars).Append('\n');
        sb.Append(Loc.F(LocDeckEdit.OwnedLine, count, CardDismantle.FreeCount(key, level))).Append('\n');
        string mastery = MasteryDetailLine(card);
        if (!string.IsNullOrEmpty(mastery)) sb.Append(Loc.Auto(mastery));
        // 主な効果(合成カードは MAIN/SUB)
        sb.Append('\n').Append(Loc.Auto(BuildDetailValueText(card))).Append('\n');
        // Lv1 / 現在 / Lv9
        string mainId = CardMastery.BaseIdOf(key);
        int cur = level;
        CardVariant v = CardVariant.IsVariantKey(key) ? CardVariant.Parse(key) : null;
        if (v != null && v.abilities.Count > 0) cur = Mathf.Clamp(v.abilities[0].stacks, 1, CardVariant.MaxLevel);
        sb.Append('\n').Append(head).Append(Loc.T(LocDeckEdit.LvEffects)).Append(headEnd).Append('\n');
        foreach (int lv in new System.Collections.Generic.SortedSet<int> { 1, cur, CardVariant.MaxLevel })
        {
            bool isCur = lv == cur;
            string line = Loc.Auto(CardVariant.AbilityEffectTextAt(mainId, lv));
            sb.Append(isCur ? "<color=#8FE9D6>" : "").Append($"Lv{lv}").Append(isCur ? $"({Loc.T(LocDeckEdit.Current)})" : "").Append(": ").Append(line).Append(isCur ? "</color>" : "").Append('\n');
        }
        // 説明
        sb.Append('\n').Append(Loc.Auto(DetailDescription(card))).Append('\n');
        // COMBO
        var combos = ComboTuning.I != null ? ComboTuning.I.combos : null;
        var comboLines = new StringBuilder();
        if (combos != null)
            foreach (var c in combos)
            {
                if (c == null || !c.enabled || c.abilities == null || !c.abilities.Contains(mainId)) continue;
                var partners = new StringBuilder();
                foreach (var a in c.abilities) if (a != mainId) { if (partners.Length > 0) partners.Append(" + "); partners.Append(Loc.Auto(CardVariant.AbilityName(a))); }
                comboLines.Append("・").Append(c.displayName).Append("  (+ ").Append(partners).Append(")\n");
            }
        sb.Append('\n').Append(head).Append(Loc.T(LocDeckEdit.ComboHead)).Append(headEnd).Append('\n');
        sb.Append(comboLines.Length > 0 ? comboLines.ToString() : "・" + Loc.T(LocDeckEdit.NotEligible) + "\n");
        // FINAL EVOLUTION
        var fe = FinalEvolutionTuning.I != null ? FinalEvolutionTuning.I.For(mainId) : null;
        sb.Append('\n').Append(head).Append("FINAL EVOLUTION").Append(headEnd).Append('\n');
        if (fe != null) sb.Append("・").Append(Loc.Auto(fe.title)).Append(string.IsNullOrEmpty(fe.description) ? "" : "\n  " + Loc.Auto(fe.description)).Append('\n');
        else sb.Append("・").Append(Loc.T(LocDeckEdit.NotEligible)).Append('\n');
        // 使用状況
        sb.Append('\n').Append(head).Append(Loc.T(LocDeckEdit.StateHead)).Append(headEnd).Append('\n');
        bool any = false;
        if (gm != null)
        {
            bool inDeck = false; foreach (var d in gm.DeckCards) if (d == key) inDeck = true;
            if (inDeck) { sb.Append("・").Append(Loc.T(LocDeckEdit.StateInDeck)).Append('\n'); any = true; }
            if (gm.GetCharacterCardLockedCountForStack(key, level) > 0) { sb.Append("・").Append(Loc.T(LocDeckEdit.StateEquipped)).Append('\n'); any = true; }
            if (DeckPresets.InInactivePreset(key)) { sb.Append("・").Append(Loc.T(LocDeckEdit.StatePreset)).Append('\n'); any = true; }
        }
        if (!any) sb.Append("・").Append(Loc.T(LocDeckEdit.StateFree)).Append('\n');
        // 解放条件
        CardDefinition baseDef = CardDatabase.FindBaseById(mainId) ?? card;
        sb.Append('\n').Append(head).Append(Loc.T(LocDeckEdit.UnlockHead)).Append(headEnd).Append('\n');
        sb.Append("・").Append(Loc.F(LocDeckEdit.UnlockLine, Mathf.RoundToInt(baseDef.unlockDistance).ToString("N0"), Mathf.Max(1, baseDef.gachaStage))).Append('\n');
        return sb.ToString();
    }

    void RefreshBigDismantle()
    {
        if (bigDismantleLabel == null) return;
        int mile = CardDismantle.RefundFor(bigKey, bigLevel);
        var block = CardDismantle.Check(bigKey, bigLevel);
        bool owned = block != CardDismantle.Block.NotOwned;
        bigDismantleRt.gameObject.SetActive(owned);
        bigDismantleLabel.text = Loc.T(LocDeckEdit.Dismantle) + $"  <color=#ffe08a>+{mile} MILE</color>";
        bigDismantleLabel.supportRichText = true;
        bool ok = block == CardDismantle.Block.None;
        bigDismantleBg.color = ok ? new Color(0.45f, 0.16f, 0.12f, 0.95f) : new Color(0.22f, 0.22f, 0.26f, 0.9f);
        bigDismantleLabel.color = ok ? UiTheme.TextButton : UiTheme.Disabled;
    }

    // 大きな詳細の中のタップ(窓が開いている間は、ここだけが反応する)
    void HandleBigTap(Vector2 p)
    {
        if (bigCloseRt != null && UiHit.Hit(bigCloseRt, p)) { CloseBigDetail(); return; }
        if (bigDismantleRt != null && bigDismantleRt.gameObject.activeInHierarchy && UiHit.Hit(bigDismantleRt, p)) { OnDismantleTapped(); return; }
        if (UiHit.Probing) return;
        if (UguiScrollText.PointerCaptured) return;
        if (bigPanel != null && !RectTransformUtility.RectangleContainsScreenPoint(bigPanel, p, null)) CloseBigDetail(); // 窓の外
    }

    void OnDismantleTapped()
    {
        string key = bigKey; int level = bigLevel;
        var block = CardDismantle.Check(key, level);
        if (block == CardDismantle.Block.Equipped)
        {
            ShowToast(Loc.T(LocDeckEdit.DismantleEquipped), 3.2f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
            Debug.Log($"[Dismantle] {key} Lv{level}: equipped (free 0) - not dismantled, nothing removed from the deck");
            return;
        }
        if (block != CardDismantle.Block.None) return;
        int mile = CardDismantle.RefundFor(key, level);
        int rarity = CardDismantle.RarityOf(key);
        bool last = CardDismantle.IsLastOfName(key);
        bool rare = rarity >= 4 || level >= 5;
        var sb = new StringBuilder();
        CardDefinition card = CardDatabase.FindById(key);
        sb.Append("<b>").Append(Loc.Auto(card != null ? card.cardName : key)).Append(level > 1 ? $" Lv.{level}" : "").Append("</b>\n");
        sb.Append(Loc.F(LocDeckEdit.DismantleConfirm, mile));
        if (rare) sb.Append("\n<color=#ff8a70>").Append(Loc.F(LocDeckEdit.DismantleRare, rarity, level)).Append("</color>");
        if (last) sb.Append("\n<color=#ff6a55><b>").Append(Loc.T(LocDeckEdit.DismantleLast)).Append("</b></color>");
        sb.Append("\n<size=20>").Append(Loc.T(LocDeckEdit.DismantleIrreversible)).Append("</size>");
        if (confirmDialog == null) { DoDismantle(key, level); return; }
        confirmDialog.Show(sb.ToString(), () => DoDismantle(key, level), null, danger: last || rare);
    }

    public static int DismantleDoneCount { get; private set; } // 確認用
    void DoDismantle(string key, int level)
    {
        if (!CardDismantle.Execute(key, level, 1, out int mile))
        {
            if (CardDismantle.Check(key, level) == CardDismantle.Block.Equipped) ShowToast(Loc.T(LocDeckEdit.DismantleEquipped), 3.2f);
            RefreshBigDismantle();
            return;
        }
        DismantleDoneCount++;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        StartCoroutine(PlayConvertGlow());
        if (detailCardId == key) ResetDetail();
        Refresh();
        if (CardInventory.GetCount(key, level) > 0) OpenBigDetail(key, level); // 残りがあれば数を書き直す
        else CloseBigDetail();
        ShowToast(Loc.F(LocDeckEdit.DismantleDone, mile));
    }

    // 自動試験用
    public void DebugOpenBigDetailOwned(string key) { foreach (var s in CardInventory.Stacks) if (s.cardId == key) { OpenBigDetail(s.cardId, s.level); return; } }
    public void DebugDismantleTap() => OnDismantleTapped();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugConfirmYes() { if (confirmDialog == null || !confirmDialog.IsOpen) return false; confirmDialog.DebugYes(); return true; }
#endif
}

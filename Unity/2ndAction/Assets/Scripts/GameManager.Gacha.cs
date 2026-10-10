using System.Collections.Generic;
using UnityEngine;

// ガチャの連続操作(2026-10-07)。
//  ・本体を1回タップ = その場で「支払い1回 + カード1枚の獲得」を同期で行う(演出を待たない)。演出中/確認中のタップも1回ずつ受け付け、
//    引いたカードは順番待ち(gachaRevealQueue)に並べて1枚ずつ見せる。支払いと獲得は同じフレームで1回だけなので、連打しても
//    二重消費/無料取得/マイナスにならない(所持金が足りない回はその場で断って「MILEが足りません」)。
//  ・確認を閉じても、画面を移っても、獲得済みのカードは所持に入っている(順番待ちは見せるだけ)。
//  ・確認: 大きく(カード画像/名前/★/効果説明)。本体はいつも見えて押せる位置(確認の板は本体の左側)。
//    本体をタップ = 確認を閉じて次の1回を引く。それ以外の場所 = 閉じるだけ(背後のボタンへは通さない)。
//    確認を出したタップでは閉じない(確認が開いた後に新しく押した指から閉じる)。パッド/キーは「閉じる」と本体を選べる。
public partial class GameManager
{
    readonly List<CardDefinition> gachaRevealQueue = new List<CardDefinition>();
    float gachaResultOpenedAt;
    bool gachaCloseArmed;
    Rect lastGachaMachineRect;
    const float GachaNextRevealDelay = 0.22f;

    // 確認用
    public int GachaPulls { get; private set; }
    public int GachaRefusedNoMile { get; private set; }
    public int GachaPendingReveals => gachaRevealQueue.Count;
    public bool GachaResultOpen => gachaResultOpen;
    public string GachaResultCardId => gachaResultCard != null ? gachaResultCard.cardId : "";

    // 1回ぶん: 支払いと獲得を同時に(どちらかだけ起きることは無い)
    bool GachaPullOnce()
    {
        if (TotalOwnedMile < GachaCostMile)
        {
            gachaInsufficientMessageTimer = gachaInsufficientMessageDuration;
            GachaRefusedNoMile++;
            Debug.Log($"[Gacha] refused: MILE {TotalOwnedMile} < {GachaCostMile} (frame {Time.frameCount}, touches {Input.touchCount})"); // 断った回を記録(2026-10-10)
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
            return false;
        }
        List<CardDefinition> pool = BuildGachaPool();
        if (pool.Count == 0) return false;
        CardDefinition drawn = DrawFromGachaPool(pool);
        if (drawn == null) return false;
        if (!TrySpendMile(GachaCostMile)) { gachaInsufficientMessageTimer = gachaInsufficientMessageDuration; GachaRefusedNoMile++; return false; }
        CardInventory.AddCard(drawn.cardId, 1, 1);
        GachaPulls++;
        gachaRevealQueue.Add(drawn);
        if (!gachaResultOpen && gachaMachineShakeTimer <= 0f) gachaMachineShakeTimer = gachaMachineShakeDuration;
        deskHotspotFlashTimer = roomHotspotFlashDuration;
        if (AudioManager.Instance != null) { AudioManager.Instance.PlaySe(SeId.Gacha); AudioManager.Instance.PlaySe(SeId.Coin); }
        return true;
    }

    void OnGachaMachineTapped()
    {
        if (gachaResultOpen) CloseGachaResult(); // 確認中に本体: 閉じて次の1回
        GachaPullOnce();
    }

    // ランへ出る時: 見せる分だけ捨てる(カードは所持済み)
    void ClearGachaPresentation() { gachaRevealQueue.Clear(); gachaResultOpen = false; gachaCloseArmed = false; gachaMachineShakeTimer = 0f; }

    void CloseGachaResult()
    {
        if (!gachaResultOpen) return;
        gachaResultOpen = false;
        gachaCloseArmed = false;
        if (gachaRevealQueue.Count > 0 && gachaMachineShakeTimer <= 0f) gachaMachineShakeTimer = GachaNextRevealDelay; // 次の1枚を順番に
    }

    // UpdateHomeRoom から: 揺れが終わったら順番待ちの先頭を見せる
    void UpdateGachaReveal()
    {
        if (gachaMachineShakeTimer > 0f) gachaMachineShakeTimer -= Time.unscaledDeltaTime;
        if (!gachaResultOpen && gachaMachineShakeTimer <= 0f && gachaRevealQueue.Count > 0)
        {
            var c = gachaRevealQueue[0];
            gachaRevealQueue.RemoveAt(0);
            gachaResultOpen = true;
            gachaResultCard = c;
            gachaResultOwnedCount = CardInventory.GetTotalCount(c.cardId);
            gachaResultRevealTimer = c.rarity >= 4 ? GachaResultRevealDuration : 0f;
            gachaResultOpenedAt = Time.unscaledTime;
            gachaCloseArmed = false;
        }
        if (gachaResultRevealTimer > 0f) gachaResultRevealTimer -= Time.unscaledDeltaTime;
    }

    // OnGUI の最初(ホームのボタンより前)。確認中は本体以外への押下/離しを全部ここで吸い取り、離した時に閉じる。
    // 2026-10-08: 説明が長い時は説明の所を指でドラッグしてスクロールできる。ドラッグした指は離しても閉じない(タップで閉じる と競合させない)
    readonly UiScroll gachaDescScroll = new UiScroll();
    Rect lastGachaDescRect; float gachaDescMax; Vector2 gachaPressPos; bool gachaDragging, gachaPressInDesc; float gachaLastY;
    public bool GachaDescDragging => gachaDragging; // テスト用
    void GachaPopupInput()
    {
        if (!gachaResultOpen || HasStarted) return;
        var e = Event.current;
        if (e == null || (e.type != EventType.MouseDown && e.type != EventType.MouseUp && e.type != EventType.MouseDrag)) return;
        if (e.type == EventType.MouseDrag)
        {
            if (gachaPressInDesc && gachaDescMax > 0.5f && !gachaDragging && Mathf.Abs(e.mousePosition.y - gachaPressPos.y) > UiScroll.DragThreshold) gachaDragging = true;
            if (gachaDragging)
            {
                gachaDescScroll.y = Mathf.Clamp(gachaDescScroll.y - (e.mousePosition.y - gachaLastY), 0f, gachaDescMax);
                gachaLastY = e.mousePosition.y;
                e.Use();
            }
            return;
        }
        if (e.type == EventType.MouseDown) { gachaPressPos = e.mousePosition; gachaLastY = e.mousePosition.y; gachaDragging = false; gachaPressInDesc = lastGachaDescRect.Contains(e.mousePosition); }
        if (e.type == EventType.MouseUp && gachaDragging) { gachaDragging = false; gachaCloseArmed = false; e.Use(); return; } // スクロールした指: 閉じない
        if (lastGachaMachineRect.width > 0f && lastGachaMachineRect.Contains(e.mousePosition)) return; // 本体のボタンが受け取る
        if (e.type == EventType.MouseDown)
        {
            if (Time.unscaledTime - gachaResultOpenedAt > 0.12f) gachaCloseArmed = true; // 確認を出した指(開いた瞬間に押していた)では閉じない
        }
        else if (gachaCloseArmed) CloseGachaResult();
        e.Use();
    }

    float GachaUiScale => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f);

    void DrawGachaResultPopup()
    {
        if (!gachaResultOpen || gachaResultCard == null) return;
        var card = gachaResultCard;
        float s = GachaUiScale;
        bool highRarity = card.rarity >= 4;
        float revealT = GachaResultRevealDuration > 0f ? Mathf.Clamp01(gachaResultRevealTimer / GachaResultRevealDuration) : 0f;

        UiKit.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));

        // 板は本体の左側(本体を隠さない)。入らなければ画面の中央
        float margin = 16f * s;
        float availRight = Mathf.Min(Screen.width - margin, lastGachaMachineRect.width > 0f ? lastGachaMachineRect.xMin - margin : Screen.width - margin); // 本体が画面外でも窓は画面内(2026-10-08)
        float pw = Mathf.Min(availRight - margin, 760f * s);
        Rect panel;
        float ph = Mathf.Min(Screen.height - margin * 2f, 640f * s);
        if (pw >= 380f * s) panel = new Rect(margin + (availRight - margin - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        else { pw = Mathf.Min(Screen.width - margin * 2f, 760f * s); panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph); }

        if (highRarity && revealT > 0f)
        {
            Color glow = card.rarity >= 5 ? new Color(1f, 0.65f, 0.2f) : new Color(0.6f, 0.75f, 1f);
            float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(revealT * Mathf.PI * 5f));
            float pad = 26f * s * revealT;
            UiKit.Fill(new Rect(panel.x - pad, panel.y - pad, panel.width + pad * 2f, panel.height + pad * 2f), new Color(glow.r, glow.g, glow.b, pulse * revealT));
        }
        OrnateUi.DrawPanel(panel, 0.94f);

        bool isNew = gachaResultOwnedCount <= 1;
        float y = panel.y + 18f * s;
        LocGUI.Label(new Rect(panel.x, y, panel.width, 36f * s), isNew ? "NEW CARD" : "DUPLICATE", UiKit.Label(26f * s, TextAnchor.MiddleCenter, true, isNew ? new Color(1f, 0.85f, 0.4f) : new Color(0.7f, 0.85f, 1f)));
        y += 40f * s;

        // カード(絵 + レア度の枠)
        float icon = Mathf.Min(panel.height * 0.36f, panel.width * 0.42f) * (highRarity ? 1f + 0.08f * revealT : 1f);
        var iconRect = new Rect(panel.center.x - icon * 0.5f, y + icon * 0.2f, icon, icon);
        Sprite frame = CardRarityFrames.GetFrame(card.rarity, null);
        if (frame != null) { float fp = icon * 0.22f; GUI.DrawTexture(new Rect(iconRect.x - fp, iconRect.y - fp, iconRect.width + fp * 2f, iconRect.height + fp * 2f), frame.texture, ScaleMode.ScaleToFit); }
        if (card.icon != null) GUI.DrawTexture(iconRect, card.icon, ScaleMode.ScaleToFit);
        y = iconRect.yMax + icon * 0.24f;

        LocGUI.Label(new Rect(panel.x, y, panel.width, 40f * s), card.cardName, UiKit.Label(30f * s, TextAnchor.MiddleCenter, true, Color.white));
        y += 38f * s;
        Color starC = card.rarity >= 5 ? new Color(1f, 0.65f, 0.2f) : card.rarity >= 4 ? new Color(0.65f, 0.8f, 1f) : new Color(1f, 0.85f, 0.4f);
        LocGUI.Label(new Rect(panel.x, y, panel.width, 28f * s), card.RarityStars, UiKit.Label(22f * s, TextAnchor.MiddleCenter, true, starC));
        y += 30f * s;
        // 効果の説明(折り返し)
        var desc = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(20f * s) };
        desc.normal.textColor = new Color(0.92f, 0.94f, 1f);
        string text = string.IsNullOrEmpty(card.description) ? "" : card.description;
        // 2026-10-08: 高さは訳した文で測る。収まらなければ縮めずにスクロール(右にバー)
        float full = desc.CalcHeight(new GUIContent(Loc.Auto(text)), panel.width - 56f * s - 12f);
        float dh = Mathf.Min(full, 90f * s);
        var descRect = new Rect(panel.x + 28f * s, y, panel.width - 56f * s, dh);
        lastGachaDescRect = descRect; gachaDescMax = Mathf.Max(0f, full - dh);
        gachaDescScroll.Text(descRect, text, desc, card.cardId + "@" + gachaResultOpenedAt.ToString("F3"));
        y += dh + 6f * s;

        string masteryHint = CardMastery.IsAwakened(card.cardId) ? "  (AWAKENED済み・保管)" : CardMastery.IsMaxReached(card.cardId) ? "  → 合成で MASTERY +1" : "";
        LocGUI.Label(new Rect(panel.x, y, panel.width, 26f * s), $"Lv.1 +1   OWNED x{gachaResultOwnedCount}{masteryHint}", UiKit.Label(18f * s, TextAnchor.MiddleCenter, false, new Color(1f, 1f, 1f, 0.85f)));
        y += 26f * s;
        int st = CurrentGachaStage;
        float next = GachaStage.NextEvolutionDistance(st);
        LocGUI.Label(new Rect(panel.x, y, panel.width, 22f * s), next > 0f ? $"CARD GACHA Lv.{st}   NEXT EVOLUTION {Mathf.FloorToInt(next)}m" : $"CARD GACHA Lv.{st}   MAX EVOLUTION",
            UiKit.Label(14f * s, TextAnchor.MiddleCenter, false, new Color(0.75f, 0.85f, 1f, 0.85f)));

        // 下: 案内 + (パッド/キー用)閉じる
        string hint = gachaRevealQueue.Count > 0 ? $"タップで次へ(あと {gachaRevealQueue.Count} 枚)" : "タップで閉じる / ガチャをタップで続けて引く";
        LocGUI.Label(new Rect(panel.x, panel.yMax - 74f * s, panel.width, 24f * s), hint, UiKit.Label(16f * s, TextAnchor.MiddleCenter, false, new Color(1f, 0.9f, 0.6f, 0.9f)));
        var okRect = new Rect(panel.center.x - 80f * s, panel.yMax - 48f * s, 160f * s, 38f * s);
        if (DrawStyledButton(okRect, gachaRevealQueue.Count > 0 ? "NEXT" : "OK", 18f * s, primary: true, ornate: true)) CloseGachaResult();

        // 本体は確認の上にも見せて、押せることを示す(パッド/キーでも選べる)
        if (gachaMachineTexture != null && lastGachaMachineRect.width > 0f)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            Color keep = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.8f, 1f);
            GUI.DrawTexture(lastGachaMachineRect, gachaMachineTexture, ScaleMode.ScaleToFit);
            GUI.color = keep;
            float lw = Mathf.Max(lastGachaMachineRect.width + 60f * s, 170f * s);
            float lx = Mathf.Clamp(lastGachaMachineRect.center.x - lw * 0.5f, 4f, Screen.width - lw - 4f); // 画面の端で切れない(縦画面、2026-10-08)
            LocGUI.Label(new Rect(lx, lastGachaMachineRect.yMax + 2f * s, lw, 24f * s),
                TotalOwnedMile >= GachaCostMile ? $"TAP +1 ({GachaCostMile} MILE)" : "MILE不足", UiKit.Label(15f * s, TextAnchor.MiddleCenter, true, new Color(1f, 0.85f, 0.4f, pulse)));
            if (PadNav.Button(lastGachaMachineRect)) OnGachaMachineTapped();
        }
    }
}

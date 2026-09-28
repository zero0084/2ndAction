using System.Collections.Generic;
using UnityEngine;

// RUN BUILD HUD(2026-09-29) - ラン中に「今どのカードを何Lvで持っているか」を常時見せる小さなHUD。
//
// ・表示するだけのView: カードの状態はGameManager(Character Card + upgradeHistory)が正で、ここでは
//   GameManager.CollectRunCardIds / GetRunCardLevel を読むだけ(カードのロジック/保存には触らない)。
//   Slotの並び・Lvは毎回そこから作り直せる。ここで持つのは追加/Lv上昇の演出の時刻だけ。
// ・既存HUDと同じIMGUI(Screen Space)。HPパネルの真下(右上の端寄せ)に、端から中央へ向かって並べ、
//   1行maxColumns個で折り返す(カードが増えても中央へ伸び続けない)。一度置いたSlotの位置は変えない。
// ・マルチ: GameManagerのカード状態は端末ごと(同期しない)なので、表示されるのは常にその端末のPlayer自身のカード。
// ・Level Up/Boss Rewardの3択中も、縮小+半透明で表示を続ける(選んだ結果は通常走行へ戻った時に反映+演出)。
//
// 将来(Combo/最終進化): Slot.flags と DrawSlotOverlay() にマーカー/枠の状態を足せるようにしてある(今回は未使用)。
public class RunBuildHud : MonoBehaviour
{
    public static RunBuildHud Instance { get; private set; }

    [System.Flags]
    public enum SlotFlags { None = 0 } // 将来: ComboReady / ComboActive / EvolveReady / Evolved など

    public class Slot
    {
        public string cardId;
        public CardDefinition card;
        public int level;
        public float addedAt = -100f;    // 新規追加の演出の開始(unscaled time)
        public float levelUpAt = -100f;  // Lv数字の演出の開始
        public SlotFlags flags;          // 将来のCombo/Evolution状態(今回は常にNone)
    }

    [Header("Layout")]
    public int maxColumns = 5;                 // 1行の最大個数(これを超えたら次の行へ)
    public float slotHeightFraction = 0.052f;  // Slotの大きさ = 画面の高さ × これ(px)
    public float minSlot = 40f, maxSlot = 62f;
    public float gapFraction = 0.12f;          // Slot間の隙間(Slotの大きさに対する比)
    public float maxBottomFraction = 0.55f;    // これより下へは伸ばさない(行が増えたらSlotを小さくする)
    [Header("Level Up / Boss Reward の3択中")]
    public float choiceScale = 0.82f;
    public float choiceAlpha = 0.8f;
    [Header("Feedback")]
    public float addPopSeconds = 0.42f;
    public float addPopScale = 1.35f;
    public float levelPopSeconds = 0.35f;
    public float levelPopScale = 1.7f;

    readonly List<Slot> slots = new List<Slot>();
    readonly List<string> ids = new List<string>();
    bool synced;

    public IReadOnlyList<Slot> Slots => slots;
    // テスト/確認用: 直前に描いた状態
    public bool IsVisible { get; private set; }
    public bool IsCompact { get; private set; }
    public Rect LastGridRect { get; private set; }
    public float LastSlotSize { get; private set; }
    public Rect SlotRect(int index) => index >= 0 && index < slotRects.Count ? slotRects[index] : default;
    readonly List<Rect> slotRects = new List<Rect>();

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    static bool ChoiceOpen(GameManager gm) => gm.IsLocalChoiceOpen || gm.IsRewardSequenceRunning;

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (!gm.HasStarted) { if (slots.Count > 0) slots.Clear(); synced = false; return; }
        // 3択の最中は開いた時の表示のまま(取ったカードは通常走行へ戻った時に追加/Lv更新の演出で見せる)
        if (ChoiceOpen(gm)) return;
        Sync(gm);
    }

    void Sync(GameManager gm)
    {
        gm.CollectRunCardIds(ids);
        float now = Time.unscaledTime;
        // 取得済みのカードが減ることは無い(Continue等で作り直された時だけ)。並びが食い違えば作り直す。
        bool mismatch = ids.Count < slots.Count;
        for (int i = 0; i < slots.Count && !mismatch; i++) if (slots[i].cardId != ids[i]) mismatch = true;
        if (mismatch) { slots.Clear(); synced = false; }
        for (int i = 0; i < ids.Count; i++)
        {
            int lv = Mathf.Max(1, gm.GetRunCardLevel(ids[i]));
            if (i < slots.Count)
            {
                var s = slots[i];
                if (lv != s.level) { if (lv > s.level && synced) s.levelUpAt = now; s.level = lv; }
                continue;
            }
            var card = CardDatabase.FindById(ids[i]);
            slots.Add(new Slot { cardId = ids[i], card = card, level = lv, addedAt = synced ? now : -100f });
        }
        synced = true; // Run開始時点(Character Card)/Continueの復元分は演出なしで並べる
    }

    // ===================================================================== //
    // 描画
    // ===================================================================== //
    static Texture2D roundTex;
    static GUIStyle roundStyle, numStyle;

    static void EnsureStyles()
    {
        if (roundTex == null)
        {
            // 角丸の白い板(9-sliceで伸ばす)。色はGUI.backgroundColorで付ける。
            const int n = 24; const float r = 7f;
            roundTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    roundTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            roundTex.Apply();
            roundStyle = null;
        }
        if (roundStyle == null)
        {
            roundStyle = new GUIStyle { border = new RectOffset(8, 8, 8, 8) };
            roundStyle.normal.background = roundTex;
        }
        if (numStyle == null)
        {
            numStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, clipping = TextClipping.Overflow, wordWrap = false };
            numStyle.padding = new RectOffset(0, 0, 0, 0);
        }
    }

    static void Round(Rect r, Color c)
    {
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = c;
        if (Event.current.type == EventType.Repaint) roundStyle.Draw(r, false, false, false, false);
        GUI.backgroundColor = prev;
    }

    static readonly Color SlotFill = new Color(0.04f, 0.06f, 0.14f, 0.72f);   // Dark Navy(半透明)
    static readonly Color SlotShadow = new Color(0f, 0f, 0f, 0.35f);
    static readonly Color SlotEdge = new Color(0.62f, 0.72f, 0.9f, 0.55f);    // 薄い青灰の縁(派手な金枠は使わない)
    static readonly Color GlowColor = new Color(0.55f, 0.85f, 1f);
    static readonly Color LevelUpColor = new Color(1f, 0.86f, 0.35f);

    // 表示するか / 3択中の縮小表示か(Home・Result・他の画面を開いている間・カード0枚は出さない)
    public bool ShouldShow(out bool compact)
    {
        compact = false;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver || gm.IsOverlayOpen || slots.Count == 0) return false;
        compact = ChoiceOpen(gm);
        return true;
    }

    // 配置(画面座標、IMGUIと同じ左上原点)。Slotの位置はindexだけで決まる(Lvが上がっても動かない)。
    // avoid: 3択中に避ける範囲(カード/説明パネル)。横はその右側の空きに収まる列数へ、縦はその上端までに収める。
    public void ComputeLayout(int n, bool compact, List<Rect> into, out Rect grid, out float size, List<Rect> avoid = null)
    {
        into.Clear();
        Rect safe = Screen.safeArea;
        float safeTop = Screen.height - (safe.y + safe.height);
        float safeRight = Screen.width - (safe.x + safe.width);
        const float margin = 28f, hudPanel = 54f, gapBelowHp = 10f; // GameManagerのUiMargin/HudPanelHeight(HPパネルの真下)
        float right = Screen.width - safeRight - margin;
        float top = safeTop + margin + hudPanel + gapBelowHp;
        float s = Mathf.Clamp(Screen.height * slotHeightFraction, minSlot, maxSlot);
        if (compact) s *= choiceScale;
        float bottom = Screen.height * maxBottomFraction;
        int perRow = maxColumns;
        if (avoid != null && avoid.Count > 0)
        {
            // 3択カードの右端より右の空き(足りなければSlotを小さくして1列)
            float leftLimit = float.NegativeInfinity;
            foreach (var r in avoid) if (r.yMax > top && r.yMin < bottom) leftLimit = Mathf.Max(leftLimit, r.xMax);
            if (!float.IsNegativeInfinity(leftLimit))
            {
                float avail = right - leftLimit - 12f;
                int fit = Mathf.FloorToInt((avail + s * gapFraction) / (s * (1f + gapFraction)));
                if (fit < 1) { s = Mathf.Max(20f, avail); fit = 1; }
                perRow = Mathf.Clamp(fit, 1, maxColumns);
            }
            foreach (var r in avoid) if (r.xMax > right - perRow * s * (1f + gapFraction) && r.yMin > top) bottom = Mathf.Min(bottom, r.yMin - 8f);
        }
        int cols = Mathf.Min(perRow, n);
        int rows = Mathf.CeilToInt(n / (float)perRow);
        float gap = s * gapFraction;
        float maxH = bottom - top;
        if (rows > 0 && rows * (s + gap) - gap > maxH) { s = Mathf.Max(20f, (maxH + gap) / rows - gap); gap = s * gapFraction; } // 行が多すぎる時だけ小さくする
        s = Mathf.Round(s);
        size = s;
        grid = new Rect(right - cols * s - Mathf.Max(0, cols - 1) * gap, top, cols * s + Mathf.Max(0, cols - 1) * gap, rows * s + Mathf.Max(0, rows - 1) * gap);
        for (int i = 0; i < n; i++)
        {
            // 端(右)から中央へ向かって並べ、perRowごとに次の行へ
            int col = i % perRow, row = i / perRow;
            into.Add(new Rect(right - (col + 1) * s - col * gap, top + row * (s + gap), s, s));
        }
    }

    // 3択(uGUI)のカード/説明パネルの画面上の範囲。開いている間だけ、少し間隔をあけて取り直す。
    readonly List<Rect> choiceRects = new List<Rect>();
    RewardCardSequence seq;
    float choiceScanAt;
    public List<Rect> ChoiceAvoidRects()
    {
        if (Time.unscaledTime < choiceScanAt) return choiceRects;
        choiceScanAt = Time.unscaledTime + 0.25f;
        choiceRects.Clear();
        if (seq == null) seq = FindFirstObjectByType<RewardCardSequence>(FindObjectsInactive.Include);
        if (seq == null) return choiceRects;
        if (seq.cards != null) foreach (var ui in seq.cards) if (ui != null && ui.isActiveAndEnabled) choiceRects.Add(ScreenRect(ui.GetComponent<RectTransform>()));
        if (seq.detailPanelGroup != null && seq.detailPanelGroup.gameObject.activeInHierarchy) choiceRects.Add(ScreenRect(seq.detailPanelGroup.GetComponent<RectTransform>()));
        return choiceRects;
    }

    // Overlay CanvasのRectTransform → IMGUI座標(左上原点)
    public static Rect ScreenRect(RectTransform rt)
    {
        if (rt == null) return default;
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        float x0 = Mathf.Min(c[0].x, c[2].x), x1 = Mathf.Max(c[0].x, c[2].x), y0 = Mathf.Min(c[0].y, c[2].y), y1 = Mathf.Max(c[0].y, c[2].y);
        return new Rect(x0, Screen.height - y1, x1 - x0, y1 - y0);
    }

    void OnGUI()
    {
        IsVisible = ShouldShow(out bool compact);
        IsCompact = compact;
        if (!IsVisible) return;
        EnsureStyles();
        ComputeLayout(slots.Count, compact, slotRects, out Rect grid, out float size, compact ? ChoiceAvoidRects() : null);
        LastGridRect = grid; LastSlotSize = size;

        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, compact ? choiceAlpha : 1f);
        float now = Time.unscaledTime;
        for (int i = 0; i < slots.Count; i++) DrawSlot(slots[i], slotRects[i], now);
        GUI.color = prevColor;
    }

    void DrawSlot(Slot slot, Rect r, float now)
    {
        // 追加の演出: 少し大きく出て元へ戻る+淡い光
        float ta = (now - slot.addedAt) / Mathf.Max(0.05f, addPopSeconds);
        float pop = 0f;
        if (ta >= 0f && ta < 1f) pop = 1f - EaseOut(ta);
        Rect body = pop > 0f ? Scale(r, 1f + (addPopScale - 1f) * pop) : r;
        // (透明度はGUI.color(3択中の半透明)がIMGUI側で全体に掛かる)
        if (pop > 0f)
        {
            Color g = GlowColor; g.a = 0.55f * pop;
            Round(Scale(body, 1.18f), g);
        }
        Round(new Rect(body.x + 1.5f, body.y + 2f, body.width, body.height), SlotShadow);
        Round(body, SlotEdge);
        float e = Mathf.Max(1f, body.width * 0.035f);
        Round(new Rect(body.x + e, body.y + e, body.width - 2f * e, body.height - 2f * e), SlotFill);

        // メインアイコン(カードUIと同じ絵)。縦長の絵は透明な上下の余白ぶん少し拡大してSlot内に切り抜く。
        Texture2D icon = slot.card != null ? slot.card.icon : null;
        Rect inner = new Rect(body.x + body.width * 0.07f, body.y + body.height * 0.07f, body.width * 0.86f, body.height * 0.86f);
        if (icon != null)
        {
            float aspect = icon.width / (float)Mathf.Max(1, icon.height);
            float h = inner.height * (aspect < 0.85f ? 1.14f : 1f);
            float w = h * aspect;
            if (w > inner.width) { w = inner.width; h = w / aspect; }
            GUI.BeginGroup(inner);
            GUI.DrawTexture(new Rect((inner.width - w) * 0.5f, (inner.height - h) * 0.5f, w, h), icon, ScaleMode.ScaleToFit);
            GUI.EndGroup();
        }

        DrawSlotOverlay(slot, body);

        // 右下の数字 = 現在Lv(RUN BUILDの統一ルール)。Lv上昇時だけ数字が大きくなって戻る。
        float tl = (now - slot.levelUpAt) / Mathf.Max(0.05f, levelPopSeconds);
        float lp = tl >= 0f && tl < 1f ? 1f - EaseOut(tl) : 0f;
        int fs = Mathf.Max(11, Mathf.RoundToInt(body.height * 0.4f * (1f + (levelPopScale - 1f) * lp)));
        numStyle.fontSize = fs;
        string txt = slot.level.ToString();
        var content = new GUIContent(txt);
        Vector2 sz = numStyle.CalcSize(content);
        var nr = new Rect(body.xMax - sz.x - body.width * 0.06f, body.yMax - sz.y + fs * 0.12f, sz.x, sz.y);
        // 背景の明るさに負けないよう、黒い縁取り(8方向)を付ける
        numStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0) GUI.Label(new Rect(nr.x + dx * 1.5f, nr.y + dy * 1.5f, nr.width, nr.height), content, numStyle);
        Color nc = Color.Lerp(Color.white, LevelUpColor, lp > 0f ? 1f : 0f);
        numStyle.normal.textColor = nc;
        GUI.Label(nr, content, numStyle);
    }

    // 将来のCombo/Evolutionのマーカー・枠の状態はここに描く(今回は何も描かない)。
    void DrawSlotOverlay(Slot slot, Rect body) { }

    static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
    static Rect Scale(Rect r, float k) { float w = r.width * k, h = r.height * k; return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h); }
}

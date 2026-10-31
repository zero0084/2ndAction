using UnityEngine;

// デッキ編集画面のプリセットの帯と一覧(2026-10-31)。IMGUI(設定画面と同じ見た目・パッド/キーボード/タッチ)。
//  ・帯: デッキ欄の見出し(Header_DECK)の位置に「◀  1  荒野疾走  12/12  ▶」。左右で切り替え、名前を押すと一覧。
//  ・一覧: 8 行(番号・名前・枚数・使用中の印)。選んだ行に「使う」「名前の変更」「コピー」「初期化」。
//    コピーは「コピー先を選ぶ → 上書きの確認」、初期化は確認つき。名前はその行で入力(最大 16 文字)。
//  ・一覧を開いている間はデッキ編集の操作を止める(UiInputGate.ModalOpen)。閉じた時の指は離れるまで後ろへ通さない。
public class DeckPresetUi : MonoBehaviour
{
    public static bool Open { get; private set; }
    DeckEditUI deck;
    enum Mode { List, Rename, PickCopyTarget, ConfirmCopy, ConfirmClear }
    Mode mode;
    int selected, copyFrom, copyTo;
    string nameEdit = "";
    bool focusName;

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    public void Bind(DeckEditUI ui) { deck = ui; }

    bool Visible => deck != null && deck.root != null && deck.root.activeInHierarchy && GameManager.Instance != null && DeckPresets.Loaded && !deck.OverlayOpen; // 2026-10-31: 大きな詳細/確認の窓の間は出さない

    void OnDisable() { Open = false; }

    void Update()
    {
        if (Open && !Visible) Close();
    }

    // 戻る(Esc / Android の戻る / パッドの B。DeckEditUI.HandleBack から): 一覧が開いていれば一覧(の中の確認)だけを閉じる
    public bool HandleBack()
    {
        if (!Open) return false;
        if (mode != Mode.List) { mode = Mode.List; GUI.FocusControl(null); } else Close();
        return true;
    }

    void OnGUI()
    {
        if (!Visible) return;
        if (SettingsPanel.IsVisible) return;
        GUI.depth = -100;
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        int padLayer = PadNav.BeginLayer(Open ? 8 : 0);
        if (Open) DrawList(s); else DrawBar(s);
        PadNav.EndLayer(padLayer);
        GUI.matrix = keep;
    }

    // ---------------------------------------------------------------- 帯
    void DrawBar(float s)
    {
        var rt = deck.DeckHeaderRect;
        if (rt == null || UiInputGate.ModalOpen) return; // (押した直後の指の待ちでは消さない)
        Rect r = GuiRectOf(rt, s);
        if (r.width < 40f) return;
        float h = Mathf.Clamp(r.height, 34f, 46f);
        r = new Rect(r.x, r.center.y - h * 0.5f, r.width, h);
        var gm = GameManager.Instance;
        int a = DeckPresets.Active;
        bool can = gm.CanChangeDeckNow;
        float arrow = h * 1.1f;
        if (UiKit.Button(new Rect(r.x, r.y, arrow, h), "◀", h * 0.7f, false, true, can) && can) Switch(-1);
        if (UiKit.Button(new Rect(r.xMax - arrow, r.y, arrow, h), "▶", h * 0.7f, false, true, can) && can) Switch(+1);
        var mid = new Rect(r.x + arrow + 6f, r.y, r.width - arrow * 2f - 12f, h);
        string label = (a + 1) + "  " + Loc.Plain(DeckPresets.DisplayName(a)) + "   " + gm.DeckCards.Count + "/" + GameManager.DeckCapacity + "  ▼";
        if (UiKit.Button(mid, Loc.Shape(label), Mathf.Min(22f, h * 0.5f), true, true, true)) { OpenList(); }
    }

    void Switch(int dir)
    {
        int n = (DeckPresets.Active + dir + DeckPresets.Count) % DeckPresets.Count;
        Use(n);
        UiInputGate.LatchUntilRelease();
    }

    void Use(int i)
    {
        var gm = GameManager.Instance;
        if (gm.ActivatePreset(i))
        {
            deck.RefreshAfterPresetChange();
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        }
    }

    // ---------------------------------------------------------------- 一覧
    void OpenList()
    {
        Open = true; mode = Mode.List; selected = DeckPresets.Active;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
    }

    void Close()
    {
        Open = false; mode = Mode.List;
        GUI.FocusControl(null);
        UiInputGate.LatchUntilRelease();
    }

    void DrawList(float s)
    {
        var gm = GameManager.Instance;
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float sl = safe.xMin / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.yMin / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.6f));
        // 背景を押したら閉じる(パネルの外)
        float pw = Mathf.Min(720f, w - sl - sr - 24f);
        float rowH = 50f;
        float ph = Mathf.Min(h - st - sb - 24f, 76f + rowH * DeckPresets.Count + 130f);
        var panel = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f, pw, ph);
        if (Event.current.type == EventType.MouseDown && !panel.Contains(Event.current.mousePosition) && mode == Mode.List) { Close(); Event.current.Use(); return; }
        OrnateUi.DrawPanel(panel, 0.95f);
        string title = mode == Mode.PickCopyTarget ? LocPendingReview.PresetCopyPick : LocPendingReview.PresetListTitle;
        LocGUI.Label(new Rect(panel.x + 24f, panel.y + 10f, panel.width - 110f, 44f), title, UiKit.Label(24f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiKit.Button(new Rect(panel.xMax - 70f, panel.y + 12f, 52f, 40f), "×", 24f, false, true)) { if (mode != Mode.List) mode = Mode.List; else Close(); return; }

        // 行の高さは画面に収まるように
        float listTop = panel.y + 62f;
        float footer = 118f;
        rowH = Mathf.Min(50f, (panel.yMax - footer - listTop) / DeckPresets.Count);
        var rowStyle = UiKit.Label(Mathf.Min(20f, rowH * 0.46f), TextAnchor.MiddleLeft, false, UiTheme.Text);
        for (int i = 0; i < DeckPresets.Count; i++)
        {
            var row = new Rect(panel.x + 18f, listTop + i * rowH, panel.width - 36f, rowH - 4f);
            var p = DeckPresets.Get(i);
            bool sel = i == selected, act = i == DeckPresets.Active;
            bool targetable = mode == Mode.PickCopyTarget && i != copyFrom;
            bool pressable = mode == Mode.List || targetable;
            if (mode == Mode.Rename && sel)
            {
                UiBackdrop.Draw(row, 0.9f);
                LocGUI.Label(new Rect(row.x + 10f, row.y, 40f, row.height), (i + 1).ToString(), rowStyle);
                GUI.SetNextControlName("presetname");
                nameEdit = UiKit.TextField(new Rect(row.x + 50f, row.y + 4f, row.width - 50f - 230f, row.height - 8f), nameEdit, rowStyle.fontSize, DeckPresets.NameMax * 2);
                if (focusName) { GUI.FocusControl("presetname"); focusName = false; }
                if (UiKit.Button(new Rect(row.xMax - 222f, row.y + 3f, 104f, row.height - 6f), "保存", 18f, true, true)) { gm.RenamePreset(i, nameEdit); mode = Mode.List; GUI.FocusControl(null); }
                if (UiKit.Button(new Rect(row.xMax - 112f, row.y + 3f, 108f, row.height - 6f), "キャンセル", 16f, false, true)) { mode = Mode.List; GUI.FocusControl(null); }
                continue;
            }
            if (UiKit.ButtonCore(row, "", 16f, sel, false, pressable, sel, 0f) && pressable)
            {
                if (mode == Mode.PickCopyTarget) { copyTo = i; mode = (p != null && (p.cards.Count > 0 || !string.IsNullOrEmpty(p.name))) ? Mode.ConfirmCopy : Mode.List; if (mode == Mode.List) DoCopy(); }
                else selected = i;
            }
            int count = i == DeckPresets.Active ? gm.DeckCards.Count : (p != null ? p.cards.Count : 0);
            string nm = Loc.Plain(DeckPresets.DisplayName(i));
            LocGUI.Label(new Rect(row.x + 12f, row.y, 36f, row.height), (i + 1).ToString(), UiKit.Label(rowStyle.fontSize, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
            LocGUI.Label(new Rect(row.x + 50f, row.y, row.width - 50f - 210f, row.height), Loc.Shape(nm), rowStyle);
            string cnt = count == 0 ? Loc.T(LocPendingReview.PresetEmpty) : count + "/" + GameManager.DeckCapacity;
            LocGUI.Label(new Rect(row.xMax - 205f, row.y, 90f, row.height), cnt, UiKit.Label(rowStyle.fontSize, TextAnchor.MiddleRight, false, count > 0 ? UiTheme.Mile : UiTheme.Disabled));
            if (act) LocGUI.Label(new Rect(row.xMax - 110f, row.y, 104f, row.height), LocPendingReview.PresetInUse, UiKit.Label(Mathf.Min(17f, rowStyle.fontSize), TextAnchor.MiddleRight, true, UiTheme.GoldText));
        }

        // 下の操作
        var foot = new Rect(panel.x + 18f, panel.yMax - footer + 8f, panel.width - 36f, footer - 22f);
        bool can = gm.CanChangeDeckNow;
        if (!can)
        {
            LocGUI.Label(foot, LocPendingReview.PresetLocked, UiKit.Label(18f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
            return;
        }
        if (mode == Mode.ConfirmCopy || mode == Mode.ConfirmClear)
        {
            string msg = mode == Mode.ConfirmCopy
                ? Loc.Shape(Loc.Plain(LocPendingReview.PresetCopyConfirm).Replace("{0}", Loc.Plain(DeckPresets.DisplayName(copyTo))).Replace("{1}", Loc.Plain(DeckPresets.DisplayName(copyFrom))))
                : Loc.Shape(Loc.Plain(LocPendingReview.PresetClearConfirm).Replace("{0}", Loc.Plain(DeckPresets.DisplayName(selected))));
            var ms = UiKit.Label(17f, TextAnchor.MiddleCenter, false, UiTheme.Danger); ms.wordWrap = true;
            GUI.Label(new Rect(foot.x, foot.y, foot.width, 44f), msg, ms);
            float bw = Mathf.Min(220f, foot.width * 0.45f);
            if (UiKit.Button(new Rect(foot.center.x - bw - 6f, foot.y + 48f, bw, 44f), "はい", 19f, true, true))
            {
                if (mode == Mode.ConfirmCopy) DoCopy();
                else { if (gm.ClearPreset(selected)) deck.RefreshAfterPresetChange(); mode = Mode.List; }
            }
            if (UiKit.Button(new Rect(foot.center.x + 6f, foot.y + 48f, bw, 44f), "いいえ", 19f, false, true)) mode = Mode.List;
            return;
        }
        if (mode == Mode.PickCopyTarget)
        {
            if (UiKit.Button(new Rect(foot.center.x - 110f, foot.y + 24f, 220f, 46f), "キャンセル", 19f, false, true)) mode = Mode.List;
            return;
        }
        if (mode != Mode.List) return;
        float gap = 8f, bwd = (foot.width - gap * 3f) / 4f, by = foot.y + 22f, bh = 50f;
        bool notActive = selected != DeckPresets.Active;
        if (UiKit.Button(new Rect(foot.x, by, bwd, bh), LocPendingReview.PresetUse, 19f, true, true, notActive) && notActive) { Use(selected); }
        if (UiKit.Button(new Rect(foot.x + (bwd + gap), by, bwd, bh), LocPendingReview.PresetRename, 17f, false, true))
        {
            var p = DeckPresets.Get(selected);
            nameEdit = p != null ? p.name : ""; mode = Mode.Rename; focusName = true;
        }
        if (UiKit.Button(new Rect(foot.x + (bwd + gap) * 2f, by, bwd, bh), LocPendingReview.PresetCopy, 17f, false, true)) { copyFrom = selected; mode = Mode.PickCopyTarget; }
        if (UiKit.Button(new Rect(foot.x + (bwd + gap) * 3f, by, bwd, bh), LocPendingReview.PresetClear, 17f, false, true)) mode = Mode.ConfirmClear;
    }

    void DoCopy()
    {
        var gm = GameManager.Instance;
        if (gm.CopyPreset(copyFrom, copyTo)) { selected = copyTo; if (copyTo == DeckPresets.Active) deck.RefreshAfterPresetChange(); }
        mode = Mode.List;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(QaSweep -qaDeckPreset): 一覧/名前の入力/コピー先の選択を開く
    public void DebugOpenList() => OpenList();
    public void DebugRename(int i) { OpenList(); selected = i; var p = DeckPresets.Get(i); nameEdit = p != null ? p.name : ""; mode = Mode.Rename; focusName = true; } // 2026-10-31: 本物の「名前の変更」と同じく入力欄に焦点
    public void DebugPickCopy(int from) { OpenList(); selected = copyFrom = from; mode = Mode.PickCopyTarget; }
    public void DebugClose() => Close();
#endif

    // uGUI の RectTransform → IMGUI の座標(GUI.matrix の倍率 s で割った値)
    static readonly Vector3[] corners = new Vector3[4];
    static Rect GuiRectOf(RectTransform rt, float s)
    {
        rt.GetWorldCorners(corners);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        return new Rect(a.x / s, (Screen.height - b.y) / s, (b.x - a.x) / s, (b.y - a.y) / s);
    }
}

using UnityEngine;

// 疾走出発の入口(2026-10-05 試作): ホームのステージ選択画面の「出発」の右に「疾走出発…」ボタン →
// 行き先(10,000m 刻み)の一覧(解放済みだけ選べる、未解放は理由を表示)→ 選ぶと通常の出発と同じ遷移で疾走が始まる。
// マルチの部屋に入っている間は出さない(ソロのみ)。ステージ選択は uGUI なので、ここは IMGUI で上に重ねる
// (パネルを開いている間は UiInputGate で背後の uGUI のタップを止める)。
public class SprintDeparturePanel : MonoBehaviour
{
    readonly UiScroll descScroll = new UiScroll(), listScroll = new UiScroll(); // 2026-10-08
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SprintDeparturePanel>() != null) return;
        var go = new GameObject("SprintDeparturePanel");
        DontDestroyOnLoad(go);
        go.AddComponent<SprintDeparturePanel>();
    }

    public static bool Open { get; private set; }
    public static int LastButtonFrame { get; private set; } = -100; // 確認用(2026-10-14)
    public static Rect LastButtonRect { get; private set; }
    public static int Opens { get; private set; }
    string stageForPanel;
    public static UiScroll DescScrollForTest => FindFirstObjectByType<SprintDeparturePanel>()?.descScroll; // 確認用(説明のスクロール)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 自動テスト用(2026-10-15): 行き先の一覧を開く/閉じる(ステージ選択を開いている時だけ表示される)
    public static void DebugOpen(bool open)
    {
        var p = FindFirstObjectByType<SprintDeparturePanel>();
        var gm = GameManager.Instance;
        if (p == null || gm == null || gm.stageSelectUI == null) return;
        if (open) { Open = true; p.stageForPanel = gm.stageSelectUI.SelectedStageIdInUi; UiInputGate.SprintPanelOpen = true; }
        else { Open = false; UiInputGate.SprintPanelOpen = false; }
    }
#endif

    void Update()
    {
        var gm = GameManager.Instance;
        bool visible = gm != null && !gm.HasStarted && gm.StageSelectIsOpen && !NetSession.IsActive;
        if (!visible && Open) { Open = false; UiInputGate.SprintPanelOpen = false; }
    }

    // CanvasScaler(1920x1080, match 0.5)と同じ倍率
    static float RefScale => Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.HasStarted || !gm.StageSelectIsOpen || NetSession.IsActive || gm.stageSelectUI == null) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (SettingsPanel.IsVisible || UiInputGate.DebugPanelOpen) return;
        // 2026-10-14(実機の不具合): CPU サポートの一覧を開いている間は、このボタンを描かない・押せない(以前は同じ深さで手前に出て「決定」の近くに重なった)
        if (SupportDeparturePanel.Open) return;
        GUI.depth = Open ? -960 : -950; // 開いた一覧は、閉じているボタン(-950)より必ず手前
        float s = RefScale;
        string stage = gm.stageSelectUI.SelectedStageIdInUi;
        if (string.IsNullOrEmpty(stage)) return;

        // 「出発」の右隣(2026-10-09: 出発と2つで1組として中央に置く。StageSelectUI.ApplyButtonLayout)
        Rect dep = gm.stageSelectUI.DepartGuiRect;
        Rect btn = dep.width > 0f
            ? new Rect(dep.xMax + StageSelectUI.PairGap * s, dep.y, StageSelectUI.SprintW * s, dep.height)
            : new Rect(Screen.width * 0.5f + 170f * s, Screen.height - (40f + 76f) * s, 300f * s, 76f * s);
        if (!Open)
        {
            int unlocked = 0;
            foreach (var d in SprintRecords.Destinations(stage)) if (d.unlocked) unlocked++;
            if (Event.current.type == EventType.Repaint) { LastButtonFrame = Time.frameCount; LastButtonRect = btn; } // 確認用: 描いたフレームと場所
            // 2026-10-28: 長い訳(タミル語など)で2行に折れてボタンの高さからはみ出していた → 1行に入る大きさまで(最小 18)
            string sprLbl = unlocked > 0 ? $"疾走出発({unlocked}地点)" : "疾走出発…";
            float sprFs = 26f * s, sprTw = UiKit.Label(sprFs, TextAnchor.MiddleCenter, true).CalcSize(new GUIContent(Loc.Auto(sprLbl))).x;
            if (sprTw > btn.width - 20f * s) sprFs = Mathf.Max(18f * s, sprFs * (btn.width - 20f * s) / sprTw);
            if (UiKit.Button(btn, sprLbl, sprFs, false, true, true))
            {
                Open = true; stageForPanel = stage; UiInputGate.SprintPanelOpen = true; Opens++;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            }
            return;
        }

        // 行き先の一覧
        float pw = Mathf.Min(Screen.width - 24f * s, 760f * s), ph = 900f * s;
        if (Screen.height > Screen.width) ph = Mathf.Min(Screen.height - 24f * s, Mathf.Max(ph, Screen.height * 0.8f)); // 2026-10-15: 縦画面は下の空きも使う
        Rect panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        // 2026-10-22(画面側の完成度向上): 共通の飾り枠の板 + 上にそのステージの絵の帯(行き先の場所が分かる。文字は絵の上に)
        OrnateUi.DrawPanel(panel, 0.97f);
        float y = panel.y + 20f * s, x = panel.x + 28f * s, w = pw - 56f * s;
        var st = StageDatabase.FindById(stageForPanel);
        if (st != null && st.thumbnail != null)
        {
            Rect band = new Rect(x - 6f * s, y - 4f * s, w + 12f * s, 58f * s);
            Color kb = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.5f * kb.a);
            GUI.DrawTexture(band, st.thumbnail, ScaleMode.ScaleAndCrop);
            GUI.color = new Color(UiTheme.PanelFill.r, UiTheme.PanelFill.g, UiTheme.PanelFill.b, 0.45f * kb.a);
            GUI.DrawTexture(band, Texture2D.whiteTexture);
            GUI.color = kb;
        }
        LocGUI.Label(new Rect(x, y, w, 50f * s), $"疾走出発  {(st != null ? st.displayName : stageForPanel)}", UiKit.Label(34f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.88f, 0.5f)));
        y += 52f * s;
        // 2026-10-08: 説明は折り返し、長い言語ではスクロール(以前は1行のままで切れていた)
        // 2026-10-15 第3弾(エステルの表示の確認): 縦画面は字を 1.35倍・説明の枠を高く(最後の到着 Lv の文まで読める。収まらない分はスクロール)
        bool portrait = Screen.height > Screen.width;
        float ts = portrait ? s * 1.35f : s, descH = (portrait ? 150f : 76f) * s;
        var descSt = new GUIStyle(UiKit.Label(20f * ts, TextAnchor.UpperLeft, false, new Color(0.85f, 0.9f, 1f))); descSt.wordWrap = true;
        descScroll.Text(new Rect(x, y, w, descH), LocPendingReview.SprintDesc, descSt, stageForPanel);
        y += descH + 8f * s;
        var list = SprintRecords.Destinations(stageForPanel);
        // 行き先の一覧も、収まらない時はスクロール(ドラッグした指では行き先を選ばない)
        var listView = new Rect(x, y, w, Mathf.Max(68f * s, panel.yMax - 90f * s - y));
        // 2026-10-28: 行の文が折り返す幅(縦の細い画面・長い訳)では、その行を高くする(以前は 62 固定で、2行目が行の枠からはみ出していた)
        var rowSt = new GUIStyle(UiKit.Label(24f * s, TextAnchor.MiddleLeft, true)) { wordWrap = true };
        float rowTextW = w - 12f - (12f + 40f + 12f + 10f) * s;
        string RowLabel(SprintRecords.Destination d) => d.unlocked ? $"{d.meters / 1000}km まで疾走   (約{SprintTuning.I.SecondsFor(d.meters):F0}秒 + リングのカード選択){(d.why == "DEBUG 全解放" ? "  [DEBUG 全解放]" : "")}" : $"{d.meters / 1000}km   🔒 {d.why}";
        float RowH(SprintRecords.Destination d) => Mathf.Max(62f * s, rowSt.CalcHeight(new GUIContent(Loc.Auto(RowLabel(d)).Replace("🔒 ", "")), rowTextW) + 18f * s);
        float listH = (SprintRecords.DevUnlockAll ? 34f * s : 0f);
        foreach (var d in list) listH += RowH(d) + 6f * s;
        listScroll.Begin(listView, listH, stageForPanel);
        float x0 = x; x = 0f; y = 0f;
        float maxMeters = 1f; foreach (var d in list) maxMeters = Mathf.Max(maxMeters, d.meters);
        foreach (var d in list)
        {
            Rect row = new Rect(x, y, w - 12f, RowH(d));
            string label = RowLabel(d);
            // 2026-10-22: 行の左にアイコン(行ける = 出発の扉 / まだ = 鍵の絵。🔒 の字は絵に置き換える)、下に「どこまで行くか」の細い線
            bool hitRow = UiKit.Button(row, "", 24f * s, d.unlocked, false, d.unlocked);
            Texture2D rowIcon = d.unlocked ? UiIcons.Home("depart") : UiIcons.Lock;
            float ic = 40f * s;
            string shown = Loc.Auto(label);
            if (rowIcon != null)
            {
                shown = shown.Replace("🔒 ", "").Replace("🔒", "");
                Color ki = GUI.color; GUI.color = new Color(1f, 1f, 1f, (d.unlocked ? 1f : 0.7f) * ki.a);
                UiIcons.Draw(new Rect(row.x + 12f * s, row.y + (row.height - ic) * 0.5f - 3f * s, ic, ic), rowIcon);
                GUI.color = ki;
            }
            float tx = row.x + 12f * s + (rowIcon != null ? ic + 12f * s : 0f);
            var rowTextSt = new GUIStyle(rowSt); rowTextSt.normal.textColor = d.unlocked ? UiTheme.GoldHover : UiTheme.Disabled;
            LocGUI.Label(new Rect(tx, row.y, row.xMax - tx - 10f * s, row.height - 8f * s), shown, rowTextSt);
            Rect track = new Rect(tx, row.yMax - 10f * s, row.xMax - tx - 16f * s, Mathf.Max(2f, 3f * s));
            UiKit.Fill(track, new Color(1f, 1f, 1f, 0.1f));
            UiKit.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(d.meters / maxMeters), track.height),
                d.unlocked ? new Color(UiTheme.GoldBright.r, UiTheme.GoldBright.g, UiTheme.GoldBright.b, 0.85f) : new Color(UiTheme.Locked.r, UiTheme.Locked.g, UiTheme.Locked.b, 0.6f));
            if (hitRow && d.unlocked)
            {
                Open = false; UiInputGate.SprintPanelOpen = false;
                UiInputGate.LatchUntilRelease();
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                if (!gm.DepartSprint(stageForPanel, d.meters)) Debug.Log("[Sprint] depart failed");
                listScroll.End(listView, listH);
                return;
            }
            y += row.height + 6f * s; // 2026-10-28: 行の高さは文の折り返しで変わる
        }
        if (SprintRecords.DevUnlockAll) LocGUI.Label(new Rect(x, y, w, 30f * s), "(開発版: DEBUG の全解放が ON)", UiKit.Label(18f * s, TextAnchor.MiddleLeft, false, new Color(1f, 0.6f, 0.5f)));
        listScroll.End(listView, listH);
        x = x0;
        if (UiKit.Button(new Rect(x, panel.yMax - 76f * s, w, 56f * s), "閉じる", 24f * s, false, false, true))
        {
            Open = false; UiInputGate.SprintPanelOpen = false; UiInputGate.LatchUntilRelease();
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
        }
        var e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
    }
}

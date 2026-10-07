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
    string stageForPanel;

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
        GUI.depth = -950;
        float s = RefScale;
        string stage = gm.stageSelectUI.SelectedStageIdInUi;
        if (string.IsNullOrEmpty(stage)) return;

        // 「出発」(中央下 280x76、下から40)の右隣
        Rect btn = new Rect(Screen.width * 0.5f + 170f * s, Screen.height - (40f + 76f) * s, 300f * s, 76f * s);
        if (!Open)
        {
            int unlocked = 0;
            foreach (var d in SprintRecords.Destinations(stage)) if (d.unlocked) unlocked++;
            if (UiKit.Button(btn, unlocked > 0 ? $"疾走出発…({unlocked})" : "疾走出発…", 26f * s, false, true, true))
            {
                Open = true; stageForPanel = stage; UiInputGate.SprintPanelOpen = true;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            }
            return;
        }

        // 行き先の一覧
        float pw = 760f * s, ph = 900f * s;
        Rect panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        UiKit.Fill(panel, new Color(0.06f, 0.08f, 0.15f, 0.97f));
        float y = panel.y + 20f * s, x = panel.x + 28f * s, w = pw - 56f * s;
        var st = StageDatabase.FindById(stageForPanel);
        LocGUI.Label(new Rect(x, y, w, 50f * s), $"疾走出発  {(st != null ? st.displayName : stageForPanel)}", UiKit.Label(34f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.88f, 0.5f)));
        y += 52f * s;
        // 2026-10-08: 説明は折り返し、長い言語ではスクロール(以前は1行のままで切れていた)
        var descSt = UiKit.Label(20f * s, TextAnchor.UpperLeft, false, new Color(0.85f, 0.9f, 1f)); descSt.wordWrap = true;
        descScroll.Text(new Rect(x, y, w, 76f * s), "攻略済みの区間を一気に駆け抜け、選んだ関門の少し手前から走り始めます。途中のボス報酬ぶんのカードは自動で取得。5,000mごとのリングをくぐると追加で1枚選べます。", descSt, stageForPanel);
        y += 84f * s;
        var list = SprintRecords.Destinations(stageForPanel);
        // 行き先の一覧も、収まらない時はスクロール(ドラッグした指では行き先を選ばない)
        var listView = new Rect(x, y, w, Mathf.Max(68f * s, panel.yMax - 90f * s - y));
        float listH = list.Count * 68f * s + (SprintRecords.DevUnlockAll ? 34f * s : 0f);
        listScroll.Begin(listView, listH, stageForPanel);
        float x0 = x; x = 0f; y = 0f;
        foreach (var d in list)
        {
            Rect row = new Rect(x, y, w - 12f, 62f * s);
            string label = d.unlocked ? $"{d.meters / 1000}km まで疾走   (約{SprintTuning.I.SecondsFor(d.meters):F0}秒 + リング){(d.why == "DEBUG 全解放" ? "  [DEBUG 全解放]" : "")}" : $"{d.meters / 1000}km   🔒 {d.why}";
            if (UiKit.Button(row, label, 24f * s, d.unlocked, false, d.unlocked) && d.unlocked)
            {
                Open = false; UiInputGate.SprintPanelOpen = false;
                UiInputGate.LatchUntilRelease();
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                if (!gm.DepartSprint(stageForPanel, d.meters)) Debug.Log("[Sprint] depart failed");
                listScroll.End(listView, listH);
                return;
            }
            y += 68f * s;
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

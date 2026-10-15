using System.Collections.Generic;
using UnityEngine;

// CPU サポート(2026-10-14): 出発の前に CPU の仲間(0〜3人)を選ぶ。ステージ選択の「出発」の上にボタン → 解放済みのキャラの一覧。
// 選んだ順に P2 / P3 / P4。0人 = 従来のソロ。前回の選択を覚える(設定 CpuSupportV1。進行ではない)。
// マルチの部屋に入っている間は出さない(人間のマルチへの CPU の補充は今回の対象外)。IMGUI で上に重ねる(SprintDeparturePanel と同じ作り)。
public class SupportDeparturePanel : MonoBehaviour
{
    public const string MileShareNote = LocPendingReview.MileShareNote; // 2026-10-15(協力マルチの説明と同じ文。レビュー待ちの一覧は LocPendingReview)
    public const string PrefKey = "CpuSupportV1";
    readonly UiScroll listScroll = new UiScroll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SupportDeparturePanel>() != null) return;
        var go = new GameObject("SupportDeparturePanel");
        DontDestroyOnLoad(go);
        go.AddComponent<SupportDeparturePanel>();
        LoadChoice();
    }

    public static bool Open { get; private set; }
    public static Rect LastSummaryRect { get; private set; } // 確認用(2026-10-14、画面の座標)
    public static Rect LastDecideRect { get; private set; }
    public static int Decides { get; private set; }
    readonly List<string> editing = new List<string>();

    // 覚えている選択を読む(解放されていないキャラは外す)。開発用の -cpuAllies が付いていればそちらを優先
    public static void LoadChoice()
    {
        if (CpuParty.Request.Count > 0) return;
        foreach (var id in SaveStore.GetString(PrefKey, "").Split(','))
            if (!string.IsNullOrEmpty(id) && CpuParty.Request.Count < CpuParty.MaxAllies && CharacterDatabase.FindById(id) != null && UnlockRules.IsCharacterUnlocked(id))
                CpuParty.Request.Add(id);
    }

    static void SaveChoice()
    {
        SaveStore.SetString(PrefKey, string.Join(",", CpuParty.Request));
        SaveStore.Save();
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool visible = gm != null && !gm.HasStarted && gm.StageSelectIsOpen && !NetSession.IsActive;
        if (!visible && Open) { Open = false; UiInputGate.SupportPanelOpen = false; }
    }

    static float RefScale => Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));

    static string NameOf(string id) { var d = CharacterDatabase.FindById(id); return d != null ? Loc.T(d.displayName) : id; }

    public static string Summary()
    {
        if (CpuParty.Request.Count == 0) return Loc.T("CPU サポート: なし(ソロ)");
        var names = new List<string>();
        foreach (var id in CpuParty.Request) names.Add(NameOf(id));
        return Loc.F("CPU サポート: {0}人", CpuParty.Request.Count) + "  " + string.Join(" / ", names);
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.HasStarted || !gm.StageSelectIsOpen || NetSession.IsActive || gm.stageSelectUI == null) return;
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (SettingsPanel.IsVisible || UiInputGate.DebugPanelOpen || SprintDeparturePanel.Open) return;
        GUI.depth = Open ? -960 : -950; // 2026-10-14: 開いた一覧は、閉じているボタン(疾走出発など -950)より必ず手前
        float s = RefScale;
        Rect dep = gm.stageSelectUI.DepartGuiRect;
        // 「出発」と「疾走出発」の組の上(同じ幅)
        float pairW = dep.width > 0f ? dep.width + (StageSelectUI.PairGap + StageSelectUI.SprintW) * s : 760f * s;
        Rect btn = dep.width > 0f
            ? new Rect(dep.x, dep.y - 70f * s, pairW, 58f * s)
            : new Rect((Screen.width - pairW) * 0.5f, Screen.height - (40f + 76f + 70f) * s, pairW, 58f * s);
        if (!Open)
        {
            LastSummaryRect = btn; LastDecideRect = Rect.zero;
            if (UiKit.Button(btn, Summary(), 22f * s, CpuParty.Request.Count > 0, false, true))
            {
                Open = true; UiInputGate.SupportPanelOpen = true;
                editing.Clear(); editing.AddRange(CpuParty.Request);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            }
            return;
        }

        float pw = Mathf.Min(Screen.width - 24f * s, 820f * s), ph = Mathf.Min(Screen.height - 24f * s, 920f * s);
        Rect panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        UiKit.Fill(panel, new Color(0.06f, 0.08f, 0.15f, 0.97f));
        float y = panel.y + 18f * s, x = panel.x + 26f * s, w = pw - 52f * s;
        LocGUI.Label(new Rect(x, y, w, 48f * s), "CPU サポート", UiKit.Label(32f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.88f, 0.5f)));
        y += 50f * s;
        var desc = new GUIStyle(UiKit.Label(19f * s, TextAnchor.UpperLeft, false, new Color(0.85f, 0.9f, 1f))) { wordWrap = true };
        // 2026-10-15: 獲得 MILE を参加人数で分配(新しい文はエステルのレビュー後に各言語へ。それまでは前の文)
        LocGUI.Label(new Rect(x, y, w, 80f * s), Loc.Reviewed(LocPendingReview.SupportDesc, LocPendingReview.SupportOld), desc);
        y += 86f * s;
        if (Loc.Ready(MileShareNote))
        {
            LocGUI.Label(new Rect(x, y, w, 28f * s), MileShareNote, UiKit.Label(19f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
            y += 30f * s;
        }
        LocGUI.Label(new Rect(x, y, w, 30f * s), Loc.F("選んだ仲間: {0}/{1}", editing.Count, CpuParty.MaxAllies), UiKit.Label(20f * s, TextAnchor.MiddleLeft, true, Color.white));
        y += 34f * s;

        // 未解放のキャラは一覧に出さない(キャラ選択と同じ決まり)
        var chars = new List<CharacterDefinition>();
        foreach (var cd in CharacterDatabase.AllCharacters) if (UnlockRules.IsCharacterVisible(cd.characterId)) chars.Add(cd);
        float rowH = 58f * s, gap = 8f * s;
        int cols = pw > 700f * s ? 2 : 1;
        float colW = (w - (cols - 1) * gap) / cols;
        int rows = Mathf.CeilToInt(chars.Count / (float)cols);
        var view = new Rect(x, y, w, Mathf.Max(rowH, panel.yMax - 150f * s - y));
        float listH = rows * (rowH + gap);
        listScroll.Begin(view, listH, "support");
        for (int i = 0; i < chars.Count; i++)
        {
            var d = chars[i];
            bool unlocked = true;
            int slot = editing.IndexOf(d.characterId);
            Rect r = new Rect((i % cols) * (colW + gap), (i / cols) * (rowH + gap), colW - 4f, rowH);
            string label = slot >= 0 ? $"P{slot + 2}  {Loc.T(d.displayName)}" : Loc.T(d.displayName);
            if (UiKit.Button(r, label, 22f * s, slot >= 0, false, unlocked) && unlocked && !listScroll.Dragging)
            {
                if (slot >= 0) editing.RemoveAt(slot);
                else if (editing.Count < CpuParty.MaxAllies) editing.Add(d.characterId);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            }
        }
        listScroll.End(view, listH);

        float bw = (w - 2f * gap) / 3f, by = panel.yMax - 76f * s;
        if (UiKit.Button(new Rect(x, by, bw, 56f * s), "なし(ソロ)", 22f * s, false, false, true))
        {
            CpuParty.Request.Clear(); SaveChoice(); Close();
        }
        LastDecideRect = new Rect(x + bw + gap, by, bw, 56f * s);
        if (UiKit.Button(new Rect(x + bw + gap, by, bw, 56f * s), "決定", 22f * s, true, false, true))
        {
            Decides++;
            CpuParty.Request.Clear(); CpuParty.Request.AddRange(editing); SaveChoice(); Close();
        }
        if (UiKit.Button(new Rect(x + 2f * (bw + gap), by, bw, 56f * s), "閉じる", 22f * s, false, false, true)) Close();
        var e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(撮影): 一覧を開く/閉じる
    public static void DebugOpen(bool open)
    {
        var p = FindFirstObjectByType<SupportDeparturePanel>();
        if (p == null) return;
        if (open) { Open = true; UiInputGate.SupportPanelOpen = true; p.editing.Clear(); p.editing.AddRange(CpuParty.Request); }
        else { Open = false; UiInputGate.SupportPanelOpen = false; }
    }
#endif

    void Close()
    {
        Open = false; UiInputGate.SupportPanelOpen = false; UiInputGate.LatchUntilRelease();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
    }
}

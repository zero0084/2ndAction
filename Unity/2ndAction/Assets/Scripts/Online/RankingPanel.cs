using System.Collections.Generic;
using UnityEngine;

// ランキングの画面(2026-10-08、IMGUI。設定と同じ作り)。ホームの「ランキング」から開く。
//  ・まだ参加していない時: 公開される内容の案内とコードネームの確認 → 「参加する」。参加しなくても遊べる。
//  ・参加している時: マップのタブ(解放済みのマップだけ。未解放は出さない)/ 上位 50 / 自分の順位と自己ベスト /
//    投稿待ちの数と再送 / 読み込み中・記録なし・通信失敗と再試行。同じ距離は同じ順位。
//  ・一覧は指で上下にドラッグして読む(ボタンを押したつもりの指でスクロールしても押されない)。
public class RankingPanel : MonoBehaviour
{
    public static RankingPanel Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    bool open;
    int tab;
    readonly Dictionary<string, Leaderboard.Page> cache = new Dictionary<string, Leaderboard.Page>();
    string loadingStage, errorStage, errorText;
    float lastLoadAt = -100f;
    string nameEdit; bool joinStep;
    readonly UiScroll list = new UiScroll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[RankingPanel]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<RankingPanel>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.open = false; };
    }

    public static void OpenStatic() { if (Instance != null) Instance.Open(); }
    void Open()
    {
        open = true; joinStep = false; nameEdit = Codename.Current; tab = Mathf.Clamp(tab, 0, Mathf.Max(0, Tabs().Count - 1));
        list.Reset();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiOpen);
        if (Leaderboard.Joined) { Load(force: false); _ = Leaderboard.FlushAsync("open"); }
    }
    void Close() { open = false; UiInputGate.LatchUntilRelease(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiClose); }

    // 解放済みのランキング対象マップ(未解放はタブも出さない)
    static List<string> Tabs()
    {
        var l = new List<string>();
        foreach (var st in Leaderboard.Stages) if (UnlockRules.IsStageVisible(st) && (st != BossManager.LastStageId || ProgressStats.FinalDungeonUnlocked)) l.Add(st);
        return l;
    }

    async void Load(bool force)
    {
        var tabs = Tabs(); if (tabs.Count == 0) return;
        string st = tabs[Mathf.Clamp(tab, 0, tabs.Count - 1)];
        if (loadingStage == st) return;
        if (!force && cache.ContainsKey(st) && Time.realtimeSinceStartup - lastLoadAt < 20f) return;
        if (Time.realtimeSinceStartup - lastLoadAt < 2f && force) return; // 連打で API を呼びすぎない
        loadingStage = st; errorStage = null; lastLoadAt = Time.realtimeSinceStartup;
        try { var page = await Leaderboard.Backend.Load(st, 50); cache[st] = page; }
        catch (System.Exception e) { errorStage = st; errorText = e.GetType().Name; Debug.LogWarning("[Ranking] load failed: " + e.GetType().Name + ": " + e.Message); }
        finally { if (loadingStage == st) loadingStage = null; }
    }

    void Update() { if (!open) Leaderboard.Tick(); }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -1900;
        int padLayer = PadNav.BeginLayer(9);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = Screen.safeArea;
        float sl = safe.xMin / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.yMin / s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.6f));
        float pw = Mathf.Min(900f, w - sl - sr - 24f), ph = Mathf.Min(h - st - sb - 24f, 680f);
        var panel = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(panel, 0.95f);
        LocGUI.Label(new Rect(panel.x + 28f, panel.y + 14f, panel.width - 120f, 44f), "ランキング", UiKit.Label(28f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
        if (UiKit.Button(new Rect(panel.xMax - 70f, panel.y + 14f, 50f, 46f), "×", 26f, false, false)) Close();

        var body = new Rect(panel.x + 24f, panel.y + 70f, panel.width - 48f, panel.height - 70f - 80f);
        if (!Leaderboard.Joined) DrawJoin(body);
        else DrawBoard(body);

        if (UiKit.Button(new Rect(panel.center.x - 110f, panel.yMax - 68f, 220f, 52f), "閉じる", 22f, true)) Close();

        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へタップを通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
        PadNav.EndLayer(padLayer);
    }

    // ---------------------------------------------------------------- 参加の案内
    void DrawJoin(Rect r)
    {
        var note = new GUIStyle(UiKit.Label(19f, TextAnchor.UpperLeft, false, new Color(0.9f, 0.93f, 1f))); note.wordWrap = true;
        float y = r.y;
        string text = joinStep
            ? "この名前で参加します。参加すると、帰還して確定した記録(マップ別の最高到達距離・使ったキャラ・オート/疾走出発の使用)がコードネームと一緒に公開されます。"
            : "ランキングに参加すると、コードネームと記録(マップ別の最高到達距離・使ったキャラ・オート/疾走出発の使用)が公開されます。参加しなくても、通常プレイと手元の記録はそのまま使えます。";
        float th = Mathf.Min(r.height * 0.5f, note.CalcHeight(new GUIContent(Loc.Auto(text)), r.width) + 8f);
        LocGUI.Label(new Rect(r.x, y, r.width, th), text, note);
        y += th + 10f;
        LocGUI.Label(new Rect(r.x, y, 180f, 44f), "コードネーム", UiKit.Label(20f));
        var style = new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        nameEdit = GUI.TextField(new Rect(r.x + 180f, y + 2f, r.width - 180f, 42f), nameEdit ?? "", 40, style);
        y += 50f;
        LocGUI.Label(new Rect(r.x, y, r.width, 26f), $"最大{Codename.MaxLength}文字。本名などの個人情報は入れないでください。", UiKit.Label(15f, TextAnchor.UpperLeft, false, new Color(0.75f, 0.8f, 0.9f)));
        y += 34f;
        string clean = Codename.Sanitize(nameEdit);
        bool ok = !string.IsNullOrEmpty(clean);
        if (!ok) LocGUI.Label(new Rect(r.x, y, r.width, 26f), "参加にはコードネームが必要です", UiKit.Label(16f, TextAnchor.UpperLeft, false, new Color(1f, 0.7f, 0.5f)));
        y += 30f;
        // 2026-10-09(依頼G-2): 中央に置く。確認の段では「戻る」(左)と「参加する」(右)の2つを1組で中央に
        float bw = 260f, bgap = 20f, bx = joinStep ? r.x + (r.width - (bw * 2f + bgap)) * 0.5f : r.x + (r.width - bw) * 0.5f;
        if (joinStep && UiKit.Button(new Rect(bx, y, bw, 64f), "戻る", 21f, false)) joinStep = false;
        if (UiKit.Button(new Rect(joinStep ? bx + bw + bgap : bx, y, bw, 64f), joinStep ? "参加する" : "次へ", 22f, ok, true, ok) && ok)
        {
            if (!joinStep) { joinStep = true; }
            else
            {
                Codename.Set(clean);
                Leaderboard.Join();
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                Load(force: true);
            }
        }
    }

    // ---------------------------------------------------------------- 一覧
    void DrawBoard(Rect r)
    {
        var tabs = Tabs();
        if (tabs.Count == 0) return;
        tab = Mathf.Clamp(tab, 0, tabs.Count - 1);
        float tw = Mathf.Min(210f, (r.width - (tabs.Count - 1) * 8f) / tabs.Count);
        for (int i = 0; i < tabs.Count; i++)
        {
            if (UiKit.Button(new Rect(r.x + i * (tw + 8f), r.y, tw, 44f), UnlockRules.StageName(tabs[i]), 17f, i == tab, false) && i != tab)
            { tab = i; list.Reset(); Load(force: false); }
        }
        string st = tabs[tab];
        float y = r.y + 54f;
        cache.TryGetValue(st, out var page);
        var info = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        // 自分(上に固定)
        var best = Leaderboard.VerifiedBest(st);
        string meLine = page != null && page.me != null
            ? Loc.Auto("あなた") + $": #{page.me.rank}  {page.me.meters:N0}m" // 順位は # で(言語ごとの順位の言い方に頼らない)
            : Loc.Auto(best != null ? "あなた: 投稿待ちの記録があります" : "あなた: まだ記録がありません");
        LocGUI.Label(new Rect(r.x, y, r.width * 0.6f, 30f), meLine, UiKit.Label(19f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.4f)));
        int pending = Leaderboard.PendingCount;
        if (pending > 0)
        {
            LocGUI.Label(new Rect(r.x + r.width * 0.6f, y, r.width * 0.4f - 130f, 30f), Loc.Auto("投稿待ち") + $" {pending}", info);
            if (UiKit.Button(new Rect(r.xMax - 120f, y - 4f, 120f, 38f), Leaderboard.Sending ? "送信中…" : "再送", 16f, false, true, !Leaderboard.Sending) && !Leaderboard.Sending) _ = Leaderboard.FlushAsync("manual");
        }
        y += 38f;

        var view = new Rect(r.x, y, r.width, r.yMax - y - 44f);
        if (loadingStage == st && page == null) { LocGUI.Label(view, "読み込み中…", UiKit.Label(20f, TextAnchor.MiddleCenter)); }
        else if (errorStage == st && page == null)
        {
            LocGUI.Label(new Rect(view.x, view.y + view.height * 0.3f, view.width, 30f), "ランキングを読み込めませんでした。通信を確認してください", UiKit.Label(18f, TextAnchor.MiddleCenter, false, new Color(1f, 0.7f, 0.6f)));
            if (UiKit.Button(new Rect(view.center.x - 90f, view.y + view.height * 0.3f + 40f, 180f, 50f), "再試行", 19f, true)) Load(force: true);
        }
        else if (page != null && page.top.Count == 0) LocGUI.Label(view, "まだ記録がありません", UiKit.Label(20f, TextAnchor.MiddleCenter));
        else if (page != null)
        {
            const float RowH = 40f;
            float contentH = page.top.Count * RowH;
            list.Begin(view, contentH);
            for (int i = 0; i < page.top.Count; i++)
            {
                var row = page.top[i];
                var rr = new Rect(0f, i * RowH, view.width - 12f, RowH - 4f);
                UiKit.Fill(rr, row.isMe ? new Color(1f, 0.8f, 0.3f, 0.22f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.02f));
                var cs = new GUIStyle(UiKit.Label(18f, TextAnchor.MiddleLeft, row.isMe, row.isMe ? new Color(1f, 0.9f, 0.55f) : Color.white));
                GUI.Label(new Rect(rr.x + 8f, rr.y, 64f, rr.height), row.rank.ToString(), cs);
                GUI.Label(new Rect(rr.x + 72f, rr.y, rr.width * 0.42f, rr.height), string.IsNullOrEmpty(row.name) ? "-" : row.name, cs); // 名前は訳さない
                LocGUI.Label(new Rect(rr.x + 72f + rr.width * 0.42f, rr.y, rr.width * 0.2f, rr.height), $"{row.meters:N0}m", cs);
                string ch = !string.IsNullOrEmpty(row.characterId) && UnlockRules.IsCharacterVisible(row.characterId) ? Loc.Auto(UnlockRules.CharName(row.characterId)) : "";
                string marks = (row.usedAuto ? " AUTO" : "") + (row.usedSprint ? " SPRINT" : "");
                LocGUI.Label(new Rect(rr.x + 72f + rr.width * 0.62f, rr.y, rr.width * 0.38f - 72f, rr.height), ch + marks, UiKit.Label(15f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
            }
            list.End(view, contentH);
        }
        if (UiKit.Button(new Rect(r.x, r.yMax - 38f, 160f, 36f), "更新", 16f, false, true, loadingStage == null) && loadingStage == null) Load(force: true);
        if (UiKit.Button(new Rect(r.xMax - 200f, r.yMax - 38f, 200f, 36f), "参加をやめる", 15f, false, true)) { Leaderboard.Leave(); joinStep = false; }
        if (!string.IsNullOrEmpty(Leaderboard.LastSendError) && pending > 0)
            LocGUI.Label(new Rect(r.x + 170f, r.yMax - 38f, r.width - 380f, 36f), "通信できない時は投稿待ちに残し、後で送ります", UiKit.Label(14f, TextAnchor.MiddleCenter, false, new Color(1f, 0.75f, 0.6f)));
    }
}

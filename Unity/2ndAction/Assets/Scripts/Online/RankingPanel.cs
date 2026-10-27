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
    int seenSentVersion; bool reloadWanted; // 2026-10-27: 送信が終わったら読み直す
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
        seenSentVersion = Leaderboard.SentVersion;
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

    async void Load(bool force, bool afterSend = false)
    {
        var tabs = Tabs(); if (tabs.Count == 0) return;
        string st = tabs[Mathf.Clamp(tab, 0, tabs.Count - 1)];
        if (loadingStage == st) { if (afterSend) reloadWanted = true; return; } // 読み込み中に送信が終わった → 終わってからもう一度
        if (!force && cache.ContainsKey(st) && Time.realtimeSinceStartup - lastLoadAt < 20f) return;
        if (Time.realtimeSinceStartup - lastLoadAt < 2f && force && !afterSend) return; // 連打で API を呼びすぎない(送信の後の読み直しは別)
        loadingStage = st; errorStage = null; lastLoadAt = Time.realtimeSinceStartup;
        try
        {
            var page = await Leaderboard.Backend.Load(st, 50); cache[st] = page;
            RankingDiag.Note("LOAD", $"{Leaderboard.BoardId(st)} total={page.total} me={(page.me != null ? "#" + page.me.rank + " " + page.me.meters + "m" : "none")} player={OnlineServices.ShortId(OnlineServices.PlayerId)} env={OnlineServices.EnvironmentName}");
        }
        catch (System.Exception e)
        {
            errorStage = st; errorText = e.GetType().Name; Debug.LogWarning("[Ranking] load failed: " + e.GetType().Name + ": " + e.Message);
            RankingDiag.Note("LOAD", $"{Leaderboard.BoardId(st)} FAILED {e.GetType().Name}: {e.Message} env={OnlineServices.EnvironmentName}");
        }
        finally
        {
            if (loadingStage == st) loadingStage = null;
            if (reloadWanted && open) { reloadWanted = false; Load(force: true, afterSend: true); }
        }
    }

    void Update()
    {
        if (!open && (GameManager.Instance == null || !GameManager.Instance.HasStarted)) Leaderboard.Tick(); // 2026-10-14: 自動の再送はホームでだけ(ラン中に通信しない)
        // 2026-10-27: 送信が終わった(サーバーが返事をした)ら、ほかのマップの表は古いので捨てて、今のマップを読み直す
        if (Leaderboard.SentVersion != seenSentVersion && !Leaderboard.Sending)
        {
            seenSentVersion = Leaderboard.SentVersion;
            var keep = new List<string>(cache.Keys);
            var tabs = Tabs(); string cur = tabs.Count > 0 ? tabs[Mathf.Clamp(tab, 0, tabs.Count - 1)] : "";
            foreach (var k in keep) if (k != cur) cache.Remove(k);
            if (open && Leaderboard.Joined) { Debug.Log("[Ranking] reloading the board after a send"); Load(force: true, afterSend: true); }
            else cache.Remove(cur);
        }
    }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -1900;
        int padLayer = PadNav.BeginLayer(9);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float sl = safe.xMin / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.yMin / s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.6f));
        float pw = Mathf.Min(900f, w - sl - sr - 24f), ph = Mathf.Min(h - st - sb - 24f, 680f);
        var panel = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(panel, 0.95f);
        LocGUI.Label(new Rect(panel.x + 28f, panel.y + 14f, panel.width - 120f, 44f), "ランキング", UiKit.Label(28f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 2026-10-14: 開発版は公開のランキング(production)へは送らない。今の環境を必ず見せる(開発者向けなので訳さない)
        GUI.Label(new Rect(panel.x + 200f, panel.y + 14f, panel.width - 300f, 44f), $"DEV  UGS env: {UgsEnvironment.Name}  (not the public ranking)" + (Leaderboard.HeldCount > 0 ? $"  held {Leaderboard.HeldCount}" : ""),
            UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.6f, 0.4f)));
#endif
        if (UiIcons.CloseButton(new Rect(panel.xMax - 70f, panel.y + 14f, 50f, 46f))) Close();

        var body = new Rect(panel.x + 24f, panel.y + 70f, panel.width - 48f, panel.height - 70f - 80f);
        if (!Leaderboard.Joined) DrawJoin(body);
        else DrawBoard(body);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DrawDiag(panel, body);
#endif

        if (UiKit.Button(new Rect(panel.center.x - 110f, panel.yMax - 68f, 220f, 52f), "閉じる", 22f, true)) Close();

        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へタップを通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
        PadNav.EndLayer(padLayer);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 2026-10-27: 開発版だけの診断(APK 387 の実機で記録が載らない件の切り分け)。秘密・トークンは出さない。PlayerId は先頭6文字
    bool diagOn = true;
    void DrawDiag(Rect panel, Rect body)
    {
        if (GUI.Button(new Rect(panel.xMax - 150f, panel.y + 18f, 70f, 36f), diagOn ? "DIAG ON" : "DIAG")) diagOn = !diagOn;
        if (!diagOn) return;
        var tabs = Tabs();
        string st = tabs.Count > 0 ? tabs[Mathf.Clamp(tab, 0, tabs.Count - 1)] : "-";
        bool init = Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized;
        bool signed = init && Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn;
        string pid = signed ? OnlineServices.ShortId(Unity.Services.Authentication.AuthenticationService.Instance.PlayerId) : "-";
        var vb = Leaderboard.VerifiedBest(st);
        cache.TryGetValue(st, out var page);
        string[] l =
        {
            $"UGS env={UgsEnvironment.Name}  services={(init ? "ok" : Unity.Services.Core.UnityServices.State.ToString())}  auth={(signed ? "signed in" : "not signed in")} {pid}  online={OnlineServices.Status} {OnlineServices.ErrorDetail}",
            $"board={Leaderboard.BoardId(st)}  joined={Leaderboard.Joined}  pending={Leaderboard.PendingCount} held={Leaderboard.HeldCount} sending={Leaderboard.Sending}  device best(ranking)={(vb != null ? vb.meters + "m" : "none")}  shown me={(page?.me != null ? "#" + page.me.rank + " " + page.me.meters + "m" : page != null ? "none (total " + page.total + ")" : "not loaded")}",
            $"test data={(SaveProfile.IsTest ? "ON" : "off")}  invincible={(GameManager.Instance != null && GameManager.Instance.InvincibleMode ? "ON (runs are not recorded)" : "off")}",
            "last commit: " + Short(RankingDiag.LastCommit),
            "last send:   " + Short(RankingDiag.LastSubmit) + (string.IsNullOrEmpty(Leaderboard.LastSendError) ? "" : "  lastError=" + Leaderboard.LastSendError),
            "last load:   " + Short(RankingDiag.LastLoad),
        };
        var box = new Rect(body.x, body.yMax - 44f - 152f, body.width, 152f); // 下の「更新」などのボタンの上
        UiKit.Fill(box, new Color(0f, 0f, 0f, 0.82f));
        var stl = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
        stl.normal.textColor = new Color(0.6f, 1f, 0.7f);
        for (int i = 0; i < l.Length; i++) GUI.Label(new Rect(box.x + 6f, box.y + 4f + i * 24f, box.width - 12f, 24f), l[i], stl);
    }
    static string Short(string s) => string.IsNullOrEmpty(s) ? "(none yet)" : s.Length > 150 ? s.Substring(0, 150) + "…" : s;
#endif

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
        string stage = tabs[tab];
        // 2026-10-14: ランキングはソロだけ(CPU サポート・マルチのランは載らない)
        LocGUI.Label(new Rect(r.x, r.y + 52f, r.width, 36f), "ソロのランだけ(CPU サポート・マルチのランは対象外)", UiKit.Label(15f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
        string st = stage;
        float y = r.y + 54f + 44f;
        cache.TryGetValue(st, out var page);
        var info = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        // 自分(上に固定)
        // 2026-10-13: 「あなた」の行は実際の状態から(読み込み中 / 読み込み失敗 / このマップの送信待ち / 順位に出ていない / 記録なし)。
        // 以前は送信待ちが無くても「投稿待ち」相当の文が出ていた。読み込み失敗と送信待ちが同時の時は、右の欄に送信待ちを別に出す
        var best = Leaderboard.VerifiedBest(stage);
        int pendingHere = Leaderboard.PendingCountFor(stage);
        bool loadingHere = loadingStage == st, failedHere = errorStage == st;
        string meLine;
        if (page != null && page.me != null) meLine = Loc.Auto("あなた") + $": #{page.me.rank}  {page.me.meters:N0}m"; // 順位は # で(言語ごとの順位の言い方に頼らない)
        else if (loadingHere) meLine = Loc.Auto("あなた: 読み込み中…");
        else if (failedHere) meLine = Loc.Auto("あなた: 順位を読み込めませんでした");
        else if (pendingHere > 0) meLine = Loc.Auto("あなた: 新しい記録を送信待ちです");
        else if (best != null) meLine = Loc.Auto("あなた: 表示中の順位に記録がありません");
        else meLine = Loc.Auto("あなた: まだ記録がありません");
        LocGUI.Label(new Rect(r.x, y, r.width * 0.6f, 30f), meLine, UiKit.Label(19f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.4f)));
        int pending = Leaderboard.PendingCount;
        if (pending > 0)
        {
            string ps = Leaderboard.Sending ? Loc.F("送信中… {0}件", pending) : !string.IsNullOrEmpty(Leaderboard.LastSendError) ? Loc.F("送信失敗・再送待ち {0}件", pending) : Loc.F("送信待ち {0}件", pending);
            LocGUI.Label(new Rect(r.x + r.width * 0.6f, y, r.width * 0.4f - 130f, 30f), ps, info);
            if (UiKit.Button(new Rect(r.xMax - 120f, y - 4f, 120f, 38f), Leaderboard.Sending ? "送信中…" : "再送", 16f, false, true, !Leaderboard.Sending) && !Leaderboard.Sending) _ = Leaderboard.FlushAsync("manual");
        }
        y += 38f;
        // 前に読み込んだ表を出している時に、最新の読み込みに失敗した(表は前回のもの)
        if (failedHere && page != null) { LocGUI.Label(new Rect(r.x, y - 8f, r.width, 22f), "最新の順位を読み込めませんでした(前回の表示です)", UiKit.Label(14f, TextAnchor.MiddleLeft, false, new Color(1f, 0.7f, 0.6f))); y += 16f; }

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
                string marks = (row.usedAuto ? " AUTO" : "") + (row.usedSprint ? " 疾走" : "");
                LocGUI.Label(new Rect(rr.x + 72f + rr.width * 0.62f, rr.y, rr.width * 0.38f - 72f, rr.height), ch + marks, UiKit.Label(15f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
            }
            list.End(view, contentH);
        }
        if (UiKit.Button(new Rect(r.x, r.yMax - 38f, 160f, 36f), "更新", 16f, false, true, loadingStage == null) && loadingStage == null) Load(force: true);
        if (UiKit.Button(new Rect(r.xMax - 200f, r.yMax - 38f, 200f, 36f), "記録の送信をやめる", 15f, false, true)) { Leaderboard.Leave(); joinStep = false; }
        if (!string.IsNullOrEmpty(Leaderboard.LastSendError) && pending > 0)
            LocGUI.Label(new Rect(r.x + 170f, r.yMax - 38f, r.width - 380f, 36f), "通信できない時は投稿待ちに残し、後で送ります", UiKit.Label(14f, TextAnchor.MiddleCenter, false, new Color(1f, 0.75f, 0.6f)));
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 開発用の簡易マルチプレイUI(正式UIは後のPhase)。
//  - Home画面右上の「LOCAL MULTI」ボタン → HOST/JOINパネル(自分のIP表示、HOSTのIP/ポート入力)
//  - Run中: 画面上部に接続状態、相手プレイヤーの頭上に「P2」等の名前
//  - 切断時: 「MULTIPLAYER CONNECTION LOST」を表示し、Run中ならHomeへ戻るボタンを出す
// GameManagerのOnGUIより手前に描き(GUI.depth)、パネル表示中はHomeの部屋のタップを止める
// (GameManager側がBlocksHomeInputを参照)。
public class NetDebugUI : MonoBehaviour
{
    const string LastHostIpKey = "net.lastHostIp";
    const string LastPortKey = "net.lastPort";

    static NetDebugUI instance;
    public static bool PanelOpen { get; private set; }
    public static bool BlocksHomeInput => PanelOpen || (instance != null && instance.BannerVisible);
    // 2026-10-01: ホームの「マルチ」ボタンはGameManagerが右上(所持MILEの下、設定の隣)に描き、ここを開く。
    public static string HomeButtonLabel => NetSession.IsActive ? $"マルチ {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}" : "マルチ";
    public static void OpenPanel() { if (!(instance != null && instance.BannerVisible)) PanelOpen = true; LanSecurity.Prewarm(); } // 2026-10-19: LOCAL の暗号化の証明書を先に作る
    public static void ClosePanel() { PanelOpen = false; }

    string hostIpInput = "";
    string portInput = "";
    List<string> localIps = new List<string>();
    float ipRefreshTimer;

    string toastText = "";
    float toastUntil;

    GUIStyle labelStyle, smallStyle, titleStyle, buttonStyle, fieldStyle, tagStyle, bannerStyle;
    int stylesForHeight = -1;

    bool BannerVisible => NetSession.Instance != null && NetSession.Instance.ConnectionLostPending;

    public static void Toast(string text)
    {
        if (instance == null) return;
        instance.toastText = text;
        instance.toastUntil = Time.unscaledTime + 2.5f;
        NetSession.Log("UI: " + text);
    }

    void Awake()
    {
        instance = this;
        hostIpInput = SaveStore.GetString(LastHostIpKey, "192.168.");
        portInput = SaveStore.GetInt(LastPortKey, NetSession.DefaultPort).ToString();
    }

    void Update()
    {
        OnlineServices.Tick(); // ONLINE の部屋の状態(開始したら IN PROGRESS)。部屋が無ければ何もしない
        if (!PanelOpen) return;
        ipRefreshTimer -= Time.unscaledDeltaTime;
        if (ipRefreshTimer <= 0f)
        {
            localIps = NetSession.GetLocalIPv4Addresses();
            ipRefreshTimer = 3f;
        }
    }

    // アプリを閉じる時: HOST なら部屋を消す(間に合わなければ、HOST の生存確認が途切れて Lobby の仕組みで一覧から消える)
    void OnApplicationQuit()
    {
        if (OnlineServices.InSession) _ = OnlineServices.LeaveAsync("app quit");
    }

    float Scale => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);

    void EnsureStyles()
    {
        if (stylesForHeight == Screen.height && labelStyle != null) return;
        stylesForHeight = Screen.height;
        // 2026-10-22(画面側の完成度向上): 文字の色は UiTheme(本文/補足/見出し)。補足は少し大きく・行間を空けて読みやすく
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
        labelStyle.normal.textColor = UiTheme.Text;
        smallStyle = new GUIStyle(labelStyle) { fontSize = 17 };
        smallStyle.normal.textColor = UiTheme.TextSubBlue;
        titleStyle = new GUIStyle(labelStyle) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        titleStyle.normal.textColor = UiTheme.GoldText;
        buttonStyle = UiKit.MakeButtonStyle(20); // 2026-10-09(依頼G-2): 他の画面と同じ 濃紺+金の縁
        fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        tagStyle = new GUIStyle(labelStyle) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        tagStyle.normal.textColor = new Color(0.55f, 0.95f, 1f);
        bannerStyle = new GUIStyle(labelStyle) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bannerStyle.normal.textColor = new Color(1f, 0.55f, 0.5f);
    }

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        GUI.depth = -1000;
        EnsureStyles();
        PadNav.BeginLayoutButtons();
        int padLayer = PadNav.BeginLayer(PanelOpen ? 8 : gm.HasStarted ? PadNav.HudLayer : 0); // マルチのパネルはホームより手前、ラン中の物は HUD(2026-10-06)

        float s = Scale;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float safeRight = (Screen.width - safe.xMax) / s, safeTop = (Screen.height - safe.yMax) / s;

        if (!gm.HasStarted)
        {
            // カード合成改修(2026-09-26) - 合成/デッキ編集/キャラ選択などの全画面
            // オーバーレイ中は、その画面と無関係なLOCAL MULTIボタンを出さない。
            // 開くボタンはGameManagerのホーム右上(OpenPanel)。ここではパネルだけ描く。
            // 2026-10-27: 切断の画面が出ている間はパネルを描かない(パネルが先にクリックを使ってしまい、切断の画面の OK が押せなかった)
            if (PanelOpen && !BannerVisible) DrawPanel(w, h);
        }
        else if (NetSession.IsActive || NetRunLauncher.IsMultiplayerRun)
        {
            if (!NetSession.IsActive) DrawRunHud(w, safeTop); // 2026-11-05: 参加者の欄は NetMatch.Hud(HP/状態/FRONT)へ。ここは切断の表示だけ(いつも出す)
            GUI.matrix = prevMatrix;
            DrawRemoteNameTags();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        if (BannerVisible) DrawConnectionLostBanner(w, h, gm);
        if (!string.IsNullOrEmpty(toastText) && Time.unscaledTime < toastUntil)
        {
            Rect r = new Rect(w * 0.5f - 260f, h * 0.18f, 520f, 56f);
            // 2026-10-29: 部屋の画面(パネル)を開いている時は、パネルの見出し(部屋の名前)に重ならないよう、パネルの上(入らなければ下)へ
            if (!gm.HasStarted && PanelOpen)
            {
                float ph = Mathf.Min(700f, h - 32f), top = (h - ph) * 0.5f, bottom = top + ph;
                float tw = Mathf.Min(520f, w - 24f);
                if (top - 62f >= safeTop + 4f) r = new Rect((w - tw) * 0.5f, top - 62f, tw, 56f);
                else if (bottom + 62f <= h) r = new Rect((w - tw) * 0.5f, bottom + 6f, tw, 56f);
                else r = new Rect((w - tw) * 0.5f, bottom - 210f, tw, 56f); // 横画面など上下に余白が無い: 下のボタンの上の空いた所
            }
            GUI.Box(r, "");
            LocGUI.Label(r, toastText, bannerStyle);
        }

        GUI.matrix = prevMatrix;
        PadNav.EndLayer(padLayer);
    }

    // ===================================================================== //
    // 2026-10-05: 正式な流れ MULTIPLAYER → LOCAL PLAY → CO-OP / VERSUS → CREATE GAME / FIND GAME(IP 入力なし)。
    // IP を直接入れる従来の画面は ADVANCED(DEBUG)に残す。部屋の発見はゲームの同期と別(LanDiscovery)。
    // ===================================================================== //
    // 2026-10-07 ONLINE PLAY: MULTIPLAYER → ONLINE PLAY → CO-OP / VERSUS → CREATE GAME / FIND GAME(インターネット越し、Relay)。
    // Mode/Choose/Room は LOCAL と同じ画面を online の印で切り替えて使う。探す画面と「使えない」画面は ONLINE 専用。
    public enum MultiScreen { Top, Mode, Choose, Find, Room, Advanced, OnlineFind, OnlineUnavailable }
    MultiScreen screen = MultiScreen.Top;
    public static MultiScreen CurrentScreen => instance != null ? instance.screen : MultiScreen.Top; // 確認用
    bool online;
    public static bool OnlineFlow => instance != null && instance.online;
    static string L(string s) => Loc.Auto(s); // GUILayout の文は LocGUI を通らないのでここで訳す
    Vector2 panelScroll; float panelContentH, panelPressY, panelLastY; bool panelPressed, panelDragging; MultiScreen lastPanelScreen = (MultiScreen)(-1);
    // 相手のキャラの名前。自分がまだ解放していないキャラは名前を出さない(2026-10-08)
    static string CharLabel(string id) => !string.IsNullOrEmpty(id) && UnlockRules.IsCharacterVisible(id) ? Loc.Auto(UnlockRules.CharName(id)) : "";
    string findMessage = "";
    float findMessageUntil;
    string joiningRoom = "";
    float joinStartedAt;


    void DrawPanel(float w, float h)
    {
        // 画面の自動の切り替えは Layout の時だけ(Layout と Repaint で描く物が変わると GUILayout が例外を出す)
        if (Event.current.type == EventType.Layout) AutoScreen();

        float pw = Mathf.Min(640f, w - 32f), ph = Mathf.Min(700f, h - 32f);
        Rect panel = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        // 後ろのホーム画面(ロゴ/CONTINUE)が透けて文字が読みにくいので、画面を暗くしてパネルの裏を濃い色で塗る
        Color keep = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        GUI.color = keep;
        // 2026-10-22: 開発時の画面(Unity 既定の箱)だった → 他の画面と同じ飾り枠の板(OrnateUi)。中身は枠の飾りの内側に
        OrnateUi.DrawPanel(panel, 0.96f);
        // 2026-10-08: 長い言語で文が増えても下のボタンへ届くよう、中身全体を指でスクロールできるようにする(収まる時は今までどおり)
        Rect area = new Rect(panel.x + 34f, panel.y + 28f, pw - 68f, ph - 56f);
        float max = Mathf.Max(0f, panelContentH - area.height);
        var ev = Event.current;
        if (max > 0.5f)
        {
            if (ev.type == EventType.MouseDown && area.Contains(ev.mousePosition)) { panelPressed = true; panelDragging = false; panelPressY = panelLastY = ev.mousePosition.y; }
            else if (ev.type == EventType.MouseDrag && panelPressed)
            {
                if (!panelDragging && Mathf.Abs(ev.mousePosition.y - panelPressY) > UiScroll.DragThreshold) panelDragging = true;
                if (panelDragging) { panelScroll.y = Mathf.Clamp(panelScroll.y - (ev.mousePosition.y - panelLastY), 0f, max); panelLastY = ev.mousePosition.y; GUIUtility.hotControl = 0; ev.Use(); }
            }
            else if (ev.type == EventType.MouseUp) { if (panelDragging) ev.Use(); panelPressed = panelDragging = false; }
        }
        else panelScroll.y = 0f;
        if (screen != lastPanelScreen) { lastPanelScreen = screen; panelScroll = Vector2.zero; }
        GUILayout.BeginArea(area);
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, false, GUIStyle.none, GUIStyle.none, GUIStyle.none, GUILayout.Height(area.height));
        GUILayout.BeginVertical(GUILayout.MinHeight(area.height - 2f));
        switch (screen)
        {
            case MultiScreen.Top: DrawTop(); break;
            case MultiScreen.Mode: DrawMode(); break;
            case MultiScreen.Choose: DrawChoose(); break;
            case MultiScreen.Find: DrawFind(); break;
            case MultiScreen.Room: DrawRoom(); break;
            case MultiScreen.OnlineFind: DrawOnlineFind(); break;
            case MultiScreen.OnlineUnavailable: DrawOnlineUnavailable(); break;
            default: DrawAdvanced(); break;
        }
        GUILayout.EndVertical();
        if (ev.type == EventType.Repaint) panelContentH = GUILayoutUtility.GetLastRect().height;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (max > 1f)
        {
            float barH = Mathf.Max(28f, area.height * area.height / panelContentH);
            float by = area.y + (area.height - barH) * (panelScroll.y / max);
            Color kc = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f, 0.7f);
            GUI.DrawTexture(new Rect(area.xMax + 8f, by, 5f, barH), Texture2D.whiteTexture);
            GUI.color = kc;
        }
        ConsumePointer(panel);
    }

    void AutoScreen()
    {
        // 接続中は待機室(部屋)の画面へ(ONLINE は Session の作成/参加が終わるまで待つ)
        if (NetSession.IsActive && screen != MultiScreen.Advanced && !OnlineServices.Busy) screen = MultiScreen.Room;
        else if (!NetSession.IsActive && screen == MultiScreen.Room && !OnlineServices.Busy) { screen = MultiScreen.Choose; NoteLeaveFinished(); }
        if (!NetSession.IsActive && leavingAt > 0f && !OnlineServices.Busy) NoteLeaveFinished(); // 選択の画面に居る間に終わった時も
        // ONLINE: サービスが使えない → 専用の画面
        if (online && OnlineServices.Status == OnlineServices.State.Unavailable && (screen == MultiScreen.Mode || screen == MultiScreen.Choose || screen == MultiScreen.OnlineFind)) screen = MultiScreen.OnlineUnavailable;
    }

    void Go(MultiScreen s)
    {
        if (screen == MultiScreen.Find && s != MultiScreen.Find) LanDiscovery.StopDiscovery("back");
        // 音の再設計(2026-10-06): 画面を進む=決定 / 最初の画面へ戻る=戻る
        if (AudioManager.Instance != null && s != screen) AudioManager.Instance.PlaySe(s == MultiScreen.Top ? SeId.Cancel : SeId.Decide);
        screen = s;
        if (s == MultiScreen.Top) online = false;
        if (s == MultiScreen.Find) { findMessage = ""; LanDiscovery.StartDiscovery(); }
        if (s == MultiScreen.OnlineFind) { findMessage = ""; OnlineServices.Rooms.Clear(); _ = OnlineServices.QueryAsync(NetRunLauncher.SelectedMode); }
    }

    void ClosePanelFromUi()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiClose);
        if (screen == MultiScreen.Find) LanDiscovery.StopDiscovery("closed");
        PanelOpen = false;
        if (!NetSession.IsActive && !OnlineServices.Busy) { screen = MultiScreen.Top; online = false; }
    }

    void DrawTop()
    {
        TitleWithIcon("MULTIPLAYER");
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("LOCAL PLAY", buttonStyle, GUILayout.Height(70f)))) { online = false; Go(MultiScreen.Mode); }
        GUILayout.Label(L("同じWi-Fi、またはスマホのテザリングにつないだ端末どうしで遊びます(最大" + NetSession.MaxPlayers + "人)。"), smallStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("ONLINE PLAY", buttonStyle, GUILayout.Height(70f)))) StartOnline();
        GUILayout.Label(L("インターネットで、離れた場所の人と遊びます(最大" + NetSession.MaxPlayers + "人)。"), smallStyle);
        GUILayout.FlexibleSpace();
        // 2026-10-22: IP を直接入れる画面は開発版だけ(製品版では入口ごと出さない。以前は製品版にも出ていた)
        if (Debug.isDebugBuild)
        {
            if (PadNav.LayoutButton(GUILayout.Button(L("ADVANCED(IPを直接入力・開発用)"), buttonStyle, GUILayout.Height(40f)))) Go(MultiScreen.Advanced);
            GUILayout.Space(6f);
        }
        if (PadNav.LayoutButton(GUILayout.Button(L("閉じる"), buttonStyle, GUILayout.Height(48f)))) ClosePanelFromUi();
    }

    // 見出し(2026-10-22): 左にホームの「マルチ」と同じアイコン
    void TitleWithIcon(string title)
    {
        GUILayout.BeginHorizontal();
        var icon = UiIcons.Home("multi");
        if (icon != null)
        {
            Rect ir = GUILayoutUtility.GetRect(40f, 40f, GUILayout.Width(40f), GUILayout.Height(40f));
            if (Event.current.type == EventType.Repaint) UiIcons.Draw(ir, icon);
            GUILayout.Space(8f);
        }
        GUILayout.Label(title, titleStyle, GUILayout.Height(40f));
        GUILayout.EndHorizontal();
    }

    // ONLINE PLAY を選んだ時に初めて UGS を初期化する(LOCAL/シングルでは一切使わない)
    void StartOnline()
    {
        online = true;
        Go(MultiScreen.Mode);
        _ = OnlineServices.EnsureReadyAsync();
    }

    void DrawMode()
    {
        if (online)
        {
            if (OnlineServices.Status != OnlineServices.State.Ready)
            {
                GUILayout.Label("ONLINE PLAY", titleStyle);
                GUILayout.Space(30f);
                GUILayout.Label(L("オンラインサービスに接続しています…"), labelStyle);
                GUILayout.FlexibleSpace();
                if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Top);
                return;
            }
        }
        GUILayout.Label(online ? "ONLINE PLAY" : "LOCAL PLAY", titleStyle);
        GUILayout.Label(L("モードを選んでください"), labelStyle);
        GUILayout.Space(8f);
        if (PadNav.LayoutButton(GUILayout.Button("CO-OP", buttonStyle, GUILayout.Height(64f)))) { NetRunLauncher.SelectedMode = MultiplayerGameMode.Coop; Go(MultiScreen.Choose); }
        GUILayout.Label(L(Loc.Reviewed(LocPendingReview.CoopRuleDesc, LocPendingReview.CoopRuleDescOld)), smallStyle); // 2026-11-05: 長押しで FRONT から HP
        if (Loc.Ready(SupportDeparturePanel.MileShareNote)) GUILayout.Label(L(SupportDeparturePanel.MileShareNote), smallStyle); // 2026-10-15: 協力は獲得 MILE を参加人数で分配(レビュー後に各言語)
        GUILayout.Space(8f);
        if (PadNav.LayoutButton(GUILayout.Button("VERSUS", buttonStyle, GUILayout.Height(64f)))) { NetRunLauncher.SelectedMode = MultiplayerGameMode.Versus; Go(MultiScreen.Choose); }
        GUILayout.Label(L(Loc.Reviewed(LocPendingReview.VersusRuleDesc, LocPendingReview.VersusRuleDescOld)), smallStyle); // 2026-11-05: 死神の乱入時間と MILE ボーナス
        if (LocPendingReview.Pend(LocPendingReview.VersusTakeHomeNote) != null) GUILayout.Label(L(LocPendingReview.VersusTakeHomeNote), smallStyle);
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Top);
    }

    string createMessage = "";
    float createMessageUntil;

    void DrawChoose()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label((online ? "ONLINE PLAY · " : "LOCAL PLAY · ") + m, titleStyle);
        GUILayout.Space(10f);
        bool busy = online && OnlineServices.Busy;
        if (NetRunLauncher.SelectedMode == MultiplayerGameMode.Versus) DrawReaperTimeButton(busy); // 2026-11-05: 部屋を作る前に選ぶ(作った後も出発まで変えられる)
        GUI.enabled = !busy;
        // 2026-11-24(マスター依頼): ボタンの文言を動作に合わせる(CREATE GAME → 部屋を作成 / FIND GAME → 部屋に参加)。訳は LocRoomUi(エステルの確認の前は前の文)
        if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(LocRoomUi.CreateRoom)), buttonStyle, GUILayout.Height(70f)))) { if (online) CreateOnlineGame(); else CreateGame(); }
        GUILayout.Label(L(LocRoomUi.T(online ? LocRoomUi.CreateDescOnline : LocRoomUi.CreateDescLocal)), smallStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(LocRoomUi.JoinRoom)), buttonStyle, GUILayout.Height(70f)))) Go(online ? MultiScreen.OnlineFind : MultiScreen.Find);
        GUILayout.Label(L(online ? "オンラインの部屋を探して参加します。" : "近くの部屋を探して参加します(IPの入力は不要)。"), smallStyle);
        GUI.enabled = true;
        if (busy) GUILayout.Label(L("部屋を作っています…"), labelStyle);
        if (Time.unscaledTime < createMessageUntil) GUILayout.Label(createMessage, bannerStyle);
        if (!online && !string.IsNullOrEmpty(NetSession.Instance.StatusText)) GUILayout.Label(L(NetSession.Instance.StatusText), smallStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = !busy;
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Mode);
        GUI.enabled = true;
    }

    // VERSUS の死神の乱入時間(2026-11-05)。押すたびに 5 → 10 → 20 → 30 → なし → 5 …(値は MultiplayerRules)
    public static string ReaperTimeLabel(int minutes) => minutes > 0 ? $"REAPER TIME: {minutes} MIN" : "REAPER TIME: " + Loc.T("なし");
    void DrawReaperTimeButton(bool disabled)
    {
        bool was = GUI.enabled;
        GUI.enabled = was && !disabled;
        if (PadNav.LayoutButton(GUILayout.Button(ReaperTimeLabel(NetRunLauncher.SelectedReaperMinutes) + "  ▶", buttonStyle, GUILayout.Height(52f))))
        {
            NetRunLauncher.CycleReaperMinutes();
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        }
        GUI.enabled = was;
        string help = LocPendingReview.Pend(LocPendingReview.ReaperTimeHelp);
        if (help != null) GUILayout.Label(L(help), smallStyle);
    }

    void CreateGame()
    {
        if (NetSession.IsActive) return;
        if (NetSession.Instance.StartHost(NetSession.DefaultPort))
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            LanDiscovery.StartAdvertising();
            screen = MultiScreen.Room;
        }
    }

    async void CreateOnlineGame()
    {
        if (NetSession.IsActive || OnlineServices.Busy) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        bool ok = await OnlineServices.CreateAsync(NetRunLauncher.SelectedMode, LanDiscovery.RoomName);
        if (ok) { screen = MultiScreen.Room; return; }
        if (OnlineServices.Status == OnlineServices.State.Unavailable) { screen = MultiScreen.OnlineUnavailable; return; }
        createMessage = L("CONNECTION FAILED") + "\n" + L("部屋を作れませんでした。通信を確認して、もう一度お試しください");
        createMessageUntil = Time.unscaledTime + 5f;
    }

    // ---- ONLINE: 部屋を探す。一覧の自動更新は10秒ごと(クラウドの API なので LAN より遅く)。手動の再検索は2秒あける
    Vector2 onlineScroll;
    string joiningOnline = "";
    void DrawOnlineFind()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label(L(LocRoomUi.T(LocRoomUi.JoinRoom)) + " · ONLINE", titleStyle);
        GUILayout.Label(L("オンラインの部屋") + $"({m})", smallStyle);
        GUILayout.Space(6f);
        bool joining = OnlineServices.Busy;
        if (!joining && !OnlineServices.Querying && Time.realtimeSinceStartup - OnlineServices.LastQueryAt > OnlineServices.AutoRefreshSeconds)
            _ = OnlineServices.QueryAsync(NetRunLauncher.SelectedMode);
        if (!string.IsNullOrEmpty(OnlineServices.LastJoinFailure) && !joining)
        {
            string why = OnlineServices.LastJoinFailure;
            findMessage = why == "CONNECTION FAILED" ? L("CONNECTION FAILED") + "\n" + L("接続できませんでした。もう一度お試しください") : L("CONNECTION FAILED") + ": " + why;
            findMessageUntil = Time.unscaledTime + 5f;
            OnlineServices.ClearJoinFailure();
            _ = OnlineServices.QueryAsync(NetRunLauncher.SelectedMode); // 一覧を新しくする
        }
        if (Time.unscaledTime < findMessageUntil) GUILayout.Label(findMessage, bannerStyle);
        if (joining) GUILayout.Label(L("接続中…") + " " + joiningOnline, labelStyle);

        var rooms = OnlineServices.Rooms;
        if (rooms.Count == 0)
        {
            GUILayout.Space(20f);
            bool searching = OnlineServices.Querying || OnlineServices.Queries == 0;
            GUILayout.Label(L(searching ? "オンラインの部屋を探しています…" : "ゲームが見つかりません"), titleStyle);
            if (!searching) GUILayout.Label(L(LocRoomUi.T(LocRoomUi.EmptyOnline)), smallStyle);
        }
        else
        {
            onlineScroll = GUILayout.BeginScrollView(onlineScroll, GUILayout.Height(330f));
            foreach (var r in rooms)
            {
                GUILayout.BeginHorizontal(UiKit.RowBox);
                GUILayout.BeginVertical();
                GUILayout.Label(r.name, labelStyle);
                GUILayout.Label($"{(r.GameMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS")}   {r.players}/{r.maxPlayers}   {r.StateLabel}", smallStyle);
                GUILayout.EndVertical();
                GUI.enabled = r.Joinable && !joining && !NetSession.IsActive;
                if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(LocRoomUi.JoinRoom)), buttonStyle, GUILayout.Width(200f), GUILayout.Height(56f)))) JoinOnline(r);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        GUILayout.FlexibleSpace();
        GUI.enabled = !joining && !OnlineServices.Querying && Time.realtimeSinceStartup - OnlineServices.LastQueryAt >= OnlineServices.MinQueryInterval;
        if (PadNav.LayoutButton(GUILayout.Button(L("再検索"), buttonStyle, GUILayout.Height(48f)))) _ = OnlineServices.QueryAsync(NetRunLauncher.SelectedMode);
        GUI.enabled = !joining;
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Choose);
        GUI.enabled = true;
    }

    async void JoinOnline(OnlineServices.Room r)
    {
        if (r == null || !r.Joinable || OnlineServices.Busy) return;
        joiningOnline = r.name;
        bool ok = await OnlineServices.JoinAsync(r);
        if (ok) screen = MultiScreen.Room;
    }

    void DrawOnlineUnavailable()
    {
        GUILayout.Label("ONLINE SERVICE UNAVAILABLE", titleStyle);
        GUILayout.Space(10f);
        GUILayout.Label(L("通信を確認してください"), labelStyle);
        GUILayout.Label(L("インターネットにつながっているか確認して、もう一度お試しください。LOCAL PLAY(同じWi-Fi)はこのまま遊べます。"), smallStyle);
        if (Debug.isDebugBuild && !string.IsNullOrEmpty(OnlineServices.ErrorDetail)) GUILayout.Label("DEV: " + OnlineServices.ErrorDetail, smallStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = OnlineServices.Status != OnlineServices.State.Initializing;
        if (PadNav.LayoutButton(GUILayout.Button("RETRY", buttonStyle, GUILayout.Height(60f)))) { OnlineServices.ResetError(); StartOnline(); }
        GUI.enabled = true;
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Top);
    }

    Vector2 roomScroll;
    void DrawFind()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label(L(LocRoomUi.T(LocRoomUi.JoinRoom)), titleStyle);
        GUILayout.Label(L("近くの部屋") + $"({m})", smallStyle);
        GUILayout.Space(6f);
        bool joining = NetSession.JoinPending;
        if (!string.IsNullOrEmpty(NetSession.LastJoinFailure) && !joining)
        {
            findMessage = NetSession.LastJoinFailure == "CONNECTION FAILED" ? "CONNECTION FAILED\n" + L("接続できませんでした。同じWi-Fiか確認して、もう一度お試しください") : "CONNECTION FAILED: " + NetSession.LastJoinFailure;
            findMessageUntil = Time.unscaledTime + 4f;
            NetSession.ClearJoinFailure();
        }
        if (Time.unscaledTime < findMessageUntil) GUILayout.Label(findMessage, bannerStyle);
        if (joining) GUILayout.Label(L("接続中…") + " " + joiningRoom, labelStyle);

        var rooms = LanDiscovery.Rooms;
        if (rooms.Count == 0)
        {
            GUILayout.Space(20f);
            bool searching = Time.unscaledTime - LanDiscovery.DiscoveryStartedAt < 4f;
            GUILayout.Label(L(searching ? "近くの部屋を探しています…" : "ゲームが見つかりません"), titleStyle);
            if (!searching) GUILayout.Label(L(LocRoomUi.T(LocRoomUi.EmptyLocal)), smallStyle);
        }
        else
        {
            roomScroll = GUILayout.BeginScrollView(roomScroll, GUILayout.Height(330f));
            // 選んだモードの部屋を上に
            var sorted = new List<LanRoom>(rooms);
            sorted.Sort((a, b) => (a.mode == NetRunLauncher.SelectedMode ? 0 : 1).CompareTo(b.mode == NetRunLauncher.SelectedMode ? 0 : 1));
            foreach (var r in sorted)
            {
                GUILayout.BeginHorizontal(UiKit.RowBox);
                GUILayout.BeginVertical();
                GUILayout.Label(r.roomName, labelStyle);
                GUILayout.Label($"{(r.mode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS")}   {r.players}/{r.maxPlayers}   {r.StateLabel}", smallStyle);
                GUILayout.EndVertical();
                GUI.enabled = r.Joinable && !joining && !NetSession.IsActive;
                if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(LocRoomUi.JoinRoom)), buttonStyle, GUILayout.Width(200f), GUILayout.Height(56f)))) JoinRoom(r);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button(L("再検索"), buttonStyle, GUILayout.Height(48f)))) LanDiscovery.Rescan();
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Choose);
    }

    void JoinRoom(LanRoom r)
    {
        if (r == null || !r.Joinable) return;
        joiningRoom = r.roomName;
        joinStartedAt = Time.unscaledTime;
        Debug.Log($"[LAN] Join requested {r.roomId} '{r.roomName}' {r.address}:{r.port}");
        NetSession.Instance.StartClient(r.address, r.port); // 既存の JOIN の経路へ(別のネットワークの仕組みは作らない)
    }

    void DrawRoom()
    {
        bool host = NetSession.IsHost;
        bool onl = NetSession.Connection == ConnectionType.Online;
        string m = (host ? NetRunLauncher.SelectedMode : NetRunLauncher.ActiveMode) == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        string roomName = onl && OnlineServices.Current != null ? OnlineServices.Current.Name : host ? LanDiscovery.RoomName : L("参加中の部屋");
        GUILayout.Label(roomName, titleStyle);
        GUILayout.Label((onl ? "ONLINE  " : "LOCAL  ") + (host ? $"MODE: {m}" + L("(HOSTが決めます)") : L("MODE: HOSTが決めます(Run開始時に自動で揃います)")), smallStyle);
        // 2026-11-05: VERSUS の死神の乱入時間。HOST は出発前まで変えられる / JOIN は見るだけ(HOST の NetPlayer の部屋の設定)
        if (host) { if (NetRunLauncher.SelectedMode == MultiplayerGameMode.Versus) DrawReaperTimeButton(false); }
        else if (NetPlayer.TryGetRoomRules(out bool rv, out int rmin))
        {
            GUILayout.Label($"MODE: {(rv ? "VERSUS" : "CO-OP")}", smallStyle);
            if (rv) GUILayout.Label(ReaperTimeLabel(rmin), labelStyle);
        }
        GUILayout.Space(8f);
        GUILayout.Label($"WAITING FOR PLAYERS {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}", titleStyle);
        var sb = new StringBuilder();
        NetCombat.RememberPlayerNumbers(); // 2026-10-29: 抜けた人の番号を通知で出せるように
        foreach (NetPlayer p in NetPlayer.All)
            sb.Append($"{p.Tag}  {(p.OwnerClientId == 0 ? "HOST" : "JOIN")}  {CharLabel(p.CharacterId.Value.ToString())}{(p.IsOwner ? "  (" + L("自分") + ")" : "")}").Append('\n');
        GUILayout.Label(sb.ToString(), smallStyle);
        DrawRoomDeck(roomName); // 2026-10-31: 使うデッキ(出発前だけ選べる)
        if (host && LanDiscovery.Advertising) GUILayout.Label(L(LocRoomUi.T(LocRoomUi.ListedLocal)), smallStyle);
        if (host && onl && OnlineServices.Current != null) GUILayout.Label(L(LocRoomUi.T(LocRoomUi.ListedOnline)), smallStyle);
        if (onl && Debug.isDebugBuild) GUILayout.Label("DEV " + NetStats.PingLine(), smallStyle);
        GUILayout.Label(L(host
            ? LocRoomUi.T(LocRoomUi.HostHelp)
            : "HOSTが出発すると自動でスタートします。そのままお待ちください。"), smallStyle);
        GUILayout.Space(6f); QuickChat.DrawRoomButtons(buttonStyle, smallStyle); // 2026-11-07 プロフィール: 定型メッセージ
        GUILayout.FlexibleSpace();
        // 2026-11-24(マスター依頼): ホストは「ゲーム開始」(ステージを選んで出発)/「部屋を解散」、参加者は「部屋から退出」。
        //  解散・退出は確かめてから(ほかの人への影響を書く)。「閉じる」は小窓を閉じるだけ(部屋はそのまま。端末の戻る/Esc も同じ)
        if (leaveConfirm != LeaveConfirm.None && leaveConfirmRoomHost != host) leaveConfirm = LeaveConfirm.None; // 役割が変わった時は出し直さない
        // 退出/解散の途中(ONLINE は部屋の削除・退出の通信を待つ)。10 秒で終わらなければ、失敗を出してもう一度押せるように
        if (leavingAt > 0f)
        {
            if (Time.unscaledTime - leavingAt < LeaveTimeout)
            {
                GUILayout.Label(L(LocRoomUi.T(leavingDisband ? LocRoomUi.Disbanding : LocRoomUi.Leaving)), bannerStyle);
                return;
            }
            leavingAt = -1f; LeaveTimeouts++;
            Debug.LogWarning("[NetUI] leaving the room did not finish in time - offering retry");
            createMessage = L(LocRoomUi.T(LocRoomUi.LeaveFailed)); createMessageUntil = Time.unscaledTime + 6f;
        }
        if (Time.unscaledTime < createMessageUntil) GUILayout.Label(createMessage, bannerStyle);
        if (leaveConfirm != LeaveConfirm.None)
        {
            bool disband = leaveConfirm == LeaveConfirm.Disband;
            GUILayout.Label(L(LocRoomUi.T(disband ? LocRoomUi.DisbandConfirm : LocRoomUi.LeaveConfirm)), bannerStyle);
            GUILayout.Space(6f);
            GUI.enabled = !OnlineServices.Busy;
            if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(disband ? LocRoomUi.Disband : LocRoomUi.Leave)), buttonStyle, GUILayout.Height(52f))))
            {
                leaveConfirm = LeaveConfirm.None;
                Debug.Log($"[NetUI] {(disband ? "disband room (host)" : "leave room (join)")}");
                leavingAt = Time.unscaledTime; leavingDisband = disband; LeaveRequests++;
                NetSession.Instance.Leave();
                screen = MultiScreen.Choose;
            }
            GUI.enabled = true;
            GUILayout.Space(6f);
            if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) leaveConfirm = LeaveConfirm.None;
            return;
        }
        if (host)
        {
            if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(LocRoomUi.StartGame)), buttonStyle, GUILayout.Height(56f)))) StartGameFromRoom();
            GUILayout.Space(6f);
        }
        GUI.enabled = !OnlineServices.Busy;
        if (PadNav.LayoutButton(GUILayout.Button(L(LocRoomUi.T(host ? LocRoomUi.Disband : LocRoomUi.Leave)), buttonStyle, GUILayout.Height(52f))))
        {
            leaveConfirm = host ? LeaveConfirm.Disband : LeaveConfirm.Leave; leaveConfirmRoomHost = host;
        }
        GUI.enabled = true;
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("閉じる"), buttonStyle, GUILayout.Height(48f)))) ClosePanelFromUi();
        if (LocRoomUi.Ready(LocRoomUi.CloseNote)) GUILayout.Label(L(LocRoomUi.CloseNote), smallStyle);
    }

    // 退出/解散の結果(2026-11-24): 部屋が無くなったら選択の画面で「解散しました/退出しました」を数秒出す(AutoScreen が Room から戻す時)
    const float LeaveTimeout = 10f;
    float leavingAt = -1f; bool leavingDisband;
    public static int LeaveRequests { get; private set; }  // 確認用
    public static int LeaveTimeouts { get; private set; }  // 確認用
    void NoteLeaveFinished()
    {
        if (leavingAt <= 0f) return;
        leavingAt = -1f;
        string done = leavingDisband ? LocRoomUi.Disbanded : LocRoomUi.Left;
        if (LocRoomUi.Ready(done)) { createMessage = L(done); createMessageUntil = Time.unscaledTime + 4f; } // 訳が無い言語では出さない(戻った画面で分かる)
    }

    enum LeaveConfirm { None, Disband, Leave }
    LeaveConfirm leaveConfirm;
    bool leaveConfirmRoomHost;
    public static int StartGameTaps { get; private set; } // 確認用

    // ホストの「ゲーム開始」: 小窓を閉じて、ステージ選択を開く(扉と同じ。出発すると全員が同時にスタート)
    void StartGameFromRoom()
    {
        var gm = GameManager.Instance;
        if (gm == null || !NetSession.IsHost) return;
        StartGameTaps++;
        Debug.Log("[NetUI] start game (host) -> stage select");
        leaveConfirm = LeaveConfirm.None;
        PanelOpen = false;
        gm.OpenStageSelect();
    }

    // デッキのプリセット(2026-10-31): 部屋に入った時に、このモードで前回使ったデッキへ(1回だけ)。出発までは ◀ ▶ で選べる
    static string deckAppliedRoom;
    void DrawRoomDeck(string roomName)
    {
        var gm = GameManager.Instance;
        if (gm == null || !DeckPresets.Loaded) return;
        string key = (NetSession.IsHost ? "H:" : "J:") + roomName;
        if (deckAppliedRoom != key && Event.current.type == EventType.Layout) { deckAppliedRoom = key; gm.ApplyRememberedPreset(""); } // マップは HOST が決めるので、モード(LOCAL/ONLINE)の記録
        bool can = gm.CanChangeDeckNow;
        GUILayout.BeginHorizontal();
        GUILayout.Label(L(LocPendingReview.PresetRoomLabel), smallStyle, GUILayout.Width(150f));
        GUI.enabled = can;
        int a = DeckPresets.Active;
        if (PadNav.LayoutButton(GUILayout.Button("◀", buttonStyle, GUILayout.Width(56f), GUILayout.Height(44f)))) gm.ActivatePreset((a + DeckPresets.Count - 1) % DeckPresets.Count);
        GUILayout.Label(Loc.Shape((a + 1) + "  " + Loc.Plain(DeckPresets.DisplayName(a)) + "   " + gm.DeckCards.Count + "/" + GameManager.DeckCapacity), labelStyle, GUILayout.Height(44f));
        if (PadNav.LayoutButton(GUILayout.Button("▶", buttonStyle, GUILayout.Width(56f), GUILayout.Height(44f)))) gm.ActivatePreset((a + 1) % DeckPresets.Count);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (!can) GUILayout.Label(L(LocPendingReview.PresetLocked), smallStyle);
    }

    // ADVANCED(開発用): 従来の IP を直接入力する画面
    void DrawAdvanced()
    {
        GUILayout.Label("ADVANCED: IPで接続(開発用)", titleStyle);
        GUILayout.Label($"同じWi-Fi、またはスマホのテザリングに接続してください(最大{NetSession.MaxPlayers}人)。", smallStyle);
        GUILayout.Space(6f);

        string ips = localIps.Count > 0 ? string.Join("  /  ", localIps) : "(取得できません - 端末のWi-Fi設定で確認してください)";
        GUILayout.Label("この端末のIP: " + ips, labelStyle);
        if (!string.IsNullOrEmpty(NetSession.Instance.StatusText)) GUILayout.Label("状態: " + NetSession.Instance.StatusText, labelStyle);
        GUILayout.Space(6f);

        // Phase 3: ゲームモード(HOSTが選んだものがセッション全体の正解。JOINは選べない)。
        if (!NetSession.IsClientOnly)
        {
            MultiplayerGameMode mode = NetRunLauncher.SelectedMode;
            GUILayout.BeginHorizontal();
            GUILayout.Label("MODE", labelStyle, GUILayout.Width(90f));
            if (GUILayout.Toggle(mode == MultiplayerGameMode.Coop, "CO-OP", buttonStyle, GUILayout.Height(46f))) mode = MultiplayerGameMode.Coop;
            if (GUILayout.Toggle(mode == MultiplayerGameMode.Versus, "VERSUS", buttonStyle, GUILayout.Height(46f))) mode = MultiplayerGameMode.Versus;
            GUILayout.EndHorizontal();
            if (mode != NetRunLauncher.SelectedMode) NetRunLauncher.SelectedMode = mode;
            GUILayout.Label(mode == MultiplayerGameMode.Coop
                ? "CO-OP: 倒れたらDOWN。倒れた地点まで来た仲間(ハート2つ以上)がREVIVEでハート1つを渡すと復活。全員DOWNで終了。"
                : "VERSUS: 倒れたら脱落(復活なし)。最後の1人まで続き、到達距離で順位が決まる。", smallStyle);
        }
        else GUILayout.Label("MODE: HOSTが選択します(ラン開始時に全員へ反映)", smallStyle);
        GUILayout.Space(8f);

        if (!NetSession.IsActive)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ポート", labelStyle, GUILayout.Width(90f));
            PadNav.NoteTextField();
            portInput = GUILayout.TextField(portInput, 5, fieldStyle, GUILayout.Width(120f), GUILayout.Height(44f));
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (PadNav.LayoutButton(GUILayout.Button("HOST(この端末で部屋を作る)", buttonStyle, GUILayout.Height(56f))))
            {
                SavePrefs();
                if (NetSession.Instance.StartHost(ParsePort())) LanDiscovery.StartAdvertising();
            }
            GUILayout.Space(10f);
            GUILayout.Label("JOIN: HOST端末に表示されたIPを入力", labelStyle);
            GUILayout.BeginHorizontal();
            PadNav.NoteTextField();
            hostIpInput = GUILayout.TextField(hostIpInput, 15, fieldStyle, GUILayout.Height(48f));
            if (PadNav.LayoutButton(GUILayout.Button("JOIN", buttonStyle, GUILayout.Width(140f), GUILayout.Height(48f))))
            {
                SavePrefs();
                NetSession.Instance.StartClient(hostIpInput, ParsePort());
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            var sb = new StringBuilder();
            foreach (NetPlayer p in NetPlayer.All)
            {
                sb.Append($"P{p.PlayerNumber}  clientId={p.OwnerClientId}  {(p.IsOwner ? "(自分)" : "")}  {(p.OwnerClientId == 0 ? "HOST" : "JOIN")}  {p.CharacterId.Value}\n");
            }
            GUILayout.Label($"接続中のプレイヤー {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}", labelStyle);
            GUILayout.Label(sb.ToString(), smallStyle);
            GUILayout.Label(NetSession.IsHost
                ? "ホームの扉 → STAGE SELECTで「出発」すると、接続中の全員が同じステージで同時にスタートします。"
                : "HOSTが出発すると自動でスタートします。そのままお待ちください。", smallStyle);
            GUILayout.Space(8f);
            if (PadNav.LayoutButton(GUILayout.Button(NetSession.IsHost ? "部屋を閉じる(HOST終了)" : "切断する", buttonStyle, GUILayout.Height(52f))))
            {
                NetSession.Instance.Leave();
            }
        }

        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button(L("戻る"), buttonStyle, GUILayout.Height(48f)))) screen = NetSession.IsActive ? MultiScreen.Room : MultiScreen.Top;
    }

    // このパネルの上でのクリックが、下にあるHome画面のボタン(GameManagerのOnGUI)へ届かないようにする。
    static void ConsumePointer(Rect r)
    {
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp) && r.Contains(e.mousePosition)) e.Use();
    }

    void DrawRunHud(float w, float safeTop)
    {
        string text;
        if (!NetSession.IsActive) text = "MULTI: 切断";
        else
        {
            text = $"MULTI {NetSession.ConnectedPlayerCount}P {(NetRunLauncher.ActiveMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS")}";
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p.IsOwner) continue;
                text += $"  {p.Tag}:{(p.Phase.Value == NetPlayer.PhaseInRun ? $"走行中 遅延{p.PlaybackLag * 1000f:F0}ms" : p.Phase.Value == NetPlayer.PhaseRunEnded ? "終了" : "Home")}";
            }
        }
        // 人数が増えても切れないよう、相手ごとに改行して高さを伸ばす(2026-10-02)
        text = text.Replace("  P", "\nP");
        int nLines = text.Split('\n').Length;
        Rect r = new Rect(w * 0.5f - 240f, safeTop + 64f, 480f, 26f * nLines + 4f);
        LocGUI.Label(r, text, tagStyle);
    }

    void DrawRemoteNameTags()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float s = Scale;
        foreach (NetPlayer p in NetPlayer.All)
        {
            RemotePlayerAvatar a = p.Avatar;
            if (a == null || !a.IsShown) continue;
            if (a.GetComponent<ParticipantAura>() != null) continue; // 2026-10-14: 識別用オーラの名前(ParticipantLabels)が出す
            Vector3 sp = cam.WorldToScreenPoint(a.HeadWorldPosition + Vector3.up * 0.3f);
            if (sp.z < 0f) continue;
            float gx = sp.x / s, gy = (Screen.height - sp.y) / s;
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            LocGUI.Label(new Rect(gx - 120f, gy - 28f, 240f, 28f), p.Tag, tagStyle);
            GUI.matrix = prev;
        }
    }

    void DrawConnectionLostBanner(float w, float h, GameManager gm)
    {
        float bw = Mathf.Min(620f, w - 32f), bh = 240f;
        Rect r = new Rect((w - bw) * 0.5f, (h - bh) * 0.5f, bw, bh);
        GUI.Box(r, "");
        GUI.Box(r, "");
        LocGUI.Label(new Rect(r.x + 16f, r.y + 16f, bw - 32f, 120f), NetSession.Instance.ConnectionLostMessage, bannerStyle);
        bool inRun = gm.HasStarted && !gm.IsGameOver;
        if (inRun)
        {
            if (GUI.Button(new Rect(r.x + 24f, r.yMax - 76f, bw * 0.5f - 36f, 56f), "ホームへ戻る", buttonStyle))
            {
                NetSession.Instance.ClearConnectionLost();
                gm.ReturnToHome();
            }
            if (GUI.Button(new Rect(r.x + bw * 0.5f + 12f, r.yMax - 76f, bw * 0.5f - 36f, 56f), "このまま走る", buttonStyle))
                NetSession.Instance.ClearConnectionLost();
        }
        else if (GUI.Button(new Rect(r.x + bw * 0.5f - 120f, r.yMax - 76f, 240f, 56f), "OK", buttonStyle))
        {
            NetSession.Instance.ClearConnectionLost();
        }
        ConsumePointer(r);
    }

    ushort ParsePort()
    {
        return ushort.TryParse(portInput, out ushort p) && p > 0 ? p : NetSession.DefaultPort;
    }

    void SavePrefs()
    {
        SaveStore.SetString(LastHostIpKey, hostIpInput);
        SaveStore.SetInt(LastPortKey, ParsePort());
        SaveStore.Save();
    }
}

// マルチの部屋のボタンと説明(2026-11-24、マスター依頼: ボタンの文言を実際の動作に合わせる)。訳は Tools/loc/keys_extra46.tsv(エステルの確認の後に反映)。
//  T(): 訳がまだ無い言語では、変更前の英語の名前(CREATE GAME など)を出す(未訳の日本語を出さない)。日本語はそのまま
public static class LocRoomUi
{
    public const string CreateRoom = "部屋を作成";                                   // ID 11065 / en: Create Room
    public const string JoinRoom = "部屋に参加";                                     // ID 11066 / en: Join Room
    public const string Disband = "部屋を解散";                                      // ID 11067 / en: Disband Room
    public const string Leave = "部屋から退出";                                      // ID 11068 / en: Leave Room
    public const string StartGame = "ゲーム開始";                                    // ID 11069 / en: Start Game
    public const string CreateDescOnline = "部屋を作成し、オンラインの「部屋に参加」の一覧に公開します。"; // ID 11070
    public const string CreateDescLocal = "この端末で部屋を作成します。同じWi-Fiの端末から「部屋に参加」で入れます。"; // ID 11071
    public const string EmptyLocal = "HOST の端末で「部屋を作成」を押し、すべての端末を同じWi-Fi(またはテザリング)につないでください。"; // ID 11072
    public const string EmptyOnline = "「部屋を作成」で部屋を作るか、少し待ってから再検索してください。"; // ID 11073
    public const string ListedLocal = "近くの端末の「部屋に参加」に表示中";            // ID 11074
    public const string ListedOnline = "オンラインの「部屋に参加」に表示中";           // ID 11075
    public const string HostHelp = "そろったら「ゲーム開始」でステージを選んで出発すると、全員が同じステージで同時にスタートします。"; // ID 11076
    public const string DisbandConfirm = "部屋を解散しますか？\n参加しているほかのプレイヤーも、全員が部屋から退出になります。"; // ID 11077
    public const string LeaveConfirm = "部屋から退出しますか？\nほかのプレイヤーは部屋に残り、そのまま続けられます。"; // ID 11078
    public const string CloseNote = "「閉じる」で画面を閉じても、部屋はそのままです(ホームの「マルチ」から戻れます)。"; // ID 11079
    public const string Disbanding = "部屋を解散しています…";      // ID 11080
    public const string Leaving = "部屋から退出しています…";       // ID 11081
    public const string Disbanded = "部屋を解散しました";          // ID 11082
    public const string Left = "部屋から退出しました";             // ID 11083
    public const string LeaveFailed = "部屋から退出できませんでした。通信を確認して、もう一度お試しください"; // ID 11084
    public static readonly string[] All = { CreateRoom, JoinRoom, Disband, Leave, StartGame, CreateDescOnline, CreateDescLocal, EmptyLocal, EmptyOnline, ListedLocal, ListedOnline, HostHelp, DisbandConfirm, LeaveConfirm, CloseNote, Disbanding, Leaving, Disbanded, Left, LeaveFailed };

    // 訳が無い言語の控え(変更前の表示。英語の名前か、変更前の日本語の鍵 = 既に訳がある)
    static readonly System.Collections.Generic.Dictionary<string, string> Fallback = new System.Collections.Generic.Dictionary<string, string>
    {
        { CreateRoom, "CREATE GAME" }, { JoinRoom, "JOIN" }, { Disband, "部屋を閉じる" }, { Leave, "退出する" }, { StartGame, "START" },
        { CreateDescOnline, "部屋を作成し、オンラインの「FIND GAME」に公開します。" },
        { CreateDescLocal, "この端末で部屋を作ります。同じWi-Fiの端末から「FIND GAME」で参加できます。" },
        { EmptyLocal, "HOST の端末で「CREATE GAME」を押して、同じWi-Fi(テザリング)につないでください。" },
        { EmptyOnline, "「CREATE GAME」で部屋を作るか、少し待ってから再検索してください。" },
        { ListedLocal, "近くの端末の「FIND GAME」に表示中" }, { ListedOnline, "オンラインの「FIND GAME」に表示中" },
        { HostHelp, "そろったら「閉じる(部屋はそのまま)」→ 扉 → STAGE SELECTで「出発」すると、全員が同じステージで同時にスタートします。" },
        { DisbandConfirm, "部屋を閉じる" }, { LeaveConfirm, "退出する" }, { Disbanding, "接続中…" }, { Leaving, "接続中…" },
        { LeaveFailed, "接続できませんでした。もう一度お試しください" },
    };
    public static string T(string key) => Loc.Ready(key) || !Fallback.TryGetValue(key, out var old) ? key : old;
    public static bool Ready(string key) => Loc.Ready(key);
}

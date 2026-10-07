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
    public static void OpenPanel() { if (!(instance != null && instance.BannerVisible)) PanelOpen = true; }
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
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
        labelStyle.normal.textColor = new Color(0.92f, 0.94f, 1f);
        smallStyle = new GUIStyle(labelStyle) { fontSize = 16 };
        titleStyle = new GUIStyle(labelStyle) { fontSize = 24, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(1f, 0.86f, 0.45f);
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
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
        Rect safe = Screen.safeArea;
        float safeRight = (Screen.width - safe.xMax) / s, safeTop = (Screen.height - safe.yMax) / s;

        if (!gm.HasStarted)
        {
            // カード合成改修(2026-09-26) - 合成/デッキ編集/キャラ選択などの全画面
            // オーバーレイ中は、その画面と無関係なLOCAL MULTIボタンを出さない。
            // 開くボタンはGameManagerのホーム右上(OpenPanel)。ここではパネルだけ描く。
            if (PanelOpen) DrawPanel(w, h);
        }
        else if (NetSession.IsActive || NetRunLauncher.IsMultiplayerRun)
        {
            DrawRunHud(w, safeTop);
            GUI.matrix = prevMatrix;
            DrawRemoteNameTags();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        if (BannerVisible) DrawConnectionLostBanner(w, h, gm);
        if (!string.IsNullOrEmpty(toastText) && Time.unscaledTime < toastUntil)
        {
            Rect r = new Rect(w * 0.5f - 260f, h * 0.18f, 520f, 56f);
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
        GUI.color = new Color(0.07f, 0.08f, 0.12f, 0.96f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = keep;
        GUI.Box(panel, "");
        // 2026-10-08: 長い言語で文が増えても下のボタンへ届くよう、中身全体を指でスクロールできるようにする(収まる時は今までどおり)
        Rect area = new Rect(panel.x + 20f, panel.y + 16f, pw - 40f, ph - 32f);
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
        else if (!NetSession.IsActive && screen == MultiScreen.Room && !OnlineServices.Busy) screen = MultiScreen.Choose;
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
        GUILayout.Label("MULTIPLAYER", titleStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("LOCAL PLAY", buttonStyle, GUILayout.Height(70f)))) { online = false; Go(MultiScreen.Mode); }
        GUILayout.Label(L("同じWi-Fi、またはスマホのテザリングにつないだ端末どうしで遊びます(最大" + NetSession.MaxPlayers + "人)。"), smallStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("ONLINE PLAY", buttonStyle, GUILayout.Height(70f)))) StartOnline();
        GUILayout.Label(L("インターネットで、離れた場所の人と遊びます(最大" + NetSession.MaxPlayers + "人)。"), smallStyle);
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button(L("ADVANCED(IPを直接入力・開発用)"), buttonStyle, GUILayout.Height(40f)))) Go(MultiScreen.Advanced);
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("閉じる"), buttonStyle, GUILayout.Height(48f)))) ClosePanelFromUi();
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
        GUILayout.Label(L("倒れたらDOWN。倒れた地点まで来た仲間(HP2以上)がHPを1つ渡すと復活。全員DOWNで終了。"), smallStyle);
        GUILayout.Space(8f);
        if (PadNav.LayoutButton(GUILayout.Button("VERSUS", buttonStyle, GUILayout.Height(64f)))) { NetRunLauncher.SelectedMode = MultiplayerGameMode.Versus; Go(MultiScreen.Choose); }
        GUILayout.Label(L("倒れたら脱落(復活なし)。最後の1人まで続き、到達距離で順位が決まる。"), smallStyle);
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
        GUI.enabled = !busy;
        if (PadNav.LayoutButton(GUILayout.Button("CREATE GAME", buttonStyle, GUILayout.Height(70f)))) { if (online) CreateOnlineGame(); else CreateGame(); }
        GUILayout.Label(L(online ? "部屋を作ります。オンラインの「FIND GAME」に表示されます。" : "この端末で部屋を作ります。近くの端末の「FIND GAME」に表示されます。"), smallStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("FIND GAME", buttonStyle, GUILayout.Height(70f)))) Go(online ? MultiScreen.OnlineFind : MultiScreen.Find);
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
        GUILayout.Label("FIND GAME · ONLINE", titleStyle);
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
            if (!searching) GUILayout.Label(L("「CREATE GAME」で部屋を作るか、少し待ってから再検索してください。"), smallStyle);
        }
        else
        {
            onlineScroll = GUILayout.BeginScrollView(onlineScroll, GUILayout.Height(330f));
            foreach (var r in rooms)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label(r.name, labelStyle);
                GUILayout.Label($"{(r.GameMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS")}   {r.players}/{r.maxPlayers}   {r.StateLabel}", smallStyle);
                GUILayout.EndVertical();
                GUI.enabled = r.Joinable && !joining && !NetSession.IsActive;
                if (PadNav.LayoutButton(GUILayout.Button("JOIN", buttonStyle, GUILayout.Width(170f), GUILayout.Height(56f)))) JoinOnline(r);
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
        if (PadNav.LayoutButton(GUILayout.Button("BACK", buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Top);
    }

    Vector2 roomScroll;
    void DrawFind()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label("FIND GAME", titleStyle);
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
            GUILayout.Label(L(searching ? "ローカルゲームを探しています…" : "ゲームが見つかりません"), titleStyle);
            if (!searching) GUILayout.Label(L("HOST の端末で「CREATE GAME」を押して、同じWi-Fi(テザリング)につないでください。"), smallStyle);
        }
        else
        {
            roomScroll = GUILayout.BeginScrollView(roomScroll, GUILayout.Height(330f));
            // 選んだモードの部屋を上に
            var sorted = new List<LanRoom>(rooms);
            sorted.Sort((a, b) => (a.mode == NetRunLauncher.SelectedMode ? 0 : 1).CompareTo(b.mode == NetRunLauncher.SelectedMode ? 0 : 1));
            foreach (var r in sorted)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label(r.roomName, labelStyle);
                GUILayout.Label($"{(r.mode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS")}   {r.players}/{r.maxPlayers}   {r.StateLabel}", smallStyle);
                GUILayout.EndVertical();
                GUI.enabled = r.Joinable && !joining && !NetSession.IsActive;
                if (PadNav.LayoutButton(GUILayout.Button("JOIN", buttonStyle, GUILayout.Width(170f), GUILayout.Height(56f)))) JoinRoom(r);
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
        GUILayout.Space(8f);
        GUILayout.Label($"WAITING FOR PLAYERS {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}", titleStyle);
        var sb = new StringBuilder();
        foreach (NetPlayer p in NetPlayer.All)
            sb.Append($"{p.Tag}  {(p.OwnerClientId == 0 ? "HOST" : "JOIN")}  {CharLabel(p.CharacterId.Value.ToString())}{(p.IsOwner ? "  (" + L("自分") + ")" : "")}").Append('\n');
        GUILayout.Label(sb.ToString(), smallStyle);
        if (host && LanDiscovery.Advertising) GUILayout.Label(L("近くの端末の「FIND GAME」に表示中"), smallStyle);
        if (host && onl && OnlineServices.Current != null) GUILayout.Label(L("オンラインの「FIND GAME」に表示中"), smallStyle);
        if (onl && Debug.isDebugBuild) GUILayout.Label("DEV " + NetStats.PingLine(), smallStyle);
        GUILayout.Label(L(host
            ? "そろったら「閉じる」→ 扉 → Stage Selectで「出発」すると、全員が同じステージで同時にスタートします。"
            : "HOSTが出発すると自動でスタートします。そのままお待ちください。"), smallStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = !OnlineServices.Busy;
        if (PadNav.LayoutButton(GUILayout.Button(L(host ? "部屋を閉じる" : "退出する"), buttonStyle, GUILayout.Height(52f))))
        {
            NetSession.Instance.Leave();
            screen = MultiScreen.Choose;
        }
        GUI.enabled = true;
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button(L("閉じる(部屋はそのまま)"), buttonStyle, GUILayout.Height(48f)))) PanelOpen = false;
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
                ? "CO-OP: 倒れたらDOWN。倒れた地点まで来た仲間(HP2以上)がHPを1つ渡すと復活。全員DOWNで終了。"
                : "VERSUS: 倒れたら脱落(復活なし)。最後の1人まで続き、到達距離で順位が決まる。", smallStyle);
        }
        else GUILayout.Label("MODE: HOSTが選択します(Run開始時に自動で揃います)", smallStyle);
        GUILayout.Space(8f);

        if (!NetSession.IsActive)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ポート", labelStyle, GUILayout.Width(90f));
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
                ? "扉 → Stage Selectで「出発」すると、接続中の全員が同じステージで同時にスタートします。"
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
            if (GUI.Button(new Rect(r.x + 24f, r.yMax - 76f, bw * 0.5f - 36f, 56f), "Homeへ戻る", buttonStyle))
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

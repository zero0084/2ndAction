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
        if (!PanelOpen) return;
        ipRefreshTimer -= Time.unscaledDeltaTime;
        if (ipRefreshTimer <= 0f)
        {
            localIps = NetSession.GetLocalIPv4Addresses();
            ipRefreshTimer = 3f;
        }
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
            GUI.Label(r, toastText, bannerStyle);
        }

        GUI.matrix = prevMatrix;
        PadNav.EndLayer(padLayer);
    }

    // ===================================================================== //
    // 2026-10-05: 正式な流れ MULTIPLAYER → LOCAL PLAY → CO-OP / VERSUS → CREATE GAME / FIND GAME(IP 入力なし)。
    // IP を直接入れる従来の画面は ADVANCED(DEBUG)に残す。部屋の発見はゲームの同期と別(LanDiscovery)。
    // ===================================================================== //
    public enum MultiScreen { Top, Mode, Choose, Find, Room, Advanced }
    MultiScreen screen = MultiScreen.Top;
    public static MultiScreen CurrentScreen => instance != null ? instance.screen : MultiScreen.Top; // 確認用
    string findMessage = "";
    float findMessageUntil;
    string joiningRoom = "";
    float joinStartedAt;


    void DrawPanel(float w, float h)
    {
        // 接続中は待機室(部屋)の画面へ
        if (NetSession.IsActive && screen != MultiScreen.Advanced) screen = MultiScreen.Room;
        else if (!NetSession.IsActive && screen == MultiScreen.Room) screen = MultiScreen.Choose;

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
        GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 16f, pw - 40f, ph - 32f));
        switch (screen)
        {
            case MultiScreen.Top: DrawTop(); break;
            case MultiScreen.Mode: DrawMode(); break;
            case MultiScreen.Choose: DrawChoose(); break;
            case MultiScreen.Find: DrawFind(); break;
            case MultiScreen.Room: DrawRoom(); break;
            default: DrawAdvanced(); break;
        }
        GUILayout.EndArea();
        ConsumePointer(panel);
    }

    void Go(MultiScreen s)
    {
        if (screen == MultiScreen.Find && s != MultiScreen.Find) LanDiscovery.StopDiscovery("back");
        // 音の再設計(2026-10-06): 画面を進む=決定 / 最初の画面へ戻る=戻る
        if (AudioManager.Instance != null && s != screen) AudioManager.Instance.PlaySe(s == MultiScreen.Top ? SeId.Cancel : SeId.Decide);
        screen = s;
        if (s == MultiScreen.Find) { findMessage = ""; LanDiscovery.StartDiscovery(); }
    }

    void ClosePanelFromUi()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiClose);
        if (screen == MultiScreen.Find) LanDiscovery.StopDiscovery("closed");
        PanelOpen = false;
        if (!NetSession.IsActive) screen = MultiScreen.Top;
    }

    void DrawTop()
    {
        GUILayout.Label("MULTIPLAYER", titleStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("LOCAL PLAY", buttonStyle, GUILayout.Height(70f)))) Go(MultiScreen.Mode);
        GUILayout.Label("同じWi-Fi、またはスマホのテザリングにつないだ端末どうしで遊びます(最大" + NetSession.MaxPlayers + "人)。", smallStyle);
        GUILayout.Space(10f);
        GUI.enabled = false;
        PadNav.LayoutButton(GUILayout.Button("ONLINE(準備中)", buttonStyle, GUILayout.Height(56f)));
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button("ADVANCED(IPを直接入力・開発用)", buttonStyle, GUILayout.Height(40f)))) Go(MultiScreen.Advanced);
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button("閉じる", buttonStyle, GUILayout.Height(48f)))) ClosePanelFromUi();
    }

    void DrawMode()
    {
        GUILayout.Label("LOCAL PLAY", titleStyle);
        GUILayout.Label("モードを選んでください", labelStyle);
        GUILayout.Space(8f);
        if (PadNav.LayoutButton(GUILayout.Button("CO-OP", buttonStyle, GUILayout.Height(64f)))) { NetRunLauncher.SelectedMode = MultiplayerGameMode.Coop; Go(MultiScreen.Choose); }
        GUILayout.Label("倒れたらDOWN。倒れた地点まで来た仲間(HP2以上)がHPを1つ渡すと復活。全員DOWNで終了。", smallStyle);
        GUILayout.Space(8f);
        if (PadNav.LayoutButton(GUILayout.Button("VERSUS", buttonStyle, GUILayout.Height(64f)))) { NetRunLauncher.SelectedMode = MultiplayerGameMode.Versus; Go(MultiScreen.Choose); }
        GUILayout.Label("倒れたら脱落(復活なし)。最後の1人まで続き、到達距離で順位が決まる。", smallStyle);
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button("戻る", buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Top);
    }

    void DrawChoose()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label("LOCAL PLAY · " + m, titleStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("CREATE GAME", buttonStyle, GUILayout.Height(70f)))) CreateGame();
        GUILayout.Label("この端末で部屋を作ります。近くの端末の「FIND GAME」に表示されます。", smallStyle);
        GUILayout.Space(10f);
        if (PadNav.LayoutButton(GUILayout.Button("FIND GAME", buttonStyle, GUILayout.Height(70f)))) Go(MultiScreen.Find);
        GUILayout.Label("近くの部屋を探して参加します(IPの入力は不要)。", smallStyle);
        if (!string.IsNullOrEmpty(NetSession.Instance.StatusText)) GUILayout.Label(NetSession.Instance.StatusText, smallStyle);
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button("戻る", buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Mode);
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

    Vector2 roomScroll;
    void DrawFind()
    {
        string m = NetRunLauncher.SelectedMode == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label("FIND GAME", titleStyle);
        GUILayout.Label($"近くの部屋({m} を選択中。HOST のモードで遊びます)", smallStyle);
        GUILayout.Space(6f);
        bool joining = NetSession.JoinPending;
        if (!string.IsNullOrEmpty(NetSession.LastJoinFailure) && !joining)
        {
            findMessage = NetSession.LastJoinFailure == "CONNECTION FAILED" ? "CONNECTION FAILED\n接続できませんでした。同じWi-Fiか確認して、もう一度お試しください" : "CONNECTION FAILED: " + NetSession.LastJoinFailure;
            findMessageUntil = Time.unscaledTime + 4f;
            NetSession.ClearJoinFailure();
        }
        if (Time.unscaledTime < findMessageUntil) GUILayout.Label(findMessage, bannerStyle);
        if (joining) GUILayout.Label($"接続中… {joiningRoom}", labelStyle);

        var rooms = LanDiscovery.Rooms;
        if (rooms.Count == 0)
        {
            GUILayout.Space(20f);
            bool searching = Time.unscaledTime - LanDiscovery.DiscoveryStartedAt < 4f;
            GUILayout.Label(searching ? "ローカルゲームを探しています…" : "ゲームが見つかりません", titleStyle);
            if (!searching) GUILayout.Label("HOST の端末で「CREATE GAME」を押して、同じWi-Fi(テザリング)につないでください。", smallStyle);
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
                if (PadNav.LayoutButton(GUILayout.Button(r.Joinable ? "JOIN" : r.StateLabel, buttonStyle, GUILayout.Width(170f), GUILayout.Height(56f)))) JoinRoom(r);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button("再検索", buttonStyle, GUILayout.Height(48f)))) LanDiscovery.Rescan();
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button("戻る", buttonStyle, GUILayout.Height(48f)))) Go(MultiScreen.Choose);
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
        string m = (host ? NetRunLauncher.SelectedMode : NetRunLauncher.ActiveMode) == MultiplayerGameMode.Coop ? "CO-OP" : "VERSUS";
        GUILayout.Label(host ? LanDiscovery.RoomName : "参加中の部屋", titleStyle);
        GUILayout.Label(host ? $"MODE: {m}(HOSTが決めます)" : "MODE: HOSTが決めます(Run開始時に自動で揃います)", smallStyle);
        GUILayout.Space(8f);
        GUILayout.Label($"WAITING FOR PLAYERS {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}", titleStyle);
        var sb = new StringBuilder();
        foreach (NetPlayer p in NetPlayer.All)
            sb.Append($"P{p.PlayerNumber}  {(p.OwnerClientId == 0 ? "HOST" : "JOIN")}  {p.CharacterId.Value}{(p.IsOwner ? "  (自分)" : "")}\n");
        GUILayout.Label(sb.ToString(), smallStyle);
        if (host && LanDiscovery.Advertising) GUILayout.Label("近くの端末の「FIND GAME」に表示中", smallStyle);
        GUILayout.Label(host
            ? "そろったら「閉じる」→ 扉 → Stage Selectで「出発」すると、全員が同じステージで同時にスタートします。"
            : "HOSTが出発すると自動でスタートします。そのままお待ちください。", smallStyle);
        GUILayout.FlexibleSpace();
        if (PadNav.LayoutButton(GUILayout.Button(host ? "部屋を閉じる" : "退出する", buttonStyle, GUILayout.Height(52f))))
        {
            NetSession.Instance.Leave();
            screen = MultiScreen.Choose;
        }
        GUILayout.Space(6f);
        if (PadNav.LayoutButton(GUILayout.Button("閉じる(部屋はそのまま)", buttonStyle, GUILayout.Height(48f)))) PanelOpen = false;
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
        if (PadNav.LayoutButton(GUILayout.Button("戻る", buttonStyle, GUILayout.Height(48f)))) screen = NetSession.IsActive ? MultiScreen.Room : MultiScreen.Top;
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
                text += $"  P{p.PlayerNumber}:{(p.Phase.Value == NetPlayer.PhaseInRun ? $"走行中 遅延{p.PlaybackLag * 1000f:F0}ms" : p.Phase.Value == NetPlayer.PhaseRunEnded ? "終了" : "Home")}";
            }
        }
        // 人数が増えても切れないよう、相手ごとに改行して高さを伸ばす(2026-10-02)
        text = text.Replace("  P", "\nP");
        int nLines = text.Split('\n').Length;
        Rect r = new Rect(w * 0.5f - 240f, safeTop + 64f, 480f, 26f * nLines + 4f);
        GUI.Label(r, text, tagStyle);
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
            GUI.Label(new Rect(gx - 80f, gy - 28f, 160f, 28f), $"P{p.PlayerNumber}", tagStyle);
            GUI.matrix = prev;
        }
    }

    void DrawConnectionLostBanner(float w, float h, GameManager gm)
    {
        float bw = Mathf.Min(620f, w - 32f), bh = 240f;
        Rect r = new Rect((w - bw) * 0.5f, (h - bh) * 0.5f, bw, bh);
        GUI.Box(r, "");
        GUI.Box(r, "");
        GUI.Label(new Rect(r.x + 16f, r.y + 16f, bw - 32f, 120f), NetSession.Instance.ConnectionLostMessage, bannerStyle);
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

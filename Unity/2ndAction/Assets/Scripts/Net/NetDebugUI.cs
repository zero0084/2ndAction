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
        hostIpInput = PlayerPrefs.GetString(LastHostIpKey, "192.168.");
        portInput = PlayerPrefs.GetInt(LastPortKey, NetSession.DefaultPort).ToString();
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

        float s = Scale;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = Screen.safeArea;
        float safeRight = (Screen.width - safe.xMax) / s, safeTop = (Screen.height - safe.yMax) / s;

        if (!gm.HasStarted)
        {
            if (!PanelOpen && !BannerVisible)
            {
                string label = NetSession.IsActive ? $"MULTI {NetSession.ConnectedPlayerCount}/{NetSession.MaxPlayers}" : "LOCAL MULTI";
                if (GUI.Button(new Rect(w - safeRight - 200f, safeTop + 96f, 188f, 48f), label, buttonStyle)) PanelOpen = true;
            }
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
    }

    void DrawPanel(float w, float h)
    {
        float pw = Mathf.Min(620f, w - 32f), ph = Mathf.Min(560f, h - 32f);
        Rect panel = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        GUI.Box(panel, "");
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 16f, pw - 40f, ph - 32f));

        GUILayout.Label("LOCAL MULTIPLAYER (開発版 Phase 1)", titleStyle);
        GUILayout.Label("同じWi-Fi、または片方のスマホのテザリングに2台を接続してください。", smallStyle);
        GUILayout.Space(6f);

        string ips = localIps.Count > 0 ? string.Join("  /  ", localIps) : "(取得できません - 端末のWi-Fi設定で確認してください)";
        GUILayout.Label("この端末のIP: " + ips, labelStyle);
        if (!string.IsNullOrEmpty(NetSession.Instance.StatusText)) GUILayout.Label("状態: " + NetSession.Instance.StatusText, labelStyle);
        GUILayout.Space(8f);

        if (!NetSession.IsActive)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ポート", labelStyle, GUILayout.Width(90f));
            portInput = GUILayout.TextField(portInput, 5, fieldStyle, GUILayout.Width(120f), GUILayout.Height(44f));
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (GUILayout.Button("HOST(この端末で部屋を作る)", buttonStyle, GUILayout.Height(56f)))
            {
                SavePrefs();
                NetSession.Instance.StartHost(ParsePort());
            }
            GUILayout.Space(10f);
            GUILayout.Label("JOIN: HOST端末に表示されたIPを入力", labelStyle);
            GUILayout.BeginHorizontal();
            hostIpInput = GUILayout.TextField(hostIpInput, 15, fieldStyle, GUILayout.Height(48f));
            if (GUILayout.Button("JOIN", buttonStyle, GUILayout.Width(140f), GUILayout.Height(48f)))
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
            if (GUILayout.Button(NetSession.IsHost ? "部屋を閉じる(HOST終了)" : "切断する", buttonStyle, GUILayout.Height(52f)))
            {
                NetSession.Instance.Leave();
            }
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("閉じる", buttonStyle, GUILayout.Height(48f))) PanelOpen = false;
        GUILayout.EndArea();
        ConsumePointer(panel);
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
            text = $"MULTI {NetSession.ConnectedPlayerCount}P";
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p.IsOwner) continue;
                text += $"  P{p.PlayerNumber}:{(p.Phase.Value == NetPlayer.PhaseInRun ? $"走行中 遅延{p.PlaybackLag * 1000f:F0}ms" : p.Phase.Value == NetPlayer.PhaseRunEnded ? "終了" : "Home")}";
            }
        }
        Rect r = new Rect(w * 0.5f - 240f, safeTop + 64f, 480f, 30f);
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
        PlayerPrefs.SetString(LastHostIpKey, hostIpInput);
        PlayerPrefs.SetInt(LastPortKey, ParsePort());
        PlayerPrefs.Save();
    }
}

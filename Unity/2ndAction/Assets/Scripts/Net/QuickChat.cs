using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 定型コミュニケーション(2026-11-07)。自由入力は無く、決まった文の番号(LocProfile.Chat の ID、1 バイト)だけを送る。
//  受け取った端末は自分の言語で文を出す(訳は ID から)。名前は送らない(送った人の番号 P1〜P8 とコードネームは NetPlayer から)。
//  通信: 既存のカスタムメッセージ(NetStats.SendNamed / Counted)に乗せる。名前 "OMM.QuickChat"(Reliable)。
//   JOIN → HOST: [byte 文の ID]   HOST → 全員(送った人を含む): [byte 送った人の番号][byte 文の ID]
//   HOST が送る時はそのまま全員へ。連打は 1 人 1.0 秒に 1 回まで(送る端末のボタンで止める)
//  2026-11-08 2台確認での修正:
//   ・HOST は送った本人へは返さない(以前は返して「自分の番号なら出さない」で消していたが、番号が決まる前だと本人の画面に2回出た)
//   ・HOST 側の連打の見張りは、通信の遅れで 2〜3 通がまとめて届いても捨てない数え方(4 秒に 5 通まで)。改造した端末の連投だけを止める
//     (以前の「前の通から 0.8 秒」だと、遅れてまとめて届いた正規の文を捨て、送った本人の画面にだけ出ることがあった)
//  通信の版: この文を知らない古い版は受け取っても処理しない(受け取り口が無いだけで同期は壊れない)。2026-11-07 の版 13 と同じ時期に入れたので版は上げていない
//  画面: マルチの部屋(NetDebugUI の部屋の画面)に文のボタン。受け取った文は画面の左下に小さな吹き出しで 4 秒(ラン中も。ゲームは止めない)
public class QuickChat : MonoBehaviour
{
    public static QuickChat Instance { get; private set; }
    const string Msg = "OMM.QuickChat";
    public const float SendInterval = 1.0f;
    public const float ShowSeconds = 4f;

    public struct Line { public int player; public byte id; public float until; }
    static readonly List<Line> lines = new List<Line>();
    public static int Received { get; private set; }  // 確認用
    public static int Sent { get; private set; }
    public static IReadOnlyList<Line> Lines => lines;
    public static event System.Action<int, byte> OnReceived; // (送った人の番号, 文の ID)

    bool registered; NetworkManager registeredManager;
    float lastSendAt = -99f;
    readonly Dictionary<ulong, Queue<float>> recentFrom = new Dictionary<ulong, Queue<float>>(); // HOST: 送った人ごとの、受け付けた時刻
    public const float GuardWindow = 4f;
    public const int GuardMax = 5;
    public static int DroppedBySpamGuard { get; private set; } // 確認用

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[QuickChat]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<QuickChat>();
    }

    void Update()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!registered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, NetStats.Counted(Msg, OnMessage));
            registered = true; registeredManager = nm;
        }
        else if (!active && registered) { registered = false; registeredManager = null; recentFrom.Clear(); }
        for (int i = lines.Count - 1; i >= 0; i--) if (lines[i].until < Time.unscaledTime) lines.RemoveAt(i);
    }

    public static bool CanSend => Instance != null && Time.unscaledTime - Instance.lastSendAt >= SendInterval;

    // 文を送る(マルチの部屋/ラン中のボタンから)。オフラインでは自分の画面にだけ出す(確認用)
    public static bool Send(byte id)
    {
        if (Instance == null || LocProfile.ChatText(id) == null || !CanSend) return false;
        Instance.lastSendAt = Time.unscaledTime;
        Sent++;
        var nm = NetSession.Manager;
        int me = NetPlayer.Local != null ? NetPlayer.Local.PlayerNumber : 1;
        if (!NetSession.IsActive || nm == null) { Show(me, id); return true; }
        if (nm.IsServer) { Broadcast(me, id, NetworkManager.ServerClientId); Show(me, id); return true; }
        SendToHost(nm, id);
        Show(me, id); // 自分の画面にはすぐ出す(HOST は本人へ返さない)
        return true;
    }

    static void SendToHost(NetworkManager nm, byte id)
    {
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)0); w.WriteValueSafe(id);
            NetStats.SendNamed(nm.CustomMessagingManager, Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
    }

    static void Broadcast(int player, byte id, ulong except)
    {
        var nm = NetSession.Manager; if (nm == null) return;
        foreach (ulong c in nm.ConnectedClientsIds)
        {
            if (c == NetworkManager.ServerClientId || c == except) continue;
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)1); w.WriteValueSafe((byte)Mathf.Clamp(player, 0, 255)); w.WriteValueSafe(id);
                NetStats.SendNamed(nm.CustomMessagingManager, Msg, c, w, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    void OnMessage(ulong sender, FastBufferReader r)
    {
        var nm = NetSession.Manager; if (nm == null) return;
        byte kind, id, player = 0;
        try
        {
            r.ReadValueSafe(out kind);
            if (kind != 0) r.ReadValueSafe(out player);
            r.ReadValueSafe(out id);
        }
        catch { return; } // 壊れた/短い知らせは捨てる
        if (kind == 0) // JOIN → HOST
        {
            if (!nm.IsServer) return;
            if (LocProfile.ChatText(id) == null) return;
            if (!recentFrom.TryGetValue(sender, out var q)) recentFrom[sender] = q = new Queue<float>();
            float now = Time.unscaledTime;
            while (q.Count > 0 && now - q.Peek() > GuardWindow) q.Dequeue();
            if (q.Count >= GuardMax) { DroppedBySpamGuard++; Debug.Log($"[QuickChat] dropped (too many from client {sender})"); return; } // 改造した端末の連投
            q.Enqueue(now);
            int from = PlayerNumberOf(sender);
            Show(from, id);
            Broadcast(from, id, sender); // 送った本人へは返さない(本人は送った時に出している)
        }
        else // HOST → JOIN
        {
            if (LocProfile.ChatText(id) == null) return;
            if (NetPlayer.Local != null && NetPlayer.Local.PlayerNumber == player) return; // 自分の分は送った時に出している
            Show(player, id);
        }
    }

    static int PlayerNumberOf(ulong clientId)
    {
        foreach (var p in NetPlayer.All) if (p != null && p.OwnerClientId == clientId) return p.PlayerNumber;
        return 0;
    }

    static void Show(int player, byte id)
    {
        lines.Add(new Line { player = player, id = id, until = Time.unscaledTime + ShowSeconds });
        while (lines.Count > 4) lines.RemoveAt(0);
        Received++;
        Debug.Log($"[QuickChat] P{player}: {id} {LocProfile.ChatText(id)}");
        try { OnReceived?.Invoke(player, id); } catch { }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugInject(int player, byte id) => Show(player, id); // 自動テスト: 受け取った扱い
    // 自動テスト: ボタンの連打止めを通さずに HOST へ n 通まとめて送る(改造した端末の連投の再現。自分の画面には出さない)
    public static void DebugSendRaw(byte id, int n)
    {
        var nm = NetSession.Manager;
        if (!NetSession.IsActive || nm == null || nm.IsServer) return;
        for (int i = 0; i < n; i++) SendToHost(nm, id);
    }
#endif

    // 受け取った文の吹き出し(左下。入力は取らない)
    void OnGUI()
    {
        if (lines.Count == 0) return;
        GUI.depth = -1950;
        float ts = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f));
        Rect safe = StableSafeArea.Rect;
        float x = safe.xMin + 14f * ts, y = Screen.height - safe.yMin - 150f * ts;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            float a = Mathf.Clamp01((l.until - Time.unscaledTime) / 0.4f);
            string who = NetSession.IsActive ? NetPlayer.TagOf(l.player) : "P" + l.player;
            string text = Loc.T(LocProfile.ChatText(l.id));
            var st = UiKit.Label(18f * ts, TextAnchor.MiddleLeft, true, new Color(1f, 1f, 1f, a));
            float tw = st.CalcSize(new GUIContent(who + "  " + text)).x + 24f * ts;
            var r = new Rect(x, y, Mathf.Min(tw, Screen.width * 0.6f), 34f * ts);
            Color keep = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            UiBackdrop.Draw(r, 0.8f);
            GUI.color = keep;
            GUI.Label(new Rect(r.x + 12f * ts, r.y, r.width - 16f * ts, r.height), Loc.Shape(who) + "  " + text, st);
            y -= 40f * ts;
        }
    }

    // マルチの部屋の画面(NetDebugUI、GUILayout)の文のボタン。3 つずつ 3 段
    public static void DrawRoomButtons(GUIStyle buttonStyle, GUIStyle labelStyle)
    {
        GUILayout.Label(Loc.T(LocProfile.QuickChatHeader), labelStyle);
        bool can = CanSend;
        for (int g = 0; g < 3; g++)
        {
            GUILayout.BeginHorizontal();
            foreach (var c in LocProfile.Chat)
            {
                if ((int)c.group != g) continue;
                GUI.enabled = can;
                if (PadNav.LayoutButton(GUILayout.Button(Loc.T(c.text), buttonStyle, GUILayout.Height(40f)))) Send(c.id);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }
    }
}

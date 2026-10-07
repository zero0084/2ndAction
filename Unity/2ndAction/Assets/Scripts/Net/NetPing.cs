using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 往復時間(Ping)の計測(2026-10-07、ONLINE)。1秒ごとに小さなメッセージを送って、返ってくるまでの時間を測る(ゲームの処理の遅れも含む、遊んでいる人が感じる値)。
// Unity Transport の GetCurrentRtt は確実に届く通信の確認応答から求めた値で、その通信が少ない時は大きく出る(遅延0でも約200ms)ため、表示/ログにはこちらを使う。
//  HOST: 全 JOIN へ送り、各 JOIN の最新値の平均/最大。JOIN: HOST へ送る。
public class NetPing : MonoBehaviour
{
    const string MsgPing = "omm.ping", MsgPong = "omm.pong";
    const float Interval = 1f;
    static readonly Dictionary<ulong, float> latest = new Dictionary<ulong, float>();
    public static bool HasSamples => latest.Count > 0;
    public static float AvgMs { get { if (latest.Count == 0) return 0f; float s = 0f; foreach (var v in latest.Values) s += v; return s / latest.Count; } }
    public static float MaxMs { get { float m = 0f; foreach (var v in latest.Values) m = Mathf.Max(m, v); return m; } }

    bool registered; NetworkManager registeredManager;
    float nextAt;

    void Update()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsConnected && nm != null && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (registered) { registered = false; registeredManager = null; }
            latest.Clear();
            return;
        }
        if (!registered || registeredManager != nm)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgPing, NetStats.Counted(MsgPing, OnPing));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgPong, NetStats.Counted(MsgPong, OnPong));
            registered = true; registeredManager = nm; latest.Clear();
        }
        if (Time.realtimeSinceStartup < nextAt) return;
        nextAt = Time.realtimeSinceStartup + Interval;
        double now = Time.realtimeSinceStartupAsDouble;
        if (nm.IsServer)
        {
            foreach (ulong id in nm.ConnectedClientsIds) if (id != NetworkManager.ServerClientId) Send(nm, MsgPing, id, now);
            // 抜けた人の値は消す
            var gone = new List<ulong>();
            foreach (var k in latest.Keys) if (!System.Linq.Enumerable.Contains(nm.ConnectedClientsIds, k)) gone.Add(k);
            foreach (var k in gone) latest.Remove(k);
        }
        else Send(nm, MsgPing, NetworkManager.ServerClientId, now);
    }

    static void Send(NetworkManager nm, string msg, ulong to, double t)
    {
        using var w = new FastBufferWriter(8, Allocator.Temp);
        w.WriteValueSafe(t);
        NetStats.SendNamed(nm.CustomMessagingManager, msg, to, w, NetworkDelivery.Unreliable);
    }

    void OnPing(ulong sender, FastBufferReader r)
    {
        r.ReadValueSafe(out double t);
        var nm = NetSession.Manager;
        if (nm != null && nm.CustomMessagingManager != null) Send(nm, MsgPong, sender, t); // そのまま返す
    }

    void OnPong(ulong sender, FastBufferReader r)
    {
        r.ReadValueSafe(out double t);
        float ms = (float)((Time.realtimeSinceStartupAsDouble - t) * 1000.0);
        if (ms >= 0f && ms < 10000f) latest[sender] = ms;
    }
}

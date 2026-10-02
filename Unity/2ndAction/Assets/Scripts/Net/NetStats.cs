using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

// マルチの負荷の計測(2026-10-02)。最大8人へ増やす時に「送信量/受信量/メッセージ数/同期している物の数」を
// 人数ごとに比べられるよう、自前の送受信(NamedMessage/スナップショット)を種類別に数える。
// 1秒ごとに直近1秒の値(B/s・件/s)へまとめる。表示は NetCombat の開発用パネルと NetAutoTest のログ。
public static class NetStats
{
    class Counter { public long bytes, msgs, lastBytes, lastMsgs; public float bps, mps; }
    static readonly Dictionary<string, Counter> sent = new Dictionary<string, Counter>();
    static readonly Dictionary<string, Counter> recv = new Dictionary<string, Counter>();
    static float windowStart = -1f;

    public static long TotalSentBytes { get; private set; }
    public static long TotalRecvBytes { get; private set; }
    public static float SentBytesPerSec { get; private set; }
    public static float RecvBytesPerSec { get; private set; }
    public static float SentMsgsPerSec { get; private set; }
    public static float RecvMsgsPerSec { get; private set; }
    public static float HostFrameMs { get; private set; }   // 直近1秒の平均フレーム時間(負荷の目安)

    static Counter Get(Dictionary<string, Counter> d, string k) { if (!d.TryGetValue(k, out var c)) { c = new Counter(); d[k] = c; } return c; }

    // 送った(宛先1つにつき1回呼ぶ)
    public static void Sent(string kind, int bytes) { var c = Get(sent, kind); c.bytes += bytes; c.msgs++; TotalSentBytes += bytes; Tick(); }
    public static void Received(string kind, int bytes) { var c = Get(recv, kind); c.bytes += bytes; c.msgs++; TotalRecvBytes += bytes; Tick(); }

    // NamedMessage を送って数える(全ての自前メッセージはここを通す)
    public static void SendNamed(CustomMessagingManager cm, string msg, ulong to, FastBufferWriter w, NetworkDelivery d)
    {
        cm.SendNamedMessage(msg, to, w, d);
        Sent(msg, w.Length);
    }
    // 受け取りを数えてから本来の処理へ渡す
    public static CustomMessagingManager.HandleNamedMessageDelegate Counted(string msg, CustomMessagingManager.HandleNamedMessageDelegate h)
    {
        return (sender, r) => { Received(msg, r.Length); h(sender, r); };
    }

    static float frameAcc; static int frameN;
    public static void Tick()
    {
        float now = Time.realtimeSinceStartup;
        if (windowStart < 0f) { windowStart = now; return; }
        float dt = now - windowStart;
        if (dt < 1f) return;
        float sb = 0f, rb = 0f, sm = 0f, rm = 0f;
        foreach (var c in sent.Values) { c.bps = (c.bytes - c.lastBytes) / dt; c.mps = (c.msgs - c.lastMsgs) / dt; c.lastBytes = c.bytes; c.lastMsgs = c.msgs; sb += c.bps; sm += c.mps; }
        foreach (var c in recv.Values) { c.bps = (c.bytes - c.lastBytes) / dt; c.mps = (c.msgs - c.lastMsgs) / dt; c.lastBytes = c.bytes; c.lastMsgs = c.msgs; rb += c.bps; rm += c.mps; }
        SentBytesPerSec = sb; RecvBytesPerSec = rb; SentMsgsPerSec = sm; RecvMsgsPerSec = rm;
        HostFrameMs = frameN > 0 ? frameAcc / frameN * 1000f : 0f; frameAcc = 0f; frameN = 0;
        windowStart = now;
    }
    // 毎フレーム(NetCombatのUpdateから)
    public static void Frame() { frameAcc += Time.unscaledDeltaTime; frameN++; Tick(); }

    public static string Summary(bool perKind = false)
    {
        var sbd = new StringBuilder();
        sbd.Append($"net out {SentBytesPerSec / 1024f:F1}KB/s {SentMsgsPerSec:F0}msg/s  in {RecvBytesPerSec / 1024f:F1}KB/s {RecvMsgsPerSec:F0}msg/s  frame {HostFrameMs:F1}ms  players {NetSession.ConnectedPlayerCount}");
        if (perKind) sbd.Append(KindSummary());
        return sbd.ToString();
    }

    // 種類別(直近1秒、0.5B/s未満は省略)
    public static string KindSummary()
    {
        var sbd = new StringBuilder();
        sbd.Append("\n out:");
        foreach (var kv in sent) if (kv.Value.bps > 0.5f) sbd.Append($" {kv.Key}={kv.Value.bps:F0}B/s");
        sbd.Append("\n in:");
        foreach (var kv in recv) if (kv.Value.bps > 0.5f) sbd.Append($" {kv.Key}={kv.Value.bps:F0}B/s");
        return sbd.ToString();
    }
}

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
    public static float MaxFrameMs { get; private set; }    // 直近1秒の最大フレーム時間(引っかかりの目安)
    public static float Fps => HostFrameMs > 0.01f ? 1000f / HostFrameMs : 0f;
    // Ping(往復時間、ms)。HOSTは全JOINの平均/最大、JOINはHOSTまで。1秒ごと。RunMaxPingMsはこの起動中の最大
    public static float AvgPingMs { get; private set; }
    public static float MaxPingMs { get; private set; }
    public static float RunMaxPingMs { get; private set; }

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

    static float frameAcc, frameMax; static int frameN;
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
        HostFrameMs = frameN > 0 ? frameAcc / frameN * 1000f : 0f; MaxFrameMs = frameMax * 1000f; frameAcc = 0f; frameMax = 0f; frameN = 0;
        SamplePing();
        windowStart = now;
    }
    // 毎フレーム(NetCombatのUpdateから)
    public static void Frame() { frameAcc += Time.unscaledDeltaTime; frameMax = Mathf.Max(frameMax, Time.unscaledDeltaTime); frameN++; Tick(); }

    static void SamplePing()
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || !NetSession.IsActive || nm.NetworkConfig == null || nm.NetworkConfig.NetworkTransport == null) { AvgPingMs = MaxPingMs = 0f; return; }
        var tr = nm.NetworkConfig.NetworkTransport;
        float sum = 0f, max = 0f; int n = 0;
        try
        {
            if (nm.IsServer)
            {
                foreach (ulong id in nm.ConnectedClientsIds)
                {
                    if (id == NetworkManager.ServerClientId) continue;
                    float rtt = tr.GetCurrentRtt(id); sum += rtt; max = Mathf.Max(max, rtt); n++;
                }
            }
            else { float rtt = tr.GetCurrentRtt(NetworkManager.ServerClientId); sum = rtt; max = rtt; n = 1; }
        }
        catch (System.Exception) { n = 0; }
        AvgPingMs = n > 0 ? sum / n : 0f;
        MaxPingMs = max;
        RunMaxPingMs = Mathf.Max(RunMaxPingMs, max);
    }

    // ===== 人数ごとの負荷の基礎情報(DebugのNET COMBATパネル/自動テストのログ)=====
    // 数は1秒に1回だけ数える(シーン内の検索を毎フレームしない)。
    public struct Load
    {
        public int Connected, Active, Alive;
        public int Enemies, Bosses, Projectiles;       // この端末のシーン内の総数(JOINはパペットを含む)
        public int NetEnemies, NetBosses, NetAttacks;  // 共有している数(NetCombat/NetAttackSync)
        public int AiUpdates;                          // この端末でAIが動いている敵/ボスの数(HOST/シングル。JOINはパペットなので0)
    }
    static Load load; static float loadAt = -10f;
    public static Load CurrentLoad()
    {
        if (Time.realtimeSinceStartup - loadAt < 1f) return load;
        loadAt = Time.realtimeSinceStartup;
        var l = new Load { Connected = WorldRange.ConnectedCount, Active = WorldRange.ActiveCount, Alive = WorldRange.AliveCount };
        bool ai = !NetCombat.Replica;
        foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e.isActiveAndEnabled) { l.Enemies++; if (ai) l.AiUpdates++; }
        foreach (var b in Object.FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b.isActiveAndEnabled) l.Bosses++;
        foreach (var b in Object.FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (b.isActiveAndEnabled) l.Bosses++;
        foreach (var b in Object.FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (b.isActiveAndEnabled) l.Bosses++;
        if (ai) l.AiUpdates += l.Bosses;
        l.Projectiles = Object.FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length
            + Object.FindObjectsByType<FireballController>(FindObjectsSortMode.None).Length
            + Object.FindObjectsByType<PlayerBullet>(FindObjectsSortMode.None).Length;
        var nc = NetCombat.Instance; var ats = NetAttackSync.Instance;
        if (nc != null) { l.NetEnemies = nc.SharedEnemyCount; l.NetBosses = nc.SharedBossCount; }
        if (ats != null) l.NetAttacks = ats.TrackedCount;
        load = l;
        return l;
    }

    public static string LoadLine()
    {
        var l = CurrentLoad();
        return $"players conn={l.Connected} active={l.Active} alive={l.Alive} | enemies={l.Enemies} bosses={l.Bosses} projectiles={l.Projectiles} aiUpdates={l.AiUpdates}"
            + $" | net enemies={l.NetEnemies} bosses={l.NetBosses} attacks={l.NetAttacks}"
            + $" | msg out={SentMsgsPerSec:F0}/s in={RecvMsgsPerSec:F0}/s bytes out={SentBytesPerSec / 1024f:F1}KB/s in={RecvBytesPerSec / 1024f:F1}KB/s"
            + $" | ping avg={AvgPingMs:F0}ms max={MaxPingMs:F0}ms runMax={RunMaxPingMs:F0}ms | fps={Fps:F0} frame avg={HostFrameMs:F1}ms max={MaxFrameMs:F1}ms";
    }

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

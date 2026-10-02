using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 障害物の耐久力のマルチ同期(2026-09-29)。敵/ボスのNetCombatと同じ「HOST権威」の設計:
//  - 障害物の配置はHOSTだけが行い(ランダム配置のため)、ID/論理座標/種類を全員へ送る。JOINは自分では置かず、
//    届いた内容で同じ障害物を作る(=全員が同じ場所に同じ障害物を見る)。
//  - 耐久力/破壊はHOSTが確定して全員へ送る。JOINの攻撃が当たったらHOSTへ「ダメージ要求」を送り(hitSeqで二重処理防止)、
//    自分の画面では結果を先に見せる(予測。高速で走っていても壊した障害物に遅れてぶつからない)。
//  - 壊れた障害物は、配置/再生成/同期のどの経路でも復活しない(壊れたIDを記録し、後から届いた配置・HPは無視)。
//  - 体当たりの被弾はこれまでどおり各自の端末で判定(プレイヤーのHPはNetMatchの既存経路)。
//  - 破壊の演出は各端末で1回だけ(Broken済みなら何もしない)。報酬は無い。
[DefaultExecutionOrder(1160)]
public class NetObstacles : MonoBehaviour
{
    public static NetObstacles Instance { get; private set; }
    const string MsgReliable = "OMM.Obstacle";      // HOST→JOIN: 配置/耐久力/破壊
    const string MsgHit = "OMM.ObstacleHit";        // JOIN→HOST: ダメージ要求
    const byte OpSpawn = 1, OpDamage = 2, OpBreak = 3;

    public static bool Authority => NetCombat.Authority;
    public static bool Replica => NetCombat.Replica;
    public static bool SuppressLocalSpawn => Replica;
    public static int LocalPlayerNumber => NetCombat.LocalPlayerNumber;

    readonly Dictionary<int, ObstacleController> byId = new Dictionary<int, ObstacleController>();
    readonly HashSet<int> brokenIds = new HashSet<int>();
    readonly Dictionary<ulong, HashSet<int>> processedHits = new Dictionary<ulong, HashSet<int>>();
    readonly List<(int seed, byte[] data)> pending = new List<(int, byte[])>();
    int nextId = 1, localHitSeq;
    bool handlersRegistered;
    NetworkManager registeredManager;

    // 集計(自動テスト用)
    public static int StatSpawnsSent, StatSpawnsRecv, StatDamageSent, StatBreakSent, StatBreakRecv, StatHitReqSent, StatHitReqApplied, StatHitReqIgnored, StatDupHits, StatReviveBlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[NetObstacles]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetObstacles>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        byId.Clear(); brokenIds.Clear(); processedHits.Clear();
        nextId = 1;
        StatSpawnsSent = StatSpawnsRecv = StatDamageSent = StatBreakSent = StatBreakRecv = StatHitReqSent = StatHitReqApplied = StatHitReqIgnored = StatDupHits = StatReviveBlocked = 0;
    }

    static void Log(string msg) => Debug.Log("[NET][OBSTACLE] " + msg);

    void Update()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!handlersRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgReliable, NetStats.Counted(MsgReliable, OnReliable));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgHit, NetStats.Counted(MsgHit, OnHitRequest));
            handlersRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlersRegistered) { handlersRegistered = false; registeredManager = null; }
        if (Replica) ProcessPending();
    }

    public static ObstacleController Find(int id) => Instance != null && Instance.byId.TryGetValue(id, out var o) ? o : null;
    public static bool IsBrokenId(int id) => Instance != null && Instance.brokenIds.Contains(id);

    // ===================================================================== //
    // HOST
    // ===================================================================== //

    // ObstacleSpawnerが障害物を確定配置した直後(HOST/ソロ)。ソロでは何もしない。
    public static void OnSpawned(ObstacleController o, string stageId, int specIndex, float angle, bool upper)
    {
        if (!Authority || Instance == null || o == null) return;
        o.NetId = Instance.nextId++;
        Instance.byId[o.NetId] = o;
        double lx = o.transform.position.x + FloatingOrigin.Offset;
        using (var w = new FastBufferWriter(128, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(OpSpawn);
            w.WriteValueSafe(o.NetId);
            w.WriteValueSafe(new FixedString32Bytes(stageId ?? ""));
            w.WriteValueSafe(specIndex);
            w.WriteValueSafe(lx); w.WriteValueSafe(o.transform.position.y);
            w.WriteValueSafe(angle);
            w.WriteValueSafe(upper);
            w.WriteValueSafe(o.Hp);
            SendToClients(w);
        }
        StatSpawnsSent++;
    }

    public static void AuthorityDamaged(ObstacleController o, int damage, int attacker)
    {
        if (!Authority || Instance == null || o == null || o.NetId == 0) return;
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(OpDamage);
            w.WriteValueSafe(o.NetId); w.WriteValueSafe(o.Hp); w.WriteValueSafe(attacker);
            SendToClients(w);
        }
        StatDamageSent++;
    }

    public static void AuthorityBroken(ObstacleController o)
    {
        if (Instance == null || o == null || o.NetId == 0) return;
        Instance.brokenIds.Add(o.NetId);
        if (!Authority) return; // JOINの予測破壊: HOSTの確定を待つ(IDだけ記録して復活を防ぐ)
        using (var w = new FastBufferWriter(24, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(OpBreak);
            w.WriteValueSafe(o.NetId);
            SendToClients(w);
        }
        StatBreakSent++;
        Log($"Break id={o.NetId} kind={o.kind}");
    }

    static void SendToClients(FastBufferWriter w)
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null) return;
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId) continue;
            NetStats.SendNamed(nm.CustomMessagingManager, MsgReliable, clientId, w, NetworkDelivery.ReliableSequenced);
        }
    }

    void OnHitRequest(ulong sender, FastBufferReader r)
    {
        if (!Authority) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out int id);
        r.ReadValueSafe(out int hitSeq);
        r.ReadValueSafe(out int damage);
        if (!processedHits.TryGetValue(sender, out var seen)) processedHits[sender] = seen = new HashSet<int>();
        if (!seen.Add(hitSeq)) { StatDupHits++; return; }
        if (seen.Count > 4096) seen.Clear();
        int attacker = NetCombat.PlayerNumberOfClient(sender);
        if (attacker <= 0) { StatHitReqIgnored++; Log($"hit request id={id} from unknown client {sender} ignored"); return; }
        if (seed != NetRunLauncher.ActiveRunSeed || !byId.TryGetValue(id, out var o) || o == null || o.Broken)
        {
            StatHitReqIgnored++;
            Log($"hit request id={id} from P{attacker} ignored (unknown/old/already broken)");
            return;
        }
        StatHitReqApplied++;
        o.ApplyDamage(Mathf.Clamp(damage, 1, 999999), o.transform.position + Vector3.up * 0.5f, attacker);
    }

    // ===================================================================== //
    // JOIN
    // ===================================================================== //

    public static void RequestHit(ObstacleController o, int damage)
    {
        if (!Replica || Instance == null || o == null || o.NetId == 0) return;
        NetworkManager nm = NetSession.Manager;
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(o.NetId);
            w.WriteValueSafe(++Instance.localHitSeq);
            w.WriteValueSafe(damage);
            NetStats.SendNamed(nm.CustomMessagingManager, MsgHit, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
        StatHitReqSent++;
    }

    void OnReliable(ulong sender, FastBufferReader r)
    {
        if (NetSession.Manager == null || NetSession.Manager.IsServer) return;
        int start = r.Position;
        int len = r.Length - start;
        var data = new byte[len];
        r.ReadBytesSafe(ref data, len);
        pending.Add((System.BitConverter.ToInt32(data, 0), data));
        ProcessPending();
    }

    static bool SceneReadyFor(int seed) =>
        NetRunLauncher.IsMultiplayerRun && NetRunLauncher.ActiveRunSeed == seed && TerrainManager.Instance != null && GameManager.Instance != null;

    void ProcessPending()
    {
        int i = 0;
        while (i < pending.Count)
        {
            var (seed, data) = pending[i];
            if (SceneReadyFor(seed))
            {
                pending.RemoveAt(i);
                using (var reader = new FastBufferReader(data, Allocator.Temp))
                {
                    try { Handle(reader); }
                    catch (System.Exception ex) { Debug.LogWarning("[NET][OBSTACLE] message failed: " + ex.Message); }
                }
                continue;
            }
            if (seed == NetRunLauncher.PendingRunSeed) { i++; continue; }
            pending.RemoveAt(i);
        }
        if (pending.Count > 2000) pending.RemoveRange(0, pending.Count - 2000);
    }

    void Handle(FastBufferReader r)
    {
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out byte op);
        r.ReadValueSafe(out int id);
        if (op == OpSpawn)
        {
            r.ReadValueSafe(out FixedString32Bytes stageId);
            r.ReadValueSafe(out int specIndex);
            r.ReadValueSafe(out double lx); r.ReadValueSafe(out float y);
            r.ReadValueSafe(out float angle);
            r.ReadValueSafe(out bool upper);
            r.ReadValueSafe(out int hp);
            if (byId.ContainsKey(id) || brokenIds.Contains(id)) { StatReviveBlocked++; return; } // 復活させない
            var spawner = ObstacleSpawner.FindForStage(stageId.ToString());
            if (spawner == null) return;
            var o = spawner.CreateReplica(specIndex, new Vector2((float)(lx - FloatingOrigin.Offset), y), angle, upper);
            if (o == null) return;
            o.NetId = id;
            o.NetReplica = true;
            o.NetSetHp(hp, false);
            byId[id] = o;
            StatSpawnsRecv++;
        }
        else if (op == OpDamage)
        {
            r.ReadValueSafe(out int hp); r.ReadValueSafe(out int attacker);
            if (!byId.TryGetValue(id, out var o) || o == null || o.Broken) return;
            o.NetSetHp(hp, attacker != LocalPlayerNumber); // 自分の攻撃の手応えは当てた瞬間に出している
        }
        else if (op == OpBreak)
        {
            StatBreakRecv++;
            brokenIds.Add(id);
            if (!byId.TryGetValue(id, out var o) || o == null || o.Broken) return; // 予測で壊し済み: 演出を重ねない
            o.Break(o.transform.position + Vector3.up * 0.5f);
        }
    }
}

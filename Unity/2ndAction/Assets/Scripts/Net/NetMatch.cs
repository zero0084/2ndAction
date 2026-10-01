using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチプレイPhase 2.5(2026-09-27) - プレイヤーのHP/状態/カード選択を「ネットワーク上で1つの結果」に
// するための層。敵HP(NetCombat)と同じく、プレイヤーHPの最終的な権威もHOSTに一本化する。
//
// ダメージの流れ(1Hitは必ず1Damage、二重処理しない):
//  - HOSTのプレイヤー: 既存のPlayerController.TakeDamage → GameManager.TryDamagePlayer がそのまま
//    HOST上でHPを確定する(HOST=権威)。結果の表はここから全員へ配る。
//  - JOINのプレイヤー: JOIN端末が「自分の画面で起きた当たり」(敵パペットとの接触/共有された攻撃/
//    穴・壁などの環境)を検出したら、HPは減らさずに「被弾申告」をHOSTへ送る。HOSTが検証して
//    (生存中か/無敵時間中でないか/同じ攻撃で既に当たっていないか/攻撃が実在するか)HPを確定し、
//    結果(HitResult)と最新の表を配る。JOINは確定を受け取ってから被弾リアクションを行う。
//    落下だけは位置の復帰を待てないので即座に復帰し、HPはHOSTの確定に従う。
//  - HOST上でしか判定できない範囲攻撃(落石/せり上がる結晶など)は、HOSTが相手の分身の位置で
//    直接判定して確定する(HostDamageRemoteInRange)。
//  - 回復/最大HP変更(レベルアップの全回復/カード/吸収)も、JOINは要求を送りHOSTが適用する。
//
// カード選択(レベルアップ/ボス報酬)はマルチでは世界を止めない(TimeControl.Pauseを使わない)。
// 選んでいる人の端末にだけUIが出て、その人の IsChoosingCard がHOSTへ伝わる。ボス報酬は
// ボスのラストヒットを取った人(RewardRecipient)の端末にだけ出る。
[DefaultExecutionOrder(1160)]
public partial class NetMatch : MonoBehaviour
{
    public static NetMatch Instance { get; private set; }

    const string MsgToHost = "OMM.MatchReq";     // JOIN→HOST(Reliable)
    const string MsgToClients = "OMM.MatchSync"; // HOST→JOIN(Reliable)

    const byte ReqHello = 1, ReqClaim = 2, ReqHeal = 3, ReqSetMax = 4, ReqChoosing = 5, ReqDebugSetHp = 6, ReqRevive = 7, ReqDebugForceOut = 8;
    const byte SyncTable = 1, SyncHitResult = 2, SyncBossReward = 3, SyncRunOver = 4, SyncRevived = 5;

    public enum ClaimKind : byte { Environment = 0, EnemyContact = 1, Attack = 2 }
    public enum PState : byte { Alive = 0, Down = 1, Eliminated = 2, Out = 3 }

    public class Rec
    {
        public int Pn;
        public ulong ClientId;
        public int Hp, MaxHp;
        public PState State;
        public bool Choosing;
        public double Distance;
        public double DownDistance, DownX; public float DownY;
        public double FinalDistance;
        public int Kills, BossLastHits;
        public float InvulnUntil = -99f;      // HOSTの実時間
        public readonly HashSet<long> HitKeys = new HashSet<long>();
        public int HitsTaken, ClaimsRejected;
        public bool Known;                    // HOSTがこの人の初期HPを受け取った
    }

    readonly Dictionary<int, Rec> recs = new Dictionary<int, Rec>();
    public IReadOnlyDictionary<int, Rec> Records => recs;

    // ---- 設定(テスト用) ----
    // 旧テスト用フラグ(-netChoiceInvincible)。2026-09-28からカード選択中の本人は常に被弾しない
    // (選択中はその場で一時停止するため)ので、現在は判定には使っていない。
    public static bool ChoiceInvincible;

    // ---- 役割 ----
    public static bool Active => NetCombat.Authority || NetCombat.Replica;
    // JOIN: 自分のHPはHOSTが決める(ローカルで減らさず申告する)。
    public static bool ClientRoutesHp => NetCombat.Replica;

    public static bool IsPlayerActive(int pn)
    {
        if (Instance == null || !Active) return true;
        return !Instance.recs.TryGetValue(pn, out Rec r) || r.State == PState.Alive;
    }

    public static bool IsLocalAlive => IsPlayerActive(NetCombat.LocalPlayerNumber);

    // カード選択中(その場で一時停止中)か。敵/ボスの狙いと被弾の対象から外すのに使う(2026-09-28)。
    public static bool IsPlayerChoosing(int pn)
    {
        if (Instance == null || !Active) return false;
        return Instance.recs.TryGetValue(pn, out Rec r) && r.Choosing && r.State == PState.Alive;
    }

    public static Rec Get(int pn) => Instance != null && Instance.recs.TryGetValue(pn, out Rec r) ? r : null;

    // 統計(自動テスト/デバッグ用)
    public int StatClaimsSent, StatClaimsAccepted, StatClaimsRejected, StatHitsConfirmed, StatHostRemoteHits;
    public readonly List<string> EventLog = new List<string>();

    bool handlersRegistered;
    NetworkManager registeredManager;
    int helloSeed;
    int claimSeq;
    float syncTimer;
    int lastSyncHash;
    bool dirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[NetMatch]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetMatch>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ResetForScene();
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string a in args) if (a == "-netChoiceInvincible") ChoiceInvincible = true;
    }

    void ResetForScene()
    {
        recs.Clear();
        helloSeed = 0;
        EventLog.Clear();
        StatClaimsSent = StatClaimsAccepted = StatClaimsRejected = StatHitsConfirmed = StatHostRemoteHits = 0;
        OnSceneResetPhase3();
    }

    void Log(string msg)
    {
        string line = $"[NET][PLAYER] {msg}";
        Debug.Log(line);
        if (EventLog.Count < 400) EventLog.Add(msg);
    }

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //

    void Update()
    {
        RegisterHandlers();
        if (!Active) return;
        GameManager gm = GameManager.Instance;
        PlayerController pc = PlayerController.Instance;
        if (gm == null || pc == null || !gm.HasStarted) return;

        int local = NetCombat.LocalPlayerNumber;
        if (NetCombat.Authority)
        {
            // HOST自身の記録はHOSTのGameManagerが持つ値そのもの(HOSTが権威)。
            Rec me = GetOrCreate(local, NetworkManager.ServerClientId);
            if (!me.Known) { me.Known = true; me.Hp = gm.Lives; me.MaxHp = gm.MaxLives; dirty = true; }
            if (me.State == PState.Alive && (me.Hp != gm.Lives || me.MaxHp != gm.MaxLives)) { me.Hp = gm.Lives; me.MaxHp = gm.MaxLives; dirty = true; }
            bool choosing = gm.IsLocalChoiceOpen;
            if (me.Choosing != choosing) { me.Choosing = choosing; dirty = true; Log($"P{local} IsChoosingCard={choosing}"); }
            if (me.State == PState.Alive) me.Distance = pc.DistanceExact;
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p == null || p.IsOwner) continue;
                if (recs.TryGetValue(p.PlayerNumber, out Rec r) && r.State == PState.Alive) r.Distance = p.RemoteDistance;
            }
            UpdatePhase3Host(gm, pc);
            syncTimer += Time.unscaledDeltaTime;
            int h = TableHash();
            if (dirty || h != lastSyncHash || syncTimer > 1f) { SendTable(); lastSyncHash = h; dirty = false; syncTimer = 0f; }
        }
        else if (NetCombat.Replica)
        {
            if (helloSeed != NetRunLauncher.ActiveRunSeed && NetPlayer.Local != null)
            {
                helloSeed = NetRunLauncher.ActiveRunSeed;
                SendToHost(w => { w.WriteValueSafe(ReqHello); w.WriteValueSafe(gm.Lives); w.WriteValueSafe(gm.MaxLives); });
                Log($"P{local} hello hp={gm.Lives}/{gm.MaxLives}");
            }
            bool choosing = gm.IsLocalChoiceOpen;
            Rec me = Get(local);
            if (me != null && me.Choosing != choosing) { me.Choosing = choosing; SendToHost(w => { w.WriteValueSafe(ReqChoosing); w.WriteValueSafe(choosing); }); Log($"P{local} IsChoosingCard={choosing}"); }
            UpdatePhase3Client(gm, pc);
        }
    }

    Rec GetOrCreate(int pn, ulong clientId)
    {
        if (!recs.TryGetValue(pn, out Rec r))
        {
            r = new Rec { Pn = pn, ClientId = clientId, Hp = 3 * CombatScale.HpPerHeart, MaxHp = 3 * CombatScale.HpPerHeart };
            recs[pn] = r;
        }
        r.ClientId = clientId;
        return r;
    }

    int TableHash()
    {
        unchecked
        {
            int h = 17;
            foreach (var r in recs.Values)
                h = h * 31 + r.Pn * 7 + r.Hp * 131 + r.MaxHp * 17 + (int)r.State * 1009 + (r.Choosing ? 3 : 0) + r.Kills * 13 + r.BossLastHits * 29 + Mathf.RoundToInt((float)r.DownDistance) + Mathf.RoundToInt((float)r.FinalDistance) * 3;
            return h;
        }
    }

    // ===================================================================== //
    // 送受信
    // ===================================================================== //

    void RegisterHandlers()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!handlersRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgToHost, OnToHost);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgToClients, OnToClient);
            handlersRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlersRegistered)
        {
            handlersRegistered = false;
            registeredManager = null;
        }
    }

    static void SendToHost(System.Action<FastBufferWriter> write)
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || nm.CustomMessagingManager == null) return;
        using (var w = new FastBufferWriter(256, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(MsgToHost, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
    }

    static void SendToClients(System.Action<FastBufferWriter> write, ulong onlyClient = ulong.MaxValue)
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || nm.CustomMessagingManager == null) return;
        using (var w = new FastBufferWriter(1024, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            write(w);
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                if (onlyClient != ulong.MaxValue && clientId != onlyClient) continue;
                nm.CustomMessagingManager.SendNamedMessage(MsgToClients, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    void SendTable()
    {
        SendToClients(w =>
        {
            w.WriteValueSafe(SyncTable);
            w.WriteValueSafe((byte)recs.Count);
            foreach (var r in recs.Values) WriteRec(w, r);
            WritePhase3Header(w);
        });
    }

    static void WriteRec(FastBufferWriter w, Rec r)
    {
        w.WriteValueSafe((byte)r.Pn); w.WriteValueSafe(r.Hp); w.WriteValueSafe(r.MaxHp); w.WriteValueSafe((byte)r.State);
        w.WriteValueSafe(r.Choosing); w.WriteValueSafe(r.Distance); w.WriteValueSafe(r.DownDistance); w.WriteValueSafe(r.DownX); w.WriteValueSafe(r.DownY);
        w.WriteValueSafe(r.FinalDistance); w.WriteValueSafe(r.Kills); w.WriteValueSafe(r.BossLastHits);
    }

    Rec ReadRec(FastBufferReader rd)
    {
        rd.ReadValueSafe(out byte pn);
        Rec r = GetOrCreate(pn, 0);
        rd.ReadValueSafe(out r.Hp); rd.ReadValueSafe(out r.MaxHp); rd.ReadValueSafe(out byte st); r.State = (PState)st;
        rd.ReadValueSafe(out bool ch);
        if (pn != NetCombat.LocalPlayerNumber) r.Choosing = ch; // 自分の選択状態は自分の端末が正
        rd.ReadValueSafe(out r.Distance); rd.ReadValueSafe(out r.DownDistance); rd.ReadValueSafe(out r.DownX); rd.ReadValueSafe(out r.DownY);
        rd.ReadValueSafe(out r.FinalDistance); rd.ReadValueSafe(out r.Kills); rd.ReadValueSafe(out r.BossLastHits);
        r.Known = true;
        return r;
    }

    // ---------------- HOST: JOINからの要求 ----------------

    void OnToHost(ulong sender, FastBufferReader r)
    {
        if (!NetCombat.Authority) return;
        r.ReadValueSafe(out int seed);
        if (seed != NetRunLauncher.ActiveRunSeed) return;
        r.ReadValueSafe(out byte op);
        int pn = NetCombat.PlayerNumberOfClient(sender);
        Rec rec = GetOrCreate(pn, sender);
        switch (op)
        {
            case ReqHello:
            {
                r.ReadValueSafe(out int hp); r.ReadValueSafe(out int max);
                if (!rec.Known) { rec.Hp = hp; rec.MaxHp = max; rec.Known = true; rec.State = PState.Alive; }
                Log($"P{pn} joined run hp={rec.Hp}/{rec.MaxHp}");
                dirty = true;
                break;
            }
            case ReqClaim:
            {
                r.ReadValueSafe(out int seq); r.ReadValueSafe(out byte kind); r.ReadValueSafe(out int id); r.ReadValueSafe(out bool isFall);
                r.ReadValueSafe(out float invuln); r.ReadValueSafe(out float slowF); r.ReadValueSafe(out float slowD);
                r.ReadValueSafe(out FixedString64Bytes src);
                HostResolveClaim(rec, seq, (ClaimKind)kind, id, isFall, invuln, slowF, slowD, src.ToString());
                break;
            }
            case ReqHeal:
            {
                r.ReadValueSafe(out int amount);
                if (rec.State == PState.Alive) { rec.Hp = Mathf.Min(rec.MaxHp, rec.Hp + Mathf.Max(0, amount)); dirty = true; Log($"P{pn} heal +{amount} -> hp={rec.Hp}/{rec.MaxHp}"); }
                break;
            }
            case ReqSetMax:
            {
                r.ReadValueSafe(out int max); r.ReadValueSafe(out bool restore);
                if (rec.State == PState.Alive)
                {
                    rec.MaxHp = Mathf.Max(1, max);
                    rec.Hp = restore ? rec.MaxHp : Mathf.Min(rec.Hp, rec.MaxHp);
                    dirty = true;
                    Log($"P{pn} maxHp={rec.MaxHp} restore={restore} -> hp={rec.Hp}");
                }
                break;
            }
            case ReqChoosing:
            {
                r.ReadValueSafe(out bool ch);
                rec.Choosing = ch; dirty = true;
                Log($"P{pn} IsChoosingCard={ch}");
                break;
            }
            case ReqDebugSetHp:
            {
                r.ReadValueSafe(out int hp);
                if (!Debug.isDebugBuild) break; // テスト/デバッグ用(リリースビルドでは受け付けない)
                if (rec.State == PState.Alive) { rec.Hp = Mathf.Clamp(hp, 1, 999); rec.MaxHp = Mathf.Max(rec.MaxHp, rec.Hp); dirty = true; Log($"P{pn} debug hp={rec.Hp}"); }
                break;
            }
            case ReqDebugForceOut:
            {
                if (!Debug.isDebugBuild) break; // テスト用(リリースビルドでは受け付けない)
                HostDebugForceOut(rec);
                break;
            }
            case ReqRevive:
            {
                r.ReadValueSafe(out byte downPn);
                HostHandleReviveRequest(pn, downPn);
                break;
            }
        }
    }

    void HostResolveClaim(Rec rec, int seq, ClaimKind kind, int id, bool isFall, float invuln, float slowF, float slowD, string source)
    {
        float now = Time.realtimeSinceStartup;
        string reject = null;
        long key = ((long)kind << 32) | (uint)id;
        if (rec.State != PState.Alive) reject = $"not alive ({rec.State})";
        else if (!isFall && rec.Choosing) reject = "choosing card (paused in place)";
        else if (!isFall && now < rec.InvulnUntil) reject = $"invulnerable {rec.InvulnUntil - now:F2}s";
        else if (kind != ClaimKind.Environment && rec.HitKeys.Contains(key)) reject = "same attack already hit";
        else if (kind == ClaimKind.EnemyContact)
        {
            var e = NetCombat.Find(id);
            if (e == null || e.Dead || e.Go == null) reject = $"enemy {id} not alive";
        }
        else if (kind == ClaimKind.Attack && !NetAttackSync.IsValidAttack(id)) reject = $"attack {id} unknown/expired";

        if (reject != null)
        {
            rec.ClaimsRejected++; StatClaimsRejected++;
            Log($"Damage claim P{rec.Pn} src={source} kind={kind} id={id} REJECTED: {reject}");
            SendHitResult(rec, seq, false, isFall, slowF, slowD, source);
            return;
        }
        if (kind != ClaimKind.Environment) rec.HitKeys.Add(key);
        if (kind == ClaimKind.Attack) NetAttackSync.ConsumeOnHit(id);
        if (kind == ClaimKind.EnemyContact) rec.HitKeys.Remove(key); // 同じ敵への接触は無敵明けに再び当たり得る(元の挙動と同じ)
        ApplyHostDamage(rec, isFall, invuln, source);
        StatClaimsAccepted++;
        SendHitResult(rec, seq, true, isFall, slowF, slowD, source);
    }

    void ApplyHostDamage(Rec rec, bool isFall, float invuln, string source)
    {
        float now = Time.realtimeSinceStartup;
        int before = rec.Hp;
        rec.Hp = Mathf.Max(0, rec.Hp - CombatScale.PlayerHit); // JOINの被弾は常にハート1つ分(10倍スケール)
        rec.HitsTaken++;
        rec.InvulnUntil = now + Mathf.Clamp(invuln, 0.2f, 4f);
        Log($"Damage P{rec.Pn} src={source} fall={isFall} hp {before} -> {rec.Hp} (authority=HOST){(rec.Choosing ? " [while choosing card]" : "")}");
        if (rec.Hp <= 0) OnPlayerHpZero(rec, source);
        dirty = true;
        SendTable();
        dirty = false;
    }

    void SendHitResult(Rec rec, int seq, bool accepted, bool isFall, float slowF, float slowD, string source)
    {
        SendToClients(w =>
        {
            w.WriteValueSafe(SyncHitResult);
            w.WriteValueSafe((byte)rec.Pn); w.WriteValueSafe(seq); w.WriteValueSafe(accepted); w.WriteValueSafe(isFall);
            w.WriteValueSafe(rec.Hp); w.WriteValueSafe(slowF); w.WriteValueSafe(slowD);
            w.WriteValueSafe(new FixedString64Bytes(source ?? ""));
        }, rec.ClientId);
    }

    static int hostAttackKeySeq;
    public static int NewHostAttackKey() => ++hostAttackKeySeq;

    // HOST上でしか判定できない範囲攻撃(落石など): 相手の分身の位置で判定し、HOSTが直接確定する。
    public static void HostDamageRemoteInRange(float sceneX0, float sceneX1, float sceneYMin, float sceneYMax, string source, int attackKey = 0)
    {
        if (Instance == null || !NetCombat.Authority) return;
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (p == null || p.IsOwner || p.Avatar == null) continue;
            Rec rec = Get(p.PlayerNumber);
            if (rec == null || rec.State != PState.Alive) continue;
            if (rec.Choosing) continue; // カード選択中の人は被弾しない(2026-09-28)
            Vector3 pos = p.Avatar.transform.position;
            if (pos.x < sceneX0 || pos.x > sceneX1 || pos.y < sceneYMin || pos.y > sceneYMax) continue;
            float now = Time.realtimeSinceStartup;
            if (now < rec.InvulnUntil) continue;
            long key = (3L << 32) | (uint)attackKey; // HOST判定の範囲攻撃専用の番号空間
            if (attackKey != 0 && rec.HitKeys.Contains(key)) continue;
            if (attackKey != 0) rec.HitKeys.Add(key);
            Instance.StatHostRemoteHits++;
            Instance.ApplyHostDamage(rec, false, 1.5f, source + "(host-judged)");
            Instance.SendHitResult(rec, -1, true, false, 1f, 0f, source);
        }
    }

    // ---------------- JOIN: HOSTからの通知 ----------------

    void OnToClient(ulong sender, FastBufferReader r)
    {
        if (sender != NetworkManager.ServerClientId) return;
        r.ReadValueSafe(out int seed);
        if (seed != NetRunLauncher.ActiveRunSeed) return;
        r.ReadValueSafe(out byte op);
        GameManager gm = GameManager.Instance;
        PlayerController pc = PlayerController.Instance;
        int local = NetCombat.LocalPlayerNumber;
        switch (op)
        {
            case SyncTable:
            {
                r.ReadValueSafe(out byte n);
                for (int i = 0; i < n; i++) ReadRec(r);
                ReadPhase3Header(r);
                Rec me = Get(local);
                if (me != null && gm != null) gm.NetApplyAuthoritativeLives(me.Hp, me.MaxHp);
                ApplyLocalStateFromTable(gm, pc);
                break;
            }
            case SyncHitResult:
            {
                r.ReadValueSafe(out byte pn); r.ReadValueSafe(out int seq); r.ReadValueSafe(out bool accepted); r.ReadValueSafe(out bool isFall);
                r.ReadValueSafe(out int hp); r.ReadValueSafe(out float slowF); r.ReadValueSafe(out float slowD);
                r.ReadValueSafe(out FixedString64Bytes src);
                if (pn != local) break;
                if (accepted)
                {
                    StatHitsConfirmed++;
                    Log($"Damage P{pn} confirmed by HOST src={src} fall={isFall} hp={hp}");
                    if (gm != null) gm.NetApplyAuthoritativeLives(hp, Get(local) != null ? Get(local).MaxHp : gm.MaxLives, fromHit: true);
                    if (pc != null && hp > 0) pc.NetConfirmHit(isFall, slowF, slowD);
                }
                else
                {
                    if (pc != null) pc.NetClaimRejected(seq);
                }
                break;
            }
            case SyncBossReward:
            {
                r.ReadValueSafe(out byte pn);
                if (pn == local && gm != null)
                {
                    Log($"Boss reward offered to P{pn} (this device)");
                    gm.NetOfferBossReward();
                }
                break;
            }
            case SyncRunOver:
                ReadRunOver(r);
                break;
            case SyncRevived:
            {
                r.ReadValueSafe(out byte downPn); r.ReadValueSafe(out byte donorPn);
                OnRevivedClient(downPn, donorPn);
                break;
            }
        }
    }

    // ===================================================================== //
    // この端末からの呼び出し口
    // ===================================================================== //

    // ---- 被弾の出どころ(JOINのPlayerController.TakeDamageが申告の種類として使う) ----
    // 攻撃ミラー/敵パペットの接触はTakeDamageを呼ぶ直前にセットする。セットされていなければ
    // 環境(穴/壁/障害物など、各端末に同じ地形がある物)として申告する。
    static bool ctxSet;
    static ClaimKind ctxKind;
    static int ctxId;
    public static void SetClaimContext(ClaimKind kind, int id) { ctxSet = true; ctxKind = kind; ctxId = id; }
    public static void ClearClaimContext() { ctxSet = false; }

    // JOIN: PlayerController.TakeDamageの既存の事前チェック(無敵/リアクション中/シールド等)を通った
    // 被弾を、ローカルでHPを減らさずにHOSTへ申告する。
    public static int RouteLocalDamage(bool isFall, float expectedInvuln, string source)
    {
        ClaimKind kind = ctxSet ? ctxKind : ClaimKind.Environment;
        int id = ctxSet ? ctxId : 0;
        string desc = kind == ClaimKind.Attack ? NetAttackSync.DescribeAttack(id) : kind == ClaimKind.EnemyContact ? $"enemyId={id}" : "environment";
        if (Instance != null) Instance.Log($"Damage claim P{NetCombat.LocalPlayerNumber} src={source} kind={kind} {desc} fall={isFall} -> HOST");
        return ClaimHit(kind, id, isFall, expectedInvuln, source);
    }

    // JOIN: 被弾を申告する(HPはHOSTが確定)。戻り値=申告したか。
    public static int ClaimHit(ClaimKind kind, int id, bool isFall, float invulnSeconds, string source, float slowFactor = 1f, float slowDuration = 0f)
    {
        if (Instance == null || !NetCombat.Replica) return -1;
        int seq = ++Instance.claimSeq;
        Instance.StatClaimsSent++;
        SendToHost(w =>
        {
            w.WriteValueSafe(ReqClaim); w.WriteValueSafe(seq); w.WriteValueSafe((byte)kind); w.WriteValueSafe(id); w.WriteValueSafe(isFall);
            w.WriteValueSafe(invulnSeconds); w.WriteValueSafe(slowFactor); w.WriteValueSafe(slowDuration);
            w.WriteValueSafe(new FixedString64Bytes(source ?? ""));
        });
        return seq;
    }

    public static void RequestHeal(int amount)
    {
        if (!ClientRoutesHp || amount <= 0) return;
        if (GameManager.Instance != null) GameManager.Instance.NetNoteLocalHpRequest();
        SendToHost(w => { w.WriteValueSafe(ReqHeal); w.WriteValueSafe(amount); });
    }

    public static void RequestSetMax(int max, bool restoreFull)
    {
        if (!ClientRoutesHp) return;
        if (GameManager.Instance != null) GameManager.Instance.NetNoteLocalHpRequest();
        SendToHost(w => { w.WriteValueSafe(ReqSetMax); w.WriteValueSafe(max); w.WriteValueSafe(restoreFull); });
    }

    // 自動テスト用(開発ビルドのみ): 自分をその場でHP0扱いにする(カード選択中でも)。
    // 「選択中に脱落/DOWN」「最後の1人が選択中に倒れる」の競合をテストで再現するための入口。
    public static void RequestDebugForceOut()
    {
        if (Instance == null || !Active || !Debug.isDebugBuild) return;
        if (NetCombat.Authority) Instance.HostDebugForceOut(Get(NetCombat.LocalPlayerNumber));
        else SendToHost(w => w.WriteValueSafe(ReqDebugForceOut));
    }

    void HostDebugForceOut(Rec rec)
    {
        if (rec == null || rec.State != PState.Alive) return;
        Log($"P{rec.Pn} debug FORCE OUT (choosing={rec.Choosing})");
        rec.Hp = 0;
        if (rec.Pn == NetCombat.LocalPlayerNumber && GameManager.Instance != null) GameManager.Instance.NetSetLocalLives(0);
        OnPlayerHpZero(rec, "DebugForceOut");
        dirty = true;
    }

    public static void RequestDebugSetHp(int hp)
    {
        if (!ClientRoutesHp) return;
        if (GameManager.Instance != null) GameManager.Instance.NetNoteLocalHpRequest();
        SendToHost(w => { w.WriteValueSafe(ReqDebugSetHp); w.WriteValueSafe(hp); });
    }

    // HOST: HOST自身がHPを変えた(被弾/回復)ことを記録へ反映する(Updateでも同期されるが即時に配る)。
    public static void HostLocalHpChanged(string reason)
    {
        if (Instance == null || !NetCombat.Authority) return;
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        Rec me = Instance.GetOrCreate(NetCombat.LocalPlayerNumber, NetworkManager.ServerClientId);
        if (me.State != PState.Alive) return;
        int before = me.Hp;
        me.Known = true; me.Hp = gm.Lives; me.MaxHp = gm.MaxLives;
        if (before != me.Hp)
        {
            me.HitsTaken += before > me.Hp ? 1 : 0;
            Instance.Log($"Damage P{me.Pn} src={reason} hp {before} -> {me.Hp} (authority=HOST, local){(me.Choosing ? " [while choosing card]" : "")}");
        }
        Instance.dirty = true;
    }

    // HOST: HOST自身のHPが0になった。trueなら(Phase 3のダウン/脱落として)既存のRun終了を行わない。
    public static bool HostLocalHpZero(string reason)
    {
        if (Instance == null || !NetCombat.Authority) return false;
        Rec me = Instance.GetOrCreate(NetCombat.LocalPlayerNumber, NetworkManager.ServerClientId);
        if (me.State != PState.Alive) return me.State == PState.Down || me.State == PState.Eliminated;
        me.Hp = 0;
        Instance.OnPlayerHpZero(me, reason);
        Instance.dirty = true;
        Instance.SendTable();
        return me.State == PState.Down || me.State == PState.Eliminated;
    }

    // HOST: ボス報酬のカード選択を受け取る人(ラストヒットの本人)へ知らせる。
    public static void SendBossRewardOffer(int pn)
    {
        if (Instance == null || !NetCombat.Authority) return;
        Rec rec = Get(pn);
        Instance.Log($"Boss reward recipient = P{pn}");
        if (rec == null) return;
        SendToClients(w => { w.WriteValueSafe(SyncBossReward); w.WriteValueSafe((byte)pn); }, rec.ClientId);
    }

    // HOST: 撃破の記録(Kill数/ボスのラストヒット数)。
    public static void HostRecordKill(int pn, bool boss)
    {
        if (Instance == null || !NetCombat.Authority || pn <= 0) return;
        Rec rec = Instance.GetOrCreate(pn, pn == NetCombat.LocalPlayerNumber ? NetworkManager.ServerClientId : 0);
        if (boss) rec.BossLastHits++; else rec.Kills++;
        Instance.dirty = true;
    }

    // ===================================================================== //
    // HP 0 の扱い(Phase 2.5の既定: その人のRunだけが終わる。Phase 3のルールで上書き)
    // ===================================================================== //

    void OnPlayerHpZero(Rec rec, string source)
    {
        if (HandleHpZeroPhase3(rec, source)) return;
        rec.State = PState.Out;
        rec.FinalDistance = rec.Distance;
        Log($"P{rec.Pn} HP 0 -> Out (src={source}) distance={rec.Distance:F1}");
    }

    void ApplyLocalStateFromTable(GameManager gm, PlayerController pc)
    {
        if (ApplyLocalStatePhase3(gm, pc)) return;
        Rec me = Get(NetCombat.LocalPlayerNumber);
        if (me != null && me.State == PState.Out && gm != null && !gm.IsGameOver) gm.NetForceGameOver("HP0(HOST)");
    }

    public string DebugDescribe()
    {
        var sb = new StringBuilder();
        foreach (var r in recs.Values)
            sb.Append($"P{r.Pn}:hp={r.Hp}/{r.MaxHp},{r.State}{(r.Choosing ? ",choosing" : "")},d={r.Distance:F0} ");
        return sb.ToString();
    }
}

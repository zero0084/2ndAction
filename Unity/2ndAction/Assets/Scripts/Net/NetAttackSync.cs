using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチプレイPhase 2.5(2026-09-27) - 敵/ボスの「攻撃」を参加側(JOIN)にも存在させる層。
//
// Phase 2までは敵/ボスのAIはHOSTだけが動かしていたため、弾・近接判定・範囲攻撃・予兆表示は
// HOSTにしか生まれず、JOINのプレイヤーには(体当たり以外)一切当たらなかった。
//
// 方式: HOSTで攻撃オブジェクトが生まれた瞬間に登録(Register)し、
//  - 出現(Reliable): AttackId / 種類 / 持ち主の敵ID(OwnerEnemyId) / 狙った相手(TargetPlayerId) /
//    出現位置 / 方向・速度 / 開始時刻 / 当たり判定の形 / 見た目(SpriteRendererの構成)
//  - 状態(Unreliable 20Hz): 位置・回転・大きさ・判定の有効/無効・発動回数(Activation)・見た目
//  - 消滅(Reliable)
// をJOINへ送る。JOINは「見た目+当たり判定だけのミラー」を作り、自分のプレイヤーに触れたら
// HPを減らさずにHOSTへ被弾申告(NetMatch.ClaimHit)する。HOSTが攻撃の実在・有効時間・無敵・
// 同じ攻撃での重複を検証してHPを確定する(=最終結果は常にHOSTの1本)。VFX/SEは各端末ローカル。
//
// 持ち主(ボス/敵)の子として付いている判定(BossHitbox/近接判定/予兆マーカー)は、持ち主の
// パペットからの相対位置で置く(ボスの補間表示とぴったり重なる)。独立して飛ぶ弾は論理座標で置き、
// 直近の速度で短く外挿する(NetCombatの敵と同じ考え方)。
//
// シングルプレイ/マルチでないRunでは Register は何もしない(既存の挙動は一切変わらない)。
[DefaultExecutionOrder(1160)] // NetCombat(1150)がパペットを置いた後に、持ち主基準の判定を置く
public class NetAttackSync : MonoBehaviour
{
    public static NetAttackSync Instance { get; private set; }

    const string MsgReliable = "OMM.Attack";      // HOST→JOIN: 出現/消滅
    const string MsgState = "OMM.AttackState";    // HOST→JOIN: 状態(非信頼)
    const string MsgRequest = "OMM.AttackReq";    // JOIN→HOST: 弾がプレイヤーに当たって消えた

    const byte OpSpawn = 1, OpDespawn = 2;
    const byte ReqConsume = 1;
    const int MaxVisuals = 6;
    const float StateSendRate = 20f;
    const float ActiveGrace = 0.6f;     // 判定が消えた直後の申告も有効とみなす猶予(通信遅延ぶん)
    const float RemovedGrace = 0.6f;    // 消滅直後の申告も有効とみなす猶予

    public enum AType : byte
    {
        Other = 0, Fireball = 1, BossProjectile = 2, BossHitbox = 3, EnemyMelee = 4, TrackedHazard = 5,
        SkyStrike = 6, FallRock = 7, RisingCrystal = 8, Telegraph = 9, WarnBand = 10,
    }

    public static bool IsDamaging(AType t) =>
        t == AType.Fireball || t == AType.BossProjectile || t == AType.BossHitbox || t == AType.EnemyMelee ||
        t == AType.TrackedHazard || t == AType.SkyStrike;

    public const byte FActive = 1, FCollider = 2;

    public class Tracked
    {
        public int Id;
        public AType Type;
        public int OwnerId;
        public bool Attached;
        public int TargetPn;
        public string Source = "";
        public bool DestroyOnHit;
        public bool NoDamage;
        public float SlowFactor = 1f, SlowDuration;
        public Vector2 Velocity;
        public double SpawnX; public float SpawnY;
        public double StartTime;
        public int Layer;
        public byte ColKind; // 0=なし 1=Box 2=Circle
        public Vector2 ColSize, ColOffset;
        public int NVis;
        public int[] VisSortingLayer = new int[MaxVisuals];
        public string[] VisName = new string[MaxVisuals];
        // HOST
        public GameObject Go;
        public Transform Root, OwnerT;
        public Collider2D Col;
        public SpriteRenderer[] Srs;
        public bool SpawnSent;
        public byte Activation;
        public bool WasColOn;
        public float LastActiveTime = -99f;
        public int SentHash;
        public float SentTime = -99f;
        public float RemovedTime = -1f;
        public bool ConsumedOnHost;
        // JOIN
        public GameObject Puppet;
        public SpriteRenderer[] PSrs;
        public Collider2D PCol;
        public readonly List<AState> Buffer = new List<AState>(8);
        public bool LocallyConsumed;
        public byte Flags;
        public byte CurActivation;
        public readonly HashSet<int> ClaimedActivations = new HashSet<int>();
    }

    public class AState
    {
        public double Time;
        public double X; public float Y;
        public float RotZ, SX, SY;
        public byte Flags, Activation;
        public readonly VisState[] Vis = new VisState[MaxVisuals];
    }

    public struct VisState
    {
        public bool Enabled;
        public int SpriteKey;
        public uint Color;
        public float PX, PY, Rot, SX, SY;
        public short Order;
    }

    readonly Dictionary<int, Tracked> tracked = new Dictionary<int, Tracked>();
    readonly Dictionary<GameObject, int> byGoInstance = new Dictionary<GameObject, int>();
    readonly Dictionary<int, float> recentlyRemoved = new Dictionary<int, float>();
    public IReadOnlyDictionary<int, Tracked> Tracks => tracked;
    int nextId = 1;
    float stateTimer;
    Transform container;
    bool handlersRegistered;
    NetworkManager registeredManager;

    // 統計(自動テスト/デバッグ用)
    public int StatRegistered, StatSpawnsSent, StatSpawnsReceived, StatTouches, StatConsumed;
    public readonly Dictionary<AType, int> RegisteredByType = new Dictionary<AType, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[NetAttackSync]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetAttackSync>();
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
        tracked.Clear();
        byGoInstance.Clear();
        recentlyRemoved.Clear();
        container = null;
        StatRegistered = StatSpawnsSent = StatSpawnsReceived = StatTouches = StatConsumed = 0;
        RegisteredByType.Clear();
        spriteCache.Clear();
        spriteKeyCache.Clear();
        if (NetRunLauncher.IsMultiplayerRun) WarmProceduralSprites();
    }

    static void Log(string msg) => Debug.Log($"[NET][ATTACK] {msg}");

    // ===================================================================== //
    // 手続き的に作るSprite(名前が空)に、両端末で同じ名前を付けておく(見た目の対応付け用)。
    // ===================================================================== //

    static void WarmProceduralSprites()
    {
        void N(Sprite s, string name) { if (s != null && string.IsNullOrEmpty(s.name)) s.name = name; }
        try
        {
            N(BossFx.Fang(), "BossFx.Fang"); N(BossFx.Slash(), "BossFx.Slash"); N(BossFx.Ring(), "BossFx.Ring");
            N(BossFx.Block(), "BossFx.Block"); N(BossFx.Orb(), "BossFx.Orb");
            N(CaveBossFx.RockChunk(), "CaveBossFx.RockChunk"); N(CaveBossFx.CrystalShard(), "CaveBossFx.CrystalShard");
            N(SkyBossFx.Bolt(), "SkyBossFx.Bolt"); N(SkyBossFx.GroundGlow(), "SkyBossFx.GroundGlow"); N(SkyBossFx.Fist(), "SkyBossFx.Fist");
            N(SkyBossFx.Beam(), "SkyBossFx.Beam"); N(SkyBossFx.Spear(), "SkyBossFx.Spear"); N(SkyBossFx.Feather(), "SkyBossFx.Feather");
            N(SkyBossFx.Fin(), "SkyBossFx.Fin"); N(SkyBossFx.Tentacle(), "SkyBossFx.Tentacle");
            N(OneShotSpriteEffect.SoftDotSprite(), "OneShot.SoftDot");
        }
        catch (System.Exception e) { Debug.LogWarning("[NET][ATTACK] sprite warm-up failed: " + e.Message); }
    }

    readonly Dictionary<Sprite, int> spriteKeyCache = new Dictionary<Sprite, int>();
    readonly Dictionary<int, Sprite> spriteCache = new Dictionary<int, Sprite>();
    float lastSpriteScan = -99f;

    static int StableHash(string s)
    {
        unchecked
        {
            int h = (int)2166136261;
            foreach (char c in s) h = (h ^ c) * 16777619;
            return h == 0 ? 1 : h;
        }
    }

    static string SpriteId(Sprite s) =>
        s.name + "|" + (s.texture != null ? s.texture.name : "") + "|" + (int)s.rect.width + "x" + (int)s.rect.height + "|" + Mathf.RoundToInt(s.pixelsPerUnit);

    int KeyOf(Sprite s)
    {
        if (s == null) return 0;
        if (spriteKeyCache.TryGetValue(s, out int k)) return k;
        k = StableHash(SpriteId(s));
        spriteKeyCache[s] = k;
        return k;
    }

    Sprite SpriteOf(int key)
    {
        if (key == 0) return null;
        if (spriteCache.TryGetValue(key, out Sprite s) && s != null) return s;
        // 見つからなければ、読み込み済みの全Spriteから探し直す(頻度を抑える)。
        if (Time.realtimeSinceStartup - lastSpriteScan > 1f)
        {
            lastSpriteScan = Time.realtimeSinceStartup;
            foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sp == null) continue;
                int k = StableHash(SpriteId(sp));
                if (!spriteCache.ContainsKey(k) || spriteCache[k] == null) spriteCache[k] = sp;
            }
            if (spriteCache.TryGetValue(key, out s) && s != null) return s;
        }
        spriteCache[key] = null;
        return null;
    }

    // ===================================================================== //
    // HOST: 登録
    // ===================================================================== //

    // 攻撃オブジェクトが生まれた時に呼ぶ(マルチRunのHOSTでなければ何もしない)。
    public static void Register(GameObject go, AType type, Vector2 velocity = default, bool destroyOnHit = false,
        float slowFactor = 1f, float slowDuration = 0f, bool noDamage = false)
    {
        if (!NetCombat.Authority || Instance == null || go == null) return;
        Instance.RegisterImpl(go, type, velocity, destroyOnHit, slowFactor, slowDuration, noDamage);
    }

    void RegisterImpl(GameObject go, AType type, Vector2 velocity, bool destroyOnHit, float slowFactor, float slowDuration, bool noDamage)
    {
        GameObject inst = go;
        if (byGoInstance.ContainsKey(inst)) return;
        var t = new Tracked
        {
            Id = nextId++, Type = type, Go = go, Root = go.transform, Velocity = velocity, DestroyOnHit = destroyOnHit,
            SlowFactor = slowFactor, SlowDuration = slowDuration, NoDamage = noDamage, Layer = go.layer,
            StartTime = NetSession.Manager != null ? NetSession.Manager.ServerTime.Time : 0.0,
        };
        // 持ち主: 親をたどって共有エンティティ(敵/ボス)を探す。独立した弾は最寄りの共有エンティティ(記録用)。
        for (Transform p = go.transform.parent; p != null; p = p.parent)
        {
            var e = NetCombat.FindByGo(p.gameObject);
            if (e != null) { t.OwnerId = e.Id; t.Attached = true; t.OwnerT = p; break; }
        }
        if (!t.Attached && NetCombat.Instance != null)
        {
            float best = 25f;
            foreach (var e in NetCombat.Instance.Entities.Values)
            {
                if (e.Go == null) continue;
                float d = Vector2.Distance(e.Go.transform.position, go.transform.position);
                if (d < best) { best = d; t.OwnerId = e.Id; }
            }
        }
        // 狙った相手: 持ち主のターゲット選択 > 出現位置に最も近い活動中プレイヤー
        var ownerEnt = t.OwnerId != 0 ? NetCombat.Find(t.OwnerId) : null;
        var sel = ownerEnt != null && ownerEnt.Go != null ? ownerEnt.Go.GetComponent<EnemyTargetSelector>() : null;
        if (sel != null && sel.TargetPlayer > 0) t.TargetPn = sel.TargetPlayer;
        else if (NetTargets.Nearest(go.transform.position, out NetTargets.Candidate c)) t.TargetPn = c.Player;

        t.Col = go.GetComponent<Collider2D>();
        if (t.Col is BoxCollider2D box) { t.ColKind = 1; t.ColSize = box.size; t.ColOffset = box.offset; }
        else if (t.Col is CircleCollider2D circle) { t.ColKind = 2; t.ColSize = new Vector2(circle.radius, circle.radius); t.ColOffset = circle.offset; }
        else if (t.Col != null) { t.ColKind = 1; Bounds b = t.Col.bounds; Vector3 ls = go.transform.lossyScale; t.ColSize = new Vector2(b.size.x / Mathf.Max(0.001f, Mathf.Abs(ls.x)), b.size.y / Mathf.Max(0.001f, Mathf.Abs(ls.y))); }
        var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
        int n = Mathf.Min(MaxVisuals, srs.Length);
        t.Srs = new SpriteRenderer[n];
        for (int i = 0; i < n; i++) { t.Srs[i] = srs[i]; t.VisSortingLayer[i] = srs[i].sortingLayerID; t.VisName[i] = srs[i].gameObject.name; }
        t.NVis = n;
        Vector3 pos = go.transform.position;
        t.SpawnX = pos.x + FloatingOrigin.Offset; t.SpawnY = pos.y;
        t.Source = go.name;
        tracked[t.Id] = t;
        byGoInstance[inst] = t.Id;
        StatRegistered++;
        RegisteredByType[type] = RegisteredByType.TryGetValue(type, out int cnt) ? cnt + 1 : 1;
    }

    // HOST: JOINの被弾申告の検証用。claimId = AttackId*64 + 発動回数(0〜63)。
    public static bool IsValidAttack(int claimId)
    {
        if (Instance == null) return false;
        int id = claimId / 64;
        float now = Time.realtimeSinceStartup;
        if (Instance.tracked.TryGetValue(id, out Tracked t))
        {
            if (t.NoDamage || !IsDamaging(t.Type)) return false;
            if (t.Go == null) return now - t.RemovedTime < RemovedGrace;
            bool on = t.Go.activeInHierarchy && (t.Col == null || t.Col.enabled);
            return on || now - t.LastActiveTime < ActiveGrace;
        }
        return Instance.recentlyRemoved.TryGetValue(id, out float when) && now - when < RemovedGrace;
    }

    public static string DescribeAttack(int claimId)
    {
        if (Instance == null) return "?";
        int id = claimId / 64;
        if (Instance.tracked.TryGetValue(id, out Tracked t))
            return $"attackId={id} type={t.Type} owner={t.OwnerId} target=P{t.TargetPn} act={claimId % 64}";
        return $"attackId={id} (removed)";
    }

    // HOST: 当たったら消える弾(火球/ボス弾)を、HOST側でも消す(=両端末で1発は1回だけ)。
    public static void ConsumeOnHit(int claimId)
    {
        if (Instance == null) return;
        Instance.ConsumeById(claimId / 64, "hit");
    }

    void ConsumeById(int id, string why)
    {
        if (!tracked.TryGetValue(id, out Tracked t) || !t.DestroyOnHit || t.Go == null || t.ConsumedOnHost) return;
        t.ConsumedOnHost = true;
        StatConsumed++;
        Log($"consume attackId={id} type={t.Type} ({why})");
        Destroy(t.Go);
    }

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //

    void Update()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!handlersRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgReliable, OnReliable);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, OnState);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgRequest, OnRequest);
            handlersRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlersRegistered)
        {
            handlersRegistered = false;
            registeredManager = null;
        }
        if (!NetCombat.Authority && !NetCombat.Replica && tracked.Count > 0)
        {
            // 接続が切れた: JOINのミラーは片付ける(HOSTの本物はそのまま)。
            foreach (var t in tracked.Values) if (t.Puppet != null) Destroy(t.Puppet);
            tracked.Clear(); byGoInstance.Clear();
        }
    }

    void LateUpdate()
    {
        if (NetCombat.Authority) AuthorityLateUpdate();
        else if (NetCombat.Replica) ReplicaLateUpdate();
    }

    // ---------------- HOST ----------------

    readonly List<int> goneBuf = new List<int>();

    void AuthorityLateUpdate()
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || nm.CustomMessagingManager == null) return;
        float now = Time.realtimeSinceStartup;
        goneBuf.Clear();
        foreach (var t in tracked.Values)
        {
            if (t.Go == null)
            {
                goneBuf.Add(t.Id);
                continue;
            }
            bool colOn = t.Go.activeInHierarchy && t.Col != null && t.Col.enabled;
            if (colOn && !t.WasColOn) t.Activation++;
            if (colOn) t.LastActiveTime = now;
            t.WasColOn = colOn;
            if (!t.SpawnSent) { SendSpawn(t); t.SpawnSent = true; }
        }
        foreach (int id in goneBuf)
        {
            Tracked t = tracked[id];
            if (t.SpawnSent) SendDespawn(t);
            tracked.Remove(id);
            recentlyRemoved[id] = now;
        }
        if (recentlyRemoved.Count > 256)
        {
            var old = new List<int>();
            foreach (var kv in recentlyRemoved) if (now - kv.Value > 5f) old.Add(kv.Key);
            foreach (int k in old) recentlyRemoved.Remove(k);
        }
        // byGoInstanceの掃除(消えたGOのinstanceIdは再利用され得るため)
        if (goneBuf.Count > 0)
        {
            var dead = new List<GameObject>();
            foreach (var kv in byGoInstance) if (!tracked.ContainsKey(kv.Value)) dead.Add(kv.Key);
            foreach (GameObject k in dead) byGoInstance.Remove(k);
        }

        stateTimer += Time.unscaledDeltaTime;
        float interval = 1f / StateSendRate;
        if (stateTimer < interval) return;
        stateTimer = Mathf.Min(stateTimer - interval, interval);
        SendStates(nm);
    }

    void SendSpawn(Tracked t)
    {
        using (var w = new FastBufferWriter(512, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(OpSpawn);
            w.WriteValueSafe(t.Id); w.WriteValueSafe((byte)t.Type); w.WriteValueSafe(t.OwnerId); w.WriteValueSafe(t.Attached);
            w.WriteValueSafe(t.TargetPn);
            w.WriteValueSafe(t.SpawnX); w.WriteValueSafe(t.SpawnY); w.WriteValueSafe(t.Velocity.x); w.WriteValueSafe(t.Velocity.y);
            w.WriteValueSafe(t.StartTime);
            w.WriteValueSafe(t.DestroyOnHit); w.WriteValueSafe(t.NoDamage); w.WriteValueSafe(t.SlowFactor); w.WriteValueSafe(t.SlowDuration);
            w.WriteValueSafe(t.Layer); w.WriteValueSafe(t.ColKind);
            w.WriteValueSafe(t.ColSize.x); w.WriteValueSafe(t.ColSize.y); w.WriteValueSafe(t.ColOffset.x); w.WriteValueSafe(t.ColOffset.y);
            w.WriteValueSafe(new FixedString32Bytes(t.Source.Length > 28 ? t.Source.Substring(0, 28) : t.Source));
            w.WriteValueSafe((byte)t.NVis);
            for (int i = 0; i < t.NVis; i++) w.WriteValueSafe(t.VisSortingLayer[i]);
            SendToClients(MsgReliable, w, NetworkDelivery.ReliableSequenced);
        }
        StatSpawnsSent++;
        Log($"Spawn attackId={t.Id} type={t.Type} owner={t.OwnerId} attached={t.Attached} target=P{t.TargetPn} x={t.SpawnX:F1} y={t.SpawnY:F2} v=({t.Velocity.x:F1},{t.Velocity.y:F1}) t0={t.StartTime:F2} src={t.Source}");
    }

    void SendDespawn(Tracked t)
    {
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(OpDespawn);
            w.WriteValueSafe(t.Id);
            SendToClients(MsgReliable, w, NetworkDelivery.ReliableSequenced);
        }
    }

    static void SendToClients(string msg, FastBufferWriter w, NetworkDelivery delivery)
    {
        NetworkManager nm = NetSession.Manager;
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId) continue;
            nm.CustomMessagingManager.SendNamedMessage(msg, clientId, w, delivery);
        }
    }

    readonly List<AState> sendList = new List<AState>();
    readonly List<int> sendIds = new List<int>();

    void SendStates(NetworkManager nm)
    {
        double now = nm.ServerTime.Time;
        float unow = Time.unscaledTime;
        sendList.Clear(); sendIds.Clear();
        foreach (var t in tracked.Values)
        {
            if (!t.SpawnSent || t.Go == null) continue;
            if (t.Attached && t.OwnerT == null) continue;
            AState s = Capture(t);
            s.Time = now;
            int h = Hash(s);
            bool visible = (s.Flags & FActive) != 0;
            float since = unow - t.SentTime;
            if (h == t.SentHash && since < (visible ? 0.5f : 2f)) continue;
            t.SentHash = h; t.SentTime = unow;
            sendList.Add(s); sendIds.Add(t.Id);
        }
        const int perPacket = 8;
        for (int start = 0; start < sendList.Count; start += perPacket)
        {
            int n = Mathf.Min(perPacket, sendList.Count - start);
            using (var w = new FastBufferWriter(1300, Allocator.Temp))
            {
                w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
                w.WriteValueSafe(now);
                w.WriteValueSafe((byte)n);
                for (int i = 0; i < n; i++)
                {
                    int id = sendIds[start + i];
                    WriteState(w, id, sendList[start + i], tracked[id].NVis);
                }
                SendToClients(MsgState, w, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    AState Capture(Tracked t)
    {
        var s = new AState();
        Transform r = t.Root;
        Vector3 p = r.position;
        if (t.Attached) { s.X = p.x - t.OwnerT.position.x; s.Y = p.y - t.OwnerT.position.y; }
        else { s.X = p.x + FloatingOrigin.Offset; s.Y = p.y; }
        s.RotZ = r.eulerAngles.z;
        Vector3 ls = r.lossyScale;
        s.SX = ls.x; s.SY = ls.y;
        bool active = t.Go.activeInHierarchy;
        if (active) s.Flags |= FActive;
        if (active && t.Col != null && t.Col.enabled) s.Flags |= FCollider;
        s.Activation = t.Activation;
        for (int i = 0; i < t.NVis; i++)
        {
            SpriteRenderer sr = t.Srs[i];
            var v = new VisState();
            if (sr != null)
            {
                Transform st = sr.transform;
                v.Enabled = sr.enabled && sr.gameObject.activeInHierarchy;
                v.SpriteKey = KeyOf(sr.sprite);
                v.Color = NetPlayerSnapshot.PackColor(sr.color);
                Vector3 lp = st == r ? Vector3.zero : r.InverseTransformPoint(st.position);
                v.PX = lp.x; v.PY = lp.y;
                v.Rot = st == r ? 0f : Mathf.DeltaAngle(r.eulerAngles.z, st.eulerAngles.z);
                Vector3 sl = st.lossyScale;
                v.SX = st == r ? 1f : sl.x / (Mathf.Abs(ls.x) > 1e-4f ? ls.x : 1f);
                v.SY = st == r ? 1f : sl.y / (Mathf.Abs(ls.y) > 1e-4f ? ls.y : 1f);
                v.Order = (short)Mathf.Clamp(sr.sortingOrder, short.MinValue, short.MaxValue);
            }
            s.Vis[i] = v;
        }
        return s;
    }

    static int Hash(AState s)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt((float)(s.X * 50.0));
            h = h * 31 + Mathf.RoundToInt(s.Y * 50f);
            h = h * 31 + Mathf.RoundToInt(s.RotZ * 2f) + Mathf.RoundToInt(s.SX * 100f) * 3 + Mathf.RoundToInt(s.SY * 100f) * 7;
            h = h * 31 + s.Flags * 131 + s.Activation;
            foreach (var v in s.Vis)
            {
                h = h * 31 + (v.Enabled ? 1 : 0) + v.SpriteKey + (int)v.Color;
                h = h * 31 + Mathf.RoundToInt(v.PX * 50f) + Mathf.RoundToInt(v.PY * 50f) * 3 + Mathf.RoundToInt(v.SX * 50f) * 7 + Mathf.RoundToInt(v.SY * 50f) * 11 + Mathf.RoundToInt(v.Rot) + v.Order;
            }
            return h;
        }
    }

    static void WriteState(FastBufferWriter w, int id, AState s, int nVis)
    {
        w.WriteValueSafe(id);
        w.WriteValueSafe(s.X); w.WriteValueSafe(s.Y); w.WriteValueSafe(s.RotZ); w.WriteValueSafe(s.SX); w.WriteValueSafe(s.SY);
        w.WriteValueSafe(s.Flags); w.WriteValueSafe(s.Activation);
        w.WriteValueSafe((byte)nVis);
        for (int i = 0; i < nVis; i++)
        {
            var v = s.Vis[i];
            w.WriteValueSafe(v.Enabled); w.WriteValueSafe(v.SpriteKey); w.WriteValueSafe(v.Color);
            w.WriteValueSafe(v.PX); w.WriteValueSafe(v.PY); w.WriteValueSafe(v.Rot); w.WriteValueSafe(v.SX); w.WriteValueSafe(v.SY);
            w.WriteValueSafe(v.Order);
        }
    }

    static AState ReadState(FastBufferReader r, out int id)
    {
        var s = new AState();
        r.ReadValueSafe(out id);
        r.ReadValueSafe(out s.X); r.ReadValueSafe(out s.Y); r.ReadValueSafe(out s.RotZ); r.ReadValueSafe(out s.SX); r.ReadValueSafe(out s.SY);
        r.ReadValueSafe(out s.Flags); r.ReadValueSafe(out s.Activation);
        r.ReadValueSafe(out byte n);
        for (int i = 0; i < n; i++)
        {
            var v = new VisState();
            r.ReadValueSafe(out v.Enabled); r.ReadValueSafe(out v.SpriteKey); r.ReadValueSafe(out v.Color);
            r.ReadValueSafe(out v.PX); r.ReadValueSafe(out v.PY); r.ReadValueSafe(out v.Rot); r.ReadValueSafe(out v.SX); r.ReadValueSafe(out v.SY);
            r.ReadValueSafe(out v.Order);
            if (i < MaxVisuals) s.Vis[i] = v;
        }
        return s;
    }

    // JOINから: 弾がプレイヤーに当たって消えた(HPの確定とは別。単発の弾をHOST側でも消すだけ)。
    void OnRequest(ulong sender, FastBufferReader r)
    {
        if (!NetCombat.Authority) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out byte op);
        r.ReadValueSafe(out int id);
        if (seed != NetRunLauncher.ActiveRunSeed) return;
        if (op == ReqConsume) { Log($"consume request attackId={id} from JOIN (alive on host={(tracked.TryGetValue(id, out Tracked t) && t.Go != null)})"); ConsumeById(id, "touched JOIN player"); }
    }

    // ---------------- JOIN ----------------

    readonly List<(int seed, byte[] data)> pending = new List<(int, byte[])>();

    void OnReliable(ulong sender, FastBufferReader r)
    {
        if (NetSession.Manager == null || NetSession.Manager.IsServer) return;
        int start = r.Position;
        int len = r.Length - start;
        var data = new byte[len];
        r.ReadBytesSafe(ref data, len);
        int seed = System.BitConverter.ToInt32(data, 0);
        pending.Add((seed, data));
        ProcessPending();
    }

    bool SceneReady(int seed) => NetRunLauncher.IsMultiplayerRun && NetRunLauncher.ActiveRunSeed == seed && GameManager.Instance != null && TerrainManager.Instance != null;

    void ProcessPending()
    {
        int i = 0;
        while (i < pending.Count)
        {
            var (seed, data) = pending[i];
            if (SceneReady(seed))
            {
                pending.RemoveAt(i);
                using (var reader = new FastBufferReader(data, Allocator.Temp))
                {
                    try { HandleReliable(reader); }
                    catch (System.Exception ex) { Debug.LogWarning("[NET][ATTACK] reliable message failed: " + ex.Message); }
                }
                continue;
            }
            if (seed == NetRunLauncher.PendingRunSeed) { i++; continue; }
            pending.RemoveAt(i);
        }
        if (pending.Count > 1000) pending.RemoveRange(0, pending.Count - 1000);
    }

    void HandleReliable(FastBufferReader r)
    {
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out byte op);
        if (op == OpSpawn) HandleSpawn(r);
        else if (op == OpDespawn)
        {
            r.ReadValueSafe(out int id);
            if (tracked.TryGetValue(id, out Tracked t))
            {
                if (t.Puppet != null) Destroy(t.Puppet);
                tracked.Remove(id);
            }
        }
    }

    Transform Container()
    {
        if (container != null) return container;
        var go = new GameObject("[NetSharedAttacks]");
        go.AddComponent<FloatingOriginExempt>();
        container = go.transform;
        return container;
    }

    void HandleSpawn(FastBufferReader r)
    {
        var t = new Tracked();
        r.ReadValueSafe(out t.Id); r.ReadValueSafe(out byte type); t.Type = (AType)type; r.ReadValueSafe(out t.OwnerId); r.ReadValueSafe(out t.Attached);
        r.ReadValueSafe(out t.TargetPn);
        r.ReadValueSafe(out t.SpawnX); r.ReadValueSafe(out t.SpawnY); r.ReadValueSafe(out float vx); r.ReadValueSafe(out float vy); t.Velocity = new Vector2(vx, vy);
        r.ReadValueSafe(out t.StartTime);
        r.ReadValueSafe(out t.DestroyOnHit); r.ReadValueSafe(out t.NoDamage); r.ReadValueSafe(out t.SlowFactor); r.ReadValueSafe(out t.SlowDuration);
        r.ReadValueSafe(out t.Layer); r.ReadValueSafe(out t.ColKind);
        r.ReadValueSafe(out float sx); r.ReadValueSafe(out float sy); r.ReadValueSafe(out float ox); r.ReadValueSafe(out float oy);
        t.ColSize = new Vector2(sx, sy); t.ColOffset = new Vector2(ox, oy);
        r.ReadValueSafe(out FixedString32Bytes src); t.Source = src.ToString();
        r.ReadValueSafe(out byte nv); t.NVis = Mathf.Min(nv, MaxVisuals);
        for (int i = 0; i < nv; i++) { r.ReadValueSafe(out int layerId); if (i < MaxVisuals) t.VisSortingLayer[i] = layerId; }
        if (tracked.ContainsKey(t.Id)) return;

        var go = new GameObject($"NetAttack_{t.Type}_{t.Id}");
        go.transform.SetParent(Container(), false);
        go.layer = t.Layer;
        t.Puppet = go;
        t.PSrs = new SpriteRenderer[t.NVis];
        for (int i = 0; i < t.NVis; i++)
        {
            var v = new GameObject("Vis" + i);
            v.transform.SetParent(go.transform, false);
            var sr = v.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = t.VisSortingLayer[i];
            sr.enabled = false;
            t.PSrs[i] = sr;
        }
        if (IsDamaging(t.Type) && (!t.NoDamage || t.SlowFactor < 1f) && t.ColKind != 0)
        {
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            if (t.ColKind == 2)
            {
                var c = go.AddComponent<CircleCollider2D>();
                c.radius = t.ColSize.x; c.offset = t.ColOffset; c.isTrigger = true;
                t.PCol = c;
            }
            else
            {
                var b = go.AddComponent<BoxCollider2D>();
                b.size = t.ColSize; b.offset = t.ColOffset; b.isTrigger = true;
                t.PCol = b;
            }
            t.PCol.enabled = false;
            var p = go.AddComponent<NetAttackPuppet>();
            p.Id = t.Id;
        }
        go.SetActive(false); // 最初の状態が届くまで出さない
        tracked[t.Id] = t;
        StatSpawnsReceived++;
        Log($"Spawn attackId={t.Id} type={t.Type} owner={t.OwnerId} attached={t.Attached} target=P{t.TargetPn} x={t.SpawnX:F1} y={t.SpawnY:F2} v=({vx:F1},{vy:F2}) t0={t.StartTime:F2} (mirror)");
    }

    void OnState(ulong sender, FastBufferReader r)
    {
        if (!NetCombat.Replica) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out double time);
        r.ReadValueSafe(out byte n);
        bool same = seed == NetRunLauncher.ActiveRunSeed;
        for (int i = 0; i < n; i++)
        {
            AState s = ReadState(r, out int id);
            s.Time = time;
            if (!same || !tracked.TryGetValue(id, out Tracked t)) continue;
            var buf = t.Buffer;
            if (buf.Count > 0 && buf[buf.Count - 1].Time >= time) continue;
            buf.Add(s);
            if (buf.Count > 6) buf.RemoveAt(0);
        }
    }

    void ReplicaLateUpdate()
    {
        if (pending.Count > 0) ProcessPending();
        NetworkManager nm = NetSession.Manager;
        double renderTime = nm.ServerTime.Time - NetCombat.InterpDelay;
        foreach (var t in tracked.Values)
        {
            if (t.Puppet == null || t.Buffer.Count == 0) continue;
            AState s = t.Buffer[t.Buffer.Count - 1];
            Vector3 pos;
            if (t.Attached)
            {
                var owner = NetCombat.Find(t.OwnerId);
                if (owner == null || owner.Go == null || !owner.Go.activeInHierarchy) { if (t.Puppet.activeSelf) t.Puppet.SetActive(false); continue; }
                Vector3 op = owner.Go.transform.position;
                pos = new Vector3(op.x + (float)s.X, op.y + s.Y, 0f);
            }
            else
            {
                double x = s.X; float y = s.Y;
                if (t.Buffer.Count >= 2 && renderTime > s.Time)
                {
                    AState p = t.Buffer[t.Buffer.Count - 2];
                    double dt = s.Time - p.Time;
                    if (dt > 0.0001 && System.Math.Abs(s.X - p.X) < 8.0)
                    {
                        double ahead = System.Math.Min(renderTime - s.Time, 0.2);
                        x += (s.X - p.X) / dt * ahead;
                        y += (float)((s.Y - p.Y) / dt * ahead);
                    }
                }
                pos = new Vector3((float)(x - FloatingOrigin.Offset), y, 0f);
            }
            bool active = (s.Flags & FActive) != 0 && !t.LocallyConsumed;
            if (t.Puppet.activeSelf != active) t.Puppet.SetActive(active);
            if (!active) continue;
            Transform tr = t.Puppet.transform;
            tr.position = pos;
            tr.rotation = Quaternion.Euler(0f, 0f, s.RotZ);
            tr.localScale = new Vector3(s.SX, s.SY, 1f);
            t.Flags = s.Flags;
            t.CurActivation = s.Activation;
            if (t.PCol != null) t.PCol.enabled = (s.Flags & FCollider) != 0;
            for (int i = 0; i < t.NVis; i++)
            {
                var v = s.Vis[i];
                SpriteRenderer sr = t.PSrs[i];
                if (sr == null) continue;
                Sprite sp = v.Enabled ? SpriteOf(v.SpriteKey) : null;
                if (v.Enabled && sp == null && v.SpriteKey != 0) sp = BossFx.Orb(); // 見つからない絵は仮の丸で出す(判定は正しい)
                sr.enabled = v.Enabled && sp != null;
                if (!sr.enabled) continue;
                sr.sprite = sp;
                sr.color = NetPlayerSnapshot.UnpackColor(v.Color);
                sr.sortingOrder = v.Order;
                Transform st = sr.transform;
                st.localPosition = new Vector3(v.PX, v.PY, 0f);
                st.localRotation = Quaternion.Euler(0f, 0f, v.Rot);
                st.localScale = new Vector3(v.SX, v.SY, 1f);
            }
        }
    }

    // JOIN: ミラーの判定が自分のプレイヤーに触れた。
    public void OnPuppetTouch(int id, Collider2D other)
    {
        if (!tracked.TryGetValue(id, out Tracked t) || t.Puppet == null) return;
        if ((t.Flags & FCollider) == 0 || t.LocallyConsumed) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        int act = t.CurActivation % 64;
        if (!t.ClaimedActivations.Add(act)) return; // 1回の発動で当たるのは最大1回(元の各攻撃と同じ)
        StatTouches++;
        Log($"touch attackId={id} type={t.Type} act={act} owner={t.OwnerId} target=P{t.TargetPn} -> claim");
        if (t.DestroyOnHit)
        {
            // 元の弾と同じく、プレイヤーに触れたら(無敵中でも)消える。HOSTの本物も消してもらう。
            t.LocallyConsumed = true;
            t.Puppet.SetActive(false);
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
                w.WriteValueSafe(ReqConsume);
                w.WriteValueSafe(id);
                NetSession.Manager.CustomMessagingManager.SendNamedMessage(MsgRequest, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }
        if (t.SlowFactor < 1f) pc.ApplyMoveSlow(t.SlowFactor, t.SlowDuration);
        if (t.NoDamage) return; // 減速だけの弾
        int sentBefore = NetMatch.Instance != null ? NetMatch.Instance.StatClaimsSent : 0;
        NetMatch.SetClaimContext(NetMatch.ClaimKind.Attack, id * 64 + act);
        try { pc.TakeDamage(source: $"Net{t.Type}#{id}"); }
        finally { NetMatch.ClearClaimContext(); }
        bool sent = NetMatch.Instance != null && NetMatch.Instance.StatClaimsSent > sentBefore;
        if (!sent) Log($"touch attackId={id} no claim (local: invincible={pc.IsHitInvincible} reacting={pc.IsReacting} shield/lock/pending) - same as single player");
    }

    public string DebugSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in RegisteredByType) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        return $"tracked={tracked.Count} registered={StatRegistered} sent={StatSpawnsSent} recv={StatSpawnsReceived} touches={StatTouches} consumed={StatConsumed} [{sb.ToString().TrimEnd()}]";
    }
}

// JOIN側のミラーの判定(自分のプレイヤーに触れたらNetAttackSyncへ知らせるだけ)。
public class NetAttackPuppet : MonoBehaviour
{
    public int Id;
    void OnTriggerEnter2D(Collider2D other) => Touch(other);
    void OnTriggerStay2D(Collider2D other) => Touch(other);
    void Touch(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null || other.GetComponentInParent<PlayerController>() != pc) return;
        if (NetAttackSync.Instance != null) NetAttackSync.Instance.OnPuppetTouch(Id, other);
    }
}

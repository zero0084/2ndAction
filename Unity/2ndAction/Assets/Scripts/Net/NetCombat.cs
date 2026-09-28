using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチプレイ対応Phase 2(2026-09-26) - 敵・ボスを「ネットワーク上の1体」として共有する層。
//
// 権威(Authority)はHOST(サーバー)に一本化する:
//  - 敵/ボスの出現・AI(既存のT0〜T5行動/ボスAIそのまま)・ノックバック/打ち上げ/叩き落とし・
//    HP・死亡・ラストヒットは、すべてHOSTの既存ゲームコードがそのまま計算する。
//  - HOSTは出現した敵/ボスに NetworkEnemyId を振り、出現/HP/死亡/消滅をReliableで、
//    位置/姿勢/見た目をUnreliable(20Hz、補間用)でJOIN側へ送る。
//  - JOIN側は自分では敵/ボスを一切生成しない(ローカルの出現処理は抑止)。受け取った敵/ボスを
//    同じ生成処理で「見た目と当たり判定だけのパペット」として作り、AIを止めて位置を補間表示する。
//  - JOIN側のプレイヤー攻撃がパペットに当たったら、HPは減らさずに「ダメージ要求」
//    (攻撃者/対象/ダメージ/攻撃種別/HitSequenceId)をHOSTへ送る。HOSTが受け取った順に既存の
//    被弾処理へ流してHPを確定し、結果を全員へ配る(ダメージの確定経路は常にHOSTの1本だけ)。
//  - HPが0になった瞬間にHOSTが IsDead と LastHitPlayer を確定・ロックする。死亡後に届いた攻撃は
//    無視されるので、ほぼ同時の攻撃でもラストヒットは必ず1人に決まり、両端末で一致する。
//
// シングルプレイ(マルチRunでない/セッション無し)では Authority/Replica が常にfalseで、
// ゲーム側のフックは何もしない。
[DefaultExecutionOrder(1150)] // FloatingOrigin(1000)/NetPlayer(1100)の後 = シフト確定後の座標で送受信する
public class NetCombat : MonoBehaviour
{
    public static NetCombat Instance { get; private set; }

    const string MsgReliable = "OMM.Combat";      // HOST→JOIN: 出現/HP/死亡/消滅
    const string MsgState = "OMM.CombatState";    // HOST→JOIN: 位置/姿勢(非信頼)
    const string MsgHit = "OMM.CombatHit";        // JOIN→HOST: ダメージ要求

    const byte OpEnemySpawn = 1, OpBossSpawn = 2, OpDamage = 3, OpDeath = 4, OpDespawn = 5, OpReject = 6;
    const byte HitDamage = 1, HitVacuum = 2;

    public const float StateSendRate = 20f;
    // 補間の遅延。敵はプレイヤーの走行速度で動くことが多い(打ち上げ中など)ため遅延を置かず、最新の値から
    // 直近の速度で外挿する(3倍速の2プロセス試験で表示誤差が最小、ガタつきの増加は見られなかった)。
    // 実機のWi-Fiで補正の飛びが目立つ場合は0.03〜0.05へ。-netEnemyDelay で変更可(自動テスト用)。
    public static double InterpDelay = 0.0;
    const int MaxEntitiesPerStatePacket = 10;
    const float NearRange = 90f;                  // どのプレイヤーからもこれ以上離れた敵は低頻度でしか送らない
    const float FarKeepaliveInterval = 1.0f;
    const float BehindDespawnDistance = 70f;      // 全プレイヤーがこれ以上通過した敵はHOSTが片付ける

    // ===== 役割 =====
    public static bool Authority => NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && NetSession.Manager.IsServer;
    public static bool Replica => NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetSession.Manager.IsServer && NetSession.Manager.IsConnectedClient;
    // JOIN側で、HOSTから届いた物以外の敵/ボスを生成しない。
    public static bool CreatingPuppet { get; private set; }
    public static bool SuppressLocalEnemySpawn => Replica && !CreatingPuppet;
    public static bool SuppressLocalBossSpawn => Replica && !CreatingPuppet;

    public static int LocalPlayerNumber => NetPlayer.Local != null && NetPlayer.Local.PlayerNumber > 0 ? NetPlayer.Local.PlayerNumber : 1;

    // ===== エンティティ =====
    public enum Kind : byte { Enemy = 1, Boss = 2 }
    public enum BossMethod : byte { None = 0, Wild, Cave, Sky, Dragon, WastelandDragon, SkyDragon, Majin, SkyMajin, MechDragon, Reaper }

    public class Entity
    {
        public int Id;
        public Kind Kind;
        public string TypeKey = "";
        public GameObject Go;
        public EnemyController Enemy;
        public WildBossBase Wild;
        public DragonController Dragon;
        public MajinController Majin;
        public GrimReaperController Reaper;
        public Transform Visual;
        public SpriteRenderer Sr;
        public List<Sprite> SpriteTable;
        public int Hp, MaxHp;
        public bool Dead;
        public int LastDamagedBy;     // 最後にダメージを成立させたプレイヤー番号(1〜)
        public int LastHitPlayer;     // HPを0にしたプレイヤー番号(死亡の瞬間にロック。0=プレイヤー以外/未確定)
        public int LastDamageSourcePlayer => LastDamagedBy;
        public byte DeathCause;       // 0=攻撃 1=落下 2=その他
        public int MileReward;
        public EnemySpawnInfo SpawnInfo;
        public BossSpawnInfo BossSpawn;
        public bool SpawnSent;
        public int RunSeed;
        // HOST: 送信の間引き
        public double SentX; public float SentY; public float SentTime = -99f; public int SentHash;
        // JOIN: 補間
        public readonly List<State> Buffer = new List<State>();
        public bool HasState;
        public bool Revealed;
        public float LocalHitCooldown;
        public int Target;            // Phase 2.5: 狙っているプレイヤー番号(HOST=選択結果 / JOIN=受信値)
        public EnemyTargetSelector Selector;
    }

    // 位置/姿勢の1サンプル(全種共通の器。使わない欄は0のまま)
    public struct State
    {
        public double Time;
        public int Id;
        public double X; public float Y;
        public float RotZ, ScaleX, ScaleY;
        public byte Flags;
        public byte SpriteIdx;
        public float VisScaleX, VisScaleY, VisRotZ, VisPosX, VisPosY;
        public uint Color;
        public byte Pose; public sbyte Facing; public byte Windup, Attack;
        public uint BaseColor; public float ExtraX, ExtraY; public short Order; public byte FramesSet;
        public uint Flash;
        public byte Target;           // Phase 2.5: このエンティティが狙っているプレイヤー番号(0=なし)
    }
    public const byte FlagReacting = 1, FlagHpBar = 2, FlagHurtbox = 4, FlagVisible = 8, FlagFlash = 16, FlagHitFlash = 32;

    readonly Dictionary<int, Entity> entities = new Dictionary<int, Entity>();
    public IReadOnlyDictionary<int, Entity> Entities => entities;
    static int nextId = 1;
    int currentSceneSeed;

    // JOIN: 届いたがまだ処理できないReliableメッセージ(シーン読み込み待ち)
    readonly List<(int seed, byte[] data)> pendingReliable = new List<(int, byte[])>();

    // HOST: 二重処理防止(クライアントごとの処理済みHitSequenceId)
    readonly Dictionary<ulong, HashSet<int>> processedHits = new Dictionary<ulong, HashSet<int>>();
    int localHitSeq;

    // 集計(デバッグ/自動テスト用)
    public int StatSpawns, StatDeaths, StatDamageEvents, StatHitRequestsSent, StatHitRequestsApplied, StatHitRequestsIgnored, StatDuplicateHits;
    public readonly List<string> KillLog = new List<string>();

    bool handlersRegistered;
    NetworkManager registeredManager;
    float stateTimer;
    Transform puppetRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[NetCombat]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetCombat>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-netEnemyDelay" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) InterpDelay = d;
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
        // シーンを読み込み直した = 前のRunの敵/ボスは全て消えている。
        entities.Clear();
        processedHits.Clear();
        puppetRoot = null;
        LastBossRewardRecipient = 0;
        currentSceneSeed = NetRunLauncher.IsMultiplayerRun ? NetRunLauncher.ActiveRunSeed : 0;
        KillLog.Clear();
        StatSpawns = StatDeaths = StatDamageEvents = StatHitRequestsSent = StatHitRequestsApplied = StatHitRequestsIgnored = StatDuplicateHits = 0;
    }

    public static void Log(string tag, string msg) => Debug.Log($"[NET][{tag}] {msg}");
    static string P(int playerNumber) => playerNumber > 0 ? "P" + playerNumber : "none";
    static string KindTag(Kind k) => k == Kind.Boss ? "BOSS" : "ENEMY";

    // ===================================================================== //
    // 接続の監視
    // ===================================================================== //

    bool wasReplica, wasAuthority;

    void Update()
    {
        NetworkManager nm = NetSession.Manager;
        bool active = NetSession.IsActive && nm != null && nm.CustomMessagingManager != null;
        if (active && (!handlersRegistered || registeredManager != nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgReliable, OnReliable);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, OnState);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgHit, OnHitRequest);
            handlersRegistered = true;
            registeredManager = nm;
        }
        else if (!active && handlersRegistered)
        {
            handlersRegistered = false;
            registeredManager = null;
        }

        bool replica = Replica, authority = Authority;
        if (wasReplica && !replica) OnReplicaLost();
        if (wasAuthority && !authority) OnAuthorityLost();
        wasReplica = replica; wasAuthority = authority;

        if (replica) ProcessPendingReliable();
        UpdateRemoteAttackers();
    }

    // JOIN側で接続が切れた: パペットはHOSTが居ないと動けないので片付け、以降はシングルと同じ
    // ローカル生成へ戻る(例外の連打を起こさないことが目的)。
    void OnReplicaLost()
    {
        int n = 0;
        foreach (var e in entities.Values)
        {
            if (e.Go != null) { Destroy(e.Go); n++; }
        }
        entities.Clear();
        pendingReliable.Clear();
        Log("SYNC", $"connection lost (JOIN) - removed {n} shared puppets, local spawning resumes");
    }

    void OnAuthorityLost()
    {
        int n = entities.Count;
        entities.Clear();
        processedHits.Clear();
        Log("SYNC", $"connection lost (HOST) - {n} shared entities become local-only");
    }

    // ===================================================================== //
    // HOST: 登録・送信
    // ===================================================================== //

    // Phase 2.5: 直近に倒されたボスの報酬を受け取るプレイヤー(ラストヒット本人。攻撃以外の死因なら最後にダメージを与えた人)。
    public static int LastBossRewardRecipient { get; private set; }

    public static Entity Find(int id) => Instance != null && Instance.entities.TryGetValue(id, out Entity e) ? e : null;

    static List<Sprite> BuildEnemySpriteTable(GameObject go)
    {
        var list = new List<Sprite>();
        var sr = go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null) list.Add(sr.sprite);
        var anim = go.GetComponent<EnemyAnimator>();
        if (anim != null)
        {
            if (anim.attackSprite != null) list.Add(anim.attackSprite);
            if (anim.runFrames != null) foreach (var s in anim.runFrames) if (s != null && !list.Contains(s)) list.Add(s);
            // 天空回廊Enemy(2026-09-28): 状態ごとの絵もJOINへ(HOST/JOINとも同じ順で並べる)
            var p = anim.poses;
            if (p != null)
                foreach (var s in new[] { p.telegraph, p.attack, p.recover, p.hit, p.death, p.dormant, p.wake, p.charge, p.dive })
                    if (s != null && !list.Contains(s)) list.Add(s);
        }
        return list;
    }

    // GroundFactory.CreateEnemyの最後から呼ばれる(HOSTのみ登録)。
    public static void OnEnemyCreated(GameObject go, EnemySpawnInfo info)
    {
        if (!Authority || Instance == null || go == null) return;
        var ec = go.GetComponent<EnemyController>();
        if (ec == null) return;
        var e = new Entity
        {
            Id = nextId++, Kind = Kind.Enemy, Go = go, Enemy = ec, RunSeed = NetRunLauncher.ActiveRunSeed,
            Sr = go.GetComponentInChildren<SpriteRenderer>(), MaxHp = ec.maxHp, Hp = ec.maxHp, MileReward = ec.mileReward,
        };
        e.Visual = e.Sr != null ? e.Sr.transform : go.transform;
        e.TypeKey = info.DefId ?? "";
        e.SpawnInfo = info;
        ec.NetId = e.Id;
        Instance.entities[e.Id] = e;
        AttachTargetSelector(e);
    }

    // ===================================================================== //
    // Phase 2.5: ターゲット選択(HOSTのAIが全ての活動中プレイヤーから狙う相手を選ぶ)
    // ===================================================================== //

    static void AttachTargetSelector(Entity e)
    {
        if (e.Go == null) return;
        var sel = e.Go.GetComponent<EnemyTargetSelector>();
        if (sel == null) sel = e.Go.AddComponent<EnemyTargetSelector>();
        if (e.Kind == Kind.Boss)
        {
            // ボスは位置取りが大きく動くため、乗り換えを慎重に(近さの差4m以上、最低2秒は同じ相手)。
            sel.switchMargin = 4f;
            sel.minHoldTime = 2f;
        }
        e.Selector = sel;
        int id = e.Id;
        sel.OnTargetChanged = c =>
        {
            Entity ent = Find(id);
            if (ent == null) return;
            int before = ent.Target;
            ent.Target = c.Player;
            ApplyTarget(ent, c.T);
            if (before != c.Player) Log("TARGET", $"{KindTag(ent.Kind)} id={ent.Id} type={ent.TypeKey} Target P{(before > 0 ? before.ToString() : "-")} -> {(c.Player > 0 ? "P" + c.Player : "none (no active player)")} (switches={sel.SwitchCount})");
        };
    }

    static void ApplyTarget(Entity e, Transform t)
    {
        if (t == null || e.Go == null) return;
        if (e.Enemy != null)
        {
            var esb = e.Go.GetComponent<EnemySpecialBehavior>();
            if (esb != null) esb.player = t;
            foreach (var f in e.Go.GetComponentsInChildren<EnemyFacing>(true)) f.player = t;
        }
        if (e.Wild != null) e.Wild.NetSetTarget(t, e.Selector);
        if (e.Dragon != null) e.Dragon.NetSetTarget(t, e.Selector);
        if (e.Majin != null) e.Majin.NetSetTarget(t, e.Selector);
        if (e.Reaper != null) e.Reaper.NetSetTarget(t);
        if (e.Kind == Kind.Boss)
            foreach (var f in e.Go.GetComponentsInChildren<EnemyFacing>(true)) f.player = t;
    }

    // GroundFactory.ApplyAttackSpriteから呼ばれる: 敵の種類(EnemyDefinition)を確定する。
    public static void OnEnemyDefinitionKnown(GameObject go, EnemyDefinition def)
    {
        if (!Authority || Instance == null || go == null || def == null) return;
        var ec = go.GetComponent<EnemyController>();
        if (ec == null || ec.NetId == 0) return;
        if (Instance.entities.TryGetValue(ec.NetId, out Entity e) && !e.SpawnSent)
        {
            e.TypeKey = def.enemyId;
            e.SpawnInfo.DefId = def.enemyId;
        }
    }

    // ===== ボス =====
    static BossMethod pendingBossMethod;
    static int pendingBossKind, pendingBossIndex;
    static float pendingBossStandoff;
    static int bossSpawnDepth;

    // BossManagerの各Spawn*の入口/出口から呼ぶ。入れ子(SkyDragon→Dragon)では外側を採用する。
    public static void BeginBossSpawn(BossMethod method, int kind = 0, int index = 0, float standoff = 0f)
    {
        if (bossSpawnDepth++ > 0) return;
        pendingBossMethod = method; pendingBossKind = kind; pendingBossIndex = index; pendingBossStandoff = standoff;
    }

    public static void EndBossSpawn()
    {
        if (--bossSpawnDepth <= 0) { bossSpawnDepth = 0; pendingBossMethod = BossMethod.None; }
    }

    // ボスのInitから呼ばれる。JOINでパペットとして作っている最中ならtrue(AIを始めない)。
    public static bool OnBossInit(MonoBehaviour boss)
    {
        if (CreatingPuppet)
        {
            puppetBuilt = boss;
            return true;
        }
        if (!Authority || Instance == null || boss == null) return false;
        var e = new Entity { Id = nextId++, Kind = Kind.Boss, Go = boss.gameObject, RunSeed = NetRunLauncher.ActiveRunSeed };
        e.BossSpawn = new BossSpawnInfo { Method = pendingBossMethod, KindValue = pendingBossKind, Index = pendingBossIndex, Standoff = pendingBossStandoff };
        AttachBossComponent(e, boss);
        e.TypeKey = boss.GetType().Name;
        Instance.entities[e.Id] = e;
        AttachTargetSelector(e);
        return false;
    }

    static MonoBehaviour puppetBuilt;

    static void AttachBossComponent(Entity e, MonoBehaviour boss)
    {
        e.Wild = boss as WildBossBase;
        e.Dragon = boss as DragonController;
        e.Majin = boss as MajinController;
        e.Reaper = boss as GrimReaperController;
        if (e.Wild != null) { e.Wild.NetId = e.Id; e.MaxHp = e.Wild.maxHp; e.Hp = e.Wild.maxHp; e.MileReward = e.Wild.mileReward; }
        if (e.Dragon != null) { e.Dragon.NetId = e.Id; e.MaxHp = e.Dragon.maxHp; e.Hp = e.Dragon.maxHp; e.MileReward = e.Dragon.mileReward; }
        if (e.Majin != null) { e.Majin.NetId = e.Id; e.MaxHp = e.Majin.maxHp; e.Hp = e.Majin.maxHp; e.MileReward = e.Majin.mileReward; }
        if (e.Reaper != null) { e.MaxHp = 0; e.Hp = 0; }
        var sr = boss.GetComponent<SpriteRenderer>();
        e.Sr = sr;
        Transform vis = boss.transform.Find("Visual");
        e.Visual = vis != null ? vis : boss.transform;
    }

    public static Entity FindByGo(GameObject go)
    {
        if (Instance == null || go == null) return null;
        foreach (var e in Instance.entities.Values) if (e.Go == go) return e;
        return null;
    }

    void LateUpdate()
    {
        if (Authority) AuthorityLateUpdate();
        else if (Replica) ReplicaLateUpdate();
    }

    void AuthorityLateUpdate()
    {
        NetworkManager nm = NetSession.Manager;
        if (nm == null || nm.CustomMessagingManager == null) return;

        // 出現の通知(このフレームに登録された物) - 種類の確定(ApplyAttackSprite)を待ってから送る。
        List<int> gone = null;
        foreach (var e in entities.Values)
        {
            if (!e.SpawnSent && e.Go != null && !e.Dead)
            {
                SendSpawn(e);
                e.SpawnSent = true;
            }
            bool alive = e.Go != null && e.Go.activeInHierarchy;
            if (!alive)
            {
                (gone ??= new List<int>()).Add(e.Id);
            }
        }
        if (gone != null)
        {
            foreach (int id in gone)
            {
                Entity e = entities[id];
                if (e.SpawnSent) SendDespawn(e);
                entities.Remove(id);
            }
        }

        CleanupPassedEnemies();

        stateTimer += Time.unscaledDeltaTime;
        float interval = 1f / StateSendRate;
        if (stateTimer < interval) return;
        stateTimer = Mathf.Min(stateTimer - interval, interval);
        SendStates();
    }

    // 全プレイヤーが十分前方へ通過した敵だけをHOSTが片付ける(片方のプレイヤーの都合で消さない)。
    // ボスは対象外(撃破/正式なボス戦終了でのみ消える)。
    void CleanupPassedEnemies()
    {
        if (!AllPlayersRearmostSceneX(out float rear)) return;
        List<Entity> remove = null;
        foreach (var e in entities.Values)
        {
            if (e.Kind != Kind.Enemy || e.Go == null || e.Dead) continue;
            if (e.Go.transform.position.x < rear - BehindDespawnDistance) (remove ??= new List<Entity>()).Add(e);
        }
        if (remove == null) return;
        foreach (var e in remove) e.Go.SetActive(false); // 次のフレームで消滅として通知される
    }

    void SendSpawn(Entity e)
    {
        double lx = e.Go.transform.position.x + FloatingOrigin.Offset;
        float y = e.Go.transform.position.y;
        using (var w = new FastBufferWriter(256, Allocator.Temp))
        {
            w.WriteValueSafe(e.RunSeed);
            if (e.Kind == Kind.Enemy)
            {
                w.WriteValueSafe(OpEnemySpawn);
                w.WriteValueSafe(e.Id);
                var si = e.SpawnInfo;
                w.WriteValueSafe(new FixedString64Bytes(si.DefId ?? ""));
                w.WriteValueSafe(lx); w.WriteValueSafe(y);
                w.WriteValueSafe(e.MaxHp); w.WriteValueSafe(e.Enemy != null ? e.Enemy.NetHp : e.Hp);
                w.WriteValueSafe(si.Flags);
                w.WriteValueSafe((byte)si.MovementType); w.WriteValueSafe((byte)si.BehaviorKind); w.WriteValueSafe((byte)si.AiTier);
                w.WriteValueSafe(si.VisualScale); w.WriteValueSafe(si.MileReward); w.WriteValueSafe(si.Tint);
                w.WriteValueSafe(si.KnockbackDistance); w.WriteValueSafe(si.KnockbackDuration);
            }
            else
            {
                w.WriteValueSafe(OpBossSpawn);
                w.WriteValueSafe(e.Id);
                var bi = e.BossSpawn;
                w.WriteValueSafe((byte)bi.Method); w.WriteValueSafe(bi.KindValue); w.WriteValueSafe(bi.Index); w.WriteValueSafe(bi.Standoff);
                w.WriteValueSafe(lx); w.WriteValueSafe(y);
                w.WriteValueSafe(e.MaxHp); w.WriteValueSafe(CurrentHp(e));
            }
            SendReliableToClients(w);
        }
        StatSpawns++;
        Log(KindTag(e.Kind), $"Spawn id={e.Id} type={e.TypeKey} x={lx:F1} y={y:F2} hp={CurrentHp(e)}/{e.MaxHp} authority=HOST");
    }

    void SendDespawn(Entity e)
    {
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(e.RunSeed);
            w.WriteValueSafe(OpDespawn);
            w.WriteValueSafe(e.Id);
            w.WriteValueSafe(e.Dead);
            SendReliableToClients(w);
        }
        if (!e.Dead)
        {
            string where = e.Go != null && AllPlayersRangeSceneX(out float rear, out float front) ? $" enemyX={e.Go.transform.position.x + FloatingOrigin.Offset:F1} rearPlayerX={rear + FloatingOrigin.Offset:F1} frontPlayerX={front + FloatingOrigin.Offset:F1}" : "";
            Log(KindTag(e.Kind), $"Despawn id={e.Id} (alive, removed by host){where}");
        }
    }

    static void SendReliableToClients(FastBufferWriter w)
    {
        NetworkManager nm = NetSession.Manager;
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId) continue;
            nm.CustomMessagingManager.SendNamedMessage(MsgReliable, clientId, w, NetworkDelivery.ReliableSequenced);
        }
    }

    static int CurrentHp(Entity e)
    {
        if (e.Enemy != null) return e.Enemy.NetHp;
        if (e.Wild != null) return e.Wild.Hp;
        if (e.Dragon != null) return e.Dragon.Hp;
        if (e.Majin != null) return e.Majin.Hp;
        return e.Hp;
    }

    readonly List<State> sendBuf = new List<State>();

    void SendStates()
    {
        NetworkManager nm = NetSession.Manager;
        double now = nm.ServerTime.Time;
        bool havePlayers = AllPlayersRangeSceneX(out float minX, out float maxX);
        sendBuf.Clear();
        foreach (var e in entities.Values)
        {
            if (!e.SpawnSent || e.Go == null || !e.Go.activeInHierarchy) continue;
            State s = Capture(e);
            s.Time = now;
            float sx = e.Go.transform.position.x;
            bool near = !havePlayers || (sx > minX - NearRange && sx < maxX + NearRange) || e.Kind == Kind.Boss;
            int hash = StateHash(s);
            bool changed = hash != e.SentHash;
            float since = Time.unscaledTime - e.SentTime;
            if (!(near && changed) && since < FarKeepaliveInterval && !(near && since > 0.5f)) continue;
            e.SentHash = hash; e.SentTime = Time.unscaledTime;
            sendBuf.Add(s);
        }
        for (int start = 0; start < sendBuf.Count; start += MaxEntitiesPerStatePacket)
        {
            int n = Mathf.Min(MaxEntitiesPerStatePacket, sendBuf.Count - start);
            using (var w = new FastBufferWriter(1200, Allocator.Temp))
            {
                w.WriteValueSafe(currentSceneSeed != 0 ? currentSceneSeed : NetRunLauncher.ActiveRunSeed);
                w.WriteValueSafe(now);
                w.WriteValueSafe((byte)n);
                for (int i = 0; i < n; i++) WriteState(w, sendBuf[start + i]);
                foreach (ulong clientId in nm.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId) continue;
                    nm.CustomMessagingManager.SendNamedMessage(MsgState, clientId, w, NetworkDelivery.UnreliableSequenced);
                }
            }
        }
    }

    static int StateHash(State s)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt((float)(s.X * 100.0));
            h = h * 31 + Mathf.RoundToInt(s.Y * 100f);
            h = h * 31 + Mathf.RoundToInt(s.RotZ * 10f);
            h = h * 31 + Mathf.RoundToInt(s.ScaleX * 100f) + Mathf.RoundToInt(s.ScaleY * 1000f);
            h = h * 31 + s.Flags + s.SpriteIdx * 7 + s.Pose * 131 + s.FramesSet * 17 + s.Facing * 3 + s.Windup + s.Attack * 5;
            h = h * 31 + (int)s.Color + (int)s.BaseColor + (int)s.Flash;
            h = h * 31 + Mathf.RoundToInt(s.VisScaleX * 100f) + Mathf.RoundToInt(s.VisScaleY * 1000f) + Mathf.RoundToInt(s.VisRotZ * 10f) + Mathf.RoundToInt(s.VisPosY * 100f);
            h = h * 31 + Mathf.RoundToInt(s.ExtraX * 100f) + Mathf.RoundToInt(s.ExtraY * 1000f) + s.Order;
            h = h * 31 + s.Target;
            return h;
        }
    }

    static uint Pack(Color c) => NetPlayerSnapshot.PackColor(c);
    static Color Unpack(uint v) => NetPlayerSnapshot.UnpackColor(v);

    State Capture(Entity e)
    {
        Transform t = e.Go.transform;
        var s = new State
        {
            Id = e.Id,
            X = t.position.x + FloatingOrigin.Offset,
            Y = t.position.y,
            RotZ = t.eulerAngles.z,
            ScaleX = t.localScale.x,
            ScaleY = t.localScale.y,
        };
        if (e.Visual != null && e.Visual != t)
        {
            s.VisScaleX = e.Visual.localScale.x; s.VisScaleY = e.Visual.localScale.y;
            s.VisRotZ = e.Visual.localEulerAngles.z;
            s.VisPosX = e.Visual.localPosition.x; s.VisPosY = e.Visual.localPosition.y;
        }
        if (e.Enemy != null)
        {
            if (e.Enemy.NetIsReacting) s.Flags |= FlagReacting;
            if (e.Sr != null)
            {
                s.Color = Pack(e.Sr.color);
                if (e.Sr.enabled) s.Flags |= FlagVisible;
                int idx = e.SpriteTable != null ? e.SpriteTable.IndexOf(e.Sr.sprite) : -1;
                if (e.SpriteTable == null) { e.SpriteTable = BuildEnemySpriteTable(e.Go); idx = e.SpriteTable.IndexOf(e.Sr.sprite); }
                s.SpriteIdx = (byte)(idx >= 0 && idx < 255 ? idx : 255);
            }
        }
        else if (e.Wild != null) e.Wild.NetCaptureVisual(ref s);
        else if (e.Dragon != null) e.Dragon.NetCaptureVisual(ref s);
        else if (e.Majin != null) e.Majin.NetCaptureVisual(ref s);
        s.Target = (byte)Mathf.Clamp(e.Target, 0, 255);
        return s;
    }

    static void WriteState(FastBufferWriter w, State s)
    {
        w.WriteValueSafe(s.Id); w.WriteValueSafe(s.X); w.WriteValueSafe(s.Y);
        w.WriteValueSafe(s.RotZ); w.WriteValueSafe(s.ScaleX); w.WriteValueSafe(s.ScaleY);
        w.WriteValueSafe(s.Flags); w.WriteValueSafe(s.SpriteIdx);
        w.WriteValueSafe(s.VisScaleX); w.WriteValueSafe(s.VisScaleY); w.WriteValueSafe(s.VisRotZ); w.WriteValueSafe(s.VisPosX); w.WriteValueSafe(s.VisPosY);
        w.WriteValueSafe(s.Color);
        w.WriteValueSafe(s.Pose); w.WriteValueSafe(s.Facing); w.WriteValueSafe(s.Windup); w.WriteValueSafe(s.Attack);
        w.WriteValueSafe(s.BaseColor); w.WriteValueSafe(s.ExtraX); w.WriteValueSafe(s.ExtraY); w.WriteValueSafe(s.Order); w.WriteValueSafe(s.FramesSet);
        w.WriteValueSafe(s.Flash);
        w.WriteValueSafe(s.Target);
    }

    static State ReadState(FastBufferReader r)
    {
        var s = new State();
        r.ReadValueSafe(out s.Id); r.ReadValueSafe(out s.X); r.ReadValueSafe(out s.Y);
        r.ReadValueSafe(out s.RotZ); r.ReadValueSafe(out s.ScaleX); r.ReadValueSafe(out s.ScaleY);
        r.ReadValueSafe(out s.Flags); r.ReadValueSafe(out s.SpriteIdx);
        r.ReadValueSafe(out s.VisScaleX); r.ReadValueSafe(out s.VisScaleY); r.ReadValueSafe(out s.VisRotZ); r.ReadValueSafe(out s.VisPosX); r.ReadValueSafe(out s.VisPosY);
        r.ReadValueSafe(out s.Color);
        r.ReadValueSafe(out s.Pose); r.ReadValueSafe(out s.Facing); r.ReadValueSafe(out s.Windup); r.ReadValueSafe(out s.Attack);
        r.ReadValueSafe(out s.BaseColor); r.ReadValueSafe(out s.ExtraX); r.ReadValueSafe(out s.ExtraY); r.ReadValueSafe(out s.Order); r.ReadValueSafe(out s.FramesSet);
        r.ReadValueSafe(out s.Flash);
        r.ReadValueSafe(out s.Target);
        return s;
    }

    // ===================================================================== //
    // HOST: ダメージの確定(ローカル/リモートどちらの攻撃もここを通る)
    // ===================================================================== //

    // 攻撃者の番号: 0 = この端末のプレイヤー(HOST自身)。それ以外はプレイヤー番号。
    public static int ResolveAttacker(int attacker) => attacker <= 0 ? LocalPlayerNumber : attacker;

    // 既存の被弾処理がHPを減らした直後に呼ばれる(敵/ボス共通)。
    public static void AuthorityDamaged(int netId, int attacker, int damage, int hpAfter, byte attackKind, Vector3 contactScene, bool killed)
    {
        if (!Authority || Instance == null || netId == 0) return;
        if (!Instance.entities.TryGetValue(netId, out Entity e)) return;
        if (e.Dead) return; // 死亡後は何も上書きしない(ロック)
        int who = ResolveAttacker(attacker);
        e.Hp = Mathf.Max(0, hpAfter);
        e.LastDamagedBy = who;
        Instance.StatDamageEvents++;
        double cx = contactScene.x + FloatingOrigin.Offset;
        Log(KindTag(e.Kind), $"Damage id={e.Id} attacker={P(who)} dmg={damage} hp={e.Hp}/{e.MaxHp} kind={attackKind}{(killed ? " (lethal)" : "")}");
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(e.RunSeed);
            w.WriteValueSafe(OpDamage);
            w.WriteValueSafe(e.Id); w.WriteValueSafe(e.Hp); w.WriteValueSafe(who); w.WriteValueSafe(damage); w.WriteValueSafe(attackKind);
            w.WriteValueSafe(cx); w.WriteValueSafe(contactScene.y);
            if (e.SpawnSent) SendReliableToClients(w);
        }
        if (killed) Instance.LockDeath(e, who, 0);
    }

    // 死亡の確定(=LastHitPlayerのロック)。攻撃以外(落下等)で死んだ時はlastHit=0、報酬先はLastDamagedBy。
    void LockDeath(Entity e, int lastHit, byte cause)
    {
        if (e.Dead) return;
        e.Dead = true;
        e.Hp = 0;
        e.LastHitPlayer = lastHit;
        e.DeathCause = cause;
        StatDeaths++;
        int rewardee = lastHit > 0 ? lastHit : e.LastDamagedBy;
        if (e.Kind == Kind.Boss) LastBossRewardRecipient = rewardee;
        NetMatch.HostRecordKill(rewardee, e.Kind == Kind.Boss);
        string line = $"{KindTag(e.Kind)} KILL id={e.Id} LastHit={P(lastHit)} LastDamagedBy={P(e.LastDamagedBy)}";
        KillLog.Add(line);
        Log(KindTag(e.Kind), $"Death id={e.Id} type={e.TypeKey} cause={(cause == 0 ? "hit" : cause == 1 ? "fall" : "other")}");
        Log(KindTag(e.Kind), $"LastHit={P(lastHit)} id={e.Id} lastDamagedBy={P(e.LastDamagedBy)}");
        using (var w = new FastBufferWriter(48, Allocator.Temp))
        {
            w.WriteValueSafe(e.RunSeed);
            w.WriteValueSafe(OpDeath);
            w.WriteValueSafe(e.Id); w.WriteValueSafe(lastHit); w.WriteValueSafe(e.LastDamagedBy); w.WriteValueSafe(cause); w.WriteValueSafe(e.MileReward);
            if (e.SpawnSent) SendReliableToClients(w);
        }
        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
            NetDebugUI.Toast((e.Kind == Kind.Boss ? "BOSS LAST HIT: " : "ENEMY KILL: ") + P(lastHit > 0 ? lastHit : e.LastDamagedBy));
    }

    // 敵の撃破報酬(撃破EXP/MILE等)の行き先を決める。trueを返したらこの端末では付与しない。
    // 報酬はラストヒットしたプレイヤー(攻撃以外の死因なら最後にダメージを与えたプレイヤー)へ。
    public static bool RouteEnemyKillReward(int netId, bool fallDeath)
    {
        if (!Authority || Instance == null || netId == 0) return false;
        if (!Instance.entities.TryGetValue(netId, out Entity e)) return false;
        if (!e.Dead) Instance.LockDeath(e, 0, fallDeath ? (byte)1 : (byte)2);
        int rewardee = e.LastHitPlayer > 0 ? e.LastHitPlayer : e.LastDamagedBy;
        return rewardee > 0 && rewardee != LocalPlayerNumber; // JOIN側がDeathメッセージで自分で付与する
    }

    public static bool RouteBossDefeatReward(int netId) => RouteEnemyKillReward(netId, false);

    // HOSTのボス撃破(ボス戦終了/報酬選択の流れは既存どおりHOSTで進む)。
    public static void AuthorityBossDied(int netId, int attacker)
    {
        if (!Authority || Instance == null || netId == 0) return;
        if (Instance.entities.TryGetValue(netId, out Entity e) && !e.Dead) Instance.LockDeath(e, ResolveAttacker(attacker), 0);
    }

    // JOINからのダメージ要求
    void OnHitRequest(ulong senderClientId, FastBufferReader r)
    {
        if (!Authority) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out byte op);
        r.ReadValueSafe(out int id);
        r.ReadValueSafe(out int hitSeq);
        r.ReadValueSafe(out int damage);
        r.ReadValueSafe(out byte attackKind);
        r.ReadValueSafe(out double cx);
        r.ReadValueSafe(out float cy);
        r.ReadValueSafe(out float vx);
        r.ReadValueSafe(out float vy);
        r.ReadValueSafe(out float vdur);

        if (!processedHits.TryGetValue(senderClientId, out HashSet<int> seen)) processedHits[senderClientId] = seen = new HashSet<int>();
        if (!seen.Add(hitSeq)) { StatDuplicateHits++; Log("HIT", $"duplicate hitSeq={hitSeq} from client {senderClientId} ignored"); return; }
        if (seen.Count > 4096) seen.Clear();

        int attacker = PlayerNumberOfClient(senderClientId);
        if (seed != NetRunLauncher.ActiveRunSeed || !entities.TryGetValue(id, out Entity e) || e.Go == null)
        {
            StatHitRequestsIgnored++;
            Log("HIT", $"request id={id} from {P(attacker)} ignored (unknown/old target)");
            return;
        }
        if (e.Dead)
        {
            StatHitRequestsIgnored++;
            Log(KindTag(e.Kind), $"Damage id={id} from {P(attacker)} ignored - already dead (LastHit={P(e.LastHitPlayer)} locked)");
            return;
        }
        Vector3 contact = new Vector3((float)(cx - FloatingOrigin.Offset), cy, 0f);
        StatHitRequestsApplied++;
        if (op == HitVacuum)
        {
            if (e.Enemy != null) e.Enemy.NetRemoteVacuum(attacker, new Vector2(vx, vy), vdur);
            return;
        }
        damage = Mathf.Clamp(damage, 1, 9999);
        if (e.Enemy != null) e.Enemy.NetApplyRemoteHit(attacker, damage, (PlayerAttackKind)attackKind, contact);
        else if (e.Wild != null) e.Wild.NetApplyRemoteHit(attacker, damage, contact);
        else if (e.Dragon != null) e.Dragon.NetApplyRemoteHit(attacker, damage);
        else if (e.Majin != null) e.Majin.NetApplyRemoteHit(attacker, damage);
    }

    public static int PlayerNumberOfClient(ulong clientId)
    {
        foreach (NetPlayer p in NetPlayer.All) if (p.OwnerClientId == clientId) return p.PlayerNumber;
        return 2;
    }

    // ===================================================================== //
    // 攻撃者の位置/速度(ノックバック/打ち上げの基準。HOSTのリモートプレイヤーは分身から推定)
    // ===================================================================== //

    readonly Dictionary<int, (float x, float y, float vx, float t)> remotePlayers = new Dictionary<int, (float, float, float, float)>();

    void UpdateRemoteAttackers()
    {
        if (!Authority && !Replica) { remotePlayers.Clear(); return; }
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (p == null || p.IsOwner) continue;
            RemotePlayerAvatar a = p.Avatar;
            if (a == null || !a.IsShown) { remotePlayers.Remove(p.PlayerNumber); continue; }
            Vector3 pos = a.transform.position;
            float now = Time.unscaledTime;
            if (remotePlayers.TryGetValue(p.PlayerNumber, out var prev) && now > prev.t)
            {
                float dx = pos.x - prev.x;
                if (Mathf.Abs(dx) > 50f) dx = 0f; // FloatingOriginのシフト
                float v = dx / (now - prev.t);
                // 自動スロー(2026-09-27): 実時間で測った移動速度はスロー倍率ぶん遅く出るため、相手が報告して
                // くる「スロー適用前の走行速度」を優先する(ノックバック等はゲーム内時間で進むので、こちらが正しい基準)。
                float vx = p.IsRunningRemote(Time.realtimeSinceStartup) ? p.RemoteRunSpeed : Mathf.Lerp(prev.vx, v, 0.2f);
                remotePlayers[p.PlayerNumber] = (pos.x, pos.y, vx, now);
            }
            else remotePlayers[p.PlayerNumber] = (pos.x, pos.y, p.IsRunningRemote(Time.realtimeSinceStartup) ? p.RemoteRunSpeed : PlayerController.RunFrameSpeed, now);
        }
    }

    // attacker: 0 = この端末のプレイヤー
    public static bool TryGetAttacker(int attacker, out Vector3 scenePos, out float forwardSpeed)
    {
        scenePos = Vector3.zero; forwardSpeed = 0f;
        if (Instance == null || attacker <= 0 || attacker == LocalPlayerNumber)
        {
            PlayerController pc = PlayerController.Instance;
            if (pc == null) return false;
            scenePos = pc.transform.position; forwardSpeed = pc.CurrentAutoRunSpeed;
            return true;
        }
        if (Instance.remotePlayers.TryGetValue(attacker, out var r))
        {
            scenePos = new Vector3(r.x, r.y, 0f); forwardSpeed = Mathf.Max(0f, r.vx);
            return true;
        }
        return TryGetAttacker(0, out scenePos, out forwardSpeed);
    }

    // 活動中(ALIVE)の全プレイヤー(自分+相手の分身)のシーン座標X範囲。敵の片付け/送信頻度の基準。
    // Phase 3: DOWN/脱落/切断した人は基準にしない(後方で倒れた人のせいで敵がいつまでも残らないように)。
    // 活動中が誰もいない時(全員DOWN直後など)は従来どおり全員を使う。
    public static bool AllPlayersRangeSceneX(out float minX, out float maxX)
    {
        if (RangeSceneX(true, out minX, out maxX)) return true;
        return RangeSceneX(false, out minX, out maxX);
    }

    static bool RangeSceneX(bool aliveOnly, out float minX, out float maxX)
    {
        minX = float.MaxValue; maxX = float.MinValue;
        PlayerController pc = PlayerController.Instance;
        if (pc != null && (!aliveOnly || NetMatch.IsLocalAlive)) { minX = maxX = pc.transform.position.x; }
        if (Instance != null)
        {
            foreach (var kv in Instance.remotePlayers)
            {
                if (aliveOnly && !NetMatch.IsPlayerActive(kv.Key)) continue;
                minX = Mathf.Min(minX, kv.Value.x); maxX = Mathf.Max(maxX, kv.Value.x);
            }
        }
        return minX <= maxX;
    }

    public static bool AllPlayersRearmostSceneX(out float rear)
    {
        bool ok = AllPlayersRangeSceneX(out rear, out _);
        return ok;
    }

    // HOSTの地形の破棄で使う基準X(マルチRunのHOSTだけ全プレイヤーを考慮)。
    // Phase 3: CO-OPでDOWNした人の足場は、その地点で復活できるよう残す(脱落/切断した人は考慮しない)。
    public static float RearmostPlayerX(float localX)
    {
        if (!Authority || Instance == null) return localX;
        bool any = false;
        float x = float.MaxValue;
        if (KeepsTerrain(LocalPlayerNumber)) { x = localX; any = true; }
        foreach (var kv in Instance.remotePlayers)
            if (KeepsTerrain(kv.Key)) { x = Mathf.Min(x, kv.Value.x); any = true; }
        return any ? x : localX;
    }

    static bool KeepsTerrain(int pn)
    {
        var r = NetMatch.Get(pn);
        return r == null || r.State == NetMatch.PState.Alive || r.State == NetMatch.PState.Down;
    }

    // HOSTの地形生成で使う基準X(自分と、活動中の相手の最前)。自分のカメラの先は常に生成しておく。
    public static float ForemostPlayerX(float localX)
    {
        if (!Authority || Instance == null) return localX;
        float x = localX;
        foreach (var kv in Instance.remotePlayers)
            if (NetMatch.IsPlayerActive(kv.Key)) x = Mathf.Max(x, kv.Value.x);
        return x;
    }

    // 敵の「追いつけないので諦める」判定の基準X(活動中のプレイヤーの最後尾)。
    public static float RearmostAlivePlayerX(float fallbackX)
    {
        if (!Authority || Instance == null) return fallbackX;
        return AllPlayersRangeSceneX(out float rear, out _) ? rear : fallbackX;
    }

    // ===================================================================== //
    // JOIN: 受信
    // ===================================================================== //

    void OnReliable(ulong sender, FastBufferReader r)
    {
        if (NetSession.Manager == null || NetSession.Manager.IsServer) return;
        // シーン読み込み待ちでも順序を保ったまま処理できるよう、まず丸ごと保存する。
        int start = r.Position;
        int len = r.Length - start;
        var data = new byte[len];
        r.ReadBytesSafe(ref data, len);
        int seed = System.BitConverter.ToInt32(data, 0);
        pendingReliable.Add((seed, data));
        ProcessPendingReliable();
    }

    bool SceneReadyFor(int seed) =>
        NetRunLauncher.IsMultiplayerRun && NetRunLauncher.ActiveRunSeed == seed && TerrainManager.Instance != null && GameManager.Instance != null;

    void ProcessPendingReliable()
    {
        int i = 0;
        while (i < pendingReliable.Count)
        {
            var (seed, data) = pendingReliable[i];
            if (SceneReadyFor(seed))
            {
                pendingReliable.RemoveAt(i);
                using (var reader = new FastBufferReader(data, Allocator.Temp))
                {
                    try { HandleReliable(reader); }
                    catch (System.Exception ex) { Debug.LogWarning("[NET][SYNC] reliable message failed: " + ex.Message); }
                }
                continue;
            }
            // 次のRun(読み込み中)の物は保持、それ以外(終わったRun)の物は捨てる。
            if (seed == NetRunLauncher.PendingRunSeed) { i++; continue; }
            pendingReliable.RemoveAt(i);
        }
        if (pendingReliable.Count > 2000) pendingReliable.RemoveRange(0, pendingReliable.Count - 2000);
    }

    void HandleReliable(FastBufferReader r)
    {
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out byte op);
        switch (op)
        {
            case OpEnemySpawn: HandleEnemySpawn(r); break;
            case OpBossSpawn: HandleBossSpawn(r); break;
            case OpDamage: HandleDamage(r); break;
            case OpDeath: HandleDeath(r); break;
            case OpDespawn: HandleDespawn(r); break;
        }
    }

    Transform PuppetRoot()
    {
        if (puppetRoot != null) return puppetRoot;
        var go = new GameObject("[NetSharedEnemies]");
        go.AddComponent<FloatingOriginExempt>();
        puppetRoot = go.transform;
        return puppetRoot;
    }

    void HandleEnemySpawn(FastBufferReader r)
    {
        r.ReadValueSafe(out int id);
        r.ReadValueSafe(out FixedString64Bytes defId);
        r.ReadValueSafe(out double x); r.ReadValueSafe(out float y);
        r.ReadValueSafe(out int maxHp); r.ReadValueSafe(out int hp);
        var si = new EnemySpawnInfo { DefId = defId.ToString() };
        r.ReadValueSafe(out si.Flags);
        r.ReadValueSafe(out byte mt); r.ReadValueSafe(out byte bk); r.ReadValueSafe(out byte tier);
        si.MovementType = (EnemyMovementType)mt; si.BehaviorKind = (EnemyBehaviorKind)bk; si.AiTier = (EnemyAiTier)tier;
        r.ReadValueSafe(out si.VisualScale); r.ReadValueSafe(out si.MileReward); r.ReadValueSafe(out si.Tint);
        r.ReadValueSafe(out si.KnockbackDistance); r.ReadValueSafe(out si.KnockbackDuration);
        if (entities.ContainsKey(id)) return;

        EnemyDefinition def = EnemyDatabase.FindById(si.DefId);
        TerrainManager tm = TerrainManager.Instance;
        Vector2 pos = new Vector2((float)(x - FloatingOrigin.Offset), y);
        GameObject go;
        CreatingPuppet = true;
        try
        {
            go = GroundFactory.CreateEnemy(PuppetRoot(), def != null ? def.sprite : tm.squareSprite, pos, Unpack(si.Tint),
                si.Has(EnemySpawnInfo.FHitSpark) ? tm.enemyHitSparkSprite : null,
                si.Has(EnemySpawnInfo.FDeathCloud) ? tm.enemyDeathCloudSprite : null,
                si.MovementType,
                si.Has(EnemySpawnInfo.FShadow) ? tm.enemyGroundShadowSprite : null,
                maxHp, si.BehaviorKind, si.Has(EnemySpawnInfo.FBigKnockback),
                si.Has(EnemySpawnInfo.FProjectile) ? tm.shooterProjectileSprite : null,
                si.Has(EnemySpawnInfo.FVisualFacing), si.Has(EnemySpawnInfo.FFacingRight),
                def != null ? def.runFrames : null, si.MileReward, si.VisualScale, si.AiTier,
                si.Has(EnemySpawnInfo.FMarker) ? tm.squareSprite : null);
            GroundFactory.ApplyAttackSprite(go, def);
        }
        finally { CreatingPuppet = false; }

        var ec = go.GetComponent<EnemyController>();
        ec.hitKnockbackDistance = si.KnockbackDistance;
        ec.hitKnockbackDuration = si.KnockbackDuration;
        ec.NetMakeReplica(id, hp);
        var e = new Entity { Id = id, Kind = Kind.Enemy, Go = go, Enemy = ec, TypeKey = si.DefId, MaxHp = maxHp, Hp = hp, MileReward = si.MileReward, RunSeed = NetRunLauncher.ActiveRunSeed };
        e.Sr = go.GetComponentInChildren<SpriteRenderer>();
        e.Visual = e.Sr != null ? e.Sr.transform : go.transform;
        e.SpriteTable = BuildEnemySpriteTable(go);
        entities[id] = e;
        StatSpawns++;
        Log("ENEMY", $"Spawn id={id} type={e.TypeKey} x={x:F1} y={y:F2} hp={hp}/{maxHp} authority=HOST (puppet)");
    }

    void HandleBossSpawn(FastBufferReader r)
    {
        r.ReadValueSafe(out int id);
        r.ReadValueSafe(out byte method); r.ReadValueSafe(out int kindValue); r.ReadValueSafe(out int index); r.ReadValueSafe(out float standoff);
        r.ReadValueSafe(out double x); r.ReadValueSafe(out float y);
        r.ReadValueSafe(out int maxHp); r.ReadValueSafe(out int hp);
        if (entities.ContainsKey(id)) return;
        BossManager bm = BossManager.Instance;
        if (bm == null) return;

        puppetBuilt = null;
        CreatingPuppet = true;
        try { bm.NetSpawnPuppet((BossMethod)method, kindValue, index, standoff); }
        finally { CreatingPuppet = false; }
        MonoBehaviour boss = puppetBuilt;
        puppetBuilt = null;
        if (boss == null)
        {
            Log("BOSS", $"Spawn id={id} method={(BossMethod)method} could not be rebuilt");
            return;
        }
        var e = new Entity { Id = id, Kind = Kind.Boss, Go = boss.gameObject, TypeKey = boss.GetType().Name, RunSeed = NetRunLauncher.ActiveRunSeed };
        AttachBossComponent(e, boss);
        e.MaxHp = maxHp; e.Hp = hp;
        boss.transform.position = new Vector3((float)(x - FloatingOrigin.Offset), y, 0f);
        if (e.Wild != null) e.Wild.NetMakePuppet(id, hp, maxHp);
        if (e.Dragon != null) e.Dragon.NetMakePuppet(id, hp, maxHp);
        if (e.Majin != null) e.Majin.NetMakePuppet(id, hp, maxHp);
        if (e.Reaper != null) e.Reaper.enabled = false;
        var facing = boss.GetComponent<EnemyFacing>();
        if (facing != null) facing.enabled = false;
        if (boss.transform.parent == null) boss.gameObject.AddComponent<FloatingOriginExempt>();
        entities[id] = e;
        StatSpawns++;
        Log("BOSS", $"Spawn id={id} type={e.TypeKey} method={(BossMethod)method} hp={hp}/{maxHp} authority=HOST (puppet)");
        NetDebugUI.Toast("BOSS: " + e.TypeKey.Replace("Boss", "").Replace("Controller", ""));
    }

    void HandleDamage(FastBufferReader r)
    {
        r.ReadValueSafe(out int id); r.ReadValueSafe(out int hp); r.ReadValueSafe(out int who); r.ReadValueSafe(out int damage); r.ReadValueSafe(out byte attackKind);
        r.ReadValueSafe(out double cx); r.ReadValueSafe(out float cy);
        if (!entities.TryGetValue(id, out Entity e)) return;
        if (e.Dead) return;
        e.Hp = hp;
        e.LastDamagedBy = who;
        StatDamageEvents++;
        Vector3 contact = new Vector3((float)(cx - FloatingOrigin.Offset), cy, 0f);
        bool mine = who == LocalPlayerNumber;
        Log(KindTag(e.Kind), $"Damage id={id} attacker={P(who)} dmg={damage} hp={hp}/{e.MaxHp} (from HOST)");
        if (e.Enemy != null) e.Enemy.NetOnAuthoritativeHp(hp, !mine, contact);
        else if (e.Wild != null) e.Wild.NetSetHp(hp, !mine, contact);
        else if (e.Dragon != null) e.Dragon.NetSetHp(hp, !mine);
        else if (e.Majin != null) e.Majin.NetSetHp(hp, !mine);
    }

    void HandleDeath(FastBufferReader r)
    {
        r.ReadValueSafe(out int id); r.ReadValueSafe(out int lastHit); r.ReadValueSafe(out int lastDamagedBy); r.ReadValueSafe(out byte cause); r.ReadValueSafe(out int mile);
        if (!entities.TryGetValue(id, out Entity e)) return;
        if (e.Dead) return;
        e.Dead = true; e.Hp = 0; e.LastHitPlayer = lastHit; e.LastDamagedBy = lastDamagedBy; e.DeathCause = cause;
        StatDeaths++;
        string line = $"{KindTag(e.Kind)} KILL id={e.Id} LastHit={P(lastHit)} LastDamagedBy={P(lastDamagedBy)}";
        KillLog.Add(line);
        Log(KindTag(e.Kind), $"Death id={id} type={e.TypeKey} cause={(cause == 0 ? "hit" : cause == 1 ? "fall" : "other")} (from HOST)");
        Log(KindTag(e.Kind), $"LastHit={P(lastHit)} id={id} lastDamagedBy={P(lastDamagedBy)}");
        if (e.Enemy != null) e.Enemy.NetMarkDead();
        else if (e.Wild != null) e.Wild.NetPuppetDie();
        else if (e.Dragon != null) e.Dragon.NetPuppetDie();
        else if (e.Majin != null) e.Majin.NetPuppetDie();

        // 撃破報酬はラストヒットしたプレイヤー本人の端末で付与する(HOSTは付与しない)。
        int rewardee = lastHit > 0 ? lastHit : lastDamagedBy;
        if (rewardee == LocalPlayerNumber && GameManager.Instance != null)
        {
            if (e.Kind == Kind.Boss) GameManager.Instance.RegisterBossDefeat(mile);
            else GameManager.Instance.RegisterEnemyKill(mile);
        }
        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
            NetDebugUI.Toast((e.Kind == Kind.Boss ? "BOSS LAST HIT: " : "ENEMY KILL: ") + P(lastHit > 0 ? lastHit : lastDamagedBy));
    }

    void HandleDespawn(FastBufferReader r)
    {
        r.ReadValueSafe(out int id); r.ReadValueSafe(out bool died);
        if (!entities.TryGetValue(id, out Entity e)) return;
        entities.Remove(id);
        if (e.Go == null) return;
        if (e.Enemy != null && died) e.Enemy.NetPlayDeathVisualAndRemove();
        else if (e.Kind == Kind.Boss && died) { /* ボスは撃破演出(NetPuppetDie)が自分で消える */ if (e.Reaper != null) Destroy(e.Go); }
        else Destroy(e.Go);
    }

    void OnState(ulong sender, FastBufferReader r)
    {
        if (!Replica) return;
        r.ReadValueSafe(out int seed);
        r.ReadValueSafe(out double time);
        r.ReadValueSafe(out byte n);
        bool sameRun = seed == NetRunLauncher.ActiveRunSeed;
        for (int i = 0; i < n; i++)
        {
            State s = ReadState(r);
            s.Time = time;
            if (!sameRun) continue;
            if (!entities.TryGetValue(s.Id, out Entity e)) continue;
            var buf = e.Buffer;
            if (buf.Count > 0 && buf[buf.Count - 1].Time >= time) continue; // 古い/重複
            buf.Add(s);
            if (buf.Count > 24) buf.RemoveAt(0);
        }
    }

    // ===================================================================== //
    // JOIN: パペットの表示(補間)
    // ===================================================================== //

    void ReplicaLateUpdate()
    {
        NetworkManager nm = NetSession.Manager;
        double renderTime = nm.ServerTime.Time - InterpDelay;
        foreach (var e in entities.Values)
        {
            if (e.Go == null) continue;
            if (e.LocalHitCooldown > 0f) e.LocalHitCooldown -= Time.deltaTime;
            if (e.Buffer.Count == 0) continue;
            if (!Sample(e.Buffer, renderTime, out State s, out double x, out float y)) continue;
            Apply(e, s, x, y);
        }
    }

    static bool Sample(List<State> buf, double t, out State s, out double x, out float y)
    {
        s = buf[buf.Count - 1]; x = s.X; y = s.Y;
        if (buf.Count == 1 || t >= s.Time)
        {
            // 最新より先: 直近の速度で短く外挿(最大0.2秒)
            if (buf.Count >= 2)
            {
                State p = buf[buf.Count - 2];
                double dt = s.Time - p.Time;
                if (dt > 0.0001 && t > s.Time)
                {
                    double ahead = System.Math.Min(t - s.Time, 0.2);
                    double vx = (s.X - p.X) / dt; float vy = (float)((s.Y - p.Y) / dt);
                    if (System.Math.Abs(s.X - p.X) < 8.0) { x = s.X + vx * ahead; y = s.Y + vy * (float)ahead; }
                }
            }
            return true;
        }
        if (t <= buf[0].Time) { s = buf[0]; x = s.X; y = s.Y; return true; }
        for (int i = buf.Count - 1; i > 0; i--)
        {
            State a = buf[i - 1], b = buf[i];
            if (t < a.Time) continue;
            double span = b.Time - a.Time;
            float f = span > 0.0001 ? (float)((t - a.Time) / span) : 1f;
            bool teleport = System.Math.Abs(b.X - a.X) > 8.0 || Mathf.Abs(b.Y - a.Y) > 8f;
            if (teleport) { s = f < 0.5f ? a : b; x = s.X; y = s.Y; return true; }
            x = a.X + (b.X - a.X) * f;
            y = Mathf.Lerp(a.Y, b.Y, f);
            s = f < 0.5f ? a : b;
            s.RotZ = Mathf.LerpAngle(a.RotZ, b.RotZ, f);
            s.ScaleX = Mathf.Lerp(a.ScaleX, b.ScaleX, f); s.ScaleY = Mathf.Lerp(a.ScaleY, b.ScaleY, f);
            s.VisRotZ = Mathf.LerpAngle(a.VisRotZ, b.VisRotZ, f);
            s.VisPosX = Mathf.Lerp(a.VisPosX, b.VisPosX, f); s.VisPosY = Mathf.Lerp(a.VisPosY, b.VisPosY, f);
            s.VisScaleY = Mathf.Lerp(a.VisScaleY, b.VisScaleY, f);
            return true;
        }
        return true;
    }

    void Apply(Entity e, State s, double x, float y)
    {
        Transform t = e.Go.transform;
        t.position = new Vector3((float)(x - FloatingOrigin.Offset), y, t.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, s.RotZ);
        if (e.Enemy != null || e.Dragon != null || e.Majin != null || e.Reaper != null)
            t.localScale = new Vector3(s.ScaleX, s.ScaleY, 1f);
        if (!e.Revealed) { e.Revealed = true; e.HasState = true; }
        if (e.Target != s.Target) { e.Target = s.Target; }
        if (e.Enemy != null)
        {
            if (e.Visual != null && e.Visual != t)
            {
                e.Visual.localScale = new Vector3(s.VisScaleX, s.VisScaleY, 1f);
                e.Visual.localRotation = Quaternion.Euler(0f, 0f, s.VisRotZ);
                e.Visual.localPosition = new Vector3(s.VisPosX, s.VisPosY, e.Visual.localPosition.z);
            }
            if (e.Sr != null && !e.Dead)
            {
                if (s.SpriteIdx != 255 && e.SpriteTable != null && s.SpriteIdx < e.SpriteTable.Count) e.Sr.sprite = e.SpriteTable[s.SpriteIdx];
                if (!e.Enemy.NetLocalFlashActive) e.Sr.color = Unpack(s.Color);
            }
            e.Enemy.NetRemoteReacting = (s.Flags & FlagReacting) != 0;
        }
        else if (e.Wild != null) e.Wild.NetApplyVisual(s);
        else if (e.Dragon != null) e.Dragon.NetApplyVisual(s);
        else if (e.Majin != null) e.Majin.NetApplyVisual(s);
        else if (e.Reaper != null && e.Visual != null && e.Visual != t)
        {
            e.Visual.localScale = new Vector3(s.VisScaleX, s.VisScaleY, 1f);
            e.Visual.localPosition = new Vector3(s.VisPosX, s.VisPosY, 0f);
            e.Visual.localRotation = Quaternion.Euler(0f, 0f, s.VisRotZ);
        }
    }

    // ===================================================================== //
    // JOIN: 自分の攻撃がパペットに当たった → HOSTへダメージ要求
    // ===================================================================== //

    public static void RequestHit(int netId, int damage, PlayerAttackKind kind, Vector3 contactScene)
        => SendHitRequest(HitDamage, netId, damage, (byte)kind, contactScene, Vector2.zero, 0f);

    public static void RequestVacuum(int netId, Vector2 offset, float duration)
        => SendHitRequest(HitVacuum, netId, 0, 0, Vector3.zero, offset, duration);

    static void SendHitRequest(byte op, int netId, int damage, byte kind, Vector3 contactScene, Vector2 v, float vdur)
    {
        if (!Replica || Instance == null) return;
        if (!Instance.entities.TryGetValue(netId, out Entity e) || e.Dead) return;
        NetworkManager nm = NetSession.Manager;
        int seq = ++Instance.localHitSeq;
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(NetRunLauncher.ActiveRunSeed);
            w.WriteValueSafe(op);
            w.WriteValueSafe(netId); w.WriteValueSafe(seq); w.WriteValueSafe(damage); w.WriteValueSafe(kind);
            w.WriteValueSafe(contactScene.x + FloatingOrigin.Offset); w.WriteValueSafe(contactScene.y);
            w.WriteValueSafe(v.x); w.WriteValueSafe(v.y); w.WriteValueSafe(vdur);
            nm.CustomMessagingManager.SendNamedMessage(MsgHit, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
        if (op == HitDamage)
        {
            Instance.StatHitRequestsSent++;
            Log(KindTag(e.Kind), $"Damage request id={netId} attacker={P(LocalPlayerNumber)} dmg={damage} kind={(PlayerAttackKind)kind} hitSeq={seq} -> HOST");
        }
    }

    // ===================================================================== //
    // デバッグ表示(Debug Mode時のみ)
    // ===================================================================== //

    GUIStyle labelStyle;

    void OnGUI()
    {
        if (!(Authority || Replica)) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.DebugMode || !gm.HasStarted) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerCenter, richText = true };
            labelStyle.normal.textColor = Color.white;
        }
        string role = Authority ? "HOST" : "JOIN";
        foreach (var e in entities.Values)
        {
            if (e.Go == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(e.Go.transform.position + Vector3.up * (e.Kind == Kind.Boss ? 3.2f : 1.6f));
            if (sp.z < 0f || sp.x < -50 || sp.x > Screen.width + 50) continue;
            int hp = Authority ? CurrentHp(e) : e.Hp;
            string text = $"<b>#{e.Id}</b> HP {hp}/{e.MaxHp}  Target={P(e.Target)}\nAuth:HOST  Dmg:{P(e.LastDamagedBy)}" + (e.Dead ? $"\n<color=#ff8080>DEAD LastHit:{P(e.LastHitPlayer)}</color>" : "");
            var rect = new Rect(sp.x - 90f, Screen.height - sp.y - 48f, 180f, 48f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x + 20f, rect.y + 4f, rect.width - 40f, rect.height - 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, text, labelStyle);
        }
        var panel = new Rect(10f, Screen.height - 170f, 380f, 150f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var sb = new System.Text.StringBuilder();
        sb.Append($"NET COMBAT [{role}] me=P{LocalPlayerNumber} shared={entities.Count}\n");
        sb.Append($"spawn={StatSpawns} dmgEv={StatDamageEvents} death={StatDeaths} req sent={StatHitRequestsSent} applied={StatHitRequestsApplied} ignored={StatHitRequestsIgnored} dup={StatDuplicateHits}\n");
        for (int i = Mathf.Max(0, KillLog.Count - 5); i < KillLog.Count; i++) sb.Append(KillLog[i]).Append('\n');
        GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, panel.height - 8f), sb.ToString());
    }

    // 自動テスト用: 生存中の共有エンティティの要約(両端末で比較する)。
    public string DebugSignature()
    {
        var ids = new List<int>(entities.Keys);
        ids.Sort();
        var sb = new System.Text.StringBuilder();
        int alive = 0;
        foreach (int id in ids)
        {
            var e = entities[id];
            if (e.Dead || e.Go == null) continue;
            alive++;
            int hp = Authority ? CurrentHp(e) : e.Hp;
            sb.Append(id).Append(':').Append(hp).Append(' ');
        }
        return $"alive={alive} [{sb.ToString().TrimEnd()}]";
    }
}

// HOSTで敵を生成した時の引数(JOIN側が同じ見た目のパペットを作るのに使う)。
public class EnemySpawnInfo
{
    public const byte FHitSpark = 1, FDeathCloud = 2, FShadow = 4, FProjectile = 8, FVisualFacing = 16, FFacingRight = 32, FBigKnockback = 64, FMarker = 128;
    public string DefId;
    public byte Flags;
    public EnemyMovementType MovementType;
    public EnemyBehaviorKind BehaviorKind;
    public EnemyAiTier AiTier;
    public float VisualScale = 1f;
    public int MileReward;
    public uint Tint;
    public float KnockbackDistance, KnockbackDuration;
    public bool Has(byte f) => (Flags & f) != 0;
}

public class BossSpawnInfo
{
    public NetCombat.BossMethod Method;
    public int KindValue, Index;
    public float Standoff;
}

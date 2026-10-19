using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 接続中のプレイヤー1人を表すネットワークオブジェクト。
// NetSessionのPlayerPrefab(Resources/Net/NetPlayer)として、接続した端末ごとに1つずつ生成される。
//
// 権限(Authority)の基本設計:
//  - プレイヤーの動き: そのプレイヤーを操作している端末(Owner)が正。Ownerだけがスナップショットを送り、
//    他端末はそれを補間して表示するだけ(ローカル入力は一切入らない)。
//  - 接続/スロット番号/Run開始: HOST(サーバー)が正。
//  - 将来の敵/ボス/ダメージ/ラストヒット: HOSTを正とする想定(サーバー権威のNetworkObjectとして追加する)。
//
// ゲーム本体(PlayerController/GameManager等)には依存するだけで手を入れない - 自分のプレイヤーの
// 状態は既存の公開プロパティから読み取り、相手の表示はPlayerControllerを持たない専用の
// RemotePlayerAvatarで行う(PlayerController.Instanceや敵の追跡対象には一切影響しない)。
// LateUpdateでFloatingOrigin(実行順1000)より後に動かす: 自分のプレイヤーはその
// フレームの移動が確定した後に記録し、相手の分身はシーンのシフト後の座標系で置く
// (先に置くと、シフトしたフレームだけ分身が1024ユニット以上ずれて見える)。
[DefaultExecutionOrder(1100)]
public class NetPlayer : NetworkBehaviour
{
    public const byte PhaseHome = 0;
    public const byte PhaseInRun = 1;
    public const byte PhaseRunEnded = 2;

    public static readonly List<NetPlayer> All = new List<NetPlayer>();
    public static NetPlayer Local { get; private set; }

    // 各プレイヤーが選んだキャラクター(将来キャラ選択同期を入れても、このまま使える)。
    public readonly NetworkVariable<FixedString32Bytes> CharacterId = new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // 今どの画面にいるか(Home/Run中/Run終了) - HOSTが次のRunを始めてよいかの判定に使う。
    public readonly NetworkVariable<byte> Phase = new NetworkVariable<byte>(PhaseHome, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // 参加しているRunのSeed(0=Runに参加していない)。同じRunの相手だけを表示するのに使う。
    public readonly NetworkVariable<int> RunSeed = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // HOSTが割り当てるプレイヤー番号(0始まり)。MaxPlayersまで人数非依存。
    public readonly NetworkVariable<int> Slot = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // コードネーム(2026-10-08)。表示名だけで、本人の識別は接続番号(OwnerClientId)。受け取った側でも整え直して表示する
    public readonly NetworkVariable<FixedString128Bytes> CodenameVar = new NetworkVariable<FixedString128Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 参加者の識別(2026-10-14、識別用オーラ): 端末ごとに変わらない鍵(オンラインはサービスのプレイヤー ID、LAN は端末の ID)。
    // HOST は部屋の間「鍵 → 番号」を覚え、抜けた人が戻った時に同じ番号(= 同じ色)へ戻す。新しく入る人には、抜けている人の番号をなるべく使わない。
    // 番号を詰め直すことはしない(途中で抜けても残った人の番号/色は変わらない)。ラン中の途中参加は今までどおり受け付けない
    // 2026-10-15 第4弾: この人の WANTED のカードの有効 Lv(協力マルチで HOST が一番高い Lv を使う)
    public readonly NetworkVariable<byte> WantedLevelVar = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public static int MaxRemoteWantedLevel() { int m = 0; foreach (var p in All) if (p != null && p.IsSpawned) m = Mathf.Max(m, p.WantedLevelVar.Value); return m; }
    public readonly NetworkVariable<FixedString64Bytes> ParticipantKey = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public const string LocalKeyPref = "Net.ParticipantKeyV1";
    static readonly Dictionary<string, int> sessionSlots = new Dictionary<string, int>(); // HOST: この部屋で使った「鍵 → 番号」
    public static string LocalParticipantKey()
    {
        // 2026-10-19: UGS の PlayerId は ONLINE の時だけ使う(LOCAL の相手へ渡す必要が無い。同じ起動でランキング/ONLINE を開いた後の LOCAL でも送らない)
        string pid = NetSession.Connection == ConnectionType.Online ? OnlineServices.PlayerId : null;
        if (!string.IsNullOrEmpty(pid)) return "u:" + pid;
        string k = SaveStore.GetString(LocalKeyPref, "");
        if (string.IsNullOrEmpty(k)) { k = System.Guid.NewGuid().ToString("N"); SaveStore.SetString(LocalKeyPref, k); SaveStore.Save(); }
        return "d:" + k;
    }

    void OnParticipantKeyChanged(FixedString64Bytes before, FixedString64Bytes now)
    {
        if (!IsServer) return;
        string key = now.ToString();
        if (string.IsNullOrEmpty(key)) return;
        if (sessionSlots.TryGetValue(key, out int want) && want != Slot.Value)
        {
            bool taken = false;
            foreach (var p in All) if (p != this && p.Slot.Value == want) { taken = true; break; }
            if (!taken) { NetSession.Log($"Player {OwnerClientId} came back: slot {Slot.Value} -> {want} (same participant)"); Slot.Value = want; }
        }
        sessionSlots[key] = Slot.Value;
    }

    public int PlayerNumber => Slot.Value >= 0 ? Slot.Value + 1 : 0;
    public static string TagOf(int playerNumber)
    {
        foreach (var p in All) if (p != null && p.PlayerNumber == playerNumber) return p.Tag;
        return $"P{playerNumber}";
    }
    // 表示名: コードネーム(未設定なら P1 等)
    public string DisplayName { get { string n = global::Codename.Sanitize(CodenameVar.Value.ToString()); return string.IsNullOrEmpty(n) ? $"P{PlayerNumber}" : n; } }
    public string Tag => $"P{PlayerNumber}" + (string.IsNullOrEmpty(global::Codename.Sanitize(CodenameVar.Value.ToString())) ? "" : " " + DisplayName);

    // 位置を送る回数(毎秒)。開発版だけ -netSnapRate N で比べられる(2026-10-12、中継のまとめ送りの回数も同じにする)
    static float sendRateCached = -1f;
    static float SendRate
    {
        get
        {
            if (sendRateCached > 0f) return sendRateCached;
            sendRateCached = 30f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var a = QaArgs.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-netSnapRate" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)) sendRateCached = Mathf.Clamp(r, 5f, 60f);
#endif
            return sendRateCached;
        }
    }
    float sendTimer;
    bool haveLastSample;
    double lastSampleTime, lastSampleX;
    float lastSampleY;

    readonly NetSnapshotInterpolator interpolator = new NetSnapshotInterpolator();
    RemotePlayerAvatar avatar;
    string avatarCharacterId;
    int snapshotsReceived;
    public int SnapshotsReceived => snapshotsReceived;
    public RemotePlayerAvatar Avatar => avatar;
    public float PlaybackLag => (float)interpolator.PlaybackLag;

    public override void OnNetworkSpawn()
    {
        // シーンの再読み込み(Run開始/Home復帰)をまたいでも接続を保つ。
        DontDestroyOnLoad(gameObject);
        All.Add(this);
        if (IsServer && IsOwner) sessionSlots.Clear(); // HOST 自身 = 新しい部屋
        ParticipantKey.OnValueChanged += OnParticipantKeyChanged;
        if (IsServer) Slot.Value = AssignSlot();
        if (IsOwner)
        {
            Local = this;
            ParticipantKey.Value = new FixedString64Bytes(LocalParticipantKey());
            RefreshOwnerState();
        }
        if (IsServer) OnParticipantKeyChanged(default, ParticipantKey.Value);
        NetSession.Log($"Player spawned clientId={OwnerClientId} ownerId={OwnerClientId} netObjectId={NetworkObjectId} isLocal={IsOwner} slot={Slot.Value}");
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        DestroyAvatar();
        NetSession.Log($"Player despawned clientId={OwnerClientId} isLocal={IsOwner}");
    }

    public override void OnDestroy()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        DestroyAvatar();
        base.OnDestroy();
    }

    int AssignSlot()
    {
        // 抜けている人の番号(この部屋で使った鍵の番号のうち、今いない人の物)は、他が空いている間は使わない
        var reserved = new HashSet<int>();
        foreach (var kv in sessionSlots)
        {
            bool present = false;
            foreach (NetPlayer p in All) if (p != this && p.ParticipantKey.Value.ToString() == kv.Key) { present = true; break; }
            if (!present) reserved.Add(kv.Value);
        }
        for (int pass = 0; pass < 2; pass++)
            for (int slot = 0; slot < NetSession.MaxPlayers; slot++)
            {
                if (pass == 0 && reserved.Contains(slot)) continue;
                bool used = false;
                foreach (NetPlayer p in All)
                {
                    if (p != this && p.Slot.Value == slot) { used = true; break; }
                }
                if (!used) return slot;
            }
        return All.Count - 1;
    }

    void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsOwner) OwnerUpdate();
        else RemoteUpdate();
        if (IsServer) FlushRelays();
    }

    // ===== 自分のプレイヤー(Owner) =====

    void RefreshOwnerState()
    {
        GameManager gm = GameManager.Instance;
        byte phase = PhaseHome;
        int seed = 0;
        string charId = gm != null ? gm.SelectedCharacterId : "";
        if (gm != null && gm.HasStarted && NetRunLauncher.IsMultiplayerRun)
        {
            phase = gm.IsGameOver ? PhaseRunEnded : PhaseInRun;
            seed = NetRunLauncher.ActiveRunSeed;
            if (!string.IsNullOrEmpty(gm.ActiveRunCharacterId)) charId = gm.ActiveRunCharacterId;
        }
        if (Phase.Value != phase) Phase.Value = phase;
        if (RunSeed.Value != seed) RunSeed.Value = seed;
        var fs = new FixedString32Bytes(charId ?? "");
        if (!CharacterId.Value.Equals(fs)) CharacterId.Value = fs;
        var cn = new FixedString128Bytes(FitBytes(global::Codename.Current, 120));
        if (!CodenameVar.Value.Equals(cn)) CodenameVar.Value = cn;
        byte wl = (byte)(gm != null && gm.HasStarted && NetRunLauncher.IsMultiplayerRun ? Mathf.Clamp(Mathf.RoundToInt(gm.Card.Get(EffectType.WantedLevel)), 0, 255) : 0);
        if (WantedLevelVar.Value != wl) WantedLevelVar.Value = wl;
    }

    // 送れる長さ(UTF-8 のバイト数)に、文字の途中で切らずに収める
    static string FitBytes(string s, int maxBytes)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var si = new System.Globalization.StringInfo(s);
        for (int n = si.LengthInTextElements; n > 0; n--)
        {
            string t = si.SubstringByTextElements(0, n);
            if (System.Text.Encoding.UTF8.GetByteCount(t) <= maxBytes) return t;
        }
        return "";
    }

    void OwnerUpdate()
    {
        RefreshOwnerState();
        if (Phase.Value != PhaseInRun) { haveLastSample = false; return; }

        sendTimer += Time.unscaledDeltaTime;
        float interval = 1f / SendRate;
        if (sendTimer < interval) return;
        sendTimer = Mathf.Min(sendTimer - interval, interval);

        if (TrySampleLocalPlayer(out NetPlayerSnapshot snap))
        {
            // 2026-10-11: 止まっている間(カード選択中/DOWN/脱落/終了の姿勢)は位置が変わらないので毎秒6回に間引く
            bool running = (snap.Flags & NetPlayerSnapshot.FlagRunning) != 0;
            if (!running && Time.unscaledTime - lastSendRealtime < 1f / StationarySendRate) return;
            lastSendRealtime = Time.unscaledTime;
            // 2026-10-11: JOIN は HOST へだけ送り、HOST が送り先ごとに中継する(遠い人へは間引く)。HOST 自身の分も同じ基準で送る
            if (IsServer) RelayFromHost(snap, true);
            else { SnapshotToServerRpc(snap); NetStats.Sent("snapshot", RpcHeaderBytes + snap.WireSize); }
        }
    }

    // ===== 位置の中継(2026-10-11 マルチ安定化) =====
    // 以前: Owner が SendTo.NotOwner で全員へ → NGO が HOST 経由で全 JOIN へ中継(人数Nで (N-1)×(N-2) 件/回、遠い人へも30Hz)。
    // いま: JOIN → HOST(1件)/ HOST が送り先ごとに、近い人(130m以内)へは毎回、遠い人へは6回に1回(5Hz)だけ送る。
    //       遠い人の分身は相手の画面の外なので、間引いても見た目は変わらない。近くで一緒に走っている人は今までどおり30Hz。
    const float StationarySendRate = 6f;
    const float FarRelayDistance = 130f; // 縦画面の斜め表示は道の先(約120m)まで見えるので、それより遠い人だけ間引く
    const int FarRelayEvery = 6;
    float lastSendRealtime = -99f;
    int relayCounter;

    // 2026-10-12(同期の版 4): 送り先ごとに「その間に届いた全員の位置」を1件へまとめ、毎秒 RelayFlushRate 回だけ送る。
    // 以前は 1人分ずつ別の RPC(8人で HOST の送信が約 900件/秒、うち見出しだけで約 2割)。待ちは最大 1/30秒(補間の 0.1秒の内)。
    struct RelayEntry { public uint ObjId; public NetPlayerSnapshot Snap; }
    struct RelayBatch : INetworkSerializable
    {
        public RelayEntry[] Items; public int Count;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            byte n = (byte)Count;
            s.SerializeValue(ref n);
            if (s.IsReader) { Count = n; Items = new RelayEntry[n]; }
            for (int i = 0; i < n; i++)
            {
                uint id = Items[i].ObjId; var snap = Items[i].Snap;
                s.SerializeValue(ref id);
                snap.NetworkSerialize(s);
                if (s.IsReader) Items[i] = new RelayEntry { ObjId = id, Snap = snap };
            }
        }
    }
    static float RelayFlushRate => SendRate;
    const int RelayMaxPerPlayer = 2; // 1回分の間に同じ人の位置が2つ以上たまったら古い方を捨てる(送る側の揺らぎ)
    static readonly Dictionary<ulong, List<RelayEntry>> pendingRelay = new Dictionary<ulong, List<RelayEntry>>();
    static float relayFlushTimer;
    static int relayFlushFrame = -1;
    public static int RelayBatchesSent, RelaySnapshotsSent, RelayDroppedStale; // 確認用

    void RelayFromHost(NetPlayerSnapshot snap, bool own)
    {
        relayCounter++;
        float sx = (float)(snap.X - FloatingOrigin.Offset);
        bool jump = (snap.Flags & NetPlayerSnapshot.FlagTeleported) != 0;
        uint objId = (uint)NetworkObjectId;
        foreach (var v in NetInterest.Viewers())
        {
            if (v.ClientId == OwnerClientId) continue;
            bool near = !v.HasPos || jump || Mathf.Abs(v.SceneX - sx) < FarRelayDistance;
            if (!(near || relayCounter % FarRelayEvery == 0)) continue;
            if (!pendingRelay.TryGetValue(v.ClientId, out var list)) pendingRelay[v.ClientId] = list = new List<RelayEntry>(16);
            int same = 0, firstSame = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].ObjId == objId) { if (firstSame < 0) firstSame = i; same++; }
            if (same >= RelayMaxPerPlayer) { list.RemoveAt(firstSame); RelayDroppedStale++; }
            list.Add(new RelayEntry { ObjId = objId, Snap = snap });
        }
    }

    // HOST: どの NetPlayer からでも1フレームに1回だけ(LateUpdate)。毎秒 RelayFlushRate 回、送り先ごとに1件で送る
    void FlushRelays()
    {
        if (relayFlushFrame == Time.frameCount) return;
        relayFlushFrame = Time.frameCount;
        relayFlushTimer += Time.unscaledDeltaTime;
        if (relayFlushTimer < 1f / RelayFlushRate) return;
        relayFlushTimer = Mathf.Min(relayFlushTimer - 1f / RelayFlushRate, 1f / RelayFlushRate);
        var nm = NetworkManager;
        foreach (var kv in pendingRelay)
        {
            var list = kv.Value;
            if (list.Count == 0) continue;
            if (nm == null || !nm.ConnectedClients.ContainsKey(kv.Key)) { list.Clear(); continue; }
            var batch = new RelayBatch { Items = list.ToArray(), Count = Mathf.Min(list.Count, 255) };
            SnapshotBatchRpc(batch, RpcTarget.Single(kv.Key, RpcTargetUse.Temp));
            int bytes = RpcHeaderBytes + 1;
            for (int i = 0; i < batch.Count; i++) bytes += 4 + batch.Items[i].Snap.WireSize;
            NetStats.Sent("snapshotRelay", bytes);
            RelayBatchesSent++; RelaySnapshotsSent += batch.Count;
            list.Clear();
        }
    }

    // JOIN → HOST
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void SnapshotToServerRpc(NetPlayerSnapshot snapshot)
    {
        ReceiveSnapshot(snapshot);
        RelayFromHost(snapshot, false);
    }

    // HOST → 1人の JOIN(その間に届いた全員分)
    [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
    void SnapshotBatchRpc(RelayBatch batch, RpcParams rpcParams = default)
    {
        int bytes = RpcHeaderBytes + 1;
        var spawned = NetworkManager != null ? NetworkManager.SpawnManager.SpawnedObjects : null;
        for (int i = 0; i < batch.Count; i++)
        {
            bytes += 4 + batch.Items[i].Snap.WireSize;
            if (spawned == null || !spawned.TryGetValue(batch.Items[i].ObjId, out var no) || no == null) continue;
            var p = no.GetComponent<NetPlayer>();
            if (p != null && !p.IsOwner) p.ReceiveSnapshot(batch.Items[i].Snap, false);
        }
        NetStats.Received("snapshot", bytes);
    }

    bool TrySampleLocalPlayer(out NetPlayerSnapshot s)
    {
        s = default;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return false;
        PlayerAnimator anim = pc.GetComponent<PlayerAnimator>();
        SpriteRenderer sr = anim != null ? anim.VisualRenderer : null;
        if (anim == null || sr == null) return false;

        // このフレームの移動(Time.deltaTime分)が反映された位置なので、時刻もフレームの時刻を使う
        // (呼び出した瞬間の実時間だと、フレーム内の処理の揺らぎがそのまま速度のブレになる)。
        double now = Time.unscaledTimeAsDouble;
        Transform root = pc.transform;
        Transform vis = sr.transform;
        double x = root.position.x + FloatingOrigin.Offset;
        float y = root.position.y;

        s.Time = now;
        s.X = x;
        s.Y = y;
        if (haveLastSample && now > lastSampleTime)
        {
            float dt = (float)(now - lastSampleTime);
            double dx = x - lastSampleX;
            s.VX = Mathf.Clamp((float)(dx / dt), -60f, 60f);
            s.VY = Mathf.Clamp((y - lastSampleY) / dt, -60f, 60f);
            if (System.Math.Abs(dx) > 6.0 || Mathf.Abs(y - lastSampleY) > 6f) s.Flags |= NetPlayerSnapshot.FlagTeleported;
        }
        haveLastSample = true;
        lastSampleTime = now;
        lastSampleX = x;
        lastSampleY = y;

        s.RootRotZ = root.eulerAngles.z;
        s.RootScaleX = root.localScale.x;
        s.VisPosX = vis.localPosition.x;
        s.VisPosY = vis.localPosition.y;
        s.VisRotZ = vis.localEulerAngles.z;
        s.VisScaleX = vis.localScale.x;
        s.VisScaleY = vis.localScale.y;
        s.ColorRGBA = NetPlayerSnapshot.PackColor(sr.color);
        s.State = (byte)anim.CurrentState;
        s.Frame = (byte)Mathf.Clamp(anim.CurrentFrameIndex, 0, 255);
        s.AttackStage = (byte)Mathf.Clamp(pc.CurrentAttackStage, 0, 255);
        s.FinishTier = (byte)Mathf.Clamp(pc.FinishTierIndex, 0, 255);
        if (pc.IsGrounded) s.Flags |= NetPlayerSnapshot.FlagGrounded;
        if (sr.enabled) s.Flags |= NetPlayerSnapshot.FlagVisible;
        GameManager gm = GameManager.Instance;
        // Phase 3: DOWN/脱落中は「走行中」ではない(自動スロー/ボスの並走などの基準から外れる)。
        bool running = gm != null && gm.HasStarted && !gm.IsGameOver && !gm.CountdownActive && !pc.IsDeadPosing && !pc.IsFinishing && !pc.NetIsDowned && !pc.NetIsChoosing; // カード選択中の本人も走行中ではない
        if (running) s.Flags |= NetPlayerSnapshot.FlagRunning;
        s.RunSpeed = running ? pc.CurrentAutoRunSpeed : 0f;
        s.Distance = pc.DistanceExact;
        return true;
    }

    // 自動スロー(2026-09-27): 相手の最新の走行状態(HOSTが最高速度を決めるのに使う)。
    float remoteRunSpeed;
    bool remoteRunning;
    float lastSnapshotRealtime = -99f;
    public float RemoteRunSpeed => remoteRunSpeed;
    double remoteDistance;
    public double RemoteDistance => remoteDistance;

    // 参加中かつ走行中として、直近1秒以内に報告してきたか(ダウン/離脱/切断した人の古い値を使わない)。
    public bool IsRunningRemote(float nowRealtime)
    {
        return Phase.Value == PhaseInRun && remoteRunning && nowRealtime - lastSnapshotRealtime < 1f;
    }

    const int RpcHeaderBytes = 24; // RPC の見出し(概算)。計測用
    // 位置/姿勢は毎秒30回・非信頼(UDP)で送る - 古い値の再送を待つより次の値を使う方が高速時に有利。
    void ReceiveSnapshot(NetPlayerSnapshot snapshot, bool count = true)
    {
        snapshotsReceived++;
        if (count) NetStats.Received("snapshot", RpcHeaderBytes + snapshot.WireSize);
        interpolator.Add(snapshot);
        remoteRunSpeed = snapshot.RunSpeed;
        remoteDistance = snapshot.Distance;
        remoteRunning = (snapshot.Flags & NetPlayerSnapshot.FlagRunning) != 0;
        lastSnapshotRealtime = Time.realtimeSinceStartup;
    }

    // ===== 相手のプレイヤー(非Owner) =====

    bool ShouldShowAvatar()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || !NetRunLauncher.IsMultiplayerRun) return false;
        if (Phase.Value != PhaseInRun) return false;
        if (RunSeed.Value == 0 || RunSeed.Value != NetRunLauncher.ActiveRunSeed) return false;
        return PlayerController.Instance != null;
    }

    void RemoteUpdate()
    {
        if (!ShouldShowAvatar())
        {
            if (avatar != null) DestroyAvatar();
            if (Phase.Value != PhaseInRun) interpolator.Clear();
            return;
        }

        string charId = CharacterId.Value.ToString();
        if (avatar == null || avatarCharacterId != charId)
        {
            DestroyAvatar();
            PlayerAnimator template = PlayerController.Instance.GetComponent<PlayerAnimator>();
            avatar = RemotePlayerAvatar.Create(template, CharacterDatabase.FindById(charId), this);
            avatarCharacterId = charId;
            NetSession.Log($"Remote avatar created clientId={OwnerClientId} character={charId}");
        }

        if (interpolator.Sample(Time.unscaledDeltaTime, out NetPlayerSnapshot d, out double x, out float y,
                out float visPosX, out float visPosY, out float visRotZ, out float visScaleX, out float visScaleY, out float rootRotZ))
        {
            avatar.Apply(d, x, y, visPosX, visPosY, visRotZ, visScaleX, visScaleY, rootRotZ);
        }
        else
        {
            avatar.SetHidden();
        }
    }

    void DestroyAvatar()
    {
        if (avatar != null) Destroy(avatar.gameObject);
        avatar = null;
        avatarCharacterId = null;
    }
}

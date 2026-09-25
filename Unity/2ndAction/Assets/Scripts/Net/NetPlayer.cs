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

    public int PlayerNumber => Slot.Value >= 0 ? Slot.Value + 1 : 0;

    const float SendRate = 30f;
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
        if (IsServer) Slot.Value = AssignSlot();
        if (IsOwner)
        {
            Local = this;
            RefreshOwnerState();
        }
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
        for (int slot = 0; slot < NetSession.MaxPlayers; slot++)
        {
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
    }

    void OwnerUpdate()
    {
        RefreshOwnerState();
        if (Phase.Value != PhaseInRun) { haveLastSample = false; return; }

        sendTimer += Time.unscaledDeltaTime;
        float interval = 1f / SendRate;
        if (sendTimer < interval) return;
        sendTimer = Mathf.Min(sendTimer - interval, interval);

        if (TrySampleLocalPlayer(out NetPlayerSnapshot snap)) SnapshotRpc(snap);
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
        return true;
    }

    // 位置/姿勢は毎秒30回・非信頼(UDP)で送る - 古い値の再送を待つより次の値を使う方が高速時に有利。
    [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
    void SnapshotRpc(NetPlayerSnapshot snapshot)
    {
        snapshotsReceived++;
        interpolator.Add(snapshot);
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

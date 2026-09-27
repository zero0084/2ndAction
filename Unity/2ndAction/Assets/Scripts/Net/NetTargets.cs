using System.Collections.Generic;
using UnityEngine;

// マルチプレイPhase 2.5(2026-09-27) - 敵/ボスの「狙う相手」を全プレイヤーから選ぶための共通窓口。
//
// ターゲット候補 = 現在「活動中」(Alive)のプレイヤー全員:
//  - この端末のプレイヤー(PlayerController.Instance): Run中・死亡/ダウン/脱落していない
//  - 相手のプレイヤー(HOSTから見たJOIN = RemotePlayerAvatar): Run中で、NetMatchの状態がAlive
// ダウン/脱落/切断した人は候補から外れる。シングルプレイでは常に「この端末のプレイヤー」だけ。
//
// 選び方は Strategy で差し替えられる(既定は最も近い有効プレイヤー)。距離がほぼ同じなら
// プレイヤー番号の小さい方(両端末で同じ結果)。HOSTだけがAIを動かすので、選んだ結果は
// NetCombatのスナップショット(State.Target)で参加側へも共有される。
public static class NetTargets
{
    public enum Strategy : byte { Nearest = 0, Foremost = 1, LastAttacker = 2, Random = 3, Fixed = 4 }

    public struct Candidate
    {
        public int Player;         // プレイヤー番号(1〜)
        public Transform T;        // 追跡に使うTransform(自分=PlayerController / 相手=分身)
        public float RunSpeed;     // スロー適用前の継続的な走行速度(ボスの並走などの基準)
        public bool IsLocal;
    }

    static readonly List<Candidate> buffer = new List<Candidate>(4);
    static int bufferFrame = -1;

    public static bool IsMulti => NetCombat.Authority || NetCombat.Replica;

    // このフレームの候補一覧(フレーム内はキャッシュ)。
    public static List<Candidate> Candidates()
    {
        if (bufferFrame == Time.frameCount) return buffer;
        bufferFrame = Time.frameCount;
        buffer.Clear();
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        int localPn = NetCombat.LocalPlayerNumber;
        if (pc != null && gm != null && LocalIsActive(pc, gm) && NetMatch.IsPlayerActive(localPn))
        {
            buffer.Add(new Candidate { Player = localPn, T = pc.transform, RunSpeed = pc.CurrentAutoRunSpeed, IsLocal = true });
        }
        if (NetCombat.Authority)
        {
            float now = Time.realtimeSinceStartup;
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p == null || p.IsOwner || !p.IsSpawned) continue;
                RemotePlayerAvatar a = p.Avatar;
                if (a == null) continue;
                int pn = p.PlayerNumber;
                if (pn <= 0 || !NetMatch.IsPlayerActive(pn)) continue;
                if (!p.IsRunningRemote(now)) continue;
                buffer.Add(new Candidate { Player = pn, T = a.transform, RunSpeed = p.RemoteRunSpeed, IsLocal = false });
            }
        }
        return buffer;
    }

    static bool LocalIsActive(PlayerController pc, GameManager gm)
    {
        if (!gm.HasStarted) return true; // Run開始前(タイトル等)は従来どおり
        if (gm.IsGameOver || pc.IsDeadPosing || pc.IsFinishing) return false;
        return true;
    }

    public static bool TryGet(int player, out Candidate c)
    {
        foreach (var x in Candidates()) if (x.Player == player) { c = x; return true; }
        c = default;
        return false;
    }

    // 位置fromに最も近い候補(ほぼ同距離ならプレイヤー番号の小さい方)。
    public static bool Nearest(Vector3 from, out Candidate best)
    {
        best = default;
        float bestD = float.MaxValue;
        bool found = false;
        foreach (var c in Candidates())
        {
            if (c.T == null) continue;
            float d = Vector2.Distance(from, c.T.position);
            if (!found || d < bestD - 0.01f || (Mathf.Abs(d - bestD) <= 0.01f && c.Player < best.Player))
            {
                best = c; bestD = d; found = true;
            }
        }
        return found;
    }

    // 危険物(落雷/突風/設置物など)が「流れる」基準の走行速度: 近くにいる活動中プレイヤーの速度。
    // 候補が無い時は従来どおりこの端末のプレイヤー。
    public static float FrameSpeedNear(Vector3 pos)
    {
        if (!IsMulti) return PlayerController.RunFrameSpeed;
        return Nearest(pos, out Candidate c) ? c.RunSpeed : PlayerController.RunFrameSpeed;
    }

    public static Transform TransformNear(Vector3 pos)
    {
        if (!IsMulti) return PlayerController.Instance != null ? PlayerController.Instance.transform : null;
        return Nearest(pos, out Candidate c) ? c.T : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
    }
}

// 敵/ボス1体ごとのターゲット選択(HOSTのAIだけが使う。参加側のパペットには付けない)。
// 一定間隔で候補を見直し、「今の相手より十分近い」相手が現れた時だけ、最低保持時間を過ぎていれば
// 切り替える(毎フレームP1/P2を行き来して震えないよう、ヒステリシス+保持時間)。
// 今の相手がダウン/脱落/切断で候補から消えたら即座に切り替える。
public class EnemyTargetSelector : MonoBehaviour
{
    public NetTargets.Strategy strategy = NetTargets.Strategy.Nearest;
    public float reevaluateInterval = 0.25f;
    public float switchMargin = 2.0f;      // 今の相手よりこれ以上近い時だけ乗り換える(world)
    public float minHoldTime = 0.8f;       // 乗り換え後、この秒数は同じ相手を狙い続ける
    public int fixedTarget;                // Strategy.Fixed用

    public int TargetPlayer { get; private set; }
    public int SwitchCount { get; private set; }
    public System.Action<NetTargets.Candidate> OnTargetChanged;

    float timer, holdTimer;
    int lastAttacker;

    public void NotifyAttackedBy(int player) { if (player > 0) lastAttacker = player; }

    void Start() { Evaluate(true); }

    void Update()
    {
        timer -= Time.deltaTime;
        holdTimer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = reevaluateInterval;
        Evaluate(false);
    }

    public void Evaluate(bool force)
    {
        var list = NetTargets.Candidates();
        if (list.Count == 0)
        {
            // 狙える相手がいない(全員DOWN/脱落など): 狙いを外す(AIの追跡先Transformはそのまま)。
            if (TargetPlayer != 0) { TargetPlayer = 0; OnTargetChanged?.Invoke(default); }
            return;
        }
        bool currentValid = NetTargets.TryGet(TargetPlayer, out NetTargets.Candidate cur);
        NetTargets.Candidate pick;
        switch (strategy)
        {
            case NetTargets.Strategy.Foremost:
                pick = list[0];
                foreach (var c in list) if (c.T.position.x > pick.T.position.x + 0.01f || (Mathf.Abs(c.T.position.x - pick.T.position.x) <= 0.01f && c.Player < pick.Player)) pick = c;
                break;
            case NetTargets.Strategy.LastAttacker:
                if (!NetTargets.TryGet(lastAttacker, out pick)) NetTargets.Nearest(transform.position, out pick);
                break;
            case NetTargets.Strategy.Random:
                pick = currentValid ? cur : list[Random.Range(0, list.Count)];
                break;
            case NetTargets.Strategy.Fixed:
                if (!NetTargets.TryGet(fixedTarget, out pick)) NetTargets.Nearest(transform.position, out pick);
                break;
            default:
                NetTargets.Nearest(transform.position, out pick);
                break;
        }
        if (pick.T == null) return;

        if (currentValid && pick.Player != TargetPlayer && !force)
        {
            if (holdTimer > 0f) return;
            if (strategy == NetTargets.Strategy.Nearest)
            {
                float dCur = Vector2.Distance(transform.position, cur.T.position);
                float dNew = Vector2.Distance(transform.position, pick.T.position);
                if (dNew > dCur - switchMargin) return; // 十分近くない = 今の相手のまま
            }
        }
        if (pick.Player == TargetPlayer && currentValid && !force) return;
        bool changed = pick.Player != TargetPlayer;
        TargetPlayer = pick.Player;
        if (changed) { SwitchCount++; holdTimer = minHoldTime; }
        OnTargetChanged?.Invoke(pick);
    }

    // 現在の相手の走行速度(ボスの並走に使う)。相手が候補に居なければ最も近い候補、それも無ければ0。
    public float TargetRunSpeed()
    {
        if (NetTargets.TryGet(TargetPlayer, out NetTargets.Candidate c)) return c.RunSpeed;
        return NetTargets.Nearest(transform.position, out c) ? c.RunSpeed : 0f;
    }
}

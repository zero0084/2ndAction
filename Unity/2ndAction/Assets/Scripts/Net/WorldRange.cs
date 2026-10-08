using System.Collections.Generic;
using UnityEngine;

// マルチ Phase 3.1(2026-10-02): 「世界をどこまで進めるか」の基準。HOSTの位置ではなく、走っているプレイヤー全体から決める。
//
//  Active(参加中) … このRunに参加していて、まだ終わっていない人(ALIVE + DOWN)。
//                   脱落(ELIMINATED)/退出(OUT)/切断/Run終了済み/シーン読み込み前(分身がまだ無い)は含まない。
//  Alive          … Activeのうち ALIVE の人(カード選択中の人も、その場に止まっているだけなので含む)。
//
//  WorldFront … Aliveの最前(シーンX/距離)。雑魚の出現・ボスの関門の基準。
//  WorldBack  … Aliveの最後尾。後ろに取り残された敵の片付けの基準(NetCombat.CleanupPassedEnemies)。
//
// 距離(Distance)は「HOSTの距離の物差し」で表す: この端末の距離 + (その人のX - 自分のX)。
// HOSTが出す敵/ボスの距離の帯・関門はすべてこの物差しなので、人によって距離の数え方が違っても(ボス戦中の除外など)ずれない。
// シングルプレイ/JOINでは、自分1人(JOINは相手の分身も見えるが、出現の判断はHOSTだけが行う)。
// 人数は任意(最大8人の予定)。P1/P2を名指しせず、この一覧から求める。
public static class WorldRange
{
    public struct PlayerPoint
    {
        public int Pn;               // プレイヤー番号(シングルは0)
        public bool IsLocal;
        public Transform T;
        public float SceneX;
        public float RunSpeed;       // 走行速度(m/s)。止まっている時(選択中/DOWN)は小さい
        public float Distance;       // HOSTの物差しでの距離
        public bool Alive, Choosing, Down;
    }

    static readonly List<PlayerPoint> active = new List<PlayerPoint>(8);
    static readonly List<PlayerPoint> alive = new List<PlayerPoint>(8);
    static int builtFrame = -1;
    static PlayerPoint front, back;
    static bool hasAlive;

    // 参加中(ALIVE + DOWN)
    public static List<PlayerPoint> GetActivePlayers() { Build(); return active; }
    // ALIVE(カード選択中を含む)
    public static List<PlayerPoint> GetAlivePlayers() { Build(); return alive; }

    public static bool HasAlive { get { Build(); return hasAlive; } }
    public static int ActiveCount { get { Build(); return active.Count; } }
    public static int AliveCount { get { Build(); return alive.Count; } }
    public static int ConnectedCount => NetSession.IsActive ? Mathf.Max(1, NetSession.ConnectedPlayerCount) : 1;

    // 最前/最後尾のALIVE(いない時=全員DOWN直後などは参加中の最前/最後尾、それも無ければ自分)
    public static PlayerPoint Front { get { Build(); return front; } }
    public static PlayerPoint Back { get { Build(); return back; } }
    public static float WorldFrontSceneX => Front.SceneX;
    public static float WorldBackSceneX => Back.SceneX;
    public static float WorldFrontDistance => Front.Distance;
    public static float WorldBackDistance => Back.Distance;
    public static int WorldFrontPlayer => Front.Pn;

    // 1フレームに1回だけ組み立てる(各所から何度呼んでも同じ結果)
    static void Build()
    {
        if (builtFrame == Time.frameCount) return;
        builtFrame = Time.frameCount;
        active.Clear(); alive.Clear();
        hasAlive = false;
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        float localX = pc != null ? pc.transform.position.x : 0f;
        float localDist = gm != null ? gm.MaxDistance : 0f;
        bool multi = NetMatch.Active;

        // 自分
        if (pc != null && gm != null && gm.HasStarted && !gm.IsGameOver && !pc.IsFinishing)
        {
            int pn = multi ? NetCombat.LocalPlayerNumber : 0;
            var rec = multi ? NetMatch.Get(pn) : null;
            bool isAlive = !multi ? !pc.IsDeadPosing : (rec == null || rec.State == NetMatch.PState.Alive) && !pc.NetIsDowned;
            bool isDown = multi && !isAlive && (pc.NetIsDowned && (rec == null || rec.State == NetMatch.PState.Down));
            if (isAlive || isDown)
                Add(new PlayerPoint { Pn = pn, IsLocal = true, T = pc.transform, SceneX = localX, RunSpeed = pc.CurrentAutoRunSpeed, Distance = localDist,
                    Alive = isAlive, Down = isDown, Choosing = isAlive && pc.NetIsChoosing });
        }

        // 相手(分身がある=同じRunのシーンにいて、位置が届いている人)
        if (multi)
        {
            foreach (NetPlayer p in NetPlayer.All)
            {
                if (p == null || p.IsOwner || p.PlayerNumber <= 0) continue;
                RemotePlayerAvatar a = p.Avatar;
                if (a == null || !a.HasRecentPosition) continue;
                var rec = NetMatch.Get(p.PlayerNumber);
                bool isAlive = rec == null || rec.State == NetMatch.PState.Alive;
                bool isDown = rec != null && rec.State == NetMatch.PState.Down;
                if (!isAlive && !isDown) continue; // 脱落/退出
                float x = a.transform.position.x;
                Add(new PlayerPoint { Pn = p.PlayerNumber, T = a.transform, SceneX = x, RunSpeed = p.RemoteRunSpeed,
                    Distance = pc != null ? localDist + (x - localX) : (float)p.RemoteDistance,
                    Alive = isAlive, Down = isDown, Choosing = isAlive && NetMatch.IsPlayerChoosing(p.PlayerNumber) });
            }
        }

        // 最前/最後尾(同じXなら番号の小さい方 = 全端末で同じ結果)
        List<PlayerPoint> src = alive.Count > 0 ? alive : active;
        hasAlive = alive.Count > 0;
        if (src.Count == 0)
        {
            front = back = new PlayerPoint { Pn = multi ? NetCombat.LocalPlayerNumber : 0, IsLocal = true, T = pc != null ? pc.transform : null, SceneX = localX,
                RunSpeed = pc != null ? pc.CurrentAutoRunSpeed : 0f, Distance = localDist };
            return;
        }
        front = back = src[0];
        foreach (var q in src)
        {
            if (q.SceneX > front.SceneX + 0.01f || (Mathf.Abs(q.SceneX - front.SceneX) <= 0.01f && q.Pn < front.Pn)) front = q;
            if (q.SceneX < back.SceneX - 0.01f || (Mathf.Abs(q.SceneX - back.SceneX) <= 0.01f && q.Pn < back.Pn)) back = q;
        }
    }

    static void Add(PlayerPoint q)
    {
        active.Add(q);
        if (q.Alive) alive.Add(q);
    }

    // 最前のALIVEのうち「今走っている」人(カード選択中を除く)。いなければ Front。ボスの登場位置などに使う。
    public static PlayerPoint FrontRunning()
    {
        Build();
        bool found = false; PlayerPoint best = front;
        foreach (var q in alive)
        {
            if (q.Choosing) continue;
            if (!found || q.SceneX > best.SceneX + 0.01f || (Mathf.Abs(q.SceneX - best.SceneX) <= 0.01f && q.Pn < best.Pn)) { best = q; found = true; }
        }
        return found ? best : front;
    }

    // 位置xが、参加中の誰かの画面に映っているか(各自のカメラはこの端末と同じ構図とみなす)。
    // マルチで「画面に見えている所でだけ予兆を始める」「誰にも見えていない時だけ位置を直す」に使う。
    public static bool VisibleToAnyone(float x, float margin = 0f)
    {
        Camera cam = Camera.main;
        PlayerController pc = PlayerController.Instance;
        if (cam == null || pc == null) return true;
        float half = GameView.HalfWidth(cam);
        float camOffset = cam.transform.position.x - pc.transform.position.x; // 自分のカメラ中心とプレイヤーの差(全員同じ構図)
        if (x < cam.transform.position.x + half - margin && x > cam.transform.position.x - half + margin) return true;
        if (!NetMatch.Active) return false;
        foreach (var q in GetActivePlayers())
        {
            if (q.IsLocal) continue;
            float cx = q.SceneX + camOffset;
            if (x < cx + half - margin && x > cx - half + margin) return true;
        }
        return false;
    }

    public static string Describe()
    {
        Build();
        var sb = new System.Text.StringBuilder();
        sb.Append($"front=P{front.Pn}@{front.Distance:F0}m back=P{back.Pn}@{back.Distance:F0}m alive={alive.Count}/{active.Count}");
        foreach (var q in active) sb.Append($" P{q.Pn}:{(q.Down ? "DOWN" : q.Choosing ? "choose" : "alive")}@{q.Distance:F0}");
        return sb.ToString();
    }
}

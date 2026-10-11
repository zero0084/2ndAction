using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// マルチ 安定化(2026-10-11): プレイヤーごとの同期範囲(Interest Range)。
// HOST が「どの JOIN へ、どの敵/攻撃の状態を、どの頻度で送るか」を決めるための共通部品。
// 敵の出現・HP・AI・死亡・LastHit の正解(HOST 権威)は変えない。送り先と頻度だけを変える。
//
//  ・各 JOIN のプレイヤー(参加中 = ALIVE/DOWN)の位置から、前方 Front / 後方 Back の範囲にある物を「近い」とする
//    (高速のゲームなので前方は広め: 130km/h ≒ 36m/s でも 3 秒以上先まで)。画面内かどうかでは決めない
//  ・近い物: 変化があれば毎回(20Hz)/ 変化が無くても 0.5 秒ごと
//  ・遠い物: 2 秒ごとの生存確認だけ(相手の画面では止まって見えるだけ。消す/死なせるのは HOST の正式な通知だけ)
//  ・範囲外でも近い扱い: ボス / その人を狙っている物 / 直前(3秒)までその人と戦っていた物
//  ・位置が分からない JOIN(読み込み中・脱落して観戦中など)は、活動中の最前の人の位置を使う。誰もいなければ全部「近い」
// LOCAL / ONLINE で同じ(接続の種類で分けない)。
public static class NetInterest
{
    public const float Front = 120f;          // 前方(m)
    public const float Back = 45f;            // 後方(m)
    public const float NearResend = 0.5f;     // 近い物: 変化が無くてもこの間隔で送る
    public const float FarKeepalive = 2.0f;   // 遠い物: この間隔でだけ送る
    public const float RecentFight = 3.0f;    // 直前まで戦っていた物は範囲外でも近い扱い
    public const int MaxPlayers = 8;

    public struct Viewer
    {
        public ulong ClientId;
        public int Pn;          // 1〜8
        public bool HasPos;
        public float SceneX;
    }

    static readonly List<Viewer> viewers = new List<Viewer>(MaxPlayers);
    static int builtFrame = -1;

    // HOST: 送り先(JOIN)の一覧。1フレームに1回だけ組み立てる
    public static List<Viewer> Viewers()
    {
        if (builtFrame == Time.frameCount) return viewers;
        builtFrame = Time.frameCount;
        viewers.Clear();
        var nm = NetSession.Manager;
        if (nm == null) return viewers;
        bool haveFront = WorldRange.ActiveCount > 0;
        float frontX = haveFront ? WorldRange.WorldFrontSceneX : 0f;
        foreach (ulong cid in nm.ConnectedClientsIds)
        {
            if (cid == NetworkManager.ServerClientId) continue;
            var v = new Viewer { ClientId = cid };
            foreach (var p in NetPlayer.All)
                if (p != null && p.OwnerClientId == cid) { v.Pn = p.PlayerNumber; break; }
            foreach (var q in WorldRange.GetActivePlayers())
                if (q.Pn == v.Pn && v.Pn > 0) { v.HasPos = true; v.SceneX = q.SceneX; break; }
            if (!v.HasPos && haveFront) { v.HasPos = true; v.SceneX = frontX; } // 観戦中/位置が古い: 最前の人の位置
            viewers.Add(v);
        }
        return viewers;
    }

    public static bool InRange(in Viewer v, float sceneX) => !v.HasPos || (sceneX > v.SceneX - Back && sceneX < v.SceneX + Front);

    // 送る判断: 近い物は変化で毎回/0.5秒、遠い物は2秒ごと
    public static bool ShouldSend(bool near, bool changed, float since) => near ? (changed || since >= NearResend) : since >= FarKeepalive;

    // 自動テスト/報告用の数(直近1秒)
    public static int SentNear, SentFar, Skipped;
}

using System.Collections.Generic;
using UnityEngine;

// 2026-11-28 マスター依頼②: マルチ中に、自分の順位と先頭との差(距離)を出す。表示だけ(勝敗・報酬・距離の計算は変えない)。
//  ・使う距離はどちらのモードもコース上の位置(自分 = PlayerController.DistanceExact、ほかの人 = 位置の同期の NetPlayer.RemoteDistance。
//    VERSUS の順位の判定(BuildResults)が使う rec.Distance と同じ物差し)。
//  ・VERSUS: 「2位 / 4人　先頭まで 125m」。順位は BuildResults と同じ並べ方(脱落/脱出 = 確定した距離、DOWN = 倒れた距離、走っている人 = 今の位置、
//    同じ距離は番号の小さい方が上)。退出(切断)した人は数えない。「先頭」は走っている人(生存)の一番前 = 走り終えた人とは比べない。
//  ・CO-OP: 「走行位置 2 / 4　先頭まで 125m」。チーム距離では並びが分からないので、コース上の位置で並べる。
//    並べるのはコースに居る人(生存・DOWN)。脱出・脱落・退出した人は外す。先頭 = 生存の一番前。
//  ・自分が先頭なら「先頭」。自分の走行が終わった(脱出・脱落)後は、順位(VERSUS)だけ・CO-OP は出さない。
public partial class NetMatch
{
    public const string RankVersusKey = "{0}位 / {1}人";      // ID 11119
    public const string RankCoopKey = "走行位置 {0} / {1}";   // ID 11120
    public const string RankGapKey = "先頭まで {0}m";         // ID 11121
    public const string RankLeaderKey = "先頭";               // ID 11122

    public struct RankInfo
    {
        public bool Valid, Versus, Running, Leader, HasFront;
        public int Pos, N;
        public double Gap;
        public string Text;
    }
    public static string LastRankText { get; private set; } = ""; // 確認用(自動試験の記録)

    static readonly List<KeyValuePair<int, double>> rankTmp = new List<KeyValuePair<int, double>>();

    static double LivePos(Rec r, int local)
    {
        if (r.Pn == local) { var pc = PlayerController.Instance; return pc != null ? pc.DistanceExact : r.Distance; }
        foreach (NetPlayer p in NetPlayer.All) if (p != null && !p.IsOwner && p.PlayerNumber == r.Pn) return p.RemoteDistance;
        return r.Distance;
    }

    public static RankInfo ComputeRank()
    {
        var ri = new RankInfo();
        var m = Instance;
        if (m == null || !Active || !NetRunLauncher.IsMultiplayerRun || m.runOver) return ri;
        int local = NetCombat.LocalPlayerNumber;
        Rec me = Get(local);
        if (me == null || me.State == PState.Out) return ri;
        // ラスダンのクリアの後(エンドロール/問いかけ)は各自の道なので出さない。YES の後(BEYOND)は出す
        if (m.ldClearApplied && (LastDungeonFlow.Instance == null || LastDungeonFlow.Instance.Current != LastDungeonFlow.State.Beyond)) return ri;
        bool vs = Mode == MultiplayerGameMode.Versus;
        if (!vs && me.State != PState.Alive && me.State != PState.Down) return ri; // CO-OP: 自分の走行が終わった
        rankTmp.Clear();
        double frontV = double.MinValue; int frontPn = 0; double myV = 0;
        foreach (var r in m.recs.Values)
        {
            if (r.State == PState.Out) continue;
            if (!vs && r.State != PState.Alive && r.State != PState.Down) continue;
            // 走り終えた人(脱落/脱出)は確定した距離。DOWN の人はその場に止まっているので今の位置(= 倒れた位置。表の DownDistance より新しい)
            double v = r.State == PState.Eliminated || r.State == PState.Escaped ? r.FinalDistance : LivePos(r, local);
            rankTmp.Add(new KeyValuePair<int, double>(r.Pn, v));
            if (r.Pn == local) myV = v;
            if (r.State == PState.Alive && (v > frontV || (v == frontV && r.Pn < frontPn))) { frontV = v; frontPn = r.Pn; }
        }
        rankTmp.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
        for (int i = 0; i < rankTmp.Count; i++) if (rankTmp[i].Key == local) ri.Pos = i + 1;
        ri.N = rankTmp.Count;
        if (ri.Pos <= 0) return ri;
        ri.Valid = true; ri.Versus = vs;
        ri.Running = me.State == PState.Alive || me.State == PState.Down;
        ri.HasFront = frontPn != 0;
        ri.Leader = frontPn == local;
        ri.Gap = ri.HasFront && !ri.Leader ? System.Math.Max(0.0, frontV - myV) : 0.0;
        string head = vs ? (Loc.Ready(RankVersusKey) ? Loc.F(RankVersusKey, ri.Pos, ri.N) : $"#{ri.Pos} / {ri.N}")
                         : (Loc.Ready(RankCoopKey) ? Loc.F(RankCoopKey, ri.Pos, ri.N) : $"POS {ri.Pos} / {ri.N}");
        string tail = "";
        if (me.State == PState.Alive && ri.HasFront) // DOWN の間は差を出さない(倒れて止まっているので「先頭まで」は意味が無い)
        {
            if (ri.Leader) tail = Loc.Ready(RankLeaderKey) ? Loc.T(RankLeaderKey) : "LEAD";
            else
            {
                string g = System.Math.Floor(ri.Gap).ToString("N0");
                tail = Loc.Ready(RankGapKey) ? Loc.F(RankGapKey, g) : $"LEAD +{g}m";
            }
        }
        ri.Text = tail.Length > 0 ? head + "   " + tail : head;
        LastRankText = ri.Text;
        return ri;
    }
}

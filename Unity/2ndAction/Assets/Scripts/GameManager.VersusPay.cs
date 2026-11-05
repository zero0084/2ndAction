using UnityEngine;

// VERSUS の MILE ボーナス(2026-11-05、マスターの決定「試合完了で持ち帰る」)。
//  ・試合が完了した時(HOST が全員の順位を決めて配った時)だけ: 脱落までに稼いだ MILE(このランの MILE)+ 順位ボーナス + REAPER BONUS を持ち帰る
//  ・試合の途中の切断/退出は今までどおり失う(この精算が届かない = NetSetVersusPay が呼ばれない)
//  ・割合は HOST が決めて結果の行で配る(NetMatch.ComputeVersusPay / MultiplayerRules)。この端末は自分の行を受け取って足すだけ
//  ・1 回だけ(FinishRunCore は 1 回 + versusPaid)。新しい通貨や VERSUS 専用のカードは作らない
public partial class GameManager
{
    public bool VersusPaySet { get; private set; }
    public int VersusRank { get; private set; }
    public int VersusPlayers { get; private set; }
    public float VersusRankBonusRate { get; private set; }
    public float VersusReaperBonusRate { get; private set; }
    public int VersusBaseMile { get; private set; }     // 脱落までに稼いだ MILE(通常の MILE)
    public int VersusRankMile { get; private set; }     // 順位ボーナスの MILE
    public int VersusReaperMile { get; private set; }   // REAPER BONUS の MILE
    public int VersusTotalMile => VersusBaseMile + VersusRankMile + VersusReaperMile;
    bool versusPaid;

    // NetMatch(試合の完了の時、NetForceGameOver の直前に 1 回)
    public void NetSetVersusPay(int rank, int players, float rankBonus, float reaperBonus)
    {
        if (IsGameOver || versusPaid) return;
        VersusPaySet = true;
        VersusRank = rank; VersusPlayers = players;
        VersusRankBonusRate = Mathf.Max(0f, rankBonus); VersusReaperBonusRate = Mathf.Max(0f, reaperBonus);
    }

    // FinishRunCore(負けの経路)から。RunDistanceMile を決めた後に呼ぶ
    void GrantVersusPayIfAny()
    {
        if (!VersusPaySet || versusPaid || IsWin) return;
        if (NetRunLauncher.ActiveMode != MultiplayerGameMode.Versus) return;
        versusPaid = true;
        VersusBaseMile = Mathf.Max(0, RunMile);
        VersusRankMile = Mathf.RoundToInt(VersusBaseMile * VersusRankBonusRate);
        VersusReaperMile = Mathf.RoundToInt(VersusBaseMile * VersusReaperBonusRate);
        int before = TotalOwnedMile;
        AddMile(VersusTotalMile);
        Debug.Log($"[VersusPay] rank={VersusRank}/{VersusPlayers} base={VersusBaseMile} rank+{VersusRankBonusRate:P0}={VersusRankMile} reaper+{VersusReaperBonusRate:P0}={VersusReaperMile} total={VersusTotalMile} wallet {before}->{TotalOwnedMile}");
    }
}

using UnityEngine;

// 2026-11-28 マスター依頼③: マルチ(CO-OP / VERSUS)で、死神に倒された場合は復活できない。
//  対象: 荒野街道・自然洞窟・天空回廊(ラスダンの死神三姉妹は対象外 = 今までどおり。ソロは変えない)。
//  ・「死神の攻撃で HP が 0 になった」時だけ。HP が残る被弾は今までどおり。通常の敵・落下で倒れた時も今までどおり。
//  ・死神の攻撃かどうかは、攻撃の持ち主(ReaperBase)で決める(名前の文字や JOIN の申告の文字では決めない)。
//     HOST 自身: BossHitbox が当たった時に LocalHitFromReaper を立てる(GameManager.TryDamagePlayer が読む)。
//     JOIN: HOST が申告の攻撃の番号から本物の攻撃を引き、その持ち主で決める(NetAttackSync.HostIsReaperAttack / 死神の体の接触)。
//  ・CO-OP は DOWN にせず脱落(Eliminated)= 仲間の長押しの救助の対象にならない。VERSUS はもともと脱落(復活なし)。
//  ・HOST の PHOENIX / FINAL EVOLUTION(PHOENIX)の緊急復活も使わない(JOIN の HP は HOST が決めるので、JOIN の端末で復活は起きない)。
//  ・判定は HOST だけ(状態の表で全員へ)。通信の遅れで後から復活の知らせが来ても、HOST が脱落を確定した人は復活しない(CanRevive は DOWN だけ)。
public partial class NetMatch
{
    // HOST: 今の被弾が死神の攻撃から来た(BossHitbox が TakeDamage の間だけ立てる)
    public static bool LocalHitFromReaper;

    public static int ReaperEliminations { get; private set; } // 確認用(CO-OP で死神により脱落した人数。VERSUS は数えない)

    static bool IsReaperNoReviveStage(string stageId) => stageId == "wasteland_road" || stageId == "natural_cave" || stageId == "sky_corridor";

    // この規則が効くラン(マルチ・通常の3マップ)
    public static bool ReaperNoReviveApplies
    {
        get
        {
            if (!Active || !NetRunLauncher.IsMultiplayerRun) return false;
            var gm = GameManager.Instance;
            return gm != null && IsReaperNoReviveStage(gm.ActiveRunStageId);
        }
    }
}

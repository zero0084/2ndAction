#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 補助試験(2026-11-28、マスター依頼 ③): 退出と撃破が重なる場合。通常プレイの通し試験とは別に記録する(強制撃破を使う)。
//  -netAutoShareOverlap 1(HOST): 参加者の退出/切断を HOST が確定する ① 直前 ② 同じフレーム(確定の後)③ 次のフレーム に、
//  残っている参加者の攻撃として雑魚を1体ずつ倒す(参加者の命中と同じ道 = EnemyController.NetApplyRemoteHit → 撃破の確定 → 撃破の知らせ)。
//  確かめること: HOST と各端末の「その撃破の分配の人数」が一致する / 残っている端末が1体につき1回だけ受け取る(重複/取りこぼし無し)
public partial class NetAutoTest
{
    bool overlapTest, overlapPostPending; int overlapLeaver;

    void OverlapSetup()
    {
        if (!overlapTest || role != "HOST") return;
        NetMatch.QaOutHook = (rec, before) =>
        {
            overlapLeaver = rec.Pn;
            OverlapKill(before ? "pre" : "same", rec.Pn);
            if (!before) overlapPostPending = true;
        };
        L("OVERLAP test armed (HOST kills one enemy right before / in the same frame / one frame after each leave is confirmed)");
    }

    void OverlapTick()
    {
        if (!overlapPostPending) return;
        overlapPostPending = false;
        OverlapKill("post", overlapLeaver);
    }

    void OverlapKill(string when, int leaver)
    {
        // 残っている(生きている)参加者の番号 = 攻撃した人
        int attacker = 0;
        foreach (var r in NetMatch.Instance != null ? NetMatch.Instance.Records.Values : new System.Collections.Generic.List<NetMatch.Rec>())
            if (r.Pn != leaver && r.Pn != NetCombat.LocalPlayerNumber && r.State == NetMatch.PState.Alive) { attacker = r.Pn; break; }
        if (attacker == 0) attacker = NetCombat.LocalPlayerNumber;
        EnemyController target = null; float best = float.MaxValue;
        var pc = PlayerController.Instance;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (e == null || !e.isActiveAndEnabled || e.IsDying || e.NetId == 0) continue;
            float d = pc != null ? Mathf.Abs(e.transform.position.x - pc.transform.position.x) : 0f;
            if (d < best) { best = d; target = e; }
        }
        int share = GameManager.Instance != null ? GameManager.Instance.MileShareCount : -1;
        if (target == null) { L($"OVERLAP {when}: no enemy to kill (leaver P{leaver}, HOST share 1/{share})"); return; }
        L($"OVERLAP {when}: kill id={target.NetId} by P{attacker} (leaver P{leaver}, HOST share 1/{share})");
        target.NetApplyRemoteHit(attacker, 999999, PlayerAttackKind.Normal, target.transform.position);
    }
}
#endif

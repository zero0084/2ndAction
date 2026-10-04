using UnityEngine;

// 自然洞窟ボス強化(2026-10-04): BossManager 側の追加。
//  ・洞窟ボスが生きている間は、CaveStage のボス区間(通常の天井の高さ・天井の針なし)をプレイヤーに追従させる。
//    ラン再開後もボスは追ってくるので、関門の位置に固定された40mの区間だけでは、すぐに狭い天井/針の区間へ入ってしまうため。
//    シングルのみ(マルチは地形を両端末で同じに作る仕組みを崩さないよう触らない。マルチの安全は CaveBossSafety の穴/天井の確認で守る)。
//  ・開発用: 洞窟ボスを「関門と同じ流れ」(BGM/ラン再開の時計/ボス区間/再戦の段階)で出す。
public partial class BossManager
{
    float caveZoneNextUpdate;

    void CaveBossTick()
    {
        if (!IsCaveStage || NetRunLauncher.IsMultiplayerRun || !IsBossPhase || AliveWildCount <= 0 || player == null) return;
        if (Time.time < caveZoneNextUpdate) return;
        caveZoneNextUpdate = Time.time + 0.25f;
        var tm = TerrainManager.Instance;
        if (tm != null && tm.cave != null) tm.cave.SetBossClearZone(player.position.x + 25f, 50f);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 洞窟ボスを関門と同じ流れで出す。tier: -1=初登場 / 0〜=再戦の段階(BossRematchTuning.tiers)
    public void DebugCaveEncounter(CaveBossKind kind, int tier, int count = 1)
    {
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        IsBossPhase = true; BossDefeatedThisPhase = false;
        if (GameManager.Instance != null) GameManager.Instance.BeginBossDistanceExclusion();
        ClearEnemiesForBoss();
        currentGateK = Mathf.Max(0, gateK - 1); // 撃破後に次の関門を飛ばさない
        ResetRematchEncounter();
        CurrentEncounterKey = Key(GateFamily.Cave, (int)kind);
        var tn = BossRematchTuning.I;
        if (tier >= 0 && tier < tn.tiers.Count)
        {
            currentTier = tn.tiers[tier];
            float d = Mathf.Max(currentTier.fromMeters, GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f);
            float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(GateFamily.Cave, (int)kind)))) : 1f;
            CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul);
            CurrentRematchTier = currentTier.label;
            DamageMul = Mathf.Max(0.1f, currentTier.damageMul);
            CurrentEncounterIsRematch = true;
        }
        StartCaveGate(kind, count);
        // ラン再開までの秒数は本来の関門の種類で(1,000m系/5,000m系/10,000m専用)
        var e = BossBattleTuning.I.For(kind.ToString());
        var bt = BossBattleTuning.I;
        resumeSeconds = e.resumeSecondsOverride > 0f ? e.resumeSecondsOverride : kind == CaveBossKind.Centipede ? bt.resumeNormal : kind == CaveBossKind.Scorpion ? bt.resumeStrong : bt.resumeSpecial;
        Debug.Log($"[Boss] DebugCaveEncounter {kind} x{count} tier={(tier >= 0 ? CurrentRematchTier : "初登場")} resume={resumeSeconds:F0}s");
    }

    public void DebugForceResume() { if (IsBossPhase && AliveBossCount > 0) { encounterResumable = true; ResumeRun(); } }
#endif
}

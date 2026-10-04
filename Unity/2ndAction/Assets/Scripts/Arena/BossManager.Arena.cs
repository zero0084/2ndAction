#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 開発用の闘技場(2026-10-04): ボスを数を指定して出す / すべて片付ける。行動/予兆/攻撃/必殺技/被弾は通常のボスのまま
public partial class BossManager
{
    // family 1 = 荒野街道 / 2 = 自然洞窟 / 3 = 天空回廊
    public void ArenaSpawnBoss(int family, int kind, int count)
    {
        count = Mathf.Clamp(count, 1, 4);
        if (family == 2) DebugForceSpawnCave((CaveBossKind)kind, count);
        else if (family == 3) DebugForceSpawnSky((SkyBossKind)kind, count);
        else DebugForceSpawn((WildBossKind)kind, count);
    }

    public void ArenaClearBosses()
    {
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        foreach (var wb in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (wb != null) Destroy(wb.gameObject);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        if (IsBossPhase) EndBossPhase();
        BossBattle.Living.Clear();
    }
}
#endif

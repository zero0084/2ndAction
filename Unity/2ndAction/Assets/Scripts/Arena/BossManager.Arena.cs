using UnityEngine;

// 開発用の闘技場(2026-10-04): ボスを数を指定して出す / すべて片付ける。行動/予兆/攻撃/必殺技/被弾は通常のボスのまま
public partial class BossManager
{
    // family 1 = 荒野街道 / 2 = 自然洞窟 / 3 = 天空回廊
    // 2026-10-06: 正式な闘技場(リリース版)でも使う。開発用の DebugForceSpawn* と同じ出し方(関門/報酬/遭遇の記録は通らない)
    public void ArenaSpawnBoss(int family, int kind, int count)
    {
        count = Mathf.Clamp(count, 1, 4);
        IsBossPhase = true;
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        if (family == 2)
        {
            if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null) TerrainManager.Instance.cave.SetBossClearZone(player.position.x, 40f);
            aliveWildThisEncounter = count;
            for (int i = 0; i < count; i++) SpawnCaveBoss((CaveBossKind)kind, i);
        }
        else if (family == 3)
        {
            if (GameManager.Instance != null) GameManager.Instance.BeginBossDistanceExclusion();
            StartSkyEncounter((SkyBossKind)kind, count);
        }
        else
        {
            var wk = (WildBossKind)kind;
            if (wk == WildBossKind.Dragon) { aliveDragonsThisEncounter = 1; SpawnWastelandDragon(dragonStandoffDistance); }
            else { aliveWildThisEncounter = count; for (int i = 0; i < count; i++) SpawnWild(wk, i); }
            BeginEncounterClock(wk.ToString());
        }
        Debug.Log($"[Arena] boss spawn family={family} kind={kind} x{count}");
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

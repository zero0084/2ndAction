using System.Collections.Generic;
using UnityEngine;

// カードバランス v3(2026-10-03): Challenge / Risk カードの「本当に難しくなる」部分。
//  ・FAST ENEMIES / HELL MODE / PANDEMONIUM: 雑魚の行動(移動/追尾/予備動作/攻撃の間隔)が速くなる(出現数ではない)。
//    EnemySpecialBehavior の行動の時間(EDt)に掛ける。ボスには掛けない。
//  ・ELITE ENEMIES / PANDEMONIUM: 出てくる雑魚の一部が精鋭(HP×2.5・少し大きい・行動×1.15・MILE×3/EXP×2)。
//  ・WANTED: 一定距離ごとに賞金首(精鋭化した敵、HP は Lv で増える)が前に現れる。倒せば賞金の MILE、通り過ぎれば逃げ切り。
//  ・出現の安全: 生きている雑魚が多すぎる時は Encounter を後回しにする(CardRules.MaxLivingEnemiesForSpawn、Android の負荷)。
// マルチ: 敵を出す/決めるのは HOST(敵の出現と同じ)。値は HOST のカード。
public static class ChallengeSystem
{
    public static int EliteSpawned, WantedSpawned, WantedKilled, WantedEscaped, SpawnsHeldByCap;
    public static void ResetCounters() { EliteSpawned = WantedSpawned = WantedKilled = WantedEscaped = SpawnsHeldByCap = 0; }

    static GameManager GM => GameManager.Instance;
    static float C(EffectType t) => GM != null ? GM.Card.Get(t) : 0f;

    public const float EliteHpMul = 2.5f, EliteScale = 1.15f, EliteActionMul = 1.15f;
    public const int EliteMileMul = 3;
    public const float EliteExpMul = 2f;

    // 雑魚の行動の速さ(1 = 通常、上限 1.6)
    public static float EnemyActionScale => 1f + Mathf.Clamp(C(EffectType.EnemyActionPct), 0f, 0.6f);
    public static float EliteChance => Mathf.Clamp(C(EffectType.EliteChance), 0f, 0.4f);

    // 生きている雑魚の数(EnemyController の OnEnable/OnDisable で数える)
    public static int LivingEnemies => EnemyController.ActiveCount;
    public static bool SpawnHeldByCap()
    {
        if (LivingEnemies < CardRules.MaxLivingEnemiesForSpawn) return false;
        SpawnsHeldByCap++;
        return true;
    }

    // TerrainManager.SpawnEncounterEnemy から(BONUS ZONE の報酬の敵は GroundFactory 側で別に付くので対象外)
    public static void OnEncounterEnemySpawned(EnemyController e)
    {
        if (e == null || e.bonus != null) return;
        if (NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority) return;
        float ch = EliteChance;
        if (ch > 0f && Random.value < ch) MakeElite(e, EliteHpMul);
    }

    public static void MakeElite(EnemyController e, float hpMul, bool wanted = false)
    {
        if (e == null || e.IsElite) return;
        e.IsElite = true;
        e.maxHp = Mathf.Max(1, Mathf.RoundToInt(e.maxHp * hpMul));
        e.ResetHpToMax();
        e.mileReward = Mathf.Max(1, e.mileReward * EliteMileMul);
        e.KillExpMultiplier = EliteExpMul;
        e.transform.localScale *= EliteScale;
        foreach (var sr in e.GetComponentsInChildren<SpriteRenderer>())
            if (sr != null && sr.sortingOrder > -50) sr.color = wanted ? new Color(1f, 0.55f, 0.55f, sr.color.a) : new Color(1f, 0.85f, 0.55f, sr.color.a);
        EliteSpawned++;
    }

    // ===================================================================== WANTED

    static float nextWantedDistance = -1f;
    static readonly List<EnemyController> wanted = new List<EnemyController>();
    public static void ResetRun() { nextWantedDistance = -1f; wanted.Clear(); }

    public static float WantedIntervalMeters(int lv) => Mathf.Max(1500f, 3000f - 150f * (lv - 1));

    // GameManager.Update(ラン中)から
    public static void Tick()
    {
        var gm = GM;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        int lv = Mathf.RoundToInt(C(EffectType.WantedLevel));
        // 逃げ切り/撃破の判定
        for (int i = wanted.Count - 1; i >= 0; i--)
        {
            var w = wanted[i];
            if (w == null || w.IsDying) { if (w != null && w.IsDying) { WantedKilled++; gm.AddRunBonusMile(20 + 10 * Mathf.Max(1, lv)); BossBattleHud.Banner("BOUNTY!", new Color(1f, 0.85f, 0.3f), 1.2f); } wanted.RemoveAt(i); continue; }
            var pc = PlayerController.Instance;
            if (pc != null && w.transform.position.x < pc.transform.position.x - 18f) { WantedEscaped++; wanted.RemoveAt(i); }
        }
        if (lv <= 0) return;
        if (NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority) return;
        if (BossManager.Instance != null && BossManager.Instance.HoldsRun) return;
        float d = gm.MaxDistance;
        if (nextWantedDistance < 0f) nextWantedDistance = d + WantedIntervalMeters(lv) * 0.5f;
        if (d < nextWantedDistance) return;
        nextWantedDistance = d + WantedIntervalMeters(lv);
        SpawnWanted(lv);
    }

    static void SpawnWanted(int lv)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        if (tm == null || pc == null) return;
        EnemyDefinition def = PickStageEnemy();
        if (def == null) return;
        float x = pc.transform.position.x + 22f;
        for (int k = 0; k < 30 && !tm.GetHeightAt(x).HasValue; k++) x += 1f;
        float? g = tm.GetHeightAt(x);
        if (!g.HasValue) return;
        bool flying = def.movementType == EnemyMovementType.Flying;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, g.Value + (flying ? 2.2f : 0f)), EnemyAiTier.T2, def.behaviorKind);
        if (go == null) return;
        var e = go.GetComponent<EnemyController>();
        if (e == null) return;
        MakeElite(e, 3f + 0.3f * lv, true);
        wanted.Add(e);
        WantedSpawned++;
        BossBattleHud.Banner("WANTED! 賞金首が現れた", new Color(1f, 0.45f, 0.4f), 1.4f);
    }

    static EnemyDefinition PickStageEnemy()
    {
        var list = new List<EnemyDefinition>();
        var ed = EncounterDirector.Instance;
        if (ed != null && ed.isActiveAndEnabled)
            foreach (var id in ed.StageEnemyIds()) { var d = EnemyDatabase.FindById(id); if (d != null && d.sprite != null) list.Add(d); }
        if (list.Count == 0)
        {
            var gm = GM;
            string stage = gm != null ? gm.ActiveRunStageId : "";
            foreach (var d in EnemyDatabase.AllEnemies)
                if (d != null && d.sprite != null && d.stageIds != null && System.Array.IndexOf(d.stageIds, stage) >= 0) list.Add(d);
        }
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : EnemyDatabase.FindById("goblin");
    }
}

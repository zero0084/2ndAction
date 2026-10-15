using System.Collections.Generic;
using UnityEngine;

// 各マップの終盤のボスラッシュ(2026-10-15、マスターの依頼「案A」)。荒野街道 / 自然洞窟 / 天空回廊(ソロ・CPU サポート)。
//  ・90〜98km に、そのマップの大ボス9体を弱い順に1体ずつ。最後(98km)はそのマップで一番強いボス。
//    荒野の 80km の竜は別の仕組み(DragonController)なので入れず、90km をゴブリンライダーにする。
//  ・同時に出るのは1体だけ(ラスダンのラッシュは最大3体 + タグの規則。こちらは使わない)。
//    1km ごとの節目で次のボスを予約し、前のボスを倒した後に予告 → 出現。速く走って節目をまとめて越えても順番に全部予約する。
//  ・距離は止めない(走りながら戦う)。99km でボスが残っていれば(戦っている/待っている)、全員倒すまで 99,000m で足止め。
//  ・全員倒すと報酬の選択(ラッシュ全体で1回)→ 足止めの解除 → 100km で死神の追跡(今の流れ)。次の関門は 101km。
//  ・90〜99km のふだんの関門は出さない(ラッシュが関門の代わり)。雑魚はラッシュの間は出さない。
//  ・強さ: そのボスの節目の距離での再戦の強さ(BossRematchTuning: 距離の HP の倍率 × 段階の HP / 被弾量)× MapRushHpMul。
//  ・報酬: 各ボスの MILE は種類と強さ、EXP は大ボスの格(80%)。ラッシュを終えると 90km の門番を倒した扱い(疾走出発の 90km の解放)。
//  ・中断/再開: 再開はチェックポイント(ラッシュの前の最後のボスの報酬)から = ラッシュは最初からやり直し(予約は作り直す)。
//  ・ラスダンのラッシュは変えていない(LastDungeonBossTuning のまま)。マルチは今までどおりの関門(このラッシュは無し)。
public partial class BossManager
{
    public static bool MapRushEnabled = true;
    public static float MapRushHpMul = 0.6f; // ラスダンの走りながらのラッシュと同じ値から(通常の育成の構成で測って決める)
    static readonly string[] WastelandRush = { "Wild/GoblinRider", "Wild/Serpent", "Wild/Cyclops", "Wild/Spider", "Wild/Golem", "Wild/Griffin", "Wild/Hydra", "Wild/Demon", "Wild/BlackKnight" };
    static readonly string[] CaveRush = { "Cave/Mole", "Cave/Troll", "Cave/Worm", "Cave/CrystalGolem", "Cave/Bat", "Cave/ScorpionKing", "Cave/Basilisk", "Cave/Drake", "Cave/AncientDemon" };
    static readonly string[] SkyRush = { "Sky/Behemoth", "Sky/Titan", "Sky/Jellyfish", "Sky/Leviathan", "Sky/Fenrir", "Sky/SkyGolem", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Guardian" };

    public bool MapRushActive => CurrentBossEncounter != null && CurrentBossEncounter.continuous && CurrentBossEncounter.mapRush;
    public int MapRushesStarted { get; private set; }
    bool MapRushStage => MapRushEnabled && RematchStage && !NetRunLauncher.IsMultiplayerRun && !ArenaMode.Active;

    static string[] MapRushPlanFor(string stageId) =>
        stageId == "natural_cave" ? CaveRush : stageId == "sky_corridor" ? SkyRush : WastelandRush;
    public static IReadOnlyList<string> MapRushPlan(string stageId) => MapRushPlanFor(stageId);

    // その節目の距離での再戦の強さ(距離の HP の倍率 × 段階)× MapRushHpMul。被弾量/段階も再戦と同じ
    void SetMapRushStrength(string key, float d)
    {
        if (!ParseKey(key, out GateFamily f, out int kind)) return;
        var tn = BossRematchTuning.I;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(f, kind)))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul) * Mathf.Max(0.05f, MapRushHpMul);
        CurrentRematchTier = currentTier.label;
        DamageMul = Mathf.Max(0.1f, currentTier.damageMul);
        CurrentEncounterIsRematch = true;
        Debug.Log($"[MapRush] strength {key} at {d:F0}m: HP x{CurrentRematchHpMul:0.00} (distance x{distMul:0.00} * {currentTier.label} x{currentTier.hpMul:0.00} * rush x{MapRushHpMul:0.00}), damage x{DamageMul:0.0}");
    }

    // ラッシュを終えた(全員倒した): 90km の門番を倒した扱い(疾走出発の 90km の解放)
    void OnMapRushCleared()
    {
        if (GameManager.Instance != null && !DebugRun.WritesBlocked)
            SprintRecords.MarkGateCleared(GameManager.Instance.ActiveRunStageId, RushFirstK);
        Debug.Log($"[MapRush] cleared at {(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0}m - next gate 101km, the reaper chase at 100km");
    }
}

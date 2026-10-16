using System.Collections.Generic;
using UnityEngine;

// 各マップの終盤のボスラッシュ(2026-10-15、マスターの依頼「案A」)。荒野街道 / 自然洞窟 / 天空回廊(ソロ・CPU サポート・第4弾から協力マルチ)。
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
//  ・ラスダンのラッシュは変えていない(LastDungeonBossTuning のまま)。協力マルチは HOST が管理(第4弾)、対戦は今までどおりの関門。
public partial class BossManager
{
    public static bool MapRushEnabled = true;
    public static float MapRushHpMul = 0.6f; // ラスダンの走りながらのラッシュと同じ値から(通常の育成の構成で測って決める)
    static readonly string[] WastelandRush = { "Wild/GoblinRider", "Wild/Serpent", "Wild/Cyclops", "Wild/Spider", "Wild/Golem", "Wild/Griffin", "Wild/Hydra", "Wild/Demon", "Wild/BlackKnight" };
    static readonly string[] CaveRush = { "Cave/Mole", "Cave/Troll", "Cave/Worm", "Cave/CrystalGolem", "Cave/Bat", "Cave/ScorpionKing", "Cave/Basilisk", "Cave/Drake", "Cave/AncientDemon" };
    static readonly string[] SkyRush = { "Sky/Behemoth", "Sky/Titan", "Sky/Jellyfish", "Sky/Leviathan", "Sky/Fenrir", "Sky/SkyGolem", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Guardian" };

    public bool MapRushActive => CurrentBossEncounter != null && CurrentBossEncounter.continuous && CurrentBossEncounter.mapRush;
    public int MapRushesStarted { get; private set; }
    // 2026-10-15 第4弾: 協力マルチも対象(HOST が出現/撃破/待ち列/完了を決め、全員が同じボスを共有。JOIN はラッシュの処理をしない)。対戦は対象外
    bool MapRushStage => MapRushEnabled && RematchStage && !ArenaMode.Active &&
        (!NetRunLauncher.IsMultiplayerRun || (NetMatch.Mode == MultiplayerGameMode.Coop && NetCombat.Authority));

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

    // ---- ボスとボスの間のレベルアップの選択(2026-10-15 第4弾、マスターの依頼) ----
    // 以前はラッシュの間(ボス戦の間)のレベルアップの選択がすべて後回しで、ラッシュの後に 20回ほど続けて出ていた。
    // 今: ボスを倒して次のボスがまだ出ていない間(予告もしていない)に、その時点の未処理の選択を順に全部出し、次のボスに強化を反映する。
    //  ・ラッシュ全体のボスの報酬(1回)は今までどおり最後(BossDefeatedThisPhase の後は従来の流れ)
    //  ・選択が残っている間 / 選択中 / 閉じた直後(MapRushChoiceGrace 秒)は、次のボスの予告を始めない
    //  ・ソロ/CPU サポート: 選択中は今までどおり世界ごと止まる(TimeControl)。選択を閉じた時、押したままの指は離すまで無視する
    //  ・協力マルチ(HOST): 誰かが選択中なら次のボスを待つ(最長 MapRushChoiceWaitCap 秒。選択中の人は今までどおり狙われず被弾しない)
    //  ・2026-10-16 第6弾(マスター): ラスダンのラッシュ(最大3体)にも使う。生きているボスが 0体で予告もしていない「自然にできた合間」にだけ出す。
    //    選択を閉じた後は、次の予告の前に出現の間隔(minSpawnSpacingSeconds)を取り直す(待ち列のボスをまとめて出さない)。
    //    合間が無かった分は、全部倒した後(三姉妹戦の前、BossManager の 100km の確認)に残りを全部処理する
    public static float MapRushChoiceGrace = 0.8f, MapRushChoiceWaitCap = 15f;
    public static bool LdRushChoices = true; // 比較用(-ldRushChoices 0 で以前の「最後にまとめて」)
    public bool RushActive => CurrentBossEncounter != null && CurrentBossEncounter.continuous;
    bool RushChoicesFor(BossEncounter enc) => enc != null && enc.continuous && (enc.mapRush || LdRushChoices);
    public int RushChoiceSpacingResets { get; private set; } // 確認用
    public bool RushChoiceWindow
    {
        get
        {
            var enc = CurrentBossEncounter;
            return RushChoicesFor(enc) && IsBossPhase && !BossDefeatedThisPhase && ActiveCount(enc) == 0 && enc.telegraphing == null;
        }
    }
    float rushChoiceClosedAt = -99f, rushChoiceWaitFrom = -1f; bool rushWasChoosing;
    public int RushChoiceHolds { get; private set; }
    bool MapRushChoiceHold(BossEncounter enc)
    {
        var gm = GameManager.Instance;
        if (gm == null) return false;
        bool multi = NetRunLauncher.IsMultiplayerRun;
        var pc = PlayerController.Instance;
        bool hostDown = multi && pc != null && pc.NetIsDowned; // DOWN の間は選択を開けないので、HOST の未処理の選択では待たない
        bool local = !hostDown && (gm.PendingLevelUpCount > 0 || gm.LevelUpPending || gm.IsRewardSequenceRunning);
        if (rushWasChoosing && !local) rushChoiceClosedAt = Time.unscaledTime;
        rushWasChoosing = local;
        bool remote = false;
        if (NetRunLauncher.IsMultiplayerRun) foreach (var p in NetPlayer.All) if (p != null && NetMatch.IsPlayerChoosing(p.PlayerNumber)) remote = true;
        bool hold = local || remote || Time.unscaledTime < rushChoiceClosedAt + MapRushChoiceGrace;
        if (!hold)
        {
            // 選択を閉じた後: 次のボスの予告の前に、出現の間隔を取り直す(閉じた直後に待ち列から続けて出さない)
            if (rushChoiceWaitFrom >= 0f) { enc.lastSpawnAt = Mathf.Max(enc.lastSpawnAt, Time.time); RushChoiceSpacingResets++; }
            rushChoiceWaitFrom = -1f; return false;
        }
        if (rushChoiceWaitFrom < 0f) { rushChoiceWaitFrom = Time.unscaledTime; RushChoiceHolds++; }
        // 協力マルチは選択中の人だけが止まる(世界は止まらない)ので、待ち続けない(上限 MapRushChoiceWaitCap 秒)。
        // 上限を過ぎて次のボスが出ても、選択中の人は今までどおり狙われず被弾しない。ソロ/CPU は世界ごと止まっているので上限なし
        if (multi && Time.unscaledTime - rushChoiceWaitFrom > MapRushChoiceWaitCap) return false;
        return true;
    }

    // ラッシュを終えた(全員倒した): 90km の門番を倒した扱い(疾走出発の 90km の解放)
    void OnMapRushCleared()
    {
        if (GameManager.Instance != null && !DebugRun.WritesBlocked && !NetRunLauncher.IsMultiplayerRun) // 疾走出発の記録はソロの通常ランだけ(通常の関門と同じ)
            SprintRecords.MarkGateCleared(GameManager.Instance.ActiveRunStageId, RushFirstK);
        Debug.Log($"[MapRush] cleared at {(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0}m - next gate 101km, the reaper chase at 100km");
    }
}

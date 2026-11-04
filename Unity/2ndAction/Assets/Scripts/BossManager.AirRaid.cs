using System.Collections.Generic;
using UnityEngine;

// 空中ボスの乱入(2026-11-04、マスターの依頼「通常走行中にランダムで空中ボスが乱入する」)。調整値は RoadEventTuning。
//  ・道中イベント(RoadEvents)の1つとして、30km 以降の 5km 区間の中で低い確率で起きる(1回出たら raidCooldownKm は出ない)。
//    5km/10km の関門の直前・90km 以降・BONUS ZONE の間・別のボス戦の間・マルチ(今は対象外)には出さない(CanStartAirRaid)。
//  ・走りながら戦う: 距離を止めない(各マップのラッシュと同じ「ラン再開」の状態で始める。報酬の後の距離の除外もしない)。
//    予告(上空を影が横切る + 風の音 + WARNING)→ 前方から飛んで画面に入る → 並走して攻撃 → 倒すとそのまま走り続ける。
//  ・関門のボスより短く: HP = その距離の再戦の強さ × raidHpMul、崩しを溜まりやすく、必殺技なし(ApplyRaidLite)。
//    raidTimeLimit 秒で倒せない/次の関門が近づいた時は、報酬なしで空へ飛び去る(長く足止めしない・関門と重ならない)。
//  ・報酬は関門のボスより軽い: MILE × raidMileMul、EXP は小ボスの割合、ハートの回復(raidHealHearts)。カードの選択・チェックポイントは無し。
//    撃破の後は関門の流れ(撃破の演出 → ボス報酬 → 次の関門の決定)を通らない(DefeatOverride)= 次の関門の予定は変わらない。
public partial class BossManager
{
    public bool AirRaidActive => CurrentBossEncounter != null && CurrentBossEncounter.raid;
    public bool AirRaidTelegraphing => AirRaidActive && raidTelegraphUntil > 0f;
    public WildBossBase AirRaidBoss => raidBoss;
    public static int RaidsStarted, RaidsSpawned, RaidsDefeated, RaidsFled;
    public static readonly List<string> RaidLog = new List<string>(); // 確認用: 「key 秒 結果 km」
    public static string LastRaidKey = "", LastRaidResult = "";
    public static float LastRaidSeconds;
    WildBossBase raidBoss;
    string raidKey = "";
    float raidTelegraphUntil = -1f, raidStartedAt, raidStartD;

    public bool CanStartAirRaid(float d, out string why)
    {
        var tn = RoadEventTuning.I;
        var gm = GameManager.Instance;
        why = "";
        if (gm == null || !gm.HasStarted || gm.IsGameOver) { why = "no run"; return false; }
        if (RoadEventTuning.RaidDisabledByArg) { why = "-airRaid 0"; return false; }
        if (IsBossPhase) { why = "boss phase"; return false; }
        if (IsLastStage || ArenaMode.Active || TutorialMode.Active || SuppressGates || !useWildSchedule) { why = "stage"; return false; }
        if (NetRunLauncher.IsMultiplayerRun) { why = "multiplayer (not supported yet)"; return false; }
        if (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle) { why = "bonus zone"; return false; }
        if (d < tn.raidUnlockKm * 1000f) { why = "before unlock"; return false; }
        if (d > tn.untilKm * 1000f - 2000f) { why = "finale near"; return false; }
        float gap = NextBossDistance - d;
        if (gap < tn.raidMinGateGapKm * 1000f) { why = $"gate in {gap:F0}m"; return false; }
        if (Time.time < nextGateNotBefore) { why = "after a boss"; return false; }
        if (player == null) { why = "no player"; return false; }
        var keys = tn.RaidKeys(gm.ActiveRunStageId);
        if (keys == null || keys.Count == 0) { why = "no raid boss for the stage"; return false; }
        return true;
    }

    // 乱入を始める(予告から)。key = null ならステージの候補から選ぶ
    public bool StartAirRaid(string key = null)
    {
        var gm = GameManager.Instance;
        float d = gm != null ? gm.MaxDistance : 0f;
        if (!CanStartAirRaid(d, out string why)) { Debug.Log($"[AirRaid] not started at {d:F0}m: {why}"); return false; }
        var keys = RoadEventTuning.I.RaidKeys(gm.ActiveRunStageId);
        if (string.IsNullOrEmpty(key) || !ParseKey(key, out _, out _)) key = keys[Random.Range(0, keys.Count)];
        if (!ParseKey(key, out _, out _)) { Debug.LogWarning($"[AirRaid] unknown key {key}"); return false; }

        IsBossPhase = true;
        BeginEncounter(0, false);
        var enc = CurrentBossEncounter;
        enc.raid = true;
        enc.label = "RAID " + key;
        enc.total = 1;
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 1;
        // 距離は止めない(ラン再開と同じ状態。距離の除外も始めない)
        RunResumed = true; resumedAt = Time.time; encounterResumable = false;
        BossMusicTier = BossBgmTier.Normal;
        BossMusicKey = $"{gm.ActiveRunStageId}/Raid";
        BossDefeatedThisPhase = false;
        raidKey = key; raidBoss = null; raidStartD = d;
        raidTelegraphUntil = Time.time + Mathf.Max(0.3f, RoadEventTuning.I.raidTelegraphSeconds);
        RaidsStarted++; LastRaidKey = key; LastRaidResult = "telegraph";
        // 予告(エステルの案): 風/羽音 + 暗い単色の大きな影が後ろから頭上を越える(約1秒)→ 0.5秒で WARNING + 短い唸り声 → 本体が前から。画面は揺らさない
        ParseKey(key, out GateFamily fam, out _);
        RoadEvents.RaidTelegraph(RaidArt(key), fam == GateFamily.Cave, key, ShortName(key)); // 洞窟のボスの絵は右向き、荒野/天空は左向き
        Debug.Log($"[AirRaid] start {key} at {d:F0}m (next gate {NextBossDistance:F0}m) - telegraph {RoadEventTuning.I.raidTelegraphSeconds:F1}s");
        return true;
    }

    // BossManager.Update から(毎フレーム、関門の判定の前)
    void AirRaidTick()
    {
        var enc = CurrentBossEncounter;
        if (enc == null || !enc.raid || !IsBossPhase) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (raidTelegraphUntil > 0f)
        {
            if (Time.time < raidTelegraphUntil) return;
            raidTelegraphUntil = -1f;
            SpawnRaidBoss();
            if (raidBoss == null) EndAirRaid(false, "spawn failed");
            return;
        }
        if (raidBoss == null) { EndAirRaid(false, raidFleeAt >= 0f ? raidFleeWhy : "vanished"); return; } // 飛び去りきった / 撃破の知らせを通らずに消えた
        if (raidFleeAt >= 0f) { if (Time.time - raidFleeAt > 4f) EndAirRaid(false, raidFleeWhy + " (timeout)"); return; }
        if (raidBoss.IsDead || raidBoss.FlyingAway) return;            // 撃破の見た目の途中(終わると DefeatOverride)
        var tn = RoadEventTuning.I;
        float t = Time.time - raidStartedAt;
        float gateGap = NextBossDistance - gm.MaxDistance;
        if (t > tn.raidTimeLimit) FleeAirRaid($"time limit {tn.raidTimeLimit:F0}s");
        else if (gateGap < tn.raidFleeBeforeGateM) FleeAirRaid($"gate in {gateGap:F0}m");
    }

    void SpawnRaidBoss()
    {
        var gm = GameManager.Instance;
        float d = gm != null ? gm.MaxDistance : raidStartD;
        if (!ParseKey(raidKey, out GateFamily fam, out int kind)) return;
        SetRaidStrength(raidKey, d);
        lastSpawnedWild = null;
        if (fam == GateFamily.Cave)
        {
            SpawnCaveBoss((CaveBossKind)kind, 0);
            if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null) TerrainManager.Instance.cave.SetBossClearZone(SpawnRef().position.x, 40f);
            encounterHasCave = true;
        }
        else if (fam == GateFamily.Sky) SpawnSkyBoss((SkyBossKind)kind, 0);
        else SpawnWild((WildBossKind)kind, 0);
        raidBoss = lastSpawnedWild;
        if (raidBoss == null) return;
        var tn = RoadEventTuning.I;
        bossKeys[raidBoss] = raidKey;
        raidBoss.ApplyRaidLite(tn.raidStaggerMul, tn.raidNoUltimate);
        raidBoss.mileReward = Mathf.Max(1, Mathf.RoundToInt(raidBoss.mileReward * Mathf.Max(0f, tn.raidMileMul)));
        raidBoss.rewardClassOverride = tn.raidExpClass;
        raidBoss.DefeatOverride = OnAirRaidDefeated;
        raidStartedAt = Time.time;
        RaidsSpawned++; LastRaidResult = "fighting";
        ProgressStats.MarkBossSeen(raidKey);
        Debug.Log($"[AirRaid] spawn {raidKey} at {d:F0}m hp={raidBoss.maxHp} (rematch x{CurrentRematchHpMul:0.00}) mile={raidBoss.mileReward} exp={tn.raidExpClass}");
    }

    // その距離での再戦の強さ(関門のボスと同じ計算)× raidHpMul
    void SetRaidStrength(string key, float d)
    {
        if (!ParseKey(key, out GateFamily f, out int kind)) return;
        var tn = BossRematchTuning.I;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(f, kind)))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul) * Mathf.Max(0.05f, RoadEventTuning.I.raidHpMul) * RoadEventTuning.I.RaidHpFor(key);
        CurrentRematchTier = currentTier.label;
        DamageMul = Mathf.Max(0.1f, currentTier.damageMul);
        CurrentEncounterIsRematch = true;
    }

    void OnAirRaidDefeated(WildBossBase who)
    {
        if (CurrentBossEncounter == null || !CurrentBossEncounter.raid) return;
        BossesDefeated++;
        var gm = GameManager.Instance;
        int heal = Mathf.Max(0, RoadEventTuning.I.raidHealHearts) * CombatScale.HpPerHeart;
        if (gm != null && heal > 0) gm.KitHeal(heal);
        RaidsDefeated++;
        EndAirRaid(true, "defeated");
    }

    // 飛び去る: 画面の外へ出きるまで乱入は続く(その間は後回しのカードの選択を出さない = 演出を途切れさせない。エステル)
    string raidFleeWhy = "";
    float raidFleeAt = -1f;
    void FleeAirRaid(string why)
    {
        if (raidFleeAt >= 0f) return;
        RaidsFled++;
        raidFleeWhy = "fled: " + why; raidFleeAt = Time.time;
        LastRaidResult = raidFleeWhy;
        if (raidBoss != null) raidBoss.FlyAway();
        Debug.Log($"[AirRaid] {raidKey} flies away ({why})");
    }

    void EndAirRaid(bool defeated, string why)
    {
        var gm = GameManager.Instance;
        float secs = raidStartedAt > 0f ? Time.time - raidStartedAt : 0f;
        LastRaidSeconds = secs; LastRaidResult = defeated ? "defeated" : why;
        RaidLog.Add($"{raidKey}\t{secs:F1}\t{(defeated ? "defeated" : why)}\t{raidStartD / 1000f:F1}");
        if (encounterHasCave && TerrainManager.Instance != null && TerrainManager.Instance.cave != null) TerrainManager.Instance.cave.ClearBossClearZone();
        if (raidBoss != null) bossKeys.Remove(raidBoss);
        if (raidBoss != null && raidFleeAt >= 0f && !raidBoss.FlyAwayDone) Destroy(raidBoss.gameObject);
        raidBoss = null; raidTelegraphUntil = -1f; raidStartedAt = 0f; raidFleeAt = -1f;
        EndBossPhase(); // 関門の流れ(撃破の演出/ボス報酬/次の関門の決定/チェックポイント)は通らない
        aliveWildThisEncounter = 0;
        RoadEvents.OnRaidEnded(defeated);
        Debug.Log($"[AirRaid] end {raidKey}: {(defeated ? "defeated" : why)} after {secs:F1}s at {(gm != null ? gm.MaxDistance : 0f):F0}m (started {raidStartD:F0}m)");
    }

    void ResetAirRaid() { raidBoss = null; raidTelegraphUntil = -1f; raidStartedAt = 0f; raidFleeAt = -1f; }

    // 予告の影の絵(移動の絵 → 待機の絵)
    Sprite RaidArt(string key)
    {
        if (!ParseKey(key, out GateFamily f, out int kind)) return null;
        if (f == GateFamily.Cave) { var a = FindCaveArt((CaveBossKind)kind); return a != null ? (a.move != null ? a.move : a.idle) : null; }
        if (f == GateFamily.Sky)
        {
            if (skyArt != null) foreach (var a in skyArt) if (a != null && a.kind == (SkyBossKind)kind) return a.move != null ? a.move : a.idle;
            return null;
        }
        var w = FindArt((WildBossKind)kind);
        return w != null ? (w.move != null ? w.move : w.idle) : null;
    }
}

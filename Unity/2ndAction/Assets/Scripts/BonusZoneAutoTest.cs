#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// BONUS ZONEの自動確認(2026-09-29)。Tools/OneMoreMile/Bonus Zone Test (batch) → BonusZoneAutoTest.txt
//  1) 荒野: ボス撃破 → 自然にBONUS ZONEが始まる(ボスと重ならない)
//  2) 荒野: 6種を順に強制(報酬/逃げる/上限/Card Choiceの順番待ち/通常Encounterの停止/後片付け)
//  3) Game Over → 仮取得MILEを失う / 4) 洞窟: FINISHで持ち帰る / 5) 天空: 共通で動く
// 自動ボット: 近くの報酬Enemyを殴る(穴は跳ぶ)。Level Up/Card Choiceは1枚目を自動で選ぶ。
public class BonusZoneAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("BonusZoneTest", 0) != 1) return;
        EditorPrefs.SetInt("BonusZoneTest", 0);
        var go = new GameObject("BonusZoneTest");
        DontDestroyOnLoad(go);
        go.AddComponent<BonusZoneAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures; bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[BonusTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    void Warn(string what) { L("  WARN: " + what); }
    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    GameManager gm; PlayerController pc; TerrainManager tm; EncounterDirector dir; BonusZone zone;
    bool botOn; int choicesSeen; int ownedAtStart; RunCheckpoint.Data savedCheckpoint;
    float savedDuration; float savedChance;

    IEnumerator Watchdog() { yield return new WaitForSecondsRealtime(1500f); L("WATCHDOG: test did not finish in time"); Finish(); }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        savedCheckpoint = RunCheckpoint.Load();
        yield return new WaitForSecondsRealtime(1.5f);
        StartCoroutine(AutoPick());
        StartCoroutine(Bot());
        var prof = BonusZoneProfile.Load();
        Check(prof != null && prof.types.Count >= 6 && prof.formations.Count >= 6, $"BonusZone profile ({prof?.types.Count} types, {prof?.formations.Count} formations)");
        foreach (string id in new[] { "treasure_goblin", "mimic", "golden_slime", "card_fairy" })
        {
            var d = EnemyDatabase.FindById(id);
            Check(d != null && d.bonusKind != BonusEnemyKind.None && d.stageIds != null && d.stageIds.Contains("bonus_zone"), $"{id}: bonus definition (never in normal spawn pools)");
        }
        savedDuration = prof.durationSeconds; savedChance = prof.chanceAfterBoss;
        BonusZone.AllowNaturalInBatch = true; // このテストだけはbatchmodeでも自然発生させる
        prof.durationSeconds = 16f;

        // ===== 1) 荒野: ボス撃破 → 自然発生 =====
        yield return BeginRun("wasteland_road");
        ownedAtStart = gm.TotalOwnedMile;
        yield return NaturalAfterBoss(prof);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false; // 以降は強制で種類ごとに確かめる(ボスは出さない)

        // 種類の抽選(開始はしない): 毎回同じにならない / JACKPOTは低確率
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 2000; i++) { var t = zone.RollType(); counts[t.id] = counts.TryGetValue(t.id, out int c) ? c + 1 : 1; }
        L("  roll x2000: " + string.Join(" ", counts.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
        int jp = counts.TryGetValue("jackpot", out int j) ? j : 0;
        Check(counts.Count >= 6, "the bonus encounter is not always the same (all 6 kinds come up)");
        Check(jp > 2000 * prof.jackpotChance * 0.5f && jp < 2000 * prof.jackpotChance * 1.6f, $"JACKPOT is rare ({jp}/2000, chance {prof.jackpotChance:P0})");

        // ===== 2) 6種 =====
        foreach (string id in new[] { "mile_rush", "mimic_bash", "exp_fever", "card_hunt", "treasure_parade", "jackpot" })
            yield return ForcedZone(id);
        yield return MimicCap();
        yield return FairyKill();
        yield return ChoiceConflict();

        // ===== 3) Game Over: 仮取得MILEを失う =====
        L("\n[GAME OVER after bonus]");
        int bonusMile = gm.RunBonusMile, runMile = gm.RunMile, owned = gm.TotalOwnedMile;
        Check(bonusMile > 0 && runMile >= bonusMile, $"bonus MILE is in the run's provisional MILE (RunBonusMile {bonusMile}, RunMile {runMile})");
        Check(owned == ownedAtStart, $"bonus MILE is not owned yet (wallet {owned} = {ownedAtStart} at run start)");
        typeof(GameManager).GetMethod("FinishRun", NP).Invoke(gm, null); // HP 0 と同じ(IsWin=false)
        yield return new WaitForSecondsRealtime(0.4f);
        Check(gm.IsGameOver && !gm.IsWin && gm.TotalOwnedMile == owned, $"GAME OVER: provisional MILE is lost (wallet stays {gm.TotalOwnedMile})");
        Check(zone.State == BonusZone.Phase.Idle && zone.ActiveEnemyCount() == 0, "no bonus state/enemies left after game over");
        yield return Retry();

        // ===== 4) 洞窟: FINISHで持ち帰る =====
        yield return BeginRun("natural_cave");
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        // PERFECTの判定(穴に落ちてGame Overにならないよう、この間は穴を出さない)
        float pit0 = TerrainManager.Instance.pitChanceBase, pit1 = TerrainManager.Instance.pitChanceMax;
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
        yield return PerfectRules();
        TerrainManager.Instance.pitChanceBase = pit0; TerrainManager.Instance.pitChanceMax = pit1;
        yield return ForcedZone("treasure_parade", "cave");
        owned = gm.TotalOwnedMile; runMile = gm.RunMile; bonusMile = gm.RunBonusMile;
        gm.Win();
        yield return new WaitForSecondsRealtime(0.4f);
        int gained = gm.TotalOwnedMile - owned;
        Check(bonusMile > 0 && gm.IsWin && gained == gm.RunMile && gm.RunMile >= bonusMile, $"FINISH: the MILE incl. bonus (+{bonusMile}) is banked (wallet +{gained}, RunMile {gm.RunMile})");
        gm.TrySpendMile(gained); // テストで増やした分を戻す
        yield return Retry();

        // ===== 5) 天空 =====
        yield return BeginRun("sky_corridor");
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        yield return ForcedZone("exp_fever", "sky");
        yield return ForcedZone("mile_rush", "sky");
        Finish();
    }

    void OnDestroy() { }

    // ===================================================================== //
    IEnumerator BeginRun(string stage)
    {
        L($"\n===== run on {stage} =====");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stage);
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance; tm = TerrainManager.Instance; dir = EncounterDirector.Instance; zone = BonusZone.Instance;
        gm.DebugSetInvincible(true);
        // 穴への落下(無敵でもGame Over)でテストが途中で崩れないよう、このテストの間は穴を出さない
        tm.pitChanceBase = 0f; tm.pitChanceMax = 0f;
        Check(gm.HasStarted && zone != null && dir != null, $"run started on {stage} (BonusZone present)");
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator Retry()
    {
        var old = gm;
        gm.Retry();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
    }

    // Level Up/Boss Reward/Card Fairyの3枚Card Choiceは1枚目を選ぶ(開いた回数を数える)
    IEnumerator AutoPick()
    {
        while (true)
        {
            var g = GameManager.Instance;
            if (g != null && g.IsRewardSequenceWaitingForSelection)
            {
                choicesSeen++;
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.2f); seq.OnCardClicked(0); }
                float w = 0f;
                while (g != null && g.IsRewardSequenceWaitingForSelection && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
                yield return new WaitForSecondsRealtime(0.1f);
                continue;
            }
            yield return null;
        }
    }

    // 近くの報酬Enemyを殴る。穴は跳ぶ。空中の妖精は跳んで空中攻撃。ミミックには打ち上げ→空中→叩きつけも混ぜる。
    IEnumerator Bot()
    {
        int n = 0;
        while (true)
        {
            var p = PlayerController.Instance; var t = TerrainManager.Instance; var z = BonusZone.Instance;
            if (!botOn || p == null || t == null || z == null || Time.timeScale <= 0f || GameManager.Instance == null || GameManager.Instance.IsGameOver) { yield return null; continue; }
            float x = p.transform.position.x;
            PlayerController.FlickDirection? f = null;
            float lead = 1.1f * Mathf.Max(1f, p.CurrentAutoRunSpeed / 5f);
            if (p.IsGrounded && t.IsNearPit(x + lead, 0.4f)) f = PlayerController.FlickDirection.Up;
            else if (!p.IsGrounded && !t.GetHeightAt(x).HasValue && !t.GetHeightAt(x + 1.5f).HasValue) f = PlayerController.FlickDirection.Up; // 穴の上: 二段ジャンプで越える
            else
            {
                BonusEnemy best = null; float bd = 99f;
                foreach (var e in z.Enemies)
                {
                    if (e == null || !e.isActiveAndEnabled) continue;
                    float dx = e.transform.position.x - x;
                    if (dx < -0.4f || dx > 2.8f) continue;
                    if (dx < bd) { bd = dx; best = e; }
                }
                if (best != null)
                {
                    float dy = best.transform.position.y - p.transform.position.y;
                    if (best.kind == BonusEnemyKind.CardFairy && dy > 1.1f) f = p.IsGrounded ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
                    else if (best.kind == BonusEnemyKind.Mimic) { int k = n % 8; f = k == 0 ? PlayerController.FlickDirection.Up : k == 5 ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward; }
                    else f = PlayerController.FlickDirection.Forward;
                }
            }
            if (f.HasValue)
            {
                n++;
                p.debugInjectFlick = f.Value;
                float w = 0f;
                do { yield return null; w += Time.unscaledDeltaTime; } while (Time.timeScale <= 0f && w < 1f);
                p.debugInjectFlick = null;
                yield return new WaitForSeconds(0.12f);
                continue;
            }
            yield return null;
        }
    }

    // ===================================================================== //
    IEnumerator NaturalAfterBoss(BonusZoneProfile prof)
    {
        L("\n[natural start after a boss]");
        prof.chanceAfterBoss = 1f;
        var bm = BossManager.Instance;
        gm.DebugWarpToDistance(bm.NextBossDistance - 25f);
        float w = 0f;
        while (!bm.IsBossPhase && w < 20f) { yield return null; w += Time.deltaTime; }
        Check(bm.IsBossPhase, $"boss phase started at {gm.MaxDistance:F0}m");
        bool bonusDuringBoss = false;
        w = 0f;
        while (bm.IsBossPhase && w < 60f)
        {
            if (zone.State != BonusZone.Phase.Idle) bonusDuringBoss = true;
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!b.IsDead) b.TakeDamage(99999, b.CenterWorld);
            yield return new WaitForSeconds(0.3f); w += 0.3f;
        }
        Check(!bm.IsBossPhase && !bonusDuringBoss, "boss defeated + reward done, no bonus during the boss");
        float endAt = gm.MaxDistance;
        w = 0f;
        while (zone.State == BonusZone.Phase.Idle && w < 20f) { yield return null; w += Time.deltaTime; }
        Check(zone.State != BonusZone.Phase.Idle, $"BONUS ZONE starts on its own after a short safe run ({zone.Current?.id}, {gm.MaxDistance - endAt:F0}m after the boss phase ended)");
        prof.chanceAfterBoss = savedChance;
        yield return RunZone(zone.Current != null ? zone.Current.id : "?", "natural");
    }

    IEnumerator ForcedZone(string id, string tag = "wasteland")
    {
        L($"\n[{id} / {tag}]");
        Check(zone.Force(id), $"Force {id}");
        yield return RunZone(id, tag);
    }

    // 1回の区画を最後まで走って確かめる
    IEnumerator RunZone(string id, string tag)
    {
        botOn = true;
        int rec0 = dir.Recent.Count;
        int mile0 = gm.RunBonusMile; float exp0 = gm.TotalExpEarned; int choices0 = choicesSeen; int level0 = gm.Level; float etn0 = gm.ExpToNext;
        bool sawIntro = zone.State == BonusZone.Phase.Intro;
        int maxActive = 0, normalAhead = 0; float dtSum = 0f; int frames = 0;
        var kinds = new HashSet<BonusEnemyKind>();
        // 逃げる: 見えた報酬Enemyの前進の速さ
        var firstSeen = new Dictionary<BonusEnemy, (float t, float x)>();
        var fleeSpeeds = new Dictionary<BonusEnemyKind, List<float>>();
        int goblinHitRewards = 0, goblinKills = 0, slimeKills = 0, fairyKills = 0, mimicHitRewards = 0, mimicAerial = 0;
        var mileBefore = new Dictionary<BonusEnemy, int>();
        float w = 0f;
        while (zone.State != BonusZone.Phase.Idle && w < 90f)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            if (Time.timeScale <= 0f) continue;
            dtSum += Time.unscaledDeltaTime; frames++;
            maxActive = Mathf.Max(maxActive, zone.ActiveEnemyCount());
            foreach (var e in zone.Enemies)
            {
                if (e == null) continue;
                kinds.Add(e.kind);
                if (e.isActiveAndEnabled && IsOnScreen(e.transform.position.x))
                {
                    if (!firstSeen.ContainsKey(e)) firstSeen[e] = (Time.time, e.transform.position.x);
                    else if (Time.time - firstSeen[e].t > 0.6f && Time.time - firstSeen[e].t < 0.7f && e.Hits == 0)
                    {
                        float sp = (e.transform.position.x - firstSeen[e].x) / (Time.time - firstSeen[e].t);
                        if (!fleeSpeeds.ContainsKey(e.kind)) fleeSpeeds[e.kind] = new List<float>();
                        fleeSpeeds[e.kind].Add(sp);
                    }
                }
                mileBefore.TryGetValue(e, out int mb);
                if (e.MileGiven > mb)
                {
                    if (e.kind == BonusEnemyKind.TreasureGoblin) goblinHitRewards++;
                    if (e.kind == BonusEnemyKind.Mimic) { mimicHitRewards++; if (!pc.IsGrounded) mimicAerial++; }
                    mileBefore[e] = e.MileGiven;
                }
            }
            if (zone.State == BonusZone.Phase.Active && zone.Elapsed > 3f && zone.Elapsed < 3.05f) normalAhead = CountNormalEnemiesAhead();
        }
        botOn = false;
        // 撃破数(非アクティブになった報酬Enemyのうち、倒されたもの=Hitsがある)
        foreach (var e in zone.Enemies) { }
        var recs = dir.Recent.Skip(rec0).ToList();
        // 区画の間(終了+安全距離まで)に置かれた通常のEncounter(戻った後に前方へ置かれた分は数えない)
        int bonusRecs = recs.Count(r => r.band == "BONUS"), normalRecs = recs.Count(r => r.band != "BONUS" && r.members.Count > 0 && r.distance < zone.EndDistance + BonusZoneProfile.Load().postRestDistance);
        int mileGot = gm.RunBonusMile - mile0; float expGot = gm.TotalExpEarned - exp0;
        L($"  zone {id}: {w:F1}s, bonus waves {bonusRecs} [{string.Join(",", recs.Where(r => r.band == "BONUS").Select(r => r.formation))}], normal encounters {normalRecs}, kinds {string.Join(",", kinds)}");
        L($"  result: MILE +{zone.BonusMile} (RunBonusMile +{mileGot}) EXP +{zone.BonusExp:F0} (TotalExp +{expGot:F0}) CARD +{zone.BonusCards} (choices shown {choicesSeen - choices0}); max active bonus enemies {maxActive}, avg frame {(frames > 0 ? dtSum / frames * 1000f : 0f):F1}ms");
        L($"  flee speeds (m/s, first 0.6s on screen): {string.Join("  ", fleeSpeeds.Select(k => $"{k.Key}={string.Join("/", k.Value.Take(4).Select(v => v.ToString("F1")))}"))}; player {pc.CurrentAutoRunSpeed:F1}");
        Check(sawIntro || zone.ZonesStarted > 0, $"{tag}/{id}: BONUS ZONE started (intro shown)");
        Check(bonusRecs > 0 && normalRecs == 0 && normalAhead == 0, $"{tag}/{id}: only bonus waves during the zone (normal encounters {normalRecs}, normal enemies waiting ahead {normalAhead})");
        Check(zone.State == BonusZone.Phase.Idle && zone.ActiveEnemyCount() == 0, $"{tag}/{id}: zone ended, no bonus enemies left behind");
        Check(mileGot == zone.BonusMile, $"{tag}/{id}: BONUS RESULT MILE matches the run's provisional MILE (+{zone.BonusMile} / +{mileGot})");
        L($"  breakdown: enemy MILE +{zone.BonusMile - zone.ClearMile - zone.PerfectMile} / CLEAR +{zone.ClearMile} MILE +{zone.ClearExp:F0} EXP / PERFECT {(zone.PerfectAchieved ? $"YES +{zone.PerfectMile} MILE +{zone.PerfectExp:F0} EXP" : "no")} [{zone.PerfectDetail}] / levels gained {gm.Level - level0} (ExpToNext was {etn0:F0})");
        Check(zone.Current == null || zone.Current.clearMile <= 0 || zone.ClearMile > 0, $"{tag}/{id}: BONUS CLEAR reward granted (+{zone.ClearMile} MILE +{zone.ClearExp:F0} EXP)");
        Check(zone.PerfectAchieved || zone.PerfectMile == 0, $"{tag}/{id}: no PERFECT reward without PERFECT");
        Check(maxActive <= BonusZoneProfile.Load().maxActiveEnemies + 4, $"{tag}/{id}: bonus enemies come in small waves (max {maxActive} active at once)");
        bool mileType = id == "mile_rush" || id == "mimic_bash" || id == "treasure_parade" || id == "jackpot";
        bool expType = id == "exp_fever" || id == "card_hunt" || id == "treasure_parade" || id == "jackpot";
        if (mileType) Check(zone.BonusMile > 0, $"{tag}/{id}: MILE earned (+{zone.BonusMile})");
        if (expType) Check(zone.BonusExp >= BonusZoneProfile.Load().goldenSlimeExp, $"{tag}/{id}: lots of EXP from Golden Slimes (+{zone.BonusExp:F0})");
        if (id == "mile_rush" || id == "treasure_parade") Check(goblinHitRewards > 0, $"{tag}/{id}: hitting a Treasure Goblin gives MILE ({goblinHitRewards} rewarded hits)");
        if (fleeSpeeds.TryGetValue(BonusEnemyKind.TreasureGoblin, out var gs)) Check(gs.Average() > 1f, $"{tag}/{id}: Treasure Goblin runs away in the player's direction (avg {gs.Average():F1} m/s)");
        if (fleeSpeeds.TryGetValue(BonusEnemyKind.CardFairy, out var fs)) Check(fs.Average() > 1f, $"{tag}/{id}: Card Fairy flies away (avg {fs.Average():F1} m/s)");
        if (id == "mimic_bash" || id == "jackpot") Check(mimicHitRewards >= 3, $"{tag}/{id}: every hit on the Mimic gives MILE ({mimicHitRewards} rewarded hits, {mimicAerial} while airborne)");
        if (id == "card_hunt" || id == "jackpot") Check(zone.BonusCards >= 1 ? choicesSeen - choices0 >= zone.BonusCards : true, $"{tag}/{id}: Card Fairy kill -> the existing 3-card choice ({zone.BonusCards} fairies caught, {choicesSeen - choices0} choices shown)");
        Check(zone.FairiesThisZone <= BonusZoneProfile.Load().maxFairiesPerZone, $"{tag}/{id}: Card Fairy is rare ({zone.FairiesThisZone} spawned, cap {BonusZoneProfile.Load().maxFairiesPerZone})");
        // (Level Upの選択が開いていれば、選び終わるまで待ってから)
        float cw = 0f;
        while ((gm.LevelUpPending || gm.PendingLevelUpCount > 0 || gm.IsRewardSequenceRunning) && cw < 10f) { yield return null; cw += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.2f);
        Check(Time.timeScale > 0.99f && !gm.LevelUpPending && pc.CurrentAutoRunSpeed > 0.1f, $"{tag}/{id}: back to normal running (timeScale {Time.timeScale:F2}, speed {pc.CurrentAutoRunSpeed:F1})");
        // 通常Encounterへ戻る
        int before = dir.Recent.Count;
        w = 0f;
        while (dir.Recent.Skip(before).All(r => r.band == "BONUS") && w < 25f) { yield return null; w += Time.deltaTime; }
        var back = dir.Recent.Skip(before).FirstOrDefault(r => r.band != "BONUS");
        Check(back != null, $"{tag}/{id}: normal encounters resume after the zone ({back?.formation} {(back != null ? (back.distance - zone.EndDistance).ToString("F0") + "m after BONUS END" : "")})");
        Check(back == null || back.distance - zone.EndDistance >= BonusZoneProfile.Load().postRestDistance, $"{tag}/{id}: a safe gap before the first normal encounter");
    }

    bool IsOnScreen(float x)
    {
        Camera cam = Camera.main; if (cam == null) return false;
        float half = cam.orthographicSize * cam.aspect;
        return x < cam.transform.position.x + half - 0.5f && x > cam.transform.position.x - half + 0.5f;
    }

    int CountNormalEnemiesAhead()
    {
        int n = 0; float px = pc.transform.position.x;
        foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (ec != null && ec.bonus == null && ec.gameObject.activeInHierarchy && ec.transform.position.x > px + 2f) n++;
        return n;
    }

    // PERFECT の判定: 何もしなければ不成立、条件を満たせば成立。PERFECTでCard Choiceは増えない。
    IEnumerator PerfectRules()
    {
        L("\n[perfect rules]");
        // 1) 何も倒さずに終わる → PERFECTなし
        zone.Force("mile_rush");
        yield return new WaitForSeconds(4f);
        zone.End();
        Check(!zone.PerfectAchieved && zone.PerfectMile == 0 && zone.ClearMile > 0, $"mile_rush without kills: CLEAR only (clear +{zone.ClearMile}, perfect={zone.PerfectAchieved} [{zone.PerfectDetail}])");
        yield return WaitIdle();

        // 2) 画面に現れたTreasure Goblinをすべて倒す → PERFECT(Card Choiceは増えない)
        int cards0 = zone.BonusCards; int choices0 = choicesSeen; int level0 = gm.Level;
        zone.Force("mile_rush");
        float t = 0f;
        int killed = 0;
        while (t < 9f)
        {
            foreach (var e in zone.Enemies)
                if (e != null && e.isActiveAndEnabled && e.Seen && !e.Killed && e.kind == BonusEnemyKind.TreasureGoblin && KillBonusEnemy(e)) killed++;
            yield return null; t += Time.deltaTime;
        }
        foreach (var e in zone.Enemies)
            if (e != null && e.kind == BonusEnemyKind.TreasureGoblin && !e.Killed)
                L($"   goblin not killed: active={e.isActiveAndEnabled} seen={e.Seen} age={(e.Seen ? Time.time - e.FirstSeenTime : -1f):F2}s hp={typeof(EnemyController).GetField("hp", NP).GetValue(e.GetComponent<EnemyController>())} dying={e.GetComponent<EnemyController>().IsDying} dx={e.transform.position.x - pc.transform.position.x:F1}");
        zone.End();
        L($"  killed {killed} goblins (zone counted {zone.KilledCount(BonusEnemyKind.TreasureGoblin)}, seen {zone.SeenCount(BonusEnemyKind.TreasureGoblin)}): perfect={zone.PerfectAchieved} [{zone.PerfectDetail}] +{zone.PerfectMile} MILE; card choices shown {choicesSeen - choices0} (levels gained {gm.Level - level0})");
        Check(zone.PerfectAchieved && zone.PerfectMile > 0, "mile_rush: all Treasure Goblins on screen killed -> PERFECT BONUS");
        Check(zone.BonusCards == 0, "PERFECT does not add a Card Choice (only level-ups / fairies do)");
        yield return WaitIdle();
        Check(choicesSeen - choices0 == gm.Level - level0, $"card choices after the perfect zone = level-ups only ({choicesSeen - choices0} choices, {gm.Level - level0} level-ups)");

        // 3) Mimic: 120 MILE 以上引き出す → PERFECT
        zone.Force("mimic_bash");
        yield return new WaitForSeconds(0.3f);
        var go = dir.DebugSpawnEnemy("mimic", EnemyAiTier.T0);
        yield return null; yield return null;
        var be = go != null ? go.GetComponent<BonusEnemy>() : null;
        if (be != null)
        {
            for (int i = 0; i < 40 && !be.Capped; i++) be.OnLocalHit(i % 3 == 0 ? PlayerAttackKind.Up : PlayerAttackKind.Normal, false, false, go.transform.position);
            string progCapped = zone.PerfectProgress(out bool metCapped);
            L($"  mimic capped at {zone.MimicMile} MILE: progress [{progCapped}] met={metCapped} (needs {zone.Current.perfectMimicMile} MILE in total, or a kill)");
            Check(!metCapped || zone.MimicMile >= zone.Current.perfectMimicMile, "one Mimic drained to its cap alone is not PERFECT when the zone target is higher");
            KillBonusEnemy(be);
            yield return null; yield return null;
            zone.End();
            L($"  mimic killed: perfect={zone.PerfectAchieved} [{zone.PerfectDetail}]");
            Check(zone.PerfectAchieved, "mimic_bash: the Mimic beaten (or the MILE target drawn) -> PERFECT BONUS");
        }
        else Warn("mimic debug spawn failed");
        yield return WaitIdle();
    }

    bool KillBonusEnemy(BonusEnemy e)
    {
        var ec = e.GetComponent<EnemyController>();
        var atk = pc.attackHitbox;
        if (ec == null || atk == null) return false;
        typeof(EnemyController).GetField("hp", NP).SetValue(ec, 1);
        PlayerAttackInfo.RearmOf(atk);
        ec.ReceiveSweptAttack(atk);
        return e.Killed;
    }

    IEnumerator WaitIdle()
    {
        float w = 0f;
        while ((zone.State != BonusZone.Phase.Idle || gm.LevelUpPending || gm.PendingLevelUpCount > 0) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.3f);
    }

    // ミミックの報酬上限(無限に稼げない)と、攻撃の種類ごとの報酬
    IEnumerator MimicCap()
    {
        L("\n[mimic reward cap]");
        var prof = BonusZoneProfile.Load();
        zone.Force("mimic_bash");
        yield return new WaitForSeconds(0.2f);
        var go = dir.DebugSpawnEnemy("mimic", EnemyAiTier.T0);
        yield return null; yield return null;
        var be = go != null ? go.GetComponent<BonusEnemy>() : null;
        Check(be != null && be.kind == BonusEnemyKind.Mimic, "debug spawn of a single Mimic");
        if (be != null)
        {
            int m0 = zone.BonusMile;
            be.OnLocalHit(PlayerAttackKind.Normal, false, false, go.transform.position);
            int normal = zone.BonusMile - m0; m0 = zone.BonusMile;
            be.OnLocalHit(PlayerAttackKind.Up, false, false, go.transform.position);
            int up = zone.BonusMile - m0; m0 = zone.BonusMile;
            be.OnLocalHit(PlayerAttackKind.Down, true, false, go.transform.position);
            int slam = zone.BonusMile - m0;
            L($"  per hit: normal +{normal}, launch +{up}, slam +{slam}");
            Check(normal > 0 && up >= normal && slam > normal, "launch / slam hits give more than a normal hit");
            for (int i = 0; i < 200; i++) be.OnLocalHit(PlayerAttackKind.Normal, false, false, go.transform.position);
            Check(be.Capped && be.MileGiven <= prof.mimicMaxMile && be.RewardHits <= prof.mimicMaxRewardHits, $"200 more hits: capped at {be.MileGiven} MILE / {be.RewardHits} rewarded hits (max {prof.mimicMaxMile} / {prof.mimicMaxRewardHits})");
            int after = zone.BonusMile;
            be.OnLocalHit(PlayerAttackKind.Normal, false, false, go.transform.position);
            Check(zone.BonusMile == after, "no more MILE after the cap");
            yield return new WaitForSeconds(2f);
            Check(!go.activeInHierarchy || !IsOnScreen(go.transform.position.x) || go.transform.position.x > pc.transform.position.x, "capped Mimic runs away");
        }
        zone.End();
        float w = 0f;
        while (zone.State != BonusZone.Phase.Idle && w < 40f) { yield return null; w += Time.deltaTime; }
    }

    // Card Fairyを実際に撃破(敵の撃破処理 → BonusEnemy.OnKilled)→ 既存の3枚Card Choiceが必ず1回出る
    IEnumerator FairyKill()
    {
        L("\n[card fairy kill -> card choice]");
        zone.Force("card_hunt");
        yield return new WaitForSeconds(0.3f);
        var go = dir.DebugSpawnEnemy("card_fairy", EnemyAiTier.T0);
        yield return null; yield return null;
        var ec = go != null ? go.GetComponent<EnemyController>() : null;
        Check(ec != null && go.activeInHierarchy && ec.bonus != null, "Card Fairy spawned (with its glow)");
        int c0 = choicesSeen, cards0 = zone.BonusCards;
        if (ec != null)
        {
            // 撃破の報酬処理(HitAndDie/落下死と同じ入口)
            typeof(EnemyController).GetMethod("RegisterKillReward", NP).Invoke(ec, new object[] { false });
            go.SetActive(false);
        }
        float w = 0f;
        while (choicesSeen == c0 && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(zone.BonusCards == cards0 + 1 && choicesSeen == c0 + 1, $"Card Fairy kill -> exactly one existing 3-card choice ({choicesSeen - c0} shown, CARD +{zone.BonusCards - cards0})");
        w = 0f;
        while ((gm.LevelUpPending || gm.IsRewardSequenceRunning) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.3f);
        Check(Time.timeScale > 0.99f && !gm.LevelUpPending, "after the fairy's card choice: running resumes");
        zone.End();
        w = 0f;
        while (zone.State != BonusZone.Phase.Idle && w < 40f) { yield return null; w += Time.deltaTime; }
    }

    // Golden SlimeのLevel UpとCard FairyのCard Choiceが同時に来ても1件ずつ出る
    IEnumerator ChoiceConflict()
    {
        L("\n[level up + card fairy choice at the same time]");
        zone.Force("exp_fever");
        yield return new WaitForSeconds(0.5f);
        int c0 = choicesSeen, lv0 = gm.Level;
        float need = gm.ExpToNext - gm.Exp + gm.ExpToNext * 1.2f; // 2段階ぶんのEXP
        typeof(GameManager).GetField("expGainMultiplier", NP).SetValue(gm, 1f);
        float applied = gm.GrantBonusExp(need);
        zone.RewardCard(pc.transform.position + Vector3.right * 2f, null);
        int expected = (gm.Level - lv0) + 1;
        L($"  granted EXP {applied:F0} -> Lv {lv0} -> {gm.Level}, + 1 card fairy = {expected} choices queued (pending {gm.PendingLevelUpCount}, open {gm.LevelUpPending})");
        float w = 0f; int maxOpenAtOnce = 0;
        while ((choicesSeen - c0 < expected || gm.LevelUpPending || gm.PendingLevelUpCount > 0) && w < 30f)
        {
            int open = FindObjectsByType<RewardCardSequence>(FindObjectsSortMode.None).Count(s => s.IsRunning);
            maxOpenAtOnce = Mathf.Max(maxOpenAtOnce, open);
            yield return null; w += Time.unscaledDeltaTime;
        }
        yield return new WaitForSeconds(0.6f);
        float x0 = pc.transform.position.x;
        yield return new WaitForSeconds(0.5f);
        Check(choicesSeen - c0 == expected && maxOpenAtOnce <= 1, $"all {expected} card choices shown one at a time ({choicesSeen - c0} shown, max open at once {maxOpenAtOnce})");
        Check(Time.timeScale > 0.99f && !gm.LevelUpPending && gm.PendingLevelUpCount == 0 && pc.transform.position.x > x0 + 1f, "after the choices: time resumes, player runs on (no stuck pause)");
        zone.End();
        w = 0f;
        while (zone.State != BonusZone.Phase.Idle && w < 40f) { yield return null; w += Time.deltaTime; }
    }

    void Finish()
    {
        var prof = BonusZoneProfile.Load();
        if (prof != null) { prof.durationSeconds = savedDuration > 0f ? savedDuration : prof.durationSeconds; prof.chanceAfterBoss = savedChance; }
        if (savedCheckpoint != null) { if (savedCheckpoint.active) RunCheckpoint.Save(savedCheckpoint); else RunCheckpoint.Clear(); }
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../BonusZoneAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}

public static class BonusZoneTestMenu
{
    [MenuItem("Tools/OneMoreMile/Bonus Zone Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("BonusZoneTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

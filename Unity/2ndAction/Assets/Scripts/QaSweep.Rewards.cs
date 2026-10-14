#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 報酬の見直し(2026-10-15): 距離に応じた雑魚の MILE / ボスの EXP・MILE / 参加人数での MILE の分配 / ボス戦の途中の中断 / 疾走の到着レベル。
// 専用の保存(-fileSave / -memSave)で行う。
//  -qaRewards <dir> [-rwPhase all|formula|enemy|boss|resume|cpu|rush] [-rwChar swordsman]
//   formula … 式の値(係数・ボスの格・分配と端数・疾走の到着レベル)
//   enemy   … 0.3km / 20.5km / 60.5km で本当の雑魚を倒し、1体の MILE と距離の係数
//   boss    … 荒野の 1km / 5km / 10km の関門と天空のメカドラゴン: ボスの MILE・EXP・上がったレベル・カードの選択の数
//   resume  … ボスを倒した後、報酬を選ぶ前に中断 → CONTINUE で、ボスの報酬が二重にならない
//   cpu     … CPU の仲間 0〜3人: 分配の人数、HUD の持ち帰り予定 = 結果の画面 = 実際に増えた所持 MILE
//   rush    … ラスダンの 90〜98km のボスラッシュ(走りながら)の合計(MILE / EXP / レベル / ガチャの回数)
public partial class QaSweep
{
    static readonly BindingFlags RwNP = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    IEnumerator RewardsMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        Application.targetFrameRate = 60;
        StartCoroutine(AutoPickCards());
        Application.logMessageReceived += (m, st, t) => { if (m.StartsWith("[LevelUpPresentation] Started")) rwShown++; }; // 選択の画面を出した回数(レベルアップ/ボスの報酬)
        string phase = Arg("-rwPhase", "all");
        if (phase == "all" || phase == "formula") RwFormula();
        if (phase == "all" || phase == "enemy") yield return RwEnemy();
        if (phase == "all" || phase == "boss") yield return RwBoss();
        if (phase == "all" || phase == "resume") yield return RwResume();
        if (phase == "all" || phase == "cpu") yield return RwCpu();
        if (phase == "all" || phase == "rush") yield return RwRush();
        if (phase == "shots") yield return RwShots();
    }

    // 説明の文の撮影(レビュー用の画像と、縦横で重ならないかの確認)。起動の画面の大きさで撮る
    IEnumerator RwShots()
    {
        string res = $"{Screen.width}x{Screen.height}";
        foreach (var lang in Arg("-tsLangs", "ja,en").Split(','))
        {
            Loc.Set(lang, false);
            yield return OpenStageSelectForTest();
            CpuParty.Request.Clear(); CpuParty.Request.Add("archer"); CpuParty.Request.Add("mage");
            SupportDeparturePanel.DebugOpen(true);
            yield return new WaitForSecondsRealtime(0.6f); Shot($"rw_{lang}_{res}_support"); yield return Frames(3);
            SupportDeparturePanel.DebugOpen(false);
            yield return new WaitForSecondsRealtime(0.4f);
            SprintDeparturePanel.DebugOpen(true);
            yield return new WaitForSecondsRealtime(0.6f); Shot($"rw_{lang}_{res}_sprint"); yield return Frames(3);
            SprintDeparturePanel.DebugOpen(false);
            yield return new WaitForSecondsRealtime(0.4f);
            gm.CloseStageSelect();
            yield return new WaitForSecondsRealtime(1.0f);
            NetDebugUI.OpenPanel();
            typeof(NetDebugUI).GetField("screen", RwNP).SetValue(FindFirstObjectByType<NetDebugUI>(), NetDebugUI.MultiScreen.Mode);
            yield return new WaitForSecondsRealtime(0.8f); Shot($"rw_{lang}_{res}_coop"); yield return Frames(3);
            typeof(NetDebugUI).GetProperty("PanelOpen").GetSetMethod(true).Invoke(null, new object[] { false });
            yield return new WaitForSecondsRealtime(0.6f);
        }
        CpuParty.Request.Clear();
    }

    string RwChar => Arg("-rwChar", "swordsman");
    int rwShown;
    void RwSetShare(int n) => typeof(GameManager).GetProperty("MileShareCount").GetSetMethod(true).Invoke(gm, new object[] { n });
    void RwClearCarry() { foreach (var f in new[] { "carryEnemy", "carryBoss", "carryBonus", "carryRing" }) typeof(GameManager).GetField(f, RwNP).SetValue(gm, 0f); }
    float RwMileMul => gm.MileGainMultiplier * FinalEvolution.MileMul;

    // ---- 式 ----
    void RwFormula()
    {
        L("== formula ==");
        Check(Mathf.Approximately(RunRewards.EnemyMileFactor(1f), 1f), "enemy: the factor is 1 at the start (early rewards unchanged)");
        foreach (float r in new[] { 1f, 2f, 3f, 4.2f, 5f, 7f }) L($"[rw] enemy HP ratio {r:F1} -> MILE factor {RunRewards.EnemyMileFactor(r):F2}");
        Check(RunRewards.BossStrengthFactor(100f) <= RunRewards.BossStrengthMax + 1e-4f && RunRewards.BossStrengthFactor(0.3f) >= 1f, "boss: the strength factor is kept within 1..2.5 (no extremes when stacked)");
        foreach (var c in new[] { RunRewards.BossClass.Small, RunRewards.BossClass.Mid, RunRewards.BossClass.Big, RunRewards.BossClass.Elite }) L($"[rw] boss class {c}: EXP = {RunRewards.BossExpFraction(c):P0} of the next level (min {RunRewards.BossExpMin})");
        Check(RunRewards.ClassOf("Wild/Wolf") == RunRewards.BossClass.Small && RunRewards.ClassOf("Sky/MechDragon") == RunRewards.BossClass.Elite && RunRewards.ClassOf("Reaper") == RunRewards.BossClass.Elite && RunRewards.ClassOf("Wild/Golem") == RunRewards.BossClass.Big, "boss: classes by key");

        // 疾走の到着レベル
        L("[rw] sprint arrival level (destination -> arrival m -> Lv / EXP to next / distance-only meters to the next level)");
        int prev = 0;
        for (int km = 10; km <= 90; km += 10)
        {
            float arrival = km * 1000f - 250f;
            int lv = GameManager.SprintArrivalLevelFor(arrival);
            float next = 150f + 60f * (lv - 1);
            L($"[rw]   {km,3}km -> {arrival,6:F0}m -> Lv{lv,2}  next {next,5:F0} EXP  (~{next:F0}m by distance only)");
            Check(lv > prev, $"sprint: a farther destination starts at a higher level ({km}km Lv{lv})");
            prev = lv;
        }
        int lv90 = GameManager.SprintArrivalLevelFor(89750f);
        Check(lv90 >= 48 && lv90 <= 52, $"sprint: 90km arrives at about Lv50 (Lv{lv90})");
    }

    // ---- 分配と端数(実際のランの中で、受け取り口を直接呼ぶ)----
    IEnumerator RwShare()
    {
        L("== share / carry ==");
        float mul = RwMileMul;
        for (int n = 1; n <= 4; n++)
        {
            RwSetShare(n); RwClearCarry();
            int m0 = gm.RunEnemyMile, b0 = gm.RunBossMile, k0 = gm.EnemyKillCount;
            for (int i = 0; i < 120; i++) gm.RegisterEnemyKill(1f, 1f, false);
            gm.RegisterBossDefeat(300f, 0f, false);
            float expectE = 120f / n * mul, expectB = 300f / n * gm.BossMileGainMultiplier * FinalEvolution.MileMul;
            int gotE = gm.RunEnemyMile - m0, gotB = gm.RunBossMile - b0;
            L($"[rw] share 1/{n}: 120 kills x 1 MILE -> +{gotE} (expect {expectE:F2}), boss 300 -> +{gotB} (expect {expectB:F2}), carry {gm.MileCarryDebug:F3}");
            Check(Mathf.Abs(gotE - Mathf.Floor(expectE + 1e-3f)) < 0.01f && Mathf.Abs(gotB - Mathf.Floor(expectB + 1e-3f)) < 0.01f, $"share 1/{n}: the split amount keeps the fractions (no per-kill rounding loss)");
            Check(gm.EnemyKillCount == k0, $"share 1/{n}: a share alone does not count a kill or give EXP");
            yield return null;
        }
        RwSetShare(1); RwClearCarry();
    }

    List<EnemyController> RwEnemies() => FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.isActiveAndEnabled && !e.IsDyingOrReplica && e.GetComponent<BonusEnemy>() == null).ToList();

    // ---- 雑魚 ----
    IEnumerator RwEnemy()
    {
        L("== enemy ==");
        yield return BeginRun(RwChar, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return RwShare();
        var bands = new List<(float d, float avg, float factor)>();
        foreach (float d in new[] { 300f, 20500f, 60500f })
        {
            WarpTo(d);
            yield return new WaitForSecondsRealtime(1.5f);
            int kills = 0, mile = 0; float facSum = 0f, ratio = DistanceTierManager.Instance != null ? DistanceTierManager.Instance.HpStrengthRatio : 1f;
            float t = 0f;
            while (kills < 8 && t < 40f)
            {
                var list = RwEnemies().Where(e => Mathf.Abs(e.transform.position.x - pc.transform.position.x) < 30f).ToList();
                foreach (var e in list)
                {
                    if (kills >= 8) break;
                    if (Bm != null && Bm.IsBossPhase) break;
                    int m0 = gm.RunEnemyMile; float fac = e.MileDistanceFactor; int baseMile = e.mileReward;
                    e.ApplyElementDamage(9999999, e.transform.position);
                    yield return null;
                    if (!e.IsDying) continue;
                    int got = gm.RunEnemyMile - m0;
                    kills++; mile += got; facSum += fac;
                    if (kills <= 3) L($"[rw]   {d:F0}m {e.name}: base {baseMile} x factor {fac:F2} -> +{got} MILE");
                }
                yield return new WaitForSecondsRealtime(0.3f); t += 0.3f;
            }
            float avg = kills > 0 ? mile / (float)kills : 0f, favg = kills > 0 ? facSum / kills : 0f;
            L($"[rw] enemy band {d / 1000f:F1}km: HP ratio {ratio:F2}, factor {favg:F2} (expect {RunRewards.EnemyMileFactor(ratio):F2}), {kills} kills, {avg:F2} MILE/kill");
            Check(kills >= 3, $"enemy {d:F0}m: enemies were defeated ({kills})");
            // 係数は「その敵が出た時」の距離の段で決まる(ワープの前に先読みで出ていた敵は前の段のまま = その敵の HP と同じ段)
            float prevFac = bands.Count > 0 ? bands[bands.Count - 1].factor : 1f;
            Check(kills == 0 || (favg >= prevFac - 0.01f && favg <= RunRewards.EnemyMileFactor(ratio) + 0.01f), $"enemy {d:F0}m: the factor follows the distance band ({favg:F2} within {prevFac:F2}..{RunRewards.EnemyMileFactor(ratio):F2})");
            bands.Add((d, avg, favg));
        }
        if (bands.Count == 3) Check(bands[0].factor <= 1.001f && bands[1].factor > bands[0].factor && bands[2].factor > bands[1].factor, "enemy: farther = more MILE per kill, the start is unchanged");
        yield return EndRun();
    }

    // ---- ボス ----
    struct RwBossResult { public string what; public int bosses, mile, levels, picks; public float exp; }
    IEnumerator RwGate(float gate, string label, List<RwBossResult> outList)
    {
        yield return WaitPhaseEnd();
        int m0 = gm.RunBossMile, lv0 = gm.Level, p0 = rwShown, k0 = gm.BossKillCount; float tot0 = gm.TotalExpEarned; // 仲間が先に倒すこともあるので、ワープの前に控える
        WarpTo(gate - 70f);
        yield return WaitBossSpawn();
        int n = Mathf.Max(Bm.AliveBossCount, gm.BossKillCount - k0);
        string names = string.Join(",", WildAlive().Select(b => b.bossName + ":" + b.mileReward).Concat(DragonsAlive().Select(b => "Dragon:" + b.mileReward)));
        yield return new WaitForSecondsRealtime(0.5f);
        KillAllBosses();
        yield return WaitPhaseEnd(40f);
        float tw = 0f; while ((gm.IsRewardSequenceRunning || gm.LevelUpPending) && tw < 20f) { yield return null; tw += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.0f);
        var r = new RwBossResult { what = $"{label} [{names}]", bosses = gm.BossKillCount - k0, mile = gm.RunBossMile - m0, levels = gm.Level - lv0, picks = rwShown - p0, exp = gm.TotalExpEarned - tot0 };
        L($"[rw] {r.what}: bosses {r.bosses}, +{r.mile} MILE, +{r.exp:F0} EXP (incl. distance), Lv {lv0}->{gm.Level}, card choices {r.picks}");
        Check(r.bosses == n && n > 0, $"{label}: every boss counted once ({r.bosses}/{n})");
        Check(r.mile > 0, $"{label}: boss MILE granted");
        outList.Add(r);
    }

    IEnumerator RwBoss()
    {
        L("== boss ==");
        yield return BeginRun(RwChar, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var res = new List<RwBossResult>();
        yield return RwGate(1000f, "1km", res);
        yield return RwGate(5000f, "5km", res);
        yield return RwGate(10000f, "10km", res);
        yield return RwGate(21000f, "21km", res);
        // 連続撃破: 2体の関門(同時に倒す)でレベルアップが消えない・二重にならない
        foreach (var r in res) Check(r.picks == r.levels + 1, $"{r.what}: one choice per level-up + one boss reward, none lost or doubled (levels {r.levels}, choices {r.picks})");
        yield return EndRun();

        // メカドラゴン(天空の特別な強敵)
        yield return BeginRun(RwChar, "sky_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(3300f);
        yield return new WaitForSecondsRealtime(1f);
        int m0 = gm.RunBossMile, lv0 = gm.Level; float x0 = gm.Exp, need0 = gm.ExpToNext;
        Bm.DebugSpawnBossForTest(2, (int)SkyBossKind.MechDragon);
        yield return WaitBossSpawn();
        var mech = WildAlive().FirstOrDefault();
        int mechMile = mech != null ? mech.mileReward : -1;
        yield return new WaitForSecondsRealtime(0.5f);
        KillAllBosses();
        yield return new WaitForSecondsRealtime(1.5f);
        L($"[rw] MechDragon: mileReward {mechMile}, +{gm.RunBossMile - m0} MILE, last boss EXP {gm.LastBossExpGranted:F0} (next-level need was {need0:F0}), Lv {lv0}->{gm.Level}");
        Check(mechMile >= 300, $"MechDragon: a strong boss pays a fitting MILE ({mechMile})");
        Check(gm.LastBossExpGranted >= need0 * 1.1f, $"MechDragon: EXP is more than a full level ({gm.LastBossExpGranted:F0} >= {need0:F0})");
        yield return EndRun();
    }

    // ---- ボス戦の途中の中断 ----
    IEnumerator RwResume()
    {
        L("== resume during a boss encounter ==");
        yield return BeginRun(RwChar, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var res = new List<RwBossResult>();
        yield return RwGate(1000f, "1km (sets the checkpoint)", res);
        WarpTo(1930f);
        yield return WaitBossSpawn();
        Check(gm.EncounterSaveFrozen, "resume: the interrupt data is frozen at the encounter start");
        int bm0 = gm.RunBossMile, bk0 = gm.BossKillCount, em0 = gm.RunEnemyMile, lv0 = gm.Level;
        autoPickHold = true;
        KillAllBosses();
        float tw = 0f; while (!gm.IsRewardSequenceWaitingForSelection && tw < 15f) { yield return null; tw += Time.unscaledDeltaTime; }
        int bmKilled = gm.RunBossMile;
        L($"[rw] killed the 2km boss: boss MILE {bm0} -> {bmKilled}, kills {bk0} -> {gm.BossKillCount}, reward choice open {gm.IsRewardSequenceWaitingForSelection}, frozen saves {gm.InterruptSavesFrozen}");
        Check(bmKilled > bm0, "resume: the kill pays in the run");
        // 報酬を選ぶ前に中断(ホームへ)→ CONTINUE
        var data0 = RunCheckpoint.Load();
        Check(data0.runBossMile == bm0 && data0.bossKillCount == bk0, $"resume: the saved data still has the pre-encounter boss reward ({data0.runBossMile} == {bm0})");
        autoPickHold = false;
        typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, false);
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        yield return ResumeContinue("rw");
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        L($"[rw] continued at {gm.MaxDistance:F0}m: boss MILE {gm.RunBossMile} kills {gm.BossKillCount} Lv{gm.Level}");
        Check(gm.RunBossMile == bm0 && gm.BossKillCount == bk0, $"resume: no boss reward carried over from the interrupted encounter ({gm.RunBossMile} == {bm0})");
        // 同じボスを倒し直す → 1回分だけ
        res.Clear();
        yield return RwGate(2000f, "2km again", res);
        Check(res.Count == 1 && gm.RunBossMile - bm0 == bmKilled - bm0, $"resume: defeating it again pays exactly once ({gm.RunBossMile - bm0} == {bmKilled - bm0})");
        Check(!gm.EncounterSaveFrozen, "resume: saving resumes after the reward");
        yield return EndRun();
    }

    // ---- CPU サポート: 分配の人数と、表示 = 実際 ----
    IEnumerator RwCpu()
    {
        L("== CPU support / take-home ==");
        string[] allies = { "archer", "mage", "dragon_lancer" };
        for (int n = 0; n <= 3; n++)
        {
            CpuParty.Request.Clear();
            for (int i = 0; i < n; i++) CpuParty.Request.Add(allies[i]);
            yield return BeginRun(RwChar, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            yield return new WaitForSecondsRealtime(1f);
            Check(gm.MileShareCount == 1 + n && CpuParty.Size == n, $"cpu {n}: the share is 1/{1 + n} (allies {CpuParty.Size})");
            var res = new List<RwBossResult>();
            yield return RwGate(1000f, $"cpu{n} 1km", res);
            WarpTo(2600f);
            yield return new WaitForSecondsRealtime(4f);
            // 結果の画面へ(ワープしたので記録の対象外の印を外す)
            RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
            int hud = gm.TakeHomeMile, owned0 = gm.TotalOwnedMile;
            gm.Win();
            float w = 0f; while (!gm.ResultShown && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            int got = gm.TotalOwnedMile - owned0;
            L($"[rw] cpu {n} (share 1/{gm.MileShareCount}): HUD take-home {hud}, result RunMile {gm.RunMile}, owned +{got} (distance {gm.RunDistanceMile} enemy {gm.RunEnemyMile} boss {gm.RunBossMile})");
            Check(hud == gm.RunMile && got == gm.RunMile, $"cpu {n}: HUD take-home = result = actually received ({hud} / {gm.RunMile} / {got})");
            yield return EndRun();
        }
        CpuParty.Request.Clear();
    }

    // ---- ラスダンのボスラッシュの合計 ----
    IEnumerator RwRush()
    {
        L("== last corridor rush totals ==");
        yield return BeginRun(RwChar, "last_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(89900f);
        float w = 0f;
        while (!(Bm.IsBossPhase && Bm.CurrentBossEncounter != null && Bm.CurrentBossEncounter.continuous) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        var enc = Bm.CurrentBossEncounter;
        Check(enc != null && enc.continuous, "rush: the run rush starts at 90km");
        if (enc == null) { yield return EndRun(); yield break; }
        // ワープで来たので Lv1 のまま → 普通に 90km まで走った時に近い Lv(疾走の到着と同じ式)にしてから数える
        int startLv = int.Parse(Arg("-rwRushLv", GameManager.SprintArrivalLevelFor(89900f).ToString()));
        typeof(GameManager).GetProperty("Level").GetSetMethod(true).Invoke(gm, new object[] { startLv });
        typeof(GameManager).GetProperty("Exp").GetSetMethod(true).Invoke(gm, new object[] { 0f });
        typeof(GameManager).GetProperty("ExpToNext").GetSetMethod(true).Invoke(gm, new object[] { gm.expBaseForLevel2 + gm.expGrowthPerLevel * (startLv - 1) });
        int m0 = gm.RunBossMile, lv0 = gm.Level, k0 = gm.BossKillCount, p0 = cardPicks; float e0 = gm.TotalExpEarned; int em0 = gm.RunEnemyMile; int s0 = rwShown;
        PlayerController.DebugSpeedScale = 2f;
        w = 0f; float lastKill = 0f;
        while (w < 420f && Bm.IsBossPhase)
        {
            lastKill += Time.unscaledDeltaTime;
            if (lastKill > 1.5f)
            {
                var a = WildAlive().Where(b => !b.IsEntering).ToList();
                if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); lastKill = 0f; }
                foreach (var d in DragonsAlive()) d.TakeDamage(99999999);
            }
            yield return null; w += Time.unscaledDeltaTime;
        }
        PlayerController.DebugSpeedScale = 1f;
        int pendAtEnd = gm.PendingLevelUpCount, lvAtEnd = gm.Level;
        // ボス戦の間に後回しにしたレベルアップが、ラッシュの後で全部出るか(消えない・二重にならない)
        float tw = 0f; while ((gm.IsRewardSequenceRunning || gm.LevelUpPending || gm.PendingLevelUpCount > 0) && tw < 90f) { yield return null; tw += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        int mile = gm.RunBossMile - m0;
        int bossChoices = 1; // ラッシュの報酬の選択(ラッシュ全体で1回)
        L($"[rw] rush end: Lv{lvAtEnd}, deferred level-ups {pendAtEnd}, after waiting {tw:F0}s pending {gm.PendingLevelUpCount}, choices shown {rwShown - s0} (levels {gm.Level - lv0} + rush reward {bossChoices})");
        Check(gm.PendingLevelUpCount == 0 && rwShown - s0 == gm.Level - lv0 + bossChoices, $"rush: every level-up gets one choice after the rush, none lost or doubled (levels {gm.Level - lv0}, choices {rwShown - s0})");
        L($"[rw] rush 90-99km ({w:F0}s): bosses {gm.BossKillCount - k0}, boss +{mile} MILE (enemy +{gm.RunEnemyMile - em0}), +{gm.TotalExpEarned - e0:F0} EXP (incl. distance), Lv {lv0}->{gm.Level}, choices shown {rwShown - s0}, gacha pulls from boss MILE {mile / (float)GameManager.GachaCostMile:F1}");
        Check(gm.BossKillCount - k0 >= 9, $"rush: all 9 rush bosses defeated ({gm.BossKillCount - k0})");
        yield return EndRun();
    }
}
#endif

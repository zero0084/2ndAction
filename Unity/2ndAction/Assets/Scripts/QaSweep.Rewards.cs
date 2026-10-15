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
        if (phase == "quit1") yield return RwQuit1();
        if (phase == "quit2") yield return RwQuit2();
        if (phase == "lvfreq") yield return RwLevelFreq();
        if (phase == "rushlow") yield return RwRushLow();
        if (phase == "titan") yield return RwTitan();
        if (phase == "maprush") yield return RwMapRush();
        if (phase == "maprushreal") yield return RwMapRushReal();
        if (phase == "shots") yield return RwShots();
        if (phase == "shotsrh") yield return RwShotsReturnHome();
    }

    // 説明の文の撮影(レビュー用の画像と、縦横で重ならないかの確認)。起動の画面の大きさで撮る
    IEnumerator RwShots()
    {
        // 2026-10-15 第3弾: 全言語(-tsLangs all)で収まりを数値で確かめ、画像は -tsShotLangs の言語だけ撮る
        string res = $"{Screen.width}x{Screen.height}";
        string langsArg = Arg("-tsLangs", "ja,en");
        var langs = langsArg == "all" ? Loc.Languages.Select(l => l.code).ToArray() : langsArg.Split(',');
        var shotLangs = Arg("-tsShotLangs", "ja,en,de,ar,my,ta").Split(',');
        foreach (var lang in langs)
        {
            Loc.Set(lang, false);
            bool shoot = shotLangs.Contains(lang);
            yield return OpenStageSelectForTest();
            CpuParty.Request.Clear(); CpuParty.Request.Add("archer"); CpuParty.Request.Add("mage");
            SupportDeparturePanel.DebugOpen(true);
            yield return new WaitForSecondsRealtime(0.6f);
            if (shoot) { Shot($"rw_{lang}_{res}_support"); yield return Frames(3); }
            // 説明 + 注意書きの下に、仲間の一覧が少なくとも2行(スクロール)見えること
            var pr = SupportDeparturePanel.LastPanelRect; float bottom = SupportDeparturePanel.LastDescBottom;
            float listRoom = pr.yMax - 150f * Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f)) - bottom;
            float rowH = 58f * Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
            L($"[shots] {lang} {res} support: description bottom {bottom:F0}px, list room {listRoom:F0}px ({listRoom / Mathf.Max(1f, rowH):F1} rows)");
            Check(listRoom >= rowH * 2f, $"{lang} {res}: the CPU support description leaves room for the ally list ({listRoom / Mathf.Max(1f, rowH):F1} rows)");
            SupportDeparturePanel.DebugOpen(false);
            yield return new WaitForSecondsRealtime(0.4f);
            SprintDeparturePanel.DebugOpen(true);
            yield return new WaitForSecondsRealtime(0.6f);
            if (shoot) { Shot($"rw_{lang}_{res}_sprint"); yield return Frames(3); }
            // 説明の最後(到着の Lv の文)までスクロールで読める
            var ds = SprintDeparturePanel.DescScrollForTest;
            if (ds != null && ds.CanScroll)
            {
                ds.y = 99999f; // 一番下へ(Begin で範囲に収まる)
                yield return Frames(3);
                if (shoot) { Shot($"rw_{lang}_{res}_sprint_end"); yield return Frames(3); }
                L($"[shots] {lang} {res} sprint description scrolls to the end (offset {ds.y:F0}px)");
                Check(ds.y > 0f, $"{lang} {res}: the sprint description can be scrolled to its last line");
                ds.y = 0f;
            }
            else L($"[shots] {lang} {res} sprint description fits without scrolling");
            SprintDeparturePanel.DebugOpen(false);
            yield return new WaitForSecondsRealtime(0.4f);
            gm.CloseStageSelect();
            yield return new WaitForSecondsRealtime(1.0f);
            if (shoot)
            {
                NetDebugUI.OpenPanel();
                typeof(NetDebugUI).GetField("screen", RwNP).SetValue(FindFirstObjectByType<NetDebugUI>(), NetDebugUI.MultiScreen.Mode);
                yield return new WaitForSecondsRealtime(0.8f); Shot($"rw_{lang}_{res}_coop"); yield return Frames(3);
                typeof(NetDebugUI).GetProperty("PanelOpen").GetSetMethod(true).Invoke(null, new object[] { false });
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }
        CpuParty.Request.Clear();
    }

    // 一時停止の「RETURN TO HOME」の確認(2026-10-15 第3弾、ID 10010): 1km のボスでチェックポイント → 1.5km で開く
    IEnumerator RwShotsReturnHome()
    {
        string res = $"{Screen.width}x{Screen.height}";
        foreach (var lang in Arg("-tsLangs", "ja,en").Split(','))
        {
            Loc.Set(lang, false);
            yield return BeginRun(RwChar, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            var r = new List<RwBossResult>();
            yield return RwGate(1000f, "rh 1km", r);
            WarpTo(1500f);
            yield return new WaitForSecondsRealtime(1f);
            typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, true);
            typeof(GameManager).GetField("showReturnHomeConfirm", RwNP).SetValue(gm, true);
            yield return new WaitForSecondsRealtime(0.8f); Shot($"rh_{lang}_{res}_returnhome"); yield return Frames(3);
            typeof(GameManager).GetField("showReturnHomeConfirm", RwNP).SetValue(gm, false);
            typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, false);
            yield return EndRun();
        }
    }

    string RwChar => Arg("-rwChar", "swordsman");
    int rwShown; float rwDiagAt;
    void RwSetShare(int n) => typeof(GameManager).GetProperty("MileShareCount").GetSetMethod(true).Invoke(gm, new object[] { n });
    void RwClearCarry() => typeof(GameManager).GetField("mileCarry", RwNP).SetValue(gm, 0f);
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
            for (int i = 0; i < 121; i++) gm.RegisterEnemyKill(1f, 1f, false);
            gm.RegisterBossDefeat(301f, 0f, false);
            float expectE = 121f / n * mul, expectB = 301f / n * gm.BossMileGainMultiplier * FinalEvolution.MileMul;
            int gotE = gm.RunEnemyMile - m0, gotB = gm.RunBossMile - b0;
            float total = gotE + gotB + gm.MileCarryDebug;
            L($"[rw] share 1/{n}: 121 kills x 1 MILE + boss 301 (base 422) -> enemy +{gotE} boss +{gotB} carry {gm.MileCarryDebug:F3} = {total:F3} (exact {expectE + expectB:F3})");
            Check(Mathf.Abs(total - (expectE + expectB)) < 0.01f, $"share 1/{n}: integers + carry = the exact split (nothing lost, nothing doubled)");
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
    IEnumerator RwGate(float gate, string label, List<RwBossResult> outList, bool strict = true)
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
        if (strict) Check(r.bosses == n && n > 0, $"{label}: every boss counted once ({r.bosses}/{n})"); // 天空の 1km の竜は撃破の数え始めが遅れることがある(試験の数え方)
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

    // ---- 中断/再開: ラン中の状態がチェックポイントの時点へそろう(2026-10-15 第2弾) ----
    struct RwSnap { public float d, exp, carry; public int lv, em, bm, kills, bkills, cards, lives, pend; public string party; }
    RwSnap RwFromData(RunCheckpoint.Data x) => new RwSnap { d = x.checkpointDistance, exp = x.exp, carry = x.mileCarry, lv = x.level, em = x.runEnemyMile, bm = x.runBossMile, kills = x.enemyKillCount, bkills = x.bossKillCount, cards = x.upgradeHistoryCardIds.Count, lives = x.lives, pend = x.pendingLevelUps, party = RwParty(x.party) };
    RwSnap RwNow() => new RwSnap { d = gm.MaxDistance, exp = gm.Exp, carry = gm.MileCarryDebug, lv = gm.Level, em = gm.RunEnemyMile, bm = gm.RunBossMile, kills = gm.EnemyKillCount, bkills = gm.BossKillCount, cards = RwHistoryCount, lives = gm.Lives, pend = gm.PendingLevelUpCount, party = RwParty(CpuParty.Export()) };
    int RwHistoryCount => ((System.Collections.IList)typeof(GameManager).GetField("upgradeHistory", RwNP).GetValue(gm)).Count;
    static string RwParty(List<CpuParty.MemberState> p) => p == null ? "" : string.Join(";", p.Select(m => JsonUtility.ToJson(m)));
    static string RwS(RwSnap s) => $"d={s.d:F0} Lv{s.lv} exp={s.exp:F0} MILE e{s.em}/b{s.bm} carry={s.carry:F3} kills={s.kills}/{s.bkills} cards={s.cards} hp={s.lives} pendingLv={s.pend} party#{(s.party ?? "").GetHashCode():X}";
    // 仲間の状態は、再開の後に動き出すと変わる(HP/位置)ので、取ったカードの部分で比べる
    static string RwPartyCards(string party) => party == null ? "" : System.Text.RegularExpressions.Regex.Replace(party, "\"(hp|down|reviveIn)\":[^,}]*", "");
    bool RwSame(RwSnap a, RwSnap b) => Mathf.Abs(a.d - b.d) < 1f && a.lv == b.lv && Mathf.Abs(a.exp - b.exp) < 0.01f && a.em == b.em && a.bm == b.bm && Mathf.Abs(a.carry - b.carry) < 1e-4f && a.kills == b.kills && a.bkills == b.bkills && a.cards == b.cards && (a.lives == b.lives || a.pend > 0 || b.pend > 0) && RwPartyCards(a.party) == RwPartyCards(b.party);
    // (チェックポイントで順番待ちだったレベルアップは、再開の直後に出る = レベルアップの HP の回復で HP だけ変わる。順番待ちの数は下で別に見る)

    // 1km のボスでチェックポイント → 1.3〜2km で雑魚/EXP を進める → 2km のボスを倒して報酬の選択中
    IEnumerator RwProgressPastCheckpoint(string tag)
    {
        var res = new List<RwBossResult>();
        yield return RwGate(1000f, $"{tag} 1km (sets the checkpoint)", res);
        WarpTo(1300f);
        yield return new WaitForSecondsRealtime(1.5f);
        int k = 0; float t = 0f;
        while (k < 6 && t < 20f)
        {
            foreach (var e in RwEnemies().Where(x => Mathf.Abs(x.transform.position.x - pc.transform.position.x) < 30f).Take(6 - k).ToList()) { e.ApplyElementDamage(9999999, e.transform.position); k++; }
            yield return new WaitForSecondsRealtime(0.4f); t += 0.4f;
        }
        WarpTo(1930f);
        yield return WaitBossSpawn();
        autoPickHold = true;
        KillAllBosses();
        float tw = 0f; while (!gm.IsRewardSequenceWaitingForSelection && tw < 15f) { yield return null; tw += Time.unscaledDeltaTime; }
        L($"[rw] {tag} before the interrupt: {RwS(RwNow())} (killed {k} enemies + the 2km boss, reward choice open {gm.IsRewardSequenceWaitingForSelection})");
    }

    IEnumerator RwResume()
    {
        L("== resume = checkpoint state ==");
        foreach (int allies in new[] { 0, 2 })
        {
            string tag = allies == 0 ? "solo" : "cpu2";
            CpuParty.Request.Clear(); if (allies > 0) { CpuParty.Request.Add("archer"); CpuParty.Request.Add("mage"); }
            yield return BeginRun(RwChar, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            yield return RwProgressPastCheckpoint(tag);
            var cp = RwFromData(RunCheckpoint.Load());
            var now = RwNow();
            L($"[rw] {tag} checkpoint data: {RwS(cp)}");
            Check(now.em > cp.em || now.bm > cp.bm, $"{tag}: the run had progressed past the checkpoint (MILE e{cp.em}->{now.em} b{cp.bm}->{now.bm})");
            Check(RunCheckpoint.Load().runAtCheckpoint && gm.InterruptSavesLedgerOnly > 0, $"{tag}: interrupt saves only update the ledger ({gm.InterruptSavesLedgerOnly})");
            autoPickHold = false;
            double walked0 = RunLedger.Current != null ? RunLedger.Current.walked : -1, reached0 = RunLedger.Current != null ? RunLedger.Current.maxReached : -1;
            typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, false);
            var old = gm;
            gm.ReturnToHome();
            float w = 0f;
            while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.8f);
            gm = GameManager.Instance;
            int shownBefore = rwShown;
            yield return ResumeContinue("rw");
            stopKeepAlive = false; StartCoroutine(KeepAlive());
            var back = RwNow();
            L($"[rw] {tag} continued: {RwS(back)}");
            Check(RwSame(back, cp), $"{tag}: CONTINUE restores the checkpoint state (distance/EXP/Lv/MILE+carry/kills/cards/HP/CPU cards)");
            float tl = 0f; while (gm.PendingLevelUpCount > 0 || gm.LevelUpPending) { if ((tl += Time.unscaledDeltaTime) > 20f) break; yield return null; }
            L($"[rw] {tag}: level-up choices offered after CONTINUE {rwShown - shownBefore} (queued at the checkpoint {cp.pend})");
            Check(rwShown - shownBefore == cp.pend, $"{tag}: the level-ups queued at the checkpoint ({cp.pend}) are offered once after CONTINUE ({rwShown - shownBefore})");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            var res = new List<RwBossResult>();
            int bm0 = gm.RunBossMile;
            yield return RwGate(2000f, $"{tag} 2km again", res);
            Check(res.Count == 1 && gm.RunBossMile - bm0 == res[0].mile && res[0].bosses >= 1, $"{tag}: the 2km boss pays once after the re-run (+{gm.RunBossMile - bm0})");
            // 記録の控え: 走り直しの区間(1,000〜2,000m)は「実際に走った距離」へ二重に足さない(走った一番遠い所 {reached0} を越えた分だけ)
            double walked1 = RunLedger.Current != null ? RunLedger.Current.walked : -1, reached1 = RunLedger.Current != null ? RunLedger.Current.maxReached : -1;
            double beyond = System.Math.Max(0.0, reached1 - reached0);
            L($"[rw] {tag} ledger: walked {walked0:F0} -> {walked1:F0} (farthest {reached0:F0} -> {reached1:F0}, new ground {beyond:F0}m)");
            Check(walked1 - walked0 <= beyond + 5.0, $"{tag}: re-running the stretch after the checkpoint does not add to the walked distance twice (+{walked1 - walked0:F0}m, new ground {beyond:F0}m)");
            yield return EndRun();
        }
        CpuParty.Request.Clear();
    }

    // アプリの終了を想定: 報酬の選択中に OnApplicationPause(true) → テストの外でプロセスを止める → 次のプロセスで CONTINUE(-rwPhase quit2)
    IEnumerator RwQuit1()
    {
        L("== quit during the reward choice (1/2) ==");
        CpuParty.Request.Clear(); CpuParty.Request.Add("archer");
        yield return BeginRun(RwChar, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return RwProgressPastCheckpoint("quit");
        var cp = RwFromData(RunCheckpoint.Load());
        gm.SendMessage("OnApplicationPause", true);
        System.IO.File.WriteAllText(Arg("-rwShared", outDir) + "/rw_quit_expect.json", JsonUtility.ToJson(new RwSnapJ(cp)));
        L($"[rw] quit: paused the app with the reward choice open; expect on CONTINUE {RwS(cp)}");
    }
    [System.Serializable] class RwSnapJ { public float d, exp, carry; public int lv, em, bm, kills, bkills, cards, lives, pend; public string party; public RwSnapJ(RwSnap s) { d = s.d; exp = s.exp; carry = s.carry; lv = s.lv; em = s.em; bm = s.bm; kills = s.kills; bkills = s.bkills; cards = s.cards; lives = s.lives; pend = s.pend; party = s.party; } public RwSnap S => new RwSnap { d = d, exp = exp, carry = carry, lv = lv, em = em, bm = bm, kills = kills, bkills = bkills, cards = cards, lives = lives, pend = pend, party = party }; }
    IEnumerator RwQuit2()
    {
        L("== quit during the reward choice (2/2): new process ==");
        string p = Arg("-rwShared", outDir) + "/rw_quit_expect.json";
        Check(System.IO.File.Exists(p) && RunCheckpoint.HasActiveRun, "quit2: the suspended run is there after the restart");
        if (!System.IO.File.Exists(p)) yield break;
        var cp = JsonUtility.FromJson<RwSnapJ>(System.IO.File.ReadAllText(p)).S;
        yield return ResumeContinue("rw");
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        var back = RwNow();
        L($"[rw] quit2 continued: {RwS(back)} / expect {RwS(cp)}");
        Check(RwSame(back, cp), "quit2: after an app kill during the reward choice, CONTINUE restores the checkpoint state");
        Check(!gm.IsRewardSequenceWaitingForSelection, "quit2: no stale reward choice after the restart");
        // 死亡: 未確定の MILE / 記録が残らない
        int owned0 = gm.TotalOwnedMile; float best0 = gm.BestDistance; double life0 = ProgressStats.LifetimeDistance;
        int th0 = gm.TakeHomeMile;
        stopKeepAlive = true; typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        yield return null;
        for (int i = 0; i < 400 && !gm.IsGameOver; i++) { gm.TryDamagePlayer(false, "qa-rw-death", 99); yield return null; }
        yield return new WaitForSecondsRealtime(1.5f);
        L($"[rw] death: take-home was {th0}, owned {owned0}->{gm.TotalOwnedMile}, best {best0:F0}->{gm.BestDistance:F0}, lifetime {life0:F0}->{ProgressStats.LifetimeDistance:F0}, active run {RunCheckpoint.HasActiveRun}, commit {(gm.LastCommit != null ? gm.LastCommit.committed.ToString() : "none")}");
        Check(gm.IsGameOver && !gm.IsWin, "death: the run ends as a game over");
        Check(gm.TotalOwnedMile == owned0 && !RunCheckpoint.HasActiveRun && (gm.LastCommit == null || !gm.LastCommit.committed) && gm.BestDistance == best0 && ProgressStats.LifetimeDistance == life0, "death: no unconfirmed MILE, record or suspended run is kept");
        yield return EndRun();
        // 破棄(ホームの「はじめから出発」= RunCheckpoint.Clear)
        CpuParty.Request.Clear();
        yield return BeginRun(RwChar, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var res = new List<RwBossResult>();
        yield return RwGate(1000f, "discard 1km", res);
        owned0 = gm.TotalOwnedMile; best0 = gm.BestDistance; life0 = ProgressStats.LifetimeDistance;
        int th = gm.TakeHomeMile;
        typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, false);
        var old = gm; gm.ReturnToHome();
        float w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f); gm = GameManager.Instance;
        bool had = RunCheckpoint.HasActiveRun;
        RunCheckpoint.Clear(); // ホームの「はじめから出発」の確認と同じ1行
        L($"[rw] discard: take-home was {th}, owned {owned0}->{gm.TotalOwnedMile}, best {best0:F0}->{gm.BestDistance:F0}, lifetime {life0:F0}->{ProgressStats.LifetimeDistance:F0}, active {had}->{RunCheckpoint.HasActiveRun}");
        Check(had && !RunCheckpoint.HasActiveRun && gm.TotalOwnedMile == owned0 && gm.BestDistance == best0 && ProgressStats.LifetimeDistance == life0, "discard: no unconfirmed MILE or record is kept");
    }

    // ---- 疾走の到着からのレベルアップの頻度(到着の Lv を入れて、補助で普通に走る。ボスは実際の HP) ----
    IEnumerator RwLevelFreq()
    {
        L("== level-up pace after a sprint arrival ==");
        foreach (int km in Arg("-rwFreqKm", "10,50,90").Split(',').Select(int.Parse))
        {
            yield return BeginRun(RwChar, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            float arrival = km * 1000f - 250f;
            WarpTo(arrival);
            typeof(GameManager).GetMethod("ApplySprintArrivalLevel", RwNP).Invoke(gm, new object[] { arrival });
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            int lv0 = gm.Level; float t0 = Time.time, d0 = gm.MaxDistance;
            var ups = new List<string>(); int lastLv = lv0; int bk0 = gm.BossKillCount; float bossAt = -1f, bossDoneAt = -1f;
            float e0 = gm.TotalExpEarned, bossExp = 0f; int lastBk = gm.BossKillCount, ek0 = gm.EnemyKillCount; var bossNames = new List<string>();
            float limit = float.Parse(Arg("-rwFreqSec", "240"));
            while (Time.time - t0 < limit)
            {
                if (gm.Level != lastLv) { ups.Add($"Lv{gm.Level}@{Time.time - t0:F0}s/{gm.MaxDistance - d0:F0}m"); lastLv = gm.Level; }
                if (bossAt < 0f && Bm.IsBossPhase) bossAt = Time.time - t0;
                if (bossDoneAt < 0f && gm.BossKillCount > bk0) bossDoneAt = Time.time - t0;
                if (gm.BossKillCount != lastBk) { bossExp += gm.LastBossExpGranted * (gm.BossKillCount - lastBk); bossNames.Add($"{gm.LastBossExpGranted:F0}@{gm.MaxDistance / 1000f:F1}km"); lastBk = gm.BossKillCount; }
                yield return null;
            }
            float tot = gm.TotalExpEarned - e0, killExp = (gm.EnemyKillCount - ek0) * 15f;
            L($"[rw] pace {km}km EXP: total {tot:F0} = bosses {bossExp:F0} ({gm.BossKillCount - bk0}: {string.Join(" ", bossNames)}) + enemies ~{killExp:F0} ({gm.EnemyKillCount - ek0}) + distance ~{tot - bossExp - killExp:F0} ({gm.MaxDistance - d0:F0}m)");
            L($"[rw] pace {km}km: arrival Lv{lv0} -> Lv{gm.Level} in {limit:F0}s ({gm.MaxDistance - d0:F0}m, kills {gm.EnemyKillCount}, boss at {bossAt:F0}s defeated at {bossDoneAt:F0}s): {string.Join(", ", ups)}");
            yield return EndRun();
        }
    }

    // ---- 火力の低い構成(カード無し)でのラスダンのボスラッシュ。試験は倒さない(補助の自動攻撃だけ、無敵) ----
    IEnumerator RwRushLow()
    {
        string ch = RwChar;
        L($"== low-power rush ({ch}, no cards) ==");
        yield return BeginRun(ch, "last_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        WarpTo(89900f);
        typeof(GameManager).GetMethod("ApplySprintArrivalLevel", RwNP).Invoke(gm, new object[] { 89900f });
        float t0 = Time.time, limit = float.Parse(Arg("-rwRushSec", "720"));
        int k0 = gm.BossKillCount; float holdAt = -1f, lastLog = 0f;
        var defeats = new List<string>();
        int lastK = k0;
        while (Time.time - t0 < limit)
        {
            var enc = Bm.CurrentBossEncounter;
            if (gm.BossKillCount != lastK) { defeats.Add($"#{gm.BossKillCount - k0}@{Time.time - t0:F0}s/{gm.MaxDistance / 1000f:F1}km"); lastK = gm.BossKillCount; }
            if (holdAt < 0f && Bm.HoldsRun && gm.MaxDistance >= 98990f) holdAt = Time.time - t0;
            if (Time.time - lastLog > 30f) { lastLog = Time.time; L($"[rw]   {ch} t={Time.time - t0:F0}s d={gm.MaxDistance:F0} alive={Bm.AliveBossCount} defeated={gm.BossKillCount - k0} queue={(enc != null ? enc.Queued : -1)} holds={Bm.HoldsRun}"); }
            if (gm.BossKillCount - k0 >= 9 && !Bm.IsBossPhase) break;
            yield return null;
        }
        L($"[rw] low-power rush {ch}: defeated {gm.BossKillCount - k0}/9 in {Time.time - t0:F0}s, 99km hold from {holdAt:F0}s, reached {gm.MaxDistance:F0}m, {string.Join(", ", defeats)}");
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
            float baseD = gm.DistanceMileBase, baseB = gm.MileBaseDbg[2], baseE = gm.MileBaseDbg[1], exact = gm.TakeHomeExactDebug, carry = gm.MileCarryDebug;
            L($"[rw] cpu {n} detail: before split = distance {baseD:F3} + boss {baseB:F3} + enemy {baseE:F3} = {baseD + baseB + baseE:F3}; after split (1/{gm.MileShareCount}) = distance {gm.DistanceMileExact:F3} + boss {gm.MileExactDbg[2]:F3} + enemy {gm.MileExactDbg[1]:F3} = {exact:F3}; integers boss {gm.RunBossMile} enemy {gm.RunEnemyMile} carry {carry:F3}; take-home floor -> {hud}");
            Check(hud == Mathf.FloorToInt(exact + 1e-3f), $"cpu {n}: take-home = floor(exact split total) ({hud} = floor({exact:F3}))");
            gm.Win();
            float w = 0f; while (!gm.ResultShown && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            int got = gm.TotalOwnedMile - owned0;
            L($"[rw] cpu {n} (share 1/{gm.MileShareCount}): HUD take-home {hud}, result RunMile {gm.RunMile}, owned +{got} (distance {gm.RunDistanceMile} enemy {gm.RunEnemyMile} boss {gm.RunBossMile})");
            Check(hud == gm.RunMile && got == gm.RunMile, $"cpu {n}: HUD take-home = result = actually received ({hud} / {gm.RunMile} / {got})");
            // 広告の MILE 2倍(開発版の -monetizationMock 1 の時だけ): 分配の後の額を、ランごとに1回だけ
            if (Monetization.Mode == MonetizationMode.Mock)
            {
                int o1 = gm.TotalOwnedMile, db = gm.DoubleBase;
                var claim = typeof(GameManager).GetMethod("StartDoubleClaim", RwNP);
                claim.Invoke(gm, new object[] { true }); claim.Invoke(gm, new object[] { true });
                yield return null;
                L($"[rw] cpu {n} ad x2: base {db} (RunMile {gm.RunMile}), owned {o1} -> {gm.TotalOwnedMile}");
                Check(db == gm.RunMile && gm.TotalOwnedMile == o1 + db, $"cpu {n}: MILE x2 adds the split amount once ({gm.TotalOwnedMile - o1} = {db})");
            }
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

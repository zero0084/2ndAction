#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 各マップの終盤のボスラッシュ(2026-10-15、案A)の確認。 -qaRewards <dir> -rwPhase maprush|maprushreal [-rwStages wasteland_road,natural_cave,sky_corridor]
//  maprush     … 流れ(試験が倒す): 見出し / 予定の9体と順番 / 同時に1体 / ふだんの関門と重ならない / 報酬が1回ずつ / 速く走って節目をまとめて越える /
//                99km の足止め(攻撃・ジャンプが使える、ボスが届く所に居る、死神が出ない)/ 全滅 → 報酬 → 101km の関門 / 100km の死神 / 中断と再開
//  maprushreal … 普通の育成の構成(mix の Lv5、Lv50)で、補助の自動攻撃だけでクリアの時間とカードの選択の頻度(無敵)。-rwChar で近接/遠距離
public partial class QaSweep
{
    IEnumerator RwMapRush()
    {
        foreach (var st in Arg("-rwStages", "wasteland_road,natural_cave,sky_corridor").Split(','))
        {
            yield return RwMapRushFlow(st);
            yield return RwMapRushResume(st);
        }
    }

    // ワープで 89,900m へ → ラッシュが始まるまで
    IEnumerator RwMapRushBegin(string st, float from = 89900f)
    {
        yield return BeginRun(RwChar, st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(from);
        float w = 0f;
        while (!Bm.MapRushActive && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    List<WildBossBase> RwRushAlive() => WildAlive().Where(b => !(b is ReaperSisterBoss)).ToList();
    // 出てきたラッシュのボスを順に記録(名前と MILE)。補助の自動攻撃で倒れた物も数える
    readonly List<WildBossBase> rwSeen = new List<WildBossBase>(); readonly List<(string name, int mile)> rwSeenInfo = new List<(string, int)>();
    void RwTrackRush() { foreach (var b in RwRushAlive()) if (!rwSeen.Contains(b)) { rwSeen.Add(b); rwSeenInfo.Add((b.bossName, b.mileReward)); } }

    IEnumerator RwMapRushFlow(string st)
    {
        L($"== map rush flow: {st} ==");
        int bossSpawns0 = 0;
        yield return RwMapRushBegin(st);
        var enc = Bm.CurrentBossEncounter;
        Check(Bm.MapRushActive && enc != null && enc.mapRush, $"{st}: the map rush starts at 90km ({gm.MaxDistance:F0}m)");
        if (enc == null) { yield return EndRun(); yield break; }
        var want = BossManager.MapRushPlan(st).ToList();
        Check(enc.plan.SequenceEqual(want) && enc.total == 9, $"{st}: the 9 bosses in order ({string.Join(" > ", enc.plan.Select(k => k.Substring(k.IndexOf('/') + 1)))})");
        int m0 = gm.RunBossMile, k0 = gm.BossKillCount, lv0 = gm.Level, shown0 = rwShown;
        float d0 = gm.MaxDistance;
        rwSeen.Clear(); rwSeenInfo.Clear();
        rwChoiceBetween = rwChoiceWithBoss = 0; int rewards0 = gm.BossRewardChoicesStarted;
        StartCoroutine(RwWatchRushChoices()); // ラッシュの間ずっと: 選択が開いた時にボスが出ているか
        // 1体目が出て、距離が進み続ける
        float w = 0f; while (RwRushAlive().Count < 1 && w < 10f) { RwTrackRush(); yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(2f); RwTrackRush();
        Check(!Bm.HoldsRun && gm.MaxDistance > d0 + 5f, $"{st}: the distance keeps going during the rush ({d0:F0} -> {gm.MaxDistance:F0}m)");
        // 速く走って節目をまとめて越える(1体目を生かしたまま 94.5km まで)→ 予約は順番に全部、出ているのは1体だけ
        int maxAlive = 0;
        PlayerController.DebugSpeedScale = 4f;
        w = 0f; while (gm.MaxDistance < 94500f && w < 120f) { RwTrackRush(); maxAlive = Mathf.Max(maxAlive, RwRushAlive().Count); yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        L($"[mr] {st} at {gm.MaxDistance:F0}m with the first boss alive: reserved {enc.milestonesReserved}, queue {enc.Queued}, alive {RwRushAlive().Count} (max {maxAlive}), catch-up max {enc.maxCatchUp}");
        Check(maxAlive <= 1 && enc.milestonesReserved == 5 && enc.Queued == 4, $"{st}: one boss at a time; crossing 91-94km reserves 4 in order ({enc.milestonesReserved}/{enc.Queued})");
        // 99km へ(まだ倒さない)→ 足止め
        PlayerController.DebugSpeedScale = 4f;
        w = 0f; while (!enc.held99 && w < 120f) { RwTrackRush(); maxAlive = Mathf.Max(maxAlive, RwRushAlive().Count); yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        yield return new WaitForSeconds(2f);
        float dh0 = gm.MaxDistance; yield return new WaitForSeconds(2f); float dh1 = gm.MaxDistance;
        Check(enc.held99 && Bm.HoldsRun && Mathf.Abs(dh1 - dh0) < 0.5f && dh0 <= 99000.5f && enc.milestonesReserved == 9, $"{st}: 99km holds the run with bosses remaining (d {dh0:F0}->{dh1:F0}, reserved {enc.milestonesReserved}, remaining {enc.Remaining})");
        Check(!Bm.DeathSpawned, $"{st}: no reaper while the rush remains");
        // 足止めの間: 攻撃 / ジャンプ / 後ろへの攻撃 が使える、ボスが画面の中・届く所に居る
        yield return RwHoldControls(st);
        // 1体ずつ倒す(順番どおり / 報酬が1回ずつ / 同時に1体)。2026-10-15 第4弾: レベルアップの選択はボスとボスの間に出る
        w = 0f;
        while (Bm.IsBossPhase && w < 240f)
        {
            RwTrackRush();
            maxAlive = Mathf.Max(maxAlive, RwRushAlive().Count);
            var a = RwRushAlive().Where(b => !b.IsEntering).ToList();
            if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); yield return new WaitForSeconds(0.5f); }
            yield return null; w += Time.unscaledDeltaTime;
        }
        var order = rwSeenInfo.Select(x => x.name).ToList(); int mileSum = rwSeenInfo.Sum(x => x.mile);
        float tw = 0f; while ((gm.IsRewardSequenceRunning || gm.LevelUpPending || gm.PendingLevelUpCount > 0) && tw < 240f) { yield return null; tw += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        L($"[mr] {st} after the rush: waited {tw:F0}s for the choices, pending {gm.PendingLevelUpCount}, choice open {gm.LevelUpPending}");
        int got = gm.RunBossMile - m0, kills = gm.BossKillCount - k0;
        L($"[mr] {st} defeated in order: {string.Join(" > ", order)} | boss MILE +{got} (sum of rewards {mileSum}) kills {kills} | Lv {lv0}->{gm.Level}, choices {rwShown - shown0} | next gate {Bm.NextBossDistance:F0}m | max alive {maxAlive}");
        Check(order.Count == 9 && order.SequenceEqual(want.Select(k => k.Substring(k.IndexOf('/') + 1))), $"{st}: appeared one by one in the planned order ({order.Count})");
        Check(kills == 9 && got == mileSum, $"{st}: each boss pays once ({kills} kills, +{got} = {mileSum})");
        Check(rwShown - shown0 == gm.Level - lv0 + 1, $"{st}: one rush reward + one choice per level-up ({rwShown - shown0} = {gm.Level - lv0} + 1)");
        Check(maxAlive <= 1, $"{st}: never more than one rush boss at once ({maxAlive})");
        int choicesWithBoss = rwChoiceWithBoss, choicesBetween = rwChoiceBetween;
        L($"[mr] {st} choices: opened between bosses {choicesBetween}, with a rush boss out {choicesWithBoss}, boss rewards {gm.BossRewardChoicesStarted - rewards0}");
        Check(choicesWithBoss == 0, $"{st}: no level-up choice opens while a rush boss is out ({choicesWithBoss})");
        Check(choicesBetween > 0, $"{st}: level-up choices are offered between the rush bosses ({choicesBetween})");
        Check(gm.BossRewardChoicesStarted - rewards0 == 1, $"{st}: one boss reward for the whole rush ({gm.BossRewardChoicesStarted - rewards0})");
        Check(!Bm.IsBossPhase && Mathf.Abs(Bm.NextBossDistance - 101000f) < 1f, $"{st}: after the rush the next gate is 101km ({Bm.NextBossDistance:F0})");
        // 100km の死神(ふだんの関門は 99/100km に出ない)
        int phases0 = 0; bool sawGate = false;
        PlayerController.DebugSpeedScale = 2f;
        w = 0f; while (!Bm.DeathSpawned && w < 60f) { if (Bm.IsBossPhase) sawGate = true; yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        Check(Bm.DeathSpawned && !sawGate, $"{st}: the reaper chase starts at 100km after the rush, no extra boss gate on the way ({gm.MaxDistance:F0}m, gate seen {sawGate})");
        Check(SprintRecords.IsGateCleared(st, 90) || DebugRun.WritesBlocked, $"{st}: clearing the rush counts as the 90km gate for sprint departures");
        yield return EndRun();
    }

    int rwChoiceBetween, rwChoiceWithBoss;
    IEnumerator RwWatchRushChoices()
    {
        bool was = false;
        while (Bm != null && Bm.IsBossPhase)
        {
            bool open = gm.LevelUpPending;
            if (open && !was && !Bm.BossDefeatedThisPhase) { if (RwRushAlive().Any(b => !b.IsEntering || true)) rwChoiceWithBoss += RwRushAlive().Count > 0 ? 1 : 0; if (RwRushAlive().Count == 0) rwChoiceBetween++; }
            was = open;
            yield return null;
        }
    }

    // 99km の足止めの間の操作(ジャンプ / 前の攻撃 / 後ろの攻撃)と、ボスの位置
    IEnumerator RwHoldControls(string st)
    {
        // 2026-10-15 第4弾: 足止めの時はボスとボスの間の選択が先に出ることがある → 選択が終わり、次のボスが出るまで待つ
        float wb = 0f; while ((RwRushAlive().Count == 0 || RwRushAlive().Any(b => b.IsEntering)) && wb < 40f) { yield return null; wb += Time.unscaledDeltaTime; } // 登場の歩み寄りの後から測る
        yield return new WaitForSeconds(1.0f);
        var cam = Camera.main;
        float y0 = pc.transform.position.y, yMax = y0; bool attacked = false, backAttacked = false;
        // 被弾ののけぞりの間は入力を受け付けない(ゲームの決まり)→ のけぞりが解けてから跳ぶ。跳べるまで数回試す
        int jumpTries = 0;
        for (; jumpTries < 6 && yMax - y0 < 0.8f; jumpTries++)
        {
            float wr = 0f; while (pc.IsReacting && wr < 3f) { yield return null; wr += Time.deltaTime; }
            y0 = pc.transform.position.y; yMax = y0;
            StartCoroutine(Flick(PlayerController.FlickDirection.Up));
            for (int i = 0; i < 40; i++) { yMax = Mathf.Max(yMax, pc.transform.position.y); yield return null; }
        }
        L($"[mr] {st} hold jump tries {jumpTries}");
        yield return new WaitForSeconds(0.6f);
        // 2026-10-15 第4弾: 前の攻撃の最中/のけぞりの間に払うと入力が通らない(ゲームの決まり)→ 攻撃が終わるのを待ち、通るまで数回試す
        int atkTries = 0, backTries = 0;
        for (; atkTries < 4 && !attacked; atkTries++)
        {
            float wa = 0f; while ((pc.IsAttacking || pc.IsReacting) && wa < 3f) { yield return null; wa += Time.deltaTime; }
            StartCoroutine(Flick(PlayerController.FlickDirection.Forward));
            for (int i = 0; i < 40; i++) { attacked |= pc.IsAttacking; yield return null; }
        }
        for (; backTries < 4 && !backAttacked; backTries++)
        {
            float wa = 0f; while ((pc.IsAttacking || pc.IsReacting) && wa < 3f) { yield return null; wa += Time.deltaTime; }
            yield return new WaitForSeconds(0.2f);
            StartCoroutine(Flick(PlayerController.FlickDirection.Backward));
            for (int i = 0; i < 40; i++) { backAttacked |= pc.IsAttacking; yield return null; }
        }
        L($"[mr] {st} hold attack tries {atkTries} / back attack tries {backTries}");
        // ボスの位置を 6秒(ボスが居るフレームだけ。テストの攻撃で倒れて次のボスの前の選択に入ったら、次のボスを待って測る: 最長 40秒)
        float dxMax = 0f; int inView = 0, frames = 0, freeGapFrames = 0;
        float t0 = Time.time, measured = 0f;
        while (measured < 6f && Time.time - t0 < 40f)
        {
            if (RwRushAlive().Count > 0 && !RwRushAlive().Any(b => b.IsEntering)) measured += Time.deltaTime;
            foreach (var b in RwRushAlive().Where(x => !x.IsEntering)) // 登場の歩み寄りの間は数えない
            {
                frames++;
                // 技でわざと離れている間(サーペントが潜って離れて戻る 等、間合いの制限を外す技)は距離に数えない。画面の中かどうかは全部のフレームで見る
                if (!b.DebugFreeGap) dxMax = Mathf.Max(dxMax, Mathf.Abs(b.CenterWorld.x - pc.transform.position.x));
                else freeGapFrames++;
                var vp = cam != null ? cam.WorldToViewportPoint(b.CenterWorld) : new Vector3(0.5f, 0.5f, 1f);
                if (vp.x > -0.05f && vp.x < 1.05f) inView++;
            }
            yield return null;
        }
        L($"[mr] {st} during the 99km hold ({string.Join(",", RwRushAlive().Select(b => b.bossName))}): jump +{yMax - y0:F1}m, attack {attacked}, back attack {backAttacked}, boss distance max {dxMax:F1}m (frames in a move that leaves on purpose {freeGapFrames}), in view {inView}/{frames}");
        Check(yMax - y0 > 0.8f && attacked && backAttacked, $"{st}: jump / attack / back attack work during the 99km hold");
        Check(frames > 0 && inView >= frames * 0.95f && dxMax < 16f, $"{st}: the boss stays within reach during the hold (max {dxMax:F1}m, in view {inView}/{frames})");
    }

    // 中断と再開: 3体倒した所でホームへ → CONTINUE → ラッシュは最初から(予約の欠落なし)、報酬は二重にならない
    IEnumerator RwMapRushResume(string st)
    {
        L($"== map rush suspend/resume: {st} ==");
        yield return BeginRun(RwChar, st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var res = new List<RwBossResult>();
        yield return RwGate(89000f, $"{st} 89km (checkpoint before the rush)", res, strict: false);
        var cp = RunCheckpoint.Load();
        float w = 0f;
        PlayerController.DebugSpeedScale = 3f;
        while (!Bm.MapRushActive && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        int killed = 0; w = 0f;
        while (killed < 3 && w < 90f)
        {
            var a = RwRushAlive().Where(b => !b.IsEntering).ToList();
            if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); killed++; yield return new WaitForSeconds(0.6f); }
            else { PlayerController.DebugSpeedScale = 2f; yield return null; }
            w += Time.unscaledDeltaTime;
        }
        PlayerController.DebugSpeedScale = 1f;
        int bmMid = gm.RunBossMile;
        L($"[mr] {st} killed {killed} rush bosses (boss MILE {cp.runBossMile} -> {bmMid}) - suspending");
        typeof(GameManager).GetField("showPauseMenu", RwNP).SetValue(gm, false);
        var old = gm; gm.ReturnToHome();
        w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f); gm = GameManager.Instance;
        yield return ResumeContinue("rw");
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        Check(Mathf.Abs(gm.MaxDistance - cp.checkpointDistance) < 1f && gm.RunBossMile == cp.runBossMile && !Bm.MapRushActive, $"{st}: CONTINUE goes back to the checkpoint before the rush ({gm.MaxDistance:F0}m, boss MILE {gm.RunBossMile})");
        PlayerController.DebugSpeedScale = 3f;
        w = 0f; while (!Bm.MapRushActive && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        var enc = Bm.CurrentBossEncounter;
        Check(enc != null && enc.mapRush && enc.total == 9 && enc.plan.Count == 9, $"{st}: the rush starts again from the beginning with all 9 bosses ({enc?.total})");
        int m1 = gm.RunBossMile, k1 = gm.BossKillCount;
        rwSeen.Clear(); rwSeenInfo.Clear(); // 出てきたボスごとに1回(フェニックスは一度蘇るので、倒した回数では数えない)
        w = 0f;
        while (Bm.IsBossPhase && w < 300f)
        {
            RwTrackRush();
            var a = RwRushAlive().Where(b => !b.IsEntering).ToList();
            if (a.Count > 0) { a[0].TakeDamage(99999999, a[0].CenterWorld); yield return new WaitForSeconds(0.5f); }
            else PlayerController.DebugSpeedScale = 3f;
            yield return null; w += Time.unscaledDeltaTime;
        }
        PlayerController.DebugSpeedScale = 1f;
        int n = rwSeenInfo.Count, mileSum = rwSeenInfo.Sum(x => x.mile);
        Check(n == 9 && gm.BossKillCount - k1 == 9 && gm.RunBossMile - m1 == mileSum, $"{st}: after CONTINUE the 9 bosses pay once each ({n} kills, +{gm.RunBossMile - m1} = {mileSum}; total boss MILE {gm.RunBossMile})");
        yield return EndRun();
    }

    // 普通の育成の構成でのクリアの時間(補助の自動攻撃だけ、無敵、実際の HP)
    IEnumerator RwMapRushReal()
    {
        string ch = RwChar;
        int lv = int.Parse(Arg("-rwBuildLv", "5"));
        foreach (var st in Arg("-rwStages", "wasteland_road,natural_cave,sky_corridor").Split(','))
        {
            L($"== map rush real: {st} {ch} (mix Lv{lv}) ==");
            yield return BeginRun(ch, st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            int killUntil = int.Parse(Arg("-rwRushKillUntil", "0"));
            bool manual = Arg("-rwManual", "0") == "1", detail = Arg("-rwFightLog", killUntil > 0 ? "1" : "0") == "1", holdLast = Arg("-rwRushHoldLast", "0") == "1";
            rwLastPx = -1e9f;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(!manual);
            foreach (var id in CardBuildPresets.Find("mix")) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, lv); }
            WarpTo(89900f);
            typeof(GameManager).GetMethod("ApplySprintArrivalLevel", RwNP).Invoke(gm, new object[] { 89900f });
            float w = 0f; while (!Bm.MapRushActive && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            var enc = Bm.CurrentBossEncounter;
            float t0 = Time.time, holdAt = -1f, limit = float.Parse(Arg("-rwRushSec", "900"));
            int k0 = gm.BossKillCount, lv0 = gm.Level, shown0 = rwShown, lastK = k0; float lastKillT = 0f;
            var lines = new List<string>(); var choiceTimes = new List<string>(); int shownSeen = rwShown;
            string current = "";
            while (Time.time - t0 < limit && Bm.IsBossPhase)
            {
                var a = RwRushAlive(); if (a.Count > 0 && a[0].bossName != current) current = a[0].bossName;
                if (gm.BossKillCount != lastK) { float t = Time.time - t0; lines.Add($"{current}@{t:F0}s(+{t - lastKillT:F0}s,{gm.MaxDistance / 1000f:F1}km)"); lastKillT = t; lastK = gm.BossKillCount; }
                if (holdAt < 0f && Bm.HoldsRun && enc != null && enc.held99) holdAt = Time.time - t0;
                if (rwShown != shownSeen) { choiceTimes.Add($"{Time.time - t0:F0}s"); shownSeen = rwShown; }
                // 2026-10-16 にらみ合いの切り分け: -rwRushKillUntil N で最初の N 体は試験が倒す(N+1 体目からを補助/手動で戦う)
                int killedNow = gm.BossKillCount - k0;
                if (killedNow < killUntil)
                {
                    // -rwRushHoldLast 1: 最後に試験が倒すボス(killUntil 体目)は 99km の足止めが始まるまで残す → 次のボスが足止めの中で出る(にらみ合いが起きた条件)
                    bool keep = holdLast && killedNow == killUntil - 1 && !(enc != null && enc.held99);
                    PlayerController.DebugSpeedScale = 3f;
                    if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                    var ka = a.Where(x => !x.IsEntering && !x.IsDead).ToList();
                    if (!keep && ka.Count > 0 && Time.time >= rwKillAt) { rwKillAt = Time.time + 0.5f; ka[0].TakeDamage(99999999, ka[0].CenterWorld); }
                }
                else if (killUntil > 0 && PlayerController.DebugSpeedScale != 1f) { PlayerController.DebugSpeedScale = 1f; if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(!manual); }
                else if (a.Count > 0)
                {
                    var b = a[0];
                    // ダメージが入らなかった一番長い時間(登場/無敵の間は数えない)= にらみ合いの長さ
                    if (b != ndBoss) { ndBoss = b; ndHp = b.Hp; ndSince = Time.time; }
                    else if (b.Hp < ndHp || b.IsEntering || b.DebugInvulnerable) { if (b.Hp < ndHp) ndHits++; ndHp = b.Hp; ndSince = Time.time; }
                    else ndMax = Mathf.Max(ndMax, Time.time - ndSince);
                    // 手動の操作(-rwManual 1): 補助を切り、人が前攻撃を連打するのと同じ(0.45秒ごと)
                    // 飛んでいて高い所のボスには、人と同じく跳んでから前攻撃(地上の前攻撃は高さが届かない)
                    if (manual && Time.time >= rwManualAt && !pc.IsAttacking)
                    {
                        rwManualAt = Time.time + 0.45f;
                        bool high = b.AssistBounds.min.y > pc.transform.position.y + 1.4f;
                        if (high && pc.IsGrounded) { StartCoroutine(Flick(PlayerController.FlickDirection.Up)); rwManualAt = Time.time + 0.2f; rwManualJumps++; }
                        else StartCoroutine(Flick(PlayerController.FlickDirection.Forward));
                    }
                    if (detail && Time.time >= rwDetailAt)
                    {
                        rwDetailAt = Time.time + 1f;
                        var hb = b.AssistBounds; float px = pc.transform.position.x;
                        float dEdge = hb.min.x > px ? hb.min.x - px : (hb.max.x < px ? px - hb.max.x : 0f);
                        float vx = rwLastPx > -1e8f ? (px - rwLastPx) / 1f : 0f; rwLastPx = px;
                        var has = HighSpeedAssist.Instance;
                        L($"[mr] fight {b.bossName} t={Time.time - t0:F0}s hp={b.Hp}/{b.maxHp} act='{b.DebugAct}' sp={b.SpecialsUsed}/{b.SpecialsDeferred} gap={b.DebugGap:F1} front={b.DebugFrontDist:F1} edge={dEdge:F1} half={b.HalfWidth:F1} slot={b.slotIndex} relV={b.DebugRelVelocity:F1} move={b.MoveScale:F2} wait={b.AiWaitScale:F2} aiTime={ElementStatus.AiTimeScaleOf(b.gameObject):F2} freeGap={b.DebugFreeGap} invuln={b.DebugInvulnerable} targetable={b.AssistTargetable} dy={b.CenterWorld.y - pc.transform.position.y:F1} | player vx={vx:F1} run={pc.CurrentAutoRunSpeed:F1} attacking={pc.IsAttacking} lunge={pc.LungeScaleNow:F2} reach={pc.AssistForwardReach:F1} plan='{(has != null ? has.BossPlan : "-")}' held99={(enc != null && enc.held99)}");
                    }
                }
                // 2026-10-15 切り分け: 1体に 60秒以上かかっている時は 20秒ごとにボスの様子を出す(HP / 登場中 / 動き / 間合い / 無敵 / 画面の位置 / プレイヤーの攻撃)
                if (a.Count > 0 && Time.time - t0 - lastKillT > 60f && Time.time >= rwDiagAt)
                {
                    rwDiagAt = Time.time + 20f;
                    var b = a[0]; var cam = Camera.main; var vp = cam != null ? cam.WorldToViewportPoint(b.CenterWorld) : Vector3.zero;
                    L($"[mr] slow {b.bossName} t={Time.time - t0:F0}s hp={b.Hp} entering={b.IsEntering} act='{b.DebugAct}' sp={b.SpecialsUsed}/{b.SpecialsDeferred} gap={b.DebugGap:F1} freeGap={b.DebugFreeGap} invuln={b.DebugInvulnerable} vp=({vp.x:F2},{vp.y:F2}) playerAttacking={pc.IsAttacking} reacting={pc.IsReacting} inputLatched={UiInputGate.DebugLatched} choice={gm.IsRewardSequenceRunning}");
                }
                yield return null;
            }
            float total = Time.time - t0;
            float tw = 0f; while ((gm.IsRewardSequenceRunning || gm.LevelUpPending || gm.PendingLevelUpCount > 0) && tw < 240f) { if (rwShown != shownSeen) { choiceTimes.Add($"{Time.time - t0:F0}s"); shownSeen = rwShown; } yield return null; tw += Time.unscaledDeltaTime; }
            L($"[mr] real {st} {ch} mix Lv{lv}: {(Bm.IsBossPhase ? "NOT cleared" : "cleared")} in {total:F0}s ({total / 60f:F1} min), defeated {gm.BossKillCount - k0}/9, 99km hold from {(holdAt < 0f ? "-" : holdAt.ToString("F0") + "s")} (held {(holdAt < 0f ? 0f : total - holdAt):F0}s), Lv {lv0}->{gm.Level}, choices {rwShown - shown0} at [{string.Join(", ", choiceTimes)}]");
            L($"[mr] real {st} {ch}: {string.Join(" > ", lines)}");
            if (killUntil > 0)
            {
                L($"[mr] standoff {st} {ch} manual={manual}: longest time with no damage {ndMax:F1}s, damage ticks {ndHits}, manual jumps {rwManualJumps}, assist approach lunges {(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.BossApproachLunges : 0)}, specials deferred {(ndBoss != null ? ndBoss.SpecialsDeferred : 0)}");
                if (Arg("-rwStandoffCheck", "0") == "1")
                    Check(!Bm.IsBossPhase && ndMax < float.Parse(Arg("-rwStandoffMax", "25")), $"{st} {ch}{(manual ? " (manual)" : "")}: the last bosses are defeated with no long standoff (longest no-damage {ndMax:F1}s)");
            }
            ndBoss = null; ndMax = 0f; ndHits = 0; rwManualJumps = 0;
            yield return EndRun();
        }
    }
}
#endif

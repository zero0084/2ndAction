#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 成功の確定の途中で落ちた時の復旧の確認(2026-10-13 第3弾)。テスト用データ(SaveProfile の test:)だけを使う。
//  1本目: -qaCommitCrash <dir> -ccPhase run -qaCrashAt <地点> [-qaCrashFlush 1]
//         テスト用データを作り直し → ランキングに参加(通信できない状態 = 投稿待ちになる)→ 1,500m の成功ランを作る →
//         中断データを書く → 書く前の値を cc_baseline.json へ → 脱出成功 → 指定の地点でプロセスを強制終了(保存しない)
//  2本目: -qaCommitCrash <dir> -ccPhase verify -ccPoint <地点> -ccBase <1本目の cc_baseline.json>
//         テスト用データで起動し直し(控えがあれば復旧)、MILE/BEST/累計距離/解放/投稿待ち/確定の印/中断データを確かめる。
//         「before」(控えを書く前に落ちた)は何も確定していないこと → CONTINUE で再開して脱出 → 1回だけ確定、を確かめる
public partial class QaSweep
{
    [System.Serializable] class CcBase { public string runId; public int mile0, add; public double best0, life0, reached, walked; public bool elite0; public int pending0; public float bestDist0; }
    const string CcStage = "wasteland_road";

    IEnumerator CommitCrashMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        string phase = Arg("-ccPhase", "run");
        string mockDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(outDir.TrimEnd('/', '\\')), "cc_mock");
        System.IO.Directory.CreateDirectory(mockDir);
        var me = new MockBackend(mockDir, "player-me");
        MockBackend.SimulateOffline = true;
        if (phase == "run")
        {
            SaveProfile.Switch(true, true);
            Leaderboard.Backend = me;
            yield return ReloadHome();
            Leaderboard.Backend = me;
            TutorialProgress.MarkOffered();
            Leaderboard.Join();
            yield return BeginRun("swordsman", CcStage);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            WarpTo(1500f); yield return null; yield return null;
            RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
            RunLedger.DevSet(r => { r.maxReached = 1500; r.walked = 1500; r.playSeconds = 600; r.testProfile = false; r.startedUtc -= 700; });
            UnlockRules.OnRunDistance(CcStage, 1500);
            UnlockManager.CheckUnlocks(1500f);
            typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
            yield return null;
            var b = new CcBase
            {
                runId = RunLedger.Current.runId, mile0 = gm.TotalOwnedMile, add = gm.TakeHomeMile,
                best0 = GameManager.ReadStageBest(CcStage), life0 = ProgressStats.LifetimeDistance, reached = 1500, walked = 1500,
                elite0 = SaveStore.GetString("UnlockedIds", "").Contains("elite_goblin"), pending0 = Leaderboard.PendingCount,
                bestDist0 = SaveStore.GetFloat(GameManager.BestDistanceKey, 0f),
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "cc_baseline.json"), JsonUtility.ToJson(b, true));
            L($"[cc] run {b.runId.Substring(0, 8)} mile0={b.mile0} add={b.add} best0={b.best0:F0} life0={b.life0:F0} elite0={b.elite0} pending0={b.pending0} crashAt='{CommitJournal.DevCrashAt}' flush={CommitJournal.DevCrashFlush}");
            gm.Win();
            yield return new WaitForSecondsRealtime(1f);
            L($"[cc] no crash (point '{CommitJournal.DevCrashAt}'): committed={gm.LastCommit != null && gm.LastCommit.committed} mile={gm.TotalOwnedMile}");
            yield break;
        }

        // ---- verify ----
        string point = Arg("-ccPoint", "");
        var bb = JsonUtility.FromJson<CcBase>(System.IO.File.ReadAllText(Arg("-ccBase", "")));
        bool journalBefore = CommitJournal.Pending; // 通常のデータ側(起動時)には控えは無い
        SaveProfile.Switch(true, false); // テスト用データで起動し直す(控えがあればここで復旧)
        Leaderboard.Backend = me;
        yield return ReloadHome();
        Leaderboard.Backend = me;
        int mile = SaveStore.GetInt(GameManager.TotalMileKey, 0);
        double best = GameManager.ReadStageBest(CcStage), life = ProgressStats.LifetimeDistance;
        bool committed = RunLedger.IsCommitted(bb.runId), elite = SaveStore.GetString("UnlockedIds", "").Contains("elite_goblin");
        int pending = Leaderboard.PendingCount;
        bool active = RunCheckpoint.HasActiveRun;
        string activeRun = active && RunCheckpoint.Load().ledger != null ? RunCheckpoint.Load().ledger.runId : "";
        L($"[cc] verify point='{point}' recovered={CommitJournal.Recovered} ({CommitJournal.LastRecovery}) normal-data journal={journalBefore}");
        L($"[cc] mile {bb.mile0}->{mile} (expected +{bb.add}) best {bb.best0:F0}->{best:F0} life {bb.life0:F0}->{life:F0} committed={committed} elite={elite} pending {bb.pending0}->{pending} activeRun={active}({(activeRun.Length >= 8 ? activeRun.Substring(0, 8) : activeRun)}) journal={CommitJournal.Pending}");
        Check(!journalBefore, "the normal data has no commit journal (the test only touched the test data)");
        Check(!CommitJournal.Pending, "no journal is left after starting up");
        if (point == "before")
        {
            // 控えを書く前に落ちた: 何も確定していない → 中断データから再開できる → 脱出で1回だけ確定
            Check(!committed && mile == bb.mile0 && System.Math.Abs(best - bb.best0) < 0.5 && System.Math.Abs(life - bb.life0) < 0.5 && pending == bb.pending0, "before: nothing was committed or paid");
            Check(active && activeRun == bb.runId, "before: the suspended run can be resumed (same run)");
            yield return ResumeContinue("cc-before");
            RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
            RunLedger.DevSet(r => { r.testProfile = false; });
            int pay = gm.TakeHomeMile;
            gm.Win();
            yield return WaitTut(() => gm.ResultShown, 6f);
            yield return new WaitForSecondsRealtime(0.5f);
            int mile2 = SaveStore.GetInt(GameManager.TotalMileKey, 0);
            L($"[cc] before -> resumed and escaped: mile {bb.mile0}->{mile2} (+{pay}) committed={RunLedger.IsCommitted(bb.runId)} pending={Leaderboard.PendingCount}");
            Check(RunLedger.IsCommitted(bb.runId) && mile2 == bb.mile0 + pay, "before: after resuming, the escape is committed and paid once");
            Check(!RunCheckpoint.HasActiveRun, "before: the finished run cannot be resumed again");
            yield return EndRun();
        }
        else
        {
            Check(committed, $"{point}: the run is committed");
            Check(mile == bb.mile0 + bb.add, $"{point}: MILE paid exactly once ({mile - bb.mile0} of {bb.add})");
            // 準備で 1,500m にした後、脱出の入力までに数m 走る(確定の値はその分だけ大きい)。二重なら 3,000m 以上になる
            Check(best >= bb.reached - 0.5 && best < bb.reached + 20.0, $"{point}: the map BEST is written ({best:F0})");
            Check(life - bb.life0 >= bb.walked - 0.5 && life - bb.life0 < bb.walked + 20.0, $"{point}: the lifetime distance is added once (+{life - bb.life0:F0})");
            Check(elite, $"{point}: the distance unlock is saved");
            Check(pending == bb.pending0 + 1, $"{point}: one ranking post is waiting ({pending - bb.pending0})");
            Check(!active, $"{point}: the finished run cannot be resumed (no second reward)");
            // もう一度起動しても何も増えない(二重の反映が無い)
            SaveProfile.Switch(true, false);
            yield return ReloadHome();
            int mile3 = SaveStore.GetInt(GameManager.TotalMileKey, 0);
            Check(mile3 == mile && Leaderboard.PendingCount == pending && System.Math.Abs(ProgressStats.LifetimeDistance - life) < 0.5, $"{point}: a second start-up changes nothing (mile {mile3}, pending {Leaderboard.PendingCount})");
        }
        SaveProfile.Switch(false, false);
        yield return ReloadHome();
    }
}
#endif

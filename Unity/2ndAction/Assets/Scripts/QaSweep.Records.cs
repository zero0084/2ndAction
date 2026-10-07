#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ランキング/コードネーム/オート/障害物(2026-10-08)の自動テスト: -qaRecords <dir>(テスト用データの中で行う)
//  R ランキング(模擬サーバー = 同じ検査の規則。実際のサービスの確認ではない):
//    参加前は送らない/参加で資格のある自己ベストを送る/自己ベスト更新/短い記録で上書きしない/同じ距離は同じ順位/別プレイヤー/
//    通信失敗は投稿待ちに残して再送(重複しない)/同じ runId はサーバーが拒否/デバッグ・テストデータ・旧データ・マルチは対象外/端末の値の検査
//  N コードネーム: 整え方(空白/改行/制御文字/タグ/長さ)、保存だけでは投稿しない、名前を変えても本人は同じ
//  A オートの個別設定: 保存と既定値、攻撃/回避の振り分け、ボス戦 OFF
//  O 障害物: 無効な体当たりは被弾に数えない(破壊の演出で消える)
public partial class QaSweep
{
    IEnumerator RecordsModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        string dir = System.IO.Path.Combine(outDir, "mockserver");
        if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        var me = new MockBackend(dir, "player-me");
        var other = new MockBackend(dir, "player-other");
        Leaderboard.Backend = me;
        Leaderboard.DevReset(); Leaderboard.Backend = me;

        // ---- N コードネーム
        L("== N codename ==");
        Check(Codename.Sanitize("   ") == "" && Codename.Sanitize("  Run  ner ") == "Run ner", "N: spaces are trimmed/collapsed, blank = unset");
        Check(Codename.Sanitize("A\nB\tC") == "A B C", "N: line breaks/tabs become one space");
        Check(Codename.Sanitize("<color=red>X</color>").IndexOf('<') < 0 && Codename.Sanitize("a\u0007b‮c") == "abc", $"N: no tags, control or direction characters ({Codename.Sanitize("<color=red>X</color>")})");
        string longName = "ランナー😀😀😀ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Check(Codename.LengthOf(Codename.Sanitize(longName)) == Codename.MaxLength, $"N: cut to {Codename.MaxLength} characters (emoji count as one)");
        Check(Codename.Sanitize("مرحبا 世界 Ñandú") == "مرحبا 世界 Ñandú", "N: multilingual names kept");
        Codename.Set("QA Runner");
        Check(Codename.Current == "QA Runner" && !Leaderboard.Joined && Leaderboard.PendingCount == 0, "N: saving a name does not join or post anything");

        // ---- R ランキング
        L("== R ranking ==");
        // 資格: 成功した通常のランだけ
        var good = new RunLedger.Run { runId = System.Guid.NewGuid().ToString("N"), stageId = "wasteland_road", characterId = "swordsman", maxReached = 12345, walked = 12345, playSeconds = 600, officialStage = true, startedUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 700 };
        Check(Leaderboard.IsEligible(good, out _), "R: a normal successful run is eligible");
        var bad = new[] {
            ("debug", new RunLedger.Run { runId = good.runId, stageId = good.stageId, maxReached = 1, officialStage = true, debugUsed = true }),
            ("legacy", new RunLedger.Run { runId = good.runId, stageId = good.stageId, maxReached = 1, officialStage = true, legacy = true }),
            ("multi", new RunLedger.Run { runId = good.runId, stageId = good.stageId, maxReached = 1, officialStage = true, multiplayer = true }),
            ("test data", new RunLedger.Run { runId = good.runId, stageId = good.stageId, maxReached = 1, officialStage = true, testProfile = true }),
            ("arena map", new RunLedger.Run { runId = good.runId, stageId = "arena", maxReached = 1, officialStage = true }),
            ("dev-selected map", new RunLedger.Run { runId = good.runId, stageId = "natural_cave", maxReached = 1, officialStage = false }) };
        foreach (var b in bad) Check(!Leaderboard.IsEligible(b.Item2, out string why), $"R: {b.Item1} run is excluded ({why})");
        // 実際の成功ラン(テストデータの印だけ外す)→ 参加前は送らない
        yield return UnlockRun("wasteland_road", "swordsman", 8000f, win: true);
        Check(gm.LastCommit.committed && !gm.LastCommit.rankingEligible, "R: a run in test data is recorded locally but not for the ranking");
        yield return EndRun();
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        WarpTo(9000f); yield return null;
        RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
        RunLedger.DevSet(r => { r.testProfile = false; r.maxReached = 9000; r.walked = 9000; r.playSeconds = 400; r.startedUtc -= 500; r.usedAuto = true; });
        gm.Win(); yield return WaitTut(() => gm.ResultShown, 6f);
        Check(gm.LastCommit.committed && gm.LastCommit.rankingEligible && Leaderboard.VerifiedBest("wasteland_road")?.meters == 9000, "R: a normal success is kept as a verified best");
        Check(Leaderboard.PendingCount == 0 && !System.IO.File.Exists(System.IO.Path.Combine(dir, "leaderboard.json")), "R: nothing is sent before joining");
        yield return EndRun();
        // 参加 → 送る
        Leaderboard.Join();
        yield return WaitTut(() => !Leaderboard.Sending && Leaderboard.PendingCount == 0, 5f);
        Leaderboard.Page page = null;
        yield return Await(me.Load("wasteland_road", 50), p => page = p);
        Check(page != null && page.me != null && page.me.meters == 9000 && page.me.name == "QA Runner" && page.me.usedAuto, $"R: joining sends the verified best with the codename and the auto mark ({page?.me?.meters})");
        // 別プレイヤー(同じ距離 = 同じ順位、長い記録は上)
        var e1 = MakeEntry("wasteland_road", "archer", 9000, 400); var e2 = MakeEntry("wasteland_road", "mage", 15000, 900);
        yield return Await(other.Submit(e1, "Other A"), _ => { });
        var other2 = new MockBackend(dir, "player-third");
        yield return Await(other2.Submit(e2, "Third"), _ => { });
        yield return Await(me.Load("wasteland_road", 50), p => page = p);
        var ranks = page.top.Select(x => $"{x.rank}:{x.name}:{x.meters}").ToList();
        L("[R] board " + string.Join(" | ", ranks));
        Check(page.top.Count == 3 && page.top[0].meters == 15000 && page.top[0].rank == 1 && page.top[1].rank == 2 && page.top[2].rank == 2 && page.me.rank == 2, "R: sorted by distance, equal distances share the rank (1, 2, 2)");
        // 自己ベストより短い記録で上書きしない / 長い記録で更新
        yield return Await(me.Submit(MakeEntry("wasteland_road", "swordsman", 5000, 300), "QA Runner"), res => Check(res.status == Leaderboard.SubmitStatus.NotBetter, $"R: a shorter run does not replace the best ({res.status})"));
        var better = MakeEntry("wasteland_road", "swordsman", 20000, 1000);
        yield return Await(me.Submit(better, "QA Runner"), res => Check(res.status == Leaderboard.SubmitStatus.Accepted, $"R: a longer run updates the best ({res.status})"));
        yield return Await(me.Submit(better, "QA Runner"), res => Check(res.status == Leaderboard.SubmitStatus.Duplicate, $"R: the same run id is refused by the server ({res.status})"));
        // サーバーの検査
        var fast = MakeEntry("wasteland_road", "swordsman", 50000, 60);
        yield return Await(me.Submit(fast, "x"), res => Check(res.status == Leaderboard.SubmitStatus.Rejected && res.message == "too fast", $"R: impossible speed is rejected ({res.message})"));
        var spr = MakeEntry("wasteland_road", "swordsman", 40000, 900); spr.usedSprint = true; spr.sprintFrom = 30000;
        yield return Await(me.Submit(spr, "x"), res => Check(res.status == Leaderboard.SubmitStatus.Rejected && res.message.Contains("sprint"), $"R: sprinting past the confirmed best is rejected ({res.message})"));
        var badChar = MakeEntry("wasteland_road", "hacker", 100, 100);
        yield return Await(me.Submit(badChar, "x"), res => Check(res.status == Leaderboard.SubmitStatus.Rejected, $"R: unknown character is rejected ({res.message})"));
        // 通信失敗 → 投稿待ちに残す → 再送(1回だけ)
        MockBackend.SimulateOffline = true;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        WarpTo(25000f); yield return null;
        RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
        RunLedger.DevSet(r => { r.testProfile = false; r.maxReached = 25000; r.walked = 25000; r.playSeconds = 1100; r.startedUtc -= 1300; });
        gm.Win(); yield return WaitTut(() => gm.ResultShown, 6f);
        yield return WaitTut(() => !Leaderboard.Sending, 4f);
        Check(gm.LastCommit.committed && Leaderboard.PendingCount == 1, $"R: offline -> the local record is saved and the post waits ({Leaderboard.PendingCount} pending)");
        yield return EndRun();
        yield return ReloadHome(); // 再起動の代わり(投稿待ちは保存されている)
        Check(Leaderboard.PendingCount == 1, "R: the pending post survives a restart");
        MockBackend.SimulateOffline = false;
        yield return AwaitTask(Leaderboard.FlushAsync("qa"));
        yield return AwaitTask(Leaderboard.FlushAsync("qa again"));
        yield return Await(me.Load("wasteland_road", 50), p => page = p);
        Check(Leaderboard.PendingCount == 0 && page.me.meters == 25000 && page.top.Count(x => x.isMe) == 1, $"R: resent once after reconnecting (best {page.me?.meters}m, one row)");
        // コードネームの変更: 本人は同じ、表示名だけ変わる
        Codename.Set("New Name");
        yield return WaitTut(() => false, 0.5f);
        yield return Await(me.Load("wasteland_road", 50), p => page = p);
        Check(page.me != null && page.me.name == "New Name" && page.me.playerId == "player-me" && page.top.Count(x => x.isMe) == 1, "R: renaming keeps the same player and record");
        // 画面
        RankingPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(1.2f);
        Shot("records_ranking_panel");
        yield return new WaitForSecondsRealtime(0.3f);
        typeof(RankingPanel).GetMethod("Close", NP).Invoke(RankingPanel.Instance, null);
        Leaderboard.Leave();
        Leaderboard.DevReset();
        RankingPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.8f);
        Shot("records_ranking_join");
        yield return new WaitForSecondsRealtime(0.3f);
        typeof(RankingPanel).GetMethod("Close", NP).Invoke(RankingPanel.Instance, null);

        // ---- A オート
        L("== A auto ==");
        var hsa = HighSpeedAssist.Instance;
        bool b0 = hsa.autoInBoss, a0 = hsa.autoAttack, v0 = hsa.autoAvoid;
        hsa.SetAutoInBoss(false); hsa.SetAutoAttack(false); hsa.SetAutoAvoid(true);
        hsa.autoInBoss = hsa.autoAttack = hsa.autoAvoid = true; hsa.ReloadPrefs();
        Check(!hsa.autoInBoss && !hsa.autoAttack && hsa.autoAvoid, "A: the three new settings are saved and loaded");
        SaveStore.DeleteKey(HighSpeedAssist.BossPrefKey); SaveStore.DeleteKey(HighSpeedAssist.AttackPrefKey); SaveStore.DeleteKey(HighSpeedAssist.AvoidPrefKey); hsa.ReloadPrefs();
        Check(hsa.autoInBoss && hsa.autoAttack && hsa.autoAvoid, "A: existing data gets ON for the new settings (same behaviour as before)");
        // 実際のランで: 回避だけ / 攻撃だけ
        int ups = 0, attacks = 0;
        hsa.ArenaApply(2, 0f, true, true);
        hsa.autoAttack = false; hsa.autoAvoid = true;
        yield return BeginRun("swordsman", "wasteland_road");
        int j0 = PlayerController.AssistJumpCount, k0 = PlayerController.AssistAttackCount;
        float t = 0f; while (t < 14f) { yield return null; t += Time.unscaledDeltaTime; }
        ups = PlayerController.AssistJumpCount - j0; attacks = PlayerController.AssistAttackCount - k0;
        L($"[A] avoid only: assist jumps {ups}, assist attacks {attacks}");
        Check(attacks == 0 && ups > 0, $"A: auto attack OFF -> no automatic attacks, jumps still happen ({ups} jumps)");
        yield return EndRun();
        ups = attacks = 0;
        hsa.ArenaApply(2, 0f, true, true);
        hsa.autoAttack = true; hsa.autoAvoid = false;
        yield return BeginRun("swordsman", "wasteland_road");
        j0 = PlayerController.AssistJumpCount; k0 = PlayerController.AssistAttackCount;
        t = 0f; while (t < 14f) { yield return null; t += Time.unscaledDeltaTime; }
        ups = PlayerController.AssistJumpCount - j0; attacks = PlayerController.AssistAttackCount - k0;
        L($"[A] attack only: assist jumps {ups}, assist attacks {attacks}");
        Check(ups == 0, $"A: auto avoid OFF -> no automatic jumps ({attacks} attacks)");
        yield return EndRun();
        hsa.ArenaRestore();
        hsa.SetAutoInBoss(b0); hsa.SetAutoAttack(a0); hsa.SetAutoAvoid(v0);

        // ---- O 障害物
        L("== O obstacles ==");
        int dmg0 = ObstacleController.TotalContactDamage, cb0 = ObstacleController.ContactBroken;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        int applied0 = pc.DamageAppliedCount;
        t = 0f; while (t < 25f && ObstacleController.ContactBroken == cb0) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.05f);
        Shot("records_obstacle_contact_invincible");
        Check(ObstacleController.ContactBroken > cb0 && ObstacleController.TotalContactDamage == dmg0, // 落下(無敵でも入る)は別なので DamageAppliedCount は見ない
            $"O: invincible body contact breaks the obstacle with the effect but is not counted as damage (broken {ObstacleController.ContactBroken - cb0}, damage {ObstacleController.TotalContactDamage - dmg0})");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        yield return EndRun();
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);

        Leaderboard.DevReset();
        Codename.Set("");
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    static Leaderboard.Entry MakeEntry(string stage, string ch, int meters, float secs)
    {
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new Leaderboard.Entry { runId = System.Guid.NewGuid().ToString("N"), stageId = stage, characterId = ch, meters = meters, playSeconds = secs, startedUtc = now - (long)secs - 30, committedUtc = now };
    }

    static IEnumerator AwaitTask(System.Threading.Tasks.Task task)
    {
        float w = 0f;
        while (!task.IsCompleted && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    static IEnumerator Await<T>(System.Threading.Tasks.Task<T> task, System.Action<T> done)
    {
        float w = 0f;
        while (!task.IsCompleted && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        if (task.IsCompleted && !task.IsFaulted) done(task.Result);
        else Debug.LogWarning("[QA] task failed: " + task.Exception);
    }
}
#endif

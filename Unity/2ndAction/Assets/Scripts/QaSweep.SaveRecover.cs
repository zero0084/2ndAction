#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// セーブの消失/バックアップからの復旧の確認(2026-10-13)。テスト用データ(SaveProfile の test:)だけを使い、本当に起動し直して確かめる。
//  -qaSaveRecover <dir> -srPhase <phase> [-srShared <dir>]
//   seed      … テスト用データを作り直して(新規)、進行を入れる
//   boot      … 起動しただけの状態を確かめる。-srExpect <BootKind> / -srFrom lastgood|lastgood_prev / -srMile N /
//                -srCompare 1(sr_expect.txt と同じ進行)/ -srWriteExpect 1 / -srDamaged 1(壊したファイルが救済用に残っている)/ -srNormal 1
//   setmile N … MILE を N にして普通に終了
//   intentreset … 意図した初期化(バックアップを別名へ移してから進行を消す)→ 新規になり、戻らない
//   release   … 開発版の確定の控えが残った状態から製品版の初期化(-qaReleaseGen 1 で起動)。-ccBase <cc_baseline.json>
//   release2  … 製品版の世代のまま再起動しても、初期化前の BEST/解放が戻らない
//   normal    … バックアップの無い新規開始 → 実際のラン(脱出)で通常の保存
//   cleanup   … テスト用データを消して通常のデータへ戻す
// 起動の前に壊す/消すのは SaveRecoverHooks(-srLoseAtStart / -srCorrupt)。書き込みの途中の終了は -qaBackupCrashAt。
public partial class QaSweep
{
    static readonly string[] SrKeys = { "TotalOwnedMile", "OwnedCardsV1", "DeckCardIds", "BestDistance", "BestDistance_v2_wasteland_road", SaveKeys.LifetimeDistance, "UnlockedIds", "SelectedCharacterId", "SaveSchemaVersion", "SaveReleaseGeneration" };

    static string SrSnapshot(bool withMile)
    {
        var all = SaveSystem.Capture().ToDictionary(i => i.k, i => i.v);
        return string.Join("\n", SrKeys.Where(k => withMile || k != "TotalOwnedMile").Select(k => k + "=" + (all.TryGetValue(k, out var v) ? v : "<none>")));
    }

    static string SrFiles(string dir)
    {
        if (!System.IO.Directory.Exists(dir)) return "(no backup folder)";
        var names = System.IO.Directory.GetFiles(dir).Select(System.IO.Path.GetFileName).OrderBy(x => x, System.StringComparer.Ordinal).ToList();
        return string.Join(" ", names.Select(n => n.StartsWith("lastgood") ? $"{n}({(SaveSystem.BackupFileValid(System.IO.Path.Combine(dir, n)) ? "ok" : "BAD")})" : n));
    }

    IEnumerator SaveRecoverMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        string phase = Arg("-srPhase", "boot");
        string shared = Arg("-srShared", System.IO.Path.GetDirectoryName(outDir.TrimEnd('/', '\\')));
        var b = SaveSystem.LastBoot;
        L($"[sr] phase={phase} profile={(SaveProfile.IsTest ? "TEST" : "NORMAL")} boot={b?.kind} from='{b?.restoredFrom}' ({b?.restoredTime}) preserved=[{(b != null ? string.Join(",", b.preserved) : "")}]");
        L($"[sr] boot report: {b?.report}");
        if (SaveRecoverHooks.Done != "") L($"[sr] before start-up: {SaveRecoverHooks.Done}");
        L($"[sr] backups(before): {SrFiles(SaveSystem.BackupDir)}");
        bool switches = phase == "seed" || phase == "normal" || phase == "cleanup";
        Check(switches || SaveProfile.IsTest, "runs on the TEST data only (the normal data is not touched)");
        if (!switches && !SaveProfile.IsTest) yield break;
        string mileKey = GameManager.TotalMileKey;

        if (phase == "seed")
        {
            SaveProfile.Switch(true, true);
            b = SaveSystem.LastBoot;
            Check(b.kind == SaveSystem.BootKind.NewGame && b.restoredFrom == "" && b.preserved.Count == 0, $"seed: fresh test data starts as a new game, nothing restored ({b.kind})");
            SaveStore.SetInt(mileKey, 7777);
            SaveStore.SetString("OwnedCardsV1", "{\"stacks\":[{\"cardId\":\"attack_up\",\"level\":3,\"count\":2}]}");
            SaveStore.SetString("DeckCardIds", "attack_up,attack_up");
            SaveStore.SetFloat("BestDistance", 45678f);
            SaveStore.SetString("BestDistance_v2_wasteland_road", "45678.5");
            SaveStore.SetString(SaveKeys.LifetimeDistance, "123456");
            SaveStore.SetString("UnlockedIds", "unlock_a,unlock_b");
            SaveStore.SetString("SelectedCharacterId", "ninja");
            SaveStore.Save();
            SaveSystem.ReloadCaches();
            yield return ReloadHome();
            Check(gm.TotalOwnedMile == 7777, $"seed: progress written (MILE {gm.TotalOwnedMile})");
        }
        else if (phase == "boot")
        {
            string ek = Arg("-srExpect", ""), ef = Arg("-srFrom", ""), em = Arg("-srMile", "");
            if (ek != "") Check(b.kind.ToString() == ek, $"start-up kind {b.kind} (expected {ek})");
            if (ef != "") Check(b.restoredFrom == ef, $"restored from '{b.restoredFrom}' (expected {ef})");
            // -srMile N / latest / prev(latest・prev = 起動の前にそのバックアップに入っていた値。エミュレーターのやり直しで世代がずれても確かめられる)
            if (em == "latest" || em == "prev") em = SaveRecoverHooks.MileInFile.TryGetValue(em == "latest" ? "lastgood" : "lastgood_prev", out int fm) ? fm.ToString() : "-999";
            if (em != "") Check(SaveStore.GetInt(mileKey, -1) == int.Parse(em) && gm.TotalOwnedMile == int.Parse(em), $"MILE {SaveStore.GetInt(mileKey, -1)} / game {gm.TotalOwnedMile} (expected {Arg("-srMile", "")} = {em})");
            string notMile = Arg("-srNotMile", "");
            if (notMile != "") Check(SaveStore.GetInt(mileKey, -1) != int.Parse(notMile), $"MILE is not the value of the run that was killed while writing the backup ({notMile})");
            string expPath = System.IO.Path.Combine(shared, "sr_expect.txt");
            if (Arg("-srCompare", "") == "1")
            {
                string exp = System.IO.File.Exists(expPath) ? System.IO.File.ReadAllText(expPath) : "";
                string now = SrSnapshot(false);
                Check(exp != "" && exp == now, "the progress is the same as before the loss (cards/deck/BEST/lifetime/unlocks/selection/format)" + (exp == now ? "" : $"\n  expected:\n{exp}\n  now:\n{now}"));
            }
            if (Arg("-srWriteExpect", "") == "1") { System.IO.File.WriteAllText(expPath, SrSnapshot(false)); L("[sr] expected progress:\n" + SrSnapshot(true)); }
            if (Arg("-srDamaged", "") == "1")
            {
                var have = new HashSet<string>(System.IO.Directory.GetFiles(SaveSystem.BackupDir).Select(p => SaveRecoverHooks.Sha1(System.IO.File.ReadAllBytes(p))));
                Check(SaveRecoverHooks.DamagedHashes.Count > 0 && SaveRecoverHooks.DamagedHashes.All(have.Contains), $"the damaged backup file(s) are kept as they were for investigation ({SaveRecoverHooks.DamagedHashes.Count})");
            }
            if (Arg("-srNormal", "") == "1")
            {
                string[] f = System.IO.File.ReadAllText(System.IO.Path.Combine(shared, "sr_normal.txt")).Split(',');
                double best = GameManager.ReadStageBest("wasteland_road"), life = ProgressStats.LifetimeDistance;
                Check(SaveStore.GetInt(mileKey, -1) == int.Parse(f[0]) && System.Math.Abs(best - double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)) < 0.5
                    && System.Math.Abs(life - double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture)) < 0.5,
                    $"normal save/load: MILE {SaveStore.GetInt(mileKey, -1)} BEST {best:F0} lifetime {life:F0} after a restart (saved {string.Join("/", f)})");
            }
            if (b.restoredFrom != "")
            {
                float w = 0f;
                while (!(NoticeQueue.Open && NoticeQueue.CurrentId.StartsWith("save_restored_")) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
                Check(NoticeQueue.Open && NoticeQueue.CurrentId.StartsWith("save_restored_"), $"the player is told at home that the save was restored from the backup ({NoticeQueue.CurrentId})");
                yield return new WaitForSecondsRealtime(0.5f);
                Shot("restored_notice"); yield return null; yield return null;
                NoticeQueue.DebugAnswer();
            }
        }
        else if (phase == "setmile")
        {
            int m = int.Parse(Arg("-srValue", "0"));
            SaveStore.SetInt(mileKey, m); SaveStore.Save();
            SaveSystem.ReloadCaches();
            yield return ReloadHome();
            Check(gm.TotalOwnedMile == m, $"setmile: MILE {gm.TotalOwnedMile}");
        }
        else if (phase == "intentreset")
        {
            SaveSystem.ForgetBackupsForIntentionalReset("qa_reset");
            SaveProfile.DeleteTestData(true);
            var r = SaveSystem.Boot(SaveSystem.BuildReleaseGeneration);
            Check(r.kind == SaveSystem.BootKind.NewGame && r.restoredFrom == "" && SaveStore.GetInt(mileKey, -1) == DefaultSave.StartingMile, $"an intended reset starts a new game and is not undone by the backup ({r.kind}, MILE {SaveStore.GetInt(mileKey, -1)})");
            Check(System.IO.Directory.GetFiles(SaveSystem.BackupDir, "qa_reset_*").Length > 0, "the backups before the reset are kept under another name (not deleted)");
            yield return ReloadHome();
        }
        else if (phase == "release")
        {
            var bb = JsonUtility.FromJson<CcBase>(System.IO.File.ReadAllText(Arg("-ccBase", "")));
            Check(b.kind == SaveSystem.BootKind.ReleaseReset && b.generationAfter == 1, $"release: the release reset ran ({b.kind}, generation {b.generationBefore}->{b.generationAfter})");
            double best = GameManager.ReadStageBest(CcStage);
            Check(!CommitJournal.Pending && CommitJournal.Recovered == 0, $"release: the development journal was not applied and is gone (recovered {CommitJournal.Recovered})");
            Check(best == 0.0 && SaveStore.GetFloat(GameManager.BestDistanceKey, 0f) == 0f, $"release: no BEST came back (map {best:F0}, all {SaveStore.GetFloat(GameManager.BestDistanceKey, 0f):F0})");
            Check(!RunLedger.IsCommitted(bb.runId) && !SaveStore.GetString("UnlockedIds", "").Contains("elite_goblin") && SaveStore.GetInt(mileKey, -1) == DefaultSave.StartingMile && System.Math.Abs(ProgressStats.LifetimeDistance) < 0.5,
                "release: no commit mark / unlock / MILE / lifetime from the development run");
            var rr = System.IO.Directory.GetFiles(SaveSystem.BackupDir, "release_reset_*");
            Check(rr.Length > 0 && System.IO.File.ReadAllText(rr[0]).Contains(CommitJournal.Key), "release: the data before the reset (with the journal) is kept in the release_reset backup");
            // 念のため: 古い世代の控えがもう一度入っても反映しない / 今の世代の控えは今までどおり反映する
            var dev = new CommitJournal.Data { runId = "qa-dev-gen0", commit = true, stageId = CcStage, reached = 1500, walked = 1500, mileBefore = DefaultSave.StartingMile, mileAfter = DefaultSave.StartingMile + 500, record = true, bestDistance = 1500, gen = 0 };
            SaveStore.SetString(CommitJournal.Key, JsonUtility.ToJson(dev)); SaveStore.Save();
            CommitJournal.RecoverIfPending();
            Check(!CommitJournal.Pending && GameManager.ReadStageBest(CcStage) == 0.0 && SaveStore.GetInt(mileKey, -1) == DefaultSave.StartingMile
                && System.IO.Directory.GetFiles(SaveSystem.BackupDir, "discarded_journal_gen0_*").Length > 0, $"release: a generation-0 journal is not applied, kept in a file ({CommitJournal.LastRecovery})");
            var cur = new CommitJournal.Data { runId = "qa-gen1", commit = true, stageId = CcStage, reached = 1500, walked = 1500, mileBefore = DefaultSave.StartingMile, mileAfter = DefaultSave.StartingMile + 500, record = true, bestDistance = 1500, gen = 1 };
            SaveStore.SetString(CommitJournal.Key, JsonUtility.ToJson(cur)); SaveStore.Save();
            CommitJournal.RecoverIfPending();
            Check(!CommitJournal.Pending && GameManager.ReadStageBest(CcStage) >= 1499.5 && SaveStore.GetInt(mileKey, -1) == DefaultSave.StartingMile + 500 && RunLedger.IsCommitted("qa-gen1"),
                $"release: a journal of the current generation is still recovered as before ({CommitJournal.LastRecovery})");
            SaveSystem.ReloadCaches();
            yield return ReloadHome();
        }
        else if (phase == "release2")
        {
            Check(b.kind == SaveSystem.BootKind.Existing && b.generationAfter == 1, $"release2: a normal start-up in the release generation ({b.kind})");
            Check(SaveStore.GetInt(mileKey, -1) == DefaultSave.StartingMile + 500 && GameManager.ReadStageBest(CcStage) >= 1499.5 && !RunLedger.IsCommitted(JsonUtility.FromJson<CcBase>(System.IO.File.ReadAllText(Arg("-ccBase", ""))).runId)
                && !CommitJournal.Pending, "release2: only the release-generation progress is there (MILE/BEST of the generation-1 journal); the development run is not committed");
        }
        else if (phase == "normal")
        {
            SaveProfile.Switch(true, true);
            b = SaveSystem.LastBoot;
            Check(b.kind == SaveSystem.BootKind.NewGame && b.restoredFrom == "" && b.preserved.Count == 0, $"normal: a new start without any backup is a plain new game ({b.kind})");
            yield return ReloadHome();
            TutorialProgress.MarkOffered();
            yield return BeginRun("swordsman", CcStage);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            WarpTo(1500f); yield return null; yield return null;
            RunLedger.DevClearDebug(); UnlockRules.RunSkipped = false;
            RunLedger.DevSet(r => { r.maxReached = 1500; r.walked = 1500; r.playSeconds = 600; r.testProfile = false; r.startedUtc -= 700; });
            int pay = gm.TakeHomeMile;
            gm.Win();
            yield return WaitTut(() => gm.ResultShown, 6f);
            yield return new WaitForSecondsRealtime(0.5f);
            int mile = SaveStore.GetInt(mileKey, 0);
            double best = GameManager.ReadStageBest(CcStage), life = ProgressStats.LifetimeDistance;
            Check(pay > 0 && mile == DefaultSave.StartingMile + pay && best >= 1499.5 && life >= 1499.5, $"normal: an escape saves MILE +{pay} / BEST {best:F0} / lifetime {life:F0}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(shared, "sr_normal.txt"), $"{mile},{best.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{life.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
            yield return EndRun();
        }
        else if (phase == "cleanup")
        {
            SaveProfile.Switch(true, true);
            SaveProfile.Switch(false, false);
            yield return ReloadHome();
            Check(!SaveProfile.IsTest, "cleanup: back to the normal data");
        }
        L($"[sr] backups(after): {SrFiles(SaveSystem.BackupDir)}");
        L($"[sr] progress now: {SrSnapshot(true).Replace('\n', ' ')}");
        string lg = System.IO.Path.Combine(SaveSystem.BackupDir, "lastgood.json");
        if (SaveProfile.IsTest) Check(SaveSystem.BackupFileValid(lg), "the latest backup can be read");
    }
}
#endif

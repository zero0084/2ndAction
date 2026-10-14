#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// データの引き継ぎ(書き出し・読み込み)の確認(2026-10-14)。保存は専用のファイル(-fileSave)か テスト用データ だけ。通常のセーブには触れない。
//  -qaTransfer <dir> -trPhase <phase> -trShared <dir>(端末をまたいで使うファイルの置き場)
//   seedA    … 引き継ぎ元の進行 A を作って書き出す(A.json / expectA.txt)
//   seedB    … 読み込み先の進行 B を作る(中断中のラン/ランキングの投稿待ちも入れる)。expectB.txt
//   cancel   … A.json を選んで確認の画面まで → キャンセル → B のまま
//   reject   … 壊れた/欠けた/不正な値/新しい形式/世代違い/他のファイル → すべて拒否、B のまま
//   import   … 確定の控え(古い MILE)を置いてから A.json を読み込む → A と同じ、中断/控えは消え、ランキングの投稿待ちは残る
//   boot     … 起動し直しただけ → A のまま(バックアップ/控えで古い進行に戻らない、MILE が増えない)
//   again    … 同じ A.json をもう一度読み込む → A のまま(加算されない)
//   crash    … -qaTransferCrash <地点> で読み込みの途中で落ちる(結果のファイルは書かれない)
//   after    … 落ちた後の起動 → -trExpect A|B の通りに揃っている(混ざらない)、途中の控えは残っていない
//   old      … セーブ形式 2 の(古いアプリの)ファイル → 読み込んで最新の形式へ移行される
public partial class QaSweep
{
    static string TrSnapshot()
    {
        var lines = new List<string>();
        foreach (var k in SaveTransfer.TransferKeys())
            lines.Add(k.key + "=" + (SaveStore.HasKey(k.key) ? SaveSystem.ReadItem(k.key, k.type).v : "<none>"));
        return string.Join("\n", lines);
    }

    static string TrDiff(string a, string b)
    {
        var da = a.Split('\n').ToDictionary(l => l.Split('=')[0], l => l);
        var db = b.Split('\n').ToDictionary(l => l.Split('=')[0], l => l);
        var diff = new List<string>();
        foreach (var k in da.Keys.Union(db.Keys))
        {
            da.TryGetValue(k, out var x); db.TryGetValue(k, out var y);
            if (x != y) diff.Add(k);
        }
        return diff.Count == 0 ? "" : string.Join(",", diff);
    }

    IEnumerator TransferMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string phase = Arg("-trPhase", "boot");
        string shared = Arg("-trShared", outDir);
        System.IO.Directory.CreateDirectory(shared);
        string fileA = System.IO.Path.Combine(shared, "A.json");
        var b = SaveSystem.LastBoot;
        L($"[tr] phase={phase} store={(FileSaveStore.Active ? "FILE " + FileSaveStore.Folder : MemorySaveStore.Active ? "MEMORY" : SaveProfile.IsTest ? "TEST DATA" : "NORMAL")} boot={b?.kind} report={b?.report}");
        bool safe = FileSaveStore.Active || MemorySaveStore.Active || SaveProfile.IsTest;
        Check(safe, "runs on a dedicated save only (the normal save is not touched)");
        if (!safe) yield break;
        string mileKey = GameManager.TotalMileKey;

        if (phase == "bootA" || phase == "bootB")
        {
            // 起動し直した後の状態を期待値にする(起動時に足される印などを含めた「落ち着いた」進行)。A はここで書き出す
            bool a = phase == "bootA";
            // ホームのお知らせ(解放/デッキ枠の案内)が出て確認済みの印が書かれるまで待つ(起動のたびに同じになる状態にしてから)
            float wn = 0f; while ((NoticeQueue.Open || wn < 4f) && wn < 15f) { yield return null; wn += Time.unscaledDeltaTime; }
            SaveStore.Save();
            System.IO.File.WriteAllText(System.IO.Path.Combine(shared, a ? "expectA.txt" : "expectB.txt"), TrSnapshot());
            if (a)
            {
                SaveTransferUi.DevExportPath = fileA;
                SaveTransferUi.DevResetResult(); SaveTransferUi.StartExport();
                yield return TrWait();
                Check(SaveTransferUi.LastResult == "export-ok" && System.IO.File.Exists(fileA), $"bootA: exported ({SaveTransferUi.LastResult})");
                var c = SaveTransfer.Check(System.IO.File.ReadAllText(fileA));
                Check(c.ok, "bootA: the exported file passes the check " + c.detail);
                if (c.ok)
                {
                    var keys = c.data.items.Select(i => i.k).ToList();
                    Check(!keys.Contains(RunCheckpoint.Key) && !keys.Contains(CommitJournal.Key) && !keys.Contains(Leaderboard.PendingKey) && !keys.Contains(Leaderboard.VerifiedKey) && !keys.Contains(Leaderboard.JoinedKey),
                        "bootA: no suspended run / commit journal / ranking keys in the file");
                    Check(!keys.Any(k => SaveKeys.All.Any(e => e.key == k && (e.cat == SaveCategory.Settings || e.cat == SaveCategory.Dev || e.cat == SaveCategory.Meta))), "bootA: no settings / dev / meta keys in the file");
                    L($"[tr] file: {c.data.itemCount} items, summary mile={c.data.summary.mile} cards={c.data.summary.cardKinds}/{c.data.summary.cardTotal} deck={c.data.summary.deckCount} stages={c.data.summary.stages} chars={c.data.summary.chars} best={c.data.summary.bestDistance} life={c.data.summary.lifetime} keys=[{string.Join(",", keys)}]");
                }
                SaveTransferUi.Instance.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
            }
            yield break;
        }

        if (phase == "seedA" || phase == "seedB")
        {
            bool a = phase == "seedA";
            SaveStore.SetInt(mileKey, a ? 123456 : 999);
            SaveStore.SetString("OwnedCardsV1", a
                ? "{\"stacks\":[{\"cardId\":\"attack_up\",\"level\":5,\"count\":3},{\"cardId\":\"heart_up\",\"level\":2,\"count\":1},{\"cardId\":\"boss_killer\",\"level\":9,\"count\":2}]}"
                : "{\"stacks\":[{\"cardId\":\"double_attack\",\"level\":1,\"count\":7}]}");
            SaveStore.SetString("DeckCardIds", a ? "attack_up,heart_up,boss_killer" : "double_attack");
            SaveStore.SetString(UnlockRules.StagesKey, a ? "natural_cave,sky_corridor" : "");
            SaveStore.SetString(UnlockRules.CharsKey, a ? "archer,mage,noble_lady" : "");
            SaveStore.SetString(SaveKeys.StageBestPrefix + "wasteland_road", a ? "23456.5" : "120");
            SaveStore.SetString(SaveKeys.StageBestPrefix + "natural_cave", a ? "8800" : "0");
            if (!a) SaveStore.DeleteKey(SaveKeys.StageBestPrefix + "natural_cave");
            SaveStore.SetFloat(GameManager.BestDistanceKey, a ? 23456.5f : 120f);
            SaveStore.SetString(SaveKeys.LifetimeDistance, a ? "345678.25" : "500");
            SaveStore.SetString(UnlockRules.ReachPrefix + "wasteland_road", a ? "23456.5" : "120");
            SaveStore.SetString("SelectedCharacterId", a ? "archer" : "swordsman");
            SaveStore.SetString(Codename.Key, a ? "TransferA" : "DeviceB");
            SaveStore.SetString(GameManager.CharacterCardSlotsPrefix + "archer", a ? "attack_up:5,heart_up:2" : "");
            if (!a)
            {
                // 読み込み先の端末だけの物: 中断中のラン / ランキングの投稿待ち(端末のアカウントの物)
                var cp = RunCheckpoint.Load(); cp.active = true; cp.checkpointDistance = 77f; RunCheckpoint.Save(cp);
                SaveStore.SetString(Leaderboard.PendingKey, "{\"items\":[]}");
            }
            SaveStore.Save();
            SaveSystem.ReloadCaches();
            yield break;
        }

        string expectA = System.IO.File.Exists(System.IO.Path.Combine(shared, "expectA.txt")) ? System.IO.File.ReadAllText(System.IO.Path.Combine(shared, "expectA.txt")) : "";
        string expectB = System.IO.File.Exists(System.IO.Path.Combine(shared, "expectB.txt")) ? System.IO.File.ReadAllText(System.IO.Path.Combine(shared, "expectB.txt")) : "";
        string textA = System.IO.File.Exists(fileA) ? System.IO.File.ReadAllText(fileA) : "";
        if (phase != "importFile") Check(expectA != "" && textA != "", "the seed files exist");

        if (phase == "importFile")
        {
            // 別の機種で書き出したファイル(-trFile)を読み込む: ファイルの各項目の値と同じになり、ファイルに無い引き継ぎのキーは消える
            string path = Arg("-trFile", "");
            var c = SaveTransfer.Check(System.IO.File.ReadAllText(path));
            Check(c.ok, $"importFile: the file from {(c.data != null ? c.data.platform : "?")} passes the check {c.detail}");
            if (!c.ok) yield break;
            SaveTransferUi.DevImportPath = path;
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
            yield return TrWait();
            var oldGm = GameManager.Instance;
            Check(SaveTransferUi.ConfirmReplace(), "importFile: applied");
            float w = 0f; while ((GameManager.Instance == null || GameManager.Instance == oldGm || LoadingScreen.BlocksInput) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1.5f);
            gm = GameManager.Instance;
            var want = c.data.items.ToDictionary(i => i.k, i => i.v);
            var diff = new List<string>();
            foreach (var k in SaveTransfer.TransferKeys())
            {
                bool has = SaveStore.HasKey(k.key);
                if (want.TryGetValue(k.key, out var v)) { if (!has || SaveSystem.ReadItem(k.key, k.type).v != v) diff.Add(k.key); }
                else if (has && k.key != "UnlockNotifiedV1" && k.key != "DeckCapacity12Notice") diff.Add(k.key + "(extra)");
            }
            Check(diff.Count == 0, $"importFile: the progress equals the file ({string.Join(",", diff)})");
            Check(gm.TotalOwnedMile == c.data.summary.mile, $"importFile: MILE {gm.TotalOwnedMile} = file {c.data.summary.mile}");
            yield break;
        }

        if (phase == "pickReject")
        {
            // 選ぶ画面で壊れたファイルを選ぶ(adb が選ぶ)→ 拒否の表示、何も変わらない
            string before = TrSnapshot();
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
            yield return TrWait();
            Check(SaveTransferUi.LastResult == "import-reject", $"pickReject: a damaged file is rejected ({SaveTransferUi.LastResult})");
            Shot("transfer_pick_reject");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(TrSnapshot() == before && RunCheckpoint.HasActiveRun, "pickReject: nothing changed");
            yield break;
        }

        if (phase == "pickCancel")
        {
            // 選ぶ画面で何も選ばずに戻る(adb が戻るキーを押す)→ 何も変わらない
            string before = TrSnapshot();
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
            yield return TrWait();
            Check(SaveTransferUi.LastResult == "import-cancel", $"pickCancel: back from the picker = cancelled ({SaveTransferUi.LastResult})");
            Check(TrSnapshot() == before && RunCheckpoint.HasActiveRun, "pickCancel: nothing changed");
            yield break;
        }

        if (phase == "export")
        {
            // Android: 標準の保存先の選択で書き出す(adb が「保存」を押す)。PC: -trExportTo へ
            if (Arg("-trExportTo", "") != "") SaveTransferUi.DevExportPath = Arg("-trExportTo", "");
            float wn = 0f; while ((NoticeQueue.Open || wn < 4f) && wn < 15f) { yield return null; wn += Time.unscaledDeltaTime; }
            SaveStore.Save();
            System.IO.File.WriteAllText(System.IO.Path.Combine(shared, "expectExport.txt"), TrSnapshot());
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartExport();
            yield return TrWait();
            Check(SaveTransferUi.LastResult == "export-ok", $"export: written ({SaveTransferUi.LastResult})");
            Shot("transfer_export_done");
            yield return new WaitForSecondsRealtime(0.3f);
            yield break;
        }

        if (phase == "cancel")
        {
            string before = TrSnapshot();
            SaveTransferUi.DevImportPath = fileA;
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
            yield return TrWait();
            Check(SaveTransferUi.LastResult == "import-confirm", $"cancel: the confirmation is shown ({SaveTransferUi.LastResult})");
            Shot("transfer_confirm");
            yield return new WaitForSecondsRealtime(0.3f);
            SaveTransferUi.CancelConfirm();
            yield return null;
            Check(TrSnapshot() == before && TrDiff(before, expectB) == "", "cancel: nothing changed (" + TrDiff(TrSnapshot(), before) + ")");
            Check(RunCheckpoint.HasActiveRun, "cancel: the suspended run is still there");
            yield break;
        }

        if (phase == "reject")
        {
            string before = TrSnapshot();
            var bad = new List<(string name, string text, string expect)>
            {
                ("empty", "", "空"),
                ("not json", "hello", "引き継ぎファイルではありません"),
                ("other json", "{\"stacks\":[]}", "引き継ぎファイルではありません"),
                ("truncated", textA.Substring(0, textA.Length / 2), "壊れて"),
                ("value edited", textA.Replace("\"123456\"", "\"999999\""), "壊れて"),
                ("item removed", RemoveItemKeepCount(textA), "壊れて"),
                ("newer format", textA.Replace("\"formatVersion\": 1", "\"formatVersion\": 2"), "新しいバージョン"),
                ("newer save schema", SaveTransfer.DevResign(textA, f => f.saveSchema = SaveSystem.CurrentSchemaVersion + 1), "新しいバージョン"),
                ("other generation", SaveTransfer.DevResign(textA, f => f.releaseGeneration = 1), "開発版と製品版"),
                ("unknown key", SaveTransfer.DevResign(textA, f => f.items.Add(new SaveSystem.Item { k = "SomethingNew", t = 0, v = "1" })), "新しいバージョン"),
                ("settings key", SaveTransfer.DevResign(textA, f => f.items.Add(new SaveSystem.Item { k = "MasterVolume", t = 1, v = "0.5" })), "新しいバージョン"),
                ("negative MILE", SaveTransfer.DevResign(textA, f => f.items.First(i => i.k == mileKey).v = "-5"), "正しくない値"),
                ("card level 15", SaveTransfer.DevResign(textA, f => { var it = f.items.First(i => i.k == "OwnedCardsV1"); it.v = it.v.Replace("\"level\":5", "\"level\":15"); }), "正しくない値"),
                ("unknown card", SaveTransfer.DevResign(textA, f => { var it = f.items.First(i => i.k == "OwnedCardsV1"); it.v = it.v.Replace("heart_up", "no_such_card"); }), "正しくない値"),
                ("NaN distance", SaveTransfer.DevResign(textA, f => f.items.First(i => i.k == SaveKeys.LifetimeDistance).v = "NaN"), "正しくない値"),
                ("broken card json", SaveTransfer.DevResign(textA, f => { var it = f.items.First(i => i.k == "OwnedCardsV1"); it.v = it.v.Substring(0, 20); }), "正しくない値"),
                ("missing MILE", SaveTransfer.DevResign(textA, f => f.items.RemoveAll(i => i.k == mileKey)), "足りません"),
                ("summary edited", SaveTransfer.DevResign(textA, f => f.summary.mile = 1, false), "壊れて"),
                ("wrong type", SaveTransfer.DevResign(textA, f => f.items.First(i => i.k == mileKey).t = 2), "壊れて"),
            };
            foreach (var (name, text, expect) in bad)
            {
                var c = SaveTransfer.Check(text);
                Check(!c.ok && c.error.Contains(expect), $"reject: {name} -> rejected '{c.error}' ({c.detail})");
                // 画面の流れでも: 選んだ → 拒否の表示 → 何も変わらない(Android は選択画面を通す pickReject で確かめる)
                if (Application.platform == RuntimePlatform.Android) continue;
                string path = System.IO.Path.Combine(outDir, "bad_" + name.Replace(' ', '_') + ".json");
                System.IO.File.WriteAllText(path, text);
                SaveTransferUi.DevImportPath = path;
                SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
                yield return TrWait();
                Check(SaveTransferUi.LastResult == "import-reject" || (name == "empty" && SaveTransferUi.LastResult == "import-reject"), $"reject: {name} -> the screen shows the rejection ({SaveTransferUi.LastResult})");
                if (name == "value edited") { Shot("transfer_reject"); yield return new WaitForSecondsRealtime(0.3f); }
                SaveTransferUi.Instance.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
            }
            Check(TrSnapshot() == before, "reject: the current data did not change (" + TrDiff(TrSnapshot(), before) + ")");
            Check(!System.IO.File.Exists(SaveTransfer.PendingPath), "reject: no pending import was written");
            yield break;
        }

        if (phase == "import" || phase == "again" || phase == "crash" || phase == "old")
        {
            string src = textA;
            if (phase == "old")
            {
                // 古いアプリ(セーブ形式 2)のファイル: 形式 2→3 の移行(Mastery の記録の作成)が読み込みの後に走る
                src = SaveTransfer.DevResign(textA, f => { f.saveSchema = 2; f.items.RemoveAll(i => i.k == "CardMasteryV1"); });
            }
            string path = System.IO.Path.Combine(outDir, "import.json");
            System.IO.File.WriteAllText(path, src);
            int mileBefore = SaveStore.GetInt(mileKey, 0);
            if (phase == "import")
            {
                // 読み込み先に「確定の途中の控え」が残っている(古い MILE +500 を足そうとしている)
                CommitJournal.Write(new CommitJournal.Data { runId = "deadbeefdeadbeefdeadbeefdeadbeef", commit = false, mileBefore = mileBefore, mileAfter = mileBefore + 500 });
            }
            var crash = Arg("-qaTransferCrash", "");
            if (crash != "") SaveTransfer.DevCrashAt = crash;
            SaveTransferUi.DevImportPath = path;
            SaveTransferUi.DevResetResult(); SaveTransferUi.StartImport();
            yield return TrWait();
            Check(SaveTransferUi.LastResult == "import-confirm", $"{phase}: confirmation shown ({SaveTransferUi.LastResult})");
            var oldGm = GameManager.Instance;
            bool ok = SaveTransferUi.ConfirmReplace();
            Check(ok, $"{phase}: import applied ({SaveTransferUi.LastResult})");
            float w = 0f; while ((GameManager.Instance == null || GameManager.Instance == oldGm || LoadingScreen.BlocksInput) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1.5f);
            gm = GameManager.Instance;
            string now = TrSnapshot();
            if (phase == "old")
            {
                Check(SaveStore.GetInt(SaveKeys.SchemaVersion, 0) == SaveSystem.CurrentSchemaVersion, $"old: migrated to the current save format ({SaveStore.GetInt(SaveKeys.SchemaVersion, 0)})");
                Check(SaveStore.HasKey("CardMasteryV1") && SaveStore.GetInt(mileKey, 0) == 123456, "old: mastery record created by the migration, MILE as in the file");
            }
            else Check(TrDiff(now, expectA) == "", $"{phase}: the progress is exactly A (diff: {TrDiff(now, expectA)})");
            Check(gm.TotalOwnedMile == 123456, $"{phase}: the reloaded game shows A's MILE ({gm.TotalOwnedMile})");
            Check(!RunCheckpoint.HasActiveRun && !SaveStore.HasKey(RunCheckpoint.Key), $"{phase}: the suspended run of this device is gone");
            Check(!CommitJournal.Pending, $"{phase}: no commit journal left (the old +500 is not applied)");
            Check(SaveStore.HasKey(Leaderboard.PendingKey), $"{phase}: this device's ranking queue is kept (not transferred, not deleted)");
            Check(!System.IO.File.Exists(SaveTransfer.PendingPath), $"{phase}: the pending import file is removed");
            Check(SaveStore.GetInt(SaveKeys.SchemaVersion, 0) == SaveSystem.CurrentSchemaVersion, $"{phase}: save format is current");
            var files = System.IO.Directory.GetFiles(SaveSystem.BackupDirectory).Select(System.IO.Path.GetFileName).ToList();
            Check(files.Any(f => f.StartsWith("pre_import_")), "the state before the import is kept (pre_import_*.json)");
            L($"[tr] backups: {string.Join(" ", files.OrderBy(x => x))}");
            Shot("transfer_after_" + phase);
            yield return new WaitForSecondsRealtime(0.5f);
            yield break;
        }

        if (phase == "boot" || phase == "after")
        {
            string want = Arg("-trExpect", "A") == "A" ? expectA : expectB;
            string now = TrSnapshot();
            Check(TrDiff(now, want) == "", $"{phase}: after the restart the progress is exactly {Arg("-trExpect", "A")} (diff: {TrDiff(now, want)})");
            Check(gm.TotalOwnedMile == SaveStore.GetInt(mileKey, 0), $"{phase}: the game reads the same MILE ({gm.TotalOwnedMile})");
            Check(!CommitJournal.Pending, $"{phase}: no commit journal");
            Check(!System.IO.File.Exists(SaveTransfer.PendingPath), $"{phase}: no pending import file left");
            L($"[tr] boot report: {b?.report}");
            if (phase == "after" && Arg("-trExpect", "A") == "A") Check(b != null && b.report.Contains("finished an interrupted import") || Arg("-trWasDone", "0") == "1", "after: the interrupted import was finished at start-up");
            yield break;
        }
    }

    // 結果が出るまで待つ(PC はすぐ。Android は選択画面を操作し終わるまで)。adb 側へ「選択画面を開いた」を知らせる印も書く
    IEnumerator TrWait(float max = 240f)
    {
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "picker_open.txt"), System.DateTime.Now.ToString("s")); } catch { }
        float w = 0f;
        while (SaveTransferUi.LastResult == "" && w < max) { yield return null; w += Time.unscaledDeltaTime; }
        try { System.IO.File.Delete(System.IO.Path.Combine(outDir, "picker_open.txt")); } catch { }
        L($"[tr] result {SaveTransferUi.LastResult} after {w:F1}s {(SaveTransferUi.LastRejectDetail != "" ? "(" + SaveTransferUi.LastRejectDetail + ")" : "")}");
    }

    // 1項目を消して個数はそのまま(検査値で見つかる)
    static string RemoveItemKeepCount(string text)
    {
        var f = JsonUtility.FromJson<SaveTransfer.FileData>(text);
        f.items.RemoveAt(f.items.Count - 1);
        f.itemCount = f.items.Count; // 個数も合わせても、検査値が合わない
        return JsonUtility.ToJson(f, true);
    }
}
#endif

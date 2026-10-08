#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// セーブ/初期化/移行(2026-10-01)の確認。 -qaSave <dir>
// テストの前に今のセーブを丸ごと控え、最後に必ず元へ戻す(バックアップの書き出し先もテスト用の別フォルダ)。
public partial class QaSweep
{
    static void ClearRegistered()
    {
        foreach (var e in SaveKeys.Expanded()) PlayerPrefs.DeleteKey(e.key);
        foreach (string k in SaveKeys.Legacy) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.Save();
    }

    // 開発中の典型的なセーブ(この仕組みより前の形: 形式の版/世代のキーが無い)
    static void SeedDevSave()
    {
        ClearRegistered();
        PlayerPrefs.SetInt("TotalOwnedMile", 12345);
        PlayerPrefs.SetString("OwnedCardsV1", "{\"stacks\":[{\"cardId\":\"attack_up\",\"level\":3,\"count\":2},{\"cardId\":\"v2|speed_up|x\",\"level\":5,\"count\":1}]}");
        PlayerPrefs.SetString("DeckCardIds", "attack_up,attack_up");
        PlayerPrefs.SetString("CharacterCardSlots", "ninja=attack_up");
        PlayerPrefs.SetFloat("BestDistance", 45678f);
        PlayerPrefs.SetFloat("BestTime", 1234.5f);
        PlayerPrefs.SetString("BestDistance_v2_wasteland_road", "45678.5");
        PlayerPrefs.SetString("UnlockedIds", "unlock_a,unlock_b");
        PlayerPrefs.SetString("SelectedCharacterId", "ninja");
        PlayerPrefs.SetString("SelectedStageId", "natural_cave");
        PlayerPrefs.SetInt("CardDataFormat", 2);
        PlayerPrefs.SetFloat("MasterVolume", 0.3f);
        PlayerPrefs.SetFloat("BgmVolume", 0.25f);
        PlayerPrefs.SetInt("ScreenShakeEnabled", 0);
        PlayerPrefs.SetFloat("HighSpeedAssistEngageKmh", 120f);
        PlayerPrefs.SetInt("PreferredOrientation", 1);
        PlayerPrefs.SetString("net.lastHostIp", "192.168.0.9");
        PlayerPrefs.SetInt("InvincibleMode", 1);
        PlayerPrefs.Save();
    }

    IEnumerator SaveMode()
    {
        var original = SaveSystem.Capture();
        L($"[save] snapshot of the current save: {original.Count} keys (restored at the end)");
        SaveSystem.TestBackupSubdir = "SaveBackups_qa";
        string backupDir = System.IO.Path.Combine(Application.persistentDataPath, "SaveBackups_qa");
        try { if (System.IO.Directory.Exists(backupDir)) System.IO.Directory.Delete(backupDir, true); } catch { }
        try
        {
            // ---- A: 新規インストール
            ClearRegistered();
            var r = SaveSystem.Boot(0);
            Check(r.kind == SaveSystem.BootKind.NewGame, $"A: new install detected ({r.kind})");
            Check(PlayerPrefs.GetInt(SaveKeys.SchemaVersion, -1) == SaveSystem.CurrentSchemaVersion && PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, -1) == 0, "A: schema/generation written");
            Check(PlayerPrefs.GetInt("TotalOwnedMile", -1) == DefaultSave.StartingMile && ProgressStats.LifetimeDistance == 0.0, "A: MILE and lifetime distance start at 0");
            Check(!ProgressStats.FinalDungeonUnlocked && !ProgressStats.HasMet(ReaperSister.Eldest) && !ProgressStats.HasMet(ReaperSister.Second) && !ProgressStats.HasMet(ReaperSister.Youngest), "A: reaper flags false, final dungeon locked");
            Check(!PlayerPrefs.HasKey("BestDistance_v2_wasteland_road") && PlayerPrefs.GetFloat("BestDistance", 0f) == 0f, "A: no BEST records");
            Check(PlayerPrefs.GetString("SelectedStageId", "") == DefaultSave.StartingStageId, "A: starting stage is the wasteland");
            // 初期デッキ/所持カードは GameManager の読み込みで DefaultSave から作られる(シーンを読み直して確認)
            yield return ReloadSceneForSave();
            var gm0 = GameManager.Instance;
            var starting = DefaultSave.StartingDeck(GameManager.DeckCapacity);
            Check(gm0 != null && gm0.DeckCards.Count == starting.Count && starting.Count > 0, $"A: starting deck = DefaultSave.StartingDeck ({gm0?.DeckCards.Count} / {starting.Count})");
            int lv1 = 0; foreach (var st in CardInventory.Stacks) if (st.level == 1) lv1++;
            Check(CardInventory.Stacks.Count > 0 && lv1 == CardInventory.Stacks.Count, $"A: starting cards owned at Lv1 ({CardInventory.Stacks.Count})");
            Check(gm0 != null && gm0.SelectedCharacterId == DefaultSave.StartingCharacterId(), $"A: starting character {gm0?.SelectedCharacterId}");

            // ---- B: 開発中のセーブで通常のアップデート(この仕組みの導入)→ 何も消えない
            SeedDevSave();
            var before = SaveSystem.Capture();
            r = SaveSystem.Boot(0);
            Check(r.kind == SaveSystem.BootKind.Existing && r.generationBefore == 0, $"B: existing dev save, generation 0 ({r.kind})");
            Check(SameValues(before, SaveSystem.Capture(), out string diffB), "B: every existing value kept " + diffB);
            bool playedB = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance) > 0.0 || SaveStore.GetFloat("BestDistance", 0f) > 0f;
            Check(!playedB || (TutorialProgress.Offered && TutorialProgress.PracticeDone && TutorialProgress.EscapeGuideShown && TutorialProgress.MileGuide == 2), "B: an existing player gets no first-time tutorial offers/guides (marked as shown)");
            r = SaveSystem.Boot(0); // 2回目(次のアップデート)
            Check(r.kind == SaveSystem.BootKind.Existing && r.schemaBefore == SaveSystem.CurrentSchemaVersion && SameValues(before, SaveSystem.Capture(), out diffB), "B: a second update changes nothing " + diffB);

            // ---- C: 古い形式 → 移行後も進行が残る
            SeedDevSave();
            PlayerPrefs.DeleteKey("BgmVolume");
            PlayerPrefs.SetInt("BgmVolumeLevel", 2);    // 旧い0〜4段階
            PlayerPrefs.SetInt("SfxVolumeLevel", 1);
            PlayerPrefs.SetInt(SaveKeys.SchemaVersion, 0);
            // 2026-10-02: 1->2(戦闘数値10倍化)も続けて通る。旧スケールの中断中ラン(ハート3/5)と調整パネルの候補値
            PlayerPrefs.SetString(RunCheckpoint.Key, "{\"active\":true,\"characterId\":\"swordsman\",\"lives\":3,\"maxLives\":5,\"level\":4}");
            PlayerPrefs.SetFloat("CardTest.attack.1", 3f);
            r = SaveSystem.Boot(0);
            Check(r.schemaBefore == 0 && r.schemaAfter == SaveSystem.CurrentSchemaVersion && !r.migrationFailed, $"C: migrated 0->{SaveSystem.CurrentSchemaVersion} ({r.report})");
            var cp = JsonUtility.FromJson<RunCheckpoint.Data>(PlayerPrefs.GetString(RunCheckpoint.Key, "{}"));
            Check(cp.lives == 30 && cp.maxLives == 50 && cp.level == 4 && cp.characterId == "swordsman", $"C: 1->2 converts a suspended run's hearts to the x10 HP (lives {cp.lives}/{cp.maxLives}, level {cp.level})");
            Check(Mathf.Abs(PlayerPrefs.GetFloat("CardTest.attack.1", 0f) - 30f) < 0.001f, "C: 1->2 converts the card-test attack presets x10");
            PlayerPrefs.DeleteKey(RunCheckpoint.Key); PlayerPrefs.DeleteKey("CardTest.attack.1");
            Check(Mathf.Abs(PlayerPrefs.GetFloat("BgmVolume", -1f) - 0.5f) < 0.001f && Mathf.Abs(PlayerPrefs.GetFloat("SfxVolume", -1f) - 0.25f) < 0.001f, "C: old volume levels converted (2/4 -> 0.5, 1/4 -> 0.25)");
            Check(!PlayerPrefs.HasKey("BgmVolumeLevel") && !PlayerPrefs.HasKey("SfxVolumeLevel"), "C: old duplicate volume keys removed");
            Check(PlayerPrefs.GetInt("TotalOwnedMile", 0) == 12345 && PlayerPrefs.GetString("OwnedCardsV1", "").Contains("\"level\":5") && PlayerPrefs.GetString("BestDistance_v2_wasteland_road", "") == "45678.5", "C: progress kept through the migration");
            // 将来の多段の移行(現行の版 → +1 → +2。2026-10-04: 3 はカード長期育成の実際の移行になったので、仮の手順はその先)
            int cur = SaveSystem.CurrentSchemaVersion;
            SaveSystem.TestSchemaTarget = cur + 2;
            SaveSystem.TestStep = from => { if (from == cur) PlayerPrefs.SetInt("TotalOwnedMile", PlayerPrefs.GetInt("TotalOwnedMile", 0) + 1); if (from == cur + 1) PlayerPrefs.SetString("DeckCardIds", PlayerPrefs.GetString("DeckCardIds", "") + ",x"); return true; };
            r = SaveSystem.Boot(0);
            Check(r.schemaAfter == cur + 2 && PlayerPrefs.GetInt("TotalOwnedMile", 0) == 12346 && PlayerPrefs.GetString("DeckCardIds", "").EndsWith(",x"), $"C: chained migration {cur}->{cur + 1}->{cur + 2} applied step by step ({r.report})");
            // 移行の失敗 → 移行前へ戻して旧い形式のまま起動
            PlayerPrefs.SetInt(SaveKeys.SchemaVersion, cur);
            var beforeFail = SaveSystem.Capture();
            SaveSystem.TestStep = from => { if (from == cur) { PlayerPrefs.SetInt("TotalOwnedMile", 1); return true; } return false; };
            r = SaveSystem.Boot(0);
            bool sameF = SameValues(beforeFail, SaveSystem.Capture(), out string diffF);
            Check(r.migrationFailed && r.schemaAfter == cur && sameF, $"C: a failed migration restores the pre-migration data and keeps the old format {diffF}");
            SaveSystem.TestSchemaTarget = 0; SaveSystem.TestStep = null;

            // ---- D: 将来の製品版(releaseGeneration 0→1)
            SeedDevSave();
            SaveSystem.Boot(0);
            r = SaveSystem.Boot(1);
            Check(r.kind == SaveSystem.BootKind.ReleaseReset && r.generationAfter == 1, $"D: release reset ran once ({r.kind}, {r.report})");
            Check(PlayerPrefs.GetInt("TotalOwnedMile", -1) == 0 && !PlayerPrefs.HasKey("OwnedCardsV1") && !PlayerPrefs.HasKey("DeckCardIds") && !PlayerPrefs.HasKey("BestDistance_v2_wasteland_road")
                && !PlayerPrefs.HasKey("UnlockedIds") && !PlayerPrefs.HasKey("SelectedCharacterId") && PlayerPrefs.GetFloat("BestDistance", 0f) == 0f, "D: progress reset (cards/deck/MILE/BEST/unlocks/selection)");
            Check(Mathf.Abs(PlayerPrefs.GetFloat("MasterVolume", 1f) - 0.3f) < 0.001f && PlayerPrefs.GetInt("ScreenShakeEnabled", 1) == 0 && Mathf.Abs(PlayerPrefs.GetFloat("HighSpeedAssistEngageKmh", 0f) - 120f) < 0.01f
                && PlayerPrefs.GetInt("PreferredOrientation", 0) == 1 && PlayerPrefs.GetString("net.lastHostIp", "") == "192.168.0.9", "D: settings kept (volume/shake/assist/orientation/net)");
            Check(!PlayerPrefs.HasKey("InvincibleMode"), "D: development-only values removed");
            bool backupFile = System.IO.Directory.Exists(backupDir) && System.IO.Directory.GetFiles(backupDir, "release_reset_*").Length > 0;
            Check(backupFile, "D: the data before the release reset is kept in a backup file");
            PlayerPrefs.SetInt("TotalOwnedMile", 500); PlayerPrefs.Save();
            r = SaveSystem.Boot(1);
            Check(r.kind == SaveSystem.BootKind.Existing && PlayerPrefs.GetInt("TotalOwnedMile", 0) == 500, "D: the second launch does not reset again");
            r = SaveSystem.Boot(0); // 古いビルドで開いても(世代が下がる)何も消さない
            Check(r.kind == SaveSystem.BootKind.Existing && PlayerPrefs.GetInt("TotalOwnedMile", 0) == 500 && PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, 0) == 1, "D: an older build never resets or lowers the generation");

            // ---- E〜H: 三姉妹/累計距離/ラスダン
            ClearRegistered(); SaveSystem.Boot(0);
            ProgressStats.MarkReaperMet(ReaperSister.Second);
            Check(!ProgressStats.HasMet(ReaperSister.Eldest) && ProgressStats.HasMet(ReaperSister.Second) && !ProgressStats.HasMet(ReaperSister.Youngest) && PlayerPrefs.GetInt("ReaperMet_Second", 0) == 1, "E: meeting one sister sets only her flag");
            ProgressStats.MarkReaperMet(ReaperSister.Eldest); ProgressStats.MarkReaperMet(ReaperSister.Youngest);
            // 2026-10-07: ラスダンの条件は「通常3マップそれぞれで死神に遭遇」+ 累計1,000,000m(UnlockRules)
            ProgressStats.DevSetLifetime(1000000);
            Check(!ProgressStats.FinalDungeonUnlocked, "F: all sisters met (no per-map record) + 1,000,000m -> still locked");
            ProgressStats.DevSetLifetime(999999);
            foreach (var m in UnlockRules.NormalMaps) UnlockRules.OnReaperMet(m);
            UnlockRules.EvaluateLastDungeon();
            Check(!ProgressStats.FinalDungeonUnlocked, "F: reaper on all 3 maps but 999,999m -> still locked");
            ProgressStats.DevAlwaysOpen = false;
            var lc = StageDatabase.FindById(BossManager.LastStageId);
            Check(lc != null && !StageDatabase.IsAvailable(lc), "F: LAST CORRIDOR not selectable while locked (dev 'always open' OFF)");
            ProgressStats.AddRunDistance(1.0);
            ProgressStats.Flush(true);
            Check(ProgressStats.FinalDungeonUnlocked && PlayerPrefs.GetInt(SaveKeys.FinalDungeonUnlocked, 0) == 1, "G: reaching 1,000,000m unlocks it and the flag is saved");
            Check(StageDatabase.IsAvailable(lc), "G: LAST CORRIDOR selectable");
            SaveSystem.Boot(0); // 再起動
            ProgressStats.DevSetLifetime(0); // 条件の値が変わっても
            Check(ProgressStats.FinalDungeonUnlocked && StageDatabase.IsAvailable(lc), "H: stays unlocked after a restart (not recalculated into a lock)");
            ProgressStats.DevAlwaysOpen = true;

            // ---- 破損: 直近の正常なバックアップから戻す / 無ければその項目だけ初期値
            SeedDevSave(); SaveSystem.Boot(0); // lastgood を作る
            string goodCards = PlayerPrefs.GetString("OwnedCardsV1", "");
            PlayerPrefs.SetString("OwnedCardsV1", "{\"stacks\":[{\"cardId\":");
            PlayerPrefs.SetInt("TotalOwnedMile", -50);
            PlayerPrefs.SetFloat("BestDistance", float.NaN);
            PlayerPrefs.SetString("BestDistance_v2_wasteland_road", "abc");
            PlayerPrefs.SetString(SaveKeys.LifetimeDistance, "xyz");
            PlayerPrefs.SetString("ActiveRunCheckpointV1", "not json");
            r = SaveSystem.Boot(0);
            Check(PlayerPrefs.GetString("OwnedCardsV1", "") == goodCards, $"corrupt: cards restored from the last good backup ({string.Join(" / ", r.repairs)})");
            Check(PlayerPrefs.GetInt("TotalOwnedMile", -1) == 0 && PlayerPrefs.GetFloat("BestDistance", -1f) == 0f, "corrupt: negative MILE / NaN BEST fixed");
            Check(PlayerPrefs.GetString("BestDistance_v2_wasteland_road", "") == "45678.5", "corrupt: broken stage BEST restored from backup");
            Check(System.IO.Directory.GetFiles(backupDir, "corrupt_*").Length > 0, "corrupt: the broken text is kept in a file");
            try { System.IO.File.Delete(System.IO.Path.Combine(backupDir, "lastgood.json")); System.IO.File.Delete(System.IO.Path.Combine(backupDir, "lastgood_prev.json")); } catch { }
            PlayerPrefs.SetString("OwnedCardsV1", "garbage");
            r = SaveSystem.Boot(0);
            Check(!PlayerPrefs.HasKey("OwnedCardsV1"), $"corrupt without a backup: only that item goes back to default, the game still starts ({string.Join(" / ", r.repairs)})");
            Check(PlayerPrefs.GetInt("TotalOwnedMile", -1) >= 0 && PlayerPrefs.GetString("DeckCardIds", "") == "attack_up,attack_up", "corrupt without a backup: other items untouched");

            // ---- I: 実際のラン(2026-10-08 仕様変更): 累計距離はラン中/一時停止/死亡/途中帰還では書かず、正規の帰還(成功)で確定する。遭遇は出現の時点で保存
            ClearRegistered(); SaveSystem.Boot(0);
            yield return ReloadSceneForSave();
            yield return BeginRun("swordsman", "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            SetKmh(150f);
            yield return new WaitForSeconds(8f);
            double life00 = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            double inMem = ProgressStats.LifetimeDistance;
            double saved = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            Check(gm.MaxDistance > 150 && System.Math.Abs(inMem - life00) < 0.01 && RunLedger.Current != null && RunLedger.Current.walked > 150, $"I: while running the distance stays in the run (ledger {RunLedger.Current?.walked:F0}m), lifetime not written ({saved:F0})");
            gm.SendMessage("OnApplicationPause", true);
            saved = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            Check(System.Math.Abs(saved - life00) < 0.01 && RunCheckpoint.Load().ledger != null && RunCheckpoint.Load().ledger.walked > 150, $"I: app pause saves the unconfirmed run (in the suspend data), not the lifetime ({saved:F1})");
            if (BossManager.Instance != null) { BossManager.Instance.enabled = true; BossManager.Instance.DebugSpawnReaper(); }
            yield return new WaitForSeconds(1f);
            Check(PlayerPrefs.GetInt("ReaperMet_Eldest", 0) == 1, "I: the reaper sister of the wasteland is recorded as met when she appears");
            // 死亡
            PlayerController.DebugSpeedScale = 1f;
            stopKeepAlive = true;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 1 });
            yield return null;
            float dBefore = gm.MaxDistance;
            gm.TryDamagePlayer(false, "qa-save");
            yield return new WaitForSecondsRealtime(0.8f);
            saved = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            Check(gm.IsGameOver && System.Math.Abs(saved - life00) < 0.01, $"I: a death does not add the run to the lifetime ({saved:F0}, run {dBefore:F0}m)");
            Check(PlayerPrefs.GetInt("ReaperMet_Eldest", 0) == 1, "I: the encounter stays recorded after dying in that run");
            yield return EndRun();
            // 途中帰還
            yield return BeginRun("swordsman", "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            double l0 = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            SetKmh(150f);
            yield return new WaitForSeconds(3f);
            PlayerController.DebugSpeedScale = 1f;
            float runD = gm.MaxDistance;
            gm.ReturnToHome();
            yield return new WaitForSecondsRealtime(0.3f);
            double l1 = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            Check(System.Math.Abs(l1 - l0) < 0.01, $"I: returning home (suspend) leaves the run unconfirmed (+{l1 - l0:F0} for a {runD:F0}m run)");
            yield return new WaitForSecondsRealtime(1.5f);
            // 成功(帰還)で確定
            gm = GameManager.Instance;
            RunCheckpoint.Clear();
            yield return BeginRun("swordsman", "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            SetKmh(150f);
            yield return new WaitForSeconds(3f);
            PlayerController.DebugSpeedScale = 1f;
            RunLedger.DevClearDebug();
            double walkedS = RunLedger.Current.walked;
            gm.Win();
            yield return new WaitForSecondsRealtime(0.5f);
            double l2 = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance);
            Check(walkedS > 50 && System.Math.Abs(l2 - l1 - walkedS) < 1.0, $"I: a successful return adds the distance actually run (+{l2 - l1:F0} / ran {walkedS:F0})");
            yield return EndRun();
        }
        finally
        {
            SaveSystem.TestSchemaTarget = 0; SaveSystem.TestStep = null;
            SaveSystem.Restore(original);
            SaveSystem.TestBackupSubdir = null;
            PlayerController.DebugSpeedScale = 1f;
            L("[save] the original save was restored");
        }
        var after = SaveSystem.Capture();
        Check(SameValues(original, after, out string diffR), "the tester's own save is back exactly as before " + diffR);
    }

    IEnumerator ReloadSceneForSave()
    {
        var old = GameManager.Instance;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    static bool SameValues(List<SaveSystem.Item> a, List<SaveSystem.Item> b, out string diff)
    {
        var da = new Dictionary<string, string>(); foreach (var it in a) da[it.k] = it.v;
        var db = new Dictionary<string, string>(); foreach (var it in b) db[it.k] = it.v;
        var bad = new List<string>();
        foreach (var kv in da)
        {
            if (kv.Key == SaveKeys.SchemaVersion || kv.Key == SaveKeys.ReleaseGeneration) continue;
            if (!db.TryGetValue(kv.Key, out string v)) bad.Add($"{kv.Key} missing");
            else if (v != kv.Value) bad.Add($"{kv.Key} {kv.Value}->{v}");
        }
        foreach (var kv in db)
        {
            if (kv.Key == SaveKeys.SchemaVersion || kv.Key == SaveKeys.ReleaseGeneration) continue;
            if (!da.ContainsKey(kv.Key) && !(kv.Key.StartsWith(SaveKeys.ReaperMetPrefix) || kv.Key == SaveKeys.LifetimeDistance || kv.Key == SaveKeys.FinalDungeonUnlocked || kv.Key.StartsWith("Tutorial.") || kv.Key.StartsWith("Unlock") || kv.Key.StartsWith(UnlockRules.ReachPrefix))) bad.Add($"{kv.Key} added"); // Tutorial.*: 初回チュートリアルの印(既存データには「案内済み」で1回だけ足す、2026-10-07)
        }
        diff = bad.Count == 0 ? "" : "(" + string.Join(", ", bad) + ")";
        return bad.Count == 0;
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 撮影: DEBUGのセーブ画面 / ラスダンがロックされたステージ選択。 -qaSaveShots <dir>
public partial class QaSweep
{
    IEnumerator SaveShotsMode()
    {
        var original = SaveSystem.Capture();
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3.5f);
        try
        {
            ProgressStats.DevSetLifetime(123456);
            ProgressStats.DevSetMet(ReaperSister.Eldest, true);
            DebugPanel.OpenStatic();
            SetPrivate(DebugPanel.Instance, "page", 1);
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("debug_save_page"); yield return null; yield return null;
            DebugPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.5f);
            ProgressStats.DevAlwaysOpen = false;
            gm.OpenStageSelect();
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("stage_select_final_locked"); yield return null; yield return null;
        }
        finally { SaveSystem.Restore(original); }
    }
}
#endif

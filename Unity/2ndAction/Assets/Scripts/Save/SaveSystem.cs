using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// セーブデータの起動時処理(2026-10-01)。ゲームが何かを読み込むより前(シーン読み込み前)に1回だけ走る。
//  ① 新規インストール … 製品版の初期状態(DefaultSave)を作る。
//  ② セーブ形式の移行(saveSchemaVersion) … 保存形式を変えた時に、旧い形式から1段ずつ最新へ変換する。
//     アプリの更新(versionCode)とは無関係。更新で進行が消えることはない。変換の前に必ずバックアップを取り、
//     失敗したらバックアップへ戻して旧い形式のまま起動する。
//  ③ リリース世代(releaseGeneration) … 製品版のビルドで BuildReleaseGeneration を 0→1 にした時だけ、
//     開発/テスト時代の進行を一度だけ初期化する(設定は残す)。終わったらセーブ側も1にするので二度と走らない。
//     ※ 今は 0(開発版)。開発中のデータは消えない。
//  ④ 検査と修復 … 壊れたJSON/異常な数値を見つけたら、直近の正常なバックアップから戻す(無ければその項目だけ初期値)。
//     読み込みに失敗したからといって全体を上書きしない。
//  バックアップ: Application.persistentDataPath/SaveBackups/(lastgood.json=毎回の起動で正常を確認した状態、
//  lastgood_prev.json=その1つ前、pre_migration_*.json / release_reset_*.json / corrupt_*.txt / orphan_*.json)。
//  ⓪ 2026-10-13 保存データの消失 … 進行のキーが1つも無いのに、進行の入った正常なバックアップが残っていれば、何かを書く前に
//     そこから戻す(最新が壊れていれば1つ前)。正当な新規開始(新規インストール/データの消去)はバックアップも無いので新規のまま。
//     意図した初期化(開発用の全初期化/テスト用データの消去)は、初期化の前にバックアップを別名へ移すので戻らない。
//     戻せない時も、残っているバックアップは orphan_* として残す(空の状態で上書きしない)。
//  バックアップの書き込みは一時ファイルに書き切ってから置き換える(書いている途中で落ちても、前の正常な控えが残る)。
public static class SaveSystem
{
    // 保存形式を変えたら上げて、Migrate に1段ぶんの変換を足す
    public const int CurrentSchemaVersion = 3; // 2: 戦闘数値10倍化(2026-10-02) / 3: カード長期育成 Mastery(2026-10-04)
    // 製品版(正式リリース)のビルドでだけ 1 にする。0=開発版。
    public const int BuildReleaseGeneration = 0;

    public enum BootKind { NewGame, Existing, ReleaseReset, RestoredFromBackup }
    public class BootResult
    {
        public BootKind kind;
        public int schemaBefore, schemaAfter, generationBefore, generationAfter;
        public bool migrationFailed;
        public string restoredFrom = "", restoredTime = ""; // ⓪ で戻したバックアップ(lastgood / lastgood_prev)と、その控えの時刻
        public List<string> preserved = new List<string>(); // ⓪ で戻せず、別名で残したバックアップ
        public List<string> repairs = new List<string>();
        public string report = "";
    }

    public static BootResult LastBoot { get; private set; }
    static bool booted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoBoot()
    {
        if (booted) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DebugRun.RecoverAfterCrash(); // 前回のデバッグラン(記録対象外)が終わらずに落ちていたら、進行を控えへ戻す
#endif
        int gen = BuildReleaseGeneration;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        gen = DevBootArgs(gen);
#endif
        try { Boot(gen); }
        catch (Exception e) { Debug.LogError("[Save] boot failed (the game continues with the data as it is): " + e); }
    }

    // ===================================================================== //
    public static BootResult Boot(int buildGeneration)
    {
        booted = true;
        if (SaveProfile.IsTest) Debug.Log("[Save] boot: TEST DATA profile");
        var r = new BootResult();
        var log = new StringBuilder();
        // 2026-10-14: データの引き継ぎ(読み込み)が途中で終わっていたら、何よりも先に同じ内容を最後まで適用する(新旧を混ぜない)
        try { SaveTransfer.RecoverIfPending(log); }
        catch (Exception e) { Debug.LogError("[Save] import recovery failed: " + e); }
        bool hasSchema = SaveStore.HasKey(SaveKeys.SchemaVersion);
        bool anyProgress = AnyProgressKey();

        // ⓪ 保存データの消失(2026-10-13): 進行のキーが1つも無い。正常なバックアップが残っていれば、初期値を書く前にそこから戻す
        if (!anyProgress)
        {
            var snap = FindRestorableBackup(out string src);
            if (snap != null)
            {
                int n = ApplyRestored(snap.items);
                SaveStore.Save(); // 戻した物を先に書き切る(この後で落ちても戻した状態から始まる)
                r.restoredFrom = src; r.restoredTime = snap.time ?? "";
                log.Append($"DATA LOSS: no progress keys{(hasSchema ? " (format key present)" : "")} - restored {n} keys from {src} ({snap.time}); ");
                Debug.LogWarning($"[Save] the progress keys were missing - restored {n} keys from the backup '{src}' saved at {snap.time}");
                hasSchema = SaveStore.HasKey(SaveKeys.SchemaVersion);
                anyProgress = AnyProgressKey();
            }
            else PreserveBackups("orphan", r.preserved, log); // 戻せる物が無い: 残っているバックアップは別名で残す(この後の起動で上書きしない)
        }

        r.schemaBefore = hasSchema ? SaveStore.GetInt(SaveKeys.SchemaVersion, 0) : 0;
        // リリース世代の記録が無い既存のセーブ = この仕組みより前の開発データ(世代0)
        r.generationBefore = SaveStore.HasKey(SaveKeys.ReleaseGeneration) ? SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0) : (hasSchema || anyProgress ? 0 : buildGeneration);

        if (!hasSchema && !anyProgress)
        {
            // ① 新規インストール
            r.kind = BootKind.NewGame;
            DefaultSave.WriteNewProgress();
            SaveStore.SetInt(SaveKeys.SchemaVersion, CurrentSchemaVersion);
            SaveStore.SetInt(SaveKeys.ReleaseGeneration, buildGeneration);
            log.Append($"new game (schema {CurrentSchemaVersion}, generation {buildGeneration})");
        }
        else
        {
            r.kind = BootKind.Existing;
            // ② セーブ形式の移行
            int schema = r.schemaBefore;
            int target = SchemaTarget;
            if (schema < target)
            {
                var before = Capture();
                WriteBackupFile($"pre_migration_v{schema}_{Stamp()}", before, true);
                try
                {
                    while (schema < target)
                    {
                        if (!MigrateStep(schema)) throw new Exception($"migration step {schema}->{schema + 1} reported failure");
                        schema++;
                        SaveStore.SetInt(SaveKeys.SchemaVersion, schema);
                        log.Append($"migrated {schema - 1}->{schema}; ");
                    }
                }
                catch (Exception e)
                {
                    // 失敗: 変換前の状態へ戻して、旧い形式のまま起動する(次回また変換を試みる)
                    Restore(before);
                    r.migrationFailed = true;
                    schema = r.schemaBefore;
                    log.Append($"MIGRATION FAILED ({e.Message}) - restored the pre-migration data; ");
                    Debug.LogError("[Save] migration failed, restored: " + e);
                }
            }
            else if (schema > target)
            {
                log.Append($"save is from a newer format ({schema} > {target}) - left as it is; ");
            }

            // ③ リリース世代: 製品版のビルドで初めて起動した時だけ、進行を一度だけ初期化(設定は残す)
            if (r.generationBefore < buildGeneration)
            {
                WriteBackupFile($"release_reset_gen{r.generationBefore}to{buildGeneration}_{Stamp()}", Capture(), false);
                DefaultSave.WriteNewProgress();
                foreach (var e in SaveKeys.All) if (e.cat == SaveCategory.Dev && !SharedWithOtherProfile(e.key)) SaveStore.DeleteKey(e.key);
                if (SaveStore.GetInt(SaveKeys.SchemaVersion, 0) < CurrentSchemaVersion) SaveStore.SetInt(SaveKeys.SchemaVersion, CurrentSchemaVersion);
                r.kind = BootKind.ReleaseReset;
                log.Append($"RELEASE RESET generation {r.generationBefore}->{buildGeneration} (progress reset, settings kept); ");
            }
            else if (r.generationBefore > buildGeneration)
            {
                log.Append($"save generation {r.generationBefore} is newer than this build ({buildGeneration}) - nothing reset; ");
            }
            SaveStore.SetInt(SaveKeys.ReleaseGeneration, Math.Max(r.generationBefore, buildGeneration));
            if (r.kind == BootKind.Existing && r.restoredFrom != "") r.kind = BootKind.RestoredFromBackup;
        }

        // ④ 検査と修復
        Validate(r.repairs);
        log.Append(TutorialProgress.EnsureInitialized());
        log.Append(UnlockRules.EnsureInitialized()); // 解放条件(2026-10-07): 既存データは今使える物をそのまま解放済みに // 初回チュートリアル(2026-10-07): 既存のデータには初回の案内を出さない
        if (r.repairs.Count > 0) log.Append("repairs: " + string.Join(" / ", r.repairs) + "; ");

        SaveStore.Save();
        r.schemaAfter = SaveStore.GetInt(SaveKeys.SchemaVersion, 0);
        r.generationAfter = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0);
        r.report = log.ToString();
        LastBoot = r;
        ReloadCaches();
        // 2026-10-13 第3弾: 成功の確定の書きかけ(落ちた等)が残っていればやり直す(欠けも二重もなく)
        try { if (CommitJournal.Pending) { CommitJournal.RecoverIfPending(); ReloadCaches(); } }
        catch (Exception e) { Debug.LogError("[Save] commit journal recovery failed: " + e); }
        UnlockRules.RequeuePending(); // 解放したのに確認していないお知らせ(アプリが落ちても失わない)
        // 正常を確認できた状態を残す(壊れた値を直した後の状態)
        RotateLastGood();
        if (r.restoredFrom != "")
        {
            string when = DateTime.TryParse(r.restoredTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime t)
                ? t.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : r.restoredTime;
            // 戻したことは見落とされないように、ホームで OK を押すまで出すお知らせにする(数秒で消える帯にしない)
            // 2026-10-14: 1つ前の世代へ戻すこともあるので「最新の状態に戻った」と読めない文にする(戻した控えの時刻を必ず書く)。短い行に分けて縦横どちらでも折り返さない
            NoticeQueue.Enqueue("save_restored_" + r.restoredTime, Loc.T("セーブデータの復元"),
                Loc.T("セーブデータを復元しました。") + "\n" + Loc.F("{0} 時点の控えから戻しています。", when) + "\n" + Loc.T("それ以降の進行は含まれない場合があります。"), null);
        }
        Debug.Log($"[Save] boot: {r.kind} schema {r.schemaBefore}->{r.schemaAfter} generation {r.generationBefore}->{r.generationAfter} | {r.report}");
        return r;
    }

    public static int SchemaTarget => TestSchemaTarget > 0 ? TestSchemaTarget : CurrentSchemaVersion;

    // ---- 1段ぶんの変換。from→from+1。成功でtrue ----
    static bool MigrateStep(int from)
    {
        switch (from)
        {
            case 0: return Migrate0To1();
            case 1: return Migrate1To2();
            case 2: return Migrate2To3();
            default:
                return TestStep != null ? TestStep(from) : false;
        }
    }

    // 2→3: カード長期育成(Mastery / AWAKENED、2026-10-04)。所持カードの Lv は一切変えない。
    //  ・新しいキー CardMasteryV1 を作る。今所持している Lv9 のカードは「Lv9 到達済み」として記録し、Mastery は全カード ★0 から
    //  ・過去に Lv9 の後で消費/変換したカードの枚数は保存されていないので、推測で Mastery を付けない
    static bool Migrate2To3()
    {
        int n = CardMastery.MigrateMarkMaxFromInventoryJson();
        Debug.Log($"[Save] 2->3 mastery: recorded {n} card(s) already at Lv9 MAX (mastery starts at ★0)");
        return true;
    }

    // 0→1: この仕組みの導入。進行の値は変えない。
    //  ・旧い0〜4段階の音量(…VolumeLevel)を0〜1の値へ読み替えて、旧キーを消す(同じ意味のデータを2か所に持たない)
    //  ・新しい進行(累計距離/三姉妹/ラスダン)は無ければ初期値
    static bool Migrate0To1()
    {
        string[] news = { "MasterVolume", "BgmVolume", "SfxVolume", "EnvVolume" };
        for (int i = 0; i < SaveKeys.Legacy.Length; i++)
        {
            string old = SaveKeys.Legacy[i];
            if (!SaveStore.HasKey(old)) continue;
            if (!SaveStore.HasKey(news[i]))
                SaveStore.SetFloat(news[i], Mathf.Clamp(SaveStore.GetInt(old, AudioManager.MaxVolumeLevel), 0, AudioManager.MaxVolumeLevel) / (float)AudioManager.MaxVolumeLevel);
            SaveStore.DeleteKey(old);
        }
        if (!SaveStore.HasKey(SaveKeys.LifetimeDistance)) SaveStore.SetString(SaveKeys.LifetimeDistance, "0");
        foreach (var s in ProgressStats.Sisters) if (!SaveStore.HasKey(SaveKeys.ReaperMetPrefix + s)) SaveStore.SetInt(SaveKeys.ReaperMetPrefix + s, 0);
        if (!SaveStore.HasKey(SaveKeys.FinalDungeonUnlocked)) SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, 0);
        return true;
    }

    // 1→2: 戦闘数値の10倍化(2026-10-02)。保存している値のうち、HPの単位が変わる物だけ読み替える。
    //  ・中断中のラン(CONTINUE)の lives / maxLives(ハートの数 → HP、×10)。攻撃力は保存していない(カードを再適用して作り直す)
    //  ・開発版のカード調整パネルの攻撃力/最大HPの候補値(CardTest.attack/hp.<0..2>、×10)
    // カードの所持/デッキ/MILE/BEST等は単位が変わらないので触らない。
    static bool Migrate1To2()
    {
        string json = SaveStore.GetString(RunCheckpoint.Key, "");
        if (!string.IsNullOrEmpty(json))
        {
            var d = JsonUtility.FromJson<RunCheckpoint.Data>(json);
            if (d == null) return false;
            if (d.lives > 0) d.lives *= CombatScale.K;
            if (d.maxLives > 0) d.maxLives *= CombatScale.K;
            SaveStore.SetString(RunCheckpoint.Key, JsonUtility.ToJson(d));
        }
        foreach (string k in new[] { "attack", "hp" })
            for (int i = 0; i < 3; i++)
            {
                string key = $"CardTest.{k}.{i}";
                if (SaveStore.HasKey(key)) SaveStore.SetFloat(key, SaveStore.GetFloat(key) * CombatScale.K);
            }
        return true;
    }

    // ===================================================================== //
    // 検査と修復
    // ===================================================================== //
    static void Validate(List<string> repairs)
    {
        Lazy<Dictionary<string, Item>> lastGood = new Lazy<Dictionary<string, Item>>(() => ToDict(FindRestorableBackup(out _, false)));

        // JSON: 所持カード
        ValidateJson<CardInventory.SaveWrapper>("OwnedCardsV1", w => w != null && w.stacks != null, repairs, lastGood);
        ValidateJson<CardMastery.SaveWrapper>(CardMastery.SaveKey, w => w != null && w.cards != null, repairs, lastGood);
        SanitizeCards(repairs);
        // JSON: 中断中のラン
        ValidateJson<RunCheckpoint.Data>("ActiveRunCheckpointV1", d => d != null, repairs, lastGood);

        // 数値
        if (SaveStore.GetInt("TotalOwnedMile", 0) < 0) { SaveStore.SetInt("TotalOwnedMile", 0); repairs.Add("MILE<0 -> 0"); }
        foreach (string k in new[] { "BestDistance", "BestTime", "BestDistance_legacyBackup" })
        {
            if (!SaveStore.HasKey(k)) continue;
            float v = SaveStore.GetFloat(k, 0f);
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) { SaveStore.SetFloat(k, 0f); repairs.Add($"{k}={v} -> 0"); }
        }
        foreach (string k in SaveKeys.StageBestKeys())
        {
            if (!SaveStore.HasKey(k)) continue;
            string s = SaveStore.GetString(k, "");
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
            {
                if (!RestoreKey(k, lastGood.Value, repairs)) { SaveStore.DeleteKey(k); repairs.Add($"{k}='{s}' removed"); }
            }
        }
        if (SaveStore.HasKey(SaveKeys.LifetimeDistance))
        {
            string s = SaveStore.GetString(SaveKeys.LifetimeDistance, "");
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
            {
                if (!RestoreKey(SaveKeys.LifetimeDistance, lastGood.Value, repairs)) { SaveStore.SetString(SaveKeys.LifetimeDistance, "0"); repairs.Add($"LifetimeDistance='{s}' -> 0"); }
            }
        }
        foreach (var e in SaveKeys.All)
        {
            if (e.type == SaveType.Float && e.cat == SaveCategory.Settings && SaveStore.HasKey(e.key))
            {
                float v = SaveStore.GetFloat(e.key, 0f);
                if (float.IsNaN(v) || float.IsInfinity(v)) { SaveStore.DeleteKey(e.key); repairs.Add($"{e.key}={v} removed"); }
            }
        }
    }

    static void ValidateJson<T>(string key, Func<T, bool> ok, List<string> repairs, Lazy<Dictionary<string, Item>> lastGood) where T : class
    {
        if (!SaveStore.HasKey(key)) return;
        string raw = SaveStore.GetString(key, "");
        if (string.IsNullOrEmpty(raw)) return;
        if (TryParse(raw, ok)) return;
        // 壊れている: 中身をファイルへ退避してから、直近の正常なバックアップへ戻す(無ければその項目だけ消す=初期値)
        WriteText($"corrupt_{key}_{Stamp()}.txt", raw);
        if (RestoreKey(key, lastGood.Value, repairs, v => TryParse(v, ok))) return;
        SaveStore.DeleteKey(key);
        repairs.Add($"{key} was corrupt and no backup was usable -> reset to default");
    }

    static bool TryParse<T>(string raw, Func<T, bool> ok) where T : class
    {
        try { var v = JsonUtility.FromJson<T>(raw); return ok(v); }
        catch { return false; }
    }

    static void SanitizeCards(List<string> repairs)
    {
        string raw = SaveStore.GetString("OwnedCardsV1", "");
        if (string.IsNullOrEmpty(raw)) return;
        CardInventory.SaveWrapper w;
        try { w = JsonUtility.FromJson<CardInventory.SaveWrapper>(raw); } catch { return; }
        if (w == null || w.stacks == null) return;
        int fixedCount = 0;
        for (int i = w.stacks.Count - 1; i >= 0; i--)
        {
            var s = w.stacks[i];
            if (s == null || string.IsNullOrEmpty(s.cardId)) { w.stacks.RemoveAt(i); fixedCount++; continue; }
            int lv = Mathf.Clamp(s.level, 1, CardInventory.MaxCardLevel);
            if (lv != s.level) { s.level = lv; fixedCount++; }
            if (s.count < 0) { s.count = 0; fixedCount++; }
        }
        if (fixedCount > 0) { SaveStore.SetString("OwnedCardsV1", JsonUtility.ToJson(w)); repairs.Add($"cards: fixed {fixedCount} invalid entries"); }
    }

    static bool RestoreKey(string key, Dictionary<string, Item> backup, List<string> repairs, Func<string, bool> accept = null)
    {
        if (backup == null || !backup.TryGetValue(key, out Item it)) return false;
        if (accept != null && !accept(it.v)) return false;
        Apply(it);
        repairs.Add($"{key} restored from the last good backup");
        return true;
    }

    // ===================================================================== //
    // スナップショット / バックアップ
    // ===================================================================== //
    [Serializable] public class Item { public string k; public int t; public string v; }
    [Serializable] class Snapshot { public string reason; public string time; public int schema; public int generation; public List<Item> items = new List<Item>(); }

    public static List<Item> Capture()
    {
        var list = new List<Item>();
        var seen = new HashSet<string>();
        foreach (var e in SaveKeys.Expanded())
        {
            if (!seen.Add(e.key) || !SaveStore.HasKey(e.key)) continue;
            list.Add(Read(e.key, e.type));
        }
        foreach (string k in SaveKeys.Legacy) if (SaveStore.HasKey(k)) list.Add(Read(k, SaveType.Int));
        return list;
    }

    public static Item ReadItem(string key, SaveType t) => Read(key, t);
    public static void ApplyItem(Item it) => Apply(it);
    public static string BackupDirectory => Dir;
    public static void WriteFileAtomic(string path, string text) => WriteAtomic(path, text);
    // 名前を付けた全キーの控え(データの引き継ぎの直前など。prefix + 日時)。書けたら true。同じ接頭辞の古い物は keep 個まで残す
    public static bool WriteNamedBackup(string prefix, int keep)
    {
        string name = prefix + Stamp();
        try
        {
            var s = new Snapshot { reason = name, time = DateTime.Now.ToString("s"), schema = SaveStore.GetInt(SaveKeys.SchemaVersion, 0), generation = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0), items = Capture() };
            WriteAtomic(System.IO.Path.Combine(Dir, name + ".json"), JsonUtility.ToJson(s, true));
            Prune(prefix, keep);
            return true;
        }
        catch (Exception e) { Debug.LogWarning("[Save] named backup failed: " + e.Message); return false; }
    }

    static Item Read(string key, SaveType t)
    {
        string v = t == SaveType.Int ? SaveStore.GetInt(key).ToString(System.Globalization.CultureInfo.InvariantCulture)
                 : t == SaveType.Float ? SaveStore.GetFloat(key).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                 : SaveStore.GetString(key);
        return new Item { k = key, t = (int)t, v = v };
    }

    static void Apply(Item it)
    {
        switch ((SaveType)it.t)
        {
            case SaveType.Int: if (int.TryParse(it.v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i)) SaveStore.SetInt(it.k, i); break;
            case SaveType.Float: if (float.TryParse(it.v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) SaveStore.SetFloat(it.k, f); break;
            default: SaveStore.SetString(it.k, it.v ?? ""); break;
        }
    }

    // 1つの分類(進行など)だけを控える / 戻す(DebugRun)。戻す時は控えと違うキーだけ書き換え、その数を返す。
    public static List<Item> CaptureCategory(SaveCategory cat)
    {
        var list = new List<Item>();
        var seen = new HashSet<string>();
        foreach (var e in SaveKeys.Expanded())
        {
            if (e.cat != cat || !seen.Add(e.key) || !SaveStore.HasKey(e.key)) continue;
            list.Add(Read(e.key, e.type));
        }
        return list;
    }

    public static int RestoreCategory(List<Item> snap, SaveCategory cat, out string note)
    {
        var want = new Dictionary<string, Item>();
        foreach (var it in snap) want[it.k] = it;
        var changed = new List<string>();
        var seen = new HashSet<string>();
        foreach (var e in SaveKeys.Expanded())
        {
            if (e.cat != cat || !seen.Add(e.key)) continue;
            bool has = SaveStore.HasKey(e.key);
            if (want.TryGetValue(e.key, out Item it))
            {
                if (!has || Read(e.key, e.type).v != it.v) { Apply(it); changed.Add(e.key); }
            }
            else if (has) { SaveStore.DeleteKey(e.key); changed.Add(e.key + "(deleted)"); }
        }
        note = string.Join(", ", changed);
        if (changed.Count > 0) SaveStore.Save();
        ReloadCaches();
        return changed.Count;
    }

    // 登録済みの全キーをスナップショットの状態にする(スナップショットに無いキーは消す)
    public static void Restore(List<Item> snap)
    {
        var keep = new HashSet<string>();
        foreach (var it in snap) keep.Add(it.k);
        foreach (var e in SaveKeys.Expanded()) if (!keep.Contains(e.key)) SaveStore.DeleteKey(e.key);
        foreach (string k in SaveKeys.Legacy) if (!keep.Contains(k)) SaveStore.DeleteKey(k);
        foreach (var it in snap) Apply(it);
        SaveStore.Save();
        ReloadCaches();
    }

    static string Dir
    {
        get
        {
            string root = Application.persistentDataPath;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // -memSave(保存をメモリだけにする検証): バックアップもこのプロセス専用の一時フォルダへ(通常のデータのバックアップに触れない)
            if (MemorySaveStore.Active && TestBackupSubdir == null)
                root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OneMoreMile_memSave", System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            // -fileSave(保存を専用のファイルへ。再起動の確認用): バックアップもそのファイルの隣へ
            if (FileSaveStore.Active && TestBackupSubdir == null) root = FileSaveStore.Folder;
#endif
            string d = System.IO.Path.Combine(root, TestBackupSubdir ?? SaveProfile.BackupSubdir(SaveProfile.IsTest));
            try { System.IO.Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    static void WriteBackupFile(string name, List<Item> items, bool prune, string crashTag = null)
    {
        try
        {
            var s = new Snapshot { reason = name, time = DateTime.Now.ToString("s"), schema = SaveStore.GetInt(SaveKeys.SchemaVersion, 0), generation = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0), items = items };
            WriteAtomic(System.IO.Path.Combine(Dir, name + ".json"), JsonUtility.ToJson(s, true), crashTag);
            if (prune) Prune("pre_migration_", 5);
        }
        catch (Exception e) { Debug.LogWarning("[Save] backup write failed: " + e.Message); }
    }

    static void WriteText(string name, string text)
    {
        try { WriteAtomic(System.IO.Path.Combine(Dir, name), text); Prune("corrupt_", 10); }
        catch (Exception e) { Debug.LogWarning("[Save] write failed: " + e.Message); }
    }

    // 調べる/救済するための控えを残す(上書きしない名前で)。CommitJournal が捨てた控え等
    public static void KeepForInvestigation(string name, string text) => WriteText($"{name}_{Stamp()}.txt", text);

    // 2026-10-13: 書いている途中で落ちても元のファイルが壊れないように、一時ファイルに書き切って(ディスクへ書き出して)から置き換える
    static void WriteAtomic(string path, string text, string crashTag = null)
    {
        string tmp = path + ".tmp";
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        using (var fs = new System.IO.FileStream(tmp, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (crashTag != null && DevBackupCrashAt == crashTag + "-partial") { fs.Write(bytes, 0, bytes.Length / 2); fs.Flush(true); DevBackupCrash(crashTag + "-partial"); }
#endif
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (crashTag != null && DevBackupCrashAt == crashTag + "-tmp") DevBackupCrash(crashTag + "-tmp");
#endif
        if (!System.IO.File.Exists(path)) { System.IO.File.Move(tmp, path); return; }
        try { System.IO.File.Replace(tmp, path, null); }
        catch (Exception e)
        {
            // 置き換えが使えない環境: 消してから移す(この間に落ちても、もう一方の世代の控えと一時ファイルが残る)
            Debug.LogWarning("[Save] File.Replace failed, using delete+move: " + e.GetType().Name);
            System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }
    }

    static Snapshot ReadSnapshot(string name)
    {
        string p = System.IO.Path.Combine(Dir, name + ".json");
        try { return System.IO.File.Exists(p) ? ParseSnapshot(System.IO.File.ReadAllText(p)) : null; }
        catch (Exception e) { Debug.LogWarning($"[Save] backup '{name}' could not be read: " + e.Message); return null; }
    }

    // 読めて、項目が1つ以上ある物だけを正常な控えとする(書いている途中で切れた JSON は読めない)
    static Snapshot ParseSnapshot(string text)
    {
        try
        {
            var s = JsonUtility.FromJson<Snapshot>(text);
            return s != null && s.items != null && s.items.Count > 0 ? s : null;
        }
        catch { return null; }
    }

    static Dictionary<string, Item> ToDict(Snapshot s)
    {
        if (s == null) return null;
        var d = new Dictionary<string, Item>();
        foreach (var it in s.items) if (it != null && !string.IsNullOrEmpty(it.k)) d[it.k] = it;
        return d;
    }

    // 戻せる控え: 最新(lastgood)→ 1つ前(lastgood_prev)の順に、読めて進行が入っている物。requireProgress=false なら読めれば良い(項目単位の修復用)
    static Snapshot FindRestorableBackup(out string source, bool requireProgress = true)
    {
        foreach (string name in new[] { "lastgood", "lastgood_prev" })
        {
            var s = ReadSnapshot(name);
            if (s == null) { if (System.IO.File.Exists(System.IO.Path.Combine(Dir, name + ".json"))) Debug.LogWarning($"[Save] backup '{name}' is damaged - trying the previous one"); continue; }
            if (requireProgress && !HasProgress(s)) continue;
            source = name;
            return s;
        }
        source = null;
        return null;
    }

    static bool HasProgress(Snapshot s)
    {
        var prog = new HashSet<string>();
        foreach (var e in SaveKeys.Expanded()) if (e.cat == SaveCategory.Progress) prog.Add(e.key);
        foreach (var it in s.items) if (it != null && prog.Contains(it.k)) return true;
        return false;
    }

    // 消えた後に戻す: 控えの値を書く。設定が今も残っていれば今の値を残す(控えより新しい)
    static int ApplyRestored(List<Item> items)
    {
        var cat = new Dictionary<string, SaveCategory>();
        foreach (var e in SaveKeys.Expanded()) cat[e.key] = e.cat;
        int n = 0;
        foreach (var it in items)
        {
            if (it == null || string.IsNullOrEmpty(it.k)) continue;
            if (cat.TryGetValue(it.k, out SaveCategory c) && c == SaveCategory.Settings && SaveStore.HasKey(it.k)) continue;
            Apply(it);
            n++;
        }
        return n;
    }

    // 最新/1つ前の控えを、上書きされない別名へ移す(戻せなかった時 / 意図した初期化の前)。中身は消さない
    static void PreserveBackups(string prefix, List<string> moved, StringBuilder log)
    {
        string stamp = Stamp();
        foreach (string name in new[] { "lastgood", "lastgood_prev" })
        {
            string p = System.IO.Path.Combine(Dir, name + ".json");
            try
            {
                if (!System.IO.File.Exists(p)) continue;
                string to = System.IO.Path.Combine(Dir, $"{prefix}_{stamp}_{name}.json");
                if (System.IO.File.Exists(to)) to = System.IO.Path.Combine(Dir, $"{prefix}_{stamp}_{Guid.NewGuid().ToString("N").Substring(0, 6)}_{name}.json");
                System.IO.File.Move(p, to);
                moved?.Add(System.IO.Path.GetFileName(to));
            }
            catch (Exception e) { Debug.LogWarning($"[Save] could not keep the backup '{name}': " + e.Message); }
        }
        if (moved != null && moved.Count > 0)
        {
            log?.Append($"no usable backup - kept the old backup files as {string.Join(", ", moved)}; ");
            Debug.LogWarning($"[Save] the old backup files were kept as {string.Join(", ", moved)} (not overwritten)");
            Prune(prefix + "_", 20);
        }
    }

    // 起動のたびに「正常を確認した状態」を残す。最新の控えを1つ前へ回してから、新しい控えを書く。
    // どの時点で落ちても、正常な控えが少なくとも1つ残る(壊れた最新は1つ前へ回さず、corrupt_* として残す)
    static void RotateLastGood()
    {
        string cur = System.IO.Path.Combine(Dir, "lastgood.json"), prev = System.IO.Path.Combine(Dir, "lastgood_prev.json");
        try
        {
            if (System.IO.File.Exists(cur))
            {
                string text = System.IO.File.ReadAllText(cur);
                if (ParseSnapshot(text) != null) WriteAtomic(prev, text, "prev");
                else
                {
                    string to = System.IO.Path.Combine(Dir, $"corrupt_lastgood_{Stamp()}.json");
                    System.IO.File.Move(cur, to);
                    Debug.LogWarning($"[Save] the latest backup was damaged - kept it as {System.IO.Path.GetFileName(to)}, the previous one is left as it is");
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Save] backup rotation failed: " + e.Message); }
        WriteBackupFile("lastgood", Capture(), false, "cur");
    }

    static void Prune(string prefix, int keep)
    {
        try
        {
            var files = new List<string>(System.IO.Directory.GetFiles(Dir, prefix + "*"));
            files.Sort(StringComparer.Ordinal);
            for (int i = 0; i < files.Count - keep; i++) System.IO.File.Delete(files[i]);
        }
        catch { }
    }

    static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

    // 2026-10-14: テスト用データの間、設定/開発用の値の多くは通常のデータと共用(SaveProfile.SharedKey)。初期化でそれを消すと通常のデータまで変わるので消さない。
    // 製品版は常に通常のデータなので、この判定は常に false(今までと同じ)
    static bool SharedWithOtherProfile(string key) => SaveProfile.IsTest && SaveProfile.SharedKey(key);

    static bool AnyProgressKey()
    {
        foreach (var e in SaveKeys.Expanded()) if (e.cat == SaveCategory.Progress && SaveStore.HasKey(e.key)) return true;
        return false;
    }

    // 読み込み済みの値(static)を読み直させる。GameManager等のシーン上の物は次のシーン読み込みで読み直す。
    public static void ReloadCaches()
    {
        CardInventory.ReloadFromPrefs();
        CardMastery.ReloadFromPrefs();
        UnlockRules.Reload();
        RunCheckpoint.Reload();
        ProgressStats.Reload();
        AdRewards.Reload(); // 広告/パスの報酬の記録(テスト用データと通常のデータで別、2026-10-10)
        GameSettings.Reload();
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.ReloadPrefs();
    }

    // ===== テスト用(開発版の自動テストからだけ使う) =====
    public static int TestSchemaTarget;
    public static Func<int, bool> TestStep;
    public static string TestBackupSubdir;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 意図して全部を初期化する前に呼ぶ: 最新/1つ前の控えを別名(<reason>_*)へ移し、起動時の「消失からの復旧」で戻らないようにする(中身は消さない)
    public static void ForgetBackupsForIntentionalReset(string reason) => PreserveBackups(reason, new List<string>(), null);
    public static string BackupDir => Dir;
    public static bool BackupFileValid(string path) { try { return System.IO.File.Exists(path) && ParseSnapshot(System.IO.File.ReadAllText(path)) != null; } catch { return false; } }

    // 確認用(開発版): バックアップの書き込みの途中でプロセスを強制終了する(-qaBackupCrashAt prev-partial / prev-tmp / cur-partial / cur-tmp)。
    // 起動時のリリース世代を変える(-qaReleaseGen N)。どちらもテスト用データ(または -memSave)の時だけ効く(通常のデータには使わない)
    public static string DevBackupCrashAt = "";
    static int DevBootArgs(int gen)
    {
        var a = QaArgs.All;
        bool safe = SaveProfile.IsTest || MemorySaveStore.Active;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-qaBackupCrashAt") { if (safe) DevBackupCrashAt = a[i + 1]; else Debug.LogWarning("[Save] -qaBackupCrashAt ignored: only for the test data"); }
            if (a[i] == "-qaReleaseGen" && int.TryParse(a[i + 1], out int g)) { if (safe) { gen = g; Debug.Log($"[Save] DEV: boot with release generation {g}"); } else Debug.LogWarning("[Save] -qaReleaseGen ignored: only for the test data"); }
        }
        return gen;
    }
    static void DevBackupCrash(string at)
    {
        Debug.Log($"[Save] DEV CRASH while writing a backup at '{at}'");
#if UNITY_ANDROID && !UNITY_EDITOR
        UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.Abort); // Android は Process.Kill が使えない(2026-10-14)
#elif !UNITY_EDITOR
        System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ===== 開発用のリセット(DEBUGパネル) =====
    // 進行だけ初期化(設定は残す)
    public static void DevResetProgress()
    {
        WriteBackupFile($"dev_reset_progress_{Stamp()}", Capture(), false);
        DefaultSave.WriteNewProgress();
        SaveStore.Save();
        ReloadCaches();
        Debug.Log("[Save] DEV: progress reset (settings kept)");
    }

    // 設定も含めて完全に初期化(新規インストールと同じ状態)
    public static void DevResetAll()
    {
        WriteBackupFile($"dev_reset_all_{Stamp()}", Capture(), false);
        ForgetBackupsForIntentionalReset("dev_reset_all");
        foreach (var e in SaveKeys.Expanded()) if (!SharedWithOtherProfile(e.key)) SaveStore.DeleteKey(e.key);
        foreach (string k in SaveKeys.Legacy) if (!SharedWithOtherProfile(k)) SaveStore.DeleteKey(k);
        SaveStore.Save();
        Boot(BuildReleaseGeneration);
        Debug.Log("[Save] DEV: everything reset (same as a new install)");
    }
#endif
}

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
//  pre_migration_*.json / release_reset_*.json / corrupt_*.txt)。
public static class SaveSystem
{
    // 保存形式を変えたら上げて、Migrate に1段ぶんの変換を足す
    public const int CurrentSchemaVersion = 1;
    // 製品版(正式リリース)のビルドでだけ 1 にする。0=開発版。
    public const int BuildReleaseGeneration = 0;

    public enum BootKind { NewGame, Existing, ReleaseReset }
    public class BootResult
    {
        public BootKind kind;
        public int schemaBefore, schemaAfter, generationBefore, generationAfter;
        public bool migrationFailed;
        public List<string> repairs = new List<string>();
        public string report = "";
    }

    public static BootResult LastBoot { get; private set; }
    static bool booted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoBoot()
    {
        if (booted) return;
        try { Boot(BuildReleaseGeneration); }
        catch (Exception e) { Debug.LogError("[Save] boot failed (the game continues with the data as it is): " + e); }
    }

    // ===================================================================== //
    public static BootResult Boot(int buildGeneration)
    {
        booted = true;
        var r = new BootResult();
        var log = new StringBuilder();
        bool hasSchema = PlayerPrefs.HasKey(SaveKeys.SchemaVersion);
        bool anyProgress = AnyProgressKey();
        r.schemaBefore = hasSchema ? PlayerPrefs.GetInt(SaveKeys.SchemaVersion, 0) : 0;
        // リリース世代の記録が無い既存のセーブ = この仕組みより前の開発データ(世代0)
        r.generationBefore = PlayerPrefs.HasKey(SaveKeys.ReleaseGeneration) ? PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, 0) : (hasSchema || anyProgress ? 0 : buildGeneration);

        if (!hasSchema && !anyProgress)
        {
            // ① 新規インストール
            r.kind = BootKind.NewGame;
            DefaultSave.WriteNewProgress();
            PlayerPrefs.SetInt(SaveKeys.SchemaVersion, CurrentSchemaVersion);
            PlayerPrefs.SetInt(SaveKeys.ReleaseGeneration, buildGeneration);
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
                        PlayerPrefs.SetInt(SaveKeys.SchemaVersion, schema);
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
                foreach (var e in SaveKeys.All) if (e.cat == SaveCategory.Dev) PlayerPrefs.DeleteKey(e.key);
                if (PlayerPrefs.GetInt(SaveKeys.SchemaVersion, 0) < CurrentSchemaVersion) PlayerPrefs.SetInt(SaveKeys.SchemaVersion, CurrentSchemaVersion);
                r.kind = BootKind.ReleaseReset;
                log.Append($"RELEASE RESET generation {r.generationBefore}->{buildGeneration} (progress reset, settings kept); ");
            }
            else if (r.generationBefore > buildGeneration)
            {
                log.Append($"save generation {r.generationBefore} is newer than this build ({buildGeneration}) - nothing reset; ");
            }
            PlayerPrefs.SetInt(SaveKeys.ReleaseGeneration, Math.Max(r.generationBefore, buildGeneration));
        }

        // ④ 検査と修復
        Validate(r.repairs);
        if (r.repairs.Count > 0) log.Append("repairs: " + string.Join(" / ", r.repairs) + "; ");

        PlayerPrefs.Save();
        r.schemaAfter = PlayerPrefs.GetInt(SaveKeys.SchemaVersion, 0);
        r.generationAfter = PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, 0);
        r.report = log.ToString();
        LastBoot = r;
        ReloadCaches();
        // 正常を確認できた状態を残す(壊れた値を直した後の状態)
        RotateLastGood();
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
            default:
                return TestStep != null ? TestStep(from) : false;
        }
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
            if (!PlayerPrefs.HasKey(old)) continue;
            if (!PlayerPrefs.HasKey(news[i]))
                PlayerPrefs.SetFloat(news[i], Mathf.Clamp(PlayerPrefs.GetInt(old, AudioManager.MaxVolumeLevel), 0, AudioManager.MaxVolumeLevel) / (float)AudioManager.MaxVolumeLevel);
            PlayerPrefs.DeleteKey(old);
        }
        if (!PlayerPrefs.HasKey(SaveKeys.LifetimeDistance)) PlayerPrefs.SetString(SaveKeys.LifetimeDistance, "0");
        foreach (var s in ProgressStats.Sisters) if (!PlayerPrefs.HasKey(SaveKeys.ReaperMetPrefix + s)) PlayerPrefs.SetInt(SaveKeys.ReaperMetPrefix + s, 0);
        if (!PlayerPrefs.HasKey(SaveKeys.FinalDungeonUnlocked)) PlayerPrefs.SetInt(SaveKeys.FinalDungeonUnlocked, 0);
        return true;
    }

    // ===================================================================== //
    // 検査と修復
    // ===================================================================== //
    static void Validate(List<string> repairs)
    {
        Lazy<Dictionary<string, Item>> lastGood = new Lazy<Dictionary<string, Item>>(() => ReadBackupFile("lastgood"));

        // JSON: 所持カード
        ValidateJson<CardInventory.SaveWrapper>("OwnedCardsV1", w => w != null && w.stacks != null, repairs, lastGood);
        SanitizeCards(repairs);
        // JSON: 中断中のラン
        ValidateJson<RunCheckpoint.Data>("ActiveRunCheckpointV1", d => d != null, repairs, lastGood);

        // 数値
        if (PlayerPrefs.GetInt("TotalOwnedMile", 0) < 0) { PlayerPrefs.SetInt("TotalOwnedMile", 0); repairs.Add("MILE<0 -> 0"); }
        foreach (string k in new[] { "BestDistance", "BestTime", "BestDistance_legacyBackup" })
        {
            if (!PlayerPrefs.HasKey(k)) continue;
            float v = PlayerPrefs.GetFloat(k, 0f);
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) { PlayerPrefs.SetFloat(k, 0f); repairs.Add($"{k}={v} -> 0"); }
        }
        foreach (string k in SaveKeys.StageBestKeys())
        {
            if (!PlayerPrefs.HasKey(k)) continue;
            string s = PlayerPrefs.GetString(k, "");
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
            {
                if (!RestoreKey(k, lastGood.Value, repairs)) { PlayerPrefs.DeleteKey(k); repairs.Add($"{k}='{s}' removed"); }
            }
        }
        if (PlayerPrefs.HasKey(SaveKeys.LifetimeDistance))
        {
            string s = PlayerPrefs.GetString(SaveKeys.LifetimeDistance, "");
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
            {
                if (!RestoreKey(SaveKeys.LifetimeDistance, lastGood.Value, repairs)) { PlayerPrefs.SetString(SaveKeys.LifetimeDistance, "0"); repairs.Add($"LifetimeDistance='{s}' -> 0"); }
            }
        }
        foreach (var e in SaveKeys.All)
        {
            if (e.type == SaveType.Float && e.cat == SaveCategory.Settings && PlayerPrefs.HasKey(e.key))
            {
                float v = PlayerPrefs.GetFloat(e.key, 0f);
                if (float.IsNaN(v) || float.IsInfinity(v)) { PlayerPrefs.DeleteKey(e.key); repairs.Add($"{e.key}={v} removed"); }
            }
        }
    }

    static void ValidateJson<T>(string key, Func<T, bool> ok, List<string> repairs, Lazy<Dictionary<string, Item>> lastGood) where T : class
    {
        if (!PlayerPrefs.HasKey(key)) return;
        string raw = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(raw)) return;
        if (TryParse(raw, ok)) return;
        // 壊れている: 中身をファイルへ退避してから、直近の正常なバックアップへ戻す(無ければその項目だけ消す=初期値)
        WriteText($"corrupt_{key}_{Stamp()}.txt", raw);
        if (RestoreKey(key, lastGood.Value, repairs, v => TryParse(v, ok))) return;
        PlayerPrefs.DeleteKey(key);
        repairs.Add($"{key} was corrupt and no backup was usable -> reset to default");
    }

    static bool TryParse<T>(string raw, Func<T, bool> ok) where T : class
    {
        try { var v = JsonUtility.FromJson<T>(raw); return ok(v); }
        catch { return false; }
    }

    static void SanitizeCards(List<string> repairs)
    {
        string raw = PlayerPrefs.GetString("OwnedCardsV1", "");
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
        if (fixedCount > 0) { PlayerPrefs.SetString("OwnedCardsV1", JsonUtility.ToJson(w)); repairs.Add($"cards: fixed {fixedCount} invalid entries"); }
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
            if (!seen.Add(e.key) || !PlayerPrefs.HasKey(e.key)) continue;
            list.Add(Read(e.key, e.type));
        }
        foreach (string k in SaveKeys.Legacy) if (PlayerPrefs.HasKey(k)) list.Add(Read(k, SaveType.Int));
        return list;
    }

    static Item Read(string key, SaveType t)
    {
        string v = t == SaveType.Int ? PlayerPrefs.GetInt(key).ToString(System.Globalization.CultureInfo.InvariantCulture)
                 : t == SaveType.Float ? PlayerPrefs.GetFloat(key).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                 : PlayerPrefs.GetString(key);
        return new Item { k = key, t = (int)t, v = v };
    }

    static void Apply(Item it)
    {
        switch ((SaveType)it.t)
        {
            case SaveType.Int: if (int.TryParse(it.v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i)) PlayerPrefs.SetInt(it.k, i); break;
            case SaveType.Float: if (float.TryParse(it.v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) PlayerPrefs.SetFloat(it.k, f); break;
            default: PlayerPrefs.SetString(it.k, it.v ?? ""); break;
        }
    }

    // 登録済みの全キーをスナップショットの状態にする(スナップショットに無いキーは消す)
    public static void Restore(List<Item> snap)
    {
        var keep = new HashSet<string>();
        foreach (var it in snap) keep.Add(it.k);
        foreach (var e in SaveKeys.Expanded()) if (!keep.Contains(e.key)) PlayerPrefs.DeleteKey(e.key);
        foreach (string k in SaveKeys.Legacy) if (!keep.Contains(k)) PlayerPrefs.DeleteKey(k);
        foreach (var it in snap) Apply(it);
        PlayerPrefs.Save();
        ReloadCaches();
    }

    static string Dir
    {
        get
        {
            string d = System.IO.Path.Combine(Application.persistentDataPath, TestBackupSubdir ?? "SaveBackups");
            try { System.IO.Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    static void WriteBackupFile(string name, List<Item> items, bool prune)
    {
        try
        {
            var s = new Snapshot { reason = name, time = DateTime.Now.ToString("s"), schema = PlayerPrefs.GetInt(SaveKeys.SchemaVersion, 0), generation = PlayerPrefs.GetInt(SaveKeys.ReleaseGeneration, 0), items = items };
            System.IO.File.WriteAllText(System.IO.Path.Combine(Dir, name + ".json"), JsonUtility.ToJson(s, true));
            if (prune) Prune("pre_migration_", 5);
        }
        catch (Exception e) { Debug.LogWarning("[Save] backup write failed: " + e.Message); }
    }

    static void WriteText(string name, string text)
    {
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Dir, name), text); Prune("corrupt_", 10); }
        catch (Exception e) { Debug.LogWarning("[Save] write failed: " + e.Message); }
    }

    static Dictionary<string, Item> ReadBackupFile(string name)
    {
        try
        {
            string p = System.IO.Path.Combine(Dir, name + ".json");
            if (!System.IO.File.Exists(p)) return null;
            var s = JsonUtility.FromJson<Snapshot>(System.IO.File.ReadAllText(p));
            if (s == null || s.items == null) return null;
            var d = new Dictionary<string, Item>();
            foreach (var it in s.items) if (it != null && !string.IsNullOrEmpty(it.k)) d[it.k] = it;
            return d;
        }
        catch (Exception e) { Debug.LogWarning("[Save] backup read failed: " + e.Message); return null; }
    }

    static void RotateLastGood()
    {
        try
        {
            string cur = System.IO.Path.Combine(Dir, "lastgood.json"), prev = System.IO.Path.Combine(Dir, "lastgood_prev.json");
            if (System.IO.File.Exists(cur)) System.IO.File.Copy(cur, prev, true);
        }
        catch { }
        WriteBackupFile("lastgood", Capture(), false);
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

    static bool AnyProgressKey()
    {
        foreach (var e in SaveKeys.Expanded()) if (e.cat == SaveCategory.Progress && PlayerPrefs.HasKey(e.key)) return true;
        return false;
    }

    // 読み込み済みの値(static)を読み直させる。GameManager等のシーン上の物は次のシーン読み込みで読み直す。
    public static void ReloadCaches()
    {
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        ProgressStats.Reload();
        GameSettings.Reload();
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.ReloadPrefs();
    }

    // ===== テスト用(開発版の自動テストからだけ使う) =====
    public static int TestSchemaTarget;
    public static Func<int, bool> TestStep;
    public static string TestBackupSubdir;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ===== 開発用のリセット(DEBUGパネル) =====
    // 進行だけ初期化(設定は残す)
    public static void DevResetProgress()
    {
        WriteBackupFile($"dev_reset_progress_{Stamp()}", Capture(), false);
        DefaultSave.WriteNewProgress();
        PlayerPrefs.Save();
        ReloadCaches();
        Debug.Log("[Save] DEV: progress reset (settings kept)");
    }

    // 設定も含めて完全に初期化(新規インストールと同じ状態)
    public static void DevResetAll()
    {
        WriteBackupFile($"dev_reset_all_{Stamp()}", Capture(), false);
        foreach (var e in SaveKeys.Expanded()) PlayerPrefs.DeleteKey(e.key);
        foreach (string k in SaveKeys.Legacy) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.Save();
        Boot(BuildReleaseGeneration);
        Debug.Log("[Save] DEV: everything reset (same as a new install)");
    }
#endif
}

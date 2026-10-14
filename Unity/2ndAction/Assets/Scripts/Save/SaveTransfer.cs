using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// 機種変更に備えたセーブデータの書き出し・読み込み(手動の引き継ぎ、2026-10-14)。クラウド同期/アカウント連携は含まない。
//
//  ・引き継ぐ物: SaveKeys の「進行」(Progress)の全キー + マップ別の進捗キー(RunReachV1_/ReaperMapV1_)。ただし下の NotTransferred を除く。
//    新しい進行のキーを SaveKeys に登録すれば自動で引き継ぎの対象になる(引き継がない物だけを NotTransferred に書く)。
//  ・引き継がない物: 設定(画面/音/操作/描画など端末ごと)、開発用の値、管理情報、中断中のラン、確定の途中の控え、
//    ランキングの参加/投稿待ち/確定済みの自己ベスト/送信済み(端末のアカウントに結びつく)。認証の ID は SaveStore に無い(各 SDK が持つ)。
//  ・ファイル: UTF-8 の JSON。形式名 + 形式の版(FormatVersion)+ 保存日時 + アプリの版 + セーブ形式(saveSchema)+ 項目の一覧 + 検査値。
//    機種に依存しない(iPhone/PC でも同じ形を読む予定。実機の相互移行は未確認)。
//  ・読み込み: ファイル全体を検査(形式・版・検査値・項目の種類・値の範囲・要約の一致)してから、確認の後に「置き換え」(合算しない)。
//    直前の状態を pre_import_*.json に控え、適用する内容を import_pending.json に書き切ってから適用する。
//    適用の途中で落ちても、次の起動(SaveSystem.Boot の最初)で同じ内容を最後まで適用する(新旧が混ざらない。何度適用しても同じ結果)。
//  ・検査値: HMAC-SHA256(鍵はアプリに入っている)。壊れたファイル/手で書き換えたファイルは検出するが、アプリを解析して鍵を取り出せば
//    作り直せるので、不正対策としては不十分(ランキングはサーバー側の検査が本体。引き継いだ記録はランキングへ投稿しない)。
public static class SaveTransfer
{
    public const string Format = "OneMoreMile.SaveTransfer";
    public const int FormatVersion = 1;
    public const int MaxFileBytes = 4 * 1024 * 1024;
    const int MaxValueChars = 512 * 1024;

    // 進行のうち引き継がない物。clear=true は読み込み先でも消す(読み込んだ進行に古い控えを残さない)、false は読み込み先の値を残す
    public static readonly (string key, bool clear, string why)[] NotTransferred =
    {
        (RunCheckpoint.Key, true, "中断中のラン(ホームで引き継ぐ仕様。読み込み先の中断データは消す)"),
        (CommitJournal.Key, true, "成功の確定の途中の控え(読み込み先の控えは消す。古い MILE/記録が後から足されないように)"),
        (Leaderboard.JoinedKey, false, "ランキングへの参加の同意(端末のアカウントごと)"),
        (Leaderboard.PendingKey, false, "ランキングの投稿待ち(端末のアカウントで送る物)"),
        (Leaderboard.VerifiedKey, false, "ランキングの資格のある自己ベスト(端末のアカウントの物)"),
        (Leaderboard.SentKey, false, "ランキングへ送り終えたランの ID"),
    };

    // ===================================================================== 引き継ぐキー
    public struct KeyInfo { public string key; public SaveType type; }
    public static List<KeyInfo> TransferKeys()
    {
        var skip = new HashSet<string>();
        foreach (var n in NotTransferred) skip.Add(n.key);
        var list = new List<KeyInfo>();
        var seen = new HashSet<string>();
        foreach (var e in SaveKeys.Expanded())
            if (e.cat == SaveCategory.Progress && !skip.Contains(e.key) && seen.Add(e.key)) list.Add(new KeyInfo { key = e.key, type = e.type });
        // マップ別の進捗(登録表は通常3マップだけ。マップが増えても拾う)
        foreach (var id in StageIds())
        {
            if (seen.Add(UnlockRules.ReachPrefix + id)) list.Add(new KeyInfo { key = UnlockRules.ReachPrefix + id, type = SaveType.String });
            if (seen.Add(UnlockRules.ReaperMapPrefix + id)) list.Add(new KeyInfo { key = UnlockRules.ReaperMapPrefix + id, type = SaveType.Int });
        }
        return list;
    }

    static IEnumerable<string> StageIds()
    {
        var ids = new List<string> { UnlockRules.Wasteland, UnlockRules.Cave, UnlockRules.Sky, BossManager.LastStageId };
        try { foreach (var s in StageDatabase.AllStages) if (s != null && !string.IsNullOrEmpty(s.stageId) && !ids.Contains(s.stageId)) ids.Add(s.stageId); } catch { }
        return ids;
    }

    // ===================================================================== ファイルの形
    [Serializable]
    public class Summary
    {
        public int mile;
        public int cardKinds, cardTotal, deckCount;
        public string stages = "", chars = "";
        public string bestDistance = "0", lifetime = "0";
        public int finalDungeon;
        public string mapBests = ""; // id=m;id=m
    }

    [Serializable]
    public class FileData
    {
        public string format = ""; // 書き出しで Format を入れる(既定値にすると別の JSON も形式名が合ってしまう)
        public int formatVersion = FormatVersion;
        public string app = "One More Mile";
        public string appVersion = "";
        public string platform = "";
        public string savedAtUtc = "";   // ISO 8601(UTC)
        public string savedAtLocal = ""; // 書き出した端末の時刻(表示用)
        public int saveSchema;
        public int releaseGeneration;
        public bool testData;            // 開発版のテスト用データから書き出した
        public Summary summary = new Summary();
        public int itemCount;
        public List<SaveSystem.Item> items = new List<SaveSystem.Item>();
        public string hash = "";
    }

    public class Checked
    {
        public bool ok;
        public string error = "";   // 表示用(日本語の元の文。Loc.T で訳す)
        public string detail = "";  // ログ用(値は出さない)
        public FileData data;
        public string text;         // 検査したファイルの中身(適用の控えに書く)
    }

    // ===================================================================== 書き出し
    // ホームでだけ。書き出したファイルを自分で検査して、読み込める物だけを返す
    public static Checked Export()
    {
        var r = new Checked();
        if (!CanRunNow(out string why)) { r.error = why; return r; }
        if (CommitJournal.Pending) { r.error = "前回のランの結果を反映しています。アプリを起動し直してから書き出してください。"; return r; }
        ProgressStats.Flush(true);
        SaveStore.Save();
        var f = new FileData
        {
            format = Format,
            appVersion = Application.version,
            platform = Application.platform.ToString(),
            savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            savedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            saveSchema = SaveStore.GetInt(SaveKeys.SchemaVersion, 0),
            releaseGeneration = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0),
            testData = SaveProfile.IsTest,
        };
        foreach (var k in TransferKeys())
            if (SaveStore.HasKey(k.key)) f.items.Add(SaveSystem.ReadItem(k.key, k.type));
        f.itemCount = f.items.Count;
        f.summary = Summarize(ToDict(f.items));
        f.hash = Sign(f);
        string text = JsonUtility.ToJson(f, true);
        var back = Check(text);
        if (!back.ok) { r.error = "書き出したデータを確認できませんでした。もう一度お試しください。"; r.detail = "self-check failed: " + back.detail; Debug.LogError("[SaveTransfer] export self-check failed: " + back.detail); return r; }
        Debug.Log($"[SaveTransfer] export: {f.itemCount} items, {text.Length} chars, schema {f.saveSchema}, gen {f.releaseGeneration}{(f.testData ? " (TEST DATA)" : "")}");
        return back;
    }

    public static string SuggestedFileName() => "OneMoreMile_save_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".json";

    // ===================================================================== 検査
    public static Checked Check(string text)
    {
        var r = new Checked { text = text };
        string Fail(string user, string detail) { r.ok = false; r.error = user; r.detail = detail; return null; }
        const string Broken = "ファイルが壊れているか、内容が書き換えられています。読み込めません(今のデータは変わっていません)。";
        const string Newer = "新しいバージョンのアプリで作られたファイルです。アプリを更新してから読み込んでください(今のデータは変わっていません)。";
        if (string.IsNullOrEmpty(text)) { Fail("ファイルが空です(今のデータは変わっていません)。", "empty"); return r; }
        if (text.Length > MaxFileBytes) { Fail("ファイルが大きすぎます。引き継ぎのファイルではありません(今のデータは変わっていません)。", "too large " + text.Length); return r; }
        if (text.TrimStart().Length == 0 || text.TrimStart()[0] != '{') { Fail("このゲームの引き継ぎファイルではありません(今のデータは変わっていません)。", "not a json object"); return r; }
        FileData f;
        try { f = JsonUtility.FromJson<FileData>(text); }
        catch (Exception e) { Fail(Broken, "json: " + e.GetType().Name); return r; }
        if (f == null || f.format != Format) { Fail("このゲームの引き継ぎファイルではありません(今のデータは変わっていません)。", "format '" + (f?.format ?? "null") + "'"); return r; }
        if (f.formatVersion > FormatVersion) { Fail(Newer, $"formatVersion {f.formatVersion} > {FormatVersion}"); return r; }
        if (f.formatVersion < 1) { Fail(Broken, "formatVersion " + f.formatVersion); return r; }
        if (f.saveSchema > SaveSystem.CurrentSchemaVersion) { Fail(Newer, $"saveSchema {f.saveSchema} > {SaveSystem.CurrentSchemaVersion}"); return r; }
        if (f.saveSchema < 1) { Fail(Broken, "saveSchema " + f.saveSchema); return r; }
        int gen = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0);
        if (f.releaseGeneration != gen) { Fail("開発版と製品版の間では引き継げません(今のデータは変わっていません)。", $"generation {f.releaseGeneration} != {gen}"); return r; }
        if (f.items == null || f.summary == null || f.itemCount != f.items.Count) { Fail(Broken, $"items {f.items?.Count ?? -1} / count {f.itemCount}"); return r; }
        if (!FixedTimeEquals(f.hash ?? "", Sign(f))) { Fail(Broken, "hash mismatch"); return r; }

        // 項目: 引き継ぎの対象のキーだけ、1回ずつ、登録の型どおり、値が範囲内
        var allowed = new Dictionary<string, SaveType>();
        foreach (var k in TransferKeys()) allowed[k.key] = k.type;
        var seen = new HashSet<string>();
        foreach (var it in f.items)
        {
            if (it == null || string.IsNullOrEmpty(it.k)) { Fail(Broken, "empty item"); return r; }
            if (!allowed.TryGetValue(it.k, out SaveType t)) { Fail(Newer, "unknown key " + it.k); return r; }
            if (!seen.Add(it.k)) { Fail(Broken, "duplicate " + it.k); return r; }
            if (it.t != (int)t) { Fail(Broken, $"type of {it.k}: {it.t} != {(int)t}"); return r; }
            if (it.v == null || it.v.Length > MaxValueChars) { Fail(Broken, "value size " + it.k); return r; }
            string bad = CheckValue(it.k, t, it.v);
            if (bad != null) { Fail("ファイルの中に正しくない値があります。読み込めません(今のデータは変わっていません)。", $"{it.k}: {bad}"); return r; }
        }
        // 欠落: 正常なセーブなら必ずある物
        foreach (var must in new[] { GameManager.TotalMileKey, SaveKeys.LifetimeDistance, UnlockRules.InitKey }) // 新規のセーブでも起動時に必ず書かれる物
            if (!seen.Contains(must)) { Fail("ファイルに必要なデータが足りません。読み込めません(今のデータは変わっていません)。", "missing " + must); return r; }
        // 要約(確認の画面に出す物)が中身と一致する
        var sum = Summarize(ToDict(f.items));
        if (JsonUtility.ToJson(sum) != JsonUtility.ToJson(f.summary)) { Fail(Broken, "summary mismatch"); return r; }
        r.ok = true; r.data = f;
        return r;
    }

    static readonly NumberStyles Num = NumberStyles.Float;
    static string CheckValue(string key, SaveType t, string v)
    {
        var inv = CultureInfo.InvariantCulture;
        if (t == SaveType.Int)
        {
            if (!int.TryParse(v, NumberStyles.Integer, inv, out int i)) return "not an int";
            if (key == GameManager.TotalMileKey) return i < 0 || i > 2000000000 ? "MILE out of range" : null;
            return i < 0 ? "negative" : null;
        }
        if (t == SaveType.Float)
        {
            if (!float.TryParse(v, Num, inv, out float fl) || float.IsNaN(fl) || float.IsInfinity(fl)) return "not a number";
            return fl < 0f || fl > 1e9f ? "out of range" : null;
        }
        // 文字列: 距離(double 文字列)
        if (key.StartsWith(SaveKeys.StageBestPrefix) || key.StartsWith(UnlockRules.ReachPrefix) || key == SaveKeys.LifetimeDistance)
        {
            if (!double.TryParse(v, Num, inv, out double d) || double.IsNaN(d) || double.IsInfinity(d)) return "not a distance";
            return d < 0 || d > 1e12 ? "distance out of range" : null;
        }
        switch (key)
        {
            case "OwnedCardsV1":
            {
                CardInventory.SaveWrapper w;
                try { w = JsonUtility.FromJson<CardInventory.SaveWrapper>(v); } catch { return "cards json"; }
                if (w == null || w.stacks == null) return "cards empty";
                foreach (var s in w.stacks)
                {
                    if (s == null || string.IsNullOrEmpty(s.cardId)) return "card without id";
                    if (s.level < 1 || s.level > CardInventory.MaxCardLevel) return "card level";
                    if (s.count < 0 || s.count > 1000000) return "card count";
                    if (!CardKnown(s.cardId)) return "unknown card";
                }
                return null;
            }
            case "CardMasteryV1":
            {
                CardMastery.SaveWrapper w;
                try { w = JsonUtility.FromJson<CardMastery.SaveWrapper>(v); } catch { return "mastery json"; }
                if (w == null || w.cards == null) return "mastery empty";
                foreach (var c in w.cards) if (c == null || string.IsNullOrEmpty(c.cardId) || c.level < 0 || c.level > 5) return "mastery entry";
                return null;
            }
            case "DeckCardIds":
            {
                var ids = SplitList(v);
                if (ids.Count > GameManager.DeckCapacity) return "deck too large";
                foreach (var id in ids) if (!CardKnown(id)) return "unknown deck card";
                return null;
            }
            case UnlockRules.StagesKey:
                foreach (var id in SplitList(v)) if (id != UnlockRules.Arena && !StageKnown(id)) return "unknown stage";
                return null;
            case UnlockRules.CharsKey:
                foreach (var id in SplitList(v)) if (CharacterDatabase.FindById(id) == null) return "unknown character";
                return null;
            case "SelectedCharacterId":
                return v.Length == 0 || CharacterDatabase.FindById(v) != null ? null : "unknown character";
            case "SelectedStageId":
                return v.Length == 0 || StageKnown(v) ? null : "unknown stage";
        }
        if (key.StartsWith(GameManager.CharacterCardSlotsPrefix))
        {
            foreach (var part in SplitList(v))
            {
                int c = part.LastIndexOf(':');
                string id = c > 0 ? part.Substring(0, c) : part;
                if (!CardKnown(id)) return "unknown slot card";
                if (c > 0 && (!int.TryParse(part.Substring(c + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lv) || lv < 1 || lv > CardInventory.MaxCardLevel)) return "slot level";
            }
            return null;
        }
        // JSON の物は読めること(中身の細かい検査は各クラスの読み込みに任せる。読めない JSON は拒否)
        if (v.Length > 0 && (v[0] == '{' || v[0] == '['))
        {
            if (!BalancedJson(v)) return "json";
        }
        return null;
    }

    static bool CardKnown(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (CardDatabase.FindBaseById(id) != null) return true;
        if (CardVariant.IsVariantKey(id)) return CardVariant.Parse(id) != null;
        try { return CardDatabase.FindById(id) != null; } catch { return false; }
    }

    static bool StageKnown(string id)
    {
        foreach (var s in StageIds()) if (s == id) return true;
        return false;
    }

    static List<string> SplitList(string v)
    {
        var l = new List<string>();
        foreach (var p in (v ?? "").Split(',')) { string t = p.Trim(); if (t.Length > 0) l.Add(t); }
        return l;
    }

    // 括弧と文字列の閉じ方だけの簡単な検査(途中で切れた JSON を拒否する)
    static bool BalancedJson(string s)
    {
        int depth = 0; bool str = false, esc = false;
        foreach (char ch in s)
        {
            if (str) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') str = false; continue; }
            if (ch == '"') str = true;
            else if (ch == '{' || ch == '[') depth++;
            else if (ch == '}' || ch == ']') { depth--; if (depth < 0) return false; }
        }
        return depth == 0 && !str;
    }

    // ===================================================================== 要約(確認の画面)
    static Dictionary<string, string> ToDict(List<SaveSystem.Item> items)
    {
        var d = new Dictionary<string, string>();
        foreach (var it in items) if (it != null && !string.IsNullOrEmpty(it.k)) d[it.k] = it.v ?? "";
        return d;
    }

    public static Summary Summarize(Dictionary<string, string> d)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = new Summary();
        if (d.TryGetValue(GameManager.TotalMileKey, out string m)) int.TryParse(m, NumberStyles.Integer, inv, out s.mile);
        if (d.TryGetValue("OwnedCardsV1", out string cj))
        {
            try
            {
                var w = JsonUtility.FromJson<CardInventory.SaveWrapper>(cj);
                if (w != null && w.stacks != null) foreach (var st in w.stacks) if (st != null && st.count > 0) { s.cardKinds++; s.cardTotal += st.count; }
            }
            catch { }
        }
        if (d.TryGetValue("DeckCardIds", out string deck)) s.deckCount = SplitList(deck).Count;
        if (d.TryGetValue(UnlockRules.StagesKey, out string st2)) { var l = SplitList(st2); l.Sort(StringComparer.Ordinal); s.stages = string.Join(",", l); }
        if (d.TryGetValue(UnlockRules.CharsKey, out string ch)) { var l = SplitList(ch); l.Sort(StringComparer.Ordinal); s.chars = string.Join(",", l); }
        double best = 0;
        var maps = new List<string>();
        foreach (var id in StageIds())
        {
            if (!d.TryGetValue(SaveKeys.StageBestPrefix + id, out string bv)) continue;
            if (!double.TryParse(bv, Num, inv, out double b) || double.IsNaN(b) || double.IsInfinity(b)) continue;
            maps.Add(id + "=" + Math.Floor(b).ToString("F0", inv));
            best = Math.Max(best, b);
        }
        if (d.TryGetValue(GameManager.BestDistanceKey, out string gb) && float.TryParse(gb, Num, inv, out float g) && !float.IsNaN(g) && !float.IsInfinity(g)) best = Math.Max(best, g);
        s.bestDistance = Math.Floor(best).ToString("F0", inv);
        s.mapBests = string.Join(";", maps);
        if (d.TryGetValue(SaveKeys.LifetimeDistance, out string lt) && double.TryParse(lt, Num, inv, out double lv) && !double.IsNaN(lv) && !double.IsInfinity(lv)) s.lifetime = Math.Floor(lv).ToString("F0", inv);
        if (d.TryGetValue(SaveKeys.FinalDungeonUnlocked, out string fd)) int.TryParse(fd, NumberStyles.Integer, inv, out s.finalDungeon);
        return s;
    }

    // 今の端末の進行の要約(確認の画面で「今」と並べる)
    public static Summary CurrentSummary()
    {
        var d = new Dictionary<string, string>();
        foreach (var k in TransferKeys()) if (SaveStore.HasKey(k.key)) d[k.key] = SaveSystem.ReadItem(k.key, k.type).v;
        return Summarize(d);
    }

    // ===================================================================== 検査値
    // 鍵はアプリに入っている(=アプリを解析すれば作り直せる)。壊れた/手で書き換えたファイルの検出用で、不正対策の本体ではない
    static readonly byte[] SignKey = Encoding.UTF8.GetBytes("OMM-transfer-v1:7c1e3f0a-55b2-4d6e-9a1f-2b8c3d4e5f60");

    static string Sign(FileData f)
    {
        var sb = new StringBuilder();
        void A(string s) { s = s ?? ""; sb.Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s).Append('|'); }
        A(f.format); A(f.formatVersion.ToString(CultureInfo.InvariantCulture)); A(f.app); A(f.appVersion); A(f.platform);
        A(f.savedAtUtc); A(f.savedAtLocal); A(f.saveSchema.ToString(CultureInfo.InvariantCulture)); A(f.releaseGeneration.ToString(CultureInfo.InvariantCulture));
        A(f.testData ? "1" : "0"); A(f.summary != null ? JsonUtility.ToJson(f.summary) : ""); A(f.itemCount.ToString(CultureInfo.InvariantCulture));
        if (f.items != null) foreach (var it in f.items) { A(it?.k); A(it != null ? it.t.ToString(CultureInfo.InvariantCulture) : ""); A(it?.v); }
        using (var h = new System.Security.Cryptography.HMACSHA256(SignKey))
        {
            byte[] mac = h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hex = new StringBuilder(mac.Length * 2);
            foreach (byte b in mac) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }
    }

    static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // ===================================================================== 読み込み(置き換え)
    public static bool CanRunNow(out string why)
    {
        why = "";
        var gm = GameManager.Instance;
        bool home = gm != null && !gm.HasStarted && !ArenaMode.Active && !ArenaMode.PendingStart && !TutorialMode.Active && !TutorialMode.PendingStart
            && !NetSession.IsActive && !DebugRun.IsActive && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning);
        if (!home) { why = "データの引き継ぎはホーム画面で行えます(ラン中・マルチ接続中はできません)。"; return false; }
        return true;
    }

    public static string PendingPath => System.IO.Path.Combine(SaveSystem.BackupDirectory, "import_pending.json");
    public static string LastImportNote { get; private set; } = "";

    // 確認の後に呼ぶ。成功で true(呼び出し側はシーンを読み直す)
    public static bool Apply(Checked c, out string error)
    {
        error = "";
        if (c == null || !c.ok || c.data == null) { error = "読み込めるファイルではありません。"; return false; }
        if (!CanRunNow(out error)) return false;
        var again = Check(c.text); // 確認の画面を出している間に世代などが変わっていないか
        if (!again.ok) { error = again.error; return false; }
        ProgressStats.Flush(true);
        SaveStore.Save();
        // ① 直前の状態を控える(全キー)
        if (!SaveSystem.WriteNamedBackup("pre_import_", 5)) { error = "読み込みの前の控えを書けませんでした。端末の空き容量を確認してください(今のデータは変わっていません)。"; return false; }
        DevCrash("import-before-pending");
        // ② 適用する内容を書き切る(この後どこで落ちても、次の起動で同じ内容を最後まで適用する)
        try { SaveSystem.WriteFileAtomic(PendingPath, again.text); }
        catch (Exception e) { error = "読み込みの準備ができませんでした。端末の空き容量を確認してください(今のデータは変わっていません)。"; Debug.LogWarning("[SaveTransfer] pending write failed: " + e.Message); return false; }
        DevCrash("import-before-apply");
        // ③ 置き換え
        ApplyItems(again.data);
        DevCrash("import-after-save");
        try { System.IO.File.Delete(PendingPath); } catch (Exception e) { Debug.LogWarning("[SaveTransfer] could not delete the pending file: " + e.Message); }
        ForgetDebugRunRestore();
        // ④ 起動時と同じ処理(形式の移行/検査/正常な控えの更新/static の読み直し)
        SaveSystem.Boot(SaveSystem.BuildReleaseGeneration);
        LastImportNote = $"imported {again.data.itemCount} items saved at {again.data.savedAtUtc} (app {again.data.appVersion}, {again.data.platform})";
        Debug.Log("[SaveTransfer] " + LastImportNote);
        return true;
    }

    // 引き継ぎの対象のキーをファイルの内容にする(ファイルに無いキーは消す)。先に書いてから消す(進行のキーが一時的に全部無い状態を作らない)
    static void ApplyItems(FileData f)
    {
        var want = new Dictionary<string, SaveSystem.Item>();
        foreach (var it in f.items) want[it.k] = it;
        SaveStore.BeginBatch();
        try
        {
            int n = 0;
            foreach (var it in f.items)
            {
                SaveSystem.ApplyItem(it);
                if (++n == f.items.Count / 2) DevCrash("import-mid");
            }
            foreach (var k in TransferKeys()) if (!want.ContainsKey(k.key) && SaveStore.HasKey(k.key)) SaveStore.DeleteKey(k.key);
            foreach (var x in NotTransferred) if (x.clear) SaveStore.DeleteKey(x.key);
            SaveStore.SetInt(SaveKeys.SchemaVersion, f.saveSchema); // 古い形式のファイルなら、この後の起動時処理で最新へ移行する
            SaveStore.Save();
        }
        finally { SaveStore.EndBatch(); }
    }

    // 起動の最初(SaveSystem.Boot から): 適用の途中で終わっていたら、同じ内容を最後まで適用する
    public static void RecoverIfPending(StringBuilder log)
    {
        string p;
        try { p = PendingPath; if (!System.IO.File.Exists(p)) return; } catch { return; }
        string text = null;
        try { text = System.IO.File.ReadAllText(p, Encoding.UTF8); } catch (Exception e) { Debug.LogWarning("[SaveTransfer] pending import unreadable: " + e.Message); }
        var c = text != null ? Check(text) : null;
        if (c != null && c.ok)
        {
            ApplyItems(c.data);
            ForgetDebugRunRestore();
            try { System.IO.File.Delete(p); } catch { }
            log?.Append($"finished an interrupted import ({c.data.itemCount} items); ");
            Debug.LogWarning($"[SaveTransfer] the last import did not finish - applied it again from the start ({c.data.itemCount} items)");
            NoticeQueue.Toast(Loc.T("前回のデータの引き継ぎを最後まで行いました"));
            return;
        }
        // 適用の前に書き切る物なので、ここへは普通は来ない(書いた控えが読めない)。適用は始まっていないので、今のデータのまま
        try { System.IO.File.Move(p, p + ".unreadable_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); } catch { }
        log?.Append("pending import file was unreadable - not applied; ");
        Debug.LogWarning("[SaveTransfer] a pending import file could not be checked (" + (c?.detail ?? "unreadable") + ") - not applied, kept for investigation");
    }

    // 開発版のデバッグランの控え(落ちた時に進行を戻す物)が残っていると、引き継いだ進行を古い進行へ戻してしまう → 別名へ
    static void ForgetDebugRunRestore()
    {
        try
        {
            string f = System.IO.Path.Combine(Application.persistentDataPath, SaveProfile.DebugRunFile(SaveProfile.IsTest));
            if (System.IO.File.Exists(f)) System.IO.File.Move(f, f + ".before_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        }
        catch { }
    }

    // ===================================================================== 確認用(開発版)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ファイルの中身を変えて、検査値を付け直す(検査値の後の検査 = 値の範囲/未知のキー/古い形式 を確かめる用)
    public static string DevResign(string text, Action<FileData> mutate, bool resummarize = true)
    {
        var f = JsonUtility.FromJson<FileData>(text);
        mutate(f);
        f.itemCount = f.items.Count;
        if (resummarize) f.summary = Summarize(ToDict(f.items));
        f.hash = Sign(f);
        return JsonUtility.ToJson(f, true);
    }
#endif
    public static string DevCrashAt = "";
    static void DevCrash(string at)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DevCrashAt != at) return;
        Debug.Log($"[SaveTransfer] DEV CRASH at '{at}'");
        if (at == "import-mid") Platform.Save.Save(); // 書いた端から残る保存の機種を再現(半分だけ書いた状態で落ちる)
#if UNITY_ANDROID && !UNITY_EDITOR
        UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.Abort);
#elif !UNITY_EDITOR
        System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
#endif
    }
}

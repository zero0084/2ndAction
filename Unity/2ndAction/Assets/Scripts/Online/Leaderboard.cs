using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

// オンラインランキング(2026-10-08): ソロのマップ別「最高到達距離」。
//  ・対象: 正規の帰還(脱出/ラスダンの終わり)で確定した距離だけ。各プレイヤー × 各マップで自己ベスト1件。距離の降順、同じ距離は同じ順位。
//  ・対象外: マルチ / 闘技場 / 練習 / Debug Run / テストデータ / デバッグ機能(ワープ・無敵)を使ったラン(途中で OFF に戻しても)/
//           この仕組みより前の記録・中断データ(成功やデバッグ未使用を確かめられない)/ 開発版の「全部選べる」で選んだ未解放のマップ。
//  ・オートと疾走出発は使ってよい(使ったかを記録に残し、一覧に印を出す)。
//  ・参加(公開への同意)は任意。参加しなくても通常プレイとローカルの記録はそのまま。参加した後の対象記録は確定の時に投稿する。
//    参加より前に確定した「資格のある自己ベスト」は、参加した時に投稿する(この版から記録した物だけ)。
//  ・投稿は「投稿待ち」に保存してから送る(オフライン/失敗は残して、後で再送)。同じラン(runId)は二度送らない。サーバーでも拒否する。
//  ・端末からの値をそのまま信じない: サーバー(Cloud Code)が形式・範囲・速さ・時刻・疾走の行き先・重複・自己ベストを検査して書く
//    (プレイヤーはランキングへ直接書けないよう Access Control で禁止する)。検査の規則は LeaderboardRules(このファイル)と
//    Tools/ugs/cloudcode/omm_submit_run.js で同じにしてある。
public static class Leaderboard
{
    public const string JoinedKey = "Ranking.JoinedV1";      // 1 = 参加した(コードネームと記録の公開に同意)
    public const string PendingKey = "Ranking.PendingV1";    // 投稿待ち(JSON)
    public const string VerifiedKey = "Ranking.VerifiedV1";  // マップ別の「投稿資格のある自己ベスト」(JSON)。投稿の有無に関係なく残す
    public const string SentKey = "Ranking.SentV1";          // 送り終えた runId(最新 50)

    public static readonly string[] Stages = { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor" };
    public static string BoardId(string stageId) => "omm_best_" + stageId;

    [Serializable]
    public class Entry
    {
        public string runId = "", stageId = "", characterId = "";
        public int meters;
        public float playSeconds, sprintFrom;
        public bool usedAuto, usedSprint;
        public long startedUtc, committedUtc;
        public string appVersion = "";
    }
    [Serializable] class EntryList { public List<Entry> items = new List<Entry>(); }

    public static bool Joined => SaveStore.GetInt(JoinedKey, 0) != 0;

    // ---------------------------------------------------------------- 資格
    public static bool IsEligible(RunLedger.Run r, out string why)
    {
        why = "";
        if (r == null) { why = "no run"; return false; }
        if (Array.IndexOf(Stages, r.stageId) < 0) { why = "not a ranking map (" + r.stageId + ")"; return false; }
        if (DebugRun.WritesBlocked) { why = "arena/practice/debug run"; return false; }
        if (r.debugUsed) { why = "debug feature used (" + r.debugWhat + ")"; return false; }
        if (r.legacy) { why = "resumed from an older save (cannot verify)"; return false; }
        if (r.multiplayer) { why = "multiplayer"; return false; }
        if (r.testProfile) { why = "test data"; return false; }
        if (!r.officialStage) { why = "map not officially unlocked (dev selection)"; return false; }
        if (r.maxReached < 1.0) { why = "no distance"; return false; }
        return true;
    }

    public static Entry MakeEntry(RunLedger.Run r) => new Entry
    {
        runId = r.runId, stageId = r.stageId, characterId = r.characterId,
        meters = (int)Math.Floor(r.maxReached),
        playSeconds = r.playSeconds, sprintFrom = r.sprintFrom, usedAuto = r.usedAuto, usedSprint = r.usedSprint,
        startedUtc = r.startedUtc, committedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        appVersion = Application.version,
    };

    // RunLedger.CommitSuccess から(資格のある成功だけ)
    public static void OnSuccessCommitted(RunLedger.Run r)
    {
        var e = MakeEntry(r);
        var verified = Load(VerifiedKey);
        var old = verified.items.Find(x => x.stageId == e.stageId);
        bool better = old == null || e.meters > old.meters;
        if (better) { verified.items.RemoveAll(x => x.stageId == e.stageId); verified.items.Add(e); Store(VerifiedKey, verified); }
        Log($"verified run {e.stageId} {e.meters}m{(better ? " (new verified best)" : "")}");
        if (Joined && better) Enqueue(e);
    }

    public static Entry VerifiedBest(string stageId) => Load(VerifiedKey).items.Find(x => x.stageId == stageId);

    // ---------------------------------------------------------------- 参加
    public static void Join()
    {
        if (Joined) return;
        SaveStore.SetInt(JoinedKey, 1);
        foreach (var e in Load(VerifiedKey).items) Enqueue(e); // 参加より前に確定した、資格のある自己ベスト
        SaveStore.Save();
        Log("joined the ranking");
        _ = FlushAsync("join");
    }
    public static void Leave() { SaveStore.SetInt(JoinedKey, 0); SaveStore.Save(); Log("left the ranking (nothing more is sent)"); }

    // ---------------------------------------------------------------- 投稿待ち
    public static int PendingCount => Load(PendingKey).items.Count;
    static void Enqueue(Entry e)
    {
        if (IsSent(e.runId)) return;
        var q = Load(PendingKey);
        if (q.items.Exists(x => x.runId == e.runId)) return;
        // 同じマップの、より短い投稿待ちは要らない(サーバーは自己ベストより短い物を書かない)
        q.items.RemoveAll(x => x.stageId == e.stageId && x.meters <= e.meters);
        if (q.items.Exists(x => x.stageId == e.stageId && x.meters > e.meters)) return;
        q.items.Add(e);
        q.items.Sort((a, b) => a.committedUtc.CompareTo(b.committedUtc)); // 古い順(疾走の行き先の検査がサーバーの自己ベストを使うため)
        Store(PendingKey, q);
        SaveStore.Save();
        Log($"queued {e.stageId} {e.meters}m ({q.items.Count} pending)");
    }

    static bool IsSent(string runId) => Array.IndexOf(SaveStore.GetString(SentKey, "").Split(','), runId) >= 0;
    static void MarkSent(string runId)
    {
        var l = new List<string>(SaveStore.GetString(SentKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        if (!l.Contains(runId)) l.Add(runId);
        while (l.Count > 50) l.RemoveAt(0);
        SaveStore.SetString(SentKey, string.Join(",", l));
    }

    public static bool Sending { get; private set; }
    public static string LastSendError { get; private set; } = "";
    static float nextAutoFlush;

    // ホームで定期的に(30秒ごと)/参加した時/確定した時に送る。失敗したら残す
    public static void Tick()
    {
        if (!Joined || Sending || Time.realtimeSinceStartup < nextAutoFlush) return;
        nextAutoFlush = Time.realtimeSinceStartup + 30f;
        if (PendingCount > 0) _ = FlushAsync("retry");
    }

    public static async Task FlushAsync(string why)
    {
        if (!Joined || Sending) return;
        var backend = Backend;
        if (backend == null) return;
        Sending = true;
        try
        {
            while (true)
            {
                var q = Load(PendingKey);
                if (q.items.Count == 0) break;
                var e = q.items[0];
                var res = await backend.Submit(e, Codename.Current);
                if (res.status == SubmitStatus.Failed) { LastSendError = res.message; Log($"send failed ({why}): {res.message} - kept for retry"); break; }
                // 受け付けた/自己ベストより短い/重複/拒否 は、どれも送り直さない
                q = Load(PendingKey);
                q.items.RemoveAll(x => x.runId == e.runId);
                Store(PendingKey, q);
                MarkSent(e.runId);
                SaveStore.Save();
                LastSendError = "";
                Log($"sent {e.stageId} {e.meters}m -> {res.status}{(string.IsNullOrEmpty(res.message) ? "" : " (" + res.message + ")")}");
            }
        }
        catch (Exception ex) { LastSendError = ex.GetType().Name; Debug.LogWarning("[Ranking] send error: " + ex.GetType().Name + ": " + ex.Message); }
        finally { Sending = false; }
    }

    // コードネームを変えた時: 掲載中の自分の記録の表示名を新しい名前へ(ID は変わらない)
    public static async Task<bool> OnCodenameChanged()
    {
        if (!Joined || Backend == null) return false;
        try { return await Backend.UpdateName(Codename.Current); }
        catch (Exception ex) { Debug.LogWarning("[Ranking] rename error: " + ex.Message); return false; }
    }

    // ---------------------------------------------------------------- 読む(画面)
    public enum SubmitStatus { Accepted, NotBetter, Duplicate, Rejected, Failed }
    public struct SubmitResult { public SubmitStatus status; public string message; public SubmitResult(SubmitStatus s, string m = "") { status = s; message = m; } }

    [Serializable]
    public class Row
    {
        public int rank;           // 同じ距離は同じ順位(1, 2, 2, 4 …)
        public string playerId = "", name = "", characterId = "";
        public int meters;
        public bool usedAuto, usedSprint, isMe;
    }
    public class Page { public List<Row> top = new List<Row>(); public Row me; public int total; }

    public interface IBackend
    {
        string Name { get; }
        Task<SubmitResult> Submit(Entry e, string codename);
        Task<Page> Load(string stageId, int limit);
        Task<bool> UpdateName(string codename);
    }

    static IBackend backend;
    public static IBackend Backend
    {
        get
        {
            if (backend != null) return backend;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string dir = Arg("-lbMock");
            if (!string.IsNullOrEmpty(dir)) { backend = new MockBackend(dir, Arg("-lbMockPlayer")); return backend; }
#endif
            backend = new UgsLeaderboardBackend();
            return backend;
        }
        set { backend = value; }
    }

    // 同じ距離は同じ順位(競技の順位: 1, 2, 2, 4)
    public static void AssignRanks(List<Row> rows, int firstRank = 1)
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i].rank = i > 0 && rows[i].meters == rows[i - 1].meters ? rows[i - 1].rank : firstRank + i;
    }

    // ---------------------------------------------------------------- 保存の補助
    static EntryList Load(string key)
    {
        string json = SaveStore.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new EntryList();
        try { return JsonUtility.FromJson<EntryList>(json) ?? new EntryList(); } catch { return new EntryList(); }
    }
    static void Store(string key, EntryList l) => SaveStore.SetString(key, JsonUtility.ToJson(l));
    static void Log(string m) => Debug.Log("[Ranking] " + m);
    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用: 端末の外に置いた共有フォルダをサーバーの代わりにする(複数の起動で「別のプレイヤー」を試せる)。
    // 検査はサーバー(Cloud Code)と同じ LeaderboardRules。実際のサービスでの確認ではない。
    public static void DevReset() { foreach (var k in new[] { JoinedKey, PendingKey, VerifiedKey, SentKey }) SaveStore.DeleteKey(k); SaveStore.Save(); backend = null; }
#endif
}

// サーバー(Cloud Code の omm_submit_run.js)と同じ検査。模擬サーバーと、端末側の事前確認に使う
public static class LeaderboardRules
{
    public const int MaxMeters = 2000000;          // これより長い記録は受け付けない
    public const float MaxSpeedMps = 160f / 3.6f;   // 自然の上限 100km/h + 補助(130)に余裕を見た値
    public const float SpeedSlack = 1.2f, DistanceSlack = 300f;
    public static readonly string[] Characters = { "swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin" };

    // serverBest: そのプレイヤーの、そのマップの掲載中の記録(無ければ 0)。recentRunIds: サーバーが覚えている送信済みの runId
    public static string Validate(Leaderboard.Entry e, int serverBest, ICollection<string> recentRunIds, long nowUtc)
    {
        if (e == null) return "empty";
        if (string.IsNullOrEmpty(e.runId) || e.runId.Length != 32 || !IsHex(e.runId)) return "bad run id";
        if (Array.IndexOf(Leaderboard.Stages, e.stageId) < 0) return "bad map";
        if (Array.IndexOf(Characters, e.characterId) < 0) return "bad character";
        if (e.meters < 1 || e.meters > MaxMeters) return "distance out of range";
        if (e.playSeconds <= 0f || e.playSeconds > 48f * 3600f) return "bad play time";
        if (e.sprintFrom < 0f || e.sprintFrom > e.meters) return "bad sprint";
        if (e.usedSprint != (e.sprintFrom > 0f)) return "sprint flag mismatch";
        if (e.sprintFrom > 0f && e.sprintFrom > serverBest) return "sprint beyond the confirmed best";
        float ran = e.meters - e.sprintFrom;
        if (ran > MaxSpeedMps * SpeedSlack * e.playSeconds + DistanceSlack) return "too fast";
        if (e.committedUtc > nowUtc + 600) return "time in the future";
        if (e.startedUtc <= 0 || e.startedUtc > e.committedUtc) return "bad start time";
        if (e.committedUtc - e.startedUtc + 120 < e.playSeconds) return "play time longer than the wall clock";
        if (recentRunIds != null && recentRunIds.Contains(e.runId)) return "duplicate";
        return "";
    }

    static bool IsHex(string s) { foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return true; }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 開発用の模擬サーバー(-lbMock <フォルダ> [-lbMockPlayer <ID>])。フォルダ内の JSON を全員で共有する
public class MockBackend : Leaderboard.IBackend
{
    [Serializable] class Rec { public string playerId = "", name = "", characterId = "", stageId = "", runId = ""; public int meters; public bool usedAuto, usedSprint; public long updatedUtc; }
    [Serializable] class Db { public List<Rec> recs = new List<Rec>(); public List<string> runIds = new List<string>(); }
    readonly string dir, player;
    public string Name => "mock(" + dir + ")";
    public static bool SimulateOffline; // テスト: 通信失敗

    public MockBackend(string dir, string playerId)
    {
        this.dir = dir; System.IO.Directory.CreateDirectory(dir);
        player = string.IsNullOrEmpty(playerId) ? "mock-" + SystemInfo.deviceUniqueIdentifier.GetHashCode().ToString("x8") : playerId;
    }
    string Path => System.IO.Path.Combine(dir, "leaderboard.json");
    Db Read() { try { return System.IO.File.Exists(Path) ? JsonUtility.FromJson<Db>(System.IO.File.ReadAllText(Path)) ?? new Db() : new Db(); } catch { return new Db(); } }
    void Write(Db d) => System.IO.File.WriteAllText(Path, JsonUtility.ToJson(d, true));

    public async Task<Leaderboard.SubmitResult> Submit(Leaderboard.Entry e, string codename)
    {
        await Task.Yield();
        if (SimulateOffline) return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, "offline (simulated)");
        var d = Read();
        var mine = d.recs.Find(x => x.playerId == player && x.stageId == e.stageId);
        string err = LeaderboardRules.Validate(e, mine != null ? mine.meters : 0, d.runIds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (err == "duplicate") return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Duplicate, err);
        if (err != "") return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Rejected, err);
        d.runIds.Add(e.runId); if (d.runIds.Count > 500) d.runIds.RemoveAt(0);
        if (mine != null && e.meters <= mine.meters) { Write(d); return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.NotBetter, $"best {mine.meters}m"); }
        if (mine == null) { mine = new Rec { playerId = player, stageId = e.stageId }; d.recs.Add(mine); }
        mine.meters = e.meters; mine.characterId = e.characterId; mine.usedAuto = e.usedAuto; mine.usedSprint = e.usedSprint;
        mine.name = Codename.Sanitize(codename); mine.runId = e.runId; mine.updatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Write(d);
        return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Accepted);
    }

    public async Task<Leaderboard.Page> Load(string stageId, int limit)
    {
        await Task.Yield();
        if (SimulateOffline) throw new Exception("offline (simulated)");
        var all = Read().recs.FindAll(x => x.stageId == stageId);
        all.Sort((a, b) => b.meters != a.meters ? b.meters.CompareTo(a.meters) : a.updatedUtc.CompareTo(b.updatedUtc));
        var rows = all.ConvertAll(x => new Leaderboard.Row { playerId = x.playerId, name = x.name, characterId = x.characterId, meters = x.meters, usedAuto = x.usedAuto, usedSprint = x.usedSprint, isMe = x.playerId == player });
        Leaderboard.AssignRanks(rows);
        var page = new Leaderboard.Page { total = rows.Count, me = rows.Find(x => x.isMe) };
        page.top = rows.GetRange(0, Math.Min(limit, rows.Count));
        return page;
    }

    public async Task<bool> UpdateName(string codename)
    {
        await Task.Yield();
        if (SimulateOffline) return false;
        var d = Read();
        foreach (var x in d.recs) if (x.playerId == player) x.name = Codename.Sanitize(codename);
        Write(d);
        return true;
    }
}
#endif

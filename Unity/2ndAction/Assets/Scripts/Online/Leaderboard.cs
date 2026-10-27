using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

// オンラインランキング(2026-10-08): ソロのマップ別「最高到達距離」。
//  ・対象: 正規の帰還(脱出/ラスダンの終わり)で確定した距離だけ。各プレイヤー × 各マップで自己ベスト1件。距離の降順、同じ距離は同じ順位。
//  ・2026-10-14(マスター指定): ランキングはソロ(CPU サポートなし)だけ。一度でも CPU サポートを使ったランは、途中で仲間が倒れても投稿しない
//    (MILE・解放・ローカルの記録は今までどおり)。サポート用の表/スクリプトは作らない。前の版の端末に残ったサポートの送信待ち/自己ベストは
//    起動時に取り除く(ソロへ振り替えない。PurgeSupportEntries)。
//  ・対象外: マルチ / 闘技場 / 練習 / Debug Run / テストデータ(production だけ。2026-10-27)/ デバッグ機能(ワープ・無敵)を使ったラン(途中で OFF に戻しても)/
//           この仕組みより前の記録・中断データ(成功やデバッグ未使用を確かめられない)/ 開発版の「全部選べる」で選んだ未解放のマップ。
//  ・オートと疾走出発は使ってよい(使ったかを記録に残し、一覧に印を出す)。
//  ・参加(公開への同意)は任意。参加しなくても通常プレイとローカルの記録はそのまま。参加した後の対象記録は確定の時に投稿する。
//    参加より前に確定した「資格のある自己ベスト」は、参加した時に投稿する(この版から記録した物だけ)。
//  ・投稿は「投稿待ち」に保存してから送る(オフライン/失敗は残して、後で再送)。同じラン(runId)は二度送らない。サーバーでも拒否する。
//  ・端末からの値をそのまま信じない: サーバー(Cloud Code)が形式・範囲・速さ・時刻・疾走の行き先・重複・自己ベストを検査して書く
//    (プレイヤーはランキングへ直接書けないよう Access Control で禁止する)。検査の規則は LeaderboardRules(このファイル)と
//    Tools/ugs/cloudcode/omm_submit_run.js で同じにしてある。
//  ・2026-10-14(マスター指定): 送信待ち/自己ベストには作った時の UGS の環境(env)を付け、今の環境で作った物だけを送る。
//    別の環境の物・環境を付ける前の物(env が空)は送らずに残す(production へ振り替えない)。開発版は production へ送らない(UgsEnvironment.MaySubmit)。
public static class Leaderboard
{
    public const string JoinedKey = "Ranking.JoinedV1";      // 1 = 参加した(コードネームと記録の公開に同意)
    public const string PendingKey = "Ranking.PendingV1";    // 投稿待ち(JSON)
    public const string VerifiedKey = "Ranking.VerifiedV1";  // マップ別の「投稿資格のある自己ベスト」(JSON)。投稿の有無に関係なく残す
    public const string SentKey = "Ranking.SentV1";          // 送り終えた runId(最新 50)

    public static readonly string[] Stages = { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor" };
    public static string BoardId(string stageId) => "omm_best_" + stageId;
    // 前の版(2026-10-14 の途中まで)が作ったサポートの記録か(取り除く対象)
    static bool WasSupport(Entry e) => e != null && (e.partySize > 1 || !string.IsNullOrEmpty(e.allies));
    // 同じ表 = 同じマップ + 同じ環境(開発用の環境の自己ベストが production の送信を止めないように)
    static bool SameBoard(Entry a, Entry b) => a != null && b != null && a.stageId == b.stageId && (a.env ?? "") == (b.env ?? "");
    static bool Sendable(Entry e) => e != null && UgsEnvironment.MaySubmit(e.env, out _);

    [Serializable]
    public class Entry
    {
        public string runId = "", stageId = "", characterId = "";
        public int meters;
        public float playSeconds, sprintFrom;
        public bool usedAuto, usedSprint;
        public long startedUtc, committedUtc;
        public string appVersion = "";
        public int partySize = 1;     // 常に 1(ソロ)。前の版の端末に残ったサポートの記録を見分けるために読む(2〜4 = サポート)
        public string allies = "";    // 前の版のサポートの記録の仲間(読むだけ。送らない)
        public string env = "";       // 2026-10-14: 作った時の UGS の環境(空 = 分ける前の版の物。送らない)
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
        // 2026-10-27: テスト用データ(開発版だけにある)は、公開のランキング(production)にだけ載せない。開発版は production へは送らない
        //  (UgsEnvironment.MaySubmit)ので、development では普通のランと同じに送る。テスト用データのランキングの参加/自己ベストは
        //  通常のデータとは別に持つ(SaveProfile の読み替え)。APK 387 の実機: テスト用データで確認していて、どのランも対象外になり送られていなかった
        if (r.testProfile && UgsEnvironment.IsProduction) { why = "test data"; return false; }
        if (!r.officialStage) { why = "map not officially unlocked (dev selection)"; return false; }
        if (r.Support || !string.IsNullOrEmpty(r.supportIds)) { why = "CPU support used (solo ranking only)"; return false; }
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
        partySize = 1, allies = "",
        env = UgsEnvironment.Name,
    };

    // RunLedger.CommitSuccess から(資格のある成功だけ)
    public static void OnSuccessCommitted(RunLedger.Run r)
    {
        var e = MakeEntry(r);
        var verified = Load(VerifiedKey);
        var old = verified.items.Find(x => SameBoard(x, e));
        bool better = old == null || e.meters > old.meters;
        if (better) { verified.items.RemoveAll(x => SameBoard(x, e)); verified.items.Add(e); Store(VerifiedKey, verified); }
        Log($"verified run {e.stageId} {e.meters}m{(better ? " (new verified best)" : "")}");
        RankingDiag.Note("VERIFY", $"{e.stageId} {e.meters}m better={better} (before {(old != null ? old.meters + "m" : "none")}) joined={Joined} env={e.env}");
        if (Joined && better) { Enqueue(e); nextAutoFlush = 0f; } // 2026-10-27: ホームへ戻ったらすぐ送る(30秒の間隔を待たない)
    }

    public static Entry VerifiedBest(string stageId) => Load(VerifiedKey).items.Find(x => x.stageId == stageId && !WasSupport(x) && x.env == UgsEnvironment.Name);

    // 起動時(SaveSystem.Boot): 前の版が残したサポートの記録を送信待ち/自己ベストから取り除く。ソロの物はそのまま。取り除いた数を返す
    public static int PurgeSupportEntries()
    {
        int n = 0;
        foreach (var key in new[] { PendingKey, VerifiedKey })
        {
            if (!SaveStore.HasKey(key)) continue;
            var l = Load(key);
            int before = l.items.Count;
            l.items.RemoveAll(WasSupport);
            if (l.items.Count != before) { n += before - l.items.Count; Store(key, l); }
        }
        if (n > 0) { SaveStore.Save(); Log($"removed {n} CPU-support record(s) left by an older version (the solo ranking is not affected)"); }
        return n;
    }

    // 起動時(SaveSystem.Boot): 今の環境と、送れない(別の環境/分ける前の)送信待ちの数をログへ
    public static void LogEnvironmentAtBoot()
    {
        Log($"{UgsEnvironment.Describe()}; {PendingCount} pending for this environment, {HeldCount} held (other environment or before the separation - never sent)");
    }

    // ---------------------------------------------------------------- 参加
    public static void Join()
    {
        if (Joined) return;
        SaveStore.SetInt(JoinedKey, 1);
        foreach (var e in Load(VerifiedKey).items) if (e.env == UgsEnvironment.Name) Enqueue(e); // 参加より前に確定した、資格のある自己ベスト(今の環境の物だけ)
        SaveStore.Save();
        Log("joined the ranking");
        _ = FlushAsync("join");
    }
    public static void Leave() { SaveStore.SetInt(JoinedKey, 0); SaveStore.Save(); Log("left the ranking (nothing more is sent)"); }

    // 2026-10-19: オンラインのデータを削除した後(AccountDeletion)。参加/送信待ち/送信済み/資格のある自己ベストを消す
    // (後でランキングに参加し直しても、削除した古い記録を新しい ID で送り直さない)。カード/MILE/進行/マップ別の BEST 表示は消さない
    public static void ForgetAfterDeletion()
    {
        SaveStore.DeleteKey(JoinedKey); SaveStore.DeleteKey(PendingKey); SaveStore.DeleteKey(VerifiedKey); SaveStore.DeleteKey(SentKey);
        SaveStore.Save();
        Log("ranking state on this device cleared after the online data was deleted");
    }

    // ---------------------------------------------------------------- 投稿待ち
    // 今の環境で送れる送信待ち(別の環境の物・環境を付ける前の物は HeldCount)
    public static int PendingCount => Load(PendingKey).items.FindAll(Sendable).Count;
    public static int HeldCount => Load(PendingKey).items.FindAll(x => !Sendable(x)).Count;
    // 2026-10-13: マップごとの送信待ち(順位の画面の「あなた」の状態を実際の送信待ちから出す)
    public static int PendingCountFor(string stageId) { int n = 0; foreach (var e in Load(PendingKey).items) if (e != null && e.stageId == stageId && Sendable(e)) n++; return n; }
    static void Enqueue(Entry e)
    {
        if (IsSent(e.runId) || WasSupport(e) || !Sendable(e)) return;
        var q = Load(PendingKey);
        if (q.items.Exists(x => x.runId == e.runId)) return;
        // 同じマップの、より短い投稿待ちは要らない(サーバーは自己ベストより短い物を書かない)
        q.items.RemoveAll(x => SameBoard(x, e) && x.meters <= e.meters);
        if (q.items.Exists(x => SameBoard(x, e) && x.meters > e.meters)) return;
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
    // 2026-10-27: サーバーが返事をした送信の数(受け付けた/短い/重複/拒否)。ランキングの画面は、これが変わったら読み直す
    //  (APK 386 の実機: 帰還してすぐ画面を開くと、送信より先に読み込みが終わって「記録がありません」のまま残っていた)
    public static int SentVersion { get; private set; }
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
        if (!Joined) return;
        if (Sending) { Log($"send skipped ({why}): already sending"); return; }
        var backend = Backend;
        if (backend == null) return;
        if (!UgsEnvironment.MaySubmit(UgsEnvironment.Name, out string block))
        {
            LastSendError = "blocked: " + block;
            Log($"send blocked ({why}): {block} - {UgsEnvironment.Describe()}");
            return;
        }
        Sending = true;
        try
        {
            // 2026-10-14: 1件が送れなくても、他のマップの送信待ちは送る。送れなかった物は残して後で送り直す
            var skip = new HashSet<string>();
            bool anyFailed = false;
            while (true)
            {
                var q = Load(PendingKey);
                var e = q.items.Find(x => !skip.Contains(x.runId) && Sendable(x)); // 別の環境の物は触らない(残す)
                if (e == null) break;
                var res = await backend.Submit(e, Codename.Current);
                if (res.status == SubmitStatus.Failed) { skip.Add(e.runId); anyFailed = true; LastSendError = res.message; Log($"send failed ({why}) {e.stageId}: {res.message} - kept for retry"); RankingDiag.Note("SEND", $"{BoardId(e.stageId)} {e.meters}m -> FAILED '{res.message}' player={OnlineServices.ShortId(OnlineServices.PlayerId)} env={UgsEnvironment.Name}"); continue; }
                // 受け付けた/自己ベストより短い/重複/拒否 は、どれも送り直さない
                q = Load(PendingKey);
                q.items.RemoveAll(x => x.runId == e.runId);
                Store(PendingKey, q);
                MarkSent(e.runId);
                SaveStore.Save();
                SentVersion++;
                if (!anyFailed) LastSendError = "";
                Log($"sent {e.stageId} {e.meters}m to '{e.env}' -> {res.status}{(string.IsNullOrEmpty(res.message) ? "" : " (" + res.message + ")")}");
                RankingDiag.Note("SEND", $"{BoardId(e.stageId)} {e.meters}m -> {res.status}{(string.IsNullOrEmpty(res.message) ? "" : " '" + res.message + "'")} player={OnlineServices.ShortId(OnlineServices.PlayerId)} env={UgsEnvironment.Name}");
            }
        }
        catch (Exception ex) { LastSendError = ex.GetType().Name; Debug.LogWarning("[Ranking] send error: " + ex.GetType().Name + ": " + ex.Message); RankingDiag.Note("SEND", "ERROR " + ex.GetType().Name + ": " + ex.Message); }
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
        // 2026-10-14: ソロだけ(CPU サポート/仲間ありの記録は受け付けない。サーバーの omm_submit_run.js も同じ)
        if (e.partySize != 1 || !string.IsNullOrEmpty(e.allies)) return "solo only";
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
        int sprintRef = mine != null ? mine.meters : 0; // サーバー(omm_submit_run.js)と同じ: そのマップの掲載中の自己ベスト
        string err = LeaderboardRules.Validate(e, sprintRef, d.runIds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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

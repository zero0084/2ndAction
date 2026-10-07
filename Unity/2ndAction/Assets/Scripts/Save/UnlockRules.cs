using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 新規プレイの初期状態と解放条件(2026-10-07)。解放した状態は永続(戻さない)。
//  マップ: 荒野街道=最初から / 自然洞窟=荒野街道で30,000m / 天空回廊=自然洞窟で30,000m / 闘技場=天空回廊で30,000m
//          ラスダン=通常3マップすべてで死神に遭遇 + 累計1,000,000m(完全な隠し要素: 解放前は一覧にも出さない)
//  キャラ: 黒剣士=最初から / 双剣士・二丁拳銃士・竜騎士=荒野街道 30k/50k/100k / 弓・魔法・格闘=自然洞窟 30k/50k/100k /
//          忍者・巫女・吸血鬼=天空回廊 30k/50k/100k / 竜人=ラスダン解放と同時(解放前は一覧に出さない) /
//          お嬢様騎士=ラン用マップで1,000m以下でゲームオーバー(1,000mちょうども対象。リタイア/闘技場は対象外)
//  距離は1回のランで「到達した瞬間」に判定して保存する(その後倒れても取り消さない)。CONTINUE は同じランの続き。
//  数えない: 闘技場/操作の練習/Debug Run(DebugRun.WritesBlocked)、開発版のワープで距離を飛ばしたラン(RunSkipped)。
//  既存データ: この仕組みより前のデータで遊んだ形跡があれば、今使えるマップ/キャラ(=全部)をそのまま解放済みにする(取り上げない)。
//            ラスダンは既存の解放フラグのまま。死神の「マップ別」遭遇はこれまで記録していないので推測で作らない。
public static class UnlockRules
{
    public const string InitKey = "UnlockRulesV1";
    public const string StagesKey = "UnlockedStagesV1";       // 解放したマップ(カンマ区切り。arena を含む)
    public const string CharsKey = "UnlockedCharsV1";         // 解放したキャラ(カンマ区切り)
    public const string NotifiedKey = "UnlockNotifiedV1";     // お知らせを確認した解放(カンマ区切り。s:<id> / c:<id>)
    public const string ReachPrefix = "RunReachV1_";          // + stageId: 1回のランで到達した最高距離(進捗の表示用、double文字列)
    public const string ReaperMapPrefix = "ReaperMapV1_";     // + stageId: そのマップで死神戦が始まった(遭遇)
    public const string RevealShownKey = "LastDungeonRevealShown"; // ラスダン出現演出を出した
    public const string DevUnlockAllKey = "Dev.UnlockAll";    // 開発版だけ: 全マップ/全キャラを選べる(正式な解放状態は変えない)

    public const string Wasteland = "wasteland_road", Cave = "natural_cave", Sky = "sky_corridor", Arena = "arena";
    public static readonly string[] NormalMaps = { Wasteland, Cave, Sky };
    public const float MapUnlockMeters = 30000f;
    public const float NobleLadyMaxMeters = 1000f;

    public struct Rule { public string id, stage; public float meters; public Rule(string i, string s, float m) { id = i; stage = s; meters = m; } }
    // マップ: 前のマップで 30,000m
    public static readonly Rule[] StageRules = { new Rule(Cave, Wasteland, 30000f), new Rule(Sky, Cave, 30000f), new Rule(Arena, Sky, 30000f) };
    public static readonly Rule[] CharRules =
    {
        new Rule("dual_blade", Wasteland, 30000f), new Rule("gunslinger", Wasteland, 50000f), new Rule("dragon_lancer", Wasteland, 100000f),
        new Rule("archer", Cave, 30000f), new Rule("mage", Cave, 50000f), new Rule("fighter", Cave, 100000f),
        new Rule("ninja", Sky, 30000f), new Rule("miko", Sky, 50000f), new Rule("vampire", Sky, 100000f),
    };
    public const string StartCharacter = "swordsman", HiddenCharacter = "dragonkin", NobleLady = "noble_lady";

    static HashSet<string> stages, chars, notified;
    static readonly Dictionary<string, double> reach = new Dictionary<string, double>();
    public static void Reload() { stages = chars = notified = null; reach.Clear(); }

    static HashSet<string> Set(ref HashSet<string> s, string key)
    {
        if (s != null) return s;
        s = new HashSet<string>();
        foreach (var p in SaveStore.GetString(key, "").Split(',')) if (!string.IsNullOrEmpty(p)) s.Add(p.Trim());
        return s;
    }
    static HashSet<string> Stages => Set(ref stages, StagesKey);
    static HashSet<string> Chars => Set(ref chars, CharsKey);
    static HashSet<string> Notified => Set(ref notified, NotifiedKey);

    public static bool DevUnlockAll
    {
        get { return Debug.isDebugBuild && SaveStore.GetInt(DevUnlockAllKey, 0) != 0; }
        set { if (Debug.isDebugBuild) { SaveStore.SetInt(DevUnlockAllKey, value ? 1 : 0); SaveStore.Save(); } }
    }

    // ---------------------------------------------------------------- 問い合わせ
    public static bool IsStageUnlocked(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return false;
        if (stageId == Wasteland) return true;
        if (stageId == BossManager.LastStageId) return ProgressStats.FinalDungeonAvailable;
        if (DevUnlockAll) return true;
        return Stages.Contains(stageId) || !IsRuledStage(stageId); // 条件の無いマップ(テスト用など)は今までどおり
    }
    static bool IsRuledStage(string id) { foreach (var r in StageRules) if (r.id == id) return true; return false; }
    public static bool IsArenaUnlocked => IsStageUnlocked(Arena);
    public static bool OfficialStageUnlocked(string id) => id == Wasteland || Stages.Contains(id);

    public static bool IsCharacterUnlocked(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (id == StartCharacter) return true;
        if (DevUnlockAll) return true;
        return Chars.Contains(id);
    }
    // 竜人は解放前は一覧にも出さない(ラスダンの存在を明かさない)
    public static bool IsCharacterVisible(string id) => id != HiddenCharacter || IsCharacterUnlocked(id);

    public static double Reach(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return 0;
        if (reach.TryGetValue(stageId, out double v)) return v;
        v = ProgressStats.ReadDouble(ReachPrefix + stageId);
        reach[stageId] = v;
        return v;
    }
    public static bool ReaperMetOn(string stageId) => SaveStore.GetInt(ReaperMapPrefix + stageId, 0) != 0;
    public static int ReaperMapsMet { get { int n = 0; foreach (var m in NormalMaps) if (ReaperMetOn(m)) n++; return n; } }

    public static string StageName(string id)
    {
        if (id == Arena) return "闘技場";
        var d = StageDatabase.FindById(id);
        return d != null ? d.displayName : id;
    }
    public static string CharName(string id) { var d = CharacterDatabase.FindById(id); return d != null ? d.displayName : id; }

    // 解放条件の文(未解放の時に見せる)。ラスダン/竜人は呼ばない(隠し要素)
    public static string ConditionText(Rule r) => $"{StageName(r.stage)}で{r.meters:N0}m到達";
    public static string ProgressText(Rule r) => $"最高 {System.Math.Floor(Reach(r.stage)):N0}m / {r.meters:N0}m";
    public static bool TryStageRule(string id, out Rule rule) { foreach (var r in StageRules) if (r.id == id) { rule = r; return true; } rule = default; return false; }
    public static bool TryCharRule(string id, out Rule rule) { foreach (var r in CharRules) if (r.id == id) { rule = r; return true; } rule = default; return false; }
    public static string CharConditionText(string id)
    {
        if (id == NobleLady) return "いずれかのマップで1,000m以下でゲームオーバー";
        return TryCharRule(id, out var r) ? ConditionText(r) + "\n" + ProgressText(r) : "";
    }

    // ---------------------------------------------------------------- 記録(ランから)
    // ランを数えるか(闘技場/練習/Debug Run/距離を飛ばした開発版のラン/ラスダンは数えない)
    public static bool RunSkipped; // 開発版のワープで距離を飛ばした(このランは解放に数えない。新しいランで戻す)
    static bool Counts(string stageId) => !DebugRun.WritesBlocked && !RunSkipped && !string.IsNullOrEmpty(stageId) && stageId != BossManager.LastStageId;

    // GameManager.ReportDistance から(距離が伸びた時)。到達した瞬間に解放して保存する
    public static void OnRunDistance(string stageId, double distance)
    {
        if (!Counts(stageId)) return;
        double prev = Reach(stageId);
        if (distance <= prev) return;
        reach[stageId] = distance;
        bool crossed = false;
        foreach (var r in StageRules) if (r.stage == stageId && prev < r.meters && distance >= r.meters) { UnlockStage(r.id); crossed = true; }
        foreach (var r in CharRules) if (r.stage == stageId && prev < r.meters && distance >= r.meters) { UnlockChar(r.id); crossed = true; }
        // 進捗(最高到達)は 500m ごと/解放の瞬間/ラン終了で保存(毎フレームは書かない)
        if (crossed || System.Math.Floor(distance / 500.0) > System.Math.Floor(prev / 500.0)) SaveReach(stageId, true);
    }

    static void SaveReach(string stageId, bool flush)
    {
        if (!reach.TryGetValue(stageId, out double v)) return;
        SaveStore.SetString(ReachPrefix + stageId, v.ToString("R", CultureInfo.InvariantCulture));
        if (flush) SaveStore.Save();
    }

    // ランが終わった時(倒れた/脱出/ホームへ)。realGameOver = 実際のゲームオーバー(リタイアではない)
    public static void OnRunEnded(string stageId, double distance, bool realGameOver)
    {
        try { OnRunEndedInner(stageId, distance, realGameOver); } finally { ClearRunFlags(); }
    }

    static void OnRunEndedInner(string stageId, double distance, bool realGameOver)
    {
        if (!Counts(stageId) && !(realGameOver && stageId == BossManager.LastStageId && !DebugRun.WritesBlocked && !RunSkipped)) return;
        if (Counts(stageId)) { OnRunDistance(stageId, distance); SaveReach(stageId, false); }
        if (realGameOver && distance <= NobleLadyMaxMeters) UnlockChar(NobleLady);
        SaveStore.Save();
    }

    // ランが終わった後(結果/ホーム)は「飛ばしたラン」の印を戻す(次のランへ持ち越さない)
    public static void ClearRunFlags() { RunSkipped = false; }

    // 死神戦が始まった(撃破は不要)。通常3マップだけマップ別に記録
    public static void OnReaperMet(string stageId)
    {
        if (DebugRun.WritesBlocked || System.Array.IndexOf(NormalMaps, stageId) < 0) return;
        if (!ReaperMetOn(stageId)) { SaveStore.SetInt(ReaperMapPrefix + stageId, 1); SaveStore.Save(); Debug.Log($"[Unlock] reaper met on {stageId} ({ReaperMapsMet}/3)"); }
        EvaluateLastDungeon();
    }

    // ラスダン: 通常3マップすべてで死神に遭遇 + 累計1,000,000m。満たした時に保存し、竜人も解放。演出はホームで1回
    public static bool EvaluateLastDungeon()
    {
        if (DebugRun.WritesBlocked) return false;
        if (ProgressStats.FinalDungeonUnlocked) { EnsureRevealQueued(); return false; }
        if (ReaperMapsMet < NormalMaps.Length || ProgressStats.LifetimeDistance < ProgressStats.UnlockDistance) return false;
        ProgressStats.SetFinalDungeonUnlocked();
        UnlockChar(HiddenCharacter, notify: false); // 竜人は演出の後のお知らせで
        Debug.Log($"[Unlock] LAST DUNGEON unlocked (lifetime {ProgressStats.LifetimeDistance:F0}m, reaper on all 3 maps)");
        EnsureRevealQueued();
        return true;
    }

    // ---------------------------------------------------------------- 解放と通知
    static void UnlockStage(string id)
    {
        if (!Stages.Add(id)) return;
        SaveStore.SetString(StagesKey, string.Join(",", stages));
        SaveStore.Save();
        Debug.Log($"[Unlock] stage {id}");
        QueueNotice("s:" + id);
        NoticeQueue.Toast("UNLOCKED: " + StageName(id));
    }

    static void UnlockChar(string id, bool notify = true)
    {
        if (!Chars.Add(id)) return;
        SaveStore.SetString(CharsKey, string.Join(",", chars));
        SaveStore.Save();
        Debug.Log($"[Unlock] character {id}");
        if (notify) { QueueNotice("c:" + id); NoticeQueue.Toast("NEW CHARACTER: " + CharName(id)); }
    }

    static void MarkNotified(string key)
    {
        if (!Notified.Add(key)) return;
        SaveStore.SetString(NotifiedKey, string.Join(",", notified));
        SaveStore.Save();
    }

    static void QueueNotice(string key)
    {
        if (Notified.Contains(key)) return;
        string id = key.Substring(2);
        if (key.StartsWith("s:"))
        {
            string body = id == Arena ? "ホームの「闘技場」から入れます。" : "マップ選択から出発できます。";
            NoticeQueue.Enqueue("unlock_" + key, $"{StageName(id)} が解放されました", body, () => MarkNotified(key));
        }
        else
        {
            NoticeQueue.Enqueue("unlock_" + key, $"{CharName(id)} が仲間になりました", "キャラ選択で選べます。", () => MarkNotified(key));
        }
    }

    static void EnsureRevealQueued()
    {
        if (!ProgressStats.FinalDungeonUnlocked || SaveStore.GetInt(RevealShownKey, 0) != 0) return;
        NoticeQueue.Enqueue("lastdungeon_reveal", "", "走り続けたあなたへ。\n最後の道が開かれました。", () =>
        {
            SaveStore.SetInt(RevealShownKey, 1); SaveStore.Save();
            if (IsCharacterUnlocked(HiddenCharacter)) QueueNotice("c:" + HiddenCharacter);
        }, special: true);
    }

    // 起動/データの切り替えの後: 解放したのに確認していないお知らせを並べ直す(アプリが落ちても失わない)
    public static void RequeuePending()
    {
        foreach (var s in Stages) QueueNotice("s:" + s);
        foreach (var c in Chars) if (c != HiddenCharacter || SaveStore.GetInt(RevealShownKey, 0) != 0) QueueNotice("c:" + c);
        EvaluateLastDungeon();
    }

    // ---------------------------------------------------------------- 起動時(SaveSystem.Boot)
    public static string EnsureInitialized()
    {
        if (SaveStore.HasKey(InitKey)) return "";
        SaveStore.SetInt(InitKey, 1);
        bool played = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance) > 0.0 || SaveStore.GetFloat("BestDistance", 0f) > 0f;
        string note;
        if (played)
        {
            // 既存のデータ: 今まで全部使えた(解放の仕組みが無かった)ので、取り上げずに全部解放済みにする。お知らせは出さない
            var st = new List<string> { Cave, Sky, Arena };
            var ch = new List<string>();
            foreach (var c in CharacterDatabase.AllCharacters) if (c != null && c.characterId != StartCharacter) ch.Add(c.characterId);
            SaveStore.SetString(StagesKey, string.Join(",", st));
            SaveStore.SetString(CharsKey, string.Join(",", ch));
            var n = new List<string>();
            foreach (var s in st) n.Add("s:" + s);
            foreach (var c in ch) n.Add("c:" + c);
            SaveStore.SetString(NotifiedKey, string.Join(",", n));
            if (ProgressStats.FinalDungeonUnlocked) SaveStore.SetInt(RevealShownKey, 1);
            // 進捗の表示: マップ別BEST(終了時の記録)から(確実に分かる値だけ)
            foreach (var m in NormalMaps)
            {
                double b = ProgressStats.ReadDouble(SaveKeys.StageBestPrefix + m);
                if (b > 0) SaveStore.SetString(ReachPrefix + m, b.ToString("R", CultureInfo.InvariantCulture));
            }
            note = $"unlocks: existing player -> kept everything available ({st.Count} maps incl. arena, {ch.Count} characters), last dungeon flag {(ProgressStats.FinalDungeonUnlocked ? "ON" : "OFF")}, reaper-per-map not inferred; ";
        }
        else
        {
            SaveStore.SetString(StagesKey, "");
            SaveStore.SetString(CharsKey, "");
            note = "unlocks: new data -> Wasteland Road + Swordsman only; ";
        }
        Reload();
        return note;
    }
}

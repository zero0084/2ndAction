using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 新規プレイの初期状態と解放条件(2026-10-07)。解放した状態は永続(戻さない)。
//  マップ: 荒野街道=最初から / 自然洞窟=荒野街道で30,000m / 天空回廊=自然洞窟で30,000m / 闘技場=天空回廊で30,000m
//          ラスダン=通常3マップすべてで死神に遭遇 + 累計1,000,000m(完全な隠し要素: 解放前は一覧にも出さない)
//  キャラ: 黒剣士=最初から / 双剣士・二丁拳銃士・竜騎士=荒野街道 30k/50k/100k / 弓・魔法・格闘=自然洞窟 30k/50k/100k /
//          忍者・巫女・吸血鬼=天空回廊 30k/50k/100k / 竜人=ラスダン解放と同時(解放前は一覧に出さない) /
//          お嬢様騎士=ラン用マップで1,000m以下でゲームオーバー(1,000mちょうども対象。リタイア/闘技場は対象外)
//  2026-10-08(仕様変更): 距離の条件はラン中は「仮判定」だけ。正規の帰還(脱出/ラスダンの終わり)= 成功の時に
//  RunLedger.CommitSuccess → CommitRun で正式に解放する。ゲームオーバーなら、そのランによる距離の解放は無い。
//  一度正式に解放したものは、以後の失敗で取り消さない。お嬢様騎士だけはゲームオーバーが条件(帰還は不要)。
//  未解放のマップ/キャラは一覧から完全に隠す(鍵の枠/シルエット/条件/総数も出さない)。解放したら NEW を付ける(SeenV1)。
//  CONTINUE は同じランの続き(仮判定も中断データの台帳 RunLedger から)。
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
    public const string SeenKey = "UnlockSeenV1";             // 2026-10-08: 一覧で見た解放(s:<id> / c:<id>)。見ていない物に NEW を付ける

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
    public static void Reload() { stages = chars = notified = seen = null; reach.Clear(); }

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
    // 2026-10-08: 未解放のキャラは一覧に出さない(竜人に限らず全員。鍵/シルエット/総数も出さない)
    public static bool IsCharacterVisible(string id) => IsCharacterUnlocked(id);
    public static bool IsStageVisible(string id) => IsStageUnlocked(id);

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

    // GameManager.ReportDistance から(距離が伸びた時)。2026-10-08: ラン中は「仮判定」だけ(保存しない・名前を出さない)。
    // まだ解放していない物の条件を満たしたら、控えめに「帰還すると確定」とだけ知らせる(何が解放されるかは言わない)
    static double runProvisionalFrom = -1; static string runProvisionalStage;
    public static int ProvisionalCount { get; private set; }
    public static void OnRunDistance(string stageId, double distance)
    {
        if (!Counts(stageId)) return;
        if (runProvisionalStage != stageId) { runProvisionalStage = stageId; runProvisionalFrom = -1; ProvisionalCount = 0; }
        double prev = runProvisionalFrom;
        if (distance <= prev) return;
        runProvisionalFrom = distance;
        int n = 0;
        foreach (var r in StageRules) if (r.stage == stageId && prev < r.meters && distance >= r.meters && !Stages.Contains(r.id)) n++;
        foreach (var r in CharRules) if (r.stage == stageId && prev < r.meters && distance >= r.meters && !Chars.Contains(r.id)) n++;
        if (n > 0 && prev >= 0)
        {
            ProvisionalCount += n;
            Debug.Log($"[Unlock] provisional: {n} condition(s) met on {stageId} at {distance:F0}m (confirmed only by returning safely)");
            NoticeQueue.Toast("新しい発見の条件を満たしました。無事に帰還すると確定します");
        }
    }

    // 2026-10-08: 成功したラン(正規の帰還)の確定。1回のランで到達した距離で正式に解放する(一度だけ、RunLedger から)
    public static void CommitRun(string stageId, double reached, RunLedger.CommitResult res)
    {
        if (!Counts(stageId)) return;
        if (reached > Reach(stageId)) { reach[stageId] = reached; SaveReach(stageId, false); }
        foreach (var r in StageRules) if (r.stage == stageId && reached >= r.meters && UnlockStage(r.id)) res?.unlockedStages.Add(r.id);
        foreach (var r in CharRules) if (r.stage == stageId && reached >= r.meters && UnlockChar(r.id)) res?.unlockedChars.Add(r.id);
    }

    // 2026-10-08: 本当のゲームオーバー(リタイア/闘技場/練習は来ない)。お嬢様騎士: ラン用マップで 1,000m 以下(ちょうども対象)
    public static void OnRealGameOver(string stageId, double reached)
    {
        if (DebugRun.WritesBlocked || RunSkipped || string.IsNullOrEmpty(stageId)) return;
        bool runMap = System.Array.IndexOf(NormalMaps, stageId) >= 0 || stageId == BossManager.LastStageId;
        if (runMap && reached <= NobleLadyMaxMeters) { UnlockChar(NobleLady); SaveStore.Save(); }
    }

    static void SaveReach(string stageId, bool flush)
    {
        if (!reach.TryGetValue(stageId, out double v)) return;
        SaveStore.SetString(ReachPrefix + stageId, v.ToString("R", CultureInfo.InvariantCulture));
        if (flush) SaveStore.Save();
    }

    // ランが終わった後(結果/ホーム)は「飛ばしたラン」の印と仮判定を戻す(次のランへ持ち越さない)
    public static void ClearRunFlags() { RunSkipped = false; runProvisionalStage = null; runProvisionalFrom = -1; ProvisionalCount = 0; }

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
    static bool UnlockStage(string id)
    {
        if (!Stages.Add(id)) return false;
        SaveStore.SetString(StagesKey, string.Join(",", stages));
        SaveStore.Save();
        Debug.Log($"[Unlock] stage {id}");
        QueueNotice("s:" + id);
        NoticeQueue.Toast("UNLOCKED: " + StageName(id));
        return true;
    }

    static bool UnlockChar(string id, bool notify = true)
    {
        if (!Chars.Add(id)) return false;
        SaveStore.SetString(CharsKey, string.Join(",", chars));
        SaveStore.Save();
        Debug.Log($"[Unlock] character {id}");
        if (notify) { QueueNotice("c:" + id); NoticeQueue.Toast("NEW CHARACTER: " + CharName(id)); }
        return true;
    }

    // ---------------------------------------------------------------- NEW(一覧で見たか)
    static HashSet<string> seen;
    static HashSet<string> Seen => Set(ref seen, SeenKey);
    // 解放していて、一覧でまだ見ていない(最初から使える物と既存データの解放は「見た」扱い)
    public static bool IsNewStage(string id) => id != Wasteland && IsOfficialStage(id) && !Seen.Contains("s:" + id);
    public static bool IsNewCharacter(string id) => id != StartCharacter && Chars.Contains(id) && !Seen.Contains("c:" + id);
    static bool IsOfficialStage(string id) => id == BossManager.LastStageId ? ProgressStats.FinalDungeonUnlocked : Stages.Contains(id);
    public static bool AnyNewStage { get { foreach (var r in StageRules) if (r.id != Arena && IsNewStage(r.id)) return true; return IsNewStage(BossManager.LastStageId); } }
    public static bool AnyNewCharacter { get { foreach (var c in Chars) if (IsNewCharacter(c) && IsCharacterVisible(c)) return true; return false; } }
    public static bool NewArena => IsNewStage(Arena);
    public static void MarkSeen(string key)
    {
        if (!Seen.Add(key)) return;
        SaveStore.SetString(SeenKey, string.Join(",", seen));
        SaveStore.Save();
    }
    public static void MarkStageSeen(string id) { if (IsNewStage(id)) MarkSeen("s:" + id); }
    public static void MarkCharacterSeen(string id) { if (IsNewCharacter(id)) MarkSeen("c:" + id); }

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
            SaveStore.SetString(SeenKey, string.Join(",", n)); // 2026-10-08: 既存の解放には NEW を付けない
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

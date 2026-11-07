using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// プロフィール/常設チャレンジの保存(2026-11-07)。キーは全部 SaveKeys に「進行」で登録(引き継ぎで一緒に移り、製品版の初期化で一緒に消える)。
//  ・達成済みのチャレンジ = 獲得済みのバッヂ(1対1。別々に持つと食い違うので、バッヂは達成の記録から決める)
//  ・壊れた値は読み込みで捨てる(知らない ID・重複・未獲得のバッヂの装備は読まない)。全体を初期化しない
public static class ChallengeStore
{
    public const string DoneKey = "ChallengesDoneV1";        // "id@unix秒[@r];..."(r = 既存の記録から復元した)
    public const string CountersKey = "ChallengeCountersV1"; // "air_kills=12;breaks=3"(累計の数え)
    public const string InitKey = "ChallengeInitV1";         // 1 = 既存の記録からの復元を済ませた

    public struct Entry { public long at; public bool restored; }
    static Dictionary<string, Entry> done;
    static Dictionary<string, int> counters;

    public static event Action<ChallengeDef, bool> Completed; // (チャレンジ, 復元か)

    public static void Reload() { done = null; counters = null; }

    static Dictionary<string, Entry> Done
    {
        get
        {
            if (done != null) return done;
            done = new Dictionary<string, Entry>();
            foreach (var part in SaveStore.GetString(DoneKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split('@');
                if (f.Length < 1 || ChallengeDefs.Find(f[0]) == null || done.ContainsKey(f[0])) continue; // 知らない ID/重複は捨てる
                long.TryParse(f.Length > 1 ? f[1] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long at);
                done[f[0]] = new Entry { at = at, restored = f.Length > 2 && f[2] == "r" };
            }
            return done;
        }
    }

    static void WriteDone()
    {
        var sb = new StringBuilder();
        foreach (var c in ChallengeDefs.All)
            if (Done.TryGetValue(c.id, out var e)) sb.Append(c.id).Append('@').Append(e.at.ToString(CultureInfo.InvariantCulture)).Append(e.restored ? "@r" : "").Append(';');
        SaveStore.SetString(DoneKey, sb.ToString());
    }

    public static bool IsDone(string id) => !string.IsNullOrEmpty(id) && Done.ContainsKey(id);
    public static bool WasRestored(string id) => Done.TryGetValue(id ?? "", out var e) && e.restored;
    public static int DoneCount => Done.Count;
    public static int Total => ChallengeDefs.All.Length;

    // 達成。新しく達成した時だけ true(報酬は1回だけ: 既に達成済みなら何もしない)。restored = 既存の記録から(通知は出さない)
    public static bool Complete(string id, bool restored = false, bool save = true)
    {
        var d = ChallengeDefs.Find(id);
        if (d == null || IsDone(id)) return false;
        if (DebugRun.BlocksSave("Challenge_" + id)) return false; // 記録対象外のラン(闘技場/練習/DEBUG RUN)
        Done[id] = new Entry { at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), restored = restored };
        WriteDone();
        GrantMile(d.mile);
        if (save) SaveStore.Save(); // 達成の印と MILE を1回の保存で(片方だけ残らない)
        Debug.Log($"[Challenge] {(restored ? "restored" : "COMPLETE")} {d.id} '{d.name}' +{d.mile} MILE ({DoneCount}/{Total})");
        try { Completed?.Invoke(d, restored); } catch (Exception e) { Debug.LogWarning("[Challenge] " + e.Message); }
        if (!restored) ChallengeToast.Show(d);
        return true;
    }

    static void GrantMile(int mile)
    {
        if (mile <= 0) return;
        var gm = GameManager.Instance;
        if (gm != null) gm.AddMileWithoutFlush(mile); // 所持 MILE(ランの MILE とは別。ランを中断/失敗しても戻らない)
        else SaveStore.SetInt(GameManager.TotalMileKey, Mathf.Max(0, SaveStore.GetInt(GameManager.TotalMileKey, 0) + mile));
    }

    // ---- 累計の数え(空中撃破/BREAK) ----
    static Dictionary<string, int> Counters
    {
        get
        {
            if (counters != null) return counters;
            counters = new Dictionary<string, int>();
            foreach (var part in SaveStore.GetString(CountersKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 0) counters[part.Substring(0, eq)] = v;
            }
            return counters;
        }
    }
    public static int Counter(string key) => Counters.TryGetValue(key, out int v) ? v : 0;
    // 数えを足す(書くだけ。ディスクへの保存はほかの定期の保存と一緒/達成の時)
    public static int AddCounter(string key, int n)
    {
        if (n <= 0 || DebugRun.BlocksSave("ChallengeCounter_" + key)) return Counter(key);
        int v = Counter(key) + n;
        Counters[key] = v;
        WriteCounters();
        return v;
    }
    static void WriteCounters()
    {
        var sb = new StringBuilder();
        foreach (var kv in Counters) sb.Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
        SaveStore.SetString(CountersKey, sb.ToString());
    }

    // ---- 既存の記録からの復元(更新後の初回起動で1回。SaveSystem.Boot から) ----
    //  既存の記録から確かに分かる物だけ: 到達距離(マップ別 BEST と1回のランの最高到達)・累計距離・死神との遭遇。
    //  ノーダメージ/コンボ/速さ/オート/BREAK/ボスの撃破は記録が無いので復元しない。
    //  復元分も報酬(バッヂ+MILE)は1回だけ受け取る(達成の印があるので、後で同じ条件を満たしても二度は出ない)。
    //  通知は連発しない: 1件のお知らせ(ホーム)にまとめる
    public static string EnsureInitialized()
    {
        if (SaveStore.GetInt(InitKey, 0) != 0) return "";
        int n = 0, mile = 0;
        foreach (var d in ChallengeDefs.All)
        {
            if (!d.restorable || IsDone(d.id) || !RecordSays(d.id)) continue;
            if (Complete(d.id, restored: true, save: false)) { n++; mile += d.mile; }
        }
        SaveStore.SetInt(InitKey, 1);
        if (n > 0)
            NoticeQueue.Enqueue("challenges_restored", Loc.T(LocProfile.RestoredTitle), Loc.F(LocProfile.RestoredBody, n, mile), null);
        return $"challenges: initialized (restored {n}, +{mile} MILE); ";
    }

    // 保存されている記録だけで達成と言えるか(ラン中ではなく、ホーム/起動時に見る)
    public static bool RecordSays(string id)
    {
        switch (id)
        {
            case "dist_10k": return BestReach() >= ChallengeDefs.Reach1;
            case "dist_50k": return BestReach() >= ChallengeDefs.Reach2;
            case "dist_100k": return BestReach() >= ChallengeDefs.Reach3;
            case "dist_total": return ProgressStats.LifetimeDistance >= ChallengeDefs.LifetimeGoal;
            case "reaper_meet": return AnyReaperMet();
        }
        return false;
    }

    // 1回のランで到達した一番遠い距離(マップ別 BEST = 脱出して確定 / 1回のランの最高到達 = 解放の進捗、失敗したランも含む)
    public static double BestReach()
    {
        double m = 0;
        foreach (var st in AllStageIds())
        {
            m = Math.Max(m, GameManager.ReadStageBest(st));
            m = Math.Max(m, UnlockRules.Reach(st));
        }
        return m;
    }
    static IEnumerable<string> AllStageIds()
    {
        yield return UnlockRules.Wasteland; yield return UnlockRules.Cave; yield return UnlockRules.Sky; yield return BossManager.LastStageId;
    }
    public static bool AnyReaperMet()
    {
        foreach (var s in ProgressStats.Sisters) if (ProgressStats.HasMet(s)) return true;
        foreach (var m in UnlockRules.NormalMaps) if (UnlockRules.ReaperMetOn(m)) return true;
        return false;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用(自動テスト): 達成/数え/初期化の印を消す(MILE は戻さない)
    public static void DevResetAll()
    {
        SaveStore.DeleteKey(DoneKey); SaveStore.DeleteKey(CountersKey); SaveStore.DeleteKey(InitKey);
        Reload(); SaveStore.Save();
    }
    public static void DevSetCounter(string key, int v) { Counters[key] = Mathf.Max(0, v); WriteCounters(); }
#endif
}

// 走る理由・装備中のバッヂ・最高速度の記録
public static class ProfileStore
{
    public const string ReasonKey = "ProfileReasonV1";       // int: 走る理由の ID(0 = 未選択)
    public const string BadgesKey = "ProfileBadgesV1";       // "id,id,id"(3枠。空き = 空文字)
    public const string TopSpeedKey = "ProfileTopSpeedV1";   // 最高速度 km/h(float の文字列。この版から測る)
    public const string BestCharKey = "ProfileBestCharV1";   // 全体の BEST を出したキャラ "キャラID|距離m[|p]"(p = 既存データの暫定。2026-11-08)
    public const int Slots = 3;

    static string[] slots;
    static float topSpeed = -1f, topSpeedWritten = -1f;

    public static void Reload() { slots = null; topSpeed = -1f; topSpeedWritten = -1f; }

    // ---- 走る理由 ----
    public static int ReasonId { get { int v = SaveStore.GetInt(ReasonKey, 0); return LocProfile.ReasonText(v) != null ? v : 0; } }
    public static bool SetReason(int id)
    {
        if (id != 0 && LocProfile.ReasonText(id) == null) return false;
        SaveStore.SetInt(ReasonKey, id);
        SaveStore.Save();
        Debug.Log($"[Profile] reason -> {id}");
        return true;
    }

    // ---- 装備中のバッヂ(3枠) ----
    public static string[] EquippedSlots
    {
        get
        {
            if (slots != null) return slots;
            slots = new string[Slots];
            var raw = SaveStore.GetString(BadgesKey, "").Split(',');
            var seen = new HashSet<string>();
            for (int i = 0; i < Slots && i < raw.Length; i++)
            {
                string id = raw[i].Trim();
                // 未獲得/知らない/重複は装備しない(読み込みで捨てる)
                if (id.Length == 0 || !ChallengeStore.IsDone(id) || !seen.Add(id)) { slots[i] = ""; continue; }
                slots[i] = id;
            }
            return slots;
        }
    }
    public static string Slot(int i) => i >= 0 && i < Slots ? EquippedSlots[i] : "";
    public static int SlotOf(string id) { for (int i = 0; i < Slots; i++) if (EquippedSlots[i] == id) return i; return -1; }

    public enum EquipResult { Ok, NotOwned, BadSlot, Unknown }
    // 枠 i へ装備。未獲得は装備できない。ほかの枠に同じバッヂがあれば、そこから移す(重複しない)
    public static EquipResult Equip(int i, string id)
    {
        if (i < 0 || i >= Slots) return EquipResult.BadSlot;
        if (ChallengeDefs.Find(id) == null) return EquipResult.Unknown;
        if (!ChallengeStore.IsDone(id)) return EquipResult.NotOwned;
        var s = EquippedSlots;
        for (int k = 0; k < Slots; k++) if (k != i && s[k] == id) s[k] = "";
        s[i] = id;
        WriteSlots();
        return EquipResult.Ok;
    }
    public static void Unequip(int i) { if (i < 0 || i >= Slots) return; EquippedSlots[i] = ""; WriteSlots(); }
    static void WriteSlots()
    {
        SaveStore.SetString(BadgesKey, string.Join(",", EquippedSlots));
        SaveStore.Save();
        Debug.Log("[Profile] badges -> " + string.Join(",", EquippedSlots));
    }

    // ---- 代表キャラ = 全体の BEST を出したキャラ(2026-11-08) ----
    //  確定した成功のランで全体の BEST をはっきり超えた時だけ、距離とキャラ ID を一緒に書く(同じ距離なら前のキャラのまま)。
    //  キャラを選び直しても変わらない。この項目より前のデータは、更新後の初回起動で「その時選んでいたキャラ」を暫定として1回だけ書く
    public struct BestChar { public string id; public double meters; public bool provisional; }
    public static BestChar ReadBestChar()
    {
        var b = new BestChar { id = "" };
        var f = SaveStore.GetString(BestCharKey, "").Split('|');
        if (f.Length < 2) return b;
        string id = f[0].Trim();
        if (id.Length == 0 || CharacterDatabase.FindById(id) == null) return b; // 知らないキャラ(壊れた値)は読まない
        if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double m) || double.IsNaN(m) || double.IsInfinity(m) || m < 0) return b;
        b.id = id; b.meters = m; b.provisional = f.Length > 2 && f[2] == "p";
        return b;
    }
    static void WriteBestChar(string id, double meters, bool provisional)
    {
        SaveStore.SetString(BestCharKey, id + "|" + meters.ToString("R", CultureInfo.InvariantCulture) + (provisional ? "|p" : ""));
    }
    // 確定(RunLedger.CommitSuccess / CommitJournal の復旧)から。prevOverall = 確定の前の全体の BEST。保存は呼び出し側の保存と一緒に
    public static bool NoteCommittedRun(string characterId, double reached, double prevOverall)
    {
        if (string.IsNullOrEmpty(characterId) || CharacterDatabase.FindById(characterId) == null) return false;
        if (DebugRun.BlocksSave("ProfileBestChar")) return false;
        var cur = ReadBestChar();
        if (!(reached > prevOverall) || (cur.id.Length > 0 && !(reached > cur.meters))) return false; // はっきり超えた時だけ
        WriteBestChar(characterId, reached, false);
        Debug.Log($"[Profile] best character -> {characterId} ({reached:F1}m, previous best {prevOverall:F1}m)");
        return true;
    }
    // 起動時(SaveSystem.Boot)に1回: 記録があるのに代表キャラが無い既存データ = 今選んでいるキャラを暫定で書く
    public static string EnsureBestChar()
    {
        if (ReadBestChar().id.Length > 0) return "";
        double best = PlayerProfileCard.BestOverall(null);
        if (best <= 0.0) return ""; // 記録が無い: 書かない(最初の BEST の時に書く)
        string sel = SaveStore.GetString("SelectedCharacterId", "");
        if (CharacterDatabase.FindById(sel) == null) sel = DefaultSave.StartingCharacterId();
        if (string.IsNullOrEmpty(sel)) return "";
        WriteBestChar(sel, best, true);
        return $"profile: best character provisional = {sel} ({best:F0}m); ";
    }
    // プロフィールに出すキャラ: BEST のキャラ。まだ記録が無い時だけ今選んでいるキャラ
    public static string DisplayCharacterId(string selected)
    {
        var b = ReadBestChar();
        return b.id.Length > 0 ? b.id : selected ?? "";
    }

    // ---- 最高速度(この版から測る。記録対象外のランは数えない) ----
    public static float TopSpeedKmh
    {
        get
        {
            if (topSpeed < 0f)
            {
                float.TryParse(SaveStore.GetString(TopSpeedKey, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out topSpeed);
                if (float.IsNaN(topSpeed) || float.IsInfinity(topSpeed) || topSpeed < 0f || topSpeed > 2000f) topSpeed = 0f;
                topSpeedWritten = topSpeed;
            }
            return topSpeed;
        }
    }
    public static void NoteSpeed(float kmh)
    {
        if (float.IsNaN(kmh) || kmh <= TopSpeedKmh || kmh > 2000f) return;
        topSpeed = kmh;
        if (topSpeed - topSpeedWritten >= 0.5f) FlushTopSpeed();
    }
    public static void FlushTopSpeed()
    {
        if (topSpeed < 0f || topSpeed <= topSpeedWritten || DebugRun.BlocksSave("ProfileTopSpeed")) return;
        SaveStore.SetString(TopSpeedKey, topSpeed.ToString("0.0", CultureInfo.InvariantCulture)); // ディスクへはほかの定期の保存と一緒に
        topSpeedWritten = topSpeed;
    }
}

// 1回のランの中だけの数え(RunLedger.Run に入る = CONTINUE の中断データに一緒に残り、新しいランでは新しく作られる)
[Serializable]
public class ChallengeRunStats
{
    public int v;               // 0 = この仕組みより前の中断データ(分からない物は達成させない)
    public bool hit;            // このランで一度でも被弾した(ノーダメージの失敗)
    public int hits;            // 被弾の回数
    public int hitsAtBoss = -1; // 今のボス戦が始まった時の被弾の回数(-1 = ボス戦の外)
    public bool autoSeen;       // オートが一度でも ON だった(設定の状態をラン中ずっと見る)
    public bool starterKnown, starterDeck; // 出発の時のデッキが初期デッキのカードだけか
    public int breaks;          // このランで BREAK させた回数
    public float maxKmh;        // このランの最高速度
    public int maxCombo;        // このランの最大コンボ
    public static ChallengeRunStats Fresh() => new ChallengeRunStats { v = 1 };
}

// プロフィールの情報(2026-11-07)。将来のランキング → プレイヤー → プロフィールの表示に使えるよう、画面とは別のデータとして持つ。
//  今は手元のデータから作るだけ(FromLocal)。送る時は ToJson(名前・走る理由の ID・装備バッヂの ID 3つ・主な戦績)
[Serializable]
public class PlayerProfileCard
{
    public int version = 1;
    public string name = "";            // コードネーム(空 = 未設定)
    public string characterId = "";     // 代表キャラ = 全体の BEST を出したキャラ(2026-11-08。記録が無い時だけ選択中のキャラ)
    public int reasonId;                // 走る理由の ID(0 = 未選択)。文ではなく ID(受け取った側の言語で出す)
    public string[] badges = new string[ProfileStore.Slots]; // 装備中のバッヂ(チャレンジの ID、空き = "")
    public int badgeCount;              // 獲得したバッヂの数
    public double bestMeters;           // BEST(全体の最高距離。ホームの BEST と同じ)
    public double totalMeters;          // 累計距離
    public float topSpeedKmh;           // 最高速度
    public bool wasteland100, cave100, sky100; // マップ別 100km 到達
    public bool lastReached, lastCleared;      // ラスダン 到達/クリア
    public double wastelandMeters, caveMeters, skyMeters, lastMeters; // マップ別の自己ベスト距離(2026-11-08。プロフィールはこの実値を出す)

    public static PlayerProfileCard FromLocal()
    {
        var gm = GameManager.Instance;
        var c = new PlayerProfileCard
        {
            name = Codename.Current ?? "",
            characterId = ProfileStore.DisplayCharacterId(gm != null ? gm.SelectedCharacterId ?? "" : SaveStore.GetString("SelectedCharacterId", "")),
            reasonId = ProfileStore.ReasonId,
            badgeCount = ChallengeStore.DoneCount,
            bestMeters = BestOverall(gm),
            totalMeters = ProgressStats.LifetimeDistance,
            topSpeedKmh = ProfileStore.TopSpeedKmh,
            wasteland100 = MapReach(UnlockRules.Wasteland) >= 100000.0,
            cave100 = MapReach(UnlockRules.Cave) >= 100000.0,
            sky100 = MapReach(UnlockRules.Sky) >= 100000.0,
            lastReached = MapReach(BossManager.LastStageId) > 0.0,
            lastCleared = ProgressStats.GameCleared,
            wastelandMeters = MapReach(UnlockRules.Wasteland),
            caveMeters = MapReach(UnlockRules.Cave),
            skyMeters = MapReach(UnlockRules.Sky),
            lastMeters = MapReach(BossManager.LastStageId),
        };
        for (int i = 0; i < ProfileStore.Slots; i++) c.badges[i] = ProfileStore.Slot(i);
        return c;
    }
    // BEST = マップ別 BEST(脱出して確定した記録)の一番遠い物。古い共通の BEST(ガチャの段階の判定用)の方が大きければそれ
    public static double BestOverall(GameManager gm)
    {
        double m = gm != null ? gm.BestDistance : SaveStore.GetFloat("BestDistance", 0f);
        foreach (var st in new[] { UnlockRules.Wasteland, UnlockRules.Cave, UnlockRules.Sky, BossManager.LastStageId }) m = Math.Max(m, GameManager.ReadStageBest(st));
        return m;
    }
    // マップの到達(BEST と1回のランの最高到達の大きい方)
    public static double MapReach(string stageId) => Math.Max(GameManager.ReadStageBest(stageId), UnlockRules.Reach(stageId));

    public string ToJson() => JsonUtility.ToJson(this);
    public static PlayerProfileCard FromJson(string json)
    {
        try
        {
            var c = JsonUtility.FromJson<PlayerProfileCard>(json);
            if (c == null) return null;
            if (c.badges == null || c.badges.Length != ProfileStore.Slots) { var b = new string[ProfileStore.Slots]; for (int i = 0; i < b.Length; i++) b[i] = c.badges != null && i < c.badges.Length ? c.badges[i] : ""; c.badges = b; }
            for (int i = 0; i < c.badges.Length; i++) if (ChallengeDefs.Find(c.badges[i]) == null) c.badges[i] = ""; // 知らない ID は出さない
            if (LocProfile.ReasonText(c.reasonId) == null) c.reasonId = 0;
            return c;
        }
        catch { return null; }
    }
}

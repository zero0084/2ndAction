using System;
using System.Collections.Generic;
using UnityEngine;

// ===== カード長期育成: Mastery / AWAKENED(2026-10-04) =====
// カードの所持Lv(合成Lv 1〜9、CardInventory)とは別のメタ育成値。カードの種類(主能力のカードID)ごとに1つ。
//   Lv1〜9 → Lv9 MAX → Mastery ★1〜5 → ★5 = AWAKENED(永久)
// ・Lv9 MAX になった後の同じカードは、合成で Mastery Progress になる(素材のLv = 何枚分か。Lv1 = +1)。
// ・昇格で余った分は次の★へ繰り越す。★5 の後に余った分は overflow に保管する(捨てない。使い道は今後)。
// ・Mastery/AWAKENED は通常の性能(攻撃/速度/EXP…)を一切変えない。ラン中の能力のLv9上限にも数えない。
// ・既存の所持Lv/合成Lvは下げない・作り直さない。旧セーブ(このキーが無い)は全カード ★0 から。
// 保存: PlayerPrefs "CardMasteryV1"(JSON)。Lv/★/進み/保管/AWAKENED/Lv9到達の記録を持つ。
public static class CardMastery
{
    public const string SaveKey = "CardMasteryV1";
    public const int MaxStars = 5;

    [Serializable]
    public class Entry
    {
        public string cardId;       // 主能力のカードID(素のID)
        public int level;           // ★の数 0〜5
        public int progress;        // 次の★までの進み
        public int overflow;        // ★5 の後に余った分(保管)
        public bool awakened;       // ★5 に一度でも届いたら true のまま(閾値を後で変えても外れない)
        public bool maxReached;     // Lv9 MAX に一度でも届いたか(所持から消えても記録は残る)
        public int totalGained;     // これまでに入った量の合計(確認用)
    }

    [Serializable]
    public class SaveWrapper
    {
        public int version = 1;
        public List<Entry> cards = new List<Entry>();
    }

    static Dictionary<string, Entry> map;

    // ===================================================================== 読み書き
    public static void ReloadFromPrefs() { map = null; }

    static void EnsureLoaded()
    {
        if (map != null) return;
        map = new Dictionary<string, Entry>();
        string json = PlayerPrefs.GetString(SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            SaveWrapper w = null;
            try { w = JsonUtility.FromJson<SaveWrapper>(json); }
            catch (Exception e) { Debug.LogWarning("[Mastery] could not parse the save, starting at ★0: " + e.Message); }
            if (w != null && w.cards != null)
                foreach (var e in w.cards) if (e != null && !string.IsNullOrEmpty(e.cardId)) { Sanitize(e); map[e.cardId] = e; }
        }
        // 所持している Lv9 は「Lv9 到達済み」としてメモリ上で記録する(読むだけで保存は書かない。次に保存する時に一緒に残る)
        SyncMaxFromInventoryInMemory();
    }

    static void Sanitize(Entry e)
    {
        e.level = Mathf.Clamp(e.level, 0, MaxStars);
        e.progress = Mathf.Max(0, e.progress);
        e.overflow = Mathf.Max(0, e.overflow);
        if (e.level >= MaxStars) { e.awakened = true; e.progress = 0; }
        if (e.awakened) e.level = Mathf.Max(e.level, MaxStars);
    }

    public static string ExportJson()
    {
        EnsureLoaded();
        var w = new SaveWrapper();
        foreach (var kv in map) w.cards.Add(kv.Value);
        w.cards.Sort((a, b) => string.CompareOrdinal(a.cardId, b.cardId));
        return JsonUtility.ToJson(w);
    }

    // 所持カードと一緒に保存する時(合成の確定)は、両方書いてから PlayerPrefs.Save() を1回だけ
    public static void WriteWithoutFlush()
    {
        if (map == null) return;
        if (DebugRun.BlocksSave("CardMastery")) return;
        PlayerPrefs.SetString(SaveKey, ExportJson());
    }

    public static void Save() { WriteWithoutFlush(); PlayerPrefs.Save(); }

    // 開発用(Mastery Test): JSON から読み直して、今の状態と同じになるか
    public static bool RoundTripEquals(out string detail)
    {
        string a = ExportJson();
        var w = JsonUtility.FromJson<SaveWrapper>(a);
        var copy = new Dictionary<string, Entry>();
        foreach (var e in w.cards) copy[e.cardId] = e;
        var keep = map;
        map = copy;
        string b = ExportJson();
        map = keep;
        detail = a == b ? $"{w.cards.Count} cards" : "differs";
        return a == b;
    }

    // ===================================================================== 参照
    // v2キー(合成カード)でも素のIDでも、主能力のカードIDへ
    public static string BaseIdOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (!CardVariant.IsVariantKey(key)) return key;
        string[] p = key.Split('|');
        return p.Length > 1 ? p[1] : key;
    }

    static Entry Get(string key) { EnsureLoaded(); return map.TryGetValue(BaseIdOf(key) ?? "", out var e) ? e : null; }
    static Entry GetOrAdd(string key)
    {
        EnsureLoaded();
        string id = BaseIdOf(key);
        if (!map.TryGetValue(id, out var e)) { e = new Entry { cardId = id }; map[id] = e; }
        return e;
    }

    public static int MasteryLevel(string key) { var e = Get(key); return e != null ? e.level : 0; }
    public static int MasteryProgress(string key) { var e = Get(key); return e != null ? e.progress : 0; }
    public static int Overflow(string key) { var e = Get(key); return e != null ? e.overflow : 0; }
    public static bool IsAwakened(string key) { var e = Get(key); return e != null && e.awakened; }
    // Lv9 MAX に届いているか(今の所持 or これまでの記録)
    public static bool IsMaxReached(string key)
    {
        var e = Get(key);
        if (e != null && e.maxReached) return true;
        return InventoryHasMax(BaseIdOf(key));
    }
    // 次の★までに必要な量(★5 なら 0)
    public static int NeedForNext(string key) { int lv = MasteryLevel(key); return lv >= MaxStars ? 0 : MasteryTuning.Need(lv); }
    public static string Stars(string key) => StarsFor(MasteryLevel(key));
    // カードの上の小さな帯用(リッチテキスト): 埋まった★は金、まだの★は暗い色
    public static string StarsRich(int lv) { lv = Mathf.Clamp(lv, 0, MaxStars); return (lv > 0 ? "<color=#ffd34d>" + new string('★', lv) + "</color>" : "") + (lv < MaxStars ? "<color=#4a4458>" + new string('★', MaxStars - lv) + "</color>" : ""); }
    public static string StarsFor(int lv) => new string('★', Mathf.Clamp(lv, 0, MaxStars)) + new string('☆', MaxStars - Mathf.Clamp(lv, 0, MaxStars));

    // ===================================================================== 総数(カードの数は CardDatabase から。固定値を使わない)
    public static int TotalCards
    {
        get
        {
            int n = 0;
            foreach (var c in CardDatabase.AllCards) if (c != null && !CardVariant.IsVariantKey(c.cardId)) n++;
            return n;
        }
    }
    public static int MaxCount
    {
        get { int n = 0; foreach (var c in CardDatabase.AllCards) if (c != null && !CardVariant.IsVariantKey(c.cardId) && IsMaxReached(c.cardId)) n++; return n; }
    }
    public static int AwakenedCount
    {
        get { int n = 0; foreach (var c in CardDatabase.AllCards) if (c != null && !CardVariant.IsVariantKey(c.cardId) && IsAwakened(c.cardId)) n++; return n; }
    }
    public static bool AllAwakened { get { int t = TotalCards; return t > 0 && AwakenedCount >= t; } }
    // 節目(将来の称号/装飾の解放): 例 10 / 25 / 50 / 75 / 99 / 全カード
    public static bool ReachedAwakenedMilestone(int count) => AwakenedCount >= count;

    // ===================================================================== 進める
    public struct Gain
    {
        public string cardId; public int amount;
        public int levelBefore, progressBefore, levelAfter, progressAfter;
        public int overflowAdded; public bool awakenedNow;
        public int StarsGained => levelAfter - levelBefore;
    }

    // n 枚分を足す(昇格で余った分は次の★へ、★5 の後は overflow へ)。保存はしない(呼び出し側)
    public static Gain AddProgressInMemory(string key, int n, string source)
    {
        var e = GetOrAdd(key);
        var g = new Gain { cardId = e.cardId, amount = Mathf.Max(0, n), levelBefore = e.level, progressBefore = e.progress };
        int left = Mathf.Max(0, n);
        e.totalGained += left;
        while (left > 0 && e.level < MaxStars)
        {
            int need = Mathf.Max(1, MasteryTuning.Need(e.level));
            int take = Mathf.Min(left, need - e.progress);
            if (take <= 0) { e.level++; e.progress = 0; continue; } // 閾値を下げた後などで既に足りている
            e.progress += take; left -= take;
            if (e.progress >= need) { e.level++; e.progress = 0; }
        }
        if (e.level >= MaxStars)
        {
            e.level = MaxStars; e.progress = 0;
            if (!e.awakened) { e.awakened = true; g.awakenedNow = true; }
            if (left > 0) { e.overflow += left; g.overflowAdded = left; }
        }
        g.levelAfter = e.level; g.progressAfter = e.progress;
        Debug.Log($"[Mastery] {e.cardId} +{n} ({source}): ★{g.levelBefore} {g.progressBefore} -> ★{g.levelAfter} {g.progressAfter}{(g.awakenedNow ? " AWAKENED" : "")}{(g.overflowAdded > 0 ? $" overflow+{g.overflowAdded}" : "")}");
        return g;
    }

    public static Gain AddProgress(string key, int n, string source)
    {
        var g = AddProgressInMemory(key, n, source);
        Save();
        return g;
    }

    // 合成の結果を見る前に: n 枚分を足したらどうなるか(何も変えない)
    public static Gain Simulate(string key, int n)
    {
        var e = Get(key);
        int lv = e != null ? e.level : 0, pr = e != null ? e.progress : 0;
        var g = new Gain { cardId = BaseIdOf(key), amount = n, levelBefore = lv, progressBefore = pr };
        int left = Mathf.Max(0, n);
        while (left > 0 && lv < MaxStars)
        {
            int need = Mathf.Max(1, MasteryTuning.Need(lv));
            int take = Mathf.Min(left, need - pr);
            if (take <= 0) { lv++; pr = 0; continue; }
            pr += take; left -= take;
            if (pr >= need) { lv++; pr = 0; }
        }
        if (lv >= MaxStars) { lv = MaxStars; pr = 0; g.awakenedNow = e == null || !e.awakened; g.overflowAdded = left; }
        g.levelAfter = lv; g.progressAfter = pr;
        return g;
    }

    public static void MarkMaxReachedInMemory(string key)
    {
        var e = GetOrAdd(key);
        if (!e.maxReached) { e.maxReached = true; Debug.Log($"[Mastery] {e.cardId} reached Lv9 MAX (recorded)"); }
    }

    static bool InventoryHasMax(string baseId)
    {
        foreach (var s in CardInventory.Stacks)
            if (s.count > 0 && s.level >= CardVariant.MaxLevel && BaseIdOf(s.cardId) == baseId) return true;
        return false;
    }

    // 所持している Lv9 を記録へ。記録を増やした数
    static int SyncMaxFromInventoryInMemory()
    {
        int n = 0;
        foreach (var s in CardInventory.Stacks)
        {
            if (s.count <= 0 || s.level < CardVariant.MaxLevel) continue;
            string id = BaseIdOf(s.cardId);
            if (!map.TryGetValue(id, out var e)) { e = new Entry { cardId = id }; map[id] = e; }
            if (!e.maxReached) { e.maxReached = true; n++; }
        }
        return n;
    }

    public static int SyncMaxFromInventory()
    {
        EnsureLoaded();
        int n = SyncMaxFromInventoryInMemory();
        if (n > 0) Save();
        return n;
    }

    // 開発用(Mastery Test): 1枚分の育成を消す(テストの中だけ。DEBUG RUN で保存は止まっている)
    public static void DebugResetCard(string key)
    {
        EnsureLoaded();
        map.Remove(BaseIdOf(key));
        SyncMaxFromInventoryInMemory();
    }

    // 旧セーブの移行(SaveSystem 2→3): 所持カードの JSON だけを見て Lv9 を記録する(CardDatabase を使わない)。Mastery は 0 から
    public static int MigrateMarkMaxFromInventoryJson()
    {
        if (PlayerPrefs.HasKey(SaveKey)) return 0;
        var w = new SaveWrapper();
        var seen = new HashSet<string>();
        string json = PlayerPrefs.GetString(CardInventory.SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            var inv = JsonUtility.FromJson<CardInventory.SaveWrapper>(json);
            if (inv != null && inv.stacks != null)
                foreach (var s in inv.stacks)
                {
                    if (s == null || s.count <= 0 || s.level < CardVariant.MaxLevel) continue;
                    string id = BaseIdOf(s.cardId);
                    if (seen.Add(id)) w.cards.Add(new Entry { cardId = id, maxReached = true });
                }
        }
        // 記録する Lv9 が無ければキーを作らない(キーが無い = 全カード ★0。既存の値を増やさない)
        if (w.cards.Count > 0) PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(w));
        map = null;
        return w.cards.Count;
    }
}

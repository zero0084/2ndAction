using System;
using System.Collections.Generic;
using UnityEngine;

// デッキのプリセット(2026-10-31、マスター依頼): 最大 8 セットのデッキを保存して切り替える。
//  ・使用中のデッキの中身は今までどおり GameManager.deckCards / "DeckCardIds"(マルチ・引き継ぎ・試験・旧版と互換)。
//    このクラスは 8 セットの名前と中身(DeckPresetsV1)と、使用中の番号(DeckPresetActive)を持つ。
//    使用中のプリセットの中身は GameManager.SaveDeck のたびに同じ保存で書く(片方だけ書かれることがない)。
//  ・初回(DeckPresetsV1 が無い): 今のデッキをプリセット 1 へ、2〜8 は空。名前は空欄(画面では「DECK n」を表示)。
//  ・読み込みでは DeckCardIds を使用中のプリセットの正とする(引き継ぎで古いファイルを読んだ時など、食い違ったら DeckCardIds に合わせる)。
//  ・同じ所持カードを別のプリセットで使える。デッキの枚数の確認(AddToDeck)は使用中のデッキだけ。
//    合成/変換からは「どれかのプリセットに入っているカード」を 1 枚ぶん守る(InInactivePreset。キャラカード枠と同じ考え方)。
//  ・場面の記録(DeckPresetMemory): 「モード|マップ」と「モード」ごとに前回のプリセットの番号。ソロはマップを選んだ時(そのマップの記録)、
//    マルチは部屋に入った時(モードの記録)に、使用中をその番号へ切り替えるだけ(中身は書き換えない)。記録はランを始めた時(GameManager.ApplyGameStart)。
//  ・将来のキャラ別のプリセット用に、各プリセットに character の欄を持つ(今は使わない)。
public static class DeckPresets
{
    public const int Count = 8;
    public const int NameMax = 16;
    public const string PresetsKey = "DeckPresetsV1";
    public const string ActiveKey = "DeckPresetActive";
    public const string MemoryKey = "DeckPresetMemory";

    [Serializable] public class Preset
    {
        public string name = "";
        public List<string> cards = new List<string>();
        public string character = ""; // 将来: キャラ別の割り当て(今は未使用)
    }
    [Serializable] public class SaveWrapper
    {
        public int v = 1;
        public List<Preset> presets = new List<Preset>();
    }
    [Serializable] class MemoryEntry { public string k = ""; public int i; }
    [Serializable] class MemoryWrapper { public List<MemoryEntry> m = new List<MemoryEntry>(); }

    static SaveWrapper data;
    static int active;
    public static bool Loaded => data != null;
    public static int Active => active;
    public static int MigratedFromOldSave { get; private set; } // 確認用: 1 = この起動で旧セーブ(プリセット無し)から作った

    public static Preset Get(int i)
    {
        if (data == null || i < 0 || i >= data.presets.Count) return null;
        return data.presets[i];
    }

    // 画面に出す名前(空欄なら「DECK n」)
    public static string DisplayName(int i)
    {
        var p = Get(i);
        return p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.T("デッキ") + " " + (i + 1); // 「デッキ」は訳がある
    }

    // GameManager.LoadDeck から(デッキの読み込みの最後)。activeDeck = 使用中のデッキ(DeckCardIds の中身)
    public static void Load(IReadOnlyList<string> activeDeck)
    {
        MigratedFromOldSave = 0;
        memory = null; // 引き継ぎの読み込み/初期化の後にも読み直す
        SaveWrapper w = null;
        string raw = SaveStore.GetString(PresetsKey, "");
        if (!string.IsNullOrEmpty(raw))
        {
            try { w = JsonUtility.FromJson<SaveWrapper>(raw); }
            catch (Exception e) { Debug.LogWarning("[DeckPreset] could not read the saved presets (" + e.Message + ") - rebuilt from the current deck"); w = null; }
        }
        bool fresh = w == null || w.presets == null;
        if (fresh) w = new SaveWrapper();
        while (w.presets.Count < Count) w.presets.Add(new Preset());
        if (w.presets.Count > Count) w.presets.RemoveRange(Count, w.presets.Count - Count);
        foreach (var p in w.presets) Sanitize(p);
        active = fresh ? 0 : Mathf.Clamp(SaveStore.GetInt(ActiveKey, 0), 0, Count - 1);
        // 使用中のプリセットの中身は DeckCardIds が正
        w.presets[active].cards = new List<string>(activeDeck);
        data = w;
        if (fresh)
        {
            MigratedFromOldSave = 1;
            Debug.Log($"[DeckPreset] created 8 presets: preset 1 = the current deck ({activeDeck.Count} cards), 2-8 empty");
            if (!DebugRun.BlocksSave(PresetsKey)) { Write(); SaveStore.Save(); }
        }
    }

    static void Sanitize(Preset p)
    {
        if (p.cards == null) p.cards = new List<string>();
        p.cards.RemoveAll(string.IsNullOrEmpty);
        if (p.cards.Count > GameManager.DeckCapacity) p.cards.RemoveRange(GameManager.DeckCapacity, p.cards.Count - GameManager.DeckCapacity);
        p.name = CleanName(p.name);
        if (p.character == null) p.character = "";
    }

    // 名前: 前後の空白を除き、制御文字を除き、最大 NameMax 文字(サロゲートペアを途中で切らない)
    public static string CleanName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
        string t = sb.ToString().Trim();
        var info = new System.Globalization.StringInfo(t);
        if (info.LengthInTextElements > NameMax) t = info.SubstringByTextElements(0, NameMax).TrimEnd();
        return t;
    }

    static void Write()
    {
        SaveStore.SetString(PresetsKey, JsonUtility.ToJson(data));
        SaveStore.SetInt(ActiveKey, active);
    }

    // GameManager.SaveDeck から(DeckCardIds と同じ保存で書く。SaveStore.Save は呼び出し側)
    public static void SyncActive(IReadOnlyList<string> deck)
    {
        if (data == null) return;
        data.presets[active].cards = new List<string>(deck);
        Write();
    }

    // 使用中の番号だけを変える(中身の入れ替えは GameManager.ActivatePreset)
    public static void SetActiveIndex(int i)
    {
        if (data == null) return;
        active = Mathf.Clamp(i, 0, Count - 1);
    }

    public static bool Rename(int i, string name)
    {
        var p = Get(i);
        if (p == null || DebugRun.BlocksSave(PresetsKey)) return false;
        p.name = CleanName(name);
        Write(); SaveStore.Save();
        return true;
    }

    // from の中身(と名前が空でない時は名前)を to へ写す。to が使用中なら GameManager 側でデッキを入れ替える
    public static bool Copy(int from, int to)
    {
        var a = Get(from); var b = Get(to);
        if (a == null || b == null || from == to || DebugRun.BlocksSave(PresetsKey)) return false;
        b.cards = new List<string>(a.cards);
        b.name = a.name;
        Write(); SaveStore.Save();
        return true;
    }

    // 中身と名前を空にする(使用中なら GameManager 側でデッキも空にする)
    public static bool Clear(int i)
    {
        var p = Get(i);
        if (p == null || DebugRun.BlocksSave(PresetsKey)) return false;
        p.cards.Clear();
        p.name = "";
        Write(); SaveStore.Save();
        return true;
    }

    // 使用中でないプリセットに、このキー(所持カードの cardId / v2 キー)が入っているか(合成/変換から 1 枚守る)
    public static bool InInactivePreset(string key)
    {
        if (data == null || string.IsNullOrEmpty(key)) return false;
        for (int i = 0; i < data.presets.Count; i++)
            if (i != active && data.presets[i].cards.Contains(key)) return true;
        return false;
    }

    // ---- 場面の記録(モード: "solo" / "local" / "online"、マップ: StageDefinition の id)
    public static string ModeNow()
    {
        if (!NetSession.IsActive) return "solo";
        return NetSession.Connection == ConnectionType.Online ? "online" : "local";
    }

    static MemoryWrapper memory;
    static MemoryWrapper Memory
    {
        get
        {
            if (memory != null) return memory;
            string raw = SaveStore.GetString(MemoryKey, "");
            try { memory = string.IsNullOrEmpty(raw) ? null : JsonUtility.FromJson<MemoryWrapper>(raw); } catch { memory = null; }
            if (memory == null || memory.m == null) memory = new MemoryWrapper();
            return memory;
        }
    }
    public static void ResetCache() { memory = null; data = null; }

    static int Find(string key)
    {
        foreach (var e in Memory.m) if (e != null && e.k == key) return e.i;
        return -1;
    }
    static void Set(string key, int i)
    {
        foreach (var e in Memory.m) if (e != null && e.k == key) { e.i = i; return; }
        Memory.m.Add(new MemoryEntry { k = key, i = i });
    }

    // 前回のプリセット。マップを指定した時は「モード|マップ」だけ(記録の無いマップでは切り替えない)、
    // マップが空の時(マルチの部屋: マップは HOST が決める)は「モード」の記録。無ければ -1
    public static int Remembered(string mode, string stage)
    {
        int i = string.IsNullOrEmpty(stage) ? Find(mode) : Find(mode + "|" + stage);
        return i >= 0 && i < Count ? i : -1;
    }

    public static void Remember(string mode, string stage)
    {
        if (data == null || DebugRun.BlocksSave(MemoryKey)) return;
        if (!string.IsNullOrEmpty(stage)) Set(mode + "|" + stage, active);
        Set(mode, active);
        SaveStore.SetString(MemoryKey, JsonUtility.ToJson(Memory));
        SaveStore.Save();
    }
}

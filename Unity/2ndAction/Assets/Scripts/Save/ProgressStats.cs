using UnityEngine;

// ゲーム全体の進行(2026-10-01): 累計走行距離 / 死神三姉妹との遭遇 / ラスダン(LAST CORRIDOR)の解放。
//  累計走行距離 … ランで距離が伸びた分だけ足す(GameManager.ReportDistance)。メモリ上で足し、100mごと/ラン終了/帰還/
//                 アプリの一時停止・終了で書き込む(毎フレームは書かない)。デバッグの距離ワープは含めない。
//  三姉妹の遭遇 … 「倒した」ではなく「正式に出現した」で false→true。保存はその場で即座に(その後死んでも残る)。
//  ラスダン解放 … 累計 >= UnlockDistance かつ 3人とも遭遇済み で true。一度 true になったら戻さない(毎回の再計算でロックしない)。
//  開発版では Dev.FinalDungeonAlwaysOpen(既定1)でラスダンを常に選べる。リリース版は解放フラグだけを見る。
public static class ProgressStats
{
    public const double UnlockDistance = 1000000.0;
    public static readonly ReaperSister[] Sisters = { ReaperSister.Eldest, ReaperSister.Second, ReaperSister.Youngest };

    static bool loaded;
    static double lifetime;
    static double lastFlushed;
    static bool dirty;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        lifetime = ReadDouble(SaveKeys.LifetimeDistance);
        lastFlushed = lifetime;
    }

    public static void Reload() { loaded = false; dirty = false; seenBosses = null; SprintRecords.Reload(); }

    // ---- 会ったボス(2026-10-05): ラスダンの節目のボスは、製品版ではこの中から選ぶ(LastDungeonBossTuning.releasePreferSeen) ----
    static System.Collections.Generic.HashSet<string> seenBosses;
    static System.Collections.Generic.HashSet<string> Seen
    {
        get
        {
            if (seenBosses == null)
            {
                seenBosses = new System.Collections.Generic.HashSet<string>();
                foreach (var k in SaveStore.GetString(SaveKeys.BossSeen, "").Split(',')) if (!string.IsNullOrEmpty(k)) seenBosses.Add(k);
            }
            return seenBosses;
        }
    }
    public static bool HasSeenBoss(string key) => !string.IsNullOrEmpty(key) && Seen.Contains(key);
    public static int SeenBossCount => Seen.Count;
    // 戦闘が始まった時に呼ぶ(何度呼んでもよい。初めての時だけ保存)
    public static void MarkBossSeen(string key)
    {
        if (string.IsNullOrEmpty(key) || Seen.Contains(key)) return;
        if (DebugRun.BlocksSave("BossSeen_" + key)) return;
        Seen.Add(key);
        SaveStore.SetString(SaveKeys.BossSeen, string.Join(",", Seen));
        SaveStore.Save();
        Debug.Log($"[Progress] boss seen: {key} ({Seen.Count})");
    }

    public static double LifetimeDistance { get { Load(); return lifetime; } }

    // ランで距離が伸びた分。100mを越えるたびに書き込む。
    public static void AddRunDistance(double meters)
    {
        if (meters <= 0.0 || double.IsNaN(meters) || double.IsInfinity(meters)) return;
        if (DebugRun.WritesBlocked) return; // 記録対象外のラン/闘技場(ワープした距離も含めて累計へ足さない)
        Load();
        lifetime += meters;
        dirty = true;
        if (lifetime - lastFlushed >= 100.0) Flush(true);
    }

    // 2026-10-08(仕様変更): 累計は成功したランの実走分だけ。RunLedger.CommitSuccess から一度だけ足して保存する
    public static void AddCommittedDistance(double meters)
    {
        if (meters <= 0.0 || double.IsNaN(meters) || double.IsInfinity(meters)) return;
        if (DebugRun.WritesBlocked) return;
        Load();
        lifetime += meters;
        dirty = true;
        Flush(false);
    }

    // 書き込む(save=true で SaveStore.Save まで)。ラン終了/帰還/一時停止/終了から呼ぶ。
    public static void Flush(bool save)
    {
        if (DebugRun.BlocksSave("ProgressStats.Flush")) return;
        Load();
        if (dirty)
        {
            SaveStore.SetString(SaveKeys.LifetimeDistance, lifetime.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            lastFlushed = lifetime;
            dirty = false;
        }
        UnlockRules.EvaluateLastDungeon(); // 2026-10-07: 通常3マップすべてで死神に遭遇 + 累計1,000,000m
        if (save) SaveStore.Save();
    }

    public static bool HasMet(ReaperSister s) => SaveStore.GetInt(SaveKeys.ReaperMetPrefix + s, 0) != 0;
    public static bool MetAllSisters { get { foreach (var s in Sisters) if (!HasMet(s)) return false; return true; } }

    // 正式に出現した時に呼ぶ(何度呼んでもよい)
    public static void MarkReaperMet(ReaperSister s)
    {
        if (HasMet(s)) return;
        if (DebugRun.BlocksSave("ReaperMet_" + s)) return;
        SaveStore.SetInt(SaveKeys.ReaperMetPrefix + s, 1);
        Debug.Log($"[Progress] met reaper sister: {s}");
        Flush(false);
        SaveStore.Save();
    }

    public static bool FinalDungeonUnlocked => SaveStore.GetInt(SaveKeys.FinalDungeonUnlocked, 0) != 0;
    public static void SetFinalDungeonUnlocked() { if (DebugRun.BlocksSave("FinalDungeonUnlocked")) return; SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, 1); SaveStore.Save(); }

    // 解放の判定(一度解放したら戻さない)。2026-10-07: 条件は UnlockRules(通常3マップすべてで死神に遭遇 + 累計1,000,000m)
    public static bool EvaluateFinalDungeon(bool save) => UnlockRules.EvaluateLastDungeon();

    // ステージ選択でラスダンを出してよいか
    public static bool FinalDungeonAvailable
    {
        get
        {
            if (FinalDungeonUnlocked) return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SaveStore.GetInt(SaveKeys.DevFinalDungeonAlwaysOpen, SaveProfile.IsTest ? 0 : 1) != 0) return true;
#endif
            return false;
        }
    }

    public static double ReadDouble(string key)
    {
        string s = SaveStore.GetString(key, "");
        if (string.IsNullOrEmpty(s)) return 0.0;
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return 0.0;
        if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0) return 0.0;
        return v;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ===== 開発用 =====
    public static void DevSetLifetime(double meters)
    {
        Load();
        lifetime = System.Math.Max(0.0, meters);
        dirty = true;
        Flush(true);
    }
    public static void DevSetMet(ReaperSister s, bool on)
    {
        SaveStore.SetInt(SaveKeys.ReaperMetPrefix + s, on ? 1 : 0);
        Flush(true);
    }
    public static void DevSetFinalDungeonUnlocked(bool on)
    {
        SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, on ? 1 : 0);
        SaveStore.Save();
    }
    public static bool DevAlwaysOpen
    {
        get => SaveStore.GetInt(SaveKeys.DevFinalDungeonAlwaysOpen, SaveProfile.IsTest ? 0 : 1) != 0;
        set { SaveStore.SetInt(SaveKeys.DevFinalDungeonAlwaysOpen, value ? 1 : 0); SaveStore.Save(); }
    }
#endif
}

// 製品版の新規ユーザーの初期状態(2026-10-01)。初期値はここだけで決める。
//  ・キャラクター: 現在の仕様では全キャラを最初から使える(キャラの解放の仕組み自体が無い)。初期選択は一覧の先頭。
//  ・カード: 解放済み(距離0で使える)カードから先頭のデッキ枠ぶんを各1枚 Lv1 で所持し、そのままデッキにする
//            (GameManager.LoadDeck がデッキの保存が無い時にここを呼ぶ)。
//  ・MILE: 0 / マップ別BEST: 無し(0) / 累計走行距離: 0 / 距離で解放する物: 無し / 中断中のラン: 無し
//  ・死神三姉妹の遭遇: すべて false / ラスダン: 未解放 / ステージの初期選択: 荒野街道
public static class DefaultSave
{
    public const int StartingMile = 0;
    public const string StartingStageId = "wasteland_road";

    public static string StartingCharacterId()
    {
        var all = CharacterDatabase.AllCharacters;
        return all != null && all.Count > 0 ? all[0].characterId : null;
    }

    // 初期デッキ(=初期所持カード)。解放済みのカードの先頭から capacity 枚
    public static System.Collections.Generic.List<string> StartingDeck(int capacity)
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (CardDefinition card in CardDatabase.UnlockedCards)
        {
            if (list.Count >= capacity) break;
            list.Add(card.cardId);
        }
        return list;
    }

    // 進行を新規の状態にする(設定には触れない)。デッキ/所持カードは次に GameManager が読み込む時に StartingDeck から作られる。
    public static void WriteNewProgress()
    {
        foreach (var e in SaveKeys.Expanded())
            if (e.cat == SaveCategory.Progress) SaveStore.DeleteKey(e.key);
        SaveStore.SetInt("TotalOwnedMile", StartingMile);
        SaveStore.SetString(SaveKeys.LifetimeDistance, "0");
        foreach (var s in ProgressStats.Sisters) SaveStore.SetInt(SaveKeys.ReaperMetPrefix + s, 0);
        SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, 0);
        SaveStore.SetString("SelectedStageId", StartingStageId);
        SaveStore.SetInt("CardDataFormat", CardDataMigration.CurrentFormat); // 新規は最初から新形式(旧形式の変換は不要)
        ProgressStats.Reload();
    }
}

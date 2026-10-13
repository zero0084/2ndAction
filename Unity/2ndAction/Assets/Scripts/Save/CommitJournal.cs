using System;
using UnityEngine;

// 成功の確定の「書きかけ」を復旧するための控え(2026-10-13 第3弾)。
//  脱出成功の確定(RunLedger.CommitSuccess + MILE + 全体の BEST + 中断データの削除)は、保存を何回かに分けて書いていたため、
//  途中でアプリが落ちると「確定の印は残ったが MILE が入っていない」等の欠けが起きた(印があるので二度と確定し直せない)。
//  ・確定の前に、このランで書く物の「書く前の値/書いた後の値」をこの控えへ書いて保存する(1回)。
//  ・確定の書き込みは SaveStore.BeginBatch〜EndBatch の間で行い、控えの削除と一緒に1回の保存で書き切る。
//  ・起動時(SaveSystem.Boot の最後)に控えが残っていたら、書きかけの確定をやり直す。
//    どの書き込みも「まだ書いていない時だけ書く」(MILE/累計距離は書く前の値と一致する時だけ書いた後の値にする、
//    BEST は大きい方、解放/投稿待ちは同じ物を二重に足さない)ので、どこで落ちても欠けも二重もない。
//  ・控えのある成功したランは、中断データ(CONTINUE)からは再開できない(確定の印と中断データの削除もやり直すため)。
public static class CommitJournal
{
    public const string Key = "PendingCommitV1";

    [Serializable]
    public class Data
    {
        public int v = 1;
        public string runId = "";
        public bool commit;                // 正式な記録として確定する(DEBUG/確定済みでない)
        public string stageId = "";
        public double reached, walked;
        public bool lifeAdd; public double lifeBefore, lifeAfter;
        public int mileBefore, mileAfter;
        public bool record; public float bestDistance, bestTime;
        public bool rankingEligible;
        public bool clearCheckpoint = true; // マルチのランはシングルの中断データ(CONTINUE)を消さない
        public RunLedger.Run run;
        public long utc;
        public int gen = -1; // 書いた時のリリース世代(2026-10-13。-1 = この項目より前の控え = 世代0)
    }

    public static bool Pending => SaveStore.HasKey(Key) && !string.IsNullOrEmpty(SaveStore.GetString(Key, ""));
    public static int Recovered { get; private set; }      // 確認用: 起動時にやり直した回数
    public static string LastRecovery { get; private set; } = "";

    // 確定の前に書く(書いて保存してから確定を始める)
    public static void Write(Data d)
    {
        d.utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        d.gen = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0);
        SaveStore.SetString(Key, JsonUtility.ToJson(d));
        SaveStore.Save();
        Debug.Log($"[CommitJournal] written run={Short(d.runId)} commit={d.commit} mile {d.mileBefore}->{d.mileAfter} reached={d.reached:F0}");
    }

    // 確定が終わった(EndBatch の保存と一緒に消える)
    public static void Clear() { SaveStore.DeleteKey(Key); }

    // 起動時: 書きかけの確定が残っていたらやり直す
    public static void RecoverIfPending()
    {
        if (!Pending) return;
        Data d = null;
        string raw = SaveStore.GetString(Key, "");
        try { d = JsonUtility.FromJson<Data>(raw); }
        catch (Exception e) { Debug.LogWarning("[CommitJournal] unreadable journal, discarded: " + e.Message); }
        if (d == null) { SaveSystem.KeepForInvestigation("discarded_journal_unreadable", raw); Clear(); SaveStore.Save(); return; }
        // 2026-10-13: 今のデータより前の世代(製品版の初期化より前 = 開発版)の控えは反映しない(初期化した進行へ BEST/解放を戻さない)
        int gen = SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0);
        if (Math.Max(d.gen, 0) < gen)
        {
            Debug.LogWarning($"[CommitJournal] a journal from release generation {Math.Max(d.gen, 0)} was found in generation {gen} data - not applied (kept in a file)");
            SaveSystem.KeepForInvestigation("discarded_journal_gen" + Math.Max(d.gen, 0), raw);
            Clear(); SaveStore.Save();
            LastRecovery = "discarded (older generation)";
            return;
        }
        var log = new System.Text.StringBuilder();
        if (d.commit)
        {
            if (!RunLedger.IsCommitted(d.runId)) { RunLedger.MarkCommitted(d.runId); log.Append("commit-mark "); }
            if (d.reached > GameManager.ReadStageBest(d.stageId)) { GameManager.WriteStageBest(d.stageId, d.reached); log.Append("stage-best "); }
            if (d.lifeAdd)
            {
                double cur = ProgressStats.LifetimeDistance;
                if (Math.Abs(cur - d.lifeBefore) < 0.5) { ProgressStats.SetLifetimeForRecovery(d.lifeAfter); log.Append("lifetime "); }
                else if (Math.Abs(cur - d.lifeAfter) >= 0.5) log.Append($"lifetime-mismatch({cur:F0}) ");
            }
            var res = new RunLedger.CommitResult();
            UnlockRules.CommitRun(d.stageId, d.reached, res);
            int other = UnlockManager.CommitStored(d.reached);
            if (res.unlockedStages.Count + res.unlockedChars.Count + other > 0) log.Append($"unlocks+{res.unlockedStages.Count + res.unlockedChars.Count + other} ");
            if (d.rankingEligible && d.run != null) { Leaderboard.OnSuccessCommitted(d.run); log.Append("ranking "); }
            if (d.record)
            {
                if (d.bestDistance > SaveStore.GetFloat(GameManager.BestDistanceKey, 0f)) { SaveStore.SetFloat(GameManager.BestDistanceKey, d.bestDistance); log.Append("best "); }
                if (d.bestTime > SaveStore.GetFloat(GameManager.BestTimeKey, 0f)) { SaveStore.SetFloat(GameManager.BestTimeKey, d.bestTime); log.Append("best-time "); }
            }
        }
        int mile = SaveStore.GetInt(GameManager.TotalMileKey, 0);
        if (mile == d.mileBefore && d.mileAfter != d.mileBefore) { SaveStore.SetInt(GameManager.TotalMileKey, d.mileAfter); log.Append($"mile {d.mileBefore}->{d.mileAfter} "); }
        else if (mile != d.mileAfter) log.Append($"mile-mismatch(now {mile}, journal {d.mileBefore}->{d.mileAfter}) ");
        if (d.clearCheckpoint) RunCheckpoint.Clear();
        Clear();
        SaveStore.Save();
        ProgressStats.Reload(); RunCheckpoint.Reload();
        Recovered++;
        LastRecovery = log.ToString().Trim();
        Debug.Log($"[CommitJournal] recovered an unfinished success commit run={Short(d.runId)}: {LastRecovery}");
        if (d.mileAfter > d.mileBefore || d.commit) NoticeQueue.Toast(Loc.F("前回のランの結果を反映しました(MILE +{0})", d.mileAfter - d.mileBefore));
    }

    static string Short(string id) => string.IsNullOrEmpty(id) ? "-" : id.Substring(0, Math.Min(8, id.Length));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(開発版): 確定の途中のこの地点で、保存をせずにプロセスを強制終了する(アプリが落ちたのと同じ)。
    // DevCrashFlush = true なら、落ちる前にそこまでの値を書き出す(書いた端から残る保存の仕組みの機種を再現)
    public static string DevCrashAt = "";
    public static bool DevCrashFlush;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs()
    {
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-qaCrashAt") DevCrashAt = a[i + 1];
            if (a[i] == "-qaCrashFlush") DevCrashFlush = a[i + 1] == "1";
        }
    }
#endif
    public static void CrashPoint(string name)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (string.IsNullOrEmpty(DevCrashAt) || DevCrashAt != name) return;
        Debug.Log($"[CommitJournal] DEV CRASH at '{name}' (flush={DevCrashFlush})");
        if (DevCrashFlush) PlayerPrefs.Save();
#if !UNITY_EDITOR
        System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
#endif
    }
}

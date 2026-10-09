using System;
using System.Collections.Generic;
using UnityEngine;

// 広告/パスの報酬の記録(2026-10-10、依頼I)。進行のデータ(テスト用データでは test: に分かれる。通常のセーブへ混ざらない)。
//  ・MILE 2倍: ランの ID ごとに1回(doubledRuns)。追加分は通常の持ち帰り額と同じ(カード倍率を掛け直さない)、
//    通常のラン獲得(記録/ランキング)には入れず、bonusMileTotal に別に数える
//  ・広告ガチャ: 日付(日本時間 0時区切り)ごとの回数。広告で引いた分とパスで無料で引いた分は共通の枠
//  ・付与は「記録 + 報酬」を同じ1回の保存で確定する(どちらかだけ残ることがない)。鍵が付与済みなら何もしない
public static class AdRewards
{
    public const string Key = "AdRewardsV1";
    public const string RunsFinishedKey = "RunsFinishedV1"; // 終わったラン(チュートリアル/練習/闘技場を除く)。初回ランの強制広告を出さない判定

    [Serializable] class Data
    {
        public List<string> doubledRuns = new List<string>();
        public string gachaDay = "";
        public int gachaUsed;
        public int gachaUsedAds, gachaUsedPass;   // 内訳(確認用。枠は合計)
        public long bonusMileTotal;               // 広告/パスで追加した MILE の合計(走行の記録とは別)
        public List<string> claimLog = new List<string>(); // 直近の付与(確認用、最新20)
    }
    static Data d;
    static Data D
    {
        get
        {
            if (d != null) return d;
            string raw = SaveStore.GetString(Key, "");
            try { d = string.IsNullOrEmpty(raw) ? new Data() : JsonUtility.FromJson<Data>(raw) ?? new Data(); } catch { d = new Data(); }
            return d;
        }
    }
    public static void Reload() { d = null; }
    static void WriteWithoutFlush()
    {
        if (D.doubledRuns.Count > 60) D.doubledRuns.RemoveRange(0, D.doubledRuns.Count - 60);
        if (D.claimLog.Count > 20) D.claimLog.RemoveRange(0, D.claimLog.Count - 20);
        SaveStore.SetString(Key, JsonUtility.ToJson(D));
    }
    static void Log(string s) { D.claimLog.Add($"{DateTime.UtcNow:MM-dd HH:mm:ss} {s}"); Debug.Log($"[AdRewards] {s}"); }
    public static IReadOnlyList<string> ClaimLog => D.claimLog;
    public static long BonusMileTotal => D.bonusMileTotal;

    // ---- 終わったランの数(初回の強制広告を出さない)
    public static int RunsFinished => SaveStore.GetInt(RunsFinishedKey, 0);
    public static void NoteRunFinished()
    {
        if (DebugRun.BlocksSave("RunsFinished")) return;
        SaveStore.SetInt(RunsFinishedKey, RunsFinished + 1);
    }

    // ===================================================================== MILE 2倍
    public static bool IsDoubled(string runId) => !string.IsNullOrEmpty(runId) && D.doubledRuns.Contains(runId);

    // 付与(広告の報酬通知/パス)。runId で1回だけ。amount = そのランの持ち帰り MILE(通常分はすでに保存済み)
    public static bool ClaimDouble(string runId, int amount, string via)
    {
        var gm = GameManager.Instance;
        if (gm == null || string.IsNullOrEmpty(runId) || amount <= 0) return false;
        if (IsDoubled(runId)) { Log($"double already claimed {Short(runId)} ({via}) - ignored"); return false; }
        if (DebugRun.BlocksSave("AdDouble")) return false;
        D.doubledRuns.Add(runId);
        D.bonusMileTotal += amount;
        Log($"double +{amount} MILE run {Short(runId)} via {via}");
        gm.AddMileWithoutFlush(amount);   // 財布へ(通常のラン獲得とは別に数える)
        WriteWithoutFlush();
        SaveStore.Save();                // 記録と MILE を1回の保存で
        return true;
    }

    // ===================================================================== 広告ガチャ
    public static int GachaRemaining(string dayKey)
    {
        if (string.IsNullOrEmpty(dayKey)) return 0;
        int used = D.gachaDay == dayKey ? D.gachaUsed : 0;
        return Mathf.Max(0, MonetizationConfig.AdGachaDailyLimit - used);
    }

    // 1回分を確定: 抽選結果のカード + 回数 + 記録を同じ1回の保存で。dayKey の枠が無ければ何もしない
    public static CardDefinition ClaimGachaPull(string dayKey, string via, Func<CardDefinition> draw)
    {
        if (string.IsNullOrEmpty(dayKey) || GachaRemaining(dayKey) <= 0) { Log($"gacha refused (no pulls left {dayKey}, {via})"); return null; }
        if (DebugRun.BlocksSave("AdGacha")) return null;
        var card = draw();
        if (card == null) return null;
        if (D.gachaDay != dayKey) { D.gachaDay = dayKey; D.gachaUsed = 0; D.gachaUsedAds = 0; D.gachaUsedPass = 0; }
        D.gachaUsed++;
        if (via == "pass") D.gachaUsedPass++; else D.gachaUsedAds++;
        Log($"gacha {via} {card.cardId} ({D.gachaUsed}/{MonetizationConfig.AdGachaDailyLimit} on {dayKey})");
        CardInventory.AddCardWithoutFlush(card.cardId, 1, 1);
        WriteWithoutFlush();
        SaveStore.Save();
        return card;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DevResetGacha() { D.gachaDay = ""; D.gachaUsed = 0; WriteWithoutFlush(); SaveStore.Save(); }
    public static string DevSummary => $"day {D.gachaDay} used {D.gachaUsed} (ads {D.gachaUsedAds} / pass {D.gachaUsedPass}), doubled runs {D.doubledRuns.Count}, bonus MILE {D.bonusMileTotal}, runs finished {RunsFinished}";
#endif

    static string Short(string id) => string.IsNullOrEmpty(id) ? "-" : id.Substring(0, Mathf.Min(8, id.Length));
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// ランの「途中の数値」と「確定した記録」を分ける(2026-10-08、仕様変更)。
//  ・ラン中の到達距離・実際に走った距離・使った機能は、ここ(Current)にだけ溜める。中断(CONTINUE)のデータにも入る。
//  ・正式な記録(マップ別BEST / 累計走行距離 / マップ・キャラの解放 / ランキングの投稿)は、
//    正規の帰還(脱出・ラスダンの終わり)= 成功の時だけ、CommitSuccess で一度だけ書く。
//  ・ゲームオーバー / ホームの「破棄」 / 闘技場 / 練習 / Debug Run では書かない(お嬢様騎士の解放だけはゲームオーバーが条件)。
//  ・同じランを二度確定しない: 確定したランの ID を CommittedRunsV1 に残し、同じ ID の中断データは再開させない。
//  ・デバッグ機能(距離ワープ/無敵)を一度でも使ったランは、その後 OFF に戻しても「対象外」のまま(中断を挟んでも)。
public static class RunLedger
{
    public const string CommittedKey = "CommittedRunsV1"; // 確定したランの ID(最新 30 件、カンマ区切り)

    [Serializable]
    public class Run
    {
        public string runId = "";
        public string stageId = "", characterId = "";
        public double maxReached;   // このランで到達した一番遠い距離(疾走の到着地点を含む)
        public double walked;       // 実際に走った距離(疾走で飛ばした区間や、CONTINUE で戻って走り直した区間は含まない)
        public float sprintFrom;    // 疾走出発の到着地点(0 = 使っていない)
        public bool usedAuto, usedSprint;
        public bool debugUsed;      // 距離ワープ/無敵など(開発版の機能)。一度立てたら戻さない
        public string debugWhat = "";
        public bool multiplayer;
        public bool officialStage;  // 正式に解放したマップで走った(開発版の「全部選べる」で選んだ未解放のマップではない)
        public bool testProfile;    // テストデータで遊んだ
        public bool legacy;         // この仕組みより前の中断データから再開した(使った機能を確かめられない)
        public float playSeconds;   // 走っていた時間(一時停止/カード選択の停止を除く)
        public long startedUtc;     // 開始時刻(Unix 秒)
    }

    public static Run Current { get; private set; }
    public static bool Active => Current != null;

    // ---------------------------------------------------------------- 開始 / 再開
    public static void BeginNew(string stageId, string characterId, bool multiplayer)
    {
        Current = new Run
        {
            runId = Guid.NewGuid().ToString("N"),
            stageId = stageId ?? "", characterId = characterId ?? "",
            multiplayer = multiplayer,
            testProfile = SaveProfile.IsTest,
            officialStage = stageId == BossManager.LastStageId ? ProgressStats.FinalDungeonUnlocked : UnlockRules.OfficialStageUnlocked(stageId),
            startedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Debug.Log($"[RunLedger] begin {Current.runId.Substring(0, 8)} stage={stageId} char={characterId}{(multiplayer ? " multi" : "")}");
    }

    // CONTINUE: 中断データの台帳をそのまま使う(無い = この仕組みより前の中断データ)
    public static void Resume(Run saved, string stageId, string characterId, double reached)
    {
        if (saved != null && !string.IsNullOrEmpty(saved.runId)) { Current = saved; }
        else
        {
            BeginNew(stageId, characterId, false);
            Current.legacy = true;
            Current.maxReached = reached;
        }
        Debug.Log($"[RunLedger] resume {Current.runId.Substring(0, 8)} reached={Current.maxReached:F0} walked={Current.walked:F0}{(Current.legacy ? " legacy" : "")}{(Current.debugUsed ? " debug:" + Current.debugWhat : "")}");
    }

    public static void End() { Current = null; }

    public static bool IsCommitted(string runId) => !string.IsNullOrEmpty(runId) && Array.IndexOf(SaveStore.GetString(CommittedKey, "").Split(','), runId) >= 0;

    // ---------------------------------------------------------------- ラン中
    public static void OnDistance(double reached, double newGround)
    {
        var r = Current; if (r == null) return;
        if (reached > r.maxReached) r.maxReached = reached;
        if (newGround > 0 && !double.IsNaN(newGround) && !double.IsInfinity(newGround)) r.walked += newGround;
    }
    public static void MarkSprint(float arrive) { var r = Current; if (r == null) return; r.usedSprint = true; r.sprintFrom = Mathf.Max(r.sprintFrom, arrive); if (arrive > r.maxReached) r.maxReached = arrive; }
    public static void MarkAuto() { var r = Current; if (r == null || r.usedAuto) return; r.usedAuto = true; }
    public static void MarkDebug(string what)
    {
        var r = Current; if (r == null) return;
        if (!r.debugUsed) Debug.Log($"[RunLedger] debug feature used: {what} -> this run is excluded from official records/ranking");
        r.debugUsed = true;
        if (!r.debugWhat.Contains(what)) r.debugWhat = string.IsNullOrEmpty(r.debugWhat) ? what : r.debugWhat + "," + what;
    }
    public static void Tick(float dt) { var r = Current; if (r != null && dt > 0f && dt < 1f) r.playSeconds += dt; }

    // ---------------------------------------------------------------- 確定(成功)
    public class CommitResult
    {
        public bool committed;          // 正式な記録に反映した
        public string skippedWhy = "";  // 反映しなかった理由(開発用のログ)
        public bool newBest;
        public double best;
        public List<string> unlockedStages = new List<string>(), unlockedChars = new List<string>();
        public bool rankingEligible;
    }
    public static CommitResult LastResult { get; private set; }

    // 正規の帰還(脱出/ラスダンの終わり)。記録してよいか判断し、マップ別BEST・累計・解放・ランキングを一度だけ書く
    public static CommitResult CommitSuccess(double reachedAtEnd)
    {
        var res = new CommitResult();
        LastResult = res;
        var r = Current;
        if (r == null) { res.skippedWhy = "no run"; return res; }
        if (reachedAtEnd > r.maxReached) r.maxReached = reachedAtEnd;
        if (DebugRun.WritesBlocked) { res.skippedWhy = "arena/practice/debug run"; return res; }
        if (r.debugUsed) { res.skippedWhy = "debug feature used (" + r.debugWhat + ")"; Debug.Log("[RunLedger] success not recorded: " + res.skippedWhy); return res; }
        if (IsCommitted(r.runId)) { res.skippedWhy = "already committed"; Debug.Log("[RunLedger] success already committed (" + r.runId.Substring(0, 8) + ")"); return res; }

        string stage = r.stageId;
        double best = GameManager.ReadStageBest(stage);
        res.newBest = r.maxReached > best;
        res.best = Math.Max(best, r.maxReached);
        if (res.newBest) GameManager.WriteStageBest(stage, r.maxReached);
        if (stage != BossManager.LastStageId) ProgressStats.AddCommittedDistance(r.walked); // ラスダンは累計に入れない(解放条件は通常3マップ)
        UnlockRules.CommitRun(stage, r.maxReached, res);
        res.rankingEligible = Leaderboard.IsEligible(r, out string why);
        if (res.rankingEligible) Leaderboard.OnSuccessCommitted(r);
        else Debug.Log("[RunLedger] not for ranking: " + why);

        var list = new List<string>(SaveStore.GetString(CommittedKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        list.Add(r.runId);
        while (list.Count > 30) list.RemoveAt(0);
        SaveStore.SetString(CommittedKey, string.Join(",", list));
        ProgressStats.Flush(false);
        SaveStore.Save();
        res.committed = true;
        Debug.Log($"[RunLedger] committed {r.runId.Substring(0, 8)} stage={stage} reached={r.maxReached:F0} walked={r.walked:F0} newBest={res.newBest} unlocked={res.unlockedStages.Count + res.unlockedChars.Count} auto={r.usedAuto} sprint={r.usedSprint} ranking={res.rankingEligible}");
        return res;
    }

    // ゲームオーバー(本当に倒れた)。正式な記録には何も書かない。お嬢様騎士の条件だけ見る
    public static void OnGameOver(double reached)
    {
        var r = Current;
        LastResult = new CommitResult { skippedWhy = "game over" };
        if (r == null || DebugRun.WritesBlocked || r.debugUsed) return;
        if (reached > r.maxReached) r.maxReached = reached;
        UnlockRules.OnRealGameOver(r.stageId, r.maxReached);
        Debug.Log($"[RunLedger] game over {r.runId.Substring(0, 8)} reached={r.maxReached:F0} -> not recorded");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 自動テスト用: ワープで準備した距離を「走った」扱いにする / 値を直接変える(製品版には無い)
    public static void DevClearDebug() { if (Current != null) { Current.debugUsed = false; Current.debugWhat = ""; } }
    public static void DevSet(Action<Run> f) { if (Current != null) f(Current); }
    public static void DevForgetCommitted() { SaveStore.DeleteKey(CommittedKey); }
#endif

    public static string Describe(Run r) => r == null ? "-" :
        string.Format(CultureInfo.InvariantCulture, "{0} {1}/{2} reached={3:F0} walked={4:F0} t={5:F0}s auto={6} sprint={7}{8}{9}{10}",
            r.runId.Length >= 8 ? r.runId.Substring(0, 8) : r.runId, r.stageId, r.characterId, r.maxReached, r.walked, r.playSeconds, r.usedAuto, r.usedSprint,
            r.debugUsed ? " debug:" + r.debugWhat : "", r.legacy ? " legacy" : "", r.multiplayer ? " multi" : "");
}

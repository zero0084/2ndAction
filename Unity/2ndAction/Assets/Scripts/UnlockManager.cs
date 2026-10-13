using System.Collections.Generic;
using UnityEngine;

// Runtime + persisted tracking of which UnlockDefinitions have been earned.
// Static (not a MonoBehaviour) so CardDatabase/EnemyDatabase/DeckEditUI can
// all query it directly without needing a scene reference, the same way
// CardDatabase/UnlockDatabase are static.
//
// 2026-10-13(仕様の統一): 距離の解放は「成功したラン(脱出/ラスダンの正常な終わり)だけ」確定する。
//  ・ラン中に距離を満たした物は「仮」(provisional)。そのランの中では出現などに効く(1,000m から ELITE GOBLIN が出る等、従来どおり)が、保存しない
//  ・RunLedger.CommitSuccess から CommitRun で一度だけ保存。ゲームオーバー/破棄/闘技場/練習/Debug Run/開発機能を使ったランでは保存しない
//  ・中断(CONTINUE)は台帳(RunLedger.Run.maxReached)に到達距離が残るので、再開時に仮の状態を作り直す(お知らせは出し直さない)
//  ・既に保存済みの解放は取り上げない。起動時の backfill は確定した BestDistance から(従来どおり)
public static class UnlockManager
{
    const string UnlockedIdsKey = "UnlockedIds";

    static HashSet<string> unlockedIds;
    static readonly HashSet<string> provisionalIds = new HashSet<string>(); // このランで条件を満たした(まだ確定していない)
    static readonly List<UnlockDefinition> newlyUnlocked = new List<UnlockDefinition>();

    // Must be called once before any of the query methods below are
    // trusted - GameManager.Awake() does this right after BestDistance is
    // loaded, before LoadDeck() (which needs IsTargetUnlocked to already
    // work correctly for its default-deck fallback).
    public static void Initialize(float bestDistanceEver)
    {
        unlockedIds = new HashSet<string>();
        string saved = SaveStore.GetString(UnlockedIdsKey, "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string id in saved.Split(','))
            {
                if (!string.IsNullOrEmpty(id)) unlockedIds.Add(id);
            }
        }

        // Silent backfill: anything the player's persisted best distance
        // already qualifies for (e.g. they'd already gone 5000m before
        // this system - or before a new low-distance unlock - existed)
        // unlocks immediately, with no announcement - it isn't "new" to
        // them, CheckUnlocks below is what queues an actual fresh crossing
        // during play.
        bool changed = false;
        foreach (UnlockDefinition def in UnlockDatabase.AllUnlocks)
        {
            if (!unlockedIds.Contains(def.unlockId) && bestDistanceEver >= def.requiredDistance)
            {
                unlockedIds.Add(def.unlockId);
                changed = true;
            }
        }
        if (changed) Save();
    }

    public static bool IsUnlocked(string unlockId)
    {
        return unlockedIds != null && !string.IsNullOrEmpty(unlockId) && unlockedIds.Contains(unlockId);
    }

    // Whether a specific piece of content (a card id, an enemy id, ...) is
    // currently available. Content with no matching UnlockDefinition is
    // always available - unlocks are opt-in, so every card/enemy that
    // existed before this system keeps working unchanged.
    public static bool IsTargetUnlocked(UnlockType type, string targetId)
    {
        UnlockDefinition def = UnlockDatabase.FindByTarget(type, targetId);
        return def == null || IsUnlocked(def.unlockId) || provisionalIds.Contains(def.unlockId); // ラン中は仮の解放も効く
    }

    // 確定した解放だけ(一覧の表示など。ラン中の仮の解放は含めない)
    public static bool IsTargetUnlockedCommitted(UnlockType type, string targetId)
    {
        UnlockDefinition def = UnlockDatabase.FindByTarget(type, targetId);
        return def == null || IsUnlocked(def.unlockId);
    }

    // Called whenever the run's distance advances (see
    // GameManager.ReportDistance) - unlocks anything newly reached and
    // queues it for the on-screen announcement.
    // 2026-10-13: ここでは保存しない(仮の解放 + 「条件達成」のお知らせ)。notify=false は再開時の作り直し
    public static void CheckUnlocks(float currentRunDistance, bool notify = true)
    {
        if (unlockedIds == null) return;
        foreach (UnlockDefinition def in UnlockDatabase.AllUnlocks)
        {
            if (!unlockedIds.Contains(def.unlockId) && !provisionalIds.Contains(def.unlockId) && currentRunDistance >= def.requiredDistance)
            {
                provisionalIds.Add(def.unlockId);
                if (notify) { newlyUnlocked.Add(def); ProvisionalNotified++; }
                Debug.Log($"[Unlock] provisional: {def.unlockId} at {currentRunDistance:F0}m (confirmed only by finishing the run)");
            }
        }
    }

    // 成功したランの確定(RunLedger.CommitSuccess から一度だけ)。確定した物の表示名を返す
    public static List<string> CommitRun(double reached)
    {
        var names = new List<string>();
        if (unlockedIds == null) return names;
        foreach (UnlockDefinition def in UnlockDatabase.AllUnlocks)
        {
            if (!unlockedIds.Contains(def.unlockId) && reached >= def.requiredDistance)
            {
                unlockedIds.Add(def.unlockId);
                names.Add(def.displayName);
                Debug.Log($"[Unlock] committed: {def.unlockId} (reached {reached:F0}m)");
            }
        }
        if (names.Count > 0) Save();
        return names;
    }

    // ランが終わった(成功/失敗/破棄)・新しいランを始める: 仮の解放を捨てる
    public static void ClearRun() { provisionalIds.Clear(); newlyUnlocked.Clear(); }
    public static int ProvisionalCount => provisionalIds.Count;
    public static int ProvisionalNotified; // 確認用(「解放条件達成」のお知らせを出した回数)

    // Returns (and clears) whatever unlocked for the first time since the
    // last call - null if nothing new. GameManager polls this to drive a
    // brief toast without ever showing the same unlock twice.
    public static List<UnlockDefinition> DrainNewlyUnlocked()
    {
        if (newlyUnlocked.Count == 0) return null;
        var result = new List<UnlockDefinition>(newlyUnlocked);
        newlyUnlocked.Clear();
        return result;
    }

    static void Save()
    {
        if (DebugRun.BlocksSave("UnlockedIds")) return;
        var ids = new List<string>(unlockedIds);
        SaveStore.SetString(UnlockedIdsKey, string.Join(",", ids));
        SaveStore.Save();
    }
}

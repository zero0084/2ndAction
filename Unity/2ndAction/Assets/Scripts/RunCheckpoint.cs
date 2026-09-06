using System;
using System.Collections.Generic;
using UnityEngine;

// Run Continuation/Checkpoint Ver.1 - persists the "Active Run" (items
// 7-13) so a player can RETURN TO HOME / background the app / force-quit
// and CONTINUE later from their last Boss Checkpoint, carrying over their
// current HP/build/unconfirmed MILE exactly as interrupted - NOT full-
// healed, NOT rewound beyond the checkpoint's distance. See GameManager's
// ContinueActiveRun/SaveCheckpoint/SaveInterruptState for exactly what does
// and doesn't get restored.
//
// Deliberately does NOT save per-enemy positions/projectiles/terrain
// chunks/VFX (item 8's own exclusion list) - only what's needed to
// reconstruct a safe, playable Gameplay state at the checkpoint's distance.
public static class RunCheckpoint
{
    const string SaveKey = "ActiveRunCheckpointV1";

    [Serializable]
    public class Data
    {
        public bool active;
        // Where Gameplay actually resumes (the last Boss Reward's
        // completion point) - item 10/12's "距離だけCheckpointへ戻る".
        public float checkpointDistance;
        // The furthest distance genuinely reached across this Run's whole
        // lifetime (including before any interruption) - item 12's MILE-
        // dedup guard. Always >= checkpointDistance.
        public float highestReachedDistance;
        // "傷" - HP at the moment of interruption/checkpoint-save, carried
        // over verbatim (never healed back up by a mere save or an app
        // kill - item 11).
        public int lives;
        public int maxLives;
        public int level;
        public float exp;
        public float expToNext;
        public int enemyKillCount;
        public int bossKillCount;
        public int runEnemyMile;
        public int runBossMile;
        // Bugfix 2026-09-06 - "ESCAPE解禁を1000m到達からBoss撃破後へ変更".
        // A one-way flag for this Run's lifetime (set true the moment the
        // first Boss Reward completes, never reset back to false within
        // the same Run) - must survive RETURN TO HOME/app-kill/CONTINUE
        // exactly like the rest of the Run's build/progress, so a player
        // who already earned Escape before interrupting doesn't lose it on
        // resume.
        public bool escapeUnlocked;
        // Replayed via GameManager.ApplyCardEffects (same mechanism as a
        // live pick) on resume, in order, right after Character Card
        // effects - reconstructs the Run's "Build" (stat modifiers)
        // without needing to separately serialize every individual derived
        // PlayerController field.
        public List<string> upgradeHistoryCardIds = new List<string>();
    }

    static Data cached;

    public static Data Load()
    {
        if (cached != null) return cached;
        string json = PlayerPrefs.GetString(SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { cached = JsonUtility.FromJson<Data>(json); }
            catch (Exception e) { Debug.LogWarning("[RunCheckpoint] Failed to parse save, starting with no active run: " + e.Message); }
        }
        if (cached == null) cached = new Data();
        return cached;
    }

    public static void Save(Data data)
    {
        cached = data;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    // Item 15/16 - called the instant GAME OVER is confirmed (before the
    // death Presentation even plays) or FINISH succeeds, so an app kill
    // immediately afterward can never resurrect a dead/finished run from
    // its last checkpoint.
    public static void Clear()
    {
        cached = new Data { active = false };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(cached));
        PlayerPrefs.Save();
    }

    public static bool HasActiveRun => Load().active;
}

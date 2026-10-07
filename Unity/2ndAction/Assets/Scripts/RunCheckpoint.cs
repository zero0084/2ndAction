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
    public const string Key = SaveKey;

    [Serializable]
    public class Data
    {
        public bool active;
        // ボスの再戦プール(2026-10-02)。bossPoolVersion=0 はこの仕組みより前の保存
        public int bossPoolVersion;
        public List<string> defeatedBosses = new List<string>();
        public List<string> recentBosses = new List<string>();
        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - このRunが
        // どのキャラクターで開始されたか。GameManager.SelectedCharacterId
        // (Homeでいつでも変えられる「次回NEW RUNの既定値」)とは別物 -
        // 一度Runが始まったら、その後Character Selectで選択を変えても
        // このRun自体のキャラクターは変わらない(BeginContinuedRunは必ず
        // この値を使う、SelectedCharacterIdは使わない)。
        public string characterId;
        // ステージ選択導線追加(2026-09-12) - characterIdと全く同じ理由・
        // 役割。このRunが実際に出発したステージ。
        public string stageId;
        // Where Gameplay actually resumes (the last Boss Reward's
        // completion point) - item 10/12's "距離だけCheckpointへ戻る".
        public float checkpointDistance;
        // CONTINUE の速さ(2026-10-07): チェックポイントを保存した瞬間の「生の走行距離」(自然加速の元。ボス戦の間も含む)と、
        // その時の走行速度(確認用)。0 = この仕組みより前の保存(従来どおり記録の距離から)
        public double speedDistance;
        public float savedSpeedKmh;
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
        public int runBonusMile; // BONUS ZONE(2026-09-29)の仮取得MILE(古いデータは0)
        public int runRingMile;  // 疾走出発のリングの代替報酬の仮取得MILE(2026-10-06、古いデータは0)
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
        // カードバランス v3(2026-10-03): 取得のやり直しだけでは戻せない状態(古いデータは既定値=未使用)
        public int phoenixConsumed;
        public int phoenixResetHistoryIndex = -1;
        public float secondWindReadyDistance;
        public bool lastChanceSpent;
        // #100 ULTIMATE(2026-10-04): Gauge(%)。古いデータは0
        public float ultimateGauge;
        // FINAL EVOLUTION(2026-10-04): 資格/READY/ACTIVE/USED/残り。古いデータは空(状態なし)
        public List<FinalEvolution.SaveState> finalEvolution = new List<FinalEvolution.SaveState>();
        public ComboSystem.SaveData combo = new ComboSystem.SaveData(); // COMBO(2026-10-06): 一時的な数えと通知済みだけ(成立は能力から計算し直す)
        // 疾走出発(2026-10-05): 飛ばした距離(持ち帰りの MILE の距離ぶんから除く)。古いデータは0
        public float sprintSkippedMeters;
        // 2026-10-08: このランの途中の数値(RunLedger)。古いデータは空(runId 無し = 使った機能を確かめられない)
        public RunLedger.Run ledger = new RunLedger.Run();
    }

    static Data cached;

    public static Data Load()
    {
        if (cached != null) return cached;
        string json = SaveStore.GetString(SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { cached = JsonUtility.FromJson<Data>(json); }
            catch (Exception e) { Debug.LogWarning("[RunCheckpoint] Failed to parse save, starting with no active run: " + e.Message); }
        }
        if (cached == null) cached = new Data();
        // 2026-10-08: 既に確定したラン(保存の直後に落ちた等)の中断データは再開させない(記録を二度確定しない)
        if (cached.active && cached.ledger != null && RunLedger.IsCommitted(cached.ledger.runId))
        {
            Debug.Log("[RunCheckpoint] the suspended run was already committed -> discarded");
            cached.active = false;
        }
        return cached;
    }

    public static void Save(Data data)
    {
        if (DebugRun.BlocksSave("RunCheckpoint.Save")) return; // 記録対象外のラン: プレイヤーの中断中のラン(CONTINUE)を上書きしない
        cached = data;
        SaveStore.SetString(SaveKey, JsonUtility.ToJson(data));
        SaveStore.Save();
    }

    // Item 15/16 - called the instant GAME OVER is confirmed (before the
    // death Presentation even plays) or FINISH succeeds, so an app kill
    // immediately afterward can never resurrect a dead/finished run from
    // its last checkpoint.
    public static void Clear()
    {
        if (DebugRun.BlocksSave("RunCheckpoint.Clear")) return; // 記録対象外のラン: 中断中のランを消さない
        cached = new Data { active = false };
        SaveStore.SetString(SaveKey, JsonUtility.ToJson(cached));
        SaveStore.Save();
    }

    public static bool HasActiveRun => Load().active;

    // セーブの起動時処理/初期化の後に読み直させる(2026-10-01)
    public static void Reload() { cached = null; }
}

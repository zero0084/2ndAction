using UnityEngine;

// ラスダンの「ONE MORE MILE?」で YES を選んだ後(2026-10-13 第3弾)。
//  ・YES の瞬間に、ここまでのクリアを「成功」として確定する(BEST/解放/ランキング/MILE。脱出と同じ RunLedger.CommitSuccess)。
//    その後に倒れても、確定した分は失わない。
//  ・同じランのまま続ける(キャラ/カード/強化/Lv/HP はそのまま。距離も続きから)。続きは新しい台帳(RunLedger.BeginContinuation)で、
//    MILE/距離の MILE は YES の後の分だけを数える(MileDistanceBase)。続きの後で脱出すれば続きの分だけ確定、倒れれば続きの分だけ失う。
//  ・中断データに「YES の後」を残す(CONTINUE で続きから。エンドロール/三姉妹は繰り返さない)。
public partial class GameManager
{
    public bool LdBeyond { get; private set; }            // YES の後を走っている
    public float LdBeyondFrom { get; private set; }       // 地形/敵/ボスが戻ってくる距離
    public float MileDistanceBase { get; private set; }   // 距離の MILE を既に受け取った距離(YES の時点)
    public RunLedger.CommitResult ClearCommit { get; private set; } // YES の時の確定
    public int ClearMilePaid { get; private set; }
    public const float BeyondRunwayMeters = 400f;         // YES の後、平らな道を走る長さ(走り出してから地形が戻る)

    public void CommitClearAndContinue()
    {
        if (!HasStarted || IsGameOver || LdBeyond) return;
        bool journal = !DebugRun.WritesBlocked;
        if (journal) { WriteCommitJournal(); CommitJournal.CrashPoint("yes_journal"); SaveStore.BeginBatch(); }
        try
        {
            ClearCommit = RunLedger.CommitSuccess(MaxDistanceExact);
            LastCommit = ClearCommit;
            bool record = ClearCommit != null && ClearCommit.committed && !DebugRun.BlocksSave("BestDistance/BestTime");
            if (record && MaxDistance > BestDistance) { BestDistance = MaxDistance; SaveStore.SetFloat(BestDistanceKey, BestDistance); }
            float rt = Time.time - runStartTime;
            if (record && rt > BestTime) { BestTime = rt; SaveStore.SetFloat(BestTimeKey, BestTime); }
            RunDistanceMile = DistanceMileNow;
            ClearMilePaid = RunMile;
            AddMile(ClearMilePaid);
            // 続きの分は 0 から(距離の MILE は YES の地点から)
            MileDistanceBase += MileDistanceForRun;
            RunDistanceMile = 0; RunEnemyMile = 0; RunBossMile = 0; RunBonusMile = 0; ResetRingMileForContinuation();
            UnlockRules.ClearRunFlags();
            UnlockManager.ClearRun();
            RunLedger.BeginContinuation(MaxDistanceExact);
            LdBeyond = true;
            LdBeyondFrom = MaxDistance + BeyondRunwayMeters;
            SaveCheckpoint(); // 中断データ = YES の地点から続き(三姉妹/エンドロールは繰り返さない)
            Debug.Log($"[EndingChoice] YES: clear committed={ClearCommit != null && ClearCommit.committed} ({(ClearCommit != null ? ClearCommit.skippedWhy : "")}) MILE +{ClearMilePaid} at {MaxDistance:F0}m -> continue as {RunLedger.Describe(RunLedger.Current)}, world returns at {LdBeyondFrom:F0}m");
        }
        finally
        {
            if (journal) { CommitJournal.Clear(); SaveStore.EndBatch(); }
        }
    }

    // CONTINUE: YES の後の中断データから(BeginContinuedRun が呼ぶ)
    void RestoreBeyond(RunCheckpoint.Data data)
    {
        LdBeyond = data.ldBeyond;
        LdBeyondFrom = data.ldBeyondFrom;
        MileDistanceBase = data.mileDistanceBase;
    }
}

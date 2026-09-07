using System.Collections;
using UnityEngine;

// Polish Pass 1, item 2 - a brief, shared "impact punch" freeze, driven by
// Time.timeScale so every animation/movement/physics update actually
// pauses (not just a fake visual stutter), yet the freeze itself is timed
// with WaitForSecondsRealtime so it isn't affected by the timeScale change
// it's making. Kept to tens of milliseconds by callers (see EnemyController)
// so it reads as impact, not as the game actually stopping.
//
// Reference-counted rather than each call independently capturing/
// restoring Time.timeScale: if two enemies die the same frame, both
// Freeze() calls run, and the second one's naive "restore to whatever it
// was when I started" would capture 0 (the FIRST call had already zeroed
// it) - so when the first call's own restore ran, the second one would
// immediately stomp it back to 0 right after, permanently freezing the
// game. (This actually happened - killing two enemies at once froze the
// game solid.) Only the first Freeze() to start actually captures/zeroes
// timeScale, and only the last one still active restores it.
public static class HitStop
{
    static int activeCount;
    static float capturedTimeScale = 1f;

    // Bugfix 2026-09-08 (Bug #001 - "TimeScale Deadlock"仮説の検証/防御) -
    // マスター側の実機Freeze Snapshotで、IsBossPhase=true・Boss(Majin)2体
    // 生存中・BossPresentationRunning/BossRewardRunning/LevelUpPendingが
    // 全てfalseなのにTime.timeScale=0(InputEnabled=false)のまま固着する
    // 事象を確認。コードベース全体を検索した結果、Time.timeScaleを直接0
    // にする書き込み元は「BossMilestonePresentation(TempoDown)」「GameManager
    // のRunLevelUpChoice/RunBossRewardChoice」「このHitStop」の4箇所のみで、
    // 前3つはそれぞれ専用のFlag(BossPresentationRunning/levelUpPending/
    // RewardSequence.IsRunning経由)で追跡されており、Freeze時点で全て
    // falseだったことから消去法でこのHitStopが最有力候補と判断した。
    //
    // このクラス自身のコメントが既に2回、同種の「activeCountが正しく0
    // に戻らない」バグを説明している(1: 同フレーム2体死亡、2:
    // 2026-09-06の外部Pauseとの競合) - 今回は「そもそも何らかの理由で
    // Freezeコルーチンのtry/finallyのfinallyまで到達しないケースがある」
    // 可能性(GameObjectが通常と異なる形でDestroy/非アクティブ化される
    // 経路など)を主要仮説とし、根本原因を完全に特定しきれるまでの間、
    // このクラス自身の内部不変条件("Freezeは常に1秒未満で終わる")を
    // 直接検査して自己修復する形の防御を追加した。マスター/エステルの
    // 指摘どおり「Reward Flow側の汎用安全弁」で誤魔化すのではなく、
    // このHitStop自身が持つべき正しさ(有限時間で必ず終わる)を保証する
    // という位置づけであり、通常再生時間(数百ms)を大幅に超える
    // MaxReasonableFreezeSeconds(3秒)を実際に超えて残り続けていること
    // 自体が、既に「壊れている」ことの確定的な証拠になる - 誤って正常な
    // Freezeを打ち切ってしまうリスクは事実上ない。
    const float MaxReasonableFreezeSeconds = 3f;
    static float freezeStartRealtime = -1f;

    // 診断用(BossDiagnostics.BuildSnapshotから参照) - 通常時はIsActive=
    // false/ActiveCount=0のはず。Freeze SnapshotでActiveCount>0かつ
    // SecondsSinceFreezeStartedが大きい値のまま出ていれば、今回の
    // TimeScale DeadlockがこのHitStop由来だったことの直接的な証拠になる。
    public static bool IsActive => activeCount > 0;
    public static int ActiveCount => activeCount;
    public static float SecondsSinceFreezeStarted => freezeStartRealtime >= 0f ? Time.realtimeSinceStartup - freezeStartRealtime : 0f;

    public static IEnumerator Freeze(float durationRealSeconds)
    {
        if (durationRealSeconds <= 0f) yield break;

        // 自己修復(上記コメント参照) - 新しいFreezeを始める前に、既存の
        // activeCountが不自然に長時間残っていないか確認する。
        if (activeCount > 0 && freezeStartRealtime >= 0f && Time.realtimeSinceStartup - freezeStartRealtime > MaxReasonableFreezeSeconds)
        {
            Debug.LogWarning($"[HitStop] Leaked activeCount detected on new Freeze() call (stuck {Time.realtimeSinceStartup - freezeStartRealtime:F1}s, a legitimate HitStop always finishes in well under 1s) - self-healing before proceeding.");
            ForceReset();
        }

        if (activeCount == 0)
        {
            // Bugfix 2026-09-06 - root cause of "Boss撃破後にゲームが停止
            // する". activeCount==0 means no OTHER HitStop.Freeze call
            // currently owns the timeScale=0 state - but Time.timeScale
            // can still already be <=0 for a completely unrelated reason
            // (Level Up/Boss Reward's own direct Time.timeScale=0 pause,
            // or Boss Milestone's tempo-down). If a stray Enemy hit
            // reaction (HitAndDie/NonLethalHit, EnemyController.cs) landed
            // during that window - e.g. an attack hitbox already
            // overlapping a second enemy the same physics step the last
            // Boss died - this method used to capture 0 as "the value to
            // restore to", then restore exactly that 0 after its own brief
            // duration, WELL AFTER the Boss Reward pause had already
            // legitimately resolved and set Time.timeScale back to 1
            // (ApplyUpgradeByCardId) - permanently re-freezing the run
            // with no further event left to un-freeze it. Since we're not
            // the one holding time at 0 in this case, we must not manage
            // it at all - just wait out the duration for the caller's own
            // animation timing and let whichever system actually owns the
            // pause resolve it on its own schedule.
            if (Time.timeScale <= 0f)
            {
                yield return new WaitForSecondsRealtime(durationRealSeconds);
                yield break;
            }
            capturedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            freezeStartRealtime = Time.realtimeSinceStartup;
        }
        activeCount++;

        // try/finally so the counter (and timeScale) can never leak even if
        // this coroutine is cut short - e.g. the enemy running it gets
        // Destroy()'d mid-freeze (BossManager.ClearAllEnemies can do this).
        // Unity disposes a stopped coroutine's enumerator, which runs this
        // finally block, so the decrement/restore below always happens.
        try
        {
            yield return new WaitForSecondsRealtime(durationRealSeconds);
        }
        finally
        {
            activeCount--;
            if (activeCount <= 0)
            {
                activeCount = 0;
                Time.timeScale = capturedTimeScale;
                freezeStartRealtime = -1f;
            }
        }
    }

    // Bugfix 2026-09-08 - BossDiagnostics.UpdateFreezeWatchdog(毎フレーム、
    // Boss Phaseの内外を問わず)から呼ばれる定期監視。上のFreeze()自身の
    // 入口チェックだけでは「次に誰かがFreezeを呼ぶまで」検知できない
    // (=もう誰も敵を殴らなくなった場合、詰まったままいつまでも気づけない)
    // ため、独立した定期ポーリングとして用意した。
    public static void PollForLeakedFreeze()
    {
        if (activeCount <= 0 || freezeStartRealtime < 0f) return;
        if (Time.realtimeSinceStartup - freezeStartRealtime <= MaxReasonableFreezeSeconds) return;

        Debug.LogWarning($"[HitStop] Leaked activeCount detected via periodic watchdog (stuck {Time.realtimeSinceStartup - freezeStartRealtime:F1}s, a legitimate HitStop always finishes in well under 1s) - self-healing.");
        ForceReset();
    }

    static void ForceReset()
    {
        activeCount = 0;
        Time.timeScale = capturedTimeScale > 0f ? capturedTimeScale : 1f;
        freezeStartRealtime = -1f;
    }
}

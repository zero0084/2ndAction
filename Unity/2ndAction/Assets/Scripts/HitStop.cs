using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Polish Pass 1, item 2 - a brief, shared "impact punch" freeze, driven by
// Time.timeScale so every animation/movement/physics update actually
// pauses (not just a fake visual stutter), yet the freeze itself is timed
// with WaitForSecondsRealtime so it isn't affected by the timeScale change
// it's making. Kept to tens of milliseconds by callers (see EnemyController)
// so it reads as impact, not as the game actually stopping.
//
// 高速走行中のフリーズ/ワープ調査(2026-09-22)で一本化 - 以前はこのクラス
// 自身が「Freeze開始前のTime.timeScale」を記憶し、自分のFreeze時間が終わっ
// たら無条件にその値へ書き戻していた(activeCountによる簡易な参照カウント
// 付き)。これは「同フレームに2体死亡」や「Boss Reward解決直後に別の敵が
// 死亡」のような単純な重なりには対応できていたが、"HitStopが有効な間に、
// 別の敵の撃破がLevel Up/Boss Rewardの選択を新たに開始する"ケースまでは
// 対応できていなかった: 後から増えたLevel Up側の「止めたい」という意思を
// このクラスは知らないまま、自分のFreeze終了時に「自分がFreezeを始める前
// の値」へ戻してしまい、カード選択中のはずのゲームが裏で進行してしまう -
// これが今回の「カード選出中/被弾直後に画面が一時停止し、再開時にワープ
// したように見える」不具合の根本原因の1つ(TerrainDamageAutoTest等と同じ
// 手法でHitAndDie→HitStop.Freeze→RegisterEnemyKill→(閾値超え時)
// RunLevelUpChoiceの呼び出し順序をコード上で確認して特定)。
//
// 対策: Time.timeScaleへの実際の書き込みはTimeControl(共有の理由付き
// 参照カウント)に一本化し、このクラスは「HitStopという理由」を自分の
// Freeze呼び出し1回ごとに専用のowner(object)として登録/解除するだけに
// した。他の停止理由(Level Up等)が同時に有効でも、TimeControl側の集合が
// 空にならない限りTime.timeScaleは0のままなので、このクラスがそれを勝手に
// 1へ戻すことは構造的に起こらない。
public static class HitStop
{
    // Freeze()呼び出しごとに専用のownerを1つ生成してactiveOwnersへ積む
    // (同じ文字列等を共有すると、複数の同時Freezeのうち先に終わった1つが
    // TimeControl側の同じ理由を消してしまう - 各呼び出しを独立させるため
    // 必ず別インスタンスにする)。
    static readonly List<object> activeOwners = new List<object>();

    // Bugfix 2026-09-08 (Bug #001 - "TimeScale Deadlock"仮説の検証/防御) -
    // マスター側の実機Freeze Snapshotで、IsBossPhase=true・Boss(Majin)2体
    // 生存中・BossPresentationRunning/BossRewardRunning/LevelUpPendingが
    // 全てfalseなのにTime.timeScale=0(InputEnabled=false)のまま固着する
    // 事象を確認。今回のTimeControl一本化後も、「そもそも何らかの理由で
    // Freezeコルーチンのtry/finallyのfinallyまで到達しないケースがある」
    // (GameObjectが通常と異なる形でDestroy/非アクティブ化される経路など)
    // 可能性に備え、この自己修復ロジック自体は維持する。
    // 2026-10-07: 3秒 → 1秒(本物の HitStop は 0.2秒未満。重なっても1秒を超えない)。見張りが働く時は必ずログが出る
    const float MaxReasonableFreezeSeconds = 1f;
    public static int LeakHeals { get; private set; } // 確認用: 自己修復が働いた回数(0 のはず)

    // 2026-10-07「攻撃が当たった時にまれに数秒止まる」の原因:
    //  敵の通常の命中(EnemyController.ReactToHit など)が HitStop を「その敵のコルーチンの中」で動かしていた。
    //  止めている間(実時間0.05〜0.1秒)にその敵が倒れて非表示(FINISH は撃破の瞬間に SetActive(false))になると、
    //  Unity はコルーチンを止めるだけで finally を実行しないことがあり、時間を戻す処理(TimeControl.Resume)が漏れる。
    //  漏れた停止は、見張り(PollForLeakedFreeze)が「3秒以上」で直すまで続いていた = 数秒止まる。
    // 対策: 停止の登録/解除は、消えない専用の実行役(Runner)の上で行う。呼び出し側は Run(d) で同じ時間だけ待つ(手応えは同じ)。
    class Runner : MonoBehaviour { }
    static Runner runner;
    static Runner GetRunner()
    {
        if (runner == null)
        {
            var go = new GameObject("[HitStopRunner]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            runner = go.AddComponent<Runner>();
        }
        return runner;
    }
    // 停止を消えない実行役で始め、呼び出し側は実時間で同じだけ待つ(呼び出し側が途中で止まっても停止は必ず解ける)
    public static IEnumerator Run(float durationRealSeconds)
    {
        if (durationRealSeconds <= 0f) yield break;
        GetRunner().StartCoroutine(Freeze(durationRealSeconds));
        yield return new WaitForSecondsRealtime(durationRealSeconds);
    }
    public static void Begin(float durationRealSeconds) { if (durationRealSeconds > 0f) GetRunner().StartCoroutine(Freeze(durationRealSeconds)); }
    static float freezeStartRealtime = -1f;

    // 診断用(BossDiagnostics.BuildSnapshotから参照) - 通常時はIsActive=
    // false/ActiveCount=0のはず。
    public static bool IsActive => activeOwners.Count > 0;
    public static int ActiveCount => activeOwners.Count;
    public static float SecondsSinceFreezeStarted => freezeStartRealtime >= 0f ? Time.realtimeSinceStartup - freezeStartRealtime : 0f;

    public static IEnumerator Freeze(float durationRealSeconds)
    {
        if (durationRealSeconds <= 0f) yield break;

        // 自己修復(上記コメント参照) - 新しいFreezeを始める前に、既存の
        // activeOwnersが不自然に長時間残っていないか確認する。
        if (activeOwners.Count > 0 && freezeStartRealtime >= 0f && Time.realtimeSinceStartup - freezeStartRealtime > MaxReasonableFreezeSeconds)
        {
            Debug.LogWarning($"[HitStop] Leaked freeze detected on new Freeze() call (stuck {Time.realtimeSinceStartup - freezeStartRealtime:F1}s, a legitimate HitStop always finishes in well under 1s) - self-healing before proceeding.");
            ForceReset();
        }

        object owner = new object();
        activeOwners.Add(owner);
        if (activeOwners.Count == 1) freezeStartRealtime = Time.realtimeSinceStartup;
        FreezeDiagnostics.LogEvent($"[HitStop] Freeze start dur={durationRealSeconds:F2}s active={activeOwners.Count}");
        TimeControl.Pause(owner);

        // try/finally so the owner (and TimeControl's registration) can
        // never leak even if this coroutine is cut short - e.g. the enemy
        // running it gets Destroy()'d mid-freeze (BossManager.ClearAllEnemies
        // can do this). Unity disposes a stopped coroutine's enumerator,
        // which runs this finally block, so cleanup below always happens.
        try
        {
            yield return new WaitForSecondsRealtime(durationRealSeconds);
        }
        finally
        {
            activeOwners.Remove(owner);
            TimeControl.Resume(owner);
            if (activeOwners.Count == 0) freezeStartRealtime = -1f;
            FreezeDiagnostics.LogEvent($"[HitStop] Freeze end active={activeOwners.Count}");
        }
    }

    // Bugfix 2026-09-08 - BossDiagnostics.UpdateFreezeWatchdog(毎フレーム、
    // Boss Phaseの内外を問わず)から呼ばれる定期監視。上のFreeze()自身の
    // 入口チェックだけでは「次に誰かがFreezeを呼ぶまで」検知できない
    // (=もう誰も敵を殴らなくなった場合、詰まったままいつまでも気づけない)
    // ため、独立した定期ポーリングとして用意した。
    public static void PollForLeakedFreeze()
    {
        if (activeOwners.Count <= 0 || freezeStartRealtime < 0f) return;
        if (Time.realtimeSinceStartup - freezeStartRealtime <= MaxReasonableFreezeSeconds) return;

        Debug.LogWarning($"[HitStop] Leaked freeze detected via periodic watchdog (stuck {Time.realtimeSinceStartup - freezeStartRealtime:F1}s, a legitimate HitStop always finishes in well under 1s) - self-healing.");
        FreezeDiagnostics.LogEvent($"[HitStop] LEAK healed after {Time.realtimeSinceStartup - freezeStartRealtime:F2}s owners={activeOwners.Count} {StallProbe.LastHitsText()}");
        LeakHeals++;
        ForceReset();
    }

    static void ForceReset()
    {
        foreach (object o in activeOwners) TimeControl.Resume(o);
        activeOwners.Clear();
        freezeStartRealtime = -1f;
    }
}

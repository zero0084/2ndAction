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

    public static IEnumerator Freeze(float durationRealSeconds)
    {
        if (durationRealSeconds <= 0f) yield break;

        if (activeCount == 0)
        {
            capturedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
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
            }
        }
    }
}

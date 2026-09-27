#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 自動スローモーション(2026-09-27)のEditor専用自動確認。結果は AutoSlowAutoTest.txt。
//  1) 通常速度では倍率1.0のまま / 画面上の前進速度 v×s(v) が速度に対して単調増加
//  2) 高速で0.75まで滑らかに(1フレームの変化が制限内)下がり、速度を戻すと滑らかに1へ戻る
//  3) ジャンプの高さ/滞空(ゲーム内時間)と攻撃判定の持続(ゲーム内時間)がスロー中も変わらない
//  4) 距離はゲーム内時間あたり走行速度ぶんだけ増える(水増ししない)、SPEEDはスロー前の値
//  5) 完全停止(Pause/HitStop/カード選択)とボス登場演出から、1ではなく自動スロー倍率へ戻る
//  6) OFFにすると1へ戻る(停止は解除しない) / ゲームオーバー・リトライでスロー状態が残らない
public class AutoSlowAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("AutoSlowTest", 0) != 1) return;
        EditorPrefs.SetInt("AutoSlowTest", 0);
        new GameObject("AutoSlowTest").AddComponent<AutoSlowAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[AutoSlowTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } }

    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        var slow = AutoSlowMotion.Instance;
        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + cond); } };
        Application.logMessageReceived += handler;

        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage("wasteland_road"); // 天井の無いステージ(洞窟の天井はジャンプの高さを変えてしまう)。穴は計測前に避ける
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
        gm.DebugSetInvincible(true);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        StartCoroutine(AutoPickLevelUp());
        StartCoroutine(JumpPits(pc));
        StartCoroutine(ClearEnemies());
        // 距離EXPのレベルアップ(カード選択の停止)が計測の途中に割り込まないようにする(明示的な確認は下で別に行う)
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);
        expF?.SetValue(gm, 0f);
        slow.SetEnabled(true);
        L($"settings start={slow.startSpeed} full={slow.fullSlowSpeed} min={slow.minScale} transition={slow.transitionTime}s smoothing={slow.speedSmoothing}s hysteresis={slow.hysteresis}");

        // ---- 1) 通常速度 / 単調性 ----
        PlayerController.DebugSpeedScale = 1f;
        yield return new WaitForSecondsRealtime(2f);
        L($"[Normal] v={pc.CurrentAutoRunSpeed:F2}m/s timeScale={Time.timeScale:F3} auto={TimeControl.AutoScale:F3}");
        Check(Mathf.Approximately(Time.timeScale, 1f), "normal speed keeps 1.0");
        float prevScreen = 0f; bool mono = true; float worst = 99f;
        for (float v = 0f; v <= 40f; v += 0.05f)
        {
            float screen = v * slow.ScaleForSpeed(v);
            if (screen < prevScreen - 1e-4f) mono = false;
            worst = Mathf.Min(worst, screen - prevScreen);
            prevScreen = screen;
        }
        L($"[Monotonic] v*s(v) non-decreasing over 0..40 m/s = {mono} (min step {worst:F4}); s(7)={slow.ScaleForSpeed(7f):F3} s(10)={slow.ScaleForSpeed(10f):F3} s(12)={slow.ScaleForSpeed(12f):F3} s(20)={slow.ScaleForSpeed(20f):F3}");
        Check(mono, "screen speed never drops as run speed rises");

        // ---- 2) 高速で0.75へ ----
        PlayerController.DebugSpeedScale = 3f; // 5m/s → 15m/s
        float maxStep = 0f, prev = Time.timeScale, t = 0f, reachT = -1f;
        while (t < 5f)
        {
            yield return null; t += Time.unscaledDeltaTime;
            if (TimeControl.IsPaused || TimeControl.IsPresentationDriving) { prev = Time.timeScale; continue; }
            maxStep = Mathf.Max(maxStep, Mathf.Abs(Time.timeScale - prev));
            prev = Time.timeScale;
            if (reachT < 0f && TimeControl.AutoScale <= slow.minScale + 0.001f) reachT = t;
        }
        L($"[High] v={pc.CurrentAutoRunSpeed:F2} timeScale={Time.timeScale:F3} auto={TimeControl.AutoScale:F3} reached0.75 at {reachT:F2}s maxStepPerFrame={maxStep:F4} SPEED(HUD)={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F1}km/h");
        Check(Mathf.Abs(TimeControl.AutoScale - slow.minScale) < 0.002f, "high speed reaches min scale");
        Check(maxStep < 0.02f, "scale changes smoothly (no jump)");
        Check(Mathf.Abs(GameManager.SpeedKmh(pc.CurrentAutoRunSpeed) - 15f * 3.6f) < 0.6f, "SPEED shows pre-slow run speed");

        // ---- 3)/4) 同じ走行速度でスローOFF(1.0)とON(0.75)を比べる(ゲーム内時間での軌道/判定/距離) ----
        var offM = new float[5]; var onM = new float[5];
        slow.SetEnabled(false);
        yield return new WaitForSecondsRealtime(slow.transitionTime + 0.5f);
        yield return MeasureSet(pc, offM);
        slow.SetEnabled(true);
        yield return new WaitForSecondsRealtime(slow.transitionTime + 0.5f);
        yield return MeasureSet(pc, onM);
        L($"[Compare v={pc.CurrentAutoRunSpeed:F1}] scale OFF={offM[4]:F2} ON={onM[4]:F2}");
        L($"  jump: apex height OFF={offM[0]:F3} ON={onM[0]:F3} / time to apex(game s) OFF={offM[1]:F3} ON={onM[1]:F3}");
        L($"  attack hitbox active(game s) OFF={offM[2]:F3} ON={onM[2]:F3}");
        L($"  distance per game-second minus run speed: OFF={offM[3]:+0.00;-0.00} ON={onM[3]:+0.00;-0.00}");
        Check(Mathf.Abs(offM[0] - onM[0]) < 0.05f && Mathf.Abs(offM[1] - onM[1]) < 0.03f, "jump arc unchanged in game time");
        Check(Mathf.Abs(offM[2] - onM[2]) < 0.03f, "attack timing unchanged in game time");
        Check(Mathf.Abs(offM[3]) < 0.3f && Mathf.Abs(onM[3]) < 0.3f, "distance per game-second = run speed (no padding)");
        // 実時間あたりの前進 = ゲーム内の前進 × 倍率
        double d0 = pc.DistanceExact; float r0 = Time.realtimeSinceStartup, g0 = Time.time;
        yield return new WaitForSecondsRealtime(2f);
        double dd = pc.DistanceExact - d0; float dr = Time.realtimeSinceStartup - r0, dg = Time.time - g0;
        L($"[Screen] per real-second {dd / dr:F2} m/s = per game-second {dd / dg:F2} x scale {TimeControl.AutoScale:F2} (game/real time ratio {dg / dr:F3})");
        Check(Mathf.Abs(dg / dr - TimeControl.AutoScale) < 0.02f, "game time runs at auto scale");

        // ---- 5) 停止からの復帰 ----
        float auto = TimeControl.AutoScale;
        TimeControl.Pause("AutoSlowTest");
        float paused = Time.timeScale;
        TimeControl.Resume("AutoSlowTest");
        L($"[Pause] during={paused:F2} after={Time.timeScale:F3} auto={auto:F3}");
        Check(paused == 0f && Mathf.Abs(Time.timeScale - TimeControl.AutoScale) < 0.001f && Time.timeScale < 0.99f, "pause resumes to auto scale, not 1");

        StartCoroutine(HitStop.Freeze(0.15f));
        yield return null;
        float during = Time.timeScale;
        yield return new WaitForSecondsRealtime(0.3f);
        L($"[HitStop] during={during:F2} after={Time.timeScale:F3} auto={TimeControl.AutoScale:F3}");
        Check(during == 0f && Mathf.Abs(Time.timeScale - TimeControl.AutoScale) < 0.001f && Time.timeScale < 0.99f, "hitstop resumes to auto scale");

        // カード選択(実際のレベルアップ処理)
        levelUpObserved = false; minDuringLevelUp = 1f;
        typeof(GameManager).GetMethod("RunLevelUpChoice", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float lt = 0f;
        while (lt < 8f && (gm.IsRewardSequenceWaitingForSelection || TimeControl.IsPaused || lt < 0.5f)) { minDuringLevelUp = Mathf.Min(minDuringLevelUp, Time.timeScale); yield return null; lt += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
        L($"[LevelUp] minDuring={minDuringLevelUp:F2} after={Time.timeScale:F3} auto={TimeControl.AutoScale:F3} observedPick={levelUpObserved}");
        Check(minDuringLevelUp == 0f && Time.timeScale < 0.99f && Mathf.Abs(Time.timeScale - TimeControl.AutoScale) < 0.001f, "card choice pauses then resumes to auto scale");

        // ボス登場演出(テンポランプ): 開始値は自動スロー倍率、終了後は自動スロー倍率へ
        var pres = BossMilestonePresentation.Instance;
        if (pres != null)
        {
            float startScale = TimeControl.AutoScale; float maxDuring = 0f; bool spawned = false; bool hitStopDuring = false;
            pres.Play(1000f, false, () => spawned = true);
            float pt = 0f; float rampMin = 1f;
            while (pt < 10f && pres.IsRunning)
            {
                if (TimeControl.IsPresentationDriving) { maxDuring = Mathf.Max(maxDuring, Time.timeScale); rampMin = Mathf.Min(rampMin, Time.timeScale); }
                // 演出の途中でHitStopが重なっても、終わったら演出の値へ戻る(1へ跳ねない)
                if (!hitStopDuring && pt > 0.4f && TimeControl.IsPresentationDriving && Time.timeScale > 0f) { hitStopDuring = true; StartCoroutine(HitStop.Freeze(0.05f)); }
                yield return null; pt += Time.unscaledDeltaTime;
            }
            yield return new WaitForSecondsRealtime(0.3f);
            L($"[BossPresentation] start={startScale:F3} maxDuringRamp={maxDuring:F3} minDuringRamp={rampMin:F2} spawned={spawned} after={Time.timeScale:F3} auto={TimeControl.AutoScale:F3}");
            Check(maxDuring <= startScale + 0.001f, "presentation ramp starts from auto scale (never jumps to 1)");
            Check(Mathf.Abs(Time.timeScale - TimeControl.AutoScale) < 0.001f && Time.timeScale < 0.99f, "presentation ends at auto scale");
        }

        // ---- 6) OFF / 速度を戻す / リセット ----
        slow.SetEnabled(false);
        yield return new WaitForSecondsRealtime(slow.transitionTime + 0.4f);
        L($"[Off] timeScale={Time.timeScale:F3}");
        Check(Mathf.Approximately(Time.timeScale, 1f), "OFF returns to 1");
        TimeControl.Pause("AutoSlowTestOff");
        bool offKeepsPause = Time.timeScale == 0f;
        TimeControl.Resume("AutoSlowTestOff");
        Check(offKeepsPause, "OFF does not cancel pauses");
        slow.SetEnabled(true);
        yield return new WaitForSecondsRealtime(slow.transitionTime + 0.4f);
        PlayerController.DebugSpeedScale = 1f;
        float back = 0f, prev2 = Time.timeScale, maxStep2 = 0f;
        while (back < 4f)
        {
            yield return null; back += Time.unscaledDeltaTime;
            // 停止(カード選択等)の前後の比較は除く(停止中も実時間で自動スローの値は進むため)
            if (TimeControl.IsPaused || TimeControl.IsPresentationDriving || wasPausedLastFrame) { prev2 = Time.timeScale; wasPausedLastFrame = TimeControl.IsPaused || TimeControl.IsPresentationDriving; continue; }
            maxStep2 = Mathf.Max(maxStep2, Mathf.Abs(Time.timeScale - prev2)); prev2 = Time.timeScale;
        }
        L($"[SlowDown] back to v={pc.CurrentAutoRunSpeed:F2}: timeScale={Time.timeScale:F3} maxStepPerFrame={maxStep2:F4}");
        Check(Mathf.Approximately(Time.timeScale, 1f) && maxStep2 < 0.02f, "returns smoothly to 1 when speed drops");

        // ヒステリシス: 開始速度の直前を行き来しても切り替わりを繰り返さない
        float baseSpeed = pc.CurrentAutoRunSpeed / Mathf.Max(0.01f, PlayerController.DebugSpeedScale);
        int flips = 0; float lastTarget = slow.TargetScale; float ht = 0f;
        while (ht < 4f)
        {
            PlayerController.DebugSpeedScale = (slow.startSpeed + (Mathf.Sin(ht * 6f) * 0.2f)) / Mathf.Max(0.01f, baseSpeed);
            bool on = slow.TargetScale < 0.9999f;
            if (on != (lastTarget < 0.9999f)) flips++;
            lastTarget = slow.TargetScale;
            yield return null; ht += Time.unscaledDeltaTime;
        }
        L($"[Hysteresis] speed oscillating around start±0.2 m/s: target on/off flips={flips} final timeScale={Time.timeScale:F3}");
        Check(flips <= 1, "no frequent toggling near threshold");

        // ゲームオーバーで残らない
        PlayerController.DebugSpeedScale = 3f;
        yield return new WaitForSecondsRealtime(slow.transitionTime + 1f);
        float beforeGo = Time.timeScale;
        gm.DebugSetInvincible(false);
        gm.DebugSetLives(1);
        float gt = 0f;
        while (gt < 6f && !gm.IsGameOver) { if (pc.ShieldCharges > 0) pc.TryConsumeShield(); pc.TakeDamage(source: "AutoSlowTest"); yield return new WaitForSecondsRealtime(0.25f); gt += 0.25f; }
        yield return new WaitForSecondsRealtime(0.5f);
        L($"[GameOver] before={beforeGo:F3} gameOver={gm.IsGameOver} timeScale={Time.timeScale:F3} auto={TimeControl.AutoScale:F3} current={slow.CurrentAutoScale:F3}");
        Check(beforeGo < 0.8f && gm.IsGameOver && Mathf.Approximately(Time.timeScale, 1f) && Mathf.Approximately(TimeControl.AutoScale, 1f), "game over leaves no slow state");
        PlayerController.DebugSpeedScale = 1f;

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../AutoSlowAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }

    bool levelUpObserved; float minDuringLevelUp = 1f; bool wasPausedLastFrame;

    IEnumerator AutoPickLevelUp()
    {
        while (true)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.IsRewardSequenceWaitingForSelection)
            {
                levelUpObserved = true;
                minDuringLevelUp = Mathf.Min(minDuringLevelUp, Time.timeScale);
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.15f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.3f);
                continue;
            }
            yield return null;
        }
    }

    // 入力はちょうど1フレームだけ(2フレーム続くと二段ジャンプが混ざって計測がぶれる)。
    // 停止中のフレームはPlayerControllerが入力を読まないので、動いているフレームで読まれるまで保持する。
    IEnumerator Flick(PlayerController pc, PlayerController.FlickDirection dir)
    {
        float t = 0f;
        while (Time.timeScale <= 0f && t < 2f) { yield return null; t += Time.unscaledDeltaTime; }
        pc.debugInjectFlick = dir;
        yield return null;
        pc.debugInjectFlick = null;
    }

    // [0]=ジャンプ高さ [1]=滞空(ゲーム内秒) [2]=攻撃判定の持続(ゲーム内秒) [3]=ゲーム内1秒あたりの距離 [4]=その時の倍率
    // 計測中は穴越えの自動ジャンプを止め、前方に穴の無い区間まで待つ
    bool measuring;
    IEnumerator WaitClearAhead(PlayerController pc, float distance)
    {
        var tm = TerrainManager.Instance;
        float t = 0f;
        while (tm != null && t < 30f)
        {
            bool pit = false;
            for (float d = -1f; d <= distance; d += 0.5f) if (tm.IsNearPit(pc.transform.position.x + d, 0.3f)) { pit = true; break; }
            if (!pit && pc.IsGrounded && !pc.IsAttacking) break;
            yield return null; t += Time.unscaledDeltaTime;
        }
    }

    // 計測を乱さないよう、地形と一緒に配置された敵も消し続ける(被弾リアクション中は前進が止まるため)
    IEnumerator ClearEnemies()
    {
        while (true)
        {
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Destroy(e.gameObject);
            yield return new WaitForSecondsRealtime(0.2f);
        }
    }

    IEnumerator JumpPits(PlayerController pc)
    {
        while (true)
        {
            var tm = TerrainManager.Instance;
            if (!measuring && tm != null && pc.IsGrounded && Time.timeScale > 0f)
            {
                float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
                if (tm.IsNearPit(pc.transform.position.x + lead, 0.4f)) yield return Flick(pc, PlayerController.FlickDirection.Up);
            }
            yield return null;
        }
    }

    IEnumerator MeasureSet(PlayerController pc, float[] outv)
    {
        // 距離は攻撃やジャンプの影響が無い区間で先に測る
        yield return WaitClearAhead(pc, pc.CurrentAutoRunSpeed * 2.4f + 2f);
        measuring = true;
        yield return new WaitForSeconds(0.3f);
        double d0 = pc.DistanceExact; float g0 = Time.time;
        float vSum = 0f; int vN = 0; int reactFrames = 0, airFrames = 0;
        while (Time.time - g0 < 1.5f)
        {
            vSum += pc.CurrentAutoRunSpeed; vN++;
            if (pc.IsReacting) reactFrames++;
            if (!pc.IsGrounded) airFrames++;
            yield return null;
        }
        outv[3] = (float)((pc.DistanceExact - d0) / Mathf.Max(0.001f, Time.time - g0));
        L($"    window: avg run speed {vSum / Mathf.Max(1, vN):F2} reactFrames={reactFrames} airFrames={airFrames}/{vN}");
        outv[3] -= vSum / Mathf.Max(1, vN); // 走行速度との差(0に近いほど水増し/目減りが無い)
        outv[4] = Time.timeScale;
        measuring = false;
        yield return WaitClearAhead(pc, pc.CurrentAutoRunSpeed * 1.6f + 2f);
        measuring = true;
        var j = new float[2]; yield return MeasureJump(pc, j);
        measuring = false;
        var a = new float[1]; yield return MeasureAttack(pc, a);
        outv[0] = j[0]; outv[1] = j[1]; outv[2] = a[0];
    }

    // [0]=最高到達(足元からの高さ) [1]=滞空のゲーム内時間
    IEnumerator MeasureJump(PlayerController pc, float[] outv)
    {
        float wt = 0f;
        while ((!pc.IsGrounded || pc.IsAttacking) && wt < 3f) { yield return null; wt += Time.unscaledDeltaTime; }
        float groundY = pc.transform.position.y;
        yield return Flick(pc, PlayerController.FlickDirection.Up);
        groundY = pc.transform.position.y - 0f; // (Flickのフレームで既に少し上昇している場合がある)
        // 上昇中の軌道だけを比べる(着地先の地形/上ルートの足場で変わる部分を含めない):
        // [0]=ゲーム内0.3秒後の高さ [1]=頂点に達するまでのゲーム内時間
        float g0 = Time.time, maxY = groundY, apexT = 0f, h03 = -1f; float t = 0f; float lastY = groundY;
        while (t < 3f)
        {
            float gy = Time.time - g0;
            float y = pc.transform.position.y;
            if (h03 < 0f && gy >= 0.3f) h03 = y - groundY;
            if (y > maxY) { maxY = y; apexT = gy; }
            if (gy > 0.35f && y < lastY - 0.001f) break; // 下降に転じた
            lastY = y;
            yield return null; t += Time.unscaledDeltaTime;
        }
        outv[0] = maxY - groundY; // 頂点の高さ(上昇だけなので着地先の地形に左右されない)
        outv[1] = apexT;
        yield return new WaitForSeconds(0.4f);
    }

    // 前攻撃の判定が有効だったゲーム内時間
    IEnumerator MeasureAttack(PlayerController pc, float[] outv)
    {
        yield return new WaitForSeconds(0.5f);
        yield return Flick(pc, PlayerController.FlickDirection.Forward);
        float on = 0f, t = 0f;
        while (t < 1.2f) // ゲーム内時間で待つ(スローの有無で後の区間がずれないように)
        {
            if (pc.attackHitbox != null && pc.attackHitbox.enabled) on += Time.deltaTime;
            yield return null; t += Time.deltaTime;
        }
        outv[0] = on;
        yield return new WaitForSeconds(0.3f);
    }
}

public static class AutoSlowTestMenu
{
    [MenuItem("Tools/OneMoreMile/Auto Slow Motion Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("AutoSlowTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

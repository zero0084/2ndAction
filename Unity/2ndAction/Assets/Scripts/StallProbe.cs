using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 攻撃が当たった時の「数秒止まる」の切り分け用の軽い計測(2026-10-07)。
//  ・重い処理による止まり: 1フレームの実時間が 0.25 秒を超えた
//  ・ゲーム内時間の止まり: timeScale が 0 のまま 0.4 秒を超えた(HitStop だけが理由の時。ポーズ/カード選択は除く)
//  のどちらかが起きたら、その直前の命中(敵/攻撃の種類/撃破か/同じフレームの命中数)と、止めている理由をログへ1行出す。
//  通常時は命中の記録を数件持つだけ(ログは出さない)。
[DefaultExecutionOrder(-10000)] // フレームの最初に動く: FrameCost の区切り = 前のフレーム全体
public class StallProbe : MonoBehaviour
{
    struct Hit { public float t; public int frame; public string enemy; public string kind; public bool killed; }
    static readonly Queue<Hit> hits = new Queue<Hit>();
    static int hitsThisFrame, killsThisFrame, frameOfCount = -1;
    public static int Stalls { get; private set; }
    public static int CostSpikes { get; private set; }
    // 2026-10-10: 前のフレームの実時間(秒)。Time.unscaledDeltaTime は Unity がなめらかにするので、50〜70ms のフレームでも 17ms と出ることがある
    // (止まりの検出が中くらいの止まりを見逃していた)。フレームの最初(このクラスの Update)で測る
    public static float RealDt { get; private set; }  // 1つの処理が 40ms 以上かかったフレームの数(開発版)      // 確認用: 計測した止まりの回数
    public static float LongestStopSeconds { get; private set; }
    public static string LastStall { get; private set; } = "";
    public static void ResetLongest() { LongestStopSeconds = 0f; }
    static float quietUntil;
    void OnApplicationPause(bool paused) { if (!paused) { quietUntil = Time.realtimeSinceStartup + 1.5f; lastReal = -1; } } // 復帰直後(裏にいた時間)は数えない
    void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => quietUntil = Time.realtimeSinceStartup + 1.5f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<StallProbe>() != null) return;
        var go = new GameObject("[StallProbe]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<StallProbe>();
    }

    public static void NoteHit(string enemy, string kind, bool killed)
    {
        if (frameOfCount != Time.frameCount) { frameOfCount = Time.frameCount; hitsThisFrame = 0; killsThisFrame = 0; }
        hitsThisFrame++; if (killed) killsThisFrame++;
        hits.Enqueue(new Hit { t = Time.realtimeSinceStartup, frame = Time.frameCount, enemy = enemy, kind = kind, killed = killed });
        while (hits.Count > 8) hits.Dequeue();
    }

    public static string LastHitsText()
    {
        var sb = new StringBuilder("hits[");
        foreach (var h in hits) sb.Append($"{h.enemy}/{h.kind}{(h.killed ? "/KILL" : "")}@{Time.realtimeSinceStartup - h.t:F2}s ");
        sb.Append($"] sameFrame {hitsThisFrame} kills {killsThisFrame}");
        return sb.ToString();
    }

    float stopSince = -1f; bool stopLogged;
    int gcPrev = -1;
    double lastReal = -1;

    // 2026-10-10: Unity の内部の計測点(開発版で有効)。重いフレームで、スクリプトの外(描画/読み込みの取り込み/生成/破棄/物理など)のどこに時間が掛かったかを出す
    static readonly string[] markerNames =
    {
        "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "Update.ScriptRunDelayedDynamicFrameRate",
        "FixedUpdate.ScriptRunBehaviourFixedUpdate", "FixedUpdate.Physics2DFixedUpdate", "FixedUpdate.PhysicsFixedUpdate", "PostLateUpdate.FinishFrameRendering",
        "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "GC.Collect", "Loading.UpdatePreloading", "Application.Integrate Assets in Background",
        "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.PlayerUpdateCanvases", "PostLateUpdate.PlayerEmitCanvasGeometry", "GUI.Repaint", "Instantiate",
        "Loading.ReadObject", "Shader.CreateGPUProgram", "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.ParticleSystemBeginUpdateAll",
        "PostLateUpdate.ParticleSystemEndUpdateAll", "Initialization.AsyncUploadTimeSlicedUpdate", "PostLateUpdate.ExecuteGameCenterCallbacks", "PreUpdate.AudioUpdate"
    };
    Unity.Profiling.ProfilerRecorder[] recorders;
    void OnDisable() { if (recorders != null) foreach (var r in recorders) r.Dispose(); recorders = null; }
    float nextScan;
    // 計測点は使われた時に登録されるので、見つからない物は10秒ごとに探し直す(名前で探す。分類は問わない)
    void EnsureRecorders()
    {
        if (!Debug.isDebugBuild || Time.realtimeSinceStartup < nextScan) return;
        nextScan = Time.realtimeSinceStartup + 10f;
        if (recorders == null) recorders = new Unity.Profiling.ProfilerRecorder[markerNames.Length];
        bool missing = false;
        foreach (var r in recorders) if (!r.Valid) missing = true;
        if (!missing) return;
        var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var h in handles)
        {
            string n = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h).Name;
            int i = System.Array.IndexOf(markerNames, n);
            if (i < 0 || recorders[i].Valid) continue;
            try { recorders[i] = new Unity.Profiling.ProfilerRecorder(h, 2, Unity.Profiling.ProfilerRecorderOptions.Default); recorders[i].Start(); }
            catch { }
        }
    }
    string MarkerText(float minMs)
    {
        if (recorders == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < recorders.Length; i++)
        {
            var r = recorders[i];
            if (!r.Valid || r.Count == 0) continue;
            double ms = r.LastValue / 1e6; // 前のフレーム(= 重かったフレーム)の値
            if (ms >= minMs) sb.Append(markerNames[i]).Append('=').Append(ms.ToString("F0")).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        EnsureRecorders();
        // 2026-10-10: 重いフレームでは GC の回数/ヒープの大きさ/時間を食った処理(FrameCost)も出す
        int gc = System.GC.CollectionCount(0);
        int gcDelta = gcPrev < 0 ? 0 : gc - gcPrev;
        gcPrev = gc;
        FrameCost.EndFrame();
        double nowReal = Time.realtimeSinceStartupAsDouble; float realDt = lastReal > 0 ? (float)(nowReal - lastReal) : 0f; lastReal = nowReal;
        RealDt = realDt > 0f ? realDt : dt;
        dt = Mathf.Max(dt, realDt); // 重いフレームの判定も実時間で
        // 開発版: 止まりにならなくても、1つの処理が 40ms 以上かかったフレームは記録する(実機ではその数倍になる)
        if (FrameCost.LastMaxMs >= 40.0 && Debug.isDebugBuild && Time.realtimeSinceStartup > quietUntil) { CostSpikes++; Debug.Log($"[Cost] {FrameCost.LastMaxMs:F0}ms frame {dt * 1000f:F0}ms (real {realDt * 1000f:F0}ms, ts {Time.timeScale:F2}): {FrameCost.LastTop(6, 5f)}"); }
        if (dt > 0.25f && Time.frameCount > 30 && Time.realtimeSinceStartup > quietUntil) // シーンの読み直し直後は数えない
            Report($"heavy frame {dt:F2}s gc+{gcDelta} heap {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576}MB/{UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576}MB cost[{FrameCost.LastTop(6, 5f)}] unity[{MarkerText(10f)}]");
        // ゲーム内時間の停止(HitStop だけが理由の時)
        bool hitStopOnly = Time.timeScale <= 0f && HitStop.IsActive && TimeControl.ActiveReasonCount <= HitStop.ActiveCount;
        if (hitStopOnly)
        {
            if (stopSince < 0f) { stopSince = Time.realtimeSinceStartup; stopLogged = false; }
            float s = Time.realtimeSinceStartup - stopSince;
            LongestStopSeconds = Mathf.Max(LongestStopSeconds, s);
            if (!stopLogged && s > 0.4f) { stopLogged = true; Report($"time stopped by HitStop for {s:F2}s"); }
        }
        else stopSince = -1f;
    }

    static void Report(string what)
    {
        Stalls++;
        LastStall = $"{what} | hitstops {HitStop.ActiveCount} reasons {TimeControl.ActiveReasonCount} [{TimeControl.DescribeActiveReasons()}] | {LastHitsText()}";
        Debug.LogWarning("[Stall] " + LastStall);
        FreezeDiagnostics.LogEvent("[Stall] " + LastStall);
    }
}

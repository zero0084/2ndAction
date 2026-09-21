#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor専用の自動確認(Floating Origin/高速走行の視認性補正)。EditorPrefs "FoAutoTest"==1 のPlay時に1回だけ走り、
// プロジェクト直下の FloatingOriginAutoTest.txt へ結果を書く。
public class FloatingOriginAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("FoAutoTest", 0) != 1) return;
        EditorPrefs.SetInt("FoAutoTest", 0);
        new GameObject("FoAutoTest").AddComponent<FloatingOriginAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int errors;
    void L(string s) { log.AppendLine(s); Debug.Log("[FoAutoTest] " + s); }
    void OnLog(string msg, string stack, LogType t) { if (t == LogType.Exception || t == LogType.Error) { errors++; log.AppendLine("  !! " + t + ": " + msg); } }

    static int ChunkCount()
    {
        var f = typeof(TerrainManager).GetField("chunks", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = f.GetValue(TerrainManager.Instance) as System.Collections.ICollection;
        return list != null ? list.Count : -1;
    }

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        // 数値の確認: floatの刻み幅と、1フレームの移動量(60fps/基礎速度と2倍速)の丸め誤差
        foreach (float big in new[] { 1000f, 10000f, 50000f, 100000f })
        {
            float ulp = Mathf.Abs(System.BitConverter.Int32BitsToSingle(System.BitConverter.SingleToInt32Bits(big) + 1) - big);
            float step = pc.runSpeed * 2f / 60f;
            float acc = big, worst = 0f;
            for (int i = 0; i < 600; i++) { float before = acc; acc += step; worst = Mathf.Max(worst, Mathf.Abs((acc - before) - step) / step); }
            L($"float precision at x={big}: ulp={ulp:F5}  per-frame step {step:F4} worst relative error={worst * 100f:F1}%");
        }

        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        L($"run started: HasStarted={gm.HasStarted} countdown={gm.CountdownActive}");
        gm.DebugSetLives(999);
        yield return new WaitForSeconds(1f);

        L($"baseline: x={pc.transform.position.x:F2} dist={pc.DistanceFromStart:F2} ratio={pc.SpeedRatio:F3} speed={pc.CurrentAutoRunSpeed:F3} chunks={ChunkCount()}");

        // --- 論理ワープ(50,000m)
        gm.expPerMeter = 0f; // 距離ワープでレベルアップの選択画面(停止)が出ないように
        float warpTarget = EditorPrefs.GetInt("FoHold", 0) == 1 ? 1000000f : 50000f;
        gm.DebugWarpToDistance(warpTarget);
        BossManager.Instance.RestoreNextBossDistance(warpTarget);
        if (EditorPrefs.GetInt("FoHold", 0) == 1) { EditorPrefs.SetInt("FoHold", 0); var dm = typeof(GameManager).GetProperty("DebugMode"); if (dm != null && dm.GetSetMethod(true) != null) dm.SetValue(gm, false); yield break; } // 目視確認用: ワープ後はそのまま走らせる
        yield return new WaitForSeconds(0.5f);
        L($"after LogicalWarp: playerX={pc.transform.position.x:F2} dist={pc.DistanceFromStart:F2} MaxDistance={gm.MaxDistance:F1} Offset={FloatingOrigin.Offset:F1} ratio={pc.SpeedRatio:F3} chunks={ChunkCount()} height={(TerrainManager.Instance.GetHeightAt(pc.transform.position.x)?.ToString("F2") ?? "null")}");

        // --- 走行の安定性(速度のばらつき/カメラ-プレイヤー間の揺れ)
        var cam = FindFirstObjectByType<CameraFollow>();
        yield return Measure("50000m相当・最高速", pc, cam, 5f);

        // --- 実際のシフト(座標を戻す)を2回起こして、論理距離/カメラ/地形が連続かを確認
        for (int round = 0; round < 3; round++)
        {
            float targetX = pc.transform.position.x; // 動かさず、走行中に直接シフトを起こす
            float distBefore = pc.DistanceFromStart;
            int shiftsBefore = FloatingOrigin.Instance.ShiftCount;
            int chunksBefore = ChunkCount();
            FloatingOrigin.Instance.Shift(1024f);
            float yBefore = pc.transform.position.y;
            float dist0 = pc.DistanceFromStart, camOff0 = cam.transform.position.x - pc.transform.position.x;
            float lastL = distBefore, maxJump = 0f, maxCamJump = 0f, lastCamOff = camOff0;
            double offsetBefore = FloatingOrigin.Offset;
            float tt = 0f, maxDt = 0f, firstJump = -1f, firstCam = -1f;
            while (tt < 1.5f)
            {
                yield return null; tt += Time.deltaTime; maxDt = Mathf.Max(maxDt, Time.deltaTime);
                float L2 = pc.DistanceFromStart;
                if (!float.IsNaN(lastL)) maxJump = Mathf.Max(maxJump, Mathf.Abs(L2 - lastL - pc.CurrentAutoRunSpeed * Time.deltaTime));
                if (firstJump < 0f) firstJump = Mathf.Abs(L2 - distBefore - pc.CurrentAutoRunSpeed * Time.deltaTime);
                lastL = L2;
                float co = cam.transform.position.x - pc.transform.position.x;
                maxCamJump = Mathf.Max(maxCamJump, Mathf.Abs(co - lastCamOff)); lastCamOff = co;
                if (firstCam < 0f) firstCam = Mathf.Abs(co - camOff0);
            }
            L($"shift round {round}: direct Shift(1024) at x={targetX:F1}; shifts +{FloatingOrigin.Instance.ShiftCount - shiftsBefore} Offset {offsetBefore:F0}->{FloatingOrigin.Offset:F0} playerX now {pc.transform.position.x:F1}; " +
              $"shift-frame distance err={firstJump:F4} cam err={firstCam:F4}; max per-frame distance discontinuity={maxJump:F4} max camera-offset jump={maxCamJump:F4}; maxFrameTime={maxDt * 1000f:F0}ms; chunks {chunksBefore}->{ChunkCount()} height={(TerrainManager.Instance.GetHeightAt(pc.transform.position.x)?.ToString("F2") ?? "null")}");
        }

        yield return Measure("シフト後", pc, cam, 6f);
        L($"final: x={pc.transform.position.x:F1} dist={pc.DistanceFromStart:F1} MaxDistance={gm.MaxDistance:F1} chunks={ChunkCount()} obstacles={FindObjectsByType<ObstacleController>(FindObjectsSortMode.None).Length} enemies={FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length} errors={errors}");
        Application.logMessageReceived -= OnLog;
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../FloatingOriginAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }

    IEnumerator Measure(string label, PlayerController pc, CameraFollow cam, float seconds)
    {
        var v = new List<float>(); var co = new List<float>(); var sz = new List<float>();
        float lastX = pc.transform.position.x, t = 0f, maxDt = 0f;
        while (t < seconds)
        {
            yield return null; float dt = Time.deltaTime; t += dt; maxDt = Mathf.Max(maxDt, dt);
            float x = pc.transform.position.x;
            if (dt > 0f) v.Add((x - lastX) / dt);
            lastX = x;
            co.Add(cam.transform.position.x - x);
            sz.Add(cam.GetComponent<Camera>().orthographicSize);
        }
        float Mean(List<float> a) { float s = 0; foreach (var f in a) s += f; return s / Mathf.Max(1, a.Count); }
        float Sd(List<float> a) { float m = Mean(a), s = 0; foreach (var f in a) s += (f - m) * (f - m); return Mathf.Sqrt(s / Mathf.Max(1, a.Count)); }
        L($"[{label}] speed mean={Mean(v):F3} sd={Sd(v):F4} (expected {pc.CurrentAutoRunSpeed:F3}); camera-player X offset mean={Mean(co):F3} sd={Sd(co):F5}; ortho size mean={Mean(sz):F3} (blend={cam.SpeedBlend:F2}) frames={v.Count} maxFrameTime={maxDt * 1000f:F0}ms");
    }
}

public static class FloatingOriginTestMenu
{
    [MenuItem("Tools/OneMoreMile/Floating Origin Hold At 50000m (visual check)")]
    static void ArmHold() { EditorPrefs.SetInt("FoAutoTest", 1); EditorPrefs.SetInt("FoHold", 1); }

    [MenuItem("Tools/OneMoreMile/Floating Origin Auto Test On Next Run")]
    static void Arm() { EditorPrefs.SetInt("FoAutoTest", 1); }

    // batchmode用: Mainシーンを開いてPlayし、テスト終了時にEditorを終了する。
    public static void RunBatch()
    {
        EditorPrefs.SetInt("FoAutoTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

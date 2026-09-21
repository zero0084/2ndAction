#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor専用の自動確認(自然洞窟: 通路の最低間隔/ジャンプ連打での天井接触)。
// EditorPrefs "CavePassageTest"==1 のPlay時に1回だけ走り、CavePassageAutoTest.txt を書く。
public class CavePassageAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("CavePassageTest", 0) != 1) return;
        EditorPrefs.SetInt("CavePassageTest", 0);
        new GameObject("CavePassageTest").AddComponent<CavePassageAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int errors;
    void L(string s) { log.AppendLine(s); Debug.Log("[CavePassage] " + s); }
    void OnLog(string msg, string stack, LogType t) { if (t == LogType.Exception || t == LogType.Error) { errors++; log.AppendLine("  !! " + t + ": " + msg); } }

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetLives(999);
        yield return new WaitForSeconds(1f);
        if (EditorPrefs.GetInt("CaveHold", 0) == 1) { EditorPrefs.SetInt("CaveHold", 0); yield break; } // 目視確認用: 洞窟ランをそのまま走らせる
        var tm = TerrainManager.Instance;
        var cave = FindFirstObjectByType<CaveStage>();
        var fire = typeof(PlayerController).GetMethod("FireJump", BindingFlags.NonPublic | BindingFlags.Instance);
        L($"cave active={cave.Active} stage={gm.ActiveRunStageId} minPassage={cave.MinPassageHeight:F2}");

        float worstOver = 0f; int neverLanded = 0; int samples = 0, gapViol = 0, obstViol = 0, upperSamples = 0, ceilViol = 0, underFloor = 0, stalls = 0, checkedObst = 0;
        float minGap = 99f, minUpperGap = 99f, minObstGap = 99f, worstGapX = 0f;
        float need = cave.MinPassageHeight - 0.001f;
        var obstSpawner = FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None);
        float passClear = 3f; foreach (var s in obstSpawner) if (s.obstacleStageId == "natural_cave") passClear = s.caveObstaclePassClearance;

        for (int iter = 0; iter < 70; iter++)
        {
            // 先へ飛ばして新しい地形/天井を生成させ、その区間を走りながらジャンプ連打(=上攻撃で天井接触)
            Vector3 p = pc.transform.position; p.x += 130f; pc.transform.position = p;
            float lastX = p.x, stallT = 0f, t = 0f; bool settled = false;
            while (t < 1.4f)
            {
                yield return null; float dt = Time.deltaTime; t += dt;
                if (Random.value < 0.35f) fire.Invoke(pc, null);
                float x = pc.transform.position.x, y = pc.transform.position.y;
                float? lim = tm.GetCeilingLimitY(x);
                if (t > 0.2f && lim.HasValue && y > lim.Value + 0.01f) { ceilViol++; worstOver = Mathf.Max(worstOver, y - lim.Value); }
                float? g = tm.GetHeightAt(x);
                if (pc.IsGrounded) settled = true;
                if (settled && g.HasValue && y < g.Value - 0.3f && !pc.IsGrounded) underFloor++;
                if (dt > 0f && (x - lastX) / dt < pc.CurrentAutoRunSpeed * 0.3f && !gm.IsGameOver) stallT += dt; else stallT = 0f;
                if (stallT > 0.5f) { stalls++; stallT = 0f; }
                lastX = x;
            }
            if (!settled) neverLanded++;
            // 監査: プレイヤー周辺(後方120〜前方45)の床-天井
            float px = pc.transform.position.x;
            for (float x = px - 120f; x <= px + 45f; x += 0.5f)
            {
                float? ce = tm.GetEffectiveCeilingHeightAt(x);
                if (!ce.HasValue) continue;
                float floor = tm.GetFloorTopAt(x);
                float gap = ce.Value - floor;
                samples++;
                if (tm.GetSkyHeightAt(x).HasValue) { upperSamples++; minUpperGap = Mathf.Min(minUpperGap, gap); }
                if (gap < minGap) { minGap = gap; worstGapX = x; }
                if (gap < need) gapViol++;
            }
            foreach (var oc in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None))
            {
                var r = oc.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                float ox = r.bounds.center.x;
                if (ox < px - 120f || ox > px + 45f) continue;
                float? ce = tm.GetEffectiveCeilingHeightAt(ox);
                if (!ce.HasValue) continue;
                checkedObst++;
                float gap = ce.Value - r.bounds.max.y;
                minObstGap = Mathf.Min(minObstGap, gap);
                if (gap < passClear - 0.05f) obstViol++;
            }
        }
        L($"samples={samples} (upper-route samples={upperSamples}) minFloorToCeiling={minGap:F2} at x={worstGapX:F1}; minUpperRouteGap={minUpperGap:F2}; passage violations={gapViol} (need >= {need:F2})");
        L($"obstacle checks={checkedObst} minObstacleTopToCeiling={minObstGap:F2} violations={obstViol} (need >= {passClear:F2}); spikeHits(total)={CaveStage.SpikeHitCount}");
        L($"player: ceiling-clamp violations={ceilViol} (worst over={worstOver:F3}, teleport warm-up frames excluded) under-floor frames={underFloor} stalls={stalls} neverLandedAfterTeleport={neverLanded}/70 final x={pc.transform.position.x:F0} errors={errors}");
        Application.logMessageReceived -= OnLog;
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../CavePassageAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class CavePassageTestMenu
{
    [MenuItem("Tools/OneMoreMile/Cave Run (visual check)")]
    static void Hold() { EditorPrefs.SetInt("CavePassageTest", 1); EditorPrefs.SetInt("CaveHold", 1); }

    public static void RunBatch()
    {
        EditorPrefs.SetInt("CavePassageTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

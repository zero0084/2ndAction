#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor専用の自動確認(地形だけでダメージを受けないか)。EditorPrefs "TerrDmgTest"==1(値=ステージID用の別キー)のPlay時に走り、
// TerrainDamageAutoTest.txt を書く。普通に走るだけで壁ハザード等のダメージが起きた位置と地形の種類を記録する。
public class TerrainDamageAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("TerrDmgTest", 0) != 1) return;
        EditorPrefs.SetInt("TerrDmgTest", 0);
        new GameObject("TerrDmgTest").AddComponent<TerrainDamageAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[TerrDmg] " + s); }
    static float NearestDist<T>(Vector3 p) where T : Component { float b = 999f; foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None)) b = Mathf.Min(b, Vector2.Distance(p, c.transform.position)); return b; }
    int wallHits; float lastWallTime = -9f;
    string lastWallDesc = "";

    void OnWall(TerrainWallHazard h, bool walked)
    {
        wallHits++; lastWallTime = Time.time;
        var pc = PlayerController.Instance;
        float x = pc.transform.position.x;
        string parent = h.transform.parent != null ? h.transform.parent.name : "?";
        lastWallDesc = $"WALL({(walked ? "walk" : "fall")}) x={x:F1} y={pc.transform.position.y:F2} speed={pc.CurrentAutoRunSpeed:F1} grounded={pc.IsGrounded} hazardOwner={parent}@{h.transform.parent.position.x:F1},{h.transform.parent.position.y:F1} rotZ={h.transform.parent.eulerAngles.z:F0} :: {TerrainManager.Instance.DebugDescribeAt(x)}";
        var hc = h.GetComponent<BoxCollider2D>(); var pcol = pc.GetComponent<Collider2D>();
        lastWallDesc += $" || hazardBounds x[{hc.bounds.min.x:F1},{hc.bounds.max.x:F1}] y[{hc.bounds.min.y:F1},{hc.bounds.max.y:F1}] localX={h.transform.localPosition.x:F1} size={hc.size.x:F1}x{hc.size.y:F1} owner/len~{h.transform.parent.childCount} playerBounds x[{pcol.bounds.min.x:F1},{pcol.bounds.max.x:F1}] y[{pcol.bounds.min.y:F1},{pcol.bounds.max.y:F1}]";
        L(lastWallDesc);
    }

    IEnumerator Start()
    {
        TerrainWallHazard.Triggered += OnWall;
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        string stage = EditorPrefs.GetString("TerrDmgStage", "wasteland_road");
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetLives(999);
        gm.expPerMeter = 0f;
        var tm = TerrainManager.Instance;
        // 敵/障害物のダメージを排除して地形だけを見る
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<UpperRouteEnemySpawner>(FindObjectsSortMode.None)) s.enabled = false;
        tm.enemySpawnChance = 0f;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false; // 距離ゲートのボスで止まらないように
        Time.timeScale = 6f;
        L($"stage={gm.ActiveRunStageId} start");
        int otherLoss = 0; int lives = gm.Lives; int jumps = 0;
        var fire = typeof(PlayerController).GetMethod("FireJump", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int phase = 0; phase < 2; phase++)
        {
            if (phase == 1) { pc.speedUpStartDistance = 0f; pc.speedUpPer100m = 5f; L($"phase 2: speed ratio {pc.SpeedRatio:F2}"); }
            float phaseStart = pc.transform.position.x; float tt = 0f;
            while (tt < 50f)
            {
                yield return null; tt += Time.unscaledDeltaTime;
                if (Random.value < 0.004f) { fire.Invoke(pc, null); jumps++; }
                if (gm.Lives < lives)
                {
                    bool wall = Time.time - lastWallTime < 0.2f;
                    if (!wall) { otherLoss++; L($"OTHER damage x={pc.transform.position.x:F1} nearestEnemy={NearestDist<EnemyController>(pc.transform.position):F1} nearestObstacle={NearestDist<ObstacleController>(pc.transform.position):F1} bossPhase={BossManager.Instance.IsBossPhase} y={pc.transform.position.y:F2} grounded={pc.IsGrounded} :: {tm.DebugDescribeAt(pc.transform.position.x)}"); }
                    lives = gm.Lives;
                }
            }
            L($"state: timeScale={Time.timeScale} gameOver={gm.IsGameOver} started={gm.HasStarted} x={pc.transform.position.x:F0} ratio={pc.SpeedRatio:F2}");
            L($"phase {phase}: distance covered {(pc.transform.position.x - phaseStart):F0}u, wallHits so far={wallHits}, otherDamage={otherLoss}, jumps={jumps}");
        }
        L($"RESULT stage={stage} wallHits={wallHits} otherDamage={otherLoss}");
        Time.timeScale = 1f;
        System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../TerrainDamageAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class TerrainDamageTestMenu
{
    public static void RunWasteland() { Run("wasteland_road"); }
    public static void RunCave() { Run("natural_cave"); }
    public static void RunSky() { Run("sky_corridor"); }
    static void Run(string stage)
    {
        EditorPrefs.SetInt("TerrDmgTest", 1);
        EditorPrefs.SetString("TerrDmgStage", stage);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Editor専用: 地形の継ぎ目(平地/坂/穴の縁/上ルートの起点・終点)をカメラで撮影してPNG保存する(見た目の検証用)。
// 保存先: プロジェクト直下 TerrainShots/<stage>_<n>_<kind>.png
public class TerrainShotAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("TerrShotTest", 0) != 1) return;
        EditorPrefs.SetInt("TerrShotTest", 0);
        new GameObject("TerrShotTest").AddComponent<TerrainShotAutoTest>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        string stage = EditorPrefs.GetString("TerrShotStage", "wasteland_road");
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetLives(999);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        var tm = TerrainManager.Instance;
        tm.enemySpawnChance = 0f;
        // 先の地形を生成させる
        for (int i = 0; i < 8; i++)
        {
            var p = pc.transform.position; p.x += 60f; pc.transform.position = p; yield return new WaitForSeconds(0.3f);
        }
        Time.timeScale = 0f;
        var cf = FindFirstObjectByType<CameraFollow>(); cf.enabled = false;
        Camera cam = cf.GetComponent<Camera>();
        string dir = System.IO.Path.Combine(Application.dataPath, "../TerrainShots"); System.IO.Directory.CreateDirectory(dir);
        var joints = tm.DebugListJoints();
        var seen = new System.Collections.Generic.Dictionary<string, int>();
        int n = 0;
        var rt = new RenderTexture(1280, 720, 24);
        foreach (var kv in joints)
        {
            seen.TryGetValue(kv.Value, out int cnt); if (cnt >= 2) continue; seen[kv.Value] = cnt + 1;
            float gy = tm.GetGroundLineAt(kv.Key);
            cam.orthographicSize = 7f; // 継ぎ目を拡大して見る(通常は約11.7)
            cam.transform.position = new Vector3(kv.Key, gy + 1.5f, cam.transform.position.z);
            var pp = pc.transform.position; pp.x = kv.Key - 40f; pc.transform.position = pp; // プレイヤーを写さない
            yield return null; yield return null;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{stage}_{n:00}_{kv.Value.Replace(">", "")}_{kv.Key:F0}.png"), tex.EncodeToPNG());
            Destroy(tex); n++;
        }
        Debug.Log($"[TerrShot] saved {n} shots");
        Time.timeScale = 1f;
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class TerrainShotTestMenu
{
    public static void RunWasteland() { Run("wasteland_road"); }
    public static void RunCave() { Run("natural_cave"); }
    public static void RunSky() { Run("sky_corridor"); }
    static void Run(string stage)
    {
        EditorPrefs.SetInt("TerrShotTest", 1); EditorPrefs.SetString("TerrShotStage", stage);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

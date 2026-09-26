#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 攻撃モーション見直し(2026-09-26) - Editor専用の目視確認用ツアー。指定キャラで走り出し、
// Run数コマと前方射撃/攻撃のコンボ各段をGame画面ごと撮影して MotionShots/<キャラ>_<名前>.png に保存する。
// 撮影時のプレイヤー画面座標を shots.txt に書き出す(後で切り抜いて拡大確認するため)。
public class MotionVisualTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string c = EditorPrefs.GetString("MotionVisualTourChar", "");
        if (c == "") return;
        EditorPrefs.SetString("MotionVisualTourChar", "");
        var tour = new GameObject("MotionVisualTour").AddComponent<MotionVisualTour>();
        tour.characterId = c;
    }

    string characterId;
    string dir;
    readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    void Shot(string name)
    {
        var pc = PlayerController.Instance;
        var cam = Camera.main;
        Vector3 sp = cam != null ? cam.WorldToScreenPoint(pc.transform.position) : Vector3.zero;
        Vector3 top = cam != null ? cam.WorldToScreenPoint(pc.transform.position + Vector3.up * 1.2f) : Vector3.zero;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{characterId}_{name}.png"));
        log.AppendLine($"{characterId}_{name}.png {sp.x:F0} {sp.y:F0} {top.y - sp.y:F0} {Screen.width} {Screen.height}");
    }

    IEnumerator Start()
    {
        dir = System.IO.Path.Combine(Application.dataPath, "../MotionShots");
        System.IO.Directory.CreateDirectory(dir);
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        gm.SetSelectedCharacter(characterId);
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        StartCoroutine(Pump());
        yield return new WaitForSeconds(1.5f);

        // Run - 1/16秒刻みで16枚(8fpsなら2周期ぶん、全コマが最低1回は写る)
        for (int i = 0; i < 16; i++) { Shot($"run_{i:00}"); yield return new WaitForSeconds(1f / 16f); }

        var pc = PlayerController.Instance;
        // 前方攻撃3段 - フリック直後(2フレーム後)に撮影、コンボ窓内で次を入力
        for (int k = 1; k <= 3; k++)
        {
            pc.debugInjectFlick = PlayerController.FlickDirection.Forward;
            yield return null;
            pc.debugInjectFlick = null;
            yield return null; yield return null;
            Shot($"attack{k}");
            yield return new WaitForSeconds(0.14f);
        }
        yield return new WaitForSeconds(0.8f);
        pc.debugInjectFlick = PlayerController.FlickDirection.Backward;
        yield return null;
        pc.debugInjectFlick = null;
        yield return null; yield return null;
        Shot("backward");
        yield return new WaitForSeconds(0.8f);

        System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "shots.txt"), log.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"done_{characterId}.txt"), System.DateTime.Now.ToString());
        EditorApplication.isPlaying = false;
    }

    IEnumerator Pump()
    {
        // ScreenCaptureでEditorのフレームが一瞬止まり、ヒッチ診断オーバーレイが撮影を覆うため閉じ続ける。
        var fdOverlay = typeof(FreezeDiagnostics).GetField("showOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        var bdOverlay = typeof(BossDiagnostics).GetField("showSnapshotOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        while (true)
        {
            fdOverlay?.SetValue(null, false);
            bdOverlay?.SetValue(null, false);
            yield return null;
        }
    }
}

public static class MotionVisualTourMenu
{
    static void Launch(string characterId)
    {
        EditorPrefs.SetString("MotionVisualTourChar", characterId);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/OneMoreMile/Motion Visual Tour: Gunslinger")]
    public static void RunGunslinger() => Launch("gunslinger");

    [MenuItem("Tools/OneMoreMile/Motion Visual Tour: Dual Blade")]
    public static void RunDualBlade() => Launch("dual_blade");
}
#endif

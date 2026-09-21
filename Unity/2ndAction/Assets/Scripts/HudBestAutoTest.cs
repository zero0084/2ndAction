#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor専用: マップ別BEST/cm表示/km/h換算の確認。結果は HudBestAutoTest.txt。
public class HudBestAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("HudBestTest", 0) != 1) return;
        EditorPrefs.SetInt("HudBestTest", 0);
        new GameObject("HudBestTest").AddComponent<HudBestAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[HudBest] " + s); }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        var fmt = typeof(GameManager).GetMethod("FormatDistanceExact", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (double d in new[] { 0.0, 6.25, 1234.56, 67694.0, 1000000.0, 9999999.99, 12.999 })
            L($"format {d} -> {fmt.Invoke(null, new object[] { d })}");
        L($"kmh: 5.75 m/s -> {GameManager.SpeedKmh(5.75f):F1}, 10 m/s -> {GameManager.SpeedKmh(10f):F1}, 34.83 m/s -> {GameManager.SpeedKmh(34.83f):F1}");
        // 精度: 1,000,000m地点でのcm(double)保持
        double big = 1000000.0 + 0.37; L($"double precision at 1e6: {big:F2}; float would give {(float)big:F2}");

        // マップ別BEST
        foreach (string k in new[] { "natural_cave", "wasteland_road", "sky_corridor" }) PlayerPrefs.DeleteKey("BestDistance_v2_" + k);
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetLives(999); gm.expPerMeter = 0f;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        L($"start: stage={gm.ActiveRunStageId} best(cave)={gm.GetStageBest("natural_cave"):F2} best(wasteland)={gm.GetStageBest("wasteland_road"):F2} best(sky)={gm.GetStageBest("sky_corridor"):F2}");
        FloatingOrigin.LogicalWarp(1234f - pc.DistanceFromStart);
        yield return new WaitForSeconds(1.0f);
        L($"exact distance now={gm.MaxDistanceExact:F3} (float MaxDistance={gm.MaxDistance:F3}); speed={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F1} km/h ratio={pc.SpeedRatio:F2}");
        typeof(GameManager).GetMethod("FinishRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        L($"after FinishRun: cave={gm.GetStageBest("natural_cave"):F3}  wasteland={gm.GetStageBest("wasteland_road"):F3}  sky={gm.GetStageBest("sky_corridor"):F3}  IsNewBest={gm.IsNewBestDistance}");
        L($"raw prefs: cave='{PlayerPrefs.GetString("BestDistance_v2_natural_cave", "")}' wasteland='{PlayerPrefs.GetString("BestDistance_v2_wasteland_road", "")}'");
        foreach (string k in new[] { "natural_cave", "wasteland_road", "sky_corridor" }) PlayerPrefs.DeleteKey("BestDistance_v2_" + k);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../HudBestAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class HudBestTestMenu
{
    public static void RunBatch()
    {
        EditorPrefs.SetInt("HudBestTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

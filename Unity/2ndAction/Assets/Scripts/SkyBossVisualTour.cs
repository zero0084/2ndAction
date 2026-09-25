#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 天空回廊ボス追加(2026-09-25) - Editor専用の目視確認用ツアー。天空回廊で各ボスを1体ずつ出し、
// 登場演出〜攻撃の途中を一定間隔でGame画面ごと撮影して SkyBossShots/<ボス>_<n>.png に保存する。
// (撃破→報酬カードの暗転に被らないよう、撮影はすべて撃破前に行う。)
public class SkyBossVisualTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("SkyBossVisualTour", 0) != 1) return;
        EditorPrefs.SetInt("SkyBossVisualTour", 0);
        new GameObject("SkyBossVisualTour").AddComponent<SkyBossVisualTour>();
    }

    IEnumerator Start()
    {
        string dir = System.IO.Path.Combine(Application.dataPath, "../SkyBossShots");
        System.IO.Directory.CreateDirectory(dir);
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        gm.SetSelectedStage("sky_corridor");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        var lives = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        StartCoroutine(Pump(gm, lives));

        var bm = BossManager.Instance;
        var spawn = typeof(BossManager).GetMethod("DebugForceSpawnSky", BindingFlags.NonPublic | BindingFlags.Instance);
        string only = EditorPrefs.GetString("SkyBossVisualTourOnly", "");
        foreach (SkyBossKind kind in System.Enum.GetValues(typeof(SkyBossKind)))
        {
            if (only != "" && !only.Contains(kind.ToString())) continue;
            spawn.Invoke(bm, new object[] { kind, kind == SkyBossKind.Dragon ? 3 : 1 });
            float[] at = { 1.0f, 2.2f, 3.6f, 5.5f, 7.5f, 9.5f, 11.5f, 13.5f };
            float st = Time.time;
            for (int i = 0; i < at.Length; i++)
            {
                while (Time.time - st < at[i]) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{kind}_{i}.png"));
                yield return null;
            }
            foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) w.TakeDamage(99999, w.CenterWorld);
            foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!d.IsDead) d.TakeDamage(99999);
            foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) m.TakeDamage(99999);
            if (kind == SkyBossKind.Phoenix)
            {
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{kind}_rebirth.png"));
                yield return new WaitForSeconds(2.5f);
                foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) w.TakeDamage(99999, w.CenterWorld);
            }
            float ws = Time.unscaledTime;
            while (bm.IsBossPhase && Time.unscaledTime - ws < 15f) yield return null;
            foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None)) Destroy(s.gameObject);
            yield return new WaitForSeconds(0.5f);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done.txt"), System.DateTime.Now.ToString());
        EditorApplication.isPlaying = false;
    }

    IEnumerator Pump(GameManager gm, MethodInfo setLives)
    {
        // 撮影自体(ScreenCapture)でEditorのフレームが一瞬止まり、ヒッチ診断の全画面オーバーレイが
        // 開いて撮影を覆ってしまうため、ツアー中は閉じ続ける(ゲーム側の挙動は変えない)。
        var fdOverlay = typeof(FreezeDiagnostics).GetField("showOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        var bdOverlay = typeof(BossDiagnostics).GetField("showSnapshotOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        while (true)
        {
            fdOverlay?.SetValue(null, false);
            bdOverlay?.SetValue(null, false);
            if (gm.Lives < 50) setLives.Invoke(gm, new object[] { 99 });
            if (gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.3f);
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }
            }
            yield return null;
        }
    }
}

public static class SkyBossVisualTourMenu
{
    [MenuItem("Tools/OneMoreMile/Sky Boss Visual Tour")]
    public static void Run()
    {
        EditorPrefs.SetInt("SkyBossVisualTour", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

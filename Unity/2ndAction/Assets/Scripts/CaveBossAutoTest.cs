#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 自然洞窟ボス追加(2026-09-22) - Editor専用の自動確認。
// 1) ResolveCaveGate(距離→ボスの表)を1〜100(=1,000m〜100,000m)まで走査し、
//    優先順位(10,000m専用 > 5,000m系 > 1,000m系)どおり・重複なしで解決
//    できているかを確認する(実行時オブジェクトは一切生成しない純粋ロジック確認)。
// 2) 実際に11種すべてを1体ずつForce Spawnし、例外が出ないか・HPバー/
//    Hitboxが機能するか・撃破でOnWildBossDefeated〜CheckEncounterComplete
//    まで正しく進んでIsBossPhaseが解除されるかを確認する。
// 結果は CaveBossAutoTest.txt。
public class CaveBossAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("CaveBossTest", 0) != 1) return;
        EditorPrefs.SetInt("CaveBossTest", 0);
        new GameObject("CaveBossTest").AddComponent<CaveBossAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[CaveBossTest] " + s); }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        var im = typeof(GameManager).GetProperty("InvincibleMode");
        if (im != null) im.GetSetMethod(true).Invoke(gm, new object[] { false });
        L($"stage={gm.ActiveRunStageId} character={gm.SelectedCharacterId} lives={gm.Lives}");
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        yield return new WaitForSeconds(0.5f);

        var bm = BossManager.Instance;
        var bmType = typeof(BossManager);

        // ---- 1) ResolveCaveGate走査 ----
        var resolveCave = bmType.GetMethod("ResolveCaveGate", BindingFlags.NonPublic | BindingFlags.Instance);
        L("--- ResolveCaveGate schedule (k=1..100) ---");
        for (int k = 1; k <= 100; k++)
        {
            object[] args = { k, null, null };
            bool ok = (bool)resolveCave.Invoke(bm, args);
            if (!ok) { L($"k={k} ({k * 1000}m): (no gate)"); continue; }
            L($"k={k} ({k * 1000}m): kind={args[1]} count={args[2]}");
        }

        // ---- 2) 実際に11種を1体ずつForce Spawn ----
        var debugSpawnCave = bmType.GetMethod("DebugForceSpawnCave", BindingFlags.NonPublic | BindingFlags.Instance);
        string[] names = System.Enum.GetNames(typeof(CaveBossKind));
        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        for (int i = 0; i < names.Length; i++)
        {
            var kind = (CaveBossKind)i;
            debugSpawnCave.Invoke(bm, new object[] { kind, 1 });
            yield return new WaitForSeconds(0.05f);
            var boss = FindFirstObjectByType<WildBossBase>();
            bool spawned = boss != null;
            L($"[Spawn] {kind}: spawned={spawned} IsBossPhase={bm.IsBossPhase} aliveWild={bm.AliveWildCount}");

            // 少し走らせてAI/Update/BossRigが例外なく動くか確認。
            yield return new WaitForSeconds(1.0f);

            if (boss != null)
            {
                float hpBefore = boss.Hp;
                boss.TakeDamage(99999, boss.CenterWorld);
                yield return new WaitForSeconds(0.05f);
                L($"[Kill] {kind}: hpBefore={hpBefore} IsDead={boss.IsDead}");
            }

            // 撃破後は既存のBoss Reward(カード選択)フローに入る - Time.timeScale=0で
            // 一時停止し、実プレイヤーのタップ(またはRewardCardSequence自身の30秒
            // タイムアウト)を待つ、荒野街道ボスと全く同じ既存の仕組み。自動テストでは
            // 実際にタップして1枚目のカードを選び(1回目=選択、2回目=確定)、実プレイヤー
            // と同じ経路で「撃破→報酬→再開」まで完了することを確認する。
            float waitStart = Time.unscaledTime;
            bool cardHandled = false;
            while (bm.IsBossPhase && Time.unscaledTime - waitStart < 8f)
            {
                if (!cardHandled && gm.IsRewardSequenceWaitingForSelection)
                {
                    var seq = FindFirstObjectByType<RewardCardSequence>();
                    if (seq != null)
                    {
                        seq.OnCardClicked(0);
                        yield return new WaitForSecondsRealtime(0.25f);
                        seq.OnCardClicked(0);
                        cardHandled = true;
                    }
                }
                yield return null;
            }
            L($"[EncounterEnd] {kind}: IsBossPhase={bm.IsBossPhase} (expect False) cardHandled={cardHandled}");
        }

        Application.logMessageReceived -= handler;

        // ---- 3) 撃破後もDistanceが進み続けるか(フリーズしていないか) ----
        float distBefore = gm.MaxDistance;
        yield return new WaitForSeconds(1.0f);
        float distAfter = gm.MaxDistance;
        L($"[ContinueRun] distance {distBefore:F1} -> {distAfter:F1} (advancing={distAfter > distBefore})");

        L("");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../CaveBossAutoTest.txt"), log.ToString());

        if (Application.isBatchMode) EditorApplication.Exit(anyException ? 1 : 0); else EditorApplication.isPlaying = false;
    }
}

public static class CaveBossTestMenu
{
    [MenuItem("Tools/OneMoreMile/Cave Boss Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("CaveBossTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

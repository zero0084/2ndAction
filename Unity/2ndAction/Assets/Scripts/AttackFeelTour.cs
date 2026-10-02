#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// 攻撃の前進/後退と派手さ(2026-09-30)の自動確認(開発ビルド/Editor専用)。
//  全キャラ: 前フリック → 画面上で前へ出る(ScreenStepOffsetの最大)/後ろフリック → 画面上で下がる(最小)、
//  踏み込みの演出(残像/速度線)が出る、目の前の敵に当てると命中の演出(AttackFlair.Hit)が出る。
//  起動: Editor = メニュー「Attack Feel Tour (batch)」、開発ビルド = -afTour <出力フォルダ>(連番を書き出す)
// 結果: AttackFeelTour.txt
public class AttackFeelTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string dir = null;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-afTour") dir = args[i + 1];
#if UNITY_EDITOR
        if (dir == null && UnityEditor.EditorPrefs.GetInt("AttackFeelTour", 0) == 1)
        {
            UnityEditor.EditorPrefs.SetInt("AttackFeelTour", 0);
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        }
#endif
        if (dir == null) return;
        Application.runInBackground = true;
        var go = new GameObject("AttackFeelTour");
        DontDestroyOnLoad(go);
        var t = go.AddComponent<AttackFeelTour>();
        t.outDir = dir;
        t.capture = !Application.isEditor;
    }

    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    string outDir;
    bool capture;
    int frameNo;
    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[AttackFeel] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("[FAIL] " + what); } }
    GameManager gm;
    PlayerController pc;

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        Application.logMessageReceived += (c, tr, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + tr.Split('\n')[0]); } };
        yield return new WaitForSecondsRealtime(1.5f);
        var chars = new List<string>();
        foreach (var c in CharacterDatabase.AllCharacters) chars.Add(c.characterId);
        string[] cmd = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < cmd.Length - 1; i++) if (cmd[i] == "-afChars") chars = new List<string>(cmd[i + 1].Split(','));
        L("characters: " + string.Join(",", chars));
        L("character        forward(max on-screen)  back(min)  stepFx  hits(fx)  finisher");
        bool first = true;
        foreach (string ch in chars)
        {
            if (!first) yield return RetryToHome();
            first = false;
            yield return BeginRun(ch);
            if (gm == null || !gm.HasStarted) { Check(false, ch + ": run started"); continue; }
            yield return new WaitForSeconds(0.8f);

            int step0 = AttackFlair.StepCount;
            float fwd = 0f, back = 0f;
            // 前
            yield return Flick(PlayerController.FlickDirection.Forward);
            yield return Sample(1.1f, v => fwd = Mathf.Max(fwd, v));
            yield return new WaitForSeconds(0.5f);
            // 後ろ
            yield return Flick(PlayerController.FlickDirection.Backward);
            yield return Sample(1.1f, v => back = Mathf.Min(back, v));
            int steps = AttackFlair.StepCount - step0;
            yield return new WaitForSeconds(0.6f);

            // 目の前の敵に連続で当てる(命中の演出/コンボの締め)
            int hit0 = AttackFlair.HitCount, fin0 = AttackFlair.FinisherCount;
            var enemy = SpawnDummy(3.2f);
            for (int i = 0; i < 4; i++)
            {
                if (enemy != null) PlaceAhead(enemy, i % 2 == 0 ? 1.7f : 3.0f); // 近い間合いと、構えの長い技(竜騎士の突き等)向けの少し遠い間合いを交互に
                yield return Flick(PlayerController.FlickDirection.Forward);
                yield return Sample(0.3f, null);
            }
            yield return Sample(0.6f, null);
            if (enemy != null) Destroy(enemy);
            int hits = AttackFlair.HitCount - hit0, fins = AttackFlair.FinisherCount - fin0;
            L($"{ch,-16} {fwd,8:F2}m {back,18:F2}m {steps,8} {hits,8} {fins,8}");
            Check(fwd >= 0.35f, $"{ch}: forward attack moves forward on screen ({fwd:F2}m)");
            Check(back <= -0.35f, $"{ch}: backward attack moves back on screen ({back:F2}m)");
            Check(steps >= 2, $"{ch}: step effect (afterimage/speed lines) shown ({steps})");
            Check(hits >= 1, $"{ch}: hit effect shown ({hits})");
        }
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "AttackFeelTour.txt"), log.ToString());
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator Flick(PlayerController.FlickDirection f)
    {
        pc.debugInjectFlick = f; // 1フレームだけだと一時停止などで取りこぼすことがあるので2フレーム
        yield return null;
        yield return null;
        pc.debugInjectFlick = null;
    }

    IEnumerator Sample(float seconds, System.Action<float> onValue)
    {
        float t = 0f;
        while (t < seconds)
        {
            onValue?.Invoke(pc.ScreenStepOffset);
            if (capture)
            {
                Time.captureFramerate = 30;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"f_{frameNo++:00000}.png"));
            }
            t += Time.deltaTime;
            yield return null;
        }
    }

    GameObject SpawnDummy(float ahead)
    {
        var def = EnemyDatabase.FindById("goblin");
        if (def == null) return null;
        float x = pc.transform.position.x + ahead;
        float? gy = TerrainManager.Instance.GetHeightAt(x);
        // 実際のEncounterと同じ置き方(地面の高さ)。倒れないようHPを大きくする
        var go = TerrainManager.Instance.SpawnEncounterEnemy(def, new Vector2(x, gy ?? pc.transform.position.y), EnemyAiTier.T0, def.behaviorKind);
        var ec = go != null ? go.GetComponent<EnemyController>() : null;
        if (ec != null) { var f = typeof(EnemyController).GetField("hp", NP); if (f != null) f.SetValue(ec, 9999); var m = typeof(EnemyController).GetField("maxHp", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (m != null && m.FieldType == typeof(int)) m.SetValue(ec, 9999); }
        return go;
    }

    // 自動前進で追い越さないよう、敵を目の前に置き直す(テスト用)
    void PlaceAhead(GameObject enemy, float ahead)
    {
        var p = enemy.transform.position;
        {
            float x = pc.transform.position.x + ahead;
            float? g0 = TerrainManager.Instance.GetHeightAt(p.x), g1 = TerrainManager.Instance.GetHeightAt(x);
            enemy.transform.position = new Vector3(x, p.y + ((g1 ?? 0f) - (g0 ?? 0f)), p.z); // 地面からの高さはそのまま
        }
    }

    IEnumerator BeginRun(string ch)
    {
        L($"--- {ch}");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage("wasteland_road");
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (TerrainManager.Instance != null) { TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f; }
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
    }

    IEnumerator RetryToHome()
    {
        var old = gm;
        gm.Retry();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
    }
}

#if UNITY_EDITOR
public static class AttackFeelTourMenu
{
    [UnityEditor.MenuItem("Tools/OneMoreMile/Attack Feel Tour (batch)")]
    public static void RunBatch()
    {
        UnityEditor.EditorPrefs.SetInt("AttackFeelTour", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        UnityEditor.EditorApplication.EnterPlaymode();
    }
}
#endif
#endif

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
        if (characterId.StartsWith("demo:")) { yield return Demo(characterId.Substring(5)); yield break; }
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
        if (pc.IsLancer) { yield return LancerMoves(pc); goto done; }
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

    done:
        System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "shots.txt"), log.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"done_{characterId}.txt"), System.DateTime.Now.ToString());
        EditorApplication.isPlaying = false;
    }

    // 竜騎士: 前突き(構え/突き)・後ろ・上・下をそれぞれ撮る。
    IEnumerator LancerMoves(PlayerController pc)
    {
        yield return Move(pc, PlayerController.FlickDirection.Forward, "fwd", new[] { 0.06f, 0.24f, 0.34f });
        yield return Move(pc, PlayerController.FlickDirection.Backward, "back", new[] { 0.24f });
        yield return Move(pc, PlayerController.FlickDirection.Up, "up", new[] { 0.08f, 0.2f });
        yield return new WaitForSeconds(1.2f);
        yield return Move(pc, PlayerController.FlickDirection.Down, "down", new[] { 0.2f, 0.3f });
    }

    IEnumerator Move(PlayerController pc, PlayerController.FlickDirection dir, string name, float[] at)
    {
        pc.debugInjectFlick = dir;
        yield return null;
        pc.debugInjectFlick = null;
        float st = Time.time;
        for (int i = 0; i < at.Length; i++)
        {
            while (Time.time - st < at[i]) yield return null;
            Shot($"{name}{i}");
            yield return null;
        }
        yield return new WaitForSeconds(1.0f);
    }

    // 確認動画用のデモ: Character Selectでカードを順に送り→対象キャラを選択→RUN開始→4方向攻撃。
    // 撮影側(ffmpeg)の準備が整うまで MotionShots/go.txt を待つ(ready.txtを書いて合図する)。
    IEnumerator Demo(string target)
    {
        var gm = GameManager.Instance;
        // 撮影後、選択キャラ/ステージ(保存される)を元に戻す。
        string prevChar = gm.SelectedCharacterId, prevStage = gm.SelectedStageId;
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ready.txt"), "ready");
        string go = System.IO.Path.Combine(dir, "go.txt");
        float w = 0f;
        while (!System.IO.File.Exists(go) && w < 120f) { w += Time.unscaledDeltaTime; yield return null; }
        StartCoroutine(Pump());
        yield return new WaitForSecondsRealtime(1.0f);

        gm.SetSelectedCharacter("swordsman");
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = gm.characterSelectUI;
        var t = typeof(CharacterSelectUI);
        var select = t.GetMethod("SelectIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        var snap = t.GetMethod("BeginSnap", BindingFlags.NonPublic | BindingFlags.Instance);
        var confirm = t.GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance);
        var all = CharacterDatabase.AllCharacters;
        int targetIndex = 0;
        for (int i = 0; i < all.Count; i++) if (all[i].characterId == target) targetIndex = i;
        for (int i = 1; i <= targetIndex; i++)
        {
            select.Invoke(ui, new object[] { i });
            snap.Invoke(ui, new object[] { i });
            yield return new WaitForSecondsRealtime(i == targetIndex ? 2.5f : 0.8f);
        }
        confirm.Invoke(ui, null);
        yield return new WaitForSecondsRealtime(1.5f);

        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        var pc = PlayerController.Instance;
        yield return new WaitForSeconds(2.0f);

        // 前突き: 前方に3体並べて1回で貫く
        DemoEnemy(pc, 3.6f, 0f); DemoEnemy(pc, 4.4f, 0f); DemoEnemy(pc, 5.2f, 0f);
        yield return new WaitForSeconds(0.25f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Forward);
        yield return new WaitForSeconds(1.6f);
        // 2連突き
        DemoEnemy(pc, 3.6f, 0f); DemoEnemy(pc, 5.0f, 0f);
        yield return new WaitForSeconds(0.25f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Forward);
        yield return new WaitForSeconds(0.45f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Forward);
        yield return new WaitForSeconds(1.6f);
        // 後ろ(石突き)
        DemoEnemy(pc, -0.2f, 0f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Backward);
        yield return new WaitForSeconds(1.6f);
        // 上突き
        DemoEnemy(pc, 2.6f, 1.8f);
        yield return new WaitForSeconds(0.1f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Up);
        yield return new WaitForSeconds(2.2f);
        // 下突き
        DemoEnemy(pc, 2.4f, 0f);
        yield return new WaitForSeconds(0.1f);
        yield return DemoFlick(pc, PlayerController.FlickDirection.Down);
        yield return new WaitForSeconds(2.0f);

        if (!string.IsNullOrEmpty(prevChar)) gm.SetSelectedCharacter(prevChar);
        if (!string.IsNullOrEmpty(prevStage)) gm.SetSelectedStage(prevStage);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done_demo.txt"), System.DateTime.Now.ToString());
        EditorApplication.isPlaying = false;
    }

    void DemoEnemy(PlayerController pc, float dx, float dy)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        float x = pc.transform.position.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? pc.transform.position.y) + dy;
        GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: 3, behaviorKind: EnemyBehaviorKind.None, visualScaleMultiplier: d.visualScaleMultiplier);
    }

    IEnumerator DemoFlick(PlayerController pc, PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        float t = 0f;
        while (t < 1f) { yield return null; t += Time.unscaledDeltaTime; if (Time.timeScale > 0f) break; }
        yield return null;
        pc.debugInjectFlick = null;
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
            // 撃破でレベルアップしてもカード選択で止まらないよう自動で選ぶ。
            var gm = GameManager.Instance;
            if (gm != null && gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.2f);
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }
            }
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

    [MenuItem("Tools/OneMoreMile/Motion Visual Tour: Dragon Lancer")]
    public static void RunLancer() => Launch("dragon_lancer");

    [MenuItem("Tools/OneMoreMile/Demo Video: Dragon Lancer")]
    public static void DemoLancer() => Launch("demo:dragon_lancer");
}
#endif

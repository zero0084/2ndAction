#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 竜騎士 改修第2弾の確認動画用(開発ビルド専用)。起動引数 -lancerDemo <出力フォルダ> で動く。
// ウィンドウのフォーカスや他のウィンドウの重なりに左右されないよう、ゲーム内で毎フレームを
// PNGに書き出す(Time.captureFramerateで動画の時間を固定)。区間: ①通常Run ②3段突き ③急降下。
public class LancerDemoCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-lancerDemo") continue;
            var go = new GameObject("LancerDemoCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<LancerDemoCapture>().outDir = args[i + 1];
            return;
        }
    }

    string outDir;
    int frame;
    bool capturing;
    readonly System.Text.StringBuilder times = new System.Text.StringBuilder();

    void Mark(string what) { times.AppendLine($"{what} {frame}"); }

    void LateUpdate()
    {
        if (!capturing) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"f_{frame:00000}.png"));
        frame++;
    }

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        yield return new WaitForSecondsRealtime(2f);
        var gm = GameManager.Instance;
        gm.SetSelectedCharacter("dragon_lancer");
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        // 撮影用: 被弾で中断されないようにする(開発ビルドにはDebugSetInvincibleが無いのでリフレクションで)
        var inv = typeof(GameManager).GetProperty("InvincibleMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        inv?.GetSetMethod(true)?.Invoke(gm, new object[] { true });
        // 撮影用: レベルアップのカード選択で動画が途切れないようEXPを止める
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        StartCoroutine(AutoPickLevelUp());
        var pc = PlayerController.Instance;

        // 確認動画用: キャラの動きが分かるようカメラを寄せる(ゲーム本来の視野は横に広い)
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) { cf.targetHorizontalHalfWidth = 8.5f; cf.offsetX = 2.2f; cf.highSpeedLookAhead = 0.5f; }
        yield return Wait(0.6f);

        Time.captureFramerate = 30;
        capturing = true;

        // ① 通常Run(穴はジャンプで越える、途中で普通のジャンプも挟む)
        Mark("run_start");
        float rt = 0f, nextJump = 2.2f;
        while (rt < 7f)
        {
            var tm = TerrainManager.Instance;
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            bool pit = pc.IsGrounded && tm != null && tm.IsNearPit(pc.transform.position.x + lead, 0.4f);
            if (pit || rt >= nextJump) { if (!pit) nextJump += 2.4f; yield return Flick(pc, PlayerController.FlickDirection.Up); }
            yield return null; rt += Time.deltaTime;
        }
        Mark("run_end");

        enemyHp = 24; // 3段とも当たって吹き飛ぶのが見えるよう、突きでは倒れない程度に硬くする
        // ② 3段突き(前方に敵を並べ、Quick Thrust → Step Thrust → Dragon Pierce を2回)
        for (int rep = 0; rep < 2; rep++)
        {
            yield return WaitNoPit(pc, 7f);
            for (int i = 0; i < 6; i++) Enemy(pc, 2.2f + i * 0.85f, 0f);
            yield return Wait(0.35f);
            if (rep == 0) Mark("combo_start");
            for (int k = 0; k < 3; k++)
            {
                yield return Flick(pc, PlayerController.FlickDirection.Forward);
                float ct = 0f;
                while (ct < 1.2f && pc.LanceComboStage == k + 1 && pc.LanceFrameIndex == 0) { yield return null; ct += Time.deltaTime; }
                yield return Wait(0.14f);
            }
            yield return Wait(1.3f);
        }
        Mark("combo_end");

        enemyHp = 3;
        // ③ ジャンプ → 急降下 → 突き刺し着地の衝撃波(着地点の前後に敵)
        for (int rep = 0; rep < 2; rep++)
        {
            yield return WaitNoPit(pc, 6f);
            yield return Wait(0.3f);
            if (rep == 0) Mark("dive_start");
            yield return Flick(pc, PlayerController.FlickDirection.Up);
            yield return Wait(0.2f);
            if (rep == 1) { yield return Flick(pc, PlayerController.FlickDirection.Up); yield return Wait(0.25f); }
            Enemy(pc, 1.0f, 0f); Enemy(pc, 1.9f, 0f); Enemy(pc, -0.4f, 0f);
            yield return Wait(0.2f);
            yield return Flick(pc, PlayerController.FlickDirection.Down);
            float dt2 = 0f;
            while (dt2 < 3f && (pc.LanceMove == PlayerController.LanceMoveKind.Dive || !pc.IsGrounded)) { yield return null; dt2 += Time.deltaTime; }
            yield return Wait(1.4f);
        }
        Mark("dive_end");
        yield return Wait(0.4f);
        capturing = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "times.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), "done");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    // captureFramerate中はゲーム内時間で待つ(WaitForSecondsはゲーム内時間)
    static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    IEnumerator Flick(PlayerController pc, PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        yield return null;
        while (Time.timeScale == 0f) yield return null;
        yield return null;
        pc.debugInjectFlick = null;
    }

    IEnumerator WaitNoPit(PlayerController pc, float distance)
    {
        var tm = TerrainManager.Instance;
        float t = 0f;
        while (tm != null && t < 10f)
        {
            bool pit = false;
            for (float d = -1f; d <= distance; d += 0.5f) if (tm.IsNearPit(pc.transform.position.x + d, 0.3f)) { pit = true; break; }
            if (!pit && pc.IsGrounded) break;
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            if (pc.IsGrounded && tm.IsNearPit(pc.transform.position.x + lead, 0.4f)) yield return Flick(pc, PlayerController.FlickDirection.Up);
            yield return null; t += Time.deltaTime;
        }
    }

    int enemyHp = 3;
    void Enemy(PlayerController pc, float dx, float dy)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        float x = pc.transform.position.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? pc.transform.position.y) + dy;
        GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: enemyHp, behaviorKind: EnemyBehaviorKind.None, visualScaleMultiplier: d.visualScaleMultiplier);
    }

    IEnumerator AutoPickLevelUp()
    {
        while (true)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.15f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.3f);
                continue;
            }
            yield return null;
        }
    }
}
#endif

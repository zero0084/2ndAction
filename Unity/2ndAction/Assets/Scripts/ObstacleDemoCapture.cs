#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 障害物の耐久力/「壊して通る」自動攻撃の確認動画用(開発ビルド専用、2026-09-29)。起動引数 -obstacleDemo <出力フォルダ>。
// 毎フレームをPNGへ書き出す(Time.captureFramerate=30で動画の時間を固定。ウィンドウのフォーカスに左右されない)。
// 高速時の自動操作補助(120km/h、入力は補助だけ)で、平らな場所へ順に障害物を置く:
//  ①小木/壊せる木(未強化で1発) ②岩(強化で1発) ③壁(攻撃力8、先にひび1段) ④大岩(攻撃力8、先にひび2段)
// ③④は「重いものが崩れる」演出と、耐久力の低下に応じたひびを見せるため、画面内に入った時点で途中まで削っておく。
public class ObstacleDemoCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-obstacleDemo") continue;
            Application.runInBackground = true;
            var go = new GameObject("ObstacleDemoCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<ObstacleDemoCapture>().outDir = args[i + 1];
            return;
        }
    }

    string outDir;
    int frame;
    bool capturing;
    float targetKmh = 120f, naturalRunSpeed = -1f;
    readonly System.Text.StringBuilder times = new System.Text.StringBuilder();
    PlayerController pc;

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
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) { TerrainManager.Instance.enemySpawnChance = 0f; TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; }
        pc = PlayerController.Instance;
        HighSpeedAssist.Instance.SetEnabled(true);
        StartCoroutine(SpeedController());
        StartCoroutine(KeepClear());
        // 動画用: 障害物の割れ方が見えるようカメラを寄せる(ゲーム本来の視野は高速で広がる)
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) { cf.highSpeedZoomOut = 0f; }
        yield return Wait(6f); // 穴の無い地形が先まで作られるまで

        Time.captureFramerate = 30;
        capturing = true;
        Mark("start");
        yield return Place("SmallTree", 0);
        yield return Place("BreakableTree", 0);
        yield return Place("SmallTree", 0);
        pc.AddAttackPower(2);   // ATTACK UP ×2 相当
        yield return Place("Rock", 0);
        yield return Place("Rock", 0);
        pc.AddAttackPower(4);   // 攻撃力8
        yield return Place("Wall", 2);       // 7→5(ひび1段)→1発で崩れる
        yield return Place("GiantRock", 6);  // 10→4(ひび2段)→1発で崩れる
        yield return Place("Wall", 4);
        yield return Wait(1.0f);
        Mark("end");
        capturing = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "times.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "summary.txt"),
            $"broken={ObstacleController.TotalBroken} hits={ObstacleController.TotalHits} contactDamage={ObstacleController.TotalContactDamage} debrisPeak={ObstacleFx.PeakPieces} breakFx={ObstacleFx.BreakFxCount}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), "done");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    // 平らで前後に穴の無い場所へ置き、通り過ぎるまで待つ。preDamage>0なら画面に入ったところで途中まで削る(ひびを見せる)。
    IEnumerator Place(string kind, int preDamage)
    {
        var tm = TerrainManager.Instance;
        float lead = Mathf.Max(30f, pc.CurrentAutoRunSpeed * 1.3f);
        capturing = false; // 平地を待つ間は撮らない(動画を短く)
        float x = float.NaN, gy = 0f, waitT = 0f;
        while (float.IsNaN(x) && waitT < 60f)
        {
            float px = pc.transform.position.x, cx = px + lead;
            if (pc.IsGrounded && tm.IsGenerated(cx + 10f) && Flat(tm, px - 2f, cx + 8f, cx, out gy)) x = cx;
            else { yield return null; waitT += Time.deltaTime; }
        }
        if (float.IsNaN(x)) yield break;
        var spawner = ObstacleSpawner.FindForStage("wasteland_road");
        int idx = -1;
        for (int i = 0; i < spawner.specs.Length; i++) if (spawner.specs[i].name == kind) idx = i;
        var oc = spawner.CreateReplica(idx, new Vector2(x, gy), 0f, false);
        capturing = true;
        Mark($"{kind}_placed");
        bool damaged = preDamage <= 0;
        float t0 = Time.time;
        while (Time.time - t0 < 8f && pc.transform.position.x < x + 6f)
        {
            if (!damaged && x - pc.transform.position.x < 24f)
            {
                damaged = true;
                oc.ApplyDamage(preDamage, oc.transform.position + Vector3.up * 1.2f, 1);
            }
            yield return null;
        }
        yield return Wait(0.5f);
    }

    static bool Flat(TerrainManager tm, float from, float to, float ox, out float gy)
    {
        // ObstacleBreakAutoTest.Clearと同じ条件: 手前は穴/分岐/段差なし、障害物の手前22m〜後ろ4mはほぼ平ら
        gy = 0f;
        float? prevH = null;
        for (float x = from; x <= to; x += 0.5f)
        {
            if (!tm.TryGetGroundFast(x, out float h, out bool pit) || pit || tm.IsInBranchRoute(x)) return false;
            if (prevH.HasValue && Mathf.Abs(h - prevH.Value) > 0.3f) return false;
            prevH = h;
        }
        if (!tm.TryGetGroundFast(ox, out gy, out _)) return false;
        for (float x = ox - 22f; x <= ox + 4f; x += 0.5f)
        {
            if (!tm.TryGetGroundFast(x, out float h, out bool pit) || pit || Mathf.Abs(h - gy) > (x < ox - 6f ? 0.6f : 0.3f)) return false;
            float? sky = tm.GetSkyHeightFast(x);
            if (sky.HasValue && sky.Value < h + 4.5f) return false;
        }
        return Mathf.Abs(tm.GetSlopeAngleAt(ox)) <= 0.01f;
    }

    IEnumerator SpeedController()
    {
        while (true)
        {
            yield return null;
            if (pc == null) continue;
            if (naturalRunSpeed < 0f) naturalRunSpeed = pc.runSpeed;
            float mult = pc.CurrentAutoRunSpeed / Mathf.Max(0.0001f, pc.runSpeed);
            if (mult > 0.01f) pc.runSpeed = (targetKmh / GameManager.KmhPerMps) / mult;
        }
    }

    // 動画を障害物だけに絞る(敵は消す)
    IEnumerator KeepClear()
    {
        while (true)
        {
            if (pc != null)
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    if (e.gameObject.activeSelf && Mathf.Abs(e.transform.position.x - pc.transform.position.x) < 80f) e.gameObject.SetActive(false);
            yield return null;
        }
    }

    static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }
}
#endif

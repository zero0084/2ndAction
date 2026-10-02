#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 死神三姉妹の確認動画用(開発ビルド専用、2026-09-29)。起動引数 -reaperDemo <出力フォルダ> -reaperDemoStage <stageId>。
// 毎フレームをPNGへ書き出す(Time.captureFramerate=30)。99,800mから120km/hで100,000mへ → 担当の姉妹が画面左端から現れる →
// 300km/hへ急加速(引き離す) → 数秒後にまた画面左へ戻ってくる → 120km/hへ戻して捕まるまで。
public class ReaperDemoCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string dir = null, stage = "wasteland_road";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-reaperDemo") dir = args[i + 1];
            if (args[i] == "-reaperDemoStage") stage = args[i + 1];
        }
        if (dir == null) return;
        Application.runInBackground = true;
        var go = new GameObject("ReaperDemoCapture");
        DontDestroyOnLoad(go);
        var c = go.AddComponent<ReaperDemoCapture>();
        c.outDir = dir; c.stage = stage;
    }

    string outDir, stage;
    int frame;
    bool capturing;
    float targetKmh = 120f;
    PlayerController pc;
    readonly System.Text.StringBuilder times = new System.Text.StringBuilder();
    void Mark(string what) => times.AppendLine($"{what} {frame}");

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
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        HighSpeedAssist.Instance.SetEnabled(true);
        StartCoroutine(SpeedController());
        ReaperBase.DebugNoReap = true;
        gm.DebugWarpToDistance(99800f);
        yield return Wait(1.5f);

        Time.captureFramerate = 30;
        capturing = true;
        Mark("start");
        var bm = BossManager.Instance;
        while (!bm.DeathSpawned) yield return null;
        Mark("death_start");
        ReaperBase r = null;
        while (r == null) { r = ReaperBase.Active; yield return null; }
        // 出現して落ち着くまで+少し並走
        while (r.CurrentPhase != ReaperBase.Phase.Chase) yield return null;
        Mark("settled");
        yield return Wait(4f);
        // 急加速: 引き離す → 数秒後に戻ってくる
        Mark("accelerate_300");
        targetKmh = 300f;
        yield return Wait(10f);
        // 普通の速さへ戻す → じわじわ詰められて捕まる
        Mark("slow_120");
        targetKmh = 120f;
        ReaperBase.DebugNoReap = false;
        float t0 = Time.time;
        while (!gm.IsGameOver && Time.time - t0 < 25f) yield return null;
        Mark("captured");
        yield return Wait(2.5f);
        Mark("end");
        capturing = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "times.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), $"gameOver={gm.IsGameOver} strikes={r.Strikes} type={r.GetType().Name}");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    IEnumerator SpeedController()
    {
        while (true)
        {
            yield return null;
            if (pc == null) continue;
            float mult = pc.CurrentAutoRunSpeed / Mathf.Max(0.0001f, pc.runSpeed);
            if (mult > 0.01f) pc.runSpeed = (targetKmh / GameManager.KmhPerMps) / mult;
        }
    }

    static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }
}
#endif

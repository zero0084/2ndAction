#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 死神三姉妹(2026-09-29)の追跡の自動確認。結果は ReaperChaseAutoTest_<stage>.txt。
// 99,800mへワープ → 100,000mで担当の姉妹が出る → 速さを段階的に変えて「一度は引き離せる/数秒でまた来る/最後は捕まる」を測る。
//  ・出現: 担当の姉妹か、画面左端の外から来たか、既存の死神の開始(DeathSpawned=BGM)と同じか
//  ・速度帯: 低速(40km/h)/100km/h超(120)/200km/h超(250)/極端(600、1000)へ急加速した時の、最大の距離・画面外の時間・画面へ戻るまで・落ち着く位置へ戻るまで
//  ・瞬間移動しない: 1フレームの距離の変化が、追従+再追跡の速さで説明できる範囲か
//  ・アニメの速さ: 低速と極端な高速でコマ送りの速さが同じか(走行速度と分離)
//  ・捕捉: 一定速で走り続けると捕まり、既存のGameOver(Reaper:<姉妹>)になる
// 起動: Tools/OneMoreMile/Reaper Chase Test (batch)  -rcStage wasteland_road|natural_cave|sky_corridor
public class ReaperChaseAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("ReaperChaseTest", 0) != 1) return;
        EditorPrefs.SetInt("ReaperChaseTest", 0);
        new GameObject("ReaperChaseTest").AddComponent<ReaperChaseAutoTest>();
    }

    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    readonly StringBuilder log = new StringBuilder();
    int failures; bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[ReaperTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    GameManager gm; PlayerController pc; ReaperBase r;
    float targetKmh = 40f;
    float maxFrameJump; int frames;
    readonly List<string> jumpNotes = new List<string>();
    string stage;

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    IEnumerator Start()
    {
        stage = Arg("-rcStage", EditorPrefs.GetString("ReaperChaseTestStage", "wasteland_road"));
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999); // 地形/敵の被弾では終わらない(死神の捕捉だけで終わる)
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f);
        HighSpeedAssist.Instance.SetEnabled(true);
        ReaperBase.DebugNoReap = true;
        StartCoroutine(SpeedController());
        L($"stage={stage} expected={ReaperBase.PrefabNameFor(stage)}");

        // ---- 100,000mの手前へワープ → 走って100,000mへ ----
        gm.DebugWarpToDistance(99800f);
        targetKmh = 120f;
        w = 0f;
        var bm = BossManager.Instance;
        while (!bm.DeathSpawned && w < 30f) { yield return null; w += Time.deltaTime; }
        r = FindFirstObjectByType<ReaperBase>();
        Check(bm.DeathSpawned && r != null, $"death start at 100,000m (MaxDistance={gm.MaxDistance:F0}, DeathSpawned={bm.DeathSpawned})");
        if (r == null) { Finish(); yield break; }
        Check(r.GetType().Name == ReaperBase.PrefabNameFor(stage), $"stage {stage} gets {r.GetType().Name}");
        Check(FindObjectsByType<ReaperBase>(FindObjectsSortMode.None).Length == 1, "exactly one reaper");
        bool hasArt = r.data != null && r.data.moveFrames != null && r.data.moveFrames.Length > 0;
        L($"  data={r.data?.name} motion={r.data?.motion} art={(hasArt ? r.data.moveFrames.Length + " frames" : "placeholder")}");
        StartCoroutine(Monitor());

        // ---- 出現: 画面左端の外から ----
        targetKmh = 40f;
        float t0 = Time.time; bool sawOffscreenStart = false; float appearedAt = -1f;
        while (Time.time - t0 < 25f && r.CurrentPhase != ReaperBase.Phase.Chase)
        {
            if (r.CurrentPhase != ReaperBase.Phase.Chase && r.OffScreen) sawOffscreenStart = true;
            if (appearedAt < 0f && r.CurrentPhase == ReaperBase.Phase.Appear) appearedAt = Time.time - t0;
            yield return null;
        }
        L($"[Appear] start offscreen={sawOffscreenStart} appear phase at {appearedAt:F1}s, settled after {Time.time - t0:F1}s gap={r.Gap:F1} leftEdge={r.LeftEdgeGap:F1} appearFx={ReaperAppearFx.PlayCount}");
        Check(sawOffscreenStart && r.CurrentPhase == ReaperBase.Phase.Chase, "appears from beyond the left edge and settles on screen");
        Check(ReaperAppearFx.PlayCount >= 1, "appear atmosphere effect played");

        // ---- アニメの速さ(低速) ----
        float lowFps = 0f;
        yield return AnimRate(v => lowFps = v);

        // ---- 速度帯ごとの急加速: 引き離せるか/戻ってくるか ----
        yield return Burst(40f, 120f, "low->100+");
        yield return Burst(120f, 250f, "100+->200+");
        yield return Burst(250f, 600f, "200+->extreme(600)");
        float highFps = 0f;
        yield return AnimRate(v => highFps = v);
        L($"[Anim] frame rate at 40km/h={lowFps:F2}/s at 600km/h={highFps:F2}/s (expected {r.data.moveFps}/s, skip={r.data.skipRate})");
        Check(Mathf.Abs(lowFps - highFps) <= Mathf.Max(0.5f, lowFps * 0.1f), "animation tempo does not depend on run speed");
        yield return Burst(600f, 1000f, "extreme(600->1000)");

        // ---- 瞬間移動しない ----
        L($"[NoTeleport] max per-frame gap change beyond explained speed = {maxFrameJump:F3}m over {frames} frames");
        foreach (string n in jumpNotes) L("    " + n);
        Check(maxFrameJump < 0.5f, "no teleport (per-frame movement explained by follow/catch-up speeds)");

        // ---- 一定速で走り続けると捕まる ----
        ReaperBase.DebugNoReap = false;
        targetKmh = 250f;
        t0 = Time.time;
        while (Time.time - t0 < 90f && !gm.IsGameOver) { yield return null; }
        L($"[Capture] after {Time.time - t0:F1}s at 250km/h: gameOver={gm.IsGameOver} strikes={r.Strikes} phase={r.CurrentPhase} gap={r.Gap:F2}");
        Check(gm.IsGameOver && r.Strikes >= 1, "keeps closing in and finally captures -> existing GameOver");
        Finish();
    }

    // from→toへ急加速し、最大の距離・画面外の時間・戻るまでを測る
    IEnumerator Burst(float fromKmh, float toKmh, string label)
    {
        targetKmh = fromKmh;
        float t0 = Time.time;
        while (Time.time - t0 < 6f) yield return null; // 落ち着かせる
        float gapBefore = r.Gap, edge = r.LeftEdgeGap;
        targetKmh = toKmh;
        t0 = Time.time;
        float maxGap = 0f, offscreenTime = 0f, firstOff = -1f, backOn = -1f, settled = -1f;
        bool wasOff = false;
        var samples = new List<string>();
        float nextSample = 0f;
        while (Time.time - t0 < 16f)
        {
            float e = Time.time - t0;
            maxGap = Mathf.Max(maxGap, r.Gap);
            if (r.OffScreen) { offscreenTime += Time.deltaTime; if (firstOff < 0f) firstOff = e; wasOff = true; }
            else if (wasOff && backOn < 0f) backOn = e;
            if (settled < 0f && e > 1f && r.Gap <= r.LeftEdgeGap * r.data.chase.targetScreenFraction + 1f) settled = e;
            if (e >= nextSample) { nextSample += 1f; samples.Add($"{e:F0}s:gap{r.Gap:F0}/{r.LeftEdgeGap:F0}{(r.OffScreen ? "*" : "")} re{r.Reacquire:F1}"); }
            if (gm.IsGameOver) break;
            yield return null;
        }
        L($"[Burst {label}] gap before={gapBefore:F1}(edge {edge:F1}) max={maxGap:F1} offscreen={offscreenTime:F1}s first off at {firstOff:F1}s back on screen at {backOn:F1}s settled at {settled:F1}s");
        L("    " + string.Join(" ", samples));
        Check(maxGap > gapBefore + 2f, $"{label}: accelerating opens the distance for a while");
        Check(!wasOff || backOn > 0f, $"{label}: comes back on screen after being left behind");
    }

    IEnumerator AnimRate(System.Action<float> result)
    {
        var anim = r.GetComponent<ReaperAnimator>();
        int changes = 0, last = anim.FrameIndex;
        float t0 = Time.time;
        while (Time.time - t0 < 3f) { if (anim.FrameIndex != last) { changes++; last = anim.FrameIndex; } yield return null; }
        result(changes / 3f);
    }

    IEnumerator Monitor()
    {
        double lastX = r.transform.position.x + FloatingOrigin.Offset;
        int lastReappears = r.Reappears;
        while (r != null && !gm.IsGameOver)
        {
            yield return null;
            float dt = Time.deltaTime;
            double x = r.transform.position.x + FloatingOrigin.Offset;
            // 死神自身の1フレームの移動 = 自分の速さ×dt。それで説明できない分(=瞬間移動)を測る(捕捉の距離で止まっている時は除く)
            bool reappeared = r.Reappears != lastReappears; lastReappears = r.Reappears;
            if (dt > 0f && !reappeared && r.Gap > r.data.chase.captureGap + 0.05f && r.CurrentPhase != ReaperBase.Phase.Waiting)
            {
                float j = Mathf.Abs((float)(x - lastX) - r.ReaperSpeed * dt);
                if (j > 0.5f && jumpNotes.Count < 6) jumpNotes.Add($"jump={j:F2} dx={(float)(x - lastX):F2} v={r.ReaperSpeed:F1} dt={dt:F3} gap={r.Gap:F2} phase={r.CurrentPhase} offset={FloatingOrigin.Offset:F0} gameTime={Time.time:F1}");
                maxFrameJump = Mathf.Max(maxFrameJump, j);
                frames++;
            }
            lastX = x;
        }
    }

    float naturalRun = -1f;
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

    void Finish()
    {
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, $"../ReaperChaseAutoTest_{stage}.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}

public static class ReaperChaseTestMenu
{
    static void Run(string stage)
    {
        EditorPrefs.SetString("ReaperChaseTestStage", stage);
        EditorPrefs.SetInt("ReaperChaseTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
    [MenuItem("Tools/OneMoreMile/Reaper Chase Test (batch)")] public static void RunBatch() => Run("wasteland_road");
    [MenuItem("Tools/OneMoreMile/Reaper Chase Test - Cave (batch)")] public static void RunCave() => Run("natural_cave");
    [MenuItem("Tools/OneMoreMile/Reaper Chase Test - Sky (batch)")] public static void RunSky() => Run("sky_corridor");
}
#endif

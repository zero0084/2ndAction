#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// 総点検(2026-09-30)。開発ビルド/Editor専用。起動引数で3つのモード:
//  -qaVisual <dir>   全キャラの走り/ジャンプ/二段ジャンプ/前/後/上/下攻撃/被弾を撮影し、
//                    絵が消える・大きさが跳ねる・足が地面から浮く/沈む・向きが違う等を自動判定(frames.csv + 画像)
//  -qaEnemies <dir>  全ステージの距離帯ごとに実際に走り(ボット: 近い敵を攻撃)、敵が「その距離帯の敵」として
//                    画面外(右)から現れて画面内に見えるか、倒せるか、プレイヤーが詰まらないかを記録。
//                    さらに全種類の敵を1体ずつ目の前に出して倒せるか(剣士)、高速で走りながら飛び道具が敵に届くか(射撃キャラ)。
//  -qaFullRun <dir> [-qaStage id] [-qaChar id] [-qaKmh n]
//                    ワープせずに0→100,000mを実際に走り、道中のボスを実際の攻撃で倒し(倒せなければ記録)、
//                    100,000mの死神に捕まってゲームオーバーになるまで。
// 結果は <dir>/qa_<mode>.txt
public partial class QaSweep : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        string mode = null, dir = null;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-qaVisual") { mode = "visual"; dir = a[i + 1]; }
            if (a[i] == "-qaEnemies") { mode = "enemies"; dir = a[i + 1]; }
            if (a[i] == "-qaFullRun") { mode = "fullrun"; dir = a[i + 1]; }
            if (a[i] == "-qaRanged") { mode = "ranged"; dir = a[i + 1]; }
            if (a[i] == "-qaFalls") { mode = "falls"; dir = a[i + 1]; }
            if (a[i] == "-qaBossShots") { mode = "bossshots"; dir = a[i + 1]; }
            if (a[i] == "-qaBranch") { mode = "branch"; dir = a[i + 1]; }
            if (a[i] == "-qaSpeedTime") { mode = "speedtime"; dir = a[i + 1]; }
            if (a[i] == "-qaTrade") { mode = "trade"; dir = a[i + 1]; }
        }
        if (mode == null) return;
        Application.runInBackground = true;
        var go = new GameObject("QaSweep");
        DontDestroyOnLoad(go);
        var q = go.AddComponent<QaSweep>();
        q.mode = mode; q.outDir = dir;
    }

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    string mode, outDir;
    readonly StringBuilder log = new StringBuilder();
    int failures, warnings;
    bool anyException;
    readonly List<string> exceptions = new List<string>();
    GameManager gm;
    PlayerController pc;
    int shotNo;

    void L(string s) { log.AppendLine(s); Debug.Log("[QA] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("[FAIL] " + what); } }
    void Warn(string what) { warnings++; L("[WARN] " + what); }

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        Application.logMessageReceived += (c, tr, type) =>
        {
            if (type == LogType.Exception) { anyException = true; if (exceptions.Count < 30) exceptions.Add(c + " | " + tr.Split('\n')[0]); }
        };
        yield return new WaitForSecondsRealtime(2f);
        StartCoroutine(AutoPickCards());
        if (mode == "visual") yield return VisualMode();
        else if (mode == "enemies") yield return EnemyMode();
        else if (mode == "ranged") yield return RangedMode();
        else if (mode == "falls") yield return FallsMode();
        else if (mode == "bossshots") yield return BossShotsMode();
        else if (mode == "branch") yield return BranchMode();
        else if (mode == "speedtime") yield return SpeedTimeMode();
        else if (mode == "trade") yield return TradeMode();
        else yield return FullRunMode();
        L("");
        foreach (var e in exceptions) L("[EXC] " + e);
        L($"failures={failures} warnings={warnings}");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"qa_{mode}.txt"), log.ToString());
        yield return new WaitForSecondsRealtime(0.5f);
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===================================================================== //
    // 共通
    // ===================================================================== //
    IEnumerator BeginRun(string ch, string stage)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage(stage);
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
    }

    IEnumerator EndRun()
    {
        stopKeepAlive = true;
        PlayerController.DebugSpeedScale = 1f;
        if (gm != null && gm.HasStarted)
        {
            var old = gm;
            gm.Retry();
            float w = 0f;
            while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        }
        yield return new WaitForSecondsRealtime(0.8f);
    }

    bool stopKeepAlive;
    IEnumerator KeepAlive()
    {
        var lives = typeof(GameManager).GetProperty("Lives");
        var setter = lives.GetSetMethod(true);
        while (!stopKeepAlive)
        {
            if (gm != null && gm.Lives < 50 && !gm.IsGameOver) setter.Invoke(gm, new object[] { 99 });
            yield return null;
        }
    }

    int cardPicks;
    IEnumerator AutoPickCards()
    {
        while (true)
        {
            var g = GameManager.Instance;
            if (g != null && g.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.25f);
                    seq.OnCardClicked(0);
                    cardPicks++;
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }
            }
            yield return null;
        }
    }

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"{shotNo++:0000}_{name}.png"));
    }

    object GetPrivate(object o, string field) { var f = o.GetType().GetField(field, NP); return f != null ? f.GetValue(o) : null; }
    void SetPrivate(object o, string field, object v) { var f = o.GetType().GetField(field, NP); if (f != null) f.SetValue(o, v); }

    // ===================================================================== //
    // 1) 見た目
    // ===================================================================== //
    readonly StringBuilder csv = new StringBuilder("char,move,frame,shot,sprite,visible,alpha,height,feetDy,grounded,facing,screenX,screenY,screenH\n");

    IEnumerator VisualMode()
    {
        var chars = CharacterDatabase.AllCharacters.Select(c => c.characterId).ToList();
        string only = Arg("-qaChars", "");
        if (only != "") chars = only.Split(',').ToList();
        foreach (string ch in chars)
        {
            yield return BeginRun(ch, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
            foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
            var tm = TerrainManager.Instance;
            tm.pitChanceBase = 0f; tm.pitChanceMax = 0f; tm.enemySpawnChance = 0f;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(o.gameObject);
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
            // 開始時に作られていた穴の区間を抜けるまで待つ(以後の地形は穴なし)
            float genEnd = FloatingOrigin.ToLogical(tm.GeneratedEndX);
            { float w = 0f; while (FloatingOrigin.ToLogical(pc.transform.position.x) < genEnd + 5f && w < 30f) { yield return null; w += Time.deltaTime; } }
            yield return new WaitForSeconds(0.5f);
            L($"--- {ch}");
            runFeet.Clear();
            var heights = new List<float>();
            yield return Capture(ch, "run", 10, 0.07f, heights, null);
            yield return Flick(PlayerController.FlickDirection.Up);
            yield return Capture(ch, "jump", 8, 0.07f, heights, null);
            yield return Flick(PlayerController.FlickDirection.Up);
            yield return Capture(ch, "doublejump", 8, 0.07f, heights, null);
            yield return WaitGrounded();
            yield return Flick(PlayerController.FlickDirection.Forward);
            yield return Capture(ch, "forward", 8, 0.06f, heights, 1f);
            yield return new WaitForSeconds(0.6f);
            yield return Flick(PlayerController.FlickDirection.Backward);
            yield return Capture(ch, "back", 8, 0.06f, heights, null);
            yield return new WaitForSeconds(0.6f);
            yield return WaitGrounded();
            yield return Flick(PlayerController.FlickDirection.Up); // 上攻撃(ジャンプに連動)
            yield return Capture(ch, "up", 6, 0.07f, heights, null);
            yield return new WaitForSeconds(0.15f);
            yield return Flick(PlayerController.FlickDirection.Down); // 空中の下攻撃
            yield return Capture(ch, "airdown", 8, 0.06f, heights, null);
            yield return WaitGrounded();
            yield return new WaitForSeconds(0.5f);
            yield return Flick(PlayerController.FlickDirection.Down); // 地上の下攻撃
            yield return Capture(ch, "grounddown", 6, 0.07f, heights, null);
            yield return new WaitForSeconds(0.8f);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            pc.TakeDamage(false, "qa");
            yield return Capture(ch, "hurt", 10, 0.08f, heights, null);
            yield return new WaitForSeconds(1.5f);
            yield return EndRun();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "frames.csv"), csv.ToString());

        // ステージの見た目(開始直後と各距離)
        foreach (string st in new[] { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor" })
        {
            yield return BeginRun("swordsman", st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            foreach (float d in new[] { 200f, 20000f, 50000f, 80000f, 98000f })
            {
                if (d > 300f) gm.DebugWarpToDistance(d);
                yield return new WaitForSeconds(4f);
                Shot($"stage_{st}_{(int)(d / 1000)}k");
                yield return null; yield return null;
            }
            yield return EndRun();
        }
    }

    readonly List<float> runFeet = new List<float>();
    IEnumerator WaitGrounded() { float w = 0f; while (!pc.IsGrounded && w < 3f) { yield return null; w += Time.deltaTime; } yield return new WaitForSeconds(0.1f); }

    IEnumerator Flick(PlayerController.FlickDirection f)
    {
        pc.debugInjectFlick = f;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
    }

    IEnumerator Capture(string ch, string move, int n, float interval, List<float> heights, float? expectFacing)
    {
        var anim = pc.GetComponent<PlayerAnimator>();
        var cam = Camera.main;
        for (int i = 0; i < n; i++)
        {
            var sr = anim != null ? anim.VisualRenderer : null;
            bool visible = sr != null && sr.enabled && sr.sprite != null && sr.gameObject.activeInHierarchy;
            float alpha = sr != null ? sr.color.a : 0f;
            Bounds b = visible ? sr.bounds : new Bounds(pc.transform.position, Vector3.zero);
            float h = b.size.y;
            float? g = TerrainManager.Instance.GetHeightAt(pc.transform.position.x);
            float feet = g.HasValue ? b.min.y - g.Value : 0f;
            float facing = Mathf.Sign(pc.transform.localScale.x) * (sr != null && sr.flipX ? -1f : 1f);
            Vector3 sp = cam.WorldToScreenPoint(b.center);
            float sh = cam.WorldToScreenPoint(b.max).y - cam.WorldToScreenPoint(b.min).y;
            string name = $"{ch}_{move}_{i}";
            Shot(name);
            csv.AppendLine($"{ch},{move},{i},{shotNo - 1},{(sr != null && sr.sprite != null ? sr.sprite.name : "null")},{visible},{alpha:F2},{h:F2},{feet:F2},{pc.IsGrounded},{facing},{sp.x:F0},{sp.y:F0},{sh:F0}");
            // 自動判定
            if (!visible) Check(false, $"{name}: player sprite not visible");
            else if (alpha < 0.25f && move != "hurt") Check(false, $"{name}: player almost transparent (alpha {alpha:F2})");
            if (visible && move == "run") { heights.Add(h); if (pc.IsGrounded && g.HasValue) runFeet.Add(feet); }
            if (visible && heights.Count >= 3 && move != "run")
            {
                float med = heights.OrderBy(x => x).ElementAt(heights.Count / 2);
                if (h > med * 1.9f || h < med * 0.45f) Warn($"{name}: size jump (height {h:F2} vs run {med:F2})");
            }
            // 足元: 走りの時の(絵の余白込みの)位置からのずれで判定
            if (visible && pc.IsGrounded && g.HasValue && move != "hurt" && move != "run" && runFeet.Count >= 3)
            {
                float med = runFeet.OrderBy(x => x).ElementAt(runFeet.Count / 2);
                if (Mathf.Abs(feet - med) > 0.5f) Warn($"{name}: feet position differs from running by {feet - med:F2}");
            }
            if (expectFacing.HasValue && i == 2 && facing != expectFacing.Value) Warn($"{name}: facing {facing} (expected {expectFacing.Value})");
            float t = 0f;
            while (t < interval) { yield return null; t += Time.deltaTime; }
        }
    }

    // ===================================================================== //
    // 2) 敵
    // ===================================================================== //
    class EnemyRec { public string id, band; public float spawnDist, spawnVx, spawnVy; public bool seen, killed, damaged, gone; public float minVy = 9, maxVy = -9; public int hp0; public GameObject go; public EnemyController ec; }

    IEnumerator EnemyMode()
    {
        string[] stages = { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor" };
        string only = Arg("-qaStages", "");
        if (only != "") stages = only.Split(',');
        foreach (string st in stages)
        {
            var prof = StageEncounterProfile.Find(st);
            if (prof == null) { Check(false, st + ": encounter profile"); continue; }
            L($"\n===== {st} ({prof.bands.Count} bands)");
            yield return BeginRun("swordsman", st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            foreach (var band in prof.bands)
            {
                float start = band.startDistance, end = band.endDistance < 0 ? start + 20000f : band.endDistance;
                float at = Mathf.Max(150f, start + Mathf.Min(300f, (end - start) * 0.3f));
                yield return BandRun(st, prof, band, at, 40f);
            }
            yield return EndRun();
        }

        // 全種類の敵を1体ずつ: 倒せるか(剣士)
        L("\n===== every enemy, one by one (swordsman, 20,000m)");
        var ids = new HashSet<string>();
        foreach (string st in stages) { var p = StageEncounterProfile.Find(st); if (p != null) foreach (var b in p.bands) foreach (var e in b.enemies) ids.Add(e.enemyId); }
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f;
        gm.DebugWarpToDistance(20000f);
        yield return new WaitForSeconds(1f);
        foreach (string id in ids.OrderBy(x => x)) yield return KillOne(id, 14f);
        yield return EndRun();

        // 高速で走りながら撃つ: 飛び道具が敵に届くか(走る速さが弾を追い越さないか)
        L("\n===== ranged at high speed (the shot must reach the enemy ahead before the player does)");
        foreach (string ch in new[] { "gunslinger", "archer", "mage", "miko" })
        {
            yield return BeginRun(ch, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f;
            foreach (float kmh in new[] { 120f, 250f, 400f })
            {
                SetKmh(kmh);
                yield return new WaitForSeconds(1.2f);
                yield return RangedShot(ch, kmh);
            }
            PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
    }

    // 10万mまでの単純走行の時間(敵/ボス/障害物/穴なし)。-qaCardFactor 1.2 等で「SPEED UPで最高速が1.2倍」の場合。
    // -qaTimeScale で早送り(計測はゲーム内の秒)。同じ速度の式を積分した理論値も出す。
    IEnumerator SpeedTimeMode()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float factor = float.Parse(Arg("-qaCardFactor", "1"), inv);
        float ts = float.Parse(Arg("-qaTimeScale", "6"), inv);
        string ch = Arg("-qaChar", "swordsman");
        yield return BeginRun(ch, "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var sp in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) sp.enabled = false;
        foreach (var sp in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) sp.enabled = false;
        var tm = TerrainManager.Instance;
        tm.pitChanceBase = 0f; tm.pitChanceMax = 0f; tm.enemySpawnChance = 0f;
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f);
        pc.runSpeed *= factor; // SPEED UPで全体が factor 倍(最高速 = 100 × factor km/h)
        // 理論値(同じ式を1mずつ積分)
        double theory = 0.0; float v0 = pc.runSpeed;
        for (int m = 0; m < 100000; m++) theory += 1.0 / (v0 * pc.NaturalMultiplierAt(m + 0.5f));
        L($"[speed] char={ch} base={GameManager.SpeedKmh(v0 / factor):F1}km/h factor={factor} top={GameManager.SpeedKmh(v0 * pc.NaturalCapMultiplier):F1}km/h ramp={pc.speedRampMode} +{pc.speedUpPer100m * 100f:F0}%/100m capAt={pc.NaturalCapDistance:F0}m theory100k={theory / 60.0:F1}min");
        TimeControl.SetDebugTimeScale(ts);
        float t0 = Time.time; float r0 = Time.realtimeSinceStartup;
        float next = 1000f; int respawns = 0, lives = gm.Lives;
        System.Action<string> tap = m => { if (m.StartsWith("[Damage]")) L($"[speed]   {m} at {gm.MaxDistance:F0}m {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h upper={pc.transform.position.y:F1} assist={(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.LastAction + "/" + HighSpeedAssist.Instance.CurrentStatus : "-")}"); };
        FreezeDiagnostics.EventTap += tap;
        var marks = new[] { 1000f, 2000f, 3000f, 3600f, 5000f, 10000f, 25000f, 50000f, 75000f, 100000f };
        int mi = 0;
        while (gm.MaxDistance < 100000f && !gm.IsGameOver)
        {
            if (Time.timeScale > 0.01f && Mathf.Abs(Time.timeScale - ts) > 0.01f && !gm.IsRewardSequenceWaitingForSelection) TimeControl.SetDebugTimeScale(ts);
            if (gm.Lives < lives) respawns++;
            lives = gm.Lives;
            while (mi < marks.Length && gm.MaxDistance >= marks[mi])
            {
                L($"[speed]   {marks[mi],7:F0}m at {(Time.time - t0) / 60f,6:F2}min  speed {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F1}km/h");
                mi++;
            }
            if (Time.realtimeSinceStartup - r0 > 3000f) { Check(false, "speed run timeout"); break; }
            yield return null;
        }
        FreezeDiagnostics.EventTap -= tap;
        TimeControl.SetDebugTimeScale(1f);
        float game = Time.time - t0;
        L($"[speed] RESULT factor={factor} top={GameManager.SpeedKmh(v0 * pc.NaturalCapMultiplier):F0}km/h: 100,000m in {game / 60f:F1}min (theory {theory / 60.0:F1}min) respawns={respawns} real={Time.realtimeSinceStartup - r0:F0}s");
    }

    // 上下ルートの分岐: 分岐の手前/上り切り/並走/合流を撮影(下の埋めと下ルートの間に空が見えないか)
    IEnumerator BranchMode()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var sp in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) sp.enabled = false;
        var tm = TerrainManager.Instance;
        int shots = 0;
        foreach (float at in new[] { 150f, 20000f, 60000f })
        {
            if (at > 200f) { gm.DebugWarpToDistance(at); yield return new WaitForSeconds(6f); }
            for (int k = 0; k < 2; k++)
            {
                float fork = 0f, merge = 0f; bool gen = false; float w = 0f;
                while (!tm.TryGetBranchAfter(pc.transform.position.x + 25f, out fork, out merge, out gen) && w < 30f) { yield return null; w += Time.deltaTime; }
                if (w >= 30f) { L("no branch found"); break; }
                float rampTop = fork + tm.BranchRampLength;
                var marks = new[] { ("fork", fork + 4f), ("ramptop", rampTop + 6f), ("mid", (fork + merge) * 0.5f + 5f), ("merge", merge + 2f) };
                foreach (var (name, mx) in marks)
                {
                    // カメラはプレイヤーの少し先を映すので、見たい地点が画面中央付近に来た時に撮る
                    while (pc.transform.position.x + 6f < mx) { SetKmh(40f); yield return null; }
                    Shot($"branch_{(int)(at / 1000)}k_{k}_{name}");
                    shots++;
                    yield return null;
                }
                L($"branch at {FloatingOrigin.ToLogical(fork):F0}m-{FloatingOrigin.ToLogical(merge):F0}m shot");
            }
        }
        PlayerController.DebugSpeedScale = 1f;
        int fills = 0; foreach (var t in tm.GetComponentsInChildren<MeshFilter>()) if (t.name == "BranchUndersideFill") fills++;
        L($"shots={shots} live underside fills={fills}");
    }

    // ボス戦の見え方: 関門の手前から走ってボス戦に入り、数秒おきに撮影+プレイヤー/地面/天井/ボスの位置を記録
    IEnumerator BossShotsMode()
    {
        string st = Arg("-qaStage", "last_corridor"), ch = Arg("-qaChar", "gunslinger");
        float kmh = float.Parse(Arg("-qaKmh", "260"), System.Globalization.CultureInfo.InvariantCulture);
        var gates = Arg("-qaGates", "10000,30000,40000,60000").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        yield return BeginRun(ch, st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        pc.AddAttackPower(12);
        var bm = BossManager.Instance;
        var tm = TerrainManager.Instance;
        var cam = Camera.main;
        foreach (float gate in gates)
        {
            typeof(GameManager).GetField("distanceExclusionOffset", NP)?.SetValue(gm, 0f);
            typeof(GameManager).GetField("distanceExclusionOffsetExact", NP)?.SetValue(gm, 0.0);
            gm.DebugWarpToDistance(gate - 400f);
            float w = 0f;
            while (!bm.IsBossPhase && w < 30f) { SetKmh(kmh); yield return null; w += Time.deltaTime; }
            float t0 = Time.time; int n = 0;
            while (bm.IsBossPhase && Time.time - t0 < 20f)
            {
                if (Time.time - t0 >= n * 1.0f)
                {
                    float px = pc.transform.position.x;
                    float? g = tm.GetHeightAt(px);
                    float? c = tm.GetCeilingHeightAt(px);
                    Vector3? b = NearestBoss();
                    Vector3 bv = b.HasValue ? cam.WorldToViewportPoint(b.Value) : new Vector3(-9, -9, 0);
                    Vector3 pv = cam.WorldToViewportPoint(pc.transform.position);
                    L($"[boss {gate / 1000f:F0}k {bm.BossMusicKey}] t={Time.time - t0:F1} player y-ground={(g.HasValue ? (pc.transform.position.y - g.Value).ToString("F2") : "PIT")} ceil-ground={(c.HasValue && g.HasValue ? (c.Value - g.Value).ToString("F1") : "-")} grounded={pc.IsGrounded} playerView=({pv.x:F2},{pv.y:F2}) bossView=({bv.x:F2},{bv.y:F2}) camSize={cam.orthographicSize:F1} speed={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}");
                    Shot($"bs_{(int)(gate / 1000)}k_{n}");
                    n++;
                }
                if (Time.time - lastBossFlick > 0.15f)
                {
                    Vector3? tp = NearestBoss();
                    if (tp.HasValue)
                    {
                        lastBossFlick = Time.time;
                        float dx = tp.Value.x - pc.transform.position.x, dy = tp.Value.y - pc.transform.position.y;
                        StartCoroutine(Flick(dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward));
                    }
                }
                yield return null;
            }
            w = 0f; while (bm.IsBossPhase && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSeconds(1f);
        }
    }
    float lastBossFlick;

    // 高速(自動操作補助ON)で各ステージの各距離を3,000mずつ走り、穴への落下を数える(穴の幅・天井の高さも記録)
    IEnumerator FallsMode()
    {
        string ch = Arg("-qaChar", "gunslinger");
        float kmh = float.Parse(Arg("-qaKmh", "260"), System.Globalization.CultureInfo.InvariantCulture);
        string[] stages = Arg("-qaStages", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        var chunksF = typeof(TerrainManager).GetField("chunks", NP);
        foreach (string st in stages)
        {
            yield return BeginRun(ch, st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            foreach (float at in new[] { 20000f, 60000f, 90000f })
            {
                typeof(GameManager).GetField("distanceExclusionOffset", NP)?.SetValue(gm, 0f);
                typeof(GameManager).GetField("distanceExclusionOffsetExact", NP)?.SetValue(gm, 0.0);
                gm.DebugWarpToDistance(at);
                yield return new WaitForSeconds(2f); // ワープ前に作られた地形を抜ける
                float d0 = gm.MaxDistance; int falls = 0, lives = gm.Lives; float t0 = Time.time;
                var widths = new List<string>();
                while (gm.MaxDistance < d0 + 3000f && Time.time - t0 < 120f)
                {
                    SetKmh(kmh);
                    if (gm.Lives < lives)
                    {
                        falls++;
                        float px = pc.transform.position.x;
                        float w = -1f;
                        foreach (var c in (IList)chunksF.GetValue(TerrainManager.Instance))
                        {
                            var ct = c.GetType();
                            if (ct.GetField("type").GetValue(c).ToString() != "Pit") continue;
                            float sx = (float)ct.GetField("startX").GetValue(c), ex = (float)ct.GetField("endX").GetValue(c);
                            if (ex > px - 12f && sx < px + 4f) { w = ex - sx; break; }
                        }
                        if (widths.Count < 12) widths.Add($"pit{w:F1}{(TerrainManager.Instance.HasCave ? $"/ceil{(TerrainManager.Instance.GetCeilingHeightAt(px) ?? 99f) - (TerrainManager.Instance.GetHeightAt(px - 6f) ?? 0f):F1}" : "")}");
                    }
                    lives = gm.Lives;
                    yield return null;
                }
                float run = gm.MaxDistance - d0;
                L($"[falls] {st,-15} from {at / 1000f:F0}km: {falls} falls in {run:F0}m ({falls * 1000f / Mathf.Max(1f, run):F1}/km)  {string.Join(" ", widths)}");
            }
            yield return EndRun();
        }
    }

    // 高速で撃った弾の動きを毎フレーム記録(自動操作補助はOFF)
    IEnumerator RangedMode()
    {
        string only = Arg("-qaChars", "gunslinger,archer,mage,miko");
        foreach (string ch in only.Split(','))
        {
            yield return BeginRun(ch, "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f;
            foreach (float kmh in new[] { 60f, 120f, 250f, 400f })
            {
                SetKmh(kmh);
                yield return new WaitForSeconds(1.5f);
                yield return RangedTrace(ch, kmh);
            }
            PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
    }

    IEnumerator RangedTrace(string ch, float kmh)
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        var def = EnemyDatabase.FindById("goblin");
        float ahead = 12f;
        // 敵までが平ら(同じ高さ)な所で撃つ(坂の上の敵には水平の弾は届かない=速度とは別の話)
        var tmr = TerrainManager.Instance;
        SetPrivate(pc, "mageLevel", 0); // 魔法使いは最低高度で撃つ(開始直後の自動補助の入力で高度が上がっていることがある)
        yield return new WaitForSeconds(0.4f);
        // 自分の足元から敵までが平らになった瞬間に置いて撃つ(待った後で位置がずれないように、判定の直後に置く)
        { float w = 0f; while (w < 8f && (!FlatAhead(tmr, pc.transform.position.x - 1f, ahead + 3f) || !pc.IsGrounded || pc.IsReacting)) { yield return null; w += Time.deltaTime; } }
        float x = pc.transform.position.x + ahead;
        float? gy = TerrainManager.Instance.GetHeightAt(x);
        var go = TerrainManager.Instance.SpawnEncounterEnemy(def, new Vector2(x, gy ?? pc.transform.position.y), EnemyAiTier.T0, def.behaviorKind);
        var ec = go.GetComponent<EnemyController>();
        var ecol = go.GetComponentInChildren<Collider2D>();
        int hp0 = ec.NetHp;
        var before = new HashSet<GameObject>();
        foreach (var b in FindObjectsByType<PlayerBullet>(FindObjectsSortMode.None)) before.Add(b.gameObject);
        foreach (var b in FindObjectsByType<KitProjectile>(FindObjectsSortMode.None)) before.Add(b.gameObject);
        yield return Flick(PlayerController.FlickDirection.Forward);
        var sb = new StringBuilder();
        float t = 0f; bool hit = false; string how = "";
        GameObject proj = null; float firedAt = -1f; float firedDx = 0f; int firedLevel = -1; string firedInfo = "";
        while (t < 1.2f && go != null)
        {
            if (proj == null)
            {
                foreach (var b in FindObjectsByType<PlayerBullet>(FindObjectsSortMode.None)) if (!before.Contains(b.gameObject)) proj = b.gameObject;
                foreach (var b in FindObjectsByType<KitProjectile>(FindObjectsSortMode.None)) if (!before.Contains(b.gameObject)) proj = b.gameObject;
                if (proj != null) { firedAt = t; firedDx = go.transform.position.x - pc.transform.position.x; firedLevel = pc.MageAltitudeLevel; firedInfo = $"proj={proj.name} projY={proj.transform.position.y - (gy ?? 0f):F2} playerY={pc.transform.position.y - (gy ?? 0f):F2} groundAtPlayer={(TerrainManager.Instance.GetHeightAt(pc.transform.position.x) ?? -99f) - (gy ?? 0f):F2} mageSurf={GetPrivate(pc, "mageSurface")} known={GetPrivate(pc, "mageSurfaceKnown")} gy={gy:F2}"; }
            }
            float pdx = go.transform.position.x - pc.transform.position.x;
            string pinfo = proj != null ? $"proj dx={go.transform.position.x - proj.transform.position.x:F2} y={proj.transform.position.y - (gy ?? 0f):F2}" : "no proj";
            if (sb.Length < 1800) sb.Append($"   t={t:F3} playerToEnemy={pdx:F2} {pinfo} enemyY=[{ecol.bounds.min.y - (gy ?? 0f):F2}..{ecol.bounds.max.y - (gy ?? 0f):F2}]\n");
            if (ec.NetHp < hp0 || ec.IsDying) { hit = true; how = $"hit at t={t:F3}, player {pdx:F2}m before the enemy"; break; }
            if (pdx < -1.5f) { how = "player passed the enemy"; break; }
            yield return null; t += Time.deltaTime;
        }
        L($"[ranged] {ch} {kmh:F0}km/h speed={pc.CurrentAutoRunSpeed:F1}m/s fired at t={firedAt:F3} (enemy {firedDx:F1}m ahead then): {(hit ? how : "MISS - " + how)}{(proj == null ? $" [no projectile: grounded={pc.IsGrounded} reacting={pc.IsReacting}]" : "")} mageLevelAtFire={firedLevel} {firedInfo} assist={(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.StatusText() : "-")}");
        if (!hit) L(sb.ToString().TrimEnd());
        Check(hit, $"{ch} at {kmh:F0}km/h: forward shot hits an enemy {ahead}m ahead");
        if (go != null) Destroy(go);
        yield return new WaitForSeconds(0.8f);
    }

    static bool FlatAhead(TerrainManager tm, float x0, float len)
    {
        float? g0 = tm.GetHeightAt(x0);
        if (!g0.HasValue) return false;
        for (float x = x0; x <= x0 + len; x += 0.5f)
        {
            float? g = tm.GetHeightAt(x);
            if (!g.HasValue || Mathf.Abs(g.Value - g0.Value) > 0.25f) return false;
            if (tm.GetSkyHeightAt(x).HasValue) return false; // 上ルートの下は除く(魔法使いは上ルートの高さを飛ぶ)
        }
        return true;
    }

    void SetKmh(float kmh)
    {
        PlayerController.DebugSpeedScale = 1f;
        float natural = pc.CurrentAutoRunSpeed;
        PlayerController.DebugSpeedScale = Mathf.Max(0.2f, kmh / GameManager.KmhPerMps / Mathf.Max(0.1f, natural));
    }

    IEnumerator RangedShot(string ch, float kmh)
    {
        var def = EnemyDatabase.FindById("goblin");
        float speed = pc.CurrentAutoRunSpeed;
        float ahead = 12f;
        float x = pc.transform.position.x + ahead;
        float? gy = TerrainManager.Instance.GetHeightAt(x);
        var go = TerrainManager.Instance.SpawnEncounterEnemy(def, new Vector2(x, gy ?? pc.transform.position.y), EnemyAiTier.T0, def.behaviorKind);
        var ec = go != null ? go.GetComponent<EnemyController>() : null;
        int hp0 = ec != null ? ec.NetHp : 0;
        yield return Flick(PlayerController.FlickDirection.Forward);
        float t = 0f; bool hit = false; float dxAtHit = 0f;
        while (t < 3f && go != null)
        {
            if (ec != null && (ec.NetHp < hp0 || ec.IsDying)) { hit = true; dxAtHit = go.transform.position.x - pc.transform.position.x; break; }
            if (go.transform.position.x < pc.transform.position.x - 1f) break; // 追い越した
            yield return null; t += Time.deltaTime;
        }
        L($"[ranged] {ch} {kmh:F0}km/h (speed {speed:F1}m/s): {(hit ? $"hit, enemy was {dxAtHit:F1}m ahead" : "MISSED (player overtook / shot never arrived)")}");
        Check(hit, $"{ch} at {kmh:F0}km/h: forward shot reaches an enemy {ahead}m ahead");
        if (go != null) Destroy(go);
        yield return new WaitForSeconds(0.8f);
    }

    // 1体を目の前に置いて、剣士ボットで倒す(近くに置き直しながら)
    IEnumerator KillOne(string id, float timeout)
    {
        var def = EnemyDatabase.FindById(id);
        if (def == null) { Check(false, id + ": enemy definition"); yield break; }
        var tm = TerrainManager.Instance;
        float x = pc.transform.position.x + 6f;
        float? gy = tm.GetHeightAt(x);
        bool flying = def.movementType == EnemyMovementType.Flying;
        Vector2 pos = new Vector2(x, (gy ?? pc.transform.position.y) + (flying ? 2.0f : 0f));
        var go = tm.SpawnEncounterEnemy(def, pos, def.aiTier, def.behaviorKind);
        var ec = go != null ? go.GetComponent<EnemyController>() : null;
        if (ec == null) { Check(false, id + ": spawned"); yield break; }
        int hp0 = ec.NetHp;
        float t = 0f, lastFlick = -1f; bool killed = false; bool everVisible = false;
        var cam = Camera.main;
        while (t < timeout)
        {
            if (go == null || ec.IsDying) { killed = true; break; }
            var r = go.GetComponentInChildren<SpriteRenderer>();
            if (r != null && r.isVisible) everVisible = true;
            Vector3 ep = go.transform.position;
            float dx = ep.x - pc.transform.position.x;
            // 追い越した/遠すぎる: 目の前に置き直す(倒せるかどうかだけを見る)
            if (dx < -0.8f || dx > 9f)
            {
                float nx = pc.transform.position.x + 3.5f;
                float? ng = tm.GetHeightAt(nx);
                go.transform.position = new Vector3(nx, (ng ?? ep.y) + (flying ? 2.0f : ep.y - (tm.GetHeightAt(ep.x) ?? ep.y)), ep.z);
            }
            if (t - lastFlick > 0.18f)
            {
                lastFlick = t;
                float dy = ep.y - pc.transform.position.y;
                if (flying && dy > 1.6f && pc.IsGrounded) StartCoroutine(Flick(PlayerController.FlickDirection.Up));
                else StartCoroutine(Flick(PlayerController.FlickDirection.Forward));
            }
            yield return null; t += Time.deltaTime;
        }
        int hp1 = go != null && ec != null ? ec.NetHp : 0;
        L($"[kill] {id,-18} hp={hp0} {(killed ? $"KILLED in {t:F1}s" : $"NOT killed in {timeout}s (hp {hp0}->{hp1})")} visible={everVisible}");
        Check(killed || hp1 < hp0, $"{id}: takes damage from the player's attacks");
        if (!killed) Warn($"{id}: not killed within {timeout}s by the bot (hp {hp0}->{hp1})");
        Check(everVisible, $"{id}: visible on screen");
        if (go != null) Destroy(go);
        yield return new WaitForSeconds(0.4f);
    }

    IEnumerator BandRun(string st, StageEncounterProfile prof, EncounterDistanceBand band, float at, float seconds)
    {
        var recs = new List<EnemyRec>();
        var cam = Camera.main;
        System.Action<EncounterDirector.Record, EncounterFormation, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)>> h = (rec, f, list) =>
        {
            foreach (var e in list)
            {
                if (e.go == null || e.def == null) continue;
                Vector3 v = cam.WorldToViewportPoint(e.go.transform.position);
                var r = new EnemyRec { id = e.def.enemyId, band = rec.band, spawnDist = rec.distance, spawnVx = v.x, spawnVy = v.y, go = e.go, ec = e.go.GetComponent<EnemyController>() };
                r.hp0 = r.ec != null ? r.ec.NetHp : 0;
                recs.Add(r);
            }
        };
        EncounterDirector.OnEncounterSpawned += h;
        typeof(GameManager).GetField("distanceExclusionOffset", NP)?.SetValue(gm, 0f);
        typeof(GameManager).GetField("distanceExclusionOffsetExact", NP)?.SetValue(gm, 0.0);
        gm.DebugWarpToDistance(at);
        yield return new WaitForSeconds(1f);
        float d0 = gm.MaxDistance, t = 0f, lastFlick = 0f;
        float lastProgressX = FloatingOrigin.ToLogical(pc.transform.position.x), lastProgressT = 0f;
        int stuck = 0;
        while (t < seconds)
        {
            // ボット: 近い敵を攻撃(空中の敵は上攻撃)
            if (t - lastFlick > 0.2f)
            {
                GameObject target = null; float best = 99f;
                foreach (var r in recs)
                {
                    if (r.go == null || r.ec == null || r.ec.IsDying) continue;
                    float dx = r.go.transform.position.x - pc.transform.position.x;
                    if (dx > -0.5f && dx < 5f && dx < best) { best = dx; target = r.go; }
                }
                if (target != null)
                {
                    lastFlick = t;
                    float dy = target.transform.position.y - pc.transform.position.y;
                    StartCoroutine(Flick(dy > 1.8f && pc.IsGrounded ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward));
                }
            }
            foreach (var r in recs)
            {
                if (r.gone) continue;
                if (r.go == null || (r.ec != null && r.ec.IsDying)) { if (r.ec == null || r.ec.IsDying || r.go == null) { r.killed = r.killed || (r.ec != null && r.ec.IsDying) || r.go == null; } r.gone = true; continue; }
                if (r.ec != null && r.ec.NetHp < r.hp0) r.damaged = true;
                Vector3 v = cam.WorldToViewportPoint(r.go.transform.position);
                if (v.x > 0f && v.x < 1f && v.y > -0.05f && v.y < 1.05f) { r.seen = true; r.minVy = Mathf.Min(r.minVy, v.y); r.maxVy = Mathf.Max(r.maxVy, v.y); }
                if (v.x < -0.3f) r.gone = true; // 追い越した(生き残り)
            }
            // 詰まり: 3秒進まない(被弾中・カード選択中を除く)
            float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
            if (lx > lastProgressX + 1f || pc.IsReacting || gm.IsRewardSequenceWaitingForSelection || Time.timeScale < 0.5f) { lastProgressX = lx; lastProgressT = t; }
            else if (t - lastProgressT > 3f) { stuck++; Check(false, $"{st} {band.bandName}: player stuck at {gm.MaxDistance:F0}m (y={pc.transform.position.y:F2})"); Shot($"stuck_{st}"); lastProgressT = t; }
            yield return null; t += Time.deltaTime;
        }
        EncounterDirector.OnEncounterSpawned -= h;
        // 集計
        var bandIds = new HashSet<string>(band.enemies.Select(e => e.enemyId));
        var byId = recs.GroupBy(r => r.id).OrderBy(g => g.Key);
        L($"--- {band.bandName} run {d0:F0}->{gm.MaxDistance:F0}m  encounters' enemies={recs.Count} stuck={stuck}");
        foreach (var g in byId)
        {
            int n = g.Count(), seen = g.Count(r => r.seen), killed = g.Count(r => r.killed), dmg = g.Count(r => r.damaged || r.killed);
            float popIn = g.Min(r => r.spawnVx);
            L($"    {g.Key,-18} n={n} seen={seen} killed={killed} damaged={dmg} spawnViewX(min)={popIn:F2} viewY={g.Min(r => r.minVy):F2}..{g.Max(r => r.maxVy):F2}");
            if (!bandIds.Contains(g.Key)) Check(false, $"{st} {band.bandName}: {g.Key} is not an enemy of this distance band");
            if (popIn < 0.9f && popIn > 0.05f) Warn($"{st} {g.Key}: appeared inside the screen (viewport x {popIn:F2})");
            if (seen == 0) Warn($"{st} {g.Key}: never seen on screen in this run ({n} spawned)");
            if (g.Max(r => r.maxVy) > 1.02f || g.Min(r => r.minVy) < -0.02f) Warn($"{st} {g.Key}: partly outside the screen vertically");
        }
        var missing = bandIds.Where(i => !recs.Any(r => r.id == i)).ToList();
        if (missing.Count > 0) L($"    (not spawned in this {seconds:F0}s run: {string.Join(",", missing)})");
    }

    // ===================================================================== //
    // 3) 0→100,000m
    // ===================================================================== //
    IEnumerator FullRunMode()
    {
        string st = Arg("-qaStage", "last_corridor"), ch = Arg("-qaChar", "gunslinger");
        float kmh = float.Parse(Arg("-qaKmh", "260"), System.Globalization.CultureInfo.InvariantCulture);
        int powerAdd = int.Parse(Arg("-qaPower", "12"));
        // 2026-09-30: -qaKmh 0 = 自然加速のまま(カードの効果も含めて実際のRunどおり)。-qaTimeScale で早送り。
        bool natural = kmh <= 0f;
        float stopAt = float.Parse(Arg("-qaStopAt", "0"), System.Globalization.CultureInfo.InvariantCulture); // 調査用: この距離で止める
        float ts = float.Parse(Arg("-qaTimeScale", "1"), System.Globalization.CultureInfo.InvariantCulture);
        L($"full run: stage={st} char={ch} target speed={(natural ? "natural" : kmh + "km/h")} attack power +{powerAdd} timeScale={ts}");
        yield return BeginRun(ch, st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        pc.AddAttackPower(powerAdd);
        var bm = BossManager.Instance;
        float t0 = Time.realtimeSinceStartup, g0 = Time.time; bool reported100k = false; float maxKmh = 0f;
        if (ts != 1f) TimeControl.SetDebugTimeScale(ts);
        // 被弾の理由を数える(ボス戦中/道中で分ける)
        var dmgWhy = new Dictionary<string, int>();
        bool inBossForTap = false;
        System.Action<string> dmgTap = m =>
        {
            if (!m.StartsWith("[Damage] Hit reason=")) return;
            string r = m.Substring("[Damage] Hit reason=".Length); int sp = r.IndexOf(' '); if (sp > 0) r = r.Substring(0, sp);
            string key = (inBossForTap ? "boss:" : "road:") + r;
            dmgWhy[key] = dmgWhy.TryGetValue(key, out int c) ? c + 1 : 1;
        };
        FreezeDiagnostics.EventTap += dmgTap;
        // ボットは穴を跳ばないので、自然速度(100km/h未満)では補助を全速度で最大にして「上手なプレイヤー」の代わりにする
        if (natural && HighSpeedAssist.Instance != null) { var hsa = HighSpeedAssist.Instance; hsa.engageKmh = 1f; hsa.releaseKmh = 0.5f; hsa.fullAssistKmh = 2f; }
        float lastLog = -1f, lastFlick = 0f;
        float lastProgressX = FloatingOrigin.ToLogical(pc.transform.position.x), lastProgressT = Time.time;
        int stuck = 0, bossFights = 0, bossKills = 0, bossForced = 0, respawns = 0;
        var bossLog = new List<string>();
        var shotKinds = new HashSet<string>();
        bool inBoss = false; float bossStart = 0f; string bossLabel = "";
        int prevLives = gm.Lives;
        while (!gm.IsGameOver)
        {
            float d = gm.MaxDistance;
            // 速さ(ボス戦中も同じ)
            if (!inBoss && d < 99900f) { if (natural) PlayerController.DebugSpeedScale = 1f; else SetKmh(kmh); }
            if (ts != 1f && Time.timeScale > 0.01f && Mathf.Abs(TimeControl.DebugTimeScale - ts) > 0.01f && d < 99500f) TimeControl.SetDebugTimeScale(ts);
            if (d >= 99500f && TimeControl.DebugTimeScale != 1f) TimeControl.SetDebugTimeScale(1f); // 死神の場面は等倍で
            if (stopAt > 0f && d >= stopAt) { L($"stop at {d:F0}m (game {(Time.time - g0) / 60f:F1}min): damage by reason: " + string.Join(", ", dmgWhy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))); break; }
            maxKmh = Mathf.Max(maxKmh, GameManager.SpeedKmh(pc.CurrentAutoRunSpeed));
            inBossForTap = inBoss || bm.IsBossPhase;
            if (!reported100k && d >= 100000f) { reported100k = true; L($"reached 100,000m at game time {(Time.time - g0) / 60f:F1}min (max speed {maxKmh:F0}km/h, cards picked {cardPicks})"); }
            // 99,500mで無敵を切る(死神に捕まるため)
            if (d >= 99500f && gm.InvincibleMode) { typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false); L($"{d:F0}m: invincible off (for the reaper)"); }
            if (d >= 99900f) { stopKeepAlive = true; PlayerController.DebugSpeedScale = Mathf.Min(PlayerController.DebugSpeedScale, 1f); }

            // ボス
            if (bm.IsBossPhase && !bm.DeathSpawned && !inBoss) { inBoss = true; bossStart = Time.time; bossFights++; bossLabel = null; }
            if (inBoss)
            {
                if (bossLabel == null && !string.IsNullOrEmpty(bm.BossMusicKey))
                {
                    bossLabel = $"{d:F0}m {bm.BossMusicKey} alive={bm.AliveBossCount}";
                    string kind = bm.BossMusicKey.Split('/').Last();
                    if (shotKinds.Add(kind)) StartCoroutine(ShotLater($"boss_{kind}", 1.5f));
                }
                // 攻撃: いちばん近いボスへ
                if (Time.time - lastFlick > 0.15f)
                {
                    Vector3? tp = NearestBoss();
                    if (tp.HasValue)
                    {
                        lastFlick = Time.time;
                        float dx = tp.Value.x - pc.transform.position.x, dy = tp.Value.y - pc.transform.position.y;
                        PlayerController.FlickDirection f = dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
                        StartCoroutine(Flick(f));
                    }
                }
                if (!bm.IsBossPhase || bm.BossDefeatedThisPhase)
                {
                    float dur = Time.time - bossStart;
                    bossLog.Add($"{bossLabel ?? d.ToString("F0") + "m ?"} -> defeated in {dur:F1}s");
                    bossKills++;
                    inBoss = false;
                    float w = 0f; while (bm.IsBossPhase && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
                }
                else if (Time.time - bossStart > 75f)
                {
                    // 倒せない: 記録してから倒して先へ(残りの確認を続けるため)
                    bossLog.Add($"{bossLabel} -> NOT defeated by attacks in 75s (forced)");
                    Check(false, $"boss not defeated by the bot's attacks: {bossLabel}");
                    bossForced++;
                    Shot("boss_notdefeated");
                    foreach (var w2 in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w2.IsDead) w2.TakeDamage(99999, w2.CenterWorld);
                    foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
                    foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
                    bossStart = Time.time; // 念のため再計時
                }
            }
            else if (Time.time - lastFlick > 0.25f)
            {
                // 道中: 目の前の敵を攻撃
                GameObject target = null; float best = 99f;
                foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                {
                    if (ec.IsDying) continue;
                    float dx = ec.transform.position.x - pc.transform.position.x;
                    if (dx > -0.3f && dx < 5f && dx < best) { best = dx; target = ec.gameObject; }
                }
                if (target != null)
                {
                    lastFlick = Time.time;
                    float dy = target.transform.position.y - pc.transform.position.y;
                    StartCoroutine(Flick(dy > 1.8f && pc.IsGrounded ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward));
                }
            }

            // 詰まり
            float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
            bool paused = inBoss || pc.IsReacting || gm.IsRewardSequenceWaitingForSelection || Time.timeScale < 0.5f || bm.IsBossPhase;
            if (lx > lastProgressX + 1f || paused) { lastProgressX = lx; lastProgressT = Time.time; }
            else if (Time.time - lastProgressT > 4f)
            {
                stuck++;
                Check(false, $"player stuck at {d:F0}m (y={pc.transform.position.y:F2} grounded={pc.IsGrounded})");
                Shot($"stuck_{(int)d}");
                lastProgressT = Time.time;
            }
            if (gm.Lives < prevLives) respawns++;
            prevLives = gm.Lives;

            // 進み具合
            if (d - lastLog >= 5000f)
            {
                lastLog = d;
                if (dmgWhy.Count > 0) L("   damage by reason: " + string.Join(", ", dmgWhy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
                L($"{d,7:F0}m  game {(Time.time - g0) / 60f,5:F1}min  real {Time.realtimeSinceStartup - t0,6:F0}s  speed {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h  lv? cards={cardPicks} bosses {bossKills}/{bossFights} stuck={stuck} hits(lives lost)={respawns} fps~{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}");
                if (((int)(d / 5000f)) % 2 == 0) StartCoroutine(ShotLater($"run_{(int)(d / 1000)}k", 0f));
            }
            if (Time.realtimeSinceStartup - t0 > 5400f) { Check(false, "full run did not finish in 90 minutes"); break; }
            yield return null;
        }
        L("");
        L("bosses:");
        foreach (var b in bossLog) L("  " + b);
        var r = ReaperBase.Active;
        L($"end: gameOver={gm.IsGameOver} distance={gm.MaxDistance:F0}m reaperSpawned={bm.DeathSpawned} reaper={(r != null ? r.GetType().Name : "-")} bossFights={bossFights} defeated={bossKills} forced={bossForced} stuck={stuck} real={Time.realtimeSinceStartup - t0:F0}s");
        Check(gm.IsGameOver && bm.DeathSpawned && gm.MaxDistance >= 99990f, "ran to 100,000m and the reaper ended the run");
        Check(bossKills >= 99, $"all 99 regular boss gates fought and defeated ({bossKills})");
        yield return new WaitForSecondsRealtime(1.5f);
        Shot("gameover");
    }

    IEnumerator ShotLater(string name, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Shot(name);
    }

    Vector3? NearestBoss()
    {
        Vector3? best = null; float bd = float.MaxValue;
        Vector3 p = pc.transform.position;
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) { float d = (w.CenterWorld - p).sqrMagnitude; if (d < bd) { bd = d; best = w.CenterWorld; } }
        foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) { float d = (x.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = x.transform.position; } }
        foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) { float d = (x.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = x.transform.position; } }
        return best;
    }
}
#endif

#if UNITY_EDITOR
public static class QaSweepMenu
{
    // Editorのbatchから: -executeMethod QaSweepMenu.Run -qaRanged <dir> 等(引数はそのまま読まれる)
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        UnityEditor.EditorApplication.EnterPlaymode();
    }
}
#endif

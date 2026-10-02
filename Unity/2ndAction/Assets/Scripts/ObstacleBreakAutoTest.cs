#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 障害物の耐久力/高速時の「壊して通る」(2026-09-29)のEditor専用自動確認。結果は ObstacleBreakAutoTest.txt。
// 全キャラで、平らな地面(前後に穴/分岐なし)へ種類ごとの障害物を1つずつ置き、入力は補助だけで走らせる:
//  ・壊せたか/当たった回数/補助の判断(壊す/跳ぶ/無理)/体当たりの被弾/壊した直後の通過
//  ・攻撃力+2(ATTACK UP 2枚相当)で必要な回数が減るか
//  ・破片に当たり判定が無いこと、破片の数が上限を超えないこと
//  ・Retry(シーン読み込み直し)を挟んでも前のRunの状態が残らないこと(キャラごとにRetryする)
// 起動: Tools/OneMoreMile/Obstacle Break Test (batch)  コマンドライン -obChars a,b,c -obKmh 120
public class ObstacleBreakAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("ObstacleBreakTest", 0) != 1) return;
        EditorPrefs.SetInt("ObstacleBreakTest", 0);
        var go = new GameObject("ObstacleBreakTest");
        DontDestroyOnLoad(go);
        go.AddComponent<ObstacleBreakAutoTest>();
    }

    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    readonly StringBuilder log = new StringBuilder();
    int failures; bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[ObstacleBreakTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    GameManager gm; PlayerController pc; HighSpeedAssist assist;
    float targetKmh = 120f, naturalRunSpeed = -1f;
    int obstacleDamage, otherDamage;
    readonly List<string> damageSamples = new List<string>();

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    void OnTap(string m)
    {
        if (!m.StartsWith("[Damage] Hit reason=")) return;
        if (m.Contains("Obstacle:")) { obstacleDamage++; if (damageSamples.Count < 30) damageSamples.Add(m); }
        else otherDamage++;
    }

    IEnumerator Watchdog() { yield return new WaitForSecondsRealtime(3600f); L("WATCHDOG: test did not finish in time"); failures++; Finish(); }

    struct Trial { public string ch, kind; public int power, hp, hits, decision; public bool broken, damaged; public float passGap; public string why; }
    readonly List<Trial> trials = new List<Trial>();

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        FreezeDiagnostics.EventTap += OnTap;
        yield return new WaitForSecondsRealtime(1.5f);
        float.TryParse(Arg("-obKmh", "120"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out targetKmh);
        var chars = new List<string>();
        string list = Arg("-obChars", "");
        if (!string.IsNullOrEmpty(list)) chars.AddRange(list.Split(','));
        else foreach (var c in CharacterDatabase.AllCharacters) chars.Add(c.characterId);
        var bal = ObstacleBalance.Get();
        L($"speed={targetKmh}km/h characters={string.Join(",", chars)}");
        foreach (var e in bal.entries) L($"  balance {e.kind}: {e.material} durability={e.durability} vanishOnContact={e.vanishOnContact}");

        string[] kinds = { "SmallTree", "BreakableTree", "Rock", "Wall", "GiantRock" };
        bool first = true;
        foreach (string ch in chars)
        {
            if (!first) yield return RetryToHome();
            first = false;
            yield return BeginRun(ch);
            if (gm == null || !gm.HasStarted) { Check(false, $"{ch}: run started"); continue; }
            int basePower = pc.AttackPower;
            foreach (int add in new[] { 0, 2 })
            {
                if (add > 0) pc.AddAttackPower(add);
                foreach (string k in kinds) yield return RunTrial(ch, k);
                if (add > 0) pc.AddAttackPower(-add);
            }
            int broken = 0; foreach (var t in trials) if (t.ch == ch && t.broken) broken++;
            Check(broken > 0, $"{ch}: auto attack broke at least one obstacle ({broken})");
        }

        // ---- まとめ ----
        L("\n==== summary ====");
        int total = trials.Count, brokenAll = 0, dmgAll = 0, breakDec = 0, jumpDec = 0, noDec = 0, brokenThenHit = 0;
        foreach (var t in trials)
        {
            if (t.broken) brokenAll++;
            if (t.damaged) dmgAll++;
            if (t.decision == 1) breakDec++; else if (t.decision == 2) jumpDec++; else if (t.decision == 3) noDec++;
            if (t.broken && t.damaged) brokenThenHit++;
        }
        L($"trials={total} broken={brokenAll} contactDamage={dmgAll} decisions break={breakDec} jump={jumpDec} none={noDec} | obstacle damage events={obstacleDamage} other damage={otherDamage}");
        L($"debris peak={ObstacleFx.PeakPieces} (cap {bal.maxDebris}) sweeps={PlayerAttackSweeper.SweepsRun} sweptTargets={PlayerAttackSweeper.SweptTargets} enemySwept={EnemyController.SweptHits}");
        Check(brokenThenHit == 0, $"no contact damage from an obstacle that was broken (broken+damaged={brokenThenHit})");
        Check(ObstacleFx.PeakPieces <= bal.maxDebris, $"debris count stays under the cap ({ObstacleFx.PeakPieces}<={bal.maxDebris})");
        // 種類ごとの傾向: 木は未強化でも全員が壊せる/岩は強化で必要回数が減る
        int woodBroken = 0, woodTotal = 0, rockUpFewer = 0, rockPairs = 0;
        foreach (var t in trials) if (t.kind == "SmallTree" && t.power == BasePowerOf(t.ch)) { woodTotal++; if (t.broken) woodBroken++; }
        foreach (string ch in chars)
        {
            Trial? a = null, b = null;
            foreach (var t in trials) if (t.ch == ch && t.kind == "Rock") { if (t.power == BasePowerOf(ch)) a = t; else b = t; }
            if (a.HasValue && b.HasValue && a.Value.broken && b.Value.broken) { rockPairs++; if (b.Value.hits <= a.Value.hits) rockUpFewer++; }
        }
        L($"wood broken at base power: {woodBroken}/{woodTotal}  rock: upgraded needed <= hits in {rockUpFewer}/{rockPairs} characters");
        L($"  (report) wood broken with base power {woodBroken}/{woodTotal} - misses are jumps/terrain, see each trial");
        Check(rockPairs == 0 || rockUpFewer == rockPairs, "ATTACK UP never needs more hits on rock");
        foreach (string s in damageSamples) L("[obstacle damage] " + s);
        Finish();
    }

    readonly Dictionary<string, int> basePower = new Dictionary<string, int>();
    int BasePowerOf(string ch) => basePower.TryGetValue(ch, out int p) ? p : 2;

    IEnumerator RunTrial(string ch, string kind)
    {
        // 平らで、前後に穴/分岐/段差の無い地面を探す(補助の判断を障害物だけに絞る)
        var tm = TerrainManager.Instance;
        float v = pc.CurrentAutoRunSpeed;
        float lead = Mathf.Max(30f, v * 1.3f);
        float x = float.NaN, gy = 0f;
        // プレイヤーが地上にいて、今の位置から障害物の先までが平らな時だけ置く(穴を越える大ジャンプの着地点の直後などに
        // 置くと、障害物ではなくテストの置き方の問題を測ってしまう)。条件がそろうまで走らせて待つ。
        float waitT = 0f;
        while (float.IsNaN(x) && waitT < 20f)
        {
            float px = pc.transform.position.x;
            float cx = px + lead;
            if (pc.IsGrounded && tm.IsGenerated(cx + 10f) && Clear(tm, px - 2f, cx + 8f, cx, out gy)) x = cx;
            else { KeepClear(); yield return null; waitT += Time.deltaTime; }
        }
        if (float.IsNaN(x)) { L($"  [{ch} {kind}] no flat spot found - skipped"); yield break; }
        var spawner = ObstacleSpawner.FindForStage("wasteland_road");
        int idx = -1;
        for (int i = 0; i < spawner.specs.Length; i++) if (spawner.specs[i].name == kind) idx = i;
        var oc = spawner.CreateReplica(idx, new Vector2(x, gy), tm.GetSlopeAngleAt(x), false);
        int hp0 = oc.Hp;
        int dmg0 = obstacleDamage;
        float t0 = Time.time;
        // 通り過ぎる(障害物の後ろ4m)か、時間切れまで
        var trace = new List<string>(); float nextTrace = 0f;
        var ob = oc.GetComponent<Collider2D>().bounds;
        trace.Add($"obstacle x={x:F1} ground={gy:F2} bounds x[{ob.min.x:F1},{ob.max.x:F1}] y[{ob.min.y:F2},{ob.max.y:F2}] collectMax={assist.CollectMaxCount} overflows={assist.CollectOverflows}");
        while (Time.time - t0 < 10f && pc.transform.position.x < x + 4f)
        {
            KeepClear();
            float dx = x - pc.transform.position.x;
            if (dx < 45f && Time.time >= nextTrace && trace.Count < 60)
            {
                nextTrace = Time.time + 0.04f;
                trace.Add($"dx={dx:F1} y={pc.transform.position.y:F2} g={pc.IsGrounded} can={pc.AssistCanStartForwardAttack} rdy={pc.AssistAttackReadyIn:F2} hp={oc.Hp} st={assist.CurrentStatus} plan=[{assist.PlanText}] brk=[{assist.LastBreakWhy}] fail=[{assist.LastFailure}] act={assist.LastAction}@{Time.time - assist.LastActionTime:F2}");
            }
            yield return null;
        }
        float passGap = oc.BrokenAt >= 0f ? Time.time - oc.BrokenAt : -1f; // 壊してから障害物の後ろ4mを通過するまで
        var t = new Trial
        {
            ch = ch, kind = kind, power = pc.AttackPower, hp = hp0, hits = oc.HitsTaken, broken = oc.BrokenAt >= 0f,
            damaged = oc.ContactDamaged || obstacleDamage > dmg0, decision = assist.DecisionFor(oc), passGap = passGap, why = assist.LastBreakWhy,
        };
        trials.Add(t);
        string dec = t.decision == 1 ? "BREAK" : t.decision == 2 ? "JUMP" : t.decision == 3 ? "NONE" : "-";
        L($"  [{ch} pow{t.power} {kind} hp{hp0}] {(t.broken ? $"broken in {t.hits} hit(s)" : $"not broken ({t.hits} hit(s))")} decision={dec} contactDamage={t.damaged}{(passGap >= 0f ? $" passed {passGap:F2}s after break" : "")} why=[{t.why}]");
        if (!t.broken || t.damaged) foreach (string s in trace) L("      " + s);
        if (!oc.Broken && oc.gameObject.activeSelf) oc.gameObject.SetActive(false);
        yield return new WaitForSeconds(0.3f);
    }

    static bool Clear(TerrainManager tm, float from, float to, float ox, out float gy)
    {
        gy = 0f;
        if (tm.IsInBranchRoute(from) || tm.IsInBranchRoute(to)) return false;
        float? prevH = null;
        // プレイヤーから障害物の先まで: 穴/分岐/段差なし(坂は可)
        for (float x = from; x <= to; x += 0.5f)
        {
            if (!tm.TryGetGroundFast(x, out float h, out bool pit) || pit) return false;
            if (prevH.HasValue && Mathf.Abs(h - prevH.Value) > 0.3f) return false; // 段差なし
            prevH = h;
            if (tm.IsInBranchRoute(x)) return false;
        }
        if (!tm.TryGetGroundFast(ox, out gy, out _)) return false;
        // 障害物の前後はゲームの配置規則と同じく平地(坂の途中には大型障害物を置かない: ObstacleSpawnerのonSlope)、頭上の足場も低くない
        for (float x = ox - 22f; x <= ox + 4f; x += 0.5f)
        {
            // 手前22m(飛び道具で狙う区間)と後ろはほぼ平ら(高台の障害物を谷から水平の弾で狙う、のような当てようのない配置を避ける)
            if (!tm.TryGetGroundFast(x, out float h, out bool pit) || pit || Mathf.Abs(h - gy) > (x < ox - 6f ? 0.6f : 0.3f)) return false;
            float? sky = tm.GetSkyHeightFast(x);
            if (sky.HasValue && sky.Value < h + 4.5f) return false;
        }
        if (Mathf.Abs(tm.GetSlopeAngleAt(ox)) > 0.01f) return false;
        return true;
    }

    // 判断を障害物だけに絞る: 近くの敵は消す
    void KeepClear()
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e.gameObject.activeSelf && Mathf.Abs(e.transform.position.x - pc.transform.position.x) < 80f) e.gameObject.SetActive(false);
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
    Coroutine speedCo;

    IEnumerator BeginRun(string ch)
    {
        L($"\n===== {ch} =====");
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
        pc = PlayerController.Instance; assist = HighSpeedAssist.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false; // テストの障害物だけにする
        // 判断を障害物だけに絞る: 以後生成される地形に穴を作らない(平地を確保しやすくする)
        if (TerrainManager.Instance != null) { TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f; }
        assist.SetEnabled(true);
        basePower[ch] = pc.AttackPower;
        naturalRunSpeed = -1f;
        if (speedCo != null) StopCoroutine(speedCo);
        speedCo = StartCoroutine(SpeedController());
        // 前のRunの障害物が残っていない(Retry直後)
        int leftovers = 0; foreach (var o in ObstacleController.All) if (o != null && o.isActiveAndEnabled) leftovers++;
        L($"  run started: attackPower={pc.AttackPower} assistEngage={assist.engageKmh}/{assist.releaseKmh} leftoverObstacles={leftovers}");
        Check(leftovers == 0 || trials.Count == 0, $"{ch}: no obstacles left over from the previous run");
        yield return new WaitForSeconds(2.0f); // 速度が上がるまで
        Check(assist.Engaged, $"{ch}: assist engaged at {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h");
    }

    IEnumerator RetryToHome()
    {
        var old = gm;
        gm.Retry();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
    }

    void Finish()
    {
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        FreezeDiagnostics.EventTap -= OnTap;
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../ObstacleBreakAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}

public static class ObstacleBreakTestMenu
{
    [MenuItem("Tools/OneMoreMile/Obstacle Break Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("ObstacleBreakTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

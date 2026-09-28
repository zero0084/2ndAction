#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 共通Encounter Systemの荒野街道(Stage01)展開(2026-09-28) - Editor専用の自動確認。結果は WastelandEncounterAutoTest.txt。
//  1) 荒野街道で共通Directorが荒野用Profileで動く/上下完全分岐が残っている/従来の上ルート用スポナーは止まる
//  2) 距離Band(0/500/1500/3000/5000m相当+高速)を走り、出たEncounterを1つずつ検証
//     (地上の敵は地面/上ルートの敵は上ルートの面、空中は飛ぶ敵だけ、上下ルートの間の空中や穴の上に出ない、
//      重なり無し、画面外で出現、解禁順、TierでHPが変わらない、Intensityの波、連続防止)
//  3) 分岐区間: 毎回上下どちらかの中身が決まる、上下で中身が違う、分岐の手前から両方の中身が見える位置にある
//  4) Gap Guard: 穴の手前の敵は縁から離れ、穴の向こうの敵は縁ぎりぎりで跳んだ着地点より先
//  5) 障害物が敵と重ならない(同じルート)
//  6) 指定Formationの強制(全Formation、上下ルートの組み合わせも)
//  7) 上ルートの敵を打ち上げても上ルートに着地する
//  8) ボス戦中は出さない/ボス後は休憩から再開
public class WastelandEncounterAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("WastelandEncounterTest", 0) != 1) return;
        EditorPrefs.SetInt("WastelandEncounterTest", 0);
        new GameObject("WastelandEncounterTest").AddComponent<WastelandEncounterAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[WastelandEncTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    void Bad(string what) { failures++; L("  FAIL: " + what); }

    int checkedEncounters, branchEncounters, groundOk, upperOk, airOk, placeBad, overlapBad, offscreenBad, routeSameBad, visibleBad, gapBad, gapChecked;
    string phase = "";
    int lateEasy, lateRest, lateT0;
    readonly List<string> phaseEnemyIds = new List<string>();
    // 出した敵の位置(論理X/Y/ルート)。障害物との重なり確認に使う
    readonly List<(double x, float y, EncounterRoute route)> spawnedPositions = new List<(double, float, EncounterRoute)>();
    readonly HashSet<string> formationsSeen = new HashSet<string>();
    readonly List<GameObject> upperEnemies = new List<GameObject>();

    IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(1500f);
        L("WATCHDOG: test did not finish in time");
        Finish();
    }

    void Finish()
    {
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../WastelandEncounterAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        Application.logMessageReceived += (cond, trace, type) => { if (type == LogType.Exception || type == LogType.Error) { anyException = true; log.AppendLine("[EXC] " + cond + "\n" + trace); } };
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        var tm = TerrainManager.Instance;
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.SetValue(gm, 0f);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        yield return new WaitForSeconds(0.5f);
        var dir = EncounterDirector.Instance;
        L($"stage={gm.ActiveRunStageId} director={(dir != null && dir.Profile != null ? dir.Profile.stageId : "none")} routeBranch={tm.RouteBranchEnabled} singleRoute={tm.SingleRouteMode}");
        Check(dir != null && dir.Profile != null && dir.Profile.stageId == "wasteland_road", "EncounterDirector handles wasteland_road with its own profile");
        Check(dir != null && dir.Profile != null && dir.Profile != StageEncounterProfile.Find("natural_cave"), "wasteland profile is not the natural cave profile");
        Check(tm.RouteBranchEnabled && !tm.SingleRouteMode, "upper/lower route branches are kept");
        Check(EncounterDirector.HandlesUpperRoute("wasteland_road") && !EncounterDirector.HandlesUpperRoute("natural_cave"), "upper route enemies are placed by the director (old upper spawner stops)");
        EncounterDirector.OnEncounterSpawned = Validate;

        var bands = new (string name, float target, float seconds, string[] allowed)[]
        {
            ("0-500", 40f, 50f, new[] { "goblin" }),
            ("500-1500", 700f, 50f, new[] { "goblin", "goblin_elite", "irregular_imp", "flying_wyvern" }),
            ("1500-3000", 1700f, 55f, new[] { "goblin", "goblin_elite", "irregular_imp", "flying_wyvern", "shooter_archer", "heavy_ogre" }),
            ("3000-5000", 3200f, 55f, new[] { "goblin", "goblin_elite", "irregular_imp", "flying_wyvern", "shooter_archer", "heavy_ogre", "chaser_runner" }),
            ("5000+", 5400f, 60f, new[] { "goblin", "goblin_elite", "irregular_imp", "flying_wyvern", "shooter_archer", "heavy_ogre", "chaser_runner", "rusher_runner" }),
        };
        // -wencQuick <formationId>: 距離Bandの走行を省いて、指定Formationの強制だけを確かめる(調査用)
        string quick = null;
        var args = System.Environment.GetCommandLineArgs();
        float quickBand = 5400f;
        for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "-wencQuick") quick = args[i + 1]; if (args[i] == "-wencQuickBand") float.TryParse(args[i + 1], out quickBand); }
        if (quick == null)
        {
            // 穴のGap Guardは、穴の密度が普通の距離(走り始め)で先に確かめる(何千mも走ると穴が最大密度になり、
            // 前後に安全な地面を取れる穴が少なくなる = 置かないのが正しい)
            EncounterDirector.DebugDistanceOffset = 1700f - gm.MaxDistance;
            foreach (string gid in new[] { "gap_guard", "route_line_vs_gap" }) yield return ForceOne(dir, gid);
            foreach (var b in bands) yield return RunPhase(b.name, b.target, b.seconds, 1.4f, b.allowed);
            SetRunSpeed(pc, 14f);
            yield return RunPhase("5000+ high speed", 6000f, 50f, PlayerController.DebugSpeedScale, bands[4].allowed);
            L($"   (high speed phase ran at {pc.CurrentAutoRunSpeed:F1} m/s)");
        }
        PlayerController.DebugSpeedScale = 1.4f;

        // ---- 指定Formationの強制 ----
        EncounterDirector.DebugDistanceOffset = (quick != null ? quickBand : 5400f) - gm.MaxDistance;
        var ids = new List<string>();
        foreach (var b in dir.Profile.bands) foreach (var fw in b.formations) if (!ids.Contains(fw.formationId)) ids.Add(fw.formationId);
        ids.Add("rest");
        if (quick != null) ids = new List<string> { quick };
        foreach (string id in ids)
        {
            if (quick == null && (id == "gap_guard" || id == "route_line_vs_gap")) continue; // 最初に確認済み
            yield return ForceOne(dir, id);
        }
        EncounterDirector.ForcedFormation = null;
        yield return UpperLaunch(dir);
        yield return BossConflict(gm, dir);
        yield return FinishAll();
    }

    IEnumerator ForceOne(EncounterDirector dir, string id)
    {
        {
            var f = dir.Profile.FindFormation(id);
            bool pair = f != null && f.routeMode == EncounterRouteMode.RoutePair;
            int before = dir.Recent.Count;
            EncounterDirector.DebugGapLog = id == "gap_guard";
            EncounterDirector.ForceFormation(id, 1);
            float t = 0f; bool seen = false; string got = "";
            float limit = id.Contains("gap") ? 150f : pair ? 90f : 30f; // 分岐/穴が来るまで走る
            while (t < limit && !seen)
            {
                yield return null; t += Time.deltaTime;
                for (int i = before; i < dir.Recent.Count; i++) { got = dir.Recent[i].formation; if (got == id) seen = true; }
            }
            var rec = seen ? dir.Recent.Last(r => r.formation == id) : null;
            L($"[Force] {id}: spawned={seen} after {t:F1}s{(rec != null && pair ? $" UPPER=[{rec.upper}] LOWER=[{rec.lower}]" : "")}{(rec != null && rec.reason != "" ? " reason=" + rec.reason : "")}");
            EncounterDirector.ForcedFormation = null;
            Check(seen, $"forced formation {id} appears");
            if (rec != null && pair) Check(!rec.reason.Contains("nofit"), $"{id}: both routes placed as designed");
        }
        EncounterDirector.ForcedFormation = null;
    }

    IEnumerator FinishAll()
    {
        yield return null;
        EncounterDirector.OnEncounterSpawned = null;
        EncounterDirector.DebugDistanceOffset = 0f;
        PlayerController.DebugSpeedScale = 1f;
        L("");
        L($"TOTAL encounters checked={checkedEncounters} (branch={branchEncounters}) ground ok={groundOk} upper ok={upperOk} air ok={airOk} placementBad={placeBad} overlap={overlapBad} onScreenSpawn={offscreenBad} sameOnBothRoutes={routeSameBad} notVisibleBeforeFork={visibleBad} gapGuard checked={gapChecked} bad={gapBad}");
        Check(placeBad == 0, "every enemy stands on the ground / upper route surface (never in the air between routes, in a pit or inside the ground)");
        Check(overlapBad == 0, "enemies do not overlap");
        Check(offscreenBad == 0, "enemies never pop in on screen");
        Check(routeSameBad == 0, "route choices always differ between the upper and lower route");
        Check(visibleBad == 0, "route contents can be seen before the fork");
        Check(gapChecked > 0 && gapBad == 0, "gap guard keeps a safe distance before and after the gap");
        Check(branchEncounters > 0, "branch encounters happen");
        Finish();
    }

    static void SetRunSpeed(PlayerController pc, float target)
    {
        float baseSpeed = pc.CurrentAutoRunSpeed / Mathf.Max(0.01f, PlayerController.DebugSpeedScale);
        PlayerController.DebugSpeedScale = target / Mathf.Max(0.1f, baseSpeed);
    }

    IEnumerator RunPhase(string name, float targetDistance, float seconds, float speed, string[] allowed)
    {
        var gm = GameManager.Instance;
        var dir = EncounterDirector.Instance;
        var tm = TerrainManager.Instance;
        phase = name;
        phaseEnemyIds.Clear();
        spawnedPositions.Clear();
        PlayerController.DebugSpeedScale = speed;
        EncounterDirector.DebugDistanceOffset = targetDistance - gm.MaxDistance;
        int start = dir.Recent.Count;
        var forks = new HashSet<int>();
        float t = 0f;
        while (t < seconds)
        {
            yield return null; t += Time.deltaTime;
            // 分岐を通過した回数(プレイヤーが分岐区間に入った分岐)
            float px = PlayerController.Instance.transform.position.x;
            if (tm.TryGetBranchAfter(px - 200f, out float fork, out float merge, out bool gen) && gen && px > fork && px < merge) forks.Add(Mathf.RoundToInt(FloatingOrigin.ToLogical(fork)));
        }
        var all = dir.Recent.Skip(start).ToList();
        var fcount = new Dictionary<string, int>();
        var icount = new int[4];
        var tiers = new Dictionary<EnemyAiTier, int>();
        int maxSame = 0, same = 0; string lastF = null;
        int maxHard = 0, hardRun = 0, maxRest = 0, restRun = 0;
        int branchRecs = 0, mirrored = 0, upperFights = 0, lowerFights = 0;
        foreach (var r in all)
        {
            fcount[r.formation] = fcount.TryGetValue(r.formation, out int c) ? c + 1 : 1;
            icount[(int)r.intensity]++;
            if (r.intensity != EncounterIntensity.Rest) { same = r.formation == lastF ? same + 1 : 1; lastF = r.formation; maxSame = Mathf.Max(maxSame, same); }
            hardRun = r.intensity == EncounterIntensity.Hard ? hardRun + 1 : 0; maxHard = Mathf.Max(maxHard, hardRun);
            restRun = r.intensity == EncounterIntensity.Rest ? restRun + 1 : 0; maxRest = Mathf.Max(maxRest, restRun);
            foreach (var m in r.members) tiers[m.tier] = tiers.TryGetValue(m.tier, out int k) ? k + 1 : 1;
            if ((r.terrain ?? "").StartsWith("branch")) branchRecs++;
            if (r.mirrored) mirrored++;
            if (r.members.Any(m => m.route == EncounterRoute.Upper)) upperFights++;
            if (r.members.Any(m => m.route == EncounterRoute.Lower)) lowerFights++;
        }
        string seq = string.Join(" ", all.Select(r => r.intensity.ToString()[0] + ":" + r.formation));
        L($"\n[Band {name}] dist {all.FirstOrDefault()?.distance:F0}-{all.LastOrDefault()?.distance:F0}m encounters={all.Count} R/E/M/H={icount[0]}/{icount[1]}/{icount[2]}/{icount[3]} maxSameInARow={maxSame} maxHardRun={maxHard} maxRestRun={maxRest}");
        L($"   branches passed={forks.Count} branch encounters={branchRecs} (mirrored {mirrored}) upper-route fights={upperFights} lower-route fights={lowerFights}");
        L($"   formations: {string.Join(", ", fcount.Select(kv => kv.Key + "=" + kv.Value))}");
        L($"   enemies: {string.Join(", ", phaseEnemyIds.GroupBy(x => x).Select(g => g.Key + "=" + g.Count()))}   tiers: {string.Join(", ", tiers.Select(kv => kv.Key + "=" + kv.Value))}");
        L($"   sequence: {seq}");
        foreach (var r in all.Where(r => (r.terrain ?? "").StartsWith("branch")).Take(4)) L($"   branch #{r.index} {r.intensity} {r.formation}: UPPER=[{r.upper}] LOWER=[{r.lower}]{(r.mirrored ? " mirrored" : "")}{(r.reason != "" ? " (" + r.reason.Trim() + ")" : "")}");
        Check(all.Count >= 5, $"{name}: encounters happen");
        Check(icount[0] > 0, $"{name}: Rest occurs");
        Check(maxHard <= 2, $"{name}: Hard does not repeat more than twice in a row");
        Check(maxSame <= 2, $"{name}: same formation not repeated 3+ times in a row");
        Check(branchRecs > 0, $"{name}: route branches get their own encounter");
        foreach (string id in phaseEnemyIds.Distinct()) Check(allowed.Contains(id), $"{name}: {id} is unlocked in this band");
        if (name.StartsWith("5000+"))
        {
            lateEasy += icount[1]; lateRest += icount[0]; lateT0 += tiers.TryGetValue(EnemyAiTier.T0, out int t0n) ? t0n : 0;
            if (name.Contains("high speed"))
            {
                Check(lateEasy > 0, $"5000+ (both phases): Easy still occurs ({lateEasy})");
                Check(lateRest > 0, $"5000+ (both phases): Rest still occurs ({lateRest})");
                Check(lateT0 > 0, $"5000+ (both phases): T0 still occurs ({lateT0})");
            }
        }
        yield return ObstacleConflicts(name);
    }

    // 障害物(このPhaseで出たもの)と、出した敵の位置が同じルートで重ならない
    IEnumerator ObstacleConflicts(string name)
    {
        yield return null;
        var tm = TerrainManager.Instance;
        int obstacles = 0, conflicts = 0;
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None))
        {
            Vector3 p = o.transform.position;
            double lx = FloatingOrigin.ToLogical(p.x);
            float? sky = tm.GetSkyHeightAt(p.x);
            float gl = tm.GetGroundLineAt(p.x);
            bool onUpper = sky.HasValue && Mathf.Abs(p.y - sky.Value) < Mathf.Abs(p.y - gl);
            obstacles++;
            foreach (var e in spawnedPositions)
            {
                bool eUpper = e.route == EncounterRoute.Upper;
                if (eUpper != onUpper) continue;
                if (System.Math.Abs(e.x - lx) < 1.6)
                {
                    conflicts++;
                    L($"  obstacle overlaps enemy: obstacle x={lx:F1} y={p.y:F2} {(onUpper ? "upper" : "ground")} enemy x={e.x:F1} y={e.y:F2}");
                }
            }
        }
        L($"   obstacles checked={obstacles} conflicts={conflicts}");
        Check(conflicts == 0, $"{name}: obstacles do not overlap encounter enemies");
    }

    static float VisibleAhead(PlayerController pc)
    {
        Camera cam = Camera.main;
        return cam.transform.position.x + cam.orthographicSize * cam.aspect - pc.transform.position.x;
    }

    void Validate(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned)
    {
        var tm = TerrainManager.Instance;
        var pc = PlayerController.Instance;
        checkedEncounters++;
        formationsSeen.Add(rec.formation);
        Camera cam = Camera.main;
        float right = cam.transform.position.x + cam.orthographicSize * cam.aspect;
        bool branch = f.routeMode == EncounterRouteMode.RoutePair;
        if (branch) branchEncounters++;
        var groundXs = new Dictionary<EncounterRoute, List<float>>();
        for (int i = 0; i < spawned.Count; i++)
        {
            var s = spawned[i];
            if (s.go == null) continue;
            var m = rec.members[i];
            phaseEnemyIds.Add(s.def.enemyId);
            Vector3 p = s.go.transform.position;
            var ec = s.go.GetComponent<EnemyController>();
            if (ec != null && DistanceTierManager.Instance != null && ec.maxHp != DistanceTierManager.Instance.EnemyHpFor(s.def.hpMultiplier)) Bad($"HP changed by tier: {s.def.enemyId} {m.tier} hp={ec.maxHp}");
            if (p.x < right + 0.5f) { offscreenBad++; L($"  on-screen spawn: {rec.formation} {s.def.enemyId} x={p.x - pc.transform.position.x:F1} ahead (screen right {right - pc.transform.position.x:F1})"); }
            spawnedPositions.Add((FloatingOrigin.ToLogical(p.x), p.y, m.route));
            if (m.route == EncounterRoute.Upper && s.def.movementType != EnemyMovementType.Flying) upperEnemies.Add(s.go);
            float? g = tm.GetHeightAt(p.x);
            float? sky = tm.GetSkyHeightAt(p.x);
            bool flying = s.def.movementType == EnemyMovementType.Flying;
            if (EncounterSlots.IsAir(s.slot))
            {
                float baseY = m.route == EncounterRoute.Upper && sky.HasValue ? sky.Value : tm.GetGroundLineAt(p.x);
                bool ok = flying && m.route != EncounterRoute.Lower && p.y > baseY + 1.0f;
                if (ok) airOk++; else { placeBad++; L($"  bad air: {rec.formation} {s.def.enemyId} route={m.route} y={p.y:F2} base={baseY:F2}"); }
            }
            else
            {
                bool ok = !flying;
                if (m.route == EncounterRoute.Upper)
                {
                    ok &= sky.HasValue && Mathf.Abs(p.y - (sky.Value + GroundOffset(tm))) < 0.12f && tm.GetSkyHeightAt(p.x - 0.7f).HasValue && tm.GetSkyHeightAt(p.x + 0.7f).HasValue;
                    if (ok) upperOk++;
                }
                else
                {
                    ok &= g.HasValue && Mathf.Abs(p.y - (g.Value + GroundOffset(tm))) < 0.12f && !tm.IsNearPit(p.x, 1.0f);
                    if (ok) groundOk++;
                }
                if (!ok) { placeBad++; L($"  bad ground: {rec.formation} {s.def.enemyId} route={m.route} y={p.y:F2} ground={g} sky={sky}"); }
                if (!groundXs.TryGetValue(m.route, out var list)) groundXs[m.route] = list = new List<float>();
                list.Add(p.x);
            }
            // 分岐の外(一本道)に置いた地上の敵が、上ルートの下(分岐区間)に入っていない
            if (m.route == EncounterRoute.Main && tm.IsInBranchRoute(p.x)) { placeBad++; L($"  main-road enemy inside a branch: {rec.formation} {s.def.enemyId}"); }
        }
        float minGap = branch ? 1.2f : f.minGroundGap;
        foreach (var kv in groundXs)
        {
            kv.Value.Sort();
            for (int i = 1; i < kv.Value.Count; i++)
                if (kv.Value[i] - kv.Value[i - 1] < minGap - 0.05f) { overlapBad++; L($"  overlap: {rec.formation} {kv.Key} gap={kv.Value[i] - kv.Value[i - 1]:F2} < {minGap}"); }
        }
        if (f.formationId == "staggered" && groundXs.TryGetValue(EncounterRoute.Main, out var st) && st.Count >= 2 && st.Zip(st.Skip(1), (a, b) => b - a).Min() < 4f) { overlapBad++; L("  staggered too tight"); }

        if (branch)
        {
            // 上下で中身が違う(同じFormation+同じ強さを両方に置かない)
            if (rec.upper == rec.lower) { routeSameBad++; L($"  same on both routes: {rec.formation} {rec.upper}"); }
            // 分岐の手前(forkの3m手前)に居る時点で、両ルートの先頭の敵が画面に入っている
            var mt = System.Text.RegularExpressions.Regex.Match(rec.terrain ?? "", @"fork=(-?\d+)");
            if (mt.Success)
            {
                float fork = (float)(double.Parse(mt.Groups[1].Value) - FloatingOrigin.Offset);
                float view = VisibleAhead(pc);
                foreach (EncounterRoute r in new[] { EncounterRoute.Upper, EncounterRoute.Lower })
                {
                    var xs = rec.members.Where(mm => mm.route == r).Select(mm => (float)(mm.x - FloatingOrigin.Offset)).ToList();
                    if (xs.Count == 0) continue;
                    float first = xs.Min() - fork;
                    if (first > view - 3f) { visibleBad++; L($"  {r} route content starts {first:F1}m after the fork (view {view:F1}m): not visible before choosing"); }
                }
            }
        }
        if (f.requiresPit) CheckGap(rec, spawned);
        // 分岐区間の片側にGap Guardを置いた時(下ルート)は、そのルートの敵だけで確かめる
        if (branch && rec.lower.StartsWith("gap_guard"))
            CheckGap(rec, spawned.Where((sp, i) => rec.members[i].route == EncounterRoute.Lower).ToList());
    }

    static float GroundOffset(TerrainManager tm) => tm.groundEnemyHeight;

    void CheckGap(EncounterDirector.Record rec, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned)
    {
        var tm = TerrainManager.Instance;
        var pc = PlayerController.Instance;
        gapChecked++;
        var xs = spawned.Where(s => s.go != null).Select(s => s.go.transform.position.x).OrderBy(x => x).ToList();
        if (xs.Count < 2) { gapBad++; L($"  gap guard with {xs.Count} enemies"); return; }
        float pitStart = float.NaN, pitEnd = float.NaN;
        for (float x = xs[0]; x <= xs[xs.Count - 1]; x += 0.1f)
        {
            bool hole = !tm.GetHeightAt(x).HasValue;
            if (hole && float.IsNaN(pitStart)) pitStart = x;
            if (!hole && !float.IsNaN(pitStart) && float.IsNaN(pitEnd)) pitEnd = x;
        }
        if (float.IsNaN(pitStart) || float.IsNaN(pitEnd)) { gapBad++; L("  gap guard: no gap between its enemies"); return; }
        float reach = pc.CurrentAutoRunSpeed * 2f * pc.jumpForce / pc.gravity;
        foreach (float x in xs)
        {
            if (x < pitStart && pitStart - x < 5.4f) { gapBad++; L($"  gap guard: enemy {pitStart - x:F1}m before the gap (need >= 6)"); }
            if (x > pitEnd && x - pitStart < reach + 1.5f) { gapBad++; L($"  gap guard: enemy {x - pitStart:F1}m after the takeoff edge, jump reach {reach:F1}m"); }
        }
        L($"   gap guard: gap {pitEnd - pitStart:F1}m, enemies at {string.Join(", ", xs.Select(x => (x < pitStart ? -(pitStart - x) : x - pitEnd).ToString("F1")))} (neg = before the gap), jump reach {reach:F1}m");
    }

    // 上ルートの地上の敵を打ち上げる → 上ルートの面に着地する(下ルートへ落ちない)
    IEnumerator UpperLaunch(EncounterDirector dir)
    {
        var tm = TerrainManager.Instance;
        upperEnemies.RemoveAll(g => g == null);
        if (upperEnemies.Count == 0)
        {
            EncounterDirector.ForceFormation("route_upper_fight", 1);
            float w = 0f;
            while (upperEnemies.Count == 0 && w < 70f) { yield return null; w += Time.deltaTime; upperEnemies.RemoveAll(g => g == null); }
        }
        var go = upperEnemies.FirstOrDefault(g => g != null);
        if (go == null) { Bad("no upper-route enemy to launch"); yield break; }
        var ec = go.GetComponent<EnemyController>();
        float x0 = go.transform.position.x;
        typeof(EnemyController).GetMethod("LaunchUpward", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ec, null);
        float t = 0f, maxY = go.transform.position.y;
        while (t < 2.5f && go != null) { yield return null; t += Time.deltaTime; maxY = Mathf.Max(maxY, go.transform.position.y); }
        if (go == null) { Bad("launched upper-route enemy disappeared"); yield break; }
        Vector3 p = go.transform.position;
        float? sky = tm.GetSkyHeightAt(p.x);
        float? ground = tm.GetHeightAt(p.x);
        L($"[Upper launch] {go.name} onUpperRoute={ec.onUpperRoute} rose to {maxY:F2}, now y={p.y:F2} sky={sky} ground={ground} dx={p.x - x0:F1}");
        Check(ec.onUpperRoute && sky.HasValue && Mathf.Abs(p.y - sky.Value) < 0.3f, "a launched upper-route enemy lands back on the upper route");
    }

    IEnumerator BossConflict(GameManager gm, EncounterDirector dir)
    {
        var bm = BossManager.Instance;
        if (bm == null) yield break;
        bm.NetTestSpawnWild(WildBossKind.Wolf, 1);
        int before = dir.Recent.Count;
        float t = 0f;
        while (t < 5f) { yield return null; t += Time.deltaTime; }
        int during = dir.Recent.Count - before;
        L($"[Boss] IsBossPhase={bm.IsBossPhase} encounters during boss phase (5s)={during} (expect 0)");
        Check(bm.IsBossPhase && during == 0, "no encounters during a boss phase");
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) Destroy(w.gameObject);
        bm.EndBossPhase();
        gm.EndBossDistanceExclusion();
        before = dir.Recent.Count;
        t = 0f;
        float firstAt = -1f; double firstLogical = 0;
        double resumeLogical = FloatingOrigin.ToLogical(PlayerController.Instance.transform.position.x);
        while (t < 15f && firstAt < 0f) { yield return null; t += Time.deltaTime; if (dir.Recent.Count > before) { firstAt = t; firstLogical = dir.Recent[before].anchorLogical; } }
        L($"[Boss] after the boss: first encounter after {firstAt:F1}s, {firstLogical - resumeLogical:F0}m ahead of where the boss ended (post-rest {dir.Profile.bossPostRest}m + spawn-ahead)");
        Check(firstAt > 0f && firstLogical - resumeLogical >= dir.Profile.bossPostRest, "encounters resume after the boss with a rest");
    }
}

public static class WastelandEncounterTestMenu
{
    [MenuItem("Tools/OneMoreMile/Wasteland Encounter Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("WastelandEncounterTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

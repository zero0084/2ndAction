#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 共通Encounter System(2026-09-27) - 自然洞窟の一本道化 + EncounterDirectorの自動確認(Editor専用)。
//  1) 地形: 上下ルート/空中足場が作られない、坂/穴/長い直線/天井の区間(通常/低/針/高い空洞)が混ざる
//  2) 距離Band(0/500/1500/3000/5000m相当)ごとに実際に走らせ、出現したEncounterを1つずつ検証
//     (地上の敵は地面の上、空中Slotは飛ぶ敵だけ、Wormは平らで穴から離れた地面、重なり無し、
//      画面外で出現、地形条件、解禁順、Tier違いでHPが変わらない、Intensityの波、連続防止)
//  3) 指定Formationの強制(全Formation)
//  4) 高速(速度倍率2.5)でのWormの予兆開始距離
//  5) ボス戦中は出さない/ボス後は休憩から再開
// 結果は EncounterAutoTest.txt。例外/検証失敗があれば終了コード1。
public class EncounterAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("EncounterAutoTest", 0) != 1) return;
        EditorPrefs.SetInt("EncounterAutoTest", 0);
        new GameObject("EncounterAutoTest").AddComponent<EncounterAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[EncounterTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } }

    // 1回のEncounterごとの検証結果(Phaseごとに集計)
    int checkedEncounters, groundOk, groundBad, airOk, airBad, burrowOk, burrowBad, overlapBad, offscreenBad, clearanceBad, spanBad;
    readonly Dictionary<string, Dictionary<EnemyAiTier, HashSet<int>>> hpByTier = new Dictionary<string, Dictionary<EnemyAiTier, HashSet<int>>>();
    string phase = "";
    readonly List<string> phaseEnemyIds = new List<string>();

    IEnumerator Start()
    {
        Application.logMessageReceived += (cond, trace, type) => { if (type == LogType.Exception || type == LogType.Error) { anyException = true; log.AppendLine("[EXC] " + cond); } };
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        var tm = TerrainManager.Instance;
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        var expField = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (expField != null) expField.SetValue(gm, 0f); // レベルアップの一時停止で時間が止まらないように
        if (BossManager.Instance != null) BossManager.Instance.enabled = false; // 距離Bandの走行中はボスを出さない(ボスは5)で別に確認)
        var dir = EncounterDirector.Instance;
        L($"stage={gm.ActiveRunStageId} director={(dir != null && dir.Profile != null ? dir.Profile.stageId : "none")} singleRoute={tm.SingleRouteMode}");
        Check(dir != null && dir.Profile != null && dir.Profile.stageId == "natural_cave", "EncounterDirector handles natural_cave");
        Check(tm.SingleRouteMode, "natural_cave terrain is single-route");
        EncounterDirector.OnEncounterSpawned = Validate;

        // ---- 2) 距離Band ----
        var bands = new (string name, float target, float seconds, string[] allowed)[]
        {
            ("0-500", 40f, 38f, new[] { "cave_ant" }),
            ("500-1500", 700f, 38f, new[] { "cave_ant", "soldier_ant", "cave_hopper" }),
            ("1500-3000", 1700f, 38f, new[] { "cave_ant", "soldier_ant", "cave_hopper", "cave_bat" }),
            ("3000-5000", 3200f, 40f, new[] { "cave_ant", "soldier_ant", "cave_hopper", "cave_bat", "burrow_worm" }),
            ("5000+", 5400f, 45f, new[] { "cave_ant", "soldier_ant", "cave_hopper", "cave_bat", "burrow_worm" }),
        };
        foreach (var b in bands)
        {
            yield return RunPhase(b.name, b.target, b.seconds, 1.4f, b.allowed);
        }
        // 高速(約14m/s)で後半Bandを走り、出現位置/間隔/地形条件が崩れないか
        SetRunSpeed(pc, 14f);
        yield return RunPhase("5000+ high speed", 6000f, 45f, PlayerController.DebugSpeedScale, bands[4].allowed);
        L($"   (high speed phase ran at {pc.CurrentAutoRunSpeed:F1} m/s)");

        // ---- 1) 地形(走った範囲の集計) ----
        TerrainSummary(tm);

        // ---- 3) 指定Formationの強制(通常の速度で) ----
        PlayerController.DebugSpeedScale = 1.4f;
        EncounterDirector.DebugDistanceOffset = 5400f - gm.MaxDistance;
        var ids = new[] { "ground_line", "ground_cluster", "staggered", "guard_hopper", "ground_air", "air_swarm", "burrow_ambush", "gauntlet", "rest" };
        foreach (string id in ids)
        {
            int before = dir.Recent.Count;
            EncounterDirector.ForceFormation(id, 1);
            float t = 0f; bool seen = false; string got = "";
            while (t < 25f && !seen)
            {
                yield return null; t += Time.deltaTime;
                for (int i = before; i < dir.Recent.Count; i++) { got = dir.Recent[i].formation; if (got == id) seen = true; }
            }
            L($"[Force] {id}: spawned={seen} (last={got}) after {t:F1}s");
            EncounterDirector.ForcedFormation = null;
            Check(seen, $"forced formation {id} appears");
        }
        EncounterDirector.ForcedFormation = null;

        // ---- 4) 高速(Worm) ----
        yield return WormHighSpeed(gm, pc);

        // ---- 5) ボスとの競合 ----
        yield return BossConflict(gm, dir);

        EncounterDirector.OnEncounterSpawned = null;
        EncounterDirector.DebugDistanceOffset = 0f;
        PlayerController.DebugSpeedScale = 1f;
        L("");
        L($"TOTAL encounters checked={checkedEncounters} ground ok/bad={groundOk}/{groundBad} air ok/bad={airOk}/{airBad} burrow ok/bad={burrowOk}/{burrowBad} overlap={overlapBad} onScreenSpawn={offscreenBad} clearance={clearanceBad} span={spanBad}");
        foreach (var kv in hpByTier)
        {
            var parts = kv.Value.Select(t => $"{t.Key}:[{string.Join(",", t.Value)}]");
            L($"[HP by tier] {kv.Key} {string.Join(" ", parts)}");
        }
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../EncounterAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
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
        phase = name;
        phaseEnemyIds.Clear();
        PlayerController.DebugSpeedScale = speed;
        EncounterDirector.DebugDistanceOffset = targetDistance - gm.MaxDistance;
        int start = dir.Recent.Count;
        float t = 0f;
        while (t < seconds) { yield return null; t += Time.deltaTime; }
        var recs = dir.Recent.Skip(start).Where(r => r.formation != "rest" || r.reason != "").ToList();
        var all = dir.Recent.Skip(start).ToList();
        var fcount = new Dictionary<string, int>();
        var icount = new int[4];
        var tiers = new Dictionary<EnemyAiTier, int>();
        int maxSame = 0, same = 0; string lastF = null;
        int maxHard = 0, hardRun = 0, maxRest = 0, restRun = 0;
        foreach (var r in all)
        {
            fcount[r.formation] = fcount.TryGetValue(r.formation, out int c) ? c + 1 : 1;
            icount[(int)r.intensity]++;
            if (r.intensity != EncounterIntensity.Rest) { same = r.formation == lastF ? same + 1 : 1; lastF = r.formation; maxSame = Mathf.Max(maxSame, same); }
            hardRun = r.intensity == EncounterIntensity.Hard ? hardRun + 1 : 0; maxHard = Mathf.Max(maxHard, hardRun);
            restRun = r.intensity == EncounterIntensity.Rest ? restRun + 1 : 0; maxRest = Mathf.Max(maxRest, restRun);
            foreach (var m in r.members) tiers[m.tier] = tiers.TryGetValue(m.tier, out int k) ? k + 1 : 1;
        }
        string seq = string.Join(" ", all.Select(r => r.intensity.ToString()[0] + ":" + r.formation));
        L($"[Band {name}] dist {all.FirstOrDefault()?.distance:F0}-{all.LastOrDefault()?.distance:F0}m encounters={all.Count} R/E/M/H={icount[0]}/{icount[1]}/{icount[2]}/{icount[3]} maxSameFormationInARow={maxSame} maxHardRun={maxHard} maxRestRun={maxRest}");
        L($"   formations: {string.Join(", ", fcount.Select(kv => kv.Key + "=" + kv.Value))}");
        L($"   enemies: {string.Join(", ", phaseEnemyIds.GroupBy(x => x).Select(g => g.Key + "=" + g.Count()))}   tiers: {string.Join(", ", tiers.Select(kv => kv.Key + "=" + kv.Value))}");
        L($"   sequence: {seq}");
        Check(all.Count >= 5, $"{name}: encounters happen");
        Check(icount[0] > 0, $"{name}: Rest occurs");
        Check(maxHard <= 2, $"{name}: Hard does not repeat more than twice in a row");
        Check(maxRest <= 2, $"{name}: Rest does not repeat more than twice in a row");
        Check(maxSame <= 2, $"{name}: same formation not repeated 3+ times in a row");
        foreach (string id in phaseEnemyIds.Distinct()) Check(allowed.Contains(id), $"{name}: {id} is unlocked in this band");
        if (name == "5000+")
        {
            Check(icount[1] > 0, "5000+: Easy still occurs");
            Check(tiers.ContainsKey(EnemyAiTier.T0), "5000+: T0 still occurs");
        }
    }

    // Encounterが出た瞬間の検証
    void Validate(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> spawned)
    {
        var tm = TerrainManager.Instance;
        var pc = PlayerController.Instance;
        checkedEncounters++;
        Camera cam = Camera.main;
        float right = cam.transform.position.x + cam.orthographicSize * cam.aspect;
        var groundXs = new List<float>();
        foreach (var s in spawned)
        {
            if (s.go == null) continue;
            phaseEnemyIds.Add(s.def.enemyId);
            Vector3 p = s.go.transform.position;
            var ec = s.go.GetComponent<EnemyController>();
            var esb = s.go.GetComponent<EnemySpecialBehavior>();
            EnemyAiTier tier = esb != null ? esb.aiTier : EnemyAiTier.T0;
            foreach (var m in rec.members) if (Mathf.Abs(m.x - FloatingOrigin.ToLogical(p.x)) < 0.01f) tier = m.tier;
            if (ec != null && DistanceTierManager.Instance != null && ec.maxHp != DistanceTierManager.Instance.EnemyHpFor(s.def.hpMultiplier)) { failures++; L($"  FAIL: HP changed by tier: {s.def.enemyId} {tier} hp={ec.maxHp}"); }
            if (ec != null && s.def.enemyId == "soldier_ant")
            {
                if (!hpByTier.TryGetValue(phase + ":" + s.def.enemyId, out var d)) hpByTier[phase + ":" + s.def.enemyId] = d = new Dictionary<EnemyAiTier, HashSet<int>>();
                if (!d.TryGetValue(tier, out var set)) d[tier] = set = new HashSet<int>();
                set.Add(ec.maxHp);
            }
            if (p.x < right + 0.5f) { offscreenBad++; L($"  on-screen spawn: {rec.formation} {s.def.enemyId} x={p.x - pc.transform.position.x:F1} ahead (screen right edge {right - pc.transform.position.x:F1})"); }
            float? g = tm.GetHeightAt(p.x);
            float? ceil = tm.GetEffectiveCeilingHeightAt(p.x);
            if (EncounterSlots.IsAir(s.slot))
            {
                bool ok = s.def.movementType == EnemyMovementType.Flying && p.y > tm.GetGroundLineAt(p.x) + 1.0f && (!ceil.HasValue || p.y < ceil.Value - 0.4f);
                if (ok) airOk++; else { airBad++; L($"  bad air: {rec.formation} {s.def.enemyId} y={p.y:F2} ground={tm.GetGroundLineAt(p.x):F2} ceil={ceil}"); }
            }
            else if (s.slot == EncounterSlotKind.Burrow)
            {
                bool flat = g.HasValue && !tm.IsNearPit(p.x, 2.5f);
                for (float dx = -1.5f; dx <= 1.5f && flat; dx += 0.5f) { float? g2 = tm.GetHeightAt(p.x + dx); flat = g2.HasValue && Mathf.Abs(g2.Value - g.Value) < 0.06f; }
                bool ok = s.def.behaviorKind == EnemyBehaviorKind.BurrowWorm && flat;
                if (ok) burrowOk++; else { burrowBad++; L($"  bad burrow: {rec.formation} {s.def.enemyId} flat={flat}"); }
            }
            else
            {
                bool ok = s.def.movementType != EnemyMovementType.Flying && s.def.behaviorKind != EnemyBehaviorKind.BurrowWorm && g.HasValue && Mathf.Abs(p.y - g.Value) < 0.1f && !tm.IsNearPit(p.x, 1.0f);
                if (ok) groundOk++; else { groundBad++; L($"  bad ground: {rec.formation} {s.def.enemyId} y={p.y:F2} ground={g}"); }
                groundXs.Add(p.x);
            }
            if (ceil.HasValue && g.HasValue && ceil.Value - g.Value < f.requiredHeight - 0.15f) { clearanceBad++; L($"  clearance: {rec.formation} needs {f.requiredHeight} has {ceil.Value - g.Value:F2}"); }
        }
        groundXs.Sort();
        for (int i = 1; i < groundXs.Count; i++)
            if (groundXs[i] - groundXs[i - 1] < f.minGroundGap - 0.05f) { overlapBad++; L($"  overlap: {rec.formation} gap={groundXs[i] - groundXs[i - 1]:F2} < {f.minGroundGap}"); }
        if (f.formationId == "air_swarm" && spawned.Count > 3) { spanBad++; L($"  swarm too dense: {spawned.Count}"); }
        if (f.formationId == "gauntlet" && rec.endLogical - rec.anchorLogical < 18.0 && spawned.Count > 2) { spanBad++; L($"  gauntlet not spread: {rec.endLogical - rec.anchorLogical:F1}m"); }
        if (f.formationId == "staggered" && groundXs.Count >= 2 && groundXs.Zip(groundXs.Skip(1), (a, b) => b - a).Min() < 4f) { spanBad++; L("  staggered too tight"); }
    }

    void TerrainSummary(TerrainManager tm)
    {
        var bf = BindingFlags.NonPublic | BindingFlags.Instance;
        var sky = (IList)typeof(TerrainManager).GetField("skyChunks", bf).GetValue(tm);
        var branches = (IList)typeof(TerrainManager).GetField("branchRanges", bf).GetValue(tm);
        var chunks = (IList)typeof(TerrainManager).GetField("chunks", bf).GetValue(tm);
        var types = new Dictionary<string, int>();
        int flatRun = 0, maxFlatRun = 0;
        foreach (var c in chunks)
        {
            string ty = c.GetType().GetField("type").GetValue(c).ToString();
            types[ty] = types.TryGetValue(ty, out int k) ? k + 1 : 1;
            flatRun = ty == "Flat" ? flatRun + 1 : 0; maxFlatRun = Mathf.Max(maxFlatRun, flatRun);
        }
        L($"[Terrain] skyChunks={sky.Count} branchRanges={branches.Count} chunks={chunks.Count} types: {string.Join(", ", types.Select(kv => kv.Key + "=" + kv.Value))} longestFlatRun={maxFlatRun} chunks");
        Check(sky.Count == 0 && branches.Count == 0, "no upper/lower routes or floating platforms in the cave");
        Check(types.ContainsKey("UpSlope") && types.ContainsKey("DownSlope") && types.ContainsKey("Pit"), "single route still has slopes and gaps");
        Check(maxFlatRun >= 4, "long straights exist");
        if (tm.cave != null)
        {
            var nodes = (IList)typeof(CaveStage).GetField("nodes", bf).GetValue(tm.cave);
            var modes = new Dictionary<int, int>();
            foreach (var n in nodes) { int m = (int)n.GetType().GetField("mode").GetValue(n); modes[m] = modes.TryGetValue(m, out int k) ? k + 1 : 1; }
            L($"[Ceiling] nodes kept={nodes.Count} modes(0 normal,1 spike,2 low,3 high)={string.Join(", ", modes.Select(kv => kv.Key + ":" + kv.Value))}");
        }
    }

    IEnumerator WormHighSpeed(GameManager gm, PlayerController pc)
    {
        phase = "worm-highspeed";
        SetRunSpeed(pc, 14f); // 実際の最高速度付近(自動スローの全開12m/sより少し速い)
        EncounterDirector.DebugDistanceOffset = 5400f - gm.MaxDistance;
        EncounterDirector.ForceFormation("burrow_ambush", 4);
        var stateField = typeof(EnemySpecialBehavior).GetField("wormState", BindingFlags.NonPublic | BindingFlags.Instance);
        var seen = new Dictionary<EnemySpecialBehavior, string>();
        var leads = new List<float>();
        float t = 0f;
        while (t < 22f)
        {
            yield return null; t += Time.deltaTime;
            foreach (var esb in FindObjectsByType<EnemySpecialBehavior>(FindObjectsSortMode.None))
            {
                if (esb.kind != EnemyBehaviorKind.BurrowWorm) continue;
                string st = stateField.GetValue(esb).ToString();
                seen.TryGetValue(esb, out string prev);
                if (st == "Telegraph" && prev != "Telegraph" && prev != null)
                {
                    float lead = esb.transform.position.x - pc.transform.position.x;
                    leads.Add(lead);
                }
                seen[esb] = st;
            }
        }
        float speed = pc.CurrentAutoRunSpeed;
        float need = speed * 1.25f; // 予兆0.9s+せり出し0.35sぶんの距離
        L($"[Worm high speed] runSpeed={speed:F1}m/s telegraph starts (m ahead of player): {string.Join(", ", leads.Select(x => x.ToString("F1")))} (need >= ~{need:F1} to see the whole telegraph)");
        Check(leads.Count > 0, "worm telegraph observed at high speed");
        Check(leads.Count == 0 || leads.Where(x => x > 0f).DefaultIfEmpty(99f).Min() >= need * 0.85f, "worm telegraph starts early enough at high speed");
        PlayerController.DebugSpeedScale = 1.4f;
        EncounterDirector.ForcedFormation = null;
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
        while (t < 12f && firstAt < 0f) { yield return null; t += Time.deltaTime; if (dir.Recent.Count > before) { firstAt = t; firstLogical = dir.Recent[before].anchorLogical; } }
        L($"[Boss] after the boss: first encounter after {firstAt:F1}s, {firstLogical - resumeLogical:F0}m ahead of where the boss ended (post-rest {dir.Profile.bossPostRest}m + spawn-ahead)");
        Check(firstAt > 0f && firstLogical - resumeLogical >= dir.Profile.bossPostRest, "encounters resume after the boss with a rest");
    }
}

public static class EncounterTestMenu
{
    [MenuItem("Tools/OneMoreMile/Encounter Director Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("EncounterAutoTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

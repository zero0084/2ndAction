#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// LAST CORRIDOR(2026-09-29)の自動確認(開発ビルド/Editor専用)。
//  起動: 開発ビルド = -lcTour <出力フォルダ>(スクリーンショットとFPSも取る)、Editor = メニュー「Last Corridor Tour (batch)」
//  1) ステージ選択: 一覧に出ている/解放済み、Run開始でテーマ・天井(暗闇なし)・演出担当が有効になる
//  2) ボスの表(1,000m〜100,000m)を走査: 3ステージのボスが交互に出る、100,000mは通常ボス無し(死神)
//  3) 0m〜100,000mの各段階へワープして、高速補助ON・実際の走行で数十秒ずつ走る。
//     段階/背景/天井の区間(抜けている割合・杭)/穴の幅/空中足場/落ちてくる構造物/背景の構造物/出てきた敵とFormation/
//     道中のボス(倒して報酬まで)/被弾数/フレーム時間 を記録
//  4) 100,000mで死神(長女)が出て、捕まるとRunが終わる(他のマップと同じ死亡処理)
// 結果: <出力>/LastCorridorTour.txt(Editorはプロジェクト直下)
public class LastCorridorTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string dir = null, stage = LastCorridorDirector.StageId;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "-lcTour") dir = args[i + 1]; if (args[i] == "-lcTourStage") stage = args[i + 1]; }
#if UNITY_EDITOR
        if (dir == null && UnityEditor.EditorPrefs.GetInt("LastCorridorTour", 0) == 1)
        {
            UnityEditor.EditorPrefs.SetInt("LastCorridorTour", 0);
            string st = UnityEditor.EditorPrefs.GetString("LastCorridorTourStage", "");
            if (!string.IsNullOrEmpty(st)) stage = st;
            UnityEditor.EditorPrefs.SetString("LastCorridorTourStage", "");
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        }
#endif
        if (dir == null) return;
        Application.runInBackground = true;
        var go = new GameObject("LastCorridorTour");
        DontDestroyOnLoad(go);
        var t = go.AddComponent<LastCorridorTour>();
        t.outDir = dir; t.stageId = stage;
        t.capture = !Application.isEditor;
        t.video = System.Array.IndexOf(args, "-lcTourVideo") >= 0 && t.capture;
    }

    string outDir, stageId;
    bool capture, video;
    int videoFrame;
    bool IsLc => stageId == LastCorridorDirector.StageId;
    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[LCTour] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("[FAIL] " + what); } }

    // 記録
    readonly List<string> encounters = new List<string>();
    readonly HashSet<string> enemyIds = new HashSet<string>();
    readonly List<string> bosses = new List<string>();
    readonly List<float> frameMs = new List<float>();
    int hits, prevLives;
    int shot;

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        Application.logMessageReceived += (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; log.AppendLine("[EXC] " + cond + "\n" + trace); }
        };
        yield return new WaitForSecondsRealtime(2f);
        var gm = GameManager.Instance;

        // ---- 1) ステージ選択 ----
        StageDatabase.Reset();
        var def = StageDatabase.FindById(LastCorridorDirector.StageId);
        L($"stages: {string.Join(", ", StageDatabase.AllStages.Select(s => $"{s.stageId}({s.displayName}, unlocked={s.unlocked})"))}");
        Check(def != null && def.unlocked, "last_corridor is listed and unlocked");
        Check(def != null && def.thumbnail != null, "last_corridor has a thumbnail");

        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stageId);
        L($"tour stage = {stageId}{(IsLc ? "" : " (比較用: 同じ条件で既存マップを走る)")}");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        var tm = TerrainManager.Instance;
        var dir = LastCorridorDirector.Instance;
        var pc = PlayerController.Instance;
        L($"run started: stage={gm.ActiveRunStageId} cave={tm.HasCave} darkness={tm.HasCaveDarkness} director={LastCorridorDirector.IsActive} bg={tm.backgroundRenderer.sprite?.name}");
        if (!IsLc) { yield return Baseline(gm, tm, pc); yield break; }
        Check(gm.ActiveRunStageId == LastCorridorDirector.StageId, "run stage is last_corridor");
        Check(tm.HasCave && !tm.HasCaveDarkness, "ceiling active without darkness");
        Check(LastCorridorDirector.IsActive, "director active");
        Check(tm.backgroundRenderer.sprite != null && tm.backgroundRenderer.sprite.name.Contains("bg_early"), "early background");

        StartCoroutine(KeepAlive(gm));
        StartCoroutine(AutoPickCards(gm));
        EncounterDirector.OnEncounterSpawned += OnEncounter;
        HighSpeedAssist.Instance.SetEnabled(true);
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        prevLives = gm.Lives;
        yield return Shot("00_start");

        // ---- 2) ボスの表 ----
        var bm = BossManager.Instance;
        var resolve = typeof(BossManager).GetMethod("ResolveLastGate", BindingFlags.NonPublic | BindingFlags.Instance);
        var fams = new Dictionary<string, int>();
        var tenKm = new List<string>();
        for (int k = 1; k <= 100; k++)
        {
            object[] a = { k, null, null, null };
            bool ok = (bool)resolve.Invoke(bm, a);
            string fam = a[1].ToString();
            int kindVal = (int)a[2], count = (int)a[3];
            string kind = fam == "Sky" ? ((SkyBossKind)kindVal).ToString() : fam == "Cave" ? ((CaveBossKind)kindVal).ToString() : ((WildBossKind)kindVal).ToString();
            if (!ok) { L($"k={k}: no gate (Death)"); Check(k == 100, "only 100,000m has no gate"); continue; }
            fams[fam] = fams.TryGetValue(fam, out int n) ? n + 1 : 1;
            if (k % 10 == 0) tenKm.Add($"{k}km:{fam}/{kind}");
            if (k <= 15 || k % 10 == 0) L($"k={k} ({k * 1000}m): {fam}/{kind} x{count}");
        }
        L($"gate families: {string.Join(", ", fams.Select(p => p.Key + "=" + p.Value))}");
        L($"10,000m bosses: {string.Join(", ", tenKm)}");
        Check(fams.Count == 3 && fams.Values.All(v => v >= 25), "all 3 stages' bosses rotate");

        // 天井の区間の抽選(段階ごとの割合)を直接確かめる(実際の区間は32〜72mと長く、短い区間の集計はばらつくため)
        var pick = typeof(LastCorridorDirector).GetMethod("PickSection", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (float lx in new[] { 5000f, 30000f, 50000f, 70000f, 90000f })
        {
            var cnt = new int[5];
            for (int i = 0; i < 4000; i++) cnt[(int)pick.Invoke(dir, new object[] { lx, (i + 0.5f) / 4000f })]++;
            L($"section odds @{lx:F0}m: open={cnt[4] / 40f:F0}% normal={cnt[0] / 40f:F0}% stake={cnt[1] / 40f:F0}% low={cnt[2] / 40f:F0}% high={cnt[3] / 40f:F0}%");
            if (lx < 25000f) Check(cnt[4] < 400, "P0 ceiling mostly intact (odds)");
            if (lx >= 55000f) Check(cnt[4] >= 3600, "P2+ ceiling mostly open (odds)");
        }

        // ---- 3) 各段階 ----
        float[] starts = { 300f, 12000f, 26000f, 42000f, 56500f, 72000f, 86000f, 96000f };
        foreach (float st in starts)
            yield return Segment(gm, bm, tm, dir, pc, st, 32f);
        // 道中のボス: 関門の手前から走って実際の距離の関門で出す(3ステージのボスが混ざる/連続する所)
        foreach (float gate in new[] { 2000f, 3000f, 10000f, 15000f, 20000f, 30000f, 60000f, 90000f })
            yield return Segment(gm, bm, tm, dir, pc, gate - 80f, 22f);

        L("");
        L($"enemy ids seen ({enemyIds.Count}): {string.Join(", ", enemyIds.OrderBy(s => s))}");
        L($"bosses fought: {string.Join(" | ", bosses)}");
        L($"falling structures attached: fall={LastCorridorDirector.FallingAttached} gate={LastCorridorDirector.GateAttached}  started={FallingDebris.Started} landed={FallingDebris.Landed} placedWithoutFall(too close)={FallingDebris.Skipped} minLeadAtLanding={FallingDebris.MinLeadSeconds:F2}s");
        Check(FallingDebris.MinLeadSeconds >= 0.8f, "falling structures land well before the player arrives");
        Check(bosses.Count >= 6, "bosses fought on the way");
        Check(enemyIds.Any(i => i.StartsWith("cave_") || i.Contains("ant") || i == "burrow_worm"), "cave enemies appear");
        Check(enemyIds.Any(i => i.StartsWith("sky_") || i == "harpy" || i == "gargoyle" || i == "celestial_knight"), "sky enemies appear");
        Check(enemyIds.Any(i => i.StartsWith("goblin") || i == "heavy_ogre" || i == "shooter_archer"), "wasteland enemies appear");
        Check(LastCorridorDirector.FallingAttached + LastCorridorDirector.GateAttached > 0 && FallingDebris.Landed > 0, "falling structures appear and land");
        if (frameMs.Count > 0)
        {
            var s = frameMs.OrderBy(x => x).ToList();
            L($"frame ms: avg={s.Average():F2} p50={s[s.Count / 2]:F2} p95={s[(int)(s.Count * 0.95f)]:F2} p99={s[(int)(s.Count * 0.99f)]:F2} max={s[s.Count - 1]:F2} (n={s.Count})  -> avg fps {1000f / s.Average():F1}");
        }

        // ---- 4) 100,000m: 死神 → 捕まるとRun終了 ----
        stopKeepAlive = true;
        ReaperBase.DebugNoReap = true;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        gm.DebugWarpToDistance(99700f);
        float t0 = Time.time;
        while (!bm.DeathSpawned && Time.time - t0 < 60f) { yield return BossGuard(gm, bm); yield return null; }
        ReaperBase r = null;
        t0 = Time.time;
        while (r == null && Time.time - t0 < 10f) { r = ReaperBase.Active; yield return null; }
        L($"[Death] spawned={bm.DeathSpawned} reaper={(r != null ? r.GetType().Name : "-")} at {gm.MaxDistance:F0}m");
        Check(r != null && r is ReaperEldest, "reaper (eldest) appears at 100,000m");
        yield return new WaitForSeconds(4f);
        yield return Shot("90_reaper");
        PlayerController.DebugSpeedScale = 0.5f;
        ReaperBase.DebugNoReap = false;
        t0 = Time.time;
        while (!gm.IsGameOver && Time.time - t0 < 40f) yield return null;
        PlayerController.DebugSpeedScale = 1f;
        L($"[Death] gameOver={gm.IsGameOver} (run ends like other maps)");
        Check(gm.IsGameOver, "captured -> run ends");
        yield return new WaitForSecondsRealtime(1.5f);
        yield return Shot("99_gameover");

        EncounterDirector.OnEncounterSpawned -= OnEncounter;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "LastCorridorTour.txt"), log.ToString());
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator Segment(GameManager gm, BossManager bm, TerrainManager tm, LastCorridorDirector dir, PlayerController pc, float start, float seconds)
    {
        int fall0 = LastCorridorDirector.FallingAttached + LastCorridorDirector.GateAttached, land0 = FallingDebris.Landed;
        int enc0 = encounters.Count, hits0 = hits, contact0 = ObstacleController.TotalContactDamage;
        int boss0 = bosses.Count;
        // ボス戦で溜まった「戦闘中の移動除外」を0に戻す(ワープ後の距離=ワープ先にする。SkyBossAutoTestと同じ)
        typeof(GameManager).GetField("distanceExclusionOffset", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        typeof(GameManager).GetField("distanceExclusionOffsetExact", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0.0);
        gm.DebugWarpToDistance(start);
        yield return new WaitForSeconds(1.5f);
        float from = gm.MaxDistance;
        int phase = dir.PhaseAt(from);
        PlayerController.DebugSpeedScale = 1.6f;
        float t0 = Time.time;
        bool shotTaken = false;
        var pits = new List<float>();
        int islands = 0, maxProps = 0;
        var modes = new int[5];
        int spikes = 0;
        while (Time.time - t0 < seconds)
        {
            yield return BossGuard(gm, bm);
            if (Time.time - t0 > 1f && !bm.IsBossPhase) frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (!shotTaken && Time.time - t0 > 6f && !bm.IsBossPhase) { shotTaken = true; yield return Shot($"{(int)(start / 1000f):00}k_p{phase}"); }
            // 確認動画用: 各区間の2〜8秒を連番で書き出す(30fps固定)
            if (video && seconds > 30f && Time.time - t0 > 2f && Time.time - t0 < 8f && !bm.IsBossPhase)
            {
                Time.captureFramerate = 30;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"v_{videoFrame++:00000}.png"));
            }
            else if (video) Time.captureFramerate = 0;
            maxProps = Mathf.Max(maxProps, dir.PropCount);
            yield return null;
        }
        PlayerController.DebugSpeedScale = 1f;
        // 生成済みの地形/天井を、今いる所の前後で集計
        float px = pc.transform.position.x;
        CollectTerrain(tm, px - 60f, px + 60f, pits, ref islands);
        CollectCeiling(tm.cave, px - 60f, px + 60f, modes, ref spikes);
        int nodes = modes.Sum();
        string bg = tm.backgroundRenderer.sprite != null ? tm.backgroundRenderer.sprite.name : "-";
        L($"--- segment {start:F0}m -> {gm.MaxDistance:F0}m (phase P{phase}, now P{dir.PhaseAt(gm.MaxDistance)}) bg={bg} fillDepth={tm.groundFillDepth:F1}");
        L($"    ceiling nodes={nodes} open={Pct(modes[4], nodes)} normal={Pct(modes[0], nodes)} stake={Pct(modes[1], nodes)} low={Pct(modes[2], nodes)} high={Pct(modes[3], nodes)} stakes={spikes}");
        L($"    pits={pits.Count} widths=[{string.Join(",", pits.Select(p => p.ToString("F1")).Distinct())}] floatingPlatforms={islands} props(max)={maxProps}");
        L($"    falling/gates attached={LastCorridorDirector.FallingAttached + LastCorridorDirector.GateAttached - fall0} landed={FallingDebris.Landed - land0} encounters={encounters.Count - enc0} bosses={bosses.Count - boss0} playerHits={hits - hits0} obstacleContacts={ObstacleController.TotalContactDamage - contact0} gameOver={gm.IsGameOver}");
        for (int i = enc0; i < encounters.Count; i++) L("      " + encounters[i]);
        for (int i = boss0; i < bosses.Count; i++) L("      boss: " + bosses[i]);
        Check(!gm.IsGameOver, $"segment {start} still running");
        Check(gm.MaxDistance > from + 50f, $"segment {start} advanced ({from:F0} -> {gm.MaxDistance:F0})");
        if (phase >= 3) Check(modes[4] == nodes, "P3 no ceiling");
        if (phase >= 2 && start < 85000f) Check(islands > 0, "P2 floating corridor fragments");
        float expectPit = dir.pitWidths[Mathf.Min(phase, dir.pitWidths.Length - 1)];
        if (pits.Count > 0) Check(pits.All(p => p <= 4.0f), "pits stay jumpable (<=4.0m)");
    }

    // 比較用: 既存マップを同じ条件(同じ距離・速度・高速補助・時間)で走り、被弾数とフレーム時間だけ記録
    IEnumerator Baseline(GameManager gm, TerrainManager tm, PlayerController pc)
    {
        var bm = BossManager.Instance;
        StartCoroutine(KeepAlive(gm));
        StartCoroutine(AutoPickCards(gm));
        HighSpeedAssist.Instance.SetEnabled(true);
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        prevLives = gm.Lives;
        foreach (float st in new[] { 300f, 12000f, 26000f, 42000f, 56500f, 72000f, 86000f, 96000f })
        {
            int h0 = hits, c0 = ObstacleController.TotalContactDamage;
            gm.DebugWarpToDistance(st);
            yield return new WaitForSeconds(1.5f);
            PlayerController.DebugSpeedScale = 1.6f;
            float t0 = Time.time;
            while (Time.time - t0 < 32f) { yield return BossGuard(gm, bm); if (Time.time - t0 > 1f) frameMs.Add(Time.unscaledDeltaTime * 1000f); yield return null; }
            PlayerController.DebugSpeedScale = 1f;
            L($"--- baseline segment {st:F0}m -> {gm.MaxDistance:F0}m playerHits={hits - h0} obstacleContacts={ObstacleController.TotalContactDamage - c0}");
        }
        if (frameMs.Count > 0)
        {
            var s = frameMs.OrderBy(x => x).ToList();
            L($"frame ms: avg={s.Average():F2} p95={s[(int)(s.Count * 0.95f)]:F2} max={s[s.Count - 1]:F2}");
        }
        L(anyException ? "SOME EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"LastCorridorTour_baseline_{stageId}.txt"), log.ToString());
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0); else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static string Pct(int a, int n) => n > 0 ? $"{100f * a / n:F0}%" : "-";

    void CollectTerrain(TerrainManager tm, float x0, float x1, List<float> pits, ref int islands)
    {
        var chunks = (IList)typeof(TerrainManager).GetField("chunks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tm);
        foreach (var c in chunks)
        {
            var ct = c.GetType();
            float sx = (float)ct.GetField("startX").GetValue(c), ex = (float)ct.GetField("endX").GetValue(c);
            if (ex < x0 || sx > x1) continue;
            if (ct.GetField("type").GetValue(c).ToString() == "Pit") pits.Add(ex - sx);
        }
        var sky = (IList)typeof(TerrainManager).GetField("skyChunks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tm);
        foreach (var s in sky)
        {
            float sx = (float)s.GetType().GetField("startX").GetValue(s);
            if (sx >= x0 && sx <= x1) islands++;
        }
    }

    void CollectCeiling(CaveStage cave, float x0, float x1, int[] modes, ref int spikes)
    {
        if (cave == null) return;
        var nodes = (IList)typeof(CaveStage).GetField("nodes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cave);
        foreach (var n in nodes)
        {
            float x = (float)n.GetType().GetField("x").GetValue(n);
            if (x < x0 || x > x1) continue;
            modes[Mathf.Clamp((int)n.GetType().GetField("mode").GetValue(n), 0, 4)]++;
        }
        var sp = (IList)typeof(CaveStage).GetField("spikes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cave);
        foreach (var s in sp)
        {
            float x = (float)s.GetType().GetField("x").GetValue(s);
            if (x >= x0 && x <= x1) spikes++;
        }
    }

    // 道中のボス: 出現を記録 → 少し戦わせてから倒す → 報酬まで(実プレイヤーと同じ経路)
    IEnumerator BossGuard(GameManager gm, BossManager bm)
    {
        if (!bm.IsBossPhase || bm.DeathSpawned) yield break;
        float t0 = Time.time;
        while (bm.AliveBossCount == 0 && bm.IsBossPhase && Time.time - t0 < 8f) yield return null;
        yield return new WaitForSeconds(2.5f);
        var names = new List<string>();
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) names.Add(w.name);
        int d = 0, m = 0;
        foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) d++;
        foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) m++;
        if (d > 0) names.Add($"Dragon x{d}");
        if (m > 0) names.Add($"Majin x{m}");
        string label = $"{gm.MaxDistance:F0}m [{bm.BossMusicKey}] {string.Join(", ", names.GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key))}";
        bosses.Add(label);
        if (bosses.Count <= 6) yield return Shot($"boss_{bosses.Count:00}");
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) w.TakeDamage(99999, w.CenterWorld);
        foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
        foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
        t0 = Time.unscaledTime;
        while (bm.IsBossPhase && Time.unscaledTime - t0 < 20f)
        {
            // フェニックスの復活などで生き残ったボスは倒し直す
            foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead && w.Hp > 0) w.TakeDamage(99999, w.CenterWorld);
            yield return new WaitForSeconds(0.5f);
        }
        Check(!bm.IsBossPhase, "boss encounter ended: " + label);
    }

    void OnEncounter(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> list)
    {
        var ids = list.Where(e => e.def != null).Select(e => e.def.enemyId).ToList();
        foreach (var i in ids) enemyIds.Add(i);
        encounters.Add($"{rec.distance:F0}m {rec.band} / {rec.intensity} / {rec.formation}: {string.Join(",", ids)}");
    }

    bool stopKeepAlive;
    IEnumerator KeepAlive(GameManager gm)
    {
        var lives = typeof(GameManager).GetProperty("Lives");
        var setter = lives.GetSetMethod(true);
        while (!stopKeepAlive)
        {
            if (gm.Lives < prevLives) hits += prevLives - gm.Lives;
            if (gm.Lives < 50) setter.Invoke(gm, new object[] { 99 });
            prevLives = gm.Lives;
            yield return null;
        }
    }

    IEnumerator AutoPickCards(GameManager gm)
    {
        while (true)
        {
            if (gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.3f);
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }
            }
            yield return null;
        }
    }

    IEnumerator Shot(string name)
    {
        if (!capture) yield break;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"{shot++:00}_{name}.png"));
        yield return null;
    }
}

#if UNITY_EDITOR
public static class LastCorridorTourMenu
{
    [UnityEditor.MenuItem("Tools/OneMoreMile/Last Corridor/Tour Test (batch)")]
    public static void RunBatch()
    {
        UnityEditor.EditorPrefs.SetInt("LastCorridorTour", 1);
        string st = null;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lcTourStage") st = args[i + 1];
        UnityEditor.EditorPrefs.SetString("LastCorridorTourStage", st ?? "");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        UnityEditor.EditorApplication.EnterPlaymode();
    }
}
#endif
#endif

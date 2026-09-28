#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 天空回廊(Stage00)のEnemy拡張 + Encounter/Formation(2026-09-28) - Editor専用の自動確認。結果は SkyEncounterAutoTest.txt。
//  1) 新規8種を1体ずつ出して、役割どおりの状態の流れになるか(予兆→攻撃→硬直、石像→起動、溜め→落雷、Hit&Away)
//  2) 翼のある種の当たり判定が胴体中心か / 状態ごとの絵が出ているか
//  3) 天空専用Formation(14)を1つずつ強制: 地上の敵は地面/浮島、飛ぶ敵は決まった高さの空中Slot
//  4) 距離Bandを走り、Global Enemy→天空固有Enemyへ比率が変わるか、Rest/波/危険Formationの連続防止
//  5) ボス直前/ボス戦中は出さない
//  6) 高速でも予兆は見える所で始まる
// 起動: Tools/OneMoreMile/Sky Encounter Test (batch)。 -skyQuick で1)2)だけ。 -skyShotsOnly でポーズの撮影だけ(SkyEnemyShots/)。
public class SkyEncounterAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("SkyEncounterTest", 0) != 1) return;
        EditorPrefs.SetInt("SkyEncounterTest", 0);
        new GameObject("SkyEncounterTest").AddComponent<SkyEncounterAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[SkyEncTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    GameManager gm; PlayerController pc; TerrainManager tm; EncounterDirector dir;
    float savedSpawnChance;
    readonly List<GameObject> spawned = new List<GameObject>();

    static readonly string[] SkyIds = { "sky_slime", "sky_hound", "harpy", "gargoyle", "celestial_knight", "ancient_sentinel", "storm_spirit", "sky_hunter" };
    static readonly HashSet<string> GlobalIds = new HashSet<string> { "goblin", "goblin_elite", "irregular_imp", "shooter_archer", "heavy_ogre", "chaser_runner", "rusher_runner", "flying_wyvern" };

    IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(1300f);
        L("WATCHDOG: test did not finish in time");
        Finish();
    }

    void Finish()
    {
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../SkyEncounterAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance; pc = PlayerController.Instance; tm = TerrainManager.Instance; dir = EncounterDirector.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage("sky_corridor");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        gm.DebugSetInvincible(true);
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        StartCoroutine(AutoPickCards());

        // ---- 0) Stage00が共通Directorで動く ----
        var prof = StageEncounterProfile.Find("sky_corridor");
        Check(prof != null && EncounterDirector.HandlesStage("sky_corridor"), "Stage00 is handled by the common EncounterDirector (Profile_sky_corridor)");
        Check(prof != null && prof.islandAware && prof.stageFormations.Count >= 13, $"sky profile: islandAware, {prof?.stageFormations.Count} stage formations");
        foreach (string id in SkyIds)
        {
            var d = EnemyDatabase.FindById(id);
            Check(d != null && d.sprite != null && d.stageIds != null && d.stageIds.Contains("sky_corridor"), $"{id}: definition + sprite + sky-only");
        }

        // ---- 1)2) 1体ずつ ----
        savedSpawnChance = tm.enemySpawnChance;
        tm.enemySpawnChance = 0f;                     // Directorを止める(1体ずつ確認する間)
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        yield return RunUntilFlat(30f);
        pc.autoRunEnabled = false;                    // プレイヤーを止めて、敵の状態の流れだけを見る
        yield return new WaitForSeconds(0.3f);
        if (System.Environment.GetCommandLineArgs().Contains("-skyShotsOnly")) { yield return PoseShots(); Finish(); yield break; }
        if (Only("slime")) yield return TestSlime();
        if (Only("hound")) yield return TestHound();
        if (Only("harpy")) yield return TestHarpy();
        if (Only("gargoyle")) yield return TestGargoyle();
        if (Only("knight")) yield return TestKnight();
        if (Only("sentinel")) yield return TestSentinel();
        if (Only("storm")) yield return TestStorm();
        if (Only("hunter")) yield return TestHunter();
        Clear();
        yield return PoseShots();

        bool quick = System.Environment.GetCommandLineArgs().Contains("-skyQuick");
        if (!quick)
        {
            pc.autoRunEnabled = true;
            tm.enemySpawnChance = savedSpawnChance;
            yield return TestFormations();
            yield return TestBands();
            yield return TestHighSpeed();
            yield return TestBoss();
        }
        Finish();
    }

    // ===================================================================== //
    // 道具
    // ===================================================================== //
    // -skyOnly <name>: 1体ずつの確認をその種だけにする(調査用。例: -skyOnly hunter)
    static bool Only(string name)
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-skyOnly");
        return i < 0 || i + 1 >= a.Length || a[i + 1] == name;
    }

    IEnumerator AutoPickCards()
    {
        while (true)
        {
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

    // 平らな地面が前方12mほど続く所まで走る(1体ずつの確認を穴の上でしない)
    IEnumerator RunUntilFlat(float maxSeconds)
    {
        float t = 0f;
        while (t < maxSeconds)
        {
            float x = pc.transform.position.x;
            bool ok = pc.IsGrounded;
            for (float d = -2f; d <= 14f && ok; d += 0.5f) { float? g = tm.GetHeightAt(x + d); if (!g.HasValue || tm.IsNearPit(x + d, 1f)) ok = false; }
            if (ok) yield break;
            if (pc.IsGrounded && tm.IsNearPit(x + 2f, 0.4f)) pc.debugInjectFlick = PlayerController.FlickDirection.Up;
            yield return null;
            pc.debugInjectFlick = null;
            t += Time.deltaTime;
        }
    }

    GameObject Spawn(string id, EnemyAiTier tier, float dx, float air = 2.2f)
    {
        var def = EnemyDatabase.FindById(id);
        if (def == null) { L($"  (no definition {id})"); return null; }
        float x = pc.transform.position.x + dx;
        float g = tm.GetHeightAt(x) ?? pc.transform.position.y;
        bool flying = def.movementType == EnemyMovementType.Flying;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, flying ? g + air : g), tier, def.behaviorKind);
        if (go != null) spawned.Add(go);
        return go;
    }

    void Clear()
    {
        foreach (var go in spawned) if (go != null) go.SetActive(false);
        spawned.Clear();
    }

    static string State(GameObject go) { var b = go != null ? go.GetComponent<EnemySpecialBehavior>() : null; return b != null ? b.DebugState : "-"; }
    static string Phase(GameObject go) { string s = State(go); int i = s.LastIndexOf(' '); return i >= 0 ? s.Substring(i + 1) : s; }

    // 状態の流れを記録(同じ状態が続く間は1つにまとめ、状態ごとの滞在時間も)
    IEnumerator Observe(GameObject go, float seconds, List<(string phase, float dur)> seq, System.Action perFrame = null)
    {
        float t = 0f; string cur = null; float start = 0f;
        while (t < seconds && go != null && go.activeInHierarchy)
        {
            string ph = Phase(go);
            if (ph != cur) { if (cur != null) seq.Add((cur, t - start)); cur = ph; start = t; }
            perFrame?.Invoke();
            yield return null;
            t += Time.deltaTime;
        }
        if (cur != null) seq.Add((cur, t - start));
    }

    static string Seq(List<(string phase, float dur)> s) => string.Join(" > ", s.Select(p => $"{p.phase}({p.dur:F2})"));
    static bool Has(List<(string phase, float dur)> s, string ph) => s.Any(p => p.phase == ph);
    static bool Order(List<(string phase, float dur)> s, params string[] order)
    {
        int k = 0;
        foreach (var p in s) if (k < order.Length && p.phase == order[k]) k++;
        return k == order.Length;
    }

    SpriteRenderer Visual(GameObject go) => go.GetComponentInChildren<SpriteRenderer>();

    bool CoreCollider(GameObject go, float maxWidthFrac)
    {
        var col = go.GetComponent<BoxCollider2D>(); var sr = Visual(go);
        if (col == null || sr == null || sr.sprite == null) return false;
        float frac = col.size.x / sr.sprite.bounds.size.x;
        L($"    collider {col.size.x:F2}x{col.size.y:F2} vs sprite {sr.sprite.bounds.size.x:F2}x{sr.sprite.bounds.size.y:F2} (width {frac:P0})");
        return frac <= maxWidthFrac + 0.001f;
    }

    // 目視確認用: 1種ずつ、プレイヤーの横に並べて各ポーズの絵を撮る(大きさ・足元・向き)。SkyEnemyShots/<id>.png
    IEnumerator PoseShots()
    {
        var cf = FindFirstObjectByType<CameraFollow>();
        Camera cam = cf != null ? cf.GetComponent<Camera>() : Camera.main;
        if (cam == null) yield break;
        bool cfWas = cf != null && cf.enabled; if (cf != null) cf.enabled = false;
        float size0 = cam.orthographicSize; Vector3 cam0 = cam.transform.position;
        string outDir = System.IO.Path.Combine(Application.dataPath, "../SkyEnemyShots"); System.IO.Directory.CreateDirectory(outDir);
        const int W = 360, H = 300;
        var rt = new RenderTexture(W, H, 24);
        foreach (string id in SkyIds)
        {
            var def = EnemyDatabase.FindById(id);
            if (def == null) continue;
            var list = new List<(string name, Sprite sp)> { ("idle", def.sprite) };
            if (def.runFrames != null) for (int i = 0; i < def.runFrames.Length; i++) list.Add(("move" + i, def.runFrames[i]));
            var ps = def.poses;
            if (ps != null) foreach (var kv in new (string, Sprite)[] { ("dormant", ps.dormant), ("wake", ps.wake), ("telegraph", ps.telegraph), ("charge", ps.charge), ("attack", ps.attack), ("dive", ps.dive), ("recover", ps.recover), ("hit", ps.hit), ("death", ps.death) })
                if (kv.Item2 != null) list.Add(kv);
            var go = Spawn(id, EnemyAiTier.T0, 2.4f, 1.2f);
            if (go == null) continue;
            yield return null;
            // 動き/絵の切り替えだけ止める(輪郭のSpriteOutlineは本体の絵に追従させたまま)
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>()) if (b is EnemyAnimator || b is EnemySpecialBehavior || b is EnemyController) b.enabled = false;
            var rb = go.GetComponent<Rigidbody2D>(); if (rb != null) rb.simulated = false;
            var sr = Visual(go);
            var sheet = new Texture2D(W * list.Count, H, TextureFormat.RGB24, false);
            for (int i = 0; i < list.Count; i++)
            {
                sr.sprite = list[i].sp; sr.transform.localPosition = Vector3.zero;
                yield return null; // 輪郭(LateUpdate)が新しい絵に追従してから撮る
                cam.orthographicSize = 1.6f;
                Vector3 p = pc.transform.position; cam.transform.position = new Vector3(p.x + 1.3f, p.y + 1.1f, cam0.z);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, W, H), W * i, 0); RenderTexture.active = null;
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, id + ".png"), sheet.EncodeToPNG());
            L($"  [shot] {id}: {string.Join(" ", list.Select(x => x.name))} flipX={sr.flipX} scaleX={go.transform.localScale.x:F2}");
            Destroy(sheet);
            Clear();
            yield return null;
        }
        rt.Release();
        cam.orthographicSize = size0; cam.transform.position = cam0; if (cf != null) cf.enabled = cfWas;
    }

    // ===================================================================== //
    // 1体ずつ
    // ===================================================================== //
    IEnumerator TestSlime()
    {
        L("[Sky Slime]");
        var go = Spawn("sky_slime", EnemyAiTier.T0, 3f);
        yield return new WaitForSeconds(0.2f);
        var vis = Visual(go).transform;
        float rootY0 = go.transform.position.y, vMin = 99f, vMax = -99f, rootDev = 0f;
        for (float t = 0f; t < 2f; t += Time.deltaTime) { vMin = Mathf.Min(vMin, vis.localPosition.y); vMax = Mathf.Max(vMax, vis.localPosition.y); rootDev = Mathf.Max(rootDev, Mathf.Abs(go.transform.position.y - rootY0)); yield return null; }
        Check(go.GetComponent<EnemySpecialBehavior>() == null, "T0: no self attack (no behaviour component)");
        Check(vMax - vMin > 0.08f && rootDev < 0.01f, $"idle: body floats up/down (visual {vMax - vMin:F2}m) while the hitbox stays put (root {rootDev:F3}m)");
        var ec = go.GetComponent<EnemyController>();
        float up = (float)typeof(EnemyController).GetField("launchUpSpeed").GetValue(ec);
        Check(up > 9f * 1.15f, $"launch-friendly: launchUpSpeed {up:F1} (base 9)");
        Clear();
        // 実際に打ち上げる: 目の前(1.1m)で上攻撃
        var s2 = Spawn("sky_slime", EnemyAiTier.T0, 1.1f);
        yield return new WaitForSeconds(0.2f);
        float y0 = s2.transform.position.y, maxRise = 0f;
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
        for (float t = 0f; t < 0.9f && s2 != null && s2.activeInHierarchy; t += Time.deltaTime) { maxRise = Mathf.Max(maxRise, s2.transform.position.y - y0); yield return null; }
        Check(maxRise > 1.2f, $"up attack launches it (rise {maxRise:F2}m)");
        yield return new WaitForSeconds(1.2f);
        Clear();
    }

    IEnumerator TestHound()
    {
        L("[Sky Hound]");
        var go = Spawn("sky_hound", EnemyAiTier.T1, 3.2f);
        var seq = new List<(string, float)>();
        float x0 = go.transform.position.x, idleDrift = 0f, attackMove = 0f, lastX = x0, yDev = 0f; float y0 = go.transform.position.y;
        // 地面からの高さで見る(突進先の地面の高さが違っても「浮いた」と誤判定しない)
        float off0 = y0 - (tm.GetHeightAt(x0) ?? y0); string yDevInfo = "";
        yield return Observe(go, 5f, seq, () =>
        {
            string ph = Phase(go); float dx = go.transform.position.x - lastX; lastX = go.transform.position.x;
            if (ph == "Attack") attackMove += Mathf.Abs(dx); else if (ph == "Idle") idleDrift += Mathf.Abs(dx);
            { var gp = go.transform.position; float off = gp.y - (tm.GetHeightAt(gp.x) ?? gp.y); float dv = Mathf.Abs(off - off0);
              if (dv > yDev) { yDev = dv; yDevInfo = $"phase={ph} dx={gp.x - x0:F2} rawDy={gp.y - y0:F2} enabled={go.GetComponent<EnemySpecialBehavior>().isActiveAndEnabled}"; } }
        });
        L("    " + Seq(seq));
        Check(Order(seq, "Telegraph", "Attack", "Recover"), "T1: telegraph > short lunge + bite > recover");
        Check(attackMove > 0.8f && idleDrift < 0.1f, $"T1 lunges ({attackMove:F2}m) but otherwise stays put ({idleDrift:F2}m)");
        Check(yDev < 0.35f, $"stays on the ground (not flying) (height above ground changes {yDev:F2}m; {yDevInfo})");
        Clear();
        var t2 = Spawn("sky_hound", EnemyAiTier.T2, 6f);
        float moved = 0f, lx = t2.transform.position.x;
        for (float t = 0f; t < 6f; t += Time.deltaTime) { if (Phase(t2) == "Idle") moved += Mathf.Abs(t2.transform.position.x - lx); lx = t2.transform.position.x; yield return null; }
        Check(moved > 0.3f, $"T2 moves around in a small range ({moved:F2}m)");
        Clear();
    }

    IEnumerator TestHarpy()
    {
        L("[Harpy]");
        var t1 = Spawn("harpy", EnemyAiTier.T1, 5f);
        var s1 = new List<(string, float)>();
        yield return Observe(t1, 4f, s1);
        Check(!Has(s1, "Telegraph"), "T1: hover only (no dive)");
        Check(CoreCollider(t1, 0.55f), "wing tips are not contact damage (collider = core body)");
        Clear();
        var go = Spawn("harpy", EnemyAiTier.T2, 5f);
        yield return new WaitForSeconds(0.1f);
        float baseY = go.transform.position.y, minY = baseY; bool divePose = false, retreated = false; float backY = float.NaN;
        var def = EnemyDatabase.FindById("harpy");
        var seq = new List<(string, float)>();
        yield return Observe(go, 6f, seq, () =>
        {
            string ph = Phase(go);
            if (float.IsNaN(backY)) minY = Mathf.Min(minY, go.transform.position.y);
            if (ph == "Attack" && Visual(go).sprite == def.poses.dive) divePose = true;
            if (ph == "Retreat") retreated = true;
            if (ph == "Idle" && retreated && float.IsNaN(backY)) backY = go.transform.position.y;   // 1回目のダイブの後に戻った高さ
        });
        L("    " + Seq(seq));
        Check(Order(seq, "Telegraph", "Attack", "Hold", "Recover", "Retreat", "Idle"), "T2: hover > telegraph > short dive > recover > back to its altitude");
        // 待機中は上下にゆれる(±harpyBobAmplitude)ので、戻った高さはゆれ幅ぶんずれてよい
        float bobTol = 2f * go.GetComponent<EnemySpecialBehavior>().harpyBobAmplitude + 0.15f;
        Check(baseY - minY > 0.5f && !float.IsNaN(backY) && Mathf.Abs(backY - baseY) < bobTol, $"dives {baseY - minY:F2}m and returns to its altitude ({backY - baseY:+0.00;-0.00}m, hover bob tolerance {bobTol:F2}m)");
        Check(divePose, "dive pose sprite shown while diving");
        var tele = seq.FirstOrDefault(p => p.Item1 == "Telegraph");
        Check(tele.Item2 >= 0.5f, $"telegraph lasts {tele.Item2:F2}s (visible before the dive)");
        Clear();
    }

    IEnumerator TestGargoyle()
    {
        L("[Gargoyle]");
        var def = EnemyDatabase.FindById("gargoyle");
        // 画面外: 石像のまま(穴の上に置くと落ちて消えるので、地面のある所を探す)
        float farDx = 40f;
        for (float d = 40f; d < 70f; d += 0.5f) { float fx = pc.transform.position.x + d; if (tm.GetHeightAt(fx).HasValue && !tm.IsNearPit(fx, 1.5f)) { farDx = d; break; } }
        var far = Spawn("gargoyle", EnemyAiTier.T1, farDx);
        yield return new WaitForSeconds(1.5f);
        Check(far != null && Phase(far) == "Dormant" && Visual(far).sprite == def.poses.dormant, $"far away / off screen: stays a statue (dormant pose) [phase={(far != null ? Phase(far) : "null")} active={(far != null && far.activeInHierarchy)} sprite={(far != null ? Visual(far).sprite?.name : "-")}]");
        Clear();
        var go = Spawn("gargoyle", EnemyAiTier.T1, 2.6f);   // 止まったプレイヤーの目の前(T1はその場で攻撃する)
        var seq = new List<(string, float)>();
        bool wakePose = false, attackBeforeTele = false; bool seenTele = false;
        yield return Observe(go, 5f, seq, () =>
        {
            string ph = Phase(go);
            if (ph == "Wake" && Visual(go).sprite == def.poses.wake) wakePose = true;
            if (ph == "Telegraph") seenTele = true;
            if (ph == "Attack" && !seenTele) attackBeforeTele = true;
        });
        L("    " + Seq(seq));
        Check(Order(seq, "Dormant", "Wake", "Idle", "Telegraph", "Attack", "Recover"), "statue > wake (glow + wings) > telegraph > attack > recover");
        var wake = seq.FirstOrDefault(p => p.Item1 == "Wake");
        Check(wake.Item2 >= 0.7f && wakePose && !attackBeforeTele, $"wake-up is visible ({wake.Item2:F2}s, wake pose) and never attacks without a telegraph");
        Check(CoreCollider(go, 0.6f), "wings are not contact damage");
        Clear();
        // Gargoyle Gate: 3体が近くにいても同時に攻撃しない
        var a = Spawn("gargoyle", EnemyAiTier.T1, 2.2f); var b = Spawn("gargoyle", EnemyAiTier.T1, 2.7f); var c = Spawn("gargoyle", EnemyAiTier.T1, 3.1f);
        int both = 0, attacks = 0; string pa = "", pb = "", pcs = "";
        for (float t = 0f; t < 7f; t += Time.deltaTime)
        {
            int n = 0;
            foreach (var g in new[] { a, b, c }) { string ph = Phase(g); if (ph == "Telegraph" || ph == "Attack") n++; }
            if (n >= 2) both++;
            string na = Phase(a), nb = Phase(b), nc = Phase(c);
            if (na == "Attack" && pa != "Attack") attacks++; if (nb == "Attack" && pb != "Attack") attacks++; if (nc == "Attack" && pcs != "Attack") attacks++;
            pa = na; pb = nb; pcs = nc;
            yield return null;
        }
        Check(both == 0 && attacks >= 2, $"gargoyle gate: never two telegraphing/attacking at once (overlap frames {both}, attacks {attacks})");
        Clear();
    }

    IEnumerator TestKnight()
    {
        L("[Celestial Knight]");
        var go = Spawn("celestial_knight", EnemyAiTier.T3, 7f);
        float x0 = go.transform.position.x, minX = x0;
        var seq = new List<(string, float)>();
        yield return Observe(go, 6f, seq, () => minX = Mathf.Min(minX, go.transform.position.x));
        L("    " + Seq(seq) + $" approached {x0 - minX:F2}m");
        var kb = go.GetComponent<EnemySpecialBehavior>();
        Check(x0 - minX > 1f && x0 - minX <= kb.knightLeash + kb.knightStepDistance + 0.3f, $"approaches the player on purpose but only a short distance ({x0 - minX:F2}m, leash {kb.knightLeash}m)");
        Check(Order(seq, "Telegraph", "Attack", "Recover"), "telegraph > slash > recover");
        Clear();
    }

    IEnumerator TestSentinel()
    {
        L("[Ancient Sentinel]");
        var go = Spawn("ancient_sentinel", EnemyAiTier.T1, 4f);
        bool band = false;
        var seq = new List<(string, float)>();
        yield return Observe(go, 6f, seq, () => { if (Phase(go) == "Telegraph" && FindObjectsByType<SkyWarnBand>(FindObjectsSortMode.None).Length > 0) band = true; });
        L("    " + Seq(seq));
        var tele = seq.FirstOrDefault(p => p.Item1 == "Telegraph"); var rec = seq.FirstOrDefault(p => p.Item1 == "Recover");
        Check(tele.Item2 >= 1.1f && band, $"long telegraph ({tele.Item2:F2}s) with the slam area shown on the ground");
        Check(rec.Item2 >= 1.1f, $"long recovery after the slam ({rec.Item2:F2}s) = time to counter-attack");
        var ec = go.GetComponent<EnemyController>();
        float up = (float)typeof(EnemyController).GetField("launchUpSpeed").GetValue(ec);
        Check(up < 9f * 0.7f && up > 0f, $"launch resistance but not immune (launchUpSpeed {up:F1})");
        Check(EnemyDatabase.FindById("ancient_sentinel").hpMultiplier >= 3f, "heavy HP");
        Clear();
    }

    IEnumerator TestStorm()
    {
        L("[Storm Spirit]");
        var go = Spawn("storm_spirit", EnemyAiTier.T4, 6f, 3f);
        float x0 = go.transform.position.x, xDev = 0f; bool strikeFixed = true; float strikeX = float.NaN; bool strikeSeen = false;
        var seq = new List<(string, float)>();
        yield return Observe(go, 6f, seq, () =>
        {
            xDev = Mathf.Max(xDev, Mathf.Abs(go.transform.position.x - x0));
            var st = FindObjectsByType<SkyStrike>(FindObjectsSortMode.None);
            if (st.Length > 0) { strikeSeen = true; if (float.IsNaN(strikeX)) strikeX = st[0].WorldX; else if (Mathf.Abs(st[0].WorldX - strikeX) > 0.05f) strikeFixed = false; }
        });
        L("    " + Seq(seq));
        Check(Order(seq, "Telegraph", "Recover") && strikeSeen, "charge (ground warning) > lightning > recover");
        var tele = seq.FirstOrDefault(p => p.Item1 == "Telegraph");
        Check(tele.Item2 >= 0.95f && strikeFixed && Mathf.Abs(strikeX - x0) < 0.2f, $"the strike is announced {tele.Item2:F2}s ahead, right below it, and does not move");
        Check(xDev < 0.1f, $"does not chase ({xDev:F2}m)");
        Check(CoreCollider(go, 0.55f), "core-body collider");
        Clear();
    }

    IEnumerator TestHunter()
    {
        L("[Sky Hunter]");
        var go = Spawn("sky_hunter", EnemyAiTier.T5, 12f, 3f);
        var hb = go.GetComponent<EnemySpecialBehavior>();
        float maxReposition = 0f; Vector3 last = go.transform.position; string prevPh = "", maxInfo = ""; float spawnT = Time.time;
        var seq = new List<(string, float)>();
        yield return Observe(go, 24f, seq, () =>
        {
            Vector3 p = go.transform.position; string ph = Phase(go);
            // 状態が変わったフレームは前の状態の移動を含むので除く。被弾で行動が止まっている間(ノックバック等で動く)も除く。
            if (!hb.isActiveAndEnabled) ph = "(reacting)";
            // 出現直後(0.6s)は位置の初期化が入るので除く
            if (ph == "Retreat" && prevPh == "Retreat" && Time.deltaTime > 0f && Time.time - spawnT > 0.6f)
            {
                float sp = (p - last).magnitude / Time.deltaTime;
                if (sp > maxReposition) { maxReposition = sp; maxInfo = $"dt={Time.deltaTime:F4} d=({p.x - last.x:F3},{p.y - last.y:F3}) runV={PlayerController.RunFrameSpeed:F2} ts={Time.timeScale:F2} ecEnabled={go.GetComponent<EnemyController>().enabled} dxToPlayer={p.x - pc.transform.position.x:F2} t={Time.time - spawnT:F2}"; }
            }
            last = p; prevPh = ph;
        });
        L("    max reposition sample: " + maxInfo);
        L("    " + Seq(seq).Substring(0, Mathf.Min(400, Seq(seq).Length)));
        int attacks = seq.Count(p => p.Item1 == "Attack");
        Check(Order(seq, "Telegraph", "Attack", "Recover", "Retreat", "Telegraph", "Attack", "Recover"), "approach > telegraph > dash > recover > back off > approach again");
        Check(attacks <= hb.hunterMaxAttacks && (!go.activeInHierarchy || Has(seq, "Leave")), $"gives up after {attacks} attacks (max {hb.hunterMaxAttacks}) and leaves - no endless chase");
        Check(maxReposition <= hb.hunterSpeedBonus * 1.3f + 0.2f, $"repositioning speed {maxReposition:F1} m/s <= player speed + {hb.hunterSpeedBonus}");
        var rec = seq.FirstOrDefault(p => p.Item1 == "Recover");
        Check(rec.Item2 >= 0.7f, $"recovery after each dash ({rec.Item2:F2}s) = counter window");
        Clear();
    }

    // ===================================================================== //
    // 3) Formation
    // ===================================================================== //
    readonly List<(EncounterDirector.Record rec, EncounterFormation f, List<(GameObject go, EnemyDefinition def, EncounterSlotKind slot)> list)> seen = new List<(EncounterDirector.Record, EncounterFormation, List<(GameObject, EnemyDefinition, EncounterSlotKind)>)>();

    IEnumerator TestFormations()
    {
        L("[Formations]");
        EncounterDirector.OnEncounterSpawned = (r, f, l) => seen.Add((r, f, new List<(GameObject, EnemyDefinition, EncounterSlotKind)>(l)));
        string[] ids = { "sky_line", "sky_pack", "aerial_line", "sky_ground_air", "aerial_stair", "launch_bridge", "gargoyle_gate", "guardian_wall",
                         "knight_patrol", "storm_zone", "hunter_attack", "aerial_wave", "sky_gauntlet", "rest" };
        EncounterDirector.DebugDistanceOffset = 10500f - gm.MaxDistance; // 全種が出る深部のBand
        int groundBad = 0, airBad = 0, islandPlaced = 0, flyingOnGround = 0;
        foreach (string id in ids)
        {
            int before = seen.Count;
            int recBefore = dir.Recent.Count;
            EncounterDirector.ForceFormation(id, 1);
            float t = 0f; bool got = false;
            while (t < 30f)
            {
                yield return JumpPits(0.1f);
                t += 0.1f;
                if (id == "rest") { for (int i = recBefore; i < dir.Recent.Count; i++) if (dir.Recent[i].formation == "rest") got = true; }
                else for (int i = before; i < seen.Count; i++) if (seen[i].rec.formation == id) got = true;
                if (got) break;
            }
            EncounterDirector.DebugDistanceOffset = 10500f - gm.MaxDistance;
            if (!got) { Check(false, $"{id}: spawned (timeout)"); continue; }
            if (id == "rest") { Check(true, "rest: an empty open-sky section"); continue; }
            var e = seen.Last(s => s.rec.formation == id);
            var sb = new StringBuilder();
            foreach (var m in e.list)
            {
                if (m.go == null) continue;
                Vector3 p = m.go.transform.position;
                float ground = tm.GetHeightAt(p.x) ?? p.y;
                float? isl = tm.GetSkyHeightAt(p.x);
                var ec = m.go.GetComponent<EnemyController>();
                bool flying = m.def.movementType == EnemyMovementType.Flying;
                if (EncounterSlots.IsAir(m.slot))
                {
                    // 出現した瞬間の位置で測る(飛ぶ敵はその後ダイブ/上下するため)
                    var mem = e.rec.members.FirstOrDefault(mm => mm.enemyId == m.def.enemyId && mm.slot == m.slot && Mathf.Abs((float)(mm.x - FloatingOrigin.ToLogical(p.x))) < 30f);
                    float sx = (float)(mem.x - FloatingOrigin.Offset);
                    float g0 = tm.GetGroundLineAt(sx);
                    float? i0 = tm.GetSkyHeightAt(sx); if (!i0.HasValue) i0 = tm.GetSkyHeightAt(sx - 1f); if (!i0.HasValue) i0 = tm.GetSkyHeightAt(sx + 1f);
                    float baseY = i0.HasValue && i0.Value > g0 ? i0.Value : g0;
                    float h = mem.y - baseY;
                    if (!flying || h < 1.3f || h > 5.6f) airBad++; // 上限: 浮島の上の「低」より上へ「中/高」を持ち上げる分(KeepAirOrder)を含む
                    sb.Append($"{m.def.enemyId}:{m.slot}@{h:F1} ");
                }
                else
                {
                    if (flying) flyingOnGround++;
                    float surf = ec != null && ec.onIsland && isl.HasValue ? isl.Value : ground;
                    if (ec != null && ec.onIsland) islandPlaced++;
                    if (Mathf.Abs(p.y - surf) > 0.6f) groundBad++;
                    sb.Append($"{m.def.enemyId}:{m.slot}{(ec != null && ec.onIsland ? "(island)" : "")} ");
                }
            }
            L($"  {id} [{e.rec.intensity}] x{e.list.Count}: {sb}");
            Check(e.list.Count >= 1, $"{id}: spawned {e.list.Count} enemies");
            if (id == "hunter_attack") Check(e.list.Count(m => m.def.enemyId == "sky_hunter") == 1, "hunter attack: exactly one Sky Hunter");
            if (id == "storm_zone") Check(e.list.Count(m => m.def.enemyId == "storm_spirit") == 1, "storm zone: one Storm Spirit");
            if (id == "guardian_wall") Check(e.list.Count(m => m.def.enemyId == "ancient_sentinel") == 1 && e.list.Count >= 2, "guardian wall: one Sentinel + a few others (not a wall of Sentinels)");
            if (id == "aerial_stair")
            {
                // 出現した瞬間の高さで比べる(ハーピーはその後ダイブする)
                float YOf(EncounterSlotKind k) { var ms = e.rec.members.Where(mm => mm.slot == k).ToList(); return ms.Count > 0 ? (float)ms[0].y : float.NaN; }
                float lo = YOf(EncounterSlotKind.AirLow), mid = YOf(EncounterSlotKind.AirMiddle), hi = YOf(EncounterSlotKind.AirHigh);
                Check(lo < mid && mid < hi && hi > lo + 1.2f, $"aerial stair: low > middle > high (spawn y {lo:F1} / {mid:F1} / {hi:F1})");
            }
            yield return new WaitForSeconds(1f);
        }
        Check(groundBad == 0 && flyingOnGround == 0, $"ground enemies stand on the ground/island (bad {groundBad}, flying in ground slots {flyingOnGround})");
        Check(airBad == 0, $"flying enemies at the air-slot heights (bad {airBad})");
        L($"  enemies placed on floating islands: {islandPlaced}");
        EncounterDirector.DebugDistanceOffset = 0f;
    }

    IEnumerator JumpPits(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            pc.debugInjectFlick = pc.IsGrounded && tm.IsNearPit(pc.transform.position.x + lead, 0.4f) ? PlayerController.FlickDirection.Up : (PlayerController.FlickDirection?)null;
            yield return null;
            t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
    }

    // ===================================================================== //
    // 4) 距離Band: Global → 天空固有、Rest/波/危険の連続防止
    // ===================================================================== //
    IEnumerator TestBands()
    {
        L("[Distance bands]");
        float[] starts = { 200f, 1200f, 3000f, 6000f, 11000f };
        var shares = new List<float>();
        int dangerViolations = 0, longStreak = 0, restTotal = 0, total = 0;
        PlayerController.DebugSpeedScale = 2.5f;
        foreach (float d in starts)
        {
            int from = dir.Recent.Count;
            float t = 0f;
            while (t < 32f)
            {
                EncounterDirector.DebugDistanceOffset = d - gm.MaxDistance;
                yield return JumpPits(0.1f); t += 0.1f;
            }
            var recs = dir.Recent.Skip(from).ToList();
            int glob = 0, sky = 0, rests = 0;
            foreach (var r in recs) { if (r.intensity == EncounterIntensity.Rest) rests++; foreach (var m in r.members) { if (GlobalIds.Contains(m.enemyId)) glob++; else sky++; } }
            float share = glob + sky > 0 ? glob / (float)(glob + sky) : 0f;
            shares.Add(share);
            restTotal += rests; total += recs.Count;
            // 危険Formationの連続/休憩なしの連戦
            var prof = dir.Profile;
            for (int i = 0; i < recs.Count; i++)
            {
                var f = prof.FindFormation(recs[i].formation);
                if (f == null || string.IsNullOrEmpty(f.dangerGroup)) continue;
                for (int k = 1; k <= f.groupCooldown && i - k >= 0; k++) { var g = prof.FindFormation(recs[i - k].formation); if (g != null && g.dangerGroup == f.dangerGroup) dangerViolations++; }
            }
            int streak = 0; foreach (var r in recs) { streak = r.intensity == EncounterIntensity.Rest ? 0 : streak + 1; longStreak = Mathf.Max(longStreak, streak); }
            var kinds = recs.SelectMany(r => r.members).GroupBy(m => m.enemyId).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}");
            var fs = recs.GroupBy(r => r.formation).Select(g => $"{g.Key}x{g.Count()}");
            L($"  band@{d:F0}m: {recs.Count} encounters (rest {rests}) global share {share:P0} | {string.Join(" ", kinds)} | {string.Join(" ", fs)}");
        }
        PlayerController.DebugSpeedScale = 1f;
        EncounterDirector.DebugDistanceOffset = 0f;
        // 設定(各Bandの敵の重み)でのGlobalの割合は距離とともに必ず下がる。実測は1帯6〜7回と少ないので、浅い2帯と深い2帯の平均で比べる。
        var cfg = new List<float>();
        foreach (var b in dir.Profile.bands)
        {
            float gw = b.enemies.Where(e => GlobalIds.Contains(e.enemyId)).Sum(e => e.weight), all = b.enemies.Sum(e => e.weight);
            cfg.Add(all > 0f ? gw / all : 0f);
        }
        bool cfgFalls = true; for (int i = 1; i < cfg.Count; i++) if (cfg[i] >= cfg[i - 1]) cfgFalls = false;
        L($"  configured global weight share per band: {string.Join(" > ", cfg.Select(s => s.ToString("P0")))}");
        Check(cfgFalls, "configured global share falls band by band");
        float shallow = (shares[0] + shares[1]) * 0.5f, deep = (shares[3] + shares[4]) * 0.5f;
        Check(shallow > deep + 0.1f && shares[shares.Count - 1] < 0.15f, $"measured global share falls with distance ({string.Join(" > ", shares.Select(s => s.ToString("P0")))}; shallow avg {shallow:P0} vs deep avg {deep:P0})");
        Check(restTotal > 0 && restTotal >= total / 6, $"Rest / Open Sky sections appear ({restTotal}/{total})");
        Check(dangerViolations == 0, $"Storm Zone / Hunter Attack / Guardian Wall never back to back (violations {dangerViolations})");
        Check(longStreak <= dir.Profile.maxEncountersWithoutRest, $"no long fights without rest (max streak {longStreak})");
    }

    // ===================================================================== //
    // 6) 高速: 予兆は見える所で始まる
    // ===================================================================== //
    IEnumerator TestHighSpeed()
    {
        L("[High speed telegraphs]");
        PlayerController.DebugSpeedScale = 6f;
        var starts = new List<(string id, float dx, bool onScreen, float v)>();
        var last = new Dictionary<EnemySpecialBehavior, string>();
        Camera cam = Camera.main;
        float t = 0f;
        while (t < 28f)
        {
            EncounterDirector.DebugDistanceOffset = 11000f - gm.MaxDistance;
            foreach (var b in FindObjectsByType<EnemySpecialBehavior>(FindObjectsSortMode.None))
            {
                string ph = Phase(b.gameObject);
                last.TryGetValue(b, out string prev);
                if ((ph == "Telegraph" || ph == "Wake") && prev != ph)
                {
                    float half = cam.orthographicSize * cam.aspect;
                    bool on = b.transform.position.x < cam.transform.position.x + half && b.transform.position.x > cam.transform.position.x - half;
                    starts.Add((b.GetComponent<EnemyController>() != null ? b.kind.ToString() : "?", b.transform.position.x - pc.transform.position.x, on, pc.CurrentAutoRunSpeed));
                }
                last[b] = ph;
            }
            yield return JumpPits(0.05f); t += 0.05f;
        }
        PlayerController.DebugSpeedScale = 1f;
        EncounterDirector.DebugDistanceOffset = 0f;
        int off = starts.Count(s => !s.onScreen);
        foreach (var g in starts.GroupBy(s => s.id)) L($"  {g.Key}: {g.Count()} telegraphs, start distance avg {g.Average(s => s.dx):F1}m at {g.Average(s => s.v) * 3.6f:F0}km/h");
        Check(starts.Count > 0 && off == 0, $"every telegraph starts on screen at ~{PlayerController.Instance.runSpeed * 6f * 3.6f:F0}km/h+ ({starts.Count} telegraphs, off-screen {off})");
    }

    // ===================================================================== //
    // 5) ボス
    // ===================================================================== //
    IEnumerator TestBoss()
    {
        L("[Boss]");
        if (BossManager.Instance == null) { L("  (no BossManager)"); yield break; }
        BossManager.Instance.enabled = true;
        float next = BossManager.Instance.NextBossDistance;
        PlayerController.DebugSpeedScale = 3f;
        int during = 0, nearBoss = 0; bool phase = false; float phaseT = 0f; int from = dir.Recent.Count;
        float t = 0f;
        while (t < 140f)
        {
            yield return JumpPits(0.1f); t += 0.1f;
            bool bp = BossManager.Instance.IsBossPhase;
            if (bp && !phase) { phase = true; from = dir.Recent.Count; L($"  boss phase started at {gm.MaxDistance:F0}m"); }
            if (phase) { phaseT += 0.1f; if (dir.Recent.Count > from) during += dir.Recent.Count - from; from = dir.Recent.Count; if (phaseT > 8f) break; }
        }
        foreach (var r in dir.Recent) if (r.intensity != EncounterIntensity.Rest && next > 0f && r.distance > next - dir.Profile.bossPreBuffer - 40f + 0.5f && r.distance <= next + 5f) nearBoss++;
        PlayerController.DebugSpeedScale = 1f;
        Check(phase, $"reached the boss gate at {next:F0}m");
        Check(during == 0 && nearBoss == 0, $"no encounters during the boss fight ({during}) or right before it ({nearBoss})");
    }
}

public static class SkyEncounterTestMenu
{
    [MenuItem("Tools/OneMoreMile/Sky Encounter Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("SkyEncounterTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

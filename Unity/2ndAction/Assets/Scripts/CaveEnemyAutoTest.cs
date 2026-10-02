#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 自然洞窟雑魚敵追加(2026-09-22) - Editor専用の自動確認。
// 1) natural_caveへ5種すべてを個別に生成し、数秒動かして例外が出ないか確認。
// 2) Ground種(Cave Ant/Soldier Ant/Cave Hopper)へ上攻撃/通常/下攻撃相当の
//    ProcessHitを直接呼び、Launch/Juggle/Ground Slam/最終的な撃破が
//    既存EnemyControllerの仕組みどおりに動くか確認する。
// 3) Cave Hopperが常に地面の高さ付近にいる(空中停止しない)か、Cave Batが
//    天井より下にいるかをサンプリングして確認。
// 4) Burrow Wormが地中(非表示/Collider無効)→出現(表示/Collider有効)→
//    攻撃判定→退避、を一巡するか確認。
// 5) stageIds="natural_cave"の5種が、wasteland_roadのプール抽選には
//    一切出てこないことを確認(数百回抽選して1回も出ないか)。
// 結果は CaveEnemyAutoTest.txt。
public class CaveEnemyAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("CaveEnemyTest", 0) != 1) return;
        EditorPrefs.SetInt("CaveEnemyTest", 0);
        new GameObject("CaveEnemyTest").AddComponent<CaveEnemyAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[CaveEnemyTest] " + s); }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        var im = typeof(GameManager).GetProperty("InvincibleMode");
        if (im != null) im.GetSetMethod(true).Invoke(gm, new object[] { false });
        L($"stage={gm.ActiveRunStageId} character={gm.SelectedCharacterId} lives={gm.Lives}");
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        yield return new WaitForSeconds(0.5f);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        MethodInfo processHit = typeof(EnemyController).GetMethod("ProcessHit", BindingFlags.NonPublic | BindingFlags.Instance);

        yield return TestGroundSpecies("cave_ant", pc, gm, processHit, testAttackCycle: false);
        yield return TestGroundSpecies("soldier_ant", pc, gm, processHit, testAttackCycle: true);
        yield return TestGroundSpecies("cave_hopper", pc, gm, processHit, testAttackCycle: true);
        yield return TestCaveBat(pc, gm, processHit);
        yield return TestBurrowWorm(pc, gm, processHit);

        // ---- stageIds gating: wasteland_roadのプールに漏れていないか ----
        // GameManager.ActiveRunStageIdは「走行開始時に確定するRunの実ステージ」
        // (SelectedStageIdは次に選ばれているステージであり、Run中は変わらない)
        // なので、SetSelectedStageだけではRun中の抽選対象は変わらない。ここでは
        // 実際にActiveRunStageIdを読んでいるEnemyDatabase.StageAllowsと同じ経路を
        // 直接検証するため、Reflectionでこの実行中Runのactiveステージ値を一時的に
        // 書き換える(Runを再スタートせずに済む、テスト専用の直接確認)。
        FieldInfo activeStageField = typeof(GameManager).GetField("activeRunStageId", BindingFlags.NonPublic | BindingFlags.Instance);
        string originalActiveStage = activeStageField != null ? (string)activeStageField.GetValue(gm) : null;
        L($"[StageGate] ActiveRunStageId before override: {originalActiveStage}");
        if (activeStageField != null) activeStageField.SetValue(gm, "wasteland_road");

        int leaks = 0;
        string[] caveIds = { "cave_ant", "soldier_ant", "cave_hopper", "cave_bat", "burrow_worm" };
        var pool = new System.Collections.Generic.List<EnemyDefinition>(EnemyDatabase.AllEnemies);
        for (int i = 0; i < 400; i++)
        {
            EnemyDefinition picked = EnemyDatabase.PickRandomUnlocked(pool);
            if (picked != null) foreach (string id in caveIds) if (picked.enemyId == id) leaks++;
        }
        L($"[StageGate] wasteland_road picks (400 tries, ActiveRunStageId forced): cave-species leaks={leaks} (expect 0)");
        bool goblinOk = false;
        for (int i = 0; i < 50; i++)
        {
            EnemyDefinition picked = EnemyDatabase.PickRandomUnlockedOfCategory(pool, EnemyCategory.Normal);
            if (picked != null && (picked.enemyId == "goblin" || picked.enemyId == "goblin_elite")) { goblinOk = true; break; }
        }
        L($"[Regression] wasteland_road Normal-category pick still returns goblin/goblin_elite: {goblinOk}");

        if (activeStageField != null) activeStageField.SetValue(gm, originalActiveStage);
        int cavePicks = 0;
        for (int i = 0; i < 200; i++)
        {
            EnemyDefinition picked = EnemyDatabase.PickRandomUnlocked(pool);
            if (picked != null) foreach (string id in caveIds) if (picked.enemyId == id) cavePicks++;
        }
        L($"[StageGate] natural_cave picks (200 tries, restored): cave-species picked at least once={cavePicks > 0} (expect True)");

        Application.logMessageReceived -= handler;

        L("");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../CaveEnemyAutoTest.txt"), log.ToString());

        if (Application.isBatchMode) EditorApplication.Exit(anyException ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    GameObject SpawnByDefId(string id, PlayerController pc)
    {
        EnemyDefinition def = EnemyDatabase.FindById(id);
        if (def == null) { L($"[Spawn] {id}: EnemyDefinition NOT FOUND"); return null; }
        Vector3 p = pc.transform.position;
        float x = p.x + 4f;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        bool airborne = def.movementType == EnemyMovementType.Flying;
        float y = airborne ? p.y + 2f : (gy ?? p.y) + 0.55f;
        GameObject go = GroundFactory.CreateEnemy(null, def.sprite, new Vector2(x, y), def.tint,
            movementType: def.movementType, maxHp: 20, behaviorKind: def.behaviorKind,
            enableVisualFacing: def.enableVisualFacing, defaultFacingRight: def.defaultFacingRight,
            runFrames: def.runFrames, mileReward: def.mileReward, visualScaleMultiplier: def.visualScaleMultiplier,
            aiTier: def.aiTier, telegraphMarkerSprite: TerrainManager.Instance != null ? TerrainManager.Instance.squareSprite : null);
        go.GetComponent<EnemyController>().movementType = def.movementType;
        var special = go.GetComponent<EnemySpecialBehavior>();
        if (special != null) special.player = pc.transform;
        L($"[Spawn] {id}: ok pos=({x:F1},{y:F1}) behaviorKind={def.behaviorKind} category={def.category}");
        return go;
    }

    IEnumerator TestGroundSpecies(string id, PlayerController pc, GameManager gm, MethodInfo processHit, bool testAttackCycle)
    {
        GameObject go = SpawnByDefId(id, pc);
        if (go == null) yield break;
        var ec = go.GetComponent<EnemyController>();
        var special = go.GetComponent<EnemySpecialBehavior>();

        if (testAttackCycle)
        {
            float t = 0f; bool sawTelegraph = false, sawAttack = false;
            while (t < 8f && (!sawTelegraph || !sawAttack))
            {
                yield return null; t += Time.deltaTime;
                if (special != null)
                {
                    var stField = special.GetType().GetField(id == "cave_hopper" ? "hopperAttackState" : "meleeAttackState", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (stField != null)
                    {
                        string s = stField.GetValue(special).ToString();
                        if (s == "Telegraph") sawTelegraph = true;
                        if (s == "Attack") sawAttack = true;
                    }
                }
            }
            L($"[AttackCycle] {id}: sawTelegraph={sawTelegraph} sawAttack={sawAttack} (within 8s)");
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
            L($"[Idle] {id}: ran 1.5s with no attack cycle expected (T0) - alive={go.activeInHierarchy}");
        }

        if (id == "cave_hopper")
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            float sampleT = 0f;
            while (sampleT < 3f)
            {
                yield return null; sampleT += Time.deltaTime;
                float gy2 = TerrainManager.Instance.GetHeightAt(go.transform.position.x) ?? go.transform.position.y;
                float dy = go.transform.position.y - gy2;
                minY = Mathf.Min(minY, dy); maxY = Mathf.Max(maxY, dy);
            }
            L($"[Hopper] height-above-ground range over 3s: [{minY:F2},{maxY:F2}] (expect small, always finite/no drift)");
        }

        // ---- Launch (上攻撃相当) ----
        Vector3 hitPos = go.transform.position;
        processHit.Invoke(ec, new object[] { PlayerAttackKind.Up, hitPos, false });
        yield return null;
        L($"[Launch] {id}: AerialState={ec.AerialState} (expect Launched)");

        // ---- 空中追撃(通常相当、Juggle延長) ----
        processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, false });
        yield return null;
        L($"[AerialCombo] {id}: AerialState after juggle-extend hit={ec.AerialState} (expect still Launched)");

        // ---- 下攻撃でGround Slam ----
        processHit.Invoke(ec, new object[] { PlayerAttackKind.Down, hitPos, false });
        yield return null;
        L($"[GroundSlam] {id}: AerialState={ec.AerialState} (expect Slamming)");

        float wait = 0f;
        while (ec.AerialState != EnemyController.EnemyAerialState.Grounded && wait < 3f) { yield return null; wait += Time.deltaTime; }
        L($"[GroundSlam] {id}: landed after slam, AerialState={ec.AerialState} alive={go.activeInHierarchy} (expect Grounded, survived)");

        // ---- 被弾中にAI Stateが破綻しないか(Telegraph中に被弾させて確認) ----
        if (testAttackCycle && special != null)
        {
            var stField = special.GetType().GetField(id == "cave_hopper" ? "hopperAttackState" : "meleeAttackState", BindingFlags.NonPublic | BindingFlags.Instance);
            float t2 = 0f;
            while (t2 < 6f && stField != null && stField.GetValue(special).ToString() != "Telegraph") { yield return null; t2 += Time.deltaTime; }
            bool wasTelegraphing = stField != null && stField.GetValue(special).ToString() == "Telegraph";
            processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, false });
            // ノックバック+追従補正が終わってRestoreMotionComponentsが走るまで待つ
            // (即座に確認すると、まだ無効化中の一瞬を誤検知するため)。
            yield return new WaitForSeconds(2.0f);
            bool specialDisabledThenBack = special.enabled; // OnDisable/OnEnable should have run and left it enabled again
            L($"[InterruptAttack] {id}: wasTelegraphing={wasTelegraphing} specialEnabledAfterHit={specialDisabledThenBack} stateAfter={(stField != null ? stField.GetValue(special).ToString() : "?")} (expect Idle, not stuck)");
        }

        // ---- 最後に撃破(通常Hit連打で倒す) ----
        for (int i = 0; i < 25 && go.activeInHierarchy; i++)
        {
            processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, i == 24 });
            yield return null;
        }
        yield return new WaitForSeconds(1.2f);
        L($"[Death] {id}: activeInHierarchy={go.activeInHierarchy} (expect False = defeated cleanly)");
    }

    IEnumerator TestCaveBat(PlayerController pc, GameManager gm, MethodInfo processHit)
    {
        GameObject go = SpawnByDefId("cave_bat", pc);
        if (go == null) yield break;
        var ec = go.GetComponent<EnemyController>();

        float t = 0f; bool everAboveCeiling = false; float worstOver = 0f;
        while (t < 4f)
        {
            yield return null; t += Time.deltaTime;
            float? ceil = TerrainManager.Instance != null ? TerrainManager.Instance.GetEffectiveCeilingHeightAt(go.transform.position.x) : null;
            if (ceil.HasValue && go.transform.position.y > ceil.Value)
            {
                everAboveCeiling = true;
                worstOver = Mathf.Max(worstOver, go.transform.position.y - ceil.Value);
            }
        }
        L($"[CaveBat] stayed above ceiling at any sampled frame over 4s: {everAboveCeiling} (worstOver={worstOver:F2}, expect False)");

        Vector3 hitPos = go.transform.position;
        processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, false });
        yield return null;
        L($"[CaveBat] normal hit did not throw, still active={go.activeInHierarchy}");
        for (int i = 0; i < 25 && go.activeInHierarchy; i++)
        {
            processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, i == 24 });
            yield return null;
        }
        yield return new WaitForSeconds(1.2f);
        L($"[CaveBat] Death: activeInHierarchy={go.activeInHierarchy} (expect False)");
    }

    IEnumerator TestBurrowWorm(PlayerController pc, GameManager gm, MethodInfo processHit)
    {
        GameObject go = SpawnByDefId("burrow_worm", pc);
        if (go == null) yield break;
        var ec = go.GetComponent<EnemyController>();
        var col = go.GetComponent<BoxCollider2D>();
        var sr = go.GetComponentInChildren<SpriteRenderer>();

        // EnemySpecialBehavior.Start()(InitBurrowWormを含む)はUnityの
        // Startフェーズで実行される - GroundFactory.CreateEnemy直後の
        // 同フレーム同期チェックだとStart()実行前の値を読んでしまうため、
        // 最低1フレーム待ってから確認する。
        yield return null;
        L($"[BurrowWorm] initial (after 1 frame): colliderEnabled={col.enabled} visualEnabled={(sr != null ? sr.enabled.ToString() : "?")} (expect False,False - starts underground)");

        float t = 0f; bool sawEmerged = false;
        while (t < 8f && !sawEmerged)
        {
            yield return null; t += Time.deltaTime;
            if (col.enabled) sawEmerged = true;
        }
        L($"[BurrowWorm] emerged within 8s: {sawEmerged} (colliderEnabled={col.enabled} visualEnabled={(sr != null ? sr.enabled.ToString() : "?")})");

        if (sawEmerged)
        {
            Vector3 hitPos = go.transform.position;
            processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, hitPos, false });
            yield return null;
            L($"[BurrowWorm] hit while emerged did not throw, active={go.activeInHierarchy}");

            // 地中へ戻るところまで観察
            float t2 = 0f; bool sawRetreat = false;
            while (t2 < 6f && !sawRetreat)
            {
                yield return null; t2 += Time.deltaTime;
                if (!col.enabled) sawRetreat = true;
            }
            L($"[BurrowWorm] retreated underground again within 6s: {sawRetreat}");
        }

        if (go.activeInHierarchy)
        {
            for (int i = 0; i < 25 && go.activeInHierarchy; i++)
            {
                processHit.Invoke(ec, new object[] { PlayerAttackKind.Normal, go.transform.position, i == 24 });
                yield return null;
            }
            yield return new WaitForSeconds(1.2f);
        }
        L($"[BurrowWorm] Death: activeInHierarchy={go.activeInHierarchy} (expect False)");
    }
}

public static class CaveEnemyTestMenu
{
    [MenuItem("Tools/OneMoreMile/Cave Enemy Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("CaveEnemyTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

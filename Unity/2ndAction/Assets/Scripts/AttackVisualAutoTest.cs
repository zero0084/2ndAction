#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 攻撃エフェクト/攻撃アニメーション本番素材化(2026-09-26) - Editor専用の自動確認。
// 1) 雑魚敵: 兵隊アリ(近接)/洞窟ホッパー(近接)/弓兵(射撃)/地中ワームで、予備動作〜攻撃中に
//    攻撃ポーズ(EnemyDefinition.attackSprite)が表示されるか、予告マークが警告アイコン素材か、
//    近接攻撃で斬撃エフェクトが出るか、弓兵が回転しない矢(arrow素材)を撃つかを確認。
// 2) ドラゴン/魔人の火球(単色四角を渡す既存呼び出し)が火球素材に置き換わるか。
// 3) 荒野街道ボスの攻撃ポーズ/自然洞窟ボスの予備動作ポーズ/天空回廊エフェクト素材が配線されているか、
//    自然洞窟ボスがプレイヤー側を向く設定(artFacesLeft=false)になっているか。
// 結果は AttackVisualAutoTest.txt。
public class AttackVisualAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("AttackVisualTest", 0) != 1) return;
        EditorPrefs.SetInt("AttackVisualTest", 0);
        new GameObject("AttackVisualTest").AddComponent<AttackVisualAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[AttackVisualTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("[FAIL] " + what); } }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        yield return new WaitForSeconds(0.5f);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;

        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        // ---- 素材の有無 ----
        foreach (string n in new[] { "warning", "arrow", "fireball", "skybolt", "titanfist", "flamefeather", "cloudpuff", "thunderspear", "muzzleflash", "lancethrust" })
        {
            bool ok = Resources.Load<Sprite>("Effects/" + n) != null;
            L($"[EffectArt] {n}: {(ok ? "ok" : "MISSING")}");
            Check(ok, "effect art " + n);
        }
        var bm = BossManager.Instance;
        int wildAttack = 0, caveWindup = 0;
        foreach (var a in bm.wildArt) if (a != null && a.attack != null) wildAttack++;
        foreach (var a in bm.caveArt) if (a != null && a.windup != null) caveWindup++;
        L($"[BossArt] wild attack poses={wildAttack}/10 cave windup poses={caveWindup}/11");
        Check(wildAttack == 10, "wasteland attack poses");
        Check(caveWindup == 11, "cave windup poses");

        // ---- 火球(単色四角→火球素材) ----
        var fb = FireballController.Create(TerrainManager.Instance.squareSprite, pc.transform.position + new Vector3(30f, 5f, 0f), Vector2.zero);
        var fbSprite = fb.GetComponent<SpriteRenderer>().sprite;
        L($"[Fireball] sprite={(fbSprite != null ? fbSprite.name : "null")}");
        Check(fbSprite != null && fbSprite.name == "fireball", "fireball art replaces square");
        Destroy(fb);

        // ---- 雑魚敵の攻撃ポーズ/予告/斬撃/矢 ----
        yield return TestEnemy("soldier_ant", pc, 12f);
        yield return TestEnemy("cave_hopper", pc, 14f);
        yield return TestEnemy("shooter_archer", pc, 8f);
        yield return TestEnemy("burrow_worm", pc, 12f);

        // ---- 弾速の走行補正: 高速走行(x4)でも自分の弾・跳ね返した火球を追い越さず、敵弾は設計速度で迫る ----
        PlayerController.DebugSpeedScale = 4f;
        yield return new WaitForSeconds(0.2f);
        float run = pc.CurrentAutoRunSpeed;
        Vector3 basePos = pc.transform.position + new Vector3(0f, 6f, 0f); // プレイヤーに当たらない高さ
        var bullet = PlayerBullet.Create(null, basePos + new Vector3(1f, 0f, 0f), new Vector2(15f, 0f), 3f);
        var reflected = FireballController.Create(TerrainManager.Instance.squareSprite, basePos + new Vector3(1f, 1f, 0f), new Vector2(18f, 0f));
        reflected.GetComponent<FireballController>().reflected = true;
        var incoming = FireballController.Create(TerrainManager.Instance.squareSprite, basePos + new Vector3(8f, 2f, 0f), new Vector2(-9f, 0f));
        float px0 = pc.transform.position.x;
        yield return new WaitForSeconds(0.5f);
        float dpx = pc.transform.position.x - px0;
        float bulletGap = bullet != null ? bullet.transform.position.x - pc.transform.position.x : -99f;
        float reflGap = reflected != null ? reflected.transform.position.x - pc.transform.position.x : -99f;
        float inGap = incoming != null ? incoming.transform.position.x - pc.transform.position.x : -99f;
        L($"[RunFrame] run={run:F1} playerMoved={dpx:F1} bulletGap={bulletGap:F1} (expect ~8.5) reflectedGap={reflGap:F1} (expect ~10) incomingGap={inGap:F1} (expect ~3.5)");
        Check(bulletGap > 6f, "player bullet stays ahead at x4 speed");
        Check(reflGap > 7f, "reflected fireball stays ahead at x4 speed");
        Check(inGap > 2f && inGap < 5f, "incoming fireball closes at its own speed");
        PlayerController.DebugSpeedScale = 1f;
        if (bullet != null) Destroy(bullet);
        if (reflected != null) Destroy(reflected);
        if (incoming != null) Destroy(incoming);

        // ---- 洞窟ボスの向き ----
        var spawnCave = typeof(BossManager).GetMethod("DebugForceSpawnCave", BindingFlags.NonPublic | BindingFlags.Instance);
        bm.enabled = true;
        spawnCave.Invoke(bm, new object[] { CaveBossKind.Troll, 1 });
        yield return new WaitForSeconds(0.3f);
        var troll = FindFirstObjectByType<WildBossBase>();
        L($"[CaveBossFacing] artFacesLeft={(troll != null ? troll.artFacesLeft.ToString() : "-")} (expect False)");
        Check(troll != null && !troll.artFacesLeft, "cave boss art faces right");
        if (troll != null) troll.TakeDamage(99999, troll.CenterWorld);

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../AttackVisualAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    IEnumerator TestEnemy(string id, PlayerController pc, float observe)
    {
        EnemyDefinition def = EnemyDatabase.FindById(id);
        if (def == null) { Check(false, id + " definition"); yield break; }
        Vector3 p = pc.transform.position;
        float x = p.x + (id == "shooter_archer" ? 7f : 2.2f);
        float? gy = TerrainManager.Instance.GetHeightAt(x);
        GameObject go = GroundFactory.CreateEnemy(null, def.sprite, new Vector2(x, (gy ?? p.y) + 0.55f), def.tint,
            movementType: def.movementType, maxHp: 999, behaviorKind: def.behaviorKind,
            projectileSprite: TerrainManager.Instance.squareSprite,
            enableVisualFacing: def.enableVisualFacing, defaultFacingRight: def.defaultFacingRight,
            runFrames: def.runFrames, mileReward: def.mileReward, visualScaleMultiplier: def.visualScaleMultiplier,
            aiTier: def.aiTier, telegraphMarkerSprite: TerrainManager.Instance.squareSprite);
        GroundFactory.ApplyAttackSprite(go, def);
        var special = go.GetComponent<EnemySpecialBehavior>();
        if (special != null) special.player = pc.transform;
        var anim = go.GetComponent<EnemyAnimator>();
        SpriteRenderer vis = anim != null && anim.visual != null ? anim.visual.GetComponent<SpriteRenderer>() : null;

        bool sawPose = false, sawWarning = false, sawSlash = false, sawArrow = false;
        float t = 0f;
        while (t < observe && go != null)
        {
            t += Time.deltaTime;
            // 敵がプレイヤーの前方に留まるよう、プレイヤーの基本速度ぶん一緒に流す(テスト専用)
            go.transform.position += new Vector3(pc.CurrentAutoRunSpeed * Time.deltaTime, 0f, 0f);
            if (vis != null && def.attackSprite != null && vis.sprite == def.attackSprite) sawPose = true;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
                if (sr.gameObject.activeInHierarchy && sr.sprite != null && sr.sprite.name == "warning") sawWarning = true;
            foreach (var fx in FindObjectsByType<OneShotSpriteEffect>(FindObjectsSortMode.None))
            {
                var fsr = fx.GetComponent<SpriteRenderer>();
                if (fsr != null && fsr.sprite == BossFx.Slash()) sawSlash = true;
            }
            foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None))
            {
                var fsr = f.GetComponent<SpriteRenderer>();
                if (fsr != null && fsr.sprite != null && fsr.sprite.name == "arrow" && f.spinSpeed == 0f) sawArrow = true;
            }
            yield return null;
        }
        L($"[Enemy] {id}: attackSprite={(def.attackSprite != null)} pose={sawPose} warningMarker={sawWarning} slash={sawSlash} arrow={sawArrow}");
        Check(def.attackSprite != null, id + " has attack sprite");
        Check(sawPose, id + " shows attack pose");
        if (def.behaviorKind == EnemyBehaviorKind.StationaryMelee || def.behaviorKind == EnemyBehaviorKind.CaveHopper)
        {
            Check(sawWarning, id + " warning marker art");
            Check(sawSlash, id + " melee slash effect");
        }
        if (def.behaviorKind == EnemyBehaviorKind.Shooter) Check(sawArrow, id + " fires arrow art");
        if (go != null) Destroy(go);
        foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None)) Destroy(f.gameObject);
        yield return new WaitForSeconds(0.3f);
    }
}

public static class AttackVisualTestMenu
{
    [MenuItem("Tools/OneMoreMile/Attack Visual Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("AttackVisualTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

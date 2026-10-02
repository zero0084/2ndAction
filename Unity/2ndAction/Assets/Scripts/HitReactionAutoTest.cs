#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor専用: 被弾リアクション(Hurt/Recovery/無敵/入力ブロック/死亡)の自動確認。結果は HitReactionAutoTest.txt。
public class HitReactionAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("HitReactTest", 0) != 1) return;
        EditorPrefs.SetInt("HitReactTest", 0);
        new GameObject("HitReactTest").AddComponent<HitReactionAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[HitReact] " + s); }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        string ch = EditorPrefs.GetString("HitReactChar", "swordsman");
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        var im = typeof(GameManager).GetProperty("InvincibleMode"); if (im != null) im.GetSetMethod(true).Invoke(gm, new object[] { false });
        L($"character={gm.SelectedCharacterId} lives={gm.Lives}");
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        TerrainManager.Instance.enemySpawnChance = 0f;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        var fire = typeof(PlayerController).GetMethod("FireJump", BindingFlags.NonPublic | BindingFlags.Instance);
        var anim = pc.GetComponent<PlayerAnimator>();
        var srs = pc.GetComponentInChildren<SpriteRenderer>();
        yield return new WaitForSeconds(1.0f);

        // ---- 通常被弾(Hurt中は入力(上フリック=ジャンプ/前フリック=攻撃)を毎フレーム注入して、受け付けないことを確認)
        gm.DebugSetLives(9);
        int lives0 = gm.Lives; float x0 = pc.transform.position.x;
        pc.TakeDamage();
        L($"[HURT] immediately: reaction={pc.Reaction} lives {lives0}->{gm.Lives} invincible={pc.IsHitInvincible}");
        float t = 0f, minX = x0, firstResume = -1f, firstFlick = -1f, invEnd = -1f; int flickToggles = 0; bool lastEn = true; int jumpsDuring = 0; bool attackedDuring = false;
        float tIgnoreHurt = -1f; int livesAtHurt = -1; bool testedHurtHit = false, testedInvHit = false; int livesAtInv = -1; bool acceptedAfter = false; int livesBeforeAfter = -1;
        var frames = new System.Collections.Generic.List<string>();
        float nextSample = 0f;
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        while (t < 1.4f)
        {
            yield return null; t += Time.deltaTime;
            float x = pc.transform.position.x; minX = Mathf.Min(minX, x);
            if (pc.IsReacting) { if (!pc.IsGrounded) jumpsDuring++; if (pc.IsAttacking) attackedDuring = true; }
            if (!pc.IsReacting && firstResume < 0f) { firstResume = t; pc.debugInjectFlick = null; }
            if (srs.enabled != lastEn) { flickToggles++; lastEn = srs.enabled; if (firstFlick < 0f) firstFlick = t; }
            if (invEnd < 0f && !pc.IsHitInvincible) invEnd = t;
            if (!testedHurtHit && t > 0.10f) { testedHurtHit = true; livesAtHurt = gm.Lives; pc.TakeDamage(); L($"[INV] hit during Hurt (t={t:F2}) ignored: lives {livesAtHurt}->{gm.Lives}"); }
            if (!testedInvHit && t > 0.55f) { testedInvHit = true; livesAtInv = gm.Lives; pc.TakeDamage(); L($"[INV] hit during post-Hurt invincible (t={t:F2}) ignored: lives {livesAtInv}->{gm.Lives}"); }
            if (!acceptedAfter && invEnd > 0f && t > invEnd + 0.05f) { acceptedAfter = true; livesBeforeAfter = gm.Lives; pc.TakeDamage(); L($"[INV] hit after invincibility ended (t={t:F2}) accepted: lives {livesBeforeAfter}->{gm.Lives}"); }
            if (t >= nextSample && frames.Count < 10) { nextSample += 0.1f; frames.Add($"t={t:F2} x={x:F2} react={pc.Reaction} inv={pc.IsHitInvincible} sprVisible={srs.enabled} rotZ={srs.transform.localEulerAngles.z:F0} scaleY={srs.transform.localScale.y:F2}"); }
        }
        pc.debugInjectFlick = null;
        L($"[HURT] reaction lasted ~{firstResume:F2}s; backward push {x0 - minX:F2} units; jumps started during Hurt={jumpsDuring}; attack started during Hurt={attackedDuring}; first flicker at t={firstFlick:F2}; flicker toggles={flickToggles}; invincibility total ~{invEnd:F2}s");
        foreach (var f in frames) L("   " + f);

        // ---- 攻撃中に被弾 → 攻撃キャンセル → Hurt → Runを経由
        yield return new WaitForSeconds(1.5f);
        gm.DebugSetLives(9);
        var doAtk = typeof(PlayerController).GetMethod("DoAttack", BindingFlags.NonPublic | BindingFlags.Instance);
        pc.StartCoroutine((IEnumerator)doAtk.Invoke(pc, new object[] { System.Enum.Parse(doAtk.GetParameters()[0].ParameterType, "Forward") }));
        yield return new WaitForSeconds(0.05f);
        bool atk0 = pc.IsAttacking; pc.TakeDamage(); yield return null;
        L($"[ATTACK] attacking before hit={atk0}; right after hit isAttacking={pc.IsAttacking} reaction={pc.Reaction}");
        yield return new WaitForSeconds(0.6f);
        L($"[ATTACK] 0.6s later isAttacking={pc.IsAttacking} (not resumed) reaction={pc.Reaction}");

        // ---- 落下復帰
        yield return new WaitForSeconds(1.5f);
        gm.DebugSetLives(9);
        int recDbg = 0; int dbgJump = 0;
        int livesF = gm.Lives;
        pc.TakeDamage(true);
        L($"[FALL] reaction={pc.Reaction} lives {livesF}->{gm.Lives} inv={pc.IsHitInvincible}");
        t = 0f; float recoverEnd = -1f; float xStart = pc.transform.position.x; float maxDx = 0f; float invEnd2 = -1f; bool recFlick = false;
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        while (t < 2.0f)
        {
            yield return null; t += Time.deltaTime;
            if (recoverEnd < 0f && !pc.IsReacting) { recoverEnd = t; pc.debugInjectFlick = null; }
            if (invEnd2 < 0f && !pc.IsHitInvincible) invEnd2 = t;
            if (pc.IsRecovering && recDbg++ < 6) L($"   rec dbg t={t:F3} dt={Time.deltaTime:F3} x={pc.transform.position.x:F2} y={pc.transform.position.y:F2} grounded={pc.IsGrounded} kb=?");
            if (pc.IsRecovering) { if (Mathf.Abs(pc.transform.position.x - xStart) > 0.5f && dbgJump++ < 3) L($"   JUMP t={t:F3} dt={Time.deltaTime:F3} x={pc.transform.position.x:F2} from {xStart:F2} timer? react={pc.Reaction} prog={pc.ReactionProgress:F2}");
            maxDx = Mathf.Max(maxDx, Mathf.Abs(pc.transform.position.x - xStart)); if (!srs.enabled) recFlick = true; }
        }
        pc.debugInjectFlick = null;
        L($"[FALL] Recovery lasted ~{recoverEnd:F2}s; max x drift while recovering={maxDx:F3}; flicker during Recovery={recFlick}; invincibility ended at t={invEnd2:F2}s (Recovery+1.0s)");

        // ---- 死亡(LIFE0)
        yield return new WaitForSeconds(1.2f);
        L($"[DEATH] pre: inv={pc.IsHitInvincible} reacting={pc.IsReacting} timeScale={Time.timeScale} lives={gm.Lives}");
        gm.DebugSetLives(1);
        pc.TakeDamage(); yield return null; yield return null;
        L($"[DEATH] lives={gm.Lives} gameOver={gm.IsGameOver} reaction={pc.Reaction} (Hurt not started)");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../HitReactionAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class HitReactionTestMenu
{
    public static void RunSwordsman() { Run("swordsman"); }
    public static void RunNoble() { Run("noble_lady"); }
    public static void RunDual() { Run("dual_blade"); }
    static void Run(string id)
    {
        EditorPrefs.SetInt("HitReactTest", 1); EditorPrefs.SetString("HitReactChar", id);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

#if UNITY_EDITOR
// 目視確認用: 被弾/落下復帰の連続フレームをPNGで保存する(HitReactionShots/<char>_*.png)。
public class HitReactionShots : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("HitReactShots", 0) != 1) return;
        EditorPrefs.SetInt("HitReactShots", 0);
        new GameObject("HitReactShots").AddComponent<HitReactionShots>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        string ch = EditorPrefs.GetString("HitReactChar", "swordsman");
        gm.SetSelectedCharacter(ch); gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        var im = typeof(GameManager).GetProperty("InvincibleMode"); if (im != null) im.GetSetMethod(true).Invoke(gm, new object[] { false });
        gm.DebugSetLives(9);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        TerrainManager.Instance.enemySpawnChance = 0f;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return new WaitForSeconds(1.0f);
        var cf = FindFirstObjectByType<CameraFollow>(); cf.enabled = false;
        Camera cam = cf.GetComponent<Camera>();
        string dir = System.IO.Path.Combine(Application.dataPath, "../HitReactionShots"); System.IO.Directory.CreateDirectory(dir);
        var rt = new RenderTexture(480, 420, 24);
        int n = 0;
        IEnumerator Shot(string label)
        {
            cam.orthographicSize = 2.6f;
            Vector3 p = pc.transform.position; cam.transform.position = new Vector3(p.x + 1.2f, p.y + 1.0f, cam.transform.position.z);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(480, 420, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 480, 420), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{ch}_{n++:00}_{label}.png"), tex.EncodeToPNG());
            Destroy(tex); yield return null;
        }
        // 被弾: 前 → 直後 → Hurt → Run復帰 → 点滅
        yield return Shot("run_before");
        pc.TakeDamage();
        float t = 0f; float[] marks = { 0.02f, 0.07f, 0.13f, 0.20f, 0.27f, 0.40f, 0.50f, 0.60f };
        int mi = 0;
        while (mi < marks.Length)
        {
            yield return null; t += Time.deltaTime;
            if (t >= marks[mi]) { yield return Shot($"hurt_{marks[mi]:F2}"); mi++; }
        }
        // 落下復帰
        yield return new WaitForSeconds(1.5f);
        pc.TakeDamage(true);
        t = 0f; float[] fm = { 0.03f, 0.15f, 0.30f, 0.42f, 0.55f, 0.90f }; mi = 0;
        while (mi < fm.Length)
        {
            yield return null; t += Time.deltaTime;
            if (t >= fm[mi]) { yield return Shot($"recovery_{fm[mi]:F2}"); mi++; }
        }
        if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.isPlaying = false;
    }
}

public static class HitReactionShotsMenu
{
    public static void Run(string id)
    {
        EditorPrefs.SetInt("HitReactShots", 1); EditorPrefs.SetString("HitReactChar", id);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
    public static void Swordsman() { Run("swordsman"); }
    public static void Noble() { Run("noble_lady"); }
    public static void Dual() { Run("dual_blade"); }
}
#endif

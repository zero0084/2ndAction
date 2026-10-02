#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 竜騎士の不具合調査/回帰確認(2026-09-26) - Editor専用。結果は LancerStressAutoTest.txt。
// ランダムな入力(上/前/後/下/無入力)を長時間流し続け、毎フレーム
//  ・キャラの絵が見えているか(SpriteRenderer.enabled / sprite / alpha / Visualの大きさ / 画面内)
//  ・攻撃状態が残り続けていないか(isAttacking / LanceMove / 突進速度 / 攻撃判定)
// を監視する。-lancerStressSeconds N で時間、-lancerStressStage id でステージを変えられる。
public class LancerStressAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("LancerStressTest", 0) != 1) return;
        EditorPrefs.SetInt("LancerStressTest", 0);
        new GameObject("LancerStressTest").AddComponent<LancerStressAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[LancerStress] " + s); }
    static FieldInfo F(string name) => typeof(PlayerController).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        string stage = "wasteland_road";
        float seconds = 40f;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-lancerStressSeconds") float.TryParse(args[i + 1], out seconds);
            if (args[i] == "-lancerStressStage") stage = args[i + 1];
        }
        string character = "dragon_lancer";
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lancerStressCharacter") character = args[i + 1];
        gm.SetSelectedCharacter(character);
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond + " " + trace.Split('\n')[0]); }
        };
        Application.logMessageReceived += handler;

        var anim = pc.GetComponentInChildren<PlayerAnimator>();
        var sr = (SpriteRenderer)typeof(PlayerAnimator).GetField("sr", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).GetValue(anim);
        var isAttackingF = F("isAttacking");
        var lungeF = F("lungeVelocityX");
        var hitInvF = F("hitInvincibleTimer");
        var stateF = typeof(PlayerAnimator).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
        var cam = Camera.main;

        // カウントダウン中も監視(Start→Runの切り替わりを含める)
        float t0 = Time.realtimeSinceStartup;
        var rng = new System.Random(20260926);
        float nextInput = 0f, elapsed = 0f;
        int frames = 0, invisibleFrames = 0, invisibleRuns = 0, curInvisible = 0;
        float attackingFor = 0f, maxAttackingFor = 0f, lanceMoveFor = 0f, maxLanceMoveFor = 0f;
        int stuckReports = 0;
        string lastInput = "-";
        string lastAttackSig = "";
        string history = "";
        L($"stage={stage} character={character} seconds={seconds} sortingLayer={sr.sortingLayerName} order={sr.sortingOrder}");
        gm.DebugSetInvincible(true);

        float realStart = Time.realtimeSinceStartup;
        while (elapsed < seconds && Time.realtimeSinceStartup - realStart < seconds * 3f + 30f)
        {
            if (gm.IsGameOver) { L($"[GAMEOVER] t={elapsed:F2} (test ends early)"); failures++; break; }
            // レベルアップのカード選択で止まらないよう自動で選ぶ
            if (gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.15f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.3f);
                continue;
            }
            if (Time.timeScale == 0f) { yield return null; continue; }
            float dt = Time.deltaTime;
            elapsed += dt;
            frames++;
            bool running = gm.HasStarted && !gm.CountdownActive;

            // ---- 入力(カウントダウン後) ----
            pc.debugInjectFlick = null;
            // 穴の手前ではジャンプして越える(落下死でテストが終わらないように)
            var tm = TerrainManager.Instance;
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            if (running && pc.IsGrounded && tm != null && tm.IsNearPit(pc.transform.position.x + lead, 0.4f))
            {
                pc.debugInjectFlick = PlayerController.FlickDirection.Up;
                lastInput = "Up(pit)";
            }
            else if (running && elapsed >= nextInput)
            {
                int r = rng.Next(100);
                PlayerController.FlickDirection? f = r < 30 ? PlayerController.FlickDirection.Up
                    : r < 60 ? PlayerController.FlickDirection.Forward
                    : r < 72 ? PlayerController.FlickDirection.Backward
                    : r < 88 ? PlayerController.FlickDirection.Down : (PlayerController.FlickDirection?)null;
                pc.debugInjectFlick = f;
                lastInput = f.HasValue ? f.Value.ToString() : "none";
                history = (history + " " + lastInput.Substring(0, 1)).Length > 40 ? (history + " " + lastInput.Substring(0, 1)).Substring(2) : history + " " + lastInput.Substring(0, 1);
                nextInput = elapsed + 0.08f + (float)rng.NextDouble() * 0.35f;
            }

            yield return null; // 入力が処理された後のフレームを見る

            // ---- 絵が見えているか ----
            object st = stateF.GetValue(anim);
            bool flicker = (float)hitInvF.GetValue(pc) > 0f; // 被弾後の無敵点滅(意図的)
            Vector3 ls = sr.transform.lossyScale;
            Vector3 vp = cam != null ? cam.WorldToViewportPoint(sr.bounds.center) : new Vector3(0.5f, 0.5f, 1f);
            string why = null;
            if (!sr.enabled && !flicker && !pc.IsDeadPosing) why = "sr.enabled=false";
            else if (sr.sprite == null) why = "sprite=null";
            else if (sr.color.a < 0.5f) why = $"alpha={sr.color.a:F2}";
            else if (Mathf.Abs(ls.x) < 0.2f || Mathf.Abs(ls.y) < 0.2f) why = $"scale={ls.x:F2},{ls.y:F2}";
            else if (vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.1f || vp.y > 1.1f) why = $"offscreen vp=({vp.x:F2},{vp.y:F2})";
            if (why != null)
            {
                invisibleFrames++;
                if (curInvisible == 0) { invisibleRuns++; L($"[INVISIBLE] t={elapsed:F2} {why} state={st} sprite={(sr.sprite != null ? sr.sprite.name : "null")} grounded={pc.IsGrounded} lance={pc.LanceMove} attacking={isAttackingF.GetValue(pc)} input={lastInput} recent=[{history}]"); }
                curInvisible++;
            }
            else if (curInvisible > 0) { L($"  visible again after {curInvisible} frames"); curInvisible = 0; }

            // ---- 攻撃状態が残り続けていないか ----
            bool attacking = (bool)isAttackingF.GetValue(pc);
            // 連打で技が次々に続いている(=段や構え/突きが変わり続けている)のは正常。同じ状態のまま
            // 動かなくなった時だけ「残り続けている」とみなす。
            string attackSig = $"{attacking}/{pc.LanceMove}/{pc.LanceComboStage}/{pc.LanceFrameIndex}/{pc.IsGrounded}";
            if (attackSig != lastAttackSig) { lastAttackSig = attackSig; attackingFor = 0f; lanceMoveFor = 0f; }
            attackingFor = attacking ? attackingFor + dt : 0f;
            maxAttackingFor = Mathf.Max(maxAttackingFor, attackingFor);
            lanceMoveFor = pc.LanceMove != PlayerController.LanceMoveKind.None ? lanceMoveFor + dt : 0f;
            maxLanceMoveFor = Mathf.Max(maxLanceMoveFor, lanceMoveFor);
            if ((attackingFor > 1.5f || lanceMoveFor > 1.5f) && stuckReports < 5)
            {
                stuckReports++;
                L($"[STUCK] t={elapsed:F2} attacking={attacking} for {attackingFor:F2}s lance={pc.LanceMove} for {lanceMoveFor:F2}s lunge={(float)lungeF.GetValue(pc):F2} hitbox={(pc.LanceHitbox != null && pc.LanceHitbox.enabled)} grounded={pc.IsGrounded} state={st} recent=[{history}]");
                attackingFor = lanceMoveFor = 0f;
            }
        }
        pc.debugInjectFlick = null;
        Application.logMessageReceived -= handler;

        if (invisibleRuns > 0) failures++;
        if (stuckReports > 0) failures++;
        if (anyException) failures++;
        L($"frames={frames} invisibleFrames={invisibleFrames} invisibleRuns={invisibleRuns} maxAttacking={maxAttackingFor:F2}s maxLanceMove={maxLanceMoveFor:F2}s stuckReports={stuckReports}");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../LancerStressAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}

public static class LancerStressTestMenu
{
    [MenuItem("Tools/OneMoreMile/Lancer Stress Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("LancerStressTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

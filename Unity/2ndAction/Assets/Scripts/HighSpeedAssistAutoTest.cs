#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 高速時の自動操作補助(2026-09-28)のEditor専用自動確認。結果は HighSpeedAssistAutoTest_<stage>.txt。
// ボット無し(入力は補助だけ)で、通常速度/200km/h付近/さらに高速 を補助ON/OFFで走り、被弾の内訳・自動行動・
// 「安全な行動なし」・判断の処理時間・時間倍率(自動スローが無いこと)を記録する。加えて、
// 発動/解除のヒステリシス、手動操作との競合、停止(TimeControl.Pause)/カード選択からの復帰を確認する。
// 起動: Tools/OneMoreMile/High Speed Assist Test (batch)  コマンドライン -hsaStage <id> -hsaChar <id>
public class HighSpeedAssistAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("HighSpeedAssistTest", 0) != 1) return;
        EditorPrefs.SetInt("HighSpeedAssistTest", 0);
        new GameObject("HighSpeedAssistTest").AddComponent<HighSpeedAssistAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[HSATest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    string stage = "wasteland_road", character = "swordsman";
    GameManager gm; PlayerController pc; HighSpeedAssist assist;
    float targetKmh; // 0=自然な速度
    System.Func<float, float> speedCurve;
    float segTime;

    // 集計
    int dmgFall, dmgObstacle, dmgEnemy, dmgOther, respawns;
    readonly List<string> noSafeSamples = new List<string>();
    readonly List<string> obstacleSamples = new List<string>();
    int slowFrames, framesTotal;
    int engageToggles; bool lastEngaged;
    int manualConflicts, manualFrames;
    float lastManualUp = -99f, lastManualAttack = -99f;
    int lastManualCount;
    bool injectManual;
    float manualTimer;
    int manualPhase;

    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    void OnTap(string m)
    {
        if (m.StartsWith("[Damage] Hit reason="))
        {
            string r = m.Substring("[Damage] Hit reason=".Length);
            int sp = r.IndexOf(' '); if (sp > 0) r = r.Substring(0, sp);
            if (r.StartsWith("DeathY")) dmgFall++;
            else if (r.Contains("Obstacle"))
            {
                dmgObstacle++;
                if (obstacleSamples.Count < 10 && assist != null && pc != null)
                    obstacleSamples.Add($"t={Time.time:F1} kmh={assist.JudgedKmh:F0} grounded={pc.IsGrounded} y={pc.transform.position.y:F2} vy={pc.AssistVelocityY:F1} jumpsUsed={pc.AssistJumpsUsed} reacting={pc.IsReacting} last={assist.LastAction}@{Time.time - assist.LastActionTime:F2}s plan=[{assist.PlanText}] fail=[{assist.LastFailure}@{Time.time - assist.LastFailureTime:F2}s] status={assist.CurrentStatus} {r}");
            }
            else if (r.Contains("Enemy") || r.Contains("Boss") || r.Contains("Fire") || r.Contains("Wolf") || r.Contains(":")) dmgEnemy++;
            else dmgOther++;
        }
        else if (m.StartsWith("[Respawn]")) respawns++;
        else if (m.StartsWith("[Assist] NO SAFE ACTION") && noSafeSamples.Count < 6) noSafeSamples.Add(m.Substring(24));
    }

    IEnumerator Start()
    {
        stage = Arg("-hsaStage", EditorPrefs.GetString("HighSpeedAssistTestStage", stage));
        character = Arg("-hsaChar", character);
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance; pc = PlayerController.Instance; assist = HighSpeedAssist.Instance;
        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + cond + " " + trace.Split('\n')[0]); } };
        Application.logMessageReceived += handler;
        FreezeDiagnostics.EventTap += OnTap;

        gm.SetSelectedCharacter(character);
        gm.SetSelectedStage(stage);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        gm.DebugSetInvincible(false); // 無敵OFF・HP999(ゲームオーバーにはならない)
        if (BossManager.Instance != null) BossManager.Instance.enabled = false; // ボス演出の時間倍率を計測から外す
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        L($"stage={stage} character={character} engage={assist.engageKmh} release={assist.releaseKmh} lookAhead={assist.lookAheadSeconds}s step={assist.simStepMeters}m suppress(atk/jump)={assist.manualAttackSuppress}/{assist.manualJumpSuppress}s");
        StartCoroutine(SpeedController());
        StartCoroutine(FrameMonitor());

        // ---- 1) 通常速度: 補助は待機のまま ----
        yield return Segment("natural", 0f, true, 10f);
        Check(lastSeg.actions == 0 && assist.CurrentStatus == HighSpeedAssist.Status.WaitingSpeed, $"natural speed ({lastSeg.kmh:F0}km/h): assist waits, no auto actions (actions={lastSeg.actions} status={assist.CurrentStatus})");

        // ---- 2) 200km/h付近 / さらに高速: 補助ON/OFFの比較 ----
        var on205 = default(Seg); var off205 = default(Seg);
        yield return Segment("205kmh ON", 205f, true, 25f); on205 = lastSeg;
        yield return Segment("205kmh OFF", 205f, false, 25f); off205 = lastSeg;
        yield return Segment("300kmh ON", 300f, true, 25f); var on300 = lastSeg;
        yield return Segment("300kmh OFF", 300f, false, 25f); var off300 = lastSeg;
        yield return Segment("420kmh ON", 420f, true, 20f); var on420 = lastSeg;
        yield return Segment("420kmh OFF", 420f, false, 20f); var off420 = lastSeg;
        Check(on205.actions > 0 && on300.actions > 0, "assist acts at 205/300km/h");
        Check(on420.fall + on420.obstacle < off420.fall + off420.obstacle || off420.fall + off420.obstacle == 0, $"420km/h terrain damage ON({on420.fall + on420.obstacle}) < OFF({off420.fall + off420.obstacle})");
        Check(on205.fall + on205.obstacle < off205.fall + off205.obstacle || off205.fall + off205.obstacle == 0, $"205km/h terrain damage ON({on205.fall + on205.obstacle}) < OFF({off205.fall + off205.obstacle})");
        Check(on300.fall + on300.obstacle < off300.fall + off300.obstacle || off300.fall + off300.obstacle == 0, $"300km/h terrain damage ON({on300.fall + on300.obstacle}) < OFF({off300.fall + off300.obstacle})");
        Check(on205.kills >= off205.kills, $"205km/h kills ON({on205.kills}) >= OFF({off205.kills})");

        // ---- 3) ヒステリシス ----
        assist.SetEnabled(true);
        speedCurve = t => 200f + 14f * Mathf.Sin(t * Mathf.PI * 0.5f); // 186〜214: 一度発動したら解除されない
        engageToggles = 0; lastEngaged = assist.Engaged;
        yield return Segment("hysteresis 186-214", -1f, true, 12f);
        int togglesA = engageToggles;
        speedCurve = t => 185f + 10f * Mathf.Sin(t * Mathf.PI * 0.5f); // 175〜195: 解除後は再発動しない
        engageToggles = 0;
        yield return Segment("hysteresis 175-195", -1f, true, 12f);
        int togglesB = engageToggles;
        Check(togglesA <= 1 && togglesB <= 1, $"no ON/OFF chatter near the threshold (toggles {togglesA}/{togglesB}, expect <=1 each)");
        speedCurve = null;

        // ---- 4) 手動操作との共存 ----
        injectManual = true;
        yield return Segment("250kmh ON + manual", 250f, true, 15f);
        injectManual = false;
        Check(manualConflicts == 0, $"no auto input in the same frame as manual input / inside the suppress windows (conflicts={manualConflicts}, manual frames={manualFrames})");

        // ---- 5) 停止(Pause)/カード選択からの復帰 ----
        targetKmh = 250f; assist.SetEnabled(true);
        yield return new WaitForSeconds(1.5f);
        int a0 = TotalActions(), r0 = assist.ResumeResets;
        object owner = new object();
        TimeControl.Pause(owner);
        yield return new WaitForSecondsRealtime(1.0f);
        int aDuringPause = TotalActions() - a0;
        TimeControl.Resume(owner);
        yield return null; yield return null;
        L($"[Pause] actions during pause={aDuringPause} resumeResets +{assist.ResumeResets - r0} timeScale={Time.timeScale:F2}");
        Check(aDuringPause == 0 && assist.ResumeResets > r0, "no auto action while paused, state reset on resume");
        a0 = TotalActions(); r0 = assist.ResumeResets;
        typeof(GameManager).GetMethod("TriggerLevelUpChoice", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gm, null);
        float cw = 0f;
        while (!gm.IsRewardSequenceWaitingForSelection && cw < 5f) { yield return null; cw += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        int aDuringChoice = TotalActions() - a0;
        float tsChoice = Time.timeScale;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.2f); seq.OnCardClicked(0); }
        cw = 0f;
        while (gm.IsLocalChoiceOpen && cw < 5f) { yield return null; cw += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.5f);
        L($"[CardChoice] timeScale during choice={tsChoice:F2} actions during choice={aDuringChoice} resumeResets +{assist.ResumeResets - r0} after={Time.timeScale:F2}");
        Check(tsChoice == 0f && aDuringChoice == 0 && Mathf.Approximately(Time.timeScale, 1f), "card choice still pauses (single), no auto action during it, time back to 1");

        // ---- 全体 ----
        L($"[Frames] slow frames (0<timeScale<1) = {slowFrames}/{framesTotal}");
        Check(slowFrames == 0, "time never slowed by speed (auto slow removed)");
        int dn = Mathf.Max(1, assist.DecideCount - 30);
        double avgMs = assist.TotalDecideMs / dn;
        L($"[Cost] decisions={assist.DecideCount} avg={avgMs:F3}ms max={assist.MaxDecideMs:F2}ms slow(>=2ms)={assist.SlowDecides} budgetCutoffs={assist.BudgetCutoffs}");
        Check(avgMs < 0.5 && assist.SlowDecides <= dn / 100, $"assist decision cost avg {avgMs:F3}ms (<0.5ms), slow frames {assist.SlowDecides}/{dn} (<=1%)");
        if (noSafeSamples.Count > 0) L("[NoSafe samples] " + string.Join(" | ", noSafeSamples));
        foreach (string o in obstacleSamples) L("[Obstacle hit] " + o);
        Check(!anyException, "no exceptions");

        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        FreezeDiagnostics.EventTap -= OnTap;
        Application.logMessageReceived -= handler;
        PlayerController.DebugSpeedScale = 1f;
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, $"../HighSpeedAssistAutoTest_{stage}_{character}.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
    }

    int TotalActions() => assist.AutoJumps + assist.AutoDoubleJumps + assist.AutoAttacks;

    struct Seg { public int actions, jumps, dbl, attacks, fall, obstacle, enemy, other, spikes, kills, noSafe; public float kmh, meters; }
    Seg lastSeg;

    IEnumerator Segment(string name, float kmh, bool on, float seconds)
    {
        if (kmh >= 0f) targetKmh = kmh;
        assist.SetEnabled(on);
        // 速度が安定するまで少し走る(集計に含めない)
        yield return new WaitForSeconds(1.0f);
        int j0 = assist.AutoJumps, d0 = assist.AutoDoubleJumps, a0 = assist.AutoAttacks, n0 = assist.NoSafeActionCount;
        int f0 = dmgFall, o0 = dmgObstacle, e0 = dmgEnemy, x0 = dmgOther, s0 = CaveStage.SpikeHitCount, k0 = gm.EnemyKillCount;
        double dist0 = pc.DistanceExact;
        float kmhSum = 0f; int kmhN = 0;
        segTime = 0f;
        while (segTime < seconds)
        {
            yield return null;
            segTime += Time.deltaTime;
            kmhSum += GameManager.SpeedKmh(pc.CurrentAutoRunSpeed); kmhN++;
        }
        var s = new Seg
        {
            jumps = assist.AutoJumps - j0, dbl = assist.AutoDoubleJumps - d0, attacks = assist.AutoAttacks - a0, noSafe = assist.NoSafeActionCount - n0,
            fall = dmgFall - f0, obstacle = dmgObstacle - o0, enemy = dmgEnemy - e0, other = dmgOther - x0, spikes = CaveStage.SpikeHitCount - s0,
            kills = gm.EnemyKillCount - k0, kmh = kmhSum / Mathf.Max(1, kmhN), meters = (float)(pc.DistanceExact - dist0),
        };
        s.actions = s.jumps + s.dbl + s.attacks;
        lastSeg = s;
        L($"[{name}] avg {s.kmh:F0}km/h {s.meters:F0}m in {seconds:F0}s | auto jump={s.jumps} double={s.dbl} attack={s.attacks} noSafe={s.noSafe} | damage fall={s.fall} obstacle={s.obstacle} enemy={s.enemy} other={s.other} (spikes={s.spikes}) | kills={s.kills} | maxDecide={assist.MaxDecideMs:F2}ms slow={assist.SlowDecides}");
    }

    // 目標速度を保つ。実際のプレイで高速になるのは速度カード(runSpeedを掛け算)なので、DEBUGの速度倍率ではなく
    // runSpeedを合わせる(DEBUG倍率は障害物の間隔(SpeedRatio)まで広げてしまい、実際より障害物が疎になるため)。
    float naturalRunSpeed = -1f;
    IEnumerator SpeedController()
    {
        float t = 0f;
        PlayerController.DebugSpeedScale = 1f;
        while (true)
        {
            yield return null;
            t += Time.deltaTime;
            if (naturalRunSpeed < 0f) naturalRunSpeed = pc.runSpeed;
            float k = speedCurve != null ? speedCurve(t) : targetKmh;
            if (k <= 0f) { pc.runSpeed = naturalRunSpeed; continue; }
            float mult = pc.CurrentAutoRunSpeed / Mathf.Max(0.0001f, pc.runSpeed); // 距離による速度上昇
            if (mult > 0.01f) pc.runSpeed = (k / GameManager.KmhPerMps) / mult;
        }
    }

    // 毎フレーム: 時間倍率/発動の切り替わり/手動入力の注入と競合の確認
    IEnumerator FrameMonitor()
    {
        float lastActionSeen = -1f;
        while (true)
        {
            // 手動入力(テスト用の注入)は PlayerController.Update より前に置く必要があるので、フレームの頭で設定する
            if (injectManual && pc != null)
            {
                manualTimer += Time.deltaTime;
                PlayerController.FlickDirection? m = null;
                if (manualTimer > 0.45f)
                {
                    manualTimer = 0f;
                    manualPhase++;
                    m = manualPhase % 3 == 0 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
                }
                pc.debugInjectFlick = m;
            }
            else if (pc != null) pc.debugInjectFlick = null;
            yield return null; // → このフレームの Update 群が終わった後(コルーチンはUpdateの後)に下を確認
            float ts = Time.timeScale;
            framesTotal++;
            if (ts > 0.001f && ts < 0.999f) slowFrames++;
            if (assist.Engaged != lastEngaged) { engageToggles++; lastEngaged = assist.Engaged; }
            // 注入した手動入力がこのフレームで実際に使われた(補助が手動入力として受け取った)時だけ数える。
            // HitStop等でPlayerController.Updateが入力処理まで来なかったフレームの注入は手動操作にならない。
            if (injectManual && pc.debugInjectFlick.HasValue && assist.ManualInputs != lastManualCount)
            {
                manualFrames++;
                if (pc.debugInjectFlick == PlayerController.FlickDirection.Up) lastManualUp = Time.time; else lastManualAttack = Time.time;
            }
            lastManualCount = assist.ManualInputs;
            if (injectManual && assist.LastActionTime != lastActionSeen && assist.LastActionTime >= Time.time - 0.0001f)
            {
                bool isJump = assist.LastAction.Contains("ジャンプ");
                bool sameFrameManual = lastManualUp >= Time.time - 0.0001f || lastManualAttack >= Time.time - 0.0001f;
                if (sameFrameManual) manualConflicts++;
                else if (isJump && Time.time - lastManualUp < assist.manualJumpSuppress - 0.0001f) manualConflicts++;
                else if (!isJump && Time.time - lastManualAttack < assist.manualAttackSuppress - 0.0001f) manualConflicts++;
            }
            lastActionSeen = assist.LastActionTime;
        }
    }
}

public static class HighSpeedAssistTestMenu
{
    [MenuItem("Tools/OneMoreMile/High Speed Assist Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetString("HighSpeedAssistTestStage", "wasteland_road");
        Run();
    }

    static void Run()
    {
        EditorPrefs.SetInt("HighSpeedAssistTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/OneMoreMile/High Speed Assist Test - Cave (batch)")]
    public static void RunCave()
    {
        EditorPrefs.SetString("HighSpeedAssistTestStage", "natural_cave");
        Run();
    }
}
#endif

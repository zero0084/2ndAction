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
        // 前後比較(2026-09-29): 固定シードで地形/障害物の種類/敵の編成を決定的にする(TerrainManager.Startの初期生成より前)。
        compareMode = Arg("-hsaCompare", "");
        if (compareMode != "")
        {
            int seed = int.Parse(Arg("-hsaSeed", "20260929"));
            WorldRng.BeginDeterministic(seed);
            if (compareMode == "before") ApplyLegacy();
        }
        new GameObject("HighSpeedAssistTest").AddComponent<HighSpeedAssistAutoTest>();
    }

    static string compareMode = "";
    // 改修前の規則を再現(比較専用): 壊せる木だけ耐久2・他は壊れない/弾は障害物を素通り/攻撃の掃引なし/
    // 補助の発動200・解除180/障害物の破壊判断なし/着地直前の二段ジャンプ判断なし。
    static void ApplyLegacy()
    {
        ObstacleController.LegacyRules = true;
        PlayerAttackSweeper.Enabled = false;
        var a = HighSpeedAssist.Instance;
        if (a != null) { a.engageKmh = 200f; a.releaseKmh = 180f; a.breakObstacles = false; a.earlyDoubleJump = false; a.requirePassObstacle = false; a.jumpClearance = 0f; }
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
    int dmgFall, dmgObstacle, dmgEnemy, dmgOther, dmgSpike, dmgWall, respawns;
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
            // 2026-09-29: 分類の修正(以前は地形の壁が「敵」、天井の針が「その他」に入っていた)
            else if (r.Contains("CeilingSpike"))
            {
                dmgSpike++;
                if (obstacleSamples.Count < 16 && assist != null && pc != null)
                    obstacleSamples.Add($"SPIKE t={Time.time:F1} kmh={assist.JudgedKmh:F0} x={pc.transform.position.x:F1} y={pc.transform.position.y:F2} vy={pc.AssistVelocityY:F1} jumpsUsed={pc.AssistJumpsUsed} last={assist.LastAction}@{Time.time - assist.LastActionTime:F2}s why=[{assist.LastActionReason}] plan=[{assist.PlanText}] status={assist.CurrentStatus}");
            }
            else if (r.Contains("TerrainWall")) dmgWall++;
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
        if (compareMode == "before") ApplyLegacy();
        StartCoroutine(SpeedController());
        StartCoroutine(FrameMonitor());

        if (compareMode != "")
        {
            yield return CompareRun();
            FinishCompare(anyException, handler);
            yield break;
        }

        // ---- 1) 通常速度: 補助は待機のまま ----
        yield return Segment("natural", 0f, true, 10f);
        Check(lastSeg.actions == 0 && assist.CurrentStatus == HighSpeedAssist.Status.WaitingSpeed, $"natural speed ({lastSeg.kmh:F0}km/h): assist waits, no auto actions (actions={lastSeg.actions} status={assist.CurrentStatus})");

        // ---- 2) 発動直後(105km/h)/200km/h付近/さらに高速: 補助ON/OFFの比較 ----
        yield return Segment("105kmh ON", 105f, true, 20f); var on105 = lastSeg;
        yield return Segment("105kmh OFF", 105f, false, 20f); var off105 = lastSeg;
        // 2026-09-30: 100〜fullAssistKmh は段階的に強くなる(弱い間は「間に合う最後の踏み切り」寄り)
        yield return Segment("120kmh ON", 120f, true, 20f); var on120 = lastSeg;
        yield return Segment("120kmh OFF", 120f, false, 20f); var off120 = lastSeg;
        Check(on105.strength < on120.strength && on120.strength < 1f, $"assist strength ramps 100->{assist.fullAssistKmh:F0}km/h (105: {on105.strength:F2}, 120: {on120.strength:F2})");
        Check(on120.fall + on120.obstacle <= off120.fall + off120.obstacle, $"120km/h terrain damage ON({on120.fall + on120.obstacle}) <= OFF({off120.fall + off120.obstacle})");
        var on205 = default(Seg); var off205 = default(Seg);
        yield return Segment("205kmh ON", 205f, true, 25f); on205 = lastSeg;
        yield return Segment("205kmh OFF", 205f, false, 25f); off205 = lastSeg;
        yield return Segment("300kmh ON", 300f, true, 25f); var on300 = lastSeg;
        yield return Segment("300kmh OFF", 300f, false, 25f); var off300 = lastSeg;
        yield return Segment("420kmh ON", 420f, true, 20f); var on420 = lastSeg;
        yield return Segment("420kmh OFF", 420f, false, 20f); var off420 = lastSeg;
        Check(on105.actions > 0 && on205.actions > 0 && on300.actions > 0, "assist acts at 105/205/300km/h");
        Check(on105.fall + on105.obstacle <= off105.fall + off105.obstacle, $"105km/h terrain damage ON({on105.fall + on105.obstacle}) <= OFF({off105.fall + off105.obstacle})");
        Check(on420.fall + on420.obstacle < off420.fall + off420.obstacle || off420.fall + off420.obstacle == 0, $"420km/h terrain damage ON({on420.fall + on420.obstacle}) < OFF({off420.fall + off420.obstacle})");
        Check(on205.fall + on205.obstacle < off205.fall + off205.obstacle || off205.fall + off205.obstacle == 0, $"205km/h terrain damage ON({on205.fall + on205.obstacle}) < OFF({off205.fall + off205.obstacle})");
        Check(on300.fall + on300.obstacle < off300.fall + off300.obstacle || off300.fall + off300.obstacle == 0, $"300km/h terrain damage ON({on300.fall + on300.obstacle}) < OFF({off300.fall + off300.obstacle})");
        Check(on205.kills >= off205.kills, $"205km/h kills ON({on205.kills}) >= OFF({off205.kills})");

        // ---- 3) ヒステリシス(発動engage/解除release: 既定100/90)----
        assist.SetEnabled(true);
        float en = assist.engageKmh, rel = assist.releaseKmh;
        float midA = (en + rel) * 0.5f + (en - rel) * 0.9f, ampA = (en - rel) * 1.3f;  // 一度発動したら、解除ラインより下へは行かない
        speedCurve = t => midA + ampA * Mathf.Sin(t * Mathf.PI * 0.5f);
        engageToggles = 0; lastEngaged = assist.Engaged;
        yield return Segment($"hysteresis {midA - ampA:F0}-{midA + ampA:F0}", -1f, true, 12f);
        int togglesA = engageToggles;
        float midB = (en + rel) * 0.5f - (en - rel) * 0.4f, ampB = (en - rel) * 0.8f;  // 解除後は、発動ラインまでは上がらない
        speedCurve = t => midB + ampB * Mathf.Sin(t * Mathf.PI * 0.5f);
        engageToggles = 0;
        yield return Segment($"hysteresis {midB - ampB:F0}-{midB + ampB:F0}", -1f, true, 12f);
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

    // ===================================================================== //
    // 前後比較(2026-09-29): 固定シード・同じ速度・同じ攻撃力で、同じ距離を走る。
    // before=改修前の規則を再現(ApplyLegacy)、after=現在。地形・障害物の種類(地点ごと)・敵の編成は同じシードで同じになる。
    // 被弾は 落下/障害物/天井の針/地形の壁/敵/その他 に分けて数える。
    // ===================================================================== //
    float cmpKmh;
    IEnumerator CompareRun()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        cmpKmh = float.Parse(Arg("-hsaKmh", "205"), inv);
        float meters = float.Parse(Arg("-hsaMeters", "3000"), inv);
        assist.SetEnabled(true);
        targetKmh = cmpKmh;
        L($"[COMPARE {compareMode}] seed={WorldRng.Seed} deterministic={WorldRng.IsDeterministic} kmh={cmpKmh} meters={meters} legacyRules={ObstacleController.LegacyRules} sweep={PlayerAttackSweeper.Enabled} engage={assist.engageKmh}/{assist.releaseKmh} breakObstacles={assist.breakObstacles} earlyDoubleJump={assist.earlyDoubleJump} attackPower={pc.AttackPower}");
        var firstSeen = new Dictionary<ObstacleController, double>();
        var layout = new List<string>();
        var kindSeen = new Dictionary<string, int>(); var kindBroken = new Dictionary<string, int>(); var kindHit = new Dictionary<string, int>();
        var tracked = new List<ObstacleController>();
        float t0 = Time.time, limit = meters / (cmpKmh / 3.6f) * 2.5f + 40f;
        while (gm.MaxDistance < meters && Time.time - t0 < limit)
        {
            foreach (var o in ObstacleController.All)
            {
                if (o == null || firstSeen.ContainsKey(o)) continue;
                double lx = o.transform.position.x + FloatingOrigin.Offset;
                firstSeen[o] = lx;
                tracked.Add(o);
                layout.Add($"{o.kind}@{lx:F0}");
            }
            yield return null;
        }
        foreach (var o in tracked)
        {
            if (o == null) continue;
            if (firstSeen[o] > gm.MaxDistance) continue; // まだ来ていない
            string k = string.IsNullOrEmpty(o.kind) ? "?" : o.kind;
            kindSeen[k] = (kindSeen.TryGetValue(k, out int a) ? a : 0) + 1;
            if (o.BrokenAt >= 0f) kindBroken[k] = (kindBroken.TryGetValue(k, out int b) ? b : 0) + 1;
            if (o.ContactDamaged) kindHit[k] = (kindHit.TryGetValue(k, out int c) ? c : 0) + 1;
        }
        var sb = new StringBuilder();
        foreach (var kv in kindSeen) sb.Append($" {kv.Key}:{kv.Value}(broken {(kindBroken.TryGetValue(kv.Key, out int b2) ? b2 : 0)}/hit {(kindHit.TryGetValue(kv.Key, out int h2) ? h2 : 0)})");
        L($"[COMPARE {compareMode}] reached {gm.MaxDistance:F0}m in {Time.time - t0:F1}s");
        L($"  damage: fall={dmgFall} obstacle={dmgObstacle} ceilingSpike={dmgSpike} terrainWall={dmgWall} enemy={dmgEnemy} other={dmgOther} | total={dmgFall + dmgObstacle + dmgSpike + dmgWall + dmgEnemy + dmgOther}");
        L($"  obstacles:{sb}");
        L($"  assist: jumps={assist.AutoJumps} double={assist.AutoDoubleJumps} attacks={assist.AutoAttacks} noSafe={assist.NoSafeActionCount} obstacleDecisions break={assist.ObstacleBreakPlans} jump={assist.ObstacleJumpPlans} none={assist.ObstacleNoPlan} abandoned={assist.BreakAbandoned} collectMax={assist.CollectMaxCount}");
        L($"  sweeper: sweeps={PlayerAttackSweeper.SweepsRun} sweptTargets={PlayerAttackSweeper.SweptTargets} enemySwept={EnemyController.SweptHits} | kills={gm.EnemyKillCount}");
        L($"  layout({layout.Count}) hash={string.Join(",", layout).GetHashCode():X8} first: {string.Join(" ", layout.GetRange(0, Mathf.Min(20, layout.Count)))}");
        if (noSafeSamples.Count > 0) L("  [NoSafe samples] " + string.Join(" | ", noSafeSamples));
        foreach (string o in obstacleSamples) L("  [Obstacle hit] " + o);
    }

    void FinishCompare(bool anyException, Application.LogCallback handler)
    {
        ObstacleController.LegacyRules = false; PlayerAttackSweeper.Enabled = true; WorldRng.EndDeterministic();
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        FreezeDiagnostics.EventTap -= OnTap;
        Application.logMessageReceived -= handler;
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, $"../HighSpeedAssistCompare_{compareMode}_{stage}_{character}_{cmpKmh:F0}.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    int TotalActions() => assist.AutoJumps + assist.AutoDoubleJumps + assist.AutoAttacks;

    struct Seg { public int actions, jumps, dbl, attacks, fall, obstacle, enemy, other, spikes, wall, kills, noSafe, broken; public float kmh, meters, strength; }
    Seg lastSeg;

    IEnumerator Segment(string name, float kmh, bool on, float seconds)
    {
        if (kmh >= 0f) targetKmh = kmh;
        assist.SetEnabled(on);
        // 速度が安定するまで少し走る(集計に含めない)
        yield return new WaitForSeconds(1.0f);
        int j0 = assist.AutoJumps, d0 = assist.AutoDoubleJumps, a0 = assist.AutoAttacks, n0 = assist.NoSafeActionCount;
        int f0 = dmgFall, o0 = dmgObstacle, e0 = dmgEnemy, x0 = dmgOther, s0 = dmgSpike, w0 = dmgWall, k0 = gm.EnemyKillCount, b0 = ObstacleController.TotalBroken;
        double dist0 = pc.DistanceExact;
        float kmhSum = 0f, strSum = 0f; int kmhN = 0;
        segTime = 0f;
        while (segTime < seconds)
        {
            yield return null;
            segTime += Time.deltaTime;
            kmhSum += GameManager.SpeedKmh(pc.CurrentAutoRunSpeed); strSum += assist.Strength; kmhN++;
        }
        var s = new Seg
        {
            jumps = assist.AutoJumps - j0, dbl = assist.AutoDoubleJumps - d0, attacks = assist.AutoAttacks - a0, noSafe = assist.NoSafeActionCount - n0,
            fall = dmgFall - f0, obstacle = dmgObstacle - o0, enemy = dmgEnemy - e0, other = dmgOther - x0, spikes = dmgSpike - s0, wall = dmgWall - w0, broken = ObstacleController.TotalBroken - b0,
            kills = gm.EnemyKillCount - k0, kmh = kmhSum / Mathf.Max(1, kmhN), strength = strSum / Mathf.Max(1, kmhN), meters = (float)(pc.DistanceExact - dist0),
        };
        s.actions = s.jumps + s.dbl + s.attacks;
        lastSeg = s;
        L($"[{name}] avg {s.kmh:F0}km/h strength {s.strength:F2} {s.meters:F0}m in {seconds:F0}s | auto jump={s.jumps} double={s.dbl} attack={s.attacks} noSafe={s.noSafe} | damage fall={s.fall} obstacle={s.obstacle} ceilingSpike={s.spikes} terrainWall={s.wall} enemy={s.enemy} other={s.other} | obstaclesBroken={s.broken} kills={s.kills} | maxDecide={assist.MaxDecideMs:F2}ms slow={assist.SlowDecides}");
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

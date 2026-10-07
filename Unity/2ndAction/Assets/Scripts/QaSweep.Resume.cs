#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// 中断セーブからの再開の準備時間(2026-10-03)の確認。 -qaResume <dir>
//   各ケース: 新しいランで走る → 距離/カード/HPを作る → RETURN TO HOME(中断セーブ) → CONTINUE →
//   停止した準備画面 → 敵を出して止まっていることを確認 → 再開ボタン(連打) → カウントダウン(途中で裏へ回す) → GO。
//   A: 通常速度(600m)  B: 高速(8,000m + SPEED UP)  C: 高速 + 慣らしON  D: 新規ランに準備画面が出ない
// テストの前に今のセーブを控え、最後に戻す。
public partial class QaSweep
{
    IEnumerator ResumeMode()
    {
        var snapSave = SaveSystem.Capture();
        bool hadEaseKey = PlayerPrefs.HasKey(GameManager.ResumeEaseDevKey);
        int easeKey = PlayerPrefs.GetInt(GameManager.ResumeEaseDevKey, -1);
        autoPickHold = true;
        string ch = Arg("-qaResumeChar", "swordsman");
        string only = Arg("-qaResumeOnly", "all"); // all / gate / footing
        if (only == "all" || only == "footing") yield return FootingSuite(ch);
        if (only == "footing")
        {
            if (hadEaseKey) PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, easeKey); else PlayerPrefs.DeleteKey(GameManager.ResumeEaseDevKey);
            autoPickHold = false;
            SaveSystem.Restore(snapSave); CardInventory.ReloadFromPrefs(); RunCheckpoint.Reload();
            L("[resume] test machine save restored");
            yield break;
        }
        yield return ResumeCase("A normal", ch, 600f, new string[] { "attack_up", "heart_up" }, false);
        yield return ResumeCase("B high", ch, 8000f, new string[] { "attack_up", "speed_up", "speed_up", "speed_up", "heart_up" }, false);
        yield return ResumeCase("C high+ease", ch, 8000f, new string[] { "speed_up", "speed_up", "speed_up" }, true);
        yield return ResumeCase("R re-interrupt", ch, 3000f, new string[] { "speed_up" }, false, 1);
        yield return ResumeCase("O old save", ch, 3000f, new string[0], false, 2);

        // D: 新規ランは従来どおり(準備画面なし、カウントダウンだけ)
        yield return BeginRun(ch, "wasteland_road");
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.None && !gm.ResumeGateActive && !pc.ResumeAccelActive, "D: a new run has no resume gate / acceleration");
        Check(Time.timeScale > 0.99f && !TimeControl.IsResumeEasing, $"D: a new run runs at normal time ({Time.timeScale:F2})");
        float x0 = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(1f);
        Check(pc.transform.position.x > x0 + 2f, "D: the new run moves on its own");
        yield return EndRun();

        if (hadEaseKey) PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, easeKey); else PlayerPrefs.DeleteKey(GameManager.ResumeEaseDevKey);
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[resume] test machine save restored");
    }

    static readonly Regex StatName = new Regex("(?i)(power|bonus|multiplier|maxjumps|jumpforce|runspeed|shield|lifesteal|range)");
    static readonly Regex StatSkip = new Regex("(?i)(timer|time|vel|cooldown|elapsed|last|frame|pending|visual)");

    // 能力値の一覧(カード/キャラが変える値)。変化の比較に使う。
    string StatSignature()
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(PlayerController).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(f => f.Name))
        {
            if (!(f.FieldType == typeof(float) || f.FieldType == typeof(int))) continue;
            if (!StatName.IsMatch(f.Name) || StatSkip.IsMatch(f.Name)) continue;
            object v = f.GetValue(pc);
            sb.Append(f.Name).Append('=').Append(v is float fv ? fv.ToString("0.####") : v.ToString()).Append(' ');
        }
        sb.Append($"| lives={gm.Lives}/{gm.maxLives} lv={gm.Level} exp={gm.Exp:0.##} cards={gm.UpgradeCount} kmh={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):0.##}");
        return sb.ToString();
    }

    IEnumerator ResumeCase(string name, string ch, float dist, string[] cards, bool ease, int variant = 0)
    {
        // variant: 0 = 通常 / 1 = 加速の途中で再び中断して CONTINUE / 2 = 古い保存(速さの距離なし)
        L($"---- {name}: {ch} {dist:F0}m cards={string.Join(",", cards)} ease={ease} variant={variant}");
        PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, ease ? 1 : 0);
        yield return BeginRun(ch, "wasteland_road");
        WarpTo(dist);
        yield return new WaitForSecondsRealtime(1.2f);
        var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", NP).GetValue(gm);
        var apply = typeof(GameManager).GetMethod("ApplyCardEffects", NP, null, new[] { typeof(CardDefinition) }, null);
        foreach (string id in cards) { var c = CardDatabase.FindById(id); if (c == null) { Warn($"{name}: unknown card {id}"); continue; } apply.Invoke(gm, new object[] { c }); hist.Add(c); }
        stopKeepAlive = true;
        yield return null;
        var livesSetter = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        int hurt = Mathf.Max(1, gm.maxLives - 13);
        livesSetter.Invoke(gm, new object[] { hurt });
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        string before = StatSignature();
        float kmhBefore = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        L($"{name}: before home d={gm.MaxDistance:F0} raw={pc.DistanceExact:F0} {before}");
        yield return ResumeGoHome();
        var cp = RunCheckpoint.Load();
        Check(RunCheckpoint.HasActiveRun && Mathf.Abs(cp.savedSpeedKmh - kmhBefore) < 0.6f, $"{name}: the checkpoint keeps the speed at save ({cp.savedSpeedKmh:F1} vs {kmhBefore:F1} km/h, speedDistance {cp.speedDistance:F0})");
        if (variant == 2)
        {
            cp.speedDistance = 0; cp.savedSpeedKmh = 0f; // この仕組みより前の保存
            RunCheckpoint.Save(cp);
        }
        double life0 = ProgressStats.LifetimeDistance;
        float highAtContinue = cp.highestReachedDistance;
        yield return ResumeContinue(name);
        Check(gm.AutoResumes == 1 && !resumeSawWaiting && !gm.ResumeGateActive, $"{name}: CONTINUE starts running by itself (no resume button / countdown) (auto {gm.AutoResumes}, waited {resumeSawWaiting})");
        Check(pc.ResumeAccelActive, $"{name}: the 5-second acceleration is running");
        string restored = StatSignature();
        Check(SigHead(restored) == SigHead(before), $"{name}: stats/HP/level/cards restored exactly");
        if (SigHead(restored) != SigHead(before)) L($"   before  : {before}\n   restored: {restored}");
        Check(Mathf.Abs(gm.MaxDistance - cp.checkpointDistance) < 1f, $"{name}: resumes at the saved checkpoint distance");
        float runStart = GameManager.SpeedKmh(pc.RunStartSpeed), target = GameManager.SpeedKmh(pc.NormalAutoRunSpeed), now0 = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        L($"{name}: run start {runStart:F1} km/h, now {now0:F1}, target {target:F1}, speed at save {kmhBefore:F1}");
        Check(now0 < runStart + Mathf.Max(1.5f, (target - runStart) * 0.08f), $"{name}: starts from the normal run start speed ({now0:F1} vs {runStart:F1})");
        if (variant != 2) Check(Mathf.Abs(target - kmhBefore) < 1f, $"{name}: accelerates to the speed at save, not some other speed ({target:F1} vs {kmhBefore:F1})");
        else L($"{name}: old save (no speed distance): target {target:F1} km/h (as before this change: from the checkpoint distance)");
        if (ease) Check(TimeControl.IsResumeEasing, $"{name}: dev ease still applies on top");

        // 途中: 止まっている間は計測も止まる
        while (pc.ResumeAccelActive && pc.ResumeAccelElapsed < 1.5f) yield return null;
        var owner = new object();
        float eP = pc.ResumeAccelElapsed, xP = pc.transform.position.x, kP = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        TimeControl.Pause(owner);
        yield return new WaitForSecondsRealtime(1.2f);
        Check(Mathf.Abs(pc.ResumeAccelElapsed - eP) < 0.001f && Mathf.Abs(pc.transform.position.x - xP) < 0.001f, $"{name}: a pause (card choice etc.) freezes the 5-second timer ({eP:F2} -> {pc.ResumeAccelElapsed:F2})");
        TimeControl.Resume(owner);
        Check(Mathf.Abs(GameManager.SpeedKmh(pc.CurrentAutoRunSpeed) - kP) < 1f, $"{name}: continues from the same speed after the pause");
        if (variant == 1)
        {
            // 加速の途中で再び中断 → 保存は変わらず、次の CONTINUE も同じ速さへ
            yield return new WaitForSecondsRealtime(0.5f);
            var cpA = RunCheckpoint.Load();
            yield return ResumeGoHome();
            var cpB = RunCheckpoint.Load();
            Check(cpB.checkpointDistance == cpA.checkpointDistance && System.Math.Abs(cpB.speedDistance - cpA.speedDistance) < 0.01 && Mathf.Abs(cpB.savedSpeedKmh - cpA.savedSpeedKmh) < 0.01f,
                $"{name}: interrupting during the acceleration does not change the saved state ({cpA.savedSpeedKmh:F1} -> {cpB.savedSpeedKmh:F1})");
            life0 = ProgressStats.LifetimeDistance;
            highAtContinue = cpB.highestReachedDistance; // 1回目の再開で走った所までは2回目では数えない(二重に足さない)
            yield return ResumeContinue(name + " again");
            float target2 = GameManager.SpeedKmh(pc.NormalAutoRunSpeed);
            Check(pc.ResumeAccelActive && Mathf.Abs(target2 - target) < 1f, $"{name}: the second CONTINUE accelerates to the same speed ({target2:F1} vs {target:F1})");
        }
        // 操作: 加速中もジャンプできる
        float y0 = pc.transform.position.y; float yMax = y0;
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
        for (int i = 0; i < 20; i++) { yMax = Mathf.Max(yMax, pc.transform.position.y); yield return null; }
        Check(yMax > y0 + 0.5f, $"{name}: jump works during the acceleration (+{yMax - y0:F2}m)");
        // 最後まで: 増えていき、終わりで継ぎ目が無い
        float last = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed), maxStep = 0f; int downs = 0;
        while (pc.ResumeAccelActive)
        {
            yield return null;
            float k = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            if (Time.deltaTime > 0f) { maxStep = Mathf.Max(maxStep, Mathf.Abs(k - last)); if (k < last - 0.05f) downs++; }
            last = k;
        }
        for (int i = 0; i < 10; i++) { yield return null; float k = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed); maxStep = Mathf.Max(maxStep, Mathf.Abs(k - last)); last = k; }
        float targetEnd = GameManager.SpeedKmh(pc.NormalAutoRunSpeed);
        L($"{name}: acceleration done: now {last:F1} km/h (normal {targetEnd:F1}), biggest per-frame change {maxStep:F2} km/h, drops {downs}");
        Check(Mathf.Abs(last - targetEnd) < 0.5f && maxStep < 2.5f, $"{name}: joins the normal speed smoothly (no jump at the end, max step {maxStep:F2} km/h)");
        Check(downs <= 2, $"{name}: speed only rises during the acceleration ({downs} drops)");
        // 累計距離: 再開後に走った分だけ増える(保存済みの距離を二重に足さない)
        double ran = gm.MaxDistance - Mathf.Max(cp.checkpointDistance, highAtContinue); // 新しく走った所だけ
        double lifeAdd = ProgressStats.LifetimeDistance - life0;
        Check(lifeAdd >= 0 && System.Math.Abs(lifeAdd - ran) < 5.0, $"{name}: lifetime distance grows only by what was run after CONTINUE (+{lifeAdd:F0}m vs ran {ran:F0}m)");
        Shot($"resume_{name.Split(' ')[0]}_after");
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
        yield return EndRun();
    }

    static string SigHead(string sig) { int i = sig.IndexOf(" kmh=", System.StringComparison.Ordinal); return i >= 0 ? sig.Substring(0, i) : sig; }

    bool resumeSawWaiting;
    IEnumerator ResumeGoHome()
    {
        var showPauseMenu = typeof(GameManager).GetField("showPauseMenu", NP);
        showPauseMenu.SetValue(gm, true);
        TimeControl.Pause(typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        yield return null;
        showPauseMenu.SetValue(gm, false);
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
    }

    IEnumerator ResumeContinue(string name)
    {
        float w = 0f, retry = 0f;
        resumeSawWaiting = false;
        while (w < 15f)
        {
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            if (gm.ResumeGate == GameManager.ResumeGatePhase.Waiting || gm.ResumeGate == GameManager.ResumeGatePhase.Countdown) resumeSawWaiting = true;
            if (gm.HasStarted && gm.AutoResumes > 0 && !gm.ResumeGateActive) break;
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        stopKeepAlive = true;
    }

    // ===== 再開地点の足場(2026-10-03) =====
    IEnumerator FootingSuite(string ch)
    {
        // 自動ジャンプ補助OFF・慣らしOFF(この2つに頼らずに落ちないこと)
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, 0);
        string[] speed3 = { "speed_up", "speed_up", "speed_up" };
        string[] speed8 = { "speed_up", "speed_up", "speed_up", "speed_up", "speed_up", "speed_up", "speed_up", "speed_up" };

        // 以前の動き(位置の指定なし)の再現: 荒野8km・高速で、2秒ぶんの区間に穴が出るか
        int legacyRuns = int.Parse(Arg("-qaResumeLegacyRuns", "6")), legacyPits = 0;
        for (int i = 0; i < legacyRuns; i++)
        {
            bool pit = false;
            yield return FootingCase($"legacy#{i + 1}", ch, "wasteland_road", 8000f, speed3, legacy: true, r => pit = r);
            if (pit) legacyPits++;
        }
        L($"[footing] LEGACY (before the fix): a pit within the 2-second stretch in {legacyPits}/{legacyRuns} continues at 8km");

        yield return ExistingPitCase(ch);

        string[] stages = Arg("-qaResumeStages", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        foreach (string st in stages)
        {
            yield return FootingCase($"{st} normal", ch, st, 600f, new string[0], false, null);
            yield return FootingCase($"{st} high", ch, st, 8000f, speed3, false, null);
        }
        // 8kmで繰り返し(地形は毎回乱数)+ さらに速い状態
        int reps = int.Parse(Arg("-qaResumeReps", "4"));
        for (int i = 0; i < reps; i++) yield return FootingCase($"wasteland 8km #{i + 1}", ch, "wasteland_road", 8000f, speed3, false, null);
        yield return FootingCase("wasteland very high", ch, "wasteland_road", 8000f, speed8, false, null);
    }

    // 生成済みの穴を安全区間で平地へ直す経路(実際のCONTINUEでは再開地点が生成済みの範囲より先なので通らないことが多い)
    IEnumerator ExistingPitCase(string ch)
    {
        yield return BeginRun(ch, "wasteland_road");
        WarpTo(8000f);
        var tm = TerrainManager.Instance;
        float? pit = null;
        float w = 0f;
        // 前方に生成済みの穴が見つかるまで少し走る
        while (w < 20f)
        {
            float px0 = pc.transform.position.x;
            pit = tm.FirstPitBetween(px0 + 12f, tm.GeneratedEndX - 12f);
            if (pit.HasValue) break;
            yield return new WaitForSecondsRealtime(0.3f); w += 0.3f;
        }
        if (!pit.HasValue) { Warn("existing pit: no generated pit found ahead - skipped"); yield return EndRun(); yield break; }
        TimeControl.Pause(this);
        float a = pit.Value - 6f, b = pit.Value + 14f;
        int fixedPits = tm.SetResumeFlatZone(FloatingOrigin.ToLogical(a), FloatingOrigin.ToLogical(b));
        Check(fixedPits >= 1, $"existing pit: an already generated pit in the stretch was fixed ({fixedPits})");
        Check(!tm.FirstPitBetween(a, b).HasValue, "existing pit: no pit left in the stretch");
        float prevH = float.NaN; int gaps = 0, steps = 0;
        for (float x = a - 10f; x <= b + 10f; x += 0.25f)
        {
            float? h = tm.GetHeightAt(x);
            if (!h.HasValue) { if (x >= a && x <= b) gaps++; prevH = float.NaN; continue; }
            if (!float.IsNaN(prevH) && Mathf.Abs(h.Value - prevH) > 0.35f) steps++;
            prevH = h.Value;
        }
        Check(gaps == 0 && steps == 0, $"existing pit: ground continuous through the fixed pit and its edges (gaps {gaps}, steps {steps})");
        // 見た目: 直した所の地面の絵(コライダー付きの地形の絵)がある
        var cam = Camera.main;
        if (cam != null) { var p = cam.transform.position; p.x = pit.Value + 2f; cam.transform.position = p; }
        yield return null;
        Shot("footing_existing_pit_fixed");
        tm.ClearResumeFlatZone();
        TimeControl.Resume(this);
        L($"existing pit: fixed {fixedPits} pit(s) at {FloatingOrigin.ToLogical(pit.Value):F0}m");
        yield return EndRun();
    }

    IEnumerator FootingCase(string name, string ch, string stage, float dist, string[] cards, bool legacy, System.Action<bool> pitResult)
    {
        yield return BeginRun(ch, stage);
        string active = (string)GetPrivate(gm, "activeRunStageId");
        if (active != stage) { Warn($"{name}: stage {stage} not available here (running {active}) - skipped"); yield return EndRun(); yield break; }
        WarpTo(dist);
        yield return new WaitForSecondsRealtime(0.8f);
        var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", NP).GetValue(gm);
        var apply = typeof(GameManager).GetMethod("ApplyCardEffects", NP, null, new[] { typeof(CardDefinition) }, null);
        foreach (string id in cards) { var c = CardDatabase.FindById(id); if (c != null) { apply.Invoke(gm, new object[] { c }); hist.Add(c); } }
        // HPは満タンで保存(テスト用のHP維持を止めてから)
        stopKeepAlive = true;
        yield return null;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        var showPauseMenu = typeof(GameManager).GetField("showPauseMenu", NP);
        showPauseMenu.SetValue(gm, true);
        TimeControl.Pause(typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        yield return null;
        showPauseMenu.SetValue(gm, false);
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.6f);
        gm = GameManager.Instance;
        if (legacy) gm.resumeSafeFootingEnabled = false;
        // CONTINUE は自動で走り出す(2026-10-07)。足場は走り出す前(遷移の覆いの下)に作ってある。走り出す直前の位置で測る
        float pxStart = 0f; bool got = false;
        w = 0f; float retry = 0f;
        while (w < 15f)
        {
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            if (gm.HasStarted && gm.ResumeGate == GameManager.ResumeGatePhase.Settling && PlayerController.Instance != null) { pxStart = PlayerController.Instance.transform.position.x; got = true; }
            if (gm.HasStarted && gm.AutoResumes > 0) break;
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        var tm = TerrainManager.Instance;
        stopKeepAlive = true;
        Check(gm.AutoResumes > 0 && got, $"{name}: CONTINUE started running by itself");
        float px = got ? pxStart : pc.transform.position.x;
        float speed = pc.NormalAutoRunSpeed;
        float need = Mathf.Max(gm.resumeSafeMinAhead, speed * gm.resumeSafeSeconds);
        float? pit = tm.FirstPitBetween(px - gm.resumeSafeBehind, px + need);
        if (legacy)
        {
            L($"{name}: LEGACY {GameManager.SpeedKmh(speed):F0} km/h, 2s = {speed * 2f:F0}m: first pit {(pit.HasValue ? $"{pit.Value - px:F1}m ahead" : "none")}");
            pitResult?.Invoke(pit.HasValue);
            if (pit.HasValue && pit.Value - px < 20f) Shot($"footing_legacy_{name.Replace('#', '_')}");
            gm.resumeSafeFootingEnabled = true;
            yield return EndRun();
            yield break;
        }
        Check(Mathf.Abs(gm.LastResumeSafeAheadMeters - need) < 0.5f, $"{name}: safe stretch = max({gm.resumeSafeMinAhead:F0}m, speed x {gm.resumeSafeSeconds:F1}s) ({gm.LastResumeSafeAheadMeters:F1} vs {need:F1}, {gm.LastResumeSafeKmh:F1} km/h)");
        Check(!pit.HasValue, $"{name}: no pit under the feet or in the next {need:F0}m (first pit {(pit.HasValue ? (pit.Value - px).ToString("F1") + "m" : "none")})");
        tm.DescribeGround(px - gm.resumeSafeBehind, px + need, out int slopes, out float minY, out float maxY);
        Check(slopes == 0 && maxY - minY < 0.01f, $"{name}: the stretch is level ground ({slopes} slopes, height {minY:F2}..{maxY:F2})");
        // 足元: 接地して地面の上に立っている
        float? gy = tm.GetHeightAt(px);
        Check(pc.IsGrounded && gy.HasValue && Mathf.Abs(pc.transform.position.y - (gy.Value + pc.groundOffset)) < 0.02f, $"{name}: standing on the ground at the restore point (grounded={pc.IsGrounded})");
        // 当たり判定(着地に使う地面の高さ)が区間とつなぎ目で途切れない: 0.25m刻みで高さが取れて、急な段差が無い
        float end = px + need, prevH = float.NaN; int gaps = 0, steps = 0;
        for (float x = px - gm.resumeSafeBehind; x <= end + 20f; x += 0.25f)
        {
            float? h = tm.GetHeightAt(x);
            if (!h.HasValue) { if (x <= end) gaps++; prevH = float.NaN; continue; }
            if (!float.IsNaN(prevH) && Mathf.Abs(h.Value - prevH) > 0.35f) steps++;
            prevH = h.Value;
        }
        Check(gaps == 0, $"{name}: ground height exists all along the stretch ({gaps} gaps)");
        Check(steps == 0, $"{name}: no sudden step in the stretch or where it joins normal terrain ({steps})");
        string sig0 = tm.DebugTerrainSignature(FloatingOrigin.ToLogical(px) - 10f, FloatingOrigin.ToLogical(end) + 10f);
        Shot($"footing_{name.Replace(' ', '_')}_ready");

        // FloatingOriginで座標がずれていても論理Xで比べる
        float pxGo = px;
        float shift = pxGo - px; // ずれ(=原点の移動)。準備画面中はプレイヤーは動かない
        string sigGo = tm.DebugTerrainSignature(FloatingOrigin.ToLogical(pxGo) - 10f, FloatingOrigin.ToLogical(pxGo) + need + 10f);
        Check(sigGo == sig0, $"{name}: footing unchanged from the ready screen to GO ({sig0} -> {sigGo})");
        Check(!tm.FirstPitBetween(pxGo - gm.resumeSafeBehind, pxGo + need).HasValue, $"{name}: still no pit at GO");

        // GO の後 2.2 秒: 補助OFF・慣らしOFFのまま、穴へ落ちない(地面の下へ行かない/落下からの復帰が無い/被弾しない)
        int lives0 = gm.Lives;
        float lx0 = FloatingOrigin.ToLogical(pc.transform.position.x);
        float t = 0f; bool fellIn = false; float lowestIn = float.PositiveInfinity;
        float zoneEndLogical = FloatingOrigin.ToLogical(pxGo) + need;
        int livesSeen = gm.Lives, damageIn = 0;
        string after = "";
        // 安全区間の中(GO〜約2秒)が判定の対象。区間を出た直後(〜2.4秒)は通常の地形なので、何が来たかを記録だけする。
        while (t < 2.4f && !gm.IsGameOver)
        {
            float x = pc.transform.position.x;
            float lx = FloatingOrigin.ToLogical(x);
            float? h = tm.GetHeightAt(x);
            bool inZone = lx <= zoneEndLogical;
            if (inZone)
            {
                if (!h.HasValue) { fellIn = true; L($"   over a pit INSIDE the stretch at {lx - zoneEndLogical:+0.0;-0.0}m (t={t:F2}s)"); }
                else lowestIn = Mathf.Min(lowestIn, pc.transform.position.y - (h.Value + pc.groundOffset));
                if (gm.Lives < livesSeen) { damageIn++; L($"   damage INSIDE the stretch {livesSeen}->{gm.Lives} source={pc.LastDamageSource} (t={t:F2}s)"); }
            }
            else if (after.Length == 0 && (!h.HasValue || gm.Lives < livesSeen))
                after = !h.HasValue ? $"normal terrain resumes: a pit {lx - zoneEndLogical:F1}m after the stretch end" : $"damage {pc.LastDamageSource} {lx - zoneEndLogical:F1}m after the stretch end";
            livesSeen = gm.Lives;
            yield return null; t += Time.unscaledDeltaTime;
        }
        float ran = FloatingOrigin.ToLogical(pc.transform.position.x) - lx0;
        Check(!fellIn && lowestIn > -0.05f, $"{name}: never over a pit / below the ground inside the stretch (lowest {lowestIn:F2})");
        Check(damageIn == 0 && !gm.IsGameOver, $"{name}: no damage inside the stretch ({damageIn})");
        if (after.Length > 0) L($"   (after the stretch) {after}");
        L($"{name}: {GameManager.SpeedKmh(speed):F0} km/h, stretch {need:F0}m (fixed pits {gm.LastResumeFixedPits}), ran {ran:F0}m in 2.4s, shift {shift:F1}");
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
        yield return EndRun();
    }

    // 敵/弾/ボスなど、時間で動く物(プレイヤー/カメラ/背景の層は除く)
    List<Transform> MovingThings()
    {
        var list = new List<Transform>();
        foreach (var rb in FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None))
            if (rb != null && rb.gameObject.activeInHierarchy && pc != null && rb.transform != pc.transform && !rb.transform.IsChildOf(pc.transform)) list.Add(rb.transform);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e != null && e.isActiveAndEnabled && !list.Contains(e.transform)) list.Add(e.transform);
        return list;
    }
}
#endif

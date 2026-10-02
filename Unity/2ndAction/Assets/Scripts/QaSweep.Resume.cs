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
        yield return ResumeCase("A normal", ch, 600f, new string[] { "attack_up", "heart_up" }, false);
        yield return ResumeCase("B high", ch, 8000f, new string[] { "attack_up", "speed_up", "speed_up", "speed_up", "heart_up" }, false);
        yield return ResumeCase("C high+ease", ch, 8000f, new string[] { "speed_up", "speed_up", "speed_up" }, true);

        // D: 新規ランは従来どおり(準備画面なし、カウントダウンだけ)
        yield return BeginRun(ch, "wasteland_road");
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.None && !gm.ResumeGateActive, "D: a new run has no resume gate");
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

    IEnumerator ResumeCase(string name, string ch, float dist, string[] cards, bool ease)
    {
        L($"---- {name}: {ch} {dist:F0}m cards={string.Join(",", cards)} ease={ease}");
        PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, ease ? 1 : 0);
        yield return BeginRun(ch, "wasteland_road");
        WarpTo(dist);
        yield return new WaitForSecondsRealtime(1.2f);
        // カードを取った状態を作る(ラン中の取得と同じ: 効果を掛けて取得履歴へ)
        var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", NP).GetValue(gm);
        var apply = typeof(GameManager).GetMethod("ApplyCardEffects", NP, null, new[] { typeof(CardDefinition) }, null);
        foreach (string id in cards) { var c = CardDatabase.FindById(id); if (c == null) { Warn($"{name}: unknown card {id}"); continue; } apply.Invoke(gm, new object[] { c }); hist.Add(c); }
        // 傷を負った状態(満タンでない)
        stopKeepAlive = true;
        yield return null;
        var livesSetter = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        int hurt = Mathf.Max(1, gm.maxLives - 13);
        livesSetter.Invoke(gm, new object[] { hurt });
        // ボス報酬の確定と同じく、今の距離をCONTINUEの再開位置にする(テスト機に残っていた古い再開位置を使わない)
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        string before = StatSignature();
        float kmhBefore = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        // 実際の操作と同じく、ポーズメニューを開いてから RETURN TO HOME(暗転中にゲームが進まないことも確かめる)
        var showPauseMenu = typeof(GameManager).GetField("showPauseMenu", NP);
        showPauseMenu.SetValue(gm, true);
        TimeControl.Pause(typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        yield return null;
        showPauseMenu.SetValue(gm, false);
        L($"{name}: before home d={gm.MaxDistance:F0} {before}");

        // RETURN TO HOME → 新しいシーン(ホーム)
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        Check(RunCheckpoint.HasActiveRun, $"{name}: an active run was saved");
        var cp = RunCheckpoint.Load();
        w = 0f;
        float retry = 0f;
        while (gm.ResumeGate != GameManager.ResumeGatePhase.Waiting && w < 12f)
        {
            // ホームへ戻る遷移が開き切るまでは CONTINUE を受け付けないので、出発するまで押し直す
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.Waiting, $"{name}: CONTINUE stops at the ready screen ({gm.ResumeGate})");
        Check(Time.timeScale == 0f, $"{name}: time is stopped while waiting ({Time.timeScale:F2}, reasons={TimeControl.DescribeActiveReasons()})");
        string waiting = StatSignature();
        L($"{name}: waiting  d={gm.MaxDistance:F0} (checkpoint {cp.checkpointDistance:F0}) {waiting}");
        Check(Mathf.Abs(gm.MaxDistance - cp.checkpointDistance) < 0.5f, $"{name}: resumes at the saved checkpoint distance");
        Check(gm.Lives == hurt, $"{name}: HP restored as saved ({gm.Lives} vs {hurt})");
        Check(gm.UpgradeCount == cards.Length, $"{name}: picked cards restored ({gm.UpgradeCount} vs {cards.Length})");
        float kmhWait = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        string Head(string sig) => sig.Substring(0, sig.IndexOf(" kmh=", System.StringComparison.Ordinal));
        Check(Head(waiting) == Head(before), $"{name}: stats/HP/level/cards restored exactly");
        if (Head(waiting) != Head(before)) L($"   before : {before}\n   waiting: {waiting}");
        Check(Mathf.Abs(kmhWait - kmhBefore) < 1f, $"{name}: saved speed restored ({kmhBefore:F1} -> {kmhWait:F1} km/h)");
        // 速度は距離で決まる(再開位置=checkpoint)。カードの倍率は同じ
        L($"{name}: speed before home {kmhBefore:F1} km/h, while waiting {kmhWait:F1} km/h (shown on the HUD)");
        Shot($"resume_{name.Split(' ')[0]}_waiting");

        // 待機中に敵を出しても動かない/被弾しない
        var dir = FindFirstObjectByType<EncounterDirector>();
        var spawned = new List<GameObject>();
        if (dir != null)
            foreach (string eid in new[] { "goblin_rider", "wolf", "bat" })
            {
                var go = dir.DebugSpawnEnemy(eid, EnemyAiTier.T3);
                if (go != null) spawned.Add(go);
            }
        if (spawned.Count == 0 && dir != null)
        {
            var ids = (List<string>)typeof(EncounterDirector).GetMethod("AllEnemyIds", NP).Invoke(dir, null);
            foreach (string eid in ids.Take(3)) { var go = dir.DebugSpawnEnemy(eid, EnemyAiTier.T3); if (go != null) spawned.Add(go); }
        }
        // プレイヤーの目の前へ寄せる(接触すれば被弾する位置)
        for (int i = 0; i < spawned.Count; i++)
        {
            var p = spawned[i].transform.position;
            p.x = pc.transform.position.x + 1.2f + i * 2.5f;
            spawned[i].transform.position = p;
        }
        yield return null;
        var objs = MovingThings();
        var pos0 = objs.ToDictionary(t => t, t => t.position);
        float px0 = pc.transform.position.x, py0 = pc.transform.position.y, d0 = gm.MaxDistance;
        int lives0 = gm.Lives;
        yield return new WaitForSecondsRealtime(2.5f);
        int moved = objs.Count(t => t != null && (t.position - pos0[t]).sqrMagnitude > 0.0001f);
        Check(spawned.Count > 0, $"{name}: test enemies spawned ({spawned.Count})");
        Check(moved == 0, $"{name}: nothing moved while waiting ({moved}/{objs.Count} moved: {string.Join(",", objs.Where(t => t != null && (t.position - pos0[t]).sqrMagnitude > 0.0001f).Take(6).Select(t => t.name))})");
        Check(Mathf.Abs(pc.transform.position.x - px0) < 0.001f && Mathf.Abs(pc.transform.position.y - py0) < 0.001f, $"{name}: player stays still while waiting");
        Check(gm.MaxDistance == d0, $"{name}: distance does not grow while waiting");
        Check(gm.Lives == lives0, $"{name}: no damage while waiting (enemies touching)");
        Check(StatSignature() == waiting, $"{name}: stats unchanged while waiting");

        // ボタン連打: 1回だけ受け付ける
        int accepted = 0;
        for (int i = 0; i < 6; i++) if (gm.RequestResumeFromGate()) accepted++;
        Check(accepted == 1 && gm.ResumeGate == GameManager.ResumeGatePhase.Countdown, $"{name}: repeated taps start one countdown ({accepted})");
        yield return new WaitForSecondsRealtime(1.3f);
        Check(gm.ResumeCountdownLabel == "2", $"{name}: countdown shows 2 after 1.3s ({gm.ResumeCountdownLabel})");
        Check(Time.timeScale == 0f, $"{name}: time still stopped during the countdown");
        // カウントダウン中にアプリが裏へ → 準備画面へ戻る
        int rev0 = gm.ResumeGateRevertCount;
        typeof(GameManager).GetMethod("OnApplicationPause", NP).Invoke(gm, new object[] { true });
        typeof(GameManager).GetMethod("OnApplicationPause", NP).Invoke(gm, new object[] { false });
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.Waiting && gm.ResumeGateRevertCount == rev0 + 1, $"{name}: backgrounding during the countdown returns to the ready screen ({gm.ResumeGate})");
        yield return new WaitForSecondsRealtime(0.5f);
        Check(Time.timeScale == 0f && gm.ResumeGate == GameManager.ResumeGatePhase.Waiting, $"{name}: still waiting after coming back");
        // ポーズメニューを開いてもカウントダウンは準備画面へ戻る
        Check(gm.RequestResumeFromGate(), $"{name}: second countdown starts");
        var showPause = typeof(GameManager).GetField("showPauseMenu", NP);
        showPause.SetValue(gm, true);
        yield return null; yield return null;
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.Waiting, $"{name}: opening the pause menu during the countdown returns to waiting ({gm.ResumeGate})");
        Check(!gm.RequestResumeFromGate(), $"{name}: resume button ignored while the pause menu is open");
        showPause.SetValue(gm, false);
        yield return null;

        // 本番のカウントダウン: 待機中に入れたフリックが GO の後に出ない
        string atStart = StatSignature();
        Check(gm.RequestResumeFromGate(), $"{name}: final countdown starts");
        float t0 = Time.realtimeSinceStartup;
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        bool flickWhileGated = false;
        float py1 = pc.transform.position.y;
        while (gm.ResumeGateActive && Time.realtimeSinceStartup - t0 < 6f)
        {
            if (pc.IsAttacking || Mathf.Abs(pc.transform.position.y - py1) > 0.001f) flickWhileGated = true;
            yield return null;
        }
        pc.debugInjectFlick = null;
        float took = Time.realtimeSinceStartup - t0;
        Check(!flickWhileGated, $"{name}: no attack/jump during the countdown");
        Check(took > 2.8f && took < 3.4f, $"{name}: countdown takes ~3s ({took:F2}s)");
        Check(gm.ResumeCountdownLabel == "GO!", $"{name}: GO! shown at release ({gm.ResumeCountdownLabel})");
        string atGo = StatSignature();
        Check(atGo == atStart, $"{name}: stats/HP/distance/cards unchanged from waiting to GO");
        if (atGo != atStart) L($"   waiting: {atStart}\n   go     : {atGo}");
        Check(gm.MaxDistance == d0, $"{name}: distance unchanged until GO");
        if (ease)
        {
            Check(TimeControl.IsResumeEasing && Mathf.Abs(Time.timeScale - gm.resumeEaseStartScale) < 0.05f, $"{name}: ease starts at x{gm.resumeEaseStartScale:F2} ({Time.timeScale:F2})");
            float runSpeed0 = pc.runSpeed;
            yield return new WaitForSecondsRealtime(gm.resumeEaseDuration * 0.4f);
            float mid = Time.timeScale;
            Check(mid > gm.resumeEaseStartScale + 0.02f && mid < 1f, $"{name}: ease ramps up ({mid:F2})");
            // 途中でカード選択などの停止が入ると 0 が優先、解除で慣らしの続き
            var owner = new object();
            TimeControl.Pause(owner);
            Check(Time.timeScale == 0f, $"{name}: a pause during the ease stops time");
            yield return new WaitForSecondsRealtime(1.5f);
            TimeControl.Resume(owner);
            Check(TimeControl.IsResumeEasing && Mathf.Abs(Time.timeScale - mid) < 0.15f, $"{name}: the ease continues after the pause ({Time.timeScale:F2} vs {mid:F2})");
            yield return new WaitForSecondsRealtime(gm.resumeEaseDuration * 0.75f);
            Check(!TimeControl.IsResumeEasing && Time.timeScale > 0.99f, $"{name}: ease ends at normal time ({Time.timeScale:F2})");
            Check(pc.runSpeed == runSpeed0, $"{name}: the ease does not touch the character's speed stat");
        }
        else
        {
            Check(Time.timeScale > 0.99f && !TimeControl.IsResumeEasing, $"{name}: normal time right after GO ({Time.timeScale:F2})");
        }
        float px2 = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(1.2f);
        Check(pc.transform.position.x > px2 + 1f && gm.MaxDistance > d0, $"{name}: running again after GO (x +{pc.transform.position.x - px2:F1})");
        int movedAfter = objs.Count(t => t != null && pos0.ContainsKey(t) && (t.position - pos0[t]).sqrMagnitude > 0.0001f);
        L($"{name}: after GO {movedAfter}/{objs.Count} tracked objects moved (they do move once time runs)");
        object gateOwner = typeof(GameManager).GetField("resumeGateTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object homeOwner = typeof(GameManager).GetField("returnHomeTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Check(!TimeControl.IsPausedBy(gateOwner) && !TimeControl.IsPausedBy(homeOwner), $"{name}: no resume/home stop reason left");
        L($"{name}: stop reasons now: {TimeControl.DescribeActiveReasons()} (level-up choice open={gm.IsRewardSequenceWaitingForSelection})");
        Shot($"resume_{name.Split(' ')[0]}_after");
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ラスダン終盤の開発用ワープ(EndgameDebug / DebugRun)の自動確認(2026-10-02)。 -ldQa <dir> -ldQaMode warps
//  A 90km → ボスラッシュ開始(雑魚なし、NEXT BOSS)        B 99km → 雑魚なし/静寂
//  C 三姉妹 → ボス戦開始                                  D 三姉妹撃破 → RESULTへ行かずエンドロール
//  E エンドロール → ONE MORE MILE? まで(F: YESで走り続ける) G ONE MORE MILE? → NO → 減速 → 停止 → ホーム
//  H DEBUG RUN の後で進行(BEST/累計距離/MILE/カード/解放/遭遇/CONTINUE)が変わっていない(落ちた時の復旧も)
//  I 同じワープを5回 → 二重生成なし/BGM二重なし/例外なし、前の状態(時間停止/選択画面)を持ち込まない
//  J 通常のラン → 保存は従来どおり(累計距離が増える)
public partial class LastDungeonQa
{
    Dictionary<string, string> ProgressPrint()
    {
        var d = new Dictionary<string, string>();
        foreach (var it in SaveSystem.CaptureCategory(SaveCategory.Progress)) d[it.k] = it.v;
        return d;
    }

    static string DiffPrint(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var diff = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k, out string va); b.TryGetValue(k, out string vb);
            if (va != vb) diff.Add($"{k}: '{Trim(va)}' -> '{Trim(vb)}'");
        }
        return string.Join(" | ", diff);
    }
    static string Trim(string s) => s == null ? "(none)" : s.Length > 40 ? s.Substring(0, 40) + "…" : s;

    IEnumerator Launch(EndgameDebug.Point p, EndgameDebug.Profile prof = EndgameDebug.Profile.Sturdy)
    {
        bool started = false;
        System.Action<EndgameDebug.Point> h = x => started = true;
        EndgameDebug.Started += h;
        float t0 = Time.realtimeSinceStartup;
        EndgameDebug.Launch(p, prof);
        float w = 0f;
        while (!started && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
        EndgameDebug.Started -= h;
        gm = GameManager.Instance; pc = PlayerController.Instance;
        L($"[launch] {EndgameDebug.Label(p)} ready in {Time.realtimeSinceStartup - t0:F1}s at {(gm != null ? gm.MaxDistance : -1f):F0}m, DebugRun={DebugRun.IsActive}, ts={Time.timeScale:F2}, state={LastDungeonFlow.Instance?.Current}");
        Check(started && DebugRun.IsActive && gm != null && gm.HasStarted && !gm.IsGameOver, $"{EndgameDebug.Label(p)}: launched as a DEBUG RUN");
        Check(Mathf.Approximately(Time.timeScale, 1f) && !gm.IsLocalChoiceOpen, $"{EndgameDebug.Label(p)}: starts with timeScale 1 and no choice screen");
    }

    int CountLoopingAudio() { int n = 0; foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (a.isPlaying && a.loop && a.clip != null) n++; return n; }
    string AudioLine() => string.Join(",", FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(a => a.isPlaying && a.loop && a.clip != null).Select(a => a.clip.name));

    IEnumerator WarpsMode()
    {
        // 元のセーブ(テスト機)は最後に戻す。DEBUG RUN の検査は「テスト前の進行」と比べる
        var machine = SaveSystem.Capture();
        gm = GameManager.Instance;
        float w0 = 0f;
        while ((gm == null || gm.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; gm = GameManager.Instance; }
        gm.SetSelectedCharacter(Arg("-ldChar", "swordsman"));
        var before = ProgressPrint();
        double life0 = ProgressStats.LifetimeDistance;
        double best0 = gm.GetStageBest(LastCorridorDirector.StageId);
        int mile0 = gm.TotalOwnedMile;
        bool cont0 = RunCheckpoint.HasActiveRun;
        L($"[warps] progress keys={before.Count} lifetime={life0:F0} lcBest={best0:F0} mile={mile0} continue={cont0}");
        var restored = new List<int>();
        System.Action note = () => restored.Add(DebugRun.LastRestoredKeys);

        // ---- A: 90km → ボスラッシュ
        yield return Launch(EndgameDebug.Point.LastDungeon90, EndgameDebug.Profile.SturdyStrong);
        var flow = LastDungeonFlow.Instance; var bm = BossManager.Instance;
        int zakoMax = 0; float w = 0f;
        while (bm.RushGateK == 0 && w < 20f) { zakoMax = Mathf.Max(zakoMax, ActiveEnemies()); yield return null; w += Time.deltaTime; }
        L($"  A: state={flow.Current} rushGate={bm.RushGateK} at {gm.MaxDistance:F0}m ({BossManager.RushGateLabel(bm.RushGateK)}) bosses={ActiveBosses()} zakoMax={zakoMax}");
        Check(flow.Current == LastDungeonFlow.State.Rush && bm.RushGateK == BossManager.RushFirstK, "A: 90km warp -> the boss rush starts at the first gate (90,000m)");
        // NEXT BOSS: 今の関門のボスを倒す → 次のボス(同じ関門の続き or 次の関門)
        int gate0 = bm.RushGateK, spawned0 = bm.RushSpawnedThisGate;
        w = 0f;
        while (ActiveBosses() == 0 && w < 10f) { yield return null; w += Time.deltaTime; }
        EndgameDebug.NextBoss();
        w = 0f;
        while (w < 45f && !(bm.RushGateK > gate0 || (bm.RushGateK == gate0 && bm.RushSpawnedThisGate > spawned0 && ActiveBosses() > 0)))
        { zakoMax = Mathf.Max(zakoMax, ActiveEnemies()); yield return null; w += Time.unscaledDeltaTime; }
        L($"  A: after NEXT BOSS: gate {gate0 * 1000} -> {bm.RushGateK * 1000}, spawned this gate {bm.RushSpawnedThisGate}, bosses {ActiveBosses()}, status '{EndgameDebug.Instance.Status}', zakoMax={zakoMax}");
        Check(bm.RushGateK > gate0 || bm.RushSpawnedThisGate > spawned0, "A: NEXT BOSS brings the next rush boss");
        Check(zakoMax == 0, "A: no normal enemies during the boss rush");

        // ---- B: 99km → 静寂
        yield return Launch(EndgameDebug.Point.LastDungeon99);
        flow = LastDungeonFlow.Instance; bm = BossManager.Instance;
        if (BossManager.ContinuousRush)
        {
            // 2026-10-06: 走りながらのボスラッシュ: 97/98km のボスが残った状態から → 99kmで足止め → 全員倒す → 静寂
            w = 0f;
            while (!(bm.CurrentBossEncounter != null && bm.CurrentBossEncounter.held99) && w < 40f) { yield return null; w += Time.deltaTime; }
            bool held = bm.CurrentBossEncounter != null && bm.CurrentBossEncounter.held99;
            float dHeld = gm.MaxDistance;
            L($"  B: run rush from 97km: held at 99km={held} d={dHeld:F0} remaining={(bm.CurrentBossEncounter != null ? bm.CurrentBossEncounter.Remaining : -1)}");
            Check(held && dHeld <= 99000.5f, "B: 99km warp (run rush): the remaining bosses stop the run at 99km");
            w = 0f;
            while (bm.IsBossPhase && !bm.BossDefeatedThisPhase && w < 40f)
            {
                foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && !(b is ReaperSisterBoss)) b.TakeDamage(99999999, b.CenterWorld);
                yield return new WaitForSeconds(0.5f); w += 0.5f;
            }
        }
        zakoMax = 0; w = 0f;
        while (flow.Current != LastDungeonFlow.State.Silence && w < 15f) { zakoMax = Mathf.Max(zakoMax, ActiveEnemies()); yield return null; w += Time.deltaTime; }
        w = 0f; int bossesMax = 0;
        while (w < 6f) { zakoMax = Mathf.Max(zakoMax, ActiveEnemies()); bossesMax = Mathf.Max(bossesMax, ActiveBosses()); yield return null; w += Time.deltaTime; }
        string bgm = AudioManager.Instance != null ? AudioManager.Instance.GetComponent<BgmDirector>()?.Reason : "-";
        L($"  B: state={flow.Current} at {gm.MaxDistance:F0}m zakoMax={zakoMax} bossesMax={bossesMax} suppressGates={BossManager.SuppressGates} bgm={bgm} callouts={flow.CalloutsShown}");
        Check(flow.Current == LastDungeonFlow.State.Silence && zakoMax == 0 && bossesMax == 0 && BossManager.SuppressGates, "B: 99km warp -> silent section (no enemies, no bosses, gates suppressed)");

        // ---- C/D: 三姉妹 → 撃破 → エンドロール
        yield return Launch(EndgameDebug.Point.ReaperSisters);
        ReaperFinaleBattle.DebugHpOverride = 12; // 撃破までを短く(各姉妹のHPを小さく)
        flow = LastDungeonFlow.Instance;
        yield return Finale(flow); // C(開始)/D(3人ソロ→同時→撃破、RESULTへ行かない)
        w = 0f;
        while (flow.Current != LastDungeonFlow.State.Credits && w < 10f) { yield return null; w += Time.deltaTime; }
        Check(flow.Current == LastDungeonFlow.State.Credits && !gm.IsGameOver, "D: after defeating the sisters -> credits (no RESULT)");

        // ---- E/F: エンドロール → ONE MORE MILE? → YES
        yield return Launch(EndgameDebug.Point.EndingCredits);
        flow = LastDungeonFlow.Instance;
        Check(flow.Current == LastDungeonFlow.State.Credits && flow.Credits != null && ReaperFinaleBattle.Instance == null, "E: ENDING CREDITS warp starts the credits directly (no sisters fight)");
        yield return CreditsAndChoice(flow, true); // 文字に乗る/攻撃/石板/END → ONE MORE MILE? → YES/NOを途中まで → YESで走り続ける(F)

        // ---- G: ONE MORE MILE? → NO → 減速 → 停止 → ホーム
        yield return Launch(EndgameDebug.Point.OneMoreMile);
        flow = LastDungeonFlow.Instance;
        yield return ChoiceOnly(flow, false);
        var speeds = new List<float>();
        float t0 = Time.time; bool stopped = false;
        var oldGm = gm;
        while (Time.time - t0 < 20f && GameManager.Instance == oldGm && !gm.QuietFinish) { speeds.Add(pc.CurrentAutoRunSpeed); if (flow.StoppedAtDistance > 0f) stopped = true; yield return null; }
        float maxRun = speeds.Count > 0 ? speeds.Max() : 0f;
        L($"  G: NO -> ran up to {maxRun:F1}m/s, stopped={stopped}, quietFinish={gm.QuietFinish}");
        Check(maxRun > 3f && stopped && gm.QuietFinish, "G: NO -> runs a little, slows down, stops (formal quiet finish)");
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == oldGm) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        note();
        Check(GameManager.Instance != null && GameManager.Instance != oldGm && !GameManager.Instance.HasStarted && !DebugRun.IsActive, "G: faded out to HOME and the DEBUG RUN ended");
        gm = GameManager.Instance;

        // ---- H: 進行が変わっていない
        var after = ProgressPrint();
        string diff = DiffPrint(before, after);
        L($"  H: progress after the debug runs: lifetime {life0:F0} -> {ProgressStats.LifetimeDistance:F0}, lcBest {best0:F0} -> {gm.GetStageBest(LastCorridorDirector.StageId):F0}, mile {mile0} -> {gm.TotalOwnedMile}, continue {cont0} -> {RunCheckpoint.HasActiveRun}, restored-at-end {DebugRun.LastRestoredKeys}");
        if (diff != "") L("  H: DIFF " + diff);
        Check(diff == "", "H: no progress key changed (BEST / lifetime / MILE / cards / unlocks / sisters met / final dungeon / CONTINUE)");
        Check(DebugRun.LastRestoredKeys == 0, "H: nothing had to be restored at the end (every save was blocked at the source)");
        // 落ちた場合の復旧: DEBUG RUN 中に進行が書かれた状態で「次の起動」の復旧処理 → 元へ戻る
        DebugRun.Begin("crash-recovery test");
        PlayerPrefs.SetInt("TotalOwnedMile", mile0 + 777);
        PlayerPrefs.SetString(SaveKeys.LifetimeDistance, "123456789");
        PlayerPrefs.Save();
        DebugRun.RecoverAfterCrash();
        int mileNow = PlayerPrefs.GetInt("TotalOwnedMile", -1);
        DebugRun.End("crash-recovery test");
        var afterCrash = ProgressPrint();
        L($"  H: crash recovery: MILE {mile0 + 777} -> {mileNow}, diff '{DiffPrint(before, afterCrash)}'");
        Check(mileNow == mile0 && DiffPrint(before, afterCrash) == "", "H: a DEBUG RUN that never ended is rolled back on the next boot");

        // ---- I: 同じワープを5回(前の状態を持ち込まない、二重にならない)
        var audioCounts = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            if (i == 2 && gm != null && gm.HasStarted)
            {
                // わざと「時間停止 + レベルアップの選択画面」の状態にしてから次のワープ
                typeof(GameManager).GetMethod("RunLevelUpChoice", NP).Invoke(gm, null); // ボス戦中でも開く(後回しにしない)
                L($"   (before launch #{i + 1}: choice open={gm.IsLocalChoiceOpen}, ts={Time.timeScale:F2})");
            }
            yield return Launch(EndgameDebug.Point.ReaperSisters);
            flow = LastDungeonFlow.Instance;
            float w2 = 0f;
            while (flow.Current != LastDungeonFlow.State.Finale && w2 < 15f) { yield return null; w2 += Time.deltaTime; }
            w2 = 0f;
            while (w2 < 4f) { yield return null; w2 += Time.deltaTime; }
            int sisters = FindObjectsByType<ReaperSisterBoss>(FindObjectsSortMode.None).Count(b => b != null && b.gameObject.activeInHierarchy);
            int finales = FindObjectsByType<ReaperFinaleBattle>(FindObjectsSortMode.None).Length;
            int flows = FindObjectsByType<LastDungeonFlow>(FindObjectsSortMode.None).Length;
            int gms = FindObjectsByType<GameManager>(FindObjectsSortMode.None).Length;
            int audio = CountLoopingAudio(); audioCounts.Add(audio);
            L($"  I sisters #{i + 1}: state={flow.Current} sisters={sisters} finales={finales} flows={flows} gameManagers={gms} loopingAudio={audio} [{AudioLine()}] ts={Time.timeScale:F2}");
            Check(flow.Current == LastDungeonFlow.State.Finale && sisters == 1 && finales == 1 && flows == 1 && gms == 1, $"I: sisters launch #{i + 1}: exactly one fight / one sister on stage");
        }
        Check(audioCounts.Distinct().Count() == 1, $"I: the same number of looping sounds every time ({string.Join(",", audioCounts)}) - no doubled BGM");
        for (int i = 0; i < 5; i++)
        {
            yield return Launch(EndgameDebug.Point.OneMoreMile);
            flow = LastDungeonFlow.Instance;
            float w2 = 0f;
            while ((flow.Choice == null || !flow.Choice.Risen) && w2 < 15f) { yield return null; w2 += Time.deltaTime; }
            var ch = flow.Choice;
            int choices = FindObjectsByType<OneMoreMileChoice>(FindObjectsSortMode.None).Length;
            int words = FindObjectsByType<ChoiceWord>(FindObjectsSortMode.None).Length;
            L($"  I choice #{i + 1}: choices={choices} words={words} YES {ch?.Yes.Hits}/{ch?.Yes.MaxHp} NO {ch?.No.Hits}/{ch?.No.MaxHp} decided={ch?.Decided} loopingAudio={CountLoopingAudio()}");
            Check(ch != null && choices == 1 && words == 2 && ch.Yes.Hits == 0 && ch.No.Hits == 0 && !ch.Decided, $"I: choice launch #{i + 1}: one YES/NO pair, both at full HP, undecided");
            if (i == 1) yield return AttackTimes(PlayerController.FlickDirection.Forward, 2); // 途中まで壊してから次のワープ → 次は満タンから
        }

        // ---- 終わり: ホームへ戻って DEBUG RUN を終える
        var g2 = gm;
        gm.ReturnToHome();
        float w3 = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == g2) && w3 < 15f) { yield return null; w3 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        var after2 = ProgressPrint();
        string diff2 = DiffPrint(before, after2);
        L($"  H(final): DebugRun={DebugRun.IsActive} restored-at-end={DebugRun.LastRestoredKeys} diff='{diff2}'");
        Check(!DebugRun.IsActive && diff2 == "", "H: after all warps and returning home, progress is exactly as before");

        // ---- J: 通常のラン(DEBUG RUN ではない)は従来どおり保存する
        double lifeJ = ProgressStats.LifetimeDistance;
        keepAlive = true; StartCoroutine(KeepAlive()); // 通常のランの保存を見るだけなので、倒れないようにする(テストのボットは壁を跳ばない)
        var assist = HighSpeedAssist.Instance;
        if (assist != null) { assist.SetEnabled(true); assist.engageKmh = 1f; assist.releaseKmh = 0.5f; assist.fullAssistKmh = 2f; } // 壁/穴は補助で越える(保存はしない設定変更)
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null);
        float w4 = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w4 < 20f) { yield return null; w4 += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        w4 = 0f;
        while (gm.MaxDistance < 250f && w4 < 60f && !gm.IsGameOver) { yield return null; w4 += Time.deltaTime; }
        L($"  J: normal run reached {gm.MaxDistance:F0}m, DebugRun={DebugRun.IsActive}, lifetime {lifeJ:F0} -> {ProgressStats.LifetimeDistance:F0}");
        double grew = ProgressStats.LifetimeDistance - lifeJ;
        Check(!DebugRun.IsActive && gm.MaxDistance > 20f && grew >= gm.MaxDistance * 0.8, $"J: a normal run is recorded as before (lifetime +{grew:F0}m for a {gm.MaxDistance:F0}m run)");
        keepAlive = false;

        // テスト機のセーブを元へ
        SaveSystem.Restore(machine);
        L("[warps] test machine save restored");
    }

    // ONE MORE MILE? だけ(エンドロールを通らずに始めた時)
    IEnumerator ChoiceOnly(LastDungeonFlow flow, bool chooseYes)
    {
        float w = 0f;
        while ((flow.Choice == null || !flow.Choice.Risen) && w < 20f) { yield return null; w += Time.deltaTime; }
        var choice = flow.Choice;
        Check(choice != null && choice.Risen && flow.Credits == null, "ONE MORE MILE? warp: the choice area appears directly (no credits road)");
        if (choice == null) yield break;
        Check(!pc.autoRunEnabled && pc.IsStandingIdle && choice.Yes.Hits == 0 && choice.No.Hits == 0 && !choice.Decided, "choice: the player stands, YES/NO at full HP, undecided");
        yield return AttackTimes(PlayerController.FlickDirection.Forward, 3);
        yield return AttackTimes(PlayerController.FlickDirection.Backward, 3);
        L($"  choice: YES {choice.Yes.Hits}/{choice.Yes.MaxHp}, NO {choice.No.Hits}/{choice.No.MaxHp} after 3 + 3 attacks");
        Check(choice.Yes.Hits >= 2 && choice.No.Hits >= 2 && !choice.Decided, "choice: YES and NO can both be damaged part way");
        var dirF = chooseYes ? PlayerController.FlickDirection.Forward : PlayerController.FlickDirection.Backward;
        float wt = 0f;
        while (!choice.Decided && wt < 30f) { yield return AttackTimes(dirF, 1); wt += 0.5f; }
        var other = chooseYes ? choice.No : choice.Yes;
        int otherHits = other.Hits;
        yield return AttackTimes(chooseYes ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward, 2);
        Check(choice.Decided && choice.ChoseYes == chooseYes && other.Hits == otherHits, $"choice: the last broken word decides ({(chooseYes ? "YES" : "NO")}) and the other is locked");
    }
}
#endif

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// RUN BUILD HUD(取得カード一覧)の自動確認(2026-09-29)。
//  Editor: Tools/OneMoreMile/Run Build HUD Test (batch) → RunBuildHudAutoTest.txt(状態/配置の確認)
//  Player: 起動引数 -runBuildHudTest → 同じ確認 + 3ステージ(荒野/洞窟/天空)の画面をRunBuildHudShots/へ撮影
//          (IMGUIはbatchmodeでは描かれないので、見た目は実行ファイルで撮る)
public class RunBuildHudAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        bool run = System.Environment.GetCommandLineArgs().Contains("-runBuildHudTest");
#if UNITY_EDITOR
        if (EditorPrefs.GetInt("RunBuildHudTest", 0) == 1) { EditorPrefs.SetInt("RunBuildHudTest", 0); run = true; }
#endif
        if (!run || FindFirstObjectByType<RunBuildHudAutoTest>() != null) return;
        Debug.Log("[RunBuildHudTest] boot");
        Application.runInBackground = true; // 実行ファイルの窓にフォーカスが無くても進める(撮影用)
        var go = new GameObject("RunBuildHudTest");
        DontDestroyOnLoad(go);
        go.AddComponent<RunBuildHudAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures; bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[RunBuildHudTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    GameManager gm; RunBuildHud hud;
    RunCheckpoint.Data savedCheckpoint;
    bool player => !Application.isEditor;
    string shotDir;
    int shotN;

    IEnumerator Watchdog() { yield return new WaitForSecondsRealtime(600f); L("WATCHDOG: test did not finish in time"); Finish(); }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        savedCheckpoint = RunCheckpoint.Load();
        shotDir = System.IO.Path.Combine(Application.dataPath, "../RunBuildHudShots");
        if (player) { System.IO.Directory.CreateDirectory(shotDir); foreach (var f in System.IO.Directory.GetFiles(shotDir, "*.png")) System.IO.File.Delete(f); }
        yield return new WaitForSecondsRealtime(1.5f);
        L($"screen {Screen.width}x{Screen.height} player={player}");

        // ---- 1本目(荒野): 状態の確認一式 ----
        yield return BeginRun("wasteland_road");
        yield return MainChecks();
        // ---- Game Over(Result) → Retry(Home) ----
        yield return GameOverAndHome();
        // ---- 2本目(洞窟): 前のRunのカードが残っていない / 途中でReturn To Home ----
        yield return BeginRun("natural_cave");
        Check(hud.Slots.Count == CountRunCards(), $"next run starts clean (slots {hud.Slots.Count} = this run's cards {CountRunCards()}, previous run's 12 cards are gone)");
        yield return AddCards(7);
        yield return Shot("cave_7cards");
        yield return SpeedShot("cave_fast");
        yield return ReturnHome();
        // ---- 3本目(天空): 見た目の撮影 + Level Up選択中 ----
        yield return BeginRun("sky_corridor");
        yield return AddCards(9);
        yield return Shot("sky_9cards");
        yield return LevelUpCheck("sky");
        yield return GameOverAndHome();
        Finish();
    }

    // ===================================================================== //
    int CountRunCards() { var ids = new List<string>(); GameManager.Instance.CollectRunCardIds(ids); return ids.Count; }

    IEnumerator BeginRun(string stage)
    {
        L($"\n===== run on {stage} =====");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stage);
        // 画面切り替えの演出中はStartGameが何もしないので、始まるまで呼び直す
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        Check(gm.HasStarted, $"run started on {stage} (stage={gm.ActiveRunStageId})");
        hud = RunBuildHud.Instance;
        // 無敵(DebugSetInvincibleはEditor専用なので、実行ファイルでも使えるようにプロパティを直接設定)
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f); // 自然なLevel Upを止めて、取得を手で制御する
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        yield return new WaitForSeconds(0.3f);
    }

    List<string> cardPool;
    List<string> Pool()
    {
        if (cardPool == null)
            cardPool = CardDatabase.AllCards.Where(c => c != null && c.icon != null && !CardVariant.IsVariantKey(c.cardId)).Select(c => c.cardId).ToList();
        return cardPool;
    }

    // カード取得の正規の処理(Level Up/Boss Rewardで選んだ時と同じ)を呼ぶ
    void Acquire(string id) => typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP).Invoke(gm, new object[] { id });

    IEnumerator AddCards(int distinct)
    {
        var have = new List<string>(); gm.CollectRunCardIds(have);
        foreach (string id in Pool().Where(p => !have.Contains(p)).Take(distinct)) { Acquire(id); yield return null; }
        // 何枚かはLvを上げる
        var now = new List<string>(); gm.CollectRunCardIds(now);
        for (int i = 0; i < now.Count; i += 3) Acquire(now[i]);
        yield return new WaitForSecondsRealtime(0.6f); // 追加の演出が終わってから撮る
    }

    bool SlotsMatchState(out string info)
    {
        var ids = new List<string>(); gm.CollectRunCardIds(ids);
        var sb = new StringBuilder(); bool ok = ids.Count == hud.Slots.Count;
        for (int i = 0; i < hud.Slots.Count; i++)
        {
            var s = hud.Slots[i];
            int lv = Mathf.Max(1, gm.GetRunCardLevel(s.cardId));
            if (i >= ids.Count || ids[i] != s.cardId || s.level != lv) ok = false;
            sb.Append($"{s.cardId}:{s.level} ");
        }
        info = sb.ToString();
        return ok;
    }

    IEnumerator MainChecks()
    {
        var rects = new List<Rect>();
        // 1. Run開始直後: このRunのカード(Character Card)だけ
        int start = CountRunCards();
        Check(hud != null && hud.Slots.Count == start, $"run start: HUD shows exactly this run's starting cards ({hud?.Slots.Count} / {start}; character cards={string.Join(",", gm.CharacterCardIds.Where(x => !string.IsNullOrEmpty(x)))})");
        Check(start > 0 || !hud.ShouldShow(out _), "no empty slots are shown when there are no cards");

        // 2. 新規取得 → 追加+演出
        string a = Pool().First(p => hud.Slots.All(s => s.cardId != p));
        Acquire(a);
        yield return null; yield return null;
        var sa = hud.Slots.FirstOrDefault(s => s.cardId == a);
        Check(sa != null && sa.level == 1, $"new card {a} is added with Lv1");
        Check(sa != null && Time.unscaledTime - sa.addedAt < 0.5f, "new card plays the add feedback");
        Check(hud.ShouldShow(out bool c0) && !c0, "HUD is shown during normal running");
        int idxA = hud.Slots.ToList().IndexOf(sa);
        hud.ComputeLayout(hud.Slots.Count, false, rects, out _, out _);
        Rect rA = rects[idxA];

        // 3/4. 同じカードを再取得 → Slotは増えずLvだけ上がる
        int before = hud.Slots.Count;
        Acquire(a); yield return null; yield return null;
        Check(hud.Slots.Count == before && sa.level == 2, $"re-acquiring {a}: no new slot ({hud.Slots.Count}), Lv 1 -> {sa.level}");
        Check(Time.unscaledTime - sa.levelUpAt < 0.5f, "Lv number plays the level-up feedback");
        Acquire(a); yield return null; yield return null;
        Check(SlotsMatchState(out string info), $"Lv shown = actual card state (GetRunCardLevel): {info}");

        // 5/6/7. 多数取得: 並びは取得順のまま、5個で折り返し、中央へ伸びない
        var order0 = hud.Slots.Select(s => s.cardId).ToList();
        foreach (string id in Pool().Where(p => hud.Slots.All(s => s.cardId != p)).Take(11 - hud.Slots.Count)) { Acquire(id); yield return null; }
        yield return null;
        Acquire(order0[0]); yield return null; yield return null; // 途中で最初のカードのLvを上げても
        var order1 = hud.Slots.Select(s => s.cardId).ToList();
        Check(order1.Take(order0.Count).SequenceEqual(order0), "acquisition order never changes (earlier slots keep their place after more cards and level ups)");
        Check(SlotsMatchState(out info), $"11 cards, levels match: {info}");
        hud.ComputeLayout(hud.Slots.Count, false, rects, out Rect grid, out float size);
        L($"  grid {grid} slot {size}px, rows {Mathf.CeilToInt(hud.Slots.Count / 5f)}");
        Check(rects[idxA] == rA, "a slot's position does not move when more cards are added");
        int maxInRow = rects.GroupBy(r => Mathf.RoundToInt(r.y)).Max(g => g.Count());
        Check(maxInRow <= 5 && rects.Select(r => Mathf.RoundToInt(r.y)).Distinct().Count() == 3, $"compact grid: max 5 per row ({maxInRow}), 11 cards -> 3 rows");
        Check(grid.xMin > Screen.width * 0.6f, $"grid stays at the right edge (left edge {grid.xMin:F0}px of {Screen.width}px, never toward the center)");
        Check(grid.yMax < Screen.height * 0.56f, $"grid stays in the upper part (bottom {grid.yMax:F0}px of {Screen.height}px)");
        bool overlapTop = false;
        Rect hearts = HeartsRect();
        foreach (var r in rects) if (r.Overlaps(hearts)) overlapTop = true;
        Check(!overlapTop, $"does not overlap the HP panel {hearts}");
        bool overlapPause = false; Rect pause = PauseRect();
        foreach (var r in rects) if (r.Overlaps(pause)) overlapPause = true;
        Check(!overlapPause, "does not overlap the pause button");
        for (int k = 0; k < 4; k++) { string id = Pool().Where(p => hud.Slots.All(s => s.cardId != p)).First(); Acquire(id); yield return null; }
        hud.ComputeLayout(hud.Slots.Count, false, rects, out grid, out size);
        Check(grid.xMin > Screen.width * 0.6f && grid.yMax < Screen.height * 0.56f, $"15 cards still fit the corner area (grid {grid}, slot {size}px)");
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Shot("wasteland_15cards");

        // 9. 高速走行でも位置は同じ(Screen Space)
        hud.ComputeLayout(hud.Slots.Count, false, rects, out Rect g1, out _);
        float spd = PlayerController.DebugSpeedScale;
        PlayerController.DebugSpeedScale = 6f;
        yield return new WaitForSeconds(1.5f);
        hud.ComputeLayout(hud.Slots.Count, false, rects, out Rect g2, out _);
        Check(g1 == g2 && hud.ShouldShow(out _), $"high speed ({PlayerController.Instance.CurrentAutoRunSpeed * 3.6f:F0} km/h): HUD rect unchanged");
        yield return Shot("wasteland_fast");
        PlayerController.DebugSpeedScale = spd;

        // 15. Pause / Resume
        var owner = new object();
        int cnt = hud.Slots.Count;
        TimeControl.Pause(owner);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(hud.ShouldShow(out _) && hud.Slots.Count == cnt, "paused: HUD still shows the build");
        TimeControl.Resume(owner);
        yield return null;
        string b = Pool().First(p => hud.Slots.All(s => s.cardId != p));
        Acquire(b); yield return null; yield return null;
        Check(hud.Slots.Count == cnt + 1 && SlotsMatchState(out info), "after resume: acquiring still updates the HUD");

        // 11/12. Level Up選択中
        yield return LevelUpCheck("wasteland");
    }

    Rect HeartsRect()
    {
        float width = Mathf.Clamp(70f + gm.maxLives * 34f, 220f, 420f);
        Rect safe = Screen.safeArea;
        return new Rect(Screen.width - (Screen.width - (safe.x + safe.width)) - 28f - width, Screen.height - (safe.y + safe.height) + 28f, width, 54f);
    }
    Rect PauseRect()
    {
        Rect safe = Screen.safeArea;
        return new Rect(Screen.width - (Screen.width - (safe.x + safe.width)) - 28f - 52f, Screen.height - safe.y - 28f - 52f, 52f, 52f);
    }

    IEnumerator LevelUpCheck(string tag)
    {
        L($"[Level Up choice ({tag})]");
        var before = new List<string>(); gm.CollectRunCardIds(before);
        var lvBefore = before.ToDictionary(x => x, x => gm.GetRunCardLevel(x));
        int slotsBefore = hud.Slots.Count;
        typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
        float w = 0f;
        while (!gm.IsRewardSequenceWaitingForSelection && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
        Check(gm.IsRewardSequenceWaitingForSelection, "level-up choice opened");
        bool shown = hud.ShouldShow(out bool compact);
        Check(shown && compact, "during the choice the current build stays visible (compact)");
        var rects = new List<Rect>();
        var avoid = new List<Rect>(hud.ChoiceAvoidRects());
        hud.ComputeLayout(hud.Slots.Count, true, rects, out Rect grid, out float cs, avoid);
        // 3択カード/説明パネル(uGUI、Screen Space Overlay)の画面上の範囲と重ならない
        bool overlap = false; var sb = new StringBuilder();
        foreach (var sr in avoid) { sb.Append($"{sr} "); foreach (var r in rects) if (sr.Overlaps(r)) overlap = true; }
        L($"  compact slot {cs}px");
        L($"  HUD grid {grid} vs choice UI {sb}");
        Check(!overlap && avoid.Count >= 3, $"HUD does not overlap the 3 cards / detail panel ({avoid.Count} areas)");
        Check(hud.Slots.Count == slotsBefore, "HUD does not change while choosing");
        yield return Shot(tag + "_levelup_choice");
        var seq = FindFirstObjectByType<RewardCardSequence>();
        seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.2f); seq.OnCardClicked(0);
        w = 0f;
        while ((gm.IsRewardSequenceRunning || gm.IsLocalChoiceOpen) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return null; yield return null;
        var after = new List<string>(); gm.CollectRunCardIds(after);
        string picked = after.Except(before).FirstOrDefault() ?? after.FirstOrDefault(x => gm.GetRunCardLevel(x) > lvBefore[x]);
        var ps = hud.Slots.FirstOrDefault(s => s.cardId == picked);
        bool isNew = !before.Contains(picked ?? "");
        string info = ""; bool match = ps != null && SlotsMatchState(out info);
        Check(match, $"after the choice the picked card ({picked}, {(isNew ? "new" : "level up")}) is reflected: {info}");
        Check(ps != null && (isNew ? Time.unscaledTime - ps.addedAt < 0.6f : Time.unscaledTime - ps.levelUpAt < 0.6f), "the pick plays its feedback when running resumes");
        yield return new WaitForSecondsRealtime(0.05f);
        yield return Shot(tag + "_after_pick");
    }

    IEnumerator GameOverAndHome()
    {
        L("[Game Over -> Result -> Home]");
        gm.Win();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(gm.IsGameOver && !hud.ShouldShow(out _), "Result screen: HUD is hidden");
        yield return Shot("result");
        var old = gm;
        gm.Retry();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        gm = GameManager.Instance; hud = RunBuildHud.Instance;
        Check(gm != null && !gm.HasStarted && hud != null && hud.Slots.Count == 0 && !hud.ShouldShow(out _), "Home after the run: no HUD, no slots left");
    }

    IEnumerator ReturnHome()
    {
        L("[Return To Home mid-run]");
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance; hud = RunBuildHud.Instance;
        Check(gm != null && !gm.HasStarted && hud != null && hud.Slots.Count == 0 && !hud.ShouldShow(out _), "Return To Home: no HUD, no slots left");
        yield return Shot("home");
    }

    IEnumerator SpeedShot(string name)
    {
        float spd = PlayerController.DebugSpeedScale;
        PlayerController.DebugSpeedScale = 6f;
        yield return new WaitForSeconds(1.2f);
        yield return Shot(name);
        PlayerController.DebugSpeedScale = spd;
    }

    IEnumerator Shot(string name)
    {
        if (!player) yield break;
        yield return new WaitForEndOfFrame();
        string path = System.IO.Path.Combine(shotDir, $"{shotN++:00}_{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        yield return null; yield return null;
        L($"  [shot] {System.IO.Path.GetFileName(path)}");
    }

    void Finish()
    {
        PlayerController.DebugSpeedScale = 1f;
        if (savedCheckpoint != null) { if (savedCheckpoint.active) RunCheckpoint.Save(savedCheckpoint); else RunCheckpoint.Clear(); } // テストで作った中断データを戻す
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../RunBuildHudAutoTest.txt"), log.ToString());
#if UNITY_EDITOR
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
#else
        Application.Quit(failures == 0 && !anyException ? 0 : 1);
#endif
    }
}

#if UNITY_EDITOR
public static class RunBuildHudTestMenu
{
    [MenuItem("Tools/OneMoreMile/Run Build HUD Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("RunBuildHudTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif

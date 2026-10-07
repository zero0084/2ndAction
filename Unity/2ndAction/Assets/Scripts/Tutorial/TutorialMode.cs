using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 初回チュートリアル(2026-10-07)。
//  ・操作の練習(TutorialRun): 平地・低速・ダメージなし・時間制限なしの練習区画。黒剣士の基準の強さで行う
//    (選んでいるキャラ/デッキ/キャラカードは変えない・書かない)。報酬/記録/所持/解放/累計距離/CONTINUE は一切書かない
//    (DebugRun.WritesBlocked に Active を足してある。闘技場と同じ止め方)。
//  ・入口: ① 新規データで初めて扉を押した時の「操作を練習する/そのまま始める」 ② 設定の「遊び方」(何度でも)
//  ・出口: ① の時は練習の後にそのまま通常のランの出発(ステージ選択)へ。② の時は元の画面(ホーム)へ戻る。中断中のランは消さない。
//  ・マルチプレイ中は出さない。
public static class TutorialMode
{
    public static bool Active { get; private set; }
    public static bool PendingStart { get; private set; }
    public static bool FirstRunFlow { get; private set; }   // 初回の扉から来た(終わったら通常のランの出発へ)
    public const float SpeedKmh = 11f;                       // 練習中の自動前進(ゆっくり)
    public static float SpeedMps => SpeedKmh / GameManager.KmhPerMps;

    public static void BeginPending(bool firstRun) { PendingStart = true; FirstRunFlow = firstRun; }
    public static void Begin() { Active = true; PendingStart = false; }
    public static void End() { Active = false; PendingStart = false; }

    // 練習を始めてよい場所か(ホームで、マルチ/闘技場/他の起動の途中でない)
    public static bool CanLaunchFromHome
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && !gm.HasStarted && !ArenaMode.Active && !ArenaMode.PendingStart && !ArenaLauncher.Pending
                && !NetSession.IsActive && !Active && !PendingStart && TutorialLauncher.Instance != null && !TutorialLauncher.Instance.Launching;
        }
    }
}

// チュートリアルの進み(テスト用データ/通常のデータごと。SaveKeys の「進行」に登録)
public static class TutorialProgress
{
    public const string InitKey = "Tutorial.V1";               // この仕組みを入れた印
    public const string OfferedKey = "Tutorial.Offered";       // 初回の「練習する/そのまま始める」を出した
    public const string PracticeDoneKey = "Tutorial.PracticeDone"; // 練習を最後まで(またはスキップ)
    public const string EscapeGuideKey = "Tutorial.EscapeGuide";   // 初めてのボス報酬の後の「脱出」の説明を出した
    public const string MileGuideKey = "Tutorial.MileGuide";       // 0=まだ / 1=初めての脱出をした(ホームで出す) / 2=出した

    static int Get(string k) => SaveStore.GetInt(k, 0);
    static void Set(string k, int v) { SaveStore.SetInt(k, v); SaveStore.Save(); }

    public static bool Offered => Get(OfferedKey) != 0;
    public static bool PracticeDone => Get(PracticeDoneKey) != 0;
    public static bool EscapeGuideShown => Get(EscapeGuideKey) != 0;
    public static int MileGuide => Get(MileGuideKey);

    public static void MarkOffered() => Set(OfferedKey, 1);
    public static void MarkPracticeDone() => Set(PracticeDoneKey, 1);
    public static void MarkEscapeGuideShown() => Set(EscapeGuideKey, 1);
    public static void MarkMileGuidePending() { if (MileGuide == 0) Set(MileGuideKey, 1); }
    public static void MarkMileGuideShown() => Set(MileGuideKey, 2);

    // 初回の扉で練習を勧めるか: このデータで一度も勧めておらず、まだ走ったことが無い(新規のデータ)
    public static bool ShouldOfferOnDoor =>
        !Offered && !PracticeDone && !RunCheckpoint.HasActiveRun && !NetSession.IsActive && ProgressStats.LifetimeDistance < 1.0;

    // セーブの起動時処理から(SaveSystem.Boot)。この仕組みより前からあるデータ(既に遊んでいる人)には、
    // 初回の案内を出さない(全部「出した」にする)。新規/走ったことが無いデータは何もしない。
    public static string EnsureInitialized()
    {
        if (SaveStore.HasKey(InitKey)) return "";
        SaveStore.SetInt(InitKey, 1);
        bool played = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance) > 0.0 || SaveStore.GetFloat("BestDistance", 0f) > 0f;
        if (played)
        {
            SaveStore.SetInt(OfferedKey, 1);
            SaveStore.SetInt(PracticeDoneKey, 1);
            SaveStore.SetInt(EscapeGuideKey, 1);
            SaveStore.SetInt(MileGuideKey, 2);
        }
        return played ? "tutorial: existing player -> first-time guides marked as shown; " : "tutorial: new data -> first-time guides enabled; ";
    }
}

// 練習の起動/終了(闘技場の起動と同じ作り: 毎回シーンを読み直して、覆いの下で作り替える)
public class TutorialLauncher : MonoBehaviour
{
    public static TutorialLauncher Instance { get; private set; }
    static bool pending;
    static bool exitToStageSelect;
    public bool Launching { get; private set; }
    public static bool Covering { get; private set; }
    static readonly object coverTimeOwner = new object();
    static GUIStyle coverStyle;
    public static int Launches { get; private set; }      // 確認用

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[TutorialLauncher]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<TutorialLauncher>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // firstRun = 初回の扉から(終わったら通常のランの出発へ)/ false = 設定の「遊び方」から(終わったらホームへ)
    public static bool Launch(bool firstRun, string why)
    {
        if (Instance == null || !TutorialMode.CanLaunchFromHome) return false;
        Debug.Log($"[Tutorial] LAUNCH ({why}) firstRun={firstRun}");
        Launches++;
        ProgressStats.Flush(true); // 通常の進行の書きかけを先に書いておく
        SaveStore.Save();
        pending = true;
        TutorialMode.BeginPending(firstRun); // 読み直しの前から進行を書かない
        Covering = true;
        ResetStatics("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    // 練習を終える。toStageSelect = 終わったらそのまま通常のランの出発(ステージ選択)を開く
    public static void Exit(bool toStageSelect, string why)
    {
        Debug.Log($"[Tutorial] EXIT ({why}) toStageSelect={toStageSelect}");
        exitToStageSelect = toStageSelect;
        Covering = true;
        TutorialRun.Teardown();
        TutorialMode.End();
        ResetStatics("exit");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); // メモリ上の値(キャラ/強化/HP)は保存された値へ戻る
    }

    // 練習の中から「もう一度練習する」: ホームを見せずに、覆ったまま最初から作り直す
    public static void Relaunch(string why)
    {
        Debug.Log($"[Tutorial] RELAUNCH ({why})");
        bool first = TutorialMode.FirstRunFlow;
        Launches++;
        TutorialRun.Teardown();
        TutorialMode.End();
        ResetStatics("relaunch");
        pending = true;
        TutorialMode.BeginPending(first);
        Covering = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void ResetStatics(string why)
    {
        TimeControl.ResetAll();
        Time.timeScale = 1f;
        GameManager.BlockExpGain = false;
        GameManager.EscapeBlocked = false;
        BossManager.SuppressGates = false;
        UiInputGate.ClearLatch();
        Debug.Log($"[Tutorial] statics reset ({why})");
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (Instance == null) return;
        if (pending)
        {
            pending = false;
            TimeControl.Pause(coverTimeOwner);
            Instance.StartCoroutine(Instance.Run());
            return;
        }
        // 練習以外へ戻った(停止メニューの「ホームへ」等): 練習の状態を全部戻す
        if (TutorialMode.Active || TutorialMode.PendingStart)
        {
            TutorialRun.Teardown();
            TutorialMode.End();
            ResetStatics("left tutorial");
            SaveSystem.ReloadCaches();
        }
        if (exitToStageSelect) { exitToStageSelect = false; Instance.StartCoroutine(Instance.OpenStageSelectWhenReady()); }
        else if (Covering) Instance.StartCoroutine(Instance.UncoverWhenReady());
    }

    IEnumerator UncoverWhenReady()
    {
        yield return null; yield return null;
        EndCover();
    }

    IEnumerator OpenStageSelectWhenReady()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return null; yield return null;
        EndCover();
        var gm = GameManager.Instance;
        w = 0f;
        while (gm != null && ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        if (gm != null && !gm.HasStarted) gm.OpenStageSelect();
    }

    // 覆いは出したまま時間だけ動かす(覆いの下でカメラを新しい位置へ追いつかせる)
    public static void ReleaseCoverTime() { TimeControl.Resume(coverTimeOwner); }

    static void EndCover()
    {
        if (!Covering) return;
        Covering = false;
        TimeControl.Resume(coverTimeOwner);
        UiInputGate.LatchUntilRelease();
    }

    IEnumerator Run()
    {
        Launching = true;
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("no GameManager"); yield break; }
        yield return null;
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        TutorialMode.Begin();
        GameManager.BlockExpGain = true;
        GameManager.EscapeBlocked = true;
        BossManager.SuppressGates = true;
        gm.SetSelectedCharacter(TutorialRun.CharacterId); // 書き込みは止まっている(読み直しで元のキャラへ戻る)
        gm.ArenaStartRun("wasteland_road");
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("the run did not start"); TutorialMode.End(); yield break; }
        var run = new GameObject("TutorialRun").AddComponent<TutorialRun>();
        yield return run.Setup();
        yield return null;
        EndCover();
        Launching = false;
    }

    void Fail(string why)
    {
        Debug.LogError("[Tutorial] launch failed: " + why);
        EndCover();
        Launching = false;
    }

    void OnGUI()
    {
        if (!Covering) return;
        GUI.depth = -30000;
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.07f, 0.05f, 0.04f, 1f));
        float s = Mathf.Max(0.6f, Screen.height / 1080f);
        if (coverStyle == null) coverStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        coverStyle.fontSize = Mathf.RoundToInt(44 * s); coverStyle.normal.textColor = new Color(1f, 0.86f, 0.55f);
        int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;
        GUI.Label(new Rect(0, Screen.height * 0.5f - 40 * s, Screen.width, 80 * s), "準備中" + new string('.', dots), coverStyle);
        if (Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)) Event.current.Use();
    }
}

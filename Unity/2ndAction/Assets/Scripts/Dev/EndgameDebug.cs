#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// ラスダン終盤/エンディングの開発用ワープ(2026-10-02)。開発版(Editor / Development Build)だけ。リリース版ではこのファイルごと存在しない
// (DEBUGパネルのページも、ラン中の「DEBUG RUN」表示/ボタンも、起動経路も無い)。
//
// 使い方: ホームの DEBUG → 「ラスダン終盤…」。ラン中は画面左下の「DEBUG RUN」の ≡ から同じページを開ける。
// 各項目は毎回「シーンを読み直す → 時間/一時停止/入力/演出の状態を戻す → ラスダンを新しく始める → その地点へワープ」。
// 前回のボス/死神/敵/飛び道具/演出/UI/コルーチン/BGMの状態は、シーンの読み直しで全部消える(何度押しても二重にならない)。
//
// ワープしたランは Debug Run(DebugRun.IsActive)= 正式記録の対象外。BEST/MILE/カード/解放/累計距離/三姉妹の遭遇/ラスダン解放/
// 中断中のラン(CONTINUE)を保存しない(保存の入口で止める + 開始時の控えへ戻す。詳しくは DebugRun)。
// Debug Run はホームへ戻る(シーンの読み直し)か、アプリの終了で終わる。
public partial class EndgameDebug : MonoBehaviour
{
    public enum Point { LastDungeon0, LastDungeon90, LastDungeon99, ReaperSisters, EndingCredits, OneMoreMile, EndingFlow }
    public enum Profile { Normal, Sturdy, SturdyStrong }

    public static EndgameDebug Instance { get; private set; }
    public static Profile SelectedProfile = Profile.Sturdy;
    static Point? pending;
    static Profile pendingProfile;

    // 長距離の確認(2026-10-04): 選んだステージを新しく始めて、10/30/50/70/100km 付近へ。代表的なカード構成を付けられる。
    // ラスダン終盤と同じく DEBUG RUN(BEST / MILE / 累計距離 / 解放 / CONTINUE を保存しない)。雑魚の硬さを実機で見る用
    public enum LongBuild { None, Mix, Attack, Fire, Lightning }
    public static readonly float[] LongDistances = { 10000f, 30000f, 50000f, 70000f, 99000f };
    public static LongBuild SelectedLongBuild = LongBuild.None;
    public static string SelectedLongStage = "wasteland_road";
    static (string stage, float d, LongBuild build)? pendingLong;
    string currentLabel = "";
    public bool IsLongCheck { get; private set; }
    public static string LongBuildLabel(LongBuild b) => b switch
    {
        LongBuild.Mix => "一般 Lv9", LongBuild.Attack => "Attack特化 Lv9", LongBuild.Fire => "炎特化 Lv9", LongBuild.Lightning => "雷特化 Lv9", _ => "カードなし",
    };
    static string LongBuildPreset(LongBuild b) => b switch
    {
        LongBuild.Mix => "mix", LongBuild.Attack => "attack", LongBuild.Fire => "fire", LongBuild.Lightning => "lightning", _ => null,
    };
    public static string LongDistanceLabel(float d) => d >= 99000f ? "100km付近" : $"{d / 1000f:0}km";

    public static void LaunchLong(string stage, float d, LongBuild build, Profile prof)
    {
        if (Instance == null) return;
        if (Instance.Launching) { Debug.Log("[EndgameDebug] launch ignored (already launching)"); return; }
        pending = null;
        pendingLong = (stage, d, build); pendingProfile = prof;
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH long-distance {stage} {d:F0}m build={build} profile={prof} - reloading the scene for a clean start");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator RunLaunchLong(string stage, float d, LongBuild build, Profile prof)
    {
        Launching = true;
        IsLongCheck = true; IsUltimateTest = false;
        currentLabel = $"{LongDistanceLabel(d)} {stage} / {LongBuildLabel(build)}";
        Status = $"{currentLabel} を準備中…";
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("GameManager がありません"); yield break; }
        yield return null;
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        DebugRun.Begin("長距離 " + currentLabel + " / " + ProfileLabel(prof));
        CurrentProfile = prof;
        LaunchedRealtime = Time.realtimeSinceStartup;
        gm.DebugStartRunOnStage(stage);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); yield break; }
        ApplyProfile(prof);
        string preset = LongBuildPreset(build);
        if (preset != null)
            foreach (var id in CardBuildPresets.Find(preset)) { var c = CardDatabase.FindBaseById(id); if (c != null) gm.ApplyCardEffectsStacked(c, 9); }
        // 関門(1kmごと)の直後へ。100km はステージの節目(三姉妹など)の手前の 99km
        Warp(gm, d + 150f);
        Debug.Log($"[EndgameDebug] long-distance check: {currentLabel} at {d + 150f:F0}m | atk x{(PlayerController.Instance != null ? PlayerController.Instance.CardAttackFactor : 1f):F2} enemyHp(x1) {(DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(1f) : 0)}");
        Status = $"{currentLabel}({ProfileLabel(prof)})開始";
        Launching = false;
    }

    public Point CurrentPoint { get; private set; }
    public Profile CurrentProfile { get; private set; }
    public bool Launching { get; private set; }
    public string Status { get; private set; } = "";
    public void SetStatus(string s) => Status = s;
    public int Launches { get; private set; }
    public float LaunchedRealtime { get; private set; } = -1f;
    public static System.Action<Point> Started; // 自動テスト用: ワープまで終わった

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[EndgameDebug]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<EndgameDebug>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting += () => DebugRun.End("application quit");
    }

    public static string Label(Point p) => p switch
    {
        Point.LastDungeon0 => "LAST DUNGEON 0km",
        Point.LastDungeon90 => "LAST DUNGEON 90km",
        Point.LastDungeon99 => "LAST DUNGEON 99km",
        Point.ReaperSisters => "REAPER SISTERS BOSS",
        Point.EndingCredits => "ENDING CREDITS",
        Point.OneMoreMile => "ONE MORE MILE?",
        _ => "LAST ENDING FLOW",
    };

    public static string ProfileLabel(Profile p) => p == Profile.Normal ? "通常性能" : p == Profile.Sturdy ? "死亡しにくい" : "死亡しにくい+攻撃UP";

    // ワープ先(m)。0 = ワープしない(ラスダンの開始状態)
    public static float WarpDistance(Point p) => p switch
    {
        Point.LastDungeon90 => 89900f,   // ボスラッシュ(90,000m)の直前
        Point.LastDungeon99 => BossManager.ContinuousRush ? 98600f : 98950f, // 静寂区間(99,000m)の直前。走りながらのボスラッシュは 97/98km のボスが残った状態から(99kmの足止め→全滅→静寂)
        Point.EndingFlow => 98950f,      // 静寂 → 三姉妹 → エンドロール → ONE MORE MILE? の通し
        Point.ReaperSisters => 99950f,   // 100,000m(三姉妹戦)の直前
        Point.EndingCredits => 98900f,   // 静寂区間へ入ってから、三姉妹撃破後と同じ状態でエンドロールを始める
        Point.OneMoreMile => 98900f,     // 同上、ONE MORE MILE? から
        _ => 0f,
    };

    // ===================================================================== //
    // 開始
    // ===================================================================== //
    public static void Launch(Point p, Profile prof)
    {
        if (Instance == null) return;
        if (Instance.Launching) { Debug.Log("[EndgameDebug] launch ignored (already launching)"); return; }
        pending = p; pendingProfile = prof;
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH {Label(p)} profile={prof} - reloading the scene for a clean start");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (Instance == null) return;
        // 開発用の闘技場: 闘技場の起動以外でシーンを読み直したら(退出/保存を戻すための読み直し)、必ず試験の設定を戻す
        // 闘技場は ArenaLauncher が起動/退出で戻す(2026-10-06 正式版)。闘技場の起動中/中は触らない
        if (!ArenaLauncher.Pending && !ArenaMode.PendingStart && !ArenaMode.Active) ArenaController.ResetAll();
        bool endedDebugRun = DebugRun.IsActive;
        if (DebugRun.IsActive)
        {
            int n = DebugRun.End("scene reload (back to HOME / new launch)");
            if (n > 0)
            {
                Debug.LogWarning($"[EndgameDebug] {n} progress keys had changed during the Debug Run and were restored: {DebugRun.LastRestoreNote}");
                // 読み込み済みの値(GameManager等)も戻すため、もう一度だけ読み直す(次の起動予約があればそちらで読み直される)
                if (!pending.HasValue && !pendingLong.HasValue && !pendingUlt.HasValue && !pendingArena && !pendingCave.HasValue && !pendingSky.HasValue && !pendingFe.HasValue && !pendingCombo.HasValue) { SceneManager.LoadScene(s.buildIndex); return; }
            }
        }
        // 状態を戻すのは DEBUG RUN の前後だけ(普通のシーンの読み直しでは何も変えない: 他の開発用の設定/自動テストの速度などを残す)
        if (endedDebugRun || pending.HasValue || pendingLong.HasValue || pendingUlt.HasValue || pendingArena || pendingCave.HasValue || pendingSky.HasValue || pendingFe.HasValue || pendingCombo.HasValue) SafeReset(endedDebugRun ? "debug run ended" : "scene loaded for a launch");
        Instance.keepAlive = false;
        Instance.IsArena = false;
        Instance.IsCaveBossTest = false;
        Instance.IsFinalEvoTest = false;
        Instance.IsComboTest = false;
        if (TakePendingCave()) { }
        else if (TakePendingFe()) { }
        else if (TakePendingCombo()) { }
        else if (pendingArena)
        {
            pendingArena = false;
            Instance.StartCoroutine(Instance.RunLaunchArena());
        }
        else if (pendingUlt.HasValue)
        {
            var up = pendingUlt.Value;
            pendingUlt = null;
            Instance.StartCoroutine(Instance.RunLaunchUltimate(up.ch, up.lv, up.stage, up.boss));
        }
        else if (pendingLong.HasValue)
        {
            var lp = pendingLong.Value; var lprof = pendingProfile;
            pendingLong = null;
            Instance.StartCoroutine(Instance.RunLaunchLong(lp.stage, lp.d, lp.build, lprof));
        }
        else if (pending.HasValue)
        {
            var p = pending.Value; var prof = pendingProfile;
            pending = null;
            Instance.StartCoroutine(Instance.RunLaunch(p, prof));
        }
    }

    // 前のテストの状態を残さない: 時間/一時停止/ヒットストップ/速度の上書き/カメラ/曲の上書き/ラスダンの静的な状態
    static void SafeReset(string why)
    {
        FinalEvolution.DebugForceAwakened = false; // FINAL EVOLUTION TEST の AWAKENED の扱いを戻す
        ComboSystem.DebugForceAwakened = false;    // COMBO TEST の AWAKENED の扱いを戻す
        TimeControl.ResetAll();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        PlayerController.DebugSpeedScale = 1f;
        PlayerController.DebugRunOnlyScale = 1f;
        PlayerController.ScriptedSpeedCapMps = float.PositiveInfinity;
        CameraFollow.ScriptedOffsetX = 0f;
        if (UltimateArt.Instance != null) UltimateArt.Instance.ForceEnd("debug reset");
        ArenaController.ResetAll(); // 開発用の闘技場: 試験の設定(速度/無敵/操作アシスト/止めた仕組み)を戻す
        CameraFollow.UltimateZoom = 1f; CameraFollow.UltimateLookAhead = 0f;
        BgmDirector.ClearOverride();
        GameManager.EscapeBlocked = false;
        GameManager.BlockExpGain = false;
        BossManager.RushEnabled = false;
        BossManager.SuppressGates = false;
        BossManager.FinaleAt100k = null;
        WorldPlatforms.Clear();
        ReaperFinaleBattle.DebugHpOverride = 0;
        UiInputGate.DebugPanelOpen = false;
        Debug.Log($"[EndgameDebug] state reset ({why}): timeScale=1 pause/hitstop cleared, speed caps/camera/BGM override cleared");
    }

    IEnumerator RunLaunch(Point p, Profile prof)
    {
        Launching = true;
        Status = $"{Label(p)} を準備中…";
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("GameManager がありません"); yield break; }
        yield return null; // シーンの Start を待つ
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }

        DebugRun.Begin(Label(p) + " / " + ProfileLabel(prof));
        CurrentPoint = p; CurrentProfile = prof;
        currentLabel = Label(p);
        IsLongCheck = false; IsUltimateTest = false;
        LaunchedRealtime = Time.realtimeSinceStartup;
        gm.DebugStartRunOnStage(LastCorridorDirector.StageId);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ラスダンを開始できませんでした"); yield break; }

        ApplyProfile(prof);
        float d = WarpDistance(p);
        if (d > 0f) Warp(gm, d);
        Debug.Log($"[FinalDungeon] Enter {d:F0} (DEBUG RUN: {Label(p)}, {ProfileLabel(prof)}) | GM {gm.DebugStateLine()}");
        if (p == Point.LastDungeon99 && BossManager.ContinuousRush && BossManager.Instance != null)
        {
            // ラスダンの流れ(LastDungeonFlow)が RushEnabled を立てるまで待ってから、97km から始めた状態にする
            w = 0f;
            while (!BossManager.RushEnabled && w < 5f) { yield return null; w += Time.deltaTime; }
            BossManager.Instance.DebugStartRushLate(97);
        }

        if (p == Point.EndingCredits || p == Point.OneMoreMile)
        {
            // ワープ前に作った地形(スタート地点)を抜けて、静寂区間の平らな道(穴/障害物/敵なし)に入ってから始める
            w = 0f;
            while (gm.MaxDistance < d + 130f && w < 25f && !gm.IsGameOver) { yield return null; w += Time.deltaTime; }
            var flow = LastDungeonFlow.Instance;
            w = 0f;
            while ((flow == null || !flow.Enabled) && w < 5f) { yield return null; w += Time.deltaTime; flow = LastDungeonFlow.Instance; }
            if (flow == null || !flow.Enabled) { Fail("LastDungeonFlow が動いていません"); yield break; }
            if (p == Point.EndingCredits) flow.DebugBeginCredits(); else flow.DebugBeginChoice();
        }
        Status = $"{Label(p)}({ProfileLabel(prof)})開始";
        Launching = false;
        Started?.Invoke(p);
    }

    void Fail(string why)
    {
        Status = "失敗: " + why;
        Debug.LogError("[EndgameDebug] " + why);
        Launching = false;
    }

    public static void Warp(GameManager gm, float d)
    {
        gm.DebugWarpToDistance(d);
        gm.DebugResetDistanceExclusion();
        // スタート地点で既に出ていた敵/障害物を消す(ワープ先に古い区間の敵を持ち込まない)
        int enemies = 0, obstacles = 0;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) { if (e != null) { Destroy(e.gameObject); enemies++; } }
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) { if (o != null) { Destroy(o.gameObject); obstacles++; } }
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        Debug.Log($"[EndgameDebug] warped to {d:F0}m (cleared {enemies} enemies, {obstacles} obstacles from the start area)");
    }

    // ===================================================================== //
    // テスト用の性能
    // ===================================================================== //
    bool keepAlive;
    void ApplyProfile(Profile prof)
    {
        keepAlive = prof != Profile.Normal;
        if (keepAlive) StartCoroutine(KeepAlive());
        if (prof == Profile.SturdyStrong && PlayerController.Instance != null) PlayerController.Instance.AddAttackPower(120); // 10倍スケールで旧+12相当
        Debug.Log($"[EndgameDebug] profile {prof}: {(keepAlive ? "HP is refilled when it drops below 60%" : "normal")}{(prof == Profile.SturdyStrong ? ", attack +120" : "")}");
    }

    IEnumerator KeepAlive()
    {
        while (keepAlive)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.HasStarted && !gm.IsGameOver && gm.Lives < gm.MaxLives * 0.6f) gm.DebugRefillLives();
            yield return null;
        }
    }

    // ===================================================================== //
    // ボスラッシュ: 次のボス
    // ===================================================================== //
    public static void NextBoss() { if (Instance != null) Instance.StartCoroutine(Instance.NextBossRoutine()); }

    IEnumerator NextBossRoutine()
    {
        var gm = GameManager.Instance; var bm = BossManager.Instance;
        if (gm == null || bm == null || !gm.HasStarted || gm.IsGameOver || gm.ActiveRunStageId != LastCorridorDirector.StageId) { Status = "NEXT BOSS: ラスダンのラン中だけ使えます"; yield break; }
        // 走りながらのボスラッシュ(2026-10-06): 出ているボスを倒して次の増援へ(待機中が出る/無ければ次の節目を今すぐ予約)。ワープしない
        if (bm.CurrentBossEncounter != null && bm.CurrentBossEncounter.continuous)
        {
            string r = bm.DebugRushAdvance();
            Status = "NEXT BOSS: " + r;
            Debug.Log("[BossRush] DEBUG NEXT BOSS (run rush): " + r);
            yield break;
        }
        // 今の遭遇のボスを全員(まだ出ていない増援も)片付ける。フェニックスの復活などで残った分はもう一度(2026-10-05)
        int killed = bm.DebugFinishEncounter();
        float w = 0f;
        while (w < 0.6f) { yield return null; w += Time.unscaledDeltaTime; }
        if (bm.IsBossPhase && !bm.BossDefeatedThisPhase) killed += bm.DebugFinishEncounter();
        Debug.Log($"[BossRush] DEBUG next boss: cleared {killed} boss(es) of the encounter at {gm.MaxDistance:F0}m (gate {bm.RushGateK * 1000}m)");
        w = 0f;
        while (w < 1.4f) { yield return null; w += Time.unscaledDeltaTime; }
        // 関門が終わるまで(報酬のカード選択はプレイヤーが選ぶ)
        w = 0f;
        while ((bm.IsBossPhase || gm.IsRewardSequenceRunning) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        int next = Mathf.Max(BossManager.RushFirstK, Mathf.FloorToInt(gm.MaxDistance / 1000f) + 1);
        if (next > BossManager.RushLastK) { Status = "NEXT BOSS: ボスラッシュの最後の関門(98km)は終わっています"; yield break; }
        if (BossManager.ContinuousRush && next > BossManager.RushFirstK) { Status = "NEXT BOSS: 走りながらのボスラッシュは 90km からの1つの遭遇です(90km の手前へ)"; next = BossManager.RushFirstK; }
        Warp(gm, next * 1000f - 40f);
        Status = $"NEXT BOSS: {next * 1000}m の関門の手前へ({BossManager.RushGateLabel(next)})";
    }

    // ===================================================================== //
    // 状態の見張り(Debug Run 中): GameManager の大きな状態の変化をログへ(フリーズした時に最後の状態を追えるように)
    // ===================================================================== //
    string lastGmState = "";
    float gmStateCheck;
    void Update()
    {
        if (!DebugRun.IsActive) return;
        gmStateCheck -= Time.unscaledDeltaTime;
        if (gmStateCheck > 0f) return;
        gmStateCheck = 0.25f;
        var gm = GameManager.Instance;
        if (gm == null) return;
        string st = gm.DebugStateLineCoarse();
        if (st != lastGmState) { lastGmState = st; Debug.Log("[GameState] " + st); }
    }

    // ===================================================================== //
    // 画面の隅の表示(Debug Run 中だけ)
    // ===================================================================== //
    GUIStyle tag;
    void OnGUI()
    {
        if (!DebugRun.IsActive || DebugPanel.IsOpen || (ArenaController.Instance != null && ArenaController.Instance.PanelOpen)) return; // 闘技場の設定を開いている間は出さない(ボタンに重なる)
        GUI.depth = -2050;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);
        if (tag == null) tag = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        tag.fontSize = Mathf.RoundToInt(13f * s);
        var flow = LastDungeonFlow.Instance;
        string text = $"DEBUG RUN · NO SAVE / NO RECORD · {currentLabel}{(flow != null && flow.Enabled ? " · " + flow.Current : "")}";
        float h = 26f * s, bw = 64f * s, tw = tag.CalcSize(new GUIContent(text)).x + 16f * s;
        var r = new Rect(8f * s, Screen.height - h - 8f * s, tw, h);
        var keep = GUI.color;
        GUI.color = new Color(0.5f, 0.05f, 0.05f, 0.78f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width + bw + 6f * s, r.height), Texture2D.whiteTexture);
        GUI.color = keep;
        tag.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(r.x + 8f * s, r.y, r.width, r.height), text, tag);
        if (IsArena) return; // 闘技場は自分の表示を持つ
        if (GUI.Button(new Rect(r.xMax, r.y + 2f * s, bw, h - 4f * s), "≡ DEBUG")) { if (IsUltimateTest) DebugPanel.OpenUltimateStatic(); else if (IsLongCheck) DebugPanel.OpenLongStatic(); else DebugPanel.OpenEndgameStatic(); }
    }
}

// EndgameDebug から使う GameManager の開発用の入口(開発版のみ)
public partial class GameManager
{
    // 選択中のステージを保存せずにそのステージでランを始める(Debug Run。解放の状態にも関係なく)
    public void DebugStartRunOnStage(string stageId)
    {
        SelectedStageId = stageId;
        StartGame();
    }

    // ワープを繰り返すと「ボス戦中に走った距離」の除外分が溜まって距離が進まなくなるので0へ戻す
    public void DebugResetDistanceExclusion()
    {
        distanceExclusionOffset = 0f; bossPhaseEntryRawDistance = 0f;
        distanceExclusionOffsetExact = 0.0; bossPhaseEntryRawDistanceExact = 0.0;
    }

    public void DebugRefillLives() { Lives = maxLives; NetMatch.RequestSetMax(maxLives, true); }
}
#endif

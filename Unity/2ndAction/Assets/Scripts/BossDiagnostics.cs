using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Bug #001 診断フェーズ (2026-09-08) - "Boss中/Boss後停止の原因特定用
// Instrumentation"。前回の根本修正(try/finally追加)後も再現したとの報告
// を受け、今回は推測での追加修正ではなく「停止した瞬間の内部状態を確実に
// 記録する」ことだけに専念する。このクラス自身は**何も直さない**
// (ブリーフの明示指示「今回は勝手に自動復旧しない」) - 状態を監視して
// ログに残すだけ。GameManagerがすでに巨大なため、この診断専用の表面を
// 1ファイルにまとめて見つけやすくした。
//
// 3つのDebug Toggle(下記)は全てdefault=false(=現行の出荷挙動を完全に
// 保持) - 診断ビルドでテスターが明示的にONにしない限り、通常時の挙動は
// 何一つ変わらない。
public static class BossDiagnostics
{
    // ===== 項目7/8/11 - 比較テスト用Debug Toggle ===== //
    // ON: BossMilestonePresentationがTime.timeScaleへの書き込みを一切ス
    // キップする(演出自体(暗転/警告UI/BGM Duck)はそのまま再生 - Timescale
    // 操作だけを外す)。TimeScale操作がFreeze原因かどうかの切り分け用。
    public static bool DisableBossTimeScalePresentation;
    // ON: GameManager.TriggerBossRewardChoiceがカード選択UI自体を丸ごと
    // スキップし、Boss撃破→Checkpoint→EndBossPhase→Gameplay Resumeを即座
    // に行う。RewardCardSequence側が原因候補かどうかの切り分け用。
    public static bool DisableBossRewardSequence;
    // ON: 既存の12秒(bossRewardStuckTimer)/30秒(pendingChoiceStuckTimer)
    // 安全弁が「強制復旧」せず、詰まったままの状態を保持する - 原因が
    // タイムアウトで隠れてしまうのを防ぐための診断専用トグル。
    public static bool DisableSafetyTimers;

    // ===== 項目4 - Ring Buffer Log ===== //
    // DebugModeの有無に関わらず常時記録する(このRing Buffer自体は
    // GameManager.LogBossから無条件に呼ばれる) - Freezeがいつ起きるか
    // 分からない以上、「後からDebugModeを付けて記録し直す」ことはできない
    // ため。コンソールへの出力(Debug.Log)自体は既存どおりDebugModeゲート
    // のまま(通常ビルドのlogcatを汚さない)。
    const int RingBufferCapacity = 100;
    static readonly Queue<string> ring = new Queue<string>(RingBufferCapacity + 4);

    public static void LogEvent(string message)
    {
        ring.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (ring.Count > RingBufferCapacity) ring.Dequeue();
    }

    static string DumpRing()
    {
        if (ring.Count == 0) return "(no events recorded yet)";
        var sb = new StringBuilder();
        foreach (string line in ring) sb.AppendLine(line);
        return sb.ToString();
    }

    // ===== 項目5 - State Transitionを「変化した時だけ」記録 ===== //
    static bool initializedTransitionTracking;
    static float lastTimeScaleLogged;
    static bool lastIsBossPhase;
    static bool lastPresentationRunning;
    static bool lastRewardRunning;
    static bool lastLevelUpPending;
    static GameManager.PendingChoiceKind lastPendingChoiceKind;
    static bool lastInputEnabled;
    static bool lastHitStopActive;
    static string lastStateTransition = "(none)";

    // GameManager.Update()から毎フレーム呼ばれる(既存のUpdateDeferred*系と
    // 同じ場所) - 値が変わっていない限り何もログしない。
    public static void PollStateTransitions()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        bool isBossPhase = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        bool presentationRunning = gm.IsBossPresentationActivePublic;
        bool rewardRunning = gm.IsRewardSequenceRunning && gm.CurrentPendingChoiceKind == GameManager.PendingChoiceKind.BossReward;
        bool levelUpPending = gm.LevelUpPending;
        GameManager.PendingChoiceKind pendingChoiceKind = gm.CurrentPendingChoiceKind;
        bool inputEnabled = Time.timeScale > 0f;
        float timeScale = Time.timeScale;
        bool hitStopActive = HitStop.IsActive;

        if (!initializedTransitionTracking)
        {
            initializedTransitionTracking = true;
            lastTimeScaleLogged = timeScale;
            lastIsBossPhase = isBossPhase;
            lastPresentationRunning = presentationRunning;
            lastRewardRunning = rewardRunning;
            lastLevelUpPending = levelUpPending;
            lastPendingChoiceKind = pendingChoiceKind;
            lastInputEnabled = inputEnabled;
            lastHitStopActive = hitStopActive;
            return;
        }

        if (!Mathf.Approximately(timeScale, lastTimeScaleLogged)) Transition($"TimeScale {lastTimeScaleLogged:F2} -> {timeScale:F2}");
        if (isBossPhase != lastIsBossPhase) Transition($"IsBossPhase {lastIsBossPhase} -> {isBossPhase}");
        if (presentationRunning != lastPresentationRunning) Transition($"BossPresentationRunning {lastPresentationRunning} -> {presentationRunning}");
        if (rewardRunning != lastRewardRunning) Transition($"BossRewardRunning {lastRewardRunning} -> {rewardRunning}");
        if (levelUpPending != lastLevelUpPending)
        {
            Transition($"levelUpPending {lastLevelUpPending} -> {levelUpPending}");
            // Boss Rewardの選択が解決した(true->falseに落ちた)瞬間 - 「Reward
            // 終了済みなのにIsBossPhase=trueが残り続ける」(項目3)の検知窓を開く。
            if (lastLevelUpPending && !levelUpPending && lastPendingChoiceKind == GameManager.PendingChoiceKind.BossReward)
            {
                NotifyBossRewardResolved();
            }
        }
        if (pendingChoiceKind != lastPendingChoiceKind) Transition($"pendingChoice {lastPendingChoiceKind} -> {pendingChoiceKind}");
        if (inputEnabled != lastInputEnabled) Transition($"InputEnabled {lastInputEnabled} -> {inputEnabled}");
        // Bugfix 2026-09-08 (Bug #001 - "TimeScale Deadlock"仮説) - HitStop
        // 自身がTime.timeScaleを掴んだ/離した瞬間を直接Ring Bufferへ記録
        // する。次にFreezeが起きた際、この行が「(true->false)」で終わって
        // いない(=trueのまま残っている)ことが、まさにこのクラスが原因
        // だったことの直接的な証拠になる。
        if (hitStopActive != lastHitStopActive) Transition($"HitStop.IsActive {lastHitStopActive} -> {hitStopActive}");

        lastTimeScaleLogged = timeScale;
        lastIsBossPhase = isBossPhase;
        lastPresentationRunning = presentationRunning;
        lastRewardRunning = rewardRunning;
        lastLevelUpPending = levelUpPending;
        lastPendingChoiceKind = pendingChoiceKind;
        lastHitStopActive = hitStopActive;
        lastInputEnabled = inputEnabled;
    }

    static void Transition(string what)
    {
        lastStateTransition = what;
        LogEvent("[Transition] " + what);
    }

    // ===== 項目6 - Exception捕捉 ===== //
    static bool hooked;
    static string lastException = "(none)";

    // GameManager.Awake()から一度だけ呼ばれる。
    public static void EnsureHooked()
    {
        if (hooked) return;
        hooked = true;
        Application.logMessageReceived += OnLogMessage;
    }

    static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        lastException = $"{condition}\n{stackTrace}";
        bool isBossPhase = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        if (!isBossPhase) return; // 次のSnapshotのためlastExceptionには残すが、強制Dumpするのは実際にBoss Phase中の例外のみ
        string snapshot = BuildSnapshot();
        Debug.LogError("[BOSS ERROR SNAPSHOT]\n" + snapshot);
        RecordSnapshot(snapshot);
    }

    // ===== 項目3 - Freeze Watchdog(検知してLogを出すだけ、修正はしない) ===== //
    static float noProgressTimer;
    static Vector3 lastCheckedPlayerPos;
    static int lastCheckedBossesDefeated = -1;
    static bool noProgressAlreadyReported;

    static float timeScaleZeroTimer;
    static bool timeScaleZeroAlreadyReported;

    static float longBossPhaseTimer;
    static bool longBossPhaseAlreadyReported;

    static float rewardResolvedGraceTimer = -1f;
    static bool rewardResolvedGraceAlreadyReported;

    public const float NoProgressThresholdSeconds = 3f;
    public const float TimeScaleZeroThresholdSeconds = 25f;
    public const float LongBossPhaseThresholdSeconds = 600f; // 荒野街道の大型ボス(HP最大280)は90秒を超えうるため延長(2026-09-20)
    public const float RewardResolvedGraceSeconds = 2f;

    // GameManager.Update()から毎フレーム呼ばれる。
    public static void UpdateFreezeWatchdog()
    {
        // Bugfix 2026-09-08 - HitStopのリーク監視はBoss Phaseの内外を問わ
        // ず常に動かす(元々2026-09-06に修正された「同フレーム2体死亡」
        // バグも通常のEnemy同士の話で、Boss Phase専用の問題ではないため)。
        // このメソッドの残り(Boss Phase専用のWatchdog群)より先に、かつ
        // 早期returnの影響を受けない位置で呼ぶ。
        HitStop.PollForLeakedFreeze();

        GameManager gm = GameManager.Instance;
        BossManager bm = BossManager.Instance;
        if (gm == null || bm == null || !gm.HasStarted || gm.IsGameOver)
        {
            ResetWatchdogState();
            return;
        }

        bool isBossPhase = bm.IsBossPhase;
        if (!isBossPhase)
        {
            ResetWatchdogState();
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // --- Boss Phase継続時間が長すぎる(Spawn〜Combat〜Defeat〜Reward
        // 〜Resumeの全体を通しで見た、粗いが単純な安全網) ---
        longBossPhaseTimer += dt;
        if (!longBossPhaseAlreadyReported && longBossPhaseTimer >= LongBossPhaseThresholdSeconds)
        {
            longBossPhaseAlreadyReported = true;
            ReportSuspected($"BossPhase継続時間が{LongBossPhaseThresholdSeconds:F0}秒を超過");
        }

        // --- Time.timeScale==0 が想定演出時間を大幅に超えて継続 ---
        if (Time.timeScale <= 0f)
        {
            timeScaleZeroTimer += dt;
            if (!timeScaleZeroAlreadyReported && timeScaleZeroTimer >= TimeScaleZeroThresholdSeconds)
            {
                timeScaleZeroAlreadyReported = true;
                ReportSuspected($"Time.timeScale=0が{TimeScaleZeroThresholdSeconds:F0}秒を超えて継続");
            }
        }
        else
        {
            timeScaleZeroTimer = 0f;
            timeScaleZeroAlreadyReported = false;
        }

        // --- Player座標/Boss撃破数が変化しない(Presentation非アクティブ・
        // TimeScale>0の「本来動いているはずの」時間帯のみ対象 - 演出中は除外) ---
        bool activelyRunning = Time.timeScale > 0f && !gm.IsBossPresentationActivePublic
            && (BossDefeatPresentation.Instance == null || !BossDefeatPresentation.Instance.IsRunning);
        if (activelyRunning && PlayerController.Instance != null)
        {
            Vector3 pos = PlayerController.Instance.transform.position;
            int bossesDefeated = bm.BossesDefeated;
            if (lastCheckedBossesDefeated < 0)
            {
                lastCheckedPlayerPos = pos;
                lastCheckedBossesDefeated = bossesDefeated;
                noProgressTimer = 0f;
            }
            else if ((pos - lastCheckedPlayerPos).sqrMagnitude > 0.0025f || bossesDefeated != lastCheckedBossesDefeated)
            {
                lastCheckedPlayerPos = pos;
                lastCheckedBossesDefeated = bossesDefeated;
                noProgressTimer = 0f;
                noProgressAlreadyReported = false;
            }
            else
            {
                noProgressTimer += dt;
                if (!noProgressAlreadyReported && noProgressTimer >= NoProgressThresholdSeconds)
                {
                    noProgressAlreadyReported = true;
                    ReportSuspected($"戦闘中(Presentation非アクティブ・TimeScale>0)にPlayer座標/Boss撃破数が{NoProgressThresholdSeconds:F0}秒以上変化なし");
                }
            }
        }
        else
        {
            noProgressTimer = 0f;
            lastCheckedBossesDefeated = -1; // 再度activelyRunningになった最初のフレームで基準を取り直す
        }

        // --- Boss Reward解決後もIsBossPhase=trueが残留 ---
        if (rewardResolvedGraceTimer >= 0f)
        {
            rewardResolvedGraceTimer += dt;
            if (!rewardResolvedGraceAlreadyReported && rewardResolvedGraceTimer >= RewardResolvedGraceSeconds && isBossPhase)
            {
                rewardResolvedGraceAlreadyReported = true;
                ReportSuspected("Boss Reward解決後もIsBossPhase=trueが残留(EndBossPhase未到達の疑い)");
            }
        }
    }

    // PollStateTransitionsから呼ばれる - levelUpPendingがBossReward解決で
    // true->falseに落ちた瞬間に上記の検知窓を開く。
    static void NotifyBossRewardResolved()
    {
        rewardResolvedGraceTimer = 0f;
        rewardResolvedGraceAlreadyReported = false;
    }

    static void ResetWatchdogState()
    {
        noProgressTimer = 0f;
        noProgressAlreadyReported = false;
        lastCheckedBossesDefeated = -1;
        timeScaleZeroTimer = 0f;
        timeScaleZeroAlreadyReported = false;
        longBossPhaseTimer = 0f;
        longBossPhaseAlreadyReported = false;
        rewardResolvedGraceTimer = -1f;
        rewardResolvedGraceAlreadyReported = false;
    }

    static void ReportSuspected(string reason)
    {
        LogEvent("[FREEZE SUSPECTED] " + reason);
        string snapshot = BuildSnapshot();
        Debug.LogWarning("[BOSS FREEZE SUSPECTED] " + reason + "\n" + snapshot);
        RecordSnapshot("[FREEZE SUSPECTED] " + reason + "\n\n" + snapshot);
    }

    // Bugfix 2026-09-08 - マスターから「そのファイルはどこにある?スマホ
    // 内?」との質問。logcat/adbが使える前提を置かず、誰でもすぐ読める形
    // にするための2本立て: (1) 画面上に直接スクロール可能なテキストとして
    // 表示する(showSnapshotOverlay - フリーズ検知/例外検知で自動的に開く
    // ので、その場でスクリーンショットを撮るだけで済む。IMGUI/OnGUIは
    // Time.timeScale==0の間も普通に動くため、本当にフリーズしていても
    // ボタン操作・表示は機能する)、(2) 念のためAndroidの
    // Application.persistentDataPath配下にもテキストファイルとして追記
    // 保存する(PC接続時にファイルとして取り出したい場合向け、必須ではない)。
    static string lastSnapshotText = "(まだSnapshotは記録されていません)";

    // 2026-09-27 改修: 検知しても詳細画面は自動で開かない(ゲーム画面を遮らない)。記録を保持して
    // 画面端に「ログ保存済み」を短く出し、ファイルへの追記は別スレッドで行う。詳細はDebug Modeの
    // 「診断ログ」ボタン(DiagnosticsOverlay)から開く。
    public static string LastSnapshotText => lastSnapshotText;
    public static int SnapshotCount;

    static void RecordSnapshot(string text)
    {
        lastSnapshotText = text;
        SnapshotCount++;
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DiagnosticsWriter.Enqueue("boss_freeze_log.txt", () => $"\n===== {stamp} =====\n{text}\n");
        FreezeDiagnostics.ShowToast("ログ保存済み: BOSS");
    }

    // ===== 項目2/10 - Debug Snapshot ===== //
    static Vector3 prevSnapshotPos;
    static float prevSnapshotPosTime;

    public static string BuildSnapshot()
    {
        GameManager gm = GameManager.Instance;
        BossManager bm = BossManager.Instance;
        PlayerController pc = PlayerController.Instance;

        float horizontalSpeedApprox = 0f;
        if (pc != null)
        {
            float dt = Time.unscaledTime - prevSnapshotPosTime;
            if (dt > 0.001f && prevSnapshotPosTime > 0f)
            {
                horizontalSpeedApprox = (pc.transform.position.x - prevSnapshotPos.x) / dt;
            }
            prevSnapshotPos = pc.transform.position;
            prevSnapshotPosTime = Time.unscaledTime;
        }

        var sb = new StringBuilder();
        sb.AppendLine("--- BOSS FREEZE SNAPSHOT ---");
        sb.AppendLine();
        sb.AppendLine($"Time.timeScale: {Time.timeScale:F2}");
        sb.AppendLine($"Time.unscaledTime: {Time.unscaledTime:F2}");
        sb.AppendLine($"Time.time: {Time.time:F2}");
        sb.AppendLine();
        sb.AppendLine($"HasStarted: {(gm != null ? gm.HasStarted.ToString() : "?")}");
        sb.AppendLine($"IsGameOver: {(gm != null ? gm.IsGameOver.ToString() : "?")}");
        sb.AppendLine($"IsWin: {(gm != null ? gm.IsWin.ToString() : "?")}");
        sb.AppendLine();
        sb.AppendLine($"IsBossPhase: {(bm != null ? bm.IsBossPhase.ToString() : "?")}");
        sb.AppendLine($"BossPresentationRunning: {(BossMilestonePresentation.Instance != null ? BossMilestonePresentation.Instance.IsRunning.ToString() : "?")}");
        sb.AppendLine($"BossDefeatPresentationRunning: {(BossDefeatPresentation.Instance != null ? BossDefeatPresentation.Instance.IsRunning.ToString() : "?")}");
        bool bossRewardRunning = gm != null && gm.IsRewardSequenceRunning && gm.CurrentPendingChoiceKind == GameManager.PendingChoiceKind.BossReward;
        sb.AppendLine($"BossRewardRunning: {bossRewardRunning} (RewardSequence.IsRunning && pendingChoice==BossReward)");
        sb.AppendLine();
        sb.AppendLine($"LevelUpPending: {(gm != null ? gm.LevelUpPending.ToString() : "?")}");
        sb.AppendLine($"LevelUpDeferredPending: {(gm != null ? gm.LevelUpDeferredPending.ToString() : "?")} (PendingLevelUpCount={(gm != null ? gm.PendingLevelUpCount.ToString() : "?")})");
        sb.AppendLine($"BossRewardDeferredPending: {(gm != null ? gm.BossRewardDeferredPending.ToString() : "?")}");
        // Bugfix 2026-09-08 - PendingChoiceは「今どちらが実行中か」ではな
        // く「直近に開始/解決した選択の種類」を指す値で、LevelUpPending/
        // RewardSequence.IsRunningが両方falseの間は単なる残留値(直近の
        // 選択が既に正常解決した後もそのまま残る - lastLevelUpDiagnostic
        // と同種の性質)。ここで明示注記しないと「Boss生存中にBossReward
        // がPending化されている」ように誤読されかねない(実際に一度この
        // 誤読が起きた)。
        bool pendingChoiceCurrentlyLive = gm != null && (gm.LevelUpPending || gm.IsRewardSequenceRunning);
        sb.AppendLine($"PendingChoice: {(gm != null ? gm.CurrentPendingChoiceKind.ToString() : "?")} ({(pendingChoiceCurrentlyLive ? "現在進行中" : "直近に解決済みの残留値 - 現在Pending中ではない")})");
        sb.AppendLine($"RewardSequence.IsWaitingForSelection: {(gm != null ? gm.IsRewardSequenceWaitingForSelection.ToString() : "?")}");
        sb.AppendLine($"InputEnabled: {Time.timeScale > 0f}");
        sb.AppendLine();
        // Bugfix 2026-09-08 (Bug #001 - "TimeScale Deadlock"仮説) - 上の
        // どのFlagもtrueでない状態でTime.timeScale<=0が続いている場合、
        // このHitStop.IsActiveが唯一の残る説明候補(コードベース内でTime.
        // timeScaleへ直接書き込む箇所はBossMilestonePresentation/
        // RunLevelUpChoice/RunBossRewardChoice/このHitStopの4箇所のみで、
        // 前3つは上の3つのFlagで既に追跡済みのため)。
        sb.AppendLine($"HitStop.IsActive: {HitStop.IsActive} (ActiveCount={HitStop.ActiveCount}, SecondsSinceFreezeStarted={HitStop.SecondsSinceFreezeStarted:F1})");
        sb.AppendLine();
        sb.AppendLine($"PlayerPos: {(pc != null ? pc.transform.position.ToString("F2") : "?")}");
        sb.AppendLine($"PlayerVelocity: (x≈{horizontalSpeedApprox:F2}/s, y={(pc != null ? pc.VerticalVelocity.ToString("F2") : "?")})");
        sb.AppendLine($"Grounded: {(pc != null ? pc.IsGrounded.ToString() : "?")}");
        sb.AppendLine();
        sb.AppendLine($"CurrentBossCount(alive): {(bm != null ? bm.AliveBossCount.ToString() : "?")} (Dragon={(bm != null ? bm.AliveDragonCount.ToString() : "?")}, Majin={(bm != null ? bm.AliveMajinCount.ToString() : "?")})");
        sb.AppendLine($"BossesDefeatedTotal: {(bm != null ? bm.BossesDefeated.ToString() : "?")}");
        sb.AppendLine();
        sb.AppendLine($"MaxDistance: {(gm != null ? gm.MaxDistance.ToString("F1") : "?")}");
        sb.AppendLine($"HighestReachedDistance: {(gm != null ? gm.HighestReachedDistance.ToString("F1") : "?")}");
        sb.AppendLine();
        // "EnemySpawnEnabled"/"TerrainGenerationEnabled" - 実装上は両方とも
        // 同一のFlag(BossManager.IsBossPhase)で直接ゲートされている
        // (EnemyWallManager.cs/TerrainManager.csがそれぞれ直接参照 -
        // 個別に追跡されたFlagが別途あるわけではない)、その通りに正直に報告。
        bool spawnEnabled = bm == null || !bm.IsBossPhase;
        sb.AppendLine($"EnemySpawnEnabled: {spawnEnabled} (derived: !IsBossPhase)");
        sb.AppendLine($"TerrainGenerationEnabled(SafeMode): {spawnEnabled} (derived: !IsBossPhase)");
        sb.AppendLine();
        sb.AppendLine($"CurrentGameState: {(gm != null && gm.IsGameOver ? "GameOver" : (bm != null && bm.IsBossPhase ? "BossPhase" : "NormalRun"))}");
        sb.AppendLine($"CurrentPresentation: {CurrentPresentationName()}");
        sb.AppendLine($"LastStateTransition: {lastStateTransition}");
        sb.AppendLine();
        sb.AppendLine("RecentBossEvents:");
        sb.Append(DumpRing());
        sb.AppendLine();
        sb.AppendLine("LastException:");
        sb.AppendLine(lastException);
        sb.AppendLine();
        sb.AppendLine("--- END SNAPSHOT ---");
        return sb.ToString();
    }

    static string CurrentPresentationName()
    {
        if (BossMilestonePresentation.Instance != null && BossMilestonePresentation.Instance.IsRunning) return "BossMilestonePresentation";
        if (BossDefeatPresentation.Instance != null && BossDefeatPresentation.Instance.IsRunning) return "BossDefeatPresentation";
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.IsRewardSequenceRunning) return "RewardCardSequence";
        return "(none)";
    }

    // ===== 項目7/8/11 用の簡易パネル(DebugMode時のみ、GameManager.OnGUIから呼ぶ) ===== //
    // Bugfix 2026-09-08 - マスターから「文言が白文字・背景も白で見えな
    // かった」との報告(Freeze中のスクリーンショットで確認)。GUILayout.Box
    // /Toggle/Buttonはこのプロジェクトが他で一切使っていないUnity標準
    // GUISkinへ丸ごと依存しており、明るい空背景の上では白っぽく溶けて
    // ほぼ読めなくなっていた。既存の`UiBackdrop.Draw`(濃紺+金縁、HUDの
    // 他要素と同じ見た目)で不透明に近い背景を敷いた上、明示的に白文字の
    // GUIStyleを使う生GUI呼び出しに書き換え - スカイ背景の上でも常に
    // 読めることを優先し、GUILayoutの自動配置には頼らない。
    // 2026-09-27 改修: 常時表示をやめ、DiagnosticsOverlayの「BOSS診断」ボタンで開いた時だけ描く。
    public static void DrawDebugPanel(Rect panelRect)
    {
        UiBackdrop.Draw(panelRect, 0.85f);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 13;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.wordWrap = true;
        titleStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, 34f),
            "Bug#001 診断Toggle(診断フェーズ専用・自動修復しません)", titleStyle);

        GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle);
        toggleStyle.fontSize = 13;
        toggleStyle.normal.textColor = Color.white;
        toggleStyle.onNormal.textColor = Color.white;
        toggleStyle.hover.textColor = Color.white;
        toggleStyle.onHover.textColor = Color.white;

        float y = panelRect.y + 44f;
        const float rowHeight = 26f;
        DisableBossTimeScalePresentation = GUI.Toggle(new Rect(panelRect.x + 8f, y, panelRect.width - 16f, rowHeight), DisableBossTimeScalePresentation, " B: DisableBossTimeScalePresentation", toggleStyle);
        y += rowHeight;
        DisableBossRewardSequence = GUI.Toggle(new Rect(panelRect.x + 8f, y, panelRect.width - 16f, rowHeight), DisableBossRewardSequence, " C: DisableBossRewardSequence", toggleStyle);
        y += rowHeight;
        DisableSafetyTimers = GUI.Toggle(new Rect(panelRect.x + 8f, y, panelRect.width - 16f, rowHeight), DisableSafetyTimers, " 既存12s/30s Safety TimerをOFF", toggleStyle);
        y += rowHeight + 6f;

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 13;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        if (GUI.Button(new Rect(panelRect.x + 8f, y, panelRect.width - 16f, 28f), "Dump Snapshot Now (手動)", buttonStyle))
        {
            string snapshot = BuildSnapshot();
            Debug.Log("[BOSS MANUAL SNAPSHOT]\n" + snapshot);
            RecordSnapshot(snapshot);
        }
    }
}

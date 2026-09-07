using System;
using System.Collections.Generic;
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

        lastTimeScaleLogged = timeScale;
        lastIsBossPhase = isBossPhase;
        lastPresentationRunning = presentationRunning;
        lastRewardRunning = rewardRunning;
        lastLevelUpPending = levelUpPending;
        lastPendingChoiceKind = pendingChoiceKind;
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
        Debug.LogError("[BOSS ERROR SNAPSHOT]\n" + BuildSnapshot());
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
    public const float LongBossPhaseThresholdSeconds = 90f;
    public const float RewardResolvedGraceSeconds = 2f;

    // GameManager.Update()から毎フレーム呼ばれる。
    public static void UpdateFreezeWatchdog()
    {
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
        Debug.LogWarning("[BOSS FREEZE SUSPECTED] " + reason + "\n" + BuildSnapshot());
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
        sb.AppendLine($"PendingChoice: {(gm != null ? gm.CurrentPendingChoiceKind.ToString() : "?")}");
        sb.AppendLine($"RewardSequence.IsWaitingForSelection: {(gm != null ? gm.IsRewardSequenceWaitingForSelection.ToString() : "?")}");
        sb.AppendLine($"InputEnabled: {Time.timeScale > 0f}");
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
    public static void DrawDebugPanel()
    {
        Rect panelRect = new Rect(10f, Screen.height - 230f, 360f, 220f);
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
            Debug.Log("[BOSS MANUAL SNAPSHOT]\n" + BuildSnapshot());
        }
    }
}

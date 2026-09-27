using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// 高速走行中の一時停止/ワープ調査(2026-09-22) - 「画面が一時的に止まり、
// 再開時にワープしたように見える」不具合の原因特定用インストルメンテー
// ション。BossDiagnostics(Bug #001)と同じ方針: このクラス自身は自動修復
// しない、常時軽量に記録し続け、異常を検知したときだけ詳しく書き出す。
//
// 記録する2種類:
// 1) 毎フレームのスナップショット(FrameSample) - 構造体の固定長リング
//    バッファに書くだけで、毎フレームのGCアロケーションは発生しない。
// 2) イベントログ(LogEvent) - カード選出の各ステップ、被ダメージ、
//    HitStop開始/終了、位置補正(Respawn/Knockback)の前後座標など、頻度の
//    低い出来事だけを文字列で記録する(呼び出し頻度が低いため許容)。
//
// 異常検知(毎フレームTick()内):
// - ヒッチ: 生の実時間フレーム間隔(Time.unscaledDeltaTime、Unityの
//   Time.deltaTimeと違いMaximumAllowedTimestepの影響を受けない)が閾値を
//   超えた場合。
// - 座標飛び: プレイヤーX座標が1フレームで閾値を超えて変化した場合。
//   直前のフレームがTime.timeScale<=0だったかどうかで「停止中に座標が
//   変わった」のか「再開した1フレームで飛んだ」のかを区別して記録する。
public static class FreezeDiagnostics
{
    // ===== フレームスナップショット(zero-GC ring buffer) ===== //
    struct FrameSample
    {
        public int frame;
        public float realtime;
        public float rawDt;    // Time.unscaledDeltaTime(実時間、クランプなし)
        public float scaledDt; // Time.deltaTime(timeScale・MaximumAllowedTimestepの影響を受ける、実際の移動計算に使われる値)
        public float timeScale;
        public int pauseReasons; // TimeControl.ActiveReasonCount
        public int hitStopActive; // HitStop.ActiveCount
        public float playerX, playerY;
        public float camX, camY;
        public float distance;
        public int lives;
    }

    const int SampleCapacity = 600; // 60fps換算で約10秒分
    static readonly FrameSample[] samples = new FrameSample[SampleCapacity];
    static int sampleHead; // 次に書き込む位置
    static int sampleCount;
    static bool havePrevSample;
    static FrameSample prevSample;

    // ===== イベントログ(頻度が低いので文字列許容) ===== //
    const int EventCapacity = 200;
    static readonly Queue<string> events = new Queue<string>(EventCapacity + 4);

    public static void LogEvent(string message)
    {
        events.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (events.Count > EventCapacity) events.Dequeue();
    }

    static string DumpEvents()
    {
        if (events.Count == 0) return "(no events recorded yet)";
        var sb = new StringBuilder();
        foreach (string line in events) sb.AppendLine(line);
        return sb.ToString();
    }

    // ===== 閾値(チューニング用) ===== //
    // 実時間で150ms以上フレームが止まった=体感で明確な「一瞬止まった」。
    public const float HitchThresholdSeconds = 0.15f;
    // 1フレームでのプレイヤーX移動がこれを超えたら「座標飛び」候補として
    // 記録する(通常走行の最高速度でも1フレーム(Maximum Allowed Timestep
    // =0.333s)あたり数ユニット程度が上限になるよう別途クランプ済みなので、
    // 意図した移動と紛れにくい値)。
    public const float PositionJumpThreshold = 3.0f;
    // 同じ異常を毎フレーム書き出し続けないための連続抑制時間(実時間)。
    const float DumpCooldownSeconds = 1.0f;
    static float lastDumpRealtime = -100f;

    // GameManager.Update()から毎フレーム呼ぶ(BossDiagnostics.
    // PollStateTransitions/UpdateFreezeWatchdogと同じ場所・同じ位置づけ)。
    public static void Tick()
    {
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        Camera cam = Camera.main;

        FrameSample s = new FrameSample
        {
            frame = Time.frameCount,
            realtime = Time.realtimeSinceStartup,
            rawDt = Time.unscaledDeltaTime,
            scaledDt = Time.deltaTime,
            timeScale = Time.timeScale,
            pauseReasons = TimeControl.ActiveReasonCount,
            hitStopActive = HitStop.ActiveCount,
            playerX = pc != null ? pc.transform.position.x : 0f,
            playerY = pc != null ? pc.transform.position.y : 0f,
            camX = cam != null ? cam.transform.position.x : 0f,
            camY = cam != null ? cam.transform.position.y : 0f,
            distance = gm != null ? gm.MaxDistance : 0f,
            lives = gm != null ? gm.Lives : -1,
        };

        samples[sampleHead] = s;
        sampleHead = (sampleHead + 1) % SampleCapacity;
        if (sampleCount < SampleCapacity) sampleCount++;

        // ゲーム未開始/ゲームオーバー中はプレイヤーが動かないのが正常な
        // ので、異常判定はHasStarted && !IsGameOverの間だけ行う。
        bool activeRun = gm != null && gm.HasStarted && !gm.IsGameOver;

        if (havePrevSample && activeRun)
        {
            if (s.rawDt >= HitchThresholdSeconds)
            {
                ReportAnomaly($"HITCH: 実フレーム間隔{s.rawDt * 1000f:F0}ms (閾値{HitchThresholdSeconds * 1000f:F0}ms)  timeScale={s.timeScale:F2}  pauseReasons={TimeControl.DescribeActiveReasons()}");
            }

            float dx = Mathf.Abs(s.playerX - prevSample.playerX);
            if (dx >= PositionJumpThreshold)
            {
                bool prevPaused = prevSample.timeScale <= 0f;
                bool nowPaused = s.timeScale <= 0f;
                string when = !prevPaused && !nowPaused ? "通常再生中(一時停止とは無関係)"
                    : prevPaused && !nowPaused ? "再開した瞬間の1フレームで発生(RESUME FRAME JUMP)"
                    : "一時停止中に発生(CHANGED WHILE PAUSED - 想定外)";
                ReportAnomaly($"POSITION JUMP: playerX {prevSample.playerX:F2} -> {s.playerX:F2} (Δ{dx:F2})  {when}  timeScale(prev->now)={prevSample.timeScale:F2}->{s.timeScale:F2}  pauseReasons={TimeControl.DescribeActiveReasons()}");
            }
        }

        prevSample = s;
        havePrevSample = true;
    }

    static void ReportAnomaly(string reason)
    {
        LogEvent("[ANOMALY] " + reason);
        if (Time.realtimeSinceStartup - lastDumpRealtime < DumpCooldownSeconds) return;
        lastDumpRealtime = Time.realtimeSinceStartup;

        string snapshot = BuildDump(reason);
        Debug.LogWarning("[FreezeDiagnostics] " + reason + "\n" + snapshot);
        RecordSnapshot(snapshot);
    }

    // 直近のフレームサンプル(新しい順)+イベントログをまとめてテキスト化
    // する。BuildSnapshotと同じ「後から見返せる形」を優先。
    static string BuildDump(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- FREEZE/WARP DIAGNOSTICS DUMP ---");
        sb.AppendLine(reason);
        sb.AppendLine();
        sb.AppendLine($"Time.timeScale={Time.timeScale:F2}  TimeControl.ActiveReasons={TimeControl.DescribeActiveReasons()}  HitStop.ActiveCount={HitStop.ActiveCount}  AutoSlow={TimeControl.AutoScale:F2}(enabled={(AutoSlowMotion.Instance != null && AutoSlowMotion.Instance.autoSlowEnabled)})");
        sb.AppendLine($"RewardCardSequence.DebugStep={RewardCardSequence.DebugStep}");
        sb.AppendLine();
        sb.AppendLine("Recent frames (oldest -> newest, up to 120):");
        sb.AppendLine("frame  realtime  rawDt(ms)  scaledDt(ms)  timeScale  pauseReasons  hitStop  playerX  playerY  camX  distance  lives");
        int show = Mathf.Min(120, sampleCount);
        int start = (sampleHead - show + SampleCapacity) % SampleCapacity;
        for (int i = 0; i < show; i++)
        {
            FrameSample fs = samples[(start + i) % SampleCapacity];
            sb.AppendLine($"{fs.frame}  {fs.realtime:F3}  {fs.rawDt * 1000f:F1}  {fs.scaledDt * 1000f:F1}  {fs.timeScale:F2}  {fs.pauseReasons}  {fs.hitStopActive}  {fs.playerX:F2}  {fs.playerY:F2}  {fs.camX:F2}  {fs.distance:F1}  {fs.lives}");
        }
        sb.AppendLine();
        sb.AppendLine("Recent events:");
        sb.Append(DumpEvents());
        sb.AppendLine("--- END DUMP ---");
        return sb.ToString();
    }

    // ===== 画面上に直接表示する(BossDiagnostics.RecordSnapshot/
    // DrawSnapshotOverlayIfAnyと同じ考え方 - adb/logcatなしでスクリーン
    // ショットだけで持ち帰れるように) ===== //
    static string lastDumpText = "(まだ記録はありません)";
    static bool showOverlay;
    static Vector2 overlayScrollPos;

    static void RecordSnapshot(string text)
    {
        lastDumpText = text;
        showOverlay = true;
        try
        {
            string path = Path.Combine(Application.persistentDataPath, "hitch_log.txt");
            File.AppendAllText(path, $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{text}\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FreezeDiagnostics] Failed to write hitch_log.txt: " + e);
        }
    }

    public static void DrawSnapshotOverlayIfAny()
    {
        if (!showOverlay) return;

        Rect area = new Rect(Screen.width * 0.04f, Screen.height * 0.05f, Screen.width * 0.92f, Screen.height * 0.85f);
        UiBackdrop.Draw(area, 0.95f);

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.75f, 0.4f) }
        };
        GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 120f, 26f), "Freeze/Warp Diagnostics Dump (スクリーンショットして保存してください)", headerStyle);

        GUIStyle closeStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, normal = { textColor = Color.white } };
        if (GUI.Button(new Rect(area.xMax - 100f, area.y + 4f, 90f, 30f), "閉じる", closeStyle))
        {
            showOverlay = false;
            return;
        }

        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = false,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.white }
        };

        Rect viewRect = new Rect(area.x + 10f, area.y + 42f, area.width - 20f, area.height - 52f);
        float innerWidth = Mathf.Max(viewRect.width - 24f, textStyle.CalcSize(new GUIContent("frame  realtime  rawDt(ms)  scaledDt(ms)  timeScale  pauseReasons  hitStop  playerX  playerY  camX  distance  lives")).x + 20f);
        float contentHeight = Mathf.Max(viewRect.height, textStyle.CalcHeight(new GUIContent(lastDumpText), innerWidth) + 20f);

        overlayScrollPos = GUI.BeginScrollView(viewRect, overlayScrollPos, new Rect(0f, 0f, innerWidth, contentHeight));
        GUI.Label(new Rect(0f, 0f, innerWidth, contentHeight), lastDumpText, textStyle);
        GUI.EndScrollView();
    }

    // DebugModeパネルからの手動ダンプ用(BossDiagnostics.DrawDebugPanelの
    // 「Dump Snapshot Now」ボタンと同じ位置づけ)。
    public static void ManualDump()
    {
        string snapshot = BuildDump("MANUAL DUMP");
        Debug.Log("[FreezeDiagnostics] Manual dump\n" + snapshot);
        RecordSnapshot(snapshot);
    }
}

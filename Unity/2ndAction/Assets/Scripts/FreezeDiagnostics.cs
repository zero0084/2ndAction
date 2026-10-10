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
// - ヒッチ: 生の実時間フレーム間隔(Time.unscaledDeltaTime)が閾値を超えた場合。
// - 座標飛び: プレイヤーの「論理X」(FloatingOriginで戻した量を足した値)が1フレームで
//   その時の速度から考えられる移動量を大きく超えて変化した場合。
//
// 表示と書き出しの方針(2026-09-27 改修):
// - 異常を検知しても詳細画面は自動で開かない。ログを保存し、画面端に「ログ保存済み」を
//   短く出すだけ(DiagnosticsOverlay)。詳細は専用ボタンを押した時だけ開く。
// - 意図した座標変更(Run開始/リトライ/CONTINUE/距離ジャンプ/落下・被弾からの復帰/
//   FloatingOriginのシフト)は異常ではなく通常のイベント([MOVE])として記録する。
// - 開始カウントダウン中/シーン読み込み直後/アプリ復帰直後のヒッチは、読み込み等の想定内の
//   ものとして通常イベントに記録する(それ以外の実際の処理落ちの検知は従来どおり)。
// - 同じ種類の異常は短時間に何度も書き出さない。ファイルへの書き出しと本文の組み立ては
//   別スレッドで行い、メインスレッド(ゲーム進行)を止めない。
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
        public float playerX, playerY; // playerXは論理X(FloatingOriginのシフトを含まない)
        public float camX, camY;
        public float distance;
        public int lives;
        public float runSpeed;
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

    // 自動テストが出来事を数えるための受け口(通常は未設定)。
    public static Action<string> EventTap;

    public static void LogEvent(string message)
    {
        events.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (events.Count > EventCapacity) events.Dequeue();
        EventTap?.Invoke(message);
    }

    // ===== 閾値(チューニング用) ===== //
    // 実時間で150ms以上フレームが止まった=体感で明確な「一瞬止まった」。
    public const float HitchThresholdSeconds = 0.15f;
    // 1フレームでのプレイヤー論理X移動の下限しきい値。実際のしきい値はその時の走行速度×フレーム
    // 時間(+突進/ノックバックの余裕)と比べて大きい方(高速+長いフレームの正常な移動を誤検知しない)。
    public const float PositionJumpThreshold = 3.0f;
    // 同じ種類の異常を続けて書き出さない間隔(実時間)と、1分あたりの書き出し上限。
    const float SameKindCooldownSeconds = 8f;
    const int MaxDumpsPerMinute = 6;
    // シーン読み込み/Run開始/アプリ復帰の直後は、読み込み由来の想定内のヒッチとして扱う時間。
    const float StartupGraceSeconds = 1.5f;

    // ===== 意図した座標変更 ===== //
    static int intendedMoveFrame = -100;
    static string intendedMoveReason = "";
    static float graceUntilRealtime;
    static bool hooksInstalled;
    static bool wasStarted;

    // 呼び出し側: 「これからプレイヤーを意図して動かす」(復帰/ワープ/CONTINUE等)。
    // このフレームと次のフレームの座標飛びは異常ではなく通常のイベントとして記録する。
    public static void NoteIntendedMove(string reason)
    {
        intendedMoveFrame = Time.frameCount;
        intendedMoveReason = reason;
    }

    // シーン読み込み/アプリ復帰など、直後のヒッチを想定内として扱う区間を始める。
    public static void BeginGrace(string reason)
    {
        graceUntilRealtime = Time.realtimeSinceStartup + StartupGraceSeconds;
        havePrevSample = false; // 直前の座標とは比べない(シーンが入れ替わった/長時間止まっていた)
        LogEvent($"[GRACE] {reason}");
    }

    static void InstallHooks()
    {
        if (hooksInstalled) return;
        hooksInstalled = true;
        FloatingOrigin.Warped += d => NoteIntendedMove($"distance warp +{d:F0}m (FloatingOrigin.LogicalWarp)");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => BeginGrace("scene loaded (start/retry/home)");
        DiagnosticsOverlay.Ensure();
    }

    // ===== 異常の記録(UI/自動テスト用) ===== //
    public class Anomaly
    {
        public string kind;     // HITCH / POSITION JUMP
        public string reason;
        public string time;
        public bool dumped;     // 詳細を書き出したか(同種の連続は数だけ数える)
    }
    public static readonly List<Anomaly> RecentAnomalies = new List<Anomaly>();
    public static int AnomalyCount, SuppressedCount, IntendedMoveCount, GraceHitchCount, DumpCount;
    static readonly Dictionary<string, float> lastDumpByKind = new Dictionary<string, float>();
    static readonly Queue<float> dumpTimes = new Queue<float>();

    // 「ログ保存済み」の通知(DiagnosticsOverlayが短時間だけ画面端に出す)。
    public static float ToastUntilRealtime { get; private set; }
    public static string ToastText { get; private set; } = "";

    // GameManager.Update()から毎フレーム呼ぶ(BossDiagnostics.
    // PollStateTransitions/UpdateFreezeWatchdogと同じ場所・同じ位置づけ)。
    public static void Tick()
    {
        InstallHooks();
        PlayerController pc = PlayerController.Instance;
        GameManager gm = GameManager.Instance;
        Camera cam = Camera.main;

        FrameSample s = new FrameSample
        {
            frame = Time.frameCount,
            realtime = Time.realtimeSinceStartup,
            rawDt = StallProbe.RealDt > 0f ? StallProbe.RealDt : Time.unscaledDeltaTime, // 2026-10-10: 実時間(unscaledDeltaTime はなめらかにされる)
            scaledDt = Time.deltaTime,
            timeScale = Time.timeScale,
            pauseReasons = TimeControl.ActiveReasonCount,
            hitStopActive = HitStop.ActiveCount,
            playerX = pc != null ? (float)(pc.transform.position.x + FloatingOrigin.Offset) : 0f,
            playerY = pc != null ? pc.transform.position.y : 0f,
            camX = cam != null ? cam.transform.position.x : 0f,
            camY = cam != null ? cam.transform.position.y : 0f,
            distance = gm != null ? gm.MaxDistance : 0f,
            lives = gm != null ? gm.Lives : -1,
            runSpeed = pc != null ? pc.CurrentAutoRunSpeed : 0f,
        };

        samples[sampleHead] = s;
        sampleHead = (sampleHead + 1) % SampleCapacity;
        if (sampleCount < SampleCapacity) sampleCount++;

        // ゲーム未開始/ゲームオーバー中はプレイヤーが動かないのが正常なので、異常判定はRun中だけ。
        bool activeRun = gm != null && gm.HasStarted && !gm.IsGameOver;
        if (activeRun && !wasStarted) BeginGrace("run started");
        wasStarted = activeRun;
        // 開始カウントダウン中は、配置/読み込み/演出のための移動・ヒッチが想定内。
        bool expectedPhase = gm != null && (gm.CountdownActive || gm.ResumeGateActive);
        bool inGrace = Time.realtimeSinceStartup < graceUntilRealtime;

        if (havePrevSample && activeRun)
        {
            if (s.rawDt >= HitchThresholdSeconds)
            {
                string msg = $"HITCH: 実フレーム間隔{s.rawDt * 1000f:F0}ms (閾値{HitchThresholdSeconds * 1000f:F0}ms)  timeScale={s.timeScale:F2}  pauseReasons={TimeControl.DescribeActiveReasons()}";
                if (expectedPhase || inGrace) { GraceHitchCount++; LogEvent($"[HITCH-EXPECTED] {(expectedPhase ? "countdown" : "loading/resume")} {msg}"); }
                else ReportAnomaly("HITCH", msg);
            }

            float dx = Mathf.Abs(s.playerX - prevSample.playerX);
            // その時の速度で1フレームに進み得る量(突進/ノックバック/打ち上げの余裕込み)より大きいか。
            float allowed = Mathf.Max(PositionJumpThreshold, (Mathf.Max(s.runSpeed, prevSample.runSpeed) * 1.3f + 8f) * Mathf.Max(s.scaledDt, 0.0001f));
            if (dx >= allowed)
            {
                bool intended = Time.frameCount - intendedMoveFrame <= 2;
                if (intended || expectedPhase || inGrace)
                {
                    IntendedMoveCount++;
                    LogEvent($"[MOVE] {(intended ? intendedMoveReason : expectedPhase ? "countdown placement" : "start/load placement")}: X {prevSample.playerX:F2} -> {s.playerX:F2} (Δ{dx:F2})");
                }
                else
                {
                    bool prevPaused = prevSample.timeScale <= 0f;
                    bool nowPaused = s.timeScale <= 0f;
                    string when = !prevPaused && !nowPaused ? "通常再生中(一時停止とは無関係)"
                        : prevPaused && !nowPaused ? "再開した瞬間の1フレームで発生(RESUME FRAME JUMP)"
                        : "一時停止中に発生(CHANGED WHILE PAUSED - 想定外)";
                    ReportAnomaly("POSITION JUMP", $"POSITION JUMP: logicalX {prevSample.playerX:F2} -> {s.playerX:F2} (Δ{dx:F2}, 許容{allowed:F2})  {when}  timeScale(prev->now)={prevSample.timeScale:F2}->{s.timeScale:F2}  pauseReasons={TimeControl.DescribeActiveReasons()}");
                }
            }
        }

        prevSample = s;
        havePrevSample = true;
        DiagnosticsWriter.Pump();
    }

    // アプリがバックグラウンドから戻った(GameManager.OnApplicationPause/Focusから)。
    public static void NoteAppResumed() => BeginGrace("app resumed");

    static void ReportAnomaly(string kind, string reason)
    {
        AnomalyCount++;
        LogEvent("[ANOMALY] " + reason);
        var a = new Anomaly { kind = kind, reason = reason, time = DateTime.Now.ToString("HH:mm:ss") };
        RecentAnomalies.Add(a);
        if (RecentAnomalies.Count > 50) RecentAnomalies.RemoveAt(0);

        float now = Time.realtimeSinceStartup;
        while (dumpTimes.Count > 0 && now - dumpTimes.Peek() > 60f) dumpTimes.Dequeue();
        bool sameKindRecent = lastDumpByKind.TryGetValue(kind, out float last) && now - last < SameKindCooldownSeconds;
        if (sameKindRecent || dumpTimes.Count >= MaxDumpsPerMinute)
        {
            SuppressedCount++; // 数だけ数える(詳細の書き出しと通知はしない)
            return;
        }
        lastDumpByKind[kind] = now;
        dumpTimes.Enqueue(now);
        a.dumped = true;
        DumpCount++;
        QueueDump(reason, "hitch_log.txt");
        // 本文はファイルへ。コンソールにはスタックトレース無しの1行だけ(大きな文字列の出力で処理落ちしない)。
        Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "[FreezeDiagnostics] {0} (logged to hitch_log.txt)", reason);
        ShowToast($"ログ保存済み: {kind}");
    }

    public static void ShowToast(string text)
    {
        ToastText = text;
        ToastUntilRealtime = Time.realtimeSinceStartup + 2.2f;
    }

    // 直近のフレーム(コピー)とイベントを別スレッドで文章にして書き出す。
    static void QueueDump(string reason, string fileName)
    {
        int show = Mathf.Min(120, sampleCount);
        var copy = new FrameSample[show];
        int start = (sampleHead - show + SampleCapacity) % SampleCapacity;
        for (int i = 0; i < show; i++) copy[i] = samples[(start + i) % SampleCapacity];
        string[] evs = events.ToArray();
        string header = $"Time.timeScale={Time.timeScale:F2}  TimeControl.ActiveReasons={TimeControl.DescribeActiveReasons()}  HitStop.ActiveCount={HitStop.ActiveCount}  Assist={(HighSpeedAssist.Instance != null ? $"{HighSpeedAssist.Instance.StatusText()} {HighSpeedAssist.Instance.JudgedKmh:F0}km/h last={HighSpeedAssist.Instance.LastAction} fail={HighSpeedAssist.Instance.LastFailure}" : "none")}\nRewardCardSequence.DebugStep={RewardCardSequence.DebugStep}";
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DiagnosticsWriter.Enqueue(fileName, () =>
        {
            var sb = new StringBuilder(16384);
            sb.AppendLine("--- FREEZE/WARP DIAGNOSTICS DUMP ---");
            sb.AppendLine(reason);
            sb.AppendLine();
            sb.AppendLine(header);
            sb.AppendLine();
            sb.AppendLine("Recent frames (oldest -> newest, up to 120):");
            sb.AppendLine("frame  realtime  rawDt(ms)  scaledDt(ms)  timeScale  pauseReasons  hitStop  logicalX  playerY  camX  distance  lives  runSpeed");
            foreach (var fs in copy)
                sb.AppendLine($"{fs.frame}  {fs.realtime:F3}  {fs.rawDt * 1000f:F1}  {fs.scaledDt * 1000f:F1}  {fs.timeScale:F2}  {fs.pauseReasons}  {fs.hitStopActive}  {fs.playerX:F2}  {fs.playerY:F2}  {fs.camX:F2}  {fs.distance:F1}  {fs.lives}  {fs.runSpeed:F1}");
            sb.AppendLine();
            sb.AppendLine("Recent events:");
            if (evs.Length == 0) sb.AppendLine("(no events recorded yet)");
            foreach (string e in evs) sb.AppendLine(e);
            sb.AppendLine("--- END DUMP ---");
            string text = sb.ToString();
            LastDumpText = text;
            return $"\n===== {stamp} =====\n{text}\n";
        });
    }

    // 最後に書き出した詳細(詳細画面で表示)。別スレッドから書き換わるのでvolatile。
    static volatile string lastDumpText = "(まだ記録はありません)";
    public static string LastDumpText { get => lastDumpText; private set => lastDumpText = value; }

    // 詳細画面の「今すぐ記録」ボタン用(従来の手動ダンプ)。
    public static void ManualDump()
    {
        QueueDump("MANUAL DUMP", "hitch_log.txt");
        ShowToast("ログ保存済み: MANUAL");
    }

    // 詳細画面の上部に出す要約。
    public static string Summary() =>
        $"異常 {AnomalyCount}件(詳細保存 {DumpCount} / 同種の連続で省略 {SuppressedCount})  意図した移動 {IntendedMoveCount}件  開始/復帰直後のヒッチ {GraceHitchCount}件\n保存先: {Path.Combine(Application.persistentDataPath, "hitch_log.txt")}";
}

// ログファイルへの書き出しを別スレッドで順番に行う(メインスレッドはキューに積むだけ)。
public static class DiagnosticsWriter
{
    static readonly object gate = new object();
    static readonly Queue<(string path, Func<string> build)> queue = new Queue<(string, Func<string>)>();
    static bool running;
    static string dir;

    public static int Pending { get { lock (gate) return queue.Count + (running ? 1 : 0); } }
    public static int Written;

    public static void Enqueue(string fileName, Func<string> buildText)
    {
        if (dir == null) dir = Application.persistentDataPath;
        lock (gate)
        {
            if (queue.Count > 20) queue.Dequeue(); // 溜まりすぎたら古いものから捨てる
            queue.Enqueue((Path.Combine(dir, fileName), buildText));
        }
    }

    // メインスレッドから毎フレーム呼ぶ: 書き出し中でなければ次を別スレッドで始める。
    public static void Pump()
    {
        (string path, Func<string> build) job;
        lock (gate)
        {
            if (running || queue.Count == 0) return;
            job = queue.Dequeue();
            running = true;
        }
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try { File.AppendAllText(job.path, job.build()); System.Threading.Interlocked.Increment(ref Written); }
            catch (Exception) { /* 書けなくてもゲームには影響させない */ }
            finally { lock (gate) running = false; }
        });
    }
}

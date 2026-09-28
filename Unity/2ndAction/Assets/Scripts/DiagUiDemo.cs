#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

// 診断表示改修(2026-09-27)の確認用デモ(開発ビルドのみ、-diagUiDemo <出力フォルダ> の時だけ動く)。
//  Debug ON → 荒野街道を開始(カウントダウン中) → SPD+で高速 → 自動スロー ON/OFF → 実際の処理落ち(300ms)
//  → 距離ジャンプ(意図した移動) → 詳細ログを開く → 閉じる、の各場面でスクリーンショットを撮り、
//  異常/意図した移動/起動直後のヒッチの件数と、ログ書き出し前後のフレーム時間を記録する。
public class DiagUiDemo : MonoBehaviour
{
    string dir;
    StreamWriter log;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-diagUiDemo")
            {
                if (FindFirstObjectByType<DiagUiDemo>() != null) return;
                var go = new GameObject("DiagUiDemo");
                DontDestroyOnLoad(go);
                go.AddComponent<DiagUiDemo>().dir = args[i + 1];
            }
    }

    void L(string s) { Debug.Log("[DIAGDEMO] " + s); log?.WriteLine(s); log?.Flush(); }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null;
        L($"shot {name}: anomalies={FreezeDiagnostics.AnomalyCount} dumps={FreezeDiagnostics.DumpCount} suppressed={FreezeDiagnostics.SuppressedCount} intendedMoves={FreezeDiagnostics.IntendedMoveCount} graceHitches={FreezeDiagnostics.GraceHitchCount} detailOpen={DiagnosticsOverlay.DetailOpen} toast='{(FreezeDiagnostics.ToastUntilRealtime > Time.realtimeSinceStartup ? FreezeDiagnostics.ToastText : "")}' ts={Time.timeScale:F2} speed={(PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f):F1}m/s");
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        log = new StreamWriter(Path.Combine(dir, "diagdemo.txt"), false);
        yield return new WaitForSeconds(2f);
        var gm = GameManager.Instance;
        typeof(GameManager).GetProperty("DebugMode").GetSetMethod(true).Invoke(gm, new object[] { true });
        gm.SetSelectedStage("wasteland_road");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        yield return new WaitForSecondsRealtime(1.2f);
        yield return Shot("01_countdown");
        while (gm.CountdownActive) yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").GetSetMethod(true).Invoke(gm, new object[] { true });
        yield return new WaitForSecondsRealtime(2f);
        yield return Shot("02_running");

        // 高速にする(SPD+を数回押した状態)
        PlayerController.DebugSpeedScale = 3f;
        yield return new WaitForSecondsRealtime(4f);
        yield return Shot("03_highspeed");
        yield return new WaitForSecondsRealtime(2.5f);

        // 比較用: ログ書き出しが無い時の同じ速度でのフレーム時間
        float baseWorst = 0f;
        for (int i = 0; i < 60; i++) { yield return null; baseWorst = Mathf.Max(baseWorst, Time.unscaledDeltaTime); }
        L($"baseline worst frame over 60 frames (no dump): {baseWorst * 1000f:F1}ms");
        // 実際の処理落ち(300ms)→ 自動で詳細は開かず、右端に「ログ保存済み」だけ
        int before = FreezeDiagnostics.AnomalyCount;
        System.Threading.Thread.Sleep(300);
        yield return null;
        // 書き出し中/後のフレーム時間(新しい処理落ちを起こしていないか)。スクリーンショットの保存自体が
        // 重いので、撮影より先に測る。
        float worst = 0f;
        var frames = new System.Text.StringBuilder();
        for (int i = 0; i < 60; i++) { yield return null; worst = Mathf.Max(worst, Time.unscaledDeltaTime); if (i < 8) frames.Append($"{Time.unscaledDeltaTime * 1000f:F0} "); }
        L($"worst frame over 60 frames right after the dump: {worst * 1000f:F1}ms (first frames: {frames}) writer written={DiagnosticsWriter.Written} pending={DiagnosticsWriter.Pending}");
        yield return Shot("05_hitch_toast");
        L($"hitch detected={FreezeDiagnostics.AnomalyCount > before} detailOpen(auto)={DiagnosticsOverlay.DetailOpen}");
        // 同じ種類の連発(8秒以内)は保存しない
        int dumps = FreezeDiagnostics.DumpCount;
        System.Threading.Thread.Sleep(250);
        yield return null; yield return null;
        L($"second hitch within cooldown: dumps {dumps} -> {FreezeDiagnostics.DumpCount} (suppressed={FreezeDiagnostics.SuppressedCount})");

        // 意図した座標変更(距離ジャンプ)は異常ではない
        int anomalies = FreezeDiagnostics.AnomalyCount, moves = FreezeDiagnostics.IntendedMoveCount;
        gm.DebugWarpToDistance(gm.MaxDistance + 2000f);
        yield return null; yield return null; yield return null;
        L($"distance warp: anomalies {anomalies} -> {FreezeDiagnostics.AnomalyCount}, intended moves {moves} -> {FreezeDiagnostics.IntendedMoveCount}");
        yield return new WaitForSecondsRealtime(1f);

        // 詳細は専用ボタンの時だけ(ここではボタンと同じ操作を直接行う)
        typeof(DiagnosticsOverlay).GetProperty("DetailOpen").GetSetMethod(true).Invoke(null, new object[] { true });
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Shot("06_detail_open");
        typeof(DiagnosticsOverlay).GetProperty("DetailOpen").GetSetMethod(true).Invoke(null, new object[] { false });
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Shot("07_detail_closed");
        L("done");
        log.Close();
        Application.Quit();
    }
}
#endif

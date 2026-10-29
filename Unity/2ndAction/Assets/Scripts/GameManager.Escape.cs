using UnityEngine;

// 脱出の成功の確定(2026-10-24、実機: 100km 以降の死神のラン中に、脱出の演出に入ったのに敗北になる)。
// 原因: 長押しが終わってから Win() までの間、成功はまだ決まっていなかった。演出(減速 → 終わりのポーズ → 暗転 → Win)の
// 最後で被弾の禁止(PresentationDamageLock)を外し、暗転が覆う約0.25秒の間に死神の大鎌(ReapPlayer → TryDamagePlayer)が
// 先に敗北を確定 → 覆った所で呼ぶ Win() は「もう終わっている」で何もしなかった。
// 直し方: 長押しが終わった瞬間に成功を確定する(EscapeConfirmed)。確定の後は
//  ・敗北にならない(被弾/死神/落下/HOST からの脱落の知らせを受けない)
//  ・距離・EXP・MILE・走った時間が増えない(確定した時の値のまま)
//  ・死神を止める(追跡/大鎌/接触の判定)
//  ・演出の最後で必ず Win() を通す(ほかの画面の切り替え中でも)
// 敗北が先に確定していれば(IsGameOver)、確定せず、成功の演出も始めない。同じフレームで先に来た方に決まる(結果と演出は食い違わない)。
public partial class GameManager
{
    public bool EscapeConfirmed { get; private set; }
    float escapeConfirmedTime;
    public float EscapeConfirmedDistance { get; private set; }
    public int EscapeConfirmFrame { get; private set; } = -1;
    public int EscapeIgnoredDamage { get; private set; } // 確定の後に受けなかった被弾/死神の一撃の数(試験・記録用)
    int resultShownFrame = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 試験の比較用: 以前の動き(確定しない・演出の最後で被弾の禁止を外す)。-qaEscape -escLegacy 1
    public static bool QaLegacyEscape;
#else
    public const bool QaLegacyEscape = false;
#endif

    // 長押しが完了した時に PlayerController が呼ぶ。true なら成功が確定(この後に演出を始める)
    public bool TryConfirmEscape(string via)
    {
        if (QaLegacyEscape) { EscapeLog($"hold complete via={via} (legacy: not confirmed)"); return !IsGameOver; }
        if (EscapeConfirmed) return false;
        if (!HasStarted || IsGameOver)
        {
            EscapeLog($"hold complete via={via} but the run already ended (win={IsWin} reason={DeathReason}) -> no success");
            return false;
        }
        EscapeConfirmed = true;
        escapeConfirmedTime = Time.time;
        EscapeConfirmedDistance = MaxDistance;
        EscapeConfirmFrame = Time.frameCount;
        SetPresentationDamageLock(true);
        ReaperBase.StopAllForRunEnd(); // 追跡/大鎌/接触を止める(その場で止まる)
        EscapeLog($"success confirmed via={via} d={MaxDistance:F1} lives={Lives} reaper={(ReaperBase.Active != null ? ReaperBase.Active.CurrentPhase.ToString() : "none")}");
        return true;
    }

    // 確定の後: 距離/EXP/報酬を増やさない
    bool EscapeFrozen => EscapeConfirmed && !IsGameOver;
    float RunEndTime => EscapeConfirmed ? escapeConfirmedTime : Time.time;

    bool IgnoreAfterEscape(string what)
    {
        if (!EscapeConfirmed) return false;
        EscapeIgnoredDamage++;
        EscapeLog($"ignored after success: {what}");
        return true;
    }

    public const string EscapeTraceFile = "escape_trace.txt";
    static string escapeTraceDir;
    static string EscapeTraceDir => escapeTraceDir ?? (escapeTraceDir = Application.persistentDataPath); // 書き出し(別の線)から使うので先に覚える
    public void EscapeLog(string msg)
    {
        if (escapeTraceDir == null) escapeTraceDir = Application.persistentDataPath;
        // 2026-10-29: 同じランの出来事(長押し完了/成功の確定/負けの要求と理由/演出の終わり/RESULT)を対応づけられるよう、ランの ID を付ける。
        //  開発版は端末の中にも残す(escape_trace.txt、256KB で古い方を .1 へ)。実機で FAILED が再発した時に取り出して追う
        string rid = RunLedger.Current != null && !string.IsNullOrEmpty(RunLedger.Current.runId) ? RunLedger.Current.runId.Substring(0, Mathf.Min(8, RunLedger.Current.runId.Length)) : "-";
        string m = $"[Escape] run={rid} f={Time.frameCount} t={Time.time:F2} {msg}";
        Debug.Log(m);
        FreezeDiagnostics.LogEvent(m);
        if (Debug.isDebugBuild)
        {
            string line = $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {m}\n";
            DiagnosticsWriter.Enqueue(EscapeTraceFile, () =>
            {
                try
                {
                    string p = System.IO.Path.Combine(EscapeTraceDir, EscapeTraceFile);
                    if (System.IO.File.Exists(p) && new System.IO.FileInfo(p).Length > 256 * 1024) { System.IO.File.Copy(p, p + ".1", true); System.IO.File.Delete(p); }
                }
                catch (System.Exception) { }
                return line;
            });
        }
    }

    // RESULT の最初の表示(順番の記録)
    void NoteResultShown()
    {
        if (resultShownFrame >= 0) return;
        resultShownFrame = Time.frameCount;
        EscapeLog($"RESULT shown win={IsWin} reason={DeathReason} d={MaxDistance:F1} mile={RunMile}");
    }
}

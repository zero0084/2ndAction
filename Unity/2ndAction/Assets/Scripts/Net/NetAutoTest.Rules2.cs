#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// 2026-11-05 マルチのルールの試験(長押し復活/参加者の欄/VERSUS の死神の乱入時間と MILE ボーナス)。開発版の自動テストだけ
//   -netAutoHudMode standard|simple|hidden   HUD の表示の設定(-memSave と一緒に使う = 保存に残さない)
//   -netAutoHudShots 秒,秒,...               ラン開始からの秒で画面を撮る(-netAutoShotDir)+ 参加者の欄/復活の欄/ボスの欄の位置を [HUD] 行に出す
//   -netAutoRulesLog                          1 秒ごとに [RULES] 行(FRONT・死神の残り・乱入・ボーナスの資格)
public partial class NetAutoTest
{
    string hudModeArg = "";
    float joinRetryAt = 3f; int joinRetries;
    float runOverHold = 4f;          // -netAutoRunOverHold 秒: 試合の終わりから終了までの秒(結果の画面を撮る時に延ばす)
    float resultShotAfter = -1f;     // -netAutoResultShot 秒: 試合の終わりから何秒後に結果の画面を撮るか
    float runOverSeenRt = -1f; bool resultShotDone;
    float waitPlayersSec = 30f; // -netAutoWaitPlayers 秒(8人を順に起動すると30秒では足りない)
    readonly List<float> hudShotTimes = new List<float>();
    int hudShotIndex; bool rulesLog; float rulesLogNext; bool hudModeApplied;

    bool ParseRulesArg(string a, string next)
    {
        if (a == "-netAutoHudMode") { hudModeArg = next.ToLowerInvariant(); return true; }
        if (a == "-netAutoHudShots") { foreach (var t in next.Split(',')) if (float.TryParse(t, out float v)) hudShotTimes.Add(v); return true; }
        if (a == "-netAutoRulesLog") { rulesLog = true; return true; }
        if (a == "-netAutoRunOverHold") { float.TryParse(next, out runOverHold); return true; }
        if (a == "-netAutoResultShot") { float.TryParse(next, out resultShotAfter); return true; }
        if (a == "-netAutoWaitPlayers") { float.TryParse(next, out waitPlayersSec); return true; }
        return false;
    }

    void RulesTick(float runTime)
    {
        if (!hudModeApplied && hudModeArg != "")
        {
            hudModeApplied = true;
            HudPrefs.SetMode(hudModeArg == "simple" ? HudPrefs.Mode.Simple : hudModeArg == "hidden" ? HudPrefs.Mode.Hidden : HudPrefs.Mode.Standard);
            L($"HUD mode set to {HudPrefs.CurrentMode}");
        }
        var nm = NetMatch.Instance;
        if (nm != null && nm.RunOver && runOverSeenRt < 0f) runOverSeenRt = Time.realtimeSinceStartup;
        if (resultShotAfter >= 0f && !resultShotDone && runOverSeenRt >= 0f && Time.realtimeSinceStartup - runOverSeenRt >= resultShotAfter && !string.IsNullOrEmpty(shotDir))
        {
            resultShotDone = true;
            var gm = GameManager.Instance;
            L($"[RESULTSHOT] gameOver={(gm != null && gm.IsGameOver)} versusPaid={(gm != null && gm.VersusPaySet)} total={(gm != null ? gm.VersusTotalMile : -1)} wallet={(gm != null ? gm.TotalOwnedMile : -1)}");
            System.IO.Directory.CreateDirectory(shotDir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"result_{role}_P{NetCombat.LocalPlayerNumber}_{Screen.width}x{Screen.height}.png"));
        }
        if (rulesLog && nm != null && runTime >= rulesLogNext)
        {
            rulesLogNext = runTime + 1f;
            var f = NetMatch.FrontAlive();
            L($"[RULES] t={runTime:F1} mode={NetRunLauncher.ActiveMode} reaperMin={NetRunLauncher.ActiveReaperMinutes} reaperSec={nm.ReaperSeconds} left={nm.ReaperLeft:F1} triggered={nm.ReaperTriggered}({nm.ReaperTriggerSource}) world100k={WorldProgress.IsTriggered("death_100k")} warns={nm.ReaperWarningsShown} front=P{(f != null ? f.Pn : 0)} holds={nm.ReviveHoldsStarted}/{nm.ReviveHoldsCancelled}/{nm.ReviveHoldsCompleted} reviveCount={nm.ReviveCount} rejected={nm.ReviveRejected} mile={(GameManager.Instance != null ? GameManager.Instance.TotalOwnedMile : -1)} runMile={(GameManager.Instance != null ? GameManager.Instance.RunMile : -1)}");
        }
        if (hudShotIndex < hudShotTimes.Count && runTime >= hudShotTimes[hudShotIndex])
        {
            hudShotIndex++;
            Rect p = NetMatch.LastPartyHudRect, r = NetMatch.LastRevivePanelRect;
            string boss = BossHudPanel.VisibleBottomPx > 0f ? $"bossBottom={BossHudPanel.VisibleBottomPx:F0}" : "boss=none";
            L($"[HUD] t={runTime:F1} screen={Screen.width}x{Screen.height} stacked={GameManager.HudStacked} mode={HudPrefs.CurrentMode} rows={NetMatch.LastPartyRowsDrawn} party=({p.x:F0},{p.y:F0},{p.width:F0},{p.height:F0}) revive=({r.x:F0},{r.y:F0},{r.width:F0},{r.height:F0}) cardsTop={GameManager.RunCardsTopPx:F0} cardsLeftLimit={GameManager.RunCardsLeftLimitPx:F0} {boss}");
            if (!string.IsNullOrEmpty(shotDir))
            {
                System.IO.Directory.CreateDirectory(shotDir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"hud_{role}_P{NetCombat.LocalPlayerNumber}_{HudPrefs.CurrentMode}_{Screen.width}x{Screen.height}_t{runTime:F0}.png"));
            }
        }
    }
}
#endif

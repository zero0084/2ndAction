#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 2026-11-24(マスター依頼: マルチで1人のボス戦/カード選択/一時停止が全員の停止に広がる・ボスの後で距離の表示が端末ごとに違う)の確認。開発版だけ
//  -netAutoTeamLog 1          … 0.5秒ごとに TEAMLOG(実時刻・自分の位置・画面の距離・各自の距離・足止め・選択中/一時停止・ボス)
//  -netAutoHoldChoiceSec S    … この端末でカードの選択(レベルアップ/ボス報酬)が開いたら、S 秒選ばずに保持する(ほかの人が走り続けるか)
//  -netAutoPauseAt T -netAutoPauseFor S … T 秒にこの端末で一時停止のメニューを開き、S 秒後に閉じる
// 判定は Tools/net/teamrun.py(2台の記録を実時刻でそろえて比べる)
using System;
using System.Reflection;
using UnityEngine;

public partial class NetAutoTest
{
    bool teamLog; float teamLogTimer;
    float holdChoiceSec = 0f; float holdChoiceUntil = -1f; bool holdChoiceSeen;
    float pauseAt = -1f, pauseFor = 3f; bool pauseOpened, pauseClosed;
    static readonly FieldInfo PendingKindField = typeof(GameManager).GetField("pendingChoiceKind", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    static readonly FieldInfo PauseField = typeof(GameManager).GetField("showPauseMenu", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    bool teamArgsParsed;
    void ParseTeamArgs()
    {
        if (teamArgsParsed) return;
        teamArgsParsed = true;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-netAutoTeamLog") teamLog = a[i + 1] != "0";
            else if (a[i] == "-netAutoHoldChoiceSec") float.TryParse(a[i + 1], out holdChoiceSec);
            else if (a[i] == "-netAutoPauseAt") float.TryParse(a[i + 1], out pauseAt);
            else if (a[i] == "-netAutoPauseFor") float.TryParse(a[i + 1], out pauseFor);
        }
    }

    // カードを選ばずに保持している間は true(PickLevelUpCard が選ばない)
    bool TeamHoldsChoice(GameManager gm)
    {
        ParseTeamArgs();
        if (holdChoiceSec <= 0f || gm == null) return false;
        // ボスの報酬のカードだけを保持する(レベルアップのカードはすぐ選ぶ)
        bool bossReward = PendingKindField != null && PendingKindField.GetValue(gm).ToString() == "BossReward";
        bool waiting = gm.IsRewardSequenceWaitingForSelection && bossReward;
        if (waiting && !holdChoiceSeen) { holdChoiceSeen = true; holdChoiceUntil = runTime + holdChoiceSec; L($"TEAMHOLD boss reward choice opened on {role} me=P{NetCombat.LocalPlayerNumber} - holding {holdChoiceSec:F0}s"); }
        if (!waiting) { if (holdChoiceSeen && holdChoiceUntil > 0f) { holdChoiceSeen = false; } return false; }
        return runTime < holdChoiceUntil;
    }

    void TeamRunTick(GameManager gm)
    {
        ParseTeamArgs();
        if (gm == null || step != Step.Running) return;
        // 一時停止のメニュー(マルチでは時間を止めず、自分だけ止まる)
        if (pauseAt >= 0f && PauseField != null)
        {
            if (!pauseOpened && runTime >= pauseAt) { pauseOpened = true; PauseField.SetValue(gm, true); L($"TEAMPAUSE open on {role} me=P{NetCombat.LocalPlayerNumber} ts={Time.timeScale:F2}"); }
            if (pauseOpened && !pauseClosed && runTime >= pauseAt + pauseFor) { pauseClosed = true; PauseField.SetValue(gm, false); L($"TEAMPAUSE close on {role} ts={Time.timeScale:F2}"); }
        }
        if (!teamLog) return;
        teamLogTimer -= Time.unscaledDeltaTime;
        if (teamLogTimer > 0f) return;
        teamLogTimer = 0.5f;
        var pc = PlayerController.Instance;
        var bm = BossManager.Instance;
        int bosses = 0;
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && b.isActiveAndEnabled) bosses++;
        double x = pc != null ? pc.transform.position.x + FloatingOrigin.Offset : 0;
        bool paused = PauseField != null && (bool)PauseField.GetValue(gm);
        bool hold = NetCombat.Authority ? (bm != null && bm.HoldsRun) : NetCombat.RemoteBossHold;
        L($"TEAMLOG utc={DateTime.UtcNow:HH:mm:ss.fff} t={runTime:F1} role={role} me=P{NetCombat.LocalPlayerNumber} mode={NetMatch.Mode} x={x:F2} raw={(pc != null ? pc.DistanceExact : 0):F2} hud={gm.HudDistanceExact:F2} own={gm.MaxDistanceExact:F2} front={WorldProgress.FrontHigh:F2} " +
          $"haltF={BossHalt.Factor:F2} holding={BossHalt.Holding} choosing={(pc != null && pc.NetIsChoosing)} paused={paused} hold={hold} phase={(bm != null && bm.IsBossPhase)} bosses={bosses} ts={Time.timeScale:F2} recv={TeamDistance.Received} dup={NetMatch.BossRewardOffersIgnored} en={EnemyCountActive()} spawned={(EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies : -1)} hsL={HitStop.LocalApplied} hsS={HitStop.LocalSkipped} hsLocal={HitStop.LocalFrozen} snaps={TeamDistance.Snaps} fightOver={(bm != null && bm.MpBossFightOver)}");
    
    static int EnemyCountActive() { int n = 0; foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null && e.isActiveAndEnabled && !e.IsDying) n++; return n; } // 2026-11-25
}
}
#endif

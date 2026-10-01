#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 死神三姉妹に捕まって死んだ後に止まる件(2026-10-01)。 -qaReaperDeath <dir> [-qaReaperCases a,b] [-qaReaperStages ...]
// 姉妹ごと(荒野街道=長女/自然洞窟=次女/天空回廊=三女)に、状態を重ねて「捕まる→即死→RESULT→ホーム」まで進むかを見る。
//  warp   : 99,900mへワープして本来の流れ(100,000mで出現)で捕まる
//  normal : その場で出す
//  fast   : 高速(自然上限付近〜さらに上)のまま捕まる
//  jump / attack / hurt : 大鎌の予告中にジャンプ/攻撃/被弾
//  card   : 大鎌の予告中にカード選択を出し、選んだ直後(閉じる演出中)に一撃
//  boss   : ボスと戦っている最中に捕まる
//  repeat : 死んだ後に即死/ダメージ/死神の一撃を何度も重ねる
public partial class QaSweep
{
    IEnumerator ReaperDeathMode()
    {
        Application.targetFrameRate = 60;
        string[] stages = Arg("-qaReaperStages", "wasteland_road,natural_cave,sky_corridor").Split(',');
        string[] cases = Arg("-qaReaperCases", "warp,bossgate,normal,fast,jump,attack,hurt,card,boss,repeat").Split(',');
        GameManager.QaLegacyDeathBehaviour = Arg("-qaLegacyDeath", "0") == "1";
        L($"[reaperDeath] legacy death behaviour = {GameManager.QaLegacyDeathBehaviour}");
        string[] chars = { "swordsman", "miko", "ninja", "noble_lady", "gunslinger", "mage", "vampire", "fighter", "dragon_lancer" };
        int k = 0, ok = 0, total = 0;
        foreach (string stage in stages)
            foreach (string c in cases)
            {
                total++;
                bool pass = false;
                yield return ReaperDeathCase(stage, chars[k++ % chars.Length], c, r => pass = r);
                if (pass) ok++;
            }
        L($"[reaperDeath] {ok}/{total} cases reached RESULT -> HOME without a freeze");
    }

    IEnumerator ReaperDeathCase(string stage, string ch, string cond, System.Action<bool> done)
    {
        string tag = $"{stage}/{cond}/{ch}";
        yield return BeginRun(ch, stage);
        stopKeepAlive = true;
        autoPickHold = false; // 選択は自動で選ぶ(card の時も自動選択の直後=閉じる演出中に一撃が来る)
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        livesSet.Invoke(gm, new object[] { 30 });
        var bm = BossManager.Instance;
        PlayerController.DebugSpeedScale = cond == "fast" ? 3.2f : 1f;
        float t0 = Time.realtimeSinceStartup;

        if (cond == "warp")
        {
            gm.DebugWarpToDistance(99900f);
        }
        else if (cond == "bossgate")
        {
            // 99,000mのボス関門 → 倒さずにラン再開 → ボスが生きたまま100,000mで死神(本来の流れが重なる場面)
            gm.DebugWarpToDistance(98700f);
            PlayerController.DebugSpeedScale = 2.5f;
        }
        else
        {
            if (cond == "boss" && bm != null) bm.NetTestSpawnWild(WildBossKind.Wolf, 1);
            if (bm != null) { bm.enabled = true; bm.DebugSpawnReaper(); }
        }

        // 捕まえ(大鎌の予告)まで待つ。その間HPは保つ(雑魚/障害物で先に死なないように)
        ReaperBase r = null;
        float w = 0f;
        bool applied = false;
        while (!gm.IsGameOver && w < 150f)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            if (gm.Lives < 30 && !applied) livesSet.Invoke(gm, new object[] { 30 });
            r = ReaperBase.Active;
            if (r == null) continue;
            if (!applied && r.CurrentPhase == ReaperBase.Phase.Captured)
            {
                applied = true;
                if (cond == "bossgate") L($"[reaperDeath] {tag}: bossPhase={(bm != null && bm.IsBossPhase)} alive={(bm != null ? bm.AliveBossCount : 0)} resumed={(bm != null && bm.RunResumed)} d={gm.MaxDistance:F0}");
                L($"[reaperDeath] {tag}: captured after {w:F1}s gap={r.Gap:F2} speed={GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h boss={(bm != null && bm.IsBossPhase)} -> apply '{cond}'");
                if (cond == "jump") { pc.debugInjectFlick = PlayerController.FlickDirection.Up; yield return null; yield return null; pc.debugInjectFlick = null; }
                else if (cond == "attack") { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; yield return null; yield return null; pc.debugInjectFlick = null; }
                else if (cond == "hurt") { livesSet.Invoke(gm, new object[] { 30 }); pc.TakeDamage(false, "qa-hurt"); }
                else if (cond == "card")
                {
                    gm.GrantBonusCardChoice();
                    float w2 = 0f; while (!gm.IsRewardSequenceWaitingForSelection && w2 < 8f) { yield return null; w2 += Time.unscaledDeltaTime; }
                    L($"[reaperDeath] {tag}: card choice open during the windup (auto pick next)");
                }
            }
            if (cond == "fast" && w > 60f) PlayerController.DebugSpeedScale = 1f; // 追いつけない時の保険
        }
        float caught = w;
        bool died = gm.IsGameOver;
        L($"[reaperDeath] {tag}: died={died} after {caught:F1}s reason={gm.DeathReason} reaper={(r != null ? r.GetType().Name : "-")} phase={(r != null ? r.CurrentPhase.ToString() : "-")} strikes={(r != null ? r.Strikes : 0)}");
        if (!died)
        {
            L($"[reaperDeath] {tag}: [no capture within 150s] {StallState()}");
            Shot($"rd_{stage}_{cond}_nocapture");
            Check(false, $"{tag}: reaper captured and killed the player");
            PlayerController.DebugSpeedScale = 1f; autoPickHold = false;
            yield return EndRun();
            done(false);
            yield break;
        }

        if (cond == "repeat")
        {
            for (int i = 0; i < 4; i++)
            {
                gm.ReapPlayer("qa-repeat");
                gm.TryDamagePlayer(true, "qa-repeat", 5);
                pc.TakeDamage(false, "qa-repeat");
                yield return null;
            }
        }

        // 死亡後: 時間/死神/結果画面/Retryまで
        int strikesAtDeath = r != null ? r.Strikes : 0;
        float minScale = 1f, pausedFor = 0f;
        float tt0 = Time.time;
        w = 0f;
        while (w < gm.retryDelayAfterGameOver + 1.2f)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            minScale = Mathf.Min(minScale, Time.timeScale);
            if (Time.timeScale == 0f) pausedFor += Time.unscaledDeltaTime;
            if (w > 1f && w - Time.unscaledDeltaTime <= 1f) Shot($"rd_{stage}_{cond}_dead");
        }
        bool retryOk = gm.RetryAllowedNow;
        bool resultShown = gm.ResultShown;
        bool reaperStopped = r == null || r.RunEnded;
        int strikesAfter = r != null ? r.Strikes - strikesAtDeath : 0;
        L($"[reaperDeath] {tag}: after death timeScale={Time.timeScale:F2} min={minScale:F2} pausedFor={pausedFor:F1}s gameTime+={Time.time - tt0:F1}s finishRunCalls={gm.FinishRunCalls} ignoredAfterDeath={gm.DamageAfterDeathIgnored} result={resultShown} retryAllowed={retryOk} reaperStopped={reaperStopped} strikesAfterDeath={strikesAfter} reasons={TimeControl.DescribeActiveReasons()} choice={gm.LevelUpPending} seq={gm.IsRewardSequenceRunning}");
        Shot($"rd_{stage}_{cond}_result");
        Check(gm.FinishRunCalls == 1, $"{tag}: death finished exactly once ({gm.FinishRunCalls})");
        Check(resultShown && retryOk, $"{tag}: RESULT shown and Tap to Retry available");
        Check(Time.timeScale > 0f && pausedFor < 1f, $"{tag}: time is not left stopped after death (paused {pausedFor:F1}s)");
        Check(reaperStopped && strikesAfter == 0, $"{tag}: reaper stopped at death (strikes after {strikesAfter})");
        Check(!gm.LevelUpPending && !gm.IsRewardSequenceRunning, $"{tag}: no card choice left open after death");

        // Tap to Retry と同じ処理でホームへ
        var old = gm;
        typeof(GameManager).GetMethod("RetryWithTransition", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        bool home = GameManager.Instance != null && GameManager.Instance != old && !GameManager.Instance.HasStarted;
        L($"[reaperDeath] {tag}: home={home} after {w:F1}s timeScale={Time.timeScale:F2}");
        Check(home && Time.timeScale > 0f, $"{tag}: back to HOME after the result");
        gm = GameManager.Instance;
        PlayerController.DebugSpeedScale = 1f;
        autoPickHold = false;
        yield return new WaitForSecondsRealtime(0.8f);
        done(died && resultShown && retryOk && home && gm != null && Time.timeScale > 0f && strikesAfter == 0);
    }
}
#endif

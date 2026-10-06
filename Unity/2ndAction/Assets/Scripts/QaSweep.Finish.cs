#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Enemy FINISH System(2026-10-06)の確認: -qaFinish <dir> [-qaFinishOnly ABCDEFGHIJKLMNOPQR] [-qaFinishShots 1]
//  A ゴブリンを実際の前攻撃で倒す: その瞬間に本体が消え(当たり判定/接触なし)、報酬が同じフレームで1回、見た目が飛んで弾ける
//  C 後ろの攻撃 → 左へ  D 上攻撃 → 上へ(星)  E 空中  F 叩きつけ  G HEAVY  H OVERKILL(実際の攻撃)
//  I 5体同時: 報酬5回、HitStop は積み重ならない  J 12体同時: 負荷(フレーム時間)/絵のプールが足りる
//  K 空中の敵  L 大型の敵  M ボスは対象外  N/O/P 100/200/300km/h: 画面基準で飛んで弾ける(残らない)
//  Q カードの選択の直前に撃破: 報酬は1回、選択中は止まり、閉じたら続く  R GAME OVER の直前に撃破: 見た目は片付き、結果へ進める
public partial class QaSweep
{
    bool FinishCase(char c) { string o = Arg("-qaFinishOnly", ""); return o == "" || o.IndexOf(c) >= 0; }
    readonly List<string> finishEvents = new List<string>();

    IEnumerator FinishMode()
    {
        Application.targetFrameRate = 60;
        FinishFx.QaEvent += s => finishEvents.Add(s);
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        GameManager.BlockExpGain = true; // レベルアップの選択で止まらないように(Q だけ自分で出す)
        autoPickHold = true;
        yield return new WaitForSeconds(1.5f);
        bool shots = Arg("-qaFinishShots", "0") == "1";

        if (FinishCase('A')) yield return FinishReal("A", PlayerController.FlickDirection.Forward, false, FinishShape.Side, 1, false, shots);
        if (FinishCase('C')) yield return FinishReal("C", PlayerController.FlickDirection.Backward, true, FinishShape.Side, -1, false, shots);
        if (FinishCase('D')) yield return FinishReal("D", PlayerController.FlickDirection.Up, false, FinishShape.Up, 0, false, shots);
        if (FinishCase('H')) yield return FinishReal("H", PlayerController.FlickDirection.Forward, false, FinishShape.Side, 1, true, shots);
        if (FinishCase('E')) yield return FinishDebugCase("E", "goblin", 1, PlayerAttackKind.Normal, false, false, true, FinishShape.Aerial, shots);
        if (FinishCase('F')) yield return FinishDebugCase("F", "goblin", 4, PlayerAttackKind.Down, true, false, true, FinishShape.Slam, shots);
        if (FinishCase('G')) yield return FinishDebugCase("G", "goblin", 1, PlayerAttackKind.Normal, true, false, false, FinishShape.Side, shots);
        if (FinishCase('I')) yield return FinishDebugCase("I", "goblin", 5, PlayerAttackKind.Normal, true, false, false, FinishShape.Side, shots);
        if (FinishCase('J')) yield return FinishDebugCase("J", "goblin", 12, PlayerAttackKind.Down, true, true, true, FinishShape.Slam, shots);
        if (FinishCase('K')) yield return FinishDebugCase("K", "harpy", 2, PlayerAttackKind.Normal, false, false, true, FinishShape.Aerial, shots);
        if (FinishCase('L')) yield return FinishDebugCase("L", "heavy_ogre", 1, PlayerAttackKind.Normal, true, false, false, FinishShape.Side, shots);
        if (FinishCase('M')) yield return FinishBossCase();
        if (FinishCase('N')) yield return FinishSpeedCase("N", 100f, shots);
        if (FinishCase('O')) yield return FinishSpeedCase("O", 200f, shots);
        if (FinishCase('P')) yield return FinishSpeedCase("P", 320f, shots);
        SetKmh(0f); PlayerController.DebugSpeedScale = 1f;
        if (FinishCase('Q')) yield return FinishChoiceCase();
        if (FinishCase('R')) yield return FinishGameOverCase();
        GameManager.BlockExpGain = false;
        autoPickHold = false;
        if (gm != null && gm.HasStarted) yield return EndRun();
    }

    void SetEnemyHp(EnemyController e, int hp)
    {
        var m = typeof(EnemyController).GetMethod("EnsureHp", BindingFlags.Instance | BindingFlags.NonPublic);
        m?.Invoke(e, null);
        SetPrivate(e, "hp", hp);
    }

    // 実際の攻撃(フリック)で倒す
    IEnumerator FinishReal(string tag, PlayerController.FlickDirection flick, bool behind, FinishShape wantShape, int wantDir, bool overkill, bool shots)
    {
        L($"== {tag}: 実際の攻撃 {flick}{(overkill ? " OVERKILL" : "")} ==");
        yield return WaitGrounded();
        var list = FinishDebug.Spawn("goblin", 1, behind ? 1.2f : 1.4f, 1f, behind);
        var e = list.FirstOrDefault();
        Check(e != null, $"{tag}: goblin spawned");
        if (e == null) yield break;
        int ap = pc.EffectiveAttackPower;
        SetEnemyHp(e, overkill ? 1 : Mathf.Max(1, Mathf.RoundToInt(ap * 0.6f)));
        int kills0 = gm.EnemyKillCount, deaths0 = EnemyController.FinishDeaths, bursts0 = FinishFx.Bursts;
        bool sameFrame = false, inactiveSameFrame = false; float killedAt = -1f;
        float w = 0f;
        while (w < 4f && !e.IsDying)
        {
            // 近づいたら攻撃(後ろは近くに居るうちに)
            float dx = e.transform.position.x - pc.transform.position.x;
            if (Mathf.Abs(dx) < 2.6f) { StartCoroutine(Flick(flick)); yield return null; yield return null; }
            if (e.IsDying)
            {
                sameFrame = gm.EnemyKillCount == kills0 + 1;
                inactiveSameFrame = !e.gameObject.activeSelf;
                killedAt = Time.unscaledTime;
                break;
            }
            yield return new WaitForSeconds(0.12f); w += 0.12f;
        }
        Check(e.IsDying, $"{tag}: the goblin was killed by the {flick} attack");
        if (!e.IsDying) { Destroy(e.gameObject); yield break; }
        var fi = e.LastFinish;
        L($"[{tag}] finish {fi} kills {kills0}->{gm.EnemyKillCount} sameFrame={sameFrame} inactive={inactiveSameFrame}");
        Check(inactiveSameFrame && sameFrame && EnemyController.FinishDeaths == deaths0 + 1, $"{tag}: at HP 0 the enemy is gone at once (no collider/AI) and the reward is counted once in the same frame");
        Check(fi.shape == wantShape && (wantDir == 0 || fi.dir == wantDir), $"{tag}: finish direction from the attack ({fi.shape}/{fi.dir}, want {wantShape}/{wantDir})");
        if (overkill) Check(fi.type == FinishType.Overkill, $"{tag}: a huge hit on a weak enemy is an OVERKILL ({fi.type})");
        if (shots) yield return FinishShots(tag);
        w = 0f; while (FinishFx.Bursts == bursts0 && w < 2f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(FinishFx.Bursts > bursts0 && w < 1.3f, $"{tag}: the flying body bursts at the screen edge ({w:F2}s)");
        yield return new WaitForSeconds(0.3f);
        Check(gm.EnemyKillCount == kills0 + 1, $"{tag}: no second reward ({gm.EnemyKillCount - kills0})");
    }

    IEnumerator FinishShots(string tag)
    {
        float[] at = { 0.0f, 0.1f, 0.2f, 0.32f, 0.5f };
        float t0 = Time.unscaledTime;
        for (int i = 0; i < at.Length; i++)
        {
            while (Time.unscaledTime - t0 < at[i]) yield return null;
            Shot($"finish_{tag}_{i}");
            yield return null;
        }
    }

    IEnumerator FinishDebugCase(string tag, string enemy, int count, PlayerAttackKind kind, bool heavy, bool overkill, bool aerial, FinishShape wantShape, bool shots)
    {
        L($"== {tag}: {enemy} ×{count} {kind}{(heavy ? " HEAVY" : "")}{(overkill ? " OVERKILL" : "")}{(aerial ? " aerial" : "")} ==");
        yield return WaitGrounded();
        var list = FinishDebug.Spawn(enemy, count, kind == PlayerAttackKind.Down ? 3f : 5f, count > 6 ? 0.9f : 1.3f);
        yield return null;
        int kills0 = gm.EnemyKillCount, bursts0 = FinishFx.Bursts, slam0 = finishEvents.Count(s => s == "slam impact");
        int exhausted0 = FinishFx.PoolExhausted;
        float stopStart = Time.realtimeSinceStartup;
        int killed = FinishDebug.Kill(list, kind, heavy, overkill, aerial);
        bool allGone = list.All(e => e == null || !e.gameObject.activeSelf);
        int sameFrameKills = gm.EnemyKillCount - kills0;
        // HitStop: 止まっていた実時間(積み重なっていないか)
        float stopped = 0f; float w = 0f; float maxDt = 0f, sumDt = 0f; int frames = 0;
        while (w < 1.6f)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime; w += dt;
            if (Time.timeScale == 0f && w < 0.6f) stopped += dt;
            if (w > 0.05f) { maxDt = Mathf.Max(maxDt, dt); sumDt += dt; frames++; }
            if (shots && frames == 2) StartCoroutine(FinishShots(tag));
        }
        var t = FinishTuning.I;
        float maxStop = Mathf.Max(t.killHitStop, t.heavyHitStop, t.overkillHitStop, t.slamHitStop);
        var fis = list.Where(e => e != null).Select(e => e.LastFinish).ToList();
        string shapes = string.Join(",", fis.Select(f => f.ToString()).Distinct());
        L($"[{tag}] killed {killed}/{count} sameFrameKills {sameFrameKills} allGone {allGone} stopped {stopped:F3}s (max single {maxStop:F2}) bursts +{FinishFx.Bursts - bursts0} slam impacts +{finishEvents.Count(s => s == "slam impact") - slam0} pool {FinishFx.Instance?.ActiveSprites}/{FinishFx.Instance?.PoolSize} exhausted +{FinishFx.PoolExhausted - exhausted0} frame avg {(frames > 0 ? sumDt / frames * 1000f : 0f):F1}ms max {maxDt * 1000f:F1}ms finishes [{shapes}]");
        Check(killed == count && sameFrameKills == count && allGone, $"{tag}: {count} kills are confirmed in the same frame (rewards x{sameFrameKills}), the bodies are gone at once");
        Check(fis.All(f => f.shape == wantShape), $"{tag}: finish shape {wantShape} ({shapes})");
        if (heavy && !overkill) Check(fis.All(f => f.type == FinishType.Heavy), $"{tag}: HEAVY finish");
        if (overkill) Check(fis.All(f => f.type == FinishType.Overkill), $"{tag}: OVERKILL finish");
        Check(stopped <= maxStop + 0.06f, $"{tag}: HitStop does not stack with {count} kills ({stopped:F3}s <= {maxStop:F2}+)");
        Check(FinishFx.Bursts - bursts0 == count, $"{tag}: every body bursts once ({FinishFx.Bursts - bursts0}/{count})");
        if (wantShape == FinishShape.Slam) Check(finishEvents.Count(s => s == "slam impact") - slam0 == count, $"{tag}: each slammed enemy hits the ground first");
        Check(FinishFx.Instance != null && FinishFx.Instance.ActiveBodies == 0, $"{tag}: nothing left flying after 1.6s");
        if (count >= 10) Check(FinishFx.PoolExhausted == exhausted0 && maxDt < 0.05f, $"{tag}: {count} simultaneous kills: pool enough, no long frame (max {maxDt * 1000f:F1}ms)");
        if (enemy == "harpy") Check(fis.All(f => f.flying), $"{tag}: flying enemies use the flying trajectory (no gravity)");
        if (enemy == "heavy_ogre") Check(fis.All(f => f.mass >= 1.8f), $"{tag}: a large enemy is heavier (mass {fis.FirstOrDefault().mass:F1})");
        Check(gm.EnemyKillCount - kills0 == count, $"{tag}: no extra rewards later ({gm.EnemyKillCount - kills0})");
    }

    IEnumerator FinishBossCase()
    {
        L("== M: ボス ==");
        int plays0 = FinishFx.Plays, deaths0 = EnemyController.FinishDeaths;
        var bm = BossManager.Instance;
        typeof(BossManager).GetMethod("DebugForceSpawn", NP).Invoke(bm, new object[] { WildBossKind.Wolf, 1 });
        float w = 0f; while (bm.AliveBossCount <= 0 && w < 8f) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(1.5f);
        var boss = WildAlive().FirstOrDefault();
        if (boss != null) boss.TakeDamage(boss.Hp + 10, boss.CenterWorld);
        w = 0f; while ((bm.IsBossPhase || gm.IsRewardSequenceRunning) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.5f);
        L($"[M] boss killed={boss != null} finish plays +{FinishFx.Plays - plays0}");
        Check(boss != null && FinishFx.Plays == plays0 && EnemyController.FinishDeaths == deaths0, "M: bosses keep their own death presentation (no zako FINISH)");
    }

    IEnumerator FinishSpeedCase(string tag, float kmh, bool shots)
    {
        L($"== {tag}: {kmh:0}km/h ==");
        SetKmh(kmh);
        yield return new WaitForSeconds(1.0f);
        float measured = pc.CurrentAutoRunSpeed * GameManager.KmhPerMps;
        foreach (var (kind, behind, aerial) in new[] { (PlayerAttackKind.Normal, false, false), (PlayerAttackKind.Normal, true, false), (PlayerAttackKind.Up, false, false), (PlayerAttackKind.Down, false, true) })
        {
            var list = FinishDebug.Spawn("goblin", 2, behind ? 2f : 4f, 1.2f, behind);
            int b0 = FinishFx.Bursts; finishEvents.Clear();
            FinishDebug.Kill(list, kind, false, false, aerial);
            float w = 0f;
            while (FinishFx.Bursts < b0 + 2 && w < 2.5f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[{tag}] {measured:F0}km/h {kind}{(behind ? " back" : "")}: bursts +{FinishFx.Bursts - b0} in {w:F2}s, still flying {FinishFx.Instance?.ActiveBodies}");
            Check(FinishFx.Bursts - b0 == 2 && w < 1.4f, $"{tag}: at {measured:F0}km/h the {kind}{(behind ? "(back)" : "")} finish leaves and bursts on screen in time ({w:F2}s)");
            if (shots && kind == PlayerAttackKind.Normal && !behind) Shot($"finish_{tag}_speed");
            yield return new WaitForSeconds(0.3f);
        }
    }

    IEnumerator FinishChoiceCase()
    {
        L("== Q: カードの選択の直前 ==");
        GameManager.BlockExpGain = false;
        var list = FinishDebug.Spawn("goblin", 3, 4f, 1.2f);
        int kills0 = gm.EnemyKillCount, b0 = FinishFx.Bursts;
        FinishDebug.Kill(list, PlayerAttackKind.Normal, true, false, false);
        gm.GrantBonusCardChoice();
        float w = 0f; while (!gm.SprintChoiceOpen && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        int bodiesInChoice = FinishFx.Instance.ActiveBodies;
        yield return new WaitForSecondsRealtime(1.0f);
        int burstsDuringChoice = FinishFx.Bursts - b0;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        w = 0f; while (seq != null && !seq.IsWaitingForSelection && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        if (seq != null && seq.IsWaitingForSelection) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.25f); seq.OnCardClicked(0); }
        w = 0f; while (gm.SprintChoiceOpen && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        w = 0f; while (FinishFx.Bursts < b0 + 3 && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        L($"[Q] choice opened={w < 5f} bodies at choice {bodiesInChoice}, bursts during choice {burstsDuringChoice}, total bursts +{FinishFx.Bursts - b0}, kills +{gm.EnemyKillCount - kills0}");
        Check(gm.EnemyKillCount - kills0 == 3, $"Q: rewards counted once each across the card choice ({gm.EnemyKillCount - kills0})");
        Check(FinishFx.Bursts - b0 == 3, "Q: the finish visuals continue after the choice and burst once each");
        GameManager.BlockExpGain = true;
    }

    IEnumerator FinishGameOverCase()
    {
        L("== R: GAME OVER の直前 ==");
        var list = FinishDebug.Spawn("goblin", 4, 4f, 1.2f);
        int kills0 = gm.EnemyKillCount;
        FinishDebug.Kill(list, PlayerAttackKind.Down, true, true, true);
        yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true;
        for (int i = 0; i < 60 && !gm.IsGameOver; i++) { SetPrivate(pc, "hitInvincibleTimer", 0f); gm.TryDamagePlayer(false, "qa-finish", CombatScale.PlayerHeavyHit); yield return new WaitForSecondsRealtime(0.03f); }
        yield return new WaitForSecondsRealtime(0.5f);
        L($"[R] game over {gm.IsGameOver}, finish sprites left {FinishFx.Instance?.ActiveSprites}, kills +{gm.EnemyKillCount - kills0}");
        Check(gm.IsGameOver && (FinishFx.Instance == null || FinishFx.Instance.ActiveSprites == 0), "R: game over is not blocked and the finish visuals are cleaned up");
        Check(gm.EnemyKillCount - kills0 == 4, "R: the rewards of the kills just before the game over were already counted");
        yield return new WaitForSecondsRealtime(gm.QuietFinish ? 0.5f : 2.5f);
        var old = gm;
        GameManager.DebugResultTapPending = true;
        float w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(GameManager.Instance != null && GameManager.Instance != old, "R: the result screen proceeds (retry) after a finish right before the game over");
        gm = GameManager.Instance;
    }
}
#endif

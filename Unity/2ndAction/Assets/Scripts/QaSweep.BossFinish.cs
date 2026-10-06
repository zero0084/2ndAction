#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// BOSS FINISH SYSTEM(2026-10-06)の確認: -qaBossFinish <dir> [-qaBfOnly ABCDEFGHIJKLMNOQRSTUXYZP] [-qaBfShots 1] [-qaBfAll 1]
//  A〜F 6つのプロファイルの基準のボス(オオカミ/黒騎士/ドラゴン/クリスタルゴーレム/リヴァイアサン/タイタン)
//  G/H/I/J 前/上/叩きつけ/飛び道具で撃破(最初の反応)  K 必殺技中  L 崩し(BREAK)中  M 段階の切り替えの直後
//  N ラン再開の前 / O ラン再開の後(走りは止め直さない)  Q 再戦(短い) / R 初撃破(少し長い)
//  S/T/U 100/200/300km/h(体が画面から流れない)  X 撃破の直後に GAME OVER  Y ボスの攻撃の残りが消える  Z リトライの後に残らない
//  P フェニックス: 1回目の HP 0 は復活(BOSS FINISH に入らない)、最後だけ入る
//  共通: HP 0 の同じフレームで 死亡/当たり判定なし/報酬、見た目の後に非表示 → 遭遇の終了(ボス報酬の選択)
public partial class QaSweep
{
    bool BfCase(char c) { string o = Arg("-qaBfOnly", ""); return o == "" || o.IndexOf(c) >= 0; }
    readonly List<string> bfEvents = new List<string>();

    IEnumerator BossFinishMode()
    {
        Application.targetFrameRate = 60;
        BossFinish.QaEvent += s => bfEvents.Add(s);
        bool shots = Arg("-qaBfShots", "0") == "1";
        // 荒野街道
        yield return BfBeginStage("wasteland_road");
        if (BfCase('A')) yield return BfKill("A", 0, (int)WildBossKind.Wolf, BossFinalAttack.Forward, BossDeathProfile.Beast, shots, first: 1);
        if (BfCase('B')) yield return BfKill("B", 0, (int)WildBossKind.BlackKnight, BossFinalAttack.Forward, BossDeathProfile.Humanoid, shots, first: 1);
        if (BfCase('C')) yield return BfKill("C", 0, (int)WildBossKind.Dragon, BossFinalAttack.Aerial, BossDeathProfile.Flying, shots, first: 1);
        if (BfCase('G')) yield return BfKill("G", 0, (int)WildBossKind.Spider, BossFinalAttack.Forward, BossDeathProfile.Beast, false);
        if (BfCase('H')) yield return BfKill("H", 0, (int)WildBossKind.Wolf, BossFinalAttack.Up, BossDeathProfile.Beast, shots);
        if (BfCase('I')) yield return BfKill("I", 0, (int)WildBossKind.Cyclops, BossFinalAttack.Slam, BossDeathProfile.Giant, shots);
        if (BfCase('J')) yield return BfKill("J", 0, (int)WildBossKind.Golem, BossFinalAttack.Projectile, BossDeathProfile.Golem, false);
        if (BfCase('K')) yield return BfKill("K", 0, (int)WildBossKind.Serpent, BossFinalAttack.Forward, BossDeathProfile.Serpent, false, prep: "ultimate");
        if (BfCase('L')) yield return BfKill("L", 0, (int)WildBossKind.GoblinRider, BossFinalAttack.Forward, BossDeathProfile.Beast, false, prep: "break");
        if (BfCase('M')) yield return BfKill("M", 0, (int)WildBossKind.Griffin, BossFinalAttack.Forward, BossDeathProfile.Flying, false, prep: "phase");
        if (BfCase('Y')) yield return BfKill("Y", 0, (int)WildBossKind.Hydra, BossFinalAttack.Forward, BossDeathProfile.Serpent, false, prep: "hazards");
        if (BfCase('N')) yield return BfKill("N", 0, (int)WildBossKind.Wolf, BossFinalAttack.Forward, BossDeathProfile.Beast, false, prep: "noresume");
        if (BfCase('O')) yield return BfKill("O", 0, (int)WildBossKind.Wolf, BossFinalAttack.Forward, BossDeathProfile.Beast, false, prep: "resume");
        if (BfCase('Q')) yield return BfKill("Q", 0, (int)WildBossKind.Demon, BossFinalAttack.Forward, BossDeathProfile.Humanoid, false, first: 0);
        if (BfCase('R')) yield return BfKill("R", 0, (int)WildBossKind.Demon, BossFinalAttack.Forward, BossDeathProfile.Humanoid, false, first: 1);
        if (BfCase('S')) yield return BfKill("S", 0, (int)WildBossKind.Wolf, BossFinalAttack.Back, BossDeathProfile.Beast, shots, prep: "speed100");
        if (BfCase('T')) yield return BfKill("T", 0, (int)WildBossKind.Cyclops, BossFinalAttack.Forward, BossDeathProfile.Giant, false, prep: "speed200");
        if (BfCase('U')) yield return BfKill("U", 0, (int)WildBossKind.Griffin, BossFinalAttack.Forward, BossDeathProfile.Flying, shots, prep: "speed320");
        if (Arg("-qaBfAll", "0") == "1")
            foreach (WildBossKind k in System.Enum.GetValues(typeof(WildBossKind))) yield return BfKill("all_" + k, 0, (int)k, BossFinalAttack.Forward, BossDeathTuning.I.For(k == WildBossKind.Dragon ? "Dragon" : "Wild/" + k).profile, false, first: 0);
        if (BfCase('X')) yield return BfGameOver();
        if (BfCase('Z')) yield return BfAfterRetry();
        yield return EndRun();
        // 自然洞窟
        yield return BfBeginStage("natural_cave");
        if (BfCase('D')) yield return BfKill("D", 1, (int)CaveBossKind.CrystalGolem, BossFinalAttack.Forward, BossDeathProfile.Golem, shots, first: 1);
        if (Arg("-qaBfAll", "0") == "1")
            foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind))) yield return BfKill("all_" + k, 1, (int)k, BossFinalAttack.Forward, BossDeathTuning.I.For("Cave/" + k).profile, false, first: 0);
        yield return EndRun();
        // 天空回廊
        yield return BfBeginStage("sky_corridor");
        if (BfCase('E')) yield return BfKill("E", 2, (int)SkyBossKind.Leviathan, BossFinalAttack.Forward, BossDeathProfile.Serpent, shots, first: 1);
        if (BfCase('F')) yield return BfKill("F", 2, (int)SkyBossKind.Titan, BossFinalAttack.Forward, BossDeathProfile.Giant, shots, first: 1);
        if (BfCase('P')) yield return BfPhoenix();
        if (Arg("-qaBfAll", "0") == "1")
            foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind)))
            {
                if (k == SkyBossKind.Phoenix) continue;
                string key = k == SkyBossKind.Dragon ? "Dragon" : k == SkyBossKind.Majin ? "Majin" : "Sky/" + k;
                yield return BfKill("all_" + k, 2, (int)k, BossFinalAttack.Forward, BossDeathTuning.I.For(key).profile, false, first: 0);
            }
        yield return EndRun();
        BossFinish.DebugFirstKill = -1;
    }

    IEnumerator BfBeginStage(string stage)
    {
        yield return BeginRun("swordsman", stage);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = true;
        yield return new WaitForSeconds(1.5f);
    }

    Object BfFindBoss()
    {
        var wb = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && b.isActiveAndEnabled);
        if (wb != null) return wb;
        var dr = FindObjectsByType<DragonController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled);
        if (dr != null) return dr;
        return FindObjectsByType<MajinController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled);
    }

    IEnumerator BfWaitEncounterEnd()
    {
        var bm = BossManager.Instance;
        float w = 0f;
        while ((bm.IsBossPhase || gm.IsRewardSequenceRunning || BossFinish.AnyRunning) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator BfKill(string tag, int family, int kind, BossFinalAttack attack, BossDeathProfile wantProfile, bool shots, int first = -1, string prep = null)
    {
        L($"== {tag}: family {family} kind {kind} {attack} {prep} ==");
        yield return BfWaitEncounterEnd();
        var bm = BossManager.Instance;
        BossFinish.DebugFirstKill = first;
        bm.DebugSpawnBossForTest(family, kind);
        float w = 0f; Object boss = null;
        while (w < 15f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            boss = BfFindBoss();
            if (boss is WildBossBase wbb && !wbb.IsEntering && w > 1.2f) break;
            if ((boss is DragonController || boss is MajinController) && w > 4f) break;
        }
        Check(boss != null, $"{tag}: boss spawned");
        if (boss == null) yield break;
        var dbg = boss as IBossBattleDebug;
        float speedBefore = -1f;
        switch (prep)
        {
            case "ultimate": dbg?.DebugForceUltimate(); yield return new WaitForSeconds(0.6f); break;
            case "break": dbg?.DebugForceBreak(); yield return new WaitForSeconds(0.3f); break;
            case "phase": dbg?.DebugSetPhase(2); yield return null; break;
            case "hazards": yield return new WaitForSeconds(4f); break;
            case "resume": bm.DebugForceResume(); yield return new WaitForSeconds(1.2f); break;
            case "speed100": bm.DebugForceResume(); yield return new WaitForSeconds(0.6f); SetKmh(100f); yield return new WaitForSeconds(1f); break;
            case "speed200": bm.DebugForceResume(); yield return new WaitForSeconds(0.6f); SetKmh(200f); yield return new WaitForSeconds(1f); break;
            case "speed320": bm.DebugForceResume(); yield return new WaitForSeconds(0.6f); SetKmh(320f); yield return new WaitForSeconds(1f); break;
        }
        boss = BfFindBoss() ?? boss;
        if (boss == null || (boss is WildBossBase dead0 && dead0.IsDead)) { Check(false, $"{tag}: boss still alive before the kill"); yield break; }
        speedBefore = pc.CurrentAutoRunSpeed;
        int hz0 = FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length + FindObjectsByType<TrackedHazard>(FindObjectsSortMode.None).Length + CaveHazard.LiveCount
                + FindObjectsByType<SkyStrike>(FindObjectsSortMode.None).Length + FindObjectsByType<SkyWarnBand>(FindObjectsSortMode.None).Length;
        int kills0 = gm.BossKillCount, started0 = BossFinish.Started, done0 = BossFinish.Completed;
        var go = ((Component)boss).gameObject;
        bool ok = boss is WildBossBase b1 ? b1.DebugKillWithAttack(attack) : boss is DragonController b2 ? b2.DebugKillWithAttack(attack) : ((MajinController)boss).DebugKillWithAttack(attack);
        // 同じフレームで: 死亡 / 当たり判定なし / 報酬 / 見た目の開始 / 攻撃の残りなし
        bool collidersOff = go.GetComponentsInChildren<Collider2D>(true).All(c => !c.enabled);
        int hz1 = FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Count(x => x != null) + FindObjectsByType<TrackedHazard>(FindObjectsSortMode.None).Length + CaveHazard.LiveCount;
        var bf = go.GetComponent<BossFinish>();
        BossFinishInfo fi = bf != null ? bf.Info : default;
        BossDeathProfile prof = bf != null ? bf.Profile : wantProfile;
        float D = bf != null ? bf.Duration : 0f;
        L($"[{tag}] killed={ok} sameFrame: reward {gm.BossKillCount - kills0}, colliders off {collidersOff}, finish {(bf != null ? $"{prof} {fi} D={D:F1}" : "none")}, hazards {hz0}->{hz1}");
        Check(ok && gm.BossKillCount == kills0 + 1 && collidersOff && BossFinish.Started == started0 + 1, $"{tag}: at HP 0 the boss is dead in the same frame (reward counted, colliders off, finish started)");
        Check(prof == wantProfile, $"{tag}: death profile {prof} (want {wantProfile})");
        if (attack != BossFinalAttack.Other) Check(fi.attack == attack, $"{tag}: the final attack type is reflected ({fi.attack})");
        if (first >= 0) Check(fi.firstKill == (first == 1), $"{tag}: first kill = {fi.firstKill}");
        if (prep == "hazards" || prep == "ultimate") Check(hz1 == 0, $"{tag}: boss hazards are cleared at the death ({hz0} -> {hz1})");
        if (shots) StartCoroutine(BfShots(tag, D));
        // 見た目の間: 体が画面に残る(走行速度で流れない)/ 走りは止め直さない
        float t0 = Time.unscaledTime; float minVx = 9f, maxVx = -9f; float speedMin = 999f;
        var cam = Camera.main;
        while (BossFinish.Completed == done0 && Time.unscaledTime - t0 < 6f)
        {
            if (go.activeInHierarchy && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(go.transform.position + Vector3.up * 1f);
                minVx = Mathf.Min(minVx, vp.x); maxVx = Mathf.Max(maxVx, vp.x);
            }
            if (Time.timeScale > 0.9f) speedMin = Mathf.Min(speedMin, pc.CurrentAutoRunSpeed);
            yield return null;
        }
        float real = Time.unscaledTime - t0;
        L($"[{tag}] visual {real:F2}s real (D {D:F1}), boss viewport x {minVx:F2}..{maxVx:F2}, active after {go.activeSelf}, run speed {speedBefore * GameManager.KmhPerMps:F0} -> min {speedMin * GameManager.KmhPerMps:F0}km/h, phase {bm.IsBossPhase}");
        Check(BossFinish.Completed == done0 + 1 && !go.activeSelf, $"{tag}: the death visual ends and the boss is hidden ({real:F2}s)");
        Check(real < D + 0.9f && real > D * 0.8f, $"{tag}: length {real:F2}s fits the {(fi.firstKill ? "first kill" : "rematch")} setting ({D:F1}s)");
        Check(minVx > -0.25f && maxVx < 1.25f, $"{tag}: the dying boss stays on screen while running ({minVx:F2}..{maxVx:F2})");
        if (prep == "resume" || (prep != null && prep.StartsWith("speed")))
            Check(speedMin >= speedBefore * 0.9f, $"{tag}: the run is not stopped again by the boss death (speed {speedBefore * GameManager.KmhPerMps:F0} -> min {speedMin * GameManager.KmhPerMps:F0})");
        yield return BfWaitEncounterEnd();
        Check(!bm.IsBossPhase && !BossFinish.AnyRunning && CameraFollow.BossFinishZoom == 1f, $"{tag}: the encounter ends normally after the visual (boss reward) and nothing is left over");
        SetKmh(0f); PlayerController.DebugSpeedScale = 1f;
        BossFinish.DebugFirstKill = -1;
    }

    IEnumerator BfShots(string tag, float D)
    {
        float[] at = { 0.02f, 0.2f, 0.45f, 0.7f, 0.95f };
        float t0 = Time.unscaledTime;
        foreach (float a in at)
        {
            while (Time.unscaledTime - t0 < a * (D + 0.3f)) yield return null;
            Shot($"bossfinish_{tag}_{a:0.00}");
            yield return null;
        }
    }

    IEnumerator BfGameOver()
    {
        L("== X: 撃破の直後に GAME OVER ==");
        yield return BfWaitEncounterEnd();
        var bm = BossManager.Instance;
        bm.DebugSpawnBossForTest(0, (int)WildBossKind.Wolf);
        float w = 0f; WildBossBase b = null;
        while (w < 10f) { yield return null; w += Time.unscaledDeltaTime; b = BfFindBoss() as WildBossBase; if (b != null && !b.IsEntering && w > 1.2f) break; }
        if (b == null) { Check(false, "X: boss spawned"); yield break; }
        int kills0 = gm.BossKillCount;
        b.DebugKillWithAttack(BossFinalAttack.Forward);
        yield return null;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true;
        for (int i = 0; i < 60 && !gm.IsGameOver; i++) { SetPrivate(pc, "hitInvincibleTimer", 0f); gm.TryDamagePlayer(false, "qa-bossfinish", CombatScale.PlayerHeavyHit); yield return new WaitForSecondsRealtime(0.03f); }
        yield return new WaitForSecondsRealtime(0.5f);
        L($"[X] game over {gm.IsGameOver}, finish running {BossFinish.Running}, zoom {CameraFollow.BossFinishZoom}, presentation busy {TimeControl.PresentationBusy}, reward {gm.BossKillCount - kills0}");
        Check(gm.IsGameOver && BossFinish.Running == 0 && CameraFollow.BossFinishZoom == 1f && !TimeControl.PresentationBusy, "X: game over right after a boss kill stops the death visual cleanly (no zoom/slow left)");
        Check(gm.BossKillCount - kills0 == 1, "X: the boss reward was already counted at the kill");
        yield return new WaitForSecondsRealtime(gm.QuietFinish ? 0.5f : 2.5f);
        var old = gm;
        GameManager.DebugResultTapPending = true;
        w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(GameManager.Instance != null && GameManager.Instance != old, "X: the result proceeds to retry");
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(1f);
    }

    IEnumerator BfAfterRetry()
    {
        L("== Z: リトライの後 ==");
        yield return new WaitForSecondsRealtime(0.5f);
        int left = FindObjectsByType<BossFinish>(FindObjectsSortMode.None).Length;
        L($"[Z] finish components {left}, running {BossFinish.Running}, zoom {CameraFollow.BossFinishZoom}, presentation busy {TimeControl.PresentationBusy}, bosses {FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Length}");
        Check(left == 0 && BossFinish.Running == 0 && CameraFollow.BossFinishZoom == 1f && !TimeControl.PresentationBusy, "Z: after retry no death visual / zoom / slow motion is left");
        if (gm != null && !gm.HasStarted) yield return BfBeginStage("wasteland_road");
    }

    IEnumerator BfPhoenix()
    {
        L("== P: フェニックス ==");
        yield return BfWaitEncounterEnd();
        var bm = BossManager.Instance;
        BossFinish.DebugFirstKill = 1;
        bm.DebugSpawnBossForTest(2, (int)SkyBossKind.Phoenix);
        float w = 0f; WildBossBase b = null;
        while (w < 12f) { yield return null; w += Time.unscaledDeltaTime; b = BfFindBoss() as WildBossBase; if (b != null && !b.IsEntering && w > 1.2f) break; }
        if (b == null) { Check(false, "P: phoenix spawned"); yield break; }
        int s0 = BossFinish.Started, k0 = gm.BossKillCount;
        b.DebugKillWithAttack(BossFinalAttack.Forward);
        yield return null;
        bool rebirth = !b.IsDead;
        L($"[P] first lethal: dead {b.IsDead}, finish started +{BossFinish.Started - s0}, kills +{gm.BossKillCount - k0}");
        if (rebirth)
        {
            Check(BossFinish.Started == s0 && gm.BossKillCount == k0, "P: the first lethal hit goes to the phoenix rebirth (no BOSS FINISH, no reward)");
            w = 0f; while (w < 10f && (b.IsInvulnerableForQa || b.IsDead)) { yield return new WaitForSeconds(0.2f); w += 0.2f; }
            yield return new WaitForSeconds(1f);
            b.DebugKillWithAttack(BossFinalAttack.Up);
            yield return null;
            L($"[P] after rebirth: dead {b.IsDead}, finish started +{BossFinish.Started - s0}, kills +{gm.BossKillCount - k0}");
            Check(b.IsDead && BossFinish.Started == s0 + 1 && gm.BossKillCount == k0 + 1, "P: the final death after the rebirth enters BOSS FINISH");
        }
        else Warn("P: the phoenix did not rebirth on the first lethal hit (rebirth off in the tuning?) - checked the normal finish instead");
        yield return BfWaitEncounterEnd();
        BossFinish.DebugFirstKill = -1;
    }
}
#endif

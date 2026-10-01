#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 「新しいRunで雑魚が一切出ない」不具合(2026-10-01)の再現/回帰確認。
//  -qaEncRuns <dir> [-qaEncStages sky_corridor,wasteland_road,natural_cave] [-qaEncCycles 1]
// 各ステージで、ランを続けて行う(ラン間はGAME OVER→Retryでシーンを読み直す=実際のプレイと同じ):
//   1) 通常走行 → 雑魚が出るか → 通常走行中に死亡
//   2) 雑魚が出るか → ボスへワープ → ボス戦中に死亡
//   3) 雑魚が出るか → BONUS ZONE中に死亡
//   4) 雑魚が出るか → ボスを倒す → 通常Encounterが再開するか → BONUS ZONEを正常終了 → 再開するか → 死亡
//   5) 雑魚が出るか(前のランの終わり方に関係なく)
// 最後に別のステージへ移って、また同じ流れ。
public partial class QaSweep
{
    IEnumerator EncRunsMode()
    {
        Application.targetFrameRate = 60;
        string[] stages = Arg("-qaEncStages", "sky_corridor,wasteland_road,natural_cave").Split(',');
        int cycles = int.Parse(Arg("-qaEncCycles", "1"));
        string[] chars = { "swordsman", "gunslinger", "fighter", "mage", "dragon_lancer" };
        int run = 0;
        string prevEnd = "(app start)";
        for (int c = 0; c < cycles; c++)
            foreach (string st in stages)
                foreach (string scenario in new[] { "normal", "boss", "bonus", "bossKill+bonusEnd", "normal" })
                {
                    string ch = chars[run % chars.Length];
                    run++;
                    yield return BeginRun(ch, st);
                    stopKeepAlive = true;
                    var dir = EncounterDirector.Instance;
                    SetGod(true);
                    L($"\n[run {run}] {st} {ch} (previous run ended by: {prevEnd})");
                    L($"   start : {dir.StateLine()}");
                    int n = 0;
                    yield return CountSpawns(200f, 12f, x => n = x);
                    L($"   after 12s from 200m: new encounter enemies={n} | {dir.StateLine()}");
                    Check(n > 0, $"run {run} ({st}, previous: {prevEnd}): normal enemies appear in the new run");

                    if (scenario == "boss")
                    {
                        yield return GoToBoss();
                        prevEnd = BossManager.Instance.IsBossPhase ? "death during a BOSS fight" : "death (boss phase not reached!)";
                        L($"   boss  : {dir.StateLine()}");
                    }
                    else if (scenario == "bonus")
                    {
                        bool ok = BonusZone.Instance != null && BonusZone.Instance.Force();
                        yield return new WaitForSeconds(2.5f);
                        prevEnd = ok ? $"death during BONUS ZONE ({BonusZone.Instance.State})" : "death (bonus could not start!)";
                        L($"   bonus : {dir.StateLine()}");
                    }
                    else if (scenario == "bossKill+bonusEnd")
                    {
                        yield return GoToBoss();
                        bool bossOn = BossManager.Instance.IsBossPhase;
                        yield return KillBossesAndWait();
                        L($"   boss defeated (was in boss phase={bossOn}) : {dir.StateLine()}");
                        int after = 0;
                        yield return CountSpawns(-1f, 16f, x => after = x);
                        L($"   after boss: new encounter enemies={after} | {dir.StateLine()}");
                        Check(after > 0, $"run {run} ({st}): normal encounters resume after the boss is defeated");
                        bool ok = BonusZone.Instance != null && BonusZone.Instance.Force();
                        float w = 0f;
                        while (ok && BonusZone.Instance.State != BonusZone.Phase.Idle && w < 120f) { SetKmh(40f); yield return null; w += Time.deltaTime; }
                        L($"   bonus ended normally={ok && BonusZone.Instance.State == BonusZone.Phase.Idle} in {w:F0}s : {dir.StateLine()}");
                        int afterBonus = 0;
                        yield return CountSpawns(-1f, 16f, x => afterBonus = x);
                        L($"   after bonus: new encounter enemies={afterBonus} | {dir.StateLine()}");
                        Check(afterBonus > 0, $"run {run} ({st}): normal encounters resume after BONUS ZONE ends normally");
                        prevEnd = "death after boss defeat + bonus end";
                    }
                    else
                    {
                        // カード選択(レベルアップ)→復帰の後も通常Encounterが続くか
                        yield return ForceLevelUpChoice();
                        int afterChoice = 0, planned0 = dir.EncounterIndex;
                        yield return CountSpawns(-1f, 10f, x => afterChoice = x);
                        L($"   after a level-up card choice: new encounter enemies={afterChoice} encounters planned={dir.EncounterIndex - planned0} (rest included) | {dir.StateLine()}");
                        // 意図的な休憩区間(rest)に当たると敵が0でもよい。Encounterが決まり続けていることを確かめる
                        Check(dir.EncounterIndex > planned0, $"run {run} ({st}): normal encounters continue after a card choice");
                        prevEnd = "death while running normally";
                    }

                    yield return KillPlayer();
                    L($"   gameover: {dir.StateLine()}");
                    PlayerController.DebugSpeedScale = 1f;
                    yield return EndRun();
                }

        // 同じステージで「開始→死亡→次のラン」を続けて(ボス戦中/BONUS中/通常の死亡を交互に)
        int rapid = int.Parse(Arg("-qaEncRapid", "10"));
        string rs = Arg("-qaEncRapidStage", "wasteland_road");
        L($"\n===== {rapid} consecutive runs on {rs}");
        int okRuns = 0;
        for (int i = 0; i < rapid; i++)
        {
            yield return BeginRun(chars[i % chars.Length], rs);
            stopKeepAlive = true;
            SetGod(true);
            var dir = EncounterDirector.Instance;
            int n = 0;
            yield return CountSpawns(200f, 10f, x => n = x);
            string how = (i % 3) == 0 ? "boss" : (i % 3) == 1 ? "bonus" : "normal";
            if (how == "boss") yield return GoToBoss();
            else if (how == "bonus") { if (BonusZone.Instance != null) BonusZone.Instance.Force(); yield return new WaitForSeconds(2f); }
            L($"[rapid {i + 1}] enemies={n} then death during {how} (boss={BossManager.Instance.IsBossPhase} bonus={BonusZone.SuppressesNormalSpawns}) | {dir.StateLine()}");
            if (n > 0) okRuns++;
            Check(n > 0, $"rapid run {i + 1}: normal enemies appear");
            yield return KillPlayer();
            yield return EndRun();
        }
        L($"[rapid] runs with normal enemies: {okRuns}/{rapid}");
    }

    IEnumerator ForceLevelUpChoice()
    {
        var gain = typeof(GameManager).GetMethod("GainExp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        gain.Invoke(gm, new object[] { gm.ExpToNext + 1f });
        float w = 0f;
        bool opened = false;
        while (w < 8f) { if (gm.IsLocalChoiceOpen) opened = true; else if (opened) break; yield return null; w += Time.unscaledDeltaTime; }
        L($"   level-up choice opened={opened} closed={!gm.IsLocalChoiceOpen}");
        yield return new WaitForSeconds(0.5f);
    }

    void SetGod(bool on) => typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, on);

    // fromDistance>=0 ならそこへワープしてから、seconds秒走って新しく出た通常Encounterの敵の数を数える
    IEnumerator CountSpawns(float fromDistance, float seconds, System.Action<int> result)
    {
        if (fromDistance >= 0f && gm.MaxDistance < fromDistance) { gm.DebugWarpToDistance(fromDistance); yield return new WaitForSeconds(0.5f); }
        var seen = new HashSet<EnemyController>(FindObjectsByType<EnemyController>(FindObjectsSortMode.None));
        int count = 0;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetKmh(45f);
            if (Time.frameCount % 10 == 0)
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    if (seen.Add(e) && e.GetComponent<BonusEnemy>() == null) count++;
            yield return null;
        }
        PlayerController.DebugSpeedScale = 1f;
        result(count);
    }

    IEnumerator GoToBoss()
    {
        var bm = BossManager.Instance;
        float target = bm.NextBossDistance;
        if (target > 0f) gm.DebugWarpToDistance(Mathf.Max(gm.MaxDistance, target - 40f));
        float w = 0f;
        while (!bm.IsBossPhase && w < 25f) { SetKmh(30f); yield return null; w += Time.deltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        yield return new WaitForSeconds(2f);
    }

    IEnumerator KillBossesAndWait()
    {
        var bm = BossManager.Instance;
        float w = 0f;
        while (bm.IsBossPhase && w < 40f)
        {
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!b.IsDead) b.TakeDamage(99999, b.CenterWorld);
            foreach (var x in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
            foreach (var x in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!x.IsDead) x.TakeDamage(99999);
            yield return new WaitForSeconds(0.3f);
            w += 0.3f;
        }
        yield return new WaitForSeconds(1f);
    }

    IEnumerator KillPlayer()
    {
        SetGod(false);
        var lives = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        float w = 0f;
        while (!gm.IsGameOver && w < 10f)
        {
            lives.Invoke(gm, new object[] { 1 });
            var p = typeof(PlayerController).GetProperty("ShieldCharges"); if (p != null) p.SetValue(pc, 0);
            pc.TakeDamage(false, "QA:kill");
            yield return new WaitForSeconds(0.2f);
            w += 0.2f;
        }
        Check(gm.IsGameOver, "player died (GAME OVER)");
        yield return new WaitForSeconds(1f);
    }
}
#endif

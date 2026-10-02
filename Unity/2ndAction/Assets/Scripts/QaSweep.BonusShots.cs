#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// BONUS ZONE報酬強化(2026-10-01)の見た目の確認用撮影。 -qaBonusShots <dir>
public partial class QaSweep
{
    IEnumerator BonusShotsMode()
    {
        Application.targetFrameRate = 60;
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
        var zone = BonusZone.Instance;
        var dir = EncounterDirector.Instance;
        yield return new WaitForSeconds(2f);

        // JACKPOT: 開始の光 → 見出し → 区画中(進み具合/数字) → 結果
        zone.Force("jackpot");
        yield return new WaitForSeconds(0.12f); Shot("jackpot_intro_flash"); yield return null; yield return null;
        yield return new WaitForSeconds(0.6f); Shot("jackpot_intro"); yield return null; yield return null;
        float t = 0f;
        while (t < 7f) { KillSeenBonus(zone, 0.5f); yield return null; t += Time.deltaTime; }
        Shot("jackpot_active"); yield return null; yield return null;
        zone.End();
        yield return new WaitForSeconds(0.4f); Shot("jackpot_result_banner"); yield return null; yield return null;
        yield return new WaitForSeconds(1.3f); Shot("jackpot_result_panel"); yield return null; yield return null;
        L($"[shots] jackpot: MILE +{zone.BonusMile} EXP +{zone.BonusExp:F0} perfect={zone.PerfectAchieved} [{zone.PerfectDetail}]");
        yield return WaitBonusIdle(zone);

        // MIMIC: 連続Hitの数字がまとまる
        zone.Force("mimic_bash");
        yield return new WaitForSeconds(1.6f);
        var go = dir.DebugSpawnEnemy("mimic", EnemyAiTier.T0);
        yield return new WaitForSeconds(0.3f);
        var be = go != null ? go.GetComponent<BonusEnemy>() : null;
        if (be != null)
        {
            for (int i = 0; i < 9; i++) { be.OnLocalHit(i % 4 == 3 ? PlayerAttackKind.Up : PlayerAttackKind.Normal, false, false, go.transform.position); yield return new WaitForSeconds(0.07f); }
            Shot("mimic_combo_popup"); yield return null; yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        Shot("mimic_status"); yield return null; yield return null;
        zone.End();
        yield return WaitBonusIdle(zone);

        // MILE RUSH: 全撃破 → PERFECT BONUS!
        zone.Force("mile_rush");
        t = 0f;
        while (t < 8f) { KillSeenBonus(zone, 0f); yield return null; t += Time.deltaTime; }
        zone.End();
        yield return new WaitForSeconds(0.45f); Shot("perfect_banner"); yield return null; yield return null;
        yield return new WaitForSeconds(1.3f); Shot("perfect_panel"); yield return null; yield return null;
        L($"[shots] mile_rush: MILE +{zone.BonusMile} perfect={zone.PerfectAchieved} [{zone.PerfectDetail}]");
        yield return WaitBonusIdle(zone);
        yield return EndRun();
    }

    // 画面に現れた報酬Enemyを倒す(minAge秒たってから。Card Fairyは除く)
    void KillSeenBonus(BonusZone zone, float minAge)
    {
        var atk = pc.attackHitbox;
        foreach (var e in zone.Enemies)
        {
            if (e == null || !e.isActiveAndEnabled || !e.Seen || e.Killed || e.kind == BonusEnemyKind.CardFairy || e.kind == BonusEnemyKind.Mimic) continue;
            if (Time.time - e.FirstSeenTime < minAge) continue;
            var ec = e.GetComponent<EnemyController>();
            if (ec == null || ec.IsDying || atk == null) continue;
            SetPrivate(ec, "hp", 1);
            PlayerAttackInfo.RearmOf(atk);
            ec.ReceiveSweptAttack(atk);
        }
    }

    IEnumerator WaitBonusIdle(BonusZone zone)
    {
        float w = 0f;
        while ((zone.State != BonusZone.Phase.Idle || gm.LevelUpPending) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.5f);
    }
}
#endif

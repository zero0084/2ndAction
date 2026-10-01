#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 戦闘数値10倍化とカードLv9上限の確認(2026-10-02)。 -qaScale <dir>
//  1) 基礎値(攻撃力/HP/上限)と、何発で倒せるか(旧と同じ)
//  2) 被弾(通常10/強い一撃20/満タンからは倒れない)・Shield・回復(ドレイン/レベルアップ)
//  3) カードの数値表示(+10など)
//  4) Lv9上限: 同じカードを12回選んでも9回分だけ / Lv9のカードは候補に出ない / 表示 "Lv.8 -> Lv.9 MAX"
//  5) ボス試験: 20発のHPのテストボスを実際に殴って、何発で倒れるか・段階が変わるか
public partial class QaSweep
{
    IEnumerator ScaleMode()
    {
        Application.targetFrameRate = 60;
        var NPS = BindingFlags.NonPublic | BindingFlags.Instance;
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var def = CharacterDatabase.FindById("swordsman");
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);

        // ---- 1) 基礎値
        L($"[base] atk={pc.AttackPower} lives={gm.Lives}/{gm.maxLives} cap={gm.maxLivesCap} def atk={def.attackPower} hp={def.baseLives}/{def.baseMaxLives}");
        Check(pc.AttackPower == 20 && gm.maxLives == 50 && def.baseLives == 30 && gm.maxLivesCap == 100, "swordsman starts with attack 20, HP 30/50, cap 100 (x10)");
        int eHp = DistanceTierManager.Instance.EnemyHpFor(1f);
        int hitsNew = Mathf.CeilToInt(eHp / (float)pc.EffectiveAttackPower);
        int hitsOld = Mathf.CeilToInt((eHp / 10f) / (pc.EffectiveAttackPower / 10f) - 1e-4f);
        L($"[enemy] hp at start={eHp} (old {eHp / 10}) hits new={hitsNew} old={hitsOld}");
        Check(eHp == 50 && hitsNew == hitsOld, "grunt HP x10 and the same number of hits");
        // 実際に殴る
        var ed = FindFirstObjectByType<EncounterDirector>();
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;
        GameObject eg = ed != null ? ed.DebugSpawnEnemy("goblin", EnemyAiTier.T0) : null;
        var ec = eg != null ? eg.GetComponent<EnemyController>() : null;
        int realHits = 0, lastHp = ec != null ? ec.NetHp : 0; float w = 0f;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        while (ec != null && !ec.IsDying && w < 15f)
        {
            float dx = ec.transform.position.x - pc.transform.position.x;
            if (dx < 3.5f) { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); yield return new WaitForSeconds(0.3f); }
            else yield return null;
            w += Time.deltaTime;
            if (ec != null && ec.NetHp < lastHp) { realHits++; lastHp = ec.NetHp; }
        }
        if (ec != null && ec.IsDying) realHits++;
        L($"[enemy] real: hits to kill a goblin = {realHits} (expect {hitsNew}) dying={(ec != null && ec.IsDying) || ec == null}");
        Check(realHits == hitsNew, $"a real goblin dies in {hitsNew} hits ({realHits})");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);

        // ---- 2) 被弾/Shield/回復
        livesSet.Invoke(gm, new object[] { 50 });
        var r1 = gm.TryDamagePlayer(false, "qa-hit");
        Check(r1 == GameManager.DamageResult.Hit && gm.Lives == 40, $"a normal hit takes 10 HP (one heart) ({gm.Lives})");
        gm.TryDamagePlayer(false, "qa-heavy", CombatScale.PlayerHeavyHit);
        Check(gm.Lives == 20, $"a heavy hit takes 20 HP ({gm.Lives})");
        var apply = typeof(GameManager).GetMethod("ApplyCardEffects", NPS);
        apply.Invoke(gm, new object[] { CardDatabase.FindById("shield") });
        int sh = pc.ShieldCharges;
        var r2 = gm.TryDamagePlayer(false, "qa-shield");
        Check(sh >= 1 && r2 == GameManager.DamageResult.Ignored && pc.ShieldCharges == sh - 1, $"shield absorbs one hit (charges {sh} -> {pc.ShieldCharges}, lives {gm.Lives})");
        typeof(GameManager).GetMethod("AddLife", NPS).Invoke(gm, new object[] { Mathf.RoundToInt(CardDatabase.FindById("vampire").effects.First(e => e.type == EffectType.LifestealAmount).value) });
        Check(gm.Lives == 30, $"lifesteal heals one heart = 10 ({gm.Lives})");
        // HEART UP: 最大+10・全回復
        int maxBefore = gm.maxLives;
        apply.Invoke(gm, new object[] { CardDatabase.FindById("heart_up") });
        Check(gm.maxLives == maxBefore + 10 && gm.Lives == gm.maxLives, $"HEART UP adds one heart (+10) and heals ({gm.Lives}/{gm.maxLives})");
        // 3) 表示
        string atkText = CardEffectFormat.Format(CardDatabase.FindById("attack_up").effects[0]);
        string hpText = CardEffectFormat.Format(CardDatabase.FindById("heart_up").effects[0]);
        L($"[text] attack_up='{atkText}' heart_up='{hpText}'");
        Check(atkText.Contains("10") && hpText.Contains("10"), "card texts show the x10 values");

        // ---- 4) Lv9上限
        var deckField = typeof(GameManager).GetField("deckCards", NPS);
        var deck = deckField != null ? deckField.GetValue(gm) as List<string> : null;
        if (deck != null) { deck.Clear(); deck.Add("attack_up"); deck.Add("speed_up"); deck.Add("jump_power_up"); }
        int atk0 = pc.AttackPower;
        var pick = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NPS);
        string label8 = "";
        for (int i = 0; i < 12; i++)
        {
            if (gm.GetCurrentRunStack("attack_up") == 8) label8 = MakeLabel("attack_up");
            pick.Invoke(gm, new object[] { "attack_up" });
        }
        L($"[lv9] attack {atk0} -> {pc.AttackPower} stack={gm.GetCurrentRunStack("attack_up")} skips={gm.MaxedCardSkips} label@8='{label8}'");
        Check(pc.AttackPower == atk0 + 90 && gm.GetCurrentRunStack("attack_up") == 9 && gm.MaxedCardSkips == 3, "picking ATTACK UP 12 times stacks only 9 times (+90)");
        Check(label8.Contains("Lv.8") && label8.Contains("Lv.9") && label8.Contains("MAX"), $"choice label shows 'Lv.8 -> Lv.9 MAX' ({label8})");
        bool offered = false;
        for (int t = 0; t < 8; t++)
        {
            gm.GrantBonusCardChoice();
            w = 0f; while (!gm.IsRewardSequenceWaitingForSelection && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            var pend = GetPrivate(gm, "pendingChoices") as CardDefinition[];
            if (pend != null && pend.Any(c => c.cardId == "attack_up")) offered = true;
            w = 0f; while ((gm.LevelUpPending || gm.IsRewardSequenceRunning) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        Check(!offered, "a Lv9 card is never offered again (8 choices)");

        // ---- 5) ボス試験
        if (BossManager.Instance != null) BossManager.Instance.enabled = true;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var ct = FindFirstObjectByType<CardBalanceTest>();
        bool spawned = ct != null && ct.SpawnTestBoss(20);
        int est = pc.BossHitEstimate;
        var tb = ct != null ? ct.TestBoss : null;
        L($"[boss] spawned={spawned} est={est} hp={(tb != null ? tb.maxHp : 0)} (expect {est * 20})");
        Check(spawned && tb != null && tb.maxHp == est * 20, "test boss HP = estimate x 20");
        // 画面: ボス試験タブ(DEBUGモードで開く)
        SetDebugMode(true);
        ct.Open(true); SetPrivate(ct, "tab", CardBalanceTest.Tab.Boss);
        yield return new WaitForSecondsRealtime(0.6f);
        Shot("scale_boss_tab");
        yield return new WaitForSecondsRealtime(0.3f);
        ct.Open(false);
        float t0 = Time.time; int maxPhase = 1; float lastFlick = 0f;
        while (tb != null && !tb.IsDead && Time.time - t0 < 90f)
        {
            yield return null;
            maxPhase = Mathf.Max(maxPhase, tb.Phase);
            if (Time.time - lastFlick > 0.2f)
            {
                lastFlick = Time.time;
                float dx = tb.CenterWorld.x - pc.transform.position.x, dy = tb.CenterWorld.y - pc.transform.position.y;
                StartCoroutine(Flick(dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward));
            }
        }
        L($"[boss] killed={(tb != null && tb.IsDead)} hits={ct.TestBossHits} phases seen={maxPhase}/{(tb != null ? tb.PhaseCount : 0)} time={Time.time - t0:F1}s breaks={(tb != null ? tb.BreakCount : 0)}");
        Check(tb != null && tb.IsDead && ct.TestBossHits >= 12 && ct.TestBossHits <= 22, $"the 20-hit test boss dies in about 20 hits (BREAK/dash lunge can shorten) ({ct.TestBossHits})");
        Check(maxPhase >= 2, $"the test boss goes through its HP phases ({maxPhase})");
        ct.Open(true); SetPrivate(ct, "tab", CardBalanceTest.Tab.Boss);
        yield return new WaitForSecondsRealtime(0.6f);
        Shot("scale_boss_done");
        ct.Open(false); SetDebugMode(false);
        yield return EndRun();
    }

    string MakeLabel(string cardId)
    {
        var m = typeof(GameManager).GetMethod("MakeChoiceCardData", BindingFlags.NonPublic | BindingFlags.Instance);
        var data = m.Invoke(gm, new object[] { CardDatabase.FindById(cardId) });
        foreach (var f in data.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var v = f.GetValue(data) as string;
            if (v != null && v.Contains("Lv.")) return v;
        }
        foreach (var p in data.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var v = p.GetValue(data) as string;
            if (v != null && v.Contains("Lv.")) return v;
        }
        return "";
    }
}
#endif

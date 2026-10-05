#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// FINAL EVOLUTION(2026-10-04 第1段階)の確認。 -qaFinalEvo <dir>
// 2026-10-05 第2段階: 終われば READY へ戻る(USED なし)。再使用/全カードは QaSweep.FinalEvo2.cs(-qaFe2)
public partial class QaSweep
{
    void FePicks(string id, int n) { for (int i = 0; i < n; i++) V3Pick.Invoke(gm, new object[] { id }); }

    IEnumerator WaitChoiceOpen(float timeout = 5f)
    {
        float w = 0f;
        while (!(gm.IsRewardSequenceWaitingForSelection && gm.DebugChoiceOpen) && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
    }

    // 3択で cardId を選ぶ(画面のカードを押すのと同じ経路)
    IEnumerator PickInChoice(string cardId)
    {
        var ids = gm.DebugPendingChoiceIds;
        int idx = System.Array.IndexOf(ids, cardId);
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (idx < 0 || seq == null) { L($"[pick] {cardId} not in [{string.Join(",", ids)}]"); yield break; }
        seq.OnCardClicked(idx);
        yield return new WaitForSecondsRealtime(0.25f);
        seq.OnCardClicked(idx);
        float w = 0f;
        while ((gm.IsRewardSequenceWaitingForSelection || gm.DebugChoiceOpen) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; } // 閉じる演出の後で選択が確定する
        yield return new WaitForSecondsRealtime(0.3f);
        L($"[pick] {cardId} resolved after {w:F1}s");
    }

    IEnumerator FinalEvoMode()
    {
        Application.targetFrameRate = 60;
        autoPickHold = true;
        var T = FinalEvolutionTuning.I;
        L($"[tuning] readyMeters={T.readyMeters} usesPerRun={T.usesPerRun} entries={T.entries.Count} awakenedBonus={T.awakenedDurationBonus}");

        // ---- B: キャラカードで開始時 Lv9 → 資格あり(0m で即 READY ではない)
        {
            string id = "speed_up", k9 = K(id, 9);
            CardInventory.AddCard(k9, 9, 1);
            float w0 = 0f; while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
            gm = GameManager.Instance;
            gm.SetSelectedCharacter("swordsman");
            bool eq = gm.EquipCharacterCard(0, k9, 9);
            yield return BeginRun("swordsman", "wasteland_road");
            yield return null; yield return null;
            var st = FinalEvolution.StageOf(id);
            float ea = FinalEvolution.EligibleAt(id);
            L($"[B] equip={eq} Lv{gm.GetAbilityRunStack(id)} stage={st} eligibleAt={ea:F0} d={gm.MaxDistance:F0}");
            Check(eq && gm.GetAbilityRunStack(id) == 9 && st == FinalEvolution.Stage.Eligible && ea <= 50f, "B: a Lv9 character card is eligible from the start of the run (not READY yet)");
            Check(CardProgression.FinalEvolutionEligible(id), "B: CardProgression.FinalEvolutionEligible counts a Lv9 start");
            yield return EndRun();
            gm = GameManager.Instance;
            gm.EquipCharacterCard(0, "", 0);
        }

        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(1200f);
        yield return new WaitForSeconds(0.5f);

        // ---- A: Lv8 → Lv9 で資格
        string atk = "attack_up";
        FePicks(atk, 8);
        yield return null;
        Check(gm.GetAbilityRunStack(atk) == 8 && FinalEvolution.StageOf(atk) == FinalEvolution.Stage.None, $"A: Lv8 is not eligible ({FinalEvolution.StageOf(atk)})");
        FePicks(atk, 1);
        yield return null; yield return null;
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Eligible && Mathf.Abs(FinalEvolution.EligibleAt(atk) - gm.MaxDistance) < 20f, $"A: Lv9 during the run -> eligible at {FinalEvolution.EligibleAt(atk):F0}m (d={gm.MaxDistance:F0})");

        // ---- C: 4,999m では READY にならない / 5,000m で READY
        float d = gm.MaxDistance;
        FinalEvolution.DebugSetEligibleAt(atk, d - (T.readyMeters - 1f) + 0.8f); // 次のフレームの走った分(1フレーム約0.5m)を見込む
        yield return null;
        float run1 = gm.MaxDistance - FinalEvolution.EligibleAt(atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Eligible, $"C: {run1:F1}m after eligibility -> not READY");
        FinalEvolution.DebugSetEligibleAt(atk, gm.MaxDistance - T.readyMeters - 0.01f);
        yield return null;
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready, $"C: {T.readyMeters:F0}m after eligibility -> READY ({FinalEvolution.StageOf(atk)})");
        Shot("fe_C_hud_ready");

        // ---- D: READY → LEVEL UP に候補(最大1枠)
        gm.DebugTriggerLevelUp();
        yield return WaitChoiceOpen();
        var ids = gm.DebugPendingChoiceIds;
        int feCount = ids.Count(x => FinalEvolution.IsChoiceId(x));
        L($"[D] choices: [{string.Join(", ", ids)}]");
        Check(ids.Contains(FinalEvolution.ChoicePrefix + atk) && feCount == 1 && ids.Length <= 3, $"D: LEVEL UP offers FINAL EVOLUTION in at most one slot ({feCount} of {ids.Length})");
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("fe_D_levelup_choice");
        // ---- E: 断る → READY のまま
        string other = ids.FirstOrDefault(x => !FinalEvolution.IsChoiceId(x));
        if (other != null) yield return PickInChoice(other);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready, $"E: declining keeps READY ({FinalEvolution.StageOf(atk)})");

        // ---- F: 選ぶ → ACTIVE(最終ダメージ×1.5、攻撃の枠は変えない)
        int powBefore = pc.EffectiveAttackPower; float atkFactor = pc.CardAttackFactor;
        float gauge0 = UltimateArt.Instance != null ? UltimateArt.Instance.Gauge : 0f; int ultLv = UltimateArt.Level;
        gm.DebugTriggerLevelUp();
        yield return WaitChoiceOpen();
        yield return PickInChoice(FinalEvolution.ChoicePrefix + atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Active, $"F: picking it -> ACTIVE ({FinalEvolution.StageOf(atk)})");
        int powDuring = pc.EffectiveAttackPower;
        Check(Mathf.Abs(powDuring / (float)Mathf.Max(1, powBefore) - 1.5f) < 0.1f && Mathf.Approximately(pc.CardAttackFactor, atkFactor), $"F: ATTACK UP final damage x1.5 ({powBefore} -> {powDuring}), attack bucket unchanged (x{pc.CardAttackFactor:F3})");
        Check(gm.GetAbilityRunStack(atk) == 9, "F: card Lv stays 9 (no Lv10)");
        // ---- M: ULTIMATE と混線しない
        float gauge1 = UltimateArt.Instance != null ? UltimateArt.Instance.Gauge : 0f;
        Check(Mathf.Abs(gauge1 - gauge0) < 5f && UltimateArt.Level == ultLv && Mathf.Approximately(UltimateArt.BuffAttackMul, 1f), $"M: ULTIMATE gauge/level/BUFF untouched (gauge {gauge0:F0}->{gauge1:F0}, buff x{UltimateArt.BuffAttackMul:F2})");
        yield return new WaitForSeconds(0.6f);
        Shot("fe_F_active_attack");
        // ---- G: 時間型の終了 → 通常の Lv9 へ
        FinalEvolution.DebugSetRemaining(atk, 0.2f);
        yield return new WaitForSeconds(0.6f);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready && Mathf.Approximately(FinalEvolution.AttackMul, 1f) && gm.GetAbilityRunStack(atk) == 9, $"G: time-type ends -> READY again, back to Lv9 MAX (mul x{FinalEvolution.AttackMul:F2})");
        Check(Mathf.Abs(pc.EffectiveAttackPower - powBefore) <= Mathf.Max(2, powBefore / 20), $"G: attack back to normal ({pc.EffectiveAttackPower} vs {powBefore})");
        Shot("fe_G_hud_ready_again");
        // ---- I(第2段階): 同じ能力はもう一度選べる(再チャージなし)
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + atk });
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Active && FinalEvolution.Uses(atk) == 2, $"I: the same ability can evolve again this run ({FinalEvolution.StageOf(atk)}, activations {FinalEvolution.Uses(atk)})");
        FinalEvolution.DebugEnd(atk);

        // ---- J / H / P: 別の能力(EXP UP、距離型)は同じランで進化できる。EXP は曲線の前の枠に足すだけ
        string exp = "exp_up";
        FePicks(exp, 9);
        yield return null;
        float bucket0 = gm.ExpBucketDistance, mult0 = gm.ExpMultDistance;
        FinalEvolution.DebugMakeReady(exp);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + exp });
        Check(FinalEvolution.StageOf(exp) == FinalEvolution.Stage.Active, "J: a different ability can evolve in the same run");
        float bucket1 = gm.ExpBucketDistance, mult1 = gm.ExpMultDistance;
        float rem0 = FinalEvolution.Remaining(exp);
        L($"[P] EXP bucket {bucket0:F2} -> {bucket1:F2}, multiplier x{mult0:F2} -> x{mult1:F2} (curve), remaining {rem0:F0}m");
        Check(Mathf.Abs(bucket1 - bucket0 - 0.6f) < 0.01f && Mathf.Approximately(mult1, CardRules.ExpMultiplier(bucket1)) && mult1 - mult0 < 0.6f, "P: EXP bonus goes into the same bucket before the diminishing curve (no old runaway)");
        float dE = gm.MaxDistance;
        yield return new WaitForSeconds(1.0f);
        float rem1 = FinalEvolution.Remaining(exp);
        Check(rem1 < rem0 && Mathf.Abs((rem0 - rem1) - (gm.MaxDistance - dE)) < 5f, $"H: distance-type counts down by distance ({rem0:F0} -> {rem1:F0}m while running {gm.MaxDistance - dE:F0}m)");
        FinalEvolution.DebugSetRemaining(exp, 3f);
        yield return new WaitForSeconds(1.0f);
        Check(FinalEvolution.StageOf(exp) == FinalEvolution.Stage.Ready && Mathf.Abs(gm.ExpBucketDistance - bucket0) < 0.001f, $"H: distance-type ends -> back to Lv9, READY again (bucket {gm.ExpBucketDistance:F2})");

        // ---- L: AWAKENED は通常の性能を変えない / 持続が少し伸びる
        string spd = "speed_up";
        FePicks(spd, 9);
        yield return null;
        float atkF = pc.CardAttackFactor, runV = pc.CurrentAutoRunSpeed;
        FinalEvolution.DebugForceAwakened = true;
        yield return null;
        Check(Mathf.Approximately(pc.CardAttackFactor, atkF) && Mathf.Abs(pc.CurrentAutoRunSpeed - runV) < 0.5f, "L: AWAKENED does not change normal Lv9 stats");
        FinalEvolution.DebugMakeReady(spd);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + spd });
        float remS = FinalEvolution.Remaining(spd);
        var eS = T.For(spd);
        Check(Mathf.Abs(remS - eS.durationSeconds * (1f + T.awakenedDurationBonus)) < 0.2f, $"L: AWAKENED adds a small duration bonus ({remS:F1}s vs {eS.durationSeconds}s)");
        // SPEED: 実速度は上限まで / 接触と障害物から守る
        float vNow = pc.CurrentAutoRunSpeed;
        Check(GameManager.SpeedKmh(vNow) <= T.speedCapKmh + 1f || GameManager.SpeedKmh(runV) > T.speedCapKmh, $"SPEED: real speed stays within {T.speedCapKmh}km/h ({GameManager.SpeedKmh(runV):F0} -> {GameManager.SpeedKmh(vNow):F0}km/h)");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true; yield return null;
        int amt = 10;
        bool gObs = FinalEvolution.InterceptDamage("Obstacle:test", ref amt), gEn = FinalEvolution.InterceptDamage("Enemy:test", ref amt), gBoss = FinalEvolution.InterceptDamage("BossProjectile:test", ref amt);
        Check(gObs && gEn && !gBoss, $"SPEED: obstacles/contact are guarded during SPEED FINAL EVOLUTION, boss attacks are not (obs={gObs} enemy={gEn} boss={gBoss})");
        yield return new WaitForSeconds(0.4f);
        Shot("fe_speed_active");
        FinalEvolution.DebugEnd(spd);
        FinalEvolution.DebugForceAwakened = false;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        stopKeepAlive = false; StartCoroutine(KeepAlive());

        // RANGE: 射程が伸び、終われば戻る
        string rng = "attack_range_up";
        FePicks(rng, 9);
        yield return null;
        float r0 = pc.AttackRangeMultiplier;
        FinalEvolution.DebugMakeReady(rng);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + rng });
        float r1 = pc.AttackRangeMultiplier;
        FinalEvolution.DebugEnd(rng);
        float r2 = pc.AttackRangeMultiplier;
        Check(Mathf.Abs(r1 / r0 - T.For(rng).power) < 0.02f && Mathf.Abs(r2 - r0) < 0.001f, $"RANGE: reach x{r1 / r0:F2} during, back after ({r0:F3} -> {r1:F3} -> {r2:F3})");

        // VAMPIRE: Blood Shield は上限まで、終われば消える
        string vam = "vampire";
        FePicks(vam, 9);
        yield return null;
        FinalEvolution.DebugMakeReady(vam);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + vam });
        for (int i = 0; i < 5; i++) FinalEvolution.OnOverheal();
        int bs = FinalEvolution.BloodShield;
        FinalEvolution.DebugEnd(vam);
        Check(bs == Mathf.RoundToInt(T.For(vam).power2) && FinalEvolution.BloodShield == 0, $"VAMPIRE: Blood Shield capped at {bs} and removed at the end ({FinalEvolution.BloodShield})");

        // ---- Q: GREED の倍率は終われば戻る
        string grd = "greed";
        FePicks(grd, 9);
        yield return null;
        FinalEvolution.DebugMakeReady(grd);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + grd });
        float mm = FinalEvolution.MileMul, dm = FinalEvolution.DamageTakenMul;
        FinalEvolution.DebugEnd(grd);
        Check(mm > 1.01f && dm > 1.01f && Mathf.Approximately(FinalEvolution.MileMul, 1f) && Mathf.Approximately(FinalEvolution.DamageTakenMul, 1f), $"Q: GREED MILE x{mm:F2} / damage taken x{dm:F2} during, both x1 after");

        // ---- N: PHOENIX の専用復活は通常の Charge を増やさない
        string phx = "phoenix";
        FePicks(phx, 9);
        yield return null;
        int charges0 = gm.PhoenixCharges, rev0 = GameManager.PhoenixRevives, emr0 = FinalEvolution.EmergencyRevives;
        FinalEvolution.DebugMakeReady(phx);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + phx });
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        stopKeepAlive = true; yield return null;
        var livesProp = typeof(GameManager).GetProperty("Lives");
        livesProp.GetSetMethod(true).Invoke(gm, new object[] { 10 });
        gm.TryDamagePlayer(false, "qa-fe-phoenix", 30);
        bool alive1 = gm.Lives > 0 && !gm.IsGameOver;
        int charges1 = gm.PhoenixCharges;
        Check(alive1 && FinalEvolution.EmergencyRevives == emr0 + 1 && GameManager.PhoenixRevives == rev0 && charges1 == charges0 && charges1 <= 1, $"N: FINAL EVOLUTION rebirth used first; normal PHOENIX charge untouched ({charges0} -> {charges1}, lives {gm.Lives})");
        FinalEvolution.DebugEnd(phx);
        Check(!FinalEvolution.PhoenixTokenReady, "N: unused rebirth vanishes at the end");
        livesProp.GetSetMethod(true).Invoke(gm, new object[] { 990 });
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        stopKeepAlive = false; StartCoroutine(KeepAlive());

        // ---- O: FLAME / THUNDER で無限の連鎖が起きない
        string fl = "flame_blade", th = "thunder_strike";
        FePicks(fl, 9); FePicks(th, 9);
        yield return null;
        FinalEvolution.DebugMakeReady(fl); FinalEvolution.DebugMakeReady(th);
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + fl });
        V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + th });
        Check(FinalEvolution.IsActive(fl) && FinalEvolution.IsActive(th), "O: two different FINAL EVOLUTIONs can be active together");
        var def = EnemyDatabase.FindById("goblin");
        var enemies = new System.Collections.Generic.List<EnemyController>();
        for (int i = 0; i < 8; i++)
        {
            float x = pc.transform.position.x + 6f + i * 1.2f;
            float? gy = TerrainManager.Instance.GetHeightAt(x);
            var go = TerrainManager.Instance.SpawnEncounterEnemy(def, new Vector2(x, gy ?? pc.transform.position.y), EnemyAiTier.T0, def.behaviorKind);
            var en = go != null ? go.GetComponent<EnemyController>() : null;
            if (en != null) { en.ArenaDummy = true; enemies.Add(en); }
        }
        int lp0 = ElementSystem.LightningProcs, lh0 = ElementSystem.LightningHits, sp0 = FinalEvolution.SpreadBurns, bp0 = ElementSystem.BurnProcs;
        float tO = 0f; int frames = 0; float worstMs = 0f;
        while (tO < 3f)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var en in enemies) if (en != null && ElementSystem.IsAlive(en)) ElementSystem.OnPlayerHit(en, null, 20);
            worstMs = Mathf.Max(worstMs, (float)sw.Elapsed.TotalMilliseconds);
            frames++; tO += Time.deltaTime;
            yield return null;
        }
        int lp = ElementSystem.LightningProcs - lp0, lh = ElementSystem.LightningHits - lh0, spr = FinalEvolution.SpreadBurns - sp0, bp = ElementSystem.BurnProcs - bp0;
        L($"[O] 3s x {enemies.Count} enemies hit every frame ({frames} frames): lightning procs {lp}, lightning hits {lh}, burn procs {bp}, spread burns {spr}, worst frame {worstMs:F2}ms, proc budget drops {CardProcs.ProcBudgetDrops}");
        Check(lp <= enemies.Count * (3f / 0.4f) + enemies.Count && spr <= enemies.Count * 3 * 2 + 4 && lh < 400, "O: lightning stays under the per-target cooldown and flame spread is bounded (no infinite procs)");
        foreach (var en in enemies) if (en != null) Destroy(en.gameObject);
        FinalEvolution.DebugEnd(fl); FinalEvolution.DebugEnd(th);

        // ---- R: CONTINUE(RunCheckpoint の保存/読み込みで同じ状態)
        FinalEvolution.DebugMakeReady(atk);
        var data = new RunCheckpoint.Data();
        data.finalEvolution = FinalEvolution.Export();
        string json = JsonUtility.ToJson(data);
        var back = JsonUtility.FromJson<RunCheckpoint.Data>(json);
        var before = FinalEvolution.Export().OrderBy(s0 => s0.id).Select(s0 => $"{s0.id}:{s0.eligibleAt:F0}:{s0.ready}:{s0.active}:{s0.uses}").ToArray();
        FinalEvolution.Import(back.finalEvolution);
        var after = FinalEvolution.Export().OrderBy(s0 => s0.id).Select(s0 => $"{s0.id}:{s0.eligibleAt:F0}:{s0.ready}:{s0.active}:{s0.uses}").ToArray();
        Check(before.SequenceEqual(after) && before.Length >= 5, $"R: FINAL EVOLUTION state survives the CONTINUE data round trip ({before.Length} states)");
        // ---- T: 古いデータ(項目なし)は状態なし
        var old = JsonUtility.FromJson<RunCheckpoint.Data>("{\"active\":true,\"maxDistance\":1234}");
        Check(old.finalEvolution != null && old.finalEvolution.Count == 0, "T: old CONTINUE data has no FINAL EVOLUTION state (no error)");

        // ---- S: ランを終える → 状態は消える
        yield return EndRun();
        yield return BeginRun("swordsman", "wasteland_road");
        yield return null;
        Check(FinalEvolution.Export().Count == 0 && !FinalEvolution.AnyActive, $"S: a new run starts with no FINAL EVOLUTION state ({FinalEvolution.Export().Count})");
        L($"[summary] activations={FinalEvolution.Activations} ends={FinalEvolution.Ends} rebirths={FinalEvolution.EmergencyRevives} bloodBlocks={FinalEvolution.BloodShieldBlocks} autoHits={FinalEvolution.AutoHits} slashWaves={FinalEvolution.SlashWaves} spreads={FinalEvolution.SpreadBurns}");
        autoPickHold = false;
        yield return EndRun();
    }
}
#endif

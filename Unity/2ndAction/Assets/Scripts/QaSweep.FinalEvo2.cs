#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// FINAL EVOLUTION 第2段階(2026-10-05: 再使用 + 全カード)の確認。 -qaFe2 <dir> [-qaFe2Only RAFSP]
//  R: 再使用の必須テスト A〜O(本物の LEVEL UP の3択を通す)+ 複数 READY の公平さ
//  A: 全対象カード(#100 ULTIMATE を除く)をデータから走査: 定義/Lv9の資格/初回+5,000m/READY/候補/発動/効果/終了/READY へ戻る/
//     2回目/AWAKENED/保存と読み込み/10回の反復で何も残らない/例外 → fe2_cards.tsv
//  S: 組み合わせの負荷(攻撃/炎/雷/速度/吸血/EXP 複数/MILE 複数/HELL MODE 系/PHOENIX 系/複数 FE + ULTIMATE)→ fe2_stress.tsv
//  P: 実時間の10回反復(敵を殴りながら 発動→終了→再発動。物/リスナー/追跡/フレーム時間が増えない)→ fe2_repeat.tsv
public partial class QaSweep
{
    static readonly EffectType[] FeTypes = (EffectType[])System.Enum.GetValues(typeof(EffectType));
    static string[] feSnapNames;

    // 能力値の写し(カードの合計 + 派生した倍率 + FINAL EVOLUTION の専用の値)。時間で変わる値(距離依存の攻撃力など)は入れない
    float[] FeSnap()
    {
        var l = new List<float>();
        var names = feSnapNames == null ? new List<string>() : null;
        void A(string n, float v) { l.Add(v); if (names != null) names.Add(n); }
        foreach (var t in FeTypes) A(t.ToString(), gm.Card.Get(t));
        A("maxLives", gm.maxLives); A("sealed", gm.SealedHearts);
        A("MileMult", gm.MileGainMultiplier); A("BossMileMult", gm.BossMileGainMultiplier);
        A("ExpMultDist", gm.ExpMultDistance); A("ExpMultKill", gm.ExpMultKill); A("ExpBucketDist", gm.ExpBucketDistance);
        A("EnemyHpMult", gm.EnemyHpMultiplier); A("EnemySpawnMult", gm.EnemySpawnRateMultiplier); A("BossHpMult", gm.BossHpMultiplier);
        A("PhoenixCharges", gm.PhoenixCharges);
        A("CardAttackFactor", pc.CardAttackFactor); A("CardSpeedFactor", pc.CardSpeedFactor); A("CardJumpFactor", pc.CardJumpFactor);
        A("RangeMult", pc.AttackRangeMultiplier); A("ShieldCap", pc.ShieldCapacity);
        A("FE.AttackMul", FinalEvolution.AttackMul); A("FE.Speed(10)", FinalEvolution.SpeedFactor(10f)); A("FE.ExpBucket", FinalEvolution.ExpBucketBonus);
        A("FE.MileMul", FinalEvolution.MileMul); A("FE.DamageTakenMul", FinalEvolution.DamageTakenMul); A("FE.Lifesteal", FinalEvolution.LifestealChanceAdd);
        A("FE.Burn", FinalEvolution.BurnChanceAdd); A("FE.BurnDps", FinalEvolution.BurnDpsMul); A("FE.Lightning", FinalEvolution.LightningChanceAdd);
        A("FE.Chains", FinalEvolution.LightningChainsAdd); A("FE.Phoenix", FinalEvolution.PhoenixTokenReady ? 1 : 0); A("FE.BloodShield", FinalEvolution.BloodShield);
        A("FE.Zoom", CameraFollow.FinalEvolutionZoom); A("FE.Sonic", SonicMoveFx.ForcedIntensity);
        if (names != null) feSnapNames = names.ToArray();
        return l.ToArray();
    }
    static bool FeSame(float[] a, float[] b) => FeDiff(a, b).Count == 0;
    static List<string> FeDiff(float[] a, float[] b)
    {
        var d = new List<string>();
        for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
            if (Mathf.Abs(a[i] - b[i]) > 1e-3f * Mathf.Max(1f, Mathf.Abs(a[i]))) d.Add($"{feSnapNames[i]} {a[i]:0.###}->{b[i]:0.###}");
        return d;
    }
    string FeSig() => string.Join(";", FinalEvolution.Export().OrderBy(s => s.id).Select(s => $"{s.id}:{s.eligible}:{s.eligibleAt:F0}:{s.ready}:{s.active}:{s.uses}:{s.remaining:F1}:{s.spent}"));
    bool FeActivate(string id) { V3Pick.Invoke(gm, new object[] { FinalEvolution.ChoicePrefix + id }); return FinalEvolution.IsActive(id); }
    // 時間/距離の残りを0にして、FinalEvolution.Update が普通に終わらせる
    IEnumerator FeEndNatural(string id) { FinalEvolution.DebugSetRemaining(id, 0f); yield return null; yield return null; }
    void FeReadyNow(string id) { FinalEvolution.DebugSetEligibleAt(id, gm.MaxDistance - FinalEvolutionTuning.I.readyMeters - 1f); }
    IEnumerator FeLv9(string id) { FePicks(id, 9 - gm.GetAbilityRunStack(id)); yield return null; yield return null; }
    float FeDuration(string id) { var e = FinalEvolutionTuning.I.For(id); return e.kind == FinalEvolutionTuning.Kind.Time ? e.durationSeconds : e.durationMeters; }
    void FeLives(int v) => V3LivesSet.Invoke(gm, new object[] { v });
    void FeInvincible(bool on) { typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, on); }
    int FeObjects() => FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
    // 演出/弾の数(走ると増減する地形/敵は含めない): 一回きりの演出 + 弾 + 粒子 + FINAL EVOLUTION の物
    int FeFx()
    {
        int n = FindObjectsByType<OneShotSpriteEffect>(FindObjectsSortMode.None).Length + FindObjectsByType<KitProjectile>(FindObjectsSortMode.None).Length;
        foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (p.IsAlive() && p.GetComponentInParent<EnemyController>() == null) n++;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name.StartsWith("FinalEvo") && t.name != "FinalEvoAura") n++;
        return n;
    }
    int FeEnemies() => FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length;
    Dictionary<string, int> feCensus;
    // 物の名前ごとの数(前回からの増加の上位)
    string FeCensusDiff()
    {
        var now = new Dictionary<string, int>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) { string n = t.name.Replace("(Clone)", ""); now[n] = now.TryGetValue(n, out int k) ? k + 1 : 1; }
        string r = feCensus == null ? "(first)" : string.Join(", ", now.Select(kv => (kv.Key, kv.Value - (feCensus.TryGetValue(kv.Key, out int b) ? b : 0))).Where(x => x.Item2 != 0).OrderByDescending(x => System.Math.Abs(x.Item2)).Take(10).Select(x => $"{x.Key}{x.Item2:+0;-0}"));
        feCensus = now;
        return r;
    }

    IEnumerator Fe2Mode()
    {
        Application.targetFrameRate = 60;
        autoPickHold = true;
        var T = FinalEvolutionTuning.I;
        L($"[tuning] readyMeters={T.readyMeters} usesPerRun={T.usesPerRun} entries={T.entries.Count} awakenedBonus={T.awakenedDurationBonus}");
        string only = Arg("-qaFe2Only", "RAFSP");
        if (only.Contains('R')) yield return Fe2Reuse();
        if (only.Contains('A')) yield return Fe2AllCards();
        if (only.Contains('S')) yield return Fe2Stress();
        if (only.Contains('P')) yield return Fe2Repeat();
        autoPickHold = false;
    }

    // ===================================================================== R
    IEnumerator Fe2Reuse()
    {
        L("== R: 再使用の必須テスト A〜O ==");
        var T = FinalEvolutionTuning.I;
        yield return V3Begin("swordsman");
        FeInvincible(true);
        BossManager.SuppressGates = true;
        WarpTo(1200f);
        yield return new WaitForSeconds(0.5f);
        V3Reset();
        string atk = "attack_up";
        var s0 = FeSnap();

        // A: Lv9 → 初回 +5,000m → READY
        yield return FeLv9(atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Eligible, $"A: Lv9 -> eligible ({FinalEvolution.StageOf(atk)})");
        FinalEvolution.DebugSetEligibleAt(atk, gm.MaxDistance - T.readyMeters + 2f); // 次のフレームの分(約0.5m)を見込む
        yield return null;
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Eligible, $"A: just under {T.readyMeters:F0}m after eligibility -> not READY ({gm.MaxDistance - FinalEvolution.EligibleAt(atk):F1}m)");
        FeReadyNow(atk);
        yield return null;
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready, $"A: {T.readyMeters:F0}m after eligibility -> READY ({FinalEvolution.StageOf(atk)})");
        var lv9 = FeSnap();

        // B: LEVEL UP の3択で選ぶ → ACTIVE
        gm.DebugTriggerLevelUp();
        yield return WaitChoiceOpen();
        Check(gm.DebugPendingChoiceIds.Count(FinalEvolution.IsChoiceId) == 1, $"B: one FINAL EVOLUTION slot [{string.Join(",", gm.DebugPendingChoiceIds)}]");
        yield return PickInChoice(FinalEvolution.ChoicePrefix + atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Active && FinalEvolution.Uses(atk) == 1, $"B: picking it -> ACTIVE ({FinalEvolution.StageOf(atk)}, activations {FinalEvolution.Uses(atk)})");
        var act1 = FeSnap();
        Check(!FeSame(lv9, act1), $"B: the FINAL EVOLUTION changes the stats ({string.Join(", ", FeDiff(lv9, act1).Take(4))})");

        // C: ACTIVE 中の LEVEL UP に同じ FE は出ない
        gm.DebugTriggerLevelUp();
        yield return WaitChoiceOpen();
        var idsC = gm.DebugPendingChoiceIds;
        Check(!idsC.Contains(FinalEvolution.ChoicePrefix + atk), $"C: while ACTIVE the same FE is not offered [{string.Join(",", idsC)}]");
        int stackBefore = FinalEvolution.Uses(atk);
        Check(!FinalEvolution.Activate(atk) && FinalEvolution.Uses(atk) == stackBefore, "C: the same FE cannot stack (Activate refused while ACTIVE)");
        string otherC = idsC.FirstOrDefault(x => !FinalEvolution.IsChoiceId(x));
        if (otherC != null) yield return PickInChoice(otherC);
        lv9 = null; // 通常カードを1枚取ったので基準を取り直す(終わった時に比べる)

        // D: 終わる → READY へ戻る(待ちなし)
        yield return FeEndNatural(atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready, $"D: ACTIVE ends -> READY again immediately ({FinalEvolution.StageOf(atk)})");
        lv9 = FeSnap();
        Check(Mathf.Approximately(FinalEvolution.AttackMul, 1f), "D: the final damage multiplier is gone after the end");

        // E: 次の LEVEL UP でまた候補に出られる
        gm.DebugTriggerLevelUp();
        yield return WaitChoiceOpen();
        var idsE = gm.DebugPendingChoiceIds;
        Check(idsE.Contains(FinalEvolution.ChoicePrefix + atk), $"E: the same FE can be offered again [{string.Join(",", idsE)}]");
        // F: 2回目の ACTIVE
        yield return PickInChoice(FinalEvolution.ChoicePrefix + atk);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Active && FinalEvolution.Uses(atk) == 2, $"F: second activation ({FinalEvolution.StageOf(atk)}, activations {FinalEvolution.Uses(atk)})");
        // G: 2回目に1回目の分が残っていない(ACTIVE の差は1回目と同じ)
        var act2 = FeSnap();
        yield return FeEndNatural(atk);
        var after2 = FeSnap();
        Check(FeSame(lv9, after2), $"G: after the second window nothing remains ({string.Join(", ", FeDiff(lv9, after2).Take(4))})");
        Check(Mathf.Abs(act2[System.Array.IndexOf(feSnapNames, "FE.AttackMul")] - act1[System.Array.IndexOf(feSnapNames, "FE.AttackMul")]) < 1e-4f, "G: the second window has the same strength as the first (no stacking)");

        // H: 3回以上でも永久の強化が無い
        for (int i = 0; i < 4; i++) { FeActivate(atk); yield return FeEndNatural(atk); }
        var afterH = FeSnap();
        Check(FeSame(lv9, afterH) && FinalEvolution.Uses(atk) == 6 && FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready, $"H: 6 windows -> no permanent buff, READY, activations {FinalEvolution.Uses(atk)} ({string.Join(", ", FeDiff(lv9, afterH).Take(4))})");

        // I: PHOENIX の専用復活は1回の ACTIVE につき1回、溜まらない。通常の Charge は変わらない
        string phx = "phoenix";
        yield return FeLv9(phx);
        FeReadyNow(phx); yield return null;
        int ch0 = gm.PhoenixCharges, nrm0 = GameManager.PhoenixRevives, er0 = FinalEvolution.EmergencyRevives;
        FeActivate(phx);
        bool w1a = FinalEvolution.TryEmergencyRevive(gm), w1b = FinalEvolution.TryEmergencyRevive(gm);
        yield return FeEndNatural(phx);
        FeActivate(phx); yield return FeEndNatural(phx);      // 使わずに終わる窓
        FeActivate(phx); FeActivate(phx);                       // ACTIVE 中にもう一度 = 重ならない
        bool w3a = FinalEvolution.TryEmergencyRevive(gm), w3b = FinalEvolution.TryEmergencyRevive(gm);
        yield return FeEndNatural(phx);
        Check(w1a && !w1b && w3a && !w3b && FinalEvolution.EmergencyRevives - er0 == 2, $"I: one rebirth per ACTIVE window, unused ones do not carry over (w1 {w1a}/{w1b}, w3 {w3a}/{w3b}, total {FinalEvolution.EmergencyRevives - er0})");
        Check(gm.PhoenixCharges == ch0 && GameManager.PhoenixRevives == nrm0 && !FinalEvolution.PhoenixTokenReady, $"I: normal PHOENIX charge untouched ({ch0} -> {gm.PhoenixCharges})");

        // J / K: EXP / MILE の倍率は溜まらない
        foreach (var id in new[] { "exp_up", "greed" })
        {
            yield return FeLv9(id);
            FeReadyNow(id); yield return null;
            var b = FeSnap();
            float[] first = null; bool same = true;
            for (int i = 0; i < 3; i++)
            {
                FeActivate(id);
                var a = FeSnap();
                if (first == null) first = a; else same &= FeSame(first, a);
                yield return FeEndNatural(id);
                same &= FeSame(b, FeSnap());
            }
            string what = id == "exp_up" ? "J: EXP" : "K: MILE";
            Check(same, $"{what} multipliers do not accumulate over 3 windows (each window the same, back to normal after each)");
            L($"[{what}] during: {string.Join(", ", FeDiff(b, first).Take(6))}");
        }

        // L: 属性の proc / リスナー / 追跡が残らない
        int lis0 = FinalEvolution.DebugListenerCount;
        foreach (var id in new[] { "flame_blade", "thunder_strike", "attack_range_up", "speed_up" }) { yield return FeLv9(id); FeReadyNow(id); }
        yield return null;
        var bL = FeSnap();
        var enemies = new List<EnemyController>();
        for (int i = 0; i < 6; i++) { var en = UltSpawn(5f + i * 1.3f); if (en != null) { en.ArenaDummy = true; enemies.Add(en); } }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            foreach (var id in new[] { "flame_blade", "thunder_strike", "attack_range_up", "speed_up" }) FeActivate(id);
            for (int f = 0; f < 30; f++) { foreach (var en in enemies) if (en != null && ElementSystem.IsAlive(en)) ElementSystem.OnPlayerHit(en, null, 20); yield return null; }
            foreach (var id in new[] { "flame_blade", "thunder_strike", "attack_range_up", "speed_up" }) FinalEvolution.DebugEnd(id);
        }
        foreach (var en in enemies) if (en != null) Destroy(en.gameObject);
        yield return new WaitForSeconds(1.2f);
        var aL = FeSnap();
        Check(FeSame(bL, aL) && FinalEvolution.DebugListenerCount == lis0 && FinalEvolution.DebugTrackedTargets == 0 && GameObject.Find("FinalEvoSlashWave") == null,
            $"L: no proc/listener/tracking residue after 3 cycles (listeners {lis0}->{FinalEvolution.DebugListenerCount}, tracked {FinalEvolution.DebugTrackedTargets}, diff {string.Join(", ", FeDiff(bL, aL).Take(4))})");

        // M: CONTINUE(ACTIVE 中の保存 → 読み込みで二重にならない / 古い USED の保存は READY)
        FeActivate(atk);
        yield return FeLv9("vampire"); FeReadyNow("vampire"); yield return null; FeActivate("vampire");
        var mBefore = FeSnap(); string sigBefore = FeSig();
        var back = JsonUtility.FromJson<RunCheckpoint.Data>(JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = FinalEvolution.Export() }));
        FinalEvolution.Import(back.finalEvolution);
        var mAfter = FeSnap();
        Check(FeSame(mBefore, mAfter) && sigBefore == FeSig(), $"M: CONTINUE during ACTIVE restores the same state, no double modifiers ({string.Join(", ", FeDiff(mBefore, mAfter).Take(4))})");
        FinalEvolution.Import(back.finalEvolution); // 2回読んでも同じ
        Check(FeSame(mBefore, FeSnap()), "M: importing twice still does not double anything");
        yield return FeEndNatural(atk); yield return FeEndNatural("vampire");
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready && FinalEvolution.Uses(atk) == 7, $"M: after CONTINUE the window ends -> READY (activations {FinalEvolution.Uses(atk)})");
        // 第1段階の保存: 使った後(READY でない)= USED → READY に読み替える
        var keep = JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = FinalEvolution.Export() });
        var oldSave = new List<FinalEvolution.SaveState> { new FinalEvolution.SaveState { id = "attack_up", eligible = true, eligibleAt = 100f, ready = false, active = false, uses = 1 } };
        FinalEvolution.Import(JsonUtility.FromJson<RunCheckpoint.Data>(JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = oldSave })).finalEvolution);
        Check(FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready && FinalEvolution.Uses(atk) == 1, $"M: an old phase-1 save (USED) loads as READY ({FinalEvolution.StageOf(atk)})");
        var old = JsonUtility.FromJson<RunCheckpoint.Data>("{\"active\":true,\"maxDistance\":1234}");
        Check(old.finalEvolution != null && old.finalEvolution.Count == 0, "M: older CONTINUE data without FE state loads with no state");
        FinalEvolution.Import(JsonUtility.FromJson<RunCheckpoint.Data>(keep).finalEvolution); // 続きのために元の状態へ
        // PHOENIX: 使った緊急復活は CONTINUE で戻らない
        FeReadyNow(phx); yield return null; FeActivate(phx); FinalEvolution.TryEmergencyRevive(gm);
        FinalEvolution.Import(JsonUtility.FromJson<RunCheckpoint.Data>(JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = FinalEvolution.Export() })).finalEvolution);
        Check(FinalEvolution.IsActive(phx) && !FinalEvolution.PhoenixTokenReady, "M: a used FE rebirth is not given back by CONTINUE");
        yield return FeEndNatural(phx);

        // N: AWAKENED でも同じ(持続だけ +10%)
        FinalEvolution.DebugForceAwakened = true;
        var bN = FeSnap(); float[] firstN = null; bool okN = true;
        for (int i = 0; i < 3; i++)
        {
            FeActivate(atk);
            okN &= Mathf.Abs(FinalEvolution.Remaining(atk) - FeDuration(atk) * (1f + T.awakenedDurationBonus)) < 0.05f;
            var a = FeSnap(); if (firstN == null) firstN = a; else okN &= FeSame(firstN, a);
            yield return FeEndNatural(atk);
            okN &= FeSame(bN, FeSnap()) && FinalEvolution.StageOf(atk) == FinalEvolution.Stage.Ready;
        }
        FinalEvolution.DebugForceAwakened = false;
        Check(okN, "N: AWAKENED reuses the same way (duration +10% every time, nothing remains)");

        // O: 別の FE 同士は同時に ACTIVE
        FeActivate(atk); FeActivate("exp_up"); FeActivate("greed"); FeActivate("speed_up");
        Check(FinalEvolution.ActiveCount >= 4 && FinalEvolution.IsActive(atk) && FinalEvolution.IsActive("greed"), $"O: different FEs are ACTIVE together ({FinalEvolution.ActiveCount})");
        foreach (var id in new[] { atk, "exp_up", "greed", "speed_up" }) FinalEvolution.DebugEnd(id);

        // 公平さ: 3つ READY → 30回の候補で回数の差は1以内、どれも出る
        var fair = new[] { "attack_up", "exp_up", "greed" };
        var cnt = fair.ToDictionary(x => x, x => 0);
        for (int i = 0; i < 30; i++) { var p = FinalEvolution.PickCandidate(); if (p != null && cnt.ContainsKey(p)) cnt[p]++; }
        L($"[fair] {string.Join(" ", cnt.Select(kv => kv.Key + "=" + kv.Value))} (other READY abilities share the slot too)");
        Check(fair.All(x => cnt[x] > 0), "fairness: every READY FE gets offered");
        FinalEvolution.DebugMakeReady("speed_up");
        FeActivate("speed_up");
        bool never = true; for (int i = 0; i < 20; i++) if (FinalEvolution.PickCandidate() == "speed_up") never = false;
        Check(never, "fairness: an ACTIVE FE is never picked as a candidate");
        FinalEvolution.DebugEnd("speed_up");
        Shot("fe2_R_hud");
        yield return V3End();
    }

    // ===================================================================== A
    IEnumerator Fe2AllCards()
    {
        L("== A: 全対象カード(データから)==");
        var T = FinalEvolutionTuning.I;
        // 定義: #100 ULTIMATE 以外の全カードに FE の定義がある / ULTIMATE には無い / 重複が無い
        var all = CardDatabase.AllCards.Where(c => c != null).Select(c => c.cardId).Distinct().ToList();
        var ids = T.entries.Where(e => e != null).Select(e => e.abilityId).ToList();
        var missing = all.Where(id => id != "character_ultimate" && T.For(id) == null).ToList();
        Check(missing.Count == 0, $"every card except #100 ULTIMATE has a FINAL EVOLUTION ({all.Count} cards, missing: {string.Join(",", missing)})");
        Check(T.For("character_ultimate") == null && !FinalEvolution.Supported("character_ultimate"), "#100 ULTIMATE (character_ultimate) is not a FINAL EVOLUTION target");
        Check(ids.Distinct().Count() == ids.Count, "no duplicated FE entries");
        var badDur = T.entries.Where(e => e.kind == FinalEvolutionTuning.Kind.Time ? (e.durationSeconds < 5f || e.durationSeconds > 13f) : (e.durationMeters < 1000f || e.durationMeters > 3000f)).Select(e => e.abilityId).ToList();
        Check(badDur.Count == 0, $"durations within 5-13s / 1,000-3,000m ({string.Join(",", badDur)})");
        L($"[A] cards {all.Count}, FE targets {ids.Count}");

        yield return V3Begin("swordsman");
        FeInvincible(true);
        BossManager.SuppressGates = true;
        WarpTo(1200f);
        yield return new WaitForSeconds(0.5f);
        var sb = new StringBuilder("id\tname\tcategory\tmodule\tkind\tduration\tamplify\tbonuses\teligible\tready\tcandidate\tactive\teffect\tend\treadyAgain\tsecond\tsame2nd\tawakened\tsave\trepeat10\texceptions\tchanges\n");
        int pass = 0, fail = 0;
        int obj0 = -1, fx0 = -1;
        foreach (var e in T.entries)
        {
            string id = e.abilityId;
            var card = CardDatabase.FindBaseById(id);
            int exc0 = excCount;
            FinalEvolution.DebugForceAwakened = false;
            V3Reset();
            // 封印(SacrificeHearts)のカードは最大HPが足りないと Lv9 まで取れない(ゲームの規則)→ 先に HEART UP で余裕を作る
            bool needHearts = card != null && card.effects != null && card.effects.Any(x => x.type == EffectType.SacrificeHearts);
            if (needHearts) FePicks("heart_up", 9);
            yield return null;
            // Lv8 → 資格なし / Lv9 → 資格
            FePicks(id, 8); yield return null;
            bool lv8None = FinalEvolution.StageOf(id) == FinalEvolution.Stage.None;
            FePicks(id, 1); yield return null; yield return null;
            bool elig = lv8None && gm.GetAbilityRunStack(id) == 9 && FinalEvolution.StageOf(id) == FinalEvolution.Stage.Eligible && Mathf.Abs(FinalEvolution.EligibleAt(id) - gm.MaxDistance) < 20f;
            // 初回 +5,000m
            FinalEvolution.DebugSetEligibleAt(id, gm.MaxDistance - T.readyMeters + 50f); yield return null;
            bool notYet = FinalEvolution.StageOf(id) == FinalEvolution.Stage.Eligible;
            FeReadyNow(id); yield return null;
            bool ready = notYet && FinalEvolution.StageOf(id) == FinalEvolution.Stage.Ready;
            bool cand = FinalEvolution.PickCandidate() == id;
            var lv9 = FeSnap();
            // 発動
            bool act = FeActivate(id) && FinalEvolution.Uses(id) == 1 && Mathf.Abs(FinalEvolution.Remaining(id) - FeDuration(id)) < 0.01f;
            var a1 = FeSnap();
            var changes = FeDiff(lv9, a1);
            bool effect = changes.Count > 0;
            bool noSelf = FinalEvolution.PickCandidate() == null; // ACTIVE の間は候補に出ない(他は READY でない)
            // 終了 → READY
            yield return FeEndNatural(id);
            var e1 = FeSnap();
            bool end = FeSame(lv9, e1) && !FinalEvolution.IsActive(id);
            bool readyAgain = FinalEvolution.StageOf(id) == FinalEvolution.Stage.Ready && FinalEvolution.PickCandidate() == id;
            // 2回目
            bool second = FeActivate(id) && FinalEvolution.Uses(id) == 2;
            bool same2 = FeSame(a1, FeSnap());
            // 保存と読み込み(ACTIVE 中)
            string sig = FeSig(); var sv = FeSnap();
            FinalEvolution.Import(JsonUtility.FromJson<RunCheckpoint.Data>(JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = FinalEvolution.Export() })).finalEvolution);
            bool save = sig == FeSig() && FeSame(sv, FeSnap());
            yield return FeEndNatural(id);
            // 10回(合計)
            for (int i = 2; i < 10; i++) { FeActivate(id); yield return null; FinalEvolution.DebugEnd(id); }
            bool rep = FinalEvolution.Uses(id) == 10 && FeSame(lv9, FeSnap()) && FinalEvolution.StageOf(id) == FinalEvolution.Stage.Ready;
            // AWAKENED: 持続 +10%、効果は同じ
            FinalEvolution.DebugForceAwakened = true;
            bool awSame = FeSame(lv9, FeSnap()); // AWAKENED だけでは通常の値は変わらない
            FeActivate(id);
            bool aw = awSame && Mathf.Abs(FinalEvolution.Remaining(id) - FeDuration(id) * (1f + T.awakenedDurationBonus)) < 0.01f && FeSame(a1, FeSnap());
            yield return FeEndNatural(id);
            aw &= FeSame(lv9, FeSnap());
            FinalEvolution.DebugForceAwakened = false;
            int exc = excCount - exc0;
            bool ok = elig && ready && cand && act && effect && noSelf && end && readyAgain && second && same2 && save && rep && aw && exc == 0;
            if (ok) pass++; else fail++;
            Check(ok, $"[{id}] elig={elig} ready={ready} cand={cand} act={act} effect={effect} noSelf={noSelf} end={end} readyAgain={readyAgain} second={second} same2={same2} save={save} repeat10={rep} awakened={aw} exc={exc}");
            string bon = e.bonuses == null || e.bonuses.Count == 0 ? "-" : string.Join(" ", e.bonuses.Select(b => $"{b.type}+{b.value:0.##}"));
            sb.AppendLine($"{id}\t{(card != null ? card.cardName : "?")}\t{(card != null ? card.category.ToString() : "?")}\t{e.module}\t{e.kind}\t{(e.kind == FinalEvolutionTuning.Kind.Time ? e.durationSeconds + "s" : e.durationMeters + "m")}\t{e.amplify:0.##}\t{bon}\t{elig}\t{ready}\t{cand}\t{act}\t{effect}\t{end}\t{readyAgain}\t{second}\t{same2}\t{aw}\t{save}\t{rep}\t{exc}\t{string.Join(" | ", changes.Take(8))}");
            if (obj0 < 0) { obj0 = FeObjects(); fx0 = FeFx(); FeCensusDiff(); }
        }
        V3Reset();
        yield return new WaitForSeconds(1.5f);
        int obj1 = FeObjects(), fx1 = FeFx();
        L($"[A] objects by name (top increases): {FeCensusDiff()}");
        L($"[A] {pass} passed, {fail} failed; effects/projectiles {fx0} -> {fx1}, all objects (incl. terrain/enemies while running) {obj0} -> {obj1} after all {T.entries.Count} cards x 11 windows");
        Check(fx1 <= fx0 + 20 && FinalEvolution.DebugListenerCount == 0 && FinalEvolution.DebugTrackedTargets == 0, $"no effect/projectile/listener build-up after every card's 11 windows (fx {fx0} -> {fx1})");
        V3Write("fe2_cards.tsv", sb);
        yield return V3End();
    }

    // ===================================================================== S
    IEnumerator Fe2Stress()
    {
        L("== S: 組み合わせの負荷 ==");
        var sb = new StringBuilder("case\tfe\tsecs\tavgMs\tmaxMs\tp99Ms\tgc0\tmonoKB\tmaxLiving\tprocs\tprocDrops\tfxDrops\tlightningHits\tspread\tfx\tobjects\tresidue\tsave\texceptions\n");
        var cases = new (string name, string[] cards, bool live)[]
        {
            ("Attack+AttackSpeed+DoubleAttack", new[] { "attack_up", "attack_speed_up", "double_attack" }, false),
            ("Fire+Inferno+BurningSoul", new[] { "flame_blade", "inferno", "burning_soul" }, false),
            ("Lightning+ChainLightning+ThunderLord", new[] { "thunder_strike", "chain_lightning", "thunder_lord" }, false),
            ("Speed+Momentum+Overdrive", new[] { "speed_up", "momentum", "overdrive" }, false),
            ("Vampire+Overheal+Blood", new[] { "vampire", "overheal", "blood_rush", "predator", "blood_blade" }, false),
            ("EXP x4", new[] { "exp_up", "pathfinder", "experience_burst", "level_break" }, false),
            ("MILE x4", new[] { "greed", "treasure_hunter", "executioner", "exp_converter" }, false),
            ("HellMode+Pandemonium+Horde", new[] { "hell_mode", "pandemonium", "horde" }, true),
            ("Phoenix+LastChance+SecondWind", new[] { "phoenix", "last_chance", "second_wind" }, false),
            ("FE x6 + ULTIMATE", new[] { "attack_up", "flame_blade", "thunder_strike", "speed_up", "vampire", "tornado" }, false),
        };
        float secs = float.Parse(Arg("-qaFe2StressSecs", "8"), IC);
        foreach (var cs in cases)
        {
            yield return V3Begin("swordsman");
            FeInvincible(true);
            BossManager.SuppressGates = true;
            WarpTo(8200f);
            yield return new WaitForSeconds(0.8f);
            V3Reset();
            foreach (var id in cs.cards) yield return FeLv9(id);
            if (cs.name.Contains("ULTIMATE")) V3Apply(UltimateArt.CardId, 3);
            foreach (var id in cs.cards) FeReadyNow(id);
            yield return null;
            var baseSnap = FeSnap();
            int exc0 = excCount;
            ElementSystem.ResetCounters(); CardProcs.ResetCounters(); ChallengeSystem.ResetCounters();
            int sp0 = FinalEvolution.SpreadBurns;
            int objBefore = FeObjects(); FeCensusDiff(); int fxBefore = FeFx();
            foreach (var id in cs.cards) FeActivate(id);
            int nAct = cs.cards.Count(FinalEvolution.IsActive);
            if (cs.name.Contains("ULTIMATE") && UltimateArt.Instance != null)
            {
                float wu = 0f; while ((Time.timeScale <= 0f || gm.UltimateChoiceOpen || pc.IsReacting) && wu < 3f) { yield return null; wu += Time.unscaledDeltaTime; } // FE の発動のヒットストップが明けるまで
                UltimateArt.Instance.DebugSetGauge(100f);
                bool u = UltimateArt.Instance.TryActivate("qa fe2");
                L($"[S] ULTIMATE activation with {nAct} FEs: {u} {(u ? "" : UltimateArt.Instance.LastBlockReason)}");
            }
            // 保存の往復(ACTIVE が複数)
            string sig = FeSig(); var sv = FeSnap();
            FinalEvolution.Import(JsonUtility.FromJson<RunCheckpoint.Data>(JsonUtility.ToJson(new RunCheckpoint.Data { finalEvolution = FinalEvolution.Export() })).finalEvolution);
            bool save = sig == FeSig() && FeSame(sv, FeSnap());
            // PHOENIX 系: 致死の被弾(FE の復活は窓に1回、通常の PHOENIX/LAST CHANCE/SECOND WIND は今まで通り)
            if (cs.name.StartsWith("Phoenix"))
            {
                FeInvincible(false); stopKeepAlive = true; yield return null;
                int per = CombatScale.HpPerHeart, er0 = FinalEvolution.EmergencyRevives, pc0 = gm.PhoenixConsumedCount;
                var log = new List<string>();
                for (int h = 0; h < 3 && !gm.IsGameOver; h++)
                {
                    pc.CardTestSetShield(0); SetPrivate(pc, "hitInvincibleTimer", 0f);
                    FeLives(per);
                    var r = gm.TryDamagePlayer(true, "QaFe2", CombatScale.PlayerHeavyHit * 3);
                    log.Add($"{r}:lives{gm.Lives}:fe{FinalEvolution.EmergencyRevives - er0}:phx{gm.PhoenixConsumedCount - pc0}");
                    yield return new WaitForSeconds(0.3f);
                }
                L($"[S] phoenix lethal hits: {string.Join("  ", log)}");
                Check(FinalEvolution.EmergencyRevives - er0 == 1, $"Phoenix combo: the FE rebirth fires once per window ({FinalEvolution.EmergencyRevives - er0})");
                if (gm.IsGameOver) { L("[S] (game over after the revives ran out - expected)"); yield return EndRun(); continue; }
                FeLives(990); FeInvincible(true); stopKeepAlive = false; StartCoroutine(KeepAlive());
            }
            // 走りながら殴る(実際の攻撃 + 属性の命中)
            var enemies = new List<EnemyController>();
            var frames = new List<float>();
            int gc0 = System.GC.CollectionCount(0); long mono0 = System.GC.GetTotalMemory(false);
            int maxLiving = 0;
            float t0 = Time.time, lastFlick = 0f, lastSpawn = 0f;
            if (!cs.live && EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            while (Time.time - t0 < secs)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                maxLiving = Mathf.Max(maxLiving, ChallengeSystem.LivingEnemies);
                if (Time.time - lastFlick > 0.15f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
                if (!cs.live && Time.time - lastSpawn > 0.4f)
                {
                    lastSpawn = Time.time;
                    foreach (var x in enemies) if (x != null && x.transform.position.x < pc.transform.position.x - 6f) Destroy(x.gameObject); // 置いた敵は通り過ぎたら片付ける
                    enemies.RemoveAll(x => x == null || !ElementSystem.IsAlive(x) || x.transform.position.x < pc.transform.position.x - 6f);
                    while (enemies.Count < 8) { var en = UltSpawn(4f + enemies.Count * 1.4f); if (en == null) break; enemies.Add(en); }
                }
                foreach (var en in enemies) if (en != null && ElementSystem.IsAlive(en)) ElementSystem.OnPlayerHit(en, null, 20);
                // 時間型は残りを保つ(負荷を見る間ずっと ACTIVE)
                foreach (var id in cs.cards) if (FinalEvolution.IsActive(id) && FinalEvolution.Remaining(id) < 2f) FinalEvolution.DebugSetRemaining(id, 2f);
            }
            frames.Sort();
            float avg = frames.Count > 0 ? frames.Average() : 0f, mx = frames.Count > 0 ? frames[frames.Count - 1] : 0f, p99 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))] : 0f;
            int procs = CardProcs.Shockwaves + CardProcs.ChainExplosions + CardProcs.Infernos + CardProcs.Tornados + CardProcs.DoubleAttacks + ElementSystem.LightningHits + ElementSystem.BurnProcs;
            long monoKB = (System.GC.GetTotalMemory(false) - mono0) / 1024;
            foreach (var en in enemies) if (en != null) Destroy(en.gameObject);
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
            // 終了 → 倍率が残らない
            foreach (var id in cs.cards) FinalEvolution.DebugEnd(id);
            if (UltimateArt.Instance != null) UltimateArt.Instance.DebugEndBuff();
            yield return new WaitForSeconds(1.5f);
            var after = FeSnap();
            var residue = FeDiff(baseSnap, after);
            int objAfter = FeObjects(); int fxAfter = FeFx();
            L($"[S] {cs.name} objects by name: {FeCensusDiff()}");
            int exc = excCount - exc0;
            string line = $"{cs.name}\t{nAct}\t{secs:F0}\t{avg:F1}\t{mx:F1}\t{p99:F1}\t{System.GC.CollectionCount(0) - gc0}\t{monoKB}\t{maxLiving}\t{procs}\t{CardProcs.ProcBudgetDrops}\t{CardProcs.FxBudgetDrops}\t{ElementSystem.LightningHits}\t{FinalEvolution.SpreadBurns - sp0}\t{fxBefore}->{fxAfter}\t{objBefore}->{objAfter}\t{(residue.Count == 0 ? "-" : string.Join(" | ", residue.Take(4)))}\t{save}\t{exc}";
            sb.AppendLine(line);
            L($"[S] {line.Replace('\t', ' ')}");
            Check(nAct == cs.cards.Length, $"{cs.name}: all {cs.cards.Length} FEs active together ({nAct})");
            Check(residue.Count == 0, $"{cs.name}: no multiplier left after the end ({string.Join(", ", residue.Take(4))})");
            Check(save, $"{cs.name}: CONTINUE round trip with several ACTIVE FEs");
            Check(exc == 0, $"{cs.name}: no exceptions");
            Check(ElementSystem.LightningHits < secs * 60 * 4 && procs < secs * 60 * 12, $"{cs.name}: procs bounded (lightning hits {ElementSystem.LightningHits}, procs {procs})");
            Check(p99 < 50f, $"{cs.name}: no sustained frame spikes (p99 {p99:F1}ms, max {mx:F1}ms)");
            Check(fxAfter <= fxBefore + 20, $"{cs.name}: effects/projectiles return to normal ({fxBefore} -> {fxAfter}; all objects incl. terrain/enemies {objBefore} -> {objAfter})");
            yield return V3End();
        }
        V3Write("fe2_stress.tsv", sb);
    }

    // ===================================================================== P
    IEnumerator Fe2Repeat()
    {
        L("== P: 実時間の10回反復 ==");
        var sb = new StringBuilder("id\tcycle\tfx\tenemies\tobjects\tlisteners\ttracked\tslashWaves\tmaxMs\tavgMs\tdiff\n");
        var list = Arg("-qaFe2RepeatIds", "speed_up,attack_range_up,flame_blade,thunder_strike,tornado,gale,shockwave,chain_explosion,vampire,phoenix,more_enemies,horde,greed,exp_up").Split(',');
        yield return V3Begin("swordsman");
        FeInvincible(true);
        BossManager.SuppressGates = true;
        WarpTo(3200f);
        yield return new WaitForSeconds(0.5f);
        foreach (var id in list)
        {
            if (FinalEvolutionTuning.I.For(id) == null) { Warn($"no FE for {id}"); continue; }
            V3Reset();
            yield return FeLv9(id);
            FeReadyNow(id); yield return null;
            var b = FeSnap();
            int exc0 = excCount;
            int obj1 = 0, objN = 0, lis1 = 0, lisN = 0, fx1 = 0, fxN = 0, fxMax = 0; float[] first = null; bool sameEach = true, backEach = true;
            var enemies = new List<EnemyController>();
            for (int c = 0; c < 10; c++)
            {
                FeActivate(id);
                var a = FeSnap(); if (first == null) first = a; else sameEach &= FeSame(first, a);
                var fr = new List<float>();
                float t0 = Time.time, lastFlick = 0f;
                while (Time.time - t0 < 1.0f)
                {
                    yield return null;
                    fr.Add(Time.unscaledDeltaTime * 1000f);
                    foreach (var x in enemies) if (x != null && x.transform.position.x < pc.transform.position.x - 6f) Destroy(x.gameObject);
                    enemies.RemoveAll(x => x == null || !ElementSystem.IsAlive(x) || x.transform.position.x < pc.transform.position.x - 6f);
                    while (enemies.Count < 5) { var en = UltSpawn(4f + enemies.Count * 1.5f); if (en == null) break; enemies.Add(en); }
                    foreach (var en in enemies) if (en != null && ElementSystem.IsAlive(en)) ElementSystem.OnPlayerHit(en, null, 20);
                    if (Time.time - lastFlick > 0.15f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
                }
                yield return FeEndNatural(id);
                yield return new WaitForSeconds(0.6f);
                var d = FeDiff(b, FeSnap());
                backEach &= d.Count == 0;
                int objs = FeObjects(), lis = FinalEvolution.DebugListenerCount, fx = FeFx();
                fxMax = Mathf.Max(fxMax, fx);
                if (c == 0) { obj1 = objs; lis1 = lis; fx1 = fx; FeCensusDiff(); }
                fxN = fx;
                if (c == 9) L($"[P] {id} objects by name, cycle 1 -> 10: {FeCensusDiff()}");
                objN = objs; lisN = lis;
                sb.AppendLine($"{id}\t{c + 1}\t{fx}\t{FeEnemies()}\t{objs}\t{lis}\t{FinalEvolution.DebugTrackedTargets}\t{FinalEvolution.SlashWaves}\t{fr.Max():F1}\t{fr.Average():F1}\t{string.Join(" | ", d.Take(3))}");
            }
            foreach (var en in enemies) if (en != null) Destroy(en.gameObject);
            L($"[P] {id}: activations {FinalEvolution.Uses(id)}, effects/projectiles after each window {fx1} -> {fxN} (max {fxMax}), all objects {obj1} -> {objN}, listeners {lis1} -> {lisN}, tracked {FinalEvolution.DebugTrackedTargets}");
            Check(FinalEvolution.Uses(id) == 10 && FinalEvolution.StageOf(id) == FinalEvolution.Stage.Ready, $"P {id}: 10 activations, READY at the end");
            Check(sameEach && backEach, $"P {id}: every window has the same strength and nothing remains after each");
            Check(lisN == lis1 && fxN <= fx1 + 15 && fxMax <= fx1 + 30 && FinalEvolution.DebugTrackedTargets == 0, $"P {id}: listeners/effects/projectiles/tracking do not build up (listeners {lis1}->{lisN}, fx {fx1}->{fxN} max {fxMax})");
            Check(excCount == exc0, $"P {id}: no exceptions");
        }
        V3Write("fe2_repeat.tsv", sb);
        yield return V3End();
    }
}
#endif

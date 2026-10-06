#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// COMBO 第1段階(2026-10-06)の確認。 -qaCombo <dir> [-qaComboOnly BCS]
//  B 基本 A〜V(成立/残る/Lv1/Lv の伸び/キャラカード/同じ能力の重複/同時/1枚が2つへ/再帰しない/FE で ENHANCED/戻る/両方 FE/AWAKENED/ULTIMATE/保存/二重読込/新しいラン/マルチ)
//  C 全 COMBO をデータから: それぞれの現象を実際に起こす(炎上/凍結/落雷/風刃/溢れた回復/速さ/OVERDRIVE/Shield/空中/締め/低HP)+ FE の ENHANCED + 10回の FE の繰り返し → combo_all.tsv
//  S 負荷: 全 COMBO + 複数 FE + ULTIMATE + 敵10体 → combo_stress.tsv
public partial class QaSweep
{
    PlayerAttackInfo comboInfo;
    PlayerAttackInfo ComboInfo(bool finisher = false, bool wind = false)
    {
        if (comboInfo == null)
        {
            var go = new GameObject("QaComboAttack");
            DontDestroyOnLoad(go);
            comboInfo = go.AddComponent<PlayerAttackInfo>();
        }
        comboInfo.kind = PlayerAttackKind.Normal;
        comboInfo.elementProc = wind; comboInfo.windBlade = wind;
        comboInfo.seqTag = finisher ? AttackSeqTag.Finisher : AttackSeqTag.None;
        comboInfo.seqMoveId = finisher ? ++comboMove : 0;
        return comboInfo;
    }
    int comboMove = 1000;

    void ComboGive(string id, int lv) { int have = gm.GetAbilityRunStack(id); if (have < lv) FePicks(id, lv - have); }
    ComboSystem.Active CA(string id) { ComboSystem.Recompute(); return ComboSystem.Instance.Get(id); }
    List<EnemyController> ComboEnemies(int n, float dx0 = 4f)
    {
        var l = new List<EnemyController>();
        for (int i = 0; i < n; i++) { var en = UltSpawn(dx0 + i * 1.2f); if (en != null) { en.ArenaDummy = true; l.Add(en); } }
        return l;
    }
    void ComboHitAll(List<EnemyController> es, bool finisher = false, bool wind = false)
    {
        foreach (var e in es) if (e != null && ElementSystem.IsAlive(e)) { var info = ComboInfo(finisher, wind); CardProcs.OnPlayerHit(e, info, null, 20); ElementSystem.OnPlayerHit(e, info, 20); }
    }
    static readonly MethodInfo CPowerM = typeof(CardProcs).GetMethod("CPower", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo CRadiusM = typeof(CardProcs).GetMethod("CRadius", BindingFlags.NonPublic | BindingFlags.Static);

    IEnumerator ComboMode()
    {
        Application.targetFrameRate = 60;
        autoPickHold = true;
        L($"[tuning] combos={ComboTuning.I.combos.Count} enabled={ComboTuning.I.combos.Count(c => c.enabled)}");
        string only = Arg("-qaComboOnly", "BCS");
        if (only.Contains('B')) yield return ComboBasics();
        if (only.Contains('C')) yield return ComboAll();
        if (only.Contains('S')) yield return ComboStress();
        autoPickHold = false;
    }

    IEnumerator ComboBegin()
    {
        yield return V3Begin("swordsman");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        BossManager.SuppressGates = true;
        WarpTo(1200f);
        yield return new WaitForSeconds(0.5f);
        V3Reset();
        yield return null;
    }

    // ===================================================================== B
    IEnumerator ComboBasics()
    {
        L("== B: 基本 ==");
        int exc0 = excCount;
        yield return ComboBegin();
        // A/B/C/D/E
        ComboGive("flame_blade", 1); yield return null;
        Check(CA("blazing_edge") == null, "A: only card A -> no COMBO");
        V3Reset(); ComboGive("burning_soul", 1); yield return null;
        Check(CA("blazing_edge") == null, "B: only card B -> no COMBO");
        int formed0 = ComboSystem.Formed;
        ComboGive("flame_blade", 1); yield return null;
        var be = CA("blazing_edge");
        Check(be != null && ComboSystem.Formed == formed0 + 1, "C/E: A+B (Lv1+Lv1) -> COMBO formed (once)");
        Check(gm.GetAbilityRunStack("flame_blade") == 1 && gm.GetAbilityRunStack("burning_soul") == 1, "D: the cards stay (Lv unchanged)");
        Shot("combo_B_formed");
        // F: Lv で伸びる / Lv9+Lv1 は完成の強さにならない
        float f11 = be.factor;
        ComboGive("flame_blade", 9); yield return null;
        float f91 = CA("blazing_edge").factor;
        ComboGive("burning_soul", 9); yield return null;
        float f99 = CA("blazing_edge").factor;
        L($"[F] factor Lv1+1 {f11:F2} / Lv9+1 {f91:F2} / Lv9+9 {f99:F2}");
        Check(f11 < f91 && f91 < f99 && f99 > 0.99f && f91 < 0.85f, "F: scaling grows with Lv, Lv9+Lv1 stays well below the full strength");
        // H: 同じ能力だけを重ねても成立しない
        V3Reset(); FePicks("flame_blade", 4); yield return null;
        Check(CA("blazing_edge") == null && ComboSystem.ActiveCount == 0, "H: the same ability twice does not form a COMBO");
        // I/J: 同時 / 1枚が2つへ
        ComboGive("burning_soul", 3); ComboGive("inferno", 3); yield return null;
        Check(CA("blazing_edge") != null && CA("wildfire") != null && ComboSystem.ActiveCount == 2, "I/J: two COMBOs at once, BURNING SOUL is in both");
        // K: COMBO の中の命中からは COMBO が出ない(再帰しない)
        var es = ComboEnemies(4);
        foreach (var e in es) ElementStatus.For(e).AddBurn(5f, 5f);
        yield return null;
        int p0 = ComboSystem.Procs;
        foreach (var e in es) CardProcs.DebugHitInsideCombo(e, ComboInfo());
        Check(ComboSystem.Procs == p0, $"K: a hit coming from a COMBO proc does not trigger COMBOs ({ComboSystem.Procs - p0})");
        // 通常の命中なら出る
        foreach (var e in es) CardProcs.OnPlayerHit(e, ComboInfo(), null, 20);
        Check(ComboSystem.Procs > p0, "K: a normal hit on a burning enemy does trigger BLAZING EDGE");
        foreach (var e in es) if (e != null) Destroy(e.gameObject);
        // M/N/O: FE で ENHANCED(1段階だけ)/戻る/両方でも同じ
        var a = CA("blazing_edge");
        float pw0 = (float)CPowerM.Invoke(null, new object[] { a }), rd0 = (float)CRadiusM.Invoke(null, new object[] { a });
        FinalEvolution.DebugMakeReady("flame_blade"); FeActivate("flame_blade"); yield return null;
        a = CA("blazing_edge");
        float pw1 = (float)CPowerM.Invoke(null, new object[] { a }), rd1 = (float)CRadiusM.Invoke(null, new object[] { a });
        Check(a.enhanced && pw1 > pw0 && rd1 > rd0, $"M: a part card in FINAL EVOLUTION -> COMBO ENHANCED (power {pw0:F1}->{pw1:F1}, radius {rd0:F2}->{rd1:F2})");
        FinalEvolution.DebugMakeReady("burning_soul"); FeActivate("burning_soul"); yield return null;
        a = CA("blazing_edge");
        float pw2 = (float)CPowerM.Invoke(null, new object[] { a });
        Check(a.enhanced && a.bothEnhanced && Mathf.Abs(pw2 - pw1) < 0.01f, $"O: both parts in FINAL EVOLUTION -> still one step (power {pw1:F1} = {pw2:F1})");
        FinalEvolution.DebugEnd("flame_blade"); FinalEvolution.DebugEnd("burning_soul"); yield return null;
        a = CA("blazing_edge");
        Check(!a.enhanced && Mathf.Abs((float)CPowerM.Invoke(null, new object[] { a }) - pw0) < 0.01f, "N: FINAL EVOLUTION ended -> normal COMBO again");
        // P: AWAKENED は能力値を変えない
        var s0 = FeSnap();
        ComboSystem.DebugForceAwakened = true; ComboSystem.Recompute(); yield return null;
        Check(CA("blazing_edge").awakened && FeSame(s0, FeSnap()), "P: AWAKENED only decorates (stats unchanged)");
        ComboSystem.DebugForceAwakened = false;
        // Q: ULTIMATE と一緒でも正常
        V3Apply(UltimateArt.CardId, 3);
        float wu = 0f; while ((Time.timeScale <= 0f || gm.UltimateChoiceOpen || pc.IsReacting) && wu < 3f) { yield return null; wu += Time.unscaledDeltaTime; }
        UltimateArt.Instance.DebugSetGauge(100f);
        bool ult = UltimateArt.Instance.TryActivate("qa combo");
        yield return new WaitForSeconds(1.5f);
        Check(ult && CA("blazing_edge") != null && CA("wildfire") != null, $"Q: ULTIMATE together with COMBOs works ({ult} {UltimateArt.Instance.LastBlockReason})");
        Check(!ComboTuning.I.combos.Any(c => c.abilities.Contains(UltimateArt.CardId)), "Q: #100 ULTIMATE is never a COMBO part");
        yield return new WaitForSeconds(3f);
        // R/S: 保存 / 二重読込
        ComboGive("vampire", 4); ComboGive("overheal", 4); yield return null;
        var ba = CA("blood_aegis"); ba.counter = 1;
        var json = JsonUtility.ToJson(new RunCheckpoint.Data { combo = ComboSystem.Export() });
        string sig() => string.Join(",", ComboSystem.Instance.ActiveCombos.Select(x => $"{x.def.id}:{x.level:F1}:{x.counter}"));
        string before = sig(); int formedB = ComboSystem.Formed;
        ComboSystem.Import(JsonUtility.FromJson<RunCheckpoint.Data>(json).combo);
        string after1 = sig();
        ComboSystem.Import(JsonUtility.FromJson<RunCheckpoint.Data>(json).combo);
        string after2 = sig();
        Check(before == after1 && after1 == after2 && ComboSystem.Formed == formedB, $"R/S: CONTINUE restores the same COMBOs and temporary state, twice = same, no re-announce ({before} | {after2})");
        var old = JsonUtility.FromJson<RunCheckpoint.Data>("{\"active\":true,\"maxDistance\":1234}");
        Check(old.combo != null, "R: old CONTINUE data without COMBO state loads");
        // U: マルチでは無効
        var mp = typeof(NetRunLauncher).GetProperty("IsMultiplayerRun");
        mp.GetSetMethod(true).Invoke(null, new object[] { true });
        ComboSystem.Recompute();
        bool off = !ComboSystem.Enabled && ComboSystem.ActiveCount == 0;
        mp.GetSetMethod(true).Invoke(null, new object[] { false });
        ComboSystem.Recompute();
        Check(off && ComboSystem.ActiveCount > 0, "U: COMBO effects are off in a multiplayer run (and back in solo)");
        // T: 新しいラン
        yield return V3End();
        yield return ComboBegin();
        Check(ComboSystem.ActiveCount == 0, "T: a new run starts with no COMBO");
        // G: キャラカード(開始時の Lv)+ ラン中のカード
        yield return V3End();
        {
            string k5 = K("flame_blade", 5);
            CardInventory.AddCard(k5, 5, 1);
            float w0 = 0f; while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
            gm = GameManager.Instance;
            gm.SetSelectedCharacter("swordsman");
            bool eq = gm.EquipCharacterCard(0, k5, 5);
            yield return BeginRun("swordsman", "wasteland_road");
            yield return null;
            FePicks("burning_soul", 1); yield return null;
            var g = CA("blazing_edge");
            Check(eq && g != null && gm.GetAbilityRunStack("flame_blade") == 5, $"G: character card (Lv{gm.GetAbilityRunStack("flame_blade")}) + run card -> COMBO");
            gm.EquipCharacterCard(0, "", 0);
            yield return EndRun();
            gm = GameManager.Instance;
        }
        Check(excCount == exc0, "V: no exceptions in the basics");
    }

    // ===================================================================== C
    IEnumerator ComboAll()
    {
        L("== C: 全 COMBO(データから)==");
        var sb = new System.Text.StringBuilder("id\tname\tparts\tmodule\tformed\tprocs\tenhanced\tfeBack\trepeat10\texceptions\n");
        yield return ComboBegin();
        foreach (var c in ComboTuning.I.combos.Where(x => x.enabled))
        {
            int exc0 = excCount;
            V3Reset();
            stopKeepAlive = false;
            PlayerController.DebugSpeedScale = 1f;
            foreach (var id in c.abilities) ComboGive(id, 5);
            if (c.module == ComboTuning.Module.DeathWish) ComboGive("heart_up", 9); // 封印で HP が足りなくならないように
            yield return null;
            var a = CA(c.id);
            bool formed = a != null;
            int p0 = a != null ? a.procs : 0;
            var es = ComboEnemies(8);
            yield return new WaitForSeconds(0.3f);
            float t = 0f;
            // それぞれの現象を実際に起こす(最大 8 秒)
            while (a != null && a.procs == p0 && t < 8f)
            {
                es.RemoveAll(e => e == null || !ElementSystem.IsAlive(e));
                if (es.Count < 4) es.AddRange(ComboEnemies(6));
                switch (c.module)
                {
                    case ComboTuning.Module.BurningBlast: foreach (var e in es) ElementStatus.For(e).AddBurn(5f, 5f); ComboHitAll(es); break;
                    case ComboTuning.Module.Wildfire: foreach (var e in es.Take(2)) { ElementStatus.For(e).AddBurn(5f, 5f); ElementSystem.DealQuiet(e, 999999, ElementType.None); } break;
                    case ComboTuning.Module.FreezeNova: case ComboTuning.Module.ExtraChain: case ComboTuning.Module.ChainBurst: ComboHitAll(es); break;
                    case ComboTuning.Module.Shatter: foreach (var e in es) for (int i = 0; i < 6; i++) ElementStatus.For(e).AddChill(0.4f, 3f, 2, 2f, 0); ComboHitAll(es, true); break;
                    case ComboTuning.Module.WindBurst: case ComboTuning.Module.WindTornado: ComboHitAll(es, false, true); break;
                    case ComboTuning.Module.BloodAegis: gm.CardHeal(gm.maxLives); break;
                    case ComboTuning.Module.SonicWave: PlayerController.DebugSpeedScale = 4f; ComboHitAll(es); break;
                    case ComboTuning.Module.Redline: PlayerController.DebugSpeedScale = 4f; break;
                    case ComboTuning.Module.AegisBreak:
                        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
                        pc.CardTestSetShield(1); SetPrivate(pc, "hitInvincibleTimer", 0f);
                        gm.TryDamagePlayer(false, "Enemy:qa-combo", 10);
                        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
                        break;
                    case ComboTuning.Module.AirAssault: yield return Flick(PlayerController.FlickDirection.Up); yield return new WaitForSeconds(0.2f); ComboHitAll(es); break;
                    case ComboTuning.Module.FinisherWave: ComboHitAll(es, true); break;
                    case ComboTuning.Module.DeathWish:
                        stopKeepAlive = true;
                        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { Mathf.Max(1, gm.maxLives * 4 / 10) });
                        ComboHitAll(es); break;
                }
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
                a = CA(c.id);
            }
            bool effect = a != null && a.procs > p0;
            PlayerController.DebugSpeedScale = 1f;
            stopKeepAlive = false; StartCoroutine(KeepAlive());
            // FE の ENHANCED / 終われば戻る / 10回
            string part = c.abilities[0];
            FinalEvolution.DebugMakeReady(part); FeActivate(part); yield return null;
            bool enh = CA(c.id) != null && CA(c.id).enhanced;
            FinalEvolution.DebugEnd(part); yield return null;
            bool back = CA(c.id) != null && !CA(c.id).enhanced;
            float pw = a != null ? (float)CPowerM.Invoke(null, new object[] { CA(c.id) }) : 0f;
            for (int i = 0; i < 10; i++) { FeActivate(part); yield return null; FinalEvolution.DebugEnd(part); }
            bool rep = CA(c.id) != null && !CA(c.id).enhanced && Mathf.Abs((float)CPowerM.Invoke(null, new object[] { CA(c.id) }) - pw) < 0.01f && FinalEvolution.StageOf(part) == FinalEvolution.Stage.Ready;
            foreach (var e in es) if (e != null) Destroy(e.gameObject);
            int exc = excCount - exc0;
            bool ok = formed && effect && enh && back && rep && exc == 0;
            Check(ok, $"[{c.id}] formed={formed} effect={effect} (procs {(a != null ? a.procs - p0 : 0)} in {t:F1}s) enhanced={enh} back={back} repeat10={rep} exc={exc}");
            sb.AppendLine($"{c.id}\t{c.displayName}\t{string.Join("+", c.abilities)}\t{c.module}\t{formed}\t{(a != null ? a.procs - p0 : 0)}\t{enh}\t{back}\t{rep}\t{exc}");
            yield return new WaitForSeconds(0.3f);
        }
        V3Write("combo_all.tsv", sb);
        yield return V3End();
    }

    // ===================================================================== S
    IEnumerator ComboStress()
    {
        // 同じ負荷を COMBO なし/あり で測って比べる(命中は1体あたり 0.15 秒に1回 = 10体で毎秒約66回。実際の手数より多め)
        ComboSystem.DebugDisabled = true;
        yield return ComboStressOne(false);
        ComboSystem.DebugDisabled = false;
        yield return ComboStressOne(true);
        L($"[S] frame avg: COMBO off {stressAvg[0]:F1}ms / on {stressAvg[1]:F1}ms, p99 off {stressP99[0]:F1} / on {stressP99[1]:F1}ms");
        Check(stressP99[1] <= stressP99[0] + 8f && stressAvg[1] <= stressAvg[0] * 1.25f + 1f, "S: COMBOs do not add frame spikes over the same load without them");
    }
    readonly float[] stressAvg = new float[2], stressP99 = new float[2];

    IEnumerator ComboStressOne(bool on)
    {
        L($"== S: 負荷(全 COMBO {(on ? "あり" : "なし")} + 複数 FE + ULTIMATE + 敵10体)==");
        yield return ComboBegin();
        foreach (var c in ComboTuning.I.combos) foreach (var id in c.abilities) ComboGive(id, 9);
        ComboGive("heart_up", 9);
        yield return null;
        ComboSystem.Recompute();
        int combos = ComboSystem.ActiveCount;
        foreach (var id in new[] { "flame_blade", "thunder_strike", "gale", "vampire", "speed_up", "combo_plus" }) { FinalEvolution.DebugMakeReady(id); FeActivate(id); }
        V3Apply(UltimateArt.CardId, 3);
        float wu = 0f; while ((Time.timeScale <= 0f || gm.UltimateChoiceOpen || pc.IsReacting) && wu < 3f) { yield return null; wu += Time.unscaledDeltaTime; }
        UltimateArt.Instance.DebugSetGauge(100f);
        bool ult = UltimateArt.Instance.TryActivate("qa combo stress");
        int fx0 = FeFx(), exc0 = excCount, gc0 = System.GC.CollectionCount(0);
        int procs0 = ComboSystem.Procs; long dmg0 = ElementSystem.ElementDamage;
        var frames = new List<float>();
        var es = new List<EnemyController>();
        float t = 0f, lastFlick = 0f, lastHeal = 0f, lastHit = 0f;
        while (t < 8f)
        {
            yield return null; t += Time.unscaledDeltaTime;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            es.RemoveAll(e => e == null || !ElementSystem.IsAlive(e) || e.transform.position.x < pc.transform.position.x - 6f);
            while (es.Count < 10) { var en = UltSpawn(4f + es.Count * 1.1f); if (en == null) break; es.Add(en); }
            if (Time.time - lastHit > 0.15f) { lastHit = Time.time; ComboHitAll(es, Time.frameCount % 7 == 0, Time.frameCount % 5 == 0); }
            if (Time.time - lastFlick > 0.15f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            if (Time.time - lastHeal > 1f) { lastHeal = Time.time; gm.CardHeal(gm.maxLives); }
            foreach (var id in new[] { "flame_blade", "thunder_strike", "gale", "vampire", "speed_up", "combo_plus" }) if (FinalEvolution.IsActive(id) && FinalEvolution.Remaining(id) < 2f) FinalEvolution.DebugSetRemaining(id, 2f);
        }
        foreach (var e in es) if (e != null) Destroy(e.gameObject);
        foreach (var id in new[] { "flame_blade", "thunder_strike", "gale", "vampire", "speed_up", "combo_plus" }) FinalEvolution.DebugEnd(id);
        yield return new WaitForSeconds(1.5f);
        frames.Sort();
        float avg = frames.Average(), mx = frames[frames.Count - 1], p99 = frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))];
        int procs = ComboSystem.Procs - procs0, fx1 = FeFx();
        string by = string.Join(" ", CardProcs.ComboProcsById.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
        L($"[S] combos {combos}, ULTIMATE {ult}, 8s x 10 enemies: frame avg {avg:F1} p99 {p99:F1} max {mx:F1}ms, gc0 {System.GC.CollectionCount(0) - gc0}, combo procs {procs} ({procs / 8f:F0}/s), proc budget drops {CardProcs.ProcBudgetDrops}, element dmg {ElementSystem.ElementDamage - dmg0}, fx {fx0}->{fx1}, by combo: {by}");
        V3Write("combo_stress.tsv", new System.Text.StringBuilder($"combos\tult\tavgMs\tp99Ms\tmaxMs\tprocs\tdrops\tfx\n{combos}\t{ult}\t{avg:F1}\t{p99:F1}\t{mx:F1}\t{procs}\t{CardProcs.ProcBudgetDrops}\t{fx0}->{fx1}\n"));
        stressAvg[on ? 1 : 0] = avg; stressP99[on ? 1 : 0] = p99;
        if (on) Check(combos == ComboTuning.I.combos.Count(c => c.enabled), $"S: all COMBOs active together ({combos})");
        Check(procs < 8f * 60f * 6f, $"S: COMBO procs are bounded ({procs} in 8s)");
        Check(fx1 <= fx0 + 25, $"S: effects return to normal ({fx0} -> {fx1})");
        Check(excCount == exc0, "S: no exceptions");
        yield return V3End();
    }
}
#endif

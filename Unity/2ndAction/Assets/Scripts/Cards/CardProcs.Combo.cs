using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// COMBO 第1段階(2026-10-06)の効果。定義は ComboTuning、成立は ComboSystem。
// 安全策:
//  ・発生源(ProcSource)を持つ。COMBO 由来の攻撃からは COMBO を出さない(comboDepth で構造的に止める)。
//    COMBO のダメージは ElementSystem.DealQuiet(命中の処理=追加攻撃/属性の判定を通らない)なので、Proc → Proc の連鎖にならない。
//  ・既存の proc の予算(0.5秒の窓)/演出の予算/間隔(ICD)/同じ敵へのクールダウンを使う。範囲/数に上限。
//  ・COMBO の炎上で倒れた敵からは WILDFIRE を出さない(ElementStatus.ComboBurned)。
public static partial class CardProcs
{
    public enum ProcSource { Normal, Card, Combo, FinalEvolution, Ultimate }
    public static int ComboHits; // 確認用(COMBO の効果が出た回数)
    public static readonly Dictionary<string, int> ComboProcsById = new Dictionary<string, int>();
    static int comboDepth;
    public static bool InComboProc => comboDepth > 0;

    static readonly Dictionary<Component, float> comboTargetCd = new Dictionary<Component, float>();
    static readonly Dictionary<Component, float> comboStrikeCd = new Dictionary<Component, float>();
    static int lastFinisherWaveMove = -1;
    static int comboTornadoAlive;
    static float redlineFor;

    public static void ComboResetRun()
    {
        comboTargetCd.Clear(); comboStrikeCd.Clear(); ComboProcsById.Clear();
        lastFinisherWaveMove = -1; comboTornadoAlive = 0; redlineFor = 0f; comboDepth = 0;
    }

    static ProcSource SourceOf(PlayerAttackInfo info)
    {
        if (comboDepth > 0) return ProcSource.Combo;
        if (info == null) return ProcSource.Card;                       // DOUBLE ATTACK の追加の1撃など
        if (info.elementProc) return info.name.StartsWith("FinalEvo") ? ProcSource.FinalEvolution : ProcSource.Card;
        return ProcSource.Normal;
    }

    static bool SourceOk(ComboSystem.Active a, ProcSource src)
    {
        switch (src)
        {
            case ProcSource.Normal: return a.def.canTriggerFromNormal;
            case ProcSource.Card: return a.def.canTriggerFromCard;
            case ProcSource.FinalEvolution: return a.def.canTriggerFromFinalEvolution;
            default: return false; // COMBO / ULTIMATE からは出さない
        }
    }

    static ComboSystem.Active Combo(ComboTuning.Module m) => ComboSystem.Instance != null ? ComboSystem.Instance.Get(m) : null;
    static float CPower(ComboSystem.Active a) => ProcBase * a.def.power * a.factor * (a.enhanced ? a.def.enhancedPowerMul : 1f);
    static float CRadius(ComboSystem.Active a) => a.def.radius * (a.enhanced ? a.def.enhancedRadiusMul : 1f);
    static Color CCol(ComboSystem.Active a, float alpha = 0.9f) { var c = a.def.color; if (a.awakened) c = Color.Lerp(c, new Color(1f, 0.85f, 0.35f), 0.35f); c.a = alpha; return c; }
    static void Count(ComboSystem.Active a) { a.procs++; ComboSystem.Procs++; ComboHits++; ComboProcsById[a.def.id] = ComboProcsById.TryGetValue(a.def.id, out int n) ? n + 1 : 1; }
    static bool Authoritative => !(NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority);

    // COMBO の効果を実行する間だけ深さを上げる(この間に起きた命中/撃破/属性の事件からは COMBO を出さない)
    static void Run(System.Action body) { comboDepth++; try { body(); } finally { comboDepth--; } }

    // ===================================================================== 命中(CardProcs.OnPlayerHit の最初)
    static void ComboOnHit(Component victim, PlayerAttackInfo info, Collider2D attack, int damage)
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0 || !Authoritative) return;
        var pc = PlayerController.Instance; var gm = GM;
        if (pc == null || gm == null) return;
        var src = SourceOf(info);
        Vector3 at = CenterOf(victim);
        float dir = pc.FacingSign;
        bool boss = IsBoss(victim);

        // 風: 風刃の命中(風刃はカードの効果なので、風の COMBO はここだけ「カードの攻撃」から出る)
        if (info != null && info.windBlade)
        {
            var wb = Combo(ComboTuning.Module.WindBurst);
            if (wb != null && Ready("combo_windburst", wb.def.cooldown))
                Run(() => { Area(at, CRadius(wb), CPower(wb), null, ElementType.Wind, CCol(wb, 0.7f), ref ComboHits); Count(wb); });
            var wt = Combo(ComboTuning.Module.WindTornado);
            if (wt != null)
            {
                wt.counter++;
                int cap = 2 + (wt.enhanced ? Mathf.Max(1, wt.def.enhancedExtra) : 0);
                if (wt.counter >= Mathf.Max(1, wt.def.count) && comboTornadoAlive < cap && Ready("combo_tornado", wt.def.cooldown))
                {
                    wt.counter = 0;
                    Count(wt);
                    gm.StartCoroutine(ComboTornado(at, wt));
                }
            }
            return;
        }

        // 炎: 炎上中の敵への命中で小さな爆発(同じ敵には間隔)
        var be = Combo(ComboTuning.Module.BurningBlast);
        if (be != null && SourceOk(be, src))
        {
            var st = victim.GetComponent<ElementStatus>();
            if (st != null && st.Burning && TargetReady(victim, be.def.cooldown))
            {
                Run(() =>
                {
                    Area(at, CRadius(be), CPower(be), null, ElementType.Fire, CCol(be), ref ComboHits);
                    if (be.enhanced) // 最終進化中: 爆発でも炎上させる(COMBO の炎上 = WILDFIRE は出ない)
                    {
                        var e = gm.Elements;
                        areaBuf.Clear(); CollectTargets(at, CRadius(be), areaBuf);
                        foreach (var c in areaBuf) if (ElementSystem.IsAlive(c)) { var s2 = ElementStatus.For(c); s2.AddBurn(Mathf.Max(e.BurnDps, 4f * ElementSystem.DamageScale) * 0.5f, 2f); s2.ComboBurned = true; }
                    }
                    Count(be);
                });
            }
        }

        // 氷: 凍った雑魚を締め/叩きつけで砕く(ボスは砕けない)
        var sh = Combo(ComboTuning.Module.Shatter);
        if (sh != null && SourceOk(sh, src) && !boss && info != null && (info.seqTag == AttackSeqTag.Finisher || info.kind == PlayerAttackKind.DownImpact))
        {
            var st = victim.GetComponent<ElementStatus>();
            if (st != null && st.Frozen && Ready("combo_shatter", sh.def.cooldown))
                Run(() => { Area(at, CRadius(sh), CPower(sh), null, ElementType.Ice, CCol(sh), ref ComboHits); Count(sh); });
        }

        // 速度: 一定の速さ以上で、主攻撃の命中から前方へ衝撃波
        var sm = Combo(ComboTuning.Module.SonicWave);
        if (sm != null && SourceOk(sm, src) && pc.CurrentRunKmh >= sm.def.count && Ready("combo_sonic", sm.def.cooldown))
        {
            float len = CRadius(sm) * (sm.enhanced ? 1.3f : 1f);
            Run(() => { Box(pc.transform.position + new Vector3(dir * (1.0f + len * 0.5f), 0.9f, 0f), new Vector2(len, 1.4f), CPower(sm), null, CCol(sm), ref ComboHits); Count(sm); });
        }

        // 空中: 着地せずに N 回当てるごとに追撃(着地で数え直し = ComboTick)
        var sa = Combo(ComboTuning.Module.AirAssault);
        if (sa != null && SourceOk(sa, src) && !pc.IsGrounded)
        {
            sa.counter++;
            int need = Mathf.Max(1, sa.def.count - (sa.enhanced ? sa.def.enhancedExtra : 0));
            if (sa.counter >= need && Ready("combo_air", sa.def.cooldown))
            {
                sa.counter = 0;
                Run(() => { Area(at, CRadius(sa), CPower(sa), null, ElementType.None, CCol(sa), ref ComboHits); Count(sa); });
            }
        }

        // 締め: 締めの一撃でさらに前方へ衝撃波(初撃には出ない。1回の技に1回)
        var fw = Combo(ComboTuning.Module.FinisherWave);
        if (fw != null && SourceOk(fw, src) && info != null && info.seqTag == AttackSeqTag.Finisher && info.seqMoveId != lastFinisherWaveMove)
        {
            lastFinisherWaveMove = info.seqMoveId;
            float len = CRadius(fw);
            Run(() => { Box(pc.transform.position + new Vector3(dir * (1.8f + len * 0.5f), 0.9f, 0f), new Vector2(len, 2.2f), CPower(fw), null, CCol(fw), ref ComboHits); Count(fw); });
        }

        // リスク: HP50%以下の間、主攻撃の命中から確率で深紅の斬撃(封印/低HPのリスクはそのまま)
        var dw = Combo(ComboTuning.Module.DeathWish);
        if (dw != null && SourceOk(dw, src) && gm.maxLives > 0 && gm.Lives * 100 <= gm.maxLives * dw.def.count && Ready("combo_deathwish", dw.def.cooldown)
            && ElementSystem.Roll() < Mathf.Min(0.9f, dw.def.procChance + (dw.enhanced ? 0.15f : 0f)))
        {
            float len = CRadius(dw);
            Run(() => { Box(at + new Vector3(dir * len * 0.3f, 0f, 0f), new Vector2(len, 1.6f), CPower(dw), null, CCol(dw), ref ComboHits); Count(dw); });
        }
    }

    static bool TargetReady(Component c, float cd)
    {
        float now = Time.time;
        if (comboTargetCd.TryGetValue(c, out float t) && now - t < cd) return false;
        if (comboTargetCd.Count > 96) comboTargetCd.Clear();
        comboTargetCd[c] = now;
        return true;
    }

    static IEnumerator ComboTornado(Vector3 at, ComboSystem.Active a)
    {
        comboTornadoAlive++;
        try
        {
            float radius = CRadius(a);
            for (int i = 0; i < 3; i++)
            {
                if (!TakeProc()) yield break;
                if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at + Vector3.up * 0.5f, CCol(a, 0.7f), 0.3f, radius * 0.6f, radius, -1f, 0f, Vector3.up * 0.8f, 180f);
                areaBuf.Clear();
                CollectTargets(at + Vector3.up * 0.6f, radius, areaBuf);
                float dmg = CPower(a);
                comboDepth++;
                try { foreach (var c in areaBuf) ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Wind); }
                finally { comboDepth--; }
                ComboHits++;
                yield return new WaitForSeconds(0.4f);
            }
        }
        finally { comboTornadoAlive--; }
    }

    // 風刃の貫通の追加(ElementSystem.SpawnWindBlade)
    public static int ComboWindPierceBonus
    {
        get
        {
            var a = ComboSystem.Instance != null && ComboSystem.Enabled ? ComboSystem.Instance.Get(ComboTuning.Module.WindBurst) : null;
            return a == null ? 0 : a.enhanced ? 1 + Mathf.Max(1, a.def.enhancedExtra) : 1;
        }
    }

    // ===================================================================== 撃破(CardProcs.OnEnemyKilled)
    static void ComboOnKill(EnemyController e, ElementStatus st)
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0 || !Authoritative || e == null) return;
        var wf = Combo(ComboTuning.Module.Wildfire);
        if (wf == null || st == null || !st.BurningOrDiedBurning || st.ComboBurned || !Ready("combo_wildfire", wf.def.cooldown)) return;
        Vector3 at = CenterOf(e);
        var el = GM.Elements;
        int n = Mathf.Max(1, wf.def.count + (wf.enhanced ? wf.def.enhancedExtra : 0));
        float dps = Mathf.Max(el.BurnDps, 4f * ElementSystem.DamageScale) * (1f + 0.6f * wf.factor) * (wf.enhanced ? wf.def.enhancedPowerMul : 1f);
        float dur = Mathf.Max(3f, el.BurnDuration);
        Run(() =>
        {
            areaBuf.Clear();
            CollectTargets(at, CRadius(wf), areaBuf);
            areaBuf.Sort((a2, b2) => (CenterOf(a2) - at).sqrMagnitude.CompareTo((CenterOf(b2) - at).sqrMagnitude));
            int k = 0;
            foreach (var c in areaBuf)
            {
                if (k >= n || c == e || !ElementSystem.IsAlive(c) || !TakeProc()) continue;
                var s2 = ElementStatus.For(c);
                s2.AddBurn(dps, dur);
                s2.ComboBurned = true; // この炎上で倒れても WILDFIRE は出ない(無限に燃え移らない)
                if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, CenterOf(c), CCol(wf, 0.8f), 0.25f, 0.4f, 1.2f, -1f, 0f);
                k++;
            }
            if (k > 0) { Count(wf); if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, CCol(wf), 0.3f, 0.6f, CRadius(wf) * 0.8f, -1f, 0f); }
        });
    }

    // ===================================================================== 属性(ElementSystem.OnPlayerHit から)
    // 凍結(ボスは強い減速)の瞬間
    public static void ComboOnFreeze(Component victim, PlayerAttackInfo info)
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0 || !Authoritative || victim == null) return;
        var fp = Combo(ComboTuning.Module.FreezeNova);
        if (fp == null || !SourceOk(fp, SourceOf(info)) || !Ready("combo_freezenova", fp.def.cooldown)) return;
        Vector3 at = CenterOf(victim);
        var el = GM.Elements;
        Run(() =>
        {
            Area(at, CRadius(fp), CPower(fp), victim, ElementType.Ice, CCol(fp), ref ComboHits);
            areaBuf.Clear();
            CollectTargets(at, CRadius(fp), areaBuf);
            foreach (var c in areaBuf)
                if (c != victim && ElementSystem.IsAlive(c))
                    ElementStatus.For(c).AddChill(Mathf.Max(0.25f, el.ChillSlow), el.ChillDuration * (fp.enhanced ? 1.3f : 1f), el.FreezeStacks, el.FreezeDuration, 0);
            Count(fp);
        });
    }

    // 落雷(連鎖の最後の相手まで決まった後)
    public static void ComboOnLightning(Component first, Component last, int damage, PlayerAttackInfo info)
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0 || !Authoritative || first == null) return;
        var src = SourceOf(info);
        var tc = Combo(ComboTuning.Module.ExtraChain);
        var sl = Combo(ComboTuning.Module.ChainBurst);
        Run(() =>
        {
            if (tc != null && SourceOk(tc, src) && ElementSystem.Roll() < (tc.enhanced ? 1f : tc.def.procChance))
            {
                // 近くの別の敵へもう1本(同じ敵へは 0.4 秒に1回 = 既存の落雷と同じ)
                Component best = null; float bd = CRadius(tc) * CRadius(tc);
                Vector3 from = CenterOf(last != null ? last : first);
                areaBuf.Clear();
                CollectTargets(from, CRadius(tc), areaBuf);
                foreach (var c in areaBuf)
                {
                    if (c == first || c == last || !ElementSystem.IsAlive(c)) continue;
                    if (comboStrikeCd.TryGetValue(c, out float t) && Time.time - t < 0.4f) continue;
                    float d = (CenterOf(c) - from).sqrMagnitude;
                    if (d <= bd) { bd = d; best = c; }
                }
                if (best != null && TakeProc())
                {
                    if (comboStrikeCd.Count > 96) comboStrikeCd.Clear();
                    comboStrikeCd[best] = Time.time;
                    ElementSystem.ComboStrike(from, best, Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Clamp(tc.def.power * tc.factor, 0.2f, 1f))));
                    Count(tc);
                    if (tc.enhanced) Area(CenterOf(best), 1.2f, CPower(tc) * 0.5f, null, ElementType.Thunder, CCol(tc, 0.8f), ref ComboHits); // 最終進化中: 終点で小さな炸裂
                }
            }
            if (sl != null && SourceOk(sl, src) && last != null && last != first && Ready("combo_chainburst", sl.def.cooldown))
            {
                Area(CenterOf(last), CRadius(sl), CPower(sl), null, ElementType.Thunder, CCol(sl), ref ComboHits); // 炸裂(連鎖はしない: DealQuiet)
                Count(sl);
            }
        });
    }

    // ===================================================================== 血(満タンで溢れた回復 → Blood Aegis / 被弾で消費)
    public static void ComboOnOverheal()
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0) return;
        var ba = Combo(ComboTuning.Module.BloodAegis);
        if (ba == null) return;
        int cap = Mathf.Max(1, ba.def.count) + (ba.enhanced ? Mathf.Max(1, ba.def.enhancedExtra) : 0); // 上限(VAMPIRE/OVERHEAL の最終進化中だけ+1)
        if (ba.counter >= cap || !Ready("combo_bloodaegis", ba.def.cooldown)) return;
        ba.counter++;
        Count(ba);
        var pc = PlayerController.Instance;
        if (pc != null && TakeFx()) OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), pc.transform.position + Vector3.up * 0.9f, CCol(ba, 0.8f), 0.3f, 0.6f, 2.0f, -1f, 0f);
    }
    public static int BloodAegisCharges { get { var a = ComboSystem.Instance != null ? ComboSystem.Instance.Get(ComboTuning.Module.BloodAegis) : null; return a != null ? a.counter : 0; } }
    // PlayerController.TakeDamage: true = この被弾は受けない(上限を超えて溜まらない。FE が終わって上限が下がれば余りは消える = ComboTick)
    public static bool ComboInterceptDamage(string source)
    {
        var a = ComboSystem.Enabled && ComboSystem.Instance != null ? ComboSystem.Instance.Get(ComboTuning.Module.BloodAegis) : null;
        if (a == null || a.counter <= 0) return false;
        a.counter--;
        var pc = PlayerController.Instance;
        if (pc != null) OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), pc.transform.position + Vector3.up * 0.9f, CCol(a, 0.9f), 0.3f, 0.7f, 2.4f, -1f, 0f);
        return true;
    }

    // ===================================================================== Shield(CardProcs.OnShieldBlock)
    static void ComboOnShieldBlock(PlayerController pc)
    {
        if (!ComboSystem.Enabled || ComboSystem.ActiveCount == 0 || comboDepth > 0 || pc == null) return;
        var ac = Combo(ComboTuning.Module.AegisBreak);
        if (ac == null || pc.ShieldCharges > 0 || !Ready("combo_aegisbreak", ac.def.cooldown)) return; // 最後の Shield が割れた時だけ(1回の消費で1回)
        Vector3 at = pc.transform.position + Vector3.up * 0.8f;
        Run(() => { Area(at, CRadius(ac), CPower(ac), null, ElementType.None, CCol(ac), ref ComboHits); Count(ac); });
        BossBattleHud.Banner("AEGIS COUNTER", CCol(ac, 1f), 0.8f);
    }

    // ===================================================================== 毎フレーム(ComboSystem.Update)
    public static void ComboTick(ComboSystem sys)
    {
        var pc = PlayerController.Instance;
        if (pc == null || !ComboSystem.Enabled) return;
        // 空中の数え: 着地で数え直し
        var sa = sys.Get(ComboTuning.Module.AirAssault);
        if (sa != null && pc.IsGrounded) sa.counter = 0;
        // Blood Aegis: 上限が下がった(最終進化が終わった)ら余りは消える(永久にしない)
        var ba = sys.Get(ComboTuning.Module.BloodAegis);
        if (ba != null) { int cap = Mathf.Max(1, ba.def.count) + (ba.enhanced ? Mathf.Max(1, ba.def.enhancedExtra) : 0); if (ba.counter > cap) ba.counter = cap; }
        // REDLINE: OVERDRIVE を続けている間、一定の間隔でまわりへ衝撃(実速度は変えない)
        var rl = sys.Get(ComboTuning.Module.Redline);
        if (rl == null || !pc.OverdriveActive || !Authoritative) { redlineFor = 0f; return; }
        redlineFor += Time.deltaTime;
        if (redlineFor >= rl.def.count && Ready("combo_redline", rl.def.cooldown))
        {
            Vector3 at = pc.transform.position + Vector3.up * 0.8f;
            Run(() => { Area(at, CRadius(rl), CPower(rl), null, ElementType.None, CCol(rl), ref ComboHits); Count(rl); });
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 自動テスト: 再帰の確認(COMBO の中から命中の処理を呼んでも COMBO は出ない)
    public static void DebugHitInsideCombo(Component victim, PlayerAttackInfo info) { comboDepth++; try { OnPlayerHit(victim, info, null, 10); } finally { comboDepth--; } }
#endif
}

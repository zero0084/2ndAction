using System.Collections.Generic;
using UnityEngine;

// カード拡張(2026-10-31、属性 12 枚)の処理。どれも HOST(属性の判定をする側)だけで動き、JOIN へは既存の知らせ(NetElementFx)で見た目だけ。
//  炎: SCORCH(炎上中の敵への攻撃力)/ FIRE WHIRL(炎上が近くへ移る・風刃が炎を運ぶ)/ COMBUSTION(4回重ねると爆発)
//  氷: PERMAFROST(重なるほど遅く)/ GLACIER GUARD(凍結で Shield 回復)/ SHATTER STRIKE(凍結中を砕く追撃・ボスは BREAK)
//  雷: CONDUCTOR(帯電中への攻撃力・冷えた敵への落雷)/ VOLT SURGE(1回の攻撃の命中3回ごとに落雷)/ JUDGMENT BOLT(溜めて大落雷)
//  風: SLIPSTREAM(速さで風刃が強く)/ UPDRAFT(空中の敵への攻撃力・空中の追撃)/ TEMPEST(風刃が連続で飛ぶ)
// 高速走行の負荷: 追加の攻撃は「1 体ごとの間隔」と「全体の 1 秒あたりの上限」を持つ(粒は ElementVfx のプールの上限のまま)。
// ボス: 永久に止めない(凍結はボスでは強い減速のまま)。BREAK の追加は既存の AddStagger を通る(BREAK 直後は溜まりにくいまま)。
public static partial class ElementSystem
{
    // ---- 数値(CardBehaviorText の表示と同じ式。Lv は 1〜9、FINAL EVOLUTION で一時的に 9 を超える)
    public static float ScorchPct(int lv) => lv <= 0 ? 0f : 0.02f + 0.04f * lv;                 // 6% → 38%
    public static float FireWhirlChance(int lv) => Mathf.Min(0.75f, 0.12f + 0.03f * lv);       // 15% → 39%(1秒ごと)
    public static float FireWhirlRange(int lv) => Mathf.Min(2.6f, 1.55f + 0.05f * lv);         // 1.6 → 2.0m
    public static float FireWhirlWindRadius(int lv) => Mathf.Min(3.2f, 1.5f + 0.1f * lv);      // 1.6 → 2.4m
    public static float CombustionPower(int lv) => 14f + 4f * lv;                               // 18 → 50
    public static float CombustionRadius(int lv) => Mathf.Min(3.8f, 2.2f + 0.1f * lv);         // 2.3 → 3.1m
    public const int CombustionStacks = 4;
    public static float PermafrostPerStack(int lv) => Mathf.Min(0.1f, 0.03f + 0.005f * lv);    // 1重ごと 3.5% → 7.5%(最大3重分)
    public static float GlacierGuardInterval(int lv) => Mathf.Max(6f, 20f - lv);               // 19 → 11秒
    public static float ShatterStrikePower(int lv) => 24f + 6f * lv;                            // 30 → 78
    public static float ShatterStrikeBreak(int lv) => Mathf.Min(0.2f, 0.05f + 0.01f * lv);     // BREAK ゲージの 6% → 14%
    public static float ConductorPct(int lv) => lv <= 0 ? 0f : 0.02f + 0.04f * lv;              // 6% → 38%
    public static float ConductorChillBonus(int lv) => 0.04f + 0.04f * lv;                      // 8% → 40%
    public const float ConductorChainRange = 1f, ConductorChargedSeconds = 2f;
    public static float VoltSurgeMul(int lv) => 0.6f + 0.05f * lv;                               // 65% → 105%
    public static int JudgmentNeed(int lv) => Mathf.Max(4, 10 - (lv - 1) / 2);                  // 10 → 6回
    public static float JudgmentMul(int lv) => 2.0f + 0.2f * lv;                                // ×2.2 → ×3.8(2026-10-31 試験: ×2.75〜4.75 はボスへの火力が通常の約3倍で強すぎた)
    public static float JudgmentBreak(int lv) => Mathf.Min(0.12f, 0.035f + 0.005f * lv);       // 4% → 8%
    public const float JudgmentInterval = 4f, JudgmentRadius = 2.2f, JudgmentShock = 0.4f;
    public static float SlipstreamDmg(int lv) => 0.10f + 0.04f * lv;                            // 最大 +14% → +46%
    public static float SlipstreamChance(int lv) => 0.01f + 0.005f * lv;                        // 最大 +1.5% → +5.5%
    public static float SlipstreamFactor(float kmh) => Mathf.Clamp01((kmh - 40f) / 80f);       // 40km/h から伸びて 120km/h で最大
    public static float UpdraftPct(int lv) => lv <= 0 ? 0f : 0.02f + 0.04f * lv;             // 6% → 38%
    public static float UpdraftPower(int lv) => 6f + 1.5f * lv;                              // 7.5 → 19.5
    public static int TempestExtra(int lv) => lv <= 0 ? 0 : lv >= 7 ? 2 : 1;                     // 2本 / 3本(Lv7〜)
    // 2026-10-31 試験: 風刃は 0.25 秒ごとに出るので、続けて飛ぶ風刃もその度に出るとボス1体へ集中した(Cyclops を5秒で倒した)→ 1秒に1回まで
    public const float TempestInterval = 1f;
    public const float TempestBossMul = 0.5f; // 続けて飛ぶ風刃はボスへ半分(竜巻と同じ)
    static float lastTempest = -9f;
    public static float TempestMul(int lv) => Mathf.Min(1f, 0.27f + 0.03f * lv);                // 後の風刃 30% → 54%(2026-10-31 試験: 55〜95% は風の完成形でボスへの火力が通常の約7倍だった)

    // 全体の上限(1秒あたり)
    const int SpreadPerSecond = 8, CombustPerSecond = 5, ShatterPerSecond = 6;
    public const int ShatterChillStacks = 3, GuardChillStacks = 3; // SHATTER STRIKE / GLACIER GUARD は凍結か冷え3回で
    const float PerTargetInterval = 0.4f, BossShatterInterval = 8f, CombustInterval = 4f, BossCombustInterval = 6f;

    // 確認用の数
    public static int ExpSpreads, ExpWindSpreads, ExpCombustions, ExpGuardRestores, ExpShatters, ExpBossShatters,
        ExpVoltSurgeBolts, ExpJudgments, ExpSkyPursuits, ExpTempestBlades, ExpCapped;
    public static void ResetExpansionCounters()
    {
        ExpSpreads = ExpWindSpreads = ExpCombustions = ExpGuardRestores = ExpShatters = ExpBossShatters = 0;
        ExpVoltSurgeBolts = ExpJudgments = ExpSkyPursuits = ExpTempestBlades = ExpCapped = 0;
        judgmentCharge = 0; lastJudgment = -99f; lastGuardRestore = -99f; swingHits.Clear(); lastPursuit.Clear(); lastSurge.Clear(); lastTempest = -9f;
    }
    public static string ExpansionReport() =>
        $"spread {ExpSpreads}/{ExpWindSpreads}w combust {ExpCombustions} guard {ExpGuardRestores} shatterStrike {ExpShatters}/{ExpBossShatters}b voltSurge {ExpVoltSurgeBolts} judgment {ExpJudgments} sky {ExpSkyPursuits} tempest {ExpTempestBlades} capped {ExpCapped}";

    public static int ExpLv(EffectType t)
    {
        var gm = GameManager.Instance;
        return gm == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(gm.Elements.Get(t)), 0, 18);
    }

    static float budgetAt = -9f;
    static int spreadUsed, combustUsed, shatterUsed;
    static bool Budget(ref int used, int max)
    {
        if (Time.time - budgetAt >= 1f) { budgetAt = Time.time; spreadUsed = combustUsed = shatterUsed = 0; }
        if (used >= max) { ExpCapped++; return false; }
        used++;
        return true;
    }

    static readonly List<Component> expBuf = new List<Component>();
    static float ExpDamage(float b) => b * DamageScale * HitWeight;

    // ---------------------------------------------------------------- 炎
    // 炎上を付けた直後(OnPlayerHit)。COMBUSTION: 炎上中の敵に 4 回重ねたら爆発
    static void ExpOnBurn(Component victim, ElementStatus st, ElementStats e)
    {
        int lv = ExpLv(EffectType.CombustionLevel);
        if (lv <= 0 || st == null || st.BurnStacks < CombustionStacks) return;
        float interval = st.IsBoss ? BossCombustInterval : CombustInterval;
        if (Time.time - st.LastCombust < interval || !Budget(ref combustUsed, CombustPerSecond)) return;
        st.LastCombust = Time.time;
        ExpCombustions++;
        Vector3 c = CenterOf(victim);
        int dmg = Mathf.Max(1, Mathf.RoundToInt(ExpDamage(CombustionPower(lv))));
        float r = CombustionRadius(lv);
        st.EndBurn();
        DealQuiet(victim, dmg, ElementType.Fire);
        expBuf.Clear();
        CardProcs.CollectTargets(c, r, expBuf);
        foreach (var o in expBuf)
        {
            if (o == victim || !IsAlive(o)) continue;
            DealQuiet(o, dmg, ElementType.Fire);
            if (IsAlive(o)) { ElementStatus.For(o).AddBurn(e.BurnDps, e.BurnDuration); ElementVfx.BurnApplied(o); }
        }
        ElementVfx.Shatter(c, r * 0.8f); // 爆ぜる光(プールの粒。色は炎の印を出す BurnApplied で補う)
        ElementVfx.BurnApplied(victim);
        NetElementFx.Point(NetElementFx.Kind.Inferno, c, r);
    }

    // FIRE WHIRL: 炎上中の敵(ElementStatus の 0.5 秒ごと)から、近くの燃えていない敵へ移る
    public static void FireWhirlTick(ElementStatus src)
    {
        if (!Authoritative || src == null || !src.Burning) return;
        int lv = ExpLv(EffectType.FireWhirlLevel);
        if (lv <= 0) return;
        if (Roll() >= FireWhirlChance(lv) * ElementStatus.TickInterval) return; // 1 秒あたりの確率
        Component self = src.Target;
        if (self == null) return;
        expBuf.Clear();
        CardProcs.CollectTargets(CenterOf(self), FireWhirlRange(lv), expBuf);
        Component best = null; float bd = float.MaxValue;
        foreach (var o in expBuf)
        {
            if (o == self || !IsAlive(o)) continue;
            var st = o.GetComponent<ElementStatus>();
            if (st != null && st.Burning) continue;
            float d = (CenterOf(o) - CenterOf(self)).sqrMagnitude;
            if (d < bd) { bd = d; best = o; }
        }
        if (best == null || !Budget(ref spreadUsed, SpreadPerSecond)) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        var e = gm.Elements;
        ElementStatus.For(best).AddBurn(Mathf.Max(e.BurnDps, src.BurnDps), e.BurnDuration);
        ElementVfx.BurnApplied(best);
        ExpSpreads++;
    }

    // FIRE WHIRL(炎 × 風): 風刃が炎上中の敵に当たると、周りの敵へ炎を運ぶ
    static void ExpOnWindHit(Component victim, ElementStats e)
    {
        int lv = ExpLv(EffectType.FireWhirlLevel);
        if (lv <= 0 || victim == null) return;
        var st = victim.GetComponent<ElementStatus>();
        if (st == null || !st.Burning) return;
        expBuf.Clear();
        CardProcs.CollectTargets(CenterOf(victim), FireWhirlWindRadius(lv), expBuf);
        float dps = Mathf.Max(e.BurnDps, st.BurnDps);
        foreach (var o in expBuf)
        {
            if (o == victim || !IsAlive(o)) continue;
            var os = ElementStatus.For(o);
            if (os.Burning || !Budget(ref spreadUsed, SpreadPerSecond)) continue;
            os.AddBurn(dps, Mathf.Max(e.BurnDuration, ElementStats.DefaultBurnDuration));
            ElementVfx.BurnApplied(o);
            ExpWindSpreads++;
        }
    }

    // ---------------------------------------------------------------- 氷
    static float lastGuardRestore = -99f;
    // GLACIER GUARD: 敵を凍結させたか冷えを3回重ねたら Shield を 1 つ回復(間隔あり。最大数は増やさない)
    static void ExpOnFreeze(Component victim)
    {
        int lv = ExpLv(EffectType.GlacierGuardLevel);
        var pc = PlayerController.Instance;
        if (lv <= 0 || pc == null || pc.IsAlly) return;
        if (Time.time - lastGuardRestore < GlacierGuardInterval(lv) || pc.ShieldCharges >= pc.ShieldCapacity) return;
        lastGuardRestore = Time.time;
        pc.AddShieldCharges(1);
        ExpGuardRestores++;
        ElementVfx.ChillApplied(pc, false); // プレイヤーの周りに冷気(Shield が戻った合図)
    }

    // SHATTER STRIKE(直接の命中の前に呼ぶ): 凍結中の雑魚を砕く追撃(周りに半分)。ボスは凍結(強い減速)中に当てると砕け、BREAK が溜まる
    static void ExpShatterStrike(Component victim)
    {
        int lv = ExpLv(EffectType.ShatterStrikeLevel);
        if (lv <= 0 || victim == null) return;
        var st = victim.GetComponent<ElementStatus>();
        if (st == null || !(st.Frozen || st.ChillStacks >= ShatterChillStacks)) return; // 凍結中、または冷えが3回以上(単独でも起きるように)
        if (st.IsBoss && Time.time - st.LastShatter < BossShatterInterval) return;
        if (!Budget(ref shatterUsed, ShatterPerSecond)) return;
        st.LastShatter = Time.time;
        st.EndFreeze(); // 凍結と冷えの重なりを使い切る(続けて砕けない)
        Vector3 c = CenterOf(victim);
        int dmg = Mathf.Max(1, Mathf.RoundToInt(ExpDamage(ShatterStrikePower(lv)) * (st.IsBoss ? 1.5f : 1f)));
        DealQuiet(victim, dmg, ElementType.Ice);
        ElementVfx.Shatter(c, ElementVfx.SizeOf(victim, out _) * 1.3f);
        NetElementFx.Point(NetElementFx.Kind.Shatter, c, ElementVfx.SizeOf(victim, out _) * 1.3f);
        if (st.IsBoss)
        {
            ExpBossShatters++;
            AddCardStagger(victim, ShatterStrikeBreak(lv));
            return;
        }
        ExpShatters++;
        expBuf.Clear();
        CardProcs.CollectTargets(c, 1.5f, expBuf);
        foreach (var o in expBuf) if (o != victim && IsAlive(o)) DealQuiet(o, Mathf.Max(1, dmg / 2), ElementType.Ice);
    }

    // ボスの BREAK を「ゲージの割合」で足す(既存の AddStagger を通る = BREAK 直後は溜まりにくい・冷えていれば少し多い)
    public static void AddCardStagger(Component boss, float fraction)
    {
        switch (boss)
        {
            case WildBossBase b: b.AddCardStagger(fraction); break;
            case DragonController d: d.AddCardStagger(fraction); break;
            case MajinController m: m.AddCardStagger(fraction); break;
        }
    }

    // ---------------------------------------------------------------- 雷
    // CONDUCTOR: 冷え・凍結中の敵への落雷を強く(Strike の中で)
    static int ConductorAdjust(Component c, int dmg)
    {
        int lv = ExpLv(EffectType.ConductorLevel);
        if (lv <= 0 || c == null) return dmg;
        var st = c.GetComponent<ElementStatus>();
        if (st != null && (st.Chilled || st.Frozen)) return Mathf.Max(1, Mathf.RoundToInt(dmg * (1f + ConductorChillBonus(lv))));
        return dmg;
    }
    static bool ConductorReaches(Component c) => ExpLv(EffectType.ConductorLevel) > 0 && c != null && c.GetComponent<ElementStatus>() is ElementStatus st && (st.Chilled || st.Frozen);

    // VOLT SURGE: 1 回の攻撃(同じ振り/発射)の命中 3 回ごとに追加の落雷(最大 3 本)
    static readonly Dictionary<int, int> swingHits = new Dictionary<int, int>();
    static readonly Dictionary<Component, float> lastSurge = new Dictionary<Component, float>();
    static void ExpVoltSurge(Component victim, PlayerAttackInfo info, ElementStats e)
    {
        int lv = ExpLv(EffectType.VoltSurgeLevel);
        if (lv <= 0 || info == null || e.LightningPower <= 0f) return;
        if (swingHits.Count > 64) swingHits.Clear();
        swingHits.TryGetValue(info.SwingId, out int n);
        n++;
        swingHits[info.SwingId] = n;
        if (n % 3 != 0 || n > 9) return;
        // 同じ敵へは 0.4 秒に 1 回(通常の落雷の間隔とは別に数える: 同じ命中で通常の落雷が出ると LightningReady が埋まり、一度も出なかった)
        if (lastSurge.TryGetValue(victim, out float st0) && Time.time - st0 < PerTargetInterval) return;
        if (lastSurge.Count > 64) lastSurge.Clear();
        lastSurge[victim] = Time.time;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(e.LightningDamage * HitWeight * VoltSurgeMul(lv)));
        ExpVoltSurgeBolts++;
        LightningProcs++;
        ElementTelemetry.Proc(ElementType.Thunder);
        Chain(victim, dmg, 0, e.LightningRange);
    }

    // JUDGMENT BOLT: 落雷(通常の発動)を数え、溜まったら次の落雷を大落雷に。戻り値 = 威力の倍率(1 = 通常)
    static int judgmentCharge;
    static float lastJudgment = -99f;
    static float JudgmentBeforeStrike(out bool judgment)
    {
        judgment = false;
        int lv = ExpLv(EffectType.JudgmentLevel);
        if (lv <= 0) return 1f;
        judgmentCharge++;
        if (judgmentCharge < JudgmentNeed(lv) || Time.time - lastJudgment < JudgmentInterval) return 1f;
        judgmentCharge = 0;
        lastJudgment = Time.time;
        judgment = true;
        ExpJudgments++;
        return JudgmentMul(lv);
    }
    static void JudgmentAfterStrike(Component victim, int dmg)
    {
        int lv = ExpLv(EffectType.JudgmentLevel);
        Vector3 c = CenterOf(victim);
        Bolt(c + Vector3.up * 5f, c);
        Bolt(c + new Vector3(-0.6f, 4.5f, 0f), c);
        expBuf.Clear();
        CardProcs.CollectTargets(c, JudgmentRadius, expBuf);
        foreach (var o in expBuf)
        {
            if (o == victim || !IsAlive(o)) continue;
            DealQuiet(o, Mathf.Max(1, dmg / 2), ElementType.Thunder);
            if (o is EnemyController && IsAlive(o)) ElementStatus.For(o).AddShock(JudgmentShock);
        }
        if (IsAlive(victim) && !(victim is EnemyController)) AddCardStagger(victim, JudgmentBreak(lv));
        ElementVfx.Shatter(c, JudgmentRadius * 0.7f);
        NetElementFx.Point(NetElementFx.Kind.Shatter, c, JudgmentRadius * 0.7f);
    }

    // ---------------------------------------------------------------- 風
    static float SlipstreamNow(int lv, bool chance)
    {
        if (lv <= 0) return 0f;
        var pc = PlayerController.Instance;
        float f = pc != null ? SlipstreamFactor(pc.CurrentRunKmh) : 0f;
        return f * (chance ? SlipstreamChance(lv) : SlipstreamDmg(lv));
    }

    // UPDRAFT: 自分も空中で、空中の敵に当てた時の風の追撃(同じ敵へ 0.4 秒に 1 回)
    static readonly Dictionary<Component, float> lastPursuit = new Dictionary<Component, float>();
    static void ExpSkyPursuit(Component victim)
    {
        int lv = ExpLv(EffectType.UpdraftLevel);
        var pc = PlayerController.Instance;
        if (lv <= 0 || pc == null || pc.IsGrounded || !IsAirTarget(victim)) return;
        if (lastPursuit.TryGetValue(victim, out float t) && Time.time - t < PerTargetInterval) return;
        if (lastPursuit.Count > 64) lastPursuit.Clear();
        lastPursuit[victim] = Time.time;
        ExpSkyPursuits++;
        DealQuiet(victim, Mathf.Max(1, Mathf.RoundToInt(ExpDamage(UpdraftPower(lv)))), ElementType.Wind);
        ElementVfx.WindHit(victim);
        if (IsAlive(victim)) ElementStatus.For(victim).MarkWind();
    }

    // TEMPEST: 風刃の後に続けて飛ぶ風刃(少し間をあけて・少し上下にずらす)
    static System.Collections.IEnumerator TempestFollow(ElementStats e, int extra, float mul)
    {
        for (int k = 1; k <= extra; k++)
        {
            yield return new WaitForSeconds(0.08f);
            if (PlayerController.Instance == null) yield break;
            SpawnWindBladeAt(e, mul, k % 2 == 1 ? 0.35f : -0.25f);
            ExpTempestBlades++;
        }
    }
}

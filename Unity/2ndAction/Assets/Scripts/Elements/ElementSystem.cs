using System.Collections.Generic;
using UnityEngine;

// 属性(2026-10-03)の命中時の処理。PlayerAttackInfo.ScaleDamage(敵/ボスへの命中がすべて通る1か所)から呼ばれる。
//   値は GameManager.Elements(カードで加算)。値が0なら何もしない(既存のカードは今は全部0)。
//   追加のダメージ(継続/落雷)は「静かなダメージ」(のけぞり/音/ヒットストップ/コンボ数なし)で入れ、
//   そのダメージ自身からは属性の判定をしない(無限に連鎖しない)。
//   マルチ: 敵/ボスのHPを持っている端末(シングル/HOST)でだけ判定する。JOINのプレイヤーの命中には、
//   今は属性の効果が乗らない(HOSTへ届くのはダメージの値だけ。今後の課題)。
public static class ElementSystem
{
    // 確認用(CARD BALANCE TEST / 自動テスト): このランで発動した回数と、属性で与えたダメージ
    public static int BurnProcs, ChillProcs, FreezeProcs, LightningProcs, LightningHits, WindBlades, BleedProcs;
    public static long ElementDamage;
    public static int BleedHealed;
    public static void ResetCounters()
    {
        BurnProcs = ChillProcs = FreezeProcs = LightningProcs = LightningHits = WindBlades = BleedProcs = 0;
        ElementDamage = 0; BleedHealed = 0;
    }

    // 判定の乱数(テストで固定できるように分けておく)
    public static System.Func<float> Roll = () => Random.value;

    static bool Authoritative => !(NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority);

    public static bool IsAlive(Component c)
    {
        if (c == null || !c.gameObject.activeInHierarchy) return false;
        switch (c)
        {
            case EnemyController e: return !e.IsDyingOrReplica;
            case WildBossBase b: return !b.IsDead && !b.NetPuppet;
            case DragonController d: return !d.IsDead && !d.NetPuppet;
            case MajinController m: return !m.IsDead && !m.NetPuppet;
        }
        return false;
    }

    // v3: 属性のダメージの倍率 = 1 + 無条件の攻撃の枠(ATTACK UP 等)の半分。条件の枠(空中/締め/ボス特効…)は乗せない
    public static float DamageScale
    {
        get
        {
            var gm = GameManager.Instance;
            float a = gm != null ? gm.Card.Get(EffectType.AttackPct) : 0f;
            return 1f + ElementAttackShareNow * CardRules.SoftAttack(a);
        }
    }

    static float lastWindBlade = -9f;

    // 2026-10-30: 属性のダメージに乗せる攻撃の枠の割合(開発版 -elemAtkShare 0.5|0.75|1.0 で比較)
    static float atkShareOverride = -2f;
    public static float ElementAttackShareNow
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (atkShareOverride < -1f)
            {
                atkShareOverride = -1f;
                var a = QaArgs.All;
                for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-elemAtkShare" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) atkShareOverride = v;
            }
            if (atkShareOverride >= 0f) return atkShareOverride;
#endif
            return CardRules.ElementAttackShare;
        }
    }
    public static void DebugSetAttackShare(float v) => atkShareOverride = v;

    // 2026-10-30: 凍結の砕けの基礎威力(ABSOLUTE ZERO の Lv)
    public static float ShatterBase(int lv) => 12f + 4f * lv;

    // 2026-10-30 氷の役割(BREAK 補助): 冷えた/凍結したボスは BREAK が溜まりやすい。冷え +10%、凍結 +20%、さらに IceBreakPct(凍結で満額、冷えは半分)
    public static int IceStaggerBoosts; public static float IceStaggerBonus; // 確認用
    public static float IceStaggerMul(GameObject boss, float v)
    {
        if (boss == null) return 1f;
        var st = boss.GetComponent<ElementStatus>();
        if (st == null || !(st.Chilled || st.Frozen)) return 1f;
        var gm = GameManager.Instance;
        float ib = gm != null ? gm.Elements.IceBreak : 0f;
        float mul = st.Frozen ? 1.2f + ib : 1.1f + ib * 0.5f;
        IceStaggerBoosts++; IceStaggerBonus += v * (mul - 1f);
        ElementTelemetry.IceStagger += v * (mul - 1f);
        return mul;
    }

    // 2026-10-30: 雷の確定(命中を重ねると必ず)の数え / 命中の吸収の端数
    static int lightningHits;
    public static int ForcedStrikes; // 確認用
    static float hitHealCarry, lastHitHealFx = -9f;

    // v3: 1回で決まる属性のダメージ(落雷/風刃/砕ける冷気)は、キャラの1発の重さに合わせる(20 = 黒剣士の基礎)。
    // 速く軽く何度も当てるキャラ(お嬢様騎士/双剣士)が回数で何倍にもならないように。継続ダメージ(炎上/出血)は掛け直しなので掛けない。
    public static float HitWeight
    {
        get
        {
            var pc = PlayerController.Instance;
            float a = pc != null ? pc.AttackPower : CardRules.ProcReferenceDamage;
            return Mathf.Clamp(a / (float)CardRules.ProcReferenceDamage, 0.5f, 2f);
        }
    }
    // 落雷は同じ相手へ 0.4秒に1回まで
    const float LightningPerTargetInterval = 0.4f;
    static readonly Dictionary<Component, float> lastStrikeOn = new Dictionary<Component, float>();
    static bool LightningReady(Component v)
    {
        if (lastStrikeOn.TryGetValue(v, out float t) && Time.time - t < LightningPerTargetInterval) return false;
        if (lastStrikeOn.Count > 128) lastStrikeOn.Clear();
        lastStrikeOn[v] = Time.time;
        return true;
    }

    // damage は使わない(v3: 属性は専用の基礎ダメージ)。呼び出しの形は互換のため残す
    public static void OnPlayerHit(Component victim, PlayerAttackInfo info, int damage)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.IsGameOver || !Authoritative) return;
        // 計測(2026-10-30): 命中の数と直接のダメージ。風刃(属性の追加攻撃)の命中は風のダメージ、ほかのカードの追加攻撃は Other
        if (info != null && info.windBlade)
        {
            ElementTelemetry.OnDamage(ElementType.Wind, damage, false); ElementVfx.WindHit(Normalize(victim));
            var ws = Normalize(victim);
            if (ElementVfx.New && ws != null && IsAlive(ws)) ElementStatus.For(ws).MarkWind();
            // 2026-10-30: 空中の敵(打ち上げ中/飛ぶ敵/ドラゴン/魔人)には風刃のダメージ+50%(静かなダメージで足す)
            if (ws != null && IsAlive(ws) && IsAirTarget(ws)) { WindAirBonusHits++; DealQuiet(ws, Mathf.Max(1, Mathf.RoundToInt(damage * ElementStats.WindAirTargetBonus)), ElementType.Wind); }
        }
        else if (info != null && info.elementProc) ElementTelemetry.OnDamage(ElementType.None, damage, false);
        else ElementTelemetry.OnHit(damage);
        ElementVfx.LastHitPos = victim != null ? victim.transform.position + Vector3.up * 0.8f : ElementVfx.LastHitPos;
        ElementStats e = gm.Elements;
        // 2026-10-30 血: 命中で与えたダメージの一部を吸収(VAMPIRE)。HP50%以下は BLOOD RUSH で2倍、ボスは PREDATOR で2倍
        if (info != null && !info.elementProc && damage > 0 && e.HitLifesteal > 0f) HitLifesteal(gm, e, Normalize(victim), damage);
        if (!e.AnyActive) return;
        if (info != null && info.elementProc && !info.windBlade) return; // カードの追加攻撃(衝撃波/斬撃)からは属性を判定しない
        victim = Normalize(victim);
        if (victim == null || !IsAlive(victim)) return;

        // 炎: 継続ダメージ(基礎/秒)。重ねると時間を延ばし少しずつ強く
        // FINAL EVOLUTION(FLAME BLADE): 確率と強さを足し、炎上させた相手の周りへ小さく延焼(延焼からは延焼しない)
        float burnChance = e.BurnChance > 0f ? Mathf.Min(1f, e.BurnChance + FinalEvolution.BurnChanceAdd) : 0f;
        if (burnChance > 0f && e.BurnPower > 0f && Roll() < burnChance)
        {
            float bdps = e.BurnDps * FinalEvolution.BurnDpsMul;
            ElementStatus.For(victim).AddBurn(bdps, e.BurnDuration);
            BurnProcs++;
            ElementTelemetry.Proc(ElementType.Fire); ElementTelemetry.StatusOn(ElementType.Fire);
            ElementVfx.BurnApplied(victim);
            FinalEvolution.OnBurnApplied(victim, bdps, e.BurnDuration);
        }
        // 氷: 行動を遅く、重なると凍結(ボスは強めの減速)
        if (e.ChillChance > 0f && e.ChillSlow > 0f && Roll() < e.ChillChance)
        {
            var st = ElementStatus.For(victim);
            bool froze = st.AddChill(e.ChillSlow, e.ChillDuration, e.FreezeStacks, e.FreezeDuration, e.AbsoluteZero);
            ElementTelemetry.Proc(ElementType.Ice); ElementTelemetry.StatusOn(ElementType.Ice);
            ElementVfx.ChillApplied(victim, froze);
            if (froze)
            {
                FreezeProcs++; ElementTelemetry.Freezes++;
                if (e.AbsoluteZero > 0) AbsoluteZeroShatter(victim, e.AbsoluteZero);
                CardProcs.ComboOnFreeze(victim, info); // COMBO(FROZEN PRISON)
            }
            ChillProcs++;
        }
        // 血: 出血(継続ダメージ、低HPほど強い、一部を回復)。BLOOD BLADE: 満HPの間は確率2倍
        float bleedChance = e.BleedChance;
        if (e.BloodBlade && gm.Lives >= gm.maxLives) bleedChance = Mathf.Min(1f, bleedChance * 2f);
        else if (gm.CardLevel("blood_rush") > 0 && gm.Lives <= gm.maxLives * 0.5f) bleedChance = Mathf.Min(1f, bleedChance * 2f); // 2026-10-30: BLOOD RUSH は HP50%以下で2倍
        if (bleedChance > 0f && e.BleedPower > 0f && Roll() < bleedChance)
        {
            float missing = gm.maxLives > 0 ? 1f - (float)gm.Lives / gm.maxLives : 0f;
            float dps = e.BleedPower * DamageScale * (1f + e.BloodLowHpBonus * Mathf.Clamp01(missing));
            ElementStatus.For(victim).AddBleed(dps, e.BleedDuration, e.BleedLifesteal);
            BleedProcs++;
            ElementTelemetry.Proc(ElementType.Blood); ElementTelemetry.StatusOn(ElementType.Blood);
            ElementVfx.BleedApplied(victim);
        }
        // 雷: 落雷(当たった相手)+ 近くの別の敵へ連鎖 + 周りへの範囲
        // FINAL EVOLUTION(THUNDER STRIKE): 落雷しやすく連鎖+1。1体へ0.4秒に1回の制限と、落雷から落雷を出さない決まりはそのまま
        float lChance = e.LightningChance > 0f ? Mathf.Min(1f, e.LightningChance + FinalEvolution.LightningChanceAdd) : 0f;
        // 2026-10-30: 命中を重ねると必ず落ちる(手数の多いキャラほど落ちる)。落雷が起きたら数え直し
        bool forced = false;
        int every = e.LightningEvery;
        if (every > 0 && (info == null || !info.elementProc)) { lightningHits++; forced = lightningHits >= every; }
        if (e.LightningPower > 0f && (forced || (lChance > 0f && Roll() < lChance)) && LightningReady(victim))
        {
            if (forced) ForcedStrikes++;
            lightningHits = 0;
            LightningProcs++;
            ElementTelemetry.Proc(ElementType.Thunder);
            int ldmg = Mathf.Max(1, Mathf.RoundToInt(e.LightningDamage * HitWeight));
            var last = Chain(victim, ldmg, e.LightningChains + FinalEvolution.LightningChainsAdd, e.LightningRange, e.LightningSplash, e.LightningShock);
            CardProcs.ComboOnLightning(victim, last, ldmg, info); // COMBO(THUNDER CHAIN / STORM LORD)
        }
        // 風: 前方へ飛ぶ貫通の風刃(風刃自身からは出さない。短い間隔をあける)
        var wpc = PlayerController.Instance;
        float wChance = e.WindBladeChance * (wpc != null && !wpc.IsGrounded ? ElementStats.WindAirChanceMul : 1f); // 2026-10-30: 空中では出やすい
        if ((info == null || !info.elementProc) && e.WindBladeChance > 0f && e.WindBladePower > 0f && Time.time - lastWindBlade > 0.25f && Roll() < wChance)
        {
            lastWindBlade = Time.time;
            SpawnWindBlade(e);
            WindBlades++;
            ElementTelemetry.Proc(ElementType.Wind);
        }
    }

    public static int Shatters;
    // ABSOLUTE ZERO: 凍結した瞬間に砕ける冷気のダメージ。ボスは凍結の代わりの減速がさらに強くなる(ElementStatus)
    static void AbsoluteZeroShatter(Component victim, int lv)
    {
        Shatters++; ElementTelemetry.Shatters++;
        ElementVfx.Shatter(CenterOf(victim), ElementVfx.SizeOf(victim, out _));
        NetElementFx.Point(NetElementFx.Kind.Shatter, CenterOf(victim), ElementVfx.SizeOf(victim, out _));
        float dmg = ShatterBase(lv) * DamageScale * HitWeight;
        DealQuiet(victim, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Ice);
        if (ElementVfx.Legacy) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, CenterOf(victim), new Color(0.75f, 0.95f, 1f, 0.9f), 0.25f, 0.4f, 1.3f, -1f, 0f);
    }

    public static int WindAirBonusHits; // 確認用
    public static bool IsAirTarget(Component c)
    {
        if (c is DragonController || c is MajinController) return true;
        if (c is EnemyController en) return en.AerialState != EnemyController.EnemyAerialState.Grounded || en.movementType == EnemyMovementType.Flying;
        return false;
    }

    // 命中の吸収(VAMPIRE 等)。端数は持ち越し、HP が1以上になったら回復。演出は0.6秒に1回まで
    static void HitLifesteal(GameManager gm, ElementStats e, Component victim, int damage)
    {
        if (gm.Lives >= gm.maxLives) { hitHealCarry = 0f; return; }
        float pct = e.HitLifesteal;
        if (gm.CardLevel("blood_rush") > 0 && gm.Lives <= gm.maxLives * 0.5f) pct *= 2f;
        bool boss = victim != null && !(victim is EnemyController);
        if (boss && e.Get(EffectType.PredatorLevel) > 0f) pct *= 2f;
        hitHealCarry += damage * pct;
        int heal = Mathf.FloorToInt(hitHealCarry);
        if (heal <= 0) return;
        hitHealCarry -= heal;
        gm.KitHeal(heal);
        ElementTelemetry.HealHp += heal; ElementTelemetry.HitHealHp += heal;
        if (victim != null && Time.time - lastHitHealFx > 0.6f) { lastHitHealFx = Time.time; ElementVfx.BloodReturn(CenterOf(victim), 0); NetElementFx.Blood(victim, true); }
    }

    // 子の当たり判定から呼ばれた時も本体に揃える
    static Component Normalize(Component c)
    {
        if (c is EnemyController || c is WildBossBase || c is DragonController || c is MajinController) return c;
        if (c is BossHurtbox h) return h.owner;
        return (Component)c.GetComponentInParent<EnemyController>() ?? (Component)c.GetComponentInParent<WildBossBase>()
            ?? (Component)c.GetComponentInParent<DragonController>() ?? c.GetComponentInParent<MajinController>();
    }

    static Vector3 CenterOf(Component c)
    {
        var col = c.GetComponentInChildren<Collider2D>();
        return col != null ? col.bounds.center : c.transform.position + Vector3.up;
    }

    // 静かなダメージ(のけぞり/音/ヒットストップ/コンボ数なし)
    public static void DealQuiet(Component victim, int dmg, ElementType element)
    {
        if (victim == null || dmg <= 0 || !IsAlive(victim)) return;
        Vector3 at = CenterOf(victim);
        bool dealt = false;
        switch (victim)
        {
            case EnemyController en: dealt = en.ApplyElementDamage(dmg, at); break;
            case WildBossBase b: b.TakeElementDamage(dmg, at); dealt = true; break;
            case DragonController d: d.TakeDamage(dmg); dealt = true; break;
            case MajinController m: m.TakeDamage(dmg); dealt = true; break;
        }
        if (dealt) { ElementDamage += dmg; ElementTelemetry.OnDamage(element, dmg, !IsAlive(victim)); }
    }

    public static void HealFromBleed(int amount, Component from = null)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0) return;
        gm.KitHeal(amount);
        BleedHealed += amount;
        ElementTelemetry.BleedHealHp += amount; ElementTelemetry.HealHp += amount;
        if (from != null && ElementVfx.New) ElementVfx.BloodReturn(CenterOf(from), 0); // 敵からプレイヤーへ戻る(2026-10-30)
        if (from != null) NetElementFx.Blood(from, false);
    }

    static readonly List<Component> chainBuf = new List<Component>();
    static readonly List<Component> splashBuf = new List<Component>();
    static void Strike(Component c, int dmg, float splash, float shock)
    {
        DealQuiet(c, dmg, ElementType.Thunder);
        LightningHits++;
        if (ElementVfx.New && IsAlive(c)) { ElementStatus.For(c).MarkCharged(); }
        ElementVfx.StrikeImpact(c, ElementVfx.Tier(ElementType.Thunder));
        if (shock > 0f && c is EnemyController && IsAlive(c)) ElementStatus.For(c).AddShock(shock);
        if (splash > 0f)
        {
            splashBuf.Clear();
            CardProcs.CollectTargets(CenterOf(c), splash, splashBuf);
            foreach (var o in splashBuf) if (o != c) DealQuiet(o, Mathf.Max(1, dmg / 3), ElementType.Thunder); // 2026-10-30: 1/2 → 1/3(群れに強すぎた)
        }
    }

    // 戻り値: 連鎖の最後に当たった相手(連鎖しなければ最初の相手)
    public const float LeftoverChainMul = 0.25f;
    static Component Chain(Component first, int dmg, int chains, float range, float splash = 0f, float shock = 0f)
    {
        int dmg0 = dmg;
        Vector3 from = CenterOf(first);
        Bolt(from + Vector3.up * 3.5f, from);
        Strike(first, dmg, splash, shock);
        Component last = first;
        if (chains <= 0) return last;
        chainBuf.Clear();
        foreach (var en in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (en != first && IsAlive(en)) chainBuf.Add(en);
        foreach (var b in Object.FindObjectsByType<WildBossBase>(FindObjectsSortMode.None))
            if (b != first && IsAlive(b)) chainBuf.Add(b);
        Vector3 cur = from;
        int i = 0;
        for (; i < chains && chainBuf.Count > 0; i++)
        {
            int best = -1; float bd = range * range;
            for (int k = 0; k < chainBuf.Count; k++)
            {
                float d = (CenterOf(chainBuf[k]) - cur).sqrMagnitude;
                if (d <= bd) { bd = d; best = k; }
            }
            if (best < 0) break;
            Component next = chainBuf[best];
            chainBuf.RemoveAt(best);
            Vector3 to = CenterOf(next);
            Bolt(cur, to, true);
            ElementTelemetry.Chains++;
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.6f)); // 連鎖するほど弱く(2026-10-30: 0.75 → 0.6。連鎖数を増やした分と、群れに強すぎた分)
            Strike(next, dmg, splash, shock);
            cur = to;
            last = next;
        }
        // 2026-10-30: 近くに連鎖先がいない時は、残りの連鎖が最初の敵へ弱く落ちる(1本ごとに最初の落雷の25%)。ボス1体でも雷らしい瞬間火力
        int left = chains - i;
        if (left > 0 && IsAlive(first)) DealQuiet(first, Mathf.Max(1, Mathf.RoundToInt(dmg0 * LeftoverChainMul * left)), ElementType.Thunder);
        return last;
    }

    // COMBO(THUNDER CHAIN): from から to へ1本(連鎖/範囲なし。COMBO の側で同じ敵の間隔を見る)
    public static void ComboStrike(Vector3 from, Component to, int dmg)
    {
        if (to == null || !IsAlive(to)) return;
        Bolt(from, CenterOf(to), true);
        Strike(to, dmg, 0f, 0f);
    }

    // 稲妻の見た目(短い線。最終演出ではない)
    static void Bolt(Vector3 a, Vector3 b, bool chain = false)
    {
        NetElementFx.Bolt(a, b, chain, ElementVfx.Tier(ElementType.Thunder)); // マルチ: JOIN にも稲妻の線(HOST だけが送る)
        if (ElementVfx.New) { ElementVfx.Bolt(a, b, ElementVfx.Tier(ElementType.Thunder), chain); return; } // 2026-10-30: 太い芯+光の縁、連鎖は少し長く残す(プール)
        var go = new GameObject("ElementBolt");
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = SharedMaterials.SpritesDefault; // 共有(出すたびに作ると消えずに残る。2026-10-08)
        lr.startColor = lr.endColor = new Color(1f, 0.95f, 0.45f, 0.95f);
        lr.startWidth = 0.12f; lr.endWidth = 0.05f;
        lr.sortingOrder = RenderOrder.CombatFx + 1;
        const int n = 6;
        lr.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.25f, 0.25f), 0f);
            lr.SetPosition(i, p);
        }
        Object.Destroy(go, 0.12f);
    }

    static void SpawnWindBlade(ElementStats e)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float dir = pc.FacingSign;
        Vector3 pos = pc.transform.position + new Vector3(dir * 0.9f, 0.9f, 0f);
        float speed = e.WindSpeed;
        float life = e.WindRange / speed;
        // 敵側は EffectiveAttackPower × damageScale で読むので、風刃の基礎ダメージになるように倍率を決める
        float scale = e.WindBladeDamage * HitWeight / Mathf.Max(1f, pc.EffectiveAttackPower);
        var proj = KitProjectile.Create(KitArt.WhiteSprite(), pos, new Vector2(dir * speed, 0f), life,
            new Vector2(0.9f, 0.18f), new Vector2(0.9f, 0.5f), new Color(0.75f, 1f, 0.85f, 0.85f), PlayerAttackKind.Normal,
            scale, 0.3f, 0f, new Color(0.7f, 1f, 0.85f, 0.5f));
        proj.name = "ElementWindBlade";
        proj.pierce = 2 + CardProcs.ComboWindPierceBonus; // 風の貫通の追加(WindPierce)は KitProjectile.Start で足される / COMBO(GALE EDGE)の+1
        var info = proj.GetComponent<PlayerAttackInfo>();
        if (info != null) { info.elementProc = true; info.windBlade = true; info.seqTag = AttackSeqTag.None; info.seqMoveId = 0; }
        if (ElementVfx.New)
        {
            // 2026-10-30: 風は透明なので背景に埋もれないよう、少し濃い緑の縁を後ろに敷く + 出した時にプレイヤーに短い風の線
            var sr = proj.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.82f, 1f, 0.93f, 0.95f);
                var edge = new GameObject("WindEdge"); edge.transform.SetParent(proj.transform, false);
                edge.transform.localScale = new Vector3(1.18f, 1.9f, 1f);
                var er = edge.AddComponent<SpriteRenderer>(); er.sprite = sr.sprite; er.color = new Color(0.05f, 0.42f, 0.36f, 0.75f); er.sortingOrder = sr.sortingOrder - 1;
            }
            ElementVfx.WindRelease(pos, dir);
        }
        NetElementFx.WindBlade(pos, dir, e.WindRange);
    }
}

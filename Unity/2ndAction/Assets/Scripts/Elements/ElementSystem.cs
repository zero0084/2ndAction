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
            return 1f + CardRules.ProcAttackShare * CardRules.SoftAttack(a);
        }
    }

    static float lastWindBlade = -9f;

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
        ElementStats e = gm.Elements;
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
            FinalEvolution.OnBurnApplied(victim, bdps, e.BurnDuration);
        }
        // 氷: 行動を遅く、重なると凍結(ボスは強めの減速)
        if (e.ChillChance > 0f && e.ChillSlow > 0f && Roll() < e.ChillChance)
        {
            var st = ElementStatus.For(victim);
            if (st.AddChill(e.ChillSlow, e.ChillDuration, e.FreezeStacks, e.FreezeDuration, e.AbsoluteZero))
            {
                FreezeProcs++;
                if (e.AbsoluteZero > 0) AbsoluteZeroShatter(victim, e.AbsoluteZero);
                CardProcs.ComboOnFreeze(victim, info); // COMBO(FROZEN PRISON)
            }
            ChillProcs++;
        }
        // 血: 出血(継続ダメージ、低HPほど強い、一部を回復)。BLOOD BLADE: 満HPの間は確率2倍
        float bleedChance = e.BleedChance;
        if (e.BloodBlade && gm.Lives >= gm.maxLives) bleedChance = Mathf.Min(1f, bleedChance * 2f);
        if (bleedChance > 0f && e.BleedPower > 0f && Roll() < bleedChance)
        {
            float missing = gm.maxLives > 0 ? 1f - (float)gm.Lives / gm.maxLives : 0f;
            float dps = e.BleedPower * DamageScale * (1f + e.BloodLowHpBonus * Mathf.Clamp01(missing));
            ElementStatus.For(victim).AddBleed(dps, e.BleedDuration, e.BleedLifesteal);
            BleedProcs++;
        }
        // 雷: 落雷(当たった相手)+ 近くの別の敵へ連鎖 + 周りへの範囲
        // FINAL EVOLUTION(THUNDER STRIKE): 落雷しやすく連鎖+1。1体へ0.4秒に1回の制限と、落雷から落雷を出さない決まりはそのまま
        float lChance = e.LightningChance > 0f ? Mathf.Min(1f, e.LightningChance + FinalEvolution.LightningChanceAdd) : 0f;
        if (lChance > 0f && e.LightningPower > 0f && Roll() < lChance && LightningReady(victim))
        {
            LightningProcs++;
            int ldmg = Mathf.Max(1, Mathf.RoundToInt(e.LightningDamage * HitWeight));
            var last = Chain(victim, ldmg, e.LightningChains + FinalEvolution.LightningChainsAdd, e.LightningRange, e.LightningSplash, e.LightningShock);
            CardProcs.ComboOnLightning(victim, last, ldmg, info); // COMBO(THUNDER CHAIN / STORM LORD)
        }
        // 風: 前方へ飛ぶ貫通の風刃(風刃自身からは出さない。短い間隔をあける)
        if ((info == null || !info.elementProc) && e.WindBladeChance > 0f && e.WindBladePower > 0f && Time.time - lastWindBlade > 0.25f && Roll() < e.WindBladeChance)
        {
            lastWindBlade = Time.time;
            SpawnWindBlade(e);
            WindBlades++;
        }
    }

    public static int Shatters;
    // ABSOLUTE ZERO: 凍結した瞬間に砕ける冷気のダメージ。ボスは凍結の代わりの減速がさらに強くなる(ElementStatus)
    static void AbsoluteZeroShatter(Component victim, int lv)
    {
        Shatters++;
        float dmg = (10f + 3f * lv) * DamageScale * HitWeight;
        DealQuiet(victim, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Ice);
        OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, CenterOf(victim), new Color(0.75f, 0.95f, 1f, 0.9f), 0.25f, 0.4f, 1.3f, -1f, 0f);
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
        if (dealt) ElementDamage += dmg;
    }

    public static void HealFromBleed(int amount)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0) return;
        gm.KitHeal(amount);
        BleedHealed += amount;
    }

    static readonly List<Component> chainBuf = new List<Component>();
    static readonly List<Component> splashBuf = new List<Component>();
    static void Strike(Component c, int dmg, float splash, float shock)
    {
        DealQuiet(c, dmg, ElementType.Thunder);
        LightningHits++;
        if (shock > 0f && c is EnemyController && IsAlive(c)) ElementStatus.For(c).AddShock(shock);
        if (splash > 0f)
        {
            splashBuf.Clear();
            CardProcs.CollectTargets(CenterOf(c), splash, splashBuf);
            foreach (var o in splashBuf) if (o != c) DealQuiet(o, Mathf.Max(1, dmg / 2), ElementType.Thunder);
        }
    }

    // 戻り値: 連鎖の最後に当たった相手(連鎖しなければ最初の相手)
    static Component Chain(Component first, int dmg, int chains, float range, float splash = 0f, float shock = 0f)
    {
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
        for (int i = 0; i < chains && chainBuf.Count > 0; i++)
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
            Bolt(cur, to);
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.75f)); // 連鎖するほど少し弱く
            Strike(next, dmg, splash, shock);
            cur = to;
            last = next;
        }
        return last;
    }

    // COMBO(THUNDER CHAIN): from から to へ1本(連鎖/範囲なし。COMBO の側で同じ敵の間隔を見る)
    public static void ComboStrike(Vector3 from, Component to, int dmg)
    {
        if (to == null || !IsAlive(to)) return;
        Bolt(from, CenterOf(to));
        Strike(to, dmg, 0f, 0f);
    }

    // 稲妻の見た目(短い線。最終演出ではない)
    static void Bolt(Vector3 a, Vector3 b)
    {
        var go = new GameObject("ElementBolt");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
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
    }
}

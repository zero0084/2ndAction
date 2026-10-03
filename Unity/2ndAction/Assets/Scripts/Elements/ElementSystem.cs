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

    public static void OnPlayerHit(Component victim, PlayerAttackInfo info, int damage)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.IsGameOver || !Authoritative) return;
        ElementStats e = gm.Elements;
        if (!e.AnyActive) return;
        victim = Normalize(victim);
        if (victim == null || !IsAlive(victim)) return;
        int dmg = Mathf.Max(1, damage);

        // 炎: 継続ダメージ(その命中のダメージ × 威力 / 秒)
        if (e.BurnChance > 0f && e.BurnPower > 0f && Roll() < e.BurnChance)
        {
            ElementStatus.For(victim).AddBurn(dmg * e.BurnPower, e.BurnDuration);
            BurnProcs++;
        }
        // 氷: 行動を遅く、重なると凍結
        if (e.ChillChance > 0f && e.ChillSlow > 0f && Roll() < e.ChillChance)
        {
            if (ElementStatus.For(victim).AddChill(e.ChillSlow, e.ChillDuration, e.FreezeStacks, e.FreezeDuration)) FreezeProcs++;
            ChillProcs++;
        }
        // 血: 出血(継続ダメージ、低HPほど強い、一部を回復)
        if (e.BleedChance > 0f && e.BleedPower > 0f && Roll() < e.BleedChance)
        {
            float missing = gm.maxLives > 0 ? 1f - (float)gm.Lives / gm.maxLives : 0f;
            float power = e.BleedPower * (1f + e.BloodLowHpBonus * Mathf.Clamp01(missing));
            ElementStatus.For(victim).AddBleed(dmg * power, e.BleedDuration, e.BleedLifesteal);
            BleedProcs++;
        }
        // 雷: 落雷(当たった相手)+ 近くの別の敵へ連鎖
        if (e.LightningChance > 0f && e.LightningPower > 0f && Roll() < e.LightningChance)
        {
            LightningProcs++;
            Chain(victim, Mathf.Max(1, Mathf.RoundToInt(dmg * e.LightningPower)), e.LightningChains, e.LightningRange);
        }
        // 風: 前方へ飛ぶ貫通の風刃(風刃自身からは出さない)
        if ((info == null || !info.elementProc) && e.WindBladeChance > 0f && e.WindBladePower > 0f && Roll() < e.WindBladeChance)
        {
            SpawnWindBlade(e);
            WindBlades++;
        }
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
    static void Chain(Component first, int dmg, int chains, float range)
    {
        Vector3 from = CenterOf(first);
        Bolt(from + Vector3.up * 3.5f, from);
        DealQuiet(first, dmg, ElementType.Thunder);
        LightningHits++;
        if (chains <= 0) return;
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
            DealQuiet(next, dmg, ElementType.Thunder);
            LightningHits++;
            cur = to;
        }
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

    const float WindBladeSpeed = 18f;
    static void SpawnWindBlade(ElementStats e)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float dir = pc.FacingSign;
        Vector3 pos = pc.transform.position + new Vector3(dir * 0.9f, 0.9f, 0f);
        float life = e.WindRange / WindBladeSpeed;
        var proj = KitProjectile.Create(KitArt.WhiteSprite(), pos, new Vector2(dir * WindBladeSpeed, 0f), life,
            new Vector2(0.9f, 0.18f), new Vector2(0.9f, 0.5f), new Color(0.75f, 1f, 0.85f, 0.85f), PlayerAttackKind.Normal,
            e.WindBladePower, 0.3f, 0f, new Color(0.7f, 1f, 0.85f, 0.5f));
        proj.name = "ElementWindBlade";
        proj.pierce = 2; // 風の貫通の追加(WindPierce)は KitProjectile.Start で足される
        var info = proj.GetComponent<PlayerAttackInfo>();
        if (info != null) { info.elementProc = true; info.seqTag = AttackSeqTag.None; info.seqMoveId = 0; }
    }
}

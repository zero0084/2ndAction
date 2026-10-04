using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// カードバランス v3(2026-10-03): カードで出る「追加の攻撃」をまとめた所。
//  ・DOUBLE ATTACK(追加の1撃) / SHOCKWAVE(衝撃波) / PIERCING BLADE(後ろの敵へ貫通) / AERIAL BLADE(空中の追い斬り)
//    / COMBO MASTER(締めの特殊な一撃) / GROUND BREAKER(地面の衝撃波) / SONIC BLADE(高速時の斬撃)
//    / CHAIN EXPLOSION(撃破で爆発) / INFERNO(燃えている敵の撃破で炎) / TORNADO(風刃の竜巻)
//    / COUNTER・FLAME COUNTER(Shield で防いだ反撃)
//  ・ダメージは「静かなダメージ」(ElementSystem.DealQuiet: のけぞり/ヒットストップ/音なし、倒れたら通常の撃破)。
//    この攻撃自身からは、カードの追加攻撃も DOUBLE ATTACK も出ない(連鎖で増え続けない)。
//    DOUBLE ATTACK の追加の1撃だけは属性(炎上/冷気/落雷/出血)を判定する(通常の命中と同じ扱い。仕様どおり)。
//  ・ダメージの基準はキャラによらない: CardRules.ProcReferenceDamage(20) × (1 + 無条件の攻撃の枠の半分)。
//  ・性能の安全: 0.5秒あたりの論理ヒットと見た目の数に上限(超えた分は見た目を出さずにダメージだけ、さらに超えたら出さない)。
//    連鎖爆発は世代の上限と「同じ連鎖で同じ相手へは1回」、竜巻/衝撃波は種類ごとの間隔(ICD)。
public static class CardProcs
{
    public static int DoubleAttacks, Shockwaves, PierceHits, AerialSlashes, ComboMasterHits, GroundBreakers, SonicSlashes,
        ChainExplosions, Infernos, Tornados, Counters, FlameCounters, ProcBudgetDrops, FxBudgetDrops;
    public static void ResetCounters()
    {
        DoubleAttacks = Shockwaves = PierceHits = AerialSlashes = ComboMasterHits = GroundBreakers = SonicSlashes = 0;
        ChainExplosions = Infernos = Tornados = Counters = FlameCounters = ProcBudgetDrops = FxBudgetDrops = 0;
    }

    static GameManager GM => GameManager.Instance;
    static float L(EffectType t) => GM != null ? GM.Card.Get(t) : 0f;
    static int Lv(EffectType t) => Mathf.RoundToInt(L(t));

    // 追加攻撃の基準ダメージ
    public static float ProcBase => CardRules.ProcReferenceDamage * (1f + CardRules.ProcAttackShare * CardRules.SoftAttack(L(EffectType.AttackPct)));

    // ---- 予算(0.5秒の窓)
    static float windowStart;
    static int windowProcs, windowFx;
    static void RollWindow()
    {
        if (Time.time - windowStart >= 0.5f) { windowStart = Time.time; windowProcs = 0; windowFx = 0; }
    }
    static bool TakeProc() { RollWindow(); if (windowProcs >= CardRules.ProcBudgetPerHalfSecond) { ProcBudgetDrops++; return false; } windowProcs++; return true; }
    public static bool TakeProcBudget() => TakeProc(); // FINAL EVOLUTION の追加攻撃も同じ枠を使う
    public static bool TakeFxBudget() => TakeFx();
    static bool TakeFx() { RollWindow(); if (windowFx >= CardRules.FxBudgetPerHalfSecond) { FxBudgetDrops++; return false; } windowFx++; return true; }

    // ---- ICD
    static readonly Dictionary<string, float> icd = new Dictionary<string, float>();
    static bool Ready(string key, float seconds)
    {
        if (icd.TryGetValue(key, out float t) && Time.time - t < seconds) return false;
        icd[key] = Time.time;
        return true;
    }

    static Component Normalize(Component c)
    {
        if (c == null) return null;
        if (c is BossHurtbox h) return h.owner;
        return c;
    }

    static bool IsBoss(Component c) => c is WildBossBase || c is DragonController || c is MajinController;

    static bool IsProjectile(Collider2D attack) => attack != null && (attack.GetComponent<KitProjectile>() != null || attack.GetComponent<PlayerBullet>() != null);

    // ===================================================================== 命中時

    static int lastDoubleSwing = -1; static bool lastDoubleRoll;
    static int lastPierceSwing = -1;
    static int lastComboMasterMove = -1;

    public static void OnPlayerHit(Component victim, PlayerAttackInfo info, Collider2D attack, int damage)
    {
        var gm = GM; var pc = PlayerController.Instance;
        if (gm == null || pc == null || gm.IsGameOver || info == null) return;
        if (NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority) return; // HP を持つ端末だけ
        victim = Normalize(victim);
        if (victim == null || !ElementSystem.IsAlive(victim)) return;

        // 竜巻(風刃が当たった所)
        if (info.windBlade && Lv(EffectType.TornadoLevel) > 0 && Ready("tornado", 0.8f)) SpawnTornado(CenterOf(victim), Lv(EffectType.TornadoLevel));
        if (info.elementProc) return; // カードの効果で出た攻撃からは、追加攻撃を出さない

        bool boss = IsBoss(victim);
        bool projectile = IsProjectile(attack);
        Vector3 at = CenterOf(victim);
        float dir = pc.FacingSign;

        // PREDATOR: ボスへの命中でも、吸収の判定(確率は1割)
        if (boss && L(EffectType.PredatorLevel) > 0f && Ready("predator", 0.5f)) gm.TryLifesteal(0.1f);

        // DOUBLE ATTACK: 1回の攻撃(振り/1発)ごとに1回だけ判定。当たった相手それぞれに追加の1撃
        float dbl = L(EffectType.DoubleAttackChance);
        if (dbl > 0f)
        {
            if (info.SwingId != lastDoubleSwing) { lastDoubleSwing = info.SwingId; lastDoubleRoll = ElementSystem.Roll() < Mathf.Min(0.9f, dbl); }
            if (lastDoubleRoll) pc.StartCoroutine(ExtraHit(victim, damage, info));
        }

        // SHOCKWAVE: 近接は相手の向こうへ短い衝撃波、飛び道具は着弾点のまわり
        int sw = Lv(EffectType.ShockwaveLevel);
        if (sw > 0 && Ready("shockwave", 0.3f))
        {
            float dmg = ProcBase * (0.25f + 0.035f * sw);
            Color c = new Color(1f, 0.92f, 0.6f, 0.9f);
            if (projectile) Area(at, 1.0f + 0.08f * sw, dmg, victim, ElementType.None, c, ref Shockwaves);
            else Box(at + new Vector3(dir * (0.9f + 0.06f * sw), 0f, 0f), new Vector2(1.8f + 0.12f * sw, 1.6f), dmg, victim, c, ref Shockwaves);
        }

        // PIERCING BLADE(近接): 相手の後ろに並ぶ敵へ貫通(1回の振りにつき1回)。飛び道具は貫通の回数(弾の側)
        int pr = Lv(EffectType.PierceLevel);
        if (pr > 0 && !projectile && info.SwingId != lastPierceSwing)
        {
            lastPierceSwing = info.SwingId;
            PierceBehind(victim, at, dir, 1 + pr / 3, 2.2f + 0.1f * pr, ProcBase * (0.35f + 0.04f * pr));
        }

        // AERIAL BLADE: 空中で当てると追い斬り
        int ab = Lv(EffectType.AerialBladeLevel);
        if (ab > 0 && !pc.IsGrounded && Ready("aerial", 0.4f))
            Area(at, 1.4f + 0.06f * ab, ProcBase * (0.3f + 0.04f * ab), null, ElementType.None, new Color(0.6f, 0.9f, 1f, 0.9f), ref AerialSlashes);

        // COMBO MASTER: 締めの一撃で前方へ特殊な衝撃
        int cm = Lv(EffectType.ComboMasterLevel);
        if (cm > 0 && info.seqTag == AttackSeqTag.Finisher && info.seqMoveId != lastComboMasterMove)
        {
            lastComboMasterMove = info.seqMoveId;
            Box(pc.transform.position + new Vector3(dir * (1.4f + 0.05f * cm), 0.9f, 0f), new Vector2(2.2f + 0.1f * cm, 2.0f), ProcBase * (0.5f + 0.08f * cm), null, new Color(1f, 0.6f, 0.95f, 0.9f), ref ComboMasterHits);
        }

        // GROUND BREAKER: 叩きつけの着地 / 地上の締めで、地面を走る衝撃波
        int gb = Lv(EffectType.GroundBreakerLevel);
        if (gb > 0 && (info.kind == PlayerAttackKind.DownImpact || (info.seqTag == AttackSeqTag.Finisher && pc.IsGrounded)) && Ready("groundbreaker", 0.5f))
        {
            Vector3 g = pc.transform.position;
            Box(g + new Vector3(0f, 0.6f, 0f), new Vector2(2f * (2.5f + 0.2f * gb), 1.2f), ProcBase * (0.3f + 0.05f * gb), victim, new Color(0.85f, 0.7f, 0.45f, 0.9f), ref GroundBreakers);
        }
    }

    static IEnumerator ExtraHit(Component victim, int damage, PlayerAttackInfo info)
    {
        yield return new WaitForSeconds(0.07f);
        if (!ElementSystem.IsAlive(victim)) yield break;
        DoubleAttacks++;
        Vector3 at = CenterOf(victim);
        var pc = PlayerController.Instance;
        if (TakeFx()) AttackFlair.Hit(at, pc != null ? pc.FacingSign : 1f, false);
        ElementSystem.DealQuiet(victim, Mathf.Max(1, damage), ElementType.None);
        // 通常の命中と同じく属性は判定する(DOUBLE ATTACK 自身はもう出ない)
        if (ElementSystem.IsAlive(victim)) ElementSystem.OnPlayerHit(victim, null, damage);
    }

    static readonly List<Component> pierceBuf = new List<Component>();
    static void PierceBehind(Component first, Vector3 from, float dir, int count, float range, float dmg)
    {
        pierceBuf.Clear();
        CollectTargets(from + new Vector3(dir * range * 0.5f, 0f, 0f), new Vector2(range, 1.6f), pierceBuf);
        pierceBuf.RemoveAll(c => c == first || (CenterOf(c).x - from.x) * dir <= 0f);
        pierceBuf.Sort((a, b) => Mathf.Abs(CenterOf(a).x - from.x).CompareTo(Mathf.Abs(CenterOf(b).x - from.x)));
        int n = 0;
        foreach (var c in pierceBuf)
        {
            if (n >= count || !TakeProc()) break;
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.None);
            if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.SkyBolt != null ? KitProjectile.SkyBolt : KitArt.WhiteSprite(), CenterOf(c), new Color(0.85f, 0.75f, 1f, 0.85f), 0.16f, 0.5f, 0.8f);
            PierceHits++; n++;
        }
    }

    // ===================================================================== 撃破時(EnemyController.RegisterKillReward)

    public static void OnEnemyKilled(EnemyController e)
    {
        var gm = GM;
        if (gm == null || e == null || gm.IsGameOver) return;
        Vector3 at = CenterOf(e);
        var st = e.GetComponent<ElementStatus>();
        bool burning = st != null && st.BurningOrDiedBurning;
        int ce = Lv(EffectType.ChainExplosionLevel);
        if (ce > 0) gm.StartCoroutine(ChainExplosion(at, ce, 0, new HashSet<Component> { e }));
        int inf = Lv(EffectType.InfernoLevel);
        if (inf > 0 && burning) gm.StartCoroutine(InfernoBurst(at, inf, 0, new HashSet<Component> { e }));
    }

    static readonly List<Component> areaBuf = new List<Component>();

    static IEnumerator ChainExplosion(Vector3 at, int lv, int gen, HashSet<Component> hitThisChain)
    {
        yield return new WaitForSeconds(0.08f);
        if (!TakeProc()) yield break;
        ChainExplosions++;
        float radius = 1.6f + 0.08f * lv;
        float dmg = ProcBase * (0.4f + 0.06f * lv) * Mathf.Pow(0.7f, gen);
        if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, new Color(1f, 0.65f, 0.3f, 0.95f), 0.28f, 0.4f, radius * 0.9f, -1f, 0f);
        var targets = new List<Component>();
        CollectTargets(at, radius, targets);
        foreach (var c in targets)
        {
            if (hitThisChain.Contains(c)) continue; // 同じ連鎖では同じ相手へ1回
            hitThisChain.Add(c);
            bool wasAlive = ElementSystem.IsAlive(c);
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Fire);
            // 連鎖: 爆発で倒れた敵からさらに爆発(世代の上限まで)
            if (wasAlive && c is EnemyController en && !ElementSystem.IsAlive(en) && gen + 1 < CardRules.ChainExplosionMaxGeneration && GM != null)
                GM.StartCoroutine(ChainExplosion(CenterOf(en), lv, gen + 1, hitThisChain));
        }
    }

    static IEnumerator InfernoBurst(Vector3 at, int lv, int gen, HashSet<Component> hitThisChain)
    {
        yield return new WaitForSeconds(0.06f);
        if (!TakeProc()) yield break;
        Infernos++;
        var e = GM != null ? GM.Elements : null;
        float radius = 2.0f + 0.1f * lv;
        float scale = ElementSystem.DamageScale;
        float dmg = (6f + 2f * lv) * scale * Mathf.Pow(0.7f, gen);
        if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, new Color(1f, 0.4f, 0.12f, 0.9f), 0.32f, 0.5f, radius * 0.9f, -1f, 0f);
        var targets = new List<Component>();
        CollectTargets(at, radius, targets);
        float burnDps = e != null && e.BurnPower > 0f ? e.BurnDps : (4f + lv) * scale;
        float burnDur = e != null ? e.BurnDuration : ElementStats.DefaultBurnDuration;
        foreach (var c in targets)
        {
            if (hitThisChain.Contains(c)) continue;
            hitThisChain.Add(c);
            bool wasAlive = ElementSystem.IsAlive(c);
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Fire);
            if (ElementSystem.IsAlive(c)) ElementStatus.For(c).AddBurn(burnDps, burnDur);
            if (wasAlive && c is EnemyController en && !ElementSystem.IsAlive(en) && gen + 1 < 2 && GM != null)
                GM.StartCoroutine(InfernoBurst(CenterOf(en), lv, gen + 1, hitThisChain));
        }
    }

    static void SpawnTornado(Vector3 at, int lv)
    {
        var gm = GM;
        if (gm == null) return;
        gm.StartCoroutine(Tornado(at, lv));
    }

    static IEnumerator Tornado(Vector3 at, int lv)
    {
        Tornados++;
        float radius = 1.2f + 0.06f * lv;
        float dmg = (4f + lv) * ElementSystem.DamageScale;
        for (int i = 0; i < 3; i++)
        {
            if (!TakeProc()) yield break;
            if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at + Vector3.up * 0.5f, new Color(0.7f, 1f, 0.85f, 0.7f), 0.3f, radius * 0.6f, radius, -1f, 0f, Vector3.up * 0.8f, 180f);
            var targets = new List<Component>();
            CollectTargets(at + Vector3.up * 0.6f, radius, targets);
            foreach (var c in targets) ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.Wind);
            yield return new WaitForSeconds(0.4f);
        }
    }

    // ===================================================================== Shield で防いだ

    public static void OnShieldBlock(PlayerController pc)
    {
        if (pc == null || GM == null) return;
        Vector3 at = pc.transform.position + Vector3.up * 0.8f;
        int co = Lv(EffectType.CounterLevel);
        if (co > 0) Area(at, 3.0f, ProcBase * (1.0f + 0.25f * co), null, ElementType.None, new Color(0.7f, 0.85f, 1f, 0.95f), ref Counters);
        int fc = Lv(EffectType.FlameCounterLevel);
        if (fc > 0)
        {
            float scale = ElementSystem.DamageScale;
            var e = GM.Elements;
            float burnDps = e.BurnPower > 0f ? Mathf.Max(e.BurnDps, (4f + fc) * scale) : (4f + fc) * scale;
            areaBuf.Clear();
            CollectTargets(at, 2.5f, areaBuf);
            if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, new Color(1f, 0.45f, 0.15f, 0.95f), 0.3f, 0.6f, 2.4f, -1f, 0f);
            foreach (var c in areaBuf)
            {
                if (!TakeProc()) break;
                ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(ProcBase * (0.6f + 0.15f * fc))), ElementType.Fire);
                if (ElementSystem.IsAlive(c)) ElementStatus.For(c).AddBurn(burnDps, Mathf.Max(3f, e.BurnDuration));
            }
            FlameCounters++;
        }
    }

    // ===================================================================== SONIC BLADE(主攻撃の1押し)

    public static void TrySonicSlash(PlayerController pc, int lv)
    {
        if (pc == null || lv <= 0 || !Ready("sonic", 0.35f)) return;
        if (NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority) return;
        float sf = pc.SpeedFactor01(60f, 150f);
        float reach = 3f + 5f * sf + 0.2f * lv;
        const float speed = 22f;
        float dir = pc.FacingSign;
        float size = 1f + 0.6f * sf;
        float want = ProcBase * (0.25f + 0.03f * lv) * (1f + 0.5f * sf);
        float scale = want / Mathf.Max(1f, pc.EffectiveAttackPower);
        Vector3 pos = pc.transform.position + new Vector3(dir * 0.8f, 0.85f, 0f);
        var proj = KitProjectile.Create(KitArt.WhiteSprite(), pos, new Vector2(dir * speed, 0f), reach / speed,
            new Vector2(0.75f * size, 0.16f * size), new Vector2(0.8f * size, 0.6f * size), new Color(0.75f, 0.9f, 1f, 0.85f), PlayerAttackKind.Normal,
            scale, 0.25f, 0f, new Color(0.7f, 0.9f, 1f, 0.5f));
        proj.name = "CardSonicSlash";
        proj.pierce = 2;
        var info = proj.GetComponent<PlayerAttackInfo>();
        if (info != null) { info.elementProc = true; info.seqTag = AttackSeqTag.None; info.seqMoveId = 0; }
        SonicSlashes++;
    }

    public static void OverdriveStart(PlayerController pc)
    {
        if (pc == null) return;
        OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, pc.transform.position + Vector3.up * 0.8f, new Color(1f, 0.4f, 0.25f, 0.9f), 0.35f, 0.6f, 2.2f, -1f, 0f);
        BossBattleHud.Banner("OVERDRIVE", new Color(1f, 0.45f, 0.3f), 1.0f);
    }

    public static void PhoenixBurst(Vector3 at)
    {
        OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at + Vector3.up * 0.8f, new Color(1f, 0.6f, 0.2f, 0.95f), 0.5f, 0.6f, 3.2f, -1f, 0f);
    }

    // ===================================================================== 範囲の共通

    static void Area(Vector3 at, float radius, float dmg, Component exclude, ElementType el, Color fx, ref int counter)
    {
        areaBuf.Clear();
        CollectTargets(at, radius, areaBuf);
        bool any = false;
        foreach (var c in areaBuf)
        {
            if (c == exclude) continue;
            if (!TakeProc()) break;
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), el);
            any = true;
        }
        if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, fx, 0.22f, radius * 0.4f, radius * 0.95f, -1f, 0f);
        if (any || exclude == null) counter++;
    }

    static void Box(Vector3 center, Vector2 size, float dmg, Component exclude, Color fx, ref int counter)
    {
        areaBuf.Clear();
        CollectTargets(center, size, areaBuf);
        bool any = false;
        foreach (var c in areaBuf)
        {
            if (c == exclude) continue;
            if (!TakeProc()) break;
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.None);
            any = true;
        }
        if (TakeFx())
        {
            var s = KitProjectile.Slash != null ? KitProjectile.Slash : KitArt.WhiteSprite();
            var fxObj = OneShotSpriteEffect.CreateTweened(s, center, fx, 0.2f, 0.7f, 1f, -1f, 0f);
            if (fxObj != null) fxObj.transform.localScale = new Vector3(size.x, Mathf.Max(0.4f, size.y * 0.6f), 1f);
        }
        if (any || exclude == null) counter++;
    }

    static readonly List<Collider2D> colBuf = new List<Collider2D>();
    static ContactFilter2D AnyFilter
    {
        get { var f = new ContactFilter2D(); f.useTriggers = true; f.useLayerMask = false; return f; }
    }

    public static void CollectTargets(Vector3 at, float radius, List<Component> into)
    {
        colBuf.Clear();
        Physics2D.OverlapCircle(at, radius, AnyFilter, colBuf);
        AddTargets(into);
    }

    public static void CollectTargets(Vector3 center, Vector2 size, List<Component> into)
    {
        colBuf.Clear();
        Physics2D.OverlapBox(center, size, 0f, AnyFilter, colBuf);
        AddTargets(into);
    }

    static void AddTargets(List<Component> into)
    {
        foreach (var col in colBuf)
        {
            if (col == null || col.CompareTag("PlayerAttack")) continue;
            if (col.GetComponent<BossHitbox>() != null || col.GetComponent<EnemyMeleeHitbox>() != null) continue;
            Component c = col.GetComponent<EnemyController>();
            if (c == null) { var h = col.GetComponent<BossHurtbox>(); if (h != null) c = h.owner; }
            if (c == null) c = col.GetComponent<WildBossBase>();
            if (c == null) c = col.GetComponent<DragonController>();
            if (c == null) c = col.GetComponent<MajinController>();
            if (c == null || into.Contains(c) || !ElementSystem.IsAlive(c)) continue;
            if (c is EnemyController en && en.bonus != null) { into.Add(c); continue; }
            into.Add(c);
        }
    }

    public static Vector3 CenterOf(Component c)
    {
        if (c == null) return Vector3.zero;
        var col = c.GetComponentInChildren<Collider2D>();
        return col != null ? col.bounds.center : c.transform.position + Vector3.up;
    }
}

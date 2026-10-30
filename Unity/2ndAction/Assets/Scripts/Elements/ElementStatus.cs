using UnityEngine;

// 属性(2026-10-03): 敵/ボス1体ぶんの状態(炎上/出血の継続ダメージ、冷気/凍結)。最初に属性が付いた時に ElementSystem が付ける。
//   ・継続ダメージは0.5秒ごと(ElementSystem.DealQuiet = のけぞり/音/ヒットストップ無し)
//   ・同じ状態の重ねがけは「時間を延ばし、強い方の威力を残す」(足し算で際限なく強くならない)
//   ・冷気/凍結は TimeScale(雑魚の行動の時間の倍率)/ AiTimeScaleOf(ボスの待ち・予備動作の倍率)で効く
//     雑魚: Chill = 1 - 鈍化、Freeze = 0(止まる)。ボス: 止めない。Chill は鈍化×0.35、Freeze は 0.5 倍の速さ
public class ElementStatus : MonoBehaviour
{
    public const float TickInterval = 0.5f;
    public const float BossChillFactor = 0.35f;
    public const float BossFreezeScale = 0.5f;

    Component target;
    bool isBoss;
    float burnDps, burnLeft;
    float bleedDps, bleedLeft, bleedHeal, healCarry;
    float chillSlow, chillLeft, freezeLeft;
    int chillStacks;
    // v3: 炎上の重ねがけ(時間を延ばし、1回ごとに+10%、最大+50%)/ 雷の一瞬の停止 / ABSOLUTE ZERO でのボスの減速
    float burnBoost, shockLeft;
    int absoluteZero;
    float tick;
    SpriteRenderer marker;

    public bool Burning => burnLeft > 0f;
    // v3: 燃えたまま倒れたか(撃破の処理は倒れる演出の後なので、その時には炎上が消えている。INFERNO が使う)
    public bool DiedBurning { get; private set; }
    public bool ComboBurned; // COMBO の炎上を受けた(この敵からは WILDFIRE を出さない: 無限に燃え移らない)
    public bool BurningOrDiedBurning => burnLeft > 0f || DiedBurning;
    public bool Bleeding => bleedLeft > 0f;
    public bool Chilled => chillLeft > 0f;
    public bool Frozen => freezeLeft > 0f;
    public int ChillStacks => chillStacks;
    public bool IsBoss => isBoss;

    // 2026-10-30 マルチ: JOIN の見た目だけの状態(HOST から届いた知らせ。ダメージも判定もしない)
    bool remote;
    public static ElementStatus ForRemote(Component victim)
    {
        var s = For(victim);
        if (s != null) s.remote = true;
        return s;
    }
    public void RemoteState(NetElementFx.StateKind kind, float seconds, int tier)
    {
        switch (kind)
        {
            case NetElementFx.StateKind.Burn: bool fresh = burnLeft <= 0f; burnLeft = Mathf.Max(burnLeft, seconds); if (fresh) ElementVfx.BurnApplied(target); break;
            case NetElementFx.StateKind.Chill: chillLeft = Mathf.Max(chillLeft, seconds); chillSlow = Mathf.Max(chillSlow, 0.01f); ElementVfx.ChillApplied(target, false); break;
            case NetElementFx.StateKind.Freeze: freezeLeft = Mathf.Max(freezeLeft, seconds); ElementVfx.ChillApplied(target, true); break;
            case NetElementFx.StateKind.Bleed: bool fb = bleedLeft <= 0f; bleedLeft = Mathf.Max(bleedLeft, seconds); if (fb) ElementVfx.BleedApplied(target); break;
            case NetElementFx.StateKind.Charged: MarkCharged(seconds); ElementVfx.StrikeImpact(target, Mathf.Max(1, tier)); break;
            case NetElementFx.StateKind.Wind: MarkWind(seconds); ElementVfx.WindHit(target); break;
        }
    }
    bool TargetAlive => remote ? target != null && target.gameObject.activeInHierarchy && !(target is EnemyController en && en.IsDying) : ElementSystem.IsAlive(target);
    // HOST: 状態が付いた/延びた時に JOIN へ知らせる(同じ状態の延長は NetElementFx が0.5秒に1回まで)
    void Notify(NetElementFx.StateKind kind, float seconds, ElementType el)
    {
        if (!remote && NetCombat.Authority) NetElementFx.State(target, kind, seconds, ElementVfx.Tier(el));
    }

    public static ElementStatus For(Component victim)
    {
        if (victim == null) return null;
        var s = victim.GetComponent<ElementStatus>();
        if (s == null) s = victim.gameObject.AddComponent<ElementStatus>();
        s.target = victim;
        s.isBoss = !(victim is EnemyController);
        return s;
    }

    // 雑魚の行動の時間の倍率
    public float TimeScale
    {
        get
        {
            if (isBoss) return AiTimeScale;
            if (freezeLeft > 0f || shockLeft > 0f) return 0f;
            if (chillLeft > 0f) return 1f - chillSlow;
            return 1f;
        }
    }

    // ボスの待ち/予備動作の時間の倍率(止めない)
    public float AiTimeScale
    {
        get
        {
            if (freezeLeft > 0f) return Mathf.Max(0.3f, BossFreezeScale - 0.02f * absoluteZero);
            if (chillLeft > 0f) return 1f - chillSlow * BossChillFactor;
            return 1f;
        }
    }

    public static float AiTimeScaleOf(GameObject go)
    {
        if (go == null) return 1f;
        var s = go.GetComponent<ElementStatus>();
        return s != null ? s.AiTimeScale : 1f;
    }

    public void AddBurn(float dps, float duration)
    {
        if (burnLeft > 0f) burnBoost = Mathf.Min(isBoss ? 0.15f : 0.5f, burnBoost + 0.1f); else burnBoost = 0f; // 2026-10-30: ボスは +15% まで(炎は群れ向き)
        burnDps = Mathf.Max(burnLeft > 0f ? burnDps : 0f, dps);
        burnLeft = Mathf.Max(burnLeft, duration);
        Notify(NetElementFx.StateKind.Burn, burnLeft, ElementType.Fire);
    }

    public void AddShock(float seconds) { shockLeft = Mathf.Max(shockLeft, seconds); }

    // 2026-10-30(視認性): 見た目だけの状態。落雷を受けた直後の「帯電」/ 風刃・竜巻を受けた直後の「風」(性能には関係しない)
    float chargedLeft, windLeft;
    public void MarkCharged(float seconds = 0.8f) { chargedLeft = Mathf.Max(chargedLeft, seconds); Notify(NetElementFx.StateKind.Charged, seconds, ElementType.Thunder); }
    public void MarkWind(float seconds = 1.0f) { windLeft = Mathf.Max(windLeft, seconds); Notify(NetElementFx.StateKind.Wind, seconds, ElementType.Wind); }
    public bool Charged => chargedLeft > 0f || shockLeft > 0f;
    public bool Windswept => windLeft > 0f;

    public void AddBleed(float dps, float duration, float healFraction)
    {
        bleedDps = Mathf.Max(bleedLeft > 0f ? bleedDps : 0f, dps);
        bleedLeft = Mathf.Max(bleedLeft, duration);
        bleedHeal = Mathf.Max(bleedHeal, healFraction);
        Notify(NetElementFx.StateKind.Bleed, bleedLeft, ElementType.Blood);
    }

    // 返り値: このChillで凍結したか
    public bool AddChill(float slow, float duration, int freezeStacks, float freezeDuration, int absoluteZeroLevel = 0)
    {
        absoluteZero = Mathf.Max(absoluteZero, absoluteZeroLevel);
        chillSlow = Mathf.Max(chillLeft > 0f ? chillSlow : 0f, slow);
        chillLeft = Mathf.Max(chillLeft, duration);
        Notify(NetElementFx.StateKind.Chill, chillLeft, ElementType.Ice);
        if (freezeLeft > 0f || freezeStacks <= 0) return false;
        chillStacks++;
        if (chillStacks < freezeStacks) return false;
        chillStacks = 0;
        freezeLeft = freezeDuration;
        Notify(NetElementFx.StateKind.Freeze, freezeLeft, ElementType.Ice);
        return true;
    }

    void Update()
    {
        if (target == null || !TargetAlive) { if (burnLeft > 0f) DiedBurning = true; Clear(); return; }
        float dt = Time.deltaTime;
        if (chillLeft > 0f) ElementTelemetry.ChilledSeconds += dt;
        if (freezeLeft > 0f) ElementTelemetry.FrozenSeconds += dt;
        if (chillLeft > 0f) { chillLeft -= dt; if (chillLeft <= 0f) { chillLeft = 0f; chillSlow = 0f; chillStacks = 0; } }
        if (freezeLeft > 0f) freezeLeft = Mathf.Max(0f, freezeLeft - dt);
        if (shockLeft > 0f) shockLeft = Mathf.Max(0f, shockLeft - dt);
        if (chargedLeft > 0f) chargedLeft = Mathf.Max(0f, chargedLeft - dt);
        if (windLeft > 0f) windLeft = Mathf.Max(0f, windLeft - dt);
        if (burnLeft > 0f || bleedLeft > 0f)
        {
            tick += dt;
            while (tick >= TickInterval)
            {
                tick -= TickInterval;
                float dmg = 0f;
                if (burnLeft > 0f) dmg += burnDps * (1f + burnBoost) * TickInterval;
                float bleed = bleedLeft > 0f ? bleedDps * TickInterval : 0f;
                dmg += bleed;
                if (dmg > 0f)
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(dmg));
                    // 計測(2026-10-30): 炎上と出血が同時の時は1回のダメージなので、属性ごとの分は割合で数える(ダメージの処理は今までどおり)
                    bool both = burnLeft > 0f && bleed > 0f;
                    if (both) { ElementTelemetry.Suppress = true; ElementTelemetry.Damage[(int)ElementType.Blood] += Mathf.RoundToInt(n * bleed / dmg); ElementTelemetry.Damage[(int)ElementType.Fire] += n - Mathf.RoundToInt(n * bleed / dmg); }
                    try { ElementSystem.DealQuiet(target, n, burnLeft > 0f ? ElementType.Fire : ElementType.Blood); }
                    finally { if (both) ElementTelemetry.Suppress = false; }
                    if (burnLeft > 0f) ElementVfx.BurnTick(target, Bounds, Size, FireTier);
                    if (bleed > 0f && bleedHeal > 0f)
                    {
                        healCarry += bleed * bleedHeal;
                        int heal = Mathf.FloorToInt(healCarry);
                        if (heal > 0) { healCarry -= heal; ElementSystem.HealFromBleed(heal, target); }
                    }
                }
                if (target == null || !ElementSystem.IsAlive(target)) { if (burnLeft > 0f) DiedBurning = true; Clear(); return; }
            }
            burnLeft = Mathf.Max(0f, burnLeft - dt);
            bleedLeft = Mathf.Max(0f, bleedLeft - dt);
        }
        else tick = 0f;
        UpdateMarker();
    }

    void Clear()
    {
        burnLeft = bleedLeft = chillLeft = freezeLeft = shockLeft = chargedLeft = windLeft = 0f;
        burnBoost = 0f;
        chillStacks = 0;
        if (marker != null) marker.enabled = false;
        if (aura != null) aura.Hide();
    }

    void OnDisable() { if (aura != null) aura.Hide(); }
    void OnDestroy() { if (aura != null) aura.Dispose(); aura = null; }

    // ---- 2026-10-30 視認性: 体の大きさ(当たり判定から。毎フレーム探さない)
    Collider2D bodyCol;
    float sizeCache = 1f; Bounds boundsCache; int boundsFrame = -1;
    public Bounds Bounds { get { RefreshBounds(); return boundsCache; } }
    public float Size { get { RefreshBounds(); return sizeCache; } }
    void RefreshBounds()
    {
        if (boundsFrame == Time.frameCount) return;
        boundsFrame = Time.frameCount;
        if (bodyCol == null || !bodyCol.enabled)
        {
            boundsCache = ElementVfx.BoundsOf(target);
            bodyCol = target is WildBossBase wb ? wb.DebugHurtCol : target != null ? target.GetComponentInChildren<Collider2D>() : null;
        }
        else boundsCache = bodyCol.bounds;
        sizeCache = Mathf.Clamp(Mathf.Max(boundsCache.size.x, boundsCache.size.y) / 1.6f, 0.8f, 3.2f);
    }
    int FireTier => ElementVfx.Tier(ElementType.Fire);
    public float BurnBoost => burnBoost;
    ElementAura aura;

    // 状態の目印(頭上の小さな菱形。炎=橙/出血=赤/冷気=水色/凍結=白)。2026-10-30 からは旧表示(-elemVfx old)の時だけ
    void UpdateMarker()
    {
        if (ElementVfx.New)
        {
            if (marker != null) marker.enabled = false;
            bool any = Burning || Chilled || Frozen || Bleeding || Charged || Windswept;
            if (!any) { if (aura != null) aura.Hide(); return; }
            if (aura == null) aura = new ElementAura(this);
            aura.Update(target);
            return;
        }
        Color? c = Frozen ? new Color(0.92f, 0.97f, 1f) : Burning ? new Color(1f, 0.55f, 0.15f) : Bleeding ? new Color(0.85f, 0.1f, 0.2f) : Chilled ? new Color(0.45f, 0.8f, 1f) : (Color?)null;
        if (c == null) { if (marker != null) marker.enabled = false; return; }
        if (marker == null)
        {
            var go = new GameObject("ElementMarker");
            go.transform.SetParent(transform, false);
            marker = go.AddComponent<SpriteRenderer>();
            marker.sprite = OneShotSpriteEffect.SoftDotSprite();
            marker.sortingOrder = RenderOrder.CombatFx;
        }
        var col = target != null ? target.GetComponentInChildren<Collider2D>() : null;
        Vector3 top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y + 0.35f, 0f) : transform.position + Vector3.up * 1.6f;
        marker.transform.position = top;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 10f);
        marker.transform.localScale = Vector3.one * 0.45f * pulse / Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
        marker.color = c.Value;
        marker.enabled = true;
    }
}

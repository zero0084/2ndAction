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
        if (burnLeft > 0f) burnBoost = Mathf.Min(0.5f, burnBoost + 0.1f); else burnBoost = 0f;
        burnDps = Mathf.Max(burnLeft > 0f ? burnDps : 0f, dps);
        burnLeft = Mathf.Max(burnLeft, duration);
    }

    public void AddShock(float seconds) { shockLeft = Mathf.Max(shockLeft, seconds); }

    public void AddBleed(float dps, float duration, float healFraction)
    {
        bleedDps = Mathf.Max(bleedLeft > 0f ? bleedDps : 0f, dps);
        bleedLeft = Mathf.Max(bleedLeft, duration);
        bleedHeal = Mathf.Max(bleedHeal, healFraction);
    }

    // 返り値: このChillで凍結したか
    public bool AddChill(float slow, float duration, int freezeStacks, float freezeDuration, int absoluteZeroLevel = 0)
    {
        absoluteZero = Mathf.Max(absoluteZero, absoluteZeroLevel);
        chillSlow = Mathf.Max(chillLeft > 0f ? chillSlow : 0f, slow);
        chillLeft = Mathf.Max(chillLeft, duration);
        if (freezeLeft > 0f || freezeStacks <= 0) return false;
        chillStacks++;
        if (chillStacks < freezeStacks) return false;
        chillStacks = 0;
        freezeLeft = freezeDuration;
        return true;
    }

    void Update()
    {
        if (target == null || !ElementSystem.IsAlive(target)) { if (burnLeft > 0f) DiedBurning = true; Clear(); return; }
        float dt = Time.deltaTime;
        if (chillLeft > 0f) { chillLeft -= dt; if (chillLeft <= 0f) { chillLeft = 0f; chillSlow = 0f; chillStacks = 0; } }
        if (freezeLeft > 0f) freezeLeft = Mathf.Max(0f, freezeLeft - dt);
        if (shockLeft > 0f) shockLeft = Mathf.Max(0f, shockLeft - dt);
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
                    ElementSystem.DealQuiet(target, n, burnLeft > 0f ? ElementType.Fire : ElementType.Blood);
                    if (bleed > 0f && bleedHeal > 0f)
                    {
                        healCarry += bleed * bleedHeal;
                        int heal = Mathf.FloorToInt(healCarry);
                        if (heal > 0) { healCarry -= heal; ElementSystem.HealFromBleed(heal); }
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
        burnLeft = bleedLeft = chillLeft = freezeLeft = shockLeft = 0f;
        burnBoost = 0f;
        chillStacks = 0;
        if (marker != null) marker.enabled = false;
    }

    // 状態の目印(頭上の小さな菱形。炎=橙/出血=赤/冷気=水色/凍結=白)。演出の最終形ではない
    void UpdateMarker()
    {
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

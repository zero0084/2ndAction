using System.Collections.Generic;
using UnityEngine;

// マップ専用カード 20 枚(2026-11-06、map_cards_v2.md)のプレイヤー側: 一時シールド・時間の強化・空中の数え・急降下の着地・攻撃/速度/BREAK の倍率。
//  数値は MapCardRules。カードの Lv はこのプレイヤーのカードの合計(CT)から読む(CPU の仲間は自分のカード)。
//  攻撃は「条件の枠」(CardStateCondition / CardTargetCondition)へ足して、既存の曲線を一度だけ通す。
//  移動速度は自動前進の倍率(CardAutoSpeedScale)。事故ダメージ(AccidentDamage)は走行速度で決まるので、そのまま 3 ハートになる。
public partial class PlayerController
{
    // ===================================================================== 一時シールド(LAST BASTION / OVERFLOW WARD)
    // 通常の Shield(ShieldCharges)とは別に数える。時間で消える。合計の上限 3 枚(MapCardRules.TempShieldTotalCap)
    public enum TempShieldSource { LastBastion, Overflow }
    struct TempShield { public float until; public TempShieldSource src; }
    readonly List<TempShield> tempShields = new List<TempShield>();
    public int TempShieldCount => tempShields.Count;
    public int TempShieldCountOf(TempShieldSource s) { int n = 0; foreach (var t in tempShields) if (t.src == s) n++; return n; }
    // 足せた枚数を返す(その出どころの上限 srcCap と、合計の上限まで)
    public int AddTempShields(int n, float seconds, TempShieldSource src, int srcCap)
    {
        int added = 0;
        while (added < n && tempShields.Count < MapCardRules.TempShieldTotalCap && TempShieldCountOf(src) < srcCap)
        {
            tempShields.Add(new TempShield { until = Time.time + seconds, src = src });
            added++;
        }
        MapCardRules.TempShieldsGiven += added;
        return added;
    }
    // 被弾を防ぐ(TryConsumeShield から。先に消えるものから使う)
    bool TryConsumeTempShield()
    {
        if (tempShields.Count == 0) return false;
        int best = 0;
        for (int i = 1; i < tempShields.Count; i++) if (tempShields[i].until < tempShields[best].until) best = i;
        tempShields.RemoveAt(best);
        MapCardRules.TempShieldsUsed++;
        return true;
    }

    // ===================================================================== 時間の強化
    float revengeUntil = -99f, airComboUntil = -99f, diveBoostUntil = -99f, secondRiseUntil = -99f, oneMoreUntil = -99f;
    int airComboHits; float airComboLastHit = -9f;
    float diveSeenAt = -99f;
    public bool RevengeActive => Time.time < revengeUntil;
    public bool AirComboActive => Time.time < airComboUntil;
    public bool DiveBoostActive => Time.time < diveBoostUntil;
    public bool SecondRiseActive => Time.time < secondRiseUntil;
    public bool OneMoreActive => Time.time < oneMoreUntil;
    public int AirComboHitCount => airComboHits;

    int MLv(EffectType t) => Mathf.Clamp(Mathf.RoundToInt(CT(t)), 0, 9);

    public void StartRevenge() { int lv = MLv(EffectType.RevengeArmorLevel); if (lv > 0) revengeUntil = Time.time + MapCardRules.RevengeSeconds(lv); }
    public void StartSecondRise() { secondRiseUntil = Time.time + MapCardRules.SecondRiseSeconds; }
    public void StartOneMore() { oneMoreUntil = Time.time + MapCardRules.OneMoreSeconds; }

    void MapCardsReset()
    {
        tempShields.Clear();
        revengeUntil = airComboUntil = diveBoostUntil = secondRiseUntil = oneMoreUntil = diveSeenAt = -99f;
        airComboHits = 0;
    }

    // 毎フレーム(CardTick から): 一時シールドの期限 / 着地で空中の数えを戻す / 急降下の着地
    void MapCardsTick()
    {
        for (int i = tempShields.Count - 1; i >= 0; i--) if (Time.time >= tempShields[i].until) tempShields.RemoveAt(i);
        if (!isGrounded)
        {
            if (IsAerialDiving) diveSeenAt = Time.time;
        }
        else if (!cardWasGrounded)
        {
            airComboHits = 0; // AIR COMBO: 着地で数え直し
            // DIVE BOOST: 急降下(真下への攻撃/竜騎士の突き下ろし/技の急降下)で着地した(急降下が終わってから 0.3 秒以内の着地も含む)
            int dv = MLv(EffectType.DiveBoostLevel);
            if (dv > 0 && (IsAerialDiving || Time.time - diveSeenAt < 0.3f)) { diveBoostUntil = Time.time + MapCardRules.DiveBoostSeconds; MapCardRules.DiveBoosts++; }
            diveSeenAt = -99f;
        }
    }

    // 空中で攻撃が当たった(OnCardAttackLanded から。手動の攻撃だけ。1振りで何体に当たっても1回)
    void MapCardsOnAirHit()
    {
        int lv = MLv(EffectType.AirComboLevel);
        if (lv <= 0 || Time.time - airComboLastHit <= 0.15f) return;
        airComboLastHit = Time.time;
        airComboHits++;
        if (airComboHits >= MapCardRules.AirComboHits(lv))
        {
            airComboHits = 0;
            airComboUntil = Time.time + MapCardRules.AirComboSeconds;
            MapCardRules.AirComboTriggers++;
        }
    }

    // ===================================================================== 攻撃(条件の枠)
    // 今の状態(CardStateCondition に足す)
    float MapCardsStatePct()
    {
        float c = 0f;
        int lv;
        if ((lv = MLv(EffectType.RevengeArmorLevel)) > 0 && RevengeActive) c += MapCardRules.RevengeAttack(lv);
        if ((lv = MLv(EffectType.AirComboLevel)) > 0 && AirComboActive) c += MapCardRules.AirComboAttack(lv);
        if ((lv = MLv(EffectType.DiveBoostLevel)) > 0 && DiveBoostActive) c += MapCardRules.DiveAttack(lv);
        if ((lv = MLv(EffectType.SonicEdgeLevel)) > 0 && CurrentRunKmh >= MapCardRules.SonicEdgeKmh) c += MapCardRules.SonicAttack(lv);
        if ((lv = MLv(EffectType.SecondRiseLevel)) > 0 && SecondRiseActive) c += MapCardRules.SecondRiseBuff(lv);
        if ((lv = MLv(EffectType.EndlessGrowthLevel)) > 0 && GameManager.Instance != null)
            c += Mathf.Min(MapCardRules.GrowthCap(lv), GameManager.Instance.MapGrowthKills * MapCardRules.GrowthPerKill(lv));
        if ((lv = MLv(EffectType.FinalDriveLevel)) > 0 && FinalDriveOn) c += MapCardRules.FinalAttack(lv);
        if ((lv = MLv(EffectType.OneMoreChanceLevel)) > 0 && OneMoreActive) c += MapCardRules.OneMoreAttack(lv);
        return c;
    }
    bool FinalDriveOn => GameManager.Instance != null && GameManager.Instance.MaxDistance >= MapCardRules.FinalDriveFromMeters;
    public float MapCardsStatePctNow => MapCardsStatePct(); // 確認用

    // 命中した相手(CardTargetCondition に足す): BREAK 中の敵(BREAK DOMINION)/ 空中で打ち上げた敵へ(LAUNCH MASTER)
    float MapCardsTargetPct(Component victim)
    {
        if (victim == null) return 0f;
        float c = 0f;
        MapCardRules.NoteHit(victim);
        int lv;
        if ((lv = MLv(EffectType.BreakDominionLevel)) > 0 && MapCardRules.IsBroken(victim)) c += MapCardRules.DominionAttack(lv);
        if ((lv = MLv(EffectType.LaunchMasterLevel)) > 0 && !isGrounded && MapCardRules.IsLaunched(victim)) c += MapCardRules.LaunchAttack(lv);
        return c;
    }

    // ===================================================================== 移動速度(自動前進の倍率)
    float MapCardsSpeedMul
    {
        get
        {
            if (cardT == null) return 1f;
            float s = 0f;
            int lv;
            if ((lv = MLv(EffectType.DiveBoostLevel)) > 0 && DiveBoostActive) s += MapCardRules.DiveSpeed(lv);
            if ((lv = MLv(EffectType.SecondRiseLevel)) > 0 && SecondRiseActive) s += MapCardRules.SecondRiseBuff(lv);
            if ((lv = MLv(EffectType.FinalDriveLevel)) > 0 && FinalDriveOn) s += MapCardRules.FinalSpeed(lv);
            if ((lv = MLv(EffectType.OneMoreChanceLevel)) > 0 && OneMoreActive) s += MapCardRules.OneMoreSpeed(lv);
            return 1f + s;
        }
    }
    public float MapCardsSpeedMulNow => MapCardsSpeedMul; // 確認用

    // ===================================================================== BREAK の溜まり(MapCardRules.BreakGainMul が読む)
    public float MapBreakBonus
    {
        get
        {
            if (cardT == null) return 0f;
            float b = 0f;
            int lv;
            if ((lv = MLv(EffectType.SkyBreakerLevel)) > 0 && !isGrounded) b += MapCardRules.SkyBreaker(lv);
            if ((lv = MLv(EffectType.SonicEdgeLevel)) > 0 && CurrentRunKmh >= MapCardRules.SonicEdgeKmh) b += MapCardRules.SonicBreak(lv);
            if ((lv = MLv(EffectType.FinalDriveLevel)) > 0 && FinalDriveOn) b += MapCardRules.FinalBreak(lv);
            if ((lv = MLv(EffectType.OneMoreChanceLevel)) > 0 && OneMoreActive) b += MapCardRules.OneMoreBreak(lv);
            return b;
        }
    }

    // ===================================================================== IRON HEART(Shield の回復の速さ)
    float MapCardsShieldRechargeMul
    {
        get
        {
            int lv = MLv(EffectType.IronHeartLevel);
            var gm = GameManager.Instance;
            return lv > 0 && gm != null ? 1f / (1f + MapCardRules.IronHeartBonus(lv, gm.maxLives)) : 1f;
        }
    }
}

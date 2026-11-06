using UnityEngine;

// マップ専用カード 20 枚(2026-11-06、map_cards_v2.md)のランの側: 回復の超過(OVERFLOW WARD / BLOOD ARMOR)・血の盾・
//  被弾の後(LAST BASTION / REVENGE ARMOR / SECOND RISE)・倒れる被弾(ONE MORE CHANCE)・BREAK(BREAK FEAST)・撃破の数(ENDLESS GROWTH)。
//  数値は MapCardRules。この状態はランの中だけ(ResetCardStatsForRun で消える)。プレイヤー側の強化/一時シールドは PlayerController.MapCards。
//  マルチ: HP を持つ端末(ソロ/HOST)の被弾・回復の入口だけで動く(JOIN の被弾は HOST が決める)
public partial class GameManager
{
    // ---- ランの状態
    public int MapGrowthKills { get; private set; }        // ENDLESS GROWTH: カードを持っている間に倒した数
    public int BloodShieldHearts { get; private set; }     // BLOOD ARMOR: 血の盾(ハート)
    public bool SecondRiseUsed { get; private set; }
    public bool OneMoreChanceUsed { get; private set; }
    float bloodCarryHp, bloodDecayAt;
    float overflowCarryHp, overflowReadyAt;
    float lastBastionReadyAt, revengeReadyAt, breakFeastReadyAt;

    void MapCardsResetRun()
    {
        MapGrowthKills = 0; BloodShieldHearts = 0; SecondRiseUsed = false; OneMoreChanceUsed = false;
        bloodCarryHp = 0f; bloodDecayAt = 0f; overflowCarryHp = 0f; overflowReadyAt = 0f;
        lastBastionReadyAt = revengeReadyAt = breakFeastReadyAt = 0f;
        MapCardRules.LastBrokenHitAt = -99f;
        MapCardRules.InvalidateDeck();
        CardProcs.ResetMapCards(); // SHIELD BURST の間隔
    }

    int MLv(EffectType t) => Mathf.Clamp(Mathf.RoundToInt(Card.Get(t)), 0, 9);

    // 毎フレーム(UpdateCardRunState): 血の盾が減る / 間隔で待っていた超過の回復を一時シールドへ
    void MapCardsTick()
    {
        if (BloodShieldHearts > 0)
        {
            if (MLv(EffectType.BloodArmorLevel) <= 0) { BloodShieldHearts = 0; bloodCarryHp = 0f; }
            else if (Time.time >= bloodDecayAt) { BloodShieldHearts--; bloodDecayAt = Time.time + MapCardRules.BloodShieldDecaySeconds; }
        }
        if (overflowCarryHp >= CombatScale.HpPerHeart) TryConvertOverflow();
    }

    // ===================================================================== 回復の超過(KitHeal / CardHeal が最大HPを超えた分)
    // drain = 吸収(命中の吸収・撃破の回復・出血の吸収)。吸収の超過は先に BLOOD ARMOR、入りきらない分を OVERFLOW WARD へ
    void MapCardsOnHealOverflow(int over, bool drain)
    {
        if (over <= 0) return;
        int per = CombatScale.HpPerHeart;
        int ba = MLv(EffectType.BloodArmorLevel);
        if (drain && ba > 0)
        {
            float pct = MapCardRules.BloodArmorPct(ba);
            int max = MapCardRules.BloodArmorMax(ba);
            int roomHearts = max - BloodShieldHearts;
            if (roomHearts > 0)
            {
                // 入る分だけ使う(使った超過 = 盾にした HP / 割合)
                float can = roomHearts * per - bloodCarryHp;
                float give = Mathf.Min(over * pct, Mathf.Max(0f, can));
                bloodCarryHp += give;
                over -= Mathf.CeilToInt(give / Mathf.Max(0.01f, pct));
                int n = Mathf.FloorToInt(bloodCarryHp / per + 1e-4f);
                if (n > 0)
                {
                    if (BloodShieldHearts == 0) bloodDecayAt = Time.time + MapCardRules.BloodShieldDecaySeconds;
                    BloodShieldHearts = Mathf.Min(max, BloodShieldHearts + n);
                    bloodCarryHp -= n * per;
                    MapCardRules.BloodShieldGained += n;
                }
            }
        }
        int ow = MLv(EffectType.OverflowWardLevel);
        if (ow > 0 && over > 0)
        {
            int cap = MapCardRules.OverflowMax(ow);
            overflowCarryHp = Mathf.Min(cap * per, overflowCarryHp + over * MapCardRules.OverflowPct(ow)); // 貯めすぎない(上限の枚数ぶんまで)
            TryConvertOverflow();
        }
    }

    // OVERFLOW WARD: ハート1つ分 = 一時シールド1枚。変換は 3 秒に1回(1回に入る分だけ)
    void TryConvertOverflow()
    {
        int ow = MLv(EffectType.OverflowWardLevel);
        var pc = PlayerController.Instance;
        if (ow <= 0 || pc == null) { overflowCarryHp = 0f; return; }
        if (Time.time < overflowReadyAt) return;
        int per = CombatScale.HpPerHeart;
        int n = Mathf.FloorToInt(overflowCarryHp / per + 1e-4f);
        if (n <= 0) return;
        int added = pc.AddTempShields(n, MapCardRules.OverflowShieldSeconds, PlayerController.TempShieldSource.Overflow, MapCardRules.OverflowMax(ow));
        if (added <= 0) return; // 上限: 貯めた分は次の機会まで(貯める量にも上限がある)
        overflowCarryHp -= added * per;
        overflowReadyAt = Time.time + MapCardRules.OverflowConvertInterval;
        MapCardRules.OverflowConversions++;
    }

    // ===================================================================== 被弾(TryDamagePlayer)
    // HP を減らす前: REVENGE ARMOR の軽減(ハート1つ、最低ハート1つ)→ 血の盾(ハート単位で消す)。0 になったら被弾なし
    int MapCardsReduceIncoming(int dmg)
    {
        int per = CombatScale.HpPerHeart;
        var pc = PlayerController.Instance;
        if (pc != null && pc.RevengeActive && MLv(EffectType.RevengeArmorLevel) > 0 && dmg > CombatScale.PlayerHit)
        {
            dmg = Mathf.Max(CombatScale.PlayerHit, dmg - per);
            MapCardRules.RevengeReduced++;
        }
        if (BloodShieldHearts > 0 && dmg > 0)
        {
            int need = Mathf.CeilToInt(dmg / (float)per);
            int use = Mathf.Min(need, BloodShieldHearts);
            BloodShieldHearts -= use;
            MapCardRules.BloodShieldUsed += use;
            dmg = Mathf.Max(0, dmg - use * per);
        }
        return dmg;
    }

    // HP を減らした後(まだ倒れていない): rawHit = 軽減の前の一撃の大きさ
    void MapCardsAfterDamage(int livesBefore, int rawHit)
    {
        var pc = PlayerController.Instance;
        int per = CombatScale.HpPerHeart;
        // REVENGE ARMOR: ハート2つ以上の被弾の後(ハート1つの接触では出ない)。8 秒に1回
        if (rawHit >= 2 * per && MLv(EffectType.RevengeArmorLevel) > 0 && Time.time >= revengeReadyAt && pc != null)
        {
            pc.StartRevenge();
            revengeReadyAt = Time.time + MapCardRules.RevengeCooldown;
            MapCardRules.RevengeTriggers++;
        }
        // LAST BASTION: HP が 30% 以下になった瞬間(上から下へ越えた時)
        int lb = MLv(EffectType.LastBastionLevel);
        float line = maxLives * MapCardRules.LastBastionHpFraction;
        if (lb > 0 && pc != null && livesBefore > line && Lives <= line && Lives > 0 && Time.time >= lastBastionReadyAt)
        {
            int n = MapCardRules.LastBastionShields(lb);
            if (pc.AddTempShields(n, MapCardRules.LastBastionShieldSeconds, PlayerController.TempShieldSource.LastBastion, n) > 0)
            {
                lastBastionReadyAt = Time.time + MapCardRules.LastBastionCooldown(lb);
                BossBattleHud.Banner("LAST BASTION", new Color(0.45f, 1f, 0.9f), 1.0f);
            }
        }
        // SECOND RISE: 1ランに1回、HP25% 以下で最大HPの割合を回復 + 8 秒の強化
        int sr = MLv(EffectType.SecondRiseLevel);
        if (sr > 0 && !SecondRiseUsed && Lives > 0 && Lives <= maxLives * MapCardRules.SecondRiseHpFraction)
        {
            SecondRiseUsed = true;
            KitHeal(Mathf.RoundToInt(maxLives * MapCardRules.SecondRiseHeal(sr)));
            if (pc != null) pc.StartSecondRise();
            MapCardRules.SecondRises++;
            BossBattleHud.Banner("SECOND RISE", new Color(1f, 0.9f, 0.55f), 1.2f);
        }
    }

    // ONE MORE CHANCE: 倒れる被弾(Lives <= 0)の時、PHOENIX / FINAL EVOLUTION の緊急復活より先に。1ランに1回。
    //  ハート1つで残り(1.5 秒無敵)、10 秒の強化。使ったら true(同じ被弾で他の復活は出ない: Lives が 1 以上になるため)
    bool MapCardsTryOneMoreChance()
    {
        int lv = MLv(EffectType.OneMoreChanceLevel);
        if (lv <= 0 || OneMoreChanceUsed || Lives > 0) return false;
        OneMoreChanceUsed = true;
        Lives = Mathf.Min(maxLives, CombatScale.HpPerHeart);
        NetMatch.RequestSetMax(maxLives, false);
        GrantCardInvincible(MapCardRules.OneMoreInvincible);
        if (PlayerController.Instance != null) PlayerController.Instance.StartOneMore();
        MapCardRules.OneMoreChances++;
        BossBattleHud.Banner("ONE MORE CHANCE", new Color(1f, 0.95f, 0.7f), 1.4f);
        return true;
    }

    // ===================================================================== BREAK / 撃破
    // ボスを BREAK させた(MapCardRules.OnBossBreak)→ BREAK FEAST。20 秒に1回
    public void MapOnEnemyBreak()
    {
        int lv = MLv(EffectType.BreakFeastLevel);
        if (lv <= 0 || IsGameOver || Time.time < breakFeastReadyAt) return;
        breakFeastReadyAt = Time.time + MapCardRules.BreakFeastCooldown;
        KitHeal(MapCardRules.BreakFeastHearts(lv) * CombatScale.HpPerHeart);
        MapCardRules.BreakFeastHeals++;
    }

    // 敵/ボスを倒した(ENDLESS GROWTH の数。上限は攻撃の側で)
    public void MapOnKill()
    {
        if (MLv(EffectType.EndlessGrowthLevel) > 0) MapGrowthKills++;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(開発版の QA)
    public void QaSetBloodShield(int hearts) { BloodShieldHearts = Mathf.Max(0, hearts); bloodDecayAt = Time.time + MapCardRules.BloodShieldDecaySeconds; }
    public void QaResetMapCooldowns() { lastBastionReadyAt = revengeReadyAt = breakFeastReadyAt = overflowReadyAt = 0f; SecondRiseUsed = false; OneMoreChanceUsed = false; overflowCarryHp = 0f; bloodCarryHp = 0f; }
#endif
}

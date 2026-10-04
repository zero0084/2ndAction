using System.Collections.Generic;
using UnityEngine;

// カードバランス v3(2026-10-03): カードの値を「このランのカードLv」から毎回計算し直す。
//  ・Lv はカードの元の cardId ごと(合成カードは能力ごとに分解済み: ApplyRunCardCapped)。Lv9上限は従来どおり能力ごと。
//  ・RecomputeCardStats が Lv → 値の合計(Card)を作り、プレイヤー/属性/倍率へ反映する。取得のたびに丸めて足し込まない。
//  ・最大HP = 基本 + 成長(HEART UP など、上限ハート20) − 封印(HP犠牲カードが払った分)。
//    HEART UP の Lv は「成長の履歴」で、後から封印しても下がらない。封印は払えた分だけで、
//    払えなかったLvの攻撃などは効かない(下限ハート1に届いた後に、利点だけが伸びることはない)。払えない時は候補に出さない。
//  ・CARD BALANCE TEST の層(runSpeed などに直接掛ける)を壊さないように、プレイヤー/倍率へは「前の値との比」で反映する。
public partial class GameManager
{
    public readonly CardTotals Card = new CardTotals();
    readonly List<string> cardOrder = new List<string>();
    readonly Dictionary<string, int> cardLevels = new Dictionary<string, int>();
    readonly Dictionary<string, int> sacrificePaid = new Dictionary<string, int>();
    public int SealedHearts { get; private set; }
    public int CardMaxLivesBeforeSeal { get; private set; }
    int baseMaxLivesForRun = 50;
    int cardMaxLivesApplied = -1;
    float cardExpApplied = 1f, cardMileApplied = 1f, cardBossMileApplied = 1f;
    public static int CardRecomputes; // 確認用

    public IReadOnlyList<string> CardLevelOrder => cardOrder;
    public int CardLevel(string id) => id != null && cardLevels.TryGetValue(id, out int n) ? n : 0;
    public int SacrificePaidOf(string id) => id != null && sacrificePaid.TryGetValue(id, out int n) ? n : 0;

    // ラン開始(キャラの基本値を入れた直後)/ CARD TEST の RESET・キャラ切替
    void ResetCardStatsForRun(CharacterDefinition def)
    {
        cardOrder.Clear(); cardLevels.Clear(); sacrificePaid.Clear(); Card.Clear();
        runAbilityStacks.Clear(); // Lv9上限の数え(このランの能力ごとの回数)も新しいランとして
        runStartAbilityStacks.Clear();
        if (UltimateArt.Instance != null) UltimateArt.Instance.ResetRun(); // #100 ULTIMATE: Gauge / BUFF / 発動中の状態
        if (FinalEvolution.Instance != null) FinalEvolution.Instance.ResetRun(); // FINAL EVOLUTION(ランの中だけの状態)
        SealedHearts = 0;
        baseMaxLivesForRun = def != null ? def.baseMaxLives : maxLives;
        maxLivesCap = CardRules.MaxHeartsCap * CombatScale.HpPerHeart;
        cardMaxLivesApplied = maxLives;
        CardMaxLivesBeforeSeal = maxLives;
        expGainMultiplier = 1f; cardExpApplied = 1f;
        MileGainMultiplier = 1f; cardMileApplied = 1f;
        BossMileGainMultiplier = 1f; cardBossMileApplied = 1f;
        EnemySpawnRateMultiplier = 1f; EnemyHpMultiplier = 1f; BossHpMultiplier = 1f;
        lifestealChance = 0f; lifestealAmount = 0f;
        phoenixCharges = 0; phoenixConsumed = 0; phoenixResetHistoryIndex = -1;
        secondWindReadyDistance = 0f; lastChanceArmed = true; lastChanceUsedAt = -999f;
        CardInvincibleUntil = -1f;
        ChallengeSystem.ResetRun();
        if (PlayerController.Instance != null) PlayerController.Instance.ResetCardFactors();
    }

    static bool HasEffect(CardDefinition c, EffectType t)
    {
        if (c == null || c.effects == null) return false;
        foreach (var e in c.effects) if (e.type == t) return true;
        return false;
    }

    // 1枚のカード(元のカード)を n Lv 分。ApplyCardEffects / ApplyCardEffectsStacked から
    void AddCardLevel(CardDefinition card, int n)
    {
        if (card == null || n <= 0) return;
        string id = card.cardId;
        if (!cardLevels.ContainsKey(id)) { cardLevels[id] = 0; cardOrder.Add(id); }
        cardLevels[id] += n;
        RecomputeCardStats();
        // 最大HPが増えるカード: 従来どおり取得で全回復(「ハート上限が増え、HPが回復する」)
        if (HasEffect(card, EffectType.MaxHpHearts) || LegacyGrowsHp(card))
        {
            Lives = maxLives;
            NetMatch.RequestSetMax(maxLives, true);
        }
        // PHOENIX: 取得で復活の Charge 1(同時に1つまで)
        if (HasEffect(card, EffectType.PhoenixLevel)) phoenixCharges = 1;
    }

    static bool LegacyGrowsHp(CardDefinition c)
    {
        if (c == null || c.effects == null) return false;
        foreach (var e in c.effects) if (e.type == EffectType.MaxHp && e.value > 0f) return true;
        return false;
    }

    // HP犠牲: そのカードが Lv で求める封印(ハート)
    static int RequiredSacrifice(CardDefinition c, int lv)
    {
        float req = 0f;
        foreach (var e in c.effects)
        {
            if (e.type == EffectType.SacrificeHearts) req += CardRules.Scale(e.scaling, e.value, lv);
            else if (e.type == EffectType.MaxHp && e.value < 0f) req += -e.value / CombatScale.HpPerHeart * lv; // 旧形式
        }
        return Mathf.Max(0, Mathf.RoundToInt(req));
    }

    static bool IsSacrificeCard(CardDefinition c) => c != null && RequiredSacrifice(c, 9) > 0;

    // 払えた封印で効く Lv(払えていない Lv の利点は効かない)
    static int EffectiveLevel(CardDefinition c, int lv, int paid)
    {
        int k = lv;
        while (k > 0 && RequiredSacrifice(c, k) > paid) k--;
        return k;
    }

    // 今、封印に使える残りのハート(下限ハート1を残す)
    public int SacrificeAvailableHearts()
    {
        int per = CombatScale.HpPerHeart;
        return Mathf.Max(0, (CardMaxLivesBeforeSeal - CardRules.MinHearts * per) / per - SealedHearts);
    }

    // 次の1Lvを取って意味があるか(HP犠牲カードで、もう払えないのに利点だけ伸びる/何も起きない Lv は出さない)
    public bool SacrificeAllowsNextLevel(string cardId)
    {
        var c = CardDatabase.FindBaseById(MainAbilityOf(cardId));
        if (c == null || !IsSacrificeCard(c)) return true;
        int lv = CardLevel(c.cardId);
        int need = RequiredSacrifice(c, lv + 1) - SacrificePaidOf(c.cardId);
        if (need <= 0) return EffectiveLevel(c, lv, SacrificePaidOf(c.cardId)) >= lv; // 今のLvまで払えていれば、同じ段のLvは伸ばせる
        return SacrificeAvailableHearts() >= need;
    }

    public void RecomputeCardStats()
    {
        CardRecomputes++;
        Card.Clear();
        int per = CombatScale.HpPerHeart;

        // 1) 最大HPの成長(封印の前)
        float growthHearts = 0f;
        foreach (string id in cardOrder)
        {
            var c = CardDatabase.FindBaseById(id);
            int lv = CardLevel(id);
            if (c == null || lv <= 0) continue;
            foreach (var e in c.effects)
            {
                if (e.type == EffectType.MaxHpHearts) growthHearts += CardRules.Scale(e.scaling, e.value, lv);
                else if (e.type == EffectType.MaxHp && e.value > 0f) growthHearts += e.value / per * lv; // 旧形式
            }
        }
        int cap = CardRules.MaxHeartsCap * per;
        maxLivesCap = cap;
        CardMaxLivesBeforeSeal = Mathf.Clamp(baseMaxLivesForRun + Mathf.RoundToInt(growthHearts * per), CardRules.MinHearts * per, cap);

        // 2) 封印の支払い(足りなかった分は、HPに余裕ができた時に払う)
        int paidTotal = 0;
        foreach (var kv in sacrificePaid) paidTotal += kv.Value;
        int avail = Mathf.Max(0, (CardMaxLivesBeforeSeal - CardRules.MinHearts * per) / per - paidTotal);
        if (paidTotal > (CardMaxLivesBeforeSeal - CardRules.MinHearts * per) / per)
        {
            // 最大HPの成長が外れた(CARD TEST の RESET 等): 後のカードから払い戻す
            int over = paidTotal - (CardMaxLivesBeforeSeal - CardRules.MinHearts * per) / per;
            for (int i = cardOrder.Count - 1; i >= 0 && over > 0; i--)
            {
                string id = cardOrder[i];
                int p = SacrificePaidOf(id);
                int back = Mathf.Min(p, over);
                if (back > 0) { sacrificePaid[id] = p - back; over -= back; }
            }
            avail = 0;
        }
        foreach (string id in cardOrder)
        {
            var c = CardDatabase.FindBaseById(id);
            int lv = CardLevel(id);
            if (c == null || lv <= 0 || !IsSacrificeCard(c)) continue;
            int req = RequiredSacrifice(c, lv);
            int paid = SacrificePaidOf(id);
            if (paid < req && avail > 0)
            {
                int pay = Mathf.Min(req - paid, avail);
                sacrificePaid[id] = paid + pay;
                avail -= pay;
            }
        }

        // 3) 値の合計
        int sealedNow = 0;
        foreach (string id in cardOrder)
        {
            var c = CardDatabase.FindBaseById(id);
            int lv = CardLevel(id);
            if (c == null || lv <= 0) continue;
            int effLv = lv;
            if (IsSacrificeCard(c))
            {
                int paid = SacrificePaidOf(id);
                sealedNow += paid;
                effLv = EffectiveLevel(c, lv, paid);
            }
            foreach (var e in c.effects)
            {
                if (e.type == EffectType.SacrificeHearts) continue;
                float amount = CardRules.Scale(e.scaling, e.value, effLv);
                AddLegacyOrV3(e.type, amount, per);
            }
        }
        SealedHearts = sealedNow;

        // 4) 最大HP(CARD TEST の層を壊さないように差分で)
        int newMax = Mathf.Max(CardRules.MinHearts * per, CardMaxLivesBeforeSeal - SealedHearts * per);
        if (cardMaxLivesApplied < 0) cardMaxLivesApplied = maxLives;
        int delta = newMax - cardMaxLivesApplied;
        cardMaxLivesApplied = newMax;
        if (delta != 0)
        {
            maxLives = Mathf.Clamp(maxLives + delta, 1, cap);
            if (Lives > maxLives) Lives = maxLives;
            NetMatch.RequestSetMax(maxLives, false);
        }

        // 5) 倍率(EXP / MILE は CARD TEST の層を壊さないように比で)
        // EXP(2026-10-04): カードの EXP は ExpMultDistance / ExpMultKill(1つの枠 + 曲線)で掛ける。expGainMultiplier は CARD TEST の層だけ
        expGainMultiplier = ApplyRatio(expGainMultiplier, ref cardExpApplied, 1f);
        MileGainMultiplier = ApplyRatio(MileGainMultiplier, ref cardMileApplied, Mathf.Max(0f, 1f + Card.Get(EffectType.MileGainMultiplier)));
        BossMileGainMultiplier = ApplyRatio(BossMileGainMultiplier, ref cardBossMileApplied, Mathf.Max(0f, 1f + Card.Get(EffectType.BossMileGainMultiplier)));
        EnemySpawnRateMultiplier = Mathf.Max(0.1f, 1f + Card.Get(EffectType.EnemySpawnRate));
        EnemyHpMultiplier = Mathf.Max(0.1f, 1f + Card.Get(EffectType.EnemyHpMultiplier));
        BossHpMultiplier = Mathf.Max(0.1f, 1f + Card.Get(EffectType.BossHpMultiplier));
        lifestealChance = Mathf.Clamp(Card.Get(EffectType.LifestealChance), 0f, CardRules.LifestealChanceCap);
        lifestealAmount = lifestealChance > 0f ? HealHearts(CardRules.BaseHealHearts) * per : 0f;

        if (PlayerController.Instance != null) PlayerController.Instance.ApplyCardTotals(Card);
    }

    static float ApplyRatio(float current, ref float applied, float target)
    {
        float r = applied > 1e-6f ? target / applied : target;
        applied = target;
        return applied > 1e-6f && current > 1e-6f ? current * r : target;
    }

    // 旧形式の EffectType(v3 より前のアセット)を v3 の枠へ読み替える。v3 のものはそのまま
    void AddLegacyOrV3(EffectType t, float amount, int per)
    {
        switch (t)
        {
            case EffectType.MoveSpeed: Card.Add(EffectType.SpeedPct, amount); break;
            case EffectType.AttackPower: Card.Add(EffectType.AttackPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.JumpPower: Card.Add(EffectType.JumpPct, amount); break;
            case EffectType.MaxHp: break; // 成長/封印は上で扱った
            case EffectType.AttackRange: Card.Add(EffectType.RangePct, amount); break;
            case EffectType.AttackSpeed: Card.Add(EffectType.AttackSpeedPct, amount); break;
            case EffectType.AirAttackPower: Card.Add(EffectType.AirPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.GroundAttackPower: Card.Add(EffectType.GroundPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.ComboFinalStageBonus: Card.Add(EffectType.FinisherPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.FirstHitBonus: Card.Add(EffectType.FirstPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.LowHpAttackBonus: Card.Add(EffectType.LowHp50Pct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.FullHpAttackBonus: Card.Add(EffectType.FullHpPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.MomentumBonus: Card.Add(EffectType.MomentumPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.BossDamageBonus: Card.Add(EffectType.BossPct, amount / CardRules.ProcReferenceDamage); break;
            case EffectType.Shield: Card.Add(EffectType.ShieldCapacity, amount); break;
            case EffectType.LifestealAmount: Card.Add(EffectType.HealBonusHearts, amount / per); break;
            default: Card.Add(t, amount); break;
        }
    }

    // 回復量(ハート) = 基本 + OVERHEAL などの追加
    public int HealHearts(int baseHearts) => Mathf.Max(0, baseHearts + Mathf.FloorToInt(Card.Get(EffectType.HealBonusHearts) + 1e-4f));

    // ===================================================================== 回復 / 防御 / 復活

    // カードの効果による短い無敵(LAST CHANCE / PHOENIX / PERFECT GUARD)。被弾の入口で見る
    public float CardInvincibleUntil { get; private set; } = -1f;
    public bool CardInvincibleActive => Time.time < CardInvincibleUntil;
    void GrantCardInvincible(float seconds) { CardInvincibleUntil = Mathf.Max(CardInvincibleUntil, Time.time + seconds); }

    int phoenixCharges, phoenixConsumed, phoenixResetHistoryIndex = -1;
    public int PhoenixCharges => phoenixCharges;
    public int PhoenixConsumedCount => phoenixConsumed;
    float secondWindReadyDistance;
    public float SecondWindReadyDistance => secondWindReadyDistance;
    bool lastChanceArmed = true;
    float lastChanceUsedAt = -999f;
    public bool LastChanceArmed => lastChanceArmed;
    public static int PhoenixRevives, SecondWinds, LastChances, ShieldBlocks, OverhealShields, HeavyArmorSaves; // 確認用

    // 吸収/SECOND WIND など、まとまった回復(OVERHEAL: 満タンで溢れた分は Shield 1 へ)
    public void CardHeal(int amount)
    {
        if (amount <= 0 || IsGameOver) return;
        int before = Lives;
        AddLife(amount);
        if (before + amount > maxLives && Card.Get(EffectType.OverhealLevel) > 0f && PlayerController.Instance != null)
        {
            if (PlayerController.Instance.AddOverhealShield()) OverhealShields++;
        }
        if (before + amount > maxLives) FinalEvolution.OnOverheal(); // FINAL EVOLUTION(VAMPIRE): 溢れた分は Blood Shield(上限あり)
    }

    // Shield で防いだ(TryDamagePlayer / NetPrecheckDamage)
    void OnShieldBlocked()
    {
        ShieldBlocks++;
        var pc = PlayerController.Instance;
        int pg = Mathf.RoundToInt(Card.Get(EffectType.PerfectGuardLevel));
        if (pg > 0) GrantCardInvincible(CardRules.PerfectGuardBaseSeconds + CardRules.PerfectGuardPerLevel * pg);
        if (pc != null) CardProcs.OnShieldBlock(pc);
    }

    // 被弾の後(HPが減った後、まだ倒れていない)
    void AfterPlayerDamaged()
    {
        int per = CombatScale.HpPerHeart;
        // LAST CHANCE: HPがハート1つになった瞬間に短い無敵。HPが2つ以上へ回復し、かつ一定時間たつまで次は出ない
        int lc = Mathf.RoundToInt(Card.Get(EffectType.LastChanceLevel));
        if (lc > 0 && lastChanceArmed && Lives > 0 && Lives <= per)
        {
            lastChanceArmed = false;
            lastChanceUsedAt = Time.time;
            GrantCardInvincible(CardRules.LastChanceBaseSeconds + CardRules.LastChancePerLevel * lc);
            LastChances++;
            BossBattleHud.Banner("LAST CHANCE", new Color(1f, 0.85f, 0.4f), 1.2f);
        }
        // SECOND WIND: HPが30%以下に落ちたら少し回復。距離のクールダウン
        int sw = Mathf.RoundToInt(Card.Get(EffectType.SecondWindLevel));
        if (sw > 0 && Lives > 0 && Lives <= Mathf.CeilToInt(maxLives * CardRules.SecondWindHpFraction) && MaxDistance >= secondWindReadyDistance)
        {
            int hearts = 1 + (sw >= 5 ? 1 : 0) + (sw >= 9 ? 1 : 0);
            CardHeal(HealHearts(hearts) * per);
            secondWindReadyDistance = MaxDistance + Mathf.Max(1000f, CardRules.SecondWindBaseCooldownMeters - CardRules.SecondWindCooldownPerLevel * (sw - 1));
            SecondWinds++;
            BossBattleHud.Banner("SECOND WIND", new Color(0.6f, 1f, 0.7f), 1.2f);
        }
    }

    void UpdateCardRunState()
    {
        if (!lastChanceArmed && Lives > CombatScale.HpPerHeart && Time.time - lastChanceUsedAt >= CardRules.LastChanceRearmSeconds) lastChanceArmed = true;
    }

    // PHOENIX: 倒れる被弾を取り消す。Charge を使い、PHOENIX はこのランの取得状態から外れる(候補に再び出る。取り直しは Lv1 から)
    bool TryPhoenix(string reason)
    {
        if (phoenixCharges <= 0) return false;
        int per = CombatScale.HpPerHeart;
        int hearts = Mathf.Max(1, Mathf.RoundToInt(Card.Get(EffectType.PhoenixLevel)));
        phoenixCharges = 0;
        phoenixConsumed++;
        phoenixResetHistoryIndex = upgradeHistory.Count;
        ResetPhoenixLevels();
        Lives = Mathf.Clamp(hearts * per, 1, maxLives);
        NetMatch.RequestSetMax(maxLives, false);
        GrantCardInvincible(CardRules.PhoenixInvincibleSeconds);
        PhoenixRevives++;
        FreezeDiagnostics.LogEvent($"[Phoenix] revived reason={reason} hp={Lives}");
        BossBattleHud.Banner("PHOENIX", new Color(1f, 0.55f, 0.2f), 1.6f);
        if (PlayerController.Instance != null) CardProcs.PhoenixBurst(PlayerController.Instance.transform.position);
        return true;
    }

    void ResetPhoenixLevels()
    {
        foreach (string id in cardOrder.ToArray())
        {
            var c = CardDatabase.FindBaseById(id);
            if (c == null || !HasEffect(c, EffectType.PhoenixLevel)) continue;
            cardLevels[id] = 0;
            ResetAbilityRunStack(id);
        }
        RecomputeCardStats();
    }

    // CONTINUE: 取得のやり直しの後で、PHOENIX の消費などを中断時の状態へ
    void RestoreCardRunState(RunCheckpoint.Data data)
    {
        phoenixConsumed = data.phoenixConsumed;
        phoenixResetHistoryIndex = data.phoenixResetHistoryIndex;
        if (phoenixConsumed > 0)
        {
            // 最後に復活した後で取り直した分だけを PHOENIX の Lv にする(キャラカード分は開始時の1回だけ)
            int picks = 0;
            string pid = null;
            for (int i = Mathf.Max(0, phoenixResetHistoryIndex); i < upgradeHistory.Count; i++)
                if (upgradeHistory[i] != null && HasEffect(upgradeHistory[i], EffectType.PhoenixLevel)) { picks++; pid = upgradeHistory[i].cardId; }
            ResetPhoenixLevels();
            if (pid != null && picks > 0)
            {
                var c = CardDatabase.FindBaseById(pid);
                if (c != null) { ApplyRunCardCapped(c, picks, "CONTINUE phoenix"); }
            }
            phoenixCharges = picks > 0 ? 1 : 0;
        }
        secondWindReadyDistance = data.secondWindReadyDistance;
        lastChanceArmed = !data.lastChanceSpent;
    }

    void ExportCardRunState(RunCheckpoint.Data data)
    {
        data.phoenixConsumed = phoenixConsumed;
        data.phoenixResetHistoryIndex = phoenixResetHistoryIndex;
        data.secondWindReadyDistance = secondWindReadyDistance;
        data.lastChanceSpent = !lastChanceArmed;
    }
}

public partial class GameManager
{
    // EXP(2026-10-04): カードの EXP の強化は1つの枠に足してから、一度だけ曲線(CardRules.ExpMultiplier)を通す。
    //   距離の EXP  = 全体(EXP UP / LEVEL BREAK / LONG HAUL / THE LONG ROAD / ONE MORE MILE / MONSTER RUSH / EXP CONVERTER)+ 距離(PATHFINDER)
    //   撃破の EXP  = 全体 + 撃破/ボス/BONUS(EXPERIENCE BURST / TOUGH ENEMIES / FAST ENEMIES / HORDE)
    //   以前は「全体」と「距離/撃破」を掛け合わせていた(2枚目以降が掛け算で伸びた)。今は足し算 → 曲線で、取得順でも変わらない
    public float ExpBucketDistance => Card.Get(EffectType.ExpGain) + Card.Get(EffectType.DistanceExpPct) + FinalEvolution.ExpBucketBonus; // FINAL EVOLUTION(EXP UP)も同じ枠 → 曲線
    public float ExpBucketKill => Card.Get(EffectType.ExpGain) + Card.Get(EffectType.KillExpPct) + FinalEvolution.ExpBucketBonus;
    public float ExpMultDistance => CardRules.ExpMultiplier(ExpBucketDistance);
    public float ExpMultKill => CardRules.ExpMultiplier(ExpBucketKill);
    float KillExpScale => ExpMultKill;
}

public partial class GameManager
{
    // キャラカードの装備の結果の説明(DeckEditUI が表示する)
    public string LastEquipMessage { get; private set; } = "";

    // そのカードを slot へ Lv level で入れた時、他の枠と合わせて Lv9 を超える能力の超過分(最大の能力)
    public int CharacterCardOverflow(int slot, string cardId, int level, out string abilityName, out bool singleAbility)
    {
        abilityName = ""; singleAbility = true;
        var want = AbilitiesOf(cardId);
        singleAbility = want.Count <= 1;
        int times = CardVariant.IsVariantKey(cardId) ? 1 : Mathf.Max(1, level);
        int worst = 0;
        foreach (var a in want)
        {
            int total = a.stacks * times;
            for (int i = 0; i < CharacterCardSlotCount; i++)
            {
                if (i == slot || string.IsNullOrEmpty(characterCardIds[i])) continue;
                int t = CardVariant.IsVariantKey(characterCardIds[i]) ? 1 : Mathf.Max(1, characterCardLevels[i]);
                foreach (var b in AbilitiesOf(characterCardIds[i])) if (b.id == a.id) total += b.stacks * t;
            }
            int over = total - MaxRunCardLevel;
            if (over > worst) { worst = over; abilityName = CardVariant.AbilityName(a.id); }
        }
        return worst;
    }
}

// Every atomic gameplay effect a card can apply. A CardDefinition holds a
// list of (EffectType, value) pairs rather than one fixed effect, so a card
// like BERSERKER can combine an AttackPower buff with a MaxHp penalty in a
// single asset - no code change needed, just two CardEffect entries.
//
// Adding a genuinely new kind of effect still requires a case in
// GameManager.ApplyCardEffects (and whatever gameplay hook it drives), but
// any new card that only recombines existing EffectTypes needs nothing but
// a new CardDefinition asset - see CardDatabaseBuilder.
public enum EffectType
{
    // Multiplicative delta on PlayerController.runSpeed (e.g. +0.12 = +12%).
    MoveSpeed,
    // Flat delta added to PlayerController.AttackPower.
    AttackPower,
    // Multiplicative delta on PlayerController.jumpForce (e.g. +0.15 = +15%).
    JumpPower,
    // Flat int delta on PlayerController.maxJumps (extra air jumps).
    JumpCount,
    // Flat int delta on GameManager.maxLives (capped at maxLivesCap) - also
    // immediately heals up to the new cap, same as the original Heal card.
    MaxHp,
    // Multiplicative delta on the attack hitbox's size/reach.
    AttackRange,
    // Fractional speed-up of the attack animation/cooldown (e.g. 0.15 =
    // 15% faster combo tempo). Stacks multiplicatively across cards.
    AttackSpeed,
    // Flat delta added to AttackPower, but only while airborne.
    AirAttackPower,
    // Flat int number of shield charges added - each absorbs exactly one
    // hit that would otherwise cost a life.
    Shield,
    // Multiplicative delta on all EXP gained (distance, kills, bosses).
    ExpGain,
    // Probability [0..1] added to the per-kill lifesteal chance.
    LifestealChance,
    // Flat HP restored per successful lifesteal proc.
    LifestealAmount,
    // Multiplicative delta on how often enemy walls spawn (higher = more
    // frequent, i.e. more dangerous).
    EnemySpawnRate,

    // ===== Card Expansion/Gacha Evolution Ver.1 additions ===== //
    // Deliberately a modest, reusable set rather than one bespoke
    // EffectType per new card - most of the ~68 new cards this pass added
    // map onto some combination of these (see CardDatabaseBuilder's Specs
    // for exactly which). A few of the more thematically unique cards
    // (elemental status effects, true chain-lightning, etc.) intentionally
    // use a SIMPLIFIED placeholder mapping onto these same types rather
    // than a new bespoke mechanic - disclosed in each Spec's own comment.

    // Flat delta added to AttackPower, but only while GROUNDED (mirrors
    // AirAttackPower's own "only while airborne" - see PlayerController.
    // EffectiveAttackPower).
    GroundAttackPower,
    // Flat delta added to AttackPower only on the FINAL stage of the combo
    // chain (comboCount >= maxComboChain).
    ComboFinalStageBonus,
    // Flat delta added to AttackPower only on the FIRST hit of a fresh
    // combo (comboCount == 1).
    FirstHitBonus,
    // Flat delta added to AttackPower, scaled by how much HP is currently
    // missing (0 at full HP, full value at 0 HP) - a "fights harder while
    // hurt" berserk-style bonus.
    LowHpAttackBonus,
    // Flat delta added to AttackPower only while at full HP (Lives ==
    // maxLives).
    FullHpAttackBonus,
    // Flat delta added to AttackPower, scaled by how much of the Speed Up
    // ramp (PlayerController.GetSpeedMultiplier) has accumulated - "the
    // longer you've been running fast, the harder you hit".
    MomentumBonus,
    // Flat delta added ONLY when the target is a Boss (Dragon/Majin/
    // Mechanical Dragon) - see PlayerController.EffectiveBossAttackPower.
    BossDamageBonus,
    // Multiplicative delta (additive to a 1.0 baseline) on every grunt
    // Enemy's HP - see DistanceTierManager.EnemyHpFor.
    EnemyHpMultiplier,
    // Multiplicative delta (additive to a 1.0 baseline) on every Boss's
    // max HP - see BossManager.EffectiveBossMaxHp.
    BossHpMultiplier,
    // Multiplicative delta (additive to a 1.0 baseline) on MILE earned per
    // ordinary Enemy kill - see GameManager.RegisterEnemyKill.
    MileGainMultiplier,
    // Multiplicative delta (additive to a 1.0 baseline) on MILE earned per
    // Boss kill - see GameManager.RegisterBossDefeat.
    BossMileGainMultiplier,

    // ===== 属性(2026-10-03、ElementSystem / ElementStats 参照) =====
    // カードの取得でだけ付く。キャラ/敵の属性・弱点・相性は持たない。値はすべて加算で重なる。
    // 既存カードの数値はまだ入れていない(0=効果なし)。アセットには番号で保存されるので、必ず末尾に足すこと。
    // 炎: 命中時に確率で Burn(継続ダメージ)。Power = その命中のダメージに対する1秒あたりの割合
    BurnChance,
    BurnPower,
    BurnDuration,       // 秒(0なら ElementStats.DefaultBurnDuration)
    // 氷: 命中時に確率で Chill(行動が遅くなる)。蓄積で Freeze(雑魚は止まる/ボスは強めの減速に変換)
    ChillChance,
    ChillSlow,          // 0〜0.9(行動の遅さの割合)
    ChillDuration,      // 秒(0なら既定)
    FreezeStacks,       // この回数Chillが重なったらFreeze(0=Freezeなし)
    FreezeDuration,     // 秒(0なら既定)
    // 雷: 命中時に確率で落雷。近くの別の敵へ連鎖(Chain)
    LightningChance,
    LightningPower,     // その命中のダメージに対する割合(1体あたり)
    LightningChains,    // 連鎖する別の敵の数
    LightningRange,     // 連鎖の届く距離の追加(m)
    // 風: 命中時に確率で風刃(前方へ飛ぶ貫通の刃)。飛び道具の貫通の追加
    WindBladeChance,
    WindBladePower,     // 攻撃力に対する割合
    WindPierce,         // 飛び道具(と風刃)が貫通できる敵の数の追加
    WindRange,          // 風刃の飛ぶ距離の追加(m)
    // 血: 命中時に確率で Bleed(継続ダメージ)、Bleedのダメージの一部を回復、低HPほど強い
    BleedChance,
    BleedPower,
    BleedDuration,      // 秒(0なら既定)
    BleedLifesteal,     // Bleedのダメージに対する回復の割合
    BloodLowHpBonus,    // 失ったHPの割合 × この値 だけ Bleed が強くなる

    // ===== カードバランス v3(2026-10-03、GameManager.CardStats.cs / CardRules.cs) =====
    // 値はカードのLvから毎回計算し直す(CardEffect.scaling: 1Lvごと / 持っていれば一度 / 3Lvごと 等)。
    // 同じ系統は1つの枠へ足してから、枠ごとに一度だけ曲線(CardRules.Soft*)を通す(カードごとに掛け算しない)。
    // 属性の値(上の Burn*〜Blood*)は v3 で「その命中のダメージに対する割合」から「専用の基礎ダメージ」へ意味を変えた
    // (ElementSystem 参照。v3 より前にこれらを使うカードは無かった)。
    SpeedPct,               // 移動速度(加算 → CardRules.SoftSpeed)
    AttackPct,              // 攻撃力(無条件の枠 A → SoftAttack)
    JumpPct,                // ジャンプ力
    AttackSpeedPct,         // 攻撃速度(全部の攻撃)
    MainAttackSpeedPct,     // 攻撃速度(主攻撃=前/後の攻撃だけ。RAPID EDGE/COMBO RUSH)
    RangePct,               // 実効攻撃範囲(近接=判定 / 飛び道具=飛ぶ距離)
    MaxHpHearts,            // 最大HP(ハート。成長)
    SacrificeHearts,        // 最大HPを封印(ハート。払えた分だけ、そのカードの他の効果が効く)
    AirPct, GroundPct,      // 以下は条件の枠 C(命中ごとに足してから SoftCondition)
    FirstPct, ComboPct, FinisherPct,
    BossPct, MobPct,        // ボス / ボス以外
    AntiAirPct,             // 空中の敵(打ち上げ中/飛ぶ敵)
    DownPct,                // 下攻撃/叩きつけ/着地の衝撃
    FullHpPct,
    LowHp50Pct, LowHp25Pct, // HP50%以下 / さらに25%以下
    LowHpAttackSpeedPct,    // HP50%以下の攻撃速度(25%以下はこの2倍)
    LowHp25LifestealChance, // HP25%以下の吸収の確率
    SealedHeartPct,         // 封印したハート1つごと
    ComboEdgePct,           // 連続ヒット1つごと(最大 CardRules.ComboEdgeMaxSteps)
    AirDominionPct,         // 空中で当てた回数1つごと(着地まで、最大 AirDominionMaxStacks)
    MomentumPct,            // 100→150km/h で 0→この値
    OverdrivePct,           // OVERDRIVE 中の攻撃
    OverdriveAttackSpeedPct,// OVERDRIVE 中の攻撃速度
    OverdriveLevel,         // OVERDRIVE の Lv(発動までの時間)
    SonicBladeLevel,
    ShieldCapacity,         // Shield の最大数
    ShieldRecharge,         // Shield の回復の速さ(秒を短く)
    ShieldGuard,            // 防御カード(COUNTER 等)を持っていれば Shield 1 を保証
    CounterLevel, PerfectGuardLevel, FlameCounterLevel,
    SecondWindLevel, LastChanceLevel, PhoenixLevel,
    HealBonusHearts,        // 回復量の追加(ハート)
    OverhealLevel,          // 満タンで回復 → Shield
    PredatorLevel,          // ボスへの命中でも吸収の判定
    DoubleAttackChance,
    PierceLevel, ShockwaveLevel, ChainExplosionLevel, AerialBladeLevel, SkyMasterLevel,
    GroundBreakerLevel, ComboMasterLevel, HunterLevel, BrakeLevel,
    HurtInvincibleSeconds,  // 被弾後の無敵の延長(秒)
    HurtKnockbackReduce,    // 被弾ののけぞりの軽減(割合)
    HeavyArmorLevel,        // 強い一撃(ハート2つ以上)を1つ軽くする
    EnemyActionPct,         // 雑魚の行動の速さ
    EliteChance,            // 雑魚が精鋭になる確率
    BossRushLevel, WantedLevel,
    TreasureMilePct,        // BONUS ZONE などの宝・報酬の MILE
    DistanceMilePct,        // 距離の MILE
    DistanceExpPct,         // 距離の EXP
    KillExpPct,             // 撃破/ボス/BONUS の EXP
    // 属性 v3 の追加
    BurnPowerPct,           // 炎上の威力の割合
    InfernoLevel,
    FreezeThresholdReduce,  // 凍結までの冷気の回数を減らす
    AbsoluteZeroLevel,
    LightningDamagePct,
    LightningShock,         // 落雷で雑魚を一瞬止める(秒)
    LightningSplash,        // 落雷の周囲へのダメージの半径(m)
    WindSpeedPct,
    TornadoLevel,
    BloodBladeLevel,        // 満HPの間は出血の確率が2倍
    UltimateLevel           // #100 ULTIMATE(2026-10-04): キャラ固有の必殺技のLv(Gauge/威力/前進/BUFF)
}

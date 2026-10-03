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
    BloodLowHpBonus     // 失ったHPの割合 × この値 だけ Bleed が強くなる
}

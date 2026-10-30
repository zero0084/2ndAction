using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// カードバランス v3(2026-10-03)の全99枚 + #100 ULTIMATE(2026-10-04)のデータ(効果・説明・属性)。この表が数値の正本。
//  Tools/OneMoreMile/Apply Card Balance v3(または -executeMethod CardBalanceV3.Apply)で Resources/Cards の各アセットへ書き込む。
//  先に CardDatabaseBuilder.Build を呼んで、#92〜#99 の新しいアセットを作る(既存のアセットの ID/アイコン/レア度/解放距離は変えない)。
//  cardId は保存データ(所持/デッキ/キャラカード/中断データ)に入っているので変えない。旧 #79 ULTIMATE は表示名だけ ALMIGHTY
//  (cardId "ultimate" のまま)。#100 ULTIMATE(2026-10-04)は別の cardId "character_ultimate"(キャラ固有の必殺技。処理は Scripts/Ultimate)。
//  値の意味: E(種類, 値, 出し方)。出し方 = PerLevel(1Lvごと)/ Once(持っていれば)/ Every3(3Lvごと)/ Every2 / Phoenix / After3。
public static class CardBalanceV3
{
    public const int Version = 3;
    const string UltimateCardId = "character_ultimate"; // #100(UltimateArt.CardId と同じ)

    struct E
    {
        public EffectType t; public float v; public CardScaling s;
        public E(EffectType t, float v, CardScaling s = CardScaling.PerLevel) { this.t = t; this.v = v; this.s = s; }
    }

    struct C
    {
        public string id, name, desc; public ElementType el; public int rec; public E[] fx;
        public int rarity, stage; public float unlock; // 2026-10-30: 属性20枚の再設計でレア度と解放の順も表で持つ(0 = アセットのまま)
    }

    static C Card(string id, string desc, params E[] fx) => new C { id = id, desc = desc, fx = fx, el = (ElementType)(-1), rec = 0 };
    static C Card(string id, string desc, ElementType el, params E[] fx) => new C { id = id, desc = desc, fx = fx, el = el, rec = 0 };
    static C Named(C c, string name) { c.name = name; return c; }
    static C Rec(C c, int rec) { c.rec = rec; return c; }
    // 2026-10-30: レア度 / ガチャ段階(1〜5)/ 解放の BEST 距離(m)
    static C Rar(C c, int rarity, int stage, float unlock) { c.rarity = rarity; c.stage = stage; c.unlock = unlock; return c; }

    const CardScaling Once = CardScaling.Once, E3 = CardScaling.Every3, E2 = CardScaling.Every2, Ph = CardScaling.Phoenix, A3 = CardScaling.After3;

    static IEnumerable<C> Table()
    {
        // ---- 移動
        yield return Card("speed_up", "移動速度が上がる(1Lvごとに+3%)", new E(EffectType.SpeedPct, 0.03f));
        yield return Rec(Card("speed_down", "移動速度-4%/Lv。速さを抑えて走りたい時の特殊なカード", new E(EffectType.SpeedPct, -0.04f)), 1);
        yield return Card("jump_power_up", "ジャンプ力が上がる(1Lvごとに+3%)。魔法使いは高度の上下が速くなる", new E(EffectType.JumpPct, 0.03f));
        yield return Card("jump_count_up", "空中ジャンプ+1回/Lv。追加ジャンプを重ねると上昇力が弱まるが、穴へ落下中は弱まらない。魔法使いは上昇できる高度がLv1〜3で1段、Lv4〜6で2段、Lv7〜9で3段増える", new E(EffectType.JumpCount, 1f));
        yield return Card("sky_runner", "ジャンプ力+2%/Lv、空中での攻撃力+3%/Lv", new E(EffectType.AirPct, 0.03f), new E(EffectType.JumpPct, 0.02f));
        yield return Card("close_call", "移動速度(1Lvごとに+2%)と、被弾した後の無敵時間(1Lvごとに+0.05秒)が上がる", new E(EffectType.SpeedPct, 0.02f), new E(EffectType.HurtInvincibleSeconds, 0.05f));
        yield return Card("momentum", "走行速度に応じて攻撃力が上がる。100km/h以下では効果なし。150km/hで+4%/Lv、それ以上も増えるが伸びは1/4になる", new E(EffectType.MomentumPct, 0.04f));
        yield return Card("overdrive", "115km/h以上を一定時間保つとOVERDRIVE発動(攻撃力+3%/Lv、攻撃速度+2%/Lv)。107km/h未満が0.8秒続くと解除。移動速度は常時+2%/Lv",
            new E(EffectType.OverdrivePct, 0.03f), new E(EffectType.OverdriveAttackSpeedPct, 0.02f), new E(EffectType.SpeedPct, 0.02f), new E(EffectType.OverdriveLevel, 1f));

        // ---- 攻撃(無条件)
        yield return Card("attack_up", "攻撃力が上がる(1Lvごとに+5%)", new E(EffectType.AttackPct, 0.05f));
        yield return Rec(Card("brake_attack", "攻撃力+7%/Lv。ただし前攻撃・後攻撃を使うたび、0.4秒間だけ移動速度が10%下がる", new E(EffectType.AttackPct, 0.07f), new E(EffectType.BrakeLevel, 1f, Once)), 3);
        yield return Rec(Card("giant_slayer", "攻撃力が大きく上がる(1Lvごとに+9%)が、攻撃速度が下がる(1Lvごとに-3%)", new E(EffectType.AttackPct, 0.09f), new E(EffectType.AttackSpeedPct, -0.03f)), 2);
        yield return Rec(Card("reverse_gear", "攻撃力が上がる(1Lvごとに+8%)が、移動速度が下がる(1Lvごとに-3%)", new E(EffectType.AttackPct, 0.08f), new E(EffectType.SpeedPct, -0.03f)), 2);
        yield return Card("mob_killer", "ボス以外の敵への攻撃力+5%/Lv。敵撃破・BONUSのMILE+10%/Lv", new E(EffectType.MobPct, 0.05f), new E(EffectType.MileGainMultiplier, 0.10f));

        // ---- HP犠牲(封印)。払えた分だけ効く。払えない時は候補に出ない
        yield return Rec(Card("berserker", "攻撃力+6%/Lv、攻撃速度+1%/Lv。ただし最大HPのハートを封印する(Lv1〜3で1つ、Lv4〜6で2つ、Lv7〜9で3つ)", new E(EffectType.AttackPct, 0.06f), new E(EffectType.AttackSpeedPct, 0.01f), new E(EffectType.SacrificeHearts, 1f, E3)), 2);
        yield return Rec(Card("glass_cannon", "攻撃力が大きく上がる(1Lvごとに+9%)。最大HPのハートを2Lvごとに1つ封印する", new E(EffectType.AttackPct, 0.09f), new E(EffectType.SacrificeHearts, 1f, E2)), 1);
        yield return Rec(Card("heart_breaker", "攻撃力+4%/Lv。封印中のハート1つにつき、さらに攻撃力+0.6%/Lv(他のカードによる封印も対象)。自身も最大HPのハートを封印する(Lv1〜3で1つ、Lv4〜6で2つ、Lv7〜9で3つ)", new E(EffectType.AttackPct, 0.04f), new E(EffectType.SealedHeartPct, 0.006f), new E(EffectType.SacrificeHearts, 1f, E3)), 1);
        yield return Rec(Card("deaths_contract", "攻撃力+12%/Lv。ただし最大HPのハートを封印する(Lv1〜2で1つ、以降2Lvごとに1つ追加。Lv9で5つ)", new E(EffectType.AttackPct, 0.12f), new E(EffectType.SacrificeHearts, 1f, E2)), 1);

        // ---- 攻撃速度 / 範囲
        yield return Card("attack_speed_up", "攻撃速度が上がる(1Lvごとに+3%)", new E(EffectType.AttackSpeedPct, 0.03f));
        yield return Card("rapid_edge", "主攻撃(前/後)の攻撃速度が上がる(1Lvごとに+5%)。上/下/叩きつけ/大技には掛からない", new E(EffectType.MainAttackSpeedPct, 0.05f));
        yield return Card("double_attack", "攻撃したとき、確率(1Lvごとに+3%)で追加の1撃が出る。追加の1撃からは出ない", new E(EffectType.DoubleAttackChance, 0.03f));
        yield return Card("attack_range_up", "攻撃範囲+3%/Lv。近接攻撃は当たる範囲が広がり、飛び道具は飛ぶ距離が延びる(弾の大きさは変わらない)", new E(EffectType.RangePct, 0.03f));
        yield return Card("long_blade", "攻撃範囲+5%/Lv。近接攻撃は当たる範囲が広がり、飛び道具は飛ぶ距離が延びる", new E(EffectType.RangePct, 0.05f));
        yield return Card("piercing_blade", "近接攻撃が当たると、その敵の後ろにいる敵にも追加ダメージ。Lvで威力と対象数が増える。飛び道具の貫通数も増える(Lv1〜3で+1、Lv4〜6で+2、Lv7〜9で+3)", new E(EffectType.PierceLevel, 1f));
        yield return Card("shockwave", "近接の命中で前方へ短い衝撃波、飛び道具は着弾点のまわりに衝撃波(Lvで威力と範囲が上がる)", new E(EffectType.ShockwaveLevel, 1f));
        yield return Card("chain_explosion", "敵を倒すと周囲に爆発が発生する。爆発で倒した敵からも連鎖し、最大3段階まで広がる。同じ敵への命中は1連鎖につき1回", new E(EffectType.ChainExplosionLevel, 1f));

        // ---- 空中 / 地上
        yield return Card("air_attack_up", "空中での攻撃力が上がる(1Lvごとに+7%)", new E(EffectType.AirPct, 0.07f));
        yield return Card("aerial_blade", "空中で攻撃を当てると、命中地点に範囲攻撃の追い斬りが発生する。Lvで威力と範囲が上がる", new E(EffectType.AerialBladeLevel, 1f));
        yield return Card("air_dominion", "空中での攻撃力+3%/Lv。着地せずに攻撃を当てるたび、さらに+1%/Lv(最大4回)。着地すると追加分はリセット", new E(EffectType.AirPct, 0.03f), new E(EffectType.AirDominionPct, 0.01f));
        yield return Card("sky_master", "空中での攻撃力+4%/Lv。空中で攻撃を当てると、使った空中ジャンプを1回取り戻す(着地までにLv1〜3で1回、Lv4〜6で2回、Lv7〜9で3回まで)", new E(EffectType.AirPct, 0.04f), new E(EffectType.SkyMasterLevel, 1f));
        yield return Card("air_strike", "空中の敵(打ち上げた雑魚/飛ぶ雑魚。ボスはドラゴンと魔人だけ)への攻撃が上がる(+8%/Lv)", new E(EffectType.AntiAirPct, 0.08f));
        yield return Card("ground_fighter", "地上での攻撃力が上がる(1Lvごとに+7%)", new E(EffectType.GroundPct, 0.07f));
        yield return Card("heavy_impact", "下攻撃と叩きつけの着地の攻撃力+10%/Lv。締めの一撃も+3%/Lv", new E(EffectType.DownPct, 0.10f), new E(EffectType.FinisherPct, 0.03f));
        yield return Card("ground_breaker", "叩きつけの着地、または地上での締めの一撃で、地面を走る衝撃波が出る(Lvで威力と長さが上がる)。地上での攻撃力も+2%/Lv", new E(EffectType.GroundBreakerLevel, 1f), new E(EffectType.GroundPct, 0.02f));
        yield return Rec(Card("ground_zero", "地上での攻撃力が大きく上がる(1Lvごとに+11%)が、空中では下がる(1Lvごとに-5%)", new E(EffectType.GroundPct, 0.11f), new E(EffectType.AirPct, -0.05f)), 2);
        yield return Rec(Card("skybound", "空中での攻撃力が大きく上がる(1Lvごとに+11%)が、地上では下がる(1Lvごとに-5%)", new E(EffectType.AirPct, 0.11f), new E(EffectType.GroundPct, -0.05f)), 2);

        // ---- コンボ(初撃/連撃中/締め。単発のキャラは1.2秒以内の3回で1つの流れ)
        yield return Card("first_strike", "攻撃の流れの1撃目(初撃)が強くなる(1Lvごとに+10%)", new E(EffectType.FirstPct, 0.10f));
        yield return Card("combo_rush", "連撃の途中の攻撃が強くなり(1Lvごとに+6%)、主攻撃の攻撃速度も少し上がる(+2%/Lv)", new E(EffectType.ComboPct, 0.06f), new E(EffectType.MainAttackSpeedPct, 0.02f));
        yield return Card("combo_plus", "前攻撃・後攻撃を続けて出した時の最後の一撃(締め)の攻撃力+7%/Lv", new E(EffectType.FinisherPct, 0.07f));
        yield return Card("combo_edge", "連続ヒット(HIT数)が続くほど攻撃力が上がる(1ヒットごとに+0.4%/Lv、10ヒットまで)", new E(EffectType.ComboEdgePct, 0.004f));
        yield return Card("combo_master", "攻撃の流れの締めの一撃が当たると、前方に追加の衝撃が出る(Lvで威力と範囲が上がる)", new E(EffectType.ComboMasterLevel, 1f));
        yield return Card("sonic_blade", "前攻撃・後攻撃を使うと、前方へ敵を貫く斬撃が飛ぶ。走行速度が速いほど斬撃の飛距離・大きさ・威力が上がる", new E(EffectType.SonicBladeLevel, 1f));

        // ---- ボス
        yield return Card("boss_killer", "ボスへの攻撃力が上がる(1Lvごとに+8%)", new E(EffectType.BossPct, 0.08f));
        yield return Card("hunter", "ボス戦中、攻撃時の前後への踏み込み距離+4%/Lv。6mより遠いボスへ近づく時の移動速度も+3%/Lv", new E(EffectType.HunterLevel, 1f));

        // ---- HP / 防御
        yield return Card("heart_up", "最大HPがハート1つ/Lv増える(上限ハート20)。取得するとHPが全回復する", new E(EffectType.MaxHpHearts, 1f));
        yield return Rec(Card("fortress", "最大HPが増え(1Lvごとにハート+1)、被弾ののけぞりが減る(+5%/Lv)が、移動速度が下がる(1Lvごとに-3%)", new E(EffectType.MaxHpHearts, 1f), new E(EffectType.HurtKnockbackReduce, 0.05f), new E(EffectType.SpeedPct, -0.03f)), 2);
        yield return Rec(Card("heavy_armor", "ハート2つ以上のダメージを受けた時、ハート1つ分減らす。最大HPも増える(Lv1〜2でハート+1、以降2Lvごとに+1)。ジャンプ力-3%/Lv", new E(EffectType.MaxHpHearts, 1f, E2), new E(EffectType.HeavyArmorLevel, 1f, Once), new E(EffectType.JumpPct, -0.03f)), 2);
        yield return Card("iron_will", "HPが満タンの間、攻撃力が上がる(1Lvごとに+5%)。最大HPも増える(3Lvごとにハート+1)", new E(EffectType.FullHpPct, 0.05f), new E(EffectType.MaxHpHearts, 1f, E3));
        yield return Card("shield", "被弾を1回防ぐShield。最大数はLv1〜3で1つ、Lv4〜6で2つ、Lv7〜9で3つ。使った分は時間で1つずつ戻る(Lvで速くなる)", new E(EffectType.ShieldCapacity, 1f, E3), new E(EffectType.ShieldRecharge, 1.5f));
        yield return Card("counter", "Shieldで攻撃を防ぐと、周囲に反撃の衝撃波が発生する。Lvで威力が上がる。このカードだけでもShieldを1つ持てる", new E(EffectType.CounterLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("perfect_guard", "Shieldで被弾を防ぐと、短い無敵になる(0.4秒+0.1秒/Lv)。このカードだけでもShieldを1つ持てる", new E(EffectType.PerfectGuardLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("flame_counter", "Shieldで攻撃を防ぐと周囲に炎で反撃し、当たった敵を4秒間炎上させる。威力・範囲はLvで増加。単独でもShieldを1つ所持できる", ElementType.Fire, new E(EffectType.FlameCounterLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("last_chance", "ダメージでHPがハート1つ以下になった瞬間、短い無敵になる(1秒+0.15秒/Lv)。落下ダメージは防げない。次に出るのは、HPがハート1つより多く戻り、発動から20秒たった後", new E(EffectType.LastChanceLevel, 1f));
        yield return Card("second_wind", "被弾してHPが最大の30%以下なら、ハート1〜3個回復する(Lv5で2個、Lv9で3個)。再発動には一定距離の走行が必要(Lv1で10,000m、Lv9で6,000m)", new E(EffectType.SecondWindLevel, 1f));

        // ---- 血(吸収 / 出血 / 低HP / 復活)。入口 VAMPIRE ★2 → 発展 BLOOD BLADE / PREDATOR / OVERHEAL / BLOOD RUSH ★3 → 特殊 PHOENIX ★5
        yield return Rar(Card("vampire", "命中で与えたダメージの一部を吸収し、HPを回復する。吸収率はLv1で1%、以降+0.5%/Lv。敵撃破時にも確率でハート1つ回復：Lv1で8%、以降+4%/Lv", ElementType.Blood, new E(EffectType.HitLifestealPct, 0.005f), new E(EffectType.HitLifestealPct, 0.005f, Once), new E(EffectType.LifestealChance, 0.04f), new E(EffectType.LifestealChance, 0.04f, Once)), 2, 1, 0f);
        yield return Card("predator", "敵撃破時にハート1つ回復する確率+4%/Lv。その確率の1/4で、ボスへの命中時にも回復が発動する。ボスへの命中による吸収量は2倍", ElementType.Blood, new E(EffectType.LifestealChance, 0.04f), new E(EffectType.PredatorLevel, 1f, Once));
        yield return Card("overheal", "吸収とSECOND WINDの回復量が、Lv1〜3でハート+1、Lv4〜6で+2、Lv7〜9で+3。最大HPを超えた回復分はShield1つに変わり、通常の所持上限+1まで持てる", ElementType.Blood, new E(EffectType.HealBonusHearts, 1f, E3), new E(EffectType.OverhealLevel, 1f, Once));
        yield return Card("blood_rush", "HP50%以下で攻撃力+8%/Lv、命中による吸収量と出血の発動確率が2倍。HP25%以下では、敵撃破時にハート1つ回復する確率も+5%/Lv(ボスを含む)", ElementType.Blood, new E(EffectType.LowHp50Pct, 0.08f), new E(EffectType.LowHp25LifestealChance, 0.05f));
        yield return Rar(Card("blood_blade", "命中時、確率で出血を付与し、出血ダメージの一部でHPを回復する。回復率はLv1で10%、以降+2%/Lv。出血はLv1で確率15%・毎秒5ダメージ、以降1Lvごとに確率+4%・毎秒ダメージ+1。HP満タン時は出血確率2倍、攻撃力+2%/Lv", ElementType.Blood,
            new E(EffectType.BleedChance, 0.04f), new E(EffectType.BleedChance, 0.11f, Once), new E(EffectType.BleedPower, 1f), new E(EffectType.BleedPower, 4f, Once), new E(EffectType.BleedLifesteal, 0.02f), new E(EffectType.BleedLifesteal, 0.08f, Once),
            new E(EffectType.BloodBladeLevel, 1f, Once), new E(EffectType.FullHpPct, 0.02f)), 3, 2, 5000f);
        yield return Rec(Card("last_stand", "HP50%以下で攻撃力が上がり(1Lvごとに+4%)、HP25%以下でさらに上がる(+4%/Lv)", new E(EffectType.LowHp50Pct, 0.04f), new E(EffectType.LowHp25Pct, 0.04f)), 3);
        yield return Rec(Card("berserk_drive", "HP50%以下で攻撃力+5%/Lv、攻撃速度+2%/Lv。HP25%以下では両方の上昇量が2倍になる", new E(EffectType.LowHp50Pct, 0.05f), new E(EffectType.LowHp25Pct, 0.05f), new E(EffectType.LowHpAttackSpeedPct, 0.02f)), 2);
        yield return Rec(Card("adrenaline", "HP50%以下で攻撃速度+3%/Lv。HP25%以下では上昇量が2倍(+6%/Lv)", new E(EffectType.LowHpAttackSpeedPct, 0.03f)), 3);
        yield return Card("phoenix", "倒れた時(落下も含む)に復活し、2秒間無敵になる(HPはLvでハート1〜5)。復活するとこのカードは外れ、また候補に出る(取り直しはLv1から)", ElementType.Blood, new E(EffectType.PhoenixLevel, 1f, Ph));

        // ---- 万能
        yield return Named(Card("ultimate", "攻撃力+3%/Lv、移動速度+2%/Lv。最大HPも増える(Lv1〜2でハート+1、以降2Lvごとに+1。Lv9で+5)",
            new E(EffectType.AttackPct, 0.03f), new E(EffectType.MaxHpHearts, 1f, E2), new E(EffectType.SpeedPct, 0.02f)), "ALMIGHTY");

        // ---- 成長(EXP)
        yield return Card("exp_up", "得られる経験値が増える(1Lvごとに+20%)", new E(EffectType.ExpGain, 0.20f));
        yield return Card("pathfinder", "走行距離EXP+20%/Lv。敵撃破EXPには適用されない", new E(EffectType.DistanceExpPct, 0.20f));
        yield return Card("level_break", "獲得EXP+30%/Lv", new E(EffectType.ExpGain, 0.30f));
        yield return Card("experience_burst", "敵撃破・ボス撃破・BONUS ZONEのEXPが大きく増える(+80%/Lv)。走行距離EXPは対象外", new E(EffectType.KillExpPct, 0.80f));
        yield return Card("long_haul", "獲得EXP+15%/Lv。最大HPも増える(Lv1〜3でハート+1、Lv4〜6で+2、Lv7〜9で+3)", new E(EffectType.ExpGain, 0.15f), new E(EffectType.MaxHpHearts, 1f, E3));
        yield return Card("the_long_road", "獲得EXP+40%/Lv。最大HPも増える(Lv1〜2でハート+1、以降2Lvごとに+1。Lv9で+5)", new E(EffectType.ExpGain, 0.40f), new E(EffectType.MaxHpHearts, 1f, E2));
        yield return Rec(Card("exp_converter", "獲得EXPが減る(-10%/Lv、最低でも通常の25%)代わりに、敵撃破・BONUSのMILE+30%/Lv", new E(EffectType.MileGainMultiplier, 0.30f), new E(EffectType.ExpGain, -0.10f)), 2);

        // ---- MILE
        yield return Card("executioner", "敵撃破で得られるMILE+15%/Lv。BONUS ZONEとWANTEDの報酬MILEにも適用される", new E(EffectType.MileGainMultiplier, 0.15f));
        yield return Card("treasure_hunter", "宝・報酬のMILE+30%/Lv(BONUS ZONEの宝やCLEAR/PERFECT、WANTEDの賞金)。ボス撃破MILE+10%/Lv、走行距離MILE+5%/Lv",
            new E(EffectType.TreasureMilePct, 0.30f), new E(EffectType.BossMileGainMultiplier, 0.10f), new E(EffectType.DistanceMilePct, 0.05f));
        yield return Card("one_more_mile", "敵撃破・BONUSのMILE+50%/Lv、ボス撃破MILE+50%/Lv、獲得EXP+20%/Lv", new E(EffectType.MileGainMultiplier, 0.50f), new E(EffectType.BossMileGainMultiplier, 0.50f), new E(EffectType.ExpGain, 0.20f));

        // ---- Challenge / Risk(本当に難しくなる代わりに MILE)
        yield return Rec(Card("greed", "移動速度が上がる(1Lvごとに+4%)が、敵の出現が増える(+8%/Lv)", new E(EffectType.SpeedPct, 0.04f), new E(EffectType.EnemySpawnRate, 0.08f)), 2);
        yield return Rec(Card("no_turning_back", "移動速度+5%/Lv。ただし敵の出現頻度も+10%/Lv", new E(EffectType.SpeedPct, 0.05f), new E(EffectType.EnemySpawnRate, 0.10f)), 2);
        yield return Rec(Card("more_enemies", "敵の出現頻度+10%/Lv。敵撃破・BONUSのMILEも+3%/Lv", new E(EffectType.EnemySpawnRate, 0.10f), new E(EffectType.MileGainMultiplier, 0.03f)), 2);
        yield return Rec(Card("tough_enemies", "雑魚のHP+15%/Lv。代わりに敵撃破・BONUSのMILE+15%/Lv、敵撃破EXP+5%/Lv", new E(EffectType.EnemyHpMultiplier, 0.15f), new E(EffectType.MileGainMultiplier, 0.15f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("fast_enemies", "雑魚の動き・追尾・攻撃が速くなる(1Lvごとに+4%)代わりに、MILE(+15%/Lv)と撃破の経験値(+5%/Lv)が増える", new E(EffectType.EnemyActionPct, 0.04f), new E(EffectType.MileGainMultiplier, 0.15f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("elite_enemies", "雑魚の一部が精鋭になる(1Lvごとに+4%。HP×2.5・少し速い、倒すとMILE×3/経験値×2)。MILEも増える(+30%/Lv)", new E(EffectType.EliteChance, 0.04f), new E(EffectType.MileGainMultiplier, 0.30f)), 2);
        yield return Rec(Card("monster_rush", "敵の出現頻度+10%/Lv。代わりに獲得EXP+15%/Lv", new E(EffectType.EnemySpawnRate, 0.10f), new E(EffectType.ExpGain, 0.15f)), 2);
        yield return Rec(Card("horde", "敵の出現頻度+15%/Lv。代わりに敵撃破・BONUSのMILE+5%/Lv、撃破・BONUSのEXP+5%/Lv", new E(EffectType.EnemySpawnRate, 0.15f), new E(EffectType.MileGainMultiplier, 0.05f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("boss_challenge", "ボスのHPが上がる(1Lvごとに+10%)代わりに、ボスMILEが増える(+40%/Lv)", new E(EffectType.BossHpMultiplier, 0.10f), new E(EffectType.BossMileGainMultiplier, 0.40f)), 2);
        yield return Rec(Card("boss_rush", "通常のボス関門で、確率(8%/Lv)でボスがもう1体加わる。ボス撃破MILE+50%/Lv。10kmごとの専用ボスとラストダンジョンのボスラッシュには加わらない", new E(EffectType.BossMileGainMultiplier, 0.50f), new E(EffectType.BossRushLevel, 1f)), 2);
        yield return Rec(Card("wanted", "一定距離ごとに賞金首が現れる(Lvで強く・頻繁に)。倒せば賞金のMILE、置き去りにすると賞金なし。敵撃破・BONUSのMILE+35%/Lv", new E(EffectType.MileGainMultiplier, 0.35f), new E(EffectType.WantedLevel, 1f)), 2);
        yield return Rec(Card("hell_mode", "雑魚のHP+12%/Lv、行動速度+4%/Lv、出現頻度+8%/Lv。代わりに敵撃破・BONUSのMILE+50%/Lv、ボス撃破MILE+30%/Lv",
            new E(EffectType.MileGainMultiplier, 0.50f), new E(EffectType.EnemyHpMultiplier, 0.12f), new E(EffectType.EnemyActionPct, 0.04f), new E(EffectType.EnemySpawnRate, 0.08f), new E(EffectType.BossMileGainMultiplier, 0.30f)), 1);
        yield return Rec(Card("pandemonium", "雑魚のHP+15%/Lv、行動速度+5%/Lv、出現頻度+12%/Lv、精鋭の出現率+2%/Lv、ボスHP+10%/Lv。代わりに敵撃破・BONUSのMILE+60%/Lv、ボス撃破MILE+60%/Lv",
            new E(EffectType.MileGainMultiplier, 0.60f), new E(EffectType.BossMileGainMultiplier, 0.60f), new E(EffectType.EnemyHpMultiplier, 0.15f), new E(EffectType.EnemyActionPct, 0.05f),
            new E(EffectType.EnemySpawnRate, 0.12f), new E(EffectType.BossHpMultiplier, 0.10f), new E(EffectType.EliteChance, 0.02f)), 1);

        // ---- 属性: 炎(継続火力・延焼・雑魚処理)。入口 FLAME BLADE ★2 → 発展 BURNING SOUL ★3 → 完成 INFERNO ★4
        yield return Rar(Card("flame_blade", "命中時、確率で敵を3秒間炎上させる。Lv1：確率20%・毎秒6ダメージ。以降1Lvごとに確率+4%・毎秒ダメージ+1.5", ElementType.Fire,
            new E(EffectType.BurnChance, 0.04f), new E(EffectType.BurnChance, 0.16f, Once), new E(EffectType.BurnPower, 1.5f), new E(EffectType.BurnPower, 4.5f, Once)), 2, 1, 0f);
        yield return Rar(Card("burning_soul", "炎上ダメージ+8%/Lv、炎上時間+0.25秒/Lv。単独でも命中時に炎上を付与：確率はLv1で8%、以降+2%/Lv。毎秒ダメージも+0.5/Lv", ElementType.Fire,
            new E(EffectType.BurnPowerPct, 0.08f), new E(EffectType.BurnDuration, 0.25f), new E(EffectType.BurnChance, 0.02f), new E(EffectType.BurnChance, 0.06f, Once), new E(EffectType.BurnPower, 0.5f), new E(EffectType.BurnPower, 3.5f, Once)), 3, 2, 5000f);
        yield return Rar(Card("inferno", "炎上中の敵を倒すと周囲の敵に延焼し、延焼で倒れた敵からも広がる。延焼はLv1〜3で2段、Lv4〜6で3段、Lv7〜9で4段。威力・範囲もLvで増加。単独でも命中時に炎上を付与：確率はLv1で8%、以降+2%/Lv", ElementType.Fire,
            new E(EffectType.InfernoLevel, 1f), new E(EffectType.BurnChance, 0.02f), new E(EffectType.BurnChance, 0.06f, Once), new E(EffectType.BurnPower, 1f), new E(EffectType.BurnPower, 3f, Once)), 4, 3, 20000f);
        // ---- 氷(足止め・安全確保・BREAK補助。総ダメージは低くてよい)。入口 FROST EDGE ★2 → 発展 ICE PRISON ★3 → 完成 ABSOLUTE ZERO ★4
        yield return Rar(Card("frost_edge", "命中時、確率で敵に冷えを付与して減速させる。Lv1：確率20%・減速28%。以降1Lvごとに確率+4%・減速+2.5%・冷え時間+0.1秒。冷えが5回重なると凍結。雑魚は停止し、ボスは大きく減速してBREAKが溜まりやすくなる", ElementType.Ice,
            new E(EffectType.ChillChance, 0.04f), new E(EffectType.ChillChance, 0.16f, Once), new E(EffectType.ChillSlow, 0.025f), new E(EffectType.ChillSlow, 0.25f, Once), new E(EffectType.ChillDuration, 0.1f)), 2, 1, 0f);
        yield return Rar(Card("ice_prison", "凍結に必要な冷えの重なりを減らす：Lv1〜3で4回、Lv4〜6で3回、Lv7〜9で2回。凍結時間+0.08秒/Lv、冷え時間+0.15秒/Lv。単独でも命中時に冷えを付与：確率はLv1で8%、以降+2%/Lv", ElementType.Ice,
            new E(EffectType.FreezeThresholdReduce, 1f, E3), new E(EffectType.FreezeDuration, 0.08f), new E(EffectType.ChillChance, 0.02f), new E(EffectType.ChillChance, 0.06f, Once), new E(EffectType.ChillSlow, 0.20f, Once), new E(EffectType.ChillDuration, 0.15f)), 3, 2, 5000f);
        yield return Rar(Card("absolute_zero", "冷え・凍結中の敵への攻撃力+3%/Lv。凍結時に追加ダメージを与え、威力はLvで増加。冷え・凍結中のボスへのBREAK蓄積量+6%/Lv(冷えだけの間は半分)。単独でも命中時に冷えを付与し、4回重なると凍結：確率はLv1で8%、以降+2%/Lv", ElementType.Ice,
            new E(EffectType.AbsoluteZeroLevel, 1f), new E(EffectType.IceBreakPct, 0.06f), new E(EffectType.FreezeDuration, 0.06f), new E(EffectType.FreezeThresholdReduce, 1f, Once),
            new E(EffectType.ChillChance, 0.02f), new E(EffectType.ChillChance, 0.06f, Once), new E(EffectType.ChillSlow, 0.20f, Once)), 4, 3, 20000f);
        // ---- 雷(瞬間火力・連鎖・手数)。入口 THUNDER STRIKE ★2 → 発展 HIGH VOLTAGE / CHAIN LIGHTNING ★3 → 完成 THUNDER LORD ★4
        yield return Rar(Card("thunder_strike", "命中時、確率で落雷が発生。Lv1：確率8%・基礎威力9。以降1Lvごとに確率+2%・基礎威力+1.5。さらに一定の命中回数ごとに必ず落雷する(Lv1で13回、Lv9で5回)。同じ敵への落雷は0.4秒に1回まで", ElementType.Thunder,
            new E(EffectType.LightningChance, 0.02f), new E(EffectType.LightningChance, 0.06f, Once), new E(EffectType.LightningPower, 1.5f), new E(EffectType.LightningPower, 7.5f, Once), new E(EffectType.LightningCounterLevel, 1f)), 2, 1, 0f);
        yield return Rar(Card("high_voltage", "落雷の威力+6%/Lv。落雷が雑魚を一瞬停止させる。単独でも落雷が発生：確率はLv1で3%、以降+1%/Lv", ElementType.Thunder,
            new E(EffectType.LightningDamagePct, 0.06f), new E(EffectType.LightningChance, 0.01f), new E(EffectType.LightningChance, 0.02f, Once), new E(EffectType.LightningShock, 0.02f), new E(EffectType.LightningShock, 0.15f, Once),
            new E(EffectType.LightningPower, 0.3f), new E(EffectType.LightningPower, 2f, Once)), 3, 2, 5000f);
        yield return Card("chain_lightning", "落雷が近くの別の敵へ連鎖する：Lv1〜3で1体、Lv4〜6で2体、Lv7〜9で3体。連鎖距離+0.2m/Lv。連鎖先がいない場合、残りの連鎖は威力を落として同じ敵に落ちる。単独でも落雷が発生：確率はLv1で5%、以降+1%/Lv", ElementType.Thunder,
            new E(EffectType.LightningChains, 1f, E3), new E(EffectType.LightningRange, 0.2f), new E(EffectType.LightningChance, 0.01f), new E(EffectType.LightningChance, 0.04f, Once), new E(EffectType.LightningPower, 0.3f), new E(EffectType.LightningPower, 3f, Once));
        yield return Card("thunder_lord", "落雷の威力+4%/Lv。連鎖数はLv1〜3で+1体、Lv4〜6で+2体、Lv7〜9で+3体。落雷地点の周囲にもダメージを与え、範囲はLvで拡大。単独でも落雷が発生：確率はLv1で5%、以降+1%/Lv", ElementType.Thunder,
            new E(EffectType.LightningDamagePct, 0.04f), new E(EffectType.LightningChains, 1f, E3), new E(EffectType.LightningSplash, 0.08f), new E(EffectType.LightningSplash, 0.8f, Once),
            new E(EffectType.LightningChance, 0.01f), new E(EffectType.LightningChance, 0.04f, Once), new E(EffectType.LightningPower, 0.5f), new E(EffectType.LightningPower, 3f, Once));
        // ---- 風(空中戦・位置操作)。入口 WIND CUTTER ★2 → 発展 GALE ★3 → 完成 TORNADO ★4。空中では風刃が出やすく(×1.5)、空中の敵に風刃が強い(+50%)
        yield return Rar(Card("wind_cutter", "命中時、確率で前方に敵を貫通する風刃を放つ。Lv1：確率12%・威力9。以降1Lvごとに確率+3%・威力+1。空中での攻撃は発動確率1.5倍。風刃は空中の敵にダメージ+50%。攻撃範囲+1%/Lv", ElementType.Wind,
            new E(EffectType.WindBladeChance, 0.03f), new E(EffectType.WindBladeChance, 0.09f, Once), new E(EffectType.WindBladePower, 1f), new E(EffectType.WindBladePower, 8f, Once), new E(EffectType.RangePct, 0.01f)), 2, 1, 0f);
        yield return Card("gale", "風刃の速さ+5%/Lv、飛距離+0.4m/Lv。風刃と飛び道具の貫通数はLv1〜3で+1、Lv4〜6で+2、Lv7〜9で+3。単独でも風刃が発生：確率はLv1で6%、以降+2%/Lv", ElementType.Wind,
            new E(EffectType.WindSpeedPct, 0.05f), new E(EffectType.WindRange, 0.4f), new E(EffectType.WindPierce, 1f, E3), new E(EffectType.WindBladeChance, 0.02f), new E(EffectType.WindBladeChance, 0.04f, Once), new E(EffectType.WindBladePower, 1f), new E(EffectType.WindBladePower, 5f, Once));
        yield return Card("tornado", "風刃の命中地点に竜巻が発生。周囲の雑魚を引き寄せ、Lv1〜3で3回、Lv4〜6で4回、Lv7〜9で5回ダメージを与える。威力・範囲・引き寄せはLvで強化。ボスへのダメージは半分。単独でも風刃が発生：確率はLv1で6%、以降+2%/Lv", ElementType.Wind,
            new E(EffectType.TornadoLevel, 1f), new E(EffectType.WindBladeChance, 0.02f), new E(EffectType.WindBladeChance, 0.04f, Once), new E(EffectType.WindBladePower, 1f), new E(EffectType.WindBladePower, 5f, Once));

        // ---- #100 ULTIMATE(2026-10-04): 新しい cardId。数値は UltimateTuning(Lv1/5/9 の前進・威力・BUFF・Gauge の溜まりやすさ)
        yield return Named(Card(UltimateCardId, "Gaugeが100%で発動(左下のボタン)。キャラ固有の必殺技で画面内の通常の敵を一撃で倒し、ボスには最大HPの20〜40%のダメージ(Lvで上がる)。そのまま大きく前進し(Lv1 300m → Lv9 1000m。ボス関門などの手前で止まり、ボス戦中は前進しない)、しばらく強化が続く(Lv1 5秒 → Lv9 12秒)。Gaugeは距離・撃破・ボスへのダメージで溜まる",
            new E(EffectType.UltimateLevel, 1f)), "ULTIMATE");
    }

    public static int CardCount { get { int n = 0; foreach (var _ in Table()) n++; return n; } }

    // 注意(2026-10-30): この表の説明文は、後でアセットを直接直したカード(文言の全体見直し等)より古いものがある。
    //  全部に当てると古い説明へ戻るので、変えたカードだけに当てる(ApplyElementsV4 / -cbOnly id,id,...)。
    [MenuItem("Tools/OneMoreMile/Apply Card Balance v3 (ALL - descriptions may be stale)")]
    public static void Apply() => ApplyFiltered(OnlyFromArgs());

    // 属性20枚の再設計(2026-10-30)
    public static readonly string[] ElementIds = { "flame_blade", "burning_soul", "inferno", "flame_counter", "frost_edge", "ice_prison", "absolute_zero",
        "thunder_strike", "high_voltage", "chain_lightning", "thunder_lord", "wind_cutter", "gale", "tornado", "vampire", "predator", "overheal", "blood_rush", "blood_blade", "phoenix" };
    [MenuItem("Tools/OneMoreMile/Apply element cards v4 (2026-10-30)")]
    public static void ApplyElementsV4() => ApplyFiltered(new HashSet<string>(ElementIds));

    static HashSet<string> OnlyFromArgs()
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-cbOnly") return new HashSet<string>(a[i + 1].Split(','));
        return null;
    }

    static void ApplyFiltered(HashSet<string> only)
    {
        CardDatabaseBuilder.Build(); // #92〜#99 の新しいアセット(既存は変えない)
        var report = new System.Text.StringBuilder();
        int applied = 0;
        var seen = new HashSet<string>();
        foreach (var c in Table())
        {
            if (only != null && !only.Contains(c.id)) { seen.Add(c.id); continue; }
            string path = $"Assets/Resources/Cards/{c.id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            if (def == null) { report.AppendLine($"MISSING {c.id}"); continue; }
            if (!seen.Add(c.id)) { report.AppendLine($"DUPLICATE {c.id}"); continue; }
            if (!string.IsNullOrEmpty(c.name)) def.cardName = c.name;
            def.description = c.desc;
            if ((int)c.el >= 0) def.element = c.el;
            if (c.rec > 0) def.recommendPriority = c.rec;
            if (c.rarity > 0) { def.rarity = c.rarity; def.gachaStage = c.stage; def.unlockDistance = c.unlock; }
            def.effects = new List<CardEffect>();
            foreach (var e in c.fx) def.effects.Add(new CardEffect { type = e.t, value = e.v, scaling = e.s });
            EditorUtility.SetDirty(def);
            applied++;
        }
        // 表に無いカード(あれば報告)
        foreach (var g in AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets/Resources/Cards" }))
        {
            var d = AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(g));
            if (d != null && !seen.Contains(d.cardId)) report.AppendLine($"NOT IN TABLE {d.cardId}");
        }
        AssetDatabase.SaveAssets();
        CardDatabase.Reset();
        string msg = $"[CardBalanceV3] applied {applied} cards (table {CardCount})\n{report}";
        Debug.Log(msg);
        System.IO.File.WriteAllText("CardBalanceV3_Apply.txt", msg);
    }
}

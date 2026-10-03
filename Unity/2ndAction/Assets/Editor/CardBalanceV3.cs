using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// カードバランス v3(2026-10-03)の全99枚のデータ(効果・説明・属性)。この表が数値の正本。
//  Tools/OneMoreMile/Apply Card Balance v3(または -executeMethod CardBalanceV3.Apply)で Resources/Cards の各アセットへ書き込む。
//  先に CardDatabaseBuilder.Build を呼んで、#92〜#99 の新しいアセットを作る(既存のアセットの ID/アイコン/レア度/解放距離は変えない)。
//  cardId は保存データ(所持/デッキ/キャラカード/中断データ)に入っているので変えない。旧 #79 ULTIMATE は表示名だけ ALMIGHTY
//  (cardId "ultimate" のまま)。将来の #100 ULTIMATE は別の cardId(例 "ultimate_art")で作ること。
//  値の意味: E(種類, 値, 出し方)。出し方 = PerLevel(1Lvごと)/ Once(持っていれば)/ Every3(3Lvごと)/ Every2 / Phoenix / After3。
public static class CardBalanceV3
{
    public const int Version = 3;

    struct E
    {
        public EffectType t; public float v; public CardScaling s;
        public E(EffectType t, float v, CardScaling s = CardScaling.PerLevel) { this.t = t; this.v = v; this.s = s; }
    }

    struct C
    {
        public string id, name, desc; public ElementType el; public int rec; public E[] fx;
    }

    static C Card(string id, string desc, params E[] fx) => new C { id = id, desc = desc, fx = fx, el = (ElementType)(-1), rec = 0 };
    static C Card(string id, string desc, ElementType el, params E[] fx) => new C { id = id, desc = desc, fx = fx, el = el, rec = 0 };
    static C Named(C c, string name) { c.name = name; return c; }
    static C Rec(C c, int rec) { c.rec = rec; return c; }

    const CardScaling Once = CardScaling.Once, E3 = CardScaling.Every3, E2 = CardScaling.Every2, Ph = CardScaling.Phoenix, A3 = CardScaling.After3;

    static IEnumerable<C> Table()
    {
        // ---- 移動
        yield return Card("speed_up", "移動速度が上がる(1Lvごとに+3%)", new E(EffectType.SpeedPct, 0.03f));
        yield return Rec(Card("speed_down", "移動速度が下がる(1Lvごとに-4%)。ソロでは選ぶ理由の少ない特殊なカード", new E(EffectType.SpeedPct, -0.04f)), 1);
        yield return Card("jump_power_up", "ジャンプ力が上がる(1Lvごとに+3%)。魔法使いは高度の上下が速くなる", new E(EffectType.JumpPct, 0.03f));
        yield return Card("jump_count_up", "空中ジャンプが増える(1Lvごとに+1回)。続けて跳ぶほど少し弱くなるが、穴に落ちた時は弱くならない。魔法使いは上がれる高さが3Lvごとに1段増える", new E(EffectType.JumpCount, 1f));
        yield return Card("sky_runner", "ジャンプ力(1Lvごとに+2%)と空中攻撃(1Lvごとに+3%)が上がる", new E(EffectType.AirPct, 0.03f), new E(EffectType.JumpPct, 0.02f));
        yield return Card("close_call", "移動速度(1Lvごとに+2%)と、被弾した後の無敵時間(1Lvごとに+0.05秒)が上がる", new E(EffectType.SpeedPct, 0.02f), new E(EffectType.HurtInvincibleSeconds, 0.05f));
        yield return Card("momentum", "今の速さが攻撃力になる。100km/hを超えると上がり始め、150km/hで最大(1Lvごとに+4%)。その先は伸びが鈍い", new E(EffectType.MomentumPct, 0.04f));
        yield return Card("overdrive", "115km/h以上を続けるとOVERDRIVE(攻撃 1Lvごとに+3%、攻撃速度 +2%)。遅くなると解除。移動速度も少し上がる(1Lvごとに+2%)",
            new E(EffectType.OverdrivePct, 0.03f), new E(EffectType.OverdriveAttackSpeedPct, 0.02f), new E(EffectType.SpeedPct, 0.02f), new E(EffectType.OverdriveLevel, 1f));

        // ---- 攻撃(無条件)
        yield return Card("attack_up", "攻撃力が上がる(1Lvごとに+5%)", new E(EffectType.AttackPct, 0.05f));
        yield return Rec(Card("brake_attack", "攻撃力が上がる(1Lvごとに+7%)。主攻撃の直後に少し減速する", new E(EffectType.AttackPct, 0.07f), new E(EffectType.BrakeLevel, 1f, Once)), 3);
        yield return Rec(Card("giant_slayer", "攻撃力が大きく上がる(1Lvごとに+9%)が、攻撃速度が下がる(1Lvごとに-3%)", new E(EffectType.AttackPct, 0.09f), new E(EffectType.AttackSpeedPct, -0.03f)), 2);
        yield return Rec(Card("reverse_gear", "攻撃力が上がる(1Lvごとに+8%)が、移動速度が下がる(1Lvごとに-3%)", new E(EffectType.AttackPct, 0.08f), new E(EffectType.SpeedPct, -0.03f)), 2);
        yield return Card("mob_killer", "ボス以外の敵への攻撃(1Lvごとに+5%)と、敵撃破のMILE(1Lvごとに+10%)が上がる", new E(EffectType.MobPct, 0.05f), new E(EffectType.MileGainMultiplier, 0.10f));

        // ---- HP犠牲(封印)。払えた分だけ効く。払えない時は候補に出ない
        yield return Rec(Card("berserker", "攻撃力(1Lvごとに+6%)と攻撃速度(+1%)が上がる。最大HPのハートを3Lvごとに1つ封印する", new E(EffectType.AttackPct, 0.06f), new E(EffectType.AttackSpeedPct, 0.01f), new E(EffectType.SacrificeHearts, 1f, E3)), 2);
        yield return Rec(Card("glass_cannon", "攻撃力が大きく上がる(1Lvごとに+9%)。最大HPのハートを2Lvごとに1つ封印する", new E(EffectType.AttackPct, 0.09f), new E(EffectType.SacrificeHearts, 1f, E2)), 1);
        yield return Rec(Card("heart_breaker", "攻撃力が上がり(1Lvごとに+4%)、封印したハート1つごとにさらに攻撃+0.6%/Lv。最大HPのハートを3Lvごとに1つ封印する", new E(EffectType.AttackPct, 0.04f), new E(EffectType.SealedHeartPct, 0.006f), new E(EffectType.SacrificeHearts, 1f, E3)), 1);
        yield return Rec(Card("deaths_contract", "攻撃力がとても大きく上がる(1Lvごとに+12%)。最大HPのハートを2Lvごとに1つ封印する", new E(EffectType.AttackPct, 0.12f), new E(EffectType.SacrificeHearts, 1f, E2)), 1);

        // ---- 攻撃速度 / 範囲
        yield return Card("attack_speed_up", "攻撃速度が上がる(1Lvごとに+3%)", new E(EffectType.AttackSpeedPct, 0.03f));
        yield return Card("rapid_edge", "主攻撃(前/後)の攻撃速度が上がる(1Lvごとに+5%)。上/下/叩きつけ/大技には掛からない", new E(EffectType.MainAttackSpeedPct, 0.05f));
        yield return Card("double_attack", "攻撃したとき、確率(1Lvごとに+3%)で追加の1撃が出る。追加の1撃からは出ない", new E(EffectType.DoubleAttackChance, 0.03f));
        yield return Card("attack_range_up", "実効攻撃範囲が広がる(1Lvごとに+3%)。近接は判定、飛び道具は飛ぶ距離(弾は大きくならない)", new E(EffectType.RangePct, 0.03f));
        yield return Card("long_blade", "実効攻撃範囲が大きく広がる(1Lvごとに+5%)", new E(EffectType.RangePct, 0.05f));
        yield return Card("piercing_blade", "攻撃が敵を貫く。近接は当てた敵の後ろへ(Lvで数と威力が上がる)、飛び道具は貫通の回数(3Lvごとに+1)", new E(EffectType.PierceLevel, 1f));
        yield return Card("shockwave", "近接の命中で前方へ短い衝撃波、飛び道具は着弾点のまわりに衝撃波(Lvで威力と範囲が上がる)", new E(EffectType.ShockwaveLevel, 1f));
        yield return Card("chain_explosion", "敵を倒すと爆発する。爆発で倒れた敵からも爆発が続く(3回まで、同じ連鎖で同じ敵へは1回)", new E(EffectType.ChainExplosionLevel, 1f));

        // ---- 空中 / 地上
        yield return Card("air_attack_up", "空中での攻撃力が上がる(1Lvごとに+7%)", new E(EffectType.AirPct, 0.07f));
        yield return Card("aerial_blade", "空中で敵に当てると、追い斬りの斬撃が出る(Lvで威力と範囲が上がる)", new E(EffectType.AerialBladeLevel, 1f));
        yield return Card("air_dominion", "空中攻撃が上がり(1Lvごとに+3%)、着地せずに当て続けるほどさらに上がる(1回ごとに+1%/Lv、4回まで)", new E(EffectType.AirPct, 0.03f), new E(EffectType.AirDominionPct, 0.01f));
        yield return Card("sky_master", "空中攻撃が上がる(1Lvごとに+4%)。空中で当てると空中ジャンプを1回取り戻す(1回の滞空で3Lvごとに1回)", new E(EffectType.AirPct, 0.04f), new E(EffectType.SkyMasterLevel, 1f));
        yield return Card("air_strike", "空中の敵(打ち上げた敵/飛ぶ敵/空のボス)への攻撃が上がる(1Lvごとに+8%)", new E(EffectType.AntiAirPct, 0.08f));
        yield return Card("ground_fighter", "地上での攻撃力が上がる(1Lvごとに+7%)", new E(EffectType.GroundPct, 0.07f));
        yield return Card("heavy_impact", "下攻撃/叩きつけ/着地の衝撃が強くなる(1Lvごとに+10%)。締めの一撃も少し上がる(+3%/Lv)", new E(EffectType.DownPct, 0.10f), new E(EffectType.FinisherPct, 0.03f));
        yield return Card("ground_breaker", "叩きつけの着地と地上の締めで、地面を走る衝撃波が出る(Lvで威力と長さが上がる)。地上攻撃も少し上がる(+2%/Lv)", new E(EffectType.GroundBreakerLevel, 1f), new E(EffectType.GroundPct, 0.02f));
        yield return Rec(Card("ground_zero", "地上での攻撃力が大きく上がる(1Lvごとに+11%)が、空中では下がる(1Lvごとに-5%)", new E(EffectType.GroundPct, 0.11f), new E(EffectType.AirPct, -0.05f)), 2);
        yield return Rec(Card("skybound", "空中での攻撃力が大きく上がる(1Lvごとに+11%)が、地上では下がる(1Lvごとに-5%)", new E(EffectType.AirPct, 0.11f), new E(EffectType.GroundPct, -0.05f)), 2);

        // ---- コンボ(初撃/連撃中/締め。単発のキャラは1.2秒以内の3回で1つの流れ)
        yield return Card("first_strike", "攻撃の流れの1撃目(初撃)が強くなる(1Lvごとに+10%)", new E(EffectType.FirstPct, 0.10f));
        yield return Card("combo_rush", "連撃の途中の攻撃が強くなり(1Lvごとに+6%)、主攻撃の攻撃速度も少し上がる(+2%/Lv)", new E(EffectType.ComboPct, 0.06f), new E(EffectType.MainAttackSpeedPct, 0.02f));
        yield return Card("combo_plus", "攻撃の流れの最後の一撃(締め)が強くなる(1Lvごとに+7%)", new E(EffectType.FinisherPct, 0.07f));
        yield return Card("combo_edge", "連続ヒット(HIT数)が続くほど攻撃力が上がる(1ヒットごとに+0.4%/Lv、10ヒットまで)", new E(EffectType.ComboEdgePct, 0.004f));
        yield return Card("combo_master", "締めの一撃で、前方へ特殊な衝撃が出る(Lvで威力と範囲が上がる)", new E(EffectType.ComboMasterLevel, 1f));
        yield return Card("sonic_blade", "主攻撃のたびに前方へ斬撃が飛ぶ。速く走っているほど遠く・大きく・強くなる", new E(EffectType.SonicBladeLevel, 1f));

        // ---- ボス
        yield return Card("boss_killer", "ボスへの攻撃力が上がる(1Lvごとに+8%)", new E(EffectType.BossPct, 0.08f));
        yield return Card("hunter", "ボス戦で動きやすくなる。攻撃の前進/後退が伸び(1Lvごとに+4%)、遠いボスへ近づく速さも上がる(+3%/Lv)", new E(EffectType.HunterLevel, 1f));

        // ---- HP / 防御
        yield return Card("heart_up", "最大HPが増え(1Lvごとにハート+1、最大ハート20)、HPが回復する", new E(EffectType.MaxHpHearts, 1f));
        yield return Rec(Card("fortress", "最大HPが増え(1Lvごとにハート+1)、被弾ののけぞりが減る(+5%/Lv)が、移動速度が下がる(1Lvごとに-3%)", new E(EffectType.MaxHpHearts, 1f), new E(EffectType.HurtKnockbackReduce, 0.05f), new E(EffectType.SpeedPct, -0.03f)), 2);
        yield return Rec(Card("heavy_armor", "強い一撃(ハート2つ以上)をハート1つ軽くする。最大HPも増える(2Lvごとにハート+1)が、ジャンプ力が下がる(1Lvごとに-3%)", new E(EffectType.MaxHpHearts, 1f, E2), new E(EffectType.HeavyArmorLevel, 1f, Once), new E(EffectType.JumpPct, -0.03f)), 2);
        yield return Card("iron_will", "HPが満タンの間、攻撃力が上がる(1Lvごとに+5%)。最大HPも増える(3Lvごとにハート+1)", new E(EffectType.FullHpPct, 0.05f), new E(EffectType.MaxHpHearts, 1f, E3));
        yield return Card("shield", "被弾を1回防ぐShield。最大数は3Lvごとに+1(最大3)、使った分は時間で戻る(Lvで速くなる)", new E(EffectType.ShieldCapacity, 1f, E3), new E(EffectType.ShieldRecharge, 1.5f));
        yield return Card("counter", "Shieldで防いだ時、まわりへ反撃の衝撃(Lvで威力が上がる)。Shieldを持っていなくても1つ持てる", new E(EffectType.CounterLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("perfect_guard", "Shieldを使った時、短い無敵になる(0.4秒+1Lvごとに0.1秒)。Shieldを持っていなくても1つ持てる", new E(EffectType.PerfectGuardLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("flame_counter", "Shieldで防いだ時、まわりへ炎の反撃(Lvで威力が上がる)、当たった敵は炎上する。Shieldを持っていなくても1つ持てる", ElementType.Fire, new E(EffectType.FlameCounterLevel, 1f), new E(EffectType.ShieldGuard, 1f, Once));
        yield return Card("last_chance", "HPがハート1つになった瞬間、短い無敵になる(1秒+1Lvごとに0.15秒)。HPが回復してしばらくたつまで次は出ない", new E(EffectType.LastChanceLevel, 1f));
        yield return Card("second_wind", "HPが30%以下に落ちた時、HPを少し回復(ハート1〜3)。一度出ると距離のクールダウン(10,000m、Lvで短く)", new E(EffectType.SecondWindLevel, 1f));

        // ---- 血(吸収 / 出血 / 低HP / 復活)
        yield return Card("vampire", "敵を倒した時、確率(1Lvごとに+3%)でHPをハート1つ回復", ElementType.Blood, new E(EffectType.LifestealChance, 0.03f));
        yield return Card("predator", "吸収の確率が上がる(1Lvごとに+3%)。ボスへの攻撃でも、時々吸収の判定がある", ElementType.Blood, new E(EffectType.LifestealChance, 0.03f), new E(EffectType.PredatorLevel, 1f, Once));
        yield return Card("overheal", "回復量が増える(Lv4〜6で+1、Lv7〜9で+2ハート)。満タンで回復した分はShield1つになる", ElementType.Blood, new E(EffectType.HealBonusHearts, 1f, A3), new E(EffectType.OverhealLevel, 1f, Once));
        yield return Card("blood_rush", "HP50%以下で攻撃力が上がり(1Lvごとに+4%)、HP25%以下では吸収の確率も上がる(+3%/Lv)", ElementType.Blood, new E(EffectType.LowHp50Pct, 0.04f), new E(EffectType.LowHp25LifestealChance, 0.03f));
        yield return Card("blood_blade", "攻撃が出血を起こす(確率と威力がLvで上がる)。HPが満タンの間は出血の確率が2倍。満HPの攻撃も少し上がる(+2%/Lv)", ElementType.Blood,
            new E(EffectType.BleedChance, 0.05f), new E(EffectType.BleedChance, 0.05f, Once), new E(EffectType.BleedPower, 0.6f), new E(EffectType.BleedPower, 3f, Once), new E(EffectType.BloodBladeLevel, 1f, Once), new E(EffectType.FullHpPct, 0.02f));
        yield return Rec(Card("last_stand", "HP50%以下で攻撃力が上がり(1Lvごとに+4%)、HP25%以下でさらに上がる(+4%/Lv)", new E(EffectType.LowHp50Pct, 0.04f), new E(EffectType.LowHp25Pct, 0.04f)), 3);
        yield return Rec(Card("berserk_drive", "HP50%以下で攻撃力(1Lvごとに+5%)と攻撃速度(+2%)、HP25%以下でさらに両方が上がる", new E(EffectType.LowHp50Pct, 0.05f), new E(EffectType.LowHp25Pct, 0.05f), new E(EffectType.LowHpAttackSpeedPct, 0.02f)), 2);
        yield return Rec(Card("adrenaline", "HP50%以下で攻撃速度が上がり(1Lvごとに+3%)、HP25%以下ではその2倍", new E(EffectType.LowHpAttackSpeedPct, 0.03f)), 3);
        yield return Card("phoenix", "倒れた時に一度だけ復活(HPはLvで ハート1〜5)。復活するとこのカードは外れ、また候補に出る(取り直しはLv1から)", ElementType.Blood, new E(EffectType.PhoenixLevel, 1f, Ph));

        // ---- 万能
        yield return Named(Card("ultimate", "攻撃力(1Lvごとに+3%)・最大HP(2Lvごとにハート+1)・移動速度(1Lvごとに+2%)をまとめて上げる万能カード",
            new E(EffectType.AttackPct, 0.03f), new E(EffectType.MaxHpHearts, 1f, E2), new E(EffectType.SpeedPct, 0.02f)), "ALMIGHTY");

        // ---- 成長(EXP)
        yield return Card("exp_up", "得られる経験値が増える(1Lvごとに+20%)", new E(EffectType.ExpGain, 0.20f));
        yield return Card("pathfinder", "走った距離の経験値が増える(1Lvごとに+20%)", new E(EffectType.DistanceExpPct, 0.20f));
        yield return Card("level_break", "得られる経験値が大きく増える(1Lvごとに+30%)", new E(EffectType.ExpGain, 0.30f));
        yield return Card("experience_burst", "敵/ボス/BONUS ZONE の経験値が大きく増える(1Lvごとに+80%)", new E(EffectType.KillExpPct, 0.80f));
        yield return Card("long_haul", "経験値(1Lvごとに+15%)と最大HP(3Lvごとにハート+1)が増える", new E(EffectType.ExpGain, 0.15f), new E(EffectType.MaxHpHearts, 1f, E3));
        yield return Card("the_long_road", "経験値(1Lvごとに+40%)と最大HP(2Lvごとにハート+1)が増える", new E(EffectType.ExpGain, 0.40f), new E(EffectType.MaxHpHearts, 1f, E2));
        yield return Rec(Card("exp_converter", "経験値が減る(1Lvごとに-10%、最低25%)代わりに、敵撃破のMILEが増える(+30%/Lv)", new E(EffectType.MileGainMultiplier, 0.30f), new E(EffectType.ExpGain, -0.10f)), 2);

        // ---- MILE
        yield return Card("executioner", "敵撃破で得られるMILEが増える(1Lvごとに+15%)", new E(EffectType.MileGainMultiplier, 0.15f));
        yield return Card("treasure_hunter", "宝・報酬(BONUS ZONE の宝運びゴブリン/黄金スライム/ミミック/CLEAR/PERFECT、WANTED の賞金)のMILEが増える(1Lvごとに+30%)。ボスと距離のMILEも少し増える",
            new E(EffectType.TreasureMilePct, 0.30f), new E(EffectType.BossMileGainMultiplier, 0.10f), new E(EffectType.DistanceMilePct, 0.05f));
        yield return Card("one_more_mile", "MILE(1Lvごとに+50%)・ボスMILE(+50%)・経験値(+20%)が増える", new E(EffectType.MileGainMultiplier, 0.50f), new E(EffectType.BossMileGainMultiplier, 0.50f), new E(EffectType.ExpGain, 0.20f));

        // ---- Challenge / Risk(本当に難しくなる代わりに MILE)
        yield return Rec(Card("greed", "移動速度が上がる(1Lvごとに+4%)が、敵の出現が増える(+8%/Lv)", new E(EffectType.SpeedPct, 0.04f), new E(EffectType.EnemySpawnRate, 0.08f)), 2);
        yield return Rec(Card("no_turning_back", "移動速度が大きく上がる(1Lvごとに+5%)が、敵の出現が増える(+10%/Lv)", new E(EffectType.SpeedPct, 0.05f), new E(EffectType.EnemySpawnRate, 0.10f)), 2);
        yield return Rec(Card("more_enemies", "敵の出現が増える(1Lvごとに+10%)。敵撃破のMILEも少し増える(+3%/Lv)", new E(EffectType.EnemySpawnRate, 0.10f), new E(EffectType.MileGainMultiplier, 0.03f)), 2);
        yield return Rec(Card("tough_enemies", "雑魚のHPが上がる(1Lvごとに+15%)代わりに、MILE(+15%/Lv)と撃破の経験値(+5%/Lv)が増える", new E(EffectType.EnemyHpMultiplier, 0.15f), new E(EffectType.MileGainMultiplier, 0.15f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("fast_enemies", "雑魚の動き・追尾・攻撃が速くなる(1Lvごとに+4%)代わりに、MILE(+15%/Lv)と撃破の経験値(+5%/Lv)が増える", new E(EffectType.EnemyActionPct, 0.04f), new E(EffectType.MileGainMultiplier, 0.15f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("elite_enemies", "雑魚の一部が精鋭になる(1Lvごとに+4%。HP×2.5・少し速い、倒すとMILE×3/経験値×2)。MILEも増える(+30%/Lv)", new E(EffectType.EliteChance, 0.04f), new E(EffectType.MileGainMultiplier, 0.30f)), 2);
        yield return Rec(Card("monster_rush", "敵の出現が増える(1Lvごとに+10%)代わりに、経験値が増える(+15%/Lv)", new E(EffectType.EnemySpawnRate, 0.10f), new E(EffectType.ExpGain, 0.15f)), 2);
        yield return Rec(Card("horde", "敵の出現が大きく増える(1Lvごとに+15%)代わりに、MILE(+5%/Lv)と撃破の経験値(+5%/Lv)が増える", new E(EffectType.EnemySpawnRate, 0.15f), new E(EffectType.MileGainMultiplier, 0.05f), new E(EffectType.KillExpPct, 0.05f)), 2);
        yield return Rec(Card("boss_challenge", "ボスのHPが上がる(1Lvごとに+10%)代わりに、ボスMILEが増える(+40%/Lv)", new E(EffectType.BossHpMultiplier, 0.10f), new E(EffectType.BossMileGainMultiplier, 0.40f)), 2);
        yield return Rec(Card("boss_rush", "ボスの関門で、確率(1Lvごとに+8%)でボスがもう1体加わる。ボスMILEが増える(+50%/Lv)", new E(EffectType.BossMileGainMultiplier, 0.50f), new E(EffectType.BossRushLevel, 1f)), 2);
        yield return Rec(Card("wanted", "一定距離ごとに賞金首が現れる(Lvで強く・頻繁に)。倒せば賞金、通り過ぎれば逃げ切り。MILEが増える(+35%/Lv)", new E(EffectType.MileGainMultiplier, 0.35f), new E(EffectType.WantedLevel, 1f)), 2);
        yield return Rec(Card("hell_mode", "雑魚のHP(+12%/Lv)・動き(+4%/Lv)・出現(+8%/Lv)が上がる代わりに、MILE(+50%/Lv)とボスMILE(+30%/Lv)が大きく増える",
            new E(EffectType.MileGainMultiplier, 0.50f), new E(EffectType.EnemyHpMultiplier, 0.12f), new E(EffectType.EnemyActionPct, 0.04f), new E(EffectType.EnemySpawnRate, 0.08f), new E(EffectType.BossMileGainMultiplier, 0.30f)), 1);
        yield return Rec(Card("pandemonium", "雑魚のHP/動き/出現/精鋭、ボスのHPがすべて上がる代わりに、MILE(+60%/Lv)とボスMILE(+60%/Lv)がとても大きく増える",
            new E(EffectType.MileGainMultiplier, 0.60f), new E(EffectType.BossMileGainMultiplier, 0.60f), new E(EffectType.EnemyHpMultiplier, 0.15f), new E(EffectType.EnemyActionPct, 0.05f),
            new E(EffectType.EnemySpawnRate, 0.12f), new E(EffectType.BossHpMultiplier, 0.10f), new E(EffectType.EliteChance, 0.02f)), 1);

        // ---- 属性: 炎
        yield return Card("flame_blade", "炎の入口。命中で敵が炎上する(確率 15%+5%/Lv、1秒あたり 5+1.5/Lv のダメージ、3秒)", ElementType.Fire,
            new E(EffectType.BurnChance, 0.05f), new E(EffectType.BurnChance, 0.10f, Once), new E(EffectType.BurnPower, 1.5f), new E(EffectType.BurnPower, 3.5f, Once));
        yield return Card("burning_soul", "炎上が強く(1Lvごとに+8%)、長く(+0.25秒/Lv)なる。少しだけ自分でも炎上させる", ElementType.Fire,
            new E(EffectType.BurnPowerPct, 0.08f), new E(EffectType.BurnDuration, 0.25f), new E(EffectType.BurnChance, 0.02f), new E(EffectType.BurnChance, 0.05f, Once), new E(EffectType.BurnPower, 3f, Once));
        yield return Card("inferno", "燃えている敵を倒すと、まわりへ炎が広がり炎上させる(Lvで威力と範囲が上がる)。少しだけ自分でも炎上させる", ElementType.Fire,
            new E(EffectType.InfernoLevel, 1f), new E(EffectType.BurnChance, 0.05f, Once), new E(EffectType.BurnPower, 3f, Once));
        // ---- 氷
        yield return Card("frost_edge", "氷の入口。命中で敵が冷える(確率 15%+5%/Lv、動きが 25%+2%/Lv 遅く)。冷えが重なると凍結(ボスは止まらず強い減速)", ElementType.Ice,
            new E(EffectType.ChillChance, 0.05f), new E(EffectType.ChillChance, 0.10f, Once), new E(EffectType.ChillSlow, 0.02f), new E(EffectType.ChillSlow, 0.23f, Once));
        yield return Card("ice_prison", "凍結しやすくなる(必要な冷えの回数が3Lvごとに-1)。冷えの確率と時間も上がる", ElementType.Ice,
            new E(EffectType.FreezeThresholdReduce, 1f, E3), new E(EffectType.ChillChance, 0.02f), new E(EffectType.ChillChance, 0.05f, Once), new E(EffectType.ChillSlow, 0.20f, Once), new E(EffectType.ChillDuration, 0.1f));
        yield return Card("absolute_zero", "凍結した雑魚が砕けてダメージ、冷えた/凍った敵への攻撃が上がる(1Lvごとに+3%)。ボスは凍結の代わりの減速が強くなる", ElementType.Ice,
            new E(EffectType.AbsoluteZeroLevel, 1f), new E(EffectType.FreezeDuration, 0.05f), new E(EffectType.ChillChance, 0.05f, Once), new E(EffectType.ChillSlow, 0.20f, Once));
        // ---- 雷
        yield return Card("thunder_strike", "雷の入口。命中で落雷が起きる(確率 5%+2%/Lv、威力 7.5+1.5/Lv。同じ敵へは0.4秒に1回まで)", ElementType.Thunder,
            new E(EffectType.LightningChance, 0.02f), new E(EffectType.LightningChance, 0.03f, Once), new E(EffectType.LightningPower, 1.5f), new E(EffectType.LightningPower, 6f, Once));
        yield return Card("high_voltage", "雷が強くなる(1Lvごとに+5%)。落雷は雑魚を一瞬止める。落雷の確率も少し上がる(+1%/Lv)", ElementType.Thunder,
            new E(EffectType.LightningDamagePct, 0.05f), new E(EffectType.LightningChance, 0.01f), new E(EffectType.LightningShock, 0.02f), new E(EffectType.LightningShock, 0.15f, Once), new E(EffectType.LightningPower, 2f, Once));
        yield return Card("chain_lightning", "落雷が近くの別の敵へ連鎖する(3Lvごとに+1体、届く距離もLvで伸びる)。少しだけ自分でも落雷を起こす", ElementType.Thunder,
            new E(EffectType.LightningChains, 1f, E3), new E(EffectType.LightningRange, 0.15f), new E(EffectType.LightningChance, 0.04f, Once), new E(EffectType.LightningPower, 3f, Once));
        yield return Card("thunder_lord", "雷の上位。威力(1Lvごとに+4%)・連鎖(3Lvごとに+1体)・落雷のまわりへの範囲が強くなる", ElementType.Thunder,
            new E(EffectType.LightningDamagePct, 0.04f), new E(EffectType.LightningChains, 1f, E3), new E(EffectType.LightningSplash, 0.08f), new E(EffectType.LightningSplash, 0.8f, Once),
            new E(EffectType.LightningChance, 0.005f), new E(EffectType.LightningChance, 0.03f, Once), new E(EffectType.LightningPower, 2f, Once));
        // ---- 風
        yield return Card("wind_cutter", "風の入口。命中で前方へ貫通する風刃が飛ぶ(確率 10%+3%/Lv、威力 8+1.5/Lv)。攻撃範囲も少し広がる(+1%/Lv)", ElementType.Wind,
            new E(EffectType.WindBladeChance, 0.03f), new E(EffectType.WindBladeChance, 0.07f, Once), new E(EffectType.WindBladePower, 1.5f), new E(EffectType.WindBladePower, 6.5f, Once), new E(EffectType.RangePct, 0.01f));
        yield return Card("gale", "風刃が速く(1Lvごとに+5%)、遠くへ(+0.4m/Lv)、多くの敵を貫く(3Lvごとに+1)。飛び道具の貫通も増える", ElementType.Wind,
            new E(EffectType.WindSpeedPct, 0.05f), new E(EffectType.WindRange, 0.4f), new E(EffectType.WindPierce, 1f, E3), new E(EffectType.WindBladeChance, 0.02f), new E(EffectType.WindBladeChance, 0.04f, Once), new E(EffectType.WindBladePower, 6f, Once));
        yield return Card("tornado", "風刃が当たった所に小さな竜巻が起き、まわりの敵を巻き込む(Lvで威力と範囲が上がる)。少しだけ自分でも風刃を出す", ElementType.Wind,
            new E(EffectType.TornadoLevel, 1f), new E(EffectType.WindBladeChance, 0.04f, Once), new E(EffectType.WindBladePower, 6f, Once));
    }

    public static int CardCount { get { int n = 0; foreach (var _ in Table()) n++; return n; } }

    [MenuItem("Tools/OneMoreMile/Apply Card Balance v3")]
    public static void Apply()
    {
        CardDatabaseBuilder.Build(); // #92〜#99 の新しいアセット(既存は変えない)
        var report = new System.Text.StringBuilder();
        int applied = 0;
        var seen = new HashSet<string>();
        foreach (var c in Table())
        {
            string path = $"Assets/Resources/Cards/{c.id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            if (def == null) { report.AppendLine($"MISSING {c.id}"); continue; }
            if (!seen.Add(c.id)) { report.AppendLine($"DUPLICATE {c.id}"); continue; }
            if (!string.IsNullOrEmpty(c.name)) def.cardName = c.name;
            def.description = c.desc;
            if ((int)c.el >= 0) def.element = c.el;
            if (c.rec > 0) def.recommendPriority = c.rec;
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

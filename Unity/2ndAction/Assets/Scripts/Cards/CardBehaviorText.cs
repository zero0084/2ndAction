using UnityEngine;

// 「挙動」のカード(Lv が強さになる28種)の表示(2026-10-13): 以前は全部「効果Lv」で、何がどれだけ強くなるのか分からなかった。
// 実際の計算と同じ式で「何が・どれだけ」を出す(CardProcs / PlayerController.CardStats / GameManager.CardStats / ElementSystem /
// ChallengeSystem / BossManager / UltimateTuning)。式を変えた時はここも合わせる。
//  ・威力は「攻撃力の強化が無い時の値」(実際は攻撃力の強化で増える。ProcBase = 20 × (1 + 0.5 × 攻撃の強化))
//  ・Lv はそのカードの能力の Lv(1ランで能力ごとに最大9)
public static class CardBehaviorText
{
    const float ProcBase0 = 20f; // CardProcs.ProcBase の攻撃の強化が無い時

    static string P(float k) => Mathf.RoundToInt(ProcBase0 * k).ToString();
    static string M(float v) => v.ToString("0.#");
    static string Pct(float v) => Mathf.RoundToInt(v * 100f).ToString();
    // カード拡張の効果の行(LocCardExpansion の文。訳のレビューが済むまで、その言語では日本語の文を出す)
    static string Exp(string id, params string[] args) => Loc.Src(LocCardExpansion.Find(id).behavior, args);
    // 2026-11-06 共通カードの追加(LocCardCommon の文。訳のレビューが済むまで、その言語では英語の文)
    static string Com(string id, params string[] args) => Loc.Src(LocCardCommon.Find(id).behavior, args);
    // 2026-11-06 マップ専用カード 20 枚(LocCardMap の文。訳のレビューが済むまで、その言語では英語の文)
    static string Map(string id, params string[] args) => Loc.Src(LocCardMap.Find(id).behavior, args);
    static string Pf(float v) => (v * 100f).ToString("0.#"); // 0.2% のような小さい割合も出す

    // 2026-10-29: 日本語の元の文(数値は差し込み済み)を返す。訳は表示の時に Loc.Auto が型(各キー)で行う
    //  (合成カードの説明は作り置きなので、作った時の言語で固まらないように。並べ替えも表示の時に1回だけ)
    public static string Describe(EffectType t, int lv)
    {
        lv = Mathf.Clamp(lv, 1, 9);
        switch (t)
        {
            case EffectType.OverdriveLevel: return Loc.Src("115km/h以上を維持 → OVERDRIVE発動まで{0}秒", Mathf.Max(0.5f, CardRules.OverdriveBaseActivateSeconds - CardRules.OverdriveActivatePerLevel * (lv - 1)).ToString("0.##"));
            case EffectType.SonicBladeLevel: return Loc.Src("飛ぶ斬撃 威力{0}・基本飛距離{1}m(速度で延長)", P(0.25f + 0.03f * lv), M(3f + 0.2f * lv));
            case EffectType.CounterLevel: return Loc.Src("Shield で防いだ時の反撃 威力{0}・範囲3m", P(1f + 0.25f * lv));
            case EffectType.PerfectGuardLevel: return Loc.Src("Shield で防いだ後の無敵 {0}秒", M(0.4f + 0.1f * lv));
            case EffectType.FlameCounterLevel: return Loc.Src("Shield で防いだ時の炎の反撃 威力{0}・炎上 毎秒{1}", P(CardProcs.FlameCounterPower(lv)), (4 + lv).ToString());
            case EffectType.SecondWindLevel: return Loc.Src("被弾でHP30%以下 → ハート{0}回復・再発動まで{1}m", (1 + (lv >= 5 ? 1 : 0) + (lv >= 9 ? 1 : 0)).ToString(), Mathf.Max(1000, 10000 - 500 * (lv - 1)).ToString("#,0"));
            case EffectType.LastChanceLevel: return Loc.Src("被弾でハート1つ以下 → 無敵{0}秒", (1f + 0.15f * lv).ToString("0.##"));
            case EffectType.PhoenixLevel: return Loc.Src("復活時のHP ハート{0}(2秒無敵)", (1 + lv / 2).ToString());
            case EffectType.OverhealLevel: return Loc.Src("満タンで溢れた回復は Shield 1つに(最大数+1まで)");
            case EffectType.PredatorLevel: return Loc.Reviewed(LocPendingReview.PredatorText, "ボス命中時もHP吸収(通常の吸収確率の1/10)");
            case EffectType.PierceLevel: return Loc.Src("近接: 後方{0}体へ追加攻撃 威力{1} / 飛び道具 貫通+{2}", (1 + lv / 3).ToString(), P(0.35f + 0.04f * lv), Mathf.CeilToInt(lv / 3f).ToString());
            case EffectType.ShockwaveLevel: return Loc.Src("衝撃波 威力{0}・幅{1}m(飛び道具は半径{2}m)", P(0.25f + 0.035f * lv), M(1.8f + 0.12f * lv), M(1f + 0.08f * lv));
            case EffectType.ChainExplosionLevel: return Loc.Src("撃破時の爆発 威力{0}・半径{1}m(連鎖は3段まで)", P(0.4f + 0.06f * lv), M(1.6f + 0.08f * lv));
            case EffectType.AerialBladeLevel: return Loc.Src("空中の追い斬り 威力{0}・半径{1}m", P(0.3f + 0.04f * lv), M(1.4f + 0.06f * lv));
            case EffectType.SkyMasterLevel: return Loc.Src("空中攻撃命中でジャンプ回復(着地まで{0}回)", Mathf.CeilToInt(lv / 3f).ToString());
            case EffectType.GroundBreakerLevel: return Loc.Src("地上の衝撃波 威力{0}・長さ{1}m", P(0.3f + 0.05f * lv), M(2f * (2.5f + 0.2f * lv)));
            case EffectType.ComboMasterLevel: return Loc.Src("連続攻撃の締めの衝撃 威力{0}・幅{1}m", P(0.5f + 0.08f * lv), M(2.2f + 0.1f * lv));
            case EffectType.HunterLevel: return Loc.Src("ボス戦 踏み込み+{0}%・6m超のボスへの接近速度+{1}%", (4 * lv).ToString(), (3 * lv).ToString());
            case EffectType.BrakeLevel: return Loc.Src("前後攻撃後0.4秒間 移動速度-10%");
            case EffectType.HeavyArmorLevel: return Loc.Src("ハート2つ以上の被弾をハート1つ分軽く");
            case EffectType.BossRushLevel: return Loc.Src("ボスがもう1体加わる確率 {0}%", Mathf.Min(100, 8 * lv).ToString());
            case EffectType.WantedLevel: return Loc.Src("賞金首 {0}mごと・HP×{1}・撃破で{2} MILE", Mathf.Max(1500, 3000 - 150 * (lv - 1)).ToString("#,0"), M(3f + 0.3f * lv), (20 + 10 * lv).ToString());
            case EffectType.InfernoLevel: return Loc.Src("炎上中の敵を撃破 → 延焼 威力{0}・半径{1}m", CardProcs.InfernoDamage(lv).ToString("0"), M(CardProcs.InfernoRadius(lv)));
            case EffectType.AbsoluteZeroLevel: return Loc.Src("冷えた/凍結した敵への攻撃+{0}%・凍結時の砕け 威力{1}", (3 * lv).ToString(), ElementSystem.ShatterBase(lv).ToString("0"));
            // 2026-10-30: 回数と引き寄せを足した新しい文。訳が入るまでは前の文(回数の所は前の「×3回」のまま)
            case EffectType.TornadoLevel:
                return Loc.Ready(LocPendingReview.TornadoText)
                    ? Loc.Src(LocPendingReview.TornadoText, CardProcs.TornadoDamage(lv).ToString("0"), CardProcs.TornadoPulses(lv).ToString(), CardProcs.TornadoRadius(lv).ToString("0.##"), CardProcs.TornadoPull(lv).ToString("0.##"))
                    : Loc.Src("竜巻 威力{0}×3回・半径{1}m", CardProcs.TornadoDamage(lv).ToString("0"), CardProcs.TornadoRadius(lv).ToString("0.##"));
            case EffectType.LightningCounterLevel:
                return Loc.Ready(LocPendingReview.LightningCounterText) ? Loc.Src(LocPendingReview.LightningCounterText, ElementStats.LightningEveryFor(lv).ToString()) : "";
            case EffectType.BloodBladeLevel: return Loc.Src("HP満タンの時 出血の確率×2");
            // 2026-10-31 カード拡張(属性 12 枚)。文は LocCardExpansion(訳のレビュー待ち)。数値は ElementSystem.Expansion と同じ式
            case EffectType.ScorchLevel: return Exp("scorch", Pct(ElementSystem.ScorchPct(lv)));
            case EffectType.FireWhirlLevel: return Exp("fire_whirl", M(ElementSystem.FireWhirlRange(lv)), Pct(ElementSystem.FireWhirlChance(lv)), M(ElementSystem.FireWhirlWindRadius(lv)));
            case EffectType.CombustionLevel: return Exp("combustion", ElementSystem.CombustionPower(lv).ToString("0"), M(ElementSystem.CombustionRadius(lv)));
            case EffectType.PermafrostLevel: return Exp("permafrost", (ElementSystem.PermafrostPerStack(lv) * 100f).ToString("0.#"));
            case EffectType.GlacierGuardLevel: return Exp("glacier_guard", ElementSystem.GlacierGuardInterval(lv).ToString("0"));
            case EffectType.ShatterStrikeLevel: return Exp("shatter_strike", ElementSystem.ShatterStrikePower(lv).ToString("0"), Pct(ElementSystem.ShatterStrikeBreak(lv)));
            case EffectType.ConductorLevel: return Exp("conductor", Pct(ElementSystem.ConductorPct(lv)), Pct(ElementSystem.ConductorChillBonus(lv)));
            case EffectType.VoltSurgeLevel: return Exp("volt_surge", Pct(ElementSystem.VoltSurgeMul(lv)));
            case EffectType.JudgmentLevel: return Exp("judgment_bolt", ElementSystem.JudgmentNeed(lv).ToString(), ElementSystem.JudgmentMul(lv).ToString("0.##"), Pct(ElementSystem.JudgmentBreak(lv)));
            case EffectType.SlipstreamLevel: return Exp("slipstream", Pct(ElementSystem.SlipstreamDmg(lv)), (ElementSystem.SlipstreamChance(lv) * 100f).ToString("0.#"));
            case EffectType.UpdraftLevel: return Exp("updraft", Pct(ElementSystem.UpdraftPct(lv)), M(ElementSystem.UpdraftPower(lv)));
            case EffectType.TempestLevel: return Exp("tempest", (1 + ElementSystem.TempestExtra(lv)).ToString(), Pct(ElementSystem.TempestMul(lv)));
            // 2026-11-06 共通カードの追加(数値は CardRewards と同じ式)
            case EffectType.ComboPayLevel: return Com("combo_pay", M(CardRewards.ComboPayMile(lv)));
            case EffectType.HuntPayLevel: return Com("hunt_pay", M(CardRewards.HuntPayMile(lv)));
            case EffectType.RoadPayLevel: return Com("road_pay", M(CardRewards.RoadPayMile(lv)));
            case EffectType.SurvivalPayLevel: return Com("survival_pay", M(CardRewards.SurvivalPayMile(lv)));
            case EffectType.ComboTrainingLevel: return Com("combo_training", M(CardRewards.ComboTrainingExp(lv)));
            case EffectType.HuntTrainingLevel: return Com("hunt_training", M(CardRewards.HuntTrainingExp(lv)));
            case EffectType.RoadTrainingLevel: return Com("road_training", M(CardRewards.RoadTrainingExp(lv)));
            case EffectType.SurvivalTrainingLevel: return Com("survival_training", M(CardRewards.SurvivalTrainingExp(lv)));
            case EffectType.DangerBountyLevel: return Com("danger_bounty", Pct(CardRewards.DangerHeavyChance(lv)));
            case EffectType.SkySiegeLevel: return Com("sky_siege", Pct(CardRewards.SkySiegeAirChance(lv)), CardRewards.SkySiegeFormationMul(lv).ToString("0.0#"));
            case EffectType.CrossroadsLevel: return Com("crossroads", M(CardRewards.CrossroadsFromKm(lv)), CardRewards.CrossroadsGapMul(lv).ToString("0.0#"));
            case EffectType.HighStakesLevel: return Com("high_stakes", Pct(CardRewards.HighStakesPct(lv)));
            case EffectType.ChainBountyLevel: return Com("chain_bounty", Pct(CardRewards.ChainBountyPct(lv)));
            case EffectType.IronMarathonLevel: return Com("iron_marathon", M(CardRewards.IronMarathonMile(lv)), M(CardRewards.IronMarathonExp(lv)));
            case EffectType.TrophyHuntLevel: return Com("trophy_hunt", M(CardRewards.TrophyMilePerKill(lv)), M(CardRewards.TrophyExpPerKill(lv)));
            case EffectType.HorizonLevel: return Com("horizon", Pct(CardRewards.HorizonPerStep(lv)), Pct(CardRewards.HorizonPerStep(lv) * CardRewards.HorizonMaxSteps));
            // 2026-11-06 マップ専用カード 20 枚(数値は MapCardRules と同じ)
            case EffectType.LastBastionLevel: return Map("last_bastion", MapCardRules.LastBastionShields(lv).ToString(), M(MapCardRules.LastBastionShieldSeconds), M(MapCardRules.LastBastionCooldown(lv)));
            case EffectType.OverflowWardLevel: return Map("overflow_ward", Pct(MapCardRules.OverflowPct(lv)), MapCardRules.OverflowMax(lv).ToString(), M(MapCardRules.OverflowShieldSeconds));
            case EffectType.RevengeArmorLevel: return Map("revenge_armor", M(MapCardRules.RevengeSeconds(lv)), Pct(MapCardRules.RevengeAttack(lv)));
            case EffectType.BreakFeastLevel: return Map("break_feast", MapCardRules.BreakFeastHearts(lv).ToString(), M(MapCardRules.BreakFeastCooldown));
            case EffectType.IronHeartLevel: return Map("iron_heart", Pf(MapCardRules.IronHeartPerHeart(lv)), Pct(MapCardRules.IronHeartCap(lv)));
            case EffectType.AirComboLevel: return Map("air_combo", MapCardRules.AirComboHits(lv).ToString(), M(MapCardRules.AirComboSeconds), Pct(MapCardRules.AirComboAttack(lv)));
            case EffectType.SkyBreakerLevel: return Map("sky_breaker", Pct(MapCardRules.SkyBreaker(lv)));
            case EffectType.DiveBoostLevel: return Map("dive_boost", M(MapCardRules.DiveBoostSeconds), Pct(MapCardRules.DiveSpeed(lv)), Pct(MapCardRules.DiveAttack(lv)));
            case EffectType.LaunchMasterLevel: return Map("launch_master", Pct(MapCardRules.LaunchAttack(lv)), Pct(MapCardRules.LaunchSlow(lv)));
            case EffectType.SonicEdgeLevel: return Map("sonic_edge", Pct(MapCardRules.SonicAttack(lv)), Pct(MapCardRules.SonicBreak(lv)));
            case EffectType.SecondRiseLevel: return Map("second_rise", Pct(MapCardRules.SecondRiseHeal(lv)), M(MapCardRules.SecondRiseSeconds), Pct(MapCardRules.SecondRiseBuff(lv)));
            case EffectType.ShieldBurstLevel: return Map("shield_burst", Pct(MapCardRules.ShieldBurstPower(lv)), Pct(MapCardRules.ShieldBurstBreak(lv)), M(MapCardRules.ShieldBurstInterval(lv)));
            case EffectType.BloodArmorLevel: return Map("blood_armor", Pct(MapCardRules.BloodArmorPct(lv)), MapCardRules.BloodArmorMax(lv).ToString(), Pct(MapCardRules.BloodArmorBleed(lv)));
            case EffectType.ElementalChaosLevel: return Map("elemental_chaos", Pct(MapCardRules.ChaosBonus(lv, 2)), Pct(MapCardRules.ChaosBonus(lv, 3)), Pct(MapCardRules.ChaosBonus(lv, 4)), Pct(MapCardRules.ChaosBonus(lv, 5)));
            case EffectType.ElementalFocusLevel: return Map("elemental_focus", Pct(MapCardRules.FocusPower(lv, 1)), Pct(MapCardRules.FocusChance(lv, 1)));
            case EffectType.BreakDominionLevel: return Map("break_dominion", Pct(MapCardRules.DominionAttack(lv)), Pct(MapCardRules.DominionElement(lv)), Pct(MapCardRules.DominionReward(lv)));
            case EffectType.EndlessGrowthLevel: return Map("endless_growth", Pf(MapCardRules.GrowthPerKill(lv)), Pct(MapCardRules.GrowthCap(lv)));
            case EffectType.FinalDriveLevel: return Map("final_drive", Pct(MapCardRules.FinalSpeed(lv)), Pct(MapCardRules.FinalAttack(lv)), Pct(MapCardRules.FinalBreak(lv)));
            case EffectType.LastGambleLevel: return Map("last_gamble", Pct(MapCardRules.GambleExtraHeartChance), Pct(MapCardRules.GambleHeavyChance), Pct(1f - MapCardRules.GambleGapMul));
            case EffectType.OneMoreChanceLevel: return Map("one_more_chance", M(MapCardRules.OneMoreSeconds), Pct(MapCardRules.OneMoreAttack(lv)), Pct(MapCardRules.OneMoreSpeed(lv)), Pct(MapCardRules.OneMoreBreak(lv)));
            case EffectType.ShieldGuard: return Loc.Src("このカードだけでも Shield を1つ持てる");
            case EffectType.UltimateLevel:
            {
                var u = UltimateTuning.I;
                if (u == null) return "";
                return Loc.Src("前進{0}m・ボスへ最大HPの{1}%ダメージ・強化{2}秒", Mathf.RoundToInt(UltimateTuning.At(u.advance, lv)).ToString(),
                    Mathf.RoundToInt(UltimateTuning.At(u.bossDamageFraction, lv) * 100f).ToString(), M(UltimateTuning.At(u.buffSeconds, lv)));
            }
        }
        return "";
    }
}

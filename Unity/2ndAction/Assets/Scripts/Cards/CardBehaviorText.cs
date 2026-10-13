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

    public static string Describe(EffectType t, int lv)
    {
        lv = Mathf.Clamp(lv, 1, 9);
        switch (t)
        {
            case EffectType.OverdriveLevel: return Loc.F("115km/h以上を維持 → OVERDRIVE発動まで{0}秒", Mathf.Max(0.5f, CardRules.OverdriveBaseActivateSeconds - CardRules.OverdriveActivatePerLevel * (lv - 1)).ToString("0.##"));
            case EffectType.SonicBladeLevel: return Loc.F("飛ぶ斬撃 威力{0}・基本飛距離{1}m(速度で延長)", P(0.25f + 0.03f * lv), M(3f + 0.2f * lv));
            case EffectType.CounterLevel: return Loc.F("Shield で防いだ時の反撃 威力{0}・範囲3m", P(1f + 0.25f * lv));
            case EffectType.PerfectGuardLevel: return Loc.F("Shield で防いだ後の無敵 {0}秒", M(0.4f + 0.1f * lv));
            case EffectType.FlameCounterLevel: return Loc.F("Shield で防いだ時の炎の反撃 威力{0}・炎上 毎秒{1}", P(0.6f + 0.15f * lv), (4 + lv).ToString());
            case EffectType.SecondWindLevel: return Loc.F("被弾でHP30%以下 → ハート{0}回復・再発動まで{1}m", (1 + (lv >= 5 ? 1 : 0) + (lv >= 9 ? 1 : 0)).ToString(), Mathf.Max(1000, 10000 - 500 * (lv - 1)).ToString("#,0"));
            case EffectType.LastChanceLevel: return Loc.F("被弾でハート1つ以下 → 無敵{0}秒", (1f + 0.15f * lv).ToString("0.##"));
            case EffectType.PhoenixLevel: return Loc.F("復活時のHP ハート{0}(2秒無敵)", (1 + lv / 2).ToString());
            case EffectType.OverhealLevel: return Loc.T("満タンで溢れた回復は Shield 1つに(最大数+1まで)");
            case EffectType.PredatorLevel: return Loc.T("ボス命中時もHP吸収(通常の吸収確率の1/10)");
            case EffectType.PierceLevel: return Loc.F("近接: 後方{0}体へ追加攻撃 威力{1} / 飛び道具 貫通+{2}", (1 + lv / 3).ToString(), P(0.35f + 0.04f * lv), Mathf.CeilToInt(lv / 3f).ToString());
            case EffectType.ShockwaveLevel: return Loc.F("衝撃波 威力{0}・幅{1}m(飛び道具は半径{2}m)", P(0.25f + 0.035f * lv), M(1.8f + 0.12f * lv), M(1f + 0.08f * lv));
            case EffectType.ChainExplosionLevel: return Loc.F("撃破時の爆発 威力{0}・半径{1}m(連鎖は3段まで)", P(0.4f + 0.06f * lv), M(1.6f + 0.08f * lv));
            case EffectType.AerialBladeLevel: return Loc.F("空中の追い斬り 威力{0}・半径{1}m", P(0.3f + 0.04f * lv), M(1.4f + 0.06f * lv));
            case EffectType.SkyMasterLevel: return Loc.F("空中攻撃命中でジャンプ回復(着地まで{0}回)", Mathf.CeilToInt(lv / 3f).ToString());
            case EffectType.GroundBreakerLevel: return Loc.F("地上の衝撃波 威力{0}・長さ{1}m", P(0.3f + 0.05f * lv), M(2f * (2.5f + 0.2f * lv)));
            case EffectType.ComboMasterLevel: return Loc.F("連続攻撃の締めの衝撃 威力{0}・幅{1}m", P(0.5f + 0.08f * lv), M(2.2f + 0.1f * lv));
            case EffectType.HunterLevel: return Loc.F("ボス戦 踏み込み+{0}%・6m超のボスへの接近速度+{1}%", (4 * lv).ToString(), (3 * lv).ToString());
            case EffectType.BrakeLevel: return Loc.T("前後攻撃後0.4秒間 移動速度-10%");
            case EffectType.HeavyArmorLevel: return Loc.T("ハート2つ以上の被弾をハート1つ分軽く");
            case EffectType.BossRushLevel: return Loc.F("ボスがもう1体加わる確率 {0}%", Mathf.Min(100, 8 * lv).ToString());
            case EffectType.WantedLevel: return Loc.F("賞金首 {0}mごと・HP×{1}・撃破で{2} MILE", Mathf.Max(1500, 3000 - 150 * (lv - 1)).ToString("#,0"), M(3f + 0.3f * lv), (20 + 10 * lv).ToString());
            case EffectType.InfernoLevel: return Loc.F("炎上中の敵を撃破 → 延焼 威力{0}・半径{1}m", (6 + 2 * lv).ToString(), M(2f + 0.1f * lv));
            case EffectType.AbsoluteZeroLevel: return Loc.F("冷えた/凍結した敵への攻撃+{0}%・凍結時の砕け 威力{1}", (3 * lv).ToString(), (10 + 3 * lv).ToString());
            case EffectType.TornadoLevel: return Loc.F("竜巻 威力{0}×3回・半径{1}m", (4 + lv).ToString(), (1.2f + 0.06f * lv).ToString("0.##"));
            case EffectType.BloodBladeLevel: return Loc.T("HP満タンの時 出血の確率×2");
            case EffectType.ShieldGuard: return Loc.T("このカードだけでも Shield を1つ持てる");
            case EffectType.UltimateLevel:
            {
                var u = UltimateTuning.I;
                if (u == null) return "";
                return Loc.F("前進{0}m・ボスへ最大HPの{1}%ダメージ・強化{2}秒", Mathf.RoundToInt(UltimateTuning.At(u.advance, lv)).ToString(),
                    Mathf.RoundToInt(UltimateTuning.At(u.bossDamageFraction, lv) * 100f).ToString(), M(UltimateTuning.At(u.buffSeconds, lv)));
            }
        }
        return "";
    }
}

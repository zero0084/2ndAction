#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// CARD BALANCE TEST「ビルド」タブの追加の表示(2026-10-03)と、属性の試験値。
//  ・実際の速さ(HUDと同じkm/h)と正規化(50〜150km/hを0〜1)、MOMENTUMの速度の項(今の式=距離 / 実速度にした場合)
//  ・攻撃時間/範囲/HP/Shield/EXP倍率/MILE倍率/吸収/敵出現率(Encounterの頻度)/能力のLv上限
//  ・属性の値と、このランで発動した回数(炎上/冷気/凍結/落雷/風刃/出血、属性で与えたダメージ、出血の回復)
//  ・属性の試験値: カードの数値はまだ決まっていないので、確認用の仮の値を上乗せする(ラン中だけ・保存しない・RESETで消える)
public partial class CardBalanceTest
{
    static readonly (string name, (EffectType t, float v)[] vals)[] ElementTestSets =
    {
        ("炎", new[] { (EffectType.BurnChance, 0.5f), (EffectType.BurnPower, 0.3f) }),
        ("氷", new[] { (EffectType.ChillChance, 0.6f), (EffectType.ChillSlow, 0.5f), (EffectType.FreezeStacks, 3f) }),
        ("雷", new[] { (EffectType.LightningChance, 0.4f), (EffectType.LightningPower, 0.5f), (EffectType.LightningChains, 3f) }),
        ("風", new[] { (EffectType.WindBladeChance, 0.4f), (EffectType.WindBladePower, 0.6f), (EffectType.WindPierce, 2f) }),
        ("血", new[] { (EffectType.BleedChance, 0.5f), (EffectType.BleedPower, 0.25f), (EffectType.BleedLifesteal, 0.3f), (EffectType.BloodLowHpBonus, 1f) }),
    };

    static bool ElementTestOn(GameManager gm, int i)
    {
        foreach (var kv in ElementTestSets[i].vals) if (!gm.Elements.DevOverlay.ContainsKey(kv.t)) return false;
        return true;
    }

    public static void ToggleElementTest(GameManager gm, int i, bool? on = null)
    {
        bool now = ElementTestOn(gm, i);
        bool want = on ?? !now;
        foreach (var kv in ElementTestSets[i].vals)
        {
            if (want) gm.Elements.DevOverlay[kv.t] = kv.v;
            else gm.Elements.DevOverlay.Remove(kv.t);
        }
    }

    void DrawBuildSpeedAndExtras(float x, ref float y, float rowH, float gap, PlayerController pc, GameManager gm)
    {
        float kmh = pc.CurrentRunKmh;
        Line(x, ref y, rowH, gap, $"実速度 {kmh:0.0}km/h(runSpeed {pc.runSpeed:0.00} × 距離の倍率 {pc.NaturalMultiplierAt(pc.DistanceFromStart):0.00})  正規化 50→150km/h = {pc.SpeedFactor01(50f, 150f):0.00}  "
            + $"MOMENTUMの速度項: 今の式(距離) {pc.MomentumSpeedTermByDistance:0.00} / 実速度にした場合 {pc.MomentumSpeedTermByCurrentSpeed:0.00}");
        Line(x, ref y, rowH, gap, $"攻撃時間 ×{pc.AttackSpeedMultiplier:0.00}(テンポ×{1f / Mathf.Max(0.01f, pc.AttackSpeedMultiplier):0.0})  攻撃範囲 ×{pc.AttackRangeMultiplier:0.00}  "
            + $"EXP ×{gm.CardTestExpGainMultiplier:0.00}  MILE ×{gm.MileGainMultiplier:0.00}(ボス ×{gm.BossMileGainMultiplier:0.00})  吸収 {gm.CardTestLifestealChance * 100f:0}%/{gm.CardTestLifestealAmount:0}");
        var dir = EncounterDirector.Instance;
        Line(x, ref y, rowH, gap, $"敵出現率 ×{gm.EnemySpawnRateMultiplier:0.00} → Encounterの頻度 ×{(dir != null ? dir.SpawnFrequency : 1f):0.00}(上限 ×{(dir != null ? dir.spawnRateMaxFrequency : 2f):0.0})  "
            + $"能力Lv上限 {GameManager.MaxRunCardLevel}(上限で効かなかった分 {gm.CardCapDiscardedStacks})");
        Line(x, ref y, rowH, gap, $"属性: {gm.Elements.Describe()}");
        Line(x, ref y, rowH, gap, $"発動: 炎上 {ElementSystem.BurnProcs} / 冷気 {ElementSystem.ChillProcs} 凍結 {ElementSystem.FreezeProcs} / 落雷 {ElementSystem.LightningProcs}(命中 {ElementSystem.LightningHits}) / 風刃 {ElementSystem.WindBlades} / 出血 {ElementSystem.BleedProcs}  "
            + $"属性ダメージ計 {ElementSystem.ElementDamage:N0}  出血の回復 {ElementSystem.BleedHealed}");
        GUI.Label(new Rect(x, y, 92f, rowH), "属性 試験値", sLabel);
        float bx = x + 92f;
        for (int i = 0; i < ElementTestSets.Length; i++)
        {
            bool on = ElementTestOn(gm, i);
            if (B(new Rect(bx, y, 64f, rowH), ElementTestSets[i].name, on)) ToggleElementTest(gm, i);
            bx += 68f;
        }
        GUI.Label(new Rect(bx + 6f, y, W - bx - 20f, rowH), "確認用の仮の値(カードの数値ではない)。ラン中だけ・保存しない", sSmall);
        y += rowH + gap;
    }
}
#endif

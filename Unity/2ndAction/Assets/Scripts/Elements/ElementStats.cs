using System.Collections.Generic;
using UnityEngine;

// 属性システムの基盤(2026-10-03)。
// 属性はカードの取得でだけ付く(キャラ固有の属性/補正、敵の属性/弱点/耐性、相性表は作らない)。
// どのキャラでも同じカードを取れば同じ属性ビルドになる。各属性は「色違いの追加ダメージ」ではなく戦い方を変える:
//   炎 Burn      … 命中した敵が燃え続ける(継続ダメージ)
//   氷 Chill     … 命中した敵の行動が遅くなる。重なると Freeze(雑魚は止まる。ボスは止めず強めの減速に変換)
//   雷 Lightning … 落雷が近くの別の敵へ連鎖する(複数の敵へ)
//   風 Wind      … 前方へ飛ぶ貫通の風刃 / 飛び道具の貫通の追加(攻撃が届く範囲を広げる)
//   血 Bleed     … 出血の継続ダメージの一部を回復、低HPほど強い(既存の吸収=VAMPIRE系とは別に重なる)
// ここはラン中の値の入れ物だけ。カードの EffectType(EffectType.cs の「属性」)の値が加算される。
// 既存カード(FLAME BLADE / FROST EDGE / THUNDER STRIKE / HIGH VOLTAGE / WIND CUTTER)には、まだ値を入れていない
// (Lv1〜Lv9の数値はこれから決める)。値が0の間は何も起きない。
public sealed class ElementStats
{
    // 時間を指定しないカード用の既定(秒)
    public const float DefaultBurnDuration = 3f;
    public const float DefaultChillDuration = 2f;
    public const float DefaultFreezeDuration = 1.2f;
    public const float DefaultBleedDuration = 4f;
    public const float BaseLightningRange = 4.5f;
    public const float BaseWindRange = 9f;

    readonly Dictionary<EffectType, float> values = new Dictionary<EffectType, float>();

    // 開発版の CARD BALANCE TEST が一時的に上乗せする値(セーブしない/新しいランで消える)
    public readonly Dictionary<EffectType, float> DevOverlay = new Dictionary<EffectType, float>();

    public void Add(EffectType type, float value)
    {
        if (!IsElementEffect(type)) return;
        values.TryGetValue(type, out float v);
        values[type] = v + value;
    }

    // CARD BALANCE TEST の RESET 用(開発版から呼ぶ)
    public void ClearForTest() { values.Clear(); DevOverlay.Clear(); }

    public float Get(EffectType type)
    {
        values.TryGetValue(type, out float v);
        DevOverlay.TryGetValue(type, out float d);
        return v + d;
    }

    public static bool IsElementEffect(EffectType t) => t >= EffectType.BurnChance && t <= EffectType.BloodLowHpBonus;

    static float Chance(float c) => Mathf.Clamp01(c);

    public float BurnChance => Chance(Get(EffectType.BurnChance));
    public float BurnPower => Mathf.Max(0f, Get(EffectType.BurnPower));
    public float BurnDuration => Get(EffectType.BurnDuration) > 0f ? Get(EffectType.BurnDuration) : DefaultBurnDuration;

    public float ChillChance => Chance(Get(EffectType.ChillChance));
    public float ChillSlow => Mathf.Clamp(Get(EffectType.ChillSlow), 0f, 0.9f);
    public float ChillDuration => Get(EffectType.ChillDuration) > 0f ? Get(EffectType.ChillDuration) : DefaultChillDuration;
    public int FreezeStacks => Mathf.Max(0, Mathf.RoundToInt(Get(EffectType.FreezeStacks)));
    public float FreezeDuration => Get(EffectType.FreezeDuration) > 0f ? Get(EffectType.FreezeDuration) : DefaultFreezeDuration;

    public float LightningChance => Chance(Get(EffectType.LightningChance));
    public float LightningPower => Mathf.Max(0f, Get(EffectType.LightningPower));
    public int LightningChains => Mathf.Clamp(Mathf.RoundToInt(Get(EffectType.LightningChains)), 0, 12);
    public float LightningRange => BaseLightningRange + Mathf.Max(0f, Get(EffectType.LightningRange));

    public float WindBladeChance => Chance(Get(EffectType.WindBladeChance));
    public float WindBladePower => Mathf.Max(0f, Get(EffectType.WindBladePower));
    public int WindPierce => Mathf.Clamp(Mathf.RoundToInt(Get(EffectType.WindPierce)), 0, 20);
    public float WindRange => BaseWindRange + Mathf.Max(0f, Get(EffectType.WindRange));

    public float BleedChance => Chance(Get(EffectType.BleedChance));
    public float BleedPower => Mathf.Max(0f, Get(EffectType.BleedPower));
    public float BleedDuration => Get(EffectType.BleedDuration) > 0f ? Get(EffectType.BleedDuration) : DefaultBleedDuration;
    public float BleedLifesteal => Mathf.Clamp01(Get(EffectType.BleedLifesteal));
    public float BloodLowHpBonus => Mathf.Max(0f, Get(EffectType.BloodLowHpBonus));

    public bool AnyActive =>
        (BurnChance > 0f && BurnPower > 0f) || (ChillChance > 0f && ChillSlow > 0f) || (LightningChance > 0f && LightningPower > 0f)
        || (WindBladeChance > 0f && WindBladePower > 0f) || WindPierce > 0 || (BleedChance > 0f && BleedPower > 0f);

    // 表示用(CARD BALANCE TEST)
    public string Describe()
    {
        var parts = new List<string>();
        if (BurnChance > 0f || BurnPower > 0f) parts.Add($"炎 {BurnChance * 100f:0}% x{BurnPower:0.##}/s {BurnDuration:0.#}s");
        if (ChillChance > 0f || ChillSlow > 0f) parts.Add($"氷 {ChillChance * 100f:0}% -{ChillSlow * 100f:0}% {ChillDuration:0.#}s" + (FreezeStacks > 0 ? $" 凍結{FreezeStacks}回" : ""));
        if (LightningChance > 0f || LightningPower > 0f) parts.Add($"雷 {LightningChance * 100f:0}% x{LightningPower:0.##} 連鎖{LightningChains}");
        if (WindBladeChance > 0f || WindPierce > 0) parts.Add($"風 {WindBladeChance * 100f:0}% x{WindBladePower:0.##} 貫通+{WindPierce}");
        if (BleedChance > 0f || BleedPower > 0f) parts.Add($"血 {BleedChance * 100f:0}% x{BleedPower:0.##}/s 吸収{BleedLifesteal * 100f:0}%");
        return parts.Count == 0 ? "なし" : string.Join(" / ", parts);
    }
}

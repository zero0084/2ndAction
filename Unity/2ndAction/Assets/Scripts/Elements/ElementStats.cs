using System.Collections.Generic;
using UnityEngine;

// 属性システム(2026-10-03、カードバランス v3 で実働化)。
// 属性はカードの取得でだけ付く(キャラ固有の属性/補正、敵の属性/弱点/耐性、相性表は作らない)。
// どのキャラでも同じカードを取れば同じ属性ビルドになる。各属性は「色違いの追加ダメージ」ではなく戦い方を変える:
//   炎 Burn      … 命中した敵が燃え続ける(継続ダメージ)。重ねると時間を延ばし、少しずつ強く(上限あり)
//   氷 Chill     … 命中した敵の行動が遅くなる。重なると Freeze(雑魚は止まる。ボスは止めず強めの減速に変換)
//   雷 Lightning … 落雷。近くの別の敵へ連鎖(Chain)、周りへの範囲(Splash)、雑魚を一瞬止める(Shock)
//   風 Wind      … 前方へ飛ぶ貫通の風刃 / 飛び道具の貫通の追加 / 竜巻(TORNADO)
//   血 Bleed     … 出血の継続ダメージ。低HPほど強く、一部を回復(吸収=VAMPIRE系とは別に重なる)
// v3: ダメージは「その命中のダメージの割合」ではなく、属性ごとの基礎ダメージ + Lv(カードの値)×
//     (1 + 無条件の攻撃の枠の半分)。攻撃×属性×コンボ×速度 の大きな掛け算にならない。
// 値はラン中のカードの合計(GameManager.Card)+ 開発版の CARD BALANCE TEST の上乗せ(DevOverlay)。
public sealed class ElementStats
{
    public const float DefaultBurnDuration = 3f;
    public const float DefaultChillDuration = 2f;
    public const float DefaultFreezeDuration = 1.0f;
    public const float DefaultBleedDuration = 4f;
    public const int DefaultFreezeThreshold = 5;
    public const float BaseLightningRange = 3.5f;
    public const float BaseWindRange = 6f;
    public const float BaseWindSpeed = 18f;

    // 開発版の CARD BALANCE TEST が一時的に上乗せする値(セーブしない/新しいランで消える)
    public readonly Dictionary<EffectType, float> DevOverlay = new Dictionary<EffectType, float>();

    // 以前の「カードから足し込む」入口(v3 では GameManager.Card から読むので何もしない。古い呼び出しの互換)
    public void Add(EffectType type, float value) { }

    // CARD BALANCE TEST の RESET 用(開発版から呼ぶ)
    public void ClearForTest() { DevOverlay.Clear(); }

    public float Get(EffectType type)
    {
        var gm = GameManager.Instance;
        float v = gm != null ? gm.Card.Get(type) : 0f;
        DevOverlay.TryGetValue(type, out float d);
        return v + d;
    }

    public static bool IsElementEffect(EffectType t) => t >= EffectType.BurnChance && t <= EffectType.BloodLowHpBonus;

    static float Chance(float c) => Mathf.Clamp01(c);
    static float Scale => ElementSystem.DamageScale;

    public float BurnChance => Chance(Get(EffectType.BurnChance));
    public float BurnPower => Mathf.Max(0f, Get(EffectType.BurnPower));                       // 基礎(1秒あたり)
    public float BurnDps => BurnPower * (1f + Mathf.Max(0f, Get(EffectType.BurnPowerPct))) * Scale;
    public float BurnDuration => DefaultBurnDuration + Mathf.Max(0f, Get(EffectType.BurnDuration));

    public float ChillChance => Chance(Get(EffectType.ChillChance));
    public float ChillSlow => Mathf.Clamp(Get(EffectType.ChillSlow), 0f, 0.7f);
    public float ChillDuration => DefaultChillDuration + Mathf.Max(0f, Get(EffectType.ChillDuration));
    public int FreezeStacks => ChillSlow > 0f ? Mathf.Max(2, DefaultFreezeThreshold - Mathf.RoundToInt(Get(EffectType.FreezeThresholdReduce))) : 0;
    public float FreezeDuration => DefaultFreezeDuration + Mathf.Max(0f, Get(EffectType.FreezeDuration));
    public int AbsoluteZero => Mathf.RoundToInt(Get(EffectType.AbsoluteZeroLevel));

    public float LightningChance => Chance(Get(EffectType.LightningChance));
    public float LightningPower => Mathf.Max(0f, Get(EffectType.LightningPower));
    public float LightningDamage => LightningPower * (1f + Mathf.Max(0f, Get(EffectType.LightningDamagePct))) * Scale;
    public int LightningChains => Mathf.Clamp(Mathf.RoundToInt(Get(EffectType.LightningChains)), 0, 8);
    public float LightningRange => BaseLightningRange + Mathf.Max(0f, Get(EffectType.LightningRange));
    public float LightningSplash => Mathf.Clamp(Get(EffectType.LightningSplash), 0f, 3f);
    public float LightningShock => Mathf.Clamp(Get(EffectType.LightningShock), 0f, 0.6f);

    public float WindBladeChance => Chance(Get(EffectType.WindBladeChance));
    public float WindBladePower => Mathf.Max(0f, Get(EffectType.WindBladePower));
    public float WindBladeDamage => WindBladePower * Scale;
    public int WindPierce => Mathf.Clamp(Mathf.RoundToInt(Get(EffectType.WindPierce)), 0, 20);
    public float WindRange => BaseWindRange + Mathf.Max(0f, Get(EffectType.WindRange));
    public float WindSpeed => BaseWindSpeed * (1f + Mathf.Max(0f, Get(EffectType.WindSpeedPct)));

    public float BleedChance => Chance(Get(EffectType.BleedChance));
    public float BleedPower => Mathf.Max(0f, Get(EffectType.BleedPower));
    public float BleedDuration => DefaultBleedDuration + Mathf.Max(0f, Get(EffectType.BleedDuration));
    public float BleedLifesteal => Mathf.Clamp01(Get(EffectType.BleedLifesteal));
    public float BloodLowHpBonus => Mathf.Max(0f, Get(EffectType.BloodLowHpBonus));
    public bool BloodBlade => Get(EffectType.BloodBladeLevel) > 0f;

    public bool AnyActive =>
        (BurnChance > 0f && BurnPower > 0f) || (ChillChance > 0f && ChillSlow > 0f) || (LightningChance > 0f && LightningPower > 0f)
        || (WindBladeChance > 0f && WindBladePower > 0f) || WindPierce > 0 || (BleedChance > 0f && BleedPower > 0f);

    // 表示用(CARD BALANCE TEST)
    public string Describe()
    {
        var parts = new List<string>();
        if (BurnChance > 0f || BurnPower > 0f) parts.Add($"炎 {BurnChance * 100f:0}% {BurnDps:0.#}/s {BurnDuration:0.#}s");
        if (ChillChance > 0f || ChillSlow > 0f) parts.Add($"氷 {ChillChance * 100f:0}% -{ChillSlow * 100f:0}% {ChillDuration:0.#}s 凍結{FreezeStacks}回/{FreezeDuration:0.#}s" + (AbsoluteZero > 0 ? $" 絶対零度Lv{AbsoluteZero}" : ""));
        if (LightningChance > 0f || LightningPower > 0f) parts.Add($"雷 {LightningChance * 100f:0}% {LightningDamage:0} 連鎖{LightningChains} 範囲{LightningSplash:0.#}m 停止{LightningShock:0.##}s");
        if (WindBladeChance > 0f || WindPierce > 0) parts.Add($"風 {WindBladeChance * 100f:0}% {WindBladeDamage:0} 貫通+{WindPierce} {WindRange:0.#}m");
        if (BleedChance > 0f || BleedPower > 0f) parts.Add($"血 {BleedChance * 100f:0}%{(BloodBlade ? "(満HP×2)" : "")} {BleedPower * Scale:0.#}/s 吸収{BleedLifesteal * 100f:0}%");
        return parts.Count == 0 ? "なし" : string.Join(" / ", parts);
    }
}

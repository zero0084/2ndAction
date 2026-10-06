using System.Collections.Generic;
using UnityEngine;

// COMBO 第1段階(2026-10-06)の定義。Resources/Cards/ComboTuning(無ければコードの既定値。アセットに無い COMBO は既定値で補う)。
// COMBO = 特定の能力(元のカードの cardId)をそのランで一緒に持つと、それぞれ単体では起きない「新しい現象」が起きる。
//  ・カードは消費しない(A+B は残る。Lv/効果/FINAL EVOLUTION の資格もそのまま)。合体して別のカードにはしない。
//  ・成立に Lv9/Mastery/AWAKENED/FINAL EVOLUTION は要らない(Lv1+Lv1 でも成立)。強さは構成カードの今の Lv から(平均と低い方)。
//  ・キャラカード(開始時の Lv)も数える。能力ごとの合算/Lv9 上限はそのまま(同じ能力を2枚持っても成立しない)。
//  ・効果は「現象」(爆発/粉砕/連鎖/衝撃波/盾/追撃)。数値の倍率だけにしない。カードバランス v3 の枠/曲線/上限/proc の予算は通る。
//  ・#100 ULTIMATE(character_ultimate)は構成に使わない。マルチでは効果なし(Docs/Multiplayer8.md)。
[CreateAssetMenu(menuName = "OneMoreMile/Combo Tuning")]
public class ComboTuning : ScriptableObject
{
    // 効果のモジュール(現象の種類)。1つの COMBO = 1つのモジュール + 値
    public enum Module
    {
        BurningBlast,     // 炎上中の敵への命中で小さな炎の爆発(同じ敵には間隔)
        Wildfire,         // 炎上中の敵を倒すと、まわりへ強い炎上を散らす(COMBO の炎上からは散らさない)
        FreezeNova,       // 凍結(ボスは強い減速)の瞬間、まわりへ冷気の衝撃波
        Shatter,          // 凍った雑魚への締め/叩きつけで砕ける範囲ダメージ(ボスは砕けない)
        ExtraChain,       // 落雷が確率で別の敵へもう1本(同じ敵へは 0.4 秒に1回)
        ChainBurst,       // 連鎖の最後の地点で雷の炸裂(炸裂からは連鎖しない)
        WindBurst,        // 風刃の貫通+1、当たった所に小さな風圧
        WindTornado,      // 風刃が N 回当たるごとに小さな竜巻(同時の数に上限)
        BloodAegis,       // 満タンで溢れた回復を Blood Aegis(被弾を1回防ぐ、上限あり)へ
        SonicWave,        // 一定の速さ以上で、主攻撃の命中から前方へ小さな衝撃波
        Redline,          // OVERDRIVE を続けると、一定の間隔でまわりへ赤い衝撃
        AegisBreak,       // Shield が尽きた瞬間、まわりへ大きな反撃の衝撃波
        AirAssault,       // 着地せずに空中で N 回当てるごとに追撃の斬撃(着地で数え直し)
        FinisherWave,     // 締めの一撃で、さらに前方へ衝撃波(初撃には出ない)
        DeathWish,        // HP が低い間、主攻撃の命中から確率で深紅の斬撃(リスクはそのまま)
    }

    [System.Serializable]
    public class Combo
    {
        public string id = "blazing_edge";
        public string displayName = "BLAZING EDGE";
        [TextArea] public string description = "";
        [Tooltip("必要な能力(元のカードの cardId)。第1段階は2つ。将来3つ以上も可")]
        public List<string> abilities = new List<string>();
        [Tooltip("どれか1つあれば良い追加の条件(空 = なし。将来用)")]
        public List<string> optionalAnyOf = new List<string>();
        public Module module = Module.BurningBlast;
        [Tooltip("主な値(ダメージ = 追加攻撃の基準ダメージ × power)")] public float power = 0.5f;
        [Tooltip("範囲(m)")] public float radius = 1.5f;
        [Tooltip("同じ発生の間隔(秒)。BurningBlast/ExtraChain は同じ敵ごと")] public float cooldown = 0.5f;
        [Tooltip("確率(0〜1)")] [Range(0f, 1f)] public float procChance = 1f;
        [Tooltip("数(回数/対象数/上限など、モジュールごと)")] public int count = 3;
        [Tooltip("強さの Lv: 構成カードの平均と最低の混ぜ方(0 = 平均だけ、1 = 最低だけ)")] [Range(0f, 1f)] public float minWeight = 0.5f;
        [Tooltip("Lv1 の時の強さ(Lv9 = 1)")] [Range(0.1f, 1f)] public float lv1Factor = 0.45f;
        [Header("発生してよい攻撃(COMBO 由来からは常に出ない)")]
        public bool canTriggerFromNormal = true;
        public bool canTriggerFromCard = false;
        public bool canTriggerFromFinalEvolution = false;
        [Header("FINAL EVOLUTION: 構成カードのどれかが ACTIVE の間だけ ENHANCED(1段階だけ)")]
        public float enhancedPowerMul = 1.25f;
        public float enhancedRadiusMul = 1.25f;
        [Tooltip("ENHANCED の時の追加(モジュールごと: 対象+1/上限+1/終端の炸裂 など)")] public int enhancedExtra = 1;
        [TextArea] public string enhancedDescription = "";
        public bool risk;
        public bool enabled = true;
        public Color color = new Color(1f, 0.6f, 0.2f);
        [Tooltip("HUD の仮アイコンの文字(正式アイコンは後で)")] public string iconText = "BE";
    }

    public List<Combo> combos = DefaultCombos();

    public Combo For(string id) { if (combos != null) foreach (var c in combos) if (c != null && c.id == id) return c; return null; }

    static Combo C(string id, string name, Module m, string a, string b, float power, float radius, float cd, float chance, int count, Color col, string icon, string desc, string enh, int extra = 1, bool risk = false)
        => new Combo { id = id, displayName = name, module = m, abilities = new List<string> { a, b }, power = power, radius = radius, cooldown = cd, procChance = chance, count = count, color = col, iconText = icon, description = desc, enhancedDescription = enh, enhancedExtra = extra, risk = risk };

    public static List<Combo> DefaultCombos() => new List<Combo>
    {
        // 炎
        C("blazing_edge", "BLAZING EDGE", Module.BurningBlast, "flame_blade", "burning_soul", 0.45f, 1.3f, 1.0f, 1f, 0, new Color(1f, 0.5f, 0.15f), "BE",
          "炎上中の敵を斬ると小さな炎の爆発(同じ敵には1秒に1回、爆発からは爆発しない)", "FLAME BLADE/BURNING SOUL の最終進化中: 爆発が広く、爆発でも炎上させる"),
        C("wildfire", "WILDFIRE", Module.Wildfire, "burning_soul", "inferno", 1.0f, 3.0f, 0.4f, 1f, 3, new Color(1f, 0.3f, 0.1f), "WF",
          "炎上中の敵を倒すと、まわりの敵(最大3体)へ強い炎上が飛び移る(COMBO の炎上で倒れても飛ばない)", "最終進化中: 飛び移る数+1、炎上が少し強い"),
        // 氷
        C("frozen_prison", "FROZEN PRISON", Module.FreezeNova, "frost_edge", "ice_prison", 0.3f, 2.0f, 0.6f, 1f, 0, new Color(0.55f, 0.85f, 1f), "FP",
          "敵が凍った(ボスは強い減速)瞬間、まわりへ冷気の衝撃波(冷やす+小ダメージ)", "最終進化中: 衝撃波が広く、ボスにも少し長く効く"),
        C("shatter", "SHATTER", Module.Shatter, "ice_prison", "absolute_zero", 1.2f, 1.8f, 0.3f, 1f, 0, new Color(0.75f, 0.95f, 1f), "SH",
          "凍った雑魚を締め/叩きつけで砕き、まわりへ氷の破片(ボスは砕けない)", "最終進化中: 破片が広い"),
        // 雷
        C("thunder_chain", "THUNDER CHAIN", Module.ExtraChain, "thunder_strike", "chain_lightning", 0.6f, 5.0f, 0.4f, 0.5f, 1, new Color(1f, 0.95f, 0.4f), "TC",
          "落雷が確率で近くの別の敵へもう1本走る(同じ敵へは0.4秒に1回)", "THUNDER STRIKE/CHAIN LIGHTNING の最終進化中: 必ず走り、終点で小さな炸裂"),
        C("storm_lord", "STORM LORD", Module.ChainBurst, "chain_lightning", "thunder_lord", 0.6f, 1.8f, 0.5f, 1f, 0, new Color(1f, 0.85f, 0.3f), "SL",
          "連鎖の最後の地点で雷の炸裂(炸裂からは連鎖しない)", "最終進化中: 炸裂が広い"),
        // 風
        C("gale_edge", "GALE EDGE", Module.WindBurst, "wind_cutter", "gale", 0.3f, 1.2f, 0.25f, 1f, 1, new Color(0.65f, 1f, 0.8f), "GE",
          "風刃の貫通+1、当たった所に小さな風圧", "最終進化中: 貫通+2"),
        C("cyclone", "CYCLONE", Module.WindTornado, "gale", "tornado", 0.5f, 1.4f, 0.8f, 1f, 4, new Color(0.6f, 1f, 0.9f), "CY",
          "風刃が4回当たるごとに小さな竜巻(同時に2つまで)", "最終進化中: 同時に3つまで、竜巻が少し大きい"),
        // 血
        C("blood_aegis", "BLOOD AEGIS", Module.BloodAegis, "vampire", "overheal", 0f, 0f, 3f, 1f, 1, new Color(0.85f, 0.1f, 0.2f), "BA",
          "満タンで溢れた回復を Blood Aegis へ(被弾を1回防ぐ。最大1つ、3秒に1回まで)", "VAMPIRE/OVERHEAL の最終進化中: 最大2つ"),
        // 速度
        C("sonic_momentum", "SONIC MOMENTUM", Module.SonicWave, "speed_up", "momentum", 0.35f, 2.2f, 0.5f, 1f, 100, new Color(0.5f, 0.9f, 1f), "SM",
          "100km/h 以上で主攻撃が当たると、前方へ小さな衝撃波(速さそのものは上げない)", "SPEED UP/MOMENTUM の最終進化中: 衝撃波が遠くまで届く"),
        C("redline", "REDLINE", Module.Redline, "momentum", "overdrive", 0.4f, 2.4f, 1.5f, 1f, 2, new Color(1f, 0.3f, 0.25f), "RL",
          "OVERDRIVE を2秒続けると、1.5秒ごとにまわりへ赤い衝撃(実速度は変えない)", "最終進化中: 衝撃が広い"),
        // 防御
        C("aegis_counter", "AEGIS COUNTER", Module.AegisBreak, "shield", "counter", 1.6f, 3.6f, 0.5f, 1f, 0, new Color(0.65f, 0.8f, 1f), "AC",
          "最後の Shield が割れた瞬間、まわりへ大きな反撃の衝撃波(1回の消費で1回)", "最終進化中: 衝撃波が広い"),
        // 空中
        C("sky_assault", "SKY ASSAULT", Module.AirAssault, "aerial_blade", "sky_master", 0.6f, 1.8f, 0.1f, 1f, 3, new Color(0.6f, 0.85f, 1f), "SA",
          "着地せずに空中で3回当てるごとに追撃の斬撃(着地で数え直し)", "最終進化中: 2回ごと"),
        // 締め
        C("finishing_blow", "FINISHING BLOW", Module.FinisherWave, "combo_plus", "combo_master", 0.7f, 2.4f, 0.2f, 1f, 0, new Color(1f, 0.55f, 0.95f), "FB",
          "締めの一撃で、さらに前方へ衝撃波(初撃には出ない)", "最終進化中: 衝撃波が大きい"),
        // リスク
        C("death_wish", "DEATH WISH", Module.DeathWish, "berserker", "glass_cannon", 0.6f, 2.0f, 0.35f, 0.3f, 50, new Color(0.8f, 0.05f, 0.15f), "DW",
          "HP50%以下の間、主攻撃の命中から確率で深紅の斬撃(封印したハートはそのまま = リスクは残る)", "最終進化中: 確率が上がる", 1, true),
    };

    static ComboTuning cached;
    static bool loaded;
    public static ComboTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<ComboTuning>("Cards/ComboTuning");
                if (cached == null) { cached = CreateInstance<ComboTuning>(); cached.hideFlags = HideFlags.DontSave; }
                if (cached.combos == null) cached.combos = new List<Combo>();
                foreach (var d in DefaultCombos()) if (cached.For(d.id) == null) cached.combos.Add(d); // アセットに無いものは既定値で補う
            }
            return cached;
        }
    }
}

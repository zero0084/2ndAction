using System.Collections.Generic;
using UnityEngine;

// FINAL EVOLUTION の調整値。Resources/FinalEvolution/FinalEvolutionTuning.asset(無ければコードの既定値。アセットに無いカードは既定値で補う)。
// カードLvは9のまま(Lv10は作らない)。Lv9で資格 → 初回だけ readyMeters 走ると READY(抽選に参加できる)→ LEVEL UP の候補(最大1枠)
// → 選ぶと一定時間/一定距離だけ限界突破(ACTIVE)→ 終われば READY へ戻る(再チャージなし。何度でも巡ってくる)。
// 2026-10-05 第2段階: 1ラン1回(USED)を廃止 / #100 ULTIMATE を除く全カードへ展開。
//  ・各カードの FINAL EVOLUTION = 共通モジュールの組み合わせ(データ):
//      増幅 amplify … そのカード自身の効果(Lv9 の値)を ACTIVE の間だけ ×amplify。カードバランス v3 の枠/曲線/上限をそのまま通る。
//                     弱点(マイナスの効果・封印・敵の強化)も同じ倍率で強くなる = 長所も短所も完成する。
//      追加 bonuses  … 既存の効果の種類(EffectType)を ACTIVE の間だけ足す(個性づけ。これも枠/曲線を通る)
//      専用の処理   … 代表9枚の第1段階の効果(最終ダメージ×1.5/超高速/斬撃波/Blood Shield/緊急復活/延焼/連鎖/EXP の枠/MILE とリスク)
//    どれも GameManager.RecomputeCardStats が毎回作り直す値に乗るだけなので、終われば何も残らない。
[CreateAssetMenu(menuName = "OneMoreMile/Final Evolution Tuning")]
public class FinalEvolutionTuning : ScriptableObject
{
    [Tooltip("資格(能力Lv9)を得てから初めて READY になるまでに走る距離(m)。終わった後は待たずに READY へ戻る")]
    public float readyMeters = 5000f;
    [Tooltip("1つの能力をこのランで FINAL EVOLUTION できる回数。0 = 制限なし(通常)。将来の特殊カード用に残す")]
    public int usesPerRun = 0;
    [Tooltip("AWAKENED 済みのカードの持続(秒/距離)の追加の割合(小さな特典)")]
    public float awakenedDurationBonus = 0.1f;
    [Tooltip("マルチでは候補に出さない(同期が未対応のため。Docs/Multiplayer8.md)")]
    public bool disableInMultiplayer = true;
    [Tooltip("SPEED の FINAL EVOLUTION で上げる実速度の上限(km/h)。人が操作できる範囲")]
    public float speedCapKmh = 150f;

    public enum Kind { Time, Distance }
    // 共通モジュール(分類。実際の効果は amplify / bonuses / 専用の処理)
    public enum Module
    {
        DamageBurst, AttackSpeed, RangeProjectile, Ground, Air, Combo, FirstStrike, Finisher,
        Shield, Guard, Heal, Lifesteal, LowHp, HighHp, Movement, Exp, Mile, EnemyChallenge, BossChallenge,
        Fire, Ice, Lightning, Wind, Blood, Revive, Special,
    }

    [System.Serializable]
    public class Bonus { public EffectType type; public float value; public Bonus() { } public Bonus(EffectType t, float v) { type = t; value = v; } }

    [System.Serializable]
    public class Entry
    {
        public string abilityId = "attack_up";   // 能力(元のカードの cardId)
        public string title = "";
        public string description = "";
        public Module module = Module.Special;
        public Kind kind = Kind.Time;
        public float durationSeconds = 10f;
        public float durationMeters = 2000f;
        [Tooltip("そのカード自身の効果の倍率(1 = 増幅しない)。マイナスの効果も同じ倍率")]
        public float amplify = 1f;
        [Tooltip("ACTIVE の間だけ足す効果")]
        public List<Bonus> bonuses = new List<Bonus>();
        public float power = 1.5f;                // 専用の処理の主な値(代表9枚)
        public float power2 = 0f;                 // 副の値
        public Color aura = new Color(1f, 0.55f, 0.2f, 1f);
    }
    public List<Entry> entries = DefaultEntries();

    static Color AuraOf(Module m)
    {
        switch (m)
        {
            case Module.Fire: return new Color(1f, 0.35f, 0.1f);
            case Module.Ice: return new Color(0.55f, 0.85f, 1f);
            case Module.Lightning: return new Color(1f, 0.95f, 0.4f);
            case Module.Wind: return new Color(0.6f, 1f, 0.75f);
            case Module.Blood: case Module.Lifesteal: return new Color(0.85f, 0.1f, 0.2f);
            case Module.Shield: case Module.Guard: case Module.HighHp: return new Color(0.6f, 0.75f, 1f);
            case Module.Heal: case Module.Revive: return new Color(1f, 0.6f, 0.15f);
            case Module.Movement: return new Color(0.45f, 0.85f, 1f);
            case Module.Exp: return new Color(0.55f, 1f, 0.6f);
            case Module.Mile: return new Color(1f, 0.85f, 0.2f);
            case Module.EnemyChallenge: case Module.BossChallenge: case Module.LowHp: return new Color(0.75f, 0.2f, 0.95f);
            case Module.Air: return new Color(0.7f, 0.9f, 1f);
            case Module.Ground: return new Color(0.85f, 0.65f, 0.35f);
            default: return new Color(1f, 0.55f, 0.2f);
        }
    }
    static Entry T(string id, Module m, float secs, float amp, string title, string desc, params Bonus[] b) =>
        new Entry { abilityId = id, module = m, kind = Kind.Time, durationSeconds = secs, amplify = amp, title = title, description = desc, bonuses = new List<Bonus>(b), aura = AuraOf(m) };
    static Entry D(string id, Module m, float meters, float amp, string title, string desc, params Bonus[] b) =>
        new Entry { abilityId = id, module = m, kind = Kind.Distance, durationMeters = meters, amplify = amp, title = title, description = desc, bonuses = new List<Bonus>(b), aura = AuraOf(m) };
    static Bonus B(EffectType t, float v) => new Bonus(t, v);

    // 全カード(#100 ULTIMATE = character_ultimate は対象外)。cardId は Resources/Cards/*.asset で確認済み(2026-10-05、100枚)
    public static List<Entry> DefaultEntries()
    {
        var l = new List<Entry>
        {
            // ---- 代表9枚(第1段階の専用の処理。基本の効果は変えない) ----
            new Entry { abilityId = "attack_up", module = Module.DamageBurst, title = "攻撃限界突破", description = "10秒間 最終ダメージ×1.5", kind = Kind.Time, durationSeconds = 10f, power = 1.5f, aura = new Color(1f, 0.45f, 0.15f) },
            new Entry { abilityId = "speed_up", module = Module.Movement, title = "超高速状態", description = "8秒間 加速・接敵時に自動の小攻撃・障害物と接触から守る", kind = Kind.Time, durationSeconds = 8f, power = 1.15f, power2 = 0.5f, aura = new Color(0.45f, 0.85f, 1f) },
            new Entry { abilityId = "attack_range_up", module = Module.RangeProjectile, title = "画面を切り裂く射程", description = "10秒間 射程が大きく伸び、命中時に斬撃波", kind = Kind.Time, durationSeconds = 10f, power = 1.6f, power2 = 0.35f, aura = new Color(0.7f, 0.95f, 1f) },
            new Entry { abilityId = "vampire", module = Module.Lifesteal, title = "血の飢え", description = "10秒間 吸収の確率が大きく上がり、溢れた分は Blood Shield", kind = Kind.Time, durationSeconds = 10f, power = 0.35f, power2 = 2f, aura = new Color(0.85f, 0.1f, 0.2f) },
            new Entry { abilityId = "phoenix", module = Module.Revive, title = "不死鳥状態", description = "10秒間 致死の被弾から1回だけ緊急復活(通常の PHOENIX とは別。使わずに終わると消える)", kind = Kind.Time, durationSeconds = 10f, power = 0.5f, aura = new Color(1f, 0.6f, 0.15f) },
            new Entry { abilityId = "flame_blade", module = Module.Fire, title = "炎獄", description = "10秒間 強化された炎上と、周囲への延焼", kind = Kind.Time, durationSeconds = 10f, power = 0.35f, power2 = 1.5f, aura = new Color(1f, 0.35f, 0.1f) },
            new Entry { abilityId = "thunder_strike", module = Module.Lightning, title = "雷神状態", description = "10秒間 落雷しやすく、連鎖が1つ増える", kind = Kind.Time, durationSeconds = 10f, power = 0.25f, power2 = 1f, aura = new Color(1f, 0.95f, 0.4f) },
            new Entry { abilityId = "exp_up", module = Module.Exp, title = "経験値覚醒", description = "次の2,000m EXP の強化を追加(減衰の曲線はそのまま)", kind = Kind.Distance, durationMeters = 2000f, power = 0.6f, aura = new Color(0.55f, 1f, 0.6f) },
            new Entry { abilityId = "greed", module = Module.Mile, title = "黄金暴走", description = "次の2,000m MILE×1.5 / 受けるダメージ×1.3", kind = Kind.Distance, durationMeters = 2000f, power = 1.5f, power2 = 1.3f, aura = new Color(1f, 0.85f, 0.2f) },

            // ---- MOVEMENT(実速度は人が操作できる範囲。SPEED DOWN は速くしない) ----
            T("speed_down", Module.Movement, 9f, 1.5f, "精密走法", "9秒間 さらに遅くなるが、攻撃/攻撃速度/被弾後の無敵が上がる", B(EffectType.AttackPct, 0.25f), B(EffectType.AttackSpeedPct, 0.15f), B(EffectType.HurtInvincibleSeconds, 0.3f)),
            T("jump_power_up", Module.Movement, 8f, 2.5f, "天翔", "8秒間 ジャンプ力の効果×2.5、空中攻撃+15%", B(EffectType.AirPct, 0.15f)),
            T("jump_count_up", Module.Air, 8f, 1.6f, "多段跳躍", "8秒間 空中ジャンプの回数×1.6、空中攻撃+10%", B(EffectType.AirPct, 0.1f)),
            T("sky_runner", Module.Air, 9f, 2f, "空を駆ける者", "9秒間 ジャンプ力/空中攻撃の効果×2、空中ジャンプ+2", B(EffectType.JumpCount, 2f)),
            T("momentum", Module.Movement, 8f, 2f, "加速の化身", "8秒間 速さが攻撃になる効果×2"),
            T("overdrive", Module.Movement, 8f, 1.8f, "限界駆動", "8秒間 OVERDRIVE の効果×1.8(入りやすく、強く)"),
            T("close_call", Module.Movement, 8f, 1.8f, "紙一重", "8秒間 速さ/被弾後の無敵×1.8、さらに無敵+0.4秒", B(EffectType.HurtInvincibleSeconds, 0.4f)),

            // ---- ATTACK ----
            T("attack_speed_up", Module.AttackSpeed, 10f, 2.2f, "超連撃", "10秒間 攻撃速度の効果×2.2"),
            T("air_attack_up", Module.Air, 10f, 2f, "空の覇者", "10秒間 空中攻撃の効果×2、空中ジャンプ+1", B(EffectType.JumpCount, 1f)),
            T("brake_attack", Module.DamageBurst, 10f, 1.8f, "制動の一撃", "10秒間 攻撃力と、攻撃後の減速が×1.8"),
            T("first_strike", Module.FirstStrike, 10f, 2.5f, "必殺の初撃", "10秒間 初撃の効果×2.5"),
            T("combo_plus", Module.Finisher, 10f, 2.2f, "締めの極意", "10秒間 締めの効果×2.2、締めで衝撃(COMBO MASTER 相当+3)", B(EffectType.ComboMasterLevel, 3f)),
            T("mob_killer", Module.DamageBurst, 10f, 2f, "殲滅者", "10秒間 雑魚への攻撃/撃破のMILEの効果×2"),
            T("ground_fighter", Module.Ground, 10f, 2.2f, "大地の拳", "10秒間 地上攻撃の効果×2.2、地上の締めで地を走る衝撃(+3)", B(EffectType.GroundBreakerLevel, 3f)),
            T("combo_edge", Module.Combo, 10f, 2.2f, "連撃の刃", "10秒間 連続ヒットの攻撃上昇×2.2"),
            T("piercing_blade", Module.RangeProjectile, 10f, 1.8f, "全貫通", "10秒間 貫通の数と威力×1.8"),
            T("shockwave", Module.RangeProjectile, 10f, 1.8f, "大衝撃波", "10秒間 衝撃波の威力と範囲×1.8"),
            T("aerial_blade", Module.Air, 10f, 1.8f, "空裂", "10秒間 空中の追い斬り×1.8"),
            T("boss_killer", Module.BossChallenge, 12f, 2f, "王殺し", "12秒間 ボスへの攻撃の効果×2"),
            T("chain_explosion", Module.Special, 10f, 1.7f, "誘爆連鎖", "10秒間 撃破の爆発×1.7(連鎖の上限はそのまま)"),
            T("double_attack", Module.AttackSpeed, 10f, 2.2f, "二重の刃", "10秒間 追加の1撃の確率×2.2(追加からは出ない)"),
            T("sonic_blade", Module.RangeProjectile, 10f, 1.8f, "音速の斬撃", "10秒間 飛ぶ斬撃の大きさ/強さ×1.8"),
            T("rapid_edge", Module.AttackSpeed, 10f, 2.2f, "神速の手数", "10秒間 主攻撃の攻撃速度の効果×2.2"),
            T("long_blade", Module.RangeProjectile, 10f, 1.8f, "長刃", "10秒間 攻撃範囲の効果×1.8"),
            T("sky_master", Module.Air, 10f, 1.8f, "天空の主", "10秒間 空中攻撃と空中ジャンプの取り戻し×1.8"),
            T("heavy_impact", Module.Ground, 10f, 2f, "大地割り", "10秒間 下攻撃/叩きつけ/着地の衝撃×2"),
            T("air_dominion", Module.Air, 10f, 2f, "制空", "10秒間 空中で当て続けるほど上がる効果×2"),
            T("combo_master", Module.Finisher, 10f, 1.8f, "奥義連舞", "10秒間 締めの特殊な衝撃×1.8"),
            T("high_voltage", Module.Lightning, 10f, 1.8f, "超高電圧", "10秒間 雷の威力/足止め×1.8"),
            T("frost_edge", Module.Ice, 10f, 1.8f, "氷結の刃", "10秒間 冷えの確率/減速×1.8、凍結に必要な冷え-1", B(EffectType.FreezeThresholdReduce, 1f)),
            T("wind_cutter", Module.Wind, 10f, 1.8f, "疾風刃", "10秒間 風刃の確率/威力×1.8、貫通+1", B(EffectType.WindPierce, 1f)),
            T("ground_breaker", Module.Ground, 10f, 1.8f, "地脈崩し", "10秒間 地面を走る衝撃波×1.8"),
            T("combo_rush", Module.Combo, 10f, 2f, "連撃怒涛", "10秒間 連撃の途中の攻撃/主攻撃の速さ×2"),
            T("air_strike", Module.Air, 10f, 2f, "対空の極み", "10秒間 空中の敵への攻撃×2"),
            T("hunter", Module.BossChallenge, 12f, 2f, "狩人の本能", "12秒間 ボス戦での踏み込み/接近の速さ×2"),
            T("burning_soul", Module.Fire, 10f, 1.8f, "燃え盛る魂", "10秒間 炎上の強さ/長さ×1.8"),
            T("inferno", Module.Fire, 10f, 1.8f, "業火", "10秒間 燃えている敵の撃破で広がる炎×1.8"),
            T("ice_prison", Module.Ice, 10f, 1.8f, "氷獄", "10秒間 凍結しやすさ/冷えの時間×1.8"),
            T("absolute_zero", Module.Ice, 10f, 1.8f, "絶対零度", "10秒間 凍結した雑魚の砕け/冷えた敵への攻撃×1.8"),
            T("chain_lightning", Module.Lightning, 10f, 1.8f, "雷鎖", "10秒間 連鎖の数/距離×1.8"),
            T("thunder_lord", Module.Lightning, 10f, 1.8f, "雷帝", "10秒間 雷の威力/連鎖/範囲×1.8"),
            T("gale", Module.Wind, 10f, 1.8f, "暴風", "10秒間 風刃の速さ/距離/貫通×1.8"),
            T("tornado", Module.Wind, 10f, 1.8f, "大竜巻", "10秒間 竜巻の威力と範囲×1.8"),

            // ---- DEFENSE(最大HPを増やすだけにしない) ----
            T("heart_up", Module.Shield, 10f, 1.6f, "鉄の心臓", "10秒間 最大HPの増加×1.6(上限ハート20。終われば戻る)、回復+1", B(EffectType.HealBonusHearts, 1f)),
            T("shield", Module.Shield, 10f, 2f, "盾の嵐", "10秒間 Shield の数と戻る速さ×2"),
            T("counter", Module.Guard, 10f, 2f, "倍返し", "10秒間 Shield で防いだ時の反撃×2"),
            T("iron_will", Module.HighHp, 10f, 2f, "鋼の意志", "10秒間 満タンの間の攻撃/最大HP×2"),
            T("perfect_guard", Module.Guard, 10f, 2f, "完全防御", "10秒間 Shield を使った時の無敵×2"),
            T("flame_counter", Module.Fire, 10f, 2f, "炎の報復", "10秒間 Shield で防いだ時の炎の反撃×2"),
            T("last_chance", Module.Guard, 10f, 2f, "最後の砦", "10秒間 ハート1つになった瞬間の無敵×2"),

            // ---- HEAL / BLOOD(無限の回復/復活はしない) ----
            T("overheal", Module.Heal, 10f, 2f, "溢れる生命", "10秒間 回復量と、溢れた分の Shield×2"),
            T("blood_rush", Module.Blood, 10f, 2f, "血の昂り", "10秒間 HP が低い時の攻撃/吸収×2"),
            T("predator", Module.Lifesteal, 10f, 2f, "捕食者", "10秒間 吸収の確率×2、ボスからも吸収しやすい"),
            T("second_wind", Module.Heal, 10f, 2f, "不屈", "10秒間 瀕死からの回復の量×2(クールダウンも短く)"),
            T("blood_blade", Module.Blood, 10f, 1.8f, "血刃", "10秒間 出血の確率/威力×1.8"),

            // ---- GROWTH / EXP(EXP の枠と減衰の曲線を必ず通す) ----
            D("pathfinder", Module.Exp, 2000f, 2f, "道を拓く者", "次の2,000m 距離の経験値の効果×2(曲線を通す)"),
            D("long_haul", Module.Exp, 2000f, 1.8f, "長旅の糧", "次の2,000m 経験値/最大HPの効果×1.8"),
            D("level_break", Module.Exp, 1500f, 1.8f, "限界突破の学び", "次の1,500m 経験値の効果×1.8(曲線を通す)"),
            D("experience_burst", Module.Exp, 2000f, 1.8f, "経験の奔流", "次の2,000m 敵/ボスの経験値の効果×1.8"),
            D("the_long_road", Module.Exp, 2500f, 1.6f, "果てなき道", "次の2,500m 経験値/最大HPの効果×1.6"),
            D("treasure_hunter", Module.Mile, 2000f, 2f, "宝の嗅覚", "次の2,000m 宝・報酬/ボス/距離の MILE の効果×2"),

            // ---- MILE / CHALLENGE / RISK(リスクも同じ倍率で強くなる) ----
            T("berserker", Module.LowHp, 10f, 1.8f, "狂戦士", "10秒間 攻撃/攻撃速度×1.8(封印したハートはそのまま)"),
            D("more_enemies", Module.EnemyChallenge, 1500f, 2f, "群れを呼ぶ", "次の1,500m 敵の出現と撃破の MILE が×2"),
            D("executioner", Module.Mile, 2000f, 2f, "処刑人", "次の2,000m 撃破の MILE の効果×2"),
            D("tough_enemies", Module.EnemyChallenge, 1500f, 2f, "強敵の試練", "次の1,500m 雑魚の HP と、MILE/撃破の経験値が×2"),
            D("fast_enemies", Module.EnemyChallenge, 1500f, 1.8f, "疾風の敵", "次の1,500m 雑魚の速さと、MILE/撃破の経験値が×1.8"),
            T("last_stand", Module.LowHp, 10f, 2f, "背水", "10秒間 HP が低い時の攻撃×2"),
            T("glass_cannon", Module.DamageBurst, 10f, 1.8f, "砕ける砲台", "10秒間 攻撃×1.8(封印したハートはそのまま)"),
            T("adrenaline", Module.LowHp, 10f, 2f, "限界の鼓動", "10秒間 HP が低い時の攻撃速度×2"),
            D("monster_rush", Module.EnemyChallenge, 1500f, 2f, "魔物の奔流", "次の1,500m 敵の出現と経験値が×2"),
            D("elite_enemies", Module.EnemyChallenge, 1500f, 2f, "精鋭の行進", "次の1,500m 精鋭の確率と MILE が×2"),
            D("horde", Module.EnemyChallenge, 1500f, 1.8f, "大群", "次の1,500m 敵の出現/MILE/撃破の経験値が×1.8"),
            D("boss_challenge", Module.BossChallenge, 3000f, 2f, "王への挑戦", "次の3,000m(その間に始まるボス戦)ボスの HP とボス MILE が×2"),
            T("giant_slayer", Module.DamageBurst, 10f, 1.8f, "巨人殺し", "10秒間 攻撃×1.8、攻撃速度の低下も×1.8(超重量の一撃)"),
            T("berserk_drive", Module.LowHp, 10f, 1.8f, "狂乱", "10秒間 HP が低い時の攻撃/攻撃速度×1.8"),
            T("reverse_gear", Module.DamageBurst, 10f, 1.8f, "逆走の力", "10秒間 攻撃と速さの低下が×1.8"),
            T("heart_breaker", Module.DamageBurst, 10f, 1.8f, "砕けた心", "10秒間 攻撃と、封印したハートごとの攻撃×1.8"),
            D("exp_converter", Module.Mile, 2000f, 2f, "錬金", "次の2,000m 経験値の低下と MILE の増加が×2"),
            T("ground_zero", Module.Ground, 10f, 1.8f, "地上の鬼", "10秒間 地上の攻撃と空中の低下が×1.8"),
            T("skybound", Module.Air, 10f, 1.8f, "天上の鬼", "10秒間 空中の攻撃と地上の低下が×1.8"),
            D("hell_mode", Module.EnemyChallenge, 1500f, 1.8f, "地獄の門", "次の1,500m 雑魚の HP/動き/出現と MILE/ボス MILE が×1.8"),
            D("boss_rush", Module.BossChallenge, 3000f, 2f, "王の行列", "次の3,000m ボスがもう1体加わる確率とボス MILE が×2"),
            D("wanted", Module.EnemyChallenge, 2000f, 1.8f, "賞金稼ぎ", "次の2,000m 賞金首の強さ/頻度と MILE が×1.8"),
            T("deaths_contract", Module.DamageBurst, 10f, 1.8f, "死神の契約", "10秒間 攻撃×1.8(封印したハートはそのまま)"),
            T("heavy_armor", Module.Guard, 10f, 1.8f, "重装の極み", "10秒間 最大HPの成長×1.8・のけぞり-40%・被弾後の無敵+0.3秒(ジャンプ力の低下も×1.8)", B(EffectType.HurtKnockbackReduce, 0.4f), B(EffectType.HurtInvincibleSeconds, 0.3f)),
            T("fortress", Module.HighHp, 10f, 1.8f, "不落の城塞", "10秒間 最大HP/のけぞりの軽減×1.8・Shield+1(移動速度の低下も×1.8)", B(EffectType.ShieldCapacity, 1f)),
            D("no_turning_back", Module.Movement, 1500f, 1.6f, "振り返らない", "次の1,500m 速さと敵の出現が×1.6、MILE+40%", B(EffectType.MileGainMultiplier, 0.4f)),
            D("pandemonium", Module.EnemyChallenge, 1500f, 1.6f, "万魔殿", "次の1,500m 敵/精鋭/ボスの強化と MILE/ボス MILE が×1.6"),

            // ---- SPECIAL ----
            T("ultimate", Module.Special, 10f, 1.6f, "万能の完成", "10秒間 攻撃/最大HP/速さ×1.6(どれも専門のカードには及ばない)"),
            D("one_more_mile", Module.Mile, 2000f, 1.6f, "もう1マイル", "次の2,000m MILE/ボス MILE/経験値の効果×1.6"),
        };
        return l;
    }

    static FinalEvolutionTuning cached;
    static bool loaded;
    public static FinalEvolutionTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<FinalEvolutionTuning>("FinalEvolution/FinalEvolutionTuning");
                if (cached == null) { cached = CreateInstance<FinalEvolutionTuning>(); cached.hideFlags = HideFlags.DontSave; }
                if (cached.entries == null) cached.entries = new List<Entry>();
                // アセットに無いカード(第1段階のアセットは代表9枚だけ)は既定値で補う
                foreach (var d in DefaultEntries()) if (cached.For(d.abilityId) == null) cached.entries.Add(d);
                foreach (var e in cached.entries) if (e != null && e.bonuses == null) e.bonuses = new List<Bonus>();
            }
            return cached;
        }
    }

    public Entry For(string abilityId)
    {
        if (entries != null) foreach (var e in entries) if (e != null && e.abilityId == abilityId) return e;
        return null;
    }
}

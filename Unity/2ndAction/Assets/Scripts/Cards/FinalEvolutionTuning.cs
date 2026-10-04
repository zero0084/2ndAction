using System.Collections.Generic;
using UnityEngine;

// FINAL EVOLUTION(2026-10-04 第1段階)の調整値。Resources/FinalEvolution/FinalEvolutionTuning.asset(無ければコードの既定値)。
// カードLvは9のまま(Lv10は作らない)。Lv9で資格 → 追加で readyMeters 走ると READY → LEVEL UP の候補(最大1枠)→ 選ぶと
// 一定時間/一定距離だけ限界突破 → 終われば通常の Lv9 MAX。通常の性能値(Card Balance V3)には触らない。
[CreateAssetMenu(menuName = "OneMoreMile/Final Evolution Tuning")]
public class FinalEvolutionTuning : ScriptableObject
{
    [Tooltip("資格(能力Lv9)を得てから READY になるまでに走る距離(m)")]
    public float readyMeters = 5000f;
    [Tooltip("1つの能力をこのランで FINAL EVOLUTION できる回数(第1試作は1)")]
    public int usesPerRun = 1;
    [Tooltip("AWAKENED 済みのカードの持続(秒/距離)の追加の割合(小さな特典)")]
    public float awakenedDurationBonus = 0.1f;
    [Tooltip("マルチでは候補に出さない(同期が未対応のため。Docs/Multiplayer8.md)")]
    public bool disableInMultiplayer = true;
    [Tooltip("SPEED の FINAL EVOLUTION で上げる実速度の上限(km/h)。人が操作できる範囲")]
    public float speedCapKmh = 150f;

    public enum Kind { Time, Distance }

    [System.Serializable]
    public class Entry
    {
        public string abilityId = "attack_up";   // 能力(元のカードの cardId)
        public string title = "";
        public string description = "";
        public Kind kind = Kind.Time;
        public float durationSeconds = 10f;
        public float durationMeters = 2000f;
        public float power = 1.5f;                // 主な倍率(カードごとに意味が違う。下の既定値の説明)
        public float power2 = 0f;                 // 副の値
        public Color aura = new Color(1f, 0.55f, 0.2f, 1f);
    }
    public List<Entry> entries = DefaultEntries();

    // 代表9枚(実際の cardId を確認済み: Resources/Cards/*.asset)
    public static List<Entry> DefaultEntries() => new List<Entry>
    {
        // 最終ダメージ×power(攻撃の枠 AttackPct は変えない)
        new Entry { abilityId = "attack_up", title = "攻撃限界突破", description = "10秒間 最終ダメージ×1.5", kind = Kind.Time, durationSeconds = 10f, power = 1.5f, aura = new Color(1f, 0.45f, 0.15f) },
        // 実速度×power(ただし speedCapKmh まで)+接敵の自動小攻撃+障害物/接触から守る+強い残像+カメラが少し引く
        new Entry { abilityId = "speed_up", title = "超高速状態", description = "8秒間 加速・接敵時に自動の小攻撃・障害物と接触から守る", kind = Kind.Time, durationSeconds = 8f, power = 1.15f, power2 = 0.5f, aura = new Color(0.45f, 0.85f, 1f) },
        // 射程×power+命中時に先端から斬撃波(power2=斬撃波のダメージの割合)
        new Entry { abilityId = "attack_range_up", title = "画面を切り裂く射程", description = "10秒間 射程が大きく伸び、命中時に斬撃波", kind = Kind.Time, durationSeconds = 10f, power = 1.6f, power2 = 0.35f, aura = new Color(0.7f, 0.95f, 1f) },
        // 吸収の確率+power、溢れた回復は Blood Shield(power2=上限の枚数)。終われば消える
        new Entry { abilityId = "vampire", title = "血の飢え", description = "10秒間 吸収の確率が大きく上がり、溢れた分は Blood Shield", kind = Kind.Time, durationSeconds = 10f, power = 0.35f, power2 = 2f, aura = new Color(0.85f, 0.1f, 0.2f) },
        // 間に1回だけ致死の被弾から専用の緊急復活(power=復活時のHPの割合)。通常の PHOENIX Charge は増やさない
        new Entry { abilityId = "phoenix", title = "不死鳥状態", description = "10秒間 致死の被弾から1回だけ緊急復活(通常の PHOENIX とは別)", kind = Kind.Time, durationSeconds = 10f, power = 0.5f, aura = new Color(1f, 0.6f, 0.15f) },
        // 炎上の確率+power、炎上の強さ×power2、炎上させた相手の周りへ小さく延焼(再帰しない)
        new Entry { abilityId = "flame_blade", title = "炎獄", description = "10秒間 強化された炎上と、周囲への延焼", kind = Kind.Time, durationSeconds = 10f, power = 0.35f, power2 = 1.5f, aura = new Color(1f, 0.35f, 0.1f) },
        // 落雷の確率+power、連鎖+power2(1体へ0.4秒に1回の制限はそのまま)
        new Entry { abilityId = "thunder_strike", title = "雷神状態", description = "10秒間 落雷しやすく、連鎖が1つ増える", kind = Kind.Time, durationSeconds = 10f, power = 0.25f, power2 = 1f, aura = new Color(1f, 0.95f, 0.4f) },
        // EXP の枠へ+power(曲線の前に足すので、減衰の曲線は迂回しない)
        new Entry { abilityId = "exp_up", title = "経験値覚醒", description = "次の2,000m EXP の強化を追加(減衰の曲線はそのまま)", kind = Kind.Distance, durationMeters = 2000f, power = 0.6f, aura = new Color(0.55f, 1f, 0.6f) },
        // MILE×power、受けるダメージ×power2(リスク)
        new Entry { abilityId = "greed", title = "黄金暴走", description = "次の2,000m MILE×1.5 / 受けるダメージ×1.3", kind = Kind.Distance, durationMeters = 2000f, power = 1.5f, power2 = 1.3f, aura = new Color(1f, 0.85f, 0.2f) },
    };

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
                if (cached.entries == null || cached.entries.Count == 0) cached.entries = DefaultEntries();
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

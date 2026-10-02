using System.Collections.Generic;
using UnityEngine;

// ボスの再戦(2026-10-02)の調整値。Resources/Bosses/BossRematchTuning(無ければコードの既定値)。
//  ・1,000m/5,000mの関門は、そのランで「撃破済み」のボスから重み付きで抽選する(各関門の本来のボスを一度倒すまでは本来のボス)。
//  ・10,000mごとの専用ボスの初登場は固定(抽選しない)。100,000mの死神は対象外。
//  ・再戦のボスは今の距離に合わせて強くする(HP=その種類の初登場距離→今の距離の節目の比、さらに段階ごとの倍率)。
[CreateAssetMenu(menuName = "OneMoreMile/Boss Rematch Tuning")]
public class BossRematchTuning : ScriptableObject
{
    [Tooltip("直近何体を抽選から外すか(1=直前だけ / 2=直近2体)。候補が無くなる時だけ緩める")]
    public int recentExclude = 2;
    [Tooltip("再戦のHPを今の距離に合わせる(初登場距離の節目HP → 今の距離の節目HP)")]
    public bool distanceHp = true;

    [System.Serializable]
    public class Tier
    {
        public string label = "通常再戦";
        public float fromMeters = 0f;
        public float hpMul = 1f;          // 距離の比に追加で掛ける
        public float damageMul = 1f;      // Playerへの被弾量
        public float staggerMul = 1f;     // 崩しゲージの大きさ(大きい=崩れにくい)
        public float cooldownMul = 1f;    // 特殊攻撃/必殺技の間隔(小さい=多く使う)
        [Range(0f, 0.4f)] public float phaseShift = 0f; // 段階の境目を早める(残りHPの割合に足す)
        public bool extraPhase = false;   // 段階を1つ足す(最後の段階の半分の所)
    }
    public List<Tier> tiers = DefaultTiers();

    [System.Serializable]
    public class Weight
    {
        public string key = "Wild/Wolf";  // Wild/<WildBossKind> / Cave/<CaveBossKind> / Sky/<SkyBossKind>
        public float weight = 1f;          // 基本
        public float weight1k = 1f;        // 1,000mの関門での倍率(軽いボス中心にする時は大型を下げる)
        public float weight5k = 1f;        // 5,000mの関門での倍率(強いボスを出やすくする時に上げる)
    }
    [Tooltip("無い種類は全部1.0")]
    public List<Weight> weights = new List<Weight>();

    public static List<Tier> DefaultTiers() => new List<Tier>
    {
        new Tier { label = "通常再戦", fromMeters = 0f },
        new Tier { label = "強化再戦", fromMeters = 30000f, hpMul = 1.1f, damageMul = 1.2f, staggerMul = 1.15f, cooldownMul = 0.85f, phaseShift = 0.05f },
        new Tier { label = "上位強化再戦", fromMeters = 50000f, hpMul = 1.2f, damageMul = 1.5f, staggerMul = 1.3f, cooldownMul = 0.75f, phaseShift = 0.1f, extraPhase = true },
        new Tier { label = "最終強化再戦", fromMeters = 80000f, hpMul = 1.3f, damageMul = 2f, staggerMul = 1.5f, cooldownMul = 0.6f, phaseShift = 0.15f, extraPhase = true },
    };

    static BossRematchTuning cached;
    static bool loaded;
    public static BossRematchTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<BossRematchTuning>("Bosses/BossRematchTuning");
                if (cached == null) { cached = CreateInstance<BossRematchTuning>(); cached.hideFlags = HideFlags.DontSave; }
                if (cached.tiers == null || cached.tiers.Count == 0) cached.tiers = DefaultTiers();
            }
            return cached;
        }
    }

    public Tier TierAt(float meters)
    {
        Tier best = tiers[0];
        foreach (var t in tiers) if (t != null && meters >= t.fromMeters && t.fromMeters >= best.fromMeters) best = t;
        return best;
    }
    public int TierIndexAt(float meters) => Mathf.Max(0, tiers.IndexOf(TierAt(meters)));

    public float WeightFor(string key, bool fiveK)
    {
        if (weights != null)
            foreach (var w in weights)
                if (w != null && w.key == key) return Mathf.Max(0f, w.weight * (fiveK ? w.weight5k : w.weight1k));
        return 1f;
    }
}

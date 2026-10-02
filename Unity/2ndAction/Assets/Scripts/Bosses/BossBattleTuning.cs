using System.Collections.Generic;
using UnityEngine;

// ボス戦の強化(2026-10-01)の調整値。Resources/Bosses/BossBattleTuning.asset(無ければコードの既定値)。
// 「HPの多い雑魚」にしないための仕組み全体の数値をここへ集める:
//  ・ラン再開: ボス戦開始(ボスの登場が終わってから)この秒数で倒せなければ、通常のラン(距離/雑魚/障害物)を再開する。ボスは残る。
//  ・次のボス: ボスが残っている間は次の距離のボスを出さない。撃破後、通過していた関門のうち1つだけを保留から出す(または全部飛ばす)。
//  ・段階(Phase)/必殺技/崩し(Stagger→BREAK)はボスの種類ごと(entries。無い種類は既定値)。
public enum BossPendingMode { PendingOne, Skip }

[CreateAssetMenu(menuName = "OneMoreMile/Boss Battle Tuning")]
public class BossBattleTuning : ScriptableObject
{
    [Header("ラン再開(秒。ボスの登場が終わってから数える)")]
    public float resumeNormal = 18f;   // 1,000m系
    public float resumeStrong = 22f;   // 5,000m系
    public float resumeSpecial = 30f;  // 10,000m専用
    [Tooltip("ラン再開を使うステージ(ラストダンジョンは独自の流れなので含めない)")]
    public string[] resumeStages = { "wasteland_road" }; // 洞窟/天空は "natural_cave"/"sky_corridor" を足す
    [Tooltip("再開してから雑魚の出現を戻すまでの秒数")]
    public float zakoResumeDelay = 1.5f;
    [Tooltip("ラン再開を知らせる表示の秒数")]
    public float resumeBannerSeconds = 2.4f;

    [Header("次のボス")]
    public BossPendingMode pendingMode = BossPendingMode.PendingOne;
    [Tooltip("保留していたボスを、前のボスの報酬が終わってから出すまでの安全時間(秒)")]
    public float pendingSafeDelay = 3f;

    [Header("必殺技中の安全策")]
    [Tooltip("ボスが必殺技の溜め〜実行中、雑魚の攻撃の間隔をこの倍率で遅らせる(1=変えない)")]
    public float zakoAttackScaleDuringUltimate = 0.35f;
    [Tooltip("必殺技の溜め〜実行中は新しい障害物を置かない")]
    public bool suppressObstaclesDuringUltimate = true;
    [Tooltip("必殺技の被弾(ハートの数)。満タンから1発で倒れることはない")]
    public int ultimateDamage = 20; // 2026-10-02: 10倍スケール(ハート2つ)

    [Header("ボス戦の攻撃の前進/後退(戦っているボスがいる間だけ)")]
    [Tooltip("ボスの正面まで lungeFarDistance 以上離れている時の前進の倍率")]
    public float lungeForwardFar = 2.6f;
    [Tooltip("ボスの正面まで lungeNearDistance 以下の時の前進の倍率(近い時は伸ばしすぎない)")]
    public float lungeForwardNear = 1.4f;
    public float lungeFarDistance = 5f;
    public float lungeNearDistance = 1.0f;
    [Tooltip("後退の倍率")]
    public float lungeBackScale = 1.6f;

    [Header("崩し(Stagger)の溜まり方: 攻撃の種類ごと")]
    public float staggerNormal = 1f;
    public float staggerUp = 3.2f;       // 上攻撃(打ち上げ)
    public float staggerDown = 2.4f;     // 下攻撃(落下)
    public float staggerDownImpact = 4.5f; // 下攻撃の着地衝撃(Ground Slam)
    public float staggerAirBonus = 1.5f; // 空中で当てた攻撃(エアリアルコンボ)
    public float staggerReflect = 10f;   // 跳ね返した弾が当たった
    [Tooltip("最後に崩しが溜まってから回復が始まるまでの秒数")]
    public float staggerRecoveryDelay = 2f;
    [Tooltip("BREAK中に受けるダメージの倍率")]
    public float breakDamageScale = 1.35f;

    [System.Serializable]
    public class Entry
    {
        public string key = "Wolf";               // WildBossKind/CaveBossKind/SkyBossKindの名前(Dragonは荒野街道のドラゴン)
        public float hpScale = 1f;
        [Tooltip("段階の境目(残りHPの割合、降順)。{0.5}=2段階、{0.7,0.3}=3段階")]
        public float[] phaseThresholds = { 0.5f };
        public float specialCooldown = 7f;        // 段階で解禁される特殊攻撃の最短間隔
        public float ultimateCooldown = 16f;      // 必殺技の最短間隔
        public float firstUltimateDelay = 2f;     // 必殺技が解禁されてから最初に使うまで
        public float staggerMax = 24f;            // 0=崩しなし
        public float staggerRecoveryPerSec = 2.5f;
        public float breakDuration = 3f;
        [Tooltip("ラン再開の秒数(0以下=距離の種類ごとの既定)")]
        public float resumeSecondsOverride = 0f;
        [Tooltip("ボスの攻撃の被弾倍率(1=ハート1つ。必殺技は ultimateDamage)")]
        public int normalDamage = 10; // 2026-10-02: 10倍スケール(ハート1つ)

        public Entry Clone()
        {
            var e = (Entry)MemberwiseClone();
            e.phaseThresholds = phaseThresholds != null ? (float[])phaseThresholds.Clone() : new float[0];
            return e;
        }
    }
    public List<Entry> entries = new List<Entry>();

    // ===================================================================== //
    static BossBattleTuning cached;
    static bool loaded;
    public static BossBattleTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<BossBattleTuning>("Bosses/BossBattleTuning");
                if (cached == null) { cached = CreateInstance<BossBattleTuning>(); cached.entries = DefaultEntries(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }

    static readonly Entry Fallback = new Entry { key = "*", phaseThresholds = new float[0], staggerMax = 0f };

    public Entry For(string key)
    {
        if (entries != null) foreach (var e in entries) if (e != null && e.key == key) return e;
        return Fallback;
    }

    public float StaggerFor(PlayerAttackKind kind, bool airborne)
    {
        float v = kind == PlayerAttackKind.Up ? staggerUp : kind == PlayerAttackKind.Down ? staggerDown : kind == PlayerAttackKind.DownImpact ? staggerDownImpact : staggerNormal;
        return airborne && kind != PlayerAttackKind.DownImpact ? v * staggerAirBonus : v;
    }

    // 荒野街道を先行(2026-10-01)。自然洞窟/天空回廊の種類は、ここへ足せば同じ仕組みが動く(未登録=段階/崩しなし=従来どおり)。
    public static List<Entry> DefaultEntries()
    {
        return new List<Entry>
        {
            new Entry { key = "Wolf",        phaseThresholds = new[] { 0.55f },        specialCooldown = 6.5f, ultimateCooldown = 0f,  staggerMax = 14f, staggerRecoveryPerSec = 2f,  breakDuration = 2.4f },
            new Entry { key = "GoblinRider", phaseThresholds = new[] { 0.55f },        specialCooldown = 7f,   ultimateCooldown = 0f,  staggerMax = 18f, staggerRecoveryPerSec = 2f,  breakDuration = 2.6f },
            new Entry { key = "Serpent",     phaseThresholds = new[] { 0.7f, 0.35f },  specialCooldown = 7f,   ultimateCooldown = 15f, staggerMax = 22f, staggerRecoveryPerSec = 2.5f, breakDuration = 3f },
            new Entry { key = "Cyclops",     phaseThresholds = new[] { 0.7f, 0.35f },  specialCooldown = 7f,   ultimateCooldown = 16f, staggerMax = 30f, staggerRecoveryPerSec = 3f,  breakDuration = 3f },
            new Entry { key = "Spider",      phaseThresholds = new[] { 0.6f },         specialCooldown = 6f,   ultimateCooldown = 15f,  staggerMax = 24f, staggerRecoveryPerSec = 2.5f, breakDuration = 2.8f },
            new Entry { key = "Golem",       phaseThresholds = new[] { 0.7f, 0.3f },   specialCooldown = 8f,   ultimateCooldown = 16f, staggerMax = 38f, staggerRecoveryPerSec = 3.5f, breakDuration = 3.2f },
            new Entry { key = "Griffin",     phaseThresholds = new[] { 0.7f, 0.35f },  specialCooldown = 7f,   ultimateCooldown = 16f, staggerMax = 28f, staggerRecoveryPerSec = 3f,  breakDuration = 3f },
            new Entry { key = "Hydra",       phaseThresholds = new[] { 0.7f, 0.35f },  specialCooldown = 7f,   ultimateCooldown = 15f, staggerMax = 36f, staggerRecoveryPerSec = 3.5f, breakDuration = 3f },
            new Entry { key = "Demon",       phaseThresholds = new[] { 0.6f },         specialCooldown = 7f,   ultimateCooldown = 16f,  staggerMax = 32f, staggerRecoveryPerSec = 3f,  breakDuration = 3f },
            new Entry { key = "Dragon",      phaseThresholds = new[] { 0.6f },         specialCooldown = 7f,   ultimateCooldown = 15f, staggerMax = 26f, staggerRecoveryPerSec = 3f,  breakDuration = 3.2f },
            new Entry { key = "BlackKnight", phaseThresholds = new[] { 0.7f, 0.35f },  specialCooldown = 7.5f, ultimateCooldown = 16f, staggerMax = 40f, staggerRecoveryPerSec = 4f,  breakDuration = 2.8f },
        };
    }
}

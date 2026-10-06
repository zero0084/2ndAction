using System.Collections.Generic;
using UnityEngine;

// BOSS FINISH SYSTEM(2026-10-06)の調整値。Resources/Finish/BossDeathTuning(無ければコードの既定値)。
// 雑魚の FINISH(爽快感 = ぶっ飛ばす)とは別: ボスは「最後の一撃を強調し、そのボスらしく倒れる」(達成感)。
public enum BossDeathProfile { Beast, Humanoid, Flying, Golem, Serpent, Giant }
public enum BossDeathFx { None, Lightning, Crystal, CloudSink, PhoenixFire, Storm, Halo, Sink }

[CreateAssetMenu(menuName = "OneMoreMile/Boss Death Tuning")]
public class BossDeathTuning : ScriptableObject
{
    [Tooltip("BOSS FINISH を使う(false = 従来の撃破演出)")]
    public bool enabled = true;

    [Header("最後の一撃(既存の HitStop / 揺れ / カメラを使う)")]
    public float finalHitStop = 0.13f;
    public float finalShake = 0.12f;
    [Tooltip("一瞬だけ寄るカメラ(1 = 通常。小さいほど寄る)")]
    public float finalZoom = 0.94f;
    public float finalZoomSeconds = 0.22f;
    [Tooltip("最後の一撃の直後のごく短いスロー(実時間の秒。0 = 使わない)。既存の TimeControl の演出の層を使う")]
    public float slowMotionSeconds = 0.16f;
    [Range(0.1f, 1f)] public float slowMotionScale = 0.45f;

    [Header("長さ(ゲームの時間の秒。HitStop は含まない)")]
    [Tooltip("初めて倒した時(10,000m ごとの専用ボスの初撃破など)")]
    public float firstKillSeconds = 2.0f;
    [Tooltip("再戦 / ラスダンのボスラッシュ")]
    public float rematchSeconds = 1.1f;

    [Header("共通")]
    public float gravity = 30f;
    [Tooltip("最後の一撃の向きの最初の反応の強さ")]
    public float reactionScale = 1f;

    [System.Serializable]
    public class BossEntry
    {
        public string key;                 // "Wild/Wolf", "Cave/CrystalGolem", "Sky/Titan", "Dragon", "Majin", "Reaper"
        public BossDeathProfile profile;
        [Tooltip("重さ(大きいほど飛ばない)")] public float deathMass = 1f;
        [Tooltip("吹っ飛びの強さ(m/s の目安)")] public float deathKnockback = 10f;
        [Tooltip("墜落の速さ(FLYING / 崩れ落ちる速さ)")] public float deathFallSpeed = 14f;
        public BossDeathFx specialFx = BossDeathFx.None;
        public BossEntry(string k, BossDeathProfile p, float mass, float kb, float fall, BossDeathFx fx = BossDeathFx.None)
        { key = k; profile = p; deathMass = mass; deathKnockback = kb; deathFallSpeed = fall; specialFx = fx; }
    }

    [Tooltip("ボスごとの割り当て(無いボスは名前から推定)")]
    public List<BossEntry> bosses = DefaultTable();

    public static List<BossEntry> DefaultTable() => new List<BossEntry>
    {
        // 荒野街道
        new BossEntry("Wild/Wolf", BossDeathProfile.Beast, 1.0f, 13f, 14f),
        new BossEntry("Wild/GoblinRider", BossDeathProfile.Beast, 1.2f, 11f, 14f),
        new BossEntry("Wild/Serpent", BossDeathProfile.Serpent, 1.6f, 6f, 14f),
        new BossEntry("Wild/Cyclops", BossDeathProfile.Giant, 3.2f, 4f, 12f),
        new BossEntry("Wild/Spider", BossDeathProfile.Beast, 1.3f, 10f, 14f),
        new BossEntry("Wild/Golem", BossDeathProfile.Golem, 4f, 2f, 12f),
        new BossEntry("Wild/Griffin", BossDeathProfile.Flying, 1.3f, 9f, 16f),
        new BossEntry("Wild/Hydra", BossDeathProfile.Serpent, 2.6f, 4f, 13f),
        new BossEntry("Wild/Demon", BossDeathProfile.Humanoid, 1.6f, 7f, 14f),
        new BossEntry("Wild/BlackKnight", BossDeathProfile.Humanoid, 1.4f, 8f, 14f),
        new BossEntry("Dragon", BossDeathProfile.Flying, 2.2f, 8f, 15f),
        // 自然洞窟
        new BossEntry("Cave/Centipede", BossDeathProfile.Serpent, 1.4f, 6f, 14f),
        new BossEntry("Cave/Scorpion", BossDeathProfile.Beast, 1.3f, 10f, 14f),
        new BossEntry("Cave/Mole", BossDeathProfile.Beast, 1.5f, 9f, 14f),
        new BossEntry("Cave/Troll", BossDeathProfile.Giant, 3f, 4f, 12f),
        new BossEntry("Cave/Worm", BossDeathProfile.Serpent, 2.4f, 4f, 13f),
        new BossEntry("Cave/CrystalGolem", BossDeathProfile.Golem, 4f, 2f, 12f, BossDeathFx.Crystal),
        new BossEntry("Cave/Bat", BossDeathProfile.Flying, 1.0f, 9f, 16f),
        new BossEntry("Cave/ScorpionKing", BossDeathProfile.Beast, 1.9f, 8f, 14f),
        new BossEntry("Cave/Basilisk", BossDeathProfile.Beast, 2.0f, 7f, 14f),
        new BossEntry("Cave/Drake", BossDeathProfile.Beast, 2.2f, 7f, 14f),
        new BossEntry("Cave/AncientDemon", BossDeathProfile.Humanoid, 2.2f, 6f, 14f),
        // 天空回廊
        new BossEntry("Sky/Behemoth", BossDeathProfile.Beast, 2.6f, 6f, 14f, BossDeathFx.Lightning),
        new BossEntry("Sky/Titan", BossDeathProfile.Giant, 6f, 1f, 9f, BossDeathFx.Sink),
        new BossEntry("Sky/Jellyfish", BossDeathProfile.Flying, 0.9f, 8f, 13f),
        new BossEntry("Sky/Leviathan", BossDeathProfile.Serpent, 3.5f, 3f, 12f, BossDeathFx.CloudSink),
        new BossEntry("Sky/Fenrir", BossDeathProfile.Beast, 1.6f, 11f, 14f),
        new BossEntry("Sky/SkyGolem", BossDeathProfile.Golem, 4f, 2f, 12f),
        new BossEntry("Sky/Phoenix", BossDeathProfile.Flying, 1.2f, 8f, 13f, BossDeathFx.PhoenixFire),
        new BossEntry("Sky/SkySerpent", BossDeathProfile.Serpent, 2.4f, 4f, 13f, BossDeathFx.Storm),
        new BossEntry("Sky/Guardian", BossDeathProfile.Humanoid, 1.8f, 5f, 14f, BossDeathFx.Halo),
        new BossEntry("Majin", BossDeathProfile.Humanoid, 2.0f, 6f, 14f),
        new BossEntry("Reaper", BossDeathProfile.Humanoid, 1.4f, 6f, 14f),
    };

    public BossEntry For(string key)
    {
        if (bosses != null) foreach (var e in bosses) if (e != null && e.key == key) return e;
        foreach (var e in DefaultTable()) if (e.key == key) return e;
        return new BossEntry(key, BossDeathProfile.Beast, 1.5f, 8f, 14f);
    }

    static BossDeathTuning cached; static bool loaded;
    public static BossDeathTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<BossDeathTuning>("Finish/BossDeathTuning");
                if (cached == null) { cached = CreateInstance<BossDeathTuning>(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }
}

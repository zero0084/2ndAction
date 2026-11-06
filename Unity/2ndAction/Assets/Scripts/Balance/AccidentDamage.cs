using UnityEngine;

// 高速時の事故ダメージ(2026-11-06、マスターの依頼)。
//  「速いほど便利、でも速いほど事故が痛い」。プレイヤー自身の走行速度が事故の大きさに関係するものだけに掛ける:
//   ・雑魚への体当たり/接触(Enemy:、空中雑魚 Enemy:Air_ と判断イベントの帯 Enemy:LaneSweep は除く)
//   ・障害物(Obstacle:)・地形の壁(TerrainWall:)・穴/落下(DeathY)
//  対象外: 雑魚の攻撃(EnemyMelee/Fireball)・ボスの攻撃/接触・光の柱/落石/天井・判断イベントの攻撃・死神。
//  段階(DifficultyTuning の設定値、Inspector で変更可): 100km/h 未満 = 1 / 100〜129 = 2 / 130 以上 = 3 ハート(上限)。
//  距離の帯で決まった被ダメージ(DifficultyTuning.Adjust)と比べて大きい方。速度は HUD の表示と同じ「能力としての走行速度」。
public static class AccidentDamage
{
    public enum Kind { None, Contact, Obstacle, Wall, Fall }

    // 確認用(-qaTier の [accident] 行)
    public static int HitsTier1, HitsTier2, HitsTier3, HpAdded;
    public static readonly int[] HpByKind = new int[5];
    public static readonly int[] HitsByKind = new int[5];
    public static void ResetStats() { HitsTier1 = HitsTier2 = HitsTier3 = HpAdded = 0; System.Array.Clear(HpByKind, 0, 5); System.Array.Clear(HitsByKind, 0, 5); }

    public static Kind Classify(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return Kind.None;
        if (reason.StartsWith("DeathY")) return Kind.Fall;
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        if (k.StartsWith("Obstacle")) return Kind.Obstacle;
        if (k.StartsWith("TerrainWall")) return Kind.Wall;
        if (k.StartsWith("Enemy:") && !k.StartsWith("Enemy:LaneSweep") && !k.StartsWith("Enemy:Air_")) return Kind.Contact;
        return Kind.None;
    }

    static bool KindEnabled(DifficultyTuning t, Kind k) => k switch
    {
        Kind.Contact => t.accidentContact,
        Kind.Obstacle => t.accidentObstacle,
        Kind.Wall => t.accidentWall,
        Kind.Fall => t.accidentFall,
        _ => false,
    };

    public static bool Enabled
    {
        get
        {
            var gm = GameManager.Instance;
            return DifficultyTuning.I.accidentSpeedDamage && !Off && gm != null && gm.HasStarted && !ArenaMode.Active && !TutorialMode.Active;
        }
    }
    public static bool Off; // 開発版 -accident 0(比較用)
    public static float FromKmOverride = -1f; // 開発版 -accidentFromKm N

    public static float CurrentKmh => PlayerController.Instance != null ? PlayerController.Instance.CurrentRunKmh : 0f;

    // 今の速度の段階(0 = 100km/h 未満)
    public static int TierAt(float kmh)
    {
        var t = DifficultyTuning.I;
        return kmh >= t.accidentKmh2 ? 2 : kmh >= t.accidentKmh1 ? 1 : 0;
    }

    // GameManager.TryDamagePlayer から(距離の帯の調整の後)
    public static int Apply(int dmg, string reason)
    {
        if (!Enabled) return dmg;
        var kind = Classify(reason);
        if (kind == Kind.None) return dmg;
        var t = DifficultyTuning.I;
        if (!KindEnabled(t, kind)) return dmg;
        int tier = TierAt(CurrentKmh);
        float fromKm = FromKmOverride >= 0f ? FromKmOverride : t.accidentFromKm;
        if (GameManager.Instance != null && GameManager.Instance.MaxDistance < fromKm * 1000f) tier = 0; // 序盤の猶予(設定)
        int hearts = t.accidentHearts != null && t.accidentHearts.Length > tier ? t.accidentHearts[tier] : tier + 1;
        int target = Mathf.Max(1, hearts) * CombatScale.HpPerHeart;
        int result = Mathf.Max(dmg, target);
        if (tier == 0) HitsTier1++; else if (tier == 1) HitsTier2++; else HitsTier3++;
        HpAdded += result - dmg;
        HpByKind[(int)kind] += result; HitsByKind[(int)kind]++;
        return result;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-accident");
        if (i >= 0 && i + 1 < a.Length && a[i + 1] == "0") Off = true;
        i = System.Array.IndexOf(a, "-accidentFromKm");
        if (i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fk)) FromKmOverride = fk;
    }
#endif
}

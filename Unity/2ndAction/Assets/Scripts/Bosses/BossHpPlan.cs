using UnityEngine;

// ボスHPの再設計案(2026-10-02)。開発版のDEBUGで「現行 / 15発 / 20発 / 25発」を切り替えて比べるためのもの。
// 製品版(と既定)は常に「現行」= ボスごとの値(10倍スケール)のまま。マスターが実機で決めてから正式に採用する。
//
// 考え方: その距離で想定する「実戦の最大級ビルドの1発」(RefDamage) × 狙う発数 = その距離の10km節目ボスのHP。
//   ボスごとの個性は残す: 全ボスに同じ倍率(=目標HP ÷ 節目ボスの現行HPの平均)を掛ける(狼とボスの比、ステージ差はそのまま)。
// RefDamage は Docs/CombatScale.md の計算(黒剣士=3段連撃の標準的な近接、満HP・地上・速度最大・ボス特効、
// デッキ10+キャラカード3がLv9で完成した状態を上限に、距離ごとの成長度合いを掛けた値)。カードの数値を見直したら作り直す。
public static class BossHpPlan
{
    public const string PrefKey = "Dev.BossHpPlanHits";
    public static readonly int[] Choices = { 0, 15, 20, 25 }; // 0 = 現行

    // 0km, 10km, ... 100km
    static readonly float[] RefDamage = { 60f, 1606f, 2774f, 4080f, 5402f, 5710f, 6017f, 6325f, 6632f, 6940f, 6940f };
    // 10km節目ボスの現行HP(10倍スケール、荒野/洞窟/天空の平均)。0kmは最初の狼くらい。
    static readonly float[] MilestoneNow = { 300f, 600f, 920f, 1130f, 1430f, 1630f, 1930f, 2100f, 2530f, 2970f, 2970f };

    public static int Hits
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild) return 0;
            return PlayerPrefs.GetInt(PrefKey, 0);
#else
            return 0;
#endif
        }
        set
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerPrefs.SetInt(PrefKey, Mathf.Max(0, value));
            PlayerPrefs.Save();
#endif
        }
    }
    public static bool Active => Hits > 0;
    public static string Label(int hits) => hits <= 0 ? "現行" : $"{hits}発";

    static float Lerp(float[] t, float km)
    {
        float i = Mathf.Clamp(km / 10f, 0f, t.Length - 1);
        int a = Mathf.FloorToInt(i); int b = Mathf.Min(t.Length - 1, a + 1);
        return Mathf.Lerp(t[a], t[b], i - a);
    }
    public static float RefDamageAt(float distance) => Lerp(RefDamage, distance / 1000f);
    // その距離の10km節目ボスに狙うHP
    public static float TargetMilestoneHp(float distance, int hits) => RefDamageAt(distance) * hits;
    // 全ボスのHPに掛ける倍率(現行=1)
    public static float Multiplier(float distance)
    {
        int h = Hits;
        if (h <= 0) return 1f;
        float km = distance / 1000f;
        return Mathf.Max(1f, TargetMilestoneHp(distance, h) / Mathf.Max(1f, Lerp(MilestoneNow, km)));
    }
    public static float CurrentDistance => GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;
}

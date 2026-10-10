using UnityEngine;

// 終盤の硬さの調整(2026-10-11、攻略の再検証)。調整の単位ごとに切り替えられるようにまとめる(比べてから採用するため)。
//  ・LateEnemyHp: 雑魚の HP の距離による伸びを、LateHpFrom より先は LateHpEvery ごとに +1段(それまでは hpIncreaseDistance ごと)にする。
//    序盤(LateHpFrom まで)の雑魚は変わらない。ボスの HP は別(BossHpPlan/再戦)。
//  ・AttackUpHighLevel: ATTACK UP の高い Lv の伸び。Lv1〜3 は今までどおり(+5%/Lv)、Lv4 から伸びを大きくする(AttackUpLevelFactor)。
// 開発版は起動引数 -balVariant base|hp|atk|hp,atk(Android は qa_args.txt)で切り替えられる。保存はしない。
public static class BalanceTuning
{
    public static bool LateEnemyHp = false;
    public static float LateHpFrom = 20000f;   // ここまでは今までどおり(hpIncreaseDistance = 2,000m ごとに +1段)
    public static float LateHpEvery = 4000f;   // ここから先は 4,000m ごとに +1段

    public static bool AttackUpHighLevel = false;
    // 値(0.05)に掛ける数。Lv1〜3 は今までどおり(1,2,3)、Lv9 で 16(= +80%)
    static readonly float[] AttackUpLevelTable = { 0f, 1f, 2f, 3f, 4.6f, 6.2f, 7.8f, 10.5f, 13.2f, 16f };
    public static float AttackUpLevelFactor(int lv) => AttackUpLevelTable[Mathf.Clamp(lv, 0, AttackUpLevelTable.Length - 1)];

    public static string Describe => $"lateEnemyHp={(LateEnemyHp ? $"on(from {LateHpFrom:F0}m every {LateHpEvery:F0}m)" : "off")} attackUpHighLevel={(AttackUpHighLevel ? "on" : "off")}";

    // 雑魚の HP の距離の段(DistanceTierManager.CurrentEnemyHp)
    public static int HpSteps(float distance, float increaseDistance)
    {
        float inc = Mathf.Max(1f, increaseDistance);
        if (!LateEnemyHp || distance <= LateHpFrom) return Mathf.FloorToInt(distance / inc);
        return Mathf.FloorToInt(LateHpFrom / inc) + Mathf.FloorToInt((distance - LateHpFrom) / Mathf.Max(1f, LateHpEvery));
    }

    static bool applied;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyArgs()
    {
        if (applied) return;
        applied = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-balVariant") continue;
            string v = a[i + 1];
            if (v == "base") { LateEnemyHp = false; AttackUpHighLevel = false; }
            else
            {
                LateEnemyHp = v.Contains("hp");
                AttackUpHighLevel = v.Contains("atk");
            }
            Debug.Log("[BalanceTuning] variant " + v + ": " + Describe);
        }
#endif
    }
}

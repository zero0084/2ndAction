using UnityEngine;

// 2026-11-28 マスター依頼⑥: ボスの BREAK の調整。
//  ① ドラゴン系(荒野の竜 "Dragon" / 天空の竜 "SkyDragon" / 地底竜 "Drake" / メカドラゴン "MechDragon")は BREAK に要る蓄積を 1.5 倍
//     (HP・攻撃力・BREAK の長さは変えない。崩しの調整表の値に掛ける。再戦の段階の倍率はその上に掛かる)。
//  ② 怒り(激昂 = HP の段階 2 以上。どのボスも撃破まで続く)の間は BREAK しない:
//     蓄積を止め、怒る前の量はそのまま残す(時間で減らさない)。怒りが解ければ再開(今のボスには解ける怒りは無い)。
//     通常のダメージはそのまま。BREAK 中に怒りへ移った時は、その BREAK(追撃を含む)を最後まで続ける(途中で切らない)。
//     同じ一撃で両方が成立する時は怒りが先(咆哮)= BREAK しない。ゲージには「怒り中は BREAK できない」印を出す(BossHudPanel)。
//  判定は HOST(シングルは自分)。参加者の画面のゲージは HOST の値(印は崩しのゲージの値に載せて送る: NetCombat.PackStagger)。
//  開発版の比較用: -dragonBreakMul <倍率>(1 = 以前)/ -rageBreakLock 0(以前 = 怒り中も BREAK する)
public static class BreakRules
{
    public static float DragonStaggerMul = 1.5f;
    public static bool RageLock = true;
    static bool argsRead;

    static void ReadArgs()
    {
        argsRead = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++)
        {
            if (a[i] == "-dragonBreakMul" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m)) { DragonStaggerMul = Mathf.Max(0.1f, m); Debug.Log($"[BossBattle] dragon BREAK multiplier = {m:F2} (dev arg)"); }
            if (a[i] == "-rageBreakLock") { RageLock = a[i + 1] != "0"; Debug.Log($"[BossBattle] rage BREAK lock = {RageLock} (dev arg)"); }
        }
#endif
    }

    public static bool IsDragonKey(string key) => key == "Dragon" || key == "SkyDragon" || key == "Drake" || key == "MechDragon";

    // ドラゴン系なら蓄積の上限を掛けた写しを返す(共有の調整表の値は書き換えない)
    public static BossBattleTuning.Entry ForBoss(string key, BossBattleTuning.Entry e)
    {
        if (!argsRead) ReadArgs();
        if (e == null || !IsDragonKey(key) || Mathf.Abs(DragonStaggerMul - 1f) < 0.001f) return e;
        var c = e.Clone();
        c.staggerMax *= DragonStaggerMul;
        return c;
    }

    // 怒り(段階 2 以上)で BREAK が止まっているか
    public static bool Locked(int phase)
    {
        if (!argsRead) ReadArgs();
        return RageLock && phase >= 2;
    }

    public static int LockedHits; // 確認用: 怒り中で蓄積しなかった一撃の数
}

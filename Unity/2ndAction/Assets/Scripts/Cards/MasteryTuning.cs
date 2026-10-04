using UnityEngine;

// Mastery の必要量(★0→★1 が need[0] … ★4→★5 が need[4])。Resources/Mastery/MasteryTuning.asset があればそれを使う。
// レア度で必要量は変えない(全カード共通)。
[CreateAssetMenu(menuName = "OneMoreMile/Mastery Tuning", fileName = "MasteryTuning")]
public class MasteryTuning : ScriptableObject
{
    [Tooltip("★0→★1, ★1→★2, ★2→★3, ★3→★4, ★4→★5 に必要な枚数")]
    public int[] need = { 1, 2, 3, 4, 5 };

    static MasteryTuning cached;
    static bool loaded;
    public static MasteryTuning I
    {
        get
        {
            if (!loaded || cached == null)
            {
                loaded = true;
                cached = Resources.Load<MasteryTuning>("Mastery/MasteryTuning");
                if (cached == null) { cached = CreateInstance<MasteryTuning>(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }
    public static int Need(int starsNow)
    {
        var n = I.need;
        if (n == null || n.Length == 0) return 1;
        return Mathf.Max(1, n[Mathf.Clamp(starsNow, 0, n.Length - 1)]);
    }
    public static int TotalToAwaken { get { int t = 0; for (int i = 0; i < CardMastery.MaxStars; i++) t += Need(i); return t; } }
}

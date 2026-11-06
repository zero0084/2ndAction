using UnityEngine;

// ラン中のレベルで最大HPが少し伸びる(2026-11-06、既定 5 レベルごとに+1・上限+6。マスターへ報告して判断を仰ぐ)。
//  初期デッキから HEART UP / SHIELD / VAMPIRE を外した後、初期デッキが 8〜14km で倒れた(最大HPが5のまま、10km の大ボス・1km ごとのボス)。
//  ボス撃破の回復(-bossHeal 1〜3)では足りなかったので、「走る・倒す・育つ」のうち「育つ」を最大HPにも少し回す案。
//  every レベルごとにハート+1(上限 maxHearts)。取った時にそのハートぶん回復。ランごと(シーンの読み直しで 0 から)。CONTINUE は保存した最大HPに含まれるので数え直さない
public partial class GameManager
{
    // 値は DifficultyTuning(Inspector)の levelHpEvery / levelHpMaxHearts。開発版 -levelHp <every>,<maxHearts> で上書き(0 = 無効)
    static int levelHpEveryOverride = -1, levelHpMaxOverride = -1;
    public static int LevelHpEvery => levelHpEveryOverride >= 0 ? levelHpEveryOverride : DifficultyTuning.I.levelHpEvery;
    public static int LevelHpMaxHearts => levelHpMaxOverride >= 0 ? levelHpMaxOverride : DifficultyTuning.I.levelHpMaxHearts;
    int levelHpApplied;
    public int LevelHpApplied => levelHpApplied; // 確認用

    void ApplyLevelHp()
    {
        if (LevelHpEvery <= 0 || TutorialMode.Active) return;
        int want = Mathf.Min(LevelHpMaxHearts, (Level - 1) / LevelHpEvery);
        while (levelHpApplied < want)
        {
            levelHpApplied++;
            if (maxLives >= maxLivesCap) continue;
            maxLives = Mathf.Min(maxLivesCap, maxLives + CombatScale.HpPerHeart);
            Lives = Mathf.Min(maxLives, Lives + CombatScale.HpPerHeart);
        }
    }

    void SyncLevelHpAfterResume()
    {
        levelHpApplied = LevelHpEvery > 0 ? Mathf.Min(LevelHpMaxHearts, (Level - 1) / LevelHpEvery) : 0;
    }

    // CONTINUE(BeginContinuedRun)の取得のやり直しの後: 今の Lv の分の最大HPを足し直す(上限 +LevelHpMaxHearts はそのまま)。
    //  2026-11-06 の修正: 以前は数えだけそろえていて、カードを持っている時は CONTINUE で最大HPのレベル分が消えていた。
    //  HP(Lives)はこの後に中断の時点の値へそろえるので、ここでは回復しない
    public static int LevelHpRestored; // 確認用(CONTINUE で足し直したハートの数)
    void RestoreLevelHpAfterResume(bool addHearts)
    {
        SyncLevelHpAfterResume();
        if (!addHearts || TutorialMode.Active) return;
        int add = 0;
        for (int i = 0; i < levelHpApplied; i++)
        {
            if (maxLives >= maxLivesCap) break;
            maxLives = Mathf.Min(maxLivesCap, maxLives + CombatScale.HpPerHeart);
            add++;
        }
        LevelHpRestored += add;
        if (add > 0) Debug.Log($"[Continue] level HP restored +{add} hearts (Lv{Level}, max {maxLives})");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LevelHpDevArgs()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-levelHp");
        if (i < 0 || i + 1 >= a.Length) return;
        var p = a[i + 1].Split(',');
        if (p.Length >= 1 && int.TryParse(p[0], out int e)) levelHpEveryOverride = e;
        if (p.Length >= 2 && int.TryParse(p[1], out int m)) levelHpMaxOverride = m;
    }
#endif
}

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

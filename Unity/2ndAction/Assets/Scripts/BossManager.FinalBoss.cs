using UnityEngine;

// 通常マップの専用ラスボス(2026-11-06、マスターの依頼。設計は Docs/FinalBosses_2026-11-06/design.md)。
//  荒野街道 = 荒野王 ガルムード(Wild/Garmud) / 自然洞窟 = 深淵の主 ノクタリス(Cave/Noctaris) / 天空回廊 = 天帝 セラフィエル(Sky/Seraphiel)。
//  ・出るのは 98km の「そのマップのラスボス」の枠だけ(BossManager.FinaleSet.cs の FinalBossKeyFor)。
//    通常の関門 / 再戦のプール / ラスダン / 3連戦の格のプール / 組の抽選には入れない(IsFinalOnlyKey で外す)。闘技場の一覧には出る(会った後)。
//  ・絵は Resources/BossAnim/<garmud|noctaris|seraphiel>/ の連番(idle_00 など)。置けば自動で使う(BossAnimSet)。
//    無い時は他のボスの絵で戦える(仮)。行動は Bosses/FinalBosses.cs
public partial class BossManager
{
    public static readonly string[] FinalBossKeys = { "Wild/Garmud", "Cave/Noctaris", "Sky/Seraphiel" };
    public static bool FinalBossOld; // 開発用の比較(-finalBossOld 1): 以前の仮のラスボス(黒騎士/古代地底悪魔/ガーディアン)

    public static bool IsFinalOnlyKey(string key) => key == "Wild/Garmud" || key == "Cave/Noctaris" || key == "Sky/Seraphiel";

    // ラスボスの待機の絵(Resources/BossAnim/<animKey>/idle_00)。無ければ仮の絵(他のボスの絵)
    static Sprite FinalBossIdle(string animKey, WildBossArt fallback)
    {
        var set = BossAnimSet.Load(animKey);
        if (set != null && set.idle != null && set.idle.Length > 0) return set.idle[0];
        Debug.LogWarning($"[FinalBoss] no art in Resources/BossAnim/{animKey}/ - using a placeholder body");
        return fallback != null ? fallback.idle : null;
    }
}

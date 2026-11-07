using System.Collections.Generic;
using UnityEngine;

// 常設チャレンジ(2026-11-07)。デイリー/ウィークリーは無く、一度達成すれば永久。報酬はバッヂ(チャレンジと1対1)と少量の MILE だけ(性能は上がらない)。
//  ・名前(name)とバッヂ名(badge)は英字で全言語そのまま(カード名と同じ扱い)。達成条件(condition)だけ訳す(ID 10820〜10843)。
//  ・測り方は ChallengeTracker。既存の記録から確かに分かる物(restorable)だけ、更新後の初回起動で達成済みにする(ChallengeStore.EnsureInitialized)。
//  ・バッヂの絵: Resources/Badges/badge_<id>.png を置けばそれを使う。無ければカテゴリ色の枠+手続きで描いたモチーフ(BadgeArt)。
public enum ChallengeCategory { Distance, NoDamage, Combo, Speed, Air, Break, Boss, Reaper, Special }

public class ChallengeDef
{
    public string id, name, badge, condition, motif;
    public ChallengeCategory cat;
    public int mile;
    public int tier;          // カテゴリの中の段(1〜3)。仮の絵の印の数
    public bool restorable;   // 既存の記録から復元する
    public double target;     // 進み具合の表示の目標(0 = 出さない)
    public ChallengeDef(string id, ChallengeCategory cat, int tier, string name, string badge, string condition, int mile, string motif, bool restorable = false, double target = 0)
    { this.id = id; this.cat = cat; this.tier = tier; this.name = name; this.badge = badge; this.condition = condition; this.mile = mile; this.motif = motif; this.restorable = restorable; this.target = target; }
}

public static class ChallengeDefs
{
    const ChallengeCategory D = ChallengeCategory.Distance, N = ChallengeCategory.NoDamage, C = ChallengeCategory.Combo, S = ChallengeCategory.Speed,
        A = ChallengeCategory.Air, B = ChallengeCategory.Break, K = ChallengeCategory.Boss, R = ChallengeCategory.Reaper, X = ChallengeCategory.Special;

    // 数値はここだけ(ChallengeTracker が読む)
    public const double Reach1 = 10000, Reach2 = 50000, Reach3 = 100000, LifetimeGoal = 1000000;
    public const double NoDamage1 = 10000, NoDamage2 = 30000;
    public const int Combo1 = 100, Combo2 = 500, Combo3 = 1000;
    public const float Speed1 = 130f, Speed2 = 150f, Speed3 = 170f;
    public const int AirKillsGoal = 100; public const float AirStaySeconds = 6f;
    public const int BreaksInRun = 5, BreaksTotal = 50;
    public const double StarterGoal = 50000, NoAutoGoal = 30000;

    // 並びは一覧の順(カテゴリごと)。ID は保存に使うので変えない
    public static readonly ChallengeDef[] All =
    {
        new ChallengeDef("dist_10k", D, 1, "REACH 10 KM", "TRAILBLAZER", "1回のランで10,000mに到達する", 30, "道標(1本の杭)と一本道", true, Reach1),               // 10820
        new ChallengeDef("dist_50k", D, 2, "REACH 50 KM", "LONG ROAD", "1回のランで50,000mに到達する", 60, "遠くへ続く一本道と道標2本", true, Reach2),            // 10821
        new ChallengeDef("dist_100k", D, 3, "REACH 100 KM", "HORIZON WALKER", "1回のランで100,000mに到達する", 100, "地平線へ消える一本道と星", true, Reach3),   // 10822
        new ChallengeDef("dist_total", D, 3, "1,000,000 M", "MILLION MILER", "累計距離を1,000,000mにする(脱出して確定した距離)", 100, "道標に刻まれた目盛り", true, LifetimeGoal), // 10823

        new ChallengeDef("nodmg_10k", N, 1, "UNTOUCHED 10 KM", "IRON GUARD", "ランの開始から一度も被弾せずに10,000m走る", 50, "盾", false, NoDamage1),           // 10824
        new ChallengeDef("nodmg_30k", N, 2, "UNTOUCHED 30 KM", "AEGIS", "ランの開始から一度も被弾せずに30,000m走る", 100, "縁取りのある大盾", false, NoDamage2),    // 10825
        new ChallengeDef("nodmg_boss", N, 3, "FLAWLESS BOSS", "FLAWLESS", "ボス戦の間に一度も被弾せずにボスを倒す", 60, "盾と小さな王冠"),                        // 10826

        new ChallengeDef("combo_100", C, 1, "100 HIT", "TWIN SLASH", "100 HITのコンボをつなぐ", 30, "2本の斬撃", false, Combo1),                                  // 10827
        new ChallengeDef("combo_500", C, 2, "500 HIT", "TRIPLE SLASH", "500 HITのコンボをつなぐ", 60, "3本の斬撃", false, Combo2),                                // 10828
        new ChallengeDef("combo_1000", C, 3, "1000 HIT", "ENDLESS BLADE", "1000 HITのコンボをつなぐ", 100, "交差する多数の斬撃", false, Combo3),                    // 10829

        new ChallengeDef("speed_130", S, 1, "130 KM/H", "SWIFT WING", "走る速さが130km/hに達する", 30, "片翼", false, Speed1),                                     // 10830
        new ChallengeDef("speed_150", S, 2, "150 KM/H", "GALE WING", "走る速さが150km/hに達する", 60, "翼と速度線", false, Speed2),                                 // 10831
        new ChallengeDef("speed_170", S, 3, "170 KM/H", "SONIC WING", "走る速さが170km/hに達する", 100, "両翼と多くの速度線", false, Speed3),                        // 10832

        new ChallengeDef("air_kill", A, 1, "AERIAL KILL", "FIRST FLIGHT", "空中にいる間に敵を倒す", 20, "羽1枚"),                                                      // 10833
        new ChallengeDef("air_kill_100", A, 2, "100 AERIAL KILLS", "SKY HUNTER", "空中にいる間に敵を累計100体倒す", 60, "羽2枚", false, AirKillsGoal),               // 10834
        new ChallengeDef("air_stay", A, 3, "6 SEC AIRBORNE", "CLOUD DANCER", "着地せずに6秒間空中にいる", 40, "輪になった羽3枚"),                                    // 10835

        new ChallengeDef("break_run5", B, 1, "5 BREAKS", "CHAIN BREAKER", "1回のランでボスを5回BREAKさせる", 50, "砕けた鎖", false, BreaksInRun),                   // 10836
        new ChallengeDef("break_total50", B, 2, "50 BREAKS TOTAL", "SHACKLE CRUSHER", "ボスを累計50回BREAKさせる", 80, "砕けた鎖2本", false, BreaksTotal),          // 10837

        new ChallengeDef("boss_garmud", K, 1, "WASTELAND KING", "BEAST CROWN", "荒野王ガルムードを倒す", 100, "王冠と牙"),                                          // 10838
        new ChallengeDef("boss_noctaris", K, 2, "ABYSS SOVEREIGN", "ABYSS CROWN", "深淵の主ノクタリスを倒す", 100, "王冠と結晶"),                                     // 10839
        new ChallengeDef("boss_seraphiel", K, 3, "SKY EMPEROR", "HALO CROWN", "天帝セラフィエルを倒す", 100, "王冠と光輪"),                                          // 10840
        new ChallengeDef("boss_rush", K, 3, "FINAL TRIAD", "TRIAD CROWN", "通常マップ終盤の95〜97kmで3体同時のボス戦を抜けて、ラスボスの前にたどり着く", 80, "3つ並んだ王冠"),   // 10841

        new ChallengeDef("reaper_meet", R, 1, "REAPER SIGHTED", "REAPER'S MARK", "死神と出会う", 30, "鎌", true),                                                   // 10842
        new ChallengeDef("reaper_escape", R, 2, "REAPER ESCAPE", "SCYTHE EVADER", "死神が現れた後に脱出に成功する", 100, "鎌を振り切る軌跡"),                         // 10843

        new ChallengeDef("sp_starter", X, 1, "STARTER'S ROAD", "ORIGIN STAR", "初期デッキのカードだけで、1回のランで50,000m走る", 80, "星", false, StarterGoal), // 10844
        new ChallengeDef("sp_noauto", X, 2, "ON MY OWN FEET", "CREST OF RESOLVE", "オートを一度もONにせずに、1回のランで30,000m走る", 80, "星と紋章", false, NoAutoGoal), // 10845
    };

    static Dictionary<string, ChallengeDef> byId;
    public static ChallengeDef Find(string id)
    {
        if (byId == null) { byId = new Dictionary<string, ChallengeDef>(); foreach (var c in All) byId[c.id] = c; }
        return id != null && byId.TryGetValue(id, out var d) ? d : null;
    }

    public static string CategoryName(ChallengeCategory c)
    {
        switch (c)
        {
            case ChallengeCategory.Distance: return "DISTANCE";
            case ChallengeCategory.NoDamage: return "NO DAMAGE";
            case ChallengeCategory.Combo: return "COMBO";
            case ChallengeCategory.Speed: return "SPEED";
            case ChallengeCategory.Air: return "AIR";
            case ChallengeCategory.Break: return "BREAK";
            case ChallengeCategory.Boss: return "BOSS";
            case ChallengeCategory.Reaper: return "REAPER";
            default: return "SPECIAL";
        }
    }

    // カテゴリの色(バッヂの枠/一覧の見出し)。小さくても見分けられるよう色相を離す
    public static Color CategoryColor(ChallengeCategory c)
    {
        switch (c)
        {
            case ChallengeCategory.Distance: return new Color(0.86f, 0.62f, 0.28f);  // 琥珀(道)
            case ChallengeCategory.NoDamage: return new Color(0.38f, 0.62f, 0.95f);  // 鋼の青(盾)
            case ChallengeCategory.Combo: return new Color(0.92f, 0.30f, 0.32f);     // 紅(斬撃)
            case ChallengeCategory.Speed: return new Color(0.25f, 0.86f, 0.90f);     // 水色(翼)
            case ChallengeCategory.Air: return new Color(0.55f, 0.90f, 0.55f);       // 若葉(羽)
            case ChallengeCategory.Break: return new Color(1.00f, 0.55f, 0.15f);     // 橙(鎖)
            case ChallengeCategory.Boss: return new Color(1.00f, 0.84f, 0.30f);      // 金(王冠)
            case ChallengeCategory.Reaper: return new Color(0.66f, 0.42f, 0.92f);    // 紫(鎌)
            default: return new Color(0.96f, 0.52f, 0.80f);                          // 桃(星/紋章)
        }
    }
}

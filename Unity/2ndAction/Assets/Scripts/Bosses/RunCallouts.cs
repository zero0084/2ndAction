using UnityEngine;

// ラン中の予告(2026-11-26 依頼④): 名前だけ(「〇〇が迫る!」)ではボスか雑魚の大群か分からなかった →
//  何が来るか(種類)が先に分かる短い言葉にする。固有名は残す(後ろに付ける)。単体/同時(ボス群)/連戦を区別する。
//  ・文は日本語が元の文(訳のキー)。BossBattleHud.Banner は描く時に訳す(マルチは HOST の元の文が届き、各自の言語へ)。
//  ・訳が入るまでは、その言語では前の文(レビュー済み)を出す(Loc.Ready。日本語は常に新しい文)。訳を入れたら前の文は使われなくなる
public static class RunCallouts
{
    public const string BossApproach = "ボス接近：{0}";                  // ID 11099 {0}=ボスの名前(英字)。単体のボス
    public const string BossGroup = "ボス群接近！ {0}体同時";              // ID 11100 {0}=体数。同時に複数のボス
    public const string BossGroupStart = "ボス群接近！ 走りながら戦え！";   // ID 11101 95km の3体同時の組の始まり
    public const string BossGroupNext = "次のボス群：{0}km";               // ID 11102 {0}=km。次の組の予約
    public const string BossSeriesStart = "ボス連戦！ 走りながら戦え！";    // ID 11103 ラスダン 90km〜の走りながらの連戦の始まり
    public const string BossSeriesNext = "ボス連戦：{0}";                 // ID 11104 {0}=ボスの名前。連戦の次のボス
    public const string FinalBoss = "最終ボス接近：{0}";                   // ID 11105 {0}=ボスの名前。98km のマップの最後のボス
    public const string Horde = "敵の大群が接近！";                       // ID 11106 雑魚の大群
    public const string HordePeak = "敵の大群：最大規模！";                // ID 11107 90〜95km の大群の一番多い波
    public const string AirSwarm = "空から敵の群れが接近！";               // ID 11108 飛ぶ敵の群れ
    public const string EliteSwarm = "強敵を含む群れが接近！";             // ID 11109 HP の多い敵が混ざる群れ
    public const string FastSwarm = "素早い敵の群れが接近！";              // ID 11110 速い敵の群れ
    public const string SeriesTrioStart = "ボス3連戦！";                  // ID 11111 95〜99km の1体ずつ3連戦(既定では使わない経路)
    public const string SeriesTrioNext = "ボス連戦：{0}({1}/3)";          // ID 11112 {0}=名前 {1}=何体目
    public const string SeriesClear = "ボス連戦を突破！";                  // ID 11113

    public static bool GateReady => Loc.Ready(BossApproach) && Loc.Ready(BossGroup);
    static string Pick(string key, string old) => Loc.Ready(key) ? key : old;
    static string PickF(string key, string old, params object[] a) => string.Format(Loc.Ready(key) ? key : old, a);

    // "Wild/GoblinRider" → "GOBLIN RIDER"(画面用。ログの名前は今まで通り)
    public static string Name(string bossKey)
    {
        if (string.IsNullOrEmpty(bossKey)) return "?";
        string n = bossKey.Substring(bossKey.IndexOf('/') + 1);
        var sb = new System.Text.StringBuilder(n.Length + 4);
        for (int i = 0; i < n.Length; i++) { if (i > 0 && char.IsUpper(n[i]) && !char.IsUpper(n[i - 1])) sb.Append(' '); sb.Append(char.ToUpperInvariant(n[i])); }
        return sb.ToString();
    }

    public static string Boss(string bossKey, string oldTemplate = "WARNING  {0} が迫る!") => PickF(BossApproach, oldTemplate, Name(bossKey));
    public static string BossNamed(string displayName, string oldText) => Loc.Ready(BossApproach) ? string.Format(BossApproach, displayName) : oldText;
    public static string Group(int count) => Loc.Ready(BossGroup) ? string.Format(BossGroup, count) : "BOSS RUSH  ×" + count;
    public static string GroupStart() => Pick(BossGroupStart, "BOSS RUSH  ×3  走りながら戦え!");
    public static string GroupNext(int km) => PickF(BossGroupNext, "BOSS SET  {0}km", km);
    public static string SeriesStart() => Pick(BossSeriesStart, "BOSS RUSH  走りながら戦え!");
    public static string SeriesNext(string bossKey) => PickF(BossSeriesNext, "WARNING  {0} が迫る!", Name(bossKey));
    public static string Final(string bossKey) => PickF(FinalBoss, "FINAL BOSS  {0}", Name(bossKey));
    public static string HordeStart() => Pick(Horde, "FINAL RUSH  >> 95km");
    public static string HordeMax() => Pick(HordePeak, "FINAL RUSH  MAX");
    public static string TrioStart(int km) => Loc.Ready(SeriesTrioStart) ? SeriesTrioStart : $"BOSS RUSH  {km}km  ×3";
    public static string TrioNext(string bossKey, int i) => PickF(SeriesTrioNext, "NEXT  {0}  ({1}/3)", Name(bossKey), i);
    public static string TrioClear(int km) => Loc.Ready(SeriesClear) ? SeriesClear : $"BOSS RUSH  {km}km  CLEAR!";

    // 道中イベント(RoadEvents): 種類が先。前の文は「WARNING  {名前} が迫る!」
    public static string RoadEvent(RoadEventKind kind, string oldName)
    {
        string old = $"WARNING  {oldName} が迫る!";
        switch (kind)
        {
            case RoadEventKind.Rush: return Pick(Horde, old);
            case RoadEventKind.Flying: return Pick(AirSwarm, old);
            case RoadEventKind.Elite: return Pick(EliteSwarm, old);
            case RoadEventKind.Chase: return Pick(FastSwarm, old);
            default: return old;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

// ガチャの抽選の対象 = 最後に出発したマップ(2026-11-14 夜、マスターの訂正。同じ日に入れた「分類を選ぶ欄」は外した)。
//  ・筐体は1台のまま。最後に実際に出発したマップ(荒野/洞窟/天空/ラスダン)の見た目と、そのマップの分類のカードを抽選の対象にする。
//  ・マップ選択の画面で選んだだけでは変えない。出発した時点(ApplyGameStart)で記録する。成功/敗北のどちらでも、その記録のまま。
//    チュートリアル/闘技場/DEBUG RUN は出発に数えない。マルチ(CO-OP/VERSUS)は数える。
//  ・記録は保存する(設定の扱い)。ホームへの帰還・再起動の後も同じマップのガチャ。まだ一度も出発していない時は荒野(最初のマップ)。
//  ・抽選の対象はタップの時点で確定する(通常: GachaPullOnce / 広告: OnAdGachaTapped で受け取って広告の後もその分類)。
//  ・対象のカードが無い時は引けない(MILE も広告の回数も使わない)。
//  ・共通カード(2026-11-15 マスター決定、案1): 1回ごとに「共通 30% / 最後に出発したマップ 70%」で分類を決め、その分類のレア度の確率で1枚。
//    通常のガチャも広告ガチャも同じ。マップ側に対象のカードが無い時は、共通だけに切り替えず引けない(MILE/広告の回数を使わない)。
//    割合は GachaCommonShare の1か所。提供割合の画面(GachaOddsPanel)・資料・試験は抽選と同じ関数(GachaShareOdds/GachaFinalOdds)を読む。
//  ・レア度の確率は GachaRarityOdds(下): 無いレア度の分は対象の一番低いレア度へ回す(2026-11-14 マスター決定。★4/★5 は重みどおり)。
public partial class GameManager
{
    public const string GachaLastMapKey = "GachaLastMapV1"; // 最後に出発したマップのステージ ID
    CardMapCategory gachaCategory = CardMapCategory.Wilderness;
    bool gachaCategoryLoaded;
    string gachaCategoryNote = ""; float gachaCategoryNoteUntil;
    public static int GachaCategoryRefusedEmpty; // 確認用: 対象のカードが無くて断った回数

    public CardMapCategory GachaCategory { get { EnsureGachaCategory(); return gachaCategory; } }
    public static string GachaLastMapStageId => SaveStore.GetString(GachaLastMapKey, "");

    // ステージ ID → カードの分類(マップでないステージは null)
    public static CardMapCategory? GachaCategoryOfStage(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return null;
        for (int i = 1; i <= 4; i++) if (CardMapCategories.StageIdOf((CardMapCategory)i) == stageId) return (CardMapCategory)i;
        return null;
    }

    void EnsureGachaCategory()
    {
        if (gachaCategoryLoaded) return;
        gachaCategoryLoaded = true;
        gachaCategory = GachaCategoryOfStage(GachaLastMapStageId) ?? CardMapCategory.Wilderness;
        gachaSkinFx.SetSkin(gachaCategory, true);
    }

    // 出発した時(ApplyGameStart)。そのマップを記録して、ガチャの見た目と対象を合わせる
    void NoteGachaDeparture(string stageId)
    {
        if (TutorialMode.Active || ArenaMode.Active || DebugRun.WritesBlocked) return;
        var k = GachaCategoryOfStage(stageId);
        if (k == null) return;
        if (SaveStore.GetString(GachaLastMapKey, "") != stageId) { SaveStore.SetString(GachaLastMapKey, stageId); SaveStore.Save(); }
        gachaCategoryLoaded = true;
        if (gachaCategory != k.Value) { gachaCategory = k.Value; gachaSkinFx.SetSkin(gachaCategory, true); }
        Debug.Log($"[Gacha] departed {stageId} -> gacha draws {gachaCategory}");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(自動試験): 出発したことにする
    public void DevNoteGachaDeparture(string stageId) => NoteGachaDeparture(stageId);
#endif

    // 選んだ分類の抽選の対象(今までの解禁条件 + 分類)。共通は共通の分類のカードだけ
    List<CardDefinition> BuildGachaPoolFor(CardMapCategory k)
    {
        var pool = new List<CardDefinition>();
        foreach (var c in BuildGachaPool()) if (c != null && CardMapCategories.Of(c) == k) pool.Add(c);
        return pool;
    }
    public int GachaPoolCount(CardMapCategory k) => BuildGachaPoolFor(k).Count;

    // ---------------------------------------------------------------- 共通 30% / マップ 70%(2026-11-15)
    public const float GachaCommonShare = 0.30f;
    public static int LastDrawCommonSide; // 確認用: 直前の抽選が共通の枠だったか(1)/マップ(0)

    // [0] = 共通の割合、[1] = マップの割合(マップに対象が無い時は引けないので両方 0)。共通に対象が無い時(起きない)はマップ 100%
    public static float[] GachaShareOdds(int mapCount, int commonCount)
    {
        if (mapCount <= 0) return new float[2];
        if (commonCount <= 0) return new[] { 0f, 1f };
        return new[] { GachaCommonShare, 1f - GachaCommonShare };
    }

    // 1回あたりの ★1..★5 の確率(分類の割合 × その分類のレア度の確率)。commonPart/mapPart はそれぞれの分類の分([0..4])
    public static float[] GachaFinalOdds(IList<CardDefinition> mapPool, IList<CardDefinition> commonPool, float[] weights, out float[] commonPart, out float[] mapPart)
    {
        var sh = GachaShareOdds(mapPool != null ? mapPool.Count : 0, commonPool != null ? commonPool.Count : 0);
        var co = GachaRarityOdds(commonPool, weights); var mo = GachaRarityOdds(mapPool, weights);
        commonPart = new float[5]; mapPart = new float[5]; var total = new float[5];
        for (int i = 0; i < 5; i++) { commonPart[i] = sh[0] * co[i]; mapPart[i] = sh[1] * mo[i]; total[i] = commonPart[i] + mapPart[i]; }
        return total;
    }

    // 2段の抽選の本体(乱数を外から渡せる = 試験で等間隔に並べられる)。マップに対象が無ければ null(引かない)
    public static CardDefinition DrawTwoStage(List<CardDefinition> mapPool, List<CardDefinition> commonPool, float[] weights, float sideRoll, float rarityRoll, float cardRoll, out bool commonSide)
    {
        commonSide = false;
        if (mapPool == null || mapPool.Count == 0) return null;
        var sh = GachaShareOdds(mapPool.Count, commonPool != null ? commonPool.Count : 0);
        commonSide = sideRoll < sh[0];
        var pool = commonSide ? commonPool : mapPool;
        int r = PickGachaRarity(GachaRarityOdds(pool, weights), rarityRoll);
        var bucket = new List<CardDefinition>();
        foreach (var c in pool) if (c != null && GachaRarityOf(c) == r) bucket.Add(c);
        if (bucket.Count == 0) bucket = pool;
        return bucket[Mathf.Clamp((int)(cardRoll * bucket.Count), 0, bucket.Count - 1)];
    }

    // ガチャ1回(通常/広告/パス共通)。map = タップの時点で確定した、最後に出発したマップの分類
    CardDefinition DrawGachaForMap(CardMapCategory map)
    {
        var mapPool = BuildGachaPoolFor(map);
        if (mapPool.Count == 0) return null;
        var commonPool = BuildGachaPoolFor(CardMapCategory.Common);
        if (Debug.isDebugBuild && !string.IsNullOrEmpty(DevForceGachaCard))
        {
            var f = mapPool.Find(c => c != null && c.cardId == DevForceGachaCard) ?? commonPool.Find(c => c != null && c.cardId == DevForceGachaCard);
            if (f != null) return f;
        }
        var c2 = DrawTwoStage(mapPool, commonPool, gachaRarityWeights, Random.value, Random.value, Random.value, out bool common);
        LastDrawCommonSide = common ? 1 : 0;
        return c2;
    }

    // 今の条件の表(提供割合の画面・確認用)
    public float[] GachaOddsNow(out float[] commonPart, out float[] mapPart, out int mapCount, out int commonCount)
    {
        var mp = BuildGachaPoolFor(GachaCategory); var cp = BuildGachaPoolFor(CardMapCategory.Common);
        mapCount = mp.Count; commonCount = cp.Count;
        return GachaFinalOdds(mp, cp, gachaRarityWeights, out commonPart, out mapPart);
    }

    // マップに対象が無い時の条件(分かる時だけ)。「自然洞窟で1,000m到達して帰還」/「ラストダンジョンの解放」
    //  返すのは並べ替えの前の訳(Loc.F(LocGacha.Condition, …) に差し込んで表示する)
    public string GachaMapUnlockHint(CardMapCategory map)
    {
        if (map == CardMapCategory.LastDungeon) return ProgressStats.FinalDungeonUnlocked ? "" : Loc.Plain(LocCardCommon.UnlockLastDungeon);
        string stage = CardMapCategories.StageIdOf(map);
        if (string.IsNullOrEmpty(stage)) return "";
        float best = BestDistance; int st = CurrentGachaStage; float need = float.MaxValue;
        foreach (var c in CardDatabase.AllCards)
        {
            if (c == null || CardMapCategories.Of(c) != map || !GachaStage.IsCardEligibleByDistance(c, best, st)) continue;
            need = Mathf.Min(need, CardMapUnlock.RequiredMeters(c));
        }
        if (need == float.MaxValue || need <= 0f) return "";
        return Loc.AutoPlain(Loc.Src(LocCardCommon.UnlockOnMap, UnlockRules.StageName(stage), need.ToString("N0", System.Globalization.CultureInfo.InvariantCulture))); // カードの解禁の条件と同じ組み立て(ConditionSrc)
    }

    void RefuseEmptyCategory(CardMapCategory k)
    {
        GachaCategoryRefusedEmpty++;
        string hint = GachaMapUnlockHint(k);
        SetGachaCategoryNote(string.IsNullOrEmpty(hint) ? Loc.T(LocGacha.NoCardsMap) : Loc.F(LocGacha.Condition, hint));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
        Debug.Log($"[Gacha] refused: no cards in {k} (nothing spent)");
    }

    void SetGachaCategoryNote(string s) { gachaCategoryNote = s; gachaCategoryNoteUntil = Time.unscaledTime + 2.5f; }

    // 案内の1行: 「天空のカード 70%・共通 30%」/ 対象が無い時は「このマップで引けるカードがまだありません」
    // 2026-11-15 午前からホームには出さない(確認用の記録だけ)。断った理由が出ている間はそれ、普段は対象の案内
    string GachaSignSubLine()
    {
        EnsureGachaCategory();
        if (!string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil) return gachaCategoryNote;
        return GachaTargetLine(gachaCategory, GachaLandCount(gachaCategory) == 0);
    }
    string GachaTargetLine(CardMapCategory k, bool empty)
    {
        if (empty) return Loc.T(LocGacha.NoCardsMap);
        int cs = Mathf.RoundToInt(GachaCommonShare * 100f);
        return Loc.F(LocGacha.MixLine, Loc.AutoPlain(CardMapCategories.LabelSrc(k)), 100 - cs, cs); // 差し込む名前は並べ替えの前(Loc.AutoPlain)
    }

    // ---------------------------------------------------------------- レア度の確率(2026-11-14 マスター決定)
    //  ・対象(選んだ分類 × ガチャLv × 解禁)にあるレア度は、重み(50/30/15/4/1、合計100)どおりの確率。
    //  ・対象に無いレア度の分は、すべて「対象にある一番低いレア度」へ回す(★4/★5 は対象にある限り 4% / 1% のまま)。
    //  ・一番低いレア度が ★4 / ★5 になる対象では、そのレア度は重みどおりにならない(GachaOddsIssue が理由を返す。今のカードの表では起きない)。
    //  抽選(DrawFromGachaPool)・資料・試験はすべてこの表を読む(別の計算を持たない)
    public static int GachaRarityOf(CardDefinition c) => c == null ? 1 : Mathf.Clamp(c.rarity, 1, 5);

    // [0..4] = ★1..★5 の確率(合計 1)。対象が空なら全部 0
    public static float[] GachaRarityOdds(IList<CardDefinition> pool, float[] weights)
    {
        var odds = new float[5];
        var present = new bool[5];
        int n = 0;
        if (pool != null) foreach (var c in pool) if (c != null) { present[GachaRarityOf(c) - 1] = true; n++; }
        if (n == 0) return odds;
        int lowest = 0; while (!present[lowest]) lowest++;
        float total = 0f;
        for (int i = 0; i < 5; i++) total += Mathf.Max(0f, i < weights.Length ? weights[i] : 0f);
        if (total <= 0f) { int k = 0; for (int i = 0; i < 5; i++) if (present[i]) k++; for (int i = 0; i < 5; i++) odds[i] = present[i] ? 1f / k : 0f; return odds; }
        float others = 0f;
        for (int i = lowest + 1; i < 5; i++) if (present[i]) { odds[i] = Mathf.Max(0f, weights[i]) / total; others += odds[i]; }
        odds[lowest] = Mathf.Max(0f, 1f - others);
        return odds;
    }

    // 表どおりにならない条件の説明(資料・試験・ログ用。無ければ "")
    public static string GachaOddsIssue(IList<CardDefinition> pool, float[] weights)
    {
        var odds = GachaRarityOdds(pool, weights);
        float total = 0f; foreach (var w in weights) total += Mathf.Max(0f, w);
        int lowest = -1; for (int i = 0; i < 5; i++) if (odds[i] > 0f) { lowest = i; break; }
        if (lowest < 0) return "";
        if (lowest >= 3 && total > 0f && Mathf.Abs(odds[lowest] - weights[lowest] / total) > 1e-6f)
            return $"★{lowest + 1} が対象の一番低いレア度のため、無いレア度の分を受けて {odds[lowest] * 100f:0.##}%(重みどおりなら {weights[lowest] / total * 100f:0.##}%)";
        return "";
    }

    // 表と 0..1 の乱数からレア度(1..5)を選ぶ(確率 0 のレア度は選ばない)
    public static int PickGachaRarity(float[] odds, float roll)
    {
        float acc = 0f; int last = 0;
        for (int i = 0; i < 5; i++)
        {
            if (odds[i] <= 0f) continue;
            last = i + 1; acc += odds[i];
            if (roll < acc) return i + 1;
        }
        return last > 0 ? last : 1; // 誤差で 1 を超えなかった時は一番上の対象のレア度
    }

    // 選んでいる分類の今の確率(確認用・資料用)
    public float[] GachaOddsFor(CardMapCategory k) => GachaRarityOdds(BuildGachaPoolFor(k), gachaRarityWeights);
}

// ガチャの分類の文言(2026-11-14)。訳は Tools/loc の keys_extra42(エステルの確認待ちの間は英語の表/日本語)
public static class LocGacha
{
    // 文言はエステルと相談して決めた物(2026-11-14)
    public const string WillDraw = "{0}のカードだけが出ます";          // ID 11000
    public const string NoCards = "この分類には引けるカードがありません"; // ID 11001
    public const string Locked = "{0}はまだ解禁されていません";            // ID 11002({0}=押した未解禁の分類名。エステル: 選んでいる分類と取り違えない)
    public const string Pick = "ガチャの分類";                          // ID 11003(縦画面の帯の見出し。2026-11-14 夜から未使用)
    // 2026-11-15(共通 30% / マップ 70%、提供割合)
    public const string MixLine = "{0}のカード {1}%・共通 {2}%";         // ID 11040 {0}=マップの分類名 {1}=70 {2}=30
    public const string NoCardsMap = "このマップで引けるカードがまだありません"; // ID 11041
    public const string Condition = "条件：{0}";                        // ID 11042 {0}=解放の条件(「自然洞窟で1,000m到達して帰還」等)
    public const string Odds = "提供割合";                                // ID 11043(ボタンと画面の見出し)
    public const string OddsHow = "1回ごとに、まず「共通」か「マップ」を抽選し、その分類のレア度別の割合に従ってレア度を決めます。選ばれた分類・レア度の対象カードから、同じ確率で1枚を選びます。通常ガチャと広告ガチャの提供割合は同じです。"; // ID 11044
    public const string OddsMapIs = "対象マップ：{0}。実際に出発すると、そのマップに切り替わります。"; // ID 11045
    public const string OddsShare = "分類";                              // ID 11046(表の見出し: 分類とその割合)
    public const string OddsTotal = "合計";                              // ID 11047
    public const string OddsCards = "対象：{0}枚";                         // ID 11048
    public const string OddsPerPull = "分類の割合を反映した、1回あたりの提供割合です（小数第2位まで表示）。"; // ID 11049
    public static readonly string[] All = { WillDraw, NoCards, Locked, Pick, MixLine, NoCardsMap, Condition, Odds, OddsHow, OddsMapIs, OddsShare, OddsTotal, OddsCards, OddsPerPull };
}

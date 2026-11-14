using System.Collections.Generic;
using UnityEngine;

// ガチャの抽選の対象 = 最後に出発したマップ(2026-11-14 夜、マスターの訂正。同じ日に入れた「分類を選ぶ欄」は外した)。
//  ・筐体は1台のまま。最後に実際に出発したマップ(荒野/洞窟/天空/ラスダン)の見た目と、そのマップの分類のカードを抽選の対象にする。
//  ・マップ選択の画面で選んだだけでは変えない。出発した時点(ApplyGameStart)で記録する。成功/敗北のどちらでも、その記録のまま。
//    チュートリアル/闘技場/DEBUG RUN は出発に数えない。マルチ(CO-OP/VERSUS)は数える。
//  ・記録は保存する(設定の扱い)。ホームへの帰還・再起動の後も同じマップのガチャ。まだ一度も出発していない時は荒野(最初のマップ)。
//  ・抽選の対象はタップの時点で確定する(通常: GachaPullOnce / 広告: OnAdGachaTapped で受け取って広告の後もその分類)。
//  ・対象のカードが無い時は引けない(MILE も広告の回数も使わない)。
//  ・共通カードをこの仕組みでどう入手するかは未決定(マスター確認待ち)。それまでの間、共通カードはガチャの対象に入らない。
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

    void RefuseEmptyCategory(CardMapCategory k)
    {
        GachaCategoryRefusedEmpty++;
        SetGachaCategoryNote(Loc.T(LocGacha.NoCards));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
        Debug.Log($"[Gacha] refused: no cards in {k} (nothing spent)");
    }

    void SetGachaCategoryNote(string s) { gachaCategoryNote = s; gachaCategoryNoteUntil = Time.unscaledTime + 2.5f; }

    // 案内の1行: 「天空のカードだけが出ます」/ 対象が無い時は「この分類には引けるカードがありません」
    // 横画面のガチャの札の2行目 / 縦画面の台の下: 断った理由が出ている間はそれ、普段は対象の案内
    string GachaSignSubLine()
    {
        EnsureGachaCategory();
        if (!string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil) return gachaCategoryNote;
        return GachaTargetLine(gachaCategory, GachaLandCount(gachaCategory) == 0);
    }
    string GachaTargetLine(CardMapCategory k, bool empty) =>
        empty ? Loc.T(LocGacha.NoCards) : Loc.F(LocGacha.WillDraw, Loc.T(CardMapCategories.LabelSrc(k)));

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
    public const string Pick = "ガチャの分類";                          // ID 11003(縦画面の帯の見出し)
    public static readonly string[] All = { WillDraw, NoCards, Locked, Pick };
}

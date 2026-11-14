using System.Collections.Generic;
using UnityEngine;

// ガチャの分類を選ぶ機能(2026-11-14、マスターの仕様変更。混入の不具合の修正ではなく機能の追加)。
//  ・筐体は1台のまま。引く前に「共通/荒野/洞窟/天空/ラスダン」の分類を選び、選んだ分類に所属するカードだけを抽選する。
//    マップの分類に共通カードは混ぜない(共通は共通を選んだ時だけ)。
//  ・未解禁の分類(マップが未解放 / ラスダン未解放)は選べない。ガチャLv・カードごとの解禁条件(GachaStage.IsCardEligible)はそのまま。
//  ・抽選する分類はタップの時点で確定する(通常: GachaPullOnce / 広告: OnAdGachaTapped で受け取って広告の後もその分類)。
//    広告の待機中・結果の演出中に分類を変えても、すでに確定した抽選は変わらない。
//  ・対象のカードが無い分類では引けない(MILE も広告の回数も使わない)。別の分類へ自動で切り替えない。
//  ・レア度の重み(gachaRarityWeights)は今までどおり「その中にあるレア度の間で」配る。
//  ・筐体のスキン = 選んでいる分類(直前に引いたカードではない)。
//  ・選んだ分類は保存する(設定の扱い。読み込んだ時に未解禁なら共通)。
public partial class GameManager
{
    public const string GachaCategoryKey = "GachaCategoryV1";
    CardMapCategory gachaCategory = CardMapCategory.Common;
    bool gachaCategoryLoaded;
    string gachaCategoryNote = ""; float gachaCategoryNoteUntil;
    public static int GachaCategoryRefusedEmpty; // 確認用: 対象のカードが無くて断った回数

    public CardMapCategory GachaCategory { get { EnsureGachaCategory(); return gachaCategory; } }

    void EnsureGachaCategory()
    {
        if (gachaCategoryLoaded) return;
        gachaCategoryLoaded = true;
        int v = SaveStore.GetInt(GachaCategoryKey, (int)CardMapCategory.Common);
        var k = (CardMapCategory)Mathf.Clamp(v, 0, 4);
        if (!GachaLandMapOpen(k)) k = CardMapCategory.Common;
        gachaCategory = k;
        gachaSkinFx.SetSkin(k, true);
    }

    public static bool GachaCategorySelectable(CardMapCategory k) => GachaLandMapOpen(k);

    // 分類を選ぶ(台の下の列のタップ)。未解禁は選べない。選んだ分類を保存して、筐体の見た目を合わせる
    public bool SelectGachaCategory(CardMapCategory k)
    {
        EnsureGachaCategory();
        if (!GachaCategorySelectable(k))
        {
            SetGachaCategoryNote(Loc.F(LocGacha.Locked, Loc.T(CardMapCategories.LabelSrc(k))));
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
            return false;
        }
        if (k == gachaCategory) return true;
        gachaCategory = k;
        SaveStore.SetInt(GachaCategoryKey, (int)k);
        SaveStore.Save();
        gachaSkinFx.SetSkin(k, false);
        gachaCategoryNoteUntil = 0f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        Debug.Log($"[Gacha] category -> {k} ({BuildGachaPoolFor(k).Count} cards)");
        return true;
    }

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

    // 案内の1行: 「天空のカードが出ます」/ 対象が無い時は「このガチャで引けるカードがまだありません」
    // 横画面のガチャの札の2行目: 断った理由が出ている間はそれ、普段は対象の案内
    string GachaSignSubLine()
    {
        EnsureGachaCategory();
        if (!string.IsNullOrEmpty(gachaCategoryNote) && Time.unscaledTime < gachaCategoryNoteUntil) return gachaCategoryNote;
        return GachaTargetLine(gachaCategory, GachaLandCount(gachaCategory) == 0);
    }
    string GachaTargetLine(CardMapCategory k, bool empty) =>
        empty ? Loc.T(LocGacha.NoCards) : Loc.F(LocGacha.WillDraw, Loc.T(CardMapCategories.LabelSrc(k)));
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

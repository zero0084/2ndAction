using System.Collections.Generic;

// カードの説明文の「前の文」(2026-10-30 属性20枚の再設計)。説明文は日本語の文そのものが訳のキーなので、説明を書き換えると
// 訳が届くまで他の言語では日本語が出てしまう。訳が入っていない言語では、前の(レビュー済みの)説明を出す(CardDefinition.DisplayDescription)。
// 前の説明の数値は古いが、効果の行(Lv1/Lv9 の値)はゲームの計算から作るので正しい。製品版の Android ビルドは、ここに載っているカードの
// 新しい説明の訳が全言語に入るまで止まる(Editor/ReleaseBuildGuard)。訳を反映したら、ここから外す。
public static class CardDescHistory
{
    static readonly Dictionary<string, string> previous = new Dictionary<string, string>
    {
        // 2026-10-30: 属性19枚(flame_blade〜blood_blade)の新しい説明は、エステルのレビュー後に30言語へ反映済み(Tools/loc/keys_extra33、ID 10204〜10222)→ 外した
    };
    public static bool Has(string cardId) => cardId != null && previous.ContainsKey(cardId);
    public static string Previous(string cardId) => cardId != null && previous.TryGetValue(cardId, out var d) ? d : null;
    public static IEnumerable<string> Ids => previous.Keys;
}

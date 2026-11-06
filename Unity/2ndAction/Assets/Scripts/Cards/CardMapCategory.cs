using System.Collections.Generic;

// カードのマップ分類(2026-11-06、マスターの依頼「カードを 共通/荒野街道/自然洞窟/天空回廊/ラストダンジョン の5分類で管理」)。
//  分類は「そのマップで解禁する場所」。一度解禁したカードは他のマップでも通常どおり使用・抽選できる(マップ専用にはしない)。
//  抽選率を分類で隠れて下げることはしない(レア度の通常の抽選のまま)。
//  ・表(Table)は cardId → 分類。表に無いカードは Common 扱い。
//  ・カード編集画面のフィルター(ALL/COMMON/WILDERNESS/CAVE/SKY/LAST DUNGEON)はこの API だけを読む。
public enum CardMapCategory { Common = 0, Wilderness = 1, Cave = 2, Sky = 3, LastDungeon = 4 }

public static class CardMapCategories
{
    // cardId → 分類(初期分類はカードのデータ担当が埋める。マスターの確認で移動する)
    static readonly Dictionary<string, CardMapCategory> table = new Dictionary<string, CardMapCategory>();

    public static void Set(string cardId, CardMapCategory c) { if (!string.IsNullOrEmpty(cardId)) table[cardId] = c; }

    public static CardMapCategory Of(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return CardMapCategory.Common;
        EnsureTable();
        return table.TryGetValue(BaseId(cardId), out var c) ? c : CardMapCategory.Common;
    }
    public static CardMapCategory Of(CardDefinition def) => def != null ? Of(def.cardId) : CardMapCategory.Common;

    // 分類の解禁に使うステージ id(Common は null = どこでも)
    public static string StageIdOf(CardMapCategory c) => c switch
    {
        CardMapCategory.Wilderness => "wasteland_road",
        CardMapCategory.Cave => "natural_cave",
        CardMapCategory.Sky => "sky_corridor",
        CardMapCategory.LastDungeon => "last_corridor",
        _ => null,
    };

    // 画面の名前(Loc.Src を通して30言語)。短い表記
    public static string LabelSrc(CardMapCategory c) => c switch
    {
        CardMapCategory.Common => "共通",
        CardMapCategory.Wilderness => "荒野",
        CardMapCategory.Cave => "洞窟",
        CardMapCategory.Sky => "天空",
        CardMapCategory.LastDungeon => "ラスダン",
        _ => "",
    };

    // v2 キー("v2|id|L|rarity|...")やレベル付きの表記から cardId を取り出す
    public static string BaseId(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (key.StartsWith("v2|")) { var p = key.Split('|'); return p.Length > 1 ? p[1] : key; }
        return key;
    }

    static bool built;
    static void EnsureTable()
    {
        if (built) return;
        built = true;
        CardMapCategoryTable.Fill(); // 初期分類(Cards/CardMapCategoryTable.cs)
    }
}

// カード編集画面の絞り込み・並び替え(2026-11-06)の新しい文。ID 10460〜10469(Tools/loc/keys_extra_cardfilter.tsv)
//  日本語と英語は今つないだ。ほかの言語はエステルのレビュー待ち(Docs/Cards_2026-11-06/loc_review)。訳が入るまでは英語で出る(Loc の英語への戻し)
//  マップの名前(共通/荒野/洞窟/天空/ラスダン)の元の文は CardMapCategories.LabelSrc(カードのデータ側の取り決め)
//  すでに訳のある文はそのまま使う: ALL(日本語は「すべて」)/ レア度 / 並び替え / 標準 / レベル / 名前 / 所持枚数 / なし
public static class LocCardFilter
{
    // ID 10460〜10464: マップの名前(絞り込みのタブ)。英語は COMMON / WILDERNESS / CAVE / SKY / LAST DUNGEON
    public const string Common = "共通";
    public const string Wilderness = "荒野";
    public const string Cave = "洞窟";
    public const string Sky = "天空";
    public const string LastDungeon = "ラスダン";

    public const string Element = "属性";                              // ID 10465(絞り込みの見出し / 並び替えの項目)
    public const string Map = "マップ";                                 // ID 10466(並び替えの項目: マップの分類の順)
    public const string UnlockOrder = "解放順";                          // ID 10467(並び替えの項目: 解放の距離・ガチャの段階の順)
    public const string Locked = "未解放 {0}";                           // ID 10468 {0}=このマップでまだ解放していないカードの種類の数
    public const string NoMatch = "この条件のカードはありません";            // ID 10469(絞り込みの結果が0件の時、一覧の中に出す)

    public static readonly string[] All = { Common, Wilderness, Cave, Sky, LastDungeon, Element, Map, UnlockOrder, Locked, NoMatch };
}

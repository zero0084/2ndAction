// 共通カードの追加(2026-11-06、18 枚)の文。訳の鍵は日本語の文(Loc の決まり)。
//  いまは日本語と英語(en.json)だけ。ほかの言語はエステルのレビュー(Docs/Cards_2026-11-06/loc)が済むまで英語の文を出す(Loc の英語への戻り)。
//  ID: 名前 10500〜10517 / 説明 10518〜10535 / 効果の行(カード詳細の Lv1/Lv9)10536〜10553(数値のカードは空)/ その他 10554〜
//  説明は Editor/CardBalanceV3 の表から、効果の行は CardBehaviorText から使う(文を二重に持たない)。数値は CardRewards と同じ
public static class LocCardCommon
{
    public struct Card
    {
        public string id, name, nameJa, desc, behavior;
        public Card(string id, string name, string nameJa, string desc, string behavior)
        { this.id = id; this.name = name; this.nameJa = nameJa; this.desc = desc; this.behavior = behavior; }
    }

    public static readonly Card[] Cards =
    {
        // ---- ★1 MILE(単一条件)
        new Card("combo_pay", "COMBO PAY", "コンボ手当",
            "連続ヒット(画面の「N HIT」)が10回に届くたびにMILE+1(1Lvごとに+1)。20回・30回…と続けるほど何度ももらえる(1回の連続ヒットで100HITまで)",
            "10HITごとに +{0} MILE"),
        new Card("hunt_pay", "HUNT PAY", "討伐手当",
            "雑魚を10体倒すたびにMILE+1(1Lvごとに+1)。ボスは数えない",
            "雑魚10体撃破ごとに +{0} MILE"),
        new Card("boss_pay", "BOSS PAY", "ボス手当",
            "ボス撃破のMILE+12%/Lv",
            ""),
        new Card("road_pay", "ROAD PAY", "走行手当",
            "1km走るたびにMILE+2(1Lvごとに+2)。疾走出発やCONTINUEで飛ばした区間は数えない",
            "1kmごとに +{0} MILE"),
        new Card("survival_pay", "SURVIVAL PAY", "生存手当",
            "ランで30秒生き残るたびにMILE+1(1Lvごとに+1)。カード選択・一時停止の間は数えない",
            "30秒ごとに +{0} MILE"),
        // ---- ★1 EXP(単一条件)
        new Card("combo_training", "COMBO TRAINING", "コンボ修行",
            "連続ヒットが10回に届くたびにEXP+20(1Lvごとに+20)。1回の連続ヒットで100HITまで",
            "10HITごとに EXP+{0}"),
        new Card("hunt_training", "HUNT TRAINING", "討伐修行",
            "雑魚を10体倒すたびにEXP+30(1Lvごとに+30)。ボスは数えない",
            "雑魚10体撃破ごとに EXP+{0}"),
        new Card("boss_training", "BOSS TRAINING", "ボス修行",
            "ボス撃破のEXP+15%/Lv",
            ""),
        new Card("road_training", "ROAD TRAINING", "走行修行",
            "1km走るたびにEXP+100(1Lvごとに+100)。疾走出発やCONTINUEで飛ばした区間は数えない",
            "1kmごとに EXP+{0}"),
        new Card("survival_training", "SURVIVAL TRAINING", "生存修行",
            "ランで30秒生き残るたびにEXP+30(1Lvごとに+30)。カード選択・一時停止の間は数えない",
            "30秒ごとに EXP+{0}"),
        // ---- ★3 リスク付き(敵の側が忙しくなる代わりに報酬)
        new Card("danger_bounty", "DANGER BOUNTY", "危険報酬",
            "雑魚の一部が高火力敵(赤紫のオーラ。攻撃のダメージ+1ハート)になる(1Lvごとに+3%、最大30%。0kmから)。代わりに敵撃破・BONUSのMILE+15%/Lv、敵撃破EXP+10%/Lv。敵が変わるのはソロの通常マップだけ",
            "雑魚が高火力敵になる確率 +{0}%"),
        new Card("sky_siege", "SKY SIEGE", "空襲",
            "空中の敵の枠に空中雑魚(急降下・横切りなど)が出やすくなり(+5%/Lv)、空の敵がいる組も出やすくなる(+10%/Lv)。代わりに敵撃破EXP+15%/Lv、敵撃破・BONUSのMILE+8%/Lv",
            "空中雑魚の出現 +{0}%・空の敵がいる組 ×{1}"),
        new Card("crossroads", "CROSSROADS", "分かれ道",
            "道中の判断イベント(上下の帯・封鎖・高火力敵など)が早い距離から始まり(通常50km → 1Lvごとに4km早く、最短10km)、間隔も短くなる(-5%/Lv、最大-50%)。代わりに敵撃破・BONUSのMILE+12%/Lv、獲得EXP+8%/Lv。判断イベントはソロの通常マップだけ",
            "判断イベントが{0}kmから・間隔×{1}"),
        new Card("high_stakes", "HIGH STAKES", "大勝負",
            "ボスから受けるダメージ+10%/Lv(ハート単位。端数は確率で1つ増える)。代わりにボス撃破MILE+35%/Lv、ボス撃破EXP+15%/Lv",
            "ボスから受けるダメージ +{0}%"),
        // ---- ★4 複合(デメリットなし。条件の組み合わせで遊び方が変わる)
        new Card("chain_bounty", "CHAIN BOUNTY", "連撃の賞金",
            "連続ヒットが10HIT以上続いている間に倒した雑魚のMILEと撃破EXPが増える(+15%/Lv)。連続ヒットを切らさずに倒し続けるほど得",
            "10HIT以上の連続ヒット中の撃破 MILE・EXP +{0}%"),
        new Card("iron_marathon", "IRON MARATHON", "鉄人マラソン",
            "被弾せずに1km走るたびにMILE+3とEXP+150(1Lvごとに増加)。被弾すると数え直し(Shieldで防いだ時はそのまま)",
            "被弾せずに1kmごとに +{0} MILE・EXP+{1}"),
        new Card("trophy_hunt", "TROPHY HUNT", "戦利品狩り",
            "ボスを倒すと、前のボスから倒した雑魚1体につきMILE+0.2とEXP+3(1Lvごとに増加、最大100体分)。雑魚を倒してからボスへ挑むほど得",
            "ボス撃破で 雑魚1体につき +{0} MILE・EXP+{1}(最大100体)"),
        new Card("horizon", "HORIZON", "地平線",
            "走行距離10kmごとに、敵撃破・ボス・BONUSのMILEと獲得EXPが+2%/Lvずつ増える(100kmで最大、Lv1で+20%)。遠くまで走るほど伸びる",
            "10kmごとに MILE・EXP +{0}%(100kmで最大+{1}%)"),
    };

    // 効果の行の名前(数値のカード)/ マップ分類の解放の条件(CardMapUnlock)
    public const string LabelBossExp = "ボス撃破EXP";                  // ID 10554
    public const string UnlockOnMap = "{0}で{1}m到達して帰還";          // ID 10555 {0}=マップ名 {1}=距離(3桁区切り)
    public const string UnlockLastDungeon = "ラストダンジョンの解放";     // ID 10556

    public static Card Find(string id)
    {
        foreach (var c in Cards) if (c.id == id) return c;
        return default;
    }

    public static readonly string[] Ids =
    {
        "combo_pay", "hunt_pay", "boss_pay", "road_pay", "survival_pay",
        "combo_training", "hunt_training", "boss_training", "road_training", "survival_training",
        "danger_bounty", "sky_siege", "crossroads", "high_stakes",
        "chain_bounty", "iron_marathon", "trophy_hunt", "horizon",
    };

    // レビュー待ちの一覧(ID, 文)。資料の書き出し(Tools/cards)と製品版の確認(LocPendingReview.Pending)が読む
    public static (string id, string key)[] PendingList()
    {
        var l = new System.Collections.Generic.List<(string, string)>();
        for (int i = 0; i < Cards.Length; i++) l.Add(((10500 + i).ToString(), Cards[i].name));
        for (int i = 0; i < Cards.Length; i++) l.Add(((10518 + i).ToString(), Cards[i].desc));
        for (int i = 0; i < Cards.Length; i++) if (!string.IsNullOrEmpty(Cards[i].behavior)) l.Add(((10536 + i).ToString(), Cards[i].behavior));
        l.Add(("10554", LabelBossExp));
        l.Add(("10555", UnlockOnMap));
        l.Add(("10556", UnlockLastDungeon));
        return l.ToArray();
    }
}

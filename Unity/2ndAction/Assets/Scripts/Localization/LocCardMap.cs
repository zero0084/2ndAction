// マップ専用カード 20 枚(2026-11-06。Docs/Cards_2026-11-06/map_cards_v2.md)の文と分類。訳の鍵は日本語の文(Loc の決まり)。
//  いまは日本語と英語(en.json)だけ。ほかの言語はエステルのレビュー(Tools/loc/est40)が済むまで英語の文を出す(Loc の英語への戻り)。
//  ID: 名前 10557〜10576(英字のカード名。全言語で英字のまま)/ 説明 10577〜10596 / 効果の行(カード詳細の Lv ごと)10597〜10616
//  説明は Editor/CardBalanceV3(MapTable)が、効果の行は CardBehaviorText が使う(文を二重に持たない)。数値は MapCardRules と同じ。
//  レア度・分類・カテゴリ・属性もここ(CardDatabaseBuilder / CardBalanceV3 / CardMapCategoryTable / QA が読む)。解放の距離はレア度から(★2 = 0m / ★3 = 5km / ★4〜5 = 20km)。
//  洞窟/天空は max(解放の距離, 1,000m)、ラスダンはラスダンの解放後(CardMapUnlock)。
//  名前は仮(エステルと相談済み: MONO ELEMENT → ELEMENTAL FOCUS)
public static class LocCardMap
{
    public struct Card
    {
        public string id, name, nameJa, desc, behavior;
        public int rarity; public CardMapCategory map; public CardCategory category; public ElementType element;
        public Card(string id, string name, string nameJa, int rarity, CardMapCategory map, CardCategory category, ElementType element, string desc, string behavior)
        { this.id = id; this.name = name; this.nameJa = nameJa; this.rarity = rarity; this.map = map; this.category = category; this.element = element; this.desc = desc; this.behavior = behavior; }
    }

    const CardMapCategory V = CardMapCategory.Cave, S = CardMapCategory.Sky, L = CardMapCategory.LastDungeon;
    const ElementType N = ElementType.None;

    public static readonly Card[] Cards =
    {
        // ---- 自然洞窟(5): 被弾・BREAK・最大HP・回復の超過を別の形で使う耐久
        new Card("last_bastion", "LAST BASTION", "最後の砦", 3, V, CardCategory.Defense, N,
            "HPが30%以下になった瞬間、8秒で消える一時シールドを得る(Lv1で1枚、Lv5で2枚、Lv9で3枚)。再発動まで90秒(Lv9で60秒)。一時シールドは通常のShieldとは別に数え、合計3枚まで",
            "HP30%以下で一時シールド{0}枚({1}秒)・再発動まで{2}秒"),
        new Card("overflow_ward", "OVERFLOW WARD", "超過治癒", 4, V, CardCategory.Defense, N,
            "最大HPを超えた回復の30%(Lv9で70%)を一時シールドに変える(ハート1つ分で1枚。20秒で消える)。最大1枚(Lv5で2枚、Lv9で3枚)、変換は3秒に1回",
            "超過した回復の{0}%を一時シールドへ(最大{1}枚・{2}秒で消える)"),
        new Card("revenge_armor", "REVENGE ARMOR", "報復の鎧", 3, V, CardCategory.Defense, N,
            "ハート2つ以上の被弾の後3秒(Lv9で5秒)、受けるダメージがハート1つ減り(最低ハート1つ)、攻撃+20%(Lv9で+40%)。再発動まで8秒。ハート1つの接触では発動しない",
            "ハート2つ以上の被弾の後{0}秒 被ダメージ-1ハート・攻撃+{1}%"),
        new Card("break_feast", "BREAK FEAST", "破砕の宴", 3, V, CardCategory.Heal, N,
            "ボスをBREAKさせるとHPが回復する(Lv1でハート1つ、Lv5で2つ、Lv9で3つ)。20秒に1回",
            "BREAKさせるとハート{0}回復({1}秒に1回)"),
        new Card("iron_heart", "IRON HEART", "鋼の心臓", 4, V, CardCategory.Defense, N,
            "最大HPが5ハートを超えた分、1ハートにつき回復量とShieldの回復の速さが+2%(Lv9で+4%)。上限+30%(Lv9で+50%)",
            "5ハートを超えた最大HP 1つにつき回復量・Shieldの回復+{0}%(上限+{1}%)"),
        // ---- 天空回廊(5): 空中・速度・打ち上げ・急降下・BREAK
        new Card("air_combo", "AIR COMBO", "空中連撃", 2, S, CardCategory.Attack, N,
            "着地せずに攻撃を5回(Lv5で4回、Lv9で3回)当てると、3秒間 攻撃+10%(Lv9で+26%)。着地すると数え直し",
            "着地せずに{0}回当てると{1}秒 攻撃+{2}%"),
        new Card("sky_breaker", "SKY BREAKER", "天空破砕", 3, S, CardCategory.Attack, N,
            "空中にいる間の攻撃で、ボスのBREAKが溜まりやすくなる(+15%、Lv9で+55%)",
            "空中の攻撃のBREAKの溜まり+{0}%"),
        new Card("dive_boost", "DIVE BOOST", "急降下加速", 3, S, CardCategory.Movement, N,
            "急降下で着地した後2.5秒、移動速度+10%(Lv9で+26%)・攻撃+15%(Lv9で+39%)。着地のたびに発動",
            "急降下の着地の後{0}秒 移動速度+{1}%・攻撃+{2}%"),
        new Card("launch_master", "LAUNCH MASTER", "打ち上げの達人", 3, S, CardCategory.Attack, N,
            "空中から打ち上げた敵への攻撃+12%(Lv9で+44%)。打ち上げた敵の落下が10%(Lv9で30%)ゆっくりになる(入力の受付や攻撃の判定の時間は変わらない)",
            "打ち上げた敵への空中の攻撃+{0}%・落下が{1}%ゆっくり"),
        new Card("sonic_edge", "SONIC EDGE", "音速の刃", 4, S, CardCategory.Attack, N,
            "130km/h以上で走っている間、攻撃+15%(Lv9で+47%)・ボスのBREAKの溜まり+10%(Lv9で+42%)。高速時の事故のダメージは変わらない",
            "130km/h以上で攻撃+{0}%・BREAKの溜まり+{1}%"),
        // ---- ラストダンジョン(10): デッキの組み方そのものが変わる
        new Card("second_rise", "SECOND RISE", "再起", 4, L, CardCategory.Heal, N,
            "1ランに1回、HPが25%以下になると最大HPの30%(Lv9で60%)を回復し、8秒間 攻撃と移動速度+20%(Lv9で+40%)",
            "1ランに1回 HP25%以下で最大HPの{0}%回復・{1}秒 攻撃・移動速度+{2}%"),
        new Card("shield_burst", "SHIELD BURST", "盾の爆発", 4, L, CardCategory.Defense, N,
            "Shield(一時シールドを含む)が壊れると、周囲3mに攻撃力の300%(Lv9で700%)の衝撃を放ち、ボスのBREAKも+5%(Lv9で+15%)。6秒(Lv9で3秒)に1回",
            "Shieldが壊れると周囲3mに攻撃力の{0}%・ボスのBREAK+{1}%({2}秒に1回)"),
        new Card("blood_armor", "BLOOD ARMOR", "血の鎧", 4, L, CardCategory.Defense, ElementType.Blood,
            "吸収(命中の吸収・撃破の回復・出血の吸収)で最大HPを超えた分の50%(Lv9で100%)を、ハート単位で被弾を消す「血の盾」にする(最大ハート1つ、Lv9で3つ。10秒で1つずつ減る)。血の盾がある間 出血の確率+10%(Lv9で+30%)",
            "吸収の超過の{0}%を血の盾へ(最大ハート{1})・血の盾がある間 出血の確率+{2}%"),
        new Card("elemental_chaos", "ELEMENTAL CHAOS", "属性混沌", 4, L, CardCategory.Special, N,
            "デッキの属性カードの種類(炎/氷/雷/風/血)が多いほど、属性の確率と威力が上がる。2種で+6%、3種で+12%、4種で+20%、5種で+30%(Lv9で+10/20/32/50%)。ELEMENTAL FOCUSと両方持つと高い方だけ",
            "デッキの属性 2/3/4/5種で属性の確率・威力+{0}/{1}/{2}/{3}%"),
        new Card("elemental_focus", "ELEMENTAL FOCUS", "属性集中", 4, L, CardCategory.Special, N,
            "デッキの属性カードが1種類だけなら、その属性の威力+30%(Lv9で+70%)・確率+10%(Lv9で+30%)。2種類なら半分、3種類以上は効果なし。ELEMENTAL CHAOSと両方持つと高い方だけ",
            "デッキの属性が1種類なら威力+{0}%・確率+{1}%(2種類なら半分)"),
        new Card("break_dominion", "BREAK DOMINION", "破砕支配", 4, L, CardCategory.Attack, N,
            "BREAK中の敵への攻撃+15%(Lv9で+47%)・属性の威力+10%(Lv9で+42%)。BREAK中に倒したボスのMILEとEXP+10%(Lv9で+42%)",
            "BREAK中の敵へ攻撃+{0}%・属性の威力+{1}%・撃破のMILE/EXP+{2}%"),
        new Card("endless_growth", "ENDLESS GROWTH", "終わりなき成長", 3, L, CardCategory.Attack, N,
            "敵を倒すたびに、そのランの間だけ攻撃+0.2%(Lv9で+0.4%)。上限+15%(Lv9で+40%)。ランが終わると消える",
            "撃破ごとに攻撃+{0}%(上限+{1}%)"),
        new Card("final_drive", "FINAL DRIVE", "最終疾走", 5, L, CardCategory.Special, N,
            "80km以降、移動速度+10%(Lv9で+20%)・攻撃+30%(Lv9で+70%)・ボスのBREAKの溜まり+20%(Lv9で+60%)。事故・判断イベント・敵の火力はそのまま",
            "80km以降 移動速度+{0}%・攻撃+{1}%・BREAKの溜まり+{2}%"),
        new Card("last_gamble", "LAST GAMBLE", "最後の賭け", 5, L, CardCategory.Risk, N,
            "【危険】敵の攻撃が30%の確率でハート1つ多くなる・高火力敵+10%・判断イベントの間隔-30%・敵の出現+15%(Lvで変わらない。敵のHPは上げない)。【報酬】敵撃破・BONUS・ボス撃破のMILE+100%(Lv9で+300%)・獲得EXP+60%(Lv9で+180%)",
            "敵の攻撃が{0}%でハート+1・高火力敵+{1}%・判断イベントの間隔-{2}%"),
        new Card("one_more_chance", "ONE MORE CHANCE", "もう一度だけ", 5, L, CardCategory.Special, N,
            "1ランに1回、倒れるダメージを受けてもハート1つで残る(1.5秒無敵)。その後10秒間 攻撃+40%(Lv9で+100%)・移動速度+15%(Lv9で+30%)・ボスのBREAKの溜まり+30%(Lv9で+80%)。PHOENIXより先に使われる",
            "1ランに1回 倒れずハート1つで残る→{0}秒 攻撃+{1}%・移動速度+{2}%・BREAK+{3}%"),
    };

    public static Card Find(string id)
    {
        foreach (var c in Cards) if (c.id == id) return c;
        return default;
    }

    static string[] ids;
    public static string[] Ids
    {
        get
        {
            if (ids != null) return ids;
            ids = new string[Cards.Length];
            for (int i = 0; i < Cards.Length; i++) ids[i] = Cards[i].id;
            return ids;
        }
    }

    // 解放の距離(BEST)とガチャの段階(共通カードの追加と同じ決まり)
    public static float UnlockOf(int rarity) => rarity >= 4 ? 20000f : rarity == 3 ? 5000f : 0f;
    public static int StageOf(int rarity) => rarity >= 4 ? 3 : rarity == 3 ? 2 : 1;

    // レビュー待ちの一覧(ID, 文)。製品版の確認(LocPendingReview.Pending)と訳の準備(Tools/cards/mapcards_loc.py)が読む。
    //  名前(10557〜10576)は全言語で英字のままなので入れない(既存のカード名と同じ扱い)
    public static (string id, string key)[] PendingList()
    {
        var l = new System.Collections.Generic.List<(string, string)>();
        for (int i = 0; i < Cards.Length; i++) l.Add(((10577 + i).ToString(), Cards[i].desc));
        for (int i = 0; i < Cards.Length; i++) l.Add(((10597 + i).ToString(), Cards[i].behavior));
        return l.ToArray();
    }
}

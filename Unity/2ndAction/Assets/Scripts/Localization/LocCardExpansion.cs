// カード拡張(2026-10-31、属性 12 枚)の文。訳の鍵は日本語の文(Loc の決まり)。エステルのレビュー後に30言語へ反映済み(Docs/LocReview_2026-10-31、Tools/loc/keys_extra34)。
//  名前 10239〜10250 / 説明 10251〜10262 / 効果の行(カード詳細の Lv1/Lv9)10263〜10274 / FINAL EVOLUTION の題 10275〜10286・説明 10287〜10298
//  説明は Editor/CardBalanceV3 の表から、効果の行は CardBehaviorText から、FINAL EVOLUTION は FinalEvolutionTuning から使う(文を二重に持たない)
public static class LocCardExpansion
{
    public struct Card
    {
        public string id, name, desc, behavior, feTitle, feDesc;
        public Card(string id, string name, string desc, string behavior, string feTitle, string feDesc)
        { this.id = id; this.name = name; this.desc = desc; this.behavior = behavior; this.feTitle = feTitle; this.feDesc = feDesc; }
    }

    public static readonly Card[] Cards =
    {
        new Card("scorch", "SCORCH",
            "炎上中の敵への攻撃力+6%、以降+4%/Lv(Lv9で+38%)。ボスにも有効。単独でも命中時に炎上を付与：確率はLv1で8%、以降+2%/Lv",
            "炎上中の敵への攻撃+{0}%",
            "焦熱", "10秒間 炎上中の敵への攻撃×1.8"),
        new Card("fire_whirl", "FIRE WHIRL",
            "炎上中の敵の近くにいる敵へ、1秒ごとに確率で炎上が移る(Lv1で15%、以降+3%/Lv)。風刃が炎上中の敵に当たると、周りの敵にも炎を運ぶ。単独でも命中時に炎上を付与：確率はLv1で8%、以降+2%/Lv",
            "炎上が{0}m以内の敵へ移る(1秒ごとに{1}%)・風刃が炎を運ぶ 半径{2}m",
            "火炎旋風", "10秒間 炎上が移りやすく、届く範囲も広がる"),
        new Card("combustion", "COMBUSTION",
            "炎上中の敵に炎上を4回重ねると爆発し、周りの敵にダメージを与えて炎上させる(爆発した敵の炎上は消える)。威力と範囲はLvで増加。同じ敵の爆発は4秒(ボスは6秒)に1回。単独でも命中時に炎上を付与：確率はLv1で8%、以降+2%/Lv",
            "炎上を4回重ねると爆発 威力{0}・半径{1}m",
            "大爆炎", "10秒間 爆発の威力と範囲×1.8"),
        new Card("permafrost", "PERMAFROST",
            "冷えが重なるほど減速が強くなる：2回目から1回ごとに減速+3.5%(Lv9で+7.5%)、最大3回分。冷え時間+0.15秒/Lv。単独でも命中時に冷えを付与：確率はLv1で8%、以降+2%/Lv",
            "冷えの2回目以降、1回ごとに減速+{0}%(最大3回分)",
            "永久凍土", "10秒間 冷えの重なりによる減速×1.8"),
        new Card("glacier_guard", "GLACIER GUARD",
            "このカードだけでもShieldを1つ持てる。敵を凍結させるか冷えを3回重ねるとShieldが1つ回復する(Lv1で19秒に1回、以降1秒ずつ短縮)。単独でも命中時に冷えを付与：確率はLv1で8%、以降+2%/Lv",
            "凍結/冷え3回でShieldが1つ回復({0}秒に1回)",
            "氷河の守り", "10秒間 凍結でShieldが回復する間隔が短くなる"),
        new Card("shatter_strike", "SHATTER STRIKE",
            "凍結中、または冷えが3回以上重なった敵に攻撃を当てると氷を砕く追撃(周りの敵にも半分)。凍結と冷えの重なりは消える。ボスは同じ条件で砕け、BREAKが溜まる(同じボスは8秒に1回)。威力とBREAKはLvで増加。単独でも命中時に冷えを付与：確率はLv1で8%、以降+2%/Lv",
            "凍結/冷え3回以上の敵を砕く 威力{0}・ボスはBREAK+{1}%",
            "粉砕", "10秒間 砕く追撃の威力とBREAK×1.8"),
        new Card("conductor", "CONDUCTOR",
            "落雷を受けた敵が2秒帯電し、帯電中の敵への攻撃力+6%(以降+4%/Lv)。冷え・凍結中の敵への落雷+8%(以降+4%/Lv)、連鎖も届きやすい。単独でも落雷が発生：確率はLv1で5%、以降+1%/Lv",
            "帯電中の敵への攻撃+{0}%・冷えた敵への落雷+{1}%",
            "超伝導", "10秒間 帯電中・冷えた敵への強化×1.8"),
        new Card("volt_surge", "VOLT SURGE",
            "1回の攻撃の命中3回ごとに追加の落雷(最大3本)。多段攻撃や範囲攻撃と相性が良い。追加の落雷の威力は通常の65%(以降+5%/Lv)。単独でも落雷が発生：確率はLv1で5%、以降+1%/Lv",
            "1回の攻撃の命中3回ごとに落雷(威力{0}%・最大3本)",
            "過電流", "10秒間 追加の落雷の威力×1.8"),
        new Card("judgment_bolt", "JUDGMENT BOLT",
            "落雷を重ねると雷力が溜まり、満タンで次の落雷が大落雷になる(周りにもダメージ・雑魚を一瞬停止・ボスはBREAKが溜まる)。必要な回数はLv1で10回(Lv9で6回)、威力はLv1で2.2倍(Lv9で3.8倍)。大落雷は4秒に1回まで。単独でも落雷が発生：確率はLv1で5%、以降+1%/Lv",
            "落雷{0}回で大落雷 威力×{1}・ボスはBREAK+{2}%",
            "天罰", "10秒間 大落雷に必要な回数が減り、威力が上がる"),
        new Card("slipstream", "SLIPSTREAM",
            "走る速さに応じて風刃が強くなり、出やすくなる(40km/hから伸びて120km/hで最大)。最大で威力+14%・確率+1.5%、以降1Lvごとに+4%・+0.5%。単独でも風刃が発生：確率はLv1で6%、以降+2%/Lv",
            "速いほど風刃が強い 最大で威力+{0}%・確率+{1}%(120km/h)",
            "追い風", "10秒間 速さによる風刃の強化×1.8"),
        new Card("updraft", "UPDRAFT",
            "空中の敵(打ち上げた敵・飛ぶ敵)への攻撃力+6%(以降+4%/Lv)。自分も空中で当てると風の追撃(威力7.5、以降+1.5/Lv)。単独でも風刃が発生：確率はLv1で6%、以降+2%/Lv",
            "空中の敵への攻撃+{0}%・空中で当てると風の追撃 威力{1}",
            "昇り龍", "10秒間 空中の敵への攻撃と追撃×1.8"),
        new Card("tempest", "TEMPEST",
            "風刃が続けて飛ぶ：Lv1〜6で2本、Lv7〜9で3本。後の風刃の威力は30%(以降+3%/Lv)。単独でも風刃が発生：確率はLv1で6%、以降+2%/Lv。続けて飛ぶのは1秒に1回まで、ボスへのダメージは半分",
            "風刃が{0}本続けて飛ぶ(後の風刃は威力{1}%)",
            "大嵐", "10秒間 続けて飛ぶ風刃の威力が上がる"),
    };

    public static Card Find(string id)
    {
        foreach (var c in Cards) if (c.id == id) return c;
        return default;
    }

    // レビュー待ちの一覧(ID, 文)。LocPendingReview.Pending に足される
    public static (string id, string key)[] PendingList()
    {
        var l = new System.Collections.Generic.List<(string, string)>();
        for (int i = 0; i < Cards.Length; i++)
        {
            l.Add(((10239 + i).ToString(), Cards[i].name));
            l.Add(((10251 + i).ToString(), Cards[i].desc));
            l.Add(((10263 + i).ToString(), Cards[i].behavior));
            l.Add(((10275 + i).ToString(), Cards[i].feTitle));
            l.Add(((10287 + i).ToString(), Cards[i].feDesc));
        }
        return l.ToArray();
    }
}

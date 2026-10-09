# -*- coding: utf-8 -*-
"""
カードのレア度 再評価(2026-10-09)用の分析スクリプト。調査専用: ゲームのデータは一切書き換えない(読むだけ)。

実行(実 Python。PATH の python は偽物):
  C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe simulate.py        # すべて出力
  ... simulate.py dump    # 読み込んだカード一覧をコンソールに出すだけ

読み込むもの(すべて現行の実装から直接読む。古いドキュメントの値は使わない):
  Assets/Resources/Cards/*.asset                  … cardId / cardName / category / rarity / unlockDistance / gachaStage / sortOrder / description / effects
  Assets/Scripts/GameManager.cs                    … gachaRarityWeights(既定値) / DeckCapacity / GachaCostMile
  Assets/Scenes/Main.unity                         … シーンに保存された gachaRarityWeights(public フィールドなので実際に使われるのはこちら)
  Assets/Scripts/Data/GachaStage.cs                … ガチャ段階のしきい値
  Assets/Scripts/Data/EffectType.cs                … 効果の種類の名前
  Assets/Scripts/Cards/ComboTuning.cs              … COMBO 15種の構成(Resources/Cards/ComboTuning.asset は現在無い → コードの既定値が使われる)
  Assets/Resources/Mastery/MasteryTuning.asset     … Mastery の必要枚数

カード番号(No.)は sortOrder の順(= Docs/CardBalanceV3_2026-10-04.md の #1〜#99 と同じ並び、#100 = ULTIMATE)。

モデル(コードの再現):
  ガチャ 1回 = GameManager.DrawFromGachaPool
     1) 対象 = GachaStage.IsCardEligible(BEST >= unlockDistance かつ 段階 >= gachaStage)
     2) 対象の中に「1枚でも存在する★」だけで重み {50,30,15,4,1} を正規化して★を選ぶ(存在しない★の重みは他へ配られる)
     3) その★の中から一様に1枚
  LEVEL UP / BOSS REWARD / 疾走リング = デッキ(重複を含む、ラン中Lv9などで取れない物は除く)から重複なしで3件(FE候補がある LEVEL UP は2件+FE)
     → ★は一切関係しない。★が効くのは「所持しているか」(= ガチャ)だけ

出力(このファイルと同じフォルダ):
  rarity_table.csv          … 100枚の表(UTF-8 BOM)
  sim_gacha_by_rarity.csv   … ガチャ段階ごと・★ごとの排出率(シナリオ別)
  sim_gacha_by_card.csv     … ガチャ段階ごと・カードごとの排出率と必要回数(現状/提案)
  sim_combo.csv             … COMBO 15種の構成カードの★と、そろう確率(現状/提案)
  sim_summary.txt           … README に載せた数字の元(コンソール出力と同じ)
"""
import copy
import csv
import glob
import io
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.normpath(os.path.join(HERE, "..", "..", "Unity", "2ndAction", "Assets"))

CATEGORY_JA = {0: "移動(Move)", 1: "攻撃(Attack)", 2: "防御(Defense)", 3: "成長(Growth)", 4: "回復(Heal)", 5: "特殊(Special)", 6: "リスク(Risk)"}
ELEMENT_JA = {0: "", 1: "炎", 2: "氷", 3: "雷", 4: "風", 5: "血"}


def read(path):
    with io.open(path, "r", encoding="utf-8") as f:
        return f.read()


# ------------------------------------------------------------------ Unity の .asset(YAML)を最小限に読む
def decode_yaml_scalar(lines, i):
    """lines[i] の 'key: value' の value。複数行の "..." と \\uXXXX に対応。戻り値 (値, 次の行番号)"""
    val = lines[i].split(":", 1)[1].strip()
    if val.startswith('"'):
        buf = val[1:]
        j = i
        while not re.search(r'(?<!\\)"\s*$', buf):
            j += 1
            if j >= len(lines):
                break
            buf += " " + lines[j].strip()  # YAML の折り返し(改行+インデント)= 空白1つ
        buf = re.sub(r'"\s*$', "", buf)
        s = buf.replace('\\"', '"')
        s = re.sub(r"\\u([0-9A-Fa-f]{4})", lambda m: chr(int(m.group(1), 16)), s)
        return s.replace("\\n", "\n"), j + 1
    j = i + 1
    while j < len(lines) and lines[j].startswith("    ") and not lines[j].strip().startswith("-"):
        val += " " + lines[j].strip()
        j += 1
    return val, j


def load_effect_names():
    src = read(os.path.join(PROJ, "Scripts", "Data", "EffectType.cs"))
    body = src[src.index("enum EffectType"):]
    body = body[body.index("{") + 1: body.index("}")]
    body = re.sub(r"//.*", "", body)
    return [t.strip() for t in body.split(",") if t.strip()]


def load_cards():
    names = load_effect_names()
    cards = []
    for path in sorted(glob.glob(os.path.join(PROJ, "Resources", "Cards", "*.asset"))):
        lines = read(path).replace("\r", "").split("\n")
        if not any(l.strip().startswith("cardId:") for l in lines):
            continue  # ComboTuning などカード以外のアセット
        c = {"file": os.path.basename(path), "effects": []}
        i = 0
        while i < len(lines):
            l = lines[i]
            s = l.strip()
            if l.startswith("  ") and not l.startswith("   ") and ":" in s:
                key = s.split(":", 1)[0]
                if key == "effects":
                    i += 1
                    while i < len(lines) and (lines[i].startswith("  - ") or lines[i].startswith("    ")):
                        t = lines[i].strip()
                        if t.startswith("- type:"):
                            c["effects"].append({"type": int(t.split(":")[1])})
                        elif t.startswith("value:") and c["effects"]:
                            c["effects"][-1]["value"] = float(t.split(":")[1])
                        i += 1
                    continue
                if key in ("cardId", "cardName", "description", "category", "sortOrder", "recommendPriority", "rarity", "unlockDistance", "gachaStage", "element"):
                    v, i = decode_yaml_scalar(lines, i)
                    c[key] = v
                    continue
            i += 1
        for k in ("category", "sortOrder", "recommendPriority", "rarity", "gachaStage", "element"):
            c[k] = int(float(c.get(k, 0)))
        c["unlockDistance"] = float(c.get("unlockDistance", 0))
        for e in c["effects"]:
            e["name"] = names[e["type"]] if e["type"] < len(names) else str(e["type"])
        cards.append(c)
    cards.sort(key=lambda c: c["sortOrder"])  # CardDatabase.Load と同じ並び
    for n, c in enumerate(cards):
        c["no"] = n + 1
    return cards


def load_game_constants():
    gm = read(os.path.join(PROJ, "Scripts", "GameManager.cs"))
    w = re.search(r"gachaRarityWeights\s*=\s*\{([^}]*)\}", gm).group(1)
    code_w = [float(x.strip().rstrip("f")) for x in w.split(",")]
    scene_w = None
    scene = os.path.join(PROJ, "Scenes", "Main.unity")
    if os.path.exists(scene):
        txt = read(scene).replace("\r", "")
        m = re.search(r"gachaRarityWeights:\n((?:  - [0-9.]+\n)+)", txt)
        if m:
            scene_w = [float(x.strip()[2:]) for x in m.group(1).strip().split("\n")]
    deck = int(re.search(r"DeckCapacity\s*=\s*(\d+)", gm).group(1))
    cost = int(re.search(r"GachaCostMile\s*=\s*(\d+)", gm).group(1))
    gs = read(os.path.join(PROJ, "Scripts", "Data", "GachaStage.cs"))
    th = [float(x.strip().rstrip("f")) for x in re.search(r"Thresholds\s*=\s*\{([^}]*)\}", gs).group(1).split(",")]
    mt = read(os.path.join(PROJ, "Resources", "Mastery", "MasteryTuning.asset"))
    hexs = re.search(r"need:\s*([0-9a-f]+)", mt).group(1)
    need = [int.from_bytes(bytes.fromhex(hexs[i:i + 8]), "little") for i in range(0, len(hexs), 8)]
    return {"weights": scene_w or code_w, "code_weights": code_w, "scene_weights": scene_w, "deck": deck, "cost": cost, "thresholds": th, "mastery_need": need}


def load_combos():
    src = read(os.path.join(PROJ, "Scripts", "Cards", "ComboTuning.cs"))
    out = []
    for m in re.finditer(r'C\("([a-z_]+)",\s*"([^"]+)",\s*Module\.(\w+),\s*"([a-z_]+)",\s*"([a-z_]+)"', src):
        out.append({"id": m.group(1), "name": m.group(2), "module": m.group(3), "a": m.group(4), "b": m.group(5)})
    return out, os.path.exists(os.path.join(PROJ, "Resources", "Cards", "ComboTuning.asset"))


# ================================================================== 提案(ここを変えれば再計算できる)
# cardId -> 変更(rarity / gachaStage / unlockDistance)と理由。ここに無いカードは現状維持。
PROPOSAL = {
    "long_haul": dict(rarity=2, reason="EXP+15%/Lv と最大HP(3Lvごと+1)の控えめな複合。★2にして成長ビルドの入口を広げる。EXP は1つの枠+曲線(CardRules.ExpMultiplier、+180%まで等倍→+450%まで半分→その先20%)を通るので、上限の強さは変わらない。段階2(5km)はそのまま"),
    "executioner": dict(rarity=1, reason="撃破MILE+15%/Lv だけで代償なしの素直な MILE カード。★1にして序盤から MILE を稼ぐ手段を持ちやすくする。強い Risk/Reward(GREED/ELITE/HELL MODE 等)と最上位の ONE MORE MILE は据え置くので長期の経済は大きく動かない"),
    "momentum": dict(rarity=3, reason="COMBO 2種(SONIC MOMENTUM・REDLINE)の要。REDLINE は唯一『両方★4』で揃いにくい。効果は速さ条件つき(100km/h超で伸び始め150km/hで最大、Lv9 +36%、条件の攻撃の枠)で単体は突出していない。★3に下げて段階3(20km)のまま"),
    "combo_master": dict(rarity=3, reason="COMBO FINISHING BLOW(COMBO PLUS★2 + COMBO MASTER)は大事な『基本+上位』の形だが、★4側が律速で段階5でも両方そろうまで約900回。締めの特殊衝撃は SHOCKWAVE/GROUND BREAKER(★3、同じく現象を起こすカード)と同格なので★3に下げる。段階3(20km)はそのまま"),
}

# 個別検討(推奨は現状維持のまま、代案だけ示す)
REVIEW = {
    "boss_killer": "上げる候補として検証したが、推奨は据え置き(★3)。ボスへ+8%/Lv は『条件の攻撃』の枠(CardRules.CondMultiplier: +40%まで等倍→+140%まで40%)を通るので Lv9 単体で ×1.53、ATTACK UP Lv9(×1.45、無条件)と大差なく、ボス以外では効果なし。上げる場合は★4だけでなく段階3/20,000m も同時に変えること(★だけ4にして段階2のままだと、段階2には★4が他に無いため★4の箱を1枚で独占し、段階2での1回の確率が 0.46%→4.04% と約9倍に上がる逆効果)。段階を上げると BEST 5〜20km の人はガチャで引けなくなる(所持済みは使える)",
    "treasure_hunter": "代案: ★2→★1。BONUS ZONE/WANTED の報酬MILEに依存するが、ボスMILE・距離MILE も少し増える基本の MILE カード。EXECUTIONER と同時に★1にすると★1の箱が9→11枚になり、初期カードの複製(合成/Mastery 用)が約18%出にくくなるので、今回は EXECUTIONER だけを推奨",
    "level_break": "代案: ★4→★3(推奨しない)。EXP+30%/Lv は EXP UP の1.5倍で、EXP 特化(EXP UP+LEVEL BREAK+EXPERIENCE BURST)が序盤から強くなりすぎる。成長ビルドの入口は LONG HAUL の★2化で足りる",
    "flame_blade": "代案: ★4→★3。属性の『入口』なのに★4/段階3で、上位の BURNING SOUL(★3/段階2)より入手しにくい逆転がある。COMBO BLAZING EDGE の構成。今回は方針(少数だけ)に合わせ現状維持",
    "frost_edge": "代案: ★4→★3。氷の入口が上位の ICE PRISON(★3/段階2)より入手しにくい逆転。COMBO FROZEN PRISON の構成。今回は現状維持",
    "thunder_strike": "代案: ★4→★3。雷の入口が上位の CHAIN LIGHTNING(★3/段階2)より入手しにくい逆転。COMBO THUNDER CHAIN の構成。今回は現状維持",
    "wind_cutter": "代案: ★4→★3。風の入口が上位の GALE(★3/段階2)より入手しにくい逆転。COMBO GALE EDGE の構成。今回は現状維持",
}

# 参考シナリオ
SCENARIOS = {
    "現状": {},
    "提案": {k: {kk: vv for kk, vv in v.items() if kk != "reason"} for k, v in PROPOSAL.items()},
    "参考: BOSS KILLER を上げる場合(★4 + 段階3/20,000m)": {"boss_killer": {"rarity": 4, "gachaStage": 3, "unlockDistance": 20000.0}},
    "悪い例: BOSS KILLER を★4にして段階2のまま": {"boss_killer": {"rarity": 4}},
    "案B: 提案 + 個別検討(TREASURE HUNTER★1・属性の入口4枚★3)": dict(
        {k: {kk: vv for kk, vv in v.items() if kk != "reason"} for k, v in PROPOSAL.items()},
        treasure_hunter={"rarity": 1}, flame_blade={"rarity": 3}, frost_edge={"rarity": 3}, thunder_strike={"rarity": 3}, wind_cutter={"rarity": 3}),
}


def apply(cards, scen):
    out = []
    for c in cards:
        d = copy.copy(c)
        for k, v in scen.get(c["cardId"], {}).items():
            d[k] = v
        out.append(d)
    return out


# ================================================================== ガチャ(GameManager.DrawFromGachaPool の再現)
def eligible(card, best, stage):
    return best >= card["unlockDistance"] and stage >= card["gachaStage"]  # GachaStage.IsCardEligible


def stage_for(best, th):
    s = 1
    for i in range(1, len(th)):
        if best >= th[i]:
            s = i + 1
    return s


def gacha_probs(cards, best, weights, th):
    stage = stage_for(best, th)
    pool = [c for c in cards if eligible(c, best, stage)]
    buckets = {}
    for c in pool:
        r = min(max(c["rarity"], 1), len(weights))
        buckets.setdefault(r, []).append(c)
    tot = sum(max(0.0, weights[r - 1]) for r in buckets)
    p, pr = {}, {}
    for r, lst in buckets.items():
        pb = max(0.0, weights[r - 1]) / tot if tot > 0 else len(lst) / len(pool)
        pr[r] = (pb, len(lst))
        for c in lst:
            p[c["cardId"]] = pb / len(lst)
    return p, pr, stage, pool


def p_own_after(p, n):
    return 1.0 - (1.0 - p) ** n


def p_both_after(pa, pb, n):
    # 多項分布: 少なくとも1枚ずつ = 1 - P(Aが0) - P(Bが0) + P(両方0)
    return 1.0 - (1 - pa) ** n - (1 - pb) ** n + max(0.0, 1 - pa - pb) ** n


def pulls_for_prob(p, q=0.5):
    return math.log(1 - q) / math.log(1 - p) if p > 0 else float("inf")


# ================================================================== ラン中の3択(RunLevelUpChoice の再現)
def p_offer(n, copies=1, shown=3):
    """候補 n 件(デッキの重複を含む)から重複なしで shown 件。そのカードが1枠以上に出る確率"""
    if n <= 0:
        return 0.0
    shown = min(shown, n)
    return 1.0 - math.comb(n - copies, shown) / math.comb(n, shown)


# ================================================================== 表の中身
def short_effect(c):
    d = c.get("description", "").replace("\n", " ")
    parts = [x for x in re.split(r"。", d) if x.strip()]
    d = parts[0] if parts else d
    if len(d) < 14 and len(parts) > 1:  # 「雷の入口」のように短い時は次の文もつなぐ
        d = d + "。" + parts[1]
    return d if len(d) <= 70 else d[:68] + "…"


SPECIAL_REASON = {
    "speed_up": "移動の基本。初期所持。COMBO SONIC MOMENTUM の『基本側』として★1のまま",
    "shield": "防御の基本で初期所持。COMBO AEGIS COUNTER の『基本側』。★2のまま",
    "combo_plus": "締め+7%/Lv の基本。COMBO FINISHING BLOW の『基本側』。★2のまま",
    "exp_up": "EXP カードの入口。すでに★1・段階1・初期所持なので下げる余地なし。EXP の曲線もそのまま",
    "pathfinder": "距離EXP のみ。★2/段階1(500m)で十分入手しやすい。現状維持",
    "level_break": "EXP+30%/Lv は EXP UP の1.5倍。下げると EXP 特化が序盤から強くなりすぎるので★4のまま",
    "experience_burst": "撃破EXP+80%/Lv と大きい。EXP 特化の要なので★4のまま(下げない)",
    "the_long_road": "EXP+40%/Lv+HP の EXP 系最上位。★5のまま",
    "monster_rush": "EXP の代わりに敵が増える Risk 寄り。★3のまま",
    "greed": "速度+4%/Lv と敵の出現増の強い Risk/Reward。方針どおり下げない",
    "more_enemies": "★1の Risk 入口(敵の出現と少しの撃破MILE)。初期所持。現状維持",
    "tough_enemies": "雑魚HPと引き換えの MILE/EXP。★2で妥当",
    "fast_enemies": "雑魚の速さと引き換えの MILE/EXP。★2で妥当",
    "mob_killer": "雑魚へ+5%/Lv と撃破MILE+10%/Lv の複合。すでに★2/段階1で入手しやすい。現状維持",
    "elite_enemies": "精鋭と MILE+30%/Lv。報酬が大きい Risk。★3のまま",
    "horde": "敵の出現が大きく増える Risk。★3のまま",
    "boss_challenge": "ボスHPと引き換えのボスMILE+40%/Lv。Risk/Reward として★3のまま",
    "hell_mode": "大きな Risk と MILE。★4のまま",
    "boss_rush": "ボスが増える Risk とボスMILE。★4のまま",
    "wanted": "賞金首(Risk)と MILE。★4のまま",
    "pandemonium": "最大の Risk/Reward。★5のまま",
    "one_more_mile": "MILE/ボスMILE+50%/Lv と EXP の万能経済カード。下げると長期の経済が崩れるので★5のまま",
    "exp_converter": "EXP を MILE に変える強い経済カード。★4のまま",
    "phoenix": "倒れた時に1回だけ復活(HPはLvでハート1〜5)。復活するとカードは外れ、取り直しはLv1から。強力だが1回きりで、段階4(50km)・★5は妥当。現状維持",
    "second_wind": "HP30%以下で少し回復、距離のクールダウン(10,000m)つき。★4で妥当",
    "last_chance": "ハート1で短い無敵(再使用に条件)。★3で妥当",
    "ultimate": "ALMIGHTY(旧 #79 ULTIMATE。cardId は ultimate のまま)。攻撃/HP/速度の万能。★5のまま",
    "deaths_contract": "攻撃+12%/Lv と大きな封印。★5のまま",
    "no_turning_back": "速度+5%/Lv の最上位 Risk。★5のまま",
    "hunter": "ボス戦の動き(前進/接近)だけで倍率ではない。★3のまま",
    "giant_slayer": "無条件の攻撃+9%/Lv(攻撃速度-3%/Lv の代償)。ボスにも効くが代償つき。★4のまま",
    "overdrive": "COMBO REDLINE の構成。MOMENTUM を★3にすれば REDLINE は★3+★4になるので、こちらは★4のまま",
    "character_ultimate": "#100 必殺技カード(ガチャのみ・段階3/20km〜)。COMBO の構成に使わない・マルチでは候補に出ない。★5のまま。ただし段階が進むほど★5の箱が増えて1回の確率が 1.00%→0.33%→0.125% に下がる点は別途検討の余地あり",
    "berserker": "攻撃+6%/Lv と封印。COMBO DEATH WISH の構成(★3+★3、揃えやすい)。現状維持",
    "glass_cannon": "攻撃+9%/Lv と大きな封印。COMBO DEATH WISH の構成。現状維持",
    "vampire": "吸収の基本。COMBO BLOOD AEGIS の構成(★3+★3)。現状維持",
    "overheal": "回復量と溢れた回復の Shield 化。COMBO BLOOD AEGIS の構成。現状維持",
    "counter": "Shield で防いだ時の反撃。COMBO AEGIS COUNTER の上位側(★2+★3で揃えやすい)。現状維持",
    "sky_master": "空中で当てるとジャンプを取り戻す。COMBO SKY ASSAULT の上位側。現状維持",
    "aerial_blade": "空中の追い斬り。COMBO SKY ASSAULT の構成。現状維持",
}


def default_reason(c, starter):
    r, cat = c["rarity"], c["category"]
    if starter and r == 1:
        return "初期デッキに入る基本カード(最初から所持)。★1のまま"
    if cat == 6:
        return "大きなリスクと報酬の組み合わせ。入手しにくさも釣り合いの一部。現状維持" if r >= 4 else "リスクと報酬が釣り合っている。現状維持"
    return {1: "基本の入口カード。★1のまま",
            2: "条件付き/補助の基本カード。★2で妥当",
            3: "条件付きの倍率や特殊な動きが付く中位カード。★3で妥当",
            4: "現象を起こす/効果の大きい上位カード。★4で妥当",
            5: "ビルドの中心になる最上位カード。★5で妥当"}[r]


def acquisition_routes(c, starter):
    s = []
    if starter:
        s.append("初期デッキ(新規開始時に Lv1 を1枚所持)")
    s.append(f"ガチャ(段階{c['gachaStage']}〜 / BEST {int(c['unlockDistance']):,}m〜)")
    s.append("所持→デッキ(12枠)/キャラカード(3枠)→ラン中の LEVEL UP・BOSS REWARD・疾走(自動取得/リング3択)")
    if c["cardId"] == "character_ultimate":
        s.append("マルチでは候補に出ない(UltimateArt.Offerable)")
    return " / ".join(s)


# ================================================================== メイン
def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")  # Windows のコンソール(cp932)で文字化けしないように
    except Exception:
        pass
    base = load_cards()
    K = load_game_constants()
    combos, combo_asset = load_combos()
    W, TH = K["weights"], K["thresholds"]
    byid = {c["cardId"]: c for c in base}

    if len(sys.argv) > 1 and sys.argv[1] == "dump":
        for c in base:
            print(c["no"], c["cardId"], c["cardName"], "R", c["rarity"], "cat", c["category"], "st", c["gachaStage"], "ud", c["unlockDistance"], "so", c["sortOrder"], [e["name"] for e in c["effects"]])
        print(K)
        return

    sc = {name: apply(base, s) for name, s in SCENARIOS.items()}
    cur, prop = sc["現状"], sc["提案"]
    propid = {c["cardId"]: c for c in prop}

    # 初期デッキ: DefaultSave.StartingDeck = UnlockedCards(BEST 0m で解放、sortOrder 順)の先頭 DeckCapacity 枚
    starter = [c["cardId"] for c in base if eligible(c, 0.0, 1)][:K["deck"]]

    combo_of = {}
    for cb in combos:
        for x in (cb["a"], cb["b"]):
            combo_of.setdefault(x, []).append(cb["name"])

    out = []
    P = lambda *a: out.append(" ".join(str(x) for x in a))

    # ---------- 1) rarity_table.csv
    with io.open(os.path.join(HERE, "rarity_table.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["No.", "カード名", "内部ID", "現在のレア度", "カテゴリ", "属性", "主な効果(短縮)", "解放(ガチャ段階 / BEST距離)", "入手経路", "COMBO構成",
                    "推奨レア度", "対応", "理由"])
        for c in base:
            cid = c["cardId"]
            isst = cid in starter
            pc = propid[cid]
            if cid in PROPOSAL:
                act = "上げる" if pc["rarity"] > c["rarity"] else "下げる"
                reason = PROPOSAL[cid]["reason"]
                if pc["gachaStage"] != c["gachaStage"]:
                    act += f"(+段階{c['gachaStage']}→{pc['gachaStage']} / {int(c['unlockDistance']):,}m→{int(pc['unlockDistance']):,}m)"
            elif cid in REVIEW:
                act = "個別検討"
                reason = REVIEW[cid]
            else:
                act = "現状維持"
                reason = SPECIAL_REASON.get(cid)
                if not reason:
                    reason = default_reason(c, isst)
                    if cid in combo_of:
                        reason += "(COMBO " + "・".join(combo_of[cid]) + " の構成)"
            w.writerow([c["no"], c["cardName"], cid, c["rarity"], CATEGORY_JA.get(c["category"], c["category"]), ELEMENT_JA.get(c["element"], ""),
                        short_effect(c), f"段階{c['gachaStage']} / {int(c['unlockDistance']):,}m", acquisition_routes(c, isst),
                        " / ".join(combo_of.get(cid, [])), pc["rarity"], act, reason])

    P("== 定数 ==")
    P(f"重み(シーン Main.unity の値): {W} / コード既定値: {K['code_weights']} / 段階のしきい値: {TH} / ガチャ1回 {K['cost']} MILE / デッキ {K['deck']} 枠 / Mastery 必要枚数 {K['mastery_need']}")
    P("初期デッキ: " + ", ".join(f"{byid[x]['cardName']}(★{byid[x]['rarity']})" for x in starter))
    P("== ★×段階の枚数(現状。その段階で初めて入る枚数) ==")
    for r in range(1, 6):
        P(f"  ★{r}: " + "  ".join(f"段階{s}:{sum(1 for c in base if c['rarity'] == r and c['gachaStage'] == s):2d}" for s in range(1, 6)) + f"  計 {sum(1 for c in base if c['rarity'] == r)}")
    late = [c for c in base if c["unlockDistance"] > TH[c["gachaStage"] - 1]]
    P("段階のしきい値より遅い unlockDistance: " + ", ".join(f"{c['cardId']}(段階{c['gachaStage']}, {c['unlockDistance']:.0f}m)" for c in late))

    # ---------- 2) ガチャ: ★ごと
    pts = [(f"段階{i + 1}({int(TH[i]):,}m)", TH[i]) for i in range(len(TH))]
    with io.open(os.path.join(HERE, "sim_gacha_by_rarity.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["シナリオ", "BEST距離", "ガチャ段階", "対象の枚数", "★", "枚数", "★の排出率", "1枚あたり"])
        for name, cl in sc.items():
            P(f"== ガチャ ★ごとの排出率: {name} ==")
            for label, d in pts:
                p, pr, st, pool = gacha_probs(cl, d, W, TH)
                cells = []
                for r in range(1, 6):
                    a = pr.get(r, (0, 0))
                    w.writerow([name, int(d), st, len(pool), r, a[1], f"{a[0]:.4f}", f"{(a[0] / a[1] if a[1] else 0):.5f}"])
                    cells.append(f"★{r} {a[1]:2d}枚 {a[0] * 100:5.1f}%(1枚 {(a[0] / a[1] * 100 if a[1] else 0):.3f}%)")
                P(f"  {label} 対象{len(pool):3d}枚: " + " | ".join(cells))

    # ---------- 3) ガチャ: カードごと
    T0 = [gacha_probs(cur, d, W, TH)[0] for _, d in pts]
    T1 = [gacha_probs(prop, d, W, TH)[0] for _, d in pts]
    awaken = 9 + sum(K["mastery_need"])
    with io.open(os.path.join(HERE, "sim_gacha_by_card.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        hdr = ["No.", "カード名", "内部ID", "現在★", "提案★"]
        for label, _ in pts:
            hdr += [f"{label} 現状 1回の確率", f"{label} 提案 1回の確率"]
        hdr += ["段階5 現状 50%で1枚目までの回数", "段階5 提案 50%で1枚目までの回数", "段階5 現状 Lv9(9枚)までの期待回数", "段階5 提案 Lv9(9枚)までの期待回数",
                f"段階5 現状 AWAKENED({awaken}枚)の期待回数", "段階5 提案 AWAKENED の期待回数"]
        w.writerow(hdr)
        for c in base:
            cid = c["cardId"]
            row = [c["no"], c["cardName"], cid, c["rarity"], propid[cid]["rarity"]]
            for a, b in zip(T0, T1):
                row += [f"{a.get(cid, 0):.5f}", f"{b.get(cid, 0):.5f}"]
            a, b = T0[-1].get(cid, 0), T1[-1].get(cid, 0)
            row += [f"{pulls_for_prob(a):.1f}", f"{pulls_for_prob(b):.1f}", f"{9 / a:.0f}", f"{9 / b:.0f}", f"{awaken / a:.0f}", f"{awaken / b:.0f}"]
            w.writerow(row)

    P("== 変更候補の1回あたりの排出率(現状 → 提案) ==")
    for cid in PROPOSAL:
        s = [f"{label} {a.get(cid, 0) * 100:.3f}%→{b.get(cid, 0) * 100:.3f}%" for (label, _), a, b in zip(pts, T0, T1) if cid in a or cid in b]
        P(f"  {byid[cid]['cardName']} ★{byid[cid]['rarity']}→★{propid[cid]['rarity']}: " + " / ".join(s))
        a, b = T0[-1][cid], T1[-1][cid]
        P(f"     段階5: 50%で1枚目 {pulls_for_prob(a):.0f}回→{pulls_for_prob(b):.0f}回 / Lv9(9枚) {9 / a:.0f}回→{9 / b:.0f}回 = {9 / a * K['cost']:,.0f}→{9 / b * K['cost']:,.0f} MILE / AWAKENED({awaken}枚) {awaken / a:.0f}回→{awaken / b:.0f}回")
    bad = gacha_probs(sc["悪い例: BOSS KILLER を★4にして段階2のまま"], TH[1], W, TH)[0]
    TK = [gacha_probs(sc["参考: BOSS KILLER を上げる場合(★4 + 段階3/20,000m)"], d, W, TH)[0] for _, d in pts]
    P("  [参考] BOSS KILLER を上げる場合(★4+段階3): " + " / ".join(f"{label} {a.get('boss_killer', 0) * 100:.3f}%→{b.get('boss_killer', 0) * 100:.3f}%" for (label, _), a, b in zip(pts, T0, TK)))
    P(f"     段階5: 50%で1枚目 {pulls_for_prob(T0[-1]['boss_killer']):.0f}回→{pulls_for_prob(TK[-1]['boss_killer']):.0f}回 / 100回で所持 {p_own_after(T0[-1]['boss_killer'], 100) * 100:.0f}%→{p_own_after(TK[-1]['boss_killer'], 100) * 100:.0f}%")
    P(f"  [悪い例] ★4にして段階2のまま: 段階2で BOSS KILLER {T0[1]['boss_killer'] * 100:.3f}% → {bad['boss_killer'] * 100:.3f}%")

    P("== 変更しないカードへの巻き添え(1枚あたり、現状 → 提案) ==")
    for (label, d) in pts:
        _, pr0, _, _ = gacha_probs(cur, d, W, TH)
        _, pr1, _, _ = gacha_probs(prop, d, W, TH)
        cells = []
        for r in range(1, 6):
            a, b = pr0.get(r, (0, 0)), pr1.get(r, (0, 0))
            if a[1] != b[1] and a[1] and b[1]:
                cells.append(f"★{r} {a[1]}→{b[1]}枚 {a[0] / a[1] * 100:.3f}%→{b[0] / b[1] * 100:.3f}% ({(b[0] / b[1]) / (a[0] / a[1]) * 100 - 100:+.1f}%)")
            elif a[1] != b[1]:
                cells.append(f"★{r} {a[1]}→{b[1]}枚(箱が{'できる' if b[1] else '消える'})")
        P(f"  {label}: " + (" | ".join(cells) if cells else "変化なし"))

    # ---------- 4) N回引いた後の所持確率
    P("== N回引いた後に1枚以上所持している確率(現状 → 提案) ==")
    for cid in list(PROPOSAL) + ["greed", "phoenix", "character_ultimate", "one_more_mile", "level_break", "attack_up", "giant_slayer", "treasure_hunter"]:
        s = []
        for (label, _), a, b in zip(pts, T0, T1):
            if cid not in a and cid not in b:
                continue
            s.append(f"{label}: 10回 {p_own_after(a.get(cid, 0), 10) * 100:.0f}%→{p_own_after(b.get(cid, 0), 10) * 100:.0f}% / 30回 {p_own_after(a.get(cid, 0), 30) * 100:.0f}%→{p_own_after(b.get(cid, 0), 30) * 100:.0f}% / 100回 {p_own_after(a.get(cid, 0), 100) * 100:.0f}%→{p_own_after(b.get(cid, 0), 100) * 100:.0f}%")
        P(f"  {byid[cid]['cardName']}(★{byid[cid]['rarity']}→★{propid[cid]['rarity']}): " + " | ".join(s))

    # ---------- 5) ラン中の3択(★は無関係)
    P("== ラン中の LEVEL UP / BOSS REWARD の3択に、デッキの1枚が出る確率(★に関係なし) ==")
    for n in (12, 10, 8, 6, 4, 3):
        P(f"  候補{n:2d}件: 1枚 {p_offer(n) * 100:5.1f}% / 同じカード2枚 {p_offer(n, 2) * 100:5.1f}% / FE候補がある LEVEL UP(通常2枠) {p_offer(n, 1, 2) * 100:5.1f}% / 疾走の自動取得(1枚) {100 / n:5.1f}%")
    P("  m回の LEVEL UP で1回以上出る(候補12件・1枚): " + ", ".join(f"{m}回 {(1 - (1 - p_offer(12)) ** m) * 100:.0f}%" for m in (1, 3, 5, 10, 20)))
    P("  COMBO の2枚が両方デッキにある時、m回の LEVEL UP で両方とも1回以上出る(独立近似、候補12件): " + ", ".join(f"{m}回 {((1 - (1 - p_offer(12)) ** m) ** 2) * 100:.0f}%" for m in (3, 5, 10, 20)))

    # ---------- 6) COMBO
    with io.open(os.path.join(HERE, "sim_combo.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["COMBO", "id", "カードA", "A★ 現状→提案", "A 段階/距離", "カードB", "B★ 現状→提案", "B 段階/距離", "揃い始める段階(現状→提案)", "組み合わせの型(現状)",
                    "揃い始めの段階で30回 両方所持 現状", "提案", "段階5で30回 現状", "提案", "段階5で100回 現状", "提案", "段階5で100回 案B",
                    "段階5で両方そろうまでの期待回数 現状", "提案", "案B", "判定"])
        TB = gacha_probs(sc["案B: 提案 + 個別検討(TREASURE HUNTER★1・属性の入口4枚★3)"], TH[-1], W, TH)[0]

        def both(tab, a, b, n):
            # 初期デッキのカード(最初から所持)は確率1として扱う
            if a in starter and b in starter:
                return 1.0
            if a in starter:
                return p_own_after(tab[b], n)
            if b in starter:
                return p_own_after(tab[a], n)
            return p_both_after(tab[a], tab[b], n)

        def exp_both(tab, a, b):
            # 2枚とも1枚以上そろうまでの回数の期待値(厳密: E[max] = 1/pa + 1/pb - 1/(pa+pb))
            if a in starter and b in starter:
                return 0.0
            if a in starter:
                return 1 / tab[b]
            if b in starter:
                return 1 / tab[a]
            pa, pb = tab[a], tab[b]
            return 1 / pa + 1 / pb - 1 / (pa + pb)
        P("== COMBO 15種 ==")
        P("Resources/Cards/ComboTuning.asset: " + ("あり" if combo_asset else "なし → ComboTuning.DefaultCombos() の既定値が使われる"))
        for cb in combos:
            A0, B0 = byid[cb["a"]], byid[cb["b"]]
            A1, B1 = propid[cb["a"]], propid[cb["b"]]

            def start(A, B):
                return max(A["gachaStage"], B["gachaStage"], stage_for(max(A["unlockDistance"], B["unlockDistance"]), TH))
            s0, s1 = start(A0, B0), start(A1, B1)
            d0 = max(TH[s0 - 1], A0["unlockDistance"], B0["unlockDistance"])
            d1 = max(TH[s1 - 1], A1["unlockDistance"], B1["unlockDistance"])
            q0 = gacha_probs(cur, d0, W, TH)[0]
            q1 = gacha_probs(prop, d1, W, TH)[0]
            ra, rb = A0["rarity"], B0["rarity"]
            if min(ra, rb) >= 4:
                kind = "上位+上位(両方★4以上)"
            elif min(ra, rb) <= 2:
                kind = "基本+上位" if max(ra, rb) >= 3 else "基本+基本"
            else:
                kind = "中位+中位" if max(ra, rb) == 3 else "中位+上位"
            a_, b_ = cb["a"], cb["b"]
            v = [both(q0, a_, b_, 30), both(q1, a_, b_, 30), both(T0[-1], a_, b_, 30), both(T1[-1], a_, b_, 30), both(T0[-1], a_, b_, 100), both(T1[-1], a_, b_, 100)]
            st_note = "(" + "・".join(byid[x]["cardName"] for x in (a_, b_) if x in starter) + " は初期所持)" if (a_ in starter or b_ in starter) else ""
            e_cur = exp_both(T0[-1], a_, b_)
            flag = ("要注意: 両方★4以上で揃いにくい" if min(ra, rb) >= 4 else
                    "揃えやすい方(期待300回未満)" if e_cur < 300 else "普通(期待300〜700回)" if e_cur < 700 else "揃いにくい(期待700回以上)") + st_note
            if kind == "基本+上位":
                flag = (flag + " / " if flag else "") + "大事な『基本+上位』"
            vb = both(TB, a_, b_, 100)
            e0, e1, eb = exp_both(T0[-1], a_, b_), exp_both(T1[-1], a_, b_), exp_both(TB, a_, b_)
            w.writerow([cb["name"], cb["id"], A0["cardName"], f"★{ra}→★{A1['rarity']}", f"{A0['gachaStage']}/{int(A0['unlockDistance']):,}m", B0["cardName"], f"★{rb}→★{B1['rarity']}",
                        f"{B0['gachaStage']}/{int(B0['unlockDistance']):,}m", f"{s0}→{s1}", kind] + [f"{x * 100:.1f}%" for x in v] + [f"{vb * 100:.1f}%", f"{e0:.0f}", f"{e1:.0f}", f"{eb:.0f}", flag])
            P(f"  {cb['name']:15s} {A0['cardName']}(★{ra}→{A1['rarity']}) + {B0['cardName']}(★{rb}→{B1['rarity']})  段階{s0}→{s1} [{kind}]  "
              f"揃い始めで30回 {v[0] * 100:.1f}%→{v[1] * 100:.1f}% / 段階5で30回 {v[2] * 100:.1f}%→{v[3] * 100:.1f}% / 100回 {v[4] * 100:.1f}%→{v[5] * 100:.1f}% (案B {vb * 100:.1f}%) / 期待回数 {e0:.0f}→{e1:.0f} (案B {eb:.0f})  {flag}")

    with io.open(os.path.join(HERE, "sim_summary.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    print("\n".join(out))


if __name__ == "__main__":
    main()

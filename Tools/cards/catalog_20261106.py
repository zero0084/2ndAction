# カードの正式な一覧(2026-11-06): CardCatalogExport の TSV → Docs/Cards_2026-11-06/catalog.md / catalog.tsv
#   python -I -X utf8 catalog_20261106.py <TSVのフォルダ(raw)> <出力フォルダ>
# 値はゲームの実装(アセット + CardVariant/CardBehaviorText/CardRules)から書き出した物。ここでは並べるだけ(ガチャの確率は GameManager.DrawFromGachaPool と同じ式)
import csv, io, os, sys
from collections import Counter, OrderedDict

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)

def tsv(name):
    with io.open(os.path.join(SRC, name), encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))

cards = tsv("cards.tsv")
settings = {r["key"]: r["value"] for r in tsv("settings.tsv")}
W = [float(x) for x in settings["gachaRarityWeights"].split(",")]
TH = [float(x) for x in settings["gachaThresholds"].split(",")]
NEW = set(["combo_pay", "hunt_pay", "boss_pay", "road_pay", "survival_pay", "combo_training", "hunt_training", "boss_training", "road_training", "survival_training",
           "danger_bounty", "sky_siege", "crossroads", "high_stakes", "chain_bounty", "iron_marathon", "trophy_hunt", "horizon"])
old = [c for c in cards if c["id"] not in NEW]
new = [c for c in cards if c["id"] in NEW]
assert len(new) == 18, len(new)

KANA = {
 "SPEED UP": "スピードアップ", "SPEED DOWN": "スピードダウン", "ATTACK UP": "アタックアップ", "JUMP POWER UP": "ジャンプパワーアップ", "JUMP COUNT UP": "ジャンプカウントアップ",
 "HEART UP": "ハートアップ", "ATTACK RANGE UP": "アタックレンジアップ", "ATTACK SPEED UP": "アタックスピードアップ", "AIR ATTACK UP": "エアアタックアップ", "SHIELD": "シールド",
 "EXP UP": "EXPアップ", "VAMPIRE": "ヴァンパイア", "BERSERKER": "バーサーカー", "HEAVY ARMOR": "ヘビーアーマー", "GREED": "グリード", "PATHFINDER": "パスファインダー",
 "MORE ENEMIES": "モアエネミーズ", "BRAKE ATTACK": "ブレーキアタック", "FIRST STRIKE": "ファーストストライク", "EXECUTIONER": "エクセキューショナー",
 "TREASURE HUNTER": "トレジャーハンター", "COMBO PLUS": "コンボプラス", "MOB KILLER": "モブキラー", "GROUND FIGHTER": "グラウンドファイター",
 "TOUGH ENEMIES": "タフエネミーズ", "FAST ENEMIES": "ファストエネミーズ", "COMBO EDGE": "コンボエッジ", "PIERCING BLADE": "ピアシングブレード", "SHOCKWAVE": "ショックウェーブ",
 "AERIAL BLADE": "エアリアルブレード", "COUNTER": "カウンター", "LAST STAND": "ラストスタンド", "OVERHEAL": "オーバーヒール", "BLOOD RUSH": "ブラッドラッシュ",
 "PREDATOR": "プレデター", "GLASS CANNON": "グラスキャノン", "IRON WILL": "アイアンウィル", "LONG HAUL": "ロングホール", "BOSS KILLER": "ボスキラー",
 "ADRENALINE": "アドレナリン", "SKY RUNNER": "スカイランナー", "MONSTER RUSH": "モンスターラッシュ", "ELITE ENEMIES": "エリートエネミーズ", "HORDE": "ホード",
 "BOSS CHALLENGE": "ボスチャレンジ", "MOMENTUM": "モメンタム", "SECOND WIND": "セカンドウィンド", "CHAIN EXPLOSION": "チェインエクスプロージョン",
 "THUNDER STRIKE": "サンダーストライク", "LEVEL BREAK": "レベルブレイク", "DOUBLE ATTACK": "ダブルアタック", "SONIC BLADE": "ソニックブレード",
 "GIANT SLAYER": "ジャイアントスレイヤー", "RAPID EDGE": "ラピッドエッジ", "LONG BLADE": "ロングブレード", "SKY MASTER": "スカイマスター", "FORTRESS": "フォートレス",
 "PERFECT GUARD": "パーフェクトガード", "EXPERIENCE BURST": "エクスペリエンスバースト", "BLOOD BLADE": "ブラッドブレード", "BERSERK DRIVE": "バーサクドライブ",
 "HEAVY IMPACT": "ヘビーインパクト", "AIR DOMINION": "エアドミニオン", "COMBO MASTER": "コンボマスター", "HIGH VOLTAGE": "ハイボルテージ", "OVERDRIVE": "オーバードライブ",
 "REVERSE GEAR": "リバースギア", "HEART BREAKER": "ハートブレイカー", "EXP CONVERTER": "EXPコンバーター", "GROUND ZERO": "グラウンドゼロ", "SKYBOUND": "スカイバウンド",
 "FLAME BLADE": "フレイムブレード", "FROST EDGE": "フロストエッジ", "WIND CUTTER": "ウィンドカッター", "HELL MODE": "ヘルモード", "BOSS RUSH": "ボスラッシュ",
 "WANTED": "ウォンテッド", "PHOENIX": "フェニックス", "ALMIGHTY": "オールマイティ", "ONE MORE MILE": "ワンモアマイル", "DEATH'S CONTRACT": "デスコントラクト",
 "NO TURNING BACK": "ノーターニングバック", "THE LONG ROAD": "ザ・ロングロード", "PANDEMONIUM": "パンデモニウム", "GROUND BREAKER": "グラウンドブレイカー",
 "FLAME COUNTER": "フレイムカウンター", "COMBO RUSH": "コンボラッシュ", "AIR STRIKE": "エアストライク", "LAST CHANCE": "ラストチャンス", "CLOSE CALL": "クロースコール",
 "HUNTER": "ハンター", "BURNING SOUL": "バーニングソウル", "INFERNO": "インフェルノ", "ICE PRISON": "アイスプリズン", "ABSOLUTE ZERO": "アブソリュートゼロ",
 "CHAIN LIGHTNING": "チェインライトニング", "THUNDER LORD": "サンダーロード", "GALE": "ゲイル", "TORNADO": "トルネード", "ULTIMATE": "アルティメット",
 "SCORCH": "スコーチ", "FIRE WHIRL": "ファイアホワール", "COMBUSTION": "コンバスチョン", "PERMAFROST": "パーマフロスト", "GLACIER GUARD": "グレイシャーガード",
 "SHATTER STRIKE": "シャッターストライク", "CONDUCTOR": "コンダクター", "VOLT SURGE": "ボルトサージ", "JUDGMENT BOLT": "ジャッジメントボルト",
 "SLIPSTREAM": "スリップストリーム", "UPDRAFT": "アップドラフト", "TEMPEST": "テンペスト",
}
CAT = OrderedDict([("Attack", "ATTACK"), ("Defense", "DEFENSE"), ("Movement", "MOVEMENT"), ("Growth", "GROWTH"), ("Heal", "HEAL"), ("Special", "SPECIAL"), ("Risk", "RISK")])
EL = {"Fire": "炎", "Ice": "氷", "Thunder": "雷", "Wind": "風", "Blood": "血", "None": "-"}
MAP = OrderedDict([("Common", "共通"), ("Wilderness", "荒野街道"), ("Cave", "自然洞窟"), ("Sky", "天空回廊"), ("LastDungeon", "ラストダンジョン")])
MAPSTAGE = {"Wilderness": "荒野街道", "Cave": "自然洞窟", "Sky": "天空回廊"}

def janame(c): return c["nameJa"] if c["nameJa"] else KANA.get(c["name"], "")
def unlock_old(c):
    d = float(c["unlockDistance"]); s = int(c["gachaStage"])
    if d <= 0 and s <= 1: return "最初から(ガチャ段階1)"
    return f"BEST {max(d, TH[s - 1]):,.0f}m(ガチャ段階{s})"
def unlock_new(c):
    k = c["mapCategory"]
    if k == "Common": return unlock_old(c)
    if k == "LastDungeon": return unlock_old(c) + " + ラストダンジョンの解放"
    m = float(c["mapMeters"])
    base = "" if (float(c["unlockDistance"]) <= 0 and int(c["gachaStage"]) <= 1) else unlock_old(c) + " + "
    if m <= 0: return base + f"{MAPSTAGE[k]}(最初のマップ。今までと同じ)" if base else f"最初から({MAPSTAGE[k]})"
    return base + f"{MAPSTAGE[k]}で{m:,.0f}m到達して帰還"
def special(c):
    s = []
    cid = c["id"]
    if cid == "character_ultimate": s.append("ULTIMATE: Gauge 100%で必殺技(左下のボタン)。キャラ固有。FINAL EVOLUTION対象外")
    if cid == "ultimate": s.append("cardId は ultimate のまま表示名 ALMIGHTY")
    if cid == "phoenix": s.append("復活すると外れ、取り直しは Lv1 から")
    if int(c["sacrificeLv9"] or 0) > 0: s.append(f"HP犠牲: ハート封印 Lv1 {c['sacrificeLv1']} / Lv9 {c['sacrificeLv9']}(払えない時は候補に出ない)")
    if "ShieldGuard" in c["effects"]: s.append("単独でも Shield 1")
    if c["behaviorLevel"] == "1": s.append("挙動のカード(Lv が強さ・数値欄なし)")
    if int(c["recommendPriority"]) <= 2: s.append(f"おすすめ編成で後回し(優先度{c['recommendPriority']})")
    if cid in ("danger_bounty", "crossroads"): s.append("敵の変化はソロの通常マップだけ(マルチでは報酬だけ)")
    if cid == "sky_siege": s.append("空中雑魚の置き換えは Encounter のあるマップ(HOST が決める)")
    return " / ".join(s)
def pool_note(c): return "ガチャ: 解禁後(レア度の重み) / ラン中: デッキに入れた時(レベルアップ・ボス報酬の3択)"

# ---------------------------------------------------------------- TSV
cols = ["no", "id", "日本語名", "英語名", "レア度", "属性", "カテゴリ", "マップ分類", "分類の理由", "効果(説明)", "Lv1", "Lv9", "Lv1(英)", "Lv9(英)", "COMBO対象", "FINAL EVOLUTION対象",
        "特殊ルール", "解禁条件(これまで)", "解禁条件(2026-11-06 以降)", "初期デッキ", "ガチャ・ラン中", "sortOrder", "新規"]
with io.open(os.path.join(OUT, "catalog.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\t".join(cols) + "\n")
    for i, c in enumerate(cards, 1):
        row = [str(i), c["id"], janame(c), c["name"], "★" * int(c["rarity"]), EL[c["element"]], CAT[c["category"]], MAP[c["mapCategory"]], c["mapReason"], c["description"],
               c["lv1"], c["lv9"], c["lv1En"], c["lv9En"], c["combos"] or "-", (c["feTitle"] + "(" + c["feDuration"] + ")") if c["feTitle"] else "-", special(c),
               unlock_old(c), unlock_new(c), "○" if c["starter"] == "1" else "", pool_note(c), c["sortOrder"], "新" if c["id"] in NEW else ""]
        f.write("\t".join(x.replace("\t", " ") for x in row) + "\n")

# ---------------------------------------------------------------- 集計
def dist(rows, key):
    t = OrderedDict()
    for r in rows:
        t.setdefault(key(r), Counter())[int(r["rarity"])] += 1
    return t
def dist_table(title, rows, key, order):
    d = dist(rows, key)
    out = [f"| {title} | ★1 | ★2 | ★3 | ★4 | ★5 | 計 |", "|---|---|---|---|---|---|---|"]
    tot = Counter()
    for k in order:
        if k not in d: continue
        cnt = d[k]; tot.update(cnt)
        out.append(f"| {k} | " + " | ".join(str(cnt.get(r, 0)) for r in range(1, 6)) + f" | {sum(cnt.values())} |")
    out.append("| **計** | " + " | ".join(str(tot.get(r, 0)) for r in range(1, 6)) + f" | **{sum(tot.values())}** |")
    return "\n".join(out)

def gacha(rows, eligible):
    pool = [c for c in rows if eligible(c)]
    b = Counter(int(c["rarity"]) for c in pool)
    tw = sum(W[r - 1] for r in b)
    return pool, b, tw

starter = [c for c in cards if c["starter"] == "1"]
md = []
A = md.append
A("# One More Mile カードの正式な一覧(2026-11-06)")
A("")
A("実装(Resources/Cards のアセット + ゲームと同じ式)から書き出した値です(`Tools/cards/catalog_20261106.py`、元の TSV は `raw/`)。全項目は `catalog.tsv`。")
A(f"**現行 {len(old)} 枚 + 新規 {len(new)} 枚 = {len(cards)} 枚。** 既存のカードのレア度・性能・初期デッキは変えていません(新規 18 枚と、マップ分類による解禁の条件だけ追加)。")
A("")
A("- 日本語名: ゲームの日本語表示は今も英語名です。既存のカードの日本語名はカタカナの読み(資料用)、新規 18 枚は新しく付けた日本語名(マスターの確認待ち。ゲームには出していません)。")
A("- Lv1 / Lv9: カード詳細と同じ効果の行(1ランで能力ごとに最大 Lv9)。威力は攻撃力の強化が無い時の値。")
A("- COMBO 対象: ComboTuning の組み合わせに入っているか(組み合わせの名前)。FINAL EVOLUTION 対象: FinalEvolutionTuning に項目があるか(題と時間)。新規 18 枚はどちらも対象外(後で足せます)。")
A("- ガチャ: 解禁済みのカードがレア度の重み(★1〜★5 = " + " / ".join(f"{w:g}" for w in W) + ")で抽選。ラン中の抽選(レベルアップ/ボス報酬の3択)はデッキ 12 枚から。")
A("")
A("## 1. レア度の分布")
A("")
A("### 現行 112 枚(カテゴリ別)")
A("")
A(dist_table("カテゴリ", old, lambda c: CAT[c["category"]], list(CAT.values())))
A("")
A("### 新規 18 枚を加えた後(カテゴリ別)")
A("")
A(dist_table("カテゴリ", cards, lambda c: CAT[c["category"]], list(CAT.values())))
A("")
A("### 属性別(全 130 枚)")
A("")
A(dist_table("属性", cards, lambda c: EL[c["element"]], ["炎", "氷", "雷", "風", "血", "-"]))
A("")
A("## 2. マップ分類(初期分類。マスターの確認で移動します)")
A("")
A("分類は「そのマップで解禁する場所」。一度解禁したカードは、どのマップでも通常どおり使え、ガチャでもレア度の重みのまま引けます(マップ専用にはしない・隠れて確率を下げない)。表は `Assets/Scripts/Cards/CardMapCategoryTable.cs`(1 行 = 1 枚、理由つき)。")
A("")
A(dist_table("分類", cards, lambda c: MAP[c["mapCategory"]], list(MAP.values())))
A("")
cnt_old = Counter(MAP[c["mapCategory"]] for c in old)
A("現行 112 枚だけ: " + " / ".join(f"{k} {cnt_old.get(k, 0)}" for k in MAP.values()) + "(新規 18 枚はすべて共通)。")
A("")
A("各マップで候補になるカード(共通 + そのマップで解禁 + 他のマップで解禁済み)は、共通 " + str(sum(1 for c in cards if c["mapCategory"] == "Common")) +
  " 枚 + 各マップ 25〜33 枚。荒野だけを遊ぶ人は共通 + 荒野 = " + str(sum(1 for c in cards if c["mapCategory"] in ("Common", "Wilderness"))) + " 枚。")
A("")
A("## 3. マップ分類による解禁(実装した決まり)")
A("")
A("| 分類 | 解禁の条件(今までの BEST/ガチャ段階の条件に加えて) |")
A("|---|---|")
A("| 共通 | なし(今までどおり: BEST の距離 ≥ 解放距離 かつ ガチャ段階) |")
A("| 荒野街道 | 荒野街道で、成功したラン(帰還)の到達距離 ≥ そのカードの解放距離(解放距離 0m のカードは最初から = 今までと同じ) |")
A("| 自然洞窟 | 自然洞窟で、成功したランの到達距離 ≥ max(解放距離, 1,000m = 最初のボスの関門) |")
A("| 天空回廊 | 天空回廊で、成功したランの到達距離 ≥ max(解放距離, 1,000m) |")
A("| ラストダンジョン | ラストダンジョンが解放されていること(通常3マップすべてで死神 + 累計 1,000,000m) |")
A("")
A("- 到達距離はマップ別の「1回のランで到達した最高距離」(`UnlockRules.Reach`。キャラ/マップの解放と同じく、帰還した時だけ確定。既存データはマップ別 BEST から作ってある)。")
A("- 既存のデータ(遊んだ形跡がある): この版で初めて起動した時に一度だけ、**今までの条件で使えていたマップのカード + 所持しているカードを解禁済みのまま**にします(`CardMapGrandfatherV1`、SaveKeys に登録)。取り上げません。")
A("- 新しいデータ: 最初から分類どおり。初期デッキ(下の 12 枚)は今までの条件で作るので変わりません。初期デッキのカードは所持しているので使えますが、マップのカード(HEART UP/SHIELD/VAMPIRE/AIR ATTACK UP)をガチャで重ねて引けるのは、そのマップで解禁してから。")
A("- 解禁の判定は `GachaStage.IsCardEligible` の1か所(ガチャの候補・解放済みカードの一覧・おすすめ編成が同じ判定)。画面用に `CardMapUnlock.IsUnlocked(card)` / `ConditionSrc(card)`(条件の文、30言語の鍵)/ `RequiredMeters(card)` を用意しました。")
A("- 開発版の「全カード開放」はそのまま全部開きます。")
A("")
p_old, b_old, tw_old = gacha(old, lambda c: float(c["unlockDistance"]) <= 0 and int(c["gachaStage"]) <= 1)
p_new, b_new, tw_new = gacha(cards, lambda c: float(c["unlockDistance"]) <= 0 and int(c["gachaStage"]) <= 1 and (c["mapCategory"] == "Common" or (c["mapCategory"] == "Wilderness" and float(c["mapMeters"]) <= 0)))
A("**新規プレイヤーの最初のガチャ(BEST 0m)の候補**")
A("")
A("| | 枚数 | ★1 | ★2 | 1枚あたり ★1 / ★2 |")
A("|---|---|---|---|---|")
A(f"| これまで(112 枚) | {len(p_old)} | {b_old[1]} | {b_old[2]} | {W[0] / tw_old / max(1, b_old[1]):.1%} / {W[1] / tw_old / max(1, b_old[2]):.1%} |")
A(f"| 今回の後(新規 18 枚 + マップ分類) | {len(p_new)} | {b_new[1]} | {b_new[2]} | {W[0] / tw_new / max(1, b_new[1]):.1%} / {W[1] / tw_new / max(1, b_new[2]):.1%} |")
A("")
gone = sorted(set(c["name"] for c in p_old) - set(c["name"] for c in p_new))
came = sorted(set(c["name"] for c in p_new) - set(c["name"] for c in p_old))
A("- 最初の候補から外れた(洞窟/天空で解禁): " + ", ".join(gone))
A("- 最初の候補に加わった(新規の共通 ★1): " + ", ".join(came))
A("")
A("## 4. 現在の初期デッキ(新規データ)")
A("")
A("| # | カード | レア度 | 分類 | Lv1 | Lv9 |")
A("|---|---|---|---|---|---|")
for i, c in enumerate(starter, 1):
    A(f"| {i} | {c['name']} `{c['id']}` | {'★' * int(c['rarity'])} | {MAP[c['mapCategory']]} | {c['lv1']} | {c['lv9']} |")
A("")
A("初期所持はこの 12 枚を各 1 枚 Lv1(`DefaultSave.StartingDeck`: 解放距離 0m・ガチャ段階1 のカードを sortOrder の順に 12 枚)。ラン中の3択はデッキの 12 枚から出るので、")
A("この 12 枚は最初のランから Lv9 まで育ちます。")
A("")
A("### HP / SHIELD / VAMPIRE / 防御・回復が最初から揃いすぎるか(報告)")
A("")
hp = [c for c in cards if "MaxHpHearts" in c["effects"]]
A("**揃います。** 12 枚のうち 3 枚(HEART UP / SHIELD / VAMPIRE)がまさに該当し、新規プレイヤーの最初のランから全部 Lv9 まで取れます。")
A("")
A("- HEART UP(★1): Lv9 で最大HP ハート+9(取得で最大HPが増えると全回復)。**ゲームで一番大きい HP の伸び**で、★4 の FORTRESS(+9、移動速度-27% の代償つき)と同じ量を ★1・代償なしで持っています。")
A("- SHIELD(★2): Lv9 で Shield 3 つ・回復の短縮 13.5 秒(24 秒 → 約 10.5 秒ごとに 1 つ)。")
A("- VAMPIRE(★2): Lv9 で命中の吸収 5%・撃破でハート1つ回復 40%。")
A("- このほか SPEED DOWN(デメリットだけのカード)も初期デッキに入っています(おすすめ編成では後回し)。")
A("")
A("最大HPが増えるカード(Lv9 の量):")
A("")
A("| カード | レア度 | 分類 | Lv9 | 代償 |")
A("|---|---|---|---|---|")
for c in sorted(hp, key=lambda c: (int(c["rarity"]), c["name"])):
    A(f"| {c['name']} | {'★' * int(c['rarity'])} | {MAP[c['mapCategory']]} | {c['lv9']} | {'あり' if int(c['recommendPriority']) <= 2 else '-'} |")
A("")
A("### 提案(実装はしていません。マスターの判断待ち)")
A("")
A("1. **初期デッキから HEART UP / SHIELD / VAMPIRE を外し、新規の共通 ★1 を入れる**(例: ROAD PAY / HUNT TRAINING / COMBO PAY)。外した 3 枚は分類どおり自然洞窟で解禁されるので、")
A("   「洞窟で守りを覚える」流れになります。SPEED DOWN も SURVIVAL PAY などへ替える候補。やり方は `DefaultSave.StartingDeck` を固定の 12 枚の一覧にするのが確実(sortOrder に頼らない)。既存のデータのデッキ/所持は変えない。")
A("2. **HP カードのレア度**: HEART UP ★1 → ★3(5km)を推奨(★4 FORTRESS と同じ伸びを代償なしで持つため)。または ★1 のまま「2Lvごとにハート+1(Lv9 で +5)」に。")
A("   HEAVY ARMOR(★3、+5 + 重い一撃の軽減)は ★4 候補。IRON WILL / LONG HAUL(★3、+3)はそのままでよい。THE LONG ROAD / ALMIGHTY(★5、+5)もそのまま。")
A("3. SHIELD ★2 / VAMPIRE ★2 はレア度は据え置きで、初期デッキから外す(1.)だけで「最初から揃う」状態はなくなります。")
A("4. 新規の共通 ★1 を初期デッキへ入れるのは、効果が分かりやすく危険がないので向いています(報酬だけのカードなので、序盤の強さは変わらない)。")
A("")
A("## 5. 新規カード 18 枚")
A("")
A("| ID | 英語名 / 日本語名 | レア度 | 効果 | Lv1 | Lv9 | 解禁 |")
A("|---|---|---|---|---|---|---|")
for c in new:
    A(f"| `{c['id']}` | **{c['name']}** / {c['nameJa']} | {'★' * int(c['rarity'])} | {c['description']} | {c['lv1']} | {c['lv9']} | {unlock_old(c)} |")
A("")
A("- MILE の固定の報酬は BONUS の MILE と同じ経路(協力マルチ/CPU サポートは人数で分配。MILE の倍率のカードは掛けない)。EXP の固定の報酬は「EXP の量」(EXP の倍率のカードは掛けない)。")
A("- BOSS PAY / BOSS TRAINING は倍率(ボス撃破 MILE / ボス撃破 EXP。EXP は撃破の EXP の枠に足して、今までの曲線を通す)。")
A("- 疾走出発/CONTINUE/ワープで距離が飛んだ区間は払わない(その位置から数え直す)。数え途中の端数は中断データに入れない。")
A("- 敵 HP だけを上げるリスクは使っていません(高火力敵・空中雑魚・判断イベント・ボスの火力)。★5 は今回は作っていません(ルールを変える級の必要が出た時に NO TURNING BACK / ENDLESS HUNT 系を検討)。")
A("")
A("## 6. 全カードの一覧")
A("")
A("| No | ID | 日本語名 | 英語名 | レア度 | 属性 | カテゴリ | 分類 | 分類の理由 | Lv1 | Lv9 | COMBO | FE | 特殊ルール | 解禁(これまで) | 解禁(今回の後) | 初期 |")
A("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
for i, c in enumerate(cards, 1):
    A(f"| {i} | `{c['id']}` | {janame(c)} | {c['name']} | {'★' * int(c['rarity'])} | {EL[c['element']]} | {CAT[c['category']]} | {MAP[c['mapCategory']]} | {c['mapReason']} | {c['lv1']} | {c['lv9']} | "
      f"{c['combos'] or '-'} | {c['feTitle'] or '-'} | {special(c) or '-'} | {unlock_old(c)} | {unlock_new(c)} | {'○' if c['starter'] == '1' else ''} |")
A("")
A("効果の説明(全文)は `catalog.tsv` の「効果(説明)」列。")
with io.open(os.path.join(OUT, "catalog.md"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(md) + "\n")
print(f"cards {len(cards)} (old {len(old)} + new {len(new)}) -> {OUT}")

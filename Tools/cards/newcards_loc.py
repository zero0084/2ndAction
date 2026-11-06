# 共通カードの追加(2026-11-06、18 枚)の訳の準備。
#   python -I newcards_loc.py
# ・日本語の元の文は Unity/2ndAction/Assets/Scripts/Localization/LocCardCommon.cs から読む(文を二重に持たない)
# ・英語は下の EN(エステルのレビュー前の下書き)。en.json へ足す/置き換える(日本語は鍵がそのまま日本語)
# ・書き出し: Docs/Cards_2026-11-06/loc/review_jp_en.tsv(エステルの日本語/英語のレビュー用)
#             Tools/loc/keys_extra39.tsv(28 言語の訳の入力。TRANSLATE.md の形式。レビュー後に out/<code>.39.tsv → merge_add.py 39 keys_extra39.tsv)
import io, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Scripts", "Localization", "LocCardCommon.cs")
LOC = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Resources", "Localization")
OUTDIR = os.path.join(ROOT, "Docs", "Cards_2026-11-06", "loc")
KEYS = os.path.join(ROOT, "Tools", "loc", "keys_extra39.tsv")

EN = {
    # id: (desc, behavior)
    "combo_pay": ("Each time your hit streak (the \"N HIT\" counter) reaches a multiple of 10, gain +1 MILE (+1 per Lv). Keeps paying at 20, 30 and so on (up to 100 HIT per streak)", "+{0} MILE every 10 HIT"),
    "hunt_pay": ("Gain +1 MILE (+1 per Lv) for every 10 enemies defeated. Bosses don't count", "+{0} MILE per 10 enemies defeated"),
    "boss_pay": ("Boss defeat MILE +12% per Lv", ""),
    "road_pay": ("Gain +2 MILE (+2 per Lv) for every 1km you run. Sections skipped by Sprint Start or CONTINUE don't count", "+{0} MILE every 1km"),
    "survival_pay": ("Gain +1 MILE (+1 per Lv) for every 30 seconds you survive in a run. Card choices and pauses don't count", "+{0} MILE every 30s"),
    "combo_training": ("Each time your hit streak reaches a multiple of 10, gain EXP +20 (+20 per Lv). Up to 100 HIT per streak", "EXP +{0} every 10 HIT"),
    "hunt_training": ("Gain EXP +30 (+30 per Lv) for every 10 enemies defeated. Bosses don't count", "EXP +{0} per 10 enemies defeated"),
    "boss_training": ("Boss defeat EXP +15% per Lv", ""),
    "road_training": ("Gain EXP +100 (+100 per Lv) for every 1km you run. Sections skipped by Sprint Start or CONTINUE don't count", "EXP +{0} every 1km"),
    "survival_training": ("Gain EXP +30 (+30 per Lv) for every 30 seconds you survive in a run. Card choices and pauses don't count", "EXP +{0} every 30s"),
    "danger_bounty": ("Some enemies become Heavy Hitters (magenta aura, +1 heart damage) (+3% per Lv, up to 30%, from 0km). In return: enemy/BONUS MILE +15% per Lv, enemy defeat EXP +10% per Lv. Enemies only change in solo runs on normal maps", "Chance enemies become Heavy Hitters +{0}%"),
    "sky_siege": ("Air enemies (divers, crossers, etc.) fill air slots more often (+5% per Lv), and groups with flying enemies appear more often (+10% per Lv). In return: enemy defeat EXP +15% per Lv, enemy/BONUS MILE +8% per Lv", "Air enemies +{0}% / groups with flyers ×{1}"),
    "crossroads": ("Road decision events (lane warnings, blockades, Heavy Hitters, etc.) start earlier (normally 50km; 4km earlier per Lv, 10km at the earliest) and come more often (gap -5% per Lv, up to -50%). In return: enemy/BONUS MILE +12% per Lv, EXP gained +8% per Lv. Decision events only happen in solo runs on normal maps", "Decision events from {0}km / gap ×{1}"),
    "high_stakes": ("Damage taken from bosses +10% per Lv (in hearts; a fraction may add 1 more heart). In return: boss defeat MILE +35% per Lv, boss defeat EXP +15% per Lv", "Damage taken from bosses +{0}%"),
    "chain_bounty": ("Enemies defeated while your hit streak is at 10 HIT or more give more MILE and defeat EXP (+15% per Lv). Keep the streak going while you defeat enemies", "Defeats during a 10+ HIT streak: MILE/EXP +{0}%"),
    "iron_marathon": ("Every 1km you run without taking damage gives +3 MILE and EXP +150 (more per Lv). Taking damage resets the count (blocking with a Shield doesn't)", "Every 1km without damage: +{0} MILE, EXP +{1}"),
    "trophy_hunt": ("Defeating a boss gives +0.2 MILE and EXP +3 for each enemy defeated since the previous boss (more per Lv, up to 100 enemies). Clear out enemies before you take on the boss", "On boss defeat, per enemy: +{0} MILE, EXP +{1} (up to 100)"),
    "horizon": ("Every 10km you run, enemy/boss/BONUS MILE and EXP gained rise by +2% per Lv (max at 100km; +20% at Lv1). The farther you run, the more it grows", "MILE/EXP +{0}% per 10km (max +{1}% at 100km)"),
}
EXTRA = [
    ("10554", "LabelBossExp", "カード詳細の効果の行の名前(BOSS TRAINING / HIGH STAKES)。後ろに +15% などが付く", "Boss defeat EXP"),
    ("10555", "UnlockOnMap", "カード編集: マップ分類のカードの解禁の条件。{0}=マップ名(荒野街道/自然洞窟/天空回廊){1}=距離(3桁区切り)", "Reach {1}m on {0} and return"),
    ("10556", "UnlockLastDungeon", "カード編集: ラストダンジョンの分類のカードの解禁の条件", "Unlock the Last Dungeon"),
]

src = io.open(SRC, encoding="utf-8").read()
cards = re.findall(r'new Card\("([a-z_]+)", "([^"]+)", "([^"]+)",\s*"([^"]*)",\s*"([^"]*)"\)', src)
assert len(cards) == 18, len(cards)
consts = dict(re.findall(r'public const string (\w+) = "([^"]*)";', src))

rows = []  # (id, kind, note, ja, en)
for i, (cid, name, nja, desc, beh) in enumerate(cards):
    rows.append((str(10500 + i), "card", f"カード名 {cid}(英字のカード名。各言語の既存のカード名と同じ扱い。日本語名の参考: {nja})", name, name))
for i, (cid, name, nja, desc, beh) in enumerate(cards):
    rows.append((str(10518 + i), "card", f"{name} の説明(カード詳細・3択)", desc, EN[cid][0]))
for i, (cid, name, nja, desc, beh) in enumerate(cards):
    if not beh: continue
    assert EN[cid][1], cid
    rows.append((str(10536 + i), "card", f"{name} の効果の行(カード詳細の Lv1/Lv9。{{n}} に数値)", beh, EN[cid][1]))
for i, const, note, en in EXTRA:
    rows.append((i, "ui", note, consts[const], en))

PH = re.compile(r"\{\d+\}")
for r in rows:
    assert sorted(PH.findall(r[3])) == sorted(PH.findall(r[4])), r

os.makedirs(OUTDIR, exist_ok=True)
with io.open(os.path.join(OUTDIR, "review_jp_en.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("id\tcategory\tnote\tja\ten\n")
    for r in rows: f.write("\t".join((r[0], r[1], r[2], r[3], r[4])) + "\n")
with io.open(KEYS, "w", encoding="utf-8", newline="\n") as f:
    for r in rows: f.write("\t".join((r[0], r[1], r[2], r[3])) + "\n")

# en.json へ(英語の表に足す/置き換える)
path = os.path.join(LOC, "en.json")
t = json.load(io.open(path, encoding="utf-8"))
idx = {k: n for n, k in enumerate(t["k"])}
added = replaced = 0
for r in rows:
    k, v = r[3], r[4]
    if k in idx:
        if t["v"][idx[k]] != v: t["v"][idx[k]] = v; replaced += 1
    else:
        idx[k] = len(t["k"]); t["k"].append(k); t["v"].append(v); added += 1
with io.open(path, "w", encoding="utf-8") as f:
    json.dump(t, f, ensure_ascii=False)
print(f"rows {len(rows)} -> review_jp_en.tsv / keys_extra39.tsv; en.json +{added} ~{replaced}")

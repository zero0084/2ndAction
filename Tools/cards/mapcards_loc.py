# マップ専用カード 20 枚(2026-11-06、Docs/Cards_2026-11-06/map_cards_v2.md)の訳の準備。
#   python -I mapcards_loc.py
# ・日本語の元の文は Unity/2ndAction/Assets/Scripts/Localization/LocCardMap.cs から読む(文を二重に持たない)
# ・英語は下の EN(エステルのレビュー前の下書き)。en.json へ足す/置き換える(日本語は鍵がそのまま日本語)。名前は英字のまま全言語の表へ
# ・書き出し: Docs/Cards_2026-11-06/loc/review_map_jp_en.tsv(名前を含む日本語/英語の一覧)
#             Tools/loc/est40/src40_for_estelle.txt / src40.tsv / src.js(エステルへ渡す。1行 = ID<TAB>JA: …<TAB>EN: …)
#             Tools/loc/keys_extra40.tsv(28 言語の訳の入力。レビュー後に out/<code>.40.tsv → merge_add.py 40 keys_extra40.tsv)
#   ID: 名前 10557〜10576(英字のまま。訳の対象外)/ 説明 10577〜10596 / 効果の行 10597〜10616(LocCardMap.PendingList と同じ)
import glob, io, json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Scripts", "Localization", "LocCardMap.cs")
LOC = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Resources", "Localization")
DOCDIR = os.path.join(ROOT, "Docs", "Cards_2026-11-06", "loc")
ESTDIR = os.path.join(ROOT, "Tools", "loc", "est40")
KEYS = os.path.join(ROOT, "Tools", "loc", "keys_extra40.tsv")

EN = {
    # id: (desc, behavior)
    "last_bastion": ("The moment your HP drops to 30% or below, gain temporary shields that vanish after 8s (1 at Lv1, 2 at Lv5, 3 at Lv9). Recharges in 90s (60s at Lv9). Temporary shields are counted separately from normal Shields, up to 3 in total",
                     "At 30% HP or below: {0} temporary shield(s) ({1}s) / recharge {2}s"),
    "overflow_ward": ("Turns 30% (70% at Lv9) of healing beyond your max HP into temporary shields (1 per heart's worth; they vanish after 20s). Up to 1 (2 at Lv5, 3 at Lv9); converts once every 3s",
                      "{0}% of overflow healing becomes temporary shields (max {1}, lasts {2}s)"),
    "revenge_armor": ("For 3s (5s at Lv9) after a hit of 2 hearts or more, damage taken is reduced by 1 heart (minimum 1) and attack +20% (+40% at Lv9). Recharges in 8s. 1-heart contact hits don't trigger it",
                      "For {0}s after a 2+ heart hit: damage taken -1 heart, attack +{1}%"),
    "break_feast": ("Restores HP when you BREAK a boss (1 heart at Lv1, 2 at Lv5, 3 at Lv9). Once every 20s",
                    "BREAK restores {0} heart(s) (once per {1}s)"),
    "iron_heart": ("For each heart of max HP above 5, healing and Shield recharge speed +2% (+4% at Lv9). Up to +30% (+50% at Lv9)",
                   "Per max HP heart above 5: healing & Shield recharge +{0}% (max +{1}%)"),
    "air_combo": ("Land 5 hits (4 at Lv5, 3 at Lv9) without touching the ground to gain attack +10% (+26% at Lv9) for 3s. Landing resets the count",
                  "{0} hits without landing: attack +{2}% for {1}s"),
    "sky_breaker": ("Attacks made in the air build up a boss's BREAK faster (+15%, +55% at Lv9)",
                    "BREAK buildup from air attacks +{0}%"),
    "dive_boost": ("For 2.5s after landing from a dive: move speed +10% (+26% at Lv9), attack +15% (+39% at Lv9). Triggers on every landing",
                   "For {0}s after a dive landing: move speed +{1}%, attack +{2}%"),
    "launch_master": ("Air attacks against launched enemies +12% (+44% at Lv9). Launched enemies fall 10% (30% at Lv9) slower (input windows and hit timing don't change)",
                      "Air attacks vs launched enemies +{0}% / they fall {1}% slower"),
    "sonic_edge": ("While running at 130 km/h or faster: attack +15% (+47% at Lv9), boss BREAK buildup +10% (+42% at Lv9). High-speed accident damage is unchanged",
                   "At 130 km/h or faster: attack +{0}%, BREAK buildup +{1}%"),
    "second_rise": ("Once per run, when your HP drops to 25% or below, restore 30% (60% at Lv9) of your max HP and gain attack and move speed +20% (+40% at Lv9) for 8s",
                    "Once per run at 25% HP or below: restore {0}% of max HP, attack & move speed +{2}% for {1}s"),
    "shield_burst": ("When a Shield (including temporary shields) breaks, release a shockwave of 300% (700% at Lv9) of your attack within 3m, and add +5% (+15% at Lv9) to a boss's BREAK. Once every 6s (3s at Lv9)",
                     "When a Shield breaks: {0}% of attack within 3m, boss BREAK +{1}% (once per {2}s)"),
    "blood_armor": ("Turns 50% (100% at Lv9) of drain healing (hit drain, kill heals, bleed drain) beyond your max HP into a Blood Shield that cancels damage in hearts (max 1 heart, 3 at Lv9; loses 1 every 10s). While you have a Blood Shield, bleed chance +10% (+30% at Lv9)",
                    "{0}% of drain overflow becomes Blood Shield (max {1} hearts) / bleed chance +{2}% while it lasts"),
    "elemental_chaos": ("The more element types (Fire/Ice/Lightning/Wind/Blood) among your deck's element cards, the higher all element chances and power: 2 types +6%, 3 types +12%, 4 types +20%, 5 types +30% (+10/20/32/50% at Lv9). With ELEMENTAL FOCUS, only the higher one applies",
                        "Deck elements 2/3/4/5 types: element chance & power +{0}/{1}/{2}/{3}%"),
    "elemental_focus": ("If your deck's element cards are all one type, that element's power +30% (+70% at Lv9) and chance +10% (+30% at Lv9). Half with 2 types, nothing with 3 or more. With ELEMENTAL CHAOS, only the higher one applies",
                        "Deck with 1 element type: power +{0}%, chance +{1}% (half with 2 types)"),
    "break_dominion": ("Attack vs enemies in BREAK +15% (+47% at Lv9), element power +10% (+42% at Lv9). Bosses defeated during BREAK give MILE and EXP +10% (+42% at Lv9)",
                       "Vs enemies in BREAK: attack +{0}%, element power +{1}%, defeat MILE/EXP +{2}%"),
    "endless_growth": ("Each enemy you defeat gives attack +0.2% (+0.4% at Lv9) for the rest of the run. Up to +15% (+40% at Lv9). Resets when the run ends",
                       "Attack +{0}% per defeat (max +{1}%)"),
    "final_drive": ("From 80 km on: move speed +10% (+20% at Lv9), attack +30% (+70% at Lv9), boss BREAK buildup +20% (+60% at Lv9). Accidents, decision events and enemy damage are unchanged",
                    "From 80 km: move speed +{0}%, attack +{1}%, BREAK buildup +{2}%"),
    "last_gamble": ("[RISK] Enemy attacks have a 30% chance to deal 1 more heart, Heavy Hitters +10%, decision event gaps -30%, enemy spawns +15% (unchanged by Lv; enemy HP is not raised). [REWARD] Enemy, BONUS and boss MILE +100% (+300% at Lv9), EXP gained +60% (+180% at Lv9)",
                    "Enemy attacks deal +1 heart {0}% of the time / Heavy Hitters +{1}% / decision event gaps -{2}%"),
    "one_more_chance": ("Once per run, a lethal hit leaves you with 1 heart instead (1.5s invincible). Then for 10s: attack +40% (+100% at Lv9), move speed +15% (+30% at Lv9), boss BREAK buildup +30% (+80% at Lv9). Used before PHOENIX",
                        "Once per run: survive with 1 heart, then for {0}s attack +{1}%, move speed +{2}%, BREAK +{3}%"),
}

src = io.open(SRC, encoding="utf-8").read()
cards = re.findall(r'new Card\("([a-z_]+)", "([^"]+)", "([^"]+)", (\d), (\w), CardCategory\.(\w+), ([\w.]+),\s*"([^"]*)",\s*"([^"]*)"\)', src)
assert len(cards) == 20, len(cards)
assert set(EN) == {c[0] for c in cards}, set(EN) ^ {c[0] for c in cards}

MAPNAME = {"V": "自然洞窟", "S": "天空回廊", "L": "ラストダンジョン"}
rows = []   # (id, kind, note, ja, en, review)
for i, (cid, name, nja, rar, m, cat, el, desc, beh) in enumerate(cards):
    rows.append((str(10557 + i), "card", f"カード名 {cid}(英字のカード名。全言語で英字のまま。日本語名の参考: {nja})", name, name, False))
for i, (cid, name, nja, rar, m, cat, el, desc, beh) in enumerate(cards):
    rows.append((str(10577 + i), "card", f"{name}({nja}・{MAPNAME[m]}の★{rar})の説明(カード詳細・3択)", desc, EN[cid][0], True))
for i, (cid, name, nja, rar, m, cat, el, desc, beh) in enumerate(cards):
    rows.append((str(10597 + i), "card", f"{name} の効果の行(カード詳細の Lv1/Lv5/Lv9。{{n}} に数値)", beh, EN[cid][1], True))

PH = re.compile(r"\{\d+\}")
for r in rows:
    assert sorted(PH.findall(r[3])) == sorted(PH.findall(r[4])), r
    assert "\t" not in r[3] + r[4] and "\n" not in r[3] + r[4], r

os.makedirs(DOCDIR, exist_ok=True); os.makedirs(ESTDIR, exist_ok=True)
with io.open(os.path.join(DOCDIR, "review_map_jp_en.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("id\tcategory\tnote\tja\ten\n")
    for r in rows: f.write("\t".join(r[:5]) + "\n")
rev = [r for r in rows if r[5]]
with io.open(KEYS, "w", encoding="utf-8", newline="\n") as f:
    for r in rev: f.write("\t".join((r[0], r[1], r[2], r[3])) + "\n")
with io.open(os.path.join(ESTDIR, "src40.tsv"), "w", encoding="utf-8", newline="\n") as f:
    for r in rev: f.write("\t".join((r[0], r[2], r[3], r[4])) + "\n")
lines = [f"{r[0]}\tJA: {r[3]}\tEN: {r[4]}" for r in rev]
with io.open(os.path.join(ESTDIR, "src40_for_estelle.txt"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(lines) + "\n")
with io.open(os.path.join(ESTDIR, "src.js"), "w", encoding="utf-8", newline="\n") as f:
    f.write("window.__SRC40=" + json.dumps("\n".join(lines), ensure_ascii=False) + ";\n")

# en.json へ(英語の表に足す/置き換える。名前も英字のまま)
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
print(f"rows {len(rows)} (review {len(rev)}) -> review_map_jp_en.tsv / est40 / keys_extra40.tsv; en.json +{added} ~{replaced}")

# カード名(英字のまま)は全言語の表へそのまま入れる(既存のカード名と同じ扱い。表に無いと「missing <言語>」の警告が出る)
names = [r for r in rows if not r[5]]
total = 0
for path in sorted(glob.glob(os.path.join(LOC, "*.json"))):
    if os.path.basename(path) == "en.json": continue
    t = json.load(io.open(path, encoding="utf-8"))
    idx = {k: n for n, k in enumerate(t["k"])}
    add = 0
    for r in names:
        if r[3] in idx: continue
        idx[r[3]] = len(t["k"]); t["k"].append(r[3]); t["v"].append(r[3]); add += 1
    if add:
        with io.open(path, "w", encoding="utf-8") as f:
            json.dump(t, f, ensure_ascii=False)
    total += add
print(f"card names (as is) added to the other language tables: +{total}")

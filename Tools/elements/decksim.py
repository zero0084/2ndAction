# 属性ビルドがラン中に揃うか(2026-10-30)。ゲームの3択と同じ: デッキ(12枚・同じカードは1枚)から Lv9 未満のカードを等確率に3枚(重複なし)。
# プレイヤーは「その属性のカードがあれば Lv の低い方」を選び、無ければ他のカード。100km のランの3択の回数は約55回(Lv51 + ボス報酬)を目安にする。
#   python decksim.py → 何回目の3択で系統が全部 Lv9 になるか / 30・45・55回目の時点の系統の Lv の合計
import random, statistics as st
def sim(deck, family, choices=55, trials=4000, seed=1):
    rng = random.Random(seed)
    done_at = []; snap = {30: [], 45: [], 55: []}
    for _ in range(trials):
        lv = {c: 0 for c in deck}
        finished = None
        for n in range(1, choices + 1):
            pool = [c for c in deck if lv[c] < 9]
            if not pool: break
            offer = rng.sample(pool, min(3, len(pool)))
            fam = [c for c in offer if c in family]
            pick = min(fam, key=lambda c: lv[c]) if fam else rng.choice(offer)
            lv[pick] += 1
            if finished is None and all(lv[c] >= 9 for c in family): finished = n
            if n in snap: snap[n].append(sum(lv[c] for c in family))
        done_at.append(finished if finished else 999)
    pct = sum(1 for d in done_at if d <= choices) / trials
    med = st.median(done_at)
    return pct, med, {k: st.mean(v) for k, v in snap.items()}
deck = [f'x{i}' for i in range(12)]
rows = []
for name, k in [('炎の系統4枚', 4), ('氷の系統3枚', 3), ('雷の系統4枚', 4), ('風の系統3枚', 3), ('血の6枚', 6), ('炎2枚+雷2枚(混成)', 4)]:
    fam = deck[:k]
    pct, med, sn = sim(deck, fam)
    need = 9 * k
    rows.append((name, k, need, pct, med, sn))
print('| 構成(デッキ12枚のうち) | 揃える Lv の合計 | 55回の3択で全部 Lv9 になる割合 | 全部 Lv9 になる3択の回数(中央値) | 30回目 | 45回目 | 55回目 |')
print('|---|---|---|---|---|---|---|')
for name, k, need, pct, med, sn in rows:
    print(f'| {name} | {need} | {pct*100:.0f}% | {"55回では届かない" if med >= 999 else f"{med:.0f}回"} | {sn[30]:.0f}/{need} | {sn[45]:.0f}/{need} | {sn[55]:.0f}/{need} |')

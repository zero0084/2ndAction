# -*- coding: utf-8 -*-
# 絵(コマ)ごとの「足元の高さ」「横の位置」「高さ」の平均(ゲームの中、体の高さに対する割合)。QaSweep -qaAnimJitter の jitter.tsv から。
# 地上のボス/雑魚で、同じ状態の他のコマより足元が浮く/沈む、横へずれる、大きさが違うコマを探す。
# 使い方: python animfeet.py jitter.tsv [id の一部]
import sys, collections, statistics as st
sys.path.insert(0, __import__('os').path.dirname(__file__))
from animjitter import load

rows = load(sys.argv[1])
flt = sys.argv[2] if len(sys.argv) > 2 else ''
per = collections.defaultdict(lambda: collections.defaultdict(list))
H = {}
for (id_, who), rs in rows.items():
    if flt and flt not in id_: continue
    hs = [r['maxY'] - r['minY'] for r in rs if r['sprite'].startswith(('move', 'run')) or '_run_' in r['sprite'] or 'flap' in r['sprite']]
    h = st.median(hs) if hs else st.median(r['maxY'] - r['minY'] for r in rs)
    H[id_] = h
    for r in rs:
        per[id_][r['sprite']].append(((r['minY'] - r['groundY']) / h, (r['minY'] - r['rootY']) / h, (r['cx'] - r['rootX']) * r['face'] / h, (r['maxY'] - r['minY']) / h, r['pose']))
for id_ in sorted(per):
    print(f'== {id_}  (体の高さ {H[id_]:.2f})')
    for sp, v in sorted(per[id_].items()):
        g = st.mean(x[0] for x in v); f = st.mean(x[1] for x in v); c = st.mean(x[2] for x in v); h = st.mean(x[3] for x in v)
        poses = ','.join(sorted({x[4] for x in v}))
        print(f'  {sp:<28} n{len(v):4d} 地面から{g:+.2f} 根元から{f:+.2f} 横{c:+.2f} 高さ{h:.2f}  {poses}')

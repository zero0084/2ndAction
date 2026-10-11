# -*- coding: utf-8 -*-
# 攻略の再検証の最終集計(2026-10-12): 調整前 V2B / 調整後 V2A(双剣士・魔法使い・忍者)/ 最終版 V3A(お嬢様・格闘家)/ 確認試験 V2R。
# usage: python aggregate_v3.py <bl2_base> <bl2_after> <bl3_v3> <outdir>
import csv, glob, json, os, statistics as st, sys
from collections import Counter

base_dir, after_dir, v3_dir, out = sys.argv[1:5]
CH = {'noble_lady': 'お嬢様', 'fighter': '格闘家', 'dual_blade': '双剣士', 'mage': '魔法使い', 'ninja': '忍者', 'swordsman': '黒剣士'}
ST = {'wasteland_road': '荒野街道', 'natural_cave': '自然洞窟', 'sky_corridor': '天空回廊', 'last_corridor': 'ラスダン'}
CAT = {'A': 'A 補助の未発動', 'B': 'B 穴・段差・地形', 'C': 'C 当てられない', 'D': 'D 火力不足(試験の上限)', 'E': 'E 被弾の積み重ね'}


def load(d):
    rows = []
    for f in sorted(glob.glob(os.path.join(d, '*', 'result.json'))):
        rows.append(json.load(open(f, encoding='utf-8')))
    return rows


rows = []
for r in load(base_dir): r['_tag'] = 'base'; rows.append(r)
for r in load(after_dir):
    t = r['trial']
    if t.startswith('V2R'): r['_tag'] = 'regress_after' if t[5] == 'A' else 'regress_base'
    elif r['character'] in ('noble_lady', 'fighter'): r['_tag'] = 'v2a_partial'  # 途中で止めた旧版の調整後(V3A に置き換え)
    else: r['_tag'] = 'after'
    rows.append(r)
for r in load(v3_dir): r['_tag'] = 'after'; r['_v3'] = True; rows.append(r)


def cat(r):
    return 'OK' if r['success'] else (r.get('category') or '?')[:1]


os.makedirs(out, exist_ok=True)
L = []
L.append('## キャラ別のまとめ(3シード×4マップ = 12本ずつ。お嬢様/格闘家の「調整後」は最終版 V3A)\n')
L.append('| キャラ | 調整前 成功 | 調整前 8万m到達 | 調整前 到達の中央値 | 調整後 成功 | 調整後 8万m到達 | 調整後 到達の中央値 | 調整後に止まった理由 |')
L.append('|---|---|---|---|---|---|---|---|')
for ch in ['noble_lady', 'fighter', 'dual_blade', 'mage', 'ninja']:
    cells = []
    for tag in ['base', 'after']:
        p = [r for r in rows if r['_tag'] == tag and r['character'] == ch]
        if not p: cells.append(('-', '-', '-', '')); continue
        ok = sum(1 for r in p if r['success']); r80 = sum(1 for r in p if r['maxDistance'] >= 80000)
        med = st.median(r['maxDistance'] for r in p) / 1000
        c = Counter(cat(r) for r in p if not r['success'])
        cells.append((f'{ok}/{len(p)}', f'{r80}/{len(p)}', f'{med:.0f}km', ' '.join(f'{k}{v}' for k, v in c.most_common())))
    L.append(f'| {CH[ch]} | {cells[0][0]} | {cells[0][1]} | {cells[0][2]} | {cells[1][0]} | {cells[1][1]} | {cells[1][2]} | {cells[1][3]} |')
L.append('\n区分: ' + ' / '.join(CAT.values()) + '\n')

L.append('## キャラ×マップ(3シードの到達 km、成功は ✓)\n')
L.append('| キャラ | マップ | 調整前 | 調整後 | 調整後の止まった所(最後のボス) |')
L.append('|---|---|---|---|---|')
for ch in ['noble_lady', 'fighter', 'dual_blade', 'mage', 'ninja']:
    for stg in ST:
        cells = []
        last = ''
        for tag in ['base', 'after']:
            p = sorted([r for r in rows if r['_tag'] == tag and r['character'] == ch and r['stage'] == stg], key=lambda x: x['seed'])
            cells.append(' / '.join(('✓' if r['success'] else f"{r['maxDistance'] / 1000:.0f}") for r in p) or '-')
            if tag == 'after':
                last = ', '.join(f"{cat(r)} {((r.get('bosses') or [{}])[-1].get('names') or '')}".strip() for r in p if not r['success'])
        L.append(f'| {CH[ch]} | {ST[stg]} | {cells[0]} | {cells[1]} | {last} |')

L.append('\n## 確認試験 V2R(同じ実行ファイルで調整なし/あり、シード 7103)\n')
L.append('| 試験 | 調整なし | 調整あり |')
L.append('|---|---|---|')
reg = {}
for r in rows:
    if r['_tag'].startswith('regress'):
        key = (r['character'], r['stage'])
        reg.setdefault(key, {})[r['_tag']] = r
for (ch, stg), d in sorted(reg.items()):
    f = lambda r: '-' if r is None else ('成功' if r['success'] else f"{r['maxDistance'] / 1000:.0f}km {cat(r)}")
    L.append(f"| {CH.get(ch, ch)} {ST.get(stg, stg)} | {f(d.get('regress_base'))} | {f(d.get('regress_after'))} |")

dirty = [r['trial'] for r in rows if r.get('debugClean') is False]
L.append(f"\n試行の数: 調整前 {sum(1 for r in rows if r['_tag'] == 'base')} / 調整後 {sum(1 for r in rows if r['_tag'] == 'after')} / 確認 {sum(1 for r in rows if r['_tag'].startswith('regress'))}"
         f" / 旧版の途中 {sum(1 for r in rows if r['_tag'] == 'v2a_partial')}。DEBUG 設定が残った試行: {len(dirty)} {dirty[:5]}")
L.append(f"平均 fps: 調整前 {st.mean(r['avgFps'] for r in rows if r['_tag'] == 'base'):.0f} / 調整後 {st.mean(r['avgFps'] for r in rows if r['_tag'] == 'after'):.0f}")
open(os.path.join(out, 'final_v3.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('\n'.join(L))

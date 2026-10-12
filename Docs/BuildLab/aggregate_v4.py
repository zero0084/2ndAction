# -*- coding: utf-8 -*-
# 格闘家/お嬢様の追加強化の集計(2026-10-12)。
# 基準: 前回の最終(お嬢様/格闘家 = V3A、他のキャラ = V2A)。今回: V4A(強化 + サソリ修正)、V4J(お嬢様 2段ジャンプ)、V4R(関連キャラの再確認)
# usage: python aggregate_v4.py <bl3_v3> <bl2_after> <bl4_v4a> <bl4_v4j> <bl4_v4r> <outdir>
import glob, json, os, statistics as st, sys
from collections import Counter, defaultdict

v3, v2, v4a, v4j, v4r, out = sys.argv[1:7]
CH = {'noble_lady': 'お嬢様', 'fighter': '格闘家', 'dual_blade': '双剣士', 'mage': '魔法使い', 'ninja': '忍者'}
ST = {'wasteland_road': '荒野街道', 'natural_cave': '自然洞窟', 'sky_corridor': '天空回廊', 'last_corridor': 'ラスダン'}
CAT = {'A': 'A 補助の未発動', 'B': 'B 穴・段差・地形', 'C': 'C 当てられない', 'D': 'D 火力不足(試験の上限)', 'E': 'E 被弾の積み重ね'}
PROBLEM = ['Scorpion', 'Titan', 'SkyGolem', 'Majin', 'Worm', 'Bat', 'Phoenix', 'Jellyfish']


def load(d, pred=lambda r: True):
    rows = []
    for f in sorted(glob.glob(os.path.join(d, '*', 'result.json'))):
        r = json.load(open(f, encoding='utf-8'))
        r['_dir'] = os.path.dirname(f)
        if pred(r): rows.append(r)
    return rows


base = load(v3) + load(v2, lambda r: r['trial'].startswith('V2A') and r['character'] not in ('noble_lady', 'fighter'))
sets = {'base': base, 'v4a': load(v4a), 'v4j': load(v4j), 'v4r': load(v4r)}


def cat(r): return 'OK' if r['success'] else (r.get('category') or '?')[:1]


def summ(p):
    if not p: return ('-', '-', '-', '', '-')
    ok = sum(1 for r in p if r['success']); r80 = sum(1 for r in p if r['maxDistance'] >= 80000)
    med = st.median(r['maxDistance'] for r in p) / 1000
    c = Counter(cat(r) for r in p if not r['success'])
    return (f'{ok}/{len(p)}', f'{r80}/{len(p)}', f'{med:.0f}km', ' '.join(f'{k}{v}' for k, v in c.most_common()), f"{st.mean(r['avgFps'] for r in p):.0f}")


def falls_per_10km(p):
    f = km = 0
    for r in p:
        ev = os.path.join(r['_dir'], 'events.tsv')
        if not os.path.exists(ev): continue
        for line in open(ev, encoding='utf-8', errors='replace'):
            q = line.split('\t')
            if len(q) > 3 and q[2] == 'hit' and q[3].startswith('DeathY'): f += 1
        km += r['maxDistance'] / 1000
    return f'{10 * f / km:.1f}' if km else '-'


os.makedirs(out, exist_ok=True)
L = []
L.append('## キャラ別(3シード × 4マップ = 12本ずつ。条件は前回と同じ: B 構成 Lv9・疾走出発 1万m・同時 8本)\n')
L.append('| キャラ | 版 | 成功 | 8万m到達 | 到達の中央値 | 止まった理由 | 落下/10km | 平均fps |')
L.append('|---|---|---|---|---|---|---|---|')
for ch, variants in [('noble_lady', [('base', '前回(V3A)'), ('v4a', '今回 強化(V4A)'), ('v4j', '今回 強化+2段ジャンプ(V4J)')]),
                     ('fighter', [('base', '前回(V3A)'), ('v4a', '今回 強化(V4A)')])]:
    for key, label in variants:
        p = [r for r in sets[key] if r['character'] == ch]
        s = summ(p)
        L.append(f'| {CH[ch]} | {label} | {s[0]} | {s[1]} | {s[2]} | {s[3]} | {falls_per_10km(p)} | {s[4]} |')
L.append('\n区分: ' + ' / '.join(CAT.values()) + '\n')

L.append('## キャラ×マップ(シード 7101 / 7102 / 7103 の到達 km、成功は ✓)\n')
L.append('| キャラ | マップ | 前回 | 今回 V4A | V4J(お嬢様 2段ジャンプ) | 今回の止まった所(V4J があればそちら) |')
L.append('|---|---|---|---|---|---|')
for ch in ['noble_lady', 'fighter']:
    for stg in ST:
        cells, last = [], ''
        for key in ['base', 'v4a', 'v4j']:
            p = sorted([r for r in sets[key] if r['character'] == ch and r['stage'] == stg], key=lambda x: x['seed'])
            cells.append(' / '.join(('✓' if r['success'] else f"{r['maxDistance'] / 1000:.0f}") for r in p) or '-')
            if p and key in ('v4a', 'v4j'):
                last = ', '.join(f"{cat(r)} {((r.get('bosses') or [{}])[-1].get('names') or '')}".strip() for r in p if not r['success'])
        L.append(f'| {CH[ch]} | {ST[stg]} | {cells[0]} | {cells[1]} | {cells[2]} | {last} |')

L.append('\n## 関連キャラの再確認(サソリ修正 = ボスの共通処理の変更のため)\n')
L.append('| キャラ | マップ | 前回(同じシード) | 今回 V4R |')
L.append('|---|---|---|---|')
for r in sorted(sets['v4r'], key=lambda x: (x['character'], x['stage'], x['seed'])):
    b = [x for x in base if x['character'] == r['character'] and x['stage'] == r['stage'] and x['seed'] == r['seed']]
    f = lambda x: '成功' if x['success'] else f"{x['maxDistance'] / 1000:.0f}km {cat(x)} {((x.get('bosses') or [{}])[-1].get('names') or '')}"
    L.append(f"| {CH[r['character']]} | {ST[r['stage']]} s{r['seed']} | {f(b[0]) if b else '-'} | {f(r)} |")

L.append('\n## 問題のボス戦(1戦あたり、全試行の合計)\n')
L.append('| キャラ | ボス | 版 | 戦数 | 秒(中央値) | 最長 | 被弾/戦 | 減ったHP/戦 | 命中率(HP減/振り) |')
L.append('|---|---|---|---|---|---|---|---|---|')
for ch in ['noble_lady', 'fighter', 'dual_blade', 'ninja']:
    for boss in PROBLEM:
        for key, label in [('base', '前回'), ('v4a', '今回'), ('v4j', '今回+2段'), ('v4r', '今回')]:
            p = [r for r in sets[key] if r['character'] == ch]
            fights = [b for r in p for b in (r.get('bosses') or []) if (b.get('names') or '') == boss]
            if not fights: continue
            secs = [b.get('seconds') or 0 for b in fights]
            hits = st.mean(b.get('hitsTaken') or 0 for b in fights)
            lost = st.mean((b.get('playerHpStart') or 0) - (b.get('playerHpEnd') or 0) for b in fights)
            hr = sum(b.get('hpDrops') or 0 for b in fights) / max(1, sum(b.get('swings') or 0 for b in fights))
            L.append(f'| {CH[ch]} | {boss} | {label} | {len(fights)} | {st.median(secs):.0f} | {max(secs):.0f} | {hits:.1f} | {lost:.0f} | {hr:.2f} |')

open(os.path.join(out, 'final_v4.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('\n'.join(L))

# -*- coding: utf-8 -*-
# 敵のアニメーションのドタバタの解析(2026-10-24)。QaSweep -qaAnimJitter の jitter.tsv を読む。
# 使い方: python animjitter.py jitter.tsv [比較用(修正後)の jitter.tsv] [--csv out.tsv]
# ゲームの中の毎フレームの「絵の名前・pivot の位置・大きさ」と、PNG/.meta から出した絵の見える範囲を合わせて、画面での位置を出す。
# 1体ごとに数えるもの(体の高さに対する割合):
#   跳び      : 絵が替わったフレームの 足元(絵の下端 - 根元)/ 横(絵の中心 - 根元)/ 高さ の変化の最大。0.08 より大きいものを「大きな跳び」
#   1枚目へ戻る: 走りのコマの途中から 0枚目へ戻った回数(動く/止まるの細かい切り替え)
#   行き来    : 0.25秒以内に元の姿勢(状態)へ戻った回数
#   震え      : 根元の高さの変化の向きが毎フレーム入れ替わった回数(体の高さの 0.4% より大きいもの)
import sys, os, re, csv, glob, collections, statistics as st
from PIL import Image
import numpy as np

A = r'C:/GameProject/2ndAction/Unity/2ndAction/Assets'
ALIGN = {0: (.5, .5), 1: (0, 1), 2: (.5, 1), 3: (1, 1), 4: (0, .5), 5: (1, .5), 6: (0, 0), 7: (.5, 0), 8: (1, 0)}

def meta(png):
    t = open(png + '.meta', encoding='utf-8').read()
    ppu = float(re.search(r'spritePixelsToUnits: ([\d.]+)', t).group(1))
    m = re.search(r'\n  alignment: (\d+)', t)
    al = int(m.group(1)) if m else 0
    if al == 9:
        m = re.search(r'spritePivot: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', t)
        return ppu, (float(m.group(1)), float(m.group(2)))
    return ppu, ALIGN.get(al, (.5, .5))

_cache = {}
def local_box(png):
    """pivot から見た不透明な範囲(ワールド単位、大きさ1の時): bottom, top, left, right, cx"""
    if png in _cache: return _cache[png]
    im = Image.open(png).convert('RGBA'); W, H = im.size
    k = max(1, max(W, H) // 400)
    a = np.asarray(im.resize((max(1, W // k), max(1, H // k))))[:, :, 3] > 40
    ys, xs = np.nonzero(a)
    ppu, (px, py) = meta(png)
    if len(xs) == 0: _cache[png] = None; return None
    x0, x1 = xs.min() * k, (xs.max() + 1) * k; y0t, y1t = ys.min() * k, (ys.max() + 1) * k
    r = dict(bottom=((H - y1t) - py * H) / ppu, top=((H - y0t) - py * H) / ppu, left=(x0 - px * W) / ppu, right=(x1 - px * W) / ppu, cx=(xs.mean() * k - px * W) / ppu)
    _cache[png] = r; return r

_index = None
def find_png(sprite, id_):
    global _index
    if _index is None:
        _index = collections.defaultdict(list)
        for f in glob.glob(A + '/**/*.png', recursive=True):
            _index[os.path.splitext(os.path.basename(f))[0]].append(f.replace('\\', '/'))
    cands = _index.get(sprite, [])
    if len(cands) <= 1: return cands[0] if cands else None
    if ':' in id_:   # ボス: BossAnim/<snake_case>/
        key = re.sub(r'(?<!^)(?=[A-Z])', '_', id_.split(':')[1]).lower()
        for c in cands:
            if '/BossAnim/' + key + '/' in c: return c
    return cands[0]

def load(path):
    rows = collections.defaultdict(list)
    with open(path, encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            for k in ('t', 'rootX', 'rootY', 'pivX', 'pivY', 'sclX', 'sclY', 'groundY'): r[k] = float(r[k])
            png = find_png(r['sprite'], r['id'])
            b = local_box(png) if png else None
            if not b: continue
            sx = r['sclX'] * (-1 if r.get('flipX') == '1' else 1)
            xs = sorted([r['pivX'] + sx * b['left'], r['pivX'] + sx * b['right']])
            r['minY'] = r['pivY'] + r['sclY'] * b['bottom']; r['maxY'] = r['pivY'] + r['sclY'] * b['top']
            r['minX'], r['maxX'] = xs; r['cx'] = r['pivX'] + sx * b['cx']; r['face'] = 1 if sx >= 0 else -1
            rows[(r['id'], r['who'])].append(r)
    return rows

def analyse(rows):
    per = collections.defaultdict(lambda: collections.defaultdict(list))
    for (id_, who), rs in rows.items():
        if len(rs) < 20: continue
        H = st.median(r['maxY'] - r['minY'] for r in rs) or 1.0
        a = per[id_]
        a['secs'].append(rs[-1]['t'] - rs[0]['t'])
        changes = 0; reset0 = 0; flick = 0
        prev = rs[0]; pose_seq = []
        for r in rs[1:]:
            if r['t'] - prev['t'] > 0.1: prev = r; continue   # 抜けたフレーム(撃破/出し直し)
            if r['sprite'] != prev['sprite']:
                changes += 1
                feet = abs((r['minY'] - r['rootY']) - (prev['minY'] - prev['rootY'])) / H
                side = abs((r['cx'] - r['rootX']) - (prev['cx'] - prev['rootX'])) / H if r['face'] == prev['face'] else 0.0
                hh = abs((r['maxY'] - r['minY']) / max(1e-4, prev['maxY'] - prev['minY']) - 1)
                a['jumps'].append((max(feet, side, hh), feet, side, hh, prev['sprite'], r['sprite'], prev['pose'], r['pose']))
                m0 = re.search(r'_(\d+)$', r['sprite']); m1 = re.search(r'_(\d+)$', prev['sprite'])
                if m0 and m1 and int(m0.group(1)) == 0 and int(m1.group(1)) not in (0,) and r['sprite'][:-len(m0.group(1))] == prev['sprite'][:-len(m1.group(1))]:
                    nxt = int(m1.group(1)) + 1
                    if not any(x['sprite'] == r['sprite'][:-len(m0.group(1))] + str(nxt).zfill(len(m1.group(1))) for x in rs[:1]):
                        pass
                    # 最後のコマ以外から 0 へ戻った = 途中で戻された
                    names = {x['sprite'] for x in rs if x['sprite'].startswith(r['sprite'][:-len(m0.group(1))])}
                    last = max(int(re.search(r'_(\d+)$', n).group(1)) for n in names)
                    if int(m1.group(1)) != last: reset0 += 1
            if r['pose'] != prev['pose']: pose_seq.append((r['t'], prev['pose'], r['pose']))
            prev = r
        for i in range(1, len(pose_seq)):
            if pose_seq[i][0] - pose_seq[i - 1][0] < 0.25 and pose_seq[i][2] == pose_seq[i - 1][1]: flick += 1
        dys = [(b['rootY'] - x['rootY']) / H for x, b in zip(rs, rs[1:]) if b['t'] - x['t'] < 0.1]
        shake = sum(1 for d1, d2 in zip(dys, dys[1:]) if abs(d1) > 0.004 and abs(d2) > 0.004 and d1 * d2 < 0)
        a['changes'].append(changes); a['reset0'].append(reset0); a['flick'].append(flick); a['shake'].append(shake)
    out = {}
    for id_, a in per.items():
        secs = sum(a['secs']) or 1.0
        js = sorted(a['jumps'], key=lambda j: -j[0])
        out[id_] = dict(secs=secs, cps=sum(a['changes']) / secs, reset0=sum(a['reset0']) / secs, flick=sum(a['flick']) / secs,
                        shake=sum(a['shake']) / secs, big=sum(1 for j in js if j[0] > 0.08) / secs, worst=js[:3])
    return out

def score(s): return s['big'] + s['reset0'] + s['flick'] + s['shake'] * 0.3

def fmt(id_, s):
    w = '; '.join(f"{j[4]}->{j[5]}({j[6]}->{j[7]}) 足{j[1]:.2f} 横{j[2]:.2f} 高{j[3]:.2f}" for j in s['worst'])
    return f"{id_:<20} 替/秒{s['cps']:5.1f} 大跳/秒{s['big']:5.2f} 1枚目/秒{s['reset0']:5.2f} 行来/秒{s['flick']:5.2f} 震/秒{s['shake']:5.2f} | {w}"

if __name__ == '__main__':
    args = [x for x in sys.argv[1:] if not x.startswith('--')]
    a = analyse(load(args[0]))
    b = analyse(load(args[1])) if len(args) > 1 else None
    for id_ in sorted(a, key=lambda k: -score(a[k])):
        print(fmt(id_, a[id_]))
        if b and id_ in b: print('  後 ' + fmt(id_, b[id_]))

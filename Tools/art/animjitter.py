# -*- coding: utf-8 -*-
# 敵のアニメーションのドタバタの解析(2026-10-24)。QaSweep -qaAnimJitter の jitter.tsv を読む。
# 使い方: python animjitter.py jitter.tsv [比較用の jitter.tsv]
# 1体(who)ごとに、絵が替わったフレームの「足元(絵の下端 - 根元)」「横(絵の中心 - 根元)」「高さ」の跳び(体の高さに対する割合)、
# 走りの1枚目へ戻った回数(動く/止まるの細かい切り替え)、姿勢の切り替えの回数(0.25秒以内に戻ったもの)、根元の上下の震えを数える。
import sys, csv, collections, statistics as st

def load(path):
    rows = collections.defaultdict(list)
    with open(path, encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            for k in ('t', 'rootX', 'rootY', 'minY', 'maxY', 'minX', 'maxX', 'cx', 'groundY', 'visSy'): r[k] = float(r[k])
            rows[(r['id'], r['who'])].append(r)
    return rows

def analyse(rows):
    per = collections.defaultdict(lambda: collections.defaultdict(list))
    for (id_, who), rs in rows.items():
        if len(rs) < 20: continue
        H = st.median(r['maxY'] - r['minY'] for r in rs) or 1.0
        a = per[id_]
        a['secs'].append(rs[-1]['t'] - rs[0]['t'])
        changes = 0; jumps = []; reset0 = 0; flick = 0; last_pose_change = {}
        prev = rs[0]; pose_seq = []
        for r in rs[1:]:
            if r['sprite'] != prev['sprite']:
                changes += 1
                feet = abs((r['minY'] - r['rootY']) - (prev['minY'] - prev['rootY'])) / H
                side = abs((r['cx'] - r['rootX']) - (prev['cx'] - prev['rootX'])) / H
                hh = abs((r['maxY'] - r['minY']) / max(1e-4, prev['maxY'] - prev['minY']) - 1)
                jumps.append((max(feet, side, hh), feet, side, hh, prev['sprite'], r['sprite'], prev['pose'], r['pose']))
                # 走りの 0 枚目へ戻った(連続のコマの途中から)
                if r['sprite'].endswith('_0') and not prev['sprite'].endswith(('_0',)) and '_run' in r['sprite']: reset0 += 1
            if r['pose'] != prev['pose']:
                pose_seq.append((r['t'], prev['pose'], r['pose']))
            prev = r
        # 0.25秒以内に元の姿勢へ戻った切り替え
        for i in range(1, len(pose_seq)):
            if pose_seq[i][0] - pose_seq[i - 1][0] < 0.25 and pose_seq[i][2] == pose_seq[i - 1][1]: flick += 1
        # 根元の上下の震え: 1フレームごとの y の変化の向きが入れ替わる回数(体の高さの 0.5% より大きいもの)
        dys = [(b['rootY'] - a_['rootY']) / H for a_, b in zip(rs, rs[1:])]
        shake = sum(1 for d1, d2 in zip(dys, dys[1:]) if abs(d1) > 0.005 and abs(d2) > 0.005 and d1 * d2 < 0)
        # 絵の下端と地面の差の揺れ(地上の敵)
        a['changes'].append(changes); a['reset0'].append(reset0); a['flick'].append(flick); a['shake'].append(shake)
        a['jumps'].extend(jumps)
    out = {}
    for id_, a in per.items():
        secs = sum(a['secs']) or 1.0
        js = sorted(a['jumps'], key=lambda j: -j[0])
        big = [j for j in js if j[0] > 0.08]
        out[id_] = dict(secs=secs, cps=sum(a['changes']) / secs, reset0=sum(a['reset0']) / secs, flick=sum(a['flick']) / secs,
                        shake=sum(a['shake']) / secs, big=len(big) / secs, worst=js[:3])
    return out

def fmt(id_, s):
    w = '; '.join(f"{j[4]}->{j[5]} feet {j[1]:.2f} side {j[2]:.2f} h {j[3]:.2f}" for j in s['worst'])
    return f"{id_:<22} 替わり/秒 {s['cps']:5.1f}  大きな跳び/秒 {s['big']:4.2f}  1枚目へ戻る/秒 {s['reset0']:4.2f}  姿勢の行き来/秒 {s['flick']:4.2f}  上下の震え/秒 {s['shake']:4.2f}  | {w}"

if __name__ == '__main__':
    a = analyse(load(sys.argv[1]))
    b = analyse(load(sys.argv[2])) if len(sys.argv) > 2 else None
    for id_ in sorted(a, key=lambda k: -(a[k]['big'] + a[k]['reset0'] + a[k]['flick'] + a[k]['shake'] * 0.2)):
        print(fmt(id_, a[id_]))
        if b and id_ in b: print('   after: ' + fmt(id_, b[id_]))

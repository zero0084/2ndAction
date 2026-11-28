# -*- coding: utf-8 -*-
# 2026-11-28 依頼⑦: 4人マルチの三姉妹戦の集計(HOST の記録): 姉妹ごとの出現〜退く/撃破の時間・HP・各自の命中数、各自の被ダメージ、クリア/YES-NO
#  python -I -X utf8 Tools/net/sis4.py <試験のフォルダ>
import re, sys, os, glob, datetime
D = sys.argv[1]
def ts(line):
    m = re.search(r'utc=(\d\d):(\d\d):(\d\d\.\d+)', line)
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)) if m else None
host = os.path.join(D, 'p1_host.log')
L = open(host, encoding='utf-8', errors='replace').read().splitlines()
last_t = None; sis = {}; order = []
for ln in L:
    t = ts(ln)
    if t is not None: last_t = t
    m = re.search(r'\[NET\]\[BOSS\] Spawn id=(\d+) type=ReaperSisterBoss.*hp=(\d+)/(\d+)', ln)
    if m: sis[m.group(1)] = {'t0': last_t, 'hp': int(m.group(3)), 'hits': {}, 'dmg': {}, 'end': None, 'how': ''}; order.append(m.group(1)); continue
    m = re.search(r'\[NET\]\[BOSS\] Damage id=(\d+) attacker=(P\d) dmg=(\d+) hp=(\d+)/', ln)
    if m and m.group(1) in sis:
        s = sis[m.group(1)]; p = m.group(2); s['hits'][p] = s['hits'].get(p, 0) + 1; s['dmg'][p] = s['dmg'].get(p, 0) + int(m.group(3))
        if int(m.group(4)) == 0 and s['end'] is None: s['end'] = last_t; s['how'] = 'HP0'
        continue
    m = re.search(r'\[NET\]\[BOSS\] (Death|Despawn) id=(\d+)', ln)
    if m and m.group(2) in sis and sis[m.group(2)]['end'] is None: sis[m.group(2)]['end'] = last_t; sis[m.group(2)]['how'] = m.group(1)
names = ['solo Eldest', 'solo Second', 'solo Youngest', 'group Eldest', 'group Second', 'group Youngest']
print('sister\thp\tsec\tend\thits P1/P2/P3/P4\tdamage P1/P2/P3/P4')
for i, k in enumerate(order):
    s = sis[k]; sec = (s['end'] - s['t0']) if s['end'] and s['t0'] else -1
    print(f"{names[i] if i < 6 else k}\t{s['hp']}\t{sec:.1f}\t{s['how']}\t" + '/'.join(str(s['hits'].get(f'P{n}', 0)) for n in range(1, 5)) + '\t' + '/'.join(str(s['dmg'].get(f'P{n}', 0)) for n in range(1, 5)))
for f in sorted(glob.glob(os.path.join(D, 'p*_*.log'))):
    txt = open(f, encoding='utf-8', errors='replace').read()
    # 三姉妹戦の間(最初の姉妹の出現 〜 クリア)の HP の減り(毎0.5秒の NAT の行。回復で増えた分は数えない)
    a = txt.find('type=ReaperSisterBoss'); b = txt.find('LAST DUNGEON CLEAR', a if a >= 0 else 0)
    seg = txt[a:b] if a >= 0 else ''
    hps = [int(x) for x in re.findall(r' NAT utc=\S+ t=\S+ .*? hp=(-?\d+)/\d+', seg)]
    drops = [hps[i - 1] - hps[i] for i in range(1, len(hps)) if hps[i] < hps[i - 1] and hps[i] >= 0]
    downs = len(re.findall(r'st=down|state=Down', seg))
    reap = drops
    allc = re.search(r'COND all cards Lv\d+: applied (\d+) \(failed (\d+)\) bossHpMul=([0-9.]+) lives=(\d+)/(\d+) atk=(\d+)', txt)
    clear = re.search(r'LAST DUNGEON CLEAR via \w+: (.*)', txt)
    ch = re.search(r'(HOST chose \w+|ONE MORE MILE\? HOST chose \w+)', txt)
    fin = re.search(r'finished by choice \((\w+)\) at (\d+)m', txt)
    beyond = re.findall(r'\[LastDungeon\] (BEYOND[^\n]{0,80}|beyond[^\n]{0,80})', txt)
    print(f"{os.path.basename(f)}: cards {allc.groups() if allc else '-'} | HP lost during the sisters {sum(reap)} ({len(reap)} drops) | clear: {clear.group(1)[:70] if clear else '-'} | {ch.group(1) if ch else '-'} | {('finished ' + fin.group(1) + ' at ' + fin.group(2) + 'm') if fin else ''} {beyond[:1]}")

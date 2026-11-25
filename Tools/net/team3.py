# -*- coding: utf-8 -*-
# 3人の同時の確認(2026-11-25 第3)。使い方: python team3.py <dir with p1.log/p2.log/p3.log> coop|versus
#  ・「P1 がボス戦(闘技場で戦っている)・P2 がカード選択中・P3 が走っている」が同時にあった時間と、その間 P3 が進み続けたか
#  ・P2 は選択中に倒れない(被弾の試し/HOST への申告が拒否・HP が減らない・DOWN にならない)
#  ・どの端末も時間(ts)が止まらない / 画面の距離が下がらない / 協力は3台の画面の距離がほぼ同じ
#  ・ボス報酬はとどめの人の端末だけ
import io, os, re, sys
from datetime import datetime

D = sys.argv[1]; MODE = sys.argv[2] if len(sys.argv) > 2 else 'coop'
pat = re.compile(r'TEAMLOG (.*)')
def parse(path):
    rows, lines = [], []
    if not os.path.exists(path): return rows, lines
    for line in io.open(path, encoding='utf-8', errors='replace'):
        lines.append(line)
        m = pat.search(line)
        if not m: continue
        kv = dict(p.split('=', 1) for p in m.group(1).split() if '=' in p)
        try: kv['T'] = datetime.strptime(kv['utc'], '%H:%M:%S.%f')
        except Exception: continue
        for k in ('x', 'hud', 'own', 'ts', 't', 'hsL', 'hsS'):
            if k in kv:
                try: kv[k] = float(kv[k])
                except Exception: kv[k] = 0.0
        for k in ('holding', 'choosing', 'paused', 'hold', 'phase'):
            kv[k] = kv.get(k) == 'True'
        kv['bosses'] = int(kv.get('bosses', '0'))
        rows.append(kv)
    return rows, lines

P = {}
for n in ('p1', 'p2', 'p3'):
    P[n] = parse(os.path.join(D, n + '.log'))
res = []
def check(ok, msg): res.append(('OK  ' if ok else 'NG  ') + msg)

for n, (rows, _) in P.items():
    if not rows: check(False, f'{n}: no TEAMLOG'); continue
    back = max((a['hud'] - b['hud'] for a, b in zip(rows, rows[1:])), default=0)
    check(back < 1.0, f'{n}: screen distance never goes back (largest drop {back:.2f} m)')
    zero = sum(1 for r in rows if r['ts'] < 0.05 and not r['paused'])
    check(zero == 0, f'{n}: the device time never stopped (ts=0 samples {zero})')
    res.append(f'INFO {n}: me={rows[-1].get("me")} hud {rows[0]["hud"]:.0f}->{rows[-1]["hud"]:.0f} own {rows[-1]["own"]:.0f} hit-stop self {rows[-1].get("hsL", 0):.0f} / not stopped {rows[-1].get("hsS", 0):.0f}')

def at(rows, T):
    best = None
    for r in rows:
        if best is None or abs((r['T'] - T).total_seconds()) < abs((best['T'] - T).total_seconds()): best = r
    return best if best is not None and abs((best['T'] - T).total_seconds()) <= 0.35 else None

r1, r2, r3 = P['p1'][0], P['p2'][0], P['p3'][0]
if r1 and r2 and r3:
    both = []
    for a in r1:
        b = at(r2, a['T']); c = at(r3, a['T'])
        if b is None or c is None: continue
        fighting = a['holding'] and a['bosses'] > 0
        if fighting and b['choosing'] and not c['choosing'] and not c['holding']: both.append((a, b, c))
    sec = len(both) * 0.5
    if '_T3' in D.replace('\\', '/').replace('/T3', '_T3'):
        # T3 は「P2 がとどめ → P2 が報酬を選ぶ間、ほかの2人は走り続ける」(ボスはもういない = 3つ同時は見ない)
        res.append(f'INFO 3 states at once: {sec:.1f}s (T3 checks the others keep running while P2 chooses the boss reward)')
        for n2, rows2 in (('P1', r1), ('P3', r3)):
            win = [r for r in r2 if r['choosing']]
            if len(win) >= 6:
                xs = [at(rows2, w['T']) for w in win]; xs = [q for q in xs if q is not None]
                if len(xs) >= 2: check(xs[-1]['x'] - xs[0]['x'] > 3.0, f'{n2} kept running while P2 chose ({xs[-1]["x"] - xs[0]["x"]:.1f} m in {len(win) * 0.5:.1f}s)')
    else:
        check(sec >= 2.0, f'3 states at once (P1 boss fight / P2 card choice / P3 running): {sec:.1f}s')
    if len(both) >= 2:
        moved3 = both[-1][2]['x'] - both[0][2]['x']
        moved2 = abs(both[-1][1]['x'] - both[0][1]['x'])
        check(moved3 > 3.0, f'P3 kept running during it ({moved3:.1f} m)')
        check(moved2 < 0.5, f'P2 stayed in place while choosing ({moved2:.2f} m)')
    if MODE == 'coop':
        diffs = []
        for a in r1:
            for rr in (r2, r3):
                b = at(rr, a['T'])
                if b is not None and a['t'] > 3: diffs.append(abs(a['hud'] - b['hud']))
        if diffs: check(max(diffs) < 15.0, f'COOP: the 3 screens show about the same team distance (max diff {max(diffs):.1f} m, mean {sum(diffs)/len(diffs):.1f} m; rows within 0.35 s)')
    else:
        for n, (rows, _) in P.items():
            d = max(abs(r['hud'] - r['own']) for r in rows)
            check(d < 0.05, f'{n}: VERSUS shows own distance (max |hud-own| {d:.2f})')

# P2 の選択中の保護
l2 = ''.join(P['p2'][1]); l1 = ''.join(P['p1'][1])
for m in re.finditer(r'CHOICE PROBE me=P\d+ hp (\d+)->(\d+) claimRejected=(\d+)', l2):
    # HP が減らなければよい(申告は JOIN の端末で止まる時は HOST に届かず、拒否の数は 0 のことがある)
    check(m.group(1) == m.group(2), f'P2 while choosing: a hit and a direct claim did nothing (hp {m.group(1)}->{m.group(2)}, HOST rejected {m.group(3)})')
for m in re.finditer(r'CHOICE CLOSED me=P\d+ after ([\d.]+)s x=([\d.-]+) maxMove=([\d.]+)', l2):
    res.append(f'INFO P2 choice closed after {m.group(1)}s, largest move while choosing {m.group(3)} m')
fall = re.findall(r'choice OVER PIT: moved ([\d.]+)m', l2)
if fall: res.append(f'INFO P2 was put over a pit when the choice opened (moved {fall[0]} m)')
downs = [l for l in P['p1'][1] if 'P2 DOWN' in l or ('Damage P2' in l and 'while choosing card' in l)]
check(len(downs) == 0, f'P2 not damaged / DOWN while choosing (HOST log lines: {len(downs)})')
foot = re.findall(r'no footing under the player after the card choice[^\n]{0,60}', l2)
if foot: res.append('INFO P2 after the choice: ' + foot[0])

# ボス報酬
for n, (_, lines) in P.items():
    k = sum(1 for l in lines if 'TEAMHOLD boss reward choice opened' in l or 'Boss reward offered to' in l)
    if k: res.append(f'INFO {n}: boss reward choice opened {k} time(s)')

print('\n'.join(res))
ng = sum(1 for r in res if r.startswith('NG'))
print(f'RESULT {"PASS" if ng == 0 else "FAIL"} ng={ng}')

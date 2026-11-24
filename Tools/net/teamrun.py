# -*- coding: utf-8 -*-
# 2台のマルチの記録(TEAMLOG)を実時刻でそろえて比べる(2026-11-24)。使い方: python teamrun.py <dir with host.log/join.log> coop|versus
#  ・画面の距離(hud)が下がらないか / 0.5 秒で不自然に跳ばないか
#  ・協力: HOST と JOIN の画面の距離の差(同じ時刻の近い記録どうし)
#  ・対戦: 画面の距離 = 各自の距離(own)のままか
#  ・相手がカード選択/一時停止の間、こちらは進み続けたか(こちらがボスの闘技場で止まっている時を除く)
#  ・ボスを倒した後(bosses=0)に足止め(hold)が残っていないか / HOST の時間(ts)が 0 になっていないか
import io, os, re, sys
from datetime import datetime

D = sys.argv[1]; MODE = sys.argv[2] if len(sys.argv) > 2 else 'coop'
pat = re.compile(r'TEAMLOG (.*)')
def parse(path):
    rows = []
    if not os.path.exists(path): return rows
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = pat.search(line)
        if not m: continue
        kv = dict(p.split('=', 1) for p in m.group(1).split() if '=' in p)
        try: kv['T'] = datetime.strptime(kv['utc'], '%H:%M:%S.%f')
        except Exception: continue
        for k in ('x', 'raw', 'hud', 'own', 'front', 'haltF', 'ts', 't'):
            if k in kv: kv[k] = float(kv[k])
        for k in ('holding', 'choosing', 'paused', 'hold', 'phase'):
            kv[k] = kv.get(k) == 'True'
        kv['bosses'] = int(kv.get('bosses', '0'))
        rows.append(kv)
    return rows

H = parse(os.path.join(D, 'host.log')); J = parse(os.path.join(D, 'join.log'))
res = []
def check(ok, msg): res.append(('OK  ' if ok else 'NG  ') + msg)

for name, rows in (('HOST', H), ('JOIN', J)):
    if not rows: check(False, f'{name}: no TEAMLOG'); continue
    back = max((a['hud'] - b['hud'] for a, b in zip(rows, rows[1:])), default=0)
    jump = max((b['hud'] - a['hud'] for a, b in zip(rows, rows[1:])), default=0)
    check(back < 0.05, f'{name}: screen distance never goes back (largest drop {back:.2f} m)')
    check(jump < 30.0, f'{name}: no sudden jump in 0.5 s (largest step {jump:.1f} m)')
    if MODE == 'versus':
        diff = max(abs(r['hud'] - r['own']) for r in rows)
        check(diff < 0.05, f'{name}: VERSUS shows own distance (max |hud-own| {diff:.2f})')
    # 1回だけの ts=0 は命中の一瞬の停止(HitStop、約0.1秒)。0.5 秒以上続けて止まった所を数える
    zero = [r['ts'] < 0.05 and not r['paused'] for r in rows]
    runs = sum(1 for a2, b2, c2 in zip(zero, zero[1:], zero[2:]) if a2 and b2 and c2)
    check(runs == 0, f'{name}: the shared world never stopped for 1 s or more (3+ consecutive ts=0 samples {runs}, single hit-stop samples {sum(zero)})')

# 協力: 同じ時刻の画面の距離の差
if MODE == 'coop' and H and J:
    diffs = []
    for r in J:
        h = min(H, key=lambda q: abs((q['T'] - r['T']).total_seconds()))
        if abs((h['T'] - r['T']).total_seconds()) <= 0.3 and r['t'] > 3: diffs.append(abs(h['hud'] - r['hud']))
    if diffs:
        tail = diffs[-10:]
        check(max(diffs) < 40.0, f'COOP: HOST/JOIN screen distance differ at most {max(diffs):.1f} m (mean {sum(diffs)/len(diffs):.1f} m, last 10 max {max(tail):.1f} m)')
        check(max(tail) < 15.0, f'COOP: they agree at the end (last 10 samples max {max(tail):.1f} m)')

# 相手が選択中/一時停止の間に、こちらが進み続けたか
def moved_while(other, me, label):
    wins = [r for r in other if r['choosing'] or r['paused']]
    if not wins: return
    t0, t1 = wins[0]['T'], wins[-1]['T']
    mine = [r for r in me if t0 <= r['T'] <= t1]
    free = [r for r in mine if not r['holding'] and not r['choosing'] and not r['paused']]
    if len(free) < 2: check(True, f'{label}: other side choosing/paused {(t1-t0).total_seconds():.1f}s (this side was in the boss arena or choosing - not measured)'); return
    dx = free[-1]['x'] - free[0]['x']
    check(dx > 3.0, f'{label}: kept running while the other side chose/paused ({(t1-t0).total_seconds():.1f}s, moved {dx:.1f} m outside the arena)')
moved_while(H, J, 'JOIN'); moved_while(J, H, 'HOST')

for name, rows in (('HOST', H), ('JOIN', J)):
    if not rows: continue
    halted = sum(1 for r in rows if r['holding'])
    after = 0; seen_kill = False
    for a, b in zip(rows, rows[1:]):
        if a['bosses'] > 0 and b['bosses'] == 0: seen_kill = True
        if seen_kill and b['holding'] and b['bosses'] == 0: after += 1
    res.append(f'INFO {name}: arena-halt samples {halted}, halted after the boss died {after}, hud {rows[0]["hud"]:.0f}->{rows[-1]["hud"]:.0f} own {rows[-1]["own"]:.0f} dupOffers {rows[-1].get("dup")}')
    if seen_kill: check(after <= 5, f'{name}: not held after the boss died beyond the ~2 s defeat animation ({after} samples of 0.5 s)')

print('\n'.join(res))
ng = sum(1 for r in res if r.startswith('NG'))
print(f'RESULT {"PASS" if ng == 0 else "FAIL"} ng={ng}')

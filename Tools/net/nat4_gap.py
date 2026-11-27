import re, sys
# HOST(P1)の記録から、自分と先頭(生きていて選択中でない人)の差を時間で追い、500m を超えていた時の状態を数える
#  python -I -X utf8 Tools/net/nat4_gap.py <p1_host.log> [基準 m]
f = sys.argv[1]; th = float(sys.argv[2]) if len(sys.argv) > 2 else 500.0
rows = []
for line in open(f, encoding='utf-8', errors='replace'):
    if 'NAT utc=' not in line or ' role=HOST' not in line: continue
    mt = re.search(r' t=([0-9.]+) ', line); mx = re.search(r' role=HOST x=([0-9.]+)', line); ms = re.search(r' st=([A-Za-z]+) char', line)
    if not (mt and mx): continue
    me = float(mx.group(1)); st = ms.group(1) if ms else '?'
    others = [(float(a), b) for a, b in re.findall(r' P\d\[shown=1 x=([0-9.]+) .*? st=([A-Za-z+]+)\]', line)]
    alive = [x for x, s in others if s == 'Alive']
    if not alive: continue
    rows.append((float(mt.group(1)), max(alive) - me, st))
over = [r for r in rows if r[1] >= th]
print(f'記録 {len(rows)} 回(0.5秒ごと)、差の最大 {max(r[1] for r in rows):.0f}m、{th:.0f}m 以上だった記録 {len(over)} 回')
from collections import Counter
print('  その時の自分の状態:', dict(Counter(r[2] for r in over)))
runs = []; cur = None
for t, g, s in rows:
    if g >= th:
        if cur is None: cur = [t, t, g, s]
        else: cur[1] = t; cur[2] = max(cur[2], g)
    elif cur is not None: runs.append(cur); cur = None
if cur is not None: runs.append(cur)
print(f'  {th:.0f}m 以上が続いた区間 {len(runs)} 回、長さ(秒) {[round(b - a, 1) for a, b, g, s in runs][:20]}')
print(f'  最後の記録: t={rows[-1][0]:.1f} 差 {rows[-1][1]:.0f}m 状態 {rows[-1][2]}')

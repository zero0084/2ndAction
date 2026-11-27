import re, sys, os
# HOST の記録: ① 関門の値 と 始まった時の最前の距離の差 ② 最前の距離(front=)の跳び ③ 開いた/閉じた区間 ④ 安全装置
D = sys.argv[1]
f = os.path.join(D, 'p1_host.log')
prev = None; jumps = []; gates = []; segs = []; safety = 0; opened = 0; closed = 0
for line in open(f, encoding='utf-8', errors='replace'):
    m = re.search(r'\[BossRun\] Gate (\d+)m \(multiplayer.*?front=(\d+)m', line)
    if m: gates.append((int(m.group(1)), int(m.group(2))))
    if 'boss segment opened' in line: opened += 1
    if 'boss segment closed' in line: closed += 1; segs.append(line.strip()[:120])
    if 'closed at' in line and 'safety' in line: safety += 1
    if 'NAT utc=' in line and ' role=HOST' in line:
        mt = re.search(r' t=([0-9.]+) ', line); mf = re.search(r' front=([0-9.]+)', line)
        if mt and mf:
            t, fr = float(mt.group(1)), float(mf.group(1))
            if prev and t - prev[0] < 2.0 and fr - prev[1] > 60: jumps.append((prev[0], prev[1], t, fr))
            prev = (t, fr)
print(f'関門 {len(gates)} 回: ' + ' '.join(f'{g}(最前 {fr}, +{fr - g})' for g, fr in gates))
print(f'区間 開いた {opened} / 閉じた {closed} / 安全装置で閉じた {safety}')
print(f'最前の距離の跳び(2秒以内に 60m 超): {len(jumps)} 回 ' + ' '.join(f'[t={a:.1f} {b:.0f} -> t={c:.1f} {d:.0f}]' for a, b, c, d in jumps[:8]))

import re, sys, os, glob, bisect
# ボス戦ごとに、4人の「止まった位置(論理X)」「表示の距離(HUD)」を比べる
D = sys.argv[1]
def tsec(s):
    h, m, x = s.split(':'); return int(h) * 3600 + int(m) * 60 + float(x)
P = {}
for f in sorted(glob.glob(os.path.join(D, 'p*_*.log'))):
    pn = int(re.match(r'p(\d)', os.path.basename(f)).group(1))
    ev = []; nat = []; lastu = None; pend = []; day = 0
    for line in open(f, encoding='utf-8', errors='replace'):
        m = re.search(r'\[NETTEST\] utc=(\S+)', line)
        if m:
            u = tsec(m.group(1))
            if lastu is None: day = u
            if u < day - 3600: u += 86400
            lastu = u
            for p in pend: ev.append((u,) + p)
            pend = []
            mm = re.search(r'NAT utc=\S+ t=(\S+) .*? x=(\S+) y=\S+ .*?own=(\S+) hud=(\S+) spd=(\S+)', line)
            if mm: nat.append((u, float(mm.group(2)), float(mm.group(4)), float(mm.group(5)), float(mm.group(3))))
            continue
        mg = re.match(r'\[BossRun\] Gate (\d+)m', line)
        if mg: pend.append(('gate', mg.group(1)))
        if line.startswith('[BossHalt] hold'): pend.append(('hold', line.strip()))
        elif line.startswith('[BossHalt] stopped'): pend.append(('stopped', line.strip()))
        elif line.startswith('[BossHalt] release'): pend.append(('release', line.strip()))
    P[pn] = dict(ev=ev, nat=nat)
def at(pn, u):
    n = P[pn]['nat']; i = bisect.bisect_left([a[0] for a in n], u)
    if i >= len(n): i = len(n) - 1
    return n[i]
host = P[1]
holds = [e for e in host['ev'] if e[1] == 'hold']
rels = [e for e in host['ev'] if e[1] == 'release']
print(f'HOST の止める遭遇 {len(holds)} 回')
rows = []
for k, h in enumerate(holds):
    u0 = h[0]; r = next((x for x in rels if x[0] > u0), None)
    if r is None: continue
    u1 = r[0]; um = u1 - 1.0  # 解除の1秒前(全員が戦っているはず)
    gates = [e for e in host['ev'] if e[1] == 'gate' and e[0] <= u0 + 1]
    gate = gates[-1][2] if gates else '?'
    line = f'#{k+1:02d} 関門 {gate:>6} 長さ {u1-u0:5.1f}s |'
    xs = []; huds = []
    for pn in sorted(P):
        if not P[pn]['nat'] or P[pn]['nat'][-1][0] < um or P[pn]['nat'][0][0] > um: line += f' P{pn} -'; continue
        uu, x, hud, spd, own = at(pn, um)
        later = at(pn, u1 + 10.0)
        P[pn].setdefault('after', {})[k] = later[4]
        # この人の止まり方(遭遇の間の stopped/slide)
        st = [e for e in P[pn]['ev'] if u0 - 30 <= e[0] <= u1 and e[1] == 'stopped']
        slide = re.search(r'slide=(-?[0-9.]+)', st[0][2]).group(1) if st else '-'
        xs.append(x); huds.append(hud)
        line += f' P{pn} x={x:9.1f} own={own:9.1f} hud={hud:9.1f} spd={spd:4.1f} slide={slide}'
    if len(xs) > 1:
        aft = [P[q]['after'][k] for q in sorted(P) if k in P[q].get('after', {})]
        line += f' || 位置の幅 {max(xs)-min(xs):6.1f}m 表示距離の幅 {max(huds)-min(huds):6.1f}m 10秒後の個人距離の幅 {max(aft)-min(aft) if aft else -1:6.1f}m'
        rows.append((max(xs)-min(xs), max(huds)-min(huds)))
    print(line)
if rows:
    import statistics as st
    a = sorted(r[0] for r in rows); b = sorted(r[1] for r in rows)
    print(f'まとめ: 位置の幅 中央 {st.median(a):.1f}m 最大 {a[-1]:.1f}m / 表示距離の幅 中央 {st.median(b):.1f}m 最大 {b[-1]:.1f}m (n={len(rows)})')

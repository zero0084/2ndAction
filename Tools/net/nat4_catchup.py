import re, sys, os, glob
# 合流補助(2026-11-28)の照合: python -I -X utf8 Tools/net/nat4_catchup.py <run フォルダ>
#  ・HOST の [CATCHUP] の指示(誰を/何 m 遅れ/どこへ/ボス戦の並びへ か)
#  ・本人の [NET][WARPAUDIT](移動の前後で距離/EXP/距離の MILE/累計が増えていないか)
#  ・本人のボス戦: 並んで止まった回数(BossHalt stopped)と、止まる遭遇の数(hold)
#  ・同じ人が短い間に何度も移されていないか(移動だけの繰り返し)
D = sys.argv[1]
host = os.path.join(D, 'p1_host.log')
orders = {}
for line in open(host, encoding='utf-8', errors='replace'):
    m = re.search(r'\[CATCHUP\] P(\d) (\d+)m behind P(\d)(.*?) -> place at (-?\d+) \(moved (-?\d+)m, #(\d+)\)', line)
    if m: orders.setdefault(int(m.group(1)), []).append((int(m.group(2)), m.group(4).strip(), int(m.group(6))))
    m2 = re.search(r'\[CATCHUP\] P(\d) .* no safe footing', line)
    if m2: orders.setdefault(int(m2.group(1)), []).append((-1, 'no safe footing', 0))
print('# 合流補助の照合:', os.path.basename(os.path.normpath(D)))
for f in sorted(glob.glob(os.path.join(D, 'p*_*.log'))):
    pn = int(re.match(r'p(\d)', os.path.basename(f)).group(1))
    audits = []; holds = stopped = 0; toasts = 0; rejoins = []; kills = 0; picks = {}
    for line in open(f, encoding='utf-8', errors='replace'):
        if '[NET][WARPAUDIT]' in line: audits.append(line.split('[NET][WARPAUDIT]', 1)[1].strip())
        if line.startswith('[BossHalt] hold'): holds += 1
        if line.startswith('[BossHalt] stopped'): stopped += 1
        if 'UI: 仲間に合流しました' in line: toasts += 1
        if '[NET][REJOIN] P' in line and 'placed at' in line: rejoins.append(line.strip()[:90])
        m = re.search(r'NATEV choice pick .* card=(\S+)', line)
        if m: picks[m.group(1)] = picks.get(m.group(1), 0) + 1
    # 監査の形: "rejoin moved Xm -> distance +Am, EXP +B, distance MILE base +C, ledger walked +Dm"(移動の前から2フレーム後まで。2フレームの普通の走りの分 = 1m 前後は正常)
    bad = []
    for a in audits:
        m = re.search(r'moved (-?[0-9.]+)m -> distance \+(-?[0-9.]+)m, EXP \+(-?[0-9.]+), distance MILE base \+(-?[0-9.]+), ledger walked \+(-?[0-9.]+)m', a)
        if m and (float(m.group(2)) > 3.0 or float(m.group(4)) > 0.05 or float(m.group(5)) > 3.0): bad.append(a)
    od = orders.get(pn, [])
    moved = [o for o in od if o[0] >= 0]
    print(f'P{pn}: 合流補助 {len(moved)} 回(足場なしで見送り {len(od) - len(moved)})'
          f' 遅れ {[o[0] for o in moved][:12]} 移動 {[o[2] for o in moved][:12]} ボスの並びへ {sum(1 for o in moved if "boss" in o[1])}'
          f' | 知らせ {toasts} | ボス戦で止まる遭遇 {holds} / 並んで止まった {stopped}'
          f' | 移動の監査 {len(audits)} 件、距離/EXP/MILE が増えた {len(bad)} 件'
          f' | 速度のカード {{"speed_up": {picks.get("speed_up", 0)}, "speed_down": {picks.get("speed_down", 0)}}}')
    for a in bad[:3]: print('   増えた:', a[:200])

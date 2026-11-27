import re, sys, os, glob, collections
# 2026-11-28 依頼⑥ の比較: python -I -X utf8 Tools/net/brk1128.py <brk の出力フォルダ>
#  各 <char>_<before|after>/run.log から、ボスごとの 撃破/時間/BREAK 回数/攻撃した回数/怒りで溜まらなかった一撃 を集める
D = sys.argv[1]
rows = []
for f in sorted(glob.glob(os.path.join(D, '*_*', 'run.log'))):
    tag = os.path.basename(os.path.dirname(f))
    ch, mode = tag.rsplit('_', 1)
    cur = None
    for line in open(f, encoding='utf-8', errors='replace'):
        m = re.search(r'\[BossBattle\] (.+?) defeated phase=(\d+) breaks=(\d+)(?: ultimates=\d+)? attacks=(\d+) t=([0-9.\-]+)s', line)
        if m: rows.append((ch, mode, m.group(1)[:22], 'killed', float(m.group(5)), int(m.group(3)), int(m.group(4)), int(m.group(2)))); continue
        m = re.search(r'\[diag result\] (.*)', line)
        if m and ('killed=False' in m.group(1) or 'killed=0' in m.group(1)):
            rows.append((ch, mode, m.group(1)[:30], 'NOT KILLED', -1, -1, -1, -1))
print('char\tmode\tboss\tresult\tsec\tbreaks\tattacks\tphase')
for r in rows: print('\t'.join(str(x) for x in r))
agg = collections.defaultdict(list)
for ch, mode, boss, res, sec, br, at, ph in rows:
    if res == 'killed': agg[(boss, ch, mode)].append((sec, br, at))
print('\n# 平均(ボス × キャラ × 変更前/後): 秒 / BREAK / 攻撃')
for k in sorted(agg):
    v = agg[k]; n = len(v)
    print(f'{k[0]}\t{k[1]}\t{k[2]}\tn={n}\t{sum(x[0] for x in v)/n:.1f}s\tBREAK {sum(x[1] for x in v)/n:.1f}\tattacks {sum(x[2] for x in v)/n:.1f}')

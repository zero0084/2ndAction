import re, sys, os, glob, collections
# 2026-11-28 第3弾の試験の照合: python -I -X utf8 Tools/net/q1128.py <run フォルダ>
#  ③ 死神による脱落 / PHOENIX / 最終進化 / 普通の致命傷 / ④ HOST のクリア / RESULT・保存・ホーム / ② 順位の行
D = sys.argv[1]
print('#', os.path.basename(os.path.normpath(D)))
host = os.path.join(D, 'p1_host.log')
KEYS = [
    ('elim_reaper', r'ELIMINATED by the reaper'), ('elim_vs', r'ELIMINATED \(VERSUS\)'), ('down', r'DOWN \(CO-OP\)'),
    ('revive', r'\] REVIVE P\d by P\d|REVIVE P\d by'), ('host_lethal_reaper', r'\[NET\]\[REAPER\] lethal reaper hit'),
    ('phoenix', r'\[Phoenix\] revived'), ('fe_revive', r'PHOENIX emergency revive'), ('clear', r'HOST CLEAR declared'),
    ('clear_local', r'HOST CLEAR via'), ('runover', r'RUN OVER mode='), ('result_row', r'RESULT #\d'),
]
def scan(f):
    ev = collections.defaultdict(list)
    for line in open(f, encoding='utf-8', errors='replace'):
        for k, p in KEYS:
            if re.search(p, line): ev[k].append(line.strip()[:230])
        if 'Damage P' in line and '(reaper)' in line: ev['reaper_dmg'].append(line.strip()[:200])
        if 'src=HPZero:BossCombatPart' in line and 'Reaper' in line: ev['reaper_hit'].append(line.strip()[:200])
        if 'NATRESULT' in line: ev['natresult'].append(re.sub(r'.*NATRESULT', 'NATRESULT', line.strip())[:260])
        if 'NATEV home reached' in line: ev['home'].append(re.sub(r'.*NATEV', 'NATEV', line.strip())[:160])
        if ' COND ' in line and ('(changed)' in line or 'KILL' in line or 'final evolution' in line or 'card ' in line):
            ev['cond'].append(re.sub(r'.*\] COND', 'COND', line.strip())[:260])
        m = re.search(r" rank='([^']*)'", line)
        if m and m.group(1): ev['rank'].append(m.group(1))
        if 'Exception' in line and 'NullReference' in line or 'Exception:' in line: ev['exc'].append(line.strip()[:200])
    return ev
for f in sorted(glob.glob(os.path.join(D, 'p*_*.log'))):
    ev = scan(f)
    me = ''
    for line in open(f, encoding='utf-8', errors='replace'):
        m = re.search(r' me=(P\d) ', line)
        if m: me = m.group(1); break
    print(f'## {os.path.basename(f)} ({me})')
    for k in ['host_lethal_reaper', 'elim_reaper', 'elim_vs', 'down', 'revive', 'phoenix', 'fe_revive', 'clear', 'clear_local', 'runover']:
        if ev[k]: print(f'  {k}: {len(ev[k])}  first: {ev[k][0][-170:]}')
    for c in ev['cond'][:14]: print('  ', c[:240])
    ranks = ev['rank']
    if ranks:
        cnt = collections.Counter(re.sub(r'\d[\d,]*m', 'Nm', r) for r in ranks)
        print(f'  rank samples {len(ranks)}: first {ranks[0]!r} last {ranks[-1]!r}; kinds {dict(cnt.most_common(6))}')
    for r in ev['result_row'][:6]: print('  ', r[-120:])
    for r in ev['natresult']: print('  ', r[:250])
    for h in ev['home'][:2]: print('  ', h)
    if ev['exc']: print(f'  exceptions {len(ev["exc"])}: {ev["exc"][0][:160]}')

import re,sys,collections
L=open(sys.argv[1],encoding='utf-8',errors='replace').read().split('\n')
last=''; c=collections.Counter(); ex=collections.defaultdict(list)
for l in L:
    m=re.match(r'(\d\d:\d\d:\d\d)\.\d+ (\S+)',l)
    if m: last=m.group(2)
    if l.startswith('[NET][PLAYER] [REJOIN]') and 'REJOIN P' in l: last='REJOIN-'+re.search(r'REJOIN (P\d)',l).group(1)
    if '[Encounter] BossStart' in l: last='EncBossStart'
    if '[Encounter] BossEnd' in l: last='EncBossEnd'
    if 'alive, removed by host' in l:
        d=dict(re.findall(r'(\w+)=(-?[0-9.]+)',l))
        if 'enemyX' not in d: continue
        e,r,f=float(d['enemyX']),float(d['rearPlayerX']),float(d['frontPlayerX'])
        if r-0.5<=e<=f+60:
            c[last]+=1
            if len(ex[last])<2: ex[last].append(l[:160])
print(c)
for k,v in ex.items(): print(k,v)

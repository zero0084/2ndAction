# 4人マルチの通常条件の通し試験(2026-11-27)の記録の照合。 python -I Tools/net/nat4.py <run フォルダ> [restart の記録のフォルダ]
#  ・位置: 見ている端末に表示された他の人の位置 と その人の本当の位置(本人の記録を時刻で補間、表示の遅れ lag を引いた時刻)の差
#    一時的な差(通信の遅れ/補間)と、残り続けるずれ(2秒以上 2.5m 超)を分ける。倒れた/脱出中/置き直しの瞬間は除く
#  ・敵: HOST(正しい位置)と参加者の表示を同じ ID・同じ時刻で比べる
#  ・HP: 他の人の画面の表(HP/最大HP)と本人の値。混ざり/食い違いが2秒以上続いたら記録
#  ・地面: 本人が接地中に地面より下(埋まり)/見えている他の人が接地の印なのに浮いている
#  ・MILE: RESULT の表示額・所持 と 保存の値(ラン前→RESULT→ホーム→最後→再起動後)
import re, sys, os, glob, bisect, statistics as st
D = sys.argv[1]
RD = sys.argv[2] if len(sys.argv) > 2 else D
def tsec(s):
    h, m, x = s.split(':'); return int(h) * 3600 + int(m) * 60 + float(x)
NUM = r'(-?[0-9.]+|NaN)'
logs = sorted(glob.glob(os.path.join(D, 'p*_*.log')))
P = {}
for f in logs:
    pn = int(re.match(r'p(\d)', os.path.basename(f)).group(1))
    P[pn] = dict(file=f, nat=[], ent=[], ev=[], snap=[], res=[], summ=None, exc=0, kill=[], net=[], reward=[], seq=[])
    for line in open(f, encoding='utf-8', errors='replace'):
        if 'Exception' in line and ('Exception:' in line or line.startswith('NullReference')): P[pn]['exc'] += 1
        # 2026-11-27: MILE の流れ(順番どおり): チャレンジの達成 / 確定の控え / RESULT の表示 / ホーム
        mc = re.search(r'\[Challenge\] COMPLETE (\S+) .*?\+(\d+) MILE', line)
        if mc: P[pn]['seq'].append(('chal', int(mc.group(2)), mc.group(1)))
        mj = re.search(r'\[CommitJournal\] written run=\S+ commit=(\S+) mile (-?\d+)->(-?\d+)', line)
        if mj: P[pn]['seq'].append(('commit', int(mj.group(2)), int(mj.group(3))))
        mr = re.search(r'\[Escape\].*RESULT shown win=(\S+) .* mile=(\d+)', line)
        if mr: P[pn]['seq'].append(('shown', mr.group(1) == 'True', int(mr.group(2))))
        mm = re.search(r'NATSNAP tag=(home|before) .*? mile=(\d+)', line)
        if mm: P[pn]['seq'].append((mm.group(1), int(mm.group(2))))
        mn = re.search(r'NATRESULT run=(\d+) .*?kind=(.+?) shownMile=(\d+) walletShown=(\d+) savedMile=(-?\d+)', line)
        if mn: P[pn]['seq'].append(('natresult', mn.group(2), int(mn.group(3)), int(mn.group(4)), int(mn.group(5))))
        if '[NETTEST]' not in line and '[NET]' not in line: continue
        m = re.search(r'\[NETTEST\] utc=(\S+) (.*)', line)
        if not m:
            if '[NET][WARPAUDIT]' in line or 'REJOIN' in line or '[NET][REJOIN' in line: P[pn]['net'].append(line.strip()[:240])
            continue
        u, body = tsec(m.group(1)), m.group(2)
        if body.startswith('NAT utc='):
            d = {'u': tsec(re.search(r'NAT utc=(\S+)', body).group(1))}
            head = body.split(' P')[0] if ' P' in body else body
            for k, v in re.findall(r'(\w+)=(\S+)', body.split(' P2[')[0].split(' P1[')[0].split(' P3[')[0].split(' P4[')[0]):
                d[k] = v
            d['others'] = {}
            for o in re.finditer(r' P(\d)\[([^\]]*)\]', body):
                od = dict(re.findall(r'(\w+)=(\S+)', o.group(2)))
                d['others'][int(o.group(1))] = od
            P[pn]['nat'].append(d)
        elif body.startswith('NATENT'):
            ents = {}
            for e in re.finditer(r' (\d+):([BE]):(-?[0-9.]+):(-?[0-9.]+)', body): ents[int(e.group(1))] = (float(e.group(3)), float(e.group(4)), e.group(2))
            P[pn]['ent'].append((u, 'auth=1' in body, ents))
        elif body.startswith('NATEV'): P[pn]['ev'].append((u, body[6:]))
        elif body.startswith('NATSNAP'): P[pn]['snap'].append(dict(re.findall(r'(\w+)=(\S+)', body)))
        elif body.startswith('NATRESULT'): P[pn]['res'].append(dict(re.findall(r'(\w+)=(\S+)', body)) | {'raw': body})
        elif body.startswith('SUMMARY'): P[pn]['summ'] = body[:160]
        elif body.startswith('KILL '): P[pn]['kill'].append(body)
        elif body.startswith('REWARD'): P[pn]['reward'].append(body)
        elif body.startswith('NATBOOT') or body.startswith('NATSEED'): P[pn]['ev'].append((u, body))
fl = lambda v: float('nan') if v in (None, 'NaN') else float(v)

print('# 4人の通し試験の照合:', os.path.basename(os.path.normpath(D)))
for pn in sorted(P):
    p = P[pn]
    print(f"P{pn}: {os.path.basename(p['file'])} samples={len(p['nat'])} exceptions={p['exc']} | {p['summ']}")

# ---------------------------------------------------------------- 位置
def truth_series(pn, run=None):
    xs = []
    for d in P[pn]['nat']:
        if 'x' not in d or d.get('st') in (None, 'over', 'none'): continue
        xs.append((d['u'], fl(d['x']), fl(d['y']), d.get('st'), fl(d.get('gnd', 'NaN')), d.get('grounded'), d.get('spd', '0')))
    return xs
TR = {pn: truth_series(pn) for pn in P}
def interp(series, u):
    us = [s[0] for s in series]
    i = bisect.bisect_left(us, u)
    if i <= 0 or i >= len(series): return None
    a, b = series[i - 1], series[i]
    if b[0] - a[0] > 1.2 or abs(b[1] - a[1]) > 40: return None  # 記録の切れ目/瞬間移動
    w = (u - a[0]) / (b[0] - a[0])
    return (a[1] + (b[1] - a[1]) * w, a[2] + (b[2] - a[2]) * w, a[3], b[3], a, b)
print('\n## 位置(見ている端末 → 他の人の表示と本当の位置の差)')
allerr = []; persist = []
for v in sorted(P):
    for q in sorted(P):
        if q == v: continue
        errs = []; dyl = []; run_bad = 0; bad_start = None; worst = 0
        for d in P[v]['nat']:
            o = d['others'].get(q)
            if not o or o.get('shown') != '1': run_bad = 0; continue
            lag = fl(o.get('lag', '0')) / 1000.0
            t = interp(TR[q], d['u'] - lag)
            if t is None or t[2] in ('down', 'escaping') or t[3] in ('down', 'escaping'): run_bad = 0; continue
            ex = abs(fl(o['x']) - t[0]); ey = abs(fl(o['y']) - t[1])
            e = (ex * ex + ey * ey) ** 0.5
            errs.append(e)
            spd = fl(t[4][6]) if len(t[4]) > 6 else 0.0
            tolx = max(2.5, 0.2 * spd)  # 高速では 0.1〜0.2秒の表示の遅れの差だけで数m(一時的な差)
            dyl.append(ey); 
            if ex > tolx or ey > 1.5:
                run_bad += 1
                if run_bad == 1: bad_start = (d['u'], d.get('t'))
                worst = max(worst, e)
                if run_bad == 4: persist.append((v, q, bad_start, e))
            else: run_bad = 0
        if errs:
            errs.sort(); allerr += errs
            dyl.sort()
            print(f"  P{v}→P{q}: n={len(errs)} 中央 {st.median(errs):.2f}m p95 {errs[int(len(errs)*0.95)-1]:.2f}m 最大 {errs[-1]:.2f}m / 上下だけ p95 {dyl[int(len(dyl)*0.95)-1]:.2f}m 最大 {dyl[-1]:.2f}m")
if persist:
    print('  残り続けるずれ(2秒以上、前後 max(2.5m, 0.2秒分) か 上下 1.5m を超える):')
    for v, q, s0, e in persist[:30]: print(f"    P{v}→P{q} t={s0[1]} 差 {e:.1f}m")
else: print('  残り続けるずれ(2秒以上、前後 max(2.5m, 0.2秒分) か 上下 1.5m を超える): なし')

# ---------------------------------------------------------------- 敵
print('\n## 敵の位置(HOST と参加者の表示、同じ ID)')
host = [pn for pn in P if any(a for _, a, _ in P[pn]['ent'])]
if host:
    h = host[0]
    hser = {}
    for u, a, ents in P[h]['ent']:
        for i, (x, y, k) in ents.items(): hser.setdefault(i, []).append((u, x, y))
    for v in sorted(P):
        if v == h: continue
        errs = []
        for u, a, ents in P[v]['ent']:
            for i, (x, y, k) in ents.items():
                s = hser.get(i)
                if not s or len(s) < 2: continue
                best = None
                for lag in (0.0, 0.1, 0.2):
                    us = [z[0] for z in s]; j = bisect.bisect_left(us, u - lag)
                    if j <= 0 or j >= len(s) or s[j][0] - s[j-1][0] > 1.5: continue
                    w = (u - lag - s[j-1][0]) / (s[j][0] - s[j-1][0])
                    hx = s[j-1][1] + (s[j][1] - s[j-1][1]) * w; hy = s[j-1][2] + (s[j][2] - s[j-1][2]) * w
                    e = ((hx - x) ** 2 + (hy - y) ** 2) ** 0.5
                    best = e if best is None else min(best, e)
                if best is not None: errs.append(best)
        if errs:
            errs.sort()
            big = sum(1 for e in errs if e > 3)
            print(f"  P{v}: n={len(errs)} 中央 {st.median(errs):.2f}m p95 {errs[int(len(errs)*0.95)-1]:.2f}m 最大 {errs[-1]:.2f}m 3m超 {big}")

# ---------------------------------------------------------------- 地面
print('\n## 地面(埋まり/浮き)')
for pn in sorted(P):
    emb = []; n = 0
    for d in P[pn]['nat']:
        if d.get('grounded') != '1' or d.get('st') in (None, 'over', 'down'): continue
        g = fl(d.get('gnd')); y = fl(d.get('y'))
        if g != g: continue
        n += 1
        if y - g < -0.3: emb.append((d.get('t'), round(y - g, 2)))
    remb = []; rfloat = []; rn = 0
    for d in P[pn]['nat']:
        for q, o in d['others'].items():
            if o.get('shown') != '1' or o.get('ag') != '1': continue
            g = fl(o.get('gnd')); y = fl(o.get('y'))
            if g != g: continue
            rn += 1
            t = interp(TR[q], d['u'] - fl(o.get('lag', '0')) / 1000.0)
            if y - g < -0.4: remb.append((q, d.get('t'), round(y - g, 2)))
            if y - g > 0.8 and t is not None and t[4][5] == '1' and abs(t[4][2] - t[4][4]) < 0.2: rfloat.append((q, d.get('t'), round(y - g, 2)))
    print(f"  P{pn}: 自分の接地 {n} 回のうち埋まり {len(emb)} {emb[:5]} / 他の人の表示 {rn} 回のうち埋まり {len(remb)} {remb[:4]} 浮き {len(rfloat)} {rfloat[:4]}")

# ---------------------------------------------------------------- HP の混ざり
print('\n## HP/最大HP(他の人の画面の表 と 本人の値)')
for q in sorted(P):
    own = [(d['u'], d.get('hp')) for d in P[q]['nat'] if 'hp' in d]
    us = [a for a, _ in own]
    for v in sorted(P):
        if v == q: continue
        mism = 0; n = 0; streak = 0; worst = []
        for d in P[v]['nat']:
            o = d['others'].get(q)
            if not o or 'hp' not in o or o['hp'].startswith('-1'): continue
            j = bisect.bisect_left(us, d['u'])
            cand = [own[k][1] for k in (j - 2, j - 1, j, j + 1) if 0 <= k < len(own)]
            n += 1
            if o['hp'] not in cand:
                streak += 1
                if streak == 5: mism += 1; worst.append((d.get('t'), o['hp'], cand[-1] if cand else '?'))
            else: streak = 0
        if n: print(f"  P{v} が見た P{q}: {n} 回、2秒以上の食い違い {mism} 回 {worst[:3]}")

# ---------------------------------------------------------------- 出来事
print('\n## 出来事(人ごと)')
for pn in sorted(P):
    evs = P[pn]['ev']
    cnt = {}
    for _, e in evs:
        k = ' '.join(e.split()[:2]) if not e.startswith('NAT') else e.split()[0]
        cnt[k] = cnt.get(k, 0) + 1
    print(f"  P{pn}: " + ', '.join(f'{k}×{v}' for k, v in sorted(cnt.items())))
print('\n## 重なった場面(カードの選択が開いた時のほかの人の状態)')
for pn in sorted(P):
    for u, e in P[pn]['ev']:
        if e.startswith('choice open') and ('Down' in e or '+choosing' in e or 'Escaped' in e):
            print(f"  P{pn}: {e[:200]}")

# ---------------------------------------------------------------- MILE と保存
print('\n## MILE と保存(ラン前 → RESULT → ホーム → 最後 → 再起動後)')
rs = {}
for f in glob.glob(os.path.join(RD, 'restart_p*.log')):
    pn = int(re.search(r'restart_p(\d)', f).group(1))
    for line in open(f, encoding='utf-8', errors='replace'):
        if 'NATSNAP tag=restart' in line: rs[pn] = dict(re.findall(r'(\w+)=(\S+)', line.split('NATSNAP', 1)[1]))
for pn in sorted(P):
    p = P[pn]; snaps = p['snap']
    def find(tag): return [s for s in snaps if s.get('tag') == tag]
    before = find('before')[-1] if find('before') else None
    line = f"  P{pn}: ラン前 {before['mile'] if before else '?'}"
    base = int(before['mile']) if before else None
    homes = find('home'); res = p['res']
    for i, r in enumerate(res):
        kind = r.get('kind'); shown = int(r.get('shownMile', 0))
        hm = int(homes[i]['mile']) if i < len(homes) else None
        exp = base + shown if base is not None and kind and kind.startswith(('win', 'versus')) else base
        ok = '一致' if hm is not None and exp is not None and hm == exp else ('?' if hm is None else f'不一致(期待 {exp})')
        saved = r.get('savedMile'); wallet = r.get('walletShown')
        line += f" | ラン{i+1} {kind} 表示 +{shown} 所持の表示 {wallet} 保存 {saved} → ホーム {hm}"
        base = hm if hm is not None else base
    fin = find('final')
    if fin: line += f" | 最後 {fin[-1]['mile']}"
    if pn in rs:
        r = rs[pn]; f0 = fin[-1] if fin else (homes[-1] if homes else None)
        same = f0 is not None and all(r.get(k) == f0.get(k) for k in ('mile', 'char', 'deck', 'owned', 'gacha', 'best', 'lifetime'))
        line += f" | 再起動後 {r['mile']} {'同じ' if same else '違う: ' + ' '.join(f'{k}:{f0.get(k)}→{r.get(k)}' for k in ('mile','char','deck','owned','gacha','lifetime') if f0 and r.get(k) != f0.get(k))}"
    print(line)
    for s in snaps: pass
print('\n## MILE の流れの照合(チャレンジの達成分も数える。RESULT の表示 = 確定した額 / ホームの所持 = 前 + 確定 + チャレンジ)')
for pn in sorted(P):
    seq = P[pn]['seq']; out = []; base = None; chal = 0; committed = 0; shown0 = None; bad = 0
    for e in seq:
        if e[0] == 'before': base = e[1]; chal = committed = 0
        elif e[0] == 'chal': chal += e[1]; out.append(f"チャレンジ+{e[1]}({e[2]})")
        elif e[0] == 'commit': committed += e[2] - e[1]; out.append(f"確定 {e[1]}→{e[2]}")
        elif e[0] == 'shown': shown0 = e[2]
        elif e[0] == 'natresult':
            kind, shown, wallet, saved = e[1], e[2], e[3], e[4]
            msg = f"RESULT {kind} 表示+{shown}"
            if kind.startswith('versus'): committed = shown; shown0 = None  # VERSUS の持ち帰りは控え(CommitJournal)を通らない。表示 = 順位/死神の加算込みの合計
            if kind.startswith('win') and committed and shown != committed: msg += f" ★表示と確定が違う(確定 +{committed})"; bad += 1
            if shown0 is not None and shown0 != shown: msg += f" ★出た時 {shown0} → 後で {shown}"; bad += 1
            if wallet != saved: msg += f" ★所持の表示 {wallet} ≠ 保存 {saved}"; bad += 1
            out.append(msg); shown0 = None
        elif e[0] == 'home':
            if base is not None:
                exp = base + committed + chal
                if exp != e[1]: bad += 1
                out.append(f"ホーム {e[1]} " + ('一致' if exp == e[1] else f'★不一致(期待 {exp})'))
            base = e[1]; chal = committed = 0
    print(f"  P{pn}: {'問題なし' if bad == 0 else f'問題 {bad} 件'} | " + ' / '.join(out))
print('\n## キャラ/デッキ(人ごと。ほかの人と混ざらないか)')
for pn in sorted(P):
    sn = P[pn]['snap']
    chars = sorted(set(s.get('char') for s in sn)); decks = sorted(set(s.get('deck') for s in sn))
    runchar = sorted(set(d.get('char') for d in P[pn]['nat'] if d.get('char')))
    print(f"  P{pn}: 保存のキャラ {chars} ラン中のキャラ {runchar} デッキ {decks} 所持 {sorted(set(s.get('owned') for s in sn))}")
print('\n## RESULT の行')
for pn in sorted(P):
    for r in P[pn]['res']: print(f"  P{pn}: {r['raw'][:330]}")
print('\n## ボスの撃破(HOST の記録)と、ボスの報酬の選択')
for pn in sorted(P):
    for k in P[pn]['kill'][:40]:
        if 'Boss' in k or 'boss' in k: print(f"  P{pn}: {k[:200]}")
    br = [e for _, e in P[pn]['ev'] if 'kind=BossReward' in e and e.startswith('choice open')]
    print(f"  P{pn}: ボスの報酬の選択 {len(br)} 回")
print('\n## 置き直し/復帰の記録')
for pn in sorted(P):
    for l in P[pn]['net'][:12]: print(f"  P{pn}: {l}")

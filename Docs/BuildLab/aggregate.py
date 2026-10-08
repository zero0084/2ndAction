# -*- coding: utf-8 -*-
# ビルド検証の結果(各試行の result.json)を集める → trials.csv(1試行1行)/ picks.csv / bosses.csv
# usage: python aggregate.py <resdir> <outprefix>
import csv, glob, json, os, sys

resdir, outp = sys.argv[1], sys.argv[2]
rows, picks, bosses = [], [], []
for f in sorted(glob.glob(os.path.join(resdir, '*', 'result.json'))):
    try: r = json.load(open(f, encoding='utf-8'))
    except Exception as e: print('bad', f, e); continue
    trial = r.get('trial') or os.path.basename(os.path.dirname(f))
    bl = r.get('bosses') or []
    rows.append({
        'trial': trial, 'commit': r.get('commit'), 'platform': r.get('platform'), 'seed': r.get('seed'),
        'character': r.get('character'), 'stage': r.get('stage'), 'build': r.get('build'), 'type': r.get('buildType'),
        'growth': r.get('growth'), 'depart': r.get('depart'), 'ring': r.get('ringPolicy'),
        'engageKmh': r.get('engageKmh'), 'assist': 'auto boss/attack/avoid ON',
        'deck': ' '.join(r.get('deck') or []), 'charCards': ' '.join(r.get('charCards') or []),
        'outcome': r.get('outcome'), 'success': r.get('success'), 'maxDistance': round(r.get('maxDistance') or 0),
        'gameMin': round((r.get('gameSeconds') or 0) / 60, 2), 'successGameMin': round(r['successGameSeconds'] / 60, 2) if (r.get('successGameSeconds') or -1) >= 0 else '',
        'reaperSurvivedM': round(r['reaperSurvivedMeters']) if (r.get('reaperSurvivedMeters') or -1) >= 0 else '', 'reaperSurvivedS': round(r['reaperSurvivedSeconds']) if (r.get('reaperSurvivedSeconds') or -1) >= 0 else '', 'reaperEnd': r.get('reaperEnd'),
        'level': r.get('level'), 'bossesDefeated': r.get('bossesDefeated'), 'bossFights': len(bl),
        'bossMaxSec': round(max([b.get('seconds') or 0 for b in bl] or [0]), 1),
        'hitsTaken': r.get('hitsTaken'), 'assistFirstActiveM': round(r.get('assistFirstActiveDist') or -1), 'maxKmh': round(r.get('maxKmh') or 0),
        'cause': r.get('cause'), 'causeDetail': r.get('causeDetail'), 'deathSnapshot': r.get('deathSnapshot'), 'finalAbilities': r.get('finalAbilities'),
        'avgFps': round(r.get('avgFps') or 0, 1), 'slowFrames': r.get('slowFrames'), 'realMin': round((r.get('realSeconds') or 0) / 60, 1), 'notes': r.get('notes'),
    })
    for p in r.get('picks') or []:
        picks.append({'trial': trial, 'dist': round(p.get('dist') or 0), 'gameSec': round(p.get('t') or 0, 1), 'kind': p.get('kind'), 'level': p.get('level'),
                      'offered': ' '.join(p.get('offered') or []), 'picked': p.get('picked'), 'why': p.get('why')})
    for b in bl:
        bosses.append({'trial': trial, 'character': r.get('character'), 'stage': r.get('stage'), 'build': r.get('build'), 'dist': round(b.get('dist') or 0), 'names': b.get('names'),
                       'seconds': round(b.get('seconds') or 0, 1), 'defeated': b.get('defeated'), 'timedOut': b.get('timedOut'), 'hpLeftFrac': round(b.get('hpLeftFrac') or 0, 3), 'hitsTaken': b.get('hitsTaken')})

def write(name, data):
    if not data: return
    with open(f'{outp}_{name}.csv', 'w', newline='', encoding='utf-8-sig') as fo:
        w = csv.DictWriter(fo, fieldnames=list(data[0].keys())); w.writeheader(); w.writerows(data)
write('trials', rows); write('picks', picks); write('bosses', bosses)
print(f'{len(rows)} trials, {len(picks)} picks, {len(bosses)} boss fights -> {outp}_*.csv')

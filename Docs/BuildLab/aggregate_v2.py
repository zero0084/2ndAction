# -*- coding: utf-8 -*-
# 攻略の再検証(2026-10-11)の集計: 調整前(V2B)と調整後(V2A)、確認試験(V2R)を1試行1行の CSV と比較の表へ。
# usage: python aggregate_v2.py <resdir_base> <resdir_after> <outdir>
import csv, glob, json, os, statistics as st, sys

base_dir, after_dir, out = sys.argv[1], sys.argv[2], sys.argv[3]
CH = {'noble_lady': 'お嬢様', 'fighter': '格闘家', 'dual_blade': '双剣士', 'mage': '魔法使い', 'ninja': '忍者', 'swordsman': '黒剣士'}
ST = {'wasteland_road': '荒野街道', 'natural_cave': '自然洞窟', 'sky_corridor': '天空回廊', 'last_corridor': 'ラスダン'}


def load(d, tag):
    rows = []
    for f in sorted(glob.glob(os.path.join(d, '*', 'result.json'))):
        r = json.load(open(f, encoding='utf-8'))
        r['_tag'] = tag
        if r['trial'].startswith('V2R'):
            r['_tag'] = 'regress_after' if r['trial'][5] == 'A' else 'regress_base'
        rows.append(r)
    return rows


rows = load(base_dir, 'base') + load(after_dir, 'after')
fields = ['tag', 'trial', 'exe', 'commit', 'build', 'character', 'stage', 'seed', 'outcome', 'success', 'maxDistance', 'reached80k', 'category', 'cause',
          'causeDetail', 'gameMin', 'realMin', 'avgFps', 'debugClean', 'assistActiveFrac', 'assistSeconds', 'assistFirstActiveDist', 'maxKmh', 'hitsTaken',
          'swings', 'zakoHits', 'zakoKills', 'lv9Abilities', 'abilityCount', 'bossesDefeated', 'lastBoss', 'lastBossDist', 'lastBossSec', 'lastBossHpStart',
          'lastBossHpLeft', 'lastBossDrops', 'lastBossSwings', 'lastBossPlayerHp', 'bossSecMedian', 'bossSecMax', 'stopEnemy', 'assistLast', 'finalAbilities']
os.makedirs(out, exist_ok=True)
with open(os.path.join(out, 'trials_v2.csv'), 'w', newline='', encoding='utf-8-sig') as f:
    w = csv.DictWriter(f, fieldnames=fields)
    w.writeheader()
    for r in rows:
        b = r['bosses'] or []
        lb = b[-1] if b else {}
        secs = [x['seconds'] for x in b if x.get('defeated')]
        w.writerow({'tag': r['_tag'], 'trial': r['trial'], 'exe': r.get('exe'), 'commit': r.get('commit'), 'build': r['build'], 'character': r['character'], 'stage': r['stage'],
                    'seed': r['seed'], 'outcome': r['outcome'], 'success': r['success'], 'maxDistance': round(r['maxDistance']), 'reached80k': r['maxDistance'] >= 80000,
                    'category': r.get('category'), 'cause': r.get('cause'), 'causeDetail': r.get('causeDetail'), 'gameMin': round(r['gameSeconds'] / 60, 1),
                    'realMin': round(r['realSeconds'] / 60, 1), 'avgFps': round(r['avgFps']), 'debugClean': r.get('debugClean'), 'assistActiveFrac': round(r.get('assistActiveFrac', -1), 2),
                    'assistSeconds': r.get('assistSeconds'), 'assistFirstActiveDist': round(r.get('assistFirstActiveDist', -1)), 'maxKmh': round(r.get('maxKmh', 0)),
                    'hitsTaken': r['hitsTaken'], 'swings': r.get('swings'), 'zakoHits': r.get('zakoHits'), 'zakoKills': r.get('zakoKills'), 'lv9Abilities': r.get('lv9Abilities'),
                    'abilityCount': r.get('abilityCount'), 'bossesDefeated': r['bossesDefeated'], 'lastBoss': lb.get('names'), 'lastBossDist': lb.get('dist'),
                    'lastBossSec': round(lb.get('seconds', 0)), 'lastBossHpStart': round(lb.get('hpStartFrac', -1), 2), 'lastBossHpLeft': round(lb.get('hpLeftFrac', -1), 2),
                    'lastBossDrops': lb.get('hpDrops'), 'lastBossSwings': lb.get('swings'), 'lastBossPlayerHp': f"{lb.get('playerHpStart')}->{lb.get('playerHpEnd')}",
                    'bossSecMedian': round(st.median(secs)) if secs else '', 'bossSecMax': round(max(secs)) if secs else '', 'stopEnemy': r.get('stopEnemy'),
                    'assistLast': r.get('assistLast'), 'finalAbilities': r.get('finalAbilities')})

# 比較の表(キャラ×マップ: 3シードの到達距離 / 終盤到達数 / 10万m・最終戦突破数 / 主な区分)
lines = ['| キャラ | マップ | 調整前 到達(km) | 前 8万m到達 | 前 成功 | 調整後 到達(km) | 後 8万m到達 | 後 成功 | 調整前の主な理由 | 調整後の主な理由 |', '|---|---|---|---|---|---|---|---|---|---|']
for ch in ['noble_lady', 'fighter', 'dual_blade', 'mage', 'ninja']:
    for stg in ST:
        def pick(tag):
            return [r for r in rows if r['_tag'] == tag and r['character'] == ch and r['stage'] == stg]
        cells = []
        for tag in ['base', 'after']:
            p = pick(tag)
            if not p:
                cells.append(('-', '-', '-', '-'))
                continue
            km = '/'.join(f"{r['maxDistance'] / 1000:.0f}" for r in sorted(p, key=lambda x: x['seed']))
            r80 = sum(1 for r in p if r['maxDistance'] >= 80000)
            ok = sum(1 for r in p if r['success'])
            cats = [((r.get('category') or '')[:1] + ' ' + ((r.get('bosses') or [{}])[-1].get('names') or '') if not r['success'] else '成功') for r in sorted(p, key=lambda x: x['seed'])]
            cells.append((km, f"{r80}/{len(p)}", f"{ok}/{len(p)}", ', '.join(cats)))
        lines.append(f"| {CH[ch]} | {ST[stg]} | {cells[0][0]} | {cells[0][1]} | {cells[0][2]} | {cells[1][0]} | {cells[1][1]} | {cells[1][2]} | {cells[0][3]} | {cells[1][3]} |")
open(os.path.join(out, 'compare_v2.md'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('\n'.join(lines))
print(len(rows), 'results')

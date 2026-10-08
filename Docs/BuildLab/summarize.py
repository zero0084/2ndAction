# -*- coding: utf-8 -*-
# 12キャラ × 4ステージの一覧(人が読む要約)を作る。aggregate.py の *_trials.csv を読む。
# usage: python summarize.py <out.md> normal=<csv> sprint=<csv> partial=<csv> [more=<csv> ...]
import csv, sys, collections, statistics

CHARS = ['swordsman', 'dual_blade', 'noble_lady', 'gunslinger', 'dragon_lancer', 'archer', 'mage', 'fighter', 'ninja', 'miko', 'vampire', 'dragonkin']
STAGES = ['wasteland_road', 'natural_cave', 'sky_corridor', 'last_corridor']
SN = {'wasteland_road': '荒野街道', 'natural_cave': '自然洞窟', 'sky_corridor': '天空回廊', 'last_corridor': 'ラスダン'}
out = sys.argv[1]
src = dict(a.split('=', 1) for a in sys.argv[2:])
def load(k):
    if k not in src: return []
    return list(csv.DictReader(open(src[k], encoding='utf-8-sig')))
normal, sprint, partial = load('normal'), load('sprint'), load('partial')
L = []
def p(s=''): L.append(s)

def cell_full(rows):
    if not rows: return '未検証'
    ok = sum(1 for r in rows if r['success'] == 'True')
    best = max(rows, key=lambda r: float(r['maxDistance'] or 0))
    causes = collections.Counter(r['cause'] for r in rows if r['success'] != 'True' and r['cause'])
    c = causes.most_common(1)[0][0] if causes else ''
    return f"{ok}/{len(rows)} 成功, 最長 {float(best['maxDistance']) / 1000:.1f}km({best['type']})" + (f", 主な敗因 {c}" if c else '')

p('# ビルド検証 一覧(自動生成)')
p()
p('## 通常出発(育成 G0、既定の自動補助)')
p('| キャラ | ' + ' | '.join(SN[s] for s in STAGES) + ' |'); p('|---|' + '---|' * len(STAGES))
for ch in CHARS:
    cells = []
    for st in STAGES:
        rs = [r for r in normal if r['character'] == ch and r['stage'] == st]
        cells.append(cell_full(rs))
    p(f'| {ch} | ' + ' | '.join(cells) + ' |')
p()
p('## 疾走出発 10k → 通し(育成 G0、既定の自動補助、リングの入力なし)')
p('| キャラ | ' + ' | '.join(SN[s] for s in STAGES) + ' |'); p('|---|' + '---|' * len(STAGES))
for ch in CHARS:
    cells = []
    for st in STAGES:
        rs = [r for r in sprint if r['character'] == ch and r['stage'] == st]
        cells.append(cell_full(rs))
    p(f'| {ch} | ' + ' | '.join(cells) + ' |')
p()
p('## 部分試験(完成ビルド = デッキの各能力 Lv9、開発用ワープで関門の手前から1戦)')
p('各セル: 型ごとに 撃破できた地点(k)/ 試した地点。✗の後ろは撃破できなかった地点と理由')
p('| キャラ | ' + ' | '.join(SN[s] for s in STAGES) + ' |'); p('|---|' + '---|' * len(STAGES))
for ch in CHARS:
    cells = []
    for st in STAGES:
        rs = [r for r in partial if r['character'] == ch and r['stage'] == st]
        if not rs: cells.append('未検証'); continue
        parts = []
        for t in 'ADBS':
            tr = [r for r in rs if r['type'] == t]
            if not tr: continue
            okk = sorted(int(r['depart'].split(':')[1]) // 1000 for r in tr if r['outcome'] == 'partial_cleared' or r['success'] == 'True')
            bad = [(int(r['depart'].split(':')[1]) // 1000, r['outcome'], (r['cause'] or '')[:12]) for r in tr if not (r['outcome'] == 'partial_cleared' or r['success'] == 'True')]
            s = f"{t}:{'/'.join(map(str, okk)) or '-'}"
            if bad: s += ' ✗' + ','.join(f'{k}k' for k, o, c in sorted(bad))
            parts.append(s)
        cells.append('<br>'.join(parts))
    p(f'| {ch} | ' + ' | '.join(cells) + ' |')
p()
# 敗因の集計
for name, rows in (('通常出発', normal), ('疾走出発', sprint), ('部分試験', partial)):
    if not rows: continue
    c = collections.Counter((r['outcome'], r['cause']) for r in rows)
    p(f'### {name}: 結果の内訳({len(rows)} 試行)')
    for (o, cause), n in c.most_common(): p(f'- {o} / {cause or "-"}: {n}')
    fps = [float(r['avgFps']) for r in rows if r.get('avgFps')]
    if fps: p(f'- 実行時の平均 fps: 最低 {min(fps):.1f} / 平均 {statistics.mean(fps):.1f}')
    p()
open(out, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('wrote', out)

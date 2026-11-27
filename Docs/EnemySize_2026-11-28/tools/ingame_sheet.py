import sys, os, csv, statistics, collections
from PIL import Image, ImageDraw, ImageFont
# ゲーム内撮影(-qaEnemySize)の結果をまとめる: python -I -X utf8 ingame_sheet.py <撮影フォルダ> <出力 png> [縦横の名前]
#  ・size.tsv から、相手の絵の高さ(フェーズごとの中央値)/ゴブリンの高さ、フレーム間の大きさの跳び(撃破のフェーズ以外)、当たり判定と絵のずれ
#  ・shot_<id>_move.png からゴブリンと相手を含む範囲を切り出して並べる
D, OUT = sys.argv[1], sys.argv[2]
tag = sys.argv[3] if len(sys.argv) > 3 else ''
rows = list(csv.DictReader(open(os.path.join(D, 'size.tsv'), encoding='utf-8'), delimiter='\t'))
by = collections.defaultdict(list)
for r in rows: by[(r['id'], r['who'])].append(r)
gob = [float(r['srH']) for r in rows if r['who'] == 'ref' and r['phase'] == 'move']
gobH = statistics.median(gob) if gob else 1.0
order = []
for r in rows:
    if r['who'] == 'target' and r['id'] not in order: order.append(r['id'])
report = []
for eid in order:
    rs = by[(eid, 'target')]
    ph = collections.defaultdict(list)
    for r in rs: ph[r['phase']].append(float(r['srH']))
    med = {k: statistics.median(v) for k, v in ph.items()}
    live = [r for r in rs if r['phase'] != 'death']
    jumps = 0; worst = 1.0
    for a, b in zip(live, live[1:]):
        ha, hb = float(a['srH']), float(b['srH'])
        if ha > 0 and hb > 0:
            q = max(ha, hb) / min(ha, hb)
            if q > 1.25: jumps += 1
            worst = max(worst, q)
    colH = [float(r['colH']) / float(r['srH']) for r in live if float(r['srH']) > 0 and float(r['colH']) > 0]
    colDy = [float(r['colCy']) for r in live if float(r['colH']) > 0]
    report.append((eid, med.get('move', 0) / gobH, med, jumps, worst, statistics.median(colH) if colH else 0, statistics.median(colDy) if colDy else 0))
with open(OUT.replace('.png', '.txt'), 'w', encoding='utf-8') as f:
    f.write(f'goblin move height (world) {gobH:.2f}\n')
    f.write('id\tmove/goblin\tmove\tattack\thurt\tdeath\tjumps>25%\tworst\tcolH/srH\tcolCy-srCy\n')
    for eid, q, med, j, w, ch, cdy in report:
        f.write(f"{eid}\t{q:.2f}\t{med.get('move',0):.2f}\t{med.get('attack',0):.2f}\t{med.get('hurt',0):.2f}\t{med.get('death',0):.2f}\t{j}\t{w:.2f}\t{ch:.2f}\t{cdy:+.2f}\n")
# 切り出し
tiles = []
for eid in order:
    p = os.path.join(D, f'shot_{eid}_move.png')
    if not os.path.exists(p): continue
    im = Image.open(p).convert('RGB')
    box = None
    for who in ('ref', 'target'):
        rs = [r for r in by[(eid, who)] if r['phase'] == 'move']
        if not rs: continue
        r = rs[min(len(rs) - 1, 20)]
        b = [float(r['sx0']), float(r['sy0']), float(r['sx1']), float(r['sy1'])]
        box = b if box is None else [min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3])]
    if box is None: continue
    pad = 40
    box = [max(0, int(box[0]) - pad), max(0, int(box[1]) - pad), min(im.width, int(box[2]) + pad), min(im.height, int(box[3]) + pad)]
    if box[2] - box[0] < 20 or box[3] - box[1] < 20: continue
    crop = im.crop(box)
    s = 260 / crop.height
    crop = crop.resize((max(1, int(crop.width * s)), 260))
    tiles.append((eid, crop))
if tiles:
    W = 1800; x = y = 0; rowh = 290; lines = []
    cur = []
    for eid, t in tiles:
        if x + t.width > W and cur: lines.append(cur); cur = []; x = 0
        cur.append((eid, t)); x += t.width + 10
    if cur: lines.append(cur)
    sheet = Image.new('RGB', (W, 30 + rowh * len(lines)), (30, 32, 40))
    d = ImageDraw.Draw(sheet)
    d.text((8, 8), f'in-game (move) - goblin + enemy {tag}', fill=(255, 220, 120))
    for i, ln in enumerate(lines):
        x = 0
        for eid, t in ln:
            sheet.paste(t, (x, 30 + i * rowh)); d.text((x + 4, 30 + i * rowh + 262), eid, fill=(230, 230, 230)); x += t.width + 10
    sheet.save(OUT)
print(open(OUT.replace('.png', '.txt'), encoding='utf-8').read())

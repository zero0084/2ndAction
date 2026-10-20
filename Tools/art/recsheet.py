"""sisrec の撮影から、場面ごとに「姉妹のまわりを切り抜いて並べた一覧」を作る。
usage: recsheet.py <qdir> [label-filter] [--every N] [--cols C] [--prefix sisrec|demo]
  --prefix demo: -qaAnimDemo の demo_pos.txt / demo_ranges.txt(2026-10-20)
"""
import sys, os
from PIL import Image, ImageDraw

q = sys.argv[1]
flt = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else ''
every = int(sys.argv[sys.argv.index('--every') + 1]) if '--every' in sys.argv else 3
cols = int(sys.argv[sys.argv.index('--cols') + 1]) if '--cols' in sys.argv else 10
pre = sys.argv[sys.argv.index('--prefix') + 1] if '--prefix' in sys.argv else 'sisrec'
pos = {}
for l in open(os.path.join(q, pre + '_pos.txt'), encoding='utf-8'):
    p = l.strip().split('\t')
    if len(p) == 4: pos[int(p[0])] = (float(p[1]), float(p[2]), float(p[3]))
os.makedirs(os.path.join(q, 'zoom'), exist_ok=True)
for l in open(os.path.join(q, pre + '_ranges.txt'), encoding='utf-8'):
    label, a, b = l.strip().split('\t'); a, b = int(a), int(b)
    if flt and flt not in label: continue
    tiles = []
    for f in range(a, b + 1, every):
        im = Image.open(os.path.join(q, 'frames', f'f{f:05d}.png')).convert('RGB')
        x, y, h = pos.get(f, (im.width / 2, im.height / 2, 200))
        s = max(160, int(h * 0.8))
        box = (int(x - s / 2), int(y - s * 0.55), int(x + s / 2), int(y + s * 0.45))
        t = im.crop(box).resize((220, 220))
        d = ImageDraw.Draw(t); d.text((4, 4), f'{(f - a) / 30:.2f}s', fill=(255, 255, 0))
        tiles.append(t)
    if not tiles: continue
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * 220, rows * 220), (0, 0, 0))
    for i, t in enumerate(tiles): sheet.paste(t, ((i % cols) * 220, (i // cols) * 220))
    sheet.save(os.path.join(q, 'zoom', label + '.png'))
    print(label, len(tiles))

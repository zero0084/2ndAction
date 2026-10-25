"""元 / 合成 を上下2段で並べる(キャラ1人の1動作の全コマ、2倍)。usage: clothrow.py <out.jpg> <元のフォルダ> <合成のフォルダ> [scale=1]"""
import sys, os, glob
from PIL import Image
out, od, nd = sys.argv[1:4]; s = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
fs = sorted(os.path.basename(f) for f in glob.glob(os.path.join(nd, '*.png')))
rows = []
for d in (od, nd):
    t = []
    for f in fs:
        im = Image.open(os.path.join(d, f)).convert('RGBA'); b = Image.new('RGBA', im.size, (110, 120, 130, 255)); b.alpha_composite(im)
        t.append(b.convert('RGB').resize((int(im.width * s), int(im.height * s)), Image.LANCZOS))
    rows.append(t)
W = sum(x.width + 6 for x in rows[0]); H = sum(max(x.height for x in r) + 6 for r in rows)
S = Image.new('RGB', (W, H), (30, 30, 30)); y = 0
for r in rows:
    x = 0
    for t in r: S.paste(t, (x, y)); x += t.width + 6
    y += max(t.height for t in r) + 6
S.save(out, quality=88); print(out, S.size)

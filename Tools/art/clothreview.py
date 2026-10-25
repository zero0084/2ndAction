"""キャラ1人のアニメ全コマの 元/修正 を、動作ごとに上下2段で1枚に(エステルのレビュー用)。高さをそろえて縮小。
usage: clothreview.py <元のArt> <合成のlocal> <out.jpg> <フォルダ> ... [--h 150]"""
import sys, os, glob
from PIL import Image, ImageDraw, ImageFont
a = sys.argv[1:]; H = int(a[a.index('--h') + 1]) if '--h' in a else 150
if '--h' in a: i = a.index('--h'); a = a[:i] + a[i + 2:]
od, nd, outp = a[:3]; F = ImageFont.truetype(r'C:/Windows/Fonts/meiryo.ttc', 14)
blocks = []
for sub in a[3:]:
    fs = sorted(glob.glob(os.path.join(nd, sub, '*.png')))
    if not fs: continue
    rows = []
    for d in (os.path.join(od, sub), os.path.join(nd, sub)):
        t = []
        for f in fs:
            im = Image.open(os.path.join(d, os.path.basename(f))).convert('RGBA'); bb = Image.open(f).convert('RGBA').getbbox()
            ob = Image.open(os.path.join(od, sub, os.path.basename(f))).convert('RGBA').getbbox()
            box = (min(bb[0], ob[0]), min(bb[1], ob[1]), max(bb[2], ob[2]), max(bb[3], ob[3])); im = im.crop(box)
            s = H / im.height; im = im.resize((max(1, int(im.width * s)), H), Image.LANCZOS)
            b = Image.new('RGB', im.size, (110, 120, 130)); b.paste(im, (0, 0), im); t.append(b)
        rows.append(t)
    w = sum(x.width + 4 for x in rows[0]); blk = Image.new('RGB', (w, 2 * H + 26), (25, 25, 30))
    ImageDraw.Draw(blk).text((2, 2), sub.split('/')[-1] + '  (上=元 / 下=修正)', font=F, fill=(255, 230, 120))
    for r, t in enumerate(rows):
        x = 0
        for im in t: blk.paste(im, (x, 22 + r * (H + 4))); x += im.width + 4
    blocks.append(blk)
W = 2400; lines = []; cur = []; cw = 0
for b in blocks:
    if cw + b.width > W and cur: lines.append(cur); cur = []; cw = 0
    cur.append(b); cw += b.width + 12
if cur: lines.append(cur)
S = Image.new('RGB', (W, sum(max(b.height for b in l) + 10 for l in lines)), (10, 10, 12)); y = 0
for l in lines:
    x = 0
    for b in l: S.paste(b, (x, y)); x += b.width + 12
    y += max(b.height for b in l) + 10
S.save(outp, quality=85); print(outp, S.size)

"""キャラ1人の 立ち絵・カード・全アニメのコマを1枚に並べる(2026-10-25 布表現の見直し、全12人の点検用)。
usage: clothsurvey.py <id> <フォルダの接頭辞> <out.jpg> [--art Unity/2ndAction/Assets/Art]
"""
import sys, os, glob
from PIL import Image, ImageDraw, ImageFont
cid, prefix, outp = sys.argv[1:4]
art = sys.argv[sys.argv.index('--art') + 1] if '--art' in sys.argv else 'Unity/2ndAction/Assets/Art'
F = ImageFont.truetype(r'C:/Windows/Fonts/meiryo.ttc', 13)
TH = 170
def tile(p, label, h=TH):
    im = Image.open(p).convert('RGBA'); bb = im.getbbox() or (0, 0, im.width, im.height); im = im.crop(bb)
    s = h / im.height; im = im.resize((max(1, int(im.width * s)), h), Image.LANCZOS)
    t = Image.new('RGB', (im.width + 6, h + 20), (90, 98, 110)); t.paste(im, (3, 3), im)
    ImageDraw.Draw(t).text((3, h + 4), label, font=F, fill=(255, 255, 160)); return t
rows = []
ui = os.path.join(art, 'UI', 'Characters')
rows.append([tile(os.path.join(ui, f'{cid}_main.png'), 'main', 340), tile(os.path.join(ui, f'{cid}_card.png'), 'card', 340)])
dirs = sorted(d for d in glob.glob(os.path.join(art, prefix + '*')) if os.path.isdir(d))
for d in dirs:
    subs = [d] + sorted(x for x in glob.glob(os.path.join(d, '*')) if os.path.isdir(x))
    for sd in subs:
        fs = sorted(glob.glob(os.path.join(sd, '*.png')))
        if not fs: continue
        name = os.path.relpath(sd, art).replace(os.sep, '/')
        rows.append([tile(f, (name + '/' if i == 0 else '') + os.path.basename(f)[:-4]) for i, f in enumerate(fs)])
W = 2400
lines = []; cur = []; cw = 0
for r in rows:  # 1フォルダ1行。長い行は折り返す
    cur = []; cw = 0
    for t in r:
        if cw + t.width > W and cur: lines.append(cur); cur = []; cw = 0
        cur.append(t); cw += t.width + 4
    if cur: lines.append(cur)
H = sum(max(t.height for t in l) + 6 for l in lines)
S = Image.new('RGB', (W, H), (30, 30, 36)); y = 0
for l in lines:
    x = 0
    for t in l: S.paste(t, (x, y)); x += t.width + 4
    y += max(t.height for t in l) + 6
S.save(outp, quality=85); print(outp, S.size, sum(len(r) for r in rows) - 2, 'frames')

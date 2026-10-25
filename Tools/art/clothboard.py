"""布の修正の比較ボード(2026-10-25 布表現の見直し)。何枚でも横に並べる。
usage: clothboard.py <出力.jpg> <題> --img <ラベル>=<_main.png> [--img ...] [--box x0,y0,x1,y1 ...(700x1100 の座標)]
出力:
  <出力>.jpg        全身(キャラ選択の表示と同じ 700x1100 を縮小)を並べ、拡大する所に枠
  <出力>_zoom.jpg   拡大(行=場所、列=絵)
  <出力>_small.jpg  ゲーム内相当の大きさ(キャラ選択 約440px・小さめ 200px・走りと同じ 62px)を等倍で
"""
import sys
from PIL import Image, ImageDraw, ImageFont

BG = (110, 120, 130)
DARK = (40, 44, 52)
FONT = 'C:/Windows/Fonts/meiryo.ttc'
LET = 'ABCDEFGH'

def flat(im):
    b = Image.new('RGBA', im.size, BG + (255,)); b.alpha_composite(im); return b.convert('RGB')

def fit(im, w, h):
    s = min(w / im.width, h / im.height)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)

args = sys.argv[1:]
out, title = args[0], args[1]
imgs, boxes = [], []
i = 2
while i < len(args):
    if args[i] == '--img':
        lab, p = args[i + 1].split('=', 1); imgs.append((lab, Image.open(p).convert('RGBA'))); i += 2
    elif args[i] == '--box':
        boxes.append(tuple(int(v) for v in args[i + 1].split(','))); i += 2
    else:
        raise SystemExit('unknown arg ' + args[i])
f = ImageFont.truetype(FONT, 26); fs = ImageFont.truetype(FONT, 20); fx = ImageFont.truetype(FONT, 15)
n = len(imgs)

# 全身
H = 900; s = H / 1100; cw = int(700 * s)
B = Image.new('RGB', (20 + n * (cw + 20), 60 + H + 20), DARK); d = ImageDraw.Draw(B)
d.text((20, 12), title, fill='white', font=f)
for k, (lab, im) in enumerate(imgs):
    x = 20 + k * (cw + 20)
    B.paste(flat(im).resize((cw, H), Image.LANCZOS), (x, 60)); d.text((x + 6, 64), lab, fill=(255, 220, 120), font=fs)
    for j, bx in enumerate(boxes):
        d.rectangle([x + bx[0] * s, 60 + bx[1] * s, x + bx[2] * s, 60 + bx[3] * s], outline=(255, 220, 0), width=2)
        d.text((x + bx[0] * s + 4, 60 + bx[1] * s + 2), LET[j], fill=(255, 220, 0), font=fs)
B.save(out, quality=92)

# 拡大
if boxes:
    zw, zh = 440, 520
    Z = Image.new('RGB', (20 + n * (zw + 20), 20 + len(boxes) * (zh + 40)), DARK); dz = ImageDraw.Draw(Z)
    for j, bx in enumerate(boxes):
        for k, (lab, im) in enumerate(imgs):
            c = fit(flat(im.crop(bx)), zw, zh)
            px, py = 20 + k * (zw + 20), 20 + j * (zh + 40)
            Z.paste(c, (px, py + 30)); dz.text((px, py), f'{LET[j]}  {lab}', fill=(255, 220, 120), font=fs)
    Z.save(out.replace('.jpg', '_zoom.jpg'), quality=92)

# 等倍
sizes = [('キャラ選択の立ち絵(約440px)', 440), ('小さめの表示(200px)', 200), ('走りと同じ高さ(62px)', 62)]
panels = []
for lab_s, h in sizes:
    for lab, im in imgs:
        c = im.crop(im.getbbox()); c = c.resize((max(1, int(c.width * h / c.height)), h), Image.LANCZOS)
        pw = max(c.width + 20, 150)
        p = Image.new('RGBA', (pw, 460), BG + (255,)); p.alpha_composite(c, ((pw - c.width) // 2, 460 - h - 10))
        panels.append((p.convert('RGB'), lab, lab_s))
    panels.append(None)
W = 20 + sum((p[0].width + 16) if p else 24 for p in panels)
S = Image.new('RGB', (W, 620), DARK); ds = ImageDraw.Draw(S)
ds.text((20, 10), title + ' — 等倍(拡大していない大きさ)', fill='white', font=f)
x = 20
for p in panels:
    if p is None: x += 24; continue
    S.paste(p[0], (x, 70)); ds.text((x, 538), p[1], fill=(255, 220, 120), font=fs); ds.text((x, 566), p[2], fill=(200, 200, 200), font=fx)
    x += p[0].width + 16
S.save(out.replace('.jpg', '_small.jpg'), quality=95)
print('ok')

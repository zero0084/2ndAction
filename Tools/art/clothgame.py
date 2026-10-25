"""ゲーム中の表示サイズで並べる(2026-10-25 布表現の見直し)。PPU の違いをそのまま反映し、足元(ピボット)を同じ線にそろえる。
usage: clothgame.py <出力.png> <背景.png or -> <倍率(1=ゲーム中)> <ラベル>=<png>[@<meta を読む png>] ...
  画面の 1 ワールド単位 = 53px(1280x720、走り PPU186 で中身の高さ約62px)。表示の大きさ = 53 / PPU。
"""
import sys, re, os
from PIL import Image, ImageDraw, ImageFont
out, bgp, mag = sys.argv[1], sys.argv[2], float(sys.argv[3])
items = []
for a in sys.argv[4:]:
    lab, rest = a.split('=', 1)
    p, mp = (rest.split('@') + [None])[:2]
    m = open((mp or p) + '.meta', encoding='utf-8').read()
    px, py = map(float, re.search(r'spritePivot: \{x: ([\d.]+), y: ([\d.]+)\}', m).groups())
    ppu = float(re.search(r'spritePixelsToUnits: ([\d.]+)', m).group(1))
    im = Image.open(p).convert('RGBA'); s = 53.0 / ppu * mag
    im2 = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    items.append((lab, im2, px * im2.width, (1 - py) * im2.height, ppu))
cw = int(max(i[1].width for i in items) + 20); H = int(max(i[3] for i in items) + 40 * mag + 30)
W = cw * len(items)
if bgp != '-':
    bg = Image.open(bgp).convert('RGBA').resize((W, H))
else:
    bg = Image.new('RGBA', (W, H), (110, 120, 130, 255))
d = ImageDraw.Draw(bg); f = ImageFont.truetype('C:/Windows/Fonts/meiryo.ttc', 13)
ground = H - int(25 * mag) - 10
for k, (lab, im, fx, fy, ppu) in enumerate(items):
    bg.alpha_composite(im, (int(k * cw + cw / 2 - fx), int(ground - fy)))
    d.text((k * cw + 4, 2), f'{lab} (PPU{ppu:.0f})', fill='white', font=f, stroke_width=2, stroke_fill='black')
d.line([(0, ground), (W, ground)], fill=(255, 255, 0))
bg.convert('RGB').save(out)
print('ok', bg.size)

"""実ゲームの撮影(-qaClothCheck の cloth_vis.txt と frames/)から、区間ごとに絵が切り替わったコマを人物のまわりで切り出して並べる。
usage: clothingame.py <qaフォルダ> <out.jpg> <区間,区間,...> [--per 8] [--zoom 2.2] [--prefix 絵の名前の頭(この絵のコマだけ)]"""
import sys, os
from PIL import Image, ImageDraw, ImageFont
a = sys.argv[1:]
def opt(n, d): return a[a.index(n) + 1] if n in a else d
qd, outp, secs = a[0], a[1], a[2].split(','); per = int(opt('--per', '8')); z = float(opt('--zoom', '2.2'))
F = ImageFont.truetype(r'C:/Windows/Fonts/meiryo.ttc', 13)
rows = [l.rstrip('\n').split('\t') for l in open(os.path.join(qd, 'cloth_vis.txt'), encoding='utf-8')]
lines = []
for sec in secs:
    r = [x for x in rows if x[1] == sec]; pick = []; prev = None
    for x in r:
        if x[2] != prev: pick.append(x); prev = x[2]
    if len(pick) > per: pick = [pick[int(i * len(pick) / per)] for i in range(per)]
    tiles = []
    for x in pick:
        f = int(x[0]); px = float(x[4]); top = float(x[6]); bot = float(x[7]); h = max(70.0, bot - top) * 1.5
        cy = (top + bot) / 2; im = Image.open(os.path.join(qd, 'frames', f'f{f:05d}.png')).convert('RGB')
        c = im.crop((int(px - h * 0.75), int(cy - h * 0.6), int(px + h * 0.75), int(cy + h * 0.6))).resize((int(h * 1.5 * z), int(h * 1.2 * z)), Image.LANCZOS)
        c.thumbnail((260, 220)); t = Image.new('RGB', (262, 238), (20, 20, 20)); t.paste(c, (1, 1))
        ImageDraw.Draw(t).text((3, 222), f'{sec} {x[2]}', font=F, fill=(255, 255, 0)); tiles.append(t)
    if tiles: lines.append(tiles)
W = max(len(l) for l in lines) * 264; S = Image.new('RGB', (W, 240 * len(lines)), (0, 0, 0))
for y, l in enumerate(lines):
    for i, t in enumerate(l): S.paste(t, (i * 264, y * 240))
S.save(outp, quality=86); print(outp, S.size)

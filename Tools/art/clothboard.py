"""布の修正の前後比較ボード(2026-10-25 布表現の見直し)。
usage: clothboard.py <前_main.png> <後_main.png> <出力.jpg> <題> [x0,y0,x1,y1 ...(拡大する所、700x1100 の座標)]
1枚目: 全身の前後(キャラ選択の表示と同じ 700x1100 → 縮小して並べる)+ 拡大(同じ所を前後で)
2枚目(<出力>_small.jpg): ゲーム内相当の大きさ(キャラ選択の表示 約 440px 高・カードの立ち絵 約 200px 高・走りと同じ 62px 高)を等倍で
"""
import sys
from PIL import Image, ImageDraw, ImageFont

BG = (110, 120, 130)
FONT = 'C:/Windows/Fonts/meiryo.ttc'

def flat(im, bg=BG):
    b = Image.new('RGBA', im.size, bg + (255,)); b.alpha_composite(im); return b.convert('RGB')

def content(im):
    return im.crop(im.getbbox())

a = sys.argv[1:]
before = Image.open(a[0]).convert('RGBA'); after = Image.open(a[1]).convert('RGBA')
out, title = a[2], a[3]
boxes = [tuple(int(v) for v in s.split(',')) for s in a[4:]]
f = ImageFont.truetype(FONT, 26); fs = ImageFont.truetype(FONT, 20)

# 1枚目
H = 900; s = H / 1100
cols = 2 + len(boxes)
zw, zh = 420, 525
W = 20 + 2 * (int(700 * s) + 20) + len(boxes) * 0 + 20
rows_z = len(boxes)
B = Image.new('RGB', (max(W, 20 + 2 * (zw + 20)) + 2 * (zw + 20), 60 + H + 40), (40, 44, 52))
d = ImageDraw.Draw(B)
d.text((20, 12), title, fill='white', font=f)
x = 20
for lab, im in (('修正前', before), ('修正後(試作)', after)):
    t = flat(im).resize((int(700 * s), H), Image.LANCZOS)
    B.paste(t, (x, 60)); d.text((x + 6, 64), lab, fill=(255, 220, 120), font=f)
    for i, bx in enumerate(boxes):
        d.rectangle([x + bx[0] * s, 60 + bx[1] * s, x + bx[2] * s, 60 + bx[3] * s], outline=(255, 220, 0), width=2)
        d.text((x + bx[0] * s + 4, 60 + bx[1] * s + 2), 'ABCDEFG'[i], fill=(255, 220, 0), font=fs)
    x += int(700 * s) + 20
zx0 = x
for i, bx in enumerate(boxes[:2]):
    for j, im in enumerate((before, after)):
        c = flat(im.crop(bx))
        sc = min(zw / c.width, (H / 2 - 30) / c.height)
        c = c.resize((int(c.width * sc), int(c.height * sc)), Image.LANCZOS)
        px, py = zx0 + j * (zw + 20), 60 + i * int(H / 2)
        B.paste(c, (px, py + 26)); d.text((px, py), f"{'ABCDEFG'[i]} {'前' if j == 0 else '後'}", fill=(255, 220, 120), font=fs)
B.save(out, quality=92)

# 拡大が3つ以上あれば別の1枚へ
if len(boxes) > 2:
    Z = Image.new('RGB', (20 + 2 * (zw + 20), 20 + (len(boxes) - 2) * (zh + 40)), (40, 44, 52)); dz = ImageDraw.Draw(Z)
    for i, bx in enumerate(boxes[2:], 2):
        for j, im in enumerate((before, after)):
            c = flat(im.crop(bx)); sc = min(zw / c.width, zh / c.height)
            c = c.resize((int(c.width * sc), int(c.height * sc)), Image.LANCZOS)
            px, py = 20 + j * (zw + 20), 20 + (i - 2) * (zh + 40)
            Z.paste(c, (px, py + 30)); dz.text((px, py), f"{'ABCDEFG'[i]} {'前' if j == 0 else '後'}", fill=(255, 220, 120), font=fs)
    Z.save(out.replace('.jpg', '_zoom2.jpg'), quality=92)

# 2枚目: ゲーム内相当の大きさ(等倍)
sizes = [('キャラ選択の立ち絵(約440px)', 440), ('小さめの表示(200px)', 200), ('走りと同じ高さ(62px)', 62)]
S = Image.new('RGB', (1720, 620), (40, 44, 52)); ds = ImageDraw.Draw(S)
ds.text((20, 10), title + ' — 等倍(拡大していない大きさ)', fill='white', font=f)
x = 20
for lab, h in sizes:
    for j, im in enumerate((before, after)):
        c = content(im); sc = h / c.height
        c = c.resize((max(1, int(c.width * sc)), h), Image.LANCZOS)
        pw = max(c.width + 20, 150); panel = Image.new('RGBA', (pw, 460), BG + (255,)); panel.alpha_composite(c, ((pw - c.width) // 2, 460 - h - 10))
        S.paste(panel.convert('RGB'), (x, 80)); ds.text((x, 548), '前' if j == 0 else '後', fill=(255, 220, 120), font=fs); ds.text((x, 576), lab, fill=(200, 200, 200), font=ImageFont.truetype(FONT, 15))
        x += pw + 20
    x += 30
S.save(out.replace('.jpg', '_small.jpg'), quality=95)
print('ok')

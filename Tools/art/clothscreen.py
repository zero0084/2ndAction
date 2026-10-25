"""キャラ選択画面のスクリーンショットに、立ち絵(700x1100)を同じ位置・大きさで重ねた見本を並べる(2026-10-25 布表現の見直し)。
usage: clothscreen.py <画面.png> <今の_main.png> <出力.jpg> <ラベル>=<_main.png> ...
  画面の立ち絵の位置は「今の _main の中身の範囲」と画面上の範囲(SCREEN_BOX)から求める。
  今の立ち絵は周りの色で埋めて消してから重ねる(見本なので、パネルの縁の近くは少し崩れることがある)。
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

SCREEN_BOX = (522, 183, 668)  # 1280x720 のキャラ選択で、立ち絵の中身の 左x / 上y / 下y

shot, curp, out = sys.argv[1:4]
items = [a.split('=', 1) for a in sys.argv[4:]]
S = np.array(Image.open(shot).convert('RGB')).astype(float)
cur = Image.open(curp).convert('RGBA')
bx0, by0, bx1, by1 = cur.getbbox()
s = (SCREEN_BOX[2] - SCREEN_BOX[1]) / (by1 - 1 - by0)
ox, oy = SCREEN_BOX[0] - bx0 * s, SCREEN_BOX[1] - by0 * s
size = (round(700 * s), round(1100 * s))
a = np.array(cur.resize(size, Image.LANCZOS))[..., 3] > 8
M = np.zeros(S.shape[:2], bool)
y0, x0 = round(oy), round(ox)
h, w = min(a.shape[0], S.shape[0] - y0), min(a.shape[1], S.shape[1] - x0)
M[y0:y0 + h, x0:x0 + w] = a[:h, :w]
M = ndimage.binary_dilation(M, iterations=3)
B = S.copy(); known = ~M
for _ in range(400):
    sm = ndimage.uniform_filter(B * known[..., None], size=(9, 9, 1)); cnt = ndimage.uniform_filter(known.astype(float), size=9)
    fill = (~known) & (cnt > 0.05); B[fill] = sm[fill] / cnt[fill, None]; known |= fill
    if known.all(): break
B = np.where(M[..., None], ndimage.gaussian_filter(B, (3, 3, 0)), S)
H, W = S.shape[:2]
f = ImageFont.truetype('C:/Windows/Fonts/meiryo.ttc', 22)
O = Image.new('RGB', (W, len(items) * (H + 34)), (40, 44, 52)); d = ImageDraw.Draw(O)
for k, (lab, p) in enumerate(items):
    if p == curp:
        im = Image.fromarray(S.astype(np.uint8))
    else:
        im = Image.fromarray(B.astype(np.uint8)).convert('RGBA')
        im.alpha_composite(Image.open(p).convert('RGBA').resize(size, Image.LANCZOS), (x0, y0)); im = im.convert('RGB')
    O.paste(im, (0, k * (H + 34) + 30)); d.text((10, k * (H + 34) + 2), lab, fill='white', font=f)
O.save(out, quality=92)
print('ok')

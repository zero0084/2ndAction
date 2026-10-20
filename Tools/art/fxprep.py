"""三姉妹の弾/効果の絵(マゼンタ背景)を、ゲーム用の透明 PNG(256px の正方形に収める)へ。
usage: fxprep.py <in.png> <out.png> gray|color [--size 256] [--bottom]
  gray : 白〜灰色の絵。R-G から不透明度を戻す(光のにじみも半透明で残す)
  color: 色付きの絵(黒+赤など)。マゼンタに近い所を抜き、縁の色かぶりを取る
  --bottom: 画像の下端に絵の下端を合わせる(地面から生える牙)
"""
import sys
import numpy as np
from PIL import Image

src, dst, mode = sys.argv[1], sys.argv[2], sys.argv[3]
size = int(sys.argv[sys.argv.index('--size') + 1]) if '--size' in sys.argv else 256
a = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
R, G, B = a[..., 0], a[..., 1], a[..., 2]
if mode == 'gray':
    al = np.clip(1.0 - (np.minimum(R, B) - G) / 255.0, 0, 1)
    al = np.clip((al - 0.12) / 0.88, 0, 1)  # 背景のマゼンタの揺れ(緑 10 前後)を消す
    v = np.clip(G / np.maximum(al, 1e-3), 0, 255)
    rgb = np.dstack([v, v, v])
else:
    mag = (R > 150) & (B > 150) & (G < 110) & (np.abs(R - B) < 90)
    soft = (R > 120) & (B > 120) & (G < 140) & (np.abs(R - B) < 110) & ~mag
    al = np.ones(R.shape, np.float32); al[mag] = 0; al[soft] = 0.45
    rgb = a.copy()
    rgb[soft, 2] = np.minimum(rgb[soft, 2], rgb[soft, 1] + 30)
    rgb[soft, 0] = np.minimum(rgb[soft, 0], np.maximum(rgb[soft, 1] + 80, rgb[soft, 0] * 0.8))
im = Image.fromarray(np.dstack([rgb, al * 255]).astype(np.uint8), 'RGBA')
bb = im.split()[-1].point(lambda x: 255 if x > 24 else 0).getbbox()
im = im.crop(bb)
k = (size - 8) / max(im.width, im.height)
im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)
can = Image.new('RGBA', (size, size), (0, 0, 0, 0))
y = size - im.height if '--bottom' in sys.argv else (size - im.height) // 2
can.alpha_composite(im, ((size - im.width) // 2, y))
can.save(dst)
print(dst, im.size)

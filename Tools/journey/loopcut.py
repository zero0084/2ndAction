# 背景の旅の内部の絵(2026-11-03): 繰り返しの柱(など)の中心 x0 と x1 で切り、継ぎ目を柱の上に置く(壁の模様がそのままつながる)。
#  上の方(家や空)だけ継ぎ目の前後 ov 画素をなめらかに混ぜる。高さ H にそろえ、幅を4の倍数に
#  python loopcut.py <in.png> <out.png> <x0> <x1> [--ov 80] [--h 1024]
import sys, numpy as np
from PIL import Image
a = sys.argv[1:]; src, dst, x0, x1 = a[0], a[1], int(a[2]), int(a[3])
opt = {k: float(v) for k, v in zip(a[4::2], a[5::2])}
ov = int(opt.get('--ov', 80)); H = int(opt.get('--h', 1024))
x = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
h = ov // 2
seg = x[:, x0 - h:x1 + h].copy()          # 両端に h ずつ余分
W = seg.shape[1]
t = np.linspace(0, 1, ov)[None, :, None]; t = t * t * (3 - 2 * t)
seg[:, :ov] = seg[:, W - ov:] * (1 - t) + seg[:, :ov] * t   # 左端 ov を「右端 ov の続き」として混ぜる
seg = seg[:, :W - ov]
im = Image.fromarray(np.clip(seg, 0, 255).astype(np.uint8), 'RGB')
im = im.resize((round(im.width * H / im.height) // 4 * 4, H), Image.LANCZOS)
im.save(dst); print(dst, im.size)

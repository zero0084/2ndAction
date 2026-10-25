"""布の外側の先端だけを横に寄せて、元の絵の左右の範囲に収める(2026-10-25 布表現の見直し)。
画像生成では「元の範囲に収める」が守られにくいので、体・鎧・武器の無い外側の帯だけを横方向に圧縮する。
usage: clothfit.py <in_raw.png> <out_raw.png> <side>:<x_inner>:<x_from>:<x_to>:<y0>:<y1> ...
  side=R: x_inner〜x_from の帯を x_inner〜x_to へ(右端 x_from → x_to)
  side=L: x_from〜x_inner の帯を x_to〜x_inner へ(左端 x_from → x_to)
  y0〜y1 の行だけ(上下 60px で効き目をなめらかに 0 へ)。帯の内側(x_inner より体側)は動かさない。
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src = np.array(Image.open(sys.argv[1]).convert('RGB')).astype(float)
H, W, _ = src.shape
yy, xx = np.mgrid[0:H, 0:W].astype(float)
sx = xx.copy()
FEATHER = 60.0
for spec in sys.argv[3:]:
    side, xi, xf, xt, y0, y1 = spec.split(':'); xi, xf, xt, y0, y1 = map(float, (xi, xf, xt, y0, y1))
    w = np.clip(np.minimum((yy - y0) / FEATHER + 1, (y1 - yy) / FEATHER + 1), 0, 1)
    w = w * w * (3 - 2 * w)  # smoothstep
    if side == 'R':
        band = xx > xi
        k = (xf - xi) / (xt - xi)              # 出力の1px = 元の k px
        full = xi + (xx - xi) * k
    else:
        band = xx < xi
        k = (xi - xf) / (xi - xt)
        full = xi - (xi - xx) * k
    sx = np.where(band, sx * (1 - w) + full * w, sx)
out = np.zeros_like(src)
for ch in range(3):
    out[..., ch] = ndimage.map_coordinates(src[..., ch], [yy, sx], order=1, mode='constant', cval=0)
# 範囲外(元画像の外)はマゼンタ
outside = (sx < 0) | (sx > W - 1)
out[outside] = (255, 0, 255)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(sys.argv[2])
print('ok')

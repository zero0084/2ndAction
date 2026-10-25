"""布の先端だけを縦に縮めて短く・相対的に太くする(2026-10-25 布表現の見直し、細い尾のような裾の先の手直し)。
usage: clothtip.py <in_rgba.png> <out_rgba.png> <x0> <x1> <y_start> <y_src_end> <y_dst_end>
  x0〜x1 の列だけ、y_start〜y_src_end の行を y_start〜y_dst_end へ縮める(それより下は空にする)。
  列の範囲に布の先端以外が無いことを確かめてから使う。
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

im = np.array(Image.open(sys.argv[1]).convert('RGBA')).astype(float)
x0, x1, ys, yse, yde = map(int, sys.argv[3:8])
H, W = im.shape[:2]
yy, xx = np.mgrid[0:H, x0:x1].astype(float)
k = (yse - ys) / (yde - ys)
sy = np.where(yy < ys, yy, ys + (yy - ys) * k)
out = im.copy()
# 色は不透明度を掛けてから補間(縁に背景の色が混ざらないように)
pm = im[..., :3] * (im[..., 3:4] / 255.0)
for ch in range(3):
    out[:, x0:x1, ch] = ndimage.map_coordinates(pm[..., ch], [sy, xx], order=1, mode='constant', cval=0)
out[:, x0:x1, 3] = ndimage.map_coordinates(im[..., 3], [sy, xx], order=1, mode='constant', cval=0)
a = out[:, x0:x1, 3:4] / 255.0
out[:, x0:x1, :3] = np.where(a > 0, out[:, x0:x1, :3] / np.maximum(a, 1e-6), 0)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(sys.argv[2])
print('ok')

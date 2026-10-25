"""布の修正版に、布以外(顔・髪・剣など)の元の画素を戻す(2026-10-25 布表現の見直し)。
usage: clothrestore.py <元のraw.png> <修正の位置合わせ済み透過.png> <出力.png> <y_full> <x0,y0,x1,y1>
  y_full より上の行: 元の画素をそのまま(布の無い所だけで使う)
  箱 x0,y0,x1,y1 の中: 元の「赤い布でない」画素を戻す(縁は 3px ぼかす)
"""
import sys
import os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image
from scipy import ndimage
from clothcmp import cut

O = cut(sys.argv[1]).astype(float)
V = np.array(Image.open(sys.argv[2]).convert('RGBA')).astype(float)
yfull = int(sys.argv[4]); x0, y0, x1, y1 = map(int, sys.argv[5].split(','))
H, W = O.shape[:2]
r, g, b = O[..., 0], O[..., 1], O[..., 2]
nonred = (O[..., 3] > 128) & ~((r > 90) & (r > g * 1.8) & (r > b * 1.8))
m = np.zeros((H, W)); m[:yfull] = 1
box = np.zeros((H, W), bool); box[y0:y1, x0:x1] = True
nb = ndimage.binary_opening(nonred & box, iterations=1)
lab, n = ndimage.label(nb)
if n:
    sz = ndimage.sum(nb, lab, range(1, n + 1)); nb = np.isin(lab, [i + 1 for i, v in enumerate(sz) if v >= 500])  # 裂けた布の影などの小さな塊は戻さない
m = np.maximum(m, nb.astype(float))
# 行の境目もなめらかに
ramp = np.clip((yfull + 8 - np.arange(H)) / 16.0, 0, 1)[:, None]
top = np.where(np.arange(H)[:, None] < yfull + 8, ramp, 0) * (O[..., 3] > 0) * nonred  # 上の行も赤い布は戻さない
m = np.maximum(top, nb.astype(float))
m = ndimage.gaussian_filter(m, 1.5)
out = V * (1 - m[..., None]) + O * m[..., None]
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(sys.argv[3])
print('restored px', int((m > 0.5).sum()))

"""エステルへ渡す1コマから、古い布を先に消す(消した所はマゼンタ)。人物の位置とポーズを固定したまま布だけ描き足してもらうため
(2026-10-25 布表現の見直し、双剣士の被弾で効果あり。並べた絵や普通の編集では、ポーズや頭の位置が変わることがあった)。
usage: clotherase.py <in_x4.png> <out.png> [--min 4000] [--thick 9] [--edgefrac 0.25]   布の色は CLOTH_SPEC
  布の色の大きな塊(--min px 以上)で、太さ(中心から縁までの最大)が --thick px より太い物だけ消す(細長い刃などは残す)。"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask
a = sys.argv[3:]
def opt(n, d): return a[a.index(n) + 1] if n in a else d
im = np.array(Image.open(sys.argv[1]).convert('RGB')).astype(float)
mag = (im[..., 0] > 200) & (im[..., 1] < 80) & (im[..., 2] > 200)
c = clothmask(im, os.environ['CLOTH_SPEC']) & ~mag
lb, n = ndimage.label(c); sz = ndimage.sum(c, lb, range(1, n + 1))
dt = ndimage.distance_transform_edt(c)  # 塊ごとの太さ = 塊の中の距離の最大(塊は離れているので全体で1回計算すればよい)
mx = ndimage.maximum(dt, lb, range(1, n + 1)) if n else []
bgn = ndimage.binary_dilation(mag, iterations=8)  # 4倍の絵では暗い輪郭線が数px ある
keep = np.zeros(c.shape, bool); ef = float(opt('--edgefrac', '0'))
for i, (v, t, sl) in enumerate(zip(sz, mx, ndimage.find_objects(lb))):
    if v <= float(opt('--min', '4000')) or t <= float(opt('--thick', '9')): continue
    m = lb[sl] == i + 1
    if ef > 0:  # 背景(マゼンタ)に触れる輪郭の割合が少ない塊は、体の中の同じ色の鎧(竜騎士の鱗)なので消さない
        pad = tuple(slice(max(0, x.start - 1), x.stop + 1) for x in sl); mp = lb[pad] == i + 1
        mf = ndimage.binary_fill_holes(mp); edge = mf & ~ndimage.binary_erosion(mf)
        if (edge & bgn[pad]).sum() < ef * max(1, edge.sum()): continue
    keep[sl] |= m
er = ndimage.binary_dilation(keep, iterations=3)
out = im.copy(); out[er] = [255, 0, 255]
Image.fromarray(out.astype(np.uint8)).save(sys.argv[2]); print(sys.argv[2], int(er.sum()), 'px erased')

"""エステルへ渡す1コマから、古い布を先に消す(消した所はマゼンタ)。人物の位置とポーズを固定したまま布だけ描き足してもらうため
(2026-10-25 布表現の見直し、双剣士の被弾で効果あり。並べた絵や普通の編集では、ポーズや頭の位置が変わることがあった)。
usage: clotherase.py <in_x4.png> <out.png> [--min 4000] [--thick 9]   布の色は CLOTH_SPEC
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
keep = np.zeros(c.shape, bool)
for i, v in enumerate(sz):
    if v <= float(opt('--min', '4000')): continue
    m = lb == i + 1
    if ndimage.distance_transform_edt(m).max() > float(opt('--thick', '9')): keep |= m
er = ndimage.binary_dilation(keep, iterations=3)
out = im.copy(); out[er] = [255, 0, 255]
Image.fromarray(out.astype(np.uint8)).save(sys.argv[2]); print(sys.argv[2], int(er.sum()), 'px erased')

"""capecomp.py <元の絵> <エステルの絵> <出力> [--region x0,y0,x1,y1] [--dil 8] [--feather 6] [--debug <png>]
キーアートのマントだけを差し替える(2026-10-26 アイコン/フィーチャー画像の布の見直し)。
赤い布(色相が赤・彩度が高い)の範囲を、元と新の両方で取り、合わせて少し広げた所だけ新しい絵を使う。それ以外(体・城・月・道・光)は元の画素のまま。
--region で布のある範囲(矩形)に限る(剣や道の赤い光を巻き込まないため)。境目はぼかしてなじませる。
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
def opt(n, d): a = sys.argv; return a[a.index(n) + 1] if n in a else d
op, ep, outp = sys.argv[1:4]
O = Image.open(op).convert('RGB'); E = Image.open(ep).convert('RGB').resize(O.size, Image.LANCZOS)
o = np.array(O).astype(float); e = np.array(E).astype(float)
def red(x):
    hsv = np.array(Image.fromarray(x.astype(np.uint8)).convert('HSV')).astype(float)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    return ((h < 18) | (h > 340)) & (s > 0.55) & (v > 0.25)
m = red(o) | red(e)
x0, y0, x1, y1 = [int(v) for v in opt('--region', f'0,0,{O.width},{O.height}').split(',')]
reg = np.zeros(m.shape, bool); reg[y0:y1, x0:x1] = True; m &= reg
m = ndimage.binary_closing(m, iterations=3)
m = ndimage.binary_dilation(m, iterations=int(opt('--dil', '8')))
lab, n = ndimage.label(m); sz = ndimage.sum(m, lab, range(1, n + 1)); keep = np.isin(lab, 1 + np.where(sz > 400)[0]); m &= keep
w = ndimage.gaussian_filter(m.astype(float), float(opt('--feather', '6')))[..., None]
r = e * w + o * (1 - w)
Image.fromarray(r.clip(0, 255).astype(np.uint8)).save(outp)
print(outp, 'replaced', round(float(m.mean()) * 100, 1), '%')
if '--debug' in sys.argv:
    g = np.array(O.convert('L')).astype(float) * 0.4; dbg = np.dstack([g, g, g]); dbg[m] = [255, 60, 60]
    Image.fromarray(dbg.astype(np.uint8)).save(opt('--debug', 'dbg.png'))

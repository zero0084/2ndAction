"""キャラ選択のカード(枠入りの1枚絵)の布だけを差し替える(2026-10-25 布表現の見直し)。
usage: clothcard.py <元のカード.png> <エステルの絵.png> <出力.png> [--keep x1,y1,...] [--body x1,y1,...] ... [--window x0,y0,x1,y1] [--full] [--cloth 色の範囲] [--debug <png>]
  新しい絵は「古いマント(元の大きな赤い面)」と「新しいマント(新しい絵の大きな赤い面)」とその縁だけに使い、
  それ以外(枠・紋章・文字帯・背景・人物)は元の画素のまま。--keep の多角形(腕の帯・剣の光・腰の前垂れなど体の赤)は必ず元。
  --window の外(枠と文字帯)は必ず元。透明度は元のまま(角の丸み)。
"""
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask

op, np_, outp = sys.argv[1:4]; a = sys.argv[4:]
def opt(n, d): return a[a.index(n) + 1] if n in a else d
O = Image.open(op).convert('RGBA'); W, H = O.size
N = np.array(Image.open(np_).convert('RGB').resize((W, H), Image.LANCZOS)).astype(float)
Oa = np.array(O).astype(float)

CLOTH = opt('--cloth', os.environ.get('CLOTH_SPEC'))  # 布の色(clothcmp.clothmask の書式)。無ければ赤
def redmask(img, big):
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    m = clothmask(img, CLOTH) if CLOTH else ((r - np.maximum(g, b) > 45) & (r > 90)) | ((r - np.maximum(g, b) > 30) & (r > 60) & (g < r * 0.45))  # 暗い赤の切れ端も
    m = ndimage.binary_opening(m, iterations=1)
    lab, n = ndimage.label(m)
    if n == 0: return m
    sz = ndimage.sum(m, lab, range(1, n + 1))
    return np.isin(lab, [i + 1 for i, v in enumerate(sz) if v >= big])

big = int(opt('--big', '600'))
oldc = redmask(Oa, big); newc = redmask(N, big)
take = ndimage.binary_dilation(oldc, iterations=3) | ndimage.binary_dilation(newc, iterations=2)
take = ndimage.binary_closing(take, iterations=3)
x0, y0, x1, y1 = map(int, opt('--window', f'14,24,{W - 14},776').split(','))
win = np.zeros((H, W), bool); win[y0:y1, x0:x1] = True
KP = np.zeros((H, W), bool)
for i, t in enumerate(a):
    if t == '--keep' or t == '--frame':  # --frame: 枠の飾りなど、窓の中でも元のままにする所
        v = list(map(float, a[i + 1].split(',')))
        im = Image.new('L', (W, H), 0); ImageDraw.Draw(im).polygon(list(zip(v[0::2], v[1::2])), fill=255)
        KP |= np.array(im) > 0
take &= win & ~KP
if '--full' in a: take = win & ~KP  # 窓の中は新しい絵をそのまま(体の位置が変わっていて布だけでは継ぎ目が出る時。体の描き直しとして記録する)
# --body 多角形: 新しい絵で位置が変わった体の部分(右腕など)。範囲の中は元の体を残し、元の古いマント(赤)だった所だけを
# 新しいマントの赤で埋める(新しい絵のその画素が赤くなければ、まわりの新しいマントの色をぼかして使う)
BD = np.zeros((H, W), bool)
for i, t in enumerate(a):
    if t == '--body':
        v = list(map(float, a[i + 1].split(',')))
        im = Image.new('L', (W, H), 0); ImageDraw.Draw(im).polygon(list(zip(v[0::2], v[1::2])), fill=255)
        BD |= np.array(im) > 0
if BD.any():
    rawO = redmask(Oa, 1); rawN = redmask(N, 1)
    fillp = BD & rawO & ~KP  # 腕のすき間に見えていた古いマント
    wgt = ndimage.gaussian_filter(rawN.astype(float), 4)
    col = np.dstack([ndimage.gaussian_filter(N[..., c] * rawN, 4) / np.maximum(wgt, 1e-3) for c in range(3)])
    N = np.where((fillp & ~rawN)[..., None], col, N)  # 範囲は体の輪郭に沿わせる(外の背景まで含めると、背景がマントの色で埋まる)
    take = (take & ~BD) | fillp
m = ndimage.gaussian_filter(take.astype(float), 1.0)
m = np.where(take, np.maximum(m, 0.0), m) * win * ~KP
m = np.clip(m * 1.6, 0, 1)
if BD.any(): m[BD & ~take] = 0  # 体の範囲では、ぼかしの縁でも新しい絵の体を混ぜない
m = m[..., None]
out = Oa.copy()
out[..., :3] = Oa[..., :3] * (1 - m) + N * m
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(outp)
print(dict(old_cape=int(oldc.sum()), new_cape=int(newc.sum()), replaced=int((m[..., 0] > 0.5).sum()), total=W * H))
dbg = opt('--debug', None)
if dbg:
    v = (Oa[..., :3] * 0.35).astype(np.uint8)
    v[oldc] = (40, 150, 255); v[newc & ~oldc] = (220, 50, 50); v[newc & oldc] = (200, 60, 200)
    v[KP] = (v[KP] * 0.3 + np.array([0, 200, 0]) * 0.7).astype(np.uint8)
    v[~win] = (v[~win] * 0.3).astype(np.uint8)
    Image.fromarray(v).save(dbg)

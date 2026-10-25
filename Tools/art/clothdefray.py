"""元の画素のまま、布の破れ・ほつれ・穴・点状の傷み・半透明に消える端を整える(2026-10-26 布表現の見直し)。
エステルの描き直しがポーズに合わないキャラ(拳銃士: コート・髪・肌が同じ茶色)や、形は意匠として残すキャラ(吸血鬼)向け。
体の画素は変えない。布の色(--cloth、無ければ CLOTH_SPEC)で、範囲(--below 割合: 外接の箱の上からこの割合より下)の中だけ:
  ① 外側の細い突起(ほつれ・裂けた先)を落とす(--open px の円で開く。背景に接する物だけ)
  ② 布に囲まれた小さな穴(--hole px² 以下)を周りの布の色で埋める
  ③ 半透明に消えかけた布の端を、不透明にするか消す
  ④ 布の中の点状の斑点を和らげる(--median)
usage: clothdefray.py <in.png> <out.png> [--cloth spec] [--below 0.45] [--open 2] [--hole 400] [--median 3] [--debug dbg.png]"""
import sys, os
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask
a = sys.argv[3:]
def opt(n, d): return a[a.index(n) + 1] if n in a else d
spec = opt('--cloth', os.environ.get('CLOTH_SPEC', ''))
im = np.array(Image.open(sys.argv[1]).convert('RGBA')).astype(float); al = im[..., 3]
H, W = al.shape; ys = np.where((al > 30).any(axis=1))[0]
reg = np.zeros((H, W), bool)
if len(ys): reg[int(ys[0] + float(opt('--below', '0')) * (ys[-1] - ys[0])):] = True
cl = clothmask(im[..., :3], spec) & (al > 20) & reg
clb = ndimage.binary_closing(cl, iterations=1)
r = int(opt('--open', '2')); st = ndimage.generate_binary_structure(2, 1)
disk = ndimage.iterate_structure(st, r)
# ① 背景に接する細い突起: 布の不透明を開いて消える所のうち、背景に近い所
fig = al > 30; bgd = ndimage.binary_dilation(~fig, iterations=r + 1)
opened = ndimage.binary_opening(clb, structure=disk)
spur = cl & ~opened & bgd
# 突起のうち、開いた布の本体からも離れている所だけ(本体の輪郭は残す)
spur &= ~ndimage.binary_dilation(opened, iterations=1) | (al < 200)
# ② 布に囲まれた穴(透明・半透明): 布+体を塞いで、塞がった小さな穴
solid = fig | clb
filled = ndimage.binary_fill_holes(ndimage.binary_closing(solid, iterations=2))
holes = filled & ~fig & reg
lbh, nh = ndimage.label(holes); hs = ndimage.sum(holes, lbh, range(1, nh + 1)) if nh else []
holes = np.isin(lbh, [i + 1 for i, s in enumerate(hs) if s <= float(opt('--hole', '400'))])
# 穴の周りが主に布の時だけ埋める(腕と体の間のすき間などは残す)
ring = ndimage.binary_dilation(holes, iterations=2) & ~holes
lbr, _ = ndimage.label(holes)
keepH = np.zeros_like(holes)
for i, sl in enumerate(ndimage.find_objects(lbr)):
    if sl is None: continue
    pad = tuple(slice(max(0, s.start - 3), s.stop + 3) for s in sl); m = lbr[pad] == i + 1
    rg = ndimage.binary_dilation(m, iterations=2) & ~m
    if (rg & cl[pad]).sum() >= 0.6 * max(1, rg.sum()): keepH[pad] |= m
out = im.copy()
if keepH.any():
    src = cl & (al > 200)
    _, (iy, ix) = ndimage.distance_transform_edt(~src, return_indices=True)
    out[keepH, :3] = im[iy[keepH], ix[keepH], :3]; out[keepH, 3] = 255
# ③ 半透明に消えかけた布の端: 本体の布に近ければ不透明に、離れていれば消す
semi = cl & (al > 20) & (al < 200)
near = ndimage.binary_dilation(cl & (al >= 200), iterations=2)
out[semi & near, 3] = 255; out[semi & ~near, 3] = 0
out[spur, 3] = 0
# ④ 点状の斑点: 布の中だけ色を中央値で和らげる
mk = int(opt('--median', '3'))
if mk > 1:
    med = np.stack([ndimage.median_filter(im[..., c], size=mk) for c in range(3)], -1)
    inner = ndimage.binary_erosion(cl & (al > 200), iterations=1)
    out[inner, :3] = 0.5 * out[inner, :3] + 0.5 * med[inner]
# 切った縁を1px なめらかに
edge = ndimage.binary_dilation(spur, iterations=1) & (out[..., 3] > 0) & reg
if edge.any():
    sa = ndimage.gaussian_filter(out[..., 3], 0.7); out[edge, 3] = np.minimum(out[edge, 3], sa[edge])
Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(sys.argv[2])
if '--debug' in a:
    bg = np.zeros_like(im); bg[...] = [120, 130, 140, 255]; v = im[..., :3] * (al[..., None] / 255) + bg[..., :3] * (1 - al[..., None] / 255)
    v[spur] = [255, 0, 0]; v[keepH] = [0, 255, 0]; v[semi] = [255, 255, 0]
    Image.fromarray(v.astype(np.uint8)).save(opt('--debug', ''))
print(os.path.basename(sys.argv[2]), {'spur_px': int(spur.sum()), 'holes_px': int(keepH.sum()), 'semi_px': int(semi.sum())})

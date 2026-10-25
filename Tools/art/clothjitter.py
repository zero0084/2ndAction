"""元と合成のコマで、頭のてっぺん・足元・体の中心(布でない不透明の所)がずれていないかを数える(2026-10-25 布表現の見直し)。
usage: clothjitter.py <元のフォルダの親> <合成のフォルダの親> <サブフォルダ> ...  (布の色は CLOTH_SPEC)
  1コマごとに dTop/dBot/dCx(px)と体の一致度(Dice)。|d|>2px か Dice<0.85 に印(*)。"""
import sys, os, glob
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import body_mask
od, nd = sys.argv[1:3]
for sub in sys.argv[3:]:
    for f in sorted(glob.glob(os.path.join(nd, sub, '*.png'))):
        o = body_mask(np.array(Image.open(os.path.join(od, sub, os.path.basename(f))).convert('RGBA'))) > 0
        n = body_mask(np.array(Image.open(f).convert('RGBA'))) > 0
        def ext(m):
            ys, xs = np.where(m); return ys.min(), ys.max(), xs.mean()
        (ot, ob, ox), (nt, nb, nx) = ext(o), ext(n)
        dice = 2 * (o & n).sum() / (o.sum() + n.sum())
        d = (nt - ot, nb - ob, round(nx - ox, 1))
        flag = '*' if max(abs(v) for v in d) > 2 or dice < 0.85 else ' '
        print(f'{flag} {sub}/{os.path.basename(f)}  dTop {d[0]:+d} dBot {d[1]:+d} dCx {d[2]:+.1f}  dice {dice:.3f}')

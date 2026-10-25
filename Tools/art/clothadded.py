"""合成したコマに、元に無かった「布でない物」が足されていないかを数える(2026-10-25 布表現の見直し)。
エステルが元に無い物(魔法使いの走りの本など)を描き足すと、agree 合成では元が透明な所は新しい絵になるので、そのまま残るため。
元で透明かつ元の輪郭から --d px より離れた所にある、布の色でない不透明な画素の塊を数え、--min px 以上の塊があるコマに印。
usage: clothadded.py <id> [--d 6] [--min 250]"""
import sys, os, glob
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask
a = sys.argv[1:]; id = a[0]
def opt(n, d): return float(a[a.index(n) + 1]) if n in a else d
W = f'Docs/ClothRevision_2026-10-25/chars/{id}'; O = 'Docs/ClothRevision_2026-10-25/ingame/original/Art'
spec = [l.split('=', 1)[1].strip() for l in open(W + '.txt', encoding='utf-8') if l.startswith('CLOTH_SPEC=')][0]
bad = []
for f in sorted(glob.glob(W + '/local/**/*.png', recursive=True)):
    rel = os.path.relpath(f, W + '/local').replace(chr(92), '/'); of = os.path.join(O, rel)
    if not os.path.exists(of): continue
    n = np.array(Image.open(f).convert('RGBA')).astype(float); o = np.array(Image.open(of).convert('RGBA')).astype(float)
    far = ndimage.distance_transform_edt(o[..., 3] < 30) > opt('--d', 6)
    nc = clothmask(n[..., :3], spec) & (n[..., 3] > 100)
    add = far & (n[..., 3] > 128) & ~ndimage.binary_dilation(nc, iterations=int(opt('--near', 6)))  # 新しい布の刺繍・縁の線・陰は除く
    lb, k = ndimage.label(add)
    sz = ndimage.sum(add, lb, range(1, k + 1)) if k else []
    big = [int(s) for s in sz if s >= opt('--min', 250)]
    if big: bad.append(rel); print(f'* {rel:50s} {sorted(big)[::-1][:4]}')
print('ADDED', len(bad))

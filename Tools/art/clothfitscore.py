"""aligned(位置合わせ後の新しい絵)と元のコマの体の一致度(Dice)を一覧に。低いコマはポーズが変わっているので1コマずつ描き直す候補。
usage: clothfitscore.py <id> [しきい値=0.8]   (布の色は chars/<id>.txt)"""
import sys, os, glob
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
cid = sys.argv[1]; th = float(sys.argv[2]) if len(sys.argv) > 2 else 0.8
D = 'Docs/ClothRevision_2026-10-25'; cf = open(f'{D}/chars/{cid}.txt', encoding='utf-8').read()
os.environ['CLOTH_SPEC'] = ([l.split('=', 1)[1] for l in cf.splitlines() if l.startswith('FIT_SPEC=')] + [l.split('=', 1)[1] for l in cf.splitlines() if l.startswith('CLOTH_SPEC=')])[0]  # FIT_SPEC: 位置合わせ用(白銀の鎧を布と見なさない等)
from clothcmp import body_mask, clothmask
from scipy import ndimage
bad = []
for f in sorted(glob.glob(f'{D}/chars/{cid}/aligned/**/*.png', recursive=True)):
    rel = os.path.relpath(f, f'{D}/chars/{cid}/aligned').replace(os.sep, '/')
    # 布の色の所(元か新しい絵のどちらか)と、元の布の近くの暗い所(古い布の影)を除いた体どうしで比べる
    OA = np.array(Image.open(f'{D}/ingame/original/Art/{rel}').convert('RGBA')).astype(float); NA = np.array(Image.open(f).convert('RGBA')).astype(float)
    spec = os.environ['CLOTH_SPEC']; oc = clothmask(OA, spec) & (OA[..., 3] > 30); nc = clothmask(NA, spec) & (NA[..., 3] > 30)
    ex = ndimage.binary_dilation(oc | nc, iterations=6)
    o = ndimage.binary_erosion((OA[..., 3] > 128) & ~ex, iterations=2); n = ndimage.binary_erosion((NA[..., 3] > 128) & ~ex, iterations=2)
    d = 2 * (o & n).sum() / (o.sum() + n.sum() + 1e-9)
    if d < th: bad.append(rel[:-4])
    print(f'{d:.3f} {rel}')
print('LOW', len(bad), ' '.join(bad))

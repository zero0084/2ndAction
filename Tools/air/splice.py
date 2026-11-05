"""シートの 1 コマだけを描き直した絵に差し替える(2026-11-05)。
  python splice.py <sheet.png> <index(0始まり)> <new_pose.png> <out_sheet.png> [--bg magenta|green] [--scale 1.0]
  新しい絵の不透明部分を、元のコマの不透明部分と同じ高さ(× scale)に縮め、元のコマの中心に置く。元のコマは背景色で消す。
  そのあと slice.py に通せば、他のコマと同じ縮尺・位置合わせになる。
"""
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, __import__('os').path.dirname(__file__))
import slice as S

args = sys.argv[1:]
src, idx, newp, out = args[0], int(args[1]), args[2], args[3]
bg = 'magenta'; scale = 1.0
if '--bg' in args: bg = args[args.index('--bg') + 1]
if '--scale' in args: scale = float(args[args.index('--scale') + 1])
bgc = (0, 255, 0) if bg == 'green' else (255, 0, 255)

sheet = np.asarray(Image.open(src).convert('RGB')).copy()
alpha = S.cutout(sheet, bg)[..., 3]
groups = S.segments(alpha, 8)
x0, x1 = groups[idx]
sub = alpha[:, x0:x1] > 8
ys = np.nonzero(sub.any(axis=1))[0]
oy0, oy1 = ys[0], ys[-1] + 1
ocx, ocy = (x0 + x1) / 2, (oy0 + oy1) / 2
# 元のコマを消す(切った範囲の列・不透明の所だけ)
region = sheet[:, x0:x1]
region[sub] = bgc

new = np.asarray(Image.open(newp).convert('RGB'))
na = S.cutout(new, bg)
m = na[..., 3] > 8
ys, xs = np.nonzero(m)
crop = na[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
h = (oy1 - oy0) * scale
k = h / crop.shape[0]
im = Image.fromarray(crop, 'RGBA').resize((max(1, int(crop.shape[1] * k)), max(1, int(crop.shape[0] * k))), Image.LANCZOS)
base = Image.fromarray(sheet, 'RGB')
base.paste(im, (int(ocx - im.width / 2), int(ocy - im.height / 2)), im)
base.save(out)
print('spliced', idx, 'size', im.size, 'at', (int(ocx), int(ocy)))

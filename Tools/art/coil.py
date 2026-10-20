# (2026-10-20 大蛇: 這う1枚目の帯 315〜1065 に合わせる。別のボスは中心を読み替えて使う)
# coil.py <src> <out> <name|-> [scale] [width] [prev...] : とぐろ(下から130pxの帯)の左右の中心を這う1枚目の中心(690)に合わせて置く
import sys, subprocess
sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
import numpy as np
from PIL import Image
src, out, name = sys.argv[1:4]
sc = sys.argv[4] if len(sys.argv) > 4 else '0.88'; W = sys.argv[5] if len(sys.argv) > 5 else '1700'; prev = sys.argv[6:]
a = np.asarray(key(Image.open(src)))[..., 3] > 0
g = int(np.nonzero(a)[0].max()) + 1
xs = np.nonzero(a[g - 130:g].any(0))[0]
cx = (xs.min() + xs.max()) / 2  # 這う1枚目の帯は 315〜1065(中心 690)
subprocess.run([sys.executable, '-I', r'C:/GameProject/2ndAction/Tools/art/riderpose.py', 'place', src, out, '--scale', sc, '--ground', str(g), '--cx', f'{cx:.0f}', '--width', W], check=True, capture_output=True)
if name != '-': open('frames.tsv', 'a', encoding='utf-8').write(f'{name}\t{src}\t{sc}\t{cx:.0f}\t{g}\t{W}\n')
subprocess.run([sys.executable, '-I', 'sheet.py', 'seq.jpg'] + prev + [out], check=True)
print('ground', g, 'cx', round(cx), 'band', xs.min(), xs.max())

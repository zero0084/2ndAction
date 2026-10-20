# despill.py <src> <out> : マゼンタ寄り(ピンク・紫)の画素を明るさを保った白〜灰へ直す(残像の弧や縁の色かぶり)。背景は純マゼンタに敷き直す
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
import numpy as np
from PIL import Image
k = np.asarray(key(Image.open(sys.argv[1]))).astype(np.float32)
r, g, b, a = k[..., 0], k[..., 1], k[..., 2], k[..., 3]
m = (a > 0) & (r > g + 25) & (b > g + 25)
L = 0.3 * r + 0.59 * g + 0.11 * b
v = np.clip(L * 1.15 + 30, 0, 255)
for c in range(3): k[..., c][m] = v[m]
print('fixed', int(m.sum()))
al = a[..., None] / 255.0
rgb = k[..., :3] * al + np.array([255, 0, 255], np.float32) * (1 - al)
Image.fromarray(rgb.astype(np.uint8), 'RGB').save(sys.argv[2])

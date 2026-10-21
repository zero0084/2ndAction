# degreen.py <src> <out> : 緑背景で生成した絵の、体に移った緑かぶりを消す(緑が赤青の大きい方を超える分を削る)。背景は純緑に敷き直す
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
import numpy as np
from PIL import Image
k = np.asarray(key(Image.open(sys.argv[1]))).astype(np.float32)
r, g, b, a = k[..., 0], k[..., 1], k[..., 2], k[..., 3]
m = (a > 0) & (g > np.maximum(r, b) + 6)
k[..., 1][m] = np.maximum(r, b)[m] + 6
print('fixed', int(m.sum()))
al = a[..., None] / 255.0
rgb = k[..., :3] * al + np.array([0, 255, 0], np.float32) * (1 - al)
Image.fromarray(rgb.astype(np.uint8), 'RGB').save(sys.argv[2])

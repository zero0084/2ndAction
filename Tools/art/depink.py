# depink.py <src> <out> : 背景のマゼンタが移ったピンクの砂ぼこり等を砂色へ直し、背景を純マゼンタに敷き直す(auto.py/install.py にそのまま渡せる)
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
import numpy as np
from PIL import Image
k = np.asarray(key(Image.open(sys.argv[1]))).astype(np.float32)
r, g, b, a = k[..., 0], k[..., 1], k[..., 2], k[..., 3]
L = 0.3 * r + 0.59 * g + 0.11 * b
pink = (a > 0) & (((r > g + 25) & (b > g + 15)) | ((r > g + 10) & (b > g - 20) & (L > 110) & (np.arange(a.shape[0])[:, None] > a.shape[0] * 0.6)))
L = 0.3 * r + 0.59 * g + 0.11 * b
for c, f in ((0, 1.08), (1, 0.93), (2, 0.74)): k[..., c][pink] = np.clip(L[pink] * f, 0, 255)
print('fixed', int(pink.sum()))
al = a[..., None] / 255.0
rgb = k[..., :3] * al + np.array([255, 0, 255], np.float32) * (1 - al)
Image.fromarray(rgb.astype(np.uint8), 'RGB').save(sys.argv[2])

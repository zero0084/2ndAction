# colormatch.py <src> <ref> <out> : 体全体(不透明な画素)の色の平均と幅を ref に合わせる(生成ごとの色ずれを直す。形は変えない)。背景は src の背景色で敷き直す
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key, is_green_bg
import numpy as np
from PIL import Image
si = Image.open(sys.argv[1]); s = np.asarray(key(si)).astype(np.float32); r = np.asarray(key(Image.open(sys.argv[2]))).astype(np.float32)
ms = s[..., 3] > 200; mr = r[..., 3] > 200
for c in range(3):
    a, b = s[..., c][ms], r[..., c][mr]
    s[..., c][s[..., 3] > 0] = np.clip((s[..., c][s[..., 3] > 0] - a.mean()) / max(a.std(), 1) * b.std() + b.mean(), 0, 255)
print('src', s[..., :3][ms].mean(0).round(1), 'ref', r[..., :3][mr].mean(0).round(1))
bg = np.array([0, 255, 0] if is_green_bg(np.asarray(si.convert('RGB')).astype(np.int16)) else [255, 0, 255], np.float32)
al = s[..., 3:4] / 255.0
Image.fromarray((s[..., :3] * al + bg * (1 - al)).astype(np.uint8), 'RGB').save(sys.argv[3])

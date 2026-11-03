# 背景の旅の内部の絵(2026-11-03): 下の方を少し暗く・彩度を下げる(地面・敵・弾が重なる所。上から from の割合から下端で mul 倍)
#  python shade.py <in/out.png> [--from 0.45] [--mul 0.78] [--desat 0.25]
import sys, numpy as np
from PIL import Image
p = sys.argv[1]; opt = {k: float(v) for k, v in zip(sys.argv[2::2], sys.argv[3::2])}
f0, mul, ds = opt.get('--from', 0.45), opt.get('--mul', 0.78), opt.get('--desat', 0.25)
x = np.asarray(Image.open(p).convert('RGB')).astype(np.float32); H = x.shape[0]
t = np.clip((np.arange(H) / H - f0) / (1 - f0), 0, 1)[:, None, None]; t = t * t * (3 - 2 * t)
lum = x.mean(axis=2, keepdims=True)
x = x + (lum - x) * ds * t
x = x * (1 - (1 - mul) * t)
Image.fromarray(np.clip(x, 0, 255).astype(np.uint8)).save(p); print(p)

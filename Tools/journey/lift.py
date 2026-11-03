# 内部の絵の上の方だけ明るく、結晶(彩度の高い明るい所)の光を少し強める(2026-11-04、洞窟の「入った」感。下 6 割は足場/敵/弾が重なるので変えない)
#  python lift.py <in> <out> [--top 0.4 --gain 1.12 --glow 1.12]
#   --top: 上からこの割合までを gain 倍(その下 10% の幅でなめらかに 1 倍へ)/ --glow: 結晶の光(彩度 0.3 以上・明るさ 0.3 以上)の明るさの倍率
import sys, argparse
import numpy as np
from PIL import Image
ap = argparse.ArgumentParser(); ap.add_argument('src'); ap.add_argument('dst')
ap.add_argument('--top', type=float, default=0.4); ap.add_argument('--gain', type=float, default=1.12); ap.add_argument('--glow', type=float, default=1.12)
a = ap.parse_args()
im = Image.open(a.src).convert('RGBA'); px = np.asarray(im).astype(np.float32) / 255.0
h = px.shape[0]
y = np.arange(h, dtype=np.float32) / h
w = np.clip((a.top + 0.1 - y) / 0.1, 0.0, 1.0)            # 上 top は 1、その下 10% で 0 へ
w = w * w * (3 - 2 * w)
g = 1.0 + (a.gain - 1.0) * w[:, None]
rgb = px[..., :3]
mx = rgb.max(axis=2); mn = rgb.min(axis=2); sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
glow = 1.0 + (a.glow - 1.0) * np.clip((sat - 0.3) / 0.2, 0, 1) * np.clip((mx - 0.3) / 0.2, 0, 1)
rgb = np.clip(rgb * g[..., None] * glow[..., None], 0, 1)
out = np.concatenate([rgb, px[..., 3:]], axis=2)
Image.fromarray((out * 255 + 0.5).astype(np.uint8), 'RGBA').save(a.dst)
print(a.dst, im.size)

# 背景の旅の素材(2026-11-03): 結晶などの色違いを作る。中心の色相 center(度)の ±width 度の画素だけ deg 度回す
#  (岩などの他の色はそのまま)。彩度 sat 倍・明るさ val 倍も同じ範囲だけ。透明度はそのまま
#  python hue.py <in.png> <out.png> <deg> [--center 215] [--width 55] [--sat 1.0] [--val 1.0]
import sys, numpy as np
from PIL import Image
a = sys.argv[1:]; src, dst, deg = a[0], a[1], float(a[2])
opt = {k: float(v) for k, v in zip(a[3::2], a[4::2])}
c0, wd = opt.get('--center', 215.0), opt.get('--width', 55.0)
im = Image.open(src).convert('RGBA'); al = im.getchannel('A')
hsv = np.asarray(im.convert('RGB').convert('HSV')).astype(np.float32)
h = hsv[..., 0] / 255.0 * 360.0
dist = np.abs((h - c0 + 180.0) % 360.0 - 180.0)
w = np.clip(1.0 - (dist - wd * 0.6) / (wd * 0.4), 0, 1) * np.clip(hsv[..., 1] / 60.0, 0, 1)  # 範囲の端と灰色はなめらかに弱める
hsv[..., 0] = ((h + deg * w) % 360.0) / 360.0 * 255.0
hsv[..., 1] = np.clip(hsv[..., 1] * (1 + (opt.get('--sat', 1.0) - 1) * w), 0, 255)
hsv[..., 2] = np.clip(hsv[..., 2] * (1 + (opt.get('--val', 1.0) - 1) * w), 0, 255)
out = Image.fromarray(hsv.astype(np.uint8), 'HSV').convert('RGBA'); out.putalpha(al); out.save(dst); print(dst, out.size)

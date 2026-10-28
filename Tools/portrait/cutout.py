# 立ち絵の切り抜きと配置(2026-10-28、キャラ選択の立ち絵の見直し)
#  python cutout.py <生成画像.png> <今の立ち絵 <id>_main.png> <出力.png> [--key 00ff00] [--scale 1.0]
#  ・単色の背景(既定 緑 #00FF00)を色の距離で透明にし、縁の色かぶり(緑の照り返し)を取る
#  ・小さな離れたかけら(背景のむら)を消す
#  ・今の立ち絵と同じ 700×1100 に、同じ高さ(人物の上端〜足元)・同じ足元の位置・同じ左右の中心で置く
import sys, argparse
import numpy as np
from PIL import Image
from scipy import ndimage

ap = argparse.ArgumentParser()
ap.add_argument("src"); ap.add_argument("ref"); ap.add_argument("out")
ap.add_argument("--key", default="00ff00")
ap.add_argument("--scale", type=float, default=1.0)   # 今の立ち絵の高さに対する倍率(武器が長い時に少し小さく等)
ap.add_argument("--lo", type=float, default=60.0)     # これより背景色に近い = 透明
ap.add_argument("--hi", type=float, default=140.0)    # これより遠い = 不透明
a = ap.parse_args()

key = np.array([int(a.key[i:i + 2], 16) for i in (0, 2, 4)], float)
im = np.array(Image.open(a.src).convert("RGB")).astype(float)
d = np.sqrt(((im - key) ** 2).sum(-1))
alpha = np.clip((d - a.lo) / (a.hi - a.lo), 0, 1)
# 色かぶりの除去: 背景色の成分が他より強い所を抑える
if key[1] > 200 and key[0] < 60 and key[2] < 60:      # 緑
    g = im[:, :, 1]; lim = np.maximum(im[:, :, 0], im[:, :, 2])
    im[:, :, 1] = np.where(g > lim, lim, g)  # 緑は他の成分より上げない(髪の毛先に緑が残っていた)
elif key[2] > 200 and key[0] < 60 and key[1] < 60:   # 青
    b = im[:, :, 2]; lim = np.maximum(im[:, :, 0], im[:, :, 1])
    im[:, :, 2] = np.where(b > lim, lim, b)
elif key[0] > 200 and key[2] > 200 and key[1] < 60:   # マゼンタ
    rb = np.minimum(im[:, :, 0], im[:, :, 2]); gg = im[:, :, 1]
    over = np.clip(rb - gg, 0, None)
    im[:, :, 0] -= over * 0.8; im[:, :, 2] -= over * 0.8
# 離れた小さなかけらを消す(本体の連結部分だけ残す。面積の 0.3% 未満は捨てる)
lab, n = ndimage.label(alpha > 0.5)
if n > 1:
    sizes = ndimage.sum(np.ones_like(alpha), lab, range(1, n + 1))
    keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= sizes.max() * 0.003])
    keep = ndimage.binary_dilation(keep, iterations=3)
    alpha = alpha * keep
rgba = np.dstack([np.clip(im, 0, 255), alpha * 255]).astype(np.uint8)
src = Image.fromarray(rgba, "RGBA")
bb = src.getbbox()
src = src.crop(bb)

ref = Image.open(a.ref).convert("RGBA")
rb = ref.getbbox()  # 今の立ち絵の人物の範囲
W, H = ref.size
th = (rb[3] - rb[1]) * a.scale
s = th / src.height
nw, nh = int(round(src.width * s)), int(round(src.height * s))
if nw > W - 8: s = (W - 8) / src.width; nw, nh = W - 8, int(round(src.height * s))
src = src.resize((nw, nh), Image.LANCZOS)
cx = (rb[0] + rb[2]) / 2
x = int(round(cx - nw / 2)); x = max(4, min(W - 4 - nw, x))
y = int(round(rb[3] - nh)); y = max(0, y)
out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
out.alpha_composite(src, (x, y))
out.save(a.out)
print(f"placed {nw}x{nh} at ({x},{y}) in {W}x{H}; ref figure {rb}; scale {s:.3f}")

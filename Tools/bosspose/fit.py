# ボスの姿勢の絵(エステル生成、単色背景)を切り抜き、今の絵と同じキャンバス・同じ大きさに置く(2026-10-28)
#  python fit.py <生成画像> <今の絵(大きさの基準)> <出力.png> [--key 00ff00] [--mode area|height|width] [--mul 1.0] [--anchor center|bottom] [--dx 0 --dy 0]
#  ・背景色を色の距離で透明にし、縁の色かぶりを取る。離れた小さなかけらは消す
#  ・大きさ: --mode area = 不透明な面積を基準の絵と同じに(翼の開き具合で変わる竜などは --mul で調整)
#             height/width = 人物の高さ/幅を基準と同じに
#  ・置き方: center = 体の重心を基準の絵の重心へ / bottom = 足元(下端)を基準の下端・左右の中心へ
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage

ap = argparse.ArgumentParser()
ap.add_argument("src"); ap.add_argument("ref"); ap.add_argument("out")
ap.add_argument("--key", default="00ff00")
ap.add_argument("--mode", default="area")
ap.add_argument("--mul", type=float, default=1.0)
ap.add_argument("--anchor", default="center")
ap.add_argument("--dx", type=float, default=0); ap.add_argument("--dy", type=float, default=0)
ap.add_argument("--lo", type=float, default=70.0); ap.add_argument("--hi", type=float, default=150.0)
ap.add_argument("--flip", action="store_true")       # 向きが逆に描かれた時
ap.add_argument("--color", type=float, default=0.0)  # 0..1 色味を基準の絵へ寄せる強さ
ap.add_argument("--grow", action="store_true")       # はみ出す時はキャンバスを左右対称に広げる/上へ伸ばす(下端の中央ピボットのボス向け。4の倍数)
ap.add_argument("--fade", type=float, default=0.0)   # 切り抜いた絵の上と左右の端から何 px で透明へ薄める(雲や光が生成画像の端で真っすぐ切れる時)
a = ap.parse_args()

key = np.array([int(a.key[i:i + 2], 16) for i in (0, 2, 4)], float)
im = np.array(Image.open(a.src).convert("RGB")).astype(float)
d = np.sqrt(((im - key) ** 2).sum(-1))
alpha = np.clip((d - a.lo) / (a.hi - a.lo), 0, 1)
if key[1] > 200 and key[0] < 80 and key[2] < 80:       # 緑
    lim = np.maximum(im[:, :, 0], im[:, :, 2]); im[:, :, 1] = np.where(im[:, :, 1] > lim, lim, im[:, :, 1])
elif key[0] > 200 and key[2] > 200 and key[1] < 80:     # マゼンタ
    over = np.clip(np.minimum(im[:, :, 0], im[:, :, 2]) - im[:, :, 1], 0, None); im[:, :, 0] -= over; im[:, :, 2] -= over
elif key[2] > 200 and key[0] < 80 and key[1] < 80:      # 青
    lim = np.maximum(im[:, :, 0], im[:, :, 1]); im[:, :, 2] = np.where(im[:, :, 2] > lim, lim, im[:, :, 2])
lab, n = ndimage.label(alpha > 0.5)
if n > 1:
    sizes = ndimage.sum(np.ones_like(alpha), lab, range(1, n + 1))
    keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= sizes.max() * 0.004])
    alpha = alpha * ndimage.binary_dilation(keep, iterations=3)
src = Image.fromarray(np.dstack([np.clip(im, 0, 255), alpha * 255]).astype(np.uint8), "RGBA")
if a.flip: src = src.transpose(Image.FLIP_LEFT_RIGHT)
src = src.crop(src.getbbox())

ref = Image.open(a.ref).convert("RGBA")
RW, RH = ref.size
ra = np.array(ref)[:, :, 3] > 128
sa = np.array(src)[:, :, 3] > 128
rb = ref.getbbox()
if a.mode == "height": s = (rb[3] - rb[1]) / src.height
elif a.mode == "width": s = (rb[2] - rb[0]) / src.width
else: s = np.sqrt(ra.sum() / max(1, sa.sum()))
s *= a.mul
src = src.resize((max(1, int(src.width * s)), max(1, int(src.height * s))), Image.LANCZOS)
out = Image.new("RGBA", (RW, RH), (0, 0, 0, 0))
if a.anchor == "top":  # 浮いている体(クラゲなど): 上端(傘)を基準の上端・左右の中心へ
    x = (rb[0] + rb[2]) / 2 - src.width / 2; y = rb[1]
elif a.anchor == "bottom":
    x = (rb[0] + rb[2]) / 2 - src.width / 2; y = rb[3] - src.height
else:
    ry, rx = ndimage.center_of_mass(ra)
    sy, sx = ndimage.center_of_mass(np.array(src)[:, :, 3] > 128)
    x = rx - sx; y = ry - sy
x += a.dx; y += a.dy
# 色合わせ(--color): 生成で赤が橙に寄るなど、今の絵と色味がずれる時。不透明な所の明るさ/色差(YCbCr)の平均と広がりを基準の絵へ寄せる
if a.color > 0:
    def ycc(img): return np.array(img.convert("RGB").convert("YCbCr")).astype(float)
    sy = ycc(src); ry_ = ycc(ref)
    sm = np.array(src)[:, :, 3] > 200; rm = np.array(ref)[:, :, 3] > 200
    for c in range(3):
        ms, ss = sy[:, :, c][sm].mean(), sy[:, :, c][sm].std() + 1e-3
        mr, sr = ry_[:, :, c][rm].mean(), ry_[:, :, c][rm].std() + 1e-3
        sy[:, :, c] = sy[:, :, c] * (1 - a.color) + ((sy[:, :, c] - ms) * (sr / ss) + mr) * a.color
    rgb = Image.fromarray(np.clip(sy, 0, 255).astype(np.uint8), "YCbCr").convert("RGB")
    src = Image.merge("RGBA", (*rgb.split(), src.split()[3]))
if a.fade > 0:  # 生成画像の端で切れた雲・光: 切り抜いた絵の外周から a.fade px で薄める
    o = np.array(src).astype(float); hh, ww = o.shape[:2]
    yy, xx = np.mgrid[:hh, :ww]
    d = np.minimum(np.minimum(xx, ww - 1 - xx), yy)  # 下端(足元)は薄めない
    o[:, :, 3] *= np.clip(d / a.fade, 0, 1)
    src = Image.fromarray(o.astype(np.uint8), "RGBA")
if a.grow:
    padX = int(np.ceil(max(0, -x, x + src.width - RW)))
    padT = int(np.ceil(max(0, -y)))
    nW = RW + 2 * padX; nH = RH + padT
    nW += (-nW) % 4; nH += (-nH) % 4
    ox_ = (nW - RW) // 2; oy_ = nH - RH
    out = Image.new("RGBA", (nW, nH), (0, 0, 0, 0)); x += ox_; y += oy_; RW, RH = nW, nH
out.alpha_composite(src, (int(round(x)), int(round(y))))

cut = out.getbbox()
clipped = cut is None or src.width > RW or src.height > RH or x < 0 or y < 0 or x + src.width > RW or y + src.height > RH
out.save(a.out)
print(f"fit: scale {s:.3f} size {src.width}x{src.height} at ({x:.0f},{y:.0f}) in {RW}x{RH}{'  WARNING: clipped by the canvas' if clipped else ''}")

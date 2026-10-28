# エフェクトの重ね方 第2版(2026-10-28、マスターの「キャラ本人が主役としてはっきり見える仕上がりに」)
#  python fxmerge2.py <キャラ(透明背景, 700x1100)> <エステルのエフェクト入り(1024x1536)> <出力.png> [--gain 1.0] [--near 26] [--head 1.6]
#  fxmerge.py との違い:
#  ・キャラの中には一切描かない(fxmerge は刃の光などを max で足していた → 体や武器の形が見分けにくくなっていた)
#    = 体を横切っていた光の帯や粒は、体の後ろを通るように見える
#  ・輪郭のすぐ外は薄く、離れるほど元の強さ(--near px で元の強さに戻る)。輪郭の線が光に溶けない
#  ・顔の周り(人物の上 22% の高さ)は離す距離を --head 倍にして、さらに濃さを 0.7 倍
#  ・離れた所の光は少しだけ強める(派手さは外側で出す)。--gain で全体の濃さ
#  ・位置合わせは fxmerge と同じ(黒背景に置いた元の絵を作り直して、縮尺と位置を探す)
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage

ap = argparse.ArgumentParser()
ap.add_argument("char"); ap.add_argument("fx"); ap.add_argument("out")
ap.add_argument("--gain", type=float, default=1.0)
ap.add_argument("--near", type=float, default=26.0)
ap.add_argument("--head", type=float, default=1.6)
ap.add_argument("--clear", type=float, default=3.0)   # 輪郭からこの距離までは完全に消す
ap.add_argument("--far", type=float, default=1.12)    # 離れた所の強め
ap.add_argument("--despill", default="none")          # g / m / b: 縁に残った緑/マゼンタ/青の色かぶりを取る
ap.add_argument("--despillAll", action="store_true")
ap.add_argument("--cap", type=float, default=1.0)     # エフェクトの不透明度の上限(画面いっぱいの光を少し透かす)
ap.add_argument("--halo", type=float, default=0.0)    # 輪郭の外の暗い幕の濃さ(0 = なし)
ap.add_argument("--haloW", type=float, default=14.0)  # その幅(px)
a = ap.parse_args()

ch = Image.open(a.char).convert("RGBA")
W, H = ch.size
fx = Image.open(a.fx).convert("RGB")
FW, FH = fx.size
# 黒背景に置いた元(fxmerge と同じ置き方: 高さを合わせて左右中央)
s = FH / H; nw = int(W * s); ox = (FW - nw) // 2
src = Image.new("RGB", (FW, FH))
src.paste(ch.resize((nw, FH), Image.LANCZOS), (ox, 0), ch.resize((nw, FH), Image.LANCZOS))

def _gray(im, sc): return np.array(im.convert("L").resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))))).astype(float)
def _warp(im, k, dx, dy):
    cx, cy = im.width / 2, im.height / 2
    return im.transform(im.size, Image.AFFINE, (k, 0, cx - k * cx + dx, 0, k, cy - k * cy + dy), Image.BICUBIC)
def _search(scale, ks, dxs, dys, best):
    fs = fx.resize((max(1, int(fx.width * scale)), max(1, int(fx.height * scale))))
    g0 = _gray(src, scale); m = g0 > 25
    for k in ks:
        for dx in dxs:
            for dy in dys:
                g = np.array(_warp(fs, k, dx * scale, dy * scale).convert("L")).astype(float)
                e = np.abs(g - g0)[m].mean()
                if e < best[0]: best = (e, k, dx, dy)
    return best
best = (1e9, 1.0, 0.0, 0.0)
best = _search(0.0625, np.arange(0.88, 1.1201, 0.02), range(-160, 161, 16), range(-160, 161, 16), best)
_, k0, x0, y0 = best
best = _search(0.125, np.arange(k0 - 0.02, k0 + 0.0201, 0.01), np.arange(x0 - 16, x0 + 16.1, 8), np.arange(y0 - 16, y0 + 16.1, 8), best)
_, k0, x0, y0 = best
best = _search(0.25, np.arange(k0 - 0.01, k0 + 0.0101, 0.0025), np.arange(x0 - 8, x0 + 8.1, 2), np.arange(y0 - 8, y0 + 8.1, 2), best)
_, k0, x0, y0 = best
best = _search(0.5, np.arange(k0 - 0.0025, k0 + 0.00251, 0.00125), np.arange(x0 - 2, x0 + 2.1, 1), np.arange(y0 - 2, y0 + 2.1, 1), best)
fx = _warp(fx, best[1], best[2], best[3])
print(f"align: scale {best[1]:.4f} shift ({best[2]:.1f},{best[3]:.1f}) err {best[0]:.1f}")

fxs = np.array(fx.crop((ox, 0, ox + nw, FH)).resize((W, H), Image.LANCZOS)).astype(float)
C = np.array(ch).astype(float)
al = C[:, :, 3] / 255.0
# 縁の色かぶり(切り抜いた時の背景色の照り返し)を、輪郭の内側 4px の帯だけで抑える
if a.despill != "none":
    band = (al > 0.02) & ~ndimage.binary_erosion(al > 0.5, iterations=4)
    if a.despillAll: band = al > 0.02          # 緑/マゼンタを使わない衣装なら絵全体(細い髪の毛先は帯より内側まで色かぶりしている)
    for mode in a.despill.split("+"):            # 例: g+olive / m+b
        r, g, b = C[:, :, 0].copy(), C[:, :, 1].copy(), C[:, :, 2].copy()
        if mode == "g":
            lim = np.maximum(r, b); C[:, :, 1] = np.where(band & (g > lim), lim, g)
        elif mode == "m":
            over = np.clip(np.minimum(r, b) - g, 0, None) * band
            C[:, :, 0] -= over; C[:, :, 2] -= over
        elif mode == "b":
            lim = np.maximum(r, g); C[:, :, 2] = np.where(band & (b > lim), lim, b)
        elif mode == "olive":                     # 髪の線に残った暗い黄緑(緑の照り返し)を髪の色(赤み)へ。明るい金髪や白は触らない
            lum = r * 0.3 + g * 0.59 + b * 0.11
            olive = band & (g > r * 0.9) & (b < g * 0.8) & (lum < 170)
            C[:, :, 1] = np.where(olive, np.minimum(g, r * 0.85), g)
body = al > 0.35
dist = ndimage.distance_transform_edt(~body)          # 輪郭からの距離(外側)
bb = ch.getbbox(); headY = int(bb[1] + (bb[3] - bb[1]) * 0.22)
# 顔の周り: 頭の高さの中の人物の画素からの距離で、離す距離を広げる
headMask = body.copy(); headMask[headY:, :] = False
dHead = ndimage.distance_transform_edt(~headMask) if headMask.any() else np.full(body.shape, 1e9)

def ramp(d, lo, hi): return np.clip((d - lo) / max(1e-3, hi - lo), 0, 1)
w = ramp(dist, a.clear, a.near) ** 1.3
w = w * (1 + (a.far - 1) * ramp(dist, a.near * 1.5, a.near * 4))
hr = ramp(dHead, a.clear * a.head, a.near * a.head) ** 1.3
hf = 0.7 + 0.3 * ramp(dHead, a.near * a.head, a.near * a.head * 2.5)
w = np.where(dHead < a.near * a.head * 2.5, np.minimum(w, hr * hf), w)
# エフェクトの取り出し(黒からの明るさ → 不透明度)
lum = fxs.max(-1) / 255.0
ea = np.clip((lum - 0.04) * 1.15, 0, 1)
ecol = np.where(ea[..., None] > 1e-3, np.clip(fxs / np.maximum(ea[..., None], 1e-3), 0, 255), 0)
ea = np.clip(ea * w * a.gain, 0, a.cap) * np.clip(1 - al * 1.0, 0, 1)
# 縁の影(--halo): 輪郭のすぐ外に暗い幕を敷き、同じ色のエフェクト(赤い体と赤い炎など)から輪郭を浮かせる
if a.halo > 0:
    ha = a.halo * (1 - ramp(dist, 1.0, a.haloW)) * (dist > 0)
    shade = np.array([10.0, 10.0, 16.0])
    ecol = (ecol * ea[..., None] + shade * ha[..., None] * (1 - ea[..., None])) / np.maximum((ea + ha * (1 - ea))[..., None], 1e-4)
    ea = ea + ha * (1 - ea)

rgb = C[:, :, :3] * al[..., None] + ecol * ea[..., None] * (1 - al[..., None])
A = al + ea * (1 - al)
rgb = np.where(A[..., None] > 1e-4, rgb / np.maximum(A[..., None], 1e-4), 0)
res = np.dstack([np.clip(rgb, 0, 255), np.clip(A * 255, 0, 255)]).astype(np.uint8)
res[:, :4, 3] = 0; res[:, -4:, 3] = 0; res[:4, :, 3] = 0; res[-4:, :, 3] = 0
Image.fromarray(res, "RGBA").save(a.out)
print(f"merged: head above y={headY}, effect coverage {float((ea > 0.1).mean()) * 100:.1f}%")

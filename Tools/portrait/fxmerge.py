# エステルが描き足したエフェクトを、キャラの画素を変えずに重ねる(2026-10-28)
#  python fxmerge.py <キャラ(透明背景, 700x1100)> <黒背景に置いた元(1024x1536)> <エステルのエフェクト入り(1024x1536)> <出力.png>
#  ・黒背景の元とエフェクト入りは同じ構図(位置ずれ 0 を確かめてから使う)
#  ・キャラの外: 黒からの明るさを不透明度にして色を取り出す(光・炎・粒だけが残る)
#  ・キャラの中: 元の画素と、エフェクト入りの明るい方(刃の光など)。顔(人物の上 22%)は元の画素のまま
import sys
import numpy as np
from PIL import Image

ch_p, src_p, fx_p, out_p = sys.argv[1:5]
ch = Image.open(ch_p).convert("RGBA")
W, H = ch.size
src = Image.open(src_p).convert("RGB"); fx = Image.open(fx_p).convert("RGB")
# 位置合わせ: 描き足した絵が少し拡大/ずれている時がある → 縮尺と位置を探して元に合わせる(低解像度で粗く → 細かく)
def _gray(im, s): return np.array(im.convert("L").resize((max(1, int(im.width * s)), max(1, int(im.height * s))))).astype(float)
def _warp(im, k, dx, dy):  # 元の座標 (x,y) ← 描き足した絵の (cx + (x-cx)*k + dx, ...)
    cx, cy = im.width / 2, im.height / 2
    return im.transform(im.size, Image.AFFINE, (k, 0, cx - k * cx + dx, 0, k, cy - k * cy + dy), Image.BICUBIC)
# 誤差はキャラの所(元の絵で明るい所)だけで比べる(描き足したエフェクトで外側は変わるため)
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
_, k0, x0_, y0_ = best
best = _search(0.125, np.arange(k0 - 0.02, k0 + 0.0201, 0.01), np.arange(x0_ - 16, x0_ + 16.1, 8), np.arange(y0_ - 16, y0_ + 16.1, 8), best)
_, k0, x0_, y0_ = best
best = _search(0.25, np.arange(k0 - 0.01, k0 + 0.0101, 0.0025), np.arange(x0_ - 8, x0_ + 8.1, 2), np.arange(y0_ - 8, y0_ + 8.1, 2), best)
_, k0, x0_, y0_ = best
best = _search(0.5, np.arange(k0 - 0.0025, k0 + 0.00251, 0.00125), np.arange(x0_ - 2, x0_ + 2.1, 1), np.arange(y0_ - 2, y0_ + 2.1, 1), best)
fx = _warp(fx, best[1], best[2], best[3])
print(f"align: scale {best[1]:.4f} shift ({best[2]:.1f},{best[3]:.1f}) err {best[0]:.1f}")
# 黒背景の元に置いた時と同じ位置・大きさで 700x1100 の座標へ戻す
sw, sh = src.size
s = sh / H; nw = int(W * s); ox = (sw - nw) // 2
fxs = np.array(fx.crop((ox, 0, ox + nw, sh)).resize((W, H), Image.LANCZOS)).astype(float)
srs = np.array(src.crop((ox, 0, ox + nw, sh)).resize((W, H), Image.LANCZOS)).astype(float)
err = np.abs(fxs - srs).mean()
C = np.array(ch).astype(float)
a = C[:, :, 3] / 255.0
bb = ch.getbbox(); faceY = int(bb[1] + (bb[3] - bb[1]) * 0.22)

# キャラの外のエフェクト(黒からの明るさ → 不透明度)
lum = fxs.max(-1) / 255.0
ea = np.clip((lum - 0.04) * 1.15, 0, 1)
ecol = np.where(ea[..., None] > 1e-3, np.clip(fxs / np.maximum(ea[..., None], 1e-3), 0, 255), 0)
# 元の絵にあった部分(黒背景の元が明るい所 = キャラ)はエフェクトとして数えない
srcl = srs.max(-1) / 255.0
ea = ea * np.clip(1 - a * 1.0, 0, 1)

out = np.zeros((H, W, 4))
out[:, :, :3] = ecol; out[:, :, 3] = ea
# キャラ: 元の画素と、エフェクト入りの明るい方(顔は元のまま)
inner = np.maximum(C[:, :, :3], fxs)
yy = np.arange(H)[:, None, None]
inner = np.where(yy < faceY, C[:, :, :3], inner)
rgb = inner * a[..., None] + out[:, :, :3] * out[:, :, 3:4] * (1 - a[..., None])
al = a + out[:, :, 3] * (1 - a)
rgb = np.where(al[..., None] > 1e-4, rgb / np.maximum(al[..., None], 1e-4), 0)
res = np.dstack([np.clip(rgb, 0, 255), np.clip(al * 255, 0, 255)]).astype(np.uint8)
res[:, :4, 3] = 0; res[:, -4:, 3] = 0
Image.fromarray(res, "RGBA").save(out_p)
print(f"merged: mean diff src/fx {err:.1f} (small = aligned), face kept above y={faceY}")

# 背景の旅の素材(2026-11-03): エステルの緑背景(#00FF00)の絵 → 透過 PNG(余白を切り、幅と高さを4の倍数に)
#  python key.py <in.png> <out.png> [--soft 0.18] [--pad 8] [--maxw 0] [--unmix 0]
#  既定: 緑の度合いで透明度(くっきりした縁の絵: 城・門)。縁の緑かぶりは g を r,b の大きい方まで下げる
#  --unmix 1: 縁をぼかした絵(洞窟の結晶の空洞など)。観測 = α·元の色 + (1-α)·緑 として α = 1 - (g - max(r,b)) と元の色を戻す
import sys, numpy as np
from PIL import Image
a = sys.argv[1:]
src, dst = a[0], a[1]
opt = {k: float(v) for k, v in zip(a[2::2], a[3::2])}
soft = opt.get('--soft', 0.18); pad = int(opt.get('--pad', 8)); maxw = int(opt.get('--maxw', 0)); unmix = opt.get('--unmix', 0) > 0
im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255.0
r, g, b = im[..., 0], im[..., 1], im[..., 2]
spill = g - np.maximum(r, b)
if unmix:
    alpha = np.clip(1.0 - spill, 0, 1)
    alpha = np.where(spill > 0.85, 0.0, alpha)            # ほぼ純粋な緑は完全に透明
    al3 = np.clip(alpha, 0.04, 1)
    r = np.clip(r / al3, 0, 1); b = np.clip(b / al3, 0, 1); g = np.clip((g - (1 - alpha)) / al3, 0, 1)
    # 縁のぼかし(半透明)は色を抜いて暗い灰色のもやにする(緑の残りが青緑のもやに見えるため)
    s = np.clip((alpha - 0.55) / 0.4, 0, 1); lum = (r + np.minimum(g, np.maximum(r, b)) + b) / 3.0
    r, g, b = lum + (r - lum) * s, lum + (g - lum) * s, lum + (b - lum) * s
else:
    alpha = 1.0 - np.clip((spill - 0.08) / soft, 0, 1)
g2 = np.minimum(g, np.maximum(r, b) + 0.02)               # 残った緑かぶりを除く
out = np.dstack([r, g2, b, alpha])
o = Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), 'RGBA')
bb = o.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
if bb:
    x0, y0, x1, y1 = bb
    o = o.crop((max(0, x0 - pad), max(0, y0 - pad), min(o.width, x1 + pad), min(o.height, y1 + pad)))
if maxw and o.width > maxw:
    o = o.resize((maxw, round(o.height * maxw / o.width)), Image.LANCZOS)
# 幅と高さを4の倍数へ(透明な画素を足す。Android ASTC / Windows BC7 の圧縮のため)
W4, H4 = (o.width + 3) // 4 * 4, (o.height + 3) // 4 * 4
if (W4, H4) != o.size:
    p4 = Image.new("RGBA", (W4, H4), (0, 0, 0, 0)); p4.paste(o, ((W4 - o.width) // 2, H4 - o.height)); o = p4
o.save(dst)
print(dst, o.size)

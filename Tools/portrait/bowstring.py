# 弓使いの立ち絵に弦を描く(弓の上端 → 矢筈(引き手) → 弓の下端)。弦は人物の後ろを通す(顔・体を横切らない)
#  4倍で描いて縮める(なめらかな線)
import sys
from PIL import Image, ImageDraw
src, out = sys.argv[1], sys.argv[2]
top, nock, bot = (411, 219), (281, 521), (642, 707)
im = Image.open(src).convert("RGBA"); W, H = im.size; S = 4
lay = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
for p, q in ((top, nock), (nock, bot)):
    d.line([(p[0] * S, p[1] * S), (q[0] * S, q[1] * S)], fill=(30, 24, 20, 190), width=int(2.6 * S))   # 縁
    d.line([(p[0] * S, p[1] * S), (q[0] * S, q[1] * S)], fill=(226, 214, 186, 240), width=int(1.3 * S))  # 弦
lay = lay.resize((W, H), Image.LANCZOS)
lay.alpha_composite(im)     # 人物を弦の上に
lay.save(out)

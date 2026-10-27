"""エステルのキャラカードの絵(カードの外はマゼンタ)を切り抜いて、ほかのキャラと同じ高さのカード <id>_card.png にする(2026-10-27、新キャラ)。
  python card.py <元の画像> <出力の png> [高さ=959]
幅・高さは 4 の倍数(圧縮が効く)。"""
import sys
from PIL import Image
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import frames

src, dst = sys.argv[1], sys.argv[2]
H = int(sys.argv[3]) if len(sys.argv) > 3 else 959
c = frames.cutout(src)
w = round(c.width * H / c.height)
c = c.resize((w, H), Image.LANCZOS)
W4, H4 = (w + 3) // 4 * 4, (H + 3) // 4 * 4
out = Image.new('RGBA', (W4, H4), (0, 0, 0, 0))
out.alpha_composite(c, ((W4 - w) // 2, (H4 - H) // 2))
out.save(dst)
print(dst, out.size)

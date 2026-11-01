# -*- coding: utf-8 -*-
# 操作説明ショート(2026-11-01): 指とフリックの矢印の絵(撮った映像に後から重ねる。ゲームには描かない)
#  python gesture.py <出力フォルダ>  → flick_<dir>_<k>.png(k = 0..N-1 のコマ。指が矢印の向きへ動き、軌跡が伸びる)
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

W = H = 520          # 1枚の大きさ(1080x1920 の画面の下の方に置く)
N = 18               # コマ数(60fps で 0.3 秒の動き。その後は最後のコマを少し保つ)
DIRS = {"forward": (1, 0), "backward": (-1, 0), "up": (0, -1), "down": (0, 1)}
GOLD = (240, 200, 110, 255)

def arrow(d, dx, dy, length, alpha):
    cx, cy = W / 2, H / 2
    x0, y0 = cx - dx * length / 2, cy - dy * length / 2
    x1, y1 = cx + dx * length / 2, cy + dy * length / 2
    a = int(255 * alpha)
    # 影 → 金の線 → 矢じり
    for w, col in ((34, (0, 0, 0, int(a * 0.55))), (20, (GOLD[0], GOLD[1], GOLD[2], a))):
        d.line([(x0, y0), (x1, y1)], fill=col, width=w)
        hx, hy = x1 + dx * 26, y1 + dy * 26
        px, py = -dy, dx
        d.polygon([(hx, hy), (x1 - dx * 30 + px * 52, y1 - dy * 30 + py * 52), (x1 - dx * 30 - px * 52, y1 - dy * 30 - py * 52)], fill=col)

def finger(img, x, y, press):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    r = 46
    d.ellipse([x - r - 8, y - r - 8, x + r + 8, y + r + 8], fill=(0, 0, 0, 110))
    d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, 235))
    if press > 0:
        rr = r + 30 * press
        d.ellipse([x - rr, y - rr, x + rr, y + rr], outline=(255, 255, 255, int(200 * (1 - press))), width=6)
    img.alpha_composite(lay)

def main():
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for name, (dx, dy) in DIRS.items():
        for k in range(N):
            t = k / (N - 1)
            e = 1 - (1 - t) ** 3  # はじめ速く
            img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
            L = 300
            arrow(d, dx, dy, L * max(0.15, e), 0.35 + 0.65 * e)
            sx, sy = W / 2 - dx * L / 2, H / 2 - dy * L / 2
            finger(img, sx + dx * L * 0.62 * e, sy + dy * L * 0.62 * e, t)  # 指は矢印の 6 割まで(矢じりを隠さない)
            img.save(os.path.join(out, f"flick_{name}_{k:02d}.png"))
    print("ok", len(DIRS) * N)

if __name__ == "__main__":
    main()

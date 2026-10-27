"""コマを切り抜いて(マゼンタの背景)、同じ地面の線の上に並べた確認用の一覧を作る(2026-10-27、新キャラのアニメーション)。

  python sheet.py <out.jpg> <frame1.png> <frame2.png> ...

切り抜きは cut.py と同じ考え方(マゼンタらしさで透明に)。並べる時は縮小の倍率をそろえ(全コマ同じ倍率)、足元を下の線に合わせる。
"""
import sys
import numpy as np
from PIL import Image, ImageDraw


def cutout(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mag = np.minimum(r, b) - g
    alpha = np.clip(1.0 - (mag - 60) / 120.0, 0, 1)
    out = a.copy()
    out[..., 0] = np.where(alpha < 1, np.minimum(r, g + (r - g) * alpha), r)
    out[..., 2] = np.where(alpha < 1, np.minimum(b, g + (b - g) * alpha), b)
    im = Image.fromarray(np.dstack([out, alpha * 255]).clip(0, 255).astype(np.uint8), 'RGBA')
    bb = im.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    return im.crop(bb), bb


def main():
    out, files = sys.argv[1], sys.argv[2:]
    cuts = [cutout(f) for f in files]
    s = 0.3
    cw = int(max(c.width for c, _ in cuts) * s) + 20
    ch = int(max(c.height for c, _ in cuts) * s) + 40
    sheet = Image.new('RGB', (cw * len(cuts), ch), (200, 205, 215))
    d = ImageDraw.Draw(sheet)
    for i, (c, bb) in enumerate(cuts):
        t = c.resize((int(c.width * s), int(c.height * s)), Image.LANCZOS)
        x = i * cw + (cw - t.width) // 2
        y = ch - 20 - t.height
        sheet.paste(t, (x, y), t)
        d.line((i * cw, ch - 20, (i + 1) * cw, ch - 20), fill=(90, 90, 90))
        d.text((i * cw + 4, 4), f"{i + 1}: {c.width}x{c.height}", fill=(0, 0, 0))
    sheet.save(out, quality=88)
    print(out, [(c.width, c.height) for c, _ in cuts])


if __name__ == '__main__':
    main()

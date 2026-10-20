# ctrlicons.py <outdir> : 操作の記号(白銀)を uGUI 用の画像にする。UiIcons.DrawPause/DrawClose と同じ色・太さの割合・影
#   chevron.png(右向きの「>」、左は反転して使う) / plus.png(「+」)  128x128、角の丸い線、右下へ薄い影
import sys, os
from PIL import Image, ImageDraw, ImageFilter
N, SS = 128, 4
COL = (237, 242, 255, 255)
def stroke(lines, w):
    big = Image.new('L', (N * SS, N * SS), 0); d = ImageDraw.Draw(big)
    for (x0, y0, x1, y1) in lines:
        d.line([(x0 * SS, y0 * SS), (x1 * SS, y1 * SS)], fill=255, width=int(w * SS))
        for (x, y) in ((x0, y0), (x1, y1)): r = w * SS / 2; d.ellipse([x * SS - r, y * SS - r, x * SS + r, y * SS + r], fill=255)
    return big.resize((N, N), Image.LANCZOS)
def compose(mask, out):
    im = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    sh = Image.new('RGBA', (N, N), (0, 0, 0, 0)); sh.putalpha(mask.point(lambda v: int(v * 0.45)))
    sh = sh.filter(ImageFilter.GaussianBlur(1.2)); im.alpha_composite(sh, (3, 4))
    fg = Image.new('RGBA', (N, N), COL); fg.putalpha(mask); im.alpha_composite(fg)
    im.save(out)
o = sys.argv[1]
s = 0.44 * N; w = s * 0.2   # DrawClose と同じ割合(長さ = 44%、太さ = 長さの20%)
c = N / 2
compose(stroke([(c - s * 0.22, c - s * 0.5, c + s * 0.28, c), (c + s * 0.28, c, c - s * 0.22, c + s * 0.5)], w * 1.05), os.path.join(o, 'chevron.png'))
compose(stroke([(c - s * 0.5, c, c + s * 0.5, c), (c, c - s * 0.5, c, c + s * 0.5)], w), os.path.join(o, 'plus.png'))
print('ok')

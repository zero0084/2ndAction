"""走りのコマを並べたループ GIF(2026-10-25 布表現の見直し)。足元(ピボット)をそろえて、元と修正版を横に並べる。
usage: clothgif.py <出力.gif> <fps> <scale> --set <ラベル>=<コマのフォルダ> [--set ...] [--pivots <ピボットを読むフォルダ>]
  コマ = フォルダの run_*.png。ピボットは同じ名前の .meta(spritePivot)から読む。無ければ --pivots のフォルダの .meta を使う
  scale: 2 = 拡大表示、0.285 = ゲーム内(1080p で高さ約62px、PPU 186 の走り)
"""
import sys, os, re, glob
from PIL import Image, ImageDraw, ImageFont

BG = (110, 120, 130)
args = sys.argv[1:]
out, fps, scale = args[0], float(args[1]), float(args[2])
sets, pivdir = [], None
i = 3
while i < len(args):
    if args[i] == '--set': lab, d = args[i + 1].split('=', 1); sets.append((lab, d)); i += 2
    elif args[i] == '--pivots': pivdir = args[i + 1]; i += 2
    else: raise SystemExit('unknown ' + args[i])

def pivot(path):
    for p in (path + '.meta', os.path.join(pivdir or '', os.path.basename(path)) + '.meta'):
        if os.path.exists(p):
            m = re.search(r'spritePivot: \{x: ([\d.]+), y: ([\d.]+)\}', open(p, encoding='utf-8').read())
            if m: return float(m.group(1)), float(m.group(2))
    raise SystemExit('no pivot for ' + path)

cols = []
for lab, d in sets:
    fr = []
    for f in sorted(glob.glob(os.path.join(d, 'run_*.png'))):
        im = Image.open(f).convert('RGBA'); px, py = pivot(f)
        im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
        fx, fy = px * im.width, (1 - py) * im.height  # ピボットの位置(左上から)
        bb = im.getbbox() or (0, 0, 1, 1)
        fr.append((im.crop(bb), fx - bb[0], fy - bb[1]))  # 透明な余白を切って、ピボットの位置も合わせる
    cols.append((lab, fr))
n = len(cols[0][1])
# ピボットを原点にした時の左右上下の広がり
L = max(fx for _, fr in cols for im, fx, fy in fr) + 6
R = max(im.width - fx for _, fr in cols for im, fx, fy in fr) + 6
U = max(fy for _, fr in cols for im, fx, fy in fr) + 6
D = max(im.height - fy for _, fr in cols for im, fx, fy in fr) + 6
cw, ch = int(L + R), int(U + D)
small = scale < 1
fs = ImageFont.truetype('C:/Windows/Fonts/meiryo.ttc', 13 if small else 20)
head = 22 if small else 32
cw = max(cw, 90 if small else 0)
frames = []
for k in range(n):
    S = Image.new('RGB', (len(cols) * (cw + 10) + 10, ch + head + 6), (40, 44, 52)); d = ImageDraw.Draw(S)
    for c, (lab, fr) in enumerate(cols):
        x0 = 10 + c * (cw + 10)
        cell = Image.new('RGBA', (cw, ch), BG + (255,))
        im, fx, fy = fr[k]
        ox = int((cw - (L + R)) / 2 + L)
        cell.alpha_composite(im, (int(round(ox - fx)), int(round(U - fy))))
        cd = ImageDraw.Draw(cell); cd.line([(0, int(U)), (cw, int(U))], fill=(150, 160, 90))
        S.paste(cell.convert('RGB'), (x0, head)); d.text((x0, 2), f'{lab}  {k + 1}/{n}' if not small else lab, fill='white', font=fs)
    frames.append(S)
frames[0].save(out, save_all=True, append_images=frames[1:], duration=int(1000 / fps), loop=0, optimize=False)
print('ok', out, frames[0].size)

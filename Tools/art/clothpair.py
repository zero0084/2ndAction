"""元のコマと合成したコマを左右に並べて1枚に(確認用、2026-10-26)。usage: clothpair.py <id> <out.jpg> <folder/name> ... [--s 1.0]"""
import sys
from PIL import Image
a = sys.argv[1:]; s = float(a[a.index('--s') + 1]) if '--s' in a else 1.0
if '--s' in a: i = a.index('--s'); a = a[:i] + a[i + 2:]
id, outp, fns = a[0], a[1], a[2:]; rows = []
for fn in fns:
    o = Image.open(f'Docs/ClothRevision_2026-10-25/ingame/original/Art/{fn}.png').convert('RGBA')
    n = Image.open(f'Docs/ClothRevision_2026-10-25/chars/{id}/local/{fn}.png').convert('RGBA')
    M = Image.new('RGBA', (o.width * 2 + 8, o.height), (30, 30, 30, 255))
    for k, im in enumerate((o, n)):
        bg = Image.new('RGBA', im.size, (120, 130, 140, 255)); bg.alpha_composite(im); M.paste(bg, (k * (o.width + 8), 0))
    rows.append(M.convert('RGB'))
W = max(r.width for r in rows); H = sum(r.height + 6 for r in rows); X = Image.new('RGB', (W, H), (30, 30, 30)); y = 0
for r in rows: X.paste(r, (0, y)); y += r.height + 6
X.resize((int(W * s), int(H * s))).save(outp, quality=88); print(outp, X.size)

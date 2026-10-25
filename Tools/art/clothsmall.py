"""エステルの出力(out/、portrait*/raw_*.png、card/out/、sheets/*_out.png)を縮小JPEGで out_small/ へ(コミット用の記録)。usage: clothsmall.py <chars/id>"""
import sys, os, glob
from PIL import Image
d = sys.argv[1]; o = os.path.join(d, 'out_small'); os.makedirs(o, exist_ok=True); n = 0
for f in glob.glob(os.path.join(d, 'out', '*.png')) + glob.glob(os.path.join(d, 'portrait*', 'raw_*.png')) + glob.glob(os.path.join(d, 'card', 'out', '*.png')) + glob.glob(os.path.join(d, 'sheets', '*_out.png')):
    name = os.path.relpath(f, d).replace(os.sep, '__').replace('.png', '.jpg'); dst = os.path.join(o, name)
    if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(f): continue
    im = Image.open(f).convert('RGB'); im.thumbnail((900, 900)); im.save(dst, quality=82); n += 1
print(d, n, 'new')

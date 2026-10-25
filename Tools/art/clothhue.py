"""合成したコマの布の色(色相・彩度・明度の中央値)を並べ、キャラ全体の中央値から外れたコマに印(2026-10-25 布表現の見直し)。
エステルが1コマずつ描き直すと、布の緑が黄緑/青緑に振れたり刺繍が金/赤に変わったりして、動作の中でちらつくため。
usage: clothhue.py <id> [dh=12]   (布の色は chars/<id>.txt の CLOTH_SPEC)"""
import sys, os, glob
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask
id = sys.argv[1]; dh = float(sys.argv[2]) if len(sys.argv) > 2 else 12
W = f'Docs/ClothRevision_2026-10-25/chars/{id}'
spec = [l.split('=', 1)[1].strip() for l in open(W + '.txt', encoding='utf-8') if l.startswith('CLOTH_SPEC=')][0]
rows = []
for f in sorted(glob.glob(W + '/local/**/*.png', recursive=True)):
    im = np.array(Image.open(f).convert('RGBA')).astype(float); m = clothmask(im[..., :3], spec) & (im[..., 3] > 200)
    if m.sum() < 200: continue
    from colorsys import rgb_to_hsv
    px = im[..., :3][m] / 255.0; px = px[np.random.default_rng(0).choice(len(px), min(4000, len(px)), replace=False)]
    hsv = np.array([rgb_to_hsv(*p) for p in px]); h = np.degrees(np.angle(np.mean(np.exp(1j * 2 * np.pi * hsv[:, 0])))) % 360
    rows.append((os.path.relpath(f, W + '/local').replace(chr(92), '/'), h, np.median(hsv[:, 1]), np.median(hsv[:, 2]), int(m.sum())))
H = np.median([r[1] for r in rows]); S = np.median([r[2] for r in rows]); V = np.median([r[3] for r in rows])
print(f'median h={H:.0f} s={S:.2f} v={V:.2f}')
bad = []
for r in rows:
    d = (r[1] - H + 180) % 360 - 180; flag = abs(d) > dh or abs(r[2] - S) > 0.15 or abs(r[3] - V) > 0.15
    if flag: bad.append(r[0])
    print(('* ' if flag else '  ') + f'{r[0]:50s} h={r[1]:5.0f} ({d:+.0f}) s={r[2]:.2f} v={r[3]:.2f} px={r[4]}')
print('OFF', len(bad), ' '.join(b[:-4] for b in bad))

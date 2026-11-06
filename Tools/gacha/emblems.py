# ガチャの土地の紋 5つ + 粒子(羽・古代文字 3つ)を切り出す(2026-11-06、エステルの絵 gs_emblems_v1.png、緑の背景)
import sys, os
import numpy as np
from PIL import Image
src = sys.argv[1]; out = sys.argv[2]
a = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
R, G, B = a[..., 0], a[..., 1], a[..., 2]
key = G - np.maximum(R, B)
alpha = np.clip(1.0 - (key - 40) / 70.0, 0, 1)
G2 = np.minimum(G, np.maximum(R, B))
cut = Image.fromarray(np.concatenate([np.stack([R, G2, B], -1), (alpha * 255)[..., None]], -1).astype(np.uint8), 'RGBA')
def save(box, name, size):
    p = cut.crop(box); bb = p.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox(); p = p.crop(bb)
    s = size / max(p.width, p.height); p = p.resize((max(1, round(p.width * s)), max(1, round(p.height * s))), Image.LANCZOS)
    c = Image.new('RGBA', (size, size)); c.alpha_composite(p, ((size - p.width) // 2, (size - p.height) // 2)); c.save(os.path.join(out, name + '.png')); print(name, bb)
xs = [40, 335, 625, 920, 1210, 1505]
for i, k in enumerate(['common', 'wilderness', 'cave', 'sky', 'lastdungeon']):
    save((xs[i], 190, xs[i + 1], 510), 'emblem_' + k, 128)
save((120, 590, 440, 870), 'feather', 128)
for i, (x0, x1) in enumerate([(530, 730), (850, 1050), (1150, 1380)]):
    save((x0, 600, x1, 860), 'rune%d' % i, 64)

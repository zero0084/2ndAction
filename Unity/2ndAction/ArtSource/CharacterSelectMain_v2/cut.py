"""マゼンタ背景の立ち絵1枚を透過にする(離れた持ち物=浮かぶ魔導書/手裏剣なども残し、小さなゴミだけ消す)。
usage: cut.py <raw_dir> <out_dir>
"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
for f in sorted(os.listdir(src)):
    if not f.endswith('.png'): continue
    im = np.array(Image.open(os.path.join(src, f)).convert('RGB')).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    m = np.minimum(r, b) - g                      # マゼンタらしさ
    alpha = np.clip((95 - m) / (95 - 35), 0, 1)
    # 色かぶり除去(縁のマゼンタを抜く)
    spill = np.clip(np.minimum(r, b) - g, 0, None) * (1 - alpha * 0.6)
    r2 = r - spill; b2 = b - spill
    solid = alpha > 0.5
    lab, n = ndimage.label(solid)
    sizes = ndimage.sum(solid, lab, range(1, n + 1))
    keep = np.zeros(n + 1, bool)
    if n:
        big = sizes.max()
        for i, sz in enumerate(sizes, 1):
            if sz >= max(400, big * 0.002): keep[i] = True
    mask = keep[lab]
    mask = ndimage.binary_dilation(mask, iterations=3)
    a = (alpha * mask * 255).astype(np.uint8)
    rgba = np.dstack([r2, g, b2]).clip(0, 255).astype(np.uint8)
    o = Image.fromarray(np.dstack([rgba, a]))
    bb = o.getbbox()
    o = o.crop(bb)
    name = f.replace('selmain_', '').replace('.png', '')
    o.save(os.path.join(out, name + '.png'))
    print(name, o.size, 'parts', int(keep.sum()))

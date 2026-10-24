# -*- coding: utf-8 -*-
# MIXGame のロゴ素材の切り出し(2026-10-24): 無地の暗い背景の画像 → 背景が透明な PNG。
# 使い方: python brand_cutout.py in.png out.png [--pad 24] [--scale 1.0]
#  ・背景色 = 画像の縁の中央値
#  ・透明にするのは「縁からつながっている背景」だけ(猫の黒い瞳や輪郭など、内側の暗い所は消さない)
#  ・境目は背景との差でなめらかに透明にし、色は背景を差し引いて戻す(暗い背景のにじみを残さない)
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

def cutout(src, pad=24, lo=10.0, hi=60.0):
    im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
    h, w, _ = im.shape
    border = np.concatenate([im[:4].reshape(-1, 3), im[-4:].reshape(-1, 3), im[:, :4].reshape(-1, 3), im[:, -4:].reshape(-1, 3)])
    bg = np.median(border, axis=0)
    dist = np.abs(im - bg).sum(axis=2)
    # 背景の候補(差が hi 未満)のうち、縁につながる所だけを背景とする
    cand = dist < hi
    lab, _ = ndimage.label(cand)
    edge_labels = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    bgmask = np.isin(lab, edge_labels[edge_labels > 0])
    a = np.ones((h, w), np.float32)
    t = np.clip((dist - lo) / (hi - lo), 0.0, 1.0)
    a[bgmask] = (t * t * (3 - 2 * t))[bgmask]
    # 色を背景から分ける: c = a*fg + (1-a)*bg → fg = (c - (1-a)*bg) / a
    safe = np.maximum(a, 1e-3)[..., None]
    fg = np.clip((im - (1 - a)[..., None] * bg) / safe, 0, 255)
    rgba = np.dstack([fg, a * 255]).astype(np.uint8)
    ys, xs = np.nonzero(a > 0.02)
    x0, x1 = max(0, xs.min() - pad), min(w, xs.max() + 1 + pad)
    y0, y1 = max(0, ys.min() - pad), min(h, ys.max() + 1 + pad)
    return Image.fromarray(rgba[y0:y1, x0:x1], 'RGBA'), tuple(bg)

if __name__ == '__main__':
    av = sys.argv[1:]
    args = [x for i, x in enumerate(av) if not x.startswith('--') and not (i > 0 and av[i - 1] in ('--pad', '--scale'))]
    pad = int(sys.argv[sys.argv.index('--pad') + 1]) if '--pad' in sys.argv else 24
    out, bg = cutout(args[0], pad)
    if '--scale' in sys.argv:
        sc = float(sys.argv[sys.argv.index('--scale') + 1])
        out = out.resize((round(out.width * sc), round(out.height * sc)), Image.LANCZOS)
    out.save(args[1])
    print(args[1], out.size, 'bg', [round(v, 1) for v in bg])
